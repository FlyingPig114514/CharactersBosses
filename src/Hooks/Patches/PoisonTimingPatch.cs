using System;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models.Powers;
using CharacterBosses.Powers;

namespace CharacterBosses.Patches;

/// <summary>
/// 中毒结算时机：把原版的「某一方回合开始时触发」拦掉，改由
/// <see cref="PoisonStrikePower.BeforeSideTurnEnd"/> 在**玩家回合结束**统一结算（可被格挡）。
///
/// 为什么必须拦原版而不是叠加自己的：
///   原版 <c>PoisonPower.AfterSideTurnStart</c> 是「<c>participants</c> 里含自己就 <c>Trigger()</c>」——
///   一个生物只要带着中毒，轮到它那一方回合开始就会被结算一次；
///   我们另起一次结算就变成**一回合两次**（伤害翻倍，用户实测过类似的翻倍坑）。
///
/// 只在「本场战斗里存在 <see cref="PoisonStrikePower"/>」时生效，
/// 其它战斗（原版毒流玩家、其它 Boss）完全走原版逻辑。
///
/// 注意：显式「立刻触发中毒」的牌（毒性爆发那类）走的是 <c>PoisonPower.Trigger()</c> 本体，
/// 不经过这里，所以它们照旧对双方立刻生效 —— 与用户设计一致。
/// </summary>
[HarmonyPatch(typeof(PoisonPower), nameof(PoisonPower.AfterSideTurnStart))]
public static class PoisonTimingPatch
{
    [HarmonyPrefix]
    public static bool Prefix(PoisonPower __instance, ref Task __result)
    {
        try
        {
            if (!IsPoisonStrikeCombat(__instance)) return true;

            __result = Task.CompletedTask;
            Logger.Log($"[CharBoss] 带毒刺击：跳过 {__instance.Owner?.Name} 的「回合开始」中毒结算" +
                       "（改到玩家回合结束时、可被格挡）");
            return false;
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 带毒刺击时机拦截失败，按原版执行: " + e.Message);
            return true;
        }
    }

    /// <summary>本场战斗里有没有「带毒刺击」这层特殊效果。</summary>
    private static bool IsPoisonStrikeCombat(PoisonPower poison)
    {
        var combat = poison.Owner?.CombatState;
        if (combat == null) return false;

        foreach (var creature in combat.Creatures)
        {
            if (creature == null) continue;
            if (creature.GetPower<PoisonStrikePower>() != null) return true;
        }
        return false;
    }
}
