using System;
using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using CharacterBosses.Monsters;

namespace CharacterBosses.Patches;

/// <summary>
/// 「炸弹」（<c>TheBombPower</c>）倒计时剩 **1** 时让那个能力图标**闪烁**
/// （用户 2026-10-29：「当储君 buff 栏中的炸弹倒计时为 1 时，让该 buff 闪烁，
/// 类似 1 代崩坠中的商人」）。
///
/// 做法：每帧驱动里找一次「这只 Boss 身上炸弹图标」的节点（`NPower`），
/// 倒计时为 1 时把它的 `Modulate` 按正弦做成 0.4↔1.0 的呼吸效果，其余情况恢复正常。
/// 只改**显示**，不动任何战斗数值；节点找不到就什么都不做（下一帧再试）。
///
/// ⚠ 不用自己的 `_Process`（本项目的 mod 程序集里实测不触发），由 `BossFrameTick` 调。
/// </summary>
internal static class BombBlink
{
    /// <summary>呼吸周期（毫秒）。用户没指定速度，取 800ms 一个来回，接近崩坠商人那种节奏。</summary>
    private const float PeriodMs = 800f;

    private static readonly Dictionary<CharBossMonster, NPower> Cache = new();

    internal static void Tick(IReadOnlyList<CharBossMonster>? bosses)
    {
        if (bosses == null || bosses.Count == 0) return;

        float phase = (float)(Time.GetTicksMsec() % (ulong)PeriodMs) / PeriodMs;      // 0..1
        float wave = Mathf.Abs(Mathf.Sin(phase * Mathf.Pi * 2f));                     // 0..1
        float alpha = 0.4f + 0.6f * wave;                                             // 0.4..1.0

        foreach (var boss in bosses)
        {
            try
            {
                if (boss?.Creature == null) continue;

                var power = boss.Creature.GetPower<TheBombPower>();
                var node = PowerNodeOf(boss, power);

                if (node == null) continue;

                bool urgent = power != null && AmountOf(power) <= 1;
                node.Modulate = urgent
                    ? new Color(1f, 1f, 1f, alpha)
                    : new Color(1f, 1f, 1f, 1f);
            }
            catch { /* 单只失败不影响其它 */ }
        }
    }

    private static int AmountOf(TheBombPower? power)
    {
        try { return power == null ? int.MaxValue : (int)power.Amount; }
        catch { return int.MaxValue; }
    }

    /// <summary>找到「这只 Boss 身上某份 Power」对应的图标节点（带缓存；失效就重找）。</summary>
    private static NPower? PowerNodeOf(CharBossMonster boss, TheBombPower? power)
    {
        if (power == null)
        {
            Cache.Remove(boss);
            return null;
        }

        if (Cache.TryGetValue(boss, out var cached) && cached != null && GodotObject.IsInstanceValid(cached))
        {
            try { if (ReferenceEquals(cached.Model, power)) return cached; }
            catch { /* 模型取不到就重找 */ }
        }

        var found = FindNode(boss.Creature, power);
        if (found != null) Cache[boss] = found;
        else Cache.Remove(boss);
        return found;
    }

    private static NPower? FindNode(MegaCrit.Sts2.Core.Entities.Creatures.Creature? creature, TheBombPower power)
    {
        try
        {
            var room = NCombatRoom.Instance;
            if (room == null || creature == null) return null;

            var creatureNode = room.GetCreatureNode(creature);
            if (creatureNode == null || !GodotObject.IsInstanceValid(creatureNode)) return null;

            return Search(creatureNode, power, 0);
        }
        catch { return null; }
    }

    private static NPower? Search(Node? node, TheBombPower power, int depth)
    {
        if (node == null || depth > 12) return null;

        try
        {
            if (node is NPower powerNode)
            {
                try
                {
                    if (ReferenceEquals(powerNode.Model, power)) return powerNode;
                }
                catch { /* Model 还没设过：忽略 */ }
            }

            foreach (var child in node.GetChildren())
            {
                var hit = Search(child, power, depth + 1);
                if (hit != null) return hit;
            }
        }
        catch { /* 忽略 */ }

        return null;
    }

    /// <summary>战斗结束/换 Boss 时清缓存（生物节点每场都是新的）。</summary>
    internal static void Reset()
    {
        try { Cache.Clear(); } catch { /* 忽略 */ }
    }
}
