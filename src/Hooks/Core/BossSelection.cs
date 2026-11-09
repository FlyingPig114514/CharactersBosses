using System;
using System.Collections.Generic;

namespace CharacterBosses.Core;

/// <summary>
/// **A2 · 每张地图允许遇到哪些 Boss**（用户 2026-11-06 改版：原来的 A2「同幕混合匹配」被它取代）。
///
/// ## 规则（用户原话）
/// 「每个在 A1 开启 boss 替换地图都可以单独选择你想遇到的 boss
///   （每个角色至少选择 1 个共至少 5 个，3 层则至少每个角色选择 2 个共至少 10 个）。」
///   · 第一幕候补 7 只（战 1 / 猎 1 / 机 2 / 骨 1 / 储 2）⇒ 每角色 ≥1 ⇒ **至少 5**；
///   · 第二幕候补 9 只（战 2 / 猎 2 / 机 1 / 骨 1 / 储 3）⇒ 每角色 ≥1 ⇒ **至少 5**；
///   · 第三幕候补 11 只（战 2 / 猎 2 / 机 3 / 骨 2 / 储 2）⇒ 每角色 ≥2 ⇒ **至少 10**。
/// 某个角色在本幕本来就没有 2 只时，下限自动降到它的实际数量（不会要求选不存在的东西）。
///
/// ## 多人：「以房主设置为准」
/// 这套勾选属于**影响怎么抽 Boss 的玩法设置**，和 A1（要替换的地图）/A4（共用匹配池）同一类：
///   · **房主**按自己的勾选抽出**主 Boss**，结论通过**同步的遭遇**（`act.BossEncounter` /
///     `SecondBossEncounter`）传到客人端 —— 客人端本来就会跟随同步遭遇（`CharBossEncounters` 的 guest-follow），
///     所以「以房主设置为准」**不需要新增任何网络消息**；
///   · **客人端不读本机勾选**（`AvailableFor` 直接返回整张候补表），避免用本机配置去猜房主的阵容；
///   · 多人时**搭子（第 2、3 只 Boss）不受勾选影响**，永远从整幕候补里按种子确定性抽取
///     （两端必须算出同一套，而勾选是各机自己的文件 —— 那里掺进用户选择就会不同步）。
///     单人局只有 1 只 Boss，所以**勾选在单人局里 100% 生效**。
/// </summary>
internal static class BossSelection
{
    /// <summary>本幕「每个角色至少选几个」的**目标值**（第三幕 2，其它 1）。实际下限见 <see cref="MinForCharacter"/>。</summary>
    internal static int TargetMinPerCharacter(int act) => act >= 2 ? 2 : 1;

    /// <summary>本机算不算「说了算」的那一方（单人 / 房主）。客人端不读本机勾选。</summary>
    internal static bool Authoritative => BossConfig.GameplaySettingsFollowHost;

    private static bool _guestLogged;

    /// <summary>
    /// 这一幕**实际可用的候补**（顺序与 <see cref="BossKeys.RosterFor"/> 一致）。
    ///
    /// · 单人 / 房主：整张候补表 ∩ 当前地图的勾选；勾选为空 / 全非法（跨版本删过 Key）⇒ 回退整张表并记一行日志。
    /// · 客人：直接整张表（房主的结论走同步遭遇，不靠本机配置猜）。
    /// </summary>
    internal static string[] AvailableFor(int act, string? mapEntry = null)
        => AvailableForCore(act, mapEntry, true);

    /// <summary>
    /// **配置界面用**：不看「客人跟随房主」那条规则，永远读本机勾选
    /// （玩家在客人端也能改自己的配置文件；他哪天当房主就生效）。
    /// </summary>
    internal static string[] AvailableForUi(int act, string? mapEntry)
        => AvailableForCore(act, mapEntry, false);

    private static string[] AvailableForCore(int act, string? mapEntry, bool allowHost)
    {
        var full = BossKeys.RosterFor(act);
        try
        {
            string? entry = mapEntry ?? CurrentMapEntry();

            // ★★ v0.2.535（用户 2026-11-09：「多人时所有 boss 都只用房主勾选的」）：
            //   客人以前**直接返回整张候补表**（因为读不到房主的配置文件）⇒ 勾选在多人里等于没用。
            //   现在房主会把 pickset 广播过来（`Core/BossPicksSync.cs`）：
            //     · 收到了 ⇒ 用**房主那一套**（主 Boss 与搭子都按它抽，两端因此完全一致）；
            //     · 还没收到 ⇒ 退回整张表（旧行为，不会算错也不会卡住，收到后会清缓存重算）。
            if (allowHost && !Authoritative)
            {
                var synced = BossPicksSync.HostPicksOf(entry);
                if (synced == null)
                {
                    if (!_guestLogged)
                    {
                        _guestLogged = true;
                        Logger.Log("[CharBoss] Boss 选择：本机是客人，还没收到房主的勾选 ⇒ 先按整张候补表算" +
                                   "（收到后会自动重算）");
                    }
                    return full;
                }
                return FilterByPicks(full, synced, entry);
            }

            var picks = BossConfig.BossPicksOf(entry);
            if (picks == null) return full;         // 没配过 = 全选
            return FilterByPicks(full, picks, entry);
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] Boss 选择：读取失败，回退整张候补表: " + e.Message);
            return full;
        }
    }

    /// <summary>把「整张候补表」按一份勾选过滤（保持候补表的原顺序）。</summary>
    private static string[] FilterByPicks(string[] full, IEnumerable<string> picks, string? entry)
    {
        var keep = new HashSet<string>(StringComparer.Ordinal);
        var unknown = new List<string>();
        foreach (var key in picks)
        {
            if (string.IsNullOrWhiteSpace(key)) continue;
            if (Array.IndexOf(full, key) >= 0) keep.Add(key);
            else unknown.Add(key);
        }
        if (unknown.Count > 0)
        {
            Logger.Log($"[CharBoss] Boss 选择：地图 {entry} 的勾选里有 {unknown.Count} 个本幕不认识的 Key " +
                       $"[{string.Join(",", unknown)}]（跨版本删掉的设计？已忽略）");
        }
        if (keep.Count == 0)
        {
            Logger.Log($"[CharBoss] Boss 选择：地图 {entry} 的勾选一个都不匹配 ⇒ 回退整张候补表");
            return full;
        }

        var result = new List<string>(keep.Count);
        foreach (var key in full) if (keep.Contains(key)) result.Add(key);   // 保持原顺序
        return result.ToArray();
    }

    /// <summary>
    /// **第 <paramref name="act"/> 幕对应的地图 id**（勾选是按地图存的）。
    ///
    /// 为什么不直接用 <see cref="CurrentMapEntry"/>：三层阵容是**开局那一刻就把第 1、2、3 幕全预告一遍**的，
    /// 那时「当前幕」还是第 1 幕 ⇒ 用它会拿第 1 幕的勾选去算第 3 幕的阵容。
    /// 这里优先按幕取 `ModelDb.ActsByIndex`，取不到才退回当前幕。
    /// </summary>
    internal static string? MapEntryOfAct(int act)
    {
        // ① 就是当前幕 ⇒ 直接用跑着的这张图（最准，模组加的地图也对）
        try
        {
            var state = MultiBossPlan.RunStateOrNull();
            if (state != null && act >= 0 && act == state.CurrentActIndex)
            {
                var entry = BossConfig.EntryOf(state.Act);
                if (!string.IsNullOrEmpty(entry)) return entry;
            }
        }
        catch { /* 走下一条 */ }

        // ② 否则按幕序号从 `ModelDb.ActsByIndex` 取该幕的候选地图（取第一个能拿到 id 的）
        try
        {
            var acts = MegaCrit.Sts2.Core.Models.ModelDb.ActsByIndex;
            if (acts != null && act >= 0 && act < acts.Count)
            {
                var candidates = acts[act];
                if (candidates != null)
                {
                    foreach (var candidate in candidates)
                    {
                        var entry = BossConfig.EntryOf(candidate);
                        if (!string.IsNullOrEmpty(entry)) return entry;
                    }
                }
            }
        }
        catch { /* 退回当前幕 */ }
        return CurrentMapEntry();
    }

    /// <summary>本幕候补里这个角色一共有几只（≥1 才有意义）。</summary>
    internal static int CountOfCharacter(int act, int character)
    {
        int n = 0;
        foreach (var key in BossKeys.RosterFor(act)) if (BossKeys.CharacterOf(key) == character) n++;
        return n;
    }

    /// <summary>
    /// 本幕这个角色的**实际下限** = min(目标值, 它在本幕的实际只数)。
    /// （第一幕的战士/猎人/亡灵各只有 1 只 ⇒ 下限 1；第三幕每个角色都有 ≥2 只 ⇒ 下限 2。）
    /// </summary>
    internal static int MinForCharacter(int act, int character)
        => Math.Max(1, Math.Min(TargetMinPerCharacter(act), CountOfCharacter(act, character)));

    /// <summary>这张地图当前已选了几只（没配过 = 整张候补表）。</summary>
    internal static int PickedCount(int act, string? mapEntry)
        => AvailableFor(act, mapEntry).Length;

    /// <summary>某只 Boss 在这张地图上是不是被勾上了。</summary>
    internal static bool IsPicked(int act, string? mapEntry, string key, bool forUi = false)
        => Array.IndexOf(forUi ? AvailableForUi(act, mapEntry) : AvailableFor(act, mapEntry), key) >= 0;

    /// <summary>
    /// 能不能把这只取消掉：取消后**它所在角色剩下的只数不能低于下限**。
    /// 配置界面用它来「直接拦住不让取消」（用户 2026-11-06 选的做法）。
    /// </summary>
    internal static bool CanUnpick(int act, string? mapEntry, string key, bool forUi = false)
    {
        try
        {
            var avail = forUi ? AvailableForUi(act, mapEntry) : AvailableFor(act, mapEntry);
            if (Array.IndexOf(avail, key) < 0) return false;      // 本来就没选
            int ch = BossKeys.CharacterOf(key);
            if (ch < 0) return true;

            int left = 0;
            foreach (var k in avail)
            {
                if (k == key) continue;
                if (BossKeys.CharacterOf(k) == ch) left++;
            }
            return left >= MinForCharacter(act, ch);
        }
        catch { return true; }        // 判不清就放行（宁可让玩家取消，也别把人锁死）
    }

    /// <summary>勾 / 取消一只（取消会被 <see cref="CanUnpick"/> 挡住）。返回是否真的改了。</summary>
    internal static bool SetPicked(int act, string? mapEntry, string key, bool on)
    {
        try
        {
            string? entry = mapEntry ?? CurrentMapEntry();
            if (string.IsNullOrEmpty(entry)) return false;

            var avail = AvailableForUi(act, entry);   // 配置界面改的是本机文件
            bool picked = Array.IndexOf(avail, key) >= 0;
            if (picked == on) return false;
            if (!on && !CanUnpick(act, entry, key, forUi: true)) return false;

            var list = new List<string>(avail);
            if (on) list.Add(key);
            else list.Remove(key);

            // 顺序按整张候补表排一下（确定性 / 日志更好读）
            var ordered = new List<string>(list.Count);
            foreach (var k in BossKeys.RosterFor(act)) if (list.Contains(k)) ordered.Add(k);

            // 与整张表一模一样 ⇒ 直接删掉这一项（配置文件保持干净）
            bool all = ordered.Count == BossKeys.RosterFor(act).Length;
            BossConfig.SetBossPicks(entry, all ? null : ordered);
            return true;
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] Boss 选择：写入失败: " + e.Message);
            return false;
        }
    }

    /// <summary>把这张地图恢复成「全选」。</summary>
    internal static void ResetToAll(string? mapEntry)
    {
        try
        {
            string? entry = mapEntry ?? CurrentMapEntry();
            if (string.IsNullOrEmpty(entry)) return;
            BossConfig.SetBossPicks(entry, null);
        }
        catch { /* 忽略 */ }
    }

    /// <summary>当前这一幕的地图 id（`ActModel.Id.Entry`）；拿不到返回 null。</summary>
    internal static string? CurrentMapEntry()
    {
        try { return BossConfig.EntryOf(MultiBossPlan.RunStateOrNull()?.Act); }
        catch { return null; }
    }
}
