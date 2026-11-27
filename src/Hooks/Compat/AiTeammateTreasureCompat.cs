using System;
using System.Collections;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;

namespace CharacterBosses.Compat;

/// <summary>
/// 兼容补丁：让「一台机器操控多个座位」（穷仔的尖塔搭子 / <c>AiTeammate</c> 的**统帅模式**）在宝箱房能正常选遗物。
///
/// ## 现象（用户 2026-11-27）
/// 「所有玩家都由我自己操控，结果卡在宝箱：非 1 号位选遗物，1 号位跟着选；换玩家后又只剩 1 号位选了。」
///
/// ## 根因（反编译 `.refs/sts2_113.dll` + `godot.log` 实证）
/// <code>
/// // TreasureRoomRelicSynchronizer
/// private readonly ulong _localPlayerId;                                  // 构造时定死 = 本机玩家
/// private Player LocalPlayer => _runState.GetPlayer(_localPlayerId);
///
/// public void PickRelicLocally(int? index) {
///     _predictedVote = new PlayerVote { index = index, voteReceived = true };
///     _actionQueueSynchronizer.RequestEnqueue(new PickRelicAction(LocalPlayer, index, _currentRound));  // ← 记在本机玩家名下
///     VotesChanged?.Invoke();
/// }
/// public void SkipRelicLocally() => PickRelicLocally(null);
/// </code>
/// 而「切视角」改的是**另一个东西** —— 游戏的 <c>LocalContext.NetId</c>（"我"是谁）：
/// <code>
/// public static Player? GetMe(IPlayerCollection c) => c.GetPlayer(NetId.Value);   // LocalContext
/// </code>
/// ⇒ 视角切到 101 号座位后，`LocalContext.NetId = 101`，但 <c>_localPlayerId</c> 还是 **1**
/// ⇒ 这一票被记到 1 号位（"1 号位跟着选"），其他座位永远没投过票（"换回来只剩 1 号位"），
/// 同一轮里 1 号位的票被反复覆盖 ⇒ 票凑不齐 ⇒ **宝箱卡住**。
///
/// ## 判据（v0.2.971 重写：不再依赖 AiTeammate 内部字段）
/// 只看两个 id：
///   · `LocalContext.NetId` = **游戏现在认为的"我"**（切视角时会被改）；
///   · `_localPlayerId`     = **原版会把这一票记到谁名下**（构造时定死）。
/// 两者**不一致** ⇒ 接管，把这一票记到 `LocalContext.NetId` 那位名下；一致 ⇒ `return true`，一个字节都不改。
///
/// 为什么不用 `AiTeammate.CommanderMode.Selected`（v0.2.970 的第一版就是那样，实测没生效）：
/// 它的 <c>Selected</c> 是 <c>LocalContext.GetMe(...)</c> 的包装，而 <c>Active</c> 在宝箱房（非战斗）时的取值
/// 依赖它自己的会话状态，实测在宝箱那一刻没满足条件 ⇒ 我们一次都没接管。
/// 现在直接比对上面两个 id，与任何模组的内部状态无关（顺带对别的"切视角"模组也成立）。
/// 只在装了 `AiTeammate` 时才启用，避免影响其它场景。
///
/// ⚠ `SkipRelicLocally()` 原版就是 `PickRelicLocally(null)` ⇒ 放弃选遗物一并修好，不用另外挂钩子。
/// </summary>
internal static class AiTeammateTreasureCompat
{
    private const string TeammateAssembly = "AiTeammate";

    private static readonly object Gate = new();
    private static bool _resolved;
    private static bool _teammatePresent;

    // 游戏侧（反射读私有成员）
    private static FieldInfo? _fRunState;             // _runState（IRunState，同时是 IPlayerCollection）
    private static FieldInfo? _fLocalPlayerId;        // _localPlayerId（原版会把票记在这个 id 名下）
    private static FieldInfo? _fRound;                // _currentRound
    private static FieldInfo? _fPredictedVote;        // _predictedVote
    private static FieldInfo? _fRelics;               // _currentRelics
    private static FieldInfo? _fVotesChanged;         // VotesChanged（字段式事件）
    private static FieldInfo? _fActionQueue;          // _actionQueueSynchronizer
    private static Type? _tPlayerVote;                // TreasureRoomRelicSynchronizer+PlayerVote
    private static ConstructorInfo? _ctorPlayerVote;
    private static ConstructorInfo? _ctorPickAction;  // (Player,int?) 或 (Player,int?,int)
    private static int _pickActionArity;

    private static int _logs;

    /// <summary>幂等解析（宝箱房开始投票时调一次）。</summary>
    public static void Install()
    {
        lock (Gate)
        {
            if (_resolved) return;
            _resolved = true;

            try { ResolveGame(); }
            catch (Exception e) { Logger.Log("[CharBoss] 宝箱投票兼容：解析宝箱成员失败: " + e.Message); }

            try
            {
                foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (!string.Equals(a.GetName().Name, TeammateAssembly, StringComparison.OrdinalIgnoreCase)) continue;
                    _teammatePresent = true;
                    break;
                }
            }
            catch { _teammatePresent = false; }

            Logger.Log(_teammatePresent
                ? "[CharBoss] 宝箱投票兼容：已就绪（LocalContext.NetId ≠ 本机玩家 时，票记到当前视角那位名下）"
                : "[CharBoss] 宝箱投票兼容：没装 AiTeammate ⇒ 不介入");
        }
    }

    /// <summary>
    /// 这一票该记在谁名下：`LocalContext.NetId`（当前视角）与 `_localPlayerId`（原版记票对象）不一致时返回前者，
    /// 否则返回 null（= 交还原版）。<paramref name="why"/> 是给日志看的一句话。
    /// </summary>
    internal static Player? SeatOverride(TreasureRoomRelicSynchronizer? sync, out string why)
    {
        why = string.Empty;
        try
        {
            if (sync == null) { why = "sync 为空"; return null; }
            if (!_teammatePresent) { why = "没装 AiTeammate ⇒ 原版"; return null; }

            ulong? acting = null;
            try { acting = LocalContext.NetId; } catch { acting = null; }

            ulong credited = 0;
            bool hasCredited = false;
            try
            {
                if (_fLocalPlayerId?.GetValue(sync) is ulong u) { credited = u; hasCredited = true; }
            }
            catch { hasCredited = false; }

            if (!acting.HasValue || !hasCredited)
            {
                why = $"读不到 id（LocalContext.NetId={acting?.ToString() ?? "null"}、" +
                      $"_localPlayerId={(hasCredited ? credited.ToString() : "?")}）⇒ 原版";
                return null;
            }
            if (acting.Value == credited)
            {
                why = $"两者相同（{credited}）⇒ 原版";
                return null;
            }

            Player? who = null;
            try
            {
                var collection = _fRunState?.GetValue(sync) as IPlayerCollection;
                who = LocalContext.GetMe(collection);
            }
            catch { who = null; }

            if (who == null)
            {
                why = $"LocalContext.NetId={acting.Value} 却取不到那位玩家 ⇒ 原版";
                return null;
            }

            why = $"LocalContext.NetId={acting.Value} ≠ _localPlayerId={credited} ⇒ 接管，记到 P#{who.NetId}";
            return who;
        }
        catch (Exception e)
        {
            why = "判据异常: " + e.Message;
            return null;
        }
    }

    /// <summary>按原版顺序替「当前视角那位」投出这一票（`index = null` 就是放弃）。</summary>
    internal static void PickFor(TreasureRoomRelicSynchronizer sync, Player seat, int? index)
    {
        var relics = _fRelics?.GetValue(sync) as IList;
        if (relics == null)
        {
            if (_logs++ < 20) Logger.Log("[CharBoss] 宝箱投票兼容：投票还没开始 ⇒ 按原版（本次点击无效）");
            return;
        }

        int round = _fRound?.GetValue(sync) is int r ? r : 0;

        var aqs = _fActionQueue?.GetValue(sync) as ActionQueueSynchronizer;
        if (aqs == null) throw new InvalidOperationException("拿不到 _actionQueueSynchronizer");

        // PickRelicAction 的构造：0.113 是 (Player,int?)、0.114+ 是 (Player,int?,int) ⇒ 反射造（三条构建都能编）
        var action = CreatePickRelicAction(seat, index, round);
        if (action == null) throw new InvalidOperationException("造不出 PickRelicAction（构造签名不认识）");
        aqs.RequestEnqueue(action);

        try
        {
            if (_fPredictedVote != null && _tPlayerVote != null && _ctorPlayerVote != null)
            {
                var vote = _ctorPlayerVote.Invoke(null);
                _tPlayerVote.GetField("index")?.SetValue(vote, index);
                _tPlayerVote.GetField("voteReceived")?.SetValue(vote, true);
                _fPredictedVote.SetValue(sync, vote);
            }
        }
        catch { /* 预测票只是界面表现 */ }

        try { (_fVotesChanged?.GetValue(sync) as Action)?.Invoke(); } catch { /* 忽略 */ }

        if (_logs++ < 20)
        {
            Logger.Log($"[CharBoss] 宝箱投票兼容：这一票记到 P#{seat.NetId}（index={(index.HasValue ? index.Value.ToString() : "放弃")}，" +
                       $"轮次 {round}；原版会记在本机玩家名下）");
        }
    }

    private static void ResolveGame()
    {
        var t = typeof(TreasureRoomRelicSynchronizer);
        const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic;
        _fRunState = t.GetField("_runState", F);
        _fLocalPlayerId = t.GetField("_localPlayerId", F);
        _fActionQueue = t.GetField("_actionQueueSynchronizer", F);
        _fRound = t.GetField("_currentRound", F);
        _fPredictedVote = t.GetField("_predictedVote", F);
        _fRelics = t.GetField("_currentRelics", F);
        _fVotesChanged = t.GetField("VotesChanged", F) ?? t.GetField("VotesChanged", BindingFlags.Instance | BindingFlags.Public);
        _tPlayerVote = t.GetNestedType("PlayerVote", BindingFlags.Public | BindingFlags.NonPublic);
        _ctorPlayerVote = _tPlayerVote?.GetConstructor(Type.EmptyTypes);

        try
        {
            foreach (var c in typeof(PickRelicAction).GetConstructors(BindingFlags.Public | BindingFlags.Instance))
            {
                var ps = c.GetParameters();
                if (ps.Length == 3) { _ctorPickAction = c; _pickActionArity = 3; break; }
                if (ps.Length == 2 && _ctorPickAction == null) { _ctorPickAction = c; _pickActionArity = 2; }
            }
        }
        catch { _ctorPickAction = null; }

        Logger.Log($"[CharBoss] 宝箱投票兼容：成员解析 `_runState`={Yes(_fRunState)} `_localPlayerId`={Yes(_fLocalPlayerId)} " +
                   $"`_actionQueueSynchronizer`={Yes(_fActionQueue)} `_currentRound`={Yes(_fRound)} " +
                   $"`_predictedVote`={Yes(_fPredictedVote)} `_currentRelics`={Yes(_fRelics)} " +
                   $"`VotesChanged`={Yes(_fVotesChanged)} `PlayerVote`={Yes(_tPlayerVote)} " +
                   $"`PickRelicAction`={(_ctorPickAction != null ? _pickActionArity + " 参数" : "找不到")}");
    }

    private static string Yes(object? o) => o == null ? "找不到" : "OK";

    /// <summary>反射造一个 <c>PickRelicAction</c>（兼容 0.113 两参数与 0.114+ 三参数）。</summary>
    private static GameAction? CreatePickRelicAction(Player seat, int? index, int round)
    {
        if (_ctorPickAction == null) return null;
        var args = _pickActionArity >= 3 ? new object?[] { seat, index, round } : new object?[] { seat, index };
        try { return _ctorPickAction.Invoke(args) as GameAction; } catch { return null; }
    }
}

/// <summary>宝箱房开始投票时解析一次。</summary>
[HarmonyPatch(typeof(TreasureRoomRelicSynchronizer), nameof(TreasureRoomRelicSynchronizer.BeginRelicPicking))]
internal static class AiTeammateTreasureInstallPatch
{
    private static void Postfix()
    {
        try { AiTeammateTreasureCompat.Install(); }
        catch { /* 兼容补丁绝不影响本体 */ }
    }
}

/// <summary>
/// **票记到「当前视角那位」名下**：判据见 <see cref="AiTeammateTreasureCompat.SeatOverride"/>。
/// 每次点击都会打一行探针日志（限流 20），下次一看就知道是「接管了」还是「为什么没接管」。
/// </summary>
[HarmonyPatch(typeof(TreasureRoomRelicSynchronizer), nameof(TreasureRoomRelicSynchronizer.PickRelicLocally))]
internal static class AiTeammateRelicPickPatch
{
    [ThreadStatic] private static bool _inside;
    private static int _probes;

    [HarmonyPrefix]
    private static bool Prefix(TreasureRoomRelicSynchronizer __instance, int? index)
    {
        if (_inside) return true;
        try
        {
            var seat = AiTeammateTreasureCompat.SeatOverride(__instance, out string why);
            if (_probes++ < 20)
            {
                Logger.Log($"[CharBoss] 宝箱投票探针：index={(index.HasValue ? index.Value.ToString() : "放弃")}｜{why}");
            }
            if (seat == null) return true;

            _inside = true;
            try { AiTeammateTreasureCompat.PickFor(__instance, seat, index); }
            finally { _inside = false; }
            return false;
        }
        catch (Exception e)
        {
            _inside = false;
            Logger.Log("[CharBoss] 宝箱投票兼容：接管失败（按原版执行）: " + e.Message);
            return true;
        }
    }
}
