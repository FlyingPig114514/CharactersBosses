using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;

namespace CharacterBosses.Patches;

/// <summary>
/// **只记日志**的探针：`LockOnPower` 每次被摘掉时打一行。
///
/// 为什么需要（用户 2026-10-29 的视频 + 日志）：
///   · 10:31:37 开局给 4 名玩家各建了 1 个锁定图标（实例 #1~#4）；
///   · 10:31:48 巡检显示「每人 1 个」；
///   · 紧接着又冒出实例 #5~#7 —— 说明那 3 个**中途消失过**，我们才又补了一份，
///     但玩家屏幕上看到的是「本来有一个、又加了一个」= 2 个。
/// 我们自己的删除点只有三处（对账时没人锁他 / 摘重复层 / Boss 死亡），三处都会打日志，
/// 而那次一条都没有 ⇒ 不是我们删的。这层探针把「谁删的」找出来：
/// 只要出现这行、而同一时刻没有我们的日志，就是游戏自己在清 power。
/// </summary>
[HarmonyPatch(typeof(Creature), nameof(Creature.RemovePowerInternal))]
public static class LockOnPowerRemoveProbe
{
    private static int _removed;

    [HarmonyPostfix]
    public static void Postfix(Creature __instance, PowerModel power)
    {
        try
        {
            if (power is not Powers.LockOnPower) return;
            if (_removed++ > 60) return;
            Logger.Log($"[CharBoss] 锁定图标被摘除：{__instance?.Name}" +
                       $"（第 {_removed} 次；这次不是我们主动摘的就会没有对应的 `跟踪锁定` 日志）");
        }
        catch { /* 忽略 */ }
    }
}
