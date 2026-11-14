using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using CharacterBosses.Core;

namespace CharacterBosses.Relics;

/// <summary>
/// 涅奥的祝福（先古遗物）—— 角色 Boss 都随身携带的「解释高血量 / 限制强度」遗物。
///
/// 效果（与一代略有不同，按用户设计）：
///   · 拾起时：获得 <see cref="PickupMaxHpBonus"/>（120）点最大生命值，**失去所有药水栏位**；
///   · 每回合开始：**少抽 2 张牌、少 1 点能量**（这就是 Boss 高血量的代价）；
///   · 每进入新一幕：再获得 <see cref="PerActMaxHpBonus"/>（180）点最大生命值；
///   · 战斗开始时：每个 Boss 的特殊效果 —— **目前是占位**，等设计完再填。
///
/// ★ 用户 2026-10-31：「一层 Boss 胜率显著偏低（最低 43%）」⇒ 把血线调成
///   **拾起 120 / 每进一幕 180**，于是：
///     一层 = 基础 + 120（比原来的 150 **−30**）
///     二层 = 基础 + 120 + 180 = 基础 + 300（**不变**，原来 150+150）
///     三层 = 基础 + 120 + 180 + 180 = 基础 + 480（比原来的 450 **+30**）
///
/// 出现规则：**默认不出现在涅奥选项里**；在 mod 目录放 <c>charbosses_neow_relic.txt</c> 才放开
/// （见 <see cref="NeowBlessingToggle"/>）。角色 Boss 的携带不受该开关限制。
/// </summary>
public sealed class NeowBlessing : RelicModel
{
    /// <summary>拾起时加的最大生命（用户 2026-10-31：150 → 120）。</summary>
    public const int PickupMaxHpBonus = 120;

    /// <summary>每进入新的一幕加的最大生命（用户 2026-10-31：150 → 180）。</summary>
    public const int PerActMaxHpBonus = 180;

    /// <summary>⚠ 兼容旧代码：`MaxHpBonus` 现在等于「拾起时」那一档。</summary>
    public const int MaxHpBonus = PickupMaxHpBonus;

    public const int DrawPenalty = 2;
    public const int EnergyPenalty = 1;

    public override RelicRarity Rarity => RelicRarity.Ancient;

    /// <summary>
    /// 复用原版涅奥遗物的贴图（不额外造美术资源）。
    /// 用户 2026-10-27：「涅奥的祝福」的图标换成**涅奥的牺牲**（`NEOWS_SACRIFICE` / `neows_sacrifice`），
    /// 不再用涅奥的护符（`NEOWS_TALISMAN` / `neows_talisman`）。
    /// 原版这两张都在包里：`res://images/atlases/relic_atlas.sprites/neows_sacrifice.tres` +
    /// `res://images/relics/neows_sacrifice.png`（轮廓图 `relic_outline_atlas.sprites/neows_sacrifice.tres` 也齐）。
    /// </summary>
    protected override string IconBaseName => "neows_sacrifice";

    protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
    {
        new MaxHpVar(PickupMaxHpBonus),
    };

    // ── 出现条件：默认关闭 ──
    // ⚠ 0.113.0 起 `RelicModel.IsAllowed` 改成非 virtual，扩展点变成 `protected virtual IsAllowedInternal`
    //   （编译实测 CS0506）⇒ 两个版本各覆盖各的。
#if STS2_107
    public override bool IsAllowed(IRunState runState) => NeowBlessingToggle.Enabled;
#else
    protected override bool IsAllowedInternal(IRunState runState) => NeowBlessingToggle.Enabled;
#endif

    public override bool IsAllowedAtNeow(Player player) => NeowBlessingToggle.Enabled;

    /// <summary>拾起时：+120 最大生命，清空药水栏位。</summary>
    public override async Task AfterObtained()
    {
        try
        {
            Flash();
            await CreatureCmd.GainMaxHp(Owner.Creature, PickupMaxHpBonus);
            if (Owner.MaxPotionCount > 0)
            {
                Owner.SubtractFromMaxPotionCount(Owner.MaxPotionCount);
            }
            Logger.Log($"[CharBoss] 涅奥的祝福：已拾起，最大生命 +{PickupMaxHpBonus}，药水栏位清零");
        }
        catch (System.Exception e)
        {
            Logger.Log("[CharBoss] 涅奥的祝福 拾起失败: " + e);
        }
    }

    /// <summary>每回合少抽 2 张。</summary>
    public override decimal ModifyHandDraw(Player player, decimal count)
        => ReferenceEquals(player, Owner) ? System.Math.Max(0m, count - DrawPenalty) : count;

    /// <summary>每回合少 1 点能量。</summary>
    public override decimal ModifyMaxEnergy(Player player, decimal amount)
        => ReferenceEquals(player, Owner) ? System.Math.Max(0m, amount - EnergyPenalty) : amount;

    /// <summary>每进入新一幕：再 +180 最大生命。</summary>
    public override async Task AfterActEntered()
    {
        try
        {
            if (Owner?.Creature == null || !Owner.Creature.IsAlive) return;
            Flash();
            await CreatureCmd.GainMaxHp(Owner.Creature, PerActMaxHpBonus);
            Logger.Log($"[CharBoss] 涅奥的祝福：进入新一幕，最大生命 +{PerActMaxHpBonus}");
        }
        catch (System.Exception e)
        {
            Logger.Log("[CharBoss] 涅奥的祝福 进幕加成失败: " + e);
        }
    }

    /// <summary>
    /// 战斗开始的特殊效果 —— **占位**：五个 Boss 各自的 buff 还没设计，等用户给完再填。
    /// 保留这个覆写是为了把挂点固定下来。
    /// </summary>
    public override Task BeforeCombatStart()
    {
        Logger.Log("[CharBoss] 涅奥的祝福：战斗开始特殊效果（占位，尚未实现）");
        return Task.CompletedTask;
    }
}
