using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using CharacterBosses.Monsters;

namespace CharacterBosses.Core;

/// <summary>
/// 静默猎手（刀猎）「小刀上牌桌」的两件事（行为对齐塔 1 的刀猎）：
///
/// ① **生成刀的牌**（刀刃之舞 / 斗篷与匕首 / 隐秘匕首 / 先制打击 / 刀扇）：
///    回合开始时手里只有那张牌，**不预先摆刀**；
///    它的意图里带着「小刀 ×N」的攻势（见 <see cref="BossIntentBuilder.IntentsFor"/>）。
///    等这张牌真的打出去、真刀进了手牌，才由 <see cref="FrontloadRealShivs"/> 把刀摆出来
///    （亮起 + 每把刀下面各自的攻势）并立刻打掉。
/// ② **袖里乾坤塞的**（每回合玩家首次打出耗能 ≥ 2 的牌 → 3 张）和
///    **无尽刀刃印的**（剧本里每回合开头那张小刀）：这些都是**回合开始就在手牌里**的真卡，
///    所以要摆最前面、最先打出 —— 同样走 <see cref="FrontloadRealShivs"/>。
///
/// 另外：终结技（<c>Finisher</c>）的段数＝本回合在它之前打出的攻击牌数，
/// 而原版 <c>CalculatedHits</c> 规划时永远是 0（一张都还没打）→ 这里按计划重算，
/// 并把「生成刀牌会生出的刀」也算成攻击（bug 3）。
/// </summary>
public static class SilentBlades
{
    /// <summary>小刀（token）牌。</summary>
    private static bool IsShivCard(CardModel card) => ShivPlays.IsShiv(card);

    /// <summary>规划后处理：重算终结技段数（要算上生成刀牌生出的刀）。</summary>
    public static void PostProcess(CharBossMonster boss, List<PlannedPlay> plan)
        => RecomputeFinisherIntents(boss, plan, BossIntentBuilder.PendingRise(boss.Creature));

    /// <summary>
    /// 把她手牌里**还没进计划**的小刀（袖里乾坤塞的、无尽刀刃印的、刚被生成牌生出来的）
    /// 插到计划最前面（＝最先打出），每把刀带自己的攻势意图，并重算终结技段数。
    /// 返回新插入的条目，方便调用方立刻把它们打出去。
    /// </summary>
    public static List<PlannedPlay> FrontloadRealShivs(CharBossMonster boss, bool planTime = true)
    {
        var added = new List<PlannedPlay>();
        try
        {
            var pcs = boss.Shadow?.PlayerCombatState;
            if (pcs == null) return added;

            var planned = new HashSet<CardModel>(boss.Plan.Select(p => p.Card));
            var fresh = pcs.Hand.Cards
                .Where(c => IsShivCard(c) && !planned.Contains(c))
                .ToList();
            if (fresh.Count == 0) return added;

            foreach (var shiv in fresh)
            {
                var target = BossPlanner.ChooseTarget(boss, shiv);
                if (target == null) continue;
                added.Add(new PlannedPlay
                {
                    Card = shiv,
                    Target = target,
                    Cost = 0,
                    StarCost = 0,
                    Intents = BossIntentBuilder.IntentsFor(shiv, target, 0, planTime ? BossIntentBuilder.PendingRise(boss.Creature) : 0),
                    GeneratedShivs = 0,
                });
            }
            if (added.Count == 0) return added;

            boss.Plan.InsertRange(0, added);   // 最前面 → 最先打出（用户要求）

            // 又多了几张小刀（都是攻击牌）→ 手里剑的成长会整体往后挪，
            // 所以整份计划的每张牌意图都要按新顺序重算一遍。
            BladesGrowth.RecomputeAll(boss, boss.Plan, planTime);

            Logger.Log($"[CharBoss] 小刀摆上牌桌: {added.Count} 把（最前、亮着、各带攻势）");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 小刀上位失败: " + e.Message);
        }
        return added;
    }

    /// <summary>
    /// 终结技的段数＝本回合在它之前打出的攻击牌数。
    /// 生成刀牌生出来的刀也算攻击（它们紧跟在那张牌后面打出去）。
    /// </summary>
    public static void RecomputeFinisherIntents(CharBossMonster boss, List<PlannedPlay> plan, int pendingRise = 0)
    {
        try
        {
            int attacks = 0;
            foreach (var play in plan)
            {
                bool isFinisher = SafeEntry(play.Card) == "FINISHER";
                if (isFinisher)
                {
                    int perHit = BossDamagePredictor.HitDamage(play.Card, play.Target) + Math.Max(0, pendingRise);
                    int hits = Math.Max(1, attacks);
                    // 段数 = 本回合之前的攻击牌数；只有 1 段时用单段意图（否则会显示「4×1」）
                    play.Intents = new List<AbstractIntent> { AttackIntent.Multi(perHit, hits) };
                }
                if (play.Card.Type == CardType.Attack) attacks++;
                attacks += Math.Max(0, play.GeneratedShivs);   // 这张牌生出来的刀也是攻击
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 终结技意图重算失败: " + e.Message);
        }
    }

    private static string SafeEntry(CardModel card)
    {
        try { return card.Id.Entry; } catch { return string.Empty; }
    }
}
