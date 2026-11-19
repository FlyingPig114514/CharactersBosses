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

    /// <summary>记录凶手那一场战斗的**遭遇 id**（原版 `<encounter>.loss` 的键）。</summary>
    private static string? _combatEncounterEntry;

    /// <summary>记录凶手那一场战斗所属的**这一局**（换局后旧记录一律不算）。</summary>
    private static MegaCrit.Sts2.Core.Runs.IRunState? _combatRun;

    /// <summary>「不是死亡那一场」的提示只各打一行日志（诊断用，防刷屏）。</summary>
    private static readonly HashSet<string> _skipLogged = new(StringComparer.Ordinal);

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

                // ★ v0.2.788：顺手记下这一场战斗的「遭遇」与「这一局」——
                //   死亡提示只允许在**这一场**用凶手改写（见 IsDeathCombat）。
                try
                {
                    _combatEncounterEntry = target.CombatState?.Encounter?.Id.Entry;
                    _combatRun = target.CombatState?.RunState;
                }
                catch { _combatEncounterEntry = null; _combatRun = null; }
            }

            var boss = BossOf(dealer) ?? BossOf(cardSource?.Owner?.Creature);
            if (boss == null) return;
            if (ReferenceEquals(boss.Shadow, target.Player)) return;   // 影子玩家自己掉血不算

            LastKiller[target] = boss;
        }
        catch { /* 绝不影响战斗 */ }
    }

    /// <summary>
    /// **这条死亡提示对应的就是「记录凶手的那一场战斗」吗？**
    ///
    /// ★ v0.2.788（用户 2026-11-19：「这不同的几局怎么都显示灵魂被亡灵契约师收走了（只有最后一局是死于灵魂骨的）」）
    ///   日志实证（同一秒里几十行）：
    ///   <code>
    ///   死亡提示按凶手显示：NECROBINDER_SOUL_BOSS_ENCOUNTER（本场遭遇是 SILENT_SNECKO_BOSS_ENCOUNTER）
    ///   死亡提示按凶手显示：NECROBINDER_SOUL_BOSS_ENCOUNTER（本场遭遇是 SPINY_TOAD_NORMAL）
    ///   </code>
    ///   根因：**结算/历史界面会为「本局每一场战斗」各问一次 `GetLossMessageFor`**，而我们的补丁
    ///   不区分「这一格是哪一场」——拿到的永远是 tracker 里**上一场（甚至是上一局）**记下的凶手
    ///   ⇒ 历史里每一格都被改写成同一句「灵魂被亡灵契约师收走了」。
    ///   现在两条判据都满足才改：**遭遇 id 与记录那一场相同** 且 **还是同一局**；
    ///   不满足就保持原版（原版本来就按每一格自己的遭遇取 `<Id>.loss`，是对的）。
    /// </summary>
    internal static bool IsDeathCombat(ModelId? encounterId)
    {
        try
        {
            string? entry = encounterId?.Entry;
            if (string.IsNullOrEmpty(entry)) return false;
            if (string.IsNullOrEmpty(_combatEncounterEntry)) return false;

            bool sameEncounter = string.Equals(entry, _combatEncounterEntry, StringComparison.Ordinal);
            if (!sameEncounter)
            {
                if (_skipLogged.Add(entry!))
                    Logger.Log($"[CharBoss] 死亡提示：{entry} 不是记录凶手的那一场" +
                               $"（那一场是 {_combatEncounterEntry}）⇒ 保持原版提示");
                return false;
            }

            // 同一局？（换局之后旧记录一律不算，否则上一局打死的 Boss 会一直冒充这一局的凶手）
            var run = MegaCrit.Sts2.Core.Runs.RunManager.Instance?.DebugOnlyGetState();
            if (run == null) return false;
            return ReferenceEquals(run, _combatRun);
        }
        catch { return false; }
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
    public static CharBossMonster? BossOf(Creature? creature)
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
