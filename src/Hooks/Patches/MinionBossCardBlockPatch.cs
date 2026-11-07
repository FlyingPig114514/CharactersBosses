using System;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using CharacterBosses.Monsters;
using CharacterBosses.Powers;

namespace CharacterBosses.Patches;

/// <summary>
/// 二层「**仆从储**」：**拦掉原版这几张牌的「变牌 / 印牌」效果**（用户 2026-11-05：
/// 「拦掉原版的变牌、印牌，因为我都已经提前写好了」）。
///
/// 涉及的牌：
///   变牌：下去！`Begone`、冲锋！！`Charge`、护驾！！！`Guards`
///   印牌：碰撞轨迹 `CollisionCourse`（印碎屑）、类星体 `Quasar`（印无色牌）、大奖 `Jackpot`（印 3 张 0 费牌）
///
/// 剧本（<c>Core/RegentMinionScript</c>）已经把「变出来 / 印出来」的那张牌直接摆在手牌里，
/// 所以这里把整张牌的 `OnPlay` 跳过即可（格挡 / 伤害这类**别的**效果这些牌本来就没有）。
///
/// 只对**我们自己的仆从储**（身上挂着 <see cref="BossCallToArmsPower"/>）生效；真人玩家不受影响。
/// </summary>
[HarmonyPatch]
public static class MinionBossCardBlockPatch
{
    private static int _logs;

    /// <summary>这张牌是不是「仆从储」打出的（决定要不要拦）。</summary>
    private static bool ShouldBlock(CardModel? card, out string entry)
    {
        entry = "?";
        try
        {
            if (card == null) return false;
            var monster = card.Owner?.Creature?.Monster;
            if (monster is not CharBossMonster boss) return false;
            if (boss.Creature?.GetPower<BossCallToArmsPower>() == null) return false;
            try { entry = card.Id.Entry ?? "?"; } catch { }
            return true;
        }
        catch { return false; }
    }

    private static bool Block(CardModel? card, ref Task __result)
    {
        try
        {
            if (!ShouldBlock(card, out string entry)) return true;
            if (_logs++ < 40)
            {
                Logger.Log($"[CharBoss] 仆从储：拦掉 {entry} 的原版「变牌/印牌」效果（剧本已提前摆好）");
            }
            __result = Task.CompletedTask;
            return false;
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 拦截变牌/印牌失败，按原版: " + e.Message);
            return true;
        }
    }

    // ── 变牌 ──
    [HarmonyPatch(typeof(Begone), "OnPlay", new[] { typeof(PlayerChoiceContext), typeof(CardPlay) })]
    [HarmonyPrefix]
    public static bool BegonePrefix(Begone __instance, ref Task __result) => Block(__instance, ref __result);

    [HarmonyPatch(typeof(Charge), "OnPlay", new[] { typeof(PlayerChoiceContext), typeof(CardPlay) })]
    [HarmonyPrefix]
    public static bool ChargePrefix(Charge __instance, ref Task __result) => Block(__instance, ref __result);

    [HarmonyPatch(typeof(Guards), "OnPlay", new[] { typeof(PlayerChoiceContext), typeof(CardPlay) })]
    [HarmonyPrefix]
    public static bool GuardsPrefix(Guards __instance, ref Task __result) => Block(__instance, ref __result);

    // ── 印牌（**没有**伤害的那张：类星体 —— 整张跳过）──
    [HarmonyPatch(typeof(Quasar), "OnPlay", new[] { typeof(PlayerChoiceContext), typeof(CardPlay) })]
    [HarmonyPrefix]
    public static bool QuasarPrefix(Quasar __instance, ref Task __result) => Block(__instance, ref __result);

    // ── 印牌，但**本体有伤害**的：碰撞轨迹（10 伤 + 印碎屑）、大奖（25 伤 + 印 3 张 0 费牌）──
    //   用户 2026-11-05：「碰撞轨迹怎么不造成伤害？」⇒ 这两张**只拦印牌，伤害照原版结算**。
    //   做法：开一个「生成窗口」，窗口内她生成的牌直接不加入任何牌堆（见下面的生成前缀）。
    [HarmonyPatch(typeof(CollisionCourse), "OnPlay", new[] { typeof(PlayerChoiceContext), typeof(CardPlay) })]
    [HarmonyPrefix]
    public static void CollisionPrefix(CollisionCourse __instance) => OpenSuppressWindow(__instance);

    [HarmonyPatch(typeof(CollisionCourse), "OnPlay", new[] { typeof(PlayerChoiceContext), typeof(CardPlay) })]
    [HarmonyPostfix]
    public static void CollisionPostfix(ref Task __result) => CloseSuppressWindow(__result);

    [HarmonyPatch(typeof(Jackpot), "OnPlay", new[] { typeof(PlayerChoiceContext), typeof(CardPlay) })]
    [HarmonyPrefix]
    public static void JackpotPrefix(Jackpot __instance) => OpenSuppressWindow(__instance);

    [HarmonyPatch(typeof(Jackpot), "OnPlay", new[] { typeof(PlayerChoiceContext), typeof(CardPlay) })]
    [HarmonyPostfix]
    public static void JackpotPostfix(ref Task __result) => CloseSuppressWindow(__result);

    /// <summary>正在「只保留伤害、拦掉印牌」的仆从储（null = 窗口没开）。</summary>
    internal static CharBossMonster? SuppressFor;

    private static void OpenSuppressWindow(CardModel card)
    {
        try
        {
            SuppressFor = null;
            if (!ShouldBlock(card, out string entry)) return;
            SuppressFor = card.Owner?.Creature?.Monster as CharBossMonster;
            Logger.Log($"[CharBoss] 仆从储：{entry} 只拦「印出来的牌」，伤害照原版结算");
        }
        catch { SuppressFor = null; }
    }

    private static void CloseSuppressWindow(Task result)
    {
        try
        {
            if (SuppressFor == null) return;
            if (result == null) { SuppressFor = null; return; }
            result.ContinueWith(_ => SuppressFor = null, TaskScheduler.Default);
        }
        catch { SuppressFor = null; }
    }
}

/// <summary>
/// 「只保留伤害、拦掉印牌」的执行者（碰撞轨迹 / 大奖）：
/// 当 <see cref="MinionBossCardBlockPatch.SuppressFor"/> 被打开时，
/// 她生成的牌**不加入任何牌堆**（剧本已经把那几张提前摆好了）。
/// </summary>
[HarmonyPatch(typeof(MegaCrit.Sts2.Core.Commands.CardPileCmd), "AddGeneratedCardsToCombat")]
public static class MinionBossNoGeneratePatch
{
    private static int _logs;

    [HarmonyPrefix]
    public static bool Prefix(IEnumerable<CardModel> cards, ref System.Threading.Tasks.Task<IReadOnlyList<MegaCrit.Sts2.Core.Entities.Cards.CardPileAddResult>> __result)
    {
        try
        {
            var boss = MinionBossCardBlockPatch.SuppressFor;
            if (boss == null) return true;

            bool anyOurs = false;
            if (cards != null)
            {
                foreach (var card in cards)
                {
                    if (card?.Owner?.Creature?.Monster == boss) { anyOurs = true; break; }
                }
            }
            if (!anyOurs) return true;

            if (_logs++ < 30)
            {
                Logger.Log("[CharBoss] 仆从储：拦掉原版「印出来」的牌（剧本已提前摆好）");
            }
            // ⚠ 必须**按输入张数**返回等长的结果列表：
            //   原版 `AddGeneratedCardToCombat` 会拿 `(await AddGeneratedCardsToCombat(...))[0]` 取第一个元素
            //   ⇒ 返回空列表会直接 IndexOutOfRange，导致「牌吊在空中 + 回合卡死」（用户 2026-11-05 实测）。
            int count = 0;
            if (cards != null) foreach (var _ in cards) count++;
            var stub = new List<MegaCrit.Sts2.Core.Entities.Cards.CardPileAddResult>(Math.Max(1, count));
            for (int i = 0; i < count; i++) stub.Add(default);
            __result = System.Threading.Tasks.Task.FromResult<IReadOnlyList<MegaCrit.Sts2.Core.Entities.Cards.CardPileAddResult>>(stub);
            return false;
        }
        catch { return true; }
    }
}