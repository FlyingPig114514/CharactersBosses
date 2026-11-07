using System;
using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Potions;
using CharacterBosses.Core;
using CharacterBosses.Monsters;

namespace CharacterBosses.UI;

/// <summary>
/// **Boss 的药水栏**（用户 2026-10-25：「药水栏位放在遗物左侧，同样是一排」）。
///
/// 排布借用 <see cref="BossStatusRow"/> 的「按子节点顺序从右往左摆」：
/// 状态条里子节点**下标越小越靠左**（遗物是反着加的，所以起始遗物落在最右；能量球最后加、最右）。
/// 于是**先加药水、再加遗物**，药水就天然排在遗物左边、同一行、同样大小的格子。
///
/// 每个格子 = 原版药水贴图（`PotionModel.Image`，56×56，和遗物图标一致）。
/// **意图**：这瓶药水本回合计划要喝时，在图标**正下方**显示原版意图图标（`NIntent`）。
///
/// ⚠ 两个必须遵守的点（都是本轮实测出来的）：
///   ① **格子必须先 AddChild 进状态条，再建意图**。`NIntent._Ready()` 里才去
///      `GetNode("%IntentParticle")` / `%Value`；节点不在树上 `_Ready` 不会跑，
///      这时调 `UpdateIntent` 会空引用（日志里的「药水意图渲染失败: Object reference not set…」），
///      于是药水意图一个都画不出来；
///   ② 格子名必须**唯一**（带下标）。以前叫 `BossPotion_<药水名>`，而同一排水里还留着上一轮的旧格子
///      （见 BossStatusRow.Fill 的注释），重名就被 Godot 自动改名为 `@Control@1234`，日志根本认不出是谁。
/// </summary>
public static class BossPotionRow
{
    private const float Side = 56f;          // 与遗物图标同尺寸

    /// <summary>
    /// 原版意图的**实测**几何（`mod_log` 导出，别再猜）：
    /// <code>
    /// Intent[NIntent]          size=(64,64)                 ← 意图节点自己的框
    ///   IntentHolder[Control]  pos=(0,0)   size=(64,64)
    ///     Intent[Sprite2D]     pos=(30,31)                    ← 箭头可视中心 ≈ 节点原点 +(32,32)
    ///     Value[MegaRichTextLabel] pos=(2,40) size=(62,23)    ← 攻势的数字（强化类没有）
    /// </code>
    /// ⚠ **不要**再用 <c>HBoxContainer</c> 套它（用户 2026-10-26 第二张截图：箭头整体偏右半个图标）：
    /// 容器排版前 NIntent 的 `pos` 是 (-32,-32)，排版后会被容器摆回 (0,0) ⇒
    /// 可见中心从「容器原点」变成「容器原点 +(30,31)」，横向偏 30px、纵向偏 31px，而且
    /// 这个偏移**取决于容器什么时候排版**，靠猜容器位置永远对不准。
    /// 现在直接把 NIntent 挂成格子的子节点：它的 Position/Scale 完全由我们控制，
    /// 于是「箭头中心 = Position + 32×缩放」是确定的。
    /// </summary>
    private const float IntentVisualRadius = 32f;

    /// <summary>箭头 <c>Sprite2D</c> 在 NIntent 局部的中心（实测 (30,31)，≈ 框心）。</summary>
    private const float IntentSpriteCenterX = 30f;
    private const float IntentSpriteCenterY = 31f;

    private static int _renderLogs;
    private static int _intentLogs;

    /// <summary>
    /// 把她的药水图标加进状态条（最终由 <see cref="BossStatusRow.Fill"/> 排到遗物左侧）。
    /// 返回真正加进去的格子（顺序 = 栏位顺序），供状态条做自检与排序。
    /// </summary>
    public static List<Control> AddTo(Control row, CharBossMonster boss)
    {
        var cells = new List<Control>();
        if (row == null || !GodotObject.IsInstanceValid(row)) return cells;

        try
        {
            var shadow = boss?.Shadow;
            var held = BossPotions.Held(shadow);
            if (held.Count == 0) return cells;

            var planned = boss!.PlannedPotions ?? new List<Type>();
            if (_renderLogs++ < 20)
            {
                Logger.Log($"[CharBoss] 药水栏渲染：held={held.Count} " +
                           $"[{string.Join(",", held.ConvertAll(x => BossPotions.SafeName(x)))}]" +
                           $" 本回合计划喝={planned.Count}");
            }

            for (int i = 0; i < held.Count; i++)
            {
                var potion = held[i];
                if (potion == null) continue;

                var cell = MakeCell(i, potion);
                row.AddChild(cell);                       // ← ① 先入树（NIntent 的 _Ready 要跑）
                cells.Add(cell);

                // 意图：只有「本回合要喝」的那几瓶才画
                bool willDrink = false;
                try { willDrink = planned.Contains(potion.GetType()); }
                catch { /* 读不到就当作不喝 */ }
                // 被击晕 ⇒ 本回合一滴药水也不会喝（她整个回合都不行动），箭头一并撤掉；
                // 药水图标本身照旧显示（那是她的持有物，不是「本回合预告」）。
                if (willDrink && boss!.Stunned) willDrink = false;
                if (willDrink) AddIntent(cell, boss!, potion);
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 药水栏渲染失败: " + e.Message);
        }
        return cells;
    }

    /// <summary>一格 = 原版药水贴图（56×56，和遗物图标一致）。</summary>
    private static Control MakeCell(int index, PotionModel potion)
    {
        var cell = new Control
        {
            // ② 名字必须唯一：带上序号，日志里也能一眼看出「第几瓶、哪一瓶」
            Name = BossStatusRow.PotionCellPrefix + index + "_" + BossPotions.SafeName(potion),
            CustomMinimumSize = new Vector2(Side, Side),
            Size = new Vector2(Side, Side),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            TooltipText = BossPotions.SafeName(potion),
        };

        try
        {
            var texture = potion.Image;
            if (texture != null)
            {
                var icon = new TextureRect
                {
                    Texture = texture,
                    ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                    StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                    CustomMinimumSize = new Vector2(Side, Side),
                    Size = new Vector2(Side, Side),
                    MouseFilter = Control.MouseFilterEnum.Ignore,
                };
                cell.AddChild(icon);
            }
            else if (_renderLogs < 21)
            {
                _renderLogs++;
                Logger.Log($"[CharBoss] 药水 {BossPotions.SafeName(potion)} 没有贴图");
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 药水贴图取不到: " + e.Message);
        }

        return cell;
    }

    /// <summary>
    /// 给药水格子挂一个意图（原版 <c>NIntent</c>，用户 2026-10-25：不要自己写的字）。
    ///
    /// **必须在这个 cell 已经在树上之后调用**（见类注释 ①）。
    /// 位置/大小见 <see cref="IntentVisualRadius"/> 的实测几何：意图节点**直接挂在格子上**（不套容器），
    ///   · 缩放默认 **1.0**（= 卡牌下面那排意图的原版大小，用户 2026-10-26：「太小」）；
    ///   · 箭头中心落在图标**正下方、水平居中**：`Position = (28 - 32s, 56 + gap)`，
    ///     因为可视中心 = Position + 32×缩放；
    ///   · 两个值都能在 `mods\CharacterBosses\charbosses_ui.txt` 里热调
    ///     （`potionIntentGapY` / `potionIntentScalePct`）。
    /// </summary>
    private static void AddIntent(Control cell, CharBossMonster boss, PotionModel potion)
    {
        try
        {
            var node = MegaCrit.Sts2.Core.Nodes.Combat.NIntent.Create(0f);
            node.Name = "PotionIntent";                        // 不套容器：位置/缩放全由我们说了算
            node.MouseFilter = Control.MouseFilterEnum.Ignore;
            cell.AddChild(node);
            // 到这一步 node 已经在树上、_Ready 已经跑过，UpdateIntent 才不会空引用
            node.UpdateIntent(IntentFor(potion), TargetsOf(boss), boss.Creature);

            float scale = Math.Max(0.2f, BossUiTuning.PotionIntentScalePct / 100f);
            float gap = BossUiTuning.PotionIntentGapY;
            node.PivotOffset = Vector2.Zero;                   // 缩放绕原点，公式才好写
            node.Scale = new Vector2(scale, scale);
            // 箭头可视中心（节点局部、缩放后）= (30,31)×缩放，可视半径 ≈ 32×缩放
            //   ⇒ 想让它落在 (图标中线, 图标下沿 + 间隙 + 半径) 上，就反推节点原点：
            float radius = IntentVisualRadius * scale;
            node.Position = new Vector2(Side * 0.5f - IntentSpriteCenterX * scale,
                                        Side + gap + radius - IntentSpriteCenterY * scale);
            cell.ClipContents = false;                          // 意图画在格子外面，别裁剪

            if (_intentLogs++ < 6)
            {
                Logger.Log($"[CharBoss] 药水意图：{BossPotions.SafeName(potion)} 格子={cell.Name} " +
                           $"节点原点={node.Position} 箭头中心=({Side * 0.5f:0},{Side + gap + radius:0}) " +
                           $"缩放={scale:0.00} 间隙={gap} 在树上={node.IsInsideTree()}");
                // 第一次顺手把 NIntent 的内部几何打出来（万一以后原版改了场景，一眼能看出偏在哪）
                if (_intentLogs == 1) DumpIntentTree(node, 0);
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 药水意图渲染失败: " + e);
        }
    }

    /// <summary>
    /// 诊断：把 <c>NIntent</c> 的子树（名字/类型/局部位置/尺寸/贴图）打出来。
    /// 只为了回答一个问题：意图图形相对节点原点是往上画还是往下画（决定 holder 该摆在哪）。
    /// </summary>
    private static void DumpIntentTree(Node node, int depth)
    {
        if (depth > 3 || !GodotObject.IsInstanceValid(node)) return;
        try
        {
            string extra = string.Empty;
            if (node is Control c) extra = $" pos={c.Position} size={c.Size} min={c.GetCombinedMinimumSize()}";
            else if (node is Sprite2D sp) extra = $" pos={sp.Position} offset={sp.Offset}" +
                                                  (sp.Texture != null ? $" tex={sp.Texture.ResourcePath}" : "");
            else if (node is MegaCrit.Sts2.addons.mega_text.MegaRichTextLabel rtl) extra = $" pos={rtl.Position} size={rtl.Size} text={rtl.Text}";
            Logger.Log($"[CharBoss]   意图节点{new string(' ', depth * 2)}{node.Name}[{node.GetType().Name}]{extra}");
            foreach (var child in node.GetChildren())
            {
                if (child is Node n) DumpIntentTree(n, depth + 1);
            }
        }
        catch { /* 诊断而已 */ }
    }

    /// <summary>
    /// 药水 → 原版意图（用户 2026-10-25 / 2026-10-26 的口径）：
    ///   · 格挡药水 / 瓶中船 → **守势**（`DefendIntent`）
    ///   · 火焰药水 / 爆裂安瓿 → **攻势**，数字写死（`RawAttackIntent`：不吃力量/虚弱等加成，就是卡面那个数）
    ///   · **狡诈药水 → 攻势「6×3」**：它给 3 张「小刀+」（每张 6 伤，见 <see cref="CunningShivs"/>），
    ///     小刀是攻击牌 ⇒ 用原版 `MultiAttackIntent`，力量/虚弱/易伤由意图节点自己算
    ///     （用户 2026-10-26：「在狡诈药水处显示 6*3 的基础伤害，受力量虚弱等影响」）
    ///   · 镣铐药水 → **策略**（`DebuffIntent`）
    ///   · 其余一律 → **强化**（`BuffIntent`）
    /// </summary>
    private static MegaCrit.Sts2.Core.MonsterMoves.Intents.AbstractIntent IntentFor(PotionModel potion)
    {
        var type = potion?.GetType();
        if (type == typeof(BlockPotion) || type == typeof(ShipInABottle))
            return new MegaCrit.Sts2.Core.MonsterMoves.Intents.DefendIntent();

        if (type == typeof(FirePotion) || type == typeof(ExplosiveAmpoule))
        {
            int damage = DirectDamage(potion!);
            return new RawAttackIntent(damage > 0 ? damage : 20);
        }

        if (type == typeof(CunningPotion))
        {
            var (shivDamage, shivs) = CunningShivs(potion!);
            // 段数只有 1 时用单段意图（否则会显示「6×1」）
            return AttackIntent.Multi(shivDamage, shivs);
        }

        if (type == typeof(ShacklingPotion))
            return new MegaCrit.Sts2.Core.MonsterMoves.Intents.DebuffIntent(false);

        return new MegaCrit.Sts2.Core.MonsterMoves.Intents.BuffIntent();
    }

    /// <summary>
    /// 狡诈药水会给几张「小刀+」、每张多少伤害。
    ///
    /// 反编译 `CunningPotion`：`Shiv.CreateInHand(player, DynamicVars.Cards.IntValue)` 之后
    /// 逐个 `CardCmd.Upgrade(current)` ⇒ **升级版小刀**；`Shiv` 的伤害是 `DamageVar(4)`、升级 +2 ⇒ **6**。
    /// 张数读药水自己的 `Cards` 变量（默认 3）。
    /// </summary>
    private static (int Damage, int Count) CunningShivs(PotionModel potion)
    {
        int count = 3;
        try { count = Math.Max(1, (int)potion.DynamicVars.Cards.BaseValue); } catch { /* 用 3 */ }

        int damage = 6;
        try
        {
            var canonical = MegaCrit.Sts2.Core.Models.ModelDb.GetById<CardModel>(
                MegaCrit.Sts2.Core.Models.ModelDb.GetId(typeof(MegaCrit.Sts2.Core.Models.Cards.Shiv)));
            if (canonical != null)
            {
                var upgraded = canonical.ToMutable() ?? canonical;
                if (!upgraded.IsUpgraded) upgraded.UpgradeInternal();
                int value = (int)Math.Round(upgraded.DynamicVars.Damage.BaseValue);
                if (value > 0) damage = value;
            }
        }
        catch { /* 读不到就用 6（4 + 升级 2） */ }

        return (damage, count);
    }

    /// <summary>意图节点要的「目标列表」：她的目标是真人玩家。</summary>
    private static System.Collections.Generic.IEnumerable<Creature> TargetsOf(CharBossMonster boss)
    {
        var list = new System.Collections.Generic.List<Creature>();
        try
        {
            foreach (var player in BossLockOn.RealPlayers(boss?.CombatState))
            {
                if (player?.Creature != null && player.Creature.IsAlive) list.Add(player.Creature);
            }
        }
        catch { /* 忽略 */ }
        if (list.Count == 0 && boss?.Creature != null) list.Add(boss.Creature);
        return list;
    }

    /// <summary>
    /// 这一瓶药水**自己直接打出的伤害**（0 = 不打伤害，例如格挡/强化/镣铐药水，
    /// 以及「狡诈药水」那种靠**生成的牌**去打、不算它自己的）。
    ///
    /// 反编译（原版 `PotionModel.OnUse`）：
    ///   · 爆裂安瓿 <c>ExplosiveAmpoule</c> —— `DamageVar(10, Unpowered)`，目标是
    ///     `CombatState.HittableEnemies`（对她来说 = 玩家侧全体）；
    ///   · 火焰药水 <c>FirePotion</c> —— `DamageVar(20, Unpowered)`，**单体**。
    /// </summary>
    internal static int DirectDamage(PotionModel? potion)
    {
        if (potion == null) return 0;
        try
        {
            var type = potion.GetType();
            bool area = type == typeof(ExplosiveAmpoule);
            bool single = type == typeof(FirePotion);
            if (!area && !single) return 0;
            try { return Math.Max(0, (int)Math.Round((double)potion.DynamicVars.Damage.BaseValue)); }
            catch { return area ? 10 : 20; }        // 读不到就用卡面值
        }
        catch { return 0; }
    }

    /// <summary>
    /// 这一瓶药水是不是**群攻**（打全体玩家）。给「无来源伤害」补算用：
    /// 群攻每个玩家都该算，单体只有被点的那位算。
    /// </summary>
    internal static bool HitsAllPlayers(PotionModel? potion)
    {
        try { return potion != null && potion.GetType() == typeof(ExplosiveAmpoule); }
        catch { return false; }
    }

    /// <summary>
    /// 她**本回合计划要喝**的药水里，会直接造成伤害的那几瓶一共打多少。
    /// 分成「群攻（每个人都挨）」与「单体（只有被点的那位挨）」两块返回，
    /// 供 <see cref="Core.MintySpireDamagePreviewCompat"/> 补进薄荷尖塔的伤害预测。
    ///
    /// 口径与药水栏意图完全一致：只认 `PlannedPotions` 里点名的、且**栏位里真的有**的那几瓶
    /// （`DrinkPlannedPotionsAsync` 找不到就不喝 ⇒ 这里也不算）。
    /// </summary>
    internal static (int Area, int Single) PlannedDirectDamage(CharBossMonster? boss)
    {
        try
        {
            var planned = boss?.PlannedPotions;
            if (planned == null || planned.Count == 0) return (0, 0);
            var shadow = boss!.Shadow;
            int area = 0, single = 0;
            foreach (var potion in BossPotions.Held(shadow))
            {
                if (potion == null) continue;
                bool willDrink;
                try { willDrink = planned.Contains(potion.GetType()); }
                catch { willDrink = false; }
                if (!willDrink) continue;

                int damage = DirectDamage(potion);
                if (damage <= 0) continue;
                if (HitsAllPlayers(potion)) area += damage;
                else single += damage;
            }
            return (area, single);
        }
        catch { return (0, 0); }
    }
}
