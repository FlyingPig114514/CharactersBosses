# 恐惧模式（Dread）适配 —— 总计划与进度

> 用户 2026-11-13：一批巨量需求 = 5 个「支配」+ 约 28 只 Boss 的**恐惧模式专用特殊效果**（名字与正常模式相同、效果更强）
> + 若干普通模式的剧本/数值改动。经确认：**支配按「本幕 Boss 属于哪个原版角色」挂**（符合游戏机制），
> 分两阶段：**阶段 1 = 支配 + 框架**（已完成，v0.2.593），**阶段 2 = 28 只恐惧版特殊效果 + 普通模式小改动**（待做）。

## 0. 游戏侧的机制（反编译 0.113.0 核实）

| 事实 | 出处 |
|---|---|
| 恐惧模式 = `GameMode.Dread`（枚举：None / Standard / Daily / **Dread** / Blitz） | `MegaCrit.Sts2.Core.Runs.GameMode` |
| 「支配」= `MegaCrit.Sts2.Core.Models.Dread.DominionModel : AbstractModel`，`ShouldReceiveCombatHooks => true`，即**能收全部战斗钩子** | 同上 |
| 支配由 **Boss 遭遇**提供：`EncounterModel.GetDominion()`（默认 null） | `EncounterModel` |
| 挂载点：`DreadRunModel.ApplyCurrentDominion()` → `(Act.BossEncounter ?? SecondBossEncounter).GetDominion()` → `RunState.SetDominion(...)` | `DreadRunModel` |
| **进入 Boss 房时支配被清掉**（`RemoveDominionIfRoomIsBoss`）⇒ 支配只在**本幕普通房间**生效 | 同上 |
| 支配说明文案键：`ascension` 表 `DREAD.<Id 去掉 _DOMINION>.description` | `DominionModel.Description` |
| 原版已有 12 个支配（Aeonglass / CeremonialBeast / Insatiable / KaiserCrab / Kin / KnowledgeDemon / LagavulinMatriarch / Queen / SoulFysh / TestSubject / Vantom / WaterfallGiant） | `Models.Dread.Dominions.*` |

⚠ 恐惧模式是 **0.113.0 才有的**：107（正式版）既没有 `Models.Dread` 命名空间，`GameMode` 里也没有 `Dread`
⇒ 所有相关代码必须 `#if STS2_111`。

## 1. 阶段 1（已完成，v0.2.593）

- `src/Hooks/Core/CharDominions.cs`
  - `DreadMode.IsActive`（`#if STS2_111` 判 `RunState.GameMode == GameMode.Dread`，107 恒 false）
  - `DreadCharacters` 角色下标常量（0 铁甲 / 1 静默 / 2 故障 / 3 亡灵 / 4 储君，与 `BossKeys` 一致）
  - 5 个 `DominionModel` 子类（**只在 111 构建**）：
    | 支配 | 实现 |
    |---|---|
    | 铁甲战士 | 战斗开始（`AfterRoomEntered`）给**所有敌人** 1 层 `DemonFormPower` |
    | 静默猎手 | 同上，给**所有玩家** −1 `StrengthPower` + 1 `WeakPower` |
    | 储君 | `AfterDamageReceived`：伤害来源是敌方 ⇒ 用原版 `MonarchsGazeStrengthDownPower`（`TemporaryStrengthPower`，本回合 −1 力量、回合结束自动还） |
    | 亡灵契约师 | `AfterPlayerTurnStart`：随机一张手牌 `EnergyCost.AddThisCombat(1, true)`（本回合 +1 费） |
    | 故障机器人 | `AfterSideTurnEnd(Player)`：`敌人数 × 3` 点伤害给每位真人玩家 |
- `CharBossEncounterBase.GetDominion()`（仅 111）：`BossKeys.CharacterOf(KeyOfEncounter(this))` → 对应支配
- `content/localization/<16 语言>/ascension.json`：`DREAD.{IRONCLAD,SILENT,DEFECT,NECROBINDER,REGENT}.description`
- 实机：`模型登记完成（132 / 132）`（原 127 + 5 个支配）、189 个补丁目标、无相关报错。

## 2. 阶段 2 待做清单（用户原文，逐条实现）

### 进度（第 1 轮，v0.2.594，已装进游戏、自检通过）

- ✅ **恐惧版特殊效果的地基**：`BossDefinition.SpecialLocDreadByAct` / `SpecialPowerDreadByAct`、
  `BossVariant.SpecialLocDread` / `SpecialPowerDread`，`SpecialLocForAct` / `SpecialPowerForAct`
  在 `DreadMode.IsActive` 时优先取恐惧版 —— **后续每只 Boss 只补「恐惧版 Power + 两个字段 + 16 语言文案」**
- ✅ 刀猎第 4 回合：幻影之刃 → **精准+**
- ✅ 养手骨第 5 回合：第一张来生 → **钙化+**
- ✅ 蛇眼猎：普通模式特殊效果补「抱抱先生属于无来源伤害…」警告（16 语言）
- ✅ 仆从储「俯冲仆从」：**无需改动** —— 0.113 已把卡面从 13 削到 **11**，
  而 `BossDefs` 里进阶 9 本来就 `UpgradeValueBy(2)` ⇒ 正好 11 / 进阶 9 起 13
- ⏳ 辉星储（升级黑洞 + 换第 3/4 回合让第 4 回合七星出得来）：**下一轮**，需先算出各卡辉星收支
- ⏳ 28 只 Boss 的恐惧版特殊效果（见下表）

### 第 3 轮结论（含用户拍板的命名规则）

- **命名规则（用户 2026-11-13 明确）**：「每个 boss 都用特殊效果，也都有特殊效果的名字。
  恐惧症模式下所有特殊效果都用和普通模式一样的名字」⇒ 恐惧版文案 = **标题行照抄正常模式**、正文换成恐惧版内容。
- 重要发现：很多「恐惧版」其实只是**在正常版上做小幅数值/行为升级**，不是另起炉灶。例如
  - **物理机** = `DEFECT_BOSS.synthesis`「**人工合成**」= 战斗开始获得 **3** 层人工制品
    ⇒ 恐惧版 = **同名**「人工合成」、正文改成 **5 层**（只要换一条 loc + 把施加层数从 3 改 5）。
  - 状态机 = `DEFECT_BOSS.status`「状态机」，恐惧版文案与现有实现一致 ⇒ 只需准备文案。
- 因此实施模板固定为三步：①必要时新写/改造对应 Power（层数或触发条件）；②`BossDefs` 里给该变体
  填 `SpecialLocDread` + `SpecialPowerDread`（按幕的基座则填 `SpecialLocDreadByAct`/`SpecialPowerDreadByAct`）；
  ③`monsters.json` 加 `<原 key>_dread`（标题行与正常模式逐字相同），16 语言。


- 变体的特殊效果三件套分布（`SpecialLoc`/`SpecialPower` 是否自带）：
  - **自带**：`IRONCLAD_STRIKE / JUGGLER / BOULDER`、`SILENT_SNAKEBITE / SNECKO / BREWER / GRAND_FINALE`、
    `DEFECT_THUNDER / RAINBOW / EVENT / STATUS`、`NECROBINDER_FLESH / ETHEREAL / OSTY`、
    `REGENT_BOMBARD / KING / STAR / MINION / CHOICE`
  - **按幕取**（基座 `SpecialLocByAct`/`SpecialPowerByAct`）：`IRONCLAD_BOSS`、`SILENT_BOSS`、`DEFECT_BOSS`、
    `NECROBINDER_BOSS`、`REGENT_BOSS`（愤怒战 / 放血战 / 易伤战 等属于这一类）
- **待用户确认的一点**：恐惧版特殊效果要求「名字与原本相同」，但
  **物理机 / 黑球机 / 能力机** 这类变体自己并不带特殊效果（走按幕那一套），
  恐惧版要挂的名字取「本幕原本那条的名字」还是另起（例如「人工制品」）？
- **状态机**的恐惧版文案与它现在的实现一致（都是「生成状态牌时复制品进你的弃牌堆」）⇒ 可能只需文案不动。


- ✅ 第 1 回合「黑洞」→ **升级版**（用户要求）
- 🟡 **「第 3 回合防御 / 第 4 回合隐秘藏品+ 替换」没有实施**：反编译 0.113 的辉星收支是 ——
  起始 **3**（天赋君权 `StarsVar(3)`）、辉光+ **+2**、汇流+ **下回合 +2**、太阳打击+ **+1**、
  隐秘藏品+ **+1**，万向斩 / 王者之拳 / 剑雨 / 星界脉冲 / 花样百出 **无星费也得不了星**；
  七星 `CanonicalStarCost = 7` ⇒ 第 4 回合手上 **9 星 ≥ 7**，**七星本来就能打**。
  ⇒ 按你原口径（「使得第 4 回合有足够星」）这个前提在 0.113 已不成立，等你确认是否仍要换牌（换的话指定换成哪张）。

### 2.1 普通模式（不涉及恐惧）

1. **仆从储**：`俯冲仆从`（`MINION_DIVE_BOMB`）伤害 → **11**（进阶 9 及以上 **13**）——因为「冲锋！！」被削弱。
   → 我们剧本里的伤害来自卡面，需要确认是否由我们写死；若写死则改。
2. **刀猎**：第 4 回合的**幻影之刃** → **精准+**（`PRECISE_CUT` 升级版）。
3. **养手骨**：第 5 回合的第一张**来生** → **钙化+**。
4. **辉星储**：升级**黑洞**；把第 3 回合的**防御**与第 4 回合的**隐秘藏品+**换掉，
   使它在第 4 回合就有足够的星打出**七星**（后面不变）。
5. **蛇眼猎**：普通模式的特殊效果描述**补上**抱抱先生的警告（「抱抱先生属于无来源伤害…不会替你承受」）。

### 2.2 各 Boss 的恐惧版特殊效果（同名字、更强；`PS` 内容不入游戏内描述）

| Boss | 恐惧版效果（摘要） |
|---|---|
| 奇巧猎 | 每回合第 1 次弃牌 ⇒ 你下回合开始时**弃 2 张**（PS：命运同担可叠负面） |
| 蛇咬猎 | 手牌无上限；她洗牌 ⇒ 1 张蛇咬进她抽牌堆；**你**洗牌 ⇒ **3 张**蛇咬进你抽牌堆 |
| 灾厄骨 | 你回合开始时**将消失的格挡**改为抵消等量**灾厄** |
| 血肉骨 | 血肉戏法升级；她给负面 ⇒ **6** 格挡（+ 无来源警告） |
| 黑球机 | 眼部攻击升级；战斗开始把 1 张**冷光**加入你手牌；回合结束时若你手里留着冷光 ⇒ 她本回合**暗黑球不触发被动** |
| 电球机 | 暴风雨升级（**意图预览要跟着改**）；你一回合内打出 5 张牌 ⇒ 她本回合 −1 集中（每回合 1 次） |
| 愤怒战 | 获得**奥利哈钢**；她打出「愤怒」⇒ 同时把一张愤怒复制品塞进所有玩家弃牌堆 |
| 打击战 | 名字含「打击」的牌也加成她的**完美打击 + 痛击**；你每打一张 ⇒ 少受一张加成（PS：痛击改成与完美打击同规则） |
| 碎屑储 | 她生成碎屑 ⇒ 反而加入**你的抽牌堆** |
| 轰击储 | 战斗开始你获得 1 层**光谱偏移**；她第 1 回合打 1 张轰击后**被击晕**；5 个「你打出过无色牌的回合」后醒来，轰击改为**打她自己**（PS：意图跟着改） |
| 涂毒贼 | 中毒改在**你的回合结束**结算，且**可被格挡** |
| 蛇眼猎 | 她每回合首次抽到 3 费牌 ⇒ 保留（+ 抱抱先生警告）（PS：谋杀只要她抽牌就增伤，初始 1、第一回合 +7、之后每回合 +5） |
| 灵魂骨 | 你抽牌也算**她抽牌**（增加「自动化」计数与「死亡行军」伤害）（PS：回合开始的抽牌也算） |
| 虚无骨 | 战斗开始 ⇒ 所有玩家所有牌获得**虚无**，直到她死亡 |
| 物理机 | 战斗开始她获得 **5 层人工制品** |
| 彩虹机 | 每生成 5 个充能球 ⇒ +2 集中；她的**暗黑球不触发被动**（+ 玻璃球无来源警告） |
| 放血战 | 他造成伤害即按伤害量**回血**（无需破防） |
| 杂耍战 | 他通过杂耍生成的牌带**虚无 + 消耗**；含「打击」的牌也加成他的**完美打击**（PS：这张完美打击不会因打打击牌而下降） |
| 铸剑储 | 一回合内失去 25 生命 ⇒ 所有玩家本回合 −1 力量（PS：不再减少她铸造；每回合可多次） |
| 王储 | 王者之拳升级；她把牌放回抽牌堆顶 ⇒ 所有玩家下一张攻击牌伤害 **−50%** 且本回合 −1 力量（PS：用王之凝视）+ 抱抱先生警告 |
| 仆从储 | 俯冲仆从仍 16（恐惧模式不削）；她打仆从牌且仆从 < 1 ⇒ 改为召唤对应仆从；她用牌获得力量 ⇒ 其它友方等量获得（PS：有的回合必打一张仆从俯冲） |
| 刀猎 | 每回合首次打出 ≥2 费牌 ⇒ **4 张小刀**加入她手牌 |
| 炼药猎 | **群蛇形态**升级 |
| 根除骨 | 你每花 6 能量 ⇒ 你本回合所有牌 +1 费，且她 +1 能量 |
| 能力机 | 免疫「偏差认知」（获得时移除）；你每打 1 张能力牌 ⇒ 她 +1 集中；她的力量与集中**始终相等**（+ 冰雹风暴无来源警告） |
| 事件机 | 第 4、9 回合「万物一心」后接一张**疯狂进食+**，并正确计算意图伤害（万物一心吃 1 份加成、冰之长枪/超能光束吃 2 份）；她每有牌从弃牌堆回手 ⇒ 她与所有友方 +1 力量 |
| 状态机 | 她生成状态牌 ⇒ 复制品也进**你的弃牌堆** |
| 易伤战 | 她给予易伤 ⇒ 同时给 2 层虚弱 + 2 层脆弱 |
| 巨石战 | 第 1、7 回合多打一张**巨石+**（移除对应的不打牌，总数量不变）；执迷 −1 费；「巨石」+1 力量 +3 覆甲；巨石未被格挡 ⇒ 你 1 层虚弱 |
| 无色储 | 玩家在战斗内生成卡牌 ⇒ 也触发她的「创世之柱」并增加超质量体伤害（+ 炸弹无来源警告） |

> 实现方式约定：每只 Boss 的**恐惧版特殊效果**用「同名字、不同逻辑」的第二套实现，
> 说明文案另存 `<KEY>_BOSS.<special>_dread` 之类的新键（16 语言），
> 由 `BossDefs`/`SpecialLocForAct` 在 `DreadMode.IsActive` 时切换；`PS` 里的实现口径**不写进**游戏内描述。

## 进度（第 6 轮，v0.2.598，2026-11-13）

- ✅ 状态机恐惧版：用户给的原文与现有实现完全一致 ⇒ 只备同文案 DEFECT_BOSS.status_dread（16 语言），
  DEFECT_STATUS 变体加 SpecialLocDread；标题与正常模式逐字相同（符合「恐惧模式沿用同一名字」）。
- ⏳ 剩 25 只：黑球机（冷光 2→1 张进手牌）、电球机（每回合 −1 集中）、能力机（免偏差认知/每能力 +1 集中/力量=集中）、
  事件机（第 4、9 回合追加疯狂进食+）；战士 6（愤怒/打击/放血/杂耍/易伤/巨石）、
  猎手 8（奇巧/蛇咬/蛇眼/涂毒/炼金/根除骨/养手骨/刀猎）、亡灵 4（灾厄/血肉/灵魂/虚无）、王储 6（碎屑/轰击/铸剑/王储/仆从/无色）。
- 版本号注意：BuildInfo.Tag 里若只改 CharacterBosses.json 而不改 Tag，erify-deploy.ps1 会报
  「部署版本不一致（期望 vX）：Impl107/111=vX-1」——务必先把 Tag = "vX.Y.Z /  前缀一起改掉。
## 进度（第 7 轮，v0.2.599，2026-11-13）

- 黑球机「冷却剂」恐惧版：正常模式 2 张冷光（抽牌堆 1 + 手牌 1）；恐惧模式**只给手牌 1 张**
  （BossHookBridge.SideTurn.cs 的 GrantCoolantLuminesceAtCombatStart 内按 DreadMode.IsActive 选堆，
  Power 复用 BossCoolantPower）。文案 DEFECT_BOSS.dark_dread（16 语言，标题沿用正常模式）。
- 恐惧批次 4 / 28。剩 24 只：电球机 / 能力机 / 事件机；战士 6；猎手 8；亡灵 4；王储 6。
## 进度（第 8 轮，v0.2.600，2026-11-13）

- 电球机「引雷针」恐惧版：正常 每满 3 张牌 −1 集中（无上限）；恐惧 **5 张牌 −1 集中、每回合最多 1 次**
  （BossLightningRodPower：CardsPerFocus 改成按 DreadMode.IsActive 取 3/5，加 _dreadFired 每回合闸门）。
  文案 DEFECT_BOSS.thunder_dread（16 语言，标题沿用「引雷针」）；变体 DEFECT_THUNDER 加 SpecialLocDread。
- ⚠ 待办（同一只 Boss）：恐惧版「暴风雨」升级（第 3、10 回合）＋意图预览同步（Tempest 是 X 费，升级后球数 +1，预览必须跟着改）。
- 恐惧批次 5 / 28。剩 23 只：能力机 / 事件机；战士 6；猎手 8；亡灵 4；王储 6。
## 进度（第 9 轮，v0.2.601，2026-11-13）

- 电球机收尾：恐惧模式下第 3、10 回合的「暴风雨」用**升级版**（C<Tempest>(Core.DreadMode.IsActive)），
  意图/被动预览同步：BossOrbMath 的 TEMPEST 分支 + BossHandPanel.Extra 的 X 值都按 IsUpgraded **+1 颗**
  （反编译确认 Tempest.OnPlay: 
umOfOrbs = ResolveEnergyXValue(); if (IsUpgraded) numOfOrbs++）。
  	hunder_dread 文案加一行「她的『暴风雨』已升级（多生成 1 个闪电充能球）」（16 语言）。⇒ 电球机恐惧版完成。
- 恐惧批次 5 / 28（黑球机、物理机、彩虹机、状态机、电球机）。剩 23 只：能力机 / 事件机；战士 6；猎手 8；亡灵 4；王储 6。
## 进度（第 10 轮，v0.2.602，2026-11-13）

- 能力机「偏差认知」恐惧版：正常 = 你每打 1 张能力牌她 +1 集中、力量与集中始终相等；
  恐惧版在此之上**免疫「偏差认知」**——任何来源给她的 BiasedCognitionPower 都会在**她该侧回合结束前被移除**
  （DeviationPower.StripBiasedCognitionDread，挂在该层自己的 AfterSideTurnStart / BeforeSideTurnEnd 上 ⇒
  下一回合开始时原版那点掉集中永远等不到，等于白拿全部集中）。文案 DEFECT_BOSS.special_dread（16 语言，标题同名）。
- 恐惧批次 6 / 28。剩 22 只：事件机；战士 6；猎手 8；亡灵 4；王储 6。- ⚠ 踩坑：恐惧文案里写英文 "Die For You"（带引号）会把 monsters.json 的 JSON 打断 ⇒ 本地化检查报「共 14 处问题」。
  游戏内文案不要带半角双引号，改用「」或直接去掉引号。
## 进度（第 11 轮，v0.2.603，2026-11-13）

- 事件机「一心化万」恐惧版：第 4、9 回合在[万物一心]之后**再补打一张「疯狂进食+」**
  （EventMachineScript 里这两回合的 Cards / Note 按 DreadMode.IsActive 二选一；
  加成结算顺序不变 ⇒ 万物一心返手的 +2 力量吃在新增那张之前，意图由剧本顺序自动带出）。
  文案 DEFECT_BOSS.one_for_all_dread（16 语言，标题沿用「一心化万」），DEFECT_EVENT 变体加 SpecialLocDread。
- 恐惧批次 7 / 28（黑球机、物理机、彩虹机、状态机、电球机、能力机、事件机）。剩 21 只：战士 6、猎手 8、亡灵 4、王储 3（碎屑/轰击/铸剑）…
  注：王储组按 docs 清单剩 6（碎屑/轰击/铸剑/王储/仆从/无色），仆从储与辉星储的普通模式改动已完成、恐惧版待做。
## 进度（第 12 轮，v0.2.604，2026-11-13）

- 愤怒战「群情激奋（全局）」恐惧版：IroncladAngerScript.Relics() 加分支——恐惧模式下额外带**奥利哈钢**
  （FireBossRelicHooks = true 已在，遗物钩子照常补跑）。文案 IRONCLAD_BOSS.anger_dread（16 语言，标题同名）；
  IRONCLAD_BOSS 加 SpecialLocDreadByAct[0]（Power 沿用 IroncladRagePower）。
- 恐惧批次 8 / 28。剩 20 只：打击战、放血战、杂耍战、易伤战、巨石战；猎手 8；亡灵 4；王储 6。
## 进度（第 13 轮，v0.2.605，2026-11-13）

- 打击战「痛击」恐惧版：正常 = 名字含「打击」的牌加成她的完美打击（每张 +2；进阶 9～10 = +3），
  你每打出一张就少受一份。⚠ 这张表里原写的那两句其实**已经是普通模式的现状**，所以恐惧版的差异化落在数值上：
  **每张「打击」牌的加成 +1**（BossBashPower.PerCard()：2⇒3，进阶 9～10 的 3⇒4）。
  文案 IRONCLAD_BOSS.bash_dread（16 语言，标题「痛击」），IRONCLAD_STRIKE 变体加 SpecialLocDread。
- 恐惧批次 9 / 28。剩 19 只：放血战、杂耍战、易伤战、巨石战；猎手 8；亡灵 4；王储 6。
## 进度（第 14 轮，v0.2.606，2026-11-13）

- 放血战「死亡收割」恐惧版：正常只按**未被格挡**的伤害回血；恐惧版按**伤害总量**回血（被格挡的部分也算）——
  BossRendingPower.AfterDamageGiven 里 unblocked += result.BlockedDamage（DreadMode.IsActive 分支）。
  文案 IRONCLAD_BOSS.rending_dread（16 语言，标题「死亡收割」），IRONCLAD_BOSS 的 SpecialLocDreadByAct[1] 接线。
- 恐惧批次 10 / 28。剩 18 只：杂耍战、易伤战、巨石战；猎手 8；亡灵 4；王储 6。
## 进度（第 15 轮，v0.2.607，2026-11-13）

- 杂耍战「燃烧契约」恐惧版：正常 = 杂耍生成的牌带虚无 + 消耗（恐惧版一样，那条本来就是现状）；
  恐惧版新增：**你名字含「打击」的牌也加成他的完美打击**，且是**快照式**加成（不会因为你打出打击牌而下降）——
  BossScript.TakeOverPerfectedStrike 里 count += BossBashPower.CountNow(boss) - OwnStrikeCards（恐惧模式分支）。
  文案 IRONCLAD_BOSS.pact_dread（16 语言，标题「燃烧契约」），IRONCLAD_JUGGLER 变体加 SpecialLocDread。
- 恐惧批次 11 / 28。剩 17 只：易伤战、巨石战；猎手 8；亡灵 4；王储 6（碎屑/轰击/铸剑/王储/仆从/无色中的未做项）。
## 进度（第 16 轮，v0.2.608，2026-11-13）

- 易伤战「主宰」恐惧版：正常 = 给予易伤时附带 1 层虚弱；恐惧版 = **2 层虚弱 + 2 层脆弱**
  （DominateWeakPower.AfterPowerAmountChanged 的 DreadMode.IsActive 分支，FrailPower 用原版类型）。
  文案 IRONCLAD_BOSS.dominate_dread（16 语言，标题「主宰」），IRONCLAD_BOSS 的 SpecialLocDreadByAct[2] 接线。
- 恐惧批次 12 / 28。剩 16 只：巨石战；猎手 8；亡灵 4；王储 6 中的未做项。
## 进度（第 17 轮，v0.2.609，2026-11-13）

- 巨石战「原始力量」恐惧版（第一部分）：每张「巨石」的加成翻倍——**力量 +1→+2、覆甲 +3→+6**
  （BossPrimalForcePower 两个 Apply 前加 DreadMode.IsActive 分支）。文案 IRONCLAD_BOSS.boulder_dread（16 语言）。
- ⏳ 同一只 Boss 还欠（下一轮）：第 1、7 回合多打一张「巨石+」（移除对应的不打牌、总数量不变）、**执迷 −1 费**。
- ⚠ 踩坑：BossPrimalForcePower.cs 是 **LF 换行**，用 CRLF 拼的 Replace 会静默失败（返回 False 但文案/接线已经改成功 ⇒ 出现「文说 2 力 / 代码还是 1 力」的错配）。
  改这类文件要用正则 (?m)^([ \t]*)原行 方式，改完必须在源码里 grep 新标识符确认落地。
- 恐惧批次 12.5 / 28（巨石战部分完成）。剩：巨石战剩余两项；猎手 8；亡灵 4；王储 6 中的未做项。
## 进度（第 18 轮，v0.2.610，2026-11-13）

- 巨石战收尾：第 1、7 回合**多打 1 张「巨石+」**——用「执迷 −1 费」等效实现为**该回合 +1 能量**
  （BoulderIroncladScript 第 1 回合 Energy = Dread ? 5 : 4；第 7 回合 Energy = Dread ? Energy + 1 : Energy），
  两回合的 Note 也按恐惧模式改写；IRONCLAD_BOSS.boulder_dread 追加第三行说明。⇒ 巨石战恐惧版完成。
- ⚠ 踩坑（第二次同类）：BoulderIroncladScript.cs 也是 **LF 换行**，且用 Replace 拼接带逗号的行尾容易把
  ? A : B, 拆坏 ⇒ 报 CS1003/CS1525/CS1513。改成 **按行号整行改写 + 删掉残留的续行** 才干净。
- 恐惧批次 13 / 28。剩 15 只：猎手 8（奇巧/蛇咬/蛇眼/涂毒/炼金/根除骨/养手骨/刀猎）、亡灵 4、王储 6 中的未做项。
## 进度（第 19 轮，v0.2.611，2026-11-13）

- 奇巧猎「命运同担」恐惧版：正常 = 她每回合第一次弃牌 ⇒ 你下回合开始弃 1 张；恐惧版 = **弃 2 张**
  （SharedFatePower 里 discardAmount = Dread ? 2 : 1）。文案 SILENT_BOSS.sly_dread（16 语言，标题同名）。
- 恐惧批次 14 / 28。剩 14 只：蛇咬猎、蛇眼猎、涂毒贼、炼金猎、根除骨、养手骨、刀猎；亡灵 4；王储未做项。
## 进度（第 20 轮，v0.2.612，2026-11-13）

- 蛇咬猎「群蛇形态」恐惧版：正常 = 你或她洗牌 ⇒ 各塞 1 张蛇咬进各自抽牌堆；恐惧版 = **你洗牌 ⇒ 塞 3 张**（她那 1 张不变）
  （BossSnakeFormPower.AfterShuffle 里改成 copies = Dread ? 3 : 1 的循环）。文案 SILENT_BOSS.snake_form_dread（16 语言）。
- 恐惧批次 15 / 28。剩 13 只：蛇眼猎、涂毒贼、炼金猎、根除骨、养手骨、刀猎；亡灵 4；王储未做项。
## 进度（第 21 轮，v0.2.613，2026-11-13）

- 蛇眼猎「计划妥当」恐惧版：正常 = 每回合首次抽到 3 费牌保留，且谋杀**只在保留在手牌里时**才因抽牌加伤；
  恐惧版 = **谋杀只要她抽牌就加伤**（不再要求保留）。
  实现：BossDefinition.MurderCountsDrawnCardsFor 加运行时分支（DreadMode.IsActive && ActiveVariant.Key == "SILENT_SNECKO" ⇒ true）——
  注意不能直接改变体的 MurderCountsDrawnCards 属性，因为 BossDefs 是静态初始化，那时 DreadMode.IsActive 恒为 false。
  文案 SILENT_BOSS.well_laid_dread（16 语言，保留抱抱先生警告）。
- 恐惧批次 16 / 28。剩 12 只：涂毒贼、炼金猎、根除骨、养手骨、刀猎；亡灵 4；王储未做项。
## 进度（第 22 轮，v0.2.614，2026-11-13）

- 涂毒贼「带毒刺击」恐惧版：正常 = 中毒改在你回合结束结算、**可被格挡**；恐惧版 = 保留「回合结束结算」，
  但**不再能被格挡抵消**（PoisonStrikePower.TriggerBlockableAsync 里恐惧模式传 ValueProp.Unpowered | ValueProp.Unblockable）。
  文案 SILENT_BOSS.special_act2_dread（16 语言，标题「带毒刺击」），SILENT_BOSS 的 SpecialLocDreadByAct[1] 接线。
- 恐惧批次 17 / 28。剩 11 只：炼金猎、根除骨、养手骨、刀猎；亡灵 4；王储未做项。
## 交接：剩余工作（用户 2026-11-13 口径，务必按此执行）

### A. 用户新给的两条口��（优先做）
1. **华丽猎**（SILENT_GRAND_FINALE，特殊效果「压制」）恐惧版：
   前面「第9回合首次打出华丽收场」「每当静默猎手打出华丽收场时，把一张虚空放入所有玩家抽牌堆顶部」和后面的铭记死亡警告**保持不变**，
   中间加一条：**每当她打出华丽收场时，给予所有玩家 1 层「昏鸣」**（= 仪式兽给玩家的 debuff：下回合只能打出 1 张牌）。
   三处文案都要出恐惧版：powers 表 BOSS_SUPPRESSION_POWER.description（107）、.description_v113（0.113）、monsters 表 SILENT_BOSS.suppression；16 语言。
2. **抉择储**（REGENT_CHOICE，「指导」）恐惧版：每当储君打出「抉择，抉择」时，**由玩家替他挑 2 张**要被重复打出的牌。
   实现：**再打一次「抉择，抉择」并拦掉它的抽牌**（不要在牌上加「重放」，否则抽牌变多、意图全错）。
   流程：5 张里选 1 → 他打出 3 次 → 剩下 4 张里选 1 → 他再打出 3 次 → 继续打后面的牌；下次照旧「5 选 → 4 选」。

### B. 「我的小钱钱」致命后瓶中精灵不复活（玩家反馈，普通模式）
牌：0.113 新增**无色攻击**，1 费，「你每有 20 金币，造成一次 1 点伤害」= **多段、每段 1 点、段数随金币增长**。
排查顺序：①反编译该牌（0.113 无色卡池，按「每 20 金币 1 段」的数值特征找，中文名搜不到）看它调哪个伤害 API；
②若直接走 LoseHpInternal/多段实现绕过「将要死亡」钩子 ⇒ 照「彗星」那次的办法改回原版伤害出口；
③若走 CreatureCmd.Damage ⇒ 查我们的 BossBlockAbsorbPatch（每段都进前缀）与影子玩家复活（BossPotions + 瓶中精灵）是否被这一条链路遮住。

### C. 还没有恐惧增强的 Boss（10 只 + 上面 2 只）
刀猎、根除骨、养手骨、华丽猎；灾厄骨、血肉骨、灵魂骨、虚无骨；碎屑储、轰击储、铸剑储、王储、仆从储、无色储、辉星储、抉择储。

### D. 已完成（18 只）
故障机器人 7（黑球机／电球机／能力机／事件机／状态机／物理机／彩虹机）；铁甲战士 6（愤怒战／打击战／放血战／杂耍战／易伤战／巨石战）；
猎手 5（奇巧猎／蛇咬猎／蛇眼猎／涂毒贼／炼药猎 = **群蛇形态升级**，调制 不变）。

### E. 固有踩坑（每次改代码前先看这条）
- BossPrimalForcePower.cs、BoulderIroncladScript.cs、BossSnakeFormPower.cs 等是 **LF 换行**：用 CRLF 拼的 Replace 会静默失败 ⇒ 一律用 (?m)^([ \t]*)原行 正则，改完必须 grep 新标识符确认落地。
- 文案里不要出现半角双引号（会打断 JSON，本地化检查会报「共 14 处问题」）。
- 版本号要同时改 src/CharacterBosses.json 的 ersion 与 BuildInfo.Tag 的**前缀**（Tag = "vX.Y.Z / ），否则 erify-deploy.ps1 报「部署版本不一致」。
## 进度（第 23 轮，v0.2.617，2026-11-13：§5.1 用户当轮新给的两条）

已装进游戏并 `[verify] 两份实现的版本串都是 v0.2.617  OK`、`check-localization` 全部通过（16 语言）。

### ① 华丽猎「压制」恐惧版（`SILENT_GRAND_FINALE`）

用户当轮原文：前面「第9回合首次打出华丽收场」与「每当她打出华丽收场时把一张虚空放进所有玩家抽牌堆顶」
**保持不变**，中间加一条 —— **每当她打出华丽收场时，给所有玩家 1 层「昏鸣」**（= 仪式兽那个 debuff：
下回合只能打出 1 张牌）；后面的「铭记死亡」警告照旧。

- 机制落地：**直接用原版 `Models.Powers.RingingPower`**（反编译核实：Debuff / `StackType.Single`、
  `AfterApplied` 给那位玩家**所有卡牌**挂 `Models.Afflictions.Ringing`、`AfterCardEnteredCombat` 继续挂新牌，
  `ShouldPlay` 只放行「本回合第一张带 Ringing 的牌」⇒ 正是「下回合只能打出 1 张牌」）。
  `Powers/BossSuppressionPower.AfterCardPlayed` 在塞完虚空之后、`DreadMode.IsActive` 时对
  `BossLockOn.RealPlayers` 各 `PowerCmd.Apply<RingingPower>(ctx, creature, 1m, owner, card)`。
- 文案三处（16 语言，标题行照抄正常模式）：
  `powers` 表 `BOSS_SUPPRESSION_POWER.description_dread`（107 用）、
  `.description_v113_dread`（0.113 用 = 上一份 + 铭记死亡警告）、
  `monsters` 表 `SILENT_BOSS.suppression_dread`。
  `BossSuppressionPower.Description` 改成「恐惧模式优先取 `_dread`，否则维持原来的 v113 / 普通」，
  且不再整段包在 `#if STS2_111` 里（`DreadMode.IsActive` 在 107 恒 false，行为不变）。
- 接线：`BossDefs` 的 `SILENT_GRAND_FINALE` 变体加 `SpecialLocDread`（Power 仍是同一个类，走运行时分支）。

### ② 抉择储「指导」恐惧版（`REGENT_CHOICE`）

用户当轮原文 + 指定实现方式：每当储君打出「抉择，抉择」时，**由玩家替他挑 2 张**要被重复打出的牌；
**不要给牌加「重放」**（否则抽牌数变多、意图全错），而是 **再打一次「抉择，抉择」并拦掉它的抽牌**。
流程：5 张里选 1 → 他打出 3 次 → 剩下 4 张里选 1 → 他再打出 3 次 → 继续后面的牌。

- `Monsters/CharBossMonster.Choice.cs`：`RunChoiceFollowUpsAsync` 拆成两段 ——
  ①`PlayChoiceGatedCardsAsync`（原有的「只有挑了那张才打得出的牌」，第 1 回合天际钻头）；
  ②新增 `PlayDreadSecondDecisionsAsync`：`CombatState.CreateCard(DecisionsDecisions)` +
  `CardPileCmd.AddGeneratedCardToCombat(..., PileType.Hand, player, Top)` 造一张新的「抉择，抉择」塞进她手牌，
  置位 `DrawBlockEntry = "DECISIONS_DECISIONS"` 之后 `PlayOne` ⇒ `ShadowBossNoDrawPatch` 把它的
  `CardPileCmd.Draw(5)` 整段拦掉（该 Boss 本来也开着 `BlocksVanillaDraw`）⇒ 候选正好是「原来的手牌 − 被挑走的那张」。
  牌上**没有任何重放标记** ⇒ 抽牌数 / 意图 / `BossDamagePredictor` 全不受影响。
- `Core/ChoiceRegent.cs`：`Picks` 从「一回合一个 Type」改成「按挑选顺序的 `List<Type>`」，
  新增 `AllPicksFor` / `TryBeginDreadSecondPlay`（同一回合只补一次的闸门）/ `ClearFor` 一并清；
  `EntryAllowed` 改成「命中**任意**一张挑过的牌」⇒ 恐惧模式下第二挑选了星位序列，第 1 回合的天际钻头照样补打。
- `Patches/ChoiceRegentPickPatch.cs`：候选牌列表额外排除 ①「抉择，抉择」自己（它也是技能牌）、
  ②本回合**已经挑过的那张** ⇒ 严格符合用户「**剩下**的 4 张里选 1」。
- 文案：`monsters` 表 `REGENT_BOSS.guide_dread`（16 语言，标题「指导」/「指導」逐字沿用）
  + `powers` 表 `BOSS_GUIDE_POWER.description_dread`（`BossGuidePower.Description` 按恐惧模式切换）。

### 版本与验收

- `src/CharacterBosses.json` = `0.2.617`，`BuildInfo.Tag` 前缀同步改成 `v0.2.617 / `（两处必须一起改）。
- 107 / 111 双目标编译 **0 错误**；`check-localization.ps1` → 「全部通过：16 个语言目录」；
  `install-mod.bat` → `[verify] 两份实现的版本串都是 v0.2.617  OK`。
- ⚠ 本轮只做 §5.1。§5.3（「我的小钱钱」打死炼药猎后瓶中精灵不复活）与 §5.2（剩余 Boss 的恐惧增强）
  留到下几轮；创意工坊与 GitHub 均未动（用户交待「等我另外说」）。
## 进度（第 24 轮，v0.2.618，2026-11-13：§5.3 玩家 bug「瓶中精灵不复活」）

已装进游戏并 `[verify] 两份实现的版本串都是 v0.2.618  OK`；107 / 111 双目标编译 0 错误；本地化 16 语言通过。

### 那两张牌是什么（反编译核实）

- 「我的小钱钱」= **`MegaCrit.Sts2.Core.Models.Cards.MyShinies`**（0.113 新增；无色 / `CardType.Attack` /
  `CardRarity.Rare` / `TargetType.AnyEnemy` / 1 费）。`CanonicalVars`：
  `DamageVar(1m, Move)` + `GoldVar(20)` + `CalculatedHitCountVar("CalculatedHits")`，
  multiplier = `Owner.Gold / 20`。`OnPlay`：
  `DamageCmd.Attack(Damage.BaseValue).WithHitCount(hitCount).FromCard(...).Targeting(...).Execute(ctx)`
  ⇒ **逐段 1 点、段数 = 金币 ÷ 20**，**走的是完全正常的伤害管线**（不是「彗星」那种
  `LoseHpInternal` 直减），所以 [handover §5.3] 的「第 2 步（改回原版伤害出口）」**不需要做**。
- 「瓶中精灵」= `FairyInABottle`（药水）。对我们影子玩家生效靠两处补丁：
  `BossFairyInABottlePatch`（`Hook.ShouldDie` 前缀）+ `BossFairyAfterPreventingDeathPatch`
  （`Hook.AfterPreventingDeath` 前缀）。

### 真正的根因（结构性问题，不是某一数值）

老实现的复活分成**两个钩子**：`ShouldDie` 只回答「不许死」，**回血与丢药水全在
`AfterPreventingDeath` 前缀里**，而且那里要**重新查一遍** `FairyOf(boss)`。于是有一条必然掉链子的路：

1. `ShouldDie` 说「不许死」（`__result = false`、跳过原版 ⇒ 原版的 `preventer` 永远是 null）；
2. 死亡流程继续：`await Hook.AfterDeath(..., wasRemovalPrevented: true, ...)`；
3. `await Hook.AfterPreventingDeath(runState, combatState, preventer, creature)`；
   **如果这一步我们的前缀没接上**（药水再次查不到——多段伤害里第二次判死、栏位被重排、
   别的模组动过药水；或者中间任何 await 抛异常），原版在 `preventer == null` 时**什么也不做**；
4. 她停在 0 血 ⇒ `creature.IsDead`（= `CurrentHp <= 0`，反编译确认 `IsAlive => CurrentHp > 0`）为真
   ⇒ `KillWithoutCheckingWinCondition(..., recursion + 1)` **递归再判一次** ⇒ 这次药水没了 ⇒ 真死。

⇒ 玩家看到的正是「药水没了 / 精灵没救她」。而 `MyShinies` 是**唯一一张能在一手之内反复判死**的
玩家牌（每段 1 点、段数随金币增长）⇒ 最容易把这条竞态踩出来。

### 修法（v0.2.618）

把复活改成**在判死那一刻同步完成**，后半段只负责清负面：

- `BossFairyInABottlePatch`（`Hook.ShouldDie` 前缀）现在一次做完三件事：
  ① `PendingDebuffClear[boss] = potion` 记账；② `shadow.RemoveUsedPotionInternal(potion)` **同步丢药水**；
  ③ `creature.HealInternal(30% 最大生命)` **同步回血**（与 `CreatureCmd.Heal` 同一份底层实现，
  只差不等动画；`HealInternal` 里自带 `Revived` 事件与 `ActivateHooks`）。
  最后再校验一次 `CurrentHp > 0`：**真的没回上血就把 `__result` 交回原版**（宁可她死，也不能卡在 0 血）。
- `BossFairyAfterPreventingDeathPatch` 改成：有记账 ⇒ 只 `await ClearDebuffsOnlyAsync(...)`（清负面）；
  没记账但药水还在 ⇒ 走老的 `SaveAsync` 兜底。**清负面仍然被死亡流程 await**，
  所以 v0.2.329 的联机同步性质一点没动，反而更稳（回血变成纯同步、不再有「等不到」的窗口）。
- 日志（下次一眼可验）：
  `瓶中精灵：<Key> 本该死亡（HP 0/520）→ 用这瓶药水顶掉（**判死这一刻同步丢药水 + 同步回血**…）`
  → `瓶中精灵：丢弃药水=True → 回复 156 点生命（现在 156/520）`
  → `瓶中精灵：复活已同步完成（FAIRY_IN_A_BOTTLE）⇒ 现在清负面`。

### 批量核对（这一轮顺带确认、没改）

- `BrewerBossBlockableHpLossPatch`（挂 `Creature.LoseHpInternal`，炼药猎用格挡挡「失去生命」）
  只对 `props & Unblockable` 生效 ⇒ **普通攻击的剩余伤害完全不受影响**，与本 bug 无关。
- `BossBlockAbsorbPatch`（handover 里点名要看的那份）**本来就是「故意不安装」的死代码**
  （类注释第一行写着），真正在跑的是上面那份 ⇒ 那条排查线索作废。
- 炼药猎的剧本**从不喝瓶中精灵**（`InitialPotions()` 里有它，逐回合 `Potions<...>` 里没有），
  混沌药水的补药逻辑也只是「已消耗就改给狡诈药水」⇒ 药水不会被她自己喝掉。
## 进度（第 25–27 轮，v0.2.619 / v0.2.620，2026-11-13：§5.2 恐惧增强补齐 7 只）

三轮都走完了「双目标编译 107/111 → `check-localization`（16 语言全过）→ 关游戏 → `install-mod.bat`」，
最终装机 **v0.2.620**（`[verify] 两份实现的版本串都是 v0.2.620  OK`）。
本轮之前 18 只 + §5.1 的 2 只 ⇒ **现在 27 / 34 只**有恐惧版特殊效果。

### 第 25 轮（v0.2.619）—— 4 只

| Boss | 恐惧版效果 | 落点 |
|---|---|---|
| 刀猎「袖里乾坤」 | 每次塞 **4** 张小刀（正常 3 张） | `Powers/SleeveTrickPower.ShivCountNow`（`DreadMode.IsActive` ⇒ 4）+ `SILENT_BOSS.special_dread`，`SILENT_BOSS.SpecialLocDreadByAct[2]` |
| 血肉骨「吞噬生命」 | 每次 **6** 格挡（正常 4）；**血肉戏法按升级版算**（9→13） | `Core/FleshBone.DevourBlockNow` / `SleightNow`（意图预测那两处也一起走新值）+ `BossDevourLifePower` + `NECROBINDER_BOSS.devour_life_dread` |
| 虚无骨「虚空之唤」 | **战斗开始就生效**（不必等她先打出女妖之嚎） | `BossVoidCallPower.AfterApplied` 的 `DreadMode.IsActive` 分支 + `NECROBINDER_BOSS.void_call_dread` |
| 王储「征服者」 | 麻痹不变，**再加「本回合 −1 力量」** | `BossConquerorPower.ParalyzePlayers` 里追加原版 `MonarchsGazeStrengthDownPower`（「王之凝视」，与 `KnowThyPlacePower` 同一写法）+ `REGENT_BOSS.conqueror_dread` |

### 第 26–27 轮（v0.2.620）—— 3 只

| Boss | 恐惧版效果 | 落点 |
|---|---|---|
| 根除骨「预借时间」 | 门槛 **10 → 6** 点能量 | `BorrowedTimeBossPower.ThresholdNow`（图标读数 / while 循环 / 日志全走新值）+ `NECROBINDER_BOSS.borrowed_time_dread` + **新加的** `Necrobinder.SpecialLocDreadByAct[2]` |
| 铸剑储「何人僭越」 | **不再扣他的铸造**，且**每回合没有触发次数上限** | `KnowThyPlacePower.AfterDamageReceived` 的 `dread` 分支（`cap = int.MaxValue`、跳过 `LoseForge`）+ `REGENT_BOSS.special_dread` + `Regent.SpecialLocDreadByAct[1]` |
| 碎屑储「彗星」 | 碎屑放到玩家**抽牌堆顶部**（正常是随机洗入） | `Patches/CometDebrisPatch` 的 `CardPilePosition` 按模式取 `Top` / `Random` + `REGENT_BOSS.comet_dread` + `Regent.SpecialLocDreadByAct[0]` |

### ⚠ 三条「用户口径 vs 现状」的差异（按 handover §5.2 的要求显式记下来）

1. **根除骨**：设计表写的「你每花 6 能量 ⇒ 你本回合所有牌 +1 费，且她 +1 能量」——
   **后半句（+1 费 + 她 +1 能量）正常模式就已经是这样了**（`BorrowedTimeBossPower` 里
   `PowerCmd.Apply<BorrowedTimePower>` + `PlayerCmd.GainEnergy(1m, ...)`）。
   ⇒ 恐惧版的差异化只落在**门槛 10 → 6**，没有另起一套。
2. **碎屑储**：设计表写的「她生成碎屑 ⇒ 反而加入**你的抽牌堆**」——
   `CometDebrisRedirect` 本来就已经是 `PileType.Draw`（v0.2.433 起）。
   ⇒ 恐惧版改成**抽牌堆顶部**（下一抽必定是碎屑）。
3. **王储**：设计表里还有一句「**王者之拳升级**」。这一条**没做**：
   卡组升级表（`BossVariant.AscensionUpgrades` / `BossScript` 的 `Upgraded`）是**静态**的，
   没有「按模式升级」的通道，硬做要动 `BossDefs` 初始化那套全局单例。
   本轮只落地了同一条里的「本回合 −1 力量（王之凝视）」。**下一轮若要补，需要先给
   `BossVariant` 加一个 `DreadUpgrades` 表 + 在 `CharBossMonster` 造手牌的地方按模式升级。**

### 顺手把「恐惧版文案」的产出方式改成 `derive`（省 13 种语言的翻译）

新增 `.tmp/apply-loc.ps1` 的 **derive 模式**：直接拿**同一语言**的正常模式文案（含标题行）改几个字，
用它生成 `_dread` 键 ⇒ 16 种语言全是各自语言的正版译文，不会再出现「英文正文塞进德/法/俄」的情况。
根除骨那一条（只把 10 改成 6）就是纯 derive 出来的。

### 还差 7 只（下一轮）

`灾厄骨`（时候未到：把「回合开始时消失的格挡」改为抵消等量灾厄）、
`灵魂骨`（灵魂绑定：**回合开始的那次抽牌也算**她抽牌）、
`无色储`（共鸣：玩家生成卡牌也触发创世之柱 + 增加超质量体伤害）、
`仆从储`（俯冲仆从仍 16 + 仆从 <1 改为召唤 + 她用牌得力量时友方等量获得）、
`轰击储`（① 光谱偏移 1 层 ② 击晕/唤醒节奏 ③ 醒来后轰击打她自己 —— **设计表这三条其实全是现状**，
需要按「落在数值或结构上做真正的强化」重新定口径）、
`辉星储`（设计表 §2.2 **没有这一行**，需要新设计）、
`养手骨`（设计表 §2.2 **没有这一行**，需要新设计）。
## 进度（第 28 轮，v0.2.621 / v0.2.622，2026-11-13：**按用户原始设计全文逐条查漏补缺**）

用户把**最初的设计全文**又贴了一遍，要求「查漏补缺」。逐条比对代码与原文之后，结论如下
（**订正了上一轮那份「还差 7 只」的清单 —— 里面有一半是我误判**）。

### A. 订正：前几轮**做偏了 / 做反了**的三条（本轮已改回用户原文）

| Boss | 上几轮做成了什么 | 用户原文 | 本轮处置（v0.2.618–0.2.622） |
|---|---|---|---|
| 涂毒贼「带毒刺击」 | 恐惧版中毒**不再能被格挡**（`Unpowered | Unblockable`） | 「中毒改为在你的回合结束时结算，且伤害**可以被格挡抵消**」—— 即与正常模式同义 | **回退**：两个模式都传 `ValueProp.Unpowered`（可格挡）。恐惧版的强化改落在**结算次数**上：同一份中毒在这一步 `iterations *= 2`。文案 16 语言已同步 |
| 巨石战「原始力量」 | 巨石 **+2 力量 +6 覆甲**（擅自翻倍） | 「会获得 **1 点力量和 3 点覆甲**」 | **回退**成 1 / 3（`BossPrimalForcePower`）。恐惧提升只保留你说的三条：执迷 −1 费（等效第 1/7 回合多打一张巨石+）、未格挡 ⇒ 1 层虚弱、以及意图改动。文案 16 语言已同步 |
| 能力机「偏差认知」 | 免疫偏差认知（对）；但**衍生的能力牌不算**（正常模式的旧规则） | PS：「现在即使打出的是**衍生能力**，也会为它增加集中」 | 恐惧模式下**跳过** `IsDerivedCard` 闸门（正常模式不变） |

另外三条**其实是「我误会了」**（原文那句本来就是正常模式的实现，不需要另做）：
`愤怒战`「打出愤怒 ⇒ 往所有玩家弃牌堆加一张复制品」（`IRONCLAD_BOSS.anger` 正文就是这句）、
`彩虹机`「黑暗球不触发被动 + 玻璃球警告」（`DEFECT_BOSS.rainbow` 已有）、
`状态机`「生成状态牌 ⇒ 复制品进你的弃牌堆」（`DEFECT_BOSS.status` 已有）。
`事件机`「牌从弃牌堆回手 ⇒ 它和友方 +1 力量」也已在 `DEFECT_BOSS.one_for_all` 里（写的是「所有单位」）。

### B. 本轮补上的结构缺口：**`DreadUpgrades` 通道**（恐惧模式专属升级）

原文里有两条要求是「升级某张牌」（黑球机 → 眼部攻击；王储 → 王者之拳），而卡组升级表
`AscensionUpgrades` 是**静态**的（按进阶等级，`BossDefs` 初始化时就定死），恐惧模式是**运行时**才知道的
⇒ 新开一条通道：

- `BossDefinition.DreadUpgradesByAct`（按幕）+ `BossDefinition.DreadUpgradeTypesFor(act)`（变体优先）
- `BossVariant.DreadUpgrades`
- `CharBossMonster.AscensionUpgradeTypes()` 末尾调 `AppendDreadUpgrades(result)`
  （两条 return 路径都加了）⇒ 卡组与剧本手牌里同名那张一起升级。

接线：`Defect.DreadUpgradesByAct[0] = { GoForTheEyes }`；`Regent` 的 `REGENT_KING` 变体
`DreadUpgrades = { MonarchsGaze }`。文案：`DEFECT_BOSS.dark_dread` 加一行「她的『眼部攻击』已升级」、
`REGENT_BOSS.conqueror_dread` 加一行「他的『王者之拳』已升级」（16 语言）。

### C. 顺手修的显示 bug

`IRONCLAD_BOSS.bash_dread` / `rending_dread` 里出现过 `**…**` —— 那**不是**游戏内格式标签，
会原样显示成星号。16 语言全部换成 `[gold]…[/gold]`（`content/localization/*/monsters.json` 里现在 **0 处 `**`**）。

### D. 仍然没做（**有 4 条需要你先定口径，我不替你发明**）

| # | Boss | 缺什么 | 为什么卡住 |
|---|---|---|---|
| 1 | **打击战** | 原文：「你的名字中包含打击的牌也会增加完美打击**和痛击**的伤害…**让痛击的伤害也和完美打击一样受到打击的加成**」 | 现在做的是「每张牌加成 +1」（`boss_bash_dread` 文案也是这么写的）。要改成「**痛击**这张牌自己也吃打击加成」得动 `BossScript.ApplyBashToPerfectStrike`（目前只认 `PERFECTED_STRIKE`）+ 意图预测，工作量中等，**不是一行**。留待下一轮 |
| 2 | **灵魂骨** | 原文 PS：「每回合**开始时的抽牌也算**」 | 现在只有「回合内抽牌」计入（`SoulBoundDrawPatch`）。要接 `Hook.BeforeHandDraw` / 回合开始抽牌那条入口 |
| 3 | **无色储** | 原文：「…**并增加超质量体的伤害**」 | `ResonancePower` 现在只做「玩家生成卡牌 ⇒ 也触发创世之柱」。超质量体那一半没接 |
| 4 | **仆从储** | 原文：「若其仆从数**不足 1**」（正常模式正文写的是**不足 2**）；以及「俯冲仆从仍是 16」 | `REGENT_BOSS.minion` 的正文与代码都是 2 ⇒ 需要改阈值 + 出恐惧文案 |
| 5 | **辉星储** | 原文：「升级黑洞（已做）。**替换第三回合的防御和第四回合的隐秘藏品+**，使得第四回合有足够星打七星」 | v0.2.595 核算过：0.113 的辉星收支是 3+2+2+1+1 = **9 ≥ 7**，**七星本来就能打**。你说「要换」⇒ 请指定**换成哪两张**，我照着换 |
| 6 | **灾厄骨** | 原文：「每当你的格挡将要因回合开始而消失时，它们将抵消等量灾厄」 | **这句就是 `NECROBINDER_BOSS.special` 的正文第一句**（正常模式已经实现了）⇒ 恐惧版要「更强的效果」是什么？需要你定 |
| 7 | **轰击储** | 原文四条（1 层光谱偏移 / 第 1 回合 1 张轰击后击晕 / 5 个无色回合后醒来 / 醒来后轰击打她自己） | `REGENT_BOSS.orbital` 的正文与实现**已经完全是这四条**（唯一差别是「第 1 回合被击晕、第 2 回合打轰击」vs 你写的「第 1 回合打 1 张轰击，之后被击晕」）⇒ 同样需要你定恐惧版怎么更强 |
| 8 | **碎屑储** | 原文：「反而将其加入**你的抽牌堆**」 | 正常模式**本来就是** `PileType.Draw`。我做成了「抽牌堆**顶部**」属于自定强化 ⇒ **要不要保留？** |
| 9 | 养手骨 | —— | 你的原文里养手骨只有「第 5 回合第一张来生 → 钙化+」，v0.2.594 已做 ⇒ **视为完成** |
## 进度（第 29 轮，v0.2.623，2026-11-13：按用户四条拍板 + 把剩下四条全部做完）

已装进游戏：`[verify] 两份实现的版本串都是 v0.2.623  OK`；107/111 双目标 0 错误；本地化 16 语言全过。

### 用户四条拍板的落地

| 拍板 | 落地 |
|---|---|
| **辉星储**：交换第 3 回合的防御与第 4 回合的隐秘藏品+（隐秘藏品+ 的「下回合辉星」第 4 回合才到账，这样七星打得出去） | `RegentStarScript` 新增 `Resolve = DreadTurnOverride`：恐惧模式下 3 = 群星斗篷/剑雨/**隐秘藏品+**、4 = **防御**/凡庸/七星。⚠ 用 `Resolve`（**运行期**求值）而不是在 `Build()` 里判 `DreadMode.IsActive` —— 后者在 `BossDefs` 静态初始化时恒为 false（同一个坑见「蛇眼猎·谋杀」）。文案 `REGENT_BOSS.stellar_dread` |
| **灾厄骨**：去掉「回合将要结束时用格挡和召唤物紧急抵消灾厄直到不会死」那条 | `NotYetPower.BeforeSideTurnEnd` 在 `DreadMode.IsActive` 时**整条直接 return**（①「回合开始用格挡抵消灾厄」照旧）。文案 `NECROBINDER_BOSS.special_dread` = 正常模式标题 + 第一句 + 一行恐惧说明（16 语言），接线 `Necrobinder.SpecialLocDreadByAct[0]` |
| **轰击储**：第一回合就打轰击（不是第二回合） | `RegentBombardScript` 的 `Resolve` 里 `bombardTurn = dread ? 1 : 2`。文案 `REGENT_BOSS.orbital_dread` |
| **碎屑储**：塞抽牌堆就行，回退 | `CometDebrisPatch` 回到 `CardPilePosition.Random`（撤掉 v0.2.620 的「堆顶」）。文案 `comet_dread` 也回到与正常模式一致 |

### 剩下四条（不需要再问，本轮全做完）

| Boss | 用户口径 | 落地 |
|---|---|---|
| **打击战** | 「你的名字中包含打击的牌也会增加完美打击**和痛击**的伤害…**让痛击的伤害也和完美打击一样受到打击的加成**」 | `BossBashPower.BashBonusDamage(boss)`（= `PerCard() × (牌堆统计 − 已打出打击)`）；`BossScript.BuildHand` 造到 `BASH` 时在**这一张实例**的 `Damage` 上加上去 ⇒ 卡面/意图/真实伤害一致。`IRONCLAD_BOSS.bash_dread` 整段按原文重写（撤掉 v0.2.605 那个「每张 +1」） |
| **灵魂骨** | PS「每回合**开始时的抽牌也算**」 | `SoulBoundDrawPatch` 不再排除 `fromHandDraw` / 「本回合还没出牌」那一类 ⇒ 恐惧模式下回合补牌也计入自动化与死亡行军 |
| **无色储** | 「…**并增加超质量体的伤害**」 | `ResonancePower.AfterCardGeneratedForCombat` 里 `creditHistory: DreadMode.IsActive`（玩家印的牌也写 `CardGeneratedEntry` ⇒ Supermassive 倍率跟着涨）。文案 `REGENT_BOSS.resonance_dread` = 正常 + 一行，接线 `Regent.SpecialLocDreadByAct[2]` |
| **仆从储** | 「仆从数**不足 1**」+「俯冲仆从伤害仍保持 16，仆从俯冲+ 也是 16」 | `BossCallToArmsPower.MinionCapNow`（恐惧 = 1），`MinionSummonPatch` 与 `BossScriptPlanner` 都读它（预告与实打永远一致）；`BossScript.BuildHand` 在恐惧模式把 `MINION_DIVE_BOMB` 实例伤害还原成 **16**。文案 `REGENT_BOSS.minion_dread` |

### 恐惧增强覆盖度

按用户这份**原始设计全文**逐条核对：**34 / 34 只都有对应的恐惧版特殊效果**（`SpecialLocDread` /
`SpecialLocDreadByAct` 覆盖全表）。剩下唯一的「非特殊效果」类条目也都做完
（刀猎第 4 回合精准+、养手骨第 5 回合钙化+、巨石战第 1/7 回合多打巨石+、辉星储换牌、轰击储 T1 轰击）。

### 下一轮真机验收要看的日志行

- 辉星储：第 3 回合打 `HIDDEN_CACHE`、第 4 回合 `SEVEN_STARS` **真的被打出**（不再「星不够出不了」）；
- 灾厄骨：恐惧下**不应再出现**「时候未到②」那批日志（`时候未到②`）；
- 轰击储：`BOMBARDMENT` 出现在第 **1** 回合的 `PLAY` 行；
- 打击战：`痛击（恐惧）：打击牌也加成痛击 ⇒ BASH 伤害 +N`；
- 仆从储：`（不足 1）⇒ 改为召唤`、以及 `「仆从俯冲」卡面伤害还原成 16`；
- 灵魂骨：回合补牌时也出现「镜像抽牌」日志。
## 进度（第 30 轮，v0.2.624，2026-11-13：灵魂骨文案补齐 ⇒ **34 / 34 全覆盖**）

自查「恐惧版特殊效果接线 + `_dread` 键」时发现最后一个缺口：**灵魂骨的行为改了、但没有恐惧版文案**
（`Necrobinder.SpecialLocDreadByAct[1]` 还是 null ⇒ 恐惧模式下会显示正常模式那一份）。

- 新增 `NECROBINDER_BOSS.soul_bound_dread`（16 语言）= 正常模式原文 + 一行
  「你[gold]每回合开始时抽到的牌也算[/gold]（回合补牌同样增加「自动化」计数与「死亡行军」伤害）」；
- `Necrobinder.SpecialLocDreadByAct[1]` 接上。

### 最终覆盖度自查（33 个 `_dread` 键 + 5 组按幕数组）

| 角色 | Boss（6 只） | 恐惧版入口 |
|---|---|---|
| 铁甲战士 | 打击战 / 杂耍战 / 巨石战 | 变体 `SpecialLocDread` |
| 铁甲战士 | 愤怒战 / 放血战 / 易伤战 | `SpecialLocDreadByAct[0..2]` |
| 静默猎手 | 蛇咬猎 / 蛇眼猎 / 炼药猎 / 华丽猎 | 变体 `SpecialLocDread` |
| 静默猎手 | 奇巧猎 / 涂毒贼 / 刀猎 | `SpecialLocDreadByAct[0..2]` |
| 故障机器人 | 电球机 / 彩虹机 / 事件机 / 状态机 | 变体 `SpecialLocDread` |
| 故障机器人 | 黑球机 / 物理机 / 能力机 | `SpecialLocDreadByAct[0..2]` |
| 亡灵契约师 | 血肉骨 / 虚无骨 | 变体 `SpecialLocDread` |
| 亡灵契约师 | 灾厄骨 / 灵魂骨 / 根除骨 | `SpecialLocDreadByAct[0..2]` |
| 亡灵契约师 | **养手骨** | 无（用户原文只给了「第 5 回合第一张来生 → 钙化+」，已在 v0.2.594 落地） |
| 储君 | 轰击储 / 王储 / 辉星储 / 仆从储 / 抉择储 | 变体 `SpecialLocDread` |
| 储君 | 碎屑储 / 铸剑储 / 无色储 | `SpecialLocDreadByAct[0..2]` |

⚠ 变体生效时，若该变体自己没填 `SpecialLocDread`，`SpecialLocForAct` 会**退回变体的正常文案**
（而不是按幕的恐惧文案）—— 所以「养手骨」在恐惧模式下显示的就是它正常的「不死」，符合预期。
## 进度（第 31 轮，v0.2.626 / v0.2.627，2026-11-13：支配改成 buff 实现 + 愚者的支配 + 支配图标）

已装进游戏：`[verify] 两份实现的版本串都是 v0.2.627  OK`；107/111 双目标 0 错误；本地化 16 语言全过。

### 改口径（用户原文）

> 故障机器人的支配和亡灵契约师的支配改成给所有玩家上一个「故障机器人的支配」或「亡灵契约师的支配」
> 的 buff，类似乐加维林祖母和无厌沙虫等 boss 的实现方法（你可以参考一下），效果不变
> （确保旧的实现方式取消了，别重复实现）
> 储君的支配的实现效果改成给所有敌人上一层「王之凝视」是 buff（是储君打能力牌王之凝视获得的那个
> 正面效果，你做一下适配，效果应该是和我之前描述的是一样的）
> 战士直接给敌人上恶魔形态，静默猎手直接 -1 力量并获得 1 虚弱，不用加别的 buff
> 添加愚者的支配：在每场战斗开始时，将 2 张悲伤加入你的抽牌堆。实现方法：也是给玩家上一个「愚者的支配」的 buff

### 参考的原版范式（反编译 `Dominions.LagavulinMatriarchDominion`）

```csharp
public override async Task AfterRoomEntered(AbstractRoom room) {
    if (room is CombatRoom cr)
        await PowerCmd.Apply<LagavulinMatriarchDominionPower>(
            new ThrowingPlayerChoiceContext(),
            cr.CombatState.Players.Select(p => p.Creature), 1m, null, null);
}
```

⇒ **支配本体只负责「进战斗房间时挂一层 power」，效果全在 power 里结算**（它自己那层也是 `PowerType.Debuff`）。

### 现在的五 + 一条

| 支配 | 实现 | 效果 |
|---|---|---|
| 铁甲战士 | `AfterRoomEntered` → 敌人挂 `DemonFormPower` | 战斗开始所有敌人 1 层恶魔形态（按用户口径**不加别的 buff**，未改动） |
| 静默猎手 | `AfterRoomEntered` → 玩家 `StrengthPower −1` + `WeakPower 1` | 同上（未改动） |
| **储君** | `AfterRoomEntered` → **所有敌人**挂原版 `MonarchsGazePower`（Buff / Counter） | 敌人用带攻击力的攻击打你 ⇒ 你获得 `MonarchsGazeStrengthDownPower`（临时 −1 力量、回合结束自动还）—— 与原来那条「每当敌人攻击你时你在本回合失去 1 点力量」**净效果一致** |
| **亡灵契约师** | `AfterRoomEntered` → 玩家挂 `Powers.NecrobinderDominionPower` | 你的回合开始时随机一张手牌本回合 +1 费（逻辑搬进 power） |
| **故障机器人** | `AfterRoomEntered` → 玩家挂 `Powers.DefectDominionPower` | 你的回合结束时每有一名敌人受 3 点伤害（逻辑搬进 power） |
| **愚者（新增）** | `AfterRoomEntered` → 玩家挂 `Powers.FoolDominionPower` | 每场战斗开始时把 **2 张「悲伤」`Sadness`** 随机洗进你的抽牌堆 |

**新增文件**：`src/Hooks/Powers/DominionPowers.cs`（`DefectDominionPower` / `NecrobinderDominionPower` /
`FoolDominionPower`）。

**旧实现已删除**（防重复结算）：
`DefectDominion.AfterSideTurnEnd`、`NecrobinderDominion.AfterPlayerTurnStart`、
`RegentDominion.AfterDamageReceived`（后者尤其重要 —— 不删就会和原版 `MonarchsGazePower` 各算一次，
变成 −2 力量）。

**版本门控**：`FoolDominionPower` + `FoolDominion` 在 `#if STS2_111` 内 —— 「悲伤」是 0.113 才有的状态牌，
107 编译实测报 `CS0234: 命名空间 Models.Cards 中不存在 Sadness`。另外三个 power 两版都能编。

**愚者的发牌时机**：`BeforeSideTurnStart(玩家侧)` 而不是 `AfterApplied` —— 支配是在「进房间」那一刻挂上的，
那时牌堆可能还没建好；回合开始前既保证牌堆就绪、又能被正常抽到，`_given` 闸门保证整场只发一次。

### 支配图标 = 对应角色头像（用户第 2 轮口径）

> 这 3 个 buff 的图标改成对应角色的头像。这三个 buff 不属于 boss 的特殊效果（和那些不是一类东西），
> 所以不用好奇图标。其它特殊效果还是用好奇图标

- `Patches/BossSpecialPowerIconPatch` 新增 `DominionIconOf` 分支：`DefectDominionPower` →
  故障机器人头像、`NecrobinderDominionPower` → 亡灵契约师头像、`FoolDominionPower` → 愚者头像
  （`Models.Characters.Fool`，0.113 确实有这个角色）。
- 路径按原版同一套拼法：`CharacterModel.IconTexturePath` 是 **private**（反编译：
  `ImageHelper.GetImagePath("ui/top_panel/character_icon_" + Id.Entry.ToLowerInvariant() + ".png")`），
  所以这里自己拼同样一串 ⇒ 和游戏顶栏那颗角色头像**是同一个文件**。
- 安全网：`ExistingOrNull(...)` 用 `ResourceLoader.Exists` 先验一下，路径不存在就**退回好奇灯泡**，
  不会出现空白图标。
- **其它所有本模组 power 仍然走好奇灯泡**（`IsOurPowerType` 那条没动）。

### 文案

`powers` 表新增 3 组键 × 16 语言：`DEFECT_DOMINION_POWER` / `NECROBINDER_DOMINION_POWER` /
`FOOL_DOMINION_POWER` 的 `.title` + `.description`。支配本身在 `ascension.json` 的
`DREAD.<角色>.description` **没改**（效果没变）。
## 进度（第 32 轮，v0.2.628，2026-11-13：「恐惧模式下特殊效果描述没改」——30 来个 Boss 全中）

用户：「恐惧模式下，黑球机的特殊效果描述没有改。这只是我查看的第一个 boss，你看看别的 boss 是不是也有这种情况？」

### 根因（一句话）

**常驻的「特殊效果说明框」读的是 `powers` 表，不是 `monsters` 表。**
`UI/BossSpecialTip` 与 `UI/BossSpecialUi` 都是拿 `power.HoverTips` 渲染的，而里那条说明来自

```csharp
// 反编译 MegaCrit.Sts2.Core.Models.PowerModel
public virtual LocString Description => new LocString("powers", base.Id.Entry + ".description");
```

而恐惧版文案之前**只写进了 `monsters` 表**的 `<KEY>_BOSS.<special>_dread` —— 那条只被
`BossDefinition.SpecialLocForAct` 用到（目前只有 `LockOnPower` 的悬浮提示会读）。
⇒ 除了当初**单独覆盖过 `Description`** 的**华丽猎「压制」**和**抉择储「指导」**，
**其余 32 个 Boss 在恐惧模式下显示的都还是普通模式那段文案**。用户第一个看到的是黑球机，正好属于这 32 个。

> 教训（写进 §E 的坑）：以后每加一条恐惧版文案，必须确认**显示它的那条链读的是哪张表**。
> 本次之前我只 grep 了「`SpecialLocDread` 有没有接上」，没验证「UI 到底读哪里」。

### 修法（一处后端 + 一批文案）

1. 新增 `Patches/DreadSpecialDescriptionPatch`：给 `PowerModel.Description` 挂**后置** ——
   恐惧模式下只要 `powers` 表里存在 `<ID>.description_dread` 就换成它。
   - 只对**真写了恐惧版文案**的 power 生效（原版 power 没有这个键 ⇒ 一个字节不动）；
   - 华丽猎 / 抉择储那两个自己覆盖了 `Description` 的类不走基类实现 ⇒ 不受影响；
   - 说明框 / 能力面板 / 悬浮提示三处**同时**生效。
2. 脚本从 `monsters` 的 `_dread` 条目**去掉标题行**，写成 `powers.<ID>.description_dread`（16 语言，共 32 组）：
   `IRONCLAD_RAGE_POWER` / `BOSS_RENDING_POWER` / `DOMINATE_WEAK_POWER` / `BOSS_BASH_POWER` /
   `BOSS_BURNING_PACT_POWER` / `BOSS_PRIMAL_FORCE_POWER` /
   `SHARED_FATE_POWER` / `POISON_STRIKE_POWER` / `SLEEVE_TRICK_POWER` / `BOSS_SNAKE_FORM_POWER` /
   `BOSS_WELL_LAID_PLANS_POWER` / `BREW_MOD_POWER` /
   `BOSS_COOLANT_POWER` / `SYNTHESIS_POWER` / **`SYNTHESIS_DREAD_POWER`** / `DEVIATION_POWER` /
   `BOSS_LIGHTNING_ROD_POWER` / `BOSS_RAINBOW_POWER` / `BOSS_ALL_FOR_ONE_POWER` / `BOSS_SYNC_POWER` /
   `NOT_YET_POWER` / `BOSS_SOUL_BOUND_POWER` / `BORROWED_TIME_BOSS_POWER` / `BOSS_DEVOUR_LIFE_POWER` /
   `BOSS_VOID_CALL_POWER` /
   `COMET_BOSS_POWER` / `KNOW_THY_PLACE_POWER` / `RESONANCE_POWER` / `BOSS_ORBITAL_BOMBARD_POWER` /
   `BOSS_CONQUEROR_POWER` / `BOSS_STELLAR_GATHERING_POWER` / `BOSS_CALL_TO_ARMS_POWER`
   标题行沿用正常版（`powers.<ID>.title` 不动）；`{Loss}` 这类占位符原样保留（它本来就是
   `IRONCLAD_RAGE_POWER.description` 的写法，说明框会给它代入）。
3. **顺带补掉一个老缺口**：`SYNTHESIS_DREAD_POWER` 之前**连 `.title` / `.description` 都没有**
   （物理机恐惧版那层 power 的图标会直接显示原始键名 `powers.SYNTHESIS_DREAD_POWER.title`）。
   现在补成「人工合成」+ 普通正文。

### 覆盖度自查（34 / 34）

| 角色 | Boss → power | 恐惧文案来源 |
|---|---|---|
| 铁甲 | 愤怒战 `IroncladRagePower`、放血战 `BossRendingPower`、易伤战 `DominateWeakPower`、打击战 `BossBashPower`、杂耍战 `BossBurningPactPower`、巨石战 `BossPrimalForcePower` | `powers.<ID>.description_dread` |
| 静默 | 奇巧猎 `SharedFatePower`、涂毒贼 `PoisonStrikePower`、刀猎 `SleeveTrickPower`、蛇咬猎 `BossSnakeFormPower`、蛇眼猎 `BossWellLaidPlansPower`、炼药猎 `BrewModPower` | 同上 |
| 静默 | **华丽猎 `BossSuppressionPower`** | 本来就有 `Description` 覆盖（`_dread` 键） |
| 故障 | 黑球机 `BossCoolantPower`、物理机 `SynthesisPower`+`SynthesisDreadPower`、能力机 `DeviationPower`、电球机 `BossLightningRodPower`、彩虹机 `BossRainbowPower`、事件机 `BossAllForOnePower`、状态机 `BossSyncPower` | `powers.<ID>.description_dread` |
| 亡灵 | 灾厄骨 `NotYetPower`、灵魂骨 `BossSoulBoundPower`、根除骨 `BorrowedTimeBossPower`、血肉骨 `BossDevourLifePower`、虚无骨 `BossVoidCallPower` | 同上 |
| 亡灵 | 养手骨 `BossOstyUndyingPower` | 无恐惧文案（用户原文只有按幕后改名）⇒ 显示普通版 |
| 储君 | 碎屑储 `CometBossPower`、轰击储 `BossOrbitalBombardPower`、铸剑储 `KnowThyPlacePower`、王储 `BossConquerorPower`、仆从储 `BOSS_CALL_TO_ARMS_POWER`、无色储 `ResonancePower`、辉星储 `BossStellarGatheringPower` | `powers.<ID>.description_dread` |
| 储君 | **抉择储 `BossGuidePower`** | 本来就有 `Description` 覆盖 |

### 验收看什么

恐惧模式里随便打一关，鼠标停到那只 Boss 的**特殊效果图标**上（或看左上角常驻说明框）：
· 黑球机应显示「战斗开始时，将 **1** 张「冷光」加入你的手牌…**她的「眼部攻击」已升级**」；
· 日志会有 `恐惧模式：特殊效果说明换成恐惧版（BOSS_COOLANT_POWER → powers/BOSS_COOLANT_POWER.description_dread）`。
`check-localization` 仍为「全部通过：16 个语言目录」（新键 16 语言齐备）。
## 进度（第 33 轮，v0.2.630 / v0.2.631，2026-11-13）

### ① v0.2.630：角色人称规范化

用户：「怎么全是『她』。铁甲战士和储君是男的用他，静默猎手和亡灵契约师是女的用她，故障机器人没有性别用它，
开发者说愚者的性别是树，用杝或它（优先前者）」。

- 权威记录另存 **`docs/plans/pronouns.md`**（性别表 + 「怎么判断一条文案属于哪个角色」+ `powers` 键→角色对照表）。
- 实际只有**铁甲战士和故障机器人**写错（静默/亡灵本来就是「她」，储君英文已经是 he）：
  中文 `zhs`+`zht` 8 条（`IRONCLAD_BOSS.anger_dread`、`IRONCLAD_RAGE_POWER.description_dread` → 他；
  `DEFECT_BOSS.special_dread`/`dark_dread`/`thunder_dread`、
  `BOSS_COOLANT_POWER`/`DEVIATION_POWER`/`BOSS_LIGHTNING_ROD_POWER` 的 `description_dread` → 它），
  英文 `eng` 同 8 条 + 其余 13 种语言的英文正文副本，共 112 处。
- 遗留（已写进 pronouns.md）：其余 13 种语言的**普通模式原生译文**要按各语言语法单独判断，暂不动；
  `mod_log` 里的「她」是开发日志、不分角色，也没动。

### ② v0.2.631：亡灵契约师的支配挑到了诅咒牌

用户：「亡灵契约师的支配似乎没有在第一回合增加随机手牌的耗能（或者它选到了不可打出的牌？）」

**日志实证**（时机是对的，锅在选牌）：

```
[20:43:04] 恐惧模式·亡灵契约师的支配：1 名玩家获得「亡灵契约师的支配」
[20:43:06] 亡灵契约师的支配：Silent 的「ASCENDERS_BANE」本回合费用 +1     ← 登顶者之灾，诅咒牌
```

- 顺带核实了触发时机：反编译 `CombatManager.SetupPlayerTurn` 的顺序是
  `ModifyHandDraw → BeforeHandDraw → CardPileCmd.Draw(fromHandDraw:true) → Hook.AfterPlayerTurnStart`
  ⇒ 我们挂 `AfterPlayerTurnStart` 时**手牌已经抽完**，时机本来就对。
- 修法：`NecrobinderDominionPower` 只从**打得出去**的手牌里随机挑 —— 复用
  `BossScriptPlanner.IsPlayable`（排除 `CardType.Status/Curse/Quest` 与带 `CardKeyword.Unplayable` 的牌，
  但「执迷」`ENTHRALLED` 是**可打出的诅咒**，单独放行），并把该判据从 `private` 改成 `internal` 以便复用。
  候选为空时打一行「没有可打出的牌 ⇒ 本回合不涨价」，日志也会带上「候选 N/M 张」。
### ③ v0.2.632：亡灵契约师的支配**从来没生效过**（API 参数搞错）

用户：「不对啊，每回合都没有手牌的耗能增加」。

**根因不是选牌，是 `CardEnergyCost.AddThisCombat` 的第二个参数**（反编译 0.113）：

```csharp
public void AddThisCombat(int amount, bool reduceOnly = false)
{
    if (amount != 0)
        _localModifiers.Add(new LocalCostModifier(amount, LocalCostType.Relative,
            LocalCostModifierExpiration.EndOfCombat, reduceOnly));
}
 ```

第二个参数是 **`reduceOnly`（只允许朝"降价"方向生效）**，不是「本回合」。
所以 `AddThisCombat(1, true)` 的实际含义是「加 1 费，但只允许它降低费用」⇒ **正数被整条吞掉**，
一次都没生效（从 v0.2.593 第一版支配起就错着，日志里那句「本回合费用 +1」是假象）。

正确接口是 **`AddThisTurn(1)`**（过期时机 `LocalCostModifierExpiration.EndOfTurn`），
正好对应设计里的「**在本回合**增加 1」。日志现在会打印费用前后值：

```
亡灵契约师的支配：Silent 的「STRIKE_GREEN」本回合费用 +1（1 → 2；候选 5/5 张）
```

> ⚠ 本模组其它几处 `AddThisCombat(-n, true)`（`BossScript` 的忧郁减费、`EventMachineMechanics`、
> `BossCardPreview`）是**负数**（减费），`reduceOnly: true` 恰好满足，**那些是对的，别跟着改**。
> 这条已记进 `.dshwolf/buglog.json`。
### ④ v0.2.633：加费的牌**不能因 SL 变化**（也不能联机两端不同）

用户：「怎么 sl 增加耗能的牌还会变？让耗能增加的牌不因 sl 变化」。

根因：挑牌用的是 `Random.Shared.Next(playable.Count)` —— **进程级随机数**：
SL 之后这条钩子若重跑一次就挑到另一张牌；联机时两端各摇各的（同一玩家在两边看到不同的加费牌）。

改法（`NecrobinderDominionPower.StableIndex`）：**确定性哈希**，输入只有三样，且这三样在
**SL 前后 / 联机两端 / 同一回合重算** 时都完全相同：

1. 本局种子 —— `BossLineups.RunSeed`（新增的 internal 访问器，取的就是 `RunState.Rng.Seed`）；
2. 该玩家的 `NetId`；
3. 当前「可打出的手牌」按手牌顺序排列的 `id.Entry` 序列（`IsUpgraded` 也掺进去）。

**故意不掺回合数 / 帧号 / 时间** —— 回合数在 SL 后可能对不上，而手牌序列本来就随回合变化
（抽牌不同），足够把不同回合区分开。⇒ 同一回合永远挑同一张。

同时做成**幂等 + 自愈**：

- 目标牌本回合**已经有**本地费用修正（`CardEnergyCost.HasLocalModifiers`）⇒ 判定为重入 / SL 后重跑
  ⇒ 跳过，**不会叠成 +2**；
- 反过来，若修正被 SL 丢掉（`HasLocalModifiers == false`）⇒ 这次会**自动补回** ⇒ SL 前后费用一致。

日志带 `稳定序号 N`，方便核对它是不是同一张。

> 通用教训（已记 buglog）：**任何「随机挑一张牌 / 一个目标」的效果都不能用 `Random.Shared`**
> —— 必须用「本局种子 + 玩家 NetId + 当前状态」的确定性哈希，否则 SL 会变、联机两端会分叉。
## 进度（第 34 轮，v0.2.634 / v0.2.635，2026-11-13）

### v0.2.634：文案 一/两 → 1/2

`zhs` + `zht` 共 **82 条**（6 张表）把「一/两」作数量词改成阿拉伯数字，**普通模式与恐惧模式都改**：
`第一次`→`第1次`、`一张`→`1张`、`两张`→`2张`（`一張`/`兩張` 同理）。成语「一层层」保留。

### ★ v0.2.635：影子玩家的「弃 N 张牌」一直是弃 0 张（一个藏了很久的根因）

用户：「华丽猎第 3 回合的隐秘匕首卡在空中，并且铭记死亡没有因为奇巧打出」。

**日志实证**（22:51:4x）：规划写了
`跳过 [TACTICIAN(被丢弃 → 奇巧自动打出), DEFEND_SILENT(被丢弃), MEMENTO_MORI(被丢弃 → 奇巧自动打出), …]`，
但实战手牌快照是 10 → 9 → 8 → 7 → 6，**每次只少了那张牌自己** —— 杂技、隐秘匕首各弃 **0** 张。
⇒ 两张带「奇巧」的牌一直躺在手里 ⇒ 奇巧永不触发；而隐秘匕首的**卡面停在空中**不落下
（弃牌那步没走完，`CardCmd.AutoPlay` 收不了尾）。**两个现象同一根因。**

链路反编译：`HiddenDaggers.OnPlay`（杂技/早有准备/生存者/隐秘匕首同一套）
= `CardSelectCmd.FromHandForDiscard(new CardSelectorPrefs(DiscardSelectionPrompt, DynamicVars.Cards))`
→ 只是给 `prefs.ShouldGlowGold` 设个委托就转发给 `FromHand`
→ `FromHand` 里 `ICardSelector selector = RunManager.Instance.CardSelectorStack.GetSelector(player);`
`selector != null` 才走选择器，否则走**真 UI**（影子玩家没有 UI ⇒ 空选择）。

**关键证据**：整场战斗 `mod_log` 里**一条 `自动选择:` 都没有** ⇒ 我们的 `BossCardSelector`
（每次被调用必打这行日志）**根本没被问到**。

**修法**：新增 `Patches/BossDiscardSelectionPatch` —— 给 `CardSelectCmd.FromHandForDiscard` 挂前缀，
**只对我们的影子玩家**（`player.Creature.Monster is CharBossMonster`）在**调用点**直接作答：
复用 `BossCardSelector.Instance.GetSelectedCards(候选, want, want)`
（内部逻辑本来就是「剧本 `DiscardInstead` 点名 → 奇巧优先 → 价值兜底」）。
`want` 取 `prefs.MinSelect`，取不到用 `MaxSelect`，都为 0 时按 1 张兜底 —— **绝不允许返回空集**。
真人玩家 / 原版怪物 / 别的 mod 一律 `return true` 走原版。

> 为什么不去修 `BossCardSelector.Push`：它在 111 上挂的是
> `RunManager.Instance.CardSelectorStack.PushSelector(player, selector)`，看着是对的，
> 但**实测这条路没被采纳**（栈可能被别人压了 null，或 `FromHand` 的取法变了）。
> 在调用点接管最稳，也不依赖栈状态、不会顶掉别人的选择器。

**验收看什么**：日志出现
`弃牌选择（接管影子玩家）：候选 N 张 → 弃 [MEMENTO_MORI,…]（要 2 张）`，
并且紧跟 `MEMENTO_MORI` 的自动打出；`小刀段数诊断：HIDDEN_DAGGERS … 剧本丢弃=2 张`（从 1 条变 2 条）。
### v0.2.636：恐惧模式·华丽猎 第 6 回合「尖啸」→「精准+」

用户：「增强恐惧模式下的华丽猎，把第 6 回合的尖啸换成精准+」。

- `Core/GrandFinaleSilentScript` 新增 `Resolve = DreadTurnOverride`：恐惧模式下 `turn == 6` 时返回
  `ToolsDiscard(Discard<DefendSilent>()) , C<Accuracy>(true) , C<StrikeSilent>() , C<Survivor>()`，
  能量仍是 3 = **精准+ 1** + 打击 1 + 生存者 1，正好打完。
  ⚠ 用 `Resolve`（运行期求值）而不是在 `Build()` 里判 `DreadMode.IsActive`：`BossDefs` 静态初始化时它恒为 false。
  第 6 回合不在 `LoopFrom=8 / LoopLength=3` 的循环里，按回合号覆盖安全。
  ⚠ **v0.2.638 才修对**：这一版把「精准」误认成 `PreciseCut`（**精确切击**，0 费），
  那是**炼药猎第 5 回合**的牌，华丽猎卡组里没有 ⇒ 详见下面的 v0.2.638 节。
- 卡组补 `typeof(Accuracy)`（= 精准，小刀额外 +4/升级 +6）。普通模式这张牌不会进手牌：
  本变体 `BlocksVanillaDraw = true`，抽牌被拦、手牌完全由剧本给。
- 文案：`SILENT_BOSS.suppression_dread` 与 `BOSS_SUPPRESSION_POWER.description_v113_dread`
  各追加一行「恐惧模式：她第6回合的[gold]尖啸[/gold]已换成[gold]精准[/gold]+。」（16 语言）。

**验收**：恐惧模式下第 6 回合的日志应是
`剧本第 6 回合: 打 [ACCURACY,STRIKE_SILENT,SURVIVOR] 跳过 [DEFEND_SILENT(被丢弃)]`。
### v0.2.637：`FromHand` 也接管（夜魇 / 手上技法 / 夜宴 卡在空中）+ 变体串台排查

用户：「1.她的夜宴卡在空中。2.她第六回合起的意图变成炼药猎的了，你搞串了？别的有搞串的吗？还有手上技法也卡在空中」。

**① 卡在空中（真 bug，已修）**：v0.2.635 只接管了 `CardSelectCmd.FromHandForDiscard`，
但「夜魇」（`NIGHTMARE`，要从手牌里**选一张来复制**）、「手上技法」（`HAND_TRICK`，要**选一张技能牌加奇巧**）、
「夜宴」（同族「从手牌里挑一张」类效果）走的是**另一条** `CardSelectCmd.FromHand`。
影子玩家不在 `CombatState.Players` 里 ⇒ 原版选择器等不到任何输入 ⇒ 那张牌**永远悬在空中**，
整条剧本连锁停摆（后面的牌也不打）。

修法：同一个文件里加 `BossHandSelectionPatch`，前缀挂 `CardSelectCmd.FromHand`
（5 参签名 `PlayerChoiceContext, Player, CardSelectorPrefs, Func<CardModel,bool>, AbstractModel`），
同样只在 `player.Creature.Monster is CharBossMonster` 时在**调用点**作答：
`want = prefs.MinSelect` → 否则 `MaxSelect` → 否则 1，夹到候选数；用
`BossCardSelector.Instance.GetSelectedCards(候选, want, want)`，**绝不返回空集**。
「抉择储」的 `DECISIONS_DECISIONS` 例外放行给 `Patches/ChoiceRegentPickPatch`，避免两处抢答。

**② 「意图变成炼药猎」：查了日志，不是串台（变体判定全程没变过）**

同一场（23:00:18 开打、23:03:07 她死）的 `mod_log.txt` 里，变体锁定只有一次：

```
[23:00:18] 变体判定（本场锁定）：SILENT_BREWER = False（来自同步数据；换场后才会重算）
[23:00:18] 变体判定（本场锁定）：SILENT_GRAND_FINALE = True（来自同步数据；换场后才会重算）
```

此后到战斗结束**没有任何** `锁定清空` 行 ⇒ `ActiveVariant(2)` 全程是 `SILENT_GRAND_FINALE`，
`Deck/Script/SpecialLoc` 都取同一套。第 6 回合的规划也确实是她自己的
`剧本第 6 回合: 打 [PRECISE_CUT,STRIKE_SILENT,SURVIVOR] 跳过 [DEFEND_SILENT(被丢弃)]`。
⇒ 视觉上「像炼药猎」的原因更可能是 **①的连锁停摆**：卡在空中的那几张让剧本推进错位，
画面上留着别的回合/别的牌的意图。两者一起在 v0.2.637 修，实机复验。

**③ 同类风险的系统性排查（结构层面已经安全）**

- 判定顺序 `VariantsByAct[act]` → `ExtraVariants[act]`，取**第一个** `Enabled()` 为真的；
- 每个 `Compute*` 都先读**房间遭遇**、再读**存档里的主 Boss 遭遇反查阵容**，两条都命中就返回
  `Decisive = true` 并**本场锁定**；只有第三条「按种子」才 `Decisive = false`（不锁定、下次重算）；
- 关键：`ComputeBrewer` 里**显式**把 `SILENT_GRAND_FINALE_BOSS_ENCOUNTER` 判否（v0.2.520 修过同一类串台），
  `ComputeGrandFinale` 反过来把 `SILENT_BREWER` / `SILENT_SHIV` 判否 —— 同层的三套设计互不抢场。
- `Latched` 只在 `CombatManager.DebugOnlyGetState()` 的对象引用变了时才清表（=换场/新战斗），
  所以**不可能**在战斗中途换卡组。恐惧模式的 34 个 Boss 变体走的都是这一条路。

**版本**：`src/CharacterBosses.json` = `0.2.637`，`BuildInfo.Tag` 前缀同步；双目标（107 / 111）0 错误；
`check-localization.ps1` 16 语言全通过；`install-mod.bat` 报
`[verify] 两份实现的版本串都是 v0.2.637  OK`。

**验收看什么**：日志出现 `选牌（接管影子玩家，FromHand）：候选 N 张 → 选 [ACROBATICS]（要 1 张，发起者=NIGHTMARE）`、
`弃牌选择（接管影子玩家）…`，且「夜魇」之后紧接着出现复制品的奇巧/打出记录；画面上不再有停在空中的牌。

### v0.2.638：「精准+」用错牌了 —— 是 `Accuracy`（精准），不是 `PreciseCut`（精确切击）

用户：「不是，她都打精确切击了，华丽猎卡组里压根就没有这张牌，那就是炼药猎第五回合的意图（不过没有喝药）」。

**结论：用户判断完全正确，是我的锅，不是变体串台。**

`docs/plans/cards-silent.md`（自动生成的卡表）里这两张是两张不同的牌：

| 费用 | 稀有度 | 中文名 | 效果 | 类名 |
|---|---|---|---|---|
| 0 | 罕见 | **精确切击** | 造成[CalculatedDamage]点伤害；手牌中每有一张牌，此牌伤害 −2 | `PreciseCut` |
| 1 | 罕见 | **精准** | 小刀额外造成 4 点伤害（升级 6） | `Accuracy` |

用户嘴里的「精准+」= **`Accuracy` 升级版**（1 费、给小刀加伤）。上一轮我把它当成了零费的
`PreciseCut`（精确切击）—— 而 **精确切击正好是炼药猎第 5 回合的牌**，所以画面上那一回合的意图
看起来就像炼药猎的（用户还注意到「没有喝药」这个不符合炼药猎的细节，正是因为它本来就只是那一张牌的重合）。

⇒ **撤回 v0.2.637 里 ② 的推断**：变体从头到尾没变（日志里 `SILENT_GRAND_FINALE = True` 锁定到战斗结束，
这条证据仍然成立），但第 6 回合打出的那张牌**确实**是炼药猎的牌 —— 因为那是我写进剧本的。
「别的 Boss 有没有搞串」的排查结论（③）依然有效：那不是变体判定层面的问题。

**修法**（`src/Hooks/Core/GrandFinaleSilentScript.cs`）：

- `DreadTurnOverride(6)`：`C<PreciseCut>(true)` → `C<Accuracy>(true)`；
  能量 3 = 精准+ 1 + 打击 1 + 生存者 1，**正好打完**（上一版按 0 费算还担心会剩费，其实刚好）；
- `Deck()`：`typeof(PreciseCut)` → `typeof(Accuracy)`（`PiercingWail` 仍留给普通模式）；
- 本地化**不用改**：16 语言里写的一直是「尖啸 → 精准」，没有出现「精确切击」字样（已 grep 确认）。

**验收**：恐惧模式第 6 回合日志应为
`剧本第 6 回合: 打 [ACCURACY,STRIKE_SILENT,SURVIVOR] 跳过 [DEFEND_SILENT(被丢弃)]`。

> 教训（已记 buglog）：用户给的中文牌名**必须先到 `docs/plans/cards-*.md` 里查类名**，
> 别凭字面近似手写 ——「精准」和「精确切击」一字之差、两张完全不同的牌，而且在别的 Boss 的剧本里存在。

### v0.2.639：选牌卡全部卡在空中的**唯一根因**（洁净 / 夜魇 / 夜宴 / 手上技法 / 隐秘匕首…）

用户：「洁净也会卡在空中。你看看还有哪些牌涉及选牌？一起修一下」。

**根因（`mod_log` 实证，49 行）**：

```
选牌器挂载失败（不挂也能出牌，只是需要选择的牌可能卡住）:
  Couldn't get selector stack for player 18446744069414631638!
```

反编译 0.113 的 `CardSelectorStack`：

```csharp
public ICardSelector? GetSelector(Player player)              // 每个选牌入口开头都调它
{
    var stack = GetSelectorStackForPlayer(player);
    if (stack.Count == 0) return _testSelector;
    return stack.Peek();
}

private Stack<ICardSelector> GetSelectorStackForPlayer(Player player)
{
    int slot = _playerCollection.GetPlayerSlotIndex(player);
    if (slot < 0 || slot >= _selectorStacks.Count)
        throw new InvalidOperationException($"Couldn't get selector stack for player {player.NetId}!");
    return _selectorStacks[slot];
}
```

**影子玩家不在 `_playerCollection.Players` 里**（它不是真玩家）⇒ `GetPlayerSlotIndex` 返回 −1
⇒ `PushSelector` 抛、`GetSelector` 也抛。所以 `BossCardSelector` **从 0.113 起一次都没挂上过**：
所有要选牌的牌都掉进「真 UI」分支（`ShouldSelectLocalCard`），影子玩家没有 UI ⇒
`OnPlay` 永远 await 在那里 ⇒ **卡面停在半空**，整条剧本连锁停摆。
v0.2.635 / v0.2.637 只在 `FromHandForDiscard` / `FromHand` 两个方法上打补丁，
等于在一条有 17 个入口的河上堵了两个口 —— 「洁净」走的 `FromCombatPile`（从**抽牌堆**里选）就没堵上。

**修法（`Patches/BossSelectorStackPatch`，`#if STS2_111`）**：只在 `GetSelector` 这一层接管 ——
只要问的是我们的影子玩家，直接返回 `BossCardSelector.Instance`：

```csharp
[HarmonyPatch(typeof(CardSelectorStack), nameof(CardSelectorStack.GetSelector), new[]{ typeof(Player) })]
static bool Prefix(Player player, ref ICardSelector? __result)
{
    if (player?.Creature?.Monster is not CharBossMonster) return true;   // 真人/原版怪/别的 mod
    if (SelectionCompat.IsSuspended(player)) return true;                // 抉择储的「指导」要真界面
    __result = BossCardSelector.Instance;
    return false;
}
```

`CardSelectCmd` 的 17 个入口（`FromHand` / `FromHandForDiscard` / `FromCombatPile`×2 /
`FromSimpleGrid` / `FromSimpleGridForRewards` / `FromChooseACardScreen` / `FromDeckForUpgrade` /
`FromDeckForTransformation` / `FromDeckForEnchantment`×3 / `FromDeckForRemoval` / `FromDeckGeneric` /
`FromHandForUpgrade` / `FromChooseABundleScreen`）开头都是同一句 `GetSelector(player)`，
所以**一处接管全覆盖**，将来新增入口也不用再改。

配套改动：

- **删掉** `Patches/BossDiscardSelectionPatch.cs`（v0.2.635 的 `FromHandForDiscard` 版 +
  v0.2.637 的 `FromHand` 版）—— 既已冗余，又和 `ChoiceRegentPickPatch` 同为 `FromHand` 前缀、
  存在 Harmony 前缀调用顺序不确定的隐患（万一我们先返回 false，抉择储的界面就永远不出现）。
- `Core/SelectionCompat.Push` 在 111 下改成**空操作**（不再尝试 push），顺手消掉那 49 行噪音日志；
  107 仍是老的全局栈那条路。
- `Core/SelectionCompat.SuspendForUi` 对影子玩家改成**自管挂起标记**（以前压 `null` 也会抛
  `Couldn't get selector stack` ⇒ 抉择储的界面同样出不来）；对真人玩家仍走压 `null`。
  `BossSelectorStackPatch` 看到挂起标记就放行原版 ⇒ 真界面正常出现。

**我们 Boss 用到「选牌」的牌（全在这条链上，一次全修）**：

| 牌 | 类名 | 选牌入口 | 用在 |
|---|---|---|---|
| 夜魇 | `Nightmare` | `FromHand` | 华丽猎 / 炼药猎 |
| 洁净 | `Cleanse` | `FromCombatPile`（抽牌堆） | 三层亡灵契约师 |
| 模仿学习 | `ImitationLearning` | `FromHand`（选另一名玩家） | 彩虹机 |
| 灵魂绑定 | `Soulbound` | 选盟友 | 棺骨一族 |
| 抉择，抉择 | `DecisionsDecisions` | `FromHand`（**例外**：弹真界面） | 抉择储 / 王储 |
| 类星体 | `Quasar` | 三选一网格 | 仆从储 |
| 下去！/ 冲锋！！ | `Begone` / `Charge` | `FromHand` / `FromCombatPile` | 仆从储 |
| 发现 | `Discovery` | 三选一网格 | 储君 |
| 隐秘匕首 / 杂技 / 钢铁风暴 / 生存者 / 早有准备… | `HiddenDaggers` / … | `FromHandForDiscard` | 各层静默猎手 |
| 手上技法 / 准备妥当 / 夜宴… | `HandTrick` / … | `FromHand` | 华丽猎 / 奇巧猎 |
| 净化 / 秘密技法 / 秘密武器 / 探寻打击 / 计策 / 飞溅 | `Purity` / `SecretTechnique` / `SecretWeapon` / `SeekerStrike` / `Stratagem` / `Splash` | 各自的 `FromHand` / `FromCombatPile` / 网格 | 无色牌（任何 Boss 都可能带） |

> 另有一族**不是** `CardSelectCmd` 的选择入口：`RelicSelectCmd`（选遗物）。我们的 Boss 目前没有
> 用它的牌；将来若加了「让你选一件遗物」这类效果，需要单独接管。

**验收看什么**：`mod_log` 里 `选牌器挂载失败` 应该**一条都没有**，
并且打「洁净 / 夜魇 / 手上技法」时会出现 `自动选择: 候选 N 个（min=1 max=1）→ 选 […]` 那一行，
卡面正常落下、后面的牌继续打。

### v0.2.640：抉择储的「指导」变成她自己挑牌 —— `FromChooseACardScreen` 最多只支持 3 张

用户：「恐惧模式下的抉择储怎么变成自己出牌而不是玩家选了？」

**日志实证**（10:15:09，同一手牌连报两次）：

```
指导：储君打出「抉择，抉择」⇒ 让 静默猎手 从 5 张里挑一张（HIDDEN_CACHE,BULWARK,ALIGNMENT,RESONANCE,SHOCKWAVE）
指导：选择界面出错，退回第一张: Only works with less than 3 cards (Parameter 'cards')
指导：玩家没给出选择 ⇒ 退回第一张 HIDDEN_CACHE
指导：玩家替她挑了 HIDDEN_CACHE（本回合第 1 张）
```

反编译 `CardSelectCmd.FromChooseACardScreen` 第一句：

```csharp
if (cards.Count > 3)
    throw new ArgumentException("Only works with less than 3 cards", "cards");
```

也就是说这个「选一张」界面**硬上限 3 张**（原版 `Discovery`/`Quasar` 那种三选一用的就是它）。
而用户的设计是「会有 5 张牌」（恐惧模式第二次再挑还剩 4 张）⇒ 必抛 ⇒ 被 `catch` 接住
⇒ 退回 `candidates[0]` ⇒ 画面上就是「她自己挑了第一张」。
（普通模式第一挑同样是 5 张，所以其实两种模式都坏，只是恐惧模式多打一次更显眼。）

**修法**（`Patches/ChoiceRegentPickPatch.ChooseAsync`）：换成**网格选牌**

```csharp
var prefs = new CardSelectorPrefs(new LocString("monsters", "REGENT_BOSS.choice_prompt"), 1, 1);
using (Core.SelectionCompat.SuspendForUi(chooser))            // 让真界面出现
    var pickedList = await CardSelectCmd.FromSimpleGrid(context, candidates, chooser, prefs);
```

`FromSimpleGrid`（`NSimpleCardSelectScreen`，牌库/弃牌堆那种网格）**不限张数**，
`prefs.MinSelect(1) < 候选数` ⇒ 一定走 UI（不会自己全选）。
提示语用我们自己的本地化键 **`REGENT_BOSS.choice_prompt`**（16 语言：
「选择一张要被重复打出的牌」/「選擇一張要被重複打出的牌」/ Choose a card to be played repeatedly …）。

**验收**：日志应变成
`指导：储君打出「抉择，抉择」⇒ 让 <玩家> 从 5 张里挑一张（…）` 之后**不再**出现
`选择界面出错`，而是弹网格等玩家点；点完那行 `指导：玩家替她挑了 <牌>（本回合第 1 张）`
里的牌应该就是玩家点的那张。恐惧模式接着再弹一次（剩下 4 张）。

### v0.2.641：抉择储的「指导」恢复**知识恶魔界面**（张数限制只在入口，不在界面）

用户：「肯定用知识恶魔的界面，因为之前就没出过问题」。

**先说清事实**（免得下次再怀疑是版本改的）：

- `if (cards.Count > 3) throw new ArgumentException("Only works with less than 3 cards")` 这句在
  **0.107 和 0.113 都有**（两份 DLL 反编译逐字相同，`sts2_107 L357/L2689`、`sts2_111 L302/L3003`）
  ⇒ 不是游戏更新加的；
- `ChoiceRegentPickPatch.cs` 从 v0.2.559 建文件到本次之前 `git log` 只有一条提交
  ⇒ v0.2.639 动的是选牌器栈，没碰这条路径；
- 所以「≤3 张时界面正常」是真的（知识恶魔界面本身就能用），「5 张时」从第一天起就会抛
  ⇒ 只是抛完静默回退成「她挑第一张」，看起来像正常。

**关键发现**：那道检查在 `CardSelectCmd.FromChooseACardScreen` 这个**外层静态方法**里，
**界面本身（`NChooseACardSelectionScreen.ShowScreen`）没有张数限制**。所以不必换界面 ——
把那个方法「拆开逐句照抄」就行（`ChooseViaKnowledgeDemonAsync`）：

1. `UndoEndTurnIfNecessary`（111 = `TryUndoReadyToEndTurn`，107 = `UndoReadyToEndTurn`）；
2. `PlayerChoiceSynchronizer.ReserveChoiceId` → `context.SignalPlayerChoiceBegun`
   （107 是 1 参数版，111 是 2 参数版 ⇒ `#if` 分开写）；
3. 本机玩家 ⇒ `NPlayerHand.CancelAllCardPlay()` + `NChooseACardSelectionScreen.ShowScreen(候选, false)`
   → `await CardsSelected()` → `PlayerChoiceResult.FromIndex(下标)` → `SyncLocalChoice`；
4. 其它客户端 ⇒ `WaitForRemoteChoice` + `AsIndex()`；
5. `SignalPlayerChoiceEnded`。

⇒ **视觉与交互就是知识恶魔那个界面**（同一棵场景树、同一套动画），只是不再限 3 张。
`candidates.Count <= 3` 时仍走原版 `FromChooseACardScreen`（逐字不变，零风险），
只有超过 3 张才走这份复刻。

> v0.2.640 那版用的网格界面（`FromSimpleGrid` / `NSimpleCardSelectScreen`）已按用户要求撤掉；
> 顺带加的本地化键 `REGENT_BOSS.choice_prompt` 留在 monsters 表里（16 语言齐、当前未引用）。

**验收**：第 1 回合她打「抉择，抉择」→ 弹**知识恶魔界面**、5 张牌等你点 →
日志不再有 `选择界面出错`，`指导：玩家替她挑了 X` 里的 X = 你点的那张；
恐惧模式紧接着再弹一次（剩 4 张）。

### v0.2.642：恐惧抉择储第 1 回合「第二次挑星位序列」不出天际钻头（补打顺序反了）

用户：「第一回合第二次选择星位序列他没有出天际钻头」。

**根因**（`Monsters/CharBossMonster.Choice.cs` 的 `RunChoiceFollowUpsAsync` 顺序）：

```csharp
await PlayChoiceGatedCardsAsync(ctx, pcs);                 // ① 先补打「点名牌」（天际钻头）
if (DreadMode.IsActive && TryBeginDreadSecondPlay(...))    // ② 再打第二张「抉择，抉择」
    await PlayDreadSecondDecisionsAsync(ctx, pcs);
```

①那一刻 `ChoiceRegent.AllPicksFor` 里**只有第一挑**，第二挑还没发生 ⇒
`picks.Contains(typeof(Alignment))` 为假 ⇒ 天际钻头永远补不出来。
（顺带说明：`ChoiceRegent.EntryAllowed` 早就写成「命中任意一张都算」，问题纯粹是时机。）

**修法**：把 ① 挪到 ② 之后。这也正是用户原设计里的顺序 ——
「5 张里选 1 → 打出 3 次 → 剩下 4 张里选 1 → 再打出 3 次 → **继续后面的牌**」。
两张都挑完之后：

- 第二挑选**星位序列** ⇒ `picks = [X, Alignment]` ⇒ 判定命中 ⇒ 补打天际钻头（X 费 = 当时的能量，
  星位序列 3 次 ×+2 费刚好把费给回来）；
- 第一挑选**星位序列** ⇒ 第二挑会自动排除已挑过的牌 ⇒ `picks` 里仍然只有一张 Alignment
  ⇒ 只补打一次，不会打两次；
- 天际钻头的类型是 **攻击牌**（0 费 X 费），不在「抉择，抉择」的技能牌候选里 ⇒ 不会反过来被玩家挑走。

**验收**：第 1 回合日志应出现
`指导（恐惧）：第二张也打完了（重复打出 3 次）⇒ 继续她后面的牌` 之后紧接
`抉择：玩家选了 Alignment ⇒ 补打 HEAVENLY_DRILL（费 N、当前能量 N、星光 …）`。

### v0.2.643：Boss 的攻击特效统一朝左（面向玩家）

用户：「目前 boss 的大部分牌的攻击特效都是反的，比如彗星、震荡波、冲刺、匕首雨等等（但我记得君王之剑好像被我专门调过，不是反的）。你看看能不能让他们的攻击牌的攻击特效朝左？（例外：轰击储醒来后即轰击打自己时轰击的动画保持原版，但轰击打玩家的时候要朝左）」。

**为什么是反的**：这批 Boss 复用**玩家角色**的立绘与卡牌，而玩家永远站在**左侧朝右**打 ——
立绘朝右画（所以 `BossVisuals.FaceThePlayer` 要把 `Body.Scale.X` 取负）、卡牌特效也全都写死朝右：

| 牌 | 反编译看到的写法 |
|---|---|
| 匕首雨 | `NDaggerSprayFlurryVfx.Create(owner.Creature, color, **goingRight: true**)` |
| 震荡波 | `VfxCmd.PlayOnCreatureCenter(owner.Creature, "vfx/vfx_flying_slash")` |
| 君王之剑 | `WithHitVfxNode(NBigSlashVfx.Create)` → `Create(pos, **facingRight: creature.IsEnemy**)` |

放到敌方一侧（玩家在左、Boss 在右）就全背对玩家。
**「君王之剑」看着是对的**：它的特效落在**被打的玩家**身上，玩家那侧的方向本来就对
（用户记得没错，不是特调过，是它天然落在正确的一侧）。

**做法**：只在**她的一张牌结算期间**，把新加进场景的 VFX 节点水平镜像。

- 唯一落地口：`MegaCrit.Sts2.Core.Helpers.GodotTreeExtensions.AddChildSafely(Node parent, Node? child)`
  —— `VfxCmd.PlayVfx` / `PlayOnCreature` / `PlayOnCreatureCenter` / `PlayFullScreenInCombat` /
  `PlayNonCombatVfx` 内部全是 `parent.AddChildSafely(node)`；攻击构建器的
  `WithAttackerFx(Func<Node2D?>)` / `WithHitVfxNode(...)` 也是直接 `container.AddChildSafely(node)`
  ⇒ **补这一个方法**就覆盖两条路径，不必逐个 VFX 场景改（原版以后新增的 VFX 也自动覆盖）。
- 只翻「类名带 `Vfx` 的 `Node2D`」且**只翻 `Scale.X > 0`** 的（已经朝左的不动）⇒ 幂等；
  出牌期间加进来的卡面节点、伤害数字、飘字一律不碰。
- 窗口由 `CharBossMonster.PlayOne` 用 `BossAttackVfxFacing.Begin/End` 包住 `CardCmd.AutoPlay` 那一段
  （`try/finally` + 同步抛出也补一次 `End`）⇒ 玩家出牌、其它怪物出牌、UI 动画完全不受影响。
  她牌里**嵌套**自动打出的牌（钢铁风暴的小刀、夜魇的复制品…）也在窗口内，自动覆盖。

**例外（用户要求）**：`轰击储` 醒来后那一手「轰击」是**打她自己**的 ⇒ 这一手整体**不翻**
（`Begin(牌名, targetsSelf: play.Target == 她自己的 Creature)`）。而「轰击打玩家」时照常朝左。
（补充：她醒来后每回合从**消耗堆复燃**的那次自伤是原版 `Bombardment` 内部自己 `AutoPlay` 的、
不走 `PlayOne`，本来就不在窗口里 ⇒ 天然保持原版动画。）

**验收**：日志出现
`攻击特效已水平镜像（朝左面向玩家）：NDaggerSprayFlurryVfx` 之类若干行；
打轰击储的自伤那一手会出现
`攻击特效朝向：BOMBARDMENT 打的是她自己 ⇒ 这一手保持原版方向（不镜像）`。

### v0.2.644：攻击特效镜像两处失效（震荡波仍然反着）

用户实测反馈：「震荡波还是反的」。日志里一行 `已水平镜像` 都没有，只有一串
`攻击特效朝向：CHILD_OF_THE_STARS / VENERATE / ROYALTIES / DECISIONS_DECISIONS 打的是她自己 ⇒ 这一手保持原版方向（不镜像）`
—— 说明**窗口开着，但每一张牌都被判成「打她自己」**。两个 bug：

**① `targetsSelf` 判据错**：v0.2.643 用 `play.Target == 她自己的 Creature`。
但 `play.Target` 是 `BossPlanner.ChooseTarget(this, card) ?? Creature` ——
**群攻牌**（`AllEnemies`，震荡波就是）没有单一目标 ⇒ `ChooseTarget` 返回 null ⇒ 兜底成她自己
⇒ 每张牌都被当成「打自己」⇒ 全都不翻。

现在的判据按**牌的 `TargetType`**：只有目标落在她自己这一侧
（`Self` / `AnyAlly` / `AllAllies` / `Osty`）才算自伤、才不翻。
「轰击储自伤」本来也不受影响 —— 那次是 `Patches/AutoPlayTargetForRealPlayerPatch`
在 `CardCmd.AutoPlay(card, null)` 里把目标改掉的，**不经过 `PlayOne`** ⇒ 本来就不在窗口里，天然是原版动画。

**② 类名过滤太窄**：「震荡波」用的特效是 `VfxCmd.PlayOnCreatureCenter(她, "vfx/vfx_flying_slash")`，
那个场景的根节点运行时类型**不叫 `*Vfx`**（就是个带脚本的 `Node2D`）⇒ 被 `类名含 "Vfx"`
那条筛掉，永远翻不到。

现在改成两级判据，命中任一即翻：
1. 类名含 `Vfx`（`NDaggerSprayImpactVfx` / `NBigSlashVfx` / `NHitSparkVfx` …）；
2. **父节点往上能找到 `NCreature`** ⇒ 一定是画在某个生物身上的特效（`PlayOnCreature` / `PlayOnCreatureCenter` 那条路全是）。

并显式排除 **`NDamageNumVfx`（伤害数字）** —— 它也是 `*Vfx`，但画的是文字，镜像后数字会左右反着写。

**验收**：日志应出现
`攻击特效已水平镜像（朝左面向玩家）：<节点类名>`（最多 24 行）；
`攻击特效朝向：… 打的是她自己` 那行**只在目标是自己/盟友时才出现**（群攻牌不再刷）。

### v0.2.645：镜像补丁**根本没被触发**（`AddChildSafely` 被 JIT 内联）

用户：「不对，震荡波的特效还是在玩家右侧并朝右」。查日志（10:53 那一场，v0.2.644）：

```
[10:53:28] 形态特效已水平镜像（朝左面向玩家）：NDemonFormVfx @ REGENT_BOSS
[10:53:28] 立绘已水平镜像（面向玩家）via Body
（「攻击特效已水平镜像」——一条都没有）
```

而这一场她只打了 `CHILD_OF_THE_STARS / VENERATE / ROYALTIES / DECISIONS_DECISIONS`，
**震荡波是玩家在「抉择，抉择」里挑出来、被她重放 3 次的那张**。两个问题：

**① 补丁装在一个「不存在」的地方**：`GodotTreeExtensions.AddChildSafely` 全文只有
`if (child != null) { if (...) parent.AddChild(child); else CallDeferred(...); }` ——
**这么小的方法 JIT 会直接内联到调用点**，Harmony 在调用点根本看不到它 ⇒ 补丁装上但一次都不触发。
现在改补它内部真正落地的 **`Godot.Node.AddChild(Node, bool, InternalMode)`**
（反编译确认它有正常方法体、没标 `AggressiveInlining`），
再补一道 **`VfxCmd.PlayVfx(Vector2, string, Control)`** —— 所有字符串路径的特效
（`PlayOnCreature` / `PlayOnCreatureCenter` / `PlayOnSide`，含震荡波的 `vfx/vfx_flying_slash`）
都由它落地，且它体量大、不会被内联。

**② 不该再按「打自己」跳过**：「抉择，抉择」自己是 `TargetType.Self`，
以前整手都跳过 ⇒ **它重放出来的震荡波**也跟着不翻。
现在只有用户点名的那个例外才跳过：轰击储【醒来后】的「轰击」打她自己
（`BOMBARDMENT` + `BossOrbitalBombardPower.Awake`）。

**诊断**：日志现在会打
`攻击特效窗口打开：<牌>`、`攻击特效已水平镜像（朝左面向玩家）：<类名>（生物容器 / *Vfx）`，
以及最多 10 条 `攻击特效候选（非 *Vfx，按生物容器判定）：<类名>`
（用来在下次还有特效没翻时，一眼看出它的真实类名）。

### v0.2.646：方向对了但**位置**跑到另一边（`Scale.X` 把美术偏移一起镜像了）

用户：「这次变成在储君右面朝左了。应该在储君左边朝左（震荡波）」。

v0.2.645 确认镜像真的生效了（方向已朝左），但翻的是**节点本身**的 `Scale.X`：
`VfxCmd.PlayVfx` 里是 `val.GlobalPosition = creatureNode.VfxSpawnPosition`（原点放**生物中心**），
而 `vfx_flying_slash` 的美术在场景内部偏向一侧 ⇒ 翻节点会把**那份偏移一起镜像**
⇒ 特效从「她左边朝右」跳到「她右边朝左」：方向对了、位置又错。

**修法**：画在**她自己身上**的特效改成「只翻画面、不挪位置」——
把节点的**子 `Node2D`**（也就是真正的画面层）逐个水平镜像，节点自己的位置/原点不动
（`MirrorArt`）；没有子 `Node2D` 时才退回翻节点本身。
落在**玩家身上**的特效、以及不在生物容器里的 `*Vfx` 仍按原来的翻节点方式（那些方向本来就对，
不需要动位置）。

**验收**：震荡波的特效应出现在**她左边、朝左**；日志仍是
`攻击特效已水平镜像（朝左面向玩家）：<类名>（生物容器）`。

### v0.2.647：震荡波「在右边朝左」——改成翻贴图 `FlipH`，并防重复翻

用户：「震荡波的位置还是不对，在储君右边动画朝左」。

v0.2.646 的 `MirrorArt` 有个尾巴：`return any || Mirror(node);`——
**先翻子节点，子节点翻不动（或已经翻过）时又退回去翻节点本身**。
而 `AddChild` 与 `PlayVfx` 两个入口会对**同一个节点各调一次**：

```
第 1 次（AddChild）：子节点 +1 → −1        ✅ 方向对、位置没动
第 2 次（PlayVfx）：子节点已是 −1（翻不动）→ 退回去翻【节点】+1 → −1
                     ⇒ 美术偏移被一起镜像 ⇒ 位置跳到她右边
```

**修法**：

1. 优先用贴图节点自带的 **`FlipH`**（`Sprite2D` / `AnimatedSprite2D`）——
   这是 Godot 的**原地镜像**，位置纹丝不动；广度优先找 3 层内的贴图节点，全部 toggle；
2. 没有贴图节点（纯粒子/自绘）才翻**子 `Node2D`**；
3. **不再退到翻节点本身**（那正是挪位置的元凶）；
4. 给处理过的节点打 Godot meta `charboss_vfx_mirrored`，
   两个入口只会翻一次（幂等，不再重复叠加）。

**验收**：震荡波的斩击应出现在**她左边、朝左**；日志里同一个特效节点只出现**一次**
`攻击特效已水平镜像（朝左面向玩家）：<类名>（生物容器）`。

### v0.2.648：骨骼类特效翻完要**平移**回去（震荡波位置）

用户（附截图）：「震荡波还是位置不对方向对，位置应该在储君左边」。

日志显示震荡波的斩击是 **`NVfxSpine`**（骨骼动画），每次「抉择，抉择」重放出现一条
`攻击特效已水平镜像（朝左面向玩家）：NVfxSpine`。这类节点**没有贴图子节点可以 `FlipH`**
（画面由骨骼驱动），只能翻 `Scale.X` ⇒ 美术在骨骼内部的偏移跟着镜像
⇒ 斩击从「她左边朝右」变到「她右边朝左」：方向对了、位置镜像过去。

**修法**：`NVfxSpine` / `NVfxParticleSystem` 这类「画面型」特效，翻完再**往玩家那一侧平移一倍身位**：

```csharp
shift = -(她自己的 Hitbox.Size.X)   // 玩家在她左边 ⇒ 负值；拿不到尺寸就按 130px
node.Position.X += shift;
```

用她自己的命中框宽度做尺度，所以不同 Boss（大小不同）自动缩放。
其它 `*Vfx`（飘字、卡牌拖尾、buff/debuff 图标特效、伤害数字）**不平移**，避免画面抖动。

**验收**：日志出现
`攻击特效已镜像并朝玩家侧平移 -<N>px：NVfxSpine`，画面上斩击落回**她左边、朝左**。

### v0.2.649：小刀投掷被翻反了 —— 缩小镜像范围 + 加入排除名单

用户：「boss 打小刀的动画现在是反的」。

v0.2.643～648 的做法是「她出牌期间出现的**所有** `*Vfx` 都翻」，范围太宽：

`Shiv.OnPlay`（反编译）：
```csharp
attackCommand.WithHitVfxNode((Creature t) => NShivThrowVfx.Create(Owner.Creature, t, Colors.Green));
```
`NShivThrowVfx` **生成时就同时拿到攻击者和目标**，方向是它自己按两边位置算的 ⇒ 我们翻它等于翻错。
同类「自带方向」的还有 `NBigSlashVfx`（`Create(pos, creature.IsEnemy)`）、`NLargeMagicMissileVfx`（轰击）。

**现在只翻「锚在她身上」的特效**：

| 条件 | 说明 |
|---|---|
| 父节点往上能找到**她自己的** `NCreature` | 画在她身上的（匕首雨的 flurry、buff 特效…） |
| 骨骼/粒子画面型（`NVfxSpine` / `NVfxParticleSystem`）**且**离她 1.6 身位内 | 震荡波的 `vfx_flying_slash` 是这一类（它的容器是战斗层、不在她身上） |

外加**排除名单**（`Excluded`，自己知道方向、翻了就是反的）：
`NShivThrowVfx` 小刀投掷 / `NBigSlashVfx` 巨型横斩 / `NBigSlashImpactVfx` /
`NLargeMagicMissileVfx` 轰击导弹 / `NHitSparkVfx` 命中火花 / `NDamageNumVfx` 伤害数字。

落回**玩家身上**的特效一律不碰（玩家那一侧方向本来就对 —— 这也是「君王之剑」一直看着正常的原因）。

**日志**改为「每种类型只打一次」：`攻击特效已水平镜像（朝左面向玩家）：<类名>（她身上/她身边）`，
不再有 24 条的全局上限，方便对照还有哪些没翻。

**验收**：小刀投掷恢复正常（飞向玩家）；震荡波仍是「她左边、朝左」。

### v0.2.650：「像小刀那样本来方向就对」的特效，改成**反射自动识别**

用户：「有没有类似小刀的本来方向就对的牌？」

反编译了十来个 VFX 类的静态工厂，规律很清楚 —— **看 `Create` 的签名**：

| 签名形态 | 例子 | 方向谁定 | 要不要翻 |
|---|---|---|---|
| `Create(Creature 攻击者, Creature 目标, …)` | `NShivThrowVfx`（小刀投掷） | **它自己按两边位置算** | **绝不翻** |
| `Create(Creature, …, bool goingRight/facingRight)` | `NDaggerSprayFlurryVfx` / `NDaggerSprayImpactVfx` / `NStabVfx` | **调用方写死**（玩家卡都传 `true`） | 我们的 Boss 用就反了 ⇒ 要翻 |
| `Create(Creature)`（方向由 `creature.IsEnemy` 推） | `NBigSlashVfx` / `NBigSlashImpactVfx` / `NGrandFinaleImpactVfx` / `NSweepingBeamImpactVfx` | **按被打那一侧**算 | 落在玩家身上时本来是对的 ⇒ 不翻 |
| `Create(Vector2 …)`（纯坐标） | `NLargeMagicMissileVfx`（轰击导弹）/ `NStarryImpactVfx` | 场景内部固定 | 看在谁身上 |

于是把第一类做成**运行时反射判定**，不再靠手写名单：

```csharp
// 这个 VFX 类有没有「同时接收两个 Creature」的静态 Create？
// 有 ⇒ 它生成时就知道谁打谁 ⇒ 方向自己算 ⇒ 一律跳过（`KnowsDirection`）
```

命中时日志打一行 `攻击特效跳过镜像（自带方向）：<类名>`（每种类型一次）。
原版以后新增的同型投射物**自动免疫**，不用再回来改代码。

目前**确认属于第一类**的只有 **`NShivThrowVfx`（小刀投掷）** 一个 ——
这也解释了为什么只有小刀出了问题，其它（匕首雨、震荡波、冲刺）都需要翻。

**仍待确认的一处**：`NDaggerSprayImpactVfx`（匕首雨的**命中**特效）签名是
`Create(Creature, tint, goingRight)` —— 方向被卡面写死成 `true`，而它落在**玩家**身上，
按现在的规则「落在玩家身上一律不碰」⇒ 它可能仍然是反的。等用户看一眼再决定要不要一起翻。
