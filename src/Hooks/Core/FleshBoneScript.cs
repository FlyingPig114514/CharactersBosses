using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Relics;
using CharacterBosses.Relics;

namespace CharacterBosses.Core;

/// <summary>
/// 第一幕亡灵契约师的**第二套设计「血肉骨」**（设计 Key <c>NECROBINDER_FLESH</c>，用户 2026-11-10 设计）。
/// 同层第一套是「灾厄骨」（<see cref="NecrobinderScript"/>，Key <c>NECROBINDER_DOOM</c>）。
///
/// 用户原话（逐条照抄，改设计时对着看）：
/// > 1层2号亡灵契约师boss血肉骨
/// > 特殊效果：吞噬生命：每当亡灵契约师给予负面效果时，她获得4点格挡。
/// > 要在会给予负面效果的牌的意图中加上血肉戏法的9点伤害。
/// > 多人时，苦难会将被锁定玩家身上的buff传给其他玩家，此时要在其他玩家的意图中提前显示
/// >   会收到的血肉戏法的伤害（取决于被她锁定的玩家身上的负面效果种类多少）
/// > 藏品:命运同担
///
/// ★ 2026-11-10 第四轮改稿（用户「把招架盾换成锚 + 新意图」）：
/// > 遗物：缚魂命匣、涅奥的祝福、骇人头盔、**锚**
/// > 意图：
/// >  1.血肉戏法、鬼火、防御
/// >  2.吸引仇恨、摧残(没费)、打击(没费)
/// >  3.打击、恐惧+、打击（没费）
/// >  4.违逆、防御、苦难
/// >  5.护卫、出击、防御（没费）
/// >  6.命运同担、防御、防御、
/// >  7.吸引仇恨、打击(没费)、打击(没费)
/// >  8.摧残、防御、防御(没费)
/// >  9.防御、恐惧+、打击(没费)
/// >  10.违逆、防御、苦难
/// >  11.护卫、出击、防御(没费)
/// >  之后7～11循环
///
/// ## 能量账
/// 第一幕亡灵契约师 = 基础 3 − 涅奥的祝福 1 = **每回合 2**：
///   · 第 1 回合 血肉戏法(2) + 鬼火(0，**回 1 能量**) + 防御(1) —— 靠鬼火那 1 点正好够；
///   · 第 2/7 回合 吸引仇恨(2)（多人 = 骸骨军团）⇒ 后面两张没费；
///   · 第 3/9 回合 打击(1) + 恐惧+(1) + 一张没费；
///   · 第 4/10 回合 违逆(1) + 防御(1) + 苦难(0)；
///   · 第 5/11 回合 护卫(1) + 出击(1) + 防御（没费）；
///   · 第 6 回合 命运同担(0) + 防御(1) + 防御(1)；
///   · 第 8 回合 摧残(1) + 防御(1) + 防御（没费）。
///
/// ## 循环
/// 用户：「之后 7～11 循环」⇒ <c>LoopFrom = 7, LoopLength = 5</c>。
/// </summary>
public static class FleshBoneScript
{
    /// <summary>每回合能量（3 基础 − 涅奥的祝福 1）。</summary>
    private const int TurnEnergy = 2;

    public static BossScript Build()
    {
        var turns = new List<BossTurn>
        {
            // 1：血肉戏法(2) + 鬼火(0，回 1 能量) + 防御(1)
            new BossTurn
            {
                Note = "1. 血肉戏法(2)、鬼火(0，回 1 能量)、防御(1)",
                Energy = TurnEnergy,
                Cards = Cards(Play<SleightOfFlesh>(2), Play<Wisp>(0), Play<DefendNecrobinder>(1)),
            },

            // 2：吸引仇恨(2)（多人 = 骸骨军团）；摧残、打击没费
            new BossTurn
            {
                Note = "2. 吸引仇恨(2)（多人=骸骨军团）；摧残、打击（没费）",
                Energy = TurnEnergy,
                Cards = Cards(Play<PullAggro>(2), Hold<Debilitate>(1), Hold<StrikeNecrobinder>(1)),
            },

            // 3：打击(1) + 恐惧+(1)；第二张打击没费
            new BossTurn
            {
                Note = "3. 打击(1)、恐惧+(1)；打击（没费）",
                Energy = TurnEnergy,
                Cards = Cards(Play<StrikeNecrobinder>(1), Play<Fear>(1, upgraded: true), Hold<StrikeNecrobinder>(1)),
            },

            // 4：违逆(1) + 防御(1) + 苦难(0)
            new BossTurn
            {
                Note = "4. 违逆(1)、防御(1)、苦难(0)",
                Energy = TurnEnergy,
                Cards = Cards(Play<Defy>(1), Play<DefendNecrobinder>(1), Play<Misery>(0)),
            },

            // 5：护卫(1) + 出击(1)；防御没费
            new BossTurn
            {
                Note = "5. 护卫(1)、出击(1)；防御（没费）",
                Energy = TurnEnergy,
                Cards = Cards(Play<Bodyguard>(1), Play<Unleash>(1), Hold<DefendNecrobinder>(1)),
            },

            // 6：命运同担(0) + 防御(1) + 防御(1)
            new BossTurn
            {
                Note = "6. 命运同担(0)、防御(1)、防御(1)",
                Energy = TurnEnergy,
                Cards = Cards(Play<SharedFate>(0), Play<DefendNecrobinder>(1), Play<DefendNecrobinder>(1)),
            },

            // 7：吸引仇恨(2)（多人 = 骸骨军团）；打击×2 没费
            new BossTurn
            {
                Note = "7. 吸引仇恨(2)（多人=骸骨军团）；打击×2（没费）",
                Energy = TurnEnergy,
                Cards = Cards(Play<PullAggro>(2),
                               Hold<StrikeNecrobinder>(1), Hold<StrikeNecrobinder>(1)),
            },

            // 8：摧残(1) + 防御(1)；第二张防御没费
            new BossTurn
            {
                Note = "8. 摧残(1)、防御(1)；防御（没费）",
                Energy = TurnEnergy,
                Cards = Cards(Play<Debilitate>(1), Play<DefendNecrobinder>(1), Hold<DefendNecrobinder>(1)),
            },

            // 9：防御(1) + 恐惧+(1)；打击没费
            new BossTurn
            {
                Note = "9. 防御(1)、恐惧+(1)；打击（没费）",
                Energy = TurnEnergy,
                Cards = Cards(Play<DefendNecrobinder>(1), Play<Fear>(1, upgraded: true), Hold<StrikeNecrobinder>(1)),
            },

            // 10：违逆(1) + 防御(1) + 苦难(0)
            new BossTurn
            {
                Note = "10. 违逆(1)、防御(1)、苦难(0)",
                Energy = TurnEnergy,
                Cards = Cards(Play<Defy>(1), Play<DefendNecrobinder>(1), Play<Misery>(0)),
            },

            // 11：护卫(1) + 出击(1)；防御没费
            new BossTurn
            {
                Note = "11. 护卫(1)、出击(1)；防御（没费）",
                Energy = TurnEnergy,
                Cards = Cards(Play<Bodyguard>(1), Play<Unleash>(1), Hold<DefendNecrobinder>(1)),
            },
        };

        return new BossScript
        {
            Turns = turns,
            LoopFrom = 7,      // 用户：「之后 7～11 循环」
            LoopLength = 5,
        };
    }

    /// <summary>卡组（按剧本里**单回合最大张数**收集；只用于展示、自检与抽牌堆张数）。</summary>
    public static List<Type> Deck() => new()
    {
        typeof(SleightOfFlesh),
        typeof(Wisp),
        // 第 6 回合要打两张、第 8 回合要「打一张 + 留一张」⇒ 两张
        typeof(DefendNecrobinder), typeof(DefendNecrobinder),
        typeof(PullAggro),
        // 第 3 回合「打一张 + 留一张」、第 7 回合留两张 ⇒ 两张
        typeof(StrikeNecrobinder), typeof(StrikeNecrobinder),
        typeof(Fear),          // 第 3、9 回合（都是恐惧+）
        typeof(Defy),
        typeof(Misery),
        typeof(Bodyguard),
        typeof(Unleash),
        typeof(SharedFate),
        typeof(Debilitate),
    };

    /// <summary>遗物（用户清单，顺序照抄）：缚魂命匣 / 涅奥的祝福 / 骇人头盔 / 锚。</summary>
    public static List<Type> Relics() => new()
    {
        typeof(BoundPhylactery),      // 缚魂命匣：战斗开始召唤奥斯提（出击的伤害按它的生命算）
        typeof(NeowBlessing),         // 涅奥的祝福（所有 Boss 都是第 2 件）
        typeof(IntimidatingHelmet),   // 骇人头盔：打出耗能 ≥2 的牌获得 4 点格挡
        typeof(Anchor),               // 锚：战斗开始获得 10 点格挡（用户 2026-11-10 把「招架盾」换掉）
    };

    // ── 剧本小工具 ─────────────────────────────────────────────

    private static List<ScriptCard> Cards(params ScriptCard[] cards) => new(cards);

    /// <summary>按卡面费用打出（`CostThisTurn` 显式写死，剧本里的数字就是账）。</summary>
    private static ScriptCard Play<T>(int cost, bool upgraded = false)
        where T : MegaCrit.Sts2.Core.Models.CardModel
        => new() { Card = typeof(T), Upgraded = upgraded, From = "deck", CostThisTurn = cost };

    /// <summary>本回合**不出**、留在手里的那张（用户写「没费」的那些）。</summary>
    private static ScriptCard Hold<T>(int cost, bool upgraded = false)
        where T : MegaCrit.Sts2.Core.Models.CardModel
        => new() { Card = typeof(T), Upgraded = upgraded, From = "deck", CostThisTurn = cost, HoldInstead = true };
}
