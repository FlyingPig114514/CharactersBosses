#if STS2_111   // 观者 Boss 只在 0.113 构建里（107 不加）
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;

namespace CharacterBosses.Patches;

/// <summary>
/// **lamali 版观者（3747492505）的「许愿」也固定选「永生不死」**（用户 2026-11-15）。
///
/// 两份实现的「许愿」走的是完全不同的选牌入口：
///   · Boninall 版（3747526116）：`WatcherMod.WatcherCombatHelper.ChooseOne(...)`
///     ⇒ 已由 <see cref="Core.WatcherRuntimePatches"/> 在运行期挂 prefix 处理；
///   · lamali 版：反编译 `Watcher.Code.Cards.Rare.WishWatcher`，它直接调**本体的**
///     <code>CardSelectCmd.FromChooseACardScreen(choiceContext, options, owner, false)</code>，
///     候选项是 `BecomeAlmighty` / `LiveForever`（永生不死）/ `FameAndFortune` 三张 token 牌。
///
/// 所以这里补**本体**的方法（可以编译期挂，不需要反射）：候选项里出现 `LiveForever`
/// 且 owner 是我们某只 Boss 的影子玩家 ⇒ 直接返回它。别的卡（本体与其它模组大量调用它）一律不动。
/// </summary>
[HarmonyPatch(typeof(CardSelectCmd), nameof(CardSelectCmd.FromChooseACardScreen))]
internal static class WatcherWishLowLevelChoicePatch
{
    private static int _logs;

    [HarmonyPrefix]
    public static bool Prefix(PlayerChoiceContext choiceContext,
                             IReadOnlyList<CardModel> cards,
                             Player player,
                             bool cancelable,
                             ref Task<CardModel> __result)
    {
        try
        {
            if (cards == null || cards.Count == 0) return true;
            if (!IsOurBossShadow(player)) return true;

            CardModel? live = null;
            foreach (var c in cards)
            {
                if (c == null) continue;
                string name;
                try { name = c.GetType().Name; } catch { continue; }
                // lamali 版叫 `LiveForever`；Boninall 版那张 token 叫 `WatcherWishLiveForever`
                if (string.Equals(name, "LiveForever", StringComparison.Ordinal)
                    || string.Equals(name, "WatcherWishLiveForever", StringComparison.Ordinal))
                { live = c; break; }
            }
            if (live == null) return true;      // 不是「许愿」的三选一 ⇒ 原样

            __result = Task.FromResult(live);
            if (_logs++ < 4) Logger.Log($"[CharBoss] 许愿（低层选牌）：替她选了「永生不死」（{live.GetType().Name}）");
            return false;
        }
        catch { return true; }
    }

    private static bool IsOurBossShadow(Player? owner)
    {
        try
        {
            if (owner == null) return false;
            var enemies = owner.Creature?.CombatState?.Enemies;
            if (enemies == null) return false;
            foreach (var c in enemies)
            {
                if (c?.Monster is not Monsters.CharBossMonster boss) continue;
                if (ReferenceEquals(boss.Shadow, owner)) return true;
            }
        }
        catch { /* 忽略 */ }
        return false;
    }
}
#endif
