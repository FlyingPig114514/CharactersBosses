using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Orbs;
using CharacterBosses.UI;

namespace CharacterBosses.Patches;

/// <summary>
/// 让 Boss 的球位在**节点被造出来的那一刻**就按「本地玩家」样式画。
///
/// 为什么不能靠帧回调：本轮实测日志里 `帧回调 ok`（心跳）与 `球位 UI 同步` **一条都没有**，
/// 说明手牌栏那条 <c>GetTree().ProcessFrame</c> 的每帧回调并没有跑到，
/// 于是只有 <c>BossOrbDisplay.Attach</c>（战斗开始，非帧路径）掰过的槽是对的 ——
/// 也正是用户看到的「刚进战斗那一个球和两个球位对，后面生成的都不对」。
///
/// 原版只有三处会造 <c>NOrb</c> 节点，全部写死 `NOrb.Create(LocalContext.IsMe(Player))`：
///
/// | 位置 | 造出来的是什么 | 用户看到的现象 |
/// |---|---|---|
/// | <c>AddSlotAnim</c> | 空槽（战斗开始 / 扩容） | 空球位看不见 |
/// | <c>AddOrbAnim</c> | 一颗**有模型的球** | 新球数值不显示、还压暗 |
/// | <c>EvokeOrbAnim</c> | 球激发后**新建的空槽**（旧节点 0.25s 淡出后释放） | 空出来的球位看不见 |
///
/// 三个后置补丁都只做一件事：如果这个管理器属于我们的 Boss，就把它的槽掰成本地样式。
/// 玩家自己的管理器走原版逻辑，完全不受影响。
/// </summary>
[HarmonyPatch(typeof(NOrbManager), nameof(NOrbManager.AddSlotAnim))]
public static class OrbAddSlotLocalPatch
{
    [HarmonyPostfix]
    public static void Postfix(NOrbManager __instance) => OrbLocalVisuals.ForceIfOurBoss(__instance);
}

[HarmonyPatch(typeof(NOrbManager), nameof(NOrbManager.AddOrbAnim))]
public static class OrbAddOrbLocalPatch
{
    [HarmonyPostfix]
    public static void Postfix(NOrbManager __instance) => OrbLocalVisuals.ForceIfOurBoss(__instance);
}

[HarmonyPatch(typeof(NOrbManager), nameof(NOrbManager.EvokeOrbAnim))]
public static class OrbEvokeLocalPatch
{
    [HarmonyPostfix]
    public static void Postfix(NOrbManager __instance) => OrbLocalVisuals.ForceIfOurBoss(__instance);
}

/// <summary>
/// 反编译 `NOrbManager`：它在 <c>_EnterTree</c> 里挂了自己的战斗状态监听
///
/// <code>
/// CombatManager.Instance.StateTracker.CombatStateChanged += OnCombatStateChanged;
/// private void OnCombatStateChanged(CombatState _) => UpdateVisuals(OrbEvokeType.None);   // ← 清高亮
/// </code>
///
/// 原版玩家侧不怕这个：鼠标提着激发牌时每帧都会重新调 <c>UpdateVisuals(Front/All)</c>。
/// 我们的「提着牌」是本回合计划（稳定不动），所以高亮刚设上就被任意一次战斗状态变化清掉 ——
/// 表现就是「永远看不到球的待激发数值」。这里在它清完之后立刻按当前计划恢复。
/// </summary>
[HarmonyPatch(typeof(NOrbManager), "OnCombatStateChanged")]
public static class OrbStateChangedLocalPatch
{
    [HarmonyPostfix]
    public static void Postfix(NOrbManager __instance) => OrbLocalVisuals.ForceIfOurBoss(__instance);
}
