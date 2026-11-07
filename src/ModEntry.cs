using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;

namespace CharacterBosses;

[ModInitializer("Initialize")]
public static class ModEntry
{
    public static readonly string ModId = "CharacterBosses";
    public static readonly string ModName = "CharacterBosses";

    public static void Initialize()
    {
        var harmony = new Harmony(ModId);

        // ★ 2026-11-05（双版本适配，用户要求 v0.107 正式版 + v0.111 beta 都能用）：
        //   原版这里是 `harmony.PatchAll()` —— 它对每个补丁类调 `PatchClassProcessor.Patch()`，
        //   而**任何一个类挂不上（目标方法在新版/旧版里不存在、或重载解析不到）都会抛 HarmonyException
        //   并中断整轮补丁**，表现就是游戏里那句「模组的程序集DLL初始化失败」，且**后面的补丁一个都没装**。
        //   （实测：107 上 `JugglingNoClonePatch` 打 `JugglingPower.BeforeCardPlayed` —— 这个方法 107 的
        //     JugglingPower 没有声明（只有 111 有），Harmony 按 DeclaredOnly 解析 ⇒ 目标为 null ⇒ 整轮中断。）
        //   所以改成**逐类 try/catch**：挂不上的类记一行日志并跳过，其余补丁照常生效。
        int patched = 0, failed = 0;
        foreach (var type in AccessTools.GetTypesFromAssembly(typeof(ModEntry).Assembly))
        {
            try
            {
                var processor = harmony.CreateClassProcessor(type);
                var result = processor?.Patch();
                if (result != null) patched += result.Count;
            }
            catch (Exception e)
            {
                failed++;
                try
                {
                    Logger.Log($"[{ModName}] 补丁类 {type.Name} 挂载失败（已跳过，不影响其它补丁）: " +
                               $"{e.GetType().Name}: {e.Message}");
                }
                catch { /* 忽略 */ }
            }
        }

        Logger.Log($"[{ModName}] Mod loaded! Hooks active.（打上的补丁目标 {patched} 个，失败补丁类 {failed} 个）");

        // 启动就把「构建指纹 + 游戏版本 + 兼容层探测结果」写进 mod_log，
        // 这样排查双版本问题时不用等某个钩子先触发。
        try { Core.BuildInfo.LogOnce(); } catch { /* 忽略 */ }

#if STS2_107
        // 107 调试用：给「BeforeCardPlayed」的每个监听者挂 enter/leave 日志（定位卡死用）
        try { CharacterBosses.Patches.BeforeCardPlayedListenerTrace.Install(); } catch { /* 忽略 */ }
#endif
    }
}

public static class Logger
{
    private static readonly string LogPath;

    static Logger()
    {
        LogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SlayTheSpire2", "logs", "mod_log.txt"
        );
        Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
    }

    public static void Log(string message)
    {
        File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
    }
}

