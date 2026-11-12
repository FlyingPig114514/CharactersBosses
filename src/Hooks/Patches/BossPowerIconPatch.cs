using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using CharacterBosses.Powers;

namespace CharacterBosses.Patches;

/// <summary>
/// **借用原版的 Power 图标**（用户 2026-10-26：命运同担 → 「好奇」的图标；玩家身上那层 → 「抑制」的图标）。
///
/// 原版取图标的规则（反编译 `PowerModel`）：
/// <code>
/// public string PackedIconPath =&gt; ImageHelper.GetImagePath("atlases/power_atlas.sprites/" + Id.Entry.ToLower() + ".tres");
/// private string BigIconPath    =&gt; ImageHelper.GetImagePath("powers/" + Id.Entry.ToLower() + ".png");
/// public string ResolvedBigIconPath { ... 找不到就退回 powers/missing_power.png ... }
/// public Texture2D Icon =&gt; ResourceLoader.Load&lt;Texture2D&gt;(PackedIconPath, ...);
/// </code>
/// 也就是说图标**完全由 `Id.Entry` 拼路径** —— 我们的 `SHARED_FATE_POWER` 在游戏的美术包里当然没有，
/// 于是状态栏上是个空图标。
///
/// 我们不自己画图（packed atlas 的 `.tres` 是 Godot 导入产物，手搓容易出错），
/// 而是在这两个 getter 上挂钩子：**只对我们的 Power** 换成原版那张已经存在的图标。
/// `Icon` 是从 `PackedIconPath` 现算的、`BigIcon` 是从 `ResolvedBigIconPath` 现算的
/// ⇒ 改这两个 getter 就把两处都覆盖了，而且**只影响这两个类型**（其它 Power 零回归）。
/// </summary>
internal static class BossPowerIcons
{
    /// <summary>我们的 Power 类型 → 借用哪张原版图标（Power 的 `Id.Entry`，小写后就是文件名）。</summary>
    internal static string? VanillaEntryOf(PowerModel? power)
    {
        try
        {
            return power switch
            {
                SharedFatePower => "CURIOUS_POWER",          // 好奇
                SharedFateDiscardPower => "DAMPEN_POWER",    // 抑制
                BossCoolantPower => "CURIOUS_POWER",         // 冷却剂 → 也用「好奇」（用户 2026-10-27）
                _ => null,
            };
        }
        catch { return null; }
    }

    /// <summary>
    /// 用**我们自己打包的 PNG** 当图标的 Power（不走原版美术包）。
    ///
    /// 目前只有「眼部攻击」buff：用户 2026-10-27 要求用**这张牌的卡图**，并要求「大小压缩一下」。
    /// 卡图原图是 1000×760，直接当图标会糊出画布，所以用
    /// `tools/art/extract_card_icon.gd` 从游戏 PCK 里抽出来缩成 **128×128** 再随 PCK 打包
    /// （见 `tools/art/pck_extras.txt`）。
    /// </summary>
    internal static string? DirectPathOf(PowerModel? power)
    {
        try
        {
            if (power is GoForTheEyesAimPower) return "res://CharacterBosses/images/powers/go_for_the_eyes.png";
            return null;
        }
        catch { return null; }
    }
}

/// <summary>状态栏/器械上的小图标（`PowerModel.Icon` 读的就是它）。</summary>
[HarmonyPatch(typeof(PowerModel), nameof(PowerModel.PackedIconPath), MethodType.Getter)]
public static class BossPowerSmallIconPatch
{
    [HarmonyPostfix]
    public static void Postfix(PowerModel __instance, ref string __result)
    {
        try
        {
            string? direct = BossPowerIcons.DirectPathOf(__instance);
            if (direct != null) { __result = direct; return; }

            string? entry = BossPowerIcons.VanillaEntryOf(__instance);
            if (entry == null) return;

            // ★ v0.2.588（CVC-跨版本兼容补丁的兼容）：`PowerModel.get_PackedIconPath` 这个 getter
            //   **三个模组同时在打**（CVC 报告原文：`[postfix] get_PackedIconPath — BaseLib; CharacterBosses;
            //   RitsuLib.framework-content-assets`）。postfix 谁最后跑谁说了算 ⇒ 我们只在「该改」时改：
            //   ① 只处理**本 mod 的 Power**（上面 VanillaEntryOf 已经保证）；
            //   ② 若当前的 __result 已经是一个**真实存在的资源**（别的模组已经解析好了），就不覆盖它，
            //      避免把别人的图标抢成我们的。
            string candidate = ImageHelper.GetImagePath(
                "atlases/power_atlas.sprites/" + entry.ToLowerInvariant() + ".tres");
            if (!string.IsNullOrEmpty(__result) && Godot.ResourceLoader.Exists(__result)) return;
            __result = candidate;
        }
        catch { /* 取不到就保持原样（会显示空图标，但不影响战斗） */ }
    }
}

/// <summary>悬浮说明里的**大图**（`PowerModel.BigIcon` 读的就是它）。</summary>
[HarmonyPatch(typeof(PowerModel), nameof(PowerModel.ResolvedBigIconPath), MethodType.Getter)]
public static class BossPowerBigIconPatch
{
    [HarmonyPostfix]
    public static void Postfix(PowerModel __instance, ref string __result)
    {
        try
        {
            string? direct = BossPowerIcons.DirectPathOf(__instance);
            if (direct != null) { __result = direct; return; }

            string? entry = BossPowerIcons.VanillaEntryOf(__instance);
            if (entry == null) return;

            // ★ v0.2.588：`get_ResolvedBigIconPath` 同样是**三个模组共用**的补丁点
            //   （CVC 报告：`CharacterBosses; RitsuLib.framework-content-assets; SteamRandomMatchMod`）⇒
            //   别人已经解析出真实资源时不动它。
            //
            //   ⚠ 但原版这个 getter **自带兜底**：反编译 `PowerModel.ResolvedBigIconPath` 是
            //   `powers/<id>.png` → `powers/beta/<id>.png` → `powers/missing_power.png`，
            //   而 `missing_power.png` 是**真实存在的资源** ⇒ 光看「Exists 就放过」会把我们自己的
            //   Power 一律留在「缺失图标」上（回归）。所以判定要排除原版那两条兜底路径。
            string lower = entry.ToLowerInvariant();
            string candidate = ImageHelper.GetImagePath("powers/" + lower + ".png");
            string vanillaDefault = ImageHelper.GetImagePath("powers/" + __instance.Id.Entry.ToLowerInvariant() + ".png");
            string missing = ImageHelper.GetImagePath("powers/missing_power.png");
            bool foreignResolved = !string.IsNullOrEmpty(__result)
                                   && Godot.ResourceLoader.Exists(__result)
                                   && !string.Equals(__result, missing, StringComparison.Ordinal)
                                   && !string.Equals(__result, vanillaDefault, StringComparison.Ordinal);
            if (foreignResolved) return;
            __result = candidate;
        }
        catch { /* 同上 */ }
    }
}
