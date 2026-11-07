using System;
using System.Reflection;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using CharacterBosses.Monsters;

namespace CharacterBosses.Core;

/// <summary>
/// 给 Boss 造一个「影子 Player」：卡牌系统要求出牌主体是 Player（卡组/能量/手牌/遗物都挂在它上面），
/// 所以把 Boss 的 Creature 与一个 Player 互相绑定。
/// 唯一的非官方操作就是这里的反射；其余全部走公开 API。
/// </summary>
public static class ShadowPlayerFactory
{
    private static readonly FieldInfo? PlayerCreatureField =
        typeof(Player).GetField("<Creature>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly FieldInfo? CreaturePlayerField =
        typeof(Creature).GetField("<Player>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);

    /// <summary>为 Boss 建好影子 Player 并把它的卡组灌进抽牌堆。失败返回 null（不会抛）。</summary>
    public static Player? Attach(CharBossMonster boss)
    {
        var combat = boss.CombatState;
        if (combat == null)
        {
            Logger.Log("[CharBoss] Attach: no combat state");
            return null;
        }

        var runState = combat.RunState;
        var def = boss.Def;
        var act = boss.ActIndex;

        var character = ModelDb.GetById<CharacterModel>(ModelDb.GetId(def.CharacterType));
        if (character == null)
        {
            Logger.Log($"[CharBoss] Attach: character {def.CharacterType} not found");
            return null;
        }

        if (runState.Players.Count == 0)
        {
            Logger.Log("[CharBoss] Attach: no players in run state");
            return null;
        }

        var shadow = Player.CreateForNewRun(character, runState.Players[0].UnlockState, ShadowNetId(boss));
        shadow.RunState = runState;
        shadow.ResetCombatState();

        if (PlayerCreatureField == null || CreaturePlayerField == null)
        {
            Logger.Log("[CharBoss] Attach: reflection fields missing, cannot bind creature");
            return null;
        }

        PlayerCreatureField.SetValue(shadow, boss.Creature);
        CreaturePlayerField.SetValue(boss.Creature, shadow);

        var pcs = shadow.PlayerCombatState;
        if (pcs == null)
        {
            Logger.Log("[CharBoss] Attach: PlayerCombatState is null");
            return null;
        }

        int added = 0;
        var deckNames = new List<string>();
        int ascension = SafeAscension(boss, runState);
        foreach (var cardType in BossLoadout.ResolveDeckTypes(def, act))
        {
            var canonical = ModelDb.GetById<CardModel>(ModelDb.GetId(cardType));
            if (canonical == null)
            {
                Logger.Log($"[CharBoss] Attach: card {cardType.Name} not found");
                continue;
            }
            var card = combat.CreateCard(canonical, shadow);
            // 进阶加成：某些牌在进阶 8/9 时是**升级版**（用户设计）
            if (ShouldUpgradeForAscension(def, cardType, ascension, act + 1))
            {
                try { if (!card.IsUpgraded) card.UpgradeInternal(); } catch { /* 忽略 */ }
            }
            pcs.DrawPile.AddInternal(card, -1, true);
            deckNames.Add(canonical.Id.Entry + (card.IsUpgraded ? "+" : string.Empty));
            added++;
        }

        // 打出实际拿到的卡组 —— 用户设计完卡组后看这一行就能确认生效
        Logger.Log($"[CharBoss] {def.Key} act{act + 1} deck({added}): {string.Join(",", deckNames)}");

        // 影子 Player 由 CreateForNewRun 建出来时已经带了角色的起始遗物，
        // 定义里配的遗物若与之重名就跳过，避免重复。
        int relicIndex = shadow.Relics.Count;
        int relicsAdded = 0;
        var relicTypes = new List<Type>(BossLoadout.ResolveRelicTypes(def, act));
        // 进阶遗物（例如进阶 8 的不死符文）
        foreach (var (minAsc, relicType) in def.AscensionRelics)
        {
            if (ascension >= minAsc && !relicTypes.Contains(relicType)) relicTypes.Add(relicType);
        }
        // 变体（三层「巨石战」这类第二套设计）**自己的**进阶遗物：接管时主定义这一行不生效。
        var variantRelics = def.VariantAscensionRelics(act);
        if (variantRelics != null)
        {
            foreach (var (minAsc, relicType) in variantRelics)
            {
                if (ascension < minAsc || relicTypes.Contains(relicType)) continue;
                relicTypes.Add(relicType);
                Logger.Log($"[CharBoss] 进阶 {ascension}（第 {act + 1} 幕·变体）额外获得遗物 {relicType.Name}");
            }
        }
        // 进阶遗物（按幕）：InsertBefore 用来控制状态条上的显示顺序
        foreach (var (minAsc, byAct, relicType, insertBefore) in def.AscensionRelicsByAct)
        {
            if (ascension < minAsc) continue;
            if (byAct != 0 && byAct != act + 1) continue;
            if (def.VariantTakesOver(act)) continue;   // 这一幕由变体接管 ⇒ 主定义那一行不生效
            if (relicTypes.Contains(relicType)) continue;
            int at = insertBefore == null ? -1 : relicTypes.IndexOf(insertBefore);
            if (at >= 0) relicTypes.Insert(at, relicType);
            else relicTypes.Add(relicType);
            Logger.Log($"[CharBoss] 进阶 {ascension}（第 {act + 1} 幕）额外获得遗物 {relicType.Name}" +
                       (at >= 0 ? $"（插在 {insertBefore!.Name} 前面）" : "（追加在末尾）"));
        }
        foreach (var relicType in relicTypes)
        {
            var canonical = ModelDb.GetById<RelicModel>(ModelDb.GetId(relicType));
            if (canonical == null)
            {
                Logger.Log($"[CharBoss] Attach: relic {relicType.Name} not found");
                continue;
            }
            try
            {
                if (shadow.GetRelicById(canonical.Id) != null) continue;
                shadow.AddRelicInternal(canonical.ToMutable(), relicIndex++, true);
                relicsAdded++;
            }
            catch (Exception e)
            {
                Logger.Log($"[CharBoss] Attach: add relic {relicType.Name} failed: {e.Message}");
            }
        }

        // ★ v0.2.349（用户 2026-11-03：「无界命匣还是没有替换缚魂命匣」）：
        //   `Player.CreateForNewRun` 建影子玩家时**已经给了角色的起始遗物**（亡灵契约师 = 「缚魂命匣」），
        //   而三层「养手骨」的设计是用「欧洛巴斯之触」把起始遗物升级成「无界命匣」
        //   ⇒ 定义里写的是无界命匣，于是她会**同时**带着缚魂命匣（角色自带的）和无界命匣（我们加的）。
        //   这里把那只角色自带的缚魂命匣摘掉，只留无界命匣。
        //   ⚠ 只在这一档生效（定义里真有 `PhylacteryUnbound` 时才动手）；
        //     其它亡灵契约师 Boss（灾厄骨 / 灵魂骨 / 根除骨）的列表里本来就是缚魂命匣，行为不变。
        try
        {
            if (relicTypes.Contains(typeof(MegaCrit.Sts2.Core.Models.Relics.PhylacteryUnbound)))
            {
                var boundId = ModelDb.GetId(typeof(MegaCrit.Sts2.Core.Models.Relics.BoundPhylactery));
                var old = shadow.GetRelicById(boundId);
                if (old != null)
                {
                    shadow.RemoveRelicInternal(old, true);
                    relicsAdded = Math.Max(0, relicsAdded - 1);
                    Logger.Log("[CharBoss] 起始遗物替换：欧洛巴斯之触把「缚魂命匣」升级成「无界命匣」⇒ 摘掉角色自带的缚魂命匣");
                }
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 起始遗物替换（缚魂命匣 → 无界命匣）失败: " + e.Message);
        }
        // 涅奥的祝福：角色 Boss 随身携带（显示在状态栏里，也解释了它们的高血量）
        if (def.CarriesNeowBlessing)
        {
            try
            {
                var blessing = ModelDb.GetById<RelicModel>(ModelDb.GetId(typeof(CharacterBosses.Relics.NeowBlessing)));
                if (blessing != null && shadow.GetRelicById(blessing.Id) == null)
                {
                    shadow.AddRelicInternal(blessing.ToMutable(), relicIndex++, true);
                    relicsAdded++;
                }
            }
            catch (Exception e)
            {
                Logger.Log("[CharBoss] Attach: 涅奥的祝福挂载失败: " + e.Message);
            }
        }

        shadow.ActivateHooks();

        // 「无礼之茶」（`TeaOfDiscourtesy`）：用户 2026-11-03 要在三层「事件机」身上挂一个
        // **已经失效的**（放在旺购客户感恩徽章的右侧）。反编译：它的状态就是 `CombatsLeft`，
        // `IsUsedUp => CombatsLeft <= 0` ⇒ 置 0 之后图标变灰、`BeforeCombatStart` 也不再塞晕眩。
        // 这个属性是 public setter，直接写就行（不需要走钩子）。
        try
        {
            var equipped = shadow.Relics;
            for (int i = 0; i < (equipped?.Count ?? 0); i++)
            {
                if (equipped![i] is not MegaCrit.Sts2.Core.Models.Relics.TeaOfDiscourtesy tea) continue;
                // `CombatsLeft` 的 setter 不是 public ⇒ 走反射（找不到就只打日志，不影响别的）
                var prop = typeof(MegaCrit.Sts2.Core.Models.Relics.TeaOfDiscourtesy).GetProperty(
                    "CombatsLeft",
                    System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.NonPublic);
                prop?.SetValue(tea, 0);
                Logger.Log($"[CharBoss] 无礼之茶：设为「已经失效」（CombatsLeft → " +
                           $"{prop?.GetValue(tea)?.ToString() ?? "?"}，不再塞晕眩）");
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 无礼之茶置为失效失败: " + e.Message);
        }

        Logger.Log($"[CharBoss] shadow player ready: {def.Key} act={act + 1} " +
                   $"deck={added} drawPile={pcs.DrawPile.Cards.Count} " +
                   $"relics={shadow.Relics.Count}(+{relicsAdded}) hp={boss.Creature.CurrentHp} " +
                   $"energy={def.EnergyForAct(act)} handSize={def.HandSizeForAct(act)}");
        return shadow;
    }

    /// <summary>
    /// 影子 Player 的 NetId。
    ///
    /// ⚠ **必须是确定性值**：以前用 `boss.GetHashCode()` 的低 16 位 —— 那是**进程内的对象身份哈希**，
    ///   房主和客人算出来必然不同。NetId 属于联机同步的战斗状态，两边不一致就会在
    ///   **进 Boss 房那一刻**报「多人数据不同步」并掉线（用户实测）。
    ///   现在改成按怪物的 `Id.Entry` 做 FNV-1a（同一只 Boss 在任何客户端都得到同一个值）。
    /// </summary>
    private static ulong ShadowNetId(CharBossMonster boss)
    {
        uint h = 2166136261u;
        try
        {
            string key = boss.Id.Entry ?? string.Empty;
            foreach (char c in key)
            {
                h ^= c;
                h *= 16777619u;
            }
        }
        catch { /* 忽略：用初始值 */ }
        return 0xFFFF_FFFF_0000_0000UL | (h & 0xFFFFu);
    }

    /// <summary>当前局的进阶等级（拿不到按 0 算）。</summary>
    private static int SafeAscension(CharBossMonster boss, MegaCrit.Sts2.Core.Runs.IRunState? runState)
    {
        try { if (runState != null) return runState.AscensionLevel; }
        catch { /* 忽略 */ }
        try
        {
            var run = boss.CombatState?.RunState;
            if (run != null) return run.AscensionLevel;
        }
        catch { /* 忽略 */ }
        return 0;
    }

    /// <summary>这张牌在当前进阶下是否为升级版（定义里的 <c>AscensionUpgrades</c> / 按幕的那张表）。</summary>
    private static bool ShouldUpgradeForAscension(BossDefinition def, Type cardType, int ascension, int act1)
    {
        foreach (var (minAsc, type) in def.AscensionUpgrades)
        {
            if (type == cardType && ascension >= minAsc) return true;
        }
        // 变体自己的升级表：接管时只看它，不看主定义那一幕的行
        var variantUpgrades = def.VariantAscensionUpgrades(act1 - 1);
        if (variantUpgrades != null)
        {
            foreach (var (minAsc, type) in variantUpgrades)
            {
                if (type == cardType && ascension >= minAsc) return true;
            }
            return false;
        }
        foreach (var (minAsc, byAct, type) in def.AscensionUpgradesByAct)
        {
            if (type != cardType || ascension < minAsc) continue;
            if (byAct == 0 || byAct == act1) return true;
        }
        return false;
    }

    /// <summary>影子 Player 的判定：既是 Player 又是 Monster 的 Creature。</summary>
    public static bool IsShadowBound(Creature? creature)
        => creature != null && creature.Player != null && creature.Monster != null;
}
