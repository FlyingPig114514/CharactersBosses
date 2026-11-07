using System;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using CharacterBosses.Monsters;

namespace CharacterBosses.Powers;

/// <summary>
/// 第二层 Boss「灵魂骨」的特殊效果「**灵魂绑定**」。
///
/// 用户设计（2026-10-22）：**你在回合内抽牌时，也视为亡灵契约师抽牌**
/// ——会增加她的「自动化」计数与「死亡行军」的伤害。
///
/// 实现见 <see cref="Patches.SoulBoundDrawPatch"/>：
///   · 玩家（真人）在玩家回合内抽到一张牌时，如果她身上有本 Power，就给**她**补一条
///     `CardDrawnEntry`（死亡行军的伤害按「本回合她自己抽到几张」算），
///     并且替她跑一遍 <c>Hook.AfterCardDrawn</c>（「自动化」这类「每抽 N 张」的能力靠这条计数）。
/// 本类自己只有「标记 + 描述」，不带任何战斗数值。
/// </summary>
public sealed class BossSoulBoundPower : PowerModel
{
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Single;

    public override bool ShouldPlayVfx => false;

    /// <summary>场上有没有「灵魂绑定」生效的 Boss（补丁用它决定要不要替她记账）。</summary>
    public static CharBossMonster? OwnerBoss(ICombatState? combat)
    {
        if (combat == null) return null;
        try
        {
            foreach (var creature in combat.Creatures)
            {
                if (creature?.Monster is not CharBossMonster boss) continue;
                if (creature.GetPower<BossSoulBoundPower>() != null) return boss;
            }
        }
        catch { /* 忽略 */ }
        return null;
    }
}
