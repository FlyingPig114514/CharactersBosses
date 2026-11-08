using System;
using System.Collections.Generic;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;

namespace CharacterBosses.Patches;

/// <summary>
/// 「**结算/历史/顶栏**」不再为我们的遭遇去加载不存在的 `ui/run_history/&lt;id&gt;.png`。
///
/// ## 背景
/// 游戏为每个遭遇拼一条 `res://images/ui/run_history/&lt;遭遇 id 小写&gt;.png`（还有 `_outline.png`）。
/// 我们的 23 个遭遇没有这套美术，于是刷 `No loader found for resource: ...` 错误。
///
/// ## ⚠ 2026-10-28 血泪教训：**绝对不许返回 null**
/// 第一版为了省事直接 `path = null`（指望原版走「隐藏图标」分支），结果**开局必崩**：
/// <code>
/// [ERROR] System.ArgumentNullException: Value cannot be null. (Parameter 'key')
///    at System.Collections.Concurrent.ConcurrentDictionary`2.TryGetValue
///    at MegaCrit.Sts2.Core.Assets.AssetCache.GetAsset(String path)
///    at MegaCrit.sts2.Core.Nodes.TopBar.NTopBarBossIcon.RefreshBossIcon_Patch1()
///    at MegaCrit.sts2.Core.Nodes.CommonUi.NTopBar.Initialize_Patch4()
///    at MegaCrit.sts2.Core.Nodes.CommonUi.NGlobalUi.Initialize(RunState)
///    at MegaCrit.Sts2.Core.Nodes.NRun._Ready()
/// [ERROR] Exception starting singleplayer run : System.NullReferenceException
///    at MegaCrit.Sts2.Core.Nodes.Screens.Map.NMapScreen.SetMap_Patch1(...)   ← 连锁反应
/// </code>
/// 原因：**顶栏那颗 Boss 图标不判空**，直接把路径塞给
/// `PreloadManager.Cache.GetTexture2D(path)`；`null` 一路进到
/// `ConcurrentDictionary.TryGetValue(null)` 就抛 `ArgumentNullException`，
/// 把 `NRun._Ready` 打断 ⇒ 运行界面半初始化 ⇒ 地图 `SetMap` 接着 NRE ⇒ 整局开不起来。
/// （`NTopBarRoomIcon.UpdateIcon`、历史条目是判空的，只有 `NTopBarBossIcon` 没判 —— 所以以前只在结算界面刷日志才没暴露。）
///
/// ## 现在的做法
/// 找不到就**换成「原版占位 Boss 图标」的路径**（同一批 `ui/run_history/` 美术、同一宽高比，
/// 顶栏/地图上用着不违和，和 <see cref="TopBarBossIconFallbackPatch"/> ③ 用的是同一张）。
/// 万一连占位图都取不到，就**原样返回**（宁可让原版刷一条加载警告，也绝不交 null 出去）。
/// </summary>
internal static class OurEncounterIconPaths
{
    private static HashSet<string>? _ourEntries;

    /// <summary>自己查占位图时用于防重入（占位图不是我们的遭遇，本来也会走空，这里再加一道保险）。</summary>
    private static bool _resolvingFallback;

    private static bool _fallbackReady;
    private static string? _fallbackIcon;
    private static string? _fallbackOutline;

    /// <summary>我们所有档位遭遇的 id（惰性建一次）。</summary>
    internal static bool IsOurEncounter(ModelId? modelId)
    {
        try
        {
            string? entry = modelId?.Entry;
            if (string.IsNullOrEmpty(entry)) return false;

            if (_ourEntries == null)
            {
                var set = new HashSet<string>(StringComparer.Ordinal);
                foreach (var enc in Encounters.CharBossEncounters.AllTierEncounters())
                {
                    if (enc == null) continue;
                    try { set.Add(enc.Id.Entry); } catch { /* 忽略 */ }
                }
                _ourEntries = set;
            }
            return _ourEntries.Contains(entry!);
        }
        catch { return false; }
    }

    /// <summary>
    /// 我们的遭遇 + 贴图确实不存在 ⇒ 换成原版占位 Boss 图标的路径。
    /// **无论如何都不会把 <paramref name="path"/> 变成 null。**
    /// </summary>
    internal static void Fix(ref string? path, ModelId? modelId, bool outline)
    {
        try
        {
            if (_resolvingFallback) return;                  // 防重入
            if (string.IsNullOrEmpty(path)) return;
            if (!IsOurEncounter(modelId)) return;
            if (Godot.ResourceLoader.Exists(path)) return;    // 以后我们补了美术就自动恢复正常

            // ★ v0.2.498（用户 2026-11-08：「历史记录里 boss 头像还是同族小队的，改成对应角色的头像」）：
            //   以前这里直接换成**原版占位图**（TheKinBoss）⇒ 历史记录那排全是同族小队脸。
            //   现在优先用**我们自己的地图徽章**（每个遭遇的 `BossNodePath + ".png"`，
            //   那张图就在我们 pck 里），取不到才退回原版占位图。
            string? ours = OurBadge(modelId, outline);
            if (ours != null)
            {
                if (Logged.Add(path!))
                    Logger.Log($"[CharBoss] 结算/历史图标 → 换成我们自己的角色徽章（{path} → {ours}）");
                path = ours;
                return;
            }

            string? substitute = Fallback(outline);
            if (substitute == null)
            {
                // 占位图也取不到：保持原路径不动（原版只会刷一条加载警告，不会崩）
                if (Logged.Add(path!)) Logger.Log($"[CharBoss] 结算/顶栏图标缺图且无占位图，保持原路径（{path}）");
                return;
            }

            if (Logged.Add(path!))
                Logger.Log($"[CharBoss] 结算/顶栏图标缺图 → 改用原版占位图标（{path} → {substitute}）");
            path = substitute;
        }
        catch { /* 出错就保持原样 */ }
    }

    /// <summary>
    /// 这套遭遇**自己的**徽章图（`BossNodePath + ".png"`，全在 `res://CharacterBosses/images/map/` 里）。
    /// 描边优先用 `_outline.png`，没有就用同一张图。找不到返回 null。
    /// </summary>
    private static string? OurBadge(ModelId? modelId, bool outline)
    {
        try
        {
            string? entry = modelId?.Entry;
            if (string.IsNullOrEmpty(entry)) return null;
            foreach (var enc in Encounters.CharBossEncounters.AllTierEncounters())
            {
                if (enc == null) continue;
                string id;
                try { id = enc.Id.Entry; } catch { continue; }
                if (!string.Equals(id, entry, StringComparison.Ordinal)) continue;

                string basePath;
                try { basePath = enc.BossNodePath; } catch { continue; }
                if (string.IsNullOrEmpty(basePath)) continue;

                string png = basePath + ".png";
                if (ResourceLoader.Exists(png))
                {
                    if (!outline) return png;
                    string outlinePng = basePath + "_outline.png";
                    if (ResourceLoader.Exists(outlinePng)) return outlinePng;
                    return png;
                }
            }
        }
        catch { /* 忽略 */ }
        return null;
    }
    /// <summary>原版占位 Boss 图标（TheKinBoss）的同款路径；取不到就返回 null。</summary>
    private static string? Fallback(bool outline)
    {
        if (!_fallbackReady)
        {
            // 只在**真的取到**占位图后锁定结果：万一第一次调用发生在 ModelDb 就绪前，
            // 下一次还会再试一次，不至于把这辈子都记成「没有占位图」。
            try
            {
                var kin = ModelDb.Encounter<MegaCrit.Sts2.Core.Models.Encounters.TheKinBoss>();
                if (kin != null)
                {
                    _resolvingFallback = true;
                    try
                    {
                        _fallbackIcon = SafePath(ImageHelper.GetRoomIconPath(
                            MapPointType.Boss, RoomType.Boss, kin.Id));
                        _fallbackOutline = SafePath(ImageHelper.GetRoomIconOutlinePath(
                            MapPointType.Boss, RoomType.Boss, kin.Id));
                    }
                    finally { _resolvingFallback = false; }
                }
            }
            catch { /* 忽略，留 null */ }
            _fallbackReady = _fallbackIcon != null;
        }
        return outline ? _fallbackOutline : _fallbackIcon;
    }

    private static string? SafePath(string? path)
    {
        try
        {
            if (string.IsNullOrEmpty(path)) return null;
            return Godot.ResourceLoader.Exists(path) ? path : null;
        }
        catch { return null; }
    }

    private static readonly HashSet<string> Logged = new(StringComparer.Ordinal);
}

[HarmonyPatch(typeof(ImageHelper), nameof(ImageHelper.GetRoomIconPath))]
internal static class OurEncounterIconPathPatch
{
    [HarmonyPostfix]
    public static void Postfix(ModelId? modelId, ref string? __result)
        => OurEncounterIconPaths.Fix(ref __result, modelId, outline: false);
}

[HarmonyPatch(typeof(ImageHelper), nameof(ImageHelper.GetRoomIconOutlinePath))]
internal static class OurEncounterIconOutlinePathPatch
{
    [HarmonyPostfix]
    public static void Postfix(ModelId? modelId, ref string? __result)
        => OurEncounterIconPaths.Fix(ref __result, modelId, outline: true);
}
