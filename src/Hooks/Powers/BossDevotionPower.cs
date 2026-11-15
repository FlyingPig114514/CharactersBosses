#if STS2_111   // 观者 Boss 只在 0.113 构建里（用户：107 不加这个 Boss）
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
/// 三层 1 号观者 Boss「**神格观**」的特殊效果「**虔信**」（用户 2026-11-15）。
///
/// 用户原话：
/// > 你每打出一张牌，观者就获得一层真言；在观者的回合开始时，她失去 5 层真言（最多失去全部，不会变负）。
/// > 每当她获得 10 层真言时，她会退出当前姿态、获得 3 点力量并进入神格，而且她不再会在回合开始时退出神格。
/// > 恐惧模式：回合开始失去 3 层；满 10 层给 4 点力量。
///
/// ## 真言就是**真的真言**（v0.2.672 订正）
///
/// 第一版把真言记成「本 Power 的层数」——用户实测后指出：
/// > 「我打出卡牌并没有让她真的获得真言，而是积累特殊效果的层数。这个特殊效果应该是没有层数的，
/// >   应该让她真的获得真言。」
/// 所以现在：
///   · 玩家出牌 ⇒ `WatcherCards.GainMantraAsync`（观者自己的 `WatcherMod.Mantra` power，界面上就是真言图标）；
///   · 她的回合开始 ⇒ 把那个 power 的层数 −5（恐惧 −3），**钳到 0、绝不为负**；
///   · 满 10 层的结算由观者模组的 `Mantra` 自己触发（它会退姿态 + 进神格），
///     我们只**观察「她这一下真的进了神格」**这件事，然后补上用户要求的那 3 点力量（恐惧 4 点）、
///     记下本回合的 +3 能量，并刷新意图。
///
/// 本 Power 本身 `StackType.Single` ⇒ **不显示任何层数**，只作为「她的特殊效果」的载体和 buff 栏第一位。
/// </summary>
public sealed class BossDevotionPower : PowerModel
{
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Single;

    public override bool ShouldPlayVfx => false;

    /// <summary>满多少层触发（用户：10 层）。</summary>
    internal const int Threshold = 10;

    /// <summary>她自己的回合开始时失去多少层：普通 5 / 恐惧 3。</summary>
    internal static int LossPerTurn()
    {
        try { return DreadMode.IsActive ? 3 : 5; }
        catch { return 5; }
    }

    /// <summary>满 10 层时给多少力量：普通 3 / 恐惧 4。</summary>
    internal static int StrengthOnDivinity()
    {
        try { return DreadMode.IsActive ? 3 : 2; }
        catch { return 3; }
    }

    // 每只 Boss 的「本回合进神格还没兑现的能量」与「上次看到的在神格状态」
    private static readonly Dictionary<int, int> _pendingEnergy = new();
    private static readonly Dictionary<int, bool> _wasInDivinity = new();

    /// <summary>取走「本回合因进神格该加的能量」（取一次就清零，由 `PrepareTurn` 调用）。</summary>
    internal static int TakePendingDivinityEnergy(Monsters.CharBossMonster boss)
    {
        try
        {
            if (boss == null) return 0;
            int key = unchecked((int)(boss.Creature?.CombatId ?? 0u));
            if (!_pendingEnergy.TryGetValue(key, out int v) || v == 0) return 0;
            _pendingEnergy[key] = 0;
            return v;
        }
        catch { return 0; }
    }

    // ── ① 玩家每打出一张牌 ⇒ 观者**真的**获得 1 层真言 ─────────────────────
    public override async Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        try
        {
            var card = cardPlay?.Card;
            if (card?.Owner == null) return;
            var boss = OwnerMonster();
            if (boss == null) return;
            if (card.Owner == boss.Shadow) return;               // 她自己打的牌不算

            var shadow = boss.Shadow;
            if (shadow == null) return;

            await WatcherCards.GainMantraAsync(shadow, 1, null);
            await WatchDivinity(choiceContext, shadow);
        }
        catch (Exception e) { Logger.Log("[CharBoss] 虔信：出牌加真言失败: " + e.Message); }
    }

    // ── ② 她的回合开始 ⇒ 失去 N 层真言（钳到 0） ────────────────────────────
    public override Task AfterSideTurnStart(CombatSide side,
                                           IReadOnlyList<Creature> participants,
                                           ICombatState combatState)
    {
        try
        {
            if (side != CombatSide.Enemy) return Task.CompletedTask;
            var shadow = OwnerMonster()?.Shadow;
            if (shadow == null) return Task.CompletedTask;

            int cur = WatcherCards.MantraOf(shadow);
            if (cur <= 0) return Task.CompletedTask;
            int loss = Math.Min(cur, LossPerTurn());
            WatcherCards.SetMantra(shadow, cur - loss);
            Logger.Log($"[CharBoss] 虔信：她的回合开始，真言 {cur} → {cur - loss}（失去 {loss} 层）");
        }
        catch (Exception e) { Logger.Log("[CharBoss] 虔信：回合开始扣真言失败: " + e.Message); }
        return Task.CompletedTask;
    }

    /// <summary>
    /// 观察「她这一下真的进了神格」：给力量、记本回合 +3 能量、刷新意图。
    /// 神格本身（退姿态 + 进神格 + 3 点能量）是观者模组在满 10 层时自己做的，我们只加用户要的那部分。
    /// </summary>
    private async Task WatchDivinity(PlayerChoiceContext ctx, MegaCrit.Sts2.Core.Entities.Players.Player shadow)
    {
        try
        {
            var boss = OwnerMonster();
            if (boss == null) return;
            int key = unchecked((int)(boss.Creature?.CombatId ?? 0u));

            bool now = WatcherCards.InDivinity(shadow);
            _wasInDivinity.TryGetValue(key, out bool before);

            if (now && !before)
            {
                _wasInDivinity[key] = true;
                int str = StrengthOnDivinity();
                var creature = Owner ?? boss.Creature;
                if (creature != null)
                {
                    try { await PowerCmd.Apply<StrengthPower>(ctx, creature, str, creature, null, false); }
                    catch (Exception e) { Logger.Log("[CharBoss] 虔信：加力量失败: " + e.Message); }
                }

                _pendingEnergy.TryGetValue(key, out int pend);
                _pendingEnergy[key] = pend + 3;

                // ★ 2026-11-15（用户第 3 条：进神格后「有费就打」的牌没亮也没打）：
                //   她的回合预算在 `PrepareTurn` 里**已经算好并写进 `pcs.Energy`** 了，
                //   而她是在**玩家回合**里进的这个神格 ⇒ 计划还是按旧预算排的。
                //   所以这里**立刻**把那 3 点加到当前的 `pcs.Energy` 上，再重排计划；
                //   `_pendingEnergy` 留给「她的回合还没开始」的情况（`PrepareTurn` 会覆盖成新预算再补这一笔）。
                try
                {
                    var pcs = shadow?.PlayerCombatState;
                    if (pcs != null) pcs.Energy += 3;
                }
                catch (Exception e) { Logger.Log("[CharBoss] 虔信：立刻加 3 点能量失败: " + e.Message); }

                Logger.Log($"[CharBoss] 虔信：她进入神格 ⇒ +{str} 力量，本回合 +3 能量");

                try { boss.RefreshPlannedIntents(); } catch { /* 下一帧会刷新 */ }
                try { UI.BossHandUi.RefreshForce(boss); } catch { /* 纯显示 */ }
            }
            else if (!now && before)
            {
                _wasInDivinity[key] = false;
            }
        }
        catch (Exception e) { Logger.Log("[CharBoss] 虔信：神格观察失败: " + e.Message); }
    }

    private Monsters.CharBossMonster? OwnerMonster()
    {
        try { return Owner?.Monster as Monsters.CharBossMonster; }
        catch { return null; }
    }
}
#endif
