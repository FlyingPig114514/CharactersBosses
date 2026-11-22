using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using CharacterBosses.Monsters;

namespace CharacterBosses.Patches;

/// <summary>
/// 群蛇形态（<c>SerpentFormPower</c>）在多人的**跟踪锁定**。
///
/// 用户 2026-10-26：「多人时，让群蛇形态的伤害也受跟踪锁定影响，只攻击被锁定目标」。
///
/// 原版实现（反编译 `SerpentFormPower.&lt;AfterCardPlayed&gt;d__13.MoveNext`）：
/// <code>
/// if (cardPlay.Card.Owner == serpentFormPower.Owner.Player
///     &amp;&amp; amountsForPlayedCards.Remove(cardPlay.Card, out damage) &amp;&amp; damage &gt; 0)
/// {
///     await Cmd.CustomScaledWait(0.1f, 0.2f);
///     Creature creature = serpentFormPower.Owner.Player.RunState.Rng.CombatTargets
///                              .NextItem(serpentFormPower.Owner.CombatState.HittableEnemies);
///     if (creature != null)
///         await CreatureCmd.Damage(choiceContext, creature, damage, ValueProp.Unpowered, serpentFormPower.Owner);
/// }
/// </code>
/// 也就是**在所有可命中敌人里随机挑一个** —— 多人时就是「随机挑一名玩家」，等于无视跟踪锁定。
///
/// 做法：给这条状态机的 `MoveNext` 挂前缀，在「她的群蛇形态正在结算」这段里把
/// <c>HittableEnemies</c> 收窄成**这只 Boss 锁定的那位玩家**
/// （<see cref="HittableEnemiesSidePatch.RequestNarrowOnce"/>，一次性、两帧内有效）。
/// 单人时收窄结果就是那个唯一的玩家 ⇒ 行为与原来完全一致（零回归）。
///
/// ⚠ 这里**不改伤害、不改次数**：只影响「随机挑到谁」。
/// </summary>
internal static class SerpentFormLockOnPatch
{
    private static int _hits;
    private static bool _installed;

    internal static void TryInstall()
    {
        if (_installed) return;
        _installed = true;

        try
        {
            var powerType = typeof(MegaCrit.Sts2.Core.Models.Powers.SerpentFormPower);
            var prefix = new HarmonyMethod(typeof(SerpentFormLockOnPatch)
                .GetMethod(nameof(BeforeSegment), BindingFlags.Static | BindingFlags.NonPublic)!);

            var harmony = new Harmony("CharacterBosses.serpentlockon");
            int patched = 0;
            foreach (var nested in powerType.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (nested.Name.IndexOf("AfterCardPlayed", StringComparison.Ordinal) < 0) continue;
                var moveNext = nested.GetMethod("MoveNext",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (moveNext == null) continue;
                harmony.Patch(moveNext, prefix: prefix);
                patched++;
            }

            Logger.Log(patched > 0
                ? $"[CharBoss] 群蛇形态：已挂 {patched} 个状态机（多人时随机目标改为「只打被锁定的那位玩家」）"
                : "[CharBoss] 群蛇形态：没找到 AfterCardPlayed 状态机 ⇒ 跟踪锁定未挂（版本不符？）");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 群蛇形态跟踪锁定挂载失败（忽略）: " + e.Message);
        }
    }

    private static System.Collections.IDictionary? PendingDamageDict(object? power)
    {
        try
        {
            var fld = power?.GetType().GetField("amountsForPlayedCards",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            return fld?.GetValue(power) as System.Collections.IDictionary;
        }
        catch { return null; }
    }

    private static bool IsHittable(CharBossMonster boss, Creature? target)
    {
        try
        {
            if (target == null) return false;
            foreach (var c in boss.CombatState.HittableEnemies)
                if (ReferenceEquals(c, target)) return true;
        }
        catch { /* 忽略 */ }
        return false;
    }

    private static Creature? FirstHittablePlayer(CharBossMonster boss)
    {
        try
        {
            foreach (var p in Core.BossLockOn.RealPlayers(boss.CombatState))
            {
                var c = p?.Creature;
                if (c == null || !c.IsAlive) continue;
                if (IsHittable(boss, c)) return c;
            }
        }
        catch { /* 忽略 */ }
        return null;
    }
    private static void BeforeSegment(object __instance)
    {
        try
        {
            if (_hits++ < 20)
            {
                try
                {
                    var pw = PowerOf(__instance) as MegaCrit.Sts2.Core.Models.Powers.SerpentFormPower;
                    var bs = pw?.Owner?.Monster as CharBossMonster;
                    string lk = bs == null ? "-" : (Core.BossLockOn.TargetFor(bs)?.Creature?.Name ?? "<空>");
                    int hit = 0;
                    try { if (bs != null) foreach (var c in bs.CombatState.HittableEnemies) hit++; } catch { /* 忽略 */ }
                    Logger.Log($"[CharBoss] 群蛇形态：结算开始（她有={bs != null} 锁定={lk} 可命中={hit}）");
                }
                catch { /* 纯诊断 */ }
            }
            var power = PowerOf(__instance);
            // ★ v0.2.885：**只在还真的有待结算伤害时**才收窄。
            //   这条状态机每次 `MoveNext` 都会进前缀，包括「伤害已打完」之后的收尾帧；
            //   窗口放宽到 8 帧后，那些收尾帧再设一次收窄就可能串到同一瞬间别人的随机目标上。
            //   判据：`SerpentFormPower.amountsForPlayedCards` 里还有条目（反编译字段名）。
            try
            {
                var fld = power?.GetType().GetField("amountsForPlayedCards",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                if (fld != null)
                {
                    var dict = fld.GetValue(power) as System.Collections.ICollection;
                    if (dict != null && dict.Count == 0) return;     // 没有待结算伤害 ⇒ 不设收窄
                }
            }
            catch { /* 反射失败就照旧收窄 */ }
            if (power == null) return;

            // 只有「她自己的群蛇形态」才收窄（玩家的群蛇形态保持原版随机）
            var boss = power.Owner?.Monster as CharBossMonster;
            if (boss == null) return;

            // ① 首选「她锁定的那位玩家」
            var locked = Core.BossLockOn.TargetFor(boss)?.Creature;

            // ② 兜底（用户 2026-10-27 让核对「会不会打到史莱姆/奥斯提/火炬头」时补的）：
            //    锁定目标缺失（还没锁 / 那位已经死了）时**不要**退回去让它走原版随机 ——
            //    那样 `HittableEnemies` 会退回「敌方阵营」（= 她自己那一侧），
            //    随机就可能挑到同侧的别的生物（她自己、她的奥斯提、同场其它 Boss/小怪）。
            //    改成收窄到「任意一位活着的真人玩家」，语义仍然是「只打玩家」。
            if (locked == null || !locked.IsAlive)
            {
                try
                {
                    foreach (var p in Core.BossLockOn.RealPlayers(boss.CombatState))
                    {
                        if (p?.Creature == null || !p.Creature.IsAlive) continue;
                        locked = p.Creature;
                        break;
                    }
                }
                catch { /* 忽略 */ }
            }
            if (locked == null || !locked.IsAlive) return;

            // ★ v0.2.884（用户 2026-11-22：「群蛇形态依旧没修好」+ 日志实证 16:21:16
            //   `群蛇形态：锁定的玩家与其它玩家都不可命中 ⇒ 本次跳过`）：
            //   v0.2.879 我加的「先问 IsHittable、不可命中就跳过」是**错的** ——
            //   `CombatState.HittableEnemies` 在这条状态机跑的瞬间可能已经切回「Boss 自己那一侧」
            //   （原版实现是 `Enemies.Where(IsHittable)`，我们只是按阵营改写），
            //   于是「一个玩家都不在里面」⇒ 整次伤害被跳过 ⇒ 就是「完全不掉血」。
            //   ⇒ 这里恢复成**无条件只打锁定的那位玩家**（原设计），只在排查时记一行。
            if (_hits < 20)
            {
                try
                {
                    Logger.Log($"[CharBoss] 群蛇形态：收窄到锁定玩家 {locked.Name}（不做可命中预判）");
                }
                catch { /* 纯诊断 */ }
            }
            // ★ v0.2.890（玩家反馈：修好「球不打自己」后变成**游戏卡死**）：
            //   收窄是「全局替换 `HittableEnemies`」，窗口一旦落到玩家自己的充能球被动上，
            //   玩家的球就会拿到一张只有一个目标的列表 ⇒ 与球被动/选靶相关的那几条管线卡住。
            //   既然下面已经**自行结算**（不再依赖随机列表），收窄就没必要了 ⇒ 整条去掉。
            // HittableEnemiesSidePatch.RequestNarrowOnce(locked);   // v0.2.890 停用

            // ★ v0.2.888（用户 2026-11-22 多轮反馈「还是不打被锁定的玩家」）：
            //   不再只靠「收窄 + 原版随机」这条路 —— 收窄一旦因任何原因没被读走，
            //   原版那句 `NextItem` 拿到 null 就是**静默零伤害**，几乎无法察觉。
            //   现在改成**自己结算**：把 `SerpentFormPower.amountsForPlayedCards` 里待结算的伤害
            //   取出来（顺带从表里摘掉 ⇒ 原版 `Remove` 返回 false，不会再打一次，避免双份），
            //   然后直接对**锁定的那位玩家** `CreatureCmd.Damage`。
            //   两端确定性：目标取自 `BossLockOn.TargetFor`、伤害取自同一张待结算表，
            //   两端跑同一份代码 ⇒ 结果一致。
            try
            {
                var dict = PendingDamageDict(power);
                if (dict != null && dict.Count > 0)
                {
                    decimal total = 0m;
                    var keys = new List<object>();
                    foreach (System.Collections.DictionaryEntry e in dict) keys.Add(e.Key);
                    foreach (var k in keys)
                    {
                        try
                        {
                            var v = dict[k];
                            if (v is int iv && iv > 0) total += iv;
                            dict.Remove(k);
                        }
                        catch { /* 忽略 */ }
                    }

                    if (total > 0m)
                    {
                        Logger.Log($"[CharBoss] 群蛇形态：自行结算 {total:0} 点无来源伤害 → {locked.Name}" +
                                   "（不再依赖 HittableEnemies 随机挑目标）");
                        var ctx = new MegaCrit.Sts2.Core.GameActions.Multiplayer.BlockingPlayerChoiceContext();
                        MegaCrit.Sts2.Core.Helpers.TaskHelper.RunSafely(
                            MegaCrit.Sts2.Core.Commands.CreatureCmd.Damage(
                                ctx, locked, total, MegaCrit.Sts2.Core.ValueProps.ValueProp.Unpowered, power.Owner));
                    }
                }
            }
            catch (Exception e) { Logger.Log("[CharBoss] 群蛇形态自行结算失败（退回原版随机）: " + e.Message); }
        }
        catch { /* 忽略：出问题就按原版随机 */ }
    }

    /// <summary>状态机结构体里指回 power 的字段（`&lt;&gt;4__this`）。</summary>
    private static MegaCrit.Sts2.Core.Models.PowerModel? PowerOf(object? stateMachine)
    {
        try
        {
            if (stateMachine == null) return null;
            var field = stateMachine.GetType().GetField("<>4__this",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return field?.GetValue(stateMachine) as MegaCrit.Sts2.Core.Models.PowerModel;
        }
        catch { return null; }
    }
}
