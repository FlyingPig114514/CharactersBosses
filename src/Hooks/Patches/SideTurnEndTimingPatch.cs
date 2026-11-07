using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Hooks;

namespace CharacterBosses.Patches;

/// <summary>
/// **敌方回合结束的计时探针**（用户 2026-10-29：「炸弹和冰雹风暴的结算有点延后，
/// 在最后一个 Boss 出完牌后又隔了快 2 秒才结算」）。
///
/// 炸弹（`TheBombPower.BeforeSideTurnEnd`）和冰雹风暴都属于「敌方阵营回合结束」时结算的东西，
/// 它们的**时机完全由原版**派发 <c>Hook.BeforeSideTurnEnd</c> 决定。所以这里只做两件事：
///   1. 在我们自己的回合收尾跑完时记一个时间戳（<see cref="MarkOwnTurnEnded"/>）；
///   2. 原版派发 `Hook.BeforeSideTurnEnd` 时打一行「距我们收尾 +Nms」。
///
/// 这样下一次日志里就能一眼分清：
///   · 差值很小（几十 ms）⇒ 那 2 秒花在**我们这边**（看 `回合收尾耗时:` 那一行）；
///   · 差值 ~2 秒 ⇒ 那 2 秒是**原版**在派发前的等待（卡面/意图动画、多人同步等），
///     需要另找切入点（例如原版 `CombatManager` 的 end-side 流程）。
///
/// 只在**敌方阵营**（有我们 Boss 的那一侧）打日志，避免玩家回合也刷。
/// ★ 双版本（v0.107 正式版 / v0.111 beta）：新版派发入口叫 `Hook.BeforeSideTurnEnd`，
///   **旧版没有这个静态入口**，旧版对应的是 `Hook.BeforeTurnEnd`（参数完全一致）。
///   所以不写死类型，改成运行时找：找到哪个挂哪个；两个都没有就整类跳过（这只是探针，跳过不影响玩法）。
/// </summary>
[HarmonyPatch]
internal static class SideTurnEndTimingPatch
{
    [HarmonyPrepare]
    public static bool Prepare()
        => Core.GameCompat.HasBeforeSideTurnEnd || Core.GameCompat.HasHookBeforeTurnEnd;

    internal static System.Reflection.MethodBase? TargetMethod()
        => AccessTools.Method(typeof(Hook), "BeforeSideTurnEnd")
           ?? AccessTools.Method(typeof(Hook), "BeforeTurnEnd");

    private static long _ownTurnEndedAt;

    /// <summary>我们自己的回合收尾（`CharBossMonster.PlayTurn` 末尾）跑完时调用。</summary>
    internal static void MarkOwnTurnEnded()
    {
        try { _ownTurnEndedAt = Environment.TickCount64; } catch { /* 忽略 */ }
    }

    [HarmonyPrefix]
    public static void Prefix(ICombatState combatState, CombatSide side, IEnumerable<Creature> participants)
    {
        try
        {
            if (combatState == null || participants == null) return;

            // 只在「这一侧有我们的 Boss」时记录（玩家侧不关心）
            bool hasOurs = false;
            foreach (var c in participants)
            {
                if (c?.Monster is Monsters.CharBossMonster) { hasOurs = true; break; }
            }
            if (!hasOurs) return;

            long now = Environment.TickCount64;
            long gap = _ownTurnEndedAt > 0 ? now - _ownTurnEndedAt : -1;
            Logger.Log($"[CharBoss] 敌方回合结束派发（BeforeSideTurnEnd）：" +
                       (gap >= 0 ? $"距我们上次回合收尾 +{gap}ms" : "（没有记录到我们这边的收尾时间）") +
                       " —— 炸弹/冰雹风暴等「回合结束」效果从这一刻开始结算");
        }
        catch { /* 忽略：探针绝不能影响战斗 */ }
    }
}
