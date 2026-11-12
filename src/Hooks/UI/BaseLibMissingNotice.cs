using System;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using CharacterBosses.Core;

namespace CharacterBosses.UI;

/// <summary>
/// **本机没装前置模组 BaseLib 时，用原版弹窗提醒一次**（★ v0.2.587）。
///
/// 用户要求：
/// > 「那你先改成软依赖吧，不过要在玩家没装 baselib 时警告玩家有的功能不可用」
///
/// 用的是和「数据记录询问」**同一套原版弹窗**（`NGenericPopup` + `NModalContainer`，
/// 见 <see cref="TelemetryConsentPrompt"/>），没有自创任何 UI。
/// 只在主菜单出现时弹一次，玩家点掉之后把 `baseLibWarned` 记进配置，之后不再打扰
/// （装了 BaseLib 就自然不会弹 —— <see cref="MaybeWarn"/> 第一行就返回）。
/// </summary>
internal static class BaseLibMissingNotice
{
    /// <summary>本地化表：用我们自己随 PCK 打的 `encounters` 表（键见 content/localization/*）。</summary>
    private const string Table = "encounters";

    private static bool _asking;

    internal static void MaybeWarn()
    {
        try
        {
            if (BaseLibPresence.Available) return;          // 装了 BaseLib：什么都不做
            if (BossConfig.BaseLibWarned) return;           // 已经提醒过
            if (_asking) return;
            _asking = true;
            Logger.Log("[CharBoss] ⚠ " + BaseLibPresence.MissingHint);
            _ = WarnAsync();
        }
        catch (Exception e)
        {
            _asking = false;
            Logger.Log("[CharBoss] BaseLib 缺失提醒：跳过（" + e.Message + "）");
        }
    }

    private static async Task WarnAsync()
    {
        try
        {
            // 等主菜单那一帧过去，确保 NModalContainer 已经进树（与数据记录询问同一套）
            var tree = Engine.GetMainLoop() as SceneTree;
            if (tree != null)
            {
                var timer = tree.CreateTimer(0.8);
                if (timer != null) await tree.ToSignal(timer, SceneTreeTimer.SignalName.Timeout);
            }

            var container = NModalContainer.Instance;
            if (container == null)
            {
                // 拿不到弹窗容器：不打扰玩家，也不记「已提醒」，下次启动再试
                Logger.Log("[CharBoss] BaseLib 缺失提醒：拿不到 NModalContainer，本次跳过");
                _asking = false;
                return;
            }

            var popup = NGenericPopup.Create();
            if (popup == null)
            {
                Logger.Log("[CharBoss] BaseLib 缺失提醒：原版通用弹窗创建失败，本次跳过");
                _asking = false;
                return;
            }

            var header = new LocString(Table, "BASELIB_MISSING_HEADER");
            var body = new LocString(Table, "BASELIB_MISSING_BODY");
            var yes = new LocString(Table, "BASELIB_MISSING_OK");
            var no = new LocString(Table, "BASELIB_MISSING_CLOSE");

            container.Add(popup, showBackstop: false);
            // 只有一个「知道了」的提示框：两个按钮都只是关掉它，返回值我们不关心
            await popup.WaitForConfirmation(body, header, no, yes);

            BossConfig.SetBaseLibWarned(true);
            Logger.Log("[CharBoss] BaseLib 缺失提醒：玩家已确认（之后不再提示；装上 BaseLib 后这些功能自动恢复）");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] BaseLib 缺失提醒失败（本次跳过，下次启动再试）: " + e.Message);
        }
        finally
        {
            _asking = false;
        }
    }
}
