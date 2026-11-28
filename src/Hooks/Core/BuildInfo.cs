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
/// ⚠ `Tag` 里**不能有反斜杠**（构建脚本会失败）——写路径一律用正斜杠。
/// </summary>
public static class BuildInfo
{
    /// <summary>构建指纹（日志/数据表里用）。每次改动都要 +0.0.1 并写一句改了什么。</summary>
    public const string Tag = "v0.2.1002 / 2026-11-28（节制力量延后到玩家回合开始 / 时间扭曲不再给力量 / 意图预览吃「前面 Boss 先挂的易伤」）— ① 用户：「让观者的时间扭曲不会在强制终止玩家回合时让所有boss获得2力量」⇒ BossTimeWarpPower 触发只做两件事：强制结束被锁定玩家的回合 + 塔1 那套特效（金边闪光/钟/音效），不再施加任何 StrengthPower；powers 表 16 语言的 BOSS_TIME_WARP_POWER.description 同步改成「你每打出 12 张牌，观者就会强制结束你的回合。」。② 用户新口径：「每当观者打出 12 张牌，她就会强制结束自己的回合并在玩家的下回合开始时使她和她的其他友方获得 2 点力量」⇒ BossTemperancePower 触发时不再当场加力量，只记一笔「欠账」(_pendingStrengthGrants)，由 AfterSideTurnStart(side==Player) 在玩家侧回合开始时结算（她 + 她的其他友方）。②′ 文案：BOSS_TEMPERANCE_POWER.description / .description_dread 与 monsters 表 WATCHER_RUSHDOWN_BOSS.temperance / temperance_dread 共 16 语言全部改成新措辞。③ 新增 Core/VulnerableLookahead：意图伤害预览现在会「前瞻」同一个敌方回合里排在它前面的 Boss 会不会先给它挂易伤（用户举例：养手骨第 4 回合「击掌」群体易伤 11 伤 + 2 易伤，巨石战「彼岸咆哮」群攻，之前彼岸咆哮的预览偏低）。实现：按 CombatState.Enemies 的行动次序正向模拟前面每只 Boss 的计划牌 —— 会挂易伤的牌用 DynamicVars.Vulnerable 判定（反编译实证：Bash/HighFive 都有 PowerVar<VulnerablePower>，键名就是 Vulnerable），群体(TargetType.AllEnemies)算到所有玩家、单体算到它计划打的那个目标；苦难(MISERY)会把被锁定玩家身上的负面复制给其他玩家（传染易伤）也算进去；只在「目标现在没有易伤」时才乘倍率（1.5，目标带摧残时 2.0），且乘在 floor 之前；无来源伤害根本不走这条管道、召唤物/影子玩家/原版怪物一律 1.0。— 上一版：v0.2.1001 / 2026-11-28（版本号口径改回 0.2.x 四位数 + 时间扭曲只记被锁定玩家的出牌）— ① 用户：「把版本号改回 0.2.1001 这个样子，先不升大版本」⇒ 从本版起版本串写成 0.2.NNNN（0.2.999 之后不再进 0.3.x）。② 用户：「确保恐惧模式多人联机时时间扭曲只记录被其锁定的玩家的出牌」⇒ BossTimeWarpPower 的记账改成：用全项目统一入口 BossLockOn.TargetFor(boss) 取锁定目标（内部已处理单人/多人/被锁玩家死亡重派/SL 换实例），取不到再退 PureVictim，再取不到而场上只有一位真人就用那一位（单人兜底）；玩家身份比较改成「引用相同 或 NetId 相同」（SamePlayer），跨实例也认得出是同一个人；影子玩家（她自己的、别的 Boss 的）的牌一律不算（Creature.Monster != null）；记/不记都打一行带 NetId 的诊断（时间扭曲：记 X 的第 n/12 张（锁定目标=Y） / 不记 X 的牌（她锁定的是 Y））。— 上一版：v0.2.1000 / 2026-11-28（时间扭曲特效按实机反馈重做，原记作 v0.3.000 + v0.3.001）— 用户原话「钟在屏幕中间转的时候卡了一下，并且钟的大小似乎有点大」。① 卡顿：0.3.000 的旋转 tween 在悬停段是 TweenInterval（整整 0.40s 不转）⇒ 现在旋转三段全部 Linear 匀速（4.7 rad/s ≈ 0.75 圈/秒，合计约 1 圈），位移/缩放的到达取消 Back 回弹（回弹会像第二下停顿），悬停段改成上浮 10px 再落回；② 尺寸：0.40 屏短边 → 0.30 屏短边（1920×1080 下贴图矩形 345px、可见怀表约 230px）。— 上一版：v0.3.000 / 2026-11-28（按用户实机反馈重做时间扭曲特效：金色辉光换成塔1 那张 borderGlow2 + 钟以前根本没出现）— ① 用户原话「你那个黄光很明显和1代的不一样，并且也没有钟的动画出现」。② 黄光：0.2.999 是自己拼 4 条纯色 ColorRect（硬边没渐变）⇒ 现在直接用塔1 的图：vfx/vfx.atlas 的 borderGlow2（xy 2,604 size 1280,720）裁出 ⇒ res://CharacterBosses/images/vfx/border_glow_2.png（pck_extras 3 行），铺满全屏 + 叠加混合（CanvasItemMaterial.BlendMode=Add）+ Color.GOLD 着色，与塔1 BorderFlashEffect(Color.GOLD, additive) 同一条路子；淡入 0.10s、淡出 0.50s（塔1 约 0.5s）。③ 钟为什么不出现：0.2.999 用 Sprite2D + 一条 SetParallel(true)/Chain() 的复合 Tween，初始状态是「屏幕下方 + modulate.a=0」⇒ 那条 tween 实机里没推进（黄光的简单顺序 tween 是好的，这就是分水岭），节点停在屏幕外全透明。现在改成 TextureRect（Control，这条渲染路已经证明可用）+ 四条最简单的顺序 Tween（position/rotation/scale/modulate:a 各一条，全程不碰 SetParallel/Chain），并且**初始状态就可见**（不透明、满尺寸、贴屏幕底边露出一截），万一 tween 又不跑会留下一个静止的金怀表而不是一片空白。④ 另外补了三行诊断（辉光贴图尺寸 / 钟的 inTree / 四条 tween 的 IsValid / 动画结束），下一轮日志能直接看出卡在哪一步。— 上一版：v0.2.999 / 2026-11-28（红蓝观恐惧模式「时间扭曲」补齐塔1 那套：图标 / 描述 / 钟与金边闪光 / +2 力量）— ① 图标：从塔1（SlayTheSpire/desktop-1.0.jar）的 powers/powers.atlas 里裁出 128/time 那张金怀表（xy 92,390 size 86,87 orig 128×128，摆进 128×128 画布偏移 21,20）⇒ res://CharacterBosses/images/powers/time_warp.png，随 PCK 发布（pck_extras 登记 3 行）；BossSpecialPowerIconPatch 里给 BossTimeWarpPower 指定它（以前落到「好奇灯泡」，所以用户看到的是别的图）。② 描述：powers 表 16 语言补 BOSS_TIME_WARP_POWER.title/.description，照塔1 原文写成「每当你打出 12 张牌，你的回合会强制结束，并且观者获得 2 点力量」；「节制」恐惧版文案与遭遇说明里「时间扭曲只结束回合」那半句同步补成「并 +2 力量」（zhs/zht 中文，其余 14 种英文，与本项目一贯做法一致）。③ 行为：塔1 TimeWarpPower.onAfterUseCard 那三件事全搬 —— 强制结束你的回合 + BorderFlashEffect 金边闪光 + TimeWarpTurnEndEffect 那个钟 + 能力持有者 +2 力量（塔1 的 STR_AMT 就是 2）；「节制」（她打满 12 张）与「时间扭曲」（你打满 12 张）两处触发共用 Core/TimeWarpFx。④ 表现：塔1 的特效用的就是能力图标那张图（AbstractPower.atlas.findRegion 128/time），这里按用户点名做成「从屏幕下方飞上来 → 旋转悬停 → 掉回下方淡出」；音效 STS_SFX_TimeWarp.ogg 与 jar 内 STS_SFX_TimeWarp_v2.ogg 逐字节一致。⚠ 本模组自己 new 的节点收不到 _Process/_Draw（实机量过 has_method(_input)=False）⇒ 整套动画只用引擎 Tween 改 Sprite2D/ColorRect 的 position/rotation/scale/modulate，一行虚函数都不用。— 上一版：v0.2.998 / 2026-11-28（本会话十八版合并：F8 导出真的能用 / 设置页日志说明 / 全量隐私脱敏 / 玩家黑球不再打队友 / 给观者「勤学精进」打防卡死补丁）— ① 0.2.976-0.2.977：F8「导出最近日志」从来没成功过，真因是我们自己 new 的 Node has_method(_input)=False（Godot 不派发），改为 Harmony 后缀挂 NGame._Input 与 NInputManager._UnhandledKeyInput；导出改读磁盘 mod_log 尾部 8MB 并打包 trace/stall/desync。② 0.2.978-0.2.979：设置页 A7「日志在哪里 / 一键导出」（手机没有 F8 的入口），界面上的路径一律 %APPDATA% 脱敏写法。③ 0.2.980：隐私审查 —— trace/stall/desync/场景树以前完全没脱敏（实测 trace 16 处用户名 + 3 处 SteamID、desync 117 处 SteamID），现在全部过 Redact；路径正则同时认正反斜杠。④ 0.2.981：多人时玩家自己的黑暗球打队友 —— BossOrbTargeting.BossOf 的兜底没排除真人玩家，把玩家的球当成 Boss 的球改写目标；现在只认「Owner 就是我们的 Boss」或「球在她影子玩家的 OrbQueue 里」，玩家黑球另加一道只打对手侧的兜底。⑤ 0.2.982：观者模组「勤学精进」出牌会把整场卡死（A/B 两版都在 async OnPlay 里必然出事：ThrowIfNull(cardPlay.Target) / cardPlay.Target.Powers 空引用；出牌动作 await 它，Task 抛异常或永不完成 ⇒ 动作队列全卡住，实测 AwaitTurnEndAndSwitchSides 卡 249546ms）；补丁 = 空目标兜底 + 守卫吞异常 + 8 秒超时放行；0.2.983 把安装时机提前到 SceneTree.process_frame；0.2.984 抓到真因并改成整段接管：观者模组调的 CardModel.UpgradeInternal() 在现版本变成 UpgradeInternal(bool silent)，那张牌 JIT 期就 MissingMethodException 并从 OnPlay 同步抛出（所以 0.2.982 的「包一层 Task」根本没机会执行）⇒ 现在前缀直接接管 OnPlay，用同一条 DamageCmd 链重做伤害 + 用 CardCmd.Upgrade 升级随机可升级手牌（两个版本签名一致）；0.2.985 按用户「这勤学精进斩杀怎么不敲牌？」把斩杀判据改成和原实现一致的 DamageResult.WasTargetKilled（0.2.984 用生物 IsDead 判，而 STS2 死亡是延迟处理的 ⇒ 永远判不到），并补了三行诊断；0.2.986 按用户「还是不敲牌」再修：CardCmd.Upgrade 第一行就是 if (CombatManager.Instance.IsEnding) return（斩杀往往就是打死最后一只怪 ⇒ 静默空转），改成反射挑对的 UpgradeInternal 重载并以 IsUpgraded 状态为准；0.2.987 按用户「她是升级卡组中的牌，不要改本来的效果」把升级来源从手牌改正成卡组（PileType 枚举 None0/Draw1/Hand2/Discard3/Exhaust4/Play5/Deck6 ⇒ 观者原实现的 (PileType)6 就是 Deck），0.2.988 按用户「装了观者mod后要支持6人联机，怎么还是5个boss？」把 MaxPlayersPerFight 从写死 5 改成「观者 mod 可用 ⇒ 6」，于是 6 只路径（SixBossFight / 平行四边形站位）终于生效；0.2.989 再修「还是只有 5 只」——观者闸门（所有人同一份实现）在测试房里过不了时，第 6 条阵容是同角色另一套设计、被伙伴去重挡掉，现在放宽为「宁可同角色也不同 Key」补满（Boss 数 = 玩家数是硬规则）；并新增「Boss>4 只时第 4 张及以后的意图行半透明（最左侧那只不受限）」；0.2.990 按用户口径**撤销**「同角色补满」（每个角色最多一只仍然是硬约束，不装观者上限仍是 5），改成修观者闸门本身：一个客人都没上报时等 20 秒后按「那些座位不是真实客户端（同机/测试房占位）」放行（真实客人 5 秒必发 caps 心跳），于是 6 人 + 观者 mod ⇒ 第 6 个角色是观者 ⇒ 6 只各不相同；0.2.991 按用户「观者 mod 阻塞联机，能联机就说明所有人装的是同一个 ⇒ 只检测房主」把「等客人上报」整套删掉，房主只看本机 WatcherModPresence.Usable（客人端用房主广播值、没收到就按本机算），PeerCaps 只留诊断（实现不同时打 ⚠ 不再阻塞）；0.2.992 修「观者和猎人叠在一起」——BossEnemySpreadPatch.SlotX 里 `if (count >= 5)` 写在 `if (count >= 6)` 前面，6 只只拿到 5 个站位、第 6 只被 `i % slotX.Length` 绕回 0 号位与第一只完全重合（SlotY 顺序是对的，所以看着是叠一起），现在 6 只分支前置 + 站位点不足时打 ⚠；0.2.993 修「6 只全堆在玩家区、右边空一大块」——6 只的 X 起点曾写死成 `视口宽×0.17`、总跨度只有 600px，现在改成和 4/5 只同一套「贴右缘 + 1.5×SpreadX 步长」总跨度≈1200（1920 下 570/870/1170/1170/1470/1770），Y 上下幅度 6 只时放大到 ±0.95；0.2.994 按用户「最左边跑到玩家那了，再往右挪，空间还是小，≥6 人时进一步扩大场地」把 6 只左边界推到 0.34 屏宽并把 6 个点铺满右半场（1920 下 653/932/1211/1211/1489/1770），且 ≥6 只时把每只 Boss 节点缩到 0.80（人和怪变小、场地变大）；同时按用户二次订正修意图半透明：≥4 只 Boss 且该 Boss 本回合计划>3 张时**整只的意图行**半透明（原来按『第 4 张及以后』判、且用 Plan 里找不到的卡去查下标 ⇒ 一次都没生效），最左侧那只仍不受限；0.2.995 按用户新口径重排多人 Boss 的站位/行动次序：1、2 幕 猎→机→观→愚→骨→战→储，3 幕 猎→机→观→愚→骨→储→战（观者/愚者从原来的 `_ => 5` 兜底档提到第三/第四位，用基类 WatcherBoss/FoolBoss 命中全部变体）；0.2.996 修观者「以手拒之」的格挡返还错给 Boss：反编译 A 版 BlockReturnPower.AfterDamageGiven 会优先用 dealer.Player，而我们的 Boss 是「影子玩家」（同一 Creature 既是 Monster 又是 Player）⇒ 某只 Boss 打中带 debuff 的另一只 Boss 时格挡返还给了那只 Boss；现在前缀拦「dealer 解析出的 Player 是影子玩家」的情况（不返还，卡面原文是「你攻击该敌人时」），其余一律交还原实现；0.2.997 按用户「统帅模式下抉择储的选牌应该跳到对应玩家视角」修「指导」：实测日志 `Cannot focus selection owner` ⇒ 退回第一张（隐秘藏品/铸墙就是 candidates[0]/[1]），现在弹界面前先用 LocalContext.NetId 把本机视角切到被锁定的那位玩家（Core/SeatSwitch，只在「本机能操控多座位」= 观察到视角自己变过 或 有 AiTeammate 程序集时才动手）；0.2.998 修「意图半透明似乎没实现」——上一版用 boss.Plan.Count>3 判，而显示阶段 Plan 常已清空（面板真正画的是 DisplayPlays）⇒ 永远不满足；现在取 Plan/DisplayPlays 较大值，并给每只 Boss 打一行诊断（场上几只/本回合几张/是否最左/结果）；并补回原来的两处表现层调用（WatcherCardVfx.BeginLesson / ResolveLessonLearned / ShowUpgradePopup，反射+try-catch）与「本节点升级历史」记录。详见 docs/plans/changelog-history.md";

    private static bool _logged;

    /// <summary>打一行「构建：…」，并第一次装上运行时才能装的东西（薄荷尖塔兼容等）。只做一次。</summary>
    public static void LogOnce()
    {
        if (_logged) return;
        _logged = true;

        // ★ v0.2.834：**先**装启动期异常快照器（FirstChanceException）——
        //   玩家那个「致命错误！在游戏启动期间遇到了问题」的弹窗来自游戏自己的
        //   `NGame.GameStartupWrapper`（它 catch 之后又 throw），所以之前交上来的日志里什么都看不到。
        //   装上之后，启动期任何被游戏 catch 掉的异常都会在 mod_log 里留一行「⚠ 启动期异常#N」。
        // ★ v0.2.839：第一件事就是装「崩溃面包屑」（同步落盘，进程被强杀也留得住）
        try { CrashTrace.Install(); } catch { /* 忽略 */ }
        try { CrashTrace.Mark("BuildInfo.LogOnce 开始（第一帧）"); } catch { /* 忽略 */ }
        try { StartupWatchdog.Install(); }
        catch { /* 忽略 */ }
        // ★ v0.2.966（用户 2026-11-26：「你试试加诊断补丁」）：
        //   主线程停摆看门狗的心跳 —— 挂 `SceneTree.process_frame`（主菜单也在跳，
        //   不能只靠 BossFrameTick：那个只在战斗里有节点时才被调到）。
        try { StallWatchdog.EnsureHooked(); } catch { /* 忽略 */ }
        // ★ v0.2.983（用户 2026-11-27：「观者mod有bug，打勤学精进会卡住」）：
        //   观者那张牌的「防卡死补丁」也在启动第一帧就找一遍 —— 它挂的是 SceneTree.process_frame，
        //   与战斗 HUD 无关（`BossFrameTick` 只在战斗里才被驱动，不该让「修别人 bug」依赖它）。
        try { WatcherLessonLearnedSafePatch.EnsureHooked(); } catch { /* 忽略 */ }
        // ★ v0.2.996（用户 2026-11-28）：观者「以手拒之」的格挡返还 —— 别返还给被上 debuff 的 Boss
        try { WatcherBlockReturnFix.EnsureHooked(); } catch { /* 忽略 */ }

        try { Logger.Log($"[CharBoss] ===== CharacterBosses 构建：{Tag} ====="); }
        catch { /* 忽略 */ }

        // 启动阶段标记：万一以后又死在初始化中间，日志最后一行就能告诉我们是哪一步
        try { Logger.Log("[CharBoss] 启动阶段 1/5：遥测补发"); } catch { /* 忽略 */ }
        try { CrashTrace.Mark("启动阶段 1/5"); } catch { /* 忽略 */ }


        // 双版本兼容层（v0.107 正式版 / v0.111 beta）：把游戏版本和各项 API 探测结果打出来，
        // 以后一看日志就知道这一把跑在哪个版本、哪些功能被跳过了。
        // 上次没发出去的遥测记录：启动就补发一次（用户 2026-11-20：「加上下次重试」）
        try { Telemetry.RetryPendingUploads(); }
        catch (Exception e) { try { Logger.Log("[CharBoss] 启动补发失败（忽略）: " + e.Message); } catch { } }
        try { Logger.Log("[CharBoss] 启动阶段 2/5：小小姐护栏"); } catch { /* 忽略 */ }
        try { CrashTrace.Mark("启动阶段 2/5"); } catch { /* 忽略 */ }
        // 与「小小姐的战斗提示」(XiaoSpireHint) 的互操作护栏：别让它的自动选牌 AI 接管角色 Boss 的选牌/出牌
        try { Patches.XiaoHintBossShield.TryApply(); }
        catch (Exception e) { try { Logger.Log("[CharBoss] 小小姐护栏安装异常（忽略）: " + e.Message); } catch { } }
        try { Logger.Log("[CharBoss] 启动阶段 3/5：兼容层探测"); } catch { /* 忽略 */ }
        try { CrashTrace.Mark("启动阶段 3/5"); } catch { /* 忽略 */ }
        try { Core.GameCompat.LogOnce(); }
        catch (Exception e) { try { Logger.Log("[CharBoss] 兼容层探测失败（忽略）: " + e.Message); } catch { } }
        try { Logger.Log("[CharBoss] 启动阶段 4/5：手写补丁安装"); } catch { /* 忽略 */ }
        try { CrashTrace.Mark("启动阶段 4/5"); } catch { /* 忽略 */ }

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

        // ★ v0.2.896：纯诊断（玩家 2026-11-23「抉择储的天际钻头好像不会优先攻击瓦皮」）。
        //   瓦皮承伤是**原版钩子**（`WuppysLoyaltyPower.ModifyPreBlockDamageTarget`）干的，
        //   我们的选靶补丁改不动它 ⇒ 先照出「是被哪一条挡下」（目标/强化攻击/瓦皮已死），再决定要不要动行为。
        try { Patches.WuppyRedirectTracePatch.TryInstall(); }
        catch (Exception e) { try { Logger.Log("[CharBoss] 瓦皮承伤诊断安装失败（忽略）: " + e.Message); } catch { } }

        // 薄荷尖塔（MintySpire2）的「即将受到的伤害」兼容：它可能排在我们后面加载，
        // 所以等所有程序集都到位后再反射挂补丁（拿不到就安静跳过）。
        try { Core.MintySpireDamagePreviewCompat.TryInstall(); }
        catch (Exception e) { try { Logger.Log("[CharBoss] 薄荷尖塔兼容安装失败（忽略）: " + e.Message); } catch { } }

        // ★★ v0.2.903（用户 2026-11-23「疾风观」订正时发现）：
        //   `Core.WatcherRuntimePatches.TryInstall()` **一直没有调用点** —— 和上面 v0.2.533 记的那四个
        //   一模一样的毛病：神格不退 / 许愿固定选 / 真言满 10 层接管 / B 版姿态伤害兜底 这些
        //   运行期补丁**从来没被安装过**（日志里也没有「已挂上…」那几行）。
        //   现在补上调用；它内部对「观者 mod 没装」是**安全跳过 + 下次再试**的（`ModAssembly() == null` 直接 return），
        //   所以 0.107 / 没装观者的玩家完全不受影响。
        //   新增的「斋戒不再使她失去能量」也挂在这批里（`InstallFastingNoEnergyDown`）。
        try { Core.WatcherRuntimePatches.TryInstall(); }
        catch (Exception e) { try { Logger.Log("[CharBoss] 观者运行期补丁安装失败（忽略）: " + e.Message); } catch { } }

        try { Logger.Log("[CharBoss] 启动阶段 5/5：完成（如果日志停在这之前，就说明死在对应那一步）"); } catch { /* 忽略 */ }
        try { CrashTrace.Mark("启动阶段 5/5"); } catch { /* 忽略 */ }
    }
}
