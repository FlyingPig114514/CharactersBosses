using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using CharacterBosses.Monsters;

namespace CharacterBosses.Powers;

/// <summary>
/// 三层静默猎手第三套设计「**华丽猎**」的特殊效果「**压制**」（用户 2026-11-09 设计）。
///
/// 用户原话：
/// > 特殊效果：压制：每当静默猎手打出**华丽收场**时，将一张**虚空**放入**所有玩家**的抽牌堆顶部。
/// > 塞牌记得用原版怪的塞牌动画。
///
/// 实现要点：
///   · 挂在**她**身上（`BossDefs` 的变体 `SpecialPower`），所以 `Owner` 就是影子玩家/她本人；
///   · 触发点是她的 `AfterCardPlayed`（她自己打完那张牌之后），只认 `GRAND_FINALE`；
///   · 塞牌走原版管线 `CardPileCmd.AddGeneratedCardToCombat(..., PileType.Draw, player, CardPilePosition.Top)`
///     ⇒ **牌堆顶**（不是洗进去），并且照抄群蛇形态那条：加完再 `CardCmd.PreviewCardPileAdd(added)`
///     播**原版怪塞牌**的动画；
///   · 「所有玩家」= 真人玩家（`BossLockOn.RealPlayers`），多人时每个人都有，影子玩家自己不算。
/// </summary>
public sealed class BossSuppressionPower : PowerModel
{
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Single;

    public override bool ShouldPlayVfx => false;

    /// <summary>华丽收场的 entry（`GrandFinale` → `GRAND_FINALE`）。</summary>
    private const string GrandFinaleEntry = "GRAND_FINALE";

    private int _logs;

    public override async Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        try
        {
            var owner = Owner;
            if (owner == null || !owner.IsAlive) return;
            if (owner.Monster is not CharBossMonster boss) return;

            var card = cardPlay?.Card;
            if (card == null) return;
            // 只算她自己打出的那张（玩家的华丽收场不归这条）
            if (!ReferenceEquals(card.Owner?.Creature, owner)) return;

            string entry;
            try { entry = card.Id.Entry; } catch { return; }
            if (!string.Equals(entry, GrandFinaleEntry, StringComparison.Ordinal)) return;

            var combat = owner.CombatState;
            if (combat == null) return;

            var voidCanonical = ModelDb.GetById<CardModel>(ModelDb.GetId(
                typeof(MegaCrit.Sts2.Core.Models.Cards.Void)));
            if (voidCanonical == null)
            {
                Logger.Log("[CharBoss] 压制：取不到「虚空」（Void）⇒ 跳过塞牌");
                return;
            }

            int given = 0;
            var players = Core.BossLockOn.RealPlayers(combat);
            if (players == null) return;
            foreach (var player in players)
            {
                if (player == null) continue;
                var made = combat.CreateCard(voidCanonical, player);
                if (made == null) continue;

                // 牌堆**顶**（用户口径）＋ 原版「怪物塞牌」动画
                CardPileAddResult added;
                try
                {
                    added = await CardPileCmd.AddGeneratedCardToCombat(
                        made, PileType.Draw, player, CardPilePosition.Top);
                }
                catch (Exception e)
                {
                    Logger.Log("[CharBoss] 压制：塞虚空失败: " + e.Message);
                    continue;
                }
                try { CardCmd.PreviewCardPileAdd(added); }
                catch { /* 动画失败不影响塞牌 */ }
                given++;
            }

            if (_logs++ < 30)
            {
                Logger.Log($"[CharBoss] 压制：她打出华丽收场 ⇒ 给 {given} 名玩家的抽牌堆顶各塞 1 张虚空" +
                           $"（第 {boss.TurnNumber} 回合）");
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 压制（华丽收场塞虚空）失败: " + e.Message);
        }
    }
}
