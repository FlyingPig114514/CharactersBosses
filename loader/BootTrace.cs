using System;
using System.IO;
using System.Text;

namespace CharacterBosses;

/// <summary>
/// **外壳侧的面包屑**（`loader/Loader.cs` 专用）。
///
/// 为什么这里再写一份几乎一样的代码：外壳（`CharacterBosses.dll`）和实现（`CharacterBosses.Impl113.dll`）
/// 是**两个程序集**，外壳跑在最前面、实现还没被载入时就得能把「走到哪一步」记下来。
/// 而玩家的崩溃恰恰可能发生在这段窗口里（连日志都没来得及写）。
///
/// 写法和实现侧的 <c>Core.CrashTrace</c> 完全一致：每行都 `WriteThrough + Flush(true)` 真正落盘，
/// 同一个文件 `%APPDATA%\SlayTheSpire2\logs\characterbosses_trace.txt`，玩家发这一个文件就够。
/// </summary>
internal static class BootTrace
{
    private static readonly object Gate = new();
    private static bool _headerWritten;
    private static int _writes;

    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "SlayTheSpire2", "logs", "characterbosses_trace.txt");

    internal static void Mark(string what)
    {
        if (_writes >= 200) return;
        _writes++;

        try
        {
            lock (Gate)
            {
                var dir = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                string line;
                if (!_headerWritten)
                {
                    _headerWritten = true;
                    line = "===== 外壳入口 pid=" + SafePid() + " =====" + Environment.NewLine +
                           "[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] [外壳] " + what;
                }
                else
                {
                    line = "[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] [外壳] " + what;
                }

                using var fs = new FileStream(FilePath, FileMode.Append, FileAccess.Write,
                    FileShare.ReadWrite, 512, FileOptions.WriteThrough);
                using var sw = new StreamWriter(fs, new UTF8Encoding(false));
                sw.Write(line + Environment.NewLine);
                sw.Flush();
                fs.Flush(true);
            }
        }
        catch { /* 面包屑失败绝不影响加载 */ }
    }

    internal static void Problem(string what, Exception? e)
    {
        Mark("!! " + what + (e == null ? string.Empty : " ⇒ " + e.GetType().Name + ": " + e.Message));
    }

    private static string SafePid()
    {
        try { return System.Environment.ProcessId.ToString(); } catch { return "?"; }
    }
}
