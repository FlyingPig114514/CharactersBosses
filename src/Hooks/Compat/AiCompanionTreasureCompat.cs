using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Multiplayer.Game;

namespace CharacterBosses.Compat;

/// <summary>
/// 兼容补丁：让「AI 陪玩 / AICompanion」在游戏 **0.114.0** 上还能选宝箱遗物。
///
/// 现象（用户 2026-11-26 反馈）：和 AI 陪玩一起玩，走到**宝箱**时点遗物没反应，
/// `godot.log` 里每点一次刷一条：
/// <code>
/// System.MissingMethodException: Method not found:
///   'Void ...TreasureRoomRelicSynchronizer.OnPicked(Player, Nullable`1&lt;Int32&gt;)'
///     at AICompanion.RoomFixes.TryCompanionVote(TreasureRoomRelicSynchronizer sync, Nullable`1 excludeIndex, Boolean random)
///     at TreasureRoomRelicSynchronizer.PickRelicLocally_Patch1(TreasureRoomRelicSynchronizer this, Nullable`1 index)
///     at NTreasureRoomRelicCollection.PickRelic(NTreasureRoomRelicHolder holder)
/// </code>
///
/// 根因：AI 陪玩 2.0.3 是照 **0.113** 编译的 —— 它在 `TreasureRoomRelicSynchronizer.PickRelicLocally`
/// 上挂了 Prefix，先替陪玩投票，调用的是 0.113 的两参数重载 `OnPicked(Player, int?)`；
/// 而 **0.114.0 改成三参数** `OnPicked(Player, int?, int roundIndex)`（多了「第几轮投票」）。
/// ⇒ 那条 `callvirt` 在 JIT 阶段就抛 `MissingMethodException`，把它所在的 Prefix 整段打断，
/// 于是**玩家自己那次点击**也没走完（点遗物没反应）。`SkipRelicLocally` 上的 Prefix 也有同一处调用。
///
/// 为什么不能「把那条调用改接一下」（试过，已实测失败）：
/// Harmony 连这个方法体都读不出来 —— `PatchProcessor.GetOriginalInstructions(TryCompanionVote)`
/// 和 `harmony.Patch(TryCompanionVote, prefix:…)` 都直接抛同一个 `MissingMethodException`
/// （`.tmp/compat-test` 里的离线验证程序复现）。所以 transpiler / prefix 方案对**那个方法**都不可用。
///
/// 现在这个做法（两件小事，都不碰第三方 DLL）：
///   1. **卸掉** AI 陪玩挂在 `PickRelicLocally` / `SkipRelicLocally` 上的坏补丁
///      （只卸「补丁方法属于 AICompanion 程序集」的那几个，别人的补丁不动）⇒ 玩家的点击恢复正常。
///   2. 由**我们自己**在 `PickRelicLocally` / `SkipRelicLocally` 上加 Prefix，用 0.114 的三参数
///      `OnPicked(Player, int?, roundIndex)` 替陪玩投票（选牌逻辑尽量复用它自己的
///      `PickBestRelicIndex`，拿不到就随机且避开玩家已选的那张）⇒ 宝箱照常开、两个人各拿一件。
///
/// 只在「装了 AI 陪玩」且「游戏是 0.114+（三参数 OnPicked）」时才动手；否则完全静默。
/// </summary>
internal static class AiCompanionTreasureCompat
{
    private const string AiCompanionAssembly = "AICompanion";

    private static readonly object Gate = new();
    private static bool _installed;

    // 游戏侧成员（按运行时解析，107/111/113 三条构建都能编）
    private static MethodInfo? _onPickedNew;      // OnPicked(Player, int?, int)  0.114+
    private static MethodInfo? _onPickedOld;      // OnPicked(Player, int?)       0.113-
    private static FieldInfo? _currentRound;      // private int _currentRound
    private static FieldInfo? _voteReceived;      // PlayerVote.voteReceived

    // AI 陪玩侧成员（反射拿，不引它的程序集）
    private static PropertyInfo? _companionPlayerProp;   // AICompanion.Entry.CompanionPlayer
    private static MethodInfo? _pickBest;                // RoomFixes.PickBestRelicIndex(relics, companion, exclude)
    private static MethodInfo? _pickRandom;              // RoomFixes.PickRandomRelicIndex(relics, exclude)

    private static int _voteLogs;

    /// <summary>幂等安装。第一次调用发生在**宝箱房开始投票**时。</summary>
    public static void Install()
    {
        lock (Gate)
        {
            if (_installed) return;

            ResolveGameMembers();
            if (_onPickedNew == null)
            {
                // 老游戏里 OnPicked 还是两参数 ⇒ AI 陪玩本来就正常，什么都别做
                _installed = true;
                Logger.Log("[CharBoss] AI 陪玩兼容：本版本游戏的 OnPicked 仍是两参数 ⇒ 无需兼容");
                return;
            }

            var asm = FindAiCompanion();
            if (asm == null) return;      // 没装 AI 陪玩：不标记 _installed，之后装上还能补

            _installed = true;

            try { ResolveAiCompanionMembers(asm); }
            catch (Exception e) { Logger.Log($"[CharBoss] AI 陪玩兼容：反射取陪玩成员失败：{e.GetType().Name}: {e.Message}"); }

            try { RemoveBrokenPatches(asm); }
            catch (Exception e) { Logger.Log($"[CharBoss] AI 陪玩兼容：卸坏补丁失败：{e.GetType().Name}: {e.Message}"); }
        }
    }

    private static void ResolveGameMembers()
    {
        if (_onPickedNew != null || _onPickedOld != null) return;
        try
        {
            var t = typeof(TreasureRoomRelicSynchronizer);
            const BindingFlags F = BindingFlags.Public | BindingFlags.Instance;
            _onPickedNew = t.GetMethod("OnPicked", F, null, new[] { typeof(Player), typeof(int?), typeof(int) }, null);
            _onPickedOld = t.GetMethod("OnPicked", F, null, new[] { typeof(Player), typeof(int?) }, null);
            _currentRound = AccessTools.Field(t, "_currentRound");
            _voteReceived = AccessTools.Field(t.GetNestedType("PlayerVote", BindingFlags.Public | BindingFlags.NonPublic)!, "voteReceived");
        }
        catch (Exception e)
        {
            Logger.Log($"[CharBoss] AI 陪玩兼容：解析宝箱成员失败：{e.GetType().Name}: {e.Message}");
        }
    }

    private static void ResolveAiCompanionMembers(Assembly asm)
    {
        var entry = asm.GetType("AICompanion.Entry");
        _companionPlayerProp = entry?.GetProperty("CompanionPlayer", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

        var roomFixes = asm.GetType("AICompanion.RoomFixes");
        if (roomFixes != null)
        {
            foreach (var m in roomFixes.GetMethods(BindingFlags.NonPublic | BindingFlags.Static))
            {
                if (m.Name == "PickBestRelicIndex" && m.GetParameters().Length == 3) _pickBest = m;
                else if (m.Name == "PickRandomRelicIndex" && m.GetParameters().Length == 2) _pickRandom = m;
            }
        }

        Logger.Log($"[CharBoss] AI 陪玩兼容：CompanionPlayer={(_companionPlayerProp != null ? "OK" : "找不到")}、" +
                  $"PickBestRelicIndex={(_pickBest != null ? "OK" : "找不到")}");
    }

    private static Assembly? FindAiCompanion()
    {
        try
        {
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (string.Equals(a.GetName().Name, AiCompanionAssembly, StringComparison.OrdinalIgnoreCase)) return a;
            }
        }
        catch { /* 忽略 */ }
        return null;
    }

    /// <summary>
    /// 卸掉 AI 陪玩挂在 `PickRelicLocally` / `SkipRelicLocally` 上的补丁（只看「补丁方法来自 AICompanion 程序集」，
    /// 别的模组的补丁一律不碰）。它那几个 Prefix 里就藏着会抛 `MissingMethodException` 的调用。
    /// </summary>
    private static void RemoveBrokenPatches(Assembly aiAsm)
    {
        var targets = new List<MethodBase>();
        var pick = AccessTools.Method(typeof(TreasureRoomRelicSynchronizer), "PickRelicLocally", new[] { typeof(int?) });
        if (pick != null) targets.Add(pick);
        var skip = AccessTools.Method(typeof(TreasureRoomRelicSynchronizer), "SkipRelicLocally");
        if (skip != null) targets.Add(skip);

        var harmony = new Harmony("CharacterBosses.aicompanion.treasure");
        int removed = 0;
        var names = new List<string>();

        foreach (var target in targets)
        {
            var info = Harmony.GetPatchInfo(target);
            if (info == null) continue;

            var all = new List<object>();
            foreach (var p in info.Prefixes) all.Add(p);
            foreach (var p in info.Postfixes) all.Add(p);
            foreach (var p in info.Transpilers) all.Add(p);
            foreach (var p in info.Finalizers) all.Add(p);

            foreach (var p in all)
            {
                var pm = PatchMethodOf(p);
                if (pm == null) continue;
                if (pm.DeclaringType?.Assembly != aiAsm) continue;      // 不是 AI 陪玩的补丁 ⇒ 不动

                try
                {
                    harmony.Unpatch(target, pm);
                    removed++;
                    names.Add($"{target.Name}←{pm.DeclaringType?.Name}.{pm.Name}");
                }
                catch (Exception e)
                {
                    Logger.Log($"[CharBoss] AI 陪玩兼容：卸载 {pm.Name} 失败：{e.GetType().Name}: {e.Message}");
                }
            }
        }

        Logger.Log(removed > 0
            ? $"[CharBoss] AI 陪玩兼容：已卸掉 {removed} 个会抛 MissingMethodException 的宝箱补丁 ⇒ {string.Join("、", names)}（改由本模组用新签名替陪玩投票）"
            : "[CharBoss] AI 陪玩兼容：没找到 AI 陪玩挂在宝箱上的补丁（可能作者已修复）");
    }

    /// <summary>Patch 对象的 `PatchMethod` 字段名在不同 Harmony 版本里可能是属性也可能是字段 ⇒ 反射读。</summary>
    private static MethodInfo? PatchMethodOf(object patch)
    {
        try
        {
            var t = patch.GetType();
            var p = t.GetProperty("PatchMethod", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (p != null) return p.GetValue(patch) as MethodInfo;
            var f = t.GetField("PatchMethod", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    ?? t.GetField("patchMethod", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            return f?.GetValue(patch) as MethodInfo;
        }
        catch { return null; }
    }

    // ───────────────────────── 替陪玩投票 ─────────────────────────

    /// <summary>
    /// 玩家点遗物（`index` 非 null）/ 放弃（null）时，先替陪玩投出它自己那一票。
    /// 幂等：陪玩已经投过就直接返回（绝不影响玩家自己那一票）。
    /// </summary>
    public static void VoteForCompanion(TreasureRoomRelicSynchronizer? sync, int? excludeIndex, bool random)
    {
        try
        {
            if (sync == null) return;
            if (_onPickedNew == null && _onPickedOld == null) return;

            var companion = CompanionPlayer();
            if (companion == null) return;
            try { if (companion.Creature == null) return; } catch { return; }

            var relics = sync.CurrentRelics;
            if (relics == null || relics.Count == 0) return;

            if (HasVoted(sync, companion)) return;

            int index;
            if (random || _pickBest == null)
            {
                index = PickRandom(relics, excludeIndex);
            }
            else
            {
                index = InvokeIndex(_pickBest, new object?[] { relics, companion, excludeIndex }, relics, excludeIndex);
            }

            if (_onPickedNew != null)
            {
                _onPickedNew.Invoke(sync, new object?[] { companion, (int?)index, CurrentRound(sync) });
            }
            else
            {
                _onPickedOld!.Invoke(sync, new object?[] { companion, (int?)index });
            }

            if (_voteLogs++ < 20)
                Logger.Log($"[CharBoss] AI 陪玩兼容：替陪玩投宝箱遗物 index={index}/{relics.Count}" +
                           (excludeIndex.HasValue ? $"（避开玩家选的 {excludeIndex}）" : ""));
        }
        catch (Exception e)
        {
            if (_voteLogs++ < 20)
                Logger.Log($"[CharBoss] AI 陪玩兼容：替陪玩投票失败：{e.GetType().Name}: {e.Message}");
        }
    }

    /// <summary>玩家放弃选遗物时，陪玩也放弃（否则宝箱会一直等它那一票）。</summary>
    public static void SkipForCompanion(TreasureRoomRelicSynchronizer? sync)
    {
        try
        {
            if (sync == null || (_onPickedNew == null && _onPickedOld == null)) return;
            var companion = CompanionPlayer();
            if (companion == null) return;
            if (HasVoted(sync, companion)) return;

            if (_onPickedNew != null) _onPickedNew.Invoke(sync, new object?[] { companion, (int?)null, CurrentRound(sync) });
            else _onPickedOld!.Invoke(sync, new object?[] { companion, (int?)null });
        }
        catch (Exception e)
        {
            if (_voteLogs++ < 20)
                Logger.Log($"[CharBoss] AI 陪玩兼容：陪玩跟随放弃失败：{e.GetType().Name}: {e.Message}");
        }
    }

    private static Player? CompanionPlayer()
    {
        try { return _companionPlayerProp?.GetValue(null) as Player; }
        catch { return null; }
    }

    private static bool HasVoted(TreasureRoomRelicSynchronizer sync, Player player)
    {
        try
        {
            var vote = sync.GetPlayerVote(player);
            if (vote == null) return false;
            if (_voteReceived == null) return true;
            return _voteReceived.GetValue(vote) is bool b && b;
        }
        catch { return true; }   // 读不出来就当已投（宁可不投票，也不要卡住宝箱）
    }

    private static int CurrentRound(TreasureRoomRelicSynchronizer sync)
    {
        try { return _currentRound != null ? Convert.ToInt32(_currentRound.GetValue(sync) ?? 0) : 0; }
        catch { return 0; }
    }

    private static int InvokeIndex(MethodInfo method, object?[] args, IReadOnlyList<MegaCrit.Sts2.Core.Models.RelicModel> relics, int? excludeIndex)
    {
        try
        {
            var raw = method.Invoke(null, args);
            if (raw != null)
            {
                int idx = Convert.ToInt32(raw);
                if (idx >= 0 && idx < relics.Count && (!excludeIndex.HasValue || excludeIndex.Value != idx)) return idx;
            }
        }
        catch { /* 复用它的评分失败就退回随机 */ }
        return PickRandom(relics, excludeIndex);
    }

    private static int PickRandom(IReadOnlyList<MegaCrit.Sts2.Core.Models.RelicModel> relics, int? excludeIndex)
    {
        int count = relics?.Count ?? 0;
        if (count <= 0) return 0;

        // 优先复用它自己的随机（会避开玩家已选的那张）
        if (_pickRandom != null)
        {
            try
            {
                var raw = _pickRandom.Invoke(null, new object?[] { relics, excludeIndex });
                if (raw != null)
                {
                    int v = Convert.ToInt32(raw);
                    if (v >= 0 && v < count) return v;
                }
            }
            catch { /* 忽略，走下面自己的随机 */ }
        }

        var pool = new List<int>(count);
        for (int i = 0; i < count; i++)
        {
            if (!excludeIndex.HasValue || excludeIndex.Value != i) pool.Add(i);
        }
        if (pool.Count == 0) return 0;
        return pool[Random.Shared.Next(pool.Count)];
    }
}

/// <summary>宝箱房开始投票时安装一次（那时 AI 陪玩早就加载完了）。</summary>
[HarmonyPatch(typeof(TreasureRoomRelicSynchronizer), nameof(TreasureRoomRelicSynchronizer.BeginRelicPicking))]
internal static class AiCompanionTreasureInstallPatch
{
    private static void Postfix()
    {
        try { AiCompanionTreasureCompat.Install(); }
        catch { /* 兼容补丁绝不影响本体 */ }
    }
}

/// <summary>
/// 玩家点遗物时：先替陪玩投票，再走原版流程（原版这一步会 enqueue 玩家自己那一票）。
/// 卸掉 AI 陪玩那个坏 Prefix 之后，这个位置就由我们接手。
/// </summary>
[HarmonyPatch(typeof(TreasureRoomRelicSynchronizer), nameof(TreasureRoomRelicSynchronizer.PickRelicLocally))]
internal static class AiCompanionTreasureVotePatch
{
    private static void Prefix(TreasureRoomRelicSynchronizer __instance, int? index)
    {
        try { AiCompanionTreasureCompat.VoteForCompanion(__instance, index, random: false); }
        catch { /* 忽略 */ }
    }
}

/// <summary>玩家放弃时，陪玩跟着放弃，避免宝箱一直等待。</summary>
[HarmonyPatch(typeof(TreasureRoomRelicSynchronizer), nameof(TreasureRoomRelicSynchronizer.SkipRelicLocally))]
internal static class AiCompanionTreasureSkipPatch
{
    private static void Prefix(TreasureRoomRelicSynchronizer __instance)
    {
        try { AiCompanionTreasureCompat.SkipForCompanion(__instance); }
        catch { /* 忽略 */ }
    }
}
