using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Combat.History;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using CharacterBosses.Monsters;
using CharacterBosses.Powers;

namespace CharacterBosses.Patches;

/// <summary>
/// 第二层 Boss「灵魂骨」的特殊效果 **灵魂绑定**：**玩家在回合内抽牌时，也视为她抽牌**。
///
/// 用户原话：「你在回合内抽牌时也视为亡灵契约师抽牌（会增加自动化的计数和死亡行军的伤害）」。
///
/// ## 原版是怎么算的（反编译确认，别猜）
/// <code>
/// // 死亡行军 DeathMarch：伤害倍率 = 本回合「她自己抽到几张」+1
/// new CalculatedDamageVar(ValueProp.Move).WithMultiplier((card, _) =&gt;
///     History.Entries.OfType&lt;CardDrawnEntry&gt;()
///            .Count(e =&gt; e.HappenedThisTurn(card.CombatState)
///                     &amp;&amp; e.Actor == card.Owner.Creature     // ← 只数「她自己」
///                     &amp;&amp; !e.FromHandDraw));                  // ← 手动抽牌不算
/// // CardDrawnEntry(card, …) : base(card.Owner.Creature, …)   ← Actor 就是牌的 Owner 生物
/// </code>
/// <code>
/// // 自动化 AutomationPower.AfterCardDrawn：卡的主人 == 能力主人的 Player 就 −1，减到 0 给能量并重置为 10
/// if (card.Owner == automationPower.Owner.Player) { data.cardsLeft--; … if (cardsLeft &lt;= 0) GainEnergy(Amount) }
/// </code>
///
/// ## 这里的做法
/// 挂在 <see cref="Hook.AfterCardDrawn"/> 上（每次抽牌结算后原版都会走它）：
///   1. **真人玩家**抽到一张牌（`card.Owner.Creature.Monster == null`；影子 Player 的牌会被排除）
///      且场上有带「灵魂绑定」的 Boss 时 → 替她补一条 `CardDrawnEntry`
///      （用她自己牌堆里现成的某张牌当载体 —— 只为了 `e.Actor` / `card.Owner` 指到她，
///      **不移动、不抽走她的牌**，所以手牌/牌堆完全不变，等于零副作用）；
///   2. 再把她身上的 **自动化** 按同一次抽牌手工结算一次（只调那一个 Power，
///      **不调整个 Hook** —— 否则玩家身上的「混乱」等抽牌钩子会误伤她牌堆里的牌）。
///
/// 单人零回归：这段只在她（灵魂骨）身上有「灵魂绑定」时才生效；
/// 其他 Boss 没有这个 Power ⇒ 一次也不会进到这里。
/// </summary>
[HarmonyPatch(typeof(Hook), nameof(Hook.AfterCardDrawn))]
public static class SoulBoundDrawPatch
{
    private static int _logged;
    private static bool _busy;

    /// <summary>「这位玩家不是她锁定的那位 ⇒ 这次抽牌不算」这条日志最多打几次。</summary>
    private static int _skipped;

    [HarmonyPrefix]
    public static void Prefix(ICombatState combatState, PlayerChoiceContext choiceContext,
                              CardModel card, bool fromHandDraw)
    {
        if (_busy) return;
        try
        {
            if (combatState == null || card == null) return;

            // 用户 2026-10-23：**回合开始时抽的牌不算**。原版「回合开始补手牌」就是
            // `CombatManager.SetupPlayerTurn` → `CardPileCmd.Draw(..., fromHandDraw: true)`，
            // 所以这里按 `fromHandDraw` 区分：true = 回合开始补牌（跳过），false = 回合内额外抽牌（记账）。
            // 回合开始抽牌：原版补手牌是 `fromHandDraw: true`；而**power 在回合开始施放的抽牌**
            // （小提琴 / 机器学习 / 崩坠「自弑之欲」）走的是 `false`。后者的共同特征是
            // 「玩家这一回合还一张牌都没打」⇒ 也按回合开始抽牌处理，不计入死亡行军。
            //
            // ★ 2026-11-13（恐惧模式「灵魂绑定」，用户当轮口径）：
            //   原文 PS「每回合**开始时的抽牌也算**」⇒ 恐惧模式下**不再排除**这一类，
            //   回合开始补的牌同样算「亡灵契约师抽了一张牌」（自动化计数 + 死亡行军伤害）。
            if (!Core.DreadMode.IsActive && (fromHandDraw || PlayerCardPlayedTracker.PlaysThisTurn == 0))
            {
                if (!fromHandDraw && _logged++ < 10)
                {
                    Logger.Log($"[CharBoss] 回合开始抽牌 → 不计入死亡行军（{card.Id.Entry}，本回合玩家出牌数=0）");
                }
                return;
            }

            var ownerCreature = card.Owner?.Creature;
            if (ownerCreature == null) return;
            if (!IsRealPlayer(ownerCreature)) return;          // 影子 Player（Boss 自己）的抽牌不镜像

            var boss = BossSoulBoundPower.OwnerBoss(combatState);
            if (boss == null) return;

            // ★ v0.2.240（用户 2026-10-30 联机反馈：「联机时灵魂骨的特殊效果会计算**所有玩家**的抽牌，
            //   而不是只有被她锁定的玩家」）：
            //   「灵魂绑定」是**对应型**特殊效果 —— 多人局里只该由**她锁定**的那位玩家的抽牌触发。
            //   以前这里只判「是不是真人玩家」，于是 4 人局里谁抽牌都给她加死亡行军/自动化 ⇒ 明显过强。
            //   判据直接用统一的 `BossLockOn.SpecialEffectAppliesTo`（单人恒 true ⇒ 单人零回归，
            //   多人 = 只有锁定她的那位；两端都从同步数据算，所以不会引入不同步）。
            if (!CharacterBosses.Core.BossLockOn.SpecialEffectAppliesTo(boss, ownerCreature.Player))
            {
                if (_skipped++ < 10)
                {
                    Logger.Log($"[CharBoss] 灵魂绑定：{ownerCreature.Name} **不是她锁定的玩家** ⇒ " +
                               $"这次抽牌（{card.Id.Entry}）不计入死亡行军/自动化");
                }
                return;
            }

            _busy = true;
            try
            {
                // 玩家「回合内额外抽牌」：
                //   ① 自动化计数照记（真的减一层）；
                //   ② 死亡行军增伤**走预补池**（`DrawCompensation` 加一笔），
                //      她回合开始时再把这几张折算成「敌人回合这一侧」的历史条目并把池子扣回去。
                //
                // 为什么不再在玩家回合写历史条目（2026-10-23 改）：
                // 玩家侧写的 `CardDrawnEntry` 在**敌人回合**读不到（`HappenedThisTurn` 要求同一边），
                // 而只在玩家回合有效 ⇒ 预览与实战两套口径，容易出现「预览算了、实战没算」。
                // 现在预览和实战都用同一份「真实历史 + 预补池」，涨落一致。
                CreditAutomation(boss, choiceContext, 1, false,
                                 $"真人 {ownerCreature.Name} 回合内抽到 {card.Id.Entry}");
                if (combatState.CurrentSide == CombatSide.Player) boss.NotePlayerInTurnDraw();
            }
            finally
            {
                _busy = false;
            }
        }
        catch (Exception e)
        {
            _busy = false;
            Logger.Log("[CharBoss] 灵魂绑定：镜像抽牌失败: " + e.Message);
        }
    }

    /// <summary>
    /// 替她记 <paramref name="count"/> 次「抽到牌」——**只记数、不搬牌**。
    ///
    /// 两个入口共用：
    ///   · 真人玩家抽到牌（本补丁）→ 记 1 次；
    ///   · **她自己的原版抽牌被拦掉**（<see cref="ShadowBossNoDrawPatch"/>，用户 2026-10-23：
    ///     她的意图是按剧本手牌提前算好的，真抽牌会乱）→ 按请求的次数记账。
    ///
    /// 记账方式（反编译确认）：死亡行军数 `CardDrawnEntry`（`Actor == 她的生物`），
    /// 自动化数「卡的主人是她的 Player」⇒
    ///   ① `History.CardDrawn(combat, 她的一张牌, fromHandDraw)`；
    ///   ② 直接调她身上那一个 `AutomationPower.AfterCardDrawn`（不调整个 Hook，免得误伤别人的抽牌钩子）。
    /// 载体牌是**只读借用**的（不移动、不抽走），所以手牌/牌堆一点不变。
    /// </summary>
    internal static void CreditFakeDraw(CharBossMonster boss, ICombatState combat,
                                        PlayerChoiceContext ctx, bool fromHandDraw, int count,
                                        string reason)
    {
        if (boss == null || combat == null || count <= 0) return;

        AddDrawHistory(boss, combat, count, fromHandDraw, reason);
        CreditAutomation(boss, ctx, count, fromHandDraw, reason);
    }

    /// <summary>
    /// **只加历史条目**（死亡行军增伤用）：补 <paramref name="count"/> 条属于她的 `CardDrawnEntry`。
    /// ⚠ 用的是**当前**的 `combat.CurrentSide` / `RoundNumber`，因为读它的
    /// `HappenedThisTurn` 要求「同回合 + 同一边 + 各玩家回合数没变」三个条件。
    /// </summary>
    internal static void AddDrawHistory(CharBossMonster boss, ICombatState combat, int count,
                                        bool fromHandDraw, string reason)
    {
        if (boss == null || combat == null || count <= 0) return;
        var herCard = RepresentativeCard(boss);
        if (herCard == null)
        {
            Logger.Log("[CharBoss] 灵魂绑定：她牌堆里找不到任何牌，跳过这次记账");
            return;
        }

        for (int i = 0; i < count; i++)
            CombatManager.Instance.History.CardDrawn(combat, herCard, fromHandDraw);

        if (_logged++ < 30)
        {
            Logger.Log($"[CharBoss] 灵魂绑定：{reason} → 补 {count} 条「她抽到牌」的历史" +
                       $"（载体牌={herCard.Id.Entry}，不搬牌，死亡行军 +{count} 段倍率）");
        }
    }

    /// <summary>
    /// **只结算「自动化」**（<c>AutomationPower</c>：每抽 10 张给 1 点能量）——不碰历史，所以不影响死亡行军。
    /// 她回合开始那「3 张」就是走这条路（用户 2026-10-23：回合开始抽牌只加自动化计数）。
    /// </summary>
    internal static void CreditAutomation(CharBossMonster boss, PlayerChoiceContext ctx, int count,
                                          bool fromHandDraw, string reason)
    {
        if (boss == null || ctx == null || count <= 0) return;

        var automation = boss.Creature?.GetPower<AutomationPower>();
        if (automation == null) return;

        var herCard = RepresentativeCard(boss);
        if (herCard == null) return;

        for (int i = 0; i < count; i++)
            _ = RunAutomationAsync(automation, ctx, herCard, fromHandDraw);

        if (_logged++ < 30)
        {
            Logger.Log($"[CharBoss] 灵魂绑定：{reason} → 自动化计数 −{count}" +
                       $"（载体牌={herCard.Id.Entry}，当前显示={automation.Amount}）");
        }
    }

    /// <summary>
    /// 她**回合开始**时调用：把「玩家上个回合内额外抽的牌」折算成属于她的历史条目
    /// （要记在**敌人回合**这一侧，死亡行军的 `HappenedThisTurn` 才认），然后清零。
    /// 自动化在玩家抽牌的那一刻就已经记过了，这里**不再重复记**。
    /// </summary>
    internal static void FlushPendingDrawCredits(CharBossMonster boss, PlayerChoiceContext ctx)
    {
        try
        {
            var combat = boss.CombatState;
            int n = boss.PendingDrawCredits;
            boss.PendingDrawCredits = 0;
            if (combat == null || n <= 0) return;

            AddDrawHistory(boss, combat, n, false, $"玩家上个回合内共抽了 {n} 张");

            // 这几张在预览里是**预补池**算的（`NotePlayerInTurnDraw` 加进去的），现在变成真实历史了
            // ⇒ 把池子里对应的那部分扣掉，显示值保持连续（不会因为折算而突然多加一遍）。
            boss.DrawCompensation = Math.Max(0, boss.DrawCompensation - Math.Max(1, boss.DeathMarchPerDraw) * n);
            Logger.Log($"[CharBoss] 灵魂绑定：折算完成 → 死亡行军预补剩 {boss.DrawCompensation}");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 灵魂绑定：折算玩家抽牌失败: " + e.Message);
        }
    }

    /// <summary>真人玩家 = 有 Player 但**没有** Monster（影子 Player 两个都有）。</summary>
    private static bool IsRealPlayer(Creature creature)
        => creature.Player != null && creature.Monster == null;

    /// <summary>
    /// 拿她牌堆里的一张牌当「她抽到的那张」的载体。**只读**：不移动、不从牌堆抽走。
    /// 顺序（抽牌堆 → 弃牌堆 → 手牌 → 消耗堆）只是为了让日志看起来合理，对数值没影响 ——
    /// 死亡行军只看 `Actor`，自动化只看 `card.Owner`。
    /// </summary>
    private static CardModel? RepresentativeCard(CharBossMonster boss)
    {
        try
        {
            var pcs = boss.Shadow?.PlayerCombatState;
            if (pcs == null) return null;

            foreach (var pile in new[] { pcs.DrawPile, pcs.DiscardPile, pcs.Hand, pcs.ExhaustPile })
            {
                var found = pile?.Cards?.FirstOrDefault(c => c != null);
                if (found != null) return found;
            }
            return pcs.AllCards?.FirstOrDefault(c => c != null);
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 灵魂绑定：取载体牌失败: " + e.Message);
            return null;
        }
    }

    /// <summary>替她结算一次「自动化」（等价于原版 Hook 分发到她那个 Power 的那一路）。</summary>
    private static async Task RunAutomationAsync(AutomationPower power, PlayerChoiceContext ctx,
                                                 CardModel card, bool fromHandDraw)
    {
        try
        {
            if (power?.Owner == null) return;
            await power.AfterCardDrawn(ctx, card, fromHandDraw);
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 灵魂绑定：替她结算自动化失败: " + e.Message);
        }
    }
}
