using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Models;

namespace CharacterBosses.Patches;

/// <summary>
/// 让 <c>CombatState.Contains(AbstractModel)</c> 变成**空引用安全**。
///
/// 这是一个兼容性补丁。日志里抓到的真实崩溃：
///
/// ```
/// NullReferenceException
///   at CombatState.Contains(AbstractModel model)
///   at CombatState.IterateHookListeners()
///   at STS2RitsuLib.Models.Capabilities.ModelCapabilityHookListeners.ExpandOwnerHookListeners(...)
///   at Hook.ModifyMaxEnergy(...)  ← 由 PlayerCombatState.MaxEnergy 触发
///   at CharBossMonster.PrepareTurn()
/// ```
///
/// 原版 `Contains` 里是 `relicModel.Owner.IsActiveForHooks` / `powerModel.Owner.CombatState` 这种写法，
/// 一旦某个监听模型（遗物/能力/球）的 `Owner` 为 null 就直接炸。而 **RitsuLib** 会把监听者再展开一层、
/// 逐个问 `Contains`，于是一个这种模型就能把整条「回合开始」链路打断：
/// 计划算不出来、手牌 UI 建不出来、玩家侧回合也起不来（用户实测：进战斗直接卡死）。
///
/// 这里只吞掉 `NullReferenceException` 并返回 false（即「这个模型不算在战斗里」），
/// 其它异常照常抛出；同时打一条日志，方便定位到底是哪个模型没有 Owner。
/// </summary>
[HarmonyPatch(typeof(CombatState), "Contains")]
public static class CombatContainsSafePatch
{
    [HarmonyFinalizer]
    public static Exception? Finalizer(Exception? __exception, ref bool __result, AbstractModel? model)
    {
        if (__exception is not NullReferenceException) return __exception;

        __result = false;

        string name;
        try { name = model == null ? "<null>" : model.Id.Entry; }
        catch { name = model?.GetType().Name ?? "<null>"; }

        Logger.Log($"[CharBoss] Contains 空引用已忽略（模型 {name}，多半是没有 Owner）");
        return null;
    }
}
