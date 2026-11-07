using System;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models.Powers;
using CharacterBosses.Monsters;

namespace CharacterBosses.Patches;

/// <summary>
/// **拦掉「好勇斗狠」的随机捞牌**（用户 2026-10-23：「把好勇斗狠的捞牌拦了，我已经把要捞的牌给写出来了」）。
///
/// 原版实现（反编译 `AggressionPower.BeforeSideTurnStart`）：
/// <code>
/// var attack = Enum.DiscardPile.Where(c =&gt; c.Type == Attack)
///                              .ToList().UnstableShuffle(RunState.Rng.CombatCardSelection)
///                              .Take(Amount);
/// foreach (var card in attack) { CardCmd.Upgrade(card); await CardPileCmd.Add(card, PileType.Hand); }
/// </code>
/// 对 Boss 来说这是**有害**的：她的手牌是剧本一回合重造一份的（意图也按那份提前算好），
/// 随机再捞一张进手会多出一张牌、并让意图对不上；而且捞哪张由 RNG 决定，不可复现。
///
/// 剧本里已经把「要捞的那张（愤怒+）」直接写在手牌里了 ⇒ 这里直接**整段跳过**。
/// 只对「我们的 Boss」生效，真人玩家完全不受影响。
///
/// ⚠ **必须把 `__result` 设成 `Task.CompletedTask`**：这个方法返回 `Task`，
/// 前缀返回 false 而不给结果时 Harmony 会把它留成 `null` ⇒ 调用方 `await null` 直接 NRE，
/// 整条「回合开始」协程就断了 —— 表现就是**玩家回合刚开始就卡死**（2026-10-23 实测踩到过）。
/// </summary>
[HarmonyPatch(typeof(AggressionPower), nameof(AggressionPower.BeforeSideTurnStart))]
public static class BossNoAggressionPullPatch
{
    [HarmonyPrefix]
    public static bool Prefix(AggressionPower __instance, ref Task __result)
    {
        try
        {
            if (__instance?.Owner?.Monster is not CharBossMonster) return true;
            Logger.Log("[CharBoss] 好勇斗狠：拦掉随机捞牌（剧本里已经写好要捞的那张愤怒+）");
            __result = Task.CompletedTask;
            return false;
        }
        catch
        {
            return true;
        }
    }
}
