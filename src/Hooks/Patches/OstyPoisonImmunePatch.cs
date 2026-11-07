using System;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using CharacterBosses.Core;

namespace CharacterBosses.Patches;

/// <summary>
/// 她的奥斯提**不吃毒**（中毒无法施加到它身上）。
///
/// 用户实测：弹跳药瓶（`BouncingFlask`：从 <c>HittableEnemies</c> 里随机取敌人、再 <c>PowerCmd.Apply&lt;PoisonPower&gt;</c>）
/// 会给奥斯提上毒。这不符合设计 ——
/// 「为你而死」(<c>DieForYouPower</c>) 只替她吸收**未被格挡的攻击伤害**
/// （反编译确认：`if (!props.IsPoweredAttack()) return target;`，中毒是 `Unpowered`，本来就不会被转移），
/// 所以奥斯提也不该承受中毒。
///
/// 为什么拦 <c>PowerCmd.Apply</c> 的非泛型重载：泛型的
/// <c>Apply&lt;T&gt;(ctx, target, …)</c> 最后都会走到
/// <c>Apply(ctx, power, target, amount, applier, cardSource, silent)</c>（反编译确认），
/// 所以毒雾、弹跳药瓶、致命毒药…全部一次覆盖，不用逐张牌打补丁。
///
/// **两只奥斯提都免疫**（她那边的 + 玩家自己那只）：
/// 用户要求"奥斯提只负责吸收未格挡的攻击伤害，中毒只能落到主人身上" ——
/// 否则敌方静默猎手的毒雾/上毒牌会把玩家的奥斯提也毒了。
/// </summary>
[HarmonyPatch(typeof(PowerCmd), nameof(PowerCmd.Apply), new[]
{
    typeof(PlayerChoiceContext), typeof(PowerModel), typeof(Creature),
    typeof(decimal), typeof(Creature), typeof(CardModel), typeof(bool),
})]
public static class OstyPoisonImmunePatch
{
    [HarmonyPrefix]
    public static bool Prefix(PowerModel power, Creature target, ref Task __result)
    {
        try
        {
            if (power is not PoisonPower) return true;
            if (!BossOsty.IsAnyOsty(target)) return true;

            __result = Task.CompletedTask;
            Logger.Log($"[CharBoss] 奥斯提不吃毒：跳过给 {target.Name} 施加中毒" +
                       "（中毒只落在主人身上）");
            return false;
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 奥斯提免疫中毒拦截失败，按原版执行: " + e.Message);
            return true;
        }
    }
}
