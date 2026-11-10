using System;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using CharacterBosses.Monsters;

namespace CharacterBosses.Patches;

/// <summary>
/// **诊断用**：查「玩家给我们的 Boss 上负面时，头上不浮图标、也没有施加音效」。
///
/// 反编译 `NCreature.OnPowerIncreased(power, amount, silent)` 的第一行就是
/// <code>
/// if (silent || !CombatManager.Instance.IsInProgress) return;   // ← 图标 + 音效一起没了
/// ...
/// NPowerAppliedVfx vfx = NPowerAppliedVfx.Create(power, amount, flag);   // 图标
/// if (power.ShouldPlayVfx) SfxCmd.Play(flag ? "event:/sfx/buff" : "event:/sfx/debuff");   // 音效
/// </code>
/// 所以「图标和音效同时没有」只可能是这两种之一：
///   · `silent == true`（两样都没）；
///   · `ShouldPlayVfx == false`（只有音效没）⇒ 而它是
///     `Owner.IsAlive &amp;&amp; CombatManager.IsInProgress &amp;&amp; IsVisible`，
///     `IsVisible` = `Target == null || LocalContext.IsMe(Target) || Target.IsEnemy`。
///
/// 这里在 `PowerCmd.Apply` 的**非泛型重载**（泛型都汇到这里）上挂一行日志，
/// 只对「目标是 CharacterBosses 的 Boss」打印这几个输入，重现一次就能看出是哪一条被掐了。
/// 确认原因后本文件可以删掉。
/// </summary>
[HarmonyPatch(typeof(PowerCmd), nameof(PowerCmd.Apply), new[]
{
    typeof(PlayerChoiceContext), typeof(PowerModel), typeof(Creature),
    typeof(decimal), typeof(Creature), typeof(CardModel), typeof(bool),
})]
internal static class BossPowerApplyTracePatch
{
    private static int _logs;

    [HarmonyPrefix]
    private static void Prefix(PowerModel power, Creature target, decimal amount, Creature? applier, bool silent)
    {
        try
        {
            if (power == null || target == null) return;
            if (target.Monster is not CharBossMonster) return;      // 只看「落在她身上」的
            if (_logs++ > 40) return;

            string inProgress = "?";
            try { inProgress = MegaCrit.Sts2.Core.Combat.CombatManager.Instance.IsInProgress.ToString(); }
            catch { inProgress = "抛异常"; }

            string shouldVfx = "?";
            try { shouldVfx = power.ShouldPlayVfx.ToString(); } catch { shouldVfx = "抛异常"; }

            string targetIsMe = "?";
            try { targetIsMe = MegaCrit.Sts2.Core.Context.LocalContext.IsMe(target).ToString(); } catch { targetIsMe = "抛异常"; }

            string entry = "?";
            try { entry = power.Id.Entry; } catch { /* 忽略 */ }

            Logger.Log($"[CharBoss] PowerApply 诊断：{entry} {amount:0.#} → {target.Name}" +
                       $"（施加者={(applier == null ? "null" : applier.Name)}，silent={silent}，" +
                       $"IsInProgress={inProgress}，ShouldPlayVfx={shouldVfx}，IsMe={targetIsMe}，" +
                       $"Side={target.Side}）");
        }
        catch { /* 诊断绝不影响战斗 */ }
    }
}
