using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.TestSupport;

namespace CharacterBosses.Core;

/// <summary>
/// Boss 出牌期间顶上去的「自动选择器」。
///
/// 为什么需要它：原版所有「让玩家选牌」的入口（<c>CardSelectCmd</c> 的
/// <c>FromHand</c> / <c>FromHandForDiscard</c> / <c>FromCombatPile</c> / <c>FromDeckForUpgrade</c> /
/// <c>FromChooseACardScreen</c> …）都长这样：
/// <code>if (Selector != null) 用选择器; else 弹 UI 等玩家点</code>
/// 我们给 Boss 用的是 <see cref="MegaCrit.Sts2.Core.GameActions.Multiplayer.BlockingPlayerChoiceContext"/>，
/// 它不会等玩家输入 —— 所以碰上「抽 3 弃 1」这类牌时，牌会卡在选择上把整回合挂死。
/// 推一个选择器进去，这些牌就会自动结算，既不弹玩家 UI 也不卡回合。
///
/// 安全性：<c>Selector</c> 在栈空时返回 null，所以只在 push 期间生效（我们用 using 保证一定弹出，
/// 否则 <c>CardSelectCmd.Reset</c> 会报 "leaked selector"）。
/// </summary>
public sealed class BossCardSelector : ICardSelector
{
    public static readonly BossCardSelector Instance = new();

    /// <summary>
    /// 剧本里明确写了「这张要被上一张牌丢掉」的牌（<see cref="ScriptCard.DiscardInstead"/>）。
    /// 自动选择器遇到「弃 N 张」时优先挑这些。
    ///
    /// ⚠ **必须按 (Boss, 回合号) 分开记**（用户 2026-10-29 实测：奇巧猎第 3 回合「杂技」把
    /// 「触不可及」丢了、连续反弹没被丢掉 ⇒ 奇巧没触发、那张牌没打出去）：
    /// 规划器在**她回合执行期间**还会为**下一回合**再算一次计划，而那一次会调用
    /// `ClearPreferDiscard()` ⇒ 把本回合刚点好的「点名要丢的牌」清空，
    /// 于是选牌退化成「按价值挑」，丢掉的是本来要打出去的牌。
    /// 现在每个回合各存一份：规划第 N+1 回合只会动第 N+1 回合那份，执行中的第 N 回合不受影响。
    /// </summary>
    private static readonly Dictionary<(Monsters.CharBossMonster Boss, int Turn), MarkSet> PreferByTurn = new();

    /// <summary>一份「点名要丢」的名单：实例集合 + 按「牌名+是否升级」的份数。</summary>
    private sealed class MarkSet
    {
        internal readonly HashSet<CardModel> Instances = new();
        internal readonly Dictionary<string, int> Keys = new();
    }

    /// <summary>正在出牌的那只 Boss 与它的回合号（由 <c>CharBossMonster.PlayOne</c> 设置）。</summary>
    internal static Monsters.CharBossMonster? ActingBoss;
    internal static int ActingTurn;

    /// <summary>取某一回合的名单（没有就 null）。</summary>
    private static MarkSet? BucketOf(Monsters.CharBossMonster? boss, int turn)
    {
        try
        {
            if (boss == null) return null;
            return PreferByTurn.TryGetValue((boss, turn), out var set) ? set : null;
        }
        catch { return null; }
    }

    private static bool IsEmpty(MarkSet? set)
        => set == null || (set.Instances.Count == 0 && set.Keys.Count == 0);

    /// <summary>这批候选项属于哪只 Boss（看卡的 owner；影子 Player 的 Creature 就是那只 Boss）。</summary>
    private static Monsters.CharBossMonster? BossOf(List<CardModel>? options)
    {
        try
        {
            if (options == null) return null;
            foreach (var c in options)
            {
                var boss = c?.Owner?.Creature?.Monster as Monsters.CharBossMonster;
                if (boss != null) return boss;
            }
        }
        catch { /* 忽略 */ }
        return null;
    }

    /// <summary>
    /// 取「**这只 Boss 这一回合**」的点名名单。
    ///
    /// ⚠ 必须按「候选项属于谁 + 它现在的 `TurnNumber`」来取，**不能**直接用
    /// `ActingBoss/ActingTurn`（用户 2026-10-29：涂毒猎第 3 回合又开始烧匕首雨）。
    /// 那两个静态字段是 `PlayOne` 出牌期间设的，一旦被「按剧本现算」顺手写死成上一回合，
    /// 下一次（下一个回合、手套消耗那一步）就会拿到**上一回合的旧名单**：
    /// 里面点名的牌（连续反弹/防御）在新手牌里根本不存在 ⇒ 命中 0 张 ⇒ 退化成兜底排序
    /// ⇒ 三张 1 费攻击牌里按牌名挑中 `DAGGER_SPRAY`。
    /// </summary>
    private static MarkSet? LiveMarks(Monsters.CharBossMonster? boss, int turn, List<CardModel> options)
    {
        // ① 出牌期间：`PlayOne` 记下的就是这只 Boss 的这一回合 → 用它（规划器刚点过的名单最准）
        if (boss != null && ReferenceEquals(ActingBoss, boss) && ActingTurn == turn)
        {
            var playing = BucketOf(boss, turn);
            if (!IsEmpty(playing)) return playing;
        }

        // ② 其它情况（手套消耗发生在规划之前 / 跨回合）→ 按剧本现算
        var derived = DeriveFromScript(boss, turn, options);
        if (!IsEmpty(derived)) return derived;

        // ③ 实在没有 → 空
        return BucketOf(boss, turn);
    }

    /// <summary>
    /// 名单为空时，**直接从剧本现算一份**（用户 2026-10-29：涂毒猎第 2 回合「生存者」丢了防御、
    /// 连续反弹没被打出）。
    ///
    /// 为什么需要这个兜底：烘焙手套（<c>ToastyMittens</c>）的「消耗 1 张手牌」发生在**她回合刚开始**，
    /// 很可能**早于**本回合的规划（规划在 `Plan` 里才 `MarkPreferDiscard`）⇒ 那一刻名单是空的，
    /// 选牌就退化成「奇巧优先 → 价值」：**连续反弹是奇巧牌**，于是被**消耗**掉了
    /// （奇巧优先本来是为了「丢弃后免费打出」，用在「消耗」上就变成白扔一张牌），
    /// 之后「生存者」只能去丢防御。
    ///
    /// 这里改为：只要名单为空，就按 (Boss 当前回合号, +1 回合) 的剧本，把
    /// `Burned` / `DiscardInstead` / 多人版标记点名的牌**就地补进名单**（只认候选项里真实存在的实例）。
    /// </summary>
    private static MarkSet? DeriveFromScript(Monsters.CharBossMonster? boss, int turn, List<CardModel> options)
    {
        try
        {
            if (options == null || options.Count == 0) return null;
            boss ??= BossOf(options);
            if (boss == null) return null;

            // ⚠ 返回**真正补到东西的那一回合**的名单（用户 2026-10-29：涂毒猎第 3 回合「匕首雨被手套烧了」
            //   —— 手套那一刻 `TurnNumber` 还是上一回合，于是按第 2 回合补不到、按第 3 回合补到了 打击，
            //   但旧代码却去取「第 2 回合那份（空）」返回 ⇒ 选牌照样退化成兜底排序，
            //   三张都是攻击牌、费用也一样 ⇒ 按牌名序挑中了 DAGGER_SPRAY）。
            for (int t = turn; t <= turn + 1; t++)
            {
                if (!SeededMark(boss, t, options)) continue;
                if (PreferByTurn.TryGetValue((boss, t), out var seeded) &&
                    (seeded.Instances.Count > 0 || seeded.Keys.Count > 0))
                {
                    return seeded;
                }
            }

            return PreferByTurn.TryGetValue((boss, turn), out var result) ? result : null;
        }
        catch { return null; }
    }

    /// <summary>把剧本第 <paramref name="turn"/> 回合点名（烧/丢）的牌补进该回合名单；返回是否补到了。</summary>
    private static bool SeededMark(Monsters.CharBossMonster boss, int turn, List<CardModel> options)
    {
        try
        {
            var scriptTurn = boss.Def?.ScriptForAct(boss.ActIndex)?.TurnAt(turn);
            if (scriptTurn?.Cards == null) return false;

            bool multi = MultiplayerCardSwap.IsMultiplayer(boss);
            var used = new HashSet<CardModel>();
            foreach (var entry in scriptTurn.Cards)
            {
                bool mark = entry.Burned || entry.DiscardInstead
                            || (entry.DiscardInsteadWhenMultiplayer && multi)
                            || (entry.DiscardInsteadSingleOnly && !multi);
                if (!mark) continue;

                foreach (var card in options)
                {
                    if (card == null || used.Contains(card)) continue;
                    if (card.GetType() != entry.Card) continue;
                    used.Add(card);
                    MarkPreferDiscard(boss, turn, card);
                    break;
                }
            }
            if (used.Count == 0) return false;

            // ⚠ **不要**在这里写 `ActingBoss/ActingTurn`（用户 2026-10-29：涂毒猎第 3 回合又开始烧匕首雨）。
            //   那两个字段是「正在出牌」的意思，只在 `PlayOne` 里设；在这里顺手写死成某一回合，
            //   下一个回合的手套消耗就会拿到上一回合的旧名单（点名全不命中 ⇒ 退化兜底 ⇒ 烧错牌）。

            // 诊断：以后再看「手套烧错牌」这类问题，一眼能知道是按哪一回合的剧本补的、点了谁
            try
            {
                if (_seededLogged.Add((boss, turn)))
                {
                    var names = new List<string>();
                    foreach (var c in used) names.Add(c.Id.Entry);
                    Logger.Log($"[CharBoss] 自动选择：点名名单为空 ⇒ 按剧本第 {turn} 回合现算" +
                               $"（点名 [{string.Join(",", names)}]；她当前 TurnNumber={boss.TurnNumber}）");
                }
            }
            catch { /* 忽略 */ }
            return true;
        }
        catch { return false; }
    }

    private static readonly HashSet<(Monsters.CharBossMonster Boss, int Turn)> _seededLogged = new();

    public static void MarkPreferDiscard(Monsters.CharBossMonster? boss, int turn, CardModel? card)
    {
        try
        {
            if (boss == null || card == null) return;
            if (!PreferByTurn.TryGetValue((boss, turn), out var set))
            {
                set = new MarkSet();
                PreferByTurn[(boss, turn)] = set;
            }
            set.Instances.Add(card);
            var key = KeyOf(card);
            if (key == null) return;
            set.Keys.TryGetValue(key, out int n);
            set.Keys[key] = n + 1;
        }
        catch { /* 忽略 */ }
    }

    /// <summary>清掉**这一回合**的点名（其它回合的名单不受影响，见字段注释）。</summary>
    public static void ClearPreferDiscard(Monsters.CharBossMonster? boss, int turn)
    {
        try
        {
            if (boss == null) return;
            PreferByTurn.Remove((boss, turn));
        }
        catch { /* 忽略 */ }
    }

    /// <summary>「牌名 + 是否升级」—— 同名的升级版与非升级版算两种。</summary>
    private static string? KeyOf(CardModel? card)
    {
        if (card == null) return null;
        try { return card.Id.Entry + (card.IsUpgraded ? "+" : string.Empty); }
        catch { return null; }
    }

    /// <summary>在 <c>using</c> 里推入自动选择器；作用域结束自动弹出。</summary>
    public static IDisposable Push() => CardSelectCmd.PushSelector(Instance);

    /// <summary>
    /// 剧本「**选择队列**」的游标：每个 (Boss, 回合号) 一份，按顺序消费
    /// <see cref="BossTurn.PickOrder"/> 里的条目。
    ///
    /// 为什么按 (Boss, 回合号) 而不是「每次出牌从头数」：回合号是一直往上加的，
    /// 每个回合只发生一次，所以（Boss, 回合号）天然唯一 —— 不用额外的重置时机会，
    /// 也就不会出现「规划第 N+1 回合顺手把第 N 回合的队列清掉」这类问题
    /// （`PreferByTurn` 踩过同样的坑，见它字段上的注释）。
    /// </summary>
    private static readonly Dictionary<(Monsters.CharBossMonster Boss, int Turn), int> PickCursor = new();

    /// <summary>
    /// 取这一次选牌应该被「剧本点名」挑中的那张牌（没有点名 / 候选项里没有 → null，走兜底逻辑）。
    /// 命中后游标 +1；没命中**不动**游标（这样等真正该选的那次到来时仍能对上）。
    /// </summary>
    private static CardModel? TakeForcedPick(Monsters.CharBossMonster? boss, int turn, List<CardModel> options)
    {
        try
        {
            if (boss == null || options == null || options.Count == 0) return null;

            var order = boss.Def?.ScriptForAct(boss.ActIndex)?.TurnAt(turn)?.PickOrder;
            if (order == null || order.Count == 0) return null;

            var key = (boss, turn);
            PickCursor.TryGetValue(key, out int cursor);
            if (cursor < 0 || cursor >= order.Count) return null;

            var wanted = order[cursor];
            if (wanted == null) return null;

            // 按「模型 id 的 entry」比：升级版与未升级版的 entry 相同，所以 `+` 也算命中。
            string? wantedEntry = null;
            try { wantedEntry = ModelDb.GetId(wanted)?.Entry; } catch { wantedEntry = null; }

            foreach (var card in options)
            {
                if (card == null) continue;
                bool hit;
                try
                {
                    hit = card.GetType() == wanted
                          || (wantedEntry != null && string.Equals(card.Id.Entry, wantedEntry, StringComparison.Ordinal));
                }
                catch { hit = false; }
                if (!hit) continue;

                if (PickCursor.Count > 256) PickCursor.Clear();
                PickCursor[key] = cursor + 1;
                return card;
            }
            return null;
        }
        catch { return null; }
    }

    /// <summary>
    /// 选择策略：只选「必须选的最少张数」。分三段，越前面越优先：
    ///   ① 剧本点名、**实例还在手里**（规划期那份没被换掉）；
    ///   ② 剧本点名、**实例换了**（手牌被 <c>BuildHand</c> 重建过）→ 按「牌名 + 是否升级」的配额认；
    ///   ③ 兜底：奇巧（丢掉＝白打一次）→ 价值从低到高 → 费用从低到高。
    ///
    /// 为什么②必须有（用户 2026-10-29：第 11 回合「隐秘匕首」丢掉了**中和**）：
    /// 剧本写的是「隐秘匕首（丢打击 ×2）」，但规划期的牌实例到她回合开始时已经被重建，
    /// ① 一个都命中不了，于是全靠③「价值→费用」：中和是 0 费、打击是 1 费 ⇒ 中和先被扔。
    /// 现在②会按牌名把两张打击认出来（奇巧猎第 2/5/6/7/9/11 回合都会踩这个）。
    /// </summary>
    public Task<IEnumerable<CardModel>> GetSelectedCards(IEnumerable<CardModel> options, int minSelect, int maxSelect)
    {
        var list = options?.ToList() ?? new List<CardModel>();
        int count = Math.Clamp(minSelect, 0, list.Count);

        var picked = new List<CardModel>();
        var rest = new List<CardModel>(list);

        // 点名名单：按「**候选项属于哪只 Boss + 它现在的回合号**」取（见 LiveMarks 的注释：
        // 用 `ActingBoss/ActingTurn` 直接取会拿到上一回合的旧名单）。
        var bossNow = BossOf(list);
        int turnNow = bossNow?.TurnNumber ?? ActingTurn;
        var marks = LiveMarks(bossNow, turnNow, list);
        int turnTag = turnNow;

        // 兜底排序要知道「这一回合剧本要打哪些牌」（`Order()` 里用），这里显式传下去
        _orderBoss = bossNow;
        _orderTurn = turnNow;

        // ⓪ 剧本点名的「选择队列」（`BossTurn.PickOrder`）—— **只在「这一次选择没有弃牌点名」时才用**。
        //    ★ v0.2.522（用户 2026-11-09：「7、10 回合手上技法错误的给隐秘匕首添加了奇巧」）：
        //      以前它无条件最先消耗，于是**杂技的「弃 1 张」也会把队列里的第一条吃掉**
        //      （华丽猎第 7/10 回合队列 = 防御，本该留给「手上技法」加奇巧）⇒ 手上技法退化成兜底排序、
        //      把奇巧给了隐秘匕首。现在带弃牌点名的选择交给下面的 ①/② 精确命中，队列留给真正需要点名的选择。
        bool hasDiscardMarks = marks != null && (marks.Instances.Count > 0 || marks.Keys.Count > 0);
        if (count > 0 && !hasDiscardMarks)
        {
            var forced = TakeForcedPick(bossNow, turnNow, rest);
            if (forced != null)
            {
                picked.Add(forced);
                rest.Remove(forced);
                string forcedEntry;
                try { forcedEntry = forced.Id.Entry; } catch { forcedEntry = "?"; }
                Logger.Log($"[CharBoss] 自动选择：按剧本**点名**挑中 {forcedEntry}（第 {turnNow} 回合的选择队列）");
            }
        }

        // ① 实例精确命中
        for (int i = rest.Count - 1; i >= 0 && picked.Count < count; i--)
        {
            if (marks == null || !marks.Instances.Contains(rest[i])) continue;
            picked.Add(rest[i]);
            rest.RemoveAt(i);
        }
        int byInstance = picked.Count;

        // ② 同名配额（实例被换掉时走这条）
        int byKey = 0;
        if (picked.Count < count && marks != null && marks.Keys.Count > 0)
        {
            var budget = new Dictionary<string, int>(marks.Keys);
            foreach (var c in picked)      // ① 已经拿走的要先从配额里扣掉
            {
                var k = KeyOf(c);
                if (k != null && budget.TryGetValue(k, out int n) && n > 0) budget[k] = n - 1;
            }
            foreach (var c in Order(rest))
            {
                if (picked.Count >= count) break;
                var k = KeyOf(c);
                if (k == null || !budget.TryGetValue(k, out int n) || n <= 0) continue;
                budget[k] = n - 1;
                picked.Add(c);
                byKey++;
            }
            if (byKey > 0) rest = rest.Where(c => !picked.Contains(c)).ToList();
        }


        // ③ 兜底之前**再用一次剧本点名**（万一前面几段把游标挡住过）：这样「手上技法给防御加奇巧」
        //    这类点名一定落在那张指定牌上，而不是按牌名首字母挑到「杂技」。
        if (picked.Count < count)
        {
            var forced2 = TakeForcedPick(bossNow, turnNow, rest);
            if (forced2 != null)
            {
                picked.Add(forced2);
                rest.Remove(forced2);
                string n2; try { n2 = forced2.Id.Entry; } catch { n2 = "?"; }
                Logger.Log($"[CharBoss] 自动选择：兜底阶段按剧本**点名**挑中 {n2}（第 {turnNow} 回合的选择队列）");
            }
        }

        // ③ 兜底：奇巧优先 → 价值 → 费用
        if (picked.Count < count)
        {
            foreach (var c in Order(rest))
            {
                if (picked.Count >= count) break;
                picked.Add(c);
            }
        }

        Logger.Log($"[CharBoss] 自动选择: 候选 {list.Count} 个（min={minSelect} max={maxSelect}）" +
                   $"→ 选 [{string.Join(",", picked.Select(c => c.Id.Entry))}]" +
                   $"（第 {turnTag} 回合的点名：{byInstance} 张按实例 + {byKey} 张按牌名 + " +
                   $"其余 {picked.Count - byInstance - byKey} 张兜底；名单 {marks?.Instances.Count ?? 0} 条）");
        return Task.FromResult<IEnumerable<CardModel>>(picked);
    }

    /// <summary>
    /// 兜底排序，从最该被丢掉到最不该：
    ///   ① **剧本这一回合要打出去的牌，一律排到最后**（宁可扔别的，也不要扔掉剧本要打的）；
    ///   ② 奇巧（丢掉＝白打一次）；
    ///   ③ 价值档位 → ④ 费用 → ⑤ 牌名（保证确定性）。
    ///
    /// ① 是用户 2026-10-29「第三回合涂毒猎不打匕首雨（被手套烧了）」之后的保险：
    /// 手套的「消耗」发生在本回合规划之前，那时根本没有点名名单；万一剧本的 `Burned` 也没匹配上，
    /// 兜底排序就会在「三张一模一样的攻击牌」里按牌名挑 —— 匕首雨（DAGGER_SPRAY）排第一，被烧掉。
    /// 现在把「剧本要打的牌」压到最后，就算点名失败也不会烧掉关键牌。
    /// </summary>
    /// <summary>兜底排序时「这一回合」的上下文（由 <c>GetSelectedCards</c> 每次设定）。</summary>
    private static Monsters.CharBossMonster? _orderBoss;
    private static int _orderTurn;

    private static IEnumerable<CardModel> Order(IEnumerable<CardModel> cards)
    {
        var playTypes = PlayTypesFor(_orderBoss, _orderTurn);
        return cards.OrderBy(c => (playTypes != null && playTypes.Contains(c.GetType())) ? 1 : 0)
                    .ThenBy(c => IsSly(c) ? 0 : 1)
                    .ThenBy(ValueRank)
                    .ThenBy(CostOf)
                    .ThenBy(c => { try { return c.Id.Entry; } catch { return string.Empty; } },
                            StringComparer.Ordinal);
    }

    /// <summary>「剧本第 turn 回合**打算打出去**的牌」（按类型；烧/弃/仅显示/保留在手的不算）。</summary>
    private static readonly Dictionary<(Monsters.CharBossMonster Boss, int Turn), HashSet<Type>> PlayTypeCache = new();

    private static HashSet<Type>? PlayTypesFor(Monsters.CharBossMonster? boss, int turn)
    {
        try
        {
            if (boss == null) return null;
            var key = (boss, turn);
            if (PlayTypeCache.TryGetValue(key, out var cached)) return cached;

            var set = new HashSet<Type>();
            var scriptTurn = boss.Def?.ScriptForAct(boss.ActIndex)?.TurnAt(turn);
            if (scriptTurn?.Cards != null)
            {
                bool multi = MultiplayerCardSwap.IsMultiplayer(boss);
                foreach (var entry in scriptTurn.Cards)
                {
                    if (entry.Burned || entry.DisplayOnly || entry.HoldInstead) continue;
                    if (entry.DiscardInstead) continue;
                    if (entry.DiscardInsteadWhenMultiplayer && multi) continue;
                    if (entry.DiscardInsteadSingleOnly && !multi) continue;
                    set.Add(entry.Card);
                }
            }
            if (PlayTypeCache.Count > 256) PlayTypeCache.Clear();
            PlayTypeCache[key] = set;
            return set;
        }
        catch { return null; }
    }

    /// <summary>
    /// 这张牌有没有「**奇巧**」（`CardKeyword.Sly`）—— 被丢弃时会**自动打出**。
    ///
    /// 为什么必须先挑它（用户 2026-10-29 四人局实测）：奇巧猎第 2 回合剧本是
    /// 「早有准备，丢掉鬼祟 + 翻越撑击（两张都有奇巧，都会自动打出）」，
    /// 但自动选择器按「价值从低到高」挑，把两张 0 费防御挑走了 ⇒ **翻越撑击留在手里没被丢**，
    /// 鬼祟自动打出了、翻越撑击没有。丢弃奇巧牌等于白赚一张牌，永远优先丢它。
    /// `IsSlyThisTurn` 覆盖「手上技法 / 谋划专家」临时给的奇巧。
    /// </summary>
    private static bool IsSly(CardModel card)
    {
        try
        {
            if (card.IsSlyThisTurn) return true;
        }
        catch { /* 继续看静态关键字 */ }
        try
        {
            return card.Keywords != null && card.Keywords.Contains(CardKeyword.Sly);
        }
        catch { return false; }
    }

    /// <summary>价值档位（越小越先被舍弃）。</summary>
    private static int ValueRank(CardModel card)
    {
        try
        {
            if (card.Type is CardType.Status or CardType.Curse) return 0;   // 状态/诅咒：先扔
            if (BossDamagePredictor.DealsDamage(card)) return 2;            // 有伤害：留到最后
            return 1;                                                      // 纯技能（防御等）
        }
        catch { return 1; }
    }

    /// <summary>Boss 正常不会走到卡牌奖励；真走到了就返回空选择，别把流程弄崩。</summary>
    public CardRewardSelection GetSelectedCardReward(
        IReadOnlyList<CardCreationResult> options,
        IReadOnlyList<CardRewardAlternative> alternatives)
    {
        Logger.Log($"[CharBoss] 自动选择(卡牌奖励): 候选 {options?.Count ?? 0} 个 → 不选");
        return default;
    }

    private static int CostOf(CardModel card)
    {
        try { return Math.Max(0, card.EnergyCost.GetAmountToSpend()); }
        catch { return 0; }
    }
}
