using System;
using System.Collections.Generic;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using CharacterBosses.Monsters;

namespace CharacterBosses.Patches;

/// <summary>
/// 「万向斩」（<c>Omnislice</c>）的**第二段（溅射）也要无视玩家的奥斯提/召唤物**。
///
/// 用户 2026-11-04：<i>「我之前就让奥斯提不会受到群伤，这个万向斩虽然不是群伤但也差不多，让它无视奥斯提。」</i>
///
/// 已有的两条「她的攻击只打真人玩家」闸门都拦不住这一段：
///   · `HittableEnemiesSidePatch`（`CombatState.HittableEnemies`）在**她出牌期间**只返回真人玩家；
///   · `BossAttackRealPlayersOnlyPatch`（`AttackCommand.GetPossibleTargets`）剔掉非真人。
/// 而 `Omnislice.OnPlay` 的第二段是这么写的（反编译）：
/// <code>
/// var teammates = combatState.GetTeammatesOf(// 第一段实际挨打的那位//)
///                      .Except(new[] { cardPlay.Target })
///                      .Where(e =&gt; e.IsHittable);
/// await CreatureCmd.Damage(ctx, teammates, dealt + overkill, ValueProp.Unpowered | ValueProp.Move, owner, this, cardPlay);
/// </code>
/// 它**既不走 `HittableEnemies`、也不走 `AttackCommand`**，直接拿着一份「同侧生物列表」调
/// <c>CreatureCmd.Damage</c> ⇒ 单人局里那份列表就是「你 + 你的奥斯提」。
/// 后果（用户实测）：万向斩 12 点打在你的奥斯提身上（第一段被「为你而死」改指向它），
/// 溅射又打它一份（第一段实伤 + 溢出）⇒ **它一条命顶不住两下**，紧接着的王者之拳就直接落在你身上。
///
/// 这里只拦「她的万向斩 + 溅射那一段（`Unpowered|Move`）+ 目标列表里混进了非真人」这一种情况，
/// 把列表过滤成真人玩家；其它任何伤害（玩家自己打牌、原版怪物、她别的牌、第一段单体）一个字都不碰。
/// </summary>
[HarmonyPatch]
public static class OmnisliceSplashRealPlayersOnlyPatch
{
    private static int _logged;

    /// <summary>两版重载不同 ⇒ 运行时按参数个数挑（见 <see cref="DamageOverload"/>）。</summary>
    [HarmonyPrepare]
    public static bool Prepare() => TargetMethod() != null;

    internal static System.Reflection.MethodBase? TargetMethod() => DamageOverload.Find();

    [HarmonyPrefix]
    public static void Prefix(object[] __args)
    {
        try
        {
            if (__args == null || __args.Length < 5) return;
            var targets = __args[1] as IEnumerable<Creature>;
            if (targets == null) return;
            var props = (ValueProp)__args[3];
            var dealer = __args[4] as Creature;
            var cardSource = DamageOverload.CardSource(__args);
            // 只认「万向斩的溅射那一段」：`Unpowered | Move`（第一段是纯 `Move`，而且走的是单体重载）
            if (!props.HasFlag(ValueProp.Unpowered)) return;
            if (!props.HasFlag(ValueProp.Move)) return;
            if (dealer?.Monster is not CharBossMonster) return;
            string entry = DamageOverload.PlayingEntryOrCard(__args);
            if (entry != "OMNISLICE") return;

            var only = new List<Creature>();
            int removed = 0;
            foreach (var creature in targets)
            {
                if (creature == null) continue;
                bool realPlayer = creature.Monster == null && creature.Player != null;
                if (realPlayer)
                {
                    bool pet = false;
                    try { pet = creature.IsPet; } catch { /* 忽略 */ }
                    if (pet) realPlayer = false;
                }
                if (realPlayer) only.Add(creature);
                else removed++;
            }
            if (removed == 0) return;                        // 本来就只有真人：不动

            if (_logged++ < 8)
            {
                Logger.Log($"[CharBoss] 万向斩溅射：剔掉 {removed} 个召唤物/宠物（含玩家的奥斯提）" +
                           $"⇒ 只溅射 {only.Count} 名真人玩家");
            }
            // 列表可能被清空：`CreatureCmd.Damage` 收到空列表只会返回空结果，不会抛
            //（所以这里不需要动 `__result`，也就不碰 async 那条铁律）。
            __args[1] = only;
        }
        catch { /* 绝不影响战斗 */ }
    }
}
