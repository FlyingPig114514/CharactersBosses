using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;

namespace CharacterBosses.Powers;

/// <summary>
/// 储君（二层：铸剑储君）的特殊效果「**何人僭越**」（与卡同名）。
///
/// 用户设计：**每当储君在一回合失去 25 点生命时**，他就失去 5 点铸造（＝君王之剑伤害 −5），
/// 然后所有玩家本回合失去 1 点力量。每回合最多触发 3 次。
///
/// 实现要点：
///   · 用 <see cref="AfterDamageReceived"/> 累计**本回合**他实际失去的生命（只算真正掉血，被格挡的不算）；
///   · 每满 25 点触发一次，最多 3 次/回合，回合开始清零；
///   · 「失去 3 点铸造」：铸造在原版就是「君王之剑的伤害」——`ForgeCmd.Forge` 只加不减，
///     所以这里直接把**他手上/牌堆里的君王之剑**的 Damage 变量 −3（不低于 0）；
///   · 「所有玩家本回合失去 1 点力量」：给每个玩家施加原版储君的
///     <c>MonarchsGazeStrengthDownPower</c>（「王之凝视」的临时力量削减，1 层）——
///     它在施加时先扣等量 <c>StrengthPower</c>，并在**该玩家回合结束时**移除自己、把力量还回去，
///     因此只影响本回合（早先直接 Apply StrengthPower −1 是永久掉力量，用户实测 bug）。
/// </summary>
public sealed class KnowThyPlacePower : PowerModel
{
    /// <summary>
    /// 触发阈值：一回合累计失去 **25** 点生命。
    ///
    /// ⚠ 用户 2026-10-29 定稿：**不随人数变化，恒为 25**（此前按 `25 × 人数` 放大的写法已废弃）。
    ///   于是每回合的上限 = 25 × 3 = **75**，也就是图标上那个计数（见 <see cref="DisplayAmount"/>）。
    /// </summary>
    private const int HpLossThreshold = 25;

    /// <summary>本场的实际阈值 —— 现在恒等于 <see cref="HpLossThreshold"/>（保留方法便于以后要改回去）。</summary>
    private int ThresholdFor() => HpLossThreshold;

    /// <summary>每次触发失去多少点铸造（＝君王之剑伤害）。用户订正：3 → 5。</summary>
    private const int ForgeLoss = 5;

    /// <summary>每回合最多触发几次。</summary>
    private const int MaxTriggersPerTurn = 3;

    public override PowerType Type => PowerType.Buff;

    /// <summary>
    /// **Counter**：让图标显示数字（用户 2026-10-29：「何人僭越做成硬化外壳那样」）。
    /// 显示的是 <see cref="DisplayAmount"/> = **本回合还能吃下多少掉血**（见它自己的注释）。
    /// `Amount` 本身不参与显示（⚠ 别再往 `Amount` 里塞阈值：`{Amount}` 会随层数变，
    /// 用户 2026-10-29 实测描述里被写成了「失去 1 点生命」）。
    /// </summary>
    public override PowerStackType StackType => PowerStackType.Counter;

    /// <summary>层数不随人数自动放大（阈值恒为 25）。</summary>
    public override bool ShouldScaleInMultiplayer => false;

    /// <summary>
    /// 图标上的计数（照用户 2026-10-29 的口径，做成「硬化外壳」那样）：
    /// **本回合还能吃下多少点掉血，才会再扣一次铸造** = <c>剩余次数 × 25 − 本回合已累计</c>。
    ///
    /// · 一进场就是 3 × 25 = **75**；掉 25 血以后变 50……；
    /// · 三次用完之后是 **0**（这一回合不会再扣铸造，效果与以前完全一致）；
    /// · 她回合开始清零 → 又回到 75。
    /// </summary>
    public override int DisplayAmount
    {
        get
        {
            try
            {
                int remaining = MaxTriggersPerTurn - _triggersThisTurn;
                if (remaining <= 0) return 0;
                int pool = remaining * ThresholdFor() - _lostThisTurn;
                return Math.Max(0, pool);
            }
            catch { return 0; }
        }
    }

    /// <summary>本回合已累计失去的生命。</summary>
    private int _lostThisTurn;

    /// <summary>本回合已触发次数。</summary>
    private int _triggersThisTurn;

    /// <summary>新的一回合：计数清零。</summary>
    public override Task BeforeSideTurnStart(PlayerChoiceContext choiceContext, CombatSide side,
                                             IReadOnlyList<Creature> participants, ICombatState combatState)
    {
        if (Owner != null && side == Owner.Side)
        {
            _lostThisTurn = 0;
            _triggersThisTurn = 0;
            RefreshAmountAndDisplay();
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// 重画图标上的 <see cref="DisplayAmount"/>。
    /// ⚠ **不再动 `Amount`**（用户 2026-10-29 实测：把阈值塞进 `Amount` 之后，
    /// 描述里的 `{Amount}` 被写成了「失去 1 点生命」—— 因为 `Amount` 是层数，不是阈值）。
    /// 描述里的 25 现在直接写在本地化文案里（阈值已定为恒 25）。
    /// </summary>
    private void RefreshAmountAndDisplay()
    {
        try { InvokeDisplayAmountChanged(); } catch { /* 忽略 */ }
    }

    /// <summary>他每受一次伤害就累计一次真实掉血。</summary>
    public override async Task AfterDamageReceived(PlayerChoiceContext choiceContext,
                                                   Creature target,
                                                   DamageResult result, ValueProp props,
                                                   Creature dealer, CardModel cardSource)
    {
        try
        {
            if (Owner == null || target == null) return;
            if (!ReferenceEquals(target, Owner)) return;

            int lost = result?.UnblockedDamage ?? 0;
            if (lost <= 0) return;

            _lostThisTurn += lost;

            int threshold = ThresholdFor();
            // ★ 2026-11-13（恐惧模式「何人僭越」）：用户设计表原文
            //   「一回合内失去 25 生命 ⇒ 所有玩家本回合 −1 力量（PS：**不再减少她铸造**；**每回合可多次**）」
            //   ⇒ 恐惧模式：① 不扣她的铸造；② 去掉每回合 3 次的上限（掉血够就继续触发）。
            bool dread = Core.DreadMode.IsActive;
            int cap = dread ? int.MaxValue : MaxTriggersPerTurn;
            while (_lostThisTurn >= threshold && _triggersThisTurn < cap)
            {
                _lostThisTurn -= threshold;
                _triggersThisTurn++;

                if (!dread) await LoseForge(choiceContext);   // 恐惧模式不再减少她铸造
                await WeakenPlayers(choiceContext);

                Logger.Log($"[CharBoss] 何人僭越：本回合第 {_triggersThisTurn} 次触发" +
                           $"（累计掉血达标 {threshold}）→ " +
                           (dread ? "恐惧模式：不减铸造，" : $"铸造 −{ForgeLoss}，") + "玩家 −1 力量");
            }

            // 图标上那个「还会扣几次铸造」的计数跟着掉血走（用户 2026-10-29 要求）
            try { InvokeDisplayAmountChanged(); } catch { /* 忽略 */ }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 何人僭越触发失败: " + e.Message);
        }
    }

    /// <summary>「失去 3 点铸造」= 把他所有君王之剑的伤害 −3（不低于 0）。</summary>
    private async Task LoseForge(PlayerChoiceContext ctx)
    {
        try
        {
            var owner = Owner?.Player;
            var combat = Owner?.CombatState;
            if (owner == null || combat == null) return;

            int changed = 0;
            foreach (var card in AllCardsOf(owner, combat))
            {
                if (card is not SovereignBlade) continue;
                try
                {
                    var vars = card.DynamicVars;
                    if (!vars.TryGetValue("Damage", out var damageVar) || damageVar == null) continue;
                    decimal next = Math.Max(0m, damageVar.BaseValue - ForgeLoss);
                    if (next == damageVar.BaseValue) continue;
                    damageVar.BaseValue = next;
                    changed++;
                }
                catch { /* 忽略 */ }
            }
            if (changed == 0) Logger.Log("[CharBoss] 何人僭越：他身上没有君王之剑，铸造损失无目标");
            else await RefreshBladeIntent();

            await Task.CompletedTask;
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 何人僭越：扣铸造失败: " + e.Message);
        }
    }

    /// <summary>
    /// 剑的伤害被扣了 → **重算意图**，否则手牌栏里「君王之剑」的意图还是扣之前的数字（偏高，用户实测）。
    ///
    /// 时间点解释：意图是**玩家回合开始时**就算好的，而「何人僭越」是在**玩家攻击她**的过程中触发的，
    /// 所以这一扣必然发生在规划之后 —— 必须手动重算一次（和「获得集中就刷新」是同一套做法）。
    /// </summary>
    private async Task RefreshBladeIntent()
    {
        try
        {
            if (Owner?.Monster is not Monsters.CharBossMonster boss) return;
            boss.RefreshPlannedIntents();
            CharacterBosses.UI.BossHandUi.RefreshForce(boss);
            Logger.Log("[CharBoss] 何人僭越：铸造被扣，已重算君王之剑的意图");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 何人僭越：重算意图失败: " + e.Message);
        }
        await Task.CompletedTask;
    }

    /// <summary>所有玩家本回合失去 1 点力量。</summary>
    private async Task WeakenPlayers(PlayerChoiceContext ctx)
    {
        try
        {
            var combat = Owner?.CombatState;
            if (combat == null) return;
            // ⚠ 只给**真人玩家**：影子 Player 也在 CombatState.Players 里，
            //   照原样遍历会把减益挂到别的 Boss 身上（那是给玩家的收益，绝对不能有）。
            foreach (var player in Core.BossLockOn.RealPlayers(combat))
            {
                var creature = player?.Creature;
                if (creature == null || !creature.IsAlive) continue;
                if (Owner != null && ReferenceEquals(creature, Owner)) continue;

                // ⚠ 必须是「王之凝视」的**临时**削减，不是永久 StrengthPower −1。
                // 原版 `MonarchsGazeStrengthDownPower : TemporaryStrengthPower`（IsPositive=false）：
                //   ① BeforeApplied 里先扣掉等量 StrengthPower（Sign = −1 ⇒ 真的 −1 力量）；
                //   ② AfterSideTurnEnd 里如果参与者含这个生物 → 移除自己并把力量原样还回来。
                // 所以它只影响「本回合」。直接 Apply<StrengthPower>(-1) 会**永久**掉力量（用户实测 bug）。
                // 注意数量传正的 1：IsPositive=false 已经把它解释成"失去"。
                await PowerCmd.Apply<MonarchsGazeStrengthDownPower>(ctx, creature, 1m, Owner, null, false);
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 何人僭越：扣玩家力量失败: " + e.Message);
        }
    }

    /// <summary>他的手牌/抽牌堆/弃牌堆/消耗堆里的所有牌。</summary>
    private static IEnumerable<CardModel> AllCardsOf(MegaCrit.Sts2.Core.Entities.Players.Player player,
                                                     ICombatState combat)
    {
        var result = new List<CardModel>();
        try
        {
            var pcs = player.PlayerCombatState;
            if (pcs == null) return result;
            foreach (var pile in pcs.AllPiles)
            {
                if (pile == null) continue;
                foreach (var card in pile.Cards) if (card != null) result.Add(card);
            }
        }
        catch { /* 忽略 */ }
        return result;
    }
}
