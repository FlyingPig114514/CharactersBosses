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

#if STS2_111
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
#endif