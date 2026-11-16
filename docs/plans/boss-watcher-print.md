# 二层 1 号观者 Boss「印牌观」实现计划（用户 2026-11-16 设计）

> 状态：**已实现并装机**（v0.2.712，107/111 两个目标 0 错误，本地化 16 语言检查全绿）。
>
> 落地清单：
> · `Core/WatcherPrintScript.cs`（8 回合剧本 + 6～8 循环，能量 3/2/4/2/3/4/5/2）
> · `Powers/BossMasterRealityPower.cs`（特殊效果「操控现实」+ `TriggerPrintAsync` 手动印牌）
> · `Core/WatcherCards.cs`（5 张观者牌 + 3 张本体储君牌访问器）
> · `Core/BossDefs.cs`（`Watcher.ExtraVariants[1]`，Key `WATCHER_PRINT`）
> · `Monsters/CharacterBosses.cs`（`WatcherPrintBoss`，`TierAct => 1`）
> · `Encounters/CharBossEncounters.cs`（`WatcherPrintBossEncounter` + Normal + `TierEncounters`/`TierByCharacterAct`）
> · `Core/BossKeys.cs`（`WATCHER_PRINT` 登记 / `EncounterOf` / `DesignNames` / `RosterFor(1)` 追加 `WatcherRosterAct2`）
> · `Core/BossLineups.cs`（`WatcherPrintActive` / `ComputeWatcherPrint`）
> · `Core/BossIntentBuilder.cs`（义愤填膺 + 何人僭越 → 策略）
> · `Core/BossDamagePredictor.cs`（`PrintStrengthOf`：印牌带来的回合内 +力量进规划期）
> · `Core/BossInfoTable.cs`（能力面板）、`Core/DownfallCollectibleCompat.cs`（藏品 = 操控现实）
> · `content/localization/*`（16 语言 × 10 键：monster 名/标题/出牌、特殊效果正文(含恐惧版)、power 标题/正文、遭遇标题/败北文案）
>
> **待实机验证**：`fight WATCHER_PRINT_BOSS_ENCOUNTER`（或用二层正常流程）——
> ① 特殊效果「操控现实」：玩家生成牌被降级、非衍生牌加消耗；
> ② 她打改造现实/欺瞒现实时日志 `印牌（WATCHER-CARVE_REALITY）⇒ 创世之柱 +2 格挡、武器库 +1 力量`；
> ③ 第 6/7 回合「惩恶+」的预览带着武器库那 +1 力量；
> ④ 义愤填膺的意图是策略（红色下箭头）；
> ⑤ 面板「循环：6～8」。 

## 1. 用户原文（逐字）

```
2层1号观者boss印牌观
特殊效果：操控现实：观者通过色彩哲学家获得了武器库、创世之柱和何人僭越。每当你在战斗中生成卡牌时，
其会被降级；如果不是衍生牌，还会获得消耗。
遗物：至纯之水、涅奥的祝福、斗篷扣、浮木、水银沙漏
进阶8：升级创世之柱和心灵堡垒。进阶9：升级改造现实、供奉和义愤填膺
意图
1.武器库+、操控现实+、改造现实、惩恶+（没费）
2.奇迹、心灵堡垒、警惕、打击（没费）、惩恶+（没费）
3.暴怒+、惩恶+、创世之柱
4.供奉、义愤填膺、内心宁静
5.欺瞒现实、平安+、何人僭越、化拳为空（没费）
6.洗牌。改造现实、化拳为空、警惕、惩恶+（没费）
7.暴怒+、欺瞒现实、惩恶+、供奉、平安+
8.义愤填膺、内心宁静、打击（没费）
之后6～8循环。

拦掉原版的印牌
每当她打出改造现实和欺瞒现实时都会"印牌"，此时要触发创世之柱和武器库（如果她有）。记得让回合内的
加力量提前被伤害预览计算（这比如第6、7回合）

义愤填膺的意图应当显示为策略

面板：
循环：6～9   ← ⚠ 与剧本「之后 6～8 循环」不一致，待用户确认
单体能力：偏强 / 群攻能力：中 / 单体高压回合：3、7 / 群体高压回合：4、7 / 特殊效果类型：单体
群体减益能力：强
藏品：操控现实
```

## 2. 卡牌 id 对照（已全部核实）

### 2.1 观者模组的牌（B 版 lamali 的 id；A 版 Boninall 走 `WatcherCards.Card()` 双写）

| 中文 | lamali id | 类名 | 费 | 效果（反编译/本地化原文） |
|---|---|---|---|---|
| 改造现实 | `WATCHER-CARVE_REALITY` | `CarveReality` | 1 | 造成伤害 + **将一张「惩恶」加入手牌**（= 印牌） |
| 欺瞒现实 | `WATCHER-DECEIVE_REALITY` | `DeceiveReality` | 2 | 获得格挡 + **将一张「平安」加入手牌**（= 印牌） |
| 惩恶（衍生） | `WATCHER-SMITE` | `Smite` | 1 | 造成伤害（`CardRarity.Token`） |
| 平安 | `WATCHER-SAFETY` | `Safety` | 0 | 获得格挡 |
| 义愤填膺 | `WATCHER-INDIGNATION` | `Indignation` | 1 | 在愤怒 ⇒ 给所有敌人易伤；否则进入愤怒 |
| 内心宁静 | `WATCHER-INNER_PEACE` | `InnerPeace` | 1 | 在平静 ⇒ 抽牌；否则进入平静 |
| 供奉 | `WATCHER-CONSECRATE` | `Consecrate` | 0 | **对所有敌人造成伤害**（群体高压） |
| 心灵堡垒 | `WATCHER-MENTAL_FORTRESS` | `MentalFortress` | 1 | 每次转换姿态获得格挡（能力） |
| 操控现实（藏品/卡） | `WATCHER-MASTER_REALITY` | `MasterReality` | 1 | 能力：所有战斗中临时加入的牌被**升级** |
| 操控现实（power） | `WATCHER-MASTER_REALITY_POWER` | `MasterRealityPower` | — | 同上（模组自带；我们的特殊效果**另写一个相反的**） |
| 警惕 / 暴怒 / 打击 / 奇迹 / 化拳为空 | `WATCHER-VIGILANCE` / `-ERUPTION` / `-STRIKE_WATCHER` / `-MIRACLE` / `-EMPTY_FIST` | 已在本项目 `WatcherCards` 里 | 2 / 2(升级 1?) / 1 / 0(+1) / 1 | — |

### 2.2 本体的储君牌（`MegaCrit.Sts2.Core.Models.Cards.*`，她「通过色彩哲学家获得」）

| 中文 | 类 | 费 | 效果（反编译） |
|---|---|---|---|
| 武器库 | `Arsenal` | 1（Power/Rare） | 施加 `ArsenalPower(1)`：**每有一张牌在战斗中被生成 ⇒ 她 +1 力量** |
| 创世之柱 | `PillarOfCreation` | 1（Power/Uncommon） | 施加 `PillarOfCreationPower(2)`：**每生成一张牌 ⇒ 她 +2 格挡**（`Unpowered`） |
| 何人僭越 | `KnowThyPlace` | **0**（Skill/Common，目标 AnyEnemy） | 给目标 易伤 + 虚弱 |

> 这三张的触发点都是原版钩子 **`AfterCardGeneratedForCombat(CardModel card, Player? creator)`**。

## 3. 每回合能量（按 2.1 的费用 + 「（没费）」反推）

| 回合 | 牌 | 费用和 | 能量 | 打不出的（用户标了「没费」） |
|---|---|---|---|---|
| 1 | 武器库+ / 操控现实+ / 改造现实 / 惩恶+ | 1+1+1 = 3 | **3** | 惩恶+（差 1） |
| 2 | 奇迹 / 心灵堡垒 / 警惕 / 打击 / 惩恶+ | 0(+1)+1+2 = 3（奇迹 +1 ⇒ 共 4 花 3） | **2** | 打击、惩恶+ |
| 3 | 暴怒+ / 惩恶+ / 创世之柱 | 2+1+1 = 4 | **4** | — |
| 4 | 供奉 / 义愤填膺 / 内心宁静 | 0+1+1 = 2 | **2** | — |
| 5 | 欺瞒现实 / 平安+ / 何人僭越 / 化拳为空 | 2+0+0 = 2（化拳为空再 1 ⇒ 3） | **3** | 化拳为空（差 1） |
| 6 | 改造现实 / 化拳为空 / 警惕 / 惩恶+ | 1+1+2 = 4 | **4** | 惩恶+ |
| 7 | 暴怒+ / 欺瞒现实 / 惩恶+ / 供奉 / 平安+ | 2+2+1+0+0 = 5 | **5** | — |
| 8 | 义愤填膺 / 内心宁静 / 打击 | 1+1 = 2 | **2** | 打击 |

`BossScript.LoopFrom = 5`、`LoopLength = 3`（= 第 6～8 回合循环）。
`BlocksVanillaDraw = true`（手牌全由剧本给）。

## 4. 特殊效果「操控现实」= 新 Power（`BossMasterRealityPower`）

用户原文：「每当**你**在战斗中生成卡牌时，其会被**降级**；如果不是衍生牌，还会获得**消耗**。」
⇒ 这是**对玩家的削弱**：

```csharp
public override Task AfterCardGeneratedForCombat(CardModel card, Player? creator)
{
    // 只管真人玩家生成的牌（她自己的惩恶/平安不算）
    if (creator == null || creator == OwnerMonster()?.Shadow) return;
    if (card.IsUpgraded) card.DowngradeInternal();                 // 降级
    if (card.Rarity != CardRarity.Token) card.AddKeyword(CardKeyword.Exhaust);  // 非衍生牌 ⇒ 消耗
}
```
API 已确认：`CardModel.DowngradeInternal()` / `AddKeyword(CardKeyword.Exhaust)` / `CardRarity.Token`。
面板「特殊效果类型：单体」⇒ 描述里点明是「你」（每位玩家各自受影响）。

## 5. 「拦掉原版的印牌」+ 手动触发创世之柱/武器库

已有现成机制：`CharBossMonster.BlocksGeneratedCardsEntries` +
`Patches/SeveranceNoCardGenerationPatch`（正在打的那张牌的 entry 在这个集合里时，**拦掉它的印牌**）。

做法：
1. 把 `WATCHER-CARVE_REALITY`、`WATCHER-DECEIVE_REALITY` 加进这个集合 ⇒ 原版「将惩恶/平安加入手牌」被拦掉
   （剧本里本来就写了 惩恶+/平安+，不拦会多一份）。
2. 她打出这两张牌时，**由我们手动结算一次「一张牌被生成」**：
   - 有 `PillarOfCreationPower` ⇒ `CreatureCmd.GainBlock(她, Amount, Unpowered)`
   - 有 `ArsenalPower` ⇒ `PowerCmd.Apply<StrengthPower>(她, Amount)`
   （等价于原版 `AfterCardGeneratedForCombat` 的那两条；因为原版那条被我们拦掉了。）
3. 触发点放在 `CharBossMonster.PlayOne`（打完后按 entry 判断），并打一行日志
   `印牌：<牌> ⇒ 创世之柱 +N 格挡 / 武器库 +N 力量`。

## 6. 意图

- **义愤填膺 → 策略**（用户点明）：加进 `BossIntentBuilder` 的「强制减益/策略」名单。
  顺带：**何人僭越**（易伤+虚弱，0 费）也是策略。
- **回合内的加力量要提前进伤害预览**：她打 改造现实/欺瞒现实 时会因「武器库」+N 力量，
  之后同回合的 惩恶+（第 6、7 回合）伤害必须带上这笔。
  做法：规划期（`BossScriptPlanner`）在推演到印牌那一步时，若本场她有 `ArsenalPower`
  ⇒ 累加 `plannedStrength += Amount`，与现有「意图预补：<牌> 给她 N 点力量」同一条通道。

## 7. 接线清单（每处都很短）

| 位置 | 改动 |
|---|---|
| `Core/WatcherCards.cs` | 新增 `CarveReality` / `Smite` / `Indignation` / `InnerPeace` / `MasterReality` 访问器 + 三张本体储君牌的 `Type` |
| `Core/WatcherPrintScript.cs`（新） | `Deck()` / `Relics()` / `Build()`（8 回合 + 6～8 循环） |
| `Powers/BossMasterRealityPower.cs`（新） | 特殊效果「操控现实」 |
| `Core/BossDefs.cs` | `Watcher.ExtraVariants[1]` 加一套：Key `WATCHER_PRINT`、Deck/Relics/Script、`SpecialLoc = "WATCHER_BOSS.print_reality"`、`SpecialPower = typeof(BossMasterRealityPower)`、`BlocksVanillaDraw = true`、进阶表（8：创世之柱+心灵堡垒；9：改造现实+供奉+义愤填膺）；`Enabled = BossLineups.WatcherPrintActive` |
| `Monsters/CharacterBosses.cs` | `WatcherPrintBoss : WatcherBoss`：`TierAct => 1`、`PinnedVariantKey => "WATCHER_PRINT"` |
| `Encounters/CharBossEncounters.cs` | `WatcherPrintBossEncounter : CharBossEncounterBase<WatcherPrintBoss>`（`TierAct => 1`，图标暂用 `watcher_divinity`）+ `WatcherPrintBossNormalEncounter`；`TierEncounters` / `TierByCharacterAct` 接线 |
| `Core/BossKeys.cs` | `new("WATCHER_PRINT", 6, 1, 1, typeof(WatcherPrintBoss))`；`EncounterOf` 分支；`DesignNames["WATCHER_PRINT"] = "印牌观"`；`RosterFor(1)` 末尾 `Concat(WatcherRosterAct2())`（`["WATCHER_PRINT"]`，同样走 `BossPicksSync.WatcherAllowedHere()` 闸门） |
| `Core/BossLineups.cs` | `WatcherPrintActive()`（`Latched`，判据 = 本场遭遇是 `WATCHER_PRINT_BOSS_ENCOUNTER` 或怪物 `PinnedVariantKey`） |
| `Core/BossIntentBuilder.cs` | 义愤填膺 / 何人僭越 → 策略；武器库的回合内力量进规划期预补 |
| `Core/BossInfoTable.cs` | 面板一行：循环 6～8（待确认）/ 单体偏强 / 群攻中 / 单体高压 3、7 / 群体高压 4、7 / 特殊效果类型 单体 / 群体减益 强 |
| 藏品「操控现实」 | 掉落表 (观者, 幕 1) → `WATCHER-MASTER_REALITY` 卡 |
| 本地化 16 语言 | `WATCHER_PRINT_BOSS.name/.title`、`WATCHER_BOSS.print_reality`（monsters 表）、`BOSS_MASTER_REALITY_POWER.title/.description`（powers 表）、`WATCHER_PRINT_BOSS_ENCOUNTER.*`（encounters 表） |

## 8. 待用户确认（两处）

1. **循环**：剧本写「之后 6～8 循环」，面板写「循环：6～9」。按剧本 = 6～8（本计划采用）。
2. **血量**：未给。观者基础 72，且她**没有布质果实** ⇒ 定义值按 72（第二幕实际 = 72 + 涅奥的祝福那份）。
