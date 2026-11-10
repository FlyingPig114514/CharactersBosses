using CharacterBosses.Core;

namespace CharacterBosses.Monsters;

// 第二幕（巢穴） —— 把这一层的每个档位暴露成一个独立的怪物类（全套 29 个，见 BossKeys.All）。
// 类名 = MonsterId（ModelId.Entry 由类名 Slugify 而来），斗蛐蛐 / 控制台 / 录像按它点名。
// 逻辑全部继承自基类，这里只钉死「幕数」与「设计 Key」；
// 这些类不挂在任何 Encounter/Act 上 ⇒ 不进 ModelDb.Monsters，对正常流程与图鉴完全隐形。

/// <summary>涂毒猎（第 2 层）—— 设计 Key <c>SILENT_ENVENOM</c>。</summary>
public sealed class SilentEnvenomBoss : SilentBoss
{
    public SilentEnvenomBoss() { SpawnVariantKey = "SILENT_ENVENOM"; }

    public override int? TierAct => 1;
    public override string? PinnedVariantKey => "SILENT_ENVENOM";
}

/// <summary>蛇眼猎（第 2 层）—— 设计 Key <c>SILENT_SNECKO</c>。</summary>
public sealed class SilentSneckoBoss : SilentBoss
{
    public SilentSneckoBoss() { SpawnVariantKey = "SILENT_SNECKO"; }

    public override int? TierAct => 1;
    public override string? PinnedVariantKey => "SILENT_SNECKO";
}

/// <summary>物理机（第 2 层）—— 设计 Key <c>DEFECT_CLAW</c>。</summary>
public sealed class DefectClawBoss : DefectBoss
{
    public DefectClawBoss() { SpawnVariantKey = "DEFECT_CLAW"; }

    public override int? TierAct => 1;
    public override string? PinnedVariantKey => "DEFECT_CLAW";
}

/// <summary>彩虹机（第 2 层）—— 设计 Key <c>DEFECT_RAINBOW</c>。</summary>
public sealed class DefectRainbowBoss : DefectBoss
{
    public DefectRainbowBoss() { SpawnVariantKey = "DEFECT_RAINBOW"; }

    public override int? TierAct => 1;
    public override string? PinnedVariantKey => "DEFECT_RAINBOW";
}

/// <summary>灵魂骨（第 2 层）—— 设计 Key <c>NECROBINDER_SOUL</c>。</summary>
public sealed class NecrobinderSoulBoss : NecrobinderBoss
{
    public NecrobinderSoulBoss() { SpawnVariantKey = "NECROBINDER_SOUL"; }

    public override int? TierAct => 1;
    public override string? PinnedVariantKey => "NECROBINDER_SOUL";
}

/// <summary>虚无骨（第 2 层）—— 设计 Key <c>NECROBINDER_ETHEREAL</c>（用户 2026-11-09）。</summary>
public sealed class NecrobinderEtherealBoss : NecrobinderBoss
{
    public NecrobinderEtherealBoss() { SpawnVariantKey = "NECROBINDER_ETHEREAL"; }

    public override int? TierAct => 1;
    public override string? PinnedVariantKey => "NECROBINDER_ETHEREAL";
}

/// <summary>铸剑储君（第 2 层）—— 设计 Key <c>REGENT_FORGE</c>。</summary>
public sealed class RegentForgeBoss : RegentBoss
{
    public RegentForgeBoss() { SpawnVariantKey = "REGENT_FORGE"; }

    public override int? TierAct => 1;
    public override string? PinnedVariantKey => "REGENT_FORGE";
}

/// <summary>王储（第 2 层）—— 设计 Key <c>REGENT_KING</c>。</summary>
public sealed class RegentKingBoss : RegentBoss
{
    public RegentKingBoss() { SpawnVariantKey = "REGENT_KING"; }

    public override int? TierAct => 1;
    public override string? PinnedVariantKey => "REGENT_KING";
}

/// <summary>放血战（第 2 层）—— 设计 Key <c>IRONCLAD_BLOOD</c>。</summary>
public sealed class IroncladBloodBoss : IroncladBoss
{
    public IroncladBloodBoss() { SpawnVariantKey = "IRONCLAD_BLOOD"; }

    public override int? TierAct => 1;
    public override string? PinnedVariantKey => "IRONCLAD_BLOOD";
}

/// <summary>杂耍战（第 2 层）—— 设计 Key <c>IRONCLAD_JUGGLER</c>。</summary>
public sealed class IroncladJugglerBoss : IroncladBoss
{
    public IroncladJugglerBoss() { SpawnVariantKey = "IRONCLAD_JUGGLER"; }

    public override int? TierAct => 1;
    public override string? PinnedVariantKey => "IRONCLAD_JUGGLER";
}

/// <summary>仆从储君（第 2 层）—— 设计 Key <c>REGENT_MINION</c>。</summary>
public sealed class RegentMinionBoss : RegentBoss
{
    public RegentMinionBoss() { SpawnVariantKey = "REGENT_MINION"; }

    public override int? TierAct => 1;
    public override string? PinnedVariantKey => "REGENT_MINION";
}
