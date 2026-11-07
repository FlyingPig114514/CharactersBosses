using System;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models.Powers;
using CharacterBosses.Powers;

namespace CharacterBosses.Patches;

/// <summary>
/// 「立刻触发中毒」也要**能被格挡**。
///
/// 用户设计「带毒刺击」原话是「中毒改为在你回合结束时结算**且伤害能被格挡抵消**」——
/// 这是对中毒伤害的全局要求，不是只针对周期性结算。所以：
///   · 周期性结算（玩家回合结束，双方）由 <see cref="PoisonStrikePower.BeforeSideTurnEnd"/> 负责；
///   · **显式**触发（毒性爆发那类「立刻触发中毒」的牌）走的是原版 <c>PoisonPower.Trigger()</c> 本体，
///     它内部写死 `ValueProp.Unblockable | ValueProp.Unpowered` —— 这里把它接管成可格挡版本
///     （<see cref="PoisonStrikePower.TriggerBlockableAsync"/>，逻辑与原版一一对应，只换伤害属性）。
///
/// 只在「本场战斗存在带毒刺击」时接管；其它战斗（原版毒流玩家、其它 Boss）完全走原版。
/// ★ 双版本（v0.107 / v0.111）：`PoisonPower.Trigger()` 是**新版才有**的方法；旧版的中毒结算全在
///   `AfterSideTurnStart` 里，没有「立刻触发」这个公共入口 ⇒ 旧版上整类跳过（那类牌在旧版本来也不走这个入口）。
/// </summary>
[HarmonyPatch]
public static class PoisonTriggerBlockablePatch
{
    [HarmonyPrepare]
    public static bool Prepare() => Core.GameCompat.HasPoisonTrigger;

    internal static System.Reflection.MethodBase? TargetMethod()
        => Core.GameCompat.DeclaredMethod(typeof(PoisonPower), "Trigger");

    [HarmonyPrefix]
    public static bool Prefix(PoisonPower __instance, ref Task __result)
    {
        try
        {
            if (!PoisonStrikePower.Applies(__instance?.Owner?.CombatState)) return true;

            __result = PoisonStrikePower.TriggerBlockableAsync(__instance!);
            Logger.Log($"[CharBoss] 带毒刺击：{__instance!.Owner?.Name} 被「立刻触发」中毒 → 改走可格挡结算");
            return false;
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 立刻触发中毒的可格挡接管失败，按原版执行: " + e.Message);
            return true;
        }
    }
}
