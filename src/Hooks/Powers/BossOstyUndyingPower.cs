using System;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.ValueProps;
using CharacterBosses.Core;
using CharacterBosses.Monsters;

namespace CharacterBosses.Powers;

/// <summary>
/// 三层「**养手骨**」（亡灵契约师 · 奥斯提轴）的特殊效果「**不死**」—— 用户 2026-11-03 设计：
///
/// > 不死：奥斯提不会死亡。它仍然会替亡灵契约师承担未被格挡的攻击伤害直到奥斯提剩余 1 点生命值，
/// > 之后亡灵契约师会替奥斯提承担它受到的伤害。
/// > PS：…当它生命值为 1 时受到伤害（比如攻击了带有荆棘的玩家）或失去生命改为由亡灵契约师替它承担伤害。
/// > 和「为你而死」做好兼容，别递归了。
///
/// 分三条落地（都只认**她自己那只奥斯提**，其它 Boss 的奥斯提完全不受影响）：
///  1. **不会死**：`Hook.ShouldDie` 前缀 —— 只要是她的奥斯提、且 HP ≤ 0，就判定「不死」
///     （`__result = false`），并把 HP 拉回 1（`CreatureCmd.Heal` 只补 1 点，代价最小）。
///  2. **HP 掉不到 1 以下**：`ModifyHpLostBeforeOsty/AfterOsty` 把奥斯提要吃的伤害/失去生命
///     截到「当前生命 − 1」⇒ 正常攻击打在奥斯提身上时，它永远停在 1 点。
///  3. **只剩 1 点之后改由她承担**：`DieForYouPower.ModifyUnblockedDamageTarget` 后置补丁 ——
///     原版会把「她挨的未格挡攻击伤害」改指向她的随从（奥斯提）；当奥斯提 HP ≤ 1 时我们把它改回
///     **她自己**，也就是「亡灵契约师替奥斯提承担」。
///     ⚠ 防递归：这个钩子在一次伤害里只会解析一次，但为了保险，改写后再进来时用
///     <see cref="Redirecting"/> 短路，避免「她 → 奥斯提 → 她」来回弹。
/// </summary>
public sealed class BossOstyUndyingPower : PowerModel
{
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Single;

    public override bool ShouldScaleInMultiplayer => false;

    /// <summary>防止「她 ⇄ 奥斯提」来回改目标（用户要求「别递归」）。</summary>
    [ThreadStatic] private static bool Redirecting;

    /// <summary>这只生物是不是**我们这只 Boss** 的奥斯提。</summary>
    internal static bool IsOwnBossOsty(Creature? creature, Creature? bossCreature = null)
    {
        try
        {
            if (creature == null) return false;
            if (!BossOsty.IsBossOsty(creature)) return false;      // 只认影子玩家的奥斯提
            var owner = creature.PetOwner?.Creature;
            if (owner == null || owner.Monster is not CharBossMonster boss) return false;
            // 这一档是不是养手骨（特殊效果在场时才有这层 power；这里再兜一层按 power 判）
            if (boss.Creature == null) return false;
            try { if (boss.Creature.GetPower<BossOstyUndyingPower>() == null) return false; }
            catch { return false; }
            if (bossCreature != null && !ReferenceEquals(owner, bossCreature)) return false;
            return true;
        }
        catch { return false; }
    }

    /// <summary>奥斯提要吃的伤害/失去生命，最多只能掉到 1 点。</summary>
    private static decimal CapToLeaveOne(Creature? target, decimal amount)
    {
        try
        {
            if (amount <= 0) return amount;
            if (!IsOwnBossOsty(target)) return amount;
            int hp = target!.CurrentHp;
            decimal allowed = Math.Max(0, hp - 1);
            return Math.Min(amount, allowed);
        }
        catch { return amount; }
    }

    /// <summary>Osty 分段：奥斯提吸收之前的这段（她挨打时用，这里主要是兜住「直接打到奥斯提」）。</summary>
    public override decimal ModifyHpLostBeforeOsty(Creature target, decimal amount, ValueProp props,
                                                   Creature dealer, CardModel cardSource)
        => CapToLeaveOne(target, amount);

    /// <summary>Osty 分段：吸收之后（奥斯提自己挨打走这一段）。</summary>
    public override decimal ModifyHpLostAfterOsty(Creature target, decimal amount, ValueProp props,
                                                  Creature dealer, CardModel cardSource)
        => CapToLeaveOne(target, amount);

    // ── Harmony：① 不会死；③ 只剩 1 点时改由她承担 ─────────────────

    /// <summary>
    /// ① 奥斯提**不会死亡**：`Hook.ShouldDie` 判定「不死」，并把 HP 补回 1 点。
    /// （正常路径下 ② 的截断已经让它掉不到 1 以下，这里是兜底：荆棘、其它模组的直接 LoseHp 之类。）
    /// </summary>
    [HarmonyPatch(typeof(Hook), nameof(Hook.ShouldDie))]
    public static class NeverDiePatch
    {
        [HarmonyPrefix]
        public static bool Prefix(Creature creature, ref bool __result)
        {
            try
            {
                if (creature == null || creature.CurrentHp > 0) return true;
                if (!IsOwnBossOsty(creature)) return true;

                __result = false;                       // 不判死
                Logger.Log("[CharBoss] 不死：奥斯提本该死亡 → 判定不死，补回 1 点生命");
                try { _ = MegaCrit.Sts2.Core.Commands.CreatureCmd.Heal(creature, 1m); }
                catch (Exception e) { Logger.Log("[CharBoss] 不死：补回生命失败（不影响不死）: " + e.Message); }
                return false;
            }
            catch (Exception e)
            {
                Logger.Log("[CharBoss] 不死：判定失败，按原版执行: " + e.Message);
                return true;
            }
        }
    }

    /// <summary>
    /// ③ 原版「为你而死」会把**她**挨的未格挡攻击伤害改指向她的随从（奥斯提）。
    /// 当奥斯提只剩 1 点生命时，把目标改回**她自己** ⇒「亡灵契约师替奥斯提承担」。
    /// </summary>
    [HarmonyPatch(typeof(DieForYouPower), nameof(DieForYouPower.ModifyUnblockedDamageTarget))]
    public static class RedirectWhenOneHpPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Creature target, ref Creature __result)
        {
            try
            {
                if (Redirecting) return;
                if (__result == null) return;
                if (!IsOwnBossOsty(__result)) return;
                if (__result.CurrentHp > 1) return;                 // 还有余量：照原版让奥斯提挡

                var owner = __result.PetOwner?.Creature;
                if (owner == null) return;

                Redirecting = true;
                try
                {
                    Logger.Log("[CharBoss] 不死：奥斯提只剩 1 点生命 ⇒ 这一击改由亡灵契约师本人承受");
                    __result = owner;                                // 由她承担
                }
                finally { Redirecting = false; }

                _ = target;   // 参数只为对齐原版签名，用不到
            }
            catch (Exception e)
            {
                Logger.Log("[CharBoss] 不死：改派伤害失败（按原版）: " + e.Message);
            }
        }
    }

}
