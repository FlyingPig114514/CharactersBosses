# Recon: Card Play Execution Pipeline (sts2.dll)

**Goal:** map the card-play pipeline end-to-end so a Harmony mod can insert an **awaited
presentation delay AFTER the player commits a card but BEFORE effects (damage/powers) resolve**.

**Assembly:** `references/sts2.dll` (9,757,184 bytes)
**Tool:** `tools/ilspy-cli` (see `docs/plans/recon-tooling.md`), driven as
`dotnet tools/ilspy-cli/bin/Release/net10.0/ilspycli.dll <types|type|method|members|search> <asm> ...`

> Decompiler note: the CLI cannot resolve nested types by `FullTypeName` in the `type` command.
> Async bodies live in compiler-generated state machines; decompile them with
> `method <asm> "<OwnerType>\.<MethodName>d__N" MoveNext 1`.
> Raw dumps used for this report are in `.recon/` (scratch, not part of the repo deliverable).

---

## 0. Executive pipeline (verified call chain)

```
[UI] NMouseCardPlay / NControllerCardPlay / keyboard
      -> NCardPlay.TryPlayCard(Creature? target)                 // void, sync   (NCardPlay.cs)
         -> CardModel.TryManualPlay(Creature? target) : bool      // sync
            -> CardModel.EnqueueManualPlay(Creature? target)      // private void, sync
               -> TaskHelper.RunSafely(OnEnqueuePlayVfx(target))  // fire-and-forget VFX
               -> RunManager.Instance.ActionQueueSynchronizer
                      .RequestEnqueue(new PlayCardAction(this, target))   // <-- NETWORK BOUNDARY
                     -> ActionQueueSet.EnqueueWithoutSynchronizing(action)
                        -> ActionExecutor.ExecuteActions()  (private async Task, loop)
                           -> GameAction.Execute()          (public async Task)
                              -> PlayCardAction.ExecuteAction()   (protected override async Task)
                                 -> await CombatState.GetCreatureAsync(TargetId, 10.0)
                                 -> NCardPlayQueue.Instance?.UpdateCardBeforeExecution(this)
                                 -> await _card.SpendResources()          // energy/stars SPENT
                                 -> await _card.OnPlayWrapper(ctx, target, false, resources)
                                        |
                                        +-- await CombatManager.Instance.WaitForUnpause()
                                        +-- CardPileCmd.Add(..., PileType.Play, ...) / AddDuringManualCardPlay
                                        +-- await Cmd.CustomScaledWait(0.25f, 0.35f)
                                        +-- playCount = await GeneratePlayCount(combatState, target)
                                        +-- for (i = 0; i < playCount; i++)
                                        |     if Power  -> await PlayPowerCardFlyVfx()
                                        |     if i > 0  -> await NCard.FindOnTable(card).AnimMultiCardPlay()
                                        |     cardPlay = new CardPlay { Card, Player, Target,
                                        |                                ResultPile, Resources,
                                        |                                IsAutoPlay, PlayIndex=i, PlayCount }
                                        |     await Hook.BeforeCardPlayed(combatState, cardPlay)   <<< STATE 8
                                        |     History.CardPlayStarted(combatState, cardPlay)
                                        |     ctx2 = new BranchingPlayerChoiceContext(...)
                                        |     await ctx2.AssignTaskAndWaitForPauseOrCompletion(
                                        |              cardModel.OnPlay(ctx2, cardPlay))          <<< EFFECTS (damage/powers)
                                        |     if (Enchantment != null) await Enchantment.OnPlay(ctx, cardPlay)
                                        |     if (Affliction  != null) await Affliction.OnPlay(ctx, target)
                                        |     History.CardPlayFinished(combatState, cardPlay)
                                        |     await Hook.AfterCardPlayed(combatState, ctx, cardPlay) <<< STATE 12
                                        +-- await CombatManager.EndCardOrPotionEffect(...)
                                        +-- pile move (Discard / Exhaust / None) + GiveToAnotherPlayer
                                        +-- await CombatManager.CheckForEmptyHand(...)
                                        +-- await Cmd.CustomScaledWait(0.15f-elapsed, 0.3f-elapsed)
                                        +-- cardModel.Played?.Invoke()
```

**The exact seam you asked for is between `Hook.BeforeCardPlayed` (state 8) and
`cardModel.OnPlay(...)`.** Energy is already spent by then (inside `PlayCardAction.ExecuteAction`),
and no damage/power has been applied yet.

---

## 1. `MegaCrit.Sts2.Core.Entities.Cards.CardPlay`

`CardPlay` is a **plain immutable data record** (C# `required` members, `init`-only). It is *not*
an executor and has **no methods that play the card**. It is constructed fresh, once per
*individual repetition* of a card, inside `CardModel.OnPlayWrapper`.

Full decompilation (`.recon/CardPlay.txt`):

```csharp
namespace MegaCrit.Sts2.Core.Entities.Cards;

[RequiredMember]
public class CardPlay
{
	[RequiredMember] public CardModel Card { get; init; }
	[RequiredMember] public Player Player { get; init; }
	[RequiredMember] public Creature? Target { get; init; }
	[RequiredMember] public PileType ResultPile { get; init; }
	[RequiredMember] public ResourceInfo Resources { get; init; }
	[RequiredMember] public bool IsAutoPlay { get; init; }
	[RequiredMember] public int PlayIndex { get; init; }
	[RequiredMember] public int PlayCount { get; init; }

	public bool IsFirstInSeries => PlayIndex == 0;
	public bool IsLastInSeries  => PlayIndex == PlayCount - 1;

	[Obsolete("Constructors of types with required members are not supported in this version of your compiler.", true)]
	[CompilerFeatureRequired("RequiredMembers")]
	public CardPlay() { }
}
```

**Answers to the sub-questions:**

| Question | Answer |
|---|---|
| `CardPlay.Target` | `MegaCrit.Sts2.Core.Entities.Creatures.Creature? Target { get; init; }` — nullable, "no target" cards pass `null`. |
| `CardPlay.Card` | `MegaCrit.Sts2.Core.Models.CardModel Card { get; init; }` |
| `CardPlay.Player` | `MegaCrit.Sts2.Core.Entities.Players.Player Player { get; init; }` (owner of the card). |
| play count / replay / times played | **Yes**: `int PlayCount` (total repetitions) and `int PlayIndex` (0-based index of *this* repetition), plus `IsFirstInSeries` / `IsLastInSeries`. |

### Where `PlayCount` comes from ("双重释放"/回响-style multi-play)

```csharp
// CardModel.<GeneratePlayCount>d__340
protected async System.Threading.Tasks.Task<int> GeneratePlayCount(ICombatState combatState, Creature? target)
{
	int playCount = GetEnchantedReplayCount() + 1;
	playCount = Hook.ModifyCardPlayCount(combatState, this, playCount, target, out List<AbstractModel> modifyingModels);
	await Hook.AfterModifyingCardPlayCount(combatState, this, (System.Collections.Generic.IEnumerable<AbstractModel>)modifyingModels);
	return playCount;
}
```

`Hook.ModifyCardPlayCount` is an `int`-returning fold over every combat hook listener
(`.recon/m-Hook-ModifyCardPlayCount.txt`):

```csharp
public static int ModifyCardPlayCount(ICombatState combatState, CardModel card, int playCount,
                                      Creature? target, out List<AbstractModel> modifyingModels)
{
	modifyingModels = new List<AbstractModel>();
	int num = playCount;
	foreach (AbstractModel current in IterateCombatHookListeners(combatState))
	{
		int num2 = num;
		num = current.ModifyCardPlayCount(card, target, num);
		if (num != num2) modifyingModels.Add(current);
	}
	return num;
}
```

Vetual seam for a mod: `AbstractModel.ModifyCardPlayCount(CardModel card, Creature target, int playCount) : int`.

Known vanilla multi-play powers (from `.recon/t-CardPlayTypes.txt`, types named
`*.<AfterModifyingCardPlayCount>d__N`): `BurstPower`, `DuplicationPower`, `OneTwoPunchPower`,
`SignalBoostPower`, `TagTeamPower`.

The repetition loop itself (`CardModel.<OnPlayWrapper>d__339`, IL_08f1 / IL_051e):

```csharp
IL_08f1:
if (<i>5__11 < <playCount>5__4 && !CombatManager.Instance.IsOverOrEnding)
{
	cardModel.CurrentPlayIndex = <i>5__11;
	if (cardModel.Type == CardType.Power) { val19 = cardModel.PlayPowerCardFlyVfx().GetAwaiter(); /* await */ }
	else if (<i>5__11 > 0) {
		NCard nCard = NCard.FindOnTable(cardModel);
		if (nCard != null) { val18 = nCard.AnimMultiCardPlay().GetAwaiter(); /* await */ }
	}
	goto IL_051e;
}
goto end_IL_03ec;
...
IL_051e:
<cardPlay>5__12 = new CardPlay
{
	Card = cardModel, Player = cardModel.Owner, Target = target,
	ResultPile = <resultLocation>5__3.pileType, Resources = resources,
	IsAutoPlay = isAutoPlay, PlayIndex = <i>5__11, PlayCount = <playCount>5__4
};
val17 = Hook.BeforeCardPlayed(<combatState>5__2, <cardPlay>5__12).GetAwaiter();
if (!((TaskAwaiter)(ref val17)).IsCompleted) { num = (<>1__state = 8); ... return; }
goto IL_05eb;
```

> A `BeforeCardPlayed`-based delay therefore applies **once per repetition**, not once per card.
> If you want one delay per *card* command, gate on `cardPlay.IsFirstInSeries` (`PlayIndex == 0`).

---

## 2. `Hook.BeforeCardPlayed` / `Hook.AfterCardPlayed`

Both are `public static System.Threading.Tasks.Task` (real `async`, `AsyncStateMachineAttribute`).
**Honest caveat:** no vanilla call site exists other than `CardModel.<OnPlayWrapper>d__339`.
No cancellation parameter, no `bool` return — the hook cannot itself veto a play.

### `Hook.BeforeCardPlayed` — declared signature

```csharp
// .recon/m-Hook-BeforeCardPlayed.txt
[AsyncStateMachine(typeof(<BeforeCardPlayed>d__15))]
public static System.Threading.Tasks.Task BeforeCardPlayed(ICombatState combatState, CardPlay cardPlay)
```

### `Hook.BeforeCardPlayed` — full body (state machine `MoveNext`)

```csharp
// .recon/sm-BeforeCardPlayed.txt   (MegaCrit.Sts2.Core.Hooks.Hook.<BeforeCardPlayed>d__15 :: MoveNext)
private void MoveNext()
{
	int num = <>1__state;
	try
	{
		if (num != 0) { <>7__wrap1 = IterateCombatHookListeners(combatState).GetEnumerator(); }
		try
		{
			if (num != 0) goto IL_00b2;
			TaskAwaiter val = <>u__1; <>u__1 = default(TaskAwaiter); num = (<>1__state = -1);
			goto IL_0099;
			IL_0099:
			((TaskAwaiter)(ref val)).GetResult();
			<model>5__3.InvokeExecutionFinished();
			<model>5__3 = null;
			goto IL_00b2;
			IL_00b2:
			if (((System.Collections.IEnumerator)<>7__wrap1).MoveNext())
			{
				<model>5__3 = <>7__wrap1.Current;
				val = <model>5__3.BeforeCardPlayed(cardPlay).GetAwaiter();      // <-- per-listener await
				if (!((TaskAwaiter)(ref val)).IsCompleted)
				{
					num = (<>1__state = 0);
					<>u__1 = val;
					((AsyncTaskMethodBuilder)(ref <>t__builder)).AwaitUnsafeOnCompleted<TaskAwaiter, <BeforeCardPlayed>d__15>(ref val, ref this);
					return;
				}
				goto IL_0099;
			}
		}
		finally { if (num < 0 && <>7__wrap1 != null) ((System.IDisposable)<>7__wrap1).Dispose(); }
		<>7__wrap1 = null;
	}
	catch (System.Exception exception) { <>1__state = -2; ((AsyncTaskMethodBuilder)(ref <>t__builder)).SetException(exception); return; }
	<>1__state = -2;
	((AsyncTaskMethodBuilder)(ref <>t__builder)).SetResult();
}
```

Semantics: iterate `IterateCombatHookListeners(combatState)` and `await` each listener's
`AbstractModel.BeforeCardPlayed(CardPlay)` sequentially, calling `InvokeExecutionFinished()` after each.

Guard used by the iterator (`.recon/m-Hook-IterateCombatHookListeners.txt`):

```csharp
private static System.Collections.Generic.IEnumerable<AbstractModel> IterateCombatHookListeners(ICombatState combatState)
{
	if (CombatManager.Instance.IsOverOrEnding && !CombatManager.Instance.IsStarting) yield break;
	foreach (AbstractModel m in combatState.IterateHookListeners()) yield return m;
}
```

> **Note:** `AfterCardPlayed` uses `combatState.IterateHookListeners()` **directly**, while
> `BeforeCardPlayed` uses `IterateCombatHookListeners(combatState)` (with the combat-over guard).

### `Hook.AfterCardPlayed` — declared signature

```csharp
// .recon/m-Hook-AfterCardPlayed.txt
[AsyncStateMachine(typeof(<AfterCardPlayed>d__16))]
public static System.Threading.Tasks.Task AfterCardPlayed(ICombatState combatState,
        MegaCrit.Sts2.Core.GameActions.Multiplayer.PlayerChoiceContext choiceContext,
        MegaCrit.Sts2.Core.Entities.Cards.CardPlay cardPlay)
```

### `Hook.AfterCardPlayed` — full body (`<AfterCardPlayed>d__16 :: MoveNext`)

It does **two full listener passes**: `AfterCardPlayed` then `AfterCardPlayedLate`.

```csharp
// .recon/sm-AfterCardPlayed.txt
<>7__wrap1 = combatState.IterateHookListeners().GetEnumerator();        // pass 1
try {
	if (num != 0) goto IL_00e1;
	TaskAwaiter val = <>u__1; <>u__1 = default(TaskAwaiter); num = (<>1__state = -1);
	goto IL_00b7;
	IL_00b7:
	((TaskAwaiter)(ref val)).GetResult();
	<model>5__3.InvokeExecutionFinished();
	choiceContext.PopModel(<model>5__3);                                 // <-- pushes/pops onto the PlayerChoiceContext
	<model>5__3 = null;
	goto IL_00e1;
	IL_00e1:
	if (((System.Collections.IEnumerator)<>7__wrap1).MoveNext())
	{
		<model>5__3 = <>7__wrap1.Current;
		choiceContext.PushModel(<model>5__3);
		val = <model>5__3.AfterCardPlayed(choiceContext, cardPlay).GetAwaiter();
		if (!((TaskAwaiter)(ref val)).IsCompleted) { num = (<>1__state = 0); <>u__1 = val; /* AwaitUnsafeOnCompleted */ return; }
		goto IL_00b7;
	}
} finally { ... }
<>7__wrap1 = null;
<>7__wrap1 = combatState.IterateHookListeners().GetEnumerator();        // pass 2
goto IL_0129;
IL_0129:
try {
	if (num != 1) goto IL_01e3;
	TaskAwaiter val2 = <>u__1; <>u__1 = default(TaskAwaiter); num = (<>1__state = -1);
	goto IL_01b9;
	IL_01b9:
	((TaskAwaiter)(ref val2)).GetResult();
	<model>5__3.InvokeExecutionFinished();
	choiceContext.PopModel(<model>5__3);
	<model>5__3 = null;
	goto IL_01e3;
	IL_01e3:
	if (((System.Collections.IEnumerator)<>7__wrap1).MoveNext())
	{
		<model>5__3 = <>7__wrap1.Current;
		choiceContext.PushModel(<model>5__3);
		val2 = <model>5__3.AfterCardPlayedLate(choiceContext, cardPlay).GetAwaiter();   // <-- pass 2
		if (!((TaskAwaiter)(ref val2)).IsCompleted) { num = (<>1__state = 1); <>u__1 = val2; /* AwaitUnsafeOnCompleted */ return; }
		goto IL_01b9;
	}
} finally { ... }
<>7__wrap1 = null;
```

### Call sites (global search result)

The only vanilla call sites are inside `MegaCrit.Sts2.Core.Models.CardModel.<OnPlayWrapper>d__339`
(state 8 and state 12 respectively — excerpts in §3). Evidence:

```
$ dotnet ilspycli.dll search sts2.dll "AfterCardPlayed" ...
// --- MegaCrit.Sts2.Core.Models.Relics.DaughterOfTheWind.<AfterCardPlayed>d__4
// --- ... (≈60 relic/power overrides of AbstractModel.AfterCardPlayed)
// --- MegaCrit.Sts2.Core.Hooks.Hook.<AfterCardPlayed>d__16
// --- MegaCrit.Sts2.Core.Hooks.Hook.<BeforeCardPlayed>d__15
```

Overriders of the *listener* side are `AbstractModel` virtuals
(`.recon/M-AbstractModel.txt`):

```
M public virtual System.Threading.Tasks.Task BeforeCardPlayed(CardPlay cardPlay)
M public virtual System.Threading.Tasks.Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
M public virtual System.Threading.Tasks.Task AfterCardPlayedLate(PlayerChoiceContext choiceContext, CardPlay cardPlay)
```

### Timing of `AfterCardPlayed` relative to damage

**After.** In `OnPlayWrapper`, the effect body runs at IL_05eb→IL_06af:

```csharp
IL_05eb:
((TaskAwaiter)(ref val17)).GetResult();                                  // BeforeCardPlayed completed
if (CombatManager.Instance.IsCurrentLiveCombat(<effectCombatId>5__6))
	CombatManager.Instance.History.CardPlayStarted(<combatState>5__2, <cardPlay>5__12);

branchingPlayerChoiceContext = new BranchingPlayerChoiceContext(cardModel, LocalContext.NetId.Value, GameActionType.Combat, choiceContext);
branchingPlayerChoiceContext.PushModel(cardModel);
task = cardModel.OnPlay(branchingPlayerChoiceContext, <cardPlay>5__12);   // <<< DAMAGE / POWERS HAPPEN HERE
val16 = branchingPlayerChoiceContext.AssignTaskAndWaitForPauseOrCompletion(task).GetAwaiter();
if (!((TaskAwaiter)(ref val16)).IsCompleted) { num = (<>1__state = 9); ...; return; }
goto IL_06af;

IL_06af:
((TaskAwaiter)(ref val16)).GetResult();                                  // effects finished
if (cardModel.Owner.Creature.IsDead) break;
cardModel.InvokeExecutionFinished();
if (cardModel.Enchantment != null) { val15 = cardModel.Enchantment.OnPlay(choiceContext, <cardPlay>5__12).GetAwaiter(); /* await */ }
IL_076c:
if (cardModel.Affliction != null) { val14 = <affliction>5__13.OnPlay(choiceContext, target).GetAwaiter(); /* await */ }
IL_081a:
if (CombatManager.Instance.IsCurrentLiveCombat(<effectCombatId>5__6))
	CombatManager.Instance.History.CardPlayFinished(<combatState>5__2, <cardPlay>5__12);
if (CombatManager.Instance.IsInProgress)
{
	val13 = Hook.AfterCardPlayed(<combatState>5__2, choiceContext, <cardPlay>5__12).GetAwaiter();  // <<< AFTER DAMAGE
	...
}
```

Wall-clock ordering inside one repetition:

```
BeforeCardPlayed  --->  CardPlayStarted  --->  OnPlay (damage/powers, awaited to completion)
                  --->  Enchantment.OnPlay  --->  Affliction.OnPlay
                  --->  CardPlayFinished   --->  AfterCardPlayed / AfterCardPlayedLate
```

So `AfterCardPlayed` ≈ *just after damage & powers resolved*, before the card is moved to its
result pile (`CardCmd.Exhaust` / `CardPileCmd.Add` at IL_0b61+ run after the loop).
It also does **not** fire if `CombatManager.Instance.IsInProgress == false` (combat ended mid-play).

---

## 3. The executor: `PlayCardAction` → `CardModel.OnPlayWrapper`

### 3a. `MegaCrit.Sts2.Core.GameActions.PlayCardAction` (sealed, `: GameAction`)

```csharp
// .recon/t-PlayCardAction.txt
namespace MegaCrit.Sts2.Core.GameActions;

public sealed class PlayCardAction : GameAction
{
	private CardModel? _card;

	public override ulong OwnerId => Player.NetId;
	public override GameActionType ActionType => GameActionType.CombatPlayPhaseOnly;

	public Player Player { get; }
	public NetCombatCard NetCombatCard { get; }
	public ModelId CardModelId { get; }
	public uint? TargetId { get; }
	public PlayerChoiceContext? PlayerChoiceContext { get; private set; }

	public Creature? Target => Player.Creature.CombatState?.GetCreature(TargetId);

	public PlayCardAction(CardModel cardModel, Creature? target)            // LOCAL ctor  (used by UI)
	{
		if (target != null && !target.CombatId.HasValue)
			throw new InvalidOperationException($"Cannot target card against target {target} with no combat ID!");
		Player = cardModel.Owner;
		NetCombatCard = NetCombatCard.FromModel(cardModel);
		CardModelId = cardModel.Id;
		TargetId = target?.CombatId;
	}

	public PlayCardAction(Player player, NetCombatCard netCombatCard, ModelId cardModelId, uint? targetId)  // REMOTE ctor
	{ Player = player; NetCombatCard = netCombatCard; CardModelId = cardModelId; TargetId = targetId; }

	[AsyncStateMachine(typeof(<ExecuteAction>d__25))]
	protected override System.Threading.Tasks.Task ExecuteAction()          // <-- ASYNC
	{ /* bare async plumbing, decompiled body is in <ExecuteAction>d__25 */ }

	protected override void CancelAction()                                   // SYNC
	{
		if (TestMode.IsOn && !RunManager.Instance.IsInProgress) return;
		if (_card == null) _card = NetCombatCard.ToCardModelOrNull();
		if (_card != null)
		{
			NCardPlayQueue.Instance?.RemoveCardFromQueueForCancellation(this);
			if (LocalContext.IsMe(Player)) NPlayerHand.Instance?.TryCancelCardPlay(_card);
		}
	}

	public override INetAction ToNetAction() { return new NetPlayCardAction { card = NetCombatCard, modelId = CardModelId, targetId = TargetId }; }
	public string ToString() { return $"PlayCardAction card: {NetCombatCard.ToCardModelOrNull()} index: {NetCombatCard.CombatCardIndex} targetid: {TargetId}"; }
}
```

### 3b. `PlayCardAction.<ExecuteAction>d__25 :: MoveNext` — skeleton with await points

**Yes, it is async; it awaits network/target lookup, resource animation, and the whole effect body.**

```csharp
// .recon/t-PlayCardAction.txt (state machine) — reconstructed, IL-goto stripped
private void MoveNext()
{
	int num = <>1__state;
	PlayCardAction playCardAction = <>4__this;
	try
	{
		switch (num)
		{
		default:
			playCardAction._card = playCardAction.NetCombatCard.ToCardModel();
			NCardPlayQueue.Instance?.UpdateCardBeforeExecution(playCardAction);      // [client view] snap card into play queue
			val3 = playCardAction.Player.Creature.CombatState
			          .GetCreatureAsync(playCardAction.TargetId, 10.0).GetAwaiter(); // AWAIT #1
			if (!val3.IsCompleted) { num = (<>1__state = 0); <>u__1 = val3; ...; return; }
			goto IL_00bb;
		case 0: /* resume AWAIT #1 */
			val3 = <>u__1; ...; goto IL_00bb;
		case 1: /* resume AWAIT #2 */
			val2 = <>u__2; ...; goto IL_0317;
		case 2: /* resume AWAIT #3 */
			val = <>u__3;  ...; break;

		IL_00bb:
			result = val3.GetResult();
			<target>5__2 = result;
			pile = playCardAction._card.Pile;
			if (pile == null || pile.Type != PileType.Hand)
			{
				NCardPlayQueue.Instance?.RemoveCardFromQueueForCancellation(playCardAction);
			}
			else
			{
				/* ... target validation, Log.Warn for missing Any-target ... */
				if (playCardAction._card.CanPlay(out UnplayableReason _, out AbstractModel _)
				    && playCardAction._card.IsValidTarget(<target>5__2))
				{
					Log.Info($"Player {playCardAction._card.Owner.NetId} playing card {playCardAction._card.Id.Entry} ({text})");
					val2 = playCardAction._card.SpendResources().GetAwaiter();      // AWAIT #2  <-- energy/stars consumed
					if (!val2.IsCompleted) { num = (<>1__state = 1); <>u__2 = val2; ...; return; }
					goto IL_0317;
				}
				playCardAction.Cancel();                        // <-- only cancellation path here
			}
			goto end_IL_000e;

		IL_0317:
			result2 = val2.GetResult();
			/* resources = new ResourceInfo { EnergySpent = item1, EnergyValue = item1, StarsSpent = item2, StarValue = item2 }; */
			playCardAction.PlayerChoiceContext = new GameActionPlayerChoiceContext(playCardAction);
			val = playCardAction._card
			        .OnPlayWrapper(playCardAction.PlayerChoiceContext, <target>5__2,
			                       isAutoPlay: false, resources).GetAwaiter();  // AWAIT #3  <-- EVERYTHING HAPPENS HERE
			if (!((TaskAwaiter)(ref val)).IsCompleted)
			{ num = (<>1__state = 2); <>u__3 = val; ...; return; }
			break;
		}
		((TaskAwaiter)(ref val)).GetResult();
		end_IL_000e:;
	}
	catch (System.Exception exception) { <>1__state = -2; <target>5__2 = null; ...SetException(exception); return; }
	<>1__state = -2;
	<target>5__2 = null;
	((AsyncTaskMethodBuilder)(ref <>t__builder)).SetResult();
}
```

**Await summary for `PlayCardAction.ExecuteAction`:**

| # | awaited expression | what it does |
|---|---|---|
| 1 | `Player.Creature.CombatState.GetCreatureAsync(TargetId, 10.0)` → `Task<Creature?>` | resolve target creature by combat id (with 10 s timeout) |
| 2 | `_card.SpendResources()` → `Task<(int,int)>` | **spend energy & stars** (`SpendEnergy` / `SpendStars`) |
| 3 | `_card.OnPlayWrapper(ctx, target, false, resources)` → `Task` | **entire effect sequence** incl. both card hooks |

### 3c. `MegaCrit.Sts2.Core.Models.CardModel.OnPlayWrapper` — public entry, async

```csharp
// .recon/m-CardModel-OnPlayWrapper.txt
[AsyncStateMachine(typeof(<OnPlayWrapper>d__339))]
public System.Threading.Tasks.Task OnPlayWrapper(PlayerChoiceContext choiceContext, Creature? target,
        bool isAutoPlay, ResourceInfo resources, bool skipCardPileVisuals = false)
```

Body = `<OnPlayWrapper>d__339 :: MoveNext` (769 ILSpy lines). Ordered skeleton:

```csharp
private void MoveNext()   // MegaCrit.Sts2.Core.Models.CardModel.<OnPlayWrapper>d__339
{
	CardModel cardModel = <>4__this;
	try
	{
		choiceContext.PushModel(cardModel);
		await CombatManager.Instance.WaitForUnpause();                        // AWAIT (state 0)
		cardModel.CurrentTarget = target;
		cardModel.CurrentPlayIndex = 0;
		if (isAutoPlay) await CardPileCmd.Add(cardModel, PileType.Play, CardPilePosition.Bottom, null, skipCardPileVisuals);  // state 1
		else            await CardPileCmd.AddDuringManualCardPlay(cardModel); // state 3
		if (!skipCardPileVisuals) await Cmd.CustomScaledWait(0.25f, 0.35f);   // state 2  <-- existing presentation wait

		<combatState>5__2 = cardModel.CombatState;
		if (<combatState>5__2 != null)
		{
			<resultLocation>5__3 = cardModel.GetResultLocationForCardPlay();
			<resultLocation>5__3 = Hook.ModifyCardPlayResultLocation(<combatState>5__2, cardModel, isAutoPlay,
			                         resources, <resultLocation>5__3, out IEnumerable<AbstractModel> modifiers);
			foreach (AbstractModel current in modifiers)
				await current.AfterModifyingCardPlayResultLocation(cardModel, <resultLocation>5__3);  // state 4
		}

		<playCount>5__4 = await cardModel.GeneratePlayCount(<combatState>5__2, target);              // state 5

		if (!cardModel.Owner.Creature.IsDead)
		{
			<playStartTime>5__5 = Time.GetTicksMsec();
			<effectCombatId>5__6 = CombatManager.Instance.BeginCardOrPotionEffect(cardModel.Owner);
			< i = 0; >
			for (; <i>5__11 < <playCount>5__4 && !CombatManager.Instance.IsOverOrEnding; <i>5__11++)
			{
				cardModel.CurrentPlayIndex = <i>5__11;
				if (cardModel.Type == CardType.Power)
					await cardModel.PlayPowerCardFlyVfx();                                           // state 6
				else if (<i>5__11 > 0)
				{
					NCard nCard = NCard.FindOnTable(cardModel);
					if (nCard != null) await nCard.AnimMultiCardPlay();                              // state 7
				}

				CardPlay cardPlay = new CardPlay { Card = cardModel, Player = cardModel.Owner, Target = target,
					ResultPile = <resultLocation>5__3.pileType, Resources = resources,
					IsAutoPlay = isAutoPlay, PlayIndex = <i>5__11, PlayCount = <playCount>5__4 };

				await Hook.BeforeCardPlayed(<combatState>5__2, cardPlay);                            // state 8   <=== SEAM
				if (CombatManager.Instance.IsCurrentLiveCombat(<effectCombatId>5__6))
					CombatManager.Instance.History.CardPlayStarted(<combatState>5__2, cardPlay);

				var branchingPlayerChoiceContext = new BranchingPlayerChoiceContext(
						cardModel, LocalContext.NetId.Value, GameActionType.Combat, choiceContext);
				branchingPlayerChoiceContext.PushModel(cardModel);
				Task task = cardModel.OnPlay(branchingPlayerChoiceContext, cardPlay);                 // the card's effect
				await branchingPlayerChoiceContext.AssignTaskAndWaitForPauseOrCompletion(task);       // state 9  <=== EFFECTS
				if (cardModel.Owner.Creature.IsDead) break;
				cardModel.InvokeExecutionFinished();

				if (cardModel.Enchantment != null) await cardModel.Enchantment.OnPlay(choiceContext, cardPlay);  // state 10
				if (cardModel.Affliction  != null) await <affliction>.OnPlay(choiceContext, target);             // state 11

				if (CombatManager.Instance.IsCurrentLiveCombat(<effectCombatId>5__6))
					CombatManager.Instance.History.CardPlayFinished(<combatState>5__2, cardPlay);
				if (CombatManager.Instance.IsInProgress)
					await Hook.AfterCardPlayed(<combatState>5__2, choiceContext, cardPlay);            // state 12
			}
		}
	}
	catch { ... }

	await CombatManager.Instance.EndCardOrPotionEffect(<effectCombatId>5__6, cardModel.Owner);        // state 13

	if (cardModel.Pile != null && cardModel.Pile.Type == PileType.Play)
	{
		if (<resultLocation>5__3.pileType == PileType.Exhaust)
			await CardCmd.Exhaust(choiceContext, cardModel, causedByEthereal: false, skipCardPileVisuals);  // state 17
		else if (<resultLocation>5__3.pileType != PileType.None)
			await CardPileCmd.Add(cardModel, <resultLocation>5__3.pileType, <resultLocation>5__3.position, null, skipCardPileVisuals); // state 18
		else
			await CardPileCmd.RemoveFromCombat(cardModel, skipCardPileVisuals);                        // state 16
	}

	<originalOwner>5__7 = cardModel.Owner;
	if (CombatManager.Instance.CurrentCombatId == <effectCombatId>5__6
	    && <originalOwner>5__7 != <resultLocation>5__3.player && <resultLocation>5__3.pileType != PileType.None)
		await CardPileCmd.GiveToAnotherPlayer(cardModel, <resultLocation>5__3.player, <resultLocation>5__3.pileType, <resultLocation>5__3.position);  // state 15

	if (!skipCardPileVisuals)
	{
		float num3 = (float)(Time.GetTicksMsec() - <playStartTime>5__5) / 1000f;
		await Cmd.CustomScaledWait(0.15f - num3, 0.3f - num3);                                         // state 14
	}

	await CombatManager.Instance.CheckForEmptyHand(<effectCombatId>5__6, choiceContext, <originalOwner>5__7);  // state 19

	if (cardModel.EnergyCost.AfterCardPlayedCleanup()) cardModel.EnergyCostChanged?.Invoke();
	if (cardModel._temporaryStarCosts.RemoveAll(c => c.ClearsWhenCardIsPlayed) > 0) cardModel.StarCostChanged?.Invoke();
	cardModel.CurrentTarget = null;
	cardModel.CurrentPlayIndex = 0;
	cardModel.Played?.Invoke();                          // <-- public System.Action Played  (post-play event)
	choiceContext.PopModel(cardModel);
}
```

### 3d. The concrete card effect seam

```csharp
// .recon/m-CardModel-OnPlayGenPlayCount.txt
protected virtual System.Threading.Tasks.Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
{
	return System.Threading.Tasks.Task.CompletedTask;
}
```

Every real card overrides this `protected virtual Task OnPlay(...)`.

### 3e. `GameAction.Execute()` / `ActionExecutor` — the queue wrapper (async)

```csharp
// .recon/m-GameAction-Execute.txt
[AsyncStateMachine(typeof(<Execute>d__45))]
public System.Threading.Tasks.Task Execute()
protected abstract System.Threading.Tasks.Task ExecuteAction();
public System.Action BeforeExecuted, AfterExecuted, JustBeforeFinished /* (as events) */;
```

`GameAction.<Execute>d__45 : MoveNext` body:

```csharp
gameAction._pauseForPlayerChoiceTaskSource = new TaskCompletionSource();
switch (gameAction.State)
{
case GameActionState.WaitingForExecution:
	gameAction.State = GameActionState.Executing;
	gameAction.BeforeExecuted?.Invoke(gameAction);
	gameAction._executionTask = TaskHelper.RunSafely(gameAction.ExecuteAction());   // fire the body
	break;
case GameActionState.ReadyToResumeExecuting:
	gameAction.State = GameActionState.Executing;
	gameAction._executeAfterResumptionTaskSource.SetResult();
	break;
default: throw new InvalidOperationException($"Attempted to execute GameAction {gameAction} from invalid state {gameAction.State}! ...");
}
val = TaskHelper.WhenAny(gameAction._executionTask, gameAction._pauseForPlayerChoiceTaskSource.Task).GetAwaiter();
// ... then: if (_executionTask.IsCompleted) { State = Finished; JustBeforeFinished?.Invoke(); _completionSource.TrySetResult(); AfterFinished?.Invoke(); }
//           else _logger.VeryDebug($"Action {gameAction} paused execution");
```

`ActionExecutor` (`.recon/M-ActionExecutor.txt`) is the per-player queue driver:

```
M public System.Void Pause()
M public System.Void Unpause()
M public System.Threading.Tasks.Task FinishedExecutingActions()
M private System.Threading.Tasks.Task ExecuteActions()      // loop: GetReadyAction() -> WaitForUnpause() -> action.Execute() -> CheckWinCondition()
M private System.Threading.Tasks.Task WaitForUnpause()
F System.Action`1[[GameAction]] BeforeActionExecuted / AfterActionExecuted / JustBeforeActionFinishedExecuting     (also exposed as events)
```

`ActionExecutor.<ExecuteActions>d__28 :: MoveNext` (`.recon/m-ActionExecutor-MoveNext.txt`):

```csharp
<readyAction>5__2 = actionExecutor._actionQueueSet.GetReadyAction();
if (<readyAction>5__2 != null)
{
	await actionExecutor.WaitForUnpause();                     // state 0
	... /* playerChoice / pause bookkeeping */
	CombatManager.Instance.CheckWinCondition()                 // state 3, awaited
}
else { actionExecutor._queueTaskCompletionSource?.SetResult(true); }
```

---

## 4. Client-layer coordinator

**There is no `CombatAnimator` / `CardPlayAnimation` / `CardEffectSequence` type.**
Type search results (`.recon/t-PlayAnimationTypes.txt`, `.recon/NS-NodesCombat.txt`):

```
MegaCrit.Sts2.Core.Animation.CreatureAnimator
MegaCrit.Sts2.Core.Nodes.Vfx.*                       (NCardFlyVfx, NCardFlyPowerVfx, NCardExhaustVfx, ...)
MegaCrit.Sts2.Core.Nodes.Combat.NCardPlay            (abstract, UI input state machine)
MegaCrit.Sts2.Core.Nodes.Combat.NMouseCardPlay       (: NCardPlay — mouse targeting)
MegaCrit.Sts2.Core.Nodes.Combat.NControllerCardPlay  (: NCardPlay — gamepad targeting)
MegaCrit.Sts2.Core.Nodes.Combat.NCardPlayQueue       (: Control  — the visible "play queue" widget)
MegaCrit.Sts2.Core.Nodes.Combat.NPlayerHand          (: Control)
MegaCrit.Sts2.Core.Nodes.Combat.NCombatUi / NCombatSceneContainer / NCombatPilesContainer
```

Roles:

- **`NCardPlayQueue`** — client-side *visual* queue only. **Every method is sync `void`**; there is
  **no `async Task` method to prefix.** It subscribes to the action queue:

  ```csharp
  // .recon/t-NCardPlayQueue.txt
  public static NCardPlayQueue? Instance => NCombatRoom.Instance?.Ui.PlayQueue;

  public override void _Ready() { RunManager.Instance.ActionQueueSet.ActionEnqueued += OnActionEnqueued; }

  private void OnActionEnqueued(GameAction action)
  {
      if (!(action is PlayCardAction { NetCombatCard: var netCombatCard } playCardAction)) return;
      CardModel cardModel = netCombatCard.ToCardModelOrNull();
      /* ... */
      if (LocalContext.IsMe(playCardAction.Player))
      { OnLocalCardPlayed(playCardAction, NPlayerHand.Instance.GetCardHolder(cardModel), cardModel); return; }
      /* remote player: create NCard at the remote intent position and tween it in */
  }

  public void UpdateCardBeforeExecution(PlayCardAction playCardAction)   // called from PlayCardAction.ExecuteAction (AWAIT #0)
  public void RemoveCardFromQueueForCancellation(PlayCardAction action)
  public void RemoveCardFromQueueForExecution(CardModel card)
  public void AnimOut()
  private void TweenCardToQueuePosition(QueueItem item, int queueIndex)   // 0.35 s tween
  ```

- **`NCardPlay`** (abstract) — the *input/targeting* state machine, not the effect sequencer:

  ```csharp
  // .recon/M-NCardPlay.txt
  M public abstract System.Void Start()
  M protected System.Void TryPlayCard(MegaCrit.Sts2.Core.Entities.Creatures.Creature target)   // void, SYNC
  M public System.Void CancelPlayCard()
  M protected System.Void Cleanup(System.Boolean isFinished)
  E MegaCrit.Sts2.Core.Nodes.Combat.NCardPlay+FinishedEventHandler Finished
  ```

  `NCardPlay.TryPlayCard` is the **commit point from the UI** but is sync `void` (cannot `await`):

  ```csharp
  protected void TryPlayCard(Creature? target)
  {
      if (Card == null) return;
      if (Card.TargetType == TargetType.AnyEnemy && target == null) { CancelPlayCard(); return; }
      if (Card.TargetType == TargetType.AnyAlly  && target == null) { CancelPlayCard(); return; }
      if (!Holder.CardModel.CanPlayTargeting(target)) { CannotPlayThisCardFtueCheck(Holder.CardModel); CancelPlayCard(); return; }
      CardModel card = Card;
      _isTryingToPlayCard = true;
      bool flag = card.TryManualPlay(target);      // AnyEnemy/AnyAlly/AOE branch above it
      _isTryingToPlayCard = false;
      if (flag) { AutoDisableCannotPlayCardFtueCheck(); Cleanup(isFinished: true); ActiveScreenContext.Instance.FocusOnDefaultControl(); }
      else CancelPlayCard();
  }
  ```

- **`NMouseCardPlay`** has async methods, but they are **drag/targeting**, not effect sequencing:
  `private Task StartAsync()`, `StartCardDrag()`, `TargetSelection(TargetMode)`, `SingleCreatureTargeting(TargetMode, TargetType)`, `MultiCreatureTargeting(TargetMode)`, `LerpToMouse(NHandCardHolder)`.
  All run **before** the card is committed.

- **`NPlayerHand`** — hand layout/selection; only async member is
  `public Task<IEnumerable<CardModel>> SelectCards(CardSelectorPrefs prefs, Func<CardModel,bool> filter, AbstractModel source, Mode mode)` (card-selection screens, not play resolution).

### The one existing async "presentation wait" primitive a mod can reuse

```csharp
// .recon/M-Cmd.txt / m-Cmd-Wait.txt
public static System.Threading.Tasks.Task Wait(float seconds, bool ignoreCombatEnd = false)
public static System.Threading.Tasks.Task Wait(float seconds, CancellationToken cancelToken, bool ignoreCombatEnd = false)
private static System.Threading.Tasks.Task WaitInternal(Godot.SceneTreeTimer timer, CancellationToken cancellationToken)
public static System.Threading.Tasks.Task CustomScaledWait(float fastSeconds, float standardSeconds,
        bool ignoreCombatEnd = false, CancellationToken cancellationToken = default(CancellationToken))
```

Vanilla itself uses `await Cmd.CustomScaledWait(0.25f, 0.35f)` inside `OnPlayWrapper` for card-play
pacing, and `Cmd.Wait` is `SceneTreeTimer`-based (Godot-frame-driven, so it is *pausable* and
*timer-scale aware*). **Prefer `Cmd.CustomScaledWait` / `Cmd.Wait` over `Task.Delay`** in a
Godot game: `Task.Delay` is wall-clock and keeps running while the game is paused / on the
pause menu, and it does not respect the game's animation-speed setting that `CustomScaledWait` does.

---

## 5. Multiplayer / network

### Replication path

```csharp
// .recon/m-CardModel-EnqueueManualPlay.txt
private void EnqueueManualPlay(Creature? target)
{
	TaskHelper.RunSafely(OnEnqueuePlayVfx(target));                                   // fire-and-forget VFX
	RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(new PlayCardAction(this, target));
}
```

```csharp
// .recon/t-ActionQueueSynchronizer.txt   (MegaCrit.Sts2.Core.GameActions.Multiplayer.ActionQueueSynchronizer)
public void RequestEnqueue(GameAction action)
{
	if (action.ActionType == GameActionType.CombatPlayPhaseOnly && CombatState == ActionSynchronizerCombatState.NotPlayPhase)
	{ _logger.Debug($"Attempted to request enqueue of action {action} during the enemy turn. Deferring ..."); _requestedActionsWaitingForPlayerTurn.Add(action); return; }

	switch (_netService.Type)
	{
	case NetGameType.Client:
		_logger.Debug($"Sending message to host requesting to enqueue action {action} at {Log.Timestamp}");
		RequestEnqueueActionMessage message = new RequestEnqueueActionMessage { action = action.ToNetAction(), location = _messageBuffer.CurrentLocation };
		_netService.SendMessage(message);
		break;
	case NetGameType.Singleplayer:
	case NetGameType.Host:
		EnqueueAction(action, _netService.NetId);
		break;
	}
}

private void EnqueueAction(GameAction action, ulong actionOwnerId)
{
	if (_netService.Type == NetGameType.Host)
	{
		_logger.Debug($"Sending message to clients to enqueue action {action} at {Log.Timestamp}");
		ActionEnqueuedMessage message = new ActionEnqueuedMessage { playerId = actionOwnerId, location = _messageBuffer.CurrentLocation, action = action.ToNetAction() };
		_netService.SendMessage(message);
	}
	_logger.Debug($"Enqueueing action {action} from owner {actionOwnerId}");
	_actionQueueSet.EnqueueWithoutSynchronizing(action);      // <-- local execution, NO per-action ACK
}
```

Wire format (`.recon/t-NetPlayCardAction.txt`):

```csharp
public struct NetPlayCardAction : INetAction, IPacketSerializable
{
	public NetCombatCard card;
	public ModelId modelId;
	public uint? targetId;

	public GameAction ToGameAction(Player player) => new PlayCardAction(player, card, modelId, targetId);
	public void Serialize(PacketWriter writer)   { writer.Write(card); writer.WriteModelEntry(modelId); writer.WriteBool(targetId.HasValue); if (targetId.HasValue) writer.WriteUInt(targetId.GetValueOrDefault(), 6); }
	public void Deserialize(PacketReader reader) { /* mirror of Serialize */ }
}
```

**Key structural fact: `PlayCardAction` is not "the host plays it and clients watch". Every peer
enqueues its own local `PlayCardAction` (via `HandleActionEnqueuedMessage` → `EnqueueAction` →
`EnqueueWithoutSynchronizing`) and runs the whole `ExecuteAction → OnPlay → damage` pipeline
locally.** Client sends only *intent* (`RequestEnqueueActionMessage`), the host fans out
`ActionEnqueuedMessage`, and each peer executes identically. There is **no per-action completion
ACK / lockstep barrier**; `GameAction.State` and per-player queues are local.

Relevant message/handler surface (`.recon/t-ActionQueueSynchronizer.txt`):

```
_messageBuffer.RegisterMessageHandler<RequestEnqueueActionMessage>(HandleRequestEnqueueActionMessage);
_messageBuffer.RegisterMessageHandler<ActionEnqueuedMessage>(HandleActionEnqueuedMessage);
_messageBuffer.RegisterMessageHandler<RequestEnqueueHookActionMessage>(HandleRequestEnqueueHookActionMessage);
_messageBuffer.RegisterMessageHandler<HookActionEnqueuedMessage>(HandleHookActionEnqueuedMessage);
```

Related types: `ActionQueueSet`, `ActionQueueSet.ActionQueue`, `ActionQueueSynchronizer`,
`PlayerChoiceSynchronizer`, `PlayerChoiceSynchronizer.<WaitForRemoteChoice>d__15`,
`PlayerChoiceContext` (abstract: `PushModel/PopModel/SignalPlayerChoiceBegun/SignalPlayerChoiceEnded`),
`BlockingPlayerChoiceContext`, `BranchingPlayerChoiceContext`, `GameActionPlayerChoiceContext`,
`HookPlayerChoiceContext`, `NetCombatCardDb`, `GenericHookGameAction`, `INetAction`.

### Will a mod-only delay on ONE client desync multiplayer?

**Depends entirely on where you put the delay:**

| Placement | Effect in MP | Desync risk |
|---|---|---|
| **Inside `Hook.BeforeCardPlayed` / `OnPlayWrapper` / any `MegaCrit.Sts2.Core` deterministic code, unconditionally** | runs on **every** peer, all peers pause the same wall-clock amount | **None** — state stays byte-identical, only global pacing changes. This is the recommended approach. |
| Same code but **conditional on local player** (`LocalContext.IsMe(player)`, `if (!IsInMultiplayer)`) | only one peer stalls | **Yes — avoid.** No ACK barrier exists; the other peer can `GetReadyAction()` on its own queue and advance `CombatManager.History`, `CurrentPlayIndex`, and pile state while the stalling peer is still inside the previous play. Divergence can surface as stuck queues / out-of-order `DrawCard`/`RNG` usage, and `CombatStateSync` will not repair a *timing-induced* divergence. |
| Inside `MegaCrit.Sts2.Core.Nodes.*` (client-only tree, e.g. `NCardPlayQueue`, `NCard` tweens) | local visuals only, logic untouched | Safe for logic; **but the card's damage will still land on time**, so it does not achieve "delay the resolution". |

The engine's own supported way to make *one* player pause and others wait is the player-choice
channel, which *is* synchronised:

```csharp
// ActionQueueSet (public API)
public void PauseActionForPlayerChoice(GameAction action, PlayerChoiceOptions options)
public void PauseAllPlayerQueues() / UnpauseAllPlayerQueues()
public void ResumeActionWithoutSynchronizing(uint id)
public bool ActionQueueIsPaused(ulong playerId)
// ActionQueueSynchronizer
public void RequestResumeActionAfterPlayerChoice(GameAction action)   // Client -> host -> ResumeActionAfterPlayerChoiceMessage (ACKed)
public GenericHookGameAction GenerateHookAction(ulong ownerId, GameActionType gameActionType)
public void RequestEnqueueHookAction(GenericHookGameAction action)
```

Also note `CombatManager.Instance.Pause()` / `Unpause()` / `WaitForUnpause()` is a **local** gate
awaited at the top of `OnPlayWrapper` — an engine-sanctioned, desync-free way to hold the *local*
queue, but it does not hold remote peers.

**Bottom line for the mission:** inject the delay in *shared, deterministic* code
(`Hook.BeforeCardPlayed`), so all peers wait together and nothing desyncs.

---

## 6. Mod-facing "card is played" event, and cancelling a play

### 6a. Event surface

There are **two** mod-facing mechanisms.

**(1) `MegaCrit.Sts2.Core.Hooks.Hook`** — static fan-out over `AbstractModel` listeners.
`AbstractModel` exposes the virtuals a mod can override
(`.recon/M-AbstractModel.txt`); the card-play ones are:

```
M public virtual System.Threading.Tasks.Task BeforeCardPlayed(CardPlay cardPlay)
M public virtual System.Threading.Tasks.Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
M public virtual System.Threading.Tasks.Task AfterCardPlayedLate(PlayerChoiceContext choiceContext, CardPlay cardPlay)
M public virtual System.Threading.Tasks.Task BeforeCardAutoPlayed(CardModel card, Creature target, AutoPlayType type)   [Hook.BeforeCardAutoPlayed]
M public virtual System.Int32 ModifyCardPlayCount(CardModel card, Creature target, Int32 playCount)
M public virtual CardLocation ModifyCardPlayResultLocation(CardModel card, Boolean isAutoPlay, ResourceInfo resources, CardLocation cardLocation)
M public virtual System.Boolean ShouldPlay(CardModel card, AutoPlayType autoPlayType)
M public virtual System.Threading.Tasks.Task AfterModifyingCardPlayCount(CardModel card)
M public virtual System.Threading.Tasks.Task AfterModifyingCardPlayResultLocation(CardModel card, CardLocation cardLocation)
M public virtual System.Threading.Tasks.Task AfterModifyingBlockAmount(Decimal modifiedAmount, CardModel cardSource, CardPlay cardPlay)
```

**(2) `MegaCrit.Sts2.Core.Modding.ModHelper`** — the official mod registration API
(`.recon/M-ModHelper.txt`, `.recon/t-Delegates.txt`):

```csharp
public static class ModHelper
{
	public static System.Void SubscribeForRunStateHooks(string id, RunHookSubscriptionDelegate del)
	public static System.Void SubscribeForCombatStateHooks(string id, CombatHookSubscriptionDelegate del)
	public static System.Collections.Generic.IEnumerable<AbstractModel> IterateAllCombatStateSubscribers(CombatState combatState)
	public static System.Collections.Generic.IEnumerable<AbstractModel> IterateAllRunStateSubscribers(RunState runState)
	public static System.Void AddModelToPool()
	public static System.Void AddModelToPool(System.Type poolType, System.Type modelType)
	public static System.Collections.Generic.IEnumerable<T> ConcatModelsFromMods<T>(IPoolModel poolModel, IEnumerable<T> pool)
}

public delegate System.Collections.Generic.IEnumerable<AbstractModel> RunHookSubscriptionDelegate(RunState runState);
public delegate System.Collections.Generic.IEnumerable<AbstractModel> CombatHookSubscriptionDelegate(CombatState combatState);
```

A mod returns its own `AbstractModel` subclasses from the delegate; those objects then appear in
`combatState.IterateHookListeners()` and therefore receive `BeforeCardPlayed` / `AfterCardPlayed`.
Subscribers are de-duplicated by `id` and sorted by `string.CompareOrdinal(x.id, y.id)` —
**listen order is deterministic and alphabetical**, which is good for MP determinism.

**(3) Post-play event on the card itself** (simpler, but no `CardPlay` payload; fires at the very
end of `OnPlayWrapper`):

```csharp
// MegaCrit.Sts2.Core.Models.CardModel
E System.Action Played            // invoked via: Action? obj5 = cardModel.Played; obj5?.Invoke();
```

### 6b. Cancellation: `Hook.ShouldPlay`

```csharp
// .recon/m-Hook-ShouldPlay.txt  (MegaCrit.Sts2.Core.Hooks.Hook)
public static bool ShouldPlay(ICombatState combatState, CardModel card, out AbstractModel? preventer, AutoPlayType autoPlayType)
{
	System.Collections.Generic.IEnumerator<AbstractModel> enumerator = IterateCombatHookListeners(combatState).GetEnumerator();
	try
	{
		while (((System.Collections.IEnumerator)enumerator).MoveNext())
		{
			AbstractModel current = enumerator.Current;
			if (!current.ShouldPlay(card, autoPlayType))
			{
				preventer = current;
				return false;
			}
		}
	}
	finally { ((System.IDisposable)enumerator)?.Dispose(); }
	preventer = null;
	return true;
}
```

Default listener implementation:

```csharp
// .recon/m-AbstractModel-ShouldPlay-body.txt
public virtual bool ShouldPlay(CardModel card, AutoPlayType autoPlayType) { return true; }
```

Only caller in vanilla (`.recon/m-CardModel-TryManualPlay.txt`):

```csharp
public bool CanPlay(out UnplayableReason reason, out AbstractModel? preventer)
{
	reason = UnplayableReason.None;
	ICombatState combatState = CombatState ?? _owner?.Creature.CombatState;
	if (combatState == null || Owner.PlayerCombatState == null) { preventer = null; return false; }
	if (Keywords.Contains(CardKeyword.Unplayable)) reason |= UnplayableReason.HasUnplayableKeyword;
	if (!Owner.PlayerCombatState.HasEnoughResourcesFor(this, out var reason2)) reason |= reason2;
	if (TargetType == TargetType.AnyAlly && combatState.PlayerCreatures.Count(c => c.IsAlive) <= 1) reason |= UnplayableReason.NoLivingAllies;
	if (!Hook.ShouldPlay(combatState, this, out preventer, AutoPlayType.None)) reason |= UnplayableReason.BlockedByHook;
	if (!IsPlayable) reason |= UnplayableReason.BlockedByCardLogic;
	return reason == UnplayableReason.None;
}
```

**Yes, a mod can short-circuit a play** — but only *pre-commit*, via `AbstractModel.ShouldPlay`
(registered through `ModHelper.SubscribeForCombatStateHooks`). When any listener returns `false`
the card is greyed out and `CanPlay` returns `false`, so `TryManualPlay` → `EnqueueManualPlay`
never happens: **no `PlayCardAction`, no `BeforeCardPlayed`, no `AfterCardPlayed`.**

Other cancellation seams:

- `CardModel.IsPlayable` (`P System.Boolean IsPlayable { get; }`) → `UnplayableReason.BlockedByCardLogic`.
- `PlayCardAction.CancelAction()` → `GameAction.Cancel()` sets `State = Canceled`, fires `BeforeCancelled`, calls `NCardPlayQueue.Instance?.RemoveCardFromQueueForCancellation(this)` and `NPlayerHand.Instance?.TryCancelCardPlay(_card)`.
  Note the only in-pipeline cancel inside `ExecuteAction` is the "card is no longer in `PileType.Hand`" / failed
  `CanPlay`/`IsValidTarget` branch — **there is no `Hook` that can cancel after commit.**
- `Hook.BeforeCardPlayed` returns `Task` with no out-param ⇒ **cannot veto**. A mod wanting
  "cancel after commit" must either swallow it in a Prefix that returns `false` (skipping
  `OnPlayWrapper` → also skipping `AfterCardPlayed`, history, and the pile move) or patch
  `CardModel.OnPlay`.

---

## 7. Exact declared signatures (as decompiled, FULL parameter types)

All six are `public static` on `MegaCrit.Sts2.Core.Hooks.Hook` (a non-generic static class),
and **all six are `async` returning `System.Threading.Tasks.Task`** — none returns `ValueTask`.

```csharp
namespace MegaCrit.Sts2.Core.Hooks;

public static class Hook
{
	[AsyncStateMachine(typeof(<BeforeCardPlayed>d__15))]
	public static System.Threading.Tasks.Task BeforeCardPlayed(
		MegaCrit.Sts2.Core.Combat.ICombatState combatState,
		MegaCrit.Sts2.Core.Entities.Cards.CardPlay cardPlay);

	[AsyncStateMachine(typeof(<AfterCardPlayed>d__16))]
	public static System.Threading.Tasks.Task AfterCardPlayed(
		MegaCrit.Sts2.Core.Combat.ICombatState combatState,
		MegaCrit.Sts2.Core.GameActions.Multiplayer.PlayerChoiceContext choiceContext,
		MegaCrit.Sts2.Core.Entities.Cards.CardPlay cardPlay);

	public static System.Boolean ShouldPlay(                       // NOT async
		MegaCrit.Sts2.Core.Combat.ICombatState combatState,
		MegaCrit.Sts2.Core.Models.CardModel card,
		out MegaCrit.Sts2.Core.Models.AbstractModel? preventer,     // ref: AbstractModel&
		MegaCrit.Sts2.Core.Entities.Cards.AutoPlayType autoPlayType);

	[AsyncStateMachine(typeof(<BeforeAttack>d__2))]
	public static System.Threading.Tasks.Task BeforeAttack(
		MegaCrit.Sts2.Core.Combat.ICombatState combatState,
		MegaCrit.Sts2.Core.Commands.Builders.AttackCommand command);

	[AsyncStateMachine(typeof(<AfterAttack>d__3))]
	public static System.Threading.Tasks.Task AfterAttack(
		MegaCrit.Sts2.Core.Combat.ICombatState combatState,
		MegaCrit.Sts2.Core.GameActions.Multiplayer.PlayerChoiceContext choiceContext,
		MegaCrit.Sts2.Core.Commands.Builders.AttackCommand command);

	[AsyncStateMachine(typeof(<AfterDamageGiven>d__24))]
	public static System.Threading.Tasks.Task AfterDamageGiven(
		MegaCrit.Sts2.Core.GameActions.Multiplayer.PlayerChoiceContext choiceContext,
		MegaCrit.Sts2.Core.Combat.ICombatState combatState,
		MegaCrit.Sts2.Core.Entities.Creatures.Creature? dealer,      // Creature
		MegaCrit.Sts2.Core.Entities.Creatures.DamageResult results,
		MegaCrit.Sts2.Core.ValueProps.ValueProp props,
		MegaCrit.Sts2.Core.Entities.Creatures.Creature target,
		MegaCrit.Sts2.Core.Models.CardModel? cardSource);            // CardModel
}
```

Nullability exactly as decompiled — note the **parameter order is unusual**:
`BeforeAttack(combatState, command)` puts `combatState` first, but
`AfterAttack(combatState, choiceContext, command)` puts `combatState` first too, while
`AfterDamageGiven(choiceContext, combatState, ...)` puts `choiceContext` **first**.

Reflection names (CLI `members` output, for cross-checking against `get_type_members`):

```
M public static System.Threading.Tasks.Task BeforeAttack(MegaCrit.Sts2.Core.Combat.ICombatState combatState, MegaCrit.Sts2.Core.Commands.Builders.AttackCommand command)
M public static System.Threading.Tasks.Task AfterAttack(MegaCrit.Sts2.Core.Combat.ICombatState combatState, MegaCrit.Sts2.Core.GameActions.Multiplayer.PlayerChoiceContext choiceContext, MegaCrit.Sts2.Core.Commands.Builders.AttackCommand command)
M public static System.Threading.Tasks.Task BeforeCardPlayed(MegaCrit.Sts2.Core.Combat.ICombatState combatState, MegaCrit.Sts2.Core.Entities.Cards.CardPlay cardPlay)
M public static System.Threading.Tasks.Task AfterCardPlayed(MegaCrit.Sts2.Core.Combat.ICombatState combatState, MegaCrit.Sts2.Core.GameActions.Multiplayer.PlayerChoiceContext choiceContext, MegaCrit.Sts2.Core.Entities.Cards.CardPlay cardPlay)
M public static System.Threading.Tasks.Task AfterDamageGiven(MegaCrit.Sts2.Core.GameActions.Multiplayer.PlayerChoiceContext choiceContext, MegaCrit.Sts2.Core.Combat.ICombatState combatState, MegaCrit.Sts2.Core.Entities.Creatures.Creature dealer, MegaCrit.Sts2.Core.Entities.Creatures.DamageResult results, MegaCrit.Sts2.Core.ValueProps.ValueProp props, MegaCrit.Sts2.Core.Entities.Creatures.Creature target, MegaCrit.Sts2.Core.Models.CardModel cardSource)
M public static System.Boolean ShouldPlay(MegaCrit.Sts2.Core.Combat.ICombatState combatState, MegaCrit.Sts2.Core.Models.CardModel card, MegaCrit.Sts2.Core.Models.AbstractModel& preventer, MegaCrit.Sts2.Core.Entities.Cards.AutoPlayType autoPlayType)
```

### Other exact signatures used above

```csharp
// MegaCrit.Sts2.Core.Models.CardModel
public System.Threading.Tasks.Task OnPlayWrapper(PlayerChoiceContext choiceContext, Creature? target,
        bool isAutoPlay, ResourceInfo resources, bool skipCardPileVisuals = false);           // async
protected virtual System.Threading.Tasks.Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay);
protected System.Threading.Tasks.Task<int> GeneratePlayCount(ICombatState combatState, Creature? target);   // async
protected virtual CardLocation GetResultLocationForCardPlay();
public bool TryManualPlay(Creature? target);
public bool CanPlayTargeting(Creature? target);
public bool CanPlay();
public bool CanPlay(out UnplayableReason reason, out AbstractModel? preventer);
public System.Threading.Tasks.Task<ValueTuple<int,int>> SpendResources();                   // async
public virtual System.Threading.Tasks.Task OnEnqueuePlayVfx(Creature? target);
private void EnqueueManualPlay(Creature? target);

// MegaCrit.Sts2.Core.GameActions.PlayCardAction
protected override System.Threading.Tasks.Task ExecuteAction();                              // async
protected override void CancelAction();
public override INetAction ToNetAction();

// MegaCrit.Sts2.Core.Commands.Cmd
public static System.Threading.Tasks.Task Wait(float seconds, bool ignoreCombatEnd = false);
public static System.Threading.Tasks.Task CustomScaledWait(float fastSeconds, float standardSeconds,
        bool ignoreCombatEnd = false, CancellationToken cancellationToken = default(CancellationToken));

// MegaCrit.Sts2.Core.Combat.CombatManager
public System.Threading.Tasks.Task WaitForUnpause();
private System.Threading.Tasks.Task WaitForUnpause(CombatTurnState turnState);
public void Pause();
public void Unpause();
public System.Threading.Tasks.Task EndCardOrPotionEffect(CombatId? combatId, Player player);
public System.Threading.Tasks.Task CheckForEmptyHand(CombatId? combatId, PlayerChoiceContext choiceContext, Player player);

// MegaCrit.Sts2.Core.GameActions.Multiplayer.PlayerChoiceContext  (abstract)
public void PushModel(AbstractModel model);
public void PopModel(AbstractModel model);
public abstract System.Threading.Tasks.Task SignalPlayerChoiceBegun(Player chooser, PlayerChoiceOptions options);
public abstract System.Threading.Tasks.Task SignalPlayerChoiceEnded();
public IEnumerable<AbstractModel> ModelStack { get; }
public AbstractModel LastInvolvedModel { get; }
public ulong? OwnerId { get; }
```

---

## Recommended patch points

### 🥇 Option 1 (recommended) — Postfix `Hook.BeforeCardPlayed`, swap the returned `Task`

```csharp
// Target: MegaCrit.Sts2.Core.Hooks.Hook
// Signature: public static System.Threading.Tasks.Task BeforeCardPlayed(
//                MegaCrit.Sts2.Core.Combat.ICombatState combatState,
//                MegaCrit.Sts2.Core.Entities.Cards.CardPlay cardPlay)
[HarmonyPatch(typeof(MegaCrit.Sts2.Core.Hooks.Hook), nameof(MegaCrit.Sts2.Core.Hooks.Hook.BeforeCardPlayed))]
internal static class Patch_BeforeCardPlayed
{
    // Postfix, ref __result — no `return false`, no reverse patch, no recursion.
    private static void Postfix(
        MegaCrit.Sts2.Core.Combat.ICombatState combatState,
        MegaCrit.Sts2.Core.Entities.Cards.CardPlay cardPlay,
        ref System.Threading.Tasks.Task __result)
    {
        __result = Wrap(__result, cardPlay);
    }

    private static async System.Threading.Tasks.Task Wrap(
        System.Threading.Tasks.Task inner,
        MegaCrit.Sts2.Core.Entities.Cards.CardPlay cardPlay)
    {
        await inner;                                   // run all vanilla listeners first
        if (!cardPlay.IsFirstInSeries) return;          // one delay per card command, not per repetition
        await MegaCrit.Sts2.Core.Commands.Cmd.CustomScaledWait(0.30f, 0.60f);  // Godot-frame-driven, pausable
    }
}
```

**Harmony kind: Postfix with `ref Task __result`.**
*Rationale:* `OnPlayWrapper` `await`s this exact call inside the `for` loop **after** energy is
spent (`SpendResources`) and **immediately before** `cardModel.OnPlay(...)` — so the returned
wrapped `Task` inserts a genuine, awaited presentation pause at the exact requested seam; because
the code is deterministic and shared, **every peer in MP pauses identically (no desync)**.
A Postfix that *replaces* `__result` avoids the async-Prefix problem (a Prefix cannot `await`) and
avoids `HarmonyReversePatch` recursion. Use `Cmd.CustomScaledWait` (or `Cmd.Wait`) rather than
`Task.Delay` so the wait respects game pause and the animation-speed setting.

### 🥈 Option 2 — Prefix `Hook.BeforeCardPlayed` returning `false` + a `HarmonyReversePatch` of the original

```csharp
// Prefix: return false, set __result to a Task that waits then calls the reverse-patched original.
[HarmonyReversePatch]
[HarmonyPatch(typeof(Hook), nameof(Hook.BeforeCardPlayed))]
private static System.Threading.Tasks.Task Original_BeforeCardPlayed(ICombatState combatState, CardPlay cardPlay)
    => throw new System.NotImplementedException();
```

**Harmony kind: Prefix (`return false`) + ReversePatch.**
*Rationale:* only needed if you must run **before** vanilla listeners, or if a vanilla listener
itself must be delayed. Strictly more code and more invasive than Option 1 — pick it only when the
ordering inside `BeforeCardPlayed` matters. (Do **not** call the original via
`AccessTools.Method(...).Invoke(...)`: that re-enters the Harmony detour and recurses forever.)

### 🥉 Option 3 — visual-only delay in the client layer (no logic delay)

```csharp
// Target: MegaCrit.Sts2.Core.Models.CardModel
// Signature: private System.Threading.Tasks.Task PlayPowerCardFlyVfx()   /   NCard.AnimMultiCardPlay()
// or the client-only queue widget: MegaCrit.Sts2.Core.Nodes.Combat.NCardPlayQueue
//   public void UpdateCardBeforeExecution(PlayCardAction playCardAction)     (sync void -> cannot await)
```

**Harmony kind: Prefix on `NCardPlayQueue.UpdateCardBeforeExecution` / Postfix on
`NCardPlayQueue.OnLocalCardPlayed`, or Transpiler on `CardModel.<OnPlayWrapper>d__339.MoveNext`.**
*Rationale:* use this only if you deliberately want a **local-visual-only** effect (card sits in
the play queue longer) while damage still resolves on schedule. `NCardPlayQueue` has no
`async Task` method at all, so a Prefix cannot await here — you would need a Transpiler or a
coroutine started from a sync Prefix, which is why Option 1 is far cleaner. **Not recommended
for the stated goal** ("delay the visual *and* logical resolution").

---

## Appendix — raw evidence files (scratch)

| File | Contents |
|---|---|
| `.recon/CardPlay.txt` | full `CardPlay` type |
| `.recon/Hook-members.txt` | all `Hook` members (reflection names, 150 entries) |
| `.recon/m-Hook-BeforeCardPlayed.txt` / `sm-BeforeCardPlayed.txt` | declared sig + `MoveNext` body |
| `.recon/m-Hook-AfterCardPlayed.txt` / `sm-AfterCardPlayed.txt` | declared sig + two-pass `MoveNext` body |
| `.recon/m-Hook-ShouldPlay.txt` | full `ShouldPlay` body |
| `.recon/m-Hook-Attacks.txt` | `BeforeAttack` / `AfterAttack` / `AfterDamageGiven` |
| `.recon/t-PlayCardAction.txt` | full `PlayCardAction` incl. `<ExecuteAction>d__25` |
| `.recon/m-CardModel-OnPlayWrapper-MoveNext.txt` | full 769-line `OnPlayWrapper` body |
| `.recon/t-NCardPlayQueue.txt`, `.recon/t-NCardPlay.txt`, `.recon/M-NMouseCardPlay.txt` | client layer |
| `.recon/t-ActionQueueSynchronizer.txt`, `.recon/t-NetPlayCardAction.txt` | multiplayer replication |
| `.recon/t-ModHelper.txt`, `.recon/t-Delegates.txt` | mod hook subscription API |
