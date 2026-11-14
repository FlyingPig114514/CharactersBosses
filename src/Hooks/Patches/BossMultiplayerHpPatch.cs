// ⚠ 0.113.0 起这个模型从 `Models.Singleton.MultiplayerScalingModel` 挪到了
//   `Models.MultiplayerScalingModel`（编译实测 CS0234）⇒ 两个版本各指一次。
#if STS2_107
using BossScalingModel = MegaCrit.Sts2.Core.Models.Singleton.MultiplayerScalingModel;
#else
using BossScalingModel = MegaCrit.Sts2.Core.Models.MultiplayerScalingModel;
#endif
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using CharacterBosses.Monsters;

namespace CharacterBosses.Patches;

/// <summary>
/// 多人时**不让本模组的 Boss 按人数涨血**（用户设计：多人是靠"多一只 Boss"增加压力，不是靠血量）。
///
/// 为什么要单独拦：原版 <c>Creature.ScaleMonsterHpForMultiplayer(encounter, playerCount, actIndex)</c>
/// 会在多人时把怪物血量乘上去。我们这几只 Boss 的血量是按单人设计好的固定值
/// （例：第三幕无色储君 555），被缩放就完全偏离设计。
///
/// **严格只走多人分支**：<c>playerCount &lt; 2</c> 时直接放行（原版怎么算就怎么算），
/// 所以单人模式的行为**完全不变**；也只认 <see cref="CharBossMonster"/>，原版怪物不受影响。
/// </summary>
[HarmonyPatch(typeof(Creature), nameof(Creature.ScaleMonsterHpForMultiplayer))]
public static class BossNoMultiplayerHpScalePatch
{
    [HarmonyPrefix]
    public static bool Prefix(Creature __instance, int playerCount)
    {
        try
        {
            if (playerCount < 2) return true;                       // 单人：原样
            if (__instance?.Monster is CharBossMonster)             // 我们的 Boss：不缩放
            {
                Logger.Log($"[CharBoss] 多人({playerCount} 人)：跳过 {__instance.Monster.Id.Entry} 的血量缩放");
                return false;
            }
        }
        catch
        {
            /* 出错就走原版 */
        }
        return true;
    }
}

/// <summary>
/// 多人时**不让本模组的 Boss 的格挡按人数翻倍**。
///
/// 反编译确认（`MultiplayerScalingModel.ModifyBlockMultiplicative`）：目标只要是
/// primary/secondary enemy，就返回
/// <code>
/// int count = RunState.Players.Count;
/// if (count &lt;= 2) return count;                    // 2 人 ⇒ 格挡 ×2（用户实测：起甲翻倍）
/// return count * GetMultiplayerScaling(encounter, act);   // 3 人以上再 ×1.1/1.2/1.3
/// </code>
/// 我们的 Boss 数值都是按单人设计好的固定值（血量也不缩放，见上一个补丁），
/// 格挡被乘人数就完全偏离设计了 ⇒ 这里把**我们** Boss 的乘数强制改成 1。
///
/// 单人局：原版本来就返回 1 ⇒ 这条不改变任何东西；原版怪物不受影响。
/// </summary>

[HarmonyPatch(typeof(BossScalingModel), nameof(BossScalingModel.ModifyBlockMultiplicative))]
public static class BossNoMultiplayerBlockScalePatch
{
    private static int _logged;

    [HarmonyPostfix]
    public static void Postfix(Creature target, ref decimal __result)
    {
        try
        {
            if (__result == 1m) return;                       // 原版没缩放：不动
            if (target?.Monster is not CharBossMonster) return;   // 原版怪物：不动

            if (_logged++ < 20)
            {
                Logger.Log($"[CharBoss] 多人：{target.Monster.Id.Entry} 的格挡缩放 ×{__result} → ×1（保持单人设计值）");
            }
            __result = 1m;
        }
        catch { /* 出错就用原版乘数 */ }
    }
}
