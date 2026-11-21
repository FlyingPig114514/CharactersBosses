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

            // ★ v0.2.866（用户 2026-11-21：「由于回合开始时狡诈药水不存在，所以不能显示其意图。
            //   改为在格挡药水左侧的位置写上狡诈药水的意图…在狡诈药水被炼出来后改为把意图显示在狡诈药水上」）：
            //   第 8 回合（含循环）二阶段的「狡诈药水」是**混沌药水喝下去之后**才补进栏位的，
            //   回合开始时那个格子里根本没有它 ⇒ `held` 里没有它 ⇒ 意图压根画不出来。
            //   现在往显示列表里插一个**占位格**（真的狡诈药水贴图 + 同样的意图，画成半透明表示"还没炼出来"），
            //   位置放在「格挡药水」左边（没有格挡药水就放最左）；等它真补进栏位之后，
            //   占位格自然消失、意图就直接画在真的那一格上（两条路都用同一个 `IntentFor`，数字一致）。
            var display = new List<(PotionModel? Potion, bool Placeholder)>();
            foreach (var p in held) display.Add((p, false));

            try
            {
                bool stunned = boss!.Stunned;
                bool wantsBronze = planned.Contains(typeof(LiquidBronze));
                bool hasBronze = false;
                foreach (var p in held) if (p != null && p.GetType() == typeof(LiquidBronze)) { hasBronze = true; break; }
                // 与 `BossPotions.DrinkAsync` 里混沌药水的判据保持一致：栏位里**还有**瓶中精灵 ⇒
                // 混沌补回来的是精灵、不会出现狡诈药水 ⇒ 这时不画占位（一阶段就该没有这个意图）。
                bool hasFairy = false;
                foreach (var p in held) if (p is FairyInABottle) { hasFairy = true; break; }
                if (wantsBronze && !hasBronze && !hasFairy && !stunned)
                {
                    var canonical = BossPotions.CanonicalPotion(typeof(LiquidBronze));
                    if (canonical != null)
                    {
                        int at = 0;
                        for (int i = 0; i < display.Count; i++)
                        {
                            if (display[i].Potion != null && display[i].Potion.GetType() == typeof(BlockPotion)) { at = i; break; }
                        }
                        display.Insert(at, (canonical, true));
                        Logger.Log($"[CharBoss] 药水栏：本回合会喝到「流动铜液」但它还没炼出来 ⇒ 在格挡药水左侧补一个占位意图" +
                                   $"（位置 {at}，本回合已喝 {boss.PotionsDrunkThisTurn} 瓶）");
                    }
                }
            }
            catch (Exception e) { Logger.Log("[CharBoss] 药水占位格失败（忽略）: " + e.Message); }

            for (int i = 0; i < display.Count; i++)
            {
                var potion = display[i].Potion;
                if (potion == null) continue;

                var cell = MakeCell(i, potion);
                row.AddChild(cell);                       // ← ① 先入树（NIntent 的 _Ready 要跑）
                cells.Add(cell);

                if (display[i].Placeholder)
                {
                    // 还没炼出来：压淡一点，玩家一眼能看出「这瓶现在不在她手里」
                    cell.Modulate = new Color(1f, 1f, 1f, 0.55f);
                }

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
            node.UpdateIntent(IntentFor(potion, boss), TargetsOf(boss), boss.Creature);

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
    private static MegaCrit.Sts2.Core.MonsterMoves.Intents.AbstractIntent IntentFor(PotionModel potion, CharBossMonster? boss = null)
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
            // ★ v0.2.866（用户 2026-11-21：「受力量虚弱等加成，要考虑回合内喝药触发爬行动物饰品增加的力量
            //   （也可能已经算上了，别重复计算）」）：爬行动物饰品每喝一瓶 +3 **临时力量**
            //   （反编译 `ReptileTrinket.AfterPotionUsed` → `ReptileTrinketPower`，它是 `TemporaryStrengthPower`）。
            //   只把「**排在这瓶之前、还没喝掉**」的那几瓶算进来 —— 已经喝掉的那几瓶真实力量里已经有了
            //   （意图节点自己会加当前力量）⇒ 不双算。于是回合开始时（一瓶都没喝）显示的数
            //   就等于「最后那一口真正打出来的数」。
            // ★ v0.2.867（用户 2026-11-21）：按**实际结算顺序**把整笔算出来，别再交给意图节点：
            //   → 原版 `AttackIntent.Multi(damage, hits)` 就是直接显示我们给的数字（不会自己加力量/易伤）
            //   口径（用户给的公式）：
            //     (6 基础 + 本回合爬行动物饰品给的力量 + 药水自己给的力量 + 当前真实力量)
            //       × 虚弱 × 易伤  … 再 + 群蛇形态每张牌那一下（普通 4 / 恐惧 6，不吃力量虚弱易伤）
            //   然后 × 3 把刀；被锁玩家有无实体 ⇒ 每段 1 点、段数变成 3 刀 + 3 次群蛇 = 6 段（1×6）。
            var (perHit, hits, detail) = CunningShivPreview(potion!, boss);
            if (_trinketLogs++ < 10)
            {
                Logger.Log($"[CharBoss] 狡诈药水意图：{hits} 段 × {perHit} = {perHit * hits}（{detail}）");
            }
            return AttackIntent.Multi(perHit, hits);
        }

        if (type == typeof(ShacklingPotion))
            return new MegaCrit.Sts2.Core.MonsterMoves.Intents.DebuffIntent(false);

        return new MegaCrit.Sts2.Core.MonsterMoves.Intents.BuffIntent();
    }

    /// <summary>
    /// ★ v0.2.867：**狡诈药水的完整伤害预览**（用户 2026-11-21 给的公式）。
    ///
    /// <code>
    /// 每段 = ((6 + 本回合爬行动物饰品力量 + 药水自己给的力量 + 当前真实力量) × 虚弱 × 易伤)
    ///        + 群蛇形态每张牌那一下（普通 4 / 恐惧 6，ValueProp.Unpowered ⇒ 不吃力量/虚弱/易伤）
    /// 合计 = 每段 × 3 把刀
    /// 被锁玩家有「无实体」⇒ 每段 1 点，段数 = 3 刀 + 3 次群蛇 = 6（显示 1×6）
    /// </code>
    ///
    /// 力量的三部分：
    ///   · 当前真实 `StrengthPower`：她身上已经有的（含本回合已经喝掉的药水带来的）；
    ///   · 爬行动物饰品：`ReptileTrinketPower`（`TemporaryStrengthPower`，每喝一瓶 +3）——
    ///     只算「排在这瓶之前、还没喝掉」的那几瓶；
    ///   · 药水自己给的力量：`FyshOil`（异鱼之油）的 `Strength` 变量，同样只算还没喝掉的那几瓶。
    /// </summary>
    private static (int PerHit, int Hits, string Detail) CunningShivPreview(PotionModel potion, CharBossMonster? boss)
    {
        try
        {
            var (baseDamage, shivs) = CunningShivs(potion);
            if (shivs <= 0) shivs = 3;

            Creature? self = boss?.Creature;
            Creature? target = null;
            try { target = boss != null ? BossLockOn.TargetFor(boss)?.Creature : null; } catch { target = null; }

            // 当前真实力量 / 虚弱 / 目标易伤
            int strengthNow = 0, weak = 0, vuln = 0, serpent = 0;
            try { if (self != null) strengthNow = (int)Math.Round((double)self.GetPowerAmount<MegaCrit.Sts2.Core.Models.Powers.StrengthPower>()); } catch { }
            try { if (self != null) weak = (int)Math.Round((double)self.GetPowerAmount<MegaCrit.Sts2.Core.Models.Powers.WeakPower>()); } catch { }
            try { if (target != null) vuln = (int)Math.Round((double)target.GetPowerAmount<MegaCrit.Sts2.Core.Models.Powers.VulnerablePower>()); } catch { }
            try { if (self != null) serpent = (int)Math.Round((double)self.GetPowerAmount<MegaCrit.Sts2.Core.Models.Powers.SerpentFormPower>()); } catch { }

            var (pending, pendingDetail) = PendingPotionStrength(potion, boss);

            decimal value = baseDamage + strengthNow + pending;
            int beforeMul = (int)value;
            if (weak > 0) value = Math.Floor(value * 0.75m);
            if (vuln > 0) value = Math.Floor(value * 1.5m);
            int per = (int)value;

            // 无实体：每段 1 点，段数把群蛇那几下也算进去（3 刀 + 3 下群蛇 = 6）
            bool intangible = false;
            try { intangible = target != null && target.GetPower<MegaCrit.Sts2.Core.Models.Powers.IntangiblePower>() != null; } catch { }
            if (intangible)
            {
                int hitsI = shivs + (serpent > 0 ? shivs : 0);
                return (1, hitsI,
                        $"无实体 ⇒ 每段 1 点 × {hitsI}（{shivs} 刀 + {shivs} 次群蛇）；" +
                        $"不吃力量/虚弱/易伤");
            }

            per += serpent;
            string detail = $"基础 {baseDamage} + 当前力量 {strengthNow} + 未喝药水 {pending}（{pendingDetail}）" +
                            $" = {beforeMul}" +
                            (weak > 0 ? " ×0.75(虚弱)" : string.Empty) +
                            (vuln > 0 ? " ×1.5(易伤)" : string.Empty) +
                            $" = {per - serpent}" +
                            (serpent > 0 ? $" + 群蛇形态 {serpent} = {per}" : string.Empty) +
                            $" × {shivs} 刀";
            return (per, shivs, detail);
        }
        catch
        {
            var (baseDamage, shivs) = CunningShivs(potion);
            return (baseDamage, Math.Max(1, shivs), "计算失败，退回基础值");
        }
    }

    /// <summary>
    /// ★ v0.2.867：本回合**还没喝**的那几瓶药水一共会给多少力量
    /// （爬行动物饰品每瓶 +3，`FyshOil` 异鱼之油给它的 `Strength` 变量）。
    /// 只算「排在这瓶药水之前、还没喝掉」的 —— 已经喝掉的力量已经在她身上了。
    /// </summary>
    private static (int Total, string Detail) PendingPotionStrength(PotionModel potion, CharBossMonster? boss)
    {
        int trinket = PendingTrinketStrength(potion, boss);
        int oil = 0;
        try
        {
            var planned = boss?.PlannedPotions;
            if (planned != null && potion != null)
            {
                int index = -1;
                var mine = potion.GetType();
                for (int i = 0; i < planned.Count; i++) { if (planned[i] == mine) { index = i; break; } }
                if (index >= 0)
                {
                    int drunk = Math.Max(0, boss!.PotionsDrunkThisTurn);
                    int perOil = 0;
                    try
                    {
                        var canonical = BossPotions.CanonicalPotion(typeof(FyshOil));
                        if (canonical != null && canonical.DynamicVars.TryGetValue("Strength", out var v) && v != null)
                            perOil = Math.Max(0, (int)Math.Round((double)v.BaseValue));
                    }
                    catch { /* 读不到就按 0 算 */ }
                    if (perOil > 0)
                    {
                        for (int i = drunk; i < index; i++)
                        {
                            if (planned[i] == typeof(FyshOil)) oil += perOil;
                        }
                    }
                }
            }
        }
        catch { /* 忽略 */ }
        return (trinket + oil, $"饰品 {trinket} + 异鱼之油 {oil}");
    }
    /// <summary>「狡诈药水意图里算了多少临时力量」这条日志最多打几次。</summary>
    private static int _trinketLogs;

    /// <summary>「药水直伤（含狡诈药水小刀）」这条日志最多打几次。</summary>
    private static int _directLogs;

    /// <summary>
    /// ★ v0.2.866：**爬行动物饰品**（`REPTILE_TRINKET`）在回合内喝药给的**临时力量**里，
    /// 有多少还没到账但一定会到账（= 排在这瓶药水之前、还没喝掉的那几瓶 × 每瓶的层数）。
    ///
    /// 反编译：`ReptileTrinket.AfterPotionUsed` → `PowerCmd.Apply&lt;ReptileTrinketPower&gt;(…, Amount)`，
    /// 而 `ReptileTrinketPower : TemporaryStrengthPower` ⇒ 每喝一瓶 +N 力量（本回合）。
    /// 已经喝掉的那几瓶不进这个数 —— 它们的真实力量已经在 `Creature` 身上了，
    /// 意图节点渲染时会自己加当前力量，这里再加就双算了（用户 2026-11-21 特别提醒）。
    /// </summary>
    private static int PendingTrinketStrength(PotionModel potion, CharBossMonster? boss)
    {
        try
        {
            if (boss == null || potion == null) return 0;

            // 她得真的带着爬行动物饰品；层数优先读遗物的变量，读不到用反编译出来的 3
            int perPotion = 0;
            var relics = boss.Shadow?.Relics;
            for (int i = 0; i < (relics?.Count ?? 0); i++)
            {
                var relic = relics![i];
                if (relic == null) continue;
                string entry = string.Empty;
                try { entry = relic.Id.Entry; } catch { /* 忽略 */ }
                if (!string.Equals(entry, "REPTILE_TRINKET", StringComparison.Ordinal)) continue;
                perPotion = 3;
                try
                {
                    foreach (var key in new[] { "Strength", "ReptileTrinketPower" })
                    {
                        if (relic.DynamicVars.TryGetValue(key, out var v) && v != null)
                        {
                            int n = (int)Math.Round((double)v.BaseValue);
                            if (n > 0) { perPotion = n; break; }
                        }
                    }
                }
                catch { /* 用 3 */ }
                break;
            }
            if (perPotion <= 0) return 0;

            var planned = boss.PlannedPotions;
            if (planned == null || planned.Count == 0) return 0;

            int index = -1;
            var mine = potion.GetType();
            for (int i = 0; i < planned.Count; i++)
            {
                if (planned[i] == mine) { index = i; break; }
            }
            if (index < 0) return 0;

            int drunk = Math.Max(0, boss.PotionsDrunkThisTurn);
            int pending = index - drunk;
            if (pending <= 0) return 0;
            return pending * perPotion;
        }
        catch { return 0; }
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
            // ★ v0.2.869（用户 2026-11-21 削弱）：二阶段混沌药水补出来的那一瓶从「狡诈药水」换成
            //   **流动铜液**（只给 3 层荆棘、不生成小刀）⇒ 这里不再有「额外的 3 把小刀要计进薄荷尖塔」。
            //   （v0.2.867 那段狡诈药水的加成逻辑已随之移除；荆棘不是伤害，薄菏那侧本来也不需要它。）
            return (area, single);
        }
        catch { return (0, 0); }
    }
}
