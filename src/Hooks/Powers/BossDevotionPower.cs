#if STS2_111   // 观者 Boss 只在 0.113 构建里（用户：107 不加这个 Boss）
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using CharacterBosses.Core;

namespace CharacterBosses.Powers;

/// <summary>
/// 三层 1 号观者 Boss「**神格观**」的特殊效果「**虔信**」（用户 2026-11-15）。
///
/// 用户原话：
/// > 你每打出一张牌，观者就获得一层真言；在观者的回合开始时，她失去 5 层真言。
/// > 每当她获得 10 层真言时，她会退出当前姿态、获得 3 点力量并进入神格，
/// > 而且她不再会在回合开始时退出神格。
/// > 恐惧模式：回合开始时失去 **3** 层真言；满 10 层时退出当前姿态、获得 **4** 点力量、进入神格。
///
/// ## 为什么真言由我们自己记（`Amount`），而不是直接用观者模组的 `Mantra`
///
/// 观者的 `WatcherCombatHelper.GainMantra` 只是「记两个计数器 + `Apply<Mantra>`」，
/// 而 `Mantra` 这个 Power 自己带「满 10 层」的结算 —— **时机和顺序不归我们管**，
/// 而用户要求的是「**先退出当前姿态**再进神格」（否则本来就在神格里时拿不到入姿态的 3 点能量）。
/// 所以这里用一个属于我们自己的计数器（就是本 Power 的 `Amount`，会显示成 buff 图标），
/// 三个数字都由我们算：+1 / 回合开始 −5（不转负）/ 满 10 层结算。
/// 姿态本身仍然走观者自己的接口（`WatcherCards.ExitStanceAsync` / `EnterDivinityAsync`），
/// 所以「神格：+3 能量、攻击三倍」这些真正的效果还是观者模组实现的。
/// </summary>
public sealed class BossDevotionPower : PowerModel
{
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override bool ShouldPlayVfx => false;

    /// <summary>满多少层触发一次（用户：10 层）。</summary>
    internal const int Threshold = 10;

    /// <summary>她自己的回合开始时失去多少层：普通 5 / 恐惧 3（用户口径）。</summary>
    internal static int LossPerTurn()
    {
        try { return DreadMode.IsActive ? 3 : 5; }
        catch { return 5; }
    }

    /// <summary>满 10 层时给多少力量：普通 3 / 恐惧 4（用户口径）。</summary>
    internal static int StrengthOnDivinity()
    {
        try { return DreadMode.IsActive ? 4 : 3; }
        catch { return 3; }
    }

    // ── ① 玩家每打出一张牌 ⇒ 观者 +1 真言 ─────────────────────────────────
    public override async Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        try
        {
            var card = cardPlay?.Card;
            if (card == null) return;

            var ownerPlayer = card.Owner;
            if (ownerPlayer == null) return;
            if (ownerPlayer == OwnerMonster()?.Shadow) return;      // 她自己打的牌不算

            await AddMantra(choiceContext, 1);
        }
        catch (Exception e) { Logger.Log("[CharBoss] 虔信：出牌加真言失败: " + e.Message); }
    }

    // ── ② 她的回合开始 ⇒ 失去 N 层真言（不转负） ────────────────────────────
    public override async Task AfterSideTurnStart(CombatSide side,
                                                 IReadOnlyList<Creature> participants,
                                                 ICombatState combatState)
    {
        try
        {
            if (side != CombatSide.Enemy) return;                  // 只在她（怪物方）的回合开始

            int cur = (int)Math.Round((double)Amount);
            if (cur <= 0) return;

            int loss = Math.Min(cur, LossPerTurn());
            await SetAmountSafe(cur - loss);
            Logger.Log($"[CharBoss] 虔信：她的回合开始，真言 {cur} → {cur - loss}（本回合失去 {loss} 层）");
        }
        catch (Exception e) { Logger.Log("[CharBoss] 虔信：回合开始扣真言失败: " + e.Message); }
    }

    // ── 加真言（含满层结算） ───────────────────────────────────────────────
    private async Task AddMantra(PlayerChoiceContext ctx, int amount)
    {
        int cur = (int)Math.Round((double)Amount);
        int next = cur + amount;
        await SetAmountSafe(next);

        while (next >= Threshold)
        {
            next -= Threshold;
            await SetAmountSafe(next);
            await TriggerDivinity(ctx);
            Logger.Log($"[CharBoss] 虔信：真言满 {Threshold} 层 ⇒ 退出姿态、+{StrengthOnDivinity()} 力量、进入神格");
        }
    }

    /// <summary>满 10 层的结算：**先退姿态** ⇒ +力量 ⇒ **再进神格**（顺序是用户点名要求的）。</summary>
    private async Task TriggerDivinity(PlayerChoiceContext ctx)
    {
        var boss = OwnerMonster();
        var shadow = boss?.Shadow;
        var creature = Owner ?? boss?.Creature;

        try { await WatcherCards.ExitStanceAsync(shadow); }
        catch (Exception e) { Logger.Log("[CharBoss] 虔信：退出姿态失败: " + e.Message); }

        if (creature != null)
        {
            try
            {
                await PowerCmd.Apply<StrengthPower>(ctx, creature,
                    StrengthOnDivinity(), creature, null, false);
            }
            catch (Exception e) { Logger.Log("[CharBoss] 虔信：加力量失败: " + e.Message); }
        }

        try { await WatcherCards.EnterDivinityAsync(shadow, null); }
        catch (Exception e) { Logger.Log("[CharBoss] 虔信：进入神格失败: " + e.Message); }

        // 进神格会额外给 3 点能量，可能让「有费就打」的牌打得出去 ⇒ 意图必须重算
        try { boss?.RefreshPlannedIntents(); } catch { /* 下一帧刷新 */ }
        try { UI.BossHandUi.RefreshForce(boss!); } catch { /* 纯显示 */ }
    }

    private async Task SetAmountSafe(int value)
    {
        int v = Math.Max(0, value);
        try { SetAmount(v, true); await Task.CompletedTask; }
        catch (Exception e) { Logger.Log("[CharBoss] 虔信：写层数失败: " + e.Message); }
    }

    private Monsters.CharBossMonster? OwnerMonster()
    {
        try { return Owner?.Monster as Monsters.CharBossMonster; }
        catch { return null; }
    }
}
#endif
