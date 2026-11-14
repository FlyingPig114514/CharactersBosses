using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Relics;
using CharacterBosses.Relics;

namespace CharacterBosses.Core;

/// <summary>
/// 三层「**巨石战**」第二版（用户 2026-10-28 重做/削弱）——取代旧版
/// （旧版见 <c>docs/plans/boss-ironclad-boulder.md</c>，第二版见 <c>...-boulder-v2.md</c>）。
///
/// 特殊效果「原始力量」（`Powers/BossPrimalForcePower.cs`）：
///   · 用巨石 → 自己 +1 力量、+3 覆甲；
///   · 巨石的**未格挡伤害**打到玩家 → 玩家抽 1 张牌。
///
/// 剧本 8 回合，之后 **6～8 循环**（`LoopFrom = 5`）。
/// 第 1 回合打出腐化+ ⇒ 之后所有技能牌 0 费且带消耗（能量账由规划器按真实费用算）。
/// 第 4 回合（用户 2026-10-29 定稿）：**打击** ＋ **坚毅+**（格挡 9、消耗 1 张 —— 剧本点名那张「防御」）。
/// 经过两轮改动：原来的「彼岸咆哮」→ 余烬 → 最终整张换成**打击**（卡组里也多了一张打击）。
/// </summary>
public static class BoulderIroncladScript
{
    private const int Energy = 3;

    public static BossScript Build()
    {
        var turns = new List<BossTurn>
        {
            new()
            {
                // 2026-11-13（恐惧模式）第 1 回合：执迷 -1 费 ⇒ 多 1 点能量，能多打 1 张巨石+
                Note = Core.DreadMode.IsActive ? "1. 执迷（恐惧模式 -1 费）、祭品+（王室认证）、腐化+、武装+、原始力量+（变化成打击×2/痛击）、巨石+×3（执迷省出的 1 点 ⇒ 打 2 张，剩 1 张没费）" : "1. 执迷、祭品+（王室认证）、腐化+（此后技能牌 0 费且消耗）、武装+、原始力量+（变化成打击×2/痛击）、巨石+×3（打 1 张，另两张没费不出；祭品+ 的 +2 能量已计入）",
                // ⚠ 这一回合的能量写死 4：加祭品 +2 = 6 点可支配；
                //   账：执迷 2 + 腐化 2（升级后）+ 武装 1 + 原始力量 0 + 祭品 0 = 5 ⇒ 只剩 1 点，正好打 1 张巨石（1 费）。
                Energy = Core.DreadMode.IsActive ? 5 : 4,   // 2026-11-13 dread: 执迷 -1 费省出 1 点 ⇒ 多打 1 张巨石+
                Cards = Cards(
                    C<Enthralled>(), C<Offering>(true), C<Corruption>(true), C<Armaments>(true),
                    C<PrimalForce>(true), C<GiantRock>(true), C<GiantRock>(true), C<GiantRock>(true)),
            },
            new()
            {
                Note = "2. 头槌（捞巨石+）、无惧疼痛、火焰屏障",
                Energy = Energy,
                Cards = Cards(C<Headbutt>(), C<FeelNoPain>(), C<FlameBarrier>()),
            },
            new()
            {
                Note = "3. 巨石+、防御、防御",
                // 用户 2026-10-31 定稿：恢复成设计原样（巨石+、防御、防御）。
                // 能量账：巨石+ 1 + 防御 0（腐化+）+ 防御 0（腐化+）= 1 ≤ 3 ✔
                Energy = Energy,
                Cards = Cards(C<GiantRock>(true), C<DefendIronclad>(), C<DefendIronclad>()),
            },
            new()
            {
                Note = "4. 防御、坚毅+（烧彼岸咆哮）、彼岸咆哮（被消耗 → 回合结束时从消耗牌堆自己打出，不花费用）",
                // 用户 2026-10-30：「回退之前对巨石战的削弱，把彼岸咆哮加回来（不回退更多）」
                //   ⇒ 第 4 回合恢复成削弱前那一版：防御 + 坚毅+（点名消耗**彼岸咆哮**），
                //   彼岸咆哮被消耗后在**回合结束时**从消耗牌堆自动打出（`FreePlayExhaustEntry`，
                //   实现见 CharBossMonster 的「回合结束自动打出」段）。
                //   能量账：防御 0（腐化+）+ 坚毅 0（腐化+）= 0 ≤ 3 ✔（彼岸咆哮不花费用）
                Energy = Energy,
                Cards = Cards(C<DefendIronclad>(), C<TrueGrit>(true), Burn<HowlFromBeyond>()),
                FreePlayExhaustEntry = "HOWL_FROM_BEYOND",
            },
            new()
            {
                Note = "5. 铁斩波、打击、防御",
                Energy = Energy,
                Cards = Cards(C<IronWave>(), C<StrikeIronclad>(), C<DefendIronclad>()),
            },
            new()
            {
                Note = "6. 洗牌；巨石+、巨石+、头槌",
                // 用户 2026-10-31 定稿：第二张从「头槌（捞巨石）」改成**第二张巨石+**；
                // 之后又把「铁斩波」换成头槌（与第 8 回合对调）。
                // 能量账：巨石+ 1 + 巨石+ 1 + 头槌 1 = 3 = 每回合 3 费 ✔
                Energy = Energy,
                Cards = Cards(C<GiantRock>(true), C<GiantRock>(true), C<Headbutt>()),
            },
            new()
            {
                Note = Core.DreadMode.IsActive
                    ? "7. 执迷（恐惧模式 -1 费）、巨石+、巨石+（两张都打得出去）"
                    : "7. 执迷、巨石+、巨石+（第二张没费）",
                Energy = Core.DreadMode.IsActive ? Energy + 1 : Energy,   // 2026-11-13 dread
                Cards = Cards(C<Enthralled>(), C<GiantRock>(true), C<GiantRock>(true)),
            },
            new()
            {
                Note = "8. 彼岸咆哮（直接打出，3 费）、打击（没费）、铁斩波（没费）",
                // 用户 2026-10-31 定稿：第 1 张改成**直接打出的彼岸咆哮**（不再是「被坚毅+烧掉再从消耗堆自动打」，
                //   也不再是之前占位的打击）；后两张按设计**没费出不了**（3 费全给彼岸咆哮了）。
                // 能量账：彼岸咆哮 3 = 3 ⇒ 打击 1、铁斩波 1 都打不出 ✔（脚本会自动跳过付不起的牌）
                Energy = Energy,
                Cards = Cards(C<HowlFromBeyond>(), C<StrikeIronclad>(), C<IronWave>()),
            },
        };

        return new BossScript
        {
            Turns = turns,
            LoopFrom = 5,      // 用户 2026-10-28：之后 6～8 循环
            LoopLength = 3,
        };
    }

    /// <summary>卡组（用户清单逐条）。</summary>
    public static List<Type> Deck() => new()
    {
        typeof(StrikeIronclad), typeof(StrikeIronclad), typeof(StrikeIronclad),   // 打击 ×3
        typeof(DefendIronclad), typeof(DefendIronclad),
        typeof(DefendIronclad), typeof(DefendIronclad),                            // 防御 ×4
        typeof(Corruption),        // 腐化（剧本里是 +）
        typeof(FeelNoPain),        // 无惧疼痛
        typeof(PrimalForce),       // 原始力量
        typeof(Offering),          // 祭品（剧本里是 +，附魔王室认证 RoyallyApproved）
        typeof(Armaments),         // 武装（剧本里是 +）
        typeof(FlameBarrier),       // 火焰屏障
        typeof(TrueGrit),          // 坚毅（本身不升级，进阶 8 才升）
        typeof(Bash),              // 痛击
        typeof(Headbutt),          // 头槌
        typeof(IronWave),          // 铁斩波
        typeof(Enthralled),        // 执迷
        typeof(HowlFromBeyond),    // 彼岸咆哮（用户 2026-10-30：加回来）
    };

    /// <summary>遗物（用户清单）：燃烧之血 / 涅奥的祝福 / 护喉甲 / 尘封魔典 / 锚 / 血染玫瑰 / 永冻冰晶 / 王室印章。</summary>
    public static List<Type> Relics() => new()
    {
        typeof(BurningBlood),        // 燃烧之血（起始）
        typeof(NeowBlessing),        // 涅奥的祝福（第 2 件）
        typeof(Gorget),              // 护喉甲
        typeof(DustyTome),           // 尘封魔典
        typeof(Anchor),              // 锚
        typeof(BloodSoakedRose),     // 血染玫瑰
        typeof(Permafrost),          // 永冻冰晶
        typeof(RoyalStamp),          // 王室印章（给一张牌附「王室认证」）
    };

    // ── 小工具 ──────────────────────────────────────────────

    private static List<ScriptCard> Cards(params ScriptCard[] cards) => new(cards);

    private static ScriptCard C<T>(bool upgraded = false) where T : CardModel
        => new() { Card = typeof(T), Upgraded = upgraded, From = "deck" };

    /// <summary>
    /// 这张牌不主动打，而是被本回合的消耗效果吃掉（第 4 回合是「坚毅+」的指定消耗，
    /// 它会优先按点名名单挑中它）。
    /// </summary>
    private static ScriptCard Burn<T>() where T : CardModel
        => new() { Card = typeof(T), From = "deck", Burned = true };
}
