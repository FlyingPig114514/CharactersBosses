using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Relics;
using CharacterBosses.Relics;

namespace CharacterBosses.Core;

/// <summary>
/// 三层储君 3 号「**抉择储**」的剧本（用户 2026-11-10 设计，原话见
/// <c>docs/plans/boss-regent-choice.md</c> §1）。
///
/// 特殊效果「**指导**」：每当储君打出「抉择，抉择」时，**由玩家**（多人时是被他锁定的那位）
/// 替他选择要被重复打出 3 次的那张牌 —— 用的是知识恶魔那套「选择一张牌」界面
/// （<c>CardSelectCmd.FromChooseACardScreen</c>），选完他才继续打后面的牌。
/// 所以「抉择，抉择+」这一手在剧本里只是**普通一张牌**，它的后果由玩家的选择决定。
///
/// 能量：用户 2026-11-10 逐回合指定（1、2、3、5、7、8、10、11 回合 3 费，12 回合 4 费，其余 2 费），
/// 所以每个回合都**写死** <see cref="BossTurn.Energy"/>。剧本里的「没费」正是靠这个数实现的：
/// 例：第 1 回合 群星之子(1) + 崇拜(1) + 王国资产(1) = 3 ⇒ 抉择(0) 之后**一点能量都不剩**，
/// 「天际钻头」只有在玩家选了「星位序列」（星位序列的 3 次各给 2 费 ⇒ +6）时才打得出去。
///
/// 遗物按用户 2026-11-10 的订正：锚换成了**灯笼**（第 1 回合 +1 能量），胜券在王换成了**王国资产**。
/// </summary>
public static class ChoiceRegentScript
{
    /// <summary>13 回合；之后 9～13 循环。</summary>
    public static BossScript Build()
    {
        var turns = new List<BossTurn>
        {
            // ── 1 ────────────────────────────────────────────────────────────────
            // 群星之子(1) + 崇拜(1) + 王国资产(1) = 3 费打完 ⇒ 抉择(0) 之后剩 0 费。
            // 抉择抉择+ 会把手里那 5 张技能牌摆出来让玩家挑一张打 3 次；
            // 「天际钻头」是 X 费牌 ⇒ 只有玩家选了「星位序列」（3 次 × +2 费 = +6）才打得出。
            T(3, "1. 群星之子、崇拜、王国资产、抉择抉择+（王室认证）：让玩家在 5 张技能牌里挑一张打 3 次 ⇒ " +
                 "天际钻头（只有选了星位序列才有费）、君王之剑（没费）",
                new ScriptCard { Card = typeof(ChildOfTheStars), UpgradeMinAscension = 8 },
                C<Venerate>(true),
                C<Royalties>(),
                Choice(),
                Display<HiddenCache>(BossIntentTag.Buff),
                Display<Bulwark>(BossIntentTag.Defend | BossIntentTag.Buff),
                Display<Alignment>(drillGain: true),
                Display<Resonance>(BossIntentTag.Debuff | BossIntentTag.Buff),
                Display<Shockwave>(BossIntentTag.Debuff, souls: true),
                new ScriptCard { Card = typeof(HeavenlyDrill), OnlyIfChosenCard = typeof(Alignment), SpendsAllEnergy = true },
                C<SovereignBlade>()),

            // ── 2 ────────────────────────────────────────────────────────────────
            T(3, "2. 大爆炸（+1 费）、辉光、收集光辉、辐射（本回合每获得 1 星打一次）、淬炼刀刃；抉择抉择+ 不出",
                C<BigBang>(), C<Glow>(),
                new ScriptCard { Card = typeof(GatherLight), UpgradeMinAscension = 8 },
                C<Radiate>(), C<RefineBlade>(),
                Choice(hold: true)),

            // ── 3 ────────────────────────────────────────────────────────────────
            T(3, "3. 招架、君王之剑、星尘、防御（没费）；抉择抉择+ 不出",
                C<Parry>(), C<SovereignBlade>(), C<Stardust>(), C<DefendRegent>(),
                Choice(hold: true)),

            // ── 4 ────────────────────────────────────────────────────────────────
            T(2, "4. 隐秘藏品、汇流+、防御（没费）；抉择抉择+ 不出",
                C<HiddenCache>(), C<Convergence>(true), C<DefendRegent>(),
                Choice(hold: true)),

            // ── 5 ────────────────────────────────────────────────────────────────
            // 0 费起手：天际钻头把这一回合所有能量（选了星位序列则是 3+6）全吃掉 ⇒ 后面的君王之剑/防御都没费。
            new BossTurn
            {
                Note = "5. 洗牌。抉择抉择+（王室认证）：让玩家挑一张打 3 次 ⇒ 天际钻头（一定打出）、" +
                       "君王之剑（没费）、防御（没费）",
                Energy = 3,
                ShuffleDrawPile = true,
                Cards = new List<ScriptCard>
                {
                    Choice(),
                    Display<HiddenCache>(BossIntentTag.Buff),
                    Display<Bulwark>(BossIntentTag.Defend | BossIntentTag.Buff),
                    Display<Alignment>(drillGain: true),
                    Display<Resonance>(BossIntentTag.Debuff | BossIntentTag.Buff),
                    Display<Shockwave>(BossIntentTag.Debuff, souls: true),
                    // 天际钻头：把这一回合剩下的能量全部花光 ⇒ 后面的君王之剑 / 防御一定没费
                    new ScriptCard { Card = typeof(HeavenlyDrill), SpendsAllEnergy = true },
                    C<SovereignBlade>(), C<DefendRegent>(),
                },
            },

            // ── 6 ────────────────────────────────────────────────────────────────
            // 用户 2026-11-10 订正：「第六回合确实应该是 2 费，改成 辉光、辐射、淬炼刀刃、收集光辉（没费）」。
            T(2, "6. 辉光、辐射、淬炼刀刃、收集光辉（没费）",
                C<Glow>(), C<Radiate>(),
                new ScriptCard { Card = typeof(RefineBlade), UpgradeMinAscension = 9 },
                new ScriptCard { Card = typeof(GatherLight), UpgradeMinAscension = 8 }),

            // ── 7 ────────────────────────────────────────────────────────────────
            T(3, "7. 君王之剑、汇流+、崇拜（没费）",
                C<SovereignBlade>(), C<Convergence>(true), C<Venerate>(true)),

            // ── 8 ────────────────────────────────────────────────────────────────
            T(3, "8. 崇拜、隐秘藏品、星尘、防御",
                C<Venerate>(true), C<HiddenCache>(), C<Stardust>(), C<DefendRegent>()),

            // ── 9 ────────────────────────────────────────────────────────────────
            // 循环起点：9～13。
            new BossTurn
            {
                Note = "9. 洗牌。汇流+、隐秘藏品、隐秘藏品（没费）",
                Energy = 2,
                ShuffleDrawPile = true,
                Cards = new List<ScriptCard>
                {
                    C<Convergence>(true), C<HiddenCache>(), C<HiddenCache>(),
                },
            },

            // ── 10 ───────────────────────────────────────────────────────────────
            T(3, "10. 辐射、铸墙、淬炼刀刃、隐秘藏品（没费）",
                C<Radiate>(), C<Bulwark>(),
                new ScriptCard { Card = typeof(RefineBlade), UpgradeMinAscension = 9 },
                C<HiddenCache>()),

            // ── 11 ───────────────────────────────────────────────────────────────
            // 共鸣**不进进阶 9 的升级表**（用户 2026-11-10 订正：「进阶9不再升级共鸣」）
            T(3, "11. 共鸣、君王之剑、防御（没费）",
                C<Resonance>(),
                C<SovereignBlade>(), C<DefendRegent>()),

            // ── 12 ───────────────────────────────────────────────────────────────
            T(4, "12. 星位序列（2 星）、辉光、震荡波、收集光辉",
                C<Alignment>(), C<Glow>(),
                // 震荡波带「消耗」⇒ 附魔「灵魂之力」去掉它（用户 2026-11-10 指定）
                new ScriptCard { Card = typeof(Shockwave), UpgradeMinAscension = 9, EnchantSouls = true },
                new ScriptCard { Card = typeof(GatherLight), UpgradeMinAscension = 8 }),

            // ── 13 ───────────────────────────────────────────────────────────────
            T(2, "13. 崇拜、星尘、天际钻头、防御（没费）",
                C<Venerate>(true), C<Stardust>(),
                new ScriptCard { Card = typeof(HeavenlyDrill), SpendsAllEnergy = true },
                C<DefendRegent>()),
        };

        return new BossScript
        {
            Turns = turns,
            LoopFrom = 9,      // 9～13 循环（用户设计「之后 9～13 循环」）
            LoopLength = 5,
        };
    }

    /// <summary>卡组（用户清单：剧本里出现过的牌 / 「抉择，抉择」挑出来的那 6 张候选）。</summary>
    public static List<Type> Deck() => new()
    {
        typeof(ChildOfTheStars), typeof(Venerate), typeof(Royalties), typeof(DecisionsDecisions),
        typeof(HiddenCache), typeof(Bulwark), typeof(Alignment), typeof(Resonance), typeof(Shockwave),
        typeof(HeavenlyDrill), typeof(BigBang), typeof(Glow), typeof(GatherLight), typeof(Radiate),
        typeof(RefineBlade), typeof(Parry), typeof(Stardust), typeof(Convergence), typeof(DefendRegent),
    };

    /// <summary>
    /// 遗物（用户 2026-11-10 的最终清单）：
    /// 天赋君权、涅奥的祝福、准备背包、燃烧木棍、玻璃眼珠、击剑指南、王室印章、钗、**灯笼**。
    /// （锚换成了灯笼；涅奥的祝福由 <c>CarriesNeowBlessing</c> 自动带，这里写出来只为顺序。）
    /// </summary>
    public static List<Type> Relics() => new()
    {
        typeof(DivineRight),            // 天赋君权（进房间 +3 星）
        typeof(CharacterBosses.Relics.NeowBlessing),   // 涅奥的祝福（已在身上会被跳过）
        typeof(BagOfPreparation),       // 准备背包
        typeof(BurningSticks),          // 燃烧木棍
        typeof(GlassEye),               // 玻璃眼珠
        typeof(FencingManual),          // 击剑指南（回合开始铸造 + 给君王之剑）
        typeof(RoyalStamp),             // 王室印章
        typeof(Sai),                    // 钗（回合开始 +7 格挡）
        typeof(Lantern),                // 灯笼（第 1 回合 +1 能量）
    };

    /// <summary>剧本里的普通一张牌。</summary>
    private static ScriptCard C<T>(bool upgraded = false)
        where T : MegaCrit.Sts2.Core.Models.CardModel
        => new() { Card = typeof(T), Upgraded = upgraded, From = "deck" };

    /// <summary>
    /// 「抉择，抉择+」挑候选时**只显示、不出**的那几张（用户：「都摆出来，但是被选的那五张牌暗着」）。
    ///
    /// ★ 2026-11-10 第二轮订正：暗着**但要画意图** ——
    /// 铸墙 = 守势+强化、隐秘藏品 = 强化、共鸣 = 策略+强化、震荡波 = 策略、
    /// 星位序列 = 标出「选它之后天际钻头会多打多少」。
    /// </summary>
    private static ScriptCard Display<T>(BossIntentTag intents = BossIntentTag.None, bool drillGain = false,
                                         bool souls = false)
        where T : MegaCrit.Sts2.Core.Models.CardModel
        => new()
        {
            Card = typeof(T),
            From = "display",
            DisplayOnly = true,
            ForceIntents = intents,
            ShowsDrillGain = drillGain,
            EnchantSouls = souls,
        };

    /// <summary>
    /// 那 5 张候选技能牌（用户设计：内容固定，顺序 = 铸墙、隐秘藏品、星位序列、共鸣、震荡波…
    /// 这里按用户原话的次序：隐秘藏品、铸墙、星位序列、共鸣、震荡波）。
    /// 「灵魂之力」附在**震荡波**上（用户 2026-11-10：它本身带「消耗」）。
    /// </summary>
    /// <summary>「抉择，抉择+」（附魔王室认证 = 固有 + 保留）。<paramref name="hold"/> = 本回合留着不出。</summary>
    private static ScriptCard Choice(bool hold = false)
        => new()
        {
            Card = typeof(DecisionsDecisions),
            Upgraded = true,
            EnchantRoyallyApproved = true,
            HoldInstead = hold,
            From = "deck",
        };

    private static BossTurn T(int energy, string note, params ScriptCard[] cards)
        => new() { Note = note, Energy = energy, Cards = new List<ScriptCard>(cards) };
}
