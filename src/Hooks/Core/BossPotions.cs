using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Potions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Potions;
using MegaCrit.Sts2.Core.Entities.Cards;
using CharacterBosses.Monsters;

namespace CharacterBosses.Core;

/// <summary>
/// **Boss 的药水**（用户 2026-10-25：「给这个 Boss 制作药水栏位和喝药水的功能」）。
///
/// 影子 Player 是一个真的 <see cref="Player"/>，所以药水可以完全走原版数据结构
/// （`Player.PotionSlots` / `AddPotionInternal` / `RemoveUsedPotionInternal`），
/// 我们只需要做三件事：
///   1. **补栏位**：原版不会替影子玩家给药水栏位（`MaxPotionCount` 是 0）⇒ 按设计补到 N 个；
///   2. **塞药水 / 喝药水**：`Give` / <see cref="DrinkAsync"/>；
///   3. **炼制药水**（`ALCHEMIZE`）要炼指定那一瓶：出牌前把目标记进 <see cref="PendingBrew"/>，
///      由 <c>Patches/AlchemizeBrewPatch</c> 在 `Alchemize.OnPlay` 前缀里换掉「随机」。
///
/// 喝药的目标怎么选：药水自己的 `TargetType` 说了算 ——
/// 单体敌人（`AnyEnemy` 之类）打**真人玩家**，其余（`Self`/`None`/友方）给自己。
/// </summary>
internal static class BossPotions
{
    /// <summary>「这次炼制药水要炼哪一瓶」——出牌前写、前缀里取、取完即清。</summary>
    internal static readonly Dictionary<Player, Type> PendingBrew = new();

    /// <summary>药水增减的版本号（每次 Give / 喝掉 +1）。状态条短路判断用它 ——
    /// 只比瓶数会漏掉「喝一瓶又炼一瓶」（数量不变），那样就不更新了。</summary>
    internal static int Version;

    /// <summary>把影子玩家的药水栏位补到 <paramref name="count"/> 个（多了不削）。</summary>
    internal static void EnsureSlots(Player? shadow, int count)
    {
        if (shadow == null || count <= 0) return;
        try
        {
            int now = shadow.MaxPotionCount;
            if (now >= count) return;
            shadow.AddToMaxPotionCount(count - now);
            Logger.Log($"[CharBoss] 药水栏位：{now} → {shadow.MaxPotionCount}（调制）");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 补药水栏位失败: " + e.Message);
        }
    }

    /// <summary>当前带着的药水（按栏位顺序，跳过空位）。</summary>
    internal static List<PotionModel> Held(Player? shadow)
    {
        var list = new List<PotionModel>();
        if (shadow == null) return list;
        try
        {
            var slots = shadow.PotionSlots;
            for (int i = 0; i < (slots?.Count ?? 0); i++)
            {
                var potion = slots![i];
                if (potion != null) list.Add(potion);
            }
        }
        catch { /* 忽略 */ }
        return list;
    }

    /// <summary>塞一瓶药水进她第一个空栏位；满了两瓶都不留（记日志）。</summary>
    internal static PotionModel? Give(Player? shadow, Type potionType)
    {
        if (shadow == null || potionType == null) return null;
        try
        {
            var canonical = CanonicalPotion(potionType);
            if (canonical == null)
            {
                Logger.Log($"[CharBoss] 药水 {potionType.Name} 取不到原版模型，跳过");
                return null;
            }
            var potion = canonical.ToMutable();
            potion.Owner = shadow;
            var result = shadow.AddPotionInternal(potion, -1, false);
            Version++;
            Logger.Log($"[CharBoss] 获得药水：{SafeName(potion)}（栏位 {shadow.PotionSlots.Count} 个，" +
                       $"现有 {Held(shadow).Count} 瓶，结果 {result}）");
            return potion;
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 给药水失败: " + e.Message);
            return null;
        }
    }

    /// <summary>喝一瓶：选目标 → 走原版 <c>OnUseWrapper</c> → 从栏位移除；喝完整 <c>HealPerPotion</c> 点生命。</summary>
    internal static async Task<bool> DrinkAsync(PlayerChoiceContext ctx, Player? shadow, PotionModel potion,
                                                int healPerPotion, IReadOnlyList<Type>? entropicFill = null)
    {
        if (shadow?.Creature == null || potion == null) return false;
        try
        {
            // 「混沌药水」：用户 2026-10-25 口径 = 补**设计里的初始药水**（不是原版随机），
            // 其中「瓶中精灵」如果已经被消耗过，则改为给「狡诈药水」。
            if (potion is EntropicBrew && entropicFill != null && entropicFill.Count > 0)
            {
                try { shadow.RemoveUsedPotionInternal(potion); } catch { }
                bool hasFairy = false;
                foreach (var held in Held(shadow)) if (held is FairyInABottle) { hasFairy = true; break; }
                foreach (var type in entropicFill)
                {
                    if (type == null) continue;
                    var give = type;
                    if (type == typeof(FairyInABottle) && !hasFairy) give = typeof(CunningPotion);
                    Give(shadow, give);
                }
                Logger.Log("[CharBoss] 混沌药水：按初始药水补满栏位");
                // ⚠ 这一瓶也是「使用药水」⇒ 同样要回血。以前这个分支直接 return true，
                //   于是混沌药水是唯一不回血的药水（用户 2026-10-26 报「喝药回 5 血没实现」时
                //   有一部分就是这么来的）。
                MarkPotionDrunk(shadow);
                await HealAfterPotionAsync(shadow, healPerPotion, SafeName(potion));
                return true;
            }
            var target = ChooseTarget(shadow, potion);
            Logger.Log($"[CharBoss] 喝药水：{SafeName(potion)}（目标 {target?.Name ?? "无"}）");

            // 「喝药水不许改变这一回合的手牌」——她的手牌完全由剧本重建（BossScriptRunner.BuildHand
            // 每次都是**新建卡实例**，她真实那副卡组一直躺在抽牌堆里），所以任何「抽牌」类药水
            // 都会在手里凭空多出一张**永远不会被计划到**的牌；如果抽到的正好是剧本里也有的那张，
            // 还会出现「同一张牌两份、一份打一份不打」（用户 2026-10-26 两次实测：
            // 第 1 回合多一张不出的扫腿、第 5 回合多一张不出的生存者 —— 都是「明晰提取物」抽到抽牌堆顶）。
            // 做法：喝完把**这次新进手牌**的牌放回抽牌堆顶（什么都不丢，只是不改变手牌）。
            var handBefore = SnapshotHand(shadow);

            // 「对所有敌方生效」的药水（镣铐药水 / 爆炸药水…）内部读的是 `CombatState.HittableEnemies`。
            // 喝药发生在她回合刚开始，此刻必须按「Boss 出手」解释，否则在 CurrentSide 还没切过来的那一瞬
            // 会把它解释成敌方阵营（= 她自己）=「扔向自己」。这里用现成的强制开关把它钉住。
            bool prevForceSide = Patches.HittableEnemiesSidePatch.ForceRelativeSide;
            // 抛掷动画的目标点：原版对 `AllEnemies` 类药水写死取 **Enemy 阵营**的平均位置 ⇒ 朝她自己飞
            //（反编译见 Patches/PotionThrowAtPlayerPatch.cs）。这里事先算好「玩家侧平均位置」喂给它。
            ArmThrowAtPlayersIfNeeded(shadow, potion, target);
            try
            {
                Patches.HittableEnemiesSidePatch.ForceRelativeSide = true;
                await potion.OnUseWrapper(ctx, target);
            }
            finally
            {
                Patches.HittableEnemiesSidePatch.ForceRelativeSide = prevForceSide;
                Patches.PotionThrowAtPlayerPatch.Disarm();
            }
            KeepScriptedHand(shadow, handBefore);

            // 「罐装幽灵」（GhostInAJar）的无实体：**给到 2 层**（用户 2026-10-27 实测要求）。
            //
            // 原版这瓶药（反编译）只给 1 层 `IntangiblePower`；而
            // `IntangiblePower.AfterSideTurnEnd` 的条件是 `side == CombatSide.Enemy`
            // —— 也就是**她自己回合结束就掉 1 层**。她是在自己回合里喝的 ⇒ 1 层当场用光，
            // 玩家回合完全没被挡（用户：「喝瓶装幽灵获得的无实体在她回合结束就消失了」）。
            // 给 2 层：她回合结束 2→1，玩家回合仍有无实体，玩家回合结束（她侧还不到）……
            // 下一回合她再打出/喝掉时才是 1→0，正好和原版「撑过一次玩家回合」的语义对齐。
            try
            {
                if (potion is GhostInAJar)
                {
                    var creature = shadow.Creature;
                    int cur = 0;
                    try { cur = creature.GetPowerAmount<MegaCrit.Sts2.Core.Models.Powers.IntangiblePower>(); }
                    catch { cur = 0; }
                    const int want = 2;
                    if (cur < want)
                    {
                        var ctx2 = ctx ?? new BlockingPlayerChoiceContext();
                        await PowerCmd.Apply<MegaCrit.Sts2.Core.Models.Powers.IntangiblePower>(
                            ctx2, creature, want - cur, creature, null);
                        Logger.Log($"[CharBoss] 罐装幽灵：无实体补到 {want} 层（原版只给 1 层，" +
                                   $"而她回合结束会掉 1 层，1 层挡不到玩家回合）—— 现在 {want} 层");
                    }
                }
            }
            catch (Exception e)
            {
                Logger.Log("[CharBoss] 罐装幽灵补无实体失败: " + e.Message);
            }

            // 「本回合已经喝掉几瓶」：爬行动物饰品每瓶 +3 力量，意图要按「还没到手的那几瓶」补，
            // 喝完一瓶就撤掉一瓶的量（用户 2026-10-26：否则 +9 之后会跳回去）。
            MarkPotionDrunk(shadow);

            // ⚠ v0.2.244：**先确认她还在栏位里**再移除。
            //   原版「喝药」那条流程本身已经把药水从栏位拿走了，这里再移一次就会抛
            //   `Tried to remove potion you don't have: POTION.XXX` —— 被 `LooksLikeProblem` 当成问题
            //   记进 diag（用户 2026-10-30 的表格里 3 条都是它，纯噪音）。
            try
            {
                bool stillHeld = false;
                foreach (var each in Held(shadow))
                {
                    if (ReferenceEquals(each, potion)) { stillHeld = true; break; }
                }
                if (stillHeld) shadow.RemoveUsedPotionInternal(potion);
            }
            catch (Exception e) { Logger.Log("[CharBoss] 移除已喝药水失败: " + e.Message); }
            Version++;
            Logger.Log($"[CharBoss] 喝药后栏位剩 {Held(shadow).Count} 瓶");

            // 「调制」：每用一瓶药水回 5 点生命。
            // ⚠ 以前只在「血没满」时打日志 ⇒ 满血喝药**屏幕上和日志里都毫无痕迹**，
            //   用户 2026-10-26 因此判断「这条没实现」。现在无论生效与否都会记一行。
            await HealAfterPotionAsync(shadow, healPerPotion, SafeName(potion));
            return true;
        }
        catch (Exception e)
        {
            Logger.Log($"[CharBoss] 喝药水 {SafeName(potion)} 失败: {e.Message}");
            return false;
        }
    }

    /// <summary>记一笔「本回合已经喝掉一瓶」（爬行动物饰品的力量预补要用，见 BladesGrowth）。</summary>
    private static void MarkPotionDrunk(Player shadow)
    {
        try
        {
            if (shadow.Creature?.Monster is CharBossMonster boss) boss.PotionsDrunkThisTurn++;
        }
        catch { /* 忽略 */ }
    }

    /// <summary>
    /// 「调制」的回血：每用一瓶药水回复 <paramref name="healPerPotion"/> 点生命。
    ///
    /// 只受「不超过最大生命」这一条限制（满血时这 5 点本来就无处可加），
    /// 但**每次都会记日志**（生效 / 满血跳过 / 找不到生物 三种都记），
    /// 免得「满血喝药看不到任何反馈」被当成功能没实现（用户 2026-10-26）。
    /// </summary>
    private static async Task HealAfterPotionAsync(Player shadow, int healPerPotion, string potionName)
    {
        if (healPerPotion <= 0) return;
        try
        {
            var me = shadow.Creature;
            if (me == null)
            {
                Logger.Log($"[CharBoss] 调制：{potionName} 后要回 {healPerPotion} 点生命，但找不到她的生物 ⇒ 跳过");
                return;
            }
            if (!me.IsAlive)
            {
                Logger.Log($"[CharBoss] 调制：{potionName} 后要回 {healPerPotion} 点生命，但她已经死亡 ⇒ 跳过");
                return;
            }

            int before = me.CurrentHp;
            int max = me.MaxHp;
            if (before >= max)
            {
                Logger.Log($"[CharBoss] 调制：{potionName} → 回 {healPerPotion} 点生命，但她已满血 {before}/{max} ⇒ 不生效");
                return;
            }

            await CreatureCmd.Heal(me, healPerPotion);
            Logger.Log($"[CharBoss] 调制：{potionName} → 回复 {healPerPotion} 点生命（{before} → {me.CurrentHp} / {max}）");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 调制回血失败: " + e.Message);
        }
    }

    /// <summary>喝药水前的手牌快照（拿不到返回 null = 不做处理）。</summary>
    private static List<CardModel>? SnapshotHand(Player shadow)
    {
        try
        {
            var pcs = shadow.PlayerCombatState;
            if (pcs?.Hand?.Cards == null) return null;
            return new List<CardModel>(pcs.Hand.Cards);
        }
        catch { return null; }
    }

    /// <summary>给「抽牌卡」用的手牌快照（和喝药水同一套，见 <see cref="KeepScriptedHand"/>）。</summary>
    internal static List<CardModel>? SnapshotHandForCards(Player shadow) => SnapshotHand(shadow);

    /// <summary>是不是小刀这类**生成的 token**（它们不该被塞回抽牌堆）。</summary>
    private static bool IsShivToken(CardModel card)
    {
        try { return ShivPlays.IsShiv(card); } catch { return false; }
    }

    /// <summary>
    /// 喝完药水后：把**这一次新进手牌**的牌放回抽牌堆顶 —— 于是「手牌 = 剧本」不被药水破坏。
    ///
    /// 只动「这次新出现的牌」，她自己原有的手牌一个不碰；放进抽牌堆顶而不是丢弃，
    /// 所以什么都没损失（她的牌堆本来也不按正常规则抽，见 <see cref="SnapshotHand"/> 的注释）。
    ///
    /// 同一套机制也给「抽牌卡」用（一层「奇巧猎」的杂技 / 早有准备 / 后空翻 / 本能反应）：
    /// 那些牌必须**真的抽**（速行者每抽 1 张对玩家造成 2 点群伤，是原版 power 结算的），
    /// 但抽进来的牌不该留在她手里（手牌由剧本决定）⇒ 打完那张牌后照样放回抽牌堆顶。
    /// 小刀是生成的攻击牌，本来就不该进抽牌堆 ⇒ 默认跳过。
    /// </summary>
    internal static void KeepScriptedHand(Player shadow, List<CardModel>? handBefore, string reason = "喝药水")
    {
        if (handBefore == null) return;
        try
        {
            var pcs = shadow.PlayerCombatState;
            if (pcs?.Hand?.Cards == null) return;

            var extra = new List<CardModel>();
            foreach (var card in pcs.Hand.Cards)
            {
                if (card == null) continue;
                if (IsShivToken(card)) continue;      // 小刀是生成的攻击牌，不进抽牌堆
                if (!ContainsSame(handBefore, card)) extra.Add(card);   // 引用比较：新实例才算「新抽到的」
            }
            if (extra.Count == 0) return;

            foreach (var card in extra)
            {
                try
                {
                    string name = "?";
                    try { name = card.Id.Entry; } catch { /* 取不到就占位 */ }
                    pcs.Hand.RemoveInternal(card, true);
                    pcs.DrawPile.AddInternal(card, 0, true);       // 0 = 牌堆顶
                    Logger.Log($"[CharBoss] {reason}抽到的 {name} 已放回抽牌堆" +
                               $"（她这回合的手牌由剧本决定，不会多出永不参与的牌）");
                }
                catch (Exception e)
                {
                    Logger.Log("[CharBoss] 把抽到的牌放回牌堆失败（留在手里也不影响战斗）: " + e.Message);
                }
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 喝药水后整理手牌失败（忽略）: " + e.Message);
        }
    }

    /// <summary>按**引用**判断这张牌是不是快照里已有的那一张（不用 Equals，防止 CardModel 以后重写相等语义）。</summary>
    private static bool ContainsSame(List<CardModel> list, CardModel card)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (ReferenceEquals(list[i], card)) return true;
        }
        return false;
    }

    /// <summary>
    /// 「对所有敌方生效」的药水（镣铐药水 / 爆炸药水…）把抛掷动画指向**玩家侧**。
    ///
    /// 原版 `PotionModel.OnUseWrapper` 对非单体目标是这样算目标点的：
    /// `TargetType == AllEnemies` ⇒ 取 `CombatSide.**Enemy**` 里可命中生物的平均 `VfxSpawnPosition`。
    /// 我们的 Boss 就在敌方阵营 ⇒ 药水朝她自己飞（效果本身已经打在玩家身上，纯属动画方向错）。
    /// 这里事先算好「真人玩家侧平均位置」交给 <c>PotionThrowAtPlayerPatch</c> 覆盖那一发。
    /// 拿不到玩家位置就不干预（退回原版行为）。
    /// </summary>
    private static void ArmThrowAtPlayersIfNeeded(Player shadow, PotionModel potion, Creature? target)
    {
        try
        {
            var type = potion.TargetType;
            bool allEnemies = type == TargetType.AllEnemies;
            if (!allEnemies) { Patches.PotionThrowAtPlayerPatch.Disarm(); return; }

            var room = MegaCrit.Sts2.Core.Nodes.Rooms.NCombatRoom.Instance;
            if (room == null) { Patches.PotionThrowAtPlayerPatch.Disarm(); return; }

            Vector2 sum = Vector2.Zero;
            int count = 0;
            foreach (var p in BossLockOn.RealPlayers(shadow.Creature?.CombatState))
            {
                var creature = p?.Creature;
                if (creature == null || !creature.IsAlive) continue;
                var node = room.GetCreatureNode(creature);
                if (node == null) continue;
                sum += node.VfxSpawnPosition;
                count++;
            }
            if (count == 0) { Patches.PotionThrowAtPlayerPatch.Disarm(); return; }

            Patches.PotionThrowAtPlayerPatch.Arm(sum / count);
            Logger.Log($"[CharBoss] 药水抛掷方向：{SafeName(potion)} 改为朝玩家侧（{count} 位玩家的平均位置）");
        }
        catch { Patches.PotionThrowAtPlayerPatch.Disarm(); }
    }

    /// <summary>
    /// 按药水自己的 TargetType 选目标：单体敌人 / 随机敌人 / **所有敌人** 一律打真人玩家，其余给自己。
    ///
    /// ⚠ 以前「所有敌人」这一类（镣铐药水、爆炸药水…）经常被选成**她自己**：这两种药水内部读的是
    /// <c>CombatState.HittableEnemies</c>，而在她**回合刚开始**时真人玩家往往还不是 `IsHittable`
    /// （和 <c>HittableEnemiesSidePatch</c> 里记的那个坑是同一件事）⇒ 这里的 `IsHittable` 判据过不了，
    /// 就退回了「自己」。后果：药水**朝她自己扔**（抛掷动画的方向、以及少数直接用 `target` 的效果
    /// 都会落在她身上），用户 2026-10-26 实测报的就是这个：
    /// 日志里是 `喝药水：SHACKLING_POTION（目标 静默猎手）`。
    /// 现在改成：优先「可命中的真人玩家」，否则退回**任意活着的真人玩家**，**绝不退回自己**。
    /// </summary>
    private static Creature? ChooseTarget(Player shadow, PotionModel potion)
    {
        var me = shadow.Creature;
        try
        {
            var type = potion.TargetType;
            bool enemyTarget = type == TargetType.AnyEnemy || type == TargetType.RandomEnemy
                               || type == TargetType.AllEnemies;
            if (enemyTarget)
            {
                Creature? aliveFallback = null;
                foreach (var p in BossLockOn.RealPlayers(shadow.Creature?.CombatState))
                {
                    var creature = p?.Creature;
                    if (creature == null || !creature.IsAlive) continue;
                    if (aliveFallback == null) aliveFallback = creature;   // 先记下第一个活着的真人玩家
                    if (creature.IsHittable) return creature;              // 有可命中的就用它
                }
                if (aliveFallback != null) return aliveFallback;
            }
            if (type == TargetType.AnyPlayer && me != null) return me;
        }
        catch { /* 忽略 */ }
        return me;
    }

    /// <summary>「强化类」药水（图标下面写「强化」的那些）。</summary>
    internal static bool IsBuff(Type potionType)
    {
        if (potionType == null) return false;
        return potionType == typeof(DexterityPotion)
            || potionType == typeof(FyshOil)
            || potionType == typeof(GhostInAJar)
            || potionType == typeof(Duplicator)
            || potionType == typeof(BottledPotential)
            || potionType == typeof(FlexPotion);
    }

    /// <summary>类型 → 原版药水模型。走 `ModelDb.GetById`（和「彗星」取碎屑那条同一条路，实测可用）；
    /// 反射那套只当兜底 —— 它拿不到时会**静默**退回随机，用户 2026-10-25 就是这样看到「炼制药水随机」的。</summary>
    internal static PotionModel? CanonicalPotion(Type potionType)
    {
        if (potionType == null) return null;
        try
        {
            var id = MegaCrit.Sts2.Core.Models.ModelDb.GetId(potionType);
            var byId = MegaCrit.Sts2.Core.Models.ModelDb.GetById<PotionModel>(id);
            if (byId != null) return byId;
        }
        catch (Exception e)
        {
            Logger.Log($"[CharBoss] GetById 取药水 {potionType.Name} 失败: {e.Message}");
        }
        try
        {
            var method = typeof(ModelDb).GetMethods()
                .FirstOrDefault(m => m.Name == "Potion" && m.IsGenericMethodDefinition && m.GetParameters().Length == 0);
            if (method == null) return null;
            return method.MakeGenericMethod(potionType).Invoke(null, null) as PotionModel;
        }
        catch (Exception e)
        {
            Logger.Log($"[CharBoss] 取药水模型 {potionType.Name} 失败: {e.Message}");
            return null;
        }
    }

    internal static string SafeName(PotionModel? potion)
    {
        try { return potion?.Id.Entry ?? "?"; }
        catch { return "?"; }
    }
}
