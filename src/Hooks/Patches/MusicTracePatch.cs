using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Audio;
using CharacterBosses.Core;

namespace CharacterBosses.Patches;

/// <summary>
/// **Boss 战期间，谁在动音乐**（用户 2026-10-30：「三层 Boss 战 BGM 放一会就没了」）。
///
/// ## 为什么要这个
/// 我们能停音乐的入口只有三处（`BeforeRemovedFromRoom` 两次 + 最后一只 Boss 倒下那一帧），
/// 而那一场的日志里**一条**「BGM：最后一只 Boss 倒下」都没有 ⇒ 说明不是我们停的
/// （或者停的时候 `_playing` 已经是空的），也就是**别人**在动 `NRunMusicController`：
///   · 原版：`PlayCustomMusic` / `StopCustomMusic` / `UpdateMusic`（换 BGM，`update_music` 会顶掉当前曲目）/
///     `UpdateTrack`（`update_global_parameter("Progress", …)`）/ `StopMusic`（`stop_music` + **卸载 bank**，
///     一旦 bank 被卸，我们那条事件就再也放不出来）；
///   · 别的模组：往昔之章 / 第四幕心脏 / 崩坠 / RitsuLib 都可能自己切音乐。
///
/// ## 做法
/// **纯观察**：全是 void 前缀，从不跳过原版方法（不返回 false、不改 `__result`），
/// 只在「正在战斗或我们占着音乐」时打一行：
///   `[CharBoss] ♪ 音乐调用 UpdateMusic（调用者 …）| 曲目=… 占着=… | _currentTrack=…`
/// 其中调用者取自调用栈里第一个「不是我们、也不是 NRunMusicController」的帧，
/// 一眼就能看出是原版哪一步、还是哪个模组干的。
///
/// ⚠ 频率限制：每个方法 1.5 秒最多一行，全场上限 200 行（诊用；不会刷爆 `mod_log.txt`）。
/// </summary>
[HarmonyPatch]
internal static class MusicTrace
{
    private const long IntervalUsec = 1_500_000;   // 同一方法 1.5 秒最多一行
    private const int MaxLines = 200;              // 全场总上限

    private static readonly Dictionary<string, long> LastLogUsec = new(StringComparer.Ordinal);
    private static int _lines;

    /// <summary>只关心战斗期间（战斗外换 BGM 是正常的）。</summary>
    private static bool Wanted()
    {
        if (_lines >= MaxLines) return false;
        try { if (BossBgm.IsPlaying || BossBgm.Claiming) return true; } catch { /* 忽略 */ }
        try { return MegaCrit.Sts2.Core.Combat.CombatManager.Instance?.IsInProgress == true; }
        catch { return false; }
    }

    private static bool Due(string key)
    {
        long now = (long)Godot.Time.GetTicksUsec();
        if (LastLogUsec.TryGetValue(key, out long last) && now - last < IntervalUsec) return false;
        LastLogUsec[key] = now;
        return true;
    }

    internal static void Log(string call, NRunMusicController? controller, string detail)
    {
        try
        {
            if (!Wanted()) return;
            if (!Due(call)) return;
            if (++_lines > MaxLines)
            {
                Logger.Log($"[CharBoss] ♪ 音乐调用追踪已达上限（{MaxLines} 行），后续不再记录");
                return;
            }
            Logger.Log($"[CharBoss] ♪ 音乐调用 {call}{detail}（调用者 {Caller()}）| {BossBgm.DescribeState()}" +
                       $" | _currentTrack={CurrentTrack(controller) ?? "<空>"}");
        }
        catch { /* 忽略 */ }
    }

    /// <summary>`NRunMusicController._currentTrack`（私有字段）：`StopCustomMusic` 只有它非空才会把原版音乐放回来。</summary>
    private static string? CurrentTrack(NRunMusicController? controller)
    {
        try
        {
            if (controller == null) return null;
            _currentTrackField ??= AccessTools.Field(typeof(NRunMusicController), "_currentTrack");
            return _currentTrackField?.GetValue(controller) as string;
        }
        catch { return null; }
    }

    private static FieldInfo? _currentTrackField;

    /// <summary>调用栈里第一个「不是我们 / 不是 NRunMusicController」的帧，例如 <c>CombatManager.EndCombatInternal</c>。</summary>
    private static string Caller()
    {
        try
        {
            var trace = new StackTrace(false);
            var sb = new StringBuilder();
            int shown = 0;
            for (int i = 1; i < trace.FrameCount && shown < 3; i++)
            {
                var method = trace.GetFrame(i)?.GetMethod();
                if (method == null) continue;
                string owner = method.DeclaringType?.Name ?? "?";
                if (owner.Contains("MusicTrace") || owner == "NRunMusicController") continue;
                if (shown > 0) sb.Append(" ← ");
                sb.Append(owner).Append('.').Append(method.Name);
                shown++;
            }
            return shown == 0 ? "?" : sb.ToString();
        }
        catch { return "?"; }
    }

    // ── 原版 `NRunMusicController` 的每一个会动音乐的方法 → 只观察，不改行为 ──
    //
    // ⚠⚠ **每一个都要写死参数类型**（用户 2026-10-30 实机：模组 DLL 初始化失败）。
    //   反编译实证 `NRunMusicController` 有**两个** `UpdateTrack`：
    //     `public void UpdateTrack()` 和 `private void UpdateTrack(string label, float trackIndex)`；
    //   只写 `nameof(...UpdateTrack)` 时 Harmony 抛
    //     `HarmonyException: Ambiguous match … AmbiguousMatchException: Ambiguous match found for 'Void UpdateTrack()'`
    //   ⇒ 整个 `PatchAll` 中断，**本模组所有补丁都没挂上**，游戏弹出「程序集DLL初始化失败」。
    //   所以这里全部显式给出参数类型，绝不留歧义。

    [HarmonyPatch(typeof(NRunMusicController), nameof(NRunMusicController.PlayCustomMusic),
        new[] { typeof(string) })]
    internal static class PlayCustomMusicPatch
    {
        [HarmonyPrefix]
        public static void Prefix(NRunMusicController __instance, string customMusic)
            => Log("PlayCustomMusic", __instance, $"({"→ 播放 " + customMusic})");
    }

    [HarmonyPatch(typeof(NRunMusicController), nameof(NRunMusicController.StopCustomMusic), new Type[0])]
    internal static class StopCustomMusicPatch
    {
        [HarmonyPrefix]
        public static void Prefix(NRunMusicController __instance)
            => Log("StopCustomMusic", __instance, "（= stop_music + 按 _currentTrack 重播 + Progress=7）");
    }

    [HarmonyPatch(typeof(NRunMusicController), nameof(NRunMusicController.StopMusic), new Type[0])]
    internal static class StopMusicPatch
    {
        [HarmonyPrefix]
        public static void Prefix(NRunMusicController __instance)
            => Log("StopMusic", __instance, "（= stop_music + 停环境音 + **_currentTrack 清空 + 卸载这一幕的 bank**）");
    }

    [HarmonyPatch(typeof(NRunMusicController), nameof(NRunMusicController.UpdateMusic), new Type[0])]
    internal static class UpdateMusicPatch
    {
        [HarmonyPrefix]
        public static void Prefix(NRunMusicController __instance)
            => Log("UpdateMusic", __instance, "（换 BGM：命中新曲目会 update_music 顶掉当前曲目）");
    }

    /// <summary>⚠ `UpdateTrack` 有两份重载，**必须**指定「无参」那一份（有参那份是私有的封装）。</summary>
    [HarmonyPatch(typeof(NRunMusicController), nameof(NRunMusicController.UpdateTrack), new Type[0])]
    internal static class UpdateTrackPatch
    {
        [HarmonyPrefix]
        public static void Prefix(NRunMusicController __instance)
            => Log("UpdateTrack", __instance, "（update_global_parameter Progress：战斗开始时=Elite/Boss、结束时=CombatEnd 7）");

        /// <summary>战斗结束那一次 `UpdateTrack` 正是胜利段落的起点 —— 顺手推一下延迟还原的时钟。</summary>
        [HarmonyPostfix]
        public static void Postfix()
        {
            try { if (BossBgm.HasPendingRestore) BossBgm.TickRestore((long)Godot.Time.GetTicksUsec()); }
            catch { /* 忽略 */ }
        }
    }

    [HarmonyPatch(typeof(NRunMusicController), nameof(NRunMusicController.UpdateMusicParameter),
        new[] { typeof(string), typeof(float) })]
    internal static class UpdateMusicParameterPatch
    {
        [HarmonyPrefix]
        public static void Prefix(NRunMusicController __instance, string label, float trackIndex)
            => Log("UpdateMusicParameter", __instance, $"({label}={trackIndex})");
    }

    [HarmonyPatch(typeof(NRunMusicController), nameof(NRunMusicController.UpdateCustomTrack),
        new[] { typeof(string), typeof(float) })]
    internal static class UpdateCustomTrackPatch
    {
        [HarmonyPrefix]
        public static void Prefix(NRunMusicController __instance, string customTrack, float label)
            => Log("UpdateCustomTrack", __instance, $"({customTrack}={label})");
    }
}

/// <summary>
/// **BGM 战后延迟还原的备用驱动**（用户 2026-10-30）。
///
/// 我们自己的战斗内每帧驱动（辉星/能量球/君王剑）在**离开战斗房之后就不跑了**，
/// 而「战斗结束后再放 4 秒才还原」这件事恰好发生在那个空档里 —— 只靠它有可能还原不了，
/// 于是 Boss 的音乐会一直拖进地图。
///
/// 这里挂原版 `NMapScreen._Process`（地图界面节点在整局里常驻，`_Process` 每帧都跑；
/// 它内部自己判断可见性，战斗期间也照跑），只在**确实有待办**时才做一次 long 比较，开销可忽略。
/// </summary>
[HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Screens.Map.NMapScreen),
    nameof(MegaCrit.Sts2.Core.Nodes.Screens.Map.NMapScreen._Process), new[] { typeof(double) })]
internal static class MusicRestoreDriverPatch
{
    [HarmonyPostfix]
    public static void Postfix()
    {
        try
        {
            if (!BossBgm.HasPendingRestore) return;
            BossBgm.TickRestore((long)Godot.Time.GetTicksUsec());
        }
        catch { /* 忽略 */ }
    }
}
