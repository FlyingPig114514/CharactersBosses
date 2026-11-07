# 二层「物理机」（故障机器人 · 爪击 / 0 费牌轴）

> 用户 2026-10-24 口述设计。本文件是**唯一权威记录**，改设计先改这里。
> 代码：剧本 `src/Hooks/Core/ClawDefectScript.cs`、特殊效果 `src/Hooks/Powers/SynthesisPower.cs`、
> 定义 `src/Hooks/Core/BossDefs.cs` 的 `Defect`（第二幕那一档）、遭遇 `DefectClawBossEncounter`
> （`ENCOUNTER.DEFECT_CLAW_BOSS_ENCOUNTER`，钉死档位 = 第二幕）。

## 1. 基本盘

| 项 | 值 |
|---|---|
| 幕 | 第二幕（`ActIndex = 1`），HP 75 + 涅奥祝福 150×2 = **375** |
| 能量 | 剧本逐回合写死 **2** |
| 遗物 | 破损核心（起始）、涅奥的祝福、镀金缆线、浮木、**数据磁盘**（2026-10-24 由手里的剑换成它） |
| 特殊效果 | **人工合成**：战斗开始时获得 **3 层人工制品**（`SynthesisPower`，走 `BossHookBridge.FireTurnStart` 施加） |
| 地图图标 | **塔1崩坠的「二层故障机器人」图标** `res://CharacterBosses/images/map/defect_bronze_orbs`（用户 2026-10-27；崩坠的 `BossSetPatch` 按幕换 Defect 图标：第一幕 `defect_void`、第二幕 `defect_bronze_orbs`、第三幕 `defect_focus`，三层能力机沿用 `defect_focus` 不动）。源图：`Downfall.jar` 的 `downfallResources/images/ui/map/icon/defect_bronze_orbs.png` + `outline/`，512×512，经 `tools\art\import_proj` 导入后由 `tools\art\pck_extras.txt` 打进 PCK |

## 2. 剧本（10 回合，7～10 循环）

| 回合 | 牌（按顺序） | 备注 |
|---|---|---|
| 1 | 暴涨、冰寒、启动流程 | 暴涨先失去 2 个球位；冰寒的冰球数 = **玩家数量**（见 §3.1） |
| 2 | 爪击、打击、防御 | |
| 3 | 野性、爪击、爪击（野性捞回来的）、热修复 | 野性 2 费；热修复 0 费（2026-10-24 与第 6 回合那张打击对调） |
| 4 | 冷却剂（单人）/ 一心化万（多人）、超越光速、超越光速（野性捞）、充电、螺旋钻机（抽上来的）、打击（抽上来的） | 充电给的是**下回合**能量 ⇒ 打击没费 |
| 5 | 超能光束、防御、防御 | 超能光束 2 费 ⇒ 两张防御都没费 |
| 6 | 打击（原第 3 回合那张）、打击、防御 | 2 费只够两张打击 ⇒ 防御没费 |
| 7 | 爪击、爪击（野性捞）、打击、防御 | 洗牌回合 |
| 8 | 超越光速、超越光速（野性捞）、充电、防御、螺旋钻机（抽上来的）、打击（抽上来的） | 打击没费 |
| 9 | 超能光束、防御、防御 | 同第 5 回合 |
| 10 | 爪击、爪击（野性捞）、打击、防御 | |

## 3. 三条实现要点

1. **冰寒（`Chill`）的数量**：原版是「`CombatState.HittableEnemies` 里每有一个敌人 → 生成 1 个冰霜球」。
   用户 2026-10-24 订正为「**只检测玩家数量**」⇒ 我们那条翻侧规则本来就把这个列表收窄成**真人玩家**
   （宠物/召唤物不算），所以原版行为直接就是对的，**不需要任何额外补丁**
   （早先那套「玩家方单位数、排除佩尔士兵/异鸟宝宝/崩坠史莱姆」的临时清单已删除）。
2. **野性（`Feral`）从第 3 回合起常驻**：每回合打出的第一张 0 费攻击牌会**回手**。
   剧本里每次「野性捞回来的那张」都写成独立一条（`From = "feral"`），
   于是同一回合会打两次同样的 0 费攻击牌；回手的那张真卡若没有人打就留到回合结束（下次重建手牌时清掉）。
3. **超越光速（`Ftl`）抽上来的牌**（`From = "ftl-draw"`：螺旋钻机 / 打击）排在回合末尾；
   它们受能量限制，剧本里注明「没费」的那张会由 `BossScriptPlanner` 自动跳过。

## 4. 进阶

| 进阶 | 内容 |
|---|---|
| 8 | 升级「启动流程」（`BootSequence`）+ 升级「充电」（`ChargeBattery`） |
| 9 | 升级「冷却剂」（`Coolant`）/**「一心化万」**（`OneForAll`，多人换牌后的那张）+ 升级「超能光束」（`Hyperbeam`） |

（2026-10-24 之前是「进阶 8 升级启动流程 + 给数据磁盘」「进阶 9 升级所有爪击」，两条都已按用户要求替换。）

## 5. 多人

- 第二幕的候补从「猎骨储」变成「**猎机骨储**」：单人四选一，双人从 4 个里挑 2 个（站位按 猎→机→骨→储）。
- 第 4 回合那张：单人 = 冷却剂，**多人换成「一心化万」**（`MultiplayerCardSwap` 的 `(1, DefectBoss, Coolant, OneForAll)`）。

## 6. 已知的「只是挂着」的部分

- 镀金缆线 / 浮木 / 数据磁盘：镀金缆线的「最右侧球被动额外触发一次」由 `BossHandPanel.Extra`
  手工算进意图、数据磁盘的 +1 集中由 `BossHookBridge` 在战斗开始时补（原版钩子对影子玩家不触发）；
  浮木是「卡牌奖励可重掷」，战斗里本来就没有作用。
- 冰寒的数量口径已按用户订正收敛为「**只检测玩家数量**」（见 §3.1），不再有名字匹配的排除名单。

## 7. 首轮实测后的修正（2026-10-24 第二轮，v0.1.47）

| # | 症状 | 根因 | 修法 |
|---|---|---|---|
| 1 | 手牌被原版抽牌 / 野性回手搞乱 | 剧本每回合重建手牌，而「超越光速」会抽牌、「野性」会把 0 费攻击牌塞回手 | `BossDefinition.BlocksVanillaDraw`（Defect）→ `ShadowBossNoDraw` 拦抽牌；`FeralNoReturnPatch` 让野性回手返回原去向 |
| 2 | 螺旋钻击 0 伤害、意图单段 | `PlayOne` 走 `AutoPlay` 不付钱 ⇒ 历史里没有 `EnergySpentEntry`，而它的段数 = 本回合已花费能量之和 | 扣能量后补记 `CombatHistory.EnergySpent`；规划期 `BossScriptPlanner.HelixExtraHits` 预补段数 |
| 3 | 超能光束朝下 | `Hyperbeam.OnPlay` 用 `CombatState.Enemies.Last()` 当光束终点（Boss 打时那是她自己那侧） | `HyperbeamAimPatch` 把两处 VFX 的目标换成玩家侧真人 |
| 4 | 看不到球被动伤害 | 手牌栏只有「冰雹风暴 / 炸弹」两个特例 | `BossHandPanel.Extra` 改成一行多意图，新增 `OrbPassiveDamage`（含镀金缆线：最右侧球 +1 次触发） |
| 5 | 显示 2 点集中 | 那 2 点是「热修复」本回合 +2；数据磁盘 +1 挂在 `AfterRoomEntered`，早于影子 Player 建立 ⇒ 从未生效 | `BossHookBridge` 战斗开始时补 `DATA_DISK` 的 +1 集中 |
| 6 | 爪击伤害不涨 | 反编译 `Claw.OnPlay`：成长记在**每张爪击卡实例**上（`BuffFromClawPlay` → `Damage.BaseValue += Increase`），而我们的手牌每回合**重建** ⇒ 每回合清零 | 记在 Boss 身上（`CharBossMonster.ClawBonus`，每打出一次爪击 += 它的 `Increase`），`BossScriptRunner.BuildHand` 新造的爪击补上；意图里第二张起再加上本回合前面那几张的成长（`BossScriptPlanner` / `RefreshPlannedIntents` 各累加一次） |
| 7 | 球被动伤害预告偏低（8 vs 实际 12） | 预告按**当前**集中算 `PassiveVal`，漏了本回合还没打出去的「热修复」那 +2 集中 | `OrbPassiveDamage` 叠加 `BossDamagePredictor.FocusDeltaOf(牌)`（只算**还在手牌里**的那些，避免打出去后重复计），每颗伤害球 `PassiveVal + 集中变化` |
| 8 | 人工制品没算进去 | 超能光束的 −3 集中是**减益**（`HyperbeamFocusDownPower`），她出生自带 3 层人工 ⇒ 有层数时那条整体被挡（消耗 1 层），球被动不该被减；热修复的 +2 是增益、不受影响 | 预告按**实时**人工层数走一遍计划：负的集中变化有人工就跳过并消耗 1 层；`FocusDeltaOf` 里按 entry 认正负（`HYPERBEAM` = −，其余 `PowerVar<FocusPower>` = +）。热修复「临时→永久」的判定不预测，靠实时读她的集中层数自然体现 |
