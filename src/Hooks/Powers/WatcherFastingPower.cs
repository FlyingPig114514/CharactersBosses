#if STS2_111   // 观者 Boss 只在 0.113 构建里（用户：107 不加这个 Boss）
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Models;

namespace CharacterBosses.Powers;

/// <summary>
/// 三层 2 号观者 Boss「**Ω观**」的特殊效果「**斋戒**」（用户 2026-11-16 原话）：
/// > 「斋戒：欧米伽改为隔一回合触发 1 次。点穴造成的伤害可以被你格挡。」
///
/// 两句都由**这个 power 的存在**当作开关（效果本身写在两处接管里，见下面的 ①②）：
///
/// ## ① 欧米伽隔一回合触发一次
/// 反编译 `WatcherMod.OmegaPower.AfterSideTurnEnd`：每回合结束对所有敌人造成 `MagicNumber` 点伤害。
/// 我们的做法是压掉原版那条钩子，改成「只有**奇数回合**才结算」——
/// 她第 5 回合打出欧米伽 ⇒ 只在 5 / 7 / 9 / 11 … 的回合结束触发，正是用户写的
/// 「只在 5、7、9、11 等回合触发」。实现见 <c>Core.WatcherOmegaMechanics</c>。
///
/// ## ② 点穴造成的伤害可以被格挡
/// 反编译 `WatcherMod.WatcherPressurePoints.OnPlay`：给目标上「印记（`MarkPower`）」后，
/// 对每个有印记的敌人 `CreatureCmd.Damage(…, marks, ValueProp 6, …)` —— 那 6 位里带了
/// **Unblockable**（原版点穴的伤害无视格挡）。有本 power 时我们把它换成**可格挡**的结算
/// （`ValueProp.Unpowered`：仍然不吃力量/虚弱，但吃格挡与「无实体」那类修正）。
///
/// 本 power 自己 `StackType.Single` ⇒ 不显示层数，只当「她的特殊效果」的载体与 buff 栏第一位。
/// </summary>
public sealed class WatcherFastingPower : PowerModel
{
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Single;

    public override bool ShouldPlayVfx => false;

    /// <summary>欧米伽的伤害是不是**这一回合**结算（奇偶由 `WatcherOmegaMechanics` 判定）。</summary>
    internal bool OmegaTriggersThisTurn(int turnNumber) => turnNumber % 2 == 1;

    // ── 她不退场就不需要额外清理；战斗结束随生物一起移除 ──
    public override Task AfterSideTurnStart(CombatSide side, IReadOnlyList<Creature> participants,
                                           ICombatState combatState)
        => Task.CompletedTask;
}
#endif
