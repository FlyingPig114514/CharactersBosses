using CharacterBosses.Core;

namespace CharacterBosses.Monsters;

// 5 个原版角色各一个 Boss 怪物类。
// 类名会决定 ModelId.Entry（IroncladBoss → IRONCLAD_BOSS），本地化 key 与之一一对应。
// 具体的 HP / 能量 / 手牌数 / 卡组 / 遗物 全部来自 BossDefinition，这里不写任何数值。

/// <summary>铁甲战士 Boss（易伤流样板，三档已设计）。</summary>
public class IroncladBoss : CharBossMonster
{
    public override BossDefinition DefSource => BossDefs.Ironclad;

    /// <summary>
    /// ★ 2026-11-10（用户需求）：铁甲战士系列 boss 的**死亡音效换成一代那三句语音**
    /// （`STS_VO_Ironclad_2a/2b/2c` 随机一句）。
    ///
    /// 反编译 `NCreature.StartDeathAnim`：
    /// <code>
    /// if (_spineAnimator != null) {
    ///     if (monster != null &amp;&amp; monster.HasDeathSfx) SfxCmd.PlayDeath(Entity.Monster);
    ///     ...
    /// }
    /// </code>
    /// 所以把 `HasDeathSfx` 覆写成 false ⇒ **原版那条 FMOD 事件彻底不播**
    /// （默认值是 `event:/sfx/enemy/enemy_attacks/{entry}/{entry}_die`，我们没做 bank，本来也响不了）。
    /// 我们自己的语音由 `Patches.IroncladBossDeathVoicePatch`（挂在同一个 `StartDeathAnim` 的后置）播，
    /// 两件事分开做，互不影响。
    /// </summary>
    public override bool HasDeathSfx => false;
}

/// <summary>静默猎手 Boss（卡组待设计，暂用起始卡组占位）。</summary>
public class SilentBoss : CharBossMonster
{
    public override BossDefinition DefSource => BossDefs.Silent;
}

/// <summary>故障机器人 Boss（卡组待设计，暂用起始卡组占位）。</summary>
public class DefectBoss : CharBossMonster
{
    public override BossDefinition DefSource => BossDefs.Defect;
}

/// <summary>亡灵契约师 Boss（卡组待设计，暂用起始卡组占位）。</summary>
public class NecrobinderBoss : CharBossMonster
{
    public override BossDefinition DefSource => BossDefs.Necrobinder;
}

/// <summary>储君 Boss（卡组待设计，暂用起始卡组占位）。</summary>
public class RegentBoss : CharBossMonster
{
    public override BossDefinition DefSource => BossDefs.Regent;
}

#if STS2_113
/// <summary>愚者 Boss（三层 1 号「塔罗愚」，用户 2026-11-14）。0.113 才有愚者角色 ⇒ 只在 111 构建里。
/// </summary>
public class FoolBoss : CharBossMonster
{
    public override BossDefinition DefSource => BossDefs.Fool;
}
#endif
#if STS2_111
/// <summary>**观者 Boss 占位**（用户 2026-11-15，还没有设计）。只有装了观者 mod 时才会出现在第三幕候选里。</summary>
public class WatcherBoss : CharBossMonster
{
    public override BossDefinition DefSource => BossDefs.Watcher;
}
#endif

#if STS2_111
/// <summary>
/// 三层 2 号「**Ω观**」（设计 Key <c>WATCHER_OMEGA</c>，用户 2026-11-16）。
///
/// 与「神格观」是**同一个观者角色**的第二套设计 ⇒ 走 `BossDefs.Watcher` 的 `ExtraVariants[2]`
/// （剧本 `WatcherOmegaScript`、遗物七件、特殊效果「斋戒」）。`PinnedVariantKey` 保证
/// 「控制台 `fight WATCHER_OMEGA_BOSS_ENCOUNTER`」永远打这一套，不会退化成按种子 50/50。
/// </summary>
public sealed class WatcherOmegaBoss : WatcherBoss
{
    public WatcherOmegaBoss() { SpawnVariantKey = "WATCHER_OMEGA"; }

    public override int? TierAct => 2;

    public override string? PinnedVariantKey => "WATCHER_OMEGA";
}

/// <summary>
/// **二层 1 号「印牌观」**（设计 Key <c>WATCHER_CREATE</c>，用户 2026-11-16）。
///
/// 同一个观者角色的第三套设计 ⇒ 走 `BossDefs.Watcher` 的 `ExtraVariants[1]`
/// （剧本 `WatcherCreateScript`、遗物五件、特殊效果「操控现实」）。
/// 控制台：`fight WATCHER_CREATE_BOSS_ENCOUNTER`。
/// </summary>
public sealed class WatcherCreateBoss : WatcherBoss
{
    public WatcherCreateBoss() { SpawnVariantKey = "WATCHER_CREATE"; }

    public override int? TierAct => 1;      // 第二幕

    public override string? PinnedVariantKey => "WATCHER_CREATE";
}

/// <summary>
/// **二层 2 号「保留观」**（设计 Key <c>WATCHER_RETAIN</c>，用户 2026-11-17）。
///
/// 同一个观者角色的第四套设计 ⇒ 走 `BossDefs.Watcher` 的 `ExtraVariants[1]`（与印牌观同层，
/// 用 `BossLineups.WatcherRetainActive()` 决定谁上场）：剧本 `WatcherRetainScript`（9 回合，
/// 6~9 循环）、遗物五件、特殊效果「确立基础」（`BossEstablishmentPower`）。
/// 控制台：`fight WATCHER_RETAIN_BOSS_ENCOUNTER`。
/// </summary>
public sealed class WatcherRetainBoss : WatcherBoss
{
    public WatcherRetainBoss() { SpawnVariantKey = "WATCHER_RETAIN"; }

    public override int? TierAct => 1;      // 第二幕

    public override string? PinnedVariantKey => "WATCHER_RETAIN";
}

/// <summary>
/// **一层 1 号「预见观」**（设计 Key <c>WATCHER_FORESIGHT</c>，用户 2026-11-17）。
///
/// 同一个观者角色的**第五套设计** ⇒ 走 `BossDefs.Watcher` 的 `ExtraVariants[0]`（第一幕）：
/// 剧本 `WatcherForesightScript`（8 回合，6~8 循环）、遗物 至纯之水/涅奥的祝福/美琅脂/手里剑、
/// 特殊效果「通晓万物」（`Powers.BossOmnisciencePower`）。
/// 控制台：`fight WATCHER_FORESIGHT_BOSS_ENCOUNTER`。
/// </summary>
public sealed class WatcherForesightBoss : WatcherBoss
{
    public WatcherForesightBoss() { SpawnVariantKey = "WATCHER_FORESIGHT"; }

    public override int? TierAct => 0;      // 第一幕

    public override string? PinnedVariantKey => "WATCHER_FORESIGHT";
}

/// <summary>
/// **一层 2 号「疾风观」**（设计 Key <c>WATCHER_FLURRY</c> = 疾风连击 Flurry of Blows，用户 2026-11-23）。
///
/// 同一个观者角色的**第七套设计** ⇒ 走 `BossDefs.Watcher` 的 `ExtraVariants[0]`（与预见观同幕，
/// 靠 `BossLineups.WatcherFlurryActive()` 分辨这一场是谁）：剧本 `WatcherFlurryScript`
/// （9 回合，6~9 循环）、遗物 至纯之水/涅奥的祝福/手里剑/苦无、
/// 特殊效果「疾风连击」（`Powers.BossFlurryPower` ⇒ 给玩家叠「力竭」`Powers.FlurryDrainedPower`）。
/// 控制台：`fight WATCHER_FLURRY_BOSS_ENCOUNTER`。
/// </summary>
public sealed class WatcherFlurryBoss : WatcherBoss
{
    public WatcherFlurryBoss() { SpawnVariantKey = "WATCHER_FLURRY"; }

    public override int? TierAct => 0;      // 第一幕

    public override string? PinnedVariantKey => "WATCHER_FLURRY";
}

/// <summary>
/// **三层 3 号「红蓝观」**（设计 Key <c>WATCHER_RUSHDOWN</c> = 猛虎下山，用户 2026-11-18）。
///
/// 同一个观者角色的**第六套设计** ⇒ 走 `BossDefs.Watcher` 的 `ExtraVariants[2]`（第三幕）。
/// 剧本 `WatcherRushdownScript`（11 回合 + 第 12 回合起无限）、
/// 遗物 至纯之水 / 涅奥的祝福 / 泪滴吊坠盒 / 符文金字塔 / 音叉 / 布质果实 / 坚固钳子、
/// 特殊效果「节制」（`Powers.BossTemperancePower`）。
/// 控制台：`fight WATCHER_RUSHDOWN_BOSS_ENCOUNTER`。
/// </summary>
public sealed class WatcherRushdownBoss : WatcherBoss
{
    public WatcherRushdownBoss() { SpawnVariantKey = "WATCHER_RUSHDOWN"; }

    public override int? TierAct => 2;      // 三层

    public override string? PinnedVariantKey => "WATCHER_RUSHDOWN";
}
#endif
