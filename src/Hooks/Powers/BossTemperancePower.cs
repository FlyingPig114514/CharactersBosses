#if STS2_111   // 观者 Boss 只在这个构建里
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;

namespace CharacterBosses.Powers;

/// <summary>
/// 三层 3 号观者 Boss「**红蓝观**」（`WATCHER_RUSHDOWN`）的特殊效果「**节制**」（用户 2026-11-18）：
///
/// > 每当观者打出 12 张牌，她就会强制结束自己的回合并使她和她的其他友方获得 2 点力量。
/// > PS：机制源于塔1 三层 Boss 时间吞噬者，不过这里是她限制自己而非玩家。
/// > 恐惧增强：特殊效果不变，把塔1 的时间扭曲搬过来 —— 两个 buff 同时挂在观者身上，
/// > 1 个计**她自己的**出牌并终止**她**的回合（节制），1 个计**被其锁定玩家**的出牌并终止**该玩家**的回合
/// > （来自塔1 的「时间扭曲」）；两个独立计数、互不干扰，触发时都要播那个钟的特效。
///
/// ## 实现
/// · **普通模式**：本 power 挂在**她**身上，`Amount` = 她本场已打出的牌数（0..11），
///   满 12 ⇒ 清零 + 她与所有友方 +2 力量 + **强制结束她自己的回合**。
/// · **恐惧模式**：额外给她挂一份 <see cref="BossTimeWarpPower"/>（计被锁定玩家的出牌、终止那个玩家的回合）。
/// · 计数在 buff 栏可见（`DisplayAmount` = 已打出张数，满格 12）。
///
/// ⚠ 待办（分阶段清单第 4 项）：塔1 时间扭曲触发时的**时钟动画 + 音效**还没接
/// （要从 StS1 资源里取 `TimeWarp` 的贴图/音效 → 导入 → 进 pck_extras），
/// 现在只打日志 <c>[CharBoss] 节制：满 12 张 ⇒ 结束她的回合 + 全体 +2 力量</c>。
/// </summary>
public sealed class BossTemperancePower : PowerModel
{
    /// <summary>触发阈值：打出 12 张牌（用户口径）。</summary>
    public const int Threshold = 12;

    public override PowerType Type => PowerType.Buff;

    /// <summary>计数型显示（真正的数字走 `DisplayAmount`）。</summary>
    public override PowerStackType StackType => PowerStackType.Counter;

    public override bool ShouldPlayVfx => false;

    /// <summary>
    /// 本场已打出的牌数。⚠ **不能用 `Amount` 存计数**（v0.2.760 用户实测「她身上没有特殊效果节制」）：
    /// `PowerCmd.ModifyAmount` 把数量改成 0 时，原版 `ShouldRemoveDueToAmount()` 会把**整条 power 移除**
    /// （v0.2.541 在「引雷针」上踩过同一个坑）⇒ 第一次满 12 清零后她就永久没有「节制」了。
    /// </summary>
    private int _played;

    /// <summary>buff 栏上显示的数字 = 已打出张数（0..12）。</summary>
    public override int DisplayAmount => Math.Clamp(_played, 0, Threshold);

    private static int _logs;

    /// <summary>
    /// 恐惧模式：**同时**再挂一份「时间扭曲」（BossTimeWarpPower）—— 用户口径：两个 buff 同时挂在
    /// 观者身上，一个计她的出牌（节制）、一个计被锁定玩家的出牌（时间扭曲），两个独立计数、互不干扰。
    /// </summary>
    public override async Task AfterApplied(Creature? applier, CardModel? cardSource)
    {
        try
        {
            if (Owner == null) return;
            if (!Core.DreadMode.IsActive) return;
            var ctx = new BlockingPlayerChoiceContext();
            await PowerCmd.Apply<BossTimeWarpPower>(ctx, Owner, 1m, Owner, null);
            Logger.Log("[CharBoss] 时间扭曲（恐惧模式）：已挂在她身上，单独计被锁定玩家的出牌");
        }
        catch (Exception e) { Logger.Log("[CharBoss] 时间扭曲：挂载失败: " + e.Message); }
    }

    /// <summary>她每打出一张牌 +1；满 12 就触发。</summary>
    public override async Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        try
        {
            if (Owner?.Player == null || cardPlay?.Card == null) return;
            if (!ReferenceEquals(cardPlay.Card.Owner, Owner.Player)) return;   // 只数她自己的牌
            if (Owner.CombatState == null) return;

            _played++;
            if (_played < Threshold) return;
            _played = 0;
            await TriggerAsync(choiceContext, Owner);
        }
        catch (Exception e) { Logger.Log("[CharBoss] 节制：计数失败（不影响战斗）: " + e.Message); }
    }

    /// <summary>触发一次：她与所有友方 +2 力量，然后**强制结束她自己的回合**。</summary>
    internal static async Task TriggerAsync(PlayerChoiceContext ctx, Creature? self)
    {
        try
        {
            if (self?.CombatState == null) return;

            // ① 她和她的其他友方各 +2 力量
            var allies = new List<Creature> { self };
            try
            {
                foreach (var e in self.CombatState.Enemies)
                    if (e != null && !ReferenceEquals(e, self) && e.IsAlive) allies.Add(e);
            }
            catch { /* 忽略 */ }

            await PowerCmd.Apply<MegaCrit.Sts2.Core.Models.Powers.StrengthPower>(ctx, allies, 2m, self, null);

            if (_logs++ < 20)
                Logger.Log("[CharBoss] 节制：满 12 张 ⇒ 结束她的回合 + 她与友方 +2 力量" +
                           "（塔1 时间扭曲的时钟特效/音效待接，见 docs/plans/boss-watcher-rushdown.md）");

            // ② 强制结束她自己的回合（影子玩家 => PlayerCmd.EndTurn）
            var player = self.Player;
            if (player != null)
            {
                try { PlayerCmd.EndTurn(player, false, null); }
                catch (Exception e) { Logger.Log("[CharBoss] 节制：结束她的回合失败（不影响战斗）: " + e.Message); }
            }
        }
        catch (Exception e) { Logger.Log("[CharBoss] 节制：结算失败（不影响战斗）: " + e.Message); }
    }
}

/// <summary>
/// **恐惧模式的「时间扭曲」**（用户 2026-11-18：把塔1 三层 Boss 时间吞噬者的时间扭曲搬过来）：
/// 计**被这只 Boss 锁定玩家的**出牌，满 12 张 ⇒ 终止**那个玩家**的回合（并播同一个钟）。
/// 与她自己的「节制」是两个独立计数、互不干扰。
/// </summary>
public sealed class BossTimeWarpPower : PowerModel
{
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Counter;

    public override bool ShouldPlayVfx => false;

    /// <summary>⚠ 同样不能用 `Amount` 存计数（会被整条移除），见 <see cref="BossTemperancePower"/>。</summary>
    private int _warped;

    public override int DisplayAmount => Math.Clamp(_warped, 0, BossTemperancePower.Threshold);

    public override async Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        try
        {
            if (Owner?.Player == null || cardPlay?.Card == null) return;
            var shooter = cardPlay.Card.Owner;
            if (shooter == null || ReferenceEquals(shooter, Owner.Player)) return;   // 只数玩家那边的牌
            // 只数**被这只 Boss 锁定**的那位玩家（多人时其余玩家不受影响）
            var boss = Owner.Monster as Monsters.CharBossMonster;
            var victim = Core.BossLockOn.PureVictim(boss, Owner.CombatState);
            if (victim == null || !ReferenceEquals(victim, shooter)) return;

            _warped++;
            if (_warped < BossTemperancePower.Threshold) return;
            _warped = 0;
            Logger.Log($"[CharBoss] 时间扭曲：{shooter.Creature?.Name} 满 12 张 ⇒ 终止他的回合" +
                       "（塔1 时间扭曲的时钟特效/音效待接）");
            PlayerCmd.EndTurn(shooter, false, null);
        }
        catch (Exception e) { Logger.Log("[CharBoss] 时间扭曲：结算失败（不影响战斗）: " + e.Message); }
    }
}
#endif
