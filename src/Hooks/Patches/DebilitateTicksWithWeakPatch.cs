using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Powers;

namespace CharacterBosses.Patches;

/// <summary>
/// 「**摧残**」（<c>DebilitatePower</c>）挂在**玩家**身上时的倒计时，改成与**虚弱 / 易伤**同步。
///
/// 用户 2026-11-10：「让玩家身上摧残的消失时间和虚弱易伤同步（现在玩家的回合一结束摧残就消失了）」。
///
/// 反编译（`sts2_111.dll`）两者用的根本不是同一条规则：
/// <code>
/// // VulnerablePower / WeakPower —— 只在**敌方**回合结束时掉 1
/// public override Task AfterSideTurnEnd(ctx, CombatSide side, IEnumerable&lt;Creature&gt; participants)
/// {
///     if (side == CombatSide.Enemy) await PowerCmd.TickDownDuration(this);
/// }
///
/// // DebilitatePower —— 只看「**持有者自己这一侧**的回合结束」
/// public override Task AfterSideTurnEnd(ctx, CombatSide side, IEnumerable&lt;Creature&gt; participants)
/// {
///     if (participants.Contains(Owner)) await PowerCmd.Decrement(this);
/// }
/// </code>
/// ⇒ 摧残在玩家身上时，**玩家回合一结束就掉 1**（2 层只撑一个玩家回合），
///   而虚弱/易伤是等到**她（敌方）回合结束**才掉 ⇒ 少撑了一个回合。
///
/// 做法：只接管「持有者是真人玩家」这一种情况，改成和虚弱/易伤一模一样的
/// `side == CombatSide.Enemy ⇒ PowerCmd.TickDownDuration(this)`；
/// 怪物身上的摧残（真玩家打出摧残打 Boss）照旧走原版。
///
/// ⚠ `AfterSideTurnEnd` 是 **async** ⇒ Prefix 返回 false 必须给 `ref Task __result`。
/// </summary>
[HarmonyPatch(typeof(DebilitatePower), "AfterSideTurnEnd",
    new[] { typeof(PlayerChoiceContext), typeof(CombatSide), typeof(IEnumerable<Creature>) })]
internal static class DebilitateTicksWithWeakPatch
{
    private static bool Prefix(PlayerChoiceContext choiceContext, CombatSide side,
                               DebilitatePower __instance, ref Task __result)
    {
        try
        {
            var owner = __instance?.Owner;
            if (owner == null) return true;
            if (owner.Monster != null) return true;      // 怪物身上的照旧（真玩家打 Boss 的情况）

            // 与虚弱/易伤同一条规则：只在**敌方**回合结束时掉 1
            if (side != CombatSide.Enemy)
            {
                __result = Task.CompletedTask;
                return false;
            }
            __result = TickAsync(__instance!);
            return false;
        }
        catch (Exception e)
        {
            try { Logger.Log("[CharBoss] 摧残倒计时（同步虚弱/易伤）判定失败，放行原版: " + e.Message); } catch { /* 忽略 */ }
            return true;
        }
    }

    private static async Task TickAsync(DebilitatePower power)
    {
        try
        {
            int before = 0;
            try { before = (int)Math.Round((double)power.Amount); } catch { /* 忽略 */ }
            await PowerCmd.TickDownDuration(power);
            int after = 0;
            try { after = (int)Math.Round((double)power.Amount); } catch { /* 忽略 */ }
            Logger.Log($"[CharBoss] 摧残（玩家身上）：她这一侧回合结束 ⇒ {before} → {after}（与虚弱/易伤同步）");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 摧残倒计时失败: " + e.Message);
        }
    }
}
