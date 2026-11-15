using System;
using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Nodes.Cards;

namespace CharacterBosses.UI;

/// <summary>
/// <see cref="BossHandPanel"/> 的排版部分（拆出来纯粹是为了单文件 200 行以内）。
///
/// 竖排顺序**自上而下**固定为：状态条（遗物 + 能量）→ 卡面 → 意图行，整块贴着 Boss 立绘上方居中。
/// 每行位置都是显式算出来的，不再是「卡面整体下移多少卡宽」那种相对量 ——
/// 所以改 <see cref="BossHandCard.CardScale"/> 或任一行高度都不会互相压住。
///
/// 分两档更新：
///   · <see cref="LayoutCells"/> —— 内容变了才跑（换手牌、遗物变化、塔顶尺寸变化）；
///   · <see cref="FollowBoss"/> —— **每帧**只重算整块的位置，让手牌栏跟着 Boss 走
///     （Boss 被位移/换位时，卡牌/意图/遗物/能量必须一起动，否则只有挂在生物节点下的球位会跟着动）。
/// </summary>
public partial class BossHandPanel
{
    private const float Gap = 8f;                   // 牌间距
    private const float GapAboveBoss = 28f;         // 意图行底边与 Boss 立绘顶部的距离（整体往上抬）
    private const float StatusGap = 22f;            // 状态条（遗物/能量）与卡面之间
    private const float IntentGap = 2f;             // 卡面底端与意图行之间
    private const float IntentBoxHeight = 32f;      // 意图那一行的高度
    private const float StatusMinHeight = 36f;      // 状态条最小高度（量不到时用）
    private const float FallbackCardWidth = 95f;    // 卡面尺寸量不到时的兜底
    private const float FallbackCardHeight = 133f;
    private const float Margin = 8f;
    private const float PanelOffsetX = 0.35f;       // 整块右移量（× 卡宽），避开立绘中线上的球位/血条
    private const float IntentShiftX = -0.58f;      // 意图行额外左移（× 卡宽：让最左意图的左缘对齐卡面左缘）
    private const float IntentShiftY = -0.52f;      // 意图行额外上移（× 卡宽，用户指定「往左上挪」）
    internal const int IntentSeparation = -8;        // 同一张牌的多个意图之间的间距（负数=收紧，防止压到右邻卡）

    /// <summary>上一轮排版算出来的卡宽（每帧跟随位置时要用，不必重测）。</summary>
    private float _cardWidth = FallbackCardWidth;

    /// <summary>
    /// **整块缩放**：一手牌太多（塔罗愚第 9/12 回合一次 16 张）时按可用宽度等比压小，
    /// 保证「所有牌 + 意图 + 能量球」都在屏幕内。1 = 不缩放（其它 Boss 一律是 1）。
    /// </summary>
    private float _panelScale = 1f;

    /// <summary>
    /// 右侧要留给**能量球**的宽度（用户给的相对约束是「能量栏左缘 = 最后一张卡的右缘」，
    /// 所以它整颗都在最后一张卡的**右边**，排版时必须把它算进右边界，否则会顶出屏幕）。
    /// </summary>
    internal const float EnergyReserve = 62f;

    /// <summary>当前生效的整块缩放（给 <see cref="FollowBoss(float,float,bool)"/> 用）。</summary>
    private float PanelScale => _panelScale <= 0.01f ? 1f : _panelScale;

    private int _followLogs;

    /// <summary>诊断用：把状态条各子节点的真实屏幕位置打出来（排查「遗物压卡面」时靠它定位）。</summary>
    private void LogStatusRow()
    {
        if (_statusRow == null || !IsInstanceValid(_statusRow)) return;
        try
        {
            Logger.Log($"[CharBoss] 状态条屏幕={_statusRow.GetScreenPosition()} 尺寸={_statusRow.Size}");
            foreach (var child in _statusRow.GetChildren())
            {
                if (child is Control control && IsInstanceValid(control))
                {
                    Logger.Log($"[CharBoss]   子节点 {control.Name} 屏幕={control.GetScreenPosition()} " +
                               $"尺寸={control.Size} 缩放={control.Scale}");
                }
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 状态条诊断失败: " + e.Message);
        }
    }

    private void LayoutCells()
    {
        _layoutDirty = false;

        float cardW = FallbackCardWidth;
        float cardH = FallbackCardHeight;
        foreach (var cell in _cells)
        {
            var card = cell.Node.GetChildren().OfType<NCard>().FirstOrDefault();
            if (card != null && card.Size.X > 1f)
            {
                cardW = card.Size.X * BossHandCard.CardScale;
                cardH = card.Size.Y * BossHandCard.CardScale;
                break;
            }
        }
        _cardWidth = cardW;

        float statusH = StatusMinHeight;
        if (_statusRow != null && IsInstanceValid(_statusRow))
        {
            statusH = BossStatusRow.Height(_statusRow);
        }

        float cardTop = statusH + StatusGap;
        float intentTop = cardTop + cardH + IntentGap;
        // 宽度按「本回合最多几张牌」算，不随出牌缩小（否则状态条/能量会往左滑）
        int sizing = Math.Max(Math.Max(_turnCards, _widthCards), _cells.Count);
        float total = sizing * cardW + Math.Max(0, sizing - 1) * Gap;
        float panelH = intentTop + IntentBoxHeight;

        Size = new Vector2(total, panelH);

        // ★ 用户 2026-11-14（塔罗愚第 9 / 12 回合一次打出 16 张牌）：
        //   「一回合出超过 10 张牌时，把他这一回合的**意图整行往左移**，保证所有牌都在屏幕内
        //     （少用/不画能量栏右侧的空白，或压缩卡间距）」
        //   实现：整块**等比缩放**到可用宽度内 —— 牌面/意图/间距按同一比例一起缩，
        //   所以相对排版一个像素都不变，只是整体变小、整行因此能往左收进屏幕。
        //   右侧另外让出能量球那一格（它挂在最后一张卡的右边）。
        float viewportW = GetViewportRect().Size.X;
        float available = Math.Max(240f, viewportW - 2f * Margin - EnergyReserve);
        float scale = total > available ? Math.Max(0.45f, available / total) : 1f;
        if (Math.Abs(scale - _panelScale) > 0.001f)
        {
            _panelScale = scale;
            Scale = new Vector2(scale, scale);
            if (scale < 0.999f)
            {
                Logger.Log($"[CharBoss] 手牌栏整块缩放：{sizing} 张牌，宽 {total:0} > 可用 {available:0}" +
                           $"（屏幕 {viewportW:0}，右侧留给能量球 {EnergyReserve:0}）⇒ 缩放 {scale:0.###}");
            }
        }

        for (int i = 0; i < _cells.Count; i++)
        {
            var cell = _cells[i].Node;
            if (!IsInstanceValid(cell)) continue;
            cell.Position = new Vector2(i * (cardW + Gap), 0f);
            cell.Size = new Vector2(cardW, panelH);

            foreach (var child in cell.GetChildren())
            {
                switch (child)
                {
                    case NCard card:
                        card.Position = new Vector2(0f, cardTop);
                        break;
                    case Control intentRow when intentRow.Name == BossHandCard.IntentsRowName:
                        // 用户指定：意图整体往上、往左各挪 0.4 卡宽（卡面本身不动）
                        intentRow.Position = new Vector2(IntentShiftX * cardW, intentTop + IntentShiftY * cardW);
                        intentRow.Size = new Vector2(cardW, IntentBoxHeight);
                        break;
                }
            }
        }

        var (anchorX, anchorTop, debugAnchor, trusted) = BossHandAnchor.Resolve(_boss, GetViewportRect().Size);
        FollowBoss(anchorX, anchorTop, trusted);

        // 状态条：能量栏左缘贴最后一张卡的右缘、遗物下端贴卡牌上端
        // （横向基准用「本回合最大手牌数」，所以打出卡牌时不会左滑）
        if (_statusRow != null && IsInstanceValid(_statusRow))
        {
            float cardsRight = (sizing - 1) * (cardW + Gap) + cardW;
            BossStatusRow.PlaceBesideCards(_statusRow, cardsRight, cardTop);
        }

        // 独立意图（冰雹风暴）：能量栏正下方 + 与其他意图同一高度
        PlaceExtraIntents(intentTop, cardW);

        // ── 诊断：这两条以前**每次排版**都打（hand ui 1 行 + 每个子节点 1 行 ≈ 30 行），
        //    一秒钟几十行写进 mod_log.txt；排查完就只留开头几轮，需要时把上限调大即可。
        if (_handUiLogs++ < 30)
        {
            Logger.Log($"[CharBoss] hand ui: {_cells.Count} card(s), cell {cardW:0}x{cardH:0}, " +
                       $"状态条 y=0 h={statusH:0} / 卡面顶 y={cardTop:0} / 意图顶 y={intentTop:0} / 面板高 {panelH:0}, " +
                       $"anchor={debugAnchor}, pos={Position}, size={Size}, visible={IsVisibleInTree()}");
            if (_handUiLogs <= 3) LogStatusRow();
        }
    }

    /// <summary>「hand ui」这条诊断日志已打了几次（上限 30，见上）。</summary>
    private int _handUiLogs;

    /// <summary>
    /// 每帧只更新「整块的位置」：Boss 被位移（包括用模组挪动它）时，
    /// 卡牌/意图/遗物/能量必须和球位一样跟着走。
    /// </summary>
    private void FollowBoss()
    {
        var (anchorX, anchorTop, _, trusted) = BossHandAnchor.Resolve(_boss, GetViewportRect().Size);
        FollowBoss(anchorX, anchorTop, trusted);
    }

    private void FollowBoss(float anchorX, float anchorTop, bool trusted)
    {
        // 战斗结束/离开战斗房时立刻隐藏，避免在地图界面还挂着（显示优先级不该高于界面）
        Visible = _boss?.CombatState?.IsLiveCombat() == true;
        if (!Visible) return;

        // 横向：居中在 Boss 立绘上，再**夹进屏幕内**（左/右都夹）。
        // 多人「双 Boss 两套面板」时两只 Boss 分别站左上/右下，两套面板各自贴自己的 Boss；
        // 右边界不夹的话靠右那只的整排会顶出屏幕（原来只夹了左边）。
        //
        // ★ 2026-11-14：右边界还要再让出**能量球**那一格（它在最后一张卡右边），
        //   否则牌多的回合（塔罗愚 16 张）能量球会被顶出屏幕。
        //   整块可能被 <see cref="_panelScale"/> 缩小 ⇒ 这里一律用**缩放后的**尺寸算位置。
        float viewportW = GetViewportRect().Size.X;
        float panelW = Size.X * PanelScale;
        float panelH = Size.Y * PanelScale;
        float x = anchorX - panelW * 0.5f + PanelOffsetX * _cardWidth * PanelScale;
        // ⚠ `Math.Clamp(min > max)` 会抛异常（牌多到一屏放不下时就是这个情形）⇒ 自己夹
        float maxX = viewportW - panelW - Margin - EnergyReserve;
        if (maxX < Margin) maxX = Margin;
        if (x < Margin) x = Margin;
        else if (x > maxX) x = maxX;

        // 纵向：贴在锚点上方 GapAboveBoss。
        float bottom = anchorTop - GapAboveBoss;
        // 兜底：**量不到立绘包围盒**时（`trusted == false`，实测：玩家用「六火亡魂」时发生），
        // 上面那个 anchorTop 是 NCreature 的交互框顶，比立绘实际顶部低 100+px
        // ⇒ 整块会压到 Boss 身上（用户实测截图：卡牌整体偏下、能量/遗物栏偏右下、盖住 Boss）。
        // 这时把整块夹进**屏幕上半区**：正常角色（trusted=true）永远不经过这里，排版一个像素都不变；
        // 双 Boss 同场时靠下那只也走 trusted 分支，不受影响。
        if (!trusted)
        {
            float cap = GetViewportRect().Size.Y * 0.5f;
            if (bottom > cap)
            {
                bottom = cap;
                if (_anchorFallbackLogs++ < 3)
                {
                    Logger.Log("[CharBoss] 手牌栏锚点不可靠（量不到立绘包围盒）→ 夹到屏幕上半区" +
                               $"（anchorY={anchorTop:0}，面板底={bottom:0}）");
                }
            }
        }

        var target = new Vector2(
            x,
            Math.Max(Margin, bottom - panelH));

        // 位移日志：确认「拖动 Boss 时整块确实跟着走」（每次位移最多记 5 条，避免刷屏）
        if (_followLogs < 5 && Position.DistanceTo(target) > 2f)
        {
            _followLogs++;
            Logger.Log($"[CharBoss] 手牌栏跟随: {Position} → {target} (anchor={anchorX:0},{anchorTop:0})");
        }

        Position = target;
    }

    /// <summary>「锚点不可靠」的日志最多 3 条（每帧都会跑到这里）。</summary>
    private int _anchorFallbackLogs;
}
