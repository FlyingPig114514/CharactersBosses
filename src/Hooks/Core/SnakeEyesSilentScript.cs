using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Relics;
using CharacterBosses.Relics;

namespace CharacterBosses.Core;

/// <summary>
/// 二层「**蛇眼猎**」（静默猎手 · 异蛇之眼轴，Key `SILENT_SNECKO`）—— 用户 2026-11-03 设计稿。
/// 这是二层静默猎手的**第二套设计**（第一套是「涂毒猎」`SilentPoisonScript`），
/// 与「放血战 / 杂耍战」「铸剑储君 / 王储」「根除骨 / 养手骨」同一套机制：
/// **以房间遭遇为准**（`SILENT_SNECKO_BOSS_ENCOUNTER`），拿不到遭遇才按种子 50/50。
///
/// 完整设计稿（用户原话 + 名字→类对照 + 能量账）见
/// `docs/plans/boss-snake-eyes-and-event-machine.md`。
///
/// ## 异蛇之眼（费用随机）
/// 原版「异蛇之眼」会给手牌**随机改费**。这里**拦掉随机**（她的手牌本来就是剧本重建的），
/// 改用 <see cref="ScriptCard.CostThisTurn"/> 把每张牌的费用**写成用户给的那个数字**
/// ⇒ 卡面显示、规划扣费、真正花掉的能量三者一致，观感就是「蛇眼随出来的数」。
///
/// ## 特殊效果「计划妥当」
/// 「每回合第一次抽到耗能为 3 的卡牌时，将其保留」。我们的手牌不是真抽来的，
/// 所以直接把这些牌在剧本里标 `Retain`（用户原文也已经用「(保留)」标好了），
/// 再由 `Powers/BossWellLaidPlansPower` 在状态栏上说明。
///
/// ## 子弹时间
/// 「将本回合后面所有牌降为 0 费」⇒ 后面那几张的 `CostThisTurn` 直接写 0。
///
/// ## 能量
/// 逐回合按「打出的牌」倒推（见设计稿的表）：第 1 回合 0 费（只打两张 0 费牌）、第 4 回合 2 费、
/// 其余 3 费。⚠ 基础能量是 3 − 涅奥 1 = 2，所以这里都是剧本写死的覆盖值。
/// </summary>
public static class SnakeEyesSilentScript
{
    public static BossScript Build()
    {
        var turns = new List<BossTurn>
        {
            // 1：背刺 0、刺杀 0（手里的子弹时间 3 费保留着）
            new()
            {
                Note = "1. 背刺(0)、刺杀(0)；不出：子弹时间(3,保留)、先制打击(1)、打击(1)、防御(2)、匕首雨(1)",
                ForceZeroEnergy = true,
                Cards = Cards(
                    Play<Backstab>(0), Play<Assassinate>(0),
                    Hold<BulletTime>(3, retain: true), Hold<LeadingStrike>(1), Hold<StrikeSilent>(1),
                    Hold<DefendSilent>(2), Hold<DaggerSpray>(1)),
            },
            // 2：冲刺 2 + 刀刃之舞 1 = 3
            // ★ v0.2.364（用户 2026-11-04）：「把突然一拳和刀刃之舞互换（改成出刀刃之舞不出突然一拳）」。
            //   原来这回合出的是「突然一拳」，刀刃之舞只在手里躺着（整份剧本里从没打出去过）。
            //   现在反过来：**打刀刃之舞**（它印 3 把小刀，那双 0 费小刀当回合会被 Frontload 打出去，
            //   所以这条意图下面会自动多出「小刀 4×3」），**突然一拳留手**。
            Turn(3, "2. 冲刺(2)、刀刃之舞(1)；不出：切割(2)、突然一拳(1)、跟踪(3,保留)、子弹时间(3,保留)", 2,
                Play<Dash>(2), Play<BladeDance>(1),
                Hold<Slice>(2), Hold<SuckerPunch>(1), Hold<Tracking>(3, retain: true), Hold<BulletTime>(3, retain: true)),

            // 3：预判 1 + 闪避翻滚 2 = 3
            Turn(3, "3. 预判(1)、闪避翻滚(2)；不出：斗篷与匕首(1)、笨拙、受伤、跟踪(3,保留)、子弹时间(3,保留)", 3,
                Play<Anticipate>(1), Play<DodgeAndRoll>(2),
                Hold<CloakAndDagger>(1), C<Clumsy>(), C<Injury>(),
                Hold<Tracking>(3, retain: true), Hold<BulletTime>(3, retain: true)),

            // 4：尖啸 2 + 声东击西 0（印触不可及）+ 触不可及 0 = 2
            Turn(2, "4. 尖啸(2)、声东击西(0, 印触不可及)、触不可及(0)；不出：笨拙、究极防御(2)、谋杀(3,保留)、跟踪(3,保留)、子弹时间(保留)", 4,
                Play<PiercingWail>(2), Play<Distraction>(0), Play<Untouchable>(0),
                C<Clumsy>(), Hold<UltimateDefend>(2), Hold<Murder>(3, retain: true),
                Hold<Tracking>(3, retain: true), Hold<BulletTime>(3, retain: true)),

            // 5：子弹时间 3（后面全部 0 费）跟踪、谋杀、先制打击、打击、防御、切割
            Turn(3, "5. 子弹时间(3, 后面的牌全部 0 费)、跟踪、谋杀、先制打击、打击、防御、切割", 5,
                Play<BulletTime>(3), Play<Tracking>(0), Play<Murder>(0), Play<LeadingStrike>(0),
                Play<StrikeSilent>(0), Play<DefendSilent>(0), Play<Slice>(0)),

            // 6：预判 1 + 冲刺 0 + 突然一拳 2 = 3
            Turn(3, "6. 预判(1)、冲刺(0)、突然一拳(2)；不出：斗篷与匕首(2)、触不可及(2)", 6,
                Play<Anticipate>(1), Play<Dash>(0), Play<SuckerPunch>(2),
                Hold<CloakAndDagger>(2), Hold<Untouchable>(2)),

            // 7：闪躲翻滚 1 + 究极防御 1 + 触不可及 1 = 3
            Turn(3, "7. 闪躲翻滚(1)、究极防御(1)、触不可及(1)；不出：匕首雨(3,保留)、受伤", 7,
                Play<DodgeAndRoll>(1), Play<UltimateDefend>(1), Play<Untouchable>(1),
                Hold<DaggerSpray>(3, retain: true), C<Injury>()),

            // 8：子弹时间 +（后面全部 0 费）谋杀、匕首雨、先制打击、打击、防御、切割、触不可及
            Turn(3, "8. 子弹时间(3, 后面的牌全部 0 费)、谋杀、匕首雨、先制打击、打击、防御、切割、触不可及", 8,
                Play<BulletTime>(3), Play<Murder>(0), Play<DaggerSpray>(0), Play<LeadingStrike>(0),
                Play<StrikeSilent>(0), Play<DefendSilent>(0), Play<Slice>(0), Play<Untouchable>(0)),

            // 9：预判 0 + 冲刺 2 + 突然一拳 1 = 3
            Turn(3, "9. 预判(0)、冲刺(2)、突然一拳(1)；不出：斗篷与匕首(1)、闪躲翻滚(1)", 9,
                Play<Anticipate>(0), Play<Dash>(2), Play<SuckerPunch>(1),
                Hold<CloakAndDagger>(1), Hold<DodgeAndRoll>(1)),

            // 10：子弹时间 + 究极防御、谋杀、匕首雨、（受伤）
            Turn(3, "10. 子弹时间(3, 后面的牌全部 0 费)、究极防御、谋杀、匕首雨、受伤", 10,
                Play<BulletTime>(3), Play<UltimateDefend>(0), Play<Murder>(0), Play<DaggerSpray>(0),
                C<Injury>()),

            // 11：先制打击 1 + 触不可及 0 + 突然一拳 2 = 3
            Turn(3, "11. 先制打击(1)、触不可及(0)、突然一拳(2)；不出：打击(1)、防御(2)", 11,
                Play<LeadingStrike>(1), Play<Untouchable>(0), Play<SuckerPunch>(2),
                Hold<StrikeSilent>(1), Hold<DefendSilent>(2)),

            // 12：预判 0 + 闪躲翻滚 1 + 冲刺 2 = 3
            Turn(3, "12. 预判(0)、闪躲翻滚(1)、冲刺(2)；不出：斗篷与匕首(2)、切割(2)", 12,
                Play<Anticipate>(0), Play<DodgeAndRoll>(1), Play<Dash>(2),
                Hold<CloakAndDagger>(2), Hold<Slice>(2)),
        };

        return new BossScript
        {
            Turns = turns,
            LoopFrom = 10,     // 用户：「之后 10～12 循环」
            LoopLength = 3,
        };
    }

    /// <summary>卡组（剧本出现过的牌各一份；「不出」的那些也留在卡组里，只是剧本不让打）。</summary>
    public static List<Type> Deck() => new()
    {
        typeof(Backstab), typeof(Assassinate), typeof(BulletTime), typeof(LeadingStrike),
        typeof(StrikeSilent), typeof(DefendSilent), typeof(DaggerSpray), typeof(Dash),
        typeof(SuckerPunch), typeof(Slice), typeof(BladeDance), typeof(Tracking),
        typeof(Anticipate), typeof(DodgeAndRoll), typeof(CloakAndDagger), typeof(PiercingWail),
        typeof(Distraction), typeof(Untouchable), typeof(UltimateDefend), typeof(Murder),
        typeof(Clumsy), typeof(Injury),
    };

    /// <summary>遗物（用户清单；涅奥的祝福由 <c>CarriesNeowBlessing</c> 自动带）。</summary>
    public static List<Type> Relics() => new()
    {
        typeof(RingOfTheSnake),   // 蛇之戒指（起始）
        typeof(NeowBlessing),     // 涅奥的祝福（所有 Boss 都是第 2 件）
        typeof(Bread),            // 面包
        typeof(Anchor),           // 锚：战斗开始获得格挡
        typeof(SneckoEye),        // 异蛇之眼：手牌费用随机（我们用剧本写死费用来复刻）
        typeof(MrStruggles),      // 抱抱先生：回合开始对所有玩家造成「她的回合数」伤害
        typeof(CloakClasp),        // 斗篷扣：回合结束时每张手牌给 N 点格挡（用户 2026-11-03 加的；BossHookBridge/CloakClaspPlays 手动结算）
    };

    // ── 小工具 ──────────────────────────────────────────────

    private static BossTurn Turn(int energy, string note, int turnIndex, params ScriptCard[] cards)
        => new() { Note = $"{turnIndex}. {note}", Cards = new List<ScriptCard>(cards), Energy = energy };

    private static List<ScriptCard> Cards(params ScriptCard[] cards) => new(cards);

    private static ScriptCard C<T>() where T : MegaCrit.Sts2.Core.Models.CardModel
        => new() { Card = typeof(T), From = "deck" };

    /// <summary>本回合**真的打出去**、费用按用户给的数字（异蛇之眼随出来的那个数）。</summary>
    private static ScriptCard Play<T>(int cost) where T : MegaCrit.Sts2.Core.Models.CardModel
        => new() { Card = typeof(T), From = "deck", CostThisTurn = cost };

    /// <summary>
    /// 本回合**留在手里不出**的那张（原本是「没费/费用太高」），费用照写，
    /// 卡面显示的就是蛇眼随出来的数；`retain` = 「计划妥当」保留（回合结束不进弃牌堆）。
    /// </summary>
    private static ScriptCard Hold<T>(int cost, bool retain = false)
        where T : MegaCrit.Sts2.Core.Models.CardModel
        => new() { Card = typeof(T), From = "deck", CostThisTurn = cost, HoldInstead = true, Retain = retain };
}
