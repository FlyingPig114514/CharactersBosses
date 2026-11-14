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
using MegaCrit.Sts2.Core.ValueProps;

namespace CharacterBosses.Powers;

/// <summary>
/// 恐惧模式「**故障机器人的支配**」的**可视载体**（用户 2026-11-13 改口径）。
///
/// 用户原话：
/// > 故障机器人的支配和亡灵契约师的支配改成给所有玩家上一个「故障机器人的支配」或
/// > 「亡灵契约师的支配」的 buff，类似乐加维林祖母和无厌沙虫等 boss 的实现方法（你可以参考一下），
/// > 效果不变（确保旧的实现方式取消了，别重复实现）
///
/// 参考的原版实现（反编译 `Models.Dread.Dominions.LagavulinMatriarchDominion`）：
/// <code>
/// public override async Task AfterRoomEntered(AbstractRoom room)
/// {
///     if (room is CombatRoom cr)
///         await PowerCmd.Apply&lt;LagavulinMatriarchDominionPower&gt;(
///             new ThrowingPlayerChoiceContext(),
///             cr.CombatState.Players.Select(p =&gt; p.Creature), 1m, null, null);
/// }
/// </code>
/// 而它自己那层 power 也是 `PowerType.Debuff`（挂着玩家身上、做实际结算）。
/// ⇒ 我们把**效果搬进 power**，支配本体只负责「进战斗房间时把它挂上去」。
///
/// 效果（与旧实现完全相同）：**在你的回合结束时，每有一名敌人，你就受到 3 点伤害**。
/// 旧写法是支配自己的 `AfterSideTurnEnd` 里遍历所有玩家扣血（已删除，避免重复结算）。
/// </summary>
public sealed class DefectDominionPower : PowerModel
{
    /// <summary>每名敌人让持有者掉多少血。</summary>
    private const int PerEnemy = 3;

    public override PowerType Type => PowerType.Debuff;

    public override PowerStackType StackType => PowerStackType.Single;

    public override bool ShouldPlayVfx => false;

    /// <summary>玩家侧回合结束时结算（每个持有者只结算自己）。</summary>
    public override async Task AfterSideTurnEnd(PlayerChoiceContext choiceContext, CombatSide side,
                                               IEnumerable<Creature> participants)
    {
        try
        {
            if (side != CombatSide.Player) return;
            var me = Owner;
            if (me == null || !me.IsAlive) return;
            if (me.Monster != null) return;               // 只对真人玩家生效（影子玩家/怪物不算）

            var combat = me.CombatState;
            if (combat == null) return;

            int enemies = 0;
            var monsters = combat.Enemies;
            if (monsters != null)
            {
                foreach (var c in monsters)
                    if (c != null && c.IsAlive) enemies++;
            }
            if (enemies <= 0) return;

            int amount = PerEnemy * enemies;
            await CreatureCmd.Damage(choiceContext, me, amount, ValueProp.Unpowered, (Creature?)null);
            Logger.Log($"[CharBoss] 故障机器人的支配：{enemies} 名敌人 ⇒ {me.Name} 受到 {amount} 点伤害");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 故障机器人的支配结算失败: " + e.Message);
        }
    }
}

/// <summary>
/// 恐惧模式「**亡灵契约师的支配**」的**可视载体**（用户 2026-11-13 改口径）。
///
/// 效果（与旧实现完全相同）：**在你的回合开始时，你随机一张手牌的费用在本回合增加 1**。
/// 旧写法是支配自己的 `AfterPlayerTurnStart`（已删除）。现在挂在玩家身上，每个持有者只管自己。
/// </summary>
public sealed class NecrobinderDominionPower : PowerModel
{
    public override PowerType Type => PowerType.Debuff;

    public override PowerStackType StackType => PowerStackType.Single;

    public override bool ShouldPlayVfx => false;

    /// <summary>
    /// **确定性挑牌**（用户 2026-11-13：「让耗能增加的牌不因 SL 变化」）。
    ///
    /// 输入只有三样，且这三样在 **SL 前后 / 联机两端 / 同一回合重算** 时都**完全相同**：
    ///   1. 本局种子（`BossLineups.RunSeed` → `RunState.Rng.Seed`）；
    ///   2. 该玩家的 `NetId`（同一玩家在任何机器上都是同一个数）；
    ///   3. 当前「可打出的手牌」按手牌顺序排列的 `id.Entry` 序列。
    /// ⇒ 同样的输入必然算出同样的序号 ⇒ 同一回合永远挑同一张牌，SL 不会换牌，联机两端也不会分叉。
    ///
    /// ⚠ **故意不掺「回合数 / 帧号 / 时间」**：回合数在 SL 后可能对不上，
    /// 而手牌序列本来就随回合变化（抽牌不同），足够把不同回合区分开。
    /// </summary>
    private static int StableIndex(IReadOnlyList<CardModel> cards, MegaCrit.Sts2.Core.Entities.Players.Player player)
    {
        try
        {
            if (cards == null || cards.Count == 0) return -1;

            ulong h = 14695981039346656037UL;                 // FNV-1a 偏移基

            h = Mix(h, Core.BossLineups.RunSeed);

            ulong netId = 0;
            try { netId = unchecked((ulong)Convert.ToInt64(player.NetId)); } catch { netId = 0; }
            h = Mix(h, netId);

            foreach (var c in cards)
            {
                if (c == null) continue;
                string entry = string.Empty;
                try { entry = c.Id.Entry ?? string.Empty; } catch { entry = string.Empty; }
                h = MixString(h, entry);
                // 升级状态也掺进去（同名不同升级算两张不同的牌）
                try { if (c.IsUpgraded) h = Mix(h, 0x9E3779B97F4A7C15UL); } catch { /* 忽略 */ }
            }

            return (int)(h % (ulong)cards.Count);
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 亡灵契约师的支配：稳定挑牌失败（退回第 0 张）: " + e.Message);
            return 0;
        }
    }

    private static ulong Mix(ulong h, ulong v)
    {
        h ^= v;
        h *= 1099511628211UL;      // FNV-1a 质数
        h ^= h >> 32;
        return h;
    }

    private static ulong MixString(ulong h, string? s)
    {
        if (string.IsNullOrEmpty(s)) return Mix(h, 0UL);
        for (int i = 0; i < s.Length; i++) h = Mix(h, (ulong)s[i]);
        return Mix(h, (ulong)s.Length);
    }

    public override Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext,
                                              MegaCrit.Sts2.Core.Entities.Players.Player player)
    {
        try
        {
            var me = Owner?.Player;
            if (me == null || player == null) return Task.CompletedTask;
            if (!ReferenceEquals(player, me)) return Task.CompletedTask;   // 每个持有者只管自己的回合

            var all = player.PlayerCombatState?.Hand?.Cards;
            if (all == null || all.Count == 0) return Task.CompletedTask;

            // ★ 2026-11-13 修（玩家实测：第 1 回合挑中了诅咒牌「登顶者之灾」⇒ 加费毫无意义）：
            //   只从**打得出去**的手牌里随机挑。判据复用规划器那把尺子
            //   （`BossScriptPlanner.IsPlayable`：排除 状态 / 诅咒 / 任务 牌与带 `Unplayable` 关键字的牌，
            //    但「执迷」是**可打出的诅咒**，单独放行）。
            var playable = new List<CardModel>();
            foreach (var c in all)
            {
                if (c == null) continue;
                if (Core.BossScriptPlanner.IsPlayable(c)) playable.Add(c);
            }
            if (playable.Count == 0)
            {
                Logger.Log($"[CharBoss] 亡灵契约师的支配：{player.Character?.GetType().Name} 手上" +
                           $"没有可打出的牌（{all.Count} 张全是状态/诅咒）⇒ 本回合不涨价");
                return Task.CompletedTask;
            }

            // ★ 2026-11-13 修（用户：「怎么 sl 增加耗能的牌还会变？让耗能增加的牌不因 sl 变化」）：
            //   **不能用 `Random.Shared.Next`** —— 那是进程级随机数：
            //     · SL 之后这条钩子若再跑一次，就会挑到**另一张牌**（用户看到的现象）；
            //     · 联机时两端各摇各的 ⇒ 两端看到的加费牌不同。
            //   改成**确定性哈希**：种子只用「本局种子 + 玩家 NetId + 当前手牌的可打出牌 id 序列」，
            //   这三样在 SL 前后、以及联机两端都是**完全相同**的 ⇒ 同一回合永远挑同一张。
            int index = StableIndex(playable, player);
            if (index < 0 || index >= playable.Count) index = 0;
            var pick = playable[index];
            if (pick == null) return Task.CompletedTask;

            // ★ 2026-11-13 修（用户：「每回合都没有手牌的能耗增加」）：
            //   **`AddThisCombat(int amount, bool reduceOnly = false)` 的第二个参数是 `reduceOnly`，不是「本回合」！**
            //   （反编译：`_localModifiers.Add(new LocalCostModifier(amount, Relative, EndOfCombat, reduceOnly))`）
            //   ⇒ 以前写的 `AddThisCombat(1, true)` 意思是「加 1 费、但只允许朝**降低**的方向生效」
            //   ⇒ 正数被整条吞掉，一次都没生效过（从 v0.2.593 第一版支配起就错着）。
            //   正确接口是 `AddThisTurn(1)`（过期时机 = `EndOfTurn`），正好对应「本回合 +1 费」。
            //   注意本模组其它几处 `AddThisCombat(-n, true)` 是**负数**（减费）⇒ `reduceOnly` 恰好满足，那些是对的。
            //
            // ★ 同时做成**幂等 + 自愈**（SL 专用）：
            //   · 这张牌本回合**已经有**本地费用修正 ⇒ 说明这次是重入（SL 后钩子又跑了一遍）⇒ 跳过，不会叠成 +2；
            //   · 修正被 SL 丢掉了 ⇒ `HasLocalModifiers == false` ⇒ 这里会**重新加回去** ⇒ SL 前后费用一致。
            if (pick.EnergyCost.HasLocalModifiers)
            {
                Logger.Log($"[CharBoss] 亡灵契约师的支配：{player.Character?.GetType().Name} 的「" +
                           $"{pick.Id.Entry}」本回合已经加过费（重入/SL）⇒ 不重复叠加");
                return Task.CompletedTask;
            }

            int beforeCost = -999, afterCost = -999;
            try { beforeCost = pick.EnergyCost.GetResolved(); } catch { /* 忽略 */ }

            pick.EnergyCost.AddThisTurn(1);

            try { afterCost = pick.EnergyCost.GetResolved(); } catch { /* 忽略 */ }

            Logger.Log($"[CharBoss] 亡灵契约师的支配：{player.Character?.GetType().Name} 的「" +
                       $"{pick.Id.Entry}」本回合费用 +1（{beforeCost} → {afterCost}；" +
                       $"候选 {playable.Count}/{all.Count} 张，稳定序号 {index}）");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 亡灵契约师的支配结算失败: " + e.Message);
        }
        return Task.CompletedTask;
    }
}

#if STS2_111
/// <summary>
/// 恐惧模式「**愚者的支配**」的**可视载体**（用户 2026-11-13 新增）。
///
/// ⚠ 只在 111 构建里：它要发的是 0.113 才有的状态牌 **「悲伤」`Sadness`**
/// （`base(-1, CardType.Status, CardRarity.Status, TargetType.None)`）。107 没有这张牌，
/// 而恐惧模式本身就是 0.113 独有的 ⇒ 整类放进 `#if STS2_111`。
/// </summary>
public sealed class FoolDominionPower : PowerModel
{
    /// <summary>给几张「悲伤」。</summary>
    private const int SadnessCount = 2;

    public override PowerType Type => PowerType.Debuff;

    public override PowerStackType StackType => PowerStackType.Single;

    public override bool ShouldPlayVfx => false;

    /// <summary>本场是否已经发过（每场战斗只发一次）。</summary>
    private bool _given;

    public override async Task BeforeSideTurnStart(PlayerChoiceContext choiceContext, CombatSide side,
                                                   IReadOnlyList<Creature> participants, ICombatState combatState)
    {
        try
        {
            if (_given) return;
            if (side != CombatSide.Player) return;

            var me = Owner;
            if (me == null || !me.IsAlive) return;
            var player = me.Player;
            if (player == null || player.PlayerCombatState == null) return;

            var canonical = ModelDb.GetById<CardModel>(ModelDb.GetId(
                typeof(MegaCrit.Sts2.Core.Models.Cards.Sadness)));
            if (canonical == null)
            {
                Logger.Log("[CharBoss] 愚者的支配：取不到「悲伤」（Sadness）⇒ 不发牌");
                return;
            }

            int given = 0;
            for (int i = 0; i < SadnessCount; i++)
            {
                var made = combatState.CreateCard(canonical, player);
                if (made == null) continue;
                try
                {
                    var added = await CardPileCmd.AddGeneratedCardToCombat(
                        made, PileType.Draw, player, CardPilePosition.Random);
                    try { CardCmd.PreviewCardPileAdd(added); } catch { /* 动画失败不影响发牌 */ }
                    given++;
                }
                catch (Exception e)
                {
                    Logger.Log("[CharBoss] 愚者的支配：塞「悲伤」失败: " + e.Message);
                }
            }

            _given = true;
            Logger.Log($"[CharBoss] 愚者的支配：给 {me.Name} 的抽牌堆洗入 {given} 张「悲伤」");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 愚者的支配结算失败: " + e.Message);
        }
    }
}
#endif
