using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using CharacterBosses.Core;

namespace CharacterBosses.Powers;

/// <summary>
/// 一层 Boss「愤怒战」的特殊效果「**群情激奋**（全局）」（用户 2026-10-23）。
///
/// 用户原话：
///   · 每当**铁甲战士打出愤怒**时，也向**所有玩家的弃牌堆**加入一张愤怒的复制品；
///   · 每当**有玩家打出愤怒**时，铁甲战士**本回合失去 1 点力量**（用黑暗镣铐的 debuff）。
///
/// 为什么用一个挂在她身上的 Power 就能看到玩家出牌：原版 `Hook.BeforeCardPlayed`
/// 是广播给 `CombatState.IterateHookListeners()` 的，它把每个生物的 `Powers` 都算进去
/// （Boss 在 `_enemies` 里）—— 和「袖里乾坤」(<see cref="SleeveTrickPower"/>) 用的是同一条路。
///
/// 「失去 1 点力量」用的是原版 `DarkShacklesPower`（黑暗镣铐用的就是它，继承
/// `TemporaryStrengthPower`，`IsPositive = false`）：它立刻把持有者的力量 −1，
/// 并在**到期**（`TemporaryStrengthPower` 的回合结束结算）时还回来 ⇒ 正好是「本回合」。
/// </summary>
public sealed class IroncladRagePower : PowerModel
{
    public override PowerType Type => PowerType.Buff;

    /// <summary>
    /// **Counter**：让图标把「本回合会失去多少力量」显示成数字（用户 2026-10-29：
    /// 「可以考虑做计数，比如群情激愤在 1/2/3/4 人时分别是 3/2/1/1 层，就减对应层数的力量」）。
    ///
    /// ⚠ 原版 `NPower.RefreshAmount` 只在 `StackType == Counter` 时画数字；
    ///   数字本身来自 <see cref="DisplayAmount"/>（**不要**再往 `Amount` 里塞人数换算值：
    ///   用户 2026-10-29 实测，那样描述里的 `{Amount}` 会显示成层数「1」而不是力量损失）。
    /// </summary>
    public override PowerStackType StackType => PowerStackType.Counter;

    /// <summary>层数不随人数自动放大：它自己按人数算（见 <see cref="StrengthLossPerAnger"/>）。</summary>
    public override bool ShouldScaleInMultiplayer => false;

    public override bool ShouldPlayVfx => false;

    /// <summary>
    /// 玩家打出「愤怒」时她本回合失去多少临时力量（用户 2026-10-23 的口径，按**真人玩家数**分摊）：
    ///   · 1 人（单人）：**3**（保持原样，不变）
    ///   · 2 人联机：**2**
    ///   · 3～4 人：**1**（用户说「未实现，但你先记着」⇒ 先按 1 处理，等多人实装再验）
    /// </summary>
    private static decimal StrengthLossPerAnger(int realPlayerCount)
        => realPlayerCount >= 3 ? 1m : (realPlayerCount == 2 ? 2m : 3m);

    /// <summary>场上真人玩家数（拿不到按 1 算）。</summary>
    private int RealPlayerCount(ICombatState? combat = null)
    {
        try
        {
            return Math.Max(1, Core.BossLockOn.RealPlayerCount(combat ?? Owner?.CombatState));
        }
        catch { return 1; }
    }

    /// <summary>本回合每次触发会失去多少力量（描述与图标都读它）。</summary>
    private int LossNow() => (int)StrengthLossPerAnger(RealPlayerCount());

    /// <summary>图标上的数字 = 当前人数对应的力量损失（3 / 2 / 1）。</summary>
    public override int DisplayAmount => Math.Max(1, LossNow());

    /// <summary>
    /// 描述里的 <c>{Loss}</c>：本地化文案用 `{Loss}` 而不是 `{Amount}`
    /// （`{Amount}` 是原版层数，改它会被别处（层数结算）跟着变，用户 2026-10-29 实测过）。
    /// </summary>
    public override MegaCrit.Sts2.Core.Localization.LocString Description
    {
        get
        {
            var description = base.Description;
            try
            {
                description.Add("Loss", LossNow().ToString());
            }
            catch { /* 忽略：取不到就显示文案里的占位符 */ }
            return description;
        }
    }

    /// <summary>回合开始时重画一次数字（人数不会中途变，主要是让开战第一帧就是对的）。</summary>
    public override Task BeforeSideTurnStart(PlayerChoiceContext choiceContext, CombatSide side,
                                             IReadOnlyList<Creature> participants, ICombatState combatState)
    {
        try { InvokeDisplayAmountChanged(); } catch { /* 忽略 */ }
        return Task.CompletedTask;
    }

    public override async Task BeforeCardPlayed(CardPlay cardPlay)
    {
        try
        {
            var card = cardPlay.Card;
            if (card is not Anger) return;

            var shadow = Owner?.Player;                 // 铁甲战士的影子 Player
            var combat = Owner?.CombatState ?? card.CombatState;
            if (combat == null || shadow == null) return;

            // 描述/图标上的数字保持最新（人数不会中途变，这里只是幂等地重画一次）
            try { InvokeDisplayAmountChanged(); } catch { /* 忽略 */ }

            // ⚠ 「这张牌是谁打的」以**牌的归属**为准，不能只看 `cardPlay.Player`：
            // 她自己的牌是 `CardCmd.AutoPlay` 打出去的，`cardPlay.Player` 可能是本地玩家/空，
            // 于是她打「愤怒」会被误判成「玩家打愤怒」→ 白扣自己力量
            // ⇒ 她的攻击意图偏低（用户 2026-10-23 实测：「好像把自己打愤怒也算成减力量了」）。
            var owner = card.Owner;
            bool playedByHer = owner != null && ReferenceEquals(owner, shadow);

            if (playedByHer)
            {
                // ① 她自己打出「愤怒」→ 给每个真人玩家的弃牌堆也塞一张复制品（**不扣自己的力量**）
                await GiveAngerCopiesToPlayers(combat, card);
                return;
            }

            // ② 真人玩家打出「愤怒」→ 她本回合按人数分摊的临时力量
            var actor = owner ?? CharacterBosses.Core.GameCompat.ActorOf(cardPlay);
            bool actorIsRealPlayer = actor != null && actor.Creature != null && actor.Creature.Monster == null;
            if (actorIsRealPlayer) await LoseStrengthThisTurn(cardPlay, combat);
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 群情激奋触发失败: " + e.Message);
        }
    }

    /// <summary>给**每个真人玩家**的弃牌堆各加一张「愤怒」的复制品（升级状态照抄她打出的那张）。</summary>
    ///
    /// ⚠ 2026-10-29 修「打出愤怒停顿太长」：以前是**每个玩家各来一遍** `Preview + Cmd.Wait(1f)`
    /// ⇒ 4 人局一次「愤怒」要停 4 秒多（日志实证：17:50:42 / 43 / 44 / 45 各一条，一秒一条）。
    /// 现在先把所有复制品**全部塞进弃牌堆**，最后**只展示一次**、只等一次（和原版碎屑储君同款观感）。
    ///
    /// ⚠ 同时修日志里露出来的 `塞愤怒复制品失败: Index was out of range`：
    /// 反编译 `CardPileCmd.AddGeneratedCardToCombat` 是 `(await AddGeneratedCardsToCombat(...))[0]`，
    /// 而 `AddGeneratedCardsToCombat` 在**战斗已结束**（`!CombatManager.Instance.IsInProgress`）
    /// 时返回**空数组** ⇒ 下标 [0] 直接抛 IndexOutOfRange。
    /// 也就是「打出愤怒的同一帧战斗已经结算」这种情况：现在先判 IsInProgress，并把这类异常当成
    /// 「战斗结束、剩下的人不用塞了」处理，不再刷红字。
    private async Task GiveAngerCopiesToPlayers(ICombatState combat, CardModel playedAnger)
    {
        bool upgraded = false;
        try { upgraded = playedAnger.IsUpgraded; } catch { /* 用未升级 */ }

        CardModel? firstCopy = null;
        int added = 0, skipped = 0;

        foreach (var player in RealPlayers(combat))
        {
            if (!CombatIsLive(combat))
            {
                skipped++;
                continue;
            }

            try
            {
                var copy = CreateAnger(combat, player, upgraded);
                if (copy == null) { skipped++; continue; }
                // ⚠ `creator` 必须传 **null**（不是玩家自己）：
                // 反编译 `PillarOfCreationPower.AfterCardGeneratedForCombat(card, creator)` 的判据是
                // `creator != null && creator.Creature == Owner` —— 也就是「**你**生成了一张牌」才给格挡。
                // 我们这是**怪物塞牌**，原版怪物塞状态牌不会触发创世之柱（用户 2026-10-24 实测要求）。
                // 牌进哪个牌堆看的是 `card.Owner`（这里就是接收的玩家），与 creator 无关，所以照旧进他的弃牌堆。
                await CardPileCmd.AddGeneratedCardToCombat(copy, PileType.Discard, null);
                added++;
                if (firstCopy == null) firstCopy = copy;

                // 诊断：把「加完之后玩家弃牌堆里到底有几张愤怒」也记下来 ——
                // 用户 2026-10-23 反馈「好像没塞」，有这行就能立刻分清是没加成还是没看见。
                Logger.Log($"[CharBoss] 群情激奋：铁甲战士打出愤怒 → 给 {player.Creature?.Name ?? "玩家"}" +
                           $" 的弃牌堆塞一张{(upgraded ? "愤怒+" : "愤怒")}（{DescribeDiscard(player)}）");
            }
            catch (IndexOutOfRangeException)
            {
                // 战斗在循环中途结算了（见上面的注释）——不是错误，安静跳过
                skipped++;
            }
            catch (Exception e)
            {
                skipped++;
                Logger.Log("[CharBoss] 群情激奋：塞愤怒复制品失败: " + e.GetType().Name + ": " + e.Message);
            }
        }

        // **只展示一次**（用户 2026-10-23：「应该和原版和碎屑储君一样展示塞的牌」）：
        // 原版「塞牌给玩家」就是 `AddGeneratedCardToCombat(...) → CardCmd.Preview(card, 0.75f) → Cmd.Wait(1f)`：
        // 牌此刻**已经在弃牌堆里**，Preview 只是把它居中按正常大小显示一下，然后飞进弃牌堆。
        // 4 人局也只有这一下，不再按人数累加。
        if (firstCopy != null)
        {
            try
            {
                MegaCrit.Sts2.Core.Commands.CardCmd.Preview(firstCopy, 0.75f);
                await MegaCrit.Sts2.Core.Commands.Cmd.Wait(0.8f);
            }
            catch { /* 展示失败不影响效果 */ }
        }

        if (skipped > 0)
        {
            Logger.Log($"[CharBoss] 群情激奋：愤怒复制品成功 {added} 份、跳过 {skipped} 份" +
                       $"（战斗已结算或该玩家已不在场）");
        }
    }

    /// <summary>战斗还在进行（`AddGeneratedCardToCombat` 在结束状态下会返回空数组并让调用方下标越界）。</summary>
    private static bool CombatIsLive(ICombatState combat)
    {
        try
        {
            if (!MegaCrit.Sts2.Core.Combat.CombatManager.Instance.IsInProgress) return false;
            return combat != null;
        }
        catch { return false; }
    }

    /// <summary>「弃牌堆共 N 张，其中愤怒 M 张」——给日志用。</summary>
    private static string DescribeDiscard(Player player)
    {
        try
        {
            var pile = player.PlayerCombatState?.DiscardPile?.Cards;
            if (pile == null) return "弃牌堆取不到";
            int total = 0, angers = 0;
            foreach (var c in pile)
            {
                if (c == null) continue;
                total++;
                if (c is Anger) angers++;
            }
            return $"弃牌堆 {total} 张，其中愤怒 {angers} 张";
        }
        catch { return "弃牌堆统计失败"; }
    }

    /// <summary>造一张属于该玩家的「愤怒」实例（走 `ICombatState.CreateCard`，与原版印牌同一条路）。</summary>
    private static CardModel? CreateAnger(ICombatState combat, Player owner, bool upgraded)
    {
        try
        {
            var canonical = ModelDb.GetById<CardModel>(ModelDb.GetId(typeof(Anger)));
            if (canonical == null) return null;
            var card = combat.CreateCard(canonical, owner);
            if (card == null) return null;
            if (upgraded && !card.IsUpgraded) card.UpgradeInternal();
            return card;
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 群情激奋：造愤怒失败: " + e.Message);
            return null;
        }
    }

    /// <summary>
    /// 铁甲战士本回合失去「按人数分摊」的临时力量 —— 用黑暗镣铐的 `DarkShacklesPower`。
    /// 单人 3 / 2 人 2 / 3～4 人 1（见 <see cref="StrengthLossPerAnger"/>）。
    /// </summary>
    private async Task LoseStrengthThisTurn(CardPlay cardPlay, ICombatState combat)
    {
        var me = Owner;
        if (me == null) return;

        int players = Math.Max(1, RealPlayers(combat).Count);
        // 以**层数**为准（它就是按人数算好写进 Amount 的那个数；描述与图标显示的是同一个）。
        decimal loss = StrengthLossPerAnger(players);

        int before = 0;
        try { before = me.GetPowerAmount<StrengthPower>(); } catch { /* 忽略 */ }

        // BeforeCardPlayed 没有 PlayerChoiceContext → 用原版那个"不需要玩家输入"的上下文
        var ctx = new ThrowingPlayerChoiceContext();
        var applied = await PowerCmd.Apply<DarkShacklesPower>(
            ctx, me, loss, CharacterBosses.Core.GameCompat.ActorOf(cardPlay)?.Creature, cardPlay.Card, false);

        int after = 0;
        try { after = me.GetPowerAmount<StrengthPower>(); } catch { /* 忽略 */ }
        Logger.Log($"[CharBoss] 群情激奋：玩家打出 {cardPlay.Card?.Id.Entry} → " +
                   $"铁甲战士本回合失去 {loss} 力量（{players} 人局；{before} → {after}，" +
                   $"黑暗镣铐层数={applied?.Amount ?? 0}）");

        // 力量变了 → 意图要重算（她的攻击预览要立刻反映这 −1）
        try
        {
            if (me.Monster is CharacterBosses.Monsters.CharBossMonster boss)
                boss.IntentsDirty = true;
        }
        catch { /* 忽略 */ }
    }

    /// <summary>场上所有**真人玩家**（影子 Player 不算：它是 Boss 自己）。</summary>
    private static List<Player> RealPlayers(ICombatState combat)
    {
        var list = new List<Player>();
        try
        {
            foreach (var creature in combat.PlayerCreatures)
            {
                var player = creature?.Player;
                if (player == null) continue;
                if (creature!.Monster != null) continue;      // 影子 Player
                if (list.Contains(player)) continue;
                list.Add(player);
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 群情激奋：收集玩家失败: " + e.Message);
        }
        return list;
    }
}
