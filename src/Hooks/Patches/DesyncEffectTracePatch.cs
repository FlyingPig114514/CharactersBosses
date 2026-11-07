using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.ValueProps;
using CharacterBosses.Monsters;
using HarmonyLib;

namespace CharacterBosses.Patches;

/// <summary>
/// **逐次 HP / 格挡变化追踪**（v0.2.449，为联机不同步排查加的**只读**探针）。
///
/// ## 为什么需要
/// 2026-11-06 的 RitsuLib 诊断包（`ritsulib_state_divergence_20261106_150225…zip`）复盘结论是：
/// 两端**动作完全一致**（同 id、同卡、同目标，连「弃牌选择」都逐字相同），
/// 分叉发生在「**同一个已执行动作的结算**」上 —— 奇巧自动打出的「翻越撑击」7 点群体伤害
/// 在房主身上赶在 `After enemy turn end` 校验和之前落地，在客人身上没有落地（或晚于它）。
/// 而那份包里**一条伤害/格挡记录都没有**（`damage`/`LoseHp`/`ApplyPower` 命中数全是 0）
/// ⇒ 只能靠两份状态快照反推，永远差最后一步。
///
/// 所以这里给两个**唯一的数值入口**各挂一条日志：
///   · <see cref="Creature.LoseHpInternal"/>（掉血的必经之路：伤害、反伤、自伤都走它）；
///   · <see cref="Creature.GainBlockInternal"/>（获得格挡的必经之路）。
/// 两端都记 ⇒ 两份 `mod_log.txt` 按时间对齐一比，就能看出**同一笔伤害在哪一端、落没落地**。
///
/// ## 为什么它自己是安全的
///   · **只读 + 只写日志**，绝不修改任何战斗状态（帧回调里改状态正是历史几次不同步的根因）；
///   · 只在本场战斗**有我们这个模组的 Boss** 时才记（普通战斗一行都不打）；
///   · 有硬上限（<see cref="Trace.MaxLines"/>），每行都很短 —— 不会把日志撑爆、也不会拖慢回合。
/// </summary>
internal static class Trace
{
    /// <summary>总共最多打多少行（防止长局把 mod_log 撑爆）。</summary>
    internal const int MaxLines = 4000;

    private static int _lines;
    private static bool _roleLogged;

    internal static bool Enabled => _lines < MaxLines;

    /// <summary>本场战斗里有没有我们的 Boss（没有就整段静音，普通战斗零开销）。</summary>
    internal static bool OurFight()
    {
        try
        {
            var combat = CombatManager.Instance?.DebugOnlyGetState();
            if (combat == null) return false;
            foreach (var c in combat.Enemies)
            {
                if (c?.Monster is CharBossMonster) return true;
            }
        }
        catch { /* 拿不到就不记 */ }
        return false;
    }

    internal static void Log(string body)
    {
        if (_lines >= MaxLines) return;
        _lines++;
        try
        {
            if (!_roleLogged)
            {
                _roleLogged = true;
                string role;
                try { role = Core.NetRole.IsGuest ? "客人" : Core.NetRole.Type.ToString(); }
                catch { role = "?"; }
                Logger.Log($"[CharBoss] 效果追踪：本机角色={role}（两端各一份 mod_log，按时间对齐比对）");
            }
            Logger.Log($"[CharBoss] 效果追踪 {body}");
        }
        catch { /* 忽略 */ }
    }

    /// <summary>叫她一个短名字（怪物 id 优先 / 角色名 / NetId），两端必须一致。</summary>
    internal static string NameOf(Creature? creature)
    {
        try
        {
            if (creature == null) return "?";
            string? entry = null;
            try { entry = creature.Monster?.Id.Entry; } catch { entry = null; }
            if (!string.IsNullOrEmpty(entry)) return entry!;
            try
            {
                var p = creature.Player;
                if (p != null)
                {
                    var ch = p.Character;
                    return ch != null ? ch.GetType().Name : ("NetId=" + p.NetId);
                }
            }
            catch { /* 忽略 */ }
            return creature.Name ?? "?";
        }
        catch { return "?"; }
    }

    /// <summary>
    /// 调用栈线索（只留前三层「像调用方」的帧），用来区分
    /// 「卡牌伤害 / 荆棘反伤 / 我们自己的遗物桥 / 充能球 / 状态牌」。
    /// </summary>
    internal static string Hint()
    {
        try
        {
            var frames = Environment.StackTrace.Split('\n');
            var parts = new List<string>();
            foreach (var raw in frames)
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;
                if (line.Contains("CharacterBosses.Patches.Trace")) continue;
                if (line.Contains("LoseHpTracePatch")) continue;
                if (line.Contains("GainBlockTracePatch")) continue;
                if (line.Contains("System.Environment")) continue;

                if (line.Contains("CharacterBosses")
                    || line.Contains("ThornsPower")
                    || line.Contains("CreatureCmd")
                    || line.Contains("CardCmd")
                    || line.Contains("OrbCmd")
                    || line.Contains("PowerCmd"))
                {
                    int at = line.IndexOf(" at ", StringComparison.Ordinal);
                    string trimmed = at >= 0 ? line.Substring(at + 4) : line;
                    int paren = trimmed.IndexOf('(');
                    if (paren > 0) trimmed = trimmed.Substring(0, paren);
                    parts.Add(trimmed);
                    if (parts.Count >= 3) break;
                }
            }
            return parts.Count > 0 ? string.Join(" < ", parts) : "?";
        }
        catch { return "?"; }
    }
}

/// <summary>掉血的唯一入口（伤害 / 荆棘反伤 / 自伤都走它）。</summary>
[HarmonyPatch(typeof(Creature), nameof(Creature.LoseHpInternal))]
internal static class LoseHpTracePatch
{
    [HarmonyPrefix]
    public static void Prefix(Creature __instance, decimal amount, ValueProp props)
    {
        try
        {
            if (!Trace.Enabled) return;
            if (!Trace.OurFight()) return;

            Trace.Log($"HP  {Trace.NameOf(__instance)} −{amount:0.##}" +
                      $"（格挡 {__instance.Block} HP {__instance.CurrentHp}/{__instance.MaxHp} props={props}）" +
                      $"← {Trace.Hint()}");
        }
        catch { /* 忽略 */ }
    }
}

/// <summary>获得格挡的唯一入口。</summary>
[HarmonyPatch(typeof(Creature), nameof(Creature.GainBlockInternal))]
internal static class GainBlockTracePatch
{
    [HarmonyPrefix]
    public static void Prefix(Creature __instance, decimal amount)
    {
        try
        {
            if (!Trace.Enabled) return;
            if (!Trace.OurFight()) return;

            Trace.Log($"BLK {Trace.NameOf(__instance)} +{amount:0.##}（原格挡 {__instance.Block}）← {Trace.Hint()}");
        }
        catch { /* 忽略 */ }
    }
}
