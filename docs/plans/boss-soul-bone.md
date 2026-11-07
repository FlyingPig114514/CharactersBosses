# 二层 Boss「灵魂骨」（亡灵契约师）— 设计与实现

> 用户口述（2026-10-23）：「暂时先不搞联机（不用每次打包）。继续做新boss。2层boss灵魂骨 … 然后暂时把2层boss固定为她」
> 本文件是这一轮的唯一权威记录：设计原文 → 落到哪几个文件 → 实测要盯什么 → 还没定的地方。

## 1. 设计原文（逐条照抄）

- **特殊效果：灵魂绑定** —— 你在回合内抽牌时也视为亡灵契约师抽牌（会增加「自动化」的计数和「死亡行军」的伤害）。
- **遗物**：缚魂命匣、涅奥的祝福、战纹涂料、灵体外质、准备背包、腰带扣。
- **进阶 8**：升级「死亡之舞」「违逆」；**进阶 9**：升级「死亡行军」「切断」。
- **逐回合出牌**（`+` = 升级版；括号里是用户给的条件）：

| 回合 | 卡 | 备注 |
|---|---|---|
| 1 | 拖延+、自动化+、挽歌、切断、防御 | 切断/防御「没费打不了」 |
| 2 | 守墓人+、死者苏生、死亡行军 | |
| 3 | 巨镰（打 23）、违逆、羞耻 | 羞耻是诅咒，打不出 |
| 4 | 灵魂、鬼火、坟冢爆射+（捞切断）、切断、灵魂、灵魂风暴、忧郁、防御 | 灵魂风暴/忧郁/防御 = 有费就打 |
| 5 | 吞噬生命+、灵魂、灵魂、增生、死亡之舞、出击、防御 | 出击/防御 = 有费就打 |
| 6 | 洗牌。切断、灵魂、死亡行军、忧郁、防御、羞耻 | 忧郁/防御 = 有费就打 |
| 7 | 守墓人+、拖延+、灵魂、灵魂风暴、防御 | 灵魂风暴/防御 = 有费就打 |
| 8 | 灵魂、灵魂、违逆、增生、坟冢爆射+、忧郁、出击、防御 | 忧郁/出击/防御 = 有费就打 |
| — | **之后 6～8 循环** | `BossScript.LoopFrom = 6`、`LoopLength = 3` |

「有费就打」不需要额外开关：`BossScriptPlanner` 按剧本顺序出牌、费不够就跳过 ⇒ 天然等于「有费就打」。

## 2. 代码落点

| 内容 | 文件 | 关键点 |
|---|---|---|
| 剧本 / 卡组 / 遗物 / 逐回合能量 | `src/Hooks/Core/SoulBoneScript.cs` | `Build()`（8 回合 + 6～8 循环）、`Deck()`（21 张）、`Relics()`（5 件，涅奥的祝福由 `CarriesNeowBlessing` 自动带）；**能量不写死**（靠「拖延+」的下回合 +2，见 §5.1） |
| 「下回合 +N 能量」真的兑现 | `Core/BossDefinition.cs`（`HonorsNextTurnEnergyActs`）+ `Core/BossDefs.cs`（亡灵契约师 `{2}`）+ `Monsters/CharBossMonster.Turn.cs`（`PendingNextTurnEnergyCredit`） | 只对声明过的幕生效；其余保持「只删不发」 |
| **X 费牌的规划记账** | `Core/BossScriptPlanner.cs`（`IsXCost` + `cost = 剩余能量`） | `CardEnergyCost.GetAmountToSpend()` 对 X 费返回**当前** `pcs.Energy`（规划期 = 满的），而真正花掉的是打出那一刻的剩余能量 ⇒ 不修就会把「挽歌/根除」判成「费不够 3>1」永远不打 |
| 接进第二幕 | `src/Hooks/Core/BossDefs.cs`（`BossDefs.Necrobinder` 的第 2 列） | `DeckByAct[1]` / `RelicsByAct[1]` / `ScriptByAct[1]` / `SpecialLocByAct[1]` / `SpecialPowerByAct[1]`，并补 `AscensionUpgradesByAct` 的 `(8,2,DanseMacabre) (8,2,Defy) (9,2,DeathMarch) (9,2,Severance)` |
| 把第二层固定成她 | `src/Hooks/Encounters/CharBossEncounters.cs` → `ActTable[1]` | `{4,4,4}`（铸剑储君）→ **`{3,3,3}`**（亡灵契约师） |
| 特殊效果本体 | `src/Hooks/Powers/BossSoulBoundPower.cs` | 只有「标记 + 描述」，不带数值；`OwnerBoss(combat)` 给补丁找持有者 |
| 特殊效果实现 | `src/Hooks/Patches/SoulBoundDrawPatch.cs` | 前缀挂 `Hook.AfterCardDrawn`（原版抽牌路径：`History.CardDrawn` → `Hook.AfterCardDrawn`），见 §3 |
| **拦掉她的原版抽牌** | `src/Hooks/Patches/ShadowBossNoDrawPatch.cs` | 前缀切 `CardPileCmd.DrawInternal` 与 `DrawWithoutBlockingOnOtherPlayers`：**一张都不搬，但照记「她抽到 N 张」**（自动化计数 / 死亡行军增伤）。只有身上有「灵魂绑定」的 Boss 生效 ⇒ 只影响她 |
| **巨镰第 3 回合打 23** | `Core/BossScript.cs`（`ScriptCard.DamageOverride`）+ `BossScriptRunner.BuildHand` + `SoulBoneScript`（`C<TheScythe>(damage: 23)`） | 改的是**这一张实例**的 `DynamicVars.Damage.BaseValue` ⇒ 意图与实战都是 23（卡面基础 13 + 本局永久成长那套不受影响） |
| 图标 / 本地化 | `Patches/BossSpecialPowerIconPatch.cs`（借用原版「好奇」灯泡图标）、`content/localization/{zhs,eng}/monsters.json`（`NECROBINDER_BOSS.soul_bound`）、`powers.json`（`BOSS_SOUL_BOUND_POWER.title/description`） | 改了 content ⇒ **必须重打 PCK** |

## 3. 灵魂绑定为什么这么做（反编译证据）

```csharp
// 死亡行军 DeathMarch：倍率 = 本回合「她自己抽到几张」
new CalculatedDamageVar(ValueProp.Move).WithMultiplier((card, _) =>
    History.Entries.OfType<CardDrawnEntry>()
           .Count(e => e.HappenedThisTurn(card.CombatState)
                    && e.Actor == card.Owner.Creature      // 只数她自己
                    && !e.FromHandDraw));
// CardDrawnEntry(card, …) : base(card.Owner.Creature, …)   ⇒ Actor 就是牌的 Owner 生物
```

```csharp
// 自动化 AutomationPower.AfterCardDrawn：卡的主人 == 能力主人的 Player → 计数 −1，减到 0 给能量并重置 10
if (card.Owner == automationPower.Owner.Player) { data.cardsLeft--; … }
```

所以补丁做两件事（真人在玩家回合抽到牌时）：

1. `CombatManager.Instance.History.CardDrawn(combat, 她的某张牌, fromHandDraw)`
   —— 只为了让 `e.Actor` 指到她（死亡行军据此涨伤害）；
   **载体牌是只读借用的**（抽牌堆 → 弃牌堆 → 手牌 → 消耗堆里第一张），不移动、不抽走，手牌/牌堆一点不变。
2. 直接调她身上那一个 `AutomationPower.AfterCardDrawn(...)` —— **不调整个 `Hook`**，
   否则玩家身上的「混乱」等抽牌钩子会顺手动她牌堆里的牌（那是真改动）。

排除条件：影子 Player 的牌（`Creature.Monster != null`）不镜像 ⇒ 她自己抽牌不会被重复计算；
`_busy` 防重入；单人同样生效（这是她自己的效果，不是联机专属）。

### 3.1 她自己的「原版抽牌」被拦掉（用户 2026-10-23 追加）

用户原话：「把原版的抽牌拦了。目前我的意图里已经把抽牌提前算好了，再触发原版抽牌会乱。不过触发抽牌时
仍然要增加自动化计数和给死亡行军增伤，实现方法你可以去看看无色储君，我也是把他的印牌给拦了。目前就这一个 boss 这么改其它别动。」

- 手法与「无色储君拦印牌」（`ShadowBossNoRandomCardPatch` 拦 `CardFactory.GetDistinctForCombat`，返回空集合）同源：
  `ShadowBossNoDrawPatch` 在真正的抽牌入口上切一刀，返回**空集合 / 已完成的 Task**。
- 拦的两个入口（反编译确认）：
  `CardPileCmd.DrawInternal(ctx, count, player, fromHandDraw)`（`Draw(ctx,player)` / `Draw(ctx,count,player,…)` 都落到它）
  与 `CardPileCmd.DrawWithoutBlockingOnOtherPlayers(…)`（它自己另写了一遍搬牌 + 记历史的循环）。
- **拦了还记账**：`SoulBoundDrawPatch.CreditFakeDraw(...)` 按请求张数补 `CardDrawnEntry`（她当 Actor ⇒ 死亡行军涨伤害）
  并结算她的 `AutomationPower`（⇒ 自动化计数），牌一张都不搬 ⇒ 手牌/牌堆与剧本、意图完全一致。
- 闸门：`player.Creature.Monster is CharBossMonster` **且她身上有 `BossSoulBoundPower`** ⇒ 只有灵魂骨这么改，其它 Boss/玩家/原版怪物不动。

### 3.1.1 谁抽的牌算什么（用户 2026-10-23 订正，这是最终口径）

| 抽牌来源 | 自动化计数 | 死亡行军增伤 |
|---|---|---|
| 玩家**回合开始补手牌**（原版 `fromHandDraw: true`，见 `CombatManager.SetupPlayerTurn`） | ✗ 不算 | ✗ 不算 |
| 玩家**回合内额外抽牌**（`fromHandDraw == false`） | ✔ +1 | ✔ 当场 +1（预览随时涨） |
| 她**自己回合开始抽的牌**（每回合 `手牌 5 − 涅奥 2 = 3` 张） | ✔ +3 | ✗ 不算 |
| 她**自己回合内额外抽牌**（`灵魂` 抽 `Cards` 张，牌不搬） | ✔ | ✔ |

实现要点：
- 玩家回合内抽牌那一刻：`History.CardDrawn(...)`（记在**玩家这一边**的回合，所以**规划期/预览**能立刻读到）+ 自动化 −1 +
  `boss.NotePlayerInTurnDraw()`（累计，供她回合开始时折算）。
- 她回合开始（`BossHookBridge.SoulBoundTurnStart`）：
  ① `HerOwnDrawsThisTurn = 0`；② `FlushPendingDrawCredits` 把玩家那笔折算成**敌人回合这一侧**的
  `CardDrawnEntry`（`HappenedThisTurn` 要求「同回合 + 同一边 + 各玩家回合数没变」，玩家那侧的条目在敌人回合读不到）；
  ③ 她回合开始的 3 张只进自动化（`CreditAutomation`，不写历史）。
- 她回合内被拦掉的抽牌：写历史（敌人回合这一侧，当场就能被死亡行军读到）+ 自动化 + `NoteHerOwnInTurnDraw`。

### 3.1.2 死亡行军伤害预览（用户 2026-10-23）

- 公式（反编译 `DeathMarch`）：`8 + 4 × 本回合抽到的牌数`（`CalculationBase` + `ExtraDamage × multiplier`）。
- **玩家回合内抽牌 → 进「预补池」，不再写玩家侧历史**（2026-10-23 改）：
  玩家侧写的 `CardDrawnEntry` 在敌人回合读不到（`HappenedThisTurn` 要求 `CurrentSide` 相同），
  会造成「预览算了、实战没算」两套口径。现在：
  - 玩家每回合内额外抽 1 张 → `NotePlayerInTurnDraw()`：自动化照记 + `DrawCompensation += 每张数值`；
  - 她回合开始 `FlushPendingDrawCredits`：把这 N 张折算成**敌人侧**历史条目，同时把池子扣回同样的量
    （显示值连续、绝不重复）。
- **提前算她本回合回合内抽的牌**：`BladesGrowth.PlanCompensation` 按剧本顺序累计 `plannedDraws`
  （「灵魂」的 `Cards`），池子 = `每张数值 × 剧本待抽 + 每张数值 × PendingDrawCredits`。
- **玩家回合内抽牌时随时更新**：抽牌那一刻打标 `IntentsDirty`，帧驱动下一帧重算。

### 3.1.2b 「意图显示了要打的牌、最后没打」（用户 2026-10-23）

根因：计划是在**玩家回合里**算的，而有些牌**耗能会变** —— 实测是「忧郁 `Melancholy`：每当有任何生物死亡，
这张牌的耗能 −1」：玩家在回合里打死召唤物后它的耗能降了，计划还按旧值记账 ⇒ 计划以为打得起、实际打不出
（出牌前的资源兜底 `执行期资源不够` 会把它拦掉，日志实证）。
修法：`CharBossMonster.ReplanAtPlayStart()` —— 她**出牌阶段开始前**用**真实能量**按同一份剧本重新规划一次，
再冻结预补、刷新意图 ⇒ 显示与执行一致。**只对灵魂骨生效**（闸门 = 身上有 `BossSoulBoundPower`），别的 Boss 不动。

### 3.1.3 灵魂风暴伤害预览（用户 2026-10-23）

- 公式（反编译 `SoulStorm`）：`ExtraDamage(4，升级 6) × 消耗牌堆里的 Soul 张数`
  （`CalculatedDamageVar.WithMultiplier(card => card.Owner.PlayerCombatState.ExhaustPile.Cards.Count(c => c is Soul))`）。
- 「灵魂」（`Soul`）带 `CardKeyword.Exhaust`（0 费 Skill、Token）⇒ 她本回合前面每打出 1 张灵魂，
  消耗牌堆就多 1 张、灵魂风暴就 +4。
- 预览：`BladesGrowth.RecomputeAll` 按剧本顺序累计 `plannedSouls`（踩到 `SOUL` 就 +1），
  到 `SOUL_STORM` 时补 `ExtraDamage × max(0, plannedSouls − SoulsPlayedThisTurn)`；
  `CharBossMonster.PlayOne` 里每打完一张灵魂就 `SoulsPlayedThisTurn++`（由帧驱动触发的意图重算据此收回预补）。

### 3.1.4 「没费也出」的根因与兜底（用户 2026-10-23 报的 bug）
两个原因，都已修：

1. **规划期多算了能量**：`BossScriptPlanner` 里那条通用记账
   `energy += DynamicVars["Energy"]` 把「拖延」（下回合 +N）和「自动化」（每抽 10 张 +N）的
   `EnergyVar` 当成**立刻回能**，于是后面的牌被误判成打得起。
   现在 `DeferredEnergyCard(card)`（`DELAY` / `AUTOMATION`）跳过这条记账；
   鬼火 `Wisp` 那种 `PlayerCmd.GainEnergy(DynamicVars.Energy.IntValue, owner)` 是真回能，照旧记账。
2. **执行期没人挡**：`CardCmd.AutoPlay` **既不检查也不扣费**，能量是我们自己 `pcs.LoseEnergy(play.Cost)` 补的
   （`LoseEnergy` 还会把负数夹成 0）⇒ 规划一旦算多，她就「白打」一张。
   现在 `CharBossMonster.PlayOne` 在出牌前兜底：`play.Cost > pcs.Energy || play.StarCost > pcs.Stars`
   就跳过并写日志 `跳过 <牌>（执行期资源不够…）`，绝不白打。

### 3.2 巨镰第 3 回合伤害 = 23

`TheScythe` 基础伤害 13、本局永久成长（`Increase`），而剧本里她第 3 回合才第一次打 ⇒ 卡面只有 13。
新增 `ScriptCard.DamageOverride`：`BossScriptRunner.BuildHand` 造完牌后把**这一张实例**的
`DynamicVars.Damage.BaseValue` 写成 23（`RecalculateCardValues` 只重算附魔，不会覆盖它）⇒ 意图与实际伤害都是 23。

### 3.3 「回合结束时若在手牌」这一族**全部**要生效（用户 2026-10-23）

- 原版机制（反编译 `CombatManager.DoTurnEnd`）：
  ```csharp
  foreach (var c in player.PlayerCombatState.Hand.Cards)
      if (c.HasTurnEndInHandEffect) turnEndCards.Add(c);
  await DoTurnEndCards(turnState, turnEndCards, choiceContext);   // → card.OnTurnEndInHandWrapper(ctx)
  ```
  只对 **`CombatState.Players` 的手牌**派发 ⇒ 影子 Player 不在里面，她手牌里的这类牌**一个都不触发**
  （用户实测：羞耻不给脆弱 = `FrailPower`）。
- 修法（`CharBossMonster.RunCurseTurnEndHooks`，在她自己回合结束时**丢手牌之前**）：
  用**同一套判定** `card.HasTurnEndInHandEffect` 扫她的手牌，逐张调 `card.OnTurnEndInHandWrapper(ctx)`
  —— 调的是**每张牌自己的实现**，我们不复刻任何一条效果。
- 原版这一族的全部成员（`search OnTurnEndInHand` 反编译结果）：
  羞耻 `Shame`（+1 脆弱）、厄运 `BadLuck`、召唤 `Beckon`、灼烧 `Burn`、
  债务 `Debt`、腐朽 `Decay`、怀疑 `Doubt`、感染 `Infection`。
  以后原版再新增同类牌**自动生效**，不需要改代码。
- 日志：`回合结束手牌效果：<牌> 已对 NECROBINDER_BOSS 结算（…）`（每张一行，失败也单独记）。

### 3.4 出击的预览要提前算「吞噬生命 + 灵魂」给奥斯提加的生命（用户 2026-10-23）

- 原版公式（`BossDamagePredictor.OstyScaledDamage`）：
  `出击 = CalculationBase + ExtraDamage × 奥斯提「当前」生命`（+ 钙化 `CalcifyPower`），
  读的是**真实**当前生命 ⇒ 规划期那些召唤还没发生，预览必然偏低。
- 奥斯提生命的两类来源（反编译确认）：
  1. **召唤牌**：`Summon` 变量（增生 `Spur` 3、死者苏生 `Reanimate` 20、骸骨军团 6/8 …）。
     `OstyCmd.Summon`：已有奥斯提 → `CreatureCmd.GainMaxHp(osty, amount)`；没有 → `SetMaxHp + Heal`。
  2. **「灵魂」× 吞噬生命**（`DevourLifePower.AfterCardPlayed`）：
     `cardPlay.Card is Soul && Owner` → `OstyCmd.Summon(ctx, owner, power.Amount, power)` ⇒ 每打一张灵魂 +`Amount`。
- 实现：`BossDamagePredictor.OstyHpGainOf(card, boss)`（两类都算、含旧 `Osty*` 变量名兜底）；
  `BladesGrowth.PlanCompensation` 按剧本顺序走一遍，把「出击之前还没到账的生命」×1 冻结成
  `UnleashCompensation`；`CharBossMonster.PlayOne` 每打完一张牌就把真实的生命增量按同样的量撤回来
  （见 §4）。剧本规划器那一路也统一用同一个 helper。
- ⚠ **「吞噬生命」常常是同一回合先打出去的牌**（第 5 回合：吞噬生命+ → 灵魂 → 灵魂 → 增生 → … → 出击）。
  规划期这层能力还没上身 ⇒ 之前把它按 0 算了，两张灵魂各少 2 点（用户录屏实证：出击显示 36、实际 41）。
  现在 `PlanCompensation` 走剧本时维护一个 running `devour`：初值取她**当前**的 `DevourLifePower` 层数，
  走到 `DEVOUR_LIFE` 这张就用牌面值抬高（`BossDamagePredictor.DevourLifeCardAmount`，
  `PowerVar` 的 key = 类型名去掉 `Power`：`DevourLife`；读不到退回「未升级 1 / 升级 2」）。

## 4. 意图「预补池」：冻结 + 增加多少撤多少（用户 2026-10-23）

用户原话：「别让她的伤害在她出牌过程中来回闪，对她伤害意图的『补偿』应该在伤害确实增加后再撤回来，增加多少撤多少」。

- 做法：规划期把三份预补**一次算好并冻结**到 `CharBossMonster` 上
  （`DrawCompensation` / `SoulStormCompensation` / `UnleashCompensation` +
  每单位数值 `DeathMarchPerDraw` / `SoulStormPerSoul` / `UnleashPerOstyHp`，见
  `BladesGrowth.PlanCompensation`）；之后每有一笔真实增量到账，就用
  `WithdrawDrawCompensation` / `WithdrawSoulCompensation` / `WithdrawOstyCompensation`
  撤掉**同样的量** ⇒ 任何时刻「显示值 = 真实值 + 池子里剩的」。
- 撤回的时机（必须等真实效果**结算完**，即 `AutoPlay` 的 await 返回之后）：
  灵魂打出（消耗牌堆 +1）、奥斯提生命真的涨了、她真的抽到牌（被拦掉的抽牌记完账）。
- **她出牌过程中不再打 `IntentsDirty`**：她自己的这些增量不触发重算，
  重算只发生在「每张牌打出之前」和「回合结束时」这两个稳定点；
  `IntentsDirty` 只留给**玩家回合内抽牌**（那时她没有任何东西在飞，实时涨是安全的）。
- **回合开始再冻结一次**（`BladesGrowth.RefreshCompensation`，在 `FireTurnStart` 之后）：
  「缚魂命匣」这类「回合开始时给奥斯提加生命」是规划之后才到账的
  （实测规划期 ostyHp=27、她的回合 28），不重新冻结出击就会少 1 点。
- 诊断日志（只在数值真的变化时记一行，便于对实际伤害）：
  `意图数值 DEATH_MARCH: 显示 21（预补 12，段数 1，本回合抽牌条目=2）`、
  `意图数值 SOUL_STORM: 显示 N（预补 M，段数 1，消耗牌堆灵魂=K）`、
  `意图数值 UNLEASH: 显示 N（预补 M，段数 1，奥斯提生命=H）`。
- **录屏读法**：这台机器上 `C:\Users\<user>\AppData\Local\Programs\xiezuo\resources\ffmpeg\ffmpeg.exe`
  可以抽帧（这个 ffmpeg 没编 png 编码器，要用 `-c:v mjpeg` 出 jpg）：
  `& <ffmpeg> -i <mp4> -vf "fps=2,scale=1400:-1" -c:v mjpeg -q:v 2 <out>\%03d.jpg`。

## 5. 这些改动对**别的 Boss** 有没有影响（用户 2026-10-23 专门问过）

| 改动 | 影响面 | 结论 |
|---|---|---|
| 出牌末尾「有费就打」补打 | **只给灵魂骨**（`Creature.GetPower<BossSoulBoundPower>() != null`） | 其它 Boss 的剧本是照「没费就打不出来」写的（储君第三幕的防御、根除骨第 3 回合的洁净+、能力机的打击），对它们补打等于改设计 ⇒ 已加闸门 |
| `DeferredEnergyCard`（拖延/自动化的 `EnergyVar` 不算立刻回能） | 谁的剧本里有这两张牌 | 只有灵魂骨有 ⇒ 零影响 |
| `NoteAutomationDraws`（规划期预演自动化能量） | `HasSoulBound(boss)` 为真才跑 | 只有灵魂骨 ⇒ 零影响 |
| `IsXCost`（X 费牌 cost = 剩余能量） | 所有 Boss | 只有两家有 X 费牌：灵魂骨（挽歌）、根除骨（根除）。根除骨的根除永远排在 0/1 费牌之后且前面有「播种 +1」，算出来与旧值**完全相同**（钙化1−1+播种1 ⇒ 剩余=当时能量），所以她的回合表现不变 |
| 「回合结束若在手牌」一族（`RunCurseTurnEndHooks`） | 所有 Boss | 只对 `HasTurnEndInHandEffect` 的牌生效；其它 Boss 剧本里没有这一族牌（羞耻只在她剧本里），有的话触发也是**本该触发** |
| 出击的奥斯提生命预补（`UnleashCompensation`） | 所有 Boss（**用户 2026-10-23 定：「出击也都这样」**） | 修正性改动，全 Boss 通用：第一幕灾厄骨第 10 回合是「护卫（召唤 5）→ 出击」，旧代码按变量名 `OstyMaxHp` 找不到（那批变量名根本不存在）⇒ 预览偏低 5；现在按 `Summon` 变量算对。第 6 回合是「出击 → 护卫」，顺序相反所以不加（计划顺序决定，正确） |
| 出牌前资源兜底（`执行期资源不够`） | 所有 Boss | 只会「少打」不会「多打」，且其它 Boss 的能量记账本来没超支 |

## 6. 实测要盯的日志（`mod_log.txt`，前缀 `[CharBoss]`）

- `===== CharacterBosses 构建：v0.1.8 …` —— 确认装的是这一版。
- `特殊效果已施加: BossSoulBoundPower` —— 她身上挂上了。
- `灵魂绑定：… → 视为 NECROBINDER_BOSS 抽到 N 张（载体牌=…，不搬牌；自动化=N）` —— 抽牌记账生效
  （两个来源：真人抽牌 / 她自己的原版抽牌被拦）。
- `剧本第 N 回合: 打 [...]` + `跳过 [...]` —— 逐回合核对上面的表；第 1 回合应看到 `Severance(费不够 …)` / `DefendNecrobinder(费不够 …)`。
- `剧本伤害覆盖：THESCYTHE = 23` —— 第 3 回合那张巨镰的伤害被写死成 23。
- 状态框里应显示「灵魂绑定」+ 灯泡图标；顶栏 Boss 图标 = 亡灵契约师头像。

## 5. 还没定 / 需要用户确认

1. ~~逐回合能量~~ **已定（用户 2026-10-23：「拖延+ 会给下回合 2 费」）**：
   剧本**不写死任何能量**（`TurnEnergy` 全 0 = 用「定义值 + 遗物」），靠「拖延+」自己兑现下回合的 2 点：
   - 基础 3 点 = 3 − 涅奥的祝福 1（`NeowBlessing.EnergyPenalty`）+ 灵体外质 1（`Ectoplasm.ModifyMaxEnergy`）；
   - 第 1 回合 3 点：拖延+(2) + **自动化+(0)** + **挽歌(X=1)** ⇒ 正好花光，切断/防御 打不出 ✔
     （用户 2026-10-23 指出：`Automation` 基础 1 费、`OnUpgrade` 是 `EnergyCost.UpgradeBy(-1)` ⇒ **自动化+ = 0 费**；
      `Dirge.HasEnergyCostX = true`，X = 打出那一刻的剩余能量 ⇒ **挽歌以 1 费打出**（召唤 3×1、给抽牌堆 1 张灵魂）。）
   - 第 2 回合 3+2=5：守墓人+(1)+死者苏生(3)+死亡行军(1) ✔
   - 第 3 回合 3：巨镰(2)+违逆(1) ✔
   - 第 4～6 回合 3：只够必打的，标「有费就打」的自动打不出 ✔
   - 第 7 回合又打拖延+ ⇒ 第 8 回合 5 点 ✔
   实现：`BossDefinition.HonorsNextTurnEnergyActs`（只有亡灵契约师**第 2 幕**声明）+ `CharBossMonster.PendingNextTurnEnergyCredit()`
   （在算本回合能量那一处读 `EnergyNextTurnPower`，读到就加进预算并移除；原版靠 `AfterEnergyReset(Player)`，影子 Player 收不到）。
   其余幕/其余 Boss 保持老行为（`BossHookBridge.RemoveNextTurnPowers` 只删不发）。
2. ~~**巨镰「打 23」**~~ **已定（用户 2026-10-23：把第 3 回合巨镰伤害调到 23）**：
   用 `ScriptCard.DamageOverride = 23` 写死这一张实例的伤害（见 §3.2），意图与实际伤害都是 23。
3. **坟冢爆射（捞切断）**：`Graveblast` 是「从弃牌堆选一张牌进手」的选择牌，Boss 出牌时由
   `Core/BossCardSelector`（`CharBossMonster` 里 `Push()`）自动选，不会弹玩家的选择框 —— 但要实测确认它捞的是「切断」。
4. **灵魂绑定与同名卡**：原版亡灵契约师有一张卡也叫「灵魂绑定」（`Soulbound`），本效果的本地化 key 是
   `BOSS_SOUL_BOUND_POWER`，不与卡牌冲突。
