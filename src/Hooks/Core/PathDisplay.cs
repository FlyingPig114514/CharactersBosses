using System;

namespace CharacterBosses.Core;

/// <summary>
/// **把绝对路径变成「能贴在界面上给人看」的样子**（v0.2.979，用户 2026-11-27：
/// 「你这个写的怎么是我电脑上的路径，这不能让玩家看吧」）。
///
/// 背景：设置页要告诉玩家「日志存在哪儿」，而路径里必定带 **Windows 用户名**
/// （`C:\Users\<他的名字>\AppData\Roaming\SlayTheSpire2\logs`）。
/// 玩家一截图发到群里/工坊反馈里，就把自己的用户名公开了 —— 本模组的日志脱敏
/// （<see cref="Redact"/>，同一件事的另一半）早就把 `C:\Users\<名字>` 换成 `&lt;userdir&gt;`，
/// **界面上也不该例外**。
///
/// 做法：把用户目录那一段换成**环境变量写法**——玩家照样能照着找（Win+R 粘 `%APPDATA%\...` 就能打开），
/// 但屏幕上不出现任何人的名字；Android / iOS 的路径里本来没有个人信息，原样返回。
/// </summary>
internal static class PathDisplay
{
    private static readonly (string Needle, string Prefix)[] Rules =
    {
        (@"AppData\Roaming", "%APPDATA%"),
        (@"AppData\Local", "%LOCALAPPDATA%"),
    };

    /// <summary>把路径里「谁的电脑」这部分抹掉（幂等，认不出来就原样返回）。</summary>
    internal static string Mask(string? path)
    {
        if (string.IsNullOrEmpty(path)) return path ?? string.Empty;
        try
        {
            string p = path!;

            // ① `…\AppData\Roaming\…` / `…/AppData/Roaming/…` → `%APPDATA%\…`（最具体，先判）
            foreach (var (needle, prefix) in Rules)
            {
                int idx = p.IndexOf(needle, StringComparison.OrdinalIgnoreCase);
                if (idx >= 0) return prefix + p.Substring(idx + needle.Length);
                string slashNeedle = needle.Replace('\\', '/');
                idx = p.IndexOf(slashNeedle, StringComparison.OrdinalIgnoreCase);
                if (idx >= 0) return prefix + p.Substring(idx + slashNeedle.Length);
            }

            // ② `C:\Users\<名字>\…` → `%USERPROFILE%\…`
            const string BackUsers = @"\Users\";
            const string FwdUsers = "/Users/";
            int users = p.IndexOf(BackUsers, StringComparison.OrdinalIgnoreCase);
            string usersNeedle = BackUsers;
            char sep = '\\';
            if (users < 0)
            {
                users = p.IndexOf(FwdUsers, StringComparison.OrdinalIgnoreCase);
                usersNeedle = FwdUsers;
                sep = '/';
            }
            if (users >= 0)
            {
                string tail = p.Substring(users + usersNeedle.Length);
                int next = tail.IndexOf(sep);
                return next < 0 ? "%USERPROFILE%" : "%USERPROFILE%" + tail.Substring(next);
            }

            // ③ 认不出来（Android / iOS 的私有目录、相对路径…）⇒ 原样返回，里面没有个人信息
            return p;
        }
        catch { return path!; }
    }
}
