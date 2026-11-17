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
using MegaCrit.Sts2.Core.ValueProps;

namespace CharacterBosses.Powers;

/// <summary>
/// 「斋戒」直接给的那层负面：**每回合开始时你的能量减少 N 点**。
///
/// 对应观者 mod 的 `WatcherMod.EnergyDownPower`（反编译：`Type => Debuff`，
/// `AfterEnergyReset(player)` 里对持有者本人 `PlayerCmd.LoseEnergy(Amount, player)`）。
/// </summary>
public sealed class TarotEnergyDownPower : PowerModel
{
    public override PowerType Type => PowerType.Debuff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override bool ShouldScaleInMultiplayer => false;

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
            Logger.Log($"[CharBoss] 塔罗·能量降低：{owner.Name} 本回合少 {loss} 点能量");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 塔罗·能量降低结算失败: " + e.Message);
        }
    }
}

/// <summary>
/// 「天人形态」给的那层 buff：**每回合开始时获得递增的能量**。
///
/// 对应观者 mod 的 `WatcherMod.DevaPower`（反编译：内部 `Instances` 记「每次给多少」，
/// 每回合 `GainEnergy(sum(Instances))` 之后把每一项 +1 ⇒ 1、2、3、4… 递增）。
/// 这里用单个计数器实现同样的序列。
/// </summary>
public sealed class TarotDevaPower : PowerModel
{
    /// <summary>下一次回合开始要给多少能量（第一次用卡面数值）。</summary>
    private int _next;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override bool ShouldScaleInMultiplayer => false;

    public override async Task AfterEnergyReset(Player player)
    {
        try
        {
            var owner = Owner;
            if (player == null || owner?.Player == null) return;
            if (!ReferenceEquals(player, owner.Player)) return;

            if (_next <= 0) _next = Math.Max(1, (int)Amount);

            int gain = _next;
            await PlayerCmd.GainEnergy(gain, player);
            _next = gain + 1;
            try { SetAmount(_next, true); } catch { /* 只是图标数字，失败无所谓 */ }
            Logger.Log($"[CharBoss] 塔罗·天人化：{owner.Name} 获得 {gain} 点能量（下一回合 {_next}）");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 塔罗·天人化结算失败: " + e.Message);
        }
    }
}

/// <summary>
/// 「斗殴」给的那层 buff：**在你的回合开始时，给所有敌人上 N 层瘀伤**（瘀伤 = <see cref="TarotBruisePower"/>）。
///
/// 对应崩坠 Hermit 的 `HermitCode.Powers.BrawlPower`（反编译：
/// `AfterPlayerTurnStart` → `PowerCmd.Apply&lt;BruisePower&gt;(ctx, CombatState.HittableEnemies, Amount, Owner, null, false)`）。
/// </summary>
public sealed class TarotBrawlPower : PowerModel
{
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override bool ShouldScaleInMultiplayer => false;

    public override async Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext, Player player)
    {
        try
        {
            var owner = Owner;
            if (player == null || owner?.Player == null) return;
            if (!ReferenceEquals(player, owner.Player)) return;

            var combat = CombatState;
            int amount = (int)Amount;
            if (combat == null || amount <= 0) return;

            await PowerCmd.Apply<TarotBruisePower>(
                choiceContext, combat.HittableEnemies, amount, owner, null, false);
            Logger.Log($"[CharBoss] 塔罗·斗殴：给所有敌人 {amount} 层瘀伤");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 塔罗·斗殴结算失败: " + e.Message);
        }
    }
}

/// <summary>
/// **瘀伤**（崩坠 Hermit 的 `HermitCode.Powers.BruisePower`）：
/// **受到攻击时额外受到 N 点伤害**，并在**下一次回合结束时消失**。
///
/// 反编译要点：
///   · `ModifyDamageAdditiveCompability`：`target == Owner &amp;&amp; dealer != null
///     &amp;&amp; props.IsPoweredAttack()` 时返回 `+Amount`；
///   · `AfterSideTurnEnd`：没有「阻止移除」的 buff 时直接 `PowerCmd.Remove(this)`
///     ⇒ 任意一方的回合结束都会把它清掉（原版就是「本回合有效」的语义）。
/// </summary>
public sealed class TarotBruisePower : PowerModel
{
    public override PowerType Type => PowerType.Debuff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override bool ShouldScaleInMultiplayer => false;

    /// <summary>受到「有来源的攻击」时，多挨 Amount 点。</summary>
    // ⚠ 0.113 给 `ModifyDamageAdditive` 加了第 6 个参数（`CardPlay`），107 只有 5 个 ⇒ 分开写。
    // ★ v0.2.732：0.111 与 0.113 的 ModifyDamageAdditive 都是 **6 参数**（第 6 个是 CardPlay），
    //   只有已停编的 0.107 是 5 参数 ⇒ 那段 #if 去掉了，只留这一份。
    public override decimal ModifyDamageAdditive(Creature? target, decimal amount, ValueProp props,
                                                 Creature? dealer, CardModel? cardSource, CardPlay? cardPlay)
    {
        try
        {
            if (target == null || !ReferenceEquals(target, Owner)) return 0m;
            if (dealer == null) return 0m;
            if (!props.IsPoweredAttack()) return 0m;
            return Amount;
        }
        catch { return 0m; }
    }

    public override async Task AfterSideTurnEnd(PlayerChoiceContext choiceContext, CombatSide side,
                                                IEnumerable<Creature> participants)
    {
        try
        {
            Logger.Log("[CharBoss] 塔罗·瘀伤：回合结束，移除");
            await PowerCmd.Remove(this);
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 塔罗·瘀伤移除失败: " + e.Message);
        }
    }
}

/// <summary>
/// 「永恒形态」给的那层 buff：**每回合手抽的前 4 张牌各减 N 费（本回合内 / 打出前有效）**。
///
/// 对应崩坠 Hermit 的 `HermitCode.Powers.EternalPower`（反编译：
/// `AfterCardDrawn` 只认 `fromHandDraw`、排除虚无牌、每回合最多 4 张，
/// 对牌调用 `card.EnergyCost.AddThisTurnOrUntilPlayed(-Amount, true)`）。
/// </summary>
public sealed class TarotEternalPower : PowerModel
{
    /// <summary>本回合已经减过几张（每回合重置，最多 4 张）。</summary>
    private int _discountedThisTurn;

    private const int MaxPerTurn = 4;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override bool ShouldScaleInMultiplayer => false;

    public override Task BeforeSideTurnStart(PlayerChoiceContext choiceContext, CombatSide side,
                                            IReadOnlyList<Creature> participants, ICombatState combatState)
    {
        try
        {
            if (participants == null) return Task.CompletedTask;
            foreach (var c in participants)
            {
                if (ReferenceEquals(c, Owner)) { _discountedThisTurn = 0; break; }
            }
        }
        catch { /* 忽略 */ }
        return Task.CompletedTask;
    }

    public override Task AfterCardDrawn(PlayerChoiceContext choiceContext, CardModel card, bool fromHandDraw)
    {
        // ★ 用户 2026-11-14：「第 11 和第 12 回合印出来的牌全变成零费了…第 12 回合的那些牌都是原本的费用」
        //   他的牌是剧本**印**出来的（不是抽的），原版口径里「永恒形态对他没有实际效果」
        //   （docs/plans/boss-fool-tarot.md §3.7）⇒ 对我们自己的 Boss 直接不生效，卡面费用保持原样。
        if (Owner?.Monster is CharacterBosses.Monsters.CharBossMonster) return Task.CompletedTask;

        try
        {
            if (!fromHandDraw || card?.Owner?.Creature == null) return Task.CompletedTask;
            if (!ReferenceEquals(card.Owner.Creature, Owner)) return Task.CompletedTask;
            if (_discountedThisTurn >= MaxPerTurn) return Task.CompletedTask;

            int amount = (int)Amount;
            if (amount <= 0) return Task.CompletedTask;

            _discountedThisTurn++;
            card.EnergyCost.AddThisTurnOrUntilPlayed(-amount, true);
            string entry;
            try { entry = card.Id.Entry; } catch { entry = "?"; }
            Logger.Log($"[CharBoss] 塔罗·永恒形态：{entry} 本回合耗能 -{amount}" +
                       $"（本回合第 {_discountedThisTurn}/{MaxPerTurn} 张）");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 塔罗·永恒形态结算失败: " + e.Message);
        }
        return Task.CompletedTask;
    }
}
