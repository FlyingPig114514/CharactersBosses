# 「塔罗愚」用的 4 张牌 + 相关 buff（2026-11-14）

> 用户需求（逐字）：
> > 为本模组添加4张牌和数个新的buff
> > 来自观者（the watcher）模组的牌：斋戒、天人形态
> > 来自崩坠（我mods文件夹里的这一个）模组的牌：斗殴、永恒形态
> > 以及它们的相关buff（比如直接获得的，还有间接的，比如斗殴会给予瘀伤，瘀伤也要加）
> > 把这四张牌的稀有度设置为衍生，即正常情况下无法获得，不会影响正常游戏。
> > 你可以直接把这四张牌和相关buff的代码搬过来
> > 我这么做是为了即将做的第一个愚者boss：塔罗愚。他使用塔罗牌（愚者的神话形态的衍生牌）进行战斗，
> > 其中"审判"和"隐者"这两张塔罗牌上的人就是观者和亡命之徒，所以我要把他们的牌拿过来用一下。
> > 这四张牌只会在这个boss的意图中出现，确保玩家在不开启控制台等作弊手段的情况下无法获得这四张牌

## 源码出处（都是**反编译**取的实际实现，不是照抄卡面文字）

| 牌 | 来源 | 类型/稀有度/目标 | 关键机制 |
|---|---|---|---|
| 斋戒 `TAROT_FASTING` | 创意工坊 `3747526116 Watcher` → `WatcherMod.WatcherFasting2` | Power / Uncommon / Self，费 2 | `MagicNumber` 3（升级 4）→ 同时给 `StrengthPower`、`DexterityPower`，再挂 `EnergyDownPower`(1) |
| 天人形态 `TAROT_DEVA_FORM` | 同上 → `WatcherMod.WatcherDevaForm` | Power / Rare / Self，费 3 | `DevaPower`：每回合开始给能量，且**每回合递增**（原版实现：`Instances` 记「每次给多少」，每回合 `GainEnergy(sum)` 后各项 +1 ⇒ 1、2、3…） |
| 斗殴 `TAROT_BRAWL` | `mods\Downfall\Downfall.dll` → `HermitCode.Cards.Uncommon.Brawl` | Power / Uncommon / Self，费 2 | `BrawlPower`：**你的回合开始时**给 `CombatState.HittableEnemies` 上 N 层瘀伤（卡面 `{BrawlPower}`，原版 3） |
| 永恒形态 `TAROT_ETERNAL_FORM` | 同上 → `HermitCode.Cards.Rare.EternalForm` | Power / Rare / Self，费 3 | `EternalPower`：**每回合手抽的前 4 张**可打出的牌 `EnergyCost.AddThisTurnOrUntilPlayed(-N, true)` |

`瘀伤`（`HermitCode.Powers.BruisePower`）是**间接**给出来的那层：`斗殴 → 瘀伤`。

## 我们这边的实现（`src/Hooks/`）

- 卡牌：`Cards/TarotFoolCards.cs` —— `TarotFasting` / `TarotDevaForm` / `TarotBrawl` / `TarotEternalForm`，
  **稀有度全部是 `CardRarity.Token`（衍生）**。
- 能力：`Powers/TarotFoolPowers.cs` —— `TarotEnergyDownPower`、`TarotDevaPower`、`TarotBrawlPower`、
  `TarotBruisePower`、`TarotEternalPower`。

本地化：新增表 **`content/localization/<16 语言>/cards.json`**（`<ENTRY>.title` / `.description`，
8 个键 × 16 语言），能力文案加进 `powers.json`（`TAROT_*_POWER.title/.description`，10 个键 × 16 语言）。

## 为什么「正常玩拿不到」

1. `CardRarity.Token`：衍生牌不进任何卡池，也不会出现在战斗奖励/商店/事件里；
2. 我们**没有**把它们登记进任何 `CardPoolModel`（卡池是显式登记的）；
3. 玩家唯一的获取途径只有 `CardCmd`/控制台这类作弊手段 —— 这正是用户允许的边界。

## 和原 mod 的差异（有意为之）

- 类名带 `Tarot` 前缀 ⇒ 模型 id 是 `TAROT_FASTING` 等，**不会和「观者」「崩坠」两个 mod 的
  `FASTING` / `HERMIT-BRAWL` 撞 id，本地化 key 也不会互相覆盖**（几个 mod 同时装时很关键）。
- 升级值：`斗殴` 原版是 `WithPower<BrawlPower>(3, 2, false)`（3 → 2，看着像削弱），
  我们按正常升级做成 **3 → 4**；`永恒形态` 原版升级只加「虚无」关键字，我们做成减费 1 → 2。
  如果要以原版数字为准，改 `CanonicalVars` / `OnUpgrade` 即可。
- 关键字：没有加 `Ethereal`（虚无）—— 这几张牌只由 Boss 的剧本打出来，手牌每回合重建，
  加不加虚无对玩法没影响，先不引入这个变量。

## 尚未接线

这 4 张牌目前**只定义、未被引用**：它们的用途是下一个 Boss「塔罗愚」（愚者 → 神话形态的塔罗牌）
的意图/剧本。等那个 Boss 开始做时，在 `BossDefs` 的 `Deck`/`Script` 里 `typeof(...)` 引用即可。
