using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.ValueProps;
using CharacterBosses.Core;
using CharacterBosses.Monsters;

namespace CharacterBosses.Patches;

/// <summary>
/// **炼药猎（<c>SILENT_BREWER</c>）能用格挡挡下「失去生命」类效果**（用户 2026-11-09 要求）。
///
/// ## 为什么需要它
/// 原版「失去生命」= 带 <see cref="ValueProp.Unblockable"/> 的伤害
/// （崩坠觉醒者的「法力灼烧」`ManaburnPower.OnDrained` 就是
/// `CreatureCmd.Damage(ctx, Owner, 层数×次数, (ValueProp)6 /* Unblockable|Unpowered */, player.Creature)`）。
/// 而 <c>Creature.DamageBlockInternal</c> 的第一句就是：
/// <code>
/// decimal num = props.HasFlag(ValueProp.Unblockable) ? 0m : Math.Min(Block, amount);
/// Block -= (int)num;
/// </code>
/// ⇒ 只要带 <c>Unblockable</c>，格挡**一点都不会被吃掉**，伤害直接进 `LoseHpInternal` 扣血。
/// 用户要求：**只让炼药猎**能挡住，别的 Boss 一律照原版（不能因为这条改动影响任何其它 Boss / 真人玩家）。
///
/// ## 怎么改（只挂一个前缀，不接管流程）
/// 挂在 `Creature.LoseHpInternal(amount, props)` 上 —— 这是**所有**「失去生命」最终的公共出口：
///   · 普通伤害：`CreatureCmd.Damage` 先调 `DamageBlockInternal` 扣格挡，再把**剩下的**交给它
///     ⇒ 走到这里时她的格挡必然已经是 0 ⇒ 我们的判断天然不生效（零回归）；
///   · 不可格挡的「失去生命」：`DamageBlockInternal` 返回 0、格挡原封不动
///     ⇒ 这时用 `DamageBlockInternal(amount, props 去掉 Unblockable)` 让**原版自己**吃掉格挡
///       （连 `BlockChanged` 事件、血条格挡数字都是原版那套），再把剩下的血量伤害交还给原版。
///
/// ⚠ 刻意**不**改 `CreatureCmd.Damage`：它有 8 个重载，逐个挂补丁既容易漏、又容易踩
///   「Harmony 用 nameof 撞重载 ⇒ AmbiguousMatchException ⇒ 整个模组补丁全不挂」那个坑。
///
/// ⚠ 只覆盖「扣血」这一种语义。<c>Creature.SetCurrentHpInternal</c>（「把生命设为 X」，
///   例如减半 / 设成 0 处决）不算「失去生命」，格挡挡它没有意义，因此不接管。
/// </summary>
[HarmonyPatch(typeof(Creature), nameof(Creature.LoseHpInternal))]
public static class BrewerBossBlockableHpLossPatch
{
    /// <summary>日志上限（法力灼烧这类每打一张牌就触发一次，别刷屏）。</summary>
    private static int _logs;

    // ★ v0.2.940（游戏 v0.114.0）：`Creature.LoseHpInternal` 的 **`ValueProp props` 参数被删掉**了
    //   （现在是 `LoseHpInternal(decimal amount, bool dryRun)`）—— 再声明 `props` 会让 Harmony
    //   整类补丁挂不上（`Parameter "props" not found in method`）。
    //   好在这条补丁**本来就不需要 props**：它想做的只是「炼药猎的格挡也能吃掉『失去生命』」。
    //   而 `LoseHpInternal` 拿到的是**过完格挡之后**的剩余量 ——
    //     · 普通攻击：格挡已经在 `DamageBlockInternal` 里扣光 ⇒ 走到这里时 `Block == 0` ⇒ 下面的闸门自然跳过；
    //     · 不可格挡的「失去生命」：格挡一点没扣 ⇒ `Block > 0` ⇒ 正是我们要接管的那一类。
    //   所以把 props 判定换成「Block > 0」这个等价闸门即可。
    [HarmonyPrefix]
    public static void Prefix(Creature __instance, ref decimal amount, bool dryRun)
    {
        try
        {
            if (amount <= 0m) return;

            // ★★ v0.2.947（2026-11-25 联机两次不同步的两份摘要实证）：
            //   **dryRun 必须直接放行**。0.114 的签名是 `LoseHpInternal(decimal amount, bool dryRun)`，
            //   `dryRun = true` 是「只是问一下会掉多少血」的预测调用 —— 原版不动任何状态，
            //   而我们以前在这里**真的扣了格挡** ⇒ 谁跑预测、跑几次，两端就错开。
            //   （实证：房主 20:07:39/40 对炼药猎处理了**两次**「失去生命」，客人只有一次；
            //     两端摘要逐字比对，唯一不同的就是 `SILENT_BOSS` 的 HP：房主 499 / 客人 536。）
            if (dryRun) return;

            if (!IsBrewerBoss(__instance)) return;
            if (__instance.Block <= 0) return;

            // 让**原版**去吃掉格挡（用「无属性」问它，等价于把 Unblockable 那一位去掉）
            decimal absorbed = __instance.DamageBlockInternal(amount, ValueProp.Unpowered);
            if (absorbed <= 0m) return;

            amount -= absorbed;
            if (_logs++ < 60)
            {
                Logger.Log($"[CharBoss] 炼药猎：格挡挡下 {absorbed} 点「失去生命」" +
                           $"（还剩 {amount} 点直接扣血，格挡余 {__instance.Block}）" +
                           $" 调用者={CallerHint()}");
            }
        }
        catch (Exception e)
        {
            // 任何意外都退化成原版行为（绝不因为这条让战斗卡住）
            try { Logger.Log("[CharBoss] 炼药猎格挡「失去生命」失败，按原版执行: " + e.Message); } catch { }
        }
    }

    /// <summary>
    /// 这条「失去生命」是谁发起的（取 3 层调用栈，跳过 Harmony 自己的帧）。
    /// ★ v0.2.947：两份联机日志里「房主两次、客人一次」，只有调用者能告诉我们
    ///   到底是同一个效果被结算了两次，还是两个不同来源各结算了一次。
    /// </summary>
    private static string CallerHint()
    {
        try
        {
            var st = new System.Diagnostics.StackTrace(2, false);
            var parts = new System.Collections.Generic.List<string>();
            for (int i = 0; i < st.FrameCount && parts.Count < 3; i++)
            {
                var m = st.GetFrame(i)?.GetMethod();
                if (m == null) continue;
                string type = m.DeclaringType?.Name ?? "?";
                if (type.Contains("Harmony") || type.Contains("Patches") || type.Contains("BrewerBoss")) continue;
                parts.Add($"{type}.{m.Name}");
            }
            return parts.Count > 0 ? string.Join(" < ", parts) : "?";
        }
        catch { return "?"; }
    }

    /// <summary>
    /// 这一只生物是不是**炼药猎本体**。
    /// 三重判据（任一成立即可）：专属怪物类 / 钉死的设计 Key / 本场遭遇是炼药猎。
    /// ⚠ 只认她**本体**，不含她的随从，也不含任何其它 Boss —— 用户口径「只改她」。
    /// </summary>
    private static bool IsBrewerBoss(Creature? creature)
    {
        try
        {
            if (creature?.Monster is not CharBossMonster boss) return false;

            // ① 控制台 / 斗蛐蛐按类名点名的那只（TierBossMonsters.Act3.SilentBrewerBoss）
            if (creature.Monster is SilentBrewerBoss) return true;

            // ② 阵容 / 伙伴路径建出来的那只：SpawnVariantKey 写死成炼药猎
            if (string.Equals(boss.SpawnVariantKey, "SILENT_BREWER", StringComparison.Ordinal)) return true;

            // ③ 兜底：本场战斗的遭遇就是炼药猎（含「以房间遭遇为准」那条判定链）
            //
            // ★ v0.2.947：**必须同时带着「调制」那层 power** 才算。
            //   以前只看遭遇 ⇒ 4 人局里同一场遭遇下的**其它 Boss**（这一场的 `DEFECT_AI` 阵容里
            //   还有 NECROBINDER/REGENT/DEFECT 三只）也会被当成炼药猎、格挡也能吃「失去生命」——
            //   既不符合用户「只改她」的口径，也会让「谁被吸收」随两端遭遇解析不同而不同。
            var key = BossKeys.KeyOfEncounter(creature.CombatState?.Encounter);
            if (!string.Equals(key, "SILENT_BREWER", StringComparison.Ordinal)) return false;
            try { return creature.GetPower<Powers.BrewModPower>() != null; } catch { return false; }
        }
        catch { return false; }
    }

    private static string SafeEntry(ValueProp props)
    {
        try
        {
            if ((props & ValueProp.Unpowered) != 0) return "不可格挡 + 无来源";
            return "不可格挡";
        }
        catch { return "?"; }
    }
}
