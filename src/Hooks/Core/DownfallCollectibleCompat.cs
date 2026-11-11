using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using CharacterBosses.Monsters;

namespace CharacterBosses.Core;

/// <summary>
/// **与 mods 文件夹里的「崩坠」(`mods\Downfall\Downfall.dll`) 的收藏家（Collector）兼容**。
///
/// 用户 2026-10-23 要求：「让收藏家击败玩家 boss 后会掉落藏品卡牌（牌本身都是原版的）」，
/// 并给了每个 Boss 对应的那张牌；3 层因为**没有战斗奖励**（藏品牌挂不上），
/// 但要「绑定藏品牌」。
///
/// ## 用的都是它自己的代码（反编译确认，别自创）
/// - `Collector.CollectorCode.Rewards.CollectibleReward(CardModel card, Player player)`：
///   藏品奖励，`RewardsSetIndex = 9`，描述里要 **3 点精华**（`Player.CanAffordEssence(3)`）。
/// - `Collector.CollectorCode.Core.CollectiblesModel`：
///   · `public static void SyncAdd(Player player, CardModel card, int essenceCost)` ——
///     这正是 `CollectibleReward.OnSelect` 里取走奖励时做的事：**扣精华 → `AddCollectible` →
///     播放「从奖励界面飞进藏品堆」动画 → 多人同步**（`LocalContext.IsMe(player)` 才执行）；
///   · `internal static void AddCollectible(Player player, CardModel card)` —— 只把牌塞进
///     `CollectorDeck`（藏品册）并刷新顶栏按钮，**不扣精华** ⇒ 3 层「绑定藏品牌」用它。
///
/// ## 实现口径
/// - 1/2 层的 Boss：战斗胜利后按收藏家的正常代价（**3 点精华**）走 `SyncAdd`；
///   精华不够就**不白送**（和它自己的 `COLLECTIBLE_REWARD_CANT_AFFORD` 一致），只记一条日志。
/// - 3 层的 Boss：直接 `AddCollectible`（绑定进藏品册，不扣精华）。
/// - 全部**反射调用**（我们的 csproj 不引用 Downfall.dll），找不到类型/方法就静默跳过 ⇒ 没装崩坠时零影响。
/// </summary>
public static class DownfallCollectibleCompat
{
    /// <summary>收藏家取走一件藏品的代价（= `CollectibleReward` 里的 3 点精华）。</summary>
    private const int CollectibleEssenceCost = 3;

    /// <summary>收藏家的角色类型全名（`Collector.CollectorCode.Core.Collector`）。</summary>
    private const string CollectorCharacterFullName = "Collector.CollectorCode.Core.Collector";

    /// <summary>收藏家的 CharacterModel（它自己重写了 `AfterCombatEnd`，掉落逻辑就在那里面）。</summary>
    private const string CollectorModelFullName = "Collector.CollectorCode.Core.CollectorModel";

    /// <summary>藏品奖励类型（`Collector.CollectorCode.Rewards.CollectibleReward`）。</summary>
    private const string CollectibleRewardFullName = "Collector.CollectorCode.Rewards.CollectibleReward";

    /// <summary>「祸不单行」叠的那层 power（`Collector.CollectorCode.Powers.DoubleTroublePower`）。</summary>
    private const string DoubleTroublePowerFullName = "Collector.CollectorCode.Powers.DoubleTroublePower";

    private static bool _installed;
    private static bool _patched;
    private static bool _loggedMissing;

    /// <summary>本场战斗里被我们打死的 Boss（Key、幕、藏品牌类型、是否升级版）——给 `AfterCombatEnd` 补奖励用。</summary>
    private static readonly List<(string Key, int Act, Type Card, bool Upgraded)> Defeated =
        new();

    /// <summary>这批 `Defeated` 属于哪一场战斗（换场就清空，避免上一场的记录漏到下一场）。</summary>
    private static object? _defeatedCombat;

    /// <summary>已经处理过「死亡→掉落」的 Boss 实例（`BeforeRemovedFromRoom` 可能被调多次）。</summary>
    private static readonly HashSet<object> HandledBosses = new();

    /// <summary>
    /// 「正在挂结算奖励」的重入闸。
    ///
    /// ⚠ 用户 2026-10-24 实测：打死二层那两只（物理机 + 灵魂骨）后**发了一堆（53 个）魂粹**，
    /// 存档再读就报错。`room.AddExtraReward(...)` 有可能再次走到 `CollectorModel.AfterCombatEnd`
    /// （结算界面刷新/多人逐个玩家处理），而我们的 postfix 只要被重入一次，就会**再发一遍**
    /// （`Defeated` 要到最外层 finally 才清）⇒ 奖励成倍增长、存档跟着坏掉。
    /// 这里加闸：重入直接跳过；同一 (Key, 幕) 也只发一次。
    /// </summary>
    private static bool _handlingCombatEnd;

    /// <summary>
    /// **动态挂**收藏家自己的 `CollectorModel.AfterCombatEnd(CombatRoom room)`（我们无法在编译期引用 Downfall.dll）。
    ///
    /// 为什么要挂它（反编译 `CollectorModel.AfterCombatEnd` 得到的原版流程）：
    /// <code>
    /// var collectibles = ModelDb.CardPool&lt;CollectibleCardPool&gt;().AllCards.OfType&lt;ICollectible&gt;();
    /// var cards = _defeatedEnemies.Select(e =&gt; collectibles.FirstOrDefault(c =&gt; c.GetMonsterModel().Id == e.Id));
    /// foreach (var p in room.CombatState.Players.Where(p =&gt; p.Character is Collector)) {
    ///     room.AddExtraReward(p, new EssenceReward(essence, p));          // 按房间类型给 1/2/3 精华
    ///     foreach (var c in cards) room.AddExtraReward(p, new CollectibleReward(c.ToMutable(), p));
    /// }
    /// </code>
    /// 也就是说「藏品掉落」是**在这个方法里**挂到结算奖励上的。我们接管同一时刻，
    /// 把「被打死的我们的 Boss」按掉落表换成 `CollectibleReward` 挂上去 —— 用的全是它自己的类与方法。
    /// </summary>
    internal static void TryInstall()
    {
        if (_installed) return;
        _installed = true;
        try
        {
            var type = FindType(CollectorModelFullName);
            if (type == null)
            {
                if (!_loggedMissing)
                {
                    _loggedMissing = true;
                    Logger.Log("[CharBoss] 收藏家兼容：没找到 CollectorModel（未安装 mods\\Downfall，跳过）");
                }
                return;
            }

            var method = type.GetMethod("AfterCombatEnd",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance,
                null, new[] { typeof(MegaCrit.Sts2.Core.Rooms.CombatRoom) }, null);
            if (method == null)
            {
                Logger.Log("[CharBoss] 收藏家兼容：CollectorModel.AfterCombatEnd(CombatRoom) 没找到 ⇒ 只能走 SyncAdd 兜底");
                return;
            }

            var harmony = new HarmonyLib.Harmony("CharacterBosses.downfall.collector");
            harmony.Patch(method,
                postfix: new HarmonyLib.HarmonyMethod(
                    typeof(DownfallCollectibleCompat).GetMethod(nameof(AfterCollectorModelCombatEnd),
                        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!));
            Logger.Log("[CharBoss] 收藏家兼容：已挂上 CollectorModel.AfterCombatEnd → 我们的 Boss 也会掉藏品牌");
            _patched = true;

            // **让藏品册里我们的牌也显示「虚无 / 消耗」**（用户 2026-10-23 实测：别的藏品牌都有，只有我们的没有）。
            //
            // 为什么实例上加关键词不管用：藏品册是 `CollectiblesModel.GetCollectibles(Player)` 里
            // 用 `CardModel.FromSerializable(...)` **重建**出来的卡 —— 序列化里只存 id/升级/附魔，
            // **不存关键词**；而它自己那批藏品牌（继承 `Cards.Token.Collectible`）是在**模型层**
            // 声明 `CanonicalKeywords` 的，所以重建后照样有。
            // 我们的牌是原版牌、模型层不能加 ⇒ 在它**重建完**的地方补上（只碰我们掉落表里那几张）。
            var getMethod = FindType(CollectiblesModelFullName)?.GetMethod("GetCollectibles",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static,
                null, new[] { typeof(MegaCrit.Sts2.Core.Entities.Players.Player) }, null);
            if (getMethod != null)
            {
                harmony.Patch(getMethod,
                    postfix: new HarmonyLib.HarmonyMethod(
                        typeof(DownfallCollectibleCompat).GetMethod(nameof(AfterGetCollectibles),
                            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!));
                Logger.Log("[CharBoss] 收藏家兼容：已挂上 GetCollectibles → 藏品册里我们的牌也会显示虚无/消耗");
            }

            // **藏品册里的「遗传算法」打出后要把成长写回去**（用户 2026-10-30：
            //   「加入收藏家藏品堆中的遗传算法不会因为它的打出而增加获得的格挡量」）。
            //
            // 根因（反编译两边都确认）：
            //   · `GeneticAlgorithm.OnPlay`：`BuffFromPlay(+3)` 只加在**这张牌实例**和它的 `DeckVersion`
            //     （= 牌库里那一张）上 —— 这是原版「牌在牌库里永久成长」的实现方式；
            //   · 收藏家的藏品堆是 `CollectiblesModel.CollectorDeck`，里面存的是
            //     `List<SerializableCard>`（**序列化快照**），`GetCollectibles` 每次都用
            //     `CardModel.FromSerializable(...)` **重建新实例**；
            //   ⇒ 藏品堆里的牌**没有"牌库那一张"**（`DeckVersion` 为 null），成长只落在临时实例上，
            //     下次重建又回到原始数值 ⇒ 永远不涨。
            //
            // 修法：`GetCollectibles` 重建时记下「卡实例 → 在 CollectorDeck 里的下标」，
            //   在 `GeneticAlgorithm.BuffFromPlay` 之后把这张牌的 `ToSerializable()`
            //   （`IncreasedBlock` 是 `[SavedProperty]`，会一起存进去）写回该下标，
            //   并像它自己的 `AddCollectible` 那样用 `Set(player, 新列表)` 提交。
            var gaType = typeof(MegaCrit.Sts2.Core.Models.Cards.GeneticAlgorithm);
            var buffFromPlay = gaType.GetMethod("BuffFromPlay",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
                null, new[] { typeof(int) }, null);
            var increasedBlockSetter = gaType.GetProperty("IncreasedBlock",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)?.GetSetMethod(true);

            bool patchedAny = false;
            // ⚠ 主钩子用**属性 setter**而不是 `BuffFromPlay`：`BuffFromPlay` 只有两行、极可能被 JIT **内联**
            //   （内联后 Harmony 挂在方法入口的补丁根本不会被调用），而 `IncreasedBlock` 的 setter 里有
            //   `AssertMutable()`，不会被内联。两者都挂上、写回是幂等的（同一个值写两次没差别）。
            if (increasedBlockSetter != null)
            {
                harmony.Patch(increasedBlockSetter,
                    postfix: new HarmonyLib.HarmonyMethod(
                        typeof(DownfallCollectibleCompat).GetMethod(nameof(AfterGrowChanged),
                            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!));
                patchedAny = true;
            }
            if (buffFromPlay != null)
            {
                harmony.Patch(buffFromPlay,
                    postfix: new HarmonyLib.HarmonyMethod(
                        typeof(DownfallCollectibleCompat).GetMethod(nameof(AfterGrowChanged),
                            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!));
                patchedAny = true;
            }

            if (patchedAny)
            {
                Logger.Log("[CharBoss] 收藏家兼容：已挂上 遗传算法成长钩子（IncreasedBlock setter" +
                           $"{(buffFromPlay != null ? " + BuffFromPlay" : "")}）→ 藏品堆里打出也会永久成长");
            }
            else
            {
                Logger.Log("[CharBoss] 收藏家兼容：没找到 GeneticAlgorithm 的成长入口 ⇒ 藏品堆里遗传算法不会成长");
            }

            // **「祸不单行」（DoubleTroublePower）也要能作用于我们的藏品牌**（用户 2026-10-30：
            //   「收藏家的『祸不单行』不能使角色 boss 掉落的藏品卡打出 2 次」）。
            //
            // 反编译 `DoubleTroublePower.ModifyCardPlayCount`：
            // <code>
            // if (card.Owner.Creature == Owner && card is ICollectible) return playCount + 1;
            // </code>
            // 它只认实现了 `ICollectible` 的**它自己的藏品牌**；我们的藏品是**原版牌**（遗传算法…），
            // 类型上不可能是 ICollectible ⇒ 永远 +0。
            // 所以在这里补一道：只要这张牌是「我们掉落表里那张」且属于这层 power 的拥有者，就 +1。
            var doubleTrouble = FindType(DoubleTroublePowerFullName)?.GetMethod("ModifyCardPlayCount",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance,
                null,
                new[]
                {
                    typeof(CardModel),
                    typeof(MegaCrit.Sts2.Core.Entities.Creatures.Creature),
                    typeof(int),
                },
                null);
            if (doubleTrouble != null)
            {
                harmony.Patch(doubleTrouble,
                    postfix: new HarmonyLib.HarmonyMethod(
                        typeof(DownfallCollectibleCompat).GetMethod(nameof(AfterModifyCardPlayCount),
                            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!));
                Logger.Log("[CharBoss] 收藏家兼容：已挂上 祸不单行（DoubleTroublePower）→ 我们的藏品牌也会被打出两次");
            }
            else
            {
                Logger.Log("[CharBoss] 收藏家兼容：没找到 DoubleTroublePower.ModifyCardPlayCount ⇒ 祸不单行对我们的藏品无效");
            }

            // **「思维抽取」（Brain Drain）能从我们的 Boss 身上抽走藏品牌**（用户 2026-10-23：
            // 「这是获得 3 层 Boss 藏品的唯一正常途径」——3 层没有战斗奖励，挂不上 `CollectibleReward`）。
            //
            // 它原来的实现（反编译 `BrainDrain.<OnPlayInternal>d__3`）：
            // <code>
            // if (FirstOrDefault(CollectibleCardPool.AllCards.Cast&lt;ICollectible&gt;(),
            //         e =&gt; e.GetMonsterModel().Id == cardPlay.Target.Monster.Id) is CollectorCardModel c) {
            //     var card = CombatState.CreateCard(c, Owner);
            //     if (IsUpgraded) CardCmd.Upgrade(card, HorizontalLayout);
            //     CardPileCmd.AddGeneratedCardToCombat(card, PileType.Hand, Owner, CardPilePosition.Bottom);
            // }
            // </code>
            // 也就是说它按「怪物 id」去**它自己的藏品牌池**里找牌 —— 我们的 Boss 是原版牌换皮，
            // 池里永远没有对应项 ⇒ 伤害照打、牌抽不到（用户实测）。
            //
            // 为什么不能只补那个 `FirstOrDefault` 的判断：那个集合是
            // `CollectibleCardPool.AllCards.Cast&lt;ICollectible&gt;()`，只含它自己的藏品牌（还得是
            // `CollectorCardModel`），我们的原版牌**根本不在里面** ⇒ 只能自己接管发牌这一步：
            // 挂在 `OnPlayInternal` 之后，等它把伤害打完，再按掉落表把牌塞进手牌。
            var brainDrain = FindType(BrainDrainFullName);
            var brainDrainOnPlay = brainDrain?.GetMethod("OnPlayInternal",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance);
            if (brainDrainOnPlay != null)
            {
                harmony.Patch(brainDrainOnPlay,
                    postfix: new HarmonyLib.HarmonyMethod(
                        typeof(DownfallCollectibleCompat).GetMethod(nameof(AfterBrainDrainOnPlay),
                            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!));
                Logger.Log("[CharBoss] 收藏家兼容：已挂上 思维抽取（BrainDrain）→ 打在我们的 Boss 上也能抽走藏品牌");
            }
            else
            {
                Logger.Log($"[CharBoss] 收藏家兼容：没找到 {BrainDrainFullName}.OnPlayInternal ⇒ 3 层藏品抽不到");
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 收藏家兼容：动态挂载失败（忽略）: " + e.Message);
        }
    }

    /// <summary>
    /// 挂在 `BrainDrain.OnPlayInternal` 之后（它是 async，返回的 Task 还没跑完）：
    /// 把我们这只 Boss 的藏品牌按掉落表塞进出牌者的手牌 —— 和它自己那句
    /// `AddGeneratedCardToCombat(card, PileType.Hand, Owner, CardPilePosition.Bottom)` 完全一致。
    /// </summary>
    private static void AfterBrainDrainOnPlay(object __instance,
                                              MegaCrit.Sts2.Core.Entities.Cards.CardPlay cardPlay,
                                              ref System.Threading.Tasks.Task __result)
    {
        try
        {
            if (__result == null || cardPlay?.Target == null) return;

            // 只有「打的是我们的角色 Boss」才接管（原版 Boss 照旧走它自己的藏品牌池）
            if (cardPlay.Target.Monster is not CharBossMonster boss) return;

            int act = Math.Clamp(boss.ActIndex, 0, 2) + 1;                  // 幕号 1 起
            if (!ResolveLoot(boss, act, out var lootCard, out var lootUpgraded)) return;   // 表里没有：不抢
            var cardType = lootCard;

            var player = CharacterBosses.Core.GameCompat.ActorOf(cardPlay);
            if (player == null) return;

            bool upgraded = false;
            try { upgraded = (__instance as MegaCrit.Sts2.Core.Models.CardModel)?.IsUpgraded ?? false; }
            catch { /* 取不到就按未升级发 */ }

            __result = GrantStolenCollectibleAsync(__result, player, boss, act, cardType,
                                                  upgraded || lootUpgraded);
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 收藏家兼容：思维抽取接管失败（忽略）: " + e.Message);
        }
    }

    /// <summary>等原版把伤害结算完，再把藏品牌发到手上（升级版「思维抽取+」连牌一起升级）。</summary>
    private static async System.Threading.Tasks.Task GrantStolenCollectibleAsync(
        System.Threading.Tasks.Task original,
        Player player,
        CharBossMonster boss,
        int act,
        Type cardType,
        bool upgraded)
    {
        try
        {
            await original;
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 收藏家兼容：思维抽取本体失败，不发牌: " + e.Message);
            return;
        }

        try
        {
            var card = CreateCard(player, cardType);
            if (card == null)
            {
                Logger.Log($"[CharBoss] 收藏家兼容：思维抽取造不出藏品牌 {cardType.Name}");
                return;
            }

            if (upgraded)
            {
                try
                {
                    MegaCrit.Sts2.Core.Commands.CardCmd.Upgrade(
                        card, MegaCrit.Sts2.Core.Nodes.CommonUi.CardPreviewStyle.HorizontalLayout);
                }
                catch (Exception e)
                {
                    Logger.Log("[CharBoss] 收藏家兼容：思维抽取升级藏品牌失败: " + e.Message);
                }
            }

            await MegaCrit.Sts2.Core.Commands.CardPileCmd.AddGeneratedCardToCombat(
                card,
                MegaCrit.Sts2.Core.Entities.Cards.PileType.Hand,
                player,
                MegaCrit.Sts2.Core.Entities.Cards.CardPilePosition.Bottom);

            Logger.Log($"[CharBoss] 收藏家兼容：思维抽取从 {boss.Def.Key} act{act} 抽到了藏品牌" +
                       $"「{card.Title}」{((upgraded) ? "（已升级）" : string.Empty)}");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 收藏家兼容：思维抽取发牌失败: " + e.Message);
        }
    }

    /// <summary>挂在 `CollectorModel.AfterCombatEnd` 之后：把我们的 Boss 藏品牌加进结算奖励。</summary>
    private static void AfterCollectorModelCombatEnd(object __instance,
                                                    MegaCrit.Sts2.Core.Rooms.CombatRoom room)
    {
        // 重入闸：`room.AddExtraReward(...)` 可能又回到这里，重入一次就多发一遍（用户实测发了 53 个魂粹）
        if (_handlingCombatEnd) return;

        _handlingCombatEnd = true;
        try
        {
            if (room?.CombatState == null) return;

            var seen = new HashSet<(string Key, int Act)>();
            foreach (var (key, act, cardType, upgraded) in Defeated.ToArray())
            {
                // 3 层没有战斗奖励（藏品牌挂不上）→ OnBossDefeated 里已经直接绑定进藏品册
                if (act >= 3) continue;
                // 同一只 Boss 只发一次（两只 Boss 各一条，这是设计）
                if (!seen.Add((key, act))) continue;

                bool any = false;
                foreach (var creature in room.CombatState.PlayerCreatures.ToArray())
                {
                    var player = creature?.Player;
                    if (player == null || creature!.Monster != null) continue;
                    if (!IsCollectorCharacter(player.Character)) continue;

                    var card = CreateRewardCard(player, cardType, upgraded);
                    if (card == null) continue;

                    var reward = CreateCollectibleReward(card, player);
                    if (reward == null) continue;

                    room.AddExtraReward(player, reward);
                    any = true;
                    Logger.Log($"[CharBoss] 收藏家兼容：{key} act{act} → 「{card.Title}」已作为藏品奖励加到结算界面");
                }
                if (!any) Logger.Log($"[CharBoss] 收藏家兼容：{key} act{act} 没有收藏家玩家 / 造牌失败，跳过奖励");
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 收藏家兼容：挂载结算奖励失败: " + e.Message);
        }
        finally
        {
            _handlingCombatEnd = false;
            Defeated.Clear();
            HandledBosses.Clear();
            _defeatedCombat = null;
        }
    }

    /// <summary>挂在 `CollectiblesModel.GetCollectibles` 之后：给「我们掉落表里那几张」补上虚无/消耗。</summary>
    private static void AfterGetCollectibles(MegaCrit.Sts2.Core.Entities.Players.Player player,
                                             List<CardModel> __result)
    {
        try
        {
            if (__result == null) return;

            // ★ 先记下「卡实例 → CollectorDeck 里的下标」——`GetCollectibles` 是**按下标顺序**重建的
            //   （`for (i…) CardModel.FromSerializable(val[i])`），所以下标与藏品册条目一一对应。
            //   藏品牌每次打出/刷新都会重建实例，所以每次重建都覆盖这份映射。
            try
            {
                var map = new Dictionary<CardModel, int>(ReferenceEqualityComparer.Instance);
                for (int i = 0; i < __result.Count; i++)
                {
                    var c = __result[i];
                    if (c != null) map[c] = i;
                }
                if (map.Count > 0) CollectibleIndex[player] = map;
                else CollectibleIndex.Remove(player);
            }
            catch { /* 映射失败不影响关键词那条 */ }

            var ours = LootEntries();
            // ★★ v0.2.239（联机不同步，玩家 2026-10-30 的小怪关 divergence 就是它）：
            //   **绝对不许在这里给牌加关键词**。以前这里会对「我们掉落表里那几张」调
            //   `ApplyCollectibleKeywords(card)`，而这些卡是 `GetCollectibles` **重建出来的实例**，
            //   这个 getter 是**收藏家藏品册的界面路径** ⇒ 只有「恰好渲染了那个册子」的客户端
            //   会把 `Exhaust`/`Ethereal` 加到自己的实例上，别的客户端没有。
            //   而关键词是**会被序列化、会进校验和**的（原版 LOCAL STATE DUMP 里每张牌都有
            //   `Keywords:` 那一行；玩家日志里 `CARD.AGGRESSION … Keywords: Ethereal` /
            //   `CARD.SNEAKY … Keywords: Ethereal,Sly` 就是同一张牌在两端关键词不一样）。
            //   关键词不一样 ⇒ 打出时「进消耗堆还是弃牌堆」不同 ⇒ 任何一场战斗都会 divergence。
            //   现在改成：关键词只在**造牌**时加（`CreateCard` / `CreateRewardCard`，两端跑同一套同步流程），
            //   这里只保留「卡实例 → 藏品册下标」的记账（纯本地映射，不改任何状态）。
            foreach (var card in __result)
            {
                if (card == null) continue;
                string entry;
                try { entry = card.Id.Entry; } catch { continue; }
                if (!ours.Contains(entry)) continue;

                if (GrowLogged.Add($"nokw:{entry}"))
                {
                    Logger.Log($"[CharBoss] 收藏家兼容：藏品「{entry}」**不再在 GetCollectibles 里改关键词**" +
                               "（改了会让各客户端的同一张牌关键词不一致 ⇒ 联机不同步；" +
                               "关键词仍在造牌时加，见我掉落那几条日志）");
                }
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 收藏家兼容：补关键词失败: " + e.Message);
        }
    }

    // ── 藏品「永久成长」写回 ─────────────────────────────────

    /// <summary>玩家 → （`GetCollectibles` 重建出来的卡实例 → 在 `CollectorDeck` 里的下标）。</summary>
    private static readonly Dictionary<MegaCrit.Sts2.Core.Entities.Players.Player, Dictionary<CardModel, int>>
        CollectibleIndex = new();

    private static readonly HashSet<string> GrowLogged = new(StringComparer.Ordinal);

    /// <summary>`GeneticAlgorithm.BuffFromPlay(int)` 之后：把成长写回藏品册对应条目。</summary>
    ///
    /// ⚠ 两条路（用户 2026-10-30 第一次修没生效 ⇒ 只按实例映射不够）：
    ///   ① **快路径**：`GetCollectibles` 重建出来的那个实例 → 下标（同一个对象才能这样对）；
    ///   ② **慢路径**：藏品堆里的牌在打出时常常是**另一份实例**（牌堆自己造/重建），
    ///      于是按「同一张牌 + 同样的升级状态 + 条目里存的格挡正好等于『打之前的数值』」去藏品册里找。
    ///      旧值 = `CurrentBlock − Increase`（Increase 是 3/4），这个组合基本唯一。
    private static void AfterBuffFromPlay(CardModel __instance)
    {
        AfterGrowChanged(__instance);
    }

    /// <summary>`IncreasedBlock` setter / `BuffFromPlay` 之后统一走这里：把成长写回藏品册。</summary>
    ///
    /// ⚠⚠ **绝对不许在这里 `CardModel.FromSerializable(...)`**（用户 2026-10-30 实机：进战斗就黑屏崩溃）。
    ///   `FromSerializable` 会**把条目里的 `[SavedProperty]` 重新赋值** ⇒ 又触发 `IncreasedBlock` 的 setter
    ///   ⇒ 又进这个 postfix ⇒ 又反序列化 … **无限递归 → 栈溢出 → 黑屏崩溃**。
    ///   现在：① `[ThreadStatic]` 重入锁；② 只用 `SerializableCard.Id` / `.Props.ints` 读数据（不反序列化）。
    private static void AfterGrowChanged(CardModel __instance)
    {
        if (_persisting) return;             // 重入锁：本线程正在写回时直接退出
        _persisting = true;
        try
        {
            if (__instance is not MegaCrit.Sts2.Core.Models.Cards.GeneticAlgorithm ga) return;

            string entryName = "?";
            try { entryName = __instance.Id.Entry; } catch { }
            int increased = -1, inc = -1, newBlock = -1;
            try { increased = ga.IncreasedBlock; } catch { }
            // ⚠ `BuffFromPlay` 是「先 IncreasedBlock += extra，后 CurrentBlock = 1 + IncreasedBlock」，
            //   主钩子挂在 setter 上 ⇒ 此刻 `CurrentBlock` 还是旧值，格挡一律按 `1 + IncreasedBlock` 算。
            try { newBlock = increased >= 0 ? 1 + increased : ga.CurrentBlock; } catch { }
            try { inc = ga.DynamicVars["Increase"].IntValue; } catch { }

            // 诊断：确认钩子真的被调到（每张牌只报一次）
            if (GrowLogged.Add($"fire:{entryName}"))
            {
                Logger.Log($"[CharBoss] 收藏家兼容：成长钩子命中「{entryName}」" +
                           $"（IncreasedBlock={increased}，按此算格挡={newBlock}，Increase={inc}）");
            }

            // ── 玩家 + 藏品册列表 ──
            MegaCrit.Sts2.Core.Entities.Players.Player? owner = null;
            try { owner = __instance.Owner; } catch { owner = null; }
            if (owner == null) owner = AnyPlayerWithCollectibles();

            var deckField = FindType(CollectiblesModelFullName)?
                .GetField("CollectorDeck",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            var deck = deckField?.GetValue(null);
            if (deck == null) return;
            var getMethod = deck.GetType().GetMethod("Get", new[] { typeof(MegaCrit.Sts2.Core.Entities.Players.Player) });
            var setMethod = deck.GetType().GetMethods().FirstOrDefault(m =>
                m.Name == "Set" && m.GetParameters().Length == 2 &&
                m.GetParameters()[0].ParameterType == typeof(MegaCrit.Sts2.Core.Entities.Players.Player));
            if (getMethod == null || setMethod == null) return;
            if (owner == null) { OwnerLoggedOnce("拿不到玩家"); return; }

            if (getMethod.Invoke(deck, new object?[] { owner }) is not System.Collections.IList list) return;
            if (list.Count == 0) { OwnerLoggedOnce("藏品册是空的"); return; }

            // ── ① 快路径：实例映射 ──
            int index = -1;
            if (CollectibleIndex.TryGetValue(owner, out var map) && map.TryGetValue(__instance, out int fast))
            {
                index = fast;
            }
            else
            {
                foreach (var pair in CollectibleIndex)
                {
                    if (pair.Value.TryGetValue(__instance, out int idx)) { owner = pair.Key; index = idx; break; }
                }
            }

            // ── ② 慢路径：**不反序列化**，直接读条目里的 Id 与 Props.ints 找「同一张牌 + 旧格挡值」 ──
            if (index < 0 && inc >= 0 && newBlock >= 0)
            {
                int oldBlock = newBlock - inc;
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i] is not MegaCrit.Sts2.Core.Saves.Runs.SerializableCard sc) continue;
                    string savedEntry = "?";
                    try { savedEntry = sc.Id.Entry; } catch { }
                    if (savedEntry != entryName) continue;
                    int? savedBlock = IntProp(sc, "CurrentBlock");
                    if (savedBlock == null || savedBlock.Value != oldBlock) continue;
                    index = i;
                    if (GrowLogged.Add($"slow:{entryName}"))
                    {
                        Logger.Log($"[CharBoss] 收藏家兼容：藏品「{entryName}」按旧格挡值匹配到藏品册第 {i} 条" +
                                   $"（条目里 {savedBlock.Value} → 打出后 {newBlock}）");
                    }
                    break;
                }
            }

            if (index < 0 || index >= list.Count)
            {
                if (GrowLogged.Add($"miss:{entryName}"))
                {
                    var ids = new List<string>();
                    foreach (var item in list)
                    {
                        string id = "?";
                        try { if (item is MegaCrit.Sts2.Core.Saves.Runs.SerializableCard sc2) id = sc2.Id.Entry; }
                        catch { /* 读不出来就用 ? */ }
                        ids.Add(id);
                    }
                    Logger.Log($"[CharBoss] 收藏家兼容：成长钩子后没能定位到藏品册条目（{entryName}）" +
                               $"；藏品册现有 [{string.Join(",", ids)}]");
                }
                return;
            }

            // ── 幂等检查：条目里已经是新值就别再写（避免每次反序列化都白跑一趟反射） ──
            if (list[index] is MegaCrit.Sts2.Core.Saves.Runs.SerializableCard current &&
                IntProp(current, "CurrentBlock") == newBlock && IntProp(current, "IncreasedBlock") == increased)
            {
                return;
            }

            // ── 写回（`Set` 要一份新列表，它自己的 `AddCollectible` 也是复制一份再 Set） ──
            if (Activator.CreateInstance(list.GetType()) is not System.Collections.IList copy) return;
            foreach (var item in list) copy.Add(item);
            copy[index] = __instance.ToSerializable();
            setMethod.Invoke(deck, new object?[] { owner, copy });

            if (GrowLogged.Add($"{entryName}#{index}"))
            {
                Logger.Log($"[CharBoss] 收藏家兼容：藏品「{entryName}」# {index} 打出后的成长已写回藏品册" +
                           $"（本次格挡 {newBlock}，累计 +{SafeIncreased(ga)}）");
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 收藏家兼容：写回藏品成长失败: " + e.Message);
        }
        finally { _persisting = false; }
    }

    /// <summary>写回重入锁（`FromSerializable` 会重新触发 setter，必须挡住）。</summary>
    [ThreadStatic] private static bool _persisting;

    /// <summary>从 `SerializableCard.Props.ints` 里读一个 `[SavedProperty]` 的整数值（**不反序列化**）。</summary>
    private static int? IntProp(MegaCrit.Sts2.Core.Saves.Runs.SerializableCard sc, string name)
    {
        try
        {
            var ints = sc.Props?.ints;
            if (ints == null) return null;
            foreach (var p in ints)
            {
                if (p.name == name) return p.value;
            }
        }
        catch { /* 忽略 */ }
        return null;
    }

    private static int SafeIncreased(MegaCrit.Sts2.Core.Models.Cards.GeneticAlgorithm ga)
    {
        try { return ga.IncreasedBlock; } catch { return -1; }
    }

    private static readonly HashSet<string> _ownerLogs = new(StringComparer.Ordinal);

    private static void OwnerLoggedOnce(string why)
    {
        if (_ownerLogs.Add(why)) Logger.Log($"[CharBoss] 收藏家兼容：写回藏品成长跳过 —— {why}");
    }

    /// <summary>哪一个玩家有藏品册（拿不到 Owner 时的兜底）。</summary>
    private static MegaCrit.Sts2.Core.Entities.Players.Player? AnyPlayerWithCollectibles()
    {
        try
        {
            foreach (var pair in CollectibleIndex)
            {
                if (pair.Value.Count > 0) return pair.Key;
            }
            var state = Core.MultiBossPlan.RunStateOrNull();
            if (state?.Players != null)
            {
                foreach (var p in state.Players)
                {
                    if (IsCollectorCharacter(p.Character)) return p;
                }
            }
        }
        catch { /* 忽略 */ }
        return null;
    }

    private static readonly HashSet<string> _doubleLogged = new(StringComparer.Ordinal);

    /// <summary>
    /// `DoubleTroublePower.ModifyCardPlayCount` 之后：如果这张牌是**我们的藏品牌**（原版牌，不是 `ICollectible`），
    /// 就把它补进「打出两次」的名单里。
    ///
    /// 只在这一种情况下动手：`playCount` 没被原版加过（`__result == playCount`）、
    /// 牌的拥有者就是这层 power 的主人、且牌 id 在我们掉落表里。
    /// </summary>
    private static void AfterModifyCardPlayCount(
        MegaCrit.Sts2.Core.Models.PowerModel __instance,
        CardModel card,
        int playCount,
        ref int __result)
    {
        try
        {
            if (card == null || __instance == null) return;
            if (__result > playCount) return;                     // 原版已经 +1 了（它自己那批藏品牌）

            var owner = __instance.Owner;
            if (owner == null) return;
            var cardOwner = card.Owner?.Creature;
            if (cardOwner == null || !ReferenceEquals(cardOwner, owner)) return;

            string entry;
            try { entry = card.Id.Entry; } catch { return; }
            if (!LootEntries().Contains(entry)) return;           // 只补我们掉落表里那几张

            __result = playCount + 1;
            if (_doubleLogged.Add(entry))
            {
                Logger.Log($"[CharBoss] 收藏家兼容：祸不单行 → 藏品牌「{entry}」这次也会被打出两次" +
                           $"（它是原版牌、不是它自己的 ICollectible，原版判断永远 +0）");
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 收藏家兼容：祸不单行补正失败: " + e.Message);
        }
    }

    private static HashSet<string>? _lootEntries;

    /// <summary>掉落表里那 9 张牌的 `Id.Entry`（只算一次）。</summary>
    private static HashSet<string> LootEntries()
    {
        if (_lootEntries != null) return _lootEntries;
        var set = new HashSet<string>();
        foreach (var loot in LootTable.Values)
        {
            try { set.Add(ModelDb.GetId(loot.Card).Entry); } catch { /* 忽略 */ }
            try { set.Add("SNEAKY"); } catch { /* 忽略 */ }
        }
        _lootEntries = set;
        return set;
    }

    /// <summary>反射 new 一个 `CollectibleReward(card, player)`（它继承 sts2 的 `Reward`，可直接用）。</summary>
    private static MegaCrit.Sts2.Core.Rewards.Reward? CreateCollectibleReward(CardModel card,
                                                                            MegaCrit.Sts2.Core.Entities.Players.Player player)
    {
        try
        {
            var type = FindType(CollectibleRewardFullName);
            var ctor = type?.GetConstructor(new[] { typeof(CardModel), typeof(MegaCrit.Sts2.Core.Entities.Players.Player) });
            if (ctor == null) return null;
            return ctor.Invoke(new object?[] { card, player }) as MegaCrit.Sts2.Core.Rewards.Reward;
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 收藏家兼容：造 CollectibleReward 失败: " + e.Message);
            return null;
        }
    }

    /// <summary>
    /// 找崩坠的类型：先按全名，找不到再退化成**简单类名**跨程序集查找。
    /// 官方（创意工坊）版把角色拆成 `Awakened.dll` / `Hexaghost.dll` …，命名空间可能和旧改版不同，
    /// 写死全名会静默失效（用户 2026-10-24 要求兼容官方版）。
    /// </summary>
    private static Type? FindType(string fullName) => ModTypeFinder.Find(fullName);

    /// <summary>这个角色模型是不是收藏家（按类型简单名判，不依赖命名空间）。</summary>
    private static bool IsCollectorCharacter(MegaCrit.Sts2.Core.Models.CharacterModel? character)
    {
        try
        {
            var type = character?.GetType();
            if (type == null) return false;
            if ((type.Namespace ?? string.Empty).IndexOf("Collector", StringComparison.OrdinalIgnoreCase) >= 0
                && type.Name == "Collector")
                return true;
            return type.FullName == CollectorCharacterFullName;
        }
        catch { return false; }
    }

    /// <summary>藏品模型类型全名。</summary>
    private const string CollectiblesModelFullName = "Collector.CollectorCode.Core.CollectiblesModel";

    /// <summary>「思维抽取」牌类型全名（英文 Brain Drain，中文「思维抽取」；打敌人并抽走它的藏品牌）。</summary>
    private const string BrainDrainFullName = "Collector.CollectorCode.Cards.Uncommon.BrainDrain";

    /// <summary>
    /// **(Boss 定义 Key, 幕号 1 起) → (藏品卡, 是否升级)**。用户 2026-10-23 指定的掉落表；
    /// 2026-10-24 追加：**易伤战 → 主宰+**、**涂毒猎 → 涂毒+**、**物理机 → 超能光束+**，
    /// 其它 Boss 的藏品一律不动、不升级。
    /// </summary>
    private static readonly Dictionary<(string Key, int Act), (Type Card, bool Upgraded)> LootTable = new()
    {
        [("IRONCLAD_BOSS", 1)] = (typeof(MegaCrit.Sts2.Core.Models.Cards.Aggression), false),   // 愤怒战 → 好勇斗狠
        [("IRONCLAD_BOSS", 3)] = (typeof(MegaCrit.Sts2.Core.Models.Cards.Dominate), false),     // 易伤战 → 主宰（用户 2026-10-25：降回普通版）
        // 二层铁甲战士（放血战）只有这一套设计，所以直接放表里最稳（不依赖读房间遭遇）。
        [("IRONCLAD_BOSS", 2)] = (typeof(MegaCrit.Sts2.Core.Models.Cards.CrimsonMantle), false), // 放血战 → 绯红披风（用户 2026-10-29）
        [("REGENT_BOSS", 1)] = (typeof(MegaCrit.Sts2.Core.Models.Cards.CrashLanding), false),   // 碎屑储君 → 迫降
        [("NECROBINDER_BOSS", 1)] = (typeof(MegaCrit.Sts2.Core.Models.Cards.Misery), false),    // 灾厄骨 → 苦难
        [("SILENT_BOSS", 2)] = (typeof(MegaCrit.Sts2.Core.Models.Cards.Envenom), true),         // 涂毒猎 → 涂毒+
        [("DEFECT_BOSS", 1)] = (typeof(MegaCrit.Sts2.Core.Models.Cards.GeneticAlgorithm), false),  // 黑球机 → 遗传算法（用户 2026-10-27）
        [("DEFECT_BOSS", 2)] = (typeof(MegaCrit.Sts2.Core.Models.Cards.Hyperbeam), true),       // 物理机 → 超能光束+
        [("NECROBINDER_BOSS", 2)] = (typeof(MegaCrit.Sts2.Core.Models.Cards.GlimpseBeyond), false), // 灵魂骨 → 彼岸一窥
        [("REGENT_BOSS", 2)] = (typeof(MegaCrit.Sts2.Core.Models.Cards.BeatIntoShape), false),  // 铸剑储 → 锻打成型
        [("DEFECT_BOSS", 3)] = (typeof(MegaCrit.Sts2.Core.Models.Cards.CreativeAi), false),     // 能力机 → 创造性AI
        [("REGENT_BOSS", 3)] = (typeof(MegaCrit.Sts2.Core.Models.Cards.Arsenal), false),        // 无色储 → 武器库
        [("SILENT_BOSS", 3)] = (typeof(MegaCrit.Sts2.Core.Models.Cards.BladeOfInk), false),     // 刀猎 → 墨之刃
        [("NECROBINDER_BOSS", 3)] = (typeof(MegaCrit.Sts2.Core.Models.Cards.Eradicate), false), // 根除骨 → 根除
        // 一层「奇巧猎」：单人 → 余像；多人 → 鬼祟+。见 ResolveLoot（这张表只放单人那一份占位）
        [("SILENT_BOSS", 1)] = (typeof(MegaCrit.Sts2.Core.Models.Cards.Afterimage), false),
    };

    /// <summary>
    /// 取这一档的掉落（**支持单人/多人不同**）。
    ///
    /// 用户 2026-10-26 的一层「奇巧猎」：**藏品 = 余像（单人）/ 鬼祟+（多人）** ——
    /// 同一只 Boss 在两种人数下掉不同的藏品牌，所以不能只查静态表。
    /// </summary>
    private static bool ResolveLoot(CharBossMonster boss, int act, out Type cardType, out bool upgraded)
    {
        cardType = typeof(MegaCrit.Sts2.Core.Models.Cards.Afterimage);
        upgraded = false;
        try
        {
            if (boss?.Def == null) return false;

            // ── **变体专属**（用户 2026-10-29 指定）──
            // 这几只和同角色同幕的另一个设计落在同一格（掉落表是按「角色 + 幕」索引的），
            // 所以必须按**这一场是哪套设计**区分。
            // ★ v0.2.333（用户 2026-11-03 报「炼药猎死亡提示显示成刀猎」查出来的同一个根因）：
            //   优先用**生成这只 Boss 时记下的变体身份**（`boss.SpawnVariantKey`）。
            //   原来只看 `CurrentEncounterEntry()`，而战斗结束 / 玩家死亡那一刻 CombatState 已经换了
            //   ⇒ 读不到房间遭遇就掉回下面按 (角色, 幕) 查的静态表，于是炼药猎会掉刀猎的藏品。
            // ★ v0.2.360（用户 2026-11-04「联动收藏家藏品」点名三只）：
            //   再用**变体 key** 直接兜一层 —— `SpawnVariantKey` 是生成这只 Boss 时钉下的，
            //   不依赖「能不能读回房间遭遇」。下面那个按遭遇名的 switch 在同名情况下结果一样，
            //   但万一 `EncounterOf`/`CurrentEncounterEntry` 取不到（战斗结束时 CombatState 已被换掉），
            //   就会掉回按 (角色, 幕) 查的静态表，而这三只和它们的兄弟设计**落在同一格** ⇒ 掉错牌。
            switch (boss.SpawnVariantKey)
            {
                case "SILENT_SNAKEBITE":     // 一层 蛇咬猎（用户 2026-11-11：藏品 = 群蛇形态）
                    // 她和同层「奇巧猎」落在同一格（掉落表按「角色 + 幕」索引 ⇒ 会掉余像）⇒ 必须点名。
                    cardType = typeof(MegaCrit.Sts2.Core.Models.Cards.SerpentForm);   // 群蛇形态
                    upgraded = false;
                    Logger.Log("[CharBoss] 收藏家藏品：蛇咬猎 → 群蛇形态");
                    return true;
                case "SILENT_BREWER":        // 三层 炼药猎（用户 2026-10-26：藏品 = 炼制药水；否则会掉刀猎的墨之刃）
                    cardType = typeof(MegaCrit.Sts2.Core.Models.Cards.Alchemize);    // 炼制药水
                    upgraded = false;
                    Logger.Log("[CharBoss] 收藏家藏品：炼药猎 → 炼制药水");
                    return true;
                case "SILENT_SNECKO":        // 二层 蛇眼猎
                    cardType = typeof(MegaCrit.Sts2.Core.Models.Cards.BulletTime);   // 子弹时间
                    upgraded = false;
                    Logger.Log("[CharBoss] 收藏家藏品：蛇眼猎 → 子弹时间");
                    return true;
                case "NECROBINDER_OSTY":     // 三层 养手骨
                    cardType = typeof(MegaCrit.Sts2.Core.Models.Cards.Reanimate);    // 死者苏生
                    upgraded = false;
                    Logger.Log("[CharBoss] 收藏家藏品：养手骨 → 死者苏生");
                    return true;
                case "DEFECT_EVENT":         // 三层 事件机
                    cardType = typeof(MegaCrit.Sts2.Core.Models.Cards.AllForOne);    // 万物一心
                    upgraded = false;
                    Logger.Log("[CharBoss] 收藏家藏品：事件机 → 万物一心");
                    return true;
                case "SILENT_GRAND_FINALE":  // 三层 华丽猎（用户 2026-11-09：藏品 = 计划妥当）
                    cardType = typeof(MegaCrit.Sts2.Core.Models.Cards.WellLaidPlans);
                    upgraded = false;
                    Logger.Log("[CharBoss] 收藏家藏品：华丽猎 → 计划妥当");
                    return true;
                case "NECROBINDER_FLESH":        // 一层 血肉骨（用户 2026-11-10：藏品 = 命运同担）
                    cardType = typeof(MegaCrit.Sts2.Core.Models.Cards.SharedFate);
                    upgraded = false;
                    Logger.Log("[CharBoss] 收藏家藏品：血肉骨 → 命运同担");
                    return true;
                case "NECROBINDER_ETHEREAL":     // 二层 虚无骨（用户 2026-11-09：藏品 = 幻景）
                    cardType = typeof(MegaCrit.Sts2.Core.Models.Cards.Eidolon);
                    upgraded = false;
                    Logger.Log("[CharBoss] 收藏家藏品：虚无骨 → 幻景");
                    return true;
            }

            string? entry = null;
            try { entry = BossKeys.EncounterOf(boss.SpawnVariantKey)?.Id.Entry; } catch { entry = null; }
            if (string.IsNullOrEmpty(entry))
            {
                try { entry = BossLineups.CurrentEncounterEntry(); } catch { entry = null; }
            }
            switch (entry)
            {
                case "IRONCLAD_BLOOD_BOSS_ENCOUNTER":      // 二层 放血战
                    cardType = typeof(MegaCrit.Sts2.Core.Models.Cards.CrimsonMantle);   // 绯红披风
                    upgraded = false;
                    return true;
                case "IRONCLAD_BOULDER_BOSS_ENCOUNTER":    // 三层 巨石战
                    cardType = typeof(MegaCrit.Sts2.Core.Models.Cards.PrimalForce);     // 原始力量
                    upgraded = false;
                    return true;
                case "IRONCLAD_JUGGLER_BOSS_ENCOUNTER":     // 二层 杂耍战
                    cardType = typeof(MegaCrit.Sts2.Core.Models.Cards.PactsEnd);        // 契约终结（用户 2026-11-03 指定，原为「杂耍」）
                    upgraded = false;
                    return true;
case "REGENT_STAR_BOSS_ENCOUNTER":         // 三层 辉星储
                    cardType = typeof(MegaCrit.Sts2.Core.Models.Cards.VoidForm);        // 虚空形态
                    upgraded = false;
                    return true;
                case "REGENT_CHOICE_BOSS_ENCOUNTER":       // 三层 抉择储（用户 2026-11-10 指定：藏品 = 天际钻头）
                    cardType = typeof(MegaCrit.Sts2.Core.Models.Cards.HeavenlyDrill);
                    upgraded = false;
                    return true;
                case "SILENT_GRAND_FINALE_BOSS_ENCOUNTER":   // 三层 华丽猎
                    cardType = typeof(MegaCrit.Sts2.Core.Models.Cards.WellLaidPlans);   // 计划妥当
                    upgraded = false;
                    return true;
                case "NECROBINDER_OSTY_BOSS_ENCOUNTER":    // 三层 养手骨
                    cardType = typeof(MegaCrit.Sts2.Core.Models.Cards.Reanimate);      // 死者苏生
                    upgraded = false;
                    return true;
                case "NECROBINDER_FLESH_BOSS_ENCOUNTER":       // 一层 血肉骨（用户 2026-11-10：藏品 = 命运同担）
                    cardType = typeof(MegaCrit.Sts2.Core.Models.Cards.SharedFate);
                    upgraded = false;
                    return true;
                case "NECROBINDER_ETHEREAL_BOSS_ENCOUNTER":    // 二层 虚无骨（用户 2026-11-09：藏品 = 幻景）
                    cardType = typeof(MegaCrit.Sts2.Core.Models.Cards.Eidolon);
                    upgraded = false;
                    return true;
                case "SILENT_SNECKO_BOSS_ENCOUNTER":   // 二层 蛇眼猎
                    cardType = typeof(MegaCrit.Sts2.Core.Models.Cards.BulletTime);     // 子弹时间
                    upgraded = false;
                    return true;
                case "DEFECT_THUNDER_BOSS_ENCOUNTER":  // 一层 电球机（用户 2026-11-05 指定：藏品 = 信号增强）
                    cardType = typeof(MegaCrit.Sts2.Core.Models.Cards.SignalBoost);
                    upgraded = false;
                    return true;
                case "REGENT_MINION_BOSS_ENCOUNTER":   // 二层 仆从储（用户 2026-11-05 指定：藏品 = 护驾！！！）
                    cardType = typeof(MegaCrit.Sts2.Core.Models.Cards.Guards);
                    upgraded = false;
                    return true;
                case "IRONCLAD_STRIKE_BOSS_ENCOUNTER": // 一层 打击战（用户 2026-11-07 指定：藏品 = 地狱狂徒）
                    cardType = typeof(MegaCrit.Sts2.Core.Models.Cards.Hellraiser);
                    upgraded = false;
                    return true;                case "DEFECT_RAINBOW_BOSS_ENCOUNTER":  // 二层 彩虹机（用户 2026-11-07 指定：藏品 = 陨石打击）
                    cardType = typeof(MegaCrit.Sts2.Core.Models.Cards.MeteorStrike);
                    upgraded = false;
                    return true;
                case "DEFECT_STATUS_BOSS_ENCOUNTER":     // 三层 状态机（用户 2026-11-05 指定：藏品 = 散射炮）
                    cardType = typeof(MegaCrit.Sts2.Core.Models.Cards.FlakCannon);
                    upgraded = false;
                    return true;
                case "REGENT_BOMBARD_BOSS_ENCOUNTER":  // 一层 轰击储（用户 2026-11-05 指定：藏品 = 轰击）
                    cardType = typeof(MegaCrit.Sts2.Core.Models.Cards.Bombardment);
                    upgraded = false;
                    return true;
                case "DEFECT_EVENT_BOSS_ENCOUNTER":   // 三层 事件机
                    cardType = typeof(MegaCrit.Sts2.Core.Models.Cards.AllForOne);      // 万物一心（一心化万的核心牌）
                    upgraded = false;
                    return true;
                case "REGENT_KING_BOSS_ENCOUNTER":    // 二层 王储
                    cardType = typeof(MegaCrit.Sts2.Core.Models.Cards.MonarchsGaze);   // 王之凝视（用户 2026-11-03 指定，原为「征服者」）
                    upgraded = false;
                    return true;
            }

            // 一层静默猎手（奇巧猎）：按人数分岔
            if (boss.Def.Key == "SILENT_BOSS" && act == 1)
            {
                if (MultiBossPlan.WantsSecondBoss(out int players) && players >= 2)
                {
                    cardType = typeof(MegaCrit.Sts2.Core.Models.Cards.Sneaky);   // 多人：鬼祟
                    upgraded = true;                                            //       鬼祟+
                }
                else
                {
                    cardType = typeof(MegaCrit.Sts2.Core.Models.Cards.Afterimage);   // 单人：余像
                    upgraded = false;
                }
                return true;
            }

            // 三层静默猎手有**两套设计**（刀猎 / 炼药猎），藏品也不同（用户 2026-10-26 指定）：
            //   · 刀猎   → 墨之刃（`LootTable` 里那条，不升级）
            //   · 炼药猎 → **炼制药水**（`Alchemize`，不升级）
            // 判据与「这一场用哪套设计」同源：`BossLineups.BrewerActive()`
            // （战斗内以**房间遭遇**为准，两端一致；控制台 `fight SILENT_BREWER_BOSS_ENCOUNTER` 也判得对）。
            if (boss.Def.Key == "SILENT_BOSS" && act == 3 && BossLineups.BrewerActive())
            {
                cardType = typeof(MegaCrit.Sts2.Core.Models.Cards.Alchemize);
                upgraded = false;
                return true;
            }

            if (!LootTable.TryGetValue((boss.Def.Key, act), out var loot)) return false;
            cardType = loot.Card;
            upgraded = loot.Upgraded;
            return true;
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 收藏家兼容：取掉落失败（按没有处理）: " + e.Message);
            return false;
        }
    }

    /// <summary>
    /// 她被击败时调用（`CharBossMonster.BeforeRemovedFromRoom`）。
    /// 只有当「这一场真的打赢了（她死了）」+「本机玩家是收藏家」+「表里有这个 Boss」时才发。
    /// </summary>
    internal static void OnBossDefeated(CharBossMonster boss)
    {
        try
        {
            if (boss?.Creature == null || boss.Creature.IsAlive) return;   // 玩家死/主动退出不算
            if (boss.Def == null) return;

            // 同一只 Boss 只处理一次（`BeforeRemovedFromRoom` 可能被调多次；多只随从也会再进来）
            if (!HandledBosses.Add(boss)) return;

            // 换了一场战斗：把上一场的残留清掉（正常情况 AfterCombatEnd 已经清过）
            var combat = boss.CombatState;
            if (combat != null && !ReferenceEquals(combat, _defeatedCombat))
            {
                if (_defeatedCombat != null) Defeated.Clear();
                _defeatedCombat = combat;
            }

            int act = boss.ActIndex + 1;                                    // 幕号 1 起
            if (!ResolveLoot(boss, act, out var cardType, out var lootUpgraded)) return;

            var player = FindCollectorPlayer(boss);
            if (player == null) return;                                     // 本机不是收藏家：不管

            var card = CreateCard(player, cardType, lootUpgraded);
            if (card == null)
            {
                Logger.Log($"[CharBoss] 收藏家兼容：造不出藏品牌 {cardType.Name}");
                return;
            }

            // 3 层：**没有战斗奖励就进不了藏品堆**（用户 2026-10-23 订正：
            // 「3 层 boss 的藏品卡不作弊进不了藏品堆」）⇒ 这里**什么都不做**，只记日志，
            // 绝对不能直接 AddCollectible（那等于白送）。
            if (act >= 3)
            {
                Logger.Log($"[CharBoss] 收藏家兼容：{boss.Def.Key} act{act} 是三层，没有战斗奖励 ⇒ " +
                           $"「{card.Title}」不发放（三层藏品只能靠正常途径，不作弊）");
                return;
            }

            // 1/2 层：记下来，等 `CollectorModel.AfterCombatEnd` 那一刻挂成**结算界面的藏品奖励**
            if (_patched)
            {
                Defeated.Add((boss.Def.Key, act, cardType, lootUpgraded));
                Logger.Log($"[CharBoss] 收藏家兼容：{boss.Def.Key} act{act} → 记下「{card.Title}」，" +
                           "等结算时按收藏家的规则挂成藏品奖励");
                return;
            }

            // 兜底（动态挂载失败时）：按收藏家自己的代价（3 精华）走 SyncAdd
            int essence = GetEssence(player);
            if (essence < CollectibleEssenceCost)
            {
                Logger.Log($"[CharBoss] 收藏家兼容：{boss.Def.Key} act{act} 掉落「{card.Title}」" +
                           $"需要 {CollectibleEssenceCost} 精华，当前只有 {essence} ⇒ 这次不白送（和它自己的 CANT_AFFORD 一致）");
                return;
            }

            if (InvokeCollectibles("SyncAdd", player, card, CollectibleEssenceCost))
                Logger.Log($"[CharBoss] 收藏家兼容：{boss.Def.Key} act{act} → 藏品「{card.Title}」" +
                           $"已进藏品册（花费 {CollectibleEssenceCost} 精华，现有 {GetEssence(player)}）");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 收藏家兼容失败（忽略）: " + e.Message);
        }
    }

    /// <summary>本机玩家是不是收藏家（多人时只处理本地那一位：SyncAdd 内部也只看 `LocalContext.IsMe`）。</summary>
    private static Player? FindCollectorPlayer(CharBossMonster boss)
    {
        try
        {
            var combat = boss.CombatState;
            if (combat == null) return null;

            foreach (var creature in combat.PlayerCreatures)
            {
                var player = creature?.Player;
                if (player == null || creature!.Monster != null) continue;
                var character = player.Character;
                if (character == null) continue;
                if (IsCollectorCharacter(character)) return player;
            }
        }
        catch { /* 忽略 */ }
        return null;
    }

    /// <summary>
    /// 造一张**结算奖励用**的藏品牌实例。
    ///
    /// ⚠ 这里**不能用** `combat.CreateCard(...)`：那是**战斗作用域**的卡（带 combat id / 牌堆归属），
    /// 而这张牌要挂在结算界面的奖励里、还会被存档序列化 —— 用户 2026-10-24 实测：
    /// 打死二层那两只之后奖励界面出现一堆魂粹，**存档再读直接报错**。
    /// 崩坠自己也是这么做的（反编译 `CollectorModel.AfterCombatEnd`）：
    /// <code>
    /// room.AddExtraReward(p, new CollectibleReward(c.ToMutable(), p));
    /// </code>
    /// 也就是**用 `ToMutable()` 造一张脱离战斗的牌**。
    /// </summary>
    private static CardModel? CreateRewardCard(Player player, Type cardType, bool upgraded)
    {
        try
        {
            var id = ModelDb.GetId(cardType);
            var canonical = ModelDb.GetById<CardModel>(id);
            if (canonical == null) return null;

            var card = canonical.ToMutable();
            if (card == null) return null;
            card.Owner ??= player;
            if (upgraded)
            {
                try { if (!card.IsUpgraded) card.UpgradeInternal(); }
                catch (Exception e) { Logger.Log("[CharBoss] 收藏家兼容：升级奖励藏品牌失败: " + e.Message); }
            }
            ApplyCollectibleKeywords(card);
            return card;
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 收藏家兼容：造奖励藏品牌失败: " + e.Message);
            return null;
        }
    }

    /// <summary>造一张属于该玩家的可变牌实例（原版牌的 canonical 不能直接进藏品堆）。</summary>
    private static CardModel? CreateCard(Player player, Type cardType, bool upgraded = false)
    {
        try
        {
            var id = ModelDb.GetId(cardType);
            var canonical = ModelDb.GetById<CardModel>(id);
            if (canonical == null) return null;

            var combat = player.Creature?.CombatState;
            var card = combat != null ? combat.CreateCard(canonical, player) : canonical.ToMutable();
            if (card == null) return null;
            card.Owner ??= player;
            if (upgraded)
            {
                try { if (!card.IsUpgraded) card.UpgradeInternal(); }
                catch (Exception e) { Logger.Log("[CharBoss] 收藏家兼容：升级藏品牌失败: " + e.Message); }
            }

            // 用户 2026-10-23：「藏品牌应当带有**虚无**和**消耗**」，
            // 但**能力牌只带虚无、不带消耗**（技能/攻击牌两者都有）。
            ApplyCollectibleKeywords(card);
            return card;
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 收藏家兼容：造牌失败: " + e.Message);
            return null;
        }
    }

    /// <summary>
    /// 藏品牌的关键词（用户 2026-10-23）：
    /// **所有**藏品牌都带 **虚无（Ethereal）**；**技能 / 攻击**牌再额外带 **消耗（Exhaust）**，
    /// **能力牌只有虚无、不带消耗**。
    /// </summary>
    private static void ApplyCollectibleKeywords(CardModel card)
    {
        AddKeywordIfMissing(card, MegaCrit.Sts2.Core.Entities.Cards.CardKeyword.Ethereal);

        bool isPower;
        try { isPower = card.Type == MegaCrit.Sts2.Core.Entities.Cards.CardType.Power; }
        catch { isPower = false; }
        if (!isPower)
            AddKeywordIfMissing(card, MegaCrit.Sts2.Core.Entities.Cards.CardKeyword.Exhaust);
    }

    /// <summary>给牌补一个关键词（已经有了就不动）。</summary>
    private static void AddKeywordIfMissing(CardModel card, MegaCrit.Sts2.Core.Entities.Cards.CardKeyword keyword)
    {
        try
        {
            if (card.Keywords != null && card.Keywords.Contains(keyword)) return;
            card.AddKeyword(keyword);
        }
        catch (Exception e)
        {
            Logger.Log($"[CharBoss] 收藏家兼容：加关键词 {keyword} 失败: {e.Message}");
        }
    }

    /// <summary>反射调 `CollectiblesModel` 的静态方法。</summary>
    private static bool InvokeCollectibles(string methodName, params object?[] args)
    {
        try
        {
            var type = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => { try { return a.GetType(CollectiblesModelFullName, false); } catch { return null; } })
                .FirstOrDefault(t => t != null);
            if (type == null) return false;      // 没装崩坠

            var method = type.GetMethod(methodName,
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Static);
            if (method == null) return false;

            method.Invoke(null, args);
            return true;
        }
        catch (Exception e)
        {
            Logger.Log($"[CharBoss] 收藏家兼容：调用 {methodName} 失败: {e.InnerException?.Message ?? e.Message}");
            return false;
        }
    }

    /// <summary>读收藏家的「精华」（`PlayerExtensions.Essence` → 反射读 `EssenceModel`）。</summary>
    private static int GetEssence(Player player)
    {
        try
        {
            // 找不到静态扩展类就再按**简单类名**找一遍（官方版可能换了命名空间）
            var extType = AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => { try { return a.GetType("Collector.CollectorCode.Extensions.PlayerExtensions", false); } catch { return null; } })
                .FirstOrDefault(t => t != null)
                ?? ModTypeFinder.Find("PlayerExtensions");
            var prop = extType?.GetProperty("Essence",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (prop != null)
            {
                var value = prop.GetValue(null, new object?[] { player });
                if (value is int i) return i;
            }
        }
        catch { /* 读不到按 0 */ }
        return 0;
    }
}
