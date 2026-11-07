# 多人联机专属换牌（设计 + 实现计划）

> 状态：**设计已定，代码未实现**。用户 2026-10-22 口头给出三条，本文件是唯一权威记录。
> 总原则：**只在多人（玩家数 ≥ 2）时替换**；替换后的效果**只作用于 Boss 侧（所有 Boss / 队友 Boss）**，
> 绝不给玩家任何收益（格挡、召唤物、小刀等都不给玩家）。

## 0. 为什么不能"直接用原版牌"

三条里被换掉的牌（冰川 / 吸引仇恨 / 斗篷与匕首）和换上的牌（休眠 / 骸骨军团 / 刀刃交响曲）
都是**以玩家自己为目标**的原版牌：原版 `OnPlay` 都是 async 状态机，目标写死为 `cardPlay.Player` 一侧。
所以只有两条路：

- **A. 自建三张 `CardModel`**：外观/名字沿用原版，但结算目标是 Boss 侧。
  代价：本模组目前**没有任何自定义卡牌**，需要先打通「注册自定义 CardModel + 立绘/卡面资源 + 本地化表」这条路，
  可行性未验证（Mod 框架是否允许新增 CardModel 需要先在 DLL 里查 `ModelDb` 的注册机制）。
- **B. Harmony 改写原版牌的结算**：拦截原版牌的 `OnPlay`（async 状态机，只能 prefix 整段跳过并自己重写效果），
  或者在 `PowerCmd.Apply` / `CreatureCmd.GainBlock` / `Osty` 召唤等下游命令上按「施法者是不是我们的 Boss」改目标。
  代价：脆弱，且要逐个效果点验证。

**结论**：先做 **B 的下游拦截**（不新增 CardModel、不碰卡面资源），因为三条效果最终都落在少数几个命令上：
「获得格挡」「召唤奥斯提」「生成小刀」。

## 1. 能力机（三层故障机器人）：冰川 → 休眠

- 只多人时替换（`ActIndex == 2` 且 `playerCount >= 2`）。
- 效果：**当回合**冰球（Frost）给**所有 Boss** 提供格挡；**不给玩家**格挡。
- 下游落点：冰球激发/回合结束给格挡走的是「给 owner（玩家）加格挡」的命令 →
  在 `CreatureCmd.GainBlock`（或等价命令）上判定「owner 是不是我们的 Boss 的影武者」，
  是则把格挡分发给所有 `CharBossMonster`。
- 待确认：原版「休眠」的数值变量（层数/回合数）与冰球数量的关系。

## 2. 根除骨：吸引仇恨 → 骸骨军团

- 只多人时替换（`ActIndex == 2` 且 `playerCount >= 2`）。
- **并且**：「能量汲取」的固定升级顺序里，`PULL_AGGRO` 那一项要换成 `LEGION_OF_BONE`
  （`CharBossMonster.DrainUpgradeOrderAct3` 第 1 组 `{ "DEFY", "PULL_AGGRO" }` → `{ "DEFY", "LEGION_OF_BONE" }`）。
- 效果：**只让 Boss 获得奥斯提**（玩家不会获得）；队友（其它 Boss）的奥斯提带 `MinionPower` 但**归属队友**；
  队友的奥斯提死亡后，**除非再次打出骸骨军团，否则不会复活**（不能像现在这样每回合自动补一只）。
- 下游落点：奥斯提召唤走 `PlayerCombatState.Pets`（`Osty`），我们的 `BossOsty` 已有「补建」逻辑，
  要按「这张牌是否打出过」来控制复活。

## 3. 刀猎：斗篷与匕首 → 刀刃交响曲

- 只多人时替换（`ActIndex == 2` 且 `playerCount >= 2`）。
- 升级与否**沿用原本按进阶的规则**（低进阶时斗篷与匕首 2 费 → 后面的「防御」打不出；0 费的「中和」仍可正常打）。
- 效果：**队友**（其它 Boss）对自己**跟踪锁定的目标**打出小刀，并且这**必须提前显示在意图里**。
- 下游落点：小刀是 `Shiv` 卡；意图侧我们有 `BossIntentBuilder.ShivsFrom(card)` 与 `BossIntentBuilder` 的
  「小刀 ×N」意图逻辑，需要扩展成「队友也要打，并且按队友自己的锁定目标结算」。

## 4. 与现有系统的耦合点（实现时必须一起看）

- 锁定关系：`Core/BossLockOn.TargetFor(boss)` / `IsLocked(boss, player)`。
- 意图发布：`CharBossMonster.PublishVanillaIntents()`（外部 mod 也读这份）。
- 手牌/状态栏：`UI/BossHandUi`（单例）、`UI/BossExtraStatusRow`（第二只的状态栏，已做）、
  `UI/BossStatusRow`（能量球已改为按行各记一份）。
- 多人判定：`Core/MultiBossPlan.WantsSecondBoss(out int playerCount)`。

## 5. 实现状态（2026-10-22 第二轮：无人值守，A1～A3 + B + C + F + D 顶栏 + 性能）

### 5.0 先把「换的三张牌真的会进手牌」验证掉了（不然全是死代码）
| 换的牌 | 出现在哪份剧本 | 证据 |
|---|---|---|
| `Glacier` → `Hibernate` | 能力机第三幕剧本 | `Core/DefectScript.cs` 第 1/4/6 回合的手牌 + 卡组（同文件 78 行） |
| `PullAggro` → `LegionOfBone` | 根除骨第三幕剧本 | `Core/NecrobinderAct3Script.cs` 第 1、2 回合手牌 + 卡组（65 行） |
| `CloakAndDagger` → `BladeSymphony` | 刀猎第三幕剧本 | `Core/SilentScript.cs` 第 1、7 回合手牌（**`Build()` 就是第三幕那份 11 回合剧本**） |

### 5.0b 出牌优先级（**用户口径决定的一条规则**）
换上的三张都比原牌贵（休眠 2 = 冰川 2；骸骨军团 2 > 吸引仇恨 1；刀刃交响曲 2（升级 1）> 斗篷与匕首 1），
而剧本给的每回合能量是按**原牌**算的。用户口径：「低进阶时斗篷与匕首 2 费 → **后面的防御**打不出；
0 费的中和仍可正常打」。意思是**换上那张要先打出去**，而不是被同档更便宜的防御挤掉。
所以 `MultiplayerCardSwap.IsSwappedIn(Type)` + `BossPlanner.PriorityOf` 里：换上的牌返回优先级 **-1（排最前）**。
单人局里这些类型根本不会出现 ⇒ 排序完全不变（零回归）。


| 块 | 状态 | 落点 / 说明 |
|---|---|---|
| A 框架：多人专属换牌表 | ✅ | `src/Hooks/Core/MultiplayerCardSwap.cs`；`BossScript.BuildHand` 里 `Resolve(...)` 生效；单人原样返回 |
| A1 冰川→休眠（能力机） | ✅ 效果已实现 | `src/Hooks/Patches/HibernateFrostBlockPatch.cs`。**公式全部来自原版**（反编译确认）：休眠 = 2 费技能、给自己挂 `HibernatePower`（计数 1，自己回合开始 −1 ⇒ 只当回合）+ 引导 2 颗冰球（升级 3 颗）；`FrostOrb.Passive/Evoke` 里 `if (Owner.HasPower<HibernatePower>())` 会把这笔格挡**再发给 `CombatState.Players` 里的每一个玩家**。每颗冰球给的格挡 = `PassiveVal = 2 + 集中`（回合结束被动），激发 = `EvokeVal = 5 + 集中`（`FocusPower.ModifyOrbValue`）。我们的 Boss 影子 Player 也在 `Players` 里 ⇒ **其它 Boss 天然就拿到同一笔格挡**；唯一要改的是「**不给真人玩家**」→ 在 `CreatureCmd.GainBlock` 前缀上，四条判据（目标真人 / `Unpowered` 且非 `Move` / 有一只带休眠的 Boss 其冰球数值与这笔完全相等 / 真人自己没在玩休眠+冰球）全中才拦成 0 |
| A2 吸引仇恨→骸骨军团（根除骨） | ✅ 效果已实现 | `src/Hooks/Patches/LegionOfBoneSummonPatch.cs` + `BossOsty.LegionSwapActive`。原版：`LegionOfBone`（2 费，Summon 6/8）`foreach (c in CombatState.PlayerCreatures.Where(IsAlive)) → OstyCmd.Summon(c.Player, 6)` —— 它本来就会给每只 Boss 各召一只（PetOwner = 那只 Boss ⇒ 归属天然正确），问题只是**也给真人玩家召**。前缀拦掉真人那一路；「死了不自动复活」由 `BossOsty.Update` 的闸门负责（`IsSwapped(PullAggro)` 为真时不再每回合补建，只有这张牌再打出才复活）；`MarkMinion` / 朝向 / 站位对**每只 Boss** 都跑（`BossFrameTick` 的 ④⑤ 改成遍历 `everyBoss`）。升级顺序已替换：`CharBossMonster.EffectiveDrainOrder()` 第三幕第 1 组 → `{ "DEFY", "LEGION_OF_BONE" }` |
| A3 斗篷与匕首→刀刃交响曲（刀猎） | ✅ 效果已实现 | `src/Hooks/Patches/BladeSymphonyPatch.cs`（整段接管 `BladeSymphony.OnPlay`）+ `BossIntentBuilder` 的 `BLADE_SYMPHONY` 意图分支 + `VolleyBossesOf`。原版：给「同阵营每个玩家（含自己）」塞 `Cards`(=2) 把小刀；改成**她自己 + 每只队友 Boss 各拿 2 把**（用户 2026-10-22 订正：刀猎自己也要拿到刀），并当场用 `ShivPlays.PlayAsync` 朝**各自的 `BossLockOn.TargetFor`** 扔出去（`BossPlanner.ChooseTarget` 优先锁定者）；意图提前画（Raw 意图，按各自的力量算，`AimsElsewhere` 时显示 0）。真人玩家一把也不给。升级沿用原进阶规则 |
| B 双 Boss 两套面板 | ✅ 已实现 | `UI/BossHandUi` 由单例改成 `Dictionary<CharBossMonster, BossHandPanel>`（新增 `PanelFor` / `PanelBosses` / `ReleasePanel`；`Panel` / `CurrentBoss` 变成「主面板」访问器，单人行为不变）；`BossFrameTick` 对每套面板各跑一次 `RunFrame` + 各缩 0.85；`BossOrbDisplay` 的激发高亮改成按 Boss 各记一份；`BossExtraStatusRow` 停用（每套面板自带状态条）；面板横向加了右边界夹取 |
| C 特殊效果按人分流 | ✅ 已实现 | 新增 `BossLockOn.RealPlayers / RealPlayerCount / SpecialEffectAppliesTo`（**关键**：影子 Player 也在 `CombatState.Players` 里，必须按 `Creature.Monster == null` 滤掉）。对应型 4 个（`DeviationPower` / `SleeveTrickPower` / `ResonancePower` / `BorrowedTimeBossPower`）都加了 `SpecialEffectAppliesTo` 闸门；全局型 `KnowThyPlacePower` 阈值改为 `25 × 真人玩家数`（2 人 = 50），且「所有玩家 −1 力量」只发给真人玩家。**可见性**：多人时描述框按用户早先的要求整块不显示（`BossSpecialUi.Ensure` 里 `ours >= 2 → Hide`），状态栏上的特殊效果图标仍然全显、悬浮可看完整说明 ⇒ 设计 §4.5「只显示对自己生效的那些 / 想看别人的鼠标悬浮图标」这条已经满足（用「全隐 + 图标悬浮」实现） |
| D 地图与顶栏 | ✅ 已实现（顶栏图标＝角色头像） | 反编译发现**原版顶栏本来就支持两只 Boss**（`_secondBossIcon` + `DOUBLE_BOSS` 悬浮说明填 `BossName1/BossName2`）⇒ 文字说明本来就列出全部 Boss。贴图：用户指定「就用玩家界面左上角那个角色头像」= `CharacterModel.IconTexture` / `IconOutlineTexture`（`ui/top_panel/character_icon_<角色>.png`，另有 `_outline`，与顶栏玩家头像同源）→ `Patches/TopBarBossIconFallbackPatch.cs` 里：我们的遭遇 → 角色头像（并设 `KeepAspectCentered` 防拉扁）；别人的遭遇 → 原版图标不动；取不到 → 退回原版占位图。**地图节点图标仍用占位图**（用户：图标不着急，先用占位的） |
| E 阶段 4 | ✅ 已完成 | **碎片储君**＝储君第一幕变体（`Core/DebrisRegentScript.cs` 的卡组/遗物/剧本 + `Powers/CometBossPower.cs` 彗星特殊效果），上一轮会话就已做完，本轮只是核对确认。**根除骨**＝亡灵契约师第三幕变体，特殊效果「预借时间」是**对应型（单体）**⇒ 已并入 C 的对应型闸门，并且做成**每玩家版**（`BorrowedTimeBossPower` 的累计改成 `Dictionary<Player,int>`，图标显示「离触发最近的那个玩家」还差多少）。**灵魂绑定**：用户 2026-10-22 明确说还没做、**暂时删掉这条需求** ⇒ 代码里本来就没有它的任何实现（只有旧计划文档提到），已从计划里移除 |
| F 意图按人清零 | ✅ 已实现 | `BossIntentBuilder.AimsElsewhere` + `RawAttackIntent(0)`：多人时「打的是别人」的单体/随机攻势意图对本地玩家显示 0（小刀通道与刀刃交响曲的队友通道也各按自己的目标判）；`MoveState.Intents` 是每生物一份、不分观看者，所以只在我们的渲染侧做 |

### 本轮额外做的（性能，用户反馈「boss 战仍然卡」）
- `BossOsty.HerLiveOstyNode`：原来被 `EnforceVisuals` / `EnforcePlacement` **每帧各调一次**，找不到节点时**递归扫整个战斗房间**；非亡灵系 Boss 永远扫不到 ⇒ 每帧两次全树扫描。现在「非亡灵契约师直接返回、不扫」+ 亡灵 5Hz 节流 + 缓存改成**按 Boss 各一份**。
- `BossFrameTick.Tick`：`AllBosses` 原来一帧调 5 次（每次新建 List）→ 只算一次；把「不跟手」的活（特殊效果说明框 / 额外状态栏 / 君王之剑兜底扫描 / 站位记录 / 面板缩放 / **跟踪锁定再平衡**）挪进 4Hz 慢车道。
- `BossHandPanel.OnFrame`：右手边「特殊效果说明框」的每帧 `Ensure`（要数敌人、找 Power、Place、`IsVisibleInTree`）降到 **10Hz**（两套面板同场时省一半以上）。
- `BossSovereignBlade.MirrorAllInScene`：从「每 120ms 扫整棵 SceneTree」改成「250ms + 只扫战斗房间子树」。
- `CharBossMonster.PublishVanillaIntents`：`MoveState.Intents` 的后备字段改成只反射查一次（它不是每帧调用，只是每次都要重新查字段）。

**仍未安装**：本轮按用户要求**没有**把 DLL 复制进 `D:\SteamLibrary\...\mods\CharacterBosses\`（那需要 danger-full-access，用户在场时再装）。构建产物在 `src\bin\Release\CharacterBosses.dll`，哈希见 handover 文件。


