using System;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Orbs;
using MegaCrit.Sts2.Core.Models.Powers;
using CharacterBosses.Monsters;

namespace CharacterBosses.Powers;

/// <summary>
/// 二层故障机器人**第二套设计「彩虹机」**的特殊效果「**彩虹**」（用户 2026-11-07）。
///
/// 用户原话：
/// > 「彩虹」：故障机器人每**生成 5 个充能球**，就获得 **1 点集中**。**黑暗充能球的被动不会触发**。
///
/// ⚠ 命名规则（用户 2026-11-05 明确）：**Boss 名**与**特殊效果名**是两件事 ——
///   「彩虹机」是 Boss 的名字（`DEFECT_BOSS.rainbow`），「彩虹」是效果的名字
///   （类名 `BossRainbowPower` / loc key `BOSS_RAINBOW_POWER`），互不推导。
///
/// 两半效果分在两地实现：
///   ① **每 5 个球 +1 集中** = 本类（覆写 `AfterOrbChanneled`，和她自己生成的球同一条钩子 ——
///      原版「节拍器」`Metronome` 用的就是它，所以两边计数口径一致）；
///   ② **黑暗充能球的被动不触发** = 见 `CharBossMonster.DarkPassiveSuppressed`（她这一幕恒为 true），
///      拦截点在 `Patches/DarkOrbPassiveSuppressPatch`。
/// </summary>
public sealed class BossRainbowPower : PowerModel
{
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Single;

    public override bool ShouldPlayVfx => false;

    /// <summary>每生成这么多颗球 → +1 集中（用户：5）。</summary>
    private const int OrbsPerFocus = 5;

    private int _orbs;

    /// <summary>已经生成了几颗球（状态栏显示用：0~5 循环）。</summary>
    public override int DisplayAmount
    {
        get { try { return _orbs % OrbsPerFocus; } catch { return 0; } }
    }

    private static int _logs;

    /// <summary>
    /// 她生成了一颗充能球 ⇒ 计数；满 <see cref="OrbsPerFocus"/> 就 +1 集中并把计数清零（每 5 颗结算一次）。
    ///
    /// ⚠ 只认**她自己**生成的球：玩家自己的球、别的 Boss 的球一律不算。
    /// </summary>
    public override async Task AfterOrbChanneled(PlayerChoiceContext choiceContext, Player player, OrbModel orb)
    {
        try
        {
            var shadow = Owner?.Player;
            if (shadow == null || player == null) return;
            if (!ReferenceEquals(player, shadow)) return;

            _orbs++;
            if (_orbs % OrbsPerFocus != 0) return;

            var creature = Owner;
            if (creature == null) return;

            await PowerCmd.Apply<FocusPower>(choiceContext, creature, 1m, null, null);
            if (_logs++ < 20)
            {
                Logger.Log($"[CharBoss] 彩虹：累计生成第 {_orbs} 颗充能球 ⇒ 故障机器人 +1 集中" +
                           $"（现在 {SafeFocus(creature)}）");
            }
            try { InvokeDisplayAmountChanged(); } catch { /* 忽略 */ }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 彩虹（每 5 球 +1 集中）失败: " + e.Message);
        }
    }

    private static string SafeFocus(MegaCrit.Sts2.Core.Entities.Creatures.Creature creature)
    {
        try
        {
            foreach (var power in creature.Powers)
            {
                if (power is FocusPower) return power.Amount.ToString();
            }
            return "0";
        }
        catch { return "?"; }
    }
}
