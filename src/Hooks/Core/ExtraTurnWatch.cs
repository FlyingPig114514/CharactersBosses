using System.Collections.Generic;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;

namespace CharacterBosses.Core;

/// <summary>
/// 「**当前这一次玩家回合是不是额外回合**」的唯一判据。
///
/// 背景（用户 2026-11-19）：
/// > 「玩家的额外回合开始时（如佩尔之眼、倒吊人、龙涎香、腾跃等提供的），所有怪物的恶魔形态也会触发，
/// >  但实际上怪物并没有真的过去一个回合」
///
/// 我们把几条「怪物每次真回合才该 +1 次」的效果（铁甲战士支配的**恶魔形态**、观者支配的**虔信/真言**）
/// 都挂在了 `AfterSideTurnStart(Player)`，而**额外回合也会派发一次同一个钩子**
/// ⇒ 怪物一回合成两三回合地叠。这里给出「这是不是额外回合」的官方判据，
/// 各调用方拿到 true 就**整条跳过**（不进结算、不播特效）。
///
/// ## 判据来源（反编译 `MegaCrit.Sts2.Core.Combat.CombatManager`）
/// 玩家回合结束时跑 `SwitchFromPlayerToEnemySide`：
/// <code>
/// turnState.PlayersTakingExtraTurn.Clear();
/// foreach (Player p in turnState.State.Players)
///     if (Hook.ShouldTakeExtraTurn(turnState.State, p, out AbstractModel source))
///         turnState.PlayersTakingExtraTurn.Add(p);
/// ...
/// SwitchSides(turnState);     // CurrentSide == Player &amp;&amp; 列表非空 ⇒ 保持 Player（不切敌方）
/// </code>
/// 而 `Hook.ShouldTakeExtraTurn` 会把**所有合法来源**问两轮
/// （先 `ShouldTakeExtraTurnEarly`，再 `ShouldTakeExtraTurn`）——
/// 佩尔之眼 `PaelsEye` 走 Early、`ExtraTurnPower`（龙涎香 / 腾跃 / 倒吊人 / 任何 mod 给的）走后者，
/// 所以**任何来源都被这份列表覆盖**，不需要认具体遗物/能力牌类型。
///
/// 时序（见 `CombatManager.StartTurn`）：
///   · 列表在**该次额外回合开始前**填好，下一次玩家回合结束才 `Clear()`
///     ⇒ 额外回合的 `AfterSideTurnStart(Player)` 触发时**一定非空**；
///   · 普通玩家回合开始时它**一定为空**（上一轮结束已清）。
/// `CombatManager.Instance.PlayersTakingExtraTurn` 的取值器在没有 turnState 时返回空数组，
/// 且该属性在 0.111 / 0.113 两份 `sts2` 里都存在。
/// </summary>
internal static class ExtraTurnWatch
{
    /// <summary>当前这一侧回合是不是额外回合（佩尔之眼 / 龙涎香 / 腾跃 / 倒吊人 / 任何 `ExtraTurnPower`）。</summary>
    internal static bool IsExtraPlayerTurn
    {
        get
        {
            try
            {
                IReadOnlyList<Player>? list = CombatManager.Instance?.PlayersTakingExtraTurn;
                return list != null && list.Count > 0;
            }
            catch
            {
                // 取不到就按「普通回合」处理（最多是额外回合里多叠一次，不会崩）
                return false;
            }
        }
    }

    /// <summary>正在拿额外回合的玩家数（诊断用）。</summary>
    internal static int Count
    {
        get
        {
            try { return CombatManager.Instance?.PlayersTakingExtraTurn?.Count ?? 0; }
            catch { return 0; }
        }
    }
}
