using System;
using System.Collections.Generic;
using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.UI;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Combat;
using CharacterBosses.Core;
using CharacterBosses.Monsters;

namespace CharacterBosses.UI;

/// <summary>
/// 手牌栏里「一个格子」的构建：一张卡面 + 它下方一排意图（一张牌可能有多个效果，
/// 例如痛击 = 攻击 8 + 2 层易伤，就会显示两个图标）。
/// 拆出来是因为这里有三个必须记住的坑（都写在注释里），与面板的布局逻辑关系不大。
/// </summary>
public static partial class BossHandCard
{
    // 卡面缩放：0.34 → 显示尺寸 ≈ 95×133（原版手牌是 1.0，Boss 这边要同时塞下遗物条/意图/球位）
    public const float CardScale = 0.34f;
    public const float NativeWidth = 280f;        // 卡牌原始尺寸（缩放前的坐标系）
    public const float NativeHeight = 392f;
    public const string IntentsRowName = "Intents";


    // 悬停放大 / 拆格子时的 MouseExited 见 BossHandCard.Hover.cs（用户明确要求的功能）

    /// <summary>在 <paramref name="parent"/>（必须已经在场景树里）里建一个格子。</summary>
    /// <param name="bright">
    /// 画亮还是压暗。**不等同于 <paramref name="planned"/> != null**：
    /// 所向无敌是「展示牌 + 回合结束免费打出」，不在 Plan 里但该亮（用户实测要求）。
    /// </param>
    public static Control BuildCell(Control parent, CharBossMonster boss, CardModel card, PlannedPlay? planned,
                                    bool bright)
    {
        var cell = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        // 先把 cell 挂进树：子节点的 _Ready 才会跑（NCard 的视觉、NIntent 的内部字段都靠它）
        parent.AddChild(cell);
        BuildCardFace(cell, card, planned, bright);
        BuildIntentRow(cell, boss, planned);
        return cell;
    }

    /// <summary>卡面本体。悬停放大（WireHover）在 BossHandCard.Hover.cs 里。</summary>
    ///
    /// ★ v0.2.791（玩家反馈 2026-11-19：「装了卡厄斯皮肤会看不到 boss 手牌」）：
    ///   卡面完全依赖 `NCard.Create` + `UpdateVisuals`，而**卡图/皮肤类模组**（例如工坊的
    ///   「亡灵契约者卡图 卡厄斯修女奥尔莱亚」）正是接管这两步去换贴图的 —— 一旦它在我们的
    ///   语境里抛异常，这里以前只打一行 `NCard.Create failed: …` 然后**什么都不画**
    ///   ⇒ 玩家看到的就是「看不到 boss 手牌」。
    ///   现在三层兜底：
    ///     ① 原版路径（Create + UpdateVisuals）；
    ///     ② 换一张新节点、**跳过 UpdateVisuals**（皮肤换图那步不做，至少卡牌本体还在）；
    ///     ③ 还不行 ⇒ 画一块**纯文本占位**（卡牌 id），保证玩家总能看见她手里有什么牌。
    ///   每次都把「哪一步、什么异常」记一行日志，下次反馈一眼能定位是哪个模组掐的。
    private static void BuildCardFace(Control cell, CardModel card, PlannedPlay? planned, bool bright)
    {
        string entry = SafeEntry(card);

        string? why = null;
        if (TryBuildCardNode(cell, card, planned, bright, skipVisuals: false, ref why))
        {
            FaceFailedEntries.Remove(entry);
            return;
        }

        if (TryBuildCardNode(cell, card, planned, bright, skipVisuals: true, ref why))
        {
            if (FaceFailedEntries.Add(entry))
                Logger.Log($"[CharBoss] 手牌卡面：{entry} 的「刷新视觉」这一步被别的模组掐了" +
                           $"（{why}）⇒ 已退回「只放卡牌本体、不刷新贴图」的画法" +
                           "（多半是卡图/皮肤类模组改了 NCard）");
            return;
        }

        if (FaceFailedEntries.Add(entry))
            Logger.Log($"[CharBoss] 手牌卡面渲染失败：{entry}（{why}）⇒ 改成文本占位" +
                       "（多半是卡图/皮肤类模组改了 NCard；把这行发我就能定位）");
        BuildTextFallback(cell, entry);
    }

    /// <summary>已经退到「备用画法」的卡（每个 id 只打一行日志）。</summary>
    private static readonly HashSet<string> FaceFailedEntries = new(StringComparer.Ordinal);

    /// <summary>建一张卡面并刷新视觉；成功返回 true。失败时 <paramref name="why"/> 里是原因。</summary>
    private static bool TryBuildCardNode(Control cell, CardModel card, PlannedPlay? planned, bool bright,
                                         bool skipVisuals, ref string? why)
    {
        try
        {
            var cardNode = NCard.Create(card, ModelVisibility.Visible);
            if (cardNode == null) { why = "NCard.Create 返回 null"; return false; }

            cell.AddChild(cardNode);
            WireHover(cell, cardNode);
            // 池子里回收的 NCard 尺寸被重置成 0，而卡面内部节点是按锚点撑满父级的
            // —— 父级 0 尺寸会让整张卡塌成看不见，必须显式给回卡牌原始尺寸。
            cardNode.Size = new Vector2(NativeWidth, NativeHeight);
            cardNode.Scale = new Vector2(CardScale, CardScale);
            // 要接收鼠标事件才能做「悬停放大」（用户要求的功能）
            cardNode.MouseFilter = Control.MouseFilterEnum.Stop;
            cardNode.Visible = true;

            // 要打的牌（含「回合结束会自动打出」的展示牌）保持原色；真打不出的压暗
            cardNode.Modulate = bright
                ? Colors.White
                : new Color(0.42f, 0.42f, 0.48f, 0.85f);

            // 卡面上的伤害/格挡数字是「对着目标算出来的」：
            // 不告诉节点预览目标，「欺凌」这类按目标易伤层数加伤的牌只会显示基础值。
            // 注意 CardModel.CurrentTarget 是只读的，得走 NCard.SetPreviewTarget。
            if (planned != null)
            {
                try { cardNode.SetPreviewTarget(planned.Target); }
                catch (Exception e) { why = "SetPreviewTarget: " + e.Message; }
            }

            // Create 只从池子里取节点并设 Model，不会刷视觉，必须显式刷一次
            //（这一步正是「卡图/皮肤模组」接管的地方 ⇒ 单独兜一层，它炸了也别把整张卡丢掉）
            if (!skipVisuals)
            {
                try { cardNode.UpdateVisuals(PileType.Hand, CardPreviewMode.Normal); }
                catch (Exception e)
                {
                    why = (why == null ? string.Empty : why + " / ") +
                          "UpdateVisuals: " + e.GetType().Name + ": " + e.Message;
                    return false;
                }
            }
            return true;
        }
        catch (Exception e)
        {
            why = (why == null ? string.Empty : why + " / ") + e.GetType().Name + ": " + e.Message;
            return false;
        }
    }

    /// <summary>最后的兜底：一块纯文本占位（卡牌 id），保证手牌栏不是空的。</summary>
    private static void BuildTextFallback(Control cell, string entry)
    {
        try
        {
            var panel = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            cell.AddChild(panel);
            panel.Size = new Vector2(NativeWidth * CardScale, NativeHeight * CardScale);
            panel.Position = Vector2.Zero;

            var label = new Label
            {
                Text = entry,
                MouseFilter = Control.MouseFilterEnum.Ignore,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
            };
            label.AddThemeFontSizeOverride("font_size", 18);
            panel.AddChild(label);
        }
        catch { /* 连占位都建不出来就算了，绝不影响战斗 */ }
    }

    private static string SafeEntry(CardModel? card)
    {
        try { return card?.Id.Entry ?? "?"; } catch { return "?"; }
    }

    /// <summary>
    /// 意图排成一行放在卡面正下方：**与卡牌左侧对齐**，顶端贴着卡牌底端（由面板负责定位）。
    /// 只有「本回合要打」的牌才有。
    /// </summary>
    internal static void BuildIntentRow(Control cell, CharBossMonster boss, PlannedPlay? planned)
    {
        if (planned == null) return;
        // 意图一条都没有、但有「天际钻头预告」的牌（星位序列）也要建这一行 —— 否则标签没地方挂
        if (planned.Intents.Count == 0 && planned.DrillPerHit <= 0) return;

        var row = new HBoxContainer
        {
            Name = IntentsRowName,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Alignment = BoxContainer.AlignmentMode.Begin,   // 左对齐卡牌
        };
        // 收紧同一张牌内多个意图的间距（用户：原来太散，容易压到右侧卡牌的意图上）
        row.AddThemeConstantOverride("separation", BossHandPanel.IntentSeparation);
        cell.AddChild(row);

        var targets = new List<Creature> { planned.Target };

        // 顺序（用户 2026-10-29）：「**防御排最后**，否则翻越撑击的伤害数字会被挡住」。
        // 意图行是从卡牌左侧往右排的，越靠右越容易被右边的卡面/邻座面板压住；
        // 格挡图标只有一个箭头、几乎不带数字信息，压住它损失最小 ⇒ 把所有 `DefendIntent`
        // 挪到这一行的末尾（其余意图保持原顺序：先牌本身的、再激发球的）。
        var ordered = new List<AbstractIntent>(planned.Intents.Count);
        // ★ 2026-11-10：只有**这一行里有攻击意图**时才需要把守势挪到最后（原意是不让格挡挡住伤害数字）。
        //   三层「抉择储」的候选牌是「守势+强化」这种纯功能行，用户要的就是守势在前 ⇒ 保持原顺序。
        bool hasAttack = false;
        foreach (var intent in planned.Intents)
        {
            if (intent is MegaCrit.Sts2.Core.MonsterMoves.Intents.AttackIntent
                || intent is MegaCrit.Sts2.Core.MonsterMoves.Intents.MultiAttackIntent) { hasAttack = true; break; }
        }
        if (!hasAttack)
        {
            ordered.AddRange(planned.Intents);
        }
        else
        {
            foreach (var intent in planned.Intents)
            {
                if (intent is DefendIntent) continue;
                ordered.Add(intent);
            }
            foreach (var intent in planned.Intents)
            {
                if (intent is DefendIntent) ordered.Add(intent);
            }
        }

        foreach (var intent in ordered)
        {
            AddIntent(boss, intent, targets, row);
        }
        // ★ 彩虹机（用户 2026-11-07）：「会触发节拍器的那张牌」下面再标一条**30 无来源群伤**。
        //   游戏里这 30 点确实会打出来（节拍器第 5 颗球），以前意图上完全看不见。
        try { AddExtraAoeLabel(boss, planned, row); } catch (Exception e) { Logger.Log("[CharBoss] 额外群伤标记失败: " + e.Message); }
        // ★ 2026-11-10（三层「抉择储」）：「星位序列」下面画出「选它之后天际钻头会多打多少」，
        //   例如第一回合 **8×12** —— 用的是**原版的意图节点**（`MultiAttackIntent` → `NIntent`），
        //   不是自造的 Label（用户 2026-11-10：「你用原版的，别自己瞎搞」）。
        //   ⚠ 但它**不进** `planned.Intents` ⇒ 也就不会进原版 `MoveState.Intents`，
        //   薄荷尖塔（读 `NextMove.Intents` 统计本回合总伤害）不会把它算进去（用户要求只改这一点）。
        try { AddDrillGainIntent(boss, planned, targets, row); }
        catch (Exception e) { Logger.Log("[CharBoss] 天际钻头预告意图失败: " + e.Message); }
        try { ApplyIntentShift(boss, row); } catch { /* 忽略 */ }
        try { ApplyCrowdedIntentFade(boss, planned, row); } catch { /* 忽略 */ }

        // 激发球的意图（攻势 / `伤害×次数` / 守势）已经在计划阶段由 BossOrbSim 算好并塞进 Intents，
        // 这里不再自己拼 —— 顺序才是对的（先牌本身的意图，再激发的意图）。
    }

    /// <summary>
    /// **Boss ≥ 4 只时，本回合要打 4 张以上牌的 Boss，其意图行半透明**（用户 2026-11-28 二次订正）：
    /// > 「大于等于 4 个 boss 时，除了最左侧 boss，其它 boss 的**意图中的牌数量大于 3** 时**卡牌**应该半透明」
    ///
    /// ⇒ 判据（不是「第 4 张及以后」，而是**整只 Boss 的意图行**）：
    ///   1. 场上活着的我们的 Boss **≥ 4 只**（原来我写成 >4，错了）；
    ///   2. 这只 Boss 本回合计划**超过 3 张牌**（`boss.Plan.Count > 3`），
    ///      —— 原来我用「这一格对应第几张牌」判，而意图行常常来自 `DisplayPlays`（所向无敌等），
    ///         `Plan` 里找不到那张牌 ⇒ index = -1 ⇒ **一次都没生效**（用户：「似乎没有实现」）；
    ///   3. **最左侧那只 Boss 不做**（用户要求）；
    ///   4. 半透明作用在该 Boss **所有**意图行上（每张牌下面那一行都设 alpha 0.45）。
    /// 只在重建意图行时算一次，不是每帧。
    /// </summary>
    internal static void ApplyCrowdedIntentFade(CharBossMonster boss, PlannedPlay? planned, Control row)
    {
        try
        {
            if (boss == null || row == null || !GodotObject.IsInstanceValid(row)) return;
            if (AliveBossCount(boss) < 4) return;              // ≥4 只才启用

            int plannedCards = 0;
            try { plannedCards = boss.Plan?.Count ?? 0; } catch { plannedCards = 0; }
            if (plannedCards <= 3) return;                      // 只糊「这一回合打 4 张以上」的 Boss

            if (IsLeftmostBoss(boss)) return;                   // 最左侧那只不受限

            row.Modulate = new Color(1f, 1f, 1f, 0.45f);
        }
        catch { /* 纯表现层，出问题就按原样显示 */ }
    }

    /// <summary>场上**活着的**我们的 Boss 有几只（拿不到返回 0 ⇒ 不启用半透明）。</summary>
    private static int AliveBossCount(CharBossMonster boss)
    {
        try
        {
            var combat = boss.CombatState;
            if (combat == null) return 0;
            int n = 0;
            foreach (var c in combat.Enemies)
            {
                if (c?.Monster is not CharBossMonster) continue;
                if (!c.IsAlive || c.IsDead) continue;
                n++;
            }
            return n;
        }
        catch { return 0; }
    }

    /// <summary>这只 Boss 是不是场上**最靠左**的那只（按生物节点的屏幕 X）。</summary>
    private static bool IsLeftmostBoss(CharBossMonster boss)
    {
        try
        {
            float? mine = ScreenX(boss.Creature);
            if (mine == null) return false;                    // 量不到 ⇒ 当成「不是最左」（保守：宁可半透明）

            var combat = boss.CombatState;
            if (combat == null) return true;
            foreach (var c in combat.Enemies)
            {
                if (c?.Monster is not CharBossMonster other) continue;
                if (ReferenceEquals(other, boss)) continue;
                if (!c.IsAlive || c.IsDead) continue;
                float? x = ScreenX(c);
                if (x != null && x.Value < mine.Value - 1f) return false;
            }
            return true;
        }
        catch { return false; }
    }

    private static float? ScreenX(Creature? creature)
    {
        try
        {
            if (creature == null) return null;
            var node = MegaCrit.Sts2.Core.Nodes.Rooms.NCombatRoom.Instance?.GetCreatureNode(creature);
            if (node == null || !GodotObject.IsInstanceValid(node)) return null;
            return node.GlobalPosition.X;
        }
        catch { return null; }
    }

    /// <summary>这一回合的意图栏整体左移（用户 2026-11-07：彩虹机第 7/12 回合意图太多被屏幕边缘切掉）。</summary>
    private static void ApplyIntentShift(CharBossMonster boss, Control row)
    {
        try
        {
            int shift = Core.RainbowMimic.IntentShiftFor(boss);
            if (shift <= 0) return;
            row.Position = new Vector2(row.Position.X - shift, row.Position.Y);
        }
        catch { /* 忽略：挪不动就按原位显示 */ }
    }
    /// <summary>
    /// 「这张牌还会额外造成 N 点**无来源群体伤害**」的小标记（当前只有彩虹机第 8 回合那张「彩虹+」）。
    /// 画成红底小方块 + 数字，并带悬停说明；位置照旧在卡面下方那一行的末尾。
    /// </summary>
    private static void AddExtraAoeLabel(CharBossMonster boss, PlannedPlay planned, Control row)
    {
        int aoe = Core.RainbowMimic.ExtraAoeFor(boss, planned?.Card);
        if (aoe <= 0) return;
        var label = new Label
        {
            Text = aoe.ToString(),
            MouseFilter = Control.MouseFilterEnum.Stop,
            TooltipText = "无来源群体伤害（节拍器）：对所有敌人造成 " + aoe + " 点伤害，"
                        + "不吃力量/集中，也不会被「为你而死」吸收",
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            CustomMinimumSize = new Vector2(30f, 0f),        // 不给最小宽度的话会被压成 0 宽 ⇒ 看不见
        };
        label.AddThemeColorOverride("font_color", new Color(1f, 0.32f, 0.32f));
        label.AddThemeFontSizeOverride("font_size", 22);      // 默认字号太小，和意图图标不搭
        row.AddChild(label);
    }

    /// <summary>
    /// 三层「抉择储」：「**星位序列**」这一格下面画出「选它之后天际钻头会**多打**多少」，形如 <c>8×12</c>
    /// （用户 2026-11-10：「例如第一回合星位序列下面就显示 8*12」）。
    ///
    /// 用户 2026-11-10 第二轮口径：「**你用原版的，别自己瞎搞。只改一下不让薄荷尖塔记录就行了**」
    /// ⇒ 这里直接用原版 <c>MultiAttackIntent</c> 走同一个 <see cref="AddIntent"/>
    /// （和别的意图一模一样的节点/图标/字体），**不用自造的 Label**。
    ///
    /// 唯一「不让薄荷尖塔记录」的做法：它**不放进 `PlannedPlay.Intents`**
    /// ⇒ 也就不会被 <c>CharBossMonster.PublishVanillaIntents</c> 写进原版 `MoveState.Intents`，
    /// 而薄荷尖塔只读那里（见 <c>Core/MintySpireDamagePreviewCompat</c> 顶部注释）⇒ 不会重复统计。
    /// </summary>
    private static void AddDrillGainIntent(CharBossMonster boss, PlannedPlay? planned,
                                           List<Creature> targets, Control row)
    {
        if (planned == null || planned.DrillPerHit <= 0) return;
        int hits = Math.Max(0, planned.DrillGainHits);
        if (hits <= 0) return;

        AddIntent(boss, new MegaCrit.Sts2.Core.MonsterMoves.Intents.MultiAttackIntent(
                            planned.DrillPerHit, hits), targets, row);
    }

    /// <summary>
    /// **原地刷新**某一格的意图行（用户 2026-10-26：出牌时整排重建会闪、会卡）。
    /// 只销毁并重建这一格的「意图行」子节点，**卡面 NCard 节点保留不动** ⇒ 卡面不再闪。
    /// 任何异常都退化成「不动」（下次整排重建会补齐）。
    /// </summary>
    internal static void RefreshIntents(Control cell, CharBossMonster boss, PlannedPlay? planned)
    {
        try
        {
            if (cell == null || !GodotObject.IsInstanceValid(cell)) return;
            foreach (var child in cell.GetChildren())
            {
                if (child is not Control c || !GodotObject.IsInstanceValid(c)) continue;
                if (c.Name != IntentsRowName) continue;
                cell.RemoveChild(c);
                c.QueueFree();
                break;
            }
            BuildIntentRow(cell, boss, planned);
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 原地刷新意图失败（忽略）: " + e.Message);
        }
    }

    private static void AddIntent(CharBossMonster boss, AbstractIntent intent, List<Creature> targets, Control row)
    {
        NIntent? node = null;
        try
        {
            node = NIntent.Create(0f);
            if (node != null)
            {
                // 必须先入树（_Ready 建好内部节点）再 UpdateIntent，否则空引用
                row.AddChild(node);
                node.UpdateIntent(intent, targets, boss.Creature);
                // 「牺牲」= 强化的蓝箭头翻转；「诱惑」= 策略的红箭头翻转（用户 2026-11-13）
                if (intent is SacrificeIntent || intent is TemptationIntent) FlipIconDown(node);
                return;
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] NIntent failed, falling back to label: " + e.Message);
            if (node != null && GodotObject.IsInstanceValid(node)) node.QueueFree();
        }

        try
        {
            var label = new Label
            {
                Text = IntentText(intent, boss, targets),
                MouseFilter = Control.MouseFilterEnum.Ignore,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            label.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.4f));
            row.AddChild(label);
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] intent label failed: " + e.Message);
        }
    }

    /// <summary>
    /// 「牺牲」意图的图标 = 强化那个蓝色向上箭头**上下翻转**（用户指定）。
    /// <c>NIntent</c> 的图标就是个 <c>Sprite2D</c> 字段，翻 <c>FlipV</c> 即可 ——
    /// 不动数字标签、不用自造美术。
    /// </summary>
    private static void FlipIconDown(NIntent node)
    {
        try
        {
            if (IntentSpriteField?.GetValue(node) is Sprite2D sprite)
            {
                sprite.FlipV = true;
                // 翻转后视觉重心可能和强化箭头对不齐 → 给一个可实机调的偏移
                float gap = BossUiTuning.SacrificeIconY;
                if (Math.Abs(gap) > 0.01f) sprite.Position = sprite.Position + new Vector2(0f, gap);
            }
            else Logger.Log("[CharBoss] 牺牲意图：拿不到 _intentSprite，图标未能翻转");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 牺牲意图图标翻转失败: " + e.Message);
        }
    }

    private static readonly System.Reflection.FieldInfo? IntentSpriteField =
        typeof(NIntent).GetField("_intentSprite",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

    /// <summary>意图节点建不出来时的退路：用文字（「攻势 4」这种）表示。</summary>
    private static string IntentText(AbstractIntent intent, CharBossMonster boss, List<Creature> targets)
    {
        string label;
        try { label = intent.GetIntentLabel(targets, boss.Creature).GetFormattedText(); }
        catch { label = intent.IntentType.ToString(); }

        int damage = intent switch
        {
            SingleAttackIntent single => single.GetTotalDamage(targets, boss.Creature),
            MultiAttackIntent multi => multi.GetTotalDamage(targets, boss.Creature),
            _ => 0,
        };
        return damage > 0 ? $"{label} {damage}" : label;
    }
}
