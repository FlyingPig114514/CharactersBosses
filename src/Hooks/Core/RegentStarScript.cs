using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Relics;
using CharacterBosses.Relics;

namespace CharacterBosses.Core;

/// <summary>
/// 三层「**辉星储**」（储君 · 辉星 / 保留手牌轴）—— 用户 2026-10-28 设计稿。
/// 这是三层储君的**第二套设计**（第一套是「无色储君」`RegentScript.BuildAct3`），
/// 与「刀猎/炼药猎」「易伤战/巨石战」同一套机制：**以房间遭遇为准**（`REGENT_STAR_BOSS_ENCOUNTER`）。
///
/// 特殊效果「**群星荟萃**」（`Powers/BossStellarGatheringPower.cs`）：
/// 每当储君**获得「保留手牌」**（`RetainHandPower`）时，你（真人玩家）也获得「保留手牌」
/// 与**下回合能量**。
///
/// 剧本 8 回合，之后 **5～8 循环**（用户原文见 docs/plans/boss-regent-star.md）：
/// <code>
/// 1 黑洞、辉光+、汇流+、星界脉冲
/// 2 太阳打击+、防御、花样百出+、（印万向斩、究极防御）万向斩、王者之拳、究极防御（没费）
/// 3 群星斗篷、剑雨、防御
/// 4 隐秘藏品+、凡庸、七星（星不够出不了）
/// 5 辉光+、汇流+、究极防御、星界脉冲
/// 6 万向斩、王者之拳、群星斗篷、防御
/// 7 剑雨、隐秘藏品+、太阳打击+
/// 8 七星、防御、凡庸
/// </code>
///
/// ## 能量
/// 基础 3 − 涅奥的祝福 1 + **贤者之石 1** = **3**，逐回合都是 3（写死在剧本里）。
/// ## 辉星
/// 起始遗物「天赋君权」（`DivineRight`）在战斗开始给星；「七星」要 **7 星**、
/// 「黑洞」花/给星时对全体造成伤害 ⇒ 前三回合给的星决定第 4 回合能不能打七星。
/// </summary>
public static class RegentStarScript
{
    /// <summary>逐回合能量（基础 3 − 涅奥 1 + 贤者之石 1）。</summary>
    private const int TurnEnergy = 3;

    public static BossScript Build()
    {
        var turns = new List<BossTurn>
        {
            // 1（3 费：黑洞 1 + 辉光+ 1 + 汇流+ 1 + 星界脉冲 0）
            new()
            {
                Note = "1. 黑洞、辉光+、汇流+、星界脉冲",
                Energy = 3,
                Cards = Cards(
                    // ★ 2026-11-13（用户）：「升级黑洞」
                C<BlackHole>(true), C<Glow>(true), C<Convergence>(true), C<AstralPulse>()),
            },
            // 2（3 费：太阳打击+ 1 + 防御 1 + 花样百出+ 0 + 万向斩 0 + 王者之拳 1；
            //    花样百出会印出「万向斩」「究极防御」，究极防御这回合没费、打不出）
            new()
            {
                Note = "2. 太阳打击+、防御、花样百出+（印万向斩/究极防御）、万向斩、王者之拳；究极防御没费不出",
                Energy = TurnEnergy,
                Cards = Cards(
                    C<SolarStrike>(true), C<DefendRegent>(), C<JackOfAllTrades>(true),
                    C<Omnislice>(), C<KinglyPunch>(), C<UltimateDefend>()),
            },
            // 3（3 费：群星斗篷 0 + 剑雨 1 + 防御 1）
            new()
            {
                Note = "3. 群星斗篷、剑雨、防御",
                Energy = TurnEnergy,
                Cards = Cards(C<CloakOfStars>(), C<Salvo>(), C<DefendRegent>()),
            },
            // 4（3 费：隐秘藏品+ 1 + 凡庸 0（诅咒，打不出）+ 七星 2）
            //    七星要 7 星 ⇒ 星不够时这张自然打不出（规划期就有星星记账）
            new()
            {
                Note = "4. 隐秘藏品+、凡庸、七星（星不够出不了）",
                Energy = TurnEnergy,
                Cards = Cards(C<HiddenCache>(true), C<Normality>(), C<SevenStars>()),
            },
            // 5（3 费：辉光+ 1 + 汇流+ 1 + 究极防御 1 + 星界脉冲 0）—— 循环起点
            new()
            {
                Note = "5. 辉光+、汇流+、究极防御、星界脉冲",
                Energy = TurnEnergy,
                Cards = Cards(
                    C<Glow>(true), C<Convergence>(true), C<UltimateDefend>(), C<AstralPulse>()),
            },
            // 6（3 费：万向斩 0 + 王者之拳 1 + 群星斗篷 0 + 防御 1）
            new()
            {
                Note = "6. 万向斩、王者之拳、群星斗篷、防御",
                Energy = TurnEnergy,
                Cards = Cards(C<Omnislice>(), C<KinglyPunch>(), C<CloakOfStars>(), C<DefendRegent>()),
            },
            // 7（3 费：剑雨 1 + 隐秘藏品+ 1 + 太阳打击+ 1）
            new()
            {
                Note = "7. 剑雨、隐秘藏品+、太阳打击+",
                Energy = TurnEnergy,
                Cards = Cards(C<Salvo>(), C<HiddenCache>(true), C<SolarStrike>(true)),
            },
            // 8（3 费：七星 2 + 防御 1 + 凡庸 0）
            new()
            {
                Note = "8. 七星、防御、凡庸",
                Energy = TurnEnergy,
                Cards = Cards(C<SevenStars>(), C<DefendRegent>(), C<Normality>()),
            },
        };

        return new BossScript
        {
            Turns = turns,
            LoopFrom = 4,      // 用户：之后 5～8 循环（0 起 ⇒ 第 5 回合）
            LoopLength = 4,
            // ★ 2026-11-13（恐惧模式，用户当轮口径）：
            //   「辉星储本来第四回合就是星不够，一定不能打出七星的。**交换防御和隐秘藏品+的位置**之后，
            //     由于隐秘藏品+的下回合辉星结算辉星就够了」
            //   ⇒ 恐惧模式下第 3 / 4 回合换成：3 = 群星斗篷、剑雨、**隐秘藏品+**；
            //     4 = **防御**、凡庸、七星 ⇒ 隐秘藏品+ 的「下回合 +星」在第 4 回合到账，
            //     七星（7 星）这回合真的打得出去。
            //
            //   ⚠ 为什么用 `Resolve` 而不是在 `Build()` 里直接 `DreadMode.IsActive` 分支：
            //     `BossDefs` 是**静态初始化**，那时 `DreadMode.IsActive` 恒为 false
            //     （同一个坑在「蛇眼猎·谋杀」那里踩过）。`Resolve` 是**每次取回合表时**才求值。
            Resolve = DreadTurnOverride,
        };
    }

    /// <summary>恐惧模式的回合覆盖（只改第 3 / 4 回合；其余返回 null 交回静态回合表）。</summary>
    private static BossTurn? DreadTurnOverride(int turn)
    {
        if (!Core.DreadMode.IsActive) return null;
        if (turn == 3)
        {
            return new BossTurn
            {
                Note = "3.（恐惧）群星斗篷、剑雨、隐秘藏品+（提前一回合收藏，好让下回合的星到账）",
                Energy = TurnEnergy,
                Cards = Cards(C<CloakOfStars>(), C<Salvo>(), C<HiddenCache>(true)),
            };
        }
        if (turn == 4)
        {
            return new BossTurn
            {
                Note = "4.（恐惧）防御、凡庸、七星（隐秘藏品+ 的星已到账 ⇒ 7 星够了）",
                Energy = TurnEnergy,
                Cards = Cards(C<DefendRegent>(), C<Normality>(), C<SevenStars>()),
            };
        }
        return null;
    }

    /// <summary>卡组（剧本里出现过的牌按最大张数收集；剧本手牌本来也是从这里造的）。</summary>
    public static List<Type> Deck() => new()
    {
        typeof(BlackHole),                            // 黑洞
        typeof(Glow), typeof(Glow),                   // 辉光
        typeof(Convergence), typeof(Convergence),     // 汇流
        typeof(AstralPulse), typeof(AstralPulse),      // 星界脉冲（v0.2.334 削弱：原为「伽马爆破」GammaBlast）
        typeof(SolarStrike), typeof(SolarStrike),     // 太阳打击
        typeof(JackOfAllTrades),                      // 花样百出
        typeof(Omnislice), typeof(Omnislice),         // 万向斩
        typeof(KinglyPunch), typeof(KinglyPunch),     // 王者之拳
        typeof(UltimateDefend), typeof(UltimateDefend), // 究极防御
        typeof(CloakOfStars), typeof(CloakOfStars),   // 群星斗篷
        typeof(Salvo), typeof(Salvo),                 // 剑雨（游戏里叫「箭雨」）
        typeof(HiddenCache), typeof(HiddenCache),     // 隐秘藏品
        typeof(SevenStars), typeof(SevenStars),       // 七星
        typeof(Normality), typeof(Normality),         // 凡庸（诅咒）
        typeof(DefendRegent), typeof(DefendRegent), typeof(DefendRegent), typeof(DefendRegent),
    };

    /// <summary>
    /// 遗物（用户清单）：天赋君权（起始）/ 涅奥的祝福（第 2 件，所有 Boss 都一样）/
    /// 闪亮口红 / 美味饼干 / 舵盘 / 贤者之石 / 迷你储君。
    /// </summary>
    public static List<Type> Relics() => new()
    {
        typeof(DivineRight),         // 天赋君权（起始）：战斗开始获得辉星
        typeof(NeowBlessing),        // 涅奥的祝福（第 2 件）
        typeof(SparklingRouge),      // 闪亮口红：第 3 回合开始 +力量 +敏捷
        typeof(YummyCookie),         // 美味饼干
        typeof(CaptainsWheel),       // 舵盘：第 3 回合开始给格挡
        typeof(PhilosophersStone),   // 贤者之石：每回合 +1 能量（玩家侧 +力量）
        typeof(MiniRegent),          // 迷你储君：每回合第一次花星 → +力量
    };

    // ── 小工具 ──────────────────────────────────────────────

    private static List<ScriptCard> Cards(params ScriptCard[] cards) => new(cards);

    private static ScriptCard C<T>(bool upgraded = false) where T : CardModel
        => new() { Card = typeof(T), Upgraded = upgraded, From = "deck" };
}
