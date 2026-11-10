using System;
using MegaCrit.Sts2.Core.Helpers;
using CharacterBosses;

namespace CharacterBosses.Core;

/// <summary>
/// 构建指纹 + 一次性启动日志。
///
/// 用途：
///   · `Tag` 写进日志与上传数据的 `mod` 字段 ⇒ 拿到玩家日志就能确认「他跑的是哪一版」；
///   · <see cref="LogOnce"/> 由 `BossFrameTickPatch` 在第一帧调一次（那时所有程序集都加载完了），
///     顺手装「薄荷尖塔」的伤害预测兼容（见 <c>Core.MintySpireDamagePreviewCompat</c>）。
///
/// ⚠ 改版本时**两处一起改**：这里 + `src/CharacterBosses.json` 的 `version`。
/// </summary>
public static class BuildInfo
{
    /// <summary>构建指纹（日志/数据表里用）。每次改动都要 +0.0.1 并写一句改了什么。</summary>
    public const string Tag = "v0.2.554 / 2026-11-10（**诊断版**：查「玩家给 Boss 上负面时，头上不浮图标、也没有施加音效」。反编译 `NCreature.OnPowerIncreased` 第一行是 `if (silent || !CombatManager.IsInProgress) return;`（图标+音效一起没），而音效另外还要求 `power.ShouldPlayVfx`（= `Owner.IsAlive && IsInProgress && IsVisible`，`IsVisible` = `Target==null || IsMe(Target) || Target.IsEnemy`）。新增 `BossPowerApplyTracePatch`：只在 `PowerCmd.Apply` 的目标是 CharacterBosses 的 Boss 时打一行 `PowerApply 诊断：<power> <amount> → <她>（施加者=… silent=… IsInProgress=… ShouldPlayVfx=… IsMe=… Side=…）`（限 40 行）。复现一次把那几行发回来就能定位是哪一条被掐。另含 v0.2.553：：血肉戏法/吞噬生命判定「负面」时写成了 `amount <= 0`，而「失去力量」这类负面是**负数**送进来的（`PowerCmd.Apply<StrengthPower>(…, -2, …)` ⇒ amount = -2）⇒ 命运同担那条 9 点被整条挡掉、她也不加格挡。原版判据本就是 `amount != 0`，现在两处都改成「只排除 0，是不是负面交给 `GetTypeForAmount`」。另：血肉骨的能力面板按用户给的数据补齐（单体强/群攻强/单体高压 3、5、11/群体高压 4、10/特殊效果类型：自身/群体减益极强/群体增益弱）。另含 v0.2.552：改成**策略 + 牺牲**：它的两个变量 `PlayerStrengthLoss`/`EnemyStrengthLoss` 都是普通 `DynamicVar` 而不是 `PowerVar<>` ⇒ `ClassifyPowers` 什么都认不出来，会掉到「技能牌兜底 = 强化」上（蓝色上箭头）。现在把 `SHARED_FATE` 同时加进 `ForceDebuffCards`（给敌人扣力量 = 策略/红下箭头）与 `SacrificeCards`（自己也扣力量 = 牺牲/蓝下箭头）；顺带它既然算「策略」，血肉戏法那 +9 也会跟着显示（它确实会给那个玩家上负面）。另含 v0.2.551：：①**招架盾 → 锚**（战斗开始 10 格挡），特殊效果警告文案同步去掉「招架盾」；②11 回合剧本再重排 —— 1 血肉戏法+鬼火+防御 / 2 吸引仇恨+摧残 / 3 打击+恐惧+ / 4 违逆+防御+苦难 / 5 护卫+出击+防御(没费) / 6 命运同担+防御×2 / 7 吸引仇恨 / 8 摧残+防御+防御(没费) / 9 防御+恐惧+ / 10 违逆+防御+苦难 / 11 护卫+出击+防御(没费)，循环仍 7～11；③**摧残在玩家身上改成与虚弱/易伤同步**（`DebilitateTicksWithWeakPatch`）—— 反编译：`VulnerablePower` 是 `side == Enemy ⇒ TickDownDuration`，而 `DebilitatePower` 是「持有者自己这一侧回合结束 ⇒ Decrement」⇒ 玩家身上摧残玩家回合一结束就掉 1，少撑一个回合；现在玩家身上的摧残也只在**敌方**回合结束掉 1。另含 v0.2.550：：**撕碎 → 玷污**（`Defile`，1 费 13 伤，升级 17）—— 第 4、11 回合各换一张，能量账不变；卡组里对应替换。另含 v0.2.549：：①**进阶 9 = 只升级摧残**（去掉恐惧）；②剧本重排 —— 2 出击+恐惧+(没费打击) / 3 违逆+防御+苦难 / 5 吸引仇恨(不升级)+摧残 / 6 打击+防御+命运同担 / 7 吸引仇恨 / 9 防御+恐惧+ / 10 违逆+防御+苦难 / 11 撕碎+护卫+防御，循环仍 7～11；③卡组按单回合最大张数重算（14 张：防御×2、打击×2，其余各 1）。另含 v0.2.548：：`UNLEASH` 的伤害是 `CalculationBase + ExtraDamage × 奥斯提当前生命`，而意图数字只在**面板重建**时算 —— 玩家在她回合外打掉/治好奥斯提、或奥斯提被召唤/死亡时，能力层数一个都没变 ⇒ `RefreshIntentsIfPowersChanged` 的指纹不变 ⇒ 不重建 ⇒ 数字停在旧值。现在把**她的奥斯提**（存活 + 当前生命 + 最大生命）也拼进那个每帧比对的指纹，一变就 `IntentsDirty` ⇒ 下一帧按新血量重算；并加了一行限流日志 `意图重算（能力/奥斯提变化）：… → …` 便于验证。另含 v0.2.547：（用户「更改部分意图和进阶效果」）：11 回合剧本按新稿重排 —— 2 出击+防御+防御 / 3 违逆+恐惧+苦难 / 4 撕碎+防御+防御 / 5 吸引仇恨+ / 9 恐惧+防御+苦难 / 10 撕碎+护卫+防御 / 11 防御+违逆+打击，循环仍是 7～11；**进阶 9 改成升级恐惧、摧残**（之前误写成苦难/出击/摧残）；卡组按单回合最大张数重算（护卫×2、防御×2、出击×2、违逆×2、恐惧×2、苦难×2、撕碎×2、打击×2，吸引仇恨/摧残/命运同担各 1）。另含 v0.2.546：：①多人时招架盾只打她锁定的那位玩家（原版是 `HittableEnemies` 里随机一个，会打到没被锁的人）——`ParryingShieldBossPatch` 按原版条件结算、目标固定成 `BossLockOn.TargetFor`；②命运同担不再打到自己 —— 反编译该卡除了让目标掉力量还会**让她自己掉力量**，而她自己的生物 `Side==Enemy` 正好满足原版血肉戏法的 `power.Owner.IsEnemy` ⇒ 她给自己上负面时被自己的血肉戏法打 9 点；现在 `SleightOfFleshBossPatch` 对「持有者是我们 Boss」的情况**整条接管**，只对真人玩家结算；③进阶 9 不再升级出击（保留苦难、摧残）；④招架盾预测修正 —— 以前无条件把她**当前**格挡算进去，而格挡在她自己回合开始会清空 ⇒ 玩家回合里那份是上回合剩下的、马上就没，现在只在「她回合内」才计入；并加了一行预测明细日志（当前格挡/牌/骇人头盔/吞噬生命各自贡献）。另含 v0.2.545：NECROBINDER_FLESH —— 11 回合剧本 + 7～11 循环，遗物缚魂命匣/涅奥的祝福/骇人头盔/招架盾，特殊效果「吞噬生命」（她每给玩家一个负面就 +4 格挡，`BossDevourLifePower`）；①`SleightOfFleshBossPatch` 让血肉戏法对她生效（原版条件是 `power.Owner.IsEnemy`＝绝对阵营，玩家是 Side.Player ⇒ 对影子玩家永远 false）；②会施加减益的牌，意图里补上血肉戏法的 9 点（无来源）；③回合结束格挡预测 ≥10 时，在独立意图位（电球/炸弹那一行）显示招架盾的 6 点（`FleshBone.EndOfTurnBlock` 算了 plan 里的格挡 + 骇人头盔 + 吞噬生命）；④多人「苦难」把被锁玩家负面传给其他玩家 ⇒ 其他玩家意图显示 血肉戏法×种类数；⑤多人把吸引仇恨换成骸骨军团（`MultiplayerCardSwap` act0 行）；⑥16 语言文案（特殊效果含「无来源伤害 / 为你而死不挡」警告）+ 藏品命运同担 + 能力面板。另含 v0.2.544：：`BossStatusRow.Fill` 里能量球是按影武者缓存的节点，旧代码只在「已挂在本行」和「完全没有父节点」两种情况把它排进 `target`，一旦它挂在**另一条（残留的/备用/不可见的）状态栏**上就既不搬运也不登记 ⇒ 这一行没有能量球、`EnergyCenterX` 一路返回 0（14:07 的 mod_log 里 `独立意图定位：能量中心 x=0` 反复出现，且同帧有 `rowChildren=5/5` 与 `4/4` 两条 Fill）。现在：①「可复用」判定再按影武者缓存兜一层，避免把行里那颗正在用的球当垃圾清掉；②父节点已失效/排队销毁/**不可见**时搬过来；③主面板（`primaryRow`，可见）一律抢过来，只有备用行才「别抢还活着的那条」。另含 v0.2.543：：`PlaceExtraIntents` 原来只用「能量栏中心 − 行宽/2」，而它和卡面下方那排意图**同一高度**，牌少或这一行偏宽时左缘会滑进卡牌意图区（用户「第二回合电球被动过于偏左，和扫荡射线叠在一起」）⇒ 现在先逐格测量所有卡牌意图行的真实右缘（意图行在卡片内部且整体左移过、内容还可能比卡面宽，不能拿卡面右缘当边界），再让这一行左缘至少落在它右边 Gap 处；彩虹机的整体左移算在 max 之前。另含 v0.2.542：改成走「超能光束」那层原版 buff：每满 3 张牌给她 1 层 `HyperbeamFocusDownPower`（基类 `TemporaryFocusPower`，IsPositive=false）—— 应用时原版立刻 −1 集中、她自己回合结束时原版移除该层并把集中全数还回来、且它是 debuff 所以人工制品能挡，图标文案也是原版那套；107 没有这个类型（107 的超能光束是直接 −3、不返还）⇒ 用 `#if STS2_111 / #else` 保持老写法自己扣自己还。另含 v0.2.541 四项：①语音音量 -4dB → -0.5dB（振幅 ×1.5，用户「有点小，调大 50%」）；②预借时间不再统计**自动打出**的牌（`CardPlay.IsAutoPlay` 或 `Resources.EnergySpent == 0` ⇒ 没花能量不计），修「历史书打出攻击牌会错误增加预借时间计数」；③引雷针计数改存字段、图标数字钳在 1..3（还差几张），修「打三张牌集中不掉」—— 根因是玩家回合开始时 `Apply(-Amount)` 把数量减到 0，原版 `PowerCmd.ModifyAmount` 见 `ShouldRemoveDueToAmount()` 直接**把整条 power 移除**了；④电球机暴风雨的激发意图闪一下就没了（另记）；另含 v0.2.540：按用户第二轮要求改：①**时机延后** —— 不在出牌前播，改在 `Hook.AfterDamageGiven`（伤害真正落地那一刻）才播；②**不再每张攻击牌都喊** —— 只认 8 张：痛击 BASH / 完美打击 PERFECTED_STRIKE / 突破 BREAKTHROUGH / 全身撞击 BODY_SLAM / 巨石 GIANT_ROCK / 彼岸咆哮 HOWL_FROM_BEYOND / 闪电霹雳 THUNDERCLAP / 欺凌 BULLY；③每回合最多一次，多段牌只在第一段响（`CardCmd.AutoPlay` 前缀挂待播标记 + 上述伤害钩子配音清标记，两版本签名一致）；④彼岸咆哮那种回合结束从消耗堆自动打出的也算，因为挂在 AutoPlay 而不是我们的 PlayOne 上。上一版 v0.2.539 首次加入：：一代崩坠那 6 句 VO_IRONCLAD_1a/1b/1c/2a/2b/2c 已打进我们自己的 PCK —— ①每回合**首次打出攻击牌**时随机喊 1a/1b/1c（`CharBossMonster.PlayOne` 在 AutoPlay 之前播，`AttacksPlayed==0` 即本回合首张，与出牌同时出声）；②**死亡**时随机喊 2a/2b/2c（`HasDeathSfx` 覆写成 false 压掉原版那条 FMOD 事件 + `NCreature.StartDeathAnim` 后置播我们的 ogg，同生物只喊一次）；③覆盖所有 IroncladBoss 子类（怒/打击/出血/杂耍/易伤/巨石）。另含 v0.2.538 的亡魂牵引段数与致死性顺序修正（用户实测预览 7×3、真实 6×4）：①**段数**补上「本回合还没打出去的虚无牌」—— 致死性是虚无牌而它排在亡魂牵引前面，规划期战斗历史里还没有它，改由 `BossIntentBuilder.PlannedEtherealBefore` 按 `boss.Plan` 自己数（三条重算路径自动一致，打完即从 Plan 摘掉⇒不会双算、也不会漏传参）；②致死性 +50% 的**顺序**改成「先走完原版伤害管道（力量/虚弱/易伤）→ 再乘倍率 → 向下取整」，与真实结算同序（(5+力量−1)×1.5=6）；旧写法先把牌面 ×1.5 再减力量会多 1 点。另含 v0.2.537 的虚无骨四处收口：①女妖之嚎不再多画「强化」意图（`NoBuffIntentCards` 点名 BANSHEES_CRY）⇒ 第 4 回合 = 攻势+策略、第 7 回合 = 只有攻势；②亡魂牵引的段数补上「本回合已打出的虚无牌」—— 致死性本身是虚无牌，规划期历史里还没有它（`BossScriptPlanner.etherealPlanned` 按剧本顺序累加，只给 PULL_FROM_BELOW）；③致死性的 +% 以前只给「奥斯提那几张」手工补，现在**她身上还没这层 power** 的普通攻击牌（正是规划期）也补一次，power 真上身之后走原版管道不双份；④配置界面补上虚无骨的能力面板（循环 7～10 / 单体极强 / 群攻很强 / 单体高压 4、7、9 / 群体高压 4、7 / 全局））";

    private static bool _logged;

    /// <summary>打一行「构建：…」，并第一次装上运行时才能装的东西（薄荷尖塔兼容等）。只做一次。</summary>
    public static void LogOnce()
    {
        if (_logged) return;
        _logged = true;

        try { Logger.Log($"[CharBoss] ===== CharacterBosses 构建：{Tag} ====="); }
        catch { /* 忽略 */ }

        // 双版本兼容层（v0.107 正式版 / v0.111 beta）：把游戏版本和各项 API 探测结果打出来，
        // 以后一看日志就知道这一把跑在哪个版本、哪些功能被跳过了。
        try { Core.GameCompat.LogOnce(); }
        catch (Exception e) { try { Logger.Log("[CharBoss] 兼容层探测失败（忽略）: " + e.Message); } catch { } }

        // ★★ v0.2.533（玩家 2026-11-09：「炼药猎的**群蛇形态多人时会打其他玩家**」）：
        //   `SerpentFormLockOnPatch` / `DarkOrbLockOnPatch` / `PlayerEffectExemption` / `BossBlockAbsorbPatch`
        //   这几套是**手写 Harmony**（要挂 async 状态机的 `MoveNext`，没法用 `[HarmonyPatch]` 特性），
        //   靠自己的 `TryInstall()` 安装 —— 而**这四个 TryInstall 一个调用点都没有**，
        //   也就是说它们**从来没生效过**（日志里也从来没有「已挂 N 个状态机」那一行）。
        //   ⇒ 「多人时群蛇形态随机打人」就是收窄逻辑压根没挂上。
        //
        //   只安装**明确对应「随机/球被动只打被锁定那位」**的两套（用户口径）：
        //     · SerpentFormLockOnPatch：群蛇形态（炼药猎的「蛇形」牌）
        //     · DarkOrbLockOnPatch：黑暗充能球的被动
        //   `BossBlockAbsorbPatch` **故意不装**：用户明确要求「只有炼药猎能挡失去生命」，
        //   那件事由 `BrewerBossBlockableHpLossPatch` 负责（装了这个就变成所有 Boss 都能挡）。
        //   `PlayerEffectExemption` 也先不装（会改变「玩家施加的效果打谁」，需要单独确认）。
        try { Patches.SerpentFormLockOnPatch.TryInstall(); }
        catch (Exception e) { try { Logger.Log("[CharBoss] 群蛇形态锁定补丁安装失败（忽略）: " + e.Message); } catch { } }
        try { Patches.DarkOrbLockOnPatch.TryInstall(); }
        catch (Exception e) { try { Logger.Log("[CharBoss] 黑暗球锁定补丁安装失败（忽略）: " + e.Message); } catch { } }

        // 薄荷尖塔（MintySpire2）的「即将受到的伤害」兼容：它可能排在我们后面加载，
        // 所以等所有程序集都到位后再反射挂补丁（拿不到就安静跳过）。
        try { Core.MintySpireDamagePreviewCompat.TryInstall(); }
        catch (Exception e) { try { Logger.Log("[CharBoss] 薄荷尖塔兼容安装失败（忽略）: " + e.Message); } catch { } }
    }
}
