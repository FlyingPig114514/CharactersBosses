using System;
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

    private static void BeforeSegment(object __instance)
    {
        try
        {
            var power = PowerOf(__instance);
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

            // ⚠ 用**一次性**收窄，不用 Push/Pop 作用域（用户 2026-10-29 四人局实测：
            //    作用域是全局开关，结算期里串到了别人的牌上 ——
            //    `PLAY SOW`（对所有敌人）之后紧跟 `选靶收窄：…只留锁定玩家` ⇒ 播种只打一个人）。
            //
            //    为什么一次性够用：`MoveNext` 每次被调用都会同步跑到下一个 `await` 为止，
            //    而 `NextItem(HittableEnemies)` 就在「等待结束后的那一片」里——
            //    本前缀刚设好、同一片同步代码里立刻就被它自己读走，中间不会插进别人的结算。
            HittableEnemiesSidePatch.RequestNarrowOnce(locked);
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
