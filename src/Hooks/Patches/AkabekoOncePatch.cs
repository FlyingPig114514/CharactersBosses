using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Relics;
using CharacterBosses.Core;

namespace CharacterBosses.Patches;

/// <summary>
/// 「赤牛」（<c>Akabeko</c>）的活力**只允许给一次**（战斗开始时）。
///
/// 用户 2026-11-02 实测（进阶 9 的「辉星储」带赤牛）：
/// 「赤牛有 bug，给辉星储加了 2 次活力（应该只在战斗开始时加一次，他第一回合开始时不加）」。
///
/// 两条路径都在给，和「击剑指南 / 破损核心 / 红面具」完全同一类问题：
///   ① 我们自己：<see cref="BossHookBridge"/> 的 <c>FireCombatStart</c> 会在**战斗开始时**
///      逐件遗物手动调 <c>AfterSideTurnStart(CombatSide.Player, [她], combat)</c>
///      （并把影子玩家的 <c>TurnNumber</c> 临时摆成 1）⇒ 赤牛的条件成立 ⇒ 活力 +8；
///   ② 原版自己：她**回合开始**时原版广播会打到她的遗物上
///      （`IterateHookListeners` 把「带 Player 的生物」的遗物也算监听者），
///      而 <c>Akabeko.AfterSideTurnStart</c> 的条件是
///      <code>
///      participants.Contains(Owner.Creature) &amp;&amp; Owner.PlayerCombatState.TurnNumber &lt;= 1
///        → PowerCmd.Apply&lt;VigorPower&gt;(Owner.Creature, DynamicVars["VigorPower"].IntValue /* 8 */, …)
///      </code>
///      影子玩家在她的第 1 回合正好是 <c>TurnNumber == 1</c> ⇒ 又给一次（合计 16）。
///
/// 所以：**她这条一律跳过**（战斗开始已经给过）；真人玩家的赤牛完全不受影响。
/// </summary>
[HarmonyPatch(typeof(Akabeko), nameof(Akabeko.AfterSideTurnStart))]
public static class AkabekoOncePatch
{
    [HarmonyPrefix]
    public static bool Prefix(Akabeko __instance, ref System.Threading.Tasks.Task __result)
    {
        try
        {
            var owner = __instance.Owner;
            if (owner?.Creature?.Monster is not Monsters.CharBossMonster) return true;   // 真人玩家 → 原版

            int now = -1;
            try { now = owner.Creature.GetPowerAmount<VigorPower>(); } catch { /* 忽略 */ }
            Logger.Log($"[CharBoss] 赤牛：跳过原版在她回合开始的那次活力（战斗开始时已给过，现有活力 {now}）");

            // ⚠ 跳过返回 Task 的方法必须给一个已完成的 Task，否则调用方 await null → NRE
            //（「击剑指南」当年就踩过这个坑，会把玩家回合卡住）。
            __result = System.Threading.Tasks.Task.CompletedTask;
            return false;
        }
        catch
        {
            return true;   // 判断失败就按原版走，绝不影响流程
        }
    }
}
