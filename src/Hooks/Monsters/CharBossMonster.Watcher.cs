#if STS2_111   // 观者 Boss 只在 0.113 构建里（用户：107 不加这个 Boss）
using System;
using System.Collections.Generic;
using System.Reflection;
using MegaCrit.Sts2.Core.Models;

namespace CharacterBosses.Monsters;

/// <summary>
/// 观者（神格观）专用的几处小逻辑，单独一个 partial 文件免得把主文件撑大。
///
/// 两条主线：
/// 1. **她打出「聚能成刃」时，把生成的那张「灭除之刃」亮给玩家看**
///    （用户 2026-11-15：「用事件机的羽化一样的方法展示」「要保证段数正确」）；
/// 2. **之后所有「灭除之刃」的段数**（用户 2026-11-16 第 4 条：应为 3 / 6 / 9 …）。
///
/// 反编译实证（两个观者模组都一样，见 `docs/plans/watcher-divinity-fixes-2026-11-16.md` §0）：
///   · `WatcherConjureBlade.OnPlay`：`num2 = ResolveEnergyXValue()`（升级再 +1）⇒
///     `CreateCard&lt;WatcherExpunger&gt;()` + `HitCount = num2` ⇒
///     `AddGeneratedCardToCombat(card, (PileType)1, …)` = **抽牌堆**（不是手牌！）；
///   · 灭除之刃的段数就是它自己的 `DynamicVars.Repeat`（`HitCount` 属性是它的 setter），
///     真实结算 `DamageCmd.Attack(Damage).WithHitCount(Repeat.IntValue)`；意图侧的
///     `BossDamagePredictor.Hits` 也读同一个变量 ⇒ **只要把这个 `Repeat` 设对，显示与实伤就都对了**。
///
/// ⚠ 为什么以前段数恒为 1：剧本第 5 / 9 回合的灭除之刃是 `BossScriptRunner.BuildHand` **按类型新造**的一张牌
/// （`Repeat` 是默认值 1），而带段数的那张实体躺在**抽牌堆**里，两者毫无关系。
/// 所以这里在她真的打完聚能成刃之后把段数记在 Boss 身上，之后每次规划都把新造的那张改成同一个数。
/// </summary>
public abstract partial class CharBossMonster
{
    /// <summary>
    /// 观者牌的 **id 双版兼容**（用户 2026-11-16 第 5 条）：
    /// A 版（3747526116）用下划线 `WATCHER_CONJURE_BLADE`，B 版（3747492505）用连字符 `WATCHER-CONJURE_BLADE`。
    /// 把 `-` / `_` 全去掉再比后缀 ⇒ 两套都命中，也不用再在每个地方写两遍常量。
    ///
    /// ★ v0.2.699（用户 2026-11-16：「第 4 和第 8 回合的还是没修好」）：观者模组还有**第三套 id** ——
    ///   「玩家版」牌带 `_P` 后缀，例如 `WATCHER_ERUPTION_P`（暴怒）、`WATCHER_STRIKE_P`（打击）。
    ///   规范化后是 `WATCHERERUPTIONP`，`EndsWith("ERUPTION")` = **false** ⇒
    ///   「暴怒 ⇒ 进入愤怒」这一步在剧本推演里从来没命中过 ⇒ 后面的打击/不惧妖邪永远按 ×1 算（显示 7 / 9）。
    ///   现在多比一次「去掉末尾单个字母」的形态；`PRESSURX_POINTS` 那种本来就以关键片段结尾的照旧命中。
    /// </summary>
    internal static bool WatcherIdIs(string? entry, string suffix)
    {
        if (string.IsNullOrEmpty(entry) || string.IsNullOrEmpty(suffix)) return false;
        var e = Normalize(entry);
        var s = Normalize(suffix);
        if (e.EndsWith(s, StringComparison.Ordinal)) return true;

        // 去掉末尾的**单个字母**变体标记（A 版玩家牌的 `_P` / `${_A}` / `_B`）再比一次。
        if (e.Length > 1 && char.IsLetter(e[e.Length - 1]) &&
            e.Substring(0, e.Length - 1).EndsWith(s, StringComparison.Ordinal)) return true;

        // ★ v0.2.701：B 版的**基础牌/衍生牌**会带 `_WATCHER` 尾巴（日志实证：
        //   `WATCHER-STRIKE_WATCHER`、`WATCHER-DEFEND_WATCHER`、`WATCHER-WISH_WATCHER`），
        //   规范化后是 `…WATCHER` ⇒ 再比一次「去掉末尾 7 个字符 WATCHER」的形态。
        const string Tail = "WATCHER";
        if (e.Length > Tail.Length && e.EndsWith(Tail, StringComparison.Ordinal) &&
            e.Substring(0, e.Length - Tail.Length).EndsWith(s, StringComparison.Ordinal)) return true;

        return false;
    }

    private static string Normalize(string s)
    {
        var sb = new System.Text.StringBuilder(s.Length);
        foreach (char c in s)
        {
            if (c == '-' || c == '_' || c == ' ') continue;
            sb.Append(char.ToUpperInvariant(c));
        }
        return sb.ToString();
    }

    /// <summary>刚才打出去的是不是「聚能成刃」（A `WATCHER_CONJURE_BLADE` / B `WATCHER-CONJURE_BLADE`）。</summary>
    private bool IsConjureBlade(CardModel? card)
    {
        try { return card != null && WatcherIdIs(card.Id.Entry, "CONJURE_BLADE"); }
        catch { return false; }
    }

    /// <summary>这张牌是不是「灭除之刃」（A `WATCHER_EXPUNGER` / B `WATCHER-EXPUNGER`）。</summary>
    internal static bool IsExpunger(CardModel? card)
    {
        try { return card != null && WatcherIdIs(card.Id.Entry, "EXPUNGER"); }
        catch { return false; }
    }

    /// <summary>
    /// 她**所有牌堆**里现存的「灭除之刃」（抽牌堆 / 手牌 / 弃牌堆 / 消耗堆全扫）。
    /// 以前只扫 `pcs.Hand` ⇒ 生成的那张在抽牌堆里，永远扫不到（用户 2026-11-16 第 3 条）。
    /// </summary>
    private List<CardModel> GeneratedExpungers(out string pileDesc)
    {
        var list = new List<CardModel>();
        pileDesc = "无";
        try
        {
            var pcs = Shadow?.PlayerCombatState;
            if (pcs?.AllPiles == null) return list;
            foreach (var pile in pcs.AllPiles)
            {
                if (pile == null) continue;
                string pileName = PileName(pile.Type);
                foreach (var c in pile.Cards)
                {
                    if (!IsExpunger(c)) continue;
                    list.Add(c!);
                    if (pileDesc == "无") pileDesc = pileName;
                }
            }
        }
        catch { /* 纯展示 */ }
        return list;
    }

    private static string PileName(MegaCrit.Sts2.Core.Entities.Cards.PileType type)
    {
        try { return type.ToString(); } catch { return "?"; }
    }


    // ── 灭除之刃的段数（用户 2026-11-16 第 4 条）────────────────────────────
    /// <summary>她最近一次「聚能成刃」实际吃到的 X（战斗内一直保留；0 = 还没打过）。</summary>
    internal int WatcherExpungerHits;

    /// <summary>
    /// 她**刚打完聚能成刃**之后调一次：从她所有牌堆里找到那张生成的灭除之刃，
    /// 读它的 `Repeat`（= 实付 X，升级版再 +1），存起来给后面几回合用；顺便亮给玩家看。
    /// 一行日志把「在哪个堆 + 段数」都打出来（用户要求：先摆事实）。
    /// </summary>
    internal void NoteWatcherConjuredBlade(CardModel? playedCard)
    {
        if (!IsConjureBlade(playedCard)) return;
        try
        {
            var cards = GeneratedExpungers(out string pileDesc);
            int hits = 0;
            foreach (var c in cards) hits = Math.Max(hits, HitsOf(c));

            if (hits > 0)
            {
                WatcherExpungerHits = hits;
                Logger.Log($"[CharBoss] 聚能成刃：生成的灭除之刃在「{pileDesc}」({cards.Count} 张)，" +
                           $"段数(Repeat)={hits} ⇒ 之后剧本里的灭除之刃都按 {hits} 段");
            }
            else
            {
                Logger.Log($"[CharBoss] 聚能成刃：打完了，但所有牌堆里都没找到灭除之刃" +
                           $"（{DescribePiles()}）⇒ 段数无法记录");
            }

            if (cards.Count > 0)
            {
                MegaCrit.Sts2.Core.Helpers.TaskHelper.RunSafely(
                    Core.BossCardPreview.ShowAsync(this, cards, 1.8f));
                Logger.Log($"[CharBoss] 聚能成刃：用原版预览界面展示 {cards.Count} 张灭除之刃（段数 {hits}）");
            }
        }
        catch (Exception e) { Logger.Log("[CharBoss] 灭除之刃展示失败: " + e.Message); }
    }

    /// <summary>
    /// 规划前把**手牌里那张（剧本新造的）灭除之刃**的段数改成她真正付过的 X。
    ///
    /// 为什么必须在规划前：意图走 `BossDamagePredictor.Hits` → `DynamicVars.Repeat`，
    /// 真实结算走牌自己的 `WithHitCount(Repeat)` —— 同一个变量，改一次两边都对。
    /// 只改高于默认值的那些（她没打过聚能成刃时 `WatcherExpungerHits == 0`，一个字都不动）。
    /// </summary>
    internal void ApplyWatcherConjuredHits()
    {
        if (WatcherExpungerHits <= 0) return;
        try
        {
            var hand = Shadow?.PlayerCombatState?.Hand?.Cards;
            if (hand == null) return;
            foreach (var card in hand)
            {
                if (!IsExpunger(card)) continue;
                int before = HitsOf(card!);
                if (before == WatcherExpungerHits) continue;
                SetHits(card!, WatcherExpungerHits);
                Logger.Log($"[CharBoss] 灭除之刃：剧本那张的段数 {before} → {WatcherExpungerHits}" +
                           "（按她上次聚能成刃实付的 X）");
            }
        }
        catch (Exception e) { Logger.Log("[CharBoss] 灭除之刃段数修正失败: " + e.Message); }
    }

    /// <summary>读这张牌现在的段数（A/B 两版都是 `HitCount` 属性 / `DynamicVars.Repeat`）。</summary>
    internal static int HitsOf(CardModel card)
    {
        try
        {
            var prop = card.GetType().GetProperty("HitCount", BindingFlags.Public | BindingFlags.Instance);
            if (prop != null && prop.CanRead && prop.PropertyType == typeof(int))
                return Math.Max(0, (int)(prop.GetValue(card) ?? 0));
        }
        catch { /* 退回 Repeat 变量 */ }
        try { return Math.Max(0, (int)Math.Round((double)card.DynamicVars["Repeat"].BaseValue)); }
        catch { return 0; }
    }

    /// <summary>把段数写进这张牌（优先走 `HitCount` 属性，它内部就是写 `Repeat`）。</summary>
    internal static void SetHits(CardModel card, int hits)
    {
        try
        {
            var prop = card.GetType().GetProperty("HitCount", BindingFlags.Public | BindingFlags.Instance);
            if (prop != null && prop.CanWrite && prop.PropertyType == typeof(int))
            {
                prop.SetValue(card, Math.Max(0, hits));
                return;
            }
        }
        catch { /* 退回 Repeat 变量 */ }
        try { card.DynamicVars["Repeat"].BaseValue = Math.Max(0, hits); }
        catch (Exception e) { Logger.Log("[CharBoss] 灭除之刃段数写入失败: " + e.Message); }
    }

    /// <summary>诊断用：把她四个牌堆的张数与内容打出来（找 token 牌落在哪）。</summary>
    internal string DescribePiles()
    {
        try
        {
            var pcs = Shadow?.PlayerCombatState;
            if (pcs?.AllPiles == null) return "无牌堆";
            var parts = new List<string>();
            foreach (var pile in pcs.AllPiles)
            {
                if (pile == null) continue;
                var entries = new List<string>();
                foreach (var c in pile.Cards)
                {
                    try { entries.Add(c?.Id.Entry ?? "?"); } catch { entries.Add("?"); }
                }
                parts.Add($"{PileName(pile.Type)}({entries.Count})[{string.Join(",", entries)}]");
            }
            return string.Join(" ", parts);
        }
        catch (Exception e) { return "牌堆读取失败: " + e.Message; }
    }
}
#endif
