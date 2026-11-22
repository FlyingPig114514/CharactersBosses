using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models.Cards;

namespace CharacterBosses.Patches;

/// <summary>
/// 「**模仿学习**」（<c>ImitationLearning</c>）由我们 Boss 打出时，**不挂原版能力**。
///
/// 玩家反馈 2026-11-23：
/// > 「彩虹机在多人模式下使用模仿学习时似乎会复制自己的扩容，导致他多了 4 个球位」
///
/// ## 根因（反编译逐字对过，不是猜）
/// <code>
/// // ImitationLearning.OnPlay
/// ImitationLearningPower p = Owner.Creature.Powers.OfType&lt;ImitationLearningPower&gt;()
///                                    .FirstOrDefault(s =&gt; s.PlayerTarget == cardPlay.Target.Player);
/// ...
/// p = await PowerCmd.Apply&lt;ImitationLearningPower&gt;(ctx, Owner.Creature, 2, Owner.Creature, this);
/// p.PlayerTarget = cardPlay.Target.Player;      // ← 目标就是「这张牌的出牌目标」
///
/// // ImitationLearningPower.BeforeCardPlayed
/// if (cardPlay.Card.Owner != _playerTarget) return;
/// if (cardPlay.Card.Type != CardType.Power) return;
/// 记下 (card, clone)  ⇒ AfterCardPlayed 里 CardCmd.AutoPlay(clone)   // 复制「那个人」的能力牌
/// </code>
/// 而这张牌是 `TargetType.AnyAlly`（选另一名玩家），我们的 `BossPlanner.ChooseTarget` 对
/// 「不是敌人的目标类型」一律 **`return self`** ⇒ 她打「模仿学习」时目标是**她自己** ⇒
/// `PlayerTarget` = 她自己的影子玩家 ⇒ 她**每打一张能力牌就把自己那张复制一遍**。
/// 她的剧本里有「扩容」（+2 充能球栏位）⇒ 扩容 2 + 复制品 2 = **4 个球位**，正是用户看到的现象。
///
/// ## 设计口径（用户 2026-11-07）
/// > 「多人时，模仿学习选择目标的优先级从高到低：涂毒猎、铸剑储、放血战…」
/// ⇒ 我们 Boss 的「模仿」本来就该由 <see cref="Core.RainbowMimic"/> 按优先级挑**在场的另一只 Boss**，
/// 原版那半（挂 `ImitationLearningPower`、复制目标的能力牌）在 Boss 身上是错的。
/// 所以这里把它整段跳掉；「挑目标 + 记牌 + 下回合免费打出」由 `RainbowMimic`
/// （`CharBossMonster.PlayOne` 里的 `OnCardPlayed` → `StartMimic`）负责，一条都不少。
///
/// ⚠ 只对**我们自己的 Boss** 生效：真人玩家打出这张牌时原版行为一个像素不变。
/// ⚠ `Task` 返回值的方法，前缀 `return false` 时**必须**填 `__result`，否则原版会 `await` 一个 null Task。
/// </summary>
[HarmonyPatch(typeof(ImitationLearning), "OnPlay")]
internal static class ImitationLearningIsMimicOnlyPatch
{
    private static int _logged;

    [HarmonyPrefix]
    private static bool Prefix(ImitationLearning __instance, CardPlay cardPlay, ref Task __result)
    {
        try
        {
            var boss = __instance?.Owner?.Creature?.Monster as Monsters.CharBossMonster;
            if (boss == null) return true;              // 真人玩家的模仿学习：原版

            if (_logged++ < 6)
            {
                Logger.Log($"[CharBoss] 模仿学习：这是我们的 Boss（{boss.Id.Entry}）打出的 ⇒ 不挂原版能力" +
                           $"（原版会把目标认成她自己 ⇒ 复制自己的扩容；改为 RainbowMimic 模仿在场其它 Boss）");
            }

            // 「挑目标」由 CharBossMonster.PlayOne 里的 RainbowMimic.OnCardPlayed 做，这里只压掉原版那半。
            _ = cardPlay;                                // 参数只为了拿签名，不参与判断
            __result = Task.CompletedTask;               // ← 必须！否则 await null 会打断出牌流程
            return false;
        }
        catch
        {
            return true;                                 // 判不出来就交给原版
        }
    }
}
