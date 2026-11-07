# 死亡提示（loss message）总表

> 状态：**纯参考文档**，手工整理，不改任何代码。
> 权威数据源永远是 `content/localization/zhs/encounters.json` 与 `content/localization/eng/encounters.json`
> （17 组 `title` / `loss`，两份文件的 key 完全一一对应）。改了那两个文件就回来同步这一页。
>
> 取文案的位置：原版 `EncounterModel.GetLossMessageFor(CharacterModel)` 就是把**房间遭遇**的
> `Id.Entry + ".loss"` 拼成 `LocString`，所以「玩家看到的死亡提示」= **这一场房间里的那个遭遇 id**。
> 正常流程里房间遭遇由 `CharBossEncounters.ForVanillaBoss` 挑，它**优先给「一只 Boss 一个 id」的档位遭遇**
> （见 `TierByCharacterAct`），因此玩家实际看到的是**设计名（愤怒战 / 灵魂骨 / 无色储君…）的文案**，
> 而不是「铁甲战士」这种角色名。多人时若被**搭子**打死，`BossLossMessageKillerPatch` 会按凶手（`BossKillerTracker`）
> 换成那只 Boss 自己的遭遇文案 —— 单人不触发（`enc.Id == __instance.Id` 直接原样返回）。

## 文案里的两个约定

| 记号 | 含义 |
|---|---|
| `{character}` | 玩家**自己**的角色名（谁在玩就填谁：铁甲战士 / 静默猎手 / 故障机器人 / 亡灵契约师 / 储君）。`character.AddDetailsTo(msg)` 填的。 |
| `[gold]…[/gold]` | 原版 BBCode 高亮，只包 **Boss 名字**，不包其余文字。 |

语言差异：中文整句以「被…」为主、句末「。」；英文统一被动语态、句末「.」，
且 `DEFECT_AI_BOSS_ENCOUNTER.loss` 英文把主角放在句首（`Every card {character} played was…`），
中文则是「的每一张牌都被…」。

## 一、角色基础遭遇（5 个，占位 / 兜底用）

| 遭遇 id | 幕 | 标题（中 / 英） | 死亡提示（中文） | 死亡提示（英文） |
|---|---|---|---|---|
| `IRONCLAD_BOSS_ENCOUNTER` | 任意（兜底） | 铁甲战士 / The Ironclad | `{character} 被[gold]铁甲战士[/gold]击溃了。` | `{character} was broken by the [gold]Ironclad[/gold].` |
| `SILENT_BOSS_ENCOUNTER` | 任意（兜底） | 静默猎手 / The Silent | `{character} 至死都没看清[gold]静默猎手[/gold]的身影。` | `{character} never saw the [gold]Silent[/gold] coming.` |
| `DEFECT_BOSS_ENCOUNTER` | 任意（兜底） | 故障机器人 / The Defect | `{character} 被[gold]故障机器人[/gold]分析并删除了。` | `{character} was analyzed and deleted by the [gold]Defect[/gold].` |
| `NECROBINDER_BOSS_ENCOUNTER` | 任意（兜底） | 亡灵契约师 / The Necrobinder | `{character} 的灵魂被[gold]亡灵契约师[/gold]收走了。` | `{character}'s soul was claimed by the [gold]Necrobinder[/gold].` |
| `REGENT_BOSS_ENCOUNTER` | 任意（兜底） | 储君 / The Regent | `{character} 被[gold]储君[/gold]判决了。` | `{character} was sentenced by the [gold]Regent[/gold].` |

**什么时候会看到这 5 条？**
1. 某一档**没有设计**（`TierEncounterFor` 返回 null）时退回它们 —— 但按当前阵容表
   （1 层战骨储 / 2 层猎机骨储 / 3 层五选一），**正常一局里碰不到这种组合**，全是带设计的档位；
2. 控制台 `fight IRONCLAD_BOSS_ENCOUNTER` 这类「直接点名基础遭遇」的调试打法；
3. 调试开关 `charbosses_debug.txt` 换进来的 `*BossNormalEncounter`（普通战斗版，见下面的空档说明）。

## 二、按角色的设计遭遇（12 个）

### 铁甲战士（Ironclad）— 2 个设计

| 遭遇 id | 幕 | 标题（中 / 英） | 死亡提示（中文） | 死亡提示（英文） |
|---|---|---|---|---|
| `IRONCLAD_ANGER_BOSS_ENCOUNTER` | 第 1 幕 | 愤怒战 / The Ironclad (Anger) | `{character} 被[gold]铁甲战士[/gold]的怒火烧成了灰。` | `{character} was burned to ash by the [gold]Ironclad[/gold]'s rage.` |
| `IRONCLAD_VULN_BOSS_ENCOUNTER` | 第 3 幕 | 易伤战 / The Ironclad (Vulnerable) | `{character} 被[gold]铁甲战士[/gold]一层层撕开了伤口。` | `{character} was cut open, layer by layer, by the [gold]Ironclad[/gold].` |

> 铁甲战士**没有第 2 幕设计**（2 层候补里也没有他），所以 `(0, 1)` 这一格是空的。

### 静默猎手（Silent）— 2 个设计

| 遭遇 id | 幕 | 标题（中 / 英） | 死亡提示（中文） | 死亡提示（英文） |
|---|---|---|---|---|
| `SILENT_ENVENOM_BOSS_ENCOUNTER` | 第 2 幕 | 静默猎手 / The Silent (Envenom) | `{character} 被[gold]静默猎手[/gold]的毒耗尽了。` | `{character} was worn down by the [gold]Silent[/gold]'s poison.` |
| `SILENT_SHIV_BOSS_ENCOUNTER` | 第 3 幕 | 静默猎手 / The Silent (Shiv) | `{character} 被[gold]静默猎手[/gold]的刀雨淹没了。` | `{character} was buried in the [gold]Silent[/gold]'s blades.` |

> 注意这两条的**中文标题**仍是「静默猎手」（用户口径：名字用角色名），英文标题才带括注
> `(Envenom)` / `(Shiv)`。猎手**没有第 1 幕设计**，1 层候补里也没有她。

### 故障机器人（Defect）— 2 个设计

| 遭遇 id | 幕 | 标题（中 / 英） | 死亡提示（中文） | 死亡提示（英文） |
|---|---|---|---|---|
| `DEFECT_CLAW_BOSS_ENCOUNTER` | 第 2 幕 | 物理机 / The Defect (Claw) | `{character} 被[gold]故障机器人[/gold]用爪击一点点拆碎了。` | `{character} was taken apart claw by claw by the [gold]Defect[/gold].` |
| `DEFECT_AI_BOSS_ENCOUNTER` | 第 3 幕 | 能力机 / The Defect (Creative AI) | `{character} 的每一张牌都被[gold]故障机器人[/gold]算进了模型。` | `Every card {character} played was folded into the [gold]Defect[/gold]'s model.` |

> 故障机器人**没有第 1 幕设计**，1 层候补里也没有他。

### 亡灵契约师（Necrobinder）— 3 个设计（唯一三幕齐全）

| 遭遇 id | 幕 | 标题（中 / 英） | 死亡提示（中文） | 死亡提示（英文） |
|---|---|---|---|---|
| `NECROBINDER_DOOM_BOSS_ENCOUNTER` | 第 1 幕 | 灾厄骨 / The Necrobinder (Doom) | `{character} 被[gold]亡灵契约师[/gold]拖向了生命尽头。` | `{character} was dragged to the end of their life by the [gold]Necrobinder[/gold].` |
| `NECROBINDER_SOUL_BOSS_ENCOUNTER` | 第 2 幕 | 灵魂骨 / The Necrobinder (Soulbound) | `{character} 的灵魂被[gold]亡灵契约师[/gold]收走了。` | `{character}'s soul was claimed by the [gold]Necrobinder[/gold].` |
| `NECROBINDER_ERADICATE_BOSS_ENCOUNTER` | 第 3 幕 | 根除骨 / The Necrobinder (Eradicate) | `{character} 连同记忆一起被[gold]亡灵契约师[/gold]根除了。` | `{character} was eradicated, memories and all, by the [gold]Necrobinder[/gold].` |

> **灵魂骨与基础遭遇的文案完全相同**（中文「灵魂被…收走了」/ 英文 `soul was claimed…`）—— 有意为之：
> 灵魂骨就是她原本那套「收魂」的打法。改一条记得同步另一条。

### 储君（Regent）— 3 个设计

| 遭遇 id | 幕 | 标题（中 / 英） | 死亡提示（中文） | 死亡提示（英文） |
|---|---|---|---|---|
| `REGENT_DEBRIS_BOSS_ENCOUNTER` | 第 1 幕 | 碎屑储君 / The Regent (Debris) | `{character} 被[gold]储君[/gold]的碎屑掩埋了。` | `{character} was buried under the [gold]Regent[/gold]'s debris.` |
| `REGENT_FORGE_BOSS_ENCOUNTER` | 第 2 幕 | 铸剑储君 / The Regent (Forge) | `{character} 成了[gold]储君[/gold]剑上的一道痕。` | `{character} became a notch on the [gold]Regent[/gold]'s blade.` |
| `REGENT_COLORLESS_BOSS_ENCOUNTER` | 第 3 幕 | 无色储君 / The Regent (Colorless) | `{character} 被[gold]储君[/gold]褪去了颜色。` | `{character} was stripped of all color by the [gold]Regent[/gold].` |

## 三、幕 →（角色，遭遇）对照（= 正常流程实际会看到的文案）

与 `CharBossEncounters.TierByCharacterAct` 及 `BossLineups` 的候补表一致：

| 幕 | 候补角色（等概率，主 Boss 逐幕不重复） | 对应的死亡提示 id |
|---|---|---|
| 第 1 幕 | 铁甲 / 亡灵 / 储君 | `IRONCLAD_ANGER_` · `NECROBINDER_DOOM_` · `REGENT_DEBRIS_` |
| 第 2 幕 | 静默 / 故障 / 亡灵 / 储君 | `SILENT_ENVENOM_` · `DEFECT_CLAW_` · `NECROBINDER_SOUL_` · `REGENT_FORGE_` |
| 第 3 幕 | 静默 / 故障 / 亡灵 / 储君 / 铁甲 | `SILENT_SHIV_` · `DEFECT_AI_` · `NECROBINDER_ERADICATE_` · `REGENT_COLORLESS_` · `IRONCLAD_VULN_` |

（上表都省略了公共后缀 `_BOSS_ENCOUNTER`。）

## 四、一句话写法约定（以后加新 Boss 照这个来）

1. **中文**：`{character} 被[gold]<角色名>[/gold]<一句动作>。` —— 只高亮角色名，动作要具体
   （烧成灰 / 撕开伤口 / 刀雨 / 爪击拆碎 / 拖向生命尽头 / 根除 / 褪去颜色），别用「击败了」这种泛词。
2. **英文**：`{character} was <过去分词短语> by the [gold]<角色名>[/gold].` 或其变体
   （`soul was claimed` / `became a notch on…` / `never saw … coming`），唯一例外是能力机把主角提到句首。
3. **标题**：中文**只写角色名**（用户口径，设计名在意图/剧本里体现）；英文写 `The X (Design)`。
4. 新增设计时，三处必须同时加：`content/localization/{zhs,eng}/encounters.json` 的 `title` + `loss`、
   `TierBossEncounters.cs` 的遭遇类（`TierAct` 钉档）、`CharBossEncounters.TierByCharacterAct` 的登记。

## 五、已知空档（不是 bug，暂不处理）

- 调试用的 `*BossNormalEncounter`（`IRONCLAD_BOSS_NORMAL_ENCOUNTER` 等，`RoomType.Monster`）
  **没有自己的 `.loss` key**（两份 json 里都没有），而原版是按 `Id.Entry + ".loss"` 取的
  ⇒ 用调试开关换进来、又恰好死在那场里时，提示会缺 key，显示的不是上面任何一句。
  只有开了 `charbosses_debug.txt` 才会走到这条路径，正常游玩碰不到。
  真要修就是给 5 个 `*_BOSS_NORMAL_ENCOUNTER`（外加猎手那两个）各补一条 `loss`，或让它们复用父类的 key。
- 同一档位「设计名 ↔ 角色名」的显示位置不止这里：地图 Boss 图标、顶栏、死亡提示都取**房间遭遇**，
  所以三者永远一致；`powers.json` / `monsters.json` 里的名字是**怪物侧**的，与死亡提示无关。

## 六、特殊效果文案的「主语」规则（用户 2026-10-25）

**结论先说**：用户 2026-10-25 **只采纳了「偏差认知」一处**（改成 Boss 本名作主语），
其余 7 条（袖里乾坤 / 带毒刺击 / 时候未到 / 灵魂绑定 / 共鸣 / 彗星 / 预借时间）**按用户要求原样回退**，
保持原来的写法不动。下面这张表只是「以后真要改写时的参考」，不是现状。

规则：特殊效果描述里，Boss 用**本名**（铁甲战士 / 静默猎手 / 故障机器人 / 亡灵契约师 / 储君）当主语或动作发出者，
不用设计名（愤怒战 / 涂毒猎 / 能力机 …）。

参考改法（**除偏差认知外均未采用**）：

| 现有写法 | 若要改写可参考 |
|---|---|
| 「你每打出一张 …能力牌，获得 1 点集中。力量与集中数值始终相同。」 | 「你每打出 1 张 …能力牌，**故障机器人**就获得 1 点集中；**它的**力量与集中数值始终相等。」（✅ 已采用） |
| 「中毒改为在你的回合结束时结算…」 | 「**静默猎手**改变了中毒：中毒改为在你的回合结束时结算…」（❌ 已回退） |
| 「每当你的格挡将要因回合开始而消失时，它们将抵消等量灾厄。」 | 「**亡灵契约师的灾厄**会被你的格挡抵消：每当你的格挡将要因回合开始而消失时…」（❌ 已回退） |
| 「每当玩家在战斗内生成卡牌时，也会触发储君的「创世之柱」。」 | 「每当玩家在战斗内生成卡牌时，**储君**也会触发一次**她的**「创世之柱」。」（❌ 已回退） |
| 「…并使亡灵契约师获得 1 能量。」 | 「…**亡灵契约师**就让你本回合所有牌的耗能增加 1 能量，而**她自己**获得 1 能量。」（❌ 已回退） |

- 文案存在**两份**，改一处必须同步另一处：`content/localization/{zhs,eng}/monsters.json` 的
  `{角色}_BOSS.special` / `.special_act2` / `.soul_bound` / `.borrowed_time` / `.comet` / `.resonance` /
  `.anger` / `.dominate` / `.synthesis`（游戏里「特殊效果」说明框显示的就是它，格式 `名称\n描述`），
  以及 `powers.json` 里对应的 `*_POWER.description`（状态栏 power 的悬浮说明）。
- 中文「他/她」：储君=他；静默猎手 / 亡灵契约师 / 故障机器人=她。
- 本来就以 Boss 为主语的 4 条（一直没动）：`群情激奋`、`主宰`、`人工合成`、`何人僭越`。


