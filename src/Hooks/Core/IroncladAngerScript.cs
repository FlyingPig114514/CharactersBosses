using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Relics;

namespace CharacterBosses.Core;

/// <summary>
/// 一层 Boss「**愤怒战**」（铁甲战士）—— 用户设计（2026-10-23 口述，逐条照抄）。
///
/// 特殊效果「**群情激奋**（全局）」：
///   ① 每当铁甲战士打出「愤怒」时，也向所有玩家的弃牌堆加入一张愤怒的复制品；
///   ② 每当有玩家打出「愤怒」时，铁甲战士**本回合失去 1 点力量**（黑暗镣铐的 debuff）。
/// 实现见 <c>Powers/IroncladRagePower</c>。
///
/// 遗物：燃烧之血、涅奥的祝福（自带）、锚、卡戎之灰。
/// 进阶 8：升级「坚毅」+ 获得「奥利哈钢」；进阶 9：升级「烙印」。
///
/// ⚠ 剧本里的「被烧」= 被**前一张**「烙印 / 坚毅」的「消耗 1 张牌」消耗掉，
/// 用 <see cref="ScriptCard.Burned"/> 标记（自动选择器会优先挑它去消耗）。
///
/// 用户原文编号 1、2、3 之后直接跳到 5（缺 4）：这里按**内容顺序**排成 9 条，
/// 最后两条循环（原文「之后 9～10 循环」）。
/// </summary>
public static class IroncladAngerScript
{
    public static BossScript Build()
    {
        var turns = new List<BossTurn>
        {
            Turn("愤怒、打击、打击", 1,
                C<Anger>(), C<StrikeIronclad>(), C<StrikeIronclad>()),

            Turn("突破（群攻）、坚毅、打击（被坚毅烧掉）", 2,
                // 用户 2026-10-30：第 2/5/7/9 回合里**没被烧掉的那张打击 → 突破**（`BREAKTHROUGH`，
                //   1 费打全体、自己掉血）⇒ 增强群攻能力
                C<Breakthrough>(), C<TrueGrit>(), Burn<StrikeIronclad>()),

            Turn("好勇斗狠、烙印、打击（被烙印烧掉）", 3,
                C<Aggression>(), C<Brand>(), Burn<StrikeIronclad>()),

            Turn("洗牌。烙印、愤怒+（洗牌前好勇斗狠捞的）、打击、打击（被烧）", 4,
                C<Brand>(), C<Anger>(true), C<StrikeIronclad>(), Burn<StrikeIronclad>()),

            Turn("愤怒+（好勇斗狠捞）、坚毅、突破（群攻）、愤怒（被烧）", 5,
                C<Anger>(true), C<TrueGrit>(), C<Breakthrough>(), Burn<Anger>()),

            Turn("洗牌。愤怒+（捞）、坚毅、愤怒+、打击（被烧）", 6,
                C<Anger>(true), C<TrueGrit>(), C<Anger>(true), Burn<StrikeIronclad>()),

            Turn("烙印、愤怒+（捞）、突破（群攻）、愤怒+（被烧）", 7,
                C<Brand>(), C<Anger>(true), C<Breakthrough>(), Burn<Anger>(true)),

            Turn("洗牌。愤怒+（捞）、愤怒+、坚毅、愤怒+（被烧）", 8,
                C<Anger>(true), C<Anger>(true), C<TrueGrit>(), Burn<Anger>(true)),

            Turn("烙印、愤怒+（捞）、突破（群攻）、愤怒+（被烧）", 9,
                C<Brand>(), C<Anger>(true), C<Breakthrough>(), Burn<Anger>(true)),
        };

        return new BossScript
        {
            Turns = turns,
            LoopFrom = 8,      // 用户 2026-10-23 订正：「之后 8～9 循环」
            LoopLength = 2,
        };
    }

    /// <summary>第一幕的卡组（剧本实际造牌，这里用于自检与展示）。</summary>
    /// <remarks>用户 2026-10-23：「削一下，把所有痛击换成打击」⇒ 痛击 `Bash` 全部换成打击 `StrikeIronclad`。</remarks>
    public static List<Type> Deck() => new()
    {
        typeof(Aggression),          // 好勇斗狠
        typeof(StrikeIronclad), typeof(StrikeIronclad),
        typeof(StrikeIronclad), typeof(StrikeIronclad),
        typeof(StrikeIronclad),      // 原来是痛击，已按用户要求换成打击（共 5 张打击）
        typeof(Anger),               // 愤怒（打出时自身还会再造一张复制品进弃牌堆）
        typeof(Brand),               // 烙印
        typeof(TrueGrit),            // 坚毅
    };

    /// <summary>第一幕的遗物（涅奥的祝福由 <c>CarriesNeowBlessing</c> 自动带）。</summary>
    public static List<Type> Relics() => new()
    {
        typeof(BurningBlood),        // 燃烧之血
        typeof(Relics.NeowBlessing),   // 涅奥的祝福：所有 Boss 都是第 2 件（用户 2026-10-27 口径）
        typeof(Anchor),              // 锚
        typeof(CharonsAshes),        // 卡戎之灰
    };

    /// <summary>进阶 8 额外给的遗物「护喉甲」（2026-10-24 用户削弱：原为奥利哈钢）。</summary>
    public static Type AscensionRelic() => typeof(Gorget);

    private static BossTurn Turn(string note, int turnIndex, params ScriptCard[] cards)
        => new() { Note = $"{turnIndex}. {note}", Cards = new List<ScriptCard>(cards) };

    private static ScriptCard C<T>(bool upgraded = false)
        where T : MegaCrit.Sts2.Core.Models.CardModel
        => new() { Card = typeof(T), Upgraded = upgraded, From = "deck" };

    /// <summary>这张牌**不打出**，而是被上一张「烙印 / 坚毅」的「消耗 1 张牌」烧掉。</summary>
    private static ScriptCard Burn<T>(bool upgraded = false)
        where T : MegaCrit.Sts2.Core.Models.CardModel
        => new() { Card = typeof(T), Upgraded = upgraded, From = "deck", Burned = true };
}
