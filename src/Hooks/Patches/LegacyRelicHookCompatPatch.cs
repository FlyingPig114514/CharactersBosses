using System;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models.Relics;
using CharacterBosses.Monsters;

namespace CharacterBosses.Patches;

/// <summary>
/// **旧版（v0.107）遗留钩子的两处适配** —— 这两件遗物是我们 Boss 真正携带、而两版行为不同的：
///
/// ## ① 钻石头冠 `DiamondDiadem`
/// - v0.111：`AfterSideTurnStart` = 「战斗开始给 20 格挡 + 残影」（我们的设计照这个实现：
///   战斗开始给 2 层残影、格挡在玩家回合开始同步补；原版那次由
///   <see cref="ShadowDiamondDiademOncePatch"/> 拦掉）。
/// - v0.107：**没有** `AfterSideTurnStart`，而是 `BeforeSideTurnEnd` ——「本回合打出的牌数 ≤ 阈值 ⇒
///   获得 `DiamondDiademPower`」。这是**另一种效果**，我们的设计里没有它；
///   不拦的话她身上会多出一层原版 power（钻石头冠在旧版会「白送」效果）。
///   ⇒ 旧版上把这次触发也拦掉（只对我们自己的 Boss）。
///
/// ## ② 烘焙手套 `ToastyMittens`
/// - v0.111：`AfterPlayerTurnStart`（她回合开始、手牌已就位之后）
/// - v0.107：`BeforeHandDraw`（**抽牌之前**）—— 那时她的手牌可能还是空的 ⇒
///   「消耗 1 张手牌」会被空过（原版自己会安静跳过），但我们的剧本点名了「该被烧掉的那张」。
///   这里只做**防御**：手牌为空时跳过原版（避免它走到牌选择流程里空转），非空则完全交给原版
///   （选择目标由 `BossCardSelector` 兜住）。
/// </summary>
[HarmonyPatch]
internal static class LegacyRelicHookCompatPatch
{
    // ───────── ① 钻石头冠（107 的 BeforeSideTurnEnd） ─────────
    [HarmonyPrepare]
    public static bool Prepare() => TargetMethod() != null;

    internal static MethodBase? TargetMethod()
        // ⚠ 只认「钻石头冠自己声明」的：否则会挂到基类上，把**所有模型**的 BeforeSideTurnEnd 拦掉
        => Core.GameCompat.DeclaredMethod(typeof(DiamondDiadem), "BeforeSideTurnEnd");

    [HarmonyPrefix]
    public static bool Prefix(object __instance, ref Task __result)
    {
        try
        {
            if (__instance is not DiamondDiadem diadem) return true;
            var owner = diadem.Owner;
            if (owner?.Creature == null) return true;
            if (owner.Creature.Side != CombatSide.Enemy) return true;   // 真人玩家：原版行为
            if (owner.Creature.Monster is not CharBossMonster) return true;

            __result = Task.CompletedTask;
            if (_logs++ < 6)
            {
                Logger.Log("[CharBoss] 钻石头冠（旧版钩子 BeforeSideTurnEnd）：跳过原版的「回合末补 power」，" +
                           "按我们的设计（战斗开始 2 层残影 + 玩家回合开始补 20 格挡）走");
            }
            return false;
        }
        catch { return true; }
    }

    private static int _logs;

    // ───────── ② 烘焙手套（107 的 BeforeHandDraw） ─────────
    [HarmonyPatch(typeof(ToastyMittens), "BeforeHandDraw")]
    internal static class MittensBeforeHandDrawCompat
    {
        [HarmonyPrefix]
        public static bool Prefix(ToastyMittens __instance, Player player)
        {
            try
            {
                if (__instance?.Owner?.Creature == null) return true;
                if (__instance.Owner.Creature.Monster is not CharBossMonster) return true;   // 真人玩家照旧

                int hand = 0;
                try { hand = player?.PlayerCombatState?.Hand?.Cards?.Count ?? 0; } catch { hand = 0; }
                if (hand > 0) return true;      // 手牌就位：原版流程（烧哪张由 BossCardSelector 兜）

                if (_mittenLogs++ < 6)
                {
                    Logger.Log("[CharBoss] 烘焙手套（旧版钩子 BeforeHandDraw）：此刻她手牌为空 ⇒ 跳过原版的烧牌，" +
                               "力量 +1 仍由原版同一方法先给（已给过就不会再走这里）");
                }
                return true;                    // 仍然放行：原版自己会跳过空手牌的消耗，但 +1 力量要给
            }
            catch { return true; }
        }

        private static int _mittenLogs;
    }
}
