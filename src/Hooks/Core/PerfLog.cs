using System;

namespace CharacterBosses.Core;

/// <summary>
/// **性能计数器**（只读计数，不做任何排版/字符串拼接，热路径上加一次 int 自增）。
///
/// 为什么需要（用户 2026-10-26：「boss 战第三/四回合掉到十几帧，这总不能是原版导致的吧？」）：
/// 上一版只统计了「每帧驱动」自己的耗时（avg 0.05ms），而真正重建节点的活全在手牌栏/状态条的
/// <c>Rebuild → Fill → LayoutCells</c> 里，那条路径**从来没有被计时**，于是日志里看起来一切正常。
/// 现在把「谁在重建、重建了几次、重建花了多久、那一秒的 fps」一起打出来，
/// 下一次实机就能直接看出掉帧是不是跟着我们跑。
/// </summary>
internal static class PerfLog
{
    /// <summary>状态条 Fill 次数（本统计窗口内）。</summary>
    internal static int Fills;

    /// <summary>手牌栏 Rebuild（整块重排）次数。</summary>
    internal static int Rebuilds;

    /// <summary>Fill 的累计耗时（毫秒）。</summary>
    internal static double FillMs;

    /// <summary>Rebuild 的累计耗时（毫秒，含 Fill 与排版）。</summary>
    internal static double RebuildMs;

    /// <summary>Fill 遇到「子节点数与期望不一致」的次数（正常情况下必须是 0）。</summary>
    internal static int FillMismatches;

    internal static void NoteFill(bool mismatch, double ms)
    {
        Fills++;
        FillMs += ms;
        if (mismatch) FillMismatches++;
    }

    internal static void NoteRebuild(double ms)
    {
        Rebuilds++;
        RebuildMs += ms;
    }

    /// <summary>取出并清零（每 N 秒由帧驱动打一条）。</summary>
    internal static string Drain(int seconds, int fpsNow, int fpsMin)
    {
        int fills = Fills, rebuilds = Rebuilds, bad = FillMismatches;
        double fillMs = FillMs, rebuildMs = RebuildMs;
        Fills = 0; Rebuilds = 0; FillMismatches = 0; FillMs = 0; RebuildMs = 0;

        double fillAvg = fills > 0 ? fillMs / fills : 0;
        double rebuildAvg = rebuilds > 0 ? rebuildMs / rebuilds : 0;
        return $"[CharBoss] 性能({seconds}s): fps 现={fpsNow} 最低={fpsMin} | 面板重建={rebuilds}次" +
               $"（均 {rebuildAvg:0.00}ms）状态条重建={fills}次（均 {fillAvg:0.00}ms 异常={bad}）";
    }
}
