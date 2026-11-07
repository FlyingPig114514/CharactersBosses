using System;
using System.Collections.Generic;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using CharacterBosses.Monsters;

namespace CharacterBosses.Patches;

/// <summary>
/// **故障机器人的闪电 / 暗黑充能球优先打「替主人承伤」的召唤物**（用户 2026-11-05）。
///
/// 背景（反编译，两套球的差异在这里就已经埋好了）：
/// <code>
/// LightningOrb.ApplyLightningDamage: targets = GetOpponentsOf(owner).Where(IsHittable)
///                                    → Rng.CombatTargets.NextItem(...)   // **随机一名**
///                                    CreatureCmd.Damage(..., ValueProp.Unpowered, orb.Owner.Creature)
/// DarkOrb.Evoke:                     weakestEnemy = HittableEnemies.MinBy(c =&gt; c.CurrentHp)  // **血量最低**
///                                    CreatureCmd.Damage(..., ValueProp.Unpowered, orb.Owner.Creature)
/// </code>
/// 两者的伤害都是 `Unpowered`，而奥斯提的「为你而死」（`DieForYouPower.ModifyUnblockedDamageTarget`）
/// 只在 `props.IsPoweredAttack()`（= 有 Move 且没有 Unpowered）时才替主人承伤
/// ⇒ 原版里这两颗球**都会穿过奥斯提**，而且闪电球随机、暗黑球专挑血最少的 ——
/// 于是「暗黑球老是打奥斯提、闪电球老是打你」，观感像两套规则。
///
/// 用户要求：**两种球都优先打奥斯提（或其它模组里带「为你而死」的召唤物）**。
///
/// 挂点选 `CreatureCmd.Damage` 的**核心重载**（闪电/暗黑最终都走到它），前缀里把「真人玩家」目标
/// 换成那只召唤物。判据收得很紧，只动**我们的故障机器人 Boss 的充能球**：
///   · `props` 带 `Unpowered`（球的伤害就是无来源）且 `cardSource == null`（球的伤害不是卡牌打的
///     —— 能力机的「冰雹风暴」是卡牌伤害，仍然**不会**被替身吸收，见本地化里保留的那句警告）；
///   · dealer = 球的 `Owner.Creature`，也就是**她本人**（`Monster is CharBossMonster`）；
///   · 她必须是**故障机器人**（`DEFECT_*`）；
///   · 场上要有**活着且可被命中**、带 `DieForYouPower` 的召唤物（奥斯提，或其它模组的同类召唤物）。
/// 玩家自己的球、其它敌人的球、玩家侧的伤害一律零影响。
/// </summary>
[HarmonyPatch]
public static class OrbPrefersDieForYouPetPatch
{
    private static int _logs;

    /// <summary>两版 `CreatureCmd.Damage` 的核心重载参数个数不同 ⇒ 运行时挑（见 <see cref="DamageOverload"/>）。</summary>
    [HarmonyPrepare]
    public static bool Prepare() => TargetMethod() != null;

    internal static System.Reflection.MethodBase? TargetMethod() => DamageOverload.Find();

    [HarmonyPrefix]
    public static void Prefix(object[] __args)
    {
        try
        {
            // ⚠ 用 `__args` 按**位置**取参数，不靠形参名 —— 这个重载有 12 个同名兄弟，
            //   名字对不上时会静默不生效（踩过）。
            //   位置：(0) ctx (1) targets (2) amount (3) props (4) dealer (5) cardSource (6) cardPlay
            if (__args == null || __args.Length < 6) return;
            if (!(__args[3] is ValueProp props) || !props.HasFlag(ValueProp.Unpowered)) return;  // 只动「无来源」伤害（充能球那类）
            if (DamageOverload.CardSource(__args) != null) return;                               // 卡牌伤害（如冰雹风暴）不动
            // 旧版没有 cardSource 参数 ⇒ 再看一眼「正在打出的是不是卡牌伤害」（冰雹风暴等放行）
            string playing = (CharBossMonster.CurrentPlayingEntry ?? "");
            if (playing.Length > 0 && playing != "BOMBARDMENT") return;
            if (!(__args[4] is Creature dealer)) return;
            if (!(__args[1] is IEnumerable<Creature> targets) || targets == null) return;

            var boss = dealer.Monster as CharBossMonster;
            if (boss == null) return;
            string key = boss.Def?.Key ?? "";
            if (!key.StartsWith("DEFECT", StringComparison.Ordinal)) return;

            var combat = dealer.CombatState;
            if (combat == null) return;

            var list = targets as List<Creature> ?? new List<Creature>(targets);
            if (list.Count == 0) return;

            // 目标里已经有替身了 → 不用改
            foreach (var t in list)
            {
                if (t != null && t.GetPower<DieForYouPower>() != null) return;
            }

            var pet = FindDieForYouPet(combat, list);
            if (pet == null) return;

            var result = new List<Creature>(list.Count);
            bool changed = false;
            foreach (var t in list)
            {
                if (t == null) continue;
                // 只替换**真人玩家**目标（召唤物/她自己不动）
                if (t.Monster == null && t.Side == CombatSide.Player)
                {
                    changed = true;
                    if (!result.Contains(pet)) result.Add(pet);
                    continue;
                }
                result.Add(t);
            }
            if (!changed || result.Count == 0) return;

            __args[1] = result;
            if (_logs++ < 20)
            {
                Logger.Log($"[CharBoss] 充能球改打替身：{key} 的无来源充能球伤害原本要打在真人玩家身上 " +
                           $"⇒ 改打 {SafeName(pet)}" +
                           $"（原目标 {string.Join(",", list.ConvertAll(c => c == null ? "null" : SafeName(c)))}）");
            }
        }
        catch { /* 任何异常都不影响原伤害流程 */ }
    }

    /// <summary>
    /// 找一只「替主人承伤」的召唤物：属于真人玩家、活着、可被命中、身上有 `DieForYouPower`。
    /// 优先取**和原目标同一个主人**的那只（多人时不会串台）。
    /// </summary>
    internal static Creature? FindDieForYouPet(ICombatState combat, List<Creature> originals)
    {
        try
        {
            Creature? fallback = null;
            foreach (var player in Core.BossLockOn.RealPlayers(combat))
            {
                var owner = player?.Creature;
                if (owner == null || !owner.IsAlive) continue;
                var pets = owner.Pets;
                if (pets == null) continue;
                foreach (var pet in pets)
                {
                    if (pet == null || !pet.IsAlive || !pet.IsHittable) continue;
                    if (pet.GetPower<DieForYouPower>() == null) continue;
                    foreach (var t in originals)
                    {
                        if (t == null || t.Monster != null) continue;
                        if (ReferenceEquals(t.Player, player)) return pet;   // 同一个主人
                    }
                    fallback ??= pet;
                }
            }
            return fallback;
        }
        catch { return null; }
    }

    private static string SafeName(Creature? creature)
    {
        try { return creature?.Monster?.Id.Entry ?? creature?.Player?.NetId.ToString() ?? "?"; }
        catch { return "?"; }
    }
}

/// <summary>
/// **单目标版本**的「充能球优先打替身」（2026-11-07 补）。
///
/// 闪电球与暗黑球最终调的都是**单目标** `CreatureCmd.Damage(ctx, target, amount, props, dealer, …)`，
/// 而 <see cref="OrbPrefersDieForYouPetPatch"/> 只挂在「列表目标」那个重载上 ⇒ 之前那条规则
/// （用户 2026-11-05 要求的「两种球都优先打奥斯提」）实际一次都没生效。这里补上同一个判据。
/// 参数位置：(0) ctx (1) target (2) amount (3) props (4) dealer (5) cardSource (6) cardPlay。
/// </summary>
[HarmonyPatch]
public static class OrbPrefersDieForYouPetSinglePatch
{
    private static int _logs;

    [HarmonyPrepare]
    public static bool Prepare() => TargetMethod() != null;

    internal static System.Reflection.MethodBase? TargetMethod() => DamageOverload.FindSingle();

    [HarmonyPrefix]
    public static void Prefix(object[] __args)
    {
        try
        {
            if (__args == null || __args.Length < 5) return;
            if (!(__args[3] is ValueProp props) || !props.HasFlag(ValueProp.Unpowered)) return;
            if (__args.Length > 5 && __args[5] is CardModel cs && cs != null) return;   // 卡牌伤害不动
            if (!(__args[4] is Creature dealer)) return;
            if (!(__args[1] is Creature target)) return;

            var boss = dealer.Monster as CharBossMonster;
            if (boss == null) return;
            string key = boss.Def?.Key ?? "";
            if (!key.StartsWith("DEFECT", StringComparison.Ordinal)) return;

            var combat = dealer.CombatState;
            if (combat == null) return;

            // 只换「真人玩家」目标（召唤物 / 她自己不动）
            if (target.Monster != null || target.Side != CombatSide.Player) return;
            if (target.GetPower<DieForYouPower>() != null) return;

            var pet = OrbPrefersDieForYouPetPatch.FindDieForYouPet(combat, new List<Creature> { target });
            if (pet == null || ReferenceEquals(pet, target)) return;

            __args[1] = pet;
            if (_logs++ < 20)
            {
                Logger.Log($"[CharBoss] 充能球改打替身（单目标）：{key} 的充能球伤害原本打在 " +
                           $"{SafeNameOf(target)} 身上 ⇒ 改打 {SafeNameOf(pet)}");
            }
        }
        catch { /* 任何异常都不影响原伤害流程 */ }
    }

    private static string SafeNameOf(Creature? creature)
    {
        try { return creature?.Monster?.Id.Entry ?? creature?.Player?.NetId.ToString() ?? "?"; }
        catch { return "?"; }
    }
}