using System;
using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.Nodes.Audio;

namespace CharacterBosses.Core;

/// <summary>
/// **Boss 战背景音乐**（用户 2026-10-28 定稿；对照表见 <c>docs/plans/config-ui-and-bgm.md</c> §B）。
///
/// 三种来源（配置项 A3，见 <see cref="BossConfig.BgmSource"/>）：
///   ① **原版其他 Boss 的音乐**（默认）—— 走二代 FMOD：`PlayCustomMusic(事件名)`；
///   ② **一代 Boss 音乐** —— 三首 ogg（打进 PCK），自己建 `AudioStreamPlayer` 播，同时压掉 FMOD；
///   ③ 自制音乐 —— 锁定关闭，按①处理。
///
/// ── 实证（反编译 `NRunMusicController` / 各原版 Boss 的 `EncounterModel.CustomBgm`）──
///   · 战斗开始时 `CombatManager` 若 `Encounter.HasBgm` 就 `PlayCustomMusic(Encounter.CustomBgm)`；
///   · `PlayCustomMusic(s)` = `stop_music` + `update_music(s)`，**传的是完整事件名**，例如
///     `event:/music/act1_boss_the_kin`（这些字符串直接把原版遭遇的 `CustomBgm` 抄过来了，不是猜的）；
///   · **曲目分段**走 `UpdateMusicParameter("<boss>_progress", 值)` → `update_music_parameter`：
///       - 女王 `queen_progress`：1 = 一阶段（火炬头还活着）、2 = 火炬头死后（二阶段）、5 = 死亡；
///       - 无厌沙虫 `insatiable_progress`：原版只在死亡时写 10（0→10 是「越来越激昂」的层进）；
///       - 墨影幻灵 `vantom_progress`：1 = 平静、2/3 = 蓄力、**3 = 肢解的重音**、5 = 死亡。
///     ⚠ 这些分段参数**实测对「我们自己播的曲目」完全无效**（2026-10-28 三场），
///       见文件下方「曲目分段」那一段的说明 —— 唯一有效的手段是换曲目。
///     （以上取值全部来自反编译原版 Boss 的 `UpdateMusicParameter` 调用点，不是估的。）
///
/// ⚠ **幕次守卫**：这些事件分别只存在于 `actN_*.bank` 里（`ActModel.MusicBankPaths` 只会加载**当前幕**
///   的 bank），所以「第二幕的事件名在第一幕硬播」= 找不到事件。一旦先 `stop_music` 再播不出来，
///   结果就是**整场没音乐**（用户 2026-10-28 实测过一模一样的症状）。因此这里每一首曲目都带
///   「它属于第几幕」，幕不对就**不接管**，让原版音乐继续放。
/// </summary>
public static class BossBgm
{
    /// <summary>
    /// 一条二代曲目：FMOD 事件名 + 它所在的幕（-1 = 任何幕都能播）+ 中文名（写日志用）。
    /// </summary>
    private readonly record struct Track(string Event, int Act, string Name)
    {
        /// <summary>「不接管」：留空事件名，让原版音乐照常播放。</summary>
        public bool IsKeepVanilla => string.IsNullOrEmpty(Event);
    }

    /// <summary>不接管（保持原版默认 Boss 音乐）。</summary>
    private static readonly Track KeepVanilla = new(string.Empty, -1, "（保持原版默认 Boss 音乐）");

    /// <summary>
    /// A3 选项①：**我们的遭遇 → 原版其他 Boss 的曲目**（用户 2026-10-28 的对照表，逐行对应）。
    ///
    /// 事件名一律取自原版同类遭遇的 `CustomBgm`：
    ///   同族小队 `the_kin` / 墨影幻灵 `vantom` / 瀑布巨兽 `waterfall_giant` / 灵魂异鱼 `soul_fysh`
    ///   / 帝皇蟹 `kaiser_crab` / 无厌沙虫 `the_insatiable` / 女王 `queen`。
    ///
    /// 「其余全部 ⇒ 永世沙漏」：原版 `AeonglassBoss.CustomBgm` 就是 `act3_boss_queen`
    /// （反编译实证 —— 永世沙漏自己**没有**独立曲目，所以兜底曲 = 女王那首），见 <see cref="Fallback"/>。
    /// </summary>
    private static readonly Dictionary<string, Track> FmodByEncounter = new(StringComparer.Ordinal)
    {
        // ── 第一幕 ──
        ["IRONCLAD_ANGER_BOSS_ENCOUNTER"]     = new("event:/music/act1_boss_the_kin",            0, "同族小队"),
        // 碎屑储：用户 2026-10-28 改成**瀑布巨兽**（原来是墨影幻灵，但它那套「随回合切段 / 彗星对重音」
        // 靠的是 `<boss>_progress` 参数 —— 实测这种参数对我们播的曲目完全无效，所以换掉）。
        ["REGENT_DEBRIS_BOSS_ENCOUNTER"]      = new("event:/music/act1_b_boss_waterfall_giant",   0, "瀑布巨兽"),   // v0.2.378 用户指定：意图循环与墨影幻灵同类（4 回合循环、第 3 回合重击），改用它这首可分段的曲子
        ["DEFECT_DARK_BOSS_ENCOUNTER"]        = new("event:/music/act1_b_boss_waterfall_giant",   0, "瀑布巨兽"),
        ["NECROBINDER_DOOM_BOSS_ENCOUNTER"]   = new("event:/music/act1_b_boss_soul_fysh",         0, "灵魂异鱼"),

        // 一层「奇巧猎」按用户表是「乐加维林族母」——**原版那只 Boss 没有任何专属曲目**
        // （`LagavulinMatriarchBoss` 没写 `CustomBgm`，它进 Boss 房放的就是本幕默认 Boss 音乐）。
        // 所以这里也「不接管」，听到的与打乐加维林族母时一致。
        ["SILENT_SLY_BOSS_ENCOUNTER"]         = KeepVanilla,

        // ── 第二幕（用户 2026-10-28 定稿：**二层全部 Boss 统一用帝皇蟹**）──
        // 原本「物理机 / 放血战 = 无厌沙虫（按玩家血量驱动 progress）」，但实测两条参数路线
        // （事件内 `update_music_parameter`、全局 `update_global_parameter`）都是「返回成功、
        // 音乐毫无变化」—— 沙虫那首的 5 个 Layer 是同一事件内部的层，原版只在死亡时写一次 10，
        // 很可能那条也没接线。所以这一档按用户口径改成帝皇蟹（唯一确认有效的手段是换曲目）。
        ["SILENT_ENVENOM_BOSS_ENCOUNTER"]     = new("event:/music/act2_boss_kaiser_crab",        1, "帝皇蟹"),
        ["NECROBINDER_SOUL_BOSS_ENCOUNTER"]   = new("event:/music/act2_boss_kaiser_crab",        1, "帝皇蟹"),
        ["REGENT_FORGE_BOSS_ENCOUNTER"]       = new("event:/music/act2_boss_kaiser_crab",        1, "帝皇蟹"),
        // 二层储君的第二套设计「征服者储君」（用户 2026-11-02）：沿用二层统一的帝皇蟹
        ["REGENT_KING_BOSS_ENCOUNTER"]   = new("event:/music/act2_boss_kaiser_crab",        1, "帝皇蟹"),
        ["DEFECT_CLAW_BOSS_ENCOUNTER"]        = new("event:/music/act2_boss_kaiser_crab",        1, "帝皇蟹"),
        ["IRONCLAD_BLOOD_BOSS_ENCOUNTER"]     = new("event:/music/act2_boss_kaiser_crab",        1, "帝皇蟹"),
        // 二层铁甲战士的第二套设计「杂耍战」（用户 2026-11-02）：沿用二层统一的帝皇蟹
        ["IRONCLAD_JUGGLER_BOSS_ENCOUNTER"]  = new("event:/music/act2_boss_kaiser_crab",        1, "帝皇蟹"),
        ["SILENT_SNECKO_BOSS_ENCOUNTER"]      = new("event:/music/act2_boss_kaiser_crab",        1, "帝皇蟹"),

        // ── 第三幕 ──
        ["SILENT_BREWER_BOSS_ENCOUNTER"]      = new("event:/music/act3_boss_queen",              2, "女王"),
        ["IRONCLAD_VULN_BOSS_ENCOUNTER"]      = new("event:/music/act3_boss_queen",              2, "女王"),
        // 三层亡灵契约师的第二套设计「养手骨」（用户 2026-11-03）：沿用三层女王
        ["NECROBINDER_OSTY_BOSS_ENCOUNTER"]   = new("event:/music/act3_boss_queen",              2, "女王"),
    };

    /// <summary>
    /// 「其余所有 Boss ⇒ **永世沙漏**」。原版永世沙漏（`AeonglassBoss`）的 `CustomBgm` 就是女王那首，
    /// 所以兜底曲 = `act3_boss_queen`。它属于第三幕 ⇒ 只有第三幕的兜底 Boss 会真的接管
    /// （正好：刀猎 / 巨石战 / 能力机 / 根除骨 / 无色储君 / 辉星储 全是三层设计）。
    /// </summary>
    private static readonly Track Fallback = new("event:/music/act3_boss_queen", 2, "永世沙漏");

    /// <summary>
    /// A3 选项②：一代曲目（PCK 里的 `res://` 路径）。
    ///
    /// ★ 用户 2026-10-31：「选②时**所有** boss 都用一代音乐，而不是只有三个机器人；都用自己所在层的」。
    ///   ⇒ 默认按**幕**选：一层 → `STS_Boss1`、二层 → `STS_Boss2`、三层 → `STS_Boss3`（四层及以后沿用三层）。
    ///   下面这张表只是**个别覆盖**（现有三只故障机器人正好与按幕一致，留着以备以后要特例）。
    /// </summary>
    private static readonly Dictionary<string, string> OggByEncounter = new(StringComparer.Ordinal)
    {
        ["DEFECT_DARK_BOSS_ENCOUNTER"] = Gen1Ogg(0),
        ["DEFECT_CLAW_BOSS_ENCOUNTER"] = Gen1Ogg(1),
        ["DEFECT_AI_BOSS_ENCOUNTER"]   = Gen1Ogg(2),
    };

    /// <summary>第几次幕（0 起）对应的一代曲；PCK 里只有三首，幕数超了沿用第三首。</summary>
    private static string Gen1Ogg(int act)
    {
        int a = act < 0 ? 0 : (act > 2 ? 2 : act);
        return $"res://CharacterBosses/audio/STS_Boss{a + 1}_NewMix_v1.ogg";
    }

    /// <summary>这个遭遇该用哪首一代曲：先查覆盖表，再按幕。</summary>
    private static string Gen1OggFor(string? encounterEntry, int act)
    {
        if (!string.IsNullOrEmpty(encounterEntry)
            && OggByEncounter.TryGetValue(encounterEntry!, out var hit)) return hit;
        return Gen1Ogg(act);
    }

    // ── 曲目分段（FMOD 事件内参数） ─────────────────────────
    //
    // ⚠ 2026-10-28 实测结论：这些 `<boss>_progress` 参数在**我们自己播的曲目上不起作用**
    //   （事件内 `update_music_parameter` 与全局 `update_global_parameter` 都返回成功、音乐毫无变化；
    //    无厌沙虫那首的 5 个 Layer 本就在同一个事件内部，原版也只在死亡时写一次 10）。
    //   保留女王这一处调用是为了**和原版保持一致**（原版 Boss 也是这么写的），
    //   但**不要**再基于它设计新效果 —— 唯一确认有效的手段是换曲目（`PlayCustomMusic`）。
    //   （碎屑储原来还有一处 `vantom_progress` 的「彗星重音」，同理无效，2026-10-28 已删。）

    private const string QueenParam = "queen_progress";

    /// <summary>墨影幻灵曲目的分段参数（1 = 平静 / 2 = 蓄力 / 3 = 重音（肢解=她的彗星）/ 5 = 死亡）。</summary>
    // ★ v0.2.379（用户 2026-11-04「BGM 不是墨影幻灵的」）：墨影幻灵那首 `act1_boss_vantom` 在
    //   **`act1_b1.bank`** 里，而碎屑储这场加载的是另一套一代 bank（`act1_b_*`）
    //   ⇒ `BossBgm` 的 bank 守卫直接把它换掉了（日志：`在本幕 bank 里没有（在 act1_b1.bank 里）⇒ 改用 …waterfall_giant`）。
    //   所以分段要挂在**本幕真能播的那首**上：瀑布巨兽 `waterfall_giant_progress`
    //   （反编译 `WaterfallGiant`：中途写过 1 / 2 / 5 ⇒ 确实有段落，不像沙虫只写死亡值）。
    private const string VantomParam = "waterfall_giant_progress";

    /// <summary>女王的「一阶段」（火炬头还活着）。</summary>
    private const float QueenPhase1 = 1f;

    /// <summary>女王的「二阶段」（火炬头死后）—— 炼药猎喝了/触发了瓶中精灵就切这一段。</summary>
    private const float QueenPhase2 = 2f;

    // ── 运行期状态 ─────────────────────────────────────────

    private static AudioStreamPlayer? _player;

    /// <summary>我们起的曲目（FMOD 事件名 / ogg 路径）。非空 ⇒ FMOD 那边**可能还在响**，迟早要还原。</summary>
    private static string? _playing;

    /// <summary>这一场我们「算不算」还占着音乐（分段推进、重复切换判定用它）。</summary>
    private static bool _claimed;

    private static string? _playingEncounter;
    private static bool _queenPhase2;

    /// <summary>延迟还原的截止时刻（<c>Time.GetTicksUsec()</c>，0 = 没有待办）。</summary>
    private static long _restoreAtUsec;

    /// <summary>我们是在**哪一份战斗状态**里接管的（用来区分「同一场」和「新的另一场」）。</summary>
    private static object? _claimedCombat;

    /// <summary>「本幕音乐库」这行诊断每局只打一次。</summary>
    private static bool _loggedBanks;

    /// <summary>「本场遭遇自己有专属曲目」这条日志每局只打一次（不然多 Boss 会刷屏）。</summary>
    private static bool _loggedVanillaBgmFight;

    /// <summary>
    /// 历史遗留：v0.2.209 曾用它做「死亡后延迟 N 毫秒才还原」，实测**没有胜利音乐**（见
    /// <see cref="OnLastBossDied"/> 的说明），现已改为当场还原。保留常量只为让老注释还能读懂。
    /// </summary>
    private const int VictoryKeepMs = 0;

    /// <summary>本次战斗我们接管了音乐没有（没接管就什么都不做，让原版照常放）。</summary>
    internal static bool Active => _claimed && !string.IsNullOrEmpty(_playing);

    /// <summary>战斗开始：按遭遇 + 配置项 A3 切曲目。拿不到控制器就静默跳过。</summary>
    internal static void OnCombatStart(string? encounterEntry)
    {
        try
        {
            _restoreAtUsec = 0;                       // 又开打了：取消任何待还原
            _fallbackLogged = false;
            // ★ 新的一场战斗 ⇒ 上一场的「死亡段落待还原」全部作废（进阶 10 的三层是连着打两场，
            //   上一场遗留的 `_deathPendingRestore` / 旧计时器会把这一场的胜利音切掉）。
            _deathPendingRestore = false;
            _deathPlaybackScheduled = false;
            _restoreGen++;
            if (string.IsNullOrEmpty(encounterEntry)) { _playingEncounter = encounterEntry; _queenPhase2 = false; return; }

            // ⚠ 原版这只 Boss **自己有专属曲目**（`EncounterModel.CustomBgm` 非空 ⇒ `HasBgm`）⇒ 音乐本来
            //   就是它自己在管，我们不要抢。「与原本 Boss 共用匹配池」打开时对手可能是原版女王 / 无厌沙虫 /
            //   墨影幻灵…，抢过来就等于把人家那一首换成我们的兜底曲；往昔之章之类模组的遭遇同理。
            //   （我们自己的遭遇一个都没写 `CustomBgm`，所以正常流程走不到这里。）
            if (CurrentEncounterHasBgm(out string vanillaBgm))
            {
                if (!_loggedVanillaBgmFight)
                {
                    _loggedVanillaBgmFight = true;
                    Logger.Log($"[CharBoss] BGM：本场遭遇自己有专属曲目（{vanillaBgm}）⇒ 不接管，" +
                               "让原版/那个模组自己放（胜利段落也由它自己走）");
                }
                _playingEncounter = encounterEntry;
                return;
            }

            // ⚠ 多人 / 多 Boss 时，**每只 Boss 的战斗开始钩子都会调到这里**（实测 4 只 ⇒ 同一秒 4 行 BGM 日志）。
            //   以前每调一次就 `PlayCustomMusic` 一次，而它是 `stop_music` + `update_music` ⇒ 音乐被从头切 4 遍。
            //   同一场（同一份战斗状态 + 同一个遭遇）已经接管过就直接返回。
            //   ⚠ 必须连**战斗状态**一起比：只比遭遇名的话，中途退出再开一局抽到同一只 Boss 会被误判成
            //     「已经接管过了」⇒ 整场没音乐。
            if (_claimed
                && string.Equals(_playingEncounter, encounterEntry, StringComparison.Ordinal)
                && ReferenceEquals(_claimedCombat, CurrentCombatState()))
            {
                Logger.Log($"[CharBoss] BGM：{encounterEntry} 这一场已经接管过了 ⇒ 不重复切换（{DescribeState()}）");
                return;
            }

            _playingEncounter = encounterEntry;
            _queenPhase2 = false;

            // 诊断（每场一次）：本幕用的是哪一套音乐库 —— 决定「战斗结束过渡（Progress=7）」到底存不存在。
            if (!_loggedBanks)
            {
                _loggedBanks = true;
                Logger.Log($"[CharBoss] BGM 本幕音乐库：{string.Join(" | ", StageMusicBanks())}");
            }

            if (BossConfig.BgmSource == BossConfig.BgmGen1Boss)
            {
                // 用户 2026-10-31：选②一代曲时**所有** Boss 都用一代曲，按它自己所在的幕挑
                // （一层 Boss1 / 二层 Boss2 / 三层 Boss3），不再只限那三只故障机器人。
                if (TryPlayGen1(encounterEntry, CurrentActIndex())) return;
                // 极少数情况（ogg 载入失败）才退回①的对照表
            }

            PlayVanillaOther(encounterEntry!, CurrentActIndex());
        }
        catch (Exception e) { Logger.Log("[CharBoss] BGM 切换失败: " + e.Message); }
    }

    /// <summary>Boss 回合开始（<c>PrepareTurn</c> 里调）：按回合推进曲目分段。</summary>
    internal static void OnBossTurn(int turnNumber)
    {
        if (!Active)
        {
            if (turnNumber <= 1)
            {
                Logger.Log($"[CharBoss] BGM 分段：这一场我们没接管音乐（_playing 为空）⇒ 不推进分段" +
                           $"（encounter={_playingEncounter ?? "<空>"}）");
            }
            return;
        }
        try
        {
            // 女王那首 = 第三幕的兜底曲（永世沙漏 / 刀猎 / 巨石战 / 能力机 …）
            // 以前只给「易伤战 / 炼药猎」两个 encounter 发参数，其它走女王曲的 Boss 一次也没发过。
            // ⚠ 2026-10-28 实测：这个参数对我们自己播的曲目听不出变化（保留只为与原版一致），
            //   但一旦哪天它真的起作用，那「一次都没发」就是「整场没声音」。
            if (IsQueenTrack()) SetParam(QueenParam, _queenPhase2 ? QueenPhase2 : QueenPhase1);
            // ★ v0.2.378（用户 2026-11-04）：「碎屑储君的意图循环和墨影幻灵同类（4 回合循环、第 3 回合重击）」
            //   ⇒ 用它的曲子，并按同一个语义切段：平静 1 / 蓄力 2 / 重音 3（肢解 ↔ 她的彗星）。
            //   她的剧本是 5～8 回合循环（`DebrisRegentScript.LoopFrom=5, LoopLength=4`）⇒ 先折回 1..4。
            if (IsVantomTrack())
            {
                int rel = ((turnNumber - 5) % 4 + 4) % 4 + 1;      // 1..4
                float v = rel == 3 ? 3f : (rel == 2 ? 2f : 1f);
                SetParam(VantomParam, v);
                Logger.Log($"[CharBoss] BGM：碎屑储第 {turnNumber} 回合（循环内第 {rel} 回合）⇒ waterfall_giant_progress={v}");
            }
        }
        catch (Exception e) { Logger.Log("[CharBoss] BGM 分段失败: " + e.Message); }
    }

    /// <summary>
    /// 炼药猎的「瓶中精灵」救了她一命 ⇒ 切女王二阶段音乐（<see cref="Patches.FairyInABottleMusicPatch"/>）。
    /// </summary>
    internal static void OnBrewerFairySaved()
    {
        if (!Active) return;
        if (!string.Equals(_playingEncounter, "SILENT_BREWER_BOSS_ENCOUNTER", StringComparison.Ordinal)) return;
        try
        {
            _queenPhase2 = true;
            SetParam(QueenParam, QueenPhase2);
            Logger.Log("[CharBoss] BGM：瓶中精灵救下炼药猎 → 女王二阶段音乐（queen_progress=2）");
        }
        catch (Exception e) { Logger.Log("[CharBoss] BGM 二阶段切换失败: " + e.Message); }
    }

    /// <summary>我们现在是不是正在接管音乐（日志/判断用）。</summary>
    internal static bool IsPlaying => _playing != null;

    /// <summary>我们是不是「算」还占着这条曲目（胜利段落期间会被标成 false）。</summary>
    internal static bool Claiming => _claimed;

    /// <summary>
    /// 还有没有「我们起的曲目还没还原」（便宜判断，给每帧驱动做兜底用）。
    ///
    /// ⚠ 三个条件都要满足：真的占着曲目、**不在还原中**、而且**不是死亡段落在放**。
    ///   最后一条很关键：`Restore()` 内部会调 `UpdateTrack()`，而音乐追踪补丁在它的 postfix 里
    ///   就用这个属性决定要不要再调 `TickRestore()` —— 以前没排除「还原中/死亡段落」，
    ///   于是形成 `Restore → UpdateTrack(postfix) → TickRestore → Restore …` 的**无限递归**。
    /// </summary>
    internal static bool HasPendingRestore => _playing != null && !_restoring && !_deathPlaybackScheduled;

    /// <summary>
    /// **最后一只 Boss 倒下的那一帧**（<c>BossFrameTickPatch</c> 里调）⇒ **立刻还原原版音乐**。
    ///
    /// ⚠ 这里走过两个反方向的弯路，实测结论（用户 2026-10-30，`mod_log.txt` 实证）：
    ///   · v0.2.176～0.2.208：死亡这一帧就 `StopCustomMusic()` ⇒ 胜利音乐**有**（之后很久没人报这个 bug）；
    ///   · v0.2.209～0.2.212：改成「死亡时不停、4 秒后再还原」，理由是「战斗结束游戏会
    ///     `UpdateTrack()` 给当前曲目设 `Progress=7` 切出胜利段落」⇒ 实测**没有胜利音乐**：
    ///     ```
    ///     [10:17:20] ♪ UpdateTrack（调用者 EndCombatInternal）… 曲目=event:/music/act1_boss_the_kin
    ///                                                              | _currentTrack=event:/music/act1_a1_v1
    ///     [10:17:36] ♪ StopMusic（调用者 CloseToMenu）… 待还原=-11392ms 后还原
    ///     ```
    ///     也就是说：那个 `Progress=7` 是给**本幕地图音乐**（`_currentTrack`）用的段落参数，
    ///     Boss 那首自定义曲目（the_kin / queen / kaiser_crab）**不响应**它 ⇒ 一声不响；
    ///     而「4 秒后还原」也根本没跑到（战斗内每帧驱动已停、地图节点在奖励界面还没实例化）。
    ///
    /// ⇒ 正确做法就是**当场还原**：`StopCustomMusic()` = `stop_music` + 把 `_currentTrack`
    ///   （本幕地图音乐）重新放起来 + `Progress=7`（CombatEnd）—— 战斗结束/胜利那段音乐正是在这里出来的。
    /// </summary>
    internal static void OnLastBossDied()
    {
        if (_playing == null) { _claimed = false; return; }
        _claimed = false;
        _queenPhase2 = false;
        _bossDiedThisFight = true;
        PlayDeathSectionThenRestore("最后一只 Boss 倒下");
    }

    /// <summary>
    /// **本场 Boss 战真的结束了**（Boss 被移出房间）。
    /// 如果死亡那一帧已经把「死亡段落」发出去并排好了还原，这里就只记一行；
    /// 否则（被移出房间时还没走过帧驱动）也走同一条路。
    /// </summary>
    internal static void OnFightEnded(string reason, bool bossDied = true)
    {
        try
        {
            if (_playing == null) { _claimed = false; _claimedCombat = null; return; }

            // 没打死就结束（快捷 SL/回滚、弃档、房间被拆…）⇒ **不发死亡段落**（那就是胜利音）
            if (!bossDied)
            {
                _bossDiedThisFight = false;
                Logger.Log($"[CharBoss] BGM：{reason}，但 Boss **还活着**（快捷 SL/回滚一类）⇒ " +
                           "静默还原，不播死亡/胜利段落（" + DescribeState() + "）");
                Stop(reason);
                return;
            }
            _bossDiedThisFight = true;
            if (_deathPlaybackScheduled)
            {
                Logger.Log($"[CharBoss] BGM：{reason}（死亡段落已在放，按计划还原；{DescribeState()}）");
                return;
            }
            _claimed = false;
            PlayDeathSectionThenRestore(reason);
        }
        catch { /* 忽略 */ }
    }

    // ── Boss「死亡段落」 ─────────────────────────────────────
    //
    // 用户 2026-10-30：「这两场 boss 战胜利后都没有播放原版的 boss 战胜利音效，
    //   而有点像打完小怪的音效（音乐淡出）」。
    //
    // 反编译实证（这才是原版 Boss 胜利音的来源）：
    //   `MegaCrit.Sts2.Core.Models.Monsters.WaterfallGiant / SoulFysh` 等原版 Boss 在**死亡**时调
    //     `NRunMusicController.Instance?.UpdateMusicParameter("<boss>_progress", 5f)`
    //   —— 也就是给它**自己那首曲目**切到「死亡 / 收尾」段落（5 = 死亡；1/2 = 一阶段/二阶段）。
    //   `sts2.dll` 里全部进度参数名（实测枚举）：`the_kin_progress` / `queen_progress` /
    //   `kaiser_crab_progress` / `waterfall_giant_progress` / `soulfysh_progress`（注意**没有下划线**）/
    //   `vantom_progress` / `ceremonial_beast_progress`。
    //
    // 我们的 Boss 是角色，不是那些原版 Boss ⇒ 没有人发这个参数 ⇒ 死亡时曲目只会被我们切回本幕音乐
    // ⇒ 听起来就是「小怪战斗结束」那种淡出。所以现在由我们在最后一只 Boss 倒下时补发 `5`，
    // 让那首曲目把死亡段落放完（`DeathPlaybackSeconds` 秒）再还原原版音乐。
    // 计时用 `SceneTreeTimer`（不依赖战斗内每帧驱动 —— 战斗一结束那些驱动就停，v0.2.209 那次
    // 「4 秒后还原」就是因此从来没跑起来）。

    /// <summary>原版 Boss 死亡时写的段落值。</summary>
    private const float DeathProgress = 5f;

    /// <summary>
    /// 死亡段落的**兜底**还原时长（秒）。正常情况不用它 —— 玩家一离开战斗房（进入下一个房间）
    /// 就会立刻还原（`OnRoomEntered`）；这个计时器只是防止「一直停在结算界面」时曲目无限循环。
    /// ⚠ 以前这里是 5 秒，用户 2026-10-30 实测「胜利音效没播完就戛然而止」⇒ 死亡段落比 5 秒长。
    /// </summary>
    private const double DeathPlaybackSeconds = 20.0;

    private static bool _deathPlaybackScheduled;

    /// <summary>「死亡段落在放，等玩家离开战斗房再还原」这个待办是否挂着。</summary>
    private static bool _deathPendingRestore;

    /// <summary>这一场到底有没有**打死** Boss（决定还原时要不要放"战斗结束/胜利"那段）。</summary>
    /// <remarks>
    /// ⚠ v0.2.257（用户 2026-10-31：「多人使用快捷 SL 时会播放 boss 战胜利音效」）：
    ///   快捷 SL / 回滚会把当前战斗**拆掉重来**，Boss 是"活着被移出房间"的 —— 以前那条路也会走
    ///   `PlayDeathSectionThenRestore`（发 `<x>_progress = 5`）⇒ 一听就是胜利音。
    ///   现在只有**真打死**才放死亡段落；否则静默还原（`StopMusic + UpdateMusic`，不放 CombatEnd 段）。
    /// </remarks>
    private static bool _bossDiedThisFight;

    /// <summary>是否正在执行 <see cref="Restore(string)"/>（防重入：`UpdateTrack` 的补丁会回调进来）。</summary>
    private static bool _restoring;

    /// <summary>「兜底还原」这行日志本场只打一次（以前每帧一行，1 秒 9621 行把游戏写死）。</summary>
    private static bool _fallbackLogged;

    /// <summary>曲目事件名 → 原版那首曲子的进度参数名（拿不到返回 null）。</summary>
    private static string? DeathParamOf(string? track)
    {
        if (string.IsNullOrEmpty(track)) return null;
        if (track!.EndsWith("act1_boss_the_kin", StringComparison.Ordinal)) return "the_kin_progress";
        if (track.EndsWith("act3_boss_queen", StringComparison.Ordinal)) return "queen_progress";
        if (track.EndsWith("act2_boss_kaiser_crab", StringComparison.Ordinal)) return "kaiser_crab_progress";
        if (track.EndsWith("waterfall_giant", StringComparison.Ordinal)) return "waterfall_giant_progress";
        if (track.EndsWith("soul_fysh", StringComparison.Ordinal)) return "soulfysh_progress";  // 原版没有下划线
        return null;
    }

    /// <summary>最后一只 Boss 倒下：给曲目发「死亡段落」，放完再还原原版音乐。</summary>
    private static void PlayDeathSectionThenRestore(string reason)
    {
        try
        {
            string? param = DeathParamOf(_playing);
            if (param != null && SetParam(param, DeathProgress))
            {
                _deathPlaybackScheduled = true;
                _deathPendingRestore = true;
                Logger.Log($"[CharBoss] BGM：{reason} ⇒ 给「{param}」发死亡段落 {DeathProgress}" +
                           $"（原版 Boss 死亡就是这么放的；**等玩家离开这个房间**再还原原版音乐，" +
                           $"{DeathPlaybackSeconds:0.#} 秒兜底）（{DescribeState()}）");
                ScheduleRestore(DeathPlaybackSeconds);
                return;
            }
            Logger.Log($"[CharBoss] BGM：{reason} ⇒ 这首曲目没有已知的死亡段落参数 ⇒ 立刻还原原版音乐" +
                       $"（{DescribeState()}）");
            Restore(reason);
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] BGM 死亡段落失败（改为立刻还原）: " + e.Message);
            Restore(reason);
        }
    }

    /// <summary>
    /// **玩家离开战斗房、进入下一个房间**（挂在 `RunManager.EnterMapPointInternal` 前缀）：
    /// 如果 Boss 死亡段落还在放 / 还等着还原，就在这一刻把原版音乐还回去。
    ///
    /// 为什么不用固定计时器（用户 2026-10-30：「胜利音效没完全播完就戛然而止」）：
    /// 死亡段落有多长事先不知道，固定 5 秒会把它切断；而「玩家自己离开房间」这个时机
    /// 天然就是段落的终点 ⇒ 既不切歌，也不会让 Boss 曲目拖进地图。
    /// </summary>
    internal static void OnRoomEntered()
    {
        try
        {
            if (!_deathPendingRestore) return;
            if (_playing == null) { _deathPendingRestore = false; return; }
            Logger.Log($"[CharBoss] BGM：离开战斗房 → 还原原版音乐（死亡段落已完整放完）（{DescribeState()}）");
            Restore("离开战斗房");
        }
        catch { /* 忽略 */ }
    }

    /// <summary>用 `SceneTreeTimer` 安排一次**兜底**还原（不依赖任何每帧驱动，战斗结束后也一定跑）。</summary>
    ///
    /// ⚠⚠ **每个计时器都带一个代号（generation），旧的自动失效**（用户 2026-10-30：
    ///   「进阶 10 三层第二只 Boss 死亡时没播胜利音乐」，实测日志里 `已安排 2 秒后还原` 每 2 秒刷一次，
    ///   然后第二只 Boss 刚发完死亡段落，**上一场遗留的旧计时器**立刻触发 `Restore` 把它切掉了）。
    ///   现在：① 每次安排都把代号 +1，旧计时器的回调一进来就发现代号对不上、直接返回；
    ///   ② 计时器到点时如果还在战斗/结算流程里，**不再自己重排**（以前每 2 秒重排一次 ⇒ 刷屏 + 埋雷）。
    private static int _restoreGen;

    private static void ScheduleRestore(double seconds)
    {
        int gen = ++_restoreGen;
        try
        {
            var tree = Engine.GetMainLoop() as SceneTree;
            if (tree == null) { _deathPlaybackScheduled = false; Restore("拿不到 SceneTree，立刻还原"); return; }
            var timer = tree.CreateTimer(seconds);
            if (timer == null) { _deathPlaybackScheduled = false; Restore("拿不到计时器，立刻还原"); return; }
            timer.Timeout += () =>
            {
                try
                {
                    if (gen != _restoreGen) return;             // 旧计时器：这一场已经不是它管了
                    if (_playing == null) { _deathPlaybackScheduled = false; return; }
                    if (CombatInProgress()) return;              // 还在战斗/结算：交给「离开房间」那条路
                    _deathPlaybackScheduled = false;
                    Restore("Boss 死亡段落播完");
                }
                catch { /* 忽略 */ }
            };
            Logger.Log($"[CharBoss] BGM：已安排 {seconds:0.#} 秒后还原原版音乐（SceneTreeTimer #{gen}）");
        }
        catch (Exception e)
        {
            _deathPlaybackScheduled = false;
            Logger.Log("[CharBoss] BGM 安排还原失败（立刻还原）: " + e.Message);
            Restore("计时器失败，立刻还原");
        }
    }

    // ── 本幕音乐库诊断 ──────────────────────────────────────

    /// <summary>取本幕的音乐库路径（拿不到返回空数组）。同时打一行诊断，方便下次看是哪一套音乐。</summary>
    private static string[] StageMusicBanks()
    {
        try { return ActBankPaths() ?? Array.Empty<string>(); }
        catch { return Array.Empty<string>(); }
    }

    /// <summary>
    /// 兜底：任何时机的「还有没还原的曲目就还原它」。
    ///
    /// ⚠ 三重保护（用户 2026-10-30 实机崩溃就是这里炸的）：
    ///   ① **正在还原中**（`_restoring`）⇒ 立刻返回，绝不重入；
    ///   ② **死亡段落在放**（`_deathPlaybackScheduled`）⇒ 交给 `SceneTreeTimer`，不要提前切掉；
    ///   ③ 日志只打一次（`_fallbackLogged`）—— 以前每帧打一行，1 秒 9621 行直接把游戏写死。
    /// </summary>
    internal static void TickRestore(long nowUsec)
    {
        try
        {
            if (_restoring) return;
            if (_playing == null) { _restoreAtUsec = 0; return; }
            if (_deathPlaybackScheduled) return;        // Boss 死亡段落在放，等计时器
            if (CombatInProgress()) return;             // 新的一场已经开打：交给这一场自己管
            if (!_fallbackLogged)
            {
                _fallbackLogged = true;
                Logger.Log($"[CharBoss] BGM：兜底还原（{DescribeState()}）");
            }
            Restore("兜底还原");
        }
        catch (Exception e) { Logger.Log("[CharBoss] BGM 兜底还原失败: " + e.Message); }
    }

    /// <summary>立刻还原（换曲目 / 幕次守卫 / 用户手动切源时用）。</summary>
    internal static void Stop(string reason)
    {
        _restoreAtUsec = 0;
        Restore(reason);
    }

    /// <summary>真正执行还原：停掉我们起的 ogg，并把原版音乐要回来。</summary>
    ///
    /// ⚠⚠ **必须防重入，而且要在动手之前就把 `_playing` 清掉**（用户 2026-10-30 实机崩溃）：
    ///   本方法内部会调 `controller.UpdateTrack()`，而音乐追踪补丁在 `UpdateTrack` 的 postfix 里
    ///   会问「还有没有没还原的曲目」→ 以前 `_playing` 是**最后**才清的 ⇒ 那次 postfix 又走进
    ///   `TickRestore → Restore → UpdateTrack → …`，**无限递归**：
    ///   `mod_log.txt` 里 1 秒内 `BGM：兜底还原` 打了 **9621 行**，游戏直接卡死崩溃。
    ///   现在的顺序：先 `_playing = null`（+ `_restoring` 互斥），再动 FMOD。
    private static void Restore(string reason)
    {
        if (_restoring) return;                      // 已经在还原中（含补丁回调重入）⇒ 直接忽略
        _restoring = true;

        string? was = _playing;
        // ★ 先清状态：之后任何回调再问「还占着吗」都会得到「没有」
        _playing = null;
        _claimed = false;
        _claimedCombat = null;
        _playingEncounter = null;
        _queenPhase2 = false;
        _restoreAtUsec = 0;
        _deathPlaybackScheduled = false;
        _deathPendingRestore = false;
        _fallbackLogged = false;

        try
        {
            if (was == null) return;
            StopOgg();
            var controller = FindController();
            if (controller != null)
            {
                bool quiet = !_bossDiedThisFight;
                if (quiet)
                {
                    // ★ v0.2.257：这一场**没打死** Boss（快捷 SL/回滚、弃档、房间被拆）⇒
                    //   `StopCustomMusic()` 会「重播当前曲目 + Progress=7(CombatEnd)」，
                    //   听起来就是 Boss 战胜利音 —— 这里改用「停掉 + 让原版重新按房间解析 BGM」。
                    try { controller.StopMusic(); } catch { /* 忽略 */ }
                    try { controller.UpdateMusic(); } catch { /* 忽略 */ }
                    Logger.Log("[CharBoss] BGM：静默还原（本场没有打死 Boss）⇒ StopMusic + UpdateMusic，" +
                               "不放 CombatEnd/胜利段落");
                }
                else
                {
                    try { controller.StopCustomMusic(); } catch { /* 忽略 */ }
                }

                // ⚠ **一代音乐（A3 = ②）这条路会留下「战斗结束后整段没音乐」**：
                //   `TryPlayGen1` 为了压掉原版音乐调了 `StopMusic()`，而它是
                //   `stop_music` + `stop_ambience` + **`_currentTrack = null`** + 卸载这一幕的 bank。
                //   于是回头 `StopCustomMusic()` 只发 `stop_music`，`_currentTrack` 为空它**什么都不放回来**
                //   ⇒ 打完那三只故障机器人（黑球机 / 物理机 / 能力机）之后没有 BGM、也没有环境音。
                //   这里补一次 `UpdateMusic()`：重新按本局种子解析这一幕的 BGM、载回 bank、并刷环境音。
                if (!quiet && CurrentTrackOf(controller) == null)
                {
                    try { controller.UpdateMusic(); } catch { /* 忽略 */ }
                    Logger.Log("[CharBoss] BGM：还原时发现 _currentTrack 为空（一代音乐那条路会这样）" +
                               "⇒ 补一次 UpdateMusic() 把这一幕的 BGM 与环境音重新载回来");
                }

                // ⚠ 再补一次 `UpdateTrack()`：`StopCustomMusic()` 内部是「stop_music → 重新 update_music →
                //   设 Progress=7」，参数是紧跟着新起的那个事件实例设下去的，有可能被吞掉（用户 2026-10-30：
                //   「音乐有切换但并不是胜利音效」）。`UpdateTrack()` 是游戏自己在战斗结束时做的同一件事
                //   （`update_global_parameter("Progress", GetTrack(现在这个房间))`），补一次只是同值重复，无副作用。
                //   ⚠ 它的 postfix 会调 `TickRestore`，靠上面的 `_restoring` + 已清空的 `_playing` 拦住。
                try { controller.UpdateTrack(); } catch { /* 忽略 */ }
            }
            _bossDiedThisFight = false;          // 还原完就归零（下一场重新判）
            _loggedVanillaBgmFight = false;      // 下一场「人家自己有专属曲目」还能再报一次
            Logger.Log($"[CharBoss] BGM：还原原版音乐（{reason}；原来在放 {was}）");
        }
        catch (Exception e) { Logger.Log("[CharBoss] BGM 停止失败: " + e.Message); }
        finally { _restoring = false; }
    }

    /// <summary>诊断用：一句话说清 BGM 的状态（写进日志，方便下次只凭日志判断）。</summary>
    internal static string DescribeState()
    {
        try
        {
            string pending = _restoreAtUsec == 0
                ? "无"
                : ((_restoreAtUsec - (long)Time.GetTicksUsec()) / 1000L) + "ms 后还原";
            return $"曲目={_playing ?? "<空>"} 占着={(_claimed ? "是" : "否")} 待还原={pending} " +
                   $"encounter={_playingEncounter ?? "<空>"}";
        }
        catch { return "曲目=<读数失败>"; }
    }

    /// <summary>现在是否处在任何战斗里（原版 <c>CombatManager.IsInProgress</c>）。</summary>
    private static bool CombatInProgress()
    {
        try { return MegaCrit.Sts2.Core.Combat.CombatManager.Instance?.IsInProgress == true; }
        catch { return false; }
    }

    /// <summary>我们现在放的是不是第三幕那首「女王」（永世沙漏的兜底也是它）。</summary>
    private static bool IsVantomTrack()
    {
        // 碎屑储君是**我们自己**把曲子指定成墨影幻灵那一首的 ⇒ 直接按「这一场是哪个遭遇」判断最稳
        // （BossBgm 里没有 `_currentTrack` 这个字段，那是游戏自己的）。
        try { return _playingEncounter != null && _playingEncounter.Contains("REGENT_DEBRIS"); }
        catch { return false; }
    }

    private static bool IsQueenTrack()
        => _playing != null && _playing.EndsWith("act3_boss_queen", StringComparison.Ordinal);

    // ── 内部实现 ───────────────────────────────────────────

    /// <summary>
    /// A3 选项②：播一代 ogg。★ 用户 2026-10-31 起**所有** Boss 都走这条（按自己所在幕挑曲），
    /// 只有 ogg 载入失败时才返回 false 让调用方退回别的方案。
    /// </summary>
    private static bool TryPlayGen1(string? encounterEntry, int act)
    {
        var resPath = Gen1OggFor(encounterEntry, act);
        if (string.IsNullOrEmpty(resPath)) return false;

        var controller = FindController();
        if (controller == null) { Logger.Log("[CharBoss] BGM：没找到 NRunMusicController，跳过"); return false; }

        StopOgg();

        // ⚠ 先**载入成功**再压掉原版音乐 —— 否则一旦 ogg 读不出来，
        // 就变成「原版音乐被停掉 + 我们的也没响」= 整场没有音乐（用户 2026-10-28 实测愤怒战无音乐）。
        AudioStream? stream = null;
        try { stream = AudioStreamOggVorbis.LoadFromFile(resPath); }
        catch (Exception le) { stream = null; Logger.Log("[CharBoss] BGM：ogg 载入异常 " + le.Message); }
        if (stream == null)
        {
            try { stream = ResourceLoader.Load<AudioStream>(resPath); } catch { stream = null; }
        }
        if (stream == null)
        {
            Logger.Log($"[CharBoss] BGM：ogg 载入失败（保持原版音乐）{resPath}");
            return false;
        }

        try { controller.Call("StopMusic"); } catch { /* 版本差异不致命 */ }
        try { if (stream is AudioStreamOggVorbis oggStream) oggStream.Loop = true; } catch { /* 忽略 */ }

        _player = new AudioStreamPlayer { Stream = stream, VolumeDb = -6f, Autoplay = true };
        controller.AddChild(_player);
        _player.Play();
        _playing = resPath;
        _claimed = true;
        _claimedCombat = CurrentCombatState();
        Logger.Log($"[CharBoss] BGM：{encounterEntry} → 播一代 {System.IO.Path.GetFileName(resPath)}（A3 = ②）");
        return true;
    }

    /// <summary>A3 选项①：按对照表播原版其他 Boss 的 FMOD 曲目。</summary>
    private static void PlayVanillaOther(string encounterEntry, int act)
    {
        var track = Lookup(encounterEntry);
        if (track.IsKeepVanilla)
        {
            Stop("这一场保持原版默认 Boss 音乐");
            Logger.Log($"[CharBoss] BGM：{encounterEntry} → {track.Name}");
            return;
        }
        if (track.Act >= 0 && track.Act != act)
        {
            // 硬播别的幕的事件 = 找不到事件（那个 bank 没加载）⇒ 会变成整场没音乐，所以宁可不接管
            Stop("曲目属于别的幕，不接管");
            Logger.Log($"[CharBoss] BGM：{encounterEntry} 的曲目「{track.Name}」属于第 {track.Act + 1} 幕，" +
                       $"当前是第 {act + 1} 幕 ⇒ 不接管，保持原版音乐");
            return;
        }

        var controller = FindController();
        if (controller == null) { Logger.Log("[CharBoss] BGM：没找到 NRunMusicController，跳过"); return; }

        // ⚠ 先确认**这一幕的 bank 里真的有这个事件**再切（用户 2026-10-30 的 `godot (5).log` 实证）：
        //     `cannot find music path: event:/music/act1_b_boss_waterfall_giant`
        //   一代二幕有两套 bank（`act1_*` / `act1_b_*`），哪一套被加载取决于**这张地图用哪套配乐**，
        //   所以「幕次对」并不等于「事件存在」。而 `PlayCustomMusic` 是 `stop_music` + `update_music` ——
        //   事件找不到时原版音乐已经被停了 ⇒ **那一整场没音乐**。
        //   现在用游戏自己的 `ActBankLoadRetry`（3 次 × 50ms）问一遍 proxy：`load_act_bank(bank, 事件)` 为真
        //   才算「这首歌这一幕确实能放」。
        //
        // ★ v0.2.270（用户 2026-10-31：「修 4」）：找不到时**不再直接放弃**，而是按顺序退让：
        //   ① 同一首曲换 bank 命名（`act1_b_boss_x` ↔ `act1_boss_x`）；
        //   ② 本模组自带的一代 ogg（PCK 里的资源，任何幕都能播）；
        //   ③ 表里其它曲目（挑这一幕 bank 里真有的，按表顺序，结果稳定）；
        //   ④ 全都不行才不接管、留原版音乐。
        string chosen = null;
        string chosenHow = "";
        foreach (var candidate in EventCandidates(track.Event))
        {
            if (EventAvailable(controller, candidate, out string how))
            {
                chosen = candidate;
                chosenHow = how;
                break;
            }
        }

        if (chosen == null)
        {
            // ② 自带 ogg：资源文件，不依赖这一幕的 bank
            if (TryPlayGen1(encounterEntry, act))
            {
                Logger.Log($"[CharBoss] BGM：{encounterEntry} 的曲目「{track.Name}」在 FMOD 里找不到" +
                           $"（{chosenHow}）⇒ 改播**本模组自带的一代曲目**（不依赖 bank）");
                return;
            }

            Stop("这一幕的 bank 里没有可用曲目，不接管");
            Logger.Log($"[CharBoss] BGM：{encounterEntry} 的曲目「{track.Name}」{track.Event} 在 FMOD 里找不到" +
                       $"（{chosenHow}），本模组也没有这一场的自带曲目 ⇒ 不接管，保持原版音乐" +
                       "（⚠ 直接切会变成整场没音乐）");
            return;
        }

        if (!string.Equals(chosen, track.Event, StringComparison.Ordinal))
        {
            Logger.Log($"[CharBoss] BGM：{encounterEntry} 的原曲「{track.Name}」{track.Event} 在本幕 bank 里没有" +
                       $"（{chosenHow}）⇒ 改用 {chosen}");
        }

        StopOgg();
        try { controller.PlayCustomMusic(chosen); }
        catch (Exception e) { Logger.Log("[CharBoss] BGM：PlayCustomMusic 失败（保持原版）: " + e.Message); return; }

        _playing = chosen;
        _claimed = true;
        _claimedCombat = CurrentCombatState();
        Logger.Log($"[CharBoss] BGM：{encounterEntry} → 「{track.Name}」{chosen}（A3 = {BossConfig.DescribeBgmSource(BossConfig.BgmSource)}）");

        // 女王那首：开局就把分段参数写成「一阶段」，与「原版女王自己会发的那一次」对齐。
        // （以前只有 IRONCLAD_VULN / SILENT_BREWER 两个 encounter 会发，其余走这首曲的 Boss 一次也没发过。）
        if (IsQueenTrack())
        {
            try { controller.UpdateMusicParameter(QueenParam, QueenPhase1); } catch { /* 忽略 */ }
        }
    }

    /// <summary>
    /// 曲目候选（v0.2.270，按顺序去重）：
    ///   ① 原曲事件本身；
    ///   ② 换 bank 命名的同一首（`act1_b_boss_x` ↔ `act1_boss_x` —— 一代二幕的两套 bank 就是这个差别）；
    ///   ③ 表里其它曲目（按表顺序，结果稳定）。
    /// 调用方逐个问 `EventAvailable`，第一个"这一幕真能播"的就是答案。
    /// </summary>
    private static IEnumerable<string> EventCandidates(string preferred)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        if (!string.IsNullOrEmpty(preferred) && seen.Add(preferred)) yield return preferred;

        if (!string.IsNullOrEmpty(preferred))
        {
            string alt = preferred.Contains("_b_boss_", StringComparison.Ordinal)
                ? preferred.Replace("_b_boss_", "_boss_", StringComparison.Ordinal)
                : preferred.Replace("_boss_", "_b_boss_", StringComparison.Ordinal);
            if (seen.Add(alt)) yield return alt;
        }

        foreach (var other in FmodByEncounter.Values)
        {
            if (string.IsNullOrEmpty(other.Event)) continue;
            if (seen.Add(other.Event)) yield return other.Event;
        }
    }

    /// <summary>遭遇 → 曲目（查不到 = 兜底「永世沙漏」）。</summary>
    private static Track Lookup(string encounterEntry)
        => FmodByEncounter.TryGetValue(encounterEntry, out var track) ? track : Fallback;


    /// <summary>
    /// 这首 FMOD 事件在**这一幕的 bank** 里存在吗。
    ///
    /// 用游戏自己的机制问：`NRunMusicController` 的 `Proxy` 节点有 `load_act_bank(bankPath, verifyEvent)`
    /// （反编译 `NRunMusicController.LoadActBank` 就是这么调的，返回是否已加载且**能找到这个事件**），
    /// 外面再套一层官方重试 `ActBankLoadRetry.Run`（3 次 × 50ms，防止 bank 还在加载时报假阴性）。
    ///
    /// 拿不到 proxy / bank 列表时**一律当真**（返回 true）—— 检查不出来就别拦，行为与以前一致。
    /// </summary>
    private static bool EventAvailable(NRunMusicController controller, string evt, out string detail)
    {
        detail = "未检查";
        try
        {
            var proxy = ProxyOf(controller);
            if (proxy == null) { detail = "拿不到 proxy，未检查"; return true; }
            var banks = ActBankPaths();

            // ★ v0.2.380（用户 2026-11-04「还是没换」）：一代（第一幕）有**两套** bank
            //   （`act1_*` / `act1_b1`），同一张地图只加载其中一套。墨影幻灵那首
            //   `event:/music/act1_boss_vantom` 只存在于 **`act1_b1.bank`** 里，
            //   本幕加载的若是另一套 ⇒ 原来这里只拿 `ActBankPaths()`（本幕那套）去问，永远问不到。
            //   `load_act_bank(bank, 事件)` 本身就会**加载** bank（反编译 `NRunMusicController.LoadActBank`），
            //   所以把另一套也塞进来问一次：能加载就返回真，事件从此可播。
            if (banks != null && banks.Length > 0)
            {
                const string extraBank = "res://banks/desktop/act1_b1.bank";
                bool already = false;
                foreach (var b0 in banks) if (string.Equals(b0, extraBank, StringComparison.OrdinalIgnoreCase)) { already = true; break; }
                if (!already)
                {
                    var merged = new string[banks.Length + 1];
                    Array.Copy(banks, merged, banks.Length);
                    merged[banks.Length] = extraBank;
                    banks = merged;
                }
            }
            if (banks == null || banks.Length == 0) { detail = "拿不到本幕 bank 列表，未检查"; return true; }

            foreach (var bank in banks)
            {
                if (string.IsNullOrEmpty(bank)) continue;
                bool ok;
                try
                {
#if STS2_107
                    ok = proxy.Call("load_act_bank", bank, evt).AsBool();
#else
                    ok = MegaCrit.Sts2.Core.Nodes.Audio.ActBankLoadRetry.Run(
                        bank, () => proxy.Call("load_act_bank", bank, evt).AsBool());
#endif
                }
                catch { continue; }        // 这个 bank 不行，试下一个
                if (ok) { detail = $"在 {System.IO.Path.GetFileName(bank)} 里"; return true; }
            }
            detail = $"本幕 {banks.Length} 个 bank 里都没有";
            return false;
        }
        catch (Exception e) { detail = "检查异常（按有处理）" + e.Message; return true; }
    }

    /// <summary>`NRunMusicController._proxy`（私有字段）：真正跟 FMOD 打交道的那颗节点。</summary>
    private static Godot.Node? ProxyOf(NRunMusicController controller)
    {
        try
        {
            _proxyField ??= HarmonyLib.AccessTools.Field(typeof(NRunMusicController), "_proxy");
            return _proxyField?.GetValue(controller) as Godot.Node;
        }
        catch { return null; }
    }

    private static System.Reflection.FieldInfo? _proxyField;

    /// <summary>本幕的 FMOD bank 路径表（`ActModel.MusicBankPaths`）。</summary>
    private static string[]? ActBankPaths()
    {
        try { return MultiBossPlan.RunStateOrNull()?.Act?.MusicBankPaths; }
        catch { return null; }
    }

    /// <summary>给当前正在播的曲目设一个事件内参数（拿不到控制器 / 事件还没起就静默忽略）。</summary>
    /// <returns>是否真的把调用发出去了。</returns>
    private static bool SetParam(string label, float value)
        => TryController(c => c.UpdateMusicParameter(label, value));

    private static bool TryController(Action<NRunMusicController> action)
    {
        try
        {
            var controller = FindController();
            if (controller == null)
            {
                Logger.Log("[CharBoss] BGM：拿不到 NRunMusicController，参数没发出去");
                return false;
            }
            action(controller);
            return true;
        }
        catch (Exception e)
        {
            Logger.Log($"[CharBoss] BGM：调用失败 {e.Message}");
            return false;
        }
    }

    /// <summary>当前是第几幕（0 起）。拿不到按 0。</summary>
    private static int CurrentActIndex()
        => BossActIndex.Resolve(() => MegaCrit.Sts2.Core.Combat.CombatManager.Instance?.DebugOnlyGetState());

    /// <summary>当前这一份战斗状态（**只当「这是哪一场」的身份用** —— 同一场里每只 Boss 拿到的都是同一个对象）。</summary>
    private static object? CurrentCombatState()
    {
        try { return MegaCrit.Sts2.Core.Combat.CombatManager.Instance?.DebugOnlyGetState(); }
        catch { return null; }
    }

    /// <summary>本场遭遇是不是自带专属曲目（`EncounterModel.HasBgm`；原版/别的模组的 Boss 会有）。</summary>
    private static bool CurrentEncounterHasBgm(out string customBgm)
    {
        customBgm = string.Empty;
        try
        {
            var encounter = MegaCrit.Sts2.Core.Combat.CombatManager.Instance?.DebugOnlyGetState()?.Encounter;
            if (encounter == null) return false;
            var bgm = encounter.CustomBgm;
            if (string.IsNullOrEmpty(bgm)) return false;
            customBgm = bgm;
            return true;
        }
        catch { return false; }
    }

    /// <summary>`NRunMusicController._currentTrack`（私有字段）。它为空时 `StopCustomMusic()` **不会**放出音乐。</summary>
    private static string? CurrentTrackOf(NRunMusicController controller)
    {
        try
        {
            _currentTrackField ??= HarmonyLib.AccessTools.Field(typeof(NRunMusicController), "_currentTrack");
            return _currentTrackField?.GetValue(controller) as string;
        }
        catch { return null; }
    }

    private static System.Reflection.FieldInfo? _currentTrackField;

    private static void StopOgg()
    {
        try
        {
            if (_player != null && GodotObject.IsInstanceValid(_player))
            {
                _player.Stop();
                _player.QueueFree();
            }
        }
        catch { /* 忽略 */ }
        _player = null;
    }

    /// <summary>拿音乐控制器：先问 <see cref="NRunMusicController.Instance"/>，拿不到再在场景树里找。</summary>
    private static NRunMusicController? FindController()
    {
        try
        {
            var direct = NRunMusicController.Instance;
            if (direct != null) return direct;
        }
        catch { /* 退回场景树 */ }

        try
        {
            var tree = Engine.GetMainLoop() as SceneTree;
            var root = tree?.Root;
            return root == null ? null : FindIn(root);
        }
        catch { return null; }
    }

    private static NRunMusicController? FindIn(Node node)
    {
        try
        {
            if (node is NRunMusicController c) return c;
            foreach (var child in node.GetChildren())
            {
                if (child is not Node n) continue;
                var found = FindIn(n);
                if (found != null) return found;
            }
        }
        catch { /* 忽略 */ }
        return null;
    }
}

