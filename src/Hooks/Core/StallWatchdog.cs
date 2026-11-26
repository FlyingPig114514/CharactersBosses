using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace CharacterBosses.Core;

/// <summary>
/// **主线程停摆看门狗**（用户 2026-11-26：「你试试加诊断补丁」）。
///
/// 背景：玩家用观者 mod 时卡死两次，Windows 事件日志是 `AppHangB1`（不是崩溃），
/// 而卡死那一刻**所有 mod 的日志与我们的帧探针同时停止** ⇒ 主线程被卡住，正常的主线程日志
/// 再也写不出来。所以需要一个**后台线程**在「主线程不说话」时把现场落盘：
///
///   · <see cref="Beat"/> 由 <c>BossFrameTick.Tick</c> 每帧调一次，只写一个 <c>long</c>（零分配）；
///   · 后台线程每秒看一眼：心跳停了多久；
///   · 停满 <see cref="StallMs"/> 且没报告过 ⇒ 往
///     `%APPDATA%\SlayTheSpire2\logs\characterbosses_stall.txt` 追加一份报告
///     （`FileOptions.WriteThrough`，进程随后被强杀也留得住）；
///   · 心跳恢复（&lt; <see cref="RecoveredMs"/>）后自动复位，下一次卡死会再写一份。
///
/// 报告里包含：停了多久 / 累计帧数 / 线程数 / GC 堆 / 最近 40 条 <see cref="Mark"/>，
/// 以及**最后一次手牌重复插入的完整调用栈**（<c>Patches.HandDuplicateDiagPatch.LastRecord</c>）——
/// 那条重复插入正好是两次卡死前最后一条日志，把它点名出来就能定位是谁在重复往手里塞牌。
///
/// ⚠ 纯诊断：不改任何游戏行为，也不碰原版返回值。
/// </summary>
internal static class StallWatchdog
{
    /// <summary>心跳停多久算「卡死」（战斗转场 / 读档本来就会有 1~3 秒的安静期）。</summary>
    private const int StallMs = 12000;

    /// <summary>心跳恢复到这个阈值以下就把「已报告」标记清掉（下一次卡死再写一份）。</summary>
    private const int RecoveredMs = 3000;

    /// <summary>最多留多少条 Mark（够看清卡死前最后几步）。</summary>
    private const int MarkCapacity = 40;

    private static readonly object _lock = new();
    private static readonly Queue<string> _marks = new();

    private static long _lastBeat = Environment.TickCount64;
    private static long _beats;
    private static volatile bool _threadStarted;
    private static volatile bool _reported;
    private static volatile bool _hooked;

    /// <summary>
    /// 把心跳挂到 <c>SceneTree.process_frame</c> 上。
    ///
    /// ★ 2026-11-26 第二次修（实测踩到）：一开始只让 <c>BossFrameTick.Tick</c> 调 <see cref="Beat"/>，
    ///   可那个 Tick 是靠「场上存在带星/带能量球/带刀的节点」才被调到的 —— 主菜单、读档、奖励界面
    ///   根本没这些东西 ⇒ 心跳自然停，于是**开局在菜单挂机两分钟就被误报成「主线程停摆」**
    ///   （实机日志里 20:30:35 / 20:36:09 两条假报告）。现在改挂 `SceneTree.process_frame`：
    ///   从启动到退出每帧都会响，与战斗无关。挂不上（太早、还没有 SceneTree）就下次再试。
    /// </summary>
    public static void EnsureHooked()
    {
        if (_hooked) return;
        try
        {
            if (Godot.Engine.GetMainLoop() is not Godot.SceneTree tree) return;
            tree.ProcessFrame += Beat;
            _hooked = true;
            Mark("心跳已挂到 SceneTree.process_frame（每帧一次，与战斗无关）");
        }
        catch { /* 忽略 */ }
    }

    /// <summary>最近一次「手牌重复插入」的记录（由 <c>Patches.HandDuplicateDiagPatch</c> 填）。</summary>
    internal static string LastHandDuplicate = string.Empty;

    private static string _reportPath;

    /// <summary>报告文件（懒算，避免静态初始化期碰 IO）。</summary>
    private static string ReportPath
    {
        get
        {
            if (_reportPath != null) return _reportPath;
            try
            {
                _reportPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "SlayTheSpire2", "logs", "characterbosses_stall.txt");
            }
            catch { _reportPath = string.Empty; }
            return _reportPath;
        }
    }

    /// <summary>每帧一次（只写一个 long；第一次调用时把后台线程拉起来）。</summary>
    public static void Beat()
    {
        if (!_hooked) EnsureHooked();
        _lastBeat = Environment.TickCount64;
        _beats++;
        // ⚠ 只有「心跳挂在 process_frame 上」时才起后台线程：
        //   否则心跳不可靠（Tick 只在战斗里有节点时被调到），会把菜单挂机误报成停摆。
        if (_threadStarted || !_hooked) return;
        StartThread();
    }

    /// <summary>记一步「卡死前最后在干什么」（最近 <see cref="MarkCapacity"/> 条会进停摆报告）。</summary>
    public static void Mark(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        try
        {
            lock (_lock)
            {
                _marks.Enqueue($"[{DateTime.Now:HH:mm:ss.fff}] {text}");
                while (_marks.Count > MarkCapacity) _marks.Dequeue();
            }
        }
        catch { /* 忽略 */ }
    }

    private static void StartThread()
    {
        _threadStarted = true;
        try
        {
            var t = new Thread(Loop)
            {
                IsBackground = true,
                Name = "CharBoss-StallWatch",
                Priority = ThreadPriority.BelowNormal,
            };
            t.Start();
        }
        catch { _threadStarted = false; }
    }

    private static void Loop()
    {
        while (true)
        {
            try { Thread.Sleep(1000); } catch { /* 忽略 */ }

            long age;
            try { age = Environment.TickCount64 - _lastBeat; } catch { age = 0; }

            if (age < RecoveredMs)
            {
                // 心跳回来了 ⇒ 允许下一次卡死再报告
                _reported = false;
                continue;
            }
            if (age < StallMs || _reported) continue;

            _reported = true;
            try { WriteReport(age); } catch { /* 忽略 */ }
        }
    }

    private static void WriteReport(long ageMs)
    {
        string path = ReportPath;
        if (string.IsNullOrEmpty(path)) return;

        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine("========================================");
        sb.AppendLine($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 主线程停摆报告（pid={SafePid()}）");
        sb.AppendLine($"  · 心跳已停 **{ageMs / 1000.0:0.0} 秒**（累计心跳 {Interlocked.Read(ref _beats)} 帧）");
        sb.AppendLine($"  · 线程数 {SafeThreads()} ｜ GC 堆 {SafeHeapMb():0.0} MB ｜ 进程已跑 {SafeUptime()}");
        // ★ v0.2.968：帧回调的「步骤路标」—— 主线程卡住时只有后台线程还能写字，
        //   这一行直接告诉我们最后进入的是哪个子步骤（或「Tick 结束」= 卡在帧回调之外）。
        sb.AppendLine($"  · 帧回调最后一步：{Core.TickSteps.Describe()}");

        try
        {
            lock (_lock)
            {
                sb.AppendLine($"  · 最近的步骤（{_marks.Count} 条，旧的在前）：");
                foreach (var m in _marks) sb.AppendLine("      " + m);
            }
        }
        catch { /* 忽略 */ }

        string dup = LastHandDuplicate;
        if (!string.IsNullOrEmpty(dup))
        {
            sb.AppendLine("  · 最后一次「手牌重复插入」的记录 + 调用栈：");
            foreach (var line in dup.Split('\n')) sb.AppendLine("      " + line.TrimEnd('\r'));
        }
        else
        {
            sb.AppendLine("  · 本次运行还没命中过「手牌重复插入」。");
        }
        sb.AppendLine("  提示：卡住期间后台线程仍在跑，所以这份报告能落盘；把整份文件发给作者即可。");

        try
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            using var fs = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite,
                                          4096, FileOptions.WriteThrough);
            byte[] bytes = new UTF8Encoding(false).GetBytes(sb.ToString());
            fs.Write(bytes, 0, bytes.Length);
            fs.Flush(true);
        }
        catch { /* 忽略 */ }
    }

    private static int SafePid()
    {
        try { return Environment.ProcessId; } catch { return -1; }
    }

    private static int SafeThreads()
    {
        try { return System.Diagnostics.Process.GetCurrentProcess().Threads.Count; } catch { return -1; }
    }

    private static double SafeHeapMb()
    {
        try { return GC.GetTotalMemory(false) / 1024.0 / 1024.0; } catch { return -1; }
    }

    private static string SafeUptime()
    {
        try
        {
            var t = DateTime.Now - System.Diagnostics.Process.GetCurrentProcess().StartTime;
            return $"{(int)t.TotalHours} 小时 {t.Minutes} 分";
        }
        catch { return "?"; }
    }
}
