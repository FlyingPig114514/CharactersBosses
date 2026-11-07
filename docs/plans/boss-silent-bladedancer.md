# 第三个 Boss：3 层静默猎手「刀猎」（Shiv / Blade build）

> 用户设计。前两个 Boss：灾厄骨（亡灵契约师，第一幕）、能力机（故障机器人，第三幕）。
> 本文是先落地的计划 + 反编译证据，实现完成后补「实机结果」。

## 1. 目标

- 第三幕（荣耀，`ActModel.Index == 2`）**第二个 Boss 槽** = 静默猎手「刀猎」；
  第一个槽保持能力机（故障机器人）。
- 特殊效果 **袖里乾坤**：每回合玩家**首次**打出耗能 ≥ 2 的牌时，把 **3 张小刀**加进静默猎手的手牌。
- 固定剧本 11 回合，之后 **7～11 循环**；意图实时计算（含"牌生成的小刀"的伤害）。
- 卡组 / 遗物按用户给的清单。

## 2. 名字 → 类型（starts2.dll 反编译逐个核对，全部存在）

| 用户写的 | 类型 | 关键数值 |
|---|---|---|
| 刀刃之舞 | `BladeDance` | 1 费技能，`Cards`=3 张**小刀**（升级 4） |
| 余像 | `Afterimage` | 1 费能力 |
| 尖啸 | `PiercingWail` | 1 费技能，全体 −6 力（升级 −8） |
| 幻影之刃 | `PhantomBlades` | 1 费能力：小刀获得保留；**每回合第一张小刀 +9**（升级 +12；层数叠加） |
| 刀扇 | `FanOfKnives` | **2 费能力**，`Shivs`=4 张**小刀**；`Shiv.HasFanOfKnives` ⇒ 在场时**小刀改为打全体** |
| 打击 ×3 | `StrikeSilent` | 1 费，6 伤 |
| 防御 ×5 | `DefendSilent` | 1 费，5 格挡 |
| 中和 | `Neutralize` | 0 费，3 伤 + 虚弱（升级 +1 伤）。**剧本里是未升级的**（2026-10-25 削弱），进阶 8 才升 |
| 生存者 | `Survivor` | 1 费，8 格挡 + 弃 1 张 |
| 斗篷与匕首+ | `CloakAndDagger` | 1 费，6 格挡 + `Cards` 张**小刀**（升级 2 张） |
| 先制打击 | `LeadingStrike` | 1 费，3 伤（升级 6）+ `Shivs`=2 张**小刀** |
| 隐秘匕首 | `HiddenDaggers` | 0 费技能，`Shivs`=2 张**小刀** |
| 终结技 | `Finisher` | 1 费，按本回合已打出的攻击牌数造成 6 伤/次 |
| 切割 | `Slice` | 0 费，6 伤 |
| 小刀 | `Shiv` | 0 费 token，4 伤（**升级 +2**） |

遗物（9 个全存在）：`RingOfTheSnake`(蛇之戒指) / `Shuriken`(手里剑) / `CallingBell`(召唤铃铛) /
`Lantern`(灯笼) / `OrnamentalFan`(精致折扇) / `Kunai`(苦无) / `HelicalDart`(螺线飞镖) /
**`DiamondDiadem`(钻石头冠)** / `TuningFork`(音叉)。

> **2026-10-25 削弱**：`Sai`(钗) → **`DiamondDiadem`(钻石头冠)**；钻石头冠 = 她的回合开始时若还是第 1 回合，
> 获得 20 格挡（`BlockVar(20, Unpowered)`）+ 1 层「模糊」（`BlurPower`，格挡多留一回合）。
> 影子玩家的遗物收不到原版广播，所以由 `BossHookBridge.FireCombatStart` 在战斗开始时替她触发一次。

「成长」= 手里剑/苦无/精致折扇：**每打出 3 张攻击牌** → +1 力 / +1 敏 / +4 格挡。它们是原版遗物，
`AfterCardPlayed` 自己会数，不需要我们实现 —— 意图数字走 `Hook.ModifyDamage` 自动跟着涨。

## 3. 剧本（用户给的，能量固定 2/回合）

| 回合 | 牌（按顺序） | 用户备注 |
|---|---|---|
| 1 | 余像、斗篷与匕首+、中和、防御、防御 | 2 费打完，两张防御没费 |
| 2 | 先制打击、防御、打击 | 打击没费 |
| 3 | 切割、打击、终结技 | |
| 4 | 幻影之刃、尖啸、隐秘匕首 | 隐秘匕首的 2 张小刀也打出去（第一把吃幻影之刃 +9） |
| 5 | 刀扇、铃铛的诅咒、防御 | 刀扇给 4 张小刀；诅咒打不出去、防御没费 |
| 6 | 刀刃之舞、生存者、防御 | 防御被生存者丢弃 |
| 7 | 斗篷与匕首+、防御、中和 | 洗牌 |
| 8 | 先制打击、防御、打击 | 打击没费 |
| 9 | 切割、打击、终结技 | |
| 10 | 铃铛的诅咒、防御、防御 | 诅咒打不出去 |
| 11 | 生存者、防御、隐秘匕首 | 防御被生存者丢弃 |

**2026-10-24 削刀猎**：第 4 回合的「无尽刀刃」换成「幻影之刃」，
⇒ 无尽刀刃「每回合开始印一张小刀」带来的那些小刀**全部删除**（第 5～11 回合首位的 `Shiv` 都没了）。

循环：`LoopFrom = 7, LoopLength = 5`。

**能量**：用户的每回合账都是 2 费 ⇒ 每回合写死 `BossTurn.Energy = 2`
（`PrepareTurn` 里剧本能量覆盖优先于定义值，这样霓奥祝福 −1 / 灯笼 +1 都不会让意图漂）。

## 4. 要改的代码

| 文件 | 改动 |
|---|---|
| `Core/SilentScript.cs`（新） | 卡组 20 张、遗物 9 个、11 回合剧本 |
| `Powers/SleeveTrickPower.cs`（新） | 袖里乾坤：`BeforeCardPlayed` 里判断「真玩家 + 耗能≥2 + 本回合首次」→ `Shiv.CreateInHand(影子玩家, 3, combat)` |
| `Core/BossDefs.cs` | `Silent` 填第三幕：Deck/Relics/Script/SpecialLoc/SpecialPower |
| `Encounters/CharBossEncounters.cs` | `ActTable[2] = { 2, 1, 2 }`（能力机第 1 槽、刀猎第 2 槽） |
| `Core/BossPlanner.cs` | `PlannedPlay` 增加 `GeneratedShivs`（这张牌会造几张小刀） |
| `Core/BossScriptPlanner.cs` | 规划时把「造小刀」的牌后面补上对应数量的小刀出牌（0 费，随时可打） |
| `Core/BossIntentBuilder.cs` | 造小刀的牌在**同一张卡下面**追加「小刀伤害 ×数量」的攻势意图（用户示例：先制打击 → `3` 和 `4*2`） |
| `Monsters/CharBossMonster.cs` | 出牌阶段末尾把手里剩下的小刀（袖里乾坤塞的）也打出去 —— 这部分**不进意图**（用户：若触发袖里乾坤，实际伤害会更高） |
| `Core/BossScript.cs` | `BuildHand` 重建手牌时**保留 token 小刀**，否则袖里乾坤塞的 3 张会在她回合开始被清掉 |
| `Patches/BossSpecialPowerIconPatch.cs` | 新特殊效果也用原版「好奇」灯泡图标（用户要求：所有特殊效果统一图标） |
| `Core/DebugMode.cs` + `Monsters/CharBossMonster.Descriptor.cs` | 调试用：`charbosses_debug.txt` 写 `silent@3` 可强制按第三幕档位出（否则只能真打到三层才能测） |
| `content/localization/{zhs,eng}/monsters.json` | `SILENT_BOSS.special` |
| `content/localization/{zhs,eng}/powers.json` | `SLEEVE_TRICK_POWER.title/description` |

改本地化文件 ⇒ **必须重打 pck**（见 handover §3）。

## 5. 血量 / 其它待定

- 血：沿用 `BossDefs.Silent.Hp[2] = 240`，加涅奥祝福 `150×(2+1)=450` ⇒ **第三幕 690 血**。
  用户若觉得太薄/太厚，只改 `Hp` 数组第 3 项。
- 手牌数：剧本自己造牌，`HandSize` 不参与。

## 6. 小刀在 UI 上的**正确**行为（用户三轮纠正后定稿）

| 来源 | 回合开始时 | 打出时机 | 意图 |
|---|---|---|---|
| 生成刀牌（刀刃之舞/斗篷与匕首/隐秘匕首/先制打击/刀扇） | 手里**只有那张牌**，不摆刀 | 那张牌**打出后**，真刀进手牌 → 亮起、各自带攻势、立刻打掉 | 刀**不在**回合开始显示；但**那张牌本身**要显示「小刀 ×N」的攻势（如先制打击 = `3` + `4*2`） |
| 袖里乾坤（每回合玩家首次打出耗能 ≥ 2 的牌 → 3 张） | 触发瞬间就塞进她手牌 | **插到计划最前、最先打出** | 每把刀下面各有攻势；终结技段数同步变 |
| 无尽刀刃（剧本里每回合开头那张 Shiv） | 本来就在手牌（剧本造牌） | 最先打出之一 | 同上 |

实现要点：
- `BossIntentBuilder.IntentsFor` 对生成刀牌追加 `MultiAttackIntent(4, N)`；
- `SilentBlades.RecomputeFinisherIntents` 按计划重算终结技段数，并把「生成刀牌会生出的刀」也算成攻击；
- `SilentBlades.FrontloadRealShivs` 收集手牌里**未进计划**的真刀 → 插到计划头部 + 重算意图 + 刷新手牌栏；
  出牌阶段在生成刀牌结算后、以及脚本打完后各调一次；
- `CharBossMonster.PlayOne` 统一出牌（主循环与小刀子循环共用），打出的刀会从手牌移除；
- ⚠ 不要"回合开始就把生成的刀摆出来"（用户明确否掉过一次）。

## 7. 验证

- 日志：`SILENT_BOSS act3 deck(...)`、`特殊效果已施加: SleeveTrickPower`、`剧本第 N 回合: 打 [...] 跳过 [...]`、
  `袖里乾坤：玩家打出 X（耗能 ≥ 2）→ 给静默猎手塞 3 张小刀` / `3 把小刀插到计划最前`、
  `小刀摆上牌桌: N 把`、`PLAY ... -> 名字（hp=… side=…）`；
- 画面：生成刀牌打出后刀才出现并亮起；袖里乾坤的刀在最前；终结技段数随之变化；
  尖啸显示红↓；第 6/11 回合防御被生存者丢掉；她身上不再冒出奥斯提。

## 8. 2026-10-25 削弱（用户三条）

| # | 改动 | 落地位置 |
|---|---|---|
| 1 | 遗物 **钗 `Sai` → 钻石头冠 `DiamondDiadem`**（回合开始时若还是第 1 回合：+20 格挡、+1 层「模糊」） | `Core/SilentScript.Relics()` |
| 2 | **中和降回未升级**（第 1、7 回合那两张：3 伤 + 3 层虚弱，原来写死成中和+） | `Core/SilentScript.Build()` 第 1、7 回合 |
| 3 | **进阶 8 改成「升级中和」**（原来进阶 8 是「多给一件遗物 风的女儿」⇒ 风的女儿**不再出现**） | `Core/BossDefs.cs` 的 `AscensionUpgradesByAct` 加 `(8, 3, Neutralize)`；`AscensionRelicsByAct` 里删掉 `(8, 3, DaughterOfTheWind, Sai)` |

要点：
- 同一张 `Neutralize` 在涂毒猎（第二幕）是**进阶 8** 升级、在刀猎（第三幕）也是**进阶 8** 升级 —— 两张表里都有条目，
  所以按幕分开配；剧本里的 `Upgraded` 一律写 `false`，升级只由进阶表给（和灾厄骨那条「升级只看进阶/能量汲取」同一个原则）。
- 钻石头冠的 `AfterSideTurnStart` 要求 `PlayerCombatState.TurnNumber <= 1`；影子玩家收不到原版广播，
  由 `BossHookBridge.FireCombatStart`（战斗开始时把 TurnNumber 临时摆回 1 再逐件遗物直接调）替她触发，
  所以第 1 回合身上就有 20 格挡 + 1 层模糊。

