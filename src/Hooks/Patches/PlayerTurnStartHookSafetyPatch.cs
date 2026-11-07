using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using CharacterBosses.Monsters;

namespace CharacterBosses.Patches;

/// <summary>
/// **玩家回合开始那条钩子链不许把整条回合开始流程带崩**。
///
/// <para>
/// 背景（用户 2026-11-04「事件机没有结束回合按钮」）：`Hook.AfterPlayerTurnStart` 在这条
/// 链上出了问题 ⇒ `NEndTurnButton.OnTurnStarted` 永远轮不到执行 ⇒ 按钮一直 `Hidden`，
/// 回合结束管线也不跑，她一手都不出。
/// </para>
///
/// <para>
/// ⚠ **v0.2.355 的 finalizer 为什么一点用都没有（已实证，别再走一遍）**：
/// 反编译 `Hook.AfterPlayerTurnStart`（`references/sts2.dll`，`Hook.&lt;AfterPlayerTurnStart&gt;d__56`）：
/// <code>
/// [AsyncStateMachine(typeof(&lt;AfterPlayerTurnStart&gt;d__56))]
/// public static Task AfterPlayerTurnStart(...) { builder.Start(...); return builder.Task; }
/// // 而状态机 MoveNext 里：
/// catch (System.Exception exception) {
///     &lt;&gt;1__state = -2;
///     builder.SetException(exception);   // ← 异常被 **自己吞进 Task**，**不会**穿过方法边界
///     return;
/// }
/// </code>
/// 也就是说：**async 方法的整个方法体都在状态机里**，Harmony 的
/// `[HarmonyFinalizer]` 挂在**那个返回 Task 的壳**上，壳本身永远正常返回
/// ⇒ `__exception` 永远是 null，finalizer 一次都不会进（跟 `CombatHasOurBoss` 判断无关）。
/// 同理 `[HarmonyFinalizer]` 挂在状态机的 `MoveNext` 上也没用：异常根本不会逃出 MoveNext。
/// **唯一能拦的地方是「拿到 Task 之后」** —— 所以这里改成 Postfix。
/// </para>
///
/// <para>
/// 本补丁做两件事（**只对有我们的 Boss 的战斗生效**，普通战斗原样不动）：
/// ① 返回的 Task 是 **null**（某个 Prefix 跳过 async 钩子却没给 `__result` —— v0.2.355 事件机的真凶就是
///    `EmotionChipBossTakeoverPatch` 这写法，已单独修掉）或**已经同步 faulted**，就换成
///    `Task.CompletedTask`，让回合开始流程继续走完；
/// ② 顺手把完整异常（含堆栈，**指名道姓**是谁抛的）打进日志。
/// </para>
///
/// <para>
/// 局限（写清楚，免得下次又误判）：如果异常是在**第一次 await 之后**才发生，Postfix 拿到时 Task 还没完成，
/// 这里拦不住（只能靠 `EndTurnButtonTracePatch.HookWatch` 的完成回调把堆栈记下来）。
/// 本次事故是**同步**抛的（日志「耗时 2ms」），这条保险对它有效。
/// </para>
/// </summary>
[HarmonyPatch(typeof(Hook), nameof(Hook.AfterPlayerTurnStart))]
public static class PlayerTurnStartHookSafetyPatch
{
    [HarmonyPostfix]
    public static void Postfix(ref Task __result, ICombatState combatState,
                               PlayerChoiceContext choiceContext, Player player)
    {
        try
        {
            bool isNull = __result == null;
            bool faultedNow = false;
            try { faultedNow = __result != null && __result.IsCompleted && __result.IsFaulted; }
            catch { faultedNow = false; }

            if (!isNull && !faultedNow) return;                 // 正常：什么都不做
            if (!CombatHasOurBoss(combatState)) return;         // 不是我们的战斗：保留原版行为（便于诊断）

            string who = "?";
            try { who = player?.Creature?.Monster?.GetType().Name ?? "真人玩家"; }
            catch { /* 忽略 */ }
            _ = choiceContext;

            if (isNull)
            {
                Logger.Log($"[CharBoss] ⚠ 玩家回合开始钩子链返回了 **null 的 Task**（{who}）——" +
                           "多半是某个 Harmony Prefix 跳过了 async 钩子却没给 __result。已换成 CompletedTask 让流程继续。");
            }
            else
            {
                Logger.Log($"[CharBoss] ⚠ 玩家回合开始钩子链抛异常（{who}；已吞掉，否则整条回合开始流程会中断、" +
                           "结束回合按钮会一直是 Hidden）。元凶与堆栈：\n" + __result.Exception);
            }

            __result = Task.CompletedTask;                      // ← 让 CombatManager 继续走完回合开始
        }
        catch { /* 绝不因为诊断代码打断游戏 */ }
    }

    /// <summary>
    /// 这一场里有没有我们的 Boss。**扫全部生物列表**（`Creatures` 是全集；另外几个按视角/可打击性过滤，
    /// 单独任何一个都可能漏掉我们的影子玩家 —— v0.2.355 只扫 Allies/Enemies 就是隐患）。
    /// </summary>
    private static bool CombatHasOurBoss(ICombatState? combat)
    {
        if (combat == null) return false;
        try
        {
            if (AnyIsOurs(combat.Creatures)) return true;
            if (AnyIsOurs(combat.Allies)) return true;
            if (AnyIsOurs(combat.Enemies)) return true;
            if (AnyIsOurs(combat.PlayerCreatures)) return true;
            if (AnyIsOurs(combat.HittableEnemies)) return true;
            if (AnyIsOurs(combat.CreaturesOnCurrentSide)) return true;
        }
        catch { /* 忽略单个列表的异常 */ }
        return false;
    }

    private static bool AnyIsOurs(IReadOnlyList<Creature>? list)
    {
        if (list == null) return false;
        try
        {
            for (int i = 0; i < list.Count; i++)
                if (list[i]?.Monster is CharBossMonster) return true;
        }
        catch { /* 忽略 */ }
        return false;
    }
}
