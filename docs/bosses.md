# 全部 Boss 一览（27 个档位）

> 登记规则：**「第几层 + 某角色的第几号」**。同一角色在同一场战斗里最多一只；主 Boss 的角色整局不重复。
> 控制台点名统一形状：`fight <KEY>_BOSS_ENCOUNTER`，**会把档位（幕数）钉死**，不管你现在在第几幕。

## 一层（7 个）

| 角色·号 | 设计 | Key | 特殊效果 | 对战指令 |
|---|---|---|---|---|
| 猎人 1 号 | 奇巧猎 | `SILENT_SLY` | 命运同担 | `fight SILENT_SLY_BOSS_ENCOUNTER` |
| 故障 1 号 | 黑球机 | `DEFECT_DARK` | 冷却剂 | `fight DEFECT_DARK_BOSS_ENCOUNTER` |
| 故障 2 号 | 电球机 | `DEFECT_THUNDER` | 引雷针 | `fight DEFECT_THUNDER_BOSS_ENCOUNTER` |
| 亡灵 1 号 | 灾厄骨 | `NECROBINDER_DOOM` | 时候未到 | `fight NECROBINDER_DOOM_BOSS_ENCOUNTER` |
| 储君 1 号 | 碎屑储君 | `REGENT_DEBRIS` | 彗星 | `fight REGENT_DEBRIS_BOSS_ENCOUNTER` |
| 储君 2 号 | 轰击储 | `REGENT_BOMBARD` | 环绕轨道 | `fight REGENT_BOMBARD_BOSS_ENCOUNTER` |
| 铁甲 1 号 | 愤怒战 | `IRONCLAD_ANGER` | 群情激奋（全局） | `fight IRONCLAD_ANGER_BOSS_ENCOUNTER` |

## 二层（9 个）

| 角色·号 | 设计 | Key | 特殊效果 | 对战指令 |
|---|---|---|---|---|
| 猎人 1 号 | 涂毒猎 | `SILENT_ENVENOM` | 带毒刺击 | `fight SILENT_ENVENOM_BOSS_ENCOUNTER` |
| 猎人 2 号 | 蛇眼猎 | `SILENT_SNECKO` | 计划妥当 | `fight SILENT_SNECKO_BOSS_ENCOUNTER` |
| 故障 1 号 | 物理机 | `DEFECT_CLAW` | 人工合成 | `fight DEFECT_CLAW_BOSS_ENCOUNTER` |
| 亡灵 1 号 | 灵魂骨 | `NECROBINDER_SOUL` | 灵魂绑定 | `fight NECROBINDER_SOUL_BOSS_ENCOUNTER` |
| 储君 1 号 | 铸剑储君 | `REGENT_FORGE` | 何人僭越 | `fight REGENT_FORGE_BOSS_ENCOUNTER` |
| 储君 2 号 | 王储 | `REGENT_KING` | 征服者 | `fight REGENT_KING_BOSS_ENCOUNTER` |
| 储君 3 号 | 仆从储 | `REGENT_MINION` | 征召上前 | `fight REGENT_MINION_BOSS_ENCOUNTER` |
| 铁甲 1 号 | 放血战 | `IRONCLAD_BLOOD` | 死亡收割 | `fight IRONCLAD_BLOOD_BOSS_ENCOUNTER` |
| 铁甲 2 号 | 杂耍战 | `IRONCLAD_JUGGLER` | 燃烧契约 | `fight IRONCLAD_JUGGLER_BOSS_ENCOUNTER` |

## 三层（11 个）

| 角色·号 | 设计 | Key | 特殊效果 | 对战指令 |
|---|---|---|---|---|
| 猎人 1 号 | 刀猎 | `SILENT_SHIV` | 袖里乾坤 | `fight SILENT_SHIV_BOSS_ENCOUNTER` |
| 猎人 2 号 | 炼药猎 | `SILENT_BREWER` | 调制 | `fight SILENT_BREWER_BOSS_ENCOUNTER` |
| 故障 1 号 | 能力机 | `DEFECT_AI` | 偏差认知 | `fight DEFECT_AI_BOSS_ENCOUNTER` |
| 故障 2 号 | 事件机 | `DEFECT_EVENT` | 一心化万 | `fight DEFECT_EVENT_BOSS_ENCOUNTER` |
| 故障 3 号 | 状态机 | `DEFECT_STATUS` | 同步 | `fight DEFECT_STATUS_BOSS_ENCOUNTER` |
| 亡灵 1 号 | 根除骨 | `NECROBINDER_ERADICATE` | 预借时间 | `fight NECROBINDER_ERADICATE_BOSS_ENCOUNTER` |
| 亡灵 2 号 | 养手骨 | `NECROBINDER_OSTY` | 不死 | `fight NECROBINDER_OSTY_BOSS_ENCOUNTER` |
| 储君 1 号 | 无色储君 | `REGENT_COLORLESS` | 共鸣 | `fight REGENT_COLORLESS_BOSS_ENCOUNTER` |
| 储君 2 号 | 辉星储 | `REGENT_STAR` | 群星荟萃 | `fight REGENT_STAR_BOSS_ENCOUNTER` |
| 铁甲 1 号 | 易伤战 | `IRONCLAD_VULN` | 主宰 | `fight IRONCLAD_VULN_BOSS_ENCOUNTER` |
| 铁甲 2 号 | 巨石战 | `IRONCLAD_BOULDER` | 原始力量 | `fight IRONCLAD_BOULDER_BOSS_ENCOUNTER` |

## 两类调试用遭遇（一般用不上）

**① 不钉档位的基础版**（强度按你**当前所在的幕**走，变体按房间遭遇/种子判）：

```
fight IRONCLAD_BOSS_ENCOUNTER      fight SILENT_BOSS_ENCOUNTER
fight DEFECT_BOSS_ENCOUNTER        fight NECROBINDER_BOSS_ENCOUNTER
fight REGENT_BOSS_ENCOUNTER
```

**② 「普通战斗房」版**（同一个 Boss，但生成在普通怪物房，不进 Boss 房，方便反复测）：

```
fight IRONCLAD_BOSS_NORMAL_ENCOUNTER        fight SILENT_BOSS_NORMAL_ENCOUNTER
fight SILENT_ENVENOM_BOSS_NORMAL_ENCOUNTER  fight SILENT_SLY_BOSS_NORMAL_ENCOUNTER
fight SILENT_SHIV_BOSS_NORMAL_ENCOUNTER     fight DEFECT_BOSS_NORMAL_ENCOUNTER
fight NECROBINDER_BOSS_NORMAL_ENCOUNTER     fight REGENT_BOSS_NORMAL_ENCOUNTER
```

## 注意

1. **`fight` 每一场只出 1 只 Boss**（走的是单怪遭遇，不套「按玩家数排阵容」那套）；想看多 Boss 联机阵容得正常爬塔。
2. 内部名字分 4 层，别混：
   - 怪物模型 id `MONSTER.*` —— **每个角色只有 1 个**，同角色所有设计共用（例如电球机和黑球机都是 `MONSTER.DEFECT_BOSS`）；
   - 阵容 Key（上表的 Key）—— 决定「用哪一套设计」，27 个；
   - 遭遇 id —— 上表的对战指令，=`<Key>_BOSS_ENCOUNTER`；
   - C# 类 —— `Monsters.DefectBoss` 之类，5 个。
3. 这三条遭遇**缺本地化文案**（`title` / `loss`），战斗顶部/败北提示会直接显示 `encounters.XXX` 原文：
   `DEFECT_THUNDER_BOSS_ENCOUNTER`、`DEFECT_STATUS_BOSS_ENCOUNTER`、`REGENT_MINION_BOSS_ENCOUNTER`。

## 特殊效果文案在哪

`content/localization/<语言>/monsters.json`，键名形如 `DEFECT_BOSS.thunder`、`REGENT_BOSS.minion`；
代码里由 `src/Hooks/Core/BossDefs.cs` 的 `SpecialLocByAct` / 变体的 `SpecialLoc` 指定，
真正的效果实现在 `src/Hooks/Powers/*.cs`。
