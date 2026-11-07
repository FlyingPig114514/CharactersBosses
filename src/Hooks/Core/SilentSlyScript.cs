using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Relics;
using CharacterBosses.Relics;

namespace CharacterBosses.Core;

/// <summary>
/// 第一幕「**奇巧猎**」（静默猎手 · 奇巧 / 弃牌轴）—— 用户 2026-10-26 晚的设计稿。
/// 完整设计稿与「名字 → 游戏对象」对照见 <c>docs/plans/boss-silent-sly.md</c>。
///
/// 要点：
///   · 每回合 **2 费**（涅奥的祝福 −1：基础 3）—— 设计稿里每一回合的「没费」都是按 2 费写的；
///   · 剧本把**要丢弃的牌也写死**（`DiscardInstead`）：它们不进计划，靠「奇巧」在丢弃后免费打出；
///   · 第 2 回合单人/多人不一样：单人丢「翻越撑击 + 防御」后打余像 + 打击（末尾多一张没费的防御），
///     多人把「余像」换成「鬼祟+」、连同翻越撑击一起丢掉（都靠奇巧免费打出），然后打打击 + 防御；
///   · **进阶 9+**：「翻越撑击」与「连续反弹」升级（`UpgradeMinAscension = 9`）；
///   · 特殊效果「命运同担」（<c>Powers/SharedFatePower</c>）：她每回合第一次弃牌 →
///     所有玩家下回合开始时各弃 1 张手牌。
///
/// ⚠ 「速行者」的群伤（每抽 1 张牌对所有玩家 2 点）**不是剧本里的牌**，而是原版
/// <c>SpeedsterPower.AfterCardDrawn</c> 自己结算的；我们只在意图里把它补出来
/// （见 <c>BossIntentBuilder.SpeedsterIntents</c>），绝不重复造成伤害。
/// </summary>
public static class SilentSlyScript
{
    /// <summary>每回合固定 2 费（角色基础 3 − 涅奥的祝福 1）。</summary>
    private const int TurnEnergy = 2;

    public static BossScript Build()
    {
        var turns = new List<BossTurn>
        {
            // 1（2 费全部给速行者+）⇒ 打击×2 / 防御×2 都没费
            Turn("速行者+（2 费）；打击×2、防御×2 没费", 1,
                C<Speedster>(upgraded: true),
                C<StrikeSilent>(), C<StrikeSilent>(),
                C<DefendSilent>(), C<DefendSilent>()),

            // 2：早有准备+（0 费，升级后丢 2 张）分单人/多人：
            //   单人 = 丢翻越撑击与防御，然后打余像(1) + 打击(1) = 2 费；再一张防御没费
            //   多人 = 鬼祟+（余像换来的）与翻越撑击都被丢（都靠奇巧免费打出），然后打打击(1) + 防御(1) = 2 费
            //   ⚠ 用户 2026-10-31 第二次改稿：**翻越撑击排在余像/鬼祟之前**（回到 2026-10-29 那次订正），
            //     单人末尾多一张「防御（没费）」。
            Turn("单人：早有准备+（丢翻越撑击与防御）、翻越撑击（奇巧）、余像、打击、防御（没费）；" +
                 "多人：早有准备+（丢鬼祟+与翻越撑击）、鬼祟+（奇巧）、翻越撑击、打击、防御", 2,
                C<Prepared>(upgraded: true),
                C<FlickFlack>(discardInstead: true, costOverride: 0, upgradeMinAscension: 9),   // 奇巧：被丢弃后自动打出
                C<Afterimage>(discardInsteadWhenMultiplayer: true),
                C<StrikeSilent>(),
                C<DefendSilent>(discardInsteadSingleOnly: true),   // 单人：被早有准备丢掉；多人：这一张打出去
                C<DefendSilent>()),                                 // 单人里没费的那张（多人里多显示一张，不影响费用）

            // 3：杂技(1) + 生存者(1) = 2 ⇒ 连续反弹 / 触不可及都靠奇巧免费打出；打击没费，笨拙 0 费
            Turn("杂技（丢连续反弹）、连续反弹（奇巧）、生存者（丢触不可及）、触不可及（奇巧）、" +
                 "打击（没费）、笨拙（0 费诅咒）", 3,
                C<Acrobatics>(),
                C<Ricochet>(discardInstead: true, costOverride: 0, upgradeMinAscension: 9),
                C<Survivor>(),
                C<Untouchable>(discardInstead: true, costOverride: 0),
                C<StrikeSilent>(),
                C<Clumsy>()),

            // 4：谋划专家+（升级后 1 费）+ 尖啸(1) = 2 ⇒ 愧疚（0 费诅咒）不占费用
            //    ⚠ 用户 2026-10-31：「匕首雨换愧疚（诅咒）」（原来那格是匕首雨 / 更早是打击）
            Turn("谋划专家+（1 费）、尖啸（全敌 -6 力量，消耗）；愧疚（0 费诅咒）", 4,
                C<MasterPlanner>(upgraded: true), C<PiercingWail>(), C<Guilty>()),

            // 5：后空翻(1) + 防御(1) = 2；早有准备+ 丢「打击 + 防御」；
            //    这一回合手牌已经空了 ⇒ 隐秘匕首没有牌可弃（用户 2026-10-31 明确）
            Turn("后空翻、早有准备+（弃打击、防御）、防御、中和、隐秘匕首（此时手牌是空的，不弃牌）", 5,
                C<Backflip>(),
                C<Prepared>(upgraded: true),
                C<StrikeSilent>(discardInstead: true),
                C<DefendSilent>(discardInstead: true),
                C<DefendSilent>(),
                C<Neutralize>(),
                C<HiddenDaggers>()),

            // 6：洗牌。早有准备+ 丢翻越撑击与触不可及（两张奇巧）+ 防御(1) + 打击(1)
            Turn("洗牌。早有准备+（丢翻越撑击、触不可及）、翻越撑击（奇巧）、触不可及（奇巧）、防御、打击", 6,
                C<Prepared>(upgraded: true),
                C<FlickFlack>(discardInstead: true, costOverride: 0, upgradeMinAscension: 9),
                C<Untouchable>(discardInstead: true, costOverride: 0),
                C<DefendSilent>(), C<StrikeSilent>()),

            // 7：杂技(1) 丢后空翻（后空翻靠奇巧免费打出）；早有准备+ 丢打击×2；
            //    防御(1) + 中和(0) + 隐秘匕首(0，再丢打击×2)
            Turn("杂技（弃后空翻）、后空翻（奇巧）、早有准备+（弃打击×2）、防御、中和、隐秘匕首（弃打击×2）", 7,
                C<Acrobatics>(),
                C<Backflip>(discardInstead: true, costOverride: 0, sly: true),
                C<Prepared>(upgraded: true),
                C<StrikeSilent>(discardInstead: true),
                C<StrikeSilent>(discardInstead: true),
                C<DefendSilent>(),
                C<Neutralize>(),
                C<HiddenDaggers>(),
                C<StrikeSilent>(discardInstead: true),
                C<StrikeSilent>(discardInstead: true)),

            // 8：防御×2（1 + 1 = 2 费，两张都打得出去）
            //    ⚠ 用户 2026-10-31：「第八回合打 2 张防御」（原来第一格是匕首雨 / 更早是打击）
            Turn("防御、防御", 8,
                C<DefendSilent>(), C<DefendSilent>()),

            // 9：防御(1) + 生存者(1) = 2，生存者丢连续反弹（奇巧免费打出）
            Turn("防御、生存者（丢连续反弹）、连续反弹（奇巧）", 9,
                C<DefendSilent>(), C<Survivor>(),
                C<Ricochet>(discardInstead: true, costOverride: 0, upgradeMinAscension: 9)),
        };

        return new BossScript
        {
            Turns = turns,
            LoopFrom = 6,     // 6～9 循环（用户：「之后 6～9 循环」）
            LoopLength = 4,
        };
    }

    /// <summary>卡组（用户清单）。「余像 / 鬼祟+」是同一格：单人是余像，多人由多人换牌换成鬼祟。</summary>
    public static List<Type> Deck() => new()
    {
        typeof(Speedster), typeof(ToolsOfTheTrade), typeof(MasterPlanner), typeof(Afterimage),
        typeof(DefendSilent), typeof(DefendSilent), typeof(DefendSilent),
        typeof(DefendSilent), typeof(DefendSilent),
        typeof(StrikeSilent), typeof(StrikeSilent), typeof(StrikeSilent),
        typeof(Neutralize), typeof(Survivor), typeof(Untouchable), typeof(FlickFlack),
        typeof(Deflect), typeof(Acrobatics), typeof(Prepared), typeof(Prepared),
        typeof(Backflip), typeof(MementoMori), typeof(Ricochet), typeof(Tactician),
        typeof(Reflex), typeof(HiddenDaggers), typeof(PiercingWail),   // 用户 2026-10-26：暴露 → 尖啸
    };

    /// <summary>遗物（用户清单）。涅奥的祝福本来就会自动带，这里写出来是为了状态栏顺序。</summary>
    public static List<Type> Relics() => new()
    {
        typeof(RingOfTheSnake),   // 蛇之戒指：战斗开始多抽
        typeof(NeowBlessing),     // 涅奥的祝福（已在身上时跳过，不会重复）
        typeof(Shuriken),         // 手里剑：每回合每 3 张攻击 → +1 力量
        typeof(WarPaint),         // 战纹涂料：拾起时随机升级技能牌（战斗内无数值影响）
    };

    // ── 小工具 ────────────────────────────────────────────────────────────────
    private static BossTurn Turn(string note, int turnIndex, params ScriptCard[] cards)
        => new() { Note = $"{turnIndex}. {note}", Cards = new List<ScriptCard>(cards), Energy = TurnEnergy };

    /// <summary>
    /// 剧本里的一张牌。
    /// <paramref name="discardInstead"/> = 这张不主动打、被上一张牌丢掉（丢掉后靠奇巧自动打出）；
    /// <paramref name="discardInsteadWhenMultiplayer"/> / <paramref name="discardInsteadSingleOnly"/>
    /// = 只在多人 / 只在单人时才是「被丢掉的那张」；
    /// <paramref name="costOverride"/> = 规划用的费用（奇巧牌被丢后是免费打出的，写 0 免得按卡面判成打不起）；
    /// <paramref name="sly"/> = 造牌时直接给它「奇巧」（后空翻本来没有，是谋划专家给的）；
    /// <paramref name="upgradeMinAscension"/> = 进阶到多少级起这张是**升级版**
    /// （用户 2026-10-31：进阶 9 起「翻越撑击」和「连续反弹」升级）。
    /// </summary>
    private static ScriptCard C<T>(bool upgraded = false, bool discardInstead = false,
                                   bool discardInsteadWhenMultiplayer = false,
                                   bool discardInsteadSingleOnly = false,
                                   int costOverride = -1, bool sly = false,
                                   int upgradeMinAscension = 0)
        where T : CardModel
        => new()
        {
            Card = typeof(T), Upgraded = upgraded, From = "deck",
            DiscardInstead = discardInstead,
            DiscardInsteadWhenMultiplayer = discardInsteadWhenMultiplayer,
            DiscardInsteadSingleOnly = discardInsteadSingleOnly,
            CostOverride = costOverride,
            Sly = sly,
            UpgradeMinAscension = upgradeMinAscension,
        };
}
