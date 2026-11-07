using System;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;

namespace CharacterBosses.Powers;

/// <summary>
/// 第三幕「根除骨」的特殊效果「**预借时间**」。
///
/// 用户设计：**你每花费 10 能量**，你本回合所有牌的耗能就增加 1 能量（＝给玩家 1 层原版
/// <see cref="BorrowedTimePower"/>），并使亡灵契约师获得 1 能量（她带冰淇淋，多余能量会存到下回合）。
/// **累计触发、可触发多次、回合内可叠加**。
///
/// 实现：Boss 身上的 Power 能看到玩家出牌，所以在 <see cref="AfterCardPlayed"/> 里
/// 按这张牌的**耗能**累加；每满 10 点就结算一次（while 循环，花 20 点就结算两次）。
/// 累加是「跨回合累计」的 —— 用户说的是"每花费 10 能量"，没有说每回合清零。
/// </summary>
public sealed class BorrowedTimeBossPower : PowerModel
{
    private const int Threshold = 10;

    public override PowerType Type => PowerType.Buff;

    /// <summary>Counter：状态栏图标上会显示一个数字 —— 就是「还差多少能量她回费」。</summary>
    public override PowerStackType StackType => PowerStackType.Counter;

    public override bool ShouldPlayVfx => false;

    /// <summary>
    /// 挂上去的那一刻把图标数字刷成 10。原版按 Apply 的 amount 先写 1 → 进入战斗时状态栏
    /// 显示「1」，但这里语义是「还差 10 点能量」（用户实测：刚进战斗应该显示 10）。
    /// </summary>
    public override Task AfterApplied(MegaCrit.Sts2.Core.Entities.Creatures.Creature applier,
                                      MegaCrit.Sts2.Core.Models.CardModel cardSource)
    {
        try { SetAmount(Threshold, true); } catch { /* 忽略 */ }
        return Task.CompletedTask;
    }

    /// <summary>
    /// 每个玩家各自已经累计花掉多少点能量（图标上的数字，**从 0 数到 10**，到 10 就结算并留余数）。
    ///
    /// 多人「每玩家版」（设计见 multiplayer-lockon.md §阶段 4）：能量是各花各的，
    /// 所以门槛也必须各算各的 —— 两个人各花 6 点不该凑成一次触发。
    /// 单人（只有一条记录）时行为与原来完全一致。
    /// </summary>
    private readonly System.Collections.Generic.Dictionary<
        MegaCrit.Sts2.Core.Entities.Players.Player, int> _spentByPlayer = new();

    /// <summary>取某玩家当前的累计值（没有就是 0）。</summary>
    private int SpentOf(MegaCrit.Sts2.Core.Entities.Players.Player player)
        => _spentByPlayer.TryGetValue(player, out int v) ? v : 0;

    /// <summary>状态栏图标上的数字 = **离触发最近的那个玩家**还差多少点能量。</summary>
    private void SyncIconAmount()
    {
        try
        {
            int best = Threshold;             // 默认显示满格（还没有人开始攒）
            foreach (var kv in _spentByPlayer)
            {
                int remain = Math.Clamp(Threshold - kv.Value, 1, Threshold);
                if (remain < best) best = remain;
            }
            SetAmount(best, true);
        }
        catch { /* 忽略 */ }
    }

    /// <summary>数量为 0 时别让原版把这条特殊效果移除掉（它要一直挂在状态栏上，从 0 开始数）。
    /// <c>PowerModel.ShouldRemoveDueToAmount()</c> 不是 virtual，覆盖不了 → 改成显示 <c>1 + _spent</c>… 
    /// 不行，那会污染读数；所以采用「数量永远 ≥ 1」的做法：0 时显示 1，其余显示真实值。</summary>

    public override async Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        try
        {
            if (Owner == null || cardPlay?.Card == null) return;

            var actor = CharacterBosses.Core.GameCompat.ActorOf(cardPlay);
            if (actor == null) return;
            if (ReferenceEquals(actor.Creature, Owner)) return;      // 只统计玩家的花费

            // **对应型**（多人）：只统计「锁定这只 Boss」的真人玩家的花费
            //（单人恒为 true；其它 Boss 影子 Player 的出牌一律不算）
            if (!Core.BossLockOn.SpecialEffectAppliesTo(Owner.Monster as Monsters.CharBossMonster, actor)) return;

            int cost = CostOf(cardPlay.Card);
            if (cost <= 0) return;

            int spent = SpentOf(actor) + cost;

            int times = 0;
            var ctx = new BlockingPlayerChoiceContext();
            while (spent >= Threshold)
            {
                spent -= Threshold;
                times++;

                // 给玩家 1 层原版「预借时间」：本回合所有牌耗能 +1（可叠加）
                await PowerCmd.Apply<BorrowedTimePower>(ctx, actor.Creature, 1m, Owner, null, false);

                // 亡灵契约师获得 1 能量（冰淇淋会把它存到下回合）
                var bossPlayer = Owner.Player;
                if (bossPlayer != null) await PlayerCmd.GainEnergy(1m, bossPlayer);

                // 她多 1 点能量 → 「根除」这种 X 费牌的 X（＝段数）跟着变，意图必须重算，
                // 否则手牌栏里还是按原来的能量画的段数（用户实测：要重新计算根除的段数）。
                await RefreshXCostIntents();
            }
            _spentByPlayer[actor] = spent;

            // 图标上的数字 = **还差多少点能量**（10 → 1 倒计时，到 0 就结算并重置回 10）
            SyncIconAmount();
            if (times > 0)
            {
                Flash();
                Logger.Log($"[CharBoss] 预借时间：玩家花费满 {Threshold} 点能量 ×{times} → 玩家 +{times} 层预借时间，" +
                           $"亡灵契约师 +{times} 能量（该玩家本轮已花 {spent}）");
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 预借时间触发失败: " + e.Message);
        }
    }

    /// <summary>这张牌实际花了几点能量（拿不到就按 0）。</summary>
    private static int CostOf(CardModel card)
    {
        try { return Math.Max(0, card.EnergyCost.GetAmountToSpend()); }
        catch { return 0; }
    }

    /// <summary>
    /// 她刚拿到 1 点能量 → 重算意图。为什么必须重算：意图是**玩家回合开始时**算好的，
    /// 而「根除」是 X 费牌，X＝打出时花掉的全部剩余能量，也等于**伤害段数**。
    /// 这笔 +1 能量发生在规划之后，不重算的话手牌栏还按旧的段数画（用户实测）。
    /// </summary>
    private async Task RefreshXCostIntents()
    {
        try
        {
            if (Owner?.Monster is not Monsters.CharBossMonster boss) return;
            boss.RefreshPlannedIntents();
            UI.BossHandUi.RefreshForce(boss);
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 预借时间：重算意图失败: " + e.Message);
        }
        await Task.CompletedTask;
    }
}
