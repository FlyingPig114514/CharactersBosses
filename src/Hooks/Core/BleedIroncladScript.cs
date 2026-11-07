using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Relics;
using CharacterBosses.Relics;

namespace CharacterBosses.Core;

/// <summary>
/// 二层「**放血战**」（铁甲战士 · 自伤 / 格挡轴）—— 用户 2026-10-27 设计稿。
/// 这是二层铁甲战士的**正式设计**，取代原来那套「卡组 + 贪心 AI」的占位（`BossDefs.IroncladAct2` 已删）。
///
/// 特殊效果「**扯碎**」（`Powers/BossRendingPower.cs`）：
/// 每当他**不在自己的回合**失去生命时，若**单次**失去的生命 **≤ 5**，
/// 也照样触发他自己的「撕裂」（`Rupture`：失去生命 → +力量）。
/// ⚠ 原版这张牌的条件写死成「在**你的回合**内失去生命」⇒ 玩家回合里那点小打小闹本来不触发，
///   这条特殊效果就是补上这部分（每次 ≤5 才补，避免玩家一大刀反而给他叠一堆）。
///   （2026-10-28 用户削弱：原来还同时重放「狱火」，已去掉。）
///
/// ## 能量（写死在剧本里）
/// 基础 3 − 涅奥的祝福 1 = **2**；**第 1 回合**灯笼 +1 ⇒ **3**；
/// **第 3 回合起**「佩尔之肉」每回合 +1 ⇒ **3**。所以：第 1 回合 3、第 2 回合 2、第 3 回合起 3。
///
/// ## 剧本 7 回合，之后 **5～7 循环**（用户 2026-10-28 第二次削弱改稿）
/// 撕裂、血墙、全身撞击+ / 时候未到、烙印、打击（被烧） / 打击、打击、防御 /
/// 御血术、突破、防御 / 打击、打击、防御 / 血墙、全身撞击+、烙印 / 御血术、突破、防御。
/// 全身撞击+（单人）在多人时换成恶魔护盾+，见 <see cref="MultiplayerCardSwap"/>。
///
/// ⚠ 2026-10-28 第二次削弱：**绯红披风 / 坚毅 / 岩石铠甲 全下线**
///   —— 绯红披风原来让他每回合白拿 7 格挡（全身撞击跟着变大），拔掉才是真正的削弱。
///
/// ⚠ 设计稿里的「**血盾**」在游戏里没有这张牌，这里按「**血墙**」（`BloodWall`：2 费、失去 2 生命、
///   获得 16 格挡）实现 —— 它正好配「全身撞击（造成等同于当前格挡的伤害）」，而且进阶 8 也是升级它。
///   若指的是别的牌，改 <see cref="Deck"/> 与剧本里那两处即可。
/// </summary>
public static class BleedIroncladScript
{
    /// <summary>第 3 回合起每回合 3 费（佩尔之肉）。</summary>
    private const int TurnEnergy = 3;

    public static BossScript Build()
    {
        var turns = new List<BossTurn>
        {
            // 1（灯笼 +1 ⇒ 3 费：撕裂 1 + 血墙 2 + 全身撞击+ 0）
            //   多人：全身撞击+ 会被 MultiplayerCardSwap **换成**恶魔护盾+（0 费，给已行动的友方 Boss 起甲）
            //   ⚠ 换牌是「替换」不是「追加」——单人绝不能在剧本里另写一张恶魔护盾，
            //     否则单人意图里会多出一条打不出去的恶魔护盾（用户 2026-10-28 实测报的就是这个）。
            new()
            {
                Note = "1. 撕裂、血墙、全身撞击+（多人：换成恶魔护盾+，给已行动、格挡最少的友方）",
                Energy = 3,
                Cards = Cards(
                    C<Rupture>(), C<BloodWall>(),
                    C<BodySlam>(true)),         // 全身撞击+ 是 0 费 ⇒ 能量刚好；多人换成恶魔护盾+
            },
            // 2（2 费：时候未到 2 + 烙印 0；打击被烙印消耗，不出）
            new()
            {
                Note = "2. 时候未到、烙印；打击被烙印消耗（不出）",
                Energy = 2,
                Cards = Cards(
                    C<NotYet>(), C<Brand>(), Burn<StrikeIronclad>()),
            },
            // 3（3 费：打击 1 + 打击 1 + 防御 1）
            new()
            {
                Note = "3. 打击、打击、防御",
                Energy = TurnEnergy,
                Cards = Cards(C<StrikeIronclad>(), C<StrikeIronclad>(), C<DefendIronclad>()),
            },
            // 4（3 费：铁斩波 1 + 突破 1 + 防御 1）
            new()
            {
                Note = "4. 铁斩波、突破、防御",
                Energy = TurnEnergy,
                Cards = Cards(C<IronWave>(), C<Breakthrough>(), C<DefendIronclad>()),
            },
            // 5（3 费：打击 1 + 打击 1 + 防御 1）—— 循环起点
            new()
            {
                Note = "5. 打击、打击、防御",
                Energy = TurnEnergy,
                Cards = Cards(C<StrikeIronclad>(), C<StrikeIronclad>(), C<DefendIronclad>()),
            },
            // 6（3 费：血墙 2 + 全身撞击+ 0 + 烙印 0）
            new()
            {
                Note = "6. 血墙、全身撞击+（多人：换成恶魔护盾+）、烙印",
                Energy = TurnEnergy,
                Cards = Cards(
                    C<BloodWall>(), C<BodySlam>(true), C<Brand>()),
            },
            // 7（3 费：铁斩波 1 + 突破 1 + 防御 1）
            new()
            {
                Note = "7. 铁斩波、突破、防御",
                Energy = TurnEnergy,
                Cards = Cards(C<IronWave>(), C<Breakthrough>(), C<DefendIronclad>()),
            },
        };
        return new BossScript
        {
            Turns = turns,
            LoopFrom = 5,      // 用户 2026-10-28：之后 5～7 循环
            LoopLength = 3,
        };
    }

    /// <summary>卡组（剧本里出现过的牌按最大张数收集）。</summary>
    public static List<Type> Deck() => new()
    {
        // ⚠ 2026-10-28 第二次削弱（用户）：**绯红披风 / 坚毅 / 岩石铠甲 全部下线**
        //   —— 绯红披风就是他原来每回合白拿 7 格挡的来源，拿掉之后「全身撞击」也跟着变小。
        typeof(BloodWall), typeof(BloodWall),        // 血墙（设计稿写作「血盾」）
        typeof(BodySlam),            // 全身撞击（剧本里是升级版，0 费）
        typeof(DemonicShield),       // 恶魔护盾（多人换上的那张）
        typeof(Rupture),             // 撕裂
        typeof(Brand), typeof(Brand),                // 烙印
        typeof(IronWave), typeof(IronWave),          // 铁斩波（用户 2026-10-28 削弱：替换御血术）
        typeof(Breakthrough), typeof(Breakthrough),  // 突破
        typeof(NotYet),              // 时候未到
        typeof(StrikeIronclad), typeof(StrikeIronclad),
        typeof(StrikeIronclad), typeof(StrikeIronclad),   // 打击 ×4
        typeof(DefendIronclad), typeof(DefendIronclad), typeof(DefendIronclad),
    };

    /// <summary>
    /// 遗物（用户清单）：燃烧之血（起始）/ 涅奥的祝福（第 2 件，所有 Boss 都一样）/
    /// 自成型黏土 / 锚 / 佩尔之肉 / 灯笼。
    /// </summary>
    public static List<Type> Relics() => new()
    {
        typeof(BurningBlood),        // 燃烧之血（起始）
        typeof(NeowBlessing),        // 涅奥的祝福
        typeof(SelfFormingClay),     // 自成型黏土：战斗中失去生命 → 下回合获得格挡
        typeof(Anchor),              // 锚：战斗开始给格挡
        typeof(PaelsFlesh),          // 佩尔之肉：第 3 回合起每回合 +1 能量
        typeof(Lantern),             // 灯笼：第 1 回合 +1 能量
    };

    // ── 小工具 ──────────────────────────────────────────────

    private static List<ScriptCard> Cards(params ScriptCard[] cards) => new(cards);

    private static ScriptCard C<T>(bool upgraded = false) where T : CardModel
        => new() { Card = typeof(T), Upgraded = upgraded, From = "deck" };

    /// <summary>这张牌不主动打，而是被上一张（烙印 / 坚毅）**消耗**掉。</summary>
    private static ScriptCard Burn<T>() where T : CardModel
        => new() { Card = typeof(T), From = "deck", Burned = true };
}
