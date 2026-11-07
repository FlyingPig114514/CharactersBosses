using System;
using System.Diagnostics;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;

namespace CharacterBosses.Patches;

/// <summary>
/// **原版「回合结束」各步骤的计时探针**（用户 2026-10-29：Boss 最后一张牌打完到「回合结束」效果
/// （炸弹 / 冰雹风暴）结算之间会停 1～2 秒；`SideTurnEndTimingPatch` 已经证明
/// 「距我们回合收尾 +2600ms 左右」⇒ 那段时间**不在我们代码里**，在原版流程里）。
///
/// 这里给 `CombatManager` 的四个私有步骤挂**完成计时**（异步方法只能等 Task 完成才知道耗时）：
///   · <c>AfterAllPlayersReadyToEndTurn</c> —— 所有玩家都点了结束回合之后开始跑；
///   · <c>WaitUntilQueueIsEmptyOrWaitingOnNonPlayerDrivenAction</c> —— 等动作队列排空
///     （可疑点：如果队列里有个永远「没准备好」的动作，这里就会等到超时）；
///   · <c>DoTurnEnd</c> —— 回合结束牌等；
///   · <c>AwaitTurnEndAndSwitchSides</c> —— 收尾 + 换边（`Hook.BeforeSideTurnEnd` 就在这附近派发）。
///
/// 日志形如 `原版回合结束步骤：WaitUntilQueueIsEmpty… = 2563ms`，
/// 哪个数字大就是那 2.6 秒的所在。
/// </summary>
internal static class VanillaTurnEndTimingPatch
{
    /// <summary>给一个异步方法挂「完成后计时」的通用 postfix 实现。</summary>
    internal static void Wrap(ref Task __result, string name)
    {
        try
        {
            if (__result == null) return;
            var original = __result;
            var sw = Stopwatch.StartNew();
            __result = Await(original, sw, name);
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 回合结束探针挂载失败（忽略）: " + e.Message);
        }
    }

    private static async Task Await(Task task, Stopwatch sw, string name)
    {
        try
        {
            await task;
        }
        finally
        {
            try
            {
                Logger.Log($"[CharBoss] 原版回合结束步骤：{name} = {sw.ElapsedMilliseconds}ms");
            }
            catch { /* 忽略 */ }
        }
    }
}

[HarmonyPatch(typeof(CombatManager), "AfterAllPlayersReadyToEndTurn")]
internal static class VanillaProbeAfterAllReady
{
    [HarmonyPostfix]
    public static void Postfix(ref Task __result)
        => VanillaTurnEndTimingPatch.Wrap(ref __result, "AfterAllPlayersReadyToEndTurn");
}

[HarmonyPatch(typeof(CombatManager), "WaitUntilQueueIsEmptyOrWaitingOnNonPlayerDrivenAction")]
internal static class VanillaProbeWaitQueueEmpty
{
    [HarmonyPostfix]
    public static void Postfix(ref Task __result)
        => VanillaTurnEndTimingPatch.Wrap(ref __result, "WaitUntilQueueIsEmpty…");
}

[HarmonyPatch(typeof(CombatManager), "DoTurnEnd")]
internal static class VanillaProbeDoTurnEnd
{
    [HarmonyPostfix]
    public static void Postfix(ref Task __result)
        => VanillaTurnEndTimingPatch.Wrap(ref __result, "DoTurnEnd");
}

// ★ 双版本：这个步骤是**新版（v0.111）才有**的（旧版 v0.107 的换边流程里没有它）
//   ⇒ 加 Prepare 守卫，旧版整类跳过。它只是计时探针，跳过不影响玩法。
//   （另外三个探针的目标两版都有；`DoTurnEnd` 两版**签名不同**，但这里只按名字挂 Postfix、
//     不引用任何参数类型，所以两版都挂得上。）
[HarmonyPatch(typeof(CombatManager), "AwaitTurnEndAndSwitchSides")]
internal static class VanillaProbeAwaitTurnEnd
{
    [HarmonyPrepare]
    public static bool Prepare() => Core.GameCompat.HasAwaitTurnEndAndSwitchSides;

    [HarmonyPostfix]
    public static void Postfix(ref Task __result)
        => VanillaTurnEndTimingPatch.Wrap(ref __result, "AwaitTurnEndAndSwitchSides");
}
