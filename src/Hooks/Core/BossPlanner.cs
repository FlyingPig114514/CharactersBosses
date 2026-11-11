using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using CharacterBosses.Monsters;

namespace CharacterBosses.Core;

/// <summary>本回合要打出的一张牌（牌 + 目标 + 花费 + 预告意图）。</summary>
public sealed class PlannedPlay
{
    public required CardModel Card { get; init; }
    public required Creature Target { get; init; }
    public required int Cost { get; init; }
    public required int StarCost { get; init; }

    /// <summary>
    /// 这张牌**她自己不花能量打**，而是由「惊逃」（`StampedePower`）在回合结束打出去
    /// （二层「杂耍战」，用户 2026-11-02）。
    ///
    /// 关键：它**照样进 `Plan`** —— 于是意图渲染、每打一张牌之后的
    /// `BladesGrowth.RecomputeAll(planTime:false)` 重算、以及「力量到账后把预补撤回去」
    /// 全都和普通出牌走**同一条路**。出牌阶段只跳过它（见 CharBossMonster 的播放循环）。
    ///
    /// 之前是塞进 `DisplayPlays` 单独算一份意图 ⇒ 力量真到账之后那笔预补不会被撤回，
    /// 于是惊逃那张的预览「直接叠加起来」（用户 2026-11-02 实测）。
    /// </summary>
    public bool StampedeOnly { get; init; }
    /// <summary>这张牌会显示的全部意图（可能不止一个：痛击 = 攻击 + 易伤）。</summary>
    public List<AbstractIntent> Intents { get; set; } = new();

    /// <summary>
    /// 这张牌打完之后会往她手里塞几张小刀（静默猎手的小刀流）。
    /// 意图里已经按「小刀 ×N」显示；出牌阶段这张牌结算后，
    /// 真刀进手牌 → 由 <see cref="SilentBlades.FrontloadRealShivs"/> 摆出来并立刻打掉。
    /// 0 = 不是小刀牌。
    /// </summary>
    public int GeneratedShivs { get; init; }

    /// <summary>
    /// 这张牌激发的充能球一共造成多少伤害（0 = 不激发球）。
    /// 只用来在手牌栏的意图下方标一个括号数字 —— 激发伤害走的是球自己的结算，
    /// 和目标无关，所以不进 <see cref="Intents"/>。
    /// </summary>
    public int EvokeDamage { get; init; }

    // ───────── ★ v0.2.412：烟囱 / 开信刀 / 散射炮 的三个数在**规划那一刻定死并存下来** ─────────
    //
    // 用户实测（日志 [21:56:57]~[21:57:00]）：同一条「开信刀 5 点」在几次重算之间
    // 从 `HOLOGRAM` 跳到 `BOOST_AWAY`、再跳到 `DEFEND_DEFECT` —— 因为每次重算都是
    // **重新从剩余计划里数**「这是本回合第几张技能牌」，牌一打出去序号就整体前移。
    // 现在这三个数在规划期算好存这里，重算只更新力量/集中那些会变的量。

    /// <summary>烟囱：这张牌打出时**每张**生成的状态牌造成的全体伤害（0 = 她这一手还没上烟囱）。</summary>
    public int SmokePerHit { get; init; }

    /// <summary>这张牌会生成几张状态牌（强撑 = 2、高速脱离 = 1、超频 = 1…）⇒ 烟囱打几段。</summary>
    public int SmokeHits { get; init; }

    /// <summary>开信刀：这张牌正好凑够「每 3 张技能牌」那一档时要打的全体伤害（0 = 这一手不触发）。</summary>
    public int LetterOpenerHit { get; init; }

    /// <summary>本回合排在它前面、会生成的状态牌张数 —— 散射炮的段数要加这一笔。</summary>
    public int PendingStatusCards { get; init; }

    // ───────── ★ 2026-11-10：三层「抉择储」的展示牌（那 5 张候选技能牌） ─────────

    /// <summary>
    /// 这张展示牌**要压暗**（它本回合不会被她打出去，但玩家要看到它的意图）。
    ///
    /// 手牌栏原来用「在不在 `Plan` / `DisplayPlays` 里」决定亮暗 ⇒ 进了 `DisplayPlays` 就一定画亮。
    /// 三层「抉择储」的 5 张候选牌正相反：**暗着**，但要把「选它会怎样」的意图画出来
    /// （用户 2026-11-10 原话：「可以选的那 5 张牌是暗的，但是显示意图」）。
    /// </summary>
    public bool DimWhenShown { get; init; }

    /// <summary>「星位序列」那条：天际钻头每段多少伤害（0 = 这张牌不显示天际钻头的预告）。</summary>
    public int DrillPerHit { get; init; }

    /// <summary>同上：**多出来**的段数（= 选了星位序列之后的段数 − 天际钻头原本的段数）。</summary>
    public int DrillGainHits { get; init; }

    /// <summary>同上（悬停说明用）：天际钻头原本的段数。</summary>
    public int DrillBaseHits { get; init; }

    /// <summary>同上（悬停说明用）：选了星位序列之后天际钻头的总段数。</summary>
    public int DrillTotalHits { get; init; }
}

/// <summary>
/// Boss 的出牌 AI：按 <see cref="BossDefinition.Priority"/> 排序，贪心地在能量预算内选牌。
/// 第一批实现刻意保持简单可预测 —— 计划一旦定下就不再改，所以意图是精确的。
/// </summary>
public static class BossPlanner
{
    public static List<PlannedPlay> Plan(CharBossMonster boss, PlayerCombatState pcs, BossDefinition def)
    {
        var result = new List<PlannedPlay>();
        int energy = pcs.Energy;
        int stars = pcs.Stars;
        var hand = pcs.Hand.Cards.ToList();
        var skipped = hand.Where(c => IsSkipped(def, c)).Select(c => c.Id.Entry).ToList();
        // 球位模拟器：按出牌顺序推进队列，判断哪几张牌会（因为球位已满）激发球
        var orbSim = new BossOrbSim(boss.Creature, pcs.OrbQueue);
        // 到 Boss 出手前还会掉的力量/集中（偏差认知），意图要先扣掉它
        int pendingDrop = BossIntentBuilder.PendingDrop(boss.Creature);
        // 她出手前还会涨的力量（烘焙手套每回合 +1）→ 预览先补上，她回合开始后再抹掉
        int pendingRise = BossIntentBuilder.PendingRise(boss.Creature);
        Logger.Log($"[CharBoss] planning: energy={energy} stars={stars} hand={hand.Count} " +
                   $"[{string.Join(",", hand.Select(c => c.Id.Entry))}]" +
                   (skipped.Count > 0 ? $" skip=[{string.Join(",", skipped)}]" : ""));

        foreach (var card in hand.OrderBy(c => PriorityOf(def, c)).ThenBy(c => CostOf(c)))
        {
            if (!IsUsable(def, card)) continue;

            int cost = CostOf(card);
            int starCost = StarCostOf(card);
            if (cost > energy || starCost > stars) continue;

            var target = ChooseTarget(boss, card);
            if (target == null) continue;

            // 先按顺序推进球位模拟（会激发球的话，意图里要补上攻势/守势）
            var intents = BossIntentBuilder.IntentsFor(card, target, pendingDrop, pendingRise);
            var evoke = orbSim.Predict(card);
            BossOrbSim.AppendIntents(intents, evoke, pendingDrop, BossIntentBuilder.RandomAimIsElsewhere(card));

            result.Add(new PlannedPlay
            {
                Card = card,
                Target = target,
                Cost = cost,
                StarCost = starCost,
                Intents = intents,
                EvokeDamage = evoke.TotalDamage,
                GeneratedShivs = BossIntentBuilder.ShivsFrom(card),
            });
            energy -= cost;
            stars -= starCost;
        }

        Logger.Log($"[CharBoss] planned {result.Count} card(s): " +
                   $"[{string.Join(",", result.Select(p => p.Card.Id.Entry))}] vars=" +
                   $"[{string.Join(" ", result.Select(p => BossIntentBuilder.DescribeVars(p.Card)))}]" +
                   (result.Any(p => p.EvokeDamage > 0)
                       ? $" evoke=[{string.Join(",", result.Where(p => p.EvokeDamage > 0).Select(p => $"{p.Card.Id.Entry}:{p.EvokeDamage}"))}]"
                       : string.Empty));
        return result;
    }

    /// <summary>星星费用（储君的牌会用）。<c>AutoPlay</c> 同样不会替我们扣，所以要自己记账。</summary>
    public static int StarCostOf(CardModel card)
    {
        try { return Math.Max(0, card.GetStarCostWithModifiers()); }
        catch { return 0; }
    }

    /// <summary>优先级：Priority 表里的下标；不在表里按卡牌类型兜底。</summary>
    public static int PriorityOf(BossDefinition def, CardModel card)
    {
        try
        {
            // 多人换牌换上的那张**排最前**：它比原牌贵，按原牌的能量剧本会被同档更便宜的防御挤掉，
            // 效果就永远看不到（用户口径：2 费打出去 → 后面的防御自然打不出）。单人时不会出现这种牌。
            if (MultiplayerCardSwap.IsSwappedIn(card.GetType())) return -1;
        }
        catch { /* 忽略，走正常优先级 */ }

        try
        {
            var id = ModelDb.GetId(card.GetType());
            for (int i = 0; i < def.Priority.Count; i++)
            {
                if (ModelDb.GetId(def.Priority[i]) == id) return i;
            }
        }
        catch (Exception e)
        {
            Logger.Log($"[CharBoss] priority lookup failed for {card.Id.Entry}: {e.Message}");
        }

        return 100 + card.Type switch
        {
            CardType.Power => 0,
            CardType.Attack => 1,
            CardType.Skill => 2,
            _ => 3,
        };
    }

    public static int CostOf(CardModel card)
    {
        try { return Math.Max(0, card.EnergyCost.GetAmountToSpend()); }
        catch { return 0; }
    }

    private static bool IsUsable(BossDefinition def, CardModel card)
    {
        if (card.Type is CardType.Status or CardType.Curse or CardType.Quest) return false;
        if (card.Keywords.Contains(CardKeyword.Unplayable)) return false;
        if (IsSkipped(def, card)) return false;

        // X 费牌先排除：AutoPlay 会把 X 按「当前全部能量」capture，
        // 而我们只按 CostOf 记账 —— 放进来等于让 Boss 白嫖一大截伤害。
        try { if (card.EnergyCost.CostsX) return false; } catch { /* 忽略 */ }
        try { if (card.HasStarCostX) return false; } catch { /* 忽略 */ }

        // 需要玩家做选择（选牌/选生物）的牌在没有真实选择上下文时会空转，先排除
        if (card.TargetType is TargetType.TargetedNoCreature or TargetType.Osty) return false;

        return true;
    }

    /// <summary>用户在设计里明确禁用的牌（<see cref="BossDefinition.SkipCards"/>）。</summary>
    private static bool IsSkipped(BossDefinition def, CardModel card)
    {
        if (def.SkipCards.Count == 0) return false;
        try
        {
            var id = ModelDb.GetId(card.GetType());
            foreach (var skipped in def.SkipCards)
            {
                if (ModelDb.GetId(skipped) == id) return true;
            }
        }
        catch (Exception e)
        {
            Logger.Log($"[CharBoss] skip lookup failed for {card.Id.Entry}: {e.Message}");
        }
        return false;
    }

    /// <summary>
    /// Boss 的「敌人」= 玩家侧；需要目标时挑血最少的玩家。
    /// 安全网：**排除自己和自己的阵营**。影子 Player 挂在 Boss 生物上，一旦哪天 IsPlayer 的隔离补丁失效，
    /// <c>PlayerCreatures</c> 就会把 Boss 自己也列进去，"血最少优先"会让它打自己。
    /// </summary>
    private static string Name(MegaCrit.Sts2.Core.Entities.Creatures.Creature? c)
        => c == null ? "null" : (c.Monster?.GetType().Name ?? c.Name ?? "?");
    public static Creature? ChooseTarget(CharBossMonster boss, CardModel card)
    {
        var combat = boss.CombatState;
        if (combat == null) return null;

        var self = boss.Creature;
        bool wantsOpponent = card.TargetType is TargetType.AnyEnemy or TargetType.AllEnemies or TargetType.RandomEnemy;
        if (!wantsOpponent) return self;

        // 多人「跟踪锁定」：单体/随机攻击**只打锁定这只 Boss 的玩家**（设计见 multiplayer-lockon.md §阶段 1）。
        // 单人时锁定的就是唯一玩家，和原来的「血最少」结果一致，行为不变。
        // ⚠ 斗蛐蛐里 Boss 会被塞进「玩家侧」（作为玩家的宠物），此时锁定到的"玩家"和它同一阵营，
        // 打过去就是"打自己人"（用户实测：左边那只怪一直打自己）。所以同阵营的锁定一律不采纳。
        var locked = BossLockOn.TargetFor(boss)?.Creature;
        if (locked != null && locked.IsAlive && !locked.IsDead && (self == null || locked.Side != self.Side))
        {
            Logger.Log($"[CharBoss] 选靶(锁定): self={Name(self)}({self?.Side}) lock={Name(locked)}({locked.Side})");
            return locked;
        }

        var picked = combat.PlayerCreatures
            .Where(c => c.IsAlive && !c.IsDead)
            .Where(c => !ReferenceEquals(c, self) && (self == null || c.Side != self.Side))
            .OrderBy(c => c.CurrentHp)
            .FirstOrDefault();
        if (picked != null)
        {
            Logger.Log($"[CharBoss] 选靶(玩家侧): self={Name(self)}({self?.Side}) -> {Name(picked)}({picked.Side})");
            return picked;
        }

        // 全员同阵营（斗蛐蛐：对面那队才是真正的对手）→ 退回「真正意义上的敌方」。
        try
        {
            var opp = combat.GetOpponentsOf(self)
                .Where(c => c != null && c.IsAlive && !c.IsDead)
                .OrderBy(c => c.CurrentHp)
                .FirstOrDefault();
            Logger.Log($"[CharBoss] 选靶(对手侧): self={Name(self)}({self?.Side}) -> {Name(opp)}({opp?.Side})");
            return opp;
        }
        catch { return null; }
    }
}
