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
