# 构建日志（BuildInfo.Tag 历史）

> 2026-11-22 从 `BuildInfo.Tag` 搬出来单独保存（Tag 里只留最近一条 + 指向本文件）。
> 旧条目逐字来自 `git show HEAD:src/Hooks/Core/BuildInfo.cs` 的 Tag 常量（v0.2.871 及其之前，共 236 条）。
> 最新版本（0.2.889）是当前 Tag，同时在各条 `workshop/changenote.txt` 里也有面向玩家的摘要。

## v0.2.905（2026-11-23）：疾风观削弱版

用户 2026-11-23 第三轮（「削弱」）：

- **拦掉疾风连击的回手**：A 版写在 `WatcherCombatHelper.OnStanceChanged`（转换姿态时把弃牌堆里的
  疾风连击挪回手牌），B 版写在卡自己的 `FlurryOfBlows.OnStanceChange` —— 两条都压，且只对
  `WATCHER_FLURRY` 这只生效。
- **「力竭」图标换成崩坠力竭的原图**：从 `Downfall.pck` 的 `res://Awakened/images/powers/drained_power.png`
  解出 PNG → 导入成 `res://CharacterBosses/images/powers/flurry_drained.png`（`tools/art/pck_extras.txt`）。
- **特殊效果描述去掉「1」**（一个能量图标就代表 1 能量），16 语言；
  **恐惧版**把「她的斋戒不会使她在回合开始时失去能量。」**单独分段**（`\n`）。
- **斋戒的能量降低真的免疫了**：v0.2.903 的 Harmony 前缀没生效 ——
  `WatcherRuntimePatches.TryInstall()` 只在 `BuildInfo.LogOnce` 里调**一次**，
  那时观者模组程序集可能还没加载（`ModAssembly() == null` 直接 return，`_tried` 永远是 false，
  之后再没人调它）。现在：① `BossFrameTick.Tick` **每帧重试**；② `PrepareTurn` 里
  `Core.WatcherRuntimePatches.StripFastingEnergyDown(this)` 直接把 `EnergyDownPower` 从她身上摘掉兜底。
- **削弱**：苦无 → **泪滴吊坠盒**（去掉「每 3 张攻击 +1 敏捷」的成长；代价是她开局就在平静，
  第 5/9 回合「化体为空」退平静会按原版拿 +2 能量）；卡组去掉「安宁」。
- **全新意图表**（普通模式）：1 以手拒之·疾风连击·防御｜2 打击·打击·防御·奇迹(不出)｜
  3 奇迹·斋戒·心灵堡垒·防御(没费)｜4 供奉·追击·腰带抽打｜5 化体为空·疾风连击·警惕·疾风连击·打击(没费)｜
  6 洗牌·疾风连击·防御·防御(没费)｜7 同 4｜8 防御·打击(没费)·打击(没费)｜9 同 5；之后 6～9 循环。
  用户标注「（恐惧模式下打出）」的牌 = 恐惧模式多打出来的那一张：
  第 5/9 回合多一张打击（4 费）、第 6 回合两张防御都打（2 费）、第 8 回合两张打击都打（3 费）。
- 能力面板：单体高压回合改回 `4、5、7、9`（群体高压仍是「无」）。

## v0.2.904（2026-11-23）：疾风观 —— 恐惧模式第 5 / 8 回合单独一套

用户实机截图（恐惧模式第 8 回合只有 1 张打击打出来）后订正：

- **恐惧模式第 5 回合**意图：`心灵堡垒、化体为空、疾风连击、安宁、疾风连击`
  （普通模式仍是「化体为空、疾风连击、心灵堡垒、安宁、疾风连击」）。
  心灵堡垒提到最前面 ⇒ 她随后「化体为空」退出姿态时才吃得到心灵堡垒的格挡。
- **恐惧模式第 8 回合**意图：`打击、打击、防御（没费）`，本回合 **2 费**（普通 1 费、只打一张打击）。
  依据：她恐惧模式下**自身免疫「能量降低」**（v0.2.903 的 `InstallFastingNoEnergyDown` 已经压掉
  斋戒给她叠的 `EnergyDownPower`），斋戒不再吞掉那 1 点能量。
- 实现走 `BossScript.Resolve`（`DreadTurnOverride`）而不是在 `Build()` 里判 `DreadMode.IsActive`
  —— `BossDefs` 是静态初始化，那时 `IsActive` 恒为 false（养手骨 / 华丽猎 / 轰击储同款坑）。

## v0.2.903（2026-11-23）：疾风观订正

- 第 6 回合意图：疾风连击、防御、**打击（没费）**（原来是「防御、防御（没费）」）；
  第 8 回合意图：打击、**打击（没费）、防御（没费）**（原来是「打击、打击×2（没费）」）。
- 能力面板：单体高压回合加上第 1 回合 ⇒ `1、4、5、7、9`；群体高压回合 ⇒ `无`。
- 恐惧强化：升级 **疾风连击**（原来为空）。
- **斋戒不再使她在回合开始时失去能量**：两份观者 mod 的「斋戒」除了给力量/敏捷，还会给持有者自己
  叠一层 `EnergyDownPower`（`AfterEnergyReset` → `PlayerCmd.LoseEnergy(Amount, player)`）。
  这个钩子对我们这只 Boss **是派发的**（所以「明耀酊剂」那条才要单独压掉）⇒ 她第 3 回合打出斋戒后
  每回合被扣 1 点能量，剧本里写死的能量全部少 1，第 6/8 回合那几张牌全变「没费」。
  现在 `Core/WatcherRuntimePatches.InstallFastingNoEnergyDown` 只压**她**那一次（真人玩家的斋戒不变）。
- 特殊效果文案（`WATCHER_FLURRY_BOSS.flurry` / `BOSS_FLURRY_POWER.description` 及 dread 版，
  16 语言）末尾补一句「她的斋戒不会使她在回合开始时失去能量」。

## v0.2.902（2026-11-23）：新 Boss —— 一层 2 号「疾风观」

用户原话：「加一个新boss，1层2号观者boss疾风观（内部名用疾风连击的英文）」。

- 内部 Key **`WATCHER_FLURRY`**（疾风连击 = Flurry of Blows），遭遇 `WATCHER_FLURRY_BOSS_ENCOUNTER`，
  控制台 `fight WATCHER_FLURRY_BOSS_ENCOUNTER`；挂 `BossDefs.Watcher.ExtraVariants[0]`（第一幕，与预见观同幕，
  按 `BossLineups.WatcherFlurryActive()` 分辨），候补表 `BossKeys.WatcherRosterAct1()` 追加。
- 特殊效果「疾风连击」（`Powers.BossFlurryPower`）：观者的「疾风连击」对玩家造成**未被格挡**的伤害时，
  该玩家叠加 1 层「力竭」（`Powers.FlurryDrainedPower`，= 崩坠 `DrainedPower`：`AfterEnergyReset` 里
  `PlayerCmd.LoseEnergy` 后自移除；**单张牌最多 1 层**，不是每段）。图标借观者 mod「能量降低」那张。
- 遗物：至纯之水 / 涅奥的祝福 / 手里剑 / 苦无。卡组 13 张（疾风连击×2、打击×3、防御×3 …）。
- 剧本 9 回合、6～9 循环（第 6 回合洗牌）；进阶 8 升 以手拒之·心灵堡垒，进阶 9 升 供奉·斋戒。
- `WatcherCards` 补 4 张牌的反射访问器（`FlurryOfBlows` / `TalkToTheHand` / `FollowUp` / `Fasting`）
  与本体遗物 `Kunai`；两份观者 mod 的类名/模型 id 都从各自 dll 核过（A 版斋戒的 id 是 `WATCHER_FASTING2`）。
- 16 语言文案（特殊效果的描述里能量用观者能量图标）；`tools/wiki-dump` 的 `VariantName` 加「疾风观」，
  中英两版百科同步重生成。

## 2026-11-22 这一批（v0.2.872 ~ v0.2.889，摘要）

- v0.2.889 储君的支配：描述按当前幕重新构造 LocString（第 2 幕显示「粘液」而不是「碎屑」）；`DominionIconOf` 补 `RegentDominionPower → 储君头像`。
- v0.2.888 群蛇形态改为自行结算：从 `SerpentFormPower.amountsForPlayedCards` 取走待结算伤害并摘掉条目（原版 Remove 返回 false 不会重复），直接对锁定者 `CreatureCmd.Damage`，不再依赖 `HittableEnemies` 随机挑目标。
- v0.2.887 群蛇形态收窄窗口 8→20 帧；新增「选靶收窄被消费」诊断（单人也打）。
- v0.2.886 群蛇形态只在 `amountsForPlayedCards` 还有条目时才收窄（避免收尾帧串到别人的随机目标）。
- v0.2.885 群蛇形态一次性收窄窗口 2→8 帧（挑目标前有 0.1~0.2 秒 await，2 帧必过期）。
- v0.2.884 群蛇形态删除「可命中预判」（它把整次伤害跳过了，日志实证 16:21:16）；改回无条件收窄到锁定玩家。
- v0.2.883 wiki 能力面板 41/41（修三处 key/程序集/读取层级）；轰击储描述 16 语言改成「所有玩家获得 1 层光谱偏移」。
- v0.2.882 群蛇形态诊断日志；轰击储描述 zhs/zht/eng 先行修改。
- v0.2.881 多人（真人玩家 > 1）不再弹特殊效果说明框。
- v0.2.880 不同步摘要补「玩家侧牌堆指纹」。
- v0.2.879 群蛇形态（错误地）加了可命中预判 —— 后来被 v0.2.884 撤销。
- v0.2.878 不同步摘要补 Boss 牌堆顺序指纹（首尾牌 + FNV-1a 哈希）。
- v0.2.877 形态牌特效朝向改为跟本体净方向对齐。
- v0.2.876 能力机「集中 → 力量」进伤害预览（只对 DEFECT_BOSS 第三幕）。
- v0.2.875 能力机第 3~5 回合意图改写；修事件机意图里冒出一心化万（缺 VariantTakesOver 闸门）。
- v0.2.874 数据上传三行诊断；进阶数据搬进变体表（wiki 可自动导出）。
- v0.2.873 日志暴涨 350MB~1.6GB 真凶修复（把正在飞向能力栏的卡面释放了）；恐惧支配不再在 Boss 房生效。
- v0.2.872 死掉的 Boss 不再跑整手 + 整手异常兜底 + 「战斗 N 秒没推进」看门狗。

## v0.2.871 及更早（逐字）

### v0.2.871 

2026-11-21 (**修：支配给的人工制品在多人被算了两遍（4 人局 8 层 ⇒ 应为 5 层）**）—— 用户 2026-11-21：「我记得我刚刚 4 人联机，2 层故障机器人的支配，但是所有敌人都有 8 层人工制品（不算本来就有人工制品的怪），你这直接 2*4 了」。根因是**双重计入**：我们 v0.2.857 起自己按 `expected = 本幕层数 + (人数 − 1)` 算发放量，而 `PowerCmd.Apply` 里原版还会再按 `ArtifactPower.GetScaledAmountForMultiplayer`（`amount + 人数 − 1`）加一次 ⇒ 第 2 幕 4 人 = 2 + 3 + 3 = **8**。现在**只把「本幕基础层数」交给原版**（第 2 幕 = 2 层），缩放完全由原版 `PowerCmd.Apply` 负责：4 人局 = 2 + 3 = **5**（2 人 3 / 3 人 4 / 5 人 6）。配套改动：`_grantedArtifact` 改记「我们发了多少基础层数」（幂等判据随之从 `expected` 换成 `amount`），`_expectedArtifactTotal` 仍是「自带 + 本幕层数 + 人数−1」当兜底回读的期望值，日志明细写成 `自带 X + 支配基础 Y（多人 N 人 ⇒ 原版再 +N−1 ⇒ 期望 Z）`。

### v0.2.870 

2026-11-21

### v0.2.869 

2026-11-21

### v0.2.868 

2026-11-21

### v0.2.867 

2026-11-21 (**联机两条：储君的支配不再漏进 Boss 房 + 「一代音乐」选项对每一场 Boss 战都生效**）—— 用户 2026-11-21：「恐惧模式联机时，主 boss 为储君时首次进入 boss 房储君的支配仍会生效（让玩家选择 1 张牌变化为眩晕/粘液/碎屑），然后触发联机不同步（sl 可解决）」+「设置中音乐开启 1 代音乐好像有问题，开启后似乎并不是每场 boss 战都会把 bgm 替换为 1 代音乐」。①储君的支配：`RegentDominion.AfterRoomEntered` 本来就有「Boss 房不生效」的闸门，但**已经挂到玩家身上的那层 `RegentDominionPower` 会跟着进 Boss 房**，而它是在**玩家回合开始**才动手（`AfterPlayerTurnStartEarly` / `AfterPlayerTurnStart`）⇒ 首次进 Boss 房第 1 回合照样弹「选 1 张牌变形」。这是**玩家选择**，联机时两端各选各的 ⇒ 立刻分叉（读档后支配已经不在，所以 SL 就好了）。现在在 `TryResolveAsync` 开头补同一道闸门（`DreadMode.SkipBecauseActBossRoom` + `RunState.CurrentRoom`），并给「亡灵契约师的支配」的 `AfterPlayerTurnStart` 也补上（同一个残留问题）。②一代音乐：`BossBgm.OnCombatStart` 里有一道「本场遭遇自带专属 BGM ⇒ 我们不接管」的闸门（原版自己的 Boss、别的模组的遭遇都算），选②时它**仍然放行**那部分战斗 ⇒ 那些场次照旧放原曲。现在 `BgmSource == BgmGen1Boss` 时这道闸门直接失效（一代曲优先），其它模式行为不变。

### v0.2.869 

2026-11-21

### v0.2.868 

2026-11-21

### v0.2.867 

2026-11-21 (**削弱炼药猎：二阶段额外那一瓶改成「流动铜液」(+3 荆棘)，不再是小刀流**）—— 用户 2026-11-21：「削弱炼药猎，2 阶段额外喝的要改为流动铜液（获得 3 荆棘），不再为狡诈药水也不再额外打出小刀」。改动：①`BossPotions.DrinkAsync` 的混沌药水分支里，二阶段（栏位里已没有瓶中精灵）补进那一格的东西从 `CunningPotion` 改成 **`LiquidBronze`**（反编译：`PowerCmd.Apply&lt;ThornsPower&gt;(… DynamicVars[\"ThornsPower\"] = 3)`）—— 她只是获得 3 层荆棘，不生成小刀、也不额外打出；②`SilentBrewerScript` 第 4 / 8 回合的药水列表末位改成 `LiquidBronze`（这两回合原本都是 `CunningPotion`），第 8 回合备注改成「混沌补出的流动铜液 → 她获得 3 层荆棘」；③`UI/BossPotionRow` 的占位格（回合开始时那瓶还没炼出来、先画在格挡药水左侧）跟着改成 `LiquidBronze`（显示强化意图，不再是 3 段攻势）；④删掉 `PlannedDirectDamage` 里「狡诈药水 3 把小刀要计进薄荷尖塔」那段（现在没有小刀了，荆棘不是伤害）。

### v0.2.868 

2026-11-21

### v0.2.867 

2026-11-21 (**联机两条：杂耍战「惊逃」打出的牌钉到被锁定玩家 + 群攻意图在无实体下钳到 1**）—— 用户 2026-11-21：「联机时杂耍战用惊逃打出的牌不一定会攻击被他锁定的玩家」「联机时，当你有无实体时，不锁定你的 boss 使用群伤的意图不会被正确的降低为 1」。①惊逃（`Stampede`）：原来 `CardCmd.AutoPlay(ctx, card, null, …)` **目标传 null** ⇒ 原版按自己的规则挑（`RandomEnemy` 随机、`AnyEnemy` 取第一个）⇒ 联机会打到别人。现在与其它「跟踪锁定」补丁同一套：单体/随机目标显式传 `BossLockOn.TargetFor(boss)` 并把 `HittableEnemies` 用 `PushNarrowTo/PopNarrowTo` 收窄到那位；`TargetType.AllEnemies`（群攻）保持原样（群攻就该打所有玩家）。日志写明「目标钉成被锁定玩家 X」/「群攻 ⇒ 打所有玩家」。②群攻意图的无实体上限：`LiveMultiAttackIntent` 的每段数字是按「**第一个真人玩家**」当受害者跑原版伤害管道算的，而 `IntangiblePower.ModifyDamageCap` 只对**持有者**生效 ⇒ 联机时自己不是「第一个」就完全没被钳制（明明只挨 1 点却显示 12×2）。现在每段算完再查一次「本机玩家有没有 `IntangiblePower`」，有就 `min(每段, 1)`（段数不变），日志 `群攻意图：本机玩家有无实体 ⇒ 每段从 N 钳到 1（M 段）`。

### v0.2.867 

2026-11-21 (**狡诈药水伤害预览按用户公式重算（含异鱼之油/饰品力量、群蛇形态、无实体）+ 薄荷尖塔也算它**）—— 用户 2026-11-21：「狡诈药水的伤害没有被薄荷尖塔计算。狡诈药水的伤害预览偏低（预览为 15*3 实际为 19*3）且没有显示群蛇形态带来的额外伤害（所以实际为 25*3）。你可能漏加了它本回合喝药获得的力量（如喝异鱼之油）和她喝狡诈药水（通过爬行动物饰品）获得的力量。那个药水的伤害预览应该这么算：（6（基础）+ 本回合爬行动物饰品提供的力量 + 药水额外提供的力量）*虚弱易伤等 + 4 或 6（群蛇形态伤害，普通 4 / 恐惧 6，不受虚弱易伤影响）*3（3 把刀 3 段）。如果被锁定的玩家获得无实体，显示为 1*6（3 把刀 + 群蛇 3 段伤害）」。做法：①新增 `CunningShivPreview(potion, boss)` —— 每段 = (6 + 当前真实力量 + **还没喝掉的药水会给的力量**) × 虚弱(×0.75) × 易伤(×1.5) + 群蛇形态层数（`SerpentFormPower.Amount`，反编译确认它走 `ValueProp.Unpowered` ⇒ 不吃力量/虚弱/易伤），再 × 3 刀；被锁玩家有**无实体** ⇒ 每段 1 点、段数 = 3 刀 + 3 次群蛇 = 6（显示 1×6）。②`PendingPotionStrength` 把「力量来源」拆成两块：爬行动物饰品（`ReptileTrinketPower` 每瓶 +3，已有逻辑）＋ **`FyshOil`（异鱼之油）自己的 `Strength` 变量**，都只算「排在这瓶之前、还没喝掉」的那几瓶（已经喝掉的力量已在她身上，不双算）。③`IntentFor` 不再依赖意图节点算力量（原版 `AttackIntent.Multi` 只是显示我们给的数）⇒ 直接把整笔烤成数字。④`PlannedDirectDamage` 把狡诈药水的 `每段 × 段数` 记进 **single**（只打被锁定玩家），薄荷尖塔的 ←N 现在会算上它；判据与真实炼制一致（栏位里还有瓶中精灵 ⇒ 不会出现狡诈 ⇒ 不计）。日志：`狡诈药水意图：3 段 × 25 = 75（基础 6 + 当前力量 0 + 未喝药水 13（饰品 9 + 异鱼之油 4） = 19 + 群蛇形态 6 = 25 × 3 刀）`。

### v0.2.866 

2026-11-21 (**炼药猎：回合开始时把「狡诈药水」的意图先画在格挡药水左侧（含爬行动物饰品的力量）**）—— 用户 2026-11-21 截图 + 口径：「由于回合开始时狡诈药水不存在，所以不能显示其意图。改为在格挡药水左侧的位置写上狡诈药水的意图（基础为 6*3，受力量虚弱等加成，要考虑回合内喝药触发爬行动物饰品增加的力量（也可能已经算上了，别重复计算）），在狡诈药水被炼出来后（喝混沌药水后）改为把意图显示在狡诈药水上（这一步目前是做到了）」。做法：①`UI/BossPotionRow.AddTo` 先把持有药水做成**显示列表**，若「本回合计划里有 `CunningPotion` 但栏位里没有它、且栏位里**已经没有瓶中精灵**（与 `BossPotions.DrinkAsync` 混沌药水的判据完全一致 ⇒ 一阶段不画）」就插入一个**占位格**：真的狡诈药水贴图 + 同样的意图、`Modulate` 压到 55% 透明（一眼看出「还没炼出来」），位置插在**格挡药水左边**（没有格挡药水就最左）；真炼出来之后占位格自然消失、意图画在真格子上（两条路共用 `IntentFor`，数字必然一致）。②`IntentFor(potion, boss)` 新增 `PendingTrinketStrength(potion, boss)`：反编译 `ReptileTrinket.AfterPotionUsed` → `ReptileTrinketPower`（`TemporaryStrengthPower`）每喝一瓶 +3（层数优先读遗物变量），**只算「排在这瓶之前、还没喝掉」的那几瓶** —— 已经喝掉的真实力量在 `Creature` 身上、意图节点渲染时会自己加，因此**不重复计算**；于是回合开始（一瓶都没喝）显示的就是「最后那一口真正打出来的数」（6+9）＝之后的显示值。日志：`药水栏：本回合会喝到「狡诈药水」但它还没炼出来 ⇒ 在格挡药水左侧补一个占位意图（位置 N，本回合已喝 M 瓶）` 与 `狡诈药水意图：3 段 × 15（基础 6 + 未喝药水的爬行动物饰品力量 9；当前真实力量由意图节点自己加，不重复计算）`。

### v0.2.865 

2026-11-21 (**炼药猎两条：恐惧版群蛇形态真的升级了 + 第 8 回合二阶段的「狡诈药水→3 张小刀」终于给得出来**）—— 用户 2026-11-21：「恐惧模式下炼药猎的群蛇形态还是没有升级；并且第八回合 2 阶段（失去瓶中精灵后）的额外意图（酿并喝狡诈药水，打小刀）从来都没实现过（不论普通还是恐惧）」。①群蛇形态：`BossDefs` 里 `SILENT_BREWER` 变体的注释写着「恐惧版的升级改走 `BossVariant.DreadUpgrades`」，但那个字段**一直没填** ⇒ 恐惧里她打的仍是未升级的群蛇形态。现在补上 `DreadUpgrades = { SerpentForm }`（走 `AppendDreadUpgrades` 运行期通道，卡组与剧本手牌里同名那张一起升级）。②狡诈药水：本机日志实证 `混沌药水：按初始药水补满栏位` 紧接 `剧本要喝 CunningPotion，但栏位里没有 → 跳过` —— 根因是**顺序**：`InitialPotions()` 里「瓶中精灵」排在最后一位，替换成「狡诈药水」后仍排最后，而栏位只有 8 格、前面 7 瓶先占满 ⇒ 狡诈药水一次都没给出去（`Give` 没空位就返回 null）。现在先做「精灵→狡诈」替换并把**狡诈药水提到最前**再逐瓶发，日志同时报「给出 N 瓶 / 因栏位满没给出 M 瓶」。

### v0.2.864 

2026-11-21 (**修：虚无骨恐惧版「开局就上恶咒」只对战斗中生成的牌生效**）—— 用户 2026-11-21：「虚无骨在恐惧模式下会在战斗开始时就给予所有玩家恶咒，但这个恶咒似乎失效了，只有在战斗中生成的牌才被添加了虚无」。本机日志证实恶咒**确实挂上了**（`powers=[HEX_POWER:1,…]`），所以问题不在施加，而在**作用范围**：反编译 `HexPower` —— `AfterApplied` 遍历 `PlayerCombatState.AllCards` 逐张 `CardCmd.Afflict<Hexed>`，`AfterCardEnteredCombat` 管之后新进战斗的牌，`TryModifyKeywordsInCombat` 只给「有 `Hexed` 的牌」加 `Ethereal`。我们在「战斗开始」那一刻挂恶咒时**玩家的战斗牌堆还没铺开**（`AllCards` 为空）⇒ 那次循环一张都没挂上，只有后来生成的牌走 `AfterCardEnteredCombat` 才有虚无。修法：`BossVoidCallPower` 新增 `SweepHexAsync(why)` —— 自己遍历每个真人玩家的 `PlayerCombatState.AllCards`，给**还没有任何 affliction** 的牌补 `CardCmd.Afflict<Hexed>`；调用点两处：①恐惧版挂完恶咒立刻补一次；②`AfterSideTurnStart(Player)` 再补一次（那时牌一定就位），每场最多补 2 次。日志会打 `虚空之唤（…）：给 N 名玩家补挂恶咒 ⇒ 新挂 X 张、跳过 Y 张`。普通模式（她首次打出女妖之嚎才生效那条）行为不变。

### v0.2.863 

2026-11-21 (**恐惧模式：辉星储的「群星荟萃」改成只给保留手牌（不再给下回合能量）+ 同步 wiki 与支配章节**）—— 用户 2026-11-21：「恐惧模式下，辉星储不仅意图被调整，特殊效果也被调整了，改为：每当储君获得「保留手牌」时，所有玩家获得「保留手牌」。警告：黑洞属于无来源伤害，带有「为你而死」和「瓦皮的忠诚」的召唤物不会替你承受该伤害。即只给玩家保留手牌而不再给玩家下回合能量。意图修改保留，这也是恐惧增强的一部分。记得同步 github 上的 wiki，另外，在目录那里写上不同角色的支配（有的支配要分 1、2、3 层，有的不用）」。改动：①`Powers/BossStellarGatheringPower` 在 `DreadMode.IsActive` 时**只发 `RetainHandPower`**、跳过 `EnergyNextTurnPower`（普通模式一个字没动），日志写明「恐惧模式：不再给下回合能量」；②16 语言的 `powers.json` `BOSS_STELLAR_GATHERING_POWER.description_dread` 与 `monsters.json` `REGENT_BOSS.stellar_dread`（游戏内说明框与 wiki 都读这一个）都换成新文案，并去掉原来那句「他把第3回合的防御与第4回合的隐秘藏品+换了位置」（**代码里的换牌保留**，它仍是恐惧增强的一部分）；③wiki：`tools/wiki/make-markdown.ps1` 新增「各角色的支配（恐惧模式）」章节（7 个角色：铁甲战士 / 静默猎手 / 故障机器人 / 储君 / 亡灵契约师 / 愚者 / 观者，标注哪些按幕分 1/2/3 层）并在**目录**里加 `**支配**（恐惧模式）` 一行；重新跑 wiki-dump → make-markdown → build-site，`docs/wiki/boss-table.md` 与 `docs/wiki/index.html` 都已刷新（辉星储的「恐惧模式加强」现在是新文案）。

### v0.2.862 

2026-11-21 (**人工制品按「施加者」判定：怪物自带的负面不吃它，玩家遗物/牌/药水上的负面照旧被挡**）—— 用户 2026-11-21：「不对，现在人工制品给的又太晚了，没法挡住红面具等开局施加负面的遗物。能不能让它既不会抵消怪物本来就有的 debuff 也不会让它挡不住玩家开局施加 debuff 的遗物？」。上一版靠**拖时间**（等 StartCombat 后再发）只能二选一，必须换成**按来源判定**。反编译确认两种来源在 `PowerCmd.Apply` 的第 4 个参数上就不同：`BowlbugRock.AfterAddedToRoom` 的失衡 = `PowerCmd.Apply<ImbalancedPower>(ctx, self, 1m, self, null)`（施加者是**它自己**）；`RedMask` 的虚弱 = `PowerCmd.Apply<WeakPower>(ctx, HittableEnemies, value, redMask.Owner.Creature, null)`（施加者是**玩家**）。改动：①新增 `ArtifactIgnoresMonsterDebuffPatch`（前缀挂 `ArtifactPower.TryModifyPowerAmountReceived`）—— 目标是我们这一侧的敌人、且施加者**不是玩家**（怪物自己/怪物之间/无来源）⇒ 直接 `__result=false` 跳过「吞掉负面」的逻辑 ⇒ 这层负面真正生效、人工制品一层不掉；玩家施加的负面照原版被挡。②进房那条路**改回开局立刻发**（`await`，见 `DefectDominion.AfterRoomEntered`）⇒ 人工制品一定早于红面具这类「开局上负面」的遗物在场。③**删掉「回读不足就 SetAmount 补齐」**（那是把玩家的负面又退回去，等于让红面具白吃）。④保留「叠加」语义（v0.2.861：自带 X + 支配 Y）与 `人工制品抵消 / 人工制品放行` 两条取证日志。

### v0.2.861 

2026-11-21 (**故障机器人的支配改回「叠加」：自带 2 层的啃咬机应该变 4 层**）—— 用户 2026-11-21 截图：「自身就有 2 层人工的啃咬机不会额外获得人工（加上支配应该是 4 层）」。根因是我 v0.2.855 起把幂等闸门写成「**已有层数 ≥ 目标就跳过**」——啃咬机自带 2 层、本幕目标也是 2 ⇒ 直接判定「已够」⇒ 支配那 2 层永远发不到。现在幂等改成**按「我们这一场给它发过多少」记账**（新增 `_grantedArtifact`，`Dictionary<Creature,int>`）：`delta = 本幕目标 − 已由支配发过的层数`，>0 就 `PowerCmd.Apply` 追加 ⇒ **自带层数原样保留、支配是额外叠加**（啃咬机 2+2=4）；两条发放路径（进房 / 后加入）与后面的兜底回读反复碰到同一只也不会重复发。兜底回读的比对目标同步改成 `_expectedArtifactTotal`（= 发放那一刻的「自带 + 支配」总数），所以「自带 2 + 支配 2」的敌人回读时也不会被误判成需要校正。日志文案改成分开写「本幕 N 层 / 已发叠加 / 明细: 自带 X + 支配 Y」。

### v0.2.860 

2026-11-21 (**故障机器人的支配改成「等敌人吃完自己的 buff/debuff 再发人工制品」**）—— 用户 2026-11-21 定口径：「阻止人工制品挡掉怪物自带的 debuff（例如盛碗虫石的失衡）→ 或者应该这么说：**在它获得自己的所有 buff 与 debuff 后再给予它们人工制品**」。根因是**发放时机**：`DominionModel.AfterRoomEntered` 跑在 `CombatManager.StartCombat` 之前（日志实证：支配那行后面紧跟着 `UpdateTrack…战斗开始时`），敌人自己开战期的 debuff 在我们发完之后才落地 ⇒ 人工制品替它挡掉并 −1（`人工制品抵消：盛碗虫（石） 挡掉了「IMBALANCED_POWER」⇒ 剩 1 层`）。改法：①`GiveArtifactAsync` 新增 `deferUntilCombatStart` —— 先等 `CombatManager.IsInProgress`（最长 4s）再补 600ms，让开战期能力全部结算完才发人工制品；②`DefectDominion.AfterRoomEntered` 改成**不 await**（fire-and-forget，先把敌人名单拷下来）—— 否则在 StartCombat 之前 await 会把「战斗开始」卡在我们后面；③两轮兜底回读（900ms / 1500ms）**不再提前 break**，更晚落地的自带负面也能补回来；④保留 v0.2.858 的 `人工制品抵消` 取证日志。

### v0.2.859 

2026-11-21 (**结案：那只「1 层」是开战瞬间被上了负面、人工制品替它挡掉了（原版机制）+ 支配描述补「多人按人数增加」**）—— 用户 2026-11-21：「没有变化…但还是 1 层显示 1 层。2 层 2 层，3 层 3 层」。v0.2.858 的取证日志一次就抓到了真凶（16:03:12，盛碗虫那场）：`落地: 盛碗虫（石）=2, 盛碗虫（蜜）=2` 紧接 `人工制品抵消：盛碗虫（石） 挡掉了「IMBALANCED_POWER」⇒ 剩 1 层`。反编译 `ImbalancedPower`：`PowerType.Debuff` / `PowerStackType.Single` / 挂 `AfterDamageGiven` —— 是**原版**的负面能力，在开战那一刻施加到「石」这只身上，人工制品按原版规则替它挡掉并 `PowerCmd.Decrement`（−1）⇒ 屏幕上就是 1 层，而没吃到负面的「蜜」还是 2 层。所以**支配没少发，也不是本模组的 bug**（用户「盛碗虫初始不带人工制品」的判断也成立：那层是开战瞬间被上负面换来的）。本版只做用户要求的那件事：`故障机器人的支配` 描述后面加「（多人时会根据人数增加层数）」，16 种语言全改（`content/localization/<lang>/ascension.json` 的 `DREAD.DEFECT.description`），并保留 v0.2.858 那条「人工制品抵消」日志（上限 40 行）当以后的取证工具。

### v0.2.858 

2026-11-21 (**加「人工制品抵消」取证日志：查那只 1 层到底被谁吃掉的**）—— 用户 2026-11-21：「还和刚刚一样」（同一场盛碗虫仍是 1 层 / 2 层）。但本机 v0.2.857 日志（16:00:58 开战那一刻）已经把「发多少」这一侧钉死了：`落地: 盛碗虫（石）=2, 盛碗虫（蜜）=2 | 各自 power: 盛碗虫（石）[ARTIFACT_POWER=2] ; 盛碗虫（蜜）[ARTIFACT_POWER=2]` —— 两只身上都只有一条 `ARTIFACT_POWER=2`、没有任何别的图标。⇒ 屏幕上那个 1 只能是**开战之后**被吃掉的：反编译 `ArtifactPower.AfterModifyingPowerAmountReceived` 挡掉一个负面后会 `PowerCmd.Decrement`（−1 层）。所以这版加 `ArtifactConsumeTracePatch`（后缀挂同一个方法），凡是「人工制品挡掉一次负面」就打一行 `人工制品抵消：<谁> 挡掉了「<哪个负面>」⇒ 剩 N 层`（上限 40 行）：有这行 ⇒ 那只 1 是原版机制正常消耗掉的；一行都没有而图标仍是 1 ⇒ 那个图标根本不是人工制品，需要悬停看名字。

### v0.2.857 

2026-11-21 (**故障机器人的支配：保留原版的人数缩放，改成「等战斗真正开始后再回读并补到位」**）—— 用户 2026-11-21 两条订正：①「多人层数增加正是我想要的，保持按原版规则增加层数」②「盛碗虫初始不带人工制品，且我没有在回合开始时自动施加负面效果的遗物，所以还是有问题，继续修」。所以 v0.2.856 那个「一律校回本幕层数」的写法是错的（会把多人该多的那几层吃掉），现在改成：**期望值 = 本幕层数 + (人数 − 1)**（这正是 `ArtifactPower.GetScaledAmountForMultiplayer` 的 `amount + players - 1`，`PowerCmd.Apply` 自己会给，我们不额外加），只有当**落地值低于期望**时才补 —— 绝不减少。另外怀疑真因是**时机**：`DominionModel.AfterRoomEntered` 跑在 `StartCombat` **之前**（日志里支配那行紧跟着就是 `UpdateTrack…战斗开始时` 与 `UI 会话已重置`），战斗初始化会重排敌人身上的 power，先挂上去的那只可能被削成 1 ⇒ 现在 apply 之后**分两轮回读**（350ms、1000ms 各一次，战斗已真正开始），`落地 < 期望` 就 `SetAmount(期望)` 补齐，缺失就重新挂；日志保留 `落地:` 与 `各自 power:` 两段取证。

### v0.2.856 

2026-11-21 (**故障机器人的支配：落地后把层数校回本幕目标 + 打印每只的实际层数与全套 power**）—— 用户 2026-11-21：「不对，还是第一个 1 第二个 2」+「目前这个支配施加的人工制品会在多人时层数增加吗」。两件事：①**多人确实会增加**：反编译 `ArtifactPower.GetScaledAmountForMultiplayer` 就是 `return amount + combatState.Players.Count - 1;`，而 `PowerCmd.Apply` 只在 `players.Count > 1 && (IsPrimaryEnemy || IsSecondaryEnemy)` 时走缩放；我们那条「Boss 不按人数翻倍」的补丁（`BossNoMultiplayerPowerScalePatch`）**只管我们自己的 Boss**，普通敌人照旧缩放 ⇒ 2 人时目标 2 层会落地成 3 层、3 人成 4 层。②本机 v0.2.855 日志显示两只盛碗虫都是 `=0→2`（`BOWLBUGS_WEAK`，单人局，不进缩放分支），可屏幕上仍是 1 和 2 ⇒ 说明「我们发了 2」和「屏幕上那个 1」并不是同一件事（很可能那只身上本来就有另一层同图标的东西，或落地值被什么改过）。改动：`GiveArtifactAsync` 在 apply 之后**逐只回读落地层数**，`!= 目标` 就用 `PowerModel.SetAmount(目标)` 校回来；日志新增两段 —— `落地: 甲=2, 乙=1→2（落地校正）` 与 `各自 power: 甲[ARTIFACT_POWER=2,…] ; 乙[…]`，下次再看到 1/2 就能立刻分辨是「我们没发到」还是「那个 1 是别的 power」。

### v0.2.855 

2026-11-21 (**恐惧模式：故障机器人的支配改成「补到本幕目标层数」+ 逐只明细日志**）—— 用户 2026-11-21：「2 层时故障机器人的支配有问题，有的敌人 1 层人工制品有的敌人 2 层」。本机日志（v0.2.854）核实：进房那次发放**每次都打了「各获得 2 层」**（15:47:31 / 15:47:46 / 15:48:58 / 15:49:17 四条，act 2 ⇒ `DreadActScaling.ActNumber()` = 2 正常），所以问题不在「发多少」，而在**幂等判据**：旧代码是「身上**有**人工制品就整只跳过」（为了挡住「进房」与「后加入」两条路重复挂），于是一只**自己本来就带 1 层**（或上一批只发过 1 层）的敌人就永远停在 1 层，同场其它敌人是 2 层 ⇒ 视觉上「有的 1 层有的 2 层」。改动：`GiveArtifactAsync` 从「有就跳过」改成**补到目标**——先算本幕目标 `amount`，逐只读现有层数，`现有 < 目标` 就按差额逐只 `PowerCmd.Apply`（一只一只喂，不再一次性给一组），`现有 ≥ 目标` 才不动；日志补**逐只明细**（`甲=1→2, 乙=2（已够，不动）`），下次再有偏差能直接看出是哪只、原来几层。

### v0.2.854 

2026-11-21 (**回滚上一版误伤：她的卡面/意图被当成「滞留卡面」隐藏了**）—— 用户 2026-11-21：「你刚刚是不是修出问题了，怎么我一提起卡牌储君的意图就消失了」。本机日志（v0.2.852，15:41:19）铁证 5 行：`⚠ 卡面滞留：HIDDEN_CACHE / DEFEND_REGENT / COLLISION_COURSE / DEFEND_REGENT / DEFEND_REGENT 在 …/CombatRoom/CombatUi/PlayContainer/@Control@4493/@Control@4519/Card 已 3.0s ⇒ 隐藏它` —— 那 5 张**正好是她第 1 回合的整手牌**。根因：我们自己的手牌栏面板（含那一排意图）**本来就挂在 `%PlayContainer` 底下**（`UI/BossHudLayer.Resolve` 在战斗 UI 可见时直接返回 `%PlayContainer` 当父节点），而 v0.2.852 的 `SweepRoom` 递归整棵 `NCombatRoom` ⇒ 把她手里每张牌的卡面都当成滞留卡面隐藏掉（v0.2.853 的 `ClearOurCardsFromPlayContainer(minAge=0)` 更狠，直接 `QueueFree`）。现在两处都收窄回**原版契约**：①`ClearOurCardsFromPlayContainer` **只认 `PlayContainer` 的直接子节点**里的 `NCard`（原版 `AddToPlayContainer` 就是这么挂的），我们的面板在更深的层级 ⇒ 碰不到；②`SweepRoom` 改成**只看不动手**（只打一行带节点路径的日志），并且整段跳过 `PlayContainer` 子树。

### v0.2.853 

2026-11-21 (**「碰撞轨迹卡在天上」的真凶找到了：`NCombatRoom/Ui/PlayContainer`**）—— 拿到玩家日志（`mod_log (52).txt`，4 人联机、v0.2.850）后核对的结论：①他那局 `PLAY COLLISION_COURSE (cost 0) self=RegentBoss(side=Enemy)` 与 `done COLLISION_COURSE` 都正常，整份日志 **0 条**「已从队列里摘掉」、**0 条**「出牌动画」⇒ 既不是队列滞留也不是我们的飞牌动画（和上一版的推断一致）；②反编译 `NCombatUi` 找到了原版把**打出去的卡面**放哪儿：`public void AddToPlayContainer(NCard card) { PlayContainer.AddChildSafely(card); }` + `PlayContainerCards => PlayContainer.GetChildren().OfType<NCard>()`，牌在结算期间 `Pile.Type == Play`，**离开 play 堆那一步才把它从展示区收走**。而那一笔是替**本机玩家**的出牌准备的 —— 她是影子玩家、走 `CardCmd.AutoPlay` ⇒ 卡面永远留在战场中央那张大卡的位置。多人比单人更容易看到：多人时每台机器上她的每一手都是「**别人**打的牌」，展示区的接手/收尾全在本机玩家的出牌流程里。改动：`StrayBossCardQueuePatch` 新增 `ClearOurCardsFromPlayContainer(why, minAgeMs)` —— 把展示区里**我们 Boss 的**卡面 `RemoveChild` + `QueueFree`（失败退回隐藏）；调用点两处：①`CharBossMonster.PlayOne` 开头（她开始打**下一张**牌之前，顺手把上一张收掉，`minAgeMs=0` 立刻收）；②1Hz 兜底 `SweepPlayContainer`（只收停留 ≥1.5s 的，绝不打断正在播的动画）。

### v0.2.852 

2026-11-21 (**「碰撞轨迹卡在天上」：补一条房间级兜底扫描（带节点路径日志）**）—— 用户 2026-11-21 截图：「碎屑储打出的碰撞轨迹还是会卡在天上」（碎屑储打出的那张 14 点伤害 + 印碎屑的牌停在战场中央不下来）。日志先把两条现有机制排除掉：①`StrayBossCardQueuePatch.Reap` 从来只扫原版 `NCardPlayQueue`，而整份日志里**没有一条**「已从队列里摘掉」⇒ 那张卡根本不在出牌队列里；②`BossHandPlayFx.Fly` 需要装了斗蛐蛐才跑，日志里 `出牌动画` 一行都没有 ⇒ 也不是我们的飞牌动画没收尾。也就是说它是**原版**为「非本机玩家出牌」建的卡面、挂在 `NCombatRoom` 的某个容器里，谁也管不到。改动：新增 `SweepRoom()`（1Hz、只在我们的 Boss 战里跑）—— 递归扫 `NCombatRoom`（跳过任何名字带 `Hand` 的子树与 `NCardPlayQueue` 子树），只盯「认得出是我们的牌」或「Model 已被原版清掉」的卡面，滞留 ≥2.5 秒就**打一行带节点路径/缩放/坐标的日志**（那行路径就是原版把它挂在哪儿了）并隐藏它；隐藏而不是 QueueFree（容器可能还记着这个节点）。

### v0.2.851 

2026-11-21 (**额外回合（腾跃/龙涎香/倒吊人/佩尔之眼）不再让她凭空跳过一回合的剧本 + 计划为空时不再清空意图**）—— 用户复测：「怎么 boss 的意图还在变，而且是在我打出腾跃之后？难道是观者 mod 引起的问题？」。日志实证两件事：①`⚠ 意图在她第 1 回合**内**变化：[DefendIntent|] → []` 的两条**都是我们自己的 4Hz 帧回调**打的，而且就发生在她这一手最后一张牌刚打完那一刻（同一秒面板写了「计划 0 张」）⇒ 那是**显示侧**的假变化：`Plan` 一空就发了一份空意图把图标抹掉，下一帧再补回来，玩家看到的就是「意图先消失再出现」。②反编译 `CombatManager.StartTurn` 5316 行：原版对**额外回合**是 `if (!isExtraPlayerTurn)` 才给敌人 `PrepareForNextTurn` —— 而额外回合**照样**会把真人玩家的 `PlayerCombatState.TurnNumber` +1（同方法 5311 行拿它画回合横幅）。我们那道「真人回合号没变就跳过」的幂等闸门按的是回合号 ⇒ 只要有任何模组在额外回合里问一次「怪物下一步干什么」，闸门就放行 ⇒ `TurnNumber++` ⇒ 她凭空跳过一回合剧本 ⇒ 意图变了；等真·下一回合开始时**再变一次**。三处改动：①`PrepareTurn` 新增**额外回合闸门**（`Core.ExtraTurnWatch.IsExtraPlayerTurn` 且已算过计划 ⇒ 整段跳过，计划原样保留，打一行带调用栈的说明）；②`PrepareTurn` 每次真的重建时打一行 `准备回合重建：真人第 N 回合 → 她第 M 回合（额外回合=是/否）| 调用者:…`，被打断的回合里若出现多行就说明有别的东西在反复触发重排；③`PublishVanillaIntents` 在 `Plan` 暂时为空时**不再清空**已发布的意图（被击晕时仍然发空，那是真的不打），把「消失再出现」从显示侧也堵掉。

### v0.2.850 

2026-11-21 (**装「小小姐」时意图在回合内变化：钉住显示 + 抓现场**）—— 用户复测：「装载小姐姐时，boss 的意图又在回合中变化了（本机）」。v0.2.848 只挡住了「同一回合被重复要求规划」，但那一下**外部重掷可能已经把 `NextMove` 的意图换掉**，再加上我们 4Hz 的 `PublishVanillaIntents` 会把它重写成当前 `Plan` ⇒ 玩家在回合里会看到意图「跳一下」。两处改动：①`PrepareTurn` 的幂等闸门命中时不再是裸 `return`，而是**按当前 `Plan` 重刷一次意图**（`PublishVanillaIntents()`，幂等）⇒ 显示被钉回她这一回合真正会打的东西；②新增**意图回合内变化诊断**：`PublishVanillaIntents` 里给发布出去的意图做短指纹（类型 + Damage/Amount/Repeat/Hits），同一 `TurnNumber` 内指纹变了就打一行 `⚠ 意图在她第 N 回合**内**变化：[旧] → [新] … 调用栈:«三帧`，一眼能分辨是我们自己重算还是外部模组改的。

### v0.2.849 

2026-11-21 (**联机：阵容随机种子改由房主广播**）—— 玩家反馈（「小姐姐」那位之外的另一位）：「mod 联机的时候只有我看到的是猎手，别人看到都是灵魂骨」「进去以后不同步，sl 就只剩一个 boss 了」，并给了 `mod_log (51).txt`。日志实证两条：①`[22:00:13] 阵容随机种子 = 1（**固定值**：读不到本局种子，人数=4）` —— 有一台机器**读不到** `RunState.Rng.Seed` 就退到常量 1，而另一台读到了真种子 ⇒ 两端按**不同种子**排阵容 ⇒ 场上的 Boss 根本不是一个 ⇒ 进战斗立刻分叉；②`重新对齐：之前用的是本地随机，联机必须两端一致` 出现 3 次 ⇒ 中途换种子会**把已经排好的阵容换掉**。修法：房主把 `BossLineups.RunSeed` 随 `BossPicksMessage.Settings` 一起广播（`seed=<ulong>`），客人用新增的 `BossLineups.AdoptHostSeed()` 直接采信房主的种子并清阵容缓存 —— 这样即使本机读不到 `Rng.Seed`，两端也一定按同一条阵容排 Boss。协议变了 ⇒ 两端必须同一版本。

### v0.2.848 

2026-11-21 (**修：装「小小姐的战斗提示」后 Boss 意图整段错位（第 1 回合显示第 7 回合）**）—— 用户本机实测：「在装载『小姐姐』后，boss 的意图会变，比如第一回合的意图变成了第七回合的」。根因：`PrepareTurn`（`Monsters/CharBossMonster.Turn.cs`）那句 `TurnNumber++` 假定「一回合只进一次」，而它唯一入口 `BossThinkState.GetNextState` 会被「小小姐的战斗提示」这类**反复询问怪物下一次行动**的模组多调几次 ⇒ 回合号连跳（实测跳到 7）⇒ `ScriptForAct().TurnAt(TurnNumber)` 取到第 7 回合的剧本 ⇒ 意图/手牌/能量全按错回合走。修法：在 `PrepareTurn` 的击晕闸门之后新增**幂等闸门** —— 以**真人玩家的 `PlayerCombatState.TurnNumber`** 为准（与 `PlayerCardPlayedTracker` 同一口径），同一真人回合里再进来就整段跳过（`Plan` / 手牌 / `TurnNumber` 原样保留）并打一行日志说明「同一回合里被重复要求规划」；多 Boss 时每只各记各的（新字段 `PreparedRealTurn`，`AfterAddedToRoom` 清零）。

### v0.2.847 

2026-11-21 (**打击战图标：刀改用高分辨率卡图重提 + 加密**）—— 用户：「刀的方向和大小不要改，把他们修的棱角分明一些，然后再多搞几把刀（方向一致）」，并明确「以原本为基础重做，或者重新从地狱狂徒上提取那些刀，多提取一些」。做法：①新增 `tools/art/make-strike-blades-all.ps1` 与 `tools/art/extract_card_icon.gd` 取出游戏 PCK 里 **1000x760** 的「地狱狂徒」卡图（`refs/card_hellraiser_big.png`；旧素材只有 246x154，所以又少又碎）；②按「亮 + 低饱和 = 钢色」判据把刀 **全部**提出（保原位置原朝向）⇒ 10 把、`refs/strike_blades_all.png`，再闭运算补暗边缝让边缘更硬；③多出来的把数用**同一批刀旋转 ±14°** 复制（`-StrikeSweep 14`），不是自己画的形状；④整环缩放 470px 让刀更靠近头像（用户选定）。墨点 24.6%。

### v0.2.846 

2026-11-21 (**蛇咬猎图标改用「飞镖」卡图里那把飞刀**）—— 用户看图后两次纠错：①「这个飞镖怎么感觉被切了一半」—— 根因是我从 `dart_tip45.png`（一对**蛇头柄匕首**）按 `KeepRight 0.52` 裁，裁切线正好切掉刀尖；②「你这用的还是游戏里的素材吗」+ 直接贴了「飞镖」（Flechettes）卡图 ⇒ 用户说的「飞镖」就是**卡图里那把飞刀**，不是蛇头匕首。现在新增 `tools/art/make-flechette-knife.ps1`：从 `card_flechettes_big.png`（1000x760）按亮度取亮部后**只保留最大连通块**（丢掉后面几道速度残影）⇒ `refs/flechette_knife.png`（779x518，刀尖完整、轴向 35.6°）。拼法（用户 2026-11-21 定案）：猎人头像居中（250px）+ 两把飞刀**镜像**垫在头像下面（宽 400 ⇒ 长约 480px），拿头像实心轮廓裁掉中间 ⇒ 左上/右上露柄环、左下/右下露完整刀尖、中间被头像盖住。另：本轮实测确认「刀长 < 头像宽度 ⇒ 刀被完全盖住」（刀宽 60~125 那几档墨点完全相同），这条几何约束决定了尺寸取值的下限。`ironclad_anger` 沿用 v0.2.845 那版（火环套娃填缝）。

### v0.2.845 

2026-11-21 (**地图图标：愤怒战火环补齐 + 蛇咬猎专属图**）—— ①`ironclad_anger` 重做（用户：「把战士头像与火环之间的空缺按照火环的样子补全，让中间不要有空，补的要和外面的火花接得起来」）：把愤怒观那圈火焰**自身**按 1.00/0.88/0.77/0.67/0.58 逐级缩小叠加上去（每层带 7~12° 旋转）⇒ 补进去的是**真的火焰形状**，内侧火舌与外侧火焰天然相接；最后向内膨胀 26px 收掉最后一道发丝缝，**外轮廓一点没动**；主体周围 2px 禁入区保证火不从盔缝里透出来（墨点 38.6%）。②新增 `silent_snakebite` 地图图标（用户口径：猎人头像居中 + 下半左右两条蛇头镜像对称 + 上半两把蛇头柄匕首交叉成 X、尖分别朝左下/右下），资源 `res://CharacterBosses/images/map/silent_snakebite`，接线到 `SilentSnakebiteBossEncounter.BossNodePath`。③`defect_thunder` / `ironclad_strike` **未动**（用户：「回退到之前的版本」）。④新增工具 `tools/art/make-icon-sheet.ps1`（白+alpha 图标在深底上拼预览）、`make-outline.ps1`（方形膨胀−原图生成 `_outline`，与线上 `fool_tarot_outline` 逐字节同大小验证过）、`run-img2img.ps1`（走 ComfyUI HTTP API 上传/排队/轮询/下载，不写它目录）、`register-map-icon.ps1`（Godot 导入 + 自动往 pck_extras 追加 6 行）、`compose-batch2.ps1`（这批的结构拼接）。

### v0.2.844 

2026-11-21 (**支配按幕分档 + 6 只时的帧预算降档 + 事件机恐惧版挪到运行期**）—— ①用户：「猎人/机器人/储君的支配做出层数区分」⇒ 新增 `Core/DreadActScaling`（运行期读幕数，夹 1..3）：静默猎手 = 玩家 −1/−2/−3 力量 + 1/2/3 虚弱；故障机器人 = 所有敌人 1/2/3 层人工制品（逐只 apply 保留）；储君 = 回合开始把 1 张非状态/诅咒牌分别变成 眩晕(`Dazed`)/粘液(`Slimed`)/碎屑(`Debris`)。战士/观者/愚者三条**不动**。②「只显示当前层数的效果」：`DominionModel.Description` 不是 virtual ⇒ 用 Harmony 后缀 `DominionDescriptionActScalingPatch` 按当前幕填占位符，16 语言文案改成 `{Strength}`/`{Weak}`/`{Artifact}`/`{Transform}`（储君的牌名直接取卡牌模型的 `Title`，自动本地化；顺带删掉静默猎手那句已经作废的「精英战 2/2」）。③6 只 Boss 同场时帧预算再降一档（仅手机：指纹闸门 6ms→4ms、隔 1 帧→隔 2 帧；慢车道 4Hz→3.3Hz），1～5 人零变化。④`EventMachineScript` 第 4/9 回合的恐惧版（多一张「疯狂进食+」）从 `Build()` 挪到运行期 `Resolve` —— 那个 build 期判断恒为 false，以前从来没生效。 /  2026-11-21 (**支持 6 人联机（装了观者时）**）—— 选人逻辑未改，只把候选池扩到 6；观者只看本机（`WatcherModPresence`），没装就夹回 1～5；新增 6 只平行四边形站位（3 行 2 列、斜移 g/2，第 3 行第一个在第 1 行第二个正下方）。

### v0.2.842 

2026-11-20 (**修：恐惧模式里「巨石战」「炼药猎·群蛇形态」的增强从来没生效过** —— 用户 2026-11-20 截图报了这两条。**同一个根因**：`BossDefs` 是**静态初始化**，而剧本 `Build()` 就在里面跑 ⇒ 那一刻 `RunManager` 还没有 run、`DreadMode.IsActive` 恒为 false ⇒ 任何「在 Build() 里判恐惧模式」的写法都永远走不到恐惧分支（本项目 RegentStar / OstyKeeper / GrandFinale 的注释里早就记过这个坑，这两个剧本漏了）。①**巨石战**：恐惧增强按用户口径是「**他的执迷减少 1 费**」（不是偷偷多给 1 点能量）⇒ 改成运行期 `Resolve`：恐惧模式下第 1 / 7 回合把执迷换成 `CostThisTurn = 1` 的版本（卡面真的显示 1 费），能量保持 4 / 3 ⇒ 第 1 回合多打 1 张巨石+、第 7 回合两张都打得出去。②**炼药猎的群蛇形态**、**电球机的暴风雨**：升级改走运行期通道 `BossVariant.DreadUpgrades`（`CharBossMonster.AppendDreadUpgrades`）。⚠ 同类隐患还剩一处没改：`EventMachineScript` 的第 4 / 9 回合仍是 build 期判恐惧（会多一张疯狂进食+），下一轮一并挪到 `Resolve`。

### v0.2.841 

2026-11-20 (**修：玩家自己的闪电球会电到自己人** —— 玩家反馈「我用机宝对战打击战的时候电球怎么电自己了」。反编译定位：球的被动/激发**没有外部目标**，它自己拿 `CombatState.GetOpponentsOf(Owner.Creature).Where(IsHittable)` 随机取一个（`LightningOrb.ApplyLightningDamage`），而被动那条是 `TriggerPassive(ctx, null)` ⇒ **选靶完全取决于这份「对手列表」**。我们为角色 Boss 做的「可命中敌人 = 玩家侧」翻侧、「只打锁定玩家」收窄都是按全场语义写的，某些时序下会漏到这条路径上 ⇒ 玩家侧的自己人被当成「对手」。两道兜底：①`ShadowPetNotAnOpponentPatch` 新增 `DropSameSideRealPlayers`：**真人玩家的对手列表里一律摘掉同侧真人玩家**（宠物/我们的影子玩家不动，判据 `Monster == null && Side 相同`）；②新增 `Patches/PlayerOrbSelfHitGuardPatch`：球主人是真人玩家、而传进来的显式目标是同侧生物时，把 target 抹成 null 交回原版重选。两处都只打一行日志说明，不动她（角色 Boss）自己的球。

### v0.2.840 

2026-11-20 (**修：轰击储的「轰击」复燃预览被所有人看到（像群伤）** —— 玩家反馈「轰击从消耗堆打出时的伤害预览似乎错误的显示为群伤」。反编译 `Bombardment`：`base(3, Attack, Rare, TargetType.AnyEnemy)`、`OnPlay` 是 `DamageCmd.Attack(...).Targeting(cardPlay.Target)` ⇒ **单体**。而我们在左侧那一列画「复燃」时（`BossHandPanel.Extra` 的 `LiveSingleAttackIntent`）**没判锁定关系** ⇒ 多人里每个玩家的面板都显示这个 18 点，看起来就是群攻（和群蛇形态 / 充能球被动踩过的是同一个坑：`LOCK_ON_POWER` 只锁一个人）。现在这一条按 `BossIntentBuilder.RandomAimIsElsewhere(boss)` 收窄：本地不是被她锁定的那位就不画，并打一行限流日志。

### v0.2.839 

2026-11-20 (**给启动崩溃加「崩溃面包屑」：同步落盘，进程被强杀也留得住** —— 用户提醒「我怀疑是游戏在日志记录前就崩溃了」，这个怀疑很可能是对的：那个「致命错误」弹窗来自游戏自己的 `NGame`（`catch { GameStartupError(); throw; }`），而如果进程在把缓冲日志刷下去之前就没了，`godot.log` / `mod_log.txt` 里就一个字都看不到；我们自己的日志又是后台线程缓冲 200ms 的，崩得太快同样丢尾巴。新增 `Core/CrashTrace`：**每写一行就 `FileOptions.WriteThrough` + `Flush(true)` 真正落盘**（不用任何缓冲、随后关句柄），文件固定在 `%APPDATA%\\SlayTheSpire2\\logs\\characterbosses_trace.txt`（玩家只发这一个文件就行）。埋点：①**外壳**（`loader/BootTrace.cs`）—— Initialize 进入 / 版本探测 / 已载入实现 / 归属登记 / 调 `ModEntry.Initialize` 前后 / 失败；②`BuildInfo.LogOnce` 的 `启动阶段 1/5…5/5`、BaseLib 配置注册前后；③`StartupWatchdog` 抓到的启动期异常也同步写一份；④新增 `Patches/GameStartupTracePatch`：`NGame._Ready` 进出、`GameStartup` 开始、**`GameStartupError` 进入**（= 游戏马上要弹致命错误）⇒ trace 的最后一行就能告诉我们「是不是启动流程里炸的、炸在哪一步」，哪怕游戏一个字节的日志都没写出来。

### v0.2.838 

2026-11-20 (**修：恐惧模式「铁甲战士的支配」只给第一只敌人挂上恶魔形态** —— 用户 4 人恐惧模式截图：三只敌人只有第一只吃到。两处改动：①以前把整个敌人列表一次交给 `PowerCmd.Apply<IEnumerable<Creature>>`（群体重载），而那个重载对「刚进场、血量/阵营还没初始化」的生物会**静默不挂**；现在改成**逐只 apply**（`ApplyOneAsync`），每只各自 try/catch 并单独计数，日志会写 `N 个敌人各获得 1 层恶魔形态（M 个失败）`。②新增 `Patches/IroncladDominionLateEnemyPatch`：挂在 `CombatManager.AfterCreatureAdded(Creature)` 后缀上，后加入战斗的敌人 300ms 后再补挂一次（幂等；本幕支配不是 `IroncladDominion` 就直接返回）—— 因为原版在那一刻判定「还不是活着的敌人」之后**不会**再派发一次钩子，晚进场的那两只会永远拿不到。

### v0.2.837 

2026-11-20 (**修：碎屑储的「碰撞轨迹」打出后卡面吊在屏幕中间** —— 玩家反馈。根因两条都落在「收卡面」这件事上：①`StrayBossCardQueuePatch.HoldReap` 以前是**裸 bool**，只要有一条路径「置上之后没走到置回」（`ScryGate.Release` 在玩家中途退局/异常/预见界面没关时没跑到就是），**整场战斗直到结束都不再回收卡面** ⇒ 之后任何一张她的牌（尤其 0 费、还会 `Cmd.Wait(0.5s)` 的「碰撞轨迹」）就永久吊在屏幕中上方；现在改成**带 2 分钟自动过期**的开关（正常慢速选牌仍不会被提前打断，真卡死也不会锁死整场）。②「这张卡面是不是她的牌」以前只看 `card.Model.Owner.Creature.Monster`，而出牌结束后 `Model` 可能被清掉 ⇒ 该卡面**再也不会被认出来**、永久滞留；现在加了一张「已确认是我方」的登记表（认出来一次就一直记着，`Model` 没了照样回收），并把队列扫描改成**递归**（卡面可能被塞进子容器里，只扫直接子节点会漏）；战斗结束时清空这两份状态。

### v0.2.836 

2026-11-20 (**手机掉帧：定位到「显示」与「指纹」两段，细分到具体调用**）—— 0.2.835 的分段计时在手机上跑出来了（41 行 `慢帧拆解`）：**「显示」段 12~31ms（最大 84.1ms）**、**「指纹」段 10~34ms**，其余段都在 1~2ms 内；「慢车道」只偶尔 17.9ms。也就是说每 2 秒窗口里的 2~8 次 20~40ms 尖峰来自 ①手牌栏/奥斯提/随从/血条这套每帧显示纠正与 ②意图/能力/面板指纹重刷。本轮把这两段再切细：显示 → 手牌栏 RunFrame / 炸弹 / 滚石 / 剑 / 随从 / 奥斯提视觉 / 奥斯提站位 / 血条 / 场景剑扫；指纹 → 焦点 / 抽牌 / 换手 / 能力 / 面板，下次日志直接指到单个调用。

### v0.2.835 

2026-11-20 (**手机掉帧：改分段计时定位真凶（日志落盘是冤枉的）**）—— 用户问「手机近几场 boss 战修好了吗」，实测答案是否定的：把日志落盘搬到后台线程（0.2.830）之后，手机 in-boss 战的 `帧回调耗时 max` 中位数仍是 **31.2ms**（0.2.829 主线程版是 29.0ms），≥20ms 的比例 189/189 = 100%，另有 12 条 ≥50ms（最大 127.9ms）⇒ **之前的 flush 假设被证伪**，尖峰在别处。现在给 `TickInner` 加 8 段计时（前置 / 面板 / 逐帧 / 指纹 / 巴什 / 显示 / 慢车道 / 收尾），只在**单帧 ≥10ms** 时打一行 `慢帧拆解 32.1ms = 慢车道28.9 …`（最多 40 行，正常帧零开销）⇒ 下次日志能直接指出是哪一段。

### v0.2.834 

2026-11-20 (**启动期异常快照器：把游戏自己吞掉的启动异常留进日志 + 启动阶段标记**）—— 玩家那个「致命错误！在游戏启动期间遇到了问题」弹窗来自游戏自己的 `NGame.GameStartupWrapper`：它 `catch (Exception) { GameStartupError(); throw; }`，也就是**异常先被游戏 catch 过一遍**再弹窗 ⇒ `AppDomain.UnhandledException` 不触发，而之前拿到的 `godot (32).log` 其实是**我们模组被禁用**的那次（`saved launch plan: 5 enabled, 60 disabled`，Mod List 里没有 CharacterBosses），所以里面一个字都没有。现在新增 `Core/StartupWatchdog`：装 `AppDomain.FirstChanceException`（异常一抛出就回调，不管后面谁 catch），启动后 150 秒内、最多 40 条，只记「栈里出现 CharacterBosses」或「可疑类型（NRE / TypeLoad / FileLoad / HarmonyException…）」的异常，每条带类型+消息+前 10 帧；同时 `LogOnce` 里加了 `启动阶段 1/5…5/5` 标记 ⇒ 下次日志能直接看出死在「我们初始化的哪一步」还是「我们之外」。

### v0.2.833 

2026-11-20 (**小小姐护栏：加诊断，先看清它在对谁下手**）—— 玩家 `mod_log (48).txt` 实证：护栏这次真的装上了（日志 `已加护栏（Picker.Before(选牌) + Pilot.Act(托管出牌)）`），但**一次都没拦到**（没有 `已拦下` 行），而 `godot (31).log` 里的 C# 栈明确是 `XiaoSpireHint.Entry.OnFrame → Frame → Pilot.Act_Patch1 → Pilot.Play → CardModel.TryManualPlay` ⇒ 说明它的参数里认不出我们的 Boss（`me` 不是影子玩家 / 候选集合不在参数里）。本轮不改行为，只加诊断：前 12 次调用各打一行 `小小姐诊断：<入口> 参数| [i]=类型(细节)`，其中 `Player` 会带 `net/影子=是|否/怪物=<Monster 类型>/手牌=N`，`int` 带数值 ⇒ 下次日志能直接判定它到底在替谁出牌、打的是哪张牌。

### v0.2.832 

2026-11-20 (**恐惧模式的「状态机」去掉「首次抽到状态牌」那半段：文案 + 机制一起**）—— 用户 2026-11-20 截图：恐惧模式加强那一块的正文与普通模式一模一样，而恐惧版不该有「你每回合首次抽到状态牌时，将其消耗、抽 1 张牌并获得 1 能量」这句。按用户选择「连机制一起」，改动两处：①文案（16 语言两个通道）：`DEFECT_BOSS.status_dread`（游戏内特殊效果框）与 `BOSS_SYNC_POWER.description_dread`（能力说明）都删掉那句，只留「她生成状态牌 ⇒ 复制品进你弃牌堆」+ 烟囱/开信刀的无来源伤害警告；普通模式两处照旧不动。②机制：`FirstStatusDrawPatch`（`Hook.AfterCardDrawn`）在 `DreadMode.IsActive` 时整段不触发（保留 `BossSyncPower` 的复制品那一半），所以恐惧模式下不会再消耗状态牌/抽牌/加能量。

### v0.2.831 

2026-11-20 (**小小姐的护栏真正装上了：按返回类型分两个前缀 + 连「托管出牌」一起拦** —— 玩家 `mod_log (47).txt` 给出确切原因：`护栏安装失败（忽略）: Cannot get result from void method static System.Void XiaoSpireHint.Picker::Before(System.Reflection.MethodBase __originalMethod, System.Object[] __args)` ⇒ 反编译确认它的两个入口返回类型不一样（`Picker.Before` 是 **void**、`Pilot.Act` 是 **string**），而我们用了一个写死 `ref object __result` 的前缀同时服务两者 ⇒ 给 void 那个打补丁时 Harmony 直接抛，整个 `TryApply` 被 catch 掉、**一条都没装上**。现在：void 的用 `PrefixVoid`、string 的用 `PrefixString`（`__result = null` = 它眼里的「成功」，不会触发它「连续失败 2 次自动关托管」），并且**前 5 次拦截会打一行日志**（含入口名与触发对象）⇒ 下次日志能直接看到「已拦下」而不是靠猜。

### v0.2.830 

2026-11-20 (**手机掉帧的真正根因：日志落盘在主线程** —— 手机实测每 120 帧一行 `帧回调耗时: avg=0.44ms max=20~41ms`，而 `avg×120≈53ms` 正好等于「那 2 秒里两次 flush 的耗时」⇒ 那些 20~41ms 的尖峰不是战斗逻辑，是**主线程往 flash 写日志**。现在主线程只把一行塞进内存队列（微秒级、零 IO），落盘全部交给后台写手线程（200ms 一趟；`⚠/失败/异常/不同步` 这类关键行立刻叫醒它，崩溃前最多晚 200ms，写失败的行会放回队列下次重试，队列有上限不会吃爆内存）。

### v0.2.829 

2026-11-20 (**修模组名/介绍乱码 + 遥测按条重试**）—— ①玩家截图：游戏里模组名显示成「鐜╁瑙掕壊Boss」、介绍整段乱码。根因**不是 BOM**，是 `CharacterBosses.json` 被**二次转码**（UTF-8 字节被当成 GBK 读了一次又写回）。现在按 GBK→UTF-8 反解回原文（`玩家角色Boss (Characters Bosses)`、作者 `飞猪`），两处反解时已经丢失的字符手工补回，并扫全仓库同类文件（另修掉 `BossDefs.cs` 一处被同样转码弄坏的注释）。②遥测：失败的记录写回队列、成功的移出，启动时补发一次。**遥测上传改成按条重试，不再丢记录**）—— 用户：「加上下次重试」。原来失败虽然「不清空队列」，但整份文件一起删：只要有一类（对局/诊断）发成功而另一类失败，或者同批里混着成败，文件就被删掉 ⇒ 失败的记录永久丢失（作者本机 2026-11-20 的 TooLargeCell 就是这么丢的）；重试时还会把已入库的再发一遍造成重复行。现在：**成功的那些移出队列、失败的写回队列**（上限 400 条、保留最新），并且**启动时补发一次**（`Telemetry.RetryPendingUploads`，在 `BuildInfo.LogOnce` 里调），日志写明「N 条留在本机队列，下次开打/启动时自动重试」。

### v0.2.827 

2026-11-20 (**修数据收集丢记录：飞书单元格超长被整批拒收**）—— 玩家问「记录收集的对局数怎么不涨了」。查本机 telemetry：今天就有 3 条 `飞书多维表格写入失败：code=1254130 TooLargeCell` —— 采集本身没坏（本地 jsonl 一直在写），是**上传**被飞书按「单元格超长」整批拒收。根因：有几列天然很长（`line` 日志行、`hpList`/`bossList` 每只 Boss 一段、`pList` 每个玩家一段），累计到一定长度就过线。修法：发之前按列限长（普通列 1000 字、日志/清单类 8000 字，截断处标「…(截断)」）；若仍被拒且是 TooLargeCell，则把每列压到 400 字**重发一次**把记录救回来，并在日志里写明。

### v0.2.826 

2026-11-20 (**与「小小姐的战斗提示」互操作护栏：不让它的自动选牌 AI 接管角色 Boss 的出牌**）—— 玩家反馈装了这个模组后它的 AI 会接管角色 Boss 出牌。反编译 `XiaoSpireHint.dll` 找到入口：它用运行期反射给 `CardSelectCmd` 的**全部方法**挂了自动应答前缀（`XiaoSpireHint.Picker.Before`），而我们的角色 Boss 是用影子玩家真的出牌的 ⇒ 她一打需要选牌的牌（抉择储的「抉择，抉择」、各种 FromHand/FromDiscard），在它眼里就是一个玩家要选牌，于是 AI 替她选了。新增 `Patches/XiaoHintBossShield.cs`：**不碰它的实现**，只在它那个前缀外面套一层——只要这次选牌涉及我们的角色 Boss（影子玩家 / `CharBossMonster` 生物 / 她持有的牌 / 候选牌集合），就 return false 让它的自动应答跳过，选牌交回我们的 `BossCardSelector` 或原版界面；没装那个模组时整段是 no-op，正常玩家流程零影响。启动时在 `BuildInfo.LogOnce` 里装一次。★ 玩家 2026-11-20 补充：出问题的是 2 层杂耍战（没有选牌牌）⇒ 说明还有第二条通道：它还有个「托管」(Pilot) 模式，`XiaoSpireHint.Pilot.Act(Answer, Player me, CombatState, string)` 直接从 `me.PlayerCombatState.Hand.Cards` 挑牌打出，而 `me` 是按「谁是本地玩家」取的 —— 我们的角色 Boss 用**影子玩家**出牌、影子正是照着本地玩家造的 ⇒ 它把 Boss 的影子当成玩家，替她打完整个回合。所以护栏现在同时挂 Picker.Before（选牌）与 Pilot.Act（托管出牌）两处，日志会列出实际挂上的入口。

### v0.2.824 

2026-11-19 (**恐惧模式两条支配改口径**）—— 用户 2026-11-19 定稿：①**储君的支配**改成「在你的回合开始时，选择 1 张非状态或诅咒牌，将其变化为「碎屑」」，并且给玩家挂一层「储君的支配」debuff（`Powers.RegentDominionPower`）；旧的「给所有敌人挂王之凝视（敌人攻击你 ⇒ 你本回合 −1 力量）」整套删除。结算放在 `AfterPlayerTurnStartEarly` ⇒ 早于烘焙手套 / 暴政 / 必备工具这些同样在回合开始结算的效果；额外回合同样触发（这本身就是影响玩家的效果）；Early 那一帧手牌没有可选牌就记待办、到普通 `AfterPlayerTurnStart` 再补一次，一回合只做一次。选牌走原版 `CardSelectCmd.FromHand` + `TransformSelectionPrompt`（过滤掉状态牌与诅咒），变化走 `CardCmd.Transform(原牌, 新建的碎屑, HorizontalLayout)`。②**故障机器人的支配**改成「所有敌人获得 1 层人工制品」，不再给玩家上「故障机器人的支配」debuff；与铁甲战士的支配同一套时机（进房给 `cr.Enemies`、之后加入战斗的敌人走 `AfterEnemiesEnteredCombat` 补发，两条路幂等，角色 Boss 跳过）。③16 语言文案同步：`DREAD.REGENT` / `DREAD.DEFECT` 两条支配说明 + 新增 `REGENT_DOMINION_POWER.title/description`。

### v0.2.823 

2026-11-19 (**拦截的意图数字跟着改 + 结算按玩家口径重写**）—— 玩家 2026-11-19 定稿：单体 1 倍（相当于 Boss 暂时换了锁定目标）、群攻 2 倍、易伤之类一律按**拦截者自己**的状态算、被保护者所有伤害预览归 0。① **实机结算**改成只挂在原版 `CreatureCmd.Damage` 的「列表版」重载上（反编译确认其余 11 个重载全都转调它）：被保护者从目标列表里**摘掉**（一点不吃）、拦截者本来就在列表里且有护人 ⇒ 按 (1+护着的人数) **份**重复加入（群攻 2 倍）、拦截者不在列表里（Boss 单体打被保护者）⇒ 把他加进列表一份（1 倍转过来）；易伤/虚弱/格挡全部由原版管道按拦截者自己的状态现算。同时把原版 `InterceptPower` 的 ×(覆盖人数+1) 对我们 Boss 的伤害**关掉**（否则每份再乘一次群攻会变 4 倍）。② **意图预览**新增 `Core/InterceptPreview.cs`：拦截者看到「按他自己状态算出来的那一笔」，群攻 ×(1+护着的人数)；被保护者一律 0（收尾 `ZeroAttacks` 只把攻击意图换成 0 伤害的同类节点，强化/策略一条不动、条数不变 ⇒ 兼容薄荷尖塔）。③ `BossIntentBuilder` 里 8 处 `FinalDamage` 预览改走 `ViewDamage`，`AimsElsewhere` / `AimedAwayFromLocal` / `RandomAimIsElsewhere` 全部改按「**EffectiveTarget**（这一笔实际会落在谁身上）」判定。

### v0.2.822 

2026-11-19 (**联机无色牌「拦截」现在真的能拦住角色 Boss**（玩家 2026-11-19：多人拦截拦不住 Boss 的攻击））—— 反编译原版：这套机制只认「带力量加成的攻击」，`CoveredPower` 与 `InterceptPower` 的 `ModifyDamageMultiplicative` 都是 `props.IsPoweredAttack()` 才动手（= 含 Move 且不含 Unpowered），而我们的 Boss 有 65 处伤害是「自己算好数值按 Unpowered 打出去」的（特殊效果 / 剧本无来源伤害），无来源那张的 dealer 还可能是 null ⇒ 原版两个钩子一律 `return 1` ⇒ 拦截完全无效。新增 `Patches/InterceptBossDamagePatch.cs`：给这两个 power 各加一个后置，只在「伤害是我们 Boss 这一侧打出去的」时候动手（判据复用 `BossKillerTracker.BossOf`：她本人 / 她的随从 / 她的影子玩家出的牌；dealer 为空时退到卡牌归属，再退到「场上有我们的 Boss」）。① 被保护者身上那一笔**直接归零** ⇒ 本回合不会再吃到任何 Boss 伤害；② 拦截者身上原版没放大过的那一笔按 **(覆盖人数 + 1)** 放大 ⇒ 覆盖 1 人就是双倍，和原版对群体伤害的处理完全一致（原版本就乘 N+1，不重复乘）。玩家/队友/原版怪物造成的伤害一律不碰。

### v0.2.821 

2026-11-19 (**修四人联机「跟踪锁定」挂错人导致的多数据不同步掉线**：玩家给的 RitsuLib 分歧诊断包（checksum #733）里，两台机器的 HP / 格挡 / 牌堆 / 遗物 / RNG / 意图**全部一致**，唯二差异是 POWER.LOCK_ON_POWER 一份挂在 1 号位、另一台挂在 2 号位。根因：锁定图标对账 ReconcilePowers 拿本地 Locks 当判定源，而 Locks 会被 NoteLocked（挂载动作）写进去 ⇒ 只要某次挂载发生在「人数 / Boss 花名册还没对齐」的瞬间，那个错误答案就被永久自证清白、对账再也纠正不回来。现在多人局 IsLocked 与 wanted 集合一律走纯函数 PureVictim（只依赖 NetId 顺序 + 怪物 id 顺序 + 本局种子）⇒ 各端必然算出同一个答案，错挂的那份会被 4Hz 对账摘掉、该挂的那位补上；NoteLocked 在多人局不再改判定（只打一行「与纯函数不一致」的日志）；纯函数暂时给不出答案时整帧跳过对账，不再来回摘挂浪费挂载次数。此外 **愚者的地图图标**：塔罗愚一直借用原版 TheKinBoss 的占位图（地图上显示成那只狼），现在用 character_icon_fool.png（尖帽 + 两个尖角绒球 + 遮面）做专属 512 纯白剪影 fool_tarot（两只眼睛挖成洞）+ 专属描边，已进 PCK)

### v0.2.820 

2026-11-19 (**修鬼祟（SneakyPower）归属判定：Boss 自己的攻击也给她加格挡**（玩家 2026-11-19 反馈「猎宝的鬼祟会把对面识别成队友」「道中根本不触发」「打BOSS战对面攻击会触发」））—— 反编译原版判据是 `cardPlay.Card.Owner.Creature != Owner` 就算「别人打的攻击」。我们的 Boss 用**影子玩家**出牌，牌的 Owner 是那个影子 Player、其 Creature 指向 Boss 本人，只要这个等式在某个时机不成立，原版就把**她自己的攻击**当成别人的 ⇒ 自己攻击也叠格挡。新增 `Patches/SneakyPowerOwnCardPatch.cs`：前缀里按「是不是同一个 CharBossMonster」判（不比 Creature 引用），是本人打出的攻击牌就直接拦住原版那句，其他情况一律放行。另一半「道中不触发」属原版行为（普通怪物攻击不走 AfterCardPlayed，只有我们 Boss 真的用 CardCmd 打牌），未在本版处理。)

### v0.2.819 

2026-11-19 (**联机修复第二版：登记归属与手动模型注册二选一**）—— 0.2.817 只加了 `ModManager.AssociateAssemblyWithMod（「CharacterBosses」, impl）`，结果游戏开始自己扫这个程序集、与我们外壳的手动 `ModelDb.Inject + InitIds` 撞车 ⇒ `DuplicateModelException: RELIC.NEOW_BLESSING` 启动崩溃（0.2.818 回退）。本版改成**互斥**：登记成功 ⇒ **跳过**外壳的手动注册（日志 `归属登记已生效 ⇒ 跳过外壳的手动模型注册（交给游戏自己扫）`）；没有该 API 的旧版 ⇒ 仍走手动注册。动机：玩家 `godot.log` 显示大厅已经能进（`Attempting to enter lobby …` → 收到 `InitialGameInfoMessage`），但随后 `ClientLoadJoinResponseMessage.Deserialize` 抛异常 —— 两边模型 ID 表不一致，根因就是那 368 条 `not associated with any mod`。)

### v0.2.818 

2026-11-19 (**回退 v0.2.817 的程序集归属登记：它让启动直接撞 DuplicateModelException**）—— v0.2.817 在 loader 里调用 `ModManager.AssociateAssemblyWithMod(\"CharacterBosses\", impl)` 修联机，结果游戏认定该程序集属于本模组后**自己又注册了一遍我们的模型**，与外壳原本的手动注册撞车：`DuplicateModelException: ModelDb already contains ID RELIC.NEOW_BLESSING mapped to type CharacterBosses.Relics.NeowBlessing` ⇒ `Encountered error on game startup`。本版把那次调用注释掉，恢复到 0.2.816 的可用状态。正确的联机修法需要「登记归属 + 放弃外壳的手动模型注册」一起改，下次单独做。)

### v0.2.817 

2026-11-19 (**修联机进不去房间：实现程序集没有登记到模组名下**（玩家 2026-11-19 报「Lobby join failed: k_EChatRoomEnterResponseError」+ `godot (28).log` 实证））—— 日志里每个模型都被判无主：`Attempting to register type CharacterBosses.Powers.X in assembly CharacterBosses.Impl113 … but it is not associated with any mod`，而这段跑在 `Multiplayer.Serialization.ContentSorter.Sort` → `ModelIdSerializationCache.Init` → `Transport.Steam.SteamCallResult.OnCallResult`，**正好在大厅回调里** ⇒ 建房/加入直接失败（或卡在好友列表「正在加载…」）。根因：双版本外壳用自己的 ALC 载入实现程序集，却没告诉游戏「它属于哪个模组」（游戏只在载入 mod 目录里那个同名 dll 时自动登记）。修：`Loader` 载入实现后调官方的 `ModManager.AssociateAssemblyWithMod(\"CharacterBosses\", asm)`（反射调用，107 没有该 API 时自动跳过）。单机不受影响。)

### v0.2.816 

2026-11-19 (**修联机不同步：阵容人数不再跟着大厅列表抖**（玩家 2026-11-19 反馈「有玩家联机不同步了」，`mod_log (44)` 最后一局 v0.2.804 实证））—— 证据：同一局里 `RunState.Players` 一直是 2 人，但 `RunLobby.Players` 五次来回跳 —— `[16:35:18] 大厅里只剩 1 人 ⇒ 按 1 只 Boss` / `[16:35:31] 变成 2 人` / `[16:37:40] 变成 1` / `[16:38:05] 变成 2` / `[16:39:59] 变成 1`；每次跳都触发「房主立即重播（含 pc=1/pc=2）」，而 `场上我方 Boss=2 只` 紧跟着就报不同步。现在 `LivePlayerCount` 拆成「原始判定 `LivePlayerCountRaw` + 两道闸门 `GateLive`」：①**战斗中一律锁定人数**（这一场阵容已造好，中途改只会两端分叉；真要有人退了，下一场再按新人数排）；②**去抖**：新数值要连续稳定 ≥5 秒才认（读到大厅列表的瞬间不完整不当真）。`ResetForNewRun` 一并清掉稳定值。日志新增 `阵容人数：N 人已连续稳定 5 秒 ⇒ 确认为 N 人`。)

### v0.2.815 

2026-11-19 (**修：恐惧模式铁甲战士的支配 —— 额外回合不再叠恶魔形态力量**（用户 2026-11-19：「玩家的额外回合开始时（如佩尔之眼、倒吊人、龙涎香、腾跃等提供的），所有怪物的恶魔形态也会触发，但实际上怪物并没有真的过去一个回合」））—— 把恶魔形态的结算时机从「怪物回合开始」挪到「玩家回合开始」之后，**额外回合也有一次 `AfterSideTurnStart(Player)`** ⇒ 怪物一回合成两三次地叠力量。现在换成游戏自己的判据 `CombatManager.Instance.PlayersTakingExtraTurn`（反编译 `SwitchFromPlayerToEnemySide`：玩家回合结束时 `Clear()` 后逐个问 `Hook.ShouldTakeExtraTurn` 填进去，下一次玩家回合结束才清 ⇒ 额外回合进行中一定非空、普通回合开始时一定为空）：非空即「当前是额外回合」⇒ `participants` 换成空列表，那一侧什么都不做，等下一个**真·玩家回合**开始再加力量。判据覆盖所有来源（佩尔之眼走 `ShouldTakeExtraTurnEarly`、龙涎香/腾跃/倒吊人走 `ExtraTurnPower`），且 0.111 / 0.113 两份 sts2 都有该属性。**不改怪物意图**，也不需要额外伤害补偿。)

### v0.2.814 

2026-11-19 (**削弱：恐惧模式下不再升级黑球机的「眼部攻击」**（用户 2026-11-19））—— ①`BossDefs` 里 `DEFECT_BOSS` 的 `DreadUpgradesByAct[0] = { GoForTheEyes }` 置空（第二幕能力机的「人工合成 3→5」不受影响；牌组里那 2 张「眼部攻击」与剧本里那几张都按未升级出场）；②特殊效果文案里那句「它的眼部攻击已升级（效果更强）」从 **16 语言 × 两张表**（`monsters.json` 的 `DEFECT_BOSS.dark_dread` 与 `powers.json` 的 `BOSS_COOLANT_POWER.description_dread` —— 面板实际读后者）全部删掉，共 32 处。本地化检查 16/16 通过。注：**仅装机，未打包未上传**。)

### v0.2.813 

2026-11-19 (**把剩下的 Raw 伤害行都补上「无实体」上限** —— 上一版只接了群蛇/烟囱/开信刀/灰烬，这一版把 点穴(pressureDamage)/黑洞(blackHole)/速行者(perDraw) 三条也接上 `BossDamagePredictor.ExtraRowDamage`；灾厄那两条是层数不是伤害，保持原样)

### v0.2.812 

2026-11-19 (**附加伤害行不吃「无实体」上限 → 预览偏高**（用户 2026-11-19：「有的伤害的伤害预览不会因玩家的无实体把伤害降低至 1（如群蛇形态）」）＋ 碎屑储恐惧版 14 语言补译文）—— ①新增 `BossDamagePredictor.ExtraRowDamage(card, amount)`：跑一遍原版伤害管道的 **Cap 阶段**（目标取「被这只 Boss 锁定的玩家」，拿不到退回本地玩家），并在 `BossIntentBuilder` 的四个「附加伤害行」处接上 —— **群蛇形态**（`serpent`）、**烟囱**（`smokePerHit`）、**开信刀**（`letterOpenerHit`）、**卡戎之灰**（`ashesDamage`）。这些行走 `RawAttackIntent`（不吃 `Hook.ModifyDamage`）⇒ 以前玩家身上有无实体时预览仍是 4/5/8，实际只掉 1。②`COMET_BOSS_POWER.description_dread` / `REGENT_BOSS.comet_dread` 的 **14 种非中文语言**从英文占位换成真正的本地化译文（德/西(es+spa)/法/印尼/意/日/韩/波/葡巴/俄/泰/土），术语沿用各语言既有官方译名；中韩日泰等用各自的「弃牌堆」写法。`tools/fix-comet-dread-loc.ps1` 是一次性脚本（⚠ 该 .ps1 必须带 UTF-8 BOM，否则 PS 5.1 按 ANSI 解码直接解析失败）。本地化检查 16/16 通过。注：**仅装机，未打包未上传**。)

### v0.2.811 

2026-11-19 (**碎屑储削弱：碎屑改塞弃牌堆**（用户 2026-11-19：「普通模式和恐惧模式都改成：每当储君生成碎屑时，反而将其加入你的弃牌堆……后面不变；描述也都改」））—— `CometDebrisRedirect` 的 `PileType.Draw` → **`PileType.Discard`**（以前碎屑随机洗进抽牌堆，玩家下一回合就能抽到、马上打掉换格挡；现在要等这一轮洗牌才回到手里）。两种模式同一次改动。文案：`powers.json` 的 `COMET_BOSS_POWER.description` / `.description_dread` 与 `monsters.json` 的 `REGENT_BOSS.comet` / `comet_dread` 四串 × 16 语言全部把「抽牌堆」改成「弃牌堆」（各语言用**本仓库既有的官方译法**：Abwurfstapel / pila de descarte / Défausse / Tumpukan Singkir / pila degli scarti / 捨て札 / 버린 카드 더미 / stos kart odrzuconych / Pilha de Descarte / стопка сброса / กองทิ้ง / Atık Deste）。本地化检查 16/16 通过。注：**仅装机，未打包未上传**。)

### v0.2.810 

2026-11-19 (**碎屑储恐惧版文案没改到 —— 说明框读的是 powers 表**（用户 2026-11-19 截图：「描述没改」））—— 说明框正文来自 **`powers.json` 的 `COMET_BOSS_POWER.description_dread`**（截图里「获得10点格挡」没有空格、带 [gold] 标记被吃掉，正是这一串），而 v0.2.809 我只改了 `monsters.json` 的 `REGENT_BOSS.comet_dread` ⇒ 面板看起来没变。现在两张表都按用户原文改成「…每当你打出碎屑时，清除所有玩家的所有虚弱和易伤。」（16 语言，本地化检查 16/16 通过）。⚠ 另外提醒：**改了 pck 必须重启游戏**才会重新读本地化。注：**仅装机，未打包未上传**。)

### v0.2.809 

2026-11-19 (**碎屑储在恐惧模式下没有增强 → 按用户口径补上**（用户 2026-11-19））—— 用户原文：「增强：修改特殊效果：每当储君生成碎屑时，反而将其加入你的抽牌堆。每当你打出碎屑时，清除所有玩家的所有虚弱和易伤。」⇒ `CometBossPower.AfterCardPlayed` 增加 `DreadMode` 分支：恐惧模式**只保留**「清掉所有玩家的虚弱/易伤」，**不再**给 10 点格挡、**也不再**让敌方各失去 10 点生命（第一句「碎屑进抽牌堆」本来两种模式一样，由 `CometDebrisRedirect` 负责）。`REGENT_BOSS.comet_dread` 文案按原文改写（16 语言，本地化检查全过）；`BossDefs` 里那句过时的注释（说恐惧版是「碎屑放抽牌堆顶部」，早已回退）一并订正。注：**仅装机，未打包未上传**（用户要求「不说就不要打包上传」）。)

### v0.2.808 

2026-11-19 (**球目标排查版**（用户 2026-11-19：「塔罗愚的电球怎么好像还是随机电」））—— v0.2.807 的两个前缀（`LightningOrb.Passive` / `DarkOrb.Passive`）确认已挂上（0.2.807 启动日志 `打上的补丁目标 222 个`，比 0.2.805 的 220 多 2 个），但日志里看不出改没改写目标。本版加两样：①`BossOrbTargeting.Rewrite` 打诊断（限 30 行，PC 上可见）——`球目标：<球类> 主人=<名或<null>> 传入=<名或<null>> ⇒ 改成 <名或<null>>`，一眼能看出「球的 Owner 是不是 null」「是否真的改写」。②`Owner == null` 的兜底：影子玩家的球在个别时机 Owner 为空（球刚生成 / 队列被换过）⇒ 只要场上**只有一只**我们的 Boss 就指向它（多 Boss 时不猜，避免打错人）。注：本版**未打包未上传**（用户要求「不说就不要打包上传」）。)

### v0.2.807 

2026-11-19 (**Boss 的电球/黑球会电到自己人**（用户 2026-11-19：「boss 的闪电充能球现在好像可以先电亡灵契约师」））—— 用户确认口径：**先打「被这只 Boss 锁定的那位玩家」的召唤物（奥斯提等），没有召唤物才打玩家本人**。根因：原版 `LightningOrb.Passive` / `DarkOrb.Passive` 在 `target` 为空时会从**施法者视角的 `HittableEnemies`** 里随机挑，而我们 Boss 的「敌人」里除了真人玩家还包含**别的 Boss 的影子玩家**及其随从（多 Boss 同场）⇒ 球会落到另一只 Boss 身上。新增 `Core/BossOrbTargeting` + 两个 Harmony 前缀（`LightningOrb.Passive` / `DarkOrb.Passive`，显式写死参数类型避免 AmbiguousMatchException）：只对「球的主人是我们的 Boss」改写目标，玩家自己的球**一律不动**；挑召唤物按 `CombatState.Creatures` 的**列表顺序**取第一个（两端确定性一致）。★ 本版同时含 v0.2.806 的「没装观者 mod 却遇到占位愚者」（`SubstituteIfUnavailable` 的目标 Key 写错成不存在的 `FOOL_BOSS`；以及 `PullNextEncounter` 的「沿用存档槽」分支绕过兜底 ⇒ 新增 `FixUnavailableEncounter`）。注：本版**未上传**。)

### v0.2.806 

2026-11-19 (**没装观者 mod 也会遇到观者 Boss（退化成占位愚者）**（用户 2026-11-19 截图：名字 `Characterbosses`、立绘占位愚者、特殊效果却是「斋戒」））—— 两层漏洞一起补：①`BossKeys.SubstituteIfUnavailable` 的兜底目标写成了 **`FOOL_BOSS`**，而 `EncounterOf` 的 switch 里根本没有这个 Key（塔罗愚叫 `FOOL_TAROT`）⇒ 落到 `default: return null` ⇒ 所有调用方都把 null 当成「没有替换」⇒ 观者遭遇原样保留（现在改成 `FOOL_TAROT`，111 正式版没有愚者遭遇则保持原样）；②「缺模组就换人」原先只管**按 Key 造遭遇**那条路，而 Boss 房实际走 `ActModel.PullNextEncounter`，它的「沿用地图上那只」分支会**原样返回存档槽里的观者遭遇**（那一局是装过观者 mod 时开的）⇒ 新增 `BossKeys.FixUnavailableEncounter(EncounterModel?)`：观者遭遇 + 本机探测不到观者 mod ⇒ 换成塔罗愚，并在 `PullNextEncounterPatch` 的返回前兜一次。注：本版**未上传**；闪电/黑球索敌奥斯提那条反馈还需确认口径。 )

### v0.2.805 

2026-11-19 (**手机端卡顿/耗电**（玩家「季节」工坊反馈））—— 实测数据：本模组的**帧回调只有 avg=0.05~0.08ms**（一帧预算的 0.5%），所以不是「逻辑慢」；真正的开销是**日志 I/O**：一局 3h40m 写了 **203,646 行 / 27.8MB**（≈15 行/秒），其中 `帧回调耗时(每120帧)` 一项 59,064 行（29%）、`结束回合按钮探针` 6,036、`原版回合结束步骤` 4,872、`效果追踪` 3,671、`观者 Boss`/`没装观者` 4,293、`性能(5s)` 1,557；而 `SafeLogPatch` 以前**每写一行都 `Directory.CreateDirectory` + 新建 FileStream + 关闭** ⇒ 手机上每次碰 flash 都贵、还费电。四处改动：①`SafeLogPatch` 改成**长开文件 + 缓冲写**（攒 32 行或 0.5s 才 flush；带 ⚠/失败/不同步 的关键行立刻 flush；每 5 分钟换句柄；帧驱动里 `FlushIfDue()` 兜底）；②新增 `Core.LogLoudness`：**手机（Android/iOS）上纯诊断行一律不打**，PC 上行为完全不变；`帧回调耗时` 在手机上只在单帧 >20ms（真卡了）时留一行；③`UI 会话重建`（进战斗房那一帧）以前**同帧重建所有 Boss 面板**—— 实测那一帧帧回调冲到 **131ms**（常态 0.05ms）⇒ 改成**排队、每帧只重建 1 只**；④`BossHandUi.RefreshForce` 加**同帧去重**：同一帧内每只 Boss 最多整排重建一次（一次出牌里好几处都会喊它，4 只 Boss 同场会叠）。注：本版**未上传**。)

### v0.2.804 

2026-11-19 (**3 玩家只出 2 只 Boss：人数改由「大厅玩家列表」判定**（用户 2026-11-19 截图 + `mod_log (42)/(43)`））—— 证据（房主侧 `mod_log (43)`，同一局来回跳）：`[23:42:23] 阵容人数：RunState 有 3 人，其中 **1 人已经 30 秒没说话** ⇒ 这一场按 2 只 Boss 出场`、`[23:42:41] …其中 0 人已经 30 秒没说话 ⇒ 这一场按 3 只`、`[23:43:11]` 又变回 2 只；而那位「没说话」的玩家 23:44 才因为别的原因掉线，当时明明在打 ⇒ 3 人局场上只有 2 只（截图 546/555 + 557/557）。根因是 `LivePlayerCount` 靠 `BossPicksMessage` 心跳猜活人，而客人每 5 秒才发一次、中间断一下就被判死。现在**优先用游戏自己维护的大厅玩家列表** `RunManager.RunLobby.Players`（`RunLobby.HandlePlayerLeftMessage` / `PlayerRejoined` 会更新它）—— 只有「不在大厅列表里的人」才算离开；拿不到列表才退回心跳判活，阈值从 30 秒放宽到 **120 秒**。日志也写清判据：`但大厅里只剩 N 人（M 人已不在大厅）` / `退回心跳判活`。★ 配合 v0.2.802（伙伴数量 = 阵容条数）双保险：人数或阵容任一处塌了，场上只数都不会再和阵容不同步。注：本版**未上传**。)

### v0.2.803 

2026-11-19 (**工坊反馈两条**（玩家 ATNAS 2026-11-19））—— ①「**恐惧模式联机，好友吃不到支配效果而导致不同步**」：根因找到了 —— 反编译 `DreadRunModel.BeforeRoomEntered` 是支配进战斗状态的**唯一一处**（普通战斗房 `CombatState.SetDominion(RunState.Dominion)`，Boss 房两处都设 null），而战斗里挂 power 的 `AfterEnemiesEnteredCombat` / `AfterPlayersEnteredCombat` 用的是 **CombatState 那一份**；客人的「本幕支配以房主为准」经常**晚于**这次拷贝（房主结论还没到）⇒ 我们事后只对齐了 `RunState.Dominion`，`CombatState` 仍是旧的/空的 ⇒ 客人整场吃不到支配、房主那边有 ⇒ 玩家回合开始校验和分叉。新增 `DreadDominionBeforeRoomPatch`（`BeforeRoomEntered` 前后各一次：前缀先对齐 RunState；**后缀先重新对齐 RunState 再同步 CombatState** —— 顺序不能反，原版方法体里 `ApplyCurrentDominion()` 会按本机槽位重设一次），并在 `EnsureCurrentAct` 末尾顺手同步一次 CombatState（房主结论晚到的场次也补得上）。②「**极速模式联机远端 boss 未替换而导致不同步**」：新增 `Boss 替换判定` 一行日志（模式 / 幕 / 本机角色 / 结果 / 地图 id 与替换开关 / 存档槽是否我们的 / 房主结论），每个 (幕,场,模式) 打一次 —— 极速(Blitz)、每日、自定义这些非 Standard 模式我们**从未实测过**，这一行能一次定位是哪条判据把它挡掉（极速模式的 `GenerateRooms` 只改「弱敌数量」，Boss 槽仍走 `_rooms.Boss`，所以最可能是「地图替换开关」或「客人还没收到房主结论」）。注：本版**未上传**。)

### v0.2.802 

2026-11-19 (**3 人局场上只有 1 只 Boss**（用户 2026-11-19 截图 + `mod_log (41)`）：房主现在**把整条阵容一起广播**，客人照抄，不再按自己那份可能过时的玩家数重算 —— 证据：日志 23:19:44 `※阵容输入※ … 玩家=1` ⇒ 阵容只排出 `[储君 3 号]` 1 条 ⇒ 23:21 结算 `REGENT_BOSS 为主，共 1 只 Boss`；同一局 23:21:47 重算时又变成 `3 人局` 3 条。三处改动：①`BossPicksSync` 的「每场最终遭遇」广播值从 `主Boss` 扩成 `主Boss,伙伴1,伙伴2…`（`HostLineupOf` 供客人整条照抄；`HostBossOf` 取逗号前那段 ⇒ 老的调用方零改动）；②房主 `BossLineups.For` 算完一条阵容就 `NoteHostLineup` 记下，并且**每 5 秒无条件重播一次**结论（以前只在「有客人上报没到齐」时重播 ⇒ 客人拿到一次过时就一直错）；客人收到后**只在内容真变了**时才清阵容缓存（否则每 5 秒重算刷屏）；③`MultiBossPlan.PartnersForWithKeys` 的**伙伴数量改由阵容条数决定**（`lineup.Length − 1`），不再问 `WantsSecondBoss`（那个数取自「玩家数」，可能过时）；每条早退都补了日志（`伙伴 Boss 判定：… 阵容只有 1 条 ⇒ 不加伙伴`）。★ 本版同时含 0.2.801 的两条打击战修复（删掉用户没要过的「恐惧模式每张 +1」；多人不再把所有玩家的打击牌加一起）。注：本版**未上传**。)

### v0.2.801 

2026-11-19 (**打击战「痛击」两条：删掉用户没要过的『恐惧模式每张 +1』；多人不再把所有玩家的打击牌加一起**（用户 2026-11-19：「这个增强我从来没让实现过，删掉」+「多人第三回合完美打击打 100+」））—— ①`BossBashPower.PerCard()` 删掉 `if (DreadMode.IsActive) per += 1`（那是当年实现者自己加的，用户当轮只要求「让**痛击**的伤害也和完美打击一样受到打击的加成」，即 `BashBonusDamage`）。现在进阶 0～8 每张 +2、进阶 9～10 每张 +3，恐惧与正常**完全相同**。②多人：`CountNow` 以前 `foreach (real player)` 把**所有真人玩家**四个牌堆里的打击牌全加一起（4 人局 ≈ 7 + 40 = 47 张 × 3 ⇒ 完美打击 147，正是玩家报的「第三回合 100+」），`AfterCardPlayed` 也「谁打都算」（连别的 Boss 的影子玩家打出的打击也算）。现在两处都改用 `BossLockOn.SpecialEffectAppliesTo`（本仓库「对应型」特殊效果的统一口径）：单人恒 true ⇒ 零回归；多人只数**被她锁定的那位**玩家的牌堆 / 出牌。③连带修复：非打击战的恐惧版「燃烧契约」（`BossScript` 里 `CountNow - OwnStrikeCards` 那一支）同样不再把别人的打击算进去。注：本版**未安装/未上传**。)

### v0.2.800 

2026-11-19 (**真凶：吞噬生命「只让房主结算」⇒ 联机必炸 StateDivergence；顺带把「阵容人数」那套乱跳的判据收干净**（用户 2026-11-19「似乎没修好」，给的是 `mod_log (38)/(39)` + `godot (22)/(23)`））—— ①**联机被踢**：客人 `godot (22).log` 22:37:41 `[RunLobby] Disconnected. Reason: StateDivergence`（checksum 56）；`mod_log` 对上后一眼看到源头 —— 房主 22:37:39 `吞噬生命：她给 Skyphantom 上了 VULNERABLE_POWER（2）⇒ 获得 6 点格挡 + 血肉戏法 13 点无来源伤害`，客人同一秒只有 `吞噬生命：本机是客人 ⇒ 只记录、不改状态（避免联机不同步）` ⇒ 房主那只 Boss 多 6 格挡、那位玩家少 13 血，客人一个数都没动。根因是 `BossDevourLifePower` 里 v0.2.815 加的 `if (!NetRole.IsHostOrSolo) return;`：战况（格挡/掉血）**不是**网络同步项，游戏靠「两端各跑一遍同样的命令」保持一致，只让一端改就是**制造**分发（本仓库 `DominateWeakPower` / `BossStellarGatheringPower` 同样在 `AfterPowerAmountChanged` 里改战况、两端都跑，从来没这问题）⇒ 删掉那道闸，两端都结算（日志实证两端钩子输入逐字一致，`amount`/`applier` 都一样）。②**阵容人数乱跳**（同一份日志 22:36:25 数出 2 人、22:36:55 数出 1 人，其实 3 人都在玩）：`LivePlayerCount` 以前把「从没听到过」的玩家当已离开，而客人收到房主结论后就不再发消息（`Tick` 里 `if (Received) return`）⇒ 房主听不到客人 ⇒ 人数一路塌 ⇒ 阵容条数跟着塌 ⇒ 两端 Boss 条数对不上，还会触发 v0.2.799 那个「主 Boss 当自己的伙伴」。现在：没听到过的人**按还在算**（只有「听过之后安静 >30 秒」才剔除）、日志只在数变了时打一行；客人收到结论后**每 5 秒仍发一次无副作用心跳**；房主的 `pc=` 一变就立刻重播。③`MultiBossPlan.WantsSecondBoss` 改成**只认 `BossLineups.PlayerCount()`**（阵容几条 = 伙伴几只，天然同口径），不再优先读 `RunState.Players.Count`。注：本版**未上传**。)

### v0.2.799 

2026-11-19 (**修：多 Boss 局把主 Boss 当成自己的伙伴 ⇒ 场上两只同角色 Boss**（用户 2026-11-19，`mod_log (36)(37)` 22:26））—— 证据：`(36)` 阵容=[NECROBINDER_FLESH,SILENT_SNAKEBITE,IRONCLAD_STRIKE]（正常三只），`(37)` 变成 阵容=[NECROBINDER_FLESH] 伙伴=[NECROBINDER_FLESH] ⇒ 两只亡灵契约师同场。根因在 `MultiBossPlan.PartnersForWithKeys`：它只跳过「与主 Boss **同 Key**」的条目，而且 v0.2.526 为了对付「阵容排出同一只两次 ⇒ 2 人局只出 1 只 Boss」加了一条兜底「不够就把重复的那只也补上」—— 当阵容里只剩主 Boss 一个条目时（候补表被勾选裁到只剩一只、或第二场 `usedInThisAct` 清空），这条兜底补出来的**就是主 Boss 自己**。修法（用户口径）：**伙伴不得与主 Boss 或已有伙伴同角色**（按 `BossKeys.CharacterOf(key)` 判，同角色的不同变体也算同一角色），**凑不满就少出**（宁可这一场只上一只 Boss）；删掉「补重复」兜底。日志：`伙伴名额 N 只、实际只凑到 M 只 ⇒ **少出**`（列出被挡下的候选）、新增 `※伙伴角色去重自检※`（场上角色列表 + 不同角色数 + 通过/失败，多人局两端应逐字一致）。注：本版**未上传**。)

### v0.2.798 

2026-11-19 (**修：恐惧模式下血肉骨的「血肉戏法」没有升级**（用户 2026-11-19））—— 根因：血肉骨（`NECROBINDER_FLESH`，第一幕）的 `BossVariant` 只配了 `AscensionUpgrades`，**没有配 `DreadUpgrades`**；而设计上恐惧模式要把它升到 13（`SleightOfFlesh.OnUpgrade` +4）。以前只有 `BossDevourLifePower` 里那句 `if (dread) sleight = FleshBone.SleightNow(sleight)` 在**伤害结算**时手动 +4 来「模拟」升级 ⇒ 牌面仍是未升级、她身上那层 `SleightOfFleshPower` 也还是 9（用户看到的就是「没升级」）。修法：①`BossDefs` 里给该变体加 `DreadUpgrades = { SleightOfFlesh }`（走运行期通道 ⇒ 卡组与剧本手牌里同名那张都会升级，卡面显示 +，能力层数就是 13）；②**去掉** `BossDevourLifePower` 里的手动 +4 —— 否则升级后 13 再加 4 会变成 **17**（双算）。`FleshBone.SleightNow` 保留但已无调用方（注释说明）。注：本版**未安装/未上传**。)

### v0.2.797 

2026-11-19 (**修：中途有人离开游戏后 Boss 房变成 4v4（多一只面对没人操控的角色）**（用户 2026-11-19））—— 现象：4 人局里有人没能进房间，剩下 3 个人进 Boss 房却还是 **4 只 Boss**，其中一只面对一个没人操控的角色。（「剩下的人结束不了回合」那条是**原版机制**：`CombatManager.AllPlayersReadyToEndTurn()` 会一直等那位缺席玩家点结束回合，不是我们的问题。）我们的那份：阵容条数 = `RunState.Players.Count`，而**已离开/掉线的人还留在 Players 里** ⇒ 照着造了 4 只。修法：`BossPicksSync` 增加**存活判定** —— `NoteHeard(senderId)` 记下每个对端最后一次说话的时刻（`BossPicksMessage` 每秒心跳，房主广播 / 客人上报都会走到它），`LivePlayerCount(fallback)` = 本机自己 + 「最近 30 秒内说过话」的其它玩家；**一条消息都还没收到时不做判断**（避免开局把 4 人算成 1 人）。`BossLineups.PlayerCount()`：**房主端**用它（人数变少就按实际人数组阵容），客人端继续只认房主广播的 `pc=`（v0.2.795）⇒ 两端一起就是 3v3。日志：`阵容人数：RunState 有 4 人，但只有 3 人还在说话（其余已离开 / 掉线）⇒ 这一场按 3 只 Boss 出场`。注：本版**未安装/未上传**。)

### v0.2.796 

2026-11-19 (**修：客人端「一层 Boss 第一次进就不同步」+ 客机顶栏显示原版 Boss（同一个根因）**（用户 2026-11-19 实测 + 朋友/自己的两份 `mod_log` 对表））—— 实证：两份日志的 4 人局（种子 3146193779372856658 / 11890500115929763354）**阵容指纹、每场生成指纹、锁定分配、Boss 手牌/抽牌/计划、玩家 HP 与 powers 全部逐字相同**，唯一异常在客人端摘要那一行：`遭遇=NECROBINDER_DOOM_BOSS_ENCOUNTER 本机=客人 **本幕存档 Boss 槽=LAGAVULIN_MATRIARCH_BOSS**` —— 客人端 `Act.BossEncounter` 槽位**还是原版 Boss**，而实际打的是我们替换进去的遭遇。后果两条：①顶栏那颗 Boss 图标按槽位取图 ⇒ 显示原版 Boss（用户看到的现象）；②原版「进 Boss 房要清本幕支配」的判据正是**槽位 id**（`RemoveDominionIfRoomIsBoss`：`combatRoom.Encounter.Id == Act.BossEncounter.Id || == SecondBossEncounter?.Id`）⇒ 客人端这一比**对不上** ⇒ 支配没被清掉，`godot (16).log` 的状态转储里 4 个玩家身上都还挂着 `NECROBINDER_DOMINION_POWER`（房主清了）⇒ 第一个 `After player turn start` 就分叉。修法：①新增 `Patches/OurBossRoomClearsDominionPatch`（`DreadRunModel.AfterRoomEntered` 后缀：房间遭遇是我们的 Boss 且 `RoomType.Boss` ⇒ 照原版补一次清支配，幂等）；②`TopBarBossIconFallbackPatch` 增加 `SubstituteWithOurLineup`：槽位不是我们的、而本幕本场阵容推导出是我们的 ⇒ **只换取图用的遭遇对象**（不动存档状态）⇒ 顶栏显示真正的 Boss。注：本版**未安装/未上传**。另：观者/愚者按层写进多人阵容清单的需求还在做（愚者 1/2 层没有设计，需要先确认是复用三层那套还是留空位）。)

### v0.2.795 

2026-11-19 (**联机不同步：阵容条数（Boss 数）改由房主权威**（房主 + 客人两份 `mod_log` 对表实证，工具 `tools/compare-mp-logs.ps1`））—— 对表结果：`※阵容输入※` 六场只差「已收到房主勾选」那一项（指纹六场**全同**：8D4A52CF / DC93ED64 / 9E8B234F / 099C9DC7 / 065DEBF0 / 2A70123D），`⚠ 不同步状态摘要` 的 #1~#7 回合快照**逐字全同**；唯一的实质分叉在 `※多 Boss 生成指纹※` 的**第 3 层第 2 场**：房主 `玩家=2 · 阵容=[FOOL_TAROT,REGENT_CHOICE] · 我=FOOL_TAROT`，客人 `玩家=3 · 阵容=[SILENT_SHIV,IRONCLAD_BOULDER,REGENT_STAR] · 我=SILENT_SHIV` ⇒ 两端造出的是**完全不同的 Boss 编队**（条数 2 vs 3），场上怪物自然不同。根因：阵容条数 = `RunState.Players.Count`（`BossLineups.PlayerCount()`），玩家掉线/重连那一刻两端会短暂不一致，而阵容是**确定性推导后缓存**的 ⇒ 一旦按不同人数算过就永久分叉。修法：把「这一场用几个人」随玩法设置一起**由房主广播**（`BossPicksMessage.Settings` 新增 `pc=`），客人端 `BossLineups.PlayerCount()` **只认房主的数**（`BossPicksSync.HostPlayersPerFight`，与勾选/共用匹配池/每场最终遭遇同一原则）；房主自己、单人、以及房主数据还没到时行为完全不变。附带确认：两份日志里 `勾选同步：广播失败 … 'CharacterBosses.Core.BossPicksMessage' was not present in the dictionary` 是 v0.2.772 之前的老版本（那版 BaseLib 认不出我们的消息类型），新版的直接登记已在日志里生效。注：本版**未安装/未上传**。)

### v0.2.794 

2026-11-19 (**放血战多人换牌改口径 + 仆从储的仆从血量不再按人数翻倍**（用户 2026-11-19 两条需求）—— ①「增强放血战，多人时将第二回合的时候未到替换为肉盾，保证他能正常给其它 boss 减伤。不再将全身撞击替换为恶魔护盾」：`MultiplayerCardSwap` 删掉 `BodySlam → DemonicShield`（含它的强制升级行），新增 `NotYet → Tank`（第二幕铁甲战士）；`BleedIroncladScript` 卡组去掉 `DemonicShield`、加上 `Tank`，第 1／6 回合恢复成全身撞击+，第 2 回合仍是「时候未到」（多人由换牌表替换）。⚠ 光换牌不够：反编译 `TankPower.AfterApplied` 只给 `item.IsPlayer` 的队友发 `GuardedPower`，而**我们其它 Boss 不是玩家生物** ⇒ 新增 `Patches/TankPowerGuardsBossAlliesPatch`（后缀 `TankPower.AfterApplied`：当持有者是我们 Boss 的影子玩家时，给同场其它活着的 `CharBossMonster` 各发同样层数的 `GuardedPower`；真人玩家自己打的肉盾一个字节都不改）。肉盾本身的代价照原版：持有者受到的攻击伤害 ×1.5。②「仆从储的仆从血量会在多人模式时翻倍。让他和 boss 血量一样不翻倍」：`BossNoMultiplayerHpScalePatch`（`Creature.ScaleMonsterHpForMultiplayer` 前缀）以前只跳过 `CharBossMonster` ⇒ 补上 `Monsters.BossMinionMonster`；顺手把 `BossNoMultiplayerBlockScalePatch`（格挡缩放）也一起覆盖仆从，两条都只在多人生效、单人零变化。注：本版**未安装/未上传**。)

### v0.2.793 

2026-11-19 (**修：一支编队里「晚进战斗列表」的那只敌人吃不到支配**（玩家反馈 2026-11-19：「3 层战士的支配时，活体盾和机枪手的组合，活体盾有恶魔形态、机枪手没有」））—— 根因：我们三个「挂在敌人身上」的支配（铁甲战士=恶魔形态 / 储君=王之凝视 / 观者=虔信）以前只在 `AfterRoomEntered` 那一刻给 `cr.Enemies` 里**当时已经在列表里**的敌人挂；而原版 `DominionModel` 的设计是两条派发：`AfterRoomEntered(room)` → `AfterEnemiesEnteredCombat(所有敌人)`，以及 `AfterCreatureAddedToCombat(creature)` → `AfterEnemiesEnteredCombat(这一个)` ⇒ **进房之后才加入战斗的那只**要走第二条。我们从来没实现第二条 ⇒ 机枪手（编队里晚一步进列表）一条霸符都没有。现在三个支配都补上 `protected override AfterEnemiesEnteredCombat`，并且两侧都做**幂等**（`GetPower<…>() != null` 就跳过，不会叠成 2/4 层），日志带上来源：`恐惧模式·铁甲战士的支配（后加入战斗）：1 个敌人各获得 1 层恶魔形态` / `（后加入战斗）：N 个敌人已有恶魔形态 ⇒ 不重复挂`。顺带把「取当前房间」抽成 `DreadMode.FirstRoomOf(...)` 供三个支配共用（Boss 房闸门在 `AfterEnemiesEnteredCombat` 这条路上同样生效）。注：本版**未安装/未上传**。

### v0.2.792 

2026-11-19 (**「看不到 boss 手牌」补一条定位日志**（承接 v0.2.791；工坊反馈者装了卡图/皮肤模组，但作者本机装同一个皮肤一切正常 ⇒ 需要玩家侧一行数据判断卡在哪一步））—— `UI/BossHandPanel.LogSelfCheck`：每回合首次（手牌张数变化时）打一行 `手牌栏自检：N 张待显示 ⇒ 格子 X、卡面节点 Y、可见卡面 Z（面板 visible=…, pos=…, size=…）`。读法：格子 0 ⇒ 没选出该显示的牌；卡面节点 0 ⇒ `NCard.Create` 就废了（皮肤模组改了 NCard）；可见卡面 0 ⇒ 节点建了但没显示（`UpdateVisuals` 抛异常或尺寸被清 0）；三项都对却看不见 ⇒ 是面板显隐/布局问题。配合 v0.2.791 的三层兜底（原版 → 跳过 `UpdateVisuals` → 文本占位）与 `手牌卡面…` 失败日志，一次就能定位。注：本版**未安装/未上传**（作者本机无法复现，等玩家日志）。)

### v0.2.791 

2026-11-19 (**修：装了卡图/皮肤类模组时「看不到 boss 手牌」**（工坊玩家反馈 2026-11-19：「装了卡厄斯皮肤会看不到 boss 手牌」，皮肤 = 工坊「亡灵契约者卡图 卡厄斯修女奥尔莱亚」/ Cards+Cosmetics））—— 根因方向：`UI/BossHandCard.BuildCardFace` 的卡面完全依赖 `NCard.Create` + `UpdateVisuals(PileType.Hand, ...)`，而**卡图/皮肤类模组正是接管这两步去换贴图**；一旦它在我们的语境里抛异常（影子玩家 / 非玩家手牌的卡），旧代码只打一行 `NCard.Create failed: …` 然后**什么都不画** ⇒ 手牌栏看起来是空的。现在三层兜底：①原版路径；②换一张新节点并**跳过 UpdateVisuals**（皮肤换图那步不做，卡牌本体还在）；③仍失败则画**纯文本占位**（卡牌 id），保证手牌栏不是空的。三类失败都带日志：`手牌卡面：<牌> 的「刷新视觉」这一步被别的模组掐了（<异常类型>: <消息>）⇒ 已退回…` / `手牌卡面渲染失败：<牌>（…）⇒ 改成文本占位`。⚠ 这是**防御性修复**：真正是哪个模组、在哪一步抛的，要靠玩家复现后那行日志确认。注：本版**未安装/未上传**。

### v0.2.790 

2026-11-19 (**修：玩家的奥斯提会吃到减益（王之凝视）**（用户 2026-11-19：「带有储君的支配（王之凝视）的小怪攻击亡灵契约师会导致奥斯提减力量。奥斯提不应该被施加任何减益」）—— 链路：恐惧模式「储君的支配」把原版 `MonarchsGazePower` 挂在所有敌人身上（`RegentDominion`），它的 `AfterDamageGiven` 对「被这只敌人打中的目标」施加 `MonarchsGazeStrengthDownPower`（`: TemporaryStrengthPower`，`IsPositive=false` ⇒ `Type == Debuff`）—— 小怪打亡灵契约师时，那一下被**玩家的奥斯提**接住 ⇒ 减益落到奥斯提身上。根因：`BossPetNoStrengthLossPatch`（随从不吃负面）只认 `IsBossPet`（**我们 Boss 那侧**的随从）⇒ 玩家自己那只奥斯提完全没被管到（之前只有「不吃毒」那条 `OstyPoisonImmunePatch` 是两隻都管的）。现在同一条拦截改用 `Core.BossOsty.IsAnyOsty(target)`：**两只奥斯提都免疫减益**（玩家那只只拦减益、正面照原版；Boss 侧那只保持 2026-10-23 口径：任何来源的负面都不吃、真人玩家给的正面也不吃）。日志 `玩家的奥斯提免疫负面效果：跳过 MONARCHS_GAZE_STRENGTH_DOWN_POWER …`。注：本版**未安装/未上传**（等你确认后一起打包）。)

### v0.2.789 

2026-11-19 (**修：Boss 战里也触发了支配（亡灵契约师的支配在 Boss 房照样加费）**（玩家反馈 + 玩家本机日志实证））—— 日志铁证：`[19:42:10] 恐惧模式·支配对齐：本幕支配 无 → NECROBINDER_DOMINION（游戏设完支配后立刻核对；来源=本机槽位）`，紧跟着 `[19:42:11] 恐惧模式·亡灵契约师的支配：1 名玩家获得…` 与 `[19:42:12] 亡灵契约师的支配：Watcher 的「WATCHER_MIRACLE」本回合费用 +1` —— 也就是说游戏**刚在进本幕 Boss 遭遇时把支配清成 null**（反编译 `DreadRunModel.RemoveDominionIfRoomIsBoss`：`RunState.SetDominion(null)` + `combatRoom.CombatState.SetDominion(null)`），我们 v0.2.780 那条 `RunState.SetDominion` 后缀**又把它塞了回去**。**v0.2.786 那次「boss 不应该受支配影响」并没有修到这里** —— 那次只是让我们的 Boss 不吃恶魔形态/王之凝视/虔信（`cr.Enemies` 里跳过 `CharBossMonster`），支配本身在 Boss 房里照样在跑。本版三处收口：①`DreadMode.IsActBossRoom(room)`（判据与游戏逐字同源：房间遭遇 id == 本幕 `BossEncounter` / `SecondBossEncounter`）；②`DreadDominionSetPatch` 改为看实参——游戏传 **null** 时（唯一场合就是进 Boss 房）**绝不再塞回去**；③`DreadDominionSync.EnsureCurrentAct` 在 Boss 房直接返回（不再靠 `FireCombatStart` / 心跳 / 槽位替换把它改回来），并且 7 个支配的 `AfterRoomEntered`（+ 静默猎手那条 `AfterSideTurnStart` 兜底）统一加 `DreadMode.SkipBecauseActBossRoom(...)` 闸门，跳过时打一行 `恐惧模式·<支配>：本幕 Boss 房 ⇒ 不生效（Boss 战里不该有支配）`。注：本版与 v0.2.787

### v0.2.788 

**一起**待安装。 / 2026-11-19 (**修：结算/历史里「每一局都显示灵魂被亡灵契约师收走了」**（用户 2026-11-19 三张截图 + 反馈：「这不同的几局怎么都显示灵魂被亡灵契约师收走了（只有最后一局是死于灵魂骨的）」）—— 日志实证（同一秒刷几十行）`死亡提示按凶手显示：NECROBINDER_SOUL_BOSS_ENCOUNTER（本场遭遇是 SILENT_SNECKO_BOSS_ENCOUNTER）` / `（本场遭遇是 SPINY_TOAD_NORMAL）`：**结算/历史界面会为本局每一场战斗各问一次 `EncounterModel.GetLossMessageFor`**，而 v0.2.4xx 那条「死亡提示按凶手显示」的补丁不区分「这一格是哪一场」——它拿到的永远是 `BossKillerTracker` 里**上一场（甚至上一局）**记下的凶手 ⇒ 本局每一格都被改写成同一句。现在 `BossKillerTracker` 额外记下「记录凶手那一场战斗的遭遇 id」和「那一局」(`IRunState`)，新增 `IsDeathCombat(encounterId)`：**遭遇 id 相同 且 还是同一局**才允许按凶手改写，否则保持原版（原版本来就按每一格自己的遭遇取 `<Id>.loss`，是对的；多人同场两只 Boss 的「报凶手」仍然照旧生效，因为那两种情况遭遇相同、同一局）。塔罗愚的 `.loss_tarot` 也加了同一道闸门。被挡下的每一格各打一行 `死亡提示：<遭遇> 不是记录凶手的那一场（那一场是 …）⇒ 保持原版提示` 便于核对。注：本版**未安装**（安装时游戏正在运行）。)

### v0.2.787 

2026-11-19 (**修：小刀的意图多了一条「强化」**（用户 2026-11-19：「小刀的意图错误的包含了强化」）—— 日志实证（玩家本机 `mod_log.txt` 19:18:46）`牌面识别（自动）：SHIV ⇒ 强化来源 [印牌]`。根因：`Core.CardBehaviorScan` 为了自动识别「抽牌/印牌/捞牌/返顶/烧牌/变牌/进姿态」会扫这张牌类型上**所有方法 + 所有嵌套类型的实例方法**（异步状态机）；而 0.113 的 `Shiv` 除了 `OnPlay`（纯一刀 4/6 伤 + `NShivThrowVfx.Create` 特效、一个能识别的调用都没有）还带**静态工厂** `Shiv.CreateInHand(...)`，它里面是 `combatState.CreateCard<Shiv>(owner)` + `CardPileCmd.AddGeneratedCardsToCombat(...)` ⇒ 静态方法本身扫不到（BindingFlags 里没有 Static），但它编译出来的状态机 `<CreateInHand>d__11` / `d__12` 是**嵌套类型**、`MoveNext` 是实例方法 ⇒ 被扫进来 ⇒ 把「别的牌调用这个工厂时才发生的事」算成「小刀自己打出去做了什么」⇒ 小刀多一条强化箭头。现在 `IsStaticFactoryStateMachine` 跳过名字形如 `<Create...>d__N` 的嵌套类型（= 静态工厂的状态机），只认这张牌自己的出牌路径；另把「是哪个方法看出来的」写进同一行日志（`（来源方法：印牌←…）`），下次再有误判一眼定位。⚠ 只影响意图图标，不碰任何结算。注：本版**未安装**（安装时游戏正在运行）。)

### v0.2.786 

2026-11-19 (**支配一律不作用于角色 Boss**（用户 2026-11-19：「boss 不应该受支配影响」）—— 恐惧模式的支配本来就不该给 Boss 加码，Boss 有自己的机制。逐条收口：①`IroncladDominion`（恶魔形态）跳过 `Monsters.CharBossMonster`；②`RegentDominion`（王之凝视）同样跳过；③`WatcherDevotionDominion`（虔信）本来就跳过（v0.2.781），这次补上跳过计数日志；④`DominionDemonFormPatch`（v0.2.785 把敌人侧的恶魔形态改到玩家回合开始加力量）增加 Boss 排除 —— 我们自己的角色 Boss 如果自己打出/身上有恶魔形态，**照原版**在她自己的回合开始加力量，不被这条支配补丁改时机。跳过时都会打一行 `跳过 N 只角色 Boss（Boss 不受支配影响）`。（注：静默/故障/亡灵/愚者的支配本来就只作用在玩家身上，不涉及 Boss。）

### v0.2.785 

2026-11-19 (**支配两条修：观者的支配说明读不到文案 + 铁甲战士的支配·恶魔形态改成玩家回合开始时加力量**（用户 2026-11-19 截图 + 反馈）—— ①「文本有问题」：支配提示框显示原始 key `DREAD.WATCHER_DEVOTION.description`。反编译 `DominionModel.Description`：它拼的是 `\"DREAD.\" + Id.Entry 去掉结尾 \"_DOMINION\" + \".description\"`，而我们的支配类叫 `WatcherDevotionDominion` ⇒ entry = `WATCHER_DEVOTION_DOMINION` ⇒ 需要 **`DREAD.WATCHER_DEVOTION.description`**；之前 16 语言 `ascension.json` 里写的是 `DREAD.WATCHER.description`（少一段、而且内容还是老口径「所有玩家获得 3 层虔信」）⇒ 现在 16 语言全部改成新键，文案同步成现行机制：所有敌人获得 2 层虔信 / 你的回合开始时每个敌人 +2 真言 / 满 10 层消耗 10 进神格、那一回合攻击三倍伤害。②「战士的支配让恶魔形态在玩家回合开始时获得力量，而不是怪物的回合开始时」：反编译 `DemonFormPower.AfterSideTurnStart` 的唯一判据是 `participants.Contains(Owner)` ⇒ 挂在敌人身上就变成「敌人回合开始加力量」。新增 `Patches/DominionDemonFormPatch`：持有者是敌人 + 恐惧模式时，把 `participants` 换成「只含持有者」（`side == Player`）或空列表（`side == Enemy`）⇒ 原版照常跑（连 `Flash()` 和恶魔形态 VFX 都在）但时机挪到**玩家回合开始**；玩家自己拿恶魔形态、以及非恐惧模式一律不动。)

### v0.2.784 

2026-11-19 (**观者 Boss 的地图徽章改成「她头像的剪影」+ 观者的支配回调成 2 层虔信（第 5/10/15 回合进神格）**（用户 2026-11-19 实测截图 + 两条要求）—— ①「观者 boss 图标应该用她头像的剪影（对于没有专属头像的 boss 来说）」：截图里那颗还是**同族小队（TheKin）的乌鸦占位图**。根因：`BossMapPointCharacterIconPatch` 本来就会把「没有专属美术」的遭遇换成 `CharacterEmblem.Silhouette(角色头像)`，但它解析角色只走 `TopBarBossIconFallbackPatch.CharacterFor`（按怪物 id 查 `BossDefs` → `CharacterType`），而**观者是别的模组的角色**、`CharacterType` 初值是**愚者占位**（运行期才被 `RefreshWatcherCharacterType` 换掉，地图页这一刻通常还没换）⇒ 解析不出 ⇒ `Silhouette(null)` 返回 null ⇒ 那位 patch 直接 return，地图上留着原版占位图。现在 `CharacterFor` 增加**观者兜底** `WatcherCharacterFor`（遭遇 id 以 `WATCHER_` 开头就问 `WatcherModPresence.CharacterType()`，拿到真观者角色；愚者占位 / 问不到就返回 null，宁可留占位图也不把愚者安到她头上），地图徽章 / 顶栏 / 结算三处同时受益；另加一行诊断日志 `地图徽章：<遭遇> 生成角色剪影失败（角色=…）⇒ 保持原版占位图`。②「回调观者的支配，改回给敌人 2 虔信（第 5、10、15 等回合进神格）」：`WatcherDevotionDominion.FaithStacks` 3 → **2**（对账：2/回合 ⇒ 第 5、10、15 回合各满 10 层进神格），16 语言 `WATCHER_DOMINION_DEVOTION_POWER.description` 里的「3 层」同步改成「2 层」。)

### v0.2.783 

2026-11-19 (**观者的支配：虔信/真言用观者 mod 的原版图标 + 虔信改成 buff（不再红框）**（用户 2026-11-19 实测截图 + 两条要求）—— ①「虔信和真言的图标用观者 mod 中的同名 buff 的图标」：从 **B 版观者 mod**（`.tools/phone-stage/mods/Watcher.pck`）里 `res://Watcher/_imported/devotion_power.png-*.ctex`（合十的双手）与 `res://Watcher/_imported/mantra.png-*.ctex`（紫色光芒眼睛）用 `tools/art/dump_texture_png.gd` 解出真图 → `tools/art/import_proj/CharacterBosses/images/powers/watcher_devotion.png` / `watcher_mantra.png` → Godot 导入（`.godot/imported/*.ctex`）→ `tools/art/pck_extras.txt` 追加 6 行随 PCK 发布；`BossSpecialPowerIconPatch` 新增 `WatcherConceptIconOf`（`WatcherFaithPower` / `WatcherDominionDevotionPower` ⇒ 虔信图，`DominionMantraPower` ⇒ 真言图，资源不存在退回好奇灯泡）。②「这里的虔信不应该是 debuff，不应该显示成红的框」：`WatcherDominionDevotionPower.Type` 与 `WatcherFaithPower.Type` 从 `PowerType.Debuff` 改成 `PowerType.Buff`（红框是 DeBuff 的类型外观）。③说明：`DominionDivinityPower`（神格）暂时仍是好奇灯泡 —— 用户这轮只点了虔信与真言两张图。）

### v0.2.782 

2026-11-19 (**观者的支配：把虔信做成真会生效的机制 —— 怪物攒真言、满 10 层进神格、那一回合攻击三倍伤害**（用户 2026-11-19 口径订正）—— 用户原话：「就是给敌人挂虔信，积累 10 点真言后消耗 10 点真言进神格（本回合 3 倍伤害）一回合。这些敌人的虔信在**玩家回合开始时**触发，并且在进神格的当回合开始时不会退出神格，**执行完自己的意图才会退神格**（这些描述不影响 boss，尤其是与神格观的机制无关，别改这个的时候把神格观带偏了）（也就是怪物第四回合、第七回合等回合会进神格，当回合伤害翻三倍）」。实现（全部在 `Powers/DominionPowers.cs`，`#if STS2_113`）：①`WatcherDominionDevotionPower`（虔信，`Amount`=3）—— **玩家侧回合开始**时给持有者 +3 真言，满 10 就扣 10 并挂上神格；持有者是我们的 `CharBossMonster` 时**直接跳过**（神格观走她自己的真言/姿态，两条链完全隔离）；②`DominionMantraPower`（真言）—— 就是那个可见计数；③`DominionDivinityPower`（神格）—— `ModifyDamageMultiplicative` 只放大「它自己作为 dealer 的有来源攻击」（反编译确认 `Hook.ModifyDamageInternal` 遍历**全部** hook 监听者 ⇒ 敌人身上的 power 也能改它自己打出来的伤害），退出时机在 `AfterSideTurnEnd` 且必须是**敌人侧 + 它在 participants 里**（= 意图执行完才退，回合开始不退）。数值对账：3 层/回合 ⇒ 第 4（12-10=2）、7（2+3×3=11-10=1）、10（1+3×3=10-10=0）回合各进一次神格，与用户说的一致。④`CharDominions.WatcherDevotionDominion` 改成只往 `cr.Enemies` 挂（跳过我们自己的 Boss、幂等不叠层），日志 `恐惧模式·观者的支配：N 个敌人各获得 3 层虔信`。⑤`content/localization/*/powers.json` 16 语言补齐 `WATCHER_FAITH_POWER` + 三条新 key（`WATCHER_DOMINION_DEVOTION_POWER` / `DOMINION_MANTRA_POWER` / `DOMINION_DIVINITY_POWER`）—— 之前 tooltip 显示原始 key 就是因为这 16 个文件里根本没有这些条。）

### v0.2.781 

2026-11-19 (**观者的支配修正：虔信只给敌人，不再给玩家挂 debuff；顺手补齐 16 语言 tooltip**（玩家 2026-11-19 截图 + 订正）—— ①**现象**：恐惧模式「观者的支配」的提示框显示**原始 key**（`WATCHER_FAITH_POWER.title` / `.description`），3 层虔信挂在**玩家**身上（玩家血条下面那个带 3 的图标），敌人身上什么都没有。②**根因一（文案）**：`content/localization/*/powers.json` 16 个语言里**从来没有**这两条 key —— `WATCHER_FAITH_POWER` 是后来新加的 power，只写了代码没写文案 ⇒ 现在 16 语言全部补齐（zhs：`这个敌人的回合开始时，观者获得 3 层[gold]真言[/gold]。`）。③**根因二（挂错人）**：`WatcherDevotionDominion.AfterRoomEntered` 以前把虔信 `PowerCmd.Apply` 到 `cr.CombatState.Players`（用户订正：「并不应该给玩家上 debuff，而是只给敌人上虔信」；口径本来就是「所有敌人获得 3 层虔信」）⇒ 现在改成遍历 `cr.Enemies`（只挑活着的）各挂 3 层，日志改成 `恐惧模式·观者的支配：N 个敌人各获得 3 层虔信`。④**已知限制（如实记下）**：恐惧模式的支配会在**进入本幕最终 Boss 战之前**被游戏清掉（反编译 `DreadRunModel.RemoveDominionIfRoomIsBoss` ⇒ `Removing dominion because we are entering the boss encounter`），而观者只作为本幕 Boss 上场 ⇒ 普通房间里挂了虔信也**没有真言接收方**，这条支配目前只有图标、没有实际数值影响；要不要给它换一个真正生效的效果等玩家决定。）

### v0.2.780 

2026-11-19 (**三机联机不同步：客人端「本幕支配」按房主结论解析 + 只认房主消息 + 空载荷不再抹掉已知结论**（玩家 2026-11-18 反馈 + 两份日志实证）—— ①**每局开局第一场战斗必炸一次**（`Context: After player turn start. Local: 1080177945. Remote: 2533528984.`）：同一只 `SHRINKER_BEETLE`，客人身上是 `RITUAL_POWER`、房主是 `MONARCHS_GAZE_POWER`（= 恐惧模式「储君的支配」）⇒ 支配解析出了不同东西。根因：v0.2.776 的 `DreadDominionSync` 是拿**本机本幕的 Boss 槽位**解析支配的，而客人这一刻槽位还是原版（`CEREMONIAL_BEAST_BOSS`，仪式兽）⇒ 拿到仪式兽的支配（`CEREMONIAL_BEAST_DOMINION` → RITUAL）；房主按替换后的 `REGENT_BOMBARD` 解析 ⇒ `REGENT_DOMINION` → MONARCHS_GAZE。现在客人端**只认房主下发的「每场最终遭遇」**（`BossPicksSync.HostSaysVanilla / HostBossOf` → `BossKeys.EncounterOf(key).GetDominion()`），房主结论还没到就**什么都不挂**（宁可不挂也不猜）。②**时机**：给游戏的唯一出口 `RunState.SetDominion` 挂后缀（反编译确认它只是 `MutableClone()` + 赋值、不触发钩子）⇒ 游戏一设完支配就立刻按房主结论纠正，**早于本幕第一场战斗**；另加「她上场（`FireCombatStart`）之前再核对一次」与每秒心跳重试。③**只认房主消息**：客人给客人的上报里 payload/settings/bosses 全空，以前照样走完整流程（日志实证 `已收到房主(NetId=76561198830124068)` —— 那是**另一位客人**）⇒ 现在空消息直接忽略。④**空载荷不再抹掉已知结论**：房主是逐步广播的（每场最终遭遇 0 条 → 1 条 → 2 条 → 3 条），以前每来一次就先 `Clear()` ⇒ 收到过一条空的就把房主刚说的话抹掉；现在三个字段各自「有内容才覆盖」。）

### v0.2.779 

2026-11-19 (**A 版观者实测三条**（用户 2026-11-19）—— ①**「特殊效果跨战斗保留了：测完红蓝观再测预见观，预见观的特殊效果变成了红蓝观的」**：日志实证（15:32:49 / 15:34:20，那一场是 `WATCHER_FORESIGHT_BOSS` + `BossOmnisciencePower`）`节制：她身上原来没有这层 buff ⇒ 已补挂` → `节制：满 12 张 ⇒ 结束她的回合`。根因：v0.2.769 给 `BossTemperancePower.NoteCardPlayedAsync` 加的「她身上没这层 buff 就自己补挂」对**每一只** Boss 都生效（`PlayOne` 不分 Boss 都会调它）⇒ 任何 Boss 打出第一张牌都会被塞上「节制」。现在 `IsTemperanceBoss(boss)`（= `Def.SpecialPowerForAct(act) == typeof(BossTemperancePower)`）把关，`NoteCardPlayedAsync` 与 `AfterCardPlayed` 的玩家侧分支都先问它。②**「冥想的捞牌没有具体点名，导致捞的牌错了（第 3 回合该捞暴怒+、第 10 回合该捞内心宁静）」**：剧本本来就写了 `ScriptCard.ReturnsToHand`，但选择器的「点名」只认烧/丢 ⇒ 冥想拿到空名单、退化成兜底（日志 15:27:47：`发起者=WATCHER_MEDITATE 的点名：0 张按实例 + 0 张按牌名 + 其余 1 张兜底；名单 0 条` ⇒ 捞到了 PURITY）。新增 `BossCardSelector.TakeReturnsToHand`：发起选择的那张牌在剧本里点了 `ReturnsToHand` 时，按类型精确捞那几张（且不再走「一定捞点穴」的老兜底）。③**「第十一回合净化只烧掉了一张牌」**：数据其实 5/5、特效 4/5（日志 `净化+：烧牌特效 4/5 张`），但 5 张在同一帧一起「瞬间烧完」⇒ 看着像只烧了一张。现在卡面仍然**当场搬出面板**（否则回合收尾重建手牌会拆掉它），只是特效播放按 **90ms 一张**错开。⚠ 仍待确认：**「有的牌打出去位置往左上偏」**（用户报的另一条）—— 需要具体是哪几张牌。）

### v0.2.778 

2026-11-19 (**三次净化+ 都要有烧牌特效**（用户 2026-11-19：「现在只有最后一次有」）—— 根因是**时机**：`BossPurify` 以前「等 300ms → 逐张播特效 + 挪数据」，而第 6 / 第 8 回合的净化+ 正好是那一回合的**最后一张牌** ⇒ 300ms 一过她这回合已经收尾、手牌栏跟着重建，那几张牌的格子没了 ⇒ `BossHandPanel.PlayExhaustFx` 找不到格子、静默什么都不做（第 11 回合那次后面还有一张「警惕」，300ms 落在回合中途，所以只有它看得到）。现在**先把卡面搬到 HUD 层、当场把烧牌特效点着**（`PlayExhaustFx` 会把 NCard 从面板里摘走 ⇒ 之后再重建手牌也拆不掉），等 300ms 才走数据 `CardCmd.Exhaust`。顺带给「手牌栏里没有这一格」补一行日志（以前是静默 return，这种问题日志里一点痕迹都没有）。）

### v0.2.777 

2026-11-19 (**新增提示 buff「不惧妖邪」（红蓝观第 12 回合起的循环回合）**（用户 2026-11-19）—— 用户原话：「新增一个 buff，也叫不惧妖邪，也用不惧妖邪的图标，在且只在红蓝观 12 及之后的重复回合出现，描述为：如果你在本回合打出攻击牌，观者会改为使用不惧妖邪进行无限。和原本那个第 4 回合出现的不惧妖邪是不同的 buff，不过都是起到一个提醒玩家的作用」。新增 `Powers.WatcherFearNoEvilHintPower`（`PowerStackType.Single`、图标沿用 `watcher_fear_no_evil.png`、不参与任何结算），由 `PrepareTurn` 在 `TurnNumber >= 脚本的 SwitchFromTurn`（=12）时挂上、她自己回合结束时摘掉（下一轮按回合号重新决定）。为了避免状态栏出现**两个一模一样的图标**，旧的 `WatcherFearNoEvilAimPower` 在第 12 回合起改为 `IsVisibleInternal => false`（**只是不显示，power 照旧挂着** —— 平静判定还要读它的 `PlayerAttacked`）。16 种语言标题用各自的观者卡名（不惧妖邪 / 不懼妖邪 / Fear No Evil / 妖邪を恐れず / 요사 불구 / …）+ 说明文案。）

### v0.2.776 

2026-11-19 (**联机不同步（第二位玩家，独立的一次）：恐惧模式的「本幕支配」两端用的是各自本地的 Boss 遭遇** —— 玩家日志 `godot (12).log`：客人 14004/14005 `Applying dominion because act 0 entered` → `Setting dominion to DOMINION.CEREMONIAL_BEAST_DOMINION`（**原版** Crab 的支配），而同一次运行到 14419/14420（不同步断线、重连之后）才变成 `DOMINION.REGENT_DOMINION`（我们的）。证据在同一场普通战斗的校验和转储里：客人那只 `MONSTER.SHRINKER_BEETLE` 只有原版 `RITUAL_POWER`，房主那只多了 `MONARCHS_GAZE_POWER`（我们的「储君的支配」）⇒ 战斗第一个 `After player turn start` 就 `Local: 864895115 / Remote: 1856624699` 分叉。根因：游戏的 `DreadRunModel.ApplyCurrentDominion()` 取 `Act.BossEncounter ?? SecondBossEncounter` 的 `GetDominion()`，而 BossEncounter 是**各端本地替换**的（客人等房主广播；这位玩家跑的是 v0.2.745，广播还因 BaseLib 看不见 BossPicksMessage 而整条失效）⇒ 客人整幕拿到原版 Boss 的支配。现在新增 `Core.DreadDominionSync.EnsureCurrentAct()`：在**收到房主结论**（`BossPicksSync`）与**我们改完本幕遭遇**（`BossSlotReplacer`）之后各对齐一次，若 `RunState.Dominion` 与「按最终遭遇算出来的」类型不同，就用游戏同一个出口 `RunState.SetDominion(...)` 重设（反编译确认它只是 MutableClone + 赋值、不触发钩子，重复调用安全）。顺带给「储君的支配：拿不到敌人列表 / 敌人列表为空」补上日志（以前是静默 return，日志里分不清是没进方法还是目标为空）。）

### v0.2.775 

2026-11-19 (**净化+ 只烧掉 1/5 张**（2026-11-19 15:05:01 日志实证：`净化+：按剧本点名消耗了 1/5 张（StrikeWatcher×4,DefendWatcher）`，同一场的 5/5 两次正常）—— 根因是**时序**：以前「等 300ms 再逐张 `Find`」，而这 300ms + 每张的换堆动画一旦跨过她的回合边界，下一回合的 `BuildHand` 会把旧手牌整批清掉（换成新实例）⇒ 后面几张再也找不到。现在**先把她点名的牌全部抓成实参**（那一刻手牌还是剧本那一批、卡面节点也在），等 300ms 之后再逐张烧。顺带按「未被占用」去重（`Find` 每次只返回同类型第一张，写「防御×3」时会拿到同一个实例三次 ⇒ 实际只烧 1 张）。）

### v0.2.774 

2026-11-19 (**红蓝观第 12 回合两条（用户 2026-11-19 截图）** —— ①「**我不打攻击牌就已经变成用不惧妖邪了**」：`SwitchRequested` / `_switchLocked` 这两个状态**从来不按轮清零**（v0.2.769 当年怕「清早了」，只清了「她回合开始时」那一次），于是第 11 回合打过的攻击牌一路带进第 12 回合 ⇒ 一进第 12 回合 `PrepareTurn` 就按切换段造手牌。现在 `PrepareTurn`（玩家回合开始、`TurnAt` 之前）调 `BossScript.ResetSwitchForNewRound()` 把两者一起清掉：这一轮**真的**打出攻击牌时再由 `RebuildForSwitch` 当场切过去。②「**除了第一张暴怒，后面暴怒的伤害不对 / 少了强化和牺牲**」：剧本推演的「观者姿态」模型漏了「不惧妖邪」——它的判据就是「被锁玩家本回合打出过攻击牌 ⇒ 进平静」，而切换段正是因此才被切上来的 ⇒ 推演时它必然把她带进**平静**。以前推演一直以为她还在愤怒 ⇒ 后面的暴怒全按 ×2 画（截图里的 42），`ApplyWatcherStanceIntents` 又按 `inWrath=true` 把强化/牺牲摘掉了。现在四处一起补上「不惧妖邪 ⇒ 平静（×1）」：`BossScriptPlanner.plannedStanceMul` / 同一处的 `watcherCalm` 记账 / `BossIntentBuilder.StanceAfterPlay` / `StanceAfterEntry`。）

### v0.2.773 

2026-11-19 (**红蓝观：攻击牌当场换意图 + 净化烧牌有真特效**（用户 2026-11-19 实机反馈两条）—— ①**「第12回合及以后，玩家打出攻击牌不会替换当回合的意图，只替换下回合」**：日志（v0.2.772 那一场，14:50:55）实证根因**不在** `TurnAt` 的锁，而在**时序** —— 她那一回合的**手牌 + 计划**是 `PrepareTurn`（挂 `MonsterModel.RollMove`，由**玩家回合开始**那一刻触发）一次性算死的：`14:50:55 剧本第 12 回合: 打 [暴怒+,供奉,内心宁静 …]`（默认段）→ `14:50:57/59` 玩家才打出 `STRIKE_SILENT` → `14:51:01` 她照默认段打完 12 张（`PLAY … INNER_PEACE` ×2，**一张不惧妖邪都没有**），到第 13 回合（下一个 `PrepareTurn`）才变成切换段。所以现在两处一起改：(a) `BossScript.TurnAt` 的解锁条件从「`turn < SwitchFromTurn`」改成「`turn <= 1`」（新一场战斗）—— 原来那一版在同一帧里被 `TurnAt(11)` 这种问旧回合的调用清掉过（`14:50:53` 打过一次「已锁」、`14:51:00` 又打一次同样的日志 ⇒ 中间被清过）；(b) 新增 `CharBossMonster.RebuildForSwitch` + 帧回调消费者：玩家打出攻击牌那一刻（`BossTemperancePower.AfterCardPlayed`，日志实证这个钩子会派发）置标记，4Hz 内把**这一回合的手牌按切换段重建**（走 `PrepareTurn` 同一条路：循环进阶/进阶加成/能量汲取升级）、`Plan` 重排、意图与手牌栏重画 —— 只换 `Plan` 不够，因为默认段的手里根本没有「不惧妖邪」，`Matches` 会全判「不在手牌」。同回合只重建一次，且只在她还没开始出牌（`CurrentSide == Player`）时做。②**「净化+ 烧牌没有特效」**：反编译 `CardPileCmd.Add` / `GetTweenForCardsChangingPiles` 拿到两处根因 —— 以前先 `RemoveInternal(card, true)` 再 `CardPileCmd.Add`，而 `Add` 自己就会 `RemoveFromCurrentPile` 并播换堆动画，我们先摘一次把节点和动画一起废掉了；更要紧的是原版那条动画对**非本机玩家**的牌**整段跳过**（`if (!LocalContext.IsMe(card.Owner) && …) continue;`），而她的牌属于影子玩家 ⇒ 永远不会飞、也不会响烧牌音效。现在照原版 `PileType.Exhaust` 分支自己做三步：`NCardExhaustQuickVfx.Create(cardNode)` + `NDebugAudioManager.Instance?.Play(\"card_exhaust.mp3\")` + `PlayAnimation()`（新增 `BossHandPlayFx.Exhaust` / `BossHandPanel.PlayExhaustFx`：先把卡面从格子搬到 HUD 层再播，否则面板重建会把节点拆掉），数据改走原版 `CardCmd.Exhaust`（含 History + `AfterCardExhausted` 钩子）。）

### v0.2.772 

2026-11-19 (**修多人不同步的根：房主「每场最终遭遇」广播从来没发出去过**（玩家 2026-11-18 反馈不同步，两份日志实证）—— `mod_log` 里 `勾选同步：广播失败（客人将退回整张候补表）: The given key 'CharacterBosses.Core.BossPicksMessage' was not present in the dictionary.` 出现**上百次**，紧跟在 v0.2.749 那句 `已让 BaseLib 重新发现自定义消息（含 BossPicksMessage）` 之后 ⇒ 上一版「清 BaseLib 缓存 + 重新 Initialize」的修法根本无效：BaseLib 的 `ReflectionHelper.GetSubtypesInMods<ICustomMessage>()` 看不见我们这套「外壳 + `Impl111.dll.bin`」的**实现程序集**，重扫多少遍都还是那批类型。后果是双向全断：房主的结论送不出去、客人的观者身份也上报不进来（日志 `期待客人上报 1 份、已收到 0 份`）⇒ 客人退回「整张候补表 + 自己按种子重算」 ⇒ 本场 Boss 可能与房主不同 ⇒ 进战斗第一个 `After player turn start` 校验和就分叉（godot.log: `State divergence detected! Checksum with ID 238 for client 76561199786052416 doesn't match host's! Local: 2020120666. Remote: 2700723302.`）。现在改成**按 BaseLib 自己的算法把 BossPicksMessage 直接登记进它的两张表**（`CustomMessageToId` / `IdToCustomMessage`）：反编译实证 `Initialize()` 的 id = `FullName.ComputeBasicHash()`（MD5 → FNV-1a，与其它类型无关、两端必然相同），撞车才 +1 避让；全程反射、不编译期引用 BaseLib。只写这两张表、**不往 `_customMessages` 里加**，这样 BaseLib 之后被谁再调一次 `Initialize()` 也不会把我们改登记到 `hash+1`。原代码保留「先重扫、扫到了就什么都不做」这条快路。⚠ 仍待验证：两台机同一版本的实机联机日志（`勾选同步：房主已广播…` + 客人 `已收到房主结论`）。）

### v0.2.771 

2026-11-19 (**红蓝观四条（用户 2026-11-19 实机实测，每条都先用 mod_log.txt 证实根因才改）** —— ①**「节制」每张牌数两次** ⇒ 第二回合 6 张就 +2 力量、buff 上还看不到数字。日志铁证（14:15:43 / 14:16:07 / 14:16:43 / 14:17:05 四次触发逐张对账）：她那 3 个回合一共只打了 7 张牌就满 12，只有「PlayOne 记一次 + `PowerModel.AfterCardPlayed` 再记一次」才对得上 ⇒ 这个 Power 钩子在她身上**是会派发的**（v0.2.768 的判断是错的）。现在计数**只走** `NoteCardPlayedAsync` 一条路，`AfterCardPlayed` 只负责「真人玩家打出攻击牌」这个信号。计数数字不显示是另一个原因：反编译 `NPower.RefreshAmount` 只在 `StackType == Counter` 时画 `DisplayAmount`，而且只在收到 `DisplayAmountChanged` 事件时重画 ⇒ 补上 `InvokeDisplayAmountChanged()`（同 `IroncladRagePower` 的写法），恐惧模式的「时间扭曲」也补。②**第 4 回合不惧妖邪不进平静**：`WatcherFearNoEvilAimPower.AfterCardPlayed` 走的是 **Ω观专用**窄闸门（身上必须有「斋戒」）⇒ 红蓝观（身上是「节制」）永远在那里静默 return，`PlayerAttacked` 一辈子 false（v0.2.759 只放宽了 OnPlay 后缀那一半，记号的这一半忘了改）。现在两边都用放宽后的 `FearNoEvilBoss`，并把两种 return 各打一行日志。③**第 12 回合不换计划**：`SwitchRequested` 以前「取一次就清掉」，而 `TurnAt` 整局被 ~25 处调用 ⇒ 第一个调用者吃掉标记，真正造手牌的规划器读到 false，于是 `剧本第 12 回合` 永远是默认那 12 张（日志 14:05:21 / 14:10:36 铁证：切换日志打了、计划仍是暴怒+/供奉/内心宁静那套，整回合 0 张不惧妖邪）。现在「切换」对回合号**黏住**（`_switchLocked`），所有取计划的地方拿到同一套，回合号掉回 12 之前（新战斗）才解锁。④**剧本 T7/T8 从腰带抽打改回碰撞连击**（v0.2.769 的全局替换改错了：T7/T8 本来就是碰撞连击）；T2 的腰带抽打保留、T3 碰撞连击不动，T10（不出）与 T11 消耗名单里的腰带抽打**按用户确认保持**。另：`Core.PlayerAttackWatch` 那条 Harmony 挂在 `CardCmd.AutoPlay` 上，而真人从手里出牌并不走这个入口（反编译实证 AutoPlay 只覆盖自动打出）⇒ 现在由 Power 钩子补一份可靠来源；`PlayOne` 里也补了三条诊断日志（标记为真/为假/交给后缀）。）

### v0.2.770 

2026-11-19 (**塔罗愚：第 12 回合起显示滚石伤害**（用户 2026-11-19：「塔罗愚第 12 回合开始，他应该显示滚石的伤害，在水银沙漏那个位置」）—— 滚石（`RollingBoulder`）不属于任何一张牌的意图，所以和水银沙漏 / 抱抱先生一样画在**最左那一列**（`BossHandPanel.Extra` 的 `leftDamages`）：`Def.Key == FOOL_BOSS` 且 `TurnNumber >= 12` 时加一条「滚石（全体）」，数值取本体 `RollingBoulder` 卡面的 `Damage`（读不到就按 20 兜底）。）

### v0.2.769 

2026-11-19 (**红蓝观换削弱方式 + 计数/不惧妖邪/换计划三处再修**（用户 2026-11-19）—— ①**削弱改法**：第 1 回合的「暴怒+」与第 2 回合的「渐强」互换位置；**所有**「粉碎关节」换成「腰带抽打」（`WATCHER_SASH_WHIP`，新加 provider）；第 2 回合打腰带抽打、第 3 回合「碰撞连击」位置不变。②**节制仍不计数 / 没结束回合却加了力量**：计数改成 `PlayOne` 显式记账（上一版已改）+ 她身上若没挂上这层 buff 就**第一次记账时自己补挂**；结算顺序改成**先 `PlayerCmd.EndTurn(她的 Shadow)` 再给力量**——原来用的是 `self.Player`，而 Boss 生物的 `Player` 是 null ⇒ 回合从来没结束过、力量却照加。③**第 4 回合不惧妖邪**：`PlayerAttackWatch` 不再依赖 Harmony 参数名绑定（绑定不上就永远收不到），改成从 `object[] __args` 里自己捞 `CardModel`。④**第 12 回合不换计划**：她回合开始时把「玩家打过攻击牌」标记清掉 ⇒ 计划取用时永远是 false；现在不再清（取一次由 `TurnAt` 自己清）。）

### v0.2.768 

2026-11-18 (**红蓝观三条 + 预见观闸门**（用户 2026-11-18）—— ①**「节制」按她自己打牌计数**：`PowerModel.AfterCardPlayed` 在她这只 Boss 身上**根本不派发**（同一个根因也害得「不惧妖邪」那条进平静失效、第 12 回合换不了计划）⇒ 现在由 `CharBossMonster.PlayOne` 显式调用 `BossTemperancePower.NoteCardPlayedAsync` 记账。②**玩家打出攻击牌 ⇒ 她那记不惧妖邪进平静**：新增 `Core/PlayerAttackWatch`（Harmony 挂在出牌入口 `CardCmd.AutoPlay` 上，只看真人玩家的 Attack），`PlayOne` 打完不惧妖邪时若标记为真就 `EnterCalm`；她回合开始时清标记。③**第 12 回合换计划**：同一个标记顺便置位 `BossScript.SwitchRequested` ⇒ `TurnAt` 换成「不惧妖邪无限」那套。④**净化+ 真烧牌**：以前直接 `ExhaustPile.AddInternal`（没有动画），现在延迟 0.3 秒后走原版 `CardPileCmd.Add(…, PileType.Exhaust, …)`，有消耗动画。⑤另含 v0.2.767 的 `Core/ScryGate`：预见观让玩家预见 2 时全体 Boss 暂停出牌，直到玩家选完。）

### v0.2.766 

2026-11-18 (**红蓝观收尾：时间扭曲的钟 + 第 12 回合动态换计划** —— ①**塔1 时间扭曲的时钟 + 音效**：新增 `Core/TimeWarpFx` —— 音效 `STS_SFX_TimeWarp.ogg`（从 StS1 的 `desktop-1.0.jar` 的 `audio/sound/STS_SFX_TimeWarp_v2.ogg` 抽出，经 `pck_extras` 打进 PCK，走和 BossVoice 同一条 `AudioStreamOggVorbis` 管线）；钟则是 `TimeWarpClock`（Godot `Control` + `_Draw` 程序化绘制：半透明表盘 + 12 刻度 + 三根指针 1.4 秒转一圈并淡出，因为 StS1 那个钟本来就是程序化画的、jar 里没有贴图）。「节制」满 12 与恐惧模式的「时间扭曲」满 12 都会播。②**第 12 回合起「玩家打出攻击牌就切换」**：`BossScript` 新增 `SwitchTurn` / `SwitchFromTurn` / `SwitchRequested`，`TurnAt` 取回合计划时若「玩家刚打过攻击牌」就把这一回合换成切换段（不惧妖邪 ×4 那套无限）；置位点在她身上那层「节制」的 `AfterCardPlayed` 里（看到玩家方打出 Attack 就置位），取一次即清。）

### v0.2.765 

2026-11-18 (**预见观特殊效果又不触发** —— v0.2.762 把普通模式改成「你预见 2」时，只认 A 版的 `WatcherMod.WatcherCombatHelper.Scry(ctx, player, amount, source)`；而 **B 版（lamali）没有这个类**，它的预见入口是 `Watcher.Code.Commands.MultiplayerCmd.Scry(ctx, source, players, amount)` ⇒ B 版玩家拿不到入口，「预见 2」什么都不做 ⇒ 看起来就是特殊效果完全不触发。现在按**参数形状**自动认两版：p2 是 `Player` ⇒ A 版；p3 是 `IEnumerable<Player>` ⇒ B 版；日志会打 `通晓万物：让玩家预见 2（<类型>.<方法>，<程序集>）`，找不到入口也会明说。）

### v0.2.764 

2026-11-18 (**模组列表乱码修好了 + 净化+ 真消耗** —— ①**游戏内模组列表乱码**：`src/CharacterBosses.json` 里的中文在很久以前被**双重编码**成了乱码（UTF-8 解码出来是 `鐜╁瑙掕壊`），游戏读到的就是那串乱码（截图里的「整十●璤摸埈Boss / 榕终尔」）。现在用 `workshop/title.txt` + `workshop/description.txt`（这两个是对的）重建整份清单：`name = 玩家角色Boss (Characters Bosses)`、`author = 飞猪`，并**加上 UTF-8 BOM**（无 BOM 时某些读取方会按系统 ANSI 解码）。⚠ 工坊页面那条仍是旧的 `0.2.730` 且是上传时编码坏的（Steam 侧要重传才会变，见下）。②**「净化+」真的消耗**：新增 `Core/BossPurify` —— 她打完带 `ExhaustWith` 名单的那张净化+ 之后，按名单把 腐朽 / 改造现实 / 打击×4 / 防御 / 粉碎关节 / 碰撞连击 / 当头棒喝 / 冥想 / 义愤填膺 从**手牌 → 消耗堆**（手牌没有就依次找抽牌堆、弃牌堆），日志 `净化+：按剧本点名消耗了 N/M 张`。）

### v0.2.763 

2026-11-18 (**形态特效在 Boss 身上是反的**（用户 2026-11-18：「群蛇形态等形态牌的特效在 boss 身上是反的，之前统一修过，新版本更新后好像就都坏了」）—— `FormVfxFacingPatch.FaceLeft` 以前只看**节点自己**的 `scale.x < 0` 就当作「已经翻过」；而新版把 `NCreatureVisuals.FormVfxHolder` 挪进了被她镜像过的 `Body` 子树 ⇒ 特效**已经**被父级翻过一次，我们再翻一次正好抵消 ⇒ 看起来是反的。现在改成看**最终生效方向** `GlobalScale.X`（父级镜像也算进去）：净方向已朝左就什么都不做，净方向朝右才翻自己的 `scale.x`。这样无论原版把 Holder 放在 Body 里还是 Body 外结果都一致；4Hz 慢车道的 `EnsureFacing` 兜底用的是同一个函数，特效被重建也会自己纠正。）

### v0.2.762 

2026-11-18 (**预见观：迂回终于拦住 + 通晓万物削弱** —— ①**B 版迂回返手还是没拦住**：根因不在迂回那条补丁本身，而在**上一层** —— `BossOmnisciencePower.TryInstall` 里「找不到 `WatcherCombatHelper` 就 return」写在 `BossWeaveNoReturn.TryInstall()` **之前**，而 B 版观者根本没有这个类 ⇒ 迂回那条**从来没被调用过**（v0.2.756~0.2.760 的日志里一次「迂回：…」都没有，就是证据）。现在把调用提到最前面。②**通晓万物削弱（普通）**：改成「每当观者触发先见之明时，**你预见 2**」——对她锁定的那位玩家跑一次观者自己的 `WatcherCombatHelper.Scry(ctx, player, 2, null)`。③**恐惧模式改成沿用之前普通模式的效果**：每当她触发先见之明时，把该玩家抽牌堆里的一张随机牌丢弃（不再走「任何预见都算」那条 Scry 钩子）。④16 语言文案同步（`BOSS_OMNISCIENCE_POWER.description` / `.description_dread`、`WATCHER_FORESIGHT_BOSS.omniscience` / `.omniscience_dread`）。⑤同时带上 v0.2.761 的红蓝观五条与泪滴吊坠盒改动。）

### v0.2.760 

2026-11-18 (**预见观（B 版）两条** —— ①**迂回的意图错误地多了一条「强化」**：迂回（`WATCHER_WEAVE` / B 版 `WATCHER-WEAVE`）是 0 费攻击、卡面只有「造成伤害 + 预见时回手」，但它身上某个变量被 `ClassifyPowers` 认成增益 ⇒ 加进 `NoBuffIntentCards` 点名排除。②**B 版迂回返手依然没拦住**（v0.2.758 那条其实**一次都没装上**）：B 版根本没有 `WatcherCombatHelper`，而 `TryInstall` 先判「找不到 helper 就 return」⇒ 后面那条 `Weave.AfterScryed` 永远走不到（日志里 v0.2.758 完全没有「迂回：已拦下…」这一行就是证据）。现在把 `Weave.AfterScryed` 的安装提到最前面、不依赖 helper；再补一层兜底：`WatcherCards.Weave` 没解析出来时，直接在已加载的观者程序集里找「名字以 `Weave` 结尾、带 `AfterScryed` 的卡片类型」再挂。命中与否都会打日志（`迂回：B 版（卡片自己的 AfterScryed）已拦下…` / `… 上没有 AfterScryed`）。）

### v0.2.759 

2026-11-18 (**红蓝观五条 + 泪滴吊坠盒 + 不惧妖邪 + buff 图标** —— ①**进阶 9 不再升级粉碎关节**（用户削弱）。②**她所有暴怒都没升级**：剧本里 `Play(Eruption)` 没带 `Upgraded` ⇒ 现在全部走 `upgraded: true`（暴怒+）。③**腾越那一回合只出了腾越**：把「第 8 回合」与「腾越给的额外回合」**合并成一个回合**（照塔罗愚「倒吊人额外回合」的先例），能量 3 → 5，牌序 = 奇迹、腾越、内心宁静、碰撞连击、净化+（消耗 打击×4·防御）+ 被消耗的 5 张 + 不惧妖邪（没费不弃）。④**她身上没有特殊效果「节制」**：计数借用了 `Power` 的 `Amount`，而 `PowerCmd.ModifyAmount` 把数量改成 0 时原版 `ShouldRemoveDueToAmount()` 会把**整条 power 移除**（v0.2.541 引雷针踩过同一个坑）⇒ 第一次满 12 清零后她就永久失去「节制」，之后自然也不再 +2 力量。现在 `Amount` 不动、计数放实例字段 `_played`，`DisplayAmount` 读它；恐惧模式的「时间扭曲」同样处理。⑤**净化没有升级**：剧本的 净化 全部改成升级版（净化+）。⑥**复读的回合错误**：原本把「玩家打出攻击牌后切换」那一段也塞进 `Turns`，把循环编号顶错一格 ⇒ 现在 `Turns` 只有 1~12（第 12 回合 = 默认无限，`LoopFrom=12/LoopLength=1`），切换那套挪成 `SwitchPlan()` 备用。⑦**泪滴吊坠盒没触发**：它的钩子挂在玩家身位、影子玩家收不到战斗开始广播 ⇒ 她第 1 个回合开始时自己补一次「进入平静」（按当前 provider 解析 `TeardropLocket`）。⑧**不惧妖邪不进平静**：那条结算的闸门原来只认 Ω观（身上有斋戒）⇒ 放宽成「Ω观 或 红蓝观（身上有节制）」（欧米伽/点穴那套仍只认 Ω观，免得她意图里凭空多出欧米伽伤害）。⑨**所有本模组 buff 触发时显示红色 NOPE**：原版触发特效读 `ResolvedBigIconPath`，我们的 Power 没有对应 png ⇒ 补一条兜底：本模组所有 Power 用「好奇」的大图（与特殊效果面板同一张）。⑩**不惧妖邪 / 眼部攻击 buff 获得时没有特效音效**：两个 power 的 `ShouldPlayVfx` 原来恒为 false ⇒ 改成「在她身上才播」。）

### v0.2.758 

2026-11-18 (**预见观三条实机修复（B 版观者）** —— ①**B 版的迂回回手没拦住**：B 版把「预见就回手」写在卡片自己身上（`Watcher.Code.Cards.Uncommon.Weave.AfterScryed`），而不是 A 版那样写在 `WatcherCombatHelper.OnScry` 里 ⇒ 之前拦的入口根本不对。现在 `BossWeaveNoReturn` 额外前缀 `Weave.AfterScryed`（是她这个影子玩家就整条跳过），并把「这是不是迂回」改成按**观者 mod 当前 provider 解析出的类型**判定（A 版 `WatcherMod.WatcherWeave` / B 版 `Watcher.Code.Cards.Uncommon.Weave`），类名兜底也放宽到 `*Weave`。②**她的最大生命值不对（161 应该是 192）**：`HpForAct` 在「变体生效」时返回的是**角色的真实基础血量**（观者 72，逐幕加成按 0 算），而四只观者变体还各自写着 `HpBonus = -31`（那是 V0.2.4xx 时代「主定义 103 里烤进了布质果实」的补丁）⇒ 72 + 涅奥 120 − 31 = 161。现在 预见观 / 印牌观 / 保留观 / Ω观 全部改成 `HpBonus = 0`（= 72 + 120 = 192），**红蓝观有布质果实 ⇒ +31**（103 + 120）。③**碰撞连击的段数只数存活玩家**：以前按 `RealPlayers` 的全部人数算，死掉的玩家也被算成一段 ⇒ 现在只数 `Creature.IsAlive` 的真人玩家（单人仍显示成一条 10，多人按存活人数出多段）。）

### v0.2.757 

2026-11-18 (**「红蓝观」第二步：Boss 落地（可打）** —— ①`BossDefs.Watcher.ExtraVariants[2]` 新增 `WATCHER_RUSHDOWN` 变体：遗物 至纯之水/涅奥的祝福/泪滴吊坠盒/符文金字塔/音叉/布质果实/坚固钳子（布质果实 +31 ⇒ 血量沿用主定义 103）、`BlocksVanillaDraw`、恐惧模式升级暴怒。②`BossKeys` 注册 `new(`WATCHER_RUSHDOWN`, 6, 2, 3, WatcherRushdownBoss)` + 设计名「红蓝观」+ `EncounterOf`；③新增 `Monsters.WatcherRushdownBoss`（TierAct=2、PinnedVariantKey）与 `Encounters.WatcherRushdownBossEncounter`（+调试用 Normal 版），进 TierEncounters 候选表与 `Add(6, 2, …)` 档位表；④`BossLineups.WatcherRushdownActive()` 按遭遇/阵容/钉死 key 判定（同一个 `Def.Key` 下的第六套设计，认不出就明确「不是红蓝观」）；⑤能力面板（循环 12 / 单体极强 / 群攻极强·集火 / 单体高压 3·4·9·12 / 群体高压 3·10·12 / 自身·单体）；⑥藏品 = 腾越（`WATCHER_RUSHDOWN` 分支）；⑦16 语言本地化：`WATCHER_RUSHDOWN_BOSS`（名字/出牌/节制/恐惧版）、`BOSS_TEMPERANCE_POWER`、`WATCHER_RUSHDOWN_BOSS_ENCOUNTER`（含死亡文案），描述末尾带「警告：观者会在第12回合开始无限。」。⑧剧本 `WatcherRushdownScript` 11 回合 + 第 12 回合起无限（默认段 + 玩家打出攻击牌后的切换段两套），所有净化+ 附魔灵魂之力。⑨特殊效果 power `BossTemperancePower`（计数 12 ⇒ 清零 + 她与友方 +2 力量 + 强制结束她的回合）与恐惧模式的 `BossTimeWarpPower`（独立计被锁定玩家出牌 ⇒ 终止该玩家回合），时钟特效/音效仍待接）

### v0.2.756 

2026-11-18 (**新增三层 3 号观者 Boss「红蓝观」第一步：地基 + ID 全部核对**（用户 2026-11-18 给了完整设计，内部名用「猛虎下山」的英文 ⇒ Key = `WATCHER_RUSHDOWN`）—— ① `WatcherCards` 补齐这一只要用而之前没定义过的 provider：猛虎下山 `WATCHER_RUSHDOWN`、粉碎关节 `WATCHER_CRUSH_JOINTS`、腾越 `WATCHER_VAULT`（模组中译「腾跃」）、净化 = 本体无色 `Purity`、腐朽 = 本体诅咒 `Decay`、泪滴吊坠盒 = 观者 `TEARDROP_LOCKET`、符文金字塔 / 音叉 / 布质果实 / 坚固钳子 = 本体同名遗物（编译已验证类型都存在）；其余（暴怒+ / 警惕 / 打击 / 防御 / 心灵堡垒 / 渐强 / 安宁 / 奇迹 / 义愤填膺 / 供奉 / 碰撞连击 / 冥想 / 不惧妖邪 / 内心宁静 / 改造现实 / 至纯之水）本来就有，卡名→id 全部从观者 pck 的 `zhs/cards.json`、`relics.json` 查出来、没有一个是猜的。② 新增 `docs/plans/boss-watcher-rushdown.md` 记录逐字设计 + ID 表 + 分阶段清单（下一步：BossDef 变体 / 遭遇 / 阵容 / 本地化 → 13 段剧本 → 「节制」计数 power 与塔1时间扭曲的时钟特效音效 → 恐惧模式的「时间扭曲」→ 全局 buff 触发图标修复））

### v0.2.755 

2026-11-18 (**特殊效果说明框的偏移改成「默认不偏移」**（用户 2026-11-18：「你这写法好唐啊，能不能让预见观之类的是默认，欧米伽观和神格观是特殊需要偏移的，你别把偏移方向搞反了」）—— `BossSpecialTip.UsesLongSpecialText` 以前是「按 `Def.Key == WATCHER_DIVINITY` 算长文本，再一行行排除印牌观 / 保留观 / 预见观」，观者每加一套设计就要补一行。现在反过来：**默认正常位置**，只按**特殊效果类型**点名三只需要上移的 —— 炼药猎 `BrewModPower`（调制，最长）、神格观 `BossDevotionPower`（虔信 4 行 + 神格 2 行）、Ω观 `WatcherFastingPower`（斋戒）。偏移量本身没动：`BossUiTuning.BrewSpecialY` 仍是默认 **−120 = 往上**，只有命中这三只才加）

### v0.2.754 

2026-11-18 (**预见观的特殊效果说明框回到正常高度**（用户 2026-11-18：「它的描述框偏高了，它很短不用特意调高，别动别的 boss」）—— 预见观和神格观共用 `Def.Key = WATCHER_DIVINITY`，于是白吃了神格观那条长文本的 −120 偏移（`BossUiTuning.BrewSpecialY`）；而「通晓万物」只有两行。`BossSpecialTip.UsesLongSpecialText` 里新增一条判据：**特殊效果类型是 `BossOmnisciencePower` 的（全模组只有预见观）一律用正常位置**；神格观 / Ω观 / 炼药猎等其它 boss 一个像素都没动）

### v0.2.753 

2026-11-18 (**预见观：被预见的牌不再显示在她手牌里**（用户 2026-11-18 更正：「我是说被预见丢弃的**其它牌**是直接从抽牌堆到弃牌堆，不需要展示在手牌里，这和猎人从手牌中弃牌的逻辑不一样，别动猎人的逻辑，只动预见观的」）—— 之前 0.2.752 只改了「迂回」，可日志里第 3 回合她的手牌是 `[THIRD_EYE,WEAVE,DEFEND,WEAVE,FORESIGHT,STRIKE]` 6 张：被天眼预见的「迂回/防御」也被造进手牌了（只是压暗），根因是 `BuildHand` 的循环对 `NotDrawn/DiscardInstead` 条目照样 `Hand.AddInternal`。现在新增 `ScriptCard.ScriedFromDraw`：这类条目**不造牌、不进手牌**，只把她**抽牌堆里那张真实实例** `RemoveInternal` + `DiscardPile.AddInternal`（抽牌堆→弃牌堆，和原版 `Scry` 的实际行为一致），规划器也不再把它当「不在手牌」的异常。只给预见观用；静默猎手那套 `DiscardInstead`（奇巧/杂技/必备工具，从手牌弃）一个字都没动）

### v0.2.752 

2026-11-18 (**迂回改成「根本回不去手牌」**（用户 2026-11-18：「被预见丢弃的牌是直接从抽牌堆到弃牌堆，不需要展示在手牌里」）—— v0.2.751 的做法还是「让它返手、再扔回弃牌堆」，手牌栏会闪一下多一张。现在改成：前缀挂 `WatcherCombatHelper.Scry` / `ScryAutoDiscard`，她预见时**先把弃牌堆里的迂回收起来**（`DiscardPile.RemoveInternal`）⇒ 原版那段 `Where(c is WatcherWeave)` 扫到 0 张、一张都进不了手牌；后缀包 Task，原版跑完（涅槃格挡照旧）再把它们放回弃牌堆。顺带核对：观者 `Scry` 的实现是「从抽牌堆取 → 选中那张先放 `PileType.Play` 再进 `Discard`」，本来就不经过 Hand ⇒ 全流程只有迂回这一处会污染她的手牌）

### v0.2.751 

2026-11-18 (**修 v0.2.750 的崩溃**：她打「天眼」时游戏直接死掉（日志停在 `Player … chose cards` 之后、无任何异常 ⇒ 进程级 StackOverflow）。根因是我拦「迂回返手」的写法：前缀挂 `WatcherCombatHelper.OnScry` 然后用反射把原方法调回来再包 Task —— 反射拿到的入口仍是被 Harmony 替换过的那个 ⇒ 无限递归。现在重写 `Powers/BossWeaveNoReturn`：**绝不再调原方法**，改成前缀挂公开入口 `Scry` / `ScryAutoDiscard`（都返回 `Task<List<CardModel>>`），前缀拍下「此刻她弃牌堆里的迂回实例」，后缀把返回的 Task 包一层，原版整段跑完（涅槃格挡照旧）之后用正规指令 `CardPileCmd.Add(…, PileType.Discard, …)` 把「刚被拉回手牌的那几个实例」放回弃牌堆；按实例判定 ⇒ 剧本自己放进手牌的那几张不误伤。v0.2.750 另外两条（碰撞连击按力量实时算、通晓万物改在她回合开始触发）保持不变）

### v0.2.750 

2026-11-18 (**预见观三条实机修复** —— ①**迂回不再自动返手**：反编译观者 `WatcherCombatHelper.OnScry` 的后半段就是「每当你预见，把弃牌堆里所有迂回塞回手牌」，她的剧本已逐张写好 ⇒ 新增 `Powers/BossWeaveNoReturn`：前缀挂 `OnScry`，只对她的影子玩家生效，按**实例**记下弃牌堆里现有的迂回，原版那一段跑完后把「刚被拉回手牌的」再丢回去（涅槃的格挡照旧保留、剧本自己放进手牌的那几张绝不误伤）。②**碰撞连击的预览少 1 点**（用户截图：卡面 11、预览 10）：它发的是 Raw 意图 ⇒ 牌面基础值 10 直接显示、力量（手里剑每 3 张攻击 +1）/虚弱/易伤全丢，改成 `LiveMultiAttackIntent` 走原版伤害管道实时算，段数仍是真人玩家数。③**通晓万物的触发时机**（用户：「应该在她回合开始时，不是玩家回合开始时」）：以前挂观者 `ForesightPower.BeforeHandDrawCompat`，那条钩子每次抽牌都会广播给所有 Power（原版 power 自己判 `player == Owner.Player`，我们没判）⇒ 真人玩家回合刚抽牌就被丢一张；现在改由 `BossHookBridge.FireSideTurnStart`（她自己的回合开始）显式触发一次，条件是「身上挂着通晓万物 + 已经打出过先见之明」，并按 Boss 回合号去重）

### v0.2.749 

2026-11-18 (**地图图标：一个图标只对应唯一一个 Boss** —— 按用户口径重排：涂毒猎 `silent_poison`、奇巧猎 `silent_mirror_image`（新从崩坠 jar 取出「镜像猎」那张，512×512 图标 + 轮廓，经 `tools/art/import_proj` 导入后打进 PCK）、刀猎 `silent_shiv`、黑球机 `defect_void`、物理机 `defect_bronze_orbs`、能力机 `defect_focus`、灵魂骨 `soul_bone`、养手骨 `osty_bone`、根除骨 `eradicate_bone`、神格观 `watcher_divinity`；其余（炼药猎 / 华丽猎 / 状态机 / 事件机 / Ω观 / 印牌观 / 保留观 / 预见观 / 铁甲战士 / 储君 / 愚者 / 亡灵其它骨 …）一律回到剪影占位 —— 以前炼药猎与涂毒猎共用 `silent_poison`、华丽猎与刀猎共用 `silent_shiv`、`defect_focus` 被基类 + 能力机 + 事件机 + 状态机共用、`watcher_divinity` 被 5 只观者共用，现在各归各家。另外把基类 `DefectBossEncounter` 的覆写删掉（能力机自己覆写），`watcher_wrath`（一代愤怒观那张）已取出备好，等红蓝观（猛虎下山）上线直接接上）

### v0.2.748 

2026-11-17 (**联机同步自愈补强** —— 顺着 v0.2.747：房主那条心跳除了「客人上报没到齐」，现在还覆盖「去重标记是空的」这种情况（有新客人刚上报 / 上次广播失败 ⇒ 内容还等着发出去），否则晚进的客人会一直等不到广播。对应玩家反馈的那个联机 mod（工坊 id 3747570046，内部名 `SteamRandomMatchMod`、「Steam 随机匹配」，工坊标题「联机大厅与多功能联机内容」）自带大厅/模组同步/PvP 框架，开局会先跑它自己的握手 ⇒ 我们那条开局只发一次的 BaseLib 自定义消息很容易落进空窗。 )

### v0.2.747 

2026-11-17 (**联机同步自愈：心跳 + 请求重发**（工坊玩家「季节」反馈：装了「联机大厅与多功能联机内容」后，**第一幕得重开房间才能同步**）—— 根因推断（代码侧可证）：那条 BaseLib 自定义消息在开局只发一次，且 `_lastSent` /  _lastCapsSent 有「内容没变就不重发」的去重 ⇒ 只要那次发送落在联机 mod 还在重建大厅/提前拉房间的窗口里被丢掉，就会**永久**停在丢包状态，只能重开房间。现在：①客人没收到房主结论时，每 3 秒带 `hello` 重发上报；②房主收到 `hello` 就把去重标记清掉、立刻重播；③房主在客人上报没到齐时每 5 秒强制重播一次（晚进客人也能拿到）；④新 `BossPicksSync.Tick()` 挂在帧驱动里（每秒一次，极便宜）。 )

### v0.2.746 

2026-11-17 (**新增一层 1 号观者 Boss「预见观」**（用户设计）—— 观者角色第五套设计，挂在 `BossDefs.Watcher.ExtraVariants[0]`：8 回合剧本 + **6~8 循环**（`WatcherForesightScript`），逐回合能量 2/2/2/4/2/2/4/2（第 4、7 回合「化体为空」退出平静 +2）；遗物 至纯之水 / 涅奥的祝福 / **美琅脂 MELANGE** / **手里剑**；特殊效果「通晓万物」（`BossOmnisciencePower`）：她触发**先见之明**时（恐惧模式 = 任何**预见**，走 `WatcherCombatHelper.Scry`）从被锁定玩家的**抽牌堆**随手弃 1 张；「弃 X」全部按「她在抽牌堆里预见掉的牌」实现（`NotDrawn + DiscardInstead`，不进手牌也不打出）；进阶 8 升 涅槃·心灵堡垒、进阶 9 升 碰撞连击·斩破命运、恐惧模式升级「迂回」；**碰撞连击**段数按**真人玩家数**算（召唤物不算）并正确显示 `7×N`；藏品 = 通晓万物；能力面板 循环 6~8 / 单体强 / 群攻集火 / 单体高压 4、7 / 群体高压 无 / 单体；16 语言本地化；一层候补表按观者闸门追加 `WATCHER_FORESIGHT`。 )

### v0.2.745 

2026-11-17 (**修 v0.2.732 留下的致命回归：`BossDefs.PlaceholderCharacterType` 在自己赋值给自己 ⇒ 0.113 构建里是 null**（用户截图：恐惧模式愚者 557 血站着不动、意图全空、地图图标占位）—— 日志铁证：`shadow player setup failed: NullReferenceException at ModelDb.GetId(Type)` + `PrepareTurn failed`（同一个栈：`ShadowPlayerFactory.Attach`）⇒ 角色类型是 null ⇒ 影子玩家建不起来 ⇒ 这回合的计划根本算不出来 ⇒ Boss 全程不出手、没有意图、立绘/徽章退回占位。改成 `typeof(MegaCrit.Sts2.Core.Models.Characters.Fool)`（0.113 才有愚者角色）。顺带 `BossCombatPreloadPatch.DefFor` 补上角色下标 **5=FOOL_BOSS / 6=WATCHER_DIVINITY**（以前只认 0~4 ⇒ 愚者与观者的立绘/徽章一条都不预载）。 )

### v0.2.744 

2026-11-17 (**薄荷尖塔兼容：补上「故障机器人的支配」的伤害**（用户）—— 反编译 MintySpire2 1.3.1 的 `SummedIncomingDamageRender.CalculateIncomingDamage`：它只累加 ① 敌人 `NextMove.Intents` 里的攻击意图、② 玩家身上的 `DisintegrationPower` / `ConstrictPower`、③ **TheInsatiable 那一个**硬编码的支配 power（`CompatHelper.GetInsatiableDominionPower` → `creature.GetPower(InsatiableDominionPowerId)`）⇒ 我们的 `DefectDominionPower` 不在名单里，血条右边那个「←N」就漏了它。做法：本模组早就有的 `MintySpireDamagePreviewCompat`（postfix 那个 private static 方法）里加一笔 `DreadDominionDamage`：只对真人玩家、且身上真有 `DefectDominionPower` 时，按 `3 × 存活敌人数` 补进返回值；数字与支配本体共用新加的 `DefectDominionPower.DamageFor(enemies)`，不会出现两处算法打架。只加算术、不碰任何游戏状态。 )

### v0.2.743 

2026-11-17 (**恐惧模式两处支配修正（用户实测）** —— ①**补齐 `DREAD.FOOL.description`**（16 语言）：之前愚者的支配在能力提示框里显示成生 key（截图：`DREAD.FOOL.description`）。②**静默猎手的支配分难度**：普通战保持 −1 力量 / +1 虚弱，**精英战改为 −2 力量 / +2 虚弱**；同时修「精英战完全无效果」—— `AfterRoomEntered` 那一刻精英房的 `CombatState.Players` 还没就绪，现在加一条兜底：玩家第一个回合开始（`AfterSideTurnStart`）时若这场还没结算过就补上，`Applied` 集合保证每场只给一次；精英判定走 `combatState.RunState.CurrentRoom.RoomType == Elite`。 )

### v0.2.742 

2026-11-17 (**增强恐惧模式下观者的支配：虔信 2 → 3 层**（用户）：`WatcherDevotionDominion` 里每位玩家获得的 `WatcherFaithPower` 由 2 层改为 3 层（`FaithStacks` 常量），即他们每人回合开始时观者收 3 层真言；16 语言的 `DREAD.WATCHER.description` 同步改成 3 层 / 3 层真言。 )

### v0.2.741 

2026-11-17 (**回滚到 v0.2.738 的代码基线（纯回滚版，不动任何逻辑）** —— 用户澄清：「预览侧有问题，实际上进神格是能打出因为加费才能打出的牌的（能正常打出，但预览没显示能打出）」。⇒ v0.2.739（姿态倍率 → 重规划）方向错、v0.2.740（把玩家回合里到账的神格能量记进 `NextTurnEnergyBonus`）也会在「原版 Divinity 已经把 +3 加进 `pcs.Energy`、而她实际也确实打出了那张牌」的前提下**双份记账** ⇒ 两处代码全部回滚：`BossFrameTickPatch.cs` 与 `BossDevotionPower.cs` 都恢复到 v0.2.738 原样，其它一个字没动。下一步按实测日志（`虔信：真言满 10 ⇒ … 能量 X → Y` / `进神格多 3 点能量 ⇒ 按当前能量 Z 整块重规划第 N 回合` / `剧本第 N 回合: 打 [...] 跳过 [...]`）定位预览为什么不跟着变，不猜着改。 )

### v0.2.740 

2026-11-17 (**撤回 v0.2.739 那套「姿态倍率→重规划」，改对方向：神格「加费」** —— 用户澄清「我指的是进神格的加费导致的意图变化，而不是增伤」。v0.2.739 盯的是姿态倍率（增伤侧）且在玩家侧做整块重规划，方向不对、还有动到好逻辑的风险 ⇒ **整个 `RefreshIntentsIfStanceChanged` 已回滚**（BossFrameTickPatch 恢复 v0.2.738 原样）。真正的问题在 `BossDevotionPower.ResolveDivinityAsync`：神格的 +3 能量如果是在**玩家回合**里到账的（玩家每打一张牌 +1 真言，满 10 触发 —— 最常见路径），她的回合开始时 `PrepareTurn` 会用 `pcs.Energy = max(0, energy) + plasma + charged + delayed` **整个覆盖**掉，这笔能量就静默丢失（她不会多打那张牌，意图自然也不变）。现在按回合分派：她自己回合里攒满 ⇒ 照旧 `ReplanForExtraEnergy`（当场重规划+刷意图）；玩家回合里攒满 ⇒ 记进 `NextTurnEnergyBonus`（`BossDefs` 注释里那条一直没人写的通道），下回合 `PrepareTurn` 真正进预算 ⇒ 她真的会多打一张，意图随之变化，日志打出实得点数。 )

### v0.2.739 

2026-11-17 (**修：神格观进神格后意图不刷新**（用户实测）—— 观者的姿态倍率（神格 ×3 / 愤怒 ×2）不在上面那套「能力指纹」的覆盖里：B 版那份挂在玩家模型上（`SpireField&lt;PlayerCombatState, WatcherStanceModel&gt;`），A 版就算在 `Creature.Powers` 里、**进神格的时机也在她自己回合中**，而 `RefreshIntentsIfPowersChanged` 有「不在玩家侧就不打标」的闸门、并且在打标之前就把新指纹写回去了 ⇒ 这次变化被彻底吞掉。现在新增 `RefreshIntentsIfStanceChanged`（每帧比对 `StanceMultiplierOf`）：一变就记「待重规划」，她回合里**只重算数字**（那一刻 `boss.Plan` 就是她的出牌队列，重规划会改掉后面要打的牌；数字侧靠 `ScaleToPlannedStance` 退回 `PlannedStanceAt` 也是准的），到玩家侧再按**当前姿态重规划一次**（`PlannedPlay.StanceMul` 是 init-only，只有重规划才能换成新倍率；神格的 +3 能量顺带进预算）⇒ 打 `IntentsDirty` 由既有消费方重算 + 重画面板。 )

### v0.2.738 

2026-11-17 (**确立基础第三轮：「保留手牌」要算、金字塔/计划妥当不算** —— 反编译发现三者引擎层面**一模一样**：`RetainHandPower`（保留手牌，`PowerStackType.Counter`，自己回合结束 −1 层）和 `RunicPyramid`（金字塔）、`WellLaidPlansPower`（计划妥当）全都是 `ShouldFlush(player) => false`（**不冲洗手牌**），被留住的牌身上没有任何保留标记 ⇒ v0.2.737 只按 `CardModel.ShouldRetainThisTurn` 过滤时把「保留手牌」的牌也一起排掉了（用户：「现在保留手牌 buff 保留的牌又不会费用增加了」）。现在判据追加一条：**持有者身上有 `RetainHandPower` ⇒ 手牌一律算被保留**（类型名按字符串比，跨版本安全）；金字塔 / 计划妥当没有这层 power ⇒ 仍然不算。真正关键字的保留与单回合保留标记照旧。 )

### v0.2.737 

2026-11-17 (**确立基础两条订正（用户实测）** —— ①**「不丢弃」不等于「保留」**：金字塔（`RunicPyramid`）和计划妥当（`WellLaidPlansPower`）在 0.111 的实现是 `ShouldFlush(player) => false`（**干脆不冲洗手牌**），于是整份手牌都会作为「留下来的牌」出现在 `Hook.AfterFlush` 的 `retainedCards` 里。上一版主路直接拿这份名单加费 ⇒ 这两件东西留住的牌也被涨费。现在 `AfterFlush` 这条路也先按 `CardModel.ShouldRetainThisTurn`（= `CardKeyword.Retain` 关键字 或 `GiveSingleTurnRetain()` 打的单回合保留标记）过一遍，**只有真正被保留的牌**才加费；日志会打出「留在手牌 N 张，其中真正被保留 M 张（K 张只是没被弃掉 ⇒ 不算）」。②**力量惩罚改回每张 −1**（v0.2.734 按文案改成 −2，现按用户要求回落），16 语言正文同步改成「失去 1 点力量」。 )

### v0.2.736 

2026-11-17 (**观者全家死亡提示重写 + 保留观无耗能牌不再扣力量** —— ①四条死亡提示按用户选稿重写（16 语言），金色高亮统一成**角色名观者**（与其它 5 个角色一致）、`{character}` 后统一加空格：神格观「在观者踏入神格的一瞬被一并超度」/ Ω观默认「未能走出观者的阿尔法、贝塔与欧米伽」/ 印牌观「**生成的**每一张牌都被观者改写成了对付自己的武器」/ 保留观「手牌被观者一张张留到了死」；不再四条全用碾碎、也不再拿「斋戒」这种纯效果名当死因。②**Ω观点穴特判**：新增 `Patches/WatcherOmegaLossPatch`（postfix `EncounterModel.GetLossMessageFor`）+ 新键 `WATCHER_OMEGA_BOSS_ENCOUNTER.loss_pressure`（16 语言）—— 点穴那一下把玩家打死时 `WatcherOmegaMechanics` 记下角色模型，死亡提示就换成「每一寸都被观者的点穴点得爆开」（多人按角色引用比对，只改那一位）。③**保留观确立基础**：没有耗能的牌（打不出的状态/诅咒，例如悲伤、睡眠不佳）与 X 费牌一律跳过，不再给它们加费、也就不会白扣她的力量（`IsCostless`：Unplayable 关键字 / CostsX / 解析耗能为负）。④删掉死键 `WATCHER_PRINT_BOSS_ENCOUNTER.title/.loss`（v0.2.713 改名的残留，代码里已无对应遭遇类）。 )

### v0.2.735 

2026-11-17 (**保留观四条（用户实测）** —— ①**修特殊效果不触发**：「保留手牌」这类 buff 留住的牌身上没有 `CardKeyword.Retain`（它走 `CardCmd.ApplySingleTurnRetain` ⇒ `CardModel.ShouldRetainThisTurn`），旧实现只认关键字 ⇒ 这类牌一次都没加费。现在主路改走 `AbstractModel.AfterFlush(…, retainedCards)`（`Hook.AfterFlush` 第 5 个参数就是 `CombatManager.FlushPlayerHand` 现算的 `cardsToRetain`，权威名单，关键字/buff/单回合保留全在里面），兜底仍保留 `BeforeSideTurnEnd` 扫手牌（改用 `ShouldRetainThisTurn`），两条靠「玩家 + `PlayerCombatState.TurnNumber` + 卡实例」记账去重、顺序无关。②**修能力面板标题**：`BossConfigModConfig` 的 monsterKey 映射兜底是 `SILENT_BOSS`，观者/愚者两个前缀一直没登记 ⇒ 说明框顶部全显示「静默猎手」；现在观者改读遭遇 `.title`（角色名「观者」，16 语言）、愚者用 `FOOL_BOSS`。③**补「保留观」能力面板**：循环 6~9 / 单体极强 / 群攻强 / 单体高压 3~6、8、9 / 群体高压 4、8 / 单体。④特殊效果描述去掉末尾那句「此效果只对被观者锁定的玩家生效」（16 语言 × 普通+恐惧 × monsters+powers 共 64 条）。 )

### v0.2.734 

2026-11-17 (**保留观三处订正（用户）** —— ①第 2 回合补「奇迹（保留不出）」、第 3 回合开头补打一张奇迹（0 费、打出 +1 能量 ⇒ 防御这才付得起：奇迹0 + 时之沙0 + 欺瞒现实1 + 改造现实1 + 防御1 = 3 = 基础 2 + 奇迹 1）；②进阶 8 改成升级 心灵堡垒 / 欺瞒现实，进阶 9 只升级 改造现实（去掉停顿/警惕/义愤填膺/旋转打击的升级）；③「确立基础」加力量变成 **每有一张牌的耗能被增加，观者失去 2 点力量**（原来每张 −1），16 语言正文同步改写；恐惧版特殊效果一字不动（去掉耗能限制、仍不扣力量）。 )

### v0.2.733 

2026-11-17 (**三条：新增二层 2 号观者 Boss 保留观 + Ω观恐惧版 + 养手骨恐惧版订正** —— ①**二层 2 号「保留观」**（设计 Key WATCHER_RETAIN，观者 mod）：特殊效果「确立基础」= 玩家回合结束时，被你保留的、耗能小于 2 的牌在本场战斗耗能 +1，然后观者按张数失去等量力量（恐惧版去掉耗能限制、不扣力量）；9 回合剧本 + 6～9 循环、逐回合能量 2/2/2/4/2/2/2/4/2（4、8 回合是义愤填膺退出平静 +2）、保留成长按用户逐格给的值写死（时之沙 4→2→0、旋转打击 7/7/11/15/19/19/19/23/27、坚韧 5/5/7/9/…/11，循环后继续涨）；遗物 至纯之水/涅奥的祝福/斗篷扣/佩尔的士兵/臂甲；进阶 8 升心灵堡垒·停顿·警惕，进阶 9 升改造现实·旋转打击；拦掉原版印牌（改造现实⇒惩恶、欺瞒现实⇒平安 由剧本自己给）；藏品 = 确立基础；16 语言 10 键。②**Ω观恐惧版**：斋戒去掉「欧米伽隔一回合触发一次」⇒ 恐惧模式每回合都触发（意图预览同步），16 语言文案重写首行。③**养手骨恐惧版**：第 5 回合普通模式恢复「来生 + 来生 + 死亡之舞」，恐惧模式才是「钙化+ + 来生 + 死亡之舞」（走运行期 Resolve，静态表不判恐惧），其它不变。 )

### v0.2.732 

2026-11-17 (**正式版 0.111 适配：双实现改成「0.111 正式 / 0.113 测试」** —— 官方把 v0.108.0–v0.111.0 合进主线 ⇒ 正式版 = 0.111.x、测试分支 = 0.113.x。实测（把参考程序集换成真正的 0.111.0 编一遍）发现：①0.111 里有 `OneForAllPower`（本更新新增的联机牌「一心化万」）⇒ 旧 loader 的 beta 判定会把新正式版误判成 beta，去加载针对 0.113 编译的实现，进游戏就炸；②0.111 与 0.113 的 API 有三处分叉：`MultiplayerScalingModel`（0.111 在 `Models.Singleton.*`、0.113 在 `Models.*`）、`RelicModel.IsAllowed` vs `IsAllowedInternal`、`CardSelectorStack`（0.113 才有）；③愚者/塔罗/恐惧整块类型只有 0.113 有。⇒ 本轮：构建目标从「107 / 111」改成「**111（正式，无愚者/恐惧） / 113（测试，含愚者/恐惧）**」，107 停止构建；源码里把愚者/恐惧/0.113 新 API 全部收进新符号 `STS2_113`（`STS2_111` 现在只表示「现代内容」）；`BossDefs.PlaceholderCharacterType` 按构建选占位角色（0.113 = 愚者、0.111 = `DeprecatedCharacter`）；loader 改认 0.113 独有的塔罗牌 `TheFool`/`Judgement` 判 beta，并按 beta 载 `Impl113`、否则载 `Impl111`；install-mod.bat 读 `release_info.json`：v0.113+ → 113，其余 → 111。两个目标都 0 错误，`tools/patch-111-split.ps1` 记录了全部条件编译改动。)

### v0.2.731 

2026-11-17 (**三条：防御误标 5 点 / 状态机警告 / 回合开始伤害意图结算即撤** —— ①用户：「状态机第二回合的防御有的时候会错误的显示 5 的攻势，但它既没有触发开信刀也没有生成状态牌触发烟囱」。根因：`CountSkillsBeforeInPlan` 在计划里**找不到这张牌**时会退回 `playsBeforeThisTurn`（本回合已排的张数），而重规划/牌打出后 `Plan` 收缩会让任意技能牌被凑成「第 3 张」⇒ 给根本没触发开信刀的防御挂上 5 点（所以是「有的时候」）。现在找不到就返回 -1，调用方**一律不猜**，只有真数得出序号且正好是 3 的倍数才画。②为状态机补警告：`DEFECT_BOSS.status`/`status_dread`（monsters 表）与 `BOSS_SYNC_POWER.description`/`description_dread`（powers 表，战斗中说明框读这一份）各追加一行「警告：「烟囱」和「开信刀」属于无来源伤害，带有「为你而死」和「瓦皮的忠诚」的召唤物不会替你承受该伤害。」——16 语言，牌名/遗物名直接取游戏本体各语言本地化的 `SMOKESTACK.title` / `LETTER_OPENER.title`（从 SlayTheSpire2.pck 里查出来的）。③用户：「能不能让哪些在 boss 回合开始时造成的伤害在结算后马上从伤害预览中移除…（水银沙漏、抱抱先生、黑洞+下回合辉星、大抱抱+烟囱）」。抱抱先生早就有这套（`MrStrugglesFiredForTurn`），本轮补齐另外三个：`HourglassFiredForTurn`（在 `FireTurnStart` 跑完她的遗物回合开始钩子后记账）、`BlackHoleFiredForTurn`（在 `SettleStarNextTurnPower` 真的开出黑洞伤害之后）、`BigHugFiredForTurn`（`DoScriptedShuffleAsync` 洗完牌之后）；`BossHandPanel.BuildExtraIntents` 里 `TurnNumber <= 记录值` 就不画那条最左列意图，下一回合 `TurnNumber++` 自然又出现。 )

### v0.2.730 

2026-11-17 (**顶栏显示角色名 + 刀猎第 4 回合恢复幻影之刃** —— ①用户截图（三层「Ω观 & 静默猎手」那一栏）：「最上方一栏这里应该显示观者而非具体名称，就像旁边的静默猎手一样。只有在历史界面才显示具体名称（比如图中的欧米伽观）」。机制核对：那一栏读的是**遭遇的 `.title`**（`NMapPointHistoryHoverTip._Ready` 的 `ModelTitle` 同源），而历史悬浮框早已由 `RunHistoryBossVariantPatch` 用 `<KEY>_BOSS.name`（monsters.json 的设计名）覆盖 ⇒ 所以只改遭遇 `.title`：四只观者遭遇（神格观 / Ω观 / 印牌观 / 旧 WATCHER_PRINT）的 `.title` 在 16 语言里全部改成**角色名**（zhs 观者 / zht 觀者 / eng The Watcher / jpn ウォッチャー / kor 와쳐 / rus Созерцающая / pol Obserwatorka / deu Die Seherin / fra La gardienne / ita Watcher / esp·spa La Vigilante / ptb A Observadora / tur Gözlemci / ind Watcher / tha นักพรตเฝ้าการณ์ —— 名字取自观者 mod 自己的 `characters.json` 的 `WATCHER.title`）。死亡提示 `.loss` 与历史界面的设计名都**不动**，其它角色本来就是这样（静默猎手在顶栏、刀猎在历史）。②用户：「刀猎在正常模式下的幻影之刃怎么被你换成精确切击了？应该只在恐惧模式下换成精准。你不止换错了模式还换错了牌！」—— 事故复盘：v0.2.594 那批「恐惧批次」把恐惧模式的改动**直接写进了共用剧本** `SilentScript` 第 4 回合（普通模式也被换掉），而且换的是 `PreciseCut`（精确切击，那是**炼药猎**第 5 回合的牌）而不是 `Accuracy`（精准）。现在剧本恢复 `PhantomBlades`（幻影之刃），并新增**恐惧模式换牌表** `MultiplayerCardSwap.DreadTable`（与多人换牌同一套 `Resolve`/`ResolveQuiet` 通道，`ForcesUpgrade` 让换上的那张强制升级）⇒ 普通模式 = 幻影之刃、恐惧模式 = 精准+，两张都是 1 费所以第 4 回合 4 费的能量账不变。 )

### v0.2.729 

2026-11-17 (**意图总表校正（二）：紫灰塞状态牌 + 自动识别强化来源 + 荒疫打击多段**) —— 用户逐条回答后落地：①新增**紫灰「塞状态牌」意图**（原版 `StatusIntent(数量)`，带数量标签）：只有两种情况画它 —— 状态机（`DEFECT_STATUS`，特殊效果「同步」`BossSyncPower` 会把她的状态牌**复制一份丢进每位真人玩家的弃牌堆**）与碎屑储（一层 `REGENT_BOSS`）的「碰撞轨迹」（碎屑由 `CometDebrisPatch` 搬进被锁定玩家的抽牌堆）。②「给自己塞状态牌」= **牺牲**：`StatusBossMath.StatusCardsGenerated` > 0 即加牺牲（数量：强撑 2、其余 1），唯一例外是碎屑储的碰撞轨迹（她不留 ⇒ 只画攻势 + 紫灰）。于是四只 Boss 自动对上用户口径：碎屑储碰撞轨迹 = 攻势+紫灰；仆从储碰撞轨迹 = 攻势+牺牲；能力机高速脱离 = 守势+牺牲；状态机 = 紫灰+牺牲（强撑另有守势）。③**自动识别「对自己有利」的行为**（新 `Core/CardBehaviorScan`）：扫该卡牌类型所有方法（含编译器生成的 `<OnPlay>d__N.MoveNext`）的 IL，解出 `call/callvirt/newobj` 目标方法后归类 —— 抽牌 `CardPileCmd.Draw*` / 印牌 `AddGeneratedCardToCombat`·`CreateCard*` / 捞牌 `CardPileCmd.Add(…, PileType.Hand, …)` / 返顶 `MoveToTop*`·`MoveToBottom*`·`Shuffle*`·`Add(…, Draw, …)` / 烧牌 `Exhaust*` / 变牌 `Transform*`·`Transmute*` / 进入姿态 `*Stance*`·`EnterCalm|Wrath|Divinity`（`Preview*` 一律不算）⇒ 命中任何一条就是「强化」，不必再一张张加点名表。④**同类意图只留一个**：函数末尾 `DedupeNonAttackIntents` —— 除攻势外（强化/牺牲/守势/策略/塞状态牌/诱惑/审判 各留第一条），修「一张牌既烧牌又印牌就两个强化箭头」。⑤**荒疫打击**改成多段攻势（`基础伤害 ×2`，反编译它的灾厄 = 这一击实际造成的总伤害）+ 策略。⑥诱惑的通用兜底按用户要求**不动**。 )

### v0.2.728 

2026-11-17 (**意图判定按用户给的总表校正（第一批）** —— 用户给出了每条意图的判定口径：「攻势：造成伤害或施加灾厄 / 守势：直接获得格挡（不考虑从能力牌中获取）/ 强化：打出能力牌、获得正面效果、抽牌、印牌、变牌、进入姿态 / 牺牲：给自己扣血、给自己上debuff、给自己塞牌、进入愤怒 / 策略（红）：给敌方施加负面 / 策略（紫灰卡牌）：给敌方塞牌 / 诱惑：给敌方施加正面效果 / 审判：满足条件立刻使目标死亡」。本轮先落三处：①**攻势 = 施加灾厄** 不再点名四张牌 —— 改成「凡是会施加灾厄的牌」都画一条「攻势 = 灾厄层数」（`DoomAmountOf` 按 `PowerVar<DoomPower>` 读），于是塔罗愚第 7/14 回合的「死神」（75 层灾厄、本身不造成伤害）也终于有攻势了；唯一例外仍是荒疫打击（灾厄层数就等于它自己的伤害）。②**鞭打恢复强化**：上一版我把它点名塞进了 `NoBuffIntentCards`（以为灾厄牌都只该有攻势+策略），但按总表它是「攻势 + 策略 + **强化**」（它自己会抽牌）⇒ 从禁表里撤回（死亡使者 / 无处可逃无自我增益，靠通用规则自然不画强化）。③**审判兼容观者模组**：本体 `Judgement` 阈值变量叫 `Threshold`、观者 A 版 `WatcherJudgment` 叫 `MagicNumber`、B 版 `Judgment` 叫 `DamageThreshold`（30 / 升级 40），以前只认本体 id ⇒ 观者版永远不出审判意图；现在三个变量名依次找、id 去掉 `-`/`_` 后缀比。其余待确认项（紫灰「塞状态牌」意图、牺牲里的「给自己塞牌」、诱惑的通用兜底）已列成问题发回。 )

### v0.2.727 

2026-11-17 (**修：灾厄牌意图 + 能力牌一律「强化」** —— 用户截图（灾厄骨）：「死亡使者、鞭打等牌意图不对，我上次改之后应该是策略+攻势（伤害=施加灾厄层数）。你是不是哪次兜底的时候搞错了，给所有技能牌都搞了个强化？厄运之衣应当是强化而非守势。所有能力牌的意图都应该是强化（有少数例外，如刀扇和会激发充能球的能力牌会带有攻势意图，但本身也是有强化的）」。根因：`DoomIntentAmount` 只按**变量名** `『Doom』` 读预览值，实机读到 0（`意图 DEATHBRINGER: [BuffIntent+DebuffIntent]`、`意图 SCOURGE: [BuffIntent+DebuffIntent]`）⇒ 那条「攻势 = 灾厄层数」根本没加，牌上只剩「`intents.Count == 0` 兜底的强化」+ 通用分类的策略。变量名不可靠（`PowerVar<T>` 的键不总等于 `T` 去掉 `Power`）⇒ 新增 `DoomAmountOf`：**按泛型类型**扫 `PowerVar<DoomPower>`，取 `BaseValue` 与预览值较大者（升级后的 27 / 16 也读得到）。另外把 DEATHBRINGER / SCOURGE / NO_ESCAPE 点名加进 `NoBuffIntentCards`（它们的变量里有 `Cards` 之类会被 `selfBenefit` 误认成自我增益 ⇒ 多一条强化），现在就是**攻势 + 策略**两条。以及新增「能力牌」统一口径：`CardType.Power` 一律摘掉守势、保证一条强化，其它意图（攻势 / 策略 / 牺牲）原样保留 —— 修「厄运之衣（`SHROUD`，Power + `BlockVar`）只画成守势」。 )

### v0.2.726 

2026-11-16 (**修：塔罗愚「死亡」回合玩家的格挡被反复吃掉** —— 用户：「我打一张防牌然后刚获得的格挡就马上消失了，我拿控制台调格挡也会消失」。根因：`BossFoolPower` 把「扣格挡」写在了 `ModifyPowerAmountGivenAdditive` 里，而反编译 `PowerCmd.Apply` 的调用顺序是 `ModifyPowerAmountGiven`（算层数）→ … → `AfterPowerAmountChanged`（真的挂上之后），那个「给定层数」钩子**并不保证后面真的施加**：任何预览/试算路径都会调用它一遍，旧实现顺手就把格挡扣了。日志铁证：一次 `PLAY DEATH` 之前 `愚者：… 用 48 点格挡抵消了 48 层灾厄（原本 75 → 27）` 出现了 3 次（规划期 / 玩家回合中两次），而她第 7 回合出手前的玩家快照里**根本没有 DOOM_POWER** ⇒ 那几次灾厄一次都没真加上去，只有格挡被吃掉。现在「给定层数」钩子**只读不写**（永远返回 0），真正的「扣格挡 + 减灾厄」移到 `AfterPowerAmountChanged`（`amount` = 这次实际给的层数、`power.Owner` = 被施加者，天然满足「只减这次新增、已存在的灾厄不动」）。同时保留诊断计数器：试算次数 vs 真正挂上次数，日志里一行可核。 )

### v0.2.725 

2026-11-16 (**诊断构建：塔罗愚「死亡」回合玩家格挡被反复吃掉** —— 用户：「我打一张防牌然后刚获得的格挡就马上消失了，我拿控制台调格挡也会消失」。日志实证 `愚者：静默猎手 用 N 点格挡抵消了 N 层灾厄` 在第 7 回合**反复出现**（22:25:58 规划期 / 22:26:12 / 22:26:21 玩家回合中 / 22:26:52 真正打 DEATH 时），而 `BossFoolPower` 只在 `PowerCmd.Apply<DoomPower>` 里被调用 ⇒ 说明这个钩子被**反复**调用（灾厄一直没真正加到她身上：每次都 75 → 抵消若干）。现在加临时诊断：把灾厄钩子每次调用的次数/amount/格挡/来源牌打出来，第 1 次调用还打 14 行调用栈，用来定位是谁在反复施放。修好即删。)

### v0.2.724 

2026-11-16 (**修：至纯之水补的那张「奇迹」被弃手牌吃掉了** —— 用户第二轮实测「你这都没生效啊」，日志实证 v0.2.723 的调用点选错了：`FireCombatStart` 早于 `PrepareTurn`，而 `PrepareTurn` 开头第一句就是 `BossPiles.DiscardHand(pcs)` ⇒ `至纯之水…补 1 张「奇迹」到她手里` 之后紧接着 `重建手牌` / `剧本手牌(4)`、`discard=1`，那张奇迹进了弃牌堆，她第 1 回合手里还是 4 张。A 版（Boninall）之所以看得见，是因为它的 `BeforeCombatStart` 派发得**更晚**（晚于 `PrepareTurn`），正好躲过这次弃牌。现在把补牌改到 `PrepareTurn` 里 `BossScriptRunner.BuildHand` **之后**（`TurnNumber == 1` 时），两版第 1 回合手牌都是 5 张（剧本 4 + 奇迹）；剧本没写这张牌 ⇒ 她永远不会打它 ⇒ 每回合能量账不变。 )

### v0.2.723 

2026-11-16 (**修：至纯之水两版行为对齐** —— 用户：「这A版的至纯之水能生效，B版的至纯之水不能，什么情况？能不能让它们都生效？」。反编译实证：两版给的牌**完全一样**（战斗开始 1 张「奇迹」进手牌、不升级），差别只在钩子 —— Boninall `WatcherMod.PureWater.BeforeCombatStart()` **没有 player 参数**，`Hook.BeforeCombatStart` 把所有监听者逐个派发一遍 ⇒ 她的影子玩家收得到（A 版实机日志：`剧本手牌(4)` 之后手牌变 5 张并多出 `WATCHER_MIRACLE`）；lamali `Watcher.Code.Relics.PureWater.BeforeHandDraw(player, ctx, state)` 要求 `player == Owner`，而 `BeforeHandDraw` 只在 `CombatManager.SetupPlayerTurn` 里派发，`StartTurn` 里 `playersStartingTurn = CurrentSide == Player ? state.Players : new List<Player>()` ⇒ **敌方回合永远不为影子玩家派发** ⇒ B 版那张奇迹从来没出现过。现在选「都生效」：只给 lamali 版在战斗开始自己补 1 张「奇迹」（时机/张数/落点/升级状态全部对齐 A 版，且幂等；Boninall 版一个字不动）。因为剧本没写这张牌她永远不会打它 ⇒ 每回合能量账不变。 )

### v0.2.722 

2026-11-16 (**修：何人僭越的意图只该有策略** —— 用户截图：「何人僭越错误的显示了强化，应该只有策略」。日志实证 `意图 KNOW_THY_PLACE: [BuffIntent+DebuffIntent]`；它是纯减益技能牌（给目标 1 层虚弱 + 1 层易伤 + 消耗），那条强化是别处的兜底给的。现在把它也纳入 `ApplyWatcherStanceIntents` 的收口：清掉强化/牺牲家族、保证**恰好一条**策略。)

### v0.2.721 

2026-11-16 (**修：第 6 回合警惕不打的根因 = 规划期没把「化体为空退出平静 +2」算进来** —— 用户截图「第六回合的警惕怎么不打了」。日志实证 `剧本第 6 回合: 打 [CARVE_REALITY, EMPTY_BODY] 跳过 [VIGILANCE(费/星不足 费 2>0), SMITE(1>0)]`：规划期的「在不在平静」只认**警惕**这一张牌进平静，而她这套剧本进平静靠的是**内心宁静**（第 4 / 8 回合）⇒ 以为她不在平静 ⇒ 化体为空退出时不加那 +2 ⇒ 预算停在 0 ⇒ 警惕被跳过。现在 `watcherCalm` 的判定：进平静认 警惕 / **内心宁静** / 冥想 / 安宁；退出姿态认 化拳为空 / **化体为空**（两种都 +2）。预算不变（第 6 回合仍是 2 费：2 −改造现实1 → 化体为空1 +离开平静+2 → 警惕2 → 惩恶+ 差 1 没费）。)

### v0.2.720 

2026-11-16 (**印牌观削弱：化拳为空 → 化体为空** —— 用户「削弱，化拳为空改成化体为空，其它不变」。化体为空（`WATCHER-EMPTY_BODY`，1 费：获得格挡 + 退出姿态）不再打伤害，费用与化拳为空相同 ⇒ 逐回合能量账一个字都不用改（第 6 回合仍是 `2 −改造现实1 → 化体为空1 +离开平静+2 → 警惕2 → 惩恶+ 差1 没费`；第 5 回合同样 2 费、化体为空打不出）。剧本第 5、6 回合改用它，其它回合/数值/遗物/面板一律不变。)

### v0.2.719 

2026-11-16 (**修：牺牲意图被「强化去重」吃掉 + 未在愤怒的义愤填膺不该带策略** —— 用户截图「义愤填膺+ 只有强化 + 策略、没有牺牲」。根因：`SacrificeIntent` **继承 `BuffIntent``（共用蓝色箭头、由 UI 翻转让它朝下），所以 `DedupeBuffIntent` 把「牺牲」当成重复的强化删掉了。现在 `ApplyWatcherStanceIntents` 改成按**意图家族整体清掉再按需放回**（强化 → 牺牲），不再走会误删的 `DedupeBuffIntent`；并对义愤填膺单独收口：**未在愤怒** ⇒ 强化 + 牺牲、**并删掉策略**（它那一刻只进愤怒、不施加易伤；以前 `ClassifyPowers` 看到 `PowerVar<VulnerablePower>` 就无条件补策略）；**已在愤怒** ⇒ 只留一条策略。)

### v0.2.718 

2026-11-16 (**姿态意图改成按剧本推演收口 + 第 6 回合惩恶真凶是「化拳为空离开平静 +2 费」** —— ①用户「第六回合为什么还是打惩恶」：日志实证 `观者姿态：WATCHER-EMPTY_FIST 退出「平静」⇒ 原版 +2 能量（规划期算入）` ⇒ 化拳为空**离开平静白送 2 费**，4 费回合实际 6 费。现在第 6 回合能量写成 **2**：2 −改造现实1 = 1 → 化拳为空 1 = 0 → 离开平静 +2 = 2 → 警惕 2 = 0 → 惩恶+ 差 1 ⇒ 没费。②**姿态意图统一在 `IntentsForCore` 末尾按意图列表增删**（`ApplyWatcherStanceIntents`，幂等）：之前改标志位压不住别处补的意图（实测第 8 回合义愤填膺仍出 `BuffIntent+DebuffIntent`、第 7 回合暴怒只有牺牲没有强化）。③**判定「在不在愤怒」改用剧本推演的 `stanceMul`**（`IntentsFor(… stanceMul:)`，与 `StanceBake` 同一把尺子），不再读「此刻的真实姿态」—— 计划是在她上一回合结束时算的，那时第 4 回合的暴怒还没打出去，所以以前永远判成「不在愤怒」。④最终口径：愤怒牌（暴怒/渐强/义愤填膺）**未在愤怒** ⇒ 恰好一条强化 + 一条牺牲；**已在愤怒** ⇒ 暴怒/渐强只剩攻势、义愤填膺只剩策略；**平静牌**（警惕/安宁/冥想/内心宁静）⇒ 强化（用户要求加回来）；**改造现实/欺瞒现实** ⇒ 强化；不惧妖邪仅在 `PlayerAttacked` 时强化。)

### v0.2.717 

2026-11-16 (**愤怒牌意图改成「未在愤怒才加」：已在愤怒只剩攻势/策略；平静牌保持现状** —— 用户 2026-11-16：「应该让所有愤怒牌都是未在愤怒时才会加上强化和牺牲，如果已经处于愤怒，比如第四回合的暴怒，就只显示攻势（因为已经在愤怒了，所以就只打伤害了）」+「平静牌先不改，保持现状」。现在 `BossIntentBuilder.IntentsFor` 的规则是：愤怒牌（暴怒 / 渐强 / 义愤填膺）**未在愤怒** ⇒ 强化 + 牺牲；**已在愤怒** ⇒ 暴怒 / 渐强去掉强化与牺牲（只剩牌面攻势）、义愤填膺去掉强化与牺牲只留策略。「进平静 ⇒ 强化」那条整段撤回（上一版加的）。另外保留：不惧妖邪在被锁玩家本回合打过攻击牌时加强化（含刷新指纹），以及消耗堆「奇迹」不再每回合复燃白送 1 费。)

### v0.2.716 

2026-11-16 (**进姿态的意图规则 + 第 6 回合惩恶多打的真凶：消耗堆里的「奇迹」每回合复燃白送 1 费** —— ①用户「第六回合的惩恶怎么还是打」：日志实证 `回合开始复燃：WATCHER-MIRACLE（在消耗牌堆里）已按其自身钩子打出` 紧接 `剧本第 9 回合 … 余费=1` ⇒ 至纯之水那张奇迹打完进消耗堆后，**每回合复燃给 +1 能量**，于是 4 费的回合实际有 5 费 ⇒ 多打一张惩恶。现在 `PlayExhaustPrePlayCardsAsync` 跳过 MIRACLE（她的能量必须严格等于剧本值）。②**进姿态的意图规则**（用户 2026-11-16）：所有**进愤怒**的牌（暴怒 / 渐强 / 未在愤怒时的义愤填膺）⇒ **强化 + 牺牲**（进愤怒连受到的伤害也翻倍）；所有**进平静**的牌（警惕 / 安宁 / 冥想 / 内心宁静）⇒ **强化**；已去重（`DedupeBuffIntent`）不会出现两个强化。③**义愤填膺**已在愤怒时**只**显示策略（之前会同时显示强化+策略）。④**不惧妖邪**：只有当 `WatcherFearNoEvilAimPower.PlayerAttacked`（被锁玩家本回合打过攻击牌 ⇒ 她这一手会进平静）时意图才加强化，并把这个标记拼进 `RefreshIntentsIfPowersChanged` 的指纹 ⇒ 玩家打完攻击牌瞬间面板就刷新。)

### v0.2.715 

2026-11-16 (**印牌观四条订正：义愤填膺意图条件化 / 第 6 回合能量对账 / 水银沙漏→船夹板 / 第 8 回合内心宁静打得出** —— ①**义愤填膺的意图按姿态二选一**（用户：「当义愤填膺会使观者进入愤怒时意图应显示为强化，如果会让观者给予易伤的话则显示为策略」）：`BossIntentBuilder.IntentsFor` 里对 `INDIGNATION` 用 `StanceMultiplierOf` 判「此刻在不在愤怒」——不在 ⇒ 强化（进入愤怒）、已在 ⇒ 策略（给所有敌人易伤）；并从静态 `ForceDebuffCards` 里移除。②**逐回合能量重新对账**（用户：「每回合基础是两费」）：`BossTurn.Energy` 会覆盖真实能量而 `CardCmd.AutoPlay` 不扣费 ⇒ 表格写进 `WatcherCreateScript` 头注释，逐格可核：1=2（武器库+1 + 操控现实+**0** + 改造现实1）/ 2=2（奇迹0 但 +1 能量 + 心灵堡垒1 + 警惕2）/ 3=3 / 4=2 / 5=2 / 6=**4**（改造现实1 + 化拳为空1 + 警惕2 ⇒ 惩恶+ 还差 1）/ 7=4 / 8=**2**。③**水银沙漏 → 船夹板**（`HornCleat`：格挡被打空后 +14 格挡；水银沙漏其实是「回合开始对所有敌人 3 点无来源伤害」）。④**第 8 回合内心宁静打得出**：供奉0 + 义愤填膺1 + 内心宁静1 = 2，正好花光基础 2 费（上一版我写成 1 费把它挡掉了）。)

### v0.2.714 

2026-11-16 (**印牌观四处订正：循环 6～8 / 义愤填膺进姿态推演 / 愤怒时受击也翻倍 / 第 6 回合恢复化拳为空** —— ①**循环原来是 5～7**：`BossScript.LoopFrom` 是**1 起**的回合号（`index = LoopFrom - 1 + (turn - LoopFrom) % LoopLength`），我上一版当成 0 起填了 5 ⇒ 第 6 回合起跑的是第 5 回合的牌、第 8 回合被跳过（用户：「你循环错了…第八回合被错误的跳过了」）。现在 `LoopFrom = 6, LoopLength = 3`。②**义愤填膺进姿态推演**（三处：`BossScriptPlanner.plannedStanceMul` / `BossIntentBuilder.StanceAfterPlay` / `StanceAfterEntry`）：它不在愤怒时进入愤怒 ⇒ 第 3 回合它后面的「惩恶+」按 ×2 预览（顺带把「内心宁静 ⇒ 进入平静 ×1」也补上）。③**愤怒时受击也翻倍**：原版 Wrath 是双向的，而 B 版姿态倍率挂在 `WrathStance.ModifyDamageMultiplicative`，判据 `dealer == Owner.Creature` ⇒ 只管她出手；现在 `Hook.ModifyDamage` 的 postfix 增加「target 是她 + 姿态是 WrathStance」分支 ⇒ 玩家打她 ×2（神格不翻受击，与原版一致）。④**第 6 回合恢复「化拳为空」正常参与、能量 3→4**：改造现实 1 + 化拳为空 1 + 警惕 2 = 4，惩恶+ 还差 1 ⇒ 没费（上一版我误把它标成 HoldInstead 是因为循环错位看到的是第 5 回合）。)

### v0.2.713 

2026-11-16 (**印牌观订正：能量按真实卡费重算（修「没费的牌被打出来」）+ 改名 WATCHER_CREATE_BOSS + 意图/说明/面板按用户新稿** —— ①**两个 bug 的根因是能量写大了**：`BossTurn.Energy` 会覆盖她那个回合的真实能量（`CharBossMonster.Turn.cs` 的 `energy = scriptedTurn.Energy`），而 `CardCmd.AutoPlay` **不扣费** ⇒ 我把第 1 回合写成 3、第 6 回合写成 4，于是标了「没费」的惩恶+ / 化拳为空照样付得起。现在按反编译出来的**真实卡费**重算：操控现实+ **0**（升级 −1）、暴怒+ **1**（升级 −1）、武器库/创世之柱/改造现实/欺瞒现实/惩恶/平安/义愤填膺/内心宁静/化拳为空/打击/心灵堡垒 各 **1**、警惕 **2**、供奉/何人僭越/奇迹 **0**（奇迹打出 +1）⇒ 逐回合能量 2/2/3/2/2/3/4/1，标了「没费」的那几张正好差 1 点。第 6 回合的化拳为空排在警惕前面且 1 费，光靠能量挡不住 ⇒ 显式 `HoldInstead`（用户原话「化拳为空被错误的打出」）。②**改名**：`WATCHER_PRINT*` → `WATCHER_CREATE*`（怪物 `WatcherCreateBoss` / 遭遇 `WATCHER_CREATE_BOSS_ENCOUNTER` / 设计 Key `WATCHER_CREATE` / 本地化 `WATCHER_CREATE_BOSS.*`）。③**特殊效果文案**去掉括号及其内容（「（已升级的退回未升级）」整段删，16 语言）。④**说明框位置**：印牌观与神格观共用 `Def.Key`，以前白吃了神格观那个 −120 偏移（用户「位置有些偏高」）⇒ `UsesLongSpecialText` 里排除印牌观，用正常位置。⑤**意图按用户新稿重排** 3/4/7/8 回合（3 义愤填膺+惩恶+创世之柱 / 4 供奉+暴怒+内心宁静 / 7 暴怒+欺瞒现实+惩恶+平安+打击(没费) / 8 供奉+义愤填膺+内心宁静(没费)）。⑥**能力面板**：循环 6～8 / 单体 强 / 群攻 中 / 单体高压 3、4、7、8 / 群体高压 4、8 / 单体 / 群体减益 强。)

### v0.2.712 

2026-11-16 (**新增二层 1 号观者 Boss「印牌观」** —— 用户设计的第三套观者设计：特殊效果「操控现实」= 玩家在战斗中**生成**的牌被**降级**、非衍生牌再加**消耗**（`BossMasterRealityPower.AfterCardGeneratedForCombat`）；她通过色彩哲学家拿到本体储君牌「武器库」（每生成一张牌 +1 力量）「创世之柱」（每生成一张牌 +2 格挡）「何人僭越」（0 费 易伤+虚弱），自己打「改造现实/欺瞒现实」时会**印牌**（惩恶/平安）——原版那次生成被 `BlocksGeneratedCardsEntries` 拦掉，改由 `PlayOne` 打完手动 `TriggerPrintAsync` 补结算创世之柱/武器库；8 回合剧本 + **6～8 循环**、逐回合能量按卡费反推（3/2/4/2/3/4/5/2，正好让用户标「没费」的那几张差 1 点）、进阶 8 升创世之柱+心灵堡垒、进阶 9 升改造现实+供奉+义愤填膺；遗物 至纯之水/涅奥的祝福/斗篷扣/浮木/水银沙漏；意图：义愤填膺+何人僭越 → **策略**，武器库的回合内 +力量进规划期预补（第 6、7 回合的惩恶+）；能力面板（循环 6～8 / 单体偏强 / 群攻中 / 单体高压 3、7 / 群体高压 4、7 / 单体 / 群体减益强）；藏品 = 操控现实；16 语言 10 键。接线：`Watcher.ExtraVariants[1]` + `WatcherPrintBoss`（TierAct=1）+ `WatcherPrintBossEncounter` + `BossKeys` 登记 `WATCHER_PRINT` + `RosterFor(1)` 追加（同样走「全端同一观者实现」闸门）。)

### v0.2.711 

2026-11-16 (**多人：房主直接广播「每场最终遭遇」+ 观者只在「全端同一实现」时出场** —— 用户拍板两条。①**协议级根治「第三幕第二场不同步」**：`BossPicksMessage` 新增 `Bosses`（`幕:场|KEY` 或 `幕:场|VANILLA`）与 `Caps`（客人的观者实现身份），并且这条消息从此**双向**（房主发勾选/玩法设置/每场最终遭遇/观者结论；客人只发自己的观者实现）。`CharBossEncounters.ForVanillaBoss` 现在是「房主记结论并触发广播、客人无条件采用」：房主说 `VANILLA` 就不替换，房主给 Key 就用那只，没收到才退回旧的确定性规则。这一条一次关掉「客人没收到勾选 / 地图替换开关默认值（模组图默认不替换）/ 候补表不同」这三类分叉 —— 正是玩家报的「房主俩玩家 Boss、客人一个原来的 Boss」。②**观者只在全端一致时出场**：用户「只有当所有玩家都装了相同的观者 mod（指统一为 A 或 B 版）时才让观者有可能作为 boss 出现」⇒ 新增 `WatcherCards.ProviderId`（`boninall`/`lamali`/空），客人上报、房主汇总「玩家数−1 份且每份都跟自己一样」才把观者放进三层候补表；客人用房主广播的 `wok=` 结论，没收到就**保守不放**。`BossKeys.WatcherRoster()` 已接这条闸门（候补表条数一变阵容就会分叉）。另外：换局时 `BossPicksSync.ResetForNewRun()` 清空同步状态（免得上局的客人能力位/每场结论漏到新局）。⚠ 协议变了 ⇒ 多人两端必须同一版本。)

### v0.2.710 

2026-11-16 (**多人自检：`WantsSecondBoss` 与 `BossLineups.PlayerCount` 口径统一 + 一行「※阵容输入※」诊断** —— 玩家在工坊评论报「新版多人联机 n10 第三幕第二个 boss 疑似不同步（房主俩玩家 Boss / 客人一个原来的 Boss）」但没有日志，本轮做静态自检。①**真 bug**：`MultiBossPlan.WantsSecondBoss` 读不到 RunState 时按「1 人」算，而 `BossLineups.PlayerCount()` 早有「记住上一次读到的人数」的兜底（2026-11-05 修「房主 2 只 / 客人 1 只」时加的）⇒ 拉房间/换幕那一瞬只要读不到，就会出现「阵容按 2 只排、`PartnersForWithKeys` 判成单人 ⇒ 一只伙伴都不加」的半截状态。现在两边同一个数。②新增 `BossLineups.LogInputs`：每个 (幕, 场) 打一行 `※阵容输入※ … 本机=Host/Client · 玩家 · 进阶 · 种子 · 地图（替换=）· 候补表条数（含观者=）· 勾选后候选条数 · 已收到房主勾选 · 共用匹配池 · 指纹`，**两份日志并排一比**就能定位是「观者模组/勾选广播/地图开关」哪一处不同导致阵容分叉。③自检结论（未改）：`BossKeys.WatcherRoster()` 只按本机「装没装观者模组」决定三层候补表 ⇒ 一端装一端不装，第三幕两场阵容都可能不同（配合 `SubstituteIfUnavailable` 只是把观者换成塔罗愚，两端仍打不同的 Boss）；多人时勾选/地图开关/共用匹配池全靠 `BossPicksSync`（需 BaseLib）广播，客人没收到就退回「整张表 + 模组地图默认不替换」⇒ 客人可能**整场不替换**（正是「客人看到原来的 Boss」的形态）。这两类要根治得让房主把「本幕两场的最终遭遇」直接广播给客人（协议改动，待用户拍板）。)

### v0.2.709 

2026-11-16 (**B 版神格：真凶是反射重载歧义 —— 退姿态/进神格以前根本没被调到；外加说明框层级** —— 用户第六轮两条实测：①「她已经处在神格时，再积累 10 层真言，不会像特殊效果里说的一样先退出姿态然后再进入神格（这会导致没有成功加费）」；②「特殊效果描述的层级不对，切到地图页面了还能看见」。①的根因不在策略而在 `WatcherCards.Method`：B 版 `StanceCmd.EnterDivinity / ExitStance / EnterCalm` **各有两份重载**（`(ctx, Player, CardModel?)` 与 `(ctx, IEnumerable<Player>, CardModel?)`），旧写法 `GetMethod(name, flags)` 遇歧义抛 `AmbiguousMatchException`、被 catch 吞掉 ⇒ 返回 null ⇒ `m?.Invoke` **静默什么都不做**。日志铁证：三次 `StanceCmd.EnterDivinity 之后姿态模型` 的「之后」都等于「之前」（`NoStance→NoStance`、`CalmStance→CalmStance`、`DivinityStance→DivinityStance`）= 这一步从来没执行过 ⇒ 「退姿态」一次都没退过（平静 +2 也从未给过），进神格全靠 v0.2.707/708 的 silent 兜底；而她本来就在神格时连兜底都不触发（`after.Contains(\"Divinity\")` 直接跳过）⇒ `能量 2 → 2`。现在：`Overload()` 按参数类型精确取重载（取不到再按名字、最后自己挑「第二个参数是 Player」那条并打一行 `反射消歧`）；`ExitStanceAsync` 改成真 await + 打 `退出姿态：X → Y` + 没退掉就 silent 强制清；`EnterDivinityCoreAsync` 进来先看姿态、已在神格就先退再进。②说明框挂在全局提示层（`NGame.HoverTipsContainer`）⇒ 战斗中打开地图/图例/牌堆时战斗房还活着，框就压在这些界面上面。新增 `BossSpecialTip.CombatScreenActive()`（战斗房 + `NOverlayStack.ScreenCount==0` + `NMapScreen.IsOpen==false` + `ActiveScreenContext.IsCurrent(room)`），`Ensure` 与 `BossSpecialTipRoomGuardPatch` 都改用它。另：`InCalm` 也改问姿态模型（B 版姿态不是 power，日志 `本来在平静=False` 而同秒模型是 `CalmStance`）。)

### v0.2.708 

2026-11-16 (**B 版神格：强制设姿态之后补跑 OnEnterStance（那 +3 能量在里面）** —— 用户第五轮实测后日志给出了真相：`进神格：StanceCmd.EnterDivinity 之后姿态模型 = NoStance` → `进神格（强制 SetStanceSilently<DivinityStance>）⇒ 姿态模型 = DivinityStance`，即 v0.2.707 的强制设姿态**已经成功**，但 `SetStanceSilently` 是 silent 版 ⇒ **跳过 `OnEnterStance`**（入姿态的 +3 能量、入姿态钩子、演出都在里面）⇒ 玩家侧看着仍然「没进神格」。现在强设之后补跑姿态模型自己的 `OnEnterStance(ctx, owner, source)`，并打印 `能量 before → after / 神格里=?`。另：神格观的遗物 护喉甲 → 奥利哈刚（`WatcherScript.Relics()` 里 `Relics.Gorget` → `Relics.Orichalcum`）。)

### v0.2.707 

2026-11-16 (**神格观：护喉甲→奥利哈刚 + B 版真言满 10 强制进神格** —— ①用户：「修改神格观，护喉甲换奥利哈刚」⇒ `WatcherScript.Relics()` 里 `Relics.Gorget` 换成 `Relics.Orichalcum`。②用户：「B 版神格观还是 10 层真言进不了神格」。日志：`虔信：真言满 10 ⇒ … 能量 3 → 3，神格里=False` —— `StanceCmd.EnterDivinity(ctx, player, source)` → `WatcherModel.SetStance<DivinityStance>` 在影子玩家身上依然静默无效。现在两步：先照旧等它跑完并**打出姿态模型类型名**（`GetStanceModel(player)`），如果还不是 Divinity，再走一条**不需要上下文**的官方入口 `WatcherModel.SetStanceSilently<DivinityStance>(player)`（反射泛型实例化）；两步都失败才放弃并记日志。顺带 `InDivinity` 也改成「power 名 或 姿态模型是 DivinityStance」——B 版的姿态不是 power，以前那个判断会把真的进神格也报成 False。)

### v0.2.706 

2026-11-16 (**B 版姿态真正的查法：`WatcherModel.GetStanceModel(Player)`** —— 用户：「B 版欧米伽观愤怒时打出的攻击牌还是不翻倍」。日志里 `姿态倍率兜底：已挂 Hook.ModifyDamage` 装了，但每次出手都没进那条 postfix 的乘算分支 ⇒ 因为 `StanceMultiplierOf` 读的是 `Creature.Powers`，而 B 版的姿态**根本不是生物身上的 power**：反编译 `Watcher.Code.Core.WatcherModel` 里它是 `private static readonly SpireField<PlayerCombatState, WatcherStanceModel> ActiveStance`，入口是 `GetStanceModel(Player)` / `IsInStance<T>(Player)` ⇒ `Creature.Powers` 永远看不到它（于是「预览按计划 ×2、实伤 ×1」）。现在新增 `WatcherCards.StanceMultiplierFromModel(player)`（反射调 `GetStanceModel`，按类型名判 `WrathStance` ⇒ ×2 / `DivinityStance` ⇒ ×3），`StanceMultiplierOf` 在 power 扫描无果时用它兜底。这条同时修好：预览的「此刻倍率」、实伤补算、以及 `BossIntentBuilder` 里所有按姿态推演的路径。)

### v0.2.705 

2026-11-16 (**历史图标变成愚者头像的修正** —— 用户截图：「变愚者了何意味」。v0.2.704 的观者角色兜底读的是 `BossDefs.Watcher.CharacterType`，而它的初值是**愚者占位** `typeof(Characters.Fool)`（真正类型由 `WatcherModPresence.CharacterType()` 运行期解析后写回）⇒ 历史那一格安上了愚者头像。现在直接问 `WatcherModPresence.CharacterType()`；问不到、或者拿到的仍是 `Fool` 占位就返回 null（宁可退回地图徽章，也不能把别的角色头像安到观者头上）。)

### v0.2.704 

2026-11-16 (**观者头像第二次修：直接用她自己的 IconTexture，不再猜路径** —— 用户：「怎么还是这样」。v0.2.703 那次只是补了 B 版（lamali）的候选路径，实测仍退回地图徽章；日志 `结算/历史图标 → … → res://CharacterBosses/images/map/watcher_divinity.png` 说明候选路径 `ResourceLoader.Exists` 全部为假。现在改成**不猜路径**：`CharacterModel.IconTexture` / `IconOutlineTexture`（顶栏那颗 Boss 图标用的正是它们，`TopBarBossIconFallbackPatch.ApplyCharacter` 已验证可取）先取它的 `ResourcePath`；取不到再退回候选路径。另外给观者遭遇加一条角色兜底 `WatcherCharacterFallback`（遭遇 id 以 `WATCHER_` 开头就用 `BossDefs.Watcher.CharacterType` 解析出的角色，不再依赖「按怪物 id 查定义」那条链），并补一行诊断日志 `结算/历史图标诊断：<遭遇> → 角色 …，IconTexture=…，候选存在性 …`，万一还不对，一行就能定位卡在哪一步。)

### v0.2.703 

2026-11-16 (**历史记录/结算里的观者头像改成她的角色头像** —— 用户：「历史记录中观者的头像不对，是头像不是地图图标」。那一路图标来自 `ImageHelper.GetRoomIconPath` → `OurEncounterIconPaths.OurBadge`：它本来先找基础游戏的 `res://images/ui/top_panel/character_icon_<角色>.png`，而观者是**另一个模组**的角色 —— A 版（Boninall）自带这条路径所以一直正常，B 版（lamali）只放在自己的命名空间 `res://Watcher/images/watcher/character_icon_watcher.png` ⇒ 找不到就退回**我们自己的地图徽章**（骷髅+光环），历史上看着就像地图图标。现在两套路径都试（`top_panel` / `res://Watcher/images/watcher` / `res://Watcher/_imported`，描边与主图按调用方要求排序），全都取不到才退回地图徽章。)

### v0.2.702 

2026-11-16 (**B 版三条：许愿固定永生不死 / 真言满 10 真的进神格 / 愤怒伤害真的翻倍** —— ①用户：「许愿会错误的选择力大无穷，应固定为永生不死」。B 版 `WishWatcher.OnPlay` 把三张许愿牌交给 `CardSelectCmd.FromChooseACardScreen`，我们的选择器按兜底排序挑中了 `WATCHER-BECOME_ALMIGHTY`；现在 `BossCardSelector` 里加一条最高优先：候选里出现 `LIVE_FOREVER`（永生不死）就拿它（A 版那条改 `OnPlay` 的补丁仍在，双保险）。②用户：「她还是没进神格」。日志实证我们自己的结算跑了（`虔信：真言满 10 ⇒ … 力量 +2 …`）但结算是 `神格里=False`、能量 2 → 2 —— 因为 `WatcherCards.EnterDivinityAsync` / `ExitStanceAsync` 在 B 版走的是 `StanceCmd.EnterDivinity(ctx, player, card)` → `WatcherModel.SetStance<DivinityStance>(ctx, player, …)`，整条链都用那个 ctx 派发命令，而我们塞的是 `ThrowingPlayerChoiceContext` ⇒ 静默什么都不做。现在两个方法都接受调用方的真实 `PlayerChoiceContext`（没有才退回 `BlockingPlayerChoiceContext`），`BossDevotionPower.ResolveDivinityAsync` 把 ctx 一路传进去。③用户：「她处于愤怒时造成的伤害没有翻倍」。日志实证第 7 回合打击真实只有 10 =（9+1）×1：B 版的姿态倍率挂在 **Player 身上的 stance 模型**（`WrathStance : IModifyDamageMultiplicative`，判据 `dealer == base.Owner.Creature`），而原版伤害管道只遍历「玩家集合 + 怪物身上的 listener」，我们的**影子玩家不在玩家集合里** ⇒ 她的 stance 永远收不到回调（A 版是挂在生物身上的 power，所以本来就生效）。现在只对 **B 版**给 `Hook.ModifyDamage` 挂 postfix：`dealer` 是我们那只观者 Boss 且 `props` 不含 `(ValueProp)4`、`modifyDamageHookType == All` 时按她的姿态倍率乘一次 —— 预览走的也是这条管道，所以显示与实伤自动一致。)

### v0.2.701 

2026-11-16 (**观者 lamali（B）版兼容：点穴/许愿/神格 + 姿态补差双向** —— 用户把观者切成 B 版后五条：①「点穴只有两个策略、不算伤害、也不算本回合前一张叠的印记」：B 版点穴是 `WithPower<MarkPower>(8,3)` ⇒ 变量键叫 **`MarkPower`**（A 版才是普通 `MagicNumber`）⇒ 新增 `WatcherCards.PressurePointsMarks`（`MarkPower` → `MagicNumber` 依次读），意图与规划期「本回合前面那张点穴」都用它。②「出现 2 个红色策略箭头」：B 版点穴带 `PowerVar<MarkPower>`，通用 `ClassifyPowers` 也会补一条减益 ⇒ 我们那条点穴策略置 `suppressGenericDebuff`，通用减益那一段跳过（A 版无影响）。③「许愿完全无效果」：反编译 B 版 `WishWatcher.OnPlay` 走 `CardSelectCmd.FromChooseACardScreen(三张许愿牌)`，那一路 `min=0/max=1` ⇒ 我们旧策略「只选必须选的最少张数」选了 0 张、返回 null ⇒ 什么都没获得。现在「允许选就至少选 1 张」。④「她获得 10 真言没有进入神格」：`MantraTakeoverPrefix` 以前**只**压掉模组那条自动进神格，然后指望我们自己的加真言入口去结算 —— 而她**自己的牌**给的真言不经过我们 ⇒ 没人结算。现在压掉之后当场自己跑 `BossDevotionPower.ResolveDivinityAsync`（扣 10 → 退姿态 → 力量 → 进神格），任何来源的真言都会触发。⑤姿态：`StanceMultiplierOf` 先按 **power id**（日志实证叫 `WRATH` / `DIVINITY`）再按类型名认；`ScaleToPlannedStance` 改成**双向**补差（`计划 < 此刻` 也补，例如「不惧妖邪」把她带进平静后的那张打击），相等才不动手；`WatcherIdIs` 再兼容 B 版基础牌的 `_WATCHER` 尾巴（`WATCHER-STRIKE_WATCHER`）。)

### v0.2.700 

2026-11-16 (**冥想之后玩家的结束回合按钮消失 + 第 3 回合贝塔/如水调序** —— ①用户：「观者出冥想的下个玩家回合开始时，玩家的回合结束按钮会消失」。反编译 `Meditate.OnPlay` 最后一句是 `PlayerCmd.EndTurn(Owner, false, null)` —— 这张牌会替**出牌者**结束回合；我们的影子玩家本来就由 `CharBossMonster` 的流程驱动，中途再被结束一次就把原版回合状态机推成「已结束但没人重置」⇒ 下一个玩家回合 `NEndTurnButton.OnTurnStarted` 拿不到状态、按钮一直 Hidden。两处修：新增 `Patches/ShadowPlayerEndTurnSkipPatch`（`PlayerCmd.EndTurn` 的目标是影子玩家时整条吞掉，日志一行；返回 void 所以不需要 `ref __result`），并把 `BossFrameTickPatch` 里「按钮保险」的闸门放宽 —— 以前只看 `AllPlayersReadyToEndTurn()`，现在**再问一次本机玩家自己是不是已经点了结束回合**，他还没点就照样强制打开按钮。②用户：「调换一下第三回合贝塔和如水的打出顺序（对牌的效果无实际影响）」⇒ 剧本第 3 回合改成 化体为空 → 点穴（0费）→ 如水（进阶8起升级）→ 贝塔 → 奇迹（不出），能量账 4 费不变。)

### v0.2.699 

2026-11-16 (**真凶：`WatcherIdIs` 认不出带 `_P` 的玩家版 id** —— ①用户第三轮截图（第 4 回合打击 7、第 8 回合不惧妖邪 9）：日志里有 `意图伤害管道 WATCHER_STRIKE_P: 牌面 6 → 14`（她**已经在愤怒**时管道自己乘了 ×2），但玩家回合里预演时 `StanceMul` 一直是 1。根因不在姿态逻辑本身：观者模组还有**第三套 id** —— 玩家版牌带 `_P` 后缀，`WATCHER_ERUPTION_P`（暴怒）、`WATCHER_STRIKE_P`（打击）。`WatcherIdIs` 规范化成 `WATCHERERUPTIONP` 后 `EndsWith(\"ERUPTION\")` = false ⇒ 剧本推演里「暴怒 ⇒ 进入愤怒」这一步从来没命中过 ⇒ 它后面的牌永远按 ×1 算。现在多比一次「去掉末尾单个字母」的形态（`_P`/`_A`/`_B`），第 4/8 回合的打击与不惧妖邪恢复 ×2。②「欧米伽的伤害不会被薄荷尖塔统计」：那一笔挂在 power 上、不进 `NextMove.Intents`，薄荷尖塔只看 `NextMove.Intents` + 我们的 `PendingOf` 补丁 ⇒ 现在把 `BossHandPanel.OmegaRowDamage`（与本回合是否触发同一位）加进 `MintySpireDamagePreviewCompat.PendingOf`。③「削弱，进阶9不再升级点穴，而是升级所有打击」：剧本里两张点穴恢复永不升级，第 4/7/9/10 回合的四张「打击」改成 `upgradeMinAscension: 9`；进阶 8 仍是如水 / 心灵堡垒不变。)

### v0.2.698 

2026-11-16 (**欧米伽改成「严格交替」+ 通用规划路也补姿态倍率** —— ①用户「欧米伽的预览怎么每回合都有？」「7、9、11 反而没了」：日志实证 `boss.TurnNumber` 在「她这一回合结束」那一刻**已经是下一回合的号**（`PrepareTurn` 先跑了）⇒ 按它判奇偶，该触发的第 5 回合被判成「偶数跳过」、实际变成 6/8/10 触发（刚好反了）；而且原版 `AfterSideTurnEnd` 对**玩家侧**也会派发，前缀没过滤阵营 ⇒ 一回合里可能翻两次。现在不用回合号：只在她自己那一侧（按 `__args` 里的 `CombatSide` 与 power 持有者的 `Owner.Side` 比对）读一个布尔位再取反 —— 初始 true ⇒ 第 5 回合结算、第 6 跳过、7 结算……预览（回合结束那一排）读的是同一位，所以预告和真实完全一致。②「打击 7 和 不惧妖邪 的预览还是没有翻倍」：日志显示**剧本规划路**已经烤进 ×2（`剧本姿态：WATCHER_STRIKE_P 造意图时 ×2 ⇒ 姿态倍率（意图层）：管道值 7 × 计划 2 ÷ 此刻 1 ⇒ 14`），但另一条**通用规划路** `BossPlanner.Plan` 从来不带姿态倍率 ⇒ 它造出来的意图烤进去是 1，玩家回合里看到的仍是 7。现在那条路也按出牌顺序推演（`IntentsFor(stanceMul:)` + `PlannedPlay.StanceMul`），两条路一致。)

### v0.2.697 

2026-11-16 (**姿态倍率改成「造意图时烤进意图对象」+ 斋戒/神格文案订正** —— ①用户截图（打击 6+金刚杵=7、打完暴怒再打它还是 7）：「你改了个寂寞」。前两版都在**渲染时**去找「计划倍率」（先按卡实例查字典 ⇒ 再按 `boss.Plan` 现推），而这两个来源在玩家回合里都可能拿不到（重规划换牌实例、`Plan` 已清）⇒ 一律退回「此刻姿态」＝没翻倍。现在改为**在造意图那一刻**把倍率烤进意图对象：`BossIntentBuilder.IntentsFor` 入口 `StanceBake = stanceMul`（线程静态）+ `LiveSingle/LiveMulti/LiveDelegateSingleAttackIntent` 构造时读走，渲染时只做一次换算 `管道值 × 计划 ÷ 此刻`（她此刻已在愤怒 ⇒ 不动手，绝不双份）；`FinalDamageFor` 里那层渲染时补差已删掉，避免双重。诊断日志：`剧本姿态：<牌> 造意图时 ×2（此刻她 ×1）` / `姿态倍率（意图层）：… 管道值 7 × 计划 2 ÷ 此刻 1 ⇒ 14`。②文案（用户要求，16 语言全改）：斋戒「（只在 5、7、9、11… 这些回合结束时结算）」整段括号删掉；警告上方那一个空行删掉；警告下面再加一行「欧米伽：在你的回合结束时，对所有敌人造成50点伤害。」；神格观的神格说明与上方正文之间那个空行也删掉。顺带修掉英文里 `or Vapi Loyalty` 被重复 3 次的拼接 bug。)

### v0.2.696 

2026-11-16 (**Ω观三条（用户第二轮实测）** —— ①「第四回合的打击和第八回合的不惧妖邪的伤害预览还是没有翻倍」：前两版都把「计划倍率」当**一次性数据**往下传（先乘牌面 → 后来改成登记进字典），只要有一条重算路重新规划剩余手牌时没带上它，登记就被覆盖回 1 ⇒ 玩家回合里看到的仍是没翻倍的 7。现在**不再存任何东西**：渲染那一刻用 `BossIntentBuilder.PlannedStanceAt(她, 这张牌)` 拿「她此刻的真实姿态」+ 剩余计划顺序现推（暴怒/渐强 ⇒ ×2、安宁/冥想/警惕 ⇒ ×1、化体为空/化拳为空 ⇒ ×1），再由 `BossDamagePredictor.ApplyPlannedStance` 把 `值 × 计划 ÷ 此刻` 补在**过完伤害管道的结果**上，谁重新规划都不会丢。②「欧米伽的伤害预览没有再出现，应该在 7、9、11 等回合也出现」：反编译 `Watcher.Code.Powers.OmegaPower.AfterSideTurnEnd` 里伤害直接取 **`Amount`**（「欧米伽」这张牌是 `WithPower<OmegaPower>(50, 10)`），而我上一版读的是 `MagicNumber`（那是点穴的变量名）⇒ 抛异常后被兜底吞掉，只有「她这回合正好要打欧米伽」时才显示。现在按 ① power 的 `Amount` ② power 上的 `MagicNumber` ③ 计划里那张欧米伽的 `PowerVar<OmegaPower>` 牌面 三级取。③「更改冥想的捞牌，让它一定会捞到点穴」：`BossCardSelector` 对发起者是「冥想」的选择加一条**最高优先**规则 —— 候选（弃牌堆）里有「点穴」就先拿它，再走原有点名/兜底。另：本机 `C:\\Program Files\\dotnet\\sdk` 整个消失（只剩 9.x 运行时）⇒ 构建改用项目自带的 `tools\\dotnet\\dotnet.exe`（10.0.401），`install-mod.bat` 本来就用它。)

### v0.2.695 

2026-11-16 (**Ω观：姿态倍率改成「过完伤害管道之后再补差」+ 欧米伽意图挪到回合结束那一排** —— ①用户 2026-11-16：「愤怒是伤害*2 不是额外加一个基础伤害」。以前是把**牌面基础值**乘上「计划姿态 ÷ 此刻姿态」再交给意图，而伤害管道里的力量/虚弱/易伤**不会跟着翻倍** ⇒ 愤怒下「打击 6 + 力量 8」显示 20、真实 28。现在只登记「这张牌打出时的姿态倍率」（`BossDamagePredictor.SetPlannedStance`，按卡实例存），由 `FinalDamage` 在渲染那一刻把差值补在**过完管道的结果**上（`值 × 计划 ÷ 此刻`；只在她本人出手时、只在计划倍率更高时动手，绝不双份）。②欧米伽的意图不再挂在欧米伽那张牌下面，而是像炸弹 / 冰雹风暴那样画在**回合结束那一排**（「斋戒」⇒ 只在奇数回合 5/7/9/11… 显示），与手里有没有那张牌无关。)

### v0.2.694 

2026-11-16 (**点穴的意图变回「攻势 + 策略」** —— 用户截图：点穴下面只有一个蓝色强化箭头。根因是我在 v0.2.691 清理重复日志时，把那段里**真正 `intents.Add` 的两行**一起删掉了，只剩日志 ⇒ 意图退回 `intents.Count == 0` 兜底的 BuffIntent。现在补回：攻势（= 目标已有印记 + 本回合前面那张的印记 + 这张施加的印记，走 RawAttackIntent，不吃力量/虚弱/易伤、可被格挡）+ 一条策略。)

### v0.2.693 

2026-11-16 (**Ω观第 10 回合：第一张点穴进阶 9 起升级** —— 用户 2026-11-16：「让第 10 回合的第一张点穴在进阶 9 被升级，第二张不变」；两张仍是剧本写死的 0 费，第 6 回合那张点穴的进阶 9 升级不变。)

### v0.2.692 

2026-11-16 (**Ω观第 11 回合：点穴 → 贪婪（不出）** —— 用户 2026-11-16：「第 11 回合的那张点穴换成贪婪（不出）」。该回合能量随之 4 → 3（化体为空 1 + 欺瞒现实 1 + 平安 1），贪婪按 `HoldInstead` 留在手里不打；顺带「进阶 9 升级第 6 / 11 回合的点穴」就只剩第 6 回合那一张。)

### v0.2.691 

2026-11-16 (**Ω观四条（用户截图）**：① 同一回合里**先打暴怒再打打击**时后者少算一半（第 4/7/8/12 回合）—— 姿态倍率以前读的是「此刻」的姿态，现在规划期按剧本顺序推演并存进 `PlannedPlay.StanceMul`，重算时按「计划倍率 ÷ 当前倍率」只补差值（`stanceMul`/`pendingMarks` 两个新参数在规划期 / `BladesGrowth.RecomputeAll` / `RefreshPlannedIntents` 三条路都传）。② 欧米伽的伤害没像炸弹那样显示 —— 规划期那张牌还没打出去、`OmegaPower` 不在她身上 ⇒ 退回读**牌面的** `MagicNumber`。③ 第 8 回合没打攻击牌、她仍留在愤怒时第 9 回合的打击预览不对 —— 同 ①（计划期起点姿态 = 她此刻真实姿态，逐张推演）。④ 点穴的伤害没算「本回合前面那张点穴已叠的印记」—— 规划期累加 `plannedMarks` 并传给意图。)

### v0.2.690 

2026-11-16 (**玩家反馈：辉星储·恐惧模式第 4 回合的七星伤害预览只显示一个 7** —— 反编译 `Models.Cards.SevenStars`：`CanonicalVars` 是 `DamageVar(7, Move)` + **`DynamicVar(HitCount, 7)`**，`OnPlay` 走 `Attack(Damage).WithHitCount(DynamicVars.HitCount.IntValue)` ⇒ 段数存在 **`HitCount`** 变量里而非 `Repeat`，我们只读 `Repeat`/`CalculatedHits` 就永远算 1 段。`BossDamagePredictor.Hits` 现在也读 `HitCount`（仅当它 > 现有段数）。普通模式第 4 回合她星不够打不出七星，所以一直没暴露；恐惧模式把隐秘藏品+ 提到第 3 回合、七星真打出来了才看见。)

### v0.2.689 

2026-11-16 (**Ω观三条**：① **点穴无效果** —— 日志 `施加能力失败（MarkPower）: Object of type Creature cannot be converted to IEnumerable<Creature>`：`PowerCmd.Apply` 有**两个** 6 参数泛型重载（单体 `Creature` / 群体 `IEnumerable<Creature>`），以前只按参数个数挑第一个、正好命中群体那个 ⇒ 改成按**第 2 个参数类型 = Creature** 精确挑（`GainMantraAsync` 的 B 版那处一起修）。② **第 3 回合的点穴零费** —— `ScriptCard.CostOverride` 以前只影响规划期记账，牌面仍是 1 费；现在 `BuildHand` 里把它真正落到本场战斗的牌费上（`EnergyCost.AddThisCombat`，日志 `剧本改费：…`）。③ **愤怒姿态下攻击牌预览没翻倍** —— 姿态倍率（愤怒 ×2 / 神格 ×3）只在真实结算里由她身上的 power 生效，意图是自己按牌面算的 ⇒ `BossIntentBuilder` 按**此刻姿态**给攻击牌预览乘上倍率（日志 `姿态倍率：…`）。)

### v0.2.688 

2026-11-16 (**立绘是愚者·真根因**：`Def` 对带 `PinnedVariantKey` 的怪物类返回 `DefSource.WithVariantPin(key)` 的**副本**并缓存，而 `RefreshWatcherCharacterType()` 只改 `BossDefs.Watcher` 这两个**正本** ⇒ 副本里那个「愚者占位」永远没人改（日志 11:16:41 铁证：`Ω观：角色类型已换成 WatcherMod.Watcher` 紧跟着 `WATCHER_DIVINITY 立绘…：fool.tscn`）。现在在 `VisualsPath` 算路径之前，把**这只怪物自己的 `Def.CharacterType`** 也换成真观者角色（并打一行 `… 的 BossDefinition 副本角色类型 → Watcher`）。)

### v0.2.687 

2026-11-16 (**立绘真的是愚者** —— v0.2.686 的慢车道重试不够：`VisualsPath` **只取一次并缓存**，而那一取常发生在慢车道解析之前（用户日志 11:12:46：立绘 fool.tscn 在 13702 行、角色类型换成 Watcher 在 13833 行，同秒但更晚）。现在在 `CharBossMonster.VisualsPath` 里、**算路径之前**当场调用 `RefreshWatcherCharacterType()`（仅当 `Def.Key` 以 `WATCHER_` 开头且还是愚者占位符），彻底去掉对时序的依赖。)

### v0.2.686 

2026-11-16 (**修「整场 Boss 立绘是愚者」** —— 用户实测 Ω观 打出来是愚者的模型：`BossDefs.Watcher.CharacterType` 是**愚者占位**，靠 `RefreshWatcherCharacterType()` 运行期换成真的观者角色，而它只在 `BossDefValidator` 里跑一次、那一跑常常**早于**观者 mod 程序集加载（`WatcherModPresence` 返回 null）⇒ 占位符原地保留、永不重试（日志 `WATCHER_DIVINITY 立绘用角色模型的 VisualsPath：…/fool.tscn（⚠ 与缺省路径不同）`）。现在 ① 慢车道每帧检查「还是愚者占位就再解析一次」（两个函数都幂等、只在值变了时打日志）；② `WatcherModPresence.Probe()` 改成**只在真的探测到之后才锁**，未装时 2 秒最多探一次（以前第一行就锁死，早探一次没装就永远认为没装）。)

### v0.2.685 

2026-11-16 (**灾厄骨的灾厄牌补攻势意图 + 警告文案补「瓦皮的忠诚」**：① 灾厄牌额外画一条「等同于会施加的灾厄层数」的攻势意图 —— 死亡使者（群体）/ 鞭打（单体，只给被锁定玩家看真实数字）/ 无处可逃（单体，按用户口径把本回合其它灾厄牌会施加的层数也累计）；三者都不吃力量/虚弱/易伤 ⇒ 走 `RawAttackIntent`；**荒疫打击跳过**（它的灾厄 = 它自己的伤害，已有攻势意图就是那个数，再画一条会让薄荷尖塔双算）。⚠ 这条必须在 `intents.Count == 0` 兜底之前加，否则三张技能牌会被补一条「强化」。② 所有「带『为你而死』的召唤物不会替你承受该伤害」的警告，16 语言全部改成同时点名「**瓦皮的忠诚**」（107 版本共用同一份文案，缺那件遗物时只是多提一句）。此外补上一轮遗留：Ω观相关 10 个新键的 16 语言本地化、设置界面 Boss 列顺序（猎机骨储战→愚者→观者）、两张能力面板、藏品 Ω观 = 阿尔法、观者遭遇角色映射与调试开关下标修复。)

### v0.2.684 

2026-11-16 (**新增三层 2 号观者 Boss「Ω观」**：特殊效果「斋戒」——欧米伽隔一回合触发一次（只在 5/7/9/11… 回合结束结算，`OmegaPower.AfterSideTurnEnd` 前缀接管）、点穴造成的伤害可以被格挡（整条接管 `PressurePoints.OnPlay`，伤害从 Unblockable 换成可格挡的 Unpowered，印记照旧）；12 回合剧本 + 9～12 循环 + 逐回合能量（按用户列的牌反推：3/2/4/5/2/2/3/4/2/2/4/3）；进阶 8 升级如水·心灵堡垒、进阶 9 只升级第 6/11 回合的点穴；遗物 = 至纯之水/涅奥的祝福/永冻冰晶/空鸟笼/舵盘/钗/金刚杵；阿尔法与贝塔生成的贝塔·欧米伽用原版预览界展示；不惧妖邪 buff（图标取 1 代崩坠 `WatcherFearNoEvil84.png`，被锁玩家打出攻击牌 ⇒ 她那记不惧妖邪进「平静」）；意图新增「点穴 = 攻势（目标印记 + 本张印记）+ 策略」「欧米伽 = 炸弹式攻势」。此外：设置界面 Boss 列顺序改成 猎机骨储战→愚者→观者（神格观在上、Ω观在下）；补 `WATCHER_OMEGA`/`WATCHER_DIVINITY` 两条能力面板；藏品 Ω观 = 阿尔法；观者遭遇补角色映射（以前是 -1）；调试开关 `watcher` 下标越界（会退化成易伤战）已修；16 语言新增 10 个键。)

### v0.2.683 

2026-11-16 (**神格观两条**：① 用户订正「那个第二回合我忘记算退平静了，不是bug，让那2费正常加」⇒ 撤掉 v0.2.682 里「进神格时压掉离开平静 +2」的补丁：离开平静的 +2 是她的原版机制（`Calm.AfterRemoved` / `CalmStance.OnExitStance`），我们触发的退姿态与「化拳为空」都照常给 ⇒ 从平静进神格当回合共 +5（2+3），不在平静就只有 +3。② **进阶 8 的升级目标改成「警惕 / 旋身 / 欺瞒现实」**（不再是「摆手」）：警惕（第 1/8 回合）、欺瞒现实（第 4/11 回合）、旋身（第 5/9 回合）三张牌按 `UpgradeMinAscension = 8` 造手牌，摆手恢复未升级。)

### v0.2.682 

2026-11-16 (**神格观两条**：① 「光辉」的伤害预览补上**本场累计获得的真言**（反编译 `WatcherBrilliance.OnPlay` = `Damage.BaseValue + GetTotalMantraGained(owner)`，也就是 `WatcherStatePower.TotalMantraGainedThisCombat`）—— 以前意图只显示牌面 12/16；同时把「她回合开始时手摇鼓 +1 真言」当 pending 补上（那条钩子是我们替她广播的，发生时计划早排完了），玩家本回合每打一张牌 +1 也会随意图重算实时跟上。⚠ B 版（lamali）把真言做进了牌面的计算型伤害里 ⇒ 只对 A 版加，避免双份。② 「平安」被打两次 —— 反编译 `WatcherDeceiveReality.OnPlay` 会**往手牌里再塞一张 `WatcherSafety`**，而剧本第 4/11 回合本来就写着「平安」⇒ 多出来的那张在补打重规划时又按类型被匹配一次；现在把「欺瞒现实」加进 `BlocksGeneratedCardsEntries`（拦掉它印的那张 token，它自己的 4 点格挡照旧）。)

### v0.2.681 

2026-11-16 (**神格观三条（用户第二轮实测）**：① 第一回合的灯笼加费要**提前算进预算** —— 以前靠原版 `Lantern.AfterSideTurnStart` 到账，而它在她回合开始**之后**才落地，计划早在玩家回合就算死了 ⇒ 面板显示「只打打击」、实际她打完打击又补打警惕；现在预算里 +1（规划期就看到 3 费 ⇒ 正确计划是「只打许愿」），同时把原版灯笼钩子**只对观者**压掉以免双份。② 进神格那一刻不再额外吃「离开平静 +2 能量」—— 反编译 `Calm.AfterRemoved` 是 `GainEnergy(2)`，而进神格会先 `RemoveAllStances` ⇒ 实测「应该 5 费、显示 7 费」；现在只在我们自己触发的「退姿态→进神格」期间压掉那 2 点（她「化拳为空」正常退平静照旧 +2）。③ 满 10 层真言的结算**改由我们自己做**（压掉观者模组的自动 `EnterDivinity`）：扣 10 层 → 先退出当前姿态 → +力量（普通 2 / 恐惧 3）→ 再进入神格（原版自己 +3 能量）—— 修「她已经处在神格时再满 10 层拿不到力量与加费」。)

### v0.2.680 

2026-11-16 (**神格观五条**：① 能量 —— 用户「你别给她补，用她原版的加费就行」⇒ 删掉我们自己补的两笔（`BossDevotionPower` 进神格立刻 +3、`_pendingEnergy` 下回合再 +3），原版 `Divinity.AfterApplied` 本来就是 `PlayerCmd.GainEnergy(3)` ⇒ 第 1 回合 3 / 之后每回合 2 / 每次进神格当回合 +3 **且只加一次**；② 进神格后**整块重规划**（新 `ReplanForExtraEnergy`：按此刻真实能量重跑剧本规划）再重排意图重画面板 —— 只拿旧 `Plan` 重算永远多不出牌；③ 聚能成刃生成的灭除之刃被塞进**抽牌堆**（反编译 `(PileType)1` 实证）⇒ `GeneratedExpungers` 改扫全部牌堆并在日志里报「在哪个堆」，预览副本同时抄 `Repeat`（`CreateCard` 造的是原版新牌，段数会回到 1）；④ 灭除之刃段数 = 打完聚能成刃时那张实体牌的 `Repeat`（= 实付 X，升级 +1），记在 Boss 身上 ⇒ 之后剧本第 5/9 回合**新造**的那张牌段数 3/6/9 与真实伤害一致（原来恒为 1 段）；⑤ 观者两张牌 id 双版兼容统一走 `WatcherIdIs`（去掉 `-`/`_` 再比后缀，A `WATCHER_X` / B `WATCHER-X` 都认）。此外**顺手两条**：⑥ 「奇迹」不再走通用的「回能」补偿 —— 反编译证实 `WatcherMiracle.OnPlay` 自己就 `PlayerCmd.GainEnergy(1, Owner)`（Owner = 影子玩家），我们又补 1 ⇒ 实测每个奇迹回 **2** 点、聚能成刃的 X 被抬到 4；现在只对观者这只关掉补偿，X 回到设计值 3。⑦ 观者姿态记账：警惕 ⇒ 进「平静」、化拳为空退出「平静」⇒ 原版 **+2 能量**（用户 2026-11-16 自己实机确认，属她的原版机制），规划期把这 2 点算进预算 —— 否则第 4/11 回合的「欺瞒现实·平安（有费就打）」会先被画成压暗、回合里又被补打出去（用户报的「即使没进神格也被打了」）。)

### v0.2.679 

2026-11-15 (**神格观六条**：① 第 1 回合的奇迹（剧本那张）**去掉**；② 灯笼加费不再重复计（原版遗物钩子自己到账，我把手动 +1 去掉）；③ 能量定义 2→**3**（涅奥的祝福每回合 −1 ⇒ 实际 2，第 1 回合灯笼 +1 ⇒ 3，修「后面回合只有 1 费」）；④ 补打的牌**并进 Plan** 再刷意图（进神格后意图预览终于会重算）；⑤ 聚能成刃/灭除之刃的 id 同时认 `WATCHER_X` 与 `WATCHER-X`（展示与 X 段数都修）。此外 **神格观三条**：① 至纯之水**两版都不拦**了（A 版原本就拦不住、B 版能拦住 ⇒ 统一放开），改为第 1 回合剧本放一张「奇迹（不出）」；② lamali 版的「许愿」也固定选「永生不死」—— 它走本体的 `CardSelectCmd.FromChooseACardScreen`，新增编译期补丁只对「候选项里有 LiveForever 且 owner 是我们的影子玩家」生效；③ 削弱：进神格的力量 3→**2**、恐惧 4→**3**（16 语言文案同步）。此外 **观者 Boss 兼容两个观者模组**：`WatcherCards` 重构成「provider」结构 —— 卡牌按「A 类名 → B 类名 → 模型 id」三级解析（A=`WATCHER_STRIKE_P`/`WatcherMod.*`，B=`WATCHER-STRIKE_WATCHER`/`Watcher.Code.Cards.*`），姿态 API 抹平（A=`WatcherCombatHelper`，B=`StanceCmd` 多一个 ctx 参数），真言/神格按类型名 `Mantra|MantraPower`、`Divinity|DivinityStance` 双认，神格不回退同时压 A 的 `AfterSideTurnEnd` 与 B 的 `BeforeSideTurnStart`，至纯之水印牌同时压 `BeforeCombatStart` 与 `BeforeHandDraw`；**两个都装时优先 3747526116**。此外 **联机不同步修复**：① 血肉骨的「吞噬生命」以前在 Power 钩子里**每台机器各改一次战斗状态**（+格挡 / 补血肉戏法伤害）⇒ 两端分叉（玩家反馈「联机给血肉骨上 debuff 会不同步」，日志 22:23:12 两行之后紧接「⚠ 不同步状态摘要 轮次 2」）；现在**只有房主结算**，客人只记日志；② `BossPicksMessage` 未在 BaseLib 注册导致「勾选同步：广播失败 … KeyNotFound」⇒ 新增 `Core/BaseLibMessageRegistration`（反射清 BaseLib 的消息缓存并重跑 `Initialize`，纯反射不引 BaseLib）。此外 **神格观**：① 布质果实 → **空鸟笼**（削弱；果实的 +31 生命本来就没生效，血量不变）；② 补打之后**重排意图并重画手牌栏**（原来她多打了牌、玩家看到的意图还是旧的）。此外 **神格观**：① 灯笼那 1 点只给一次（`TurnNumber` 0 起，原来 `<=1` 让第 2 回合又给一遍 ⇒ 4 费）；② 进神格后按剧本「有费就打」补打的门禁对观者开放（原来只给灵魂骨/三层第 3 回合）。此外 **神格观**：① 进神格时**立刻**把那 3 点能量加进 `pcs.Energy` 再重排计划（原来只记「下回合」，所以本轮进神格后「有费就打」的牌不亮也不打）；② 特殊效果正文去掉首行那个白色的「虔信」（上面黄色 buff 名已够），神格说明里三个能量图标后面的「能量」删掉（16 语言）。此外 **神格观七条实机修复**：① 真言改成**真的真言**（走观者 `Mantra` power，特殊效果本身不再有层数）；② 灯笼第 1 回合 +1 能量、进神格当回合 +3 能量（后者记在 `TakePendingDivinityEnergy`）；③ 进入神格后刷新意图，于是「有费就打」的牌真的打得出去；④ 第 1 回合剧本去掉奇迹（至纯之水会自己印一张）；⑤ 许愿全难度升级；⑥ 能量口径 第 1 回合 3 / 之后 2 / 每次进神格 +3；⑦ 打出聚能成刃后用原版预览界面展示生成的灭除之刃（段数 = 牌上的 X）。此外 **神格观三条**：① 压掉「至纯之水」开局印的那张奇迹（运行期挂 `WatcherMod.PureWater.BeforeCombatStart`，只对她那件生效；剧本里已有奇迹）；② 特殊效果说明太长 ⇒ 只把**神格观**的说明框上移（复用 `brewSpecialY`，默认 −120，`charbosses_ui.txt` 可热调，其它 Boss 不动）；③ 布质果实 +31 最大生命写进血量（72 → **103**，影子玩家身上那件收不到 `AfterObtained`）。此外 **神格观订正**：初始遗物「圣水」→ **至纯之水**（`WatcherMod.PureWater`）；第 1、2 回合末尾各补一张「奇迹（不出）」（`ScriptCard.HoldInstead`，有费也不打、留手里）；拦掉原版抽牌印牌（`BlocksVanillaDraw` 已在）。此外 **神格观改名 + 地图图标**：内部 id 按别的 Boss 的规律改成 `WATCHER_DIVINITY`（控制台 `fight WATCHER_DIVINITY_BOSS_ENCOUNTER`，不再是笼统的 `WATCHER_BOSS`）；地图节点图标换成**一代崩坠三层神格观**那张（`watcher_divinity` + `_outline`，随 PCK 打包）。此外 **神格观登场**：三层 1 号观者 Boss 进候选表（装观者模组 3747526116 才出现）；虔信特殊效果 / 11 回合剧本 / 神格说明 16 语言 / 许愿固定「永生不死」/ 恐惧支配 / 斗蛐蛐规则 / 聚能成刃 X 段数 / 缺模组换塔罗愚。另修：观者的两处补丁改成**运行期**挂（`Divinity.AfterSideTurnEnd` 是派生类覆写，补基类拦不到）。此外 **适配游戏 v0.113.1 热补丁**：把 `.refs/sts2_111.dll` 换成 0.113.1 的同名程序集后，107 / 111 两个目标仍然 0 错误、零改动通过（这轮热补丁没有动到我们依赖的任何 API）。此外 **滚石替身尺寸**：替身只取了石头贴图自身的 `Scale`，漏了 `NRollingBoulderVfx` 节点上的动画缩放（`_Ready` 里从 1 长到 1.5）⇒ 一出来就巨大；现在 `Scale = 节点动画缩放 × 贴图缩放`。此外 **两个重复**：① 一张牌出现两个强化图标 —— 兜底强化与 `selfBenefit` 各加一条，改为 `DedupeBuffIntent` 只留一条；② 佩尔之泪一回合给 4 费 —— 反编译 `PaelsTears.AfterSideTurnStart` 在影子玩家身上**真的会跑到**，叠在 `PaelsTearsBonus` 预算之上，新增 `BossRelicSuppressPaelsTearsPatch` 只留我们自己那一次。此外 **说明框：改成按框自身判活**。前两次都靠「我们自己的静态引用」去收（锚点被房间先释放 ⇒ Remove 从没调过；补了 QueueFree + BeforeRemovedFromRoom 仍留着）⇒ 新增 `BossSpecialTipRoomGuardPatch`：postfix `NHoverTipSet._Process`，只要这块框是我们创建的、且当前**没有战斗房**就整块 `Visible=false + QueueFree`，不依赖任何引用是否还有效。此外 **说明框真的收了**：`BossSpecialTip.Hide` 以前只按锚点 `NHoverTipSet.Remove(_anchor)` 收，而离开战斗房时锚点先随房间释放 ⇒ `IsInstanceValid` false ⇒ 一次都没收、还把引用清空 ⇒ 框永久留在全局提示层（切地图也看得到）。现在对框本身 `Visible=false + QueueFree` 兜底，并在 `BeforeRemovedFromRoom`（本场没有其它 Boss 活着时）主动 `BossSpecialUi.HideAll()`。此外 **特殊效果永远是 buff 栏第一个**：`FireCombatStart` 原本把 `ApplySpecialPower` 放在最后一步 ⇒ 金刚杵力量/神话形态/遗物 buff 都排在它前面；现在提到流程最前施加（原位置加 `SpecialAlreadyOn` 去重，避免 `PowerCmd.Apply` 叠层），环绕轨道的后续初始化照旧。此外 **群蛇形态不再吃掉牌自己的意图**：附加伤害行（群蛇/开信刀/灰烬/黑洞/速行者）以前先 Add，导致纯能力牌的「强化」兜底意图（`intents.Count == 0` 分支）永远补不上 ⇒ 现在先补牌自己的意图、附加行排在后面；**塔罗愚三条实机修复**：① 特殊效果说明框跟到地图页 ⇒ 离开战斗房就收（`BossUiSession` 的 `id==0` 分支 + `BossSpecialTip.Ensure` 无战斗房即 Hide）；② 滚石滚向反了 ⇒ 原版 `NRollingBoulderVfx` 的几何写死「向右滚」，改为隐藏原贴图 + 每帧镜像替身（只对「目标全是玩家」的那一颗生效，玩家自己的滚石不受影响，伤害仍走原版 `HitCreature`）；③ 瘀伤进伤害预览 ⇒ `PendingBruise` 取消「只在玩家回合补」的闸门（意图是我们自己按牌面算的、不走原版管线），并把 `PreviewIntents` 三条路径（Sly/Burn/普通）都补上；此外 **塔罗费用宝石**：斋戒 / 天人形态 = 观者紫 #9E68FF，斗殴 / 永恒形态 = 亡命之徒黄褐 #CEA477，贴图随本模组一起打包，不装别的模组也成立；此外 **塔罗愚收尾**：倒吊人的额外回合实现在同一个 Boss 回合里（打完前 3 张重跑一次「回合开始」钩子 ⇒ 水银沙漏/恶魔形态各再触发一次，再打高塔/星星/恶魔；原来「14 额外回合」是独立一行导致后面所有回合错位一格）；恐惧模式下`新生之喜`/`教皇`额外多印 1 张「非凡技艺」；瘀伤计入伤害预览（`PendingBruise`，只在他回合开始前补）；回响形态的重打计入意图（群蛇那一条 `4×2`/`6×2`、被重打的力量算两遍）；`愚者`改成按剧本顺序打牌（不再随机）；`虚空形态`不再把他的牌显示成 0 费，`永恒形态`对他彻底空转；`天人形态`的递增能量进预算；5 层观者/Hermit 能力用各自原版图标、4 张塔罗牌用各自原版卡图（都从别的 mod 的 pck 导出并随本模组 PCK 发布）；第 3⇄7、4、6、8 回合牌序按用户口径调整；遗物换成锚；进阶 9 = 金刚杵；调试开关下把玩家最大生命值调到 10000 免得灾厄秒杀测试）

### v0.2.657 

2026-11-14 (**新增三层 1 号「塔罗愚」（愚者）**：特殊效果「愚者」= 玩家获得灾厄时先用格挡抵消等量灾厄（已存在的灾厄不动，走 `ModifyPowerAmountGivenAdditive`）；卡组只有 3 张神话形态 + 16 回合剧本（10～16 循环）+ 8 件遗物 / 进阶 8 石头与奥利哈钢 / 进阶 9 玉之剑插在涅奥的祝福与闪亮口红之间；`BlocksVanillaDraw` 打开；恐惧模式 = 非无色牌全部升级（走 `DreadUpgradesByAct` 运行期通道）；本地化 16 语言 12 键 × 16 = 192 条；仅 0.113 构建，107 照旧编译）

### v0.2.656 

2026-11-14 (**塔罗牌按用户口径校正：天人/永恒 3 费且升级去虚无；斗殴升级 3→5 瘀伤；斋戒 2 费 3/4 力敏 −1 能量**)

### v0.2.655 

2026-11-14 (**新增 4 张衍生牌 + 5 个能力：斋戒/天人形态（观者 mod）、斗殴/永恒形态（崩坠 Hermit）+ 能量降低/天人化/斗殴/瘀伤/永恒形态；为「塔罗愚」准备**)

### v0.2.654 

2026-11-14 (**奇巧猎·命运同担改版：玩家侧 debuff 改成可叠加（层数=下回合开始时弃几张），普通 1 层/恐惧 2 层，图标借用「抑制」**)

### v0.2.653 

2026-11-14 (**临时诊断：追踪真人玩家身上的 WEAK/VULNERABLE/STRENGTH 是谁加的、谁扣的（查「虚弱闪到 2 又回 1」）**)

### v0.2.652 

2026-11-14 (**修：静默猎手的支配给的虚弱多撑一回合 —— PowerCmd.Apply 在敌方回合给玩家上负面会置 SkipNextDurationTick，房间进入时正好算敌方回合；现在清掉该标记**)

### v0.2.651 

2026-11-14 (**地图图标：根除骨 = eradicate_bone、养手骨 = osty_bone 各自的专属图（灵魂骨仍用 soul_bone）；新增贴图已进 PCK**)

### v0.2.650 

2026-11-14 (**攻击特效镜像：反射自动识别「自带方向」的 VFX（Create 里同时有两个 Creature）⇒ 小刀这类永远不翻，以后新增的也自动免疫**)

### v0.2.649 

2026-11-14 (**修：小刀投掷被翻反了 —— 只翻「锚在她身上」的特效（她自己的容器 / 她身边的骨骼粒子），并排除 NShivThrowVfx 等自带方向的**）

### v0.2.648 

2026-11-14 (**震荡波位置：骨骼/粒子特效翻完再往玩家侧平移一倍身位（Hitbox.Size.X），把它挪回储君左边**)

### v0.2.647 

2026-11-14 (**修震荡波位置：改翻贴图 FlipH（原地镜像），并给节点打 meta 防两个入口重复翻；不再退到翻节点本身**)

### v0.2.646 

2026-11-14 (**修：画在她自己身上的特效改成「只翻画面不挪位置」（翻子节点而不是节点本身），否则镜像会把美术偏移一起翻 ⇒ 特效从她左边跑到右边**)

### v0.2.645 

2026-11-14 (**修攻击特效镜像根本没触发：AddChildSafely 太小被 JIT 内联 ⇒ 改补 Node.AddChild + VfxCmd.PlayVfx 双保险；不再按目标跳过（抉择重放的技能牌也要翻）**)

### v0.2.644 

2026-11-14 (**修：攻击特效镜像两处失效 —— targetsSelf 用了群攻牌的 play.Target 兜底(她本人)⇒每张牌都不翻；vfx_flying_slash 场景名不带 Vfx 被筛掉**)

### v0.2.643 

2026-11-14 (**攻击特效朝左：她的牌结算期间把 AddChildSafely 加进来的 *Vfx 节点 Scale.X 取负；轰击储自伤那一手保持原版**)

### v0.2.642 

2026-11-14 (**修：恐惧抉择储第1回合第二挑选了星位序列不出天际钻头 —— 补打顺序反了，改成「先打完第二张抉择，再补打点名牌」**)

### v0.2.641 

2026-11-14 (**抉择储的「指导」恢复知识恶魔界面（NChooseACardSelectionScreen）：候选 >3 张时走我复刻的同步流程绕开 FromChooseACardScreen 的 3 张限制**)

### v0.2.640 

2026-11-14 (**修：抉择储的「指导」用 FromChooseACardScreen（最多 3 张，>3 直接抛）⇒ 5 张候选必抛 ⇒ 变成她自己挑第一张；改用 FromSimpleGrid 网格选牌 + 新本地化键 REGENT_BOSS.choice_prompt（16 语言）**)

### v0.2.639 

2026-11-13 (**修根因：影子玩家不在 _playerCollection ⇒ CardSelectorStack.GetSelector/PushSelector 必抛 ⇒ 自动选牌器一次都没挂上过（49 行挂载失败）；改为在 GetSelector 一层接管，17 个选牌入口一次全好：洁净/夜魇/夜宴/手上技法/隐秘匕首…**)

### v0.2.638 

2026-11-13 (**修 v0.2.636 用错牌：恐惧华丽猎第6回合的「精准+」应是 Accuracy，我误写成 PreciseCut 精确切击（那是炼药猎第5回合的牌）**)

### v0.2.637 

2026-11-13 (**修：影子玩家的 CardSelectCmd.FromHand 也返回空（夜魇复制/手上技法/夜宴）⇒ 卡面停空中；加 BossHandSelectionPatch**)

### v0.2.636 

2026-11-13 (**dread: 华丽猎第6回合 尖啸 → 精准+**)

### v0.2.635 

2026-11-13 (**修：影子玩家的 FromHandForDiscard 弃了 0 张 ⇒ 奇巧不触发 + 卡面停空中；改为在调用点接管**)

### v0.2.634 

2026-11-13 (**文案：一/两 → 1/2（含普通模式），82 条**)

### v0.2.633 

2026-11-13 (**亡灵契约师的支配：稳定挑牌（不因 SL 变）+ 幂等不叠加**)

### v0.2.632 

2026-11-13 (**修：亡灵契约师的支配用 AddThisCombat(+1, true) 被 reduceOnly 吞掉 ⇒ 改 AddThisTurn(1)**)

### v0.2.631 

2026-11-13 (**修：亡灵契约师的支配只从可打出的手牌里挑，别挑诅咒/状态**)

### v0.2.630 

2026-11-13 (**角色人称规范化：铁甲/储君=他、静默/亡灵=她、故障=它、愚者=杝/它**)

### v0.2.629 

2026-11-13 (**修 v0.2.628 事故：powers.json 里 SYNTHESIS_DREAD_POWER.description 被写入裸换行导致语言文件解析失败、游戏起不来**)

### v0.2.628 

2026-11-13 (**修：恐惧模式特殊效果说明框读的是 powers 表 ⇒ 给 32 个 power 补 description_dread**)

### v0.2.627 

2026-11-13 (**支配 buff 图标 = 对应角色头像；不属特殊效果，不用好奇图标**)

### v0.2.626 

2026-11-13 (**支配改成 buff 实现：故障机器人/亡灵契约师 挂 power、储君 = 敌人身上王之凝视、新增愚者的支配**)

### v0.2.625 

2026-11-13 (**新增两种意图：诱惑(TEMPTATION,策略图标翻转) + 审判(JUDGEMENT,灾厄图标，秒杀)**)

### v0.2.624 

2026-11-13 (**dread: 灵魂骨 soul_bound_dread 文案补齐 —— 34/34 全覆盖**)

### v0.2.623 

2026-11-13 (**dread: 辉星储换牌/灾厄骨去续命/轰击储T1轰击/碎屑储回退/仆从储不足1+16/灵魂骨回合抽牌/无色储超质量/痛击吃打击**)

### v0.2.622 

2026-11-13 (**dread audit: 涂毒贼回退可格挡+双结算 / 巨石战回退1力3甲 / DreadUpgrades 通道: 黑球机眼部攻击+王储王者之拳**)

### v0.2.621 

2026-11-13 (**dread: 能力机 衍生能力牌也加集中**)

### v0.2.620 

2026-11-13 (**dread: 根除骨门槛6 / 铸剑储不减铸造可多次 / 碎屑储碎屑进堆顶**)

### v0.2.619 

2026-11-13 (**dread: 刀猎4刀/血肉骨6格挡+戏法+/虚无骨开局 /王储王之凝视**)

### v0.2.618 

2026-11-13 (**fix: 瓶中精灵复活改成判死那一刻同步丢药水+同步回血**)

### v0.2.617 

2026-11-13 (**dread: 华丽猎 压制 + 昏鸣 / 抉择储 指导 双挑**)

### v0.2.616 

2026-11-13 (**dread: 炼药猎 = SerpentForm upgraded; 调制 unchanged (rollback v0.2.615)**) / 2026-11-13 (**dread: 炼药猎 调制 block absorb free**) / 2026-11-13 (**dread: 涂毒贼 poison unblockable**) / 2026-11-13 (**dread: 蛇眼猎 Murder scales on any draw**) / 2026-11-13 (**dread: 蛇咬猎 player shuffle 3 Snakebites**) / 2026-11-13 (**dread: 奇巧猎 命运同担 discard 2**) / 2026-11-13 (**dread: 巨石战 turns 1/7 extra Giant Rock+ (Enthralled -1 cost)**) / 2026-11-13 (**dread: Giant Rock doubled strength/plating**) / 2026-11-13 (**dread: 易伤战 主宰 2 weak + 2 frail**) / 2026-11-13 (**dread: 杂耍战 player strikes boost Perfected Strike**) / 2026-11-13 (**dread: 放血战 heal includes blocked damage**) / 2026-11-13 (**dread: 打击战 痛击 +1 per strike card**) / 2026-11-13 (**dread: 愤怒战 Orichalcum relic**) / 2026-11-13 (**dread: 事件机 turns 4/9 extra FeedingFrenzy+**) / 2026-11-13 (**dread: 能力机 免疫偏差认知**) / 2026-11-13 (**dread: 电球机 暴风雨+ at turns 3/10 + intent math**) / 2026-11-13 (**dread: 电球机 引雷针 3->5 cards, once per turn**) / 2026-11-13 (**恐惧批次：黑球机恐惧版 —— 冷光两张变手牌一张**) / 另含 2026-11-13（**恐惧批次：状态机恐惧版（同名「状态机」，效果与现状一致）**）／另含 v0.2.597 / 2026-11-13（**恐惧批次 4/5：彩虹机恐惧版（每 5 球 +2 集中）** —— 按用户规则「恐惧模式沿用普通模式的名字」，落地第 2 只：彩虹机「**彩虹**」。正常模式每 5 个充能球 +1 集中；恐惧模式 **+2**（`BossRainbowPower` 里按 `DreadMode.IsActive` 选 `focusGain`，Power 仍是同一个类，所以 `SpecialPowerDread` 不需要另指），文案 `DEFECT_BOSS.rainbow_dread`（16 语言，标题「彩虹」与正常模式逐字相同，正文 + 玻璃充能球的无来源警告）；`BossDefs` 的 `DEFECT_RAINBOW` 变体上填 `SpecialLocDread`。另含 v0.2.596 / 2026-11-13（**恐惧批次 3/5：第一只恐惧版特殊效果（物理机·人工合成 3→5）** —— 用户定的规则：「每个 boss 都用特殊效果，也都有特殊效果的名字；恐惧症模式下所有特殊效果都用和普通模式一样的名字」⇒ 恐惧版 = **同名**、效果更强。本轮落地第 1 只：物理机（`DEFECT_BOSS` 第二幕）「**人工合成**」——正常 3 层人工制品，恐惧模式 **5 层**：新增 `Powers/SynthesisDreadPower`（照 `SynthesisPower` 那套 `AfterApplied` 给 `ArtifactPower`，只是层数 5），并在 `DEFECT_BOSS` 的 `SpecialLocDreadByAct[1]` / `SpecialPowerDreadByAct[1]` 上接线；文案 `DEFECT_BOSS.synthesis_dread`（16 语言，标题行与正常模式逐字相同）。下一轮继续按角色推进其余 27 只。另含 v0.2.595 / 2026-11-13（**恐惧批次 2/5：辉星储升级黑洞 + 辉星收支核算** —— ①第 1 回合的「黑洞」改成**升级版**（用户「升级黑洞」）。②按用户要求核算「第 4 回合能不能打出七星」，反编译 0.113 取到的收支是：起始 3 星（天赋君权 `StarsVar(3)`）、辉光+ **+2**、汇流+ **下回合 +2**、太阳打击+ **+1**、隐秘藏品+ **+1**、其余（万向斩/王者之拳/剑雨/星界脉冲/花样百出）**无星费也无得星**，而七星的星费是 `CanonicalStarCost = 7` ⇒ 到第 4 回合手上是 **3+2+2+1+1 = 9 ≥ 7**，**七星本来就打得出**。所以「替换第 3 回合防御 / 第 4 回合隐秘藏品+」这两处**没有按原口径实施**，等用户确认是否仍要换（换的话还需要指定换成哪张）。另含 v0.2.594 / 2026-11-13（**恐惧批次 1/5：恐惧版特殊效果的地基 + 4 处普通模式改动** —— ①地基：`BossDefinition` 新增 `SpecialLocDreadByAct`/`SpecialPowerDreadByAct`、`BossVariant` 新增 `SpecialLocDread`/`SpecialPowerDread`，`SpecialLocForAct`/`SpecialPowerForAct` 在 `DreadMode.IsActive` 时优先取恐惧版（取不到退回正常版）⇒ 后续每只 Boss 只需补「恐惧版 Power + 两个字段 + 16 语言文案」。②刀猎第 4 回合：幻影之刃 → **精准+**（0 费；幻影之刃在 0.113 已被削到 8 点）。③养手骨第 5 回合：第一张来生 → **钙化+**（取消 v0.2.367 那张第二张来生，能量账仍是 3）。④蛇眼猎：普通模式特殊效果（`SILENT_BOSS.well_laid`）按用户要求补上「抱抱先生属于无来源伤害，带『为你而死』的召唤物不会替你承受」的警告（16 语言）。⑤仆从储「俯冲仆从」**无需改动**：0.113 已把 `MinionDiveBomb` 从 13 削到 **11**，而 `BossDefs` 里进阶 9 本来就 `UpgradeValueBy(2)` ⇒ 正好 11 / 进阶 9 起 13，与用户口径一致（已核对反编译）。⑥辉星储（升级黑洞 + 换第 3/4 回合让第 4 回合七星出得来）留到下一轮 —— 需要先精确算出各卡的辉星收支。另含 v0.2.593 / 2026-11-13（**恐惧模式（Dread）适配第一步：5 个角色支配 + 遭遇接线** —— 用户「适配恐惧模式，添加对应支配（不影响其他模式）」，并确认按「本幕 Boss 是哪个原版角色」挂。反编译确认机制：`DreadRunModel.ApplyCurrentDominion()` 取 `Act.BossEncounter ?? SecondBossEncounter` 的 `EncounterModel.GetDominion()` 塞进 `RunState.Dominion`，进 Boss 房时由 `RemoveDominionIfRoomIsBoss` 清掉 ⇒ 支配只在**本幕普通房间**生效；我们替换了每一幕 Boss 遭遇 ⇒ 以前恐惧模式会报 `... which doesn't have any dominion set!`。本轮：①新增 `Core/CharDominions.cs`：`DreadMode.IsActive`（`GameMode.Dread`）+ 5 个 `DominionModel` 子类 —— 铁甲战士（战斗开始给所有敌人 1 层恶魔形态）、静默猎手（战斗开始玩家 −1 力量 +1 虚弱）、储君（敌人攻击你 ⇒ 用原版 `MonarchsGazeStrengthDownPower` 本回合 −1 力量）、亡灵契约师（玩家回合开始随机一张手牌本回合 +1 费，走 `EnergyCost.AddThisCombat`）、故障机器人（玩家回合结束按敌人数 ×3 点伤害）；②`CharBossEncounterBase.GetDominion()` 按 `BossKeys.CharacterOf` 返回对应支配；③新增 16 语言的 `content/localization/<lang>/ascension.json`（`DREAD.<角色>.description`，与游戏 `ascension` 表的键式一致）。恐惧模式是 0.113.0 才有的类型（107 的 `GameMode` 里没有 Dread）⇒ 全部支配代码与 `GetDominion()` 覆盖都只在 `STS2_111` 构建里。注：28 只 Boss 的恐惧版特殊效果（同名加强）与 4 处普通模式剧本/数值改动留到下一轮。）另含 v0.2.592 / 2026-11-13（**文案「第九回合」→「第9回合」+ 幻影之刃削弱核对** —— ①用户要求把特殊效果文案里的中文数字改成阿拉伯数字：`BOSS_SUPPRESSION_POWER.description` / `.description_v113` / monsters 的 `SILENT_BOSS.suppression` 三处、zhs+zht 共 6 处「第九回合」→「第9回合」。②0.113.0 把「幻影之刃」从 9（升级 12）削到 **8（升级 11）**（反编译新 `PhantomBlades.CanonicalVars` = `PowerVar<PhantomBladesPower>(8m)`，`OnUpgrade` 仍 +3）。核对结论：**我们的意图预览没有任何写死的数字**，两处来源都是实时读 —— 已挂在身上时走 `PhantomShivs.AmountOf` → `GetPowerAmount<PhantomBladesPower>()`；还在手里时走 `BladesGrowth` 的 `VarInt(card, 『PhantomBladesPower』)` → 牌面 `DynamicVars` ⇒ 新版本上刀猎的第一把小刀预览会自动少 1 点，无需改逻辑；顺带确认 `Shiv` 本身仍是 4 点伤害（`ShivBaseDamage = 4` 依旧正确），并把这几个注释里过时的「9 / 12」标注成「0.111/107 的旧值」。）另含 v0.2.591 / 2026-11-13（**华丽猎特殊效果描述补首行** —— 用户给了新文案：「静默猎手将在第九回合首次打出华丽收场。每当静默猎手打出华丽收场时，将一张虚空放入所有玩家的抽牌堆顶部。警告：静默猎手的铭记死亡会攻击生命值最少的玩家（忽略召唤物），而不一定是被她锁定的玩家。」⇒ 三处文本都补上首行「第九回合首次打出华丽收场」：powers 表的 `BOSS_SUPPRESSION_POWER.description`（107 用）、`.description_v113`（0.113 用，仍在末尾保留警告）与 monsters 表的 `SILENT_BOSS.suppression`，16 种语言全齐；本地化检查通过。）另含 v0.2.590 / 2026-11-13（**适配 0.113.0 + 铭记死亡重做** —— 游戏今天更新到 v0.113.0（commit 16ac0fe3），原版把「铭记死亡」重做成：3 费、**奇巧**、`TargetType.RandomEnemy`、「对**生命值最低的敌人**造成 19(23) 点伤害」（反编译 `MementoMori.CanonicalVars` 只剩一个 `DamageVar(19m, Move)`，`OnUpgrade` +4 ⇒ 23；旧的「每弃一张 +4」整段删除）。据此改：①**0.113 的 API 适配**（编译实测）：`MultiplayerScalingModel` 从 `Models.Singleton.*` 挪到 `Models.*`；`RelicModel.IsAllowed` 改成非 virtual（新版覆盖 `IsAllowedInternal`）；`CardSelectCmd.PushSelector / SuspendSelectorForTest` 删除 ⇒ 新增 `Core/SelectionCompat`（新版走 `RunManager.Instance.CardSelectorStack.PushSelector(player, selector)`，摘选择器改成压一个 null）。②**奇巧猎**：卡组移除铭记死亡（用户：「本来现在也不在剧本中出现」）。③**华丽猎第 2 / 9 回合**（仅 `STS2_111`，107 仍是旧意图 —— 用户：「适配旧版本的部分不更改华丽猎的意图」）：第 2 回合改成 肾上腺素、夜魇+（复制杂技）、杂技（弃战术大师）、战术大师（奇巧）、隐秘匕首（弃防御**和铭记死亡**）、铭记死亡（奇巧免费打出）、防御，不出：防御（被弃）、防御（保留没费）、华丽收场（保留，抽牌堆非空）；第 9 回合改成 杂技（弃铭记死亡）、铭记死亡（奇巧）、中和、防御、防御、华丽收场，不出：打击（被必备工具丢弃）、防御 / 打击（保留没费）。④**多人**：新增 `Patches/MementoMoriLowestHpPatch`（前缀接管 `MementoMori.OnPlay`，只对我们的 Boss 生效）—— 目标改成 `BossLockOn.LowestHpRealPlayer`（**只数真人玩家、忽略奥斯提等召唤物**），仍走原版 `DamageCmd.Attack(...).FromCard(...).Targeting(...).WithHitFx(...)` 管线；意图侧 `BossIntentBuilder` 对 `MEMENTO_MORI` 改按「生命值最低的玩家」清零（那只玩家看得到伤害、其他人 0），其它牌仍按锁定关系（用户：「其它牌仍以锁定的玩家为目标，别影响其它牌和boss」）。⑤**特殊效果文案**：`BossSuppressionPower.Description` 在 0.113 构建里改读新键 `BOSS_SUPPRESSION_POWER.description_v113`（原文 + 空行 + 警告），16 种语言已补齐。⑥`BossDamagePredictor.MementoMoriDamage` 在 0.113 构建里直接读牌面伤害。) 另含 v0.2.589 / 2026-11-12（**彗星改成原版「失去生命」+ 血肉戏法意图不再显示成群体** —— 用户实测两条：①「碎屑储的特殊效果让玩家打出碎屑可以让所有敌人失去10生命…但你是不是直接在代码层面把他们的血减少了10？他们都没有受击动画，而且所有奥斯提（包括血肉骨用骸骨军团召唤出来的）血都减成0了还在那站着。这里应该用原版的减少生命方法」—— 根因：`CometBossPower.AfterCardPlayed` 里直接调 `Creature.LoseHpInternal(...)`，反编译那个方法只有三行（`CurrentHp = Max(CurrentHp-num,0)` + 返回 DamageResult），**只改数字**：不播受击动画、不弹伤害、不跑 `WasTargetKilled` 的死亡处理 ⇒ 奥斯提被扣到 0 还站着。现在改成原版「失去生命」的公共出口：`CreatureCmd.Damage(ctx, 所有存活敌方, 10, Unblockable|Unpowered, dealer=null)`（和 `PoisonPower.Trigger` 同一套写法，不吃格挡/力量/易伤，但受击/死亡流程完整）。注意用 **5 参数重载**（107 的 `CreatureCmd.Damage` 没有 7 参数版，编译实测 CS1501）。②「血肉骨所有的血肉戏法伤害似乎都被错误的显示成群体伤害，但实际上血肉戏法本身是单体的，而当她使用苦难时才会变成群体（除了被亡灵契约师锁定的玩家外显示苦难基础伤害+被她锁定玩家的负面效果种类*9，被她锁定的玩家只显示苦难的基础伤害。记得考虑在本回合刚施加的负面效果）」—— 以前 `hasDebuff` 就无条件 `new RawAttackIntent(sleight)`，每个玩家的手牌栏都显示自己会挨 ⇒ 像群攻。现在：单体减益牌只有**被锁定的那位**看得到（`LiveDelegateSingleAttackIntent` 逐帧判 `BossLockOn.TargetFor`），群体减益牌（`TargetType.AllEnemies`）才显示给所有人，每笔 = 该牌负面**种类数** × 血肉戏法（原版触发点是 `AfterPowerAmountChanged`，一种类型一次）；苦难（MISERY）那一笔**并进牌面伤害**成**一个数**：`MiserySleightPerHit` 分支用 `FinalDamage(牌面) + 种类数×9`，只给没被锁的玩家加，被锁玩家只显示牌面；种类数改走新的 `FleshBone.LockedPlayerDebuffKinds(boss, upToCard)` —— 除了当前身上的负面，还把「她本回合还没打出、但排在苦难之前的牌会给玩家上的负面」按类型一并计入（用户「记得考虑在本回合刚施加的负面效果」）。) 另含 v0.2.588 / 2026-11-11（**兼容「CVC-跨版本兼容补丁」** —— 用户「本模组似乎和 CVC-跨版本兼容补丁 冲突。进行兼容」。CVC 是加载前先跑一遍 Mono.Cecil 的 IL 改写器，它会把 mod 目录里**每一个 .dll** 都当成一个模组实现去做版本指纹（报告写到 %APPDATA%/SlayTheSpire2/cross_version_compat_report.txt）。它报告里我们这条的坏味道是：`CharacterBosses 99/100 [clean]`、0 处 findings/repairs/will-throw，但目标版本被推断成 `0.107 [medium] … older→newer`。根因是我们双版本外壳同时带 Impl107/Impl111 两份实现，扫描器把**休眠的那份 107** 也读进去了 ⇒ 它一边把整个模组判成「目标 0.107」，一边准备去改写一份根本不会被加载的 DLL。三处改动：①两份实现改以 `CharacterBosses.Impl107.dll.bin` / `.Impl111.dll.bin` 的名义发布 —— `loader/Loader.cs` 新增 `FindImpl`，按「.dll → .dll.bin」顺序找，`AssemblyLoadContext.LoadFromAssemblyPath` 不看扩展名所以行为完全等价；扫描器从此看不见它们。`install-mod.bat` / `tools/pack-dist.ps1` 同步改名，并**主动删除**旧版留下的 `.dll`（否则升级后两份并存、又被扫到；打包护栏也认两种扩展名）；②`PowerModel::get_PackedIconPath` / `get_ResolvedBigIconPath` 两个 postfix 改成**非破坏性** —— 先看原返回值，只有它为空或资源不存在时才写我们的候选路径（这两个是报告里和 BaseLib / RitsuLib / SteamRandomMatchMod 共用的 patch 点，以前无条件覆盖会顶掉排在前面的那家的图标）；③清单补 `min_game_version: 0.107.1`，版本过低的游戏直接拒载而不是崩。注：CVC 的 IL 改写本身没有改坏我们的代码（0 findings / 0 repairs / 0 will-throw），这次改的是「别让它误判我们」）另含v0.2.587 / 2026-11-11（**BaseLib 从硬依赖改成软依赖** —— 玩家反馈：①「把其它模组都去掉以后进第一场战斗就崩」= 把前置 BaseLib 一起删了（清单原来把 BaseLib 声明成 `dependencies`，且 `BossPicksSync` 直接 `using BaseLib.Abstracts`）；②「装完后主界面按钮全没了」= 类型加载期 `ReflectionTypeLoadException` 发生在 Harmony 之前，我们的 catch 拦不到。改动：①清单去掉 `dependencies`；②新增 `Core/BaseLibPresence`（探测 BaseLib 在不在），所有碰 BaseLib 类型的代码先问它、且真正碰类型的部分放进 `NoInlining` 方法（否则 JIT 调用方时就抛 TypeLoadException）；③`loader/Loader.cs` 用 `ReflectionTypeLoadException.Types` 拿**部分结果**登记模型 ⇒ 没装 BaseLib 时 Boss 依然存在（以前会一个都登记不上）；④没装 BaseLib **只是少两项功能**（游戏内模组配置界面、多人以房主为准的勾选同步），主菜单会用**原版弹窗**提醒一次（`UI/BaseLibMissingNotice`，16 语言文案，点掉后写 `baseLibWarned` 不再打扰）。另含 v0.2.586 / 2026-11-11（**联机「共用匹配池」不同步修复** —— 玩家反馈「联机时开和原版 boss 混合匹配会不同步」。根因：`SharePoolForGameplay` 为了修 v0.2.468 的配置分叉把**客人端写死成 false** ⇒ `ForVanillaBoss` 里「按种子抽这一场要不要保留原版 Boss」那段在客人端**整段被跳过** ⇒ 房主抽到「保留原版」时客人照样替换成角色 Boss ⇒ 进 Boss 房立刻分叉（反过来，房主关掉某张原版图的替换时客人也照样替换）。现在**房主把「决定上哪个 Boss」的玩法设置随勾选消息一起广播**（`BossPicksMessage` 新增 `Settings`：共用匹配池 / 进阶10一场原本 / 每张地图替换开关），客人直接用房主的值（还没收到时共用匹配池按「开」算并打日志）；房主改设置后立刻重播。协议变了 ⇒ 两端必须同一版本）。另含 v0.2.585 / 2026-11-11（**5 人联机适配** —— 用户「适配一下 5 人联机（原版的联机上限是四，但是有模组可以提高这个上限）。5 个 boss 的站位是 w 型…每一场 boss 战的 5 个 boss 分别是那 5 个原版角色，不重复也不少」。①`BossLineups.MaxPlayersPerFight = 5`，`PlayersPerFight()` 夹到 1～5，`MultiBossPlan.wantPartners` 引用**同一个常量**（否则会「阵容算 5 只、只造 4 只」）；②`Build` 在 need≥5 时补构成自检日志（打印 5 只分属哪个角色，重复/不足会喊 ⚠）；③`BossEnemySpreadPatch` 新增 5 只的 **W 型站位**（左上/左下/中上/右下/右上，Y=上下上下上，X 在可用宽度内等距，相邻≈240），仍按行动次序从左到右；④战场缩放不动（5 个点占 ~960px，与 4 只的 ~900px 同量级，再拉远原版背景会露黑边）。1～4 人行为不变。计划见 docs/plans/five-player-mp.md）。另含 v0.2.584 / 2026-11-11（**彩虹机特殊效果补标题行** —— 13 种非中文语言的 `DEFECT_BOSS.rainbow` 只有说明、缺「彩虹」标题行（本地化检查报「换行数不一致」的最后一处）；按各语言 `RAINBOW.title` 补上（Regenbogen / Arcoíris / Arc-en-ciel / Pelangi / Arcobaleno / レインボー / 무지개 / Tęcza / Arco-íris / Радуга / สายรุ้ง / Gökkuşağı）。本地化检查从 39 处 → **0 处**）。另含 v0.2.583 / 2026-11-11（**新 Boss 的本地化缺口全部补齐** —— 用户「都补」。①**血肉骨/虚无骨/抉择储 的怪物条目整个没写**（`<KEY>_BOSS.name` / `.title` / `.moves.CHARBOSS_PLAY.title`，16 语言全缺）：中文用设计名、其余 13 语言按既有约定给英文名（Flesh / Ethereal Necrobinder、Choice Regent）；②**蛇咬猎「群蛇形态」文案**（`SILENT_BOSS.snake_form` + `BOSS_SNAKE_FORM_POWER.title/.description`，此前 16 语言/13 语言全缺）按用户给的原文补齐，13 语言各写译文；③**打击战的「痛击」文案**（`IRONCLAD_BOSS.bash` + `BOSS_BASH_POWER.*`，缺 13 语言）补齐译文；④顺带修 `IRONCLAD_BOSS.bash` 里写成 `\\n` 的双反斜杠（说明框会把 `\n` 当字面量显示）。审计脚本 `.tmp/audit_boss*.ps1` 复跑结果：怪物三件套 / 特殊效果文案 / powers 三张表**全部齐备**）。另含 v0.2.582 / 2026-11-11（**华丽猎/血肉骨/虚无骨 三个遭遇补登记进 TierEncounters** —— 用户「现在第二个 boss 有可能显示原版的 boss 图标。这个 boss 是华丽猎，是因为没有让她用角色剪影做 boss 图标吗？是不是还有新 boss 也是这样？」。根因：这三个遭遇**类早就存在**、`BossKeys.EncounterOf` 也认得，但漏登记在 `CharBossEncounters.TierEncounters` 这张表里；而 `OurEncounterIconPaths.IsOurEncounter()` 正是遍历这张表建白名单 ⇒ 它们被判成「不是我们的遭遇」⇒ `BossMapPointCharacterIconPatch` 直接 return（地图徽章保持原版 TheKinBoss 占位图 = 用户看到的「原版图标」）、`EncounterIconPathPatch.Fix` 也不接管（顶栏/结算/历史同理）。现在补齐三条：`SilentGrandFinaleBossEncounter`（走 silent_shiv 专属图）、`NecrobinderFleshBossEncounter` / `NecrobinderEtherealBossEncounter`（不覆写 BossNodePath ⇒ 用亡灵契约师角色剪影）。**受影响的正是这 3 只，其余 31 只图标一直是好的**）。另含 v0.2.581 / 2026-11-11（**储君一族名字统一成「X储」** —— 用户「把这四个的“君”也去掉」。zhs/zht 的 `monsters.json` 各改 8 处（`.name` + `.title`）：碎屑储君→碎屑储、轰击储君→轰击储、仆从储君→仆从储、辉星储君→辉星储；连上一版的铸剑储/无色储，储君一族 7 个短名现在是「碎屑储 / 轰击储 / 仆从储 / 铸剑储 / 无色储 / 辉星储 / 王储」。`BossKeys.DesignNames` 本来就是这套短名 ⇒ 代码注释以外无需改动。顺带把 zht 里唯一还是简体的「王储」转成「王儲」）。另含 v0.2.580 / 2026-11-11（**储君两个变体名改短** —— 用户「把“铸剑储君”和“无色储君”改成“铸剑储”和“无色储”，统一格式」。只动本地化里的显示名，`BossKeys.DesignNames` 本来就是短的（`铸剑储`/`无色储`）⇒ 现在历史悬浮框、图鉴、Boss 名三处一致。zhs 改 `REGENT_FORGE_BOSS.name/.title`、`REGENT_COLORLESS_BOSS.name/.title` 共 4 处；zht 同 4 处并写成繁体（`鑄劍儲`/`無色儲`）。另注：`碎屑储君`/`轰击储君`/`仆从储君`/`辉星储君` 仍是长格式，等用户口径）。另含 v0.2.579 / 2026-11-11（**历史记录显示变体名** —— 用户「能不能在历史记录这里额外显示出来该 boss 是哪个变体（如黑球机）？局内最上方仍显示储君、静默猎手这样」。那一行读的是遭遇的 `.title`（反编译 `NMapPointHistoryHoverTip._Ready`：`ModelTitle = EncounterOrDeprecated(First(Rooms).ModelId).Title`），而我们的遭遇 `.title` 为修 v0.2.578 的「显示成原始键名」一律写成了**角色名**，且局内顶栏也用它 ⇒ 不能改文案。新增 `Patches/RunHistoryBossVariantPatch`（Postfix 到 `NMapPointHistoryHoverTip._Ready`）：遭遇 id → `BossKeys.KeyOfEncounterEntry` → `<KEY>_BOSS.name`（monsters.json 里各语言自带的设计名，zhs 黑球机 / eng Dark Orb Defect），覆盖 `ModelTitle` 后重渲染；取不到本地化名（如 zhs 的 NECROBINDER_FLESH_BOSS.name 是空的）退回 `BossKeys.DesignNameOf`。非档位遭遇（基础遭遇等）`KeyOfEncounterEntry` 返 null ⇒ 原版行为不动）。另含 v0.2.578 / 2026-11-11（**补齐 8 个遭遇的 `.title`** —— 用户：「华丽猎等新 Boss 在最上方一栏的名字显示不正确」，截图里直接显示成 `encounters.SILENT_GRAND_FINALE_BOSS_ENCOUNTER.title`。根因与死亡提示那批同源：新加的遭遇只写了 `.loss` 没写 `.title`（遭遇本地化只有这两个键）。本轮按「同角色已有遭遇的 title」逐语言复制补齐：`SILENT_GRAND_FINALE`（华丽猎）/`DEFECT_THUNDER`（电球机）/`DEFECT_STATUS`（状态机）/`REGENT_BOMBARD`（轰击储君）/`REGENT_MINION`（仆从储君）5 个 16 语言全缺，另有 13 种非中文语言缺 `IRONCLAD_STRIKE`（打击战）与 `SILENT_SNAKEBITE`（蛇咬猎）的 title、zht 缺 `DEFECT_RAINBOW`（彩虹机）的 title ⇒ 一并补上。校验：**16 语言 × 34 个档位遭遇的 title 现在全部齐备**）。另含 v0.2.577 / 2026-11-11（**死亡提示：其它 13 语言全部补齐 + 仆从储改文案** —— ①仆从储改为「{character} 被储君和他的仆从们围殴致死。」（zhs/zht/eng 及另 13 语言同步）；②13 种非中文语言（deu/esp/fra/ind/ita/jpn/kor/pol/ptb/rus/spa/tha/tur）此前各缺 7 条（电球机/轰击储君/打击战/蛇咬猎/仆从储君/华丽猎/状态机），另有 4 条仍是英文占位（血肉骨/虚无骨/养手骨）、2 条是旧措辞（彩虹机/放血战）⇒ 本轮每语言补/改 12 条，术语全部取自游戏自带各语言卡名（雷暴/轰击/仆从打击/华丽收场/散射炮/血肉戏法/蛇咬/完美打击）与角色名（OSTY 也用各语言的宠物名）。至此 **16 种语言 × 34 只档位 Boss = 544 条死亡提示全部齐备**（校验脚本逐条核过）。泰语缺 REGENT 角色名与 Flak Cannon / Sleight of Flesh 卡名标题 ⇒ 这几处按游戏自身的英文回落写）。另含 v0.2.576 / 2026-11-11（**死亡提示大改** —— 用户给了 6 条新文案（血肉骨/打击战/彩虹机/虚无骨/放血战/养手骨），另按现有句式**新写 5 条**补齐此前完全没写的那 5 只（电球机/轰击储君/仆从储君/华丽猎/状态机）。至此 34 只档位 Boss 在 zhs / zht / eng 三份 `encounters.json` 里**全部**有 `<KEY>_BOSS_ENCOUNTER.loss`（此前 zhs/eng 各 42 条里缺 5 条档位键、zht 还缺 1 个键）。血肉骨的英文按用户 PS 要求同时含 flesh + sleight（对应「血肉戏法」`SleightOfFlesh` 的两半）；顺带把 zht 里原本是英文占位的「养手骨」那条翻成繁体。另含 v0.2.575 / 2026-11-11（**轰击储「环绕轨道」文案统一** —— 用户「我记得我让你改过…文案，怎么还是这个？」。根因：这个特殊效果在本地化里有**两份**，`monsters.json` 的 `REGENT_BOSS.orbital`（带标题行，Boss 信息用）和 `powers.json` 的 `BOSS_ORBITAL_BOMBARD_POWER.description`（战斗中那个「特殊效果说明框」读的是这一份），上次只改了前者 ⇒ 界面没变。按用户口径「用 monsters.json 那份（短句版）覆盖 powers.json」：16 语言的 powers description 全部换成各自 monsters 的正文（去掉标题行），两份从此一致；顺带修 zht 的 `BOSS_ORBITAL_BOMBARD_POWER.title`（原来是简体「环绕轨道」→「環繞軌道」））。另含 v0.2.574 / 2026-11-11（**拦掉状态机「火箭飞拳」自带的抽 1 张** —— 用户「拦掉火箭飞拳的抽牌，因为我已经提前在剧本里写好了」。反编译 `RocketPunch.OnPlay` = 13 点伤害 + `CardPileCmd.Draw(ctx, Cards=1, Owner)`；那一抽把她抽牌堆里的状态牌（大抱抱的煤灰、烟囱/污秽攻击生成的 WOUND/SLIMED/DAZED）抽上手，mod_log 21:16:46 实证：`PLAY ROCKET_PUNCH` 紧接 `回合开始抽牌 →（SOOT）`，手牌变成 [DEFEND_DEFECT,COMPACT,DOUBT,SOOT]。新增 `Patches/RocketPunchNoDrawPatch`（与全息影像同一套窗口写法：`OnPlay` 前后置开窗口 + `CardPileCmd.Draw` 前置返回空任务），伤害照常结算；这些状态牌现在留给她第 1/4/9 回合的「散射炮」一次性烧掉。另：全息影像的拦截本身没问题 —— 日志里 BURN / DUALCAST 的返手都被拦下了，煤灰是火箭飞拳抽上来的）。另含 v0.2.573 / 2026-11-11（①**能量汲取的升级进度每场清零** —— 用户「根除骨的恐惧和戳击怎么在第一回合就升级了？」。日志钉住：20:58 那场第 4 回合升了 FEAR/POKE，21:00 那场**第 1 回合**手牌就是 [POKE+,FEAR+,FRIENDSHIP+]，而当时卡组 `deck(12): POKE,FEAR,FRIENDSHIP+,…` 并未升级 ⇒ 不是进阶效果、是残留状态：怪物 Model 在同一次运行里被复用，`_persistentlyUpgraded` 与 `DrainUpgradeStep` 从来没清过。现在在 `AfterAddedToRoom`（和 `CombatStartFired / TurnNumber / NextTurnEnergyBonus` 同一处）一起清掉；②**蛇咬猎死亡提示**「蛇群」→「蛇咬」（zhs/zht；英文那行仍是 serpents））。另含 v0.2.572 / 2026-11-11（新增**游戏内 F9 导出 Godot 场景树**（`Core/SceneTreeDumper.cs`）：任意界面按 F9 → `%APPDATA%/SlayTheSpire2/scene_tree.txt`（整棵树：路径/类名/可见性/尺寸/文本）+ 有战斗时另存 `scene_tree_combat.txt`（只导战斗房那棵子树），日志里有「场景树已导出：…（N 行）」，用来替 Godot 编辑器找节点路径。另含 v0.2.571 / 2026-11-11（藏品补齐两处：①**蛇咬猎 → 群蛇形态**（`SerpentForm`，用户 2026-11-11 指定）—— 它原本没有专属分支，会和同幕「奇巧猎」落在同一格（掉落表按「角色+幕」索引）从而错误地掉「余像」；②**炼药猎 → 炼制药水**（`Alchemize`，用户 2026-10-26 早就指定过，但代码里一直没有分支，实际掉的是刀猎的「墨之刃」）。另：34 只 Boss 现在**全部**有藏品（变体走 ①变体 key / ②遭遇 id，主设计走 ③`LootTable[(角色, 幕)]`），之前我说「16 只没有藏品」是只扫了 switch、漏了掉落表兜底，属误判）。另含 v0.2.570 / 2026-11-11（①**愤怒战**「群情激奋」的描述早就是按人数动态的（`BOSS_RAGE_POWER.description` 用 `{Loss}`，`IroncladRagePower.Description` 现算 单人3/双人2/三四人1 ⇒ 界面只显示当前那一个数字，图标数字走 `DisplayAmount`）；本轮把 `monsters.json` 的 `IRONCLAD_BOSS.anger` 里写死的「3 点」也换成 `{Loss}`（16 语言），消灭最后一处硬编码。②**巨石战**按用户要求统一代码与描述：「巨石」破防改回**给该玩家 1 层虚弱**（`BossPrimalForcePower` 里原来的「该玩家抽 1 张牌」已删），每次巨石仍是 **+1 力量、+3 覆甲**；`BOSS_PRIMAL_FORCE_POWER.description` 与 `IRONCLAD_BOSS.boulder` 16 语言同步改成「+3 覆甲 / 未被格挡 ⇒ 你获得 1 层虚弱」）。另含 v0.2.569 / 2026-11-11（按用户口径修两处：①**打击战的完美打击全部附魔王室认证** —— `IRONCLAD_STRIKE` 剧本第 6~9 回合由「地狱狂徒」免费打出的那几张以前经 `Hell()` 复制时丢了 `EnchantRoyallyApproved`，现在原样带上；②**彩虹机第 8 回合不再有 30 点无来源群伤** —— 节拍器早已从遗物里移除，剧本那个 `ExtraAoe = 30` 一并去掉，手牌栏不再标那一条。另：Boss 意图表核查结论更正 —— 之前列的 10 条「表格 vs 游戏内差异」里有 8 条是我的基线写错了（拿 2026-11-07 的 intent-dump 当现状），当前代码本来就和表格一致）。另含 v0.2.568 / 2026-11-10（抉择储：按用户「把 4、5、11 这三个回合中那张不出的防御换成打击，保持它不出」——第 4 / 5 / 11 回合原本压暗的那张「防御」改成「打击」（仍是打不出、压暗），这样它**不是技能牌** ⇒ 第 5 回合「抉择，抉择」的候选仍然正好是那 5 张；卡组里同时补两张打击）。另含 v0.2.567 / 2026-11-10（抉择储：新增**铸造对账日志** —— 用户「怎么感觉铸造有问题？…君王之剑第四回合应该是 10+10+8+5=33，我看到的是 37」。君王之剑伤害 = 卡面 10 + 本场铸造之和；37 = 33 + 4 正好是「淬炼刀刃」进阶 9 升级后的铸造 12（`ForgeVar(8)` + `UpgradeValueBy(4)`，未升级才是 8）。为了让下次一眼对账，`PlayOne` 每打出一张带 `Forge` 变量的牌就打一行 `铸造：<牌>（升级=…）铸了 N ⇒ 君王之剑当前伤害 M`，配合既有的 `击剑指南：战斗开始铸造 10…` 就能逐笔核出 M。另含 v0.2.566 / 2026-11-10（抉择储剧本第三轮订正（用户）：第 3 回合 = 招架、星尘、汇流+、防御（3 费）；第 4 回合 = 君王之剑、隐秘藏品、防御（没费）（3 费）；第 5 回合只有 **2 费**（天际钻头 X=2；选了星位序列则 X=8 ⇒ 16 段，星位序列下面的预告也随之变成 8×14））。另含 v0.2.565 / 2026-11-10（抉择储：「第一回合选隐秘藏品的情况下，第二回合的辐射没有计算隐秘藏品在回合开始时给的星」—— 「下回合 +N 辉星」（`StarNextTurnPower`）是**她回合开始时**到账的，对本回合而言就是「本回合获得过的辉星」⇒ 规划期现在把它**同时**算进星预算 `stars` 与 `starsGainedThisTurn`（辐射的段数口径）；星尘的星数口径也一并包含它）。另含 v0.2.564 / 2026-11-10（抉择储：①**星尘/辐射的段数现在真的生效了** —— 用户「辐射仍没有提前计算本回合会获得的辉星数。星尘仍然没有正确显示段数」：上一版只把 `starHits` 传给规划期那一处，而 `PrepareTurn` 规划完**紧接着**就调 `BladesGrowth.RecomputeAll` 重算、出牌期每张牌前还会再重算 ⇒ 全被「历史口径」覆盖回 1 段。现在把它定死存进 `PlannedPlay.StarHits`，两条重算路径（`BladesGrowth.RecomputeAll` / `CharBossMonster.RefreshPlannedIntents`）都原样再用（和 `SmokePerHit`/`LetterOpenerHit` 同一套做法）；②**进阶 9 不再升级共鸣**（用户订正）——`BossDefs` 的进阶表与第 11 回合剧本那张都改回未升级）。另含 v0.2.563 / 2026-11-10（抉择储意图/资源四处修正：①**星尘**（X 星费，把身上所有辉星花光 ⇒ 一星一段）与**辐射**（段数 = 本回合获得过的辉星数）现在按剧本顺序把星数递进 `BossIntentBuilder.IntentsFor(starHits:)`（规划期历史是空的，原来恒显示 1 段）；②规划期新增 `starsGainedThisTurn` 记账；③**汇流/淬炼刀刃的「下回合 +1 能量」不再当成立刻到手**（`DeferredEnergyCard`），修第 9/10 回合最后那张本该死费的隐秘藏品被画成还能打；④天际钻头 <<把这一回合剩余能量全部花光>> 改由剧本 `ScriptCard.SpendsAllEnergy` 明说（不再靠 `EnergyCost.CostsX` 猜），修第 5 回合「君王之剑」被画成能打；⑤`XHitBonus` 改成**规划期算出的 cost 优先**（以前优先读已捕获的 X，可能残留上一张牌的值 ⇒ 第 13 回合只剩 1 费却画 2 段），并给天际钻头补上「X≥4 翻倍」；⑥天钻 X=0/星尘辐射 0 段时不再画一条 0 点攻击意图。另含 v0.2.562 / 2026-11-10（抉择储：「抉择，抉择」不再被提前打断 —— 用户「如果我选的太慢，他的这个回合会结束并使我的下个回合开始。应该让储君的这张牌不会被提前打断，玩家选完之前所有其他进程都应该停着。也不要因为这张牌卡了很久就把它当bug（只让抉择抉择受到这些豁免）」。两处一起改：①`PlayOne` 那个 5 秒 AutoPlay 兜底对它**不计时**（一直 await 到玩家选完，期间她整个回合停在这一句，其它 Boss/玩家回合都排在后面）；②`StrayBossCardQueuePatch.Reap()` 在等待期间**暂停**（新增 `HoldReap`）—— 慢车道 4Hz 会把它当成「怪物打出的残留卡面」从出牌队列里摘掉，等于提前打断她这一手；③`BossFrameTickPatch.WatchPlayQueue` 不再对 DECISIONS_DECISIONS 报「⚠ 出牌队列滞留」（和数据收集里那条「玩家自己在选牌」的豁免同一性质）。另含 v0.2.561 / 2026-11-10（抉择储：星位序列那条「天际钻头会多打多少」改用**原版意图节点**（`MultiAttackIntent` → `NIntent`，与其它意图同一套图标/字体），不再是我自造的 Label（用户「你用原版的，别自己瞎搞」）；它仍然**不放进 `PlannedPlay.Intents`** ⇒ 不进原版 `MoveState.Intents`，薄荷尖塔不会重复统计（用户「只改一下不让薄荷尖塔记录就行了」）。顺带：第 5 回合的钻头是常驻的、没有 `OnlyIfChosenCard`，`FindChoiceDrill` 现在按「X 费牌」也认得出它，所以那一手也会画预告。另含 v0.2.560 / 2026-11-10（抉择储第二轮：①第 6 回合按用户订正改成 2 费「辉光、辐射、淬炼刀刃、收集光辉（没费）」；②那 5 张候选技能牌**压暗但画意图** —— 铸墙=守势+强化、隐秘藏品=强化、共鸣=策略+强化、震荡波=策略（`ScriptCard.ForceIntents` + `PlannedPlay.DimWhenShown`，手牌栏 `Bright` 不再只看「在不在 DisplayPlays 里」）；星位序列那一栏标出「选它会多打多少」（如第一回合 8×12 = 天际钻头 8×6 不变、选后 8×12，即多 6 段），这条是**标签不是意图** ⇒ 不进原版 MoveState.Intents，薄荷尖塔的伤害统计不会算它（`PlannedPlay.DrillPerHit/DrillGainHits` + `BossHandCard.AddDrillGainLabel`）；③震荡波本身带「消耗」，给它附魔「灵魂之力」（候选那张与第 12 回合那张都加）。另含 v0.2.559 / 2026-11-10（三层储君 3 号「抉择储」（用户设计）：特殊效果「指导」= 她打「抉择，抉择」时**由玩家**（多人时是被他锁定的那位）替他挑那张要被重复打出 3 次的牌 —— 用知识恶魔那套 CardSelectCmd.FromChooseACardScreen，选完他才继续打后面的牌（Patches/ChoiceRegentPickPatch 接管 CardSelectCmd.FromHand；选牌前用 SuspendSelectorForTest 把自动选择器摘掉，否则界面根本不会出现；挑中的那张记进 Core/ChoiceRegent）。13 回合剧本、9～13 循环（Core/ChoiceRegentScript）；遗物 = 天赋君权/涅奥的祝福/准备背包/燃烧木棍/玻璃眼珠/击剑指南/王室印章/钗/灯笼（锚→灯笼、胜券在王→王国资产、崇拜全难度升级）；逐回合写死能量（1、2、3、5、7、8、10、11 = 3 费，12 = 4 费，其余 2 费）；第 1 回合「天际钻头」只有玩家选了星位序列（3 次 ×+2 费）才打得出去（ScriptCard.OnlyIfChosenCard + CharBossMonster.RunChoiceFollowUpsAsync 在选择落地后补打），第 5 回合的钻头一定打出（X = 出手时的剩余能量，X≥4 翻倍照原版）；16 语言文案 + 藏品天际钻头 + 能力面板占位）。另含 v0.2.558 / 2026-11-10（顶栏两个 Boss 头像一样：用户「剪影是一个亡灵契约师和一个储君，顶栏却两个亡灵契约师」—— 两颗图标各自按**遭遇**解析角色，而第二槽的遭遇很容易解析回第一槽那只角色（第二槽复用第一槽遭遇 / `SecondBossEncounter` 读取兜底没换成伙伴那套）。现在两颗解析出同一只角色时，第二颗改用**我们阵容里的第二只设计**（多人同场 = 本场阵容 partner；进阶 10 = 该幕第 2 场阵容主角色），`BossLineups.LineupNow()` 是新加的 UI 用包装。另含 v0.2.557：，日志直接指出的：`power.Applier` 不能当判据 —— 日志 22:56:25 显示 `STRENGTH_POWER −2 → 目标=玩家 钩子施加者=… power.Applier=…(是我=False)`；原因是 `StrengthPower` **复用已有实例**（玩家早先给自己 +1 力量时建的那份，实例上的 Applier 一直是玩家自己），而虚弱/易伤每次都新建实例所以一直正常。现在判据改成「钩子参数 applier 是她」为主，只有 applier 为空时才回退到 `power.Applier` 且要求 amount > 0（这样衰减 −1 不会被误判成她给负面）。诊断行把两个施加者分开打印。另含 v0.2.556：：日志证明钩子**确实被派发**（`吞噬生命钩子：power=STRENGTH_POWER amount=-2 …`），而 `VULNERABLE +2`/`WEAK +1` 都能正常打出 9 点 ⇒ 掐在「是不是负面」这一步：`GetTypeForAmount(-2)` 对 `STRENGTH_POWER` 认不出负面。现在与 `BossIntentBuilder.ClassifyPowers` 同一把尺子：`harmful = kind == Debuff || amount < 0`。并把诊断行扩成一次打全（目标 side/Monster、施加者是否是她、GetTypeForAmount 结果、是否 ITemporaryPower），下次一眼定位。另含 v0.2.555：改由**吞噬生命**（她必然带着的特殊效果）结算 —— 原来放在 `SleightOfFleshBossPatch` 里接管她身上那层 `SleightOfFleshPower` 的回调，实测有一次那次派发整段没走到（22:42 的 `STRENGTH_POWER −2` 连日志都没有）⇒ 9 点整条丢了；现在 `BossDevourLifePower` 里一次性做「+4 格挡 + 有血肉戏法就对该玩家补 Amount 点无来源伤害」，那条 patch 只保留「压掉原版，免得她自伤」+ 加了一行钩子诊断日志。②**Boss 的随从（奥斯提）不吃任何负面效果**（`BossPetNoStrengthLossPatch`）：以前只在「施加者是真人玩家」时才拦，于是她自己/她的牌给的负面（王储的临时减力量等）照样落到随从身上；现在改成「负面效果一律拦（任何来源）+ 真人玩家的正面效果也拦」。另含 v0.2.554：：查「玩家给 Boss 上负面时，头上不浮图标、也没有施加音效」。反编译 `NCreature.OnPowerIncreased` 第一行是 `if (silent || !CombatManager.IsInProgress) return;`（图标+音效一起没），而音效另外还要求 `power.ShouldPlayVfx`（= `Owner.IsAlive && IsInProgress && IsVisible`，`IsVisible` = `Target==null || IsMe(Target) || Target.IsEnemy`）。新增 `BossPowerApplyTracePatch`：只在 `PowerCmd.Apply` 的目标是 CharacterBosses 的 Boss 时打一行 `PowerApply 诊断：<power> <amount> → <她>（施加者=… silent=… IsInProgress=… ShouldPlayVfx=… IsMe=… Side=…）`（限 40 行）。复现一次把那几行发回来就能定位是哪一条被掐。另含 v0.2.553：：血肉戏法/吞噬生命判定「负面」时写成了 `amount <= 0`，而「失去力量」这类负面是**负数**送进来的（`PowerCmd.Apply<StrengthPower>(…, -2, …)` ⇒ amount = -2）⇒ 命运同担那条 9 点被整条挡掉、她也不加格挡。原版判据本就是 `amount != 0`，现在两处都改成「只排除 0，是不是负面交给 `GetTypeForAmount`」。另：血肉骨的能力面板按用户给的数据补齐（单体强/群攻强/单体高压 3、5、11/群体高压 4、10/特殊效果类型：自身/群体减益极强/群体增益弱）。另含 v0.2.552：改成**策略 + 牺牲**：它的两个变量 `PlayerStrengthLoss`/`EnemyStrengthLoss` 都是普通 `DynamicVar` 而不是 `PowerVar<>` ⇒ `ClassifyPowers` 什么都认不出来，会掉到「技能牌兜底 = 强化」上（蓝色上箭头）。现在把 `SHARED_FATE` 同时加进 `ForceDebuffCards`（给敌人扣力量 = 策略/红下箭头）与 `SacrificeCards`（自己也扣力量 = 牺牲/蓝下箭头）；顺带它既然算「策略」，血肉戏法那 +9 也会跟着显示（它确实会给那个玩家上负面）。另含 v0.2.551：：①**招架盾 → 锚**（战斗开始 10 格挡），特殊效果警告文案同步去掉「招架盾」；②11 回合剧本再重排 —— 1 血肉戏法+鬼火+防御 / 2 吸引仇恨+摧残 / 3 打击+恐惧+ / 4 违逆+防御+苦难 / 5 护卫+出击+防御(没费) / 6 命运同担+防御×2 / 7 吸引仇恨 / 8 摧残+防御+防御(没费) / 9 防御+恐惧+ / 10 违逆+防御+苦难 / 11 护卫+出击+防御(没费)，循环仍 7～11；③**摧残在玩家身上改成与虚弱/易伤同步**（`DebilitateTicksWithWeakPatch`）—— 反编译：`VulnerablePower` 是 `side == Enemy ⇒ TickDownDuration`，而 `DebilitatePower` 是「持有者自己这一侧回合结束 ⇒ Decrement」⇒ 玩家身上摧残玩家回合一结束就掉 1，少撑一个回合；现在玩家身上的摧残也只在**敌方**回合结束掉 1。另含 v0.2.550：：**撕碎 → 玷污**（`Defile`，1 费 13 伤，升级 17）—— 第 4、11 回合各换一张，能量账不变；卡组里对应替换。另含 v0.2.549：：①**进阶 9 = 只升级摧残**（去掉恐惧）；②剧本重排 —— 2 出击+恐惧+(没费打击) / 3 违逆+防御+苦难 / 5 吸引仇恨(不升级)+摧残 / 6 打击+防御+命运同担 / 7 吸引仇恨 / 9 防御+恐惧+ / 10 违逆+防御+苦难 / 11 撕碎+护卫+防御，循环仍 7～11；③卡组按单回合最大张数重算（14 张：防御×2、打击×2，其余各 1）。另含 v0.2.548：：`UNLEASH` 的伤害是 `CalculationBase + ExtraDamage × 奥斯提当前生命`，而意图数字只在**面板重建**时算 —— 玩家在她回合外打掉/治好奥斯提、或奥斯提被召唤/死亡时，能力层数一个都没变 ⇒ `RefreshIntentsIfPowersChanged` 的指纹不变 ⇒ 不重建 ⇒ 数字停在旧值。现在把**她的奥斯提**（存活 + 当前生命 + 最大生命）也拼进那个每帧比对的指纹，一变就 `IntentsDirty` ⇒ 下一帧按新血量重算；并加了一行限流日志 `意图重算（能力/奥斯提变化）：… → …` 便于验证。另含 v0.2.547：（用户「更改部分意图和进阶效果」）：11 回合剧本按新稿重排 —— 2 出击+防御+防御 / 3 违逆+恐惧+苦难 / 4 撕碎+防御+防御 / 5 吸引仇恨+ / 9 恐惧+防御+苦难 / 10 撕碎+护卫+防御 / 11 防御+违逆+打击，循环仍是 7～11；**进阶 9 改成升级恐惧、摧残**（之前误写成苦难/出击/摧残）；卡组按单回合最大张数重算（护卫×2、防御×2、出击×2、违逆×2、恐惧×2、苦难×2、撕碎×2、打击×2，吸引仇恨/摧残/命运同担各 1）。另含 v0.2.546：：①多人时招架盾只打她锁定的那位玩家（原版是 `HittableEnemies` 里随机一个，会打到没被锁的人）——`ParryingShieldBossPatch` 按原版条件结算、目标固定成 `BossLockOn.TargetFor`；②命运同担不再打到自己 —— 反编译该卡除了让目标掉力量还会**让她自己掉力量**，而她自己的生物 `Side==Enemy` 正好满足原版血肉戏法的 `power.Owner.IsEnemy` ⇒ 她给自己上负面时被自己的血肉戏法打 9 点；现在 `SleightOfFleshBossPatch` 对「持有者是我们 Boss」的情况**整条接管**，只对真人玩家结算；③进阶 9 不再升级出击（保留苦难、摧残）；④招架盾预测修正 —— 以前无条件把她**当前**格挡算进去，而格挡在她自己回合开始会清空 ⇒ 玩家回合里那份是上回合剩下的、马上就没，现在只在「她回合内」才计入；并加了一行预测明细日志（当前格挡/牌/骇人头盔/吞噬生命各自贡献）。另含 v0.2.545：NECROBINDER_FLESH —— 11 回合剧本 + 7～11 循环，遗物缚魂命匣/涅奥的祝福/骇人头盔/招架盾，特殊效果「吞噬生命」（她每给玩家一个负面就 +4 格挡，`BossDevourLifePower`）；①`SleightOfFleshBossPatch` 让血肉戏法对她生效（原版条件是 `power.Owner.IsEnemy`＝绝对阵营，玩家是 Side.Player ⇒ 对影子玩家永远 false）；②会施加减益的牌，意图里补上血肉戏法的 9 点（无来源）；③回合结束格挡预测 ≥10 时，在独立意图位（电球/炸弹那一行）显示招架盾的 6 点（`FleshBone.EndOfTurnBlock` 算了 plan 里的格挡 + 骇人头盔 + 吞噬生命）；④多人「苦难」把被锁玩家负面传给其他玩家 ⇒ 其他玩家意图显示 血肉戏法×种类数；⑤多人把吸引仇恨换成骸骨军团（`MultiplayerCardSwap` act0 行）；⑥16 语言文案（特殊效果含「无来源伤害 / 为你而死不挡」警告）+ 藏品命运同担 + 能力面板。另含 v0.2.544：：`BossStatusRow.Fill` 里能量球是按影武者缓存的节点，旧代码只在「已挂在本行」和「完全没有父节点」两种情况把它排进 `target`，一旦它挂在**另一条（残留的/备用/不可见的）状态栏**上就既不搬运也不登记 ⇒ 这一行没有能量球、`EnergyCenterX` 一路返回 0（14:07 的 mod_log 里 `独立意图定位：能量中心 x=0` 反复出现，且同帧有 `rowChildren=5/5` 与 `4/4` 两条 Fill）。现在：①「可复用」判定再按影武者缓存兜一层，避免把行里那颗正在用的球当垃圾清掉；②父节点已失效/排队销毁/**不可见**时搬过来；③主面板（`primaryRow`，可见）一律抢过来，只有备用行才「别抢还活着的那条」。另含 v0.2.543：：`PlaceExtraIntents` 原来只用「能量栏中心 − 行宽/2」，而它和卡面下方那排意图**同一高度**，牌少或这一行偏宽时左缘会滑进卡牌意图区（用户「第二回合电球被动过于偏左，和扫荡射线叠在一起」）⇒ 现在先逐格测量所有卡牌意图行的真实右缘（意图行在卡片内部且整体左移过、内容还可能比卡面宽，不能拿卡面右缘当边界），再让这一行左缘至少落在它右边 Gap 处；彩虹机的整体左移算在 max 之前。另含 v0.2.542：改成走「超能光束」那层原版 buff：每满 3 张牌给她 1 层 `HyperbeamFocusDownPower`（基类 `TemporaryFocusPower`，IsPositive=false）—— 应用时原版立刻 −1 集中、她自己回合结束时原版移除该层并把集中全数还回来、且它是 debuff 所以人工制品能挡，图标文案也是原版那套；107 没有这个类型（107 的超能光束是直接 −3、不返还）⇒ 用 `#if STS2_111 / #else` 保持老写法自己扣自己还。另含 v0.2.541 四项：①语音音量 -4dB → -0.5dB（振幅 ×1.5，用户「有点小，调大 50%」）；②预借时间不再统计**自动打出**的牌（`CardPlay.IsAutoPlay` 或 `Resources.EnergySpent == 0` ⇒ 没花能量不计），修「历史书打出攻击牌会错误增加预借时间计数」；③引雷针计数改存字段、图标数字钳在 1..3（还差几张），修「打三张牌集中不掉」—— 根因是玩家回合开始时 `Apply(-Amount)` 把数量减到 0，原版 `PowerCmd.ModifyAmount` 见 `ShouldRemoveDueToAmount()` 直接**把整条 power 移除**了；④电球机暴风雨的激发意图闪一下就没了（另记）；另含 v0.2.540：按用户第二轮要求改：①**时机延后** —— 不在出牌前播，改在 `Hook.AfterDamageGiven`（伤害真正落地那一刻）才播；②**不再每张攻击牌都喊** —— 只认 8 张：痛击 BASH / 完美打击 PERFECTED_STRIKE / 突破 BREAKTHROUGH / 全身撞击 BODY_SLAM / 巨石 GIANT_ROCK / 彼岸咆哮 HOWL_FROM_BEYOND / 闪电霹雳 THUNDERCLAP / 欺凌 BULLY；③每回合最多一次，多段牌只在第一段响（`CardCmd.AutoPlay` 前缀挂待播标记 + 上述伤害钩子配音清标记，两版本签名一致）；④彼岸咆哮那种回合结束从消耗堆自动打出的也算，因为挂在 AutoPlay 而不是我们的 PlayOne 上。上一版 v0.2.539 首次加入：：一代崩坠那 6 句 VO_IRONCLAD_1a/1b/1c/2a/2b/2c 已打进我们自己的 PCK —— ①每回合**首次打出攻击牌**时随机喊 1a/1b/1c（`CharBossMonster.PlayOne` 在 AutoPlay 之前播，`AttacksPlayed==0` 即本回合首张，与出牌同时出声）；②**死亡**时随机喊 2a/2b/2c（`HasDeathSfx` 覆写成 false 压掉原版那条 FMOD 事件 + `NCreature.StartDeathAnim` 后置播我们的 ogg，同生物只喊一次）；③覆盖所有 IroncladBoss 子类（怒/打击/出血/杂耍/易伤/巨石）。另含 v0.2.538 的亡魂牵引段数与致死性顺序修正（用户实测预览 7×3、真实 6×4）：①**段数**补上「本回合还没打出去的虚无牌」—— 致死性是虚无牌而它排在亡魂牵引前面，规划期战斗历史里还没有它，改由 `BossIntentBuilder.PlannedEtherealBefore` 按 `boss.Plan` 自己数（三条重算路径自动一致，打完即从 Plan 摘掉⇒不会双算、也不会漏传参）；②致死性 +50% 的**顺序**改成「先走完原版伤害管道（力量/虚弱/易伤）→ 再乘倍率 → 向下取整」，与真实结算同序（(5+力量−1)×1.5=6）；旧写法先把牌面 ×1.5 再减力量会多 1 点。另含 v0.2.537 的虚无骨四处收口：①女妖之嚎不再多画「强化」意图（`NoBuffIntentCards` 点名 BANSHEES_CRY）⇒ 第 4 回合 = 攻势+策略、第 7 回合 = 只有攻势；②亡魂牵引的段数补上「本回合已打出的虚无牌」—— 致死性本身是虚无牌，规划期历史里还没有它（`BossScriptPlanner.etherealPlanned` 按剧本顺序累加，只给 PULL_FROM_BELOW）；③致死性的 +% 以前只给「奥斯提那几张」手工补，现在**她身上还没这层 power** 的普通攻击牌（正是规划期）也补一次，power 真上身之后走原版管道不双份；④配置界面补上虚无骨的能力面板（循环 7～10 / 单体极强 / 群攻很强 / 单体高压 4、7、9 / 群体高压 4、7 / 全局））

