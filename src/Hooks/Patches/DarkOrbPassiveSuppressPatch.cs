using System;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Orbs;
using CharacterBosses.Monsters;

namespace CharacterBosses.Patches;

/// <summary>
/// 「冷却剂」第二半：**玩家打出「冷光」的那一回合，故障机器人的暗黑充能球不使用被动**。
///
/// 挂点（反编译）：
/// <code>
/// DarkOrb.BeforeTurnEndOrbTrigger(ctx)   ← 球被动在「回合结束」的入口（内部调 TriggerPassive）
/// OrbQueue.BeforeTurnEnd(ctx)            ← 逐颗调用上面那个
/// </code>
/// 前缀里判断：这颗球的 Owner 是不是**我们的 Boss**、且她那层标记（`DarkPassiveSuppressed`，
/// 由 `Powers/CoolantPower` 在玩家打出冷光时置位）为真 ⇒ 跳过这次触发（`__result = Task.CompletedTask`）。
///
/// 只拦**暗黑球**（闪电/冰霜/等离子照常），也只拦**我们的 Boss**：玩家自己的黑暗球、其它敌人的球都零影响。
/// </summary>
[HarmonyPatch(typeof(DarkOrb), nameof(DarkOrb.BeforeTurnEndOrbTrigger))]
public static class DarkOrbPassiveSuppressPatch
{
    [HarmonyPrefix]
    public static bool Prefix(DarkOrb __instance, ref Task __result)
    {
        try
        {
            var boss = __instance?.Owner?.Creature?.Monster as CharBossMonster;
            if (boss == null || !boss.DarkPassiveSuppressed) return true;   // 不归我们管：照常触发

            __result = Task.CompletedTask;
            return false;
        }
        catch { return true; }
    }
}
