using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace CharacterBosses.Patches;

/// <summary>
/// 把**原版那条意图**藏起来，只留我们手牌栏里画的那一套。
///
/// 用户 2026-10-24 实测截图（无色储君第一回合）：卡片下面**出现了两套意图**。
/// 原因：为了别的 mod（像薄荷尖塔那种「本回合将受到的总伤害」）能读到她的意图，我们会把计划
/// 写进原版 <c>MoveState.Intents</c>（见 <c>CharBossMonster.PublishVanillaIntents</c>）——
/// 而原版 UI 正是从同一个字段渲染敌人头顶那条意图的 ⇒ 数据一份、显示两份。
///
/// 这里的做法：**数据照旧发布**（别的 mod 不受影响），只把这个原版 <c>NIntent</c> 节点隐藏。
/// 判别方式：意图节点的祖先里有 <see cref="CharacterBosses.UI.BossHandPanel"/> 的，是我们自己画的，保留；
/// 其余（挂在生物节点下的那条）一律隐藏。
/// </summary>
[HarmonyPatch(typeof(NIntent), nameof(NIntent.UpdateIntent))]
public static class HideVanillaBossIntentPatch
{
    [HarmonyPostfix]
    public static void Postfix(NIntent __instance, Creature owner)
    {
        try
        {
            if (owner?.Monster is not CharacterBosses.Monsters.CharBossMonster) return;
            if (InsideOurPanel(__instance)) return;         // 我们自己画的意图：保留
            if (!__instance.Visible) return;

            __instance.Visible = false;
            if (_logged++ < 4)
            {
                Logger.Log("[CharBoss] 已隐藏原版意图条（她的意图由手牌栏显示；" +
                           "MoveState.Intents 里的数据仍然发布给别的 mod 读）");
            }
        }
        catch { /* 绝不影响显示以外的东西 */ }
    }

    private static int _logged;

    /// <summary>这个意图节点是不是我们自己手牌栏里的。</summary>
    private static bool InsideOurPanel(Godot.Node node)
    {
        try
        {
            var parent = node.GetParent();
            for (int i = 0; i < 10 && parent != null; i++)
            {
                if (parent is CharacterBosses.UI.BossHandPanel) return true;
                parent = parent.GetParent();
            }
        }
        catch { /* 忽略 */ }
        return false;
    }
}
