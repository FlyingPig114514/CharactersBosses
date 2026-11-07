using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models.Relics;
using CharacterBosses.Monsters;

namespace CharacterBosses.Patches;

/// <summary>
/// 「**情感芯片**」（<c>EmotionChip</c>）在**我们的影子 Boss** 身上的接管（用户 2026-11-03）。
///
/// 原版钩子是 `AfterPlayerTurnStart`，它会用「上一个玩家回合」的战斗历史判断「她上一回合掉过血没有」。
/// 影子玩家的回合号是我们手动对齐的 ⇒ 那条判定对她从来不是真（实测一次都没触发）。
///
/// 所以我们自己算（见 <c>Core/EmotionChipPlays</c>：逐回合记她的生命基线，掉了血就在下个回合开始
/// 触发所有充能球的被动），这里把**原版那条**对我们这只 Boss 关掉，避免以后哪天它又能触发时**双份**。
/// 真人玩家、其它怪物完全不受影响。
///
/// <para>
/// ⚠ **2026-11-04 修的致命 bug（就是「事件机没有结束回合按钮」的真凶）**：
/// 这里原先只 <c>return false</c>、**没有给 <c>__result</c> 赋值**。而
/// <c>EmotionChip.AfterPlayerTurnStart</c> 是 **async** 方法（反编译可见
/// <c>[AsyncStateMachine(typeof(&lt;AfterPlayerTurnStart&gt;d__5))]</c>，方法体只是
/// <c>builder.Start(...)</c> + <c>return builder.Task</c>）。
/// Harmony 用 Prefix 跳过它 ⇒ 方法体不执行 ⇒ 返回 <c>default(Task)</c> = **null**
/// ⇒ <c>Hook.AfterPlayerTurnStart</c> 的状态机里
/// <c>model.AfterPlayerTurnStart(...).GetAwaiter()</c> 直接 **NullReferenceException**。
/// </para>
/// <para>
/// 后果（与 13:03 / 13:07 两场日志完全吻合）：那条链没有兜异常 ⇒ 整条「玩家回合开始」流程中断：
/// ① <c>NEndTurnButton.OnTurnStarted</c> 永远轮不到 ⇒ **结束回合按钮一直 Hidden**；
/// ② 回合结束管线从不跑；③ 她那一手永远派不下来（一手都不出）。
/// 日志顺序也对得上：`选择悖论：对她跳过`（那个 Prefix 有 <c>ref Task __result</c>）→ 下一个监听者
/// 正是情感芯片 ⇒ 立刻 `玩家回合开始钩子：异常:Object reference not set…`。
/// </para>
/// <para>
/// **教训（已进 Do-Not-Repeat）**：凡是跳过**返回 <c>Task</c> 的钩子**的 Prefix，
/// 必须 <c>ref Task __result</c> 并赋 <c>Task.CompletedTask</c>，否则返回 null Task 会把整条钩子链炸掉。
/// </para>
/// </summary>
[HarmonyPatch(typeof(EmotionChip), nameof(EmotionChip.AfterPlayerTurnStart))]
public static class EmotionChipBossTakeoverPatch
{
    [HarmonyPrefix]
    public static bool Prefix(EmotionChip __instance, PlayerChoiceContext choiceContext,
                              MegaCrit.Sts2.Core.Entities.Players.Player player, ref Task __result)
    {
        try
        {
            _ = choiceContext; _ = player;
            if (__instance?.Owner?.Creature?.Monster is not CharBossMonster) return true;   // 不是我们的 Boss：照常

            __result = Task.CompletedTask;   // ← 关键：不赋值就是 null Task ⇒ 下游 GetAwaiter() 抛 NRE
            return false;                    // 我们的 Boss：交给 EmotionChipPlays
        }
        catch
        {
            return true;
        }
    }
}

/// <summary>
/// 「**选择悖论**」（<c>ChoicesParadox</c>，三层「事件机」的遗物）对她**整条钩子关掉**。
///
/// 反编译 `ChoicesParadox.AfterPlayerTurnStart`：
/// <code>
/// if (player == Owner &amp;&amp; TurnNumber == 1) {
///     var cards = CardFactory.GetDistinctForCombat(...).ToList();   // 对我们这只 Boss 被拦成空
///     if (cards.Count != 0) await CardSelectCmd.FromSimpleGrid(...); // ← **弹一张牌的选择界面**
///     ...
/// }
/// </code>
/// 那个选择界面是**给玩家的**（`choiceContext` 来自我们替影子玩家跑钩子时传进去的上下文），
/// 而她的回合里没人会去点 ⇒ 钩子永远不返回 ⇒ **她的回合卡死**（用户 2026-11-03 报的
/// 「对战事件机时回合仍然无法正常结束」）。
///
/// 设计上也不需要它：剧本里第 1 回合的「飞跃」就是它印出来的，而且**已经带「保留」**
/// （`ScriptCard.Retain`），见 `Core/EventMachineScript.cs` 的说明。所以这里直接跳过。
/// 真人玩家的选择悖论完全不受影响。
/// </summary>
[HarmonyPatch(typeof(ChoicesParadox), nameof(ChoicesParadox.AfterPlayerTurnStart))]
public static class ChoicesParadoxBossSkipPatch
{
    [HarmonyPrefix]
    public static bool Prefix(ChoicesParadox __instance, ref Task __result)
    {
        try
        {
            if (__instance?.Owner?.Creature?.Monster is not CharBossMonster) return true;

            __result = Task.CompletedTask;
            Logger.Log("[CharBoss] 选择悖论：对她跳过（那张「飞跃 + 保留」由剧本直接给，原版会弹选择界面把她回合卡死）");
            return false;
        }
        catch
        {
            return true;
        }
    }
}