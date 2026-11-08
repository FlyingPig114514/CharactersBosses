using System;
using System.Linq;

namespace CharacterBosses.Core;

/// <summary>
/// 和「怪物乱斗 / 斗蛐蛐（CricketFightMod）」的联动检测。
///
/// 用户 2026-11-08：配置界面里专门开一节斗蛐蛐的设置，**只有装了怪物乱斗才显示**；
/// 没装就整节不出现，避免给不玩斗蛐蛐的人塞一堆看不懂的选项。
/// </summary>
internal static class CricketCompat
{
    private static bool? _installed;
    private static string _version = "";

    /// <summary>本机有没有装斗蛐蛐（按已加载程序集判断，比扫目录可靠）。</summary>
    public static bool Installed
    {
        get
        {
            if (_installed is bool known) return known;
            bool found = false;
            try
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    string? name = null;
                    try { name = asm.GetName().Name; } catch { /* 动态程序集 */ }
                    if (name != null && name.Equals("CricketFightMod", StringComparison.OrdinalIgnoreCase))
                    {
                        found = true;
                        try { _version = asm.GetName().Version?.ToString() ?? ""; } catch { /* 忽略 */ }
                        break;
                    }
                }
            }
            catch { /* 忽略 */ }

            _installed = found;
            return found;
        }
    }

    /// <summary>斗蛐蛐版本号（拿不到就是空串），只用于配置界面显示。</summary>
    public static string Version => Installed ? _version : "";
}
