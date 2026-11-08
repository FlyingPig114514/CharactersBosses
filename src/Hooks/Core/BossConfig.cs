using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using MegaCrit.Sts2.Core.Models;

namespace CharacterBosses.Core;

/// <summary>
/// **本模组的配置**（用户 2026-10-28 定稿，规格见 <c>docs/plans/config-ui-and-bgm.md</c> 的 A1/A2/A3）。
///
/// 三项：
///   · **A1 希望被替换的地图**：按 3 幕列出**当前所有**地图（动态从 `ModelDb.ActsByIndex` 取，
///     不能写死 4 个 —— 装了增加地图的模组要能列出来）。开启 = 那张地图的 Boss 会被替换。
///   · **A2 同一幕的 Boss 混合匹配**：默认开、**锁定不可关**（一层只有 5 个 Boss，不够单独匹配）。
///   · **A3 Boss 战 BGM 来源**：①原版其他 Boss 的音乐（默认）②一代 Boss 音乐 ③自制音乐（锁定关闭）。
///
/// 存盘位置：DLL 同目录的 <c>charbosses_config.json</c>（和 <c>charbosses_debug.txt</c> 同一套约定）。
/// 文件不存在 / 读坏了 ⇒ 全部走默认值，绝不因为配置问题影响游戏启动。
/// </summary>
public static class BossConfig
{
    /// <summary>A3 选项①：使用原版其他 Boss 的音乐（默认）。</summary>
    public const int BgmVanillaOtherBoss = 1;

    /// <summary>A3 选项②：使用一代 Boss 战的音乐（三首 ogg，随 PCK 打包）。</summary>
    public const int BgmGen1Boss = 2;

    /// <summary>A3 选项③：使用本模组自制的音乐 —— **锁定关闭**（目前没有自制音乐）。</summary>
    public const int BgmOwn = 3;

    /// <summary>
    /// 配置文件名（DLL 同目录）。
    /// ⚠ **不能用 .json**：游戏的模组扫描会把 mod 目录里**每个** *.json 当成清单去反序列化，
    /// 我们这份不是清单 ⇒ 每次启动都在 godot.log 里刷一条
    /// `Caught System.Text.Json.JsonException trying to deserialize mod manifest json`（用户 2026-10-29 日志实证）。
    /// 所以换成 .cfg（内容仍是 JSON，BaseLib 的配置也用的是 .cfg）。
    /// </summary>
    public const string FileName = "charbosses_config.cfg";

    /// <summary>旧文件名（2026-10-28 及更早用的是 .json）：只读兼容，读到就迁到新名字。</summary>
    private const string LegacyFileName = "charbosses_config.json";

    /// <summary>A2「同幕混合匹配」——**永远为 true**，不可关（用户口径）。</summary>
    public static bool MixedMatching => true;

    // ── 存盘结构 ───────────────────────────────────────────

    private sealed class Data
    {
        /// <summary>
        /// 存盘结构版本。
        ///   1 = 早期（**所有**地图默认「开」）
        ///   2 = 2026-10-29 起（用户装了「往昔之章」等加地图的模组）：**只给原版四张图默认「开」**，
        ///       模组加出来的地图默认「关」，需要玩家在「模组配置」里手动打开。
        /// 读到旧版（&lt;2）时做一次迁移：非原版地图全部改回「关」。
        /// </summary>
        [JsonPropertyName("version")] public int Version { get; set; } = 2;

        /// <summary>
        /// 地图 id（`ActModel.Id.Entry`）→ 是否替换该地图的 Boss。
        /// 缺项时的默认值见 <see cref="IsMapEnabledById"/>（原版图 = 开、其余 = 关）。
        /// </summary>
        [JsonPropertyName("maps")] public Dictionary<string, bool> Maps { get; set; } = new();

        /// <summary>A2。**只读**：写盘时永远是 true，读盘时忽略文件里的值。</summary>
        [JsonPropertyName("mixedMatching")] public bool MixedMatching { get; set; } = true;

        /// <summary>
        /// A2（用户 2026-11-06 改版）：**每张地图允许出现哪些 Boss**（Key 数组）。
        ///
        /// · 键 = 地图 id（`ActModel.Id.Entry`，和 A1 同一套 id）；值 = 该地图允许的 Key 列表；
        /// · **缺项 = 该地图全选**（等价于以前的行为，旧配置文件不受影响）；
        /// · 只存用户真正改过的地图，配置保持干净。
        /// · 校验：每张地图里**每个角色至少 1 个**（第三幕至少 2 个），见 `Core/BossSelection.cs`。
        /// </summary>
        [JsonPropertyName("bossPicks")] public Dictionary<string, List<string>> BossPicks { get; set; } = new();

        /// <summary>A3：1 = 原版其他 Boss / 2 = 一代 Boss / 3 = 自制（锁定关闭，按 1 处理）。</summary>
        [JsonPropertyName("bgmSource")] public int BgmSource { get; set; } = BgmVanillaOtherBoss;

        /// <summary>
        /// A4（用户 2026-10-29 新增）：**与原本 Boss 共用匹配池**。
        /// 开启后，开了替换的地图不再「单纯替换」，而是从「这张地图原本的 Boss」和
        /// 「这一幕本模组的角色 Boss」里按种子随机抽一个。
        /// </summary>
        [JsonPropertyName("sharePool")] public bool SharePool { get; set; }

        /// <summary>
        /// A4 的子项（用户 2026-10-29 追加）：**进阶 10 第三幕固定「一场原本 Boss + 一场角色 Boss」**。
        /// 需要 <see cref="SharePool"/> 开启才生效；开启后进阶 10 第三幕的两场 Boss 战不再各抽各的，
        /// 而是一场原版、一场本模组（哪一场是原版由种子决定）。
        /// </summary>
        [JsonPropertyName("asc10Act3OneEach")] public bool Asc10Act3OneEach { get; set; }

        /// <summary>
        /// A5（用户 2026-10-30 新增）：**本地对战数据记录**（默认关）。
        /// 开了才把 Boss 战结果与我们的报错行写进本机文件，帮助作者平衡与修 bug；
        /// **只写本机、不联网**。详见 <see cref="Telemetry"/>。
        /// </summary>
        [JsonPropertyName("telemetry")] public bool Telemetry { get; set; }

        /// <summary>
        /// 首次启动时那条「要不要记录本地游玩数据」的弹窗**问过没有**（用户 2026-10-30）。
        /// 问过一次就不再打扰（无论玩家选了是还是否）。
        /// </summary>
        /// <summary>
        /// B1（用户 2026-11-08）：斗蛐蛐里的说明面板**字号**（默认 13，范围 9~28）。
        /// 只有装了「怪物乱斗（CricketFightMod）」才会在配置界面上看到这一节。
        /// </summary>
        [JsonPropertyName("cricketPanelFontSize")] public int CricketPanelFontSize { get; set; } = 13;

        /// <summary>B1：斗蛐蛐里 Boss 出牌动画**飞向出牌区**的时长（毫秒，默认 200 = 原版参数）。</summary>
        [JsonPropertyName("cricketFxFlyMs")] public int CricketFxFlyMs { get; set; } = 200;

        /// <summary>B1：出牌动画在出牌区**停留**的时长（毫秒，默认 180）。</summary>
        [JsonPropertyName("cricketFxHoldMs")] public int CricketFxHoldMs { get; set; } = 180;

        /// <summary>B1：出牌动画**淡出**时长（毫秒，默认 200）。</summary>
        [JsonPropertyName("cricketFxFadeMs")] public int CricketFxFadeMs { get; set; } = 200;

        /// <summary>B1：斗蛐蛐里要不要播放 Boss 出牌动画。</summary>
        [JsonPropertyName("cricketFxEnabled")] public bool CricketFxEnabled { get; set; } = true;
        /// <summary>
        /// B1（用户 2026-11-08）：**把奥斯提立绘再翻一次**。
        /// 自动朝向是按「敌人在哪边」算的，如果某只 Boss 的召唤物画反了，勾上这个就能单独纠回来。
        /// </summary>
        [JsonPropertyName("cricketOstyFlip")] public bool CricketOstyFlip { get; set; }

        [JsonPropertyName("consentAsked")] public bool ConsentAsked { get; set; }

        /// <summary>
        /// 同意弹窗的**文本版本**（用户 2026-11-01）。
        /// 为什么要它：弹窗正文写的是「作者配置了收集服务器的话，同一份匿名数据**也会上传**给作者」。
        /// v0.2.262 之前模组里根本没有端点（见 <see cref="BuiltInAppId"/> 那一段注释），那句话当时是空头支票：
        /// 老玩家点过「同意」也只会写本机，`consentAsked` 又让他们永远看不到第二次弹窗。
        /// 现在端点内置了 ⇒ 把版本号抬到 <see cref="ConsentVersionCurrent"/>，让老玩家**再被问一次**，
        /// 问到的是「真的会上传」的版本；新玩家本来就会被问，不受影响。
        /// </summary>
        [JsonPropertyName("consentVersion")] public int ConsentVersion { get; set; }

        /// <summary>
        /// A6（用户 2026-10-30）：A5 记下来的数据**要不要上传给作者**。
        /// 只有当 <see cref="TelemetryUrl"/> 非空（作者配置了收集端点）时才可能为真。
        /// </summary>
        [JsonPropertyName("telemetryUpload")] public bool TelemetryUpload { get; set; }

        /// <summary>
        /// 收集端点（作者自己填，默认空 = 完全不上传、不联网）。
        /// 我们的 POST 体是一段 JSON（见 <see cref="Telemetry"/>），任何能收 HTTP POST 的地方都行：
        /// Google Apps Script Web App / Cloudflare Worker / 自建 nginx。
        /// </summary>
        [JsonPropertyName("telemetryUrl")] public string TelemetryUrl { get; set; } = "";

        /// <summary>
        /// A6 备选：**飞书群机器人 webhook**（用户 2026-10-30：国内打不开 Google 表格）。
        /// 零鉴权，直接把每条记录当一条文本消息发进群里，手机/桌面飞书都能看。
        /// 形如 `https://open.feishu.cn/open-apis/bot/v2/hook/xxxxxxxx`。
        /// </summary>
        [JsonPropertyName("feishuBotUrl")] public string FeishuBotUrl { get; set; } = "";

        /// <summary>A6 备选：**飞书多维表格**（结构化，能筛选/分组统计）。企业自建应用的 app_id。</summary>
        [JsonPropertyName("feishuAppId")] public string FeishuAppId { get; set; } = "";

        /// <summary>A6 备选：飞书自建应用的 app_secret（换取 tenant_access_token 用）。</summary>
        [JsonPropertyName("feishuAppSecret")] public string FeishuAppSecret { get; set; } = "";

        /// <summary>A6 备选：多维表格的 app_token（表格 URL 里 `/base/XXXX` 那一段）。</summary>
        [JsonPropertyName("feishuBitableAppToken")] public string FeishuBitableAppToken { get; set; } = "";

        /// <summary>A6 备选：多维表格的数据表 id（`table=XXXX` 或 tblXXXX）。</summary>
        [JsonPropertyName("feishuTableId")] public string FeishuTableId { get; set; } = "";

        /// <summary>
        /// A6 备选（用户 2026-10-30）：「杂项记录」（`diag` 问题诊断 / `consent` 同意记录）单独写进的
        /// **另一张数据表** id，同一个多维表格里即可。
        /// · 填了 → 杂项进这张表，主表（<see cref="FeishuTableId"/>）里**只有对局数据**；
        /// · 留空 → 杂项**只留在本机、不上传**（主表同样干净）。
        /// </summary>
        [JsonPropertyName("feishuDiagTableId")] public string FeishuDiagTableId { get; set; } = "";
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // ── 内置的收集端点（2026-11-07 改成「只放中转站 URL」，不再内嵌任何密钥）─────────
    //
    // 历史：v0.2.262~v0.2.460 把飞书 app_id / **app_secret** / app_token / table_id 用 base64
    //   写进 DLL 当默认值 —— 而 DLL 是工坊的公开文件 ⇒ **任何人反编译都能拿到 app_secret**。
    //   base64 不是加密，这条路本身就不安全（用户 2026-11-07 问「能不能搞个中转站」，就是它）。
    //
    // 现在改成：客户端只 know 一个**中转站 URL**（`https://…/`，不含任何密钥），
    //   中转站（Cloudflare Worker，见 `tools/relay/feishu-relay.js` 与 `docs/plans/feishu-relay.md`）
    //   拿着存在服务端环境变量里的 app_secret 去写飞书多维表格。
    //
    // ⚠ 部署好之后，把地址填到下面这个常量里再发版。
    //   留空 ⇒ 玩家端点没有任何内置值（A6 只会把数据写在本机），**不会**误传。
    //
    // ★ 2026-11-07 起**两种地址都认**（Cloudflare Worker 在国内连不上，见
    //   `docs/plans/feishu-relay-deploy-status.md`，实际采用飞书群机器人）：
    //   · 形如 `https://open.feishu.cn/open-apis/bot/v2/hook/xxxx` ⇒ 回填 `feishuBotUrl`
    //     （零鉴权，玩家只可能往一个群里发消息，拿不到任何飞书凭据）；
    //   · 其它 https 地址 ⇒ 回填 `telemetryUrl`（走中转站 / 自建服务）。
    private const string BuiltInRelayUrl =
        "https://open.feishu.cn/open-apis/bot/v2/hook/00501773-d3c0-4a95-a553-a2042fd88054";

    /// <summary>这个地址是不是飞书**群自定义机器人** webhook（零鉴权、只能发消息）。</summary>
    internal static bool LooksLikeFeishuBotUrl(string url)
        => !string.IsNullOrWhiteSpace(url)
           && url.IndexOf("/open-apis/bot/v2/hook/", StringComparison.OrdinalIgnoreCase) >= 0;

    /// <summary>同意弹窗的当前文本版本；老配置（&lt; 这个值）会被再问一次（见 <see cref="Data.ConsentVersion"/>）。</summary>
    internal const int ConsentVersionCurrent = 1;

    /// <summary>
    /// 把**中转站 URL** 回填进配置的 `telemetryUrl`（只填空值，绝不覆盖玩家自己填的）。
    ///
    /// 为什么必须回填：玩家的 `charbosses_config.cfg` 早就生成好了，里面 `telemetryUrl` 是空串；
    /// 反序列化会用文件里的空串覆盖类默认值 ⇒ 光改默认值对老玩家无效。
    /// </summary>
    private static void BackfillBuiltInEndpoints(Data data)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(BuiltInRelayUrl)) return;              // 还没配端点
            if (!BuiltInRelayUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                Logger.Log("[CharBoss] 配置：BuiltInRelayUrl 不是 https 地址，已忽略（请检查地址）");
                return;
            }

            // ★ 飞书群机器人：走 feishuBotUrl 这条路（零鉴权、只有「往一个群发消息」的权限）。
            if (LooksLikeFeishuBotUrl(BuiltInRelayUrl))
            {
                if (!string.IsNullOrWhiteSpace(data.FeishuBotUrl)) return;       // 玩家自己配了，别动
                data.FeishuBotUrl = BuiltInRelayUrl;
                _dirty = true;
                Logger.Log("[CharBoss] 配置：已写入数据收集用的**飞书群机器人**地址" +
                           "（零鉴权，客户端不持有任何飞书密钥）" +
                           " ⇒ 在弹窗里选「开启」后，匿名对战数据会发给作者；不想参与就在 模组配置 → A5 关掉");
                return;
            }

            if (!string.IsNullOrWhiteSpace(data.TelemetryUrl)) return;           // 玩家自己配了，别动

            data.TelemetryUrl = BuiltInRelayUrl;
            _dirty = true;
            Logger.Log("[CharBoss] 配置：已写入数据收集的**中转站**地址（客户端不再持有飞书 app_secret）" +
                       " ⇒ 在弹窗里选「开启」后，匿名对战数据会发给作者；不想参与就在 模组配置 → A5 关掉");
        }
        catch (Exception e) { Logger.Log("[CharBoss] 配置：回填端点地址失败（忽略）: " + e.Message); }
    }

    private static Data? _data;
    private static bool _dirty;

    /// <summary>已经解析好的配置文件绝对路径（只解析一次）。</summary>
    private static string? _resolvedPath;

    /// <summary>
    /// 配置文件路径。
    ///
    /// ⚠ 用户 2026-10-29（要紧的一条）：**不能只往模组目录里写** ——
    /// 以后上创意工坊，玩家的模组目录在 `steamapps/workshop/content/...`（不是 `mods\`），
    /// 那里的写入随时可能被更新覆盖、或者是只读的。所以改成**优先写到「本机的用户数据目录」**，
    /// 依次尝试：
    ///   1. `%APPDATA%\SlayTheSpire2\mods\CharacterBosses\`（游戏自己的用户数据区，日志也在这里；
    ///      创意工坊更新模组不会动它）；
    ///   2. Godot 的 `user://` 目录（`ProjectSettings.GlobalizePath("user://")`）；
    ///   3. 兜底才是 DLL 所在目录（以前的行为，仅当上面都不可写时）。
    /// 每张候选目录都会先做一次「写一个临时文件再删掉」的可写性测试。
    /// </summary>
    private static string FilePath => _resolvedPath ??= ResolveFilePath();

    private static string ResolveFilePath()
    {
        foreach (var dir in CandidateDirs())
        {
            try
            {
                Directory.CreateDirectory(dir);
                if (!IsWritable(dir)) continue;
                _resolvedPath = Path.Combine(dir, FileName);
                return _resolvedPath;
            }
            catch { /* 换下一个 */ }
        }
        try
        {
            var dllDir = Path.GetDirectoryName(typeof(BossConfig).Assembly.Location);
            if (!string.IsNullOrEmpty(dllDir)) return Path.Combine(dllDir!, FileName);
        }
        catch { /* 忽略 */ }
        return FileName;
    }

    /// <summary>候选目录（按优先级）。</summary>
    private static IEnumerable<string> CandidateDirs()
    {
        // ① 用户数据目录：`%APPDATA%\SlayTheSpire2\mod_configs\CharacterBosses\`
        //    （`mod_configs` 是 BaseLib 存各模组配置的地方，我们把子目录放这里，玩家好找、
        //      也不会和 BaseLib 自己的 `CharacterBosses.cfg` 撞名）
        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SlayTheSpire2", "mod_configs", "CharacterBosses");

        // ② Godot 的 user://（导出后通常就是 %APPDATA%\<游戏名>）
        string? userDir = null;
        try
        {
            userDir = Godot.ProjectSettings.GlobalizePath("user://");
        }
        catch { /* 拿不到就算了 */ }
        if (!string.IsNullOrEmpty(userDir))
        {
            yield return Path.Combine(userDir!, "mod_config", "CharacterBosses");
        }

        // ③ 兜底：DLL 所在目录（模组目录 / 创意工坊目录）
        string? dllDir = null;
        try { dllDir = Path.GetDirectoryName(typeof(BossConfig).Assembly.Location); }
        catch { /* 忽略 */ }
        if (!string.IsNullOrEmpty(dllDir)) yield return dllDir!;
    }

    /// <summary>这个目录能不能写（写一个临时文件再删）。</summary>
    private static bool IsWritable(string dir)
    {
        try
        {
            var probe = Path.Combine(dir, ".write_test_" + Guid.NewGuid().ToString("N"));
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
            return true;
        }
        catch { return false; }
    }

    /// <summary>给配置界面显示用：当前实际保存到哪个文件。</summary>
    internal static string DescribeConfigPath()
    {
        try { return FilePath; }
        catch { return FileName; }
    }

    /// <summary>
    /// 旧版文件名（`charbosses_config.json`）→ 新名字（`.cfg`）的一次性迁移；
    /// 同时把「以前写在模组目录里的 `.cfg`」搬到新的用户数据目录（模组更新不会覆盖用户设置）。
    /// 旧文件保留不删（万一用户还想手改），但**不删就等于继续被模组扫描器当成清单报错** ——
    /// 所以迁移成功后把旧的删掉。
    /// </summary>
    private static void TryMigrateLegacy()
    {
        try
        {
            var dllDir = Path.GetDirectoryName(typeof(BossConfig).Assembly.Location);
            if (string.IsNullOrEmpty(dllDir)) return;

            // 目标已经存在就不动（用户的现役配置优先）
            string target = FilePath;
            if (File.Exists(target)) return;

            foreach (var legacyName in new[] { FileName, LegacyFileName })
            {
                var legacy = Path.Combine(dllDir!, legacyName);
                if (!File.Exists(legacy)) continue;

                var text = File.ReadAllText(legacy);
                File.WriteAllText(target, text);
                try { File.Delete(legacy); } catch { /* 删不掉也不致命 */ }
                Logger.Log($"[CharBoss] 配置：已把 {legacyName}（旧位置：模组目录）迁移到 {target}" +
                           "（用户数据目录，创意工坊更新不会覆盖它）");
                return;
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 配置迁移失败（忽略）: " + e.Message);
        }
    }

    private static Data Current
    {
        get
        {
            // ★ v0.2.243：**加锁 + 只解析一次**。
            //   用户 2026-10-30 把 cfg 手工改坏（JSON 少逗号）之后，日志里 1 秒内出现 **411 条**同样的
            //   `配置读取失败` —— 因为「预载线程 + 主线程」同时第一次访问配置：大家都看到 `_data == null`
            //   ⇒ 各自 `Load()` 一遍 ⇒ 各自记一遍日志（连带几百次同步读盘）。
            //   现在第一次读在锁里完成，后来者直接拿结果；失败也只记一次。
            var cached = _data;
            if (cached != null) return cached;
            lock (_loadLock)
            {
                if (_data != null) return _data;
                _data = Load();
                return _data;
            }
        }
    }

    /// <summary>第一次解析配置时只用一条线程（避免多线程各解析一遍 + 日志风暴）。</summary>
    private static readonly object _loadLock = new();

    /// <summary>「配置读取失败」这条日志只记一次（坏文件不该刷屏）。</summary>
    private static int _loadFailureLogged;

    /// <summary>
    /// 主动把配置读一遍（顺便完成「旧 .json → 新 .cfg」的迁移）。
    /// 由主菜单的启动钩子调用：不然要等到「第一次生成地图 / 打开配置界面」才会读，
    /// 那之前旧的 `charbosses_config.json` 还躺在 mod 目录里被模组扫描器当成清单报错。
    /// </summary>
    public static void EnsureLoaded()
    {
        try
        {
            _ = Current;
            Save();
        }
        catch (Exception e) { Logger.Log("[CharBoss] 配置预加载失败（忽略）: " + e.Message); }
    }

    private static Data Load()
    {
        Data data;
        try
        {
            var path = FilePath;
            if (!File.Exists(path))
            {
                // 兼容旧版：2026-10-28 及更早存在 charbosses_config.json 里，读到就迁过来
                TryMigrateLegacy();
            }
            if (File.Exists(path))
            {
                var text = File.ReadAllText(path);
                data = JsonSerializer.Deserialize<Data>(text, JsonOptions) ?? new Data();
            }
            else
            {
                data = new Data();
            }
        }
        catch (Exception e)
        {
            // 只记一次（坏文件被反复解析会把日志刷爆，见 `Current` 上的注释）
            if (Interlocked.Increment(ref _loadFailureLogged) == 1)
            {
                Logger.Log("[CharBoss] 配置读取失败，全部用默认值: " + e.Message);
                // 顺手把坏掉的文件备份一份：玩家的手改内容不至于被下一次保存覆盖得干干净净
                try
                {
                    var broken = FilePath + ".broken-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                    File.Copy(FilePath, broken, overwrite: true);
                    Logger.Log("[CharBoss] 坏掉的配置文件已备份到 " + broken +
                               "（修好原文件里那处 JSON 逗号/引号，再重启游戏即可）");
                }
                catch { /* 备份失败不影响默认值兜底 */ }
            }
            data = new Data();
        }

        data.Maps ??= new Dictionary<string, bool>();
        data.MixedMatching = true;                   // A2 锁定
        if (data.BgmSource != BgmGen1Boss) data.BgmSource = BgmVanillaOtherBoss;   // ③ 锁定关闭

        // ★ v0.2.263（用户 2026-11-01）：把作者内置的收集端点补进配置（只填空值）。
        BackfillBuiltInEndpoints(data);

        // ★ 迁移到 schema 2（用户 2026-10-29：装了「往昔之章」等加地图的模组）：
        //   旧版（version 1）在补齐地图清单时把**所有**地图（含模组加的）都写成了「开」，
        //   于是新加的地图会被自动替换 Boss。这里一次性把**非原版地图**全部改回「关」，
        //   玩家想要就自己去「模组配置」里打开（那里也会显示成关）。
        if (data.Version < 2)
        {
            int changed = 0;
            try
            {
                foreach (var key in new List<string>(data.Maps.Keys))
                {
                    if (IsVanillaMap(key)) continue;
                    if (!data.Maps[key]) continue;
                    data.Maps[key] = false;
                    changed++;
                }
            }
            catch (Exception e) { Logger.Log("[CharBoss] 配置迁移（非原版地图默认关）失败: " + e.Message); }
            data.Version = 2;
            _dirty = true;
            if (changed > 0)
            {
                Logger.Log($"[CharBoss] 配置迁移：{changed} 张**非原版地图**的 Boss 替换已改回「关」" +
                           "（装了地图模组时这是默认行为；想替换请到「模组配置」里手动打开）");
            }
        }

        // 第一次跑（或装了新地图模组）：把当前所有地图补齐 —— **原版四张默认「开」、其余默认「关」**。
        try
        {
            bool added = false;
            foreach (var act in AllKnownActs())
            {
                string? entry = EntryOf(act);
                if (string.IsNullOrEmpty(entry)) continue;
                if (data.Maps.ContainsKey(entry!)) continue;
                data.Maps[entry!] = IsVanillaMap(entry!);
                added = true;
            }
            if (added) _dirty = true;
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 配置：补齐地图清单失败: " + e.Message);
        }

        // 第一次跑（或装了新地图模组）⇒ **立刻把补齐后的默认值落盘**。
        // 不落盘的话用户永远看不到这个文件、也就没法手改（踩过：只置 _dirty 不写，
        // 而 Save() 只在「用户改设置」时才被调用）。
        if (_dirty) { WriteSnapshot(data); _dirty = false; }

        Logger.Log($"[CharBoss] 配置：BGM 来源 = {DescribeBgmSource(data.BgmSource)}，" +
                   $"地图开关 {data.Maps.Count} 个（关掉的：" +
                   $"{string.Join(",", OffMaps(data))}）");
        return data;
    }

    private static IEnumerable<string> OffMaps(Data data)
    {
        var off = new List<string>();
        foreach (var kv in data.Maps) if (!kv.Value) off.Add(kv.Key);
        if (off.Count == 0) off.Add("无");
        return off;
    }

    /// <summary>数值 → 人话（写日志用）。</summary>
    public static string DescribeBgmSource(int source) => source switch
    {
        BgmVanillaOtherBoss => "①原版其他 Boss 的音乐",
        BgmGen1Boss => "②一代 Boss 音乐",
        BgmOwn => "③本模组自制音乐（锁定关闭，按①处理）",
        _ => "①原版其他 Boss 的音乐（未识别值，按①处理）",
    };

    // ── A3 ────────────────────────────────────────────────

    /// <summary>A3：Boss 战 BGM 来源。写 ③ 会被拒绝（锁定关闭）。</summary>
    public static int BgmSource
    {
        get => Current.BgmSource;
        set
        {
            int v = value == BgmGen1Boss ? BgmGen1Boss : BgmVanillaOtherBoss;
            if (v == Current.BgmSource) return;
            Current.BgmSource = v;
            _dirty = true;
            Save();
            Logger.Log($"[CharBoss] 配置：BGM 来源改为 {DescribeBgmSource(v)}");
        }
    }

    // ── B1 · 斗蛐蛐（用户 2026-11-08）──────────────────────
    // 只有装了「怪物乱斗（CricketFightMod）」时，配置界面才会出现这一节。

    /// <summary>说明面板字号（9~28）。</summary>
    public static int CricketPanelFontSize
    {
        get => Math.Clamp(Current.CricketPanelFontSize, 9, 28);
        set
        {
            int v = Math.Clamp(value, 9, 28);
            if (v == Current.CricketPanelFontSize) return;
            Current.CricketPanelFontSize = v;
            _dirty = true;
            Save();
            Logger.Log($"[CharBoss] 配置：斗蛐蛐说明字号 = {v}");
        }
    }

    /// <summary>出牌动画飞行时长（毫秒，80~600）。</summary>
    public static int CricketFxFlyMs
    {
        get => Math.Clamp(Current.CricketFxFlyMs, 80, 600);
        set
        {
            int v = Math.Clamp(value, 80, 600);
            if (v == Current.CricketFxFlyMs) return;
            Current.CricketFxFlyMs = v;
            _dirty = true;
            Save();
            Logger.Log($"[CharBoss] 配置：斗蛐蛐出牌动画飞行 = {v}ms");
        }
    }

    /// <summary>出牌动画停留时长（毫秒，0~600）。</summary>
    public static int CricketFxHoldMs
    {
        get => Math.Clamp(Current.CricketFxHoldMs, 0, 600);
        set
        {
            int v = Math.Clamp(value, 0, 600);
            if (v == Current.CricketFxHoldMs) return;
            Current.CricketFxHoldMs = v;
            _dirty = true;
            Save();
            Logger.Log($"[CharBoss] 配置：斗蛐蛐出牌动画停留 = {v}ms");
        }
    }

    /// <summary>出牌动画淡出时长（毫秒，60~500）。</summary>
    public static int CricketFxFadeMs
    {
        get => Math.Clamp(Current.CricketFxFadeMs, 60, 500);
        set
        {
            int v = Math.Clamp(value, 60, 500);
            if (v == Current.CricketFxFadeMs) return;
            Current.CricketFxFadeMs = v;
            _dirty = true;
            Save();
            Logger.Log($"[CharBoss] 配置：斗蛐蛐出牌动画淡出 = {v}ms");
        }
    }

    /// <summary>斗蛐蛐里是否播放出牌动画。</summary>
    public static bool CricketFxEnabled
    {
        get => Current.CricketFxEnabled;
        set
        {
            if (value == Current.CricketFxEnabled) return;
            Current.CricketFxEnabled = value;
            _dirty = true;
            Save();
            Logger.Log($"[CharBoss] 配置：斗蛐蛐出牌动画 = {(value ? "开" : "关")}");
        }
    }

    /// <summary>斗蛐蛐里把奥斯提立绘额外翻转一次（朝向画反时的手动纠正开关）。</summary>
    public static bool CricketOstyFlip
    {
        get => Current.CricketOstyFlip;
        set
        {
            if (value == Current.CricketOstyFlip) return;
            Current.CricketOstyFlip = value;
            _dirty = true;
            Save();
            Logger.Log($"[CharBoss] 配置：奥斯提额外翻转 = {(value ? "开" : "关")}");
        }
    }
    // ── A1 ────────────────────────────────────────────────

    /// <summary>
    /// 这张地图的 Boss 要不要被替换。
    ///
    /// ⚠ 用户 2026-10-29（装了「往昔之章」等会加地图的模组）：
    /// **原版地图默认开、非原版地图默认关**（玩家在「模组配置」里手动打开才会替换）。
    /// 认不出是哪张图时同样按「关」处理 —— 宁可不动别人的幕，也不要把别人的 Boss 换掉。
    /// </summary>
    public static bool IsMapEnabled(ActModel? act) => IsMapEnabledCore(act, allowLocal: true);

    /// <summary>客人端「本机不读玩法开关」时只记一次日志（避免每场刷）。</summary>
    private static readonly HashSet<string> GuestDefaultLogged = new(StringComparer.Ordinal);

    /// <summary>
    /// **玩法路径用**的地图开关（★ v0.2.468）：
    /// 单人 / 房主 = 读本机开关；**客人 = 不读本机开关**，改用「原版地图默认开、其它默认关」这条默认规则，
    /// 真正的结论由房主写进同步槽位（`act.BossEncounter` / `act.SecondBossEncounter`）——
    /// 客人一看到同步槽位是我们的遭遇就直接跟随（见 `CharBossEncounters.ForVanillaBoss`）。
    ///
    /// 为什么必须这样（用户 2026-11-07：「地图替换开关和共用匹配池也改成以房主为准，否则容易不同步」）：
    /// 以前客人这台机器是**读自己的 cfg** 决定「这张图替不替换」的 ⇒ 客人关着、房主开着，
    /// 就会一台替换一台不替换，轮次 1 立刻分叉。
    /// </summary>
    internal static bool IsMapEnabledForGameplay(ActModel? act)
        => IsMapEnabledCore(act, allowLocal: GameplaySettingsFollowHost);

    private static bool IsMapEnabledCore(ActModel? act, bool allowLocal)
    {
        try
        {
            string? entry = EntryOf(act);
            if (string.IsNullOrEmpty(entry)) return false;

            if (!allowLocal)
            {
                if (GuestDefaultLogged.Add(entry!))
                {
                    Logger.Log($"[CharBoss] 本机是**客人**⇒ 不读本机「地图替换开关」与「共用匹配池」，" +
                               $"一律以房主的同步槽位为准（地图 {entry} 按默认值：{(IsVanillaMap(entry!) ? "替换" : "不替换")}）");
                }
                return IsVanillaMap(entry!);
            }

            return IsMapEnabledById(entry);
        }
        catch { return false; }
    }

    /// <summary>玩法用的「共用匹配池」：★ v0.2.468 起**客人端恒 false**（以房主的同步槽位为准）。</summary>
    internal static bool SharePoolForGameplay => GameplaySettingsFollowHost && SharePool;

    /// <summary>玩法用的「进阶 10 第三幕一场原本一场角色」：客人端恒 false（同上）。</summary>
    internal static bool Asc10Act3OneEachForGameplay => GameplaySettingsFollowHost && Asc10Act3OneEach;

    /// <summary>按地图 id 设置开关（配置界面用）。</summary>
    public static void SetMapEnabled(string? mapEntry, bool enabled)
    {
        if (string.IsNullOrEmpty(mapEntry)) return;
        try
        {
            Current.Maps[mapEntry!] = enabled;
            _dirty = true;
            Save();
            Logger.Log($"[CharBoss] 配置：地图 {mapEntry} → {(enabled ? "替换" : "不替换（保持原版）")}");
        }
        catch (Exception e) { Logger.Log("[CharBoss] 配置：写地图开关失败: " + e.Message); }
    }

    /// <summary>
    /// 按地图 id 查开关：**用户显式设过就听用户的**；没设过时
    /// **原版四张图（OVERGROWTH / UNDERDOCKS / HIVE / GLORY）默认开，其余（模组加的图）默认关**。
    ///
    /// ⚠ 多人里**客人不用本机配置**（见 <see cref="GameplaySettingsFollowHost"/>），
    /// 由 `CharBossEncounters.ForVanillaBoss` 直接跟随房主写进同步状态的结论。
    /// </summary>
    public static bool IsMapEnabledById(string? mapEntry)
    {
        if (string.IsNullOrEmpty(mapEntry)) return false;
        if (Current.Maps.TryGetValue(mapEntry!, out var on)) return on;
        return IsVanillaMap(mapEntry!);
    }

    /// <summary>本机是不是「说了算」的那一方（单人/房主 = 是；多人客人 = 否）。</summary>
    public static bool GameplaySettingsFollowHost => Core.NetRole.IsHostOrSolo;

    // ── A2 · 每张地图的 Boss 勾选（用户 2026-11-06）─────────────────────

    /// <summary>
    /// 某张地图的 Boss 勾选列表；**null = 没配过 = 全选**（旧配置文件因此完全不受影响）。
    /// 多人客人不读本机这一项 —— 选择是「以房主为准的玩法设置」，客人端的结论
    /// 通过**同步的遭遇**（`act.BossEncounter` / `SecondBossEncounter`）落地，见 `Core/BossSelection.cs`。
    /// </summary>
    public static List<string>? BossPicksOf(string? mapEntry)
    {
        if (string.IsNullOrEmpty(mapEntry)) return null;
        try
        {
            if (Current.BossPicks != null
                && Current.BossPicks.TryGetValue(mapEntry!, out var list)
                && list != null && list.Count > 0)
            {
                return list;
            }
        }
        catch { /* 读不出来就当全选 */ }
        return null;
    }

    /// <summary>写某张地图的勾选并立刻存盘（配置界面用）；传 null = 恢复全选（从文件里删掉这一项）。</summary>
    public static void SetBossPicks(string? mapEntry, IEnumerable<string>? keys)
    {
        if (string.IsNullOrEmpty(mapEntry)) return;
        try
        {
            Current.BossPicks ??= new Dictionary<string, List<string>>();
            if (keys == null)
            {
                Current.BossPicks.Remove(mapEntry!);
                Logger.Log($"[CharBoss] 配置：地图 {mapEntry} 的 Boss 勾选已恢复为「全选」");
            }
            else
            {
                var list = new List<string>(keys);
                Current.BossPicks[mapEntry!] = list;
                Logger.Log($"[CharBoss] 配置：地图 {mapEntry} 的 Boss 勾选 → {list.Count} 只 [{string.Join(",", list)}]");
            }
            _dirty = true;
            Save();
        }
        catch (Exception e) { Logger.Log("[CharBoss] 配置：写 Boss 勾选失败: " + e.Message); }
    }

    /// <summary>配置文件所在目录（`telemetry` 子目录挂在它下面）。</summary>
    internal static string ConfigDirectory
    {
        get
        {
            try { return System.IO.Path.GetDirectoryName(FilePath) ?? string.Empty; }
            catch { return string.Empty; }
        }
    }

    /// <summary>A5：本地数据记录开没开（默认关 = 没有同意）。</summary>
    public static bool TelemetryEnabled
    {
        get
        {
            try { return Current.Telemetry; }
            catch { return false; }
        }
    }

    /// <summary>首次启动的「要不要记录本地数据」弹窗问过没有。</summary>
    public static bool ConsentAsked
    {
        get
        {
            try { return Current.ConsentAsked; }
            catch { return true; }          // 读不出来就当「问过了」，别反复打扰
        }
    }

    /// <summary>记下「已经问过」（弹窗用；是/否都记）。</summary>
    public static void SetConsentAsked(bool asked)
    {
        try
        {
            if (Current.ConsentAsked == asked) return;
            Current.ConsentAsked = asked;
            _dirty = true;
            Save();
        }
        catch (Exception e) { Logger.Log("[CharBoss] 配置：写『已询问数据记录』失败: " + e.Message); }
    }

    /// <summary>同意弹窗的文本版本（见 <see cref="Data.ConsentVersion"/>）。</summary>
    public static int ConsentVersion
    {
        get
        {
            try { return Current.ConsentVersion; }
            catch { return 0; }
        }
    }

    /// <summary>记下「这个版本的弹窗已经答过了」。</summary>
    public static void SetConsentVersion(int version)
    {
        try
        {
            if (Current.ConsentVersion == version) return;
            Current.ConsentVersion = version;
            _dirty = true;
            Save();
        }
        catch (Exception e) { Logger.Log("[CharBoss] 配置：写『同意弹窗版本』失败: " + e.Message); }
    }

    /// <summary>现在还要不要弹「要不要上传游玩数据」（老版本弹窗问过但没提上传 ⇒ 版本号不匹配 ⇒ 再问一次）。</summary>
    public static bool NeedsConsentPrompt
    {
        get
        {
            try { return !Current.ConsentAsked || Current.ConsentVersion < ConsentVersionCurrent; }
            catch { return false; }
        }
    }

    /// <summary>A6：上传端点（作者配置；空 = 不上传）。</summary>
    public static string TelemetryUrl
    {
        get
        {
            try { return Current.TelemetryUrl ?? ""; }
            catch { return ""; }
        }
    }

    /// <summary>飞书群机器人 webhook（零鉴权）。空 = 不用这条路。</summary>
    public static string FeishuBotUrl
    {
        get { try { return Current.FeishuBotUrl ?? ""; } catch { return ""; } }
    }

    /// <summary>飞书多维表格：app_id / app_secret / app_token / table_id 任意一项为空就视为未配置。</summary>
    public static bool FeishuBitableReady
    {
        get
        {
            try
            {
                return !string.IsNullOrWhiteSpace(Current.FeishuAppId)
                       && !string.IsNullOrWhiteSpace(Current.FeishuAppSecret)
                       && !string.IsNullOrWhiteSpace(Current.FeishuBitableAppToken)
                       && !string.IsNullOrWhiteSpace(Current.FeishuTableId);
            }
            catch { return false; }
        }
    }

    public static string FeishuAppId { get { try { return Current.FeishuAppId ?? ""; } catch { return ""; } } }
    public static string FeishuAppSecret { get { try { return Current.FeishuAppSecret ?? ""; } catch { return ""; } } }
    public static string FeishuBitableAppToken { get { try { return Current.FeishuBitableAppToken ?? ""; } catch { return ""; } } }
    public static string FeishuTableId { get { try { return Current.FeishuTableId ?? ""; } catch { return ""; } } }
    public static string FeishuDiagTableId { get { try { return Current.FeishuDiagTableId ?? ""; } catch { return ""; } } }

    /// <summary>A6 有没有任何可用的收数据去处（通用 URL / 飞书群机器人 / 飞书多维表格）。</summary>
    public static bool AnyUploadEndpoint
    {
        get
        {
            try
            {
                return !string.IsNullOrWhiteSpace(Current.TelemetryUrl)
                       || !string.IsNullOrWhiteSpace(Current.FeishuBotUrl)
                       || FeishuBitableReady;
            }
            catch { return false; }
        }
    }

    /// <summary>设置 A6 是否生效：只有「A5 开着 + 端点非空 + 玩家同意上传」三者都满足才会上传。</summary>
    public static bool TelemetryUpload
    {
        get
        {
            try
            {
                if (!Current.Telemetry) return false;
                if (!AnyUploadEndpoint) return false;
                return Current.TelemetryUpload;
            }
            catch { return false; }
        }
    }

    /// <summary>设置 A6（配置界面用）。</summary>
    public static void SetTelemetryUpload(bool enabled)
    {
        try
        {
            if (Current.TelemetryUpload == enabled) return;
            if (enabled && string.IsNullOrWhiteSpace(Current.TelemetryUrl))
            {
                if (!AnyUploadEndpoint)
                {
                    Logger.Log("[CharBoss] 配置：上传数据需要先配置收数据的去处（通用端点 telemetryUrl、" +
                               "飞书群机器人 feishuBotUrl、或飞书多维表格那四项之一）⇒ 本次只在本机记录");
                    return;
                }
            }
            Current.TelemetryUpload = enabled;
            _dirty = true;
            Save();
            Logger.Log($"[CharBoss] 配置：上传游玩数据 → {(enabled ? "开（只上传匿名对战数据）" : "关（只写本机）")}");
        }
        catch (Exception e) { Logger.Log("[CharBoss] 配置：写上传开关失败: " + e.Message); }
    }

    /// <summary>设置收集端点（作者用；玩家一般不用动）。</summary>
    public static void SetTelemetryUrl(string url)
    {
        try
        {
            url = (url ?? "").Trim();
            if (Current.TelemetryUrl == url) return;
            Current.TelemetryUrl = url;
            _dirty = true;
            Save();
            Logger.Log($"[CharBoss] 配置：收集端点 → {(url.Length == 0 ? "（空：不上传）" : url)}");
        }
        catch (Exception e) { Logger.Log("[CharBoss] 配置：写收集端点失败: " + e.Message); }
    }

    /// <summary>设置 A5（配置界面用）。打开的那一刻写一条 consent 记录。</summary>
    public static void SetTelemetry(bool enabled)
    {
        try
        {
            if (Current.Telemetry == enabled) return;
            Current.Telemetry = enabled;
            _dirty = true;
            Save();
            Logger.Log($"[CharBoss] 配置：本地数据记录 → {(enabled ? "开" : "关")}" +
                       (enabled
                           ? "（只写入本机文件，不上传；想给作者就把文件发出去）"
                           : "（不再写入任何数据）"));
            if (enabled) Telemetry.WriteConsentOnce();
        }
        catch (Exception e) { Logger.Log("[CharBoss] 配置：写数据记录开关失败: " + e.Message); }
    }

    /// <summary>
    /// A4「与原本 Boss 共用匹配池」当前是否生效。
    /// 客人不读本机配置（跟随房主的同步结果，见 ForVanillaBoss 的客人分支）。
    /// </summary>
    public static bool SharePool
    {
        get
        {
            try { return Current.SharePool; }
            catch { return false; }
        }
    }

    /// <summary>设置 A4（配置界面 / 控制台用）。</summary>
    public static void SetSharePool(bool enabled)
    {
        try
        {
            if (Current.SharePool == enabled) return;
            Current.SharePool = enabled;
            _dirty = true;
            Save();
            Logger.Log($"[CharBoss] 配置：与原本 Boss 共用匹配池 → {(enabled ? "开" : "关")}" +
                       (enabled
                           ? "（开了替换的地图会从「原本的 Boss」和「本模组角色 Boss」里按种子随机抽）"
                           : "（恢复成「开了替换就一定是角色 Boss」）"));
        }
        catch (Exception e) { Logger.Log("[CharBoss] 配置：写共用匹配池失败: " + e.Message); }
    }

    /// <summary>
    /// A4 的子项（用户 2026-10-29）：「进阶 10 第三幕固定一场原本 + 一场角色」。
    /// ⚠ **必须同时开着 <see cref="SharePool"/> 才有意义**，所以这里返回的是**生效值**。
    /// </summary>
    public static bool Asc10Act3OneEach
    {
        get
        {
            try { return Current.SharePool && Current.Asc10Act3OneEach; }
            catch { return false; }
        }
    }

    /// <summary>用户在这一项上勾没勾（配置界面显示用，不受混匹开关影响）。</summary>
    public static bool Asc10Act3OneEachRaw
    {
        get
        {
            try { return Current.Asc10Act3OneEach; }
            catch { return false; }
        }
    }

    /// <summary>设置「进阶 10 第三幕固定一场原本 + 一场角色」（需要混匹开启）。</summary>
    public static void SetAsc10Act3OneEach(bool enabled)
    {
        try
        {
            if (Current.Asc10Act3OneEach == enabled) return;
            Current.Asc10Act3OneEach = enabled;
            _dirty = true;
            Save();
            Logger.Log($"[CharBoss] 配置：进阶 10 第三幕固定「一场原本 + 一场角色」→ {(enabled ? "开" : "关")}" +
                       (enabled && !Current.SharePool
                           ? "（注意：还没开启「与原本 Boss 共用匹配池」，本项暂不生效）"
                           : string.Empty));
        }
        catch (Exception e) { Logger.Log("[CharBoss] 配置：写「进阶10第三幕固定」失败: " + e.Message); }
    }

    // ── 地图清单（配置界面用） ─────────────────────────────

    /// <summary>一张地图的展示信息。</summary>
    public readonly record struct MapInfo(string Entry, string DisplayName, bool Enabled, bool IsVanilla);

    /// <summary>
    /// 三幕各自的地图清单（**动态**：遍历 `ModelDb.ActsByIndex`，装了新地图模组也会列出来）。
    /// 拿不到时退回空列表。
    /// </summary>
    public static List<MapInfo>[] MapsByAct()
    {
        var result = new List<MapInfo>[] { new(), new(), new() };
        try
        {
            var byIndex = ModelDb.ActsByIndex;
            for (int act = 0; act < result.Length; act++)
            {
                if (byIndex == null || act >= byIndex.Count) continue;
                var group = byIndex[act];
                if (group == null) continue;
                foreach (var model in group)
                {
                    string? entry = EntryOf(model);
                    if (string.IsNullOrEmpty(entry)) continue;
                    result[act].Add(new MapInfo(entry!, DisplayTitle(model, entry!), IsMapEnabledById(entry!), IsVanillaMap(entry!)));
                }
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 配置：列地图失败: " + e.Message);
        }
        return result;
    }

    /// <summary>原版的那四个地图（配置界面里标注用）。</summary>
    private static bool IsVanillaMap(string entry) => entry switch
    {
        "OVERGROWTH" => true,     // 密林
        "UNDERDOCKS" => true,     // 暗港
        "HIVE" => true,           // 巢穴
        "GLORY" => true,          // 荣耀
        _ => false,
    };

    private static IEnumerable<ActModel> AllKnownActs()
    {
        var list = new List<ActModel>();
        try
        {
            var acts = ModelDb.Acts;
            if (acts != null) foreach (var a in acts) if (a != null) list.Add(a);
        }
        catch { /* 忽略 */ }
        return list;
    }

    internal static string? EntryOf(ActModel? act)
    {
        try { return act?.Id.Entry; } catch { return null; }
    }

    /// <summary>地图的显示名（本地化标题；取不到就用 id）。</summary>
    private static string DisplayTitle(ActModel? act, string fallback)
    {
        try
        {
            var title = act?.Title;
            var text = title?.GetFormattedText();
            if (!string.IsNullOrWhiteSpace(text)) return text!;
        }
        catch { /* 用 fallback */ }
        return fallback;
    }

    // ── 存盘 ──────────────────────────────────────────────

    /// <summary>把当前配置写回磁盘（只在有改动时写）。</summary>
    public static void Save()
    {
        if (!_dirty || _data == null) return;
        WriteSnapshot(_data);
        _dirty = false;
    }

    /// <summary>写盘本体（**不要走 <see cref="Current"/>** —— 它会在首次加载时递归）。</summary>
    private static void WriteSnapshot(Data data)
    {
        try
        {
            var snapshot = new Data
            {
                Version = 2,
                Maps = new Dictionary<string, bool>(data.Maps),
                MixedMatching = true,                    // A2 锁定
                BgmSource = data.BgmSource == BgmGen1Boss ? BgmGen1Boss : BgmVanillaOtherBoss,
                // ⚠ 2026-10-30 补：以前这里**漏了** A4 和新增的 A5 ⇒ 「共用匹配池 / 进阶 10 固定一场原本」
                //   每次重启都被悄悄写回默认值（用户勾了也白勾）。现在全部原样写回。
                SharePool = data.SharePool,
                Asc10Act3OneEach = data.Asc10Act3OneEach,
                Telemetry = data.Telemetry,
                ConsentAsked = data.ConsentAsked,
                ConsentVersion = data.ConsentVersion,
                TelemetryUpload = data.TelemetryUpload,
                TelemetryUrl = data.TelemetryUrl ?? "",
                FeishuBotUrl = data.FeishuBotUrl ?? "",
                FeishuAppId = data.FeishuAppId ?? "",
                FeishuAppSecret = data.FeishuAppSecret ?? "",
                FeishuBitableAppToken = data.FeishuBitableAppToken ?? "",
                FeishuTableId = data.FeishuTableId ?? "",
                FeishuDiagTableId = data.FeishuDiagTableId ?? "",
            };
            File.WriteAllText(FilePath, JsonSerializer.Serialize(snapshot, JsonOptions));
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 配置写盘失败: " + e.Message);
        }
    }
}
