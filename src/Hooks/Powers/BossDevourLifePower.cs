using System;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;
using MegaCrit.Sts2.Core.Models.Powers;
using CharacterBosses.Core;

namespace CharacterBosses.Powers;

/// <summary>
/// 一层亡灵契约师 2 号「**血肉骨**」的特殊效果「**吞噬生命**」（用户 2026-11-10 设计）。
///
/// 用户原话：
/// > 特殊效果：吞噬生命：每当亡灵契约师给予负面效果时，她获得4点格挡。
///
/// 实现依据（反编译 `SleightOfFleshPower`，血肉戏法用的就是同一个钩子）：
/// <code>
/// public override Task AfterPowerAmountChanged(PlayerChoiceContext ctx, PowerModel power,
///                                              decimal amount, Creature? applier, CardModel? cardSource)
/// // 血肉戏法原文：
/// //   if (amount != 0 &amp;&amp; power.GetTypeForAmount(amount) == PowerType.Debuff
/// //       &amp;&amp; power.Owner.IsEnemy &amp;&amp; applier == Owner &amp;&amp; !(power is ITemporaryPower))
/// //       CreatureCmd.Damage(..., power.Owner, Amount, Unpowered, Owner)
/// </code>
/// 我们照同一把尺子，但把「目标是不是敌人」换成「目标是不是**真人玩家**」——
/// `Creature.IsEnemy` 是**绝对阵营**（`Side == CombatSide.Enemy`），而她的负面落在 Side=Player 的
/// 玩家身上，直接用原版那条件对她是永远 false（<c>SleightOfFleshPower</c> 因此需要单独接管，
/// 见 <c>Patches.SleightOfFleshBossPatch</c>）。
/// </summary>
public sealed class BossDevourLifePower : PowerModel
{
    // 特殊效果按 Buff 显示（和引雷针一样：挂在她身上的「设计效果」，不是掉到减益行的 debuff）
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Single;

    public override bool ShouldPlayVfx => false;

    private int _logs;

    private static string SafeEntry(PowerModel? power)
    {
        try { return power?.Id.Entry ?? "?"; } catch { return "?"; }
    }

    /// <summary>每次她给目标上负面 ⇒ 自己获得 4 点格挡（并补血肉戏法的伤害）。</summary>
    public override async Task AfterPowerAmountChanged(PlayerChoiceContext choiceContext, PowerModel power,
                                                      decimal amount, Creature? applier, CardModel? cardSource)
    {
        try
        {
            var owner = Owner;
            if (owner == null || !owner.IsAlive) return;

            // 诊断（限流）：把所有「会不会继续走」的输入一次打全 —— user 2026-11-10 报
            // 「命运同担还是不触发血肉戏法」时，光有钩子被叫到的那一行还不够定位是哪一条判据掐的。
            if (_logs++ < 40)
            {
                string kindNow = "?";
                try { kindNow = power == null ? "-" : power.GetTypeForAmount(amount).ToString(); } catch { kindNow = "抛异常"; }
                var t0 = power?.Owner;
                Logger.Log($"[CharBoss] 吞噬生命钩子：power={SafeEntry(power)} amount={amount:0.#} " +
                           $"目标={t0?.Name ?? "?"}(side={t0?.Side.ToString() ?? "-"}, monster={(t0?.Monster != null ? "有" : "无")}) " +
                           $"钩子施加者={(applier == null ? "null" : applier.Name)}(是我={ReferenceEquals(applier, owner)}) " +
                           $"power.Applier={(power?.Applier == null ? "null" : power.Applier.Name)}" +
                           $"(是我={ReferenceEquals(power?.Applier, owner)}) " +
                           $"类型={kindNow} temp={power is ITemporaryPower}");
            }

            if (power == null || amount == 0m) return;

            // 是不是「负面」：原版用 `GetTypeForAmount` 判；
            // ★ 2026-11-10 补充：**负数层数**（「失去 2 点力量」这种）一律当负面处理 ——
            //   与 `BossIntentBuilder.ClassifyPowers` 里 `harmful = kind == Debuff || amount < 0` 同一把尺子。
            PowerType kind;
            try { kind = power.GetTypeForAmount(amount); }
            catch { kind = PowerType.Buff; }
            bool harmful = kind == PowerType.Debuff || amount < 0m;
            if (!harmful) return;

            // 「临时降集中」那类（ITemporaryPower）不算 —— 与血肉戏法同一口径
            if (power is ITemporaryPower) return;

            // 目标必须是**真人玩家**（从她视角的敌人）；别的 Boss 的影子玩家不算
            var target = power.Owner;
            if (target == null || target.Monster != null) return;

            // ★★ 2026-11-10 第三刀（用户：「还是没修好，你自己读日志吧」）：
            //   **必须只用钩子参数里的 `applier`，不能回退到 `power.Applier`**。
            //   日志实证（22:56:25，命运同担）：
            //     `STRENGTH_POWER −2 → 目标=亡灵契约师(side=Player) 钩子施加者=… power.Applier=…(是我=False)`
            //   —— `StrengthPower` 是**复用已有实例**（玩家早先给自己 +1 力量时创建的那份），
            //   实例上的 `Applier` 还停在**玩家自己**，所以「回退到 power.Applier」这条把整件事判成
            //   「不是她给的」⇒ 9 点又丢了。（虚弱/易伤每次都新建实例，所以它们一直正常。）
            //   而 `applier` 是「这一次变化是谁造成的」—— 正是我们要的。
            //   反过来，**衰减**（`PowerCmd.TickDownDuration` / 覆甲每回合 −1）传的 `applier` 是 null，
            //   这样也不会把「负面层数掉 1」误当成「她给了负面」。
            bool fromHer = ReferenceEquals(applier, owner)
                          || (applier == null                                   // 钩子没给施加者时（衰减之类）
                              && ReferenceEquals(power.Applier, owner)
                              && amount > 0m);                                  // 只认「加层」，衰减（−1）不算
            if (!fromHer) return;

            await CreatureCmd.GainBlock(owner, FleshBone.DevourBlockNow, ValueProp.Unpowered, null);

            // ★ 2026-11-10 第二轮（用户：「命运同担还是不对玩家造成伤害」）：
            //   以前「血肉戏法」那 9 点放在 `Patches/SleightOfFleshBossPatch` 里（接管**她身上那层
            //   `SleightOfFleshPower`** 的回调）。但那条链要靠原版把这层 power 也派发一遍，
            //   一旦某次派发没走到（实测 22:42 那次 `STRENGTH_POWER −2` 连本行日志都没有），
            //   9 点就整条丢掉。而「吞噬生命」是她**必然带着**的特殊效果 —— 同一次派发里一定会到。
            //   ⇒ 把 9 点挪到这里一起算：只要她身上有血肉戏法（`SleightOfFleshPower`），
            //     就对那个玩家补一次 `Amount` 点**无来源伤害**（与反编译原版那一句完全一致）。
            int sleight = 0;
            try { sleight = Math.Max(0, owner.GetPowerAmount<SleightOfFleshPower>()); }
            catch { sleight = 0; }

            // 2026-11-13（恐惧模式「吞噬生命」）：她的「血肉戏法」按**升级版**算（9 → 13）
            if (sleight > 0) sleight = FleshBone.SleightNow(sleight);

            if (sleight > 0)
            {
                await CreatureCmd.Damage(choiceContext, target, sleight, ValueProp.Unpowered, owner);
            }

            Logger.Log($"[CharBoss] 吞噬生命：她给 {target.Name} 上了 {SafeEntry(power)}" +
                       $"（{amount:0.#}）⇒ 获得 {FleshBone.DevourBlockNow} 点格挡" +
                       (sleight > 0 ? $" + 血肉戏法 {sleight} 点无来源伤害" : ""));
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 吞噬生命触发失败: " + e.Message);
        }
    }
}
