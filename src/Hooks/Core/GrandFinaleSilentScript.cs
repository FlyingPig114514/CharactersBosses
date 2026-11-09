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
                    Discard<Acrobatics>(), Discard<Acrobatics>(), Discard<Acrobatics>(),
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
                    Discard<StrikeSilent>(),
                },
                Energy = 3,
            },

            // 6：生存者、打击、尖啸、防御（被生存者丢弃）
            new BossTurn
            {
                Note = "6. 生存者、打击、尖啸；防御（被生存者丢弃）",
                Cards = new List<ScriptCard>
                {
                    C<Survivor>(), Discard<DefendSilent>(), C<StrikeSilent>(), C<PiercingWail>(),
                },
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
                    Discard<StormOfSteel>(),          // 用户口径：被「必备工具」丢弃的是钢铁风暴
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
                    Discard<Survivor>(),
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
                    Discard<StrikeSilent>(),
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
                    Discard<StrikeSilent>(),
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

    /// <summary>被上一张牌**丢弃**的那张。</summary>
    private static ScriptCard Discard<T>() where T : MegaCrit.Sts2.Core.Models.CardModel
        => new() { Card = typeof(T), From = "deck", DiscardInstead = true };

    /// <summary>被上一张牌**丢弃**、并且带「奇巧」（丢弃时自动打出）的那张。</summary>
    private static ScriptCard SlyDiscard<T>() where T : MegaCrit.Sts2.Core.Models.CardModel
        => new() { Card = typeof(T), From = "deck", DiscardInstead = true, Sly = true };
}
