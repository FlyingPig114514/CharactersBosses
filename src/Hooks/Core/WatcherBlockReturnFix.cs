#if STS2_111
using System;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;

namespace CharacterBosses.Core;

/// <summary>
/// **观者「以手拒之」（Talk to the Hand）的格挡返还，别返还给她自己**（用户 2026-11-28）。
///
/// 用户报：「以手拒之给敌方 boss 上的 debuff 格挡返还好像会让被上 debuff 的 boss 受击时也起甲。
/// 你看看是不是有这个问题，是和本模组有关吗？」
///
/// ## 反编译（A 版 Boninall `WatcherMod.BlockReturnPower.<AfterDamageGiven>d__4.MoveNext`）
/// <code>
/// if (target == Owner &amp;&amp; dealer != null &amp;&amp; dealer != Owner
///     &amp;&amp; cardSource != null &amp;&amp; cardSource.Type == Attack
///     &amp;&amp; props.IsPoweredAttack() &amp;&amp; result.TotalDamage > 0)
/// {
///     object obj = dealer.Player;              // ← ① 攻击者自己有 Player 就用它
///     if (obj == null) obj = dealer.PetOwner;  // ← ② 攻击者的主人
///     if (obj == null) obj = Owner.Applier?.Player;   // ← ③ 兜底：施加 debuff 的那位玩家
///     Player p = (Player)obj;
///     if (p != null) await CreatureCmd.GainBlock(p.Creature, Amount, (ValueProp)4, null, false);
/// }
/// </code>
///
/// ## 为什么只有在本模组的多 Boss 局里会「给 Boss 起甲」
/// **我们的 Boss 是「影子玩家」**（`ShadowPlayerFactory`）：同一只 `Creature` **既是 Monster 又是 Player**
/// （`IsShadowBound(creature)` = `Player != null &amp;&amp; Monster != null`）。
/// 而原版游戏的敌人 `Creature.Player` **永远是 null** ⇒ ① 那条永远不成立、必然走到 ③（返还给施加者）。
/// 我们这里一旦发生「**某只 Boss 打了被上 debuff 的另一只 Boss**」（多 Boss 同场时球/AoE/召唤物
/// 打到自己人的情况是真实存在的——`BossOrbTargeting` 那条 bug 就是同一个根源），
/// ① 立刻命中：`dealer.Player` = 那只 Boss 的影子玩家 ⇒ **格挡返还给那只 Boss**。
/// 玩家自己打被 debuff 的 Boss 时返还仍然正确（日志里是 `效果追踪 BLK Watcher +3` 这种，给的是玩家）。
///
/// ## 做法
/// 前缀：只在「dealer 解析出来的 Player 是我们的影子玩家」时**接管**（直接完成、不返还任何格挡）
/// —— 卡面原文是「**你**攻击该敌人时获得格挡」，Boss 打 Boss 不算「你」。
/// 其余情况（真玩家、玩家宠物、施加者兜底）**一律 return true 交还原实现**，行为零变化。
///
/// ⚠ B 版（lamali 3747492505）的同名 power 用的是 `AfterAttack(ctx, AttackCommand)`，签名不同，
///   这里暂未覆盖（本项目当前用的是 A 版 `boninall`）；将来切到 B 版要另加一处。
/// </summary>
internal static class WatcherBlockReturnFix
{
    private static readonly string[] TypeNames = { "WatcherMod.BlockReturnPower" };

    private static bool _installed;
    private static bool _hooked;
    private static long _nextTryMs = long.MinValue / 4;
    private static int _logs;

    /// <summary>挂 `SceneTree.process_frame`（主菜单也在跳），游戏一起来就开始找那个 power。</summary>
    internal static void EnsureHooked()
    {
        if (_hooked) return;
        try
        {
            if (Godot.Engine.GetMainLoop() is not Godot.SceneTree tree) return;
            tree.ProcessFrame += TryInstall;
            _hooked = true;
            TryInstall();
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 观者补丁：以手拒之 心跳挂载失败（忽略）: " + e.Message);
        }
    }

    /// <summary>幂等；没找到类型时 5 秒后再试（观者模组可能比我们晚加载）。</summary>
    internal static void TryInstall()
    {
        if (_installed) return;
        long now = Environment.TickCount64;
        if (now < _nextTryMs) return;
        _nextTryMs = now + 5000;

        foreach (var name in TypeNames)
        {
            try
            {
                var type = FindType(name);
                if (type == null) continue;

                // ⚠ 写死参数类型：PowerModel 上 AfterDamageGiven 只有这一个「带 DamageResult」的形态，
                //   但仍按工程硬规矩显式给类型，避免重载解析歧义。
                var method = type.GetMethod("AfterDamageGiven",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[]
                    {
                        typeof(MegaCrit.Sts2.Core.GameActions.Multiplayer.PlayerChoiceContext),
                        typeof(Creature),                                        // dealer
                        typeof(DamageResult),
                        typeof(MegaCrit.Sts2.Core.ValueProps.ValueProp),
                        typeof(Creature),                                        // target
                        typeof(CardModel),
                    },
                    null);
                if (method == null) continue;

                var prefix = new HarmonyMethod(typeof(WatcherBlockReturnFix)
                    .GetMethod(nameof(Prefix), BindingFlags.Static | BindingFlags.NonPublic)!);
                new Harmony("CharacterBosses.watcher.blockreturn").Patch(method, prefix: prefix);

                _installed = true;
                Logger.Log($"[CharBoss] 观者补丁：以手拒之（{type.FullName}.AfterDamageGiven）已挂 —— " +
                           "我们的 Boss（影子玩家）打被上 debuff 的 Boss 时**不再**把格挡返还给 Boss");
            }
            catch (Exception e)
            {
                Logger.Log($"[CharBoss] 观者补丁：以手拒之 {name} 挂载失败（忽略）: {e.Message}");
            }
        }
    }

    private static Type? FindType(string fullName)
    {
        try
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    if (asm.IsDynamic) continue;
                    var t = asm.GetType(fullName, false);
                    if (t != null) return t;
                }
                catch { /* 单个程序集读不到就算了 */ }
            }
        }
        catch { /* 忽略 */ }
        return null;
    }

    /// <summary>
    /// 只有「dealer 是我们的影子玩家（= 一只 Boss / 它的召唤物主人）」时才接管：直接完成、不返还。
    /// 其它情况 return true（原实现照旧：真玩家 / 玩家宠物 / 施加者兜底）。
    /// </summary>
    private static bool Prefix(PowerModel? __instance, Creature? dealer, ref Task __result)
    {
        try
        {
            if (__instance == null || dealer == null) return true;

            var owner = __instance.Owner;
            if (owner == null || ReferenceEquals(dealer, owner)) return true;   // 原实现自己会挡

            var p = PlayerOf(dealer);
            if (p == null || !BossOrbTargeting.IsShadowPlayer(p)) return true;  // 真玩家 / 玩家宠物 ⇒ 原实现

            // 走到这里 = 我们的 Boss（或它名下的召唤物）打了被上 debuff 的另一只 Boss。
            if (_logs++ < 10)
            {
                Logger.Log("[CharBoss] 以手拒之：这次是**我们的 Boss**打中带 debuff 的 Boss ⇒ 不返还格挡" +
                           $"（dealer={SafeName(dealer)}，被上 debuff 的={SafeName(owner)}；" +
                           "原实现会因为 Boss 的 Creature.Player 非空而把格挡返还给那个 Boss）");
            }
            __result = Task.CompletedTask;
            return false;
        }
        catch { return true; }
    }

    /// <summary>原实现的两级解析：`dealer.Player` → `dealer.PetOwner`。</summary>
    private static Player? PlayerOf(Creature dealer)
    {
        try { return dealer.Player ?? dealer.PetOwner; } catch { return null; }
    }

    private static string SafeName(Creature? c)
    {
        try { return c == null ? "<null>" : (c.Name ?? "?"); } catch { return "?"; }
    }
}
#endif
