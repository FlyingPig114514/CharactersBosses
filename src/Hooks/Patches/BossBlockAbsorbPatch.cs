using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using CharacterBosses.Monsters;

namespace CharacterBosses.Patches;

/// <summary>⚠⚠ <b>本类**故意不安装**（没有任何调用点，`TryInstall()` 也请不要再接上）。</b>
///
/// 用户 2026-11-09 的口径：**只有炼药猎**能挡住「失去生命」，其它 Boss 一律照原版。
/// 这条通用实现会让**所有**我们的 Boss 都能用格挡吃掉不可格挡的掉血，与那个口径冲突
/// ⇒ 现在由 <see cref="BrewerBossBlockableHpLossPatch"/>（挂在同一个 `Creature.LoseHpInternal` 上、
/// 判据是「炼药猎本体」）负责。两者**不要同时安装**，否则她的格挡会被吃两次。
///
/// 下面这段是原始设计说明，留作参考。
///
/// 「**调制**」第三条的新实现（用户 2026-10-26 改口径）：
/// **每当她在拥有格挡时将要失去生命，就改为用等量格挡抵消；伤害超过格挡时，溢出的部分照常扣血（并击破格挡）。**
///
/// 为什么换成这条：原条款只是「她可以格挡**中毒和魂劫**造成的伤害」，而各种模组的 DoT 五花八门
/// （崩坠的灼魂 / 活体炸弹、别的 mod 的流血/灼烧…），逐个点名既不完整也很脆
/// （原来那版要反射挂中毒与 `CollectorDoomPower` 的**状态机**才能认出这笔伤害的来源）。现在改成一条通用规则：
/// **任何**「将失去生命」都被格挡按 1:1 吃掉。
///
/// ## 挂在哪个点（反编译依据）
/// 所有掉血最后都走同一个同步函数：
/// <code>
/// Creature.LoseHpInternal(decimal amount, ValueProp props):
///     CurrentHp = Math.Max(CurrentHp - num, 0);   → 返回 DamageResult
/// Creature.DamageBlockInternal(decimal amount, ValueProp props):
///     num = props.HasFlag(Unblockable) ? 0 : Min(Block, amount);  Block -= num;  → 返回被格挡的量
/// </code>
/// 原版「普通攻击」是**先** `DamageBlockInternal` 吃掉格挡、**再**把剩下的交给 `LoseHpInternal`；
/// 而「中毒 / 魂劫 / 失去生命」这类 `Unblockable` 的伤害**跳过**第一步、直接进 `LoseHpInternal`。
/// 所以只要在 `LoseHpInternal` 上加前置：
///   · 目标是不是我们的 Boss；不是 → 一个字都不改（玩家、随从、别的敌人全部零回归）；
///   · 她的格挡 &lt;= 0 → 原样（没格挡就是普通掉血）；
///   · 否则 `吃掉 = min(格挡, 这次要掉的血)` → `LoseBlockInternal(吃掉)`，把 `amount` 减掉同样多。
/// 剩多少就走多少：**溢出照常扣血**，格挡也会被扣到 0（= 被击破），和用户说的「过量了还是会击破格挡」一致。
///
/// 注意：普通攻击这条路上，格挡在 `DamageBlockInternal` 里已经被扣过了 ⇒ 到这里 `Block` 通常已是 0，
/// 本补丁自然不重复吃（这点很重要：否则等于双倍格挡）。
/// </summary>
internal static class BossBlockAbsorbPatch
{
    private static bool _installed;
    private static int _logs;

    internal static void TryInstall()
    {
        if (_installed) return;
        _installed = true;
        try
        {
            var target = AccessTools.Method(typeof(Creature), nameof(Creature.LoseHpInternal),
                new[] { typeof(decimal), typeof(MegaCrit.Sts2.Core.ValueProps.ValueProp) });
            if (target == null)
            {
                Logger.Log("[CharBoss] 格挡吃伤害：找不到 Creature.LoseHpInternal ⇒ 未挂（版本不符？）");
                return;
            }
            var harmony = new Harmony("CharacterBosses.blockabsorb");
            harmony.Patch(target, prefix: new HarmonyMethod(typeof(BossBlockAbsorbPatch)
                .GetMethod(nameof(LoseHpPrefix), System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!));
            Logger.Log("[CharBoss] 调制：已挂「有格挡时用格挡抵消将失去的生命」（只对我们的 Boss，溢出照常扣血）");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 调制：挂格挡抵消失败（忽略）: " + e.Message);
        }
    }

    /// <summary>
    /// <c>Creature.LoseHpInternal(decimal amount, ValueProp props)</c> 的前缀：把「要掉的血」换成「先扣格挡」。
    /// 参数用位置名（<c>__0</c>）而不是名字，避免 Harmony 因到处方的参数名差异绑不上。
    /// </summary>
    /// <summary>
    /// 她这一档是不是「炼药猎」的「调制」（`BrewModPower`）。
    /// 优先看身上真有没有那层 Power（战斗内最准），拿不到再看定义里这一档的特殊效果。
    /// </summary>
    private static bool HasBrewMod(CharBossMonster boss)
    {
        try
        {
            if (boss.Creature?.GetPower<CharacterBosses.Powers.BrewModPower>() != null) return true;
        }
        catch { /* 继续看定义 */ }
        try
        {
            return boss.Def?.SpecialPowerForAct(boss.ActIndex) == typeof(CharacterBosses.Powers.BrewModPower);
        }
        catch { return false; }
    }
    private static void LoseHpPrefix(Creature __instance, ref decimal __0)
    {
        try
        {
            if (__instance == null) return;
            if (__instance.Monster is not CharBossMonster boss) return;   // 只对我们的 Boss 生效

            // ⚠ 用户 2026-10-27：这一条是**三层「炼药猎」的「调制」专属**，以前却对**所有** Boss 生效
            //   （三层刀猎被误认为「有炼药猎的特殊效果、能格挡中毒」）。判据：她身上真有「调制」那层 Power。
            if (!HasBrewMod(boss)) return;
            if (__0 <= 0m) return;

            int block = __instance.Block;
            if (block <= 0) return;                                   // 没格挡：普通掉血

            decimal absorbed = Math.Min(block, __0);
            if (absorbed <= 0m) return;

            __instance.LoseBlockInternal(absorbed);                   // 走原版 API：BlockChanged → 血条/格挡数字跟着刷新
            __0 -= absorbed;

            if (_logs++ < 12)
            {
                Logger.Log($"[CharBoss] 调制：她将失去 {absorbed + __0:0} 点生命 → 用格挡抵消 {absorbed:0}，" +
                           $"实际掉血 {__0:0}，剩余格挡 {__instance.Block}" +
                           (__0 > 0m ? "（溢出部分照常扣血）" : string.Empty));
            }
        }
        catch { /* 绝不影响战斗结算 */ }
    }
}
