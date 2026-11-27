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
                TraceUnobserved(e.Exception);
                try { e.SetObserved(); } catch { /* 忽略 */ }
            };

            // ★★ v0.2.898（玩家 2026-11-22「亡命之徒打电球机崩溃」：两份日志里**一条异常都没有**，
            //   `godot.log` 直接断在闪电球激发的受击动画后面 ⇒ 要么是原生层硬崩，要么异常在
            //   Godot 的帧回调 / 异步续体里被吞掉、进程随即消失）：
            //   加一道**首过异常**（FirstChanceException）面包屑 —— 只在「原版帧」里抛、又不是我们自己的
            //   代码抛的那几种（空引用 / 无效操作 / 参数为空）才记，去重 + 全程最多 20 行。
            //   这样下一份 `characterbosses_trace.txt` 里就能看到「崩之前原版哪一句先空引用了」。
            AppDomain.CurrentDomain.FirstChanceException += (_, e) => TraceFirstChance(e.Exception);
        }
        catch { /* 忽略 */ }
    }

    private static readonly System.Collections.Generic.HashSet<string> _seenFirstChance = new();
    private static int _firstChanceLines;

    private static readonly System.Collections.Generic.HashSet<string> _seenUnobserved = new();
    private static int _unobservedLines;

    /// <summary>
    /// **未观察的任务异常**的面包屑（v0.2.899，玩家 2026-11-22 的 `characterbosses_trace.txt` 实证）。
    ///
    /// 玩家那份 trace 里 28 个会话是这样分布的：
    /// <code>
    /// 25. pid=17308  19:31:41 → 20:20:28  408 行  «!! 391 条 未观察的任务异常»
    /// 16. pid=74620  20:58:30 → 01:53:54  392 行  «!! 161 条 未观察的任务异常»
    /// 28. pid=64348  22:37:32 → 22:37:46   17 行  «真正的崩溃会话：一条面包屑都没有»
    /// </code>
    /// 两个毛病：
    ///   ① 旧写法把 **AggregateException 本身**丢给 `Describe`，而它的 `StackTrace` 基本是空的
    ///      ⇒ 391 行全是同一句「A Task's exception(s) were not observed…」，**看不到是谁抛的**；
    ///   ② 它还会**吃光** `_writes` 那 400 行的总额度 ⇒ 同一个进程里真出事时的面包屑被丢掉
    ///      （这很可能就是崩溃会话什么都留不下的原因之一）。
    ///
    /// 现在：拆开 AggregateException 取**内层**异常（它的栈才是真正抛出的那一帧）、
    /// 按「类型 + 第一帧」去重、全程最多 20 行，超了打一行说明就停 —— 把额度留给真正的崩溃。
    /// </summary>
    private static void TraceUnobserved(Exception? ex)
    {
        try
        {
            if (ex == null) return;
            if (_unobservedLines >= 20)
            {
                if (_unobservedLines++ == 20) Write("!! 未观察的任务异常已记满 20 条（去重后），后面的只计数不再记");
                return;
            }

            var inner = ex;
            try
            {
                if (inner is AggregateException agg && agg.InnerExceptions.Count > 0)
                    inner = agg.InnerExceptions[0];
            }
            catch { /* 忽略：拆不开就用原异常 */ }

            string key;
            try
            {
                string stack = inner.StackTrace ?? string.Empty;
                string first = stack.Length > 0
                    ? stack.Replace("\r", string.Empty).Split('\n')[0].Trim()
                    : "(无栈)";
                key = inner.GetType().Name + "|" + first;
            }
            catch { key = inner.GetType().Name; }

            lock (_seenUnobserved)
            {
                if (!_seenUnobserved.Add(key)) return;
                _unobservedLines++;
            }

            Problem("未观察的任务异常（内层）", inner);
        }
        catch { /* 面包屑绝不影响游戏 */ }
    }

    /// <summary>首过异常面包屑（过滤 + 去重 + 限量，见 <see cref="Install"/> 里的说明）。</summary>
    private static void TraceFirstChance(Exception? ex)
    {
        try
        {
            if (ex == null) return;
            if (_firstChanceLines >= 20) return;
            if (ex is not NullReferenceException && ex is not InvalidOperationException &&
                ex is not ArgumentNullException && ex is not IndexOutOfRangeException) return;

            string stack;
            try { stack = ex.StackTrace ?? string.Empty; } catch { return; }
            if (stack.Length == 0) return;
            if (stack.Contains("CharacterBosses")) return;        // 我们自己抛的（外面都有 try/catch）
            if (!stack.Contains("MegaCrit")) return;              // 与原版无关

            // 去重：按第一行栈帧（同一个抛点的重复异常只留一条）
            string key;
            try
            {
                key = ex.GetType().Name + "|" + stack.Replace("\r", string.Empty).Split('\n')[0].Trim();
            }
            catch { key = ex.GetType().Name; }

            lock (_seenFirstChance)
            {
                if (!_seenFirstChance.Add(key)) return;
                _firstChanceLines++;
            }

            Problem("首过异常（原版帧，可能被吞掉）", ex);
        }
        catch { /* 面包屑绝不影响游戏 */ }
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
            // ★★ v0.2.980（用户 2026-11-27：「检查一下，目前本模组有没有什么其它地方写了隐私信息？」）：
            //   这个文件**以前完全没脱敏**，实测里面有 16 处 `C:\Users\<用户名>\…` 和 3 处 17 位 SteamID
            //   （都来自游戏自己的异常消息，例如 `Could not find file 'C:\Users\<名字>\AppData\…'`、
            //     `Save is invalid! … Local ID: 7656…9423`）。而 v0.2.976 起它会被
            //   **打进玩家要发给作者的 zip** ⇒ 必须在这里过一遍 `Redact`（代号规则不变，照样能定位问题）。
            line = Redact.Text(line);
        }
        catch { /* 脱敏失败也要落盘（宁可多一条真 ID，也不能丢面包屑） */ }

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
