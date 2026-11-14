using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using CharacterBosses.Core;

namespace CharacterBosses.Powers;

/// <summary>
/// 静默猎手（三层：刀猎）的特殊效果「**袖里乾坤**」—— 挂在 **Boss 身上**，
/// 但描述里的「你」指玩家：**每回合玩家首次打出耗能不低于 2 的牌时，
/// 把 3 张小刀加进静默猎手的手牌**（也就是塞给 Boss，让玩家看到"她把刀藏进袖子"）。
///
/// 为什么能收到玩家的出牌：原版 <c>Hook.BeforeCardPlayed</c> 是广播给
/// <c>CombatState.IterateHookListeners()</c> 的，而它会把每个生物的 <c>Powers</c> 都算进去
/// （Boss 就在 <c>_enemies</c> 里）—— 所以 Boss 身上的 Power 天然能看到玩家出牌。
/// 参考实现：原版 <c>SurroundedPower.BeforeCardPlayed</c> 就是靠这个改朝向的。
///
/// 塞进去的小刀会在她回合出牌阶段的末尾被打出去（见 <c>CharBossMonster.PlayPlannedCards</c>），
/// 而这部分**不进意图** —— 用户设计：意图是最低值，触发了袖里乾坤实际伤害更高。
/// </summary>
public sealed class SleeveTrickPower : PowerModel
{
    /// <summary>触发的耗能门槛（用户设计：耗能不低于 2）。</summary>
    private const int MinCost = 2;

    /// <summary>每次塞几张小刀（正常 3 张）。</summary>
    private const int ShivCount = 3;

    /// <summary>
    /// ★ 2026-11-13（恐惧模式「袖里乾坤」）：**每次 4 张**小刀。
    /// （用户设计表原文：「每回合首次打出 ≥2 费牌 ⇒ **4 张小刀**加入她手牌」。）
    /// </summary>
    private const int ShivCountDread = 4;

    /// <summary>这一次实际塞几张（按当前模式取）。</summary>
    private static int ShivCountNow => Core.DreadMode.IsActive ? ShivCountDread : ShivCount;

    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Single;

    /// <summary>
    /// 本回合是否已经触发过。**以「刀猎自己的回合」为界**重置
    /// （用户 2026-10-24：玩家拿到额外回合时——佩尔之泪、某些模组卡——
    /// 以前每个**玩家**回合都会重置一次，一回合下来塞了 6 把小刀）。
    /// </summary>
    private bool _usedThisTurn;

    /// <summary>她自己的回合开始 → 重置「本回合首次」。</summary>
    public override Task BeforeSideTurnStart(PlayerChoiceContext choiceContext, CombatSide side,
                                             IReadOnlyList<Creature> participants, ICombatState combatState)
    {
        if (side == CombatSide.Enemy) _usedThisTurn = false;
        return Task.CompletedTask;
    }

    /// <summary>
    /// 由 Boss 自己的回合开始显式调一次（<c>CharBossMonster.PrepareTurn</c>）——
    /// 不依赖 `BeforeSideTurnStart` 有没有派发到影子玩家那一侧。
    /// </summary>
    internal void ResetForBossTurn() => _usedThisTurn = false;

    /// <summary>玩家每次出牌都会走这里：本回合第一次「耗能 ≥ 2」的牌 → 塞 3 张小刀。</summary>
    public override async Task BeforeCardPlayed(CardPlay cardPlay)
    {
        try
        {
            if (_usedThisTurn) return;

            var card = cardPlay.Card;
            if (card == null) return;

            var shadow = Owner?.Player;        // 静默猎手（影子 Player）
            var actor = CharacterBosses.Core.GameCompat.ActorOf(cardPlay);       // 出牌的人
            if (shadow == null || actor == null) return;

            // 她自己打牌不算「你打出」；玩家的召唤物（宠物）替玩家打出也不算
            if (ReferenceEquals(actor, shadow)) return;

            if (EffectiveCostOf(card, cardPlay) < MinCost) return;

            // **对应型**（多人）：只统计「锁定这只 Boss」的真人玩家的出牌
            //（单人时恒为 true；另外其它 Boss 的影子 Player 出牌一律不算）
            if (!BossLockOn.SpecialEffectAppliesTo(Owner?.Monster as CharacterBosses.Monsters.CharBossMonster, actor))
                return;

            var combat = Owner?.CombatState ?? cardPlay.Card.CombatState;
            if (combat == null) return;

            _usedThisTurn = true;
            await CharacterBosses.Core.GameCompatExtra.CreateShivInHand(shadow, ShivCountNow, combat);
            Logger.Log($"[CharBoss] 袖里乾坤：玩家打出 {card.Id.Entry}（耗能 ≥ {MinCost}）→ " +
                       $"给静默猎手塞 {ShivCountNow} 张小刀");

            // 用户要求：这几把刀要**放在手牌最前面、最先打出、亮着、每把刀下有攻势意图**，
            // 并且终结技的段数跟着变 → 插进计划头部并重算意图，再刷新手牌栏。
            if (Owner?.Monster is CharacterBosses.Monsters.CharBossMonster boss)
            {
                SilentBlades.FrontloadRealShivs(boss);
                CharacterBosses.UI.BossHandUi.Refresh(boss);
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 袖里乾坤触发失败: " + e.Message);
        }
    }

    /// <summary>
    /// 这张牌**实际花了多少能量**（袖里乾坤的门槛看它）。
    ///
    /// 为什么要单独算：**X 费牌**（挽歌 / 根除 / 一心同体…）的牌面耗能是 0（X 本身不算数字），
    /// 真正花掉的是"打出那一刻的剩余能量"。以前只读牌面 ⇒ 玩家用 X 牌砸了 3 费也**不会**触发
    /// （用户 2026-10-24：「消耗费用达到 2 的 X 牌似乎没触发袖里乾坤」）。
    /// 这里依次尝试：牌面耗能 → `CardPlay.Resources.EnergyValue` → `ResolveEnergyXValue()`，取最大值。
    /// </summary>
    private static int EffectiveCostOf(CardModel card, CardPlay cardPlay)
    {
        int cost = 0;
        try { cost = BossPlanner.CostOf(card); } catch { cost = 0; }

        try
        {
            bool isX = false;
            try { isX = card.EnergyCost.CostsX; } catch { isX = false; }

            int spent = -1;
            try { spent = (int)cardPlay.Resources.EnergyValue; } catch { spent = -1; }
            if (spent > cost) cost = spent;

            if (isX)
            {
                int x = 0;
                try { x = card.ResolveEnergyXValue(); } catch { x = 0; }
                if (x > cost) cost = x;
            }
        }
        catch { /* 忽略：按牌面算 */ }

        return Math.Max(0, cost);
    }
}
