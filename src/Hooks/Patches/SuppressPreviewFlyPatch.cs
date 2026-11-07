using System;
using System.Collections.Generic;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Vfx;

namespace CharacterBosses.Patches;

/// <summary>
/// 「展示用副本」的**飞行动画消音器**。
///
/// 背景（用户 2026-10-24）：能量汲取要把升级掉的牌展示出来，用的是原版
/// <c>CardCmd.Preview(多张, …)</c> —— 牌会居中并排出现 ✓，但 `PreviewInternal` 的补间结束时
/// 会 `NCardFlyVfx.Create(...)` 把牌**飞进玩家弃牌堆**（因为那个 API 要求副本归本地玩家）。
/// 用户要求：牌不要飞进弃牌堆，甚至最好飘向 Boss。
///
/// 「飘向 Boss」做不到：`PreviewInternal` 的飞行目标是 `card.Owner.Creature`，而
/// `LocalContext.IsMine(card)` 又强制副本必须归本地玩家 —— 两者绑死。
/// 所以这里把**飞行动画整个掐掉**：登记过的展示副本一旦要生成 NCardFlyVfx，直接返回 null，
/// <c>PreviewInternal</c> 就会走它的 `else` 分支（`node.QueueFreeSafely()`）—— 牌展示完原地消失。
/// </summary>
[HarmonyPatch(typeof(NCardFlyVfx), "Create",
    new[] { typeof(NCard), typeof(PileType), typeof(bool), typeof(string) })]
public static class SuppressPreviewFlyPatch
{
    private static readonly HashSet<CardModel> Suppressed = new();

    /// <summary>这张副本接下来不要播飞行动画。</summary>
    internal static void Suppress(CardModel? card)
    {
        if (card == null) return;
        try { Suppressed.Add(card); } catch { /* 忽略 */ }
    }

    internal static void Release(CardModel? card)
    {
        if (card == null) return;
        try { Suppressed.Remove(card); } catch { /* 忽略 */ }
    }

    [HarmonyPrefix]
    public static bool Prefix(NCard card, ref NCardFlyVfx __result)
    {
        try
        {
            CardModel? model = null;
            try { model = card?.Model; } catch { /* 忽略 */ }
            if (model == null || !Suppressed.Contains(model)) return true;

            __result = null;      // 让 PreviewInternal 走「没有 VFX」那条分支：node.QueueFreeSafely()
            return false;
        }
        catch
        {
            return true;
        }
    }
}
