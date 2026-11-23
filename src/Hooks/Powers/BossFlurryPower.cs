#if STS2_111   // 观者 Boss 只在这个构建里
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using CharacterBosses.Core;

namespace CharacterBosses.Powers;

/// <summary>
/// 一层 2 号观者 Boss「**疾风观**」（设计 Key `WATCHER_FLURRY`）的特殊效果「**疾风连击**」
/// （用户 2026-11-23 原话）：
/// > 「疾风连击：每当观者的疾风连击对你造成未被格挡的伤害时，你在下回合失去 1 能量」
///
/// 挂在**观者自己身上**当载体（`BossVariant.SpecialPower`），真正生效的判定在
/// <see cref="AfterDamageGiven"/>：`dealer == 她`、来源牌是「疾风连击」、目标是真的玩家、
/// 且这一段伤害**没有被格挡**（`DamageResult.UnblockedDamage &gt; 0`）⇒ 给该玩家 1 层
/// <see cref="FlurryDrainedPower"/>。
///
/// ★ 用户订正（2026-11-23）：「疾风连击是单端伤害，自然是**单张 1 层**」——
///   疾风连击是单体单段攻击 ⇒ 一次伤害事件 = 一张牌 ⇒ 正好 1 层；不做「每段叠一层」的处理。
///
/// 写法完全照 <see cref="BossPrimalForcePower"/>（三层「原始力量」的「巨石造成未格挡伤害」那一条），
/// 那条已经在实机上验证过对她生效。
/// </summary>
public sealed class BossFlurryPower : PowerModel
{
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Single;

    public override bool ShouldPlayVfx => false;

    /// <summary>每次「疾风连击」造成未被格挡伤害时，给该玩家的力竭层数（用户口径：1）。</summary>
    private const int DrainedPerPlay = 1;

    private int _logs;

    public override async Task AfterDamageGiven(PlayerChoiceContext choiceContext, Creature dealer,
                                                DamageResult result, ValueProp props, Creature target,
                                                CardModel? cardSource)
    {
        try
        {
            var self = Owner;
            if (self == null || !self.IsAlive) return;
            if (dealer == null || !ReferenceEquals(dealer, self)) return;   // 必须是她的伤害
            if (target == null || target.Monster != null) return;           // 只认真人玩家
            if (!IsFlurryOfBlows(cardSource)) return;

            int unblocked = 0;
            try { unblocked = result.UnblockedDamage; } catch { unblocked = 0; }
            if (unblocked <= 0) return;                                    // 全被格挡 ⇒ 不叠

            if (target.Player == null) return;

            var ctx = choiceContext ?? new BlockingPlayerChoiceContext();
            await PowerCmd.Apply<FlurryDrainedPower>(ctx, target, DrainedPerPlay, self, cardSource);

            if (_logs++ < 20)
            {
                Logger.Log($"[CharBoss] 疾风连击：对 {target.Name} 造成 {unblocked} 点未被格挡伤害 " +
                           $"→ 下回合失去 {DrainedPerPlay} 点能量");
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 疾风连击（叠力竭）结算失败: " + e.Message);
        }
    }

    /// <summary>
    /// 这张牌是不是「疾风连击」。先按**类型**认（两份观者 mod 的类名不同：
    /// `WatcherMod.WatcherFlurryOfBlows` / `Watcher.Code.Cards.Common.FlurryOfBlows`），
    /// 再按模型 id 兜底（`WATCHER_FLURRY_OF_BLOWS` / `WATCHER-FLURRY_OF_BLOWS`）。
    /// </summary>
    private static bool IsFlurryOfBlows(CardModel? card)
    {
        if (card == null) return false;
        try
        {
            var type = WatcherCards.FlurryOfBlows;
            if (type != null && type.IsInstanceOfType(card)) return true;
        }
        catch { /* 类型认不出就按 id */ }

        try
        {
            string entry = card.Id.Entry;
            return string.Equals(entry, "WATCHER_FLURRY_OF_BLOWS", StringComparison.Ordinal)
                || string.Equals(entry, "WATCHER-FLURRY_OF_BLOWS", StringComparison.Ordinal);
        }
        catch { return false; }
    }
}

/// <summary>
/// 玩家侧的「**力竭**」（= 崩坠 `DrainedPower`，`AWAKENED-DRAINED_POWER`）。
///
/// 反编译崩坠：`Type => Debuff`、`StackType => Counter`，
/// `AfterEnergyReset(Player)` 里 `PlayerCmd.LoseEnergy(Amount, player)` 然后 `PowerCmd.Remove(this)`。
/// 也就是说：**只在下一个回合的能量结算时扣一次、扣完就消失**（层数可叠）。
///
/// 文案（`FLURRY_DRAINED_POWER.description`）里的能量用**观者的能量图标**
/// （`res://images/packed/sprite_fonts/watcher_energy_icon.png`），与用户要求一致。
/// 图标本身走 <c>Patches.BossSpecialPowerIconPatch</c> ⇒ 借观者 mod「少获得能量」那张图。
/// </summary>
public sealed class FlurryDrainedPower : PowerModel
{
    public override PowerType Type => PowerType.Debuff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override bool ShouldScaleInMultiplayer => false;

    protected override IEnumerable<IHoverTip> ExtraHoverTips => new[]
    {
        HoverTipFactory.ForEnergy(this),
    };

    public override async Task AfterEnergyReset(Player player)
    {
        try
        {
            var owner = Owner;
            if (player == null || owner?.Player == null) return;
            if (!ReferenceEquals(player, owner.Player)) return;

            decimal loss = Amount;
            if (loss <= 0m) return;

            await PlayerCmd.LoseEnergy(loss, player);
            await PowerCmd.Remove(this);

            Logger.Log($"[CharBoss] 力竭：{owner.Name} 本回合少 {loss} 点能量（力竭已移除）");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 力竭结算失败: " + e.Message);
        }
    }
}
#endif
