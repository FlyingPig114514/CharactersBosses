using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes;

namespace CharacterBosses.Patches;

/// <summary>
/// **游戏启动流程的面包屑**（用户 2026-11-20：「我怀疑是游戏在日志记录前就崩溃了」）。
///
/// 玩家那个「致命错误！在游戏启动期间遇到了问题」是游戏自己的 `NGame` 弹的：
/// <code>
/// try { await GameStartup(); }
/// catch { _gameStartupComplete.TrySetResult(); TaskHelper.RunSafely(GameStartupError()); throw; }
/// </code>
/// 也就是说：**异常先被 catch、弹窗、再 throw**。如果进程在把自己的日志刷下去之前就没了，
/// 玩家交上来的 `godot.log` / `mod_log.txt` 里就一个字都看不到。
///
/// 所以这里在三个关键点上写**同步落盘**的面包屑（见 <c>Core.CrashTrace</c>）：
///   · `NGame._Ready` 完成 —— 节点起来了；
///   · `GameStartup` 开始 —— 真正的启动流程开始；
///   · `GameStartupError` 进入 —— **游戏马上就要弹「致命错误」了**。
/// 最后一行如果是 `GameStartupError`，就说明崩溃确实发生在启动流程里；
/// 如果 trace 停在别的某一步，那就是硬崩溃，停在哪一行就是死在哪一步。
/// </summary>
[HarmonyPatch(typeof(NGame), "GameStartup")]
internal static class GameStartupTracePatch
{
    [HarmonyPrefix]
    public static void Prefix()
    {
        try { Core.CrashTrace.Mark("NGame.GameStartup 开始"); } catch { /* 忽略 */ }
    }
}

/// <summary>`NGame.GameStartupError` 进入 ⇒ 游戏判定「启动失败」、即将弹致命错误对话框。</summary>
[HarmonyPatch(typeof(NGame), "GameStartupError")]
internal static class GameStartupErrorTracePatch
{
    [HarmonyPrefix]
    public static void Prefix()
    {
        try
        {
            Core.CrashTrace.Mark("!!! NGame.GameStartupError 进入 ⇒ 游戏即将弹「致命错误」对话框");
            Core.CrashTrace.Mark("（这条之前的最后一行 `!!` 就是被吞掉的异常；没有 `!!` 说明它不是托管异常）");
        }
        catch { /* 忽略 */ }
    }
}

/// <summary>`NGame._Ready`（整个游戏节点就绪）。</summary>
[HarmonyPatch(typeof(NGame), "_Ready")]
internal static class NGameReadyTracePatch
{
    [HarmonyPrefix]
    public static void Prefix()
    {
        try { Core.CrashTrace.Mark("NGame._Ready 进入"); } catch { /* 忽略 */ }
    }

    [HarmonyPostfix]
    public static void Postfix()
    {
        try { Core.CrashTrace.Mark("NGame._Ready 返回（未抛异常）"); } catch { /* 忽略 */ }
    }
}
