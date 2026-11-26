#if STS2_111   // 观者 Boss 只在 0.113 构建里（用户：107 不加这个 Boss）
using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Models;

namespace CharacterBosses.Core;

/// <summary>
/// 三层 2 号观者 Boss「**Ω观**」的剧本（用户 2026-11-16 设计）。
///
/// 用户原话（逐字）：
/// ```
/// 1.阿尔法+、点穴、防御（没费）
/// 2.确立基础、冥想（捞点穴）、笨拙、奇迹（不出）
/// 3.化体为空、点穴（0费）、贝塔、如水、奇迹（不出）
/// 4.暴怒+、打击、警惕、奇迹（不出）
/// 5.奇迹、欧米伽、不惧妖邪（没费）、贪婪
/// 6.点穴、心灵堡垒、欺瞒现实（没费）
/// 7.渐强、打击、安宁
/// 8.洗牌。点穴、暴怒+、不惧妖邪
/// 9.打击、冥想、防御（没费）
/// 10.点穴（0费）、点穴（0费）、警惕、打击（没费）
/// 11.点穴、化体为空、欺瞒现实、平安
/// 12.洗牌。点穴（0费）、暴怒、不惧妖邪
/// 之后9～12循环
/// ```
///
/// ## 能量口径（⚠ 用户没给每回合几费，这里按「他列的牌」反推）
/// 用户只在个别牌后面标了「（没费）」⇒ 意思是**其它牌都要打得出来**。所以逐回合写死
/// `BossTurn.Energy` = 那些「能打出来」的牌的费用之和（奇迹按 0 费且**打出时 +1** 计算）：
/// 3 / 2 / 4 / 5 / 2 / 2 / 3 / 4 / 2 / 2 / 4 / 3。
/// 这样被标「没费」的那几张（第 5 回合不惧妖邪、第 6 回合欺瞒现实、第 9 回合防御、第 10 回合打击）
/// 就正好差 1 点，与用户写的完全一致。要改成固定 N 费，只改本文件里对应的 `Energy = …` 一行。
///
/// ## 「洗牌。」（第 8 / 12 回合开头）
/// 观者两份 cards.json 里**没有名为「洗牌」的牌**（只有「重排」= `WATCHER_RESHUFFLE`，效果完全不同）
/// ⇒ 按「她这一回合重新洗牌」理解：她的手牌本来就每回合由剧本重建，所以这里只记一条 `Note`。
///
/// ## 点穴（0 费）
/// 用户标了 0 费的几处，用 `ScriptCard.CostOverride = 0` 写死（对应他设计里「确立基础」把保留过的
/// 点穴降到 0 费）；牌本身仍然是原版 `WATCHER_PRESSURE_POINTS`，施加印记/伤害逻辑不变。
///
/// ## 进阶
/// · **进阶 8**：升级**如水**与**心灵堡垒**（用户 2026-11-16）；
/// · **进阶 9**：只升级**第 6 回合与第 11 回合**的点穴，其它点穴不升级。
/// </summary>
public static class WatcherOmegaScript
{
    public static BossScript Build()
    {
        var turns = new List<BossTurn>
        {
            // ── 1 ── 阿尔法+ / 点穴 / 防御        （3 费：1 + 1 + 1）
            Turn(3, "",
                W(WatcherCards.Alpha, upgraded: true), W(WatcherCards.PressurePoints), W(WatcherCards.Defend)),

            // ── 2 ── 确立基础 / 冥想（捞点穴）/ 笨拙 / 奇迹（不出）   （2 费）
            Turn(2, "2. 确立基础、冥想（捞点穴）、笨拙、奇迹（不出）",
                W(WatcherCards.Establishment), W(WatcherCards.Meditate), W(WatcherCards.Clumsy),
                W(WatcherCards.Miracle, holdInstead: true)),

            // ── 3 ── 化体为空 / 点穴（0 费）/ 如水 / 贝塔 / 奇迹（不出）   （4 费）
            //   ★ v0.2.700（用户 2026-11-16）：「调换一下第三回合贝塔和如水的打出顺序（对牌的效果无实际影响）」
            Turn(4, "3. 化体为空、点穴（0费）、如水、贝塔、奇迹（不出）",
                W(WatcherCards.EmptyBody), W(WatcherCards.PressurePoints, costOverride: 0),
                W(WatcherCards.LikeWater), W(WatcherCards.Beta),
                W(WatcherCards.Miracle, holdInstead: true)),

            // ── 4 ── 暴怒+ / 打击（进阶 9 起升级）/ 警惕 / 奇迹（不出）   （5 费：2 + 1 + 2）
            Turn(5, "",
                W(WatcherCards.Eruption, upgraded: true), W(WatcherCards.Strike),
                W(WatcherCards.Vigilance), W(WatcherCards.Miracle, holdInstead: true)),

            // ── 5 ── 奇迹 / 欧米伽 / 不惧妖邪（没费）/ 贪婪   （2 费 + 奇迹的 1 = 刚好欧米伽 3）
            Turn(2, "5. 奇迹、欧米伽、不惧妖邪（没费）、贪婪（欧米伽 3 费，奇迹 +1 正好花光）",
                W(WatcherCards.Miracle), W(WatcherCards.Omega), W(WatcherCards.FearNoEvil),
                W(WatcherCards.Greed)),

            // ── 6 ── 点穴 / 心灵堡垒（进阶 8 起升级）/ 欺瞒现实（没费）   （2 费）
            //   ★ v0.2.699（用户 2026-11-16）：「削弱，进阶9不再升级点穴，而是升级所有打击」
            //   ⇒ 点穴恢复为永不升级；进阶 9 改成升级剧本里**所有**「打击」。
            Turn(2, "",
                W(WatcherCards.PressurePoints),
                W(WatcherCards.MentalFortress),
                W(WatcherCards.DeceiveReality)),

            // ── 7 ── 渐强 / 打击（进阶 9 起升级）/ 安宁   （3 费）
            Turn(3, "",
                W(WatcherCards.Crescendo), W(WatcherCards.Strike),
                W(WatcherCards.Tranquility)),

            // ── 8 ── 洗牌 / 点穴 / 暴怒+ / 不惧妖邪   （4 费：1 + 2 + 1）
            Turn(4, "8. 洗牌。",
                W(WatcherCards.PressurePoints), W(WatcherCards.Eruption, upgraded: true),
                W(WatcherCards.FearNoEvil)),

            // ── 9 ── 打击（进阶 9 起升级）/ 冥想 / 防御（没费）   （2 费）  ← 9～12 循环起点
            Turn(2, "",
                W(WatcherCards.Strike), W(WatcherCards.Meditate), W(WatcherCards.Defend)),

            // ── 10 ── 点穴（0 费）/ 点穴（0 费）/ 警惕 / 打击（没费，进阶 9 起升级）   （2 费）
            //   ★ v0.2.693（用户 2026-11-16）：「让第 10 回合的第一张点穴在进阶 9 被升级，第二张不变」
            //   ★ v0.2.699（用户 2026-11-16）：「进阶9不再升级点穴，而是升级所有打击」⇒ 两张点穴都不再升级，
            //     这一回合的那张打击改成进阶 9 起升级。
            Turn(2, "",
                W(WatcherCards.PressurePoints, costOverride: 0),
                W(WatcherCards.PressurePoints, costOverride: 0),
                W(WatcherCards.Vigilance), W(WatcherCards.Strike)),

            // ── 11 ── 贪婪（不出）/ 化体为空 / 欺瞒现实 / 平安   （3 费：1 + 1 + 1）
            //   ★ v0.2.692（用户 2026-11-16）：「第 11 回合的那张点穴换成贪婪（不出）」
            //   ⇒ 进阶 9 的「升级第 6 / 11 回合点穴」也就只剩**第 6 回合**那一张了。
            Turn(3, "11. 贪婪（不出）、化体为空、欺瞒现实、平安",
                W(WatcherCards.Greed, holdInstead: true),
                W(WatcherCards.EmptyBody), W(WatcherCards.DeceiveReality), W(WatcherCards.Safety)),

            // ── 12 ── 洗牌 / 点穴（0 费）/ 暴怒 / 不惧妖邪   （3 费：0 + 2 + 1）
            Turn(3, "12. 洗牌。",
                W(WatcherCards.PressurePoints, costOverride: 0),
                W(WatcherCards.Eruption), W(WatcherCards.FearNoEvil)),
        };

        return new BossScript
        {
            Turns = turns,
            LoopFrom = 9,       // 用户：之后 9～12 循环
            LoopLength = 4,
        };
    }

    /// <summary>卡组：她靠剧本吃饭，卡组只放备用牌（真正的每回合手牌由剧本生成）。</summary>
    public static List<Type> Deck()
    {
        var deck = new List<Type>();
        if (WatcherCards.Strike is Type s) { deck.Add(s); deck.Add(s); deck.Add(s); }
        if (WatcherCards.Defend is Type d) { deck.Add(d); deck.Add(d); deck.Add(d); }
        return deck;
    }

    /// <summary>
    /// 遗物（用户原话）：至纯之水、涅奥的祝福、永冻冰晶、空鸟笼、舵盘、钗、金刚杵。
    /// 至纯之水是**观者模组**的遗物 ⇒ 反射取；涅奥的祝福是我们自己复制的那份；其余是本体遗物。
    /// </summary>
    public static List<Type> Relics()
    {
        var relics = new List<Type>();
        if (WatcherCards.T("PureWater") is Type pure) relics.Add(pure);   // 至纯之水
        relics.Add(typeof(CharacterBosses.Relics.NeowBlessing));                 // 涅奥的祝福
        relics.Add(typeof(MegaCrit.Sts2.Core.Models.Relics.Permafrost));         // 永冻冰晶
        relics.Add(typeof(MegaCrit.Sts2.Core.Models.Relics.EmptyCage));          // 空鸟笼
        relics.Add(typeof(MegaCrit.Sts2.Core.Models.Relics.CaptainsWheel));      // 舵盘
        relics.Add(typeof(MegaCrit.Sts2.Core.Models.Relics.Sai));                // 钗
        relics.Add(typeof(MegaCrit.Sts2.Core.Models.Relics.Vajra));              // 金刚杵
        return relics;
    }

    // ── 剧本小工具 ─────────────────────────────────────────────────────────
    private static BossTurn Turn(int energy, string note, params ScriptCard?[] cards)
    {
        var list = new List<ScriptCard>();
        foreach (var c in cards) if (c != null) list.Add(c);
        return new BossTurn { Note = note, Cards = list, Energy = energy };
    }

    /// <summary>观者的牌：类型是运行期反射拿的，取不到就返回 null（这一格会被剔掉）。</summary>
    private static ScriptCard? W(Type? card, bool upgraded = false, int upgradeMinAscension = 0,
                                 bool holdInstead = false, int costOverride = -1)
    {
        if (card == null) return null;
        return new ScriptCard
        {
            Card = card,
            Upgraded = upgraded,
            UpgradeMinAscension = upgradeMinAscension,
            HoldInstead = holdInstead,
            CostOverride = costOverride,
            From = "deck",
        };
    }
}
#endif