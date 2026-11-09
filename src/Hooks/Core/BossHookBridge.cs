using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Orbs;
using CharacterBosses.Monsters;
using CharacterBosses.Patches;

namespace CharacterBosses.Core;

/// <summary>
/// 手动替影子 Player 触发「玩家侧」的时机钩子。
///
/// 影子 Player 不在 <c>CombatState.Players</c> 里（我们故意把它排除，避免它参与玩家回合），
/// 因此原版给玩家触发的钩子永远不会落到它头上 —— 后果是**它身上的遗物/能力不干活**：
///   · 裂核本应在战斗开始时引导 1 个闪电球 → 不引导；
///   · 弹珠袋/红面具/扭曲漏斗本应在战斗开始给玩家上易伤/虚弱/中毒 → 不生效；
///   · 亡灵契约师的初始遗物本应召 Osty、「下回合多抽牌」这类能力也不生效。
/// </summary>
public static partial class BossHookBridge
{
    /// <summary>在 Boss 出牌前代入「它的回合开始了」—— 让它的遗物与能力跑起来。</summary>
    public static async Task FireTurnStart(CharBossMonster boss, PlayerChoiceContext context)
    {
        var combat = boss.CombatState;
        var shadow = boss.Shadow;
        if (combat == null || shadow == null) return;

        try
        {
            await FirePlayerTurnStartPerModel(combat, context, shadow);
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 触发出牌前钩子失败: " + e.Message);
        }

        // 「抱抱先生」（MrStruggles，二层「王储」进阶 9 的奖励遗物）：
        // 原版是 `AfterPlayerTurnStart` → 对 `HittableEnemies` 造成 `PlayerCombatState.TurnNumber`
        // 点 `ValueProp.Unpowered` 伤害。影子玩家永远不是「回合开始时的那位真人玩家」
        // ⇒ 那条钩子根本不会替她触发，这里在**她的回合开始**手动结算一次
        // （目标 = 所有真人玩家；伤害 = 她的回合数，逐回合成长）。
        try
        {
            await Core.MrStrugglesPlays.RunAtTurnStart(boss, context);
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 抱抱先生（回合开始）失败: " + e.Message);
        }

        // 「下回合 +N 能量 / +N 辉星」到期：**必须自己删**。
        // 原版过期靠 EnergyNextTurnPower/StarNextTurnPower 的 AfterEnergyReset(player)，
        // 而那个钩子只由 CombatManager.SetupPlayerTurn 对 CombatState.Players 里的玩家派发 ——
        // 影子 Player 不在里面 ⇒ 这两个 Power 既不兑现也永不过期，图标一直挂在 Boss 身上（用户实测）。
        // 「只删不发」是刻意的：储君剧本已把这两笔写进回合能量（第 5 回合 4、第 9 回合 3），
        // 这里再发一次会让剧本里"故意打不出"的防御牌变成能打出。
        await RemoveNextTurnPowers(boss);

        // 「在你的回合开始时」类遗物（缚魂命匣召唤奥斯提、闪亮口红第 3 回合给力量…）
        // 挂的是 BeforeSideTurnStart / AfterSideTurnStart，而影子 Player 不是监听者 →
        // 必须每个 Boss 回合手动触发一次，否则奥斯提永远是死的（用户实测）。
        FireSideTurnStart(boss, context);

        // 灵魂绑定（只有二层灵魂骨有这个能力）：她这一回合的「抽牌」全部在这里入账。
        SoulBoundTurnStart(boss, context);
    }

    /// <summary>
    /// 灵魂绑定的回合开始记账（用户 2026-10-23）：
    ///   ① 她**回合开始抽的牌**（每回合 3 张 = 手牌 5 − 涅奥的祝福 2）→ **只**加自动化计数；
    ///   ② 玩家上个回合内额外抽的牌 → 折算成她这一回合的抽牌（死亡行军增伤），自动化在玩家抽的那一刻已记过；
    ///   ③ 本回合「她自己的回合内抽牌」计数清零（意图里预补的那份从满额起算）。
    /// 她的手牌是剧本直接造的（不走原版抽牌），所以这份账必须自己记。
    /// </summary>
    private static void SoulBoundTurnStart(CharBossMonster boss, PlayerChoiceContext context)
    {
        try
        {
            if (boss.Creature?.GetPower<Powers.BossSoulBoundPower>() == null) return;

            // ③ 先清零：本回合还没发生任何「她的回合内抽牌」
            boss.HerOwnDrawsThisTurn = 0;

            // ② 玩家上个回合的额外抽牌 → 她这一回合的抽牌（历史条目记在敌人回合这一侧）
            SoulBoundDrawPatch.FlushPendingDrawCredits(boss, context);

            // ① 回合开始补手牌：只进自动化计数（不加死亡行军）
            int draws = 0;
            try
            {
                draws = boss.Def.HandSizeForAct(boss.ActIndex)
                        - (boss.Def.CarriesNeowBlessing ? Relics.NeowBlessing.DrawPenalty : 0);
            }
            catch { /* 用 0 */ }
            if (draws > 0)
            {
                SoulBoundDrawPatch.CreditAutomation(boss, context, draws, true,
                                                    $"她回合开始抽 {draws} 张（只进自动化计数）");
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 灵魂绑定：回合开始记账失败: " + e.Message);
        }
    }

    /// <summary>
    /// 把他身上「下回合 +N 能量 / +N 辉星」的 Power 到期移除
    /// （汇流 <c>Convergence</c>、充电 <c>ChargeBattery</c> 用的就是这两个）。
    /// </summary>
    private static async Task RemoveNextTurnPowers(CharBossMonster boss)
    {
        var creature = boss.Creature;
        if (creature == null) return;

        var powers = new List<PowerModel>(creature.Powers);
        foreach (var power in powers)
        {
            try
            {
                if (power is MegaCrit.Sts2.Core.Models.Powers.EnergyNextTurnPower)
                {
                    Logger.Log($"[CharBoss] 「下回合 +{power.Amount} 能量」到期移除（原版 AfterEnergyReset 对影子玩家不派发）");
                    await PowerCmd.Remove(power);
                }
                else if (power is MegaCrit.Sts2.Core.Models.Powers.StarNextTurnPower)
                {
                    // ⚠ 不能直接移除：这笔星**根本没到账**就被清掉了（用户 2026-10-28 实测：
                    //   第 8 回合七星因为星不够打不出，日志里只有「到期移除」没有加星）。
                    //   先按 SettleStarNextTurnPower 加星（并结算黑洞），那一层由它自己移除。
                    Logger.Log($"[CharBoss] 「下回合 +{power.Amount} 辉星」到账（到期移除路径，先加星再移除）");
                    boss.SettleStarNextTurnPower();
                }
            }
            catch (Exception e)
            {
                Logger.Log("[CharBoss] 移除下回合能量/辉星失败: " + e.Message);
            }
        }
    }

    /// <summary>泛型施加：让游戏自己创建并初始化 PowerModel（自己 ToMutable 会 NRE）。</summary>
    private static async Task ApplySpecialPower<TPower>(PlayerChoiceContext ctx, Creature target)
        where TPower : PowerModel
    {
        await PowerCmd.Apply<TPower>(ctx, target, 1m, target, null, false);
    }

    /// <summary>把影子 Player 的回合数对齐成 Boss 自己的回合数（很多遗物按它判定「第 N 回合」）。</summary>
    public static void AlignTurnNumber(CharBossMonster boss, PlayerCombatState pcs)
        => SetTurnNumber(pcs, boss.TurnNumber);

    // TurnNumber 的 setter 在引用程序集里是 internal，只能改后备字段
    private static readonly System.Reflection.FieldInfo? TurnField =
        typeof(PlayerCombatState).GetField("<TurnNumber>k__BackingField",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

    private static int GetTurnNumber(PlayerCombatState pcs)    {
        try { return TurnField?.GetValue(pcs) is int v ? v : 0; }
        catch { return 0; }
    }

    private static void SetTurnNumber(PlayerCombatState pcs, int value)
    {
        try { TurnField?.SetValue(pcs, value); }
        catch (Exception e) { Logger.Log("[CharBoss] 设置回合数失败: " + e.Message); }
    }

    /// <summary>影子玩家身上有没有「裂核」（破损核心）。</summary>
    private static bool HasCrackedCore(Player shadow)
    {
        try
        {
            var relics = shadow.Relics;
            if (relics == null) return false;
            for (int i = 0; i < relics.Count; i++)
            {
                if (relics[i]?.Id.Entry == "CRACKED_CORE") return true;
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 查裂核失败: " + e.Message);
        }
        return false;
    }

    private static string SafeEntry(RelicModel relic)
    {
        try { return relic.Id.Entry; } catch { return string.Empty; }
    }

    /// <summary>
    /// 替影子玩家触发「在你的回合开始时」（<c>AbstractModel.AfterPlayerTurnStart*</c>）。
    ///
    /// ★ v0.2.266（用户 2026-10-31 表格里的 diag：`触发出牌前钩子失败: Sequence contains no elements`）：
    ///   以前这里调 <c>Hook.AfterPlayerTurnStart(combat, ctx, shadow)</c> —— 那是**一条广播链**：
    ///     · 它会对整场战斗的**每一个**监听者派发（连真人玩家的遗物都会因为 Boss 的回合被触发一次）；
    ///     · 任何一个 model 抛异常，**后面的 model 全部哑掉**，我们外面只 catch 到一句笼统的日志，
    ///       连是谁炸的都看不出来（玩家的表里就是这么一条没头没尾的 diag）。
    ///   影子玩家的遗物/能力才真正需要「它自己的回合开始了」，所以改成**只对它们逐个派发**：
    ///   每件单独 try/catch ⇒ 一件坏掉不影响其它，并且日志能点名是哪一件。
    ///   （Early / After / Late 三个阶段各走一遍，和原版顺序一致。）
    /// </summary>
    private static async Task FirePlayerTurnStartPerModel(ICombatState combat, PlayerChoiceContext ctx, Player shadow)
    {
        var relics = Snapshot(shadow.Relics ?? System.Array.Empty<RelicModel>());
        var powers = new List<PowerModel>();
        try
        {
            var list = shadow.Creature?.Powers;
            if (list != null)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i] != null) powers.Add(list[i]!);
                }
            }
            // 随从（奥斯提 / 佩尔的士兵…）身上的能力也算「它这一侧」的监听者
            var pets = shadow.PlayerCombatState?.Pets;
            if (pets != null)
            {
                for (int i = 0; i < pets.Count; i++)
                {
                    var petPowers = pets[i]?.Powers;
                    if (petPowers == null) continue;
                    for (int k = 0; k < petPowers.Count; k++)
                    {
                        if (petPowers[k] != null) powers.Add(petPowers[k]!);
                    }
                }
            }
        }
        catch { /* 拿不到能力就只跑遗物 */ }

        foreach (var relic in relics)
        {
            await RunModelHook(relic, "遗物", "AfterPlayerTurnStartEarly", () => relic.AfterPlayerTurnStartEarly(ctx, shadow));
            await RunModelHook(relic, "遗物", "AfterPlayerTurnStart", () => relic.AfterPlayerTurnStart(ctx, shadow));
            await RunModelHook(relic, "遗物", "AfterPlayerTurnStartLate", () => relic.AfterPlayerTurnStartLate(ctx, shadow));
        }
        foreach (var power in powers)
        {
            await RunModelHook(power, "能力", "AfterPlayerTurnStartEarly", () => power.AfterPlayerTurnStartEarly(ctx, shadow));
            await RunModelHook(power, "能力", "AfterPlayerTurnStart", () => power.AfterPlayerTurnStart(ctx, shadow));
            await RunModelHook(power, "能力", "AfterPlayerTurnStartLate", () => power.AfterPlayerTurnStartLate(ctx, shadow));
        }
    }

    /// <summary>跑一条 model 钩子；失败只记一行并继续（同一件+同一钩子每场只记一次，别刷屏）。</summary>
    private static async Task RunModelHook(MegaCrit.Sts2.Core.Models.AbstractModel model, string kind,
                                           string hook, Func<Task> call)
    {
        try
        {
            // ★ v0.2.348（用户 2026-11-03：「对战事件机时回合仍然无法正常结束」）：
            //   遗物钩子偶尔会**永远不返回**（等一个永远不来的玩家选择 / 卡在动画上），
            //   而她这一整回合都在 await 它 ⇒ 表现为「回合卡住、结束回合没反应」。
            //   这里给每个钩子加 1.5 秒上限：超时就记录下来、当它没做完，继续往下走。
            var task = call();
            var finished = await Task.WhenAny(task, Task.Delay(HookTimeoutMs));
            if (!ReferenceEquals(finished, task))
            {
                string stuckName = "?";
                try { stuckName = model.Id.Entry; } catch { /* 用问号 */ }
                string stuckKey = kind + ":" + stuckName + ":" + hook;
                if (_hookFailureLogged.Add(stuckKey))
                {
                    Logger.Log($"[CharBoss] {kind} {stuckName} 的 {hook} 超过 {HookTimeoutMs}ms 没返回 ⇒ " +
                               "不再等它（继续这一回合）");
                }
                return;
            }
            await task;      // 已经完成：把异常照常抛出来走下面的 catch
        }
        catch (Exception e)
        {
            string name = "?";
            try { name = model.Id.Entry; } catch { /* 用问号 */ }
            string key = kind + ":" + name + ":" + hook;
            if (_hookFailureLogged.Add(key))
            {
                Logger.Log($"[CharBoss] {kind} {name} 的 {hook} 失败: {e.Message}（本场跳过，其余照常）");
            }
        }
    }

    /// <summary>单个遗物/能力钩子的等待上限（毫秒）。超时就不等了，避免把她整个回合拖死。</summary>
    private const int HookTimeoutMs = 1500;
    /// <summary>已经记过的「某件 model 的某个钩子失败」（避免每个 Boss 回合刷一条）。</summary>
    private static readonly HashSet<string> _hookFailureLogged = new(StringComparer.Ordinal);

    private static List<RelicModel> Snapshot(IReadOnlyList<RelicModel> relics)    {        var copy = new List<RelicModel>();
        for (int i = 0; i < relics.Count; i++)
        {
            if (relics[i] != null) copy.Add(relics[i]);
        }
        return copy;
    }

    private static async Task SafeRun(RelicModel relic, string hook, Func<Task> call)
    {
        try
        {
            await call();
        }
        catch (Exception e)
        {
            Logger.Log($"[CharBoss] 遗物 {relic.Id.Entry} 的 {hook} 失败: {e.Message}");
        }
    }

    /// <summary>把异步钩子丢到主循环里跑（<c>PrepareTurn</c> 是同步方法，不能 await）。</summary>
    public static void FireCombatStartWithoutAwait(CharBossMonster boss)
        => TaskHelper.RunSafely(FireCombatStart(boss));
}
