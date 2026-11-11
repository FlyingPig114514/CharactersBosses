using CharacterBosses.Core;

namespace CharacterBosses.Monsters;

// 第三幕（荣耀） —— 把这一层的每个档位暴露成一个独立的怪物类（全套 29 个，见 BossKeys.All）。
// 类名 = MonsterId（ModelId.Entry 由类名 Slugify 而来），斗蛐蛐 / 控制台 / 录像按它点名。
// 逻辑全部继承自基类，这里只钉死「幕数」与「设计 Key」；
// 这些类不挂在任何 Encounter/Act 上 ⇒ 不进 ModelDb.Monsters，对正常流程与图鉴完全隐形。

/// <summary>刀猎（第 3 层）—— 设计 Key <c>SILENT_SHIV</c>。</summary>
public sealed class SilentShivBoss : SilentBoss
{
    public SilentShivBoss() { SpawnVariantKey = "SILENT_SHIV"; }

    public override int? TierAct => 2;
    public override string? PinnedVariantKey => "SILENT_SHIV";
}

/// <summary>炼药猎（第 3 层）—— 设计 Key <c>SILENT_BREWER</c>。</summary>
public sealed class SilentBrewerBoss : SilentBoss
{
    public SilentBrewerBoss() { SpawnVariantKey = "SILENT_BREWER"; }

    public override int? TierAct => 2;
    public override string? PinnedVariantKey => "SILENT_BREWER";
}

/// <summary>能力机（第 3 层）—— 设计 Key <c>DEFECT_AI</c>。</summary>
public sealed class DefectAiBoss : DefectBoss
{
    public DefectAiBoss() { SpawnVariantKey = "DEFECT_AI"; }

    public override int? TierAct => 2;
    public override string? PinnedVariantKey => "DEFECT_AI";
}

/// <summary>事件机（第 3 层）—— 设计 Key <c>DEFECT_EVENT</c>。</summary>
public sealed class DefectEventBoss : DefectBoss
{
    public DefectEventBoss() { SpawnVariantKey = "DEFECT_EVENT"; }

    public override int? TierAct => 2;
    public override string? PinnedVariantKey => "DEFECT_EVENT";
}

/// <summary>状态机（第 3 层）—— 设计 Key <c>DEFECT_STATUS</c>。</summary>
public sealed class DefectStatusBoss : DefectBoss
{
    public DefectStatusBoss() { SpawnVariantKey = "DEFECT_STATUS"; }

    public override int? TierAct => 2;
    public override string? PinnedVariantKey => "DEFECT_STATUS";
}

/// <summary>根除骨（第 3 层）—— 设计 Key <c>NECROBINDER_ERADICATE</c>。</summary>
public sealed class NecrobinderEradicateBoss : NecrobinderBoss
{
    public NecrobinderEradicateBoss() { SpawnVariantKey = "NECROBINDER_ERADICATE"; }

    public override int? TierAct => 2;
    public override string? PinnedVariantKey => "NECROBINDER_ERADICATE";
}

/// <summary>养手骨（第 3 层）—— 设计 Key <c>NECROBINDER_OSTY</c>。</summary>
public sealed class NecrobinderOstyBoss : NecrobinderBoss
{
    public NecrobinderOstyBoss() { SpawnVariantKey = "NECROBINDER_OSTY"; }

    public override int? TierAct => 2;
    public override string? PinnedVariantKey => "NECROBINDER_OSTY";
}

/// <summary>无色储君（第 3 层）—— 设计 Key <c>REGENT_COLORLESS</c>。</summary>
public sealed class RegentColorlessBoss : RegentBoss
{
    public RegentColorlessBoss() { SpawnVariantKey = "REGENT_COLORLESS"; }

    public override int? TierAct => 2;
    public override string? PinnedVariantKey => "REGENT_COLORLESS";
}

/// <summary>辉星储君（第 3 层）—— 设计 Key <c>REGENT_STAR</c>。</summary>
public sealed class RegentStarBoss : RegentBoss
{
    public RegentStarBoss() { SpawnVariantKey = "REGENT_STAR"; }

    public override int? TierAct => 2;
    public override string? PinnedVariantKey => "REGENT_STAR";
}

/// <summary>抉择储君（第 3 层）—— 设计 Key <c>REGENT_CHOICE</c>。</summary>
public sealed class RegentChoiceBoss : RegentBoss
{
    public RegentChoiceBoss() { SpawnVariantKey = "REGENT_CHOICE"; }

    public override int? TierAct => 2;
    public override string? PinnedVariantKey => "REGENT_CHOICE";
}

/// <summary>易伤战（第 3 层）—— 设计 Key <c>IRONCLAD_VULN</c>。</summary>
public sealed class IroncladVulnBoss : IroncladBoss
{
    public IroncladVulnBoss() { SpawnVariantKey = "IRONCLAD_VULN"; }

    public override int? TierAct => 2;
    public override string? PinnedVariantKey => "IRONCLAD_VULN";
}

/// <summary>巨石战（第 3 层）—— 设计 Key <c>IRONCLAD_BOULDER</c>。</summary>
public sealed class IroncladBoulderBoss : IroncladBoss
{
    public IroncladBoulderBoss() { SpawnVariantKey = "IRONCLAD_BOULDER"; }

    public override int? TierAct => 2;
    public override string? PinnedVariantKey => "IRONCLAD_BOULDER";
}
