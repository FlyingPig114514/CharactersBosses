# Slay the Spire 2 Mod Development Rules

## Project Structure

```
src/
├── ModEntry.cs              # Mod entry — only modify ModId and ModName, do not change logic
├── Hooks/
│   ├── CombatHooks.cs       # Combat-related Hooks
│   ├── MapHooks.cs          # Map-related Hooks
│   └── ...                  # Split by feature module
├── <ModName>.csproj         # Project file — do not modify
└── <ModName>.json           # Mod manifest — id/has_dll/has_pck must be correct
```

## Mandatory Rules

### 1. ModEntry.cs Must Not Be Modified
- The `Initialize()` method and `Logger` class code logic **cannot be modified**
- Only `ModId` and `ModName` constants can be changed
- `ModId` must match the manifest `id`, and equal the folder name/DLL filename

### 2. Hook Files Split by Feature
- Create multiple `.cs` files in `src/Hooks/` directory
- Each file handles one feature module (e.g., `CombatHooks.cs`, `MapHooks.cs`, `RewardHooks.cs`)
- Single file must not exceed 200 lines; split further if needed
- Filenames use PascalCase, ending with `Hooks`

### 3. Hook Implementation
- Use `[HarmonyPatch]` + `Postfix`/`Prefix` for injection
- Must use `Logger.Log()` to record key operations
- Every method signature must be verified via ILSpy MCP decompilation, **guessing signatures is forbidden**
- Postfix method names for different Hooks cannot duplicate (compilation error)
- Recommend using nested classes to wrap patches for same-named methods (avoid method name conflicts)

### 4. Manifest Rules
- `id` must equal the folder name, DLL filename, and PCK filename
- `has_dll: true` and `has_pck: true` must be set, otherwise the game won't load
- `mod_manifest.json` does not need an `id` field (game will error but it's harmless)
- Game loads DLL from path `{mod_folder}/{id}.dll`

### 5. Hook Target Class
All Hooks are defined on the `MegaCrit.Sts2.Core.Hooks.Hook` static class, which is a virtual method placeholder class that Harmony patches to inject logic.

### 6. Value Modification Hooks
When modifying return values, use `ref` parameter:

```csharp
[HarmonyPatch(typeof(MegaCrit.Sts2.Core.Hooks.Hook), nameof(MegaCrit.Sts2.Core.Hooks.Hook.ModifyDamage))]
public static void Postfix(ref decimal __result)
{
    __result *= 2;
}
```

Parameter signatures must match ILSpy MCP decompilation results.

### 7. Event Notification Hooks
No return value, only used for receiving notifications:

```csharp
[HarmonyPatch(typeof(MegaCrit.Sts2.Core.Hooks.Hook), nameof(MegaCrit.Sts2.Core.Hooks.Hook.AfterCardPlayed))]
public static void Postfix()
{
    Logger.Log("[Hook] AfterCardPlayed triggered");
}
```

## Prohibitions

- Do not modify `Initialize()` method and `Logger` class in `ModEntry.cs`
- Do not modify `<ModName>.csproj` (except GameDir, managed by install.ps1)
- Do not guess parameter signatures in Hook methods, must verify with ILSpy MCP
- Do not add NuGet package reference for Harmony (use game's bundled `0Harmony.dll`)
- Do not include malicious logic in code
- Do not pile all Hooks in a single file (must split by feature)

## Hook Classification Table

> Obtained by decompiling `MegaCrit.Sts2.Core.Hooks.Hook` via ILSpy MCP. 144 public methods total (1 private helper excluded).

### Event Notification Hooks (After/Before) — async Task, no return value

Used to receive notifications when game events occur. Inject with Postfix.

| Category | Hook Method | Parameters |
|----------|-------------|------------|
| **Turns** | `AfterPlayerTurnStart` | `CombatState, PlayerChoiceContext, Player` |
| | `BeforePlayPhaseStart` | `CombatState, Player` |
| | `BeforeTurnEnd` | `CombatState, CombatSide` |
| | `AfterTurnEnd` | `CombatState, CombatSide` |
| | `BeforeSideTurnStart` | `CombatState, CombatSide` |
| | `AfterSideTurnStart` | `CombatState, CombatSide` |
| | `AfterTakingExtraTurn` | `CombatState, Player` |
| **Cards** | `BeforeCardPlayed` | `CombatState, CardPlay` |
| | `AfterCardPlayed` | `CombatState, PlayerChoiceContext, CardPlay` |
| | `BeforeCardAutoPlayed` | `CombatState, CardModel, Creature?, AutoPlayType` |
| | `AfterCardDiscarded` | `CombatState, PlayerChoiceContext, CardModel` |
| | `AfterCardDrawn` | `CombatState, PlayerChoiceContext, CardModel, bool` |
| | `AfterCardEnteredCombat` | `CombatState, CardModel` |
| | `AfterCardExhausted` | `CombatState, PlayerChoiceContext, CardModel, bool` |
| | `AfterCardGeneratedForCombat` | `CombatState, CardModel, bool` |
| | `AfterCardChangedPiles` | `IRunState, CombatState?, CardModel, PileType, AbstractModel?` |
| | `AfterCardRetained` | `CombatState, CardModel` |
| | `BeforeCardRemoved` | `IRunState, CardModel` |
| | `BeforeHandDraw` | `CombatState, Player, PlayerChoiceContext` |
| | `AfterHandEmptied` | `CombatState, PlayerChoiceContext, Player` |
| | `AfterShuffle` | `CombatState, PlayerChoiceContext, Player` |
| | `AfterModifyingBlockAmount` | `CombatState, decimal, CardModel?, CardPlay?, IEnumerable<AbstractModel>` |
| | `AfterModifyingCardPlayCount` | `CombatState, CardModel, IEnumerable<AbstractModel>` |
| | `AfterModifyingCardRewardOptions` | `IRunState, IEnumerable<AbstractModel>` |
| | `AfterModifyingDamageAmount` | `IRunState, CombatState?, CardModel?, IEnumerable<AbstractModel>` |
| | `AfterModifyingHandDraw` | `CombatState, IEnumerable<AbstractModel>` |
| | `AfterModifyingHpLostBeforeOsty` | `IRunState, CombatState?, IEnumerable<AbstractModel>` |
| | `AfterModifyingHpLostAfterOsty` | `IRunState, CombatState?, IEnumerable<AbstractModel>` |
| | `AfterModifyingOrbPassiveTriggerCount` | `CombatState, OrbModel, IEnumerable<AbstractModel>` |
| | `AfterModifyingPowerAmountGiven` | `CombatState, IEnumerable<AbstractModel>, PowerModel` |
| | `AfterModifyingPowerAmountReceived` | `CombatState, IEnumerable<AbstractModel>, PowerModel` |
| | `AfterModifyingRewards` | `IRunState, IEnumerable<AbstractModel>` |
| **Combat** | `BeforeCombatStart` | `IRunState, CombatState?` |
| | `AfterCombatEnd` | `IRunState, CombatState?, CombatRoom` |
| | `AfterCombatVictory` | `IRunState, CombatState?, CombatRoom` |
| | `AfterActEntered` | `IRunState` |
| | `AfterCreatureAddedToCombat` | `CombatState, Creature` |
| **Damage/HP** | `BeforeAttack` | `CombatState, AttackCommand` |
| | `AfterAttack` | `CombatState, AttackCommand` |
| | `BeforeDamageReceived` | `PlayerChoiceContext, IRunState, CombatState?, Creature, decimal, ValueProp, Creature?, CardModel?` |
| | `AfterDamageGiven` | `PlayerChoiceContext, CombatState, Creature?, DamageResult, ValueProp, Creature, CardModel?` |
| | `AfterDamageReceived` | `PlayerChoiceContext, IRunState, CombatState?, Creature, DamageResult, ValueProp, Creature?, CardModel?` |
| | `AfterCurrentHpChanged` | `IRunState, CombatState?, Creature, decimal` |
| | `BeforeDeath` | `IRunState, CombatState?, Creature` |
| | `AfterDeath` | `IRunState, CombatState?, Creature, bool, float` |
| | `AfterDiedToDoom` | `CombatState, IReadOnlyList<Creature>` |
| **Block** | `AfterBlockGained` | `CombatState, Creature, decimal, ValueProp, CardModel?` |
| | `BeforeBlockGained` | `CombatState, Creature, decimal, ValueProp, CardModel?` |
| | `AfterBlockBroken` | `CombatState, Creature` |
| | `AfterBlockCleared` | `CombatState, Creature` |
| | `AfterPreventingBlockClear` | `CombatState, AbstractModel, Creature` |
| **Energy** | `AfterEnergyReset` | `CombatState, Player` |
| | `AfterEnergySpent` | `CombatState, CardModel, int` |
| | `BeforeFlush` | `CombatState, Player` |
| **Powers** | `BeforePowerAmountChanged` | `CombatState, PowerModel, decimal, Creature, Creature?, CardModel?` |
| | `AfterPowerAmountChanged` | `CombatState, PowerModel, decimal, Creature?, CardModel?` |
| | `AfterForge` | `CombatState, decimal, Player, AbstractModel?` |
| **Orbs** | `AfterOrbChanneled` | `CombatState, PlayerChoiceContext, Player, OrbModel` |
| | `AfterOrbEvoked` | `PlayerChoiceContext, CombatState, OrbModel, IEnumerable<Creature>` |
| | `AfterSummon` | `CombatState, PlayerChoiceContext, Player, decimal` |
| **Potions** | `BeforePotionUsed` | `IRunState, CombatState?, PotionModel, Creature?` |
| | `AfterPotionUsed` | `IRunState, CombatState?, PotionModel, Creature?` |
| | `AfterPotionDiscarded` | `IRunState, CombatState?, PotionModel` |
| | `AfterPotionProcured` | `IRunState, CombatState?, PotionModel` |
| **Economy** | `AfterGoldGained` | `IRunState, Player` |
| | `AfterStarsGained` | `CombatState, int, Player` |
| | `AfterStarsSpent` | `CombatState, int, Player` |
| **Map/Rooms** | `AfterMapGenerated` | `IRunState, ActMap, int` |
| | `BeforeRoomEntered` | `IRunState, AbstractRoom` |
| | `AfterRoomEntered` | `IRunState, AbstractRoom` |
| **Rewards** | `BeforeRewardsOffered` | `IRunState, Player, IReadOnlyList<Reward>` |
| | `AfterRewardTaken` | `IRunState, Player, Reward` |
| **Shop** | `AfterItemPurchased` | `IRunState, Player, MerchantEntry, int` |
| **Rest Site** | `AfterRestSiteHeal` | `IRunState, Player, bool` |
| | `AfterRestSiteSmith` | `IRunState, Player` |
| **Death Prevention** | `AfterPreventingDeath` | `IRunState, CombatState?, AbstractModel, Creature` |
| | `AfterOstyRevived` | `CombatState, Creature` |
| **Draw Prevention** | `AfterPreventingDraw` | `CombatState, AbstractModel` |

### Value Modification Hooks (Modify) — has return value, use ref __result

Modify game values. Use `ref __result` or `out` parameters to return modified values.

| Hook Method | Return Type | Key Parameters |
|-------------|------------|----------------|
| `ModifyAttackHitCount` | `decimal` | `CombatState, AttackCommand, int` |
| `ModifyBlock` | `decimal` | `CombatState, Creature, decimal, ValueProp, CardModel?, CardPlay?, out IEnumerable<AbstractModel>` |
| `ModifyCardBeingAddedToDeck` | `CardModel` | `IRunState, CardModel, out List<AbstractModel>` |
| `ModifyCardPlayCount` | `int` | `CombatState, CardModel, int, Creature?, out List<AbstractModel>` |
| `ModifyCardPlayResultPileTypeAndPosition` | `(PileType, CardPilePosition)` | `CombatState, CardModel, bool, ResourceInfo, PileType, CardPilePosition, out IEnumerable<AbstractModel>` |
| `ModifyCardRewardAlternatives` | `IEnumerable<AbstractModel>` | `IRunState, Player, CardReward, List<CardRewardAlternative>` |
| `ModifyCardRewardCreationOptions` | `CardCreationOptions` | `IRunState, Player, CardCreationOptions` |
| `TryModifyCardRewardOptions` | `bool` | `IRunState, Player, List<CardCreationResult>, CardCreationOptions, out List<AbstractModel>` |
| `ModifyCardRewardUpgradeOdds` | `decimal` | `IRunState, Player, CardModel, decimal` |
| `ModifyDamage` | `decimal` | `IRunState, CombatState?, Creature?, Creature?, decimal, ValueProp, CardModel?, ModifyDamageHookType, CardPreviewMode, out IEnumerable<AbstractModel>` |
| `ModifyEnergyCostInCombat` | `decimal` | `CombatState, CardModel, decimal` |
| `ModifyExtraRestSiteHealText` | `IReadOnlyList<LocString>` | `IRunState, Player, IReadOnlyList<LocString>` |
| `ModifyGeneratedMap` | `ActMap` | `IRunState, ActMap, int` |
| `ModifyGeneratedMapLate` | `ActMap` | `IRunState, ActMap, int` |
| `ModifyHandDraw` | `decimal` | `CombatState, Player, decimal, out IEnumerable<AbstractModel>` |
| `ModifyHealAmount` | `decimal` | `IRunState, CombatState?, Creature, decimal` |
| `ModifyHpLostBeforeOsty` | `decimal` | `IRunState, CombatState?, Creature, decimal, ValueProp, Creature?, CardModel?, out IEnumerable<AbstractModel>` |
| `ModifyHpLostAfterOsty` | `decimal` | `IRunState, CombatState?, Creature, decimal, ValueProp, Creature?, CardModel?, out IEnumerable<AbstractModel>` |
| `ModifyMaxEnergy` | `decimal` | `CombatState, Player, decimal` |
| `ModifyMerchantCardCreationResults` | `void` | `IRunState, Player, List<CardCreationResult>` |
| `ModifyMerchantCardPool` | `IEnumerable<CardModel>` | `IRunState, Player, IEnumerable<CardModel>` |
| `ModifyMerchantCardRarity` | `CardRarity` | `IRunState, Player, CardRarity` |
| `ModifyMerchantPrice` | `decimal` | `IRunState, Player, MerchantEntry, decimal` |
| `ModifyNextEvent` | `EventModel` | `IRunState, EventModel` |
| `ModifyOddsIncreaseForUnrolledRoomType` | `float` | `IRunState, RoomType, float` |
| `ModifyOrbPassiveTriggerCount` | `int` | `CombatState, OrbModel, int, out List<AbstractModel>` |
| `ModifyOrbValue` | `decimal` | `CombatState, Player, decimal` |
| `ModifyPowerAmountGiven` | `decimal` | `CombatState, PowerModel, Creature, decimal, Creature?, CardModel?, out IEnumerable<AbstractModel>` |
| `ModifyPowerAmountReceived` | `decimal` | `CombatState, PowerModel, Creature, decimal, Creature?, out IEnumerable<AbstractModel>` |
| `ModifyRestSiteHealAmount` | `decimal` | `IRunState, Creature, decimal` |
| `ModifyRestSiteOptions` | `IEnumerable<AbstractModel>` | `IRunState, Player, ICollection<RestSiteOption>` |
| `ModifyRestSiteHealRewards` | `IEnumerable<AbstractModel>` | `IRunState, Player, List<Reward>, bool` |
| `ModifyRewards` | `IEnumerable<AbstractModel>` | `IRunState, Player, List<Reward>, AbstractRoom?` |
| `ModifyShuffleOrder` | `void` | `CombatState, Player, List<CardModel>, bool` |
| `ModifyStarCost` | `decimal` | `CombatState, CardModel, decimal` |
| `ModifySummonAmount` | `decimal` | `CombatState, Player, decimal, AbstractModel?` |
| `ModifyUnblockedDamageTarget` | `Creature` | `CombatState, Creature, decimal, ValueProp, Creature?` |
| `ModifyUnknownMapPointRoomTypes` | `IReadOnlySet<RoomType>` | `IRunState, IReadOnlySet<RoomType>` |
| `ModifyXValue` | `int` | `CombatState, CardModel, int` |

### Conditional Gate Hooks (Should) — returns bool

Boolean gate hooks that control game logic. Return `true`/`false` to allow/deny a behavior.

| Hook Method | Purpose | Key Parameters |
|-------------|---------|----------------|
| `ShouldAddToDeck` | Allow adding to deck | `IRunState, CardModel, out AbstractModel?` |
| `ShouldAfflict` | Allow affliction | `CombatState, CardModel, AfflictionModel` |
| `ShouldAllowAncient` | Allow ancient event | `IRunState, Player, AncientEventModel` |
| `ShouldAllowHitting` | Allow hitting | `CombatState, Creature` |
| `ShouldAllowMerchantCardRemoval` | Allow shop card removal | `IRunState, Player` |
| `ShouldAllowSelectingMoreCardRewards` | Allow selecting more card rewards | `IRunState, Player, CardReward` |
| `ShouldAllowTargeting` | Allow targeting | `CombatState, Creature, out AbstractModel?` |
| `ShouldClearBlock` | Allow block clear | `CombatState, Creature, out AbstractModel?` |
| `ShouldCreatureBeRemovedFromCombatAfterDeath` | Remove from combat after death | `CombatState, Creature` |
| `ShouldDie` | Allow death | `IRunState, CombatState?, Creature, out AbstractModel?` |
| `ShouldDisableRemainingRestSiteOptions` | Disable rest site options | `IRunState, Player` |
| `ShouldDraw` | Allow draw | `CombatState, Player, bool, out AbstractModel?` |
| `ShouldEtherealTrigger` | Trigger ethereal | `CombatState, CardModel` |
| `ShouldFlush` | Allow flush/discard | `CombatState, Player` |
| `ShouldGainGold` | Allow gold gain | `IRunState, CombatState?, decimal, Player` |
| `ShouldGenerateTreasure` | Generate treasure | `IRunState, Player` |
| `ShouldGainStars` | Allow star gain | `CombatState, decimal, Player` |
| `ShouldPayExcessEnergyCostWithStars` | Pay excess energy with stars | `CombatState, Player` |
| `ShouldPlay` | Allow card play | `CombatState, CardModel, out AbstractModel?, AutoPlayType` |
| `ShouldPlayerResetEnergy` | Reset player energy | `CombatState, Player` |
| `ShouldProceedToNextMapPoint` | Proceed to next map point | `IRunState` |
| `ShouldProcurePotion` | Procure potion | `IRunState, CombatState?, PotionModel, Player` |
| `ShouldRefillMerchantEntry` | Refill merchant entry | `IRunState, MerchantEntry, Player` |
| `ShouldStopCombatFromEnding` | Stop combat from ending | `CombatState` |
| `ShouldTakeExtraTurn` | Take extra turn | `CombatState, Player` |
| `ShouldForcePotionReward` | Force potion reward | `IRunState, Player, RoomType` |
| `ShouldPowerBeRemovedOnDeath` | Remove power on death | `PowerModel` |
