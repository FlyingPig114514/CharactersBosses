using System;
using System.IO;
using System.Text;
using Godot;

namespace CharacterBosses.Core;

/// <summary>
/// **按 F8 一键导出「最近约一小时的日志」**（v0.2.900 起；v0.2.901 从「最近 3000 行」改成按时间）。
///
/// 为什么需要（用户 2026-11-23 问：「报 bug 模板里为什么没有 mod_log」）：
///   非崩溃类问题（数值不对 / 意图不对 / 联机不同步）**恰恰只能靠 mod_log 定位** ——
///   里面记着每回合的计划、意图、牌堆指纹、支配落地层数、翻侧状态…
///   但整份 `mod_log.txt` 有两个硬问题：
///     · **体积**：一局 3 小时能写 20 万行 / 几十 MB（玩家的那份累积到 96 MB）；
///     · **崩溃时尾巴会丢**：`SafeLogPatch` 是后台线程缓冲落盘（最多 200 ms），硬崩就没了尾巴。
///   ⇒ 正确做法不是「不发」，而是「**发得小**」。
///
/// ★ 用户 2026-11-23 定口径：「改成大概最近一个小时吧，一局最长大概也就这么久」
///   ⇒ 按**时间**留最近 60 分钟（日志密度约 15 行/秒，≈5.4 万行 / 5～8 MB），
///     刚好覆盖一整局，玩家不用「打完了才发现不对、再回头找」。
///
/// 输出：`%APPDATA%\SlayTheSpire2\logs\characterbosses_recent.txt`
///   · 开头写构建串（`BuildInfo.Tag`）+ 导出时间 + **覆盖时段** + 一句「已脱敏」说明；
///   · 正文是最近 60 分钟的日志（**已经过 `Redact` 脱敏**，不含 SteamID / NetId / 昵称 / 用户名）。
///
/// 与 F9（`SceneTreeDumper`，导场景树）同一套挂载方式：挂在 root 上、只吃 unhandled key input。
/// </summary>
internal sealed partial class RecentLogDumper : Node
{
    private static RecentLogDumper? _inst;

    /// <summary>幂等挂载（由帧回调每帧调一次，内部只有一次真正创建）。</summary>
    internal static void Ensure()
    {
        try
        {
            if (_inst != null && GodotObject.IsInstanceValid(_inst)) return;
            if (Engine.GetMainLoop() is not SceneTree tree) return;
            _inst = new RecentLogDumper { Name = "CharacterBossesRecentLogDumper" };
            tree.Root.AddChild(_inst);
            _inst.SetProcessUnhandledKeyInput(true);
            Logger.Log($"[CharBoss] 最近日志导出：按 **F8** 导出到 {TargetPath()}（报 bug 用，几十 KB）");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 最近日志导出器挂载失败: " + e.Message);
        }
    }

    private static string TargetPath()
        => Path.Combine(OS.GetUserDataDir(), "logs", "characterbosses_recent.txt");

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        try
        {
            if (@event is not InputEventKey key || !key.Pressed || key.Echo) return;
            if (key.Keycode != Key.F8 && key.PhysicalKeycode != Key.F8) return;
            Dump();
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 最近日志导出失败: " + e.Message);
        }
    }

    /// <summary>把内存里的最近约一小时日志写成文件（幂等、可重复按）。</summary>
    internal static void Dump()
    {
        try
        {
            var lines = Patches.SafeLogPatch.SnapshotRecent(out var first, out var last);

            var sb = new StringBuilder();
            sb.AppendLine("===== 玩家角色Boss · 最近日志（游戏内按 F8 导出）=====");
            try { sb.AppendLine("构建：" + BuildInfo.Tag); } catch { /* 忽略 */ }
            sb.AppendLine("导出时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("覆盖时段：" + first.ToString("HH:mm:ss") + " → " + last.ToString("HH:mm:ss") +
                          "（最多最近 60 分钟）");
            sb.AppendLine("行数：" + lines.Length + "（只含本模组日志）");
            sb.AppendLine("隐私：SteamID / NetId / 玩家昵称 / 路径里的用户名已替换成局内代号（P#xxxxxx）");
            sb.AppendLine("提示：崩溃/卡死时更该发 characterbosses_trace.txt（逐行落盘，崩得再快也留得住）");
            sb.AppendLine();

            foreach (var line in lines)
            {
                try { sb.AppendLine(line.TrimEnd('\r', '\n')); } catch { /* 单行坏了跳过 */ }
            }

            var path = TargetPath();
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            // WriteThrough：导完立刻断电/被杀也留得住
            using (var fs = new FileStream(path, FileMode.Create, System.IO.FileAccess.Write,
                                           FileShare.ReadWrite, 4096, System.IO.FileOptions.WriteThrough))
            using (var sw = new StreamWriter(fs, new UTF8Encoding(false)))
            {
                sw.Write(sb.ToString());
                sw.Flush();
                fs.Flush(true);
            }

            Logger.Log($"[CharBoss] 最近日志已导出：{path}（{lines.Length} 行，直接发这个文件就行）");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 最近日志导出失败（可改用 PowerShell 取 mod_log 尾部）: " + e.Message);
        }
    }
}
