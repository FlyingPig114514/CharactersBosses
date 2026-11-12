using System;
using System.Runtime.CompilerServices;
using BaseLib.Config;
using CharacterBosses.Core;

namespace CharacterBosses.Config;

/// <summary>
/// 把本模组的配置注册进 BaseLib 的「模组配置」子菜单（主菜单 → 模组配置）。
///
/// ⚠ **BaseLib 是可选依赖**：它虽然被多数整合包/崩坠带着，但不能假定人人都装。
///   所以这里先看进程里到底有没有 BaseLib 这个程序集，**没有就静默跳过**
///   （那种情况下配置界面不可用，但手改 `charbosses_config.json` 照样生效）。
///   真正碰 BaseLib 类型的那段单独放在 <see cref="RegisterCore"/> 里并标了
///   <c>NoInlining</c>，保证没有 BaseLib 时它根本不会被 JIT 到 —— 否则会抛
///   <c>TypeLoadException</c> 把整个模组的初始化带崩。
/// </summary>
internal static class BossConfigBaseLibBootstrap
{
    private static bool _registered;
    private static bool _reportedMissing;

    /// <summary>幂等；可以由多个启动切点重复调用。</summary>
    internal static void TryInstall()
    {
        if (_registered) return;
        try
        {
            if (!BaseLibLoaded())
            {
                if (!_reportedMissing)
                {
                    _reportedMissing = true;
                    Logger.Log("[CharBoss] 没检测到 BaseLib ⇒ 配置界面不可用" +
                               "（仍然可以手改 mods\\CharacterBosses\\charbosses_config.json）");
                }
                return;
            }

            RegisterCore();
            _registered = true;
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] BaseLib 配置注册失败（不影响游戏）: " + e.Message);
        }
    }

    /// <summary>
    /// 本机有没有 BaseLib —— ★ v0.2.587 起统一问 <see cref="Core.BaseLibPresence"/>
    /// （以前这里自己扫一遍，和别处的判断可能不一致；调试开关 `charbosses_force_no_baselib.txt` 也只有那边认）。
    /// </summary>
    private static bool BaseLibLoaded() => Core.BaseLibPresence.Available;

    /// <summary>真正碰 BaseLib 的那一段（没有 BaseLib 时绝不进入）。</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RegisterCore()
    {
        var config = new BossConfigModConfig();
        ModConfigRegistry.Register("CharacterBosses", config);
        Logger.Log("[CharBoss] 已把配置注册进 BaseLib：主菜单 →「模组配置」→ CharacterBosses");
    }

    /// <summary>
    /// **调试用**：DLL 同目录放一个 <c>charbosses_open_config.txt</c>，主菜单一出现就自动打开
    /// BaseLib 的「模组配置」子菜单 —— 纯粹是为了「改完能自己验证一眼」，正常玩不需要它。
    /// （自动化验证时没法可靠地给游戏窗口点鼠标/走键盘焦点，这个开关绕开了那件事。）
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void MaybeAutoOpen(Godot.Node? mainMenu)
    {
        try
        {
            var dir = System.IO.Path.GetDirectoryName(typeof(BossConfigBaseLibBootstrap).Assembly.Location);
            if (string.IsNullOrEmpty(dir)) return;
            if (!System.IO.File.Exists(System.IO.Path.Combine(dir!, AutoOpenFlagFile))) return;

            // ⚠ 不能在主菜单 `_Ready` 里立刻推子菜单：那时菜单栈还没排过版，
            //   BaseLib 的右侧滚动区会算成「内容比视口宽」⇒ 面板整体右移、按钮跑到屏幕外
            //   （实测就是这样）。等 2 秒、菜单彻底显示出来再推，和玩家自己点进去一致。
            var tree = mainMenu?.GetTree();
            if (tree == null) { Logger.Log("[CharBoss] 自动打开配置：拿不到 SceneTree"); return; }

            tree.CreateTimer(2.0).Timeout += () =>
            {
                try
                {
                    var stack = (mainMenu as MegaCrit.Sts2.Core.Nodes.Screens.MainMenu.NMainMenu)?.SubmenuStack;
                    if (stack == null) { Logger.Log("[CharBoss] 自动打开配置：拿不到 SubmenuStack"); return; }
                    stack.PushSubmenuType<BaseLib.Config.UI.NModConfigSubmenu>();
                    Logger.Log("[CharBoss] 调试：已自动打开 BaseLib 的「模组配置」子菜单");
                }
                catch (Exception e) { Logger.Log("[CharBoss] 自动打开配置失败: " + e.Message); }
            };
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 自动打开配置（登记）失败: " + e.Message);
        }
    }

    /// <summary>自动打开配置界面的开关文件名（放在 DLL 同目录）。</summary>
    internal const string AutoOpenFlagFile = "charbosses_open_config.txt";
}
