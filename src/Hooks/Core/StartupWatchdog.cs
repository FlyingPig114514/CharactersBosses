using System;
using System.Threading.Tasks;

namespace CharacterBosses.Core;

/// <summary>
/// **启动期异常快照器**（用户 2026-11-20：玩家一开我们模组，游戏就弹「致命错误！在游戏启动期间遇到了问题」）。
///
/// 为什么需要它：那个弹窗来自游戏自己的 `NGame.GameStartupWrapper` ——
/// 它把 `GameStartup()` 的异常 catch 住 → 弹 `STARTUP_ERROR` 对话框 → **重新 throw**。
/// 也就是说异常**已经被游戏 catch 过一遍**，`AppDomain.UnhandledException` 不会触发；
/// 而玩家交上来的 `godot.log` 里连一行 ERROR 都没有（那份日志其实是我们模组被禁用的那次）。
///
/// `AppDomain.FirstChanceException` 是**异常一被抛出**就回调的钩子 —— 不管后来谁 catch 了它。
/// 这样下次玩家日志里就会直接留下「是谁、在哪一行、抛了什么」，不用再猜。
///
/// 代价控制：只在**启动后 150 秒内**、最多 40 条、且只记「可疑类型」或「栈里出现我们程序集」
/// 的异常；每条都截断到 900 字。生产中基本无感。
/// </summary>
internal static class StartupWatchdog
{
    private static bool _installed;
    private static long _installedTicks;
    private static int _logged;

    private const int WindowMs = 150_000;
    private const int MaxLogs = 40;

    internal static void Install()
    {
        if (_installed) return;
        _installed = true;
        _installedTicks = Environment.TickCount64;

        try { AppDomain.CurrentDomain.FirstChanceException += OnFirstChance; }
        catch { /* 装不上就算了，绝不能影响游戏 */ }

        try
        {
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                try
                {
                    if (e.ExceptionObject is Exception ex)
                        Logger.Log("[CharBoss] ⚠ 未处理异常（进程可能退出）: " + Describe(ex));
                }
                catch { /* 忽略 */ }
            };

            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                try { Logger.Log("[CharBoss] ⚠ 未观察的任务异常: " + Describe(e.Exception)); }
                catch { /* 忽略 */ }
                try { e.SetObserved(); } catch { /* 忽略 */ }
            };
        }
        catch { /* 忽略 */ }
    }

    private static void OnFirstChance(object? sender, System.Runtime.ExceptionServices.FirstChanceExceptionEventArgs e)
    {
        try
        {
            if (_logged >= MaxLogs) return;
            if (Environment.TickCount64 - _installedTicks > WindowMs) return;

            var ex = e.Exception;
            if (ex == null) return;

            // 栈里出现我们自己的程序集 ⇒ 一定是我们的代码参与的异常，优先记
            bool ours = false;
            string stack = string.Empty;
            try
            {
                stack = ex.StackTrace ?? string.Empty;
                ours = stack.IndexOf("CharacterBosses", StringComparison.Ordinal) >= 0;
            }
            catch { /* 拿不到栈就当不是我们的 */ }

            if (!ours && !LooksSuspicious(ex)) return;

            _logged++;
            Logger.Log($"[CharBoss] ⚠ 启动期异常#{_logged}{((ours) ? "（栈里有我们）" : string.Empty)}: " + Describe(ex));
        }
        catch { /* 快照器自身绝不能抛 */ }
    }

    /// <summary>启动期有可能致命的那几类异常（普通业务异常不记，免得刷屏）。</summary>
    private static bool LooksSuspicious(Exception ex)
    {
        return ex is NullReferenceException
            || ex is TypeLoadException
            || ex is System.IO.FileNotFoundException
            || ex is System.IO.FileLoadException
            || ex is MissingMethodException
            || ex is MissingFieldException
            || ex is InvalidCastException
            || ex is IndexOutOfRangeException
            || ex is ArgumentOutOfRangeException
            || ex is InvalidOperationException
            || ex is HarmonyLib.HarmonyException
            || (ex.GetType().FullName?.IndexOf("Harmony", StringComparison.Ordinal) >= 0);
    }

    private static string Describe(Exception ex)
    {
        try
        {
            var text = ex.GetType().Name + ": " + ex.Message;
            if (ex.InnerException != null)
                text += " ← " + ex.InnerException.GetType().Name + ": " + ex.InnerException.Message;

            string stack = string.Empty;
            try { stack = ex.StackTrace ?? string.Empty; } catch { /* 忽略 */ }
            if (stack.Length > 0)
            {
                // 只留前 10 帧，够定位、不刷爆日志
                var lines = stack.Replace("\r", string.Empty).Split('\n');
                int kept = 0;
                var sb = new System.Text.StringBuilder();
                foreach (var line in lines)
                {
                    var t = line.Trim();
                    if (t.Length == 0) continue;
                    if (kept++ >= 10) break;
                    sb.Append(" | ").Append(t);
                }
                text += sb.ToString();
            }

            return text.Length > 900 ? text.Substring(0, 900) + "…(截断)" : text;
        }
        catch { return ex.GetType().Name; }
    }
}
