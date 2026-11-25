using System;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using CharacterBosses.Monsters;

namespace CharacterBosses.Patches;

/// <summary>
/// **她在自己回合里被击晕时，把击晕推迟到下一回合**（用户 2026-11-24）。
///
/// 用户原话：
/// > 「在 boss 因为拥有失衡（通过收藏家的盛碗虫（石）藏品给予）在回合内击晕时，后续回合都没有意图。
/// >   让 boss 因为在回合内被击晕时，仍打出当回合的所有卡牌，但下回合被击晕」
///
/// 机制（反编译）：
/// <code>
/// // MegaCrit.Sts2.Core.Models.Powers.ImbalancedPower.AfterDamageGiven
/// if (dealer == Owner &amp;&amp; result.WasFullyBlocked) {
///     if (Owner.Monster is BowlbugRock rock) rock.IsOffBalance = true;
///     else await CreatureCmd.Stun(Owner);
/// } else await CreatureCmd.Stun(Owner);
/// </code>
/// 也就是「她打出的伤害被**完全格挡** ⇒ 立刻把**她自己**打晕」。这一下发生在**她的回合中途**
/// （刚打完一张牌），于是：
///   · `Creature.StunInternal` 把移动状态机切到 `STUNNED`，`FollowUpStateId` = 当时状态栈最后一个
///     （= 她正在跑的 `CHARBOSS_PLAY`）⇒ 晕完之后**不会再经过 `CHARBOSS_THINK`**
///     ⇒ `PrepareTurn`（造手牌 / 排计划）**再也不会被调用**；
///   · 而她那回合的牌已经打光（`Plan` 空）⇒ 屏幕上从此没有意图。
///
/// 修法（对用户口径的两条要求一一对应）：
///   1. **本回合照常打完** —— 她在执行自己的回合时收到 `CreatureCmd.Stun`，这里**先压住**，
///      只记一个 <see cref="CharBossMonster.PendingStun"/>；
///   2. **下回合被击晕** —— 回合打完时由 <see cref="CharBossMonster.ApplyPendingStunIfAny"/> 真正补上
///      这一次击晕，并且**显式指定 `nextMoveId = CHARGOSS_THINK`**，让「晕完」之后回到
///      `BossThinkState`（重新规划、重新出意图），不再卡成「没有意图的空手牌」。
/// </summary>
[HarmonyPatch(typeof(CreatureCmd), nameof(CreatureCmd.Stun), new[] { typeof(Creature), typeof(string) })]
public static class BossStunWhileExecutingPatch
{
    private static int _logs;

    [HarmonyPrefix]
    public static bool Prefix(Creature creature, ref string nextMoveId)
    {
        try
        {
            if (creature?.Monster is not CharBossMonster boss) return true;   // 原版怪物 / 玩家：照旧
            if (!ReferenceEquals(CharBossMonster.TurnExecuting, boss)) return true;  // 不在她自己的回合里：照旧

            // 她在自己回合里 ⇒ 压住这次击晕，等她把这回合的牌打完再补
            boss.PendingStun = true;
            if (_logs++ < 20)
            {
                Logger.Log($"[CharBoss] {boss.SafeEntryName()} 在自己回合里被击晕（失衡一类）⇒ " +
                           "本回合照常把牌打完，击晕推迟到她下一个回合");
            }
            return false;
        }
        catch { /* 出错就走原版 */ }
        return true;
    }
}

/// <summary>
/// **失衡（`ImbalancedPower`）生效一次后就移除**（用户 2026-11-24）：
/// > 「储君的失衡没有在生效一次后被『盛碗虫（石）』buff 正常清除」
///
/// 反编译对照（这就是「为什么原版怪物没事、我们的 Boss 会一直挂着」）：
/// <code>
/// // 原版 ImbalancedPower.AfterDamageGiven（无类型特判 —— 它对谁都不移除自己）
/// if (dealer == Owner &amp;&amp; result.WasFullyBlocked) {
///     Flash();
///     if (Owner.Monster is BowlbugRock rock) rock.IsOffBalance = true;   // 原版虫子走「眩晕一手」那条路
///     else await CreatureCmd.Stun(Owner);                                // 其它人：只是击晕，**不移除**
/// }
/// // 崩坠自己的 BowlbugRockPower.AfterDamageGiven（收藏家那份）
/// if (dealer == Owner &amp;&amp; result.WasFullyBlocked) {
///     Flash();
///     if (Owner.Monster is not BowlbugRock) {
///         await PowerCmd.Remove(Owner.GetPower&lt;ImbalancedPower&gt;());   // ← 非虫子：**移除失衡**（再击晕）
///     } else rock.IsOffBalance = true;
/// }
/// </code>
/// 也就是说「生效后移除」这条是**崩坠那份 power 的语义**（收藏家藏品「盛碗虫（石）」的说明也是这么写的），
/// 而它只作用在**持有那只虫子的人**身上；失衡落到我们的 Boss 身上时没人替她移除 ⇒ 层数越叠越高、
/// 每次攻击被格挡都再击晕一次。这里补齐：我们 Boss 身上的失衡**触发一次就整条移除**（与崩坠同口径，
/// 用 `PowerCmd.Remove` 而不是减层，`StackType.Single` 只有一条）。
/// </summary>
[HarmonyPatch(typeof(MegaCrit.Sts2.Core.Models.Powers.ImbalancedPower), nameof(MegaCrit.Sts2.Core.Models.Powers.ImbalancedPower.AfterDamageGiven))]
public static class BossImbalancedOncePatch
{
    private static int _logs;

    [HarmonyPostfix]
    public static void Postfix(MegaCrit.Sts2.Core.Models.Powers.ImbalancedPower __instance,
                               Creature? dealer,
                               MegaCrit.Sts2.Core.Entities.Creatures.DamageResult result)
    {
        try
        {
            var owner = __instance?.Owner;
            if (owner?.Monster is not CharBossMonster boss) return;      // 只管我们的 Boss
            if (!ReferenceEquals(dealer, boss.Creature)) return;         // 只认「她自己打出的伤害」
            if (!result.WasFullyBlocked) return;                        // 只认「被完全格挡」那一下

            var power = boss.Creature?.GetPower<MegaCrit.Sts2.Core.Models.Powers.ImbalancedPower>();
            if (power == null) return;
            _ = MegaCrit.Sts2.Core.Commands.PowerCmd.Remove(power);
            if (_logs++ < 20)
            {
                Logger.Log($"[CharBoss] {boss.SafeEntryName()} 的「失衡」生效（攻击被完全格挡）⇒ 按崩坠口径整条移除");
            }
        }
        catch (Exception e)
        {
            if (_logs++ < 5) Logger.Log("[CharBoss] 移除失衡失败（忽略）: " + e.Message);
        }
    }
}