using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Relics;

namespace CharacterBosses.Patches;

/// <summary>
/// 红面具（<c>RedMask</c>）：让它**只在战斗开始**由 mod 给玩家上虚弱，别再被她自己的回合开始重复触发。
///
/// 反编译的红面具实现：
/// <code>
/// BeforeSideTurnStart(ctx, side, participants, combatState):
///     if (participants.Contains(Owner.Creature) && TurnNumber &lt;= 1)
///         PowerCmd.Apply&lt;WeakPower&gt;(ctx, combatState.HittableEnemies, 1, Owner.Creature, null);
/// </code>
/// 它比的是「可命中敌人」而不是阵营，而 <c>CombatState.IterateHookListeners()</c> 会把
/// 挂在 Boss 生物上的影子 Player 的遗物也算成监听者 ⇒ 她的红面具会在**她自己的第 1 回合开始**
/// 被原版广播一次；我们又在战斗开始手动触发一次 ⇒ 玩家一共吃到 2 层虚弱。
///
/// 用户要求：红面具应当「**一进战斗**就给玩家 1 层虚弱」。
/// 所以这里把原版那次（在她回合开始）屏蔽掉，改由
/// <c>BossHookBridge.FireCombatStart → EnsureRedMaskWeak</c> 在战斗开始时按玩家侧语义施加。
///
/// 补丁打在**具体类型**上：基类虚方法打补丁拦不住派生 override（踩过）。
/// 真玩家自己的红面具完全不受影响（按 owner 的阵营判断）。
/// </summary>
[HarmonyPatch(typeof(RedMask), nameof(RedMask.BeforeSideTurnStart))]
public static class ShadowRedMaskTurnStartPatch
{
    [HarmonyPrefix]
    public static bool Prefix(RedMask __instance, ref Task __result)
    {
        try
        {
            var owner = __instance.Owner;
            if (owner?.Creature == null) return true;
            if (owner.Creature.Side != CombatSide.Enemy) return true;   // 真人玩家：走原版
            if (owner.Creature.Monster == null) return true;            // 不是我们的 Boss 生物

            __result = Task.CompletedTask;
            Logger.Log("[CharBoss] 红面具：跳过她回合开始的重复触发（虚弱已在战斗开始时给过）");
            return false;
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 红面具拦截失败，按原版执行: " + e.Message);
            return true;
        }
    }
}
