using System;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Models.Relics;

namespace CharacterBosses.Patches;

/// <summary>
/// 「抱抱先生」（<c>MrStruggles</c>）：**屏蔽原版那一次**，只留我们自己的结算。
///
/// 原版（反编译 `MrStruggles.AfterPlayerTurnStart`）：
/// <code>
/// await CreatureCmd.Damage(choiceContext, combatState.HittableEnemies,
///                          Owner.PlayerCombatState.TurnNumber, ValueProp.Unpowered, Owner.Creature);
/// </code>
/// 也就是「**持有者自己的回合开始时**，对所有可命中敌人造成「回合数」点无来源伤害」。
///
/// 问题是：影子玩家的遗物**确实**挂在 `IterateHookListeners()` 里（和红面具 / 破损核心同一类），
/// 于是「真人玩家回合开始」这件事也会广播到她的遗物上 ⇒ 她一件遗物打出**两次**：
///   · 原版那次：真人玩家回合开始时，伤害 = `PlayerCombatState.TurnNumber`；
///   · 我们的那次：**她自己的回合开始**（`BossHookBridge.FireTurnStart → MrStrugglesPlays`），
///     伤害 = 她的回合数 —— 这才是原版语义（持有者的回合）。
/// 用户 2026-11-02 实测：「第一回合受到了 1 点伤害 2 次」。
///
/// 所以这里把原版那次在「owner 是我们的 Boss 生物」时拦掉
/// （打法同 <see cref="ShadowRedMaskTurnStartPatch"/>：打在**具体类型**上，派生 override 才拦得住；
/// 真人玩家自己的抱抱先生完全不受影响）。
/// </summary>
[HarmonyPatch(typeof(MrStruggles), nameof(MrStruggles.AfterPlayerTurnStart))]
public static class ShadowMrStrugglesTurnStartPatch
{
    [HarmonyPrefix]
    public static bool Prefix(MrStruggles __instance, ref Task __result)
    {
        try
        {
            var owner = __instance.Owner;
            var creature = owner?.Creature;
            if (creature == null) return true;
            if (creature.Side != CombatSide.Enemy) return true;   // 真人玩家：走原版
            if (creature.Monster == null) return true;            // 不是我们的 Boss 生物

            __result = Task.CompletedTask;
            Logger.Log("[CharBoss] 抱抱先生：跳过原版那次（她的回合开始已由 MrStrugglesPlays 结算，否则会打两次）");
            return false;
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 抱抱先生拦截失败，按原版执行: " + e.Message);
            return true;
        }
    }
}
