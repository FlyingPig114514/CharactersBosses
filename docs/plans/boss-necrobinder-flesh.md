# 一层亡灵契约师 2 号「血肉骨」（`NECROBINDER_FLESH`，v0.2.545）

> 用户设计（2026-11-10 口述，逐字原话保存在 `src/Hooks/Core/FleshBoneScript.cs` 的类注释里）。
> 代码是唯一权威；本文记「为什么这么写」和踩到的坑。

## 1. 落点

| 东西 | 文件 |
| --- | --- |
| 剧本 / 卡组 / 遗物 | `src/Hooks/Core/FleshBoneScript.cs` |
| 数值预测（血肉戏法 9 / 招架盾 6 / 格挡） | `src/Hooks/Core/FleshBone.cs` |
| 特殊效果「吞噬生命」 | `src/Hooks/Powers/BossDevourLifePower.cs` |
| 让血肉戏法对她生效 | `src/Hooks/Patches/SleightOfFleshBossPatch.cs` |
| 变体挂载 | `src/Hooks/Core/BossDefs.cs`（`Necrobinder.VariantsByAct[0]`） |
| 出阵 / 名字 / 遭遇 | `BossKeys.cs`、`BossLineups.FleshActive()`、`TierBossEncounters.cs`、`TierBossMonsters.Act1.cs` |
| 多人换牌 | `MultiplayerCardSwap.Table`（act 0：吸引仇恨 → 骸骨军团） |
| 藏品 | `DownfallCollectibleCompat`（命运同担 `SharedFate`） |
| 能力面板 | `BossInfoTable` |

## 2. 能量账（用户写的「没费」全对得上）

第一幕亡灵契约师 = 基础 3 − 涅奥的祝福 1 = **每回合 2**：

- 第 1 回合 血肉戏法(2) + 鬼火(0，**回 1 能量**) + 护卫(1) —— 靠鬼火那 1 点正好够；
- 第 5/7 回合 吸引仇恨(2)（多人 = 骸骨军团，同样 2 费）⇒ 后面两张没费；
- 第 6 回合 命运同担(0) + 打击 + 打击 = 2；
- 其余回合两张 1 费 + 一张没费。

循环 `LoopFrom = 7, LoopLength = 5`。

### 2.1 第四轮改稿后的剧本（v0.2.550，用户「削弱，撕碎换玷污」）

| 回合 | 出牌 | 能量账 |
| --- | --- | --- |
| 1 | 血肉戏法(2)、鬼火(0，回 1)、护卫(1) | 2 + 鬼火 1 |
| 2 | 出击(1)、恐惧+(1)、打击（没费） | 2 |
| 3 | 违逆(1)、防御(1)、苦难(0) | 2 |
| 4 | 玷污(1)、防御(1)、防御（没费） | 2 |
| 5 | 吸引仇恨(2)、摧残、打击（没费） | 2 |
| 6 | 打击(1)、防御(1)、命运同担(0) | 2 |
| 7 | 吸引仇恨(2)、打击×2（没费） | 2 |
| 8 | 出击(1)、摧残(1)、防御（没费） | 2 |
| 9 | 防御(1)、恐惧+(1)、打击（没费） | 2 |
| 10 | 违逆(1)、防御(1)、苦难(0) | 2 |
| 11 | 玷污(1)、护卫(1)、防御（没费） | 2 |

之后 7～11 循环。进阶：**8 = 护卫 + 吸引仇恨**；**9 = 只升级摧残**（v0.2.549 起）。

## 3. 必须接管原版的地方：血肉戏法对她不生效

反编译 `SleightOfFleshPower.AfterPowerAmountChanged`：
```csharp
if (amount != 0 && power.GetTypeForAmount(amount) == PowerType.Debuff
    && power.Owner.IsEnemy            // ★ 绝对阵营
    && applier == Owner && !(power is ITemporaryPower))
    await CreatureCmd.Damage(ctx, power.Owner, Amount, ValueProp.Unpowered, Owner);
```
而 `Creature.IsEnemy => Side == CombatSide.Enemy`，**玩家的 `Side` 是 `CombatSide.Player`**
⇒ 她（影子玩家）给玩家上的负面一条都不满足 ⇒ **原版这层能力对我们永远不触发**。

所以 `SleightOfFleshBossPatch` 挂在同一个 `AfterPowerAmountChanged` 上：
- 只接管「持有者是我们的 Boss + 目标是真人玩家 + 是 debuff + applier 是她 + 不是 ITemporaryPower」；
- 接管时照原版那条 `CreatureCmd.Damage(..., Amount, Unpowered, Owner)` 原样补一次；
- 其它情况 `return true` 走原版（真玩家自己的血肉戏法零回归）；
- ⚠ async 方法 ⇒ Prefix 返回 false 必须给 `ref Task __result`（本项目 v0.2.356 的坑）。

## 4. 三笔「不是牌面变量」的意图数字

### 4.1 会施加减益的牌 → 额外 +9（血肉戏法）
`BossIntentBuilder.IntentsFor` 里复用已有的 `ClassifyPowers`（它已经把「这张牌会给敌人上负面」
判出来了，`SleightOfFlesh` 的 2 费能力牌除外）。数值来自 `FleshBone.SleightPerHit`：
优先读她身上 `SleightOfFleshPower` 的层数；身上还没有时去 `Plan` 里找**排在这张之前的**
「血肉戏法」卡面值（升级版自动算）。用 `RawAttackIntent`（无来源伤害不吃力量/易伤）。

### 4.2 回合结束格挡 ≥10 → 招架盾 6 点
反编译 `ParryingShield`：
```csharp
new BlockVar(10m, ValueProp.Unpowered), new DamageVar(6m, ValueProp.Unpowered)
// AfterSideTurnEnd：participants 含她 && Owner.Creature.Block >= 10
//   ⇒ HittableEnemies 里【随机一个】吃 6 点 Unpowered 伤害
```
格挡预测在 `FleshBone.EndOfTurnBlock`：
她当前格挡 + plan 里每张牌的 `Block` 变量 + **骇人头盔**（耗能 ≥2 的牌 +4）
+ **吞噬生命**（会施加减益的牌，按种类数 ×4）。数字画在 `_extraIntents`（电球/炸弹那一行）。

反编译 `IntimidatingHelmet`：`BlockVar(4)` / `EnergyVar(2)`，`AfterCardPlayed` 里判
`cardPlay.Resources.EnergyValue >= 2` ⇒ 给 4 点格挡。预测按「剧本写的 `play.Cost` ≥2」算。

### 4.3 多人「苦难」给别人的那几刀
反编译 `Misery`：先按 `TypeForCurrentAmount == Debuff` 把**目标身上**的负面逐种克隆，
再对 `CombatState.HittableEnemies`（她视角 = 所有玩家，不含原目标）逐种 `PowerCmd.Apply`
⇒ 每复制一种，血肉戏法就打那个玩家一次 ⇒ 其他玩家各挨 `Amount × 种类数`。
所以对 `MISERY` 追加一条「每帧现算」的委托意图：被锁定的本地玩家不显示（他只挨牌面的 7 点），
其他玩家显示 `血肉戏法 × 被锁玩家身上负面种类数`（`FleshBone.LockedPlayerDebuffKinds`）。

## 5. 特殊效果文案里的那句警告

用户要求：「警告：血肉戏法和招架盾属于无来源伤害，带有『为你而死』的召唤物不会替你承受该伤害。」
- 血肉戏法：反编译确认是 `ValueProp.Unpowered` ✓
- 招架盾：同样 `Unpowered` ✓
- 而「为你而死」只挡 `props.IsPoweredAttack()`（有力量加成的攻击）⇒ 这两笔它都不挡 ✓

文案在 `content/localization/*/monsters.json` 的 `NECROBINDER_BOSS.devour_life`，
16 语言都写了（zhs/zht 中文，其余英文）。

## 6. 实机验证要点

- 进战斗日志：`shadow player ready: NECDEFCT_BOSS act=1 ...`（实际是 `DEFECT_*`/`NECROBINDER_*`
  按角色），以及 `变体判定（本场锁定）：NECROBINDER_FLESH = True`。
- 她给玩家上负面：`吞噬生命：她给 <玩家> 上了 <power>（N）⇒ 获得 4 点格挡`；
  若此时她有血肉戏法：`血肉戏法：她给 <玩家> 上了负面 ⇒ 造成 9 点无来源伤害`。
- 意图：恐惧+/违逆/摧残 那张牌下面会多一条 `9`（血肉戏法），日志 `血肉戏法意图：… 额外 +9`。
- 回合结束格挡 ≥10 时，独立意图那行多一条 `6`（招架盾）。
- 多人：苦难那回合，**非被锁玩家**的手牌栏会多一条 `9×N`；被锁玩家只有牌面的 7。
## 7. 第二轮收口（v0.2.546，用户实机反馈四条）

### 7.1 多人时招架盾只打她锁定的玩家
反编译 `ParryingShield.AfterSideTurnEnd` 的目标是
`Owner.RunState.Rng.CombatTargets.NextItem(CombatState.HittableEnemies)` —— **整份敌方列表随机一个**，
多人局里会打到没被她锁定的人。新增 `Patches/ParryingShieldBossPatch`：遗物主人是我们的 Boss 时
按原版条件（她这侧回合结束 + 格挡 ≥ `BlockVar`）结算，但目标固定成 `BossLockOn.TargetFor`；
拿不到锁定对象才退回原版随机。真玩家自己的招架盾不受影响（`return true`）。

### 7.2 命运同担会打到自己
反编译 `SharedFate`（0 费技能）：
```csharp
PowerCmd.Apply<StrengthPower>(ctx, cardPlay.Target, -EnemyStrengthLoss, Owner.Creature, card);
PowerCmd.Apply<StrengthPower>(ctx, Owner.Creature, -PlayerStrengthLoss, Owner.Creature, card);  // ← 她自己掉力量
```
「她自己掉力量」= 一次**目标为她自己**的 debuff，而她自己的生物 `Side == CombatSide.Enemy`，
正好满足原版血肉戏法的 `power.Owner.IsEnemy` ⇒ **她被自己的血肉戏法打 9 点**。
现在 `SleightOfFleshBossPatch` 对「持有者是我们 Boss」的情况**整条接管**：
只有目标**是真人玩家**才结算，她给自己/给别的怪上负面一律不结算（压掉原版）。
真玩家自己的血肉戏法仍然走原版。

### 7.3 进阶 9 不再升级出击
`BossDefs` 里血肉骨的 `AscensionUpgrades` 去掉 `(9, typeof(Unleash))`，只保留苦难、摧残。

### 7.4 招架盾预测虚高
`FleshBone.EndOfTurnBlock` 原来**无条件**把她当前格挡加进去。但格挡是在**她自己回合开始时清空**的：
玩家回合（= 意图显示的时刻）她身上那份是上回合剩下的、下回合开始就没了 ⇒ 虚高。
现在只在 `CombatState.CurrentSide == CombatSide.Enemy`（她回合内每帧重算）才计入。
并加了一行预测明细日志（限 30 条）：
```
招架盾预测：玩家回合 当前格挡不计 + 牌 11 + 骇人头盔 4 + 吞噬生命 8 = 23（门槛 10）
```
## 8. 出击的伤害预览不跟奥斯提血量（v0.2.548）

用户：「出击的伤害预览怎么不实时变化了？（根据奥斯提血量）」

**机制**：「出击」`UNLEASH` 的伤害 = `CalculationBase + ExtraDamage × **奥斯提当前生命**`
（反编译见 `BossDamagePredictor.OstyScaledDamage`，它每次调用都读 `card.Owner.Osty.CurrentHp`）。

**根因**：意图数字是在**面板重建**时算一次就冻在 `PlannedPlay.Intents` 里的。而重建只由若干事件触发；
`BossFrameTickPatch.RefreshIntentsIfPowersChanged` 每帧比对的指纹里只有「她的能力 + 每个真人玩家的能力」
—— **玩家在她回合外把奥斯提打掉/治好、或奥斯提被召唤/死亡**时，能力层数一个都没变 ⇒ 指纹不变
⇒ 不重建 ⇒ 出击的数字停在旧值（血肉骨的奥斯提来自「缚魂命匣」，玩家很容易在回合内把它打掉）。

**修法**：把**她自己的奥斯提**（存活 + 当前生命 + 最大生命）也拼进那个每帧比对的指纹：

```csharp
var osty = boss.Shadow?.Osty;
print += osty != null && osty.IsAlive ? $"|O{osty.CurrentHp}/{osty.MaxHp}" : "|O-";
```
一变就 `boss.IntentsDirty = true` ⇒ 下一帧由 `RefreshIntentsIfDrawsChanged` 消费
⇒ `BladesGrowth.RecomputeAll(planTime: 玩家回合)` 重算整排意图（出击那条会重新读奥斯提血量，
plan 里「护卫召唤」带来的待加生命仍由 `UnleashCompensation` 预补池负责）。

同时加了一行限流日志（20 条）方便验证：
```
意图重算（能力/奥斯提变化）：|O24/30 → |O18/30
```
