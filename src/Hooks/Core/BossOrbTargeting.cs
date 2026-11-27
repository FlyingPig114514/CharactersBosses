using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Orbs;
using MegaCrit.Sts2.Core.ValueProps;
using CharacterBosses.Monsters;

namespace CharacterBosses.Core;

/// <summary>
/// **我们 Boss 的充能球被动该打谁**（用户 2026-11-19）：
/// > 「boss 的闪电充能球现在好像可以先电亡灵契约师。电球和黑球都应该**先索敌奥斯提**。」
///
/// 用户口径（已确认）：**先打「被这只 Boss 锁定的那位玩家」的召唤物（奥斯提等），没有召唤物才打玩家本人**。
///
/// 为什么需要接管：原版 `LightningOrb.Passive` / `DarkOrb.Passive` 在 `target` 为空时会从
/// **施法者视角的 `HittableEnemies`** 里随机挑一个 —— 而我们这只 Boss 的「敌人」里除了真人玩家，
/// 还包含**别的 Boss 的影子玩家**和它们的随从（多 Boss 同场），于是球会电到自己人身上
/// （用户实测：电到了另一只 Boss「亡灵契约师」）。
///
/// 做法：只对「球的主人是我们的 Boss」改写目标，其它情况（真人玩家自己的球）**一律不动**。
/// 挑召唤物时按 `CombatState.Creatures` 的**列表顺序**取第一个 ⇒ 两端确定性一致，不会造成不同步。
/// </summary>
internal static class BossOrbTargeting
{
    /// <summary>球被动/激发该打的目标：先召唤物，再玩家本人（拿不到返回 null = 用原版逻辑）。</summary>
    internal static Creature? Preferred(CharBossMonster? boss)
    {
        try
        {
            var combat = boss?.CombatState;
            if (combat == null) return null;

            // ① 被这只 Boss 锁定的那位玩家（单人就是唯一真人玩家）
            var victim = BossLockOn.TargetFor(boss!);
            if (victim == null)
            {
                try
                {
                    foreach (var p in BossLockOn.RealPlayers(combat))
                    {
                        if (p?.Creature == null) continue;
                        victim = p;
                        break;
                    }
                }
                catch { /* 忽略 */ }
            }
            var victimCreature = victim?.Creature;
            if (victimCreature == null) return null;

            // ② 优先：这位玩家的召唤物（奥斯提 / 瓦皮 / 其它宠物）。按列表顺序取第一个 ⇒ 两端一致。
            //
            // ★ v0.2.969（用户 2026-11-26：「故障机器人 boss 的电球和黑球好像还是不是优先攻击奥斯提/瓦皮」）：
            //   以前只认 `c.IsPet == true`。但「瓦皮」（`MONSTER.WUPPY`，原版宠物）与某些模组的宠物
            //   只设了 `PetOwner` 而没有把 `IsPet` 打开 ⇒ 整条规则对它们失效，球还是落在玩家本人身上。
            //   现在判据放宽成「**有 PetOwner 就按宠物算**」（`IsPet` 只作为附加确认），并且
            //   只要 `PetOwner` 是这位玩家本人即可（不再要求 `IsPet` 同时成立）。
            try
            {
                Creature? firstPet = null;
                int pets = 0;
                foreach (var c in combat.Creatures)
                {
                    if (c == null || ReferenceEquals(c, victimCreature)) continue;
                    if (!c.IsAlive || c.IsDead) continue;

                    bool ownedByVictim = false;
                    try { ownedByVictim = c.PetOwner != null && ReferenceEquals(c.PetOwner, victim); }
                    catch { ownedByVictim = false; }
                    if (!ownedByVictim)
                    {
                        // 退一步：`PetOwner` 拿不到时，至少要求它被标记成宠物
                        bool isPet = false;
                        try { isPet = c.IsPet; } catch { isPet = false; }
                        if (!isPet) continue;
                    }

                    pets++;
                    if (firstPet == null) firstPet = c;
                }
                if (firstPet != null)
                {
                    if (DiagCount < 40)
                    {
                        DiagCount++;
                        Core.LogLoudness.Diag($"[CharBoss] 球目标：{SafeName(boss!.Creature)} 锁定={SafeName(victimCreature)}" +
                                              $" ⇒ 优先打它的召唤物 {SafeName(firstPet)}（场上候选 {pets} 只）");
                    }
                    return firstPet;
                }
            }
            catch { /* 忽略：退回玩家本人 */ }

            // ③ 没有召唤物 ⇒ 打玩家本人
            if (DiagCount < 40)
            {
                DiagCount++;
                Core.LogLoudness.Diag($"[CharBoss] 球目标：{SafeName(boss!.Creature)} 锁定={SafeName(victimCreature)}" +
                                      " ⇒ 没有召唤物，打玩家本人");
            }
            return victimCreature;
        }
        catch { return null; }
    }

    /// <summary>球的主人是我们的 Boss 才改写目标。</summary>
    internal static Creature? Rewrite(OrbModel? orb, Creature? given)
    {
        try
        {
            var boss = BossOf(orb);
            if (boss == null)
            {
                // v0.2.808 诊断：在 PC 上留一行（限流），用来确认「球的 Owner 到底有没有」
                if (DiagCount++ < 30)
                    Core.LogLoudness.Diag($"[CharBoss] 球目标：{Name(orb)} 主人=**不是我们的 Boss**" +
                                          $"（Owner={OwnerName(orb)}）⇒ 不改写（沿用原版随机）");
                return given;
            }
            var want = Preferred(boss);
            if (DiagCount++ < 30)
                Core.LogLoudness.Diag($"[CharBoss] 球目标：{Name(orb)} 主人={SafeName(boss.Creature)} " +
                                      $"传入={SafeName(given)} ⇒ 改成 {SafeName(want)}" +
                                      (want == null ? "（算不出，沿用原版）" : ""));
            return want ?? given;
        }
        catch { return given; }
    }

    private static int DiagCount;

    /// <summary>
    /// 这颗球属于哪只我们的 Boss。**必须精确对号**，认不出就返回 null（= 一个字节都不改）。
    ///
    /// ★★ v0.2.981（用户 2026-11-27：「多人时玩家方故障机器人的黑球打队友了（对面 boss 有黑球机，
    /// 是不是它的黑球索敌影响到玩家了）」——**猜得对**，就是这里）：
    ///   v0.2.969 为了救「影子玩家的球偶尔 `Owner == null` ⇒ 不改写」，加了一条兜底：
    ///   「`Owner` 认不出就问『当前正在行动的那只 Boss』」。可那条兜底**没有先排除真人玩家**——
    ///   玩家自己的球（`Owner` 是真人 `Player`，其 `Creature.Monster` 为 null）也会掉进这条兜底，
    ///   于是被当成「正行动的那只 Boss 的球」，改写成**那只 Boss 锁定的玩家** ⇒ 玩家的黑球去打了**队友**。
    ///   日志铁证（本机 mod_log，22:24:26，`#5 轮次 3 侧=Player` = 玩家回合）：
    ///   `黑球激发：静默猎手 的黑暗球 ⇒ 目标改为 P#c5d6a4`，紧接着 `P#c5d6a4：HP 52/80 → 35/80`；
    ///   而那一场 `静默猎手` 身上只有群蛇形态/迅捷，**根本没有充能球**，黑暗球的主人是
    ///   `DEFECT_BOSS`（orbs=D,F,F）或**那位故障机器人玩家**（`DEFECT_BOSS→Defect/NetId=P#c89a9c`），
    ///   也就是说被改写的其实是玩家的球，还打到了 `SILENT_BOSS` 锁定的另一位玩家身上。
    ///
    /// 现在的判定（从强到弱，全都**证明得了才认**）：
    ///   ① `Owner.Creature.Monster` 就是我们的 Boss（影子玩家的生物同时是 Player 和 Monster）⇒ 认它；
    ///   ② `Owner` 是一位**真实玩家**（不是我们造出来的影子玩家）⇒ **立刻放弃**（玩家的球绝不改写）；
    ///   ③ `Owner` 丢了/认不出 ⇒ 拿这颗球的**对象身份**去比「她影子玩家的球队列」，
    ///      在谁那里就归谁（比「谁在行动」可靠得多，也不依赖时序）；
    ///   ④ 还是认不出 ⇒ null（退回原版行为，宁可少一次「先打召唤物」，也不能改写别人的球）。
    /// </summary>
    internal static CharBossMonster? BossOf(OrbModel? orb)
    {
        try
        {
            if (orb == null) return null;

            // ① 球的主人就是我们的 Boss
            var ownerCreature = orb.Owner?.Creature;
            if (ownerCreature?.Monster is CharBossMonster direct) return direct;

            // ② 球的主人是一位**真实玩家** ⇒ 玩家自己的球，一个字节都不改
            var ownerPlayer = orb.Owner;
            if (ownerPlayer != null && !IsShadowPlayer(ownerPlayer))
            {
                if (DiagCount++ < 30)
                {
                    Core.LogLoudness.Diag($"[CharBoss] 球目标：{Name(orb)} 的主人=真实玩家 " +
                                          $"{SafeName(ownerPlayer.Creature)} ⇒ 玩家的球不动（原版行为）");
                }
                return null;
            }

            // ③ 主人丢了 / 认不出 ⇒ 按「这颗球在谁的球槽里」精确对号
            foreach (var boss in OurBosses(orb))
            {
                try
                {
                    var orbs = boss.Shadow?.PlayerCombatState?.OrbQueue?.Orbs;
                    if (orbs == null) continue;
                    foreach (var o in orbs)
                    {
                        if (ReferenceEquals(o, orb)) return boss;
                    }
                }
                catch { /* 换下一只 */ }
            }

            return null;
        }
        catch { return null; }
    }

    /// <summary>是不是**我们造的影子玩家**（`ShadowPlayerFactory` 里 NetId 走 `0xFFFFFFFF_0000_xxxx` 段）。</summary>
    internal static bool IsShadowPlayer(MegaCrit.Sts2.Core.Entities.Players.Player? player)
    {
        try
        {
            if (player == null) return false;
            // 最强判据：影子玩家是「Player 与 Monster 合一」的生物（原版真人玩家没有 Monster）
            if (player.Creature?.Monster != null) return true;
            return (player.NetId >> 32) == 0xFFFF_FFFFUL;
        }
        catch { return false; }
    }

    /// <summary>场上的全部我们的 Boss（取本机视角的敌人列表；拿不到就空）。</summary>
    private static IEnumerable<CharBossMonster> OurBosses(OrbModel? orb)
    {
        MegaCrit.Sts2.Core.Combat.ICombatState? combat = null;
        try { combat = orb?.Owner?.Creature?.CombatState; } catch { combat = null; }
        if (combat == null)
        {
            try { combat = MegaCrit.Sts2.Core.Combat.CombatManager.Instance?.DebugOnlyGetState(); }
            catch { combat = null; }
        }
        if (combat == null) yield break;

        List<CharBossMonster> found = new();
        try
        {
            foreach (var c in combat.Enemies)
            {
                if (c?.Monster is CharBossMonster b) found.Add(b);
            }
        }
        catch { /* 忽略 */ }

        foreach (var b in found) yield return b;
    }

    private static string Name(OrbModel? orb)
    {
        try { return orb?.GetType().Name ?? "?"; } catch { return "?"; }
    }

    private static string OwnerName(OrbModel? orb)
    {
        try { return orb?.Owner == null ? "<null>" : SafeName(orb.Owner.Creature); } catch { return "?"; }
    }

    private static string SafeName(Creature? c)
    {
        try { return c == null ? "<null>" : (c.Name ?? "?"); } catch { return "?"; }
    }
}

/// <summary>
/// 闪电充能球的被动目标（见 <see cref="BossOrbTargeting"/>）。
/// ⚠ 这个 `Passive` 是**在具体球类上声明**的（不在 `OrbModel` 上）⇒ 必须单独挂，
///   并且显式写死参数类型（本仓库踩过重载 ⇒ Harmony 抛 AmbiguousMatchException 的坑）。
/// </summary>
[HarmonyPatch(typeof(LightningOrb), nameof(LightningOrb.Passive),
    new[] { typeof(PlayerChoiceContext), typeof(Creature) })]
internal static class LightningOrbBossTargetPatch
{
    [HarmonyPrefix]
    public static void Prefix(LightningOrb __instance, ref Creature target)
    {
        try
        {
            var want = BossOrbTargeting.Rewrite(__instance, target);
            // ★ v0.2.891（玩家反馈「鸡煲的球不打自己了，但游戏卡死」）：只接受**还活着且仍在战斗里**的目标。
            //   以前只要非 null 就塞给原版；万一那个生物已经被移出战斗（召唤物死了/离场），
            //   原版 `ApplyLightningDamage` 会对着一个不在战斗里的对象 await 伤害 ⇒ 看起来就是「卡死」。
            if (want != null && want.IsAlive && !want.IsDead && want.CombatState != null) target = want;
        }
        catch { /* 绝不影响战斗 */ }
    }
}

// ── 黑暗充能球**不能**这样改（v0.2.919 删除）────────────────────────────────
// 用户 2026-11-23 转来的玩家崩溃 trace 里反复出现：
//   `!! 未观察的任务异常 ⇒ AggregateException … (Dark orbs cannot target creatures.)`
//   `← InvalidOperationException: Dark orbs cannot target creatures.`
// 反编译实证（`MegaCrit.Sts2.Core.Models.Orbs.DarkOrb.Passive`）：
// <code>
// public override Task Passive(PlayerChoiceContext choiceContext, Creature? target)
// {
//     if (target != null) throw new InvalidOperationException("Dark orbs cannot target creatures.");
//     ...   // 黑暗球的被动只是给自己攒伤害，本来就没有目标
// }
// </code>
// 而这里以前挂了一个 `DarkOrbBossTargetPatch`，**把 null 的 target 改写成「被锁定的玩家」**
// ⇒ 每次黑暗球被动触发都必抛，而且是「未观察的任务异常」——它会在 finalizer 线程上重抛，
// 在 Godot/Mono 里就是**整个进程被打掉**。
// 黑暗球不需要任何选靶（它的伤害在激发时才落到「生命值最低的敌人」身上），所以这个补丁整个删掉。

/// <summary>
/// **黑暗充能球的「激发」也要按同一套规则选靶**
/// （用户 2026-11-26：「故障机器人 boss 的电球和黑球好像还是不是优先攻击奥斯提/瓦皮」）。
///
/// ⚠ 两件事必须分清：
///   · **被动**（`DarkOrb.Passive`）**绝对不能**塞目标 —— 原版会抛
///     `InvalidOperationException: Dark orbs cannot target creatures.`，而且那是**未观察的任务异常**，
///     会在 finalizer 线程上重抛、把整个进程打掉（v0.2.919 就是删掉了一个那样做的补丁）。
///     所以这里**没有**恢复 `DarkOrbBossTargetPatch`。
///   · **激发**（`Evoke`）才是真正造成伤害的那一步，而原版是
///     `CombatState.HittableEnemies.MinBy(c => c.CurrentHp)` —— 「全场血最少的敌人」：
///     多人时会落到别的玩家身上、也完全不管「先打召唤物」这条口径。
///
/// 做法：只对「球的主人是我们的 Boss」用 <see cref="BossOrbTargeting.Preferred"/> 换目标；
/// 其余步骤（激发音效 / `ActivateEvoke` / `CreatureCmd.Damage` 的 `EvokeVal` 与 `ValueProp.Unpowered`）
/// 逐字照抄原版，行为一致；算不出目标或不是我们的球 ⇒ `return true` 交还原版（零回归）。
/// </summary>
[HarmonyPatch(typeof(DarkOrb), nameof(DarkOrb.Evoke))]
internal static class DarkOrbEvokeTargetPatch
{
    private static System.Reflection.MethodInfo? _playEvokeSfx;
    private static bool _probed;
    private static int _logs;
    private static int _playerGuardLogs;

    [HarmonyPrefix]
    public static bool Prefix(DarkOrb __instance, PlayerChoiceContext playerChoiceContext,
                              ref Task<IEnumerable<Creature>> __result)
    {
        try
        {
            // ── ⓪ 玩家自己的黑暗球：**保证只打对手侧**（v0.2.981 防御性兜底）──────────
            // 用户 2026-11-27 报的是「玩家方故障机器人的黑球打队友了」：主因是 `BossOf` 把玩家的球
            // 误认成她（Boss）的球（已修）。这里再做一道**与机制无关**的兜底 —— 只要球的主人是
            // **真人玩家**，目标就从 `GetOpponentsOf(主人)` 里按「血最少」取（= 原版 `DarkOrb.Evoke`
            // 的语义），彻底杜绝「玩家的黑球落到玩家侧」这种结果（不管是翻侧残留、收窄残留还是将来别的写法）。
            var orbOwner = __instance.Owner;
            if (orbOwner != null && !BossOrbTargeting.IsShadowPlayer(orbOwner))
            {
                var me = orbOwner.Creature;
                var combat = __instance.CombatState ?? me?.CombatState;
                if (me != null && combat != null)
                {
                    Creature? lowest = null;
                    try
                    {
                        foreach (var c in combat.GetOpponentsOf(me))
                        {
                            if (c == null || !c.IsAlive || c.IsDead || !c.IsHittable) continue;
                            if (lowest == null || c.CurrentHp < lowest.CurrentHp) lowest = c;
                        }
                    }
                    catch { lowest = null; }

                    if (lowest != null)
                    {
                        __result = EvokeOnAsync(__instance, playerChoiceContext, lowest);
                        if (_playerGuardLogs++ < 20)
                        {
                            Core.LogLoudness.Diag($"[CharBoss] 黑球激发：玩家自己的黑暗球 ⇒ 只打对手侧 " +
                                                  $"{SafeName(lowest)}（原版「全场血最少」可能落到玩家侧）");
                        }
                        return false;
                    }
                }
                return true;      // 对手侧没得打 ⇒ 交还原版（原版同样什么都不做）
            }

            var boss = BossOrbTargeting.BossOf(__instance);
            if (boss == null) return true;                       // 不是我们的球 ⇒ 原版
            var want = BossOrbTargeting.Preferred(boss);
            if (want == null) return true;
            if (!want.IsAlive || want.IsDead || want.CombatState == null) return true;

            __result = EvokeOnAsync(__instance, playerChoiceContext, want);
            if (_logs++ < 30)
            {
                Core.LogLoudness.Diag($"[CharBoss] 黑球激发：{SafeName(boss.Creature)} 的黑暗球 ⇒ 目标改为 " +
                                      $"{SafeName(want)}（原版是「全场血最少的敌人」）");
            }
            return false;
        }
        catch { return true; }
    }

    /// <summary>照抄 <c>DarkOrb.Evoke</c> 的步骤，只把目标换成我们算出来的那只。</summary>
    private static async Task<IEnumerable<Creature>> EvokeOnAsync(
        DarkOrb orb, PlayerChoiceContext ctx, Creature target)
    {
        try { PlayEvokeSfx(orb); } catch { /* 音效拿不到就算了 */ }
        try { orb.ActivateEvoke(new[] { target }); } catch { /* 表现层失败不影响伤害 */ }
        try { await CreatureCmd.Damage(ctx, target, orb.EvokeVal, ValueProp.Unpowered, orb.Owner?.Creature); }
        catch { /* 伤害失败就当作没发生（与原版一样不向上抛） */ }
        return new[] { target };
    }

    /// <summary>`OrbModel.PlayEvokeSfx()` 是 protected ⇒ 反射取一次并缓存（拿不到就跳过音效）。</summary>
    private static void PlayEvokeSfx(OrbModel orb)
    {
        if (!_probed)
        {
            _probed = true;
            try { _playEvokeSfx = AccessTools.Method(typeof(OrbModel), "PlayEvokeSfx"); }
            catch { _playEvokeSfx = null; }
        }
        if (_playEvokeSfx == null) return;
        _playEvokeSfx.Invoke(orb, null);
    }

    private static string SafeName(Creature? c)
    {
        try { return c == null ? "<null>" : (c.Name ?? "?"); } catch { return "?"; }
    }
}
