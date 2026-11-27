using System;
using System.Collections.Generic;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;

namespace CharacterBosses.Patches;

/// <summary>
/// 给原版「广播钩子监听者」的收口 <c>Hook.IterateCombatHookListeners(combatState)</c> 加**空引用兜底**。
///
/// ## 实测崩溃（用户 2026-11-27，四人联机，「所有玩家选择回合结束，有一个 boss 被毒死，然后就卡住了」）
///
/// `godot.log` 原文（房主与客人**各一次**，一模一样）：
/// ```
/// [ERROR] Combat #18 turn loop died while its combat is in progress;
///         the combat is stuck until the room is restarted:
/// System.NullReferenceException: Object reference not set to an instance of an object.
///    at MegaCrit.Sts2.Core.Hooks.Hook.IterateCombatHookListeners(ICombatState combatState)+MoveNext()
///    at MegaCrit.Sts2.Core.Hooks.Hook.ModifyEnergyGain(ICombatState, Player, Decimal, IEnumerable`1&)
///    at MegaCrit.Sts2.Core.Commands.PlayerCmd.GainEnergy(Decimal amount, Player player)
///    at MegaCrit.Sts2.Core.Models.Relics.PaelsTears.AfterSideTurnStart(CombatSide, IReadOnlyList`1, ICombatState)
///    at MegaCrit.Sts2.Core.Hooks.Hook.AfterSideTurnStart(ICombatState, CombatSide, IReadOnlyList`1)
///    at MegaCrit.Sts2.Core.Combat.CombatManager.StartTurn(CombatTurnState, Func`1)
///    ...   at PowerModel.PoisonPower.Trigger()  ...
/// ```
///
/// ## 机制（反编译实证）
///
/// 1. 敌方回合开始的钩子广播 <c>Hook.AfterSideTurnStart</c> 里，**毒**先结算
///    （`PoisonPower.AfterSideTurnStart → Trigger → Cmd.Wait`），把带着毒的「塔罗愚」打死；
/// 2. 那一次广播的监听者快照是**死亡之前**取好的（`CombatState.IterateHookListeners` 先建整个 List，
///    再逐个 yield），所以死掉的 Boss 身上那 12 件遗物**照样被调用**；
/// 3. 其中「佩尔的泪」(`PaelsTears`)：`participants.Contains(Owner.Creature) && HadLeftoverEnergy`
///    ⇒ `PlayerCmd.GainEnergy(2, Owner)` ⇒ `<GainEnergy>` 里 `ICombatState combatState = player.Creature.CombatState;`
///    —— 而这只 Boss 已经死了（`CombatState.RemoveCreature` 会把 `Creature.CombatState` 置 null）
///    ⇒ 传进 `Hook.ModifyEnergyGain(null, …)` ⇒ `IterateCombatHookListeners(null)` ⇒ **NRE**；
/// 4. 这个异常把**整条回合循环（turn loop）打死**，原版自己打了 `the combat is stuck until the room is restarted`
///    —— 除了重开房间没有任何恢复办法，玩家看到的就是「所有人点了结束回合，然后永远停在那」。
///
/// 原版永远碰不到这件事：**原版的尸体身上没有遗物**。是我们的 Boss 把「玩家那一套（遗物/能量/卡组）」
/// 挂在了怪物 Creator 上才出现的组合，所以必须在我们的侧兜底。
///
/// ## 兜底口径
///
/// 战斗状态为 null ⇒ 这次广播没有任何合法监听者可查 ⇒ 返回**空序列**（原版本来也只会 NRE）。
/// 非 null 一律原样放行，玩法零改动；日志节流，避免刷屏。
/// </summary>
[HarmonyPatch(typeof(Hook), "IterateCombatHookListeners")]
internal static class HookIterateNullCombatStatePatch
{
    private static int _count;
    private static long _lastLogTicks;
    private static int _stackLogged;

    [HarmonyPrefix]
    public static bool Prefix(ICombatState? combatState, ref IEnumerable<AbstractModel> __result)
    {
        if (combatState != null) return true;

        __result = Array.Empty<AbstractModel>();

        try
        {
            _count++;
            long now = DateTime.UtcNow.Ticks;
            if (_count <= 3 || now - _lastLogTicks > TimeSpan.TicksPerSecond * 10)
            {
                _lastLogTicks = now;
                Logger.Log("[CharBoss] 钩子广播兜底：战斗状态为 null ⇒ 本次不发任何监听者" +
                           "（典型成因：刚被毒死的 Boss 身上还挂着遗物，同一次广播继续调它 ⇒ 原版会 NRE" +
                           $"并把整条回合循环打死、只能重开房间）。累计 {_count} 次");
                if (_stackLogged < 2)
                {
                    _stackLogged++;
                    try
                    {
                        var lines = (Environment.StackTrace ?? string.Empty)
                            .Split('\n', StringSplitOptions.RemoveEmptyEntries);
                        var head = new List<string>();
                        for (int i = 0; i < lines.Length && head.Count < 12; i++)
                        {
                            string t = lines[i].Trim();
                            if (t.Length == 0) continue;
                            head.Add(t);
                        }
                        Logger.Log("[CharBoss] 钩子广播兜底调用栈（前 2 次才打）: « " + string.Join(" « ", head));
                    }
                    catch { /* 忽略 */ }
                }
            }
        }
        catch { /* 日志失败不影响放行 */ }

        return false;
    }
}

/// <summary>
/// 同一条链路的**第二道**兜底：`PlayerCmd.GainEnergy(amount, player)`。
///
/// 反编译（`PlayerCmd.&lt;GainEnergy&gt;d__3`）：
/// <code>
/// ICombatState combatState = player.Creature.CombatState;
/// decimal finalAmount = Hook.ModifyEnergyGain(combatState, player, amount, out var modifiers);
/// await Hook.AfterModifyingEnergyGain(combatState, modifiers);
/// if (finalAmount > 0) { SfxCmd.Play(...); player.PlayerCombatState.GainEnergy(finalAmount); }
/// </code>
/// 上面的广播兜底能让 `ModifyEnergyGain` 不炸，但**后面还有两步**：广播
/// `AfterModifyingEnergyGain` 与 `player.PlayerCombatState.GainEnergy(...)`。
/// 对一只**已经死了的 Boss**（`Creature.CombatState` 为 null、`PlayerCombatState` 也可能已经不在了）
/// 这两步都可能再抛一次 —— 原版同样只会把整条回合循环打死。
/// 既然「尸体拿能量」本来就没有任何意义，这里直接跳过：不动别人的任何行为，
/// 只对「没有战斗状态的玩家」短路。
/// </summary>
[HarmonyPatch(typeof(PlayerCmd), "GainEnergy")]
internal static class GainEnergyWithoutCombatStatePatch
{
    private static int _count;
    private static long _lastLogTicks;

    [HarmonyPrefix]
    public static bool Prefix(Player? player, ref System.Threading.Tasks.Task __result)
    {
        try
        {
            if (player == null) return true;
            var creature = player.Creature;
            if (creature == null) return true;
            if (creature.CombatState != null) return true;

            __result = System.Threading.Tasks.Task.CompletedTask;

            _count++;
            long now = DateTime.UtcNow.Ticks;
            if (_count <= 3 || now - _lastLogTicks > TimeSpan.TicksPerSecond * 10)
            {
                _lastLogTicks = now;
                Logger.Log("[CharBoss] 加能量兜底：这只「玩家」已经不在战斗里（Boss 尸体，" +
                           $"Creature.CombatState = null）⇒ 跳过给能量（原版这里会 NRE 并把回合循环打死）。累计 {_count} 次");
            }

            return false;
        }
        catch
        {
            return true;   // 兜底自己出错就放行原版
        }
    }
}
