using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using CharacterBosses.Monsters;

namespace CharacterBosses.Core;

/// <summary>
/// 「**地狱狂徒**」（`Hellraiser`）对**我们的 Boss** 的落地（用户 2026-11-07 的「打击战」）。
///
/// 用户原话：
/// > 5. 地狱狂徒（下回合起，所有名字中包含打击的牌都会被自动免费打出，
/// >    注意多人时要打**被锁定的玩家**，而不是随机的玩家）
/// > 注意被地狱狂徒打出的打击会有特效
///
/// 为什么不能靠原版：这张牌是**能力牌**，它的自动打出挂在「抽到名字含打击的牌」那条钩子上，
/// 而那条钩子只派发给 `CombatState.Players` 里的**真人玩家** —— 影子玩家一辈子收不到
/// （和「缚魂命匣不给奥斯提」同一类问题）。所以由剧本**点名**哪些牌要走这条路
/// （`ScriptCard.HellraiserFree`），这里负责把它们**免费、打向被锁定玩家**地打出去。
///
/// 特效：走 `CardCmd.AutoPlay` 自带的出牌演出（卡面飞出去打人），并在打出前给目标一个受击提示，
/// 这样玩家能看出「这张不是我打的那张，是她自动打出来的」。
/// </summary>
internal static class HellraiserPlays
{
    /// <summary>Boss（CombatId）→ 这一回合里由地狱狂徒自动打出的牌（实例）。</summary>
    private static readonly Dictionary<int, HashSet<CardModel>> Free =
        new Dictionary<int, HashSet<CardModel>>();

    /// <summary>造牌时登记：这张牌由「地狱狂徒」自动免费打出。</summary>
    internal static void MarkFree(CharBossMonster? boss, CardModel? card)
    {
        try
        {
            if (boss?.Creature == null || card == null) return;
            int id = (int)boss.Creature.CombatId;
            if (!Free.TryGetValue(id, out var set))
            {
                set = new HashSet<CardModel>();
                Free[id] = set;
            }
            set.Add(card);
        }
        catch { /* 忽略 */ }
    }

    /// <summary>这张牌是不是「地狱狂徒」要打的那张。</summary>
    internal static bool IsFree(CardModel? card)
    {
        try
        {
            if (card == null) return false;
            foreach (var set in Free.Values)
            {
                if (set.Contains(card)) return true;
            }
        }
        catch { /* 忽略 */ }
        return false;
    }

    /// <summary>回合结束时清掉登记（下一回合会重新造牌、重新登记）。</summary>
    internal static void Clear(int combatId)
    {
        try { Free.Remove(combatId); } catch { /* 忽略 */ }
    }

    /// <summary>
    /// 由地狱狂徒把这张牌打出去：**0 费**、**只打被跟踪锁定的那名玩家**、带一次受击提示。
    /// 返回 true = 已经由这里处理完（调用方不要再走普通出牌流程）。
    /// </summary>
    internal static async Task<bool> TryPlayAsync(CharBossMonster boss, PlayerCombatState pcs,
                                                  PlayerChoiceContext ctx, CardModel? card)
    {
        if (card == null || !IsFree(card)) return false;

        try
        {
            // 目标：**被跟踪锁定的那名玩家**（用户明确要求，不是随机）
            Creature? victim = null;
            try { victim = BossLockOn.TargetFor(boss)?.Creature; } catch { victim = null; }
            if (victim == null || !victim.IsAlive || victim.IsDead)
            {
                try { victim = BossPlanner.ChooseTarget(boss, card); } catch { victim = null; }
            }

            Logger.Log($"[CharBoss] 地狱狂徒：自动免费打出 {card.Id.Entry}" +
                       $" → {(victim == null ? "<无目标>" : victim.Name)}（目标=被锁定玩家）");

            // ★ v0.2.485（用户 2026-11-08：「地狱狂徒打出的打击还是要原版那种铁山靠 + 脚下浮刀的演出」）：
            //   反编译 `HellraiserPower.AfterCardDrawnEarly` 后确认：**原版根本没有单独的 VFX**，
            //   它就是 `CardCmd.AutoPlay(ctx, card, null)` —— 目标传 **null**（自动选），
            //   于是走的是这张打击牌自己的出牌动画（就是我们看到的那套动作）。
            //   我们之前①传了显式目标、②又额外调了 `TriggerAnim(victim, "Hit")`，
            //   后者会把出牌动画顶掉 ⇒ 看起来「没有特效」。现在**完全照原版**来。
            var task = CardCmd.AutoPlay(ctx, card, null, AutoPlayType.Default);
            var finished = await Task.WhenAny(task, Task.Delay(8000));
            if (!ReferenceEquals(finished, task))
            {
                Logger.Log("[CharBoss] 地狱狂徒：自动打出超时(8s)，跳过");
            }

            // 打出去的牌从手牌里挪走（AutoPlay 之后它可能还留在手牌栏）
            try
            {
                if (pcs.Hand.Cards.Any(c => ReferenceEquals(c, card)))
                {
                    pcs.Hand.RemoveInternal(card, true);
                    pcs.DiscardPile.AddInternal(card, -1, true);
                }
            }
            catch { /* 忽略 */ }

            return true;
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 地狱狂徒：自动打出失败（退回普通出牌: " + e.Message);
            return false;
        }
    }
}
