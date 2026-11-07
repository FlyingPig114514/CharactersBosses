using System;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using CharacterBosses.Core;

namespace CharacterBosses.Powers;

/// <summary>
/// 二层「**放血战**」的特殊效果「**死亡收割**」（用户 2026-10-28 改稿，取代原来的「扯碎」）：
///
/// > 每当铁甲战士造成**未被格挡的伤害**时，他恢复等同于该伤害量的生命值。
///
/// 挂点：<see cref="AfterDamageGiven"/>（和一层「毒刺」`PoisonStrikePower` 同一个钩子），
/// 只认 **dealer == 他**、且那段伤害**没有被格挡**（`DamageResult.UnblockedDamage`）的部分。
/// 回血走 `CreatureCmd.Heal`，与任何回血遗物（燃烧之血等）互不干扰。
/// </summary>
public sealed class BossRendingPower : PowerModel
{
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Single;

    public override bool ShouldPlayVfx => false;

    private int _logs;

    public override async Task AfterDamageGiven(PlayerChoiceContext choiceContext, Creature dealer,
                                                DamageResult result, ValueProp props, Creature target,
                                                CardModel? cardSource)
    {
        try
        {
            var self = Owner;
            if (self == null || !self.IsAlive || dealer == null) return;
            if (!ReferenceEquals(dealer, self)) return;          // 只认**他**造成的伤害
            if (target == null || ReferenceEquals(target, self)) return;   // 自己伤自己不算

            int unblocked = 0;
            try { unblocked = result.UnblockedDamage; } catch { unblocked = 0; }
            if (unblocked <= 0) return;

            var ctx = choiceContext ?? new BlockingPlayerChoiceContext();
            _ = ctx;   // Heal 不需要 ctx（签名：Heal(Creature, decimal, bool playAnim)）
            await CreatureCmd.Heal(self, unblocked, true);

            if (_logs++ < 20)
            {
                Logger.Log($"[CharBoss] 死亡收割：他对 {SafeName(target)} 造成 {unblocked} 点未被格挡伤害 → 回复 {unblocked} 生命");
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 死亡收割结算失败: " + e.Message);
        }
    }

    private static string SafeName(Creature? c)
    {
        try { return c?.Name ?? "?"; } catch { return "?"; }
    }
}
