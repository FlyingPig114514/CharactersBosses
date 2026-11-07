using System;
using System.Diagnostics;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace CharacterBosses.Patches;

/// <summary>
/// 「**结束回合按钮为什么不见了**」的定位探针（用户 2026-11-04，事件机）。
///
/// 已拿到的证据（`mod_log.txt` 12:58 那场）：
/// `inProgress=True side=Player meAlive=True extraTurns=0 roomMode=ActiveCombat
///  isCurrentScreen=True handMode=Play inCardPlay=False inSelection=False`
/// —— 前置条件**全都满足**，可 `btnState=**Hidden**`。
/// 也就是说按钮是被人 `SetState(Hidden)` 之后**再也没被打开**。
///
/// 反编译 `NEndTurnButton` 的几个入口：
///   · `OnTurnStarted(state)`：玩家回合开始时 `SetState(Enabled)`（这才是把按钮亮出来的地方）；
///   · `OnAboutToSwitchToEnemyTurn(state)` / `OnCombatEnded()`：藏起来。
///
/// 所以这里打两件事：
///   ① `NEndTurnButton.SetState` 每次变化都记一行（带调用者），看是谁藏的、之后有没有人再打开；
///   ② `Hook.AfterPlayerTurnStart` 的开始/结束与耗时 —— 如果玩家回合开始的那条广播链
///      **卡在某个遗物/能力的钩子上**（比如挂在影子玩家身上的那几件），
///      `OnTurnStarted` 就永远轮不到执行，按钮也就永远是 Hidden（这和「她这一手从不开始」完全吻合）。
/// 纯诊断：不改变任何游戏行为。
/// </summary>
[HarmonyPatch(typeof(NEndTurnButton), "SetState")]
public static class EndTurnButtonStateTracePatch
{
    [HarmonyPrefix]
    public static void Prefix(NEndTurnButton __instance, object newState)
    {
        try
        {
            string caller = "?";
            try
            {
                var frames = new StackTrace().GetFrames();
                for (int i = 1; i < frames.Length && i < 6; i++)
                {
                    var m = frames[i]?.GetMethod();
                    if (m == null) continue;
                    if (m.Name is "Prefix" or "MoveNext") continue;
                    caller = m.DeclaringType?.Name + "." + m.Name;
                    break;
                }
            }
            catch { /* 忽略 */ }

            string before = "?";
            try
            {
                var f = typeof(NEndTurnButton).GetField("_state",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                before = f?.GetValue(__instance)?.ToString() ?? "?";
            }
            catch { /* 忽略 */ }

            Logger.Log($"[CharBoss] 结束回合按钮状态：{before} → {newState}（调用者 {caller}）");
        }
        catch { /* 绝不打断游戏 */ }
    }
}

/// <summary>`Hook.AfterPlayerTurnStart` 的开始/结束/耗时（判断玩家回合开始这条广播链有没有卡住）。</summary>
[HarmonyPatch(typeof(Hook), nameof(Hook.AfterPlayerTurnStart))]
public static class PlayerTurnStartTracePatch
{
    [HarmonyPrefix]
    public static void Prefix(ICombatState combatState, PlayerChoiceContext choiceContext, Player player)
    {
        try
        {
            string who = "?";
            try { who = player == null ? "null" : (player.Creature?.Monster?.GetType().Name ?? "真人玩家"); }
            catch { who = "?"; }
            Logger.Log($"[CharBoss] 玩家回合开始钩子：开始（{who}）");
            HookWatch.MarkStart();
        }
        catch { /* 忽略 */ }
    }

    [HarmonyPostfix]
    public static void Postfix(ref Task __result)
    {
        try
        {
            if (__result == null) return;
            HookWatch.Attach(__result);
        }
        catch { /* 忽略 */ }
    }
}

/// <summary>把「钩子什么时候真的结束」打进日志（`Task` 完成时回调）。</summary>
internal static class HookWatch
{
    private static long _startTicks;

    internal static void MarkStart()
    {
        _startTicks = Stopwatch.GetTimestamp();
    }

    internal static void Attach(Task task)
    {
        task.ContinueWith(t =>
        {
            try
            {
                double ms = _startTicks == 0 ? -1
                    : (Stopwatch.GetTimestamp() - _startTicks) * 1000.0 / Stopwatch.Frequency;
                if (t.IsFaulted)
                {
                    // ⚠ 打**完整异常**（含堆栈）：只打 Message 的话永远看不出是谁抛的。
                    // 注意 async 方法里的异常被框架塞进 Task，不会穿过方法边界 —— 所以只能在这里看。
                    Logger.Log($"[CharBoss] 玩家回合开始钩子：异常（耗时 {ms:0}ms）元凶与堆栈：\n{t.Exception}");
                    return;
                }
                string state = t.IsCanceled ? "取消" : "完成";
                Logger.Log($"[CharBoss] 玩家回合开始钩子：{state}（耗时 {ms:0}ms）");
            }
            catch { /* 忽略 */ }
        }, TaskScheduler.Default);
    }
}
