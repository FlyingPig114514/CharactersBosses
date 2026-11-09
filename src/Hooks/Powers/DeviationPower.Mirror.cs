using System;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;

namespace CharacterBosses.Powers;

/// <summary>
/// 「力量与集中数值始终相同」这件事，**改成即时镜像**，不再靠轮询对账。
///
/// 轮询版的 bug（用户实测）：`BiasedCognitionPower.AfterSideTurnStart` 会调
/// <c>PowerCmd.Apply&lt;FocusPower&gt;(ctx, owner, -Amount, owner, null)</c> 掉 1 点集中，
/// 而我们在「每次出牌后」才比对基线 —— 如果「暴涨」是本回合第一张牌，那么
/// 「集中 -1」和「力量 +2」落进同一个窗口，对账只看得到「两边都变了」，
/// 于是按「以力量为准」把集中补齐 → 那 -1 被吃掉，偏差认知当回合等于失效。
///
/// 正解：所有能力值变化都会经过 <c>PowerCmd.Apply</c>，它在改完之后广播
/// <c>AbstractModel.AfterPowerAmountChanged(choiceContext, power, amount, applier, cardSource)</c>
/// （反编译确认；注意这个虚方法比静态的 <c>Hook.AfterPowerAmountChanged</c> 少一个 combatState 参数）。
/// 在这里**改多少就立刻把同样的差值补给另一边**，时序问题从根上消失。
///
/// 轮询对账（<see cref="ReconcileAsync"/>）保留作兜底：镜像生效后两边始终相等，
/// 它自然退化成「只更新基线」的空转；万一有某条不广播钩子的路径改了数值，它还能补上。
/// </summary>
public sealed partial class DeviationPower
{
    public override async Task AfterPowerAmountChanged(PlayerChoiceContext choiceContext, PowerModel power,
                                                       decimal amount, Creature? applier, CardModel? cardSource)
    {
        // 自己镜像出去的那一次不要再被镜像回来（否则无限递归）
        if (_syncing || amount == 0m) return;

        var owner = Owner;
        if (owner == null || !owner.IsAlive) return;
        if (power == null || ReferenceEquals(power, this)) return;
        if (power.Owner != owner) return;   // 只关心 Boss 自己身上的数值

        if (power is FocusPower)
        {
            await MirrorAsync<StrengthPower>(choiceContext, owner, amount);
        }
        else if (power is StrengthPower)
        {
            await MirrorAsync<FocusPower>(choiceContext, owner, amount);
        }
    }

    /// <summary>把 <paramref name="delta"/> 施加到另一边（silent，避免动画/日志抖动）。</summary>
    private async Task MirrorAsync<TPower>(PlayerChoiceContext choiceContext, Creature owner, decimal delta)
        where TPower : PowerModel
    {
        if (delta == 0m) return;

        _syncing = true;
        try
        {
            Logger.Log($"[CharBoss] 偏差认知：镜像 {typeof(TPower).Name} {delta:+0;-0}");
            var ctx = choiceContext ?? new BlockingPlayerChoiceContext();
            await PowerCmd.Apply<TPower>(ctx, owner, delta, owner, null, false);
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 偏差认知镜像失败: " + e.Message);
        }
        finally
        {
            // 镜像之后两边应当相等，基线跟着刷新（否则下一轮轮询会把这次的差值再算一遍）
            _lastStrength = AmountOf<StrengthPower>(owner);
            _lastFocus = AmountOf<FocusPower>(owner);
            _syncing = false;
        }
    }
}
