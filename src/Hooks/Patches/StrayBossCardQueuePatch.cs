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
    /// 暂停 <see cref="Reap"/>（「抉择，抉择」在等玩家选牌期间由 <c>CharBossMonster.PlayOne</c> 置上）。
    /// 只影响这一个时间窗，其它时候照旧收她的卡面。
    /// </summary>
    internal static bool HoldReap;

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
            foreach (var child in queue.GetChildren())
            {
                if (child is not NCard card) continue;
                if (!GodotObject.IsInstanceValid(card)) continue;
                if (!IsOurBossCard(card)) continue;
                ours.Add(card);
            }
            if (ours.Count == 0) return;

            foreach (var card in ours)
            {
                if (!GodotObject.IsInstanceValid(card)) continue;
                string entry = "?";
                try { entry = card.Model?.Id.Entry ?? "?"; } catch { /* 忽略 */ }

                try
                {
                    queue.RemoveCardFromQueueForCancellation(card, false);
                    if (_removed++ < 40)
                    {
                        Logger.Log($"[CharBoss] 出牌队列：{entry} 是 Boss 打出的牌 → 已从队列里摘掉" +
                                   "（以前只隐藏、item 留着，会越积越多，把回合收尾拖慢）");
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
