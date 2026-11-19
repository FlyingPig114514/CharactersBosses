using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;
using CharacterBosses.Core;

namespace CharacterBosses.Patches;

/// <summary>
/// 「这一笔伤害是不是**我们的角色 Boss**（她本人 / 她的随从 / 她的影子玩家出的牌）打出去的」。
///
/// 用途：联机无色牌「**拦截**」（`Intercept`）对角色 Boss 的伤害完全不生效（玩家 2026-11-19 反馈）——
/// 反编译出来的原因是原版这套机制**只认「带力量加成的攻击」**：
///
/// ```text
/// ValuePropExtensions.IsPoweredAttack(props) => props 含 Move 且不含 Unpowered
///
/// CoveredPower.ModifyDamageMultiplicative   ：target==Owner 且 IsPoweredAttack ⇒ 返回 0（被保护者不掉血）
/// InterceptPower.ModifyDamageMultiplicative ：target==Owner 且 IsPoweredAttack ⇒ 返回 (覆盖人数 + 1)
/// ```
///
/// 而我们的 Boss 有大量伤害是**自己算好数值后以 `ValueProp.Unpowered` 打出去的**
/// （特殊效果、剧本里的无来源伤害，全局 65 处）⇒ `IsPoweredAttack` 为 false ⇒ 原版这两个钩子
/// 一律 `return 1` ⇒ 拦截**一点用都没有**。另外「无来源伤害」的 `dealer` 还可能是 null，
/// 更不能指望原版自己认出来。
///
/// 玩家 2026-11-19 定的最终口径（比原版更明确）：
///   · **被保护者**：本回合不受到**任何** Boss 伤害；
///   · **拦截者**：Boss 的**单体**打在被保护者身上 ⇒ 全额转到拦截者身上，**1 倍**
///     （"Boss 暂时换了锁定目标"的效果，但代码里不真去改锁定）；Boss 的**群攻** ⇒ 拦截者吃 **2 倍**；
///     Boss 单体**本来就是打拦截者自己** ⇒ 还是 **1 倍**；
///   · 易伤/虚弱之类一律**按拦截者自己的状态**重算。
/// </summary>
internal static class BossDamageOrigin
{
    /// <summary>诊断日志上限（多人大战会刷屏，只留前几条）。</summary>
    private static int _logged;

    /// <summary>这笔伤害是不是我们角色 Boss 这一侧打出去的。</summary>
    public static bool FromOurBoss(Creature? dealer, CardModel? cardSource, CardPlay? cardPlay, ICombatState? combat)
    {
        try
        {
            // ① 有明确的出手生物：认得出是她的（本人 / 随从 / 影子玩家）就算；
            //    认不出就**相信原版**（玩家自己、队友、原版怪物都不该被我们改）。
            if (dealer != null)
            {
                if (BossKillerTracker.BossOf(dealer) != null) return true;
                if (cardPlay?.Card?.Owner?.Creature?.Monster is Monsters.CharBossMonster) return true;
                if (cardSource?.Owner?.Creature?.Monster is Monsters.CharBossMonster) return true;
                return false;
            }

            // ② dealer 为空：退到卡牌归属
            var owner = cardPlay?.Card?.Owner ?? cardSource?.Owner;
            if (owner != null)
            {
                if (BossKillerTracker.BossOf(owner.Creature) != null) return true;
                if (owner.Creature?.Monster is Monsters.CharBossMonster) return true;
                return false;
            }

            // ③ 连来源都没有：只要场上还有我们的 Boss，就按「Boss 侧伤害」处理 ——
            //    本模组替换了每一幕 Boss，这种无来源伤害只可能来自她（毒、特殊效果、剧本）。
            return AnyBossAlive(combat);
        }
        catch { return false; }
    }

    /// <summary>场上还有活着的角色 Boss 吗。</summary>
    public static bool AnyBossAlive(ICombatState? combat)
    {
        try
        {
            if (combat?.Enemies == null) return false;
            foreach (var c in combat.Enemies)
            {
                if (c?.Monster is Monsters.CharBossMonster && c.IsAlive) return true;
            }
        }
        catch { /* 忽略 */ }
        return false;
    }

    /// <summary>
    /// 这个拦截者**此刻护着几个人**（等价于原版 `InterceptPower` 内部的 `coveredCreatures.Count`；
    /// 那份名单在私有嵌套类型里，所以这里改数「全场玩家身上由他施加的 `CoveredPower`」）。
    /// </summary>
    public static int CoveredCount(Creature? interceptor)
    {
        try
        {
            if (interceptor == null) return 0;
            var combat = interceptor.CombatState;
            if (combat?.Players == null) return 0;

            int n = 0;
            foreach (var player in combat.Players)
            {
                var creature = player?.Creature;
                if (creature == null || creature.Monster != null) continue;   // 影子玩家不算
                foreach (var power in creature.Powers)
                {
                    if (power is CoveredPower covered && ReferenceEquals(covered.Applier, interceptor)) n++;
                }
            }
            return n;
        }
        catch { return 0; }
    }

    /// <summary>这个生物被谁「拦截保护」着（没有则 null）。</summary>
    public static Creature? InterceptorOf(Creature? victim)
    {
        try
        {
            if (victim == null) return null;
            foreach (var power in victim.Powers)
            {
                if (power is not CoveredPower covered) continue;
                var applier = covered.Applier;
                if (applier != null && applier.IsAlive) return applier;
            }
        }
        catch { /* 忽略 */ }
        return null;
    }

    /// <summary>限量的诊断日志。</summary>
    public static void Note(string message)
    {
        try
        {
            if (_logged >= 8) return;
            _logged++;
            Logger.Log("[CharBoss] 拦截对账：" + message + (_logged == 8 ? "（后续同类日志省略）" : string.Empty));
        }
        catch { /* 忽略 */ }
    }
}

/// <summary>
/// 被保护的人（`CoveredPower` 持有者）：**我们的 Boss 打过来的一律归零**（兜底）。
///
/// 主路径由 <see cref="BossInterceptRedirectPatch"/> 把被保护者直接从目标列表里摘掉；
/// 这里再加一道，防止有别的调用点绕过那个重载。
/// </summary>
[HarmonyPatch(typeof(CoveredPower), nameof(CoveredPower.ModifyDamageMultiplicative))]
internal static class CoveredPowerBossDamageZeroPatch
{
    [HarmonyPostfix]
    private static void Postfix(CoveredPower __instance, Creature target, decimal amount,
                                ValueProp props, Creature dealer, CardModel cardSource, CardPlay cardPlay,
                                ref decimal __result)
    {
        try
        {
            if (__result <= 0m) return;
            if (target == null || !ReferenceEquals(target, __instance?.Owner)) return;
            if (!BossDamageOrigin.FromOurBoss(dealer, cardSource, cardPlay, __instance!.Owner?.CombatState)) return;

            __result = 0m;
            BossDamageOrigin.Note($"被保护者 {target.Name ?? "?"} 吃到 Boss 侧的 {amount:0.##} 点伤害 ⇒ 归零" +
                                  $"（原版 props={props}）");
        }
        catch { /* 判不出来就不动原版 */ }
    }
}

/// <summary>
/// 拦截者（`InterceptPower` 持有者）：**把原版那条 ×(覆盖人数+1) 关掉**（对我们 Boss 的伤害）。
///
/// 为什么必须关：玩家定的口径是「单体 1 倍 / 群攻 2 倍」，而原版是「只要打到你身上就 ×(N+1)」——
/// 单体也会被放大一倍。现在改为**在伤害目标列表里复制份额**（见
/// <see cref="BossInterceptRedirectPatch"/>）：群攻时拦截者拿 2 份 ⇒ 自然 2 倍；单体转过来的只有 1 份 ⇒ 1 倍。
/// 若原版那条还开着，每份都会被再乘一次 ⇒ 群攻变 4 倍，所以这里一律压回 1。
/// </summary>
[HarmonyPatch(typeof(InterceptPower), nameof(InterceptPower.ModifyDamageMultiplicative))]
internal static class InterceptPowerBossDamageNoVanillaMultiplierPatch
{
    [HarmonyPostfix]
    private static void Postfix(InterceptPower __instance, Creature target, Creature dealer,
                                CardModel cardSource, CardPlay cardPlay, ref decimal __result)
    {
        try
        {
            if (__result == 1m) return;
            if (target == null || !ReferenceEquals(target, __instance?.Owner)) return;
            if (!BossDamageOrigin.FromOurBoss(dealer, cardSource, cardPlay, __instance!.Owner?.CombatState)) return;

            __result = 1m;      // 份额已经在目标列表里复制过了（见 BossInterceptRedirectPatch）
        }
        catch { /* 判不出来就不动原版 */ }
    }
}

/// <summary>
/// **拦截的伤害改派**（唯一的实机入口）—— 只挂原版那个「列表版」重载：
/// 反编译确认其余 11 个 `CreatureCmd.Damage` 重载**全都**转调它（单目标版会先把目标包成单元素列表）。
///
/// ```text
/// Damage(ctx, IEnumerable<Creature> targets, decimal amount, ValueProp props,
///        Creature dealer, CardModel cardSource, CardPlay cardPlay)
/// ```
///
/// 规则（玩家 2026-11-19 定稿）：
///   · 目标是**被保护者**（身上有 `CoveredPower`，施加者是活着的拦截者 I）⇒ 从列表里**摘掉**
///     ⇒ 他这一笔一点都不吃（后面还有 `CoveredPower` 归零兜一层）。
///   · I **本来就在这次的目标列表里**（= 群攻，所有人挨打）⇒ 给他**多复制「覆盖人数」份**
///     ⇒ 他吃 (1 + 覆盖人数) 份；覆盖 1 人就是**双倍**。
///   · I **不在列表里**（= Boss 的单体打在被保护者身上）⇒ 把 I **加进列表一份**
///     ⇒ 这一笔全额转到他身上，**1 倍**（"Boss 暂时换了目标"的效果，但不去动锁定关系本身）。
///   · 易伤/虚弱/格挡全部由原版管道**按 I 自己的状态**现算（我们只是把「打谁」换掉）。
///
/// 没有 `CoveredPower` 参与时**一个字节都不改** ⇒ 单人/没打拦截时零回归。
/// </summary>
[HarmonyPatch(typeof(CreatureCmd), nameof(CreatureCmd.Damage),
    new[] { typeof(PlayerChoiceContext), typeof(IEnumerable<Creature>), typeof(decimal), typeof(ValueProp),
            typeof(Creature), typeof(CardModel), typeof(CardPlay) })]
internal static class BossInterceptRedirectPatch
{
    [HarmonyPrefix]
    private static void Prefix(decimal amount, ValueProp props,
                               Creature? dealer, CardModel? cardSource, CardPlay? cardPlay,
                               ref IEnumerable<Creature>? targets)
    {
        try
        {
            if (targets == null) return;

            var list = targets as IList<Creature> ?? targets.ToList();
            if (list.Count == 0) return;

            var combat = list[0]?.CombatState;
            if (!BossDamageOrigin.FromOurBoss(dealer, cardSource, cardPlay, combat)) return;

            // ① 这一次的目标里有没有「被保护者」？有的话谁在护着他？
            Creature? interceptor = null;
            int coveredHere = 0;
            bool interceptorWasTargeted = false;
            foreach (var t in list)
            {
                if (t == null) continue;
                var body = BossDamageOrigin.InterceptorOf(t);
                if (body != null) { coveredHere++; interceptor ??= body; }
                else if (interceptor != null && ReferenceEquals(t, interceptor)) interceptorWasTargeted = true;
            }
            if (interceptor == null) return;                       // 没人在拦截 ⇒ 一个字节都不改
            if (!BossDamageOrigin.AnyBossAlive(combat)) return;

            if (!interceptorWasTargeted)
            {
                foreach (var t in list)
                {
                    if (ReferenceEquals(t, interceptor)) { interceptorWasTargeted = true; break; }
                }
            }

            int covered = Math.Max(coveredHere, BossDamageOrigin.CoveredCount(interceptor));

            // ② 被保护者一律摘掉（这一笔他们一点都不吃）
            var keep = new List<Creature>(list.Count);
            foreach (var t in list)
            {
                if (t == null) continue;
                if (BossDamageOrigin.InterceptorOf(t) != null) continue;
                if (ReferenceEquals(t, interceptor)) continue;      // 拦截者稍后按份数重新加回来
                keep.Add(t);
            }

            if (interceptorWasTargeted)
            {
                // 群攻：他自己那份 + 「覆盖人数」份替队友承受的 ⇒ (1 + covered) 倍
                for (int i = 0; i < covered + 1; i++) keep.Add(interceptor);
                BossDamageOrigin.Note($"群攻：{coveredHere} 名被保护者被摘掉，拦截者 " +
                                      $"{interceptor.Name ?? "?"} 按 {covered + 1} 份结算（{amount:0.##}/份）");
            }
            else
            {
                // 单体：全额转到拦截者身上（1 份）
                keep.Add(interceptor);
                BossDamageOrigin.Note($"单体：{amount:0.##} 点伤害从被保护者转到拦截者 " +
                                      $"{interceptor.Name ?? "?"}（按他自己的易伤/格挡结算，1 倍）");
            }

            if (keep.Count == 0) keep.Add(interceptor);

            // 双保险：① 把新的目标列表交回原版（`ref` 前缀参数）；
            //         ② 若调用方传进来的本身就是个可变列表，也**就地**改成同一份内容 ——
            //            这样即使某个 Harmony 版本没有把 ref 改动传回去，拦截也照样生效
            //            （原版只会遍历它最终拿到的那一份，不会重复结算）。
            targets = keep;
            try
            {
                if (list is List<Creature> mutable && !ReferenceEquals(mutable, keep))
                {
                    mutable.Clear();
                    mutable.AddRange(keep);
                }
            }
            catch { /* 只读列表（原版单目标版会包一层只读集合）⇒ 忽略 */ }
        }
        catch { /* 判不出来就交回原版 */ }
    }
}
