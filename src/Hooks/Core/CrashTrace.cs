using System;
using System.IO;
using System.Text;

namespace CharacterBosses.Core;

/// <summary>
/// **崩溃面包屑**（用户 2026-11-20：「我怀疑是游戏在日志记录前就崩溃了」）。
///
/// 玩家的「致命错误！在游戏启动期间遇到了问题」既不进 `godot.log`、也不进我们的 `mod_log.txt`，
/// 那有两种可能：
///   · 异常还没被写下来，进程就没了（硬崩溃 / failfast / 原生层直接挂）；
///   · 写日志时是**缓冲**的（我们的 `SafeLogPatch` 后台线程最多缓 200ms），崩得太快就丢了尾巴。
///
/// 所以这份 trace **不用任何缓冲**：每写一行就 `WriteThrough + Flush(true)` 真正落盘、随后关闭句柄，
/// 进程下一秒被强杀也留得住。写的东西只有「里程碑 + 可疑异常」，正常一局也就几十行、几十 KB。
///
/// 文件：`%APPDATA%\SlayTheSpire2\logs\characterbosses_trace.txt`（玩家直接发这个文件就行，不用挑日志）。
/// </summary>
internal static class CrashTrace
{
    private static readonly object Gate = new();
    private static bool _installed;
    private static bool _headerWritten;
    private static int _writes;

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SlayTheSpire2", "logs", "characterbosses_trace.txt");

    /// <summary>文件超过这个大小就在本次进程的第一次写入时重开（只留最近一次启动）。</summary>
    private const long MaxBytes = 400_000;

    /// <summary>装全局钩子（幂等）。越早调越好 —— 我们在第一帧 + 加载器里各调一次。</summary>
    internal static void Install()
    {
        if (_installed) return;
        _installed = true;

        Mark("Install（进程 pid=" + SafePid() + "）");

        try
        {
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                Problem("未处理异常（进程可能立刻退出）", e.ExceptionObject as Exception);

            AppDomain.CurrentDomain.ProcessExit += (_, _) =>
                Mark("进程正常退出（ProcessExit）");

            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                Problem("未观察的任务异常", e.Exception);
                try { e.SetObserved(); } catch { /* 忽略 */ }
            };
        }
        catch { /* 忽略 */ }
    }

    /// <summary>里程碑：正常推进到某一步就记一行。</summary>
    internal static void Mark(string what)
    {
        Write("· " + what);
    }

    /// <summary>疑点：异常/失败一律记下来（带类型、消息、前 8 帧）。</summary>
    internal static void Problem(string what, Exception? e)
    {
        if (e == null) { Write("!! " + what); return; }
        Write("!! " + what + " ⇒ " + Describe(e));
    }

    private static string SafePid()
    {
        try { return System.Environment.ProcessId.ToString(); } catch { return "?"; }
    }

    private static string Describe(Exception ex)
    {
        try
        {
            var sb = new StringBuilder();
            sb.Append(ex.GetType().Name).Append(": ").Append(ex.Message);
            if (ex.InnerException != null)
                sb.Append(" ← ").Append(ex.InnerException.GetType().Name).Append(": ").Append(ex.InnerException.Message);

            string stack = string.Empty;
            try { stack = ex.StackTrace ?? string.Empty; } catch { /* 忽略 */ }
            if (stack.Length > 0)
            {
                int kept = 0;
                foreach (var raw in stack.Replace("\r", string.Empty).Split('\n'))
                {
                    var t = raw.Trim();
                    if (t.Length == 0) continue;
                    if (kept++ >= 8) break;
                    sb.Append(" | ").Append(t);
                }
            }

            var text = sb.ToString();
            return text.Length > 700 ? text.Substring(0, 700) + "…(截断)" : text;
        }
        catch { return ex.GetType().Name; }
    }

    private static void Write(string line)
    {
        // 上限：一次进程最多 400 行（别把玩家磁盘写满）
        if (_writes >= 400) return;
        _writes++;

        try
        {
            lock (Gate)
            {
                var dir = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                // 本次进程第一次写：太大就覆盖重开，否则续写并加一行分隔
                if (!_headerWritten)
                {
                    _headerWritten = true;
                    try
                    {
                        if (File.Exists(FilePath) && new FileInfo(FilePath).Length > MaxBytes)
                            File.Delete(FilePath);
                    }
                    catch { /* 删不掉就续写 */ }
                    line = "===== 新进程 pid=" + SafePid() + " =====" + Environment.NewLine +
                           "[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] " + line;
                }
                else
                {
                    line = "[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] " + line;
                }

                // WriteThrough + Flush(true)：绕过系统缓存，进程被强杀也留得住
                using var fs = new FileStream(FilePath, FileMode.Append, FileAccess.Write,
                    FileShare.ReadWrite, 512, FileOptions.WriteThrough);
                using var sw = new StreamWriter(fs, new UTF8Encoding(false));
                sw.Write(line + Environment.NewLine);
                sw.Flush();
                fs.Flush(true);
            }
        }
        catch { /* 面包屑失败绝不影响游戏 */ }
    }
}
