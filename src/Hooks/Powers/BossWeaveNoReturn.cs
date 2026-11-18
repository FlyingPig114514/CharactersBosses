#if STS2_111   // 观者 Boss 只在这个构建里
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using CharacterBosses.Core;

namespace CharacterBosses.Powers;

/// <summary>
/// **「迂回」不自动返手**（用户 2026-11-18：「拦掉迂回自己的返手，因为我已经在剧本里写好了」）。
///
/// ## 原版行为（反编译观者 A 版 `Watcher.dll`）
/// `WATCHER_WEAVE`（迂回，0 费攻击，伤害 4/升级 6）卡面第二行：
/// 「每当你**预见**时，把这张牌从弃牌堆放回手中。」
/// 对应实现是 `WatcherCombatHelper.OnScry(Player owner)` 的**后半段**：
/// <code>
/// if (owner.Creature.HasPower&lt;NirvanaPower&gt;())          // ① 涅槃：预见就给格挡
///     await CreatureCmd.GainBlock(owner.Creature, Nirvana 层数, ValueProp 4, null, false);
/// foreach (var c in PileType.Discard.GetPile(owner).Cards
///                      .Where(c => c is WatcherWeave))       // ② 迂回返手
///     await CardPileCmd.Add(c, PileType.Hand, CardPilePosition.Top, null, false);
/// </code>
///
/// ## 为什么要拦
/// 她的整条剧本（<see cref="WatcherForesightScript"/>）已经把「哪一回合打出哪一张迂回」逐张写好，
/// 而原版每预见一次就会把弃牌堆里**所有**迂回塞回她手上 ⇒ 回合中凭空多出 0 费攻击牌，
/// 手牌张数、能量账、玩家看到的「她这回合要打什么」全乱。
///
/// ## 怎么拦（只影响她那一边，真人玩家玩观者照旧）
/// 前缀挂 `WatcherCombatHelper.OnScry`：
/// · 不是她的影子玩家 ⇒ 原样跑；
/// · 是她的 ⇒ 先记下**弃牌堆里现有的迂回实例**，跑完原版那一段（涅槃的格挡留着，那是她自己那张牌该有的效果），
///   再把「刚才被拉回手牌的那些实例」逐个塞回弃牌堆 —— 按**实例**判定，所以剧本自己放进手牌的
///   那几张（`BossScript` 从抽牌堆/牌库造的）绝不会被误伤。
///
/// 钩子由 <see cref="BossOmnisciencePower.TryInstall"/> 顺手装上（只有「预见观」会带迂回）。
/// </summary>
internal static class BossWeaveNoReturn
{
    private const string WeaveTypeName = "WatcherWeave";

    private static bool _installed;
    private static int _attempts;
    private static int _logs;

    /// <summary>幂等安装。</summary>
    internal static void TryInstall()
    {
        if (_installed) return;
        _attempts++;
        try
        {
            var helper = WatcherCards.T("WatcherCombatHelper");
            var onScry = helper?.GetMethod("OnScry", BindingFlags.NonPublic | BindingFlags.Static);
            if (onScry == null)
            {
                if (_attempts <= 3) Logger.Log("[CharBoss] 迂回：暂时没找到观者的 OnScry 入口 ⇒ 稍后重试");
                return;
            }

            var prefix = new HarmonyMethod(typeof(BossWeaveNoReturn)
                .GetMethod(nameof(Prefix), BindingFlags.Static | BindingFlags.NonPublic)!);
            new Harmony("CharacterBosses.watcher.weave.noreturn").Patch(onScry, prefix: prefix);
            _installed = true;
            Logger.Log("[CharBoss] 迂回：已拦下「预见时从弃牌堆返回手牌」（她的剧本自己安排每一张迂回）");
        }
        catch (Exception e) { Logger.Log("[CharBoss] 迂回：挂钩失败: " + e.Message); }
    }

    // ── 钩子 ───────────────────────────────────────────────────────────────
    private static bool Prefix(Player owner, MethodBase __originalMethod, ref Task __result)
    {
        try
        {
            if (!IsBossShadow(owner)) return true;      // 真人玩家的观者 ⇒ 原版行为
            var before = WeavesInDiscard(owner);
            var original = (Task?)__originalMethod.Invoke(null, new object?[] { owner });
            if (original == null) return true;          // 反射失败就退回原版
            __result = before.Count == 0
                ? original                              // 弃牌堆里没有迂回 ⇒ 只需要跑原版
                : StripAsync(original, owner, new HashSet<CardModel>(before));
            return false;
        }
        catch (Exception e)
        {
            if (_logs++ < 6) Logger.Log("[CharBoss] 迂回：拦返手时出错（这次放行原版）: " + e.Message);
            return true;
        }
    }

    /// <summary>等原版那一段跑完（涅槃的格挡也在这段里），再把「被拉回手牌的迂回」放回弃牌堆。</summary>
    private static async Task StripAsync(Task original, Player owner, HashSet<CardModel> returned)
    {
        try { await original; } catch { /* 原版自己报错与我们无关 */ }
        try
        {
            var pcs = owner.PlayerCombatState;
            if (pcs == null) return;
            foreach (var card in new List<CardModel>(pcs.Hand.Cards))
            {
                if (card == null || !returned.Contains(card)) continue;   // 剧本自己放进手牌的不动
                try
                {
                    pcs.Hand.RemoveInternal(card, true);
                    pcs.DiscardPile.AddInternal(card, -1, true);
                    if (_logs++ < 12)
                        Logger.Log($"[CharBoss] 迂回：预见把它从弃牌堆拉回了手牌 ⇒ 再丢回去（她的剧本自己安排）");
                }
                catch (Exception e)
                {
                    if (_logs++ < 12) Logger.Log("[CharBoss] 迂回：丢回弃牌堆失败: " + e.Message);
                }
            }
        }
        catch { /* 绝不影响战斗 */ }
    }

    // ── 小工具 ─────────────────────────────────────────────────────────────
    private static List<CardModel> WeavesInDiscard(Player owner)
    {
        var list = new List<CardModel>();
        try
        {
            var pcs = owner.PlayerCombatState;
            if (pcs == null) return list;
            foreach (var card in pcs.DiscardPile.Cards)
                if (IsWeave(card)) list.Add(card);
        }
        catch { /* 忽略 */ }
        return list;
    }

    private static bool IsWeave(CardModel? card)
    {
        try { return card != null && card.GetType().Name == WeaveTypeName; }
        catch { return false; }
    }

    /// <summary>「这位玩家就是她（我们 Boss 的影子玩家）」。</summary>
    private static bool IsBossShadow(Player? player)
    {
        if (player == null) return false;
        try
        {
            var enemies = player.Creature?.CombatState?.Enemies;
            if (enemies == null) return false;
            foreach (var e in enemies)
                if (e?.Monster is Monsters.CharBossMonster b && ReferenceEquals(b.Shadow, player)) return true;
        }
        catch { /* 忽略 */ }
        return false;
    }
}
#endif
