#if STS2_111   // 观者 Boss 只在这个构建里
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
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
/// > PS：特殊效果中的「你」指玩家，观者会给**被她锁定的玩家**被保留的牌加费（**单体特殊效果**）。
///
/// 恐惧版（用户 2026-11-17）：
/// > 修改特殊效果：确立基础：在你的回合结束时，你保留的牌在本场战斗中的耗能增加1。
/// ⇒ 去掉「耗能小于 2」这条限制、**不再扣她力量**。
///
/// 实现：挂在**她自己**身上，走 `BeforeSideTurnEnd(Player 侧)` —— 玩家回合结束时，
/// 手里带「保留（`CardKeyword.Retain`）」的牌就是他这一回合会保留下来的那些
/// （弃手牌发生在回合结束之后，这里还没弃）。对每张符合条件的牌
/// `card.EnergyCost.AddThisCombat(+1, reduceOnly: false)`（本场战斗永久 +1），
/// 然后按张数给她上 **-2×张数** 力量（`PowerCmd.Apply&lt;StrengthPower&gt;(-2N)`）。
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

    public override async Task BeforeSideTurnEnd(PlayerChoiceContext choiceContext, CombatSide side,
                                                 IEnumerable<Creature> participants)
    {
        if (side != CombatSide.Player) return;
        try
        {
            var boss = Owner?.Monster as Monsters.CharBossMonster;
            bool dread = Core.DreadMode.IsActive;

            int bumped = 0;
            foreach (var creature in participants)
            {
                try
                {
                    if (creature == null || !creature.IsAlive) continue;
                    var player = creature.Player;
                    if (player == null) continue;                       // 只处理真人玩家
                    if (!BossLockOn.SpecialEffectAppliesTo(boss, player)) continue;   // 单体：只对被锁定者

                    var pcs = player.PlayerCombatState;
                    var hand = pcs?.Hand?.Cards;
                    if (hand == null) continue;

                    var targets = new List<CardModel>();
                    foreach (var card in hand)
                    {
                        if (card == null) continue;
                        bool retain = false;
                        try { retain = card.Keywords.Contains(CardKeyword.Retain); } catch { retain = false; }
                        if (!retain) continue;
                        if (!dread)
                        {
                            // 普通版：只对「耗能 < 2」的保留牌加费
                            int cost = 99;
                            try { cost = card.EnergyCost.GetWithModifiers(CostModifiers.All); } catch { cost = 99; }
                            if (cost >= 2) continue;
                        }
                        targets.Add(card);
                    }

                    foreach (var card in targets)
                    {
                        try
                        {
                            card.EnergyCost.AddThisCombat(1, false);
                            bumped++;
                            if (_logs++ < 24)
                                Logger.Log($"[CharBoss] 确立基础（{(dread ? "恐惧版" : "普通版")}）：{creature.Name} 被保留的 " +
                                           $"{SafeEntry(card)} 耗能 +1（本场战斗）" +
                                           (dread ? "（恐惧版不扣她的力量）" : string.Empty));
                        }
                        catch (Exception e) { Logger.Log("[CharBoss] 确立基础：加费失败: " + e.Message); }
                    }
                }
                catch (Exception e) { Logger.Log("[CharBoss] 确立基础：处理某位玩家失败: " + e.Message); }
            }

            // 普通版：每有一张牌的耗能被增加 ⇒ 她失去 **2** 点力量（恐惧版不扣）
            // ★ v0.2.734（用户 2026-11-17 改文案）：「确立基础：…其在本场战斗中的耗能增加1；
            //   每有一张卡牌的耗能被增加，观者就失去2点力量。」⇒ 由「每张 −1」改成「每张 −2」。
            if (!dread && bumped > 0 && boss?.Creature != null && boss.Creature.IsAlive)
            {
                try
                {
                    int loss = bumped * 2;
                    await PowerCmd.Apply<StrengthPower>(choiceContext, boss.Creature, -loss, boss.Creature, null, false);
                    Logger.Log($"[CharBoss] 确立基础：给 {bumped} 张保留牌加费 ⇒ 观者失去 {loss} 点力量");
                }
                catch (Exception e) { Logger.Log("[CharBoss] 确立基础：扣力量失败: " + e.Message); }
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 确立基础结算失败（不影响战斗）: " + e.Message);
        }
    }

    private static string SafeEntry(CardModel? card)
    {
        try { return card?.Id.Entry ?? "?"; } catch { return "?"; }
    }
}
#endif
