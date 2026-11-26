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

    // ── 「卡面吊在屏幕中间」的第二条路：全房间扫（v0.2.852）──────────────────
    //
    // 用户 2026-11-21 截图：「碎屑储打出的碰撞轨迹还是会卡在天上」，那张大卡停在战场中央、
    // 既不在原版出牌队列里（`Reap` 一次都没摘到过东西，日志里没有一条「已从队列里摘掉」，看门狗也没报），
    // 也不是我们自己的飞牌动画（`出牌动画` 那行一次都没出现 ⇒ 斗蛐蛐没装、`Fly` 直接收走了）。
    // ⇒ 说明它是**原版**为「非本机玩家出牌」建的卡面，只是挂在 `NCombatRoom` 的某个容器里，
    //   而 `Reap` 只扫 `NCardPlayQueue` ⇒ 谁也管不到它。
    //
    // 所以补一条**房间级**的兜底扫描（1Hz，只在我们的 Boss 战里跑）：
    //   · 跳过 `NCardPlayQueue` 子树（那是 `Reap` 的地盘，它有自己的摘除流程）与任何名字带 `Hand` 的子树
    //     （真人的手牌，一个像素都不动）；
    //   · 只盯「认得出是我们的牌」或「Model 已经被原版清掉」的卡面（出牌结束后原版会清 Model）；
    //   · 滞留 ≥2.5 秒 ⇒ 打一行带**节点路径**的日志（下次能直接看出是哪个容器漏了），然后隐藏它。
    //     隐藏而不是释放：那个容器可能还记着这个节点，提前 QueueFree 更危险。
    private static readonly System.Collections.Generic.Dictionary<GodotObject, long> _roomSeen = new();
    private static readonly System.Collections.Generic.HashSet<string> _roomLogged = new(StringComparer.Ordinal);
    private static long _lastRoomSweepUsec;

    /// <summary>房间级兜底扫描：滞留在 `NCombatRoom` 里的我方卡面（1Hz，幂等）。</summary>
    internal static void SweepRoom(long nowUsec)
    {
        if (nowUsec - _lastRoomSweepUsec < 1_000_000L) return;
        _lastRoomSweepUsec = nowUsec;

        try
        {
            // 只在**我们的 Boss 战**里扫（别的模组的战斗/幕一律不碰，和 `WatchPlayQueue` 同一个门）。
            if (!Core.BossShadowPets.BossFightActive(
                    MegaCrit.Sts2.Core.Combat.CombatManager.Instance?.DebugOnlyGetState()))
            {
                _roomSeen.Clear();
                return;
            }

            var room = MegaCrit.Sts2.Core.Nodes.Rooms.NCombatRoom.Instance;
            if (room == null || !GodotObject.IsInstanceValid(room)) { _roomSeen.Clear(); return; }

            var found = new System.Collections.Generic.List<NCard>();
            var paths = new System.Collections.Generic.Dictionary<NCard, string>();
            CollectRoomCards(room, 0, found, paths);

            var alive = new System.Collections.Generic.HashSet<GodotObject>();
            foreach (var card in found)
            {
                if (!GodotObject.IsInstanceValid(card)) continue;
                alive.Add(card);

                bool ours = false;
                try { ours = IsOurBossCard(card) || KnownOurs.Contains(card); } catch { /* 忽略 */ }
                bool modelGone = false;
                try { modelGone = card.Model == null; } catch { modelGone = true; }
                if (!ours && !modelGone) continue;
                if (ours) KnownOurs.Add(card);

                if (!_roomSeen.TryGetValue(card, out long since)) { _roomSeen[card] = nowUsec; continue; }
                long age = nowUsec - since;
                if (age < 2_500_000L) continue;

                string entry = "?";
                try { entry = card.Model?.Id.Entry ?? "<Model 已清>"; } catch { /* 忽略 */ }
                string path = paths.TryGetValue(card, out string? p) ? p : card.GetPath().ToString();
                if (_roomLogged.Count < 15 && _roomLogged.Add(path + "#" + entry))
                {
                    Logger.Log($"[CharBoss] ⚠ 卡面滞留：{entry} 在 {path} 已 {(age / 1_000_000.0):0.0}s" +
                               $"（scale={card.Scale} pos={card.GlobalPosition}）" +
                               "。这一行的路径就是原版把它挂在哪儿了（**只看不动手**：v0.2.854 起这里只报告，" +
                               "真正的回收交给 `PlayContainer` 直接子节点那条路，免得误伤我们自己的手牌栏）");
                    // ★ v0.2.968（用户 2026-11-26：「如果没什么影响，做吧」）：
                    //   把**装着这张滞留卡的容器**整棵子树 dump 一遍 —— 玩家反馈的卡死里，
                    //   有一张打完之后 Model 已被清空的玩家卡面（WAH_BLAST）挂在 `CombatUi/@Control@61594`
                    //   这种匿名容器里，只看那一行路径根本不知道是谁把它丢在那儿的。
                    //   这里补一行「父节点类型/名字 + 最多 12 个子节点（含各自的 Model）」，
                    //   下次就能一眼看出是哪个模组造的容器、里面还压着什么。
                    Logger.Log($"[CharBoss]     └ 容器：{DescribeContainer(card.GetParent())}");
                    Logger.Log($"[CharBoss]     └ 这张卡：Model={(card.Model == null ? "<已清>" : entry)}" +
                               $" Visible={card.Visible} 子节点={card.GetChildCount()}" +
                               $" 在树里={card.IsInsideTree()}");
                }
                // ★ v0.2.854：**只报告、不隐藏**。上一版在这里直接隐藏，
                //   结果把她手牌栏里的 5 张卡面（正好是整手牌）当滞留卡面隐藏了
                //   ⇒ 玩家看到「一提起卡牌，她的意图/卡面就没了」。
                _roomSeen[card] = nowUsec + 10_000_000L;   // 报过就别反复报
            }

            if (_roomSeen.Count > alive.Count)
            {
                var gone = new System.Collections.Generic.List<GodotObject>();
                foreach (var kv in _roomSeen) if (!alive.Contains(kv.Key)) gone.Add(kv.Key);
                foreach (var g in gone) _roomSeen.Remove(g);
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 房间卡面扫描失败（忽略）: " + e.Message);
        }
    }

    /// <summary>递归收集房间里的卡面（跳过出牌队列与任何手牌容器）。</summary>
    private static void CollectRoomCards(Godot.Node node, int depth,
        System.Collections.Generic.List<NCard> into,
        System.Collections.Generic.Dictionary<NCard, string> paths)
    {
        if (node == null || depth > 6) return;
        try
        {
            foreach (var child in node.GetChildren())
            {
                if (!GodotObject.IsInstanceValid(child)) continue;
                string name = child.Name.ToString();
                if (name.Contains("Hand", StringComparison.OrdinalIgnoreCase)) continue;   // 真人手牌
                // ★ v0.2.854：`PlayContainer` 子树整段跳过 —— 我们自己的手牌栏面板就挂在它底下
                //   （`UI/BossHudLayer.Resolve`），由 `ClearOurCardsFromPlayContainer` 只对它的**直接子节点**动手。
                if (name == "PlayContainer") continue;
                if (child is NCardPlayQueue) continue;                                    // Reap 的地盘
                if (child is NCard card)
                {
                    into.Add(card);
                    if (!paths.ContainsKey(card)) paths[card] = card.GetPath().ToString();
                    continue;
                }
                CollectRoomCards(child, depth + 1, into, paths);
            }
        }
        catch { /* 忽略 */ }
    }

    // ── 真正卡住那张牌的容器：`NCombatRoom/Ui/PlayContainer`（v0.2.853）────────────
    //
    // 反编译找到了原版把**打出去的卡面**放在哪儿（`NCombatUi`）：
    // <code>
    // public void AddToPlayContainer(NCard card) { PlayContainer.AddChildSafely(card); }
    // private IEnumerable&lt;NCard&gt; PlayContainerCards =&gt; PlayContainer.GetChildren().OfType&lt;NCard&gt;();
    // </code>
    // 牌在结算期间 `Pile.Type == Play`，**离开 play 堆那一步才把它从展示区收走** ——
    // 而我们的 Boss 是影子玩家（`CardCmd.AutoPlay`），那条「离开 play 堆 ⇒ 收走卡面」的流程
    // 是替**本机玩家的出牌**准备的，不替她跑 ⇒ 卡面永久留在战场中央那张大卡的位置。
    // 这正好解释了玩家 2026-11-21 的截图：那张「碰撞轨迹」既不在出牌队列（`Reap` 一次都没摘到）、
    // 也不是我们的飞牌动画（`BossHandPlayFx` 没装斗蛐蛐时直接收走）。
    //
    // 多人比单人更容易看到：多人时每台机器上她的每一手都是「**别人**打的牌」，
    // 展示区的接手/收尾全在本机玩家的出牌流程里 ⇒ 没人收；单人时同一张牌常常赶上本机路径被原版收掉。
    private static readonly System.Collections.Generic.Dictionary<GodotObject, long> _pcSeen = new();
    private static long _lastPcSweepMs;
    private static int _pcLogs;

    private static Control? PlayContainerOrNull()
    {
        try
        {
            var container = MegaCrit.Sts2.Core.Nodes.Rooms.NCombatRoom.Instance?.Ui?.PlayContainer;
            if (container == null || !GodotObject.IsInstanceValid(container) || !container.IsInsideTree()) return null;
            return container;
        }
        catch { return null; }
    }

    /// <summary>
    /// <summary>
    /// 这张卡面上是否还挂着**原版自己的出牌特效**（`NCardFlyPowerVfx` / `NCardTrailVfx`）。
    ///
    /// ★ v0.2.873（玩家 2026-11-22 给的 `godot (34).log` + `mod_log (55).txt`，两边时间戳一一对上）：
    ///   打**能力牌**时原版会建一个 `NCardFlyPowerVfx`（卡面飞向能力栏），它**一直持有这个 `NCard` 节点**，
    ///   动画结束前谁都不能动它。而我们 v0.2.853 的「她开始打下一张牌 ⇒ 立刻把上一张从 `PlayContainer` 收掉」
    ///   正好在动画进行中 `QueueFree` 了它 ⇒
    ///     · `NCardFlyPowerVfx.PlayAnim()` 对着已释放的节点抛 `ObjectDisposedException`；
    ///     · 它挂的拖尾节点 `NCardTrailVfx` 还活着，**每帧**再去读那张卡 ⇒ 每帧一条异常，
    ///       玩家那边一小时写出 **350 MB ~ 1.6 GB** 的 `godot.log`（实测 1,008,114 次）。
    ///   证据：日志里 `出牌展示区：SPEEDSTER 打出后没人从 PlayContainer 收走⇒ 我们自己收掉（她开始打下一张牌）`
    ///   紧跟着就是 `NCardFlyPowerVfx.PlayAnim` 的 ObjectDisposedException，之后整段全是 `NCardTrailVfx` 刷屏。
    ///
    /// ⇒ 只要卡面上还挂着这类特效节点，就**当它还有主**，这一轮什么都不做（不摘、不隐藏、不释放）。
    /// 按**类名**匹配而不是按类型：这两个类型在 0.107 里不一定存在，写死类型会编不过。
    /// </summary>
    private static bool HasVanillaCardVfx(Node? card, int depth = 0)
    {
        if (card == null || depth > 3) return false;
        try
        {
            if (!GodotObject.IsInstanceValid(card)) return false;
            foreach (var child in card.GetChildren())
            {
                if (child == null || !GodotObject.IsInstanceValid(child)) continue;
                string name = child.GetType().Name;
                if (name.Contains("NCardFlyPowerVfx", StringComparison.Ordinal)
                    || name.Contains("NCardTrailVfx", StringComparison.Ordinal)
                    || name.Contains("NCardFlyVfx", StringComparison.Ordinal))
                {
                    return true;
                }
                if (HasVanillaCardVfx(child, depth + 1)) return true;
            }
        }
        catch { /* 忽略：判断不出来就当没挂（下面还有年龄兜底） */ }
        return false;
    }

    /// <summary>
    /// 这张卡是不是**能力牌**（`CardType.Power`）—— 原版会为它播「飞向能力栏」那段动画，
    /// 所以它天然会有一段时间「合法地」留在 `PlayContainer` 里（见 <see cref="HasVanillaCardVfx"/>）。
    /// </summary>
    private static bool IsPowerCard(NCard card)
    {
        try { return card.Model?.Type == MegaCrit.Sts2.Core.Entities.Cards.CardType.Power; }
        catch { return false; }
    }

    /// <summary>
    /// 把展示区（`PlayContainer`）里**我们 Boss 打出的**卡面收掉。
    /// <paramref name="minAgeMs"/> = 0 表示立刻全收（她开始打下一张牌时用这个）；
    /// 大于 0 时只收「已经停留超过这么久」的（1Hz 兜底用，不会打断正在播的动画）。
    /// </summary>
    internal static void ClearOurCardsFromPlayContainer(string why, long minAgeMs = 0)
    {
        var container = PlayContainerOrNull();
        if (container == null) { _pcSeen.Clear(); return; }

        long now = System.Environment.TickCount64;
        try
        {
            // ⚠⚠ v0.2.854（用户 2026-11-21：「我一提起卡牌储君的意图就消失了」——是我们上一版的锅）：
            //   原版 `AddToPlayContainer` 挂进去的「打出去的卡面」是 `PlayContainer` 的**直接子节点**，
            //   而**我们自己的手牌栏面板（含意图行）也挂在 `PlayContainer` 底下**（见 `UI/BossHudLayer.Resolve`：
            //   战斗 UI 可见时父节点就是 `%PlayContainer`）⇒ v0.2.852/853 的递归扫描把**她手里那 5 张牌的卡面**
            //   当成了滞留卡面（日志实证：`⚠ 卡面滞留：HIDDEN_CACHE/DEFEND_REGENT/COLLISION_COURSE/…` 5 张，
            //   正好是她第 1 回合的整手牌，路径是 `…/PlayContainer/@Control@4493/@Control@4519/Card`）
            //   然后隐藏/回收 ⇒ 手牌栏的卡面（连带那一排意图的位置）就没了。
            //   ⇒ 现在**只看 `PlayContainer` 的直接子节点里的 `NCard`**（原版契约），我们的面板在更深的层级，碰不到。
            foreach (var child in container.GetChildren())
            {
                if (child is not NCard card) continue;   // ← 只认直接子节点（v0.2.854）
                if (!GodotObject.IsInstanceValid(card)) continue;

                bool ours = false;
                try { ours = IsOurBossCard(card) || KnownOurs.Contains(card); } catch { /* 忽略 */ }
                if (!ours) continue;
                KnownOurs.Add(card);

                if (!_pcSeen.TryGetValue(card, out long since)) { _pcSeen[card] = now; since = now; }
                long age = now - since;
                if (minAgeMs > 0 && age < minAgeMs) continue;

                // ★ v0.2.873：两条「**这张卡面还有主，别碰**」的判据（详见 HasVanillaCardVfx 注释）：
                //   ① 上面还挂着原版的飞牌/拖尾特效 ⇒ 动画还没完，谁动谁炸（每帧一条异常、日志暴涨）；
                //   ② 能力牌在动画期间本来就会留在展示区里，给它 3 秒冷静期，之后还在才算真滞留。
                if (HasVanillaCardVfx(card)) continue;
                if (IsPowerCard(card) && age < 3_000L) continue;

                string entry = "?";
                try { entry = card.Model?.Id.Entry ?? "?"; } catch { /* 忽略 */ }
                _pcSeen.Remove(card);

                try
                {
                    container.RemoveChild(card);
                    card.QueueFree();
                    if (_pcLogs++ < 30)
                    {
                        Logger.Log($"[CharBoss] 出牌展示区：{entry} 打出后没人从 PlayContainer 收走" +
                                   $"⇒ 我们自己收掉（{why}）");
                    }
                }
                catch (Exception e)
                {
                    try
                    {
                        card.Visible = false;
                        card.Modulate = new Color(1f, 1f, 1f, 0f);
                    }
                    catch { /* 忽略 */ }
                    if (_pcLogs++ < 30)
                        Logger.Log($"[CharBoss] 出牌展示区：{entry} 移除失败（已隐藏）: " + e.Message);
                }
            }

            // 已经不在展示区的记账项清掉
            if (_pcSeen.Count > 0)
            {
                var alive = new System.Collections.Generic.HashSet<GodotObject>();
                foreach (var child in container.GetChildren())
                    if (child is NCard c && GodotObject.IsInstanceValid(c)) alive.Add(c);
                var dead = new System.Collections.Generic.List<GodotObject>();
                foreach (var kv in _pcSeen) if (!alive.Contains(kv.Key)) dead.Add(kv.Key);
                foreach (var d in dead) _pcSeen.Remove(d);
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 出牌展示区清理失败（忽略）: " + e.Message);
        }
    }

    /// <summary>1Hz 兜底：展示区里我方卡面停留 ≥1.5s 就收掉（<paramref name="nowUsec"/> = `Time.GetTicksUsec()`）。</summary>
    internal static void SweepPlayContainer(long nowUsec)
    {
        if (nowUsec - _lastPcSweepMs < 1_000_000L) return;
        _lastPcSweepMs = nowUsec;
        ClearOurCardsFromPlayContainer("1Hz 兜底", 1_500L);
    }

    // ── 诊断用：把「装着滞留卡的那个容器」和「整个房间的卡面情况」说清楚（v0.2.968）──────

    /// <summary>
    /// 描述一个容器：类型 + 名字 + 前 12 个子节点（卡面带上它的 Model）。
    /// 用途见 <see cref="SweepRoom"/> 里那条「卡面滞留」日志的下一行。
    /// </summary>
    private static string DescribeContainer(Godot.Node? parent)
    {
        if (parent == null) return "（父节点已为 null）";
        try
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(parent.GetType().Name).Append('「').Append(parent.Name).Append("」")
              .Append(" 子节点 ").Append(parent.GetChildCount()).Append(" 个：");
            int n = 0;
            foreach (var child in parent.GetChildren())
            {
                if (n++ >= 12) { sb.Append(" …"); break; }
                sb.Append(" {").Append(child.GetType().Name).Append(':').Append(child.Name);
                if (child is NCard nc)
                {
                    string model = "<已清>";
                    try { model = nc.Model?.Id.Entry ?? "<已清>"; } catch { /* 忽略 */ }
                    sb.Append(" model=").Append(model);
                }
                sb.Append('}');
            }
            return sb.ToString();
        }
        catch (Exception e) { return "（读子节点失败: " + e.Message + "）"; }
    }

    /// <summary>
    /// 整个房间的卡面概况（1Hz 给「回合卡住」看门狗用）：总数 / 我方 / Model 已清 / 前几个可疑的。
    /// 只读，不动手。
    /// </summary>
    internal static string RoomCardSummary()
    {
        try
        {
            var room = MegaCrit.Sts2.Core.Nodes.Rooms.NCombatRoom.Instance;
            if (room == null || !GodotObject.IsInstanceValid(room)) return "房间=null";

            var found = new System.Collections.Generic.List<NCard>();
            var paths = new System.Collections.Generic.Dictionary<NCard, string>();
            CollectRoomCards(room, 0, found, paths);

            int ours = 0, modelGone = 0;
            var samples = new System.Text.StringBuilder();
            foreach (var card in found)
            {
                if (card == null || !GodotObject.IsInstanceValid(card)) continue;
                bool isOurs = false, gone = false;
                try { isOurs = IsOurBossCard(card); } catch { /* 忽略 */ }
                try { gone = card.Model == null; } catch { gone = true; }
                if (isOurs) ours++;
                if (gone) modelGone++;
                if ((isOurs || gone) && samples.Length < 220)
                {
                    string entry = "<Model 已清>";
                    try { entry = card.Model?.Id.Entry ?? "<Model 已清>"; } catch { /* 忽略 */ }
                    string path = paths.TryGetValue(card, out string? p) ? p : "?";
                    samples.Append('[').Append(entry).Append(" @").Append(path).Append("] ");
                }
            }
            return $"房内卡面={found.Count} 我方={ours} Model已清={modelGone}" +
                   (samples.Length > 0 ? " 可疑=" + samples.ToString().Trim() : "");
        }
        catch (Exception e) { return "（扫描失败: " + e.Message + "）"; }
    }

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
