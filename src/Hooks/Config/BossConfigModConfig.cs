using System;
using System.Collections.Generic;
using BaseLib.Config;
using BaseLib.Config.UI;
using Godot;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.addons.mega_text;
using CharacterBosses.Core;

namespace CharacterBosses.Config;

/// <summary>
/// **本模组的配置界面** —— 交给 BaseLib 的「模组配置」子菜单渲染（用户 2026-10-28 要求：
/// 之前自绘的那块面板和原版不契合，改用 BaseLib）。
///
/// 外壳、标题、字体、主题、返回/恢复默认都是 BaseLib 用原版控件搭的，我们只往容器里塞行。
/// 数据存在 <see cref="BossConfig"/> 自己的 `charbosses_config.cfg` 里，**位置在用户数据目录**
/// （`%APPDATA%\SlayTheSpire2\mod_configs\CharacterBosses\`，见 `BossConfig.FilePath`）——
/// 以前写在模组目录（DLL 同目录），上创意工坊后那个目录是 workshop 的，更新会覆盖、也可能只读。
/// **不**用 BaseLib 自己的 .cfg 存一份，理由：
///   · 地图清单是**动态**的（装了加地图的模组会多出条目），而 BaseLib 的配置项是编译期的属性；
///   · 没装 BaseLib 的玩家也得能玩（那种情况下界面不可用，但手改同一个 JSON 照样生效）。
///
/// 入口：主菜单 →「模组配置」，见 <see cref="BossConfigBaseLibBootstrap"/>。
/// </summary>
public sealed class BossConfigModConfig : SimpleModConfig
{
    // 注：`SimpleModConfig` 只有无参构造，BaseLib 用**根命名空间**给 .cfg 定名
    // ⇒ 配置文件是 `%APPDATA%\SlayTheSpire2\mod_configs\CharacterBosses.cfg`。
    // 本类一个配置属性都没有，那个文件实际是空的；真正的数据在 charbosses_config.cfg。

    /// <summary>
    /// 本类**没有任何配置属性**（全部内容是动态行），而 BaseLib 的注册条件是
    /// 「有可见属性」或「有按钮」—— 两个都不满足就不会出现在模组列表里，所以这里直接放行。
    /// </summary>
    public override bool VisibleInModList() => true;

    /// <summary>构建期标记（这段时间里勾选框的程序化赋值不该写配置）。</summary>
    private bool _initializing;

    public override void SetupConfigUI(Control optionContainer)
    {
        try
        {
            _initializing = true;
            // 顺序 = 编号顺序 A1 → A2 → A3 → A4（用户 2026-10-29：「你这什么神秘排列顺序…
            // 把顺序调对」—— 之前 A4 是在 A1 里顺手加进去的，所以画面上的编号是 1/4/2/3）。
            BuildMapAndBossSection(optionContainer);   // v0.2.454：A1 与 A2 合并成一节
            optionContainer.AddChild(CreateDividerControl());
            optionContainer.AddChild(CreateDividerControl());
            BuildBgmSection(optionContainer);
            optionContainer.AddChild(CreateDividerControl());
            BuildSharePoolSection(optionContainer);
            optionContainer.AddChild(CreateDividerControl());
            BuildTelemetrySection(optionContainer);
            optionContainer.AddChild(CreateDividerControl());
            // 用户 2026-11-08：斗蛐蛐专用的那一节，**只有装了怪物乱斗才出现**。
            if (CricketCompat.Installed)
            {
                BuildCricketSection(optionContainer);
                optionContainer.AddChild(CreateDividerControl());
            }
            optionContainer.AddChild(InfoLabel("改动立刻保存到：" + BossConfig.DescribeConfigPath() +
                                               "\n（用户数据目录，**不在**模组文件夹里 —— 创意工坊更新模组不会覆盖你的设置）"));

            SetupFocusNeighbors(optionContainer);

            Logger.Log($"[CharBoss] 配置界面已由 BaseLib 构建（容器 {optionContainer?.Name}，" +
                       $"BGM 来源 {BossConfig.DescribeBgmSource(BossConfig.BgmSource)}）");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 配置界面构建失败: " + e);
        }
        finally
        {
            _initializing = false;
        }
    }

    // ── 零件 ──────────────────────────────────────────────

    /// <summary>
    /// **一行 = 控件在左 + 文字在右（文字吃掉剩余宽度）**。自己搭 `HBoxContainer`，不用 `NConfigOptionRow`。
    ///
    /// 为什么不用 BaseLib 的 `NConfigOptionRow`：它内部是 `MarginContainer`，左右两个子节点
    /// **共用同一个矩形**、控件靠右 ⇒ 这个窗口尺寸下勾选框的落点是 `x=610..1694`（实测日志），
    /// 乘上实际显示缩放就跑出屏幕了 —— **勾选框完全看不见**（用户 2026-10-28 反复反馈；
    /// 别的模组的 BaseLib 配置页也一样看不见，拿「崩坠」「意图状态机」的页签对照过）。
    ///
    /// ⚠ 排版还有个坑：`CreateRawLabelControl` 造出来的 `MegaRichTextLabel` **最小宽度只有 1px**、
    /// 高度是 0。放在 `HBoxContainer` 里（不像 MarginContainer 会拉伸子节点）如果不给
    /// `ExpandFill`，文字宽度就只剩 1px ⇒ **整行什么都看不见**（实测）。所以这里让**文字**
    /// 吃满剩余宽度、控件固定放最左边 —— 控件永远在屏幕里，文字也正常显示。
    ///
    /// 另外两个坑（都实测过，别再踩）：
    ///   ① `NTickbox` 的初值必须在 `_Ready` 里设，而且 `_Ready` 里**不能**调 `base._Ready()`
    ///      （原版直接抛 `Don't call base._Ready()! Call ConnectSignals() instead.`）；
    ///   ② 每次点击都要**现读配置**再取反，别把构建时的状态捕进闭包
    ///      （旧面板因此「关掉以后开不回来」）。
    /// </summary>
    private static HBoxContainer AddInlineRow(Control container, Control control, Control label, string rowName)
    {
        var row = new HBoxContainer
        {
            Name = rowName,
            CustomMinimumSize = new Vector2(0f, 64f),
        };
        row.AddThemeConstantOverride("separation", 18);
        label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(control);
        row.AddChild(label);
        container.AddChild(row);
        return row;
    }

    /// <summary>
    /// 行内文字标签。
    /// ⚠ `ModConfig.CreateRawLabelControl` 造出来的 `MegaRichTextLabel` **高度是 0**
    /// （`AutoSizeEnabled=false`），不给最小高度就**整块不画**（实测：文字全消失、只剩分割线）。
    /// BaseLib 的 `NConfigOptionRow` 也是内部给 label 设了 `CustomMinimumSize(0,64)` 才显示的。
    /// </summary>
    private static MegaRichTextLabel RowLabel(string text, int fontSize = 28)
    {
        var label = ModConfig.CreateRawLabelControl(text, fontSize);
        label.CustomMinimumSize = new Vector2(0f, 64f);
        label.VerticalAlignment = VerticalAlignment.Center;
        return label;
    }

    /// <summary>说明性小字（不是行内控件那一列，用 FitContent 让它自己长高）。</summary>
    private static MegaRichTextLabel InfoLabel(string text, int fontSize = 24)
    {
        var label = ModConfig.CreateRawLabelControl(text, fontSize);
        label.FitContent = true;
        label.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        return label;
    }

    // ── A1：要替换的地图 ──────────────────────────────────

    private void BuildMapAndBossSection(Control container)
    {
        container.AddChild(CreateSectionHeader("A1 · 替换的地图与 Boss 选择"));
        container.AddChild(InfoLabel(
            "**勾上地图** ⇒ 这张地图的 Boss 会被换掉；它下面那一片勾选决定「这一幕会遇到哪些」。\n" +
            "· 同一**列** = 同一个角色。每个角色至少留 1 个，**第三幕至少留 2 个** ——\n" +
            "　 取消到下限以下会被直接拦住（一层/二层全场至少 5 个，三层至少 10 个）。\n" +
            "· 没勾的地图，它下面的勾选**暂不生效**（但会保留，随时勾回来）。\n" +
            "· ⚠ 多人联机时**以房主的设置为准**：主 Boss 按房主的勾选抽，客人跟随同步的遭遇；\n" +
            "　 多人局的第 2、3 只「搭子」不受这里影响（必须两端算出同一套，否则会不同步）。\n" +
            "· 单人局只有 1 只 Boss ⇒ 这里的勾选 100% 生效。"));

        List<BossConfig.MapInfo>[] maps;
        try { maps = BossConfig.MapsByAct(); }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 配置界面：列地图失败: " + e.Message);
            container.AddChild(InfoLabel("（读取地图清单失败，详见 mod_log.txt）"));
            return;
        }

        string[] actTitles = { "第一幕", "第二幕", "第三幕" };
        bool any = false;
        for (int act = 0; act < maps.Length; act++)
        {
            if (maps[act] == null || maps[act].Count == 0) continue;
            any = true;
            container.AddChild(InfoLabel("[b]" + actTitles[act] + "[/b]"));
            foreach (var map in maps[act]) AddMapWithPicks(container, act, map);
        }
        if (!any) container.AddChild(InfoLabel("（没读到任何地图模型）"));
    }

    // ── A4：与原本 Boss 共用匹配池（用户 2026-10-29 新增） ────────────

    /// <summary>
    /// A4 区块。**排在最末（A3 之后）**：编号就是显示顺序 —— 用户 2026-10-29
    /// 「你这什么神秘排列顺序…把顺序调对」指的就是它以前被塞在 A1 里，画面上成了 1/4/2/3。
    /// </summary>
    private void BuildSharePoolSection(Control container)
    {
        container.AddChild(CreateSectionHeader("A4 · 与原本 Boss 共用匹配池"));
        container.AddChild(InfoLabel("开启后：勾了替换的地图不再「一定是角色 Boss」，而是从" +
                                     "「这张地图原本的 Boss」和「本模组的角色 Boss」里按本局种子随机抽一个。\n" +
                                     "抽取在一幕开始时就定好 ⇒ 地图预告与实际 Boss 一致；联机两端也一致。\n" +
                                     "多人时**以房主的这一项为准**（其它影响游戏的设置同理，音乐除外）。"));
        AddSharePoolRow(container);
        AddAsc10SplitRow(container);
    }

    /// <summary>A4 一行：勾选框 + 文字 + 当前状态。</summary>
    private void AddSharePoolRow(Control container)
    {
        bool enabled = BossConfig.SharePool;
        string head = "与原本 Boss 共用匹配池";

        var label = RowLabel(StateText2(head, enabled));

        var box = new CheckBoxButton();
        box.Setup(enabled);
        box.OnChanged = on =>
        {
            BossConfig.SetSharePool(on);
            try { label.Text = StateText2(head, on); } catch { /* 改字失败不影响功能 */ }
        };

        AddInlineRow(container, box, label, "SharePoolRow");
    }

    /// <summary>
    /// A4 的子项（用户 2026-10-29 追加）：**进阶 10 第三幕固定「一场原本 Boss + 一场角色 Boss」**。
    /// 需要混匹开启才生效 —— 混匹关着的时候这一行只显示说明文字（勾上也暂不生效）。
    /// </summary>
    private void AddAsc10SplitRow(Control container)
    {
        bool raw = BossConfig.Asc10Act3OneEachRaw;
        string head = "进阶 10 第三幕固定「一场原本 + 一场角色」";

        var label = RowLabel(StateText3(head, raw));

        var box = new CheckBoxButton();
        box.Setup(raw);
        box.OnChanged = on =>
        {
            BossConfig.SetAsc10Act3OneEach(on);
            try { label.Text = StateText3(head, on); } catch { /* 改字失败不影响功能 */ }
        };

        AddInlineRow(container, box, label, "Asc10SplitRow");
    }

    /// <summary>
    // ── B1 · 斗蛐蛐 ───────────────────────────────────────

    /// <summary>
    /// B1：斗蛐蛐专用的几项（用户 2026-11-08）。
    /// **只有装了怪物乱斗（CricketFightMod）才显示这一节**，否则整个不出现。
    /// </summary>
    private void BuildCricketSection(Control container)
    {
        container.AddChild(CreateSectionHeader("B1 · 斗蛐蛐（怪物乱斗）"));
        container.AddChild(InfoLabel(
            "这一节只在装了「怪物乱斗 / 斗蛐蛐（CricketFightMod）」时出现，调的是**斗蛐蛐战斗里**这套 UI 的表现。\n" +
            "说明面板：左右上角那块小面板，按住可以拖动，位置会记住。\n" +
            "出牌动画：Boss 打出一张牌时，那张牌从手牌栏飞向屏幕中间再淡出的节奏；\n" +
            "默认 200 / 180 / 200 毫秒就是原版 AnimCardToPlayPile 的参数。"));

        AddNumberRow(container, "CricketFontRow", BossConfig.CricketPanelFontSize, 9, 28, 1,
            v => BossConfig.CricketPanelFontSize = v,
            v => $"说明文字字号：{v}（9~28）");

        AddToggleRow(container, "CricketFxRow", BossConfig.CricketFxEnabled,
            "斗蛐蛐里播放 Boss 出牌动画",
            v => BossConfig.CricketFxEnabled = v);

        AddNumberRow(container, "CricketFlyRow", BossConfig.CricketFxFlyMs, 80, 600, 20,
            v => BossConfig.CricketFxFlyMs = v,
            v => $"出牌动画飞行时长：{v} 毫秒（越小越快）");

        AddNumberRow(container, "CricketHoldRow", BossConfig.CricketFxHoldMs, 0, 600, 20,
            v => BossConfig.CricketFxHoldMs = v,
            v => $"出牌动画停留时长：{v} 毫秒（0 = 不停顿）");

        AddNumberRow(container, "CricketFadeRow", BossConfig.CricketFxFadeMs, 60, 500, 20,
            v => BossConfig.CricketFxFadeMs = v,
            v => $"出牌动画淡出时长：{v} 毫秒");

        AddToggleRow(container, "CricketOstyFlipRow", BossConfig.CricketOstyFlip,
            "奥斯提（骨妹召唤物）额外翻转一次：画反了才勾",
            v => BossConfig.CricketOstyFlip = v);
    }

    /// <summary>B1 的一行数字：左边 [−][+]，右边说明 + 当前值。</summary>
    private void AddNumberRow(Control container, string rowName, int value, int min, int max, int step,
                              Action<int> setter, Func<int, string> describe)
    {
        int current = Math.Clamp(value, min, max);
        var label = RowLabel(describe(current));

        var ctl = new HBoxContainer { Name = rowName };
        ctl.AddThemeConstantOverride("separation", 10);

        var minus = new Button { Text = "−", CustomMinimumSize = new Vector2(46, 36) };
        var plus = new Button { Text = "+", CustomMinimumSize = new Vector2(46, 36) };
        ctl.AddChild(minus);
        ctl.AddChild(plus);

        minus.Pressed += () =>
        {
            current = Math.Clamp(current - step, min, max);
            setter(current);
            try { label.Text = describe(current); } catch { /* 改字失败不影响功能 */ }
        };
        plus.Pressed += () =>
        {
            current = Math.Clamp(current + step, min, max);
            setter(current);
            try { label.Text = describe(current); } catch { /* 改字失败不影响功能 */ }
        };

        AddInlineRow(container, ctl, label, rowName + "Inline");
    }

    /// <summary>B1 的一行开关：勾选框 + 说明。</summary>
    private void AddToggleRow(Control container, string rowName, bool value, string head, Action<bool> setter)
    {
        var label = RowLabel(StateText2(head, value));
        var box = new CheckBoxButton();
        box.Setup(value);
        box.OnChanged = on =>
        {
            setter(on);
            try { label.Text = StateText2(head, on); } catch { /* 改字失败不影响功能 */ }
        };
        AddInlineRow(container, box, label, rowName + "Inline");
    }

    /// A5（用户 2026-10-30 新增）：**本地对战数据记录**，默认关。
    /// 打开 = 知情同意：只把「我们的 Boss 战结果」与「我们自己的报错行」写进本机文件，
    /// 不联网、不收集身份信息。想帮作者就把文件发出去。
    /// </summary>
    private void BuildTelemetrySection(Control container)
    {
        container.AddChild(CreateSectionHeader("A5 · 本地对战数据（默认关）"));
        container.AddChild(InfoLabel(
            "开启后：每场**本模组的 Boss 战**结束时，把结果记一行到本机文件 ——\n" +
            "幕/场、遭遇、进阶、人数、Boss 血量与剩余、回合数、胜负、本局种子，以及当时几项设置；\n" +
            "同时把我们自己的异常 / 失败日志也记一份，方便作者做强度平衡与修 bug。\n" +
            "**不收集 SteamID 或昵称**；随时可以关掉（关掉后不再写任何数据）。\n" +
            "本机文件 → " + CharacterBosses.Core.Telemetry.DescribePath() + "\n" +
            "上传去处（作者在 charbosses_config.cfg 里配，默认全空 = 不联网）：telemetryUrl（自建/Worker）/\n" +
            "feishuBotUrl（飞书群机器人）/ feishuAppId+appSecret+bitableAppToken+tableId（飞书多维表格）。"));
        AddTelemetryRow(container);
        AddTelemetryUploadRow(container);
        AddTelemetryExportRow(container);
    }

    /// <summary>
    /// A6 一行：**要不要把数据上传给作者**（用户 2026-10-30：「不上传我怎么平衡」）。
    /// 只有在作者（模组配置里）填了收集端点时才可能打开；没填就只在本机记录。
    /// </summary>
    private void AddTelemetryUploadRow(Control container)
    {
        bool configured = BossConfig.AnyUploadEndpoint;
        bool enabled = BossConfig.TelemetryUpload;
        string head = "上传游玩数据给作者（匿名对战数据）";

        string State() => head + (!configured
            ? "　[color=#8a8a8a]（作者未配置收集端点 ⇒ 只在本机记录）[/color]"
            : enabled
                ? "　[b]开（上传到作者配置的收集端点）[/b]"
                : "　[color=#8a8a8a]关（只在本机记录）[/color]");

        var label = RowLabel(State());

        var box = new CheckBoxButton();
        box.Setup(enabled);
        box.OnChanged = on =>
        {
            BossConfig.SetTelemetryUpload(on);
            try { label.Text = State(); } catch { /* 改字失败不影响功能 */ }
        };

        AddInlineRow(container, box, label, "TelemetryUploadRow");
    }

    /// <summary>「把数据文件复制到桌面」——不想自动上传的玩家可以手动发给作者。</summary>
    private void AddTelemetryExportRow(Control container)
    {
        string head = "把数据文件复制到桌面（方便手动发给作者）";

        var label = RowLabel(head);

        var box = new CheckBoxButton();
        box.Setup(false);
        box.OnChanged = on =>
        {
            try { box.Setup(false); } catch { /* 忽略 */ }
            if (!on) return;
            var path = CharacterBosses.Core.Telemetry.ExportToDesktop();
            try
            {
                label.Text = path == null
                    ? head + "　[color=#8a8a8a]（还没有数据文件）[/color]"
                    : head + "　[b]已导出[/b]";
            }
            catch { /* 忽略 */ }
        };

        AddInlineRow(container, box, label, "TelemetryExportRow");
    }

    /// <summary>A5 一行：开关 + 状态文字。</summary>
    private void AddTelemetryRow(Control container)
    {
        bool enabled = BossConfig.TelemetryEnabled;
        string head = "记录本地对战数据（帮助作者平衡与修 bug）";

        var label = RowLabel(head + (enabled
            ? "　[b]开（写入本机文件）[/b]"
            : "　[color=#8a8a8a]关（不记录任何数据）[/color]"));

        var box = new CheckBoxButton();
        box.Setup(enabled);
        box.OnChanged = on =>
        {
            BossConfig.SetTelemetry(on);
            try
            {
                label.Text = head + (on
                    ? "　[b]开（写入本机文件）[/b]"
                    : "　[color=#8a8a8a]关（不记录任何数据）[/color]");
            }
            catch { /* 改字失败不影响功能 */ }
        };

        AddInlineRow(container, box, label, "TelemetryRow");
    }

    /// <summary>
    /// A4 的状态文字。
    /// ⚠ 用户 2026-10-29：「你这描述怎么前后矛盾？不勾应该是一定是角色 boss」
    /// —— 以前关的时候写成「勾了就一定是角色 Boss」，正好说反了。现在：
    ///   不勾 = **一定是角色 Boss**（就是完全替换）；勾上 = 原版 Boss 与角色 Boss 混抽。
    /// </summary>
    private static string StateText2(string head, bool enabled)
        => head + (enabled
            ? "　[b]开（原版 Boss 与角色 Boss 混抽）[/b]"
            : "　[color=#8a8a8a]关（一定是角色 Boss）[/color]");

    /// <summary>A4 子项的状态文字：混匹没开时明确写「暂不生效」。</summary>
    private static string StateText3(string head, bool enabled)
    {
        string state = enabled
            ? "　[b]开（第三幕一场原版、一场角色）[/b]"
            : "　[color=#8a8a8a]关（两场各抽各的）[/color]";
        if (!BossConfig.SharePool)
        {
            state += "　[color=#8a8a8a]（需要开启「与原本 Boss 共用匹配池」才生效）[/color]";
        }
        return head + state;
    }


    /// <summary>行文字 + 当前状态（文字里也写一遍：万一图没加载出来，还能看出开关）。</summary>
    private static string StateText(string head, bool enabled)
        => head + (enabled ? "　[b]已替换[/b]" : "　[color=#8a8a8a]保持原版[/color]");

    /// <summary>
    /// **能打勾的方框** —— 直接用游戏自己的两张勾选框贴图：
    /// `res://images/atlases/ui_atlas.sprites/checkbox_ticked.tres` / `checkbox_unticked.tres`
    /// （就是 `res://scenes/ui/tickbox.tscn` 里用的那两张，也就是原版设置界面里那个方框）。
    ///
    /// 为什么不用原版的 `NTickbox` / BaseLib 的 `NConfigTickbox`：
    ///   · `new NTickbox()` **没有视觉子节点**（`_imageContainer` / `_tickedImage` / `_notTickedImage`
    ///     都来自场景），`ConnectSignals()` 在里面直接空引用 ⇒ 画面上**一个像素都不出**（实测多轮）；
    ///   · `NConfigTickbox.Initialize` 需要一个编译期的 `PropertyInfo`，而我们的地图清单是**动态**的；
    ///   · 而且 BaseLib 的 `NConfigOptionRow` 把控件排在行尾，这个窗口尺寸下控件落在屏幕外
    ///     （实测 x=610..1694），根本点不到。
    /// 用一颗普通的 `Button`（`Flat` + 两张贴图切换）就没有这些坑：一定是原版那张图、
    /// 一定在屏幕里、点击一定生效（`Toggled` 是引擎派发的原生信号）。
    /// </summary>
    private sealed class CheckBoxButton : Button
    {
        private const string TickedTexPath = "res://images/atlases/ui_atlas.sprites/checkbox_ticked.tres";
        private const string UntickedTexPath = "res://images/atlases/ui_atlas.sprites/checkbox_unticked.tres";

        internal Action<bool>? OnChanged;

        private Texture2D? _ticked;
        private Texture2D? _unticked;

        internal void Setup(bool initial, float size = 64f)
        {
            ToggleMode = true;
            Flat = true;
            Text = string.Empty;
            CustomMinimumSize = new Vector2(size, size);
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
            FocusMode = Control.FocusModeEnum.All;

            try { _ticked = ResourceLoader.Load<Texture2D>(TickedTexPath); }
            catch (Exception e) { Logger.Log("[CharBoss] 勾选框贴图（已勾）加载失败: " + e.Message); }
            try { _unticked = ResourceLoader.Load<Texture2D>(UntickedTexPath); }
            catch (Exception e) { Logger.Log("[CharBoss] 勾选框贴图（未勾）加载失败: " + e.Message); }

            ButtonPressed = initial;
            Icon = initial ? _ticked : _unticked;
            // ★ 让贴图**跟着按钮尺寸缩放** —— 否则 Icon 的自然尺寸会把「想缩小的格子」又撑回去。
            ExpandIcon = true;

            Toggled += on =>
            {
                Icon = on ? _ticked : _unticked;
                if (_silent) return;
                try { OnChanged?.Invoke(on); } catch { /* 忽略 */ }
            };
        }

        private bool _silent;

        /// <summary>**静默**改状态（不触发 OnChanged）：用于「这个勾不能取消，视觉上弹回去」。</summary>
        internal void SetSilently(bool on)
        {
            _silent = true;
            try
            {
                ButtonPressed = on;
                Icon = on ? _ticked : _unticked;
            }
            finally { _silent = false; }
        }
    }

    // ── A1 的勾选格（v0.2.454）─────────────────────────────

    /// <summary>
    /// **列次序：猎 → 机 → 骨 → 储 → 战**（与 `BossKeys.RosterFor` 的候选顺序一致，
    /// 也和用户给的第三幕样例一致）。同一列 = 同一个角色，一个角色本幕有几只就竖着排几只。
    /// </summary>
    private static readonly int[] PickColumns = { 1, 2, 3, 4, 0 };

    /// <summary>每格名字列的固定宽度 —— 让「王储」「刀猎」这种两个字的名字也能和三个字的对齐。</summary>
    private const float PickNameWidth = 88f;

    /// <summary>勾选格的方框尺寸（勾选格用的小号；地图那一行仍是默认 64）。</summary>
    private const float PickBoxSize = 46f;

    /// <summary>一张地图：先一行「地图名（勾选框）　已选 N/M」，紧跟一个 5 列 × 若干行的 Boss 勾选格。</summary>
    private void AddMapWithPicks(Control container, int act, BossConfig.MapInfo map)
    {
        string entry = map.Entry;
        int total = BossKeys.RosterFor(act).Length;

        var label = RowLabel(MapRowHead(act, map, entry, total));
        var box = new CheckBoxButton();
        box.Setup(map.Enabled);
        box.OnChanged = on =>
        {
            if (_initializing) return;
            BossConfig.SetMapEnabled(entry, on);
            try { label.Text = MapRowHead(act, map, entry, total); } catch { /* 改字失败不影响功能 */ }
        };
        AddInlineRow(container, box, label, "MapRow_" + entry);

        // 5 列 × 最多 3 行：行优先填，空位用等宽占位，保证每一列都对得齐
        var cols = new List<string>[PickColumns.Length];
        int rows = 1;
        for (int i = 0; i < PickColumns.Length; i++)
        {
            var list = new List<string>();
            foreach (var key in BossKeys.RosterFor(act))
            {
                if (BossKeys.CharacterOf(key) == PickColumns[i]) list.Add(key);
            }
            cols[i] = list;
            if (list.Count > rows) rows = list.Count;
        }

        var grid = new GridContainer { Columns = PickColumns.Length };
        try
        {
            grid.AddThemeConstantOverride("h_separation", 4);
            grid.AddThemeConstantOverride("v_separation", 2);
        }
        catch { /* 改不了主题常量也无所谓 */ }

        for (int row = 0; row < rows; row++)
        {
            for (int i = 0; i < cols.Length; i++)
            {
                var list = cols[i];
                if (row >= list.Count)
                {
                    grid.AddChild(new Control { CustomMinimumSize = new Vector2(PickNameWidth + PickBoxSize + 6f, 0f) });
                    continue;
                }
                grid.AddChild(BuildPickCell(act, entry, list[row]));
            }
        }
        // ★ 2026-11-06：外面套一层**横向滚动条** —— 5 列已经能一行放下了，但窗口更窄/字体更大时
        //   还能左右拖，不至于把最后一列挤掉。
        var scroll = new ScrollContainer
        {
            HorizontalScrollMode = ScrollContainer.ScrollMode.Auto,
            VerticalScrollMode = ScrollContainer.ScrollMode.Disabled,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        grid.SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin;
        scroll.AddChild(grid);
        container.AddChild(scroll);
    }

    /// <summary>一个格子：`名字（勾选框）`；名字列定宽 + 右对齐 ⇒ 两个字的名字也不会把勾选框推歪。</summary>
    private Control BuildPickCell(int act, string entry, string key)
    {
        var cell = new HBoxContainer();
        try { cell.AddThemeConstantOverride("separation", 2); } catch { /* 忽略 */ }

        string capturedKeyForTip = key;
        var label = RowLabel(BossKeys.DesignNameOf(key));
        label.CustomMinimumSize = new Vector2(PickNameWidth, PickBoxSize);
        label.HorizontalAlignment = HorizontalAlignment.Right;
        AttachBossInfoTip(label, capturedKeyForTip);
        // ★ 2026-11-06 实测：默认会自动换行 ⇒ 「奇巧/猎」被折成两行。这里**关掉自动换行**，
        //   再配合下面加宽的名字列，三个字一定在同一行。
        try { label.AutowrapMode = TextServer.AutowrapMode.Off; } catch { /* 老版本没有这个属性也无所谓 */ }
        cell.AddChild(label);

        bool on = BossSelection.IsPicked(act, entry, key, forUi: true);
        var box = new CheckBoxButton();
        box.Setup(on, PickBoxSize);
        box.OnChanged = changed =>
        {
            if (_initializing) return;
            if (!BossSelection.SetPicked(act, entry, key, changed))
            {
                box.SetSilently(!changed);          // 被「每角色至少 N 个」挡住 ⇒ 视觉上弹回去
            }
        };
        cell.AddChild(box);
        return cell;
    }

    /// <summary>地图那一行的文字：`密林（OVERGROWTH）　已选 7/7　已替换/保持原版`。</summary>
    private static string MapRowHead(int act, BossConfig.MapInfo map, string entry, int total)
    {
        int picked = total;
        try { picked = BossSelection.AvailableForUi(act, entry).Length; } catch { /* 用总数 */ }
        string state = map.Enabled ? "[b]已替换[/b]" : "[color=#8a8a8a]保持原版[/color]";
        return $"[b]{map.DisplayName}[/b]（{entry}）　已选 {picked}/{total}　{state}";
    }
    // ── Boss 信息框（用户 2026-11-07：鼠标停在名字上，用原版说明框显示）──────

    /// <summary>鼠标停在名字上 → 用**原版说明框**（`NHoverTipSet`）显示这个 Boss 的信息。</summary>
    private static void AttachBossInfoTip(Control anchor, string key)
    {
        try
        {
            string? body = BossInfoTable.TextOf(key);
            if (string.IsNullOrEmpty(body)) return;

            // 标题用她那只怪物的名字（原版框需要一个 LocString）；正文是用户给的字段表。
            string monsterKey = key switch
            {
                var k when k.StartsWith("SILENT_") => "SILENT_BOSS",
                var k when k.StartsWith("NECROBINDER_") => "NECROBINDER_BOSS",
                var k when k.StartsWith("DEFECT_") => "DEFECT_BOSS",
                var k when k.StartsWith("IRONCLAD_") => "IRONCLAD_BOSS",
                var k when k.StartsWith("REGENT_") => "REGENT_BOSS",
                _ => "SILENT_BOSS",
            };

            bool shown = false;
            void Show()
            {
                try
                {
                    if (shown) return;
                    var title = new MegaCrit.Sts2.Core.Localization.LocString("monsters", monsterKey + ".name");
                    var tip = new MegaCrit.Sts2.Core.HoverTips.HoverTip(
                        title, BossKeys.DesignNameOf(key) + "\n" + body, null);
                    MegaCrit.Sts2.Core.Nodes.HoverTips.NHoverTipSet.CreateAndShow(
                        anchor, new MegaCrit.Sts2.Core.HoverTips.IHoverTip[] { tip },
                        MegaCrit.Sts2.Core.HoverTips.HoverTipAlignment.Right);
                    shown = true;
                }
                catch (Exception e) { Logger.Log("[CharBoss] 配置界面：Boss 信息框显示失败: " + e.Message); }
            }
            void Hide()
            {
                try
                {
                    if (!shown) return;
                    MegaCrit.Sts2.Core.Nodes.HoverTips.NHoverTipSet.Remove(anchor);
                    shown = false;
                }
                catch { /* 忽略 */ }
            }

            anchor.MouseEntered += Show;
            anchor.MouseExited += Hide;
            // 键盘/手柄也行：拿焦点就显示
            anchor.FocusEntered += Show;
            anchor.FocusExited += Hide;
            anchor.MouseFilter = Control.MouseFilterEnum.Pass;
        }
        catch (Exception e) { Logger.Log("[CharBoss] 配置界面：Boss 信息框挂载失败: " + e.Message); }
    }
    // ── A3：Boss 战 BGM 来源 ──────────────────────────────

    private void BuildBgmSection(Control container)
    {
        container.AddChild(CreateSectionHeader("A3 · Boss 战 BGM 来源"));

        int current = BossConfig.BgmSource;
        AddBgmRow(container, "① 原版其他 Boss 的音乐（默认）", BossConfig.BgmVanillaOtherBoss, current);
        AddBgmRow(container, "② 一代 Boss 战的音乐", BossConfig.BgmGen1Boss, current);

        // ③ 锁定关闭：只显示，不给按钮
        container.AddChild(InfoLabel("[color=#8a8a8a]③ 本模组自制的音乐 —— 不可用（暂无）[/color]"));
    }

    private void AddBgmRow(Control container, string text, int value, int current)
    {
        bool selected = current == value;
        var label = RowLabel(text + (selected ? "　[b]当前[/b]" : ""));

        // 三选一也用同一种「原版勾选框」：点一下就把别的取消（radio 行为），
        // 好处是**一定看得见、一定点得到**（BaseLib 的按钮控件同样有「没有视觉子节点」的风险）。
        var box = new CheckBoxButton();
        box.Setup(selected);
        box.OnChanged = on =>
        {
            if (_initializing) return;
            if (!on) { Refresh(container); return; }      // 不许把「当前」取消成空，重新画回选中态
            BossConfig.BgmSource = value;
            Refresh(container);                            // 重建一屏，别的那两颗自动取消
        };

        AddInlineRow(container, box, label, "BgmRow_" + value);
    }

    /// <summary>清空容器再重建（BaseLib 给的容器就是个普通 VBox，重建是安全的）。</summary>
    private void Refresh(Control container)
    {
        try
        {
            foreach (var child in container.GetChildren())
            {
                if (child is not Node node) continue;
                container.RemoveChild(node);
                node.QueueFree();
            }
            SetupConfigUI(container);
        }
        catch (Exception e) { Logger.Log("[CharBoss] 配置界面刷新失败: " + e.Message); }
    }
}
