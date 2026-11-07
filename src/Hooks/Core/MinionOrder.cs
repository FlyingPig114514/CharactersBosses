using System;
using System.Collections.Generic;
using System.Reflection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using CharacterBosses.Monsters;

namespace CharacterBosses.Core;

/// <summary>
/// 「仆从排在储君**之前**行动」（用户 2026-11-05）。
///
/// ## 为什么需要
/// 原版的行动顺序就是 `CombatState._enemies` 这个 `List&lt;Creature&gt;` 的**先后次序**
/// （`CreatureCmd.Add` 是**追加到末尾**）⇒ 战斗中途召唤出来的仆从会排在储君**后面**，
/// 变成「储君先动、仆从后动」，与用户要求相反。
///
/// ## 做法
/// 召唤成功后，把这只仆从从 `_enemies` 里挪到**它的召唤者之前**（同一个 `List` 内原地移动）。
/// 这样：其它 boss（更早生成）→ 仆从 → 储君，正好是用户要的
/// 「其它boss、所有仆从依次行动、储君」，多人时储君自动永远最后。
///
/// ⚠ 改的是列表**顺序**，不动任何数值/成员集合；
///   `_enemies` 是 private 字段，所以这里走反射（字段名由反编译确认）。
/// </summary>
internal static class MinionOrder
{
    private static FieldInfo? _enemiesField;
    private static bool _searched;

    /// <summary>把 <paramref name="minion"/> 挪到它的召唤者（<paramref name="summoner"/>）之前。</summary>
    internal static void MoveBeforeSummoner(ICombatState? combat, Creature? summoner, Creature? minion)
    {
        try
        {
            if (combat == null || summoner == null || minion == null) return;

            var list = EnemiesList(combat);
            if (list == null) return;

            int minionIdx = list.IndexOf(minion);
            int summonerIdx = list.IndexOf(summoner);
            if (minionIdx < 0 || summonerIdx < 0) return;
            if (minionIdx < summonerIdx) return;          // 已经在前面了

            list.RemoveAt(minionIdx);
            // 移除后召唤者的下标可能前移一格
            summonerIdx = list.IndexOf(summoner);
            if (summonerIdx < 0) return;
            list.Insert(summonerIdx, minion);
            Logger.Log($"[CharBoss] 仆从行动顺序：把 {minion.Monster?.Id.Entry} 挪到储君之前" +
                       $"（当前敌人次序 {Describe(combat)}）");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 调整仆从行动顺序失败（按原版顺序）: " + e.Message);
        }
    }

    /// <summary>
    /// 敌方回合**开始那一刻**统一排序（用户 2026-11-05：仆从先于储君行动）。
    ///
    /// ⚠ 为什么不在召唤时就挪：那是在**她的回合进行中**改动 `_enemies`，
    ///   而出牌/行动循环正在按这个列表推进 ⇒ 会把它卡死（实测：召唤完最后一行日志就没了）。
    ///   改成回合开始前一次性排好：其它敌人（原顺序）→ 仆从 → 仆从储本人。
    /// </summary>
    internal static void ReorderAtEnemyTurnStart(ICombatState? combat, Creature? minionBoss)
    {
        try
        {
            if (combat == null || minionBoss == null) return;
            var list = EnemiesList(combat);
            if (list == null || list.Count < 2) return;

            var others = new List<Creature>();
            var minions = new List<Creature>();
            var bosses = new List<Creature>();
            foreach (var c in list)
            {
                if (ReferenceEquals(c, minionBoss)) bosses.Add(c);
                else if (IsMinion(c)) minions.Add(c);
                else others.Add(c);
            }
            if (minions.Count == 0) return;

            var ordered = new List<Creature>(list.Count);
            ordered.AddRange(others);
            ordered.AddRange(minions);
            ordered.AddRange(bosses);

            bool changed = false;
            for (int i = 0; i < ordered.Count && i < list.Count; i++)
            {
                if (!ReferenceEquals(ordered[i], list[i])) { changed = true; break; }
            }
            if (!changed) return;

            list.Clear();
            list.AddRange(ordered);
            Logger.Log("[CharBoss] 仆从行动顺序：敌方回合开始前重排 ⇒ " + Describe(combat));
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 仆从行动顺序重排失败（按原版顺序）: " + e.Message);
        }
    }
    /// <summary>拿到底层的 `_enemies` 列表（private 字段）。</summary>
    private static List<Creature>? EnemiesList(ICombatState combat)
    {
        try
        {
            if (!_searched)
            {
                _searched = true;
                var type = combat.GetType();
                while (type != null && _enemiesField == null)
                {
                    _enemiesField = type.GetField("_enemies",
                        BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    type = type.BaseType;
                }
            }
            return _enemiesField?.GetValue(combat) as List<Creature>;
        }
        catch { return null; }
    }

    private static string Describe(ICombatState combat)
    {
        try
        {
            var parts = new List<string>();
            foreach (var c in combat.Enemies) parts.Add(c?.Monster?.Id.Entry ?? "?");
            return string.Join(" → ", parts);
        }
        catch { return "?"; }
    }

    /// <summary>这只敌人是不是「仆从」（我们召唤的，或身上有原版爪牙）。</summary>
    internal static bool IsMinion(Creature? creature)
        => creature?.Monster is BossMinionMonster;
}
