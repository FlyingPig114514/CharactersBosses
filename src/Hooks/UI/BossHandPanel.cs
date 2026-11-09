using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using CharacterBosses.Monsters;

namespace CharacterBosses.UI;

/// <summary>
/// Boss 头顶的手牌栏。
/// 排列顺序：本回合要打出的牌在左（按出牌顺序），不打的牌压暗并排在右侧；
/// 每张要打的牌下方挂一排意图（一张牌可能有多个效果）。
/// 位置跟着 Boss 立绘的包围盒走（见 <see cref="BossHandAnchor"/>），单张卡面见 <see cref="BossHandCard"/>，
/// 竖排的具体数字在 <c>BossHandPanel.Layout.cs</c> 里。
/// </summary>
public partial class BossHandPanel : Control
{
    /// <summary>一个格子 = 一张牌 + 它的节点，记住牌是为了出牌时能只摘掉那一格。</summary>
    private sealed class Cell
    {
        public required Control Node { get; init; }
        public required CardModel Card { get; init; }
        public required bool Planned { get; init; }

        /// <summary>
        /// 这一格画成亮的还是暗的。**不等于 <see cref="Planned"/>**：
        /// 所向无敌是「展示牌 + 回合结束免费打出」——不在 Plan 里，但该亮
        /// （见 <see cref="CharBossMonster.AutoPlayedCards"/>，用户实测要求）。
        /// </summary>
        public required bool Bright { get; set; }
    }

    private readonly List<Cell> _cells = new();
    private CharBossMonster? _boss;
    /// <summary>回退开关：false = 回到「意图变化就整排重建」的旧行为。</summary>
    internal static bool UseInPlaceIntentRefresh = true;

    private Control? _statusRow;
    private bool _layoutDirty;
    private int _tickCount;
    private int _tickLogs;

    /// <summary>特殊效果说明框的确认间隔（10Hz；它是静态的、不跟手，不需要每帧）。</summary>
    private const ulong SpecialUiIntervalUsec = 100_000;
    private ulong _lastSpecialUiUsec;

    /// <summary>
    /// 本回合手牌的**最大张数**，用来固定整块宽度。
    /// 出牌只会让格子变少；如果宽度跟着缩，整块会重新居中、状态条（遗物/能量）就会往左滑
    /// ——用户实测要求「不随打出卡牌向左移动」。
    /// </summary>
    private int _turnCards;
    /// <summary>本场战斗见过的最大手牌数：整块宽度按它算，出牌/多抽牌都不再变宽变窄（用户 2026-11-08 要求）。</summary>
    private int _widthCards;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;
        SetProcess(true);

        // ⚠ **绝对不要给这个面板（或它的意图行）抬 `ZIndex`**。
        // 2026-10-29 连试两版（面板 200 / 面板 10 / 意图行挪到面板下再给 2）都不行：
        // 抬起来之后切到**地图、设置、图鉴**时，本模组的卡面/意图会浮在那些界面之上。
        // 同一个教训在 `BossStatusRow.Icons.cs` 里早就记过（「不抬 ZIndex，否则切地图/设置时
        // 本 mod 的节点会浮在游戏界面之上」）。用户最终口径：**回退到改图层之前**，宁可被卡面略微遮挡。

        // 本项目的 mod 程序集里 _Process 不一定被调用（踩过），所以额外挂场景树的帧信号；
        // 两个都开，谁生效都行，OnFrame 本身是幂等的。
        var tree = GetTree();
        if (tree != null) tree.ProcessFrame += OnFrame;
    }



    public override void _ExitTree()
    {
        var tree = GetTree();
        if (tree != null) tree.ProcessFrame -= OnFrame;
    }

    public override void _Process(double delta) => OnFrame();

    /// <summary>
    /// 外部每帧驱动入口。**实战走这条**（<c>BossFrameTick</c> 由 Harmony 挂在原版
    /// <c>NStarCounter/NEnergyCounter._Process</c> 上调用）；上面的 <c>_Process</c> 重载
    /// 实测在 mod 程序集里不会被调到，留着只是无害的兜底。
    /// </summary>
    public void RunFrame() => OnFrame();

    /// <summary>每帧：内容变了才重排；否则只让整块跟着 Boss 走（模组位移 Boss 时卡牌/遗物也要跟着动）。</summary>
    private void OnFrame()
    {
        try
        {
            // 战斗不在进行中（地图界面 / 结算界面 / 已经离场）⇒ **立刻隐藏整块**。
            // 用户 2026-10-29：「意图的显示层级似乎太高了，切到地图界面都会显示」——
            // 光靠图层值治不了这个，必须在「不在战斗里」时把面板本身藏掉（连带意图一起）。
            try
            {
                bool live = _boss?.CombatState?.IsLiveCombat() == true;
                if (!live)
                {
                    if (Visible) Visible = false;
                    return;
                }
                if (!Visible) Visible = true;
            }
            catch { /* 读不到就按原来的流程走 */ }

            if (_layoutDirty) LayoutCells();
            else FollowBoss();

            // 球位管理器只改位置：每帧贴着 Boss 头顶（槽位/动画/数字仍由原版负责）
            BossOrbDisplay.Follow(_boss);

            // 右侧「特殊效果」说明框：确认它还在（打开菜单时会被原版收掉，
            // 回到战斗界面后必须自己长回来 —— 用户实测以前回不来）。
            //
            // 性能：这条原本**每帧**都跑（它要数敌人、找 Power、Place、IsVisibleInTree），
            // 两套面板同场时还要 ×2。说明框是静态的、也不跟手，降到 10Hz 完全看不出差别。
            if (_boss != null)
            {
                var now = Time.GetTicksUsec();
                if (now - _lastSpecialUiUsec >= SpecialUiIntervalUsec)
                {
                    _lastSpecialUiUsec = now;
                    BossSpecialUi.Ensure(_boss);
                }
            }

            // 君王之剑的朝向 / 随从的朝向站位**不在这里**：实测这个 OnFrame 根本不会被调用
            // （见 BossFrameTickPatch 的说明），它们已经搬到 BossFrameTick.Tick() 里，
            // 由 Harmony 挂在原版每帧方法上驱动。这里只留给「面板自己」的跟随逻辑。

            // 前 3 秒每秒打一条心跳，用来确认「帧回调到底有没有跑」
            if (_tickLogs < 3 && ++_tickCount % 60 == 0)
            {
                _tickLogs++;
                Logger.Log($"[CharBoss] 帧回调 ok #{_tickLogs} pos={Position} visible={IsVisibleInTree()}");
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] hand ui frame failed: " + e.Message);
        }
    }

    /// <summary>
    /// 按当前手牌 + 计划同步手牌栏。
    /// 出牌过程中手牌只是**变少**，所以这里只摘掉打出去的那一格、其余节点原样保留 ——
    /// 早先的实现每次都整排重建（每打一张就重建 5 个 NCard 节点），视觉会闪、对象池被反复取用。
    /// 手牌/计划整体换了（新回合）或顺序对不上时自动退回整排重建。
    /// </summary>
    public void Rebuild(CharBossMonster boss, IReadOnlyList<CardModel> hand)
    {
        _boss = boss;

        // 意图数字是**会变的**：力量/减力量/易伤每帧都可能不同，而「根除」这类 X 费多段意图
        // 用的是会实时重算的 <see cref="Core.LiveMultiAttackIntent"/>。
        // 增量同步只摘格子、**不重画已有格子里的意图节点** ⇒ 数字变了却还挂着旧值
        //（用户 2026-10-23：玩家用崩坠「哀嚎」减了她 11 点力量，意图一直显示 10×4，应 0×4）。
        // 所以这里每帧比一次「渲染签名」，变了就整排重建一次。

        // 她这回合被**击晕**（吹哨等）⇒ 一张牌都不会打：全部压暗、意图一行都不画（用户 2026-11-04）。
        // 只读一个 bool，每次重建时顺便取一次，不额外开销。
        bool stunned = boss.Stunned;

        var signature = IntentSignature(boss);
        if (!string.Equals(signature, _intentSignature, StringComparison.Ordinal))
        {
            _intentSignature = signature;
            // **不再整排清空**（那会让每张卡都闪一下、并反复取用对象池）：
            // 只把每一格的「意图行」原地换掉，卡面节点保留（用户 2026-10-26 要求）。
            // 回退开关：把 UseInPlaceIntentRefresh 改成 false 就回到旧的整排重建。

            if (UseInPlaceIntentRefresh && _cells.Count > 0)
            {
                foreach (var cell in _cells)
                {
                    // 被击晕时 planned 一律当 null ⇒ 原地刷新也会把那一格的意图行清掉（不然会残留）
                    var planned = stunned ? null : (boss.Plan.FirstOrDefault(p => ReferenceEquals(p.Card, cell.Card))
                                                   ?? boss.DisplayPlays.FirstOrDefault(p => ReferenceEquals(p.Card, cell.Card)));
                    BossHandCard.RefreshIntents(cell.Node, boss, planned);
                    cell.Bright = planned != null;
                }
            }
            else
            {
                ClearCells();
            }
        }

        var wanted = WantedCards(boss, hand);

        if (!CanSyncWith(wanted))
        {
            ClearCells();
            foreach (var card in wanted)
            {
                _cells.Add(BuildCell(boss, card));
            }
            // 新回合/新计划：以这一次的手牌数作为整块宽度的基准
            _turnCards = wanted.Count;
            // ★ v0.2.521（用户 2026-11-09：「第四回合她的卡牌偏移了（太往左了）」）：
            //   以前 _widthCards 是「本场见过的最多张数」，只涨不跌 —— 第 4 回合只有 3 张牌时
            //   面板仍按 5 张的宽度排版，牌就整排偏到左边。现在**每回合重置**：
            //   同一回合内出牌不会变窄（用户当初的要求），跨回合则跟着新手牌数走。
            _widthCards = wanted.Count;
        }
        else
        {
            // 手牌只会变少（出牌），摘掉打出去的那几格即可，其余节点原样保留
            for (int i = _cells.Count - 1; i >= 0; i--)
            {
                if (wanted.Any(c => ReferenceEquals(c, _cells[i].Card))) continue;

                var cell = _cells[i];
                if (IsInstanceValid(cell.Node))
                {
                    // 这一回合**真的要打**的牌 ⇒ 给它一段「飞向目标」的出牌动画（用户 2026-11-08 要求）；
                    // 其它情况（回合结束弃牌、被击晕后清空）仍然直接消失。
                    // 注意：Fly 会接管这一格的释放，这里不能先 ReleaseCell/QueueFree。
                    var planned = stunned
                        ? null
                        : (boss.Plan.FirstOrDefault(p => ReferenceEquals(p.Card, cell.Card))
                           ?? boss.DisplayPlays.FirstOrDefault(p => ReferenceEquals(p.Card, cell.Card)));

                    if (planned != null)
                    {
                        BossHandPlayFx.Fly(cell.Node, boss, planned.Target);
                    }
                    else
                    {
                        BossHandCard.ReleaseCell(cell.Node);
                        cell.Node.QueueFree();
                    }
                }
                _cells.RemoveAt(i);
            }
        }

        // 状态条（遗物 / 药水 / 能量 / 辉星）：内容指纹变了才真正重建（见 BossStatusRow.Fill）
        var swDbg = System.Diagnostics.Stopwatch.StartNew();
        _statusRow = BossStatusRow.Ensure(this, _statusRow);
        BossStatusRow.Fill(_statusRow, boss);

        // 不属于某张牌的独立意图（冰雹风暴）——要排在状态条之后，才能对齐到能量球下方
        BuildExtraIntents(boss);

        // 诊断：「手里有、但这一回合不会出」的牌列出来（用户 2026-10-26 问「多了张不出的扫腿」）
        try { LogUnplayed(boss, wanted); } catch { /* 忽略 */ }

        _layoutDirty = true;
        LayoutCells();

        // 诊断：「面板重建一次到底花多久」—— 汇总进 PerfLog，由每帧驱动每 5 秒打一条
        // （原来这里每 60 次打一条，而真正的大头 LayoutCells/建节点 其实没被单独计时）
        try { Core.PerfLog.NoteRebuild(swDbg.Elapsed.TotalMilliseconds); }
        catch { /* 忽略 */ }
    }

    /// <summary>
    /// 这张牌**现在真的打出去了** ⇒ 把对应格子从手牌栏里摘出来，播一段「飞向目标」的动画。
    ///
    /// 为什么不在 <see cref="Rebuild"/> 里顺手做（用户 2026-11-08：卡牌没有打出动画）：
    /// 出牌循环结束时 `Plan` 往往会先被清掉，等 Rebuild 发现「手牌少了一张」时已经拿不到
    /// 这张牌的目标了；所以由出牌循环在出手那一刻直接调用这里（详见 CharBossMonster 的播放循环）。
    /// </summary>
    public void PlayCardFx(CardModel card, Creature? target)
    {
        try
        {
            for (int i = _cells.Count - 1; i >= 0; i--)
            {
                if (!ReferenceEquals(_cells[i].Card, card)) continue;

                var cell = _cells[i];
                _cells.RemoveAt(i);
                if (IsInstanceValid(cell.Node) && _boss != null)
                {
                    BossHandPlayFx.Fly(cell.Node, _boss, target);
                }
                break;
            }

            _layoutDirty = true;
            LayoutCells();
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] play card fx failed: " + e.Message);
        }
    }

    public void Reset()
    {
        ClearCells();

        if (_extraIntents != null && IsInstanceValid(_extraIntents))
        {
            _extraIntents.QueueFree();
        }
        _extraIntents = null;
        _layoutDirty = false;
        _unplayedSignature = string.Empty;
    }

    /// <summary>
    /// 诊断：把「手牌里有、但这一回合不会打出去」的牌打出来（每次内容变化打一条）。
    ///
    /// 为什么需要（用户 2026-10-26：「第一回合出牌后为什么多了张不出的扫腿？」）：
    /// 她的**计划是在玩家回合里就排好的**（这样你才能提前看到意图），而
    /// 「明晰提取物」这类药水是**她回合开始喝下去才抽牌**的 ⇒ 抽进来的那张牌天然不在计划里，
    /// 加上这回合的费已按剧本花完（`余费=0`），它就只能压暗挂着、这回合不出。
    /// 这一行会把「全部手牌 / 会打的 / 不打的」一次列清，下次再有这种疑问直接看日志。
    /// </summary>
    private void LogUnplayed(CharBossMonster boss, List<CardModel> wanted)
    {
        var all = new List<string>();
        var skip = new List<string>();
        foreach (var card in wanted)
        {
            string name = SafeEntry(card);
            all.Add(name);
            if (!IsBright(card)) skip.Add(name);
        }

        // 内容没变就不重复刷屏（计划/手牌任一变化都算变化）
        string signature = string.Join(",", all) + "|" + string.Join(",", skip) + "|" + boss.Plan.Count;
        if (signature == _unplayedSignature) return;
        _unplayedSignature = signature;

        if (skip.Count == 0)
        {
            Logger.Log($"[CharBoss] 手牌栏：{all.Count} 张全部会打 [{string.Join(",", all)}]");
            return;
        }
        Logger.Log($"[CharBoss] 手牌栏：{all.Count} 张里有 {skip.Count} 张本回合不会打 " +
                   $"[{string.Join(",", skip)}]（全部=[{string.Join(",", all)}]，计划 {boss.Plan.Count} 张；" +
                   $"剧本没写 / 费不够 / 喝药抽进来的都算）");
    }

    /// <summary>上一次打过的「不打清单」签名（变了才打日志）。</summary>
    private string _unplayedSignature = string.Empty;

    /// <summary>牌名（取不到就占位）。</summary>
    private static string SafeEntry(CardModel? card)
    {
        try { return card?.Id.Entry ?? "?"; }
        catch { return "?"; }
    }

    /// <summary>
    /// 强制下一次 <see cref="Rebuild"/> 走「整排重建」。
    ///
    /// 为什么需要：<see cref="Rebuild"/> 平时只做增量同步（摘掉打出去的格子、其余节点原样保留），
    /// **不会重画已有格子里的意图节点**。而我们会在她回合开始/每打完一张牌时重算
    /// <c>PlannedPlay.Intents</c>（撤掉"提前补的力量"）—— 不强制重建的话，面板上留着的
    /// 仍是规划期那套数字（用户实测：涂毒猎回合开始拿到力量后，修正没有减回去）。
    /// </summary>
    public void Invalidate() => ClearCells();

    /// <summary>上一次画出来的「意图数字」签名（变了就整排重建，见 <see cref="Rebuild"/>）。</summary>
    private string _intentSignature = string.Empty;

    /// <summary>
    /// 把当前计划里每个意图的**数字**拼成一个字符串。
    ///
    /// 只取数字（不含牌名/图标），所以「她力量变了」「玩家给她上了减力量」「易伤变了」
    /// 这类真正会改数字的变化才会触发重建；其余情况一律走增量同步，不闪。
    /// 走 <c>GetTotalDamage</c> / <c>Repeats</c>：对 <see cref="Core.LiveMultiAttackIntent"/>
    /// 来说这两个就是**实时重算**入口（同一帧内它自己会缓存）。
    /// </summary>
    private static string IntentSignature(CharBossMonster boss)
    {
        try
        {
            var plan = boss?.Plan;
            var owner = boss?.Creature;
            if (plan == null || plan.Count == 0 || owner == null) return string.Empty;

            var sb = new System.Text.StringBuilder();
            foreach (var play in plan)
            {
                if (play?.Intents == null || play.Intents.Count == 0) continue;
                var targets = new List<MegaCrit.Sts2.Core.Entities.Creatures.Creature>();
                if (play.Target != null) targets.Add(play.Target);

                foreach (var intent in play.Intents)
                {
                    if (intent == null) continue;
                    switch (intent)
                    {
                        case MegaCrit.Sts2.Core.MonsterMoves.Intents.MultiAttackIntent multi:
                            sb.Append(multi.GetTotalDamage(targets, owner)).Append('x').Append(multi.Repeats);
                            break;
                        case MegaCrit.Sts2.Core.MonsterMoves.Intents.SingleAttackIntent single:
                            sb.Append(single.GetTotalDamage(targets, owner));
                            break;
                        default:
                            sb.Append(intent.IntentType);
                            break;
                    }
                    sb.Append(',');
                }
                sb.Append(';');
            }
            return sb.ToString();
        }
        catch { return string.Empty; }
    }

    /// <summary>
    /// 能不能增量同步：目标列表必须是「现有格子按同一顺序过滤掉打出去的牌」的结果
    /// （即子序列，且没有新增牌），并且剩下格子的高亮/压暗状态没变。
    /// 注意不能写成「前缀比对」—— 打掉第 1 张后现有格子是 5 个、目标是 4 个，前缀比对会直接失败，
    /// 优化就白做了（踩过）。
    /// </summary>
    private bool CanSyncWith(List<CardModel> wanted)
    {
        var present = _cells.Where(cell => wanted.Any(c => ReferenceEquals(c, cell.Card))).ToList();
        if (present.Count != wanted.Count) return false;

        for (int i = 0; i < wanted.Count; i++)
        {
            if (!ReferenceEquals(present[i].Card, wanted[i])) return false;
        }

        foreach (var cell in present)
        {
            if (cell.Bright != IsBright(cell.Card)) return false;
        }
        return true;
    }

    /// <summary>按当前计划，这张牌该画亮吗（在 Plan 里 → 亮；或本回合会自动打出的展示牌 → 亮）。</summary>
    private bool IsBright(CardModel card)
    {
        if (_boss == null) return false;
        // 被击晕 ⇒ 一张都不会打，全部画暗（用户 2026-11-04）。
        // 注意这里同时是「要不要整排重建」的判据（见 CanSyncWith）：只要它翻转，下一次 Rebuild
        // 就会自动走整排重建，不需要额外的失效标记。
        if (_boss.Stunned) return false;
        return IsPlanned(card) || IsAutoPlayed(_boss, card);
    }

    private Cell BuildCell(CharBossMonster boss, CardModel card)
    {
        var planned = boss.Plan.FirstOrDefault(p => ReferenceEquals(p.Card, card));
        // 展示牌（所向无敌）：不在 Plan 里，但「会打出去」并且要画它下面的意图
        var display = boss.DisplayPlays.FirstOrDefault(p => ReferenceEquals(p.Card, card));
        // 被击晕：既压暗也不挂意图行（`BuildCell` 的 planned 为 null ⇒ 不建意图行；bright=false ⇒ 压暗）
        var showAs = boss.Stunned ? null : (planned ?? display);
        return new Cell
        {
            Node = BossHandCard.BuildCell(this, boss, card, showAs, showAs != null),
            Card = card,
            Planned = planned != null,
            Bright = showAs != null,
        };
    }

    private bool IsPlanned(CardModel card)
        => _boss != null && _boss.Plan.Any(p => ReferenceEquals(p.Card, card));

    /// <summary>这张牌是不是「不在 Plan 里、但本回合会被打出去」的展示牌（所向无敌）。</summary>
    private static bool IsAutoPlayed(CharBossMonster boss, CardModel card)
    {
        foreach (var p in boss.DisplayPlays)
        {
            if (ReferenceEquals(p.Card, card)) return true;
        }
        return false;
    }

    /// <summary>
    /// 要摆上牌桌的卡＝真实手牌，按计划顺序排列：
    /// 本回合会打的牌在左、亮着；不打的牌落在右侧压暗。
    /// （小刀只在她手牌里真的有时才出现 —— 生成刀牌打出后刀才进手牌，塔 1 就是这个观感）
    /// </summary>
    private List<CardModel> WantedCards(CharBossMonster boss, IReadOnlyList<CardModel> hand)
    {
        return hand.OrderBy(PlannedOrder).ToList();
    }

    private int PlannedOrder(CardModel card)
    {
        if (_boss == null) return 1000;
        for (int i = 0; i < _boss.Plan.Count; i++)
        {
            if (ReferenceEquals(_boss.Plan[i].Card, card)) return i;
        }
        return 1000;
    }

    private void ClearCells()
    {
        foreach (var cell in _cells)
        {

            if (IsInstanceValid(cell.Node))
            {
                BossHandCard.ReleaseCell(cell.Node);
                cell.Node.QueueFree();
            }
        }
        _cells.Clear();
    }
}
