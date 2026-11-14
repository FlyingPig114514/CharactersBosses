# 角色第三人称称谓规范（Pronouns）

> 用户 2026-11-13 定：游戏内文案里每个角色的第三人称**按角色性别**写，不许一律用「她」。
> 英文同理（`he/him/his`、`she/her`、`it/its`）。
> 这一页是**唯一权威记录**；以后新增任何文案（本地化、能力面板、提示框）都照这张表写。

## 1. 对照表

| 角色（Key 前缀） | 性别 | 中文 | 英文 |
|---|---|---|---|
| **铁甲战士** `IRONCLAD_` | 男 | **他** | he / him / his |
| **储君** `REGENT_` | 男 | **他** | he / him / his |
| **静默猎手** `SILENT_` | 女 | **她** | she / her / her |
| **亡灵契约师** `NECROBINDER_` | 女 | **她** | she / her / her |
| **故障机器人** `DEFECT_` | 无性别 | **它** | it / it / its |
| **愚者** `FOOL_` | 性别是树（开发者设定） | **杝**（优先）或 **它** | it / it / its |

> 愚者的「杝」是用户指定的写法，**优先用它**；如果哪天字体渲染不出来再退成「它」。

## 2. 怎么判断一条文案属于哪个角色

1. **`monsters` / `encounters` / `ascension` 表**：键前缀就是角色 ——
   `IRONCLAD_BOSS.*`、`SILENT_BOSS.*`、`DEFECT_BOSS.*`、`NECROBINDER_BOSS.*`、`REGENT_BOSS.*`、`FOOL_*`；
   档位遭遇的键也带角色前缀（`SILENT_BREWER_BOSS.*`、`REGENT_MINION_BOSS.*` 等）。
2. **`powers` 表**：键是 `<POWER_ID>.title/.description`，本身不带角色 ⇒ 按 **power 属于谁**判断。
   权威来源是 `src/Hooks/Core/BossDefs.cs` 的接线（`SpecialPower` / `SpecialPowerByAct` /
   `SpecialPowerDreadByAct` / 各变体的 `SpecialPower`）。当前这张表：

   | 角色 | power 键 |
   |---|---|
   | 铁甲战士 | `IRONCLAD_RAGE_POWER` `BOSS_RENDING_POWER` `DOMINATE_WEAK_POWER` `BOSS_BASH_POWER` `BOSS_BURNING_PACT_POWER` `BOSS_PRIMAL_FORCE_POWER` |
   | 静默猎手 | `SHARED_FATE_POWER` `POISON_STRIKE_POWER` `SLEEVE_TRICK_POWER` `BOSS_SNAKE_FORM_POWER` `BOSS_WELL_LAID_PLANS_POWER` `BREW_MOD_POWER` `BOSS_SUPPRESSION_POWER` |
   | 故障机器人 | `BOSS_COOLANT_POWER` `SYNTHESIS_POWER` `SYNTHESIS_DREAD_POWER` `DEVIATION_POWER` `BOSS_LIGHTNING_ROD_POWER` `BOSS_RAINBOW_POWER` `BOSS_ALL_FOR_ONE_POWER` `BOSS_SYNC_POWER` |
   | 亡灵契约师 | `NOT_YET_POWER` `BOSS_SOUL_BOUND_POWER` `BORROWED_TIME_BOSS_POWER` `BOSS_DEVOUR_LIFE_POWER` `BOSS_VOID_CALL_POWER` `BOSS_OSTY_UNDYING_POWER` |
   | 储君 | `COMET_BOSS_POWER` `KNOW_THY_PLACE_POWER` `RESONANCE_POWER` `BOSS_ORBITAL_BOMBARD_POWER` `BOSS_CONQUEROR_POWER` `BOSS_STELLAR_GATHERING_POWER` `BOSS_CALL_TO_ARMS_POWER` `BOSS_GUIDE_POWER` |
   | 恐惧模式「支配」 | `DEFECT_DOMINION_POWER` / `NECROBINDER_DOMINION_POWER` / `FOOL_DOMINION_POWER`（文案里的「你」指玩家，一般不需要第三人称） |

3. **`intents` 表**：`SACRIFICE` / `TEMPTATION` / `JUDGEMENT` 这类是通用意图说明，
   主语是「这名敌人」，**不要**写具体角色的人称。

## 3. 这一轮（v0.2.630）实际改了哪些

只有 **铁甲战士** 和 **故障机器人** 的文案原本误用了「她」（静默猎手/亡灵契约师本来就是「她」，储君英文本来就是 he）：

- 中文（`zhs` + `zht`）8 条：
  `IRONCLAD_BOSS.anger_dread`、`IRONCLAD_RAGE_POWER.description_dread` → **他**；
  `DEFECT_BOSS.special_dread`、`DEFECT_BOSS.dark_dread`、`DEFECT_BOSS.thunder_dread`、
  `BOSS_COOLANT_POWER.description_dread`、`DEVIATION_POWER.description_dread`、
  `BOSS_LIGHTNING_ROD_POWER.description_dread` → **它**。
- 英文（`eng` 8 条 + 其余 13 种语言的**英文正文**副本，共 112 处）：
  同样这 8 个键，`she/her` → `he/his/him`（铁甲）或 `it/its`（故障）。

## 4. 还没做的（已知遗留）

- **其余 13 种语言的「普通模式」译文**里仍是各语言的原生代词（德语 `sie`、俄语 `она`…）。
  那些是人工译文，改起来要为每种语言单独判断性数格，**暂不动**；等哪天重译时按本表来。
- **`mod_log.txt` 里的「她」**没有改：那是开发日志（`Logger.Log`），同一段代码服务所有 Boss、
  没有「当前角色」这个概念，改了反而更难读。若以后要做，得给日志加一个「当前 Boss → 代词」的查表。
- **能力面板** `BossInfoTable.cs` 的字段是「循环 / 单体能力 / 高压回合…」这类无主语短语，不需要人称。
