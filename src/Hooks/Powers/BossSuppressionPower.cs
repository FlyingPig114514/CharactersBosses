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

    /// <summary>
    /// 特殊效果说明。
    ///
    /// 用户 2026-11-13：「新版本在华丽猎的特殊效果中添加警告：警告：静默猎手的『铭记死亡』
    /// 会攻击生命值最少的玩家（忽略召唤物），而不一定是被她锁定的玩家。」
    ///
    /// 为什么单开一个键而不是直接改原文：这段警告只在 0.113 之后成立
    /// （旧版铭记死亡是 1 费单体、按锁定打），所以 `powers` 表里另存
    /// `<ID>.description_v113`（原文 + 空行 + 警告，16 种语言齐备），
    /// 只在 `STS2_111` 这套构建里读它；107 那份仍然读原来的 `description`。
    ///
    /// ★ 2026-11-13（恐惧模式「压制」）：恐惧模式下还要多一条「给她所有玩家 1 层昏鸣」，
    ///   所以另存 `.description_dread`（107 用）/ `.description_v113_dread`（0.113 用）两套。
    /// </summary>
    public override MegaCrit.Sts2.Core.Localization.LocString Description
    {
        get
        {
            try
            {
                bool dread = Core.DreadMode.IsActive;
#if STS2_111
                return new MegaCrit.Sts2.Core.Localization.LocString(
                    "powers", Id.Entry + (dread ? ".description_v113_dread" : ".description_v113"));
#else
                if (dread)
                {
                    return new MegaCrit.Sts2.Core.Localization.LocString(
                        "powers", Id.Entry + ".description_dread");
                }
                return base.Description;
#endif
            }
            catch { return base.Description; }
        }
    }

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

            // ★ 2026-11-13（恐惧模式「压制」，用户当轮原文）：
            //   「每当她打出华丽收场时，给所有玩家 1 层『昏鸣』」（= 仪式兽给的那个 debuff：
            //    下回合只能打出 1 张牌）。
            //   原版 `RingingPower` 就是它：Debuff / Single，`AfterApplied` 给那位玩家**所有卡牌**
            //   挂 `Afflictions.Ringing`，之后 `ShouldPlay` 只放行本回合的第一张带 Ringing 的牌
            //   ⇒ 正是「下回合只能打出 1 张牌」。所以这里直接施加原版那层 power，不自己造机制。
            if (Core.DreadMode.IsActive)
            {
                await ApplyRingingAsync(choiceContext, owner, card);
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 压制（华丽收场塞虚空）失败: " + e.Message);
        }
    }

    /// <summary>恐惧模式：给**所有真人玩家**各 1 层原版「昏鸣」（`RingingPower`）。</summary>
    private static async Task ApplyRingingAsync(PlayerChoiceContext choiceContext,
                                                Creature owner, CardModel? source)
    {
        var combat = owner.CombatState;
        if (combat == null) return;
        var players = Core.BossLockOn.RealPlayers(combat);
        if (players == null) return;

        int rung = 0;
        foreach (var player in players)
        {
            var creature = player?.Creature;
            if (creature == null || !creature.IsAlive) continue;
            try
            {
                await PowerCmd.Apply<MegaCrit.Sts2.Core.Models.Powers.RingingPower>(
                    choiceContext, creature, 1m, owner, source);
                rung++;
            }
            catch (Exception e)
            {
                Logger.Log("[CharBoss] 压制（恐惧）：给玩家施加昏鸣失败: " + e.Message);
            }
        }
        Logger.Log($"[CharBoss] 压制（恐惧）：她打出华丽收场 ⇒ 给 {rung} 名玩家各 1 层昏鸣" +
                   "（下回合只能打出 1 张牌）");
    }
}
