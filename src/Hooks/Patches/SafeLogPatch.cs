using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using HarmonyLib;

namespace CharacterBosses.Patches;

/// <summary>
/// **日志落盘永不允许抛异常**（用户 2026-10-24 双开实测）：
/// 玩家用「单人联机 mod」开两个客户端，两个进程**写同一个** `%APPDATA%\SlayTheSpire2\logs\mod_log.txt`，
/// 而 `ModEntry.Logger.Log` 是裸的 `File.AppendAllText(...)`（默认 `FileShare.Read`）——
/// 另一个进程正在写时直接抛 `IOException: ... being used by another process`。
///
/// 后果不是"少一行日志"，而是**调用方被打断**：日志是我们代码里到处都在调的东西，
/// 一旦它在「敌人回合的协程 / 手牌栏刷新 / 意图发布」里抛出，那一段流程就直接死掉 ——
/// 实测表现就是「一边无法结束回合，另一边能结束，而对面那只 Boss 再也不出牌」。
///
/// 修法：给**我们自己**的 `Logger.Log` 挂前缀，用 `FileShare.ReadWrite` 打开 + 失败静默丢弃，
/// 绝不把 IO 异常抛给游戏逻辑（`src/ModEntry.cs` 一行都不用改）。
///
/// ★ v0.2.830（手机卡顿的真正尖峰）：以前是「主线程缓冲 + 每 0.5 秒/每 32 行由帧回调自己 flush」，
///   实测手机日志里每 120 帧（≈2 秒）一行 `帧回调耗时: avg=0.44ms max=20~41ms` ——
///   avg × 120 正好等于「两次 flush 的时间」，也就是说**那两个 20~41ms 的尖峰就是主线程上写 flash**。
///   现在：主线程只往内存队列里塞一行（纯字符串，微秒级），**落盘全交给后台线程**
///   （200ms 醒一次；`⚠ / 失败 / 异常 / 不同步` 这类关键行会立刻叫醒它，最多多等 200ms，
///   崩溃前那几十毫秒的日志仍能保住大部分）。
/// </summary>
[HarmonyPatch(typeof(CharacterBosses.Logger), nameof(CharacterBosses.Logger.Log))]
public static class SafeLogPatch
{
    /// <summary>主线程与后台写手之间交换「待落盘行」的锁（只在内存里倒手，不做任何 IO）。</summary>
    private static readonly object Gate = new();

    /// <summary>
    /// 日志本体路径。**唯一权威**：`RecentLogDumper`（F8 导出）按它读磁盘尾巴，
    /// 不要在两处各算一遍路径（`OS.GetUserDataDir()` 与 `%APPDATA%` 在手机/不同平台上不一定一致）。
    /// </summary>
    internal static string LogPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SlayTheSpire2", "logs", "mod_log.txt");

    private static int _failures;

    // ── 待落盘队列（只由主线程追加、后台线程取走） ─────────────────────────
    private static readonly List<string> Pending = new();
    private const int MaxPendingLines = 20000;      // 兜底上限，写盘长期失败时也不会吃爆内存

    // ── 「最近 N 分钟」环形缓冲（v0.2.900 起，v0.2.901 改按时间） ──────────────
    // 玩家报 bug 时按 **F8** 就把这段导出成 characterbosses_recent.txt。
    // ★ 用户 2026-11-23：「改成大概最近一个小时吧，一局最长大概也就这么久」
    //   ⇒ 按**时间**裁剪（60 分钟），行数只做内存上限兜底。
    //   实测日志密度 ≈ 15 行/秒（`LogLoudness` 里记的：3h40m 写了 203,646 行）
    //   ⇒ 1 小时约 5.4 万行、约 5~8 MB，QQ 群里发完全没问题。
    private const int RecentWindowMinutes = 60;
    private const int RecentHardCap = 90000;        // 内存兜底（≈100 分钟的量），正常碰不到
    private static readonly List<string> Recent = new();
    private static readonly List<DateTime> RecentAt = new();
    private static int _recentSweep;

    /// <summary>把超出时间窗 / 超过内存上限的旧行裁掉（每 64 行做一次，别每行都算）。</summary>
    private static void SweepRecent()
    {
        bool due = ((++_recentSweep & 63) == 0) || Recent.Count > RecentHardCap;
        if (!due) return;

        var cut = DateTime.Now.AddMinutes(-RecentWindowMinutes);
        int drop = 0;
        while (drop < Recent.Count)
        {
            bool tooOld = RecentAt[drop] < cut;
            bool tooMany = (Recent.Count - drop) > RecentHardCap;
            if (!tooOld && !tooMany) break;
            drop++;
        }
        if (drop <= 0) return;

        try
        {
            Recent.RemoveRange(0, drop);
            RecentAt.RemoveRange(0, drop);
        }
        catch { /* 忽略：裁不掉也只是多占点内存 */ }
    }

    /// <summary>取「最近约一小时」的快照（供 <c>RecentLogDumper</c> 一键导出）。</summary>
    internal static string[] SnapshotRecent(out DateTime first, out DateTime last)
    {
        try
        {
            lock (Gate)
            {
                if (Recent.Count == 0) { first = last = DateTime.Now; return Array.Empty<string>(); }
                first = RecentAt[0];
                last = RecentAt[RecentAt.Count - 1];
                return Recent.ToArray();
            }
        }
        catch
        {
            first = last = DateTime.Now;
            return Array.Empty<string>();
        }
    }

    // ── 后台写手（唯一碰文件句柄的线程） ──────────────────────────────────
    private static readonly AutoResetEvent Wake = new(false);
    private static Thread? _worker;
    private static FileStream? _stream;
    private static StreamWriter? _writer;
    private static DateTime _openedAtUtc;
    private static bool _dirEnsured;

    [HarmonyPrefix]
    public static bool Prefix(string message)
    {
        bool urgent = false;
        try
        {
            Write(message ?? string.Empty, out urgent);
        }
        catch
        {
            // 落盘失败就丢弃这条日志：日志绝不能把游戏逻辑打崩（双开/磁盘忙都可能失败）
            _failures++;
        }

        // 本地数据记录（A5，默认关）：把「我们自己打出来的问题行」也记一份。
        // 玩家愿意的话把数据文件发给作者就能定位 bug（只写本机、不上传）。
        try
        {
            if (!string.IsNullOrEmpty(message) && LooksLikeProblem(message))
            {
                CharacterBosses.Core.Telemetry.Diagnostic(message);
            }
        }
        catch { /* 忽略 */ }

        return false;   // 已经自己写过了，跳过原方法
    }

    /// <summary>主线程只做这一件事：把带时间戳的一行塞进内存队列（不做任何 IO）。</summary>
    private static void Write(string message, out bool urgent)
    {
        var line = $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}";
        urgent = LooksLikeProblem(message);

        lock (Gate)
        {
            if (Pending.Count >= MaxPendingLines)
            {
                // 写盘一直失败（磁盘满 / 权限）时丢弃最早的 1/4，留一行说明，绝不涨到吃爆内存
                Pending.RemoveRange(0, MaxPendingLines / 4);
                Pending.Add($"[{DateTime.Now:HH:mm:ss}] [CharBoss] ⚠ 日志缓冲溢出，已丢弃最早 {MaxPendingLines / 4} 行{Environment.NewLine}");
            }
            Pending.Add(line);
            Recent.Add(line);
            RecentAt.Add(DateTime.Now);
            SweepRecent();
        }

        EnsureWorker();

        // 关键行（⚠ / 失败 / 异常 / 不同步）立刻叫醒写手；普通行就等它 200ms 那一趟。
        if (urgent)
        {
            try { Wake.Set(); } catch { /* 忽略 */ }
        }
    }

    /// <summary>后台写手：唯一碰文件句柄的线程（幂等）。</summary>
    private static void EnsureWorker()
    {
        if (_worker != null) return;
        lock (Gate)
        {
            if (_worker != null) return;
            var t = new Thread(WorkerLoop)
            {
                IsBackground = true,                 // 不拦住游戏退出
                Name = "CharacterBosses.LogWriter",
                Priority = ThreadPriority.BelowNormal // 抢 CPU 别影响帧率
            };
            _worker = t;
            try { t.Start(); } catch { _worker = null; }
            try { AppDomain.CurrentDomain.ProcessExit += (_, _) => DrainSync(); } catch { /* 忽略 */ }
        }
    }

    private static void WorkerLoop()
    {
        while (true)
        {
            try { Wake.WaitOne(200); } catch { Thread.Sleep(200); }
            try { Drain(); } catch { _failures++; CloseQuietly(); }
        }
    }

    /// <summary>把内存队列里的行落到盘（**只由后台写手调用**）。</summary>
    private static void Drain()
    {
        string[] batch;
        lock (Gate)
        {
            if (Pending.Count == 0) return;
            batch = Pending.ToArray();
            Pending.Clear();
        }

        int written = 0;
        try
        {
            EnsureWriter();
            if (_writer == null) { Requeue(batch, written); return; }

            for (; written < batch.Length; written++) _writer.Write(batch[written]);
            _writer.Flush();                 // 唯一的磁盘动作，发生在后台线程
        }
        catch
        {
            // 写不进去（磁盘满 / 独占）⇒ 没写成的那些放回队列，下一趟再试，绝不丢
            _failures++;
            CloseQuietly();
            Requeue(batch, written);
        }
    }

    /// <summary>把没写成的行放回队列头部（有上限，避免磁盘长期失败时吃爆内存）。</summary>
    private static void Requeue(string[] batch, int from)
    {
        int count = batch.Length - from;
        if (count <= 0) return;
        lock (Gate)
        {
            if (count > MaxPendingLines)
            {
                from += count - MaxPendingLines;
                count = MaxPendingLines;
            }
            Pending.InsertRange(0, new ArraySegment<string>(batch, from, count));
        }
    }

    /// <summary>进程退出时尽量把尾巴落盘（同步，最多等一小会儿）。</summary>
    private static void DrainSync()
    {
        try
        {
            for (int i = 0; i < 5; i++)
            {
                lock (Gate) if (Pending.Count == 0) break;
                Drain();
                Thread.Sleep(20);
            }
            CloseQuietly();
        }
        catch { /* 忽略 */ }
    }

    private static void EnsureWriter()
    {
        if (_writer != null)
        {
            // 每隔 5 分钟换一次文件句柄，避免极长的会话一直占着文件
            if ((DateTime.UtcNow - _openedAtUtc).TotalMinutes < 5) return;
            CloseQuietly();
        }

        if (!_dirEnsured)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            _dirEnsured = true;
        }

        _stream = new FileStream(LogPath, FileMode.Append, FileAccess.Write,
            FileShare.ReadWrite);              // ← 关键：允许别的进程同时写（双开那次的教训）
        _writer = new StreamWriter(_stream);
        _openedAtUtc = DateTime.UtcNow;
    }

    private static void CloseQuietly()
    {
        try { _writer?.Dispose(); } catch { /* 忽略 */ }
        try { _stream?.Dispose(); } catch { /* 忽略 */ }
        _writer = null;
        _stream = null;
    }

    /// <summary>
    /// 以前是「主线程顺手 flush」的入口，现在是**非阻塞的空操作**（只催一下后台写手）。
    /// 帧回调里仍然照旧调一次，代价是一次 `Set()`。
    /// </summary>
    internal static void FlushIfDue()
    {
        try { Wake.Set(); } catch { /* 忽略 */ }
    }

    /// <summary>这条日志是不是「出问题」的那种（异常 / 失败 / 不同步 / 我们的 ⚠ 诊断）。</summary>
    private static bool LooksLikeProblem(string message)
    {
        try
        {
            if (message.Contains("失败") || message.Contains("异常") || message.Contains("Exception") ||
                message.Contains("不同步") || message.Contains('⚠')) return true;
        }
        catch { /* 忽略 */ }
        return false;
    }

    /// <summary>写失败的次数（调试用）。</summary>
    internal static int Failures => _failures;
}
