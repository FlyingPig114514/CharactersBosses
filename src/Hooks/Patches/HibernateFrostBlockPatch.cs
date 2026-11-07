using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Orbs;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using CharacterBosses.Monsters;

namespace CharacterBosses.Patches;

/// <summary>
/// 只记录、不改行为：真人自己的冰球开始结算被动/激发时盖一个时间戳，
/// 好让 <see cref="HibernateFrostBlockPatch"/> 不去拦「他本来该拿的格挡」。
/// </summary>
[HarmonyPatch(typeof(FrostOrb), nameof(FrostOrb.Passive))]
public static class FrostOrbPassiveStampPatch
{
    [HarmonyPrefix]
    public static void Prefix(FrostOrb __instance) => HibernateFrostBlockPatch.NoteOwnFrostOrb(__instance);
}

/// <summary>同上，激发那一路。</summary>
[HarmonyPatch(typeof(FrostOrb), nameof(FrostOrb.Evoke))]
public static class FrostOrbEvokeStampPatch
{
    [HarmonyPrefix]
    public static void Prefix(FrostOrb __instance) => HibernateFrostBlockPatch.NoteOwnFrostOrb(__instance);
}

/// <summary>
/// 多人换牌 **A1：冰川 → 休眠** 的效果（设计见 <c>docs/plans/multiplayer-cards.md</c> §1）。
///
/// ## 原版「休眠」到底做了什么（已反编译 <c>references\sts2.dll</c> 确认）
/// <code>
/// // Hibernate（2 费技能，MultiplayerOnly，Repeat=2；升级 Repeat=3）
/// PowerCmd.Apply&lt;HibernatePower&gt;(owner.Creature, 1);   // 计数器，自己回合开始 -1 ⇒ 只持续当回合
/// OrbCmd.Channel&lt;FrostOrb&gt;() ×Repeat;                  // 引导 2/3 颗冰球
///
/// // HibernatePower：除了「回合开始减 1」之外它自己什么都不做
///
/// // FrostOrb.Passive / .Evoke（关键在这里）：
/// await CreatureCmd.GainBlock(Owner.Creature, PassiveVal /*2+集中*/ 或 EvokeVal /*5+集中*/, Unpowered, null);
/// if (Owner.Creature.HasPower&lt;HibernatePower&gt;())
///     foreach (玩家 p in CombatState.Players) if (p != Owner)          // ← 就是这一句
///         await CreatureCmd.GainBlock(p.Creature, 同上数值, Unpowered, null);
/// </code>
///
/// 所以「每只 Boss 拿多少格挡」根本不用我们自己算 —— 原版公式已经在里面了：
///   · 每颗冰球 **被动**（回合结束）= <c>2 + 集中</c>，**激发** = <c>5 + 集中</c>；
///   · 冰球有几颗就结算几次（冰球数量由「冰川/休眠」引导 + 队列容量决定）。
/// 我们唯一要改的是**发给谁**：原版那句 `foreach (玩家)` 遍历的是
/// <c>CombatState.Players</c>，而**我们 Boss 的影子 Player 不在这个列表里**
/// （见 `BossHookBridge` 类注释：故意排除，免得它参与玩家回合）⇒
/// 原版只会把格挡发给**真人玩家**，其它 Boss 一个都拿不到
/// （用户 2026-10-29 四人局：「故障机器人的休眠现在不会给予其它 Boss 它本回合冰球获得的格挡」）。
///
/// ## 因此这里做两件事
///   ① 拦掉「发给真人玩家」的那一笔；
///   ② **原样转给其它 Boss**（`GrantToOthersAsync`），这才符合休眠的设计意图。
/// 判据（四条全中才拦，尽量不可能误伤）：
///   1. 目标是**真人玩家**（<c>Creature.Monster == null</c>；Boss 的 Creature 一定带 Monster）；
///   2. 数字是**充能球那条路**给的（<c>ValueProp.Unpowered</c> 且不带 <c>Move</c>）；
///   3. 场上有一只**我们模组的 Boss** 带着 <c>HibernatePower</c>，且它队列里的某颗冰球的
///      <c>PassiveVal</c> / <c>EvokeVal</c> 与这笔格挡**数值完全相等**；
///   4. 真人自己**没有**同时带「休眠 + 冰球」（那种情况是他自己在玩休眠，退回原版行为）。
/// 命中就返回 0 格挡（不发给真人），其它 Boss 照旧从原版那句循环里拿到格挡。
///
/// 单人零回归：单人局里我们模组的 Boss 卡组不含「休眠」（换牌只在多人生效）⇒ 判据 3 永远不成立。
/// </summary>
[HarmonyPatch(typeof(CreatureCmd), nameof(CreatureCmd.GainBlock),
    new[] { typeof(Creature), typeof(decimal), typeof(ValueProp), typeof(CardPlay), typeof(bool) })]
public static class HibernateFrostBlockPatch
{
    private static int _blocked;

    [HarmonyPrefix]
    public static bool Prefix(Creature creature, decimal amount, ValueProp props, ref Task<decimal> __result)
    {
        try
        {
            if (!TryShouldBlock(creature, amount, props, out var source)) return true;

            // ⚠ 关键修正（用户 2026-10-29：「多人时故障机器人的休眠不再给予其它 Boss 格挡」）：
            //   原版那句 `foreach (玩家 in CombatState.Players)` **只包含真人玩家** ——
            //   我们 Boss 的影子 Player 被故意排除在 `CombatState.Players` 之外
            //   （见 `BossHookBridge` 类注释），所以「其它 Boss 本来就能拿到」是错的，
            //   一个都拿不到。这里拦下发给真人的那一笔，**原样转给其它 Boss**。
            //
            // ⚠⚠ 但那一圈循环是「每名真人各调一次」⇒ 4 人局会进来 **4 次**。
            //   以前每次进来都把 amount 发给**所有**其它 Boss ⇒ 每只 Boss 拿到的格挡是
            //   「原版一份 × 真人数」（用户 2026-10-29：「休眠给队友的格挡似乎过多了」；
            //   日志实证 11:25:56~11:26:02 连着 4 行 `冰球格挡 5 ⇒ 转给其它 Boss [3 只]`）。
            //   所以只让**这一圈里的第一次**（= 第一名真人玩家的那一次）负责转发，
            //   其余几次照样吞掉（真人不能拿），但**不再重复转发**。
            var others = FirstPlayerInLoop(creature) ? OtherBosses(creature?.CombatState, source?.Creature) : new List<Creature>();
            if (others.Count > 0)
            {
                if (_blocked++ < 40)
                {
                    Logger.Log($"[CharBoss] 休眠：冰球格挡 {amount} 不发给真人玩家 {creature.Name}" +
                               $" ⇒ 转给其它 Boss [{string.Join(",", others.ConvertAll(c => c.Monster?.Id.Entry ?? c.Name))}]" +
                               "（这一圈里只转一次；同一圈的其它真人那几次照样吞掉）");
                }
                __result = GrantToOthersAsync(others, amount);
                return false;
            }

            // 非「这一圈的第一个真人」的那几次：只吞掉，不转发（原版会对每名真人各调一次）
            if (!FirstPlayerInLoop(creature))
            {
                __result = Task.FromResult(0m);
                return false;
            }

            if (_blocked++ < 40)
            {
                Logger.Log($"[CharBoss] 休眠：冰球格挡 {amount} 不发给真人玩家 {creature.Name}（场上没有其它 Boss）");
            }
            __result = Task.FromResult(0m);
            return false;
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 休眠格挡拦截失败（按原版放行）: " + e.Message);
            return true;
        }
    }

    /// <summary>把这一笔格挡发给其它 Boss（原版不会发，见上面注释），返回自身拿到 0。</summary>
    private static async Task<decimal> GrantToOthersAsync(List<Creature> others, decimal amount)
    {
        foreach (var other in others)
        {
            try
            {
                if (other == null || !other.IsAlive) continue;
                await CreatureCmd.GainBlock(other, amount, ValueProp.Unpowered, null);
            }
            catch (Exception e)
            {
                Logger.Log("[CharBoss] 休眠格挡转给其它 Boss 失败（忽略）: " + e.Message);
            }
        }
        return 0m;
    }

    /// <summary>
    /// 这笔格挡的目标是不是「原版那一圈里的第一名真人玩家」。
    ///
    /// 用途：原版 `foreach (Player p in CombatState.Players) if (p != Owner) GainBlock(...)`
    /// 会**对每名真人各调一次**，而我们的转发是「发给所有其它 Boss」——
    /// 不挑一次的话就是真人数倍的格挡（用户 2026-10-29：「休眠给队友的格挡似乎过多了」）。
    /// 只认「这一圈里第一个被调到的人」，每次冰球结算就只会转发一次（一颗球一份，符合原版语义）。
    /// </summary>
    private static bool FirstPlayerInLoop(Creature? target)
    {
        try
        {
            if (target?.Player == null) return false;
            var combat = target.CombatState;
            if (combat?.Players == null) return false;
            foreach (var p in combat.Players)
            {
                if (p?.Creature == null) continue;
                if (p.Creature.Monster != null) continue;      // 我们的 Boss 不在这一圈里（也不在原版循环里）
                return ReferenceEquals(p, target.Player);
            }
            return false;
        }
        catch { return false; }
    }

    /// <summary>场上「除了施法者以外」我们还活着的 Boss。</summary>
    private static List<Creature> OtherBosses(MegaCrit.Sts2.Core.Combat.ICombatState? combat, Creature? sourceBoss)
    {
        var list = new List<Creature>();
        try
        {
            if (combat?.Enemies == null) return list;
            foreach (var c in combat.Enemies)
            {
                if (c?.Monster is not CharBossMonster) continue;
                if (!c.IsAlive) continue;
                if (ReferenceEquals(c, sourceBoss)) continue;
                list.Add(c);
            }
        }
        catch { /* 忽略 */ }
        return list;
    }

    private static bool TryShouldBlock(Creature? target, decimal amount, ValueProp props, out CharBossMonster? source)
    {
        source = null;
        if (target == null || amount <= 0m) return false;
        if ((props & ValueProp.Unpowered) == 0) return false;
        if ((props & ValueProp.Move) != 0) return false;
        if (target.Monster != null) return false;      // 只拦真人玩家；Boss 的 Creature 带 Monster

        var combat = target.CombatState;
        if (combat == null) return false;

        // ④' 目标**自己**就有一颗数值相同的冰球 ⇒ 这一笔更可能是他自己那颗球的被动/激发
        //     （真人玩故障机器人时会这样），绝不能拦 —— 拦了等于偷他的格挡。
        //     这条比下面那条「他自己在玩休眠」更宽：连没带休眠的自有冰球也一并放过。
        if (HasMatchingFrostOrb(target, amount)) return false;

        // ④'' 他**正在**结算自己那颗冰球（激发时原版会先把球从队列里摘掉再结算，
        //      所以上面那条可能扑空）⇒ 这笔格挡是他的，放行。
        if (OwnFrostInFlight(target)) return false;

        // ④ 真人自己在玩「休眠 + 冰球」⇒ 那是他的原版行为，一律放行
        foreach (var p in combat.Players)
        {
            if (p?.Creature == null) continue;
            if (p.Creature.Monster != null) continue;   // 我们的 Boss 不算「真人」
#if STS2_107
            continue;   // 旧版没有「休眠」这张牌
#else
            if (p.Creature.GetPower<HibernatePower>() == null) continue;
#endif
            if (HasFrostOrb(p)) return false;
        }

        // ③ 有我们模组的 Boss 带着休眠、冰球数值对得上
        foreach (var c in combat.Enemies)
        {
            if (c?.Monster is not CharBossMonster) continue;
            try
            {
#if STS2_107
                continue;   // 旧版没有「休眠」
#else
                if (c.GetPower<HibernatePower>() == null) continue;
#endif
            }
            catch { continue; }

            var queue = c.Player?.PlayerCombatState?.OrbQueue;
            if (queue == null) continue;
            foreach (var orb in queue.Orbs)
            {
                if (orb is not FrostOrb) continue;
                if (!SameValue(orb, amount)) continue;
                source = (CharBossMonster)c.Monster;      // 谁在休眠就是谁给的
                return true;
            }
        }
        return false;
    }

    /// <summary>这颗冰球的被动值 / 激发值里有没有等于 <paramref name="amount"/> 的那一个。</summary>
    private static bool SameValue(OrbModel orb, decimal amount)
    {
        try
        {
            if (orb.PassiveVal == amount) return true;
            if (orb.EvokeVal == amount) return true;
        }
        catch { /* 取不到就当对不上 */ }
        return false;
    }

    /// <summary>这名玩家自己的充能球队列里，有没有一颗「数值正好等于这笔格挡」的冰球。</summary>
    private static bool HasMatchingFrostOrb(Creature owner, decimal amount)
    {
        try
        {
            var queue = owner.Player?.PlayerCombatState?.OrbQueue;
            if (queue == null) return false;
            foreach (var orb in queue.Orbs)
            {
                if (orb is FrostOrb && SameValue(orb, amount)) return true;
            }
        }
        catch { /* 忽略 */ }
        return false;
    }

    // ── 「真人自己的冰球正在结算」的时间窗 ──────────────────────────────────
    // 原版激发时是**先 Remove 再 Evoke**（OrbCmd.Evoke 里 `if (dequeue) queue.Remove(orb)` 在
    // `await orb.Evoke()` 之前），所以那一刻队列里已经没有那颗球、HasMatchingFrostOrb 会扑空。
    // 于是改成在 FrostOrb 自己的 Passive/Evoke 上打一条**只记录**的前缀：只要这名真人正在结算
    // 自己的冰球，这笔格挡就一定是他的 —— 一律放行（宁可少拦一次，也不能吞玩家的格挡）。
    private static readonly Dictionary<Player, ulong> OwnFrostStamp = new();
    private static readonly ulong OwnFrostWindowUsec = 1_500_000;   // 1.5 秒，足够跨过几次 await

    /// <summary>记录「这名玩家刚触发了一次自己的冰球被动/激发」。</summary>
    internal static void NoteOwnFrostOrb(FrostOrb? orb)
    {
        try
        {
            var owner = orb?.Owner;
            if (owner?.Creature == null) return;
            if (owner.Creature.Monster != null) return;      // 我们的 Boss 不算（要拦的就是它的分发）
            OwnFrostStamp[owner] = Time.GetTicksUsec();
        }
        catch { /* 忽略 */ }
    }

    private static bool OwnFrostInFlight(Creature target)
    {
        try
        {
            var player = target.Player;
            if (player == null) return false;
            if (!OwnFrostStamp.TryGetValue(player, out var at)) return false;
            return Time.GetTicksUsec() - at < OwnFrostWindowUsec;
        }
        catch { return false; }
    }

    private static bool HasFrostOrb(MegaCrit.Sts2.Core.Entities.Players.Player player)
    {
        try
        {
            var queue = player.PlayerCombatState?.OrbQueue;
            if (queue == null) return false;
            foreach (var orb in queue.Orbs) if (orb is FrostOrb) return true;
        }
        catch { /* 忽略 */ }
        return false;
    }
}
