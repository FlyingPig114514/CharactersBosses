using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using CharacterBosses.Core;

namespace CharacterBosses.Patches;

/// <summary>
/// **玩家离开战斗房、进入下一个房间**时，把我们的 Boss 战音乐还原成原版
/// （用户 2026-10-30：「胜利音效没完全播完就戛然而止」）。
///
/// 背景：原版 Boss 死亡时是给自己那首曲目发「进度=5 死亡段落」（反编译
/// `WaterfallGiant` / `SoulFysh` 等确认）。段落有多长事先不知道，用固定计时器（5 秒）会把它切断。
/// 而「玩家自己离开房间」这个时机天然就是段落的终点 ⇒ 挂在这里还原，既不切歌，
/// 也不会让 Boss 曲目拖进地图。
///
/// `RunManager.EnterMapPointInternal(int, MapPointType, AbstractRoom, bool)` 是「进入下一个地图点」
/// 的唯一入口（反编译确认，返回 Task），前缀在它开始执行时就会跑。
/// </summary>
[HarmonyPatch(typeof(RunManager), nameof(RunManager.EnterMapPointInternal), new[]
{
    typeof(int),
    typeof(MegaCrit.Sts2.Core.Map.MapPointType),
    typeof(AbstractRoom),
    typeof(bool),
})]
internal static class BossBgmRoomChangePatch
{
    [HarmonyPrefix]
    public static void Prefix()
    {
        try { BossBgm.OnRoomEntered(); } catch { /* 忽略 */ }
    }
}
