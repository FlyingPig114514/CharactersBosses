#if STS2_111   // 观者 Boss 只在这个构建里
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
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using CharacterBosses.Core;

namespace CharacterBosses.Powers;

/// <summary>
/// 一层 2 号观者 Boss「**疾风观**」（设计 Key `WATCHER_FLURRY`）的特殊效果「**疾风连击**」
/// —— 2026-11-23 **第四版**（用户：「削弱，修改特殊效果」）：
///
/// > 「每当观者将疾风连击返回手牌时，**你**也将弃牌堆中的一张随机牌返回手牌。」
/// > PS（用户补的触发时机）：「观者疾风连击的返回手牌时机：**当观者打出警惕或化体为空的时候**」
///
/// 所以触发点 = **她打出「警惕」或「化体为空」**（这两张都是姿态切换牌，正是她卡面上
/// 「每次转换姿态时把弃牌堆的疾风连击放回手牌」的时机）。每次触发，**每位真人玩家**
/// 从自己的弃牌堆随机拿回 1 张牌到手上。
///
/// ★ 这是**对玩家有利**的效果（削弱 Boss）。之前那版「疾风连击造成未被格挡伤害 ⇒ 你下回合少 1 能量」
///   已经撤掉 —— `FlurryDrainedPower` 现在没有任何地方施加它，只保留类本身（随时能改回去）。
///
/// ⚠ 随机取牌走 <c>RunState.Rng.CombatCardSelection</c>（联机时两端同一条随机流、调用顺序一致），
///   所以客人端拿到的牌和房主完全一样。
/// </summary>
public sealed class BossFlurryPower : PowerModel
{
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Single;

    public override bool ShouldPlayVfx => false;

    private int _logs;

    /// <summary>她打出「警惕」/「化体为空」时，每位真人玩家从弃牌堆随机捞回 1 张。</summary>
    public override async Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        try
        {
            var self = Owner;
            if (self == null || !self.IsAlive) return;
            if (cardPlay?.Card == null) return;
            if (!ReferenceEquals(cardPlay.Card.Owner?.Creature, self)) return;   // 必须是她自己打的牌
            if (!IsStanceChangeCard(cardPlay.Card)) return;

            var combat = self.CombatState;
            if (combat == null) return;
            var ctx = choiceContext ?? new BlockingPlayerChoiceContext();

            string source = SafeEntry(cardPlay.Card);
            int given = 0;

            foreach (var player in BossLockOn.RealPlayers(combat))
            {
                try
                {
                    var pile = player?.PlayerCombatState?.DiscardPile;
                    if (pile == null) continue;
                    int count = pile.Cards.Count;
                    if (count <= 0) continue;

                    int index = RandomIndex(combat, count);
                    var card = pile.Cards[index];
                    if (card == null) continue;

                    await CardPileCmd.Add(card, PileType.Hand, CardPilePosition.Top, null, false);
                    given++;
                }
                catch (Exception e)
                {
                    Logger.Log("[CharBoss] 疾风连击（玩家捞牌）单次失败: " + e.Message);
                }
            }

            if (_logs++ < 20)
            {
                Logger.Log($"[CharBoss] 疾风连击：她打出 {source} ⇒ 给 {given} 位玩家各捞回 1 张弃牌");
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 疾风连击（玩家捞牌）结算失败: " + e.Message);
        }
    }

    /// <summary>
    /// 弃牌堆里的随机下标：走 **本局战斗的卡牌选择随机流**
    /// （`RunState.Rng.CombatCardSelection`）—— 联机两端同一条流、调用顺序也一致 ⇒ 拿到同一张。
    /// 取不到就退回「本场触发次数 + 种子」的确定性散列，仍然两端一致。
    /// </summary>
    private static int RandomIndex(ICombatState combat, int count)
    {
        if (count <= 1) return 0;
        try
        {
            var rng = combat.RunState?.Rng?.CombatCardSelection;
            if (rng != null) return Math.Max(0, Math.Min(count - 1, rng.NextInt(count)));
        }
        catch { /* 退回散列 */ }

        try
        {
            ulong seed = unchecked((ulong)(combat.RunState?.Rng?.Seed ?? 0UL));
            return (int)(seed % (ulong)count);
        }
        catch { return 0; }
    }

    /// <summary>
    /// 这张牌是不是「警惕」或「化体为空」（用户口径里的两个姿态切换时机）。
    /// 先按**类型**认（A 版 `WatcherVigilance` / B 版 `Vigilance`），再按模型 id 兜底
    /// （两份实现的 id 前缀不同：`WATCHER_VIGILANCE` / `WATCHER-VIGILANCE`）。
    /// </summary>
    private static bool IsStanceChangeCard(CardModel? card)
    {
        if (card == null) return false;

        try
        {
            foreach (var type in new[] { WatcherCards.Vigilance, WatcherCards.EmptyBody })
            {
                if (type != null && type.IsInstanceOfType(card)) return true;
            }
        }
        catch { /* 类型认不出就按 id */ }

        string entry = SafeEntry(card);
        if (entry.Length == 0) return false;
        string norm = entry.Replace("-", string.Empty).Replace("_", string.Empty).ToUpperInvariant();
        return norm.EndsWith("VIGILANCE", StringComparison.Ordinal)
            || norm.EndsWith("EMPTYBODY", StringComparison.Ordinal);
    }

    private static string SafeEntry(CardModel? card)
    {
        try { return card?.Id.Entry ?? string.Empty; } catch { return string.Empty; }
    }
}

/// <summary>
/// 玩家侧的「**力竭**」（= 崩坠 `DrainedPower`，`AWAKENED-DRAINED_POWER`）。
///
/// 反编译崩坠：`Type => Debuff`、`StackType => Counter`，
/// `AfterEnergyReset(Player)` 里 `PlayerCmd.LoseEnergy(Amount, player)` 然后 `PowerCmd.Remove(this)`。
/// 也就是说：**只在下一个回合的能量结算时扣一次、扣完就消失**（层数可叠）。
///
/// ⚠ 2026-11-23 起**特殊效果改成对玩家有利的版本**（「她返回疾风连击时你也捞回一张弃牌」），
///   `BossFlurryPower` 不再施加这一层 ⇒ 这个类目前**没有任何地方在用**，保留是为了随时改回去
///   （`FLURRY_DRAINED_POWER.*` 的本地化也一起留着）。
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
