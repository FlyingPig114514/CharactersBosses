using System;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;

namespace CharacterBosses.Patches;

/// <summary>
/// 「**地狱狂徒**」自动打出打击时的**专属演出**（用户 2026-11-08 逐条纠正后的版本）。
///
/// 反编译实证（`Hellraiser` 卡 vs `HellraiserPower`）：
///   · 打出**地狱狂徒这张牌**时：往 `CombatVfxContainer` 加 `NHellraiserVfx`（**范围很大**的刀阵）+ `PowerUp` 动作；
///   · 地狱狂徒**自动打出打击**时（`AfterCardDrawnEarly` 里那条攻击指令）：
///       `WithHitFx("vfx/hellraiser_attack_vfx", …).WithAttackerAnim("Cast", …)
///        .SpawningHitVfxOnEachCreature().WithHitVfxSpawnedAtBase()`
///     ⇒ **脚下一小片刀阵** + **只有 Cast（铁山靠）这一个动作**，没有后续那段普通的挥刀。
///
/// 用户 2026-11-08 的口径：
///   · 现在用的 `NHellraiserVfx` 是「打地狱狂徒这张牌」的那个大刀阵，**用错了**；
///   · 要的是脚下那个小的刀阵 + 只要铁山靠，**后面那段普通出牌动画要拦掉**。
///
/// 所以这里：
///   ① `Cast` 动作照旧触发；
///   ② 小刀阵按路径 `vfx/hellraiser_attack_vfx` 用反射去 `VfxCmd` 里找能播的方法（拿不到就静默跳过）；
///   ③ 自动打出期间**临时压制她生物的攻击动画**（`TriggerAnim` 前缀），打完立刻解除。
/// </summary>
internal static class HellraiserVfx
{
    /// <summary>自动打出期间要压制攻击动画的那个生物（只压制它自己，别人完全不受影响）。</summary>
    private static Creature? _suppressed;

    internal static bool IsSuppressed(Creature? creature)
    {
        try { return creature != null && _suppressed != null && ReferenceEquals(creature, _suppressed); }
        catch { return false; }
    }

    internal static void BeginSuppress(Creature? her)
    {
        try { _suppressed = her; } catch { _suppressed = null; }
    }

    internal static void EndSuppress()
    {
        _suppressed = null;
    }

    /// <summary>打出前：只要「Cast」（铁山靠）这一个动作 + 脚下的小刀阵。</summary>
    internal static void Spawn(Creature? her)
    {
        if (her == null) return;

        try { _ = CreatureCmd.TriggerAnim(her, "Cast", 0.6f); }
        catch (Exception e) { Logger.Log("[CharBoss] 地狱狂徒动作（Cast）失败（忽略）: " + e.Message); }

        PlayAttackVfx(her);
    }

    /// <summary>按路径播「地狱狂徒命中」那一小片刀阵（反射调用，拿不到就跳过）。</summary>
    private static void PlayAttackVfx(Creature her)
    {
        try
        {
            var vfxCmd = AccessTools.TypeByName("MegaCrit.Sts2.Core.Commands.VfxCmd");
            if (vfxCmd == null) return;

            foreach (var name in new[] { "Play", "Spawn", "Create", "PlayAtBase", "SpawnAtBase" })
            {
                foreach (var method in vfxCmd.GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    if (method.Name != name) continue;
                    var ps = method.GetParameters();
                    if (ps.Length != 2) continue;
                    if (ps[0].ParameterType != typeof(string) || ps[1].ParameterType != typeof(Creature)) continue;
                    method.Invoke(null, new object[] { "vfx/hellraiser_attack_vfx", her });
                    Logger.Log("[CharBoss] 地狱狂徒：脚下刀阵已按路径 vfx/hellraiser_attack_vfx 播放（VfxCmd." + name + "）");
                    return;
                }
            }
            Logger.Log("[CharBoss] 地狱狂徒：没在 VfxCmd 上找到 (string, Creature) 形式的播放入口，跳过小刀阵（不影响出牌）");
        }
        catch (Exception e) { Logger.Log("[CharBoss] 地狱狂徒小刀阵播放失败（忽略）: " + e.Message); }
    }

    /// <summary>兼容旧调用。</summary>
    internal static void Flash(Creature? victim) => Spawn(victim);
}

/// <summary>
/// 自动打出期间**只拦她自己那条攻击动画**：`CreatureCmd.TriggerAnim` 前缀。
/// 命中条件：目标正是被压制的那只生物、且动作名是普通攻击类（Attack/Hit/Slash/Stab/Swing）。
/// 其它生物、其它动作（包括 Cast）一律放行。
/// </summary>
[HarmonyPatch(typeof(CreatureCmd), nameof(CreatureCmd.TriggerAnim))]
internal static class HellraiserAnimSuppressPatch
{
    [HarmonyPrefix]
    private static bool Prefix(Creature creature, string triggerName)
    {
        try
        {
            if (!HellraiserVfx.IsSuppressed(creature)) return true;
            string n = triggerName ?? string.Empty;
            if (n.IndexOf("Attack", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("Slash", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("Stab", StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("Swing", StringComparison.OrdinalIgnoreCase) >= 0
                || n == "Hit")
            {
                Logger.Log($"[CharBoss] 地狱狂徒：拦掉后续的普通攻击动画（{n}）");
                return false;
            }
        }
        catch { /* 出错就放行 */ }
        return true;
    }
}