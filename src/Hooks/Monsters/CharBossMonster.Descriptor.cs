using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using CharacterBosses.Core;

namespace CharacterBosses.Monsters;

/// <summary>
/// <see cref="CharBossMonster"/> 的「描述性」成员：幕数、血量、名字、立绘、动画器。
/// 拆出来纯粹是为了单文件 200 行以内（主文件放行为逻辑）。
/// </summary>
public abstract partial class CharBossMonster
{
    public const string ThinkStateId = "CHARBOSS_THINK";
    public const string PlayStateId = "CHARBOSS_PLAY";

    /// <summary>这个怪物类对应的**原始**设计（5 个基类各自返回 <c>BossDefs.Xxx</c>）。</summary>
    public abstract BossDefinition DefSource { get; }

    private BossDefinition? _defCache;

    /// <summary>
    /// 实际生效的设计。基类 = <see cref="DefSource"/> 本身（行为与以前完全一致）；
    /// 「暴露给外部工具」的档位类会通过 <see cref="PinnedVariantKey"/> 钉死某一套设计，
    /// 这里给它一份只改 pin 的副本并缓存（定义是全局单例，不能就地改）。
    /// </summary>
    public BossDefinition Def
        => _defCache ??= PinnedVariantKey is { Length: > 0 } key ? DefSource.WithVariantPin(key) : DefSource;

    /// <summary>
    /// 本类钉死的**设计 Key**（`BossKeys.All` 里的那 29 个之一）；null = 按遭遇/种子正常判定。
    /// 见 <see cref="CharacterBosses.Monsters.TierBossMonsters"/>。
    /// </summary>
    public virtual string? PinnedVariantKey => null;

    /// <summary>
    /// 本类钉死的**幕数**（0 起）；null = 按遭遇 / 真实幕数。
    /// 斗蛐蛐里遭遇恒为 `CricketEncounter`（不实现 <see cref="IBossTierPin"/>），
    /// 所以暴露类必须自己带幕数，否则永远打第一幕的配置。
    /// </summary>
    public virtual int? TierAct => null;

    /// <summary>
    /// 当前是第几幕（0 = 第一幕，1 = 第二幕，2 = 第三幕）—— 决定用哪一档 HP / 能量 / 手牌数 / 卡组。
    /// 口径见 <see cref="BossActIndex"/>：传的是**委托**，因为 <c>CombatState</c> 在生物创建期间会抛异常。
    ///
    /// 三档优先级：
    /// ① 调试开关强制指定的档位（<c>charbosses_debug.txt</c> 写 <c>silent@3</c>）—— 开发期最高，压一切；
    /// ② 遭遇自己钉死的档位（<see cref="IBossTierPin"/>）—— 控制台「一只 Boss 一个 id」用，
    ///    例：<c>fight NECROBINDER_SOUL_BOSS_ENCOUNTER</c> 永远二层灵魂骨；
    /// ③ 真实幕数。没有 ①② 时就是原来的行为，一个字都没变。
    /// </summary>
    public int ActIndex
    {
        get
        {
            if (DebugMode.ForcedActIndex >= 0) return DebugMode.ForcedActIndex;
            // 外部暴露的档位类自带幕数（斗蛐蛐里问不到遭遇，只能靠这个）。
            if (TierAct is int ownTier) return Math.Clamp(ownTier, 0, 2);
            var pinned = PinnedTier();
            if (pinned >= 0) return pinned;
            return BossActIndex.Resolve(() => CombatState);
        }
    }

    /// <summary>遭遇钉死的档位缓存（-2 = 还没问到）。</summary>
    private int _tierPin = UnresolvedPin;

    private const int UnresolvedPin = -2;

    /// <summary>
    /// 问当前遭遇有没有把档位钉死（<see cref="IBossTierPin"/>）；没有就返回 -1。
    /// **问到 -1 不缓存**：生物还在构造期间 <c>CombatState</c> 会抛
    /// 「Creature was accessed before it was set」，那时问不出结果，等进了房间再问一次。
    /// </summary>
    private int PinnedTier()
    {
        if (_tierPin != UnresolvedPin) return _tierPin;
        try
        {
            var encounter = CombatState?.Encounter;
            if (encounter is IBossTierPin pin && pin.TierAct is int tier)
            {
                _tierPin = Math.Clamp(tier, 0, 2);
                Logger.Log($"[CharBoss] {Def.Key} 遭遇钉死档位={_tierPin + 1}（{encounter.Id.Entry}）");
            }
        }
        catch
        {
            // 生物还没构造完，下次再问
        }
        return _tierPin == UnresolvedPin ? -1 : _tierPin;
    }

    /// <summary>
    /// 涅奥的祝福带来的额外最大生命。
    ///
    /// 口径（用户 2026-10-31 定稿）：**拾起时 +120，之后每进入新的一幕再 +180** ⇒
    ///   第 1 幕 = +120、第 2 幕 = +300、第 3 幕 = +480。
    ///
    /// ★ v0.2.297（用户 2026-11-02：「你是不是让 boss 血量直接基础+120×层数了，怎么 2、3 层 boss 的血也变少了」）
    ///   —— **确实是我写错了**。这里原来是 `NeowBlessing.MaxHpBonus * (ActIndex + 1)`，
    ///   而 v0.2.287 把 `MaxHpBonus` 改成了「拾起值」的别名（120）⇒ 公式静默变成 `120 × (幕+1)`：
    ///     第 1 幕 120（对的）、第 2 幕 **240**（应为 300，少 60）、第 3 幕 **360**（应为 480，少 120）。
    ///   现在改成**两项分别取常量**：拾取值 + 每幕值 × 幕序号（不要再用那个别名，
    ///   它就是为了「旧代码别编译不过」才留的，语义已经变了）。
    /// </summary>
    internal int BlessingHpBonus
        => Def.CarriesNeowBlessing
            ? Relics.NeowBlessing.PickupMaxHpBonus
              + Relics.NeowBlessing.PerActMaxHpBonus * Math.Max(0, ActIndex)
            : 0;

    /// <summary>
    /// 变体自带的额外最大生命（三层「事件机」带着遗物「大蘑菇」⇒ +20）。
    /// 影子玩家收不到「拾起时」钩子，所以只能算进定义里（见 <c>BossVariant.HpBonus</c>）。
    /// </summary>
    internal int VariantHpBonus => Def.VariantHpBonusForAct(ActIndex);

    public override int MinInitialHp => Def.HpForAct(ActIndex) + BlessingHpBonus + VariantHpBonus;

    public override int MaxInitialHp => Def.HpForAct(ActIndex) + BlessingHpBonus + VariantHpBonus;

    public override LocString Title => new LocString("monsters", Id.Entry + ".name");

    /// <summary>
    /// 立绘场景。**优先问角色模型自己的 <c>VisualsPath</c>**，而不是拼死路径。
    ///
    /// 为什么（用户 2026-10-24：兼容「绯刃战姬」这类铁甲战士**皮肤 mod**）：
    /// 皮肤 mod 的常见做法就是把 <c>CharacterModel.VisualsPath</c> 指向自己的场景
    ///（或者干脆用 pck 覆盖同路径的资源）。我们以前写死
    /// <c>res://scenes/creature_visuals/ironclad.tscn</c> ⇒ 皮肤换了角色却不换 Boss。
    /// 这里改成委托给角色模型：原版角色返回的就是同一个路径（行为不变），
    /// 任何改了 <c>VisualsPath</c> 的皮肤 mod 都会自动被我们的 Boss 继承。
    /// </summary>
    protected override string VisualsPath
    {
        get
        {
            try
            {
                if (_visualsPathCache == null)
                {
                    var character = ModelDb.GetById<CharacterModel>(ModelDb.GetId(Def.CharacterType));
                    // ⚠ `CharacterModel.VisualsPath` 是 protected，编译期读不到 ⇒ 走反射。
                    //   反射调用的是**运行时真正的 getter**，所以皮肤 mod 用 Harmony 改过它也能读到。
                    var fromCharacter = CharacterVisualsPath(character);
                    if (!string.IsNullOrEmpty(fromCharacter)
                        && Godot.ResourceLoader.Exists(fromCharacter))
                    {
                        _visualsPathCache = fromCharacter;
                        Logger.Log($"[CharBoss] {Def.Key} 立绘用角色模型的 VisualsPath：{fromCharacter}" +
                                   $"{(fromCharacter.Contains(Def.VisualScene) ? string.Empty : "（⚠ 与缺省路径不同，可能来自皮肤 mod）")}");
                    }
                    else
                    {
                        _visualsPathCache = "res://scenes/creature_visuals/" + Def.VisualScene + ".tscn";
                        Logger.Log($"[CharBoss] {Def.Key} 角色模型的 VisualsPath 不可用（{fromCharacter ?? "null"}）" +
                                   $"⇒ 退回缺省 {_visualsPathCache}");
                    }
                }
            }
            catch (System.Exception e)
            {
                Logger.Log("[CharBoss] 取角色立绘路径失败，退回缺省: " + e.Message);
                if (_visualsPathCache == null)
                    _visualsPathCache = "res://scenes/creature_visuals/" + Def.VisualScene + ".tscn";
            }
            return _visualsPathCache!;
        }
    }

    private string? _visualsPathCache;

    /// <summary>反射读 `CharacterModel.VisualsPath`（protected；皮肤 mod 改过 getter 也能读到）。</summary>
    private static System.Reflection.PropertyInfo? _characterVisualsPathProp;
    private static bool _characterVisualsPathSearched;

    private static string? CharacterVisualsPath(CharacterModel? character)
    {
        if (character == null) return null;
        try
        {
            if (!_characterVisualsPathSearched)
            {
                _characterVisualsPathSearched = true;
                _characterVisualsPathProp = typeof(CharacterModel).GetProperty("VisualsPath",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic);
            }
            return _characterVisualsPathProp?.GetValue(character) as string;
        }
        catch (System.Exception e)
        {
            Logger.Log("[CharBoss] 反射读 CharacterModel.VisualsPath 失败: " + e.Message);
            return null;
        }
    }

    /// <summary>
    /// 用**角色**的动画器（状态名是 "Attack"/"Cast"/"Hit"/"PowerUp"），
    /// 而不是怪物动画器（"attack"/"cast"/"hurt"）—— 否则卡牌攻击不会播动画。详见 <see cref="BossVisuals"/>。
    /// </summary>
    public override MegaCrit.Sts2.Core.Animation.CreatureAnimator GenerateAnimator(
        MegaCrit.Sts2.Core.Bindings.MegaSpine.MegaSprite controller)
        => BossVisuals.BuildCharacterAnimator(this, Def, controller) ?? base.GenerateAnimator(controller);

    /// <summary>
    /// 按**真实幕数**校正血量。
    ///
    /// 生物的 HP 是在 <c>Creature</c> 构造时由 <see cref="MinInitialHp"/> 算出来的，而那一刻
    /// <c>MonsterModel.CombatState</c> 还不可用（"Creature was accessed before it was set"），
    /// `BossActIndex` 只能退回 RunManager，未必拿得到 —— 结果是三层 Boss 用了第一幕的血量
    /// （用户实测：三层故障机器人只有 290 血，应该是 75 + 480 = 555）。
    /// 进房后 CombatState 一定可用，这里再校正一次。
    /// </summary>
    private void FixActHp()
    {
        try
        {
            var creature = Creature;
            if (creature == null) return;

            int target = Def.HpForAct(ActIndex) + BlessingHpBonus + VariantHpBonus;
            if (target <= 0 || creature.MaxHp == target) return;

            float ratio = creature.MaxHp > 0 ? (float)creature.CurrentHp / creature.MaxHp : 1f;
            // 用游戏自己的内部方法改，血条/钩子才会跟着刷新（直接改后备字段不会通知 UI）
            creature.SetMaxHpInternal(target);
            creature.SetCurrentHpInternal(Math.Max(1, (int)Math.Round(target * ratio)));
            Logger.Log($"[CharBoss] {Def.Key} 幕数={ActIndex} 血量校正为 {target}（当前 {creature.CurrentHp}）");
        }
        catch (System.Exception e)
        {
            Logger.Log("[CharBoss] 血量校正失败: " + e.Message);
        }
    }

    /// <summary>
    /// 离开战斗房时**要不要停掉我们自己播的 BGM**（用户 2026-10-29）。
    ///
    /// 规则：**只有这场 Boss 战真的结束了才停** ——
    ///   · 场上已经没有别的活着的我们的 Boss（把最后一只打死 / 全部阵亡）⇒ 停（随后由游戏播胜利音效）；
    ///   · 还有别的 Boss 活着、而且战斗仍在进行 ⇒ **不停**（以前无条件停，打死一只就静音了）；
    ///   · 还有别的 Boss 活着、但战斗已经不在了（玩家全灭 / 退出战斗）⇒ 停。
    /// </summary>
    private bool ShouldStopBgmOnLeaveRoom()
    {
        try
        {
            if (!Core.MultiBossPlan.AnyOtherBossAlive(this)) return true;

            bool live;
            try { live = Creature?.CombatState?.IsLiveCombat() == true; }
            catch { live = false; }
            return !live;
        }
        catch { return true; }
    }

    /// <summary>离开战斗房时把 Boss 手牌栏收掉（否则会残留到地图界面）。</summary>
    public override void BeforeRemovedFromRoom()
    {
        // ★ v0.2.418：把「本场遭遇」的记录清掉 —— 否则它会在下一场开打前的那一小段时间里
        //   被变体判定读到（那时 `state.Encounter` 还是空的），把**上一场**的遭遇当成这一场的
        //   ⇒ 直接判错并锁定（用户实测 `fight DEFECT_EVENT_BOSS_ENCOUNTER` 打成能力机就是这个串场）。
        try { Core.BossLineups.ClearFightEncounter(); } catch { /* 忽略 */ }

        // 先算「她还活着吗」—— BGM 与数据记录都要用（快捷 SL/回滚时她是**活着**被移出房间的）。
        bool bossStillAlive = false;
        try { bossStillAlive = Creature != null && Creature.IsAlive; } catch { bossStillAlive = false; }
        // ★ 2026-11-05（用户：「所有仆从带有爪牙，在储君死亡时死亡；其它 boss 死亡不会使储君的仆从死亡」）：
        //   仆从是**敌方怪物**（不是玩家宠物），原版 `MinionPower` 不会替我们处理
        //   「召唤者死亡 ⇒ 仆从死亡」，所以在这里统一清场 —— 只清**这只 boss 自己召唤的**那几只。
        try
        {
            if (!bossStillAlive)
                MegaCrit.Sts2.Core.Helpers.TaskHelper.RunSafely(Monsters.BossMinionMonster.KillAllOf(this));
        }
        catch { /* 忽略 */ }

        // ⚠ **多人时不能在这里停 BGM**（用户 2026-10-29：「一名 boss 死亡后 boss 战的背景音乐就停了。
        //   应该等所有 boss 都死亡后再停止播放，然后播放 boss 战胜利音效」）。
        //   以前这里无条件 `BossBgm.Stop()` —— 任意一只 Boss 被移出房间（= 死亡）就把音乐掐掉，
        //   而其它 Boss 还在打。现在只在「场上没有别的活着的我们的 Boss」时才安排还原，
        //   而且**不是立刻 stop**（立刻 stop 会把刚起的胜利段落掐掉，见 BossBgm.VictoryKeepMs）。
        try { if (ShouldStopBgmOnLeaveRoom()) Core.BossBgm.OnFightEnded("Boss 被移出房间（本场战斗结束）", bossDied: !bossStillAlive); }
        catch { /* 忽略 */ }
        // 崩坠（mods\Downfall）收藏家兼容：她被打死时掉落对应的「藏品牌」（用户 2026-10-23）。
        // 放在收面板之前 —— 那时玩家与战斗数据都还在。没装崩坠时内部会静默跳过。
        try { Core.DownfallCollectibleCompat.OnBossDefeated(this); }
        catch (System.Exception e) { Logger.Log("[CharBoss] 收藏家兼容失败: " + e.Message); }

        // 战斗结束：把她的随从收起来（用户 2026-10-23：亡灵契约师死后奥斯提尸体还躺在场上，半死不活）。
        // ⚠ 分两种结局（用户 2026-10-27 提问后明确）：
        //   · 她被打死（我们赢了）⇒ 连随从一起弄死（派发 Died，胜利界面不留活着的随从）；
        //   · 玩家被打死（我们输了、她还活着）⇒ **只把节点收起来**，不派死亡事件 ——
        //     这一场马上就要被整个拆掉，没必要在拆场景时多触发一轮 Died（别的模组可能挂在上面）。

        // 本地数据记录（A5，默认关）：一场我们的 Boss 战结束就记一条（won = 她被打死）。
        // 只写本机文件、不上传；开关由玩家在「模组配置 → CharacterBosses → A5」自己勾。见 Core/Telemetry.cs。
        // ⚠ v0.2.245：还要告诉它「真人玩家是否全灭」—— 双方都活着就结束（控制台/中途结束）时
        //   不能再记成 lose，否则胜率统计被污染（用户 2026-10-30：「我没死啊，是她直接倒了」）。
        try
        {
            // ⚠ v0.2.252：要同时告诉它「真人玩家有几个 / 还有几个活的」——
            //   中途退出、弃档、房间被拆时这张表可能是空的，**空表不能当成「玩家全灭」**
            //   （用户 2026-10-31：「我打一半后退出的数据怎么也被上传而且判定成输了」）。
            int playerCount = 0;
            bool anyPlayerAlive = false;
            try
            {
                var players = CharacterBosses.Core.BossLockOn.RealPlayers(CombatState);
                playerCount = players?.Count ?? 0;
                foreach (var each in players ?? new System.Collections.Generic.List<MegaCrit.Sts2.Core.Entities.Players.Player>())
                {
                    if (each?.Creature != null && each.Creature.IsAlive) { anyPlayerAlive = true; break; }
                }
            }
            catch { playerCount = 0; anyPlayerAlive = false; }

            Core.Telemetry.NoteBossEnded(this, bossDead: !bossStillAlive, playersAlive: anyPlayerAlive,
                                         playerCount: playerCount,
                                         anyOtherBossAlive: Core.MultiBossPlan.AnyOtherBossAlive(this));
        }
        catch { /* 忽略 */ }

        try { Core.BossOsty.PurgeAllOnFightEnd(this, killPets: !bossStillAlive); }
        catch (System.Exception e) { Logger.Log("[CharBoss] 战斗结束清随从失败: " + e.Message); }

        // 收面板：**多人时只收她自己那一套**（用户 2026-10-26 反馈：双人联机击败一只 Boss 后，
        // 另一只还活着的 Boss 这一回合的意图面板会整块消失）。
        // 根因就是这里以前无条件调 `BossHandUi.EndFight` —— 它会把**所有**面板连同锁定/诊断一起清掉，
        // 而那只 Boss 要等到它下个回合 `PrepareTurn` 才重建面板。三人/四人局同一个根因。
        try
        {
            if (Core.MultiBossPlan.AnyOtherBossAlive(this))
            {
                CharacterBosses.UI.BossHandUi.ReleasePanel(this);
                Logger.Log($"[CharBoss] {Def.Key} 已阵亡：只收它自己那套面板，" +
                           "其它 Boss 的手牌栏/意图保持不动（本场战斗还没结束）");
            }
            else
            {
                CharacterBosses.UI.BossHandUi.EndFight("BeforeRemovedFromRoom");
            }
        }
        catch (System.Exception e) { Logger.Log("[CharBoss] hand ui cleanup failed: " + e.Message); }
        // 同上：只在这场战斗真的结束（没有别的 Boss 活着）时才还原音乐 ——
        // 否则「打死一只、其余还在打」时背景音乐会突然消失（用户 2026-10-29）。
        try { if (ShouldStopBgmOnLeaveRoom()) Core.BossBgm.OnFightEnded("Boss 被移出房间（收面板后）", bossDied: !bossStillAlive); }
        catch { /* 忽略 */ }
        base.BeforeRemovedFromRoom();
    }
}