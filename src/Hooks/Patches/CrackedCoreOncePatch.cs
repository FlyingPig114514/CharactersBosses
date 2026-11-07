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
/// 「破损核心」的闪电球改由**战斗开始**给，不再在他回合开始给。
///
/// 原版 <c>CrackedCore.BeforeSideTurnStart</c>（逐字）：
/// <code>
/// if (participants.Contains(Owner.Creature) &amp;&amp; Owner.PlayerCombatState.TurnNumber &lt;= 1)
///     while (i &lt; DynamicVars["Lightning"].BaseValue /* 1 */)
///         await OrbCmd.Channel&lt;LightningOrb&gt;(...);
/// </code>
/// 影子玩家的 <c>TurnNumber</c> 在**他自己的第 1 回合**正好是 1 ⇒ 球出现在他回合开始（用户实测），
/// 而战斗开始时一个都没有。现在战斗开始那一刻由
/// <c>BossHookBridge.ChannelCrackedCoreOrbAtCombatStart</c> 引导，所以这里把原版这条对影子玩家跳过，
/// 否则又会变成「战斗开始 1 个 + 回合开始 1 个 = 2 个」。
/// </summary>
[HarmonyPatch(typeof(CrackedCore), nameof(CrackedCore.BeforeSideTurnStart))]
public static class CrackedCoreOncePatch
{
    [HarmonyPrefix]
    public static bool Prefix(CrackedCore __instance,
                              PlayerChoiceContext choiceContext,
                              CombatSide side,
                              IReadOnlyList<Creature> participants,
                              ICombatState combatState,
                              ref Task __result)
    {
        try
        {
            var owner = __instance.Owner;
            if (owner?.Creature?.Monster is not Monsters.CharBossMonster) return true;   // 真人玩家 → 原版

            // 只拦「他自己第 1 回合那次」（原版也只在那时出球）；战斗开始已经由我们给过。
            Logger.Log("[CharBoss] 破损核心：跳过原版在他回合开始的那次引导（战斗开始时已引导过）");
            // 跳过返回 Task 的方法必须给一个已完成的 Task，否则调用方 await null → NRE
            __result = Task.CompletedTask;
            return false;
        }
        catch
        {
            return true;
        }
    }
}
