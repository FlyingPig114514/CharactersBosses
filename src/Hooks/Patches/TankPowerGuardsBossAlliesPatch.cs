#if STS2_111
using System;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models.Powers;
using CharacterBosses.Monsters;

namespace CharacterBosses.Patches;

/// <summary>
/// 「**肉盾**」（`Tank`）真的能给我们**其它 Boss** 减伤。
///
/// 用户 2026-11-19：
/// > 「多人时将第二回合的时候未到替换为肉盾，**保证他能正常给其它 boss 减伤**」
///
/// 反编译原版：
/// <code>
/// // Tank.OnPlay：给自己上 TankPower
/// await PowerCmd.Apply&lt;TankPower&gt;(ctx, Owner.Creature, 1m, Owner.Creature, this);
///
/// // TankPower.AfterApplied：给「队友」发 GuardedPower（受到的伤害 ×0.5）
/// foreach (Creature item in CombatState.GetTeammatesOf(Owner))
///     if (item.IsAlive &amp;&amp; **item.IsPlayer** &amp;&amp; item != Owner)
///         await PowerCmd.Apply&lt;GuardedPower&gt;(ctx, item, Amount, Owner, null);
/// </code>
/// 问题是那句 `item.IsPlayer`：我们的**其它 Boss 是敌人侧的生物**（`CharBossMonster`），
/// 不是玩家生物 ⇒ 原版这条循环一个都不发，肉盾对其它 Boss **完全没有减伤效果**。
///
/// 这里补一段后缀：当这张牌是**我们 Boss 的影子玩家**打出来的时候，
/// 给同场其它活着的 `CharBossMonster` 各发一份同样层数的 `GuardedPower`。
/// 原版那条循环照旧（对玩家队友仍然生效，我们只是**追加** Boss 队友）。
///
/// ⚠ 只认「持有者属于我们某只 Boss 的影子玩家」：真人玩家自己打卡「肉盾」时
///   一个字节都不改（否则玩家开肉盾会顺手给对面的 Boss 减伤）。
/// </summary>
[HarmonyPatch(typeof(TankPower), nameof(TankPower.AfterApplied))]
internal static class TankPowerGuardsBossAlliesPatch
{
    private static int _logs;

    [HarmonyPostfix]
    public static void Postfix(TankPower __instance)
    {
        try
        {
            var owner = __instance?.Owner;
            if (owner == null) return;

            var combat = owner.CombatState;
            if (combat == null) return;

            if (!OwnerIsOurBossShadow(owner, combat)) return;      // 玩家自己打的肉盾：不动

            int amount = Math.Max(1, (int)Math.Round((double)__instance.Amount));
            int given = 0;
            foreach (var list in new[] { combat.Enemies, combat.Allies })
            {
                if (list == null) continue;
                foreach (var mate in list)
                {
                    if (mate == null || !mate.IsAlive) continue;
                    if (ReferenceEquals(mate, owner)) continue;
                    if (mate.Monster is not CharBossMonster) continue;
                    if (mate.GetPower<GuardedPower>() != null) continue;   // 已经有就不叠

                    TaskHelper.RunSafely(PowerCmd.Apply<GuardedPower>(
                        new ThrowingPlayerChoiceContext(), mate, (decimal)amount, owner, null, false));
                    given++;
                    if (_logs++ < 20)
                        Logger.Log($"[CharBoss] 肉盾：{owner.Name} 打出「肉盾」⇒ 友方 Boss {mate.Name} " +
                                   $"获得 {amount} 层「守护」（受到的伤害 ×0.5）");
                }
            }

            if (given > 0 && _logs++ < 20)
                Logger.Log($"[CharBoss] 肉盾：本场共给 {given} 只友方 Boss 补上「守护」" +
                           "（原版只发给玩家队友，我们的 Boss 不是玩家生物 ⇒ 必须在这里补）");
        }
        catch (Exception e) { Logger.Log("[CharBoss] 肉盾·友方 Boss 减伤失败（不影响结算）: " + e.Message); }
    }

    /// <summary>这个生物是不是**我们某只 Boss 的影子玩家**的生物。</summary>
    private static bool OwnerIsOurBossShadow(Creature owner, MegaCrit.Sts2.Core.Combat.ICombatState combat)
    {
        try
        {
            var shadow = owner.Player;
            if (shadow == null) return false;

            foreach (var list in new[] { combat.Enemies, combat.Allies })
            {
                if (list == null) continue;
                foreach (var c in list)
                {
                    if (c?.Monster is CharBossMonster boss && ReferenceEquals(boss.Shadow, shadow)) return true;
                }
            }
        }
        catch { /* 忽略 */ }
        return false;
    }
}
#endif
