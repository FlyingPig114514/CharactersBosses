using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Relics;
using CharacterBosses.Core;

namespace CharacterBosses.Core;

/// <summary>
/// 故障机器人（三层：能力机）的剧本。
///
/// 设计者（用户）原话要点，写在这里免得后面改乱：
///   · 血上限 75 + 480 = 555（75 是定义值，+480 来自涅奥的祝福：拾起 120 + 每幕 180×2）；
///   · 特殊效果「偏差认知」：玩家每打出一张**能力牌**，Boss 获得 1 点集中；Boss 的力量与集中**同步变化**；
///   · 4 回合之后出现的能力牌都是「创造性AI」当场印的（为保证意图固定，印出来的牌也是固定的）；
///   · 第 5 回合那张冰川是「全息影像」从弃牌堆捞的；
///   · 卡组里原本的消耗牌/能力牌打出后不再出现；9～12 回合循环。
///
/// 能量为什么自洽：基础 3 费（4 − 涅奥祝福 1）；「子程序」每打出一张能力牌 +1 费；
/// 「充电」下回合 +1 费。所以「这张牌没费打不了」在剧本里不需要特殊标记 ——
/// <see cref="BossPlanner.PlanFromScript"/> 会在能量不够时自动跳过它。
/// </summary>
public static class DefectScript
{
    /// <summary>第三幕用的剧本（11 回合，之后 7～11 循环）。</summary>
    public static BossScript Build()
    {
        var turns = new List<BossTurn>
        {
            Turn("开局：偏差认知+ 拿集中（永冻冰晶给 7 格挡）、冰川铺冰球；弹幕齐射没费",
                C<BiasedCognition>(true), C<Glacier>(), C<Barrage>()),

            Turn("子程序（能力牌 → 之后每张能力牌 +1 费）、冰雹风暴；双重释放激发电球两次（意图显示 14×2）",
                C<Subroutine>(), C<Hailstorm>(), C<Dualcast>()),

            Turn("雷暴、碎片整理+、冷却剂；球位已满 → 引导时激发队首（若队首是冰球，激发给格挡＝守势）",
                C<Storm>(), C<Defragment>(true), C<Coolant>()),

            Turn("创造性AI 落地；寒流铺球；电击没费",
                C<CreativeAi>(), C<ColdSnap>(), C<Zap>()),

            Turn("碎片整理（AI 印）、全息影像、冰川（全息影像从弃牌堆捞的那张）；打击没费",
                C<Defragment>(false, "ai"), C<Hologram>(), C<Glacier>(false, "hologram"), C<StrikeDefect>()),

            Turn("缓冲（AI 印）、充电、高速脱离（塞晕眩）、防御",
                C<MegaCrit.Sts2.Core.Models.Cards.Buffer>(false, "ai"), C<ChargeBattery>(), C<BoostAway>(), C<DefendDefect>()),

            Turn("冰雹风暴（AI 印）、究极防御、飞跃、防御",
                C<Hailstorm>(false, "ai"), C<UltimateDefend>(), C<Leap>(), C<DefendDefect>()),

            Turn("洗牌。扩容（AI 印）、究极防御、充电、防御",
                C<Capacitor>(false, "ai"), C<UltimateDefend>(), C<ChargeBattery>(), C<DefendDefect>()),

            Turn("暴涨（AI 印）、冰川、寒流；打击没费",
                C<BulkUp>(false, "ai"), C<Glacier>(), C<ColdSnap>(), C<StrikeDefect>()),

            Turn("冰雹风暴（AI 印）、弹幕齐射、高速脱离、晕眩（上回合自己塞的状态牌）",
                C<Hailstorm>(false, "ai"), C<Barrage>(), C<BoostAway>(), C<Dazed>(false, "status")),

            Turn("暴涨（AI 印）、双重释放（队首是冰球 → 意图是守势；是电球则是 伤害×2）、电击；防御没费",
                C<BulkUp>(false, "ai"), C<Dualcast>(), C<Zap>(), C<DefendDefect>()),
        };

        return new BossScript
        {
            Turns = turns,
            LoopFrom = 8,     // 8～11 回合循环（用户订正）
            LoopLength = 4,
        };
    }

    /// <summary>第三幕的卡组（剧本实际造牌，这里用于展示与自检）。</summary>
    public static List<Type> Deck() => new()
    {
        typeof(Dualcast), typeof(Zap), typeof(StrikeDefect),
        typeof(DefendDefect), typeof(DefendDefect), typeof(UltimateDefend),
        typeof(ChargeBattery), typeof(BoostAway), typeof(Leap),
        typeof(BiasedCognition), typeof(Glacier), typeof(Barrage),
        typeof(Subroutine), typeof(Hailstorm), typeof(Storm),
        typeof(Capacitor), typeof(BulkUp), typeof(Hologram),
        typeof(CreativeAi), typeof(ColdSnap), typeof(MegaCrit.Sts2.Core.Models.Cards.Buffer),
        typeof(Defragment), typeof(Coolant),    // 碎片整理 / 冷却剂（用户新加入第 3 回合剧本）
    };

    /// <summary>遗物（用户给的顺序，从右往左显示时最右是起始遗物破损核心）。</summary>
    public static List<Type> Relics() => new()
    {
        typeof(CrackedCore),      // 起始遗物：战斗开始引导 1 个闪电球
        typeof(Relics.NeowBlessing),   // 涅奥的祝福：所有 Boss 都是第 2 件（用户 2026-10-27 口径）
        typeof(RedMask),          // 战斗开始给玩家上虚弱
        typeof(Permafrost),       // 本场第一次打出能力牌时给 7 格挡
        typeof(DustyTome),        // 说明卡组里「偏差认知」的来源
        typeof(SturdyClamp),      // 格挡跨回合保留
        typeof(WhisperingEarring),// 低语耳环：每回合 +1 能量（正好抵消涅奥祝福的 -1，净 3 费）
        typeof(TungstenRod),      // 钨合金棍
    };

    private static BossTurn Turn(string note, params ScriptCard[] cards)
        => new() { Note = note, Cards = new List<ScriptCard>(cards) };

    private static ScriptCard C<T>(bool upgraded = false, string from = "deck") where T : MegaCrit.Sts2.Core.Models.CardModel
        => new() { Card = typeof(T), Upgraded = upgraded, From = from };
}
