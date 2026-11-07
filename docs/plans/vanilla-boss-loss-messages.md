# 原版 12 只 Boss 的死亡提示（参考用）

> 来源：从游戏本体 `SlayTheSpire2.pck` 抽出 `res://localization/zhs/encounters.json`
> （`tools\art\extract_pck_file.gd`，13,413 字节，93 条 `loss`）。
> 原版文案统一用 **`{encounter}`** 指代「这只 Boss 的标题」，所以同一句能套到任何 Boss 上；
> 我们模组因为标题就是角色名，文案里是**写死**的 `[gold]铁甲战士[/gold]` 之类。
> 幕次归属来自 `MegaCrit.Sts2.Core.Models.Acts.{Overgrowth,Underdocks,Hive,Glory}.BossDiscoveryOrder`。

## 第一幕 · 密林（Overgrowth）

| Boss | 死亡提示 |
|---|---|
| 墨影幻灵 `VANTOM_BOSS` | {character}被[gold]{encounter}[/gold]变成了一只墨宝。 |
| 仪式兽 `CEREMONIAL_BEAST_BOSS` | {character}被[gold]{encounter}[/gold]所终结。 |
| 同族小队 `THE_KIN_BOSS` | [gold]{encounter}[/gold]的优越战术彻底击败了{character}。 |

## 第一幕 · 暗港（Underdocks）

| Boss | 死亡提示 |
|---|---|
| 瀑布巨兽 `WATERFALL_GIANT_BOSS` | [gold]{encounter}[/gold]将{character}从暗港清除了。 |
| 灵魂异鱼 `SOUL_FYSH_BOSS` | 一只[gold]{encounter}[/gold]将{character}召唤到了灾厄的末路。 |
| 乐加维林族母 `LAGAVULIN_MATRIARCH_BOSS` | {character}被[gold]{encounter}[/gold]切成了两半。 |

## 第二幕 · 巢穴（Hive）

| Boss | 死亡提示 |
|---|---|
| 无厌沙虫 `THE_INSATIABLE_BOSS` | [gold]{encounter}[/gold]吞下了{character}，可是它却依然饥肠辘辘…… |
| 知识恶魔 `KNOWLEDGE_DEMON_BOSS` | [gold]{encounter}[/gold]让{character}堕入了无尽知识的诅咒。 |
| 帝皇蟹 `KAISER_CRAB_BOSS` | [gold]{encounter}[/gold]从各个方向攻击，让{character}无从招架。 |

## 第三幕 · 荣耀（Glory）

| Boss | 死亡提示 |
|---|---|
| 女王 `QUEEN_BOSS` | [gold]{encounter}[/gold]的藏品中，又多了一位{character}。 |
| 实验体 `TEST_SUBJECT_BOSS` | 一个[gold]{encounter}[/gold]忠诚地将{character}挡在了它主人的门外。 |
| 永世沙漏 `AEONGLASS_BOSS` | [gold]{encounter}[/gold]受够了{character}的侵扰。 |

## 可以对照的写法特征

1. **句式短、一句收**；多数是「谁把谁怎么了」，主语要么是 Boss 要么是玩家。
2. **一部分带场景/世界观细节**，而不是单纯描述伤害：
   - 女王 → 「藏品里又多了一位」（她的藏品癖）
   - 实验体 → 「挡在了它主人的门外」（守门）
   - 永世沙漏 → 「受够了…侵扰」（嫌烦）
   - 无厌沙虫 → 「吞下了，可它依然饥肠辘辘」（永不满足）
   - 暗港那只 → 「从暗港清除了」（把地名写进去）
3. **少数用「赢了」的正向视角**（Boss 视角）：`{encounter}的优越战术彻底击败了…`、
   `一位英勇的 {encounter} 战胜了…`（这条是小怪 frog knight）。
4. **几乎不用「你」**：一律用 `{character}`（阵亡角色的名字）。
5. 数量词很讲究：`一只/一个/一尊/一帮/一拨` 会跟着 Boss 的形态走。

## 与本模组现状的差异（供改写参考）

- 我们 32 条「基础 + 档位 + NORMAL 变体」，实际可打到的是 20 条档位文案（见 `STATUS.md` v0.2.216 一节）。
- 其中 `NECROBINDER_SOUL_BOSS_ENCOUNTER` 与基础 `NECROBINDER_BOSS_ENCOUNTER` 文案完全重复（都是「灵魂被收走了」）。
- `DEFECT_START_BOSS_ENCOUNTER` 是**死键**（代码里没有这个遭遇类），永远不会显示。
