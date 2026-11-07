using System;
using System.Collections.Generic;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Unlocks;
using CharacterBosses.Encounters;

namespace CharacterBosses.Patches;

/// <summary>
/// 把「这一幕的 Boss 槽」换成角色 Boss 的**统一入口**（供下面几个补丁共用）。
///
/// 为什么需要好几个切点：原版把两个 Boss 分开滚，
///   · <c>BossEncounter</c>（第一个）：<c>ApplyDiscoveryOrderModifications</c> 收口；
///   · <c>SecondBossEncounter</c>（进阶 10 的第三幕要连打两个）：在
///     <c>GenerateRooms</c> / <c>ValidateRoomsAfterLoad</c> 里**随机滚**出来的。
/// 只在第一个切点替换，第二个就会保持原版（用户实测：第三幕第一只能力机、第二只还是原版的实验体/女王）。
/// 所以这三个切点都调一次 <see cref="ReplaceAll"/>，并且是幂等的（已经是我们的一律跳过）。
/// </summary>
internal static class BossSlotReplacer
{
    public static void ReplaceAll(ActModel act, string reason)
    {
        try
        {
            ReplaceOne(act, act.BossEncounter, "Boss", reason);
            ReplaceOne(act, act.SecondBossEncounter, "第二个 Boss", reason, secondBoss: true);

            // ⚠ 2026-10 实测：**不要**在这里强行 `SetSecondBossEncounter` 补建第二槽。
            // 试过之后的表现是「第一、二幕也变成两个 Boss，且第三幕第二槽仍然是储君」——
            // 说明原版对那个字段的写入有自己的时机/校验，我们从这里写会被覆盖或造成多余的 Boss。
            // 第二场 Boss 的替换要换个更靠后的切点（第二场战斗房间创建时），见 docs/plans/。
        }
        catch (Exception e)
        {
            Logger.Log($"[Hook] boss slot fix failed ({reason}, non-fatal): " + e.Message);
        }
    }

    /// <summary>把「第二个 Boss 槽」显式建出来（当原版没给时）。</summary>
    private static void EnsureSecondBoss(ActModel act, string reason)
    {
        var first = act.BossEncounter;
        var second = CharBossEncounters.ForVanillaBoss(act, act.SecondBossEncounter ?? first, secondBoss: true);
        if (second == null) return;

        var current = act.SecondBossEncounter;
        if (current != null && current.Id == second.Id) return;   // 已经是第二只了

        act.SetSecondBossEncounter(second);
        Logger.Log($"[Hook] act {act.Index} 第二个 Boss槽补建({reason}): " +
                   $"{(current == null ? "<空>" : current.Id.ToString())} -> {second.Id}");
    }

    private static void ReplaceOne(ActModel act, EncounterModel? vanilla, string label, string reason,
                                   bool secondBoss = false)
    {
        if (vanilla == null) return;

        var replacement = CharBossEncounters.ForVanillaBoss(act, vanilla, secondBoss);
        if (replacement == null) return;
        if (replacement.Id == vanilla.Id) return;      // 已经是这一槽该有的那只

        // 注意：**不能**用「当前已经是我们的遭遇」当早退条件 ——
        // 第二个 Boss 槽有时会复用第一个槽（此时它已经是"我们的"第一只 Boss），
        // 那样第二个槽就永远不会被换成第二只（用户实测：第三幕两个都是储君）。
        if (CharBossEncounters.IsOurs(vanilla) && !secondBoss) return;

        // ★ v0.2.237（联机不同步）：**客人不写同步状态**。
        //   以前客人这边也会 `SetBossEncounter(我们的遭遇)`，等于客户端自己改存档状态；
        //   房主那份随后再同步过来时两边对不上 ⇒ 进 Boss 房就是 divergence
        //   （`mod_log (14).txt`：客人本地写过一次，房里打到的却还是原版 Boss）。
        //   实际生成战斗走的是 `PullNextEncounter` 的返回值（下面那行日志照打），
        //   所以不写状态也不会影响「客人打的是哪只 Boss」，只是地图/顶栏要等房主的数据同步过来才对。
        if (Core.NetRole.IsGuest)
        {
            if (_guestMirrorLogged.Add((act.Index, secondBoss)))
            {
                Logger.Log($"[Hook] act {act.Index} {label}槽：客人端**不写**同步状态（房主的结论会自己同步过来）；" +
                           $"本机按确定性规则算得 {replacement.Id}，生成战斗时就用它");
            }
            return;
        }

        if (label == "Boss") act.SetBossEncounter(replacement);
        else act.SetSecondBossEncounter(replacement);

        Logger.Log($"[Hook] act {act.Index} {label}槽替换({reason}): {vanilla.Id} -> {replacement.Id}");
    }

    /// <summary>「客人端不写同步状态」这条日志每 (幕, 是不是第二槽) 只打一次。</summary>
    private static readonly HashSet<(int Act, bool Second)> _guestMirrorLogged = new();
}

/// <summary>
/// 切点①：选 Boss 的最终收口（`BossDiscoveryOrder` → <c>_rooms.Boss</c>）。
///
/// 为什么是这个切点：它是**基类上的普通方法**（不是 virtual、也不抽象），一个 Postfix 覆盖全部幕；
/// 而且地图屏读的就是 <c>ActModel.BossEncounter</c> ⇒ 地图显示的 Boss 与实际打到的一致。
/// </summary>
[HarmonyPatch(typeof(ActModel), nameof(ActModel.ApplyDiscoveryOrderModifications))]
public static class BossSlotAssignmentPatch
{
    [HarmonyPostfix]
    public static void Postfix(ActModel __instance, UnlockState unlockState)
        => BossSlotReplacer.ReplaceAll(__instance, "发现顺序");
}

/// <summary>切点②：生成这一幕的房间时会随机滚出两个 Boss。</summary>
[HarmonyPatch(typeof(ActModel), nameof(ActModel.GenerateRooms))]
public static class BossSlotGenerateRoomsPatch
{
    [HarmonyPostfix]
    public static void Postfix(ActModel __instance) => BossSlotReplacer.ReplaceAll(__instance, "生成房间");
}

/// <summary>切点③：读档后校验房间时也会（重新）滚第二个 Boss。</summary>
[HarmonyPatch(typeof(ActModel), nameof(ActModel.ValidateRoomsAfterLoad))]
public static class BossSlotValidateAfterLoadPatch
{
    [HarmonyPostfix]
    public static void Postfix(ActModel __instance) => BossSlotReplacer.ReplaceAll(__instance, "读档校验");
}

/// <summary>切点④（读取兜底）：任何地方读 <c>SecondBossEncounter</c> 时，若还是原版的就返回我们的。</summary>
[HarmonyPatch(typeof(ActModel), "get_SecondBossEncounter")]
public static class SecondBossEncounterGetPatch
{
    [HarmonyPostfix]
    public static void Postfix(ActModel __instance, ref EncounterModel? __result)
    {
        try
        {
            var current = __result;
            if (current == null) return;

            // ⚠ 这里也不能因为「已经是我们的遭遇」就早退：第二个 Boss 槽可能正复用第一个槽的
            // 我们的遭遇，那样第二只永远上不来（用户实测：第三幕两个都是储君）。
            var replacement = CharBossEncounters.ForVanillaBoss(__instance, current, secondBoss: true);
            if (replacement == null || replacement.Id == current.Id) return;

            __result = replacement;   // 只改返回值，不动存档字段（避免在 getter 里写状态）
        }
        catch (Exception e)
        {
            Logger.Log("[Hook] SecondBossEncounter 读取兜底失败: " + e.Message);
        }
    }
}
