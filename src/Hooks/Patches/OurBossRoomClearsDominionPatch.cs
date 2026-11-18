#if STS2_113
using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace CharacterBosses.Patches;

/// <summary>
/// **客人端「Boss 房清支配」补一次**。
///
/// 用户 2026-11-19：「一层 boss 第一次进会不同步」。两端日志实证（客人 `mod_log`）：
/// <code>
/// ⚠ 不同步状态摘要：轮次 1
///   遭遇=NECROBINDER_DOOM_BOSS_ENCOUNTER 本机=客人 **本幕存档 Boss 槽=LAGAVULIN_MATRIARCH_BOSS**
/// </code>
/// 也就是说**客人端的 `Act.BossEncounter` 槽位还是原版 Boss**（拉古林母虫），
/// 我们真正打的那场是我们替换进去的 `NECROBINDER_DOOM_BOSS_ENCOUNTER`（房主那边槽位已替换）。
/// 而原版判定「进 Boss 房要清掉本幕支配」看的就是槽位 id：
/// <code>
/// if (combatRoom.Encounter.Id == Act.BossEncounter.Id || == Act.SecondBossEncounter?.Id)
///     { RunState.SetDominion(null); combatRoom.CombatState.SetDominion(null); }
/// </code>
/// ⇒ 客人端这一比**对不上**（房间遭遇是我们的、槽位是原版的）⇒ **支配没被清掉**，
/// 于是 Boss 战里玩家身上还挂着 `NECROBINDER_DOMINION_POWER`（上面那份状态转储里四个玩家身上都有），
/// 房主端清掉了 ⇒ 第一个 `After player turn start` 校验和就分叉。
///
/// 修法：在 `DreadRunModel.AfterRoomEntered` 之后补一刀 —— 只要这一场是**我们替换进去的 Boss 遭遇**
/// （`RoomType.Boss` + 遭遇 id 是我们的），就照原版同样做一次「清支配」。
/// 房主端槽位本来就是我们那只 ⇒ 原版已经清过，这里看到已是 null 就直接跳过（幂等）。
/// </summary>
[HarmonyPatch(typeof(MegaCrit.Sts2.Core.Models.Dread.DreadRunModel), "AfterRoomEntered")]
internal static class OurBossRoomClearsDominionPatch
{
    private static int _logs;

    [HarmonyPostfix]
    public static void Postfix(AbstractRoom room)
    {
        try
        {
            if (room is not CombatRoom cr) return;

            var enc = cr.Encounter;
            if (enc == null) return;
            if (enc.RoomType != RoomType.Boss) return;                       // 只认 Boss 房间

            bool ours;
            try { ours = OurEncounterIconPaths.IsOurEncounter(enc.Id); } catch { ours = false; }
            if (!ours) return;                                              // 原版 Boss：不干预

            var run = MegaCrit.Sts2.Core.Runs.RunManager.Instance?.DebugOnlyGetState();
            var combat = cr.CombatState;

            bool runHas = run?.Dominion != null;
            bool combatHas = false;
            try { combatHas = combat?.Dominion != null; } catch { combatHas = false; }
            if (!runHas && !combatHas) return;                              // 已经清过了（房主端通常走这里）

            Logger.Log("[CharBoss] Boss 房（我们的 Boss 遭遇，但本机槽位是原版）：" +
                       "补一次「清掉本幕支配」——否则 Boss 战里支配还挂在玩家身上 ⇒ 与房主不同步");
            try { run?.SetDominion(null); } catch (Exception e) { Logger.Log("[CharBoss] 清支配（RunState）失败: " + e.Message); }
            try { combat?.SetDominion(null); } catch (Exception e) { Logger.Log("[CharBoss] 清支配（CombatState）失败: " + e.Message); }

            if (_logs++ < 8)
                Logger.Log($"[CharBoss] Boss 房清支配完成（遭遇 {enc.Id.Entry}）");
        }
        catch (Exception e) { Logger.Log("[CharBoss] Boss 房清支配补丁失败（忽略）: " + e.Message); }
    }
}
#endif
