using CharacterBosses.Core;

namespace CharacterBosses.Monsters;

// 5 个原版角色各一个 Boss 怪物类。
// 类名会决定 ModelId.Entry（IroncladBoss → IRONCLAD_BOSS），本地化 key 与之一一对应。
// 具体的 HP / 能量 / 手牌数 / 卡组 / 遗物 全部来自 BossDefinition，这里不写任何数值。

/// <summary>铁甲战士 Boss（易伤流样板，三档已设计）。</summary>
public sealed class IroncladBoss : CharBossMonster
{
    public override BossDefinition Def => BossDefs.Ironclad;
}

/// <summary>静默猎手 Boss（卡组待设计，暂用起始卡组占位）。</summary>
public sealed class SilentBoss : CharBossMonster
{
    public override BossDefinition Def => BossDefs.Silent;
}

/// <summary>故障机器人 Boss（卡组待设计，暂用起始卡组占位）。</summary>
public sealed class DefectBoss : CharBossMonster
{
    public override BossDefinition Def => BossDefs.Defect;
}

/// <summary>亡灵契约师 Boss（卡组待设计，暂用起始卡组占位）。</summary>
public sealed class NecrobinderBoss : CharBossMonster
{
    public override BossDefinition Def => BossDefs.Necrobinder;
}

/// <summary>储君 Boss（卡组待设计，暂用起始卡组占位）。</summary>
public sealed class RegentBoss : CharBossMonster
{
    public override BossDefinition Def => BossDefs.Regent;
}
