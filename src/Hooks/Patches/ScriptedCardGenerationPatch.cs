using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Random;
using CharacterBosses.Core;
using CharacterBosses.Monsters;
using CharacterBosses.UI;

namespace CharacterBosses.Patches;

/// <summary>
/// **拦掉**储君的「印牌」效果 —— 烦人机关盒 / 光谱偏移 / 君权自授 都不许再印牌。
///
/// 用户设计：这些效果"印出来"的牌，剧本里已经直接摆在手上了（意图也是照它显示的），
/// 再让它们真的印一张就变成两张不同的牌（实测：机关盒印的和意图里显示的不是同一张）。
/// 这三者都走同一个入口（反编译确认）：
/// <c>CardFactory.GetDistinctForCombat(Player player, IEnumerable&lt;CardModel&gt; cards, int count, Rng rng)</c>，
/// 所以在这里直接返回**空集合**，调用方就什么都不加。
/// 只对"印牌的人是我们的 Boss"生效，真人玩家/其它怪完全不受影响。
/// 唯一需要补偿的是"这张牌本该 0 费"（机关盒的设定）→ 由剧本的 <c>ScriptCard.FreeThisTurn</c> 负责。
/// </summary>
[HarmonyPatch(typeof(CardFactory), nameof(CardFactory.GetDistinctForCombat))]
public static class ShadowBossNoRandomCardPatch
{
    [HarmonyPrefix]
    public static bool Prefix(Player player, ref IEnumerable<CardModel> __result)
    {
        try
        {
            if (player == null) return true;
            if (player.Creature?.Monster is not CharBossMonster) return true;

#if STS2_107
            // ★ 2026-11-05 实测（用户：「羽化一印牌游戏就卡死」）：旧版（v0.107）的调用方拿到
            //   **空集合**后不会善罢甘休（一直重试/等待）⇒ 直接把这场战斗卡死。
            //   所以旧版**不再拦随机印牌**：宁可让「羽化」按原版印 3 张随机攻击牌，也不能卡死。
            //   （配合 `EventMachineMechanics.MetamorphosisPrintsAsync` 在旧版跳过，避免印成两份。）
            Logger.Log("[CharBoss] 旧版（107）：随机印牌放行（返回空集合会让原版死循环/卡死）");
            return true;
#else
            __result = Array.Empty<CardModel>();
            Logger.Log("[CharBoss] 拦掉一次随机印牌（机关盒/光谱偏移/君权自授 —— 剧本里已经写好了那张牌）");
            return false;
#endif
        }
        catch
        {
            return true;
        }
    }
}

/// <summary>
/// 剧本里「这一回合会被印出来的牌」的队列（每个 Boss 每回合重填一次）。
///
/// 判据：剧本条目里 <c>From</c> 不是 "deck" 的那些（"generated"/"box"/"ai"/"hologram"…）
/// —— 它们代表"某个效果印出来的牌"，正好就是印牌效果应该给的那几张。
/// </summary>
public static class BossScriptTakeQueue
{
    private static readonly Dictionary<CharBossMonster, Queue<ScriptCard>> Queues = new();
    private static readonly Dictionary<CharBossMonster, int> FilledTurn = new();

    /// <summary>按当前回合的剧本准备队列（每回合只做一次；来源变了重做）。</summary>
    private static void EnsureFilled(CharBossMonster boss)
    {
        int turn = boss.TurnNumber;
        if (FilledTurn.TryGetValue(boss, out int filled) && filled == turn && Queues.ContainsKey(boss)) return;

        FilledTurn[boss] = turn;
        var queue = new Queue<ScriptCard>();

        try
        {
            var turnScript = boss.Def.ScriptForAct(boss.ActIndex)?.TurnAt(turn);
            if (turnScript != null)
            {
                foreach (var entry in turnScript.Cards)
                {
                    if (entry.From == "deck") continue;          // 卡组里的牌不是"印出来的"
                    queue.Enqueue(entry);
                }
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 准备「剧本印牌」队列失败: " + e.Message);
        }

        Queues[boss] = queue;
        if (queue.Count > 0)
        {
            Logger.Log($"[CharBoss] 第 {turn} 回合的剧本印牌队列（{queue.Count} 张）：" +
                       string.Join(",", queue.Select(x => x.Card.Name)));
        }
    }

    /// <summary>
    /// 按顺序取 <paramref name="count"/> 张剧本写好的牌。
    ///
    /// ⚠ **优先返回手牌里已经存在的那一张**，而不是新建一张：
    /// 剧本造的牌本来就在手上（意图也是照它显示的），如果印牌效果再新建一张，
    /// 就会变成"手上 1 张全费 + 印出来 1 张"两张不同的牌（用户实测：
    /// 机关盒印的和意图里显示的不是同一张）。返回同一个实例后，
    /// 机关盒那句 <c>SetToFreeThisTurn()</c> 正好把**意图里那张**变成 0 费。
    /// </summary>
    public static List<CardModel> Take(Player player, int count)
    {
        var result = new List<CardModel>();
        try
        {
            // 多人「双 Boss 两套面板」：优先按**出牌的那个 Player** 反查是哪只 Boss
            //（原来只认 CurrentBoss，两只同场时可能拿错另一只的剧本队列）。
            var boss = (player?.Creature?.Monster as Monsters.CharBossMonster) ?? BossHandUi.CurrentBoss;
            if (boss == null) return result;
            EnsureFilled(boss);

            if (!Queues.TryGetValue(boss, out var queue) || queue.Count == 0) return result;

            var pcs = player.PlayerCombatState;
            var hand = pcs?.Hand?.Cards;

            for (int i = 0; i < count && queue.Count > 0; i++)
            {
                var entry = queue.Dequeue();
                var existing = FindInHand(hand, entry);
                if (existing != null) { result.Add(existing); continue; }

                // 手上没有（例如这回合没有经过 BuildHand）→ 才新建一张
                var canonical = ModelDb.GetById<CardModel>(ModelDb.GetId(entry.Card));
                if (canonical == null) continue;
                var card = player.Creature?.CombatState?.CreateCard(canonical, player);
                if (card == null) continue;
                if (entry.Upgraded && !card.IsUpgraded) card.UpgradeInternal();
                result.Add(card);
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 取剧本印牌失败: " + e.Message);
        }
        return result;
    }

    private static CardModel? FindInHand(IReadOnlyList<CardModel>? hand, ScriptCard entry)
    {
        if (hand == null) return null;
        try
        {
            var wantId = ModelDb.GetId(entry.Card);
            foreach (var card in hand)
            {
                if (card == null) continue;
                if (!ReferenceEquals(ModelDb.GetId(card.GetType()), wantId)) continue;
                if (entry.Upgraded && !card.IsUpgraded) card.UpgradeInternal();
                return card;
            }
        }
        catch { /* 忽略 */ }
        return null;
    }

    private static string SafeEntry(CardModel? card)
    {
        try { return card?.Id.Entry ?? "?"; } catch { return "?"; }
    }
}
