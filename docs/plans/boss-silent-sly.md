# 一层 Boss「奇巧猎」（静默猎手 · 奇巧/弃牌轴）—— v0.2.28

> 用户 2026-10-26 晚给的设计稿。下面是**用户原话**（逐字保留）＋ 已核对过的游戏对象映射＋实现要点。
> 实现都在：`src/Hooks/Core/SilentSlyScript.cs`（剧本/卡组/遗物）、`src/Hooks/Powers/SharedFate*.cs`（特殊效果）、
> `BossDefs.Silent` 的第一幕槽位、`BossKeys`（`SILENT_SLY`）、`Encounters/SilentSlyBossEncounter`。

---

## 1. 用户原话（设计稿）

```
1层boss奇巧猎
卡组：速行者+、必备工具、谋划专家、余像/鬼祟+、防御*5、打击*3、中和、生存者、触不可及、翻越撑击、偏折、
      杂技、早有准备+*2、后空翻、铭记死亡、连续反弹、战术大师、本能反应、隐秘匕首、暴露
遗物：蛇之戒指、涅奥的祝福、手里剑、战纹涂料
藏品：余像（单人）/鬼祟+（多人）
特殊效果：命运同担：如果静默猎手在一回合内丢弃卡牌，那在你的下回合开始时，选择1张手牌将它丢弃。
      （可以是每回合首次弃牌时给对应的玩家上一个debuff，效果是下回合开始时选择弃一张手牌，然后效果结束）
意图：
1.速行者+、偏折、打击*2（没费）、防御（没费）
2.余像、早有准备+（丢弃翻越撑击和防御）、翻越撑击（因奇巧自动打出）、打击
  （单人）/ 早有准备+（丢弃翻越撑击和鬼祟）、翻越撑击（奇巧）、鬼祟+（奇巧）、防御、打击（多人）
3.杂技（丢弃连续反弹）、连续反弹（奇巧）、灵动步伐、触不可及（没费打不了）、打击（没费）、生存者（没费）
4.谋划专家+、尖啸、铭记死亡（没费）   ← 2026-10-26 订正：谋划专家升级、暴露换尖啸
5.早有准备+（丢弃本能反应和战术大师）、本能反应（奇巧）、战术大师（奇巧）、后空翻、打击、
  隐秘匕首（丢弃防御和打击）、中和
6.洗牌。早有准备+（丢弃翻越撑击和触不可及）、翻越撑击（奇巧）、触不可及（奇巧）、防御、铭记死亡
7.杂技（丢弃因谋划专家而带有奇巧的后空翻）、后空翻（奇巧）、早有准备+（丢弃战术大师和本能反应）、
  战术大师（奇巧）、本能反应（奇巧）、隐秘匕首（丢弃打击*2）、打击、防御、防御、中和
8.打击、防御、偏折
9.防御、生存者（丢弃连续反弹）、连续反弹（奇巧）
之后6～9循环。给所有抽牌卡加上抽牌数*2的群伤（速行者的伤害），不要和原版的重复计算
```

## 2. 名字 → 游戏对象（已核对 `docs/plans/cards-silent.md` + 反编译 `sts2.dll`）

| 设计稿 | 类 | 费用 | 已核实的关键数值 |
|---|---|---|---|
| 速行者+ | `Speedster` | 2 | `PowerVar<SpeedsterPower>(2)`；**升级只加「固有」，每抽 1 张仍是 2 点群伤** |
| 必备工具 | `ToolsOfTheTrade` | 1 | 能力：回合开始抽 1 弃 1 |
| 谋划专家 | `MasterPlanner` | 2 | `MasterPlannerPower.AfterCardPlayed` → `CardCmd.ApplyKeyword(cardPlay.Card, Sly)` |
| 余像 | `Afterimage` | 1 | 能力：每打一张牌得 1 格挡 |
| 鬼祟 | `Sneaky` | 2 | 能力（**多人向**）；自带 `Sly`；`SneakyPower` 升级 +1 |
| 防御 / 打击 | `DefendSilent` / `StrikeSilent` | 1 / 1 | 8 格挡（+3）/ 6 伤（+3） |
| 中和 | `Neutralize` | 0 | 3 伤 + 1 虚弱（升级各 +1） |
| 生存者 | `Survivor` | 1 | `BlockVar(8)`；**弃 1 张** |
| 触不可及 | `Untouchable` | 2 | `BlockVar(6)`（升级 +3）；自带 `Sly` |
| 翻越撑击 | `FlickFlack` | 1 | **全体 7 伤**（升级 +2）；自带 `Sly` |
| 偏折 | `Deflect` | 0 | 4 格挡 |
| 尖啸 | `PiercingWail` | 1 | 全体 -`StrengthLoss(6)` 力量（升级 +2）；消耗 |
| 杂技 | `Acrobatics` | 1 | 抽 `Cards(3)`（升级 4）**弃 1** |
| 早有准备+ | `Prepared` | 0 | 抽 `Cards`（1，升级 2）**弃同样张数** |
| 后空翻 | `Backflip` | 1 | `BlockVar(5)` + 抽 `Cards(2)`；**本身没有奇巧**（第 7 回合那张由剧本直接给，见 §8） |
| 铭记死亡 | `MementoMori` | 1 | `CalculationBase(9)` + `ExtraDamage(4) × 本回合丢弃张数`（升级 +2/+1） |
| 连续反弹 | `Ricochet` | 2 | 3 伤 ×`Repeat(4)`；自带 `Sly` |
| 战术大师 | `Tactician` | 3 | `EnergyVar(1)`；自带 `Sly` |
| 本能反应 | `Reflex` | 3 | 抽 `Cards(2)`（升级 3）；自带 `Sly` |
| 隐秘匕首 | `HiddenDaggers` | 0 | 弃 `Cards(2)`，加 `Shivs(2)` 张小刀 |
| ~~暴露~~ | ~~`Expose`~~ | — | **用户 2026-10-26 订正：第 4 回合不用它了，改成「尖啸」** |
| ~~手上技法~~ | ~~`HandTrick`~~ | — | **用户 2026-10-26 订正：不用它了**，第 5 回合「隐秘匕首」弃的第 2 张改成**打击** |
| 蛇之戒指 | `RingOfTheSnake` | — | 战斗开始多抽（对她＝更早铺牌，不改能量） |
| 手里剑 | `Shuriken` | — | 每回合每 3 张攻击 → +1 力量 |
| 战纹涂料 | `WarPaint` | — | 拾起时随机升级技能牌（战斗内无数值影响，纯人设） |

## 3. 能量：每回合 **2**

`涅奥的祝福` 固定 −1 能量、−2 抽牌 ⇒ 静默猎手（基础 3）每回合就是 2 费（和刀猎/涂毒猎同一个口径）。
把用户写的每一回合账都代进去都能对上（下面括号里是逐张累加）：

* 第 1 回合 速行者+(2) + 偏折(0) = 2 ⇒ 打击×2、防御打不出 ✔（与「没费」一致）
* 第 2 回合（单人）余像(1) + 早有准备+(0) + 翻越撑击(奇巧＝免费) + 打击(1) = 2 ✔
* 第 2 回合（多人）早有准备+(0) + 翻越撑击(奇巧) + 鬼祟+(奇巧) + 防御(1) + 打击(1) = 2 ✔
* 第 3 回合 杂技(1) + 灵动步伐(1) = 2 ⇒ 触不可及/打击/生存者都打不出 ✔
* 第 4 回合 谋划专家(2) + 暴露(0) = 2 ⇒ 铭记死亡打不出 ✔
* 第 5 回合 早有准备+(0) + 后空翻(1) + 打击(1) + 隐秘匕首(0) + 中和(0) = 2 ✔
* 第 6 回合 早有准备+(0) + 防御(1) + 铭记死亡(1) = 2 ✔
* 第 7 回合 杂技(1) + 打击(1) + 中和(0) = 2 ⇒ 两张防御打不出（设计稿没标「没费」，按能量自然跳过）
* 第 8 回合 打击(1) + 防御(1) + 偏折(0) = 2 ✔
* 第 9 回合 防御(1) + 生存者(1) = 2 ✔

## 4. 单人 / 多人的差别（两处，都由「玩家数 ≥ 2」判定，两端一致）

1. **手里那张稀有牌**：单人 = `余像`（第 2 回合**打出**，1 费拿到能力）；
   多人 = `鬼祟+`（第 2 回合**被丢弃**，靠奇巧免费打出）。
   实现：`MultiplayerCardSwap` 新增一行 `(0, SilentBoss, Afterimage → Sneaky)` + **强制升级**，
   再给这张剧本牌标 `DiscardInsteadWhenMultiplayer`（单人照常打，多人改成"被弃"）。
2. **藏品**：单人 → 「余像」；多人 → 「鬼祟+」。见 `DownfallCollectibleCompat` 的 `ResolveLoot`
   （藏品牌只有 1、2 层会作为结算奖励发放，1 层正好符合）。

## 5. 特殊效果「命运同担」

* `Powers/SharedFatePower`（**挂在她身上**，状态条显示）：`AfterCardDiscarded` 里判断
  「被弃的是她自己的牌」→ **每回合第一次**弃牌时，给所有存活真人玩家挂上
  `Powers/SharedFateDiscardPower`（一回合最多一次；她自己回合开始重置）。
* `Powers/SharedFateDiscardPower`（**挂在玩家身上**，Debuff）：`AfterPlayerTurnStart` →
  `CardSelectCmd.FromHandForDiscard(DiscardSelectionPrompt, 1)` → `CardCmd.Discard` → **移除自己**。
  （时机与「烘焙手套」同一条钩子：那时手牌已经抽好了。）
* 多人时「对应的玩家」：设计稿没写死具体是谁，**当前实现 = 所有存活真人玩家各挂一层**（一人弃 1 张）。
  若要改成「只影响锁定她的那一位」，把 `ApplyTo` 里的列表换成 `BossLockOn.RealPlayers` + `SpecialEffectAppliesTo`。

## 6. 「速行者的伤害」＝ 抽牌卡多一条群伤意图

`SpeedsterPower.AfterCardDrawn`（反编译）：`!fromHandDraw && card.Owner == Owner && 当前阵营是她的阵营`
→ 对所有可命中敌人（＝玩家）造成 `Amount`（2）点 `Unpowered` 伤害。也就是**每抽 1 张 = 2 点群伤**。

* **真实伤害由原版结算**（她的牌确实在抽牌），我们**只在意图里补这部分**，绝不自己再打一次
  （用户：「不要和原版的重复计算」）。
* 抽牌卡白名单（`BossIntentBuilder.DrawCards`）：`PREPARED / ACROBATICS / BACKFLIP / REFLEX`，
  张数读牌面 `Cards` 变量 ⇒ 意图追加 `2×N`（N=1 时是单段 `2`）。

## 7. 「铭记死亡」的意图＝按本回合已弃张数加伤

`MementoMori` 的倍率数的是「本回合、她当 Actor 的 `CardDiscardedEntry`」。
规划期历史里一张都没有 ⇒ 以前会显示 9（实际 17）。现在由 `BladesGrowth` 沿计划累加
`DiscardsOf(card)`（早有准备＝`Cards`、杂技＝1、生存者＝1、隐秘匕首＝`Cards`）传给意图。

## 8. 别的实现要点

* **奇巧**：`ScriptCard.Sly` 让造牌时直接 `CardCmd.ApplyKeyword(card, CardKeyword.Sly)`
  —— 第 7 回合那张「后空翻」就是这么带上的（设计稿说的「因谋划专家而带有奇巧」；
  谋划专家只会给**打出去的牌**上奇巧，手牌里的这张必须由剧本点名）。
* **手牌不会被抽牌撑乱**：`BossDefinition.KeepsScriptedHand`（本 Boss 打开）⇒
  她每打完一张牌，把**这次新进手牌**的牌放回抽牌堆顶（小刀 token 除外），手牌恒等于剧本。
  抽牌本身照常发生 ⇒ 速行者那 2 点群伤照常结算。
* 阵容：第一幕候补从 3 个变 4 个（愤怒战 / 灾厄骨 / 碎屑储君 / **奇巧猎**），
  仍然是「人数 ≤ 职业数」的规则（4 个不同角色，3 人局够用）。

## 9. 复测要点（`mod_log.txt` 里该看到什么）

```
[CharBoss] 剧本手牌(9): [PREPARED,REFLEX,TACTICIAN,BACKFLIP,...
[CharBoss] 剧本第 2 回合: 打 [AFTERIMAGE,PREPARED,STRIKE] 跳过 [FLICK_FLACK(被丢弃 → 奇巧自动打出),DEFEND_SILENT(被丢弃)] 余费=0
[CharBoss] 速行者：ACROBATICS 会抽 3 → 补 2×3 的群伤意图（真实伤害由原版 SpeedsterPower 结算，这里只是显示）
[CharBoss] 命运同担：她本回合第一次弃牌（DEFEND_SILENT）→ 1 名玩家下回合开始时各弃 1 张手牌
[CharBoss] 命运同担：玩家被迫弃掉 STRIKE_SILENT
[CharBoss] 多人换牌（2 人）：Afterimage → Sneaky（SilentBoss 第三幕）   ← ⚠ 日志里的「第几幕」是旧文案，实际是第一幕
[CharBoss] 剧本给 BACKFLIP 加上「奇巧」（被丢弃时会自动打出）
[CharBoss] 收藏家兼容：SILENT_BOSS act1 → 藏品「鬼祟+」（多人）/「余像」（单人）
```

### 9.1 还没实机验证的三件事（下一轮先看这三条）

1. **奇巧牌是否真的自动打出**：第 2/5/6/7/9 回合那些「被丢弃」的牌（翻越撑击 / 本能反应 / 战术大师 /
   触不可及 / 后空翻 / 连续反弹）应当在她弃掉后**立刻免费打出**，并且手牌栏里是**亮**的、下面有意图。
2. **速行者的群伤**：`SpeedsterPower` 是原版结算的，我们只补意图 —— 若实战里玩家**没掉血**，
   说明她的抽牌被拦了（`BlocksVanillaDraw` / `ShadowBossNoDrawPatch`）或 `HittableEnemies` 没翻成玩家侧。
   对着看两行：`速行者：… 补 2×N` 与实际掉血。
3. **命运同担**：她第一次弃牌后玩家身上应出现一层「命运同担」，**玩家下回合开始时会弹选牌界面**。

## 10. v0.2.29：三处收尾（用户 2026-10-26 深夜反馈）

1. **「命运同担」只对被锁定的那位玩家生效**（原来给所有存活真人玩家各挂一层）。
   实现：SharedFatePower 里用 BossLockOn.SpecialEffectAppliesTo(boss, player) 过滤
   —— 单人恒成立、多人只有 TargetFor(boss) 那位。
2. **第 5 回合「隐秘匕首」弃的第 2 张：手上技法 → 打击**（设计稿更新）。
3. **多人多 Boss 的旧 bug：一只 Boss 阵亡会把所有 Boss 的意图面板清空**
   （BeforeRemovedFromRoom 无条件 BossHandUi.EndFight）。现在改成
   MultiBossPlan.AnyOtherBossAlive(this) 为真时只 ReleasePanel(this)；详见
   .dshwolf/buglog.json 与 CharBossMonster.Descriptor.cs 的注释。三人/四人同一条路径，一并修好。

## 11. v0.2.34：第 4 回合 + 两个图标

1. **第 4 回合**：`谋划专家+`（升级只降费 2 → 1，反编译 `OnUpgrade: EnergyCost.UpgradeBy(-1)`）
   + **尖啸**（1 费，全体 -6 力量，消耗）= 正好 2 费；`铭记死亡` 仍然没费。
   原来的「暴露（0 费）」换成尖啸，卡组里的 `Expose` 也同步换成 `PiercingWail`。
   尖啸的意图是**策略**（`BossIntentBuilder.ForceDebuffCards` 里早就有 `PIERCING_WAIL`）。
2. **图标借用原版**（`Patches/BossPowerIconPatch.cs`）：原版 `PowerModel` 的图标是按 `Id.Entry` 拼路径的
   （`atlases/power_atlas.sprites/<entry>.tres` + `powers/<entry>.png`），我们的两条 Power 在美术包里没有。
   现在在 `PackedIconPath` / `ResolvedBigIconPath` 两个 getter 上挂钩子，**只对我们这两个类型**返回：
   - 她身上的「命运同担」→ **好奇**（`CURIOUS_POWER`）
   - 玩家身上那层 → **抑制**（`DAMPEN_POWER`）
3. **回退**：二层 Boss 不再固定灵魂骨（`BossLineups.ForcedMainBossAct2 = null`）。
## 12. v0.2.36：奇巧猎的进阶加成 + 炼药猎的藏品

### 12.1 进阶（`BossDefs.Silent.AscensionUpgradesByAct`，只作用于**第一幕**）

| 进阶 | 升级哪张 | 变化 |
|---|---|---|
| **8** | **中和** | 3 伤 / 1 虚弱 → 4 伤 / 2 虚弱（`DamageVar` 与 `PowerVar<WeakPower>` 各 +1） |
| **9** | **连续反弹** | 3 伤 ×4 → 3 伤 ×**5**（`Repeat` +1） |
| **9** | **铭记死亡** | 9 + 4×弃牌数 → **11 + 5×弃牌数**（`CalculationBase` +2、`ExtraDamage` +1） |

⚠ 三条都只对**第一幕**生效（`byAct == 1`）：涂毒猎（第二幕）与刀猎（第三幕）也有中和，
不分幕会串味。执行路径是 `CharBossMonster.AscensionUpgradeTypes()` → `BossScriptRunner.BuildHand`
的 `extraUpgrades`，所以**剧本造出来的手牌**照样是升级版；意图数值读的是同一张实例的变量，会跟着变。
连续反弹在剧本里是「被丢弃后靠奇巧免费打出」（`costOverride: 0`），升级只影响它的伤害段数。

### 12.2 炼药猎的藏品 = 炼制药水

三层静默猎手有**两套设计**、藏品也不同（用户 2026-10-26）：

| Boss | 藏品牌 |
|---|---|
| 刀猎（`SILENT_SHIV`） | 墨之刃 `BladeOfInk` |
| **炼药猎（`SILENT_BREWER`）** | **炼制药水 `Alchemize`** |

实现：`DownfallCollectibleCompat.ResolveLoot` 里加一条分支 —— `SILENT_BOSS` + 第 3 幕 +
`BossLineups.BrewerActive()` 为真 ⇒ 用 `Alchemize`；判据与「这一场用哪套设计」同源
（战斗内以**房间遭遇**为准，两端一致，控制台点名也判得对）。