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
/// > 每有一张卡牌的耗能被增加，观者就失去1点力量。
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

            // ★ v0.2.737（用户 2026-11-17：「金字塔和计划妥当**不丢弃**的牌费用也会上涨。
            //   **不丢弃不等于保留**」）：
            //   反编译 0.111 里这两样东西压根不是「保留」，而是**干脆不冲洗手牌**：
            //   `RunicPyramid.ShouldFlush(player) => false`、`WellLaidPlansPower.ShouldFlush(player) => false`
            //   ⇒ 手里所有牌都会作为「留下来的牌」出现在 `retainedCards` 里（并没有被标记成保留）。
            //   真正的「保留」只有两种：`CardKeyword.Retain` 关键字，和 `GiveSingleTurnRetain()`
            //   打上的单回合保留标记 —— 正好就是 `CardModel.ShouldRetainThisTurn` 的定义。
            //   ⇒ `AfterFlush` 这条主路也必须先按它过一遍，不能把整份 `retainedCards` 当成保留牌。
            var reallyRetained = new List<CardModel>();
            int keptButNotRetained = 0;
            foreach (var card in retainedCards ?? (IReadOnlyCollection<CardModel>)Array.Empty<CardModel>())
            {
                if (card == null) continue;
                if (WillBeRetained(card, player)) reallyRetained.Add(card);
                else keptButNotRetained++;
            }

            if (_logs++ < 24)
                Logger.Log($"[CharBoss] 确立基础：收到 AfterFlush —— {player.Character?.GetType().Name} 这一回合留在手牌 {count} 张，" +
                           $"其中**真正被保留** {reallyRetained.Count} 张" +
                           (keptButNotRetained > 0 ? $"（{keptButNotRetained} 张只是没被弃掉，例如金字塔/计划妥当 ⇒ 不算）" : string.Empty) +
                           $"（整理弃掉 {(flushedCards?.Count ?? 0)} 张）");

            await SettleAsync(choiceContext, player, reallyRetained, "AfterFlush");
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
                        if (!WillBeRetained(card, player)) continue;
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
    /// 这张牌算不算「被保留」。
    ///
    /// ★ 三轮实测逐步收敛出来的口径（用户 2026-11-17）：
    ///
    /// | 来源 | 引擎实现 | 算不算保留 |
    /// |---|---|---|
    /// | 自带「保留」关键字（时之沙 / 旋转打击 / 坚韧…） | `CardKeyword.Retain` | **算** |
    /// | 「保留手牌」buff（`RetainHandPower`，计数器 N 回合） | `ShouldFlush(player) =&gt; false` | **算**（用户第 3 轮：「保留手牌 buff 保留的牌又不会费用增加了」⇒ 要算） |
    /// | 金字塔（遗物 `RunicPyramid`） | `ShouldFlush(player) =&gt; false` | **不算**（用户第 2 轮：「不丢弃不等于保留」） |
    /// | 计划妥当（`WellLaidPlansPower`） | `ShouldFlush(player) =&gt; false` | **不算**（同上） |
    /// | 别的「单回合保留」效果 | `GiveSingleTurnRetain()` ⇒ `CardModel.ShouldRetainThisTurn` | **算** |
    ///
    /// 麻烦在于：金字塔 / 计划妥当 / 保留手牌**三者引擎层面长得一模一样**（都是「不冲洗手牌」），
    /// 手里那些牌身上一个标记也没有 ⇒ 只能按**持有者身上有没有「保留手牌」这个 power** 来区分
    /// （`RetainHandPower` 是 `PowerStackType.Counter`、自己回合结束 −1 层，语义就是「再保留几回合手牌」）。
    /// 类型名用字符串比，避免版本间类名/命名空间变动把编译搞崩。
    /// </summary>
    private static bool WillBeRetained(CardModel card, Player? player)
    {
        try
        {
            if (card.Keywords != null && card.Keywords.Contains(CardKeyword.Retain)) return true;
        }
        catch { /* 忽略 */ }
        try { if (card.ShouldRetainThisTurn) return true; } catch { /* 忽略 */ }
        return HasRetainHandBuff(player);
    }

    /// <summary>这位玩家身上有没有「保留手牌」（`RetainHandPower`）。</summary>
    private static bool HasRetainHandBuff(Player? player)
    {
        try
        {
            var powers = player?.Creature?.Powers;
            if (powers == null) return false;
            for (int i = 0; i < powers.Count; i++)
            {
                var p = powers[i];
                if (p == null) continue;
                if (string.Equals(p.GetType().Name, "RetainHandPower", StringComparison.Ordinal)) return true;
            }
        }
        catch { /* 忽略 */ }
        return false;
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
                    // ★ v0.2.736（用户 2026-11-17）：「有一些会保留的卡牌没有耗能（不能被打出，
                    //   例如悲伤和睡眠不佳）。保证它们留在手里不会让保留观减力量（因为它们没有耗能，
                    //   所以自然也增加不了）。X 牌判定上是 10 亿-1 费，应该没有这个问题。」
                    //   ⇒ 先摘掉「根本没法打出 / 没有耗能」的牌，再走后面的耗能判断。
                    if (IsCostless(card)) continue;
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

            // 普通版：每有一张牌的耗能被增加 ⇒ 她失去 **1** 点力量（恐惧版不扣）
            // ★ v0.2.737（用户 2026-11-17）：「把每有一张牌耗能增加观者减少的力量改回 1」。
            if (!dread && bumped > 0 && boss?.Creature != null && boss.Creature.IsAlive)
            {
                try
                {
                    int loss = bumped;
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

    /// <summary>
    /// 这张牌**压根没有耗能**吗（打不出的状态牌 / 诅咒，例如「悲伤」「睡眠不佳」，以及 X 费牌）。
    ///
    /// 用户 2026-11-17：「有一些会保留的卡牌没有耗能（不能被打出，例如悲伤和睡眠不佳）。
    /// 保证它们留在手里不会让保留观减力量（因为他们没有耗能，所以自然也增加不了）。
    /// X 牌我记得判定上是 10 亿-1 费，应该没有这个问题。」
    /// ⇒ 三条任意命中就跳过：① `Unplayable` 关键字；② `CostsX`（X 费，涨 1 点毫无意义）；
    ///   ③ 解析出来的耗能是**负数**（引擎里「无耗能」用的就是负值哨兵，这种牌 `&lt; 2` 也成立，
    ///   所以必须单独排掉，否则会白送她一个 −2 力量）。
    /// </summary>
    private static bool IsCostless(CardModel card)
    {
        try
        {
            if (card.Keywords != null && card.Keywords.Contains(CardKeyword.Unplayable)) return true;
        }
        catch { /* 忽略 */ }
        try { if (card.EnergyCost.CostsX) return true; } catch { /* 忽略 */ }
        try { if (card.EnergyCost.GetWithModifiers(CostModifiers.All) < 0) return true; } catch { /* 忽略 */ }
        return false;
    }

    private static string SafeEntry(CardModel? card)
    {
        try { return card?.Id.Entry ?? "?"; } catch { return "?"; }
    }
}
#endif
