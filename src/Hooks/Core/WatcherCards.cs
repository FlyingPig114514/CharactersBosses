#if STS2_111   // 观者 Boss 只在 0.113 构建里（107 不加）
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;

namespace CharacterBosses.Core;

/// <summary>
/// **同时兼容两个观者模组**的引用层（用户 2026-11-15）。
///
/// 工坊里有两份互不相干的「观者」实现，玩家装哪个都可能来打我们的「神格观」：
///
/// | | 3747526116（Boninall，**优先**） | 3747492505（lamali） |
/// |---|---|---|
/// | 卡牌 | `WatcherMod.WatcherStrike_P` … | `Watcher.Code.Cards.Basic.StrikeWatcher` … |
/// | 模型 id | `WATCHER_STRIKE_P`（下划线） | `WATCHER-STRIKE_WATCHER`（连字符） |
/// | 姿态 API | `WatcherMod.WatcherCombatHelper.{EnterDivinity,ExitStance}`（只收 `Player`） | `Watcher.Code.Commands.StanceCmd.{EnterDivinity,ExitStance}`（收 `ctx, Player, card`） |
/// | 真言 | power 类型名 `Mantra` | `MantraPower` |
/// | 神格 | `Divinity`，回合**结束**自动退 | `DivinityStance`，回合**开始**自动退 |
/// | 至纯之水印牌钩子 | `BeforeCombatStart` | `BeforeHandDraw` |
///
/// 用户确认：「这两个模组除了多人牌和先古牌以外大部分牌都是相同的（都是从 1 代搬过来的）」，
/// 少量差异（许愿「永生不死」给覆甲/多重护甲、停顿升级后的额外格挡量）无伤大雅
/// ⇒ 我们**只用两边都有的牌**（剧本里那 20 张全部重叠）。
/// **两个都装时优先 3747526116**（现有实现，动画也与它一致）。
///
/// 铁律照旧：全部运行期反射，绝不编译期引用别人的程序集。
/// </summary>
internal static class WatcherCards
{
    internal enum Provider { None, Boninall, Lamali }

    private static bool _probed;
    private static long _lastProbeMs = long.MinValue;
    private static bool _loggedMissing;
    private static Assembly? _preferred;      // Boninall（优先）
    private static Assembly? _secondary;      // lamali
    private static Provider _provider = Provider.None;

    private static readonly Dictionary<string, Type?> _types = new(StringComparer.Ordinal);

    internal static Provider Current
    {
        get { _ = ModAssembly(); return _provider; }
    }

    internal static string ProviderName => _provider switch
    {
        Provider.Boninall => "3747526116（Boninall）",
        Provider.Lamali => "3747492505（lamali）",
        _ => "未装",
    };

    /// <summary>
    /// 观者实现的**身份串**（联机能力位用）：`boninall` / `lamali` / 空串 = 没装。
    ///
    /// ★ v0.2.711（用户 2026-11-16）：「只有当所有玩家都装了相同的观者 mod（指统一为 A 或 B 版）
    ///   时才让观者有可能作为 boss 出现」⇒ 每台机器把自己的这个串广播出去，
    ///   房主确认「所有人都一样」才把观者放进三层候补表（见 `BossPicksSync.WatcherAllowedHere`）。
    /// </summary>
    internal static string ProviderId
    {
        get
        {
            Probe();
            return _provider switch
            {
                Provider.Boninall => "boninall",
                Provider.Lamali => "lamali",
                _ => string.Empty,
            };
        }
    }

    /// <summary>优先那一份（有 Boninall 就用它）。</summary>
    internal static Assembly? ModAssembly()
    {
        Probe();
        return _preferred ?? _secondary;
    }

    private static void Probe()
    {
        if (_probed) return;
        // ★ v0.2.906（用户 2026-11-23「她的泪滴吊坠盒呢」）：**没探到就不锁**。
        //   以前这里第一行就 `_probed = true` —— 只要有一次在观者模组还没加载时探过，
        //   之后就永远认为「没装」，所有 `T(...)` 全返回 null（遗物静默消失，但是卡牌
        //   因为后面还有别的入口看不出来）。和 v0.2.686 修 `WatcherModPresence.Probe` 是同一个坑。
        //   现在：没探到不锁；为避免每帧都扫程序集，最快每 2 秒再探一次。
        long nowMs = Environment.TickCount64;
        if (_lastProbeMs != long.MinValue && nowMs - _lastProbeMs < 2000) return;
        _lastProbeMs = nowMs;
        try
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                string? name = null;
                try { name = asm.GetName().Name; } catch { continue; }
                if (string.IsNullOrEmpty(name)) continue;
                if (name.StartsWith("CharacterBosses", StringComparison.OrdinalIgnoreCase)) continue;
                if (name.IndexOf("Watcher", StringComparison.OrdinalIgnoreCase) < 0) continue;

                bool isBoninall = asm.GetType("WatcherMod.WatcherCombatHelper", false) != null
                                  || asm.GetType("WatcherMod.WatcherCardPool", false) != null;
                bool isLamali = asm.GetType("Watcher.Code.Commands.StanceCmd", false) != null;

                if (isBoninall) _preferred = asm;
                else if (isLamali && _secondary == null) _secondary = asm;
            }

            _provider = _preferred != null ? Provider.Boninall
                        : _secondary != null ? Provider.Lamali
                        : Provider.None;

            if (_provider != Provider.None)
            {
                _probed = true;      // 真探到了才锁死
                Logger.Log($"[CharBoss] 观者引用层：使用 {ProviderName}" +
                           (_preferred != null && _secondary != null
                               ? "（两个观者模组都在 ⇒ **优先 3747526116**）"
                               : string.Empty));
            }
            else if (!_loggedMissing)
            {
                _loggedMissing = true;
                Logger.Log("[CharBoss] 观者引用层：暂未发现观者模组（不锁死，最多每 2 秒再探一次）");
            }
        }
        catch { _provider = Provider.None; }
    }

    /// <summary>按短类名取类型：先优先那一份，再另一份，最后在各自程序集里按类名全扫。</summary>
    internal static Type? T(string shortName)
    {
        if (string.IsNullOrEmpty(shortName)) return null;
        if (_types.TryGetValue(shortName, out var cached)) return cached;

        Probe();
        Type? found = null;
        foreach (var asm in new[] { _preferred, _secondary })
        {
            if (asm == null) continue;
            try
            {
                found = asm.GetType("WatcherMod." + shortName, false)
                        ?? asm.GetType("Watcher.Code.Cards.Basic." + shortName, false)
                        ?? asm.GetType("Watcher.Code.Cards.Common." + shortName, false)
                        ?? asm.GetType("Watcher.Code.Cards.Uncommon." + shortName, false)
                        ?? asm.GetType("Watcher.Code.Cards.Rare." + shortName, false)
                        ?? asm.GetType("Watcher.Code.Cards.Token." + shortName, false)
                        ?? asm.GetType("Watcher.Code.Relics." + shortName, false)
                        ?? asm.GetType("Watcher.Code.Powers." + shortName, false)
                        ?? asm.GetType("Watcher.Code.Stances." + shortName, false)
                        ?? asm.GetType(shortName, false);
                if (found == null)
                {
                    foreach (var t in asm.GetTypes())
                    {
                        if (string.Equals(t.Name, shortName, StringComparison.Ordinal)) { found = t; break; }
                    }
                }
            }
            catch { found = null; }
            if (found != null) break;
        }

        // ★ v0.2.906：**只缓存「找到了」的结果**。以前连 null 一起缓存 ⇒ 观者模组晚加载时
        //   那些名字就永久是 null（遗物静默消失）。现在没找到就下次再试（Probe 有 2 秒节流）。
        if (found != null) _types[shortName] = found;
        return found;
    }

    /// <summary>先按类名找（不依赖 ModelDb），再按模型 id 兜底（两份实现的 id 前缀不同）。</summary>
    private static Type? Card(string boninallName, string lamaliName, string boninallId, string lamaliId)
    {
        var byName = T(boninallName) ?? (string.IsNullOrEmpty(lamaliName) ? null : T(lamaliName));
        if (byName != null) return byName;

        foreach (var id in new[] { boninallId, lamaliId })
        {
            if (string.IsNullOrEmpty(id)) continue;
            try
            {
                foreach (var c in ModelDb.AllCards)
                {
                    string? entry = null;
                    try { entry = c.Id.Entry; } catch { continue; }
                    if (string.Equals(entry, id, StringComparison.OrdinalIgnoreCase)) return c.GetType();
                }
            }
            catch { /* ModelDb 还没就绪就算没找到 */ }
        }
        return null;
    }

    // ── 剧本用到的牌（两份实现都有的那批）──────────────────────────────────
    internal static Type? Wish => Card("WatcherWish_P", "WishWatcher", "WATCHER_WISH_P", "WATCHER-WISH_WATCHER");
    internal static Type? Strike => Card("WatcherStrike_P", "StrikeWatcher", "WATCHER_STRIKE_P", "WATCHER-STRIKE_WATCHER");
    internal static Type? Defend => Card("WatcherDefend_P", "DefendWatcher", "WATCHER_DEFEND_P", "WATCHER-DEFEND_WATCHER");
    internal static Type? Vigilance => Card("WatcherVigilance", "Vigilance", "WATCHER_VIGILANCE", "WATCHER-VIGILANCE");
    internal static Type? Brilliance => Card("WatcherBrilliance", "Brilliance", "WATCHER_BRILLIANCE", "WATCHER-BRILLIANCE");
    internal static Type? Worship => Card("WatcherWorship", "Worship", "WATCHER_WORSHIP", "WATCHER-WORSHIP");
    internal static Type? Miracle => Card("WatcherMiracle", "Miracle", "WATCHER_MIRACLE", "WATCHER-MIRACLE");
    internal static Type? ConjureBlade => Card("WatcherConjureBlade", "ConjureBlade", "WATCHER_CONJURE_BLADE", "WATCHER-CONJURE_BLADE");
    internal static Type? Consecrate => Card("WatcherConsecrate", "Consecrate", "WATCHER_CONSECRATE", "WATCHER-CONSECRATE");
    internal static Type? EmptyFist => Card("WatcherEmptyFist", "EmptyFist", "WATCHER_EMPTY_FIST", "WATCHER-EMPTY_FIST");
    internal static Type? WaveOfTheHand => Card("WatcherWaveOfTheHand", "WaveOfTheHand", "WATCHER_WAVE_OF_THE_HAND", "WATCHER-WAVE_OF_THE_HAND");
    internal static Type? DeceiveReality => Card("WatcherDeceiveReality", "DeceiveReality", "WATCHER_DECEIVE_REALITY", "WATCHER-DECEIVE_REALITY");
    internal static Type? Safety => Card("WatcherSafety", "Safety", "WATCHER_SAFETY", "WATCHER-SAFETY");
    internal static Type? Expunger => Card("WatcherExpunger", "Expunger", "WATCHER_EXPUNGER", "WATCHER-EXPUNGER");
    internal static Type? Devotion => Card("WatcherDevotion", "Devotion", "WATCHER_DEVOTION", "WATCHER-DEVOTION");
    internal static Type? Swivel => Card("WatcherSwivel", "Swivel", "WATCHER_SWIVEL", "WATCHER-SWIVEL");
    internal static Type? Sanctity => Card("WatcherSanctity", "Sanctity", "WATCHER_SANCTITY", "WATCHER-SANCTITY");
    internal static Type? Conclude => Card("WatcherConclude", "Conclude", "WATCHER_CONCLUDE", "WATCHER-CONCLUDE");
    internal static Type? Greed => typeof(MegaCrit.Sts2.Core.Models.Cards.Greed);   // 贪婪（本体牌）

    // ── 三层 2 号「Ω观」用到的牌（用户 2026-11-16）────────────────────────────
    internal static Type? Alpha => Card("WatcherAlpha", "Alpha", "WATCHER_ALPHA", "WATCHER-ALPHA");
    internal static Type? Beta => Card("WatcherBeta", "Beta", "WATCHER_BETA", "WATCHER-BETA");
    internal static Type? Omega => Card("WatcherOmega", "Omega", "WATCHER_OMEGA", "WATCHER-OMEGA");
    internal static Type? PressurePoints => Card("WatcherPressurePoints", "PressurePoints", "WATCHER_PRESSURE_POINTS", "WATCHER-PRESSURE_POINTS");
    internal static Type? Establishment => Card("WatcherEstablishment", "Establishment", "WATCHER_ESTABLISHMENT", "WATCHER-ESTABLISHMENT");
    internal static Type? Meditate => Card("WatcherMeditate", "Meditate", "WATCHER_MEDITATE", "WATCHER-MEDITATE");
    internal static Type? EmptyBody => Card("WatcherEmptyBody", "EmptyBody", "WATCHER_EMPTY_BODY", "WATCHER-EMPTY_BODY");
    internal static Type? Eruption => Card("WatcherEruption_P", "Eruption", "WATCHER_ERUPTION_P", "WATCHER-ERUPTION");
    internal static Type? Crescendo => Card("WatcherCrescendo", "Crescendo", "WATCHER_CRESCENDO", "WATCHER-CRESCENDO");
    internal static Type? Tranquility => Card("WatcherTranquility", "Tranquility", "WATCHER_TRANQUILITY", "WATCHER-TRANQUILITY");
    internal static Type? FearNoEvil => Card("WatcherFearNoEvil", "FearNoEvil", "WATCHER_FEAR_NO_EVIL", "WATCHER-FEAR_NO_EVIL");
    internal static Type? MentalFortress => Card("WatcherMentalFortress", "MentalFortress", "WATCHER_MENTAL_FORTRESS", "WATCHER-MENTAL_FORTRESS");
    internal static Type? LikeWater => Card("WatcherLikeWater", "LikeWater", "WATCHER_LIKE_WATER", "WATCHER-LIKE_WATER");
    /// <summary>笨拙（本体状态牌，用户剧本第 2 回合手里那张）。</summary>
    internal static Type? Clumsy => typeof(MegaCrit.Sts2.Core.Models.Cards.Clumsy);

    // ── 二层 1 号「印牌观」用到的牌（用户 2026-11-16）────────────────────────
    /// <summary>改造现实（B `WATCHER-CARVE_REALITY`）：伤害 + **印一张「惩恶」进手牌**。</summary>
    internal static Type? CarveReality => Card("WatcherCarveReality", "CarveReality",
        "WATCHER_CARVE_REALITY", "WATCHER-CARVE_REALITY");

    /// <summary>惩恶（衍生牌，`CardRarity.Token`）：改造现实印出来的那张。</summary>
    internal static Type? Smite => Card("WatcherSmite", "Smite", "WATCHER_SMITE", "WATCHER-SMITE");

    /// <summary>义愤填膺：在愤怒 ⇒ 给所有敌人易伤；否则进入愤怒。**意图按「策略」显示**（用户要求）。</summary>
    internal static Type? Indignation => Card("WatcherIndignation", "Indignation",
        "WATCHER_INDIGNATION", "WATCHER-INDIGNATION");

    /// <summary>内心宁静：在平静 ⇒ 抽牌；否则进入平静。</summary>
    internal static Type? InnerPeace => Card("WatcherInnerPeace", "InnerPeace",
        "WATCHER_INNER_PEACE", "WATCHER-INNER-PEACE");

    /// <summary>操控现实（**藏品/卡**，模组自带）：她这只 Boss 的特殊效果同名，但效果相反（见 `BossMasterRealityPower`）。</summary>
    internal static Type? MasterReality => Card("WatcherMasterReality", "MasterReality",
        "WATCHER_MASTER_REALITY", "WATCHER-MASTER_REALITY");

    // ── 「印牌观」通过色彩哲学家拿到的三张**储君**牌（本体牌，编译期已知）──────────
    /// <summary>武器库（本体 `Arsenal`，1 费 Power）：`ArsenalPower(N)` ⇒ **每生成一张牌 +N 力量**。</summary>
    internal static Type? Arsenal => typeof(MegaCrit.Sts2.Core.Models.Cards.Arsenal);

    /// <summary>创世之柱（本体 `PillarOfCreation`，1 费 Power）：`PillarOfCreationPower(N)` ⇒ **每生成一张牌 +N 格挡**。</summary>
    internal static Type? PillarOfCreation => typeof(MegaCrit.Sts2.Core.Models.Cards.PillarOfCreation);

    /// <summary>何人僭越（本体 `KnowThyPlace`，**0 费** Skill）：给目标 易伤 + 虚弱。</summary>
    internal static Type? KnowThyPlace => typeof(MegaCrit.Sts2.Core.Models.Cards.KnowThyPlace);

    // ── 二层 2 号「保留观」用到的 5 张（用户 2026-11-17）────────────────────────
    //   这 5 张是**观者模组**独有的牌（本体没有）⇒ 两版类名都写进 `Card(...)`：
    //   A 版 `WatcherMod.WatcherEstablishment` / B 版 `Watcher.Code.Cards.*.Establishment`。
    //   确立基础已经在上面「Ω观」那一组定义过了（同一个类），这里不重复。

    /// <summary>时之沙（`SandsOfTime`，4 费攻击，自带保留）：伤害 20（升级 +6），每被保留一次费用 −1（本场）。</summary>
    internal static Type? SandsOfTime => Card("WatcherSandsOfTime", "SandsOfTime",
        "WATCHER_SANDS_OF_TIME", "WATCHER-SANDS_OF_TIME");

    /// <summary>旋转打击（`WindmillStrike`，自带保留）：伤害 7，**每被保留一次 +4**（本场）。</summary>
    internal static Type? WindmillStrike => Card("WatcherWindmillStrike", "WindmillStrike",
        "WATCHER_WINDMILL_STRIKE", "WATCHER-WINDMILL_STRIKE");

    /// <summary>坚韧（`Perseverance`，自带保留）：格挡 5，**每被保留一次 +2**（本场）。</summary>
    internal static Type? Perseverance => Card("WatcherPerseverance", "Perseverance",
        "WATCHER_PERSEVERANCE", "WATCHER-PERSEVERANCE");

    // ── 一层 1 号「预见观」用到的牌/遗物（用户 2026-11-17）────────────────────
    //   卡名 → 观者 mod 的 id 全部是从观者 pck 的 zhs `cards.json` / `relics.json` 里查出来的：
    //   涅槃 `WATCHER_NIRVANA`、迂回 `WATCHER_WEAVE`、先见之明 `WATCHER_FORESIGHT`、
    //   天眼 `WATCHER_THIRD_EYE`、斩破命运 `WATCHER_CUT_THROUGH_FATE`、
    //   碰撞连击 `WATCHER_BOWLING_BASH`、通晓万物 `WATCHER_OMNISCIENCE`（藏品）、
    //   美琅脂 = 观者遗物 `MELANGE`（「每次洗抽牌堆时预见 3」）。
    //   受伤 / 贪婪 / 笨拙 是本体牌（`INJURY` / `GREED` / `CLUMSY`）。
    internal static Type? Nirvana => Card("WatcherNirvana", "Nirvana", "WATCHER_NIRVANA", "WATCHER-NIRVANA");
    internal static Type? Weave => Card("WatcherWeave", "Weave", "WATCHER_WEAVE", "WATCHER-WEAVE");
    internal static Type? Foresight => Card("WatcherForesight", "Foresight", "WATCHER_FORESIGHT", "WATCHER-FORESIGHT");
    internal static Type? ThirdEye => Card("WatcherThirdEye", "ThirdEye", "WATCHER_THIRD_EYE", "WATCHER-THIRD_EYE");
    internal static Type? CutThroughFate => Card("WatcherCutThroughFate", "CutThroughFate",
        "WATCHER_CUT_THROUGH_FATE", "WATCHER-CUT_THROUGH_FATE");
    internal static Type? BowlingBash => Card("WatcherBowlingBash", "BowlingBash",
        "WATCHER_BOWLING_BASH", "WATCHER-BOWLING_BASH");
    /// <summary>通晓万物（`Omniscience`，4 费技能）：这一只 Boss 的**藏品**。</summary>
    internal static Type? Omniscience => Card("WatcherOmniscience", "Omniscience",
        "WATCHER_OMNISCIENCE", "WATCHER-OMNISCIENCE");
    /// <summary>受伤（本体诅咒 `INJURY`）：剧本里被她预见丢掉的那张。</summary>
    internal static Type? Injury => typeof(MegaCrit.Sts2.Core.Models.Cards.Injury);
    /// <summary>美琅脂（观者遗物 `MELANGE`）：「每次洗抽牌堆时预见 3」。</summary>
    internal static Type? Melange => T("Melange");
    /// <summary>手里剑（本体遗物 `Shuriken`）：每打出 3 张攻击牌 +1 力量。</summary>
    internal static Type? Shuriken => typeof(MegaCrit.Sts2.Core.Models.Relics.Shuriken);
    /// <summary>苦无（本体遗物 `Kunai`）：每打出 3 张攻击牌 +1 敏捷。</summary>
    internal static Type? Kunai => typeof(MegaCrit.Sts2.Core.Models.Relics.Kunai);
    /// <summary>停顿（`Halt`，0 费技能）：获得格挡；若在愤怒中额外获得格挡。</summary>
    internal static Type? Halt => Card("WatcherHalt", "Halt",
        "WATCHER_HALT", "WATCHER-HALT");

    // ── 三层 3 号「红蓝观」（内部 Key `WATCHER_RUSHDOWN` = 猛虎下山，用户 2026-11-18）─────
    //   卡名 → id 依旧是从观者 pck 的 zhs `cards.json` / `relics.json` 查出来的：
    //   猛虎下山 `WATCHER_RUSHDOWN`、粉碎关节 `WATCHER_CRUSH_JOINTS`、
    //   腾越 `WATCHER_VAULT`（模组中译「腾跃」，用户口中是「腾越」，同一张）。
    //   净化 / 腐朽 是**本体**牌（无色 `PURITY` / 诅咒 `DECAY`）。
    /// <summary>猛虎下山（`Rushdown`，观者能力）：进入愤怒时抽牌。</summary>
    internal static Type? Rushdown => Card("WatcherRushdown", "Rushdown",
        "WATCHER_RUSHDOWN", "WATCHER-RUSHDOWN");
    /// <summary>粉碎关节（`CrushJoints`，1 费攻击）：造成伤害；若本回合打出过技能牌则施加易伤。</summary>
    internal static Type? CrushJoints => Card("WatcherCrushJoints", "CrushJoints",
        "WATCHER_CRUSH_JOINTS", "WATCHER-CRUSH_JOINTS");
    /// <summary>腾越（`Vault`，3 费技能）：结束本回合并获得一个额外回合。</summary>
    internal static Type? Vault => Card("WatcherVault", "Vault",
        "WATCHER_VAULT", "WATCHER-VAULT");
    /// <summary>净化（本体无色 `Purity`，0 费技能）：消耗手中任意张牌。</summary>
    internal static Type? Purity => typeof(MegaCrit.Sts2.Core.Models.Cards.Purity);
    /// <summary>腐朽（本体诅咒 `Decay`）：回合结束时若在手牌则受伤害。</summary>
    internal static Type? Decay => typeof(MegaCrit.Sts2.Core.Models.Cards.Decay);

    /// <summary>泪滴吊坠盒（观者遗物 `TEARDROP_LOCKET`）：每场战斗开始时处于平静。</summary>
    internal static Type? TeardropLocket => T("TeardropLocket");
    /// <summary>符文金字塔（本体遗物 `RUNIC_PYRAMID`）：回合结束不再弃牌。</summary>
    internal static Type? RunicPyramid => typeof(MegaCrit.Sts2.Core.Models.Relics.RunicPyramid);
    /// <summary>音叉（本体遗物 `TUNING_FORK`）。</summary>
    internal static Type? TuningFork => typeof(MegaCrit.Sts2.Core.Models.Relics.TuningFork);
    /// <summary>布质果实（本体遗物 `LOOMING_FRUIT`，+31 最大生命 —— 她的血量要算上它）。</summary>
    internal static Type? LoomingFruit => typeof(MegaCrit.Sts2.Core.Models.Relics.LoomingFruit);
    /// <summary>坚固钳子（本体遗物 `STURDY_CLAMP`）。</summary>
    internal static Type? SturdyClamp => typeof(MegaCrit.Sts2.Core.Models.Relics.SturdyClamp);
    /// <summary>腰带抽打（`SashWhip`，1 费攻击）：造成伤害；若上一张是技能牌则施加虚弱。</summary>
    internal static Type? SashWhip => Card("WatcherSashWhip", "SashWhip",
        "WATCHER_SASH_WHIP", "WATCHER-SASH_WHIP");

    /// <summary>当头棒喝（`Wallop`，2 费攻击）：造成伤害并获得等量格挡。</summary>
    internal static Type? Wallop => Card("WatcherWallop", "Wallop",
        "WATCHER_WALLOP", "WATCHER-WALLOP");

    // ── 一层 2 号「疾风观」（内部 Key `WATCHER_FLURRY` = 疾风连击，用户 2026-11-23）──────
    //   两份观者模组的类名 / 模型 id 都是从各自 dll 里核过的（`ilspy-cli types` + pck 的 zhs json）：
    //   A 版（3747526116，`WatcherMod.`）  : WatcherFlurryOfBlows / WatcherFollowUp / WatcherFasting2 / WatcherTalkToTheHand
    //   B 版（3747492505，`Watcher.Code.`）: FlurryOfBlows / FollowUp / Fasting / TalkToTheHand
    //   注意 A 版的斋戒模型 id 是 `WATCHER_FASTING2`（不是 `WATCHER_FASTING`）。
    /// <summary>疾风连击（`FlurryOfBlows`，1 费攻击）：造成伤害；每次转换姿态时从弃牌堆回到手牌。</summary>
    internal static Type? FlurryOfBlows => Card("WatcherFlurryOfBlows", "FlurryOfBlows",
        "WATCHER_FLURRY_OF_BLOWS", "WATCHER-FLURRY_OF_BLOWS");
    /// <summary>以手拒之（`TalkToTheHand`，1 费攻击）：造成伤害；目标受到攻击时她获得格挡。</summary>
    internal static Type? TalkToTheHand => Card("WatcherTalkToTheHand", "TalkToTheHand",
        "WATCHER_TALK_TO_THE_HAND", "WATCHER-TALK_TO_THE_HAND");
    /// <summary>追击（`FollowUp`，1 费攻击）：造成伤害；若上一张牌是攻击牌则获得 1 能量。</summary>
    internal static Type? FollowUp => Card("WatcherFollowUp", "FollowUp",
        "WATCHER_FOLLOW_UP", "WATCHER-FOLLOW_UP");
    /// <summary>斋戒（`Fasting`，2 费能力）：+力量 +敏捷；每回合开始少获得 1 能量。</summary>
    internal static Type? Fasting => Card("WatcherFasting2", "Fasting",
        "WATCHER_FASTING2", "WATCHER-FASTING");

    /// <summary>
    /// **B 版（lamali）她此刻的姿态倍率**：1（无/平静）/ 2（愤怒）/ 3（神格）。
    ///
    /// ★ v0.2.706（用户 2026-11-16：「B 版欧米伽观愤怒时打出的攻击牌还是不翻倍」）：
    ///   B 版的姿态**不是挂在生物身上的 power**，而是绑在**玩家**上的模型：
    ///   <code>
    ///   private static readonly SpireField&lt;PlayerCombatState, WatcherStanceModel&gt; ActiveStance;
    ///   public static WatcherStanceModel GetStanceModel(Player player);   // WatcherModel
    ///   public static bool IsInStance&lt;T&gt;(Player player);
    ///   </code>
    ///   ⇒ `Creature.Powers` 里永远看不到它，`StanceMultiplierOf` 一直是 1（所以「预览按计划 ×2、实伤没翻」）。
    ///   这里直接问它自己的 API，靠类型名（`WrathStance` / `DivinityStance` / `CalmStance` / `NoStance`）判档。
    /// </summary>
    internal static int StanceMultiplierFromModel(Player? player)
    {
#if STS2_111
        try
        {
            if (player == null) return 1;
            if (Current != Provider.Lamali) return 1;
            var m = Method("Watcher.Code.Core.WatcherModel", "GetStanceModel");
            if (m == null) return 1;
            object? model = m.Invoke(null, new object?[] { player });
            string name = model?.GetType().Name ?? string.Empty;
            if (name.Contains("Divinity")) return 3;
            if (name.Contains("Wrath")) return 2;
            return 1;
        }
        catch { return 1; }
#else
        return 1;
#endif
    }

    /// <summary>
    /// 「点穴」这张牌会施加多少层印记（B 版实测订正，用户 2026-11-16）。
    ///
    /// · A 版：普通 `DynamicVar("MagicNumber")`（`WatcherPressurePoints.OnPlay` 里 `PowerCmd.Apply&lt;MarkPower&gt;(…, MagicNumber, …)`）；
    /// · B 版（lamali）：`WithPower&lt;MarkPower&gt;(8, 3)` ⇒ 变量键是 **`MarkPower`**。
    /// 以前只读 `MagicNumber` ⇒ B 版恒为 0：第 1 回合目标没有印记时攻势整条消失，
    /// 后面回合只显示「目标已有印记」那部分，且「本回合前面那张点穴叠的印记」永远是 0。
    /// </summary>
    internal static int PressurePointsMarks(CardModel? card)
    {
        if (card == null) return 0;
        int v = BossDamagePredictor.VarPreviewInt(card, "MarkPower", 0);
        if (v <= 0) v = BossDamagePredictor.VarPreviewInt(card, "MagicNumber", 0);
        return Math.Max(0, v);
    }

    /// <summary>至纯之水（观者初始遗物）：A = `WatcherMod.PureWater`，B = `Watcher.Code.Relics.PureWater`。</summary>
    internal static Type? PureWater => T("PureWater");

    /// <summary>
    /// **至纯之水两版对齐**（用户 2026-11-16：「这A版的至纯之水能生效，B版的至纯之水不能，什么情况？
    /// 能不能让它们都生效？或都不生效？」）——这里选**都生效**。
    ///
    /// 两版给的**牌完全一样**（战斗开始时把 1 张「奇迹」放进她的手牌，不升级），差别只在**钩子**：
    ///
    /// | | 3747526116（Boninall） | 3747492505（lamali） |
    /// |---|---|---|
    /// | 钩子 | `PureWater.BeforeCombatStart()` | `PureWater.BeforeHandDraw(player, ctx, state)` |
    /// | 条件 | 无（**没有 player 参数**） | `player == Owner` **且** `Owner.PlayerCombatState.TurnNumber == 1` |
    /// | 对影子玩家 | **会**触发（`Hook.BeforeCombatStart` 把所有监听者逐个派发一遍） | **永远不触发** |
    ///
    /// 反编译实证（`sts2.dll`）：
    ///   · `Hook.BeforeHandDraw(ICombatState, Player, ctx)` = 把所有监听者的 `BeforeHandDraw` 都叫一遍，
    ///     「是不是**我**的回合」这段判断在**遗物自己**的方法体里（`player == Owner`）；
    ///   · 但派发它的只有 `CombatManager.SetupPlayerTurn(turnState, player, …)`，而 `StartTurn` 里
    ///     `playersStartingTurn = CurrentSide == Player ? state.Players : new List<Player>()`
    ///     ⇒ **敌方回合根本不会为任何 Player 派发这条钩子**，她的影子玩家是敌方侧的 ⇒ B 版那张奇迹从来没出现过。
    ///
    /// 实机日志实证（A 版印牌观，2026-11-16 21:34:28→29）：
    /// `剧本手牌(4): [ARSENAL+,WATCHER_MASTER_REALITY+,WATCHER_CARVE_REALITY+,WATCHER_SMITE+]`
    /// 之后手牌变 5 张并多出 `WATCHER_MIRACLE`（`手牌栏：5 张里有 2 张本回合不会打 [WATCHER_SMITE,WATCHER_MIRACLE]`），
    /// 第 1 回合她**不会打它**（剧本没写），第 2 回合重建手牌时随弃手牌一起进弃牌堆。
    ///
    /// 所以这里**只给 lamali 版补一张**（Boninall 版原版自己会印，补了就成两张）；
    /// 张数（1）、落点（手牌末尾）、不升级 —— 全部对齐 A 版。
    ///
    /// ⚠ **调用点必须是「她第 1 回合的手牌建完之后」**（`CharBossMonster.PrepareTurn` 里
    /// `BossScriptRunner.BuildHand` 之后），不能放 `BossHookBridge.FireCombatStart`：
    /// `FireCombatStart` 早于 `PrepareTurn`，而 `PrepareTurn` 第一句就是 `BossPiles.DiscardHand(pcs)`
    /// ⇒ 补进去的牌下一步就进弃牌堆了（v0.2.723 实测：`补 1 张「奇迹」` → `手牌栏：1 张…`
    /// → `剧本手牌(4)`、`discard=1`，界面上看得见变化、她手里却没有）。
    /// A 版能看见正是因为它的 `BeforeCombatStart` 派发得**更晚**（晚于 `PrepareTurn`），躲过了这次弃牌。
    ///
    /// ⚠ 因为剧本没写这张牌，她**永远不会打出它** ⇒ 不会多出「奇迹打出 +1 能量」，
    ///   每回合的能量账（`BossTurn.Energy`）一个字都不用改。
    /// </summary>
    internal static void EnsurePureWaterMiracle(Player? shadow, MegaCrit.Sts2.Core.Combat.ICombatState? combat)
    {
        try
        {
            if (shadow == null || combat == null) return;

            // ① 她身上真有这件遗物吗（按**类型**判最稳：两版类名都叫 PureWater）
            var pureType = PureWater;
            bool has = false;
            try
            {
                var relics = shadow.Relics;
                for (int i = 0; i < (relics?.Count ?? 0); i++)
                {
                    var r = relics![i];
                    if (r == null) continue;
                    if (pureType != null && r.GetType() == pureType) { has = true; break; }
                    string? e = null;
                    try { e = r.Id.Entry; } catch { /* 忽略 */ }
                    if (!string.IsNullOrEmpty(e) &&
                        (e!.IndexOf("PURE_WATER", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         e.IndexOf("CLEAN_WATER", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         e.IndexOf("PUREWATER", StringComparison.OrdinalIgnoreCase) >= 0))
                    { has = true; break; }
                }
            }
            catch { has = false; }
            if (!has) return;

            // ② ★ v0.2.913（用户 2026-11-23：「她的至纯之水应该在**战斗开始**时就给她手里放一张奇迹，
            //   而不是她第一回合开始时才放」）：**两个版本都由我们自己补**。
            //   以前 A 版（Boninall）这里直接 return，指望它自己的 `BeforeCombatStart()` 印牌 ——
            //   而那个钩子对我们的影子玩家派发得**很晚**（在她自己回合开始之后），
            //   于是玩家在自己第 1 回合看到的她的手牌里没有奇迹。
            //   现在模组那次印牌由 `WatcherRuntimePatches.InstallPureWaterNoDouble` 压掉，
            //   统一走下面这条注入（时机 = 她第 1 回合手牌建好之后 = 玩家第 1 回合刚开始）。

            var pcs = shadow.PlayerCombatState;
            if (pcs == null) return;

            // ③ 幂等：手里已经有一张「奇迹」（比如剧本那一手正好也写了）就别再补
            int existing = 0;
            try
            {
                foreach (var c in pcs.Hand.Cards)
                {
                    if (c == null) continue;
                    string entry = string.Empty;
                    try { entry = c.Id.Entry ?? string.Empty; } catch { /* 忽略 */ }
                    if (entry.IndexOf("MIRACLE", StringComparison.OrdinalIgnoreCase) >= 0) existing++;
                }
            }
            catch { existing = 0; }
            if (existing > 0)
            {
                Logger.Log($"[CharBoss] 至纯之水：她手里已经有 {existing} 张「奇迹」⇒ 不重复补");
                return;
            }

            // ④ 造一张「奇迹」放进她手牌（和原版同一张牌：`CreateCard` 复制原型再绑 Owner）
            var miracleType = Miracle;
            if (miracleType == null)
            {
                Logger.Log("[CharBoss] 至纯之水：取不到「奇迹」这张牌 ⇒ 无法补");
                return;
            }
            var canonical = ModelDb.GetById<CardModel>(ModelDb.GetId(miracleType));
            if (canonical == null)
            {
                Logger.Log("[CharBoss] 至纯之水：「奇迹」原型取不到 ⇒ 无法补");
                return;
            }
            var card = combat.CreateCard(canonical, shadow);
            if (card == null)
            {
                Logger.Log("[CharBoss] 至纯之水：造牌失败 ⇒ 无法补");
                return;
            }
            pcs.Hand.AddInternal(card, -1, true);
            Logger.Log($"[CharBoss] 至纯之水（{ProviderName}）：她第 1 回合的手牌建好之后补 1 张「奇迹」" +
                       "（观众第 1 回合就能看到；剧本没写这张牌 ⇒ 她不会打它，能量账不变）");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 至纯之水补「奇迹」失败（不影响战斗）: " + e.Message);
        }
    }

    /// <summary>手摇鼓（进阶 9 遗物）。</summary>
    internal static Type? Damaru => T("Damaru");

    /// <summary>她现在的姿态模型类型名（B 版：`GetStanceModel(player)` 的类型；A 版没有这个概念 ⇒ 空串）。</summary>
    internal static string StanceModelName(Player? owner)
    {
#if STS2_111
        try
        {
            if (owner == null) return string.Empty;
            if (Current != Provider.Lamali) return string.Empty;
            var m = Method("Watcher.Code.Core.WatcherModel", "GetStanceModel");
            if (m == null) return string.Empty;
            object? model = m.Invoke(null, new object?[] { owner });
            return model?.GetType().Name ?? string.Empty;
        }
        catch { return string.Empty; }
#else
        return string.Empty;
#endif
    }

    /// <summary>
    /// **silent 强制**把她设成某个姿态（不需要 `PlayerChoiceContext`，`WatcherModel.SetStanceSilently&lt;T&gt;`）。
    ///
    /// ⚠ 语义（反编译 `WatcherModel.SetStance(…, silent: true)`）：走的是
    /// `current.CleanupWithoutExit(player)` + 直接写 `ActiveStance[pcs]`，
    /// **不跑 `OnExitStance`**（所以离开平静不会给那 +2），**也不跑 `OnEnterStance`**
    /// （所以进神格不会给那 +3 —— 那笔要自己补跑，见 <see cref="ForceDivinitySilentlyAsync"/>）。
    /// 只当 ctx 那条正规路真的失败时用。
    /// </summary>
    private static async Task<bool> SetStanceSilentlyAsync(Player? owner, string stanceTypeName)
    {
#if STS2_111
        try
        {
            if (owner == null || string.IsNullOrEmpty(stanceTypeName)) return false;
            var stanceType = ModAssembly()?.GetType("Watcher.Code.Stances." + stanceTypeName, false);
            var setSilently = Method("Watcher.Code.Core.WatcherModel", "SetStanceSilently");
            if (stanceType == null || setSilently == null)
            {
                Logger.Log($"[CharBoss] 强制设姿态（silent）：找不到 {stanceTypeName} / SetStanceSilently");
                return false;
            }
            var task = setSilently.MakeGenericMethod(stanceType)
                .Invoke(null, new object?[] { owner }) as Task;
            if (task != null) await task;
            return true;
        }
        catch (Exception e)
        {
            Logger.Log($"[CharBoss] 强制设姿态（silent {stanceTypeName}）失败: " + e.Message);
            return false;
        }
#else
        await Task.CompletedTask;
        return false;
#endif
    }

    private static async Task<bool> ForceDivinitySilentlyAsync(Player? owner,
                                                            MegaCrit.Sts2.Core.GameActions.Multiplayer.PlayerChoiceContext? ctx,
                                                            CardModel? source)
    {
#if STS2_111
        try
        {
            if (owner == null) return false;
            if (!await SetStanceSilentlyAsync(owner, "DivinityStance"))
            {
                Logger.Log("[CharBoss] 进神格（强制）：找不到 DivinityStance / SetStanceSilently");
                return false;
            }
            string after = StanceModelName(owner);
            Logger.Log($"[CharBoss] 进神格（强制 SetStanceSilently<DivinityStance>）⇒ 姿态模型 = {after}");
            if (!after.Contains("Divinity")) return false;

            // ★ v0.2.708（用户 2026-11-16 第五轮实测）：v0.2.707 的日志证明**姿态确实设上了**
            //   <code>
            //   进神格：StanceCmd.EnterDivinity 之后姿态模型 = NoStance
            //   进神格（强制 SetStanceSilently<DivinityStance>）⇒ 姿态模型 = DivinityStance
            //   </code>
            //   但 `SetStanceSilently` 是 **silent** 版 ⇒ 它**跳过了 `OnEnterStance`**（那 +3 能量、
            //   以及入姿态的其它钩子都在里面）⇒ 玩家看到的就是「没进神格」（能量没涨、也不亮）。
            //   所以这里补跑一次姿态模型自己的 `OnEnterStance(ctx, owner, source)`。
            try
            {
                int before = -1;
                try { before = owner.PlayerCombatState?.Energy ?? -1; } catch { /* 忽略 */ }

                var getModel = Method("Watcher.Code.Core.WatcherModel", "GetStanceModel");
                object? stance = getModel?.Invoke(null, new object?[] { owner });
                var onEnter = stance?.GetType().GetMethod(
                    "OnEnterStance", BindingFlags.Public | BindingFlags.Instance);
                if (onEnter != null)
                {
                    var useCtx = ctx ?? new MegaCrit.Sts2.Core.GameActions.Multiplayer.BlockingPlayerChoiceContext();
                    var t2 = onEnter.Invoke(stance, new object?[] { useCtx, owner, source }) as Task;
                    if (t2 != null) await t2;
                }

                int energyAfter = -1;
                try { energyAfter = owner.PlayerCombatState?.Energy ?? -1; } catch { /* 忽略 */ }
                Logger.Log($"[CharBoss] 进神格：已补跑 OnEnterStance（能量 {before} → {energyAfter}，" +
                           $"神格里={InDivinity(owner)}）");
            }
            catch (Exception e) { Logger.Log("[CharBoss] 进神格：补跑 OnEnterStance 失败: " + e.Message); }

            return true;
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 进神格（强制）失败: " + e.Message);
            return false;
        }
#else
        await Task.CompletedTask;
        return false;
#endif
    }

    // ── 姿态 / 真言（两份实现的接口不同，这里抹平）─────────────────────────
    /// <summary>进神格（观者自己的实现：+3 能量、攻击三倍）。</summary>
    internal static Task EnterDivinityAsync(Player? owner, CardModel? source,
                                            MegaCrit.Sts2.Core.GameActions.Multiplayer.PlayerChoiceContext? ctx = null)
        => EnterDivinityCoreAsync(owner, source, ctx);

    private static async Task EnterDivinityCoreAsync(Player? owner, CardModel? source,
                                                     MegaCrit.Sts2.Core.GameActions.Multiplayer.PlayerChoiceContext? ctx)
    {
        try
        {
            Probe();
            if (_provider == Provider.Boninall)
            {
                var m = Method("WatcherMod.WatcherCombatHelper", "EnterDivinity");
                var t = m?.Invoke(null, new object?[] { owner, source }) as Task;
                if (t != null) await t;
                return;
            }
            if (_provider == Provider.Lamali)
            {
                // ★ v0.2.709（用户 2026-11-16 第六轮：「她已经处在神格时，再积累 10 层真言，不会像
                //   特殊效果里说的一样先退出姿态然后再进入神格（这会导致没有成功加费）」）：
                //   反编译 `WatcherModel.SetStance` 第一件事就是
                //   `if (current?.GetType() == newCanonical.GetType()) return;`
                //   ⇒ **本来就该由调用方先退姿态**（`ResolveDivinityAsync` 的 ① 会退）。
                //   但 v0.2.708 及之前 `StanceCmd.ExitStance` 因为反射重载歧义根本没被调到
                //   （见 `Overload` 的注释），于是她一直停在神格里，重新进神格被那句 return 吃掉 ⇒ 不加费。
                //   这里再兜一层：进来时发现她已经在神格，就先 silent 退掉，保证「退出 → 再进入」真的发生。
                string beforeStance = StanceModelName(owner);
                if (beforeStance.Contains("Divinity"))
                {
                    await SetStanceSilentlyAsync(owner, "NoStance");
                    Logger.Log($"[CharBoss] 进神格：她本来就在神格 ⇒ 先退出姿态再重新进" +
                               $"（退完 = {Describe(StanceModelName(owner))}）");
                }

                // 参数类型写全：`StanceCmd.EnterDivinity` 有 Player / IEnumerable<Player> 两个重载，
                // 只按名字取会抛 AmbiguousMatchException（旧写法被吞掉 ⇒ 静默不执行）。
                var m = Overload("Watcher.Code.Commands.StanceCmd", "EnterDivinity",
                                 new[]
                                 {
                                     typeof(PlayerChoiceContext), typeof(Player),
                                     typeof(CardModel),
                                 });
                var useCtx = ctx ?? new MegaCrit.Sts2.Core.GameActions.Multiplayer.BlockingPlayerChoiceContext();
                try
                {
                    var task = m?.Invoke(null, new object?[] { useCtx, owner, source }) as Task;
                    if (task != null) await task;
                }
                catch (Exception e)
                {
                    // 姿态可能已经设上了（`ActiveStance` 的赋值在 OnEnterStance 之前），
                    // 后面那句「之后姿态模型」会照实打印；这里只记一行，别把兜底也跳过。
                    Logger.Log("[CharBoss] 进神格：StanceCmd.EnterDivinity 抛异常（继续看姿态模型 / 走兜底）: "
                               + e.Message);
                }

                string after = StanceModelName(owner);
                Logger.Log($"[CharBoss] 进神格：StanceCmd.EnterDivinity 之后姿态模型 = " +
                           $"{(string.IsNullOrEmpty(after) ? "（A 版没有这个）" : Describe(after))}");
                if (!after.Contains("Divinity"))
                {
                    if (await ForceDivinitySilentlyAsync(owner, ctx, source)) return;
                    Logger.Log("[CharBoss] 进神格：两条路都没成功（姿态模型仍不是 Divinity）");
                }
                return;
            }
        }
        catch (Exception e) { Logger.Log("[CharBoss] 进神格失败: " + e.Message); }
    }

    /// <summary>
    /// 退出当前姿态。**进神格前必须先退**，否则拿不到入姿态的 3 点能量。
    ///
    /// ★ v0.2.709：B 版这条路以前**从来没生效过** —— `StanceCmd.ExitStance` 有两份重载
    ///   （`Player` / `IEnumerable&lt;Player&gt;`），旧反射工具只按名字取 ⇒ 抛歧义被吞 ⇒ 返回 null
    ///   ⇒ `m?.Invoke` 什么都不做。现在：①按参数类型精确取；②真的 await 完再读姿态模型；
    ///   ③还没退掉就 silent 强制清（静默版不给「离开平静 +2」，所以只当兜底）。
    /// </summary>
    internal static async Task ExitStanceAsync(Player? owner,
                                               MegaCrit.Sts2.Core.GameActions.Multiplayer.PlayerChoiceContext? ctx = null)
    {
        try
        {
            Probe();
            if (_provider == Provider.Boninall)
            {
                var m = Method("WatcherMod.WatcherCombatHelper", "ExitStance");
                var t = m?.Invoke(null, new object?[] { owner }) as Task;
                if (t != null) await t;
                return;
            }
            if (_provider == Provider.Lamali)
            {
                var m = Overload("Watcher.Code.Commands.StanceCmd", "ExitStance",
                                 new[]
                                 {
                                     typeof(PlayerChoiceContext), typeof(Player),
                                     typeof(CardModel),
                                 });
                var useCtx = ctx ?? new MegaCrit.Sts2.Core.GameActions.Multiplayer.BlockingPlayerChoiceContext();
                string before = StanceModelName(owner);
                try
                {
                    var t = m?.Invoke(null, new object?[] { useCtx, owner, null }) as Task;
                    if (t != null) await t;
                }
                catch (Exception e)
                {
                    Logger.Log("[CharBoss] 退出姿态：StanceCmd.ExitStance 抛异常（继续看姿态模型 / 走兜底）: "
                               + e.Message);
                }

                string after = StanceModelName(owner);
                Logger.Log($"[CharBoss] 退出姿态：{Describe(before)} → {Describe(after)}" +
                           (m == null ? "（⚠ 反射没找到 ExitStance）" : string.Empty));

                if (!after.Contains("NoStance"))
                {
                    if (await SetStanceSilentlyAsync(owner, "NoStance"))
                    {
                        Logger.Log($"[CharBoss] 退出姿态（silent 强制清）⇒ " +
                                   $"{Describe(StanceModelName(owner))}");
                    }
                }
                return;
            }
        }
        catch (Exception e) { Logger.Log("[CharBoss] 退出姿态失败: " + e.Message); }
    }

    /// <summary>获得真言。A 走 `WatcherCombatHelper.GainMantra`；B 直接给 `MantraPower` 加层（它自己结算神格）。</summary>
    internal static Task GainMantraAsync(Player? owner, int amount, CardModel? source)
    {
        try
        {
            if (amount <= 0 || owner?.Creature == null) return Task.CompletedTask;
            Probe();
            if (_provider == Provider.Boninall)
            {
                var m = Method("WatcherMod.WatcherCombatHelper", "GainMantra");
                return m?.Invoke(null, new object?[] { owner, amount, source }) as Task ?? Task.CompletedTask;
            }
            if (_provider == Provider.Lamali)
            {
                var mantra = T("MantraPower");
                if (mantra == null) return Task.CompletedTask;
                var apply = typeof(MegaCrit.Sts2.Core.Commands.PowerCmd)
                    .GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(x => x.Name == "Apply" && x.IsGenericMethodDefinition
                                         && x.GetParameters().Length == 6
                                                                              && x.GetParameters()[1].ParameterType == typeof(Creature));
                if (apply == null) return Task.CompletedTask;
                return apply.MakeGenericMethod(mantra)
                    .Invoke(null, new object?[]
                    {
                        new ThrowingPlayerChoiceContext(), owner.Creature, (decimal)amount,
                        owner.Creature, source, false,
                    }) as Task ?? Task.CompletedTask;
            }
        }
        catch (Exception e) { Logger.Log("[CharBoss] 获得真言失败: " + e.Message); }
        return Task.CompletedTask;
    }

    /// <summary>她身上**现在的**真言层数（A: `Mantra` / B: `MantraPower`）。</summary>
    internal static int MantraOf(Player? owner) => PowerAmount(owner, "Mantra", "MantraPower");

    /// <summary>把真言直接改到指定层数（不转负由调用方保证）。</summary>
    internal static void SetMantra(Player? owner, int value)
    {
        try
        {
            var powers = owner?.Creature?.Powers;
            if (powers == null) return;
            for (int i = 0; i < powers.Count; i++)
            {
                var p = powers[i];
                if (p == null) continue;
                string name;
                try { name = p.GetType().Name; } catch { continue; }
                if (name != "Mantra" && name != "MantraPower") continue;
                p.SetAmount(Math.Max(0, value), true);
                return;
            }
        }
        catch { /* 忽略 */ }
    }

    /// <summary>进「平静」（A: `WatcherCombatHelper.EnterCalm`；B: `StanceCmd.EnterCalm`）。</summary>
    internal static Task EnterCalmAsync(Player? owner)
    {
        try
        {
            Probe();
            if (_provider == Provider.Boninall)
            {
                var m = Method("WatcherMod.WatcherCombatHelper", "EnterCalm");
                return m?.Invoke(null, new object?[] { owner, null }) as Task ?? Task.CompletedTask;
            }
            if (_provider == Provider.Lamali)
            {
                // ⚠ v0.2.709：这里同样要**按参数类型取重载**（`EnterCalm` 也有 Player / IEnumerable<Player> 两份），
                //   否则返回 null ⇒ 静默不执行（「如水」「渐强」进平静那条路以前一直没生效）。
                var m = Overload("Watcher.Code.Commands.StanceCmd", "EnterCalm",
                                 new[]
                                 {
                                     typeof(PlayerChoiceContext), typeof(Player),
                                     typeof(CardModel),
                                 });
                return m?.Invoke(null, new object?[] { new ThrowingPlayerChoiceContext(), owner, null }) as Task
                       ?? Task.CompletedTask;
            }
        }
        catch (Exception e) { Logger.Log("[CharBoss] 进入平静失败: " + e.Message); }
        return Task.CompletedTask;
    }

    /// <summary>
    /// 按**运行期拿到的 Type** 施加一个能力（我们编译期没有观者模组的类型 ⇒ 只能反射泛型）。
    /// 与 <see cref="GainMantraAsync"/> 里 B 版那段完全同一套写法，这里抽出来给「印记」等复用。
    /// </summary>
    internal static Task ApplyPowerAsync(Type powerType, Creature target, decimal amount,
                                         Creature applier, CardModel? source)
    {
        try
        {
            if (powerType == null || target == null || amount <= 0m) return Task.CompletedTask;
            var apply = typeof(MegaCrit.Sts2.Core.Commands.PowerCmd)
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(x => x.Name == "Apply" && x.IsGenericMethodDefinition
                                     && x.GetParameters().Length == 6
                                                                          && x.GetParameters()[1].ParameterType == typeof(Creature));
            if (apply == null) return Task.CompletedTask;
            return apply.MakeGenericMethod(powerType)
                .Invoke(null, new object?[]
                {
                    new ThrowingPlayerChoiceContext(), target, amount, applier, source, false,
                }) as Task ?? Task.CompletedTask;
        }
        catch (Exception e) { Logger.Log("[CharBoss] 施加能力失败（" + powerType.Name + "）: " + e.Message); }
        return Task.CompletedTask;
    }

    /// <summary>她是不是在「平静」姿态里（给「不惧妖邪」/「如水」这类判断用）。</summary>
    internal static bool InCalmState(Player? owner) => HasPowerNamed(owner, "Calm", "CalmStance");
    /// <summary>她现在是不是在神格里（A: `Divinity` / B: `DivinityStance`）。</summary>
    internal static bool InDivinity(Player? owner)
    {
        // ★ v0.2.707：B 版的姿态**不是生物身上的 power**（见 `StanceMultiplierFromModel`），
        //   所以还要问一下姿态模型，否则日志永远报「神格里=False」（哪怕真的进去了）。
        if (HasPowerNamed(owner, "Divinity", "DivinityStance")) return true;
        return StanceModelName(owner).Contains("Divinity");
    }

    /// <summary>她现在是不是在「**平静**」姿态里（A: `Calm` / B: `CalmStance`）。
    ///
    /// 用途（用户 2026-11-16：「第四回合的欺瞒现实和平安即使没进神格也被打了」）：
    /// 观者原版规则里**离开平静 = +2 能量**，而剧本第 4 / 11 回合的「化拳为空」正是「退出当前姿态」
    /// ⇒ 那两张「有费就打」靠的就是这一笔。规划期得先知道她此刻在不在平静，才能算准预算。
    ///
    /// ⚠ v0.2.709：B 版的姿态**不是生物身上的 power**（见 `StanceMultiplierFromModel`），
    ///   只查 power 会永远返回 false —— 日志里 `虔信：真言满 10 ⇒ ① 退出姿态（本来在平静=False）`
    ///   而同一秒的姿态模型明明是 `CalmStance`，就是这么来的。现在再问一次姿态模型。
    /// </summary>
    internal static bool InCalm(Player? owner)
    {
        if (HasPowerNamed(owner, "Calm", "CalmStance")) return true;
        return StanceModelName(owner).Contains("Calm");
    }

    /// <summary>
    /// **本场战斗累计获得的真言层数**（A 版：`WatcherStatePower.TotalMantraGainedThisCombat`）。
    ///
    /// 用途：观者「光辉」的真实伤害 = 牌面 + 这个数（反编译 `WatcherBrilliance.OnPlay` =
    /// `DamageCmd.Attack(DynamicVars.Damage.BaseValue + GetTotalMantraGained(owner))`，
    /// 而 `GetTotalMantraGained` 就是读这个属性）。用户 2026-11-16：
    /// 「光辉的伤害预览似乎没有计算虔信、手摇鼓和她特殊效果…导致的她在回合开始时的真言变化」。
    ///
    /// ⚠ 注意它是「**累计获得**」：她特殊效果每回合扣掉的那 5 层**不会**让它变小
    ///   （我们的 `SetMantra` 只改 `Mantra` power 自己的层数）。
    /// ⚠ B 版（lamali）的光辉走 `WithCalculatedDamage(12, MantraGainedThisCombat, …)`，
    ///   那个数**已经在牌面的 `DynamicVars.Damage` 里**了 ⇒ 调用方不要再加一遍（见 BossIntentBuilder 的 gate）。
    /// </summary>
    internal static int TotalMantraGained(Player? owner)
    {
        try
        {
            var powers = owner?.Creature?.Powers;
            if (powers == null) return 0;
            for (int i = 0; i < powers.Count; i++)
            {
                var p = powers[i];
                if (p == null) continue;
                string name;
                try { name = p.GetType().Name; } catch { continue; }
                if (name != "WatcherStatePower" && name != "WatcherState") continue;
                var prop = p.GetType().GetProperty("TotalMantraGainedThisCombat",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                var value = prop?.GetValue(p);
                if (value is int gained) return Math.Max(0, gained);
            }
        }
        catch { /* 读不到当 0 */ }
        return 0;
    }

    /// <summary>
    /// 她身上有没有「**手摇鼓**」（`Damaru`：`AfterPlayerTurnStart` ⇒ +1 真言）。
    /// 我们替影子玩家广播那条钩子的时机是**她回合开始** ⇒ 计划（玩家回合）排的时候这笔还没到账，
    /// 意图要把它当「pending +1」先补上（用户 2026-11-16 点名的三条之一）。
    /// </summary>
    internal static bool HasDamaru(Player? owner)
    {
        try
        {
            var relics = owner?.Relics;
            for (int i = 0; i < (relics?.Count ?? 0); i++)
            {
                string? entry = null;
                try { entry = relics![i]?.Id.Entry; } catch { continue; }
                if (string.IsNullOrEmpty(entry)) continue;
                if (entry.Replace("-", "").Replace("_", "")
                        .Equals("DAMARU", StringComparison.OrdinalIgnoreCase)) return true;
            }
        }
        catch { /* 忽略 */ }
        return false;
    }

    private static int PowerAmount(Player? owner, params string[] names)
    {
        try
        {
            var powers = owner?.Creature?.Powers;
            if (powers == null) return 0;
            for (int i = 0; i < powers.Count; i++)
            {
                var p = powers[i];
                if (p == null) continue;
                string name;
                try { name = p.GetType().Name; } catch { continue; }
                foreach (var n in names)
                    if (string.Equals(name, n, StringComparison.Ordinal)) return (int)Math.Round((double)p.Amount);
            }
        }
        catch { /* 忽略 */ }
        return 0;
    }

    private static bool HasPowerNamed(Player? owner, params string[] names)
    {
        try
        {
            var powers = owner?.Creature?.Powers;
            if (powers == null) return false;
            for (int i = 0; i < powers.Count; i++)
            {
                var p = powers[i];
                if (p == null) continue;
                string name;
                try { name = p.GetType().Name; } catch { continue; }
                foreach (var n in names)
                    if (string.Equals(name, n, StringComparison.Ordinal)) return true;
            }
        }
        catch { /* 忽略 */ }
        return false;
    }

    private static MethodInfo? Method(string typeFullName, string methodName)
        => Overload(typeFullName, methodName, null);

    private static int _overloadLogs;

    /// <summary>
    /// 按**参数类型**精确取重载（拿不到就按名字兜底，再有歧义就自己挑）。
    ///
    /// ★ v0.2.709（用户 2026-11-16 第六轮：「她已经处在神格时，再积累 10 层真言，不会像特殊效果里
    ///   说的一样先退出姿态然后再进入神格（这会导致没有成功加费）」）—— **真凶不是策略，是这个工具**：
    ///   B 版（lamali）`Watcher.Code.Commands.StanceCmd` 里
    ///   `EnterDivinity` / `ExitStance` / `EnterCalm` **各有两份重载**
    ///   （`(ctx, Player, CardModel?)` 与 `(ctx, IEnumerable&lt;Player&gt;, CardModel?)`），
    ///   而旧写法 `t.GetMethod(name, flags)` 遇到歧义会抛 `AmbiguousMatchException`，
    ///   被本函数的 catch 吞掉 ⇒ 返回 null ⇒ 调用点 `m?.Invoke(...)` **静默什么都不做**。
    ///
    ///   日志铁证（三次「之后」都等于「之前」，说明这一步从来没执行过）：
    ///   <code>
    ///   进神格：StanceCmd.EnterDivinity 之后姿态模型 = NoStance      （她本来是 NoStance）
    ///   进神格：StanceCmd.EnterDivinity 之后姿态模型 = CalmStance    （她本来是平静）
    ///   进神格：StanceCmd.EnterDivinity 之后姿态模型 = DivinityStance（她本来就在神格）
    ///   </code>
    ///   ⇒ 于是「退姿态」从来没退过（平静的 +2 也一次都没给过），「进神格」全靠 v0.2.707 的
    ///   silent 强制兜底 + v0.2.708 的补跑 OnEnterStance 才勉强成立。
    /// </summary>
    private static MethodInfo? Overload(string typeFullName, string methodName, Type[]? parameterTypes)
    {
        try
        {
            foreach (var asm in new[] { _preferred, _secondary })
            {
                if (asm == null) continue;
                var t = asm.GetType(typeFullName, false);
                if (t == null) continue;

                if (parameterTypes != null)
                {
                    try
                    {
                        var exact = t.GetMethod(methodName,
                            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                            null, parameterTypes, null);
                        if (exact != null) return exact;
                    }
                    catch { /* 往下走 */ }
                }

                MethodInfo? byName = null;
                try
                {
                    byName = t.GetMethod(methodName,
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                }
                catch { byName = null; }
                if (byName != null) return byName;

                // 还是有歧义 ⇒ 自己挑：两份实现的姿态命令都首选「第二个参数是 Player」那一条
                MethodInfo? best = null;
                try
                {
                    foreach (var cand in t.GetMethods(
                                 BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                    {
                        if (!string.Equals(cand.Name, methodName, StringComparison.Ordinal)) continue;
                        var ps = cand.GetParameters();
                        if (ps.Length < 2) continue;
                        if (ps[1].ParameterType != typeof(Player)) continue;
                        if (best == null || ps.Length < best.GetParameters().Length) best = cand;
                    }
                }
                catch { /* 忽略 */ }
                if (best != null)
                {
                    if (_overloadLogs++ < 8)
                    {
                        var names = new List<string>();
                        foreach (var p in best.GetParameters()) names.Add(p.ParameterType.Name);
                        Logger.Log($"[CharBoss] 反射消歧：{t.Name}.{methodName} 有多个重载 ⇒ 取 " +
                                   $"({string.Join(", ", names)})");
                    }
                    return best;
                }
            }
        }
        catch { /* 忽略 */ }
        return null;
    }

    /// <summary>姿态模型类型名 → 中文说明（只给日志用）。</summary>
    private static string Describe(string stanceName)
    {
        if (string.IsNullOrEmpty(stanceName)) return "（拿不到）";
        return stanceName switch
        {
            "NoStance" => "无姿态",
            "CalmStance" => "平静",
            "WrathStance" => "愤怒",
            "DivinityStance" => "神格",
            _ => stanceName,
        };
    }
}
#endif