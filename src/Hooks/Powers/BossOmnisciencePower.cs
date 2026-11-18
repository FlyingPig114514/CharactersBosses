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
/// · **普通模式**：`WatcherMod.ForesightPower.BeforeHandDrawCompat` —— 她的「先见之明」
///   （`FORESIGHT_POWER`，「每回合开始预见 3」）每次触发时都会走这里。
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

            // ① 普通模式：先见之明的回合开始触发
            var foresightPower = WatcherCards.T("ForesightPower");
            var hook = foresightPower?.GetMethod("BeforeHandDrawCompat",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (hook != null)
            {
                var fix = new HarmonyMethod(typeof(BossOmnisciencePower)
                    .GetMethod(nameof(ForesightScopePostfix), BindingFlags.Static | BindingFlags.NonPublic)!);
                PatchSafe("CharacterBosses.watcher.omniscience.foresight", hook, fix);
                okAny = true;
            }

            // ② 恐惧模式：任何「预见」（Scry）都算
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

            if (okAny)
            {
                _installed = true;
                Logger.Log("[CharBoss] 通晓万物：已挂上触发钩子（先见之明 / 恐惧模式下的任意预见）");
            }
            else if (_installAttempts <= 3)
            {
                Logger.Log("[CharBoss] 通晓万物：暂时没找到观者的 ForesightPower / Scry 入口 ⇒ 稍后重试");
            }
        }
        catch (Exception e) { Logger.Log("[CharBoss] 通晓万物：挂钩失败: " + e.Message); }
    }

    private static void PatchSafe(string id, MethodBase target, HarmonyMethod postfix)
    {
        try { new Harmony(id).Patch(target, postfix: postfix); }
        catch (Exception e) { Logger.Log($"[CharBoss] 通晓万物：{id} 挂载失败: {e.Message}"); }
    }

    /// <summary>`ForesightPower.BeforeHandDrawCompat(...)` 之后：她这次「先见之明」结算了 ⇒ 触发。</summary>
    private static void ForesightScopePostfix(object __instance)
    {
        try
        {
            if (DreadMode.IsActive) return;                  // 恐惧模式统一走 Scry 那条（避免双份）
            var power = __instance as PowerModel;
            var boss = BossOf(power?.Owner);
            if (boss == null) return;
            var ctx = new BlockingPlayerChoiceContext();
            MegaCrit.Sts2.Core.Helpers.TaskHelper.RunSafely(TriggerAsync(boss, ctx, "先见之明"));
        }
        catch { /* 忽略 */ }
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
