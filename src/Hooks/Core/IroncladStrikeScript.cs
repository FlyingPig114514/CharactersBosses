using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Relics;
using CharacterBosses.Relics;

namespace CharacterBosses.Core;

/// <summary>
/// 一层铁甲战士的**第二套设计「打击战」**（Key `IRONCLAD_STRIKE`，用户 2026-11-07 设计）。
/// 同层第一套是「愤怒战」（<see cref="IroncladAngerScript"/>）。
///
/// 用户原话：
/// > 特殊效果「**痛击**」（单体）：你的名字中包含打击的牌也会增加铁甲战士的完美打击的伤害。
/// >   每当你打出名字中包含打击的牌时，铁甲战士的完美打击的伤害就少受一张名字中包含打击的牌的加成。
/// > 遗物：燃烧之血、涅奥的祝福、王室印章、烛台
/// > 进阶 8：升级岩石铠甲、获得护喉甲。进阶 9：升级完美打击、预备打击
/// > 藏品：地狱狂徒
/// > （9 回合剧本，之后 6～9 循环；「地狱狂徒」打出后，从第 6 回合起所有名字含打击的牌自动免费打出，
/// >   多人时**只打被锁定的玩家**，并且打出时带特效）
///
/// 能量账：角色基础 3 − 涅奥的祝福 1 = **2 点/回合**（墓石/王室印章的额外能量由遗物自己发，
///   剧本里标「没费」的那几张就是花完之后打不出的）。
/// </summary>
public static class IroncladStrikeScript
{
    private const int TurnEnergy = 2;

    public static BossScript Build()
    {
        var turns = new List<BossTurn>
        {
            // 1：打击、打击、完美打击（附魔皇室认证；没费不出）
            Turn("1. 打击、打击、完美打击（附魔皇室认证，没费不出）", 1,
                C<StrikeIronclad>(), C<StrikeIronclad>(), RoyalPerfect()),

            // 2：岩石铠甲、防御（没费）、痛击（没费）、完美打击（皇室认证，没费不出）
            Turn("2. 岩石铠甲、防御（没费）、痛击（没费）、完美打击（附魔皇室认证，没费不出）", 2,
                C<StoneArmor>(), C<DefendIronclad>(), C<Bash>(), RoyalPerfect()),

            // 3：完美打击（皇室认证）、防御（没费）、旋风斩（没费）
            Turn("3. 完美打击（附魔皇室认证）、防御（没费）、旋风斩（没费）", 3,
                RoyalPerfect(), C<DefendIronclad>(), NoEnergy<Whirlwind>()),   // 完美打击把 2 费吃光 ⇒ 旋风斩不出

            // 4：预备打击、灰烬打击、防御（没费）
            Turn("4. 预备打击、灰烬打击、防御（没费）", 4,
                C<SetupStrike>(), C<AshenStrike>(), C<DefendIronclad>()),

            // 5：地狱狂徒（下回合起，名字含「打击」的牌全部自动免费打出）、打击×2（没费）
            Turn("5. 地狱狂徒、打击、打击（没费）", 5,
                C<Hellraiser>(), C<StrikeIronclad>(), C<StrikeIronclad>()),

            // 6：洗牌；打击（地狱狂徒）、打击（地狱狂徒）、旋风斩（2 费）
            new BossTurn
            {
                Note = "6. 洗牌；打击（地狱狂徒）、打击（地狱狂徒）、旋风斩（2 费）",
                ShuffleDrawPile = true,
                Cards = new List<ScriptCard>
                {
                    Hell(C<StrikeIronclad>()), Hell(C<StrikeIronclad>()), C<Whirlwind>(),
                },
                Energy = TurnEnergy,
            },

            // 7：完美打击（皇室认证，因地狱狂徒自动免费打出）、防御、防御
            Turn("7. 完美打击（附魔皇室认证，因地狱狂徒自动免费打出）、防御、防御", 7,
                Hell(RoyalPerfect()), C<DefendIronclad>(), C<DefendIronclad>()),

            // 8：打击（地狱狂徒）、打击（地狱狂徒）、防御
            Turn("8. 打击（地狱狂徒）、打击（地狱狂徒）、防御", 8,
                Hell(C<StrikeIronclad>()), Hell(C<StrikeIronclad>()), C<DefendIronclad>()),

            // 9：预备打击（地狱狂徒）、灰烬打击（地狱狂徒）、痛击
            Turn("9. 预备打击（地狱狂徒）、灰烬打击（地狱狂徒）、痛击", 9,
                Hell(C<SetupStrike>()), Hell(C<AshenStrike>()), C<Bash>()),
        };

        return new BossScript
        {
            Turns = turns,
            LoopFrom = 6,      // 用户：「6～9 循环」
            LoopLength = 4,
        };
    }

    /// <summary>卡组（按剧本里出现的最大张数收集）。</summary>
    public static List<Type> Deck() => new()
    {
        typeof(StrikeIronclad), typeof(StrikeIronclad), typeof(StrikeIronclad), typeof(StrikeIronclad),
        typeof(SetupStrike), typeof(AshenStrike), typeof(PerfectedStrike),
        typeof(StoneArmor), typeof(Whirlwind), typeof(Bash), typeof(Hellraiser),
        typeof(DefendIronclad), typeof(DefendIronclad), typeof(DefendIronclad), typeof(DefendIronclad),
        typeof(DefendIronclad), typeof(DefendIronclad),
    };

    /// <summary>
    /// 遗物（用户 2026-11-07）：燃烧之血 / 涅奥的祝福 / **王室印章** / **烛台**。
    /// </summary>
    public static List<Type> Relics() => new()
    {
        typeof(BurningBlood),
        typeof(NeowBlessing),
        typeof(RoyalStamp),
        typeof(Shuriken),        // 手里剑（用户 2026-11-07：「烛台换成手里剑」）
    };

    /// <summary>「没费所以不出」的展示牌：只摆在手牌栏里让玩家看见，不参与出牌。</summary>
    private static ScriptCard NoEnergy<T>() where T : CardModel
        => new() { Card = typeof(T), From = "deck", DisplayOnly = true };
    private static ScriptCard RoyalPerfect() => new()
    {
        Card = typeof(PerfectedStrike), From = "deck", EnchantRoyallyApproved = true,
    };

    /// <summary>第 6～9 回合：这张牌由「地狱狂徒」自动免费打出。</summary>
    private static ScriptCard Hell(ScriptCard card) => new()
    {
        Card = card.Card, Upgraded = card.Upgraded, From = card.From,
        HellraiserFree = true, CostThisTurn = 0,
    };

    private static BossTurn Turn(string note, int turnIndex, params ScriptCard[] cards)
        => new()
        {
            Note = $"{turnIndex}. {note}",
            Cards = new List<ScriptCard>(cards),
            Energy = TurnEnergy,
        };

    private static ScriptCard C<T>(bool upgraded = false)
        where T : CardModel
        => new() { Card = typeof(T), Upgraded = upgraded, From = "deck" };
}
