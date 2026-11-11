using System;
using System.Collections.Generic;
using System.Threading.Tasks;
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
/// 只在有 <see cref="ScriptCard.OnlyIfChosenCard"/> 的回合触发，其它回合 / 其它 Boss 一行都不走。
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
            if (!ChoiceRegent.TurnHasChoiceGatedCard(this, TurnNumber)) return;

            int turnNumber = TurnNumber;
            var picked = ChoiceRegent.PickFor(this, turnNumber);
            if (picked == null)
            {
                Logger.Log($"[CharBoss] 抉择：第 {turnNumber} 回合没有记下玩家挑了哪张 ⇒ 不补打");
                return;
            }

            var scriptTurn = Def.ScriptForAct(ActIndex)?.TurnAt(turnNumber);
            if (scriptTurn?.Cards == null) return;

            foreach (var entry in scriptTurn.Cards)
            {
                if (entry?.OnlyIfChosenCard == null) continue;
                if (entry.OnlyIfChosenCard != picked) continue;
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
            Logger.Log("[CharBoss] 抉择补打（整段）失败: " + e.Message);
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
