using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Relics;

namespace CharacterBosses.Core;

/// <summary>
/// 静默猎手（三层：刀猎 / 小刀流）的剧本。用户设计，原话要点记在这里免得后面改乱：
///
///   · 遗物：蛇之戒指、手里剑、召唤铃铛、灯笼、精致折扇、苦无、螺线飞镖、**钻石头冠**、音叉。
///     （用户 2026-10-25 削弱：**钗 → 钻石头冠**；钻石头冠 = 战斗开始获得 20 格挡 + 残影，格挡撑过玩家回合）
///     「成长」＝手里剑/苦无/精致折扇：**每打出 3 张攻击牌** → +1 力 / +1 敏 / +4 格挡。
///     这三个都是原版遗物，靠 <c>AfterCardPlayed</c> 自己数，我们不需要实现 ——
///     意图数字会自己随之变高（走 <c>Hook.ModifyDamage</c>）。
///   · 特殊效果「袖里乾坤」：每回合玩家首次打出耗能 ≥ 2 的牌时，给她塞 3 张小刀（见 Powers/SleeveTrickPower）。
///   · 第 **4** 回合打出**幻影之刃**（用户 2026-10-24 削刀猎：把「无尽刀刃」换成它）。
///     用户 2026-10-31：把之前那次「第 4、5 回合对调」**撤回**（恢复成第 4 回合幻影之刃、第 5 回合刀扇）。
///     原版效果：小刀获得保留；**每回合打出的第一张小刀 +9 伤害**。
///     ⚠ 无尽刀刃原本「每回合开始印一张小刀」，换掉之后剧本里那些「无尽刀刃印的小刀」也一并删了
///     —— 那些小刀不再存在，脚本里不能再写。
///   · 11 回合之后，7～11 循环。
///   · **中和在剧本里是未升级的**（用户 2026-10-25 削弱：降级中和，进阶 8 才升级，见 `BossDefs` 的
///     `(8, 3, Neutralize)`）；那 3 点虚弱因此只在进阶 8 以上才变 4 点。
///
/// 能量为什么是每回合写死 2：用户给的每回合账都是 2 费（例如第 1 回合余像 1＋斗篷与匕首 1＝2，
/// 两张防御没费；第 3 回合切割 0＋打击 1＋终结技 1＝2）。写进 <see cref="BossTurn.Energy"/>
/// 比让它受霓奥祝福 −1 / 灯笼 +1 影响更稳，意图不会漂。
/// </summary>
public static class SilentScript
{
    /// <summary>每回合固定 2 费。</summary>
    private const int TurnEnergy = 2;

    /// <summary>第三幕用的剧本（11 回合，之后 7～11 循环）。</summary>
    public static BossScript Build()
    {
        var turns = new List<BossTurn>
        {
            // 用户 2026-10-25 削弱：**中和改回未升级**（进阶 8 才升级，见 BossDefs 的 (8, 3, Neutralize)）
            // 用户 2026-10-31：**第 1 回合按 3 费算**（基础 2 + 灯笼 +1；涅奥的祝福已经含在基础里）
            //   ⇒ 余像 1 + 斗篷与匕首 1（多人时换成刀刃交响曲 2／进阶9+ 升到 1 费）+ 中和 0 之后，
            //     还能打得出一张防御（以前两张防御都没费）。
            Turn("余像、斗篷与匕首（6 格挡＋1 刀）、中和、防御；第二张防御没费", 1, 3, 
                C<Afterimage>(), C<CloakAndDagger>(), C<Neutralize>(),
                C<DefendSilent>(), C<DefendSilent>()),

            Turn("先制打击（3 伤＋2 刀）、防御；打击没费", 2,
                C<LeadingStrike>(), C<DefendSilent>(), C<StrikeSilent>()),

            Turn("切割、打击、终结技（按本回合已打出的攻击次数结算）", 3,
                C<Slice>(), C<StrikeSilent>(), C<Finisher>()),

            // ★ 2026-11-13（用户）：「将第四回合的幻影之刃替换为精准+」
            //   （幻影之刃在 0.113 被削到 8 点，用户索性把这回合改成精准切击+ —— 它 0 费、
            //     伤害 = 13+? − 2 × 手里其它牌数，见 BossDamagePredictor.PreciseCutDamage）
            Turn("精准+（0 费，按出手时手牌数结算）、尖啸、隐秘匕首（0 费给 2 刀，当回合就打出去）", 4,
                C<PreciseCut>(true), C<PiercingWail>(), C<HiddenDaggers>()),

            Turn("刀扇（2 费能力：给 4 刀，且小刀改打全体）；铃铛的诅咒/防御没费", 5,
                C<FanOfKnives>(), C<CurseOfTheBell>(), C<DefendSilent>()),

            Turn("刀刃之舞（3 刀）、生存者（防御被它丢弃）", 6,
                C<BladeDance>(), C<Survivor>(), C<DefendSilent>(discardInstead: true)),

            Turn("洗牌：斗篷与匕首（6 格挡＋1 刀）、防御、中和", 7,
                C<CloakAndDagger>(), C<DefendSilent>(), C<Neutralize>()),

            // 与第 10 回合对调：那张「没费出的防御」挪去第 10 回合，这里换成打击（2 费下它是打不出的那张）
            Turn("先制打击（3 伤＋2 刀）、防御；打击没费", 8,
                C<LeadingStrike>(), C<DefendSilent>(), C<StrikeSilent>()),

            Turn("切割、打击、终结技", 9,
                C<Slice>(), C<StrikeSilent>(), C<Finisher>()),

            // 与第 8 回合对调：这里原本的第二张打击换成了那张防御；
            // 另一张打击按用户要求换成「铃铛的诅咒」（诅咒不能打出 → 意图只剩两张防御）
            Turn("防御、防御；铃铛的诅咒（打不出去）", 10,
                C<CurseOfTheBell>(), C<DefendSilent>(), C<DefendSilent>()),

            Turn("生存者（防御被它丢弃）、隐秘匕首（2 刀）", 11,
                C<Survivor>(), C<DefendSilent>(discardInstead: true), C<HiddenDaggers>()),
        };

        return new BossScript
        {
            Turns = turns,
            LoopFrom = 7,     // 7～11 回合循环
            LoopLength = 5,
        };
    }

    /// <summary>第三幕的卡组（剧本实际造牌，这里用于展示与自检）。</summary>
    public static List<Type> Deck() => new()
    {
        typeof(BladeDance), typeof(Afterimage), typeof(PiercingWail), typeof(PhantomBlades),
        typeof(FanOfKnives), typeof(StrikeSilent), typeof(StrikeSilent),
        typeof(DefendSilent), typeof(DefendSilent), typeof(DefendSilent),
        typeof(DefendSilent), typeof(DefendSilent),
        typeof(Neutralize), typeof(Survivor), typeof(CloakAndDagger),
        typeof(LeadingStrike), typeof(HiddenDaggers), typeof(Finisher), typeof(Slice),
        // 召唤铃铛带来的诅咒（用户设计：卡组里有一张打击被换成它）
        typeof(CurseOfTheBell),
    };

    /// <summary>遗物（用户给的 9 个，顺序照抄）。</summary>
    public static List<Type> Relics() => new()
    {
        typeof(RingOfTheSnake),   // 蛇之戒指：第 1 回合多抽（对小刀流＝更早铺刀）
        typeof(Relics.NeowBlessing),   // 涅奥的祝福：所有 Boss 都是第 2 件（用户 2026-10-27 口径）
        typeof(Shuriken),         // 手里剑：每 3 张攻击 → +1 力
        typeof(CallingBell),      // 召唤铃铛
        typeof(Lantern),          // 灯笼：第 1 回合 +1 费
        typeof(OrnamentalFan),    // 精致折扇：每 3 张攻击 → +4 格挡
        typeof(Kunai),            // 苦无：每 3 张攻击 → +1 敏
        typeof(HelicalDart),      // 螺线飞镖
        // 用户 2026-10-25 削弱：**钗 → 钻石头冠**（`DiamondDiadem`）。
        // 用户 2026-10-27：卡面是「战斗开始时」，所以**一进战斗**就给 20 格挡 + **2 层残影**
        // （她回合开始会掉 1 层残影，所以给 2 层）；实现在 BossHookBridge.GrantDiamondDiademAtCombatStart
        // （影子 Player 的遗物收不到原版广播，见那一段的说明）。
        typeof(DiamondDiadem),    // 钻石头冠
        typeof(TuningFork),       // 音叉（用户 2026-10-24 削刀猎：纸鹤 → 音叉）：每 3 张技能牌→格挡
    };

    private static BossTurn Turn(string note, int turnIndex, params ScriptCard[] cards)
        => Turn(note, turnIndex, null, cards);

    /// <summary>带能量覆盖的回合（<paramref name="energy"/> 为空 = 用默认的 2 费）。</summary>
    private static BossTurn Turn(string note, int turnIndex, int? energy, params ScriptCard[] cards)
        => new() { Note = $"{turnIndex}. {note}", Cards = new List<ScriptCard>(cards), Energy = energy ?? TurnEnergy };

    private static ScriptCard C<T>(bool upgraded = false, string from = "deck", bool discardInstead = false)
        where T : MegaCrit.Sts2.Core.Models.CardModel
        => new() { Card = typeof(T), Upgraded = upgraded, From = from, DiscardInstead = discardInstead };
}
