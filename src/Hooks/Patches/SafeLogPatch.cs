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

    [HarmonyPrefix]
    public static bool Prefix(string message)
    {
        try
        {
            string line = $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}";
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
                using var stream = new FileStream(LogPath, FileMode.Append, FileAccess.Write,
                    FileShare.ReadWrite);          // ← 关键：允许别的进程同时写
                using var writer = new StreamWriter(stream);
                writer.Write(line);
            }
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
