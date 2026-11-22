using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using CharacterBosses.Core;

namespace CharacterBosses.Patches;

/// <summary>
/// 启动时把本模组的配置注册进 BaseLib 的「模组配置」子菜单（用户 2026-10-28 要求改用 BaseLib）。
///
/// **两个切点都挂**（注册本身幂等）：
///   · <c>NMainMenu._Ready</c> —— 主菜单一出现就注册。这是可靠的那个：模组是在
///     `NGame` 启动流程里加载的，等我们的 Harmony 补丁生效时 `NGame._Ready` 已经在执行，
///     那一次调用**不会**再走到新加的后缀（实测：只挂 NGame._Ready 时日志里一条注册都没有）。
///     BaseLib 自己也挂在这里（`BaseLib.Config.NMainMenu_Ready_Patch`），说明这个点可用；
///   · <c>NGame._Ready</c> —— 万一将来加载顺序变成「补丁先上、_Ready 后跑」，这个也能兜住。
///
/// 没装 BaseLib 时 <see cref="CharacterBosses.Config.BossConfigBaseLibBootstrap.TryInstall"/>
/// 会静默跳过（详见那边的注释），这两个切点都不会有副作用。
/// </summary>
[HarmonyPatch(typeof(NMainMenu), nameof(NMainMenu._Ready))]
public static class BossConfigRegisterMainMenuPatch
{
    [HarmonyPostfix]
    public static void Postfix(NMainMenu __instance)
    {
        // ★ v0.2.874（作者 2026-11-22：「新上传的数据只有我自己打的，都有玩家直播玩了」）：
        //   弹「要不要上传数据」的窗口、以及配置注册，全都挂在这一个补丁上。
        //   如果游戏改版后 `NMainMenu._Ready` 变了（Harmony 整类失败）或者别的补丁把这里挡掉，
        //   **新玩家就永远等不到那个弹窗** ⇒ 他们不会同意上传 ⇒ 表里只有作者自己的数据。
        //   作者本机看不出问题（他的 cfg 早就 asked=true）。
        //   ⇒ 这里打一行「补丁已生效」，配合 `数据记录询问：…` 那几行就能一眼分清
        //     「补丁没上」还是「弹窗没弹」还是「玩家选了否」。
        LogPatchAliveOnce();

        // 先把配置读一遍：顺手把旧版 `charbosses_config.json` 迁成 `.cfg` 并删掉旧文件
        //（游戏扫描 mod 目录时会把每个 *.json 当清单反序列化，旧文件会一直报警）。
        try { CharacterBosses.Core.BossConfig.EnsureLoaded(); }
        catch (Exception e) { Logger.Log("[CharBoss] 配置预加载（主菜单）失败（忽略）: " + e.Message); }

        try { CharacterBosses.Config.BossConfigBaseLibBootstrap.TryInstall(); }
        catch (Exception e) { Logger.Log("[CharBoss] 配置注册（主菜单）失败（忽略）: " + e.Message); }

        // 调试开关（DLL 同目录 charbosses_open_config.txt）：主菜单一出现就自动打开配置界面
        try { CharacterBosses.Config.BossConfigBaseLibBootstrap.MaybeAutoOpen(__instance); }
        catch (Exception e) { Logger.Log("[CharBoss] 自动打开配置（补丁）失败（忽略）: " + e.Message); }

        // 首次加载本模组：用**原版的通用弹窗**问一次「要不要记录本地游玩数据」
        //（用户 2026-10-30；默认关，问过一次就记下来，以后不再打扰。见 UI/TelemetryConsentPrompt.cs）
        try { CharacterBosses.UI.TelemetryConsentPrompt.MaybeAsk(); }
        catch (Exception e) { Logger.Log("[CharBoss] 数据记录询问（补丁）失败（忽略）: " + e.Message); }

        // ★ v0.2.587：没装前置 BaseLib 时，用同一个原版弹窗**提醒一次**哪些功能不可用
        //（用户：「改成软依赖，但要在玩家没装 baselib 时警告玩家有的功能不可用」）
        try { CharacterBosses.UI.BaseLibMissingNotice.MaybeWarn(); }
        catch (Exception e) { Logger.Log("[CharBoss] BaseLib 缺失提醒（补丁）失败（忽略）: " + e.Message); }
    }

    private static bool _aliveLogged;

    /// <summary>
    /// 只打一次：证明「主菜单这个切点」在我们的模组里确实跑到了。
    ///
    /// 为什么需要它（v0.2.874）：数据上传全靠这里弹出的同意弹窗。玩家反馈「表里只有作者自己的数据」时，
    /// 有这一行就能立刻分清三种情况：
    ///   · 日志里**没有**这一行 ⇒ 补丁没生效（游戏改版后 `NMainMenu._Ready` 变了 / 被别的模组挡住）；
    ///   · 有这一行、但没有 `数据记录询问：…` ⇒ 询问逻辑自己早退了；
    ///   · 有 `数据记录询问：玩家选择…` ⇒ 弹窗正常，玩家选了「否」。
    /// </summary>
    private static void LogPatchAliveOnce()
    {
        try
        {
            if (_aliveLogged) return;
            _aliveLogged = true;
            Logger.Log("[CharBoss] 主菜单补丁已生效（配置注册 / 数据记录询问 / BaseLib 提醒都在这一帧跑）");
        }
        catch { /* 纯日志 */ }
    }
}

/// <summary>备用切点：游戏根节点 Ready。</summary>
[HarmonyPatch(typeof(NGame), nameof(NGame._Ready))]
public static class BossConfigRegisterPatch
{
    [HarmonyPostfix]
    public static void Postfix()
    {
        try { CharacterBosses.Config.BossConfigBaseLibBootstrap.TryInstall(); }
        catch (Exception e) { Logger.Log("[CharBoss] 配置注册（根节点）失败（忽略）: " + e.Message); }
    }
}
