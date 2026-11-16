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
///   · 满 10 层 ⇒ **我们自己结算**（v0.2.681，用户 2026-11-16 第 3 条）：
///     扣 10 层 → **先退出当前姿态** → 给力量（普通 2 / 恐惧 3）→ 进入神格（原版自己 +3 能量）。
///     ⚠ 以前是观者模组的 `Mantra` 自己触发 `EnterDivinity`，而她已经在神格里时
///       `ChangeStance` 会「原样返回」⇒ 既不退姿态、也没有那 3 点能量和力量（用户实测「再进神格都没有」）。
///       现在模组那条自动触发被 `WatcherRuntimePatches.InstallMantraTakeover()` 压掉了。
///     ⚠ 用户 2026-11-16：「你别给她补，用她原版的加费就行」⇒ 能量**只由原版给**，
///       我们一笔都不加（v0.2.679 补的两笔正是「第 2 回合 5 费」的根因）。
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

            await GainMantraAndResolveAsync(boss, shadow, 1, choiceContext, null);
        }
        catch (Exception e) { Logger.Log("[CharBoss] 虔信：出牌加真言失败: " + e.Message); }
    }

    /// <summary>
    /// **观者获得真言的唯一入口**：加真言 → 立刻结算「满 10 层」。
    ///
    /// 谁会给真言：玩家每打一张牌（上面）、以及 `WatcherFaithPower`（虔信 buff，回合开始给 N 层）。
    /// 满 10 层的结算**不再交给观者模组**（它会直接 `EnterDivinity`，而她已经在神格里时
    /// `ChangeStance` 会「原样返回」⇒ 既不退姿态也没有那 3 点能量，正是用户 2026-11-16 第 3 条），
    /// 改由 <see cref="ResolveDivinityAsync"/> 按用户口径走：扣 10 层 → **先退出姿态** → 力量 → 进神格。
    /// </summary>
    internal static async Task GainMantraAndResolveAsync(Monsters.CharBossMonster boss,
                                                         MegaCrit.Sts2.Core.Entities.Players.Player shadow,
                                                         int amount, PlayerChoiceContext? ctx, CardModel? source)
    {
        if (boss == null || shadow == null || amount <= 0) return;
        await WatcherCards.GainMantraAsync(shadow, amount, source);
        await ResolveDivinityAsync(boss, shadow, ctx);
    }

    /// <summary>
    /// 用户口径（特殊效果原文）：
    /// > 「每当她获得 10 层真言时，她会退出当前姿态、获得 3 点力量并进入神格」
    /// > 「先退出当前姿态…这是为了保证她已处在神格时玩家再打满 10 张牌，观者也可以加费和获得力量」
    ///
    /// 所以每满 10 层都完整走一遍：**扣 10 → 退出姿态 → 力量 → 进入神格**。
    /// 「进入神格」那一步的 +3 能量由观者原版 `Divinity.AfterApplied` 自己给（用户：「别给她补，用她原版的加费」）；
    /// 而退出姿态时原版「平静」会给 +2（`Calm.AfterRemoved`），那笔**按用户口径不算**（否则进神格一会儿 +5 一会儿 +3，
    /// 用户实测「应该五费、实际七费」）⇒ 期间用 `WatcherRuntimePatches.SuppressCalmExitEnergy` 压掉。
    /// </summary>
    internal static async Task ResolveDivinityAsync(Monsters.CharBossMonster boss,
                                                   MegaCrit.Sts2.Core.Entities.Players.Player shadow,
                                                   PlayerChoiceContext? ctx)
    {
        try
        {
            var creature = boss.Creature;
            if (creature == null) return;
            var pcs = shadow.PlayerCombatState;

            int guard = 0;
            while (guard++ < 8)
            {
                int cur = WatcherCards.MantraOf(shadow);
                if (cur < Threshold) break;

                WatcherCards.SetMantra(shadow, cur - Threshold);     // 消耗 10 层

                bool wasCalm = WatcherCards.InCalm(shadow);
                int energyBefore = -1;
                try { energyBefore = pcs?.Energy ?? -1; } catch { /* 忽略 */ }

                // ① 先退出当前姿态（她本来在神格里也照样退 —— 这样「重新进入」才有那 3 点能量）
                //    ⚠ 用户 2026-11-16 傍晚订正：「那个第二回合我忘记算退平静了，不是bug，**让那2费正常加**」
                //    ⇒ 离开平静的 +2 是她的原版机制（`Calm.AfterRemoved` / `CalmStance.OnExitStance`），
                //      这里**不压**；所以从平静进神格当回合共 +5（2 平静 + 3 神格），不进平静就只有 +3。
                try { await WatcherCards.ExitStanceAsync(shadow, ctx); }
                catch (Exception e) { Logger.Log("[CharBoss] 虔信：退出姿态失败: " + e.Message); }

                // ② 力量（普通 2 / 恐惧 3，用户 2026-11-15 削弱后的数值）
                int str = StrengthOnDivinity();
                try
                {
                    var useCtx = ctx ?? new MegaCrit.Sts2.Core.GameActions.Multiplayer.BlockingPlayerChoiceContext();
                    await PowerCmd.Apply<StrengthPower>(useCtx, creature, str, creature, null, false);
                }
                catch (Exception e) { Logger.Log("[CharBoss] 虔信：加力量失败: " + e.Message); }

                // ③ 进入神格（观者原版：+3 能量、攻击三倍）
                await WatcherCards.EnterDivinityAsync(shadow, null, ctx);

                int energyAfter = -1;
                try { energyAfter = pcs?.Energy ?? -1; } catch { /* 忽略 */ }
                Logger.Log($"[CharBoss] 虔信：真言满 {Threshold} ⇒ ① 退出姿态（本来在平静={wasCalm}" +
                           "，在平静就照原版 +2 能量）② 力量 +" + str +
                           $" ③ 进入神格（原版 Divinity 自己 +3 能量）⇒ 能量 {energyBefore} → {energyAfter}" +
                           $"，神格里={WatcherCards.InDivinity(shadow)}");

                // 多出来的能量会让「有费就打」的牌打得出去 ⇒ 整块重规划再刷意图
                try { boss.ReplanForExtraEnergy("进神格多 3 点能量"); } catch { /* 下一帧兜底 */ }
            }
        }
        catch (Exception e) { Logger.Log("[CharBoss] 虔信：满 10 层结算失败: " + e.Message); }
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

            // 诊断（用户 2026-11-16 第 1 条的能量对账）：她回合开始那一刻**真实**拿到多少能量。
            // `PrepareTurn` 写进去的是预算；原版遗物钩子（灯笼这类）会在这之后再补进来 —— 两者一比就知道
            // 「我们该不该手动加」。期望：第 1 回合 3、之后每回合 2。
            try
            {
                int energyNow = shadow.PlayerCombatState?.Energy ?? -1;
                Logger.Log($"[CharBoss] 虔信：她回合开始时的实际能量 = {energyNow}" +
                           "（= PrepareTurn 的预算 + 原版钩子到账的部分）");
            }
            catch { /* 纯诊断 */ }

            int cur = WatcherCards.MantraOf(shadow);
            if (cur <= 0) return Task.CompletedTask;
            int loss = Math.Min(cur, LossPerTurn());
            WatcherCards.SetMantra(shadow, cur - loss);
            Logger.Log($"[CharBoss] 虔信：她的回合开始，真言 {cur} → {cur - loss}（失去 {loss} 层）");
        }
        catch (Exception e) { Logger.Log("[CharBoss] 虔信：回合开始扣真言失败: " + e.Message); }
        return Task.CompletedTask;
    }

    private Monsters.CharBossMonster? OwnerMonster()
    {
        try { return Owner?.Monster as Monsters.CharBossMonster; }
        catch { return null; }
    }
}
#endif
