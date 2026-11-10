using CharacterBosses.Core;

namespace CharacterBosses.Monsters;

// 第一幕（密林 / 暗港） —— 把这一层的每个档位暴露成一个独立的怪物类（全套 30 个，见 BossKeys.All）。
// 类名 = MonsterId（ModelId.Entry 由类名 Slugify 而来），斗蛐蛐 / 控制台 / 录像按它点名。
// 逻辑全部继承自基类，这里只钉死「幕数」与「设计 Key」；
// 这些类不挂在任何 Encounter/Act 上 ⇒ 不进 ModelDb.Monsters，对正常流程与图鉴完全隐形。

/// <summary>奇巧猎（第 1 层）—— 设计 Key <c>SILENT_SLY</c>。</summary>
public sealed class SilentSlyBoss : SilentBoss
{
    public SilentSlyBoss() { SpawnVariantKey = "SILENT_SLY"; }

    public override int? TierAct => 0;
    public override string? PinnedVariantKey => "SILENT_SLY";
}

/// <summary>蛇咬猎（第 1 层）—— 设计 Key <c>SILENT_SNAKEBITE</c>。</summary>
public sealed class SilentSnakebiteBoss : SilentBoss
{
    public SilentSnakebiteBoss() { SpawnVariantKey = "SILENT_SNAKEBITE"; }

    public override int? TierAct => 0;
    public override string? PinnedVariantKey => "SILENT_SNAKEBITE";
}

/// <summary>黑球机（第 1 层）—— 设计 Key <c>DEFECT_DARK</c>。</summary>
public sealed class DefectDarkBoss : DefectBoss
{
    public DefectDarkBoss() { SpawnVariantKey = "DEFECT_DARK"; }

    public override int? TierAct => 0;
    public override string? PinnedVariantKey => "DEFECT_DARK";
}

/// <summary>灾厄骨（第 1 层）—— 设计 Key <c>NECROBINDER_DOOM</c>。</summary>
public sealed class NecrobinderDoomBoss : NecrobinderBoss
{
    public NecrobinderDoomBoss() { SpawnVariantKey = "NECROBINDER_DOOM"; }

    public override int? TierAct => 0;
    public override string? PinnedVariantKey => "NECROBINDER_DOOM";
}

/// <summary>血肉骨（第 1 层）—— 设计 Key <c>NECROBINDER_FLESH</c>（用户 2026-11-10）。</summary>
public sealed class NecrobinderFleshBoss : NecrobinderBoss
{
    public NecrobinderFleshBoss() { SpawnVariantKey = "NECROBINDER_FLESH"; }

    public override int? TierAct => 0;
    public override string? PinnedVariantKey => "NECROBINDER_FLESH";
}

/// <summary>碎屑储君（第 1 层）—— 设计 Key <c>REGENT_DEBRIS</c>。</summary>
public sealed class RegentDebrisBoss : RegentBoss
{
    public RegentDebrisBoss() { SpawnVariantKey = "REGENT_DEBRIS"; }

    public override int? TierAct => 0;
    public override string? PinnedVariantKey => "REGENT_DEBRIS";
}

/// <summary>轰击储君（第 1 层）—— 设计 Key <c>REGENT_BOMBARD</c>。</summary>
public sealed class RegentBombardBoss : RegentBoss
{
    public RegentBombardBoss() { SpawnVariantKey = "REGENT_BOMBARD"; }

    public override int? TierAct => 0;
    public override string? PinnedVariantKey => "REGENT_BOMBARD";
}

/// <summary>电球机（第 1 层）—— 设计 Key <c>DEFECT_THUNDER</c>。</summary>
public sealed class DefectThunderBoss : DefectBoss
{
    public DefectThunderBoss() { SpawnVariantKey = "DEFECT_THUNDER"; }

    public override int? TierAct => 0;
    public override string? PinnedVariantKey => "DEFECT_THUNDER";
}

/// <summary>愤怒战（第 1 层）—— 设计 Key <c>IRONCLAD_ANGER</c>。</summary>
public sealed class IroncladAngerBoss : IroncladBoss
{
    public IroncladAngerBoss() { SpawnVariantKey = "IRONCLAD_ANGER"; }

    public override int? TierAct => 0;
    public override string? PinnedVariantKey => "IRONCLAD_ANGER";
}

/// <summary>打击战（第 1 层）—— 设计 Key <c>IRONCLAD_STRIKE</c>。</summary>
public sealed class IroncladStrikeBoss : IroncladBoss
{
    public IroncladStrikeBoss() { SpawnVariantKey = "IRONCLAD_STRIKE"; }

    public override int? TierAct => 0;
    public override string? PinnedVariantKey => "IRONCLAD_STRIKE";
}
