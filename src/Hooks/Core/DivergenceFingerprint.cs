using System;
using System.Collections.Generic;
using System.Text;
using MegaCrit.Sts2.Core.Entities.Players;

namespace CharacterBosses.Core;

/// <summary>
/// **牌堆「顺序指纹」**（v0.2.878，用户 2026-11-22：两端 HP/格挡/power/能量/计划逐字相同却仍报不同步）。
///
/// 为什么需要：不同步摘要一直只打**张数**（`抽=15/弃=0`）。张数一样、里面是哪 15 张、什么顺序分叉，
/// 在日志里完全看不见 ⇒ 两份日志一比「全都一样」，问题就永远查不出来。
/// 这里给每个牌堆算一行可读指纹：`首牌/尾牌#短哈希`，哈希按**顺序**滚一遍（顺序变了哈希就变）。
///
/// 输出形如：`抽:STRIKE/DEFEND#3F2A 弃:-/-#811C 耗:DAZED/DAZED#0000 手:STRIKE#7B1D`
/// 两端这一行不同 ⇒ 牌堆分叉；相同 ⇒ 分叉不在这里（去看 RNG / 意图 / 遗物计数）。
/// </summary>
internal static class DivergenceFingerprint
{
    internal static string OfPiles(PlayerCombatState? pcs)
    {
        if (pcs == null) return "<无影子玩家>";
        var sb = new StringBuilder();
        try
        {
            Append(sb, "抽", pcs.DrawPile?.Cards);
            Append(sb, "弃", pcs.DiscardPile?.Cards);
            Append(sb, "耗", pcs.ExhaustPile?.Cards);
            Append(sb, "手", pcs.Hand?.Cards);
        }
        catch (Exception e) { return "<取牌堆失败:" + e.GetType().Name + ">"; }
        return sb.ToString().TrimEnd();
    }

    private static void Append(StringBuilder sb, string tag, IEnumerable<MegaCrit.Sts2.Core.Models.CardModel>? cards)
    {
        if (cards == null) return;
        var entries = new List<string>();
        try { foreach (var c in cards) entries.Add(SafeEntry(c)); } catch { /* 忽略 */ }

        string first = entries.Count > 0 ? entries[0] : "-";
        string last = entries.Count > 0 ? entries[entries.Count - 1] : "-";
        uint hash = 2166136261u;                       // FNV-1a，按顺序滚
        foreach (var e in entries)
        {
            foreach (char ch in e) { hash ^= ch; hash *= 16777619u; }
            hash ^= '/'; hash *= 16777619u;
        }
        sb.Append(tag).Append(':').Append(first).Append('/').Append(last)
          .Append('#').Append((hash & 0xFFFF).ToString("X4")).Append(' ');
    }

    private static string SafeEntry(MegaCrit.Sts2.Core.Models.CardModel? card)
    {
        try { return card?.Id.Entry ?? "?"; } catch { return "?"; }
    }
    /// <summary>
    /// **玩家侧**的牌堆指纹（v0.2.880）。Boss 侧一致但玩家牌堆分叉（抽牌顺序、手牌内容）时，
    /// 我们原来的快照**根本不打玩家牌堆** ⇒ 两端看着一样其实已经不同。
    /// </summary>
    internal static string OfPlayer(Player? player)
    {
        if (player == null) return "<无玩家>";
        try
        {
            var pcs = player.PlayerCombatState;
            if (pcs == null) return "<无战斗状态>";
            return "手/抽/弃/耗=" + OfPiles(pcs).Trim() + $" 能量={pcs.Energy}/星={pcs.Stars}";
        }
        catch (Exception e) { return "<取玩家牌堆失败:" + e.GetType().Name + ">"; }
    }
}