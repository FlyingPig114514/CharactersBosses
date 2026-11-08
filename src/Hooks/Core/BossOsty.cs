using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using CharacterBosses.Monsters;

namespace CharacterBosses.Core;

/// <summary>
/// Boss 侧那�?*奥斯�?*（亡灵契约师的随从）的收尾工作�?
    ///
/// 三个问题（都实测过）�?
    ///   1. **死了就再也用不了** —�?<c>Player.Osty => PlayerCombatState.GetPet&lt;Osty&gt;()</c>�?
    ///      �?<c>GetPet</c> 取的�?<c>Pets</c> �?*第一�?* Osty。原版碎骨会把奥斯提杀死，
///      <c>OstyCmd.Summon</c> 复活时又 <c>AddPet</c> 一个新的，于是列表里躺着一死一活，
///      `GetPet` 拿到的是**那只死的** �?之后所有奥斯提牌都判成「奥斯提已死」而空转�?
    ///      这里把死掉的那几只从 <c>Pets</c> 里摘掉（<c>_pets</c> 是私�?List，反射操作）�?
    ///   2. **爪牙标记** —�?用户的设定：她的奥斯提算是爪牙，补一层原�?<c>MinionPower</c>�?
    ///   3. **朝向与站�?* —�?她在玩家侧是镜像的，随从也要翻过来并站到她的左侧�?
    /// </summary>
internal static class BossOsty
{
    private static readonly FieldInfo? HealthBarField =
        typeof(NCreature).GetField("_healthBar", BindingFlags.Instance | BindingFlags.NonPublic);

    // 血�?状态栏是靠 modulate.a 淡入淡出的（ShowNameplate/HideNameplate 就是补间这两个容器的 alpha�?
            private static readonly FieldInfo? NameplateField =
        typeof(NCreature).GetField("_nameplateContainer", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? PowerContainerField =
        typeof(NCreature).GetField("_powerContainer", BindingFlags.Instance | BindingFlags.NonPublic);

    /// <summary>
    /// 让她的奥斯提**血条与状态栏常显**�?
        ///
    /// 原版�?不是本地玩家的随�?会把血条压�?隐藏（联机时看不到队友奥斯提的血量就是这个逻辑），
    /// 影子玩家不算本地玩家 �?一并被压掉。和当初「充能球不常亮」是同一类问题（本地标记）�?
        /// 这里把节点和血条强制设成可�?+ 白色（原�?<c>NCreature._healthBar</c>）�?
        /// </summary>
    /// <summary>
    /// 让她的奥斯提**稳定朝左**�?
        ///
    /// 探针实测（关键）：`NCreature.Visuals.Scale.X` 始终�?*正数**�?.000 ~ 1.007），
    /// 因为 `OstyScaleToSize`（血量变�?�?缩放补间）会不断把它写成正数 —�?所�?
        /// 「翻 Visuals」必然被补间转回去（这就�?出场向左、慢慢转回右�?的真相）�?
        /// 而底图本身就是朝右，朝向也不�?Visuals 的符号上�?
        ///
    /// 做法：在 `Visuals` 里插一�?`Scale=(-1,1)` 的镜像容器，把立绘子节点都搬进去�?
        /// 补间只碰 `Visuals`，镜像容器一直在内层，因�?*一次到位、永不回�?*�?
        /// 血�?名字牌是 `NCreature` 的直接子节点，不在这�?�?文字不会镜像�?
        /// </summary>
    private static void MirrorVisuals(Node ostyNode)
    {
        try
        {
            if (ostyNode is not NCreature oc || oc.Visuals == null) return;
            var visuals = oc.Visuals;

            Node2D? mirror = null;
            foreach (var child in visuals.GetChildren())
            {
                if (child is Node2D n && n.Name.ToString() == "CharBossMirror") { mirror = n; break; }
            }
            if (mirror == null)
            {
                mirror = new Node2D { Name = "CharBossMirror", Scale = new Vector2(-1f, 1f) };
                visuals.AddChild(mirror);
                Logger.Log("[CharBoss] 奥斯提镜像容器已创建（立绘朝左）");
            }

            // �?Visuals 下除镜像容器以外的子节点搬进去（新加的也一并搬�?         
    foreach (var child in visuals.GetChildren())
            {
                if (ReferenceEquals(child, mirror)) continue;
                if (child is Node node && GodotObject.IsInstanceValid(node)) node.Reparent(mirror);
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 奥斯提镜像失败: " + e.Message);
        }
    }
    private static bool _facingLoopRunning;
    private static float _lastScaleX = float.NaN;

    /// <summary>探针：她的奥斯提 Visuals.Scale.X 一变就打一行（定位"谁把朝向写回�?）�?/summary>
    private static void ProbeScale(Node ostyNode)
    {
        try
        {
            if (ostyNode is not NCreature oc || oc.Visuals == null) return;
            float x = oc.Visuals.Scale.X;
            if (Math.Abs(x - _lastScaleX) < 0.001f) return;
            Logger.Log($"[CharBoss] 朝向探针: Visuals.Scale.X {_lastScaleX:0.000} → {x:0.000}");
            _lastScaleX = x;
        }
        catch { /* 忽略 */ }
    }

    /// <summary>
    /// 把奥斯提�?*立绘骨架节点**镜像成朝左�?
        ///
    /// 为什么是 <c>NCreature.Body</c>�?
        ///   · <c>NCreature.Body => Visuals.GetCurrentBody()</c>，�?<c>NCreatureVisuals._Ready</c> 里是
    ///     <c>_body = GetNode&lt;Node2D&gt;("%Visuals")</c>（用 GetNode，缺节点会抛�?&gt; **一定有**�?
        ///   · 奥斯提的 <c>_body</c> 就是 SpineSprite（`@SpineMesh2D@*` 的父节点），所以它"不叫 Body"不代表没有；
    ///   · 游戏自己的朝向权�?<c>SurroundedPower.FaceDirection</c> 翻的正是�?
        ///     （对 Owner 与全�?Pets �?<c>GetCreatureNode(c).Body</c>，再 <c>Scale *= (-1,1)</c>）；
    ///   · 血量补�?<c>OstyScaleToSize</c> 写的是父�?<c>Visuals.Scale</c>，碰不到 Body �?不会被抹掉�?
        ///
    /// 幂等：已经是负的就不再翻（避免每回合翻回来）�?
        /// </summary>
    /// <summary>把立绘骨架写回朝右（不镜像）。斗蛐蛐里 Boss 站在玩家侧时用。</summary>
    internal static void UnmirrorBody(NCreature oc)
    {
        try
        {
            if (oc.Visuals == null) return;
            var body = oc.Body;
            if (body == null || !GodotObject.IsInstanceValid(body)) return;
            var s = body.Scale;
            if (s.X < 0f) body.Scale = new Vector2(-s.X, s.Y);
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 奥斯提 Body 取消镜像失败: " + e.Message);
        }
    }
    /// <summary>
    /// 朝向：**看敌人站在哪边**（真实几何），而不是看立绘的镜像符号。
    /// 斗蛐蛐会把 Boss 塞进玩家侧（左边），这时 Visuals.Scale.X 的正负和"敌人方向"没有必然关系，
    /// 只看符号就会出现"左边那只的奥斯提朝向反了"。
    /// </summary>
    /// <summary>
    /// 取某只生物**真正属于自己**的节点：先走原版 <c>GetCreatureNode</c>，并校验 <c>Entity</c> 是否同一只，
    /// 对不上（斗蛐蛐把红队战士挂在玩家侧时会发生）就全树搜一遍。
    /// </summary>
    internal static NCreature? NodeFor(Creature? creature)
    {
        if (creature == null) return null;
        NCreature? direct = null;
        try { direct = NCombatRoom.Instance?.GetCreatureNode(creature); } catch { /* 忽略 */ }

        if (direct != null && GodotObject.IsInstanceValid(direct))
        {
            try { if (ReferenceEquals(direct.Entity, creature)) return direct; } catch { /* 忽略 */ }
        }

        try { return FindNodeInTree(NCombatRoom.Instance, creature) ?? direct; } catch { return direct; }
    }

    private static NCreature? FindNodeInTree(Node? node, Creature creature)
    {
        if (node == null) return null;
        foreach (var child in node.GetChildren())
        {
            if (child is not Node n || !GodotObject.IsInstanceValid(n)) continue;
            if (n is NCreature nc)
            {
                try { if (ReferenceEquals(nc.Entity, creature)) return nc; } catch { /* 忽略 */ }
            }
            var hit = FindNodeInTree(n, creature);
            if (hit != null) return hit;
        }
        return null;
    }

    internal static float FacingDir(CharBossMonster boss, Node bossNode)
    {
        try
        {
            var self = boss.Creature;
            if (self == null) return 1f;

            // 斗蛐蛐（和原版战斗一样）：**玩家侧在左、敌人侧在右**。
            // 原来按「对手节点的平均 X」算，斗蛐蛐里红队战士挂在玩家侧时 GetCreatureNode 会返回
            // 真人占位节点，几何均值被拉偏 ⇒ 左边那只的奥斯提朝反（用户 2026-11-08 的反馈）。
            // 阵营是确定的，直接按阵营定朝向，几何只用来打日志。
            float bySide = self.Side == MegaCrit.Sts2.Core.Combat.CombatSide.Player ? 1f : -1f;

            try
            {
                var combat = self.CombatState;
                if (combat != null && bossNode is Node2D b2 && GodotObject.IsInstanceValid(bossNode))
                {
                    float sx = b2.GlobalPosition.X;
                    float ox = 0f; int n = 0;
                    foreach (var c in combat.GetOpponentsOf(self))
                    {
                        var nd = NodeFor(c);
                        if (nd != null && GodotObject.IsInstanceValid(nd)) { ox += nd.GlobalPosition.X; n++; }
                    }
                    if (n > 0 && _facingLogged.Add(boss.Creature?.CombatId ?? 0UL))
                    {
                        Logger.Log("[CharBoss] 朝向判定 self=" + self.Side + " selfX=" + sx
                                   + " oppX=" + (ox / n) + " ⇒ side=" + bySide
                                   + " geom=" + (ox / n < sx ? -1 : 1));
                    }
                }
            }
            catch { /* 只打日志用 */ }

            return bySide;
        }
        catch { /* 回退到立绘符号 */ }
        try { if (bossNode is NCreature nc && nc.Visuals != null) return nc.Visuals.Scale.X < 0f ? -1f : 1f; } catch { }
        return 1f;
    }
    private static readonly HashSet<ulong> _facingLogged = new();

    /// <summary>
    /// 让奥斯提朝 <paramref name="dir"/>（-1 = 朝左，+1 = 朝右）。
    ///
    /// 关键：**原版对敌方侧生物会把 <c>Visuals.Scale.X</c> 翻成负数**，我们再翻 <c>Body</c>
    /// 就等于翻了两次 ⇒ 净朝向又变回去。用户 2026-11-08 说的「怪物侧的宠物朝向还是反的」就是这回事。
    /// 所以这里按**净朝向**算：<c>Visuals 符号 × Body 符号</c> 要等于 dir。
    /// </summary>
    internal static void FaceToward(NCreature oc, float dir)
    {
        try
        {
            if (oc.Visuals == null) return;
            var body = oc.Body;
            if (body == null || !GodotObject.IsInstanceValid(body)) return;

            float visualSign = oc.Visuals.Scale.X < 0f ? -1f : 1f;
            float want = dir * visualSign;          // = dir / visualSign
            if (CharacterBosses.Core.BossConfig.CricketOstyFlip) want = -want;   // 手动纠偏开关
            float cur = body.Scale.X < 0f ? -1f : 1f;
            if (Math.Abs(cur - want) < 0.001f)
            {
                if (!_facingLogged.Contains(oc.GetInstanceId()))
                {
                    _facingLogged.Add(oc.GetInstanceId());
                    Logger.Log("[CharBoss] 奥斯提朝向(未改): dir=" + dir + " visual=" + visualSign
                               + " body=" + cur + " (净朝向=" + (cur * visualSign) + ")");
                }
                return;
            }

            body.Scale = new Vector2(want * Math.Abs(body.Scale.X), body.Scale.Y);
            Logger.Log("[CharBoss] 奥斯提朝向：dir=" + dir + " visual=" + visualSign
                       + " body=" + want + " (净朝向=" + (want * visualSign) + ")");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 奥斯提朝向设置失败: " + e.Message);
        }
    }
    internal static void MirrorBody(NCreature oc)
    {
        try
        {
            if (oc.Visuals == null) return;
            var body = oc.Body;
            if (body == null || !GodotObject.IsInstanceValid(body))
            {
                Logger.Log("[CharBoss] 奥斯提没有 Body 节点（%Visuals 缺失），无法镜像朝向");
                return;
            }

            var s = body.Scale;
            if (s.X > 0f)
            {
                body.Scale = new Vector2(-s.X, s.Y);
                Logger.Log($"[CharBoss] 奥斯提立绘已镜像朝左 via Body: " +
                           $"name={body.Name} class={body.GetClass()} scale={body.Scale}");
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 奥斯提 Body 镜像失败: " + e.Message);
        }
    }

    private static float _lastBodyScaleX = float.NaN;
    private static bool _bodyDumped;

    /// <summary>
    /// Body 探针：第一次打印骨架节点本身（名字/�?子节点），之后只�?<c>Scale.X</c> 变化时打一行�?
        /// 用途：①证�?Body 存在且是 SpineSprite；②若负值被人写回正数，日志会直接暴露�?
        /// </summary>
    private static void ProbeBody(NCreature oc)
    {
        try
        {
            if (oc.Visuals == null) return;
            var body = oc.Body;
            if (body == null || !GodotObject.IsInstanceValid(body)) return;

            if (!_bodyDumped)
            {
                _bodyDumped = true;
                var kids = new List<string>();
                foreach (var child in body.GetChildren())
                    kids.Add(GodotObject.IsInstanceValid(child) ? $"{child.Name}({child.GetType().Name})" : "?");
                Logger.Log($"[CharBoss] 奥斯提 Body 探针: name={body.Name} class={body.GetClass()} " +
                           $"type={body.GetType().Name} scale={body.Scale} Visuals.DefaultScale={oc.Visuals.DefaultScale:0.000} " +
                           $"子节点=[{string.Join(", ", kids)}]");
            }

            float x = body.Scale.X;
            if (!float.IsNaN(_lastBodyScaleX) && Math.Abs(x - _lastBodyScaleX) > 0.001f)
                Logger.Log($"[CharBoss] 奥斯提 Body.Scale.X 被改动: {_lastBodyScaleX:0.000} → {x:0.000}（变正=有人写回朝右）");
            _lastBodyScaleX = x;
        }
        catch { /* 忽略 */ }
    }

    /// <summary>持续把奥斯提�?Visuals 掰成朝左（原版会一直写回朝右）�?/summary>
    private static async Task FacingLoop(CharBossMonster boss)
    {
        try
        {
            while (true)
            {
                await Cmd.CustomScaledWait(0.08f, 0.12f);
                if (boss.Creature == null || !boss.Creature.IsAlive) break;
                if (boss.CombatState?.IsLiveCombat() != true) break;

                var room = NCombatRoom.Instance;
                var shadow = boss.Shadow;
                var osty = shadow?.Osty;
                if (room == null || osty == null || !osty.IsAlive) continue;

                var node = room.GetCreatureNode(osty) as NCreature;
                if (node?.Visuals == null || !GodotObject.IsInstanceValid(node.Visuals)) continue;

                var vs = node.Visuals.Scale;
                if (vs.X > 0f) node.Visuals.Scale = new Vector2(-vs.X, vs.Y);
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 朝向纠正循环结束: " + e.Message);
        }
        finally
        {
            _facingLoopRunning = false;
        }
    }
    private static bool _dumped;

    /// <summary>一次性把奥斯提节点树打出来（找血�?状态栏真正挂在谁身上）�?/summary>
    private static void DumpTree(Node node, int depth)
    {
        if (depth > 3) return;
        try
        {
            string extra = node is CanvasItem ci
                ? $" visible={ci.Visible} alpha={ci.Modulate.A:0.00}"
                : string.Empty;
            Logger.Log($"[CharBoss] Osty节点{new string(' ', depth * 2)}{node.Name} ({node.GetType().Name}){extra}");
            foreach (var child in node.GetChildren()) DumpTree(child, depth + 1);
        }
        catch { /* 忽略 */ }
    }
    internal static void ForceVisible(Node ostyNode)
    {
        try
        {
            if (ostyNode is Control ctl)
            {
                ctl.Visible = true;
                if (ctl.Modulate.A < 0.99f) ctl.Modulate = new Color(1f, 1f, 1f, 1f);
            }

            // 日志实测：整�?"HealthBar (NCreatureStateDisplay)" �?visible=False / alpha=0�?
                // 而里面的 NHealthBar / NPowerContainer / NameplateContainer 都是好的 —�?
                // 所以要掰的�?*外层这个状态显示容�?*（非本地玩家的随从会被它整块关掉）�?         
    foreach (var child in ostyNode.GetChildren())
            {
                if (child is not CanvasItem ci || !GodotObject.IsInstanceValid(ci)) continue;
                bool isStateDisplay = child.GetType().Name.Contains("StateDisplay")
                                      || child.Name.ToString() == "HealthBar";
                if (!isStateDisplay) continue;
                ci.Visible = true;
                var m = ci.Modulate;
                ci.Modulate = new Color(m.R, m.G, m.B, 1f);

                // �?名字�?*不要**再强制拉满：用户要求「名字不该常显，鼠标移上去才显示�?
                    //（所有敌人都是这�?—�?原版 ShowNameplate/HideNameplate 就是补间它的 alpha）�?
                    // 以前这里强行 NameplateContainer.Visible=true / alpha=1，于是名字一直挂着�?
                    // 现在只保证外层状态容器可见（血条要常显），名字牌交回原�?hover 逻辑�?
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 奥斯提血条强制显示失败: " + e.Message);
        }
    }

    /// <summary>把整棵子树的 Visible 打开、alpha 拉满（只用于状态显示容器，不碰意图/选择框）�?/summary>
    private static void GrantAlpha(Node node)
    {
        try
        {
            // 格挡条是「跟主人共用」的：原版只画蓝色覆盖、不显示数值�?
                // 整棵子树点亮会把 BlockContainer 里的数字也放出来（用户实测），所以跳过它�?
            if (node.Name.ToString() == "BlockContainer") return;

            if (node is CanvasItem ci)
            {
                ci.Visible = true;
                var m = ci.Modulate;
                ci.Modulate = new Color(m.R, m.G, m.B, 1f);
            }
            foreach (var child in node.GetChildren()) GrantAlpha(child);
        }
        catch { /* 忽略 */ }
    }
    private static readonly FieldInfo? PetsField =
        typeof(PlayerCombatState).GetField("_pets", BindingFlags.Instance | BindingFlags.NonPublic);

    /// <summary>
    /// 任何一只奥斯提（她那边的、或**玩家自己**那只）�?
        /// 用途：中毒免疫 —�?用户要求「奥斯提只负责吸收未格挡的攻击伤害」，中毒只能落在主人身上�?
        /// </summary>
    public static bool IsAnyOsty(Creature? creature)
    {
        try { return creature?.Monster is MegaCrit.Sts2.Core.Models.Monsters.Osty; }
        catch { return false; }
    }

    /// <summary>
    /// 这只生物是不是「角�?Boss 的奥斯提」（不是玩家自己那只）�?
        ///
    /// 判定：怪物�?<c>Osty</c>，且它的 <c>PetOwner</c> �?*敌方�?*（玩家侧那只�?owner 是真人玩家）�?
        /// </summary>
    public static bool IsBossOsty(Creature? creature)
    {
        try
        {
            if (creature == null) return false;
            if (creature.Monster is not MegaCrit.Sts2.Core.Models.Monsters.Osty) return false;

            var owner = creature.PetOwner ?? creature.Player;
            if (owner?.Creature != null)
                return owner.Creature.Side == MegaCrit.Sts2.Core.Combat.CombatSide.Enemy;

            // 归属拿不到（日志实测：`PetOwner` 和 `Player` **都**可能是 null）→ 退回**阵营**判据。
            // 这个 mod 里敌人侧只可能是我们的角色 Boss，所以「敌人侧的奥斯提」一定是她的。
            // 以前这里直接 return false ⇒ 那只奥斯提留在 `HittableEnemies` 里，
            // 被崩坠「转嫁疼痛」（`Collector.FeelMyPainPower` 随机取一只 `HittableEnemies`）选中并掉血
            //（测试玩家 2026-10-23 实测，含已经死掉的那只）。
            return creature.Side == MegaCrit.Sts2.Core.Combat.CombatSide.Enemy;
        }
        catch { return false; }
    }

    /// <summary>每个 Boss 回合开始（召唤之后）与界面刷新时调一次�?/summary>
    public static void Update(CharBossMonster boss, PlayerChoiceContext? context = null)
    {
        try
        {
            // 只有亡灵契约师（缚魂命匣 + 奥斯提）需要这套收尾�?
                // 别的角色 Boss 绝不能走到这�?—�?用户实测：静默猎手战斗里冒出了一只奥斯提
            //（兜底召唤那段对任何 Boss 都会 Summon 一只）�?
            if (boss.Def.Key != "NECROBINDER_BOSS") return;

            var shadow = boss.Shadow;
            var pcs = shadow?.PlayerCombatState;
            if (shadow == null || pcs == null) return;

            DropDeadPets(pcs);
            // 兜底：原版遗物召出来的那只死后**可能已经被原版从 Pets 里摘走**，
            // DropDeadPets 只见得到还在 Pets 里的那些 ⇒ 尸体节点留在场上，
            // 下一回合又召一只新的 ⇒ 画面上一只活的 + 一具尸体（用户实测）。
            // 这里直接扫战斗房，把「已死 + 是奥斯提 + 在她这一侧」的节点一律收掉。
            SweepDeadOstyNodes(boss);

            // `shadow.Osty` 走的是 `PlayerCombatState.GetPet<Osty>()`（读 pcs.Pets）。
            // 实测：她的 Pets 一直是 0（原版遗物召唤出来的奥斯提没进影子玩家的 Pets），
            // 但画面上确实有一只活的 ⇒ 不能只信 Pets，按**节点**再找一次
            //（用户实测：只信 Pets 时每回合都会走「没有奥斯提」的兜底分支 → 每回合右边多一只）。
            var osty = SafeOsty(shadow);
            if (osty == null || !osty.IsAlive) osty = LiveOstyCreature(boss);
            Logger.Log($"[CharBoss] BossOsty.Update: turn={boss.TurnNumber} " +
                       $"shadowOsty={(osty == null ? "null" : osty.IsAlive ? "alive" : "dead")} " +
                       $"pets={SafePetCount(pcs)} 明细={DumpPets(shadow)}");
            if (osty == null || !osty.IsAlive)
            {
                // 多人换牌 A2（吸引仇恨 → 骸骨军团）：这张牌打出之后她的奥斯提由**牌**给，
                // 死了就死了 —— 用户要求「必须再次打出该牌才复活」，所以这里不再每回合自动补建。
                // 单人（或没换牌时的第二幕）走原样：缺奥斯提就自己补一只。
                // ⚠ 多人换牌时**只禁止「死了以后自动复活」**，不禁第一次召唤 ——
                //   否则她第 1 回合（剧本里是戳击这种奥斯提牌）等于空手打，且要等到第 2 回合
                //   「骸骨军团」才有一只（用户实测：三层根除骨全程没有奥斯提）。
                if (LegionSwapActive(boss) && HasHadOsty(boss))
                {
                    Logger.Log("[CharBoss] 骸骨军团（多人换牌）：她的奥斯提死过 → 不自动补建，等这张牌再打出");
                    return;
                }

                // 实测（日志）：原版缚魂命匣对「敌方阵营的影子玩家」**不会**召唤（pets=0、场上也没有），
                // 所以缺奥斯提时必须我们自己召一只 —— 但**必须先按节点确认场上真的没有活的**
                // （上面那次 LiveOstyCreature 就是干这个的），否则每回合都会多出一只
                //（用户实测：每回合开始右边多一只手，而不是给左边那只加血）。
                if (_lastSummonTurn != boss.TurnNumber)
                {
                    _lastSummonTurn = boss.TurnNumber;
                    Logger.Log("[CharBoss] 场上没有活着的奥斯提 → 自己召唤一只（每回合最多一次）");
                    TaskHelper.RunSafely(SummonAndRegister(boss, shadow, context));
                }
                return;
            }
            // （原来的"兜底召唤"分支已删除：它就是"每回合右边多一只手"的来源，
            //   见上面对缚魂命匣的说明。缺奥斯提只等原版召唤。）

            // �?先确认这只奥斯提**确实属于�?*（影�?Player）�?
                // 如果是玩家的奥斯提（例如两个 PlayerCombatState 串了），绝对不能摆到 Boss 身上�?
                bool hers = true;
            try { hers = osty.Player == null || ReferenceEquals(osty.Player, shadow); }
            catch { /* 忽略 */ }
            Logger.Log($"[CharBoss] 奥斯提诊断: id={SafeId(osty)} alive={osty.IsAlive} " +
                        $"ownerNetId={SafeOwnerNet(osty)} shadowNetId={SafeNet(shadow)} 属于她={hers}");
            if (!hers) return;

            MarkMinion(boss, osty, context);
            NoteHadOsty(boss);       // 她有过奥斯提了（之后死了就不再自动补，见上）
            MirrorAndPlace(boss, osty);

            // 缚魂命匣的「召唤 1」：原版每回合跑一次，活着就 **给奥斯提加 1 血**（GainMaxHp）。
            // 它不替影子玩家触发，所以我们自己每回合补一次 —— 前提是上面已经把它登记进 Pets，
            // 否则这一下会"造一只新的"（那就是之前右边多一只手的原因）。
            if (_lastSummonTurn != boss.TurnNumber)
            {
                _lastSummonTurn = boss.TurnNumber;
                TaskHelper.RunSafely(SummonAndRegister(boss, shadow, context));
            }

            // 原版战斗房会在我们之后按「随从↔主人」重新排布一次（会先出现在她右边再瞬移）
            // 延迟再摆一次把它压住。
            TaskHelper.RunSafely(PlaceAfterDelay(boss, osty));
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 奥斯提收尾失败: " + e.Message);
        }
    }

    /// <summary>把死掉的宠物从 Pets 里摘掉（否则 GetPet 永远拿到尸体）。</summary>
    private static void DropDeadPets(PlayerCombatState pcs)
    {
        try
        {
            if (PetsField?.GetValue(pcs) is not List<Creature> pets || pets.Count == 0) return;

            int removed = 0;
            for (int i = pets.Count - 1; i >= 0; i--)
            {
                var pet = pets[i];
                bool dead = pet == null || !pet.IsAlive;
                bool osty = pet != null && SafeMonsterEntry(pet).Contains("OSTY", StringComparison.OrdinalIgnoreCase);
                if (dead && osty)
                {
                    HideNode(pet);            // 尸体节点也要收掉，否则每死一次就多一具"尸骨"（用户实测堆积如山）
                    DetachFromCombat(pet);    // 更关键：死掉的生物仍留在战斗列表 → 每个钩子广播都要遍历它（回合越久越卡）
                    pets.RemoveAt(i);
                    removed++;
                }
            }
            if (removed > 0) Logger.Log($"[CharBoss] 清掉 {removed} 只已死的奥斯提（原版 GetPet 会一直拿到尸体）");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 清理死宠物失败: " + e.Message);
        }
    }

    /// <summary>
    /// 取她那只**活着�?*奥斯提�?
        ///
    /// �?只用 <c>shadow.Osty</c>（影子玩家自己的 Pets），并且必须再确�?
        /// <c>PetOwner</c> 就是她本�?—�?早先为了"找不到就扫战�?写过一个兜底，
    /// 结果�?*玩家的奥斯提**（宠物单位的 Player 常为 null）当成她的，标记爪牙并拖到她那边�?
        /// 原版又拖回来 �?出现"抢人、来回拉�?（用户实测）。宁可不摆，也不许抢�?
        /// </summary>
    private static Creature? SafeOsty(Player shadow)
    {
        try
        {
            // ① 先在**影子玩家自己的 Pets** 里找活的奥斯提 —— 那是我们登记进去的，本来就是她的。
            //    为什么必须先看这里：原版 `PlayerCmd.AddPet` 造出来的随从 `PetOwner` 不一定是影子玩家
            //    （日志实测：pets=1 但 `shadow.Osty` 返回 null ⇒ 每回合都判成"没有奥斯提"→ 再召一只
            //     ⇒ 每回合右边多一只手）。
            var pcs = shadow.PlayerCombatState;
            if (pcs != null && PetsField?.GetValue(pcs) is List<Creature> pets)
            {
                foreach (var pet in pets)
                {
                    if (pet == null || !pet.IsAlive) continue;
                    if (SafeMonsterEntry(pet).Contains("OSTY", StringComparison.OrdinalIgnoreCase)) return pet;
                }
            }

            // ② 再退回原版属性
            var direct = shadow.Osty;
            if (direct == null || !direct.IsAlive) return null;
            if (!IsHers(direct, shadow)) return null;
            return direct;
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 取奥斯提失败: " + e.Message);
            return null;
        }
    }

    /// <summary>这只宠物是不是影子玩家的（宠物可�?Player �?null，所以优先看 PetOwner）�?/summary>
    private static bool IsHers(Creature creature, Player shadow)
    {
        try
        {
            var owner = creature.PetOwner;
            if (owner != null) return ReferenceEquals(owner, shadow);
        }
        catch { /* 忽略 */ }

        try { return creature.Player != null && ReferenceEquals(creature.Player, shadow); }
        catch { return false; }
    }
    private static int _lastSummonTurn = -1;

    /// <summary>召唤�?*补登�?*进影子玩家的 Pets（否则下回合 shadow.Osty 仍是 null，会重复召唤）�?/summary>
    private static async Task SummonAndRegister(CharBossMonster boss, Player shadow, PlayerChoiceContext? context,
                                                decimal? forcedAmount = null, AbstractModel? forcedSource = null)
    {
        try
        {
            // ★ v0.2.345：遗物从「缚魂命匣」（Summon=1）换成了「无界命匣」
            //   （`PhylacteryUnbound`：战斗开始召唤 StartOfCombat=5、**每回合开始召唤 StartOfTurn=2**）
            //   ⇒ 召唤量统一问 <see cref="TurnStartSummonOf"/>，两种遗物都认得。
            var (perTurn, source) = TurnStartSummonOf(shadow);
            // ★ v0.2.347（用户 2026-11-03）：无界命匣的「战斗开始召唤 5」**不能在战斗开始那一刻做**
            //   —— 那时 `CombatManager.IsInProgress` 还是 false，`PlayerCmd.AddPet` 会抛错
            //   （Telemetry 里那条「PHYLACTERY_UNBOUND：召唤时机不在战斗进行中 ⇒ AddPet 抛错」就是它）。
            //   所以把这一笔挪到**她第 1 个回合开始**，和每回合的 2 一起给（总数与卡面一致：5 + 2 = 7）。
            decimal amount = forcedAmount ?? perTurn;
            if (forcedSource != null) source = forcedSource;
            if (amount <= 0) amount = 1m;

            // ★ v0.2.467（玩家上报 2026-11-06 act1 灾厄骨：`奥斯提召唤异常: Object reference not set`）：
            //   反编译 `OstyCmd.Summon` 后看得很清楚，它一进来就是
            //     `ICombatState combatState = summoner.Creature.CombatState;`
            //   ⇒ **影子玩家的 `Creature` 为 null 时直接 NRE**；后面还会
            //   `summoner.PlayerCombatState.AddPetInternal(...)` 和 `CombatManager.Instance.History.Summoned(...)`，
            //   战斗已经结束时这两处同样会 NRE。
            //   这些都是「环境前提不成立」（她已经离场 / 战斗结束 / 还没入场），不是我们算错了 ⇒
            //   先自检，条件不满足就**明确跳过并说明原因**，别再抛一句没信息量的 NRE。
            bool ready;
            string why;
            try
            {
                if (MegaCrit.Sts2.Core.Combat.CombatManager.Instance == null
                    || !MegaCrit.Sts2.Core.Combat.CombatManager.Instance.IsInProgress)
                {
                    ready = false; why = "战斗不在进行中";
                }
                else if (shadow.Creature == null)
                {
                    ready = false; why = "影子玩家没有生物（已离场 / 尚未入场）";
                }
                else if (shadow.Creature.CombatState == null)
                {
                    ready = false; why = "影子玩家的生物没有 CombatState";
                }
                else if (shadow.PlayerCombatState == null)
                {
                    ready = false; why = "影子玩家没有 PlayerCombatState";
                }
                else { ready = true; why = ""; }
            }
            catch (Exception e) { ready = false; why = "自检本身抛异常: " + e.Message; }

            if (!ready)
            {
                Logger.Log($"[CharBoss] 奥斯提召唤跳过（第 {boss.TurnNumber} 回合）：{why} —— " +
                           "原版 OstyCmd.Summon 要求 summoner.Creature + CombatState + 战斗进行中，缺一样就 NRE");
                return;
            }

            var ctx = context ??
                new BlockingPlayerChoiceContext();
            var result = await OstyCmd.Summon(ctx, shadow, amount, source);
            var osty = result?.Creature;
            if (osty == null)
            {
                Logger.Log($"[CharBoss] 奥斯提召唤没有返回生物（amount={amount} source={SafeEntryOf(source)}，" +
                           $"isAlive={SafeOsty(shadow) != null}");
                return;
            }
            Logger.Log($"[CharBoss] 奥斯提召唤成功：amount={amount} source={SafeEntryOf(source)}" +
                       $"（第 {boss.TurnNumber} 回合；无界命匣每回合 {perTurn}{(amount > perTurn ? " + 首次的战斗开始部分" : string.Empty)}）");

            var pcs = shadow.PlayerCombatState;
            if (pcs != null)
            {
                // 关键：用**原版 API** 登记进 Pets（不是反射改 _pets）。
                // 原因（反编译 OstyCmd.Summon）：它先看 `summoner.IsOstyAlive`（＝读 Pets 里的 Osty）——
                //   · 活着 → `GainMaxHp(奥斯提, amount)`，也就是「召唤 1」＝每回合加 1 血；
                //   · 不活着 → 复活旧的 / `PlayerCmd.AddPet<Osty>` **造一只新的**。
                // 影子玩家的 Pets 一直是空的 ⇒ 每次调用都走"造新的" ⇒ 每回合右边多一只手（用户实测）。
                try
                {
                    if (!ReferenceEquals(shadow.Osty, osty))
                    {
                        pcs.AddPetInternal(osty);
                        Logger.Log("[CharBoss] 奥斯提已登记进 Pets（之后 Summon 会走加血，而不是再造一只）");
                    }
                }
                catch (Exception e)
                {
                    Logger.Log("[CharBoss] 奥斯提登记进 Pets 失败: " + e.Message);
                }
            }

            MarkMinion(boss, osty, context);
            MirrorAndPlace(boss, osty);

            // 原版战斗房会在我们之后按「随从↔主人」重新排布一次（会先出现在她右边再瞬移）
            // 延迟再摆一次把它压住。
            TaskHelper.RunSafely(PlaceAfterDelay(boss, osty));
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 奥斯提召唤异常: " + e.Message + FramesOf(e));
        }
    }
    /// <summary>
    /// 异常的前几帧（`| at X.Y()` 用 `|` 连成一行）。
    /// 为什么需要：`奥斯提召唤异常: Object reference not set to an instance of an object.`
    /// 这句话本身**不含任何位置信息**，收到玩家上报也只能干瞪眼（2026-11-06 那条就是这样）。
    /// </summary>
    private static string FramesOf(Exception e)
    {
        try
        {
            string stack = e.StackTrace ?? "";
            if (stack.Length == 0) return "";
            var frames = stack.Split('\n');
            var sb = new System.Text.StringBuilder();
            int used = 0;
            foreach (var raw in frames)
            {
                string t = raw.Trim();
                if (t.Length == 0) continue;
                sb.Append(" | ").Append(t);
                if (++used >= 4) break;
            }
            return sb.ToString();
        }
        catch { return ""; }
    }
    /// <summary>
    /// 「**战斗开始**」那一笔召唤（无界命匣 `PhylacteryUnbound.StartOfCombat` = 5）—— 用户 2026-11-04：
    /// 「改一下无界命匣，让她在战斗开始时就召唤 5，然后再每回合召唤 2
    ///  （把原版第一回合召唤 5 的拦了，别像王室猛毒那样子重复）」。
    ///
    /// 历史：v0.2.347 把这 5 挪去「她第 1 个回合开始」跟每回合的 2 一起给 —— 因为**战斗开始那一刻**
    /// `CombatManager.IsInProgress` 还是 false，`PlayerCmd.AddPet` 会抛错（Telemetry 里的已知噪音）。
    /// 现在改成：由**每帧 tick** 在「战斗真的开始了」(`IsInProgress &amp;&amp; !IsStarting`) 之后**立刻**补一次，
    /// 于是 ①玩家一进战斗就能看到那只手（而不是等到她第一个回合）；
    /// ②`AddPet` 也不会在战斗未开始时被调用；
    /// ③每回合那笔回到纯 `StartOfTurn`(2)，不会和这 5 重复。
    ///
    /// ⚠ 原版 `BeforeCombatStart` 那条（同样是 5）由 `Patches/OstyNoCombatStartSummonPatch` 对她**已经拦掉**，
    ///   原版每回合 `AfterSideTurnStart` 那条也在 `BossHookBridge` 的跳过名单里 ⇒ 不会双份。
    /// ⚠ 只对有「无界命匣」的 Boss 生效；缚魂命匣（`CombatStartSummonOf` 返回 0）什么都不做。
    /// </summary>
    internal static void TryCombatStartSummon(CharBossMonster boss)
    {
        try
        {
            if (boss == null || boss.CombatStartSummonDone) return;
            var shadow = boss.Shadow;
            if (shadow == null || boss.Creature == null) return;

            var (amount, source) = CombatStartSummonOf(shadow);
            if (amount <= 0)
            {
                boss.CombatStartSummonDone = true;      // 没有无界命匣：以后不用再查
                return;
            }

            var cm = MegaCrit.Sts2.Core.Combat.CombatManager.Instance;
            if (cm == null || !cm.IsInProgress || cm.IsStarting) return;   // 还没真正开始：下一帧再试

            boss.CombatStartSummonDone = true;
            Logger.Log($"[CharBoss] 无界命匣：战斗开始召唤 {amount}（原版 BeforeCombatStart 那条对她已拦掉，" +
                       "每回合另有 2 —— 不会再重复）");
            TaskHelper.RunSafely(SummonAndRegister(boss, shadow, null, amount, source));
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 无界命匣战斗开始召唤失败: " + e.Message);
        }
    }

    private static async Task PlaceAfterDelay(CharBossMonster boss, Creature osty)
    {
        try
        {
            await Cmd.CustomScaledWait(0.05f, 0.12f);
            if (osty.IsAlive) MirrorAndPlace(boss, osty);
        }
        catch { /* 忽略 */ }
    }
    /// <summary>把死掉宠物的节点收起来（只隐藏，不销毁，避免动到原版的节点管理）�?/summary>
    private static readonly FieldInfo? AlliesField =
        typeof(CombatState).GetField("_allies", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? EnemiesField =
        typeof(CombatState).GetField("_enemies", BindingFlags.Instance | BindingFlags.NonPublic);

    /// <summary>把死掉的奥斯提从战斗生物列表里摘掉（只影响遍历，不影响存档）�?/summary>
    private static void DetachFromCombat(Creature? pet)
    {
        try
        {
            if (pet?.CombatState == null) return;
            foreach (var field in new[] { AlliesField, EnemiesField })
            {
                if (field?.GetValue(pet.CombatState) is not IList list) continue;
                if (list.Contains(pet)) { list.Remove(pet); Logger.Log("[CharBoss] 已把死掉的奥斯提移出战斗列表"); }
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 移出战斗列表失败: " + e.Message);
        }
    }
    private static void HideNode(Creature? pet)
    {
        try
        {
            if (pet == null) return;
            var node = NCombatRoom.Instance?.GetCreatureNode(pet);
            if (node == null || !GodotObject.IsInstanceValid(node)) return;
            HideNodeDeep(node);
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 隐藏奥斯提尸体失败: " + e.Message);
        }
    }

    /// <summary>把节点整棵子树收起来（尸体上还挂着 Spine/血条等子节点，只关自己可能还剩残影）。</summary>
    private static void HideNodeDeep(Node node)
    {
        try
        {
            if (!GodotObject.IsInstanceValid(node)) return;
            if (node is CanvasItem ci)
            {
                ci.Visible = false;
                ci.Modulate = new Color(1f, 1f, 1f, 0f);
            }
            foreach (var child in node.GetChildren())
            {
                if (child is Node n) HideNodeDeep(n);
            }
        }
        catch { /* 单个节点失败不影响其它 */ }
    }

    /// <summary>
    /// 扫一遍战斗房，把「已经死了的、属于她的」奥斯提节点收起来。
    ///
    /// 为什么需要（用户实测：「手死了之后复活又多了一只，原来那只还躺着」）：
    /// 原版遗物召出来的那只死后，**可能已经被原版自己从 Pets 里摘走** ——
    /// <see cref="DropDeadPets"/> 只遍历 Pets，所以根本没见过它，也就没人去隐藏它的节点；
    /// 而下一个回合我们又召一只新的 ⇒ 一只活的 + 一具尸体同时在场上。
    /// 判据用「阵营 = Boss 那一侧」来区分：真人玩家自己的奥斯提在 Player 侧，绝不会被误收。
    /// </summary>
    private static void SweepDeadOstyNodes(CharBossMonster boss)
    {
        try
        {
            var room = NCombatRoom.Instance;
            var shadow = boss.Shadow;
            var side = boss.Creature?.Side;
            if (room == null || side == null) return;

            int hidden = 0;
            foreach (var nc in CollectCreatureNodes(room))
            {
                Creature? entity;
                try { entity = nc.Entity; } catch { continue; }
                if (entity == null || entity.IsAlive) continue;
                if (!SafeMonsterEntry(entity).Contains("OSTY", StringComparison.OrdinalIgnoreCase)) continue;
                if (entity.Side != side) continue;                       // 玩家侧的奥斯提不归我们管
                if (entity.PetOwner != null)                             // 有主的：只收属于影子玩家的
                {
                    bool hers = shadow != null && ReferenceEquals(entity.PetOwner, shadow);
                    if (!hers) continue;
                }
                HideNodeDeep(nc);
                hidden++;
            }
            if (hidden > 0) Logger.Log($"[CharBoss] 收起 {hidden} 只残留的死奥斯提节点");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 扫死奥斯提节点失败: " + e.Message);
        }
    }

    /// <summary>递归收集某个节点下所有的 <see cref="NCreature"/>。</summary>
    private static List<NCreature> CollectCreatureNodes(Node root)
    {
        var found = new List<NCreature>();
        void Walk(Node node)
        {
            foreach (var child in node.GetChildren())
            {
                if (child is not Node n || !GodotObject.IsInstanceValid(n)) continue;
                if (n is NCreature nc) found.Add(nc);
                Walk(n);
            }
        }
        try { Walk(root); } catch { /* 忽略 */ }
        return found;
    }
    /// <summary>她的这一侧（Boss 所在阵营）是否已经有活着的奥斯提（不�?Pets 登记）�?/summary>
    private static bool HasLiveOstyOnHerSide(CharBossMonster boss)
    {
        try
        {
            var combat = boss.CombatState;
            var side = boss.Creature?.Side;
            if (combat == null) return false;
            foreach (var list in new[] { combat.Enemies, combat.Allies })
            {
                if (list == null) continue;
                foreach (var creature in list)
                {
                    if (creature == null || !creature.IsAlive) continue;
                    if (!SafeMonsterEntry(creature).Contains("OSTY", StringComparison.OrdinalIgnoreCase)) continue;
                    try { if (side != null && creature.Side != side) continue; } catch { /* 忽略 */ }
                    return true;
                }
            }
        }
        catch (Exception e) { Logger.Log("[CharBoss] 检查活奥斯提失败: " + e.Message); }
        return false;
    }

    /// <summary>召唤一次（数量取缚魂命匣的 Summon 变量，读不到�?1）�?/summary>
    private static void SummonOnce(CharBossMonster boss, Player shadow, PlayerChoiceContext? context)
    {
        try
        {
            // 同上：召唤量问 <see cref="TurnStartSummonOf"/>（缚魂命匣 Summon=1 / 无界命匣 StartOfTurn=2）
            var (amount, source) = TurnStartSummonOf(shadow);

            var ctx = context ??
                new BlockingPlayerChoiceContext();
            TaskHelper.RunSafely(OstyCmd.Summon(ctx, shadow, amount, source));
            Logger.Log($"[CharBoss] 奥斯提召唤一次：amount={amount} source={SafeEntryOf(source)}");
        }
        catch (Exception e) { Logger.Log("[CharBoss] 奥斯提召唤异常: " + e.Message + FramesOf(e)); }
    }
    /// <summary>
    /// 本场里「她到底有没有过奥斯提」—— 用来区分「第一次召唤」和「死了以后的自动复活」。
    /// 多人换牌（吸引仇恨 → 骸骨军团）只该禁止后者。
    /// </summary>
    private static readonly HashSet<int> HadOsty = new();

    private static bool HasHadOsty(CharBossMonster boss)
        => boss != null && HadOsty.Contains(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(boss));

    private static void NoteHadOsty(CharBossMonster boss)
    {
        try { if (boss != null) HadOsty.Add(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(boss)); }
        catch { /* 忽略 */ }
    }

    /// <summary>战斗结束：清掉「有没有过奥斯提」的记录（生物实例每场都是新的，不清会一直攒）。</summary>
    internal static void ResetForNewFight()
    {
        try { HadOsty.Clear(); } catch { /* 忽略 */ }
    }

    /// <summary>
    /// 多人换牌 A2 是否生效（吸引仇恨 → 骸骨军团）。
    /// 只有「多人 + 第三幕（ActIndex 2）+ 亡灵契约师 Boss」时才为 true —— 判定完全交给
    /// <see cref="MultiplayerCardSwap.IsSwapped"/>，所以**单人局恒为 false**（零回归）。
    /// </summary>
    private static bool LegionSwapActive(CharBossMonster boss)
    {
        try
        {
            return MultiplayerCardSwap.IsSwapped(typeof(MegaCrit.Sts2.Core.Models.Cards.PullAggro), boss);
        }
        catch { return false; }
    }

    /// <summary>爪牙标记（原版 MinionPower，只加一次）。</summary>
    internal static void MarkMinion(CharBossMonster boss, Creature osty, PlayerChoiceContext? context)
    {
        try
        {
            if (osty.GetPower<MinionPower>() != null) return;
            var ctx = context ??
                new BlockingPlayerChoiceContext();
            TaskHelper.RunSafely(PowerCmd.Apply<MinionPower>(ctx, osty, 1, boss.Creature, null, true));
            Logger.Log($"[CharBoss] {osty.Name} 已标记为爪牙（MinionPower）");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 爪牙标记失败: " + e.Message);
        }
    }

    /// <summary>镜像朝向 + 站到 Boss 左侧�?/summary>
    private static void MirrorAndPlace(CharBossMonster boss, Creature osty)
    {
        try
        {
            var room = NCombatRoom.Instance;
            var ostyNode = room?.GetCreatureNode(osty);
            var bossNode = boss.Creature != null ? room?.GetCreatureNode(boss.Creature) : null;
            if (ostyNode == null || !GodotObject.IsInstanceValid(ostyNode)) return;

            // �?只翻**立绘**（NCreature.Visuals），不要翻整�?NCreature�?
                // 连节点一起翻会把血�?名字牌的字也镜像成反的（用户实测"字都是反�?）�?
                float dir = FacingDir(boss, bossNode);

            // 朝向不再靠容器：�?*节点**移进 Boss 所在的敌方容器只是为了让层�?站位�?
                //（容器本身不参与它的朝向判定，handover §6 #4 已证）�?
                // 只动节点（视觉），不�?Creature.Side / 战斗列表 �?爪牙(MinionPower)�?
                // 「不可选中、不被群攻直接命中」特性完全保留�?
            var bossParent = bossNode.GetParent();
            if (bossParent != null && ostyNode.GetParent() != bossParent)
            {
                ostyNode.Reparent(bossParent);
                Logger.Log($"[CharBoss] 奥斯提节点已移入 Boss 容器 {bossParent.Name}（阵营与爪牙特性不变）");
            }

            // 节点本身保持不镜像（血�?名字牌是它的直接子节点，翻了字会反）
            ostyNode.Scale = new Vector2(Math.Abs(ostyNode.Scale.X), ostyNode.Scale.Y);

            // 朝向：镜�?**NCreature.Body**�? NCreatureVisuals 里唯一�?%Visuals 节点�?
                // 奥斯提那里它就是 SpineSprite）。见 docs/plans/osty-facing-body.md�?
                //   · 游戏自己的朝向权�?SurroundedPower.FaceDirection 就是�?.Body.Scale 的符号；
            //   · �?mod �?BossVisuals.FaceThePlayer 也用它翻角色 Boss 立绘，已实机有效�?
                //   · OstyScaleToSize 的补间写的是**父级 Visuals.Scale**，碰不到 Body �?一次到位不回头�?
            if (ostyNode is NCreature ostyCreature)
            {
                // ⚠ 只有 Boss 真的朝左（敌方侧）才镜像：斗蛐蛐里 Boss 会被塞到玩家侧（朝右），
                // 这时再镜像就是"方向反了"（用户实测：左边那只的奥斯提朝向相反）。
                FaceToward(ostyCreature, dir);
                // (已由 FaceToward 处理)
                ProbeBody(ostyCreature);   // Body 探针：打印骨架节点信息，并抓"谁把符号写回正数"
            }

            if (bossNode == null || !GodotObject.IsInstanceValid(bossNode)) return;

            // 站位：用**原版公式**，只�?X 镜像到她的左手边�?
                //
            // 原版只对「本地玩家」的奥斯提摆位（OstyScaleToSize �?
            // 原版只对「本地玩家」的奥斯提摆位（OstyScaleToSize 里 if (LocalContext.IsMe(Entity.PetOwner)))
            // 才动 position），影子玩家不满�?�?位置必须我们自己给，那就照抄公式�?
                //   NCreature.GetOstyOffsetFromPlayer =
            //       Vector2.Right * 主人Hitbox�?2 + Osty.MinOffset.Lerp(Osty.MaxOffset, hp/150)
            //   Osty.MinOffset=(150,-75)、MaxOffset=(250,-75) �?原版＝「主人右手边、比主人�?75」�?
                // 敌方�?X 取负 �?站到�?*左手�?*、同一高度�?
                //（旧做法是镜像「玩家那只奥斯提的屏幕坐标」再硬上�?100，偏高且不稳，已弃用。）
            var offset = MirrorOstyOffset(bossNode, osty, dir < 0f);
            ostyNode.GlobalPosition = bossNode.GlobalPosition + offset;
            Logger.Log($"[CharBoss] 奥斯提站位: offset={offset}（原版公式 X 镜像）bossPos={bossNode.GlobalPosition}");
            ForceVisible(ostyNode);   // 血�?状态栏：非本地玩家的随从会被压掉，强制显示
            ProbeScale(ostyNode);     // 朝向探针（每次摆位采样一次，值变了才打）
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 奥斯提镜像/摆位失败: " + e.Message);
        }
    }

    /// <summary>
    /// 站位微调（叠加在原版镜像公式之上）。用户要再挪位置就只改这一行�?
        /// 例：<c>new Vector2(-40f, 20f)</c> = 再往�?40、往�?20�?
        /// </summary>
    private static readonly Vector2 OstyPositionNudge = new(0f, 0f);

    /// <summary>
    /// 她的奥斯提相对她的偏移：<see cref="NCreature.GetOstyOffsetFromPlayer"/> �?**X 镜像**版本�?
        ///
    /// 原版公式（只对本地玩家的奥斯提调用）�?
        ///   <c>Vector2.Right * 主人Hitbox�?2 + Osty.MinOffset.Lerp(Osty.MaxOffset, hp/150)</c>�?
        /// �?<c>Osty.MinOffset=(150,-75)</c>�?c>MaxOffset=(250,-75)</c>
    /// �?原版＝「主�?*右手**边、比主人**�?75**」。敌方侧�?X 取负 �?站她**左手**边、同一高度�?
        /// </summary>
    private static Vector2 MirrorOstyOffset(Node bossNode, Creature osty, bool mirror = true)
    {
        Vector2 offset;
        try
        {
            float halfWidth = 0f;
            if (bossNode is NCreature bossCreature && bossCreature.Hitbox != null)
                halfWidth = bossCreature.Hitbox.Size.X * 0.5f;

            float t = Mathf.Clamp((float)osty.MaxHp / 150f, 0f, 1f);
            var lerped = MegaCrit.Sts2.Core.Models.Monsters.Osty.MinOffset
                .Lerp(MegaCrit.Sts2.Core.Models.Monsters.Osty.MaxOffset, t);

            float dx = halfWidth + lerped.X;
            offset = new Vector2(mirror ? -dx : dx, lerped.Y);
            if (float.IsNaN(offset.X) || float.IsInfinity(offset.X)) offset = DefaultOffset(bossNode);
        }
        catch
        {
            offset = DefaultOffset(bossNode);
        }
        return offset + OstyPositionNudge;
    }

    private static Vector2 DefaultOffset(Node bossNode)
    {
        float w = 250f, h = 260f;
        try
        {
            var bounds = (bossNode as NCreature)?.Visuals?.Bounds;
            if (bounds != null && GodotObject.IsInstanceValid(bounds))
            {
                if (bounds.Size.X > 1f) w = bounds.Size.X;
                if (bounds.Size.Y > 1f) h = bounds.Size.Y;
            }
        }
        catch { /* 默认�?*/ }
        return new Vector2(-(w * 0.85f + 60f), h * 0.25f);
    }

    /// <summary>
    /// 每帧轻量纠正奥斯提站位（只在**玩家回合**里做）。
    ///
    /// 为什么需要（用户实测：「复活后的手跑到她右边去了」）：
    /// 复活那只由 <c>OstyCmd.Summon</c> 造出来，原版会把它摆到「随从默认位置」（主人右手边），
    /// 而我们的摆位只在**她的回合开始**做一次 ⇒ 整个玩家回合里它都挂在她右边。
    /// 这里在她不出手的时候把位置纠回「她左手边」（<see cref="MirrorOstyOffset"/> 恒为负 X）；
    /// 只在她不在出手阶段时动，免得打断它自己的攻击位移。
    /// </summary>
    internal static void EnforcePlacement(CharBossMonster? boss)
    {
        try
        {
            var room = NCombatRoom.Instance;
            if (boss == null || boss.Creature == null || room == null) return;

            // ⚠ 这里**不能**用 SafeOsty(shadow)：那只复活的手是我们自己
            // 用 OstyCmd.Summon + 手动登记进 Pets 造的，`shadow.Osty` 可能一直是 null
            //（日志实测：turn=2 之后 shadowOsty 还是 null，于是这条纠正从来没跑到过）。
            // 所以改成「按节点找」：全树扫一次、缓存住，失效了再扫。
            var ostyNode = HerLiveOstyNode(boss, room);
            if (ostyNode == null) return;

            var bossNode = room.GetCreatureNode(boss.Creature);
            if (bossNode == null || !GodotObject.IsInstanceValid(bossNode)) return;

            // 站位方向跟着 Boss 的朝向走：朝左（敌方侧）站她左手边，朝右（斗蛐蛐里被塞进玩家侧）站右手边。
            float dir = FacingDir(boss, bossNode);
            var want = bossNode.GlobalPosition + MirrorOstyOffset(bossNode, ostyNode.Entity, dir < 0f);

            // 判定「挂错边」：站在了背离敌人的那一侧。
            // 只要不在错误一侧就不插手 —— 这样她自己出手时奥斯提的攻击/受击位移不会被每帧拉回来。
            bool wrongSide = dir < 0f
                ? ostyNode.GlobalPosition.X > bossNode.GlobalPosition.X
                : ostyNode.GlobalPosition.X < bossNode.GlobalPosition.X;
            if (!wrongSide && ostyNode.GlobalPosition.DistanceTo(want) < 400f) return;   // 差得不多 → 多半是它自己在动

            ostyNode.GlobalPosition = want;
            Logger.Log($"[CharBoss] 奥斯提站位已纠正（跑到右边就是这种）→ offset={want - bossNode.GlobalPosition}");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 奥斯提站位纠正失败: " + e.Message);
        }
    }

    /// <summary>每只 Boss 各记一份找到的奥斯提节点（多人同场时不能共用一个静态缓存）。</summary>
    private static readonly Dictionary<CharBossMonster, NCreature?> OstyNodeCache = new();

    /// <summary>上次「扫全树找奥斯提」的时刻（<c>Time.GetTicksUsec</c>）；找不着时节流用。</summary>
    private static readonly Dictionary<CharBossMonster, ulong> OstyScanUsec = new();

    /// <summary>找不到时最少隔多久再扫一次（性能：见 <see cref="HerLiveOstyNode"/>）。</summary>
    private const ulong OstyScanIntervalUsec = 200_000;   // 5Hz

    /// <summary>已经为哪只生物请求过「爪牙」（避免每帧重复申请叠层）。</summary>
    private static Creature? _minionRequested;

    /// <summary>
    /// 每帧确认奥斯提的**朝向与血条/状态栏**（幂等，开销很小）。
    ///
    /// 为什么必须每帧：复活出来的那只奥斯提是原版 <c>OstyCmd.Summon</c> **新建的 NCreature**，
    /// 我们摆位那一刻它可能还没初始化完；原版之后会
    ///   ① 把 <c>Body.Scale</c>（立绘）写成正数 → 看起来又朝右；
    ///   ② 因为「不是本地玩家的随从」把整个状态显示容器（血条/状态）压成 visible=false + alpha=0。
    /// 所以只在摆放时做一次是不够的（用户实测：复活后朝右、而且没有血量和状态栏）。
    /// </summary>
    internal static void EnforceVisuals(CharBossMonster? boss)
    {
        try
        {
            var room = NCombatRoom.Instance;
            if (boss?.Creature == null || room == null) return;

            var ostyNode = HerLiveOstyNode(boss, room);
            if (ostyNode == null) return;

            // 朝向按"敌人在哪边"决定：朝左才镜像，朝右保持正数（幂等）。
            FaceToward(ostyNode, FacingDir(boss, NodeFor(boss.Creature) ?? room.GetCreatureNode(boss.Creature)));
            // (已由 FaceToward 处理)
            ForceVisible(ostyNode);   // 幂等：把状态显示容器点亮

            // 爪牙标记（MinionPower）：复活出来的是原版新建的生物，
            // 我们的 MarkMinion 只在新召唤那一刻调过一次 ⇒ 复活后身上没有「爪牙」（用户实测）。
            // 这里每帧确认一次，但**每只生物只请求一次**（PowerCmd.Apply 是异步的，
            // 每帧都请求会叠出好几层爪牙）。
            var entity = ostyNode.Entity;
            if (entity != null && entity.IsAlive && !ReferenceEquals(_minionRequested, entity))
            {
                try
                {
                    if (entity.GetPower<MinionPower>() == null)
                    {
                        _minionRequested = entity;
                        MarkMinion(boss!, entity, null);
                    }
                }
                catch { /* 忽略 */ }
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 奥斯提朝向/血条每帧纠正失败: " + e.Message);
        }
    }

    /// <summary>她那一侧活着的奥斯提（按**节点**找，不看 pcs.Pets）。</summary>
    /// <summary>
    /// 多人「**爪牙随主人死亡**」（设计见 multiplayer-lockon.md §阶段 1）：
    /// Boss 一死就把她的奥斯提一起带走，免得它作为「还活着的敌人」挡着战斗结算、或者留下来继续挨打。
    /// 单人时同样生效（她的奥斯提本来就该随她消失），对战斗结果没有影响。
    /// </summary>
    /// <summary>
    /// 主人死了 / 战斗结束时：她的所有随从（**含尸体**）一起清掉。
    /// 用户 2026-10-23 实测：亡灵契约师死后奥斯提的尸体还躺在场上（半死不活）⇒ 这里连尸体一起摘掉。
    /// </summary>
    /// <param name="killPets">
    /// 要不要**真的把它们弄死**（= 派发 `Died` 事件、走死亡结算）。
    /// · 她被打死（我们赢了）⇒ true：随从随主人一起死，胜利界面里不会留活着的随从；
    /// · **玩家被打死（我们输了）⇒ false**：这一场马上就要被整个拆掉，没必要再给随从派一次
    ///   `Died`（用户 2026-10-27 提问「因玩家死亡而战斗结束也要清掉奥斯提吗」，结论：只需**藏起来**，
    ///   免得在拆场景的时候多触发一轮死亡事件 —— 别的模组可能挂在上面）。
    /// 两种情况下「把节点收起来」都照做，所以不会留「半死不活的奥斯提在场上」。
    /// </param>
    internal static void PurgeAllOnFightEnd(CharBossMonster boss, bool killPets = true)
    {
        try
        {
            if (boss?.CombatState == null) return;

            var pets = new List<Creature>();
            try { foreach (var p in OstysOf(boss)) if (p != null && !pets.Contains(p)) pets.Add(p); }
            catch { /* 忽略 */ }
            try
            {
                var list = boss.Shadow?.PlayerCombatState?.Pets;
                if (list != null)
                    foreach (var p in list)
                        if (p != null && !pets.Contains(p)) pets.Add(p);
            }
            catch { /* 忽略 */ }

            int n = 0;
            foreach (var pet in pets)
            {
                try
                {
                    if (killPets && pet.IsAlive)
                        pet.LoseHpInternal(pet.CurrentHp,
                            MegaCrit.Sts2.Core.ValueProps.ValueProp.Unpowered
                            | MegaCrit.Sts2.Core.ValueProps.ValueProp.Unblockable);
                    // ⚠ **不要在这里 DetachFromCombat**：战斗结束时引擎自己还要按 `_enemies`/`_allies`
                    //   列表做清理（`CombatState.RemoveCreature`），我们先把它摘掉会让它找不到生物：
                    //   `KeyNotFoundException: Removed creature 佩尔的士兵 was not found`
                    //   → 玩家被击杀后返回主菜单时黑屏（测试玩家 2026-10-23 实测的堆栈）。
                    //   只把节点收起来就够「别留半死不活的奥斯提在场上」了。
                    HideNode(pet);
                    n++;
                }
                catch (Exception e)
                {
                    Logger.Log("[CharBoss] 战斗结束清随从失败: " + e.Message);
                }
            }
            if (n > 0)
            {
                Logger.Log(killPets
                    ? $"[CharBoss] 战斗结束：清掉她的随从 ×{n}（她已阵亡 ⇒ 随从一起死，含尸体，避免半死不活）"
                    : $"[CharBoss] 战斗结束（玩家阵亡、她还活着）：只把她的随从 ×{n} 收起来，"
                      + "不再派发死亡事件（这一场马上就要整个拆掉了）");
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 战斗结束清随从异常: " + e.Message);
        }
    }

    internal static void RemoveOnBossDeath(CharBossMonster boss)
    {
        try
        {
            var owner = boss?.Creature;
            if (owner == null || owner.IsAlive) return;      // 主人还活着：什么都不做

            // ⚠ 必须**把所有属于她的奥斯提都带走**，而不是只处理「按节点找到的第一只」：
            //   多人「骸骨军团」会让每只 Boss 各有一只奥斯提，而 `HerLiveOstyNode` 是按节点扫、
            //   缓存又可能指向别的那只 ⇒ 只处理一只时她那只就活到了队友死掉才消失（用户实测）。
            int killed = 0;
            foreach (var osty in OstysOf(boss))
            {
                if (osty == null || !osty.IsAlive) continue;
                try
                {
                    osty.LoseHpInternal(osty.CurrentHp,
                        MegaCrit.Sts2.Core.ValueProps.ValueProp.Unblockable
                        | MegaCrit.Sts2.Core.ValueProps.ValueProp.Unpowered);
                    killed++;
                    // 每只都记一行：万一哪天误伤了队友的奥斯提，日志能直接看出来
                    Logger.Log($"[CharBoss] 爪牙随主人死亡：带走 {SafeId(osty)}" +
                               $"（它的主人 netId={SafeOwnerNet(osty)}，她自己的 netId={SafeNet(boss.Shadow)}）");
                }
                catch (Exception e)
                {
                    Logger.Log("[CharBoss] 爪牙随主人死亡：带走奥斯提失败: " + e.Message);
                }
            }

            if (killed > 0)
                Logger.Log($"[CharBoss] 爪牙随主人死亡：{boss.Id.Entry} 的奥斯提 ×{killed} 已随之消失");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 爪牙随主人死亡处理失败: " + e.Message);
        }
    }

    /// <summary>
    /// 这名 Boss 名下**所有**活着的奥斯提（按 `PetOwner` / 她自己 `Pets` 列表判定归属）。
    /// 注意 `PetOwner` 有时是 null（日志实测），所以两条判据都要认。
    /// </summary>
    private static List<Creature> OstysOf(CharBossMonster boss)
    {
        var result = new List<Creature>();
        try
        {
            var combat = boss?.CombatState;
            if (combat == null) return result;
            foreach (var list in new[] { combat.Enemies, combat.Allies })
            {
                if (list == null) continue;
                foreach (var c in list)
                {
                    if (c == null) continue;
                    if (c.Monster is not MegaCrit.Sts2.Core.Models.Monsters.Osty) continue;
                    if (!BelongsTo(boss, c)) continue;
                    if (!result.Contains(c)) result.Add(c);
                }
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 收集她的奥斯提失败: " + e.Message);
        }
        return result;
    }

    /// <summary>
    /// 这只奥斯提是不是**属于这只 Boss**。
    ///
    /// 判据全部是「归属」而不是「是不是我召唤的」——这一点很关键：
    /// 骸骨军团是**她**打出的，但它给每只 Boss 各召一只，那些奥斯提的主人是**被召的那只 Boss**
    /// （`OstyCmd.Summon` 会把 `PetOwner` 设成挨召的 Player、并登记进**那只**的 `Pets`）。
    /// 所以这里两道判据都不可能命中队友的奥斯提，她死了**不会**把队友的带走。
    /// </summary>
    private static bool BelongsTo(CharBossMonster boss, Creature osty)
    {
        try
        {
            var shadow = boss?.Shadow;
            if (shadow == null || osty == null) return false;

            // ① `PetOwner` 最权威：指向别人（例如队友 Boss 的影子）→ 不是她的
            var petOwner = osty.PetOwner;
            if (petOwner != null) return ReferenceEquals(petOwner, shadow);

            // ② 退化判据：在她自己的 Pets 列表里才算她的（`PetOwner` 有时是 null，日志实测）
            var pcs = shadow.PlayerCombatState;
            if (pcs != null && PetsField?.GetValue(pcs) is List<Creature> pets) return pets.Contains(osty);

            // ③ 两条都拿不到 → **保守返回 false**：宁可少带走一只，也绝不能把队友的奥斯提带走
            return false;
        }
        catch { /* 忽略 */ }
        return false;
    }

    /// <summary>
    /// 给「Boss 死亡」挂一次监听（每只一次）—— 光靠每帧检查不够：
    /// 她死后可能已经不在 `CombatState.Enemies` 里了，`BossFrameTick` 的 everyBoss 就漏掉她，
    /// 于是她的奥斯提一直活到队友死掉（用户实测）。`Creature.Died` 一定会触发。
    /// </summary>
    private static readonly HashSet<int> DeathHooked = new();

    internal static void EnsureDeathHook(CharBossMonster boss)
    {
        try
        {
            var creature = boss?.Creature;
            if (creature == null) return;
            if (!DeathHooked.Add(System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(creature))) return;
            creature.Died += _ => RemoveOnBossDeath(boss);
            Logger.Log($"[CharBoss] 已挂上「{boss.Id.Entry} 死亡 → 带走奥斯提」的监听");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 挂奥斯提死亡监听失败: " + e.Message);
        }
    }

    /// <summary>战斗结束：清掉死亡监听去重表（生物实例每场都是新的）。</summary>
    internal static void ResetDeathHooks()
    {
        try { DeathHooked.Clear(); } catch { /* 忽略 */ }
    }

    private static Creature? LiveOstyCreature(CharBossMonster boss)    {
        try
        {
            var room = NCombatRoom.Instance;
            if (room == null) return null;
            return HerLiveOstyNode(boss, room)?.Entity;
        }
        catch { return null; }
    }

    /// <summary>影子玩家的 Pets 数量（-1 = 取不到）。</summary>
    private static int SafePetCount(PlayerCombatState pcs)
    {
        try { return PetsField?.GetValue(pcs) is List<Creature> pets ? pets.Count : -1; }
        catch { return -1; }
    }

    /// <summary>诊断：把 Pets 里每只随从的怪物/血量/归属/阵营都打出来（定位"到底哪只算她的"）。</summary>
    private static string DumpPets(Player shadow)
    {
        try
        {
            var pcs = shadow.PlayerCombatState;
            if (pcs == null) return "pcs=null";
            var sb = new System.Text.StringBuilder();
            if (PetsField?.GetValue(pcs) is List<Creature> pets)
            {
                sb.Append($"fieldCount={pets.Count} [");
                foreach (var p in pets)
                {
                    if (p == null) { sb.Append("null;"); continue; }
                    sb.Append($"{SafeMonsterEntry(p)} hp={p.CurrentHp}/{p.MaxHp} alive={p.IsAlive} " +
                              $"owner={SafeOwnerNet(p)} side={p.Side}; ");
                }
                sb.Append(']');
            }
            else sb.Append("PetsField=取不到");
            sb.Append($" propPets={pcs.Pets?.Count ?? -1}");
            sb.Append($" ostyProp={(shadow.Osty == null ? "null" : "有")}");
            return sb.ToString();
        }
        catch (Exception e) { return "dump失败:" + e.Message; }
    }

    /// <summary>她那一侧活着的奥斯提节点（带缓存；节点换了/死了就重新扫）。</summary>
    /// <remarks>
    /// ⚠ 性能（用户反馈「boss 战仍然卡」的主因之一）：这个方法被
    /// <see cref="EnforceVisuals"/> 与 <see cref="EnforcePlacement"/> **每帧各调一次**，
    /// 而找不到节点时它会 <see cref="FindHerLiveOstyNode"/> **递归扫一遍整个战斗房间的所有节点**。
    /// 除亡灵契约师以外的 Boss 永远没有奥斯提 ⇒ 每帧两次全树扫描、什么都扫不到。
    ///
    /// 现在加两道闸：
    ///   ① <see cref="MightHaveOsty"/>：影子玩家连随从都没有就直接返回 null，**根本不扫**；
    ///   ② 扫不到时按 <see cref="OstyScanIntervalUsec"/>（5Hz）节流，不再每帧扫。
    /// 缓存也改成**按 Boss 各一份**（原来是一个静态字段，多人同场时两只 Boss 会互相顶掉）。
    /// </remarks>
    private static NCreature? HerLiveOstyNode(CharBossMonster boss, Node room)
    {
        if (boss == null || room == null) return null;

        if (OstyNodeCache.TryGetValue(boss, out var node) &&
            node != null && GodotObject.IsInstanceValid(node) && IsHerLiveOsty(node, boss))
        {
            return node;
        }

        // ① 连随从都没有 → 不可能有奥斯提节点，别扫
        if (!MightHaveOsty(boss))
        {
            OstyNodeCache[boss] = null;
            return null;
        }

        // ② 扫不到时节流
        var now = Time.GetTicksUsec();
        if (OstyScanUsec.TryGetValue(boss, out var last) && now - last < OstyScanIntervalUsec) return null;
        OstyScanUsec[boss] = now;

        node = FindHerLiveOstyNode(room, boss);
        OstyNodeCache[boss] = node;
        return node;
    }

    /// <summary>这只 Boss 有没有可能带着奥斯提（= 影子玩家的随从列表非空）。拿不到归属信息时保守返回 true。</summary>
    private static bool MightHaveOsty(CharBossMonster boss)
    {
        try
        {
            var pcs = boss.Shadow?.PlayerCombatState;
            if (pcs == null) return false;

            // ① 只要有随从就一定要找（**不分角色**：多人换牌「骸骨军团」会给队友 Boss 也召一只，
            //    那只的爪牙标记/朝向/站位全靠这里找节点）。
            int count = SafePetCount(pcs);
            if (count > 0) return true;

            try
            {
                var creaturePets = boss.Creature?.Pets;
                if (creaturePets != null && creaturePets.Count > 0) return true;
            }
            catch { /* 忽略 */ }

            // ② 没有随从：只有亡灵契约师会「读秒」召奥斯提，其它角色直接不扫
            //    （这是「boss 战卡」的主因：以前每帧两次递归扫整个战斗房间，扫半天什么也没有）。
            if (boss.Def.Key != "NECROBINDER_BOSS") return false;

            // ③ 她的 Pets 可能是 0、而画面上确实有一只活的（原版遗物召出来那只没进影子玩家的 Pets，
            //    见 BossOsty.Update 的注释）⇒ 亡灵契约师**保留兜底扫描**，只是按 5Hz 节流。
            return true;
        }
        catch { return true; }
    }

    /// <summary>这个节点是不是「她那一侧、活着的」奥斯提。</summary>
    private static bool IsHerLiveOsty(NCreature node, CharBossMonster boss)
    {
        try
        {
            var entity = node.Entity;
            if (entity == null || !entity.IsAlive) return false;
            if (!SafeMonsterEntry(entity).Contains("OSTY", StringComparison.OrdinalIgnoreCase)) return false;

            // 归属判定两种都认：
            //   ① PetOwner 就是影子玩家（原版正常路径）；
            //   ② 在影子玩家的 Pets 里（我们自己登记的那只，PetOwner 可能没被设上）。
            // 以前这里还硬要求 `entity.Side == boss.Creature.Side`，但原版把宠物挂在
            // Allies 里（玩家一侧）⇒ 活的奥斯提永远匹配不上，于是每回合都再召一只（用户实测）。
            if (entity.PetOwner != null && ReferenceEquals(entity.PetOwner, boss.Shadow)) return true;

            var pcs = boss.Shadow?.PlayerCombatState;
            if (pcs != null && PetsField?.GetValue(pcs) is List<Creature> pets) return pets.Contains(entity);
            return false;
        }
        catch { return false; }
    }

    private static NCreature? FindHerLiveOstyNode(Node room, CharBossMonster boss)
    {
        foreach (var nc in CollectCreatureNodes(room))
        {
            if (IsHerLiveOsty(nc, boss)) return nc;
        }
        return null;
    }

    /// <summary>兜底：直接调原版 OstyCmd.Summon 给她召一只（数量取缚魂命匣的 Summon 变量，读不到就用 1）</summary>
    /// <summary>
    /// 她的「**回合开始召唤**」量 + 来源遗物。
    ///
    /// · 缚魂命匣 `BoundPhylactery`：`DynamicVars.Summon`（1）；
    /// · 无界命匣 `PhylacteryUnbound`（欧洛巴斯之触把前者升级成它，用户 2026-11-03 换上）：
    ///   `DynamicVars["StartOfTurn"]`（2）—— 它**没有** `Summon` 变量，所以不能直接读 `.Summon`。
    /// 找不到遗物时按 1（与缚魂命匣原版一致，不至于召唤失败）。
    /// </summary>
    internal static (decimal Amount, AbstractModel? Source) TurnStartSummonOf(Player? shadow)
    {
        try
        {
            var relics = shadow?.Relics;
            if (relics != null)
            {
                for (int i = 0; i < relics.Count; i++)
                {
                    var relic = relics[i];
                    if (relic == null) continue;
                    string entry = SafeEntryOf(relic);
                    if (entry == "PHYLACTERY_UNBOUND")
                        return (ReadSummonVar(relic, "StartOfTurn", 2m), relic);
                    if (entry == "BOUND_PHYLACTERY")
                        return (ReadSummonVar(relic, "Summon", 1m), relic);
                }
            }
        }
        catch (Exception e) { Logger.Log("[CharBoss] 读回合开始召唤量失败: " + e.Message); }
        return (1m, null);
    }

    /// <summary>
    /// 她的「**战斗开始召唤**」量 + 来源遗物（`PhylacteryUnbound.BeforeCombatStart` → `StartOfCombat` = 5）。
    /// 缚魂命匣没有这一条（原版那条被 `Patches/OstyNoCombatStartSummonPatch` 拦掉了）⇒ 返回 0。
    /// </summary>
    internal static (decimal Amount, AbstractModel? Source) CombatStartSummonOf(Player? shadow)
    {
        try
        {
            var relics = shadow?.Relics;
            if (relics != null)
            {
                for (int i = 0; i < relics.Count; i++)
                {
                    var relic = relics[i];
                    if (relic == null) continue;
                    if (SafeEntryOf(relic) != "PHYLACTERY_UNBOUND") continue;
                    return (ReadSummonVar(relic, "StartOfCombat", 5m), relic);
                }
            }
        }
        catch (Exception e) { Logger.Log("[CharBoss] 读战斗开始召唤量失败: " + e.Message); }
        return (0m, null);
    }

    /// <summary>
    /// 「**第一次**召唤时额外补上的量」—— 无界命匣的「战斗开始召唤 `StartOfCombat`(5)」。
    ///
    /// 为什么挪到这里：战斗开始那一刻 `CombatManager.IsInProgress` 还是 false，
    /// `PlayerCmd.AddPet` 会抛错（Telemetry 里那条已知噪音就是它）⇒ 那一笔永远拿不到。
    /// 所以在**她第 1 个回合开始**和每回合的 `StartOfTurn` 一起给，总数与卡面一致。
    /// 缚魂命匣没有这一项 ⇒ 返回 0（不影响别的亡灵契约师 Boss）。
    /// </summary>
    private static decimal FirstTurnExtraSummonOf(Player? shadow, CharBossMonster? boss)
    {
        try
        {
            if (boss == null || boss.TurnNumber > 1) return 0m;
            var relics = shadow?.Relics;
            if (relics == null) return 0m;
            for (int i = 0; i < relics.Count; i++)
            {
                var relic = relics[i];
                if (relic == null) continue;
                if (SafeEntryOf(relic) != "PHYLACTERY_UNBOUND") continue;
                return ReadSummonVar(relic, "StartOfCombat", 5m);
            }
        }
        catch (Exception e) { Logger.Log("[CharBoss] 读首次召唤量失败: " + e.Message); }
        return 0m;
    }
    /// <summary>读遗物上某个召唤量变量（取不到用 <paramref name="fallback"/>）。</summary>
    private static decimal ReadSummonVar(RelicModel relic, string key, decimal fallback)
    {
        try
        {
            if (relic.DynamicVars.TryGetValue(key, out var v) && v != null) return v.BaseValue;
        }
        catch { /* 忽略 */ }
        return fallback;
    }
    private static Creature? SummonFallback(CharBossMonster boss, Player shadow, PlayerChoiceContext? context)
    {
        try
        {
            // 兜底召唤：召唤量同样问 <see cref="TurnStartSummonOf"/>（缚魂命匣 1 / 无界命匣 2）
            var (amount, source) = TurnStartSummonOf(shadow);

            var ctx = context ??
                new BlockingPlayerChoiceContext();
            TaskHelper.RunSafely(OstyCmd.Summon(ctx, shadow, amount, source));
            Logger.Log($"[CharBoss] 奥斯提兜底召唤：amount={amount} source={SafeEntryOf(source)}");
            return null;   // 异步召唤，下一�?Update 再取
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 奥斯提兜底召唤异常: " + e.Message);
            return null;
        }
    }

    private static string SafeEntryOf(AbstractModel? model)
    {
        try { return model?.Id.Entry ?? "null"; } catch { return "?"; }
    }
    private static string SafeId(Creature creature)
    {
        try { return creature.Monster?.Id.Entry ?? "?"; } catch { return "?"; }
    }

    private static string SafeOwnerNet(Creature creature)
    {
        try { return creature.Player?.NetId.ToString() ?? "null"; } catch { return "?"; }
    }

    private static string SafeNet(Player player)
    {
        try { return player.NetId.ToString(); } catch { return "?"; }
    }
    private static string SafeMonsterEntry(Creature creature)
    {
        try { return creature.Monster?.Id.Entry ?? string.Empty; }
        catch { return string.Empty; }
    }
}
