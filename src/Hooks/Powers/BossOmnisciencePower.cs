#if STS2_111   // 观者 Boss 只在这个构建里
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using CharacterBosses.Core;

namespace CharacterBosses.Powers;

/// <summary>
/// 一层 1 号观者 Boss「**预见观**」的特殊效果「**通晓万物**」（用户 2026-11-17）：
///
/// > 通晓万物：每当观者触发先见之明时，将你抽牌堆中的一张随机牌丢弃。
/// > 恐惧增强：修改特殊效果为「每当观者**预见**时，将你抽牌堆中的一张随机牌丢弃。」
/// > PS：预见是从抽牌堆里弃牌的，所以被弃的牌不用在她手牌中显示。
///
/// ## 触发点（反编译 A 版 `Watcher.dll` 取到的两个钩子）
/// · **普通模式**：**观者自己的回合开始时**，只要她身上带着「先见之明」（`ForesightPower`）就触发一次
///   —— 由 `BossHookBridge.FireSideTurnStart`（她每个回合开始时调一次）驱动。
///   ⚠ 以前挂的是 `ForesightPower.BeforeHandDrawCompat`，但那条钩子是**每次都广播给所有 Power** 的：
///   原版 power 自己会判 `player == Owner.Player`（不是她的抽牌就什么都不做），我们那条 postfix 没判
///   ⇒ **真人玩家回合开始抽牌时**也会触发一次，玩家看到的就是「我的回合刚开始她就丢我一张牌」
///   （用户 2026-11-18 反馈）。现在改由她自己的回合开始显式触发，时机与卡面「每回合开始预见 3」一致。
/// · **恐惧模式**：改成 `WatcherMod.WatcherCombatHelper.Scry` —— 任何「预见」
///   （天眼 / 斩破命运 / 美琅脂洗牌 / 先见之明…）都算，正好对应用户那句「每当观者**预见**时」。
///
/// ## 结算口径
/// · **单体**：只对被这只 Boss 锁定的那位玩家生效（`BossLockOn.PureVictim`）——
///   和其它特殊效果一致，多人时其余玩家不受影响；
/// · 从该玩家**抽牌堆**里随机挑 1 张，`CardPileCmd.Add(card, PileType.Discard, CardPilePosition.Random)`
///   丢进弃牌堆（和「预见」本身同一个语义：从抽牌堆弃牌）；
/// · 抽牌堆是空的时候就白给一次（不抽牌、不补牌）。
/// </summary>
public sealed class BossOmnisciencePower : PowerModel
{
    public override PowerType Type => PowerType.Buff;

    public override PowerStackType StackType => PowerStackType.Single;

    public override bool ShouldPlayVfx => false;

    private static int _logs;
    private static bool _installed;
    private static int _installAttempts;

    /// <summary>她被挂上这层能力时顺手把两个触发钩子装上（幂等）。</summary>
    public override Task AfterApplied(Creature? applier, CardModel? cardSource)
    {
        try { TryInstall(); } catch { /* 绝不影响战斗 */ }
        return Task.CompletedTask;
    }

    // ── 钩子安装 ───────────────────────────────────────────────────────────
    internal static void TryInstall()
    {
        if (_installed) return;
        _installAttempts++;
        try
        {
            bool okAny = false;

            // ① 恐惧模式：任何「预见」（Scry）都算
            var combatHelper = WatcherCards.T("WatcherCombatHelper");
            var scry = combatHelper?.GetMethod("Scry",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (scry != null)
            {
                var fix2 = new HarmonyMethod(typeof(BossOmnisciencePower)
                    .GetMethod(nameof(ScryScopePostfix), BindingFlags.Static | BindingFlags.NonPublic)!);
                PatchSafe("CharacterBosses.watcher.omniscience.scry", scry, fix2);
                okAny = true;
            }
            if (combatHelper == null)
            {
                if (_installAttempts <= 3) Logger.Log("[CharBoss] 通晓万物：暂时没找到观者的 WatcherCombatHelper ⇒ 稍后重试");
                return;
            }

            // ② 顺手装上「迂回不自动返手」（预见观的牌里只有她会带迂回）
            try { BossWeaveNoReturn.TryInstall(); } catch { /* 忽略 */ }

            _installed = true;
            Logger.Log("[CharBoss] 通晓万物：已挂上恐惧模式的「预见」钩子；普通模式改由她的回合开始触发" +
                       (okAny ? "" : "（Scry 入口没找到，恐惧模式可能不触发）"));
        }
        catch (Exception e) { Logger.Log("[CharBoss] 通晓万物：挂钩失败: " + e.Message); }
    }

    private static void PatchSafe(string id, MethodBase target, HarmonyMethod postfix)
    {
        try { new Harmony(id).Patch(target, postfix: postfix); }
        catch (Exception e) { Logger.Log($"[CharBoss] 通晓万物：{id} 挂载失败: {e.Message}"); }
    }

    /// <summary>
    /// **她自己的回合开始**（用户 2026-11-18：「先见之明应该在她回合开始触发，而不是玩家回合开始」）。
    /// 由 <see cref="Core.BossHookBridge"/> 的 `FireSideTurnStart` 每个 Boss 回合调一次。
    ///
    /// 触发条件三条缺一不可：
    /// ① 不是恐惧模式（恐惧走 Scry 那条：每次预见都算）；
    /// ② 她身上挂着「通晓万物」（= 这一场真的是预见观）；
    /// ③ 她已经打出过「先见之明」（`ForesightPower`）—— 它自己就是「每回合开始预见 3」，
    ///    所以没打出来之前不该有任何弃牌（她第 3 回合才打出它）。
    /// </summary>
    internal static void OnBossTurnStart(Monsters.CharBossMonster boss, PlayerChoiceContext? ctx)
    {
        try
        {
            if (boss?.Creature == null) return;
            if (DreadMode.IsActive) return;
            if (boss.Creature.GetPower<BossOmnisciencePower>() == null) return;
            if (!HasForesight(boss.Creature)) return;

            // 同一个 Boss 回合只结算一次（FireSideTurnStart 理论上只调一次，这里再兜一层）
            int turn = 0;
            try { turn = boss.Shadow?.PlayerCombatState?.TurnNumber ?? 0; } catch { turn = 0; }
            if (ReferenceEquals(boss, _lastTurnBoss) && turn == _lastTurnNumber) return;
            _lastTurnBoss = boss;
            _lastTurnNumber = turn;

            var context = ctx ?? new BlockingPlayerChoiceContext();
            MegaCrit.Sts2.Core.Helpers.TaskHelper.RunSafely(TriggerAsync(boss, context, "先见之明（她回合开始）"));
        }
        catch (Exception e) { Logger.Log("[CharBoss] 通晓万物：回合开始触发失败（不影响战斗）: " + e.Message); }
    }

    private static Monsters.CharBossMonster? _lastTurnBoss;
    private static int _lastTurnNumber = int.MinValue;

    /// <summary>她身上有没有「先见之明」（`WatcherMod.ForesightPower`，用类型名判，不硬引用观者 mod）。</summary>
    private static bool HasForesight(Creature? creature)
    {
        try
        {
            if (creature == null) return false;
            var type = WatcherCards.T("ForesightPower");
            if (type == null) return false;
            foreach (var power in creature.Powers)
                if (power != null && power.GetType() == type) return true;
        }
        catch { /* 忽略 */ }
        return false;
    }

    /// <summary>`WatcherCombatHelper.Scry(...)` 之后：恐惧模式下任何预见都触发。</summary>
    private static void ScryScopePostfix(object[] __args)
    {
        try
        {
            if (!DreadMode.IsActive) return;
            var player = FirstPlayerIn(__args);
            var boss = BossOfShadow(player);
            if (boss == null) return;
            var ctx = new BlockingPlayerChoiceContext();
            MegaCrit.Sts2.Core.Helpers.TaskHelper.RunSafely(TriggerAsync(boss, ctx, "预见"));
        }
        catch { /* 忽略 */ }
    }

    // ── 真正的结算 ─────────────────────────────────────────────────────────
    /// <summary>从**被锁定玩家**的抽牌堆随手弃 1 张。</summary>
    internal static async Task TriggerAsync(Monsters.CharBossMonster boss, PlayerChoiceContext? ctx, string source)
    {
        try
        {
            if (boss?.Creature == null) return;
            var combat = boss.CombatState;
            var target = BossLockOn.PureVictim(boss, combat);
            var pcs = target?.PlayerCombatState;
            var draw = pcs?.DrawPile?.Cards;
            if (draw == null || draw.Count == 0)
            {
                if (_logs++ < 12)
                    Logger.Log($"[CharBoss] 通晓万物（{source}）：{(target == null ? "没有锁定的玩家" : (target.Creature?.Name ?? "该玩家") + " 的抽牌堆是空的")} ⇒ 这次不弃");
                return;
            }

            int index = MegaCrit.Sts2.Core.Random.Rng.Chaotic.NextInt(draw.Count);
            var card = draw[Math.Max(0, Math.Min(index, draw.Count - 1))];
            if (card == null) return;

            await CardPileCmd.Add(card, PileType.Discard, CardPilePosition.Random, null, false);
            if (_logs++ < 12)
                Logger.Log($"[CharBoss] 通晓万物（{source}）：{(target!.Creature?.Name ?? "该玩家")} 抽牌堆里的 {SafeEntry(card)} 被弃掉了" +
                           $"（剩 {Math.Max(0, draw.Count)} 张）");
        }
        catch (Exception e) { Logger.Log("[CharBoss] 通晓万物结算失败（不影响战斗）: " + e.Message); }
    }

    private static string SafeEntry(CardModel? card)
    {
        try { return card?.Id.Entry ?? "?"; } catch { return "?"; }
    }

    /// <summary>「这个生物就是我们的某只 Boss」→ 那只 Boss。</summary>
    private static Monsters.CharBossMonster? BossOf(Creature? creature)
    {
        try
        {
            if (creature?.Monster is Monsters.CharBossMonster b) return b;
        }
        catch { /* 忽略 */ }
        return null;
    }

    /// <summary>「这位玩家的影子 = 我们的某只 Boss」→ 那只 Boss。</summary>
    private static Monsters.CharBossMonster? BossOfShadow(Player? player)
    {
        if (player == null) return null;
        try
        {
            var enemies = player.Creature?.CombatState?.Enemies;
            if (enemies == null) return null;
            foreach (var e in enemies)
            {
                if (e?.Monster is Monsters.CharBossMonster b && ReferenceEquals(b.Shadow, player)) return b;
            }
        }
        catch { /* 忽略 */ }
        return null;
    }

    private static Player? FirstPlayerIn(object[]? args)
    {
        if (args == null) return null;
        foreach (var a in args)
        {
            if (a is Player p) return p;
            if (a is IEnumerable<Player> many)
            {
                foreach (var q in many) return q;
            }
        }
        return null;
    }
}
#endif
