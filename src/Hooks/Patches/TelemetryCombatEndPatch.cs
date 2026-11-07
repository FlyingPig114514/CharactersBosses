using System;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace CharacterBosses.Patches;

/// <summary>
/// **战斗结束兜底**：`Hook.AfterCombatEnd` 之后，把这一场（我们参与过的）Boss 战写成一行数据。
///
/// 为什么需要它（用户 2026-10-31 提问）：
///   「只在本场最后一只 Boss 被移出房间时写一行 —— 那如果玩家全死了，最后一只 Boss 还会被移出房间吗？」
///   答案是不一定：战败时房间是被**直接拆掉**的，不保证每只怪物都走 `BeforeRemovedFromRoom`
///   ⇒ 只靠那条路会**漏掉所有「输」的局**（表里只剩赢的，平衡数据全歪）。
///
/// 反编译确认（`CombatManager.EndCombatInternal`）：**无论胜负**都会 `await Hook.AfterCombatEnd(...)`，
/// 而 `Hook.AfterCombatVictory(...)` 只在胜利时调 ⇒ 这里就是「战斗真的结束了」的可靠时机。
/// 写库那一步是**幂等**的（`Telemetry` 里有 `_fightWritten` 去重），
/// 所以胜利时（已经由「最后一只被移出」写过）不会被写成两行。
/// </summary>
[HarmonyPatch(typeof(Hook), nameof(Hook.AfterCombatEnd),
              new[] { typeof(IRunState), typeof(ICombatState), typeof(CombatRoom) })]
public static class TelemetryCombatEndPatch
{
    [HarmonyPostfix]
    public static void Postfix(IRunState? runState, ICombatState? combatState)
    {
        try { Core.Telemetry.FinalizeFightAtCombatEnd(combatState, runState); }
        catch (Exception e) { Logger.Log("[CharBoss] 战斗结束数据汇总失败（忽略）: " + e.Message); }
    }
}
