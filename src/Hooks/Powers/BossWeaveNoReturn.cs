#if STS2_111   // 观者 Boss 只在这个构建里
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
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
/// 实现在 `WatcherCombatHelper.OnScry(Player owner)` 的后半段：
/// <code>
/// if (owner.Creature.HasPower&lt;NirvanaPower&gt;())          // ① 涅槃：预见就给格挡
///     await CreatureCmd.GainBlock(owner.Creature, Nirvana 层数, ValueProp 4, null, false);
/// foreach (var c in PileType.Discard.GetPile(owner).Cards
///                      .Where(c => c is WatcherWeave))       // ② 弃牌堆里所有迂回 → 手牌
///     await CardPileCmd.Add(c, PileType.Hand, CardPilePosition.Top, null, false);
/// </code>
///
/// ## 为什么要拦
/// 她的整条剧本（<see cref="WatcherForesightScript"/>）已经把「哪一回合打出哪一张迂回」逐张写好，
/// 而原版每预见一次就把弃牌堆里**所有**迂回塞回她手上 ⇒ 回合中凭空多出 0 费攻击牌，
/// 手牌张数、能量账、玩家看到的「她这回合要打什么」全乱。
///
/// ## 怎么拦（用户 2026-11-18 订正后的做法）
/// 用户：「被预见丢弃的牌是直接从抽牌堆到弃牌堆，**不需要展示在手牌里**」
/// ⇒ **不能**「先让它进手牌、再把它扔回去」（那样她的手牌栏会闪一下多一张，而且一进一出都是真实牌堆操作）。
/// 现在改成**让它根本回不去**：
/// · 前缀挂公开入口 `WatcherCombatHelper.Scry` / `ScryAutoDiscard`（都返回 `Task&lt;List&lt;CardModel&gt;&gt;`）——
///   这次预见是**她**发起的时候，先把弃牌堆里的迂回**收起来**（`DiscardPile.RemoveInternal`），
///   于是原版那半段的 `Where(c =&gt; c is WatcherWeave)` 扫到 0 张、一张都不会进手牌；
/// · 后缀把返回的 Task 包一层：原版整段跑完（涅槃的格挡照旧保留）之后，再把这些迂回**放回弃牌堆**。
///
/// ⚠ v0.2.750 的第一版用「前缀里反射调用原方法 + 包 Task」，反射拿到的仍是被 Harmony 替换过的入口
/// ⇒ 无限递归 ⇒ 她打「天眼」时进程级 StackOverflow（v0.2.751 已重写，这里不再调用原方法）。
///
/// 钩子由 <see cref="BossOmnisciencePower.TryInstall"/> 顺手装上（只有「预见观」会带迂回）。
/// </summary>
internal static class BossWeaveNoReturn
{
    private const string WeaveTypeName = "WatcherWeave";

    private static bool _installed;
    private static int _attempts;
    private static int _logs;

    /// <summary>这次预见期间被临时收起来的迂回（跑完原版就放回弃牌堆）。</summary>
    private static readonly List<Frame> _frames = new();

    private sealed class Frame
    {
        public Player Owner = null!;
        public List<CardModel> Stashed = new();
    }

    /// <summary>幂等安装。</summary>
    internal static void TryInstall()
    {
        if (_installed) return;
        _attempts++;
        try
        {
            var helper = WatcherCards.T("WatcherCombatHelper");
            if (helper == null)
            {
                if (_attempts <= 3) Logger.Log("[CharBoss] 迂回：暂时没找到观者的 WatcherCombatHelper ⇒ 稍后重试");
                return;
            }

            var prefix = new HarmonyMethod(typeof(BossWeaveNoReturn)
                .GetMethod(nameof(ScryPrefix), BindingFlags.Static | BindingFlags.NonPublic)!);
            var postfix = new HarmonyMethod(typeof(BossWeaveNoReturn)
                .GetMethod(nameof(ScryPostfix), BindingFlags.Static | BindingFlags.NonPublic)!);

            bool any = false;
            foreach (var name in new[] { "Scry", "ScryAutoDiscard" })
            {
                var m = helper.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (m == null) continue;
                try
                {
                    new Harmony("CharacterBosses.watcher.weave.noreturn." + name)
                        .Patch(m, prefix: prefix, postfix: postfix);
                    any = true;
                }
                catch (Exception e) { Logger.Log($"[CharBoss] 迂回：{name} 挂载失败: {e.Message}"); }
            }

            // ② **B 版（lamali）**：它把「预见就回手」写在**卡片自己**身上 ——
            //   `Watcher.Code.Cards.Uncommon.Weave.AfterScryed(ctx, player, scryAmount, discardAmount, seen, discarded)`。
            //   Scry 那条入口拦不到它（用户 2026-11-18 实测：「B 版观者的迂回回手没有拦住」），
            //   所以这里直接把这个方法前缀掉：是她（影子玩家）就整条跳过。
            try
            {
                var weave = WatcherCards.Weave;
                var after = weave?.GetMethod("AfterScryed",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (after != null)
                {
                    var pre = new HarmonyMethod(typeof(BossWeaveNoReturn)
                        .GetMethod(nameof(WeaveAfterScryedPrefix), BindingFlags.Static | BindingFlags.NonPublic)!);
                    new Harmony("CharacterBosses.watcher.weave.noreturn.afterscryed").Patch(after, prefix: pre);
                    any = true;
                }
            }
            catch (Exception e) { Logger.Log("[CharBoss] 迂回：B 版 AfterScryed 挂载失败: " + e.Message); }

            if (!any)
            {
                if (_attempts <= 3) Logger.Log("[CharBoss] 迂回：观者的 Scry / ScryAutoDiscard / Weave.AfterScryed 都还没找到 ⇒ 稍后重试");
                return;
            }
            _installed = true;
            Logger.Log("[CharBoss] 迂回：已拦下「预见时从弃牌堆返回手牌」（她的剧本自己安排每一张迂回，且不会进手牌）");
        }
        catch (Exception e) { Logger.Log("[CharBoss] 迂回：挂钩失败: " + e.Message); }
    }

    /// <summary>**B 版的迂回**（`Weave.AfterScryed`）：是她打出去的这张就不回手（A 版走下面的 Scry 那条）。</summary>
    private static bool WeaveAfterScryedPrefix(Player player)
    {
        try
        {
            if (!IsBossShadow(player)) return true;      // 真人玩家的观者 ⇒ 原版行为
            if (_logs++ < 12)
                Logger.Log("[CharBoss] 迂回（B 版）：她的 Weave.AfterScryed 已拦下 ⇒ 不返手");
            return false;
        }
        catch { return true; }
    }
    // ── 前缀：先把弃牌堆里的迂回收起来，让原版那张「扫 discard 找 Weave」扫到空 ──
    private static void ScryPrefix(Player owner)
    {
        try
        {
            if (!IsBossShadow(owner)) return;
            var pcs = owner.PlayerCombatState;
            if (pcs == null) return;

            var stash = new List<CardModel>();
            foreach (var card in new List<CardModel>(pcs.DiscardPile.Cards))
            {
                if (!IsWeave(card)) continue;
                pcs.DiscardPile.RemoveInternal(card, true);
                stash.Add(card);
            }
            if (stash.Count == 0) return;
            _frames.Add(new Frame { Owner = owner, Stashed = stash });
            if (_logs++ < 12)
                Logger.Log($"[CharBoss] 迂回：这次预见先把她弃牌堆里的 {stash.Count} 张迂回收起来" +
                           "（预见本来就不该让它返到手牌）");
        }
        catch (Exception e)
        {
            if (_logs++ < 12) Logger.Log("[CharBoss] 迂回：收起来那一步失败（这次放行原版）: " + e.Message);
        }
    }

    // ── 后缀：原版跑完（涅槃格挡照旧）再把迂回放回弃牌堆 ──────────────────
    private static void ScryPostfix(Player owner, ref Task<List<CardModel>> __result)
    {
        try
        {
            var frame = TakeFrame(owner);
            if (frame == null || __result == null) return;
            __result = RestoreAfter(__result, frame);
        }
        catch (Exception e)
        {
            if (_logs++ < 6) Logger.Log("[CharBoss] 迂回：包 Task 失败（立刻放回）: " + e.Message);
            RestoreFrame(_lastTakeForEmergency);
        }
    }

    private static Frame? _lastTakeForEmergency;

    private static Frame? TakeFrame(Player? owner)
    {
        for (int i = _frames.Count - 1; i >= 0; i--)
        {
            if (!ReferenceEquals(_frames[i].Owner, owner)) continue;
            var f = _frames[i];
            _frames.RemoveAt(i);
            _lastTakeForEmergency = f;
            return f;
        }
        return null;
    }

    private static async Task<List<CardModel>> RestoreAfter(Task<List<CardModel>> original, Frame frame)
    {
        try { return await original; }
        finally { RestoreFrame(frame); }
    }

    private static void RestoreFrame(Frame? frame)
    {
        if (frame == null) return;
        try
        {
            var pcs = frame.Owner?.PlayerCombatState;
            if (pcs == null) return;
            foreach (var card in frame.Stashed)
            {
                if (card == null) continue;
                if (pcs.DiscardPile.Cards.Contains(card)) continue;   // 已经被别处放回去了
                pcs.DiscardPile.AddInternal(card, -1, true);
            }
        }
        catch (Exception e)
        {
            if (_logs++ < 12) Logger.Log("[CharBoss] 迂回：放回弃牌堆失败（不影响战斗）: " + e.Message);
        }
    }

    // ── 小工具 ─────────────────────────────────────────────────────────────
    private static bool IsWeave(CardModel? card)
    {
        try
        {
            if (card == null) return false;
            // A 版类名 `WatcherMod.WatcherWeave`、B 版 `Watcher.Code.Cards.Uncommon.Weave`
            // ⇒ 优先按「观者 mod 当前 provider 解析出来的那个类型」比，再退回类名兜底。
            var resolved = WatcherCards.Weave;
            if (resolved != null && card.GetType() == resolved) return true;
            string name = card.GetType().Name;
            return name == WeaveTypeName || name.EndsWith("Weave", StringComparison.Ordinal);
        }
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
