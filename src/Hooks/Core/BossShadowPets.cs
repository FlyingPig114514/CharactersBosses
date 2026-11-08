using System;
using Godot;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using CharacterBosses.Monsters;

namespace CharacterBosses.Core;

/// <summary>
/// 影子玩家（Boss）名下**随从/召唤物**的朝向与站位修正（储君的「佩尔的士兵」
/// <c>PaelsLegion</c> 就属于这一类）。
///
/// 为什么需要：
///   · 原版摆位只对**本地玩家**的随从生效 —— <c>OstyScaleToSize</c> 里
///     <c>if (LocalContext.IsMe(Entity.PetOwner))</c> 才 tween 到
///     <c>GetOstyOffsetFromPlayer(Entity)</c> 算出的位置；影子玩家不是本地玩家 ⇒ 没人给它摆位，
///     它就停在默认点（看起来「位置不对」）。
///   · 朝向同样是照「主人在左边」画的，Boss 在右边 ⇒ 朝左才对（和奥斯提同一套做法）。
///
/// 做法：照抄原版公式，把 X 取负（敌方侧 = 主人的左手边），朝向镜像 <c>NCreature.Body</c>。
/// 位置只在**偏离太多**时才纠正 —— 否则每帧硬写会把随从自己的攻击/受击位移吃掉。
/// </summary>
public static class BossShadowPets
{
    /// <summary>
    /// 偏离目标位置超过这个距离才重新摆位（像素）。
    /// 别调大：原版随从就贴在主人脚下（横向只差 20px 左右），阈值太大会让"差一点"永远不纠正。
    /// 也别调到 0：随从自己攻击/受击有位移补间，每帧硬写会把它吃掉。
    /// </summary>
    private const float SnapDistance = 40f;
    private static bool _logged;

    // ── 「随从只当形象、不当能承伤的敌人」登记表 ──────────────────────────────
    // 由 Update 每帧登记，ShadowPetNotHittablePatch 用它把随从从「可命中敌人」里摘掉。
    private static readonly HashSet<Creature> Known = new();

    /// <summary>
    /// 随从 → 它的主人（哪只 Boss）。多人同场两只 Boss 时**必须有**：
    /// 否则打死一只会把另一只的士兵一起摘掉，死亡清理也分不清是谁的。
    /// </summary>
    private static readonly Dictionary<Creature, CharBossMonster> OwnedBy = new();

    /// <summary>已经挂过「死亡 → 清理随从」监听的 Boss 生物（按实例 hash；每只只挂一次）。</summary>
    private static readonly HashSet<int> DeathHooked = new();

    /// <summary>已经做过死亡清理的 Boss（按实例 hash；每只各一次）。</summary>
    private static readonly HashSet<int> CleanedFor = new();

    // 已经上过爪牙标记的随从（PowerCmd.Apply 是异步的，用集合避免每帧重复施加）
    private static readonly HashSet<Creature> MarkedAsMinion = new();

    /// <summary>这只生物是不是我们 Boss 名下、只该当形象不该承伤的随从。</summary>
    internal static bool IsShadowPet(Creature? creature) => creature != null && Known.Contains(creature);

    /// <summary>「不可命中」日志只报一次（给 <c>ShadowPetNeverHittablePatch</c> 用）。</summary>
    internal static bool NoteBlockedHit(Creature? creature)
    {
        if (creature == null) return false;
        return _blockLogged.Add(creature);
    }

    /// <summary>
    /// Boss 一死就把这些装饰性随从**摘出战斗**（尸体节点也收掉）。
    ///
    /// 为什么必须做（用户实测：储君死亡后战斗不结束）：
    /// 反编译 <c>CombatManager.IsCombatEnding</c>：
    /// <code>
    /// if (State.Enemies.Any(e => e != null &amp;&amp; e.IsAlive &amp;&amp; e.IsPrimaryEnemy)) return false;
    /// </code>
    /// 佩尔的士兵在敌方一侧，只要它还算「活着的敌人」战斗就不会结算
    /// （虽然有 <c>MinionPower</c> 把它降成次要敌人，但那条判定只是"不需要杀它"，
    ///  一旦有任何路径让它重新算成 primary，或者它压根没吃到那层 Power，就会卡住）。
    /// 这里在 Boss 死亡后把它彻底摘出战斗列表 + 隐藏节点，一劳永逸。
    /// **不动任何其它逻辑**：只在 Boss 死亡这一刻做一次。
    /// </summary>
    internal static void RemoveOnBossDeath(CharBossMonster? boss)
    {
        try
        {
            if (boss?.Creature == null || !boss.Creature.IsDead) return;
            int bossKey = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(boss);
            if (!CleanedFor.Add(bossKey)) return;

            int removed = 0;
            // ① 已登记的随从 —— **只摘这只 Boss 自己的**（多人同场时别把队友的士兵一起带走）
            foreach (var pet in Known.ToList())
            {
                if (OwnedBy.TryGetValue(pet, out var owner) && !ReferenceEquals(owner, boss)) continue;
                RemovePet(pet);
                Known.Remove(pet);
                OwnedBy.Remove(pet);
                removed++;
            }

            // ② 兜底：扫一遍战斗列表，凡是「在她那一侧、名字是佩尔的士兵」的都摘掉
            //（万一它没被登记进 Known —— 例如这一帧 Update 还没跑到。）
            try
            {
                var combat = boss.CombatState;
                if (combat != null)
                {
                    foreach (var list in new[] { combat.Enemies, combat.Allies })
                    {
                        if (list == null) continue;
                        foreach (var creature in list.ToList())
                        {
                            if (creature == null || Known.Contains(creature)) continue;
                            if (creature.Side != boss.Creature.Side) continue;
                            // ★ v0.2.506（玩家反馈：多人局「仆从储」死了以后他的随从还活着）：
                            //   以前这里只认「佩尔的士兵」(PAELS_LEGION) 这一种装饰随从，
                            //   而仆从储的「征召上前」召出来的是 **MINION_STRIKE_MONSTER**（打击仆从）⇒ 一个都没清掉。
                            //   现在把「她召出来的仆从」也算进来：名字里带 PAELS / MINION，
                            //   或者宠物主人是她的影子玩家（PetOwner / Player）。
                            string petEntry = SafeEntry(creature);
                            bool oursPet = petEntry.Contains("PAELS", StringComparison.OrdinalIgnoreCase)
                                           || petEntry.Contains("MINION", StringComparison.OrdinalIgnoreCase);
                            if (!oursPet)
                            {
                                try
                                {
                                    var owner = creature.PetOwner;
                                    if (owner != null && boss.Shadow != null && ReferenceEquals(owner, boss.Shadow)) oursPet = true;
                                }
                                catch { /* 忽略 */ }
                            }
                            if (!oursPet) continue;
                            RemovePet(creature);
                            removed++;
                        }
                    }
                }
            }
            catch (Exception e) { Logger.Log("[CharBoss] 扫战斗列表失败: " + e.Message); }

            Logger.Log($"[CharBoss] {boss.Id.Entry} 已死：移除 {removed} 只装饰性随从（避免战斗卡在「还没打完」）");

            // 诊断：把「战斗为什么还没结束」的直接依据打出来。
            // CombatManager.IsCombatEnding 只看 `State.Enemies.Any(e => e.IsAlive && e.IsPrimaryEnemy)`，
            // 所以这里把每个敌人的 alive / IsPrimaryEnemy / IsSecondaryEnemy / 身上的 Power 全列一遍，
            // 一眼就能看出是谁在拦（顺带能看出我们的 IsSecondaryEnemy 补丁有没有生效）。
            try
            {
                var combat = boss.CombatState;
                if (combat != null)
                {
                    foreach (var e in combat.Enemies)
                    {
                        if (e == null) continue;
                        string powers;
                        try { powers = string.Join(",", e.Powers.Select(p => p.Id.Entry)); }
                        catch { powers = "?"; }
                        Logger.Log($"[CharBoss] 死亡诊断: 敌人={e.Name} alive={e.IsAlive} " +
                                   $"primary={e.IsPrimaryEnemy} secondary={e.IsSecondaryEnemy} powers=[{powers}]");
                    }
                }
            }
            catch (Exception e2) { Logger.Log("[CharBoss] 死亡诊断失败: " + e2.Message); }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] Boss 死亡后清理随从失败: " + e.Message);
        }
    }

    /// <summary>
    /// 挂「Boss 死亡」事件（**每只 Boss 各一次**）。
    ///
    /// 为什么不能只靠每帧检查：实测 Boss 死后那些帧驱动源（辉星计数器 / 能量球 / 君王之剑）会被
    /// 战斗界面收掉，`Tick` 可能就不再跑了 ⇒ <see cref="RemoveOnBossDeath"/> 永远等不到。
    /// 直接挂 <c>Creature.Died</c>（原版 <c>NSovereignBladeVfx._Ready</c> 也是这么监听的）最可靠。
    ///
    /// ⚠ 多人同场两只 Boss 时**必须按生物去重**：以前只有一个静态字段记「挂过谁」，
    /// 两只交替回来就会每帧重复挂一次监听（死的时候几千个回调）。
    /// </summary>
    internal static void EnsureDeathHook(CharBossMonster boss)
    {
        try
        {
            var creature = boss.Creature;
            if (creature == null) return;
            if (!DeathHooked.Add(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(creature))) return;
            creature.Died += _ => RemoveOnBossDeath(boss);
            Logger.Log($"[CharBoss] 已挂上「{boss.Id.Entry} 死亡 → 清理装饰性随从」的监听");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 挂死亡监听失败: " + e.Message);
        }
    }

    /// <summary>把一只随从从战斗列表里摘掉并收掉它的节点。</summary>
    private static void RemovePet(Creature? pet)
    {
        try
        {
            if (pet == null) return;
            HideNode(pet);
            DetachFromCombat(pet);
        }
        catch (Exception e) { Logger.Log("[CharBoss] 移除随从失败: " + e.Message); }
    }

    private static void HideNode(Creature pet)
    {
        try
        {
            var room = NCombatRoom.Instance;
            var node = room?.GetCreatureNode(pet);
            if (node == null || !GodotObject.IsInstanceValid(node))
            {
                node = FindInTree(room!, pet);      // 兜底：全树找
                if (node == null) return;
            }
            node.Visible = false;
            node.Modulate = new Color(1f, 1f, 1f, 0f);
        }
        catch { /* 忽略 */ }
    }

    private static readonly System.Reflection.FieldInfo? EnemiesField =
        typeof(MegaCrit.Sts2.Core.Combat.CombatState).GetField("_enemies",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
    private static readonly System.Reflection.FieldInfo? AlliesField =
        typeof(MegaCrit.Sts2.Core.Combat.CombatState).GetField("_allies",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

    /// <summary>把它从战斗的生物列表里摘掉（只影响遍历，不动存档）。</summary>
    private static void DetachFromCombat(Creature pet)
    {
        try
        {
            if (pet.CombatState == null) return;
            foreach (var field in new[] { EnemiesField, AlliesField })
            {
                if (field?.GetValue(pet.CombatState) is not System.Collections.IList list) continue;
                if (list.Contains(pet)) list.Remove(pet);
            }
        }
        catch (Exception e) { Logger.Log("[CharBoss] 摘出战斗列表失败: " + e.Message); }
    }

    private static string SafeEntry(Creature creature)
    {
        try { return creature.Monster?.Id.Entry ?? creature.Name; }
        catch { return creature.Name ?? "?"; }
    }

    private static readonly HashSet<Creature> _blockLogged = new();

    /// <summary>场上有没有登记过随从（没有时补丁直接放行，零开销）。</summary>
    internal static bool HasKnownPets => Known.Count > 0;

    // 「这间战斗房里有没有我们的角色 Boss」——按战斗实例缓存，只有换了一间战斗才重扫。
    private static object? _bossFightKey;
    private static bool _bossFightActive;

    /// <summary>
    /// 这间战斗房里有没有我们的角色 Boss。
    ///
    /// 为什么需要它（测试玩家 2026-10-23 实测）：过滤「可命中敌人 / 对手列表」的那两个补丁
    /// 原来拿 <see cref="HasKnownPets"/> 当零开销闸门，可**亡灵契约师没有装饰性随从**
    /// （她只有奥斯提，奥斯提不登记进 <c>Known</c>）⇒ 闸门永远是 false ⇒ 整段过滤被跳过
    /// ⇒ 她的奥斯提留在 <c>HittableEnemies</c> 里，被崩坠「转嫁疼痛」
    /// （<c>Collector.FeelMyPainPower</c>：随机取 <c>HittableEnemies</c> 的一只）当成靶子打，
    /// 而且专挑**已经死了的那只**（死的仍留在列表里更久）。
    ///
    /// 代价：每次读取只是一次引用比较 + 换战斗时扫一遍敌人/友军列表。
    /// </summary>
    internal static bool BossFightActive(MegaCrit.Sts2.Core.Combat.ICombatState? state)
    {
        try
        {
            if (state == null) return false;
            if (ReferenceEquals(_bossFightKey, state)) return _bossFightActive;

            bool found = false;
            foreach (var list in new[] { state.Enemies, state.Allies })
            {
                if (list == null) continue;
                foreach (var creature in list)
                {
                    if (creature?.Monster is CharacterBosses.Monsters.CharBossMonster)
                    {
                        found = true;
                        break;
                    }
                }
                if (found) break;
            }

            _bossFightKey = state;
            _bossFightActive = found;
            return found;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>每场战斗重新武装诊断日志 + 清空随从登记（「随从诊断」只在第一帧打一条）。</summary>
    public static void ResetDiagnostics()
    {
        _logged = false;
        _facingLogs = 0;
        _bailLogged = false;
        _petNodeLogged = false;
        Known.Clear();
        OwnedBy.Clear();
        MarkedAsMinion.Clear();
        _blockLogged.Clear();
        DeathHooked.Clear();
        CleanedFor.Clear();
        try { Patches.ShadowPetNotHittablePatch.ResetDroppedLog(); } catch { /* 忽略 */ }
    }

    public static void Update(CharBossMonster? boss)
    {
        try
        {
            if (boss == null) { Bail("boss=null"); return; }
            var pcs = boss.Shadow?.PlayerCombatState;
            var room = NCombatRoom.Instance;
            if (boss.Creature == null) { Bail("boss.Creature=null"); return; }
            if (pcs == null) { Bail("shadow.PlayerCombatState=null"); return; }
            if (room == null) { Bail("NCombatRoom.Instance=null"); return; }

            var bossNode = FindCreatureNode(room, boss.Creature);
            if (bossNode == null) { Bail("bossNode=null（GetCreatureNode 与全树搜索都没找到）"); return; }

            EnsureDeathHook(boss);

            var pets = pcs.Pets;
            if (!_logged)
            {
                _logged = true;
                var names = new List<string>();
                for (int i = 0; i < (pets?.Count ?? 0); i++)
                {
                    var p = pets![i];
                    names.Add(p == null ? "null" : $"{p.Name}(alive={p.IsAlive},pet={p.PetOwner != null})");
                }
                Logger.Log($"[CharBoss] 随从诊断: pcs.Pets={pets?.Count ?? 0} [{string.Join(",", names)}] " +
                           $"creaturePets={boss.Creature.Pets?.Count ?? 0}");
            }
            for (int i = 0; i < (pets?.Count ?? 0); i++)
            {
                var pet = pets![i];
                if (pet == null || !pet.IsAlive) continue;
                // 奥斯提自己有整套处理（BossOsty），这里不能抢；
                // 而且奥斯提是**真随从**（该承伤、该有血条），用户明确说两者不是一回事。
                if (BossOsty.IsBossOsty(pet)) continue;

                // 登记成「只当形象的随从」，让 ShadowPetNotHittablePatch 把它从可命中敌人里摘掉
                Known.Add(pet);
                OwnedBy[pet] = boss;      // 记清是哪只 Boss 的（多人同场时死亡清理要按主人分）

                // **爪牙标记**：让战斗能正常结束。
                // 反编译 CombatManager.IsCombatEnding：
                //   if (State.Enemies.Any(e => e.IsAlive && e.IsPrimaryEnemy)) return false;  // 还没打完
                // 而 Creature.IsPrimaryEnemy => Side == Enemy && !IsSecondaryEnemy，
                // IsSecondaryEnemy => Powers.Any(p => p.OwnerIsSecondaryEnemy)。
                // 也就是说：只要这只随从还算「必需敌人」，储君一死战斗也结束不了（用户实测）。
                // 而 Creature.Side 是只读的，没法把它移出敌方列表 —— 游戏给的唯一出口就是
                // 带 OwnerIsSecondaryEnemy 的 Power，即原版 MinionPower（纯分类位：
                // ShouldPlayVfx=false，不给攻击、不给血、不让它承伤）。
                if (MarkedAsMinion.Add(pet))
                {
                    BossOsty.MarkMinion(boss, pet, null);
                }

                var node = FindCreatureNode(room, pet);
                if (node == null)
                {
                    if (!_petNodeLogged)
                    {
                        _petNodeLogged = true;
                        Logger.Log($"[CharBoss] 随从节点还没建好: {pet.Name}（GetCreatureNode + 全树搜索都没命中），下一帧再试");
                    }
                    continue;
                }

                ForceFaceLeft(node, pet);

                var offset = MirrorPetOffset(bossNode, node, i, pets!.Count);
                var want = bossNode.GlobalPosition + offset;
                if (node.GlobalPosition.DistanceTo(want) > SnapDistance)
                {
                    node.GlobalPosition = want;
                    Logger.Log($"[CharBoss] 随从摆位: {pet.Name} → bossPos+{offset}（原版主人脚下公式，X 镜像）");
                }

                // ⚠ 这里**故意不调 BossOsty.ForceVisible**。
                // 用户实测反馈：「佩尔的士兵不该被单独搞成一个能承伤的敌人」——
                // ForceVisible 会把随从的 HealthBar/StateDisplay 整块点亮，于是它看起来就是个
                // 带着 9999/9999 血条的独立敌人。它只该是立在主人身边的一个形象，
                // 所以血条交回原版（非本地玩家的随从本来就该被压掉），
                // 同时配合 ShadowPetNotHittablePatch 让它不再是可命中目标。
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 随从摆位失败: " + e.Message);
        }
    }

    /// <summary>
    /// 朝向：**每帧强制**把立绘骨架的 <c>Scale.X</c> 按成负值（朝左）。
    ///
    /// 为什么不能像奥斯提那样只翻一次（用户实测：「刚进战斗的时候士兵朝右，但储君的回合一开始就好了」）：
    /// 游戏自己有一套朝向权威 —— <c>SurroundedPower.FaceDirection</c>，它会
    /// <code>
    /// var arr = [Owner, ...Owner.Pets];
    /// arr.Select(c =&gt; NCombatRoom.GetCreatureNode(c).Body).ForEach(body =&gt; FlipScale(body));
    /// </code>
    /// 也就是**主人和它的每一个随从**都会被翻一遍（这就是"系统给佩尔的士兵兜底"）。
    /// 谁先谁后取决于它什么时候跑：战斗刚开始那次把我们翻成正的（朝右），
    /// 到他的回合开始才又被我们的镜像翻回来 —— 于是看起来"进战斗时朝右、回合开了才对"。
    ///
    /// 所以这里不再"翻一次就完事"，而是每帧把符号钉死成负的：
    /// 谁把它写正，下一帧就被改回来。只在真的发生变化时打日志（并限条数），
    /// 这样日志能直接看出到底是谁在写（<c>朝向: 0.250 → -0.250</c> 就是被别人写正了）。
    /// </summary>
    private static void ForceFaceLeft(NCreature node, Creature pet)
    {
        try
        {
            var body = node.Body;
            if (body == null || !GodotObject.IsInstanceValid(body)) return;

            var s = body.Scale;
            float want = -Math.Abs(s.X);
            if (Mathf.Abs(s.X) > 0.0001f && Mathf.Abs(s.X - want) <= 0.0001f) return;

            body.Scale = new Vector2(want, s.Y);
            if (_facingLogs < 12)
            {
                _facingLogs++;
                Logger.Log($"[CharBoss] 随从朝向钉成朝左: {pet.Name} node#{node.GetInstanceId()} " +
                           $"Body={body.Name} Scale.X {s.X:0.000} → {want:0.000}");
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 随从朝向失败: " + e.Message);
        }
    }

    private static int _facingLogs;
    private static bool _bailLogged;
    private static bool _petNodeLogged;

    /// <summary>
    /// 为什么这一帧没干活 —— 每场战斗只打一条。
    /// （上一轮就是被这些静默 return 坑了：日志里既没有「没找到」也没有「已镜像」，
    ///  完全看不出是"没跑"还是"跑了但提前退出"。）
    /// </summary>
    private static void Bail(string reason)
    {
        if (_bailLogged) return;
        _bailLogged = true;
        Logger.Log($"[CharBoss] 随从处理提前退出（本场只报一次）: {reason}");
    }

    /// <summary>
    /// 取某只生物的 <c>NCreature</c> 节点：先走原版 <c>GetCreatureNode</c>，
    /// 没拿到就**全树搜** <c>Entity</c> 相同的那一个。
    ///
    /// 为什么要兜底：实测进入战斗后的**整个玩家回合**里，`Update`/`Ensure` 都在这里提前退出
    /// （日志里既没有「随从诊断」也没有「没找到君王之剑」），而到了他的回合开始就突然好了 ——
    /// 说明这段时间 `GetCreatureNode(boss.Creature)` 拿不到节点。全树搜索能绕开它。
    /// </summary>
    private static NCreature? FindCreatureNode(Node room, Creature? creature)
    {
        if (creature == null) return null;
        try
        {
            var direct = (room as NCombatRoom)?.GetCreatureNode(creature) as NCreature;
            if (direct != null && GodotObject.IsInstanceValid(direct)) return direct;
        }
        catch { /* 忽略，走全树搜索 */ }
        try { return FindInTree(room, creature); }
        catch { return null; }
    }

    private static NCreature? FindInTree(Node node, Creature creature)
    {
        foreach (var child in node.GetChildren())
        {
            if (child is not Node n || !GodotObject.IsInstanceValid(n)) continue;
            if (n is NCreature nc)
            {
                try
                {
                    if (ReferenceEquals(nc.Entity, creature)) return nc;
                }
                catch { /* 忽略单个节点 */ }
            }
            var hit = FindInTree(n, creature);
            if (hit != null) return hit;
        }
        return null;
    }

    /// <summary>
    /// 随从相对主人的偏移 —— **原版 <c>NCombatRoom.PositionPlayersAndPets</c> 里"随从"那一支的公式，X 镜像**。
    ///
    /// 原版（玩家侧）逐字是：
    /// <code>
    /// num15 = pets.Count &gt; 1 ? player.Visuals.Bounds.Size.X / (pets.Count - 1) : 0f;
    /// pet.Position = new Vector2(
    ///     0 - targetXPos + 20f - i * num15 - pet.Visuals.Bounds.Size.X * 0.5f,
    ///     player.Position.Y + 10f);
    /// </code>
    /// 而 <c>player.Position.X = 0 - targetXPos - player.Visuals.Bounds.Size.X * 0.5f</c>，
    /// 所以**相对主人**的偏移 = <c>(20 - i*num15 + (主人宽 - 随从宽)/2, +10)</c>：
    ///   · 横向只差 20px + 半个宽度差 —— 随从就贴着主人站；
    ///   · 纵向 **+10（在主人脚下）**。
    ///
    /// 上一版错在照搬了 <c>Osty</c> 那套偏移（<c>Osty.MinOffset/MaxOffset ≈ (250, -75)</c>），
    /// 那是给"奥斯提那只大手"用的，算出来 -365px / 往上 75px ⇒ 士兵被扔到场地中间
    /// （用户实测：「放的位置太远了，你看左边玩家的就在脚下」）。
    /// 敌方侧把 X 取负 = 站到主人**左手边**。
    /// </summary>
    private static Vector2 MirrorPetOffset(NCreature bossNode, NCreature petNode, int petIndex, int petCount)
    {
        float playerW = BoundsWidth(bossNode);
        float petW = BoundsWidth(petNode);
        float spacing = petCount > 1 ? playerW / (petCount - 1) : 0f;

        float dx = 20f - petIndex * spacing + (playerW - petW) * 0.5f;
        return new Vector2(-dx, 10f);
    }

    /// <summary>原版用的是 <c>Visuals.Bounds.Size.X</c>；取不到就退回 hitbox 宽度。</summary>
    private static float BoundsWidth(NCreature node)
    {
        try
        {
            float w = node.Visuals.Bounds.Size.X;
            if (w > 1f) return w;
        }
        catch { /* 忽略 */ }
        try
        {
            if (node.Hitbox != null) return node.Hitbox.Size.X;
        }
        catch { /* 忽略 */ }
        return 200f;
    }
}
