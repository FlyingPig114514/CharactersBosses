using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using CharacterBosses.Core;

namespace CharacterBosses.Powers;

/// <summary>
/// 三层「**巨石战**」的特殊效果「**原始力量**」（用户 2026-10-28 重做版）：
///
/// > 铁甲战士使用**巨石**（`GiantRock`）时，获得 **1 点力量** 与 **3 点覆甲**；
/// > 巨石对你造成**未被格挡的伤害**时，**你抽 1 张牌**。
///
/// 两条分别挂：
///   · `AfterCardPlayed`：他打出 `GIANT_ROCK` ⇒ `StrengthPower +1`、`PlatingPower +3`；
///   · `AfterDamageGiven`：来源是巨石、目标是真人玩家、且有未格挡伤害 ⇒ 该玩家抽 1 张。
/// </summary>
public sealed class BossPrimalForcePower : PowerModel
{
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Single;

    public override bool ShouldPlayVfx => false;

    private const int StrengthPerRock = 1;
    private const int PlatingPerRock = 3;
    /// <summary>「巨石」造成未被格挡伤害时，该玩家获得的**虚弱**层数（用户 2026-11-11：改回破防上虚弱）。</summary>
    private const int WeakOnUnblocked = 1;

    private int _logs;

    public override async Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        try
        {
            var self = Owner;
            if (self == null || !self.IsAlive) return;
            if (cardPlay?.Card == null) return;
            if (!ReferenceEquals(cardPlay.Card.Owner?.Creature, self)) return;

            string entry;
            try { entry = cardPlay.Card.Id.Entry; } catch { return; }
            if (entry != "GIANT_ROCK") return;

            var ctx = choiceContext ?? new BlockingPlayerChoiceContext();
            await PowerCmd.Apply<StrengthPower>(ctx, self, StrengthPerRock, self, null);
            await PowerCmd.Apply<PlatingPower>(ctx, self, PlatingPerRock, self, null);

            if (_logs++ < 20)
            {
                Logger.Log($"[CharBoss] 原始力量：他打出巨石 → +{StrengthPerRock} 力量、+{PlatingPerRock} 覆甲");
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 原始力量（用巨石）结算失败: " + e.Message);
        }
    }

    public override async Task AfterDamageGiven(PlayerChoiceContext choiceContext, Creature dealer,
                                                DamageResult result, ValueProp props, Creature target,
                                                CardModel? cardSource)
    {
        try
        {
            var self = Owner;
            if (self == null || !self.IsAlive) return;
            if (dealer == null || !ReferenceEquals(dealer, self)) return;   // 必须是他的巨石
            if (target == null || target.Monster != null) return;           // 只认真人玩家

            bool fromRock = false;
            try { fromRock = cardSource != null && cardSource.Id.Entry == "GIANT_ROCK"; } catch { fromRock = false; }
            if (!fromRock) return;

            int unblocked = 0;
            try { unblocked = result.UnblockedDamage; } catch { unblocked = 0; }
            if (unblocked <= 0) return;

            var player = target.Player;
            if (player == null) return;

            var ctx = choiceContext ?? new BlockingPlayerChoiceContext();
            // ★ 2026-11-11（用户）：「改回巨石破防上虚弱，每次巨石 +1 力量、+3 覆甲，代码和描述统一修改」
            //   以前这里是该玩家**抽 1 张牌**（v0.2.382 的写法）；现在改成给该玩家 **1 层虚弱**，
            //   与「原始力量」的本地化文案一致（`BOSS_PRIMAL_FORCE_POWER.description` /
            //   `IRONCLAD_BOSS.boulder`）。
            await PowerCmd.Apply<WeakPower>(ctx, target, WeakOnUnblocked, self, cardSource);

            if (_logs++ < 20)
            {
                Logger.Log($"[CharBoss] 原始力量：巨石对 {target.Name} 造成 {unblocked} 点未被格挡伤害 → 获得 {WeakOnUnblocked} 层虚弱");
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 原始力量（抽牌）结算失败: " + e.Message);
        }
    }
}
