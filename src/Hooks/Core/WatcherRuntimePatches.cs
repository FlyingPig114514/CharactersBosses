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
            any |= InstallFastingNoEnergyDown();
            any |= InstallFlurryNoReturn();
            any |= InstallPureWaterNoDouble();
            if (any) _tried = true;      // 都装上了才不再重试
        }
        catch (Exception e) { Logger.Log("[CharBoss] 观者运行期补丁安装失败: " + e.Message); }
    }

    // ── ⑥ 疾风连击不再「转换姿态时从弃牌堆回手」（用户 2026-11-23）──────────────
    /// <summary>
    /// 用户原话：「**拦掉疾风连击的回手**」。
    ///
    /// 两份观者 mod 的实现位置不同，两条都压：
    /// · **A 版（3747526116）**：写在姿态切换的总入口
    ///   `WatcherCombatHelper.OnStanceChanged(Player, Type, Type)` 里 ——
    ///   它把**弃牌堆**里所有 `WatcherFlurryOfBlows` 挪回手牌（反编译实证）。
    ///   对她整段跳过（她身上没有紫莲 `VIOLET_LOTUS`，这个方法的另一半是紫莲抽牌，跳过无副作用）；
    /// · **B 版（3747492505）**：挂在卡自己的 `FlurryOfBlows.OnStanceChange(...)` 上，直接压掉。
    ///
    /// 只对我们的 **疾风观**（`WATCHER_FLURRY`）生效 —— 别的观者设计/w 真人玩家一点都不变。
    /// </summary>
    private static bool InstallFlurryNoReturn()
    {
        bool ok = false;

        // A 版：姿态切换总入口
        try
        {
            var helper = WatcherCards.T("WatcherCombatHelper");
            var method = helper?.GetMethod("OnStanceChanged",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (method != null)
            {
                var prefix = new HarmonyMethod(typeof(WatcherRuntimePatches)
                    .GetMethod(nameof(FlurryNoReturnStancePrefix), BindingFlags.Static | BindingFlags.NonPublic)!);
                new Harmony("CharacterBosses.watcher.flurrnoreturn.a").Patch(method, prefix: prefix);
                Logger.Log("[CharBoss] 疾风连击不回手：已压掉 A 版 WatcherCombatHelper.OnStanceChanged（只对她）");
                ok = true;
            }
        }
        catch (Exception e) { Logger.Log("[CharBoss] 疾风连击不回手（A 版）挂载失败: " + e.Message); }

        // B 版：卡自己的 OnStanceChange
        try
        {
            var cardType = WatcherCards.FlurryOfBlows;
            var method = cardType?.GetMethod("OnStanceChange",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (method != null)
            {
                var prefix = new HarmonyMethod(typeof(WatcherRuntimePatches)
                    .GetMethod(nameof(FlurryNoReturnCardPrefix), BindingFlags.Static | BindingFlags.NonPublic)!);
                new Harmony("CharacterBosses.watcher.flurrnoreturn.b").Patch(method, prefix: prefix);
                Logger.Log("[CharBoss] 疾风连击不回手：已压掉 B 版 FlurryOfBlows.OnStanceChange（只对她）");
                ok = true;
            }
        }
        catch (Exception e) { Logger.Log("[CharBoss] 疾风连击不回手（B 版）挂载失败: " + e.Message); }

        if (!ok) Logger.Log("[CharBoss] 疾风连击不回手：两份实现都没找到姿态切换钩子 ⇒ 跳过");
        return true;
    }

    /// <summary>A 版：`OnStanceChanged(Player owner, Type oldStance, Type newStance)`。</summary>
    private static bool FlurryNoReturnStancePrefix(Player owner, ref Task __result)
    {
        try
        {
            if (!IsFlurryBossShadowPlayer(owner)) return true;
            __result = Task.CompletedTask;
            if (_logs5++ < 4)
                Logger.Log("[CharBoss] 疾风连击不回手：她转换姿态 ⇒ 压掉「弃牌堆里的疾风连击回手」");
            return false;
        }
        catch { return true; }
    }

    /// <summary>B 版：`FlurryOfBlows.OnStanceChange(...)`，`__instance` 是那张牌。</summary>
    private static bool FlurryNoReturnCardPrefix(object __instance, ref Task __result)
    {
        try
        {
            var card = __instance as CardModel;
            if (card == null) return true;
            if (!IsFlurryBossShadowPlayer(card.Owner)) return true;
            __result = Task.CompletedTask;
            if (_logs5++ < 4)
                Logger.Log("[CharBoss] 疾风连击不回手：她转换姿态 ⇒ 压掉 B 版卡上的回手");
            return false;
        }
        catch { return true; }
    }

    private static int _logs5;

    /// <summary>这个 Player 是不是**疾风观**那只 Boss 的影子玩家（只认设计 Key `WATCHER_FLURRY`）。</summary>
    private static bool IsFlurryBossShadowPlayer(Player? player)
    {
        try
        {
            if (player == null) return false;
            var enemies = player.Creature?.CombatState?.Enemies;
            if (enemies == null) return false;
            foreach (var c in enemies)
            {
                if (c?.Monster is not Monsters.CharBossMonster boss) continue;
                if (!ReferenceEquals(boss.Shadow, player)) continue;
                // ★ v0.2.913（用户 2026-11-23：「疾风连击的回手还是没有拦掉」）：
                //   这里以前比的是 `boss.Def?.Key` —— 而**变体**的 `Def` 是
                //   `DefSource.WithVariantPin(key)` 出来的副本，`Key` 仍是主定义的
                //   `WATCHER_DIVINITY`，变体的 key 只写在 `PinnedVariantKey` 上
                //   ⇒ 这个判定永远 false ⇒ 前缀一直放行 ⇒ 回手从来没被拦掉。
                string variant = boss.PinnedVariantKey ?? boss.SpawnVariantKey ?? string.Empty;
                if (string.Equals(variant, "WATCHER_FLURRY", StringComparison.Ordinal)) return true;
                string key = string.Empty;
                try { key = boss.Def?.Key ?? string.Empty; } catch { key = string.Empty; }
                return string.Equals(key, "WATCHER_FLURRY", StringComparison.Ordinal);
            }
        }
        catch { /* 忽略 */ }
        return false;
    }

    // ── ⑤ 斋戒不再扣她自己的能量（用户 2026-11-23「疾风观」）─────────────────────
    /// <summary>
    /// 用户原话（2026-11-23）：
    /// > 「修改特殊效果：**斋戒不再会使观者在回合开始时失去能量**。……（后面不变）
    /// >  PS：这意味着她第六回合和第八回合能多打一张打击」
    ///
    /// 根因：两份观者 mod 的「斋戒」除了给力量/敏捷，还会给持有者自己叠一层
    /// `EnergyDownPower`（反编译：`Type = Debuff`、`StackType = Counter`、
    /// `AfterEnergyReset(player)` 里 `PlayerCmd.LoseEnergy(Amount, player)`）。
    /// 而 `AfterEnergyReset` 对我们这只 Boss **是派发的**（所以 `BossRadianceSuppressPatch`
    /// 才必须手动压掉明耀酊剂那一条），结果她第 3 回合打出斋戒之后，每回合都被扣 1 点能量
    /// ⇒ 剧本里写死的每回合能量全部少 1，第 6/8 回合本该打得出的牌全变成「没费」。
    ///
    /// 修法：把 `EnergyDownPower.AfterEnergyReset` 对她的那一次**压掉**（真玩家的斋戒一点不变）。
    /// 只认「持有者是我们的观者 Boss」⇒ 其它角色 Boss / 真人玩家都不受影响。
    /// </summary>
    private static bool InstallFastingNoEnergyDown()
    {
        bool ok = false;

        // ── A 版（Boninall）：斋戒给她叠一层 `EnergyDownPower`，扣费在 `AfterEnergyReset` ──
        try
        {
            var type = WatcherCards.T("EnergyDownPower");
            var method = type?.GetMethod("AfterEnergyReset",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (method != null)
            {
                var prefix = new HarmonyMethod(typeof(WatcherRuntimePatches)
                    .GetMethod(nameof(FastingNoEnergyDownPrefix), BindingFlags.Static | BindingFlags.NonPublic)!);
                new Harmony("CharacterBosses.watcher.fastingnoenergydown").Patch(method, prefix: prefix);
                Logger.Log($"[CharBoss] 斋戒不扣能量：已挂上 {type!.Name}.AfterEnergyReset（A 版，只对她生效）");
                ok = true;
            }
        }
        catch (Exception e) { Logger.Log("[CharBoss] 斋戒不扣能量（A 版）挂载失败: " + e.Message); }

        // ── B 版（lamali）：斋戒叠的是**另一个** power `FastingPower`（Buff），
        //    扣费在 `AfterPlayerTurnStart(ctx, player)` 里 `Owner.Player.PlayerCombatState.LoseEnergy(Amount)`。
        //    ★ v0.2.914（用户 2026-11-23：「B 版中恐惧模式下没有免疫 debuff 斋戒（这和 A 版的能量降低
        //      不是同一个 buff，虽然效果一样）」「B 版有的回合斋戒的减费好像被重复计算了」）
        //      ⇒ 这条以前完全没管，B 版每回合照样扣（还可能被派发两次）。
        try
        {
            var type = WatcherCards.T("FastingPower");
            var method = type?.GetMethod("AfterPlayerTurnStart",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (method != null)
            {
                var prefix = new HarmonyMethod(typeof(WatcherRuntimePatches)
                    .GetMethod(nameof(FastingNoEnergyDownPrefix), BindingFlags.Static | BindingFlags.NonPublic)!);
                new Harmony("CharacterBosses.watcher.fastingnoenergydown.b").Patch(method, prefix: prefix);
                Logger.Log($"[CharBoss] 斋戒不扣能量：已挂上 {type!.Name}.AfterPlayerTurnStart（B 版，只对她生效）");
                ok = true;
            }
        }
        catch (Exception e) { Logger.Log("[CharBoss] 斋戒不扣能量（B 版）挂载失败: " + e.Message); }

        if (!ok) Logger.Log("[CharBoss] 斋戒不扣能量：两版都没找到可压的扣费钩子 ⇒ 跳过（靠 PrepareTurn 里的兜底摘除）");
        return true;
    }

    private static bool FastingNoEnergyDownPrefix(object __instance, ref Task __result)
    {
        try
        {
            var power = __instance as PowerModel;
            if (power == null) return true;
            Creature? owner = null;
            try { owner = power.Owner; } catch { return true; }
            if (owner == null) return true;
            if (OurBossOwning(owner) == null) return true;      // 不是我们的 Boss ⇒ 走原版

            __result = Task.CompletedTask;
            if (_logs4++ < 4)
                Logger.Log("[CharBoss] 斋戒：压掉「她每回合开始少 1 能量」（用户 2026-11-23 订正）");
            return false;
        }
        catch { return true; }
    }

    /// <summary>
    /// **兜底**：把「能量降低」（`EnergyDownPower`）从她身上摘掉。
    ///
    /// 为什么除了 Harmony 前缀还要这一手：用户 2026-11-23 实测「她还是没有免疫斋戒给自己上的能量降低」——
    /// 前缀只在「观者 mod 的程序集已加载 + 补丁真的挂上」时才有效，而那条链一旦断（加载次序、
    /// 类型改名）用户是看不出来的。这里在 `PrepareTurn`（**玩家回合开始、她的能量刚算完**）里
    /// 直接把这类 power 摘掉，下一回合她的 `AfterEnergyReset` 就没东西可扣了 ——
    /// 与 Harmony 那条互不依赖，任意一条生效她就免疫。
    /// </summary>
    internal static void StripFastingEnergyDown(Monsters.CharBossMonster? boss)
    {
        try
        {
            var creature = boss?.Creature;
            if (creature == null) return;

            // ★ v0.2.914：两个版本各有一个「斋戒扣费」power ——
            //   A 版 = `EnergyDownPower`，B 版 = `FastingPower`（名字、类型都不一样，效果相同）。
            var types = new List<Type>();
            var a = WatcherCards.T("EnergyDownPower");
            if (a != null) types.Add(a);
            var b = WatcherCards.T("FastingPower");
            if (b != null && b != a) types.Add(b);
            if (types.Count == 0) return;

            List<PowerModel>? doomed = null;
            foreach (var p in creature.Powers)
            {
                if (p == null) continue;
                if (!types.Contains(p.GetType())) continue;
                (doomed ??= new List<PowerModel>()).Add(p);
            }
            if (doomed == null) return;

            foreach (var p in doomed)
            {
                try { _ = MegaCrit.Sts2.Core.Commands.PowerCmd.Remove(p); } catch { /* 忽略 */ }
            }
            if (_logs6++ < 6)
                Logger.Log($"[CharBoss] 斋戒兜底：摘掉她身上的「斋戒扣费」×{doomed.Count}（她免疫这一条）");
        }
        catch (Exception e) { Logger.Log("[CharBoss] 斋戒兜底失败: " + e.Message); }
    }

    // ── ⑦ 至纯之水：改由我们自己在她第 1 回合的手牌建好之后补「奇迹」（用户 2026-11-23）──────
    /// <summary>
    /// 用户原话（2026-11-23）：「她的至纯之水应该在**战斗开始**时就给她手里放一张奇迹，
    /// 而不是她第一回合开始时才放」。
    ///
    /// 为什么以前是「她第一回合开始时」才放：A 版（Boninall）的 `PureWater.BeforeCombatStart()`
    /// 给持有者印一张「奇迹」，而那个钩子对我们的影子玩家派发得**很晚**（在她自己回合开始之后），
    /// 于是玩家在自己的第 1 回合看到的她的手牌里没有奇迹。
    /// 我们自己那条注入（`BossHookBridge` → `WatcherCards.EnsurePureWaterMiracle`）当时
    /// **只对 B 版**启用（A 版「原版自己会印」），所以 A 版就走了那条晚路径。
    ///
    /// 现在：① 我们自己**两个版本都注入**（在她第 1 回合手牌建好之后 —— 那正是玩家第 1 回合
    /// 刚开始的时刻，手牌栏立刻就能看到 4 张）；② 这里把模组自己那次印牌**压掉**，避免变两张。
    /// 只对她（我们的影子玩家）生效，真人玩家拿至纯之水一点不变。
    /// </summary>
    private static bool InstallPureWaterNoDouble()
    {
        var type = WatcherCards.PureWater;
        if (type == null)
        {
            Logger.Log("[CharBoss] 至纯之水去重：当前观者实现取不到 PureWater ⇒ 跳过");
            return true;
        }

        bool ok = false;
        foreach (var name in new[] { "BeforeCombatStart", "BeforeHandDraw" })
        {
            try
            {
                var method = type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (method == null) continue;
                // 只补返回 Task 的那条（void 的挂不上 `ref Task __result`）
                if (method.ReturnType != typeof(Task)) continue;

                var prefix = new HarmonyMethod(typeof(WatcherRuntimePatches)
                    .GetMethod(nameof(PureWaterNoDoublePrefix), BindingFlags.Static | BindingFlags.NonPublic)!);
                new Harmony("CharacterBosses.watcher.purewater." + name).Patch(method, prefix: prefix);
                Logger.Log($"[CharBoss] 至纯之水去重：已压掉 {type.Name}.{name}（她的奇迹改由我们注入）");
                ok = true;
            }
            catch (Exception e) { Logger.Log($"[CharBoss] 至纯之水去重：{name} 挂载失败: " + e.Message); }
        }

        if (!ok) Logger.Log("[CharBoss] 至纯之水去重：没找到可压的印牌钩子（可能改了名）");
        return true;
    }

    private static bool PureWaterNoDoublePrefix(object __instance, ref Task __result)
    {
        try
        {
            var relic = __instance as RelicModel;
            Player? owner = null;
            try { owner = relic?.Owner; } catch { owner = null; }
            if (owner == null || !IsOurBossShadowPlayer(owner)) return true;   // 真人玩家：走原版

            __result = Task.CompletedTask;
            if (!_pureWaterSuppressedOnce)
            {
                _pureWaterSuppressedOnce = true;
                Logger.Log("[CharBoss] 至纯之水：压掉模组自己那次印牌（奇迹由我们在她第 1 回合手牌建好后注入）");
            }
            return false;
        }
        catch { return true; }
    }

    private static bool _pureWaterSuppressedOnce;

    private static int _logs6;

    private static int _logs4;

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

    private static void StanceDamagePostfix(Creature? dealer, Creature? target, decimal damage,
                                           MegaCrit.Sts2.Core.ValueProps.ValueProp props,
                                           MegaCrit.Sts2.Core.Hooks.ModifyDamageHookType modifyDamageHookType,
                                           ref decimal __result)
    {
        try
        {
            if (__result <= 0 || damage <= 0) return;
            if (modifyDamageHookType != MegaCrit.Sts2.Core.Hooks.ModifyDamageHookType.All) return;
            if (((int)props & 4) != 0) return;                 // Unpowered 一类不吃姿态

            // ★ v0.2.714（用户 2026-11-16「印牌观」）：「她在处于愤怒时**受到**的攻击伤害没有翻倍」。
            //   原版愤怒（Wrath）是**双向**的 —— 造成与受到都 ×2。而 B 版（lamali）的姿态倍率挂在
            //   `WrathStance.ModifyDamageMultiplicative`，判据是 `dealer == Owner.Creature`
            //   ⇒ **只管她出手**，玩家打她那一侧没人翻 ⇒ 这里补「她挨打」那一半。
            //   · 只有**愤怒**翻倍（神格 ×3 是她出手的事，受击不翻，与原版一致）；
            //   · 只对真人玩家的攻击生效（她自己的牌、无来源伤害不受影响）。
            if (target != null && target.Monster is Monsters.CharBossMonster victim)
            {
                string vkey = string.Empty;
                try { vkey = victim.Def?.Key ?? string.Empty; } catch { vkey = string.Empty; }
                if (vkey.StartsWith("WATCHER_", StringComparison.Ordinal)
                    && WatcherCards.StanceModelName(victim.Shadow) == "WrathStance")
                {
                    __result *= 2;
                    if (_logs3++ < 6)
                        Logger.Log($"[CharBoss] 姿态倍率兜底：{vkey} 在愤怒中**受到** {damage:0.##} ×2 ⇒ {__result:0.##}");
                    return;
                }
            }

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
    private static int _logs3;

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
