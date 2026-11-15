#if STS2_111   // 观者 Boss 只在 0.113 构建里（用户：107 不加这个 Boss）
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;

namespace CharacterBosses.Patches;

/// <summary>
/// **「许愿」固定选「永生不死」**（用户 2026-11-15：第 1 回合「许愿（选择**永生不死**）」）。
///
/// 反编译 `WatcherMod.WatcherWish_P.OnPlay`：
/// <code>
/// optStrength = CombatState.CreateCard&lt;WatcherWishAlmighty&gt;(Owner);
/// optArmor    = CombatState.CreateCard&lt;WatcherWishLiveForever&gt;(Owner);   // ← 永生不死
/// optGold     = CombatState.CreateCard&lt;WatcherWishFameAndFortune&gt;(Owner);
/// var chosen  = await WatcherCombatHelper.ChooseOne(ctx, Owner, options, prompt);
/// </code>
/// 也就是说选项是**三张临时卡**，玩家（这里是我们的影子玩家）通过 `ChooseOne` 挑一张，
/// 挑完再按挑中的那张 Apply 对应的 Power（永生不死 = `WishPlatedArmorPower`）。
///
/// 影子玩家的自动选牌器只会「随便挑一张」⇒ 这里在运行期给观者的
/// `WatcherCombatHelper.ChooseOne` 挂一个 prefix：**候选项里有 `WatcherWishLiveForever`
/// （只有「许愿」会开出这张）且 owner 是我们某只 Boss 的影子玩家**时，直接返回那一个。
/// 观者自己的玩家、其它卡（观者还有几十张牌也用 `ChooseOne`）**一律不受影响**。
///
/// 为什么走运行期挂补丁：观者是别人的模组，我们按铁律不能编译期引用它的类型，
/// 也就没法写 `[HarmonyPatch(typeof(WatcherCombatHelper), ...)]`。
/// 先例：<see cref="Core.MintySpireDamagePreviewCompat"/>。
/// </summary>
internal static class WatcherWishChoiceCompat
{
    private const string HelperTypeName = "WatcherCombatHelper";
    private const string MethodName = "ChooseOne";
    private const string LiveForeverTypeName = "WatcherWishLiveForever";

    private static bool _installed;
    private static bool _tried;
    private static int _logs;

    /// <summary>幂等；由每帧慢车道调用（观者模组可能比我们晚加载 ⇒ 要能重试）。</summary>
    internal static void TryInstall()
    {
        if (_installed) return;
        try
        {
            var helper = Core.WatcherCards.T(HelperTypeName);
            if (helper == null) return;                  // 没装观者模组：什么都不做

            var method = helper.GetMethod(MethodName,
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                null,
                new[]
                {
                    typeof(PlayerChoiceContext), typeof(Player),
                    typeof(IReadOnlyList<CardModel>), typeof(LocString), typeof(bool),
                },
                null);
            if (method == null)
            {
                if (!_tried) { _tried = true; Logger.Log("[CharBoss] 许愿固定选项：观者的 ChooseOne 签名不符（版本不同）⇒ 跳过"); }
                return;
            }

            var prefix = new HarmonyMethod(typeof(WatcherWishChoiceCompat)
                .GetMethod(nameof(Prefix), BindingFlags.Static | BindingFlags.NonPublic)!);
            new Harmony("CharacterBosses.watcher.wishchoice").Patch(method, prefix: prefix);
            _installed = true;
            Logger.Log("[CharBoss] 许愿固定选项：已挂上观者的 ChooseOne（只在候选项含「永生不死」时生效）");
        }
        catch (Exception e) { Logger.Log("[CharBoss] 许愿固定选项挂载失败: " + e.Message); }
    }

    /// <summary>
    /// ⚠ 参数名必须和对方方法**完全一样**（Harmony 按名字注入）；
    /// `__result` 是 `Task&lt;CardModel?&gt;`（可空引用在运行期擦除 ⇒ 这里写 `Task&lt;CardModel&gt;`）。
    /// </summary>
    private static bool Prefix(PlayerChoiceContext choiceContext, Player owner,
                               IReadOnlyList<CardModel> options, LocString prompt, bool cancelable,
                               ref Task<CardModel> __result)
    {
        try
        {
            if (options == null || options.Count == 0) return true;
            if (!IsOurBossShadow(owner)) return true;          // 只对神格观的影子玩家生效

            CardModel? liveForever = null;
            foreach (var opt in options)
            {
                if (opt == null) continue;
                string typeName;
                try { typeName = opt.GetType().Name; } catch { continue; }
                if (string.Equals(typeName, LiveForeverTypeName, StringComparison.Ordinal)) { liveForever = opt; break; }
            }
            if (liveForever == null) return true;               // 不是「许愿」的三选一 ⇒ 原样

            __result = Task.FromResult(liveForever);
            if (_logs++ < 3) Logger.Log("[CharBoss] 许愿：替她选了「永生不死」");
            return false;
        }
        catch { return true; }
    }

    /// <summary>这个 Player 是不是某只我们的 Boss 的影子玩家。</summary>
    private static bool IsOurBossShadow(Player owner)
    {
        try
        {
            if (owner == null) return false;
            var enemies = owner.Creature?.CombatState?.Enemies;
            if (enemies == null) return false;
            foreach (var c in enemies)
            {
                if (c?.Monster is not Monsters.CharBossMonster boss) continue;
                if (ReferenceEquals(boss.Shadow, owner)) return true;
            }
        }
        catch { /* 忽略 */ }
        return false;
    }
}
#endif
