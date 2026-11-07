using System;
using System.Collections.Generic;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Runs;

namespace CharacterBosses.Patches;

/// <summary>
/// 玩家侧血量统计的两个数据入口（v0.2.264，用户 2026-10-31）。
///
/// 为什么需要：现有 `boss_fight` 只有 Boss 的血量，玩家赢了就只剩 `result=win` 与 Boss 的 0 血 ——
/// 「轻松赢」和「险胜」在数据上完全一样，没法拿来调平衡。用户要的额外信息是：
/// 玩家最大生命、进场血量、结束血量、**战斗中掉的血**、**战斗中回的血**。
///
/// 为什么不用「进场血量 − 结束血量」：缩放仪（Pantograph，Boss 战开局回 25）、再生药水这类会在
/// 战斗中回血，差值会把「实际挨了多少」抹平。掉血 / 回血必须**分别**从 HP 变化事件里累加。
///
/// 为什么挂在 `Hook` 的静态方法上：这两条是**同步的、两端都会 await 的**钩子。
/// 帧回调里改/统计战斗状态是踩过的坑（钻石头冠 20 格挡联机不同步那次，见 `ShadowDiamondDiademOncePatch`）。
/// </summary>
[HarmonyPatch(typeof(Hook), nameof(Hook.BeforeCombatStart), new[] { typeof(IRunState), typeof(ICombatState) })]
public static class TelemetryCombatStartSnapshotPatch
{
    /// <summary>
    /// Prefix（不是 Postfix）：缩放仪之类的「开局回血」发生在战斗开始的钩子链里，
    /// 我们要的是**任何开局回血之前**的进场血量；那些回血随后会进 `pHeal`。
    /// </summary>
    [HarmonyPrefix]
    public static void Prefix(ICombatState combatState)
    {
        try { CharacterBosses.Core.Telemetry.NoteCombatStarting(combatState); }
        catch (Exception e) { Logger.Log("[CharBoss] 玩家血量快照失败（忽略）: " + e.Message); }
    }
}

/// <summary>
/// `Hook.AfterCurrentHpChanged(runState, combatState, creature, delta)`：任何生物的当前生命变化。
/// 我们只累计**真人玩家**（影子 Player 的 Creature 就是 Boss 本体，必须滤掉），并区分掉血 / 回血。
/// </summary>
[HarmonyPatch(typeof(Hook), nameof(Hook.AfterCurrentHpChanged),
              new[] { typeof(IRunState), typeof(ICombatState), typeof(Creature), typeof(decimal) })]
public static class TelemetryPlayerHpChangedPatch
{
    [HarmonyPostfix]
    public static void Postfix(ICombatState combatState, Creature creature, decimal delta)
    {
        try { CharacterBosses.Core.Telemetry.NotePlayerHpChanged(combatState, creature, delta); }
        catch (Exception e) { Logger.Log("[CharBoss] 玩家血量累计失败（忽略）: " + e.Message); }
    }
}
