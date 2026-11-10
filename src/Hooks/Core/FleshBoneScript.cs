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
/// > 遗物：缚魂命匣、涅奥的祝福、骇人头盔、招架盾
/// > 特殊效果：吞噬生命：每当亡灵契约师给予负面效果时，她获得4点格挡。
/// > 要在会给予负面效果的牌的意图中加上血肉戏法的9点伤害。
/// > 如果回合结束时她的格挡会不小于10点，在显示电球、炸弹等的位置显示招架盾的6点伤害，
/// >   记得考虑她特殊效果和骇人头盔为她带来的格挡
/// > 多人时，苦难会将被锁定玩家身上的buff传给其他玩家，此时要在其他玩家的意图中提前显示
/// >   会收到的血肉戏法的伤害（取决于被她锁定的玩家身上的负面效果种类多少）
/// > 特殊效果中添加警告：警告：血肉戏法和招架盾属于无来源伤害，带有"为你而死"的召唤物不会替你承受该伤害。
/// > 藏品:命运同担
///
/// ★ 2026-11-10 第二轮改稿（用户「更改部分意图和进阶效果」）：
/// > 进阶9：升级恐惧和摧残
/// > 意图：
/// >  1.血肉戏法、鬼火、护卫
/// >  2.出击、防御、防御（没费）
/// >  3.违逆、恐惧、苦难
/// >  4.撕碎、防御、防御（没费）
/// >  5.吸引仇恨+、摧残(没费)、打击(没费)
/// >  6.命运同担、打击、打击
/// >  7.吸引仇恨+、打击(没费)、打击(没费)
/// >  8.出击、摧残、防御(没费)
/// >  9.恐惧、防御、苦难
/// >  10.撕碎、护卫、防御（没费）
/// >  11.防御、违逆、打击(没费)
/// >  之后7～11循环
///
/// ## 能量账
/// 第一幕亡灵契约师 = 基础 3 − 涅奥的祝福 1 = **每回合 2**：
///   · 第 1 回合 血肉戏法(2) + 鬼火(0，**回 1 能量**) + 护卫(1) —— 靠鬼火那 1 点正好够；
///   · 第 3/9 回合 违逆/恐惧(1) + 恐惧/防御(1) + 苦难(0) = 2；
///   · 第 5/7 回合 吸引仇恨+(2)（多人 = 骸骨军团）⇒ 后面两张没费；
///   · 第 6 回合 命运同担(0) + 打击 + 打击 = 2；
///   · 其余回合都是两张 1 费 + 一张没费。
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
            // 1：血肉戏法(2) + 鬼火(0，回 1 能量) + 护卫(1)
            new BossTurn
            {
                Note = "1. 血肉戏法(2)、鬼火(0，回 1 能量)、护卫(1)",
                Energy = TurnEnergy,
                Cards = Cards(Play<SleightOfFlesh>(2), Play<Wisp>(0), Play<Bodyguard>(1)),
            },

            // 2：出击(1) + 防御(1)；第二张防御没费
            new BossTurn
            {
                Note = "2. 出击(1)、防御(1)；防御（没费）",
                Energy = TurnEnergy,
                Cards = Cards(Play<Unleash>(1), Play<DefendNecrobinder>(1), Hold<DefendNecrobinder>(1)),
            },

            // 3：违逆(1) + 恐惧(1) + 苦难(0)
            new BossTurn
            {
                Note = "3. 违逆(1)、恐惧(1)、苦难(0)",
                Energy = TurnEnergy,
                Cards = Cards(Play<Defy>(1), Play<Fear>(1), Play<Misery>(0)),
            },

            // 4：撕碎(1) + 防御(1)；第二张防御没费
            new BossTurn
            {
                Note = "4. 撕碎(1)、防御(1)；防御（没费）",
                Energy = TurnEnergy,
                Cards = Cards(Play<Rend>(1), Play<DefendNecrobinder>(1), Hold<DefendNecrobinder>(1)),
            },

            // 5：吸引仇恨+(2)（多人 = 骸骨军团）；摧残、打击没费
            new BossTurn
            {
                Note = "5. 吸引仇恨+(2)（多人=骸骨军团）；摧残、打击（没费）",
                Energy = TurnEnergy,
                Cards = Cards(Play<PullAggro>(2, upgraded: true), Hold<Debilitate>(1), Hold<StrikeNecrobinder>(1)),
            },

            // 6：命运同担(0) + 打击(1) + 打击(1)
            new BossTurn
            {
                Note = "6. 命运同担(0)、打击(1)、打击(1)",
                Energy = TurnEnergy,
                Cards = Cards(Play<SharedFate>(0), Play<StrikeNecrobinder>(1), Play<StrikeNecrobinder>(1)),
            },

            // 7：吸引仇恨+(2)（多人 = 骸骨军团+）；打击×2 没费
            new BossTurn
            {
                Note = "7. 吸引仇恨+(2)（多人=骸骨军团+）；打击×2（没费）",
                Energy = TurnEnergy,
                Cards = Cards(Play<PullAggro>(2, upgraded: true),
                               Hold<StrikeNecrobinder>(1), Hold<StrikeNecrobinder>(1)),
            },

            // 8：出击(1) + 摧残(1)；防御没费
            new BossTurn
            {
                Note = "8. 出击(1)、摧残(1)；防御（没费）",
                Energy = TurnEnergy,
                Cards = Cards(Play<Unleash>(1), Play<Debilitate>(1), Hold<DefendNecrobinder>(1)),
            },

            // 9：恐惧(1) + 防御(1) + 苦难(0)
            new BossTurn
            {
                Note = "9. 恐惧(1)、防御(1)、苦难(0)",
                Energy = TurnEnergy,
                Cards = Cards(Play<Fear>(1), Play<DefendNecrobinder>(1), Play<Misery>(0)),
            },

            // 10：撕碎(1) + 护卫(1)；防御没费
            new BossTurn
            {
                Note = "10. 撕碎(1)、护卫(1)；防御（没费）",
                Energy = TurnEnergy,
                Cards = Cards(Play<Rend>(1), Play<Bodyguard>(1), Hold<DefendNecrobinder>(1)),
            },

            // 11：防御(1) + 违逆(1)；打击没费
            new BossTurn
            {
                Note = "11. 防御(1)、违逆(1)；打击（没费）",
                Energy = TurnEnergy,
                Cards = Cards(Play<DefendNecrobinder>(1), Play<Defy>(1), Hold<StrikeNecrobinder>(1)),
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
        typeof(Bodyguard), typeof(Bodyguard),            // 第 1、10 回合各一张
        typeof(DefendNecrobinder), typeof(DefendNecrobinder),   // 第 2、4 回合各要两张
        typeof(Unleash), typeof(Unleash),                // 第 2、8 回合
        typeof(Defy), typeof(Defy),                      // 第 3、11 回合
        typeof(Fear), typeof(Fear),                      // 第 3、9 回合
        typeof(Misery), typeof(Misery),                  // 第 3、9 回合
        typeof(Rend), typeof(Rend),                      // 第 4、10 回合
        typeof(PullAggro),                               // 第 5、7 回合（同一张，跨回合复用）
        typeof(Debilitate),
        typeof(StrikeNecrobinder), typeof(StrikeNecrobinder),   // 第 6 回合要两张
        typeof(SharedFate),
    };

    /// <summary>遗物（用户清单，顺序照抄）：缚魂命匣 / 涅奥的祝福 / 骇人头盔 / 招架盾。</summary>
    public static List<Type> Relics() => new()
    {
        typeof(BoundPhylactery),      // 缚魂命匣
        typeof(NeowBlessing),         // 涅奥的祝福（所有 Boss 都是第 2 件）
        typeof(IntimidatingHelmet),   // 骇人头盔：打出耗能 ≥2 的牌获得格挡
        typeof(ParryingShield),       // 招架盾：回合结束时格挡 ≥10 ⇒ 对她锁定的玩家造成 6 点无来源伤害
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
