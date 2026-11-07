using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using CharacterBosses.Monsters;

namespace CharacterBosses.UI;

/// <summary>
/// Boss 手牌栏的门面。
/// 挂在自建的 <see cref="CanvasLayer"/> 上，而不是战斗房间节点上 ——
/// 战斗房间带相机变换，Control 挂上去会跟着镜头走甚至跑到屏幕外；
/// CanvasLayer 用的是屏幕坐标，能稳定盖在 UI 上层。
///
/// ## 多人（≥2 只 Boss 同场）= **每只 Boss 各一套面板**
/// 从「单例面板」改成「按 Boss 各一份」：<see cref="Panels"/> 以 Boss 为键，
/// 每只 Boss 的手牌栏/遗物/能量/辉星/意图全部独立（<see cref="BossHandPanel"/> 本来就自带
/// <c>_boss</c> 字段、定位走 <see cref="BossHandAnchor.Resolve"/> 各自量自己的立绘，
/// <see cref="BossStatusRow"/> 也早已是「按行各记一份能量球」，所以这里只是把**门面**变成多实例）。
///
/// 单人（1 只）时行为与改造前**完全一致**：只有一份面板，<see cref="Panel"/> / <see cref="CurrentBoss"/>
/// 就是它，所有旧调用点（<c>BossFrameTick</c> 等）不用改。
/// </summary>
public static class BossHandUi
{
    private const int HudLayer = 80;

    private static CanvasLayer? _layer;

    /// <summary>按 Boss 各一份手牌栏（多人「双 Boss 两套面板」的关键；单人只有一份）。</summary>
    private static readonly Dictionary<CharBossMonster, BossHandPanel> Panels = new();

    /// <summary>
    /// 主面板 = 「锁定本地玩家的那只」优先，其次第一只。
    /// 只给那些只需要一个参考点的地方用（例如 <see cref="BossExtraStatusRow"/> 的定位基准）。
    /// </summary>
    public static CharBossMonster? CurrentBoss { get; private set; }

    /// <summary>主面板（单人＝唯一那只）。</summary>
    public static BossHandPanel? Panel => PanelFor(CurrentBoss);

    /// <summary>某只 Boss 自己的那套面板（没有就是 null）。</summary>
    public static BossHandPanel? PanelFor(CharBossMonster? boss)
    {
        if (boss == null) return null;
        if (!Panels.TryGetValue(boss, out var panel) || panel == null) return null;
        return GodotObject.IsInstanceValid(panel) ? panel : null;
    }

    /// <summary>当前挂着面板的所有 Boss（多人时 ≥2；给每帧驱动逐套跑 <c>RunFrame</c> 用）。</summary>
    public static List<CharBossMonster> PanelBosses()
    {
        var list = new List<CharBossMonster>();
        foreach (var kv in Panels)
        {
            if (kv.Key == null || kv.Value == null || !GodotObject.IsInstanceValid(kv.Value)) continue;
            list.Add(kv.Key);
        }
        return list;
    }

    /// <summary>本地玩家（多人时用来判断「这只 Boss 是不是在盯我」）。</summary>
    private static MegaCrit.Sts2.Core.Entities.Players.Player? LocalPlayerOrNull(CharBossMonster? boss)
    {
        try
        {
            // 原版就是 `LocalContext.GetMe(owner.CombatState)`（见 BossDamagePredictor 的注释）
            var combat = boss?.CombatState;
            if (combat == null) return null;
            var me = MegaCrit.Sts2.Core.Context.LocalContext.GetMe(combat);
            // ⚠ 影子 Player 也在 CombatState.Players 里，万一 GetMe 把它当成「本地玩家」，
            //    后面的锁定判定会一律为假（面板就画不出来了）⇒ 先把它排除掉。
            if (me != null && me.Creature?.Monster == null) return me;

            var real = Core.BossLockOn.RealPlayers(combat);
            if (real.Count == 1) return real[0];
            return me;   // 兜底：宁可回到原版结果
        }
        catch { return null; }
    }

    public static void Refresh(CharBossMonster boss)
    {
        try
        {
            if (boss == null) return;

            // 多人「跟踪锁定」：本地玩家**优先**看待锁定自己那只的面板（它排在主位），
            // 但两只 Boss 的**面板都要画** —— 用户要求（见 multiplayer-lockon.md §0.2 第 1 条）。
            // ⚠ 人数必须用**真人玩家数**：影子 Player 也在 CombatState.Players 里，按它数永远是 2。
            bool multi = Core.BossLockOn.RealPlayerCount(boss?.CombatState) >= 2;
            NotePrimary(boss, multi);

            var hand = boss.Shadow?.PlayerCombatState?.Hand.Cards;
            if (hand == null)
            {
                // 拿不到手牌数据（影子玩家还没建好 / 不在战斗里）→ 收掉这一只的面板
                //（不能像以前那样 Clear 掉全部 —— 另一只的手牌栏还得留着）
                ReleasePanel(boss);
                BossSpecialUi.Ensure(boss);
                return;
            }

            // ⚠ **手牌为空也要保留面板**（用户 2026-10-29）：
            //   「有的 boss 的界面在他打完本回合要打的牌就消失了（未打的牌、能量、遗物等），
            //     有的则保留。能不能统一成保留？」
            //   以前这里在 `hand.Count == 0` 时直接 `ReleasePanel`，于是**把手牌全打完的那几只**
            //   整块界面（连同能量/遗物/药水/辉星）一起消失；而手里还留着打不出的牌的 Boss 就保留 ——
            //   表现就不一致。现在一律保留：卡面区自然变空，状态条继续显示，
            //   直到她阵亡（`ReleasePanel`）或战斗结束（`EndFight`）才收。

            var tree = Engine.GetMainLoop() as SceneTree;
            if (tree == null)
            {
                Logger.Log("[CharBoss] hand ui: no scene tree yet");
                return;
            }

            var panel = EnsurePanel(tree, boss);
            panel?.Rebuild(boss, hand);

            // 球的激发数值：手里（计划里）有激发牌时，让原版把「将要被激发」的球显示激发值并变蓝
            BossOrbDisplay.HighlightEvokes(boss);

            // 特殊效果的原版说明框（常驻在立绘右侧）
            BossSpecialUi.Ensure(boss);
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] hand ui refresh failed: " + e);
        }
    }

    /// <summary>主面板归属：单人/首次 = 这只；多人时**锁定本地玩家**的那只优先坐主位。</summary>
    private static void NotePrimary(CharBossMonster boss, bool multi)
    {
        try
        {
            bool changed = false;
            if (CurrentBoss == null)
            {
                CurrentBoss = boss;
                changed = true;
            }
            else if (!ReferenceEquals(CurrentBoss, boss))
            {
                if (!multi) { CurrentBoss = boss; changed = true; }
                else
                {
                    bool mine = Core.BossLockOn.IsLocked(boss, LocalPlayerOrNull(boss));
                    bool primaryMine = Core.BossLockOn.IsLocked(CurrentBoss, LocalPlayerOrNull(CurrentBoss));
                    if (mine && !primaryMine) { CurrentBoss = boss; changed = true; }
                }
            }
            if (changed)
            {
                Logger.Log($"[CharBoss] 当前 Boss 已登记: {boss.Id}（每帧驱动开始为它工作）");
            }
        }
        catch { /* 忽略 */ }
    }

    /// <summary>
    /// 强制整排重建后再刷新。
    ///
    /// 用途：意图数字被**重算**过（她回合开始拿到力量后要把"提前补的那点"撤掉）时，
    /// 普通 <see cref="Refresh"/> 只做增量同步、不会重画已有格子的意图 —— 会留下规划期的旧数字。
    /// </summary>
    public static void RefreshForce(CharBossMonster boss)
    {
        try
        {
            var panel = PanelFor(boss);
            if (panel != null && GodotObject.IsInstanceValid(panel)) panel.Invalidate();
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] hand ui 强制重建失败: " + e.Message);
        }
        Refresh(boss);
    }

    /// <summary>为某只 Boss 建（或取回）它自己的那套面板。</summary>
    private static BossHandPanel? EnsurePanel(SceneTree tree, CharBossMonster boss)
    {
        var existing = PanelFor(boss);
        if (existing != null) return existing;

        // 优先挂到**战斗房自己的 UI 层**（NCombatRoom.Ui，全屏 Control）：
        //   · 坐标系与屏幕一致（0,0 1920x1080）；
        //   · 和游戏 UI 在同一个节点树里，所以遗物悬浮说明/弹窗这些全局 UI 会画在我们**上面**
        //     （之前挂在自己的 CanvasLayer 上，层级比它们高，导致遗物说明被卡牌盖住）；
        //   · 战斗房销毁时跟着消失，不会残留在别的界面上。
        var ui = NCombatRoom.Instance?.Ui;
        Node parent = ui?.GetNodeOrNull<Control>("%PlayContainer") ?? (Node?)ui ?? (Node?)tree.Root;
        if (ui == null)
        {
            if (_layer == null || !GodotObject.IsInstanceValid(_layer))
            {
                _layer = new CanvasLayer { Name = "CharacterBossesHud", Layer = HudLayer };
                tree.Root.AddChild(_layer);
            }
            parent = _layer;
        }

        var panel = new BossHandPanel
        {
            // 两只 Boss 的面板不能重名（同一个父节点下 Godot 会自动改名，日志里就看不出是谁的了）
            Name = "BossHandPanel_" + SafeKey(boss),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        parent.AddChild(panel);
        Panels[boss] = panel;
        Logger.Log($"[CharBoss] hand ui: 挂到 {parent.Name}（战斗房 UI 层）" +
                   $"boss={SafeKey(boss)}（本场共 {Panels.Count} 套面板）");
        return panel;
    }

    private static string SafeKey(CharBossMonster boss)
    {
        try { return boss.Id.Entry + "_" + boss.ActIndex; } catch { return "boss"; }
    }

    /// <summary>
    /// 只收掉**某一只** Boss 的面板。收完如果一套都不剩，就做一次全局收尾
    /// （CanvasLayer / 锚点 / 说明框 / 球高亮）。
    /// </summary>
    public static void ReleasePanel(CharBossMonster boss)
    {
        try
        {
            if (boss != null && Panels.TryGetValue(boss, out var panel))
            {
                if (panel != null && GodotObject.IsInstanceValid(panel))
                {
                    panel.Reset();
                    panel.QueueFree();
                }
                Panels.Remove(boss);
            }

            if (Panels.Count == 0) GlobalCleanup();

            // ⚠ 这里**故意不把 CurrentBoss 清成 null**：面板没了（手牌为空）不代表这场战斗不归它管，
            // 而「每帧驱动」（君王之剑朝向 / 随从朝向站位）就是靠 CurrentBoss 才干的。
            // 早先 Refresh 在手牌为空时把它一起清掉，实测表现正是「刚进战斗士兵朝右，储君回合一开始才对」。
            // 只有在还有别的面板时才改主位。真正释放发生在 EndFight。
            if (CurrentBoss != null && !Panels.ContainsKey(CurrentBoss) && Panels.Count > 0)
            {
                CurrentBoss = Panels.Keys.First();
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] hand ui 收面板失败: " + e.Message);
        }
    }

    /// <summary>
    /// 收掉**所有** Boss 的面板节点，**不动 <see cref="CurrentBoss"/>**。
    ///
    /// 为什么必须分开（踩过的坑）：<see cref="Refresh"/> 在「手牌为空」时也会收面板，
    /// 如果把「当前 Boss」一起清掉，那么每帧驱动（君王之剑朝向、随从朝向站位）就会跟着停摆 ——
    /// 实测表现正是「刚进战斗士兵朝右，储君回合一开始才对」。
    /// 所以界面生命周期与「这场战斗归谁」解耦，后者只由 <see cref="EndFight"/> 结束。
    /// </summary>
    public static void Clear()
    {
        try
        {
            foreach (var kv in Panels)
            {
                var panel = kv.Value;
                if (panel != null && GodotObject.IsInstanceValid(panel))
                {
                    panel.Reset();
                    panel.QueueFree();
                }
            }
            Panels.Clear();
            GlobalCleanup();
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] hand ui clear failed: " + e.Message);
        }
    }

    /// <summary>面板全收完之后的一次性收尾（与「归谁」无关的那些全局东西）。</summary>
    private static void GlobalCleanup()
    {
        try
        {
            if (_layer != null && GodotObject.IsInstanceValid(_layer)) _layer.QueueFree();
            _layer = null;

            // 下一场战斗要重新量一次锚点（Boss 立绘/位置可能不一样）
            BossHandAnchor.ResetLock();

            // 说明框挂在全局的悬浮说明层上，界面收掉时必须显式移除
            BossSpecialUi.Hide();
            BossOrbDisplay.ClearHighlight(null);
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] hand ui 全局收尾失败: " + e.Message);
        }
    }

    /// <summary>战斗真正结束：收界面 + 放掉当前 Boss + 让「每场只报一次」的诊断日志重新武装。</summary>
    public static void EndFight(string reason)
    {
        int count = Panels.Count;
        Clear();
        if (CurrentBoss != null)
        {
            Logger.Log($"[CharBoss] 战斗结束（{reason}）：释放 {count} 套 Boss 面板（主位 {CurrentBoss.Id}）");
        }
        CurrentBoss = null;
        BossSovereignBlade.ResetDiagnostics();
        Core.BossShadowPets.ResetDiagnostics();
        Core.BossOsty.ResetForNewFight();
        Core.BossOsty.ResetDeathHooks();
        BossStatusRow.ResetDiagnostics();
        try { Patches.BossFrameTick.ResetForNewFight(); } catch { /* 忽略 */ }
        // 「皮肤补跑过谁」的记账每场都是新节点 ⇒ 必须清掉
        try { Patches.SkinReadyPatchReplay.ResetForNewFight(); } catch { /* 忽略 */ }
        // 一层「轰击储」的环绕轨道计数（无色牌回合数 / 唤醒回合）每场从 0 起
        try { Powers.BossOrbitalBombardPower.ResetForNewFight(); } catch { /* 忽略 */ }
        // 多人「跟踪锁定」：一场结束就把 Boss↔玩家 的锁定关系清掉（下一场的生物实例是新的）
        Core.BossLockOn.Reset();
    }
}
