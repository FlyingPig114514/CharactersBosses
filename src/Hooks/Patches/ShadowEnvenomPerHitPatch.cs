using System;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;

namespace CharacterBosses.Patches;

/// <summary>
/// 涂毒（<c>EnvenomPower</c>）对**角色 Boss**：按「伤害事件」逐段上毒 —— 接管原版实现。
///
/// 原版（反编译）：
/// <code>
/// AfterDamageGiven(ctx, dealer, result, props, target, cardSource):
///     if (dealer == Owner &amp;&amp; props.IsPoweredAttack() &amp;&amp; result.UnblockedDamage &gt; 0)
///         PowerCmd.Apply&lt;PoisonPower&gt;(ctx, target, Amount, Owner, null);
/// </code>
/// 而多段攻击（连续反弹 `WithHitCount(Repeat)`、匕首雨 `WithHitCount(2)`）在原版
/// <c>AttackCommand.Execute</c> 里是 `for (i &lt; attackCount) await CreatureCmd.Damage(...)`
/// —— **每段一次**伤害事件，所以理论上每段都会各上一次毒。但实测她的多段牌只上 1 层毒
/// （用户报告：连续反弹不会每段上毒，匕首雨同理）。
///
/// 所以这里对她接管：同样按「每个伤害事件」结算，但条件放宽到
/// `dealer == 她 && 未格挡伤害 > 0`（不再要求 <c>IsPoweredAttack</c>），
/// 并打一条诊断日志把当时的 props / 未格挡数值记下来，方便定位原版到底卡在哪一步。
/// 真玩家（含玩家自己的涂毒）完全走原版。
/// </summary>
[HarmonyPatch(typeof(EnvenomPower), nameof(EnvenomPower.AfterDamageGiven))]
public static class ShadowEnvenomPerHitPatch
{
    [HarmonyPrefix]
    public static bool Prefix(EnvenomPower __instance, PlayerChoiceContext choiceContext, Creature dealer,
                              DamageResult result, ValueProp props, Creature target, CardModel cardSource,
                              ref Task __result)
    {
        try
        {
            var owner = __instance.Owner;   // PowerModel.Owner 就是生物（Creature）
            if (owner == null) return true;
            if (owner.Side != CombatSide.Enemy || owner.Monster == null) return true;   // 真玩家：走原版

            bool powered = props.IsPoweredAttack();
            int unblocked = result?.UnblockedDamage ?? 0;
            int blocked = result?.BlockedDamage ?? 0;
            bool mine = ReferenceEquals(dealer, owner);

            Logger.Log($"[CharBoss] 涂毒诊断: dealer={dealer?.Name ?? "null"} 是她={mine} " +
                       $"card={cardSource?.Id.Entry ?? "null"} props={props} " +
                       $"powered={powered} 未格挡={unblocked} 被格挡={blocked} " +
                       $"目标={target?.Name ?? "null"} 层数={__instance.Amount}");

            if (mine && unblocked > 0 && target != null && __instance.Amount > 0)
            {
                __result = PowerCmd.Apply<PoisonPower>(choiceContext, target,
                    __instance.Amount, owner, null, false);
                Logger.Log($"[CharBoss] 涂毒（她）：这段未格挡 {unblocked} → 给 {target.Name} " +
                           $"+{__instance.Amount} 层中毒");
                return false;
            }

            __result = Task.CompletedTask;
            return false;
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 涂毒接管失败，按原版执行: " + e.Message);
            return true;
        }
    }
}
