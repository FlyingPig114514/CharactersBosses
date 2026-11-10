using System;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;

namespace CharacterBosses.Patches;

/// <summary>
/// 让「**招架盾**」（<c>ParryingShield</c>）对**我们的 Boss** 只在多人时打她锁定的那位玩家。
///
/// 反编译原文（`sts2_111.dll`）：
/// <code>
/// public override Task AfterSideTurnEnd(PlayerChoiceContext ctx, CombatSide side, IEnumerable&lt;Creature&gt; participants)
/// {
///     if (participants.Contains(Owner.Creature) &amp;&amp; Owner.Creature.Block &gt;= DynamicVars.Block.BaseValue)
///     {
///         Creature target = Owner.RunState.Rng.CombatTargets.NextItem(Owner.Creature.CombatState.HittableEnemies);
///         await CreatureCmd.Damage(ctx, target, DynamicVars.Damage, Owner.Creature);
///     }
/// }
/// DynamicVars：BlockVar(10, Unpowered) 门槛、DamageVar(6, Unpowered) 伤害
/// </code>
/// `HittableEnemies` 是**整份敌方列表** —— 多人局里它包含所有真人玩家（以及玩家的召唤物），
/// 于是一个「随机」就可能打到没被她锁定的人（用户 2026-11-10：`多人时让招架盾只会打被她锁定的玩家`）。
///
/// 做法：只接管「遗物主人是我们的 Boss」这一种；按原版条件结算，但目标固定成
/// <see cref="Core.BossLockOn.TargetFor"/>（她锁定的玩家）；拿不到锁定对象时退回原版随机。
/// 真玩家自己的招架盾照旧走原版（`return true`）。
///
/// ⚠ `AfterSideTurnEnd` 是 **async** ⇒ Prefix 返回 false 必须给 `ref Task __result`。
/// </summary>
[HarmonyPatch(typeof(ParryingShield), "AfterSideTurnEnd",
    new[]
    {
        typeof(MegaCrit.Sts2.Core.GameActions.Multiplayer.PlayerChoiceContext),
        typeof(MegaCrit.Sts2.Core.Combat.CombatSide),
        typeof(System.Collections.Generic.IEnumerable<Creature>),
    })]
internal static class ParryingShieldBossPatch
{
    private static bool Prefix(MegaCrit.Sts2.Core.GameActions.Multiplayer.PlayerChoiceContext choiceContext,
                               MegaCrit.Sts2.Core.Combat.CombatSide side,
                               System.Collections.Generic.IEnumerable<Creature> participants,
                               ParryingShield __instance, ref Task __result)
    {
        try
        {
            var relic = __instance;
            var owner = relic?.Owner?.Creature;
            if (owner == null) return true;
            if (owner.Monster is not Monsters.CharBossMonster boss) return true;    // 只接管我们的 Boss

            // 原版条件：她这一侧回合结束 + 格挡 ≥ 门槛
            bool participating = false;
            try { participating = participants != null && System.Linq.Enumerable.Contains(participants, owner); }
            catch { participating = false; }
            if (!participating) { __result = Task.CompletedTask; return false; }

            int threshold = Core.FleshBone.ParryBlockThreshold;
            int damage = Core.FleshBone.ParryDamageAmount;
            try
            {
                var vars = relic!.DynamicVars;
                threshold = (int)Math.Round((double)vars.Block.BaseValue);
                damage = (int)Math.Round((double)vars.Damage.BaseValue);
            }
            catch { /* 读不到就用反编译里的 10 / 6 */ }

            if (owner.Block < threshold) { __result = Task.CompletedTask; return false; }

            // ★ 目标：多人时**只打她锁定的那位玩家**
            Creature? target = null;
            try { target = Core.BossLockOn.TargetFor(boss)?.Creature; } catch { target = null; }
            if (target == null)
            {
                // 没锁定对象（单人局开局那一瞬等）⇒ 退回原版随机，别自己造一个错的目标
                return true;
            }
            if (!target.IsAlive) { __result = Task.CompletedTask; return false; }

            __result = HitAsync(choiceContext, relic!, owner, target, damage);
            return false;
        }
        catch (Exception e)
        {
            try { Logger.Log("[CharBoss] 招架盾（Boss 版）判定失败，放行原版: " + e.Message); } catch { /* 忽略 */ }
            return true;
        }
    }

    private static async Task HitAsync(MegaCrit.Sts2.Core.GameActions.Multiplayer.PlayerChoiceContext choiceContext,
                                       ParryingShield relic, Creature owner, Creature target, int damage)
    {
        try
        {
            MegaCrit.Sts2.Core.Localization.DynamicVars.DamageVar var = null!;
            try { var = relic.DynamicVars.Damage; } catch { /* 忽略 */ }

            if (var != null)
            {
                await MegaCrit.Sts2.Core.Commands.CreatureCmd.Damage(choiceContext, target, var, owner);
            }
            else
            {
                await MegaCrit.Sts2.Core.Commands.CreatureCmd.Damage(
                    choiceContext, target, damage, MegaCrit.Sts2.Core.ValueProps.ValueProp.Unpowered, owner);
            }
            Logger.Log($"[CharBoss] 招架盾：回合结束格挡 {owner.Block} ≥ 门槛 ⇒ 对她锁定的 {target.Name} 造成 {damage} 点无来源伤害");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 招架盾（Boss 版）结算失败: " + e.Message);
        }
    }
}
