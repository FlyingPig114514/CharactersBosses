using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using CharacterBosses.Monsters;

namespace CharacterBosses.Core;

/// <summary>
/// 「这个玩家是被**哪一只** Boss 打死的」——多人同场两只 Boss 时，死亡提示要报**凶手**，
/// 而不是这一场的主 Boss（用户 2026-10-23）。
///
/// 数据来源：原版 <c>Hook.AfterDamageGiven(ctx, combatState, dealer, results, props, target, cardSource)</c>
/// 是**每一次伤害结算后**的钩子，把 (受伤的玩家 → 出手的那只 Boss) 记下来即可
/// （挂载见 <c>Patches/BossLossMessagePatches.cs</c>）。
///
/// 「出手方」有三种可能，都要认：
///   1. 她本人（`dealer.Monster is CharBossMonster`）—— 奥斯提/遗物这类直接伤害；
///   2. 她的随从（奥斯提、佩尔的士兵）—— 看 `PetOwner` / `Player`；
///   3. 她的**影子玩家**替她出牌（卡牌攻击的 dealer 是影子 Player 的生物）——
///      反查「哪个 Boss 的 `Shadow` 是这位玩家」。
/// `cardSource` 再兜一次底（中毒/持续伤害这类 dealer 可能是 null，但牌还在）。
///
/// 每场战斗一张新表（换战斗时清空）。
/// </summary>
public static class BossKillerTracker
{
    private static readonly Dictionary<Creature, CharBossMonster> LastKiller = new();
    private static object? _combatKey;

    /// <summary>记一笔「谁打了他」。只在确实能认出是哪只 Boss 时才写。</summary>
    internal static void Note(Creature? dealer, Creature? target, CardModel? cardSource)
    {
        try
        {
            if (target == null) return;
            if (target.Player == null) return;            // 只关心玩家被打（影子玩家也会进来，靠下面 BossOf 过滤）

            object? key = null;
            try { key = target.CombatState; } catch { /* 忽略 */ }
            if (key != null && !ReferenceEquals(key, _combatKey))
            {
                _combatKey = key;
                LastKiller.Clear();
            }

            var boss = BossOf(dealer) ?? BossOf(cardSource?.Owner?.Creature);
            if (boss == null) return;
            if (ReferenceEquals(boss.Shadow, target.Player)) return;   // 影子玩家自己掉血不算

            LastKiller[target] = boss;
        }
        catch { /* 绝不影响战斗 */ }
    }

    /// <summary>
    /// 这个角色是被谁打死的（认不出来就返回 null = 保持原版提示）。
    /// 先找「已经不在世的、角色对得上的玩家生物」，再退回「角色对得上的任意一条记录」。
    /// </summary>
    public static CharBossMonster? KillerOf(CharacterModel? character)
    {
        if (character == null) return null;
        try
        {
            CharBossMonster? fallback = null;
            foreach (var pair in LastKiller)
            {
                var victim = pair.Key;
                if (victim == null || pair.Value == null) continue;
                if (!SameCharacter(victim.Player, character)) continue;
                if (!victim.IsAlive) return pair.Value;
                fallback ??= pair.Value;
            }
            return fallback;
        }
        catch { return null; }
    }

    private static bool SameCharacter(Player? player, CharacterModel character)
    {
        try
        {
            if (player?.Character == null) return false;
            if (ReferenceEquals(player.Character, character)) return true;
            return player.Character.Id == character.Id;
        }
        catch { return false; }
    }

    /// <summary>「这个出手方属于哪只 Boss」——她自己 / 她的随从 / 她的影子玩家。</summary>
    private static CharBossMonster? BossOf(Creature? creature)
    {
        try
        {
            if (creature == null) return null;

            if (creature.Monster is CharBossMonster direct) return direct;

            // 她的随从（奥斯提、佩尔的士兵）：PetOwner 最权威，取不到再看 Player
            var owner = creature.PetOwner ?? creature.Player;
            if (owner?.Creature?.Monster is CharBossMonster viaOwner) return viaOwner;

            // 她的影子玩家替她出牌：反查谁 Shadow 是这位玩家
            if (creature.Player != null) return BossOwningShadow(creature.Player);
        }
        catch { /* 忽略 */ }
        return null;
    }

    private static CharBossMonster? BossOwningShadow(Player shadow)
    {
        try
        {
            var combat = shadow.Creature?.CombatState;
            if (combat == null) return null;

            foreach (var list in new[] { combat.Enemies, combat.Allies })
            {
                if (list == null) continue;
                foreach (var creature in list)
                {
                    if (creature?.Monster is CharBossMonster boss && ReferenceEquals(boss.Shadow, shadow))
                        return boss;
                }
            }
        }
        catch { /* 忽略 */ }
        return null;
    }
}
