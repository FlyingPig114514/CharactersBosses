using System.Collections.Generic;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models.Relics;

namespace CharacterBosses.Patches;

/// <summary>
/// 「共生病毒」的黑暗球改由**战斗开始**给，不再在她回合开始再给一次。
///
/// 原版 <c>SymbioticVirus.AfterSideTurnStart</c>（逐字）：
/// <code>
/// if (participants.Contains(Owner.Creature) &amp;&amp; Owner.PlayerCombatState.TurnNumber &lt;= 1)
///     await OrbCmd.Channel&lt;DarkOrb&gt;(new BlockingPlayerChoiceContext(), Owner);
/// </code>
/// 影子玩家的 `TurnNumber` 在**她自己第 1 回合**正好是 1 ⇒ 这条会真的触发，
/// 而战斗开始那一刻已经由 `BossHookBridge.ChannelSymbioticVirusOrbAtCombatStart` 引导过一颗
/// ⇒ 结果是**两个黑暗球**（用户 2026-10-27 实测）。
/// 这里把原版这条对影子玩家跳过（真人玩家完全不受影响）。
/// </summary>
[HarmonyPatch(typeof(SymbioticVirus), nameof(SymbioticVirus.AfterSideTurnStart))]
public static class SymbioticVirusOncePatch
{
    [HarmonyPrefix]
    public static bool Prefix(SymbioticVirus __instance, ref Task __result)
    {
        try
        {
            var owner = __instance.Owner;
            if (owner?.Creature?.Monster is not Monsters.CharBossMonster) return true;   // 真人玩家 → 原版

            Logger.Log("[CharBoss] 共生病毒：跳过原版在她回合开始的那次引导（战斗开始时已引导过 1 个黑暗球）");
            __result = Task.CompletedTask;   // 跳过返回 Task 的方法必须给个已完成的 Task
            return false;
        }
        catch
        {
            return true;
        }
    }
}
