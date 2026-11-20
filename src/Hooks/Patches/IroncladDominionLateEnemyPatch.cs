#if STS2_113   // 恐惧模式（DominionModel）是 0.113 才有的东西
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;

namespace CharacterBosses.Patches;

/// <summary>
/// **后加入战斗的敌人也要吃到「铁甲战士的支配」**（用户 2026-11-20 截图：4 人恐惧模式里
/// 三只敌人只有第一只挂上了恶魔形态）。
///
/// 为什么光靠 `DominionModel.AfterEnemiesEnteredCombat` 不够：那只生物刚被 `AddCreature` 进来时，
/// 血量 / 阵营 / `IsAlive` 可能还没初始化完，那一刻我们判「不是活着的敌人」就跳过了，
/// 而原版之后**不会**再为它派发一次钩子 ⇒ 它永远拿不到恶魔形态。
///
/// 这里挂在游戏自己的出牌/加入流程出口 `CombatManager.AfterCreatureAdded(Creature)` 上，
/// 每次有新生物加入战斗就给它一个「稍后再看一次」的机会（幂等；本幕不是铁甲战士的支配就直接返回）。
/// </summary>
[HarmonyPatch(typeof(CombatManager), nameof(CombatManager.AfterCreatureAdded),
    new[] { typeof(Creature) })]
internal static class IroncladDominionLateEnemyPatch
{
    [HarmonyPostfix]
    public static void Postfix(Creature creature)
    {
        try { Core.IroncladDominion.EnsureForLateEnemy(creature); }
        catch { /* 绝不影响战斗流程 */ }
    }
}
#endif
