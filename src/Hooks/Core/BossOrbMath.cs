using System;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Orbs;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Orbs;
using MegaCrit.Sts2.Core.Models.Powers;

namespace CharacterBosses.Core;

/// <summary>
/// 充能球的数值换算（纯数据，不碰节点）。两处用到：
///   1. 手牌栏里「这张牌会激发多少伤害」的括号数字（<see cref="EvokeDamageFor"/>）；
///   2. 等离子球在**回合开始**给的能量（<see cref="PlasmaEnergyFor"/>）。
///      原版走 <c>OrbQueue.AfterTurnStart</c>，而 Boss 的计划在玩家回合就得定死（意图必须精确），
///      所以只能提前把这笔能量算进本回合的能量预算，而不是等出牌时才加。
///
/// 激发伤害的判定按原版各球的 <c>Evoke</c> 实现来：
///   闪电/黑暗/玻璃 → 伤害；冰霜 → 格挡；等离子 → 能量。后两种不算进攻击伤害。
/// </summary>
public static class BossOrbMath
{
    /// <summary>
    /// 这颗球被激发时的伤害（**已剔除集中加成**）；不造成伤害的球返回 0。
    ///
    /// 为什么要剔除：<c>orb.EvokeVal</c> 是 <c>ModifyOrbValue(基础值)</c>，而 <c>FocusPower.ModifyOrbValue</c>
    /// 会把集中加进去。这个数字现在被喂给原版攻击意图节点，而意图节点自己还会跑一次
    /// <c>Hook.ModifyDamage</c>（dealer = Boss），于是力量又加一遍 ——
    /// 因为本 mod 的力量与集中恒等，表现就是「激发意图被力量和集中双重加成」。
    /// 这里减去集中、让意图节点只加力量，数值正好还原成原版（力量 == 集中）。
    /// </summary>
    public static int EvokeDamageOf(OrbModel? orb, int focus)
    {
        if (orb == null) return 0;
        if (orb is FrostOrb || orb is PlasmaOrb) return 0;
        try
        {
            // ★ 黑暗球例外（2026-11-04 反编译核对）：`DarkOrb.EvokeVal => _evokeVal`（字段，默认 6），
            //   **不走** `ModifyOrbValue` ⇒ 它的激发伤害里**没有集中**，不该减。
            //   （它会随每次回合结束 `_evokeVal += PassiveVal` 而涨，那时才把集中带进来。）
            //   闪电/玻璃球的 `EvokeVal` 才是 `ModifyOrbValue(基础值)`，要减掉集中。
            if (orb is DarkOrb) return Math.Max(0, (int)Math.Round(orb.EvokeVal));
            return Math.Max(0, (int)Math.Round(orb.EvokeVal) - focus);
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] evoke value failed: " + e.Message);
            return 0;
        }
    }

    /// <summary>Boss 身上当前的集中层数（用来从球数值里剔除它）。</summary>
    public static int FocusOf(Creature? creature)
    {
        if (creature == null) return 0;
        try { return creature.GetPower<FocusPower>()?.Amount ?? 0; }
        catch { return 0; }
    }

    /// <summary>
    /// 每颗球被激发几次。原版这两张是「同一颗球连续激发两次」（次数只写在卡牌代码里，数据里没有）：
    /// 双重释放对队首球激发 2 次；碎片对所有球各激发 2 次。
    /// 其余牌看 Repeat 变量（四重释放 = 4 次），再没有就当 1 次。
    /// </summary>
    public static int EvokesPerOrb(CardModel card)
    {
        if (card is Dualcast || card is Shatter) return 2;
        try
        {
            int repeat = card.DynamicVars.Repeat.IntValue;
            if (repeat > 0) return repeat;
        }
        catch { /* 没这个变量就是 1 次 */ }
        return 1;
    }

    /// <summary>
    /// 「这张牌会生成什么球」——原版数据里没有，只能按卡牌类写死（逐个反编译 `Channel&lt;XOrb&gt;` 核对过）。
    /// </summary>
    private static readonly System.Collections.Generic.Dictionary<string, (Type Orb, int Count)> Channels =
        new(StringComparer.Ordinal)
        {
            ["ZAP"] = (typeof(LightningOrb), 1),
            ["BALL_LIGHTNING"] = (typeof(LightningOrb), 1),
            ["COLD_SNAP"] = (typeof(FrostOrb), 1),
            ["COOLHEADED"] = (typeof(FrostOrb), 1),
            ["GLACIER"] = (typeof(FrostOrb), 2),        // OnPlay 里 for (i < 2)
            ["HIBERNATE"] = (typeof(FrostOrb), 2),      // 休眠：也是生成 2 个冰霜球（多人时替换冰川）
            ["CHILL"] = (typeof(FrostOrb), 1),
            ["DARKNESS"] = (typeof(DarkOrb), 1),
            ["NULL"] = (typeof(DarkOrb), 1),            // 空值：10 伤 + 虚弱 + 生成 1 个黑暗球（2026-10-27 补）
            ["FUSION"] = (typeof(PlasmaOrb), 1),
            ["METEOR_STRIKE"] = (typeof(PlasmaOrb), 3),
            ["ICE_LANCE"] = (typeof(FrostOrb), 3),      // DynamicVars.Repeat
            ["GLASSWORK"] = (typeof(GlassOrb), 1),        // 玻璃工艺：5 格挡 + 1 个玻璃充能球（v0.2.472 补，之前漏了 ⇒ 预览里没有它）
            // RAINBOW 见 BossOrbSim.Predict 的特殊分支（要生成 电+冰+暗 三种球，一个 (type,count) 表示不了）
        };

    /// <summary>这张牌会不会生成球、生成哪种、几个。</summary>
    public static bool TryChannels(CardModel? card, out Type orbType, out int count)
        => TryChannels(card, 0, out orbType, out count);

    /// <summary>
    /// 同上，但把「**这张牌出手时她还有多少能量**」传进来 —— X 费牌（暴风雨）生成的球数 = 那个能量。
    /// 用户 2026-11-05：「暴风雨会根据当时拥有能量的不同，生成不同数量的闪电充能球，你需要提前计算」。
    /// </summary>
    public static bool TryChannels(CardModel? card, int xEnergy, out Type orbType, out int count)
    {
        orbType = typeof(LightningOrb);
        count = 0;
        if (card == null) return false;
        string entry;
        try { entry = card.Id.Entry; } catch { return false; }

        // ★ v0.2.357（用户 2026-11-04：「伤害预览没有计算吞噬暗影生成的黑球」）：
        //   「吞噬暗影」（CONSUMING_SHADOW，`生成{N}个黑暗充能球；在你的回合结束时，激发你最左侧的充能球`）
        //   的球数在 `Repeat` 变量里（基础 2），不能写死在表里（以后调数值就跟不上了）。
        if (string.Equals(entry, "CONSUMING_SHADOW", StringComparison.Ordinal))
        {
            orbType = typeof(DarkOrb);
            count = 0;
            try { count = Math.Max(0, card.DynamicVars.Repeat.IntValue); } catch { count = 0; }
            if (count <= 0) count = 2;      // 读不到就退回卡面基础值
            return count > 0;
        }

        // ★ 暴风雨（TEMPEST）：X 费 ⇒ 生成 X 个闪电充能球（X = 出手时剩余能量）
        if (string.Equals(entry, "TEMPEST", StringComparison.Ordinal))
        {
            orbType = typeof(LightningOrb);
            count = Math.Max(0, xEnergy);
            try { if (card.IsUpgraded) count++; } catch { }   // 2026-11-13 dread: Tempest+ channels 1 more
            return count > 0;
        }

        if (!Channels.TryGetValue(entry, out var channel)) return false;
        orbType = channel.Orb;
        count = channel.Count;
        return true;
    }

    /// <summary>等离子球的被动是「回合开始 +1 能量」，把所有等离子球的这份能量加起来。</summary>
    public static int PlasmaEnergyFor(OrbQueue? queue)
    {
        if (queue == null || queue.Orbs.Count == 0) return 0;
        int energy = 0;
        try
        {
            foreach (var orb in queue.Orbs)
            {
                if (orb is PlasmaOrb) energy += Math.Max(0, (int)Math.Round(orb.PassiveVal));
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] plasma energy failed: " + e.Message);
        }
        return energy;
    }

    private static string SafeEntry(CardModel card)
    {
        try { return card.Id.Entry; } catch { return card.GetType().Name; }
    }
}
