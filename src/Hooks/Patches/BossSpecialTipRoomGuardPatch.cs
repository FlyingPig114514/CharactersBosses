using System;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.HoverTips;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace CharacterBosses.Patches;

/// <summary>
/// **离开战斗房就把我们那条常驻说明框收掉**（用户 2026-11-15 第三次反馈：
/// 「切到地图页面还是能看到特殊效果的描述框」）。
///
/// 前两次的失败教训：
///   · `BossSpecialTip.Hide()` 靠锚点 `NHoverTipSet.Remove(_anchor)` 收框 —— 但离房时锚点
///     随房间先被释放，`IsInstanceValid` 已为 false ⇒ 一次都没收过；
///   · 补了「直接对框 QueueFree」+ `BeforeRemovedFromRoom` 里 `HideAll()` —— **仍然留着**，
///     说明还有一个不经过我们静态引用的副本（悬停层自己再建的、或引用被清空后没法回收的那个）。
///
/// 所以这次改成**按「框自己」判活**：`NHoverTipSet._Process` 是它自己的每帧驱动，
/// 只要这块框还在屏幕上，它每帧都会跑一次 —— 在那里判「当前没有战斗房就整块收掉」，
/// 不依赖任何静态引用是否还有效。只对有战斗房时才放行 ⇒ 正常悬停提示（原版/其它模组）不受影响。
/// </summary>
[HarmonyPatch(typeof(NHoverTipSet), "_Process")]
internal static class BossSpecialTipRoomGuardPatch
{
    /// <summary>我们创建的框（`BossSpecialTip` 会把它们登记进来）。</summary>
    private static readonly System.Collections.Generic.List<WeakRef> Ours = new();

    internal static void Register(NHoverTipSet? tip)
    {
        try
        {
            if (tip == null || !GodotObject.IsInstanceValid(tip)) return;
            Ours.Add(GodotObject.WeakRef(tip));
        }
        catch { /* 纯外观 */ }
    }

    /// <summary>每帧：不在战斗房里 ⇒ 我们登记过的框一律隐藏并释放。</summary>
    [HarmonyPostfix]
    public static void Postfix(NHoverTipSet __instance)
    {
        try
        {
            if (__instance == null || !GodotObject.IsInstanceValid(__instance)) return;
            if (!OursContain(__instance)) return;

            bool inCombat;
            try { inCombat = NCombatRoom.Instance != null && GodotObject.IsInstanceValid(NCombatRoom.Instance); }
            catch { inCombat = false; }
            if (inCombat) return;

            __instance.Visible = false;
            __instance.QueueFree();
            Logger.Log("[CharBoss] 已离开战斗房 ⇒ 收掉常驻特殊效果说明框");
        }
        catch { /* 纯外观 */ }
    }

    private static bool OursContain(NHoverTipSet tip)
    {
        for (int i = Ours.Count - 1; i >= 0; i--)
        {
            try
            {
                var target = Ours[i].GetRef().As<GodotObject>();
                if (target == null) { Ours.RemoveAt(i); continue; }
                if (target != null && ReferenceEquals(target, tip)) return true;
            }
            catch { Ours.RemoveAt(i); }
        }
        return false;
    }
}
