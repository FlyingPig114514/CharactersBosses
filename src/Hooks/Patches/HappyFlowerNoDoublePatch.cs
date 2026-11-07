using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models.Relics;

namespace CharacterBosses.Patches;

/// <summary>
/// **拦掉原版「开心小花」的计数**（二层「杂耍战」，用户 2026-11-02）。
///
/// 用户口径：
/// > 开心小花计数错误，战斗开始时计数应该是 0，在他的第 1、4、7 等（3n+1）回合 1，
/// > 第 2、5、8 等（3n+2）回合 2，然后在 3、6、9 等（3n）回合加费（计数归 0）。
///
/// 原版 <c>HappyFlower.AfterSideTurnStart</c> 是「每次某一方回合开始都 +1 计数、满 3 就加 1 费」，
/// 而影子玩家的遗物**会被广播多次**（原版广播 + 我们自己的补触发，见 `BossHookBridge` 的类注释），
/// 于是计数会跑得比回合快 ⇒ 加费时点完全不对（用户实测）。
///
/// 现在：**她这条一律跳过**，改由 <see cref="Core.HappyFlowerPlays"/> 在**她自己的回合开始**
/// 按回合号算（`TurnNumber % 3 == 0` 时 +1 费，并把计数摆成 `1/2/0` 的位置）。真人玩家不受影响。
/// </summary>
[HarmonyPatch(typeof(HappyFlower), nameof(HappyFlower.AfterSideTurnStart))]
public static class HappyFlowerNoDoublePatch
{
    [HarmonyPrefix]
    public static bool Prefix(HappyFlower __instance, ref System.Threading.Tasks.Task __result)
    {
        try
        {
            var owner = __instance.Owner;
            if (owner?.Creature?.Monster is not Monsters.CharBossMonster) return true;   // 真人玩家 → 原版

            // ⚠ 跳过返回 Task 的方法必须给一个已完成的 Task，否则调用方 await null → NRE。
            __result = System.Threading.Tasks.Task.CompletedTask;
            return false;
        }
        catch
        {
            return true;   // 判断失败就按原版走
        }
    }
}
