using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using CharacterBosses.Monsters;

namespace CharacterBosses.Core;

/// <summary>
/// 多人时「这一幕同时上两只 Boss」的决策（设计见 <c>docs/plans/multiplayer-lockon.md</c> §阶段 1 第 1 条）。
///
/// 做法：原版 <c>EncounterModel.GenerateMonstersWithSlots(IRunState)</c> 是**唯一**能拿到 runState
/// （也就是玩家数、当前幕）的地方，而它是 non-virtual 的（编译期确认过，不能 override），
/// 所以用 Harmony 前缀把它接一份下来，等 <c>GenerateMonsters()</c> 里再问「第二只该是谁」。
///
/// 单人（玩家数 &lt; 2）时一律返回 null ⇒ 遭遇仍然只造一只，**单人与改动前完全一致**。
/// </summary>
public static class MultiBossPlan
{
    private static IRunState? _runState;

    internal static void Capture(IRunState runState) => _runState = runState;

    /// <summary>本场战斗该不该上第二只（= 玩家数 ≥ 2）。</summary>
    public static bool WantsSecondBoss(out int playerCount)
    {
        // ★★ v0.2.800（用户 2026-11-19 的两份 `mod_log (38)/(39)` 22:26）：
        //   **口径必须与 `BossLineups.PlayerCount()` 完全一致**（阵容条数就是它算出来的 need）。
        //   以前这里优先读 `RunState.Players.Count`，而 `PlayerCount()` 还会再修正一次
        //   （房主端按「还在说话的人数」、客人端认房主广播的 `pc=`）⇒ 两边能差 1～2：
        //     · 阵容按 **1 人** 排（1 条）⇒ 只有主 Boss 一个候选；
        //     · 这里却按 **3 人** 判 ⇒ 还想要 2 只伙伴 ⇒ 旧版「补重复」兜底把**主 Boss 自己**补上
        //       ⇒ 场上两只亡灵契约师（22:26 的证据）。
        //   现在一律先问 `PlayerCount()`（它自己会处理房主/客人/读不到时的兜底），只有它彻底算不出来
        //   才退回 `RunState`。这样「阵容几条」与「伙伴几只」天然相同，`PartnersForWithKeys` 不再需要兜底。
        playerCount = 1;
        int byLineups = 0;
        try { byLineups = BossLineups.PlayerCount(); } catch { byLineups = 0; }
        if (byLineups > 0)
        {
            playerCount = byLineups;
            return playerCount >= 2;
        }

        try { playerCount = CurrentRunState()?.Players?.Count ?? 0; } catch { playerCount = 0; }
        if (playerCount <= 0) playerCount = 1;
        if (playerCount >= 2 && _fallbackLogged++ < 6)
        {
            Logger.Log($"[CharBoss] 多人判定：`BossLineups.PlayerCount()` 算不出来 ⇒ 退回 RunState 的 {playerCount} 人" +
                       "（注意：阵容条数走的是前者，两边可能又不一致）");
        }
        return playerCount >= 2;
    }

    private static int _fallbackLogged;

    /// <summary>当前局的 RunState（`RunManager.State` 是 private ⇒ 反射；拿不到就退回抓取到的那个）。</summary>
    private static IRunState? CurrentRunState()
    {
        try
        {
            var rm = MegaCrit.Sts2.Core.Runs.RunManager.Instance;
            if (rm != null)
            {
                if (!_statePropSearched)
                {
                    _statePropSearched = true;
                    _stateProp = typeof(MegaCrit.Sts2.Core.Runs.RunManager).GetProperty("State",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                }
                if (_stateProp?.GetValue(rm) is IRunState live) return live;
            }
        }
        catch { /* 忽略，退回抓取到的 */ }
        return _runState;
    }

    private static System.Reflection.PropertyInfo? _stateProp;
    private static bool _statePropSearched;

    /// <summary>供 <see cref="BossLineups"/> 用的 RunState 访问（拿不到返回 null）。</summary>
    internal static IRunState? RunStateOrNull() => CurrentRunState();

    /// <summary>
    /// 场上是否还有**别的、活着的**「我们的 Boss」。
    ///
    /// 用途（用户 2026-10-26 反馈的双人联机 bug）：击败一只 Boss 时
    /// <c>CharBossMonster.BeforeRemovedFromRoom</c> 以前无条件调 <c>BossHandUi.EndFight</c>，
    /// 而那会把**所有** Boss 的手牌栏/意图面板一起清掉 ⇒ 另一只还活着的 Boss 这一回合的意图就没了
    /// （要等她下个回合 <c>PrepareTurn</c> 才重建）。三人/四人局同一个根因，只是坏得更明显。
    /// </summary>
    public static bool AnyOtherBossAlive(Monsters.CharBossMonster? me)
    {
        try
        {
            var enemies = me?.CombatState?.Enemies;
            if (enemies == null) return false;
            foreach (var creature in enemies)
            {
                if (creature == null) continue;
                if (ReferenceEquals(creature.Monster, me)) continue;
                if (creature.Monster is not Monsters.CharBossMonster) continue;
                if (creature.IsAlive && !creature.IsDead) return true;
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 判断「还有别的 Boss 活着」失败（按没有处理）: " + e.Message);
        }
        return false;
    }

    /// <summary>
    /// 这一场除了「我」之外还要同时上场的那些 Boss（**Boss 标识**由 <see cref="BossLineups"/> 决定）。
    ///
    /// 用户 2026-10-26：只数 = 玩家数（1～3）⇒ 我 + 伙伴数 = 玩家数。
    ///
    /// ★★ v0.2.799（用户 2026-11-19，`mod_log (37)` 里「场上两只亡灵契约师」）：
    ///   **硬约束 —— 伙伴不得与主 Boss 或已有伙伴同角色**（同角色按 `BossKeys.CharacterOf(key)` 判），
    ///   **凑不满就少出**。以前这里允许「同一角色的另一个变体」（旧注释原话：「主 Boss 是能力机，
    ///   伙伴是炼药猎」），而且 v0.2.526 又加了「先要不同的、不够就把重复的那只也上」的兜底
    ///   ⇒ 阵容里只剩主 Boss 一个条目时（候补表被勾选裁到只剩一只 / 第二场 `usedInThisAct` 清空），
    ///   兜底会把**主 Boss 自己**当成自己的伙伴再上一只。用户给的证据：
    ///     · `mod_log (36)` 22:26 阵容=[NECROBINDER_FLESH,SILENT_SNAKEBITE,IRONCLAD_STRIKE]
    ///     · `mod_log (37)` 22:26 阵容=[NECROBINDER_FLESH]，伙伴=[NECROBINDER_FLESH] ⇒ 两只亡灵契约师
    ///   现在同角色一律不出（宁可这一场只有一只 Boss），并打一行「伙伴角色去重自检」。
    ///
    /// 单人或取不到阵容时返回空列表（遭遇仍然只造一只，行为与改动前一致）。
    /// </summary>
    public static List<MonsterModel> PartnersFor(string? myKey)
    {
        var result = new List<MonsterModel>();
        foreach (var (_, monster) in PartnersForWithKeys(myKey)) result.Add(monster);
        return result;
    }

    /// <summary>
    /// 同上，但同时把**每个伙伴是哪一套设计（BossKeys 的 Key）**一起返回 ——
    /// 用来在怪物实例上记下自己的变体身份（见 <c>CharBossMonster.SpawnVariantKey</c>），
    /// 死亡提示 / 跟踪锁定的名字 / 地图徽章都靠它，不能靠「再算一次」（v0.2.333 修
    /// 「炼药猎死亡提示显示成刀猎」：那时候变体判定的锁定可能已经失效，重算就会落到同档的主变体上）。
    /// </summary>
    public static List<(string Key, MonsterModel Monster)> PartnersForWithKeys(string? myKey)
    {
        var result = new List<(string, MonsterModel)>();
        try
        {
            // ★★ v0.2.802（用户 2026-11-19「这怎么就一个boss了」）：**伙伴数量改由阵容条数决定**，
            //   不再问 `WantsSecondBoss`（那个数取自「玩家数」，可能过时）。
            //   `mod_log (41)` 23:19:44 客人读到的玩家数是 1 ⇒ 以前这里直接 return ⇒ 3 人局只出 1 只 Boss，
            //   而房主那边阵容是 3 条。阵容条数（房主广播、客人照抄）才是真正的单一真相。
            int playerCount = 0;
            bool multi = WantsSecondBoss(out playerCount);      // 只用于日志

            int act = 0;
            try { act = CurrentRunState()?.CurrentActIndex ?? 0; } catch { act = 0; }

            // ★ 用户 2026-10-29（装了「第四幕心脏」模组）：**第 4 幕及以后绝对不碰** ——
            //   我们的设计只覆盖前三幕，第四幕的 Boss 是那个模组自己的。
            if (act >= 3)
            {
                Logger.Log($"[CharBoss] 第 {act + 1} 幕不是我们覆盖的幕（≥4）⇒ 不额外加伙伴 Boss");
                return result;
            }

            int fight = FindFightOf(act, myKey, out var lineup);
            if (lineup == null || lineup.Length == 0)
            {
                Logger.Log($"[CharBoss] 取伙伴 Boss：阵容里没有我（{myKey ?? "?"}）⇒ 不额外加 Boss");
                return result;
            }

            // 让「静默猎手用哪套设计」能按这一场的阵容判断（见 BossLineups.BrewerActive ②）
            BossLineups.NoteCurrentFight(act, fight);

            // 阵容几条 ⇒ 我之外还要几只（单人的阵容就只有 1 条 ⇒ 0 只，与以前完全一致）
            int wantPartners = Math.Max(0, Math.Min(lineup.Length, BossLineups.MaxPlayersPerFight) - 1);
            if (wantPartners == 0)
            {
                Logger.Log($"[CharBoss] 伙伴 Boss 判定：第 {act + 1} 层第 {fight + 1} 场阵容只有 " +
                           $"{lineup.Length} 条 [{string.Join(",", lineup)}] ⇒ **不加伙伴**" +
                           $"（本机玩家数={playerCount}、多人={multi}；第 3 层第 1 场/第 2 场各自独立）");
                return result;
            }

            var parts = new List<string>();

            // ★★ v0.2.799：**硬约束 —— 伙伴不得与主 Boss 或已有伙伴同角色**（用户 2026-11-19）。
            //   同角色按 `BossKeys.CharacterOf(key)` 判（这是「角色下标」，同一角色的不同变体
            //   例如 NECROBINDER_DOOM / NECROBINDER_FLESH 用的是**同一个** Character）；
            //   **凑不满就少出**，绝不为了让「只数 = 玩家数」而把同角色再上一只。
            //
            //   历史（v0.2.526，`mod_log (25).txt`：2 人局只出 1 只 Boss）那段「跳过与我同 Key 的条目
            //   ⇒ 一只伙伴都不加」的问题，当初是靠「不够就把重复的那只也补上」兜底解决的；
            //   但那个兜底正是 v0.2.799 要根治的 bug：阵容只剩主 Boss 一个条目时，它补出来的
            //   **就是主 Boss 自己**（`mod_log (37)`：阵容=[NECROBINDER_FLESH]、伙伴=[NECROBINDER_FLESH]）。
            //   ⇒ 兜底已删除；少一只 Boss 好过场上一模一样的两个。
            int mainChar = BossKeys.CharacterOf(myKey);
            var usedChars = new HashSet<int>();
            if (mainChar >= 0) usedChars.Add(mainChar);
            var usedKeys = new HashSet<string>(StringComparer.Ordinal);
            if (!string.IsNullOrEmpty(myKey)) usedKeys.Add(myKey!);
            var rejected = new List<string>();

            var order = new List<string>();
            foreach (var key in lineup)
            {
                if (order.Count >= wantPartners) break;
                if (string.IsNullOrEmpty(key)) continue;
                if (usedKeys.Contains(key))
                {
                    rejected.Add($"{key}（与主 Boss 是同一个 Key）");
                    continue;
                }
                int ch = BossKeys.CharacterOf(key);
                if (ch >= 0 && usedChars.Contains(ch))
                {
                    rejected.Add($"{key}（角色「{BossKeys.CharacterName(ch)}」已经有 Boss 上场）");
                    continue;
                }
                order.Add(key);
                usedKeys.Add(key);
                if (ch >= 0) usedChars.Add(ch);
            }

            if (order.Count < wantPartners)
            {
                Logger.Log($"[CharBoss] 伙伴名额 {wantPartners} 只、实际只凑到 {order.Count} 只 ⇒ **少出**" +
                           "（硬约束：伙伴不得与主 Boss 或已有伙伴同角色；宁可少一只，也不要场上一模一样的两个）" +
                           (rejected.Count > 0 ? $"；被挡下的候选=[{string.Join(" | ", rejected)}]" : ""));
            }

            foreach (var key in order)
            {
                var type = BossKeys.MonsterTypeOf(key);
                var mutable = type != null ? GetMonsterCanonical(type)?.ToMutable() : null;
                if (mutable == null)
                {
                    Logger.Log($"[CharBoss] 伙伴 Boss {key} 取不到怪物模型 ⇒ 跳过");
                    continue;
                }
                result.Add((key, mutable));
                parts.Add($"{key}={mutable.Id.Entry}");
            }

            if (parts.Count > 0)
            {
                Logger.Log($"[CharBoss] 多人({CurrentRunState()?.Players?.Count} 人)：这一幕同时上 " +
                           $"{lineup.Length} 只 Boss —— 我={myKey}，伙伴={string.Join(" | ", parts)}" +
                           $"（第 {act + 1} 层第 {fight + 1} 场阵容 [{string.Join(",", lineup)}]）");
            }

            // ★ 指纹行（用户 2026-10-30：多人不同步 —— 房主场上有 2 只 Boss、客人只有 1 只）。
            //   这一行**每台机器都会打**，两端必须完全一样；不一样就是「多 Boss 生成」分叉，
            //   而且直接能看出是阵容不同、还是玩家数不同、还是只挑了 0 只伙伴。
            Logger.Log($"[CharBoss] ※多 Boss 生成指纹※ 第 {act + 1} 层第 {fight + 1} 场 · " +
                       $"玩家={CurrentRunState()?.Players?.Count} · 阵容=[{string.Join(",", lineup)}] · " +
                       $"我={myKey} · 伙伴=[{string.Join(",", parts)}]（两端这一行必须一致）");

            // ★★ v0.2.799 自检（用户 2026-11-19 要的）：**场上绝不能出现同一角色的两只 Boss**。
            //   判据只看「主 Boss 的角色 + 每个伙伴的角色」有没有重复；判据全是 Key 派生的
            //   ⇒ 两端确定性一致（这一行在多人局也应该逐字相同）。
            var charsInPlay = new List<int>();
            if (mainChar >= 0) charsInPlay.Add(mainChar);
            foreach (var key in order)
            {
                int ch = BossKeys.CharacterOf(key);
                if (ch >= 0) charsInPlay.Add(ch);
            }
            int distinctChars = charsInPlay.Distinct().Count();
            bool dedupOk = distinctChars == charsInPlay.Count;
            Logger.Log($"[CharBoss] ※伙伴角色去重自检※ 我={myKey}（角色「{BossKeys.CharacterName(mainChar)}」）· " +
                       $"要 {wantPartners} 只伙伴 · 实得 {result.Count} 只（模型取到 {parts.Count} 只）· " +
                       $"场上角色=[{string.Join(",", charsInPlay.Select(c => BossKeys.CharacterName(c)))}]" +
                       $"（{distinctChars} 个不同 / 共 {charsInPlay.Count} 只）⇒ " +
                       (dedupOk ? "通过（无同角色）" : "⚠ **失败：场上有同角色的两只 Boss**") +
                       (mainChar < 0 ? " · ⚠ 认不出主 Boss 的角色 ⇒ 只能按 Key 去重" : "") +
                       (rejected.Count > 0 ? $" · 被挡=[{string.Join(" | ", rejected)}]" : ""));
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 取伙伴 Boss 失败: " + e.Message);
        }
        return result;
    }

    /// <summary>找出「我」所在的那一场（优先我是主 Boss 的那场），返回场次并给出阵容。</summary>
    private static int FindFightOf(int act, string? myKey, out string[]? lineup)
    {
        lineup = null;
        if (myKey == null) return 0;

        // ★ 先用**联机同步的**两个遭遇槽位判「我是第几场」（用户 2026-10-30 的不同步日志：
        //   房主这一场是「黑球机 + 灾厄骨」两只，客人那边只有黑球机一只 ⇒ 这就是不同步的源头）。
        //   房主把结论写进了 `act.BossEncounter` / `act.SecondBossEncounter`，客人读到的就是同一份；
        //   而「按种子反推阵容再找我在第几场」只要两端任何一处输入不同就会分叉。
        int synced = SyncedFightOf(act, myKey);
        if (synced >= 0)
        {
            var syncedLineup = BossLineups.For(act, synced);
            // ★ v0.2.526：**还要能对得上我**才算数。同步槽位可能指向另一场
            //   （例如槽位里写的是第一场、而我们这只其实是第二场的主 Boss），
            //   以前只要「阵容非空」就采用 ⇒ 拿到一场根本不含我的阵容 ⇒
            //   伙伴数量随之算错（房主 2 只 / 客人 1 只那种）。
            if (syncedLineup.Length > 0 && Array.IndexOf(syncedLineup, myKey) >= 0)
            {
                lineup = syncedLineup;
                return synced;
            }
            Logger.Log($"[CharBoss] 取伙伴 Boss：同步槽位指向第 {synced + 1} 场，但那场阵容 " +
                       $"[{string.Join(",", syncedLineup)}] 里没有我（{myKey}）⇒ 改按种子反推");
        }

        for (int fight = 0; fight < 2; fight++)
        {
            var candidate = BossLineups.For(act, fight);
            if (candidate.Length > 0 && candidate[0] == myKey) { lineup = candidate; return fight; }
        }
        for (int fight = 0; fight < 2; fight++)
        {
            var candidate = BossLineups.For(act, fight);
            if (Array.IndexOf(candidate, myKey) >= 0) { lineup = candidate; return fight; }
        }
        return 0;
    }

    /// <summary>
    /// 用**同步数据**判断「我」是本幕第几场：`act.BossEncounter` = 第一场、`act.SecondBossEncounter` = 第二场。
    /// 拿不到 / 我两个槽位都不占（例如「共用匹配池」抽到原本 Boss 时我们只是搭子）⇒ 返回 -1，退回按种子反推。
    /// </summary>
    private static int SyncedFightOf(int act, string? myKey)
    {
        if (myKey == null) return -1;
        try
        {
            var actModel = CurrentRunState()?.Act;
            if (actModel == null) return -1;
            try { if (actModel.Index != act) return -1; } catch { /* 取不到 Index 就当它一致 */ }
            if (BossKeys.KeyOfEncounter(actModel.BossEncounter) == myKey) return 0;
            var second = actModel.SecondBossEncounter;      // getter 有我们的兜底补丁，读到的是替换后的
            if (second != null && BossKeys.KeyOfEncounter(second) == myKey) return 1;
        }
        catch (Exception e) { Logger.Log("[CharBoss] 按同步数据判「我是第几场」失败（退回按种子反推）: " + e.Message); }
        return -1;
    }
    // 角色下标 → Boss 怪物类型（与 CharBossEncounters 的职业下标一致）
    private static readonly Type?[] MonsterTypes =
    {
        typeof(IroncladBoss), typeof(SilentBoss), typeof(DefectBoss), typeof(NecrobinderBoss), typeof(RegentBoss),
    };

    private static Type? MonsterTypeOf(int characterIndex)
        => characterIndex >= 0 && characterIndex < MonsterTypes.Length ? MonsterTypes[characterIndex] : null;

    private static int CharacterIndexOf(Type monsterType)
    {
        for (int i = 0; i < MonsterTypes.Length; i++)
            if (MonsterTypes[i] == monsterType) return i;
        return -1;
    }

    /// <summary>按运行时类型取 canonical（<c>ModelDb.GetById&lt;MonsterModel&gt;</c> 是泛型，只能反射调）。</summary>
    private static MonsterModel? GetMonsterCanonical(Type monsterType)
    {
        try
        {
            var id = ModelDb.GetId(monsterType);
            var method = typeof(ModelDb).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == "GetById" && m.IsGenericMethodDefinition && m.GetParameters().Length == 1);
            if (method == null) return null;
            var generic = method.MakeGenericMethod(typeof(MonsterModel));
            return generic.Invoke(null, new object[] { id }) as MonsterModel;
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 取第二只 Boss（反射）失败: " + e.Message);
            return null;
        }
    }
}

/// <summary>把 <c>GenerateMonstersWithSlots</c> 的 runState 接给 <see cref="MultiBossPlan"/>（它是 non-virtual，只能挂前缀）。</summary>
[HarmonyPatch(typeof(EncounterModel), nameof(EncounterModel.GenerateMonstersWithSlots))]
public static class EncounterRunStateCapturePatch
{
    [HarmonyPrefix]
    public static void Prefix(EncounterModel __instance, IRunState runState)
    {
        try { MultiBossPlan.Capture(runState); } catch { /* 忽略 */ }

        // ★ v0.2.417：**顺便把「本场遭遇」记下来**。
        //   卡组/遗物/剧本/特殊效果都在「创建怪物」时定，而那时 `state.Encounter` 还没赋值
        //   ⇒ 变体判定以为自己在打另一只（用户实测 `fight DEFECT_EVENT_BOSS_ENCOUNTER` 打成了能力机）。
        //   这里是 Harmony 前缀，恰好在怪物被创建**之前**执行 ⇒ 记下的就是这一场真正的遭遇。
        try
        {
            string? entry = null;
            try { entry = __instance?.Id.Entry; } catch { entry = null; }
            if (!string.IsNullOrEmpty(entry)) Core.BossLineups.NoteFightEncounter(entry);
        }
        catch { /* 纯记录，失败无所谓 */ }
    }
}
