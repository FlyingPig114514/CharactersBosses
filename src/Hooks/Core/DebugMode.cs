using System;
using System.IO;

namespace CharacterBosses.Core;

/// <summary>
/// 开发开关：mod 目录下存在 <c>charbosses_debug.txt</c> 时，
/// 第一幕的第一场普通战斗也会换成角色 Boss —— 不用打通整幕就能测试。
///
/// 文件内容决定换哪个角色（第一行，大小写不敏感）：
///   · 空文件 / 不认识的词 → 铁甲战士（交付的样板）
///   · ironclad / silent / defect / necrobinder / regent → 指定角色
///   · cycle → 每次普通战斗依次轮换 5 个角色（一次会话就能把 5 个都看一遍）
///   · 角色名后面可以跟 <c>@幕数</c>（1 起）强制档位，例如 <c>silent@3</c> = 用第三幕的卡组/剧本/血量。
///     没有它就只能真打到第三幕才能验证第三幕的设计。
/// </summary>
public static class DebugMode
{
    public const string FileName = "charbosses_debug.txt";
    public const string Cycle = "cycle";

    /// <summary>按角色下标排列的关键字，供内容解析用。</summary>
    public static readonly string[] CharacterKeys =
    {
        "ironclad", "silent", "defect", "necrobinder", "regent",
    };

    private static bool _loaded;
    private static string? _content;
    private static int _cycleIndex;
    private static int _forcedActIndex = -2;   // -2 = 还没解析

    /// <summary>调试开关是否打开。</summary>
    public static bool Enabled => Content != null;

    /// <summary>
    /// 调试开关强制指定的幕档位（0 起，-1 = 没指定）。写在角色名后面：<c>silent@3</c> → 2。
    /// </summary>
    public static int ForcedActIndex
    {
        get
        {
            _ = Content;   // 触发解析
            return _forcedActIndex < 0 ? -1 : _forcedActIndex;
        }
    }

    /// <summary>文件内容（首行、去空白、转小写）；文件不存在则为 null。</summary>
    public static string? Content
    {
        get
        {
            if (!_loaded)
            {
                _loaded = true;
                _content = ReadContent();
            }
            return _content;
        }
    }

    /// <summary>请求的角色下标；-1 表示没有指定具体角色。</summary>
    public static int RequestedCharacter
    {
        get
        {
            var content = Content;
            if (string.IsNullOrEmpty(content)) return -1;
            var index = Array.IndexOf(CharacterKeys, content);
            return index;
        }
    }

    /// <summary>是否处于「轮换」模式。</summary>
    public static bool IsCycle => Content == Cycle;

    /// <summary>轮换模式下的下一个下标。</summary>
    public static int NextCycleIndex(int count)
    {
        if (count <= 0) return 0;
        var index = _cycleIndex % count;
        _cycleIndex++;
        return index;
    }

    private static string? ReadContent()
    {
        try
        {
            var dir = Path.GetDirectoryName(typeof(DebugMode).Assembly.Location);
            if (string.IsNullOrEmpty(dir)) return null;

            var path = Path.Combine(dir, FileName);
            if (!File.Exists(path)) return null;

            var text = File.ReadAllText(path).Trim();
            var firstLine = text.Split('\n')[0].Trim().ToLowerInvariant();

            // 角色名后面可以跟 @幕数（1 起）：silent@3 → 角色 silent + 强制第三幕档位
            var at = firstLine.IndexOf('@');
            if (at >= 0)
            {
                var actPart = firstLine[(at + 1)..].Trim();
                firstLine = firstLine[..at].Trim();
                if (int.TryParse(actPart, out var act) && act >= 1 && act <= 3)
                    _forcedActIndex = act - 1;
            }
            else
            {
                _forcedActIndex = -1;
            }

            Logger.Log($"[CharBoss] debug mode ON, content='{firstLine}'" +
                       (_forcedActIndex >= 0 ? $" 强制幕档={_forcedActIndex + 1}" : string.Empty));
            return firstLine;
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] debug mode read failed: " + e.Message);
            return null;
        }
    }
}
