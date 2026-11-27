using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using Godot;
// `using Godot;` 会带进 Godot.Environment / Godot.FileAccess，与 System.Environment / System.IO.FileAccess 撞名
using Environment = System.Environment;
using FileAccess = System.IO.FileAccess;
using FileShare = System.IO.FileShare;

namespace CharacterBosses.Core;

/// <summary>
/// **按 F8 一键导出「最近的日志 + 报告压缩包」**（v0.2.900 起；v0.2.976 重写触发通道与数据源）。
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
///   ⇒ 按**时间**留最近 60 分钟（日志密度约 15 行/秒，≈5.4 万行 / 5～8 MB）。
///
/// ★★ v0.2.976 两处根本性重做（用户 2026-11-27：「游戏内按 F8 生成『最近日志』的功能从没成功过」）：
///   ① **触发通道**：原来只挂在我们自己 new 的裸 Node 的 `_UnhandledKeyInput` 上 ——
///      实测 `mod_log.txt` 里挂载成功行 58 行、「已导出 / 导出失败」各 0 行、文件不存在
///      ⇒ 按键压根没进那个回调。现在改由 `Core.HotkeyRouter` 统管，**四条通道并行**
///      （游戏自己的 `NGame._Input` / `NInputManager._UnhandledKeyInput` + 我们自己 Node 的两条），
///      见 `Core/HotkeyPatches.cs`。
///   ② **数据源**：原来只从内存环里取。现在**优先读磁盘上 `mod_log.txt` 的最后 8 MB**（那份永远最全，
///      连崩溃前一刻的内容都在），内存环降级成兜底；并且顺手把崩溃面包屑 / 停摆报告 / 不同步报告
///      一起打成 `<c>characterbosses_report_yyyyMMdd_HHmmss.zip</c>`，玩家只需发一个文件。
///
/// 输出：
///   · `%APPDATA%\SlayTheSpire2\logs\characterbosses_recent.txt` —— 表头 + 最近约 60 分钟日志（已脱敏）；
///   · 同目录 `characterbosses_report_*.zip` —— 上面那份 txt +（存在才带）trace / stall / desync 报告。
/// </summary>
internal sealed partial class RecentLogDumper : Node
{
    /// <summary>从磁盘读多少个字节的尾巴（8 MB ≈ 最近 8～9 小时的量，正常远超一局）。</summary>
    private const int TailBytes = 8 * 1024 * 1024;

    /// <summary>最多导出多少行（1 小时约 5.4 万行，这里留点余量）。</summary>
    private const int MaxLines = 60000;

    /// <summary>时间窗：最近 60 分钟（用户口径：「一局最长大概也就这么久」）。</summary>
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(60);

    /// <summary>单个附带报告最多打进 zip 多少字节（防一手超大文件）。</summary>
    private const long MaxAttachBytes = 8 * 1024 * 1024;

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
            // 三条都开：_Input 在 GUI 之前最不容易被吃，_UnhandledKeyInput 是原来的写法，
            // _UnhandledInput 顺手也听一份（只做探针，真正干活的是 HotkeyRouter）。
            _inst.SetProcessInput(true);
            _inst.SetProcessUnhandledInput(true);
            _inst.SetProcessUnhandledKeyInput(true);
            Logger.Log($"[CharBoss] 最近日志导出：按 **F8** 导出到 {TargetPath()}（报 bug 用，含一份 zip 报告包）");
            SelfCheck();
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 最近日志导出器挂载失败: " + e.Message);
        }
    }

    private static bool _selfChecked;

    /// <summary>
    /// ★ v0.2.977：**挂载之后的自检行**（只打一次）。
    ///
    /// 为什么要它：实测（2026-11-27，本机 v0.2.976）四条通道里
    /// **游戏的**两条（`NGame._Input` / `NInputManager._UnhandledKeyInput`）都活着、
    /// **我们自己 Node 的**三条（`_Input` / `_UnhandledInput` / `_UnhandledKeyInput`）**一条都没被调到**
    /// ⇒ 说明问题不在「事件被上游吃掉」，而在「Godot 根本没把这个节点当成该收输入的节点」。
    /// 这一行把 Godot 自己的说法打出来（`has_method(_input)` / 脚本实例 / process 开关），
    /// 下次谁再想「给我自己的 Node 挂个输入回调」就能先看这行，别又白试一遍。
    /// （同一条现象在更早的会话里也记过：本模组自绘 UI 的 `_Process` / `ProcessFrame` / `Control.Resized`
    ///   实测同样不触发 —— 见 `.dshwolf/cerebrum.md` 的 Do-Not-Repeat。）
    /// </summary>
    private static void SelfCheck()
    {
        if (_selfChecked || _inst == null) return;
        _selfChecked = true;
        try
        {
            string hasInput = "?", hasUnhandledKey = "?", script = "?", procInput = "?", procUnhandledKey = "?", path = "?";
            try { hasInput = _inst.HasMethod("_input").ToString(); } catch { /* 忽略 */ }
            try { hasUnhandledKey = _inst.HasMethod("_unhandled_key_input").ToString(); } catch { /* 忽略 */ }
            try { procInput = _inst.IsProcessingInput().ToString(); } catch { /* 忽略 */ }
            try { procUnhandledKey = _inst.IsProcessingUnhandledKeyInput().ToString(); } catch { /* 忽略 */ }
            try { script = _inst.GetScript().VariantType == Variant.Type.Nil ? "null" : "有"; } catch { /* 忽略 */ }
            try { path = _inst.GetPath().ToString(); } catch { /* 忽略 */ }

            Logger.Log($"[CharBoss] 导出器节点自检：path={path} script={script} " +
                       $"has_method(_input)={hasInput} has_method(_unhandled_key_input)={hasUnhandledKey} " +
                       $"processInput={procInput} processUnhandledKey={procUnhandledKey}" +
                       "（若 has_method 都是 False，就说明 Godot 不认为这个节点能收输入 —— " +
                       "F8/F9 靠的是 HotkeyPatches 里挂在游戏自己输入入口上的那条通道）");
        }
        catch { /* 自检绝不影响挂载 */ }
    }

    internal static string TargetPath()
        => Path.Combine(OS.GetUserDataDir(), "logs", "characterbosses_recent.txt");

    /// <summary>日志目录（**运行时解析出来的绝对路径**，就是这个进程真正在写的那个目录）。</summary>
    internal static string LogDirectory()
    {
        try
        {
            var dir = Path.GetDirectoryName(Patches.SafeLogPatch.LogPath);
            if (!string.IsNullOrEmpty(dir)) return dir!;
        }
        catch { /* 忽略 */ }
        return "（取不到）";
    }

    /// <summary>
    /// 给玩家看/贴到界面上的日志目录：把 `C:\Users\&lt;名字&gt;` 换成 `%APPDATA%`
    /// （★ v0.2.979，用户 2026-11-27：「你这个写的怎么是我电脑上的路径，这不能让玩家看吧」
    ///  —— 路径本身是**玩家自己机器上运行时算出来的**，但屏幕上不该出现任何人的用户名）。
    /// </summary>
    internal static string LogDirectoryFriendly() => PathDisplay.Mask(LogDirectory());

    // ── 触发：所有通道统一交给 HotkeyRouter（它内部判 F8/F9、去重、打「按到 F8 了」那行） ──
    public override void _Input(InputEvent @event) => HotkeyRouter.Handle(@event, "node._Input");
    public override void _UnhandledInput(InputEvent @event) => HotkeyRouter.Handle(@event, "node._UnhandledInput");
    public override void _UnhandledKeyInput(InputEvent @event) => HotkeyRouter.Handle(@event, "node._UnhandledKeyInput");

    /// <summary>
    /// 把最近约一小时日志（+ 附带报告）导出成文件（幂等、可重复调用）。
    /// **返回一句给人看的结果**（成功/失败 + 路径），设置页那个「导出」按钮会把这句话显示在面板上 ——
    /// 手机上没键盘、按不了 F8，玩家就靠这句话知道文件落在哪。
    /// </summary>
    internal static string Dump()
    {
        try
        {
            var (lines, first, last, source) = ReadRecent();
            if (lines.Length == 0)
            {
                Logger.Log("[CharBoss] 最近日志导出失败：一条日志都没读到（磁盘尾巴和内存环都是空的）" +
                           "—— 可改用 PowerShell 取 mod_log 尾部");
                return "失败：一条日志都没读到（磁盘尾巴和内存环都是空的）。\n" +
                       "可以退一步，直接把整份日志发给我：" + Patches.SafeLogPatch.LogPath;
            }

            var sb = new StringBuilder();
            sb.AppendLine("===== 玩家角色Boss · 最近日志（游戏内按 F8 导出）=====");
            try { sb.AppendLine("构建：" + BuildInfo.Tag); } catch { /* 忽略 */ }
            sb.AppendLine("导出时间：" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("覆盖时段：" + first.ToString("HH:mm:ss") + " → " + last.ToString("HH:mm:ss") +
                          "（最多最近 60 分钟）");
            sb.AppendLine("行数：" + lines.Length + "（只含本模组日志；数据源 = " + source + "）");
            sb.AppendLine("触发通道：" + HotkeyRouter.LastChannel + "（本次活着的通道：" + HotkeyRouter.ChannelsSeen() + "）");
            sb.AppendLine("隐私：SteamID / NetId / 玩家昵称 / 路径里的用户名已替换成局内代号（P#xxxxxx）；" +
                          "zip 里的 trace / stall / desync 也已同样脱敏");
            sb.AppendLine("提示：崩溃/卡死时更该发同目录的 characterbosses_report_*.zip（里面带 trace / stall 报告）");
            sb.AppendLine();

            foreach (var line in lines)
            {
                try
                {
                    // ★ v0.2.980：**再脱敏一遍**（幂等）。两个理由：
                    //   ① 磁盘上那 8 MB 里可能有**旧版本**写的行（那时 Redact 的路径正则只认反斜杠）；
                    //   ② 玩家发出去的就是这份文件，宁可多一道保险。
                    sb.AppendLine(Redact.LooksSensitive(line) ? Redact.Text(line) : line);
                }
                catch { /* 单行坏了跳过 */ }
            }

            var path = TargetPath();
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            // WriteThrough：导完立刻断电/被杀也留得住
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write,
                                           FileShare.ReadWrite, 4096, FileOptions.WriteThrough))
            using (var sw = new StreamWriter(fs, new UTF8Encoding(false)))
            {
                sw.Write(sb.ToString());
                sw.Flush();
                fs.Flush(true);
            }

            Logger.Log($"[CharBoss] 最近日志已导出：{path}（{lines.Length} 行，覆盖 " +
                       $"{first:HH:mm:ss}→{last:HH:mm:ss}，数据源={source}，直接发这个文件就行）");

            string? zip = TryZipReport(path);
            // 给界面看的一律走 PathDisplay.Mask：不把 `C:\Users\<玩家名>` 印在屏幕上
            return $"已导出 {lines.Length} 行（覆盖 {first:HH:mm:ss}→{last:HH:mm:ss}）\n" +
                   "文本：" + PathDisplay.Mask(path) + "\n" +
                   (zip != null ? "报告包（推荐发这个）：" + PathDisplay.Mask(zip)
                                : "（报告包没生成，发上面那个 txt 就行）");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 最近日志导出失败（可改用 PowerShell 取 mod_log 尾部）: " + e.Message);
            return "失败：" + e.Message + "\n可以退一步，直接把整份日志发给我：" +
                   PathDisplay.Mask(Patches.SafeLogPatch.LogPath);
        }
    }

    // ── 数据源 ────────────────────────────────────────────────────────────

    /// <summary>
    /// 取最近约一小时的日志行。
    /// **优先读磁盘尾巴**（`mod_log.txt` 永远最全），磁盘读失败才退回 `SafeLogPatch` 的内存环。
    /// </summary>
    private static (string[] Lines, DateTime First, DateTime Last, string Source) ReadRecent()
    {
        string reason = string.Empty;
        try
        {
            var (diskLines, dFirst, dLast) = ReadTailFromDisk();
            if (diskLines.Length > 0) return (diskLines, dFirst, dLast, "磁盘尾巴");
            reason = "磁盘尾巴是空的";
        }
        catch (Exception e)
        {
            reason = "读磁盘尾巴失败: " + e.Message;
        }

        try { Logger.Log("[CharBoss] " + reason + "，退回内存环"); } catch { /* 忽略 */ }

        string[] mem = Patches.SafeLogPatch.SnapshotRecent(out var first, out var last);
        var list = new List<string>(mem.Length);
        foreach (var raw in mem)
        {
            // 内存环里的行是「[HH:mm:ss] 正文 + 换行」，去掉换行免得写出双空行
            string line = (raw ?? string.Empty).TrimEnd('\r', '\n');
            if (line.Length > 0) list.Add(line);
        }
        return (list.ToArray(), first, last, "内存环（退路）");
    }

    /// <summary>读 `mod_log.txt` 的最后 <see cref="TailBytes"/> 字节，按行切开并裁到「最近 60 分钟」。</summary>
    private static (string[] Lines, DateTime First, DateTime Last) ReadTailFromDisk()
    {
        string logPath = Patches.SafeLogPatch.LogPath;
        if (!File.Exists(logPath)) return (Array.Empty<string>(), default, default);

        // FileShare.ReadWrite：SafeLogPatch 的后台写手正开着这个文件，不能独占
        using var fs = new FileStream(logPath, FileMode.Open, FileAccess.Read,
                                      FileShare.ReadWrite | FileShare.Delete);
        long len = fs.Length;
        long start = Math.Max(0, len - TailBytes);
        int size = (int)(len - start);
        if (size <= 0) return (Array.Empty<string>(), default, default);

        var buf = new byte[size];
        fs.Seek(start, SeekOrigin.Begin);
        int read = 0;
        while (read < size)
        {
            int n = fs.Read(buf, read, size - read);
            if (n <= 0) break;
            read += n;
        }

        // 日志是 UTF-8（Logger 与 SafeLogPatch 都是默认 UTF-8 无 BOM）
        string text = Encoding.UTF8.GetString(buf, 0, read);
        var raw = text.Split('\n');

        int from = 0;
        if (start > 0) from = 1;                       // 从文件中间开始 ⇒ 第一行是半截，丢掉
        if (raw.Length - from > MaxLines) from = raw.Length - MaxLines;

        var list = new List<string>(Math.Max(0, raw.Length - from));
        for (int i = from; i < raw.Length; i++)
        {
            string line = raw[i].TrimEnd('\r');
            if (line.Length == 0) continue;
            list.Add(line);
        }
        var lines = list.ToArray();
        if (lines.Length == 0) return (Array.Empty<string>(), default, default);

        // ── 按时间裁到「最近 60 分钟」：以最后一条能认出时间戳的行为基准 ──
        DateTime? refStamp = null;
        for (int i = lines.Length - 1; i >= 0 && refStamp == null; i--) refStamp = StampOf(lines[i]);
        DateTime first = refStamp ?? File.GetLastWriteTime(logPath);
        DateTime last = refStamp ?? File.GetLastWriteTime(logPath);

        if (refStamp is DateTime refT)
        {
            int keepFrom = 0;
            DateTime earliest = refT;
            for (int i = lines.Length - 1; i >= 0; i--)
            {
                if (StampOf(lines[i]) is not DateTime t) continue;
                var delta = refT - t;
                if (delta < TimeSpan.Zero) delta += TimeSpan.FromDays(1);   // 跨零点
                if (delta > Window) { keepFrom = i + 1; break; }
                earliest = t;
            }
            if (keepFrom > 0 && keepFrom < lines.Length)
                lines = lines[keepFrom..];                 // 只留窗口内的
            first = earliest;
            last = refT;
        }

        return (lines, first, last);
    }

    /// <summary>认出日志行开头的 `[HH:mm:ss]`（认不出返回 null；日期部分不重要，只用来算相对时间）。</summary>
    private static DateTime? StampOf(string line)
    {
        try
        {
            if (line.Length < 10 || line[0] != '[' || line[9] != ']') return null;
            if (!int.TryParse(line.AsSpan(1, 2), out int h)) return null;
            if (!int.TryParse(line.AsSpan(4, 2), out int m)) return null;
            if (!int.TryParse(line.AsSpan(7, 2), out int s)) return null;
            if (h > 23 || m > 59 || s > 59) return null;
            return DateTime.Today.AddHours(h).AddMinutes(m).AddSeconds(s);
        }
        catch { return null; }
    }

    // ── 报告压缩包 ─────────────────────────────────────────────────────────

    /// <summary>
    /// 把刚导出的 txt +（存在才带）崩溃面包屑 / 停摆报告 / 不同步报告打成一个 zip。
    /// 返回 zip 的绝对路径（生成失败返回 null —— 那不算导出失败，txt 已经存好了）。
    /// </summary>
    private static string? TryZipReport(string recentPath)
    {
        try
        {
            string dir = Path.GetDirectoryName(recentPath) ?? OS.GetUserDataDir();
            string zipPath = Path.Combine(dir, "characterbosses_report_" +
                                              DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".zip");

            var attached = new List<string>();
            using (var fs = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.Read))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                if (AddTextFile(zip, recentPath, "characterbosses_recent.txt")) attached.Add("recent.txt");
                foreach (var name in new[] { "characterbosses_trace.txt", "characterbosses_stall.txt", "characterbosses_desync.txt" })
                    if (AddTextFile(zip, Path.Combine(dir, name), name)) attached.Add(name);
            }

            Logger.Log($"[CharBoss] 最近日志报告包已生成：{zipPath}（含 {string.Join(" + ", attached)}）");
            return zipPath;
        }
        catch (Exception e)
        {
            // 打不出 zip 不算失败：txt 已经存好了，让玩家直接发那个
            Logger.Log("[CharBoss] 报告压缩包生成失败（txt 已存好，直接发那个就行）: " + e.Message);
            return null;
        }
    }

    /// <summary>
    /// 把一个文本文件**脱敏后**塞进 zip（不存在就跳过；超大只取尾巴）。返回是否真的加进去了。
    ///
    /// ★★ v0.2.980（用户 2026-11-27 让我查隐私信息）：以前是**原样 FileStream.CopyTo**，
    /// 于是磁盘上那些**旧版本**写出来的 `trace` / `desync`（实测分别带 16 处用户名、117 处 SteamID）
    /// 会被原封不动打进玩家发给作者的 zip。现在读出来过一遍 <see cref="Redact.Text"/> 再写进去，
    /// 连历史文件也一并兜住。
    /// </summary>
    private static bool AddTextFile(ZipArchive zip, string path, string entryName)
    {
        try
        {
            if (!File.Exists(path)) return false;

            string text;
            using (var src = new FileStream(path, FileMode.Open, FileAccess.Read,
                                             FileShare.ReadWrite | FileShare.Delete))
            {
                long len = src.Length;
                if (len <= 0) return false;
                bool tailOnly = len > MaxAttachBytes;
                if (tailOnly) src.Seek(len - MaxAttachBytes, SeekOrigin.Begin);

                using var ms = new MemoryStream();
                src.CopyTo(ms);
                text = Encoding.UTF8.GetString(ms.ToArray());

                // 从文件中间开始 ⇒ 第一行是半截，丢掉
                if (tailOnly)
                {
                    int nl = text.IndexOf('\n');
                    if (nl >= 0) text = text.Substring(nl + 1);
                }
            }

            try { text = Redact.Text(text); } catch { /* 脱敏失败也照旧塞进去，不能丢证据 */ }

            var entry = zip.CreateEntry(entryName, CompressionLevel.Fastest);
            using var es = entry.Open();
            byte[] bytes = new UTF8Encoding(false).GetBytes(text);
            es.Write(bytes, 0, bytes.Length);
            return true;
        }
        catch
        {
            return false;      // 单个文件加不进去不影响其它文件
        }
    }
}
