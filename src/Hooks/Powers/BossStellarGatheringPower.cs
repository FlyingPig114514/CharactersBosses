using System;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Commands;
using CharacterBosses.Core;

namespace CharacterBosses.Powers;

/// <summary>
/// 三层「**辉星储**」的特殊效果「**群星荟萃**」（用户 2026-10-28 设计稿）：
///
/// > 每当储君获得「保留手牌」时，**你**（真人玩家）也获得「保留手牌」和「下回合能量」。
///
/// 反编译口径：游戏里「保留手牌」不是卡牌关键字，而是一层 power ——
/// `Salvo`（箭雨）/ `Convergence`（汇流）/ `Equilibrium`（均衡）都是在
/// `OnPlay` 里 `PowerCmd.Apply&lt;RetainHandPower&gt;(ctx, 自己, 1, 自己, 这张牌)`，
/// 而 `RetainHandPower.AfterSideTurnEnd` 才真正「不丢手牌」。
/// 所以「他获得保留手牌」= **`RetainHandPower` 加到了他身上**，
/// 这里挂在 <see cref="AfterPowerAmountChanged"/> 上判断（同「主宰」给易伤附带虚弱的写法）。
///
/// 落点：
///   · 玩家侧 `RetainHandPower`（本回合手牌保留，回合结束不弃）；
///   · 玩家侧 `EnergyNextTurnPower`（下回合 +`Amount` 能量，默认 1）。
/// 只对**真人玩家**生效（影子/宠物一律跳过），多人时每个真人都给。
/// </summary>
public sealed class BossStellarGatheringPower : PowerModel
{
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Single;

    public override bool ShouldPlayVfx => false;

    /// <summary>下回合给玩家多少能量（每层 1 点；层数可在设计里调）。</summary>
    private const int EnergyPerStack = 1;

    /// <summary>保留手牌的层数（1 = 本回合一次）。</summary>
    private const int RetainStacks = 1;

    private int _logs;

    public override async Task AfterPowerAmountChanged(PlayerChoiceContext choiceContext, PowerModel power,
                                                      decimal amount, Creature? applier, CardModel? cardSource)
    {
        try
        {
            var owner = Owner;
            if (owner == null || !owner.IsAlive) return;

            if (power is not RetainHandPower) return;                 // 只认「保留手牌」
            if (!ReferenceEquals(power.Owner, owner)) return;          // 必须是**他自己**拿到
            if (amount <= 0m) return;                                 // 只在「获得」时（移除不算）

            var combat = owner.CombatState;
            if (combat == null) return;

            var ctx = choiceContext ?? new BlockingPlayerChoiceContext();
            int energy = Math.Max(1, Amount) * EnergyPerStack;

            int given = 0;
            foreach (var player in BossLockOn.RealPlayers(combat))
            {
                var creature = player?.Creature;
                if (creature == null || !creature.IsAlive) continue;
                await PowerCmd.Apply<RetainHandPower>(ctx, creature, RetainStacks, owner, null);
                await PowerCmd.Apply<EnergyNextTurnPower>(ctx, creature, energy, owner, null);
                given++;
            }

            if (_logs++ < 20)
            {
                Logger.Log($"[CharBoss] 群星荟萃：他获得保留手牌（{SafeName(cardSource)}）→ " +
                           $"{given} 名玩家也获得保留手牌 + 下回合 {energy} 能量");
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 群星荟萃结算失败: " + e.Message);
        }
    }

    /// <summary>
    /// 贤者之石（PhilosophersStone）：原版是「每回合 +1 能量、**敌人** +N 力量」——
    /// 「敌人」那条对影子玩家算的是他的敌方阵营，**真人玩家拿不到**（用户 2026-10-28 实测）。
    /// 这里在他身上挂上本 power（= 他出场）时，替遗物把这笔力量补给所有真人玩家。
    /// </summary>
    public override async Task AfterApplied(Creature? applier, CardModel? cardSource)
    {
        try
        {
            var self = Owner;
            var combat = self?.CombatState;
            if (self == null || combat == null) return;

            int str = 0;
            var relics = self.Player?.Relics;
            for (int i = 0; i < (relics?.Count ?? 0); i++)
            {
                var r = relics![i];
                if (r == null) continue;
                if (r.Id.Entry != "PHILOSOPHERS_STONE") continue;
                foreach (var key in new[] { "Strength", "PhilosophersStonePower" })
                {
                    try
                    {
                        if (r.DynamicVars.TryGetValue(key, out var v) && v != null)
                        {
                            str = Math.Max(0, (int)Math.Round((double)v.BaseValue));
                            if (str > 0) break;
                        }
                    }
                    catch { /* 换下一个 key */ }
                }
                if (str <= 0) str = 1;
                break;
            }
            if (str <= 0) return;

            // ★★ 2026-11-07 用户实测：「辉星储的贤者之石给我加了两点力量」⇒ **这里是重复实现**。
            //   原版 `PhilosophersStone` 自己有 `AfterCreatureAddedToCombat` / `AfterRoomEntered`，
            //   对影子玩家这两条**确实会把力量加到真人玩家身上**（当年注释里写的「打不到真人」是旧结论），
            //   我们再手动补一次就变成 +2。
            //   ⇒ 现在**只记一行日志、不再手动加力量**，力量完全交给原版那条路径（无色储君同理，它本来就没有这条路）。
            int given = 0;
            foreach (var real in BossLockOn.RealPlayers(combat))
            {
                var c = real?.Creature;
                if (c != null && c.IsAlive) given++;
            }
            Logger.Log($"[CharBoss] 贤者之石：原版会自己给 {given} 名玩家 +{str} 力量（本 mod 不再重复补）");
            await Task.CompletedTask;
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 贤者之石补力量失败: " + e.Message);
        }
    }
    private static string SafeName(CardModel? card)
    {
        try { return card?.Id.Entry ?? "?"; } catch { return "?"; }
    }
}
