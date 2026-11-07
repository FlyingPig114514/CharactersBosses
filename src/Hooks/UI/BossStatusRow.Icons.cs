using System;
using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Relics;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace CharacterBosses.UI;

/// <summary>
/// 状态条里两类图标的创建：遗物（原版 <c>NRelicInventoryHolder</c>，可点击看详情）与
/// 能量球（原版 <c>NEnergyCounter</c>）。拆出来纯粹是为了单文件 200 行以内。
/// </summary>
public static partial class BossStatusRow
{
    /// <summary>诊断日志上限（原来每次 Fill 都打 8～10 条，日志一秒几十行，见 PerfLog）。</summary>
    private static int _relicLogs;

    /// <summary>建一个原版遗物图标并挂到状态条；失败返回 null（<see cref="Fill"/> 会因此作废这一轮缓存）。</summary>
    private static Control? AddRelic(Control row, Player shadow, RelicModel relic)
    {
        try
        {
            var holder = NRelicInventoryHolder.Create(relic);
            if (holder == null) return null;

            row.AddChild(holder);
            var size = Fit(holder);

            // 点击 = 打开原版遗物详情页（和点自己的遗物完全一样的界面，左右可翻）
            holder.Connect(NClickableControl.SignalName.Released,
                Callable.From<NButton>(_ => OpenInspect(shadow, relic)));
            if (_relicLogs++ < 40) Logger.Log($"[CharBoss] 遗物图标 {relic.Id.Entry}: 盒={size}");
            return holder;
        }
        catch (Exception e)
        {
            Logger.Log($"[CharBoss] 遗物节点失败 {relic.Id.Entry}: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// 能量显示：**原版 <c>NEnergyCounter</c>，一个属性都不改**（尺寸/字号/球体/数字全用原版），
    /// 只把它放到遗物右边。
    ///
    /// 两个必须遵守的点（都踩过）：
    ///   ① `AnimIn()` 会补间自身 Position，不能调 —— 直接把 Position 设成 (0,0)；
    ///   ② **不要改 Size / Scale** —— 改了就和球体不匹配，数字会比球框大（用户实测「3/3 太大」）。
    /// 另外节点只建一次并复用：每次刷新重建会反复触发它的粒子特效（看起来像多转了几个圈）。
    /// </summary>
    private static Control? MakeEnergyDisplay(Player shadow, PlayerCombatState pcs)
    {
        try
        {
            // ⚠ **按影武者各建一个**（多 Boss 同场时两套面板各要一个能量球）。
            //   以前只有一对静态字段 `_energyCounter/_energyPlayer`，两只 Boss 交替刷新时会
            //   每次刷新都重建一个 NEnergyCounter —— 那正好触发它每次创建都播一遍的粒子特效
            //   （下面注释说的"多转几个圈"），而且两行会互相抢节点。
            if (!EnergyCounters.TryGetValue(shadow, out var counter) ||
                counter == null || !GodotObject.IsInstanceValid(counter))
            {
                counter = NEnergyCounter.Create(shadow);
                EnergyCounters[shadow] = counter;
            }

            if (counter != null)
            {
                counter.Position = Vector2.Zero;
                counter.Visible = true;
                // 原版球是「锚点撑满父级」的节点：CustomMinimumSize/组合最小尺寸都是 0，
                // 只有 Size 是真实尺寸（128×128）。显式写进 CustomMinimumSize，
                // 后面的排版（行高/对齐）才能读到它，不必依赖节点自己 Size 的时机。
                var actual = counter.Size;
                if (actual.X > 1f && actual.Y > 1f) counter.CustomMinimumSize = actual;
                return counter;
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 能量球节点失败: " + e.Message);
        }

        // 兜底：原版节点建不出来时，至少把数字显示出来
        var box = new Vector2(EnergyBox, EnergyBox);
        var root = new Control
        {
            CustomMinimumSize = box,
            Size = box,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        bool empty = pcs.Energy <= 0;
        root.AddChild(MakeGameText($"{pcs.Energy}/{pcs.MaxEnergy}", box,
            empty ? new Color(1f, 0.45f, 0.4f) : new Color(1f, 0.97f, 0.9f)));
        return root;
    }

    /// <summary>按影武者各记一个原版能量球节点（多 Boss 同场时每套面板各用各的）。</summary>
    private static readonly System.Collections.Generic.Dictionary<Player, NEnergyCounter?> EnergyCounters = new();

    /// <summary>
    /// 辉星显示：**直接用原版 <c>NStarCounter</c> 节点本身**（<c>star_counter.tscn</c>）。
    ///
    /// 来历（本轮实测证据，别再自造）：上一版是「<c>sprite_fonts/star_icon.png</c> + 一个普通 Label」，
    /// 用户明确指出「贴图和字体都不对」。把原版 <c>%StarCounter</c> 的节点树打出来后真相是：
    ///   · 星星贴图 = <c>res://images/ui/combat/energy_star.png</c>，外加两层旋转的
    ///     <c>energy_star_layer_2.png</c> / <c>energy_star_layer_3.png</c>，
    ///     外面套 <c>star_counter.tscn</c> 里的 HSV ShaderMaterial（会呼吸/闪）。
    ///     <c>star_icon.png</c> 只是**富文本里的小内联图标**，根本不是这个大盘子。
    ///   · 数字 = <c>MegaRichTextLabel</c>，字号 36，字体走主题（<c>themes/fonts/zhs/…</c>）。
    ///   · 位置 = 能量球的**左下角**（原版 <c>StarCounter.Position = (-36, 40)</c>，尺寸同为 128×128）。
    /// 所以现在直接实例化原版场景：贴图/字体/着色/旋转光环/悬停说明/数字补间全是原版的，
    /// 我们只负责「挂到能量球左下角」一件事。它自己监听 <c>StarsChanged</c>，数字会自己更新。
    /// </summary>
    private static void ShowStars(Player shadow, PlayerCombatState pcs, Control? row = null)
    {
        try
        {
            // 用**这一行自己的**能量球节点（多 Boss 同场时有两条以上状态栏，不能都用静态那个）
            var energy = EnergyOf(row);
            if (energy == null || !GodotObject.IsInstanceValid(energy)) return;

            // 已经挂过、且还是同一个影武者 → **直接复用，不重建**。
            // 原版 NStarCounter 自己监听 StarsChanged，数字会自己更新；而每次刷新都摘掉重建的话，
            // 新节点在同步到真实值之前会先显示默认值 —— 表现就是「每打一张牌辉星闪到 1」（用户实测）。
            // ⚠ 绑定关系必须**按这一行的能量球**各记一份：多 Boss 同场时两套面板各有一个辉星计数器，
            //   以前共用一个静态字段 → 两只交替刷新时每轮都在「摘掉 + 重建」，闪烁就回来了。
            if (StarBoundTo.TryGetValue(energy, out var bound) &&
                ReferenceEquals(bound, shadow) && energy.HasNode(StarCounterNodeName)) return;

            DumpVanillaStarCounter();
            RemoveStarNodes(energy);
            StarBoundTo[energy] = shadow;

            if (TryShowVanillaStar(energy, shadow)) return;

            // 原版场景取不到时才自绘（不能因为这个让用户完全看不到辉星）
            if (pcs.Stars <= 0) return;
            ShowStarsFallback(energy, pcs);
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 辉星显示失败: " + e.Message);
        }
    }

    /// <summary>当前辉星计数器绑定的影武者（**按能量球节点各记一份**，见 <see cref="ShowStars"/>）。</summary>
    private static readonly System.Collections.Generic.Dictionary<Control, Player> StarBoundTo = new();

    /// <summary>清掉指向已释放节点的辉星绑定（面板每场战斗重建，旧能量球会被 QueueFree）。</summary>
    private static void RemoveDeadStarBindings()
    {
        try
        {
            var dead = new List<Control>();
            foreach (var key in StarBoundTo.Keys)
            {
                if (key == null || !GodotObject.IsInstanceValid(key)) dead.Add(key!);
            }
            foreach (var key in dead) StarBoundTo.Remove(key);
        }
        catch { /* 忽略 */ }
    }

    private const string StarCounterScenePath = "res://scenes/combat/energy_counters/star_counter.tscn";

    /// <summary>我们挂上去的原版辉星计数器节点名（每回合刷新时要先摘掉旧的）。</summary>
    private const string StarCounterNodeName = "BossStarCounter";

    private static bool _starNodeLogged;

    /// <summary>挂一个**原版**辉星计数器到能量球左下角；成功返回 true。</summary>
    private static bool TryShowVanillaStar(Control energy, Player shadow)
    {
        try
        {
            var scene = GD.Load<PackedScene>(StarCounterScenePath);
            if (scene == null || !scene.CanInstantiate())
            {
                if (!_starNodeLogged)
                {
                    _starNodeLogged = true;
                    Logger.Log($"[CharBoss] 辉星：原版场景取不到（{StarCounterScenePath}）→ 退回自绘徽章");
                }
                return false;
            }

            var counter = scene.Instantiate<MegaCrit.Sts2.Core.Nodes.Combat.NStarCounter>();
            if (counter == null) return false;

            counter.Name = StarCounterNodeName;
            energy.AddChild(counter);

            // 用户指定：贴在费用球的**右下角**，并且要**往左上挪一点、略微盖住能量栏**
            //（原版是贴在球的左下角并压住球体；这里保留"压住一点"的观感）
            var orbSize = energy.Size;
            if (orbSize.X < 1f || orbSize.Y < 1f) orbSize = new Vector2(128f, 128f);
            counter.Position = new Vector2(orbSize.X * 0.44f, orbSize.Y * 0.44f);
            // 图层：和能量球同层（不抬 ZIndex，否则切地图/设置时本 mod 的节点会浮在游戏界面之上）
            counter.ZIndex = 0;
            // 订阅 StarsChanged + 按原版规则决定显不显示（辉星为 0 且角色不强制显示时它会自己隐藏）
            counter.Initialize(shadow);

            if (!_starNodeLogged)
            {
                _starNodeLogged = true;
                Logger.Log($"[CharBoss] 辉星：已挂原版 NStarCounter pos={counter.Position} size={counter.Size} " +
                           $"visible={counter.Visible} stars={shadow.PlayerCombatState?.Stars}");
            }
            return true;
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 辉星：原版节点实例化失败 → 退回自绘徽章: " + e.Message);
            return false;
        }
    }

    /// <summary>摘掉上一次刷新建的辉星节点（原版节点 + 旧的自绘徽章）。</summary>
    private static void RemoveStarNodes(Control energy)
    {
        foreach (var old in energy.GetChildren())
        {
            if (old is not Node n || !GodotObject.IsInstanceValid(n)) continue;
            var name = n.Name.ToString();
            if (name != StarCounterNodeName && name != StarBadgeName) continue;
            energy.RemoveChild(n);
            n.QueueFree();
        }
    }

    /// <summary>兜底：原版场景不可用时自绘一颗（数字用原版 MegaLabel 字号，别用默认字体）。</summary>
    private static void ShowStarsFallback(Control energy, PlayerCombatState pcs)
    {
        try
        {
            const float side = 46f;    // 星星图标边长
            var box = new Control
            {
                Name = StarBadgeName,
                CustomMinimumSize = new Vector2(side, side),
                Size = new Vector2(side, side),
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };

            var tex = LoadStarIcon();
            if (tex != null)
            {
                var icon = new TextureRect
                {
                    Texture = tex,
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                    Size = new Vector2(side, side),
                    CustomMinimumSize = new Vector2(side, side),
                    MouseFilter = Control.MouseFilterEnum.Ignore,
                };
                box.AddChild(icon);
            }
            else
            {
                // 取不到原版图时至少别什么都不显示
                Logger.Log("[CharBoss] 辉星图标取不到，退回文字显示");
            }

            // 数字叠在星星中间：原版是 MegaRichTextLabel / 字号 36 → 这里用同族的 MegaLabel + 同样字号
            var count = MakeGameText(pcs.Stars.ToString(), new Vector2(side, side), new Color(1f, 0.97f, 0.9f), 36f);
            box.AddChild(count);

            // 贴到能量球**左下角**（原版 StarCounter 就在这个位置；球 128×128，偏移按球尺寸算）
            var energySize = energy.Size;
            if (energySize.X < 1f || energySize.Y < 1f) energySize = new Vector2(128f, 128f);
            box.Position = new Vector2(-energySize.X * 0.28f, energySize.Y * 0.31f);
            box.ZIndex = 5;

            energy.AddChild(box);
            Logger.Log($"[CharBoss] 辉星徽章(兜底): {pcs.Stars} 个，贴在能量球左下 offset={box.Position}");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 辉星徽章失败: " + e.Message);
        }
    }

    private const string StarBadgeName = "BossStarBadge";

    private static bool _starDumped;

    /// <summary>每场战斗重新武装「原版辉星计数器」诊断。</summary>
    internal static void ResetDiagnostics() => _starDumped = false;

    /// <summary>
    /// 诊断：把**原版**辉星计数器（<c>NCombatUi</c> 里那个 <c>%StarCounter</c>）的节点结构、
    /// 贴图资源路径、字体与字号打出来。
    ///
    /// 目的：用户实测反馈「我们的辉星贴图和字体都跟原版不一样」。要改得对，必须知道原版
    /// 那个大星星到底用哪张图（<c>NStarCounter</c> 的 <c>Icon</c> 带 ShaderMaterial，
    /// 大概率不是 <c>star_icon.png</c> 那张富文本小图标）、数字用的哪个字体/字号。
    /// 每场战斗只打一次。
    /// </summary>
    internal static void DumpVanillaStarCounter()
    {
        if (_starDumped) return;
        _starDumped = true;
        try
        {
            var ui = NCombatRoom.Instance?.Ui;
            var star = ui?.GetNodeOrNull<MegaCrit.Sts2.Core.Nodes.Combat.NStarCounter>("%StarCounter");
            if (star == null)
            {
                Logger.Log("[CharBoss] 原版辉星计数器: 取不到 %StarCounter");
                return;
            }
            Logger.Log($"[CharBoss] 原版辉星计数器 {star.Name} size={star.Size} visible={star.Visible}");
            DumpNode(star, 0);
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 原版辉星计数器诊断失败: " + e.Message);
        }
    }

    /// <summary>递归打节点名/类型/贴图/字体（最多 3 层，够看出星星那张图与数字字体）。</summary>
    private static void DumpNode(Node node, int depth)
    {
        if (depth > 3) return;
        string pad = new string(' ', 2 + depth * 2);
        string extra = string.Empty;

        try
        {
            if (node is TextureRect tr && tr.Texture != null)
                extra += $" texture={tr.Texture.ResourcePath}";
            if (node is Sprite2D sp && sp.Texture != null)
                extra += $" texture={sp.Texture.ResourcePath}";
            if (node is CanvasItem ci && ci.Material != null)
                extra += $" material={ci.Material.ResourcePath}";
            if (node is RichTextLabel rtl)
            {
                var f = rtl.GetThemeFont("normal_font");
                extra += $" font={f?.ResourcePath} fontSize={rtl.GetThemeFontSize("normal_font_size")}";
            }
            else if (node is Label lb)
            {
                var f = lb.GetThemeFont("font");
                extra += $" font={f?.ResourcePath} fontSize={lb.GetThemeFontSize("font_size")}";
            }
            if (node is Control c)
                extra += $" pos={c.Position} size={c.Size}";
        }
        catch { /* 诊断而已，读不到就少打点 */ }

        Logger.Log($"[CharBoss]   辉星节点{pad}{node.Name}[{node.GetType().Name}]{extra}");
        foreach (var child in node.GetChildren())
        {
            if (child is Node n && GodotObject.IsInstanceValid(n)) DumpNode(n, depth + 1);
        }
    }

    private static Texture2D? _starIcon;
    private static bool _starIconTried;

    /// <summary>原版星星图标（和原版 <c>StarIconsFormatter</c> 用的是同一张图）。</summary>
    private static Texture2D? LoadStarIcon()
    {
        if (_starIconTried) return _starIcon;
        _starIconTried = true;
        try
        {
            _starIcon = GD.Load<Texture2D>("res://images/packed/sprite_fonts/star_icon.png");
            if (_starIcon == null) Logger.Log("[CharBoss] 星星图标路径取不到（res://images/packed/sprite_fonts/star_icon.png）");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 星星图标加载失败: " + e.Message);
        }
        return _starIcon;
    }

    /// <summary>用原版文字节点（MegaLabel）造一个居中数字；失败时退回 Label。</summary>
    /// <param name="fontSize">字号（&lt;=0 = 用能量球那个字号）。原版辉星数字是 36。</param>
    private static Control MakeGameText(string text, Vector2 box, Color color, float fontSize = -1f)
    {
        int size = (int)(fontSize > 0f ? fontSize : EnergyFontSize);
        try
        {
            var megaLabel = new MegaCrit.Sts2.addons.mega_text.MegaLabel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Size = box,
                CustomMinimumSize = box,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            megaLabel.AddThemeColorOverride("font_color", color);
            megaLabel.AddThemeFontSizeOverride("font_size", size);
            megaLabel.SetTextAutoSize(text);
            return megaLabel;
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] MegaLabel 失败，退回 Label: " + e.Message);
            var label = new Label
            {
                Text = text,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Size = box,
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            label.AddThemeColorOverride("font_color", color);
            label.AddThemeFontSizeOverride("font_size", size);
            return label;
        }
    }

    /// <summary>
    /// 把节点统一成**同样高**的图标：一律按 `IconSide` 的高度缩放（等比，宽度按原比例），
    /// 并写回 CustomMinimumSize 让排版按缩放后的尺寸排。
    ///
    /// 早先只「超过就缩小、不超就原样」，于是 68×68 的被缩到 56、48 的还是 48 ——
    /// 一排遗物高矮不一（用户要求「把遗物都整的一样高」，参照玩家 HUD 那排）。
    /// </summary>
    private static Vector2 Fit(Control child)
    {
        var size = child.GetCombinedMinimumSize();
        size.X = Math.Max(size.X, child.Size.X);
        size.Y = Math.Max(size.Y, child.Size.Y);
        if (size.X < 1f || size.Y < 1f) size = new Vector2(RelicFallback, RelicFallback);

        float k = IconSide / size.Y;      // 统一高度
        if (k > 0.01f && Math.Abs(k - 1f) > 0.01f)
        {
            child.PivotOffset = Vector2.Zero;
            child.Scale = new Vector2(k, k);
            size *= k;
        }

        child.CustomMinimumSize = size;
        return size;
    }

    private static void OpenInspect(Player shadow, RelicModel relic)
    {
        try
        {
            var all = new List<RelicModel>();
            var relics = shadow.Relics;
            if (relics != null)
            {
                for (int i = 0; i < relics.Count; i++) all.Add(relics[i]);
            }

            var screen = NGame.Instance?.GetInspectRelicScreen();
            if (screen == null) return;
            screen.Open(all, relic);
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 遗物详情页打开失败: " + e.Message);
        }
    }

    private static Label MakeLabel(string text, Color color)
    {
        var label = new Label
        {
            Text = text,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            VerticalAlignment = VerticalAlignment.Center,
        };
        label.AddThemeColorOverride("font_color", color);
        label.AddThemeFontSizeOverride("font_size", 20);
        label.CustomMinimumSize = new Vector2(0f, MinHeight);
        return label;
    }
}
