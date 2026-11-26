using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using MegaCrit.Sts2.Core.ValueProps;
using CharacterBosses.Monsters;

namespace CharacterBosses.Monsters;

/// <summary>
/// 二层储君 3 号「**仆从储**」召唤的三种仆从（用户 2026-11-05 设计）。
///
/// 用户原话：
/// > 打击仆从：生命值 36（进阶 8 及以上为 40）。意图：打击：造成 6（进阶 9 及以上为 9）点伤害（复读这一意图）
/// > 俯冲仆从：生命值 23（进阶 8 及以上为 26）意图：俯冲：造成 13（进阶 9 及以上为 16）点伤害，使自身死亡
/// > 捐躯仆从：生命值 30（进阶 8 以上为 33）意图：捐躯：给予储君 7（10 点格挡）
/// > 仆从在储君之前行动（所以本回合刚召唤的仆从不会在本回合行动），站在储君左边
/// > 与奥斯提不同，这些仆从是相对独立的：会自己行动，也不会替储君承受伤害
/// > 所有仆从带有爪牙，在储君死亡时死亡
///
/// 实现说明：
///   · 它们是**真正的敌方怪物**（`CreatureCmd.Add<T>(combatState)` 在战斗中加入，
///     见 <c>Patches/MinionSummonPatch</c>），不是玩家宠物 ⇒ 不会替储君承伤；
///   · 攻击目标是「锁定储君的那位玩家」（<see cref="Core.BossLockOn.TargetFor"/>），与 Boss 单体攻击同规则；
///   · **爪牙**：给它们挂原版 <c>MinionPower</c>，并把「召唤者 → 仆从」记在
///     <see cref="Registry"/> 里；储君死亡时由 <c>CharBossMonster.BeforeRemovedFromRoom</c> 统一清场
///     （其它 Boss 死亡不影响它们）。
/// </summary>
public abstract class BossMinionMonster : MonsterModel
{
    /// <summary>召唤者（储君）。召唤时写入。</summary>
    public CharBossMonster? Summoner { get; set; }

    /// <summary>「召唤者 → 它召唤的仆从」的登记表（用于储君死亡时连带清场）。</summary>
    internal static readonly Dictionary<CharBossMonster, List<BossMinionMonster>> Registry = new();

    /// <summary>登记一个仆从（召唤时调用）。</summary>
    internal static void Register(CharBossMonster summoner, BossMinionMonster minion)
    {
        try
        {
            if (!Registry.TryGetValue(summoner, out var list))
            {
                list = new List<BossMinionMonster>();
                Registry[summoner] = list;
            }
            list.Add(minion);
        }
        catch { /* 忽略 */ }
    }

    /// <summary>某个储君当前还活着的仆从数量。</summary>
    public static int CountAlive(CharBossMonster? summoner)
    {
        try
        {
            if (summoner == null) return 0;

            // ★★ v0.2.954（2026-11-25 用户截图：「仆从储没有仆从，但他的仆从打击还是显示为攻击而非召唤」）：
            //   **以战斗里真实存在的生物为准**，不再只看 `Registry`。
            //   原因：`Registry` 是一份我们自己维护的名单，仆从被打死 / 被带走 / 大战结算时只要有一条路径
            //   没清干净（或清理晚于意图重算），`CountAlive` 就会报「还有 1 只」⇒
            //   `BossScriptPlanner.IsMinionSummonHere` 认为已达上限 ⇒ 仆从牌被预测成「攻击/格挡」，
            //   而场上其实一只都没有（玩家看到的就是「没有仆从，但显示为攻击」）。
            //   `CombatState.Creatures` 是两端同步的权威列表，活没活一眼就能看出来。
            var combat = summoner.CombatState;
            if (combat != null)
            {
                int live = 0;
                try
                {
                    var all = combat.Creatures;
                    if (all != null)
                    {
                        for (int i = 0; i < all.Count; i++)
                        {
                            var c = all[i];
                            if (c == null) continue;
                            if (c.Monster is not BossMinionMonster) continue;
                            if (!c.IsAlive) continue;
                            live++;
                        }
                    }
                }
                catch { /* 读不到就退回 Registry */ }

                if (live > 0) return live;

                // 战斗里一只活的都没有 ⇒ 就是 0（顺手把过期名单清掉）
                try { if (Registry.TryGetValue(summoner, out var stale)) stale.RemoveAll(m => m == null || m.Creature == null || !m.Creature.IsAlive); }
                catch { /* 忽略 */ }
                return 0;
            }

            // 兜底：拿不到战斗状态时，仍然按 Registry 数（并剔除已死的）
            if (!Registry.TryGetValue(summoner, out var list)) return 0;
            list.RemoveAll(m => m?.Creature == null || !m.Creature.IsAlive);
            return list.Count;
        }
        catch { return 0; }
    }

    /// <summary>储君死亡时把它的仆从一起清掉（用户：只有储君死了才会连带死）。</summary>
    internal static async Task KillAllOf(CharBossMonster summoner)
    {
        try
        {
            if (!Registry.TryGetValue(summoner, out var list)) return;
            Registry.Remove(summoner);
            foreach (var minion in list)
            {
                try
                {
                    var creature = minion?.Creature;
                    if (creature == null || !creature.IsAlive) continue;
                    Logger.Log($"[CharBoss] 爪牙：储君死亡 ⇒ 仆从 {minion!.Id.Entry} 一并死亡");
                    await CreatureCmd.Damage(new MegaCrit.Sts2.Core.GameActions.Multiplayer.ThrowingPlayerChoiceContext(),
                        new[] { creature }, 9999m, ValueProp.Unpowered, summoner.Creature);
                }
                catch (Exception e)
                {
                    Logger.Log("[CharBoss] 爪牙清场失败: " + e.Message);
                }
            }
        }
        catch { /* 忽略 */ }
    }

    // ── 通用实现 ─────────────────────────────────────

    /// <summary>没有独立立绘（用户：先用三张仆从牌的卡面代替）。这里给一个安全占位，
    /// 卡面立绘由 <c>Patches/MinionCardArtPatch</c> 覆盖贴图。</summary>
    protected override string VisualsPath => SceneHelper.GetScenePath("creature_visuals/defect");

    /// <summary>低/高进阶的血量（进阶 8 及以上用高值）。</summary>
    protected abstract int HpLow { get; }
    protected abstract int HpHigh { get; }

    /// <summary>
    /// ★★ v0.2.931（用户 2026-11-24）：「增强仆从储，它所有仆从的最大生命值增加 5（全难度）」
    /// + 「新增仆从储的恐惧模式增强：所有仆从的最大生命值增加 5（加上前面的增强，
    ///    现在恐惧模式下仆从的血量比原来多 10）」。
    /// ⇒ 普通模式 = 原值 + <see cref="HpBuff"/>；恐惧模式 = 原值 + <see cref="HpBuff"/> + <see cref="HpBuffDread"/>。
    /// </summary>
    private const int HpBuff = 5;
    private const int HpBuffDread = 5;

    public override int MinInitialHp => HpByAscension();
    public override int MaxInitialHp => HpByAscension();

    private int HpByAscension()
    {
        int asc = 0;
        try { asc = Core.BossLineups.Ascension(); } catch { asc = 0; }
        int hp = asc >= 8 ? HpHigh : HpLow;
        hp += HpBuff;
        try { if (Core.DreadMode.IsActive) hp += HpBuffDread; } catch { /* 判不出恐惧就当普通模式 */ }
        return hp;
    }

    // ★ 多人不涨血：这一层**早就修过**（v0.2.794，见 `Patches/BossMultiplayerHpScalePatch`
    //   的 `BossNoMultiplayerHpScalePatch`：`playerCount >= 2` 时对 `BossMinionMonster`
    //   直接跳过 `Creature.ScaleMonsterHpForMultiplayer`）。这里不再重复声明，
    //   只把「设计值 = 单人值」写在这里提醒后来的人。

    public override LocString Title => new LocString("monsters", Id.Entry + ".name");

    /// <summary>
    /// 卡面是否**不翻转**（默认翻转成朝玩家）。用户 2026-11-05：捐躯仆从的卡面方向反了，
    /// 它单独再翻回来；其它两只保持不动。
    /// </summary>
    protected virtual bool FaceRight => false;

    /// <summary>立绘用的**卡牌**（用户 2026-11-05：「先直接用这三张牌当对应仆从的立绘」）。</summary>
    protected abstract Type SourceCardType { get; }

    /// <summary>
    /// **这只仆从对应的那张卡**（对外暴露）。
    /// 用途（用户 2026-11-24）：「让仆从储的 3 个仆从的藏品分别是它们对应的卡牌」
    /// —— 收藏家的「思维抽取」打到仆从身上时，就抽走这张牌（见 `DownfallCollectibleCompat`）。
    /// </summary>
    internal Type MinionCardType => SourceCardType;

    /// <summary>
    /// 进房间后把**卡面图**盖到仆从身上（原版没有独立立绘）。
    /// 做法：取 <c>CardModel.PortraitPath</c> 的贴图，在它的立绘节点上叠一个 `Sprite2D`
    /// （占位的怪物立绘保持不动，只在最上层盖一张卡片图，缩放到仆从尺寸）。
    /// </summary>
    public override async Task AfterAddedToRoom()
    {
        try { AttachCardArt(); }
        catch (Exception e) { Logger.Log("[CharBoss] 仆从卡面立绘失败（忽略）: " + e.Message); }
        await Task.CompletedTask;
    }

    /// <summary>
    /// 仆从死掉时：把它从登记表里摘掉，并**立刻重算储君的意图**。
    ///
    /// ★ 2026-11-05（用户实测：「我在第三回合干掉了 1 个仆从，但是意图没更新成 2 个都召唤」）：
    ///   仆从数量变了会改变「仆从牌这一手是召唤还是原版效果」，而意图只在力量/抽牌/集中变化时才重算
    ///   ⇒ 死一只仆从不会触发重算，屏幕上的意图就停在上一次规划的结果。这里补一次主动刷新。
    /// </summary>
    public override void BeforeRemovedFromRoom()
    {
        try
        {
            var boss = Summoner;
            if (boss != null)
            {
                if (Registry.TryGetValue(boss, out var list)) list.Remove(this);
                Logger.Log($"[CharBoss] 仆从 {Id.Entry} 离场 ⇒ 储君剩余仆从 {CountAlive(boss)} 只，重算意图");
                boss.RefreshPlannedIntents();
                try { UI.BossHandUi.RefreshForce(boss); } catch { /* 忽略 */ }
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 仆从离场处理失败: " + e.Message);
        }
        base.BeforeRemovedFromRoom();
    }

    /// <summary>卡面立绘节点（用来做「左移一下」的攻击动作）。</summary>
    private Sprite2D? _art;

    /// <summary>
    /// **假攻击动画**（用户 2026-11-05 提议）：卡面向左平移一下再回来。
    /// 刻意**不等任何动画信号** —— 仆从是原版没有的单位，它的「攻击动画」在我们这里根本不存在，
    /// 等信号就会永远卡住（实测：`打击仆从出手` 之后再无下文）。这里只跑 tween + 固定 260ms。
    /// </summary>
    protected async Task PlayLungeAsync()
    {
        try
        {
            var art = _art;
            if (art == null) return;
            float baseX = art.Position.X;
            var tween = art.CreateTween();
            tween.TweenProperty(art, "position:x", baseX - 70f, 0.12);
            tween.TweenProperty(art, "position:x", baseX, 0.14);
            await Task.Delay(260);      // 不等信号，避免卡死
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 仆从攻击动作失败（忽略）: " + e.Message);
        }
    }

    /// <summary>
    /// 每帧兜底（4Hz）：①把占位立绘保持**透明**（死亡动画会重置 Modulate ⇒ 又露出那只故障机器人）；
    /// ②死后把卡面隐藏 ⇒ 死亡就是「直接消失」（用户 2026-11-05）。
    /// </summary>
    internal void EnforceArtVisuals()
    {
        try
        {
            var creature = Creature;
            var room = MegaCrit.Sts2.Core.Nodes.Rooms.NCombatRoom.Instance;
            var node = creature != null ? room?.GetCreatureNode(creature) : null;
            var visuals = node?.Visuals;
            if (visuals != null) visuals.Modulate = new Color(1f, 1f, 1f, 0f);
            if (_art != null) _art.Visible = creature != null && creature.IsAlive;
        }
        catch { /* 忽略 */ }
    }

    private void AttachCardArt()
    {
        var room = MegaCrit.Sts2.Core.Nodes.Rooms.NCombatRoom.Instance;
        var node = room?.GetCreatureNode(Creature);
        var visuals = node?.Visuals;
        if (visuals == null) return;

        string? path = null;
        try
        {
            var id = ModelDb.GetId(SourceCardType);
            var card = ModelDb.GetById<CardModel>(id);
            path = card?.PortraitPath;
        }
        catch (Exception e) { Logger.Log("[CharBoss] 取卡图路径失败: " + e.Message); }
        if (string.IsNullOrEmpty(path)) return;

        var tex = GD.Load<Texture2D>(path!);
        if (tex == null)
        {
            Logger.Log($"[CharBoss] 仆从卡面立绘：加载失败 {path}（继续用占位立绘）");
            return;
        }

        var sprite = new Sprite2D
        {
            Texture = tex,
            Centered = true,
            // 往上挪（用户 2026-11-05：贴图把仆从自己的血条挡住了）
            Position = new Vector2(0f, -165f),
        };
        // 卡片图很宽 ⇒ 按宽度缩到仆从大小；**X 取负** ⇒ 左右翻转，让它朝着玩家（玩家在左边）
        float targetW = 210f;
        float scale = tex.GetWidth() > 0 ? targetW / tex.GetWidth() : 1f;
        sprite.Scale = new Vector2(FaceRight ? scale : -scale, scale);

        // ① 先把卡面挂到**生物节点**上（不是立绘节点里），
        // ② 再把占位的怪物立绘（`creature_visuals/defect`）整个隐藏 ——
        //    用户 2026-11-05 截图：卡面后面还露着一只故障机器人。
        node!.AddChild(sprite);
        _art = sprite;
        // ⚠ 用「透明」而不是 Visible = false：隐藏节点会让攻击动画永远等不到结束信号，
        //   表现就是「仆从一攻击就卡住」（用户 2026-11-05 实测）。
        try { visuals.Modulate = new Color(1f, 1f, 1f, 0f); } catch { /* 忽略 */ }
        Logger.Log($"[CharBoss] 仆从卡面立绘：{Id.Entry} ← {path}（scale {scale:0.00}）");
    }

    /// <summary>攻击目标 = 锁定储君的那位玩家（找不到就退回「可攻击敌人」里的第一个真人玩家）。</summary>
    protected Creature? TargetPlayer()
    {
        try
        {
            var target = Summoner != null ? Core.BossLockOn.TargetFor(Summoner)?.Creature : null;
            if (target != null && target.IsAlive) return target;
            var combat = CombatState;
            if (combat == null) return null;
            foreach (var c in combat.HittableEnemies)
            {
                if (c != null && c.IsAlive && c.Player != null) return c;
            }
        }
        catch { /* 忽略 */ }
        return null;
    }
}

/// <summary>打击仆从：每回合复读「打击」（6 / 进阶 9 起 9 点）。</summary>
public sealed class MinionStrikeMonster : BossMinionMonster
{
    protected override int HpLow => 22;

    protected override Type SourceCardType => typeof(MegaCrit.Sts2.Core.Models.Cards.MinionStrike);
    protected override int HpHigh => 25;

    private int Damage
    {
        get
        {
            int asc = 0;
            try { asc = Core.BossLineups.Ascension(); } catch { asc = 0; }
            return asc >= 9 ? 9 : 6;
        }
    }

    protected override MonsterMoveStateMachine GenerateMoveStateMachine()
    {
        var strike = new MoveState("MINION_STRIKE_MOVE", StrikeMove, new SingleAttackIntent(Damage));
        strike.FollowUpState = strike;     // 复读这一意图
        var states = new List<MonsterState> { strike };
        return new MonsterMoveStateMachine(states, strike);
    }

    private async Task StrikeMove(IReadOnlyList<Creature> targets)
    {
        var target = TargetPlayer();
        Logger.Log($"[CharBoss] 打击仆从出手：目标 {(target == null ? "null" : target.Name)}，{Damage} 点");
        if (target == null) return;
        await PlayLungeAsync();
        // ⚠ 用 `CreatureCmd.Damage` 直接结算，不再用 `DamageCmd.Attack(...).Execute(...)`：
        //   后者会去等「攻击者动画」播完，而仆从没有原版动画 ⇒ await 永远不返回 ⇒ 回合卡死。
        var me = Creature;
        await CreatureCmd.Damage(new MegaCrit.Sts2.Core.GameActions.Multiplayer.ThrowingPlayerChoiceContext(),
            new[] { target }, Damage, ValueProp.Move, me);
        Logger.Log("[CharBoss] 打击仆从出手完成");
    }
}

/// <summary>俯冲仆从：每回合复读一击（13 / 进阶 9 起 16 点）。</summary>
public sealed class MinionDiveBombMonster : BossMinionMonster
{
    protected override int HpLow => 12;

    protected override Type SourceCardType => typeof(MegaCrit.Sts2.Core.Models.Cards.MinionDiveBomb);
    protected override int HpHigh => 14;

    private int Damage
    {
        get
        {
            int asc = 0;
            try { asc = Core.BossLineups.Ascension(); } catch { asc = 0; }
            return asc >= 9 ? 16 : 13;
        }
    }

    protected override MonsterMoveStateMachine GenerateMoveStateMachine()
    {
        var dive = new MoveState("MINION_DIVE_MOVE", DiveMove, new SingleAttackIntent(Damage));
        dive.FollowUpState = dive;
        var states = new List<MonsterState> { dive };
        return new MonsterMoveStateMachine(states, dive);
    }

    private async Task DiveMove(IReadOnlyList<Creature> targets)
    {
        try
        {
            var target = TargetPlayer();
            await PlayLungeAsync();
            var me0 = Creature;
            if (target != null && me0 != null)
            {
                await CreatureCmd.Damage(new MegaCrit.Sts2.Core.GameActions.Multiplayer.ThrowingPlayerChoiceContext(),
                    new[] { target }, Damage, ValueProp.Move, me0);
            }
            // ★ 2026-11-05（用户改口）：「让它也是复读意图（打 13/16），而不是打一次就自爆」
            //   ⇒ 不再自灭；`FollowUpState` 会一直复读这一手。
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 俯冲仆从出手失败: " + e.Message);
        }
    }
}

/// <summary>捐躯仆从：给储君 7（进阶 9 起 10）点格挡。</summary>
public sealed class MinionSacrificeMonster : BossMinionMonster
{
    protected override int HpLow => 15;

    protected override Type SourceCardType => typeof(MegaCrit.Sts2.Core.Models.Cards.MinionSacrifice);
    // 用户 2026-11-05：捐躯仆从的卡面朝向反了 ⇒ 单独再翻一次（其它两只不翻）
    protected override bool FaceRight => true;
    protected override int HpHigh => 17;

    private int BlockGain
    {
        get
        {
            int asc = 0;
            try { asc = Core.BossLineups.Ascension(); } catch { asc = 0; }
            return asc >= 9 ? 10 : 7;
        }
    }

    protected override MonsterMoveStateMachine GenerateMoveStateMachine()
    {
        var donate = new MoveState("MINION_SACRIFICE_MOVE", DonateMove, new DefendIntent());
        donate.FollowUpState = donate;
        var states = new List<MonsterState> { donate };
        return new MonsterMoveStateMachine(states, donate);
    }

    private async Task DonateMove(IReadOnlyList<Creature> targets)
    {
        try
        {
            var regent = Summoner?.Creature;
            if (regent == null || !regent.IsAlive) return;
            Logger.Log($"[CharBoss] 捐躯仆从：给储君 {BlockGain} 点格挡");
            await CreatureCmd.GainBlock(regent, BlockGain, ValueProp.Move, null);
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 捐躯仆从出手失败: " + e.Message);
        }
    }
}
