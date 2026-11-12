using System;

namespace CharacterBosses.Core;

/// <summary>
/// **前置模组 BaseLib 在不在**（★ v0.2.587：从「硬依赖」改成「软依赖」）。
///
/// 背景（玩家反馈）：
///   · 一个玩家「把其它模组都去掉了」以后进第一场战斗就崩 —— 他把 BaseLib 一起删了，
///     而我们的清单原来把 BaseLib 声明成 `dependencies`，且 `BossPicksSync` 里
///     **直接 `using BaseLib.Abstracts`**（`ICustomMessage` / `CustomMessageWrapper`）；
///   · 另一个玩家装完后**主界面按钮全没了** —— 类型加载期（`ReflectionTypeLoadException`）
///     失败是发生在 Harmony 之前、我们的 catch 拦不到的，游戏会把模组标成出错、界面半初始化。
///
/// 现在：BaseLib 只影响**两个可选功能**（游戏内「模组配置」界面、多人勾选/设置同步），
/// 没有它时其余功能全部照常。任何要碰 BaseLib 类型的代码都必须：
///   ① 先问 <see cref="Available"/>；
///   ② 真正碰类型的部分放在标了 `NoInlining` 的独立方法里 ——
///      否则 JIT 编译调用方时就会因为缺类型抛 `TypeLoadException`，判断都轮不到执行。
/// </summary>
internal static class BaseLibPresence
{
    private const string AssemblyName = "BaseLib";

    private static bool? _available;

    /// <summary>
    /// 调试开关（DLL 同目录放这个文件 = **强制当成没装 BaseLib**，用来本机验证降级路径）。
    /// 玩家正常不会碰到它；自动化测试里没法真的卸载 BaseLib，所以留了这一个口子。
    /// </summary>
    private const string ForceMissingFlagFile = "charbosses_force_no_baselib.txt";

    private static bool ForcedMissing()
    {
        try
        {
            var dir = System.IO.Path.GetDirectoryName(typeof(BaseLibPresence).Assembly.Location);
            if (string.IsNullOrEmpty(dir)) return false;
            if (!System.IO.File.Exists(System.IO.Path.Combine(dir!, ForceMissingFlagFile))) return false;
            return true;
        }
        catch { return false; }
    }

    private static bool Detect()
    {
        bool found = false;
        try
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly == null) continue;
                string? name;
                try { name = assembly.GetName().Name; } catch { continue; }
                if (string.Equals(name, AssemblyName, StringComparison.OrdinalIgnoreCase)) { found = true; break; }
            }
        }
        catch { found = false; }
        return found;
    }

    /// <summary>本机进程里有没有 BaseLib（探测一次后缓存）。</summary>
    internal static bool Available
    {
        get
        {
            if (_available.HasValue) return _available.Value;
            bool found = Detect();
            if (found && ForcedMissing())
            {
                found = false;
                Logger.Log("[CharBoss] 调试开关 charbosses_force_no_baselib.txt 存在 ⇒ **强制按「没装 BaseLib」处理**" +
                           "（用于本机验证降级路径；删掉该文件即恢复）");
            }
            _available = found;
            return found;
        }
    }

    internal static bool Missing => !Available;

    /// <summary>供日志/警告用的一句话说明。</summary>
    internal const string MissingHint =
        "没装前置模组 BaseLib（创意工坊 id 3737335127）⇒ 「模组配置」界面与多人勾选同步不可用，" +
        "其余功能（Boss 替换 / 卡组 / 剧本 / 联机阵容）照常工作";
}
