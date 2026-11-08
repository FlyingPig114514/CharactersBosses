using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;

namespace CharacterBosses.Patches;

/// <summary>
/// 「**地狱狂徒**」自动打出打击时的**特效**（用户 2026-11-07：「被地狱狂徒打出的打击会有特效」）。
///
/// 做法：不自己造节点，直接复用原版那套**受击演出**（`CreatureCmd.TriggerAnim` → `Hit`），
/// 于是敌人身上会正常闪一下、出伤害数字；出牌本身由 `CardCmd.AutoPlay` 播放卡面飞出的演出。
/// 任何异常都吞掉：**特效失败绝不影响出牌**。
/// </summary>
internal static class HellraiserVfx
{
    internal static void Flash(Creature? victim)
    {
        try
        {
            if (victim == null) return;
            _ = CreatureCmd.TriggerAnim(victim, "Hit", 1f);
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 地狱狂徒特效失败（忽略）: " + e.Message);
        }
    }
}