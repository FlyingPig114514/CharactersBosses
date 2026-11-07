using System;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;

namespace CharacterBosses.Powers;

/// <summary>
/// 「**麻痹**」—— 二层「王储」（储君 · 王权轴）特殊效果「征服者」施加给玩家的减益
/// （用户 2026-11-02 设计）。
///
/// 用户原话：
/// > 增加 debuff：麻痹。效果：你下一张攻击牌的伤害减少 50%。
/// > 叠加时减少伤害减少幅度，最低 0%。2 层及以上的效果就是：你下一张攻击牌的伤害减少 100%。
/// > 玩家打出下一张攻击牌时该 debuff 失效。
///
/// 实现：
///   · **减伤幅度** = `50% × 层数`，封顶 100%（⇒ 2 层及以上 = 这一击完全无效，伤害 0）。
///   · 只对**这个玩家自己打出的攻击牌**生效（`ModifyDamageMultiplicative`；反编译签名
///     `decimal ModifyDamageMultiplicative(Creature target, decimal amount, ValueProp props,
///      Creature dealer, CardModel cardSource, CardPlay cardPlay)` —— 返回的是**乘数**，1 = 不变）。
///   · **失效时机**：`AfterCardPlayed` 里发现他打出了一张攻击牌 ⇒ `PowerCmd.Remove(this)`
///     （伤害结算发生在牌打出过程中，所以那一张吃得到减伤，之后才移除）。
///   · 层数用 `StackType.Counter` 叠：每层 +50%，所以 2 层就满 100%。
///   · 描述简洁（用户 2026-11-02 给了张游戏内截图作参照）：
///     「你本回合打出的下一张攻击牌造成的伤害减少{Reduction}%。」——百分比随层数变。
///
/// 图标：`res://CharacterBosses/images/powers/paralysis.png`（**黄色闪电**、透明背景、居中；
/// 见 `Patches/BossSpecialPowerIconPatch`）。
/// </summary>
public sealed class ParalysisPower : PowerModel
{
    public override PowerType Type => PowerType.Debuff;

    public override PowerStackType StackType => PowerStackType.Counter;

    /// <summary>每层减少的伤害比例。</summary>
    private const decimal PerStack = 0.5m;

    /// <summary>层数不随人数放大：特殊效果对每个玩家各施加 1 层。</summary>
    public override bool ShouldScaleInMultiplayer => false;

    // ⚠ 这里**不要**覆写 `ShouldPlayVfx => false`：反编译 `NCreature` 的「获得 power」那段
    //   只有 `if (power.ShouldPlayVfx) SfxCmd.Play(flag ? "buff" : "debuff")` —— 覆写成 false
    //   就没有获得减益的那个音效了（用户 2026-11-02：「和获得其它 debuff 一样有音效」）。
    //   基类默认是 `IsVisible`（= 可见），正好就是我们要的。

    /// <summary>
    /// 描述里的 <c>{Reduction}</c> = 实际的减伤百分比（50 × 层数，封顶 100）。
    /// 写法同 `IroncladRagePower` 的 `{Loss}`：拿基类描述再 `Add` 一个占位符，
    /// **不要**用 `{Amount}`（那是层数，不是百分比）。
    /// </summary>
    public override MegaCrit.Sts2.Core.Localization.LocString Description
    {
        get
        {
            var description = base.Description;
            try { description.Add("Reduction", ReductionPercent().ToString()); }
            catch { /* 填不进去就用原文 */ }
            return description;
        }
    }

    /// <summary>减伤百分比：50 × 层数，封顶 100（2 层及以上就是 100%）。</summary>
    internal int ReductionPercent()
    {
        try { return (int)Math.Round(Math.Min(1m, PerStack * Math.Max(0, Amount)) * 100m); }
        catch { return 0; }
    }

    private int _logs;

    /// <summary>「下一张攻击牌」减伤：只有**他自己打出的攻击牌**吃这一刀。
    ///
    /// ⚠ 这里是**普通方法**而不是 `override`（用户 2026-11-05 双版本改造）：
    ///   `ModifyDamageMultiplicative` 的签名在 v0.107 与 v0.111 之间变了（新版末尾多两个参数：
    ///   `CardPlay cardPlay`）⇒ 一个 `override` 不可能同时对上两版。所以改由
    ///   <c>Patches/ParalysisDamagePatch</c> 挂在 `AbstractModel.ModifyDamageMultiplicative` 上，
    ///   用 `__args` 按位置取值（两版位置完全一致）后转调这里。返回**倍率**（1 = 不变）。
    /// </summary>
    internal decimal MultiplierFor(Creature? dealer, CardModel? cardSource)
    {
        try
        {
            var self = Owner;
            if (self == null) return 1m;
            if (dealer == null || !ReferenceEquals(dealer, self)) return 1m;
            if (!IsOwnerAttack(cardSource)) return 1m;

            int stacks = Math.Max(0, Amount);
            if (stacks <= 0) return 1m;

            decimal reduce = Math.Min(1m, PerStack * stacks);
            if (_logs++ < 30)
            {
                Logger.Log($"[CharBoss] 麻痹：{SafeName(self)} 的 {SafeEntry(cardSource)} 吃 {stacks} 层 ⇒ " +
                           $"伤害 ×{1m - reduce}（−{reduce * 100:0}%）");
            }
            return Math.Max(0m, 1m - reduce);
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 麻痹减伤计算失败: " + e.Message);
            return 1m;
        }
    }

    /// <summary>他打出**下一张攻击牌**后失效。</summary>
    public override async Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        try
        {
            var self = Owner;
            if (self == null) return;
            var card = cardPlay?.Card;
            if (card == null) return;
            if (!ReferenceEquals(card.Owner?.Creature, self)) return;
            if (!IsOwnerAttack(card)) return;

            Logger.Log($"[CharBoss] 麻痹：{SafeName(self)} 打出了攻击牌 {SafeEntry(card)} ⇒ 麻痹失效（剩余{Amount}层）");
            await PowerCmd.Remove(this);
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 麻痹失效处理失败: " + e.Message);
        }
    }

    /// <summary>这张牌是不是「这个玩家自己打出的攻击牌」。</summary>
    private bool IsOwnerAttack(CardModel? card)
    {
        if (card == null) return false;
        try { if (card.Type != CardType.Attack) return false; } catch { return false; }
        try { return ReferenceEquals(card.Owner?.Creature, Owner); }
        catch { return false; }
    }

    private static string SafeName(Creature? c)
    {
        try { return c?.Name ?? "?"; } catch { return "?"; }
    }

    private static string SafeEntry(CardModel? c)
    {
        try { return c?.Id.Entry ?? "?"; } catch { return "?"; }
    }
}
