using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Runs;

namespace CharacterBosses.Core;

/// <summary>
/// **本地对战数据记录**（用户 2026-10-30：「有些模组会收集玩家的游戏数据（先询问玩家）来帮助
/// 作者平衡与修 bug，你能不能也给本模组搞一个」）。
///
/// ## 原则（先写清楚，出问题先看这段）
///   1. **默认关闭**：只有玩家自己在「模组配置 → CharacterBosses → A5」里打开才会记录；
///      打开这个动作本身就是**知情同意**，我们会在数据文件第一行写一条 `consent` 记录（含时间与版本）。
///   2. **只写本地文件，绝不上传、不联网**：文件在
///      `%APPDATA%\SlayTheSpire2\mod_configs\CharacterBosses\telemetry\charbosses-telemetry.jsonl`
///      （一行一条 JSON）。玩家想给作者就直接把这个文件发出去。
///   3. **不含身份信息**：不写 SteamID / 昵称 / 存档路径；只有「几人局」「进阶几」「哪只 Boss」这类
///      与平衡相关的事实。
///   4. **绝不影响游戏**：所有写入都 `try/catch` 吞掉异常，且用 `FileShare.ReadWrite` 打开
///      （双开时两个进程写同一个文件也不会互相炸，教训见 `SafeLogPatch`）。
///   5. 文件超过 <see cref="MaxBytes"/> 就轮转成 `.1`（只留一份旧的）。
///
/// ## 记什么
///   · `boss_fight`：每场**我们的** Boss 战结束时一条 —— 幕/场、遭遇、进阶、人数、Boss 血量与剩余、
///     回合数、胜负、本局种子、以及当时的几项设置（是否混匹等）。用来回答「哪只 Boss 太难/太简单」。
///   · `diag`：我们自己打出的**异常/失败/不同步**日志行（不同步摘要 1500 字、其余 300 字），用来定位 bug。
/// </summary>
public static class Telemetry
{
    /// <summary>单文件上限（超过就轮转成 `.1`，避免无限增长）。</summary>
    private const long MaxBytes = 4L * 1024 * 1024;

    /// <summary>`diag` 每次进程最多记这么多条（防止某个循环刷爆文件）。</summary>
    private const int MaxDiagPerSession = 300;

    /// <summary>
    /// diag 行写进表里的**字数上限**。
    ///
    /// ★ v0.2.311（用户 2026-11-02：「表格中收到了不同步报错…后面没了，是不是一次只能上传这么多」）：
    /// 不是飞书的限制，是**我们自己**截断到 300 字 —— 而「不同步状态摘要」天生是多行的
    /// （轮次 / 遭遇 / 每个玩家 / 每个 Boss 各一行），300 字刚够写到第 2 个 Boss 就断了。
    /// 现在：**不同步摘要放宽到 1500 字**（飞书多行文本列完全吃得下），其余仍 300 字。
    ///
    /// ★ v0.2.464（2026-11-07）：**1500 还是不够** —— 玩家上报的那条正好卡在 1500，
    ///   截断点正好落在「回合快照」中间（=`DivergenceStateProbe.History`，一行就 300+ 字），
    ///   最有用的「两端第一处分叉」整段被切掉 ⇒ 报告等于没用。现在抬到 4000 字，
    ///   并让群消息按**总字数**分批（见 <see cref="BotMsgChars"/>），长行不会再被顶掉。
    /// </summary>
    private const int DiagLineLimit = 300;

    private const int DiagLineLimitDesync = 4000;

    private static string ClipDiagLine(string message)
    {
        int limit = message.Contains("不同步", StringComparison.Ordinal) ? DiagLineLimitDesync : DiagLineLimit;
        return message.Length > limit ? message.Substring(0, limit) : message;
    }

    private static readonly object Gate = new();
    private static int _diagCount;
    private static bool _consentWritten;

    /// <summary>玩家有没有打开记录（= 知情同意）。</summary>
    internal static bool Enabled
    {
        get { try { return BossConfig.TelemetryEnabled; } catch { return false; } }
    }

    private static string Dir
    {
        get
        {
            try
            {
                string baseDir = BossConfig.ConfigDirectory;
                if (string.IsNullOrEmpty(baseDir)) return string.Empty;
                return Path.Combine(baseDir, "telemetry");
            }
            catch { return string.Empty; }
        }
    }

    /// <summary>数据文件路径（配置界面显示用；拿不到返回空串）。</summary>
    internal static string FilePath
    {
        get
        {
            string dir = Dir;
            return string.IsNullOrEmpty(dir) ? string.Empty : Path.Combine(dir, "charbosses-telemetry.jsonl");
        }
    }

    // ── 记录入口 ──────────────────────────────────────────

    internal static void NoteBossEnded(Monsters.CharBossMonster? boss, bool bossDead, bool playersAlive,
                                       int playerCount, bool anyOtherBossAlive)
    {
        if (!Enabled || boss == null) return;
        try
        {
            var combat = boss.CombatState;

            // 换了一场战斗 ⇒ 清空累积
            if (!ReferenceEquals(combat, _fightCombat))
            {
                _fightCombat = combat;
                _fightBosses.Clear();
                _fightWritten = false;
            }

            // 登记这一只（同一只可能被多次触发，覆盖式更新）
            FightBossInfo? info = null;
            foreach (var pair in _fightBosses)
            {
                if (ReferenceEquals(pair.Boss, boss)) { info = pair.Info; break; }
            }
            if (info == null)
            {
                info = new FightBossInfo { Key = SafeKey(boss) };
                _fightBosses.Add((boss, info));
            }
            info.MaxHp = SafeMaxHp(boss);
            info.Hp = SafeHp(boss);
            info.Dead = bossDead;
            info.Turns = Math.Max(info.Turns, SafeTurns(boss));

            // 场上还有别的我们的 Boss ⇒ 这一场还没结束，**先不写**（等最后一只）
            if (anyOtherBossAlive)
            {
                try
                {
                    Logger.Log($"[CharBoss] 数据记录：{info.Key} 已结束（死={bossDead}），" +
                               $"但场上还有别的 Boss ⇒ 等最后一只再写这一场的记录");
                }
                catch { /* 忽略 */ }
                return;
            }

            FinalizeFight(boss, playersAlive, playerCount);
        }
        catch { /* 记录失败绝不影响游戏 */ }
    }

    /// <summary>
    /// **战斗结束兜底**（`Hook.AfterCombatEnd`）：玩家全灭时最后一只 Boss **未必会被移出房间**，
    /// 只靠 `BeforeRemovedFromRoom` 会漏掉所有「输」的局（用户 2026-10-31 提问点出来的）。
    ///
    /// 反编译确认 `CombatManager.EndCombatInternal` **无论胜负**都会调 `Hook.AfterCombatEnd`
    /// （`AfterCombatVictory` 才只在胜利时调），所以这里补一刀：
    ///   · 把场上还活着的我们的 Boss 也登记进来（它们就是「没打死的那几只」）；
    ///   · 已经写过这一场就直接返回（幂等，胜利时不会写两行）。
    /// </summary>
    internal static void FinalizeFightAtCombatEnd(MegaCrit.Sts2.Core.Combat.ICombatState? combat,
                                                  IRunState? runState = null)
    {
        if (!Enabled || combat == null) return;
        try
        {
            if (ReferenceEquals(combat, _fightCombat) && _fightWritten) return;

            Monsters.CharBossMonster? anchor = null;
            foreach (var list in new[] { combat.Enemies, combat.Allies })
            {
                if (list == null) continue;
                foreach (var creature in list)
                {
                    if (creature?.Monster is not Monsters.CharBossMonster boss) continue;
                    anchor ??= boss;

                    if (!ReferenceEquals(combat, _fightCombat))
                    {
                        _fightCombat = combat;
                        _fightBosses.Clear();
                        _fightWritten = false;
                    }

                    FightBossInfo? known = null;
                    foreach (var pair in _fightBosses)
                    {
                        if (ReferenceEquals(pair.Boss, boss)) { known = pair.Info; break; }
                    }

                    bool dead = false;
                    try { dead = !creature.IsAlive; } catch { dead = false; }

                    if (known == null)
                    {
                        _fightBosses.Add((boss, new FightBossInfo
                        {
                            Key = SafeKey(boss),
                            MaxHp = SafeMaxHp(boss),
                            Hp = SafeHp(boss),
                            Dead = dead,
                            Turns = SafeTurns(boss),
                        }));
                    }
                    else if (dead && !known.Dead)
                    {
                        known.Dead = true;
                        known.Hp = SafeHp(boss);
                    }
                }
            }

            if (anchor == null || _fightBosses.Count == 0) return;

            int playerCount = 0;
            bool anyAlive = false;
            try
            {
                var players = Core.BossLockOn.RealPlayers(combat);
                playerCount = players?.Count ?? 0;
                foreach (var each in players ?? new List<MegaCrit.Sts2.Core.Entities.Players.Player>())
                {
                    if (each?.Creature != null && each.Creature.IsAlive) { anyAlive = true; break; }
                }
            }
            catch { playerCount = 0; anyAlive = false; }

            // ★ v0.2.269（用户 2026-10-31：「目前数据中一个失败都没有」）：
            //   战败时 `CombatState.Players` 有可能**已经被清空/拆掉**（实测日志里出现过「真人玩家 0 人」），
            //   于是 `playerCount > 0 && !anyAlive` 这条判负条件永远不成立 ⇒ 输的局被当成 aborted 丢掉。
            //   两个补救：① 玩家数退回「场上所有 IsPlayer 且不是影子」的生物；
            //            ② 再看一眼整局是否已经结束（`IRunState.IsGameOver`）—— 那基本就是战败。
            if (playerCount == 0)
            {
                try
                {
                    foreach (var creature in combat.Creatures ?? (IReadOnlyList<Creature>)Array.Empty<Creature>())
                    {
                        if (creature == null || !creature.IsPlayer || creature.Monster != null) continue;
                        playerCount++;
                        if (creature.IsAlive) anyAlive = true;
                    }
                }
                catch { /* 忽略 */ }
            }

            bool gameOver = false;
            try { gameOver = runState?.IsGameOver == true; } catch { gameOver = false; }

            Logger.Log($"[CharBoss] 数据记录：战斗结束兜底（Hook.AfterCombatEnd）⇒ 汇总这一场" +
                       $"（Boss {_fightBosses.Count} 只、真人玩家 {playerCount} 人、" +
                       $"{(anyAlive ? "玩家还有活着的" : "玩家全灭")}、整局已结束={gameOver}）");
            FinalizeFight(anchor, anyAlive, playerCount, gameOver);
        }
        catch { /* 记录失败绝不影响游戏 */ }
    }

    /// <summary>把这一场汇总成**一行**写出去（用户 2026-10-31：「一场战斗 1 行」）。</summary>
    private static void FinalizeFight(Monsters.CharBossMonster boss, bool playersAlive, int playerCount,
                                      bool gameOver = false)
    {
        try
        {
            if (_fightWritten) return;                 // 幂等：同一场只写一次
            if (_fightBosses.Count == 0) return;

            bool allDead = _fightBosses.All(p => p.Info.Dead);
            // 判负：Boss 没全死，且「玩家全灭」或「整局已经结束」（战败 / 被放弃）
            bool lost = !allDead && (gameOver || (playerCount > 0 && !playersAlive));

            // ★ v0.2.276（用户 2026-10-31 实测：故意被打死的那一局**没上传**）：
            //   战败时 `BeforeRemovedFromRoom`（这条写库路径）拿到的是 `真人玩家 0 人` ——
            //   `CombatState.Players` 那时已经被拆掉了 ⇒ `playerCount > 0` 永不成立 ⇒ 判成 aborted。
            //   补一道**基于证据**的兜底（两条写库路径都经过 FinalizeFight，改一处全管）：
            //   直接看**本局存档里的玩家生物**是否还活着 —— 全死 = 战败；还有人活着 = 中途退出，仍算 aborted。
            //   （不能用「战斗里数不到活人」当判据：SL / 控制台结束同样是数不到人。）
            if (!lost && !allDead)
            {
                bool runAllDead = RunPlayersAllDead(out int runCount);
                bool runGameOver = false;
                try { runGameOver = MegaCrit.Sts2.Core.Runs.RunManager.Instance?.DebugOnlyGetState()?.IsGameOver == true; }
                catch { runGameOver = false; }

                if (runAllDead) lost = true;

                Logger.Log($"[CharBoss] 数据记录：判负兜底 ⇒ 本局玩家 {runCount} 人、" +
                           $"存活={!runAllDead}、整局已结束={runGameOver}、" +
                           $"战斗里数到 {playerCount} 人/{(playersAlive ? "有活着的" : "数不到活人")} " +
                           $"⇒ {(lost ? "按**战败**记" : "仍按未打完")}");
            }
            bool aborted = !allDead && !lost;

            string encounter = SafeEncounter(boss);
            string lineup = SafeLineup(boss);

            // 控制台点名打的遭遇和「本幕本场阵容」对不上时，lineup 前面标 `console:`
            try
            {
                if (!string.IsNullOrEmpty(lineup) && !string.IsNullOrEmpty(encounter)
                    && !LineupCoversEncounter(lineup, encounter))
                {
                    lineup = "console:" + lineup;
                }
            }
            catch { /* 标注失败不影响记录 */ }

            if (aborted)
            {
                lineup = "aborted:" + lineup;
                try
                {
                    Logger.Log("[CharBoss] 数据记录：这一场**没有真正打完**" +
                               "（打一半退出 / 弃档 / 控制台结束 / 读档回滚…）" +
                               "⇒ result 留空、lineup 标 aborted，**只写本机、不上传**" +
                               $"（Boss {_fightBosses.Count} 只，其中死 {_fightBosses.Count(p => p.Info.Dead)} 只；" +
                               $"真人玩家 {playerCount} 人、{(playersAlive ? "还有活的" : "全灭/未知")}）");
                }
                catch { /* 忽略 */ }
            }

            var main = _fightBosses[0].Info;                       // lineup 第 0 位 = 主 Boss
            int hpTotal = _fightBosses.Sum(p => p.Info.Hp);
            int maxHpTotal = _fightBosses.Sum(p => p.Info.MaxHp);
            int killed = _fightBosses.Count(p => p.Info.Dead);

            var fields = new Dictionary<string, object?>
            {
                ["kind"] = "boss_fight",
                ["act"] = SafeAct(boss) + 1,
                ["encounter"] = encounter,
                ["lineup"] = lineup,
                ["boss"] = main.Key,
                ["ascension"] = SafeAscension(),
                // 人数即「单人 / 多人」的判据（用户 2026-10-31：不需要额外的 mode 列）
                ["players"] = SafePlayers(),
                ["bossMaxHp"] = main.MaxHp,
                ["bossHp"] = main.Hp,
                ["turns"] = _fightBosses.Max(p => p.Info.Turns),
                ["seed"] = SafeSeed(),
                ["cfg"] = SettingsSnapshot(),

                // ── v0.2.253 新增：一场战斗的汇总（多人多 Boss 时才有意义）──
                ["bossTotal"] = _fightBosses.Count,
                ["bossesKilled"] = killed,
                ["maxHpTotal"] = maxHpTotal,
                ["hpTotal"] = hpTotal,
                ["bossList"] = string.Join(",", _fightBosses.Select(p => p.Info.Key)),
                ["hpList"] = string.Join(";", _fightBosses.Select(p => $"{p.Info.Key}:{p.Info.Hp}")),
            };
            if (!aborted) fields["result"] = allDead ? "win" : "lose";

            // ★ v0.2.264：玩家侧的血量证据（轻松赢 / 险胜的区别就在这几个字段里）
            AppendPlayerHpFields(fields, _fightCombat);

            // ★ v0.2.248：联机时**只有房主上传**对局数据，客人只写本机；每条带 `role`。
            fields["role"] = RoleName();
            bool upload = Core.NetRole.IsHostOrSolo && !aborted;
            Write(fields, queueForUpload: upload);

            // ★ v0.2.265：这一场攒下的 diag 在**对局数据之后**写，且和它同样只在真正打完时上传
            FlushFightDiagnostics(upload, aborted ? null : (allDead ? "win" : "lose"), main.Key);

            Logger.Log($"[CharBoss] 数据记录（一场一行）：{main.Key} 为主，" +
                       $"共 {_fightBosses.Count} 只 Boss 打死 {killed} 只，" +
                       $"{(aborted ? "未打完（不上传）" : (allDead ? "win" : "lose"))}，" +
                       $"血量 {hpTotal}/{maxHpTotal}，回合 {fields["turns"]}");

            _fightWritten = true;                 // 这一场已写；等下一场开始时复位
        }
        catch { /* 记录失败绝不影响游戏 */ }
    }

    /// <summary>一场战斗里的一只 Boss（用于「一场战斗一行」的汇总）。</summary>
    private sealed class FightBossInfo
    {
        public string Key = "?";
        public int MaxHp;
        public int Hp;
        public bool Dead;
        public int Turns;
    }

    /// <summary>本场战斗里已经结束的 Boss（换了战斗就清空）。</summary>
    private static readonly List<(Monsters.CharBossMonster Boss, FightBossInfo Info)> _fightBosses = new();

    /// <summary>当前累积属于哪一场战斗。</summary>
    private static object? _fightCombat;
    /// <summary>这一场是否已经写过（`AfterCombatEnd` 兜底与「最后一只被移出」两条路都靠它去重）。</summary>
    private static bool _fightWritten;

    // ── 玩家血量统计（v0.2.264，用户 2026-10-31）────────────────────────────
    //
    // 用户要回答的问题：「玩家是**轻松赢**还是**险胜**」—— 只看 win/lose 与 Boss 剩余血量分不出来。
    // 但不能简单用「进入血量 − 结束血量」：缩放仪（Pantograph，Boss 战开局回血）、再生药水这类
    // 会在战斗中回血，会把「实际挨了多少」抹平。所以分开记：
    //   · pHpStart / pHpEnd / pMaxHp —— 快照，给「这场压力多大」一个背景；
    //   · pDmg —— **战斗中真正掉的血**（所有负 HP 变化累加，含自伤 / 献祭）；
    //   · pHeal —— **战斗中回的血**（正 HP 变化累加，含药水 / 遗物 / 卡牌）。
    // 数据来源：`Hook.BeforeCombatStart` 拍快照 + `Hook.AfterCurrentHpChanged` 累加（都是同步钩子，
    // 不能在帧回调里做 —— 见钻石头冠那次的联机不同步教训）。
    // 只统计**真人玩家**：影子 Player 的 Creature 就是 Boss 本体，必须按 `BossLockOn.RealPlayers` 滤掉。

    /// <summary>这份玩家血量统计属于哪一场战斗。</summary>
    private static object? _hpCombat;

    /// <summary>是否已抓到开局快照。</summary>
    private static bool _hpSnapshot;

    /// <summary>开局快照：(生物, 最大生命, 开局生命)。</summary>
    private static readonly List<(Creature Creature, int MaxHp, int HpStart)> _hpPlayers = new();

    /// <summary>本场真人玩家操控的角色 id（v0.2.267，与 <see cref="_hpPlayers"/> 同序）。</summary>
    private static readonly List<string> _pChars = new();

    /// <summary>对应角色**属于哪个模组**（v0.2.267：`vanilla` / 模组 id(+工坊 id)）。</summary>
    private static readonly List<string> _pCharMods = new();

    /// <summary>战斗中玩家掉的血（负变化的绝对值累计）。</summary>
    private static int _hpDmg;

    /// <summary>战斗中玩家回的血（正变化累计）。</summary>
    private static int _hpHeal;

    /// <summary>「玩家血量异常」日志的上限（避免刷屏）。v0.2.452</summary>
    private static int _hpAnomalyLogged;

    /// <summary>「单次血量变化超限被丢弃」日志的上限。v0.2.452</summary>
    private static int _hpHealAnomalyLogged;
    /// <summary>「快照缺失，用首次变化兜底」只记一次日志。</summary>
    private static int _hpFallbackLogged;

    /// <summary>
    /// 战斗开始（同步钩子 <c>Hook.BeforeCombatStart</c> 的 Prefix）：给真人玩家拍一张血量快照。
    /// 放在 Prefix 而不是 Postfix，是为了拿到**任何开局回血（缩放仪等）之前**的进场血量；
    /// 那些回血之后会通过 `AfterCurrentHpChanged` 记进 `pHeal`，两者不混。
    /// </summary>
    internal static void NoteCombatStarting(MegaCrit.Sts2.Core.Combat.ICombatState? combat)
    {
        if (combat == null) return;
        try
        {
            if (ReferenceEquals(_hpCombat, combat) && _hpSnapshot) return;
            _hpCombat = combat;
            _hpSnapshot = false;
            _hpPlayers.Clear();
            _hpDmg = 0;
            _hpHeal = 0;

            // 每场战斗开始把上一场没来得及写的 diag 丢掉（那意味着上一场没打完 / SL 回滚）
            _fightDiag.Clear();

            _pChars.Clear();
            _pCharMods.Clear();

            foreach (var player in BossLockOn.RealPlayers(combat))
            {
                var creature = player?.Creature;
                if (creature == null) continue;
                _hpPlayers.Add((creature, Math.Max(0, creature.MaxHp), Math.Max(0, creature.CurrentHp)));

                // ★ v0.2.267（用户 2026-10-31）：记下「玩家操控的角色」与「角色所属模组」——
                //   某些模组角色本身超模，会直接污染 Boss 的强度数据，得能按角色/模组分组看。
                string charId = "?";
                try { charId = player!.Character?.Id.Entry ?? "?"; } catch { /* 用问号 */ }
                _pChars.Add(charId);
                _pCharMods.Add(CharacterModOf(player!.Character));
            }
            _hpSnapshot = _hpPlayers.Count > 0;
        }
        catch { /* 统计失败绝不影响游戏 */ }
    }

    /// <summary>
    /// HP 变化（同步钩子 <c>Hook.AfterCurrentHpChanged</c> 的 Postfix）：累计真人玩家的掉血 / 回血。
    /// `delta` &lt; 0 = 掉血、&gt; 0 = 回血（用的是 Creature 的当前生命变化量）。
    /// </summary>
    internal static void NotePlayerHpChanged(MegaCrit.Sts2.Core.Combat.ICombatState? combat,
                                             Creature? creature, decimal delta)
    {
        if (!Enabled || combat == null || creature == null) return;
        try
        {
            if (!ReferenceEquals(_hpCombat, combat)) return;      // 不是这一场战斗的统计
            if (!creature.IsPlayer || creature.Monster != null) return;   // 只算真人玩家

            // 兜底：没抓到开局快照（极端情况下 BeforeCombatStart 没被广播到）
            if (!_hpSnapshot)
            {
                NoteCombatStarting(combat);
                if (_hpFallbackLogged++ == 0)
                {
                    Logger.Log("[CharBoss] 数据记录：玩家血量开局快照缺失，已在该次变化时补拍" +
                               "（这一场的「进入血量」可能略偏）");
                }
            }

            int amount = (int)Math.Round(Math.Abs(delta));
            if (amount <= 0) return;

            // ★ v0.2.452：血量被写成天文数字时，那一次「变化量」会被当成巨额回血（实测 `pHeal = 12957`）。
            //   单次变化不可能超过「最大生命 × 10」，超了就当作异常丢掉并记一行。
            int cap = 0;
            try { cap = Math.Max(0, creature.MaxHp) * 10; } catch { cap = 0; }
            if (cap > 0 && amount > cap)
            {
                if (_hpHealAnomalyLogged++ < 8)
                {
                    try
                    {
                        Logger.Log($"[CharBoss] 数据记录：单次血量变化 {amount} 超过上限 {cap}（最大生命×10）⇒ 当作异常丢弃，" +
                                   "不计入「掉血/回血」（十有八九是别的东西把血量写坏了）");
                    }
                    catch { /* 忽略 */ }
                }
                return;
            }

            if (delta < 0) _hpDmg += amount;
            else _hpHeal += amount;
        }
        catch { /* 忽略 */ }
    }

    /// <summary>
    /// 把玩家血量统计并进这一场的记录。拿不到快照 / 战斗对不上就什么都不加（表格里留空）。
    /// 字段：`pMaxHp` 最大生命合计、`pHpStart` 进场血量合计、`pHpEnd` 结束血量合计、
    /// `pDmg` 战斗中掉的血、`pHeal` 战斗中回的血、`pList` 每人「开局/上限-结束」。
    /// </summary>
    private static void AppendPlayerHpFields(Dictionary<string, object?> fields, object? combat)
    {
        try
        {
            if (!_hpSnapshot || _hpPlayers.Count == 0) return;
            if (combat != null && !ReferenceEquals(_hpCombat, combat)) return;   // 已经是另一场了

            int maxHpTotal = 0, hpStart = 0, hpEnd = 0;
            var perPlayer = new List<string>(_hpPlayers.Count);
            bool anomaly = false;
            foreach (var (creature, maxHp, startHp) in _hpPlayers)
            {
                int endHp = startHp;
                int liveMax = maxHp;
                try
                {
                    if (creature != null)
                    {
                        endHp = Math.Max(0, creature.CurrentHp);
                        liveMax = Math.Max(0, creature.MaxHp);
                    }
                }
                catch { /* 生物已经没了：用开局值兜底 */ }

                // ★ v0.2.452（2026-11-06 在真实数据里抓到）：**玩家血量会被写成天文数字**。
                //   实例（feishu 表里 3 行）：`pList = 42/70-99762/70`（当前血量 99762、上限 70）、
                //   `81/81-11354/16101`（上限 16101）、以及下一场开局仍带着 99762。
                //   这些**不是我们的算术错**：`endHp` 就是直接读 `creature.CurrentHp` 的结果，
                //   说明战斗里真的有东西把玩家血写成了荒谬值（同一个玩家的下一场「开局血量」也还是 99762）。
                //   危害：`残血 = pHpEnd / pMaxHp` 这一列会被单行拉到几万 %，把整张难度表带偏。
                //   做法：① 记录时**夹到 [0, liveMax]**，别再污染统计；
                //        ② 打一行 `数据记录：玩家血量异常`（带原始 per-player 串），下次玩家发日志就能顺着查。
                if (maxHp > 0 && (endHp > liveMax || liveMax > maxHp * 4))
                {
                    anomaly = true;
                    endHp = Math.Min(endHp, Math.Max(0, liveMax));
                }

                maxHpTotal += maxHp;
                hpStart += startHp;
                hpEnd += endHp;
                perPlayer.Add($"{startHp}/{maxHp}-{endHp}/{liveMax}");
            }

            if (anomaly && _hpAnomalyLogged++ < 8)
            {
                try
                {
                    Logger.Log("[CharBoss] 数据记录：玩家血量异常（已按上限夹住，不再计入「残血」统计）—— " +
                               $"pList=[{string.Join(";", perPlayer)}] 掉血 {_hpDmg} 回血 {_hpHeal} " +
                               "（如果这位玩家乐意，麻烦把他的 mod 列表发作者）");
                }
                catch { /* 忽略 */ }
            }

            fields["pMaxHp"] = maxHpTotal;
            fields["pHpStart"] = hpStart;
            fields["pHpEnd"] = hpEnd;
            fields["pDmg"] = _hpDmg;
            fields["pHeal"] = _hpHeal;
            fields["pList"] = string.Join(";", perPlayer);

            // ★ v0.2.267：谁在打这一场（角色 + 角色所属模组），和 pList 同序
            if (_pChars.Count > 0) fields["pChar"] = string.Join(",", _pChars);
            if (_pCharMods.Count > 0) fields["pCharMod"] = string.Join(",", _pCharMods);
        }
        catch { /* 忽略 */ }
    }

    /// <summary>
    /// 这个角色来自哪个模组：原版角色返回 <c>vanilla</c>；模组角色返回**模组清单里的 id**
    /// （有工坊 id 就拼成 `Downfall#3737335127` 这种），找不到归属时退回程序集名。
    ///
    /// 做法：角色模型的运行时类型所在程序集 —— 原版在 `sts2.dll` 里，模组的角色在模组自己的 DLL 里；
    /// 再用 <c>ModManager.GetLoadedMods()</c> 的 <c>Mod.assemblies</c> 反查是哪份模组（`Mod.manifest.id`）。
    /// 用户 2026-10-31：「因为有模组角色很超模会一定程度影响数据」—— 所以这列必须能和 `pChar` 对上。
    /// </summary>
    private static string CharacterModOf(MegaCrit.Sts2.Core.Models.CharacterModel? character)
    {
        try
        {
            if (character == null) return "?";
            var assembly = character.GetType().Assembly;
            if (assembly == typeof(MegaCrit.Sts2.Core.Models.CharacterModel).Assembly) return "vanilla";

            try
            {
                foreach (var mod in MegaCrit.Sts2.Core.Modding.ModManager.GetLoadedMods())
                {
#if STS2_107
                    if (mod?.assembly == null) continue;
                    bool hit = ReferenceEquals(mod.assembly, assembly);
#else
                    if (mod?.assemblies == null) continue;
                    bool hit = false;
                    foreach (var a in mod.assemblies)
                    {
                        if (ReferenceEquals(a, assembly)) { hit = true; break; }
                    }
#endif
                    if (!hit) continue;

                    string id = "?";
                    try { id = mod.manifest?.id ?? "?"; } catch { /* 用问号 */ }
                    if (id == "?") { try { id = assembly.GetName().Name ?? "?"; } catch { } }
#if !STS2_107
                    try { if (mod.workshopId.HasValue) id += "#" + mod.workshopId.Value; } catch { }
#endif
                    return id;
                }
            }
            catch { /* 查不到就退回程序集名 */ }

            try { return "mod:" + (assembly.GetName().Name ?? "?"); }
            catch { return "mod:?"; }
        }
        catch { return "?"; }
    }

    /// <summary>`lineup`（逗号分隔的 Key）里有没有哪一个对应这个遭遇（`SILENT_BREWER` ↔ `SILENT_BREWER_BOSS_ENCOUNTER`）。</summary>
    private static bool LineupCoversEncounter(string lineup, string encounter)
    {
        try
        {
            foreach (var raw in lineup.Split(','))
            {
                var key = raw.Trim();
                if (key.Length == 0) continue;
                if (encounter.IndexOf(key, StringComparison.Ordinal) >= 0) return true;
            }
        }
        catch { /* 忽略 */ }
        return false;
    }

    // ★ v0.2.463（2026-11-07 隐私自查）：把**本机用户目录**打码。
    //   Windows：`C:\Users\<名>\…` → `C:\Users\<user>\…`；Linux/macOS：`/home/<名>/…` → `/home/<user>/…`。
    //   为什么需要：日志行一旦含「失败/异常」就会被 `SafeLogPatch` 转成 diag 记录上传，
    //   而 .NET 的 IO/加密异常消息里**经常带完整路径**（＝玩家的 Windows 用户名）。
    private static readonly System.Text.RegularExpressions.Regex UserPathWin =
        new(@"(?i)([A-Z]:\\+Users\\+)[^\\/:*?""<>|\r\n]+", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static readonly System.Text.RegularExpressions.Regex UserPathUnix =
        new(@"(?i)(/home/)[^/\s""']+", System.Text.RegularExpressions.RegexOptions.Compiled);

    private static string MaskUserPaths(string s)
    {
        try
        {
            if (string.IsNullOrEmpty(s)) return s;
            if (s.IndexOf("\\Users\\", StringComparison.OrdinalIgnoreCase) >= 0)
                s = UserPathWin.Replace(s, "$1<user>");
            if (s.IndexOf("/home/", StringComparison.OrdinalIgnoreCase) >= 0)
                s = UserPathUnix.Replace(s, "$1<user>");
            return s;
        }
        catch { return s; }
    }

    /// <summary>记一条我们自己打出来的问题日志（异常 / 失败 / 不同步…）。</summary>
    internal static void Diagnostic(string message)
    {
        if (!Enabled || string.IsNullOrEmpty(message)) return;
        if (_diagCount >= MaxDiagPerSession) return;

        // ★ v0.2.463（2026-11-07 隐私自查）：日志里偶尔会带**本机路径**——
        //   `SafeLogPatch` 会把含「失败/异常」的行转成 diag，而 .NET 的 IO 异常消息里就带完整路径
        //   （`Could not find file 'C:\Users\<Windows 用户名>\AppData\...'`）。
        //   玩家从没同意过上传「他的电脑用户名」，所以**入队前统一打码**（本机文件里也一起打码）。
        message = MaskUserPaths(message);

        // 兜底为「不在我们的 Boss 战里」：后面用 try 包着，任何一步异常都不会误上传
        bool oursFight = false;

        // ★ v0.2.244（用户 2026-10-30：「我刚刚打了一下炼药猎，这数据有问题吗」——
        //   表格里 6 行里 4 行是 `diag`，真问题会被淹掉）：
        //   ① 性能统计、构建横幅这类「不是问题」的行不记；
        //   ② **同一条消息每个进程只记一次**（`LooksLikeProblem` 是按关键字过滤的，
        //      `性能(5s): … 异常=0` 因为含「异常」二字被放进来，每 5 秒刷一条）。
        try
        {
            if (message.Contains("性能(", StringComparison.Ordinal)) return;
            if (message.Contains("帧回调耗时", StringComparison.Ordinal)) return;
            if (message.Contains("CharacterBosses 构建", StringComparison.Ordinal)) return;

            // ★ v0.2.265（用户 2026-10-31：「你是不是把玩家 sl 的也上传了，这有脏数据」）：
            //   **数据收集自己的日志绝不能当成 diag 再收一遍** —— 以前这里是死循环：
            //     收 1 条 diag → 上传 → 上传成功后 `Logger.Log("诊断记录 1 条 → 写入主表（失败/异常 与 不同步摘要）")`
            //     这句话本身含「失败」「不同步」⇒ 又满足下面的 worthKeeping ⇒ 变成新 diag → 再上传…
            //   表格里那些每分钟一条、文本都是「写入主表」的行就是这么来的。
            if (message.Contains("诊断记录", StringComparison.Ordinal)) return;
            if (message.Contains("数据上传成功", StringComparison.Ordinal)) return;
            if (message.Contains("暂不上传", StringComparison.Ordinal)) return;

            // ★ v0.2.292（用户 2026-11-02：表里出现「⚠ 出牌队列滞留：SURVIVOR（拥有者 WBZ）」）：
            //   **滞留是线索，不是失败**，而且它经常只是玩家自己在选牌（生存者打出后要挑一张弃掉，
            //   选牌面板开着的时候那张牌面本来就挂在出牌队列上）⇒ 一律**只写本机日志，永不进表**。
            //   看门狗那边也同时收窄成「只看我们 Boss 的牌」（见 BossFrameTickPatch.WatchPlayQueue）。
            if (message.Contains("出牌队列滞留", StringComparison.Ordinal)) return;

            // ★ v0.2.270（用户 2026-10-31）：下面这些是**设计如此**，不再上传给作者 ——
            //   原版这几个遗物对影子玩家根本走不通，我们当初就是"拦了原版自己实现"另辟蹊径：
            //     · PAELS_LEGION（佩尔的士兵）：装饰性随从召不出来 ⇒ 每回合 NRE；
            //     · VEXING_PUZZLEBOX（烦人机关盒）：随机印牌对影子玩家抽不到候选 ⇒ 空序列；
            //     · PHYLACTERY_UNBOUND（无界命匣）：召唤时机不在战斗进行中 ⇒ AddPet 抛错，
            //       而她自己的奥斯提走 BossOsty；
            //   这些仍会写进本机 mod_log（便于我们排查），但不再污染玩家的表格。
            if (IsKnownDesignNoise(message)) return;

            // ★ v0.2.247（用户 2026-10-30：「只让玩家那上传 3 和 5 吧，表格里也只留这两个」）：
            //   只留两类真正有诊断价值的 ——
            //     ③ `失败 / 异常 / Exception`（别人机器上才出现的分支、我们复现不了的）
            //     ⑤ `不同步`（联机 divergence 摘要，之前那几个联机 bug 全靠它定位）
            //   其余（构建横幅、性能统计、普通提示语）一律不记。
            bool worthKeeping =
                message.Contains("失败", StringComparison.Ordinal)
                || message.Contains("异常", StringComparison.Ordinal)
                || message.Contains("Exception", StringComparison.Ordinal)
                || message.Contains("不同步", StringComparison.Ordinal)
                || message.Contains('⚠');
            if (!worthKeeping) return;

            // ★ v0.2.466（用户 2026-11-07：「目前上传的问题中还是有很多假问题，点开一看"卡住"的牌全是玩家出的」）：
            //   先查清事实：表里 283 条「出牌队列滞留」**全部**来自 v0.2.267/276/285/287（都早于加过滤的
            //   v0.2.292），当前版本一条都没有 ⇒ 那条修好了，噪音是**没更新的老客户端**发的。
            //   但顺着这次投诉又发现两类真正还会刷的「纯追踪行」，一并挡在表外：
            //     · `⚠ 佩尔的士兵 受到了伤害 X`（CombatSidePatches 的伤害记账追踪，数字一变就是一条新记录）
            //     · `⚠ BGM：… 在 FMOD 里找不到`（曲库缺歌时每场都刷，属于环境信息不是 bug）
            if (IsPureTrace(message)) return;

            // ★ v0.2.466：去重键把**数字**都归一成 `#` —— 否则
            //   `⚠ X 受到了伤害 2.25` / `… 3` / `… 19.50` 会被当成三条不同的记录全传上来。
            string key = message.Length > 120 ? message.Substring(0, 120) : message;
            key = NormalizeForDedupe(key);
            if (!_diagSeen.Add(key)) return;

            // ★ v0.2.265：**战斗进行中先攒着**，等这一场真正打完（有 win/lose）再随对局数据一起写。
            //   以前是边打边写边传 ⇒ 中途退出 / SL 回滚 / 打到一半报错都会在表里留下**没有结果**的行。
            if (InBossFight())
            {
                if (_fightDiag.Count < MaxDiagPerFight) _fightDiag.Add(message);
                return;
            }

            // ★ v0.2.271（用户 2026-10-31：「怎么别的模组的第四幕也上传过来了」）：
            //   **不在我们的 Boss 战里** ⇒ 这些日志跟本模组无关（例如别的模组第四幕的卡/角色：
            //   `TOJI-QUICKSTEP（拥有者 伏黑甚尔）`、`SUKUNA-SUKUNA_GAU…`），
            //   一律**只写本机、绝不上传**，免得把别人的幕/遭遇混进我们的表格。
            oursFight = false;
        }
        catch { /* 过滤失败就照常记 */ }

        try
        {
            _diagCount++;
            Write(new Dictionary<string, object?>
            {
                ["kind"] = "diag",
                ["line"] = ClipDiagLine(message),
                ["act"] = SafeAct(null) + 1,
                ["encounter"] = SafeEncounter(),
                ["role"] = RoleName(),
                ["fight"] = oursFight ? "boss" : "none",
            }, queueForUpload: oursFight);
        }
        catch { /* 忽略 */ }
    }

    /// <summary>已经记过的 diag 消息（前 120 字）—— 同一条只记一次，避免循环刷屏。</summary>
    private static readonly HashSet<string> _diagSeen = new(StringComparer.Ordinal);

    /// <summary>
    /// 「设计如此」的已知噪音（v0.2.270，用户 2026-10-31 亲自定的）：
    /// 这几处原版逻辑对影子玩家走不通，我们当年就是**拦掉原版、自己实现**，报错是预期的，
    /// 不该再把玩家的表格刷满。判定按遗物 id / 引擎固定文案，避免误伤别的日志。
    /// </summary>
    private static bool IsKnownDesignNoise(string message)
    {
        try
        {
            if (message.Contains("遗物 PAELS_LEGION", StringComparison.Ordinal)) return true;
            if (message.Contains("遗物 VEXING_PUZZLEBOX", StringComparison.Ordinal)) return true;
            if (message.Contains("遗物 PHYLACTERY_UNBOUND", StringComparison.Ordinal)) return true;
            // 这三个遗物最终都撞到同一条引擎兜底文案（召唤时机不在战斗进行中）
            if (message.Contains("Attempted to add a creature outside of combat", StringComparison.Ordinal)) return true;
        }
        catch { /* 判定失败就当不是噪音 */ }
        return false;
    }

    /// <summary>
    /// **纯追踪/信息行**（v0.2.466，用户 2026-11-07：「上传的问题中还是有很多假问题」）：
    /// 这些东西写进本机 `mod_log.txt` 便于排查，但**不该出现在玩家的表格里** ——
    /// 它们不是 bug，数字每变一次就是一条新记录，很容易把真问题淹掉。
    /// </summary>
    private static bool IsPureTrace(string message)
    {
        try
        {
            // 随从/生物的伤害记账追踪（`⚠ 佩尔的士兵 受到了伤害 2.25（未格挡=2,props=Move）`）
            if (message.Contains("受到了伤害", StringComparison.Ordinal)) return true;
            // 曲目映射说明：曲库里缺这首歌时每场都刷一条（环境差异，不是我们的 bug）
            if (message.Contains("BGM：", StringComparison.Ordinal)) return true;
            // 伤害预测/耗时这类「性能与预测」追踪
            if (message.Contains("伤害预测", StringComparison.Ordinal)) return true;
            if (message.Contains("耗时:", StringComparison.Ordinal)) return true;
            if (message.Contains("耗时：", StringComparison.Ordinal)) return true;
        }
        catch { /* 判定失败就当不是追踪行 */ }
        return false;
    }

    /// <summary>去重用的归一化：把连续数字换成 `#`，让「同一句话、数字不同」的追踪行只留一条。</summary>
    private static string NormalizeForDedupe(string s)
    {
        try
        {
            var sb = new System.Text.StringBuilder(s.Length);
            bool inNum = false;
            foreach (char ch in s)
            {
                bool digit = ch >= '0' && ch <= '9';
                if (digit)
                {
                    if (!inNum) { sb.Append('#'); inNum = true; }
                }
                else
                {
                    inNum = false;
                    sb.Append(ch);
                }
            }
            return sb.ToString();
        }
        catch { return s; }
    }


    // ── 战斗中的 diag 缓冲（v0.2.265）─────────────────────────────────────

    /// <summary>这一场战斗里攒下的 diag 原文（等战斗打完再写/上传）。</summary>
    private static readonly List<string> _fightDiag = new();

    /// <summary>单场战斗最多攒这么多条 diag（防止某个循环把表刷爆）。</summary>
    private const int MaxDiagPerFight = 6;

    /// <summary>现在是不是正在打**我们的** Boss 战（决定 diag 是「立即写」还是「攒到打完」）。</summary>
    private static bool InBossFight()
    {
        try
        {
            var manager = MegaCrit.Sts2.Core.Combat.CombatManager.Instance;
            if (manager == null || !manager.IsInProgress) return false;
            var combat = manager.DebugOnlyGetState();
            return combat != null && BossShadowPets.BossFightActive(combat);
        }
        catch { return false; }
    }

    /// <summary>
    /// 把这一场攒下的 diag 一起写出去。
    /// `queueForUpload` 与对局数据一致（aborted 那一场**只留本机**，不上传）；
    /// 每条额外带上 `result` / `boss`，一眼看出它属于哪一场、什么结果。
    /// </summary>
    private static void FlushFightDiagnostics(bool queueForUpload, string? result, string bossKey)
    {
        try
        {
            if (_fightDiag.Count == 0) return;
            var pending = new List<string>(_fightDiag);
            _fightDiag.Clear();

            foreach (var message in pending)
            {
                _diagCount++;
                Write(new Dictionary<string, object?>
                {
                    ["kind"] = "diag",
                    ["line"] = ClipDiagLine(message),
                    ["act"] = SafeAct(null) + 1,
                    ["encounter"] = SafeEncounter(),
                    ["role"] = RoleName(),
                    ["result"] = result ?? "",
                    ["boss"] = bossKey,
                }, queueForUpload: queueForUpload);
            }
        }
        catch { /* 忽略 */ }
    }


    /// <summary>配置界面显示用：一句话说明文件在哪。</summary>
    internal static string DescribePath()
    {
        try
        {
            string path = FilePath;
            return string.IsNullOrEmpty(path) ? "（拿不到配置目录）" : path;
        }
        catch { return "（拿不到配置目录）"; }
    }

    // ── 内部 ─────────────────────────────────────────────

    private static void Write(Dictionary<string, object?> fields, bool queueForUpload = true)
    {
        string path = FilePath;
        if (string.IsNullOrEmpty(path)) return;

        lock (Gate)
        {
            try
            {
                string? dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                // 轮转：太大就留一份旧的
                try
                {
                    var info = new FileInfo(path);
                    if (info.Exists && info.Length > MaxBytes)
                    {
                        string backup = path + ".1";
                        if (File.Exists(backup)) File.Delete(backup);
                        File.Move(path, backup);
                    }
                }
                catch { /* 轮转失败就继续写 */ }

                var payload = new Dictionary<string, object?>(fields)
                {
                    ["at"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                    ["mod"] = BuildInfo.Tag,
                };

                string line = JsonSerializer.Serialize(payload) + Environment.NewLine;
                using var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                using var writer = new StreamWriter(stream);
                writer.Write(line);

                // A6：玩家同意上传 + 作者配置了端点 ⇒ 顺手塞进「待上传队列」
                // （`queueForUpload: false` 的（如 consent）只留本机，不进表）
                if (queueForUpload && BossConfig.TelemetryUpload) QueueForUpload(line);
            }
            catch { /* 写不进去就丢：绝不影响游戏 */ }
        }

        if (BossConfig.TelemetryUpload) FlushUploads();
    }

    // ── A6：上传（可选，默认关闭；作者在配置里填端点才会走这条） ─────────

    private static string OutboxPath
    {
        get
        {
            string dir = Dir;
            return string.IsNullOrEmpty(dir) ? string.Empty : Path.Combine(dir, "upload-queue.jsonl");
        }
    }

    /// <summary>待上传队列上限（条）。超了就丢掉最旧的，避免磁盘无限涨。</summary>
    private const int MaxOutboxLines = 400;

    private static bool _uploading;

    private static void QueueForUpload(string line)
    {
        try
        {
            string path = OutboxPath;
            if (string.IsNullOrEmpty(path)) return;
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.AppendAllText(path, line);
        }
        catch { /* 忽略 */ }
    }

    /// <summary>
    /// 把待上传队列发出去。三种去处（用户 2026-10-30：国内打不开 Google 表格 ⇒ 支持飞书）：
    ///   ① 通用 `telemetryUrl`：POST `{"mod":…,"records":[…]}`（自建服务 / Worker / Apps Script）；
    ///   ② 飞书**群机器人 webhook**（零鉴权）：每条记录当一条文本消息发进群；
    ///   ③ 飞书**多维表格**：`tenant_access_token` → `bitable/v1/apps/{app}/tables/{table}/records/batch_create`。
    /// 失败**不清队列**，下次再发；成功清空。全程 try/catch + 后台线程，绝不阻塞/影响游戏。
    /// </summary>
    private static void FlushUploads()
    {
        if (_uploading) return;
        string outbox = OutboxPath;
        if (string.IsNullOrEmpty(outbox) || !File.Exists(outbox)) return;
        if (!BossConfig.AnyUploadEndpoint) return;

        _uploading = true;
        _ = Task.Run(async () =>
        {
            try
            {
                string[] lines;
                try { lines = File.ReadAllLines(outbox); } catch { return; }
                if (lines.Length == 0) return;
                if (lines.Length > MaxOutboxLines) lines = lines[^MaxOutboxLines..];

                // ★ v0.2.246（用户 2026-10-30：「能不能把那四项杂项隐藏或者放在别的地方？
                //   它们可能会影响我看数据」）：**按 kind 分流**。
                //   · `boss_fight`（对局数据）→ 主表 `feishuTableId`（群机器人 / 通用端点也走这条）；
                //   · `diag` / `consent`（杂项）→ ① 配了 `feishuDiagTableId` 就写那张表；
                //                              ② 没配 ⇒ **只留在本机、不上传**（主表永远干净）。
                // ★ v0.2.247（用户 2026-10-30：「只让玩家那上传 3 和 5 吧，表格里也只留这两个」）：
                //   能上传的**只有两种**：
                //     · `boss_fight`（对局数据）
                //     · `diag`（已经被 `Diagnostic` 收窄到「失败/异常」与「不同步摘要」两类）
                //   其余（`consent` 之类）**只留本机**，不进表。
                var fightLines = new List<string>();
                var diagLines = new List<string>();
                var localOnly = 0;
                foreach (var line in lines)
                {
                    string kind = KindOf(line);
                    if (kind == "boss_fight") fightLines.Add(line);
                    else if (kind == "diag") diagLines.Add(line);
                    else localOnly++;
                }

                bool ok = true;

                if (fightLines.Count > 0)
                {
                    bool fightOk = false;
                    // ③ 飞书多维表格优先（结构化，最好用）
                    if (BossConfig.FeishuBitableReady)
                    {
                        fightOk = await UploadToFeishuBitable(fightLines.ToArray(),
                            BossConfig.FeishuBitableAppToken, BossConfig.FeishuTableId).ConfigureAwait(false);
                    }
                    // ② 飞书群机器人
                    if (!fightOk && !string.IsNullOrWhiteSpace(BossConfig.FeishuBotUrl))
                    {
                        fightOk = await UploadToFeishuBot(fightLines.ToArray()).ConfigureAwait(false);
                    }
                    // ① 通用 URL
                    if (!fightOk && !string.IsNullOrWhiteSpace(BossConfig.TelemetryUrl))
                    {
                        fightOk = await UploadToGenericUrl(fightLines.ToArray()).ConfigureAwait(false);
                    }
                    ok = fightOk;
                }

                if (diagLines.Count > 0)
                {
                    string diagTable = BossConfig.FeishuDiagTableId;
                    if (!string.IsNullOrWhiteSpace(diagTable) && BossConfig.FeishuBitableReady)
                    {
                        bool diagOk = await UploadToFeishuBitable(diagLines.ToArray(),
                            BossConfig.FeishuBitableAppToken, diagTable).ConfigureAwait(false);
                        ok = ok && diagOk;
                        if (diagOk) Logger.Log($"[CharBoss] 诊断记录 {diagLines.Count} 条 → 已写入单独的 diag 表 {diagTable}");
                        else Logger.Log($"[CharBoss] 诊断记录写入单独的表失败（{diagTable}），数据仍在本机");
                    }
                    else if (BossConfig.FeishuBitableReady)
                    {
                        // ★ 默认：诊断记录（只有「失败/异常」和「不同步摘要」两类）**和対局数据进同一张表**，
                        //   用户在 2026-10-30 选的就是这个：「只让玩家那上传 3 和 5，表格里也只留这两个」。
                        bool diagOk = await UploadToFeishuBitable(diagLines.ToArray(),
                            BossConfig.FeishuBitableAppToken, BossConfig.FeishuTableId).ConfigureAwait(false);
                        ok = ok && diagOk;
                        if (diagOk)
                        {
                            Logger.Log($"[CharBoss] 诊断记录 {diagLines.Count} 条 → 写入主表（失败/异常 与 不同步摘要）");
                        }
                    }
                    else
                    {
                        _diagDropped += diagLines.Count;
                        if (_diagDropped <= 40 || _diagDropped % 100 == 0)
                        {
                            Logger.Log($"[CharBoss] 诊断记录 {diagLines.Count} 条暂不上传（没有可写的数据表）");
                        }
                    }
                }

                if (ok)
                {
                    try { File.Delete(outbox); } catch { /* 删不掉下次重复发一次，可接受 */ }
                    Logger.Log($"[CharBoss] 数据上传成功：对局 {fightLines.Count} 条" +
                               (diagLines.Count > 0 ? $"、诊断 {diagLines.Count} 条" : "") +
                               (localOnly > 0 ? $"（另有 {localOnly} 条只留本机）" : ""));
                }
            }
            catch (Exception e)
            {
                Logger.Log("[CharBoss] 数据上传出错（数据仍在本机）: " + e.Message);
            }
            finally { _uploading = false; }
        });
    }

    /// <summary>本机这一局的角色：`solo` / `host` / `guest`（联机时表里一眼看出数据是谁传的）。</summary>
    private static string RoleName()
    {
        try
        {
            var type = Core.NetRole.Type;
            if (type == MegaCrit.Sts2.Core.Multiplayer.Game.NetGameType.Host) return "host";
            if (type == MegaCrit.Sts2.Core.Multiplayer.Game.NetGameType.Client) return "guest";
            if (type == MegaCrit.Sts2.Core.Multiplayer.Game.NetGameType.Singleplayer) return "solo";
            return type.ToString().ToLowerInvariant();
        }
        catch { return "?"; }
    }
    /// <summary>不上传的杂项记录累计条数（只用于日志节流）。</summary>
    private static int _diagDropped;

    /// <summary>读一条记录的 `kind`（读不到返回空串）。</summary>
    private static string KindOf(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            if (doc.RootElement.TryGetProperty("kind", out var k) && k.ValueKind == JsonValueKind.String)
                return k.GetString() ?? "";
        }
        catch { /* 坏行当杂项 */ }
        return "";
    }
    private static readonly System.Net.Http.HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    // ── ① 通用 URL ──
    private static async Task<bool> UploadToGenericUrl(string[] lines)
    {
        try
        {
            string body = "{\"mod\":" + JsonSerializer.Serialize(BuildInfo.Tag) + ",\"records\":[" +
                          string.Join(",", lines) + "]}";
            using var content = new System.Net.Http.StringContent(body, System.Text.Encoding.UTF8, "application/json");
            var resp = await Http.PostAsync(BossConfig.TelemetryUrl, content).ConfigureAwait(false);
            if (resp.IsSuccessStatusCode) return true;
            Logger.Log($"[CharBoss] 数据上传失败（HTTP {(int)resp.StatusCode}），数据留在本机待下次再发");
        }
        catch (Exception e) { Logger.Log("[CharBoss] 通用端点上传出错: " + e.Message); }
        return false;
    }

    // ── ② 飞书群机器人（零鉴权） ──
    // ★ v0.2.462（用户 2026-11-07：Cloudflare Worker 在国内 `ERR_CONNECTION_TIMED_OUT`，
    //   改用它做中转）：玩家端只持有一个**群机器人 webhook**（拿不到任何飞书凭据，
    //   最坏结果只是别人能往那个群发消息），作者本机脚本
    //   `tools/feishu-group-pull.ps1` 读群消息 → 解析 → 照旧写进多维表格。
    //   所以消息体里**每条记录都带原始 JSON 一行**（以前只有人读摘要，脚本没法还原成表）。
    // 同时修掉旧版一个会**丢数据**的坑：以前超过 20 条就只发前 20 条却照样返回成功、
    //   outbox 被删 ⇒ 第 21 条起永远丢了。现在按 <see cref="BotBatch"/> 条一批**全部发出**，
    //   任一批失败就整体算失败（outbox 保留，下次重发；重复的由拉取脚本按内容去重）。
    private const int BotBatch = 8;          // 一条飞书文本消息里**最多**几条记录
    private const int BotMsgChars = 6000;    // 一条飞书文本消息里**最多**几个字符（长行按字数提前切批）
    private const int BotLineCap = 4500;     // 单行 JSON 硬上限，防意外超长把整条消息顶掉

    private static async Task<bool> UploadToFeishuBot(string[] lines)
    {
        int sent = 0;
        int i = 0;
        while (i < lines.Length)
        {
            int count = 0;
            int chars = 0;
            var body = new System.Text.StringBuilder();
            while (i < lines.Length && count < BotBatch)
            {
                string line = Truncate(lines[i], BotLineCap);
                if (count > 0 && chars + line.Length + 1 > BotMsgChars) break;   // 这一条留给下个消息
                body.Append('\n').Append(line);
                chars += line.Length + 1;
                i++;
                count++;
            }

            var sb = new System.Text.StringBuilder();
            // 「CharacterBosses」是群机器人「自定义关键词」安全设置要匹配的词，别删。
            sb.Append("CharacterBosses 数据 ").Append(count).Append(" 条 ");
            sb.Append(BuildInfo.Tag.IndexOf(' ') > 0 ? BuildInfo.Tag[..BuildInfo.Tag.IndexOf(' ')] : BuildInfo.Tag);
            sb.Append(body);

            if (!await PostFeishuBotText(sb.ToString()).ConfigureAwait(false)) return false;
            sent += count;
        }
        return sent > 0;
    }

    /// <summary>
    /// 发一条文本消息给飞书群机器人。⚠ 飞书**出错也返回 HTTP 200**（错在 body 的 `code`/`StatusCode` 里），
    /// 所以必须解析消息体，不能只看状态码。
    /// </summary>
    private static async Task<bool> PostFeishuBotText(string text)
    {
        try
        {
            string body = JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["msg_type"] = "text",
                ["content"] = new Dictionary<string, object?> { ["text"] = text },
            });
            using var content = new System.Net.Http.StringContent(body, System.Text.Encoding.UTF8, "application/json");
            var resp = await Http.PostAsync(BossConfig.FeishuBotUrl, content).ConfigureAwait(false);
            string reply = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode)
            {
                Logger.Log($"[CharBoss] 飞书群机器人上传失败（HTTP {(int)resp.StatusCode}）: {Truncate(reply, 200)}");
                return false;
            }

            try
            {
                using var doc = JsonDocument.Parse(reply);
                var r = doc.RootElement;
                int code = 0;
                if (r.TryGetProperty("code", out var c) && c.ValueKind == JsonValueKind.Number) code = c.GetInt32();
                else if (r.TryGetProperty("StatusCode", out var sc) && sc.ValueKind == JsonValueKind.Number) code = sc.GetInt32();
                if (code == 0) return true;
                Logger.Log("[CharBoss] 飞书群机器人拒收（code=" + code + "）：" + Truncate(reply, 200) +
                           "（常见原因：群里机器人被删、关键词安全设置不匹配、或每分钟条数超限）");
                return false;
            }
            catch { return true; }      // 解析不了但 HTTP 200 ⇒ 当成功，别把队列堵死
        }
        catch (Exception e) { Logger.Log("[CharBoss] 飞书群机器人上传出错: " + e.Message); }
        return false;
    }

    /// <summary>把一条 JSON 记录压成一行短文本（群消息用）。</summary>
    private static string Summarize(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            string G(string k) => r.TryGetProperty(k, out var v) ? (v.ToString() ?? "") : "";
            if (G("kind") == "diag") return $"[问题] {Truncate(G("line"), 90)}";
            if (G("kind") == "consent") return "[同意] 玩家开启了本地数据记录";
            return $"[战斗] {G("result")} | 第{G("act")}幕 {G("encounter")} | {G("boss")} | " +
                   $"进阶{G("ascension")} {G("players")}人 | 回合{G("turns")} | HP {G("bossHp")}/{G("bossMaxHp")} | 种子{G("seed")}";
        }
        catch { return Truncate(json, 120); }
    }

    private static string Truncate(string s, int n)
        => string.IsNullOrEmpty(s) ? "" : (s.Length <= n ? s : s.Substring(0, n) + "…");

    // ── ③ 飞书多维表格 ──
    private static string? _feishuToken;
    private static DateTime _feishuTokenExpiry = DateTime.MinValue;

    /// <summary>丢掉缓存的 access token（失效/换 secret 之后必须丢，否则会一直拿坏 token 重试）。</summary>
    private static void InvalidateFeishuToken()
    {
        _feishuToken = null;
        _feishuTokenExpiry = DateTime.MinValue;
    }

    private static async Task<string?> FeishuToken()
    {
        if (_feishuToken != null && DateTime.UtcNow < _feishuTokenExpiry) return _feishuToken;
        try
        {
            string body = JsonSerializer.Serialize(new Dictionary<string, object?>
            {
                ["app_id"] = BossConfig.FeishuAppId,
                ["app_secret"] = BossConfig.FeishuAppSecret,
            });
            using var content = new System.Net.Http.StringContent(body, System.Text.Encoding.UTF8, "application/json");
            var resp = await Http.PostAsync("https://open.feishu.cn/open-apis/auth/v3/tenant_access_token/internal",
                content).ConfigureAwait(false);
            string text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            using var doc = JsonDocument.Parse(text);
            if (!doc.RootElement.TryGetProperty("tenant_access_token", out var tok))
            {
                // ★ v0.2.275：**取不到就清缓存**，别把 null 也"缓存"住；
                //   走到这里多半是 app_secret 已被重置（作者换 secret 之后，旧版客户端会一直 401）。
                string msg = text.Length > 240 ? text.Substring(0, 240) : text;
                Logger.Log("[CharBoss] 飞书 token 获取失败：" + msg +
                           "（若是刚重置过 app_secret，说明这个客户端还是旧 secret ⇒ 更新模组）");
                InvalidateFeishuToken();
                return null;
            }
            _feishuToken = tok.GetString();
            _feishuTokenExpiry = DateTime.UtcNow.AddMinutes(100);      // 官方 2 小时，提前一点过期
            return _feishuToken;
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 飞书 token 获取失败: " + e.Message);
            InvalidateFeishuToken();
            return null;
        }
    }

    /// <summary>
    /// 数字列按 number 发（其余按文本），这样多维表格里能排序/求和。
    /// ⚠ `seed` **不在这里**：本局种子是 19 位整数（如 8461494873891562058），
    /// 超过 double 的精确范围 ⇒ 必须当**文本**发，否则飞书那边会丢精度/直接拒收。
    /// </summary>
    private static readonly HashSet<string> NumericKeys = new(StringComparer.Ordinal)
    {
        "act", "ascension", "players", "bossMaxHp", "bossHp", "turns",
        "bossTotal", "bossesKilled", "maxHpTotal", "hpTotal",
        // v0.2.264：玩家侧血量（数字列，可在飞书里排序 / 求平均）
        "pMaxHp", "pHpStart", "pHpEnd", "pDmg", "pHeal",
    };

    /// <summary>
    /// 写进飞书多维表格。★ v0.2.275（用户 2026-10-31 的 `code=99991663 Invalid access token`）：
    /// **令牌失效要能自愈** —— 以前 token 缓存 100 分钟，只要飞书那边提前作废（作者重置 secret、
    /// 后台刷新、或多端抢用），客户端就会一直拿着坏 token 重试到进程结束。
    /// 现在整段包一层：命中「token 失效」码 ⇒ 清缓存、重取一次、把同一批再发一遍。
    /// </summary>
    private static async Task<bool> UploadToFeishuBitable(string[] lines, string appToken, string tableId)
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                var (ok, tokenInvalid) = await TryUploadToFeishuBitable(lines, appToken, tableId).ConfigureAwait(false);
                if (ok) return true;
                if (!tokenInvalid || attempt > 0) return false;

                Logger.Log("[CharBoss] 飞书多维表格：access token 失效（99991663/99991668）⇒ " +
                           "丢弃缓存、重新取一次令牌，再把这一批重发");
                InvalidateFeishuToken();
            }
            catch (Exception e)
            {
                Logger.Log("[CharBoss] 飞书多维表格上传出错: " + e.Message);
                return false;
            }
        }
        return false;
    }

    private static async Task<(bool Ok, bool TokenInvalid)> TryUploadToFeishuBitable(string[] lines,
                                                                                     string appToken, string tableId)
    {
        try
        {
            string? token = await FeishuToken().ConfigureAwait(false);
            if (string.IsNullOrEmpty(token)) return (false, true);      // token 都拿不到：下一轮重试一次

            // ★ 先问一下这张表里**实际有哪些字段**（用户 2026-10-30：飞书 AI 建表时会自己编字段名，
            //   我们发过去不存在的字段会被整批拒收）。这里按「表里真有的字段」过滤后再发：
            //   多出来的跳过并记一行日志（作者以后补列就能自动生效）。
            var existing = await FeishuFields(token, tableId).ConfigureAwait(false);
            if (existing == null || existing.Count == 0)
            {
                // 读字段都可能是因为 token 失效（见 FeishuFields 的日志）⇒ 报 tokenInvalid，
                // 让外层清缓存重取一次；真没权限的话第二次还是会失败，只多一行日志。
                Logger.Log("[CharBoss] 飞书多维表格：读不到字段列表（应用没加为该表协作者，或权限不足）⇒ 本次跳过");
                return (false, true);
            }

            var skipped = new SortedSet<string>(StringComparer.Ordinal);
            var mapped = new List<(string Key, string Field)>();
            foreach (var key in new[] { "at", "kind", "mod", "role", "result", "act", "encounter", "lineup", "boss",
                                        "ascension", "players", "bossMaxHp", "bossHp", "turns", "seed", "cfg", "line",
                                        "bossTotal", "bossesKilled", "maxHpTotal", "hpTotal", "bossList", "hpList",
                                        // v0.2.264：玩家侧血量证据
                                        "pMaxHp", "pHpStart", "pHpEnd", "pDmg", "pHeal", "pList",
                                        // v0.2.267：玩家操控的角色 + 角色所属模组
                                        "pChar", "pCharMod" })
            {
                if (existing.TryGetValue(key, out var actual)) mapped.Add((key, actual));
                else skipped.Add(key);
            }
            if (skipped.Count > 0)
            {
                Logger.Log($"[CharBoss] 飞书多维表格：表里没有这些列 ⇒ 本次跳过 [{string.Join(",", skipped)}]" +
                           "（想让它们也进表，就按这些名字加列，代码会自动带上）");
            }
            if (mapped.Count == 0)
            {
                Logger.Log("[CharBoss] 飞书多维表格：一个能对上的列都没有 ⇒ 不写（请按文档里的字段名建列）");
                return (false, false);
            }

            string url = $"https://open.feishu.cn/open-apis/bitable/v1/apps/{appToken}" +
                         $"/tables/{tableId}/records/batch_create";

            for (int start = 0; start < lines.Length; start += 100)
            {
                var records = new List<Dictionary<string, object?>>();
                for (int i = start; i < Math.Min(start + 100, lines.Length); i++)
                {
                    var fields = new Dictionary<string, object?>();
                    try
                    {
                        using var doc = JsonDocument.Parse(lines[i]);
                        foreach (var (key, field) in mapped)
                        {
                            if (!doc.RootElement.TryGetProperty(key, out var v)) continue;
                            if (v.ValueKind == JsonValueKind.Object || v.ValueKind == JsonValueKind.Array)
                                fields[field] = v.GetRawText();
                            else if (NumericKeys.Contains(key) && v.ValueKind == JsonValueKind.Number)
                                fields[field] = v.GetDouble();
                            else
                                fields[field] = v.ValueKind == JsonValueKind.Null ? "" : v.ToString();
                        }
                    }
                    catch { continue; }
                    records.Add(new Dictionary<string, object?> { ["fields"] = fields });
                }
                if (records.Count == 0) continue;

                string body = JsonSerializer.Serialize(new Dictionary<string, object?> { ["records"] = records });
                using var req = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, url);
                req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
                req.Content = new System.Net.Http.StringContent(body, System.Text.Encoding.UTF8, "application/json");
                var resp = await Http.SendAsync(req).ConfigureAwait(false);
                string text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode || !text.Contains("\"code\":0"))
                {
                    string detail = text.Length > 300 ? text.Substring(0, 300) : text;
                    // ★ v0.2.275：认出「令牌失效」这一类，让外层清缓存重试一次
                    bool badToken = text.Contains("99991663") || text.Contains("99991668")
                                    || text.Contains("Invalid access token", StringComparison.OrdinalIgnoreCase);
                    Logger.Log($"[CharBoss] 飞书多维表格写入失败：{detail}" +
                               (badToken
                                 ? "（access token 失效 ⇒ 会清缓存重取一次）"
                                 : "（数据留在本机；多半是某列类型不匹配，例如 seed 必须是文本）"));
                    return (false, badToken);
                }
            }
            return (true, false);
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 飞书多维表格上传出错: " + e.Message);
            return (false, false);
        }
    }

    /// <summary>这张表实际有哪些字段（小写名 → 真实字段名）；拿不到返回 null。按表缓存 10 分钟。</summary>
    private static Dictionary<string, string>? _fieldCache;
    private static string _fieldCacheTable = "";
    private static DateTime _fieldCacheAt = DateTime.MinValue;

    private static async Task<Dictionary<string, string>?> FeishuFields(string token, string tableId)
    {
        // 缓存按**表**区分（主表 / 杂项表字段可能完全不同）
        if (_fieldCache != null && _fieldCacheTable == tableId &&
            DateTime.UtcNow - _fieldCacheAt < TimeSpan.FromMinutes(10)) return _fieldCache;
        try
        {
            string url = $"https://open.feishu.cn/open-apis/bitable/v1/apps/{BossConfig.FeishuBitableAppToken}" +
                         $"/tables/{tableId}/fields?page_size=100";
            using var req = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, url);
            req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
            var resp = await Http.SendAsync(req).ConfigureAwait(false);
            string text = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            using var doc = JsonDocument.Parse(text);
            if (!doc.RootElement.TryGetProperty("data", out var data) ||
                !data.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in items.EnumerateArray())
            {
                if (!item.TryGetProperty("field_name", out var name)) continue;
                string? n = name.GetString();
                if (!string.IsNullOrEmpty(n)) map[n!] = n!;
            }
            _fieldCache = map;
            _fieldCacheTable = tableId;
            _fieldCacheAt = DateTime.UtcNow;
            Logger.Log($"[CharBoss] 飞书多维表格：表 {tableId} 内字段 [{string.Join(",", map.Values)}]");
            return map;
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 飞书多维表格：读字段失败: " + e.Message);
            return null;
        }
    }

    /// <summary>把本机数据文件复制到桌面（玩家不想自动上传时，可以手动发给作者）。</summary>
    internal static string? ExportToDesktop()
    {
        try
        {
            string path = FilePath;
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (string.IsNullOrEmpty(desktop)) return null;
            string target = Path.Combine(desktop,
                $"CharacterBosses-telemetry-{DateTime.Now:yyyyMMdd-HHmmss}.jsonl");
            File.Copy(path, target, overwrite: true);
            Logger.Log("[CharBoss] 数据已导出到桌面：" + target);
            return target;
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 导出数据失败: " + e.Message);
            return null;
        }
    }

    /// <summary>同意记录：打开开关后的第一条永远是这个（含时间与版本），说明「这是我自愿开的」。</summary>
    internal static void WriteConsentOnce()
    {
        if (!Enabled || _consentWritten) return;
        _consentWritten = true;
        try
        {
            // ★ v0.2.247（用户 2026-10-30：「只让玩家那上传 3 和 5 吧，表格里也只留这两个」）：
            //   `consent` 是**隐私凭据**，只写本机文件、**不进待上传队列**（表格里不需要它）。
            Write(new Dictionary<string, object?>
            {
                ["kind"] = "consent",
                ["note"] = "玩家在模组配置里开启了本地数据记录；数据只写入本机文件，不会上传。",
                ["platform"] = Environment.OSVersion.Platform.ToString(),
            }, queueForUpload: false);
        }
        catch { /* 忽略 */ }
    }

    private static Dictionary<string, object?> SettingsSnapshot()
    {
        var cfg = new Dictionary<string, object?>();
        try { cfg["mixedMatching"] = BossConfig.MixedMatching; } catch { }
        try { cfg["sharePool"] = BossConfig.SharePool; } catch { }
        try { cfg["asc10Act3OneEach"] = BossConfig.Asc10Act3OneEachRaw; } catch { }
        try { cfg["bgmSource"] = BossConfig.BgmSource; } catch { }
        return cfg;
    }

    private static int SafeAct(Monsters.CharBossMonster? boss)
    {
        try
        {
            var state = MultiBossPlan.RunStateOrNull();
            if (state != null) return state.CurrentActIndex;
        }
        catch { /* 忽略 */ }
        return 0;
    }

    private static string SafeEncounter(Monsters.CharBossMonster? boss = null)
    {
        // ★ v0.2.333：先看这只 Boss 出生时记下的变体身份（`SpawnVariantKey`）——
        //   战斗结束那一刻 `CurrentEncounterEntry()` 常常已经读不到了（CombatState 换了），
        //   只按房间遭遇记会把「炼药猎」错记成「刀猎」。两条都拿不到才写 "?"。
        try
        {
            var bySpawn = BossKeys.EncounterOf(boss?.SpawnVariantKey);
            if (bySpawn != null) return bySpawn.Id.Entry;
        }
        catch { /* 继续 */ }
        try { return BossLineups.CurrentEncounterEntry() ?? "?"; } catch { return "?"; }
    }

    private static string SafeLineup(Monsters.CharBossMonster? boss)
    {
        try
        {
            int act = SafeAct(boss);
            for (int fight = 0; fight < 2; fight++)
            {
                var lineup = BossLineups.For(act, fight);
                if (lineup.Length > 0 && lineup[0] == SafeKey(boss)) return string.Join(",", lineup);
            }
            var first = BossLineups.For(act, 0);
            return string.Join(",", first);
        }
        catch { return "?"; }
    }

    private static string SafeKey(Monsters.CharBossMonster? boss)
    {
        try { return boss?.Def?.Key ?? "?"; } catch { return "?"; }
    }

    private static int SafeAscension()
    {
        try { return BossLineups.Ascension(); } catch { return 0; }
    }

    private static int SafePlayers()
    {
        try { return BossLineups.PlayerCount(); } catch { return 1; }
    }

    /// <summary>
    /// 本局存档里的玩家生物是不是**全死了**（v0.2.276）。
    ///
    /// 为什么需要：战败瞬间 `CombatState.Players` 可能已经空了，用「战斗里数不到活人」会把
    /// **退出/回滚**也误判成战败。这里改看 `IRunState.Players` 各自 `Creature.IsAlive`：
    ///   · 全部读到且都死了 ⇒ 真战败；
    ///   · 还有人活着 / 一个都读不到 ⇒ 不下这个结论（保持 aborted）。
    /// </summary>
    private static bool RunPlayersAllDead(out int count)
    {
        count = 0;
        try
        {
            var players = MultiBossPlan.RunStateOrNull()?.Players;
            if (players == null) return false;

            bool anyAlive = false;
            bool anyRead = false;
            foreach (var player in players)
            {
                if (player == null) continue;
                count++;
                try
                {
                    var creature = player.Creature;
                    if (creature == null) continue;
                    anyRead = true;
                    if (creature.IsAlive) anyAlive = true;
                }
                catch { /* 单个读不到不影响其它 */ }
            }
            return anyRead && !anyAlive;
        }
        catch { return false; }
    }

    private static int SafeMaxHp(Monsters.CharBossMonster? boss)
    {
        try { return boss?.Creature?.MaxHp ?? 0; } catch { return 0; }
    }

    private static int SafeHp(Monsters.CharBossMonster? boss)
    {
        try { return boss?.Creature?.CurrentHp ?? 0; } catch { return 0; }
    }

    private static int SafeTurns(Monsters.CharBossMonster? boss)
    {
        try { return boss?.TurnNumber ?? 0; } catch { return 0; }
    }

    private static long SafeSeed()
    {
        try
        {
            var state = MultiBossPlan.RunStateOrNull();
            if (state == null) return 0;
            return (long)state.Rng.Seed;
        }
        catch { return 0; }
    }
}
