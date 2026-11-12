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
