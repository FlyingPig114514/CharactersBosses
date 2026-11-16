#if STS2_111   // 观者 Boss 只在 0.113 构建里（用户：107 不加这个 Boss）
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using CharacterBosses.Powers;

namespace CharacterBosses.Core;

/// <summary>
/// **观者模组的两处运行期补丁**（用户 2026-11-15）。
///
/// 为什么是「运行期」而不是 `[HarmonyPatch]`：目标类型在**别人的程序集**里（铁律：不编译期引用）。
/// 为什么放在 `Core` 命名空间而不是 `Patches`：`ModEntry` 会扫 `CharacterBosses.Patches` 下
/// 带 `[HarmonyPatch]` 的类去挂 —— 这两个类没有特性，留在那儿只会让挂载器抛一次无用的异常
/// （日志里那两行 `补丁类 … 挂载失败: HarmonyException: Patching exception in method null` 就是它）。
///
/// ## ① 进了神格就不再退出
/// 反编译 `WatcherMod.Divinity`：退出神格发生在它**自己的** `AfterSideTurnEnd` 覆写里
/// （`if (side == Owner.Side &amp;&amp; Owner.Player != null) await ExitStance(Owner.Player)`）。
/// ⚠ 这是**派生类的覆写**：补 `AbstractModel.AfterSideTurnEnd`（基类声明）根本拦不到它
/// —— 第一版就是这么写错的（日志：`Patching exception in method null`）。
/// 现在直接拿 `Divinity.AfterSideTurnEnd` 的 `MethodInfo` 补。
///
/// ## ② 许愿固定选「永生不死」
/// `WatcherWish_P.OnPlay` 会 `ChooseOne(ctx, owner, [Almighty, LiveForever, FameAndFortune], prompt)`，
/// 候选项里出现 `WatcherWishLiveForever` 就说明这张是「许愿」⇒ 直接返回它。
/// </summary>
internal static class WatcherRuntimePatches
{
    private static bool _tried;
    private static int _logs;

    internal static void TryInstall()
    {
        if (_tried) return;
        try
        {
            // 观者模组没装 / 还没加载 ⇒ 下次再试
            if (WatcherCards.ModAssembly() == null) return;

            bool any = InstallDivinityKeep();
            any |= InstallWishChoice();
            any |= InstallMantraTakeover();
            any |= InstallStanceDamageFallback();
            if (any) _tried = true;      // 都装上了才不再重试
        }
        catch (Exception e) { Logger.Log("[CharBoss] 观者运行期补丁安装失败: " + e.Message); }
    }

    // ── ④ 姿态倍率兜底（B 版）─────────────────────────────────────────────
    /// <summary>
    /// 让她在**愤怒 / 神格**下的真实伤害真的翻倍（B 版修复，用户 2026-11-16：
    /// 「她处于愤怒时造成的伤害没有翻倍」—— 日志实证第 7 回合打击真实只有 10 = (9+1)×1）。
    ///
    /// 根因：B 版的姿态倍率挂在 **Player 身上的 stance 模型**
    /// （`WrathStance : IModifyDamageMultiplicative`，判据 `dealer == base.Owner.Creature`），
    /// 而原版伤害管道只遍历「玩家集合 + 怪物身上的 listener」—— 我们的**影子玩家不在玩家集合里**
    /// （`BossFrameTickPatch` 之前那条 `_playerCollection` 问题同源）⇒ 她的 stance 永远收不到回调。
    /// A 版是挂在生物身上的 power，所以本来就生效（日志实证 A 版真实伤害 6+1 → 14）。
    ///
    /// 修法：只在 **B 版**上，给我们自己那只观者 Boss 的**出手伤害**补一次乘算
    /// （`Hook.ModifyDamage` 的 postfix，`modifyDamageHookType == All` 那一次）。
    /// 预览走的是同一条 `Hook.ModifyDamage` ⇒ 显示与实伤自动一致，`ScaleToPlannedStance` 那层仍然只补差值。
    /// </summary>
    private static bool InstallStanceDamageFallback()
    {
        try
        {
            if (WatcherCards.Current != WatcherCards.Provider.Lamali)
            {
                Logger.Log("[CharBoss] 姿态倍率兜底：A 版原生生效 ⇒ 不装");
                return true;
            }
            var method = typeof(MegaCrit.Sts2.Core.Hooks.Hook).GetMethod(
                "ModifyDamage",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[]
                {
                    typeof(MegaCrit.Sts2.Core.Runs.IRunState),
                    typeof(MegaCrit.Sts2.Core.Combat.ICombatState),
                    typeof(Creature), typeof(Creature), typeof(decimal),
                    typeof(MegaCrit.Sts2.Core.ValueProps.ValueProp),
                    typeof(MegaCrit.Sts2.Core.Models.CardModel),
                    typeof(MegaCrit.Sts2.Core.Entities.Cards.CardPlay),
                    typeof(MegaCrit.Sts2.Core.Hooks.ModifyDamageHookType),
                    typeof(MegaCrit.Sts2.Core.Entities.Cards.CardPreviewMode),
                    typeof(System.Collections.Generic.IEnumerable<MegaCrit.Sts2.Core.Models.AbstractModel>).MakeByRefType(),
                },
                null);
            if (method == null)
            {
                Logger.Log("[CharBoss] 姿态倍率兜底：找不到 Hook.ModifyDamage ⇒ 跳过");
                return true;
            }
            var postfix = new HarmonyMethod(typeof(WatcherRuntimePatches)
                .GetMethod(nameof(StanceDamagePostfix), BindingFlags.Static | BindingFlags.NonPublic)!);
            new Harmony("CharacterBosses.watcher.stancedamage").Patch(method, postfix: postfix);
            Logger.Log("[CharBoss] 姿态倍率兜底：已挂 Hook.ModifyDamage（B 版愤怒/神格的伤害乘算由我们补）");
            return true;
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 姿态倍率兜底：挂载失败: " + e.Message);
            return true;
        }
    }

    private static void StanceDamagePostfix(Creature? dealer, decimal damage,
                                           MegaCrit.Sts2.Core.ValueProps.ValueProp props,
                                           MegaCrit.Sts2.Core.Hooks.ModifyDamageHookType modifyDamageHookType,
                                           ref decimal __result)
    {
        try
        {
            if (__result <= 0 || damage <= 0) return;
            if (modifyDamageHookType != MegaCrit.Sts2.Core.Hooks.ModifyDamageHookType.All) return;
            if (((int)props & 4) != 0) return;                 // Unpowered 一类不吃姿态
            if (dealer == null) return;
            if (dealer.Monster is not Monsters.CharBossMonster boss) return;
            string key = string.Empty;
            try { key = boss.Def?.Key ?? string.Empty; } catch { return; }
            if (!key.StartsWith("WATCHER_", StringComparison.Ordinal)) return;

            int mul = BossIntentBuilder.StanceMultiplierOf(dealer);
            if (mul <= 1) return;
            __result *= mul;
            if (_logs2++ < 6)
                Logger.Log($"[CharBoss] 姿态倍率兜底：{key} 出手 {damage:0.##} ×{mul} ⇒ {__result:0.##}");
        }
        catch { /* 忽略 */ }
    }

    private static int _logs2;

    // ── ① 神格不退 ─────────────────────────────────────────────────────────
    private static bool InstallDivinityKeep()
    {
        try
        {
            // 两条路：A 的 `Divinity.AfterSideTurnEnd`（回合结束退）、B 的 `DivinityStance.BeforeSideTurnStart`（回合开始退）
            var type = WatcherCards.T("Divinity") ?? WatcherCards.T("DivinityStance");
            if (type == null) return false;
            var method = type.GetMethod("AfterSideTurnEnd",
                            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                         ?? type.GetMethod("BeforeSideTurnStart",
                            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (method == null)
            {
                Logger.Log("[CharBoss] 神格不退：Divinity 上没有 AfterSideTurnEnd（版本不同）⇒ 跳过");
                return true;      // 不再重试
            }

            var prefix = new HarmonyMethod(typeof(WatcherRuntimePatches)
                .GetMethod(nameof(DivinityKeepPrefix), BindingFlags.Static | BindingFlags.NonPublic)!);
            new Harmony("CharacterBosses.watcher.divinitykeep").Patch(method, prefix: prefix);
            Logger.Log("[CharBoss] 神格不退：已挂上观者的 Divinity.AfterSideTurnEnd");
            return true;
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 神格不退：挂载失败: " + e.Message);
            return true;
        }
    }

    /// <summary>参数名要跟目标一致（Harmony 按名字注入）；`__instance` 是那个 Divinity power。</summary>
    private static bool DivinityKeepPrefix(object __instance,
                                           PlayerChoiceContext choiceContext,
                                           CombatSide side,
                                           IEnumerable<Creature> participants,
                                           ref Task __result)
    {
        try
        {
            var power = __instance as PowerModel;
            if (power == null) return true;

            Creature? owner = null;
            try { owner = power.Owner; } catch { return true; }
            if (owner == null) return true;

            var boss = OurBossOwning(owner);
            if (boss == null) return true;
            try { if (boss.Creature?.GetPower<BossDevotionPower>() == null) return true; }
            catch { return true; }

            __result = Task.CompletedTask;
            if (_logs++ < 3)
                Logger.Log("[CharBoss] 神格观：她进了神格 ⇒ 压掉原版的「回合结束退神格」");
            return false;
        }
        catch { return true; }
    }

    // ── ② 许愿固定选「永生不死」 ────────────────────────────────────────────
    private static bool InstallWishChoice()
    {
        try
        {
            var helper = WatcherCards.T("WatcherCombatHelper");
            if (helper == null)
            {
                if (WatcherCards.Current != WatcherCards.Provider.None)
                    Logger.Log("[CharBoss] 许愿固定选项：当前观者实现没有 `WatcherCombatHelper.ChooseOne`（lamali 版）⇒ 沿用该模组自己的选法");
                return true;      // 不再重试
            }
            var method = helper.GetMethod("ChooseOne",
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
                Logger.Log("[CharBoss] 许愿固定选项：观者的 ChooseOne 签名不符 ⇒ 跳过");
                return true;
            }

            var prefix = new HarmonyMethod(typeof(WatcherRuntimePatches)
                .GetMethod(nameof(WishChoicePrefix), BindingFlags.Static | BindingFlags.NonPublic)!);
            new Harmony("CharacterBosses.watcher.wishchoice").Patch(method, prefix: prefix);
            Logger.Log("[CharBoss] 许愿固定选项：已挂上观者的 ChooseOne（只在候选项含「永生不死」时生效）");
            return true;
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 许愿固定选项：挂载失败: " + e.Message);
            return true;
        }
    }

    private static bool WishChoicePrefix(PlayerChoiceContext choiceContext, Player owner,
                                         IReadOnlyList<CardModel> options, LocString prompt, bool cancelable,
                                         ref Task<CardModel> __result)
    {
        try
        {
            if (options == null || options.Count == 0) return true;
            if (!IsOurBossShadowPlayer(owner)) return true;

            CardModel? liveForever = null;
            foreach (var opt in options)
            {
                if (opt == null) continue;
                string typeName;
                try { typeName = opt.GetType().Name; } catch { continue; }
                if (string.Equals(typeName, "WatcherWishLiveForever", StringComparison.Ordinal))
                { liveForever = opt; break; }
            }
            if (liveForever == null) return true;

            __result = Task.FromResult(liveForever);
            if (_logs++ < 6) Logger.Log("[CharBoss] 许愿：替她选了「永生不死」");
            return false;
        }
        catch { return true; }
    }

    // ── ③ 真言满 10 层的触发改由我们做（用户 2026-11-16 第 3 条）────────────────
    /// <summary>
    /// 用户原话：
    /// > 「我在特殊效果里写了，她获得 10 层真言，会先退出当前姿态，再进入神格，
    /// >   这是为了保证她已处在神格时玩家再打满 10 张牌，观者也可以加费和获得力量，
    /// >   但现在再进神格的力量和加费都没有。」
    ///
    /// 根因：满 10 层是**观者模组自己**触发的 —— A 版在 `Mantra.AfterPowerAmountChangedCompat` 里
    /// 直接 `EnterDivinity`（`ChangeStance&lt;Divinity&gt;` 见到「已经在神格里」就**原样返回**，
    /// 于是既不退姿态也没有那 3 点能量），B 版同理（`MantraPower.AfterPowerAmountChanged`）。
    ///
    /// 所以这里把**模组那条自动触发**压掉，改由 `BossDevotionPower` 自己按用户口径结算：
    /// 扣 10 层 → 先退姿态 → 给力量 → 再进神格（原版 `EnterDivinity` 自己 +3 能量）。
    /// 压不掉时（版本改名）也不会双份：模组先触发就会把真言扣到 10 以下，我们那段循环自然不跑。
    /// </summary>
    private static bool InstallMantraTakeover()
    {
        try
        {
            // A：`Mantra.AfterPowerAmountChangedCompat(PowerModel, decimal, Creature?, CardModel?)`
            // B：`MantraPower.AfterPowerAmountChanged(ctx, PowerModel, decimal, Creature?, CardModel?)`
            var type = WatcherCards.T("Mantra") ?? WatcherCards.T("MantraPower");
            if (type == null)
            {
                Logger.Log("[CharBoss] 真言接管：找不到观者的 Mantra / MantraPower ⇒ 沿用模组自己的触发");
                return true;
            }
            MethodInfo? method = null;
            foreach (var name in new[] { "AfterPowerAmountChangedCompat", "AfterPowerAmountChanged" })
            {
                method = type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (method != null) break;
            }
            if (method == null)
            {
                Logger.Log("[CharBoss] 真言接管：Mantra 上没有 AfterPowerAmountChanged(Compat) ⇒ 沿用模组自己的触发");
                return true;
            }

            var prefix = new HarmonyMethod(typeof(WatcherRuntimePatches)
                .GetMethod(nameof(MantraTakeoverPrefix), BindingFlags.Static | BindingFlags.NonPublic)!);
            new Harmony("CharacterBosses.watcher.mantratakeover").Patch(method, prefix: prefix);
            Logger.Log($"[CharBoss] 真言接管：已压掉观者的 {type.Name}.{method.Name}" +
                       "（满 10 层改由我们自己「退姿态 → 力量 → 进神格」）");
            return true;
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 真言接管：挂载失败: " + e.Message);
            return true;
        }
    }

    private static bool MantraTakeoverPrefix(object __instance, object[] __args, ref Task __result)
    {
        try
        {
            var power = __instance as PowerModel;
            if (power == null) return true;
            Creature? owner = null;
            try { owner = power.Owner; } catch { return true; }
            if (!IsOurWatcherShadow(owner)) return true;

            __result = Task.CompletedTask;

            // ★ v0.2.701（用户 2026-11-16，B 版实测）：「她获得 10 真言没有进入神格」。
            //   以前这里**只**把模组那条自动进神格压掉，然后指望我们自己的
            //   `BossDevotionPower.GainMantraAndResolveAsync`（只覆盖「我们给她加真言」的两条路：
            //   玩家出牌 + 虔信）去结算 —— 而她**自己的牌**给的真言完全不经过我们 ⇒ 没人结算。
            //   现在压掉之后**顺手把结算自己跑一遍**：任何来源的真言满 10 层都会走
            //   「扣 10 → 退姿态 → 力量 → 进神格」。A 版 / B 版同一条路。
            try
            {
                var boss = OurBossOwning(owner!);
                var shadow = owner?.Player;
                var ctx = default(MegaCrit.Sts2.Core.GameActions.Multiplayer.PlayerChoiceContext);
                if (__args != null)
                {
                    foreach (var a in __args)
                    {
                        if (a is MegaCrit.Sts2.Core.GameActions.Multiplayer.PlayerChoiceContext p)
                        { ctx = p; break; }
                    }
                }
                if (boss != null && shadow != null)
                {
                    MegaCrit.Sts2.Core.Helpers.TaskHelper.RunSafely(
                        BossDevotionPower.ResolveDivinityAsync(boss, shadow, ctx));
                }
            }
            catch (Exception e) { Logger.Log("[CharBoss] 真言接管：自己结算满 10 层失败: " + e.Message); }

            if (_logs++ < 4)
                Logger.Log("[CharBoss] 真言接管：压掉模组那条自动进神格 ⇒ 改由我们自己「退姿态 → 力量 → 进神格」");
            return false;
        }
        catch { return true; }
    }

    // ── 注意：进神格不压「平静退姿态」的 +2（用户 2026-11-16 傍晚订正）──────────────
    //   「哦那个第二回合我忘记算退平静了，不是bug，让那2费正常加」
    //   ⇒ 离开平静的 +2 能量是她的原版机制（Calm.AfterRemoved / CalmStance.OnExitStance），
    //     不管是我们触发的退姿态还是「化拳为空」都照常给，不要压。

    /// <summary>她是不是我们那只观者的影子玩家（且那只身上挂着「虔信」）。</summary>
    private static bool IsOurWatcherShadow(Creature? creature)
    {
        try
        {
            if (creature == null) return false;
            var boss = OurBossOwning(creature);
            if (boss == null) return false;
            return boss.Creature?.GetPower<BossDevotionPower>() != null;
        }
        catch { return false; }
    }

    // ── 共用判定 ──────────────────────────────────────────────────────────
    private static Monsters.CharBossMonster? OurBossOwning(Creature owner)
    {
        try
        {
            var enemies = owner.CombatState?.Enemies;
            if (enemies == null) return null;
            foreach (var c in enemies)
            {
                if (c?.Monster is not Monsters.CharBossMonster boss) continue;
                var shadowCreature = boss.Shadow?.Creature;
                if (shadowCreature != null && ReferenceEquals(shadowCreature, owner)) return boss;
            }
        }
        catch { /* 忽略 */ }
        return null;
    }

    private static bool IsOurBossShadowPlayer(Player owner)
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
