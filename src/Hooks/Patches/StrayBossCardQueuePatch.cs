using System;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Combat;
using CharacterBosses.Monsters;

namespace CharacterBosses.Patches;

/// <summary>
/// **Boss 打出的牌不要挂在原版「出牌队列」里不走**（用户 2026-10-29：「奇巧猎打出（打击）就停在空中，
/// 一直在那，直到我返回主菜单」）。
///
/// ## 根因（反编译 <c>NCardPlayQueue.OnActionEnqueued</c>）
/// 原版每收到一个 <c>PlayCardAction</c> 就给它建一个卡面节点放进 <c>NCardPlayQueue</c>：
/// <code>
/// if (LocalContext.IsMe(playCardAction.Player)) { …从玩家手牌里拿节点… }
/// else
/// {
///     var creatureNode = NCombatRoom.Instance.GetCreatureNode(playCardAction.Player.Creature);
///     var handler = creatureNode.PlayerIntentHandler;              // ← 怪物没有这个，或挂在奇怪的位置
///     var nCard = NCard.Create(cardModel);
///     nCard.GlobalPosition = handler.CardIntent.GlobalPosition + …;  // ← 于是卡面被摆在了屏幕中上方
///     nCard.Scale = Vector2.One * 0.25f;
///     AddChildSafely(nCard);  _playQueue.Add(item);  TweenCardToQueuePosition(item, …);
/// }
/// </code>
/// 这条「远程玩家正在出牌」的展示路径**默认用玩家手牌 / 玩家意图位**来定位与清理；
/// 我们的 Boss 是**影子 Player**（不在 <c>CombatState.Players</c> 里，也没有玩家手牌 UI），
/// 出牌走的是 <c>CardCmd.AutoPlay</c> ⇒ 卡面被建出来、被补间到队列位置之后，
/// **没有任何一步会把它收走**（收走那步属于「玩家出牌的动画流程」）⇒ 一张大卡永久吊在屏幕中上方。
///
/// ## 做法
/// 只做**最小、可自愈**的清理：每帧驱动（4Hz 慢车道）+ 每次我们的牌打完时各扫一遍
/// <c>NCardPlayQueue</c> 的**直接子节点**，凡是「卡面所属生物是<see cref="CharBossMonster"/>」的，
/// 直接隐藏（<c>Visible=false</c> + 全透明）。
///   · 只隐藏、**不 QueueFree**：队列内部还记着这个 item，后续执行阶段会按 item 找节点，
///     提前释放会撞上它的下标查找（`InvalidOperationException`）；
///   · 只认我们自己的 Boss（`Monster is CharBossMonster`）⇒ 真人玩家的牌面一个像素都不动。
/// </summary>
internal static class StrayBossCardQueuePatch
{
    private static readonly System.Collections.Generic.HashSet<string> Logged = new(StringComparer.Ordinal);
    private static int _removed;

    /// <summary>
    /// 暂停 <see cref="Reap"/>（「抉择，抉择」/「通晓万物」预见 在等玩家选牌期间置上）。
    /// 只影响这一个时间窗，其它时候照旧收她的卡面。
    ///
    /// ★ v0.2.837（玩家反馈「碎屑储的碰撞轨迹打出后卡在屏幕中间」）：
    ///   以前这是一个**裸 bool** —— 只要有一条路径「置上之后没走到置回」（`ScryGate.Release`
    ///   在玩家中途退局/失败/异常时没跑到，就是这种情况），**整场战斗直到结束都不再回收卡面**，
    ///   于是任何一张她的牌（比如 0 费的「碰撞轨迹」）就会永久吊在屏幕中上方。
    ///   现在改成**带自动过期**的开关：置上时只买 2 分钟，时间一到自动恢复回收（并打一行日志）。
    ///   正常选牌（玩家慢慢选）仍在 2 分钟内 ⇒ 不会被提前打断；真卡死了也不会把整场锁住。
    /// </summary>
    internal static bool HoldReap
    {
        get
        {
            long until = System.Threading.Volatile.Read(ref _holdUntilMs);
            if (until == 0) return false;
            if (System.Environment.TickCount64 < until) return true;

            // 过期：自动恢复（只打一次日志）
            System.Threading.Volatile.Write(ref _holdUntilMs, 0);
            if (_holdExpiredLogs++ < 3)
            {
                try
                {
                    Logger.Log("[CharBoss] 出牌队列：暂停回收已超过 2 分钟 ⇒ 自动恢复（避免整场不再收卡面）");
                }
                catch { /* 忽略 */ }
            }
            return false;
        }
        set => System.Threading.Volatile.Write(ref _holdUntilMs,
            value ? System.Environment.TickCount64 + 120_000L : 0L);
    }

    private static long _holdUntilMs;
    private static int _holdExpiredLogs;

    /// <summary>
    /// 「确认过是我们 Boss 的卡面」的登记表（v0.2.837）。
    /// 为什么要它：牌的 `Model` 在出牌结束后可能被清掉 ⇒ <see cref="IsOurBossCard"/> 返回 false
    /// ⇒ 那张卡面就**永远不会再被认出来**，于是永久留在屏幕上（玩家报的「碰撞轨迹卡在屏幕中间」）。
    /// 只要某一张节点被确认过一次是我方的，之后哪怕它的 Model 没了也照样回收。
    /// </summary>
    private static readonly System.Collections.Generic.HashSet<NCard> KnownOurs = new();

    /// <summary>卡面第一次被看到的时间（只在「已是我们的牌」时记录，用来判断真滞留）。</summary>
    private static readonly System.Collections.Generic.Dictionary<NCard, long> FirstSeenMs = new();

    /// <summary>
    /// 把「属于我们 Boss 的、挂在出牌队列里的卡面」**真的移出队列**（幂等，随便调）。
    ///
    /// ⚠ 2026-10-29 第二版（用户实测：多人「前两回合还好，第三回合明显变长」，
    /// 日志里「距我们上次回合收尾」= 594ms → 1094ms → 2578ms → 2625ms…**随回合数递增**）：
    /// 以前只是把卡面**藏起来**、item 仍留在 `NCardPlayQueue._playQueue` 里 ⇒
    /// 每回合每只 Boss 打 3~4 张牌就新积几份残留 ⇒ 队列越滚越长，
    /// 原版回合收尾那条「等队列/等动画」的流程就一回合比一回合慢。
    ///
    /// 现在改成用原版自己的接口 `RemoveCardFromQueueForCancellation(card, false)` 把它**真正摘掉**
    /// （内部会 `Kill` 补间、从 `_playQueue` 移除、并让那张卡淡出后 `QueueFree`）。
    /// 万一那个接口抛异常，退回「只隐藏」的旧做法，保证不会把界面搞崩。
    /// 另外给 `RemoveCardFromQueueForExecution` 挂了个「找不到就跳过」的前缀，
    /// 免得我们提前摘掉之后，原版执行阶段按 item 查找时抛 `InvalidOperationException`。
    /// </summary>
    internal static void Reap()
    {
        // ★ 2026-11-10（用户）：「抉择，抉择」正在**等真人玩家选牌**时，这张牌合法地挂在出牌队列上
        //   （和玩家自己打「生存者」等选择完全同一性质）。这段时间里一律**不动队列** ——
        //   慢车道 4Hz 会调到这里，而 `RemoveCardFromQueueForCancellation` 会把正在执行的卡面
        //   从队列里摘掉，等于把她这一手提前打断 ⇒ 表现为「我选慢一点，她的回合就结束了」。
        if (HoldReap) return;
        try
        {
            var queue = NCardPlayQueue.Instance;
            if (queue == null || !GodotObject.IsInstanceValid(queue)) return;

            var ours = new System.Collections.Generic.List<NCard>();
            CollectCards(queue, 0, ours);
            if (ours.Count == 0)
            {
                // 队列空 ⇒ 顺手把登记表里的死节点清掉（每场最多几百项，成本可忽略）
                PruneRegistry();
                return;
            }

            long nowMs = System.Environment.TickCount64;
            var reapNow = new System.Collections.Generic.List<NCard>();
            foreach (var card in ours)
            {
                // 认出来一次就永久记住（牌的 Model 出牌后可能被清掉，见 KnownOurs 的注释）
                if (IsOurBossCard(card)) KnownOurs.Add(card);

                bool mine = KnownOurs.Contains(card);
                if (!mine) continue;

                if (!FirstSeenMs.ContainsKey(card)) FirstSeenMs[card] = nowMs;
                long age = nowMs - FirstSeenMs[card];

                // ⚠ 仍然**立刻**摘（和以前一样）：这条队列路径是给「远程玩家正在出牌」用的，
                //   我们跟着把她的牌留在屏幕中上方反而更难看。这里唯一的变化是：
                //   「认清是谁的牌」不再只依赖 `Model`（出牌结束后 Model 可能被清掉 ⇒ 永远认不出来）。
                if (age >= 0) reapNow.Add(card);
            }
            if (reapNow.Count == 0) return;

            foreach (var card in reapNow)
            {
                if (!GodotObject.IsInstanceValid(card)) continue;
                string entry = "?";
                try { entry = card.Model?.Id.Entry ?? "?"; } catch { /* 忽略 */ }
                long ageMs = FirstSeenMs.TryGetValue(card, out long t0) ? nowMs - t0 : 0;

                FirstSeenMs.Remove(card);
                KnownOurs.Remove(card);

                try
                {
                    queue.RemoveCardFromQueueForCancellation(card, false);
                    if (_removed++ < 40)
                    {
                        Logger.Log($"[CharBoss] 出牌队列：{entry} 是 Boss 打出的牌、滞留 {ageMs}ms 没人收 ⇒ 已从队列里摘掉" +
                                   "（不摘的话它就会一直吊在屏幕中上方）");
                    }
                }
                catch (Exception e)
                {
                    // 退回「只隐藏」：至少屏幕上不留一张大卡
                    card.Visible = false;
                    card.Modulate = new Color(1f, 1f, 1f, 0f);
                    if (_removed++ < 40)
                        Logger.Log($"[CharBoss] 出牌队列：{entry} 摘除失败（改隐藏）: " + e.Message);
                }
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 出牌队列清理失败（忽略）: " + e.Message);
        }
    }

    /// <summary>把队列里的卡面节点收集起来（**递归**：有的卡面被塞在子容器里，只扫直接子节点会漏）。</summary>
    private static void CollectCards(Godot.Node node, int depth, System.Collections.Generic.List<NCard> into)
    {
        if (node == null || depth > 3) return;
        try
        {
            foreach (var child in node.GetChildren())
            {
                if (!GodotObject.IsInstanceValid(child)) continue;
                if (child is NCard card) { into.Add(card); continue; }
                CollectCards(child, depth + 1, into);
            }
        }
        catch { /* 忽略 */ }
    }

    /// <summary>清掉登记表里已经失效的节点与时间戳。</summary>
    private static void PruneRegistry()
    {
        try
        {
            if (KnownOurs.Count == 0 && FirstSeenMs.Count == 0) return;
            var dead = new System.Collections.Generic.List<NCard>();
            foreach (var c in KnownOurs) if (!GodotObject.IsInstanceValid(c)) dead.Add(c);
            foreach (var c in dead) { KnownOurs.Remove(c); FirstSeenMs.Remove(c); }
            var dead2 = new System.Collections.Generic.List<NCard>();
            foreach (var kv in FirstSeenMs) if (!GodotObject.IsInstanceValid(kv.Key)) dead2.Add(kv.Key);
            foreach (var c in dead2) FirstSeenMs.Remove(c);
        }
        catch { /* 忽略 */ }
    }

    /// <summary>
    /// **战斗结束之后**把出牌队列里剩下的**所有**卡面收干净（不分谁的牌）。
    ///
    /// ★ v0.2.295（用户 2026-11-02 本机实测：「搞了一堆剑柄打击+，最后 boss 都被揍死了还卡在屏幕中间」）：
    /// 那一条链是**玩家自己的**牌：被打 →「百年积木」在敌方回合抽牌 →「地狱狂徒」
    /// 把抽到的「剑柄打击+」自动打出 → 它又抽牌 → 又触发……（每张约 1 秒）。
    /// Boss 在链条中途就被打死 ⇒ 战斗结束，剩下那些卡面挂在 `NCardPlayQueue` 里没人收
    /// （原版「收卡面」那一步属于出牌动画流程，战斗一结束就不跑了；我们的 <see cref="Reap"/>
    /// 只收「她自己打出的牌」，所以这些玩家卡会留在屏幕中间）。
    ///
    /// 只在**战斗已结束**时调用（`CombatManager.IsInProgress == false`），此时队列里剩下的
    /// 一定是残留 ⇒ 全清最安全（战斗中绝不能这么干：玩家的牌面还在用）。
    /// </summary>
    internal static void ReapLeftoversAfterCombat()
    {
        try
        {
            // 战斗结束了 ⇒ 登记表/暂停标记一并清空（下一场重新开始记）
            try { KnownOurs.Clear(); FirstSeenMs.Clear(); System.Threading.Volatile.Write(ref _holdUntilMs, 0L); }
            catch { /* 忽略 */ }

            var queue = NCardPlayQueue.Instance;
            if (queue == null || !GodotObject.IsInstanceValid(queue)) return;

            int removed = 0, failed = 0;
            foreach (var child in queue.GetChildren())
            {
                if (child is not NCard card) continue;
                if (!GodotObject.IsInstanceValid(card)) continue;
                try
                {
                    queue.RemoveCardFromQueueForCancellation(card, false);
                    removed++;
                }
                catch
                {
                    try
                    {
                        card.Visible = false;
                        card.Modulate = new Color(1f, 1f, 1f, 0f);
                        failed++;
                    }
                    catch { /* 忽略 */ }
                }
            }

            if ((removed > 0 || failed > 0) && _afterCombatLogs++ < 5)
            {
                Logger.Log($"[CharBoss] 战斗结束后清理残留卡面：移除 {removed} 个、隐藏 {failed} 个" +
                           "（出牌链在打完之后被战斗结束截断时留下的）");
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 战斗结束后清理残留卡面失败（忽略）: " + e.Message);
        }
    }

    private static int _afterCombatLogs;

    /// <summary>这张卡面是不是「我们 Boss 打出的牌」。</summary>
    /// <c>internal</c>：出牌队列看门狗（<see cref="BossFrameTickPatch"/> 的 <c>WatchPlayQueue</c>）
    /// 也用同一个判据 —— 两边必须一致，否则会把真人玩家的牌当成我们 Boss 的牌报出去。
    /// </summary>
    internal static bool IsOurBossCard(NCard card)
    {
        try
        {
            var model = card.Model;
            if (model == null) return false;

            var creature = model.Owner?.Creature;
            return creature?.Monster is CharBossMonster;
        }
        catch { return false; }
    }
}

/// <summary>
/// `NCardPlayQueue.RemoveCardFromQueueForExecution(card)` 在队列里找不到那张牌时会
/// **抛 `InvalidOperationException`**。我们自己会把 Boss 的牌提前摘掉（见
/// <see cref="StrayBossCardQueuePatch"/>），所以这里加一道保护：
/// 找不到就直接跳过，绝不因为「我们已经收过了」而把原版的出牌流程炸掉。
/// </summary>
[HarmonyPatch(typeof(NCardPlayQueue), nameof(NCardPlayQueue.RemoveCardFromQueueForExecution))]
internal static class BossCardQueueNoThrowPatch
{
    [HarmonyPrefix]
    public static bool Prefix(NCardPlayQueue __instance, MegaCrit.Sts2.Core.Models.CardModel card)
    {
        try
        {
            if (__instance == null || card == null) return true;
            // ⚠ **只对我们自己的 Boss 出牌网开一面**（用户 2026-10-30：普通小怪战里杀怪时卡死）。
            //   以前是「只要队列里找不到这张牌就跳过原版」，等于替**所有**卡（含玩家卡、别的模组的卡）
            //   把原版流程吞掉 —— 原版那个抛异常其实也是流程的一部分，吞掉可能让某个等待方永远等不到。
            //   现在先确认这张牌的拥有者是我们的 Boss，否则一律走原版。
            if (card.Owner?.Creature?.Monster is not CharBossMonster) return true;
            if (__instance.GetCardNode(card) == null) return false;   // 已经被我们摘掉了
        }
        catch { /* 判断不了就走原版 */ }
        return true;
    }
}
