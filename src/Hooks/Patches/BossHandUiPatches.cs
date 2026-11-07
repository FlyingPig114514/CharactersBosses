using HarmonyLib;
using MegaCrit.Sts2.Core.Hooks;
using CharacterBosses.UI;

namespace CharacterBosses.Patches;

/// <summary>
/// 战斗结束时立刻清掉 Boss 手牌栏。
///
/// 之前只靠 <c>CharBossMonster.BeforeRemovedFromRoom</c> 清理，但 Boss 被打死、或房间已经切走时
/// 那条路径不一定走到，结果手牌和意图会挂在地图界面上（显示优先级压过了游戏自己的界面）。
/// 这里在战斗收尾的两个 Hook 上兜底。
/// </summary>
[HarmonyPatch(typeof(Hook), nameof(Hook.AfterCombatEnd))]
public static class ClearHandUiOnCombatEndPatch
{
    [HarmonyPostfix]
    public static void Postfix()
    {
        try { BossHandUi.EndFight("AfterCombatEnd"); }
        catch { /* 清理失败不该影响流程 */ }
    }
}

[HarmonyPatch(typeof(Hook), nameof(Hook.AfterCombatVictory))]
public static class ClearHandUiOnCombatVictoryPatch
{
    [HarmonyPostfix]
    public static void Postfix()
    {
        try { BossHandUi.EndFight("AfterCombatVictory"); }
        catch { /* 同上 */ }
    }
}
