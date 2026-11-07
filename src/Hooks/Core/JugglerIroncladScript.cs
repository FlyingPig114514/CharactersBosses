using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Relics;
using CharacterBosses.Relics;

namespace CharacterBosses.Core;

/// <summary>
/// 二层「**杂耍战**」（铁甲战士 · 杂耍复制 / 惊逃自动打出轴）—— 用户 2026-11-02 设计稿。
/// 这是二层铁甲战士的**第二套设计**（第一套是「放血战」`BleedIroncladScript`），
/// 与「刀猎/炼药猎」「易伤战/巨石战」「无色储君/辉星储」同一套机制：
/// **以房间遭遇为准**（`IRONCLAD_JUGGLER_BOSS_ENCOUNTER`），拿不到遭遇才按种子 50/50。
///
/// ## 特殊效果「燃烧契约」（`Powers/BossBurningPactPower.cs`）
/// 用户原话：「杂耍生成的攻击牌获得**虚无**和**消耗**」。
/// 实现分两半（见 `Patches/JugglingNoClonePatch` 与 <see cref="ScriptCard.JugglingCopy"/>）：
///   · **拦掉**原版「杂耍」（`JugglingPower.BeforeCardPlayed`：每回合前 3 张攻击牌各复制一张进手牌）；
///   · 改成**剧本提前把复制牌摆进她的手牌**，并且这些复制牌自带「虚无 + 消耗」
///     ⇒ 回合结束前没被打出就消失，不会把手牌越堆越乱。
///
/// ## 「惊逃」（`StampedePower`）由我们手动派发
/// 原版是 `AfterAutoPostPlayPhaseEntered(ctx, player)`：**回合结束时**随机打出手牌里
/// `Amount` 张攻击牌（不收能量）。这条钩子只派发给 `CombatState.Players` 里的真人，
/// 影子玩家收不到 ⇒ 由 <see cref="StampedePlays"/> 在她回合结束时手动调它自己的实现。
/// 剧本里括号写的「（杂耍复制+惊逃打出）」= 那张复制牌**不是她花能量打的**，是惊逃在回合结束打出去的。
///
/// ## 能量（写死在剧本里）
/// 基础 3 − 涅奥的祝福 1 = **2**；
/// · **第 1 回合**：灯笼 `Lantern` +1 ⇒ **3**；
/// · **第 3、6 回合**：开心小花 `HappyFlower`（每 3 回合 +1）⇒ **3**；
/// · 其余回合 **2**。
///
/// ## 剧本 7 回合，之后 **2～7 循环**（用户原文）
/// 1 杂耍+、无惧疼痛、惊逃+ / 2 打击、打击、打击（复制→惊逃打出）/
/// 3 预备打击、打击、踩踏、踩踏（复制→惊逃打出）/ 4 打击、防御、痛击（复制→惊逃打出）/
/// 5 防御、防御、受伤 / 6 完美打击、贪婪、愧疚 / 7 防御、坚毅（烧彼岸咆哮）、彼岸咆哮（回合结束自动打出）
///
/// 完美打击（`PerfectedStrike`）的伤害 = 6 + 2 ×「牌堆里带 Strike 标签的牌数」，
/// 而惊逃每循环都会再打出一张「打击」复制 ⇒ 那张复制会留在弃牌堆里被算进去，
/// 于是**每个循环伤害 +2**（用户原话：「到这里有又增加 1 张打击，之后每个循环增加 1 张打击」）。
/// </summary>
public static class JugglerIroncladScript
{
    /// <summary>常规回合的能量（基础 3 − 涅奥的祝福 1）。</summary>
    private const int TurnEnergy = 2;

    public static BossScript Build()
    {
        var turns = new List<BossTurn>
        {
            // 1（3 费 = 2 + 灯笼：杂耍+ 1 + 无惧疼痛 1 + 惊逃+ 1；惊逃+ 升级后 2→1 费）
            new()
            {
                Note = "1. 杂耍+、无惧疼痛、惊逃+（灯笼 +1 ⇒ 3 费）",
                Energy = 3,
                Cards = Cards(C<Juggling>(true), C<FeelNoPain>(), C<Stampede>(true)),
            },
            // 2（2 费：突破 1 + 打击 1）—— 手里是**3 张打击** + 1 张突破：
            //   突破（对所有敌人 9 伤、自己掉 1 血）与第一张打击花能量打出；
            //   第二张打击不花能量、由「惊逃」在回合结束打出；
            //   第三张是杂耍的复制（带虚无+消耗，回合结束被虚无烧掉 ⇒ 触发无惧疼痛）。
            //   ★ 用户 2026-11-02 削弱：「第二回合改成：突破，打击，打击（惊逃打出），打击（杂耍复制，虚无被烧）」
            //     （原来是「预备打击、打击、打击(惊逃)、打击(复制)」—— 预备打击的 +3 临时力量没了）。
            new()
            {
                Note = "2. 突破、打击、打击（由惊逃打出）、打击（杂耍复制，虚无+消耗 → 无惧疼痛起甲）",
                Energy = TurnEnergy,
                Cards = Cards(C<Breakthrough>(), C<StrikeIronclad>(),
                              StampedePlay<StrikeIronclad>(), Copy<StrikeIronclad>()),
            },
            // 3（3 费 = 2 + 开心小花：打击 ×3）—— 手里**4 张打击**：
            //   前三张花能量打出（1+1+1 = 3），第四张是杂耍的复制、由惊逃在回合结束打出。
            //   ★ 用户 2026-11-02 削弱：「第三回合改成：打击，打击，打击，打击（杂耍复制，惊逃打出）」
            //     （原来是「打击、打击、踩踏、踩踏(复制)」—— 踩踏的 12 点全体伤害换成了 6 点的打击）。
            new()
            {
                Note = "3. 打击、打击、打击、打击（杂耍复制 → 回合结束被惊逃打出后消耗）",
                Energy = 3,
                Cards = Cards(C<StrikeIronclad>(), C<StrikeIronclad>(), C<StrikeIronclad>(),
                              Copy<StrikeIronclad>(playedByStampede: true)),
            },
            // 4（2 费：防御 1 + 防御 1；痛击不花能量、由惊逃在回合结束打出）
            //   ★ 用户 2026-11-02 削弱：「第四回合改成：防御，防御，痛击（惊逃）」（原来的那张打击去掉）
            new()
            {
                Note = "4. 防御、防御、痛击（由惊逃打出）",
                Energy = TurnEnergy,
                Cards = Cards(C<DefendIronclad>(), C<DefendIronclad>(), StampedePlay<Bash>()),
            },
            // 5（2 费：防御、防御；受伤是诅咒，打不出）
            new()
            {
                Note = "5. 防御、防御、受伤（诅咒，打不出）",
                Energy = TurnEnergy,
                Cards = Cards(C<DefendIronclad>(), C<DefendIronclad>(), C<Injury>()),
            },
            // 6（3 费 = 2 + 开心小花：完美打击 2；贪婪/愧疚都是诅咒）
            new()
            {
                Note = "6. 完美打击、贪婪、愧疚（后两张是诅咒；完美打击按牌堆里的 Strike 数成长）",
                Energy = 3,
                Cards = Cards(C<PerfectedStrike>(), C<Greed>(), C<Guilty>()),
            },
            // 7（2 费：坚毅+ 1 + 邪眼 1；坚毅烧掉彼岸咆哮，彼岸咆哮回合结束从消耗牌堆自动打出）
            //   ★ 用户 2026-11-02 削弱：「第七回合改成：坚毅+（烧彼岸咆哮）、邪眼（本回合有消耗牌
            //     则额外获得格挡）、彼岸咆哮（被烧自动打出）」。
            //   邪眼（`EvilEye`）是 1 费 8 格挡，**本回合消耗过卡牌就再 +8** ⇒ 这一回合的坚毅+
            //   先把彼岸咆哮消耗掉，所以邪眼实际给 16 点格挡（⚠ 意图里按卡面只画 8，
            //   因为它是在牌真正打出时才判定的）。
            new()
            {
                Note = "7. 坚毅+（烧彼岸咆哮）、邪眼（本回合有消耗牌 → 16 格挡）、彼岸咆哮（回合结束时自动打出）",
                Energy = TurnEnergy,
                FreePlayExhaustEntry = "HOWL_FROM_BEYOND",
                Cards = Cards(C<TrueGrit>(true), C<EvilEye>(),
                              Burn<HowlFromBeyond>()),
            },
        };

        return new BossScript
        {
            Turns = turns,
            LoopFrom = 2,      // 用户：之后 2～7 循环
            LoopLength = 6,
        };
    }

    /// <summary>卡组（剧本里出现过的牌按最大张数收集）。</summary>
    public static List<Type> Deck() => new()
    {
        typeof(Juggling),           // 杂耍（能力：每回合前 3 张攻击牌各复制一张）
        typeof(FeelNoPain),         // 无惧疼痛（消耗牌 → 格挡）
        typeof(Stampede),           // 惊逃（回合结束自动打出手牌里的攻击牌）
        typeof(TrueGrit),           // 坚毅
        typeof(Breakthrough),       // 突破（1 费：对所有敌人 9 伤、自己掉 1 血；v0.2.321 替换预备打击）
        typeof(EvilEye),            // 邪眼（1 费 8 格挡，本回合消耗过牌则再 +8；v0.2.321 替换踩踏）
        typeof(Bash),               // 痛击
        typeof(PerfectedStrike),    // 完美打击
        typeof(HowlFromBeyond),     // 彼岸咆哮
        // 用户：「卡组中原本有 4 张打击 + 1 张预备打击 + 这一张完美打击」⇒ 打击 4 张
        //（第 3 回合手里要同时有 4 张打击：3 张自己打 + 1 张复制由惊逃打出）
        typeof(StrikeIronclad), typeof(StrikeIronclad),
        typeof(StrikeIronclad), typeof(StrikeIronclad),
        typeof(DefendIronclad), typeof(DefendIronclad),
        typeof(Injury),             // 受伤（诅咒）
        typeof(Greed),              // 贪婪（诅咒）
        typeof(Guilty),             // 愧疚（诅咒）
    };

    /// <summary>
    /// 遗物（用户清单）：燃烧之血、涅奥的祝福、灯笼、佩尔之翼、开心小花、**精致折扇**、手里剑。
    /// 顺序：起始遗物 → 涅奥的祝福（所有 Boss 都是第 2 件）→ 其它。
    ///
    /// ★ 用户 2026-11-02 削弱：「奥利哈钢换折扇」—— 奥利哈钢是「回合结束没格挡 → +6 格挡」，
    ///   精致折扇（`OrnamentalFan`）是「每在同一回合内打出 N 张攻击牌 → +M 格挡」，
    ///   对这套「一回合打一堆打击」的剧本明显更弱（第 2 回合只打 2 张攻击，拿不到格挡）。
    ///   折扇是原版遗物、靠 `AfterCardPlayed` 自己数，影子玩家同样收得到（同手里剑/苦无，
    ///   见 `Core/SilentScript.cs` 的注释）⇒ 不需要额外桥接。
    /// </summary>
    public static List<Type> Relics() => new()
    {
        typeof(BurningBlood),   // 燃烧之血（起始：战斗结束回血）
        typeof(NeowBlessing),   // 涅奥的祝福（第 2 件）
        typeof(Lantern),        // 灯笼：第 1 回合 +1 能量
        typeof(PaelsWing),      // 佩尔之翼
        typeof(HappyFlower),    // 开心小花：每 3 回合 +1 能量
        typeof(OrnamentalFan),  // 精致折扇：每打出 3 张攻击牌 → +4 格挡（v0.2.321 取代奥利哈钢）
        typeof(Shuriken),       // 手里剑：每打出 3 张攻击牌 +1 力量
    };

    // ── 小工具 ──────────────────────────────────────────────

    private static List<ScriptCard> Cards(params ScriptCard[] cards) => new(cards);

    private static ScriptCard C<T>(bool upgraded = false) where T : CardModel
        => new() { Card = typeof(T), Upgraded = upgraded, From = "deck" };

    /// <summary>
    /// 由「**惊逃**」在回合结束打出的那张牌：她不花能量、规划期不当作自己的出牌，
    /// 但**本回合一定会打出去** ⇒ 手牌栏画亮 + 画出攻击意图（用户 2026-11-02 要求）。
    /// </summary>
    private static ScriptCard StampedePlay<T>() where T : CardModel
        => new()
        {
            Card = typeof(T),
            From = "deck",
            StampedePlay = true,
        };

    /// <summary>
    /// 「杂耍的复制」：本回合**留在手里不打出**，带「虚无 + 消耗」（由<see cref="ScriptCard.JugglingCopy"/>
    /// 在造牌时加上关键字），最后被「惊逃」在回合结束时打出去。
    /// </summary>
    /// <param name="playedByStampede">
    /// 这张复制牌**会被「惊逃」在回合结束打出去**（第三回合的踩踏：她三张都打完了，手里只剩这张复制）。
    /// 打上这个标记后手牌栏会给它画亮并画出攻击意图（用户 2026-11-02：
    /// 「第三回合惊逃打出的踩踏没有显示攻击意图」）。没标的（第二回合那张打击复制）只自行消耗。
    /// </param>
    private static ScriptCard Copy<T>(bool playedByStampede = false) where T : CardModel
        => new()
        {
            Card = typeof(T),
            From = "deck",
            // 「会被惊逃打出」的那张**不能**再标 HoldInstead：它要走「0 费计划牌」那条路，
            // 否则会先被 HoldInstead 分支吃掉 ⇒ 既不进 Plan 也没有意图。
            HoldInstead = !playedByStampede,
            JugglingCopy = true,
            StampedePlay = playedByStampede,
        };

    /// <summary>这张牌**不打出**，而是被「坚毅」当成「消耗一张牌」的那一张（彼岸咆哮）。</summary>
    private static ScriptCard Burn<T>() where T : CardModel
        => new() { Card = typeof(T), From = "deck", Burned = true };
}
