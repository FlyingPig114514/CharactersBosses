#if STS2_111   // 观者 Boss 只在这个构建里
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
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
/// ## 怎么拦（⚠ 这一版是**重写**，见下）
/// v0.2.750 的第一版是「前缀挂 `WatcherCombatHelper.OnScry`，用反射把原方法调回来、再包一层 Task」——
/// **实测她打「天眼」时游戏直接崩溃**（09:27:32，日志停在 `chose cards` 之后、没有任何异常信息 ⇒
/// 反射回原方法落进了被 Harmony 替换过的入口，无限递归 → StackOverflow 直接杀进程）。
///
/// 现在改成**绝不去调原方法**：前缀挂公开入口
/// `WatcherCombatHelper.Scry` / `ScryAutoDiscard`（两者都返回 `Task&lt;List&lt;CardModel&gt;&gt;`），
/// · 前缀：如果这次预见是她（影子玩家）发起的，记下此刻**弃牌堆里的迂回实例**；
/// · 后缀：把返回的 Task 包一层 —— 原版整段跑完（涅槃的格挡照旧）之后，
///   只把「刚被拉回手牌的那几个实例」用**正规牌堆命令** `CardPileCmd.Add(…, PileType.Discard, …)`
///   放回弃牌堆。按实例判定 ⇒ 剧本自己放进手牌的那几张绝不会被误伤。
///
/// 钩子由 <see cref="BossOmnisciencePower.TryInstall"/> 顺手装上（只有「预见观」会带迂回）。
/// </summary>
internal static class BossWeaveNoReturn
{
    private const string WeaveTypeName = "WatcherWeave";

    private static bool _installed;
    private static int _attempts;
    private static int _logs;

    /// <summary>前缀拍下的「这次预见开始前，她弃牌堆里的迂回」，后缀用它来判定谁该退回去。</summary>
    private static readonly List<Frame> _frames = new();

    private sealed class Frame
    {
        public Player Owner = null!;
        public HashSet<CardModel> DiscardWeaves = new();
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

            if (!any)
            {
                if (_attempts <= 3) Logger.Log("[CharBoss] 迂回：观者的 Scry / ScryAutoDiscard 都还没找到 ⇒ 稍后重试");
                return;
            }
            _installed = true;
            Logger.Log("[CharBoss] 迂回：已拦下「预见时从弃牌堆返回手牌」（她的剧本自己安排每一张迂回）");
        }
        catch (Exception e) { Logger.Log("[CharBoss] 迂回：挂钩失败: " + e.Message); }
    }

    // ── 前缀：拍下弃牌堆里现有的迂回 ──────────────────────────────────────
    private static void ScryPrefix(Player owner)
    {
        try
        {
            if (!IsBossShadow(owner)) return;
            var frame = new Frame { Owner = owner };
            var pcs = owner.PlayerCombatState;
            if (pcs != null)
                foreach (var card in pcs.DiscardPile.Cards)
                    if (IsWeave(card)) frame.DiscardWeaves.Add(card);
            if (frame.DiscardWeaves.Count == 0) return;      // 没迂回 ⇒ 什么都不用做（也不占栈）
            _frames.Add(frame);
        }
        catch { /* 绝不影响战斗 */ }
    }

    // ── 后缀：把返回的 Task 包一层，跑完再清 ──────────────────────────────
    private static void ScryPostfix(Player owner, ref Task<List<CardModel>> __result)
    {
        try
        {
            var frame = TakeFrame(owner);
            if (frame == null || __result == null) return;
            __result = StripAfter(__result, frame);
        }
        catch (Exception e)
        {
            if (_logs++ < 6) Logger.Log("[CharBoss] 迂回：包 Task 失败（这次放行原版）: " + e.Message);
        }
    }

    private static Frame? TakeFrame(Player? owner)
    {
        for (int i = _frames.Count - 1; i >= 0; i--)
        {
            if (!ReferenceEquals(_frames[i].Owner, owner)) continue;
            var f = _frames[i];
            _frames.RemoveAt(i);
            return f;
        }
        return null;
    }

    /// <summary>等原版整段跑完（涅槃的格挡也在这段里），再把「被拉回手牌的迂回」用正规指令放回弃牌堆。</summary>
    private static async Task<List<CardModel>> StripAfter(Task<List<CardModel>> original, Frame frame)
    {
        var result = await original;
        try
        {
            var pcs = frame.Owner.PlayerCombatState;
            if (pcs == null) return result;
            foreach (var card in new List<CardModel>(pcs.Hand.Cards))
            {
                if (card == null || !frame.DiscardWeaves.Contains(card)) continue;   // 剧本自己放进手牌的不动
                await CardPileCmd.Add(card, PileType.Discard, CardPilePosition.Random, null, false);
                if (_logs++ < 12)
                    Logger.Log("[CharBoss] 迂回：预见把它从弃牌堆拉回了手牌 ⇒ 再丢回去（她的剧本自己安排）");
            }
        }
        catch (Exception e)
        {
            if (_logs++ < 12) Logger.Log("[CharBoss] 迂回：丢回弃牌堆失败（不影响战斗）: " + e.Message);
        }
        return result;
    }

    // ── 小工具 ─────────────────────────────────────────────────────────────
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
