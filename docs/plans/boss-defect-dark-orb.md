# 一层 Boss「黑球机」（故障机器人 · 暗黑充能球轴）—— v0.2.44

> 用户 2026-10-26 晚设计稿。卡组 / 遗物 / 剧本 / 特殊效果 / 进阶全部按稿件实现。
> 代码：`src/Hooks/Core/DarkOrbBossScript.cs`（剧本+卡组+遗物）、
> `src/Hooks/Powers/CoolantPower.cs`（冷却剂）、`src/Hooks/Powers/GoForTheEyesAimPower.cs`（眼部攻击 buff）、
> `src/Hooks/Patches/DarkOrbPassiveSuppressPatch.cs`（黑球被动拦截）、
> `BossDefs.Defect` 第一幕槽位、`BossKeys`（`DEFECT_DARK`）、`Encounters/DefectDarkBossEncounter`。

---

## 1. 用户原话（设计稿）

```
一层boss黑球机
特殊效果：冷却剂:战斗开始时，将3张冷光+分别加入你的手牌，抽牌堆和弃牌堆。其被打出会使故障机器人的
所有暗黑充能球本回合不使用被动效果。
遗物:破损核心、涅奥的祝福、面包、共生病毒、灯笼
进阶八:升级飞跃，获得不安灯油。进阶九:升级弹回、趁势打击
第一回合: 启动流程、双重释放、打击(没费)
第二回合: 飞跃、眼部攻击（本回合将要打出眼部攻击时，故障机器人自身获得"眼部攻击"buff，效果是如果有玩家
对它打出攻击牌或有群攻牌波及了它，它将要打出的眼部攻击就会触发"如果敌人的意图是攻击"的特效，即给予1层虚弱）、热修复
第三回合: 空值、笨拙、防御
第四回合: 遗传算法(16点格挡)、弹回、趁势打击（之后降为0费）
第五回合: 趁势打击、双重释放、眼部攻击（同上，获得"眼部攻击"buff……）
第六回合: 空值、飞跃、打击(没费)
第七回合: 弹回、趁势打击、防御
之后5～7循环。
```

## 2. 名字 → 游戏对象（已核对本地化 + 反编译）

| 设计稿 | 类 | 费用 | 已核实的关键数值 |
|---|---|---|---|
| 启动流程 | `BootSequence` | 0 | 获得 10 点格挡 |
| ~~双重释放~~ → **冰川** | ~~`Dualcast`~~ → `Glacier` | 1 → **2** | 2026-10-27 削弱：改成冰川（6 格挡 + 生成 2 个冰霜球），不再激发黑球 |
| 打击 | `StrikeDefect` | 1 | 6 伤 |
| 飞跃 | `Leap` | 1 | 9 格挡（升级 +3 = 12） |
| 眼部攻击 | `GoForTheEyes` | 0 | 3 伤；**原版判定**「目标 Monster 的意图是攻击」才给 1 虚弱 ⇒ 对 Boss/玩家都判不到（见 §5） |
| 热修复 | `Hotfix` | 0 | 本回合 +2 集中 |
| 空值 | `Null` | 2 | 10 伤 + 1 虚弱 + **生成 1 个黑暗充能球** |
| 笨拙 | `Clumsy` | — | **诅咒牌**（`CardType.Curse`，打不出） |
| 防御 | `DefendDefect` | 1 | 5 格挡 |
| 遗传算法 | `GeneticAlgorithm` | 1 | `BlockVar` 每次打出永久 +`Increase(3)`；剧本**写死 16**（见 §4） |
| 弹回 | `Rebound` | 1 | 9 伤（升级 +3 = 12），Event 稀有度 |
| 趁势打击 | `MomentumStrike` | 1 | 11 伤；**打出后这张牌耗能降为 0**（升级 +? ） |
| 冷光 | `Luminesce` | **0** | Token 技能：**获得 2 点能量（升级 3）** ⇒ 玩家的「白送能量」 |
| 破损核心 | `CrackedCore` | — | 战斗开始 1 个闪电球 |
| 面包 | `Bread` | — | **第 1 回合 −2 能量**，其余回合 **+1 能量** |
| 共生病毒 | `SymbioticVirus` | — | 战斗开始 1 个**黑暗充能球**（她这套玩法就靠它） |
| ~~灯笼~~ | ~~`Lantern`~~ | — | **2026-10-27 削弱：已去掉**（第 1 回合不再 +1 能量） |
| 不安油灯 | `UnsettlingLamp` | — | 每场战斗第一次给敌人上负面时，效果翻倍（进阶 8 奖励） |

## 3. 能量：第 1 回合 1 点，之后 3 点

角色基础 3 − **涅奥的祝福 1** − **面包 2**（仅第 1 回合）/ + **面包 1**（其余回合）（灯笼已于 2026-10-27 去掉）：

| 回合 | 账 | 结果 | 稿件里的「没费」 |
|---|---|---|---|
| 1 | 3 −1 −2 | **0** | 只有 0 费的启动流程打得出去；冰川(2)、打击(1) 都打不出 ✔ |
| 2–7 | 3 −1 +1 | **3** | 第 3/4/7 回合都正好花 3 ✔；第 6 回合空值(2)+飞跃(1) 之后打击没费 ✔ |

剧本把每回合能量**写死**（和其它 Boss 一样），免得被遗物钩子的实现细节影响意图。

## 4. 剧本（7 回合，之后 **5～7 循环**）

| 回合 | 能量 | 牌序 |
|---|---|---|
| 1 | 0 | 启动流程（0，10 格挡）；**冰川（2）、打击（1）都没费** |
| 2 | 3 | 飞跃（1）、**眼部攻击**（0，打出前她先获得「眼部攻击」buff）、热修复（0，本回合 +2 集中） |
| 3 | 3 | 空值（2，10 伤 + 虚弱 + 1 个黑暗球）、笨拙（诅咒，打不出）、防御（1） |
| 4 | 3 | **遗传算法（写死 16 点格挡）**、弹回（1）、趁势打击（1，打出后这张牌降为 0 费） |
| 5 | 3 | 趁势打击、**冰川**（2 费，6 格挡 + 2 冰霜球）、**眼部攻击**（同样先拿 buff） |
| 6 | 3 | 空值（2）、飞跃（1）、打击（1，**没费**） |
| 7 | 3 | 弹回（1）、趁势打击（1）、防御（1） |

生成的黑球顺序：战斗开始 `破损核心` → 闪电球，`共生病毒` → **黑暗球**（由 `BossHookBridge` 手动入队，
原版遗物钩子到不了影子玩家）；之后「空值」再各生成 1 个黑暗球。

## 5. 特殊效果「冷却剂」（`Powers/CoolantPower`）

**战斗开始时**：给**被她锁定的那位玩家**（单体特殊效果）3 张「**冷光+**」，分别放进 **手牌 / 抽牌堆 / 弃牌堆**
（`BossHookBridge.GrantCoolantLuminesceAtCombatStart`）。冷光 0 费、+3 能量 ⇒ 玩家白拿 9 费。

**被锁定的那位玩家回合结束时，手牌里还留着「冷光」**：故障机器人的**所有暗黑充能球本回合不触发被动**（打出冷光换能量 = 拿不到保护）
（`CoolantPower.AfterCardPlayed` 置 `boss.DarkPassiveSuppressed`；
拦截点是 `Patches/DarkOrbPassiveSuppressPatch` → `DarkOrb.BeforeTurnEndOrbTrigger` 的前缀，
只拦**她的**暗黑球，闪电/冰霜/等离子照常，玩家自己的球零影响）。
她那回合结束时自动清掉标记（下一回合照常）。

## 6. 「眼部攻击」buff（`Powers/GoForTheEyesAimPower`）

- 触发时机：`PrepareTurn` 发现**本回合计划里有「眼部攻击」** ⇒ 立刻给她挂上这层 Power（状态栏可见、可悬浮）；
- 判定：本回合**有真人玩家用攻击牌真的打到她**（单体打她 / 群攻波及她；打别的 Boss 不算）⇒ 记下；
- 结算：她真打出「眼部攻击」时，若上述条件成立 ⇒ **补 1 层虚弱**给目标；
- 她回合结束时这层 buff 自动移除。

为什么必须自己做：原版 `GoForTheEyes.OnPlay` 判的是 `cardPlay.Target.Monster.IntendsToAttack`，
而她的目标**是玩家**（`Monster` 为 null）⇒ 原版永远不触发。

## 7. 进阶

| 进阶 | 加成 |
|---|---|
| **8** | 升级「**飞跃**」+ 获得遗物「**不安油灯**」（每次战斗第一次给敌人上负面时效果翻倍） |
| **9** | 升级「**趁势打击**」（2026-10-27 订正：**弹回不再升级**） |

只作用于**第一幕**（物理机/能力机不受影响）。

## 8. 身份与出处

| 项 | 值 |
|---|---|
| 遭遇 id | `DEFECT_DARK_BOSS_ENCOUNTER`（控制台 `fight DEFECT_DARK_BOSS_ENCOUNTER`） |
| 档位 | **第一幕**（`TierAct = 0`） |
| 阵容 | 一层候补 5 选 1：`SILENT_SLY` · `NECROBINDER_DOOM` · **`DEFECT_DARK`** · `IRONCLAD_ANGER` · `REGENT_DEBRIS` |
| 血量 | 75 + 涅奥祝福 150 = **225** |
| 地图图标 | **塔1崩坠的「一层故障机器人」图标** `res://CharacterBosses/images/map/defect_void`（用户 2026-10-27；崩坠的 `BossSetPatch` 按幕换 Defect 图标：第一幕 `defect_void`、第二幕 `defect_bronze_orbs`、第三幕 `defect_focus`，我们三层能力机沿用 `defect_focus` 不动）。源图：`Downfall.jar` 的 `downfallResources/images/ui/map/icon/defect_void.png` + `outline/defect_void_outline.png`（512×512），经 `tools\art\import_proj` 导入后由 `tools\art\pck_extras.txt` 打进 PCK |

## 9. 复测要点 / 已知未验证

⏳ 一次都没实机跑过。该看的日志：

```
[CharBoss] 共生病毒：战斗开始引导黑暗球 入队=True（球数 2/3）
[CharBoss] 破损核心：战斗开始引导闪电球 入队=True
[CharBoss] 冷却剂：战斗开始给 1 名玩家各塞 3 张「冷光+」（手牌/抽牌堆/弃牌堆，共 3 张）
[CharBoss] 冷却剂：玩家打出「冷光」→ 故障机器人本回合的**暗黑充能球不使用被动**
[CharBoss] 她的计划里有「眼部攻击」→ 给她挂上「眼部攻击」buff
[CharBoss] 眼部攻击 buff：玩家打出攻击牌 … → 她这记「眼部攻击」会额外给予 1 层虚弱
[CharBoss] 剧本格挡覆盖：GENETIC_ALGORITHM = 16
```

需要特别验证的三件事：

1. **黑暗球真的出现了吗**（共生病毒那条 `入队=`，球数应当是 2/3：闪电 + 黑暗）；
2. **冷光真的进了手牌/抽牌堆/弃牌堆**（玩家手牌里应有 3 费可白拿的一张 0 费牌）；
3. **打出冷光后她那回合的黑暗球是否真的不打人**（`DarkOrbPassiveSuppressPatch` 是反射式前缀，
   如果 `DarkOrb.BeforeTurnEndOrbTrigger` 改名就会静默失效 —— 日志里那条「不使用被动」是唯一的确认点）。

## 10. v0.2.45：三处订正

1. **冷却剂是单体特殊效果**：冷光只发给**被她锁定的那位玩家**，也只有他打出冷光才压她的黑球被动
   （判据 `BossLockOn.IsLocked`，与「命运同担」同一套）。
2. **「眼部攻击」buff 的判定改成「真的打到她」**：原来按「玩家打出攻击牌」就置位，
   于是「打别的 Boss 没打到她」也会触发。现在改成监听
   `AfterDamageReceived`（`target == 她` 且 `cardSource` 是攻击牌）—— 单体打她、群攻波及她都算，
   打别人一律不算（被格挡全挡住也算「打到」，因为 `DamageResult` 照样产生）。
3. **她的攻击牌不再打玩家召唤物**（用户反馈：奇巧猎的「连续反弹」打到史莱姆老大的史莱姆）。
   根因：反编译 `AttackCommand.GetPossibleTargets()` —— 「随机敌人 / 对所有敌人」走的是
   `PlayerCreatures`（**含玩家宠物**）或 `GetOpponentsOf(Attacker)`，**根本不读 `HittableEnemies`**
   ⇒ 之前那条「只挑真人玩家」的收窄对它无效。
   现在新增 `Patches/BossAttackRealPlayersOnlyPatch`：在 `GetPossibleTargets` 的 postfix 里，
   **攻击者是我们的 Boss** 时剔掉所有「有 Monster 或没有 Player」的生物 —— 只留真人玩家。
   玩家自己打牌、原版怪物出手一切照旧。
## 11. v0.2.47：两个实机 bug

1. **黑暗球生成了两个**：原版 `SymbioticVirus.AfterSideTurnStart` 的条件是
   「`participants` 含她 && `TurnNumber <= 1`」——影子玩家在**她自己第 1 回合**正好满足
   ⇒ 除了我们战斗开始时手动引导的那颗，它又导了一颗。
   新增 `Patches/SymbioticVirusOncePatch.cs`（和 `CrackedCoreOncePatch` 同一套做法）：
   对**我们的 Boss** 跳过原版这一条，真人玩家的共生病毒完全不受影响。
2. **她的「眼部攻击」打出后停在半空**：`GoForTheEyesAimPower.AfterCardPlayed` 里 `await PowerCmd.Apply<WeakPower>`
   —— 那条钩子在卡牌结算链里、牌还在播动画，await 一个会播动画的指令会互相等 ⇒ 牌永远悬着。
   现在钩子里**只做记录**（`PlayerAttackHit`），虚弱改由 **`CharBossMonster.PlayOne` 在 AutoPlay 返回之后**补。
## 12. v0.2.48：削弱（用户 2026-10-27「太超模了」）

| 项 | 改法 |
|---|---|
| 双重释放 → **冰川**（两处：第 1 / 5 回合）+ 卡组同步换 | 冰川 `Glacier` = **2 费**，获得 6 格挡 + **生成 2 个冰霜球**（不再激发黑球打两次爆发伤害） |
| 去掉遗物「**灯笼**」 | 第 1 回合不再 +1 能量 ⇒ 能量账变成 3 −1(涅奥) −2(面包) = **0**，第 1 回合只剩 0 费的「启动流程」 |

其它（空值 / 遗传算法 16 格挡 / 弹回 / 趁势打击 / 眼部攻击 buff / 冷却剂 / 进阶）一律不动。
第 5 回合仍然是「冰川(2) + 趁势打击(1) + 眼部攻击(0) = 3 费」，正好花完。
## 13. v0.2.49：眼部攻击卡空中（真正原因）+ 两个图标 + 空值的黑球意图

### 13.1 「眼部攻击」打出后停在空中 —— 真因不是我们的 await

v0.2.47 把虚弱结算从钩子挪到 `PlayOne`，但**牌还是悬着**。翻日志才发现：我们这边**正常打完了**
（`done GO_FOR_THE_EYES: bossHp=220 …`、虚弱也补上了），所以悬的是**原版那条出牌动画链**。

反编译 `GoForTheEyes.OnPlay`：

```csharp
await DamageCmd.Attack(DynamicVars.Damage.BaseValue).FromCard(this, cardPlay)
        .Targeting(cardPlay.Target).WithHitFx(...).Execute(choiceContext);
if (cardPlay.Target.Monster.IntendsToAttack)     // ← 目标没有 Monster 时 NRE
    await PowerCmd.Apply<WeakPower>(...);
```

玩家的攻击目标是**玩家**（`Target.Monster == null`）⇒ 那一句抛 `NullReferenceException`，
`OnPlay` 的 Task 变成 faulted：伤害打出去了，但动画链等不到正常完成 ⇒ 牌停在半空。

修法（`Patches/GoForTheEyesBossPatch.cs`）：对我们的 Boss **接管 `OnPlay`**，只复刻「伤害」那一半
（`DamageCmd.Attack(...).Targeting(...).WithHitFx("vfx/vfx_attack_blunt", …).Execute(ctx)`）；
虚弱交给「眼部攻击」buff 判定 + `PlayOne` 在结算完之后补。真人玩家打出这张牌仍走原版。
（`OnPlay` 是 `protected`，Harmony 只能按字符串名挂：`[HarmonyPatch(typeof(GoForTheEyes), "OnPlay")]`。）

### 13.2 图标

| Power | 图标 |
|---|---|
| 冷却剂 `BossCoolantPower` | 借用原版「**好奇**」（`CURIOUS_POWER`） |
| 眼部攻击 buff `GoForTheEyesAimPower` | **这张牌的卡图**，缩成 **128×128** 后随 PCK 打包 |

卡图是从**游戏本体 PCK** 里抽的（`res://images/packed/card_portraits/defect/go_for_the_eyes.png`，原图 1000×760），
用新脚本 `tools/art/extract_card_icon.gd` 缩放 + 居中到 128×128，产物
`tools/art/import_proj/CharacterBosses/images/powers/go_for_the_eyes.png`，
已加进 `tools/art/pck_extras.txt`。`BossPowerIconPatch` 新增 `DirectPathOf` 分支，
小图标与大图都直接指向这张 PNG（不走原版 atlases）。

### 13.3 空值生成的黑球不进意图 —— 确认是漏登记

`Core/BossOrbMath.Channels`（「这张牌会生成什么球」的写死表）里**没有 `NULL`**，
所以「空值」生成的那颗黑球不会参与「将要被激发的球」的模拟 ⇒ 牌下方缺那条激发伤害。
已补：`["NULL"] = (typeof(DarkOrb), 1)`。（`GLACIER` 本来就在表里，2 个冰霜球。）
## 14. v0.2.51：藏品 = 遗传算法

一层「黑球机」的**藏品**（崩坠收藏家击败她后掉的那张牌）= **遗传算法 `GeneticAlgorithm`**（用户 2026-10-27）。
写在 `DownfallCollectibleCompat.LootTable` 的 `[("DEFECT_BOSS", 1)]` 一格；
第二幕物理机仍是「超能光束+」、第三幕能力机仍是「创造性AI」。
## 15. v0.2.52：冷却剂改口径（用户 2026-10-27）

> 在战斗开始时将 2 张冷光分别加入你的抽牌堆和手牌。在你的回合结束时，若你手牌中有冷光保留，
> 本回合故障机器人的暗黑充能球不触发被动效果。

| 项 | 原 | 现 |
|---|---|---|
| 发牌 | 3 张「冷光+」→ 手牌 / 抽牌堆 / **弃牌堆** | **2 张「冷光」** → 抽牌堆 / 手牌（不进弃牌堆，**不升级**） |
| 触发 | 玩家**打出**冷光 ⇒ 那回合黑球不打被动 | 玩家**回合结束时手里留着**冷光 ⇒ 那回合黑球不打被动 |
| 挂点 | `AfterCardPlayed` | **`BeforeFlush`**（手牌被丢弃**之前**，按玩家逐个派发） |

⇒ 玩法变成「留牌换保护 / 打牌换能量」二选一：打出冷光拿走 2 费，就没这一回合的黑球免疫；
想免疫就得把它攥在手里（占一个手牌位）。单体效果不变：只认被她锁定的那位玩家。
## 16. v0.2.53：多人时「冰川」换「休眠」

用户 2026-10-27：多人模式时黑球机的**冰川 → 休眠**。

写在 `MultiplayerCardSwap.Table`（**幕下标 + Boss 类型 + 原牌 → 换上那张**）：

```csharp
(0, typeof(DefectBoss), typeof(Glacier), typeof(Hibernate)),   // A6. 黑球机：冰川 → 休眠（多人）
```

* 两张都是 **2 费技能、都生成 2 个冰霜球** ⇒ 第 5 回合的能量账（2 + 趁势打击 1 = 3）不变；
* 区别是休眠的「本回合冰霜充能球会给予所有**队友**格挡」在我方阵营 = 给**所有 Boss** 格挡，不给玩家；
* 顺带把 `HIBERNATE` 补进 `BossOrbMath.Channels`（2 个冰霜球）——否则多人时它激发球的伤害不进意图
  （和「空值」同一个坑）。
* 单人模式完全不受影响（换牌表只在 ≥2 名真人玩家时生效）。