using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using CharacterBosses.Monsters;
using MegaCrit.Sts2.Core.Models.Orbs;
using CharacterBosses.Patches;

namespace CharacterBosses.Core;

/// <summary>
/// 「在你的回合开始时」类遗物的手动触发 —— <see cref="BossHookBridge"/> 的分部文件。
/// 影子 Player 不在 CombatState.Players 里，原版永远不会替它广播 Before/AfterSideTurnStart，
/// 所以每个 Boss 回合开始时逐件遗物直接调一次（缚魂命匣召唤奥斯提、闪亮口红第 3 回合给力量…）。
/// </summary>
public static partial class BossHookBridge
{
    /// <summary>
    /// 「回合开始」类遗物的处理（**不再手动广播 BeforeSideTurnStart**）。
    ///
    /// 曾经的实现是逐件遗物手动调 <c>BeforeSideTurnStart(side: Player, …)</c>「补触发」。
    /// 但实测证明**原版自己就会广播**：<c>CombatState.IterateHookListeners()</c> 会把
    /// 「带 Player 的生物」的遗物全算成监听者，而我们把影子 Player 挂在 Boss 生物上 ——
    /// 于是她回合开始时原版会广播一次、我们再手动触发一次 ⇒ 双重触发：
    ///   · 破损核心（`TurnNumber &lt;= 1` → 引导 1 个电球）→ **第一回合两个电球**（用户实测）；
    ///   · 红面具（同上条件 → 给「可命中敌人」上虚弱）→ 玩家被上两层虚弱。
    ///
    /// 现在：只做两件必须由 mod 负责的事 ——
    ///   ① 破损核心兜底（原版那次万一没出球就补 1 个，且只在第 1 回合、队列为空时补）；
    ///   ② 红面具的虚弱已经在 <see cref="FireCombatStart"/> 里按玩家侧语义给过，
    ///      她回合开始那次由 <see cref="Patches.ShadowRedMaskTurnStartPatch"/> 拦掉。
    /// </summary>
    private static void FireSideTurnStart(CharBossMonster boss, PlayerChoiceContext context)
    {
        if (boss.Creature == null) return;
        // 「开心小花」按**她自己的回合号**结算（3n 回合 +1 能量），原版那条对她已拦掉
        // （见 Patches/HappyFlowerNoDoublePatch 与本文件的 HappyFlowerPlays）。
        try { HappyFlowerPlays.OnBossTurnStart(boss, context); } catch { /* 忽略 */ }
        // ⚠ 这里**不再**补破损核心的球：改成战斗开始时引导（见 ChannelCrackedCoreOrbAtCombatStart），
        // 否则他第 1 回合会和原版遗物那条叠成两个（用户实测）。

        // 诊断：她这一回合开始时到底有多少力量（烘焙手套每回合 +1 —— 意图预测依赖它真的到账）
        try
        {
            int strength = boss.Creature.GetPowerAmount<MegaCrit.Sts2.Core.Models.Powers.StrengthPower>();
            Logger.Log($"[CharBoss] 她回合开始时 力量={strength}（规划期预测的基础上限见「成长修正」行）");
        }
        catch { /* 忽略 */ }
    }

    /// <summary>
    /// 本场战斗是否已经替影子玩家铸过「击剑指南」的那一次。
    /// 给 <see cref="Patches.FencingManualForgeOncePatch"/> 用：原版遗物的
    /// <c>AfterSideTurnStart</c>（<c>TurnNumber &lt;= 1</c> 就铸）在他第 1 回合会再铸一次，
    /// 用户实测「战斗开始铸一次、第一回合又铸一次」——已经铸过就让那条钩子跳过。
    /// </summary>
    internal static bool FencingManualForgedThisFight;

    /// <summary>
    /// 击剑指南（<c>FencingManual</c>）：「在每场战斗开始时，铸造 10」。
    /// 原版遗物钩子（<c>BeforeCombatStart</c>）对影子玩家不会触发，所以这里自己铸造。
    ///
    /// ⚠ 为什么**不调</b> <c>ForgeCmd.Forge</c>（实测两次都没修好的坑）：
    /// 反编译 <c>ForgeCmd.Forge</c>：
    /// <code>
    /// blades = GetSovereignBlades(player, includeExhausted:false);   // 走 PlayerCombatState.AllCards
    /// if (blades.Count == 0) {
    ///     blade = CreateCard&lt;SovereignBlade&gt;(player);  blade.CreatedThroughForge = true;
    ///     await CardPileCmd.AddGeneratedCardToCombat(blade, PileType.Hand, player);   // ← await
    /// }
    /// IncreaseSovereignBladeDamage(amount, player);   // ← 伤害在 await 之后才加
    /// await Hook.AfterForge(...);
    /// </code>
    /// 它的适配/时序都依赖「玩家的手牌管线」，我们这条路径（剧本直接 CreateCard 造手牌）插不进去：
    /// 实测日志是「先放剑（伤害 10）→ 铸造 10 完成（伤害 10）」，伤害根本没变。
    /// 于是这里只做**同步等价物**：<c>CreateCard</c> + <c>CreatedThroughForge</c> + <c>AddDamage</c>
    /// （<c>AddDamage</c> 是 public，原版铸造就是 1:1 加伤，基础 10 + 铸造 10 = 20）。
    /// 同步 ⇒ 紧接着造出来的剧本手牌拿到的就是 20。
    /// </summary>
    private static void EnsureFencingManualForge(MegaCrit.Sts2.Core.Entities.Players.Player shadow)
    {
        var pcs = shadow.PlayerCombatState;
        if (pcs == null) return;

        RelicModel? manual = null;
        var relics = shadow.Relics;
        for (int i = 0; i < (relics?.Count ?? 0); i++)
        {
            if (SafeEntry(relics![i]) == "FENCING_MANUAL") { manual = relics[i]; break; }
        }
        if (manual == null) return;

        const int forge = 10;   // FencingManual 的 ForgeVar(10)

        // ① 场上没剑 → 先造一把（剑只由铸造产生，所以剧本要用它时优先取这把）
        if (FindSovereignBlade(pcs) == null)
        {
            try
            {
                var blade = shadow.Creature?.CombatState?
                    .CreateCard<MegaCrit.Sts2.Core.Models.Cards.SovereignBlade>(shadow);
                if (blade != null)
                {
                    blade.CreatedThroughForge = true;
                    pcs.DrawPile.AddInternal(blade, -1, true);
                    Logger.Log($"[CharBoss] 击剑指南：战斗开始造出君王之剑（基础伤害 {BladeDamage(blade)}）");
                }
            }
            catch (Exception e)
            {
                Logger.Log("[CharBoss] 预造君王之剑失败: " + e.Message);
            }
        }

        // ② 同步铸造：给所有剑加伤（原版 IncreaseSovereignBladeDamage 的做法）
        int forged = 0;
        try
        {
            foreach (var pile in pcs.AllPiles)
            {
                if (pile == null) continue;
                foreach (var card in pile.Cards)
                {
                    if (card is not MegaCrit.Sts2.Core.Models.Cards.SovereignBlade blade) continue;
                    blade.AddDamage(forge);
                    forged++;
                }
            }
            Logger.Log($"[CharBoss] 击剑指南：战斗开始铸造 {forge}（同步加伤，共 {forged} 把，" +
                       $"剑伤 {BladeDamage(FindSovereignBlade(pcs))}）");
            FencingManualForgedThisFight = true;   // 让原版遗物那条钩子别再铸第二次
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 击剑指南铸造失败: " + e.Message);
        }
    }

    /// <summary>
    /// 「佩尔的士兵」（<c>PaelsLegion</c>）的装饰性随从：原版在 <c>BeforeCombatStart</c> 里
    /// <c>PlayerCmd.AddPet&lt;Monsters.PaelsLegion&gt;(Owner)</c> 召出来。影子玩家走不到那条钩子 ⇒ 这里补召。
    /// 只在「她身上确实有这个遗物」且「场上还没有这只随从」时召；
    /// Boss 死后由 <see cref="BossShadowPets.RemoveOnBossDeath"/> 负责摘掉（否则战斗会卡在「还没打完」）。
    /// </summary>
    private static async Task EnsurePaelsLegionPet(Player shadow)
    {
        var pcs = shadow.PlayerCombatState;
        if (pcs == null) return;

        bool hasRelic = false;
        var relics = shadow.Relics;
        for (int i = 0; i < (relics?.Count ?? 0); i++)
        {
            if (SafeEntry(relics![i]) == "PAELS_LEGION") { hasRelic = true; break; }
        }
        if (!hasRelic) return;

        try
        {
            foreach (var pet in pcs.Pets)
            {
                if (pet?.Monster is MegaCrit.Sts2.Core.Models.Monsters.PaelsLegion) return;   // 已经有了
            }
        }
        catch { /* 读不到就继续尝试召 */ }

        // ★ v0.2.465（2026-11-07 玩家上报：「补召佩尔的士兵失败: Attempted to add a creature outside of combat.」）：
        //   这一串「战斗开始」补效果是**逐个 await** 的（王室猛毒 / 护喉甲 / 钻石头冠 / 弹珠袋 / 贤者之石 /
        //   金刚杵 …），期间只要有一只 Boss 被打死、或战斗提前结束，轮到补随从时战斗已经不在进行中，
        //   原版 `PlayerCmd.AddPet` 就会抛「Attempted to add a creature outside of combat」。
        //   随从是**纯装饰**，战斗都结束了就根本不需要它 ⇒ 先确认「战斗仍在进行」再召。
        bool live = true;
        try { live = MegaCrit.Sts2.Core.Combat.CombatManager.Instance?.IsInProgress == true; } catch { live = true; }
        if (!live)
        {
            Logger.Log("[CharBoss] 佩尔的士兵：战斗已经结束，跳过补召（否则原版会抛 outside of combat）");
            return;
        }

        var creature = await MegaCrit.Sts2.Core.Commands.PlayerCmd
            .AddPet<MegaCrit.Sts2.Core.Models.Monsters.PaelsLegion>(shadow);
        Logger.Log("[CharBoss] 佩尔的士兵：战斗开始补召随从（原版遗物钩子走不到影子玩家）" +
                   (creature == null ? "（AddPet 返回空）" : $"（combatId={SafeCombatId(creature)}）"));
    }

    private static string SafeCombatId(Creature? creature)
    {
        try { return creature?.CombatId.ToString() ?? "?"; }
        catch { return "?"; }
    }

    /// <summary>影子玩家所有牌堆里第一把君王之剑（没有则 null）。</summary>
    private static MegaCrit.Sts2.Core.Models.Cards.SovereignBlade? FindSovereignBlade(PlayerCombatState pcs)
    {
        try
        {
            foreach (var pile in pcs.AllPiles)
            {
                if (pile == null) continue;
                foreach (var card in pile.Cards)
                {
                    if (card is MegaCrit.Sts2.Core.Models.Cards.SovereignBlade blade) return blade;
                }
            }
        }
        catch { /* 忽略 */ }
        return null;
    }

    private static string BladeDamage(MegaCrit.Sts2.Core.Models.Cards.SovereignBlade? blade)
    {
        if (blade == null) return "?";
        try { return blade.DynamicVars.Damage.IntValue.ToString(); }
        catch { return "?"; }
    }

    /// <summary>
    /// 破损核心：**在战斗开始时**引导 1 个闪电球（原版遗物钩子走不到影子玩家身上）。
    ///
    /// 原版 <c>CrackedCore.BeforeSideTurnStart</c> 的条件是
    /// <c>participants.Contains(Owner.Creature) &amp;&amp; Owner.PlayerCombatState.TurnNumber &lt;= 1</c>，
    /// 它挂在「某一方回合开始」的广播上 —— 影子玩家要到他**自己的第 1 回合**才收到，
    /// 于是球出现在他回合开始而不是战斗开始（用户实测）。
    /// 再加上原来那个「裂核兜底」也补一个 ⇒ 他回合开始一次冒出**两个**。
    ///
    /// 现在：这里在战斗开始引导一次；原版那条由 <see cref="Patches.CrackedCoreOncePatch"/> 对他跳过；
    /// 兜底那个方法已经删掉（不再需要）。
    /// </summary>
    private static async Task ChannelCrackedCoreOrbAtCombatStart(CharBossMonster boss, Player shadow,
                                                                  PlayerChoiceContext ctx)
    {
        try
        {
            if (!HasCrackedCore(shadow)) return;
            var orbs = shadow.PlayerCombatState?.OrbQueue;
            if (orbs == null || orbs.Capacity <= 0 || orbs.Orbs.Count > 0) return;

            // ⚠ 这里是**手动入队**，不是 `OrbCmd.Channel`。
            // 实测（日志）：战斗开始那一刻调 `OrbCmd.Channel<LightningOrb>` 会静默无效
            // —— 调用完日志里仍是 `球数 0/3`，于是「战斗开始引导」变成「一个球都没有」。
            // 原版 Channel 内部是「先 orb.Owner = player 再 TryEnqueue」，而 TryEnqueue 不设 Owner；
            // 少了 Owner 还会让 CombatState.Contains 里 `orbModel.Owner.IsActiveForHooks` 空引用。
            var canonical = ModelDb.GetById<OrbModel>(ModelDb.GetId(typeof(LightningOrb)));
            if (canonical == null) return;

            var orb = canonical.ToMutable(1);
            orb.Owner = shadow;

            bool ok = await orbs.TryEnqueue(orb);
            if (ok) CharacterBosses.UI.BossOrbDisplay.AfterChannel(boss);
            Logger.Log($"[CharBoss] 破损核心：战斗开始引导闪电球 入队={ok}（球数 " +
                       $"{shadow.PlayerCombatState?.OrbQueue.Orbs.Count}/{shadow.PlayerCombatState?.OrbQueue.Capacity}）");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 破损核心引导失败: " + e.Message);
        }
    }

    /// <summary>
    /// 共生病毒（<c>SymbioticVirus</c>）：卡面是「每场战斗开始时，生成 1 个黑暗充能球」。
    /// 和破损核心一样，原版遗物钩子只派发给 `CombatState.Players` 里的真人玩家
    /// ⇒ 影子玩家收不到，必须在这里替她手动入队（做法与破损核心同一个坑：**手动**入队，别用 `OrbCmd.Channel`）。
    /// 一层「黑球机」的整个玩法就靠这颗暗黑球（空值/双重释放/冷却剂都围着它转）。
    /// </summary>
    private static async Task ChannelSymbioticVirusOrbAtCombatStart(CharBossMonster boss, Player shadow,
                                                                    PlayerChoiceContext ctx)
    {
        try
        {
            var relics = shadow.Relics;
            if (relics == null) return;
            bool has = false;
            for (int i = 0; i < relics.Count; i++)
            {
                if (relics[i]?.Id.Entry == "SYMBIOTIC_VIRUS") { has = true; break; }
            }
            if (!has) return;

            var orbs = shadow.PlayerCombatState?.OrbQueue;
            if (orbs == null || orbs.Capacity <= 0) return;

            // 已经有黑暗球了就不再给（避免和别的来源重复）
            foreach (var existing in orbs.Orbs)
            {
                if (existing is MegaCrit.Sts2.Core.Models.Orbs.DarkOrb) return;
            }

            var canonical = ModelDb.GetById<OrbModel>(ModelDb.GetId(typeof(MegaCrit.Sts2.Core.Models.Orbs.DarkOrb)));
            if (canonical == null) return;

            var orb = canonical.ToMutable(1);
            orb.Owner = shadow;

            bool ok = await orbs.TryEnqueue(orb);
            if (ok) CharacterBosses.UI.BossOrbDisplay.AfterChannel(boss);
            Logger.Log($"[CharBoss] 共生病毒：战斗开始引导黑暗球 入队={ok}（球数 " +
                       $"{shadow.PlayerCombatState?.OrbQueue.Orbs.Count}/{shadow.PlayerCombatState?.OrbQueue.Capacity}）");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 共生病毒引导失败: " + e.Message);
        }
    }

    /// <summary>
    /// 冷却剂（一层「黑球机」的特殊效果）：战斗开始时，给**被她锁定的那位玩家** **2 张「冷光」**
    /// —— **各 1 张**分别进**手牌**和**抽牌堆**。
    ///
    /// ⚠ 用户 2026-11-05（「削弱/回调黑球机」）：从 v0.2.3xx 的「1 张、只进手牌」**回调**成
    /// 「2 张、手牌 + 抽牌堆各 1」—— 冷光（`LUMINESCE`）是 0 费 Token 技能「获得能量」，
    /// 手里那张是「这回合要不要拿它换能量（换掉就没了黑暗球保护）」，
    /// 抽牌堆那张是下回合的备用（见 <c>Powers/BossCoolantPower</c>）。
    /// 只对装了这条特殊效果的 Boss 生效（按 `SpecialPowerForAct` 判断，其它 Boss 零影响）。
    /// </summary>
    private static async Task GrantCoolantLuminesceAtCombatStart(CharBossMonster boss, PlayerChoiceContext ctx)
    {
        try
        {
            if (boss?.Creature == null) return;
            if (boss.Def.SpecialPowerForAct(boss.ActIndex) != typeof(Powers.BossCoolantPower)) return;

            var combat = boss.CombatState;
            if (combat == null) return;

            var canonical = ModelDb.GetById<CardModel>(ModelDb.GetId(typeof(MegaCrit.Sts2.Core.Models.Cards.Luminesce)));
            if (canonical == null)
            {
                Logger.Log("[CharBoss] 冷却剂：取不到「冷光」的模型 ⇒ 跳过");
                return;
            }

            int players = 0, given = 0;
            foreach (var player in Core.BossLockOn.RealPlayers(combat))
            {
                if (player?.Creature == null || !player.Creature.IsAlive) continue;
                if (player.Creature.Monster != null) continue;
                // 冷却剂是**单体**特殊效果：只给**被她锁定的那位玩家**（用户 2026-10-27 订正）。
                if (!Core.BossLockOn.IsLocked(boss, player)) continue;
                players++;

                // 用户 2026-11-05：**2 张冷光，手牌 + 抽牌堆各 1 张**（回调成最初的写法）
                foreach (var pile in new[]
                         {
                             MegaCrit.Sts2.Core.Entities.Cards.PileType.Hand,
                             MegaCrit.Sts2.Core.Entities.Cards.PileType.Draw,
                         })
                {
                    try
                    {
                        var card = combat.CreateCard(canonical, player);
                        if (card == null) continue;
                        await MegaCrit.Sts2.Core.Commands.CardPileCmd.AddGeneratedCardToCombat(card, pile, player);
                        given++;
                        Logger.Log($"[CharBoss] 冷却剂：给 {player.NetId} 塞 1 张「冷光」进{pile} 堆");
                    }
                    catch (Exception e)
                    {
                        Logger.Log($"[CharBoss] 冷却剂：给 {player.NetId} 塞冷光进{pile} 堆失败: {e.Message}");
                    }
                }
            }

            Logger.Log($"[CharBoss] 冷却剂：战斗开始给 {players} 名玩家各 2 张「冷光」（手牌 1 + 抽牌堆 1，共 {given} 张）" +
                       "—— 回合结束时**留在手里**才会让她的暗黑充能球不打被动");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 冷却剂发牌失败: " + e.Message);
        }
    }

    /// <summary>
    /// 「玉之剑」（<c>SwordOfJade</c>）：卡面「每场战斗开始时获得 1 点力量」。
    /// 原版挂在 `AfterRoomEntered`（建影子 Player 之前）⇒ 对她不生效，这里照卡面补。
    /// 用户 2026-11-03 把它加进了三层「事件机」的遗物栏。
    /// </summary>
    private static async Task GrantSwordOfJadeStrengthAtCombatStart(CharBossMonster boss, Player shadow,
                                                                   PlayerChoiceContext ctx)
    {
        try
        {
            var relics = shadow.Relics;
            if (relics == null) return;
            for (int i = 0; i < relics.Count; i++)
            {
                var relic = relics[i];
                if (relic == null) continue;
                string entry;
                try { entry = relic.Id.Entry; } catch { continue; }
                if (entry != "SWORD_OF_JADE") continue;

                int amount = 1;
                try
                {
                    if (relic.DynamicVars.TryGetValue("StrengthPower", out var v) && v != null)
                        amount = Math.Max(0, (int)Math.Round((double)v.BaseValue));
                }
                catch { amount = 1; }
                if (amount <= 0) return;

                var creature = boss.Creature;
                if (creature == null) return;
                await PowerCmd.Apply<MegaCrit.Sts2.Core.Models.Powers.StrengthPower>(
                    ctx, creature, amount, creature, null);
                Logger.Log($"[CharBoss] 玉之剑：战斗开始 +{amount} 力量（原版钩子对影子玩家不生效，这里补）");
                return;
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 玉之剑补力量失败: " + e.Message);
        }
    }

    /// <summary>
    /// 「王室猛毒」（<c>RoyalPoison</c>）：卡面「每场战斗开始时失去 4 点生命」。
    /// 反编译：原版在 `AfterPlayerTurnStart` 里、`TurnNumber &lt;= 1` 时对自己造成
    /// 4 点「无格挡 + 无来源」伤害 —— 影子玩家收不到那条钩子，所以一次都没掉过。
    /// 用户 2026-11-03：「把掉血时间改在**战斗开始**，她第一回合开始不掉」⇒ 战斗开始扣一次。
    /// </summary>
    private static async Task ApplyRoyalPoisonAtCombatStart(CharBossMonster boss, Player shadow,
                                                            PlayerChoiceContext ctx)
    {
        try
        {
            var relics = shadow.Relics;
            var creature = boss.Creature;
            if (relics == null || creature == null) return;

            for (int i = 0; i < relics.Count; i++)
            {
                var relic = relics[i];
                if (relic == null) continue;
                string entry;
                try { entry = relic.Id.Entry; } catch { continue; }
                if (entry != "ROYAL_POISON") continue;

                int amount = 4;
                try
                {
                    if (relic.DynamicVars.TryGetValue("Damage", out var v) && v != null)
                        amount = Math.Max(0, (int)Math.Round((double)v.BaseValue));
                }
                catch { amount = 4; }
                if (amount <= 0) return;

                int before = creature.CurrentHp;
                await CreatureCmd.Damage(ctx, creature, amount,
                    MegaCrit.Sts2.Core.ValueProps.ValueProp.Unblockable
                    | MegaCrit.Sts2.Core.ValueProps.ValueProp.Unpowered,
                    (Creature?)null);
                Logger.Log($"[CharBoss] 王室猛毒：战斗开始掉 {amount} 血（{before} → {creature.CurrentHp}）" +
                           "（原版在她第 1 回合开始时扣，影子玩家收不到钩子 ⇒ 改到战斗开始）");
                return;
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 王室猛毒掉血失败: " + e.Message);
        }
    }
    /// <summary>
    /// 「数据磁盘」（<c>DataDisk</c>）：卡面写「每场战斗开始时获得 1 点集中」，
    /// 但反编译出来它挂的是 <c>AfterRoomEntered</c> —— 那件事发生在**建影子 Player 之前**
    /// （拉 Boss 房间那一刻），所以对 Boss 一次都没生效。这里在战斗开始时按卡面补一次。
    /// 只有她身上真有这件遗物才补（进阶 8 的二层物理机 / 进阶 9 的三层能力机）。
    /// </summary>
    private static async Task GrantDataDiskFocusAtCombatStart(CharBossMonster boss, Player shadow,
                                                              PlayerChoiceContext ctx)
    {
        try
        {
            var relics = shadow.Relics;
            if (relics == null) return;

            int amount = 0;
            bool foundOnRelics = false;
            for (int i = 0; i < relics.Count; i++)
            {
                var relic = relics[i];
                if (relic == null) continue;
                string entry;
                try { entry = relic.Id.Entry; } catch { continue; }
                if (entry != "DATA_DISK") continue;
                foundOnRelics = true;
                try { amount = (int)Math.Round((double)relic.DynamicVars["FocusPower"].BaseValue); }
                catch { amount = 1; }
                break;
            }

            // ⚠ 兜底：**按定义**再确认一次。实测发现「能力机（三层）进阶 10」明明日志里写了
            // 「额外获得遗物 DataDisk」，可 `shadow.Relics` 里却扫不到（用户 2026-10-25 问
            // 「能力机进阶9的数据磁盘生效了吗」——它确实没生效）。所以这里再查
            // `BossDefinition.AscensionRelicsByAct`：这一档该给 DataDisk 就一定补上。
            if (amount <= 0)
            {
                if (!BossShouldCarryDataDisk(boss)) return;
                amount = 1;
                Logger.Log("[CharBoss] 数据磁盘：遗物列表里没扫到，但按进阶奖励定义她该有 ⇒ 照样补 1 点集中");
            }
            if (amount <= 0) return;

            var creature = boss.Creature;
            if (creature == null) return;
            await PowerCmd.Apply<MegaCrit.Sts2.Core.Models.Powers.FocusPower>(ctx, creature, amount, creature, null);
            int now = -1;
            try { now = creature.GetPowerAmount<MegaCrit.Sts2.Core.Models.Powers.FocusPower>(); } catch { /* 忽略 */ }
            Logger.Log($"[CharBoss] 数据磁盘：战斗开始 +{amount} 集中" +
                       $"（{(foundOnRelics ? "从遗物列表" : "按进阶定义兜底")}；原版钩子对影子玩家不生效，这里补；现在 {now}）");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 数据磁盘补集中失败: " + e.Message);
        }
    }

    /// <summary>
    /// 按 **BossDefinition 的进阶奖励定义**判断：这一幕、这个进阶等级下她「应该有」数据磁盘吗。
    /// 用于遗物列表扫不到时兜底（三层能力机实测踩过）。
    /// </summary>
    private static bool BossShouldCarryDataDisk(CharBossMonster? boss)
    {
        try
        {
            var def = boss?.Def;
            if (def == null) return false;

            int asc = 0;
            try { asc = boss!.Shadow?.RunState?.AscensionLevel ?? 0; } catch { asc = 0; }
            int act = Math.Clamp(boss!.ActIndex, 0, 2) + 1;      // 幕号从 1 起

            // ★ 2026-11-06（用户：「削弱事件机，进阶 9 不再获得数据磁盘」）：
            //   这一幕由**变体**接管时，只看变体自己的进阶遗物表；跳过主定义那一行，
            //   否则事件机会按主定义白拿 1 点集中（她现在已经没有这件遗物了）。
            bool variantTakesOver = false;
            try { variantTakesOver = def.VariantTakesOver(act - 1); } catch { variantTakesOver = false; }
            if (!variantTakesOver)
            {
                foreach (var (minAsc, byAct, relicType, _) in def.AscensionRelicsByAct)
                {
                    if (relicType != typeof(MegaCrit.Sts2.Core.Models.Relics.DataDisk)) continue;
                    if (asc < minAsc) continue;
                    if (byAct != 0 && byAct != act) continue;
                    return true;
                }
            }
            foreach (var (minAsc, relicType) in def.AscensionRelics)
            {
                if (relicType != typeof(MegaCrit.Sts2.Core.Models.Relics.DataDisk)) continue;
                if (asc >= minAsc) return true;
            }
        }
        catch { /* 忽略 */ }
        return false;
    }

    /// <summary>
    /// 「护喉甲」（<c>Gorget</c>）：卡面是「每场战斗开始时获得 4 层覆甲」，
    /// 但反编译出来和「数据磁盘」一样挂在 <c>AfterRoomEntered</c> 上 —— 那发生在**建影子 Player 之前**，
    /// 所以对 Boss 永远不生效。这里在战斗开始时按卡面补一次（读遗物的 `PlatingPower` 变量）。
    ///
    /// ## 多人实测两个坑（用户 2026-10-25）
    /// ① **层数被人数放大**：`PowerCmd.Apply` 对敌人身上的 `ShouldScaleInMultiplayer` Power 会套
    ///    `PlatingPower.GetScaledAmountForMultiplayer = ((人数-1)*2+1) * 层数`（2 人 = ×3）
    ///    ⇒ 我们给的 4 层变成 **12 层**（队友截图就是这个数）。已由
    ///    <c>Patches/BossNoMultiplayerPowerScalePatch</c> 还原成 4 层。
    /// ② **那笔格挡两端不同步**：原版 `PlatingPower.BeforeSideTurnStart` 只在
    ///    「第 1 回合**玩家**回合开始、且敌人身上有覆甲」时给等量格挡；而本方法是
    ///    **异步 fire-and-forget**（`TaskHelper.RunSafely`），可能晚于那个时点
    ///    ⇒ 一端给了（10 + 12 = 22）、一端没给（只有锚的 10）。
    ///    现在改成**确定性的**：原版那条钩子对我们是压掉的（`BossRelicSuppressPlatingTurnStartPatch`），
    ///    由这里在挂上覆甲之后**自己补一次等量格挡**，两端在同一个代码点各自结算，数值必然一致。
    /// </summary>
    /// <summary>
    /// 钻石头冠（<c>DiamondDiadem</c>）：**战斗开始**就获得 20 点格挡，并给 2 层残影让这笔格挡撑过玩家回合。
    ///
    /// 反编译：原版写在 `AfterSideTurnStart` 里，条件是
    /// `participants.Contains(Owner.Creature) &amp;&amp; Owner.PlayerCombatState.TurnNumber &lt;= 1`
    /// ⇒ 实际是「她第 1 回合开始时」，而不是卡面写的「战斗开始时」。
    /// 用户 2026-10-27：
    ///   · 卡面是「在战斗开始时……」，所以**一进战斗就要拿到**（和锚/护喉甲同一时机）；
    ///   · 残影给 **2 层**：`BlurPower.AfterSideTurnStart` 会在**她回合开始**掉 1 层
    ///     （反编译确认：`participants.Contains(Owner)` → `PowerCmd.Decrement`），
    ///     只给 1 层的话到玩家回合就没了、那 20 点格挡也跟着失效。
    ///
    /// 原版那次（她回合开始）由 `Patches/ShadowDiamondDiademOncePatch` 拦掉，不会重复。
    /// </summary>
    private static async Task GrantDiamondDiademAtCombatStart(CharBossMonster boss, Player shadow,
                                                               PlayerChoiceContext ctx)
    {
        try
        {
            var creature = boss?.Creature;
            if (creature == null) return;

            decimal block = DiademBlockOf(shadow);
            if (block <= 0m) return;      // 她身上没有这件遗物

            const decimal BlurStacks = 2m;   // 1 层会在她回合开始被吃掉

            // ⚠ v0.2.241：**格挡不在这里给**（用户 2026-10-30 联机不同步的根因）。
            //   「战斗开始清零」与这段代码的相对时序两端并不一致 ⇒ 有的客户端这 20 点活下来、
            //   有的被清掉 ⇒ 同一场战斗两端格挡不同。现在格挡统一由
            //   `Patches.ShadowDiamondDiademOncePatch.DiademBlockSyncPatch` 在
            //   「玩家回合开始」这条同步钩子里同步补上（见 GrantDiademBlockSync）。
            await PowerCmd.Apply<MegaCrit.Sts2.Core.Models.Powers.BlurPower>(ctx, creature, BlurStacks, creature, null);

            Logger.Log($"[CharBoss] 钻石头冠：战斗开始 +{BlurStacks:0} 层残影" +
                       $"（{block:0} 点格挡在**玩家回合开始**同步补，见 DiademBlockSyncPatch；" +
                       "原版是「她第 1 回合开始」才给、且只给 1 层残影）");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 钻石头冠补格挡失败: " + e.Message);
        }
    }

    /// <summary>她身上「钻石头冠」这件遗物给的格挡值（没有就 0）。</summary>
    internal static decimal DiademBlockOf(Player? shadow)
    {
        try
        {
            var relics = shadow?.Relics;
            for (int i = 0; i < (relics?.Count ?? 0); i++)
            {
                var relic = relics![i];
                if (relic == null) continue;

                string entry;
                try { entry = relic.Id.Entry; } catch { continue; }
                if (entry != "DIAMOND_DIADEM") continue;

                try { return relic.DynamicVars["Block"].BaseValue; }
                catch { return 20m; }
            }
        }
        catch { /* 忽略 */ }
        return 0m;
    }

    /// <summary>
    /// **钻石头冠的格挡：唯一发放点**（v0.2.241；由 <c>DiademBlockSyncPatch</c> 在
    /// `Hook.AfterSideTurnStart(Player)` 的 postfix 里调用）。
    ///
    /// 为什么从「帧驱动 + `Task.Delay` 异步补」改成这里：
    ///   用户 2026-10-30 的联机不同步，RitsuLib 诊断包逐项比对只有一处不一样 ——
    ///   `creatures[6].block: local=20; remote=0`（当地那只怪物 = `MONSTER.SILENT_BOSS`）。
    ///   说明「战斗开始那 20 点被清零」这件事**两端的相对时序不同**，靠帧回调补救必然有人补不到。
    ///   战斗状态不允许在帧回调里改：这条钩子是两端都会 await 的同步流程，且写在校验和之前，
    ///   所以两端结果必然一致；对玩家而言仍然「一进战斗就有格挡」。
    /// </summary>
    internal static void GrantDiademBlockSync(MegaCrit.Sts2.Core.Combat.ICombatState? combat)
    {
        if (combat == null) return;
        try
        {
            foreach (var list in new[] { combat.Enemies, combat.Allies })
            {
                if (list == null) continue;
                foreach (var creature in list)
                {
                    if (creature?.Monster is not CharBossMonster boss) continue;
                    if (boss.TurnNumber > 1) continue;                // 只在她第 1 回合内补
                    if (creature.Block > 0) continue;                 // 已经有格挡（战斗开始那笔活下来了）
                    decimal block = DiademBlockOf(boss.Shadow);
                    if (block <= 0m) continue;                        // 没有这件遗物

                    creature.GainBlockInternal(block);                 // 同步写，写完才继续
                    Logger.Log($"[CharBoss] 钻石头冠：玩家回合开始补 {block:0} 点格挡" +
                               $"（战斗开始那笔被清掉了；现在 {creature.Block:0}，她第 {boss.TurnNumber} 回合）");
                }
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 钻石头冠补格挡失败: " + e.Message);
        }
    }
    private static async Task GrantGorgetPlatingAtCombatStart(CharBossMonster boss, Player shadow,
                                                               PlayerChoiceContext ctx)
    {
        try
        {
            var relics = shadow.Relics;
            if (relics == null) return;

            decimal amount = 0m;
            for (int i = 0; i < relics.Count; i++)
            {
                var relic = relics[i];
                if (relic == null) continue;
                string entry;
                try { entry = relic.Id.Entry; } catch { continue; }
                if (entry != "GORGET") continue;
                try { amount = relic.DynamicVars["PlatingPower"].BaseValue; }
                catch { amount = 4m; }
                break;
            }
            if (amount <= 0m) return;

            var creature = boss.Creature;
            if (creature == null) return;

            // 幂等：战斗开始钩子可能被调用不止一次（读档/多人重进都会），已经有覆甲就不再叠。
            decimal already = 0m;
            try { already = creature.GetPowerAmount<MegaCrit.Sts2.Core.Models.Powers.PlatingPower>(); }
            catch { already = 0m; }
            if (already > 0m)
            {
                Logger.Log($"[CharBoss] 护喉甲：她已经有 {already} 层覆甲，跳过（不重复叠）");
                return;
            }

            await PowerCmd.Apply<MegaCrit.Sts2.Core.Models.Powers.PlatingPower>(ctx, creature, amount, creature, null);

            // 实际到账多少（多人缩放已由 BossNoMultiplayerPowerScalePatch 压回原值，这里只做核对）。
            decimal now = amount;
            try { now = creature.GetPowerAmount<MegaCrit.Sts2.Core.Models.Powers.PlatingPower>(); }
            catch { /* 取不到就按请求值 */ }

            // 原版那条「第 1 回合玩家回合开始 → 给等量格挡」对我们是压掉的 ⇒ 这里自己补一次。
            if (now > 0m)
            {
                try
                {
                    await CreatureCmd.GainBlock(creature, now, MegaCrit.Sts2.Core.ValueProps.ValueProp.Unpowered, null);
                }
                catch (Exception e)
                {
                    Logger.Log("[CharBoss] 护喉甲：补起手格挡失败: " + e.Message);
                }
            }

            Logger.Log($"[CharBoss] 护喉甲：战斗开始 +{now} 层覆甲" +
                       $"（请求 {amount}；格挡补到 {creature.Block}，原版钩子对影子玩家不生效，这里补）");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 护喉甲补覆甲失败: " + e.Message);
        }
    }

    /// <summary>
    /// 「弹珠袋」（<c>BagOfMarbles</c>）：卡面是「战斗开始时给予所有敌人 1 层易伤」，
    /// 原版实现在 `BeforeSideTurnStart` 里对 `HittableEnemies` 上易伤 —— 对影子玩家不生效
    /// （用户 2026-10-24：「弹珠袋应当在玩家刚进战斗时就给予所有玩家易伤」）。
    /// 这里在战斗开始时按卡面补一次：对**所有真人玩家**各上 1 层易伤（applier = 她）。
    /// 注意：这也是「她给予易伤」⇒ 三层「易伤战」的「主宰」会顺带再给 1 层虚弱。
    /// </summary>
    /// <summary>
    /// 「红面具」（`RedMask`）：卡面 = 战斗开始时**给予所有敌人 1 层虚弱**。
    /// 原版挂在 `AfterRoomEntered` 上 ⇒ 影子玩家收不到（和弹珠袋/护喉甲同一类问题），这里手动补一次。
    /// 用户 2026-11-08：「蛇咬猎的红面具没有生效」。
    /// </summary>
    private static async Task GrantRedMaskWeakAtCombatStart(CharBossMonster boss, Player shadow, PlayerChoiceContext ctx)
    {
        try
        {
            var relics = shadow.Relics;
            if (relics == null) return;

            decimal amount = 0m;
            for (int i = 0; i < relics.Count; i++)
            {
                var relic = relics[i];
                if (relic == null) continue;
                string entry;
                try { entry = relic.Id.Entry; } catch { continue; }
                if (entry != "RED_MASK") continue;
                try { amount = relic.DynamicVars["Weak"].BaseValue; }
                catch { amount = 1m; }
                break;
            }
            if (amount <= 0m) return;

            var me = boss.Creature;
            var combat = me?.CombatState;
            if (me == null || combat == null) return;

            var targets = new List<Creature>();
            try
            {
                foreach (var creature in combat.PlayerCreatures)
                {
                    if (creature == null || !creature.IsAlive) continue;
                    if (creature.Monster != null || creature.Player == null) continue;   // 只要真人玩家
                    targets.Add(creature);
                }
            }
            catch { /* 取不到就空 */ }
            if (targets.Count == 0) return;

            await PowerCmd.Apply<MegaCrit.Sts2.Core.Models.Powers.WeakPower>(ctx, targets, amount, me, null);
            Logger.Log($"[CharBoss] 红面具：战斗开始给 {targets.Count} 名玩家各 {amount} 层虚弱");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 红面具补虚弱失败: " + e.Message);
        }
    }
    private static async Task GrantBagOfMarblesVulnerableAtCombatStart(CharBossMonster boss, Player shadow,
                                                                       PlayerChoiceContext ctx)
    {
        try
        {
            var relics = shadow.Relics;
            if (relics == null) return;

            decimal amount = 0m;
            RelicModel? marbleRelic = null;
            for (int i = 0; i < relics.Count; i++)
            {
                var relic = relics[i];
                if (relic == null) continue;
                string entry;
                try { entry = relic.Id.Entry; } catch { continue; }
                if (entry != "BAG_OF_MARBLES") continue;
                marbleRelic = relic;
                try { amount = relic.DynamicVars["Vulnerable"].BaseValue; }
                catch { amount = 1m; }
                break;
            }
            if (amount <= 0m) return;

            var me = boss.Creature;
            var combat = me?.CombatState;
            if (me == null || combat == null) return;

            var targets = new List<Creature>();
            try
            {
                foreach (var creature in combat.PlayerCreatures)
                {
                    if (creature == null || !creature.IsAlive) continue;
                    if (creature.Monster != null || creature.Player == null) continue;   // 只要真人玩家
                    targets.Add(creature);
                }
            }
            catch { /* 取不到就空 */ }
            if (targets.Count == 0) return;

            // ⚠ v0.2.522：PowerCmd.Apply 的第 5 个参数是 **CardModel?**（卡牌来源），遗物传不进去，
            //   所以这里仍然传 null（与反编译里原版 BagOfMarbles 自己那一句完全一致）。
            //   另外补一行日志把**实际施加的层数**打出来，便于核对「下一回合有没有掉」。
            await PowerCmd.Apply<MegaCrit.Sts2.Core.Models.Powers.VulnerablePower>(ctx, targets, amount, me, null);
            try
            {
                var t0 = targets[0];
                int now = t0.GetPowerAmount<MegaCrit.Sts2.Core.Models.Powers.VulnerablePower>();
                Logger.Log($"[CharBoss] 弹珠袋：施加后 {t0.Name} 的易伤层数 = {now}（遗物={marbleRelic?.Id.Entry}）");
            }
            catch { /* 忽略 */ }
            Logger.Log($"[CharBoss] 弹珠袋：战斗开始给 {targets.Count} 名玩家各 {amount} 层易伤");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 弹珠袋补易伤失败: " + e.Message);
        }
    }

    /// <summary>
    /// 「贤者之石」（<c>PhilosophersStone</c>）：卡面「每回合 +1 能量；每场战斗开始时**所有敌人** +1 力量」。
    /// 反编译确认实现挂在 `AfterRoomEntered(room)`：`if (room is CombatRoom)` 时对
    /// `GetOpponentsOf(Owner.Creature)` 上力量 —— 对 Boss 而言对手就是**所有真人玩家**。
    /// 影子玩家的遗物收不到原版派发 ⇒ 这里在战斗开始手动补一次（applier = 她）。
    /// </summary>
    private static async Task GrantPhilosophersStoneStrengthAtCombatStart(CharBossMonster boss, Player shadow,
                                                                          PlayerChoiceContext ctx)
    {
        try
        {
            var relics = shadow.Relics;
            if (relics == null) return;

            decimal amount = 0m;
            for (int i = 0; i < relics.Count; i++)
            {
                var relic = relics[i];
                if (relic == null) continue;
                string entry;
                try { entry = relic.Id.Entry; } catch { continue; }
                if (entry != "PHILOSOPHERS_STONE") continue;
                try { amount = relic.DynamicVars["Strength"].BaseValue; }
                catch { amount = 1m; }
                break;
            }
            if (amount <= 0m) return;

            var me = boss.Creature;
            var combat = me?.CombatState;
            if (me == null || combat == null) return;

            var targets = new List<Creature>();
            try
            {
                foreach (var creature in combat.PlayerCreatures)
                {
                    if (creature == null || !creature.IsAlive) continue;
                    if (creature.Monster != null || creature.Player == null) continue;   // 只要真人玩家
                    targets.Add(creature);
                }
            }
            catch { /* 取不到就空 */ }
            if (targets.Count == 0) return;

            await PowerCmd.Apply<MegaCrit.Sts2.Core.Models.Powers.StrengthPower>(ctx, targets, amount, me, null);
            Logger.Log($"[CharBoss] 贤者之石：战斗开始给 {targets.Count} 名玩家各 +{amount} 力量");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 贤者之石补力量失败: " + e.Message);
        }
    }

    /// <summary>
    /// 「吊灯」（<c>Chandelier</c>）在她**第 3 回合开始时**给的额外能量（卡面 `EnergyVar(3)`）。
    ///
    /// 反编译：条件是 `participants.Contains(Owner.Creature) &amp;&amp; Owner.PlayerCombatState.TurnNumber == 3`
    /// ⇒ 对影子玩家不生效；而且我们自己在 <c>PrepareTurn</c> 里会 `pcs.Energy = …` 硬覆盖，
    /// 就算到了账也会被抹掉。所以由这里按卡面直接算进本回合预算。
    /// 原版那条已被 <see cref="Patches.BossRelicSuppressChandelierPatch"/> 压掉，不会双份。
    /// </summary>
    internal static int ChandelierEnergyBonusAtTurnStart(CharBossMonster boss, MegaCrit.Sts2.Core.Entities.Players.PlayerCombatState pcs,
                                                        int bossTurnNumber)
    {
        try
        {
            if (boss?.Shadow == null || pcs == null) return 0;
            // ⚠ 用 **Boss 自己的回合数**：`pcs.TurnNumber` 要到后面 `AlignTurnNumber` 才对齐，
            // 这里读到的还是上一回合的值（用户 2026-10-25 实测：加了这一步还是没出主宰）。
            if (bossTurnNumber != 3) return 0;                    // 卡面写死：第 3 回合

            var relics = boss.Shadow.Relics;
            if (relics == null) return 0;
            for (int i = 0; i < relics.Count; i++)
            {
                var relic = relics[i];
                if (relic == null) continue;
                string entry;
                try { entry = relic.Id.Entry; } catch { continue; }
                if (entry != "CHANDELIER") continue;

                int amount = 3;
                try { amount = (int)Math.Round((double)relic.DynamicVars["Energy"].BaseValue); }
                catch { amount = 3; }
                if (amount <= 0) return 0;
                Logger.Log($"[CharBoss] 吊灯：第 3 回合开始 +{amount} 能量（原版钩子对影子玩家不生效，这里补）");
                return amount;
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 吊灯加费计算失败: " + e.Message);
        }
        return 0;
    }

    /// <summary>
    /// 「意外光滑的石头」（<c>OddlySmoothStone</c>）：卡面是「每场战斗开始时获得 1 点敏捷」，
    /// 反编译出来和「数据磁盘 / 护喉甲」一样挂在 <c>AfterRoomEntered</c> 上 —— 那发生在
    /// **建影子 Player 之前**，所以对 Boss 永远不生效（用户 2026-10-25：「意外光滑的石头没有生效」）。
    /// 这里在战斗开始时按卡面补一次（读遗物的 `Dexterity` 变量）。
    /// </summary>
    /// <summary>
    /// 「金刚杵」（`Vajra`，进阶 9 奖励）：原版靠 **`AfterRoomEntered`** 给 +1 力量 ——
    /// 而影子玩家不在 `RunState.Players` 里、收不到房间事件 ⇒ 遗物挂上了但**永远不给力量**
    /// （用户 2026-11-05 实测：一层「轰击储」进阶 9/10 的金刚杵没生效）。
    /// 这里在战斗开始时替她补上（和「意外光滑的石头」同一条路子）。
    /// </summary>
    private static async Task GrantVajraStrengthAtCombatStart(CharBossMonster boss, Player shadow,
                                                              PlayerChoiceContext ctx)
    {
        try
        {
            var relics = shadow?.Relics;
            if (relics == null) return;

            decimal amount = 0m;
            for (int i = 0; i < relics.Count; i++)
            {
                var relic = relics[i];
                if (relic == null) continue;
                string entry;
                try { entry = relic.Id.Entry; } catch { continue; }
                if (entry != "VAJRA") continue;
                try { amount = relic.DynamicVars["Strength"].BaseValue; }
                catch { amount = 1m; }
                break;
            }
            if (amount <= 0m) return;

            var creature = boss.Creature;
            if (creature == null) return;
            await PowerCmd.Apply<MegaCrit.Sts2.Core.Models.Powers.StrengthPower>(ctx, creature, amount, creature, null);
            int now = -1;
            try { now = creature.GetPowerAmount<MegaCrit.Sts2.Core.Models.Powers.StrengthPower>(); } catch { /* 忽略 */ }
            Logger.Log($"[CharBoss] 金刚杵：战斗开始给 +{amount} 力量（原版 AfterRoomEntered 对影子玩家不触发，现有力量 {now}）");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 金刚杵补力量失败: " + e.Message);
        }
    }

    private static async Task GrantOddlySmoothStoneDexterityAtCombatStart(CharBossMonster boss, Player shadow,
                                                                          PlayerChoiceContext ctx)
    {
        try
        {
            var relics = shadow.Relics;
            if (relics == null) return;

            decimal amount = 0m;
            for (int i = 0; i < relics.Count; i++)
            {
                var relic = relics[i];
                if (relic == null) continue;
                string entry;
                try { entry = relic.Id.Entry; } catch { continue; }
                if (entry != "ODDLY_SMOOTH_STONE") continue;
                try { amount = relic.DynamicVars["Dexterity"].BaseValue; }
                catch { amount = 1m; }
                break;
            }
            if (amount <= 0m) return;

            var creature = boss.Creature;
            if (creature == null) return;
            await PowerCmd.Apply<MegaCrit.Sts2.Core.Models.Powers.DexterityPower>(ctx, creature, amount, creature, null);
            int now = -1;
            try { now = creature.GetPowerAmount<MegaCrit.Sts2.Core.Models.Powers.DexterityPower>(); } catch { /* 忽略 */ }
            Logger.Log($"[CharBoss] 意外光滑的石头：战斗开始 +{amount} 敏捷（原版钩子对影子玩家不生效，这里补；现在 {now}）");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 意外光滑的石头补敏捷失败: " + e.Message);
        }
    }

    /// <summary>
    /// 「在你的第 N 回合开始时」类遗物：它们内部自己数回合数，所以**战斗开始时绝不能触发**
    /// （否则整体提前一回合：用户实测船夹板第 1 回合就给格挡、口红第 2 回合就给力量）。
    /// 这些交给 FireSideTurnStart（每个 Boss 回合开始）触发。
    /// </summary>
    private static bool IsTurnStartRelic(RelicModel relic)
    {
        var entry = SafeEntry(relic);
        return entry == "BOUND_PHYLACTERY"    // 缚魂命匣：召唤奥斯提
            || entry == "PHYLACTERY_UNBOUND"   // 无界命匣（v0.2.345）：同样由 BossOsty 在回合开始召唤（2）
            || entry == "HORN_CLEAT"          // 船夹板：第 2 回合开始给格挡
            || entry == "SPARKLING_ROUGE"     // 闪亮口红：第 3 回合给力量/敏捷
            // 钻石头冠：卡面写「战斗开始时获得格挡，且第 2 回合开始前不会被移除」，
            // ⇒ 由 GrantDiamondDiademAtCombatStart 在战斗开始补（并给 2 层残影，见那里注释）。
            || entry == "DIAMOND_DIADEM"
            // 破损核心：它的钩子也是 BeforeSideTurnStart（`TurnNumber <= 1` → 引导 1 个电球），
            // 但它在**战斗开始**这一刻调 `OrbCmd.Channel` 是不出球的；真出球是在她的回合开始
            // （原版广播就会打给她）。战斗开始若也触发一次，就会和那次叠成两个电球（用户实测）。
            || entry == "CRACKED_CORE";
    }
    private static void SafeRunWithoutAwait(RelicModel relic, string hook, Func<Task> call)
        => TaskHelper.RunSafely(SafeRun(relic, hook, call));

    /// <summary>
    /// 战斗开始时给影子 Player 的遗物补触发 <c>BeforeSideTurnStart</c> / <c>AfterSideTurnStart</c>。
    ///
    /// 原版这两钩子是在「某一方回合开始」时对着所有监听者广播的，玩家的战斗开始类遗物靠的就是它
    /// （裂核引导闪电球、锚给格挡、弹珠袋上易伤、红面具上虚弱、扭曲漏斗上中毒……）。
    /// 影子 Player 的遗物不是监听者（它不在 Players 里），所以必须**逐个遗物直接调**。
    ///
    /// 两个关键点：
    ///   1. 条件判断用的是 <c>participants.Contains(owner.Creature)</c>，所以 participants 里必须放 Boss 本体；
    ///   2. 时机放在**玩家第 1 回合开始**（Boss 在这里算计划）= 玩家眼里的「战斗开始」，
    ///      并且这时当前阵营还是 Player，需要 <see cref="HittableEnemiesSidePatch.ForceRelativeSide"/>
    ///      把「可命中敌人」解释成玩家侧，否则弹珠袋会给 Boss 自己上易伤。
    /// </summary>
    public static async Task FireCombatStart(CharBossMonster boss)
    {
        var combat = boss.CombatState;
        var shadow = boss.Shadow;
        if (combat == null || shadow == null) return;

        var ctx = new BlockingPlayerChoiceContext();

        var swSide = System.Diagnostics.Stopwatch.StartNew();
        var participants = new List<Creature> { boss.Creature };
        var relics = shadow.Relics;

        // 「击剑指南」这场战斗的铸造由**我们**负责（下面 EnsureFencingManualForge 会同步铸一次）。
        // 有这件遗物就把标志立起来，于是原版 <c>FencingManual.AfterSideTurnStart</c>
        // （条件 TurnNumber &lt;= 1）在战斗开始与他的第 1 回合都不会再铸 —— 用户实测「铸了两次」。
        FencingManualForgedThisFight = false;
        for (int i = 0; i < (relics?.Count ?? 0); i++)
        {
            if (SafeEntry(relics![i]) == "FENCING_MANUAL") { FencingManualForgedThisFight = true; break; }
        }

        // 遗物里的「本场第一次」判断用 PlayerCombatState.TurnNumber <= 1，
        // 而我们把影子玩家的回合数改成逐回合递增了（否则红面具每回合都给虚弱）。
        // 这里在触发期间临时摆回 1，触发完恢复 —— 否则裂核的闪电球会因为「已经不是第 1 回合」而不生成。
        int savedTurn = 0;
        int orbsBefore = 0;
        try
        {
            var pcs = shadow.PlayerCombatState;
            if (pcs != null)
            {
                savedTurn = GetTurnNumber(pcs);
                SetTurnNumber(pcs, 1);

                // 球位容量为 0 时引导球会静默失败（OrbQueue.TryEnqueue 直接返回 false）
                int capacity = pcs.OrbQueue.Capacity;
                if (capacity == 0)
                {
                    int slots = shadow.Character.BaseOrbSlotCount;
                    if (slots > 0) pcs.OrbQueue.AddCapacity(slots);
                }
                orbsBefore = pcs.OrbQueue.Orbs.Count;
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 战斗开始前准备失败: " + e.Message);
        }

        HittableEnemiesSidePatch.ForceRelativeSide = true;
        try
        {
            if (relics != null)
            {
                Logger.Log($"[CharBoss] 战斗开始：替 {relics.Count} 件遗物触发 BeforeSideTurnStart");
                foreach (var relic in Snapshot(relics))
                {
                    // 缚魂命匣是「**在你的回合开始时**召唤奥斯提」，不是战斗开始 ——
                    // 战斗开始时她连回合都没开始，所以这里跳过它（用户实测：否则开局就站着个奥斯提）。
                    // 它由 FireSideTurnStart（每个 Boss 回合开始）负责召唤。
                    if (IsTurnStartRelic(relic)) continue;
                    await SafeRun(relic, "BeforeSideTurnStart",
                        () => relic.BeforeSideTurnStart(ctx, CombatSide.Player, participants, combat));
                }

                foreach (var relic in Snapshot(relics))
                {
                    if (IsTurnStartRelic(relic)) continue;
                    await SafeRun(relic, "AfterSideTurnStart",
                        () => relic.AfterSideTurnStart(CombatSide.Player, participants, combat));
                }
            }
        }
        finally
        {
            HittableEnemiesSidePatch.ForceRelativeSide = false;
            try
            {
                var pcs = shadow.PlayerCombatState;
                if (pcs != null) SetTurnNumber(pcs, savedTurn);
            }
            catch { /* 忽略 */ }
        }

        // 破损核心：**战斗开始**就把那个闪电球给出来（原版钩子要到他回合开始才到，用户实测）。
        // 放在球位容量已经补好之后、统计日志之前，日志里的 orbs=0→1 就是它。
        await ChannelCrackedCoreOrbAtCombatStart(boss, shadow, ctx);

        // 共生病毒（一层「黑球机」）：战斗开始再引导 1 个**黑暗球**（同样是遗物钩子到不了影子玩家）。
        await ChannelSymbioticVirusOrbAtCombatStart(boss, shadow, ctx);

        // 冷却剂（一层「黑球机」的特殊效果）：给被锁定的玩家 2 张「冷光」（手牌 + 抽牌堆）。
        await GrantCoolantLuminesceAtCombatStart(boss, ctx);

        // 战斗开始就可能引导出球（裂核）→ 球位要跟着刷一次
        try
        {
            var orbs = shadow.PlayerCombatState?.OrbQueue;
            Logger.Log($"[CharBoss] 战斗开始钩子结束: 球位 capacity={orbs?.Capacity} " +
                       $"orbs={orbsBefore}→{orbs?.Orbs.Count} [{CharacterBosses.UI.BossOrbDisplay.DescribeOrbs(boss)}]");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 战斗开始钩子统计失败: " + e.Message);
        }

        // 「击剑指南」：原版是"每场战斗开始时铸造 10"（＝他手上那把君王之剑 +20 伤害）。
        // 但遗物钩子对影子玩家不一定会执行 → 战斗开始时检查：他牌堆里一把剑都没有就自己铸造一次。
        try { EnsureFencingManualForge(shadow); }
        catch (Exception e) { Logger.Log("[CharBoss] 击剑指南兜底失败: " + e.Message); }

        // 「数据磁盘」（DataDisk，进阶奖励遗物）：卡面是「每场战斗开始时获得 1 点集中」，
        // 原版实现却是 `AfterRoomEntered` —— 那件事发生在**建影子 Player 之前**（进 Boss 房那一刻），
        // 所以对 Boss 永远不生效（用户 2026-10-24：「这货为啥有2点集中，数据磁盘不是只给一点吗？」
        // ——那 2 点是「热修复」本回合给的，磁盘的 1 点其实一次都没到过）。这里补上。
        try { await GrantDataDiskFocusAtCombatStart(boss, shadow, ctx); }
        catch (Exception e) { Logger.Log("[CharBoss] 数据磁盘补集中失败: " + e.Message); }

        // ★ v0.2.357（用户 2026-11-04：「削弱一下，移除它的遗物玉之剑」）：
        //   「玉之剑」已经从 `EventMachineScript.Relics()` 里删掉了 ⇒ 这里那段「照卡面补 1 点力量」
        //   也就不需要了（它本来就只在「她身上真有这件遗物」时才动手，删掉遗物即自动失效）。
        //   函数本体还留着，因为别的 Boss 将来若要加它可以直接用。

        // 「王室猛毒」（RoyalPoison）：原版是 `AfterPlayerTurnStart` 里
        // `if (Owner.PlayerCombatState.TurnNumber <= 1)` 就对自己造成 4 点「无格挡 + 无来源」伤害
        // —— 也就是「她的第 1 个回合开始时掉 4 血」。
        // 用户 2026-11-03 要求：「把王室猛毒的掉血时间改在**战斗开始**。他第一回合开始是不掉。」
        // 所以这里在战斗开始扣一次（整场只扣这一次，与卡面「每场战斗开始时」一致）。
        // ⚠ v0.2.357：原版那条钩子**其实是会打到她身上的**（2026-11-04 修好钩子链的 NRE 之后暴露出来：
        //   用户报「王室猛毒触发了两次」）⇒ 由 `Patches/RoyalPoisonOncePatch` 对她整条跳过来保证只掉一次。
        try { await ApplyRoyalPoisonAtCombatStart(boss, shadow, ctx); }
        catch (Exception e) { Logger.Log("[CharBoss] 王室猛毒掉血失败: " + e.Message); }

        // 「护喉甲」（Gorget，愤怒战进阶 8 的奖励遗物）：同样挂在 `AfterRoomEntered` 上
        // ⇒ 对 Boss 不生效，这里照卡面在战斗开始时补 4 层覆甲。
        try { await GrantGorgetPlatingAtCombatStart(boss, shadow, ctx); }
        catch (Exception e) { Logger.Log("[CharBoss] 护喉甲补覆甲失败: " + e.Message); }

        // 「钻石头冠」（DiamondDiadem）：卡面是「战斗开始时获得 20 点格挡。你的格挡不会在你的第 2 回合开始前被移除」，
        // 但原版把它实现在 `AfterSideTurnStart`（`TurnNumber <= 1`），并用「1 层残影」近似「格挡不被移除」。
        // 用户 2026-10-27 要求：① 战斗开始就给；② 残影给 **2 层**（她回合开始会掉 1 层）。
        // BGM：按本次遭遇切曲目（黑球机/物理机/能力机 = 一代 Boss1/2/3；愤怒战/碎屑储 = FMOD 曲目）
        try { BossBgm.OnCombatStart(BossLineups.CurrentEncounterEntry()); } catch (Exception be) { Logger.Log("[CharBoss] BGM 调用失败: " + be.Message); }

        try { await GrantDiamondDiademAtCombatStart(boss, shadow, ctx); }
        catch (Exception e) { Logger.Log("[CharBoss] 钻石头冠补格挡/残影失败: " + e.Message); }

        // 「弹珠袋」（BagOfMarbles）：卡面是「战斗开始时给予所有敌人 1 层易伤」，
        // 用户 2026-10-24 要求：**玩家刚进战斗就要吃到易伤**。
        try { await GrantBagOfMarblesVulnerableAtCombatStart(boss, shadow, ctx); }
        catch (Exception e) { Logger.Log("[CharBoss] 弹珠袋补易伤失败: " + e.Message); }
        // 红面具（用户 2026-11-08）：战斗开始给所有玩家 1 层虚弱
        try { await GrantRedMaskWeakAtCombatStart(boss, shadow, ctx); }
        catch (Exception e) { Logger.Log("[CharBoss] 红面具补虚弱失败: " + e.Message); }

        // 「贤者之石」（PhilosophersStone，用户 2026-10-31 给无色储君换上）：卡面是「每回合 +1 能量；
        //   每场战斗开始时所有**敌人** +1 力量」。对 Boss 来说「敌人」= 所有玩家 ⇒ 战斗开始给每人 +1 力量。
        //   原版挂在 `AfterRoomEntered`，影子玩家收不到派发 ⇒ 这里手动补一次。
        try { await GrantPhilosophersStoneStrengthAtCombatStart(boss, shadow, ctx); }
        catch (Exception e) { Logger.Log("[CharBoss] 贤者之石补力量失败: " + e.Message); }

        // 「意外光滑的石头」（OddlySmoothStone）：卡面是「每场战斗开始时获得 1 点敏捷」，
        // 原版同样挂在 `AfterRoomEntered` 上 ⇒ 对影子玩家不生效（用户 2026-10-25：「意外光滑的石头没有生效」）。
        try { await GrantOddlySmoothStoneDexterityAtCombatStart(boss, shadow, ctx); }
        catch (Exception e) { Logger.Log("[CharBoss] 意外光滑的石头补敏捷失败: " + e.Message); }

        // 「金刚杵」（Vajra，进阶 9 奖励）：原版 `AfterRoomEntered` 给 +力量，影子玩家收不到房间事件
        // ⇒ 遗物在栏里但不生效（用户 2026-11-05：一层轰击储进阶 9/10 金刚杵没生效）。
        try { await GrantVajraStrengthAtCombatStart(boss, shadow, ctx); }
        catch (Exception e) { Logger.Log("[CharBoss] 金刚杵补力量失败: " + e.Message); }

        // 「佩尔的士兵」（PaelsLegion，易伤战 / 储君的遗物）：原版在 `BeforeCombatStart` 里
        // `PlayerCmd.AddPet<Monsters.PaelsLegion>(Owner)` 召出装饰性随从，之后它的
        // `AfterSideTurnStart` / `AfterCardPlayed` **全是针对这只随从**做动画（Sleep / Wake / Block 三种触发）。
        // 影子玩家收不到原版 `BeforeCombatStart` ⇒ 随从永远不存在 ⇒ 每个 Boss 回合都抛
        // `遗物 PAELS_LEGION 的 AfterSideTurnStart 失败: Object reference not set…`
        // （用户 2026-10-31 表里的 diag）；而且它是在 **Hook 广播链里抛的**，会顺手把后面的监听者一起哑掉。
        // 这里照卡面把随从补出来（纯装饰；格挡倍率走 `ModifyBlockMultiplicative`，与此无关）。
        try { await EnsurePaelsLegionPet(shadow); }
        catch (Exception e) { Logger.Log("[CharBoss] 补召佩尔的士兵失败: " + e.Message); }

        // 特殊效果：挂在 Boss 身上的一层 Power（按幕区分，例如三层故障机器人的「偏差认知」）
        var powerType = boss.Def.SpecialPowerForAct(boss.ActIndex);
        if (powerType != null && boss.Creature != null)
        {
            try
            {
                // 用泛型 Apply 让游戏自己建模型并初始化，别自己 ToMutable（会 NRE）
                var method = typeof(BossHookBridge)
                    .GetMethod(nameof(ApplySpecialPower), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
                    .MakeGenericMethod(powerType);
                var task = (Task)method.Invoke(null, new object[] { ctx, boss.Creature })!;
                await task;
                Logger.Log($"[CharBoss] 特殊效果已施加: {powerType.Name}");
            }
            catch (Exception e)
            {
                Logger.Log("[CharBoss] 特殊效果施加失败: " + e);
            }

            // ★ 一层「轰击储」的「环绕轨道」第一句：「**你以一层光谱偏移开始战斗**」。
            //   `SpectrumShift`（光谱偏移）是张 Power 牌，效果就是给自己上 `SpectrumShiftPower`；
            //   它的 `BeforeHandDraw` 每回合往手牌塞 `Amount` 张**随机无色牌** ⇒
            //   玩家靠这些牌去凑「打出过无色牌的第五个回合」。
            if (powerType == typeof(Powers.BossOrbitalBombardPower))
            {
                Powers.BossOrbitalBombardPower.ResetForNewFight();   // 新的一场：计数清零
                // 计数是**倒数**：开局就把图标层数摆成 5（用户 2026-11-05：「5 开始」）。
                try
                {
                    boss.Creature.GetPower<Powers.BossOrbitalBombardPower>()
                        ?.SetAmount(Powers.BossOrbitalBombardPower.TurnsToWake);
                }
                catch (Exception e)
                {
                    Logger.Log("[CharBoss] 环绕轨道：初始化倒数层数失败（忽略）: " + e.Message);
                }
                try
                {
                    int given = 0;
                    foreach (var real in Core.BossLockOn.RealPlayers(boss.CombatState))
                    {
                        var target = real?.Creature;
                        if (target == null || !target.IsAlive) continue;
                        await PowerCmd.Apply<MegaCrit.Sts2.Core.Models.Powers.SpectrumShiftPower>(
                            ctx, target, 1m, boss.Creature, null, true);
                        given++;
                    }
                    Logger.Log($"[CharBoss] 环绕轨道：给 {given} 名真人玩家各上 1 层「光谱偏移」" +
                               "（每回合多摸 1 张随机无色牌）");
                }
                catch (Exception e)
                {
                    Logger.Log("[CharBoss] 环绕轨道：给玩家上光谱偏移失败: " + e.Message);
                }
            }
        }

        CharacterBosses.UI.BossHandUi.Refresh(boss);
    }

}
