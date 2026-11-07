using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;

namespace CharacterBosses.Patches;

/// <summary>
/// 让「影子 Player」不污染玩家侧。
///
/// 角色 Boss 的 Creature 同时挂着 Player（卡牌系统要求出牌主体是 Player），
/// 于是 <c>Creature.IsPlayer</c> 会变成 true，后果有两个：
///   ① <c>CombatState.Players</c> 会把 Boss 当成真人玩家 —— 回合开始抽 5 张、重置能量、回合结束弃手牌全砸到它身上；
///   ② <c>NCombatRoom.AddCreature</c> 会把它画到玩家那一侧。
/// 只改这一个 getter（且只对「同时有 Player 和 Monster 的 Creature」生效）就同时解决两件事。
/// 判定条件本来就是我们的印记：真正的玩家 Creature 的 Monster 是 null。
/// </summary>
[HarmonyPatch(typeof(Creature), "get_IsPlayer")]
public static class ShadowPlayerIsolationPatch
{
    [HarmonyPrefix]
    public static bool Prefix(Creature __instance, ref bool __result)
    {
        try
        {
            if (__instance.Monster != null && __instance.Player != null)
            {
                __result = false;
                return false;
            }
        }
        catch
        {
            // 出问题时退回原实现
        }
        return true;
    }
}
