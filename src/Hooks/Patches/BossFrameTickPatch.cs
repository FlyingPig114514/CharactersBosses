using System;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using CharacterBosses.Core;
using CharacterBosses.Monsters;
using CharacterBosses.UI;

namespace CharacterBosses.Patches;

/// <summary>
/// 每帧驱动 —— **只挂原版每帧一定会跑的方法**，绝不依赖 mod 自己的 Godot 回调。
///
/// 为什么（本轮实机证据）：<c>mod_log.txt</c> 里 <c>帧回调 ok</c>（<see cref="BossHandPanel"/> 的心跳）
/// **一条都没有**，而同期「手牌栏跟随」却有 —— 说明 <c>BossHandPanel.OnFrame</c> 从来没被调用：
///   ① <c>GetTree().ProcessFrame += OnFrame</c>（在 <c>_Ready</c> 里挂的）没生效；
///   ② <c>_Process</c> 重载也没被调到。
/// 于是写在里面的「君王之剑镜像」「随从朝向站位」一次都没跑，诊断日志自然是空的。
/// 本项目所有真正生效的逻辑走的都是 Harmony（<c>OrbLocalVisualPatch</c> 里有同样的教训）。
///
/// 这里挂三个来源，每个**第一次被调到时各打一条日志** —— 这样下一轮日志能直接看出
/// 到底哪个原版每帧方法真的在跑（上一版只挂前两个，结果一条心跳都没有，无法判断是没跑还是没挂上）：
///   · <c>NStarCounter._Process</c>：原版辉星计数器（战斗房间里常驻，负责转光环）
///   · <c>NEnergyCounter._Process</c>：能量球数字补间
///   · <c>NSovereignBladeVfx._Process</c>：君王之剑绕着 Boss 飞 —— **这把剑肉眼可见地在动，
///     所以它一定每帧在跑**，是最可信的那个来源；顺带在这条里直接镜像这一把剑。
/// </summary>
[HarmonyPatch(typeof(NStarCounter), nameof(NStarCounter._Process))]
public static class BossFrameTickStarPatch
{
    [HarmonyPostfix]
    public static void Postfix() => BossFrameTick.Tick(BossFrameTick.SourceStar);
}

/// <summary>备用驱动源：能量球每帧补间。</summary>
[HarmonyPatch(typeof(NEnergyCounter), nameof(NEnergyCounter._Process))]
public static class BossFrameTickEnergyPatch
{
    [HarmonyPostfix]
    public static void Postfix() => BossFrameTick.Tick(BossFrameTick.SourceEnergy);
}

/// <summary>
/// 原版 <c>NSovereignBladeVfx.UpdateHoverTip</c> 把剑牌说明框摆在热区**右侧**
/// （<c>_hitbox.GlobalPosition + Vector2.Right * _hitbox.Size.X</c>）。Boss 的剑在最右边 → 框出屏。
/// 这里在它之后把框挪到热区左侧；玩家的剑不动（判定在 <c>RepositionHover</c> 里）。
/// </summary>
[HarmonyPatch(typeof(NSovereignBladeVfx), "UpdateHoverTip")]
public static class BossBladeHoverPositionPatch
{
    [HarmonyPostfix]
    public static void Postfix(NSovereignBladeVfx __instance)
    {
        BossSovereignBlade.RepositionHover(__instance);
    }
}

/// <summary>
/// 最可信的驱动源 + 精确目标：君王之剑自己的每帧方法。除了驱动统一的 Tick，
/// 还直接把**这一个** <c>NSovereignBladeVfx</c> 的内层骨架镜像掉（不依赖找到 Boss 节点）。
/// </summary>
[HarmonyPatch(typeof(NSovereignBladeVfx), nameof(NSovereignBladeVfx._Process))]
public static class BossFrameTickBladePatch
{
    [HarmonyPostfix]
    public static void Postfix(NSovereignBladeVfx __instance)
    {
        BossSovereignBlade.MirrorOne(__instance);
        BossFrameTick.Tick(BossFrameTick.SourceBlade);
    }
}

/// <summary>
/// 统一的每帧入口：把「跟着 Boss 走 / 君王之剑朝左 / 随从朝向站位」串起来。
/// 只有场上真的有我们的 Boss（<see cref="BossHandUi.CurrentBoss"/>）时才干活。
/// </summary>
public static class BossFrameTick
{
    internal const string SourceStar = "辉星计数器";
    internal const string SourceEnergy = "能量球";
    internal const string SourceBlade = "君王之剑";

    /// <summary>两次真正执行之间至少间隔的微秒数（多个来源叠加时的节流）。</summary>
    private const ulong MinIntervalUsec = 4000;   // ≤250Hz

    /// <summary>
    /// 状态栏图标节点对账的间隔：5Hz。
    /// 用户看到的残留图标是「多出来一个、一直挂着」而不是闪一下，200ms 收敛已经远超肉眼需求，
    /// 而每次对账只是两次反射取字段 + 一遍小 List，5Hz 的开销可以忽略。
    /// </summary>
    private const ulong PowerIconReconcileIntervalUsec = 200_000;

    private static readonly System.Collections.Generic.HashSet<string> Announced = new();
    private static ulong _lastUsec;

    /// <summary>上一帧已经跑过 Tick 的帧号（同帧多来源去重用，见 TickInner 里的注释）。</summary>
    private static int _lastFrame = -1;

    /// <summary>
    /// 「慢车道」间隔：只纠正显示、不跟手的那几件事（特殊效果说明框 / 第二只状态栏 /
    /// 君王之剑兜底扫描 / 多 Boss 站位记录）合到 4Hz 一起做。
    ///
    /// 为什么（用户反馈「boss 战仍然卡」）：这些活原本挤在**每帧**的同一个
    /// <see cref="Tick"/> 里，其中
    ///   · <c>EnsureSpecialUiForAll</c> 每帧 <c>Place</c> + <c>IsVisibleInTree</c>；
    ///   · <c>BossSovereignBlade.MirrorAllInScene</c> 每 120ms **递归扫整个场景树**；
    ///   · <c>BossExtraStatusRow.Update</c> 自己 5Hz 却仍被每帧调用；
    ///   · 原版 <c>AllBosses</c> 在同一帧里被调用 **5 次**、每次都新建一个 List。
    /// 现在：每帧只跑「跟手」的部分（手牌栏跟随、剑的镜像、奥斯提/随从朝向纠正），
    /// 其余进 4Hz 慢车道；<c>AllBosses</c> 每帧只算一次。
    /// </summary>
    private const ulong SlowIntervalUsec = 250_000;   // 4Hz 足够，用户看不到差别
    private static ulong _lastSlowUsec;

    // 集中变化监控：充能球激发的伤害（以及守势格挡）随集中变化，用户要求「每当获得集中就刷新意图」。
    // ⚠ 多 Boss 同场时**每只各记一份**（以前只有一个 `_focusBoss/_lastFocus`，队友 Boss 拿到集中时
    //   意图不会重算 —— 那正是「激发伤害跟着集中走」这条要求漏掉的那一半）。
    private static readonly System.Collections.Generic.Dictionary<CharBossMonster, int> LastFocus = new();

    /// <summary>战斗结束：把「每只 Boss 的集中基线」清掉（否则整局跑下来会一直攒旧实例）。</summary>
    internal static void ResetForNewFight()
    {
        try
        {
            LastFocus.Clear();
            Announced.Clear();
            try { BombBlink.Reset(); } catch { /* 忽略 */ }
            LastPotionVersion.Clear();
            LastStunned.Clear();
            _lastUsec = 0;
            _lastSlowUsec = 0;
            _perfWindowUsec = 0;
            _fpsMin = int.MaxValue;
            // 状态栏图标对账的登记表（生物节点每场都是新的）
            try { PowerIconNodeReconcile.Reset(); } catch { /* 忽略 */ }
        }
        catch { /* 忽略 */ }
    }

    private static int _tickCalls;
    private static double _tickSumMs;
    private static double _tickMaxMs;

    public static void Tick(string source)
    {
        var swTick = System.Diagnostics.Stopwatch.StartNew();
        // 进新战斗房（斗蛐蛐连打第二场）时，把上一场留下的面板/层/翻侧计数全拆掉。
        CharacterBosses.UI.BossUiSession.EnsureFresh();
        try { TickInner(source); }
        finally
        {
            // 诊断：每 120 帧打一次（avg/max 用来看偶发尖峰）
            try
            {
                double ms = swTick.Elapsed.TotalMilliseconds;
                _tickCalls++;
                _tickSumMs += ms;
                if (ms > _tickMaxMs) _tickMaxMs = ms;
                if (_tickCalls % 120 == 0)
                {
                    Logger.Log($"[CharBoss] 帧回调耗时(每120帧): avg={_tickSumMs / 120:0.000}ms max={_tickMaxMs:0.000}ms");
                    _tickSumMs = 0; _tickMaxMs = 0;
                }
            }
            catch { /* 忽略 */ }
        }
    }

    // ── 出牌队列卡死看门狗（诊断用，1Hz） ──────────────────────

    private static long _lastLeftoverUsec;

    /// <summary>
    /// 1Hz：**战斗已经结束**（`CombatManager.IsInProgress == false`）却还有卡面挂在出牌队列里
    /// ⇒ 说明是「出牌链被战斗结束截断」留下的残留，直接清掉。
    ///
    /// 用户 2026-11-02 实测场景：被 Boss 打中 →「百年积木」抽牌 →「地狱狂徒」自动打出抽到的
    /// 「剑柄打击+」→ 它又抽牌 → 又触发……Boss 在链条中途被打死，剩下的卡面就一直挂在屏幕中间。
    /// </summary>
    private static long _lastEndTurnProbeUsec;
    private static int _endTurnHiddenStreak;
    private static string _lastEndTurnProbeLine = string.Empty;

    /// <summary>
    /// 1Hz：把「结束回合按钮」的全部前置条件打一行（**只在有我们的 Boss 的战斗里**、只在状态变化时打）。
    /// 用户 2026-11-04 报「事件机还是没有结束回合按钮」—— 反编译的前置条件见调用处注释。
    /// </summary>
    private static void ProbeEndTurnButton(long nowUsec)
    {
        if (nowUsec - _lastEndTurnProbeUsec < 1_000_000L) return;
        _lastEndTurnProbeUsec = nowUsec;

        try
        {
            var combat = MegaCrit.Sts2.Core.Combat.CombatManager.Instance?.DebugOnlyGetState();
            if (combat == null) return;
            var room = MegaCrit.Sts2.Core.Nodes.Rooms.NCombatRoom.Instance;
            var me = MegaCrit.Sts2.Core.Context.LocalContext.GetMe(combat);

            bool inProgress = false;
            try { inProgress = MegaCrit.Sts2.Core.Combat.CombatManager.Instance?.IsInProgress ?? false; } catch { /* 忽略 */ }
            int extraTurnCount = 0;
            bool extraTurnHasMe = false;
            try
            {
                var list = MegaCrit.Sts2.Core.Combat.CombatManager.Instance?.PlayersTakingExtraTurn;
                if (list != null)
                {
                    extraTurnCount = list.Count;
                    if (me != null) extraTurnHasMe = list.Contains(me);
                }
            }
            catch { /* 忽略 */ }

            string mode = "?", handMode = "?", inCardPlay = "?", inSelection = "?";
            bool isCurrentScreen = false;
            try { mode = room?.Mode.ToString() ?? "null"; } catch { /* 忽略 */ }
            try
            {
                var hand = room?.Ui?.Hand;
                if (hand != null)
                {
                    handMode = hand.CurrentMode.ToString();
                    inCardPlay = hand.InCardPlay.ToString();
                    inSelection = hand.IsInCardSelection.ToString();
                }
            }
            catch { /* 忽略 */ }
            try
            {
                isCurrentScreen = room != null
                    && MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext.ActiveScreenContext.Instance.IsCurrent(room);
            }
            catch { /* 忽略 */ }

            string buttonState = "?", buttonVisible = "?";
            try
            {
                var btn = room?.Ui?.EndTurnButton;
                if (btn != null)
                {
                    buttonVisible = btn.Visible.ToString();
                    var f = btn.GetType().GetField("_state",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    buttonState = f?.GetValue(btn)?.ToString() ?? "?";
                }
            }
            catch { /* 忽略 */ }

            string meAlive = "?";
            try { meAlive = me?.Creature?.IsAlive.ToString() ?? "null"; } catch { /* 忽略 */ }

            string line = $"inProgress={inProgress} side={combat.CurrentSide} me={(me == null ? "null" : "ok")} " +
                          $"meAlive={meAlive} extraTurns={extraTurnCount}(含我={extraTurnHasMe}) " +
                          $"roomMode={mode} isCurrentScreen={isCurrentScreen} handMode={handMode} " +
                          $"inCardPlay={inCardPlay} inSelection={inSelection} " +
                          $"btnState={buttonState} btnVisible={buttonVisible}";
            // ★ v0.2.354（用户 2026-11-04）：**保险** —— 前置条件全满足、可按钮仍然是 `Hidden` 时，
            //   说明它是被谁藏了之后没人再打开（`RefreshEnabled()` 只会在 `Enabled` 状态上做减法）。
            //   连续 2 次探针（约 2 秒）都这样，就用反射调它自己的 `SetState(State.Enabled)` 把按钮打开。
            //   只在我们 Boss 的战斗里、且玩家还没点过「结束回合」（`AllPlayersReadyToEndTurn() == false`）时才动手。
            try
            {
                if (buttonState == "Hidden" && inProgress
                    && combat.CurrentSide == MegaCrit.Sts2.Core.Combat.CombatSide.Player
                    && me != null && me.Creature != null && me.Creature.IsAlive
                    && extraTurnCount == 0 && !extraTurnHasMe
                    && mode == "ActiveCombat" && inSelection != "True" && inCardPlay != "True")
                {
                    bool readyToEnd = false;
                    try
                    {
                        readyToEnd = MegaCrit.Sts2.Core.Combat.CombatManager.Instance?
                            .AllPlayersReadyToEndTurn() ?? false;
                    }
                    catch { readyToEnd = false; }

                    if (!readyToEnd)
                    {
                        if (++_endTurnHiddenStreak >= 2)
                        {
                            _endTurnHiddenStreak = 0;
                            var btn = room?.Ui?.EndTurnButton;
                            if (btn != null)
                            {
                                var setState = typeof(MegaCrit.Sts2.Core.Nodes.Combat.NEndTurnButton)
                                    .GetMethod("SetState",
                                        System.Reflection.BindingFlags.Instance
                                        | System.Reflection.BindingFlags.NonPublic);
                                var stateType = typeof(MegaCrit.Sts2.Core.Nodes.Combat.NEndTurnButton)
                                    .GetNestedType("State", System.Reflection.BindingFlags.NonPublic);
                                if (setState != null && stateType != null)
                                {
                                    var enabled = Enum.Parse(stateType, "Enabled");
                                    setState.Invoke(btn, new[] { enabled });
                                    _lastEndTurnProbeLine = string.Empty;     // 让下一行探针把新状态打出来
                                    Logger.Log("[CharBoss] 结束回合按钮保险：条件全部满足但按钮仍是 Hidden ⇒ 已强制打开（Enabled）");
                                }
                            }
                        }
                    }
                    else _endTurnHiddenStreak = 0;
                }
                else _endTurnHiddenStreak = 0;
            }
            catch (Exception e) { Logger.Log("[CharBoss] 结束回合按钮保险失败: " + e.Message); }
            if (line == _lastEndTurnProbeLine) return;      // 状态没变就不刷屏
            _lastEndTurnProbeLine = line;
            Logger.Log("[CharBoss] 结束回合按钮探针：" + line);
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 结束回合按钮探针失败: " + e.Message);
        }
    }
    private static void SweepLeftoverCardsAfterCombat(long nowUsec)
    {
        if (nowUsec - _lastLeftoverUsec < 1_000_000L) return;
        _lastLeftoverUsec = nowUsec;

        bool inProgress;
        try { inProgress = MegaCrit.Sts2.Core.Combat.CombatManager.Instance?.IsInProgress ?? false; }
        catch { return; }                       // 判断不了不动手
        if (inProgress) return;                 // 战斗还在进行：玩家的牌面还在用，绝不能清

        try { StrayBossCardQueuePatch.ReapLeftoversAfterCombat(); } catch { /* 忽略 */ }
    }

    private static readonly System.Collections.Generic.Dictionary<MegaCrit.Sts2.Core.Nodes.Cards.NCard, long> _queueSeen = new();
    private static long _lastWatchUsec;

    /// <summary>
    /// 1Hz 扫一遍 `NCardPlayQueue`：**我们自己的 Boss 打出的牌**滞留超过 3 秒就打一行（每张只报一次）。
    /// 只读、不改任何东西 —— 目的是下次「卡死」时能从日志看出是哪张牌、卡在哪。
    ///
    /// ★ v0.2.292（用户 2026-11-02 从表里看到两条 diag：「出牌队列滞留：SURVIVOR（拥有者 WBZ）」
    ///   与「FUSED_GU_ZHEN_REN_CARD_SHI_BAI_GU…（拥有者 古月方源）」）：
    ///   两条的**拥有者都是真人玩家**，不是我们的 Boss ⇒ 不是本模组的 bug，也根本不是卡死：
    ///     · 生存者 `Survivor.OnPlay` 是 `GainBlock(8)` 之后 `CardSelectCmd.FromHandForDiscard(…, 1)`
    ///       —— **等玩家自己挑一张牌弃掉**，选牌面板开着的时候那张牌的卡面本来就一直挂在出牌队列上，
    ///       玩家想 5 秒就挂 5 秒；
    ///     · 蛊真人那张 `FUSED_…` 是别的模组的融合牌（同类：长演出 / 要选择 / 网络延迟）。
    ///   以前这条看门狗扫的是**整个队列**（谁的牌都报）⇒ 只要玩家手快或者多想一会儿就会被我们记一条 ⚠，
    ///   还会被数据收集收走传进表里。
    ///   现在只看**我们 Boss 的牌**（判据与 <see cref="StrayBossCardQueuePatch.IsOurBossCard"/> 同一个：
    ///   `card.Model.Owner.Creature.Monster is CharBossMonster`）—— 她的牌走 `CardCmd.AutoPlay`、
    ///   选择器由我们自动回答，队列本该秒清，滞留 3 秒才是真线索。
    /// </summary>
    private static void WatchPlayQueue(long nowUsec)
    {
        // ★ v0.2.271（用户 2026-10-31）：看门狗**只在我们的 Boss 战里**跑。
        //   以前是全局扫 NCardPlayQueue ⇒ 玩家在别的模组的幕里（第四幕的 `SUKUNA-SUKUNA_GAU…`、
        //   别的模组角色的 `TOJI-QUICKSTEP（拥有者 伏黑甚尔）`）出牌一慢，我们就报一行 ⚠，
        //   而 ⚠ 会被数据收集收走 ⇒ 别人的幕/遭遇混进我们的表格。与本模组无关的战斗一律不看。
        try
        {
            if (!CharacterBosses.Core.BossShadowPets.BossFightActive(
                    MegaCrit.Sts2.Core.Combat.CombatManager.Instance?.DebugOnlyGetState()))
            {
                _queueSeen.Clear();
                return;
            }
        }
        catch { /* 判断失败就当不在我们的战斗里 */ _queueSeen.Clear(); return; }

        if (nowUsec - _lastWatchUsec < 1_000_000L) return;
        _lastWatchUsec = nowUsec;

        var queue = MegaCrit.Sts2.Core.Nodes.Combat.NCardPlayQueue.Instance;
        if (queue == null || !GodotObject.IsInstanceValid(queue)) { _queueSeen.Clear(); return; }

        var alive = new System.Collections.Generic.HashSet<MegaCrit.Sts2.Core.Nodes.Cards.NCard>();
        foreach (var child in queue.GetChildren())
        {
            if (child is not MegaCrit.Sts2.Core.Nodes.Cards.NCard card) continue;
            if (!Godot.GodotObject.IsInstanceValid(card)) continue;
            // ★ v0.2.292：**只盯我们 Boss 的牌**。真人玩家的牌会合法地挂在队列里（选牌 / 网络 / 长演出）。
            if (!StrayBossCardQueuePatch.IsOurBossCard(card)) continue;
            alive.Add(card);
            if (!_queueSeen.ContainsKey(card)) { _queueSeen[card] = nowUsec; continue; }

            long age = nowUsec - _queueSeen[card];
            if (age < 3_000_000L) continue;                                  // 3 秒内属正常出牌
            string entry = "?", owner = "?", ages = $"{(age / 1_000_000.0):0.0}s";
            try { entry = card.Model?.Id.Entry ?? "?"; } catch { }
            try { owner = card.Model?.Owner?.Creature?.Name ?? "?"; } catch { }
            Logger.Log($"[CharBoss] ⚠ 出牌队列滞留：{entry}（拥有者 {owner}）已在队列里 {ages}" +
                       $"，队列内我们 Boss 的卡面 {alive.Count} 个 —— 若随后卡死，这一行就是线索");
            _queueSeen[card] = nowUsec;                                      // 每 3 秒最多报一次，别刷屏
        }

        // 清掉已经离开队列的记录
        if (_queueSeen.Count > alive.Count)
        {
            var gone = new System.Collections.Generic.List<MegaCrit.Sts2.Core.Nodes.Cards.NCard>();
            foreach (var kv in _queueSeen) if (!alive.Contains(kv.Key)) gone.Add(kv.Key);
            foreach (var g in gone) _queueSeen.Remove(g);
        }
    }

    private static void TickInner(string source)
    {
        try
        {
            CharacterBosses.Core.BuildInfo.LogOnce();

            // 「这个来源到底有没有在跑」——每个来源只报一次，用来定位驱动源
            if (Announced.Add(source))
            {
                Logger.Log($"[CharBoss] 帧驱动源已生效: {source}");
            }

            var now = Time.GetTicksUsec();

            // ①⓪⓪ 出牌队列卡死看门狗（用户 2026-10-30：「杀个小怪怎么卡死了」，日志停在死亡那一行、
            //   之后连帧回调都没有 ⇒ 主线程卡住）。我们不猜原因，只在有卡面滞留在
            //   `NCardPlayQueue` 里超过 3 秒时打一行（谁的牌、什么牌、多久），下次直接看日志定位。
            //   ⚠ 放在 `boss == null` 早退之前 —— 普通小怪战（没有我们的 Boss）也要能看见。
            try { WatchPlayQueue((long)now); } catch { /* 忽略 */ }

            // ①⓪c ★ v0.2.353（用户 2026-11-04：「事件机还是没有结束回合按钮」）：
            //   把「结束回合按钮该不该出现」的每一个前置条件都打出来（1Hz，只在有我们的 Boss 的战斗里）。
            //   反编译 `NEndTurnButton`：
            //     · 显示条件 = `state.CurrentSide == Player && CombatManager.Instance.IsInProgress
            //                   && PlayerCanTakeAction(localPlayer)`；
            //     · `PlayerCanTakeAction` = 自己活着 && （`PlayersTakingExtraTurn` 为空 或 含自己）；
            //     · `RefreshEnabled` 里还会因为 `NCombatRoom.Mode != ActiveCombat`、
            //       战斗房不是当前 screen context、或 `Ui.Hand.IsInCardSelection` 而**藏掉**按钮。
            //   这些值一打出来就知道是哪一条不成立。
            try { ProbeEndTurnButton((long)now); } catch { /* 忽略 */ }

            // ①⓪b ★ v0.2.295：**战斗结束后**兜一次「出牌队列残留卡面」
            //   （用户 2026-11-02：「一堆剑柄打击+ 把 boss 揍死了，卡还卡在屏幕中间」——
            //    那条自动出牌链被「战斗结束」截断，原版收卡面那一步不跑了；
            //    我们的 Reap 只收她自己打出的牌，玩家的这类残留没人清）。
            //   同样放在 `boss == null` 早退之前：战斗结束时她已经被释放了。
            try { SweepLeftoverCardsAfterCombat((long)now); } catch { /* 忽略 */ }

            // ⚠ **同一帧只跑一次**：这个 Tick 有 3 个驱动源（辉星计数器 / 能量球 / 君王之剑），
            //   它们各自每帧都会调进来一次 ⇒ 以前一帧最多跑 3 遍（4 只 Boss 时就是 12 份工作）。
            //   用户 2026-10-29 四人局实测 fps 掉到 2~7、单次帧回调最大 131ms。
            //   用「已渲染帧号」当闸门，无论多少个来源、每帧都只做一次。
            try
            {
                int frame = (int)Engine.GetFramesDrawn();
                if (frame == _lastFrame) return;
                _lastFrame = frame;
            }
            catch { /* 拿不到帧号就走时间闸门 */ }

            if (now - _lastUsec < MinIntervalUsec) return;
            _lastUsec = now;

            var boss = BossHandUi.CurrentBoss;
            if (boss == null) return;

            // 性能取样（只读两个数字）：把「这一秒的 fps」和「我们重建了多少次、花了多久」对齐打出来，
            // 用来回答「boss 战掉帧到底是不是本模组造成的」。
            SampleFrameStats();

            // ①⓪ 状态栏图标节点对账（用户 2026-10-29 唯一遗留：一个人身上出现两个「跟踪锁定」图标）。
            //     原版 `NPowerContainer.Add` 不去重、`Remove` 在战斗未进行时整段跳过，
            //     而且按引用只摘第一个匹配项 ⇒ 一旦多出一个节点就永久残留。
            //     这里 5Hz 对账一次，只回收「背后已经没有活 Power」和「同一份 Power 的第二个节点」，
            //     与可见性判定无关（队友身上的图标该不该显示仍由原版 IsVisible 决定）。
            PowerIconNodeReconcile.Tick(now, PowerIconReconcileIntervalUsec);

            // ①① BGM 战后延迟还原（用户 2026-10-30：三层 Boss 战没有胜利音效）。
            //     战斗结束后我们的曲目要再放几秒（让游戏的 `Progress=7` 胜利段落在它上面播完），
            //     到点且确实不在战斗里才 `StopCustomMusic()`。放在慢车道（4Hz）里跑。
            try { CharacterBosses.Core.BossBgm.TickRestore((long)now); } catch { /* 忽略 */ }

            // 多人「多 Boss 同场」：每帧要处理的**不只是面板上那只** ——
            // 第二只 Boss 的随从/奥斯提/站位/死亡清理同样得跑，否则会漏（设计见 multiplayer-lockon.md）。
            // ⚠ 这一帧里**只算一次**（原来下面被调用 5 次，每次都新建 List 并遍历 Enemies）。
            var everyBoss = AllBosses(boss);

            // 药水真正变了（喝掉 / 炼出来 / 混沌药水补满）→ 立刻刷面板。
            // 有了这条，「哪段代码忘了调 Refresh」都不影响实时性：BossPotions.Version 是唯一真相。
            // 用 Refresh（增量）而不是 RefreshForce：只重画状态条与变了的格子，不整排清空。
            foreach (var each in everyBoss) RefreshIfPotionsChanged(each);

            // 被击晕 / 击晕解除（吹哨等）→ 手牌全暗、全部意图撤掉；解除时立刻恢复（用户 2026-11-04）。
            // 每帧只读一个 bool，翻转才重建面板 ⇒ 不需要额外的失效标记。
            foreach (var each in everyBoss) RefreshIfStunnedChanged(each);

            // ───────────────── 每帧（跟手，不能降频） ─────────────────

            foreach (var each in everyBoss)
            {
                try
                {
                    BossShadowPets.RemoveOnBossDeath(each);          // 内部：主人没死立刻返回
                    CharacterBosses.Core.BossOsty.EnsureDeathHook(each);   // 一次性：挂「死→带走奥斯提」
                    CharacterBosses.Core.BossOsty.RemoveOnBossDeath(each);
                    // 无界命匣的「战斗开始召唤 5」：战斗真的开始了的第一帧补一次（用户 2026-11-04）
                    CharacterBosses.Core.BossOsty.TryCombatStartSummon(each);
                    if (each.Creature == null || each.Creature.IsDead || !each.Creature.IsAlive)
                    {
                        CharacterBosses.Core.BossLockOn.OnBossDied(each);

                        // **最后一只** Boss 倒下的这一刻：**不要停音乐**！
                        // 用户 2026-10-30：「三层 Boss 战 BGM 放一会就没了，也没有胜利音效」。
                        // 反编译实证（`CombatManager.EndCombatInternal`）：战斗一结束它就调
                        // `NRunMusicController.UpdateTrack()` ⇒ `update_global_parameter("Progress", 7)`
                        // （`MusicProgressTrack.CombatEnd`）—— **胜利段落是在「当前正在响的那条曲目」上切出来的**。
                        // 我们以前在这一帧 `StopCustomMusic()`（= `stop_music`）⇒ 刚起的胜利段落被掐掉，
                        // 而且 v0.2.175 把它挪到「移出房间」那步也一样掐（那步同样早于/紧贴战斗结束）。
                        // 现在：只记账 + 曲目再放 4 秒（VictoryKeepMs），由慢车道 `TickRestore` 还原原版音乐。
                        if (!CharacterBosses.Core.MultiBossPlan.AnyOtherBossAlive(each))
                        {
                            CharacterBosses.Core.BossBgm.OnLastBossDied();
                        }
                    }
                }
                catch { /* 单只失败不影响其它 */ }
            }

            // 多人「跟踪锁定」：Boss 一死就撤掉它那把锁（设计见 multiplayer-lockon.md §阶段 1 第 5 条）
            try
            {
                if (boss.Creature == null || boss.Creature.IsDead || !boss.Creature.IsAlive)
                    CharacterBosses.Core.BossLockOn.OnBossDied(boss);

                // 兜底：主人已死的锁一律清掉（死掉的 Boss 可能不在 Enemies / 面板里，见方法注释）
                CharacterBosses.Core.BossLockOn.PurgeDeadBosses();
            }
            catch { /* 忽略 */ }

            // 集中变了 → 激发意图要重算（力量与集中在本 mod 里恒等，球数值会跟着动）
            // 多 Boss：每只各查一次（每帧遍历 2 只、只读一个 Power 数字，开销可忽略）
            foreach (var each in everyBoss) RefreshIntentsIfFocusChanged(each);

            // 灵魂绑定：玩家回合内抽了牌（或她自己又额外抽了牌）→ 死亡行军的伤害预览要跟着变。
            // 放在帧驱动里做（不在抽牌回调里直接重排 UI），并且只在真的有变化时跑一次。
            foreach (var each in everyBoss) RefreshIntentsIfDrawsChanged(each);

            // 她身上的**能力层数**变了（玩家的减力量/易伤/虚弱、模组的临时减力量…）→ 意图数字要重算。
            // 用户 2026-10-23：崩坠「哀嚎」减了她 11 点力量，意图还挂着 10×4。
            foreach (var each in everyBoss) RefreshIntentsIfPowersChanged(each);
        // ★ 2026-11-07 用户：「能力机被减力量/集中后，充能球被动的伤害没有及时更新」。
        //   充能球被动那条意图的数字来自面板重建，而面板只在若干事件里重建 ⇒
        //   她身上「力量 / 集中 / 球的构成」被**外部**改动时不刷新。这里按指纹兜底：一变就强制重画。
        foreach (var each in everyBoss) RefreshPanelIfStatsChanged(each);

        // ★ v0.2.484（用户 2026-11-08：「为什么我打一张打击伤害−6？」）：
        //   根因是**时序**：打出的那张打击在「出牌中」的短暂几帧里既不在手牌也不在弃牌堆，
        //   于是牌堆统计少 1（−3）、已打出计数又多 1（−3）⇒ 被算成 −6，而且那一刻算出来的数值会**粘住**。
        //   这里改成：**每帧**都按当前真实牌堆重算一次（只对挂着 BossBashPower 的她，成本可忽略），
        //   等那张牌落进弃牌堆后下一帧就会自动修正回 −3。
        foreach (var each in everyBoss)
        {
            try
            {
                if (each?.Creature?.GetPower<CharacterBosses.Powers.BossBashPower>() == null) continue;
                // ★ v0.2.486（用户 2026-11-08：「每回合第一次打出打击伤害会 −6」）：
                //   重算之后必须**把面板也重画**，否则玩家看到的是上一帧那个「出牌中途」的错数（−6）。
                int before = Core.BossScriptRunner.BashCalcSnapshot(each);
                Core.BossScriptRunner.RetuneBashPerfectStrike(each);
                int after = Core.BossScriptRunner.BashCalcSnapshot(each);
                if (before != after && each.Plan.Count > 0)
                {
                    each.RefreshPlannedIntents();
                    BossHandUi.RefreshForce(each);
                }
            }
            catch { /* 忽略 */ }
        }

            // ① 手牌栏跟随 Boss（原先写在 OnFrame 里，等于从没跑过）
            //    多人「双 Boss 两套面板」：**每只 Boss 各跑一次自己的面板**
            //    （每套面板只跟自己的 Boss，见 BossHandUi 的多实例改造）。
            foreach (var panelBoss in BossHandUi.PanelBosses())
            {
                try
                {
                    var eachPanel = BossHandUi.PanelFor(panelBoss);
                    if (eachPanel != null && GodotObject.IsInstanceValid(eachPanel)) eachPanel.RunFrame();
                }
                catch { /* 单套失败不影响其它 */ }
            }

            // ①b 「炸弹」倒计时剩 1 ⇒ 让那个能力图标闪烁（用户 2026-10-29，照 1 代崩坠商人的观感）。
            //     每帧跑（要跟手），节点带缓存；没有炸弹的 Boss 直接返回。
            try { BombBlink.Tick(everyBoss); } catch { /* 忽略 */ }

            // ①c 钻石头冠的 20 点格挡**不再在这里补**（v0.2.241）。
            //    以前这里每帧调 `EnsureDiamondDiademBlock`：只要她还没格挡就 `Task.Delay(120)` 后异步补 20 点 ——
            //    而「战斗开始清零」的时序两端不同 ⇒ 有的客户端补到了 20、有的没补到 0
            //    ⇒ 联机时同一只 Boss 两端格挡不同 ⇒ 校验和分叉（2026-10-30 RitsuLib 诊断包实测：
            //    `creatures[6].block: local=20; remote=0`，怪物是 `MONSTER.SILENT_BOSS`）。
            //    **战斗状态绝不能在帧回调里改**。现在它由 `DiademBlockSyncPatch`（同步钩子
            //    `Hook.AfterSideTurnStart(Player)`）在两端一致地补，见 BossHookBridge.GrantDiademBlockSync。

            // ② 君王之剑剑尖朝左
            BossSovereignBlade.Ensure(boss);

            // ③ 影子玩家名下的随从（佩尔的士兵）：朝向朝左 + 镜像摆位
            //    （朝向必须每帧钉死：原版 SurroundedPower 会随时把它写正，见 BossShadowPets.ForceFaceLeft）
            //    多 Boss：**每只都要跑** —— 以前只给面板上那只跑，队友 Boss 的士兵既不会朝左、
            //    也不会被登记成爪牙（多人同场时那是看得见的错）。
            foreach (var each in everyBoss)
            {
                try { BossShadowPets.Update(each); } catch { /* 单只失败不影响其它 */ }
            }

            // ④ 奥斯提：**朝向 + 血条/状态栏每帧确认**（复活出来的节点会被原版写回朝右，
            //    并把「非本地玩家随从」的状态显示整块压掉 → 用户实测：朝右、没有血量和状态栏）。
            //    注：这两条以前每帧各扫一遍整个房间找奥斯提节点，是「boss 战卡」的主因之一；
            //    已在 BossOsty.HerLiveOstyNode 里改成「没有随从就不扫 + 5Hz 节流 + 按 Boss 缓存」。
            //    多 Boss：**每只都要跑** —— 多人换牌 A2（骸骨军团）会给队友 Boss 也召一只奥斯提，
            //    它的爪牙标记/朝向/站位都在这里面（见 docs/plans/multiplayer-cards.md §2）。
            foreach (var each in everyBoss)
            {
                try { BossOsty.EnforceVisuals(each); } catch { /* 单只失败不影响其它 */ }

                // ⑤ 奥斯提站位：两侧都跑 —— 只在它**跑到 Boss 右手边**时拉回来，
                //    自己出手时的攻击位移不会被插手（用户实测：复活后整个她回合都挂在右边）。
                try { BossOsty.EnforcePlacement(each); } catch { /* 单只失败不影响其它 */ }
                // 血条配色：她有格挡时前景必须是银蓝色（原版 RefreshBlockUi 只在本机玩家的链路上跑，
                // 影子玩家收不到 ⇒ 一直是红的）。用户 2026-11-02：「拥有格挡时，boss 的血条应该是
                // 和格挡一样的银蓝色的」。
                try { Core.BossBlockBarTint.Enforce(each); } catch { /* 纯显示，失败不影响 */ }
            }

            // ───────────────── 4Hz 慢车道（纯显示纠正 + 重活） ─────────────────
            if (now - _lastSlowUsec < SlowIntervalUsec) return;
            _lastSlowUsec = now;

            // ★ v0.2.410（用户 2026-11-05：「充能球被动还是没有被薄荷尖塔识别」）：
            //   薄荷尖塔（MintySpire2）**比我们晚加载** ⇒ `BuildInfo.LogOnce` 那一刻它的类型还不在
            //   已加载程序集里，一次失败就再没机会重试（日志实证：只有 `暂时没找到 ⇒ 稍后重试`，
            //   之后永远没有「已给她补上」那一行）。这里放到 4Hz 慢车道里持续重试
            //   （`TryInstall` 内部只在「程序集数量变了」时才真的扫类型，装好之后立刻早退，开销可忽略）。
            try { Core.MintySpireDamagePreviewCompat.TryInstall(); } catch { /* 忽略 */ }
            // ★ 2026-11-05（用户：「召唤的 2 个仆从叠一起了，让它们分开」）：
            //   仆从是战斗中陆续召出来的，`PositionEnemies` 只在进战斗/重排时跑一次 ⇒
            //   后召出来的那只没被摆过，就和前一只重叠。这里放慢车道兜一次（纯坐标，不改状态）。
            try { BossEnemySpreadPatch.PlaceMinions(); } catch { /* 忽略 */ }

            // ★ v0.2.412：开信刀的「真实技能牌计数」诊断（值变了才打一行）——
            //   用户说「全息影像不应该标注打 5」，这一行能直接告诉我们她遗物里的计数器到底走到几。
            foreach (var each in everyBoss)
            {
                try { Core.StatusBossMath.LogLetterOpenerCounter(each); } catch { /* 纯诊断 */ }
            }

            // 2b. 多 Boss 同场：把坐标各记一次（只记录，不改行为；内部有 HashSet，只记一次）
            LogBossPositionsOnce(everyBoss);

            // 原版「出牌队列」会把**怪物打出**的牌当成「远程玩家出牌」挂成一张卡面，而且没人收走
            // ⇒ 一张大卡永久吊在屏幕中上方（用户 2026-10-29：「奇巧猎的打击停在了空中」）。
            // 这里在慢车道兜一次（出牌那一刻还会立刻扫一遍，见 CharBossMonster.PlayOne）。
            try { StrayBossCardQueuePatch.Reap(); } catch { /* 单次失败不影响其它 */ }

            // 形态特效（群蛇/恶魔/回响/死神/虚空…）：她立绘是靠翻 Body 镜像的，而形态特效挂在独立的 FormVfxHolder 上
            // ⇒ 不继承镜像（默认朝右、背对玩家）。这里 4Hz 兜一次，重建/重置后也能翻回来。
            foreach (var each in everyBoss)
            {
                try { FormVfxFacingPatch.EnsureFacing(each); } catch { /* 单只失败不影响其它 */ }
            }

            // 「这一手算不算攻击」对第三方永远可见（用户 2026-10-26：眼部攻击上不了虚弱、别的 mod 同类牌也出问题）。
            // 反编译：`GoForTheEyes` 问的是 `Target.Monster.IntendsToAttack`
            //      ⇒ `NextMove.Intents.Any(i => i.IntentType is Attack or DeathBlow)`。
            // 我们的意图是自己画的，只有把它**写进原版 MoveState.Intents** 第三方才看得见；
            // 而 `NextMove` 会在 `RollMove` 时被换成新实例 ⇒ 4Hz 幂等地重写一遍最省心
            // （只做两次反射赋值，开销可忽略；写得进去就不再有「意图是攻击却判成非攻击」）。
            foreach (var each in everyBoss)
            {
                try { each.PublishVanillaIntents(); } catch { /* 单只失败不影响其它 */ }
            }

            // 多人「跟踪锁定」的均衡分配：**不需要每帧**（锁定关系只在开战与死亡时变），
            // 挪到慢车道省掉每帧那两次小 List 分配。
            try { CharacterBosses.Core.BossLockOn.Rebalance(everyBoss); } catch { /* 忽略 */ }

            // 2c. 多 Boss：位置错开改由 `PositionEnemies` 的 postfix 处理（见文件末尾 BossEnemySpreadPatch），
            //     每帧硬写会被原版布局拉回去；每只各自挂特殊效果框的逻辑保留（多人时全部不挂）。
            EnsureSpecialUiForAll(everyBoss);

            // 多 Boss：**每只都已经有自己完整的一套面板**（手牌 + 遗物/能量/辉星 + 意图），
            // 所以那条「给面板外那只补一条状态栏」的补丁不再需要 —— 收掉它，避免同一只 Boss 出现两条状态栏。
            try
            {
                if (everyBoss.Count >= 2) CharacterBosses.UI.BossExtraStatusRow.HideAll();
            }
            catch { /* 忽略 */ }

            // 多 Boss 时把我们自己的手牌面板整体缩一点（用户要求：显示空间太紧张）。
            // 只缩我们挂上去的节点，不影响原版 UI；单人恢复 1.0。**每套面板各自缩**。
            try
            {
                foreach (var panelBoss in BossHandUi.PanelBosses())
                {
                    var uiPanel = BossHandUi.PanelFor(panelBoss);
                    if (uiPanel == null || !GodotObject.IsInstanceValid(uiPanel)) continue;

                    // 只在「**同一侧**有两只以上我们的 Boss」时才缩。
                    // 斗蛐蛐是红蓝各一只，按原来的 everyBoss.Count 会误判成多 Boss，
                    // 于是每帧在 0.85 / 1.0 之间来回跳 —— 就是用户看到的「战斗卡回合时 UI 缩放」。
                    int sameSide = 1;
                    try
                    {
                        var combat = panelBoss.Creature?.CombatState;
                        if (combat != null && panelBoss.Creature != null)
                            sameSide = combat.GetTeammatesOf(panelBoss.Creature).Count(t => t is CharBossMonster);
                    }
                    catch { /* 忽略 */ }

                    var want = sameSide >= 2 ? new Vector2(0.85f, 0.85f) : Vector2.One;
                    if (uiPanel.Scale.DistanceTo(want) > 0.001f) uiPanel.Scale = want;
                }
            }
            catch { /* 忽略 */ }

            // 铸造/攻击动画用的剑不是常驻那把节点 → 兜底扫描整个场景把带 Sword/Blade 的节点也翻转
            BossSovereignBlade.MirrorAllInScene();

            // ②b 场上**每一只**我们的 Boss 的剑也都要补齐（多 Boss 同场时面板外那只同样需要）
            foreach (var each in everyBoss)
            {
                if (ReferenceEquals(each, boss)) continue;
                try { BossSovereignBlade.Ensure(each); } catch { /* 忽略 */ }
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 帧驱动异常: " + e.Message);
        }
    }

    /// <summary>
    /// 药水一变（<see cref="CharacterBosses.Core.BossPotions.Version"/> 递增）就刷一次面板。
    ///
    /// 为什么放在每帧驱动里：喝药水的路径有三条（回合开始的剧本药水 / 某张牌之前的「先喝一瓶」/
    /// 「炼制药水」炼出来一瓶），三条都改 <c>Player.PotionSlots</c>，但**谁都没义务记得调 Refresh**。
    /// 用户 2026-10-26 报的「喝药后药水栏不实时变化」就是这类漏刷：数据早就对了（日志里「喝药后栏位剩 N 瓶」
    /// 与状态条 <c>held=N</c> 一致），只是没人让界面重画。现在以版本号为准，改完当帧就重画。
    /// </summary>
    private static readonly System.Collections.Generic.Dictionary<CharBossMonster, int> LastPotionVersion = new();

    private static void RefreshIfPotionsChanged(CharBossMonster boss)
    {
        try
        {
            if (boss == null) return;
            int version = CharacterBosses.Core.BossPotions.Version;
            if (!LastPotionVersion.TryGetValue(boss, out int last))
            {
                LastPotionVersion[boss] = version;   // 第一次见到这只：只记基线
                return;
            }
            if (last == version) return;
            LastPotionVersion[boss] = version;

            Logger.Log($"[CharBoss] 药水变动（版本 {last}→{version}）→ 重画状态条");
            BossHandUi.Refresh(boss);
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 药水变动刷新失败: " + e.Message);
        }
    }

    /// <summary>
    /// 她**被击晕 / 击晕解除**的那一帧 → 重排手牌栏（用户 2026-11-04：击晕时「立刻」让所有牌变暗、
    /// 撤掉所有意图；下回合补打时再亮回来）。
    ///
    /// 判定面是 <c>Creature.IsStunned</c>（反编译：<c>=> Monster?.NextMove.Id == "STUNNED"</c>），
    /// 由吹哨这类牌经 <c>CreatureCmd.Stun → Creature.StunInternal</c> 把移动状态机切到 STUNNED 而来。
    /// 那次切换发生在**玩家回合中途**，没有任何回调通知我们，所以这里按帧比对（一只一份，只读一个 bool）。
    /// 重建本身走 <see cref="BossHandUi.RefreshForce"/>：压暗/撤意图是在 `BossHandPanel.BuildCell`
    /// 里按 <c>boss.Stunned</c> 决定的，必须整排重来，增量同步不会重画已有格子。
    /// </summary>
    private static readonly System.Collections.Generic.Dictionary<CharBossMonster, bool> LastStunned = new();

    private static void RefreshIfStunnedChanged(CharBossMonster boss)
    {
        try
        {
            if (boss == null) return;
            bool stunned = boss.Stunned;
            if (!LastStunned.TryGetValue(boss, out bool last))
            {
                LastStunned[boss] = stunned;   // 第一次见到这只：只记基线
                return;
            }
            if (last == stunned) return;
            LastStunned[boss] = stunned;

            if (stunned)
            {
                Logger.Log($"[CharBoss] 击晕：本回合不行动 → 手牌全部压暗、撤掉全部意图" +
                           $"（待执行计划仍保留 {boss.Plan.Count} 张，TurnNumber={boss.TurnNumber}）");
            }
            else
            {
                Logger.Log($"[CharBoss] 击晕解除：恢复意图显示，补打待执行计划 {boss.Plan.Count} 张" +
                           $"（仍是第 {boss.TurnNumber} 回合的那一套）");
            }
            BossHandUi.RefreshForce(boss);
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 击晕状态刷新失败: " + e.Message);
        }
    }

    /// <summary>性能统计窗口长度（秒）。</summary>
    private const int PerfWindowSeconds = 5;
    private static ulong _perfWindowUsec;
    private static int _fpsMin = int.MaxValue;

    /// <summary>
    /// 每 5 秒汇总一条性能日志：`fps 现/最低 | 面板重建次数+均耗时 | 状态条重建次数+均耗时`。
    ///
    /// 目的（用户 2026-10-26）：「boss 战第三/四回合掉到十几帧，这总不能是原版导致的吧？」
    /// 之前日志只有「帧回调耗时 avg=0.05ms」，那是**我们自己那点活**的耗时，而重建节点的大头
    /// （遗物/药水图标、卡面格子）根本没被计时 —— 所以永远看不到问题。
    /// 现在把 fps 与「我们这一窗口重建了多少次 / 花了多少毫秒」放在同一行：
    ///   · fps 掉、重建次数也跟着涨 ⇒ 是我们的锅；
    ///   · fps 掉、重建次数是 0 ⇒ 与本模组无关（去看别的 mod / 原版特效）。
    /// </summary>
    private static void SampleFrameStats()
    {
        try
        {
            int fps = (int)Math.Round(Engine.GetFramesPerSecond());   // Godot 返回 double
            var now = Time.GetTicksUsec();

            if (_perfWindowUsec == 0)
            {
                _perfWindowUsec = now;
                _fpsMin = fps > 0 ? fps : int.MaxValue;
                return;
            }
            if (fps > 0 && fps < _fpsMin) _fpsMin = fps;
            if (now - _perfWindowUsec < PerfWindowSeconds * 1_000_000UL) return;

            _perfWindowUsec = now;
            int lowest = _fpsMin == int.MaxValue ? fps : _fpsMin;
            Logger.Log(CharacterBosses.Core.PerfLog.Drain(PerfWindowSeconds, fps, lowest));
            _fpsMin = fps > 0 ? fps : int.MaxValue;
        }
        catch { /* 诊断而已，绝不影响战斗 */ }
    }

    /// <summary>
    /// 多 Boss 同场时把每只 Boss 的屏幕坐标各记一次（**只记一次，不改任何东西**）。
    /// 用途：原版 <c>NCombatRoom.PositionEnemies</c> 是按「只有一只敌人」写的，
    /// 多 Boss 会不会重叠必须靠实机坐标确认 —— 有数据我再决定要不要加偏移（见 multiplayer-lockon.md §5）。
    /// </summary>
    private static readonly System.Collections.Generic.HashSet<int> _loggedBossPos = new();

    private static void LogBossPositionsOnce(System.Collections.Generic.List<Monsters.CharBossMonster> bosses)
    {
        if (bosses.Count < 2) return;   // 单人：不打扰日志
        try
        {
            var room = MegaCrit.Sts2.Core.Nodes.Rooms.NCombatRoom.Instance;
            if (room == null) return;

            var parts = new System.Collections.Generic.List<string>();
            foreach (var b in bosses)
            {
                if (b?.Creature == null) continue;
                if (!_loggedBossPos.Add(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(b))) continue;
                var node = room.GetCreatureNode(b.Creature);
                parts.Add($"{b.Id.Entry}@{(node != null ? node.GlobalPosition.ToString() : "?")}");
            }
            if (parts.Count > 0)
            {
                Logger.Log($"[CharBoss] 多 Boss 站位（{bosses.Count} 只）：{string.Join(" | ", parts)}");
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 多 Boss 站位记录失败: " + e.Message);
        }
    }

    /// <summary>
    /// 多 Boss 同场时把两只**拉开**（用户要求：站得开一点才有位置显示各自的卡牌/遗物/能量）。
    ///
    /// 原版 `NCombatRoom.PositionEnemies` 是按「只有一只敌人」排的，两只 Boss 会几乎重叠。
    /// 做法：第一次见到某只 Boss 时把**原版给它的位置**记成基准，之后每帧按序号把它推到
    /// 「基准 + (i - (n-1)/2) × 间距」—— 不改原版排版逻辑，只是叠加一个稳定偏移。
    /// 单人（1 只）时整段不生效。
    /// </summary>
    // 注意：偏移按「两只见效值」直接给 —— 之前的 (±45, ±31) 太小，两只看着还是平行（用户实测日志）。
    private const float BossSpreadPx = 200f;
    private const float BossSpreadYPx = 140f;

    private static readonly System.Collections.Generic.Dictionary<
        MegaCrit.Sts2.Core.Entities.Creatures.Creature, Vector2> _bossBasePos = new();

    private static void SpreadBosses(System.Collections.Generic.List<Monsters.CharBossMonster> bosses)
    {
        if (bosses.Count < 2) return;
        try
        {
            var room = MegaCrit.Sts2.Core.Nodes.Rooms.NCombatRoom.Instance;
            if (room == null) return;

            for (int i = 0; i < bosses.Count; i++)
            {
                var creature = bosses[i]?.Creature;
                if (creature == null) continue;
                var node = room.GetCreatureNode(creature);
                if (node == null || !GodotObject.IsInstanceValid(node)) continue;

                if (!_bossBasePos.TryGetValue(creature, out var basePos))
                {
                    _bossPosOffset[creature] = new Vector2((i - (bosses.Count - 1) * 0.5f) * BossSpreadPx,
                                                           (i - (bosses.Count - 1) * 0.5f) * BossSpreadYPx);
                    // ⚠ 基准就是**此刻原版给的位置**，不要再减偏移 —— 上一版写成 `current − offset`，
                    // 于是 target = base + offset 又回到原位，等于什么都没动（用户实测：两只还是平行）。
                    basePos = node.GlobalPosition;
                    _bossBasePos[creature] = basePos;
                    Logger.Log($"[CharBoss] 多 Boss 站位基准：{bosses[i].Id.Entry}@{basePos}" +
                               $" 偏移={_bossPosOffset[creature]}（共 {bosses.Count} 只）");
                }

                // 用户要求：一只偏左上、一只偏右下，**竖直方向也要错开**。
                // X 偏移只做小幅（±100，别朝玩家那边挪），Y 偏移给足（±70）让它们上下明显分开。
                var target = basePos + _bossPosOffset[creature];
                if (node.GlobalPosition.DistanceTo(target) > 0.5f)
                {
                    // 同时写全局位置与本地位置：原版布局可能按 local Position 重新算，
                    // 只写 GlobalPosition 时会被它每帧拉回去（上一版实测两只挤在同一列）。
                    node.Position += target - node.GlobalPosition;
                    node.GlobalPosition = target;
                    _spreadWrites++;
                    if (_spreadWrites % 180 == 0)   // 约每 3 秒记一次，确认偏移是否真的站住了
                        Logger.Log($"[CharBoss] 多 Boss 纠偏：{bosses[i].Id.Entry} → {target}" +
                                   $"（实际 {node.GlobalPosition}）");
                }
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 多 Boss 拉开站位失败: " + e.Message);
        }
    }

    private static readonly System.Collections.Generic.Dictionary<
        MegaCrit.Sts2.Core.Entities.Creatures.Creature, Vector2> _bossPosOffset = new();

    private static int _spreadWrites;

    /// <summary>多 Boss 时**不显示**特殊效果详细说明框（用户要求：显示空间太紧张；单人不影响）。</summary>
    private static void EnsureSpecialUiForAll(System.Collections.Generic.List<Monsters.CharBossMonster> bosses)
    {
        // 每只各自维护（同阵营多 Boss 的"不显示"规则在 BossSpecialUi.Ensure 里按阵营判断）。
        foreach (var each in bosses)
        {
            try { CharacterBosses.UI.BossSpecialUi.Ensure(each); } catch { /* 单只失败不影响其它 */ }
        }
    }

    /// <summary>
    /// 场上所有「我们的」Boss（多 Boss 同场时会有两只以上）。
    /// 至少要包含面板上那只（<see cref="BossHandUi.CurrentBoss"/>），再并上战斗里所有
    /// <c>CharBossMonster</c> 的生物 —— 这样面板只显示一只也不影响另一只的每帧维护。
    /// </summary>
    private static System.Collections.Generic.List<Monsters.CharBossMonster> AllBosses(Monsters.CharBossMonster panelBoss)
    {
        var list = new System.Collections.Generic.List<Monsters.CharBossMonster> { panelBoss };
        try
        {
            var creatures = panelBoss.CombatState?.Enemies;
            if (creatures != null)
            {
                foreach (var c in creatures)
                {
                    if (c?.Monster is Monsters.CharBossMonster other && !list.Contains(other))
                        list.Add(other);
                }
            }
        }
        catch { /* 忽略 */ }
        return list;
    }

    /// <summary>
    /// 集中一变就重算意图并重画手牌栏（用户要求：激发球的伤害/格挡跟着集中走）。
    /// 换 Boss（新战斗）时只记录基线，不触发刷新。
    /// </summary>
    private static void RefreshIntentsIfFocusChanged(CharBossMonster boss)
    {
        int focus;
        try { focus = BossOrbMath.FocusOf(boss.Creature); }
        catch { return; }

        if (!LastFocus.TryGetValue(boss, out int last))
        {
            LastFocus[boss] = focus;      // 第一次见到这只：只记基线，不触发刷新
            return;
        }
        if (focus == last) return;

        Logger.Log($"[CharBoss] {boss.Id.Entry} 集中 {last} → {focus}：重算意图（充能球激发数值随之变化）");
        LastFocus[boss] = focus;
        try
        {
            if (boss.Plan.Count > 0)
            {
                boss.RefreshPlannedIntents();
                BossHandUi.RefreshForce(boss);
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 集中变化后重算意图失败: " + e.Message);
        }
    }

    /// <summary>
    /// 她身上的**能力层数**变了 → 重算意图（每帧比对一次「能力指纹」）。
    ///
    /// 为什么需要（用户 2026-10-23 根除骨截图）：意图数字只有在「重算」时才会更新，
    /// 而重算原先只由几种事件触发（回合开始、抽牌数变化、集中变化、她自己出牌…）。
    /// 玩家在她回合外用**任何**手段改她身上的能力（崩坠「哀嚎」这种「本回合失去 N 点力量」的
    /// 临时减力量、原版易伤/虚弱/活力、别 mod 的增伤…）都不会触发 ⇒ 数字停在规划期那套。
    ///
    /// 做法：把「能力的 id + 层数」拼成一个指纹，变了就按已有的 <c>IntentsDirty</c> 流程重算一次
    /// （消费方见 <see cref="RefreshIntentsIfDrawsChanged"/>）。只在**玩家侧行动时**打标 ——
    /// 她自己在出牌的回合里能力层数会不停变（力量/集中成长），那时候重算就是用户抱怨过的「来回闪」。
    /// </summary>
    private static readonly Dictionary<CharBossMonster, string> LastPowerPrint = new();

    private static void RefreshIntentsIfPowersChanged(CharBossMonster boss)
    {
        try
        {
            if (boss == null) return;
            var creature = boss.Creature;
            if (creature == null) return;

            var print = PowerPrintOf(creature);
            if (!LastPowerPrint.TryGetValue(boss, out var last))
            {
                LastPowerPrint[boss] = print;      // 第一次见到这只：只记基线
                return;
            }
            if (string.Equals(print, last, StringComparison.Ordinal)) return;
            LastPowerPrint[boss] = print;

            // 她自己回合里：能力层数的变化由出牌本身引起，让原来的流程去处理（避免数字来回闪）
            bool playerSide;
            try { playerSide = boss.CombatState?.CurrentSide == MegaCrit.Sts2.Core.Combat.CombatSide.Player; }
            catch { playerSide = false; }
            if (!playerSide) return;

            if (boss.Plan.Count > 0) boss.IntentsDirty = true;
        }
        catch { /* 忽略：绝不影响战斗 */ }
    }

    /// <summary>
    /// **她的「攻击性数值」变了就强制重画面板**（用户 2026-11-07：能力机被减力量/集中后，
    /// 充能球被动那条伤害不更新）。
    ///
    /// 为什么需要：球被动/意图的数字都是在**面板重建**时算的，而重建由若干事件触发；
    /// 当玩家在她回合外用卡牌/能力把她的**力量、集中**改小（偏差认知让力量与集中恒等，
    /// 所以减力量就等于减集中），或干脆改了球的构成时，这些事件一个都不会来 ⇒ 数字停在旧值。
    /// 这里每帧比一次「力量 + 集中 + 球序列」的指纹，变了就 `RefreshForce` 一次
    /// （只在**玩家侧**动手，避免她自己出牌那回合数字来回闪）。
    /// </summary>
    private static readonly Dictionary<CharBossMonster, string> LastStatsPrint = new();

    private static void RefreshPanelIfStatsChanged(CharBossMonster boss)
    {
        try
        {
            if (boss?.Creature == null) return;

            string print;
            try
            {
                int strength = boss.Creature.GetPowerAmount<MegaCrit.Sts2.Core.Models.Powers.StrengthPower>();
                int focus = Core.BossOrbMath.FocusOf(boss.Creature);
                var orbs = boss.Shadow?.PlayerCombatState?.OrbQueue?.Orbs;
                var names = new List<string>();
                if (orbs != null) { foreach (var o in orbs) names.Add(o == null ? "-" : o.GetType().Name); }
                print = strength + "/" + focus + "/" + string.Join(",", names);
                // ★ v0.2.482（用户 2026-11-08：「我打出打击，当回合完美打击的伤害预览不会变，应该实时计算」）：
                //   痛击现在是**按牌堆实时统计**的 ⇒ 把「名字含打击的牌数」也放进指纹。
                //   数字一变就在玩家回合里立刻重算完美打击并重画面板（只对挂着 BossBashPower 的她生效）。
                try
                {
                    if (boss.Creature.GetPower<CharacterBosses.Powers.BossBashPower>() != null)
                        print += "/strike=" + CharacterBosses.Powers.BossBashPower.CountNow(boss);
                }
                catch { /* 忽略 */ }
            }
            catch { return; }

            if (!LastStatsPrint.TryGetValue(boss, out var last)) { LastStatsPrint[boss] = print; return; }
            if (string.Equals(print, last, StringComparison.Ordinal)) return;
            LastStatsPrint[boss] = print;

            bool playerSide;
            try { playerSide = boss.CombatState?.CurrentSide == MegaCrit.Sts2.Core.Combat.CombatSide.Player; }
            catch { playerSide = false; }
            // 打击战：数字变了先把他手上/牌堆里的完美打击按新统计重算，再重画面板
            try
            {
                if (boss.Creature?.GetPower<CharacterBosses.Powers.BossBashPower>() != null)
                    Core.BossScriptRunner.RetuneBashPerfectStrike(boss);
            }
            catch { /* 忽略 */ }

            if (!playerSide) return;      // 她自己回合里数值本来就在变，交给原来的流程

            Logger.Log($"[CharBoss] {boss.Id.Entry} 力量/集中/球 变化（{last} → {print}）⇒ 重画面板（球被动随之更新）");
            if (boss.Plan.Count > 0) boss.RefreshPlannedIntents();
            BossHandUi.RefreshForce(boss);
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 力量/集中变化后重画面板失败: " + e.Message);
        }
    }
    /// <summary>她身上所有能力（id + 层数）拼成的指纹；顺序无关（按 id 排序）。</summary>
    private static string PowerPrintOf(MegaCrit.Sts2.Core.Entities.Creatures.Creature creature)
    {
        try
        {
            var powers = creature.Powers;
            if (powers == null || powers.Count == 0) return string.Empty;

            var parts = new List<string>(powers.Count);
            foreach (var power in powers)
            {
                if (power == null) continue;
                string entry;
                int amount;
                try { entry = power.Id.Entry; } catch { continue; }
                try { amount = power.Amount; } catch { amount = 0; }
                parts.Add(entry + "=" + amount);
            }
            parts.Sort(StringComparer.Ordinal);
            return string.Join("|", parts);
        }
        catch { return string.Empty; }
    }

    /// <summary>
    /// 灵魂绑定：抽牌数变了（玩家回合内抽牌 / 她自己又额外抽了牌）→ 死亡行军的伤害预览要跟着变。
    /// 用户 2026-10-23：「玩家回合内抽牌时要**随时**增加死亡行军的伤害预览」。
    /// 由 <c>CharBossMonster.IntentsDirty</c> 打标，这里消费一次就把标清掉
    /// （不会每帧重算 —— 重算会重建整排手牌栏）。
    /// </summary>
    private static void RefreshIntentsIfDrawsChanged(CharBossMonster boss)
    {
        try
        {
            if (boss == null || !boss.IntentsDirty) return;
            boss.IntentsDirty = false;

            if (boss.Plan.Count == 0) return;

            var pcs = boss.Shadow?.PlayerCombatState;
            if (pcs == null) return;

            // 玩家回合里刷新时用**规划期**语义（保留「还没到账的力量/铸造」那类预补）；
            // 她自己的回合里用**出牌期**语义（那些成长已经真到账了）。
            bool playerTurn;
            try { playerTurn = boss.CombatState?.CurrentSide == MegaCrit.Sts2.Core.Combat.CombatSide.Player; }
            catch { playerTurn = true; }

            if (pcs.OrbQueue.Capacity == 0)
            {
                BladesGrowth.RecomputeAll(boss, boss.Plan, planTime: playerTurn);
                boss.PublishVanillaIntents();
            }
            else
            {
                boss.RefreshPlannedIntents();   // 有球的 Boss：走它自己那套（本功能只对灵魂骨，留个兜底）
            }
            BossHandUi.RefreshForce(boss);
        }
        catch (Exception e)
        {
            boss.IntentsDirty = false;
            Logger.Log("[CharBoss] 抽牌变化后重算意图失败: " + e.Message);
        }
    }
}
