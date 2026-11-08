using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using CharacterBosses.Monsters;

namespace CharacterBosses.Powers;

/// <summary>
/// 一层「打击战」的特殊效果「**痛击**」（Key `IRONCLAD_STRIKE`，用户 2026-11-07）。
///
/// 用户原话（逐字）：
/// > 「痛击」：你的名字中包含打击的牌也会增加铁甲战士的完美打击的伤害。
/// >   每当你打出名字中包含打击的牌时，铁甲战士的完美打击的伤害就少受一张名字中包含打击的牌的加成。
/// 细则（用户 2026-11-07 补）：
///   · **战斗开始时**结算一次：统计「你四个牌堆里名字含『打击』的牌」＋「她自己的 7 张
///     （打击×4、预备打击、灰烬打击、完美打击）」；
///   · 每 1 张 ⇒ 她的**完美打击 +2**（进阶 0～8）／**+3**（进阶 9～10）；
///   · 之后你每打出 1 张名字含「打击」的牌 ⇒ 池子减 1 张的额度（−2／−3），**最低到 0**；
///   · 也就是 `完美打击伤害 = 卡面基础 + 池子剩余额度`，池子**只减不增**。
///
/// 判定「名字含打击」：牌 id 里带 `STRIKE`（打击 `STRIKE_IRONCLAD`、预备打击 `SETUP_STRIKE`、
/// 灰烬打击 `ASHEN_STRIKE`、完美打击 `PERFECTED_STRIKE` 全都命中）。
/// </summary>
public sealed class BossBashPower : PowerModel
{
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Single;

    public override bool ShouldPlayVfx => false;

    /// <summary>她自己的名字含「打击」的牌张数（用户口径：打击×4 + 预备打击 + 灰烬打击 + 完美打击 = 7）。</summary>
    private const int OwnStrikeCards = 7;

    /// <summary>每张牌给多少伤害（进阶 9～10 = 3，其余 = 2）。</summary>
    internal static int PerCard()
    {
        try { return Core.BossLineups.Ascension() >= 9 ? 3 : 2; }
        catch { return 2; }
    }

    /// <summary>这张牌算不算「名字包含打击」。</summary>
    internal static bool IsStrike(CardModel? card)
    {
        try
        {
            if (card == null) return false;
            string entry = card.Id.Entry ?? string.Empty;
            return entry.Contains("STRIKE", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    /// <summary>池子剩余额度（伤害单位）。第一次访问时按当前战斗结算。<c>0</c> = 已经没有加成了。</summary>
    internal int Pool { get; private set; } = -1;

    /// <summary>还没结算过就算一次（幂等）。</summary>
    internal void EnsureInit()
    {
        if (Pool >= 0) return;
        try
        {
            int cards = OwnStrikeCards;
            var combat = Owner?.CombatState;
            if (combat != null)
            {
                foreach (var player in Core.BossLockOn.RealPlayers(combat))
                {
                    var pcs = player?.PlayerCombatState;
                    if (pcs == null) continue;
                    foreach (var pile in pcs.AllPiles)
                    {
                        if (pile?.Cards == null) continue;
                        foreach (var card in pile.Cards)
                        {
                            if (IsStrike(card)) cards++;
                        }
                    }
                }
            }
            Pool = cards * PerCard();
            Logger.Log($"[CharBoss] 痛击：战斗开始结算 —— 「名字含打击」共 {cards} 张" +
                       $"（她自己 {OwnStrikeCards} + 你那边 {cards - OwnStrikeCards}）× {PerCard()} = **{Pool}** 点完美打击加成");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 痛击结算失败（按 0 处理）: " + e.Message);
            Pool = 0;
        }
    }

    /// <summary>有真人玩家打出一张「名字含打击」的牌 ⇒ 池子少一张的额度（最低 0）。</summary>
    public override Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        try
        {
            EnsureInit();
            var card = cardPlay?.Card;
            if (card == null || !IsStrike(card)) return Task.CompletedTask;

            // 她自己打的不算（她打「打击」不该削自己的加成）
            var owner = card.Owner;
            if (owner != null && ReferenceEquals(owner, Owner?.Player)) return Task.CompletedTask;

            int per = PerCard();
            int before = Pool;
            Pool = Math.Max(0, Pool - per);
            // ★ v0.2.476（用户 2026-11-07：「我打出打击，他的完美打击的伤害并没有降低」）：
            //   以前只在**造牌那一刻**把池子并进卡面，而她的手牌是在**玩家回合之前**造好的
            //   ⇒ 玩家回合里打掉打击之后，那张完美打击的数值不会变。现在池子一变就把她手里/牌堆里
            //   所有完美打击的伤害同步减下去（意图和实伤用的都是这个数）。
            int drop = before - Pool;
            if (drop > 0)
            {
                // 池子变了 ⇒ 让她所有完美打击按新池子重算（意图与实伤同一个数）
                try
                {
                    var boss = Owner?.Monster as CharBossMonster;
                    if (boss != null) Core.BossScriptRunner.RetuneBashPerfectStrike(boss, Pool);
                }
                catch (Exception e2) { Logger.Log("[CharBoss] 痛击：重算失败: " + e2.Message); }
            }
            if (before != Pool && _logs++ < 40)
            {
                Logger.Log($"[CharBoss] 痛击：你打出 {card.Id.Entry} ⇒ 完美打击加成 {before} → {Pool}" +
                           $"（−{per}，最低 0）");
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 痛击结算（玩家打出打击）失败: " + e.Message);
        }
        return Task.CompletedTask;
    }

    /// <summary>她（影子玩家）所有牌堆里的完美打击实例。</summary>
    private IEnumerable<CardModel> bossShadowCards()
    {
        var pcs = Owner?.Player?.PlayerCombatState;
        if (pcs == null) yield break;
        foreach (var pile in pcs.AllPiles)
        {
            if (pile?.Cards == null) continue;
            foreach (var card in pile.Cards)
            {
                if (card == null) continue;
                string entry;
                try { entry = card.Id.Entry; } catch { continue; }
                if (entry == "PERFECTED_STRIKE") yield return card;
            }
        }
    }

    /// <summary>把池子的下降量从这些完美打击的伤害上减掉（最低 0）。</summary>
    private static void ApplyDrop(IEnumerable<CardModel> cards, int drop)
    {
        try
        {
            foreach (var card in cards)
            {
                try
                {
                    decimal cur = card.DynamicVars.Damage.BaseValue;
                    decimal next = System.Math.Max(0m, cur - drop);
                    card.DynamicVars.Damage.BaseValue = next;
                    Logger.Log($"[CharBoss] 痛击：完美打击伤害 {cur} → {next}（你打出打击，池子 −{drop}）");
                }
                catch { /* 忽略 */ }
            }
            try { CharacterBosses.UI.BossHandUi.RefreshForce(null); } catch { /* 刷新失败无所谓 */ }
        }
        catch { /* 忽略 */ }
    }

    private static int _logs;
}
