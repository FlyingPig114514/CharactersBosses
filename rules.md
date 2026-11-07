# 杀戮尖塔 2 Mod 开发规范

## 项目结构

```
src/
├── ModEntry.cs              # Mod 入口 — 只修改 ModId 和 ModName，不要改动逻辑
├── Hooks/
│   ├── CombatHooks.cs       # 战斗相关 Hook
│   ├── MapHooks.cs          # 地图相关 Hook
│   └── ...                  # 按功能模块拆分
├── <ModName>.csproj         # 项目文件 — 不要修改
└── <ModName>.json           # Mod 清单 — id/has_dll/has_pck 必须正确
```

## 必须遵守的规则

### 1. ModEntry.cs 禁止修改
- `Initialize()` 方法和 `Logger` 类的代码逻辑**不可修改**
- 只允许修改 `ModId` 和 `ModName` 两个常量
- `ModId` 必须与 manifest 的 `id` 一致，且等于文件夹名/DLL 文件名

### 2. Hook 文件按功能拆分
- 在 `src/Hooks/` 目录下创建多个 `.cs` 文件
- 每个文件负责一个功能模块（如 `CombatHooks.cs`、`MapHooks.cs`、`RewardHooks.cs`）
- 单个文件不超过 200 行，超过时必须继续拆分
- 文件名用 PascalCase，以 `Hooks` 结尾

### 3. Hook 写法
- 使用 `[HarmonyPatch]` + `Postfix`/`Prefix` 注入
- 必须用 `Logger.Log()` 记录关键操作
- 每个方法签名必须通过 ILSpy MCP 反编译验证，**禁止猜测签名**
- 不同 Hook 的 Postfix 方法名不能重复（编译报错）
- 建议用嵌套类包裹同名方法的 Patch（避免方法名冲突）

### 4. Manifest 清单规则
- `id` 必须等于文件夹名、DLL 文件名、PCK 文件名
- `has_dll: true` 和 `has_pck: true` 必须设置，否则游戏不会加载
- `mod_manifest.json` 不需要 `id` 字段（游戏会报错但无害）
- 游戏从路径 `{mod_folder}/{id}.dll` 加载 DLL

### 5. Hook 目标类
所有 Hook 定义在 `MegaCrit.Sts2.Core.Hooks.Hook` 静态类上，这是一个虚方法占位符类，Harmony 对它们打补丁以注入逻辑。

### 6. 数值修改类 Hook
修改返回值时，使用 `ref` 参数：

```csharp
[HarmonyPatch(typeof(MegaCrit.Sts2.Core.Hooks.Hook), nameof(MegaCrit.Sts2.Core.Hooks.Hook.ModifyDamage))]
public static void Postfix(ref decimal __result)
{
    __result *= 2;
}
```

参数签名必须与 ILSpy MCP 反编译结果一致。

### 7. 事件通知类 Hook
无返回值，仅用于接收通知：

```csharp
[HarmonyPatch(typeof(MegaCrit.Sts2.Core.Hooks.Hook), nameof(MegaCrit.Sts2.Core.Hooks.Hook.AfterCardPlayed))]
public static void Postfix()
{
    Logger.Log("[Hook] AfterCardPlayed triggered");
}
```

## 禁止事项

- 禁止修改 `ModEntry.cs` 的 `Initialize()` 方法和 `Logger` 类
- 禁止修改 `<ModName>.csproj`（GameDir 除外，由 install.ps1 自动管理）
- 禁止在 Hook 方法中猜测参数签名，必须用 ILSpy MCP 验证
- 禁止添加 NuGet 包引用 Harmony（使用游戏自带的 `0Harmony.dll`）
- 禁止在代码中包含恶意逻辑
- 禁止将所有 Hook 堆积在单个文件中（必须按功能拆分）

## Hook 分类表

> 通过 ILSpy MCP 反编译 `MegaCrit.Sts2.Core.Hooks.Hook` 获取，共 144 个公开方法（1 个私有辅助方法不计）。

### 事件通知类（After/Before）— 异步 Task，无返回值

用于在游戏事件发生时接收通知，通常用 Postfix 注入。

| 分类 | Hook 方法 | 参数 |
|------|----------|------|
| **回合** | `AfterPlayerTurnStart` | `CombatState, PlayerChoiceContext, Player` |
| | `BeforePlayPhaseStart` | `CombatState, Player` |
| | `BeforeTurnEnd` | `CombatState, CombatSide` |
| | `AfterTurnEnd` | `CombatState, CombatSide` |
| | `BeforeSideTurnStart` | `CombatState, CombatSide` |
| | `AfterSideTurnStart` | `CombatState, CombatSide` |
| | `AfterTakingExtraTurn` | `CombatState, Player` |
| **卡牌** | `BeforeCardPlayed` | `CombatState, CardPlay` |
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
| **战斗** | `BeforeCombatStart` | `IRunState, CombatState?` |
| | `AfterCombatEnd` | `IRunState, CombatState?, CombatRoom` |
| | `AfterCombatVictory` | `IRunState, CombatState?, CombatRoom` |
| | `AfterActEntered` | `IRunState` |
| | `AfterCreatureAddedToCombat` | `CombatState, Creature` |
| **伤害/生命** | `BeforeAttack` | `CombatState, AttackCommand` |
| | `AfterAttack` | `CombatState, AttackCommand` |
| | `BeforeDamageReceived` | `PlayerChoiceContext, IRunState, CombatState?, Creature, decimal, ValueProp, Creature?, CardModel?` |
| | `AfterDamageGiven` | `PlayerChoiceContext, CombatState, Creature?, DamageResult, ValueProp, Creature, CardModel?` |
| | `AfterDamageReceived` | `PlayerChoiceContext, IRunState, CombatState?, Creature, DamageResult, ValueProp, Creature?, CardModel?` |
| | `AfterCurrentHpChanged` | `IRunState, CombatState?, Creature, decimal` |
| | `BeforeDeath` | `IRunState, CombatState?, Creature` |
| | `AfterDeath` | `IRunState, CombatState?, Creature, bool, float` |
| | `AfterDiedToDoom` | `CombatState, IReadOnlyList<Creature>` |
| **格挡** | `AfterBlockGained` | `CombatState, Creature, decimal, ValueProp, CardModel?` |
| | `BeforeBlockGained` | `CombatState, Creature, decimal, ValueProp, CardModel?` |
| | `AfterBlockBroken` | `CombatState, Creature` |
| | `AfterBlockCleared` | `CombatState, Creature` |
| | `AfterPreventingBlockClear` | `CombatState, AbstractModel, Creature` |
| **能量** | `AfterEnergyReset` | `CombatState, Player` |
| | `AfterEnergySpent` | `CombatState, CardModel, int` |
| | `BeforeFlush` | `CombatState, Player` |
| **力量** | `BeforePowerAmountChanged` | `CombatState, PowerModel, decimal, Creature, Creature?, CardModel?` |
| | `AfterPowerAmountChanged` | `CombatState, PowerModel, decimal, Creature?, CardModel?` |
| | `AfterForge` | `CombatState, decimal, Player, AbstractModel?` |
| **球体** | `AfterOrbChanneled` | `CombatState, PlayerChoiceContext, Player, OrbModel` |
| | `AfterOrbEvoked` | `PlayerChoiceContext, CombatState, OrbModel, IEnumerable<Creature>` |
| | `AfterSummon` | `CombatState, PlayerChoiceContext, Player, decimal` |
| **药水** | `BeforePotionUsed` | `IRunState, CombatState?, PotionModel, Creature?` |
| | `AfterPotionUsed` | `IRunState, CombatState?, PotionModel, Creature?` |
| | `AfterPotionDiscarded` | `IRunState, CombatState?, PotionModel` |
| | `AfterPotionProcured` | `IRunState, CombatState?, PotionModel` |
| **经济** | `AfterGoldGained` | `IRunState, Player` |
| | `AfterStarsGained` | `CombatState, int, Player` |
| | `AfterStarsSpent` | `CombatState, int, Player` |
| **地图/房间** | `AfterMapGenerated` | `IRunState, ActMap, int` |
| | `BeforeRoomEntered` | `IRunState, AbstractRoom` |
| | `AfterRoomEntered` | `IRunState, AbstractRoom` |
| **奖励** | `BeforeRewardsOffered` | `IRunState, Player, IReadOnlyList<Reward>` |
| | `AfterRewardTaken` | `IRunState, Player, Reward` |
| **商店** | `AfterItemPurchased` | `IRunState, Player, MerchantEntry, int` |
| **休息站** | `AfterRestSiteHeal` | `IRunState, Player, bool` |
| | `AfterRestSiteSmith` | `IRunState, Player` |
| **死亡阻止** | `AfterPreventingDeath` | `IRunState, CombatState?, AbstractModel, Creature` |
| | `AfterOstyRevived` | `CombatState, Creature` |
| **抽牌阻止** | `AfterPreventingDraw` | `CombatState, AbstractModel` |

### 数值修改类（Modify）— 有返回值，用 ref __result

修改游戏数值，需要用 `ref __result` 或 `out` 参数返回修改后的值。

| Hook 方法 | 返回类型 | 关键参数 |
|----------|---------|---------|
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

### 条件控制类（Should）— 返回 bool

控制游戏逻辑的布尔门控，返回 `true`/`false` 决定是否允许某行为。

| Hook 方法 | 用途 | 关键参数 |
|----------|------|---------|
| `ShouldAddToDeck` | 是否允许加入卡组 | `IRunState, CardModel, out AbstractModel?` |
| `ShouldAfflict` | 是否施加负面效果 | `CombatState, CardModel, AfflictionModel` |
| `ShouldAllowAncient` | 是否允许古代事件 | `IRunState, Player, AncientEventModel` |
| `ShouldAllowHitting` | 是否允许命中 | `CombatState, Creature` |
| `ShouldAllowMerchantCardRemoval` | 是否允许商店删卡 | `IRunState, Player` |
| `ShouldAllowSelectingMoreCardRewards` | 是否允许选择更多卡牌奖励 | `IRunState, Player, CardReward` |
| `ShouldAllowTargeting` | 是否允许选择目标 | `CombatState, Creature, out AbstractModel?` |
| `ShouldClearBlock` | 是否清除格挡 | `CombatState, Creature, out AbstractModel?` |
| `ShouldCreatureBeRemovedFromCombatAfterDeath` | 死亡后是否移出战斗 | `CombatState, Creature` |
| `ShouldDie` | 是否死亡 | `IRunState, CombatState?, Creature, out AbstractModel?` |
| `ShouldDisableRemainingRestSiteOptions` | 是否禁用剩余休息选项 | `IRunState, Player` |
| `ShouldDraw` | 是否抽牌 | `CombatState, Player, bool, out AbstractModel?` |
| `ShouldEtherealTrigger` | 是否触发虚无 | `CombatState, CardModel` |
| `ShouldFlush` | 是否弃牌 | `CombatState, Player` |
| `ShouldGainGold` | 是否获得金币 | `IRunState, CombatState?, decimal, Player` |
| `ShouldGenerateTreasure` | 是否生成宝箱 | `IRunState, Player` |
| `ShouldGainStars` | 是否获得星星 | `CombatState, decimal, Player` |
| `ShouldPayExcessEnergyCostWithStars` | 是否用星星支付多余能量 | `CombatState, Player` |
| `ShouldPlay` | 是否允许出牌 | `CombatState, CardModel, out AbstractModel?, AutoPlayType` |
| `ShouldPlayerResetEnergy` | 是否重置玩家能量 | `CombatState, Player` |
| `ShouldProceedToNextMapPoint` | 是否推进到下一地图点 | `IRunState` |
| `ShouldProcurePotion` | 是否获取药水 | `IRunState, CombatState?, PotionModel, Player` |
| `ShouldRefillMerchantEntry` | 是否补充商店物品 | `IRunState, MerchantEntry, Player` |
| `ShouldStopCombatFromEnding` | 是否阻止战斗结束 | `CombatState` |
| `ShouldTakeExtraTurn` | 是否额外回合 | `CombatState, Player` |
| `ShouldForcePotionReward` | 是否强制药水奖励 | `IRunState, Player, RoomType` |
| `ShouldPowerBeRemovedOnDeath` | 死亡时是否移除力量 | `PowerModel` |
