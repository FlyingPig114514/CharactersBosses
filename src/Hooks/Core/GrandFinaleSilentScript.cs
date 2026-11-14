using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Relics;
using CharacterBosses.Relics;

namespace CharacterBosses.Core;

/// <summary>
/// 三层静默猎手**第三套设计「华丽猎」**（Key `SILENT_GRAND_FINALE`，用户 2026-11-09 设计）。
/// 同层前两套是「刀猎」（<see cref="SilentScript"/>）与「炼药猎」（<see cref="SilentBrewerScript"/>）。
///
/// 用户原话（逐条照抄，改设计时对着看）：
/// > 特殊效果「压制」：每当静默猎手打出华丽收场时，将一张虚空放入所有玩家的抽牌堆顶部。
/// > 遗物：蛇之戒指、涅奥的祝福、弹珠袋、佩尔之肉、绷带、布质果实、水银沙漏
/// > 进阶 8：升级中和。进阶 9：升级背刺、钢铁风暴、隐秘匕首
/// > 意图：（见下面 10 个回合）
/// > 之后 8～10 循环
/// > 拦掉原版的抽牌，每张弃牌卡的丢弃哪张牌都要按剧本上来
/// > 塞牌记得用原版怪的塞牌动画
/// > 内部名字用「华丽收场」的英文
/// > 藏品：计划妥当
///
/// 剧本约定（与其它设计一致）：
///   · `C&lt;T&gt;()` = 打出去；`Kept&lt;T&gt;()` = **保留不出**（进手牌只显示、不参与出牌）；
///   · `Discard&lt;T&gt;()` = **被上一张牌丢弃**（`DiscardInstead`，自动选择器会照这个点名丢）；
///   · `Sly&lt;T&gt;()` = 被丢弃且带**奇巧**（丢弃时自动打出）；
///   · `PickOrder` = 剧本点名的「选择队列」（夜魇要复制的牌、手上技法要加奇巧的那张技能牌）。
///
/// 能量：用户没给每回合能量，这里**按「这一回合要打出去的牌的费用之和」写死**
/// （她基础 3 − 涅奥的祝福 1 = 2，剧本里多出来的部分由肾上腺素/战术大师当场返还，
/// 写死整数值只是为了不让计划器把「费不够」的牌跳过 —— 与其它设计同一套做法）。
/// </summary>
public static class GrandFinaleSilentScript
{
    public static BossScript Build()
    {
        var turns = new List<BossTurn>
        {
            // 1：计划妥当、背刺、华丽收场（保留不出）、防御（保留不出）、笨拙（诅咒，打不出）
            new BossTurn
            {
                Note = "1. 计划妥当、背刺；华丽收场 / 防御（保留不出）、笨拙（诅咒）",
                Cards = new List<ScriptCard>
                {
                    C<WellLaidPlans>(), C<Backstab>(),
                    Kept<GrandFinale>(), Kept<DefendSilent>(), C<Clumsy>(),
                },
                Energy = 2,
            },

#if STS2_111
            // 2（0.113.0 重做版）：肾上腺素、夜魇+（复制杂技）、杂技（弃战术大师）、战术大师（奇巧）、
            //    隐秘匕首（弃防御 **和铭记死亡**）、铭记死亡（奇巧：被丢掉时免费打出）、防御；
            //    不出：防御（被隐秘匕首丢弃）、防御（保留，没费）、华丽收场（保留，抽牌堆非空）。
            //    能量账：起手 2（3−涅奥 1）+ 肾上腺素 1 + 战术大师（奇巧弃牌）1 = 4
            //           = 夜魇+ 2 + 杂技 1 + 防御 1（铭记死亡是奇巧自动打出，不花能量）。
            new BossTurn
            {
                Note = "2. 肾上腺素、夜魇+（复制杂技）、杂技（弃战术大师）、战术大师（奇巧）、隐秘匕首（弃防御和铭记死亡）、铭记死亡（奇巧）、防御",
                Cards = new List<ScriptCard>
                {
                    C<Adrenaline>(), C<Nightmare>(true), C<Acrobatics>(),
                    SlyDiscard<Tactician>(),
                    C<HiddenDaggers>(),
                    Discard<DefendSilent>(), SlyDiscard<MementoMori>(),
                    C<DefendSilent>(),
                    Kept<DefendSilent>(), Kept<GrandFinale>(),
                },
                Energy = 2,
                PickOrder = new List<Type> { typeof(Acrobatics) },   // 夜魇复制的就是「杂技」
            },
#else
            // 2：肾上腺素、夜魇+（夜魇杂技）、杂技（弃战术大师）、战术大师（奇巧）、隐秘匕首（弃 2 张防御）、
            //    铭记死亡、防御×2（被丢弃）、防御 / 华丽收场（保留不出）
            new BossTurn
            {
                Note = "2. 肾上腺素、夜魇+（复制杂技）、杂技（弃战术大师）、战术大师（奇巧）、隐秘匕首（弃 2 防御）、铭记死亡",
                Cards = new List<ScriptCard>
                {
                    C<Adrenaline>(), C<Nightmare>(true), C<Acrobatics>(),
                    SlyDiscard<Tactician>(),
                    C<HiddenDaggers>(),
                    Discard<DefendSilent>(), Discard<DefendSilent>(),
                    C<MementoMori>(),
                    Kept<DefendSilent>(), Kept<GrandFinale>(),
                },
                // 用户 2026-11-09：「注意肾上腺素和战术大师都会给费」
                //   ⇒ 起手只给 2 费：肾上腺素(+1) 打完就有 3，杂技丢掉战术大师(奇巧 +1) 再 +1，
                //     够 夜魇+(2) + 杂技(1) + 铭记死亡(1)。
                Energy = 2,
                PickOrder = new List<Type> { typeof(Acrobatics) },   // 夜魇复制的就是「杂技」
            },

#endif

            // 3：中和、手上技法（为防御添加奇巧）、钢铁风暴（丢光剩下 5 张：防御(奇巧)、杂技×3、华丽收场）
            new BossTurn
            {
                Note = "3. 中和、手上技法（给防御加奇巧）、钢铁风暴（丢光剩下的 5 张）",
                Cards = new List<ScriptCard>
                {
                    C<Neutralize>(), C<HandTrick>(), C<StormOfSteel>(),
                    Discard<DefendSilent>(),
                    NotDrawn(Discard<Acrobatics>()), NotDrawn(Discard<Acrobatics>()), NotDrawn(Discard<Acrobatics>()),
                    Discard<GrandFinale>(),
                },
                Energy = 2,
                PickOrder = new List<Type> { typeof(DefendSilent) },   // 手上技法要加奇巧的那张
            },

            // 4：谋杀、必备工具+、偏折
            new BossTurn
            {
                Note = "4. 谋杀、必备工具+、偏折",
                Cards = new List<ScriptCard>
                {
                    C<Murder>(), C<ToolsOfTheTrade>(true), C<Deflect>(),
                },
                Energy = 3,
            },

            // 5：打击、打击、防御、打击（被必备工具丢弃）
            new BossTurn
            {
                Note = "5. 打击、打击、防御；打击（被必备工具丢弃）",
                Cards = new List<ScriptCard>
                {
                    C<StrikeSilent>(), C<StrikeSilent>(), C<DefendSilent>(),
                    ToolsDiscard(Discard<StrikeSilent>()),
                },
                Energy = 3,
            },

            // 6：尖啸、打击、生存者（打出它时手牌已空 ⇒ 不弃牌）；防御（回合开始时被必备工具丢弃）
            new BossTurn
            {
                Note = "6. 尖啸、打击、生存者（手牌空、不弃牌）；防御（被必备工具丢弃）",
                Cards = new List<ScriptCard>
                {
                    // ★ v0.2.525（用户 2026-11-09 亲自改口径）：
                    //   「我好像漏算了必备工具的弃牌。不过没关系一样。改成这样：
                    //     尖啸、打击、生存者（手牌是空的，不弃牌）、防御（回合开始时被必备工具丢弃）」
                    //   ⇒ 必备工具先丢掉那张「防御」，于是剩下三张正好打完、手牌清空，
                    //     最后打出的「生存者」没有牌可丢（原版「弃 1 张」在空手时自然什么都不丢）。
                    ToolsDiscard(Discard<DefendSilent>()),
                    C<PiercingWail>(), C<StrikeSilent>(), C<Survivor>(),
                },
                // 尖啸 1 + 打击 1 + 生存者 1 = 3（三张都打得出）
                Energy = 3,
            },

            // 7：洗牌。杂技（弃打击）、手上技法（为防御加奇巧）、打击、隐秘匕首（丢防御，只丢一张）、
            //    防御（奇巧）、钢铁风暴（被必备工具丢弃）
            new BossTurn
            {
                Note = "7. 洗牌；杂技（弃打击）、手上技法（给防御加奇巧）、打击、隐秘匕首（丢防御）、防御（奇巧）；钢铁风暴（被必备工具丢弃）",
                ShuffleDrawPile = true,
                Cards = new List<ScriptCard>
                {
                    C<Acrobatics>(), Discard<StrikeSilent>(),
                    C<HandTrick>(), C<StrikeSilent>(),
                    C<HiddenDaggers>(), Discard<DefendSilent>(),
                    ToolsDiscard(Discard<StormOfSteel>()),   // 用户口径：被「必备工具」丢弃的是钢铁风暴
                },
                Energy = 3,
                PickOrder = new List<Type> { typeof(DefendSilent) },
            },

            // 8：杂技（弃战术大师）、战术大师（奇巧）、谋杀、偏折、生存者（被必备工具丢弃）、
            //    防御 / 打击（保留不出）
            new BossTurn
            {
                Note = "8. 杂技（弃战术大师）、战术大师（奇巧）、谋杀、偏折；生存者（被必备工具丢弃）、防御 / 打击（保留不出）",
                Cards = new List<ScriptCard>
                {
                    C<Acrobatics>(), SlyDiscard<Tactician>(),
                    C<Murder>(), C<Deflect>(),
                    ToolsDiscard(Discard<Survivor>()),
                    Kept<DefendSilent>(), Kept<StrikeSilent>(),
                },
                // 杂技(1) + 谋杀(3) + 偏折(0)，其中战术大师被杂技丢掉时（奇巧）回 1 费 ⇒ 起手 3 费够。
                Energy = 3,
            },

#if STS2_111
            // 9（0.113.0 重做版）：杂技（弃铭记死亡）、铭记死亡（奇巧）、中和、防御、防御、华丽收场；
            //    不出：打击（被必备工具丢弃）、防御（保留，没费）、打击（保留，没费）。
            //    能量账：杂技 1 + 防御 1 + 防御 1 = 3（中和 0、华丽收场 0、铭记死亡奇巧免费）。
            new BossTurn
            {
                Note = "9. 杂技（弃铭记死亡）、铭记死亡（奇巧）、中和、防御、防御、华丽收场；打击（被必备工具丢弃）、防御 / 打击（保留不出）",
                Cards = new List<ScriptCard>
                {
                    C<Acrobatics>(), SlyDiscard<MementoMori>(),
                    C<Neutralize>(), C<DefendSilent>(), C<DefendSilent>(),
                    C<GrandFinale>(emptyDrawPileFirst: true),
                    ToolsDiscard(Discard<StrikeSilent>()),
                    Kept<DefendSilent>(), Kept<StrikeSilent>(),
                },
                Energy = 3,
            },
#else
            // 9：杂技（弃防御）、铭记死亡、中和、防御、华丽收场、打击（被必备工具丢弃）、
            //    防御 / 打击（保留不出）
            new BossTurn
            {
                Note = "9. 杂技（弃防御）、铭记死亡、中和、防御、华丽收场；打击（被必备工具丢弃）、防御 / 打击（保留不出）",
                Cards = new List<ScriptCard>
                {
                    C<Acrobatics>(), Discard<DefendSilent>(),
                    C<MementoMori>(), C<Neutralize>(), C<DefendSilent>(),
                    C<GrandFinale>(emptyDrawPileFirst: true),
                    ToolsDiscard(Discard<StrikeSilent>()),
                    Kept<DefendSilent>(), Kept<StrikeSilent>(),
                },
                Energy = 3,
            },

#endif

            // 10：洗牌。杂技（弃防御）、手上技法（为防御加奇巧）、钢铁风暴（丢光剩下 4 张）、
            //     防御（奇巧）、打击（被必备工具丢弃）
            new BossTurn
            {
                Note = "10. 洗牌；杂技（弃防御）、手上技法（给防御加奇巧）、钢铁风暴（丢光剩下的 4 张）、防御（奇巧）；打击（被必备工具丢弃）",
                ShuffleDrawPile = true,
                Cards = new List<ScriptCard>
                {
                    C<Acrobatics>(), Discard<DefendSilent>(),
                    C<HandTrick>(), C<StormOfSteel>(),
                    Discard<DefendSilent>(),
                    Discard<HiddenDaggers>(), Discard<StrikeSilent>(), Discard<StrikeSilent>(),
                    ToolsDiscard(Discard<StrikeSilent>()),
                },
                Energy = 3,
                PickOrder = new List<Type> { typeof(DefendSilent) },
            },
        };

        return new BossScript
        {
            Turns = turns,
            LoopFrom = 8,      // 用户：「之后 8～10 循环」
            LoopLength = 3,
            // ★ 2026-11-13（恐惧模式，用户当轮口径）：「增强恐惧模式下的华丽猎，把第 6 回合的尖啸换成精准+」。
            //   用 `Resolve`（**运行期**求值）而不是在 `Build()` 里判 `DreadMode.IsActive` ——
            //   `BossDefs` 是静态初始化，那时 IsActive 恒为 false（同一个坑见蛇眼猎·谋杀 / 辉星储换牌）。
            //   第 6 回合不在 8～10 的循环里，所以按回合号覆盖是安全的。
            Resolve = DreadTurnOverride,
        };
    }

    /// <summary>
    /// 恐惧模式的回合覆盖：把第 6 回合的「尖啸」（`PiercingWail`，1 费）换成「**精准+**」
    /// （`Accuracy` 升级版，「小刀额外造成 6 点伤害」，1 费 —— 比尖啸更贴「增强」的意思：
    /// 第 7/10 回合的隐秘匕首、钢铁风暴都会掉小刀，精准+ 正好给小刀加伤）。
    /// 本回合能量仍是 3：精准+ 1 + 打击 1 + 生存者 1 = 3，正好打完。
    ///
    /// ⚠ v0.2.638：第一版（0.2.636/637）这里误写成了 `PreciseCut`（**精确切击**，0 费，
    ///   是「炼药猎」第 5 回合用的牌，华丽猎卡组里压根没有）—— 用户当场认出来「她都打精确切击了，
    ///   华丽猎卡组里根本没有这张牌」。中文「精准」= `Accuracy`，别再和「精确切击」搞混。
    /// </summary>
    private static BossTurn? DreadTurnOverride(int turn)
    {
        if (!Core.DreadMode.IsActive) return null;
        if (turn != 6) return null;

        return new BossTurn
        {
            Note = "6.（恐惧）精准+、打击、生存者（手牌空、不弃牌）；防御（被必备工具丢弃）",
            Cards = new List<ScriptCard>
            {
                ToolsDiscard(Discard<DefendSilent>()),
                C<Accuracy>(true), C<StrikeSilent>(), C<Survivor>(),
            },
            Energy = 3,
        };
    }

    /// <summary>
    /// 「谋杀」（<c>Murder</c>）在**第 N 回合**应该打多少伤害 —— 用户 2026-11-09 最终口径（**写死，不再推算**）：
    ///
    /// > 「感觉让你算有点儿麻烦。你就直接按第四回合打 20，第八回合打 42，之后每回合成长 7，
    /// >  也就是每个循环成长 21，第十一回合的时候打 63，第 14 回合的时候打 84……这个样子」
    ///
    /// 于是：
    ///   · 第 4 回合 = 20（她自己给的算式 1 + 5 + 8 + 3 + 3）；
    ///   · 第 5/6/7 回合 = 24 / 28 / 35（按她原来那串「每回合新进手牌 4、4、7、7」累加上去）；
    ///   · 第 8 回合 = 42（1 + 19 + 4 + 4 + 7 + 7）；
    ///   · 第 9 回合起每回合 +7（第 9 = 49、第 10 = 56、第 11 = 63、第 14 = 84 …），
    ///     正好等于「8~10 每循环 +21」。
    ///
    /// 为什么改成**写死**而不是继续记账：她的牌每回合重建、抽牌被拦、夜魇印牌与计划妥当保留都要排除，
    /// 记账口径怎么调都容易差一两张（v0.2.523/524 连试两版都被否）。用户直接给了数字，就照数字来。
    /// </summary>
    public static int MurderDamageForTurn(int turn)
    {
        if (turn <= 3) return 1;                       // 剧本里第 4 回合才第一次打它
        if (turn == 4) return 20;
        if (turn == 5) return 24;
        if (turn == 6) return 28;
        if (turn == 7) return 35;
        return 42 + 7 * (turn - 8);                   // 第 8 回合 = 42，之后每回合 +7
    }

    /// <summary>卡组（按剧本里出现的最大张数收集；只用于展示与自检）。</summary>
    public static List<Type> Deck() => new()
    {
        typeof(WellLaidPlans), typeof(Backstab), typeof(Backstab),
        typeof(GrandFinale), typeof(GrandFinale),
        typeof(Clumsy),
        typeof(Adrenaline),
        typeof(Nightmare),
        typeof(Acrobatics), typeof(Acrobatics), typeof(Acrobatics),
        typeof(Tactician), typeof(Tactician),
        typeof(HiddenDaggers), typeof(HiddenDaggers),
        typeof(MementoMori), typeof(MementoMori),
        typeof(Neutralize),
        typeof(HandTrick), typeof(HandTrick), typeof(HandTrick),
        typeof(StormOfSteel), typeof(StormOfSteel),
        typeof(Murder), typeof(Murder),
        typeof(ToolsOfTheTrade), typeof(ToolsOfTheTrade),
        typeof(Deflect), typeof(Deflect),
        typeof(Survivor), typeof(Survivor),
        typeof(StrikeSilent), typeof(StrikeSilent), typeof(StrikeSilent),
        typeof(StrikeSilent), typeof(StrikeSilent), typeof(StrikeSilent),
        typeof(DefendSilent), typeof(DefendSilent), typeof(DefendSilent),
        typeof(DefendSilent), typeof(DefendSilent), typeof(DefendSilent),
        typeof(PiercingWail),
        // 恐惧模式第 6 回合把「尖啸」换成「精准+」（`Accuracy`，**不是** `PreciseCut` 精确切击）
        // ⇒ 卡组里也要有它（见 DreadTurnOverride）。普通模式这张牌不会出现在手牌里
        // （本变体 `BlocksVanillaDraw = true`，抽牌被拦、手牌完全由剧本给）。
        typeof(Accuracy),
    };

    /// <summary>遗物（用户给的 7 个，顺序照抄）。</summary>
    public static List<Type> Relics() => new()
    {
        typeof(RingOfTheSnake),        // 蛇之戒指
        typeof(NeowBlessing),          // 涅奥的祝福（所有 Boss 都是第 2 件）
        typeof(BagOfMarbles),          // 弹珠袋：战斗开始给所有敌人易伤
        typeof(PaelsFlesh),            // 佩尔之肉
        typeof(ToughBandages),         // 绷带：每次弃牌获得格挡
        typeof(LoomingFruit),          // 布质果实：最大生命 +
        typeof(MercuryHourglass),      // 水银沙漏：回合开始对所有敌人造成伤害
    };

    // ── 剧本小工具（与本模组其它设计同一套写法） ─────────────────────────

    /// <summary>打出去。</summary>
    private static ScriptCard C<T>(bool upgraded = false, bool emptyDrawPileFirst = false)
        where T : MegaCrit.Sts2.Core.Models.CardModel
        => new() { Card = typeof(T), Upgraded = upgraded, From = "deck", EmptyDrawPileFirst = emptyDrawPileFirst };

    /// <summary>
    /// 「保留不出」：本回合**不参与出牌**（只显示）。
    /// ★ v0.2.522（用户 2026-11-09：「不用给我标注保留在手的那些牌添加保留，因为计划妥当就能做到这一点」）：
    ///   这里**不再**加 Retain 关键字（那张牌上就不会显示「保留」字样）—— 第 1 回合打出「计划妥当」之后，
    ///   原版能力本身就会让她的手牌在回合结束时不被弃掉。
    /// </summary>
    private static ScriptCard Kept<T>() where T : MegaCrit.Sts2.Core.Models.CardModel
        => new() { Card = typeof(T), From = "deck", DisplayOnly = true };

    /// <summary>标成「被必备工具在她回合开始丢掉」的那一张。</summary>
    private static ScriptCard ToolsDiscard(ScriptCard card)
        => new()
        {
            Card = card.Card, From = card.From, DiscardInstead = true,
            Sly = card.Sly, NotDrawn = card.NotDrawn, ToolsDiscard = true,
        };

    /// <summary>标成「不是抽到的牌」（夜魇印出来的复制品）。</summary>
    private static ScriptCard NotDrawn(ScriptCard card)
        => new()
        {
            Card = card.Card, From = card.From, DiscardInstead = card.DiscardInstead,
            Sly = card.Sly, DisplayOnly = card.DisplayOnly, NotDrawn = true,
        };

    /// <summary>被上一张牌**丢弃**的那张。</summary>
    private static ScriptCard Discard<T>() where T : MegaCrit.Sts2.Core.Models.CardModel
        => new() { Card = typeof(T), From = "deck", DiscardInstead = true };

    /// <summary>被上一张牌**丢弃**、并且带「奇巧」（丢弃时自动打出）的那张。</summary>
    private static ScriptCard SlyDiscard<T>() where T : MegaCrit.Sts2.Core.Models.CardModel
        => new() { Card = typeof(T), From = "deck", DiscardInstead = true, Sly = true };
}
