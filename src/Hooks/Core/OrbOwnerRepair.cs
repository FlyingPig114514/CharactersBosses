using System;
using MegaCrit.Sts2.Core.Entities.Orbs;
using MegaCrit.Sts2.Core.Entities.Players;
using CharacterBosses.Monsters;

namespace CharacterBosses.Core;

/// <summary>
/// **给她的充能球补回主人**（`OrbModel.Owner == null` ⇒ 原版那句 `Owner.Creature.CombatState` 会空引用）。
///
/// 反编译（0.111 / 0.113 一致）：
/// <code>
/// public abstract class OrbModel : AbstractModel {
///     private Player? _owner;
///     public Player Owner { get => _owner; set { ...按原样赋值... } }
///     protected decimal ModifyOrbValue(decimal result)
///         =&gt; Hook.ModifyOrbValue(Owner.Creature.CombatState, this, result);   // ← Owner 为空就 NRE
/// }
/// // LightningOrb
/// public override decimal PassiveVal =&gt; ModifyOrbValue(3m);
/// public override decimal EvokeVal  =&gt; ModifyOrbValue(8m);
/// private async Task&lt;IEnumerable&lt;Creature&gt;&gt; ApplyLightningDamage(...)
///     =&gt; base.CombatState.GetOpponentsOf(base.Owner.Creature) ...                    // ← 同上
///</code>
/// 也就是说「球的 Owner 丢了」不只是显示问题：**激发/被动那一下真的会空引用**，
/// 而那一句跑在 Boss 回合的协程里 —— 抛出来就可能让整场战斗停住（我们只能看到「⚠ 战斗 N 秒没有推进」）
/// 或者直接把进程带走（玩家 2026-11-22 报的「亡命之徒打电球机崩溃」）。
///
/// 玩家日志里的**canary**：那一局 16 分钟出现 **63 次**
/// `evoke value failed: Object reference not set to an instance of an object.`
/// —— 我们自己的预览代码读 `EvokeVal` 时先炸了，说明当时确实有「没有主人」的球。
///
/// 做法：4Hz 慢车道里扫一遍我们 Boss 的球队列，`Owner` 为空的就补回她的影子玩家
/// （`OrbModel.Owner` 的 setter 只在「已有主人且想换成别人」时才抛，**从空补上永远合法**）。
/// 纯修复、不改数值；补不上（模型已冻结等）就安静跳过。
/// </summary>
internal static class OrbOwnerRepair
{
    private static int _logs;

    /// <summary>扫一只 Boss 的球队列（幂等，便宜；由 4Hz 慢车道调用）。</summary>
    internal static void Sweep(CharBossMonster? boss)
    {
        try
        {
            var shadow = boss?.Shadow;
            if (shadow == null) return;

            var orbs = shadow.PlayerCombatState?.OrbQueue?.Orbs;
            if (orbs == null || orbs.Count == 0) return;

            foreach (var orb in orbs)
            {
                if (orb == null) continue;

                Player? owner;
                try { owner = orb.Owner; } catch { continue; }
                if (owner != null) continue;          // 有主人 ⇒ 什么都不做

                try
                {
                    orb.Owner = shadow;
                    if (_logs++ < 20)
                    {
                        Logger.Log($"[CharBoss] 充能球补主人：{boss!.Id.Entry} 的一颗 {orb.GetType().Name} 没有 Owner " +
                                   "⇒ 补成她的影子玩家（否则 EvokeVal / 原版 Evoke 会空引用）");
                    }
                }
                catch { /* 冻结/结算中等情况：补不上就算了，绝不打断战斗 */ }
            }
        }
        catch { /* 纯修复，任何意外都不影响战斗 */ }
    }
}
