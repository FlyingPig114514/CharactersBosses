using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;

namespace CharacterBosses.Powers;

/// <summary>
/// 铁甲战士（三层：**易伤战**）的特殊效果「**主宰**」（用户 2026-10-24）：
/// **每当铁甲战士给予易伤时，同样给予 1 层虚弱**。
///
/// 注意和同名卡牌「主宰」（<c>Dominate</c>：给 1 层易伤、按敌人身上的易伤层数换力量）不是一回事。
///
/// 实现：反编译 <c>AbstractModel.AfterPowerAmountChanged(choiceContext, power, amount, applier, cardSource)</c> ——
/// 每当某个 power 的层数变化时广播；我们只认「**易伤** + 是**她给的** + 增加」：
/// <code>
/// if (power is VulnerablePower &amp;&amp; amount &gt; 0 &amp;&amp; applier == Owner)
///     await PowerCmd.Apply&lt;WeakPower&gt;(ctx, power.Owner, 1, Owner, null);
/// </code>
/// 「她给的」用 <c>applier == Owner</c> 判定（她自己的牌/遗物/能力都符合；玩家给她上易伤不会触发）。
/// </summary>
public sealed class DominateWeakPower : PowerModel
{
    /// <summary>每次给易伤时附带的虚弱层数（用户设计：1 层）。</summary>
    public const int WeakAmount = 1;

    public override PowerType Type => PowerType.Buff;

    /// <summary>不可叠加：有这一层就够了。</summary>
    public override PowerStackType StackType => PowerStackType.Single;

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new PowerVar<WeakPower>(WeakAmount),
    };

    public override async Task AfterPowerAmountChanged(PlayerChoiceContext choiceContext, PowerModel power,
                                                       decimal amount, Creature? applier, CardModel? cardSource)
    {
        try
        {
            var owner = Owner;
            if (owner == null || !owner.IsAlive) return;

            if (power is not VulnerablePower) return;         // 只认易伤
            if (amount <= 0m) return;                         // 只在「给予」时
            if (applier == null || !ReferenceEquals(applier, owner)) return;   // 必须是**她**给的

            var target = power.Owner;
            if (target == null || !target.IsAlive) return;

            int stacks = WeakAmount;
            try
            {
                if (DynamicVars.TryGetValue("Weak", out var v) && v != null)
                    stacks = Math.Max(1, (int)Math.Round((double)v.BaseValue));
            }
            catch { /* 用默认 1 */ }

            // 用对方拿到的 choiceContext；拿不到就临时造一个（和「人工合成」同一套兜底）
            var ctx = choiceContext ?? new BlockingPlayerChoiceContext();
            await PowerCmd.Apply<WeakPower>(ctx, target, stacks, owner, null);

            Logger.Log($"[CharBoss] 主宰：给 {target.Name} 上 {amount} 层易伤 → 附带 {stacks} 层虚弱");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 主宰（易伤附带虚弱）失败: " + e.Message);
        }
    }
}
