using System;
using System.IO;
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
/// </summary>
[HarmonyPatch(typeof(CharacterBosses.Logger), nameof(CharacterBosses.Logger.Log))]
public static class SafeLogPatch
{
    private static readonly object Gate = new();

    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SlayTheSpire2", "logs", "mod_log.txt");

    private static int _failures;

    // ── v0.2.805（手机卡顿/耗电）：**写盘改成缓冲 + 长开文件** ──────────────────
    //   以前每一条日志都 `Directory.CreateDirectory(...)` + `new FileStream(...)` + 关掉 ——
    //   一局实测 203,646 行 ⇒ 20 万次「建目录 + 开关文件」。PC 上无所谓，
    //   手机上每次碰 flash 都贵、还费电（玩家反馈「手机版四人联机卡顿明显，耗电也明显」）。
    //   现在：文件只开一次、目录只建一次，攒够 32 行或过了 0.5 秒才 flush；
    //   带 ⚠ / 失败 / 不同步 这类**关键行**仍然立刻 flush（出问题时不丢证据）。
    private static FileStream? _stream;
    private static StreamWriter? _writer;
    private static DateTime _openedAtUtc;
    private static DateTime _lastFlushUtc;
    private static int _buffered;
    private static bool _dirEnsured;

    [HarmonyPrefix]
    public static bool Prefix(string message)
    {
        try
        {
            Write(message ?? string.Empty);
        }
        catch
        {
            // 落盘失败就丢弃这条日志：日志绝不能把游戏逻辑打崩（双开/磁盘忙都可能失败）
            _failures++;
            CloseQuietly();
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

    private static void Write(string message)
    {
        lock (Gate)
        {
            if (!_dirEnsured)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                _dirEnsured = true;
            }

            EnsureWriter();
            _writer!.Write($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
            _buffered++;

            var now = DateTime.UtcNow;
            // 关键行立刻落盘；其余攒批
            if (_buffered >= 32
                || (now - _lastFlushUtc).TotalMilliseconds >= 500
                || LooksLikeProblem(message))
            {
                Flush();
            }
        }
    }

    private static void EnsureWriter()
    {
        if (_writer != null)
        {
            // 每隔 5 分钟换一次文件句柄，避免极长的会话一直占着文件
            if ((DateTime.UtcNow - _openedAtUtc).TotalMinutes < 5) return;
            Flush();
            CloseQuietly();
        }

        _stream = new FileStream(LogPath, FileMode.Append, FileAccess.Write,
            FileShare.ReadWrite);              // ← 关键：允许别的进程同时写（双开那次的教训）
        _writer = new StreamWriter(_stream);
        _openedAtUtc = DateTime.UtcNow;
        _lastFlushUtc = DateTime.UtcNow;
    }

    /// <summary>把缓冲写下去（帧驱动/关键行都会调；失败静默）。</summary>
    internal static void Flush()
    {
        try
        {
            lock (Gate)
            {
                if (_writer == null) return;
                _writer.Flush();
                _buffered = 0;
                _lastFlushUtc = DateTime.UtcNow;
            }
        }
        catch { _failures++; CloseQuietly(); }
    }

    private static void CloseQuietly()
    {
        try { _writer?.Dispose(); } catch { /* 忽略 */ }
        try { _stream?.Dispose(); } catch { /* 忽略 */ }
        _writer = null;
        _stream = null;
        _buffered = 0;
    }

    /// <summary>该不该现在把日志缓冲刷下去（帧驱动每秒调一次就够）。</summary>
    internal static void FlushIfDue()
    {
        try
        {
            if (_writer == null) return;
            if (_buffered > 0 && (DateTime.UtcNow - _lastFlushUtc).TotalMilliseconds >= 500) Flush();
        }
        catch { /* 忽略 */ }
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
