using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using CharacterBosses.Monsters;

namespace CharacterBosses.Powers;

/// <summary>
/// 二层储君 3 号「**仆从储**」的特殊效果「**征召上前**」（用户 2026-11-05 设计）。
///
/// 用户原话：
/// > 每当储君打出仆从牌时，若其仆从数不足 2，改为召唤对应种类的仆从。
/// > 每当储君获得力量时，他的所有其它友方也会获得等量力量。
/// > （限制：只在他**用卡牌**获得力量时才给其它友方；
/// >   被玩家减力量而不连累友方；玩家临时减力量结束回涨时也不给。）
///
/// ## 第一条（召唤）：`Patches/MinionSummonPatch`
///   三张仆从牌（仆从打击 / 仆从俯冲 / 仆从捐躯）在原版只是普通牌，
///   由那个补丁在「她身上有本 Power 且当前仆从数 &lt; 2」时拦截原版效果、改为召唤对应仆从。
///
/// ## 第二条（力量共享）：本类
///   在**她打出一张牌的前后**比较她自己的力量：只在她**自己用牌获得力量**时，
///   把增量发给她的其它友方（场上其它敌人，不含她自己）。
///   这样「玩家给她上减力量 / 临时减力量到期回涨」都不会传导给友方。
/// </summary>
public sealed class BossCallToArmsPower : PowerModel
{
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Single;

    public override bool ShouldPlayVfx => false;

    /// <summary>仆从数量上限（不足这个数就改为召唤）。</summary>
    internal const int MinionCap = 2;

    private int _logs;

    /// <summary>她出牌前记录的力量值（用来判断「这张牌有没有给她加力量」）。</summary>
    private static readonly Dictionary<CharBossMonster, int> StrengthBefore = new();

    /// <summary>出牌**之前**：记下她当前的力量（只记她自己打出的牌）。</summary>
    public override Task BeforeCardPlayed(CardPlay cardPlay)
    {
        try
        {
            var owner = Owner;
            var card = cardPlay?.Card;
            if (owner?.Monster is CharBossMonster boss && card?.Owner?.Creature == owner)
            {
                StrengthBefore[boss] = StrengthOf(owner);
            }
        }
        catch { /* 忽略 */ }
        return Task.CompletedTask;
    }

    /// <summary>出牌**之后**：如果她自己的力量涨了，就把等量力量发给其它友方。</summary>
    public override async Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        try
        {
            var owner = Owner;
            if (owner == null || !owner.IsAlive) return;
            if (owner.Monster is not CharBossMonster boss) return;

            var card = cardPlay?.Card;
            if (card == null || card.Owner?.Creature != owner) return;   // 只算她自己打出的牌

            if (!StrengthBefore.TryGetValue(boss, out int before)) return;
            StrengthBefore.Remove(boss);

            int after = StrengthOf(owner);
            int delta = after - before;
            if (delta <= 0) return;      // 只传导「获得力量」

            var combat = owner.CombatState;
            if (combat == null) return;
            int given = 0;
            var who = new List<string>();
            foreach (var ally in combat.Enemies)
            {
                if (ally == null || !ally.IsAlive) continue;
                if (ReferenceEquals(ally, owner)) continue;              // 她自己不加
                // ★ v0.2.514（用户 2026-11-08：「仆从储也会给玩家加力量」）：只发给**她自己这一侧**。
                //   写死按阵营过滤，是因为这条链路唯一可信的判据就是 `Side`：万一 `Enemies` 里
                //   混进了玩家侧的东西（阴影玩家/宠物错绑），力量就会落到玩家头上。
                try { if (ally.Side != owner.Side) continue; } catch { /* 读不到就不拦 */ }
                await PowerCmd.Apply<StrengthPower>(choiceContext, ally, delta, owner, null, true);
                given++;
                if (who.Count < 6) who.Add(SafeName(ally));
            }
            if (given > 0 && _logs++ < 30)
            {
                Logger.Log($"[CharBoss] 征召上前：储君用 {SafeEntry(card)} 获得 {delta} 点力量 ⇒ " +
                           $"其它 {given} 个友方各获得 {delta} 点力量（{string.Join("、", who)}）");
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 征召上前（力量共享）失败: " + e.Message);
        }
    }

    /// <summary>
    /// 敌方回合开始那一刻：把敌人列表重排成「其它敌人 → 所有仆从 → 仆从储本人」，
    /// 于是**仆从先行动、储君永远最后**（用户 2026-11-05）。放在这里而不是召唤时，
    /// 是因为召唤发生在她回合进行中，那时改列表会把行动循环卡死。
    /// </summary>
    public override Task AfterSideTurnStart(MegaCrit.Sts2.Core.Combat.CombatSide side,
                                             IReadOnlyList<Creature> participants,
                                             MegaCrit.Sts2.Core.Combat.ICombatState combatState)
    {
        try
        {
            if (side != MegaCrit.Sts2.Core.Combat.CombatSide.Enemy) return Task.CompletedTask;
            var owner = Owner;
            if (owner?.Monster is CharBossMonster boss)
                Core.MinionOrder.ReorderAtEnemyTurnStart(combatState, boss.Creature);
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 仆从重排（回合开始）失败: " + e.Message);
        }
        return Task.CompletedTask;
    }

    private static int StrengthOf(Creature owner)
    {
        try { return owner.GetPowerAmount<StrengthPower>(); }
        catch { return 0; }
    }

    private static string SafeEntry(CardModel? card)
    {
        try { return card?.Id.Entry ?? "?"; } catch { return "?"; }
    }

    /// <summary>日志用：`名字(Side)`，用来一眼看出力量到底发给了谁。</summary>
    private static string SafeName(Creature? creature)
    {
        try
        {
            if (creature == null) return "?";
            string name = creature.Monster?.Id.Entry ?? creature.Name ?? "?";
            return $"{name}({creature.Side})";
        }
        catch { return "?"; }
    }
}
