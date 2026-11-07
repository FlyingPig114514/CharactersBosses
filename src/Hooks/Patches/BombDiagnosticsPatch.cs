using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models.Powers;

namespace CharacterBosses.Patches;

/// <summary>
/// **炸弹诊断日志**（用户 2026-10-29：「储君第二回合只打了 1 个炸弹，怎么第四回合炸了两个？」）。
///
/// 不改变任何行为，只在她自己阵营**回合结束**时（原版 `TheBombPower.BeforeSideTurnEnd`
/// 就是在这里倒计时/爆炸）把当时场上的炸弹清点一遍：
/// <code>
/// 炸弹巡检：她身上 2 个炸弹实例，层数 [1,2]，威力 [40,50] → 本次结算会处理「层数=1」的那个
/// </code>
/// 这样下一次出问题就能一眼分清是：
///   · 真的有**两个实例**都走到 1 层（＝第二次施加/重复出牌），还是
///   · 只有一个实例爆炸、屏幕上多出来的烟雾是 `NFireSmokePuffVfx` **按目标各放一次**的观感。
///
/// 为什么用前缀而不是写进我们的 UI：`TheBombPower` 是原版 power，倒计时完全由它自己结算，
/// 只有挂在这里才能读到**结算那一刻**的真实层数。
/// </summary>
[HarmonyPatch(typeof(TheBombPower), nameof(TheBombPower.BeforeSideTurnEnd))]
internal static class BombDiagnosticsPatch
{
    private static readonly HashSet<string> _logged = new(StringComparer.Ordinal);

    [HarmonyPrefix]
    public static void Prefix(TheBombPower __instance, IEnumerable<Creature> participants)
    {
        try
        {
            var owner = __instance?.Owner;
            if (owner == null || participants == null) return;

            // 原版只在「持有者所在阵营的回合结束」时真的结算；不是就什么都不用记
            bool ownersSide = false;
            foreach (var c in participants)
            {
                if (c == null) continue;
                if (ReferenceEquals(c, owner)) { ownersSide = true; break; }
            }
            if (!ownersSide) return;

            var instances = owner.Powers?.OfType<TheBombPower>().ToList();
            if (instances == null || instances.Count == 0) return;

            var amounts = new List<string>();
            var damages = new List<string>();
            bool anyExploding = false;
            foreach (var bomb in instances)
            {
                int amount = 0;
                try { amount = bomb.Amount; } catch { /* 忽略 */ }
                decimal damage = 0m;
                try { damage = bomb.DynamicVars.Damage.BaseValue; } catch { /* 忽略 */ }
                amounts.Add(amount.ToString());
                damages.Add(damage.ToString("0.##"));
                if (amount <= 1) anyExploding = true;
            }

            // 只在「有实例要爆」或「实例数 ≥ 2」时刷日志，避免每回合刷屏
            if (!anyExploding && instances.Count < 2) return;

            string key = string.Join(",", amounts) + "|" + instances.Count;
            bool first = _logged.Add(key);

            Logger.Log($"[CharBoss] 炸弹巡检：她身上 {instances.Count} 个炸弹实例，" +
                       $"层数 [{string.Join(",", amounts)}]，威力 [{string.Join(",", damages)}]" +
                       $" ⇒ {(anyExploding ? "本次结算**会爆炸**（层数 ≤ 1 的那些）" : "本次只倒计时")}" +
                       (first ? "" : "（重复状态，已省略后续同类）"));
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 炸弹巡检失败（忽略）: " + e.Message);
        }
    }
}
