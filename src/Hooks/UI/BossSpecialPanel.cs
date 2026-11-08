using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.HoverTips;

namespace CharacterBosses.UI;

/// <summary>
/// Boss 特殊效果说明：**可拖动的小面板**（替代原来常驻的原版 <c>NHoverTipSet</c>）。
///
/// 为什么换掉原版说明框（用户 2026-11-08 斗蛐蛐实测）：
///   · 多人/多 Boss 时好几个说明框会**互相重叠**，原版说明框只能跟着锚点走，挪不开；
///   · 玩家希望它像「怪物乱斗」自己的面板一样**能自由摆放、记住位置**；
///   · 还需要更小、更靠边、靠上，才能同时容纳好几只 Boss。
///
/// 所以这里自绘一个 <see cref="PanelContainer"/>：标题 + 正文（仍然是原版
/// <c>PowerModel.HoverTips</c> 的文案），整块可以按住鼠标拖动，落点写进
/// <c>%AppData%\SlayTheSpire2\CharacterBosses\hud_layout.json</c>（按「怪物类名」记）。
/// </summary>
internal partial class BossSpecialPanel : PanelContainer
{
    public const float MinWidth = 156f;
    private const float PanelWidth = MinWidth;
    private const int MinFontSize = 9;
    private const int MaxFontSize = 28;

    private readonly RichTextLabel _body;
    private string _contentKey = string.Empty;
    private bool _dragging;
    private Vector2 _dragOffset;
    private int _fontSize = 13;

    /// <summary>记位置的键（怪物类名，例如 <c>SilentShivBoss</c>）。</summary>
    public string LayoutKey { get; }

    /// <summary>玩家手动拖过（或配置文件里已有位置）⇒ 不再自动排版。</summary>
    public bool Placed { get; set; }

    public BossSpecialPanel(string layoutKey, Vector2? saved, int savedFontSize = 0)
    {
        LayoutKey = layoutKey;
        Name = "BossSpecialPanel_" + layoutKey;
        MouseFilter = MouseFilterEnum.Stop;
        CustomMinimumSize = new Vector2(PanelWidth, 0f);
        Size = new Vector2(PanelWidth, 1f);
        ZIndex = 60;

        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.05f, 0.06f, 0.08f, 0.90f),
            BorderColor = new Color(0.62f, 0.52f, 0.30f, 0.85f),
            CornerRadiusTopLeft = 6,
            CornerRadiusTopRight = 6,
            CornerRadiusBottomLeft = 6,
            CornerRadiusBottomRight = 6,
        };
        style.SetBorderWidthAll(1);
        AddThemeStyleboxOverride("panel", style);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 8);
        margin.AddThemeConstantOverride("margin_right", 8);
        margin.AddThemeConstantOverride("margin_top", 6);
        margin.AddThemeConstantOverride("margin_bottom", 6);
        margin.MouseFilter = MouseFilterEnum.Ignore;
        AddChild(margin);

        _body = new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = true,
            ScrollActive = false,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(PanelWidth - 16f, 0f),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        _body.AddThemeFontSizeOverride("normal_font_size", 11);
        _body.AddThemeFontSizeOverride("bold_font_size", 11);
        margin.AddChild(_body);

        if (savedFontSize >= MinFontSize && savedFontSize <= MaxFontSize) SetFontSize(savedFontSize);
        else SetFontSize(CharacterBosses.Core.BossConfig.CricketPanelFontSize);

        if (saved is Vector2 pos)
        {
            Placed = true;
            // 位置要等节点进树后才有意义（CanvasLayer 的坐标系），由 BossSpecialUi 在 Place 时应用。
            BossHudLayout.PendingPosition[this] = pos;
        }
    }

    /// <summary>更新文案（内容没变就什么都不做，避免每帧重排导致闪烁）。</summary>
    public void SetTips(IEnumerable<IHoverTip> tips)
    {
        var sb = new StringBuilder();
        foreach (var tip in tips)
        {
            // PowerModel.HoverTips 给的通常是 HoverTip 结构体（自带 Title/Description）；
            // 其它实现（卡牌关键词、静态提示）只暴露 Id，这里就跳过。
            if (tip is not HoverTip ht) continue;
            if (!string.IsNullOrWhiteSpace(ht.Title))
                sb.Append("[b]").Append(Esc(ht.Title!)).Append("[/b]\n");
            if (!string.IsNullOrWhiteSpace(ht.Description))
                sb.Append(Esc(ht.Description)).Append('\n');
        }
        string text = sb.ToString().TrimEnd();
        if (text == _contentKey) return;
        _contentKey = text;
        _body.Text = text;
    }

    public void ApplyPendingPosition()
    {
        if (BossHudLayout.PendingPosition.Remove(this, out var pos))
            Position = pos;
    }

    /// <summary>字号（用户 2026-11-08：说明字太小，要能自己放大缩小并记住）。</summary>
    public void SetFontSize(int size)
    {
        size = Math.Clamp(size, MinFontSize, MaxFontSize);
        if (size == _fontSize && IsNodeReady()) return;
        _fontSize = size;
        _body.AddThemeFontSizeOverride("normal_font_size", size);
        _body.AddThemeFontSizeOverride("bold_font_size", size);
        // 宽度跟着字号走，字大了不至于每行挤成一坨。
        float width = Math.Max(MinWidth, size * 15f);
        CustomMinimumSize = new Vector2(width, 0f);
        _body.CustomMinimumSize = new Vector2(width - 16f, 0f);
        Callable.From(() => Size = new Vector2(width, Size.Y)).CallDeferred();
    }

    public override void _GuiInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton mb when mb.Pressed && mb.CtrlPressed
                                               && (mb.ButtonIndex == MouseButton.WheelUp || mb.ButtonIndex == MouseButton.WheelDown):
                // Ctrl + 滚轮缩放说明文字，松手时和其它布局一起存进 hud_layout.json。
                int delta = mb.ButtonIndex == MouseButton.WheelUp ? 1 : -1;
                SetFontSize(_fontSize + delta);
                BossHudLayout.Save(LayoutKey, GlobalPosition, _fontSize);
                AcceptEvent();
                break;
            case InputEventMouseButton mb when mb.ButtonIndex == MouseButton.Left:
                if (mb.Pressed)
                {
                    _dragging = true;
                    _dragOffset = GetGlobalMousePosition() - GlobalPosition;
                    MoveToFront();
                    AcceptEvent();
                }
                else if (_dragging)
                {
                    _dragging = false;
                    Placed = true;
                    BossHudLayout.Save(LayoutKey, GlobalPosition, _fontSize);
                    AcceptEvent();
                }
                break;
            case InputEventMouseMotion when _dragging:
                GlobalPosition = GetGlobalMousePosition() - _dragOffset;
                AcceptEvent();
                break;
        }
    }

    private bool _mouseWasDown;

    /// <summary>
    /// 拖动**不用 <see cref="_GuiInput"/> 的鼠标事件**：斗蛐蛐里游戏自己的全屏 Control 会把
    /// 我们的面板挡在鼠标事件之外（用户 2026-11-08：说明框拖不动）。改成每帧轮询鼠标位置，
    /// 只要按下时指针落在这块矩形里就开始拖 —— 不依赖事件路由。
    /// </summary>
    public override void _Process(double delta)
    {
        try
        {
            if (!IsVisibleInTree())
            {
                _dragging = false;
                _mouseWasDown = false;
                return;
            }

            bool down = Input.IsMouseButtonPressed(MouseButton.Left);
            Vector2 mouse = GetGlobalMousePosition();

            if (down && !_mouseWasDown && !_dragging && GetGlobalRect().HasPoint(mouse))
            {
                _dragging = true;
                _dragOffset = mouse - GlobalPosition;
                MoveToFront();
            }
            else if (_dragging)
            {
                if (down)
                {
                    GlobalPosition = mouse - _dragOffset;
                }
                else
                {
                    _dragging = false;
                    Placed = true;
                    BossHudLayout.Save(LayoutKey, GlobalPosition, _fontSize);
                    Logger.Log("[CharBoss] 说明框已挪到 (" + (int)GlobalPosition.X + ", " + (int)GlobalPosition.Y + ")");
                }
            }

            _mouseWasDown = down;
        }
        catch { /* 拖动失败不该影响战斗 */ }
    }
    private static string Esc(string s) => s.Replace("[", "[lb]");
}

/// <summary>
/// 说明框位置存档：<c>%AppData%\SlayTheSpire2\CharacterBosses\hud_layout.json</c>，
/// 内容是 <c>{ "SilentShivBoss": [x, y], ... }</c>。
/// </summary>
internal static class BossHudLayout
{
    private static readonly Dictionary<BossSpecialPanel, Vector2> _pending = new();
    private static Dictionary<string, float[]>? _cache;

    public static Dictionary<BossSpecialPanel, Vector2> PendingPosition => _pending;

    private static string FilePath
    {
        get
        {
            string dir = System.IO.Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
                "SlayTheSpire2", "CharacterBosses");
            try { Directory.CreateDirectory(dir); } catch { /* 忽略 */ }
            return System.IO.Path.Combine(dir, "hud_layout.json");
        }
    }

    public static Vector2? Load(string key)
    {
        try
        {
            _cache ??= Read();
            if (_cache != null && _cache.TryGetValue(key, out var xy) && xy is { Length: >= 2 })
                return new Vector2(xy[0], xy[1]);
        }
        catch { /* 忽略 */ }
        return null;
    }

    public static void Save(string key, Vector2 pos, int fontSize = 0)
    {
        try
        {
            _cache ??= Read() ?? new Dictionary<string, float[]>();
            float size = fontSize > 0 ? fontSize : (_cache.TryGetValue(key, out var old) && old is { Length: >= 3 } ? old[2] : 0f);
            _cache[key] = new[] { pos.X, pos.Y, size };
            File.WriteAllText(FilePath, JsonSerializer.Serialize(_cache), new UTF8Encoding(false));
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 说明框位置保存失败: " + e.Message);
        }
    }

    /// <summary>读回这只 Boss 上次调好的字号（0 = 没存过）。</summary>
    public static int LoadFontSize(string key)
    {
        try
        {
            _cache ??= Read();
            if (_cache != null && _cache.TryGetValue(key, out var v) && v is { Length: >= 3 })
                return (int)v[2];
        }
        catch { /* 忽略 */ }
        return 0;
    }

    private static Dictionary<string, float[]>? Read()
    {
        try
        {
            string path = FilePath;
            if (!File.Exists(path)) return null;
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<Dictionary<string, float[]>>(json);
        }
        catch { return null; }
    }
}
