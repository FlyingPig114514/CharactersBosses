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
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using CharacterBosses.Core;

namespace CharacterBosses.Powers;

/// <summary>
/// 二层 2 号观者 Boss「**保留观**」的特殊效果「**确立基础**」（用户 2026-11-17）：
///
/// > 确立基础：在你的回合结束时，若你有耗能小于2的牌被保留，其在本场战斗中的耗能增加1；
/// > 每有一张卡牌的耗能被增加，观者就失去2点力量。
///
/// 恐惧版（用户 2026-11-17）：
/// > 确立基础：在你的回合结束时，你保留的牌在本场战斗中的耗能增加1。
/// ⇒ 去掉「耗能小于 2」这条限制、**不再扣她力量**。
///
/// ## 怎么知道「哪些牌被保留了」
///
/// ★ v0.2.735（用户 2026-11-17 实测报告：「保留手牌 buff 保留的卡牌不会触发她的特殊效果进行加费 /
///   给她自己减力」）：以前只认 `CardKeyword.Retain`，而**被 buff（`RetainHandPower`）留住的牌**
///   身上并没有这个关键字 —— 它走的是另一条路：`CardModel.GiveSingleTurnRetain()` /
///   `CardCmd.ApplySingleTurnRetain(card)` 给卡面打一个「本回合保留」的标记
///   （`HasSingleTurnRetain` / `ShouldRetainThisTurn`，反编译 `CardModel` 的成员）。
///   所以现在两条路一起认：
///
/// · **主路** —— `AbstractModel.AfterFlush(choiceContext, player, flushedCards, retainedCards)`：
///   反编译 `Hook.AfterFlush(...)` 的第 5 个参数就是 `IReadOnlyCollection&lt;CardModel&gt; retainedCards`
///   （由 `CombatManager.FlushPlayerHand` 现算的 `cardsToRetain` 传出来）⇒ **这是权威名单**，
///   关键字保留、buff 保留、单回合保留全都在里面。
/// · **兜底** —— `BeforeSideTurnEnd(Player 侧)`：那时手牌还没被弃，把「关键字保留 / 本回合保留」
///   的牌先算一遍（万一某个版本不给我们派发 `AfterFlush`，效果仍然在）。
///
/// 两条都会跑 ⇒ 用 `_settled`（按「玩家 + 该玩家的 `PlayerCombatState.TurnNumber`」记账）
/// **按卡实例去重**，同一张牌同一个回合结束只加一次费、只扣一次力量。顺序无关：
/// `AfterFlush` 先跑就把名单记下、`BeforeSideTurnEnd` 只会补没记过的，反之亦然。
///
/// ⚠ 只对「被这只 Boss 锁定的那位玩家」生效（`BossLockOn.SpecialEffectAppliesTo`）——
///   多人时其余玩家不受影响（用户明确：单体特殊效果）。
/// </summary>
public sealed class BossEstablishmentPower : PowerModel
{
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Single;

    public override bool ShouldPlayVfx => false;

    private static int _logs;

    /// <summary>「这位玩家这一回合结束已经结算过哪些牌」—— 按卡实例去重（见类注释）。</summary>
    private static readonly Dictionary<Player, (int Turn, HashSet<CardModel> Cards)> Settled = new();

    private static (int Turn, HashSet<CardModel> Cards) SettledFor(Player player, int turn)
    {
        if (Settled.TryGetValue(player, out var entry) && entry.Turn == turn) return entry;
        if (Settled.Count > 16) Settled.Clear();                    // 不该发生；防御性
        var fresh = (Turn: turn, Cards: new HashSet<CardModel>());
        Settled[player] = fresh;
        return fresh;
    }

    private static int TurnOf(Player player)
    {
        try { return player.PlayerCombatState?.TurnNumber ?? 0; } catch { return 0; }
    }

    /// <summary>
    /// **主路**：手牌真正被「冲洗」完之后，`retainedCards` 就是这一回合结束被保留下来的全部卡牌。
    /// </summary>
    public override async Task AfterFlush(PlayerChoiceContext choiceContext, Player player,
                                          IReadOnlyCollection<CardModel> flushedCards,
                                          IReadOnlyCollection<CardModel> retainedCards)
    {
        try
        {
            if (player == null) return;
            if (!IsMine(player)) return;

            int count = 0;
            try { count = retainedCards?.Count ?? 0; } catch { /* 忽略 */ }
            if (_logs++ < 24)
                Logger.Log($"[CharBoss] 确立基础：收到 AfterFlush —— {player.Character?.GetType().Name} 这一回合保留 {count} 张" +
                           $"（整理弃掉 {(flushedCards?.Count ?? 0)} 张）");

            await SettleAsync(choiceContext, player, retainedCards, "AfterFlush");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 确立基础：AfterFlush 结算失败（不影响战斗）: " + e.Message);
        }
    }

    /// <summary>
    /// **兜底路**：玩家回合结束（手牌还没弃）时，把手牌里「关键字保留 / 本回合保留」的牌先算一遍。
    /// 与 <see cref="AfterFlush"/> 靠 <see cref="Settled"/> 去重，谁先跑都不会重复结算。
    /// </summary>
    public override async Task BeforeSideTurnEnd(PlayerChoiceContext choiceContext, CombatSide side,
                                                 IEnumerable<Creature> participants)
    {
        if (side != CombatSide.Player) return;
        try
        {
            foreach (var creature in participants ?? (IEnumerable<Creature>)Array.Empty<Creature>())
            {
                try
                {
                    if (creature == null || !creature.IsAlive) continue;
                    var player = creature.Player;
                    if (player == null) continue;                          // 只处理真人玩家
                    if (!IsMine(player)) continue;                         // 单体：只对被锁定者

                    var hand = player.PlayerCombatState?.Hand?.Cards;
                    if (hand == null) continue;

                    var willRetain = new List<CardModel>();
                    foreach (var card in hand)
                    {
                        if (card == null) continue;
                        if (!WillBeRetained(card)) continue;
                        willRetain.Add(card);
                    }
                    await SettleAsync(choiceContext, player, willRetain, "BeforeSideTurnEnd");
                }
                catch (Exception e)
                {
                    Logger.Log("[CharBoss] 确立基础：处理某位玩家失败: " + e.Message);
                }
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 确立基础结算失败（不影响战斗）: " + e.Message);
        }
    }

    /// <summary>
    /// 这张牌这一回合结束时会被保留吗。
    ///
    /// 反编译 <c>CardModel.ShouldRetainThisTurn</c> 的实现正好就是
    /// <c>Keywords.Contains(CardKeyword.Retain) || HasSingleTurnRetain</c>
    /// ⇒ 一条就同时覆盖「关键字保留」和「被 buff 临时保留」（`GiveSingleTurnRetain`）。
    /// （`HasSingleTurnRetain` 是私有属性，用不了，也没必要用。）
    /// </summary>
    private static bool WillBeRetained(CardModel card)
    {
        try { return card.ShouldRetainThisTurn; } catch { /* 忽略 */ }
        try { return card.Keywords != null && card.Keywords.Contains(CardKeyword.Retain); }
        catch { return false; }
    }

    private bool IsMine(Player? player)
    {
        try
        {
            var boss = Owner?.Monster as Monsters.CharBossMonster;
            if (boss == null || player == null) return false;
            return BossLockOn.SpecialEffectAppliesTo(boss, player);
        }
        catch { return false; }
    }

    /// <summary>
    /// 真正的结算：把这一回合「被保留」的牌按规则加费，再按张数扣她的力量。
    /// 同一张牌同一回合结束只算一次（见 <see cref="Settled"/>）。
    /// </summary>
    private async Task SettleAsync(PlayerChoiceContext choiceContext, Player player,
                                   IEnumerable<CardModel>? retained, string source)
    {
        try
        {
            var boss = Owner?.Monster as Monsters.CharBossMonster;
            bool dread = Core.DreadMode.IsActive;
            int turn = TurnOf(player);
            var settled = SettledFor(player, turn).Cards;

            int bumped = 0;
            foreach (var card in retained ?? (IEnumerable<CardModel>)Array.Empty<CardModel>())
            {
                try
                {
                    if (card == null) continue;
                    if (settled.Contains(card)) continue;              // 这一回合结束已经算过
                    if (!dread)
                    {
                        // 普通版：只对「耗能 < 2」的保留牌加费
                        int cost = 99;
                        try { cost = card.EnergyCost.GetWithModifiers(CostModifiers.All); } catch { cost = 99; }
                        if (cost >= 2) continue;
                    }

                    card.EnergyCost.AddThisCombat(1, false);
                    settled.Add(card);
                    bumped++;
                    if (_logs++ < 24)
                        Logger.Log($"[CharBoss] 确立基础（{(dread ? "恐惧版" : "普通版")}·{source}）：{player.Character?.GetType().Name} 被保留的 " +
                                   $"{SafeEntry(card)} 耗能 +1（本场战斗）" +
                                   (dread ? "（恐惧版不扣她的力量）" : string.Empty));
                }
                catch (Exception e) { Logger.Log("[CharBoss] 确立基础：加费失败: " + e.Message); }
            }

            // 普通版：每有一张牌的耗能被增加 ⇒ 她失去 **2** 点力量（恐惧版不扣）
            // ★ v0.2.734（用户 2026-11-17 改文案）：「每有一张卡牌的耗能被增加，观者就失去2点力量。」
            if (!dread && bumped > 0 && boss?.Creature != null && boss.Creature.IsAlive)
            {
                try
                {
                    int loss = bumped * 2;
                    await PowerCmd.Apply<StrengthPower>(choiceContext, boss.Creature, -loss, boss.Creature, null, false);
                    Logger.Log($"[CharBoss] 确立基础：这次给 {bumped} 张保留牌加费 ⇒ 观者失去 {loss} 点力量");
                }
                catch (Exception e) { Logger.Log("[CharBoss] 确立基础：扣力量失败: " + e.Message); }
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 确立基础：结算异常（不影响战斗）: " + e.Message);
        }
    }

    private static string SafeEntry(CardModel? card)
    {
        try { return card?.Id.Entry ?? "?"; } catch { return "?"; }
    }
}
#endif
