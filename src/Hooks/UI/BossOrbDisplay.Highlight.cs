using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Orbs;
using CharacterBosses.Core;
using CharacterBosses.Monsters;

namespace CharacterBosses.UI;

/// <summary>
/// 球位「待激发显示」与「每回合被动结算」—— <see cref="BossOrbDisplay"/> 的分部文件（守住单文件 ≤200 行）。
///
/// 原版玩家侧：手里提着「双重释放/碎片」时，**将要被激发的那个球**右下角数值会从被动值换成
/// **激发值**并变蓝（两套标签由 <c>NOrb.UpdateVisuals(isEvoking:true)</c> 切换，颜色在场景里）。
///
/// 原版只给了两档：<c>NOrbManager.UpdateVisuals(OrbEvokeType.Front/All)</c> = 「队首」或「全部」。
/// 但 Boss 这边会出现「队首之外的某颗球会被激发」（球位满时生成球会激发队首，双重释放激发队首两次，
/// 碎片激发所有球之后队首又换了…），两档不够用。好在 <c>NOrb.UpdateVisuals(bool)</c> 本身是 public，
/// 于是这里自己逐球点亮 —— 判定用的是**和意图完全相同的模拟器** <see cref="BossOrbSim"/>，
/// 所以「亮起的球」与「显示了攻势/守势意图的球」永远是同一批。
/// </summary>
public static partial class BossOrbDisplay
{
    /// <summary>
    /// 本回合会被激发的球（真实队列里的实例）——记下来，好让每次战斗状态变化后重新点亮。
    ///
    /// 多人「双 Boss 两套面板」：**按 Boss 各记一份**。原来是一个全局 HashSet 且每次刷新都 Clear，
    /// 于是第二只 Boss 一刷新就把第一只的高亮抹了。
    /// </summary>
    private static readonly Dictionary<CharBossMonster, HashSet<OrbModel>> Evoking = new();

    /// <summary>这颗球是不是「本回合会被激发」（所有 Boss 的那几份取并集 —— 球实例天然不重复）。</summary>
    private static bool IsEvoking(OrbModel orb)
    {
        foreach (var set in Evoking.Values)
        {
            if (set.Contains(orb)) return true;
        }
        return false;
    }

    /// <summary>按本回合计划重算「哪些球会被激发」，然后点亮它们（手牌栏每次刷新时调）。</summary>
    public static void HighlightEvokes(CharBossMonster boss)
    {
        try
        {
            if (boss == null) return;
            if (!Evoking.TryGetValue(boss, out var mine))
            {
                mine = new HashSet<OrbModel>();
                Evoking[boss] = mine;
            }
            mine.Clear();

            var pcs = boss.Shadow?.PlayerCombatState;
            if (pcs != null && boss.Plan.Count > 0)
            {
                var sim = new BossOrbSim(boss.Creature, pcs.OrbQueue);
                foreach (var play in boss.Plan) sim.Predict(play.Card);
                foreach (var orb in sim.Evoked) mine.Add(orb);
            }

            ReapplyHighlight(NodeOf(boss)?.OrbManager);
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 球位高亮失败: " + e.Message);
        }
    }

    /// <summary>
    /// 把「待激发」状态落到节点上：先全部恢复被动值，再逐球点亮 <see cref="EvokingOrbs"/> 里的那些。
    /// 补丁会在原版 <c>OnCombatStateChanged → UpdateVisuals(None)</c> 清掉高亮之后调它恢复。
    /// </summary>
    public static void ReapplyHighlight(NOrbManager? manager)
    {
        if (manager == null || !GodotObject.IsInstanceValid(manager)) return;

        manager.UpdateVisuals(OrbEvokeType.None);

        var nodes = SlotList(manager);
        var queue = QueueOf(manager);
        if (nodes == null || queue == null) return;

        // _orbs[i] 就是队列里第 i 颗（队首画在最右侧，与原版「提着牌时最右边那颗变蓝」一致）
        for (int i = 0; i < nodes.Count && i < queue.Count; i++)
        {
            var node = nodes[i];
            if (node == null || !GodotObject.IsInstanceValid(node)) continue;
            if (!IsEvoking(queue[i])) continue;
            node.UpdateVisuals(true);
        }
    }

    /// <summary>清掉激发高亮（回合结束后球数值恢复成被动值）。boss 为 null = 全场清空。</summary>
    public static void ClearHighlight(CharBossMonster? boss)
    {
        try
        {
            if (boss != null) Evoking.Remove(boss);
            else Evoking.Clear();

            var manager = boss != null ? NodeOf(boss)?.OrbManager : null;
            if (manager == null || !GodotObject.IsInstanceValid(manager)) return;
            manager.UpdateVisuals(OrbEvokeType.None);
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 球位取消高亮失败: " + e.Message);
        }
    }

    /// <summary>管理器挂着的那名玩家的 orb 队列（顺序 = 屏幕上从右到左）。</summary>
    private static IReadOnlyList<OrbModel>? QueueOf(NOrbManager manager)
    {
        try
        {
            if (CreatureField?.GetValue(manager) is not NCreature node) return null;
            return node.Entity?.Player?.PlayerCombatState?.OrbQueue?.Orbs;
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 读球队列失败: " + e.Message);
            return null;
        }
    }

    /// <summary>Boss 自己回合结束时结算球的被动（原版时机）。</summary>
    public static async Task ResolveTurnEnd(CharBossMonster boss, PlayerChoiceContext ctx)
    {
        var pcs = boss.Shadow?.PlayerCombatState;
        if (pcs == null || pcs.OrbQueue.Orbs.Count == 0) return;

        try
        {
            Logger.Log($"[CharBoss] 球被动结算: {pcs.OrbQueue.Orbs.Count} 颗");
            await pcs.OrbQueue.BeforeTurnEnd(ctx);
            // 本回合的计划已经执行完 → 不再有「将要激发」的球，恢复显示被动值
            ClearHighlight(boss);
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 球被动结算失败: " + e);
        }
    }
}
