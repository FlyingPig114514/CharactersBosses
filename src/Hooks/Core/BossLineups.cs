using System;
using System.Collections.Generic;

namespace CharacterBosses.Core;

/// <summary>
/// **每一层到底上哪些 Boss、按什么顺序**（用户 2026-10-23 的新规则）。
///
/// ## 用户口径
/// - **单人**：每层的 Boss **等概率**出现。
///   1 层候补 = 战 / 骨 / 储 / 猎（愤怒战、灾厄骨、碎屑储君、**奇巧猎** —— 用户 2026-10-26 新增奇巧猎）；
///   2 层候补 = 猎 / 机 / 骨 / 储（涂毒猎、**物理机**、灵魂骨、铸剑储君 —— 用户 2026-10-24 新增物理机）；
///   3 层候补 = 刀猎 / 能力机 / 根除骨 / 无色储君 / **易伤战**（用户 2026-10-24：改成五选一，
///     多人从 5 个里挑 2 个；站位按 `Act3Roster` 顺序，**易伤战永远最右**）。
/// - **双人**：
///   · 1 层：从「战骨储」3 个里随机挑 **2** 个，**站位按「战→骨→储」的固定次序**（缺哪个就把哪个去掉）；
///   · 2 层：从「猎机骨储」4 个里随机挑 2 个，站位按「猎→机→骨→储」；
///   · 3 层：从 5 个里随机挑 2 个（同上，易伤战最右）；进阶 10 时**两场都打**，第二场避开与第一场相同的搭配。
/// - 3～4 人：用户明确「未实现」，沿用双人规则（先能跑，别乱加）。
///
/// ## 为什么必须「确定性随机」
/// 联机两端各自算一遍，必须得到同一个结果（否则进 Boss 房就「多人数据不同步」）。
/// 所以随机数**只用「本局 RNG 种子 + 幕 + 用途 salt」**算（和 <see cref="BossLockOn"/> 里那套一致），
/// 结果按 (幕, 第几场) 缓存在内存里，同一局里反复问都得到同一套阵容。
/// </summary>
internal static class BossLineups
{
    // 角色下标：0 铁甲 / 1 静默 / 2 故障 / 3 亡灵 / 4 储君（与 CharBossEncounters 一致）
    private const int Ironclad = 0, Silent = 1, Defect = 2, Necrobinder = 3, Regent = 4;

    private static readonly Dictionary<(ulong Seed, int Act, int Fight), string[]> Cache = new();

    /// <summary>本局用的阵容随机种子（第一次算阵容时定下来；之后一旦能读到真实种子会重新对齐）。</summary>
    private static ulong _runSeed;

    /// <summary>
    /// **临时开关**（用户 2026-10-26）：「暂时把二层 Boss 固定为灵魂骨，我看看效果」——
    /// 二层第一场的**主 Boss** 固定成这个 Key（`NECROBINDER_SOUL` = 灵魂骨，二层亡灵契约师）。
    ///
    /// 只钉主位，多人的伙伴照旧从其余候补里随机 ⇒ 2/3 人局里灵魂骨一定在场，但不会顶替别人。
    /// **恢复随机只要把它改成 `null`**（不需要动别的地方）。
    /// </summary>
    private static readonly string? ForcedMainBossAct2 = null;   // 用户 2026-10-26：看完图标了，恢复随机

    /// <summary>
    /// 本局种子。**必须两端一致**，否则同一场战斗在两边是不同的 Boss
    /// （用户 2026-10-24 联机日志 `mod_log (3).txt`：`阵容随机种子 = 1874266711334065713（来源：本地随机）`
    /// 出现在一场 **2 人局**里 ⇒ 两个客户端各摇各的 ⇒ 进 Boss 房「多人数据不同步」）。
    ///
    /// 规则（按优先级）：
    ///   1. **能读到 `RunState.Rng.Seed` 就用它**（两端读同一个 RunState ⇒ 必然一致）；
    ///      而且每次调用都会重试 —— 早先的实现只在第一次读一次，那时 `RunManager.State` 还是 null，
    ///      于是一整局都用本地随机种子，之后再也不会纠正。
    ///   2. 读不到时：
    ///      · **确认是单人局**（能读到 `Players.Count == 1`）→ 本地随机（保持「每局阵容不同」）；
    ///      · **其它情况（多人，或连人数都读不到）→ 用固定种子 1**：
    ///        宁可「这一局不随机」，也绝不能两端算出不同阵容。
    /// </summary>
    private static ulong CurrentSeed()
    {
        var state = MultiBossPlan.RunStateOrNull();
        ulong read = ReadSeed(state);

        // ① 能读到本局种子：以它为准（换局/首次读到都走这里，顺手清缓存）
        if (read != 0)
        {
            if (read != _runSeed)
            {
                bool realign = _runSeed != 0;
                _runSeed = read;
                Cache.Clear();
                ResetPlayerCountMemory();      // 换局：人数也要重新读，别把上局的人数带过来
                try { Encounters.CharBossEncounters.ResetSharePoolLog(); } catch { /* 忽略 */ }
                Logger.Log($"[CharBoss] 阵容随机种子 = {_runSeed}（来源：RunState.Rng.Seed）" +
                           (realign ? "（重新对齐：之前用的是本地随机，联机必须两端一致）" : string.Empty));
                try
                {
                    Logger.Log($"[CharBoss] 共用匹配池 = {(BossConfig.SharePool ? "**开**" : "关")}" +
                               $"（种子 {_runSeed}；开=每幕按种子在「原版 Boss / 角色 Boss」里抽一边）");
                }
                catch { /* 忽略 */ }
            }
            return _runSeed;
        }

        // ② 读不到：已经定过就不再换（换只会让两端更可能不同）
        if (_runSeed != 0) return _runSeed;

        int players;
        try { players = state?.Players?.Count ?? -1; }
        catch { players = -1; }

        if (players == 1)
        {
            _runSeed = FreshSeed();
            Logger.Log($"[CharBoss] 阵容随机种子 = {_runSeed}（本地随机；确认是单人局）");
        }
        else
        {
            _runSeed = 1;
            Logger.Log($"[CharBoss] 阵容随机种子 = 1（**固定值**：读不到本局种子，人数={players}）" +
                       "—— 多人局两端必须一致，所以不用本地随机");
        }
        Cache.Clear();
        return _runSeed;
    }

    /// <summary>从 RunState 读种子；多个候选属性/字段都试一遍（版本差异时不至于全废）。</summary>
    private static ulong ReadSeed(object? state)
    {
        if (state == null) return 0;

        // ★ v0.2.285：**先用强类型路径**（`IRunState.Rng.Seed`，和 `Telemetry.SafeSeed` 同一条，
        //   实测能拿到真实种子）。以前只走反射 + `Convert.ToInt64`，遇到 `ulong` 超出 long 范围就直接抛异常
        //   ⇒ 退回常量种子 ⇒ 共用匹配池/阵容随机全部固定（用户 2026-10-31 反馈的现象）。
        try
        {
            if (state is MegaCrit.Sts2.Core.Runs.IRunState run)
            {
                ulong typed = unchecked((ulong)run.Rng.Seed);
                if (typed != 0) return typed;
            }
        }
        catch { /* 继续试反射 */ }

        try
        {
            var rng = state.GetType().GetProperty("Rng")?.GetValue(state);
            if (rng != null)
            {
                var seedObj = rng.GetType().GetProperty("Seed")?.GetValue(rng);
                if (seedObj != null) return ToSeed(seedObj);
            }
        }
        catch { /* 继续试 */ }
        foreach (var name in new[] { "Seed", "RunSeed", "RandomSeed" })
        {
            try
            {
                var v = state.GetType().GetProperty(name)?.GetValue(state);
                if (v != null) return ToSeed(v);
            }
            catch { /* 继续试 */ }
        }
        return 0;
    }

    /// <summary>把读到的种子对象安全转成 ulong（ulong/long/int/字符串都认，绝不靠 Convert 抛异常）。</summary>
    private static ulong ToSeed(object? v)
    {
        try
        {
            switch (v)
            {
                case ulong u: return u;
                case long l: return unchecked((ulong)l);
                case int i: return unchecked((ulong)i);
                case uint ui: return ui;
                case string s when ulong.TryParse(s, out var parsed): return parsed;
                default: return unchecked((ulong)Convert.ToDecimal(v));
            }
        }
        catch { return 0; }
    }

    /// <summary>
    /// 拿不到本局种子的**兜底**种子。
    /// ⚠ 以前是 `Random.Shared.NextInt64() ^ TickCount64` —— **每台机器都不一样**，
    /// 多人时两端算出的阵容可能不同 ⇒ 直接掉线（用户 2026-10-28 反馈「打冷光会不同步」，
    /// 客户端/房主的日志里都有 `State divergence detected`）。
    /// 现在改成**确定性常量**：宁可所有人都抽到同一套「默认阵容」，也绝不让两端分叉。
    /// </summary>
    private static ulong FreshSeed() => 0x5EED_0000_0000_0001UL;

    /// <summary>
    /// 这一幕**这一场**要上的 Boss（**Boss 标识**数组，<c>[0]</c> = 主 Boss）。
    /// <paramref name="fight"/> = 0 第一场、1 第二场（只有进阶 10 的第三幕有第二场）。
    ///
    /// 条数 = <see cref="PlayersPerFight"/>（1～3 人 ⇒ 1～3 只），用户 2026-10-26：
    /// 「为了支持更多人联机…进阶 10 三层会有两个 boss，需要的 boss 数量变成了人数 × 2」。
    /// 第三幕现在有 6 个 Boss（刀猎 / 能力机 / 根除骨 / 无色储君 / 易伤战 / **炼药猎**）
    /// ⇒ 三人联机 3 只 × 2 场 = 6，刚好用满。
    /// </summary>
    internal static string[] For(int act, int fight)
    {
        // ★ v0.2.535：房主在这里顺手把 pickset 广播一次（内容没变就不发；单人/客人什么都不做）。
        //   放这里是因为「算阵容」这一刻必然已经有 RunManager + NetService，不需要额外的时机钩子。
        try { Core.BossPicksSync.BroadcastIfHost(); } catch { /* 忽略 */ }

        var key = (CurrentSeed(), act, fight);
        lock (Cache)
        {
            if (Cache.TryGetValue(key, out var cached)) return cached;

            // ★★ v0.2.515（用户 2026-11-09「进战斗的时候游戏怎么崩了？」—— 日志尾部几千行
            //   `第 3 层第 2 场主 Boss = …` 刷屏）：
            //   **重入会无限递归**。链路：`For(act,1)` → `Build` → ⑥ `SyncedMainCharacter` 读
            //   `act.SecondBossEncounter` → 那个 getter 被我们自己的 `SecondBossEncounterGetPatch`
            //   改成「按当前版本重算」→ `ForVanillaBoss` → 又回到 `BossLineups.For(act,1)`；
            //   此时缓存还没写（Build 没返回）⇒ 再一次 Build ⇒ 递归到栈溢出、日志被刷爆、游戏崩。
            //   这里加**重入保护**：同一 (幕, 场) 已在计算中就直接返回固定表（只影响这一次嵌套调用）。
            if (!Computing.Add((act, fight)))
            {
                if (_reentryLogged.Add((act, fight)))
                {
                    Logger.Log($"[CharBoss] 阵容重入保护：第 {act + 1} 层第 {fight + 1} 场正在计算中" +
                               "（触发源：`SecondBossEncounter` 读取兜底回调）⇒ 本次返回固定表，避免无限递归");
                }
                return Fallback(act);
            }

            try
            {
                string[] result;
                try { result = Build(act, fight); }
                catch (Exception e)
                {
                    Logger.Log("[CharBoss] 阵容随机失败，退回固定表: " + e.Message);
                    result = Fallback(act);
                }
                if (Cache.Count > 64) Cache.Clear();      // 长期在同一进程里反复开局别把字典撑爆
                Cache[key] = result;
                Logger.Log($"[CharBoss] 第 {act + 1} 层第 {fight + 1} 场阵容：" +
                           $"（{PlayerCount()} 人局，进阶 {Ascension()}，种子 {CurrentSeed()}，指纹 {FingerprintOf(result)}）" +
                           $"[{string.Join(" | ", System.Array.ConvertAll(result, k => BossKeys.LabelOf(k)))}]");
                return result;
            }
            finally { Computing.Remove((act, fight)); }
        }
    }

    /// <summary>
    /// 清空阵容缓存（勾选变了 / 收到房主的勾选之后调）。
    /// ★ v0.2.535：客人收到房主 pickset 时用它 —— 之前算过的阵容是按「整张候补表」算的，必须重算。
    /// </summary>
    internal static void InvalidateCache()
    {
        try { lock (Cache) Cache.Clear(); } catch { /* 忽略 */ }
    }

    /// <summary>正在计算中的 (幕, 场) —— 重入保护用（见 <see cref="For"/>）。</summary>
    private static readonly HashSet<(int Act, int Fight)> Computing = new();

    /// <summary>重入保护日志每个 (幕, 场) 只打一次。</summary>
    private static readonly HashSet<(int Act, int Fight)> _reentryLogged = new();

    /// <summary>
    /// 阵容的**短指纹**（只用于联机对表）：两台机器的这一行必须完全一样。
    /// 用 FNV-1a 把每个 Key 折进去 —— 同阵容必然同值，不同阵容几乎不可能撞。
    /// </summary>
    internal static string FingerprintOf(string[]? lineup)
    {
        try
        {
            if (lineup == null || lineup.Length == 0) return "empty";
            ulong h = 14695981039346656037UL;
            foreach (var key in lineup)
            {
                foreach (char c in key ?? "?") h = unchecked((h ^ c) * 1099511628211UL);
                h = unchecked((h ^ '|') * 1099511628211UL);
            }
            return (h & 0xFFFFFFFFUL).ToString("X8");
        }
        catch { return "?"; }
    }

    /// <summary>
    /// 这一场同时上几只 Boss = **玩家数（1～4）**。
    ///
    /// ⚠ 这里以前夹到 3（"4 人暂不支持"），于是四人联机只出 **3 只 Boss**（用户 2026-10-29 实测：
    /// "只看到 3 个 boss"）。改成 4 之后：一层/二层候补各 5 只、三层 8 只，凑 4 只不成问题；
    /// 至于「同角色不复用」「两场不重复」这些约束，下面的 ⑥/② 遍会自动让步（极端情况才降级）。
    /// </summary>
    internal static int PlayersPerFight() => Math.Clamp(PlayerCount(), 1, 4);

    /// <summary>
    /// 生成某一幕某一场的阵容（Boss 标识数组，`[0]` = 站位/行动里的第 1 只，也就是**主 Boss**）。
    ///
    /// ★ 2026-11-05（用户口径，两轮澄清后的最终版）：
    ///   1. Boss **只按层数分**，同一个角色的几个设计**完全平权**，只在**角色内部**排号
    ///      （猎人 1 号刀猎 / 2 号炼药猎；故障 1~3 号能力机 / 事件机 / 状态机）；
    ///   2. **同一场战斗里，每个角色最多出现一只** —— 不会同时出刀猎和炼药猎
    ///      （`_ = 0` 那句用户原话：「多人时同一场战斗每个角色还是最多只能出现一个」）；
    ///   3. **主 Boss 的角色整局不重复**（跨幕 + 本幕前面各场都不重复）；
    ///   4. 只数 = 玩家数；进阶 10 同一幕的两场之间不复用同一个 Key。
    ///
    /// 做法：先把候补**按角色分桶**（每桶 = 该角色在本幕的若干编号），
    /// 角色次序与桶内编号都用「本局种子 + 幕 + 场」派生的确定性次序（<see cref="RankOf"/>）
    /// ⇒ 同种子结果固定、联机两端一致。
    /// </summary>
    private static string[] Build(int act, int fight)
    {
        int need = PlayersPerFight();

        // ★ v0.2.453（A2 改成「每张地图的 Boss 勾选」）：
        //   · **主 Boss** 从「房主勾选过的候补」里抽；
        //   · **搭子**（第 2、3 只）**永远从整张候补表**抽 —— 搭子一旦掺进各机自己的配置文件，
        //     两端就会算出不同阵容 ⇒ 又是不不同步。单人局只有 1 只 Boss，勾选照样 100% 生效。
        // ★ v0.2.503（玩家实测的不同步，两端日志一对比就实锤）：
        //   房主 `※本机生成※ … → 怪物 2 只 [IRONCLAD_BOSS,REGENT_BOSS]`，
        //   客人 `→ 怪物 3 只 [REGENT_BOSS,IRONCLAD_BOSS,NECROBINDER_BOSS]`。
        //   原因：**多人时主 Boss 的候选表也读「本机勾选」**（房主挑过勾 ⇒ 某个角色一只都不剩），
        //   而客人按规则不读本机勾选、永远拿整张表 ⇒ 两端阵容长度都不一样 ⇒ 轮次 1 立刻分叉。
        //   修法：**多人局（≥2 名玩家）一律用整张花名册**（房主的勾选只影响单人局）。
        //
        // ★★ v0.2.535（用户 2026-11-09：「多人时所有 boss 都只用房主勾选的」）：
        //   上面那条「多人一律用整张花名册」让勾选在多人里**完全失效**（用户：「那这勾不勾没啥区别」）。
        //   现在房主通过 BaseLib 的自定义网络消息把 pickset 广播给所有客人（见 `Core/BossPicksSync.cs`），
        //   两端拿到**同一套**候选 ⇒ 主 Boss 与搭子**都**按房主勾选来抽，仍然是确定性纯函数。
        //   客人还没收到消息时 `AvailableFor` 返回整张表（旧行为，不会算错也不会卡住），
        //   收到后会清缓存重算。
        bool multiplayer = false;
        try { multiplayer = Core.MultiBossPlan.WantsSecondBoss(out int mpPlayers) && mpPlayers >= 2; }
        catch { multiplayer = false; }

        // 这一场对应的**地图 id**（按幕取，避免「预告第 2、3 幕时却读了当前幕的地图」）
        string? mapEntry = null;
        try { mapEntry = Core.BossSelection.MapEntryOfAct(act); } catch { mapEntry = null; }

        var mainRoster = Core.BossSelection.AvailableFor(act, mapEntry);
        var partnerRoster = mainRoster;      // v0.2.535：搭子也用同一套（房主勾选 / 单人勾选 / 未收到时的整张表）

        // ② 本幕前面那几场已经上过的 Key 全部排除（进阶 10 的两场：任何 Boss 都不重复）
        var usedInThisAct = new HashSet<string>(StringComparer.Ordinal);
        for (int earlier = 0; earlier < fight; earlier++)
        {
            foreach (var key in For(act, earlier)) usedInThisAct.Add(key);
        }

        // ③ 主 Boss 的角色整局不重复：前面各幕的主 Boss + 本幕前面各场的主 Boss
        var mainTaken = MainCharactersBefore(act);
        for (int earlier = 0; earlier < fight; earlier++)
        {
            try
            {
                var prev = For(act, earlier);
                if (prev.Length > 0)
                {
                    int ch = BossKeys.CharacterOf(prev[0]);
                    if (ch >= 0) mainTaken.Add(ch);
                }
            }
            catch { /* 算不出来就少避一个，不影响确定性 */ }
        }

        // 按角色分桶：主 Boss 用「勾选过的候补」，搭子用整张候补表（两套桶，规则完全一样）
        var (characters, buckets) = Bucketize(act, fight, mainRoster, usedInThisAct);
        var (partnerChars, partnerBuckets) = Bucketize(act, fight, partnerRoster, usedInThisAct);

        var picked = new List<string>(need);

        // ④（**临时**开关，默认 null）二层第 1 场把某个 Key 钉到主位
        if (act == 1 && fight == 0 && ForcedMainBossAct2 != null && !usedInThisAct.Contains(ForcedMainBossAct2))
        {
            int forcedCh = BossKeys.CharacterOf(ForcedMainBossAct2);
            if (forcedCh >= 0 && buckets.TryGetValue(forcedCh, out var fb) && fb.Remove(ForcedMainBossAct2))
            {
                picked.Add(ForcedMainBossAct2);
                characters.Remove(forcedCh);
                Logger.Log($"[CharBoss] 二层 Boss 已**临时固定**为 {BossKeys.LabelOf(ForcedMainBossAct2)}" +
                           "（改 BossLineups.ForcedMainBossAct2 = null 即可恢复随机）");
            }
        }

        // ⑤ 主 Boss：角色整局没当过主 Boss 的优先
        if (picked.Count == 0)
        {
            int mainIdx = -1;
            for (int i = 0; i < characters.Count; i++)
            {
                if (mainTaken.Contains(characters[i])) continue;
                mainIdx = i;
                break;
            }
            if (mainIdx < 0 && characters.Count > 0) mainIdx = 0;   // 避不开就让步
            if (mainIdx >= 0)
            {
                int ch = characters[mainIdx];
                picked.Add(buckets[ch][0]);
                characters.RemoveAt(mainIdx);
                if (mainTaken.Count > 0)
                {
                    Logger.Log($"[CharBoss] 第 {act + 1} 层第 {fight + 1} 场主 Boss = {BossKeys.LabelOf(picked[0])}" +
                               $"（避开了本局已当过主 Boss 的角色下标：{string.Join('/', mainTaken)}）");
                }
            }
        }

        // ⑥ 搭子：**每个角色最多一只** ⇒ 从**整张候补表**的桶里取 1 号键（桶内已按种子排好）。
        //
        // ★★ v0.2.526（玩家 2026-11-09 上报的多人不同步，`mod_log (25).txt`）：
        //   这里**不再**去读「同步遭遇」里的主角色（原来的 `SyncedMainCharacter`）——
        //   那个值来自 `act.BossEncounter` / `act.SecondBossEncounter`，而这两个 getter
        //   我们自己的补丁会「按当前版本重算」，且**客人端不写同步状态、房主端会写**
        //   ⇒ 同一时刻两端读到的可能不同 ⇒ 主角色判错 ⇒ 搭子没有跳过主角色 ⇒
        //   阵容里出现**同一只 Boss 两次**。实测日志（同一名玩家、v0.2.513）：
        //     第 2 层第 1 场阵容 … [故障 2 号（DEFECT_RAINBOW） | 故障 2 号（DEFECT_RAINBOW）]
        //     第 3 层第 1 场阵容 … [猎人 1 号（SILENT_SHIV）  | 猎人 1 号（SILENT_SHIV）]
        //   而 `MultiBossPlan.PartnersForWithKeys` 会跳过「与我同 Key」的条目
        //   ⇒ **2 人局只出 1 只 Boss**，一人有锁定图标、另一人 0 个；
        //   更糟的是两端一旦在这一步分叉，就是「房主 2 只 / 客人 1 只」那种轮次 1 校验和不同步。
        //   ⇒ 现在阵容**只由（种子, 幕, 场）决定**：主角色直接用本机刚抽中的那只
        //     （它本身就是种子派生的、两端必然一致），同步值只用来打诊断日志。
        int mainChar = picked.Count > 0 ? BossKeys.CharacterOf(picked[0]) : -1;

        try
        {
            int syncedCh = SyncedMainCharacter(act, fight, -1);
            if (syncedCh >= 0 && mainChar >= 0 && syncedCh != mainChar)
            {
                Logger.Log($"[CharBoss] ⚠ 阵容自检：第 {act + 1} 层第 {fight + 1} 场「同步遭遇的主角色」={syncedCh}、" +
                           $"「本机抽到的主角色」={mainChar} ⇒ **以本机为准**" +
                           "（同步值只用于诊断，不再参与排阵容；以前它会让我们把同一只 Boss 排两次）");
            }
        }
        catch { /* 纯诊断 */ }

        var takenKeys = new HashSet<string>(picked, StringComparer.Ordinal);
        foreach (var ch in partnerChars)
        {
            if (picked.Count >= need) break;
            if (ch == mainChar) continue;
            var bucket = partnerBuckets[ch];
            if (bucket.Count == 0) continue;
            // 同一个 Key 绝不重复 —— 重复会让「只数 = 玩家数」直接落空（见上面的注释）
            string? choice = null;
            foreach (var candidate in bucket)
            {
                if (takenKeys.Contains(candidate)) continue;
                choice = candidate;
                break;
            }
            if (choice == null) continue;
            takenKeys.Add(choice);
            picked.Add(choice);
        }

        if (picked.Count == 0) return Fallback(act);
        return picked.ToArray();
    }

    /// <summary>
    /// 把一张候补表按**角色**分桶（v0.2.453 抽出来的：主 Boss 与搭子各用一张表）。
    /// 返回角色列表（登场次序由种子派生）+ 每个角色自己的 Key 桶（桶内也按种子排好）；
    /// 空桶（本幕这一场已经没得选）直接踢掉。
    /// </summary>
    private static (List<int> Characters, Dictionary<int, List<string>> Buckets)
        Bucketize(int act, int fight, string[] roster, HashSet<string> excluded)
    {
        var characters = new List<int>();
        var buckets = new Dictionary<int, List<string>>();
        foreach (var key in roster)
        {
            int ch = BossKeys.CharacterOf(key);
            if (ch < 0) continue;
            if (!buckets.TryGetValue(ch, out var bucket))
            {
                bucket = new List<string>();
                buckets[ch] = bucket;
                characters.Add(ch);
            }
            if (!excluded.Contains(key)) bucket.Add(key);
        }

        characters.RemoveAll(ch => buckets[ch].Count == 0);
        foreach (var ch in characters)
        {
            var bucket = buckets[ch];
            bucket.Sort((a, b) => RankOf(act, fight, a).CompareTo(RankOf(act, fight, b)));
        }
        characters.Sort((a, b) => RankOf(act, fight, "#char" + a).CompareTo(RankOf(act, fight, "#char" + b)));
        return (characters, buckets);
    }

    /// <summary>
    /// 本场**主 Boss 的角色**，取**同步数据**里的那个（`act.BossEncounter` / `act.SecondBossEncounter`），
    /// 拿不到才退回本机自己抽出来的那个。用途见 `Build` ⑥：搭子要跳过主 Boss 的角色，两端必须跳过同一个。
    /// </summary>
    private static int SyncedMainCharacter(int act, int fight, int fallback)
    {
        try
        {
            var actModel = MultiBossPlan.RunStateOrNull()?.Act;
            if (actModel == null) return fallback;
            // ★★ v0.2.515：这里**必须读存档里的原值**，不能走 `actModel.SecondBossEncounter` ——
            //   那个 getter 被 `SecondBossEncounterGetPatch` 改成了「按当前版本重算」，
            //   而重算会回来调 `For(act, 1)`，我们正处在 `Build(act,1)` 里面 ⇒ 无限递归（崩溃）。
            //   原值在 `act._rooms.Boss / .SecondBoss`（反射读，见 `RawBossEncounters`）。
            var (rawBoss, rawSecond) = RawBossEncounters(actModel);
            var encounter = fight <= 0
                ? (rawBoss ?? actModel.BossEncounter)
                : (rawSecond ?? actModel.SecondBossEncounter);
            int ch = BossKeys.CharacterOf(BossKeys.KeyOfEncounter(encounter));
            return ch >= 0 ? ch : fallback;
        }
        catch { return fallback; }
    }

    private static System.Reflection.FieldInfo? _roomsField2;
    private static System.Reflection.PropertyInfo? _roomBossProp;
    private static System.Reflection.PropertyInfo? _roomSecondProp;
    private static bool _roomsSearched2;

    /// <summary>反射读 `act._rooms.Boss / .SecondBoss`（**绕过**我们自己的 getter 补丁）。</summary>
    private static (MegaCrit.Sts2.Core.Models.EncounterModel? Boss,
                    MegaCrit.Sts2.Core.Models.EncounterModel? Second) RawBossEncounters(
                    MegaCrit.Sts2.Core.Models.ActModel act)
    {
        try
        {
            if (!_roomsSearched2)
            {
                _roomsSearched2 = true;
                const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
                _roomsField2 = typeof(MegaCrit.Sts2.Core.Models.ActModel).GetField("_rooms", flags);
                var roomSet = typeof(MegaCrit.Sts2.Core.Rooms.RoomSet);
                _roomBossProp = roomSet.GetProperty("Boss", flags);
                _roomSecondProp = roomSet.GetProperty("SecondBoss", flags);
                if (_roomsField2 == null || _roomBossProp == null || _roomSecondProp == null)
                    Logger.Log("[CharBoss] 读不到 RoomSet.Boss/SecondBoss（阵容主角色改用补丁后的 getter，已加重入保护）");
            }
            if (_roomsField2 == null || _roomBossProp == null || _roomSecondProp == null) return (null, null);
            var rooms = _roomsField2.GetValue(act);
            if (rooms == null) return (null, null);
            return (_roomBossProp.GetValue(rooms) as MegaCrit.Sts2.Core.Models.EncounterModel,
                    _roomSecondProp.GetValue(rooms) as MegaCrit.Sts2.Core.Models.EncounterModel);
        }
        catch { return (null, null); }
    }
    /// <summary>Key 的确定性排序权重（同种子下固定）。</summary>
    private static ulong RankOf(int act, int fight, string key)
    {
        ulong seed = CurrentSeed();
        ulong h = unchecked(seed * 6364136223846793005UL
                            + (ulong)(act + 7) * 1442695040888963407UL
                            + (ulong)(fight + 3) * 2862933555777941757UL
                            + (ulong)(key.Length + 1) * 0x9E3779B97F4A7C15UL);
        foreach (char c in key)
        {
            h = unchecked((h ^ c) * 1099511628211UL);
        }
        h ^= h >> 33;
        h *= 0xff51afd7ed558ccdUL;
        h ^= h >> 33;
        return h;
    }

    /// <summary>拿不到 RunState 时的保底（就是改动前那套固定表，别让整局起不来）。</summary>
    private static string[] Fallback(int act)
        => act >= 2 ? new[] { "NECROBINDER_ERADICATE", "REGENT_COLORLESS" } : new[] { BossSelection.AvailableFor(act)[0] };

    /// <summary>
    /// 在「第 act 幕」之前各幕的**主 Boss 角色**（用户 2026-11-05 重申的规则：
    /// 「主 boss 仍然整局不重复」⇒ 每一幕的主 Boss 角色都要避开这些）。
    /// </summary>
    private static HashSet<int> MainCharactersBefore(int act)
    {
        var used = new HashSet<int>();
        for (int earlier = 0; earlier < act; earlier++)
        {
            try
            {
                var lineup = For(earlier, 0);
                if (lineup.Length > 0)
                {
                    int ch = BossKeys.CharacterOf(lineup[0]);
                    if (ch >= 0) used.Add(ch);
                }
            }
            catch { /* 单幕算不出来就少避一个，不影响确定性 */ }
        }
        return used;
    }

    /// <summary>
    /// 给「第二场主 Boss 和第一场同角色」这种情况换一只**别的角色**的 Boss（**确定性**：按 RankOf 排）。
    ///
    /// 为什么还要它（用户 2026-11-05 重申「主 Boss 整局不重复」）：单人进阶 10 的三层，
    /// 地图预告是「静默猎手 + 亡灵契约师」，但中途更新过一版 mod —— 第一场的 Boss 早已写进存档，
    /// 而 `SecondBossEncounter` 的读取兜底（`SecondBossEncounterGetPatch`）**每次读取都用当前版本重算**，
    /// 重算出来的第二场可能又落回同一个角色 ⇒ 两场主 Boss 同角色。
    /// 所以这里以**存档里真实的第一场 Boss** 为准，同角色就换一只（排除本场已经排好的那几只）。
    ///
    /// ⚠ 只换**主位**，且换到的一定是**别的角色**（同角色在场最多一只这条不能破）。
    /// </summary>
    /// <param name="avoidCharacter">要避开的角色下标（第一场主 Boss 的角色）。</param>
    /// <param name="excluded">这一场已经排上的 Key（主 Boss 自己 + 搭子），不能重复选。</param>
    internal static string? AlternativeMainKey(int act, int fight, int avoidCharacter, IEnumerable<string>? excluded)
    {
        try
        {
            var skip = new HashSet<string>(StringComparer.Ordinal);
            // 本场已经排上的那几只 ⇒ 它们的**角色**也不能再被选（同场每角色最多一只）
            var skipChars = new HashSet<int>();
            if (excluded != null)
            {
                foreach (var k in excluded)
                {
                    skip.Add(k);
                    int c = BossKeys.CharacterOf(k);
                    if (c >= 0) skipChars.Add(c);
                }
            }

            var pool = new List<string>();
            foreach (var key in BossSelection.AvailableFor(act))    // v0.2.453：换上主 Boss 也要听 A2 的勾选
            {
                if (skip.Contains(key)) continue;
                int ch = BossKeys.CharacterOf(key);
                if (ch < 0 || ch == avoidCharacter) continue;
                if (skipChars.Contains(ch)) continue;
                pool.Add(key);
            }
            if (pool.Count == 0) return null;

            pool.Sort((a, b) => RankOf(act, fight, a).CompareTo(RankOf(act, fight, b)));
            return pool[0];
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 换主 Boss 失败: " + e.Message);
            return null;
        }
    }
    /// <summary>本局玩家数（拿不到就按单人算）。</summary>
    /// <summary>
    /// 三层**静默猎手的档位设计**是不是「炼药猎」（用户 2026-10-25：刀猎 / 炼药猎 **50/50**）。
    ///
    /// 只用「本局种子 + salt」算 ⇒ 同一个种子结果固定、联机两端一致（和阵容那套一样）。
    /// 刀猎的档位是 `(1, 2)`：`Roll(2, 41, 2) == 1` 就是炼药猎。
    /// ⚠ 这一条只用来**决定阵容里放哪个遭遇**（`CharBossEncounters.TierEncounterFor`）；
    /// 「这一场用哪套设计」请看 <see cref="BrewerActive"/>（以房间遭遇为准，控制台点名时才不会被种子覆盖）。
    /// </summary>
    internal static bool SilentBrewerAt3()
    {
        try
        {
            return Roll(2, 41, 2) == 1;
        }
        catch { return false; }
    }

    /// <summary>
    /// **这一场战斗实际用哪套三层静默猎手设计**（炼药猎 or 刀猎）。
    ///
    /// 用户 2026-10-25 实测问题：控制台 `fight SILENT_BREWER_BOSS_ENCOUNTER` 打出来的是**刀猎**
    /// —— 因为设计只看了种子（种子判给刀猎），和房间遭遇无关。
    /// 现在改成：**战斗内以房间遭遇为准**（遭遇是炼药猎 ⇒ 用炼药猎那套卡组/剧本/药水），
    /// 只有在拿不到遭遇时（战斗外、开局自检 dump）才退回按种子算。结果由 <see cref="Latched"/> 本场锁定。
    /// </summary>
    internal static bool BrewerActive() => Latched("SILENT_BREWER", ComputeBrewer);

    private static (bool Value, bool Decisive) ComputeBrewer()
    {
        // ① 当前遭遇直接说明了（主 Boss 是自己的遭遇时最准）
        try
        {
            string? entry = CurrentEncounterEntry();
            if (!string.IsNullOrEmpty(entry))
            {
                if (entry == "SILENT_BREWER_BOSS_ENCOUNTER") return (true, true);
                if (entry == "SILENT_SHIV_BOSS_ENCOUNTER") return (false, true);
                // ★ v0.2.520（用户 2026-11-09：ight SILENT_GRAND_FINALE_BOSS_ENCOUNTER 打成了炼药猎）：
                //   炼药猎是 VariantsByAct[2]，**先于** ExtraVariants[2]（华丽猎）被问到；
                //   以前它遇到新遭遇时 ①② 都不命中，就落到 ③ 按种子随机 ⇒ 一半概率抢走这一场。
                //   现在把新遭遇显式判否，控制台点名就一定是华丽猎。
                if (entry == "SILENT_GRAND_FINALE_BOSS_ENCOUNTER") return (false, true);
            }
        }
        catch { /* 继续往下判断 */ }

        // ② 从**存档里的主 Boss 遭遇**反查这一场阵容（同步数据）
        try
        {
            var lineup = CurrentFightLineup();
            if (lineup != null)
            {
                foreach (var k in lineup)
                {
                    if (k == "SILENT_BREWER") return (true, true);
                    if (k == "SILENT_SHIV") return (false, true);
                }
            }
        }
        catch { /* 继续兜底 */ }

        // ③ 种子兜底（不可靠、不锁定，见 Latched 注释）
        return (SilentBrewerAt3(), false);
    }

    /// <summary>
    /// 三层静默猎手的**第三套设计「华丽猎」**（Key `SILENT_GRAND_FINALE`，用户 2026-11-09）。
    /// 判据同 `BrewerActive`：以**房间遭遇**为准（控制台 `fight SILENT_GRAND_FINALE_BOSS_ENCOUNTER`
    /// 点名也才会真的是华丽猎），拿不到遭遇才按种子兜底。
    /// 注意判定顺序：`VariantsByAct[2]`（炼药猎）先问，问不到才轮到 `ExtraVariants[2]` 这一套。
    /// </summary>
    internal static bool GrandFinaleActive() => Latched("SILENT_GRAND_FINALE", ComputeGrandFinale);

    private static (bool Value, bool Decisive) ComputeGrandFinale()
    {
        try
        {
            string? entry = CurrentEncounterEntry();
            if (!string.IsNullOrEmpty(entry))
            {
                if (entry == "SILENT_GRAND_FINALE_BOSS_ENCOUNTER") return (true, true);
                if (entry == "SILENT_SHIV_BOSS_ENCOUNTER") return (false, true);
                if (entry == "SILENT_BREWER_BOSS_ENCOUNTER") return (false, true);
            }
        }
        catch { /* 继续往下判断 */ }

        try
        {
            var lineup = CurrentFightLineup();
            if (lineup != null)
            {
                foreach (var k in lineup)
                {
                    if (k == "SILENT_GRAND_FINALE") return (true, true);
                    if (k == "SILENT_SHIV") return (false, true);
                    if (k == "SILENT_BREWER") return (false, true);
                }
            }
        }
        catch { /* 继续兜底 */ }

        // 种子兜底：与炼药猎**错开**（那边用 salt 41，这里用 42，且只在炼药猎判否时才会被问到）
        try { return (Roll(2, 42, 2) == 1, false); } catch { return (false, false); }
    }

    /// <summary>
    /// 二层亡灵契约师的**第二套设计「虚无骨」**（Key `NECROBINDER_ETHEREAL`，用户 2026-11-09）。
    /// 判据与炼药猎/华丽猎同一套：**以房间遭遇为准**（控制台 `fight NECROBINDER_ETHEREAL_BOSS_ENCOUNTER`
    /// 点名也才会真的是虚无骨），拿不到遭遇才按种子兜底（与灵魂骨 50/50）。
    ///
    /// ⚠ 用户 2026-11-09 特别提醒「别和同层的灵魂骨搞串」⇒ 这里把灵魂骨/养手骨/根除骨三个遭遇
    ///   **显式判否**，免得按种子兜底时把别的场次抢走。
    /// </summary>
    internal static bool EtherealActive() => Latched("NECROBINDER_ETHEREAL", ComputeEthereal);

    private static (bool Value, bool Decisive) ComputeEthereal()
    {
        try
        {
            string? entry = CurrentEncounterEntry();
            if (!string.IsNullOrEmpty(entry))
            {
                if (entry == "NECROBINDER_ETHEREAL_BOSS_ENCOUNTER") return (true, true);
                if (entry == "NECROBINDER_SOUL_BOSS_ENCOUNTER") return (false, true);
                if (entry == "NECROBINDER_FLESH_BOSS_ENCOUNTER") return (false, true);   // 一层血肉骨（用户 2026-11-10）
                if (entry == "NECROBINDER_OSTY_BOSS_ENCOUNTER") return (false, true);
                if (entry == "NECROBINDER_ERADICATE_BOSS_ENCOUNTER") return (false, true);
                if (entry == "NECROBINDER_DOOM_BOSS_ENCOUNTER") return (false, true);
            }
        }
        catch { /* 继续往下判断 */ }

        try
        {
            var lineup = CurrentFightLineup();
            if (lineup != null)
            {
                foreach (var k in lineup)
                {
                    if (k == "NECROBINDER_ETHEREAL") return (true, true);
                    if (k == "NECROBINDER_SOUL") return (false, true);
                }
            }
        }
        catch { /* 继续兜底 */ }

        // 种子兜底（仅在拿不到遭遇/阵容时才走到这里）：与灵魂骨 50/50
        try { return (Roll(1, 51, 2) == 1, false); } catch { return (false, false); }
    }

    /// <summary>
    /// 第一幕**亡灵契约师**这一档用哪套设计：「灾厄骨」（主，<c>NECROBINDER_DOOM</c>）/
    /// 「**血肉骨**」（用户 2026-11-10，<c>NECROBINDER_FLESH</c>）。
    /// 与虚无骨同一套机制：以**房间遭遇**为准，拿不到遭遇/阵容时才按种子 50/50 兜底。
    /// ⚠ 同层与二层/三层的亡灵契约师遭遇必须**显式判否**，别把别的场次抢走。
    /// </summary>
    internal static bool FleshActive() => Latched("NECROBINDER_FLESH", ComputeFlesh);

    private static (bool Value, bool Decisive) ComputeFlesh()
    {
        try
        {
            string? entry = CurrentEncounterEntry();
            if (!string.IsNullOrEmpty(entry))
            {
                if (entry == "NECROBINDER_FLESH_BOSS_ENCOUNTER") return (true, true);
                if (entry == "NECROBINDER_DOOM_BOSS_ENCOUNTER") return (false, true);
                if (entry == "NECROBINDER_SOUL_BOSS_ENCOUNTER") return (false, true);
                if (entry == "NECROBINDER_ETHEREAL_BOSS_ENCOUNTER") return (false, true);
                if (entry == "NECROBINDER_OSTY_BOSS_ENCOUNTER") return (false, true);
                if (entry == "NECROBINDER_ERADICATE_BOSS_ENCOUNTER") return (false, true);
            }
        }
        catch { /* 继续往下判断 */ }

        try
        {
            var lineup = CurrentFightLineup();
            if (lineup != null)
            {
                foreach (var k in lineup)
                {
                    if (k == "NECROBINDER_FLESH") return (true, true);
                    if (k == "NECROBINDER_DOOM") return (false, true);
                    if (k == "NECROBINDER_SOUL") return (false, true);
                }
            }
        }
        catch { /* 继续兜底 */ }

        // 种子兜底：与灾厄骨 50/50
        try { return (Roll(0, 71, 2) == 1, false); } catch { return (false, false); }
    }

    /// <summary>
    /// 三层**铁甲战士**的档位设计是不是「巨石战」（易伤战 / 巨石战 **50/50**）。
    ///
    /// ⚠ **用户 2026-10-27：暂时恒 false** ——「巨石战」先不进正常对局，只能用控制台点名
    ///   （`fight IRONCLAD_BOULDER_BOSS_ENCOUNTER`）。要放回随机池就改成
    ///   `return Roll(2, 43, 2) == 1;`，并把 `BossKeys.RosterFor(2)` 里的 `"IRONCLAD_BOULDER"` 加回去。
    /// </summary>
    internal static bool BoulderAt3()
    {
        // 用户 2026-10-28：巨石战**放进三层正常对局**（与易伤战 50/50，salt 43 与本局种子绑定）
        try { return Roll(2, 43, 2) == 1; }
        catch { return false; }
    }

    /// <summary>
    /// 三层**储君**的档位设计是不是「**辉星储**」（无色储君 / 辉星储 **50/50**，用户 2026-10-28）。
    /// 只用「本局种子 + salt」算 ⇒ 同种子固定、联机两端一致（与刀猎/炼药猎、易伤战/巨石战同一套）。
    /// </summary>
    internal static bool RegentStarAt3()
    {
        try { return Roll(2, 47, 2) == 1; }
        catch { return false; }
    }

    /// <summary>
    /// 「**与原本 Boss 共用匹配池**」（用户 2026-10-29 新增的配置项）里，这一场是不是仍然上
    /// **这张地图原本的（原版）Boss**。
    ///
    /// 语义：开启该配置后，开了替换的地图不再「单纯被替换」，而是从
    /// 「这张地图原本的 Boss」和「这一幕本模组的 Boss」里**随机抽一个**。
    /// 这里只负责那次抽取 —— `true` = 这一场保持原版 Boss，`false` = 换成本模组的角色 Boss
    /// （此时和完全替换一样：主 Boss 是本模组的，Boss 数 = 玩家数）。
    ///
    /// ⚠ 抽取**只用「本局种子 + 幕 + 场」**算 ⇒
    ///   · 同一局里结果固定，**一幕开始（生成房间）时就已经定下来**，
    ///     所以地图预告与实际 Boss 永远一致；
    ///   · 联机两端算出同一个结果（不会不同步）。
    /// </summary>
    internal static bool SharePoolPicksVanilla(int act, int fight)
    {
        try
        {
            // salt 61 专用于「共用匹配池」的抽取；+fight 让同一幕的两场各自抽一次
            return Roll(Math.Clamp(act, 0, 2), 61 + Math.Clamp(fight, 0, 1), 2) == 0;
        }
        catch { return false; }
    }

    /// <summary>
    /// **进阶 10 第三幕固定「一场原本 Boss + 一场角色 Boss」**（用户 2026-10-29 追加，需要混匹开启）。
    ///
    /// 和 <see cref="SharePoolPicksVanilla"/> 的区别：那个是「每场各抽各的」（可能两场都是原版、
    /// 也可能两场都是角色）；这里改成**整幕只抽一次**，两场互补 ⇒ 一定是一场原版、一场角色。
    ///
    /// 哪一场是原版由种子（salt 63）决定 ⇒ 一幕开始时就定好、联机两端一致、预告 = 实际。
    /// </summary>
    internal static bool Asc10Act3PicksVanilla(int fight)
    {
        try
        {
            bool firstVanilla = Roll(2, 63, 2) == 0;
            return fight <= 0 ? firstVanilla : !firstVanilla;
        }
        catch { return false; }
    }

    /// <summary>
    /// **变体判定「本场锁定」**（用户 2026-10-29 实测：「储君第一回合是辉星储的意图，第二回合就开始打炸弹
    /// （无色储君的剧本）了」）。
    ///
    /// 原因：`BossDefinition.ActiveVariant` 每帧都会去问 `StarRegentActive` 这类委托，而它的判据
    /// 在**战斗刚开始那一小段时间里会变**（`CombatManager` 的 state / `Encounter` 还没就绪）。
    ///
    /// 两道保险：
    ///   1. 判据本身只用**联机同步的数据**（房间遭遇 / 存档里的主 Boss 遭遇 + 由它反推出的那一场阵容），
    ///      最后才退回种子；
    ///   2. **只有「决定性」的答案才会被锁定**（`Decisive = true`）。退回种子得到的答案是**不可靠**的
    ///      （战斗还没开始、遭遇还没写进去时就会走这条路），所以那种答案**不缓存**、下一次调用再算，
    ///      等真数据到了自然就对了。
    ///
    /// ⚠ 2026-10-29 修：以前把种子答案也锁进表里，于是「谁先问谁说了算」——
    /// 两个客户端问的时机不同就会拿到不同剧本 ⇒ 多人**数据不同步**
    /// （`mod_log (12).txt` 里同一秒内 `REGENT_STAR = True` 紧接着 `= False` 就是这个坑）。
    /// </summary>
    private static bool Latched(string key, Func<(bool Value, bool Decisive)> compute)
    {
        object? state = null;
        try { state = MegaCrit.Sts2.Core.Combat.CombatManager.Instance?.DebugOnlyGetState(); }
        catch { state = null; }

        lock (LatchTable)
        {
            if (!ReferenceEquals(state, _latchState))
            {
                bool had = LatchTable.Count > 0;
                _latchState = state;
                LatchTable.Clear();
                if (had)
                {
                    try { Logger.Log("[CharBoss] 变体判定：战斗状态对象变了 ⇒ 锁定清空（换场/新战斗）"); }
                    catch { /* 忽略 */ }
                }
            }
            if (LatchTable.TryGetValue(key, out bool cached)) return cached;

            bool value = false, decisive = false;
            try
            {
                (value, decisive) = compute();
            }
            catch { value = false; decisive = false; }

            if (decisive)
            {
                LatchTable[key] = value;
                try
                {
                    Logger.Log($"[CharBoss] 变体判定（本场锁定）：{key} = {value}" +
                               "（来自同步数据；换场后才会重算）");
                }
                catch { /* 忽略 */ }
            }
            return value;
        }
    }

    /// <summary>
    /// 当前这一场的阵容（**从同步数据推**）：拿存档里的主 Boss 遭遇 → 反查是哪一场的阵容。
    /// 拿不到返回 null。这样「第几场」不再依赖本机记账（`_currentFightAct`），两端一致。
    /// </summary>
    /// <summary>当前这一场的阵容（拿不到就 null）—— 给顶栏头像这类 UI 用。</summary>
    internal static string[]? LineupNow() => CurrentFightLineup();

    private static string[]? CurrentFightLineup()
    {
        try
        {
            var act = MultiBossPlan.RunStateOrNull()?.Act;
            if (act == null) return null;
            int actIndex = act.Index;

            // ★★ v0.2.261（用户 2026-11-01：「进阶10 第三幕两场战斗里都出现了炼药猎和易伤战，
            //   储君却没重复（一个辉星一个无色）」）：
            //   **先用「房间遭遇」反查这一场是哪一场**。以前只用 `act.BossEncounter`（存档里的第一个 Boss 槽），
            //   可第二场开打时那个槽里**还是第一场的主 Boss**（第二场存在 `SecondBossEncounter` 里）
            //   ⇒ 反查永远落到第一场的阵容 ⇒ 第二场的静默猎手/铁甲战士被判成**第一场的变体**
            //   （炼药猎 / 易伤战），而储君因为「房间遭遇」直接就能说清（REGENT_COLORLESS）所以没重复。
            string? encKey = BossKeys.KeyOfEncounterEntry(CurrentEncounterEntry());
            if (!string.IsNullOrEmpty(encKey))
            {
                for (int fight = 0; fight < 2; fight++)
                {
                    var lineup = For(actIndex, fight);
                    if (lineup == null || lineup.Length == 0) continue;
                    if (Array.IndexOf(lineup, encKey) >= 0) return lineup;
                }
            }

            // ② 退回旧判据：存档里的主 Boss（第一场开打时它是对的）
            string? mainKey = BossKeys.KeyOfEncounter(act.BossEncounter);
            if (string.IsNullOrEmpty(mainKey)) return null;

            for (int fight = 0; fight < 2; fight++)
            {
                var lineup = For(actIndex, fight);
                if (lineup == null || lineup.Length == 0) continue;
                foreach (var k in lineup) if (k == mainKey) return lineup;
            }
        }
        catch { /* 推不出来就返回 null */ }
        return null;
    }

    private static readonly Dictionary<string, bool> LatchTable = new(StringComparer.Ordinal);
    private static object? _latchState;

    /// <summary>
    /// **这一场战斗实际用哪套三层储君设计**（无色储君 / 辉星储）。
    /// 判据全部来自同步数据：① 房间遭遇；② 存档里的主 Boss 遭遇 → 反查这一场阵容；③ 才退回种子（不可靠、不锁定）。
    /// </summary>
    /// <summary>
    /// **一层的储君用哪套设计**：第一套「碎屑储君」还是第二套「**轰击储**」（用户 2026-11-04）。
    /// 判据同 <see cref="StarRegentActive"/>：以**房间遭遇**为准，拿不到遭遇再按种子 50/50。
    /// </summary>
    internal static bool BombardActive() => Latched("REGENT_BOMBARD", ComputeBombard);

    private static (bool Value, bool Decisive) ComputeBombard()
    {
        try
        {
            string? entry = CurrentEncounterEntry();
            if (!string.IsNullOrEmpty(entry))
            {
                if (entry == "REGENT_BOMBARD_BOSS_ENCOUNTER") return (true, true);
                if (entry == "REGENT_DEBRIS_BOSS_ENCOUNTER") return (false, true);
            }
        }
        catch { /* 继续往下判断 */ }

        // ② 从存档里的主 Boss 遭遇反查这一场阵容（同步数据）
        try
        {
            var lineup = CurrentFightLineup();
            if (lineup != null)
            {
                foreach (var k in lineup)
                {
                    if (k == "REGENT_BOMBARD") return (true, true);
                    if (k == "REGENT_DEBRIS") return (false, true);
                }
            }
        }
        catch { /* 继续兜底 */ }

        // ③ 最后才按种子算（此时遭遇/阵容都还读不到 ⇒ 不锁定）
        try { return (Roll(0, 48, 2) == 1, false); }
        catch { return (false, false); }
    }

    internal static bool StarRegentActive() => Latched("REGENT_STAR", ComputeStarRegent);

    private static (bool Value, bool Decisive) ComputeStarRegent()
    {
        try
        {
            string? entry = CurrentEncounterEntry();
            if (!string.IsNullOrEmpty(entry))
            {
                if (entry == "REGENT_STAR_BOSS_ENCOUNTER") return (true, true);
                if (entry == "REGENT_COLORLESS_BOSS_ENCOUNTER") return (false, true);
            }
        }
        catch { /* 继续往下判断 */ }

        // ② 从**存档里的主 Boss 遭遇**反查这一场阵容（同步数据；不依赖本机记账）
        try
        {
            var lineup = CurrentFightLineup();
            if (lineup != null)
            {
                foreach (var k in lineup)
                {
                    if (k == "REGENT_STAR") return (true, true);
                    if (k == "REGENT_COLORLESS") return (false, true);
                }
            }
        }
        catch { /* 继续兜底 */ }

        // ③ 最后才按种子算 —— 这是**不可靠**的答案（此时遭遇/阵容都还读不到），所以不锁定
        return (RegentStarAt3(), false);
    }

    /// <summary>
    /// **这一场战斗实际用哪套三层铁甲战士设计**（易伤战 / 巨石战）。判据同 <see cref="StarRegentActive"/>。
    /// </summary>
    internal static bool BoulderActive() => Latched("IRONCLAD_BOULDER", ComputeBoulder);

    /// <summary>
    /// 二层**铁甲战士**这一档用哪套设计：**放血战**（第一套）还是**杂耍战**（第二套，用户 2026-11-02）。
    /// 判据同 <see cref="BoulderActive"/>：以**房间遭遇**为准（控制台点名 `IRONCLAD_JUGGLER_BOSS_ENCOUNTER`
    /// 也才会真的是杂耍战），拿不到遭遇才按种子 50/50。
    /// </summary>
    internal static bool JugglerActive() => Latched("IRONCLAD_JUGGLER", ComputeJuggler);

    private static (bool Value, bool Decisive) ComputeJuggler()
    {
        try
        {
            string? entry = CurrentEncounterEntry();
            if (!string.IsNullOrEmpty(entry))
            {
                if (entry == "IRONCLAD_JUGGLER_BOSS_ENCOUNTER") return (true, true);
                if (entry == "IRONCLAD_BLOOD_BOSS_ENCOUNTER") return (false, true);
            }
        }
        catch { /* 继续往下判断 */ }

        // 存档里的主 Boss 遭遇反查（后缀剥离规则见 BossKeys）
        try
        {
            string? k = BossKeys.KeyOfEncounterEntry(CurrentEncounterEntry());
            if (k == "IRONCLAD_JUGGLER") return (true, true);
            if (k == "IRONCLAD_BLOOD") return (false, true);
        }
        catch { /* 继续往下判断 */ }

        // 都拿不到 ⇒ 按「本局种子 + salt 53」确定性 50/50（联机两端一致）
        try { return (Roll(1, 53, 2) == 1, false); }
        catch { return (false, false); }
    }


    /// <summary>
    /// 二层**储君**这一档用哪套设计：**铸剑储君**（第一套）还是**征服者储君**（第二套，用户 2026-11-02）。
    /// 判据同 <see cref="JugglerActive"/>：以**房间遭遇**为准（控制台点名
    /// `REGENT_KING_BOSS_ENCOUNTER` 也才会真的是征服者储君），拿不到遭遇才按种子 50/50。
    /// </summary>
    internal static bool KingActive() => Latched("REGENT_KING", ComputeKing);

    private static (bool Value, bool Decisive) ComputeKing()
    {
        try
        {
            string? entry = CurrentEncounterEntry();
            if (!string.IsNullOrEmpty(entry))
            {
                if (entry == "REGENT_KING_BOSS_ENCOUNTER") return (true, true);
                if (entry == "REGENT_FORGE_BOSS_ENCOUNTER") return (false, true);
            }
        }
        catch { /* 继续往下判断 */ }

        // 存档里的主 Boss 遭遇反查（后缀剥离规则见 BossKeys）
        try
        {
            string? k = BossKeys.KeyOfEncounterEntry(CurrentEncounterEntry());
            if (k == "REGENT_KING") return (true, true);
            if (k == "REGENT_FORGE") return (false, true);
        }
        catch { /* 继续往下判断 */ }

        // 都拿不到 ⇒ 按「本局种子 + salt 54」确定性 50/50（联机两端一致）
        try { return (Roll(1, 54, 2) == 1, false); }
        catch { return (false, false); }
    }

    /// <summary>
    /// 三层**亡灵契约师**这一档用哪套设计：**根除骨**（第一套）还是**养手骨**（第二套，用户 2026-11-03）。
    /// 判据同 <see cref="BoulderActive"/>：以**房间遭遇**为准，拿不到遭遇才按种子 50/50（salt 55）。
    /// </summary>
    internal static bool OstyActive() => Latched("NECROBINDER_OSTY", ComputeOsty);

    /// <summary>
    /// 二层**静默猎手**这一档用哪套设计：**涂毒猎**（第一套）还是**蛇眼猎**（第二套，用户 2026-11-03）。
    /// 判据同 <see cref="OstyActive"/>：以**房间遭遇**为准，拿不到遭遇才按种子 50/50（salt 56）。
    /// </summary>
    internal static bool SneckoActive() => Latched("SILENT_SNECKO", ComputeSnecko);

    /// <summary>一层静默猎手用哪套设计：奇巧猎（主）还是**蛇咬猎**（用户 2026-11-08）。</summary>
    internal static bool SnakeBiteActive() => Latched("SILENT_SNAKEBITE", ComputeSnakeBite);

    private static (bool Value, bool Decisive) ComputeSnakeBite()
    {
        try
        {
            string? entry = CurrentEncounterEntry();
            if (!string.IsNullOrEmpty(entry))
            {
                if (entry == "SILENT_SNAKEBITE_BOSS_ENCOUNTER") return (true, true);
                if (entry == "SILENT_SLY_BOSS_ENCOUNTER") return (false, true);
            }
        }
        catch { /* 继续往下判断 */ }
        try
        {
            var lineup = CurrentFightLineup();
            if (lineup != null)
            {
                foreach (var k in lineup)
                {
                    if (k == "SILENT_SNAKEBITE") return (true, true);
                    if (k == "SILENT_SLY") return (false, true);
                }
            }
        }
        catch { /* 继续兜底 */ }
        try { return (Roll(0, 63, 2) == 1, false); } catch { return (false, false); }
    }

    /// <summary>
    /// 三层**故障机器人**这一档用哪套设计：**能力机**（第一套）还是**事件机**（第二套，用户 2026-11-03）。
    /// 判据同 <see cref="OstyActive"/>：以**房间遭遇**为准，拿不到遭遇才按种子 50/50（salt 57）。
    /// </summary>
    internal static bool EventMachineActive() => Latched("DEFECT_EVENT", ComputeEventMachine);

    /// <summary>
    /// 三层故障机器人的**第三套设计「状态机」**（Key `DEFECT_STATUS`，用户 2026-11-05；同层还有「能力机」「事件机」）。
    /// 判据同 <see cref="EventMachineActive"/>：以**房间遭遇**为准，拿不到遭遇才按种子 50/50（salt 58）。
    /// </summary>
    internal static bool StatusActive() => Latched("DEFECT_STATUS", ComputeStatus);
    /// <summary>一层故障机器人用哪套设计：黑球机（主）还是**电球机**（用户 2026-11-05）。</summary>
    internal static bool ThunderActive() => Latched("DEFECT_THUNDER", ComputeThunder);

    /// <summary>一层铁甲战士用哪套设计：愤怒战（主）还是**打击战**（用户 2026-11-07）。</summary>
    internal static bool StrikeActive() => Latched("IRONCLAD_STRIKE", ComputeStrike);

    private static (bool Value, bool Decisive) ComputeStrike()
    {
        try
        {
            string? entry = CurrentEncounterEntry();
            if (!string.IsNullOrEmpty(entry))
            {
                if (entry == "IRONCLAD_STRIKE_BOSS_ENCOUNTER") return (true, true);
                if (entry == "IRONCLAD_ANGER_BOSS_ENCOUNTER") return (false, true);
            }
        }
        catch { /* 继续往下判断 */ }
        try
        {
            var lineup = CurrentFightLineup();
            if (lineup != null)
            {
                foreach (var k in lineup)
                {
                    if (k == "IRONCLAD_STRIKE") return (true, true);
                    if (k == "IRONCLAD_ANGER") return (false, true);
                }
            }
        }
        catch { /* 继续兜底 */ }
        try { return (Roll(0, 62, 2) == 1, false); } catch { return (false, false); }
    }
    /// <summary>二层故障机器人用哪套设计：物理机（主）还是**彩虹机**（用户 2026-11-07）。</summary>
    internal static bool RainbowActive() => Latched("DEFECT_RAINBOW", ComputeRainbow);

    private static (bool Value, bool Decisive) ComputeRainbow()
    {
        try
        {
            string? entry = CurrentEncounterEntry();
            if (!string.IsNullOrEmpty(entry))
            {
                if (entry == "DEFECT_RAINBOW_BOSS_ENCOUNTER") return (true, true);
                if (entry == "DEFECT_CLAW_BOSS_ENCOUNTER") return (false, true);
            }
        }
        catch { /* 继续往下判断 */ }
        try
        {
            var lineup = CurrentFightLineup();
            if (lineup != null)
            {
                foreach (var k in lineup)
                {
                    if (k == "DEFECT_RAINBOW") return (true, true);
                    if (k == "DEFECT_CLAW") return (false, true);
                }
            }
        }
        catch { /* 继续兜底 */ }
        try { return (Roll(0, 61, 2) == 1, false); } catch { return (false, false); }
    }

    private static (bool Value, bool Decisive) ComputeThunder()
    {
        try
        {
            string? entry = CurrentEncounterEntry();
            if (!string.IsNullOrEmpty(entry))
            {
                if (entry == "DEFECT_THUNDER_BOSS_ENCOUNTER") return (true, true);
                if (entry == "DEFECT_DARK_BOSS_ENCOUNTER") return (false, true);
            }
        }
        catch { /* 继续往下判断 */ }
        try
        {
            var lineup = CurrentFightLineup();
            if (lineup != null)
            {
                foreach (var k in lineup)
                {
                    if (k == "DEFECT_THUNDER") return (true, true);
                    if (k == "DEFECT_DARK") return (false, true);
                }
            }
        }
        catch { /* 继续兜底 */ }
        try { return (Roll(0, 59, 2) == 1, false); } catch { return (false, false); }
    }

    /// <summary>二层储君用哪套设计：铸剑储君（主）/ 王储 / **仆从储**（用户 2026-11-05）。</summary>
    internal static bool MinionActive() => Latched("REGENT_MINION", ComputeMinion);

    private static (bool Value, bool Decisive) ComputeMinion()
    {
        try
        {
            string? entry = CurrentEncounterEntry();
            if (!string.IsNullOrEmpty(entry))
            {
                if (entry == "REGENT_MINION_BOSS_ENCOUNTER") return (true, true);
                if (entry == "REGENT_FORGE_BOSS_ENCOUNTER") return (false, true);
                if (entry == "REGENT_KING_BOSS_ENCOUNTER") return (false, true);
            }
        }
        catch { /* 继续往下判断 */ }
        try
        {
            var lineup = CurrentFightLineup();
            if (lineup != null)
            {
                foreach (var k in lineup)
                {
                    if (k == "REGENT_MINION") return (true, true);
                    if (k == "REGENT_FORGE") return (false, true);
                    if (k == "REGENT_KING") return (false, true);
                }
            }
        }
        catch { /* 继续兜底 */ }
        try { return (Roll(1, 60, 2) == 1, false); } catch { return (false, false); }
    }

    private static (bool Value, bool Decisive) ComputeStatus()
    {
        // ★ 2026-11-05：状态机挪到**三层**以后，同层故障机器人有 3 个设计
        //   （能力机 `DEFECT_AI` / 事件机 `DEFECT_EVENT` / 状态机 `DEFECT_STATUS`）。
        //   判据必须把「同层另外两个」都当作**决定性的否**，否则会掉到种子兜底、
        //   把一只能力机的战斗错认成状态机（剧本/卡组全错）。
        try
        {
            string? entry = CurrentEncounterEntry();
            if (!string.IsNullOrEmpty(entry))
            {
                if (entry == "DEFECT_STATUS_BOSS_ENCOUNTER") return (true, true);
                if (entry == "DEFECT_AI_BOSS_ENCOUNTER") return (false, true);
                if (entry == "DEFECT_EVENT_BOSS_ENCOUNTER") return (false, true);
                if (entry == "DEFECT_CLAW_BOSS_ENCOUNTER") return (false, true);
            }
        }
        catch { /* 继续往下判断 */ }

        try
        {
            var lineup = CurrentFightLineup();
            if (lineup != null)
            {
                foreach (var k in lineup)
                {
                    if (k == "DEFECT_STATUS") return (true, true);
                    if (k == "DEFECT_AI") return (false, true);
                    if (k == "DEFECT_EVENT") return (false, true);
                    if (k == "DEFECT_CLAW") return (false, true);
                }
            }
        }
        catch { /* 继续兜底 */ }

        try { return (Roll(2, 58, 2) == 1, false); }
        catch { return (false, false); }
    }
    private static (bool Value, bool Decisive) ComputeEventMachine()
    {
        try
        {
            string? entry = CurrentEncounterEntry();
            if (!string.IsNullOrEmpty(entry))
            {
                if (entry == "DEFECT_EVENT_BOSS_ENCOUNTER") return (true, true);
                if (entry == "DEFECT_AI_BOSS_ENCOUNTER") return (false, true);
                if (entry == "DEFECT_STATUS_BOSS_ENCOUNTER") return (false, true);
            }
        }
        catch { /* 继续往下判断 */ }

        try
        {
            var lineup = CurrentFightLineup();
            if (lineup != null)
            {
                foreach (var k in lineup)
                {
                    if (k == "DEFECT_EVENT") return (true, true);
                    if (k == "DEFECT_AI") return (false, true);
                    if (k == "DEFECT_STATUS") return (false, true);
                }
            }
        }
        catch { /* 继续兜底 */ }

        try { return (Roll(2, 57, 2) == 1, false); }
        catch { return (false, false); }
    }

    private static (bool Value, bool Decisive) ComputeSnecko()
    {
        try
        {
            string? entry = CurrentEncounterEntry();
            if (!string.IsNullOrEmpty(entry))
            {
                if (entry == "SILENT_SNECKO_BOSS_ENCOUNTER") return (true, true);
                if (entry == "SILENT_ENVENOM_BOSS_ENCOUNTER") return (false, true);
            }
        }
        catch { /* 继续往下判断 */ }

        try
        {
            var lineup = CurrentFightLineup();
            if (lineup != null)
            {
                foreach (var k in lineup)
                {
                    if (k == "SILENT_SNECKO") return (true, true);
                    if (k == "SILENT_ENVENOM") return (false, true);
                }
            }
        }
        catch { /* 继续兜底 */ }

        try { return (Roll(1, 56, 2) == 1, false); }
        catch { return (false, false); }
    }
    private static (bool Value, bool Decisive) ComputeOsty()
    {
        try
        {
            string? entry = CurrentEncounterEntry();
            if (!string.IsNullOrEmpty(entry))
            {
                if (entry == "NECROBINDER_OSTY_BOSS_ENCOUNTER") return (true, true);
                if (entry == "NECROBINDER_ERADICATE_BOSS_ENCOUNTER") return (false, true);
            }
        }
        catch { /* 继续往下判断 */ }

        try
        {
            var lineup = CurrentFightLineup();
            if (lineup != null)
            {
                foreach (var k in lineup)
                {
                    if (k == "NECROBINDER_OSTY") return (true, true);
                    if (k == "NECROBINDER_ERADICATE") return (false, true);
                }
            }
        }
        catch { /* 继续兜底 */ }

        try { return (Roll(2, 55, 2) == 1, false); }
        catch { return (false, false); }
    }
    private static (bool Value, bool Decisive) ComputeBoulder()
    {
        try
        {
            string? entry = CurrentEncounterEntry();
            if (!string.IsNullOrEmpty(entry))
            {
                if (entry == "IRONCLAD_BOULDER_BOSS_ENCOUNTER") return (true, true);
                if (entry == "IRONCLAD_VULN_BOSS_ENCOUNTER") return (false, true);
            }
        }
        catch { /* 继续往下判断 */ }

        try
        {
            var lineup = CurrentFightLineup();
            if (lineup != null)
            {
                foreach (var k in lineup)
                {
                    if (k == "IRONCLAD_BOULDER") return (true, true);
                    if (k == "IRONCLAD_VULN") return (false, true);
                }
            }
        }
        catch { /* 继续兜底 */ }

        return (BoulderAt3(), false);
    }

    /// <summary>最近一次「正在生成 BOSS」的那一场（由 <see cref="NoteCurrentFight"/> 记录）。</summary>
    private static int _currentFightAct = -1;
    private static int _currentFightIndex;

    /// <summary>
    /// 记下「现在正在生成第 act 幕第 fight 场的 BOSS」。
    ///
    /// ⚠ 2026-10-29：**变体判定已经不再用它了**（那是本地记账，两端时机不同 ⇒ 不同步；
    /// 现在改成从同步数据推，见 `CurrentFightLineup`）。保留这个方法只是因为别处还在调用，
    /// 目前只用于诊断日志与「这一次生成时用的是哪一场」的记录。
    /// </summary>
    internal static void NoteCurrentFight(int act, int fight)
    {
        _currentFightAct = act;
        _currentFightIndex = fight;
    }

    /// <summary>
    /// **本场正在生成的遭遇**（由 <c>EncounterRunStateCapturePatch</c> 在
    /// `EncounterModel.GenerateMonstersWithSlots` 的**前缀**里记录）。
    ///
    /// ★ v0.2.417（用户实测：`fight DEFECT_EVENT_BOSS_ENCOUNTER` 打出来的是**能力机**）：
    ///   原因就是**时序** —— 卡组/遗物/剧本是在「创建怪物」那一刻定的，
    ///   而那时 `CombatManager` 的 `state.Encounter` **还没赋值**
    ///   （日志同屏还有 `act index via combat state failed … Creature was accessed before it was set`）。
    ///   ⇒ `CurrentEncounterEntry()` 返回 null ⇒ 变体判定掉到「按种子 50/50」⇒ 控制台点名也未必生效
    ///   （这次 `DEFECT_EVENT` 落到 false，于是用了同层的 1 号设计＝能力机；
    ///    而 `DEFECT_STATUS` 稍后一瞬再问时遭遇已经就绪，才被判成 False 并锁定）。
    ///
    ///   现在改成：**生成遭遇时就把它记下来**，判定优先用它 —— 那一刻它一定是对的，
    ///   而且对正常流程没有任何影响（正常流程里它和 `state.Encounter` 是同一个）。
    /// </summary>
    private static string? _fightEncounterEntry;

    /// <summary>记下「本场正在生成的遭遇 id」（见 <see cref="_fightEncounterEntry"/> 的说明）。</summary>
    internal static void NoteFightEncounter(string? entry)
    {
        if (string.IsNullOrEmpty(entry)) return;
        _fightEncounterEntry = entry;

        // ★ v0.2.418：新的一场开始了 ⇒ **把变体判定的本场锁定清掉**。
        //   否则「上一场/开场前一瞬」用旧遭遇算出来的答案会留在这张表里，
        //   等真正的遭遇记进来时已经晚了（用户实测：`fight DEFECT_EVENT_BOSS_ENCOUNTER` 打成能力机）。
        try
        {
            lock (LatchTable)
            {
                if (LatchTable.Count > 0) Logger.Log("[CharBoss] 变体判定：本场遭遇已确定 ⇒ 锁定清空（重新判定）");
                LatchTable.Clear();
            }
        }
        catch { /* 忽略 */ }
    }

    /// <summary>当前这一场的遭遇 id（拿不到返回 null）。</summary>
    internal static string? CurrentEncounterEntry()
    {
        // ① **生成遭遇时记下的**（造怪物那一刻它一定是最新的；而 `state.Encounter` 那一刻还是**上一场**的
        //   ⇒ 2026-11-06 用户实测：`fight DEFECT_EVENT_BOSS_ENCOUNTER` 又变回能力机，就是因为读到上一场（电球机）的遭遇）。
        //   它由 `GenerateMonsters` 开头写入、战斗结束时 `BeforeRemovedFromRoom` 清空 ⇒ 不会串场。
        try
        {
            if (!string.IsNullOrEmpty(_fightEncounterEntry)) return _fightEncounterEntry;
        }
        catch { /* 往下走 */ }

        // ② 战斗状态里的（战斗开始后与 ① 等价，作兜底）
        try
        {
            var state = MegaCrit.Sts2.Core.Combat.CombatManager.Instance?.DebugOnlyGetState();
            return state?.Encounter?.Id.Entry;
        }
        catch { return null; }

        return null;
    }

    /// <summary>战斗结束时把「本场遭遇」清掉（防止串到下一场的变体判定）。</summary>
    internal static void ClearFightEncounter() => _fightEncounterEntry = null;

    /// <summary>最近一次成功读到的玩家数（同一局人数不会变；读不到时用它，见下）。</summary>
    private static int _lastKnownPlayers;

    /// <summary>
    /// 本局玩家数。
    ///
    /// ★ 2026-11-05（自检「多人会不会因为两端抽到的 Boss 不同而不同步」）：
    ///   阵容的**条数**就等于玩家数，所以这个数一旦一端读到 2、另一端读不到退回 1，
    ///   就会出现「房主 2 只、客人 1 只」—— 那正是 2026-10-30 那次不同步的形态。
    ///   以前读不到就 `?? 1`（**默认单人**，等于主动制造差异）；现在改成：
    ///   读不到就沿用**最近一次成功读到的值**，只有从没读到过才退回 1。
    /// </summary>
    internal static int PlayerCount()
    {
        int n = 0;
        try { n = MultiBossPlan.RunStateOrNull()?.Players?.Count ?? 0; } catch { n = 0; }
        if (n > 0)
        {
            _lastKnownPlayers = n;
            return n;
        }
        return _lastKnownPlayers > 0 ? _lastKnownPlayers : 1;
    }

    /// <summary>换局（种子变了）时把「记住了几个人」也清掉，免得上局的人数漏到下一局。</summary>
    private static void ResetPlayerCountMemory() { _lastKnownPlayers = 0; }

    /// <summary>当前进阶等级（拿不到按 0）。</summary>
    internal static int Ascension()
    {
        try { return MultiBossPlan.RunStateOrNull()?.AscensionLevel ?? 0; }
        catch { return 0; }
    }

    /// <summary>
    /// 确定性随机：只用「本局 RNG 种子 + 幕 + 用途 salt」算，两端一致、同一局里稳定。
    /// </summary>
    private static int Roll(int act, int salt, int count)
    {
        if (count <= 1) return 0;
        ulong seed = CurrentSeed();

        ulong h;
        unchecked
        {
            h = seed * 6364136223846793005UL
                + (ulong)(act + 7) * 1442695040888963407UL
                + (ulong)(salt + 13) * 2862933555777941757UL;
            h ^= h >> 33;
            h *= 0xff51afd7ed558ccdUL;
            h ^= h >> 33;
            // ★ v0.2.285（用户 2026-10-31「共用匹配池好像不起作用，一直给替换的 boss」）：
            //   以前直接 `h % count` 取的是**最低比特**，而输入里三项系数都是奇数 ⇒
            //   当种子退化成常量（读不到本局种子时是 `1` 或 `FreshSeed()`）时，
            //   奇偶性被 (1 + act + salt) 决定 ⇒ **三幕会一起落到同一侧**（实测 seed=1 → 1,1,1 = 全是角色 Boss）。
            //   再加一轮强混合（并把结果取到高位），常量种子下也能拉开分布。
            h ^= h >> 29;
            h *= 0xc2b2ae3d27d4eb4fUL;
            h ^= h >> 32;
        }

        return (int)(h % (ulong)count);
    }

}
