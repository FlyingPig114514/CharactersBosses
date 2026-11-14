using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using CharacterBosses.Core;

namespace CharacterBosses.Monsters;

/// <summary>
/// 「抉择储」（三层储君 3 号）在**玩家选完之后**接着把这一手的后半段打完。
///
/// 为什么需要：剧本里有些牌**只有玩家在「抉择，抉择」里挑了指定那张才打得出**
/// （<see cref="ScriptCard.OnlyIfChosenCard"/>，第 1 回合的「天际钻头」——
/// 它是 X 费牌，而第 1 回合的 3 点能量被 群星之子/崇拜/王国资产 花光了，
/// 只有玩家选了「星位序列」（3 次 × +2 费 = +6）才有费打它）。
/// 规划发生在**玩家回合**，那时选择还不知道 ⇒ 这种牌规划期一律跳过；
/// 选择落地之后由这里按剧本再跑一次规划，把那些「现在可以打了」的牌接着打出去。
///
/// ★ 2026-11-13（恐惧模式「指导」，用户当轮原文）：
///   > 每当储君打出「抉择，抉择」时，**由玩家替他挑 2 张**要被重复打出的牌。
///   > 实现方式：**再打一次「抉择，抉择」并拦掉它的抽牌**（不要在牌上加「重放」，
///   > 否则抽牌数变多、意图全部算错）。
///   > 流程：5 张里选 1 → 他打出 3 次 → 剩下 4 张里选 1 → 他再打出 3 次 → 继续后面的牌。
///
///   落地方式就照用户说的：第一张打完之后**新建一张「抉择，抉择」实例塞进手牌**再打一次，
///   出牌期间把 <see cref="DrawBlockEntry"/> 置位 ⇒ <c>ShadowBossNoDrawPatch</c> 把它的
///   `CardPileCmd.Draw` 整段拦掉（手牌仍是原来那几张减掉被挑走的那张 = 剩下的 4 张），
///   于是玩家从剩下的技能牌里挑第二张，它同样被原版重复打出 <c>Repeat</c> 次。
///   牌本身**不加任何「重放」标记** ⇒ 抽牌数、意图、`BossDamagePredictor` 全都不受影响。
///
/// 只在有 <see cref="ScriptCard.OnlyIfChosenCard"/> 的回合 / 恐惧模式的抉择储触发，其它情况一行都不走。
/// </summary>
public abstract partial class CharBossMonster
{
    internal async Task RunChoiceFollowUpsAsync(PlayerChoiceContext ctx, PlayerCombatState? pcs,
                                                CardModel? justPlayed)
    {
        try
        {
            if (pcs == null || justPlayed == null || Creature == null || Creature.IsDead) return;
            if (SafeEntryOf(justPlayed) != "DECISIONS_DECISIONS") return;
            if (!ChoiceRegent.IsChoiceBoss(this)) return;

            // ★ v0.2.642（用户：「第一回合第二次选择星位序列他没有出天际钻头」）：
            //   **顺序必须是「先打完第二张抉择，再补打点名牌」**。
            //   以前是先 `PlayChoiceGatedCardsAsync` 再打第二次抉择 ⇒ 那一瞬间 `AllPicksFor` 里
            //   只有第一挑，第二挑（星位序列）还没发生 ⇒ `picks.Contains(Alignment)` 为假
            //   ⇒ 天际钻头永远补不出来。用户原设计也是这个顺序：
            //   「5 张里选 1 → 打出 3 次 → 剩下 4 张里选 1 → 再打出 3 次 → **继续后面的牌**」。
            //   两张都挑完之后再补打，`EntryAllowed` / 这里的 `picks.Contains` 都会认**任意一张**
            //   （第二挑选星位序列也照样出钻头）；而第二挑本来就会排除已挑过的牌
            //   ⇒ 不会出现「两张都是星位序列」而让钻头被打两次。

            // ① 恐惧模式：先把第二张「抉择，抉择」打完（让玩家从剩下的技能牌里挑第二张）
            if (Core.DreadMode.IsActive && ChoiceRegent.TryBeginDreadSecondPlay(this, TurnNumber))
            {
                await PlayDreadSecondDecisionsAsync(ctx, pcs);
            }

            // ② 再把「只有挑了那张才打得出的牌」按剧本补打（第 1 回合的天际钻头）
            await PlayChoiceGatedCardsAsync(ctx, pcs);
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 抉择补打（整段）失败: " + e.Message);
        }
    }

    /// <summary>把剧本里「玩家挑了指定那张才打得出的牌」按顺序补打出去。</summary>
    private async Task PlayChoiceGatedCardsAsync(PlayerChoiceContext ctx, PlayerCombatState pcs)
    {
        try
        {
            if (Creature == null || Creature.IsDead) return;
            if (!ChoiceRegent.TurnHasChoiceGatedCard(this, TurnNumber)) return;

            int turnNumber = TurnNumber;
            var picks = ChoiceRegent.AllPicksFor(this, turnNumber);
            if (picks.Count == 0)
            {
                Logger.Log($"[CharBoss] 抉择：第 {turnNumber} 回合没有记下玩家挑了哪张 ⇒ 不补打");
                return;
            }

            var scriptTurn = Def.ScriptForAct(ActIndex)?.TurnAt(turnNumber);
            if (scriptTurn?.Cards == null) return;

            foreach (var entry in scriptTurn.Cards)
            {
                if (entry?.OnlyIfChosenCard == null) continue;
                if (!picks.Contains(entry.OnlyIfChosenCard)) continue;
                if (Creature.IsDead) break;

                var card = FindHandCardOf(pcs, entry.Card);
                if (card == null)
                {
                    Logger.Log($"[CharBoss] 抉择：{entry.Card.Name} 不在手牌里 ⇒ 不补打");
                    continue;
                }

                var target = BossPlanner.ChooseTarget(this, card) ?? Creature;
                if (target == null) continue;

                // X 费牌的「费」= 出手那一刻的剩余能量（和剧本规划同一把尺子，见 BossScriptPlanner.IsXCost）
                int cost;
                try { cost = BossScriptPlanner.IsXCost(card) ? Math.Max(0, pcs.Energy) : BossPlanner.CostOf(card); }
                catch { cost = 0; }
                int starCost;
                try { starCost = BossPlanner.StarCostOf(card); } catch { starCost = 0; }

                var play = new PlannedPlay
                {
                    Card = card,
                    Target = target,
                    Cost = cost,
                    StarCost = starCost,
                    Intents = new List<MegaCrit.Sts2.Core.MonsterMoves.Intents.AbstractIntent>(),
                };

                Logger.Log($"[CharBoss] 抉择：玩家选了 {entry.OnlyIfChosenCard.Name} ⇒ 补打 {SafeEntryOf(card)}" +
                           $"（费 {cost}、当前能量 {pcs.Energy}、星光 {pcs.Stars}）");
                Plan.Add(play);
                try { await PlayOne(ctx, pcs, play); }
                catch (Exception e) { Logger.Log($"[CharBoss] 抉择补打 {SafeEntryOf(card)} 失败: " + e.Message); }
                finally { Plan.Remove(play); }
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 抉择补打（点名牌那一段）失败: " + e.Message);
        }
    }

    /// <summary>
    /// 恐惧模式「指导」：新建一张「抉择，抉择」塞进手牌再打一次，**出牌期间拦掉它的抽牌**
    /// ⇒ 玩家从「剩下的技能牌」里挑第二张，它同样被原版重复打出。
    /// </summary>
    private async Task PlayDreadSecondDecisionsAsync(PlayerChoiceContext ctx, PlayerCombatState pcs)
    {
        var player = Creature?.Player;
        var combat = CombatState;
        if (player == null || combat == null) return;

        CardModel? made = null;
        try
        {
            var canonical = ModelDb.GetById<CardModel>(
                ModelDb.GetId(typeof(MegaCrit.Sts2.Core.Models.Cards.DecisionsDecisions)));
            if (canonical == null)
            {
                Logger.Log("[CharBoss] 指导（恐惧）：取不到「抉择，抉择」⇒ 不再打第二次");
                return;
            }
            made = combat.CreateCard(canonical, player);
            if (made == null) return;

            await CardPileCmd.AddGeneratedCardToCombat(made, PileType.Hand, player, CardPilePosition.Top);
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 指导（恐惧）：造第二张「抉择，抉择」失败: " + e.Message);
            return;
        }

        var target = BossPlanner.ChooseTarget(this, made) ?? Creature;
        int playCost;
        try { playCost = BossPlanner.CostOf(made); } catch { playCost = 0; }

        var play = new PlannedPlay
        {
            Card = made,
            Target = target,
            Cost = playCost,
            StarCost = 0,
            Intents = new List<MegaCrit.Sts2.Core.MonsterMoves.Intents.AbstractIntent>(),
        };

        // ⚠ 必须在 PlayOne **之前**置位：`ShadowBossNoDraw.ShouldBlock` 在看到 DrawBlockEntry 非空时
        //   会无条件拦掉这次抽牌（PlayOne 自己在 finally 里把它清回 null，那已经是出完牌之后了）。
        var previousBlock = DrawBlockEntry;
        DrawBlockEntry = "DECISIONS_DECISIONS";
        Plan.Add(play);
        try
        {
            Logger.Log($"[CharBoss] 指导（恐惧）：再打一次「抉择，抉择」并**拦掉它的抽牌**" +
                       $"（手牌 {pcs.Hand.Cards.Count()} 张）」⇒ 让玩家从剩下的技能牌里挑第二张");
            await PlayOne(ctx, pcs, play);
            Logger.Log("[CharBoss] 指导（恐惧）：第二张也打完了（重复打出 3 次）⇒ 继续她后面的牌");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 指导（恐惧）：第二次「抉择，抉择」失败: " + e.Message);
        }
        finally
        {
            Plan.Remove(play);
            DrawBlockEntry = previousBlock;
        }
    }

    /// <summary>她手里第一张「是这种牌」的实例（按模型 id 的 entry 比，升级版也算）。</summary>
    private static CardModel? FindHandCardOf(PlayerCombatState pcs, Type cardType)
    {
        try
        {
            string? wanted = null;
            try { wanted = ModelDb.GetId(cardType)?.Entry; } catch { wanted = null; }

            foreach (var card in pcs.Hand.Cards)
            {
                if (card == null) continue;
                try
                {
                    if (card.GetType() == cardType) return card;
                    if (wanted == null) continue;
                    string? actual = ModelDb.GetId(card.GetType())?.Entry;
                    if (string.Equals(actual, wanted, StringComparison.Ordinal)) return card;
                }
                catch { /* 单张读不到就跳过 */ }
            }
        }
        catch { /* 忽略 */ }
        return null;
    }
}
