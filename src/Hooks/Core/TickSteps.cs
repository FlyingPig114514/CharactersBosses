using System;

namespace CharacterBosses.Core;

/// <summary>
/// **帧回调的「步骤路标」**（用户 2026-11-26：「如果没什么影响，做吧」）。
///
/// 背景：玩家卡死时我们只有「日志停在哪一行」这一条线索，而主线程一旦卡住就再也写不出日志，
/// 于是谁也说不清到底是**我们这帧的某个子步骤**卡住了，还是**帧回调之外**（原版或别的 mod）卡住了。
///
/// 做法：<c>BossFrameTick.Tick</c> / <c>TickInner</c> 在进入每个子步骤时打一个「路标」，
/// 路标只是**两个静态字段的赋值**（一个常量字符串 + 一个 TickCount64），零分配、零 IO；
/// 真正落盘的是后台线程的 <see cref="StallWatchdog"/>：它发现心跳停了 12 秒以上时，
/// 把「最后进入的步骤 + 距今多少毫秒」写进停摆报告。
///
/// 于是下次卡死看一眼报告就能分开两种情况：
///   · 报「Tick 结束 于 xxx ms 前」 ⇒ **我们的帧回调早就跑完了**，卡在帧回调之外（原版 / 别的 mod）；
///   · 报「SweepRoom 于 xxx ms 前」/「TickInner 是 xxx ms 前」 ⇒ 卡在我们某个具体子步骤里。
///
/// ⚠ 纯诊断：不改任何游戏状态，也不做任何 IO。
/// </summary>
internal static class TickSteps
{
    /// <summary>最后进入的步骤名（常量字符串，不分配）。</summary>
    internal static volatile string Last = string.Empty;

    private static long _lastTicks;

    /// <summary>每帧调十来次：两个赋值，别在这里做任何格式化 / IO。</summary>
    public static void Enter(string step)
    {
        Last = step;
        _lastTicks = Environment.TickCount64;
    }

    /// <summary>给报告用：「SweepRoom 于 137ms 前」。</summary>
    public static string Describe()
    {
        try
        {
            string last = Last;
            if (last.Length == 0) return "（帧回调还没跑过）";
            long age = Environment.TickCount64 - _lastTicks;
            return $"{last}（{age} ms 前）";
        }
        catch { return "?"; }
    }

    /// <summary>最后路标距今多久（毫秒；路标还没打过返回 -1）。</summary>
    public static long AgeMs()
    {
        try { return Last.Length == 0 ? -1 : Environment.TickCount64 - _lastTicks; }
        catch { return -1; }
    }
}
