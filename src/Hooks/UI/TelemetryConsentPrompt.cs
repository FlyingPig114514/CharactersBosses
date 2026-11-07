using System;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using CharacterBosses.Core;

namespace CharacterBosses.UI;

/// <summary>
/// **首次加载本模组时，用原版的通用弹窗问一次要不要记录本地游玩数据**
/// （用户 2026-10-30：「让玩家首次加载本模组时弹窗进行询问（用原版的弹窗，别自创）是否开启选项，
/// 具体文本可以参考原版的（我记得原版也会问这个）」）。
///
/// ## 用的是原版哪一个弹窗
/// 反编译 + 抽场景确认，原版自带一个「通用二选一弹窗」：
///   · 场景 `res://scenes/ui/generic_popup.tscn`（类 `MegaCrit.Sts2.Core.Nodes.Multiplayer.NGenericPopup`）
///   · 用法：`NGenericPopup.Create()` → `NModalContainer.Instance.Add(popup, showBackstop: false)`
///     → `await popup.WaitForConfirmation(body, header, noButton, yesButton)`（返回 true = 点了「是」）
///   原版自己的「放弃本局」「断线重连」这类确认框都是同一套 `NModalContainer` 机制，
///   所以我们没有自创任何 UI，只是把文字换成本模组自己的。
///
/// ## 什么时候问
/// 主菜单 `NMainMenu._Ready` 之后（`BossConfigRegisterMainMenuPatch`）——
/// 那时弹窗容器已经就绪，也不会打断战斗。
///
/// ## 问完记什么
/// `BossConfig.SetConsentAsked(true)` + `SetConsentVersion(ConsentVersionCurrent)`：
/// 问过一次就不再弹（无论选是还是否）。选「是」→ `BossConfig.SetTelemetry(true)`（并写一条 consent 记录）；
/// 若作者配置了收集端点（v0.2.263 起是内置的飞书多维表格），**同时** `SetTelemetryUpload(true)`；
/// 选「否」→ 两者都保持关闭。之后随时可以在「模组配置 → CharacterBosses → A5」改。
/// </summary>
internal static class TelemetryConsentPrompt
{
    /// <summary>本地化表名：我们用**自己已经随 PCK 打包**的 `encounters` 表（键见 content/localization/*/encounters.json）。</summary>
    private const string Table = "encounters";

    private static bool _asking;

    /// <summary>
    /// 该不该问：还要问（没问过，或老版弹窗的文本版本已过期）、且没有正在问。
    /// （配置读写失败一律当作「别问」，绝不影响启动）
    /// </summary>
    internal static void MaybeAsk()
    {
        try
        {
            if (_asking) return;
            if (!BossConfig.NeedsConsentPrompt) return;
            _asking = true;
            _ = AskAsync();
        }
        catch (Exception e)
        {
            _asking = false;
            Logger.Log("[CharBoss] 数据记录询问：跳过（" + e.Message + "）");
        }
    }

    private static async Task AskAsync()
    {
        try
        {
            // 等主菜单那一帧过去，确保 NModalContainer 已经进树
            var tree = Engine.GetMainLoop() as SceneTree;
            if (tree != null)
            {
                var timer = tree.CreateTimer(0.8);
                if (timer != null) await tree.ToSignal(timer, SceneTreeTimer.SignalName.Timeout);
            }

            var container = NModalContainer.Instance;
            if (container == null)
            {
                // 没有弹窗容器（极端情况）⇒ 不打扰玩家，也不标记「已问过」，下次启动再试
                Logger.Log("[CharBoss] 数据记录询问：拿不到 NModalContainer，本次跳过");
                _asking = false;
                return;
            }

            var popup = NGenericPopup.Create();
            if (popup == null)
            {
                Logger.Log("[CharBoss] 数据记录询问：原版通用弹窗创建失败，本次跳过");
                _asking = false;
                return;
            }

            var header = Loc("TELEMETRY_PROMPT_HEADER", "UPLOAD_GAMEPLAY_DATA_HEADER");
            var body = Loc("TELEMETRY_PROMPT_BODY", "UPLOAD_GAMEPLAY_DATA_DESCRIPTION");
            var yes = Loc("TELEMETRY_PROMPT_YES", "YES");
            var no = Loc("TELEMETRY_PROMPT_NO", "NO");

            container.Add(popup, showBackstop: false);
            bool accepted = await popup.WaitForConfirmation(body, header, no, yes);

            BossConfig.SetConsentAsked(true);       // 问过一次就不再问（是/否都算）
            BossConfig.SetConsentVersion(BossConfig.ConsentVersionCurrent);   // 这一版弹窗的答案已给出
            BossConfig.SetTelemetry(accepted);

            // 作者配置了收集端点（内置飞书多维表格 / 通用 URL / 群机器人）、玩家又选了「开启」
            // ⇒ 一并同意上传（弹窗正文里已经写明）。
            // ★ v0.2.263：以前这里**只认**通用 `telemetryUrl`，飞书那几项永远走不到
            //   ⇒ 玩家点了「是」也只有本机记录、表里一条都没有（用户 2026-11-01 的实况）。
            bool uploaded = false;
            if (accepted && BossConfig.AnyUploadEndpoint)
            {
                BossConfig.SetTelemetryUpload(true);
                uploaded = BossConfig.TelemetryUpload;
            }

            Logger.Log($"[CharBoss] 数据记录询问：玩家选择「{(accepted ? "开启" : "保持关闭")}」" +
                       (uploaded ? "（含上传给作者）" : "（只写本机）") +
                       "（之后可在 模组配置 → CharacterBosses → A5 改）");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 数据记录询问失败（本次跳过，下次启动再问）: " + e.Message);
        }
        finally
        {
            _asking = false;
        }
    }

    /// <summary>取本地化字符串：优先我们自己那份，取不到再退到原版同名文案（原版 settings_ui 里那一对）。</summary>
    private static LocString Loc(string ourKey, string vanillaKey)
    {
        try
        {
            if (LocString.GetIfExists(Table, ourKey) != null) return new LocString(Table, ourKey);
        }
        catch { /* 继续退 */ }
        try
        {
            if (LocString.GetIfExists("settings_ui", vanillaKey) != null)
                return new LocString("settings_ui", vanillaKey);
        }
        catch { /* 继续退 */ }
        // 都没有：用我们自己的键（显示成 key，但至少不会崩）
        return new LocString(Table, ourKey);
    }
}
