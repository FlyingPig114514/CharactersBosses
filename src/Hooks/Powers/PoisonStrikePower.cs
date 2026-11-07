using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;

namespace CharacterBosses.Powers;

/// <summary>
/// 静默猎手（二层：涂毒猎）的特殊效果「**带毒刺击**」。
///
/// 用户设计（原话）：
///   · 中毒改为在**你的回合结束时**结算，且伤害**能被格挡抵消**；
///   · **每当对敌方造成未被格挡的伤害时，就将自身的一点中毒转移给目标。**
///     （2026-10-22 用户订正：**不限制转移的发起者和目标** —— 敌方身上有中毒时，
///      它对玩家造成未格挡伤害也能把毒转给玩家；可以与「涂毒」叠加；玩家之间同样适用。）
/// 备注（用户）：双方的中毒都在你回合结束时结算；对她来说结算时间基本没变
/// （她原本在「她的回合开始」结算，而她的回合紧接着你的回合结束），但现在都能被格挡挡了。
/// 触媒（<c>AccelerantPower</c>：每次触发额外触发一次）与「立刻触发中毒」的牌对双方都生效，
/// 因为那两张牌本身没有主语 —— 这部分由原版实现，我们不用管。
///
/// 实现分两半：
///   1. **结算**：原版 <c>PoisonPower.AfterSideTurnStart</c> 在**某一方回合开始**时触发，
///      这里由 <see cref="Patches.PoisonTimingPatch"/> 在「本场战斗有这层特殊效果」时把它拦掉，
///      改由本类的 <see cref="BeforeSideTurnEnd"/> 在**玩家回合结束**统一结算双方，
///      并用 <c>ValueProp.Unpowered</c>（不带 <c>Unblockable</c>）⇒ 伤害可以被格挡抵消。
///      每条中毒的触发次数与递减规则照抄原版：`TriggerCount = min(层数, 1 + 触媒层数)`，
///      每次造成「当前层数」点伤害、然后层数 −1。
///   2. **转移**：<see cref="AfterDamageGiven"/> 里判断「任何生物对敌方造成未被格挡的伤害」，
///      满足就把**发起者自己**的中毒 −1、被打的那个目标中毒 +1（双向、不限发起者）。
/// </summary>
public sealed class PoisonStrikePower : PowerModel
{
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Single;

    /// <summary>
    /// 本场战斗里有没有「带毒刺击」这层特殊效果（补丁用它来决定要不要接管中毒结算）。
    /// </summary>
    public static bool Applies(ICombatState? combat)
    {
        if (combat == null) return false;
        try
        {
            foreach (var creature in combat.Creatures)
            {
                if (creature == null) continue;
                if (creature.GetPower<PoisonStrikePower>() != null) return true;
            }
        }
        catch { /* 忽略 */ }
        return false;
    }

    /// <summary>
    /// **可被格挡**的中毒结算 —— 与 <see cref="PoisonPower.Trigger"/> 一一对应，只把伤害属性从
    /// `Unblockable|Unpowered` 换成 `Unpowered`：
    /// ```csharp
    /// iterations = TriggerCount;                       // min(层数, 1 + 触媒层数)
    /// for (i in 0..iterations) {
    ///     await CreatureCmd.Damage(ctx, Owner, 当前层数, Unblockable|Unpowered, null, null);
    ///     if (Owner.IsAlive) await PowerCmd.Decrement(this);
    ///     else await Cmd.CustomScaledWait(0.1f, 0.25f);
    /// }
    /// ```
    /// 供两个入口共用：本类「玩家回合结束」的周期性结算，以及
    /// <see cref="Patches.PoisonTriggerBlockablePatch"/> 接管的「立刻触发」。
    /// </summary>
    public static async Task TriggerBlockableAsync(PoisonPower poison)
    {
        if (poison?.Owner == null) return;

        var ctx = new ThrowingPlayerChoiceContext();   // 与原版 Trigger() 用的是同一个上下文
        int iterations = TriggerCountOf(poison);
        for (int i = 0; i < iterations; i++)
        {
            int hit = poison.Amount;
            if (hit <= 0) break;

            // 炼金术士模组的「解毒剂」是靠**伤害属性形状**认中毒 tick 的（必须 Unblockable|Unpowered），
            // 而我们为了「可格挡」只传了 Unpowered ⇒ 它认不出来、一点不吸收（用户 2026-10-25 实测：
            // 毒没超过解毒剂也照样扣血/吃格挡）。这里先按原版形状问它一次，剩下的再用我们的属性结算。
            // 详见 Core/AlchemistAntitoxinCompat.cs。
            decimal absorbed = Core.AlchemistAntitoxinCompat.AbsorbedBy(poison.Owner, hit, hit);
            decimal damage = hit - absorbed;
            if (damage < 0m) damage = 0m;

            if (damage > 0m)
            {
                await CreatureCmd.Damage(ctx, poison.Owner, damage, ValueProp.Unpowered, null, null);
            }
            if (poison.Owner.IsAlive)
            {
                await PowerCmd.Decrement(poison);
            }
            else
            {
                await Cmd.CustomScaledWait(0.1f, 0.25f);
                break;
            }
        }
    }

    /// <summary>
    /// 原版的 <c>TriggerCount</c>（私有）：<c>min(层数, 1 + Σ 该生物「对手」里存活者的触媒层数)</c>。
    ///
    /// ⚠ 2026-10-29 修正（用户：「带毒刺击不再会使『触媒』和『毒性爆发』也对玩家方产生效果」）：
    /// 反编译原版 `PoisonPower.TriggerCount` 是
    /// <code>
    /// var opp = CombatState.GetOpponentsOf(Owner).Where(c =&gt; c.IsAlive);
    /// return Math.Min(Amount, 1 + opp.Sum(a =&gt; a.GetPowerAmount&lt;AccelerantPower&gt;()));
    /// </code>
    /// —— 触媒（`AccelerantPower`）**属于「下毒的那一方」**：它让**对手身上**的中毒多触发几次。
    /// 以前这里写成「毒主人自己的触媒」，于是：
    ///   · 涂毒猎的触媒反而给她自己的毒加速（对玩家毫无影响）；
    ///   · 而**玩家**身上的触媒会把玩家自己的中毒多触发几次（＝打自己），
    ///     毒性爆发的「立即触发」也走同一条路 ⇒ 一起变成「对玩家方生效」。
    /// 现在改回原版口径：只看**对手**的触媒。
    /// </summary>
    private static int TriggerCountOf(PoisonPower poison)
    {
        int extra = 0;
        try
        {
            var owner = poison.Owner;
            var combat = owner?.CombatState;
            if (owner == null || combat == null) return Math.Min(poison.Amount, 1);

            foreach (var other in combat.GetOpponentsOf(owner))
            {
                if (other == null || !other.IsAlive) continue;
                try { extra += other.GetPowerAmount<AccelerantPower>(); } catch { /* 忽略 */ }
            }
        }
        catch { extra = 0; }

        return Math.Min(poison.Amount, 1 + extra);
    }

    /// <summary>玩家回合结束 → 双方中毒统一结算（可被格挡）。</summary>
    public override async Task BeforeSideTurnEnd(PlayerChoiceContext choiceContext, CombatSide side,
                                                 IEnumerable<Creature> participants)
    {
        if (side != CombatSide.Player) return;

        var combat = Owner?.CombatState;
        if (combat == null) return;

        var targets = new List<Creature>();
        try
        {
            foreach (var c in participants)          // 玩家侧（含玩家自己的随从）
                if (c != null && c.IsAlive) targets.Add(c);

            foreach (var c in combat.Creatures)      // 静默猎手侧
                if (c != null && c.IsAlive && Owner != null && c.Side == Owner.Side && !targets.Contains(c))
                    targets.Add(c);
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 带毒刺击：收集中毒目标失败: " + e.Message);
        }

        foreach (var creature in targets)
        {
            try { await ResolvePoison(choiceContext, creature); }
            catch (Exception e) { Logger.Log("[CharBoss] 带毒刺击：中毒结算失败: " + e.Message); }
        }
    }

    /// <summary>
    /// 结算一个生物身上的中毒：直接走 <see cref="TriggerBlockableAsync"/>（可被格挡）。
    /// </summary>
    private static async Task ResolvePoison(PlayerChoiceContext ctx, Creature creature)
    {
        var poison = creature.GetPower<PoisonPower>();
        if (poison == null) return;

        int before = poison.Amount;
        if (before <= 0) return;

        await TriggerBlockableAsync(poison);

        Logger.Log($"[CharBoss] 带毒刺击：{creature.Name} 的中毒结算完成" +
                   $"（{before} → {poison.Amount} 层，伤害可被格挡）");
    }

    /// <summary>
    /// **中毒转移（双向、不限发起者）**：任何生物对**敌方**造成未被格挡的伤害时，
    /// 把**它自己**身上的 1 点中毒转给被打的那个目标。
    ///
    /// 用户 2026-10-22 的口径：
    ///   · **不限制转移的发起者和目标** —— 敌方身上有中毒时，它对玩家造成未格挡伤害也能把毒转给玩家；
    ///   · 可以与「涂毒」（<c>Envenom</c>：攻击造成未格挡伤害时 +1 中毒）叠加；
    ///   · 玩家之间（多人）同样适用。
    ///
    /// 注意：我们自己结算的中毒伤害 dealer 是 null，不会在这里递归。
    /// </summary>
    public override async Task AfterDamageGiven(PlayerChoiceContext choiceContext, Creature dealer,
                                                DamageResult result, ValueProp props, Creature target,
                                                CardModel cardSource)
    {
        try
        {
            if (Owner == null || dealer == null || result == null || target == null) return;
            if (ReferenceEquals(dealer, target)) return;                  // 自己打自己不算
            if (ReferenceEquals(dealer, Owner) && ReferenceEquals(target, Owner)) return;

            // 用户 2026-10-23 实测：「荆棘的反伤也会把毒转移给 Boss」——反伤不是「打出牌造成的伤害」。
            // 现在只对**有牌来源**（`cardSource != null`，即真的打出攻击牌）的伤害做转移；
            // 反伤、中毒结算这类没有牌来源的伤害一律不转移。
            if (cardSource == null) return;
            if (dealer.Side == target.Side) return;                       // 同阵营不算「对敌方」
            if (result.UnblockedDamage <= 0) return;                      // 被格挡完就没得转
            if (!target.IsAlive) return;                                  // 目标已经死了，没意义

            var dealerPoison = dealer.GetPower<PoisonPower>();
            if (dealerPoison == null || dealerPoison.Amount < 1) return;   // 发起者身上没毒就没什么可转

            // ⚠ applier 必须传 null：传 Owner（静默猎手）会被原版当成"她给出的毒"，
            // 于是她的「异蛇头骨」（`ModifyPowerAmountGivenAdditive`：giver == Owner.Creature 时 +1）
            // 会给这一次转移也 +1 → 说好转移 1 点，实际多涨 1 点（用户实测：异蛇头骨对我也起效）。
            // 转移是"搬运"，既不是她给的毒、也不是玩家给的毒，所以两个方向都不该被遗物加成。
            await PowerCmd.ModifyAmount(choiceContext, dealerPoison, -1m, null, null, false);
            await PowerCmd.Apply<PoisonPower>(choiceContext, target, 1m, null, null, false);
            Logger.Log($"[CharBoss] 带毒刺击：{dealer.Name} 对 {target.Name} 造成未格挡伤害 " +
                       $"{result.UnblockedDamage} → 转移 1 点中毒" +
                       $"（{dealer.Name} 剩 {dealer.GetPowerAmount<PoisonPower>()} 层，" +
                       $"{target.Name} {target.GetPowerAmount<PoisonPower>()} 层）");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 带毒刺击：转移中毒失败: " + e.Message);
        }
    }
}
