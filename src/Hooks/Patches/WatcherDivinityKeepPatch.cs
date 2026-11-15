#if STS2_111   // 观者 Boss 只在 0.113 构建里（用户：107 不加这个 Boss）
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using CharacterBosses.Powers;

namespace CharacterBosses.Patches;

/// <summary>
/// **神格观进了神格之后就不再退出**（用户 2026-11-15）：
/// > 「而且她不再会在回合开始时退出神格。」
///
/// 反编译观者模组 `WatcherMod.Divinity`（303 行那份）：它有一个 `AfterSideTurnEnd` 的 async 状态机
/// `<AfterSideTurnEnd>d__11`，逻辑就两句：
/// <code>
/// if (side == Owner.Side &amp;&amp; Owner.Player != null)
///     await WatcherCombatHelper.ExitStance(Owner.Player);     // ← 到点自动退神格
/// </code>
/// 所以只要把这条钩子在我们这只 Boss 身上压掉，神格就会一直挂着（观者原版玩家的行为完全不变）。
///
/// 实现方式：不引用观者的类型（铁律），而是补**本体**的 `PowerModel.AfterSideTurnEnd`，
/// 在里面对 `__instance.GetType().Name == "Divinity"` 且「owner 是我们的 Boss 的影子玩家」
/// 且「她身上有我们的虔信 Power」这三种条件同时成立时才短路。
/// </summary>
[HarmonyPatch(typeof(PowerModel), nameof(PowerModel.AfterSideTurnEnd))]
internal static class WatcherDivinityKeepPatch
{
    private static int _logs;

    [HarmonyPrefix]
    public static bool Prefix(PowerModel __instance, PlayerChoiceContext choiceContext,
                              CombatSide side, IEnumerable<Creature> participants, ref Task __result)
    {
        try
        {
            if (__instance == null) return true;
            if (!string.Equals(__instance.GetType().Name, "Divinity", StringComparison.Ordinal)) return true;

            Creature? owner = null;
            try { owner = __instance.Owner; } catch { return true; }
            if (owner == null) return true;

            var boss = OurBossOwning(owner);
            if (boss == null) return true;

            // 只有挂着「虔信」的这只才免退（别的场合一律原样）
            try { if (boss.Creature?.GetPower<BossDevotionPower>() == null) return true; }
            catch { return true; }

            __result = Task.CompletedTask;
            if (_logs++ < 3)
                Logger.Log("[CharBoss] 神格观：她进了神格 ⇒ 压掉原版的「回合结束退神格」");
            return false;
        }
        catch { return true; }
    }

    /// <summary>这个生物是不是某只我们的 Boss 的影子玩家（观者的状态挂在影子玩家身上）。</summary>
    private static Monsters.CharBossMonster? OurBossOwning(Creature owner)
    {
        try
        {
            var enemies = owner.CombatState?.Enemies;
            if (enemies == null) return null;
            foreach (var c in enemies)
            {
                if (c?.Monster is not Monsters.CharBossMonster boss) continue;
                var shadowCreature = boss.Shadow?.Creature;
                if (shadowCreature != null && ReferenceEquals(shadowCreature, owner)) return boss;
            }
        }
        catch { /* 忽略 */ }
        return null;
    }
}
#endif
