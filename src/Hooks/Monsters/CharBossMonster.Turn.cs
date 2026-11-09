using System;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;
using CharacterBosses.Core;

namespace CharacterBosses.Monsters;

/// <summary>
/// <see cref="CharBossMonster"/> 的「回合准备」部分：算这一回合的手牌、能量、计划，把计划写进招式。
/// 拆出来纯粹是为了单文件 200 行以内。
/// </summary>
public abstract partial class CharBossMonster
{
    /// <summary>在玩家回合开始前跑一次：抽牌/造牌 → 选牌 → 把计划写进招式（意图即实际计划）。</summary>
    internal void PrepareTurn()
    {
        // ★ 击晕闸门（用户 2026-11-04）：被击晕时**绝不能重建计划**，否则「下回合补打被打断的那一套」就没了。
        //
        // 为什么这道闸门正好只在「她上一轮被晕」时成立（反编译实证，见 docs/plans/stun-behavior.md）：
        //   · `PrepareTurn` 只从 `BossThinkState.GetNextState` 进来，而那条链只由玩家回合开始时的
        //     `PrepareForNextTurn → RollMove` 触发；
        //   · 玩家这一回合的吹哨一定发生在 `RollMove` **之后** ⇒ 走到这里时 `Creature.IsStunned`
        //     为 true ⟺ 她在**上一个玩家回合**里被晕过（STUNNED 那个 MoveState 还没被换掉）。
        // 被晕时 vanilla 已经替我们跳过了她的出牌（`PerformMove` 跑的是 STUNNED 的空 Task，
        // `PlayPlannedCards` 根本不会进），所以这里什么都不做就是正确行为：
        // `Plan` / `PlanReady` / `TurnNumber` / 手牌 全部保持「被打断的那一回合」的样子。
        if (Stunned)
        {
            Logger.Log($"[CharBoss] 击晕：跳过本回合的规划重建（待执行计划保留 {Plan.Count} 张，" +
                       $"TurnNumber 停在 {TurnNumber}，PlanReady={PlanReady}）");
            return;
        }

        Plan.Clear();
        PlanReady = true;
        try
        {
            Shadow ??= ShadowPlayerFactory.Attach(this);
            BossRunStats.Ensure(CombatState, Shadow);   // 每层/每回合都确认一次（统计表会按地图点重建）
            // 进房时 CombatState 还没接上、幕数拿不到，血量可能是错的；第一个回合这里一定拿得到，补正一次
            FixActHp();

            // 战斗开始类遗物（裂核引导球、锚给格挡、弹珠袋上易伤……）必须在玩家第 1 回合就生效，
            // 而不是等到 Boss 自己回合开始；这里只触发一次。
            if (!CombatStartFired)
            {
                CombatStartFired = true;
                BossHookBridge.FireCombatStartWithoutAwait(this);
            }

            var pcs = Shadow?.PlayerCombatState;
            if (pcs == null)
            {
                Logger.Log("[CharBoss] PrepareTurn: no shadow player available");
                return;
            }

            // 药水（炼药猎的「调制」）：补栏位 + 发初始药水，整场只做一次。
            // 原版不会替影子玩家准备药水栏位（`MaxPotionCount` 是 0），所以必须我们自己补。
            EnsurePotionsOnce(Shadow!);

            BossPiles.DiscardHand(pcs);

            // 天赋君权（DivineRight）本来是「进入房间时 +3 辉星」，但那是走真人玩家的房间事件 →
            // 影子玩家收不到 ⇒ 他的星恒为 0，吃星的牌（群星斗篷 1 星、陨星 2 星）全都打不出（用户实测）。
            // 规划前先补齐，否则意图里连这些牌都不会出现。
            // 多人「跟踪锁定」：每回合幂等地确认「这只 Boss 锁定了谁」（单人时就是唯一玩家，无副作用）。
            Core.BossLockOn.EnsurePower(this);

            EnsureStarterStars(pcs);
            // 能量 = 定义值 + 遗物带来的增减（涅奥祝福 -1、低语耳环 +1 …）。
            // 遗物那部分直接问影子 Player 的 MaxEnergy 与角色基础能量之差，这样加/减费遗物都自动生效；
            // 涅奥祝福的少抽 2 张牌仍然手动记账（Boss 不走原版抽牌管线）。
            var blessing = Def.CarriesNeowBlessing;
            int handSize = Def.HandSizeForAct(ActIndex) - (blessing ? Relics.NeowBlessing.DrawPenalty : 0);
            int relicEnergy = 0;
            try { relicEnergy = pcs.MaxEnergy - Shadow!.Character.MaxEnergy; }
            catch { /* 取不到就按 0 算 */ }
            int energy = Def.EnergyForAct(ActIndex) + relicEnergy;

            // 灯笼：**第 1 回合 +1 能量**。它是战斗内一次性加成、不走 MaxEnergy，
            // 所以上面那笔「MaxEnergy 差值」读不到它（用户实测：面板 3 费，第 1 回合四张牌打不完）。
            try
            {
                if (TurnNumber == 0)   // 这里还没 ++，第 1 回合就是 0
                {
                    var relicsOfBoss = Shadow?.Relics;
                    for (int i = 0; i < (relicsOfBoss?.Count ?? 0); i++)
                    {
                        var entry = relicsOfBoss![i]?.Id.Entry;
                        if (entry == "LANTERN") { energy += 1; break; }
                    }
                }
            }
            catch { /* 忽略 */ }

            // 有剧本的 Boss 不抽牌堆：手牌由剧本直接造出来（意图因此完全固定）
            TurnNumber++;
            // 新回合的记账从 0 起：本回合「她自己的回合内抽牌」与「打出的灵魂」都还没发生
            // （意图里会提前把这两笔补进去，之后每真的发生一次就收回来一张）
            HerOwnDrawsThisTurn = 0;
            SoulsPlayedThisTurn = 0;
            OstyHpGainedThisTurn = 0;
            PlannedStrengthGainedThisTurn = 0;
            PendingReturnStrength = 0;     // 「一心化万」的待结算力量池：每回合从 0 起
            PotionsDrunkThisTurn = 0;      // 本回合还没喝药（爬行动物饰品的力量预补要用，见 BladesGrowth）
            ScriptEnergySpent = 0;         // ★ v0.2.532：本回合「已花能量」从 0 起（补打重算预算要用）
            var script = Def.ScriptForAct(ActIndex);
            var scriptedTurn = script?.TurnAt(TurnNumber);

            // 本回合计划喝的药水（炼药猎）：UI 用它决定在药水图标下面写不写「强化」。
            try
            {
                PlannedPotions = new List<Type>();
                foreach (var sp in scriptedTurn?.Potions ?? new List<ScriptPotion>())
                    if (sp?.Potion != null) PlannedPotions.Add(sp.Potion);

                // 炼制药水要炼哪几瓶（按剧本顺序排队，出牌时逐张取）
                PlannedBrews = new Queue<Type>();
                // 「打这张牌之前先喝哪瓶」（复制药水夹在牌中间那种），按 (牌, 药水) 排队
                PlannedDrinks = new Queue<(Type Card, Type Potion)>();
                foreach (var sc in scriptedTurn?.Cards ?? new List<ScriptCard>())
                {
                    if (sc?.BrewPotion != null) PlannedBrews.Enqueue(sc.BrewPotion);
                    if (sc?.DrinkBefore != null && sc.Card != null)
                        PlannedDrinks.Enqueue((sc.Card, sc.DrinkBefore));
                }
            }
            catch
            {
                PlannedPotions = new List<Type>();
                PlannedBrews = new Queue<Type>();
                PlannedDrinks = new Queue<(Type Card, Type Potion)>();
            }
            // 剧本可以按回合覆盖能量（用户设计里「第 7 回合起多一费」这种）
            if (scriptedTurn != null && scriptedTurn.Energy > 0) energy = scriptedTurn.Energy;
            // 也支持**强制 0 费**（黑球机第 1 回合：3 − 涅奥祝福 1 − 面包 2 = 0）。
            // `Energy = 0` 表示「不覆盖」，所以 0 费用这个单独的标记。
            if (scriptedTurn != null && scriptedTurn.ForceZeroEnergy) energy = 0;

            // 「吊灯」（Chandelier）：卡面是「在你的第 3 回合开始时获得 3 点能量」。
            // 反编译它的钩子是 `AfterSideTurnStart`，条件 `participants.Contains(Owner.Creature)`
            // —— 对影子玩家不生效（下面第 129 行的 `pcs.Energy = …` 还会把任何到账的能量硬覆盖掉），
            // 于是三层「易伤战」第 3 回合只有 3 费、打不出「主宰」（用户 2026-10-25 实测）。
            // 这里按卡面直接补进本回合预算（原版那条已被 BossRelicSuppressChandelierPatch 压掉，不会双份）。
            try { energy += BossHookBridge.ChandelierEnergyBonusAtTurnStart(this, pcs, TurnNumber); }
            catch (Exception e) { Logger.Log("[CharBoss] 吊灯加费失败: " + e.Message); }

            // 「明耀酊剂」留下的 `RadiancePower`：**每个回合开始时 +1 能量**（用户 2026-10-25）。
            // ⚠ 只在剧本**没有**逐回合写死能量时才补这一笔：炼药猎的剧本现在整轮写死 3 费
            //   （用户 2026-10-25 订正：明耀酊剂无限续杯 ⇒ 每回合都按 3 费规划，上限仍是 2），
            //   再叠这 +1 就会变成 4 费。
            // 原版那条 `AfterEnergyReset` 对我们是压掉的（BossRadianceSuppressPatch），所以不会双份。
            if (scriptedTurn == null || scriptedTurn.Energy <= 0)
            {
                try
                {
                    var radiance = Creature?.GetPower<MegaCrit.Sts2.Core.Models.Powers.RadiancePower>();
                    if (radiance != null)
                    {
                        int bonus = 1;
                        try { bonus = radiance.DynamicVars.Energy.IntValue; } catch { /* 用 1 */ }
                        energy += bonus;
                        Logger.Log($"[CharBoss] 明耀酊剂：本回合 +{bonus} 能量（规划期算入）");
                    }
                }
                catch (Exception e) { Logger.Log("[CharBoss] 明耀酊剂加费失败: " + e.Message); }
            }

            // 「明耀酊剂」的**层数要在她每回合开始时 -1**（用户 2026-10-26 实测：计数一直不减）。
            // 原版是 `RadiancePower.AfterEnergyReset` 里「GainEnergy + Decrement」，而那条钩子对影子玩家
            // 根本不派发（见 `BossRadianceSuppressPatch`：我们不让她真的加能量，因为能量由剧本写死），
            // 于是连 Decrement 也一起没了 ⇒ 图标上的层数永远停在那里。
            // 这里把「计时」补回来：只减层数（到 0 时 `PowerCmd.Decrement` 会自己移除这层）。
            // ⚠ 上面那段 +1 能量只在剧本没写死能量时才加，所以不会因为这次减层而少算费。
            try
            {
                var radianceNow = Creature?.GetPower<MegaCrit.Sts2.Core.Models.Powers.RadiancePower>();
                if (radianceNow != null)
                {
                    int before = 0;
                    try { before = (int)radianceNow.Amount; } catch { before = 0; }
                    _ = MegaCrit.Sts2.Core.Commands.PowerCmd.Decrement(radianceNow);
                    Logger.Log($"[CharBoss] 明耀酊剂：本回合开始计数 {before} → {before - 1}" +
                               "（原版 AfterEnergyReset 对影子玩家不派发，这里手动走同一套 Decrement）");
                }
            }
            catch (Exception e) { Logger.Log("[CharBoss] 明耀酊剂减层失败: " + e.Message); }

            // 本回合还会发生几次「印牌」（共鸣）：她回合开始 1 次 + 剧本里打君权自授/发现各 1 次。
            // 超质量体的意图要提前把这些算进去（每发生一次再收回去，见 NoteGenerationFired）。
            try
            {
                int planned = 1;
                foreach (var sc in scriptedTurn?.Cards ?? new List<ScriptCard>())
                {
                    var name = sc.Card?.Name ?? string.Empty;
                    if (name == "ManifestAuthority" || name == "Discovery") planned++;
                }
                PendingGenerations = planned;
            }
            catch { PendingGenerations = 0; }
            if (scriptedTurn != null)
            {
                // 循环进阶：每轮循环按剧本设定额外升级一对牌（用户设计）
                var cycleUpgrades = script!.UpgradeSetFor(script.CycleAt(TurnNumber));
                // 进阶加成（例如进阶 8 的厄运之衣+、进阶 9 的友谊+/无处可逃+/荒疫打击+）
                // 也要作用到**剧本造出来的手牌**上 —— 否则牌面上看不到升级版。
                var ascensionUpgrades = AscensionUpgradeTypes();
                var merged = new List<Type>(cycleUpgrades);
                foreach (var t in ascensionUpgrades) if (!merged.Contains(t)) merged.Add(t);
                // 「能量汲取」升级过的牌：之后的循环要用**升级版**造手牌（用户要求），
                // 否则弃牌堆里明明是升级版、手里却又冒出未升级的原版。
                foreach (var t in PersistentlyUpgradedTypes()) if (!merged.Contains(t)) merged.Add(t);
                BossScriptRunner.BuildHand(CombatState, Shadow, pcs, scriptedTurn, merged.ToArray());
            }
            else
            {
                BossPiles.Draw(pcs, Math.Max(1, handSize));
            }

            // 「等离子球回合开始 +1 能量」和「充电：下回合 +1 能量」都在这里补进预算
            int plasma = BossOrbMath.PlasmaEnergyFor(pcs.OrbQueue);
            int charged = NextTurnEnergyBonus;
            NextTurnEnergyBonus = 0;
            // 「拖延 / 汇流」这类牌的「下回合 +N 能量」：只对定义里声明要兑现的幕生效
            int delayed = PendingNextTurnEnergyCredit();
            pcs.Energy = Math.Max(0, energy) + plasma + charged + delayed;
            if (plasma > 0) Logger.Log($"[CharBoss] 等离子球 +{plasma} 能量");
            if (charged > 0) Logger.Log($"[CharBoss] 充电 +{charged} 能量");
            if (delayed > 0) Logger.Log($"[CharBoss] 「下回合 +{delayed} 能量」到账（EnergyNextTurnPower）→ 本回合 {pcs.Energy} 点");

            // 「下回合获得 N 点辉星」（StarNextTurnPower）：原版是靠
            // <c>StarNextTurnPower.AfterEnergyReset(Player)</c> 里 <c>PlayerCmd.GainStars</c> 结算的，
            // 而那个钩子只对 <c>CombatState.Players</c> 里的人触发 —— 影武者不在里面，星永远不到账
            //（用户实测：下回合辉星的 buff 不给她加星；第二幕铸剑储君同款问题）。
            // 这里在能量重置点手动结算一次，然后把这一层移除（原版结算完也是移除自己）。
            // ⚠ 只在**他的**回合开始结算「下回合辉星」（用户 2026-10-28：仍过早 —— 这段代码在玩家回合的能量重置也会跑）。
            try { if (Creature?.CombatState == null || Creature.CombatState.CurrentSide == Creature.Side) SettleStarNextTurnPower(); }
            catch { SettleStarNextTurnPower(); }            // 影子玩家的回合数要跟着 Boss 递增：遗物里「本场第一次」类条件用的是 TurnNumber <= 1，
            // 不递增的话红面具/裂核会每回合都触发一次（用户实测：红面具每回合给虚弱）。
            // ⚠ 不能递增成「比真实回合多 1」：船夹板/闪亮口红是 AfterBlockCleared 里比对
            // PlayerCombatState.TurnNumber == 2 / == 3 的，多 1 就整体提前一回合（用户反复实测）。
            // 直接对齐成 Boss 自己的回合数。
            try { BossHookBridge.AlignTurnNumber(this, pcs); }
            catch (Exception e) { Logger.Log("[CharBoss] 回合数对齐失败: " + e.Message); }

            // MaxEnergy 会走 Hook.ModifyMaxEnergy（遍历所有监听者），第三方 mod 出问题时它会抛异常。
            // 所以这里取一次「安全值」用于日志，避免日志把整个 PrepareTurn 带崩（踩过）。
            int maxEnergy = pcs.Energy;
            try { maxEnergy = pcs.MaxEnergy; } catch { /* 忽略 */ }

            Logger.Log($"[CharBoss] {Def.Key} turn {pcs.TurnNumber}: hand={pcs.Hand.Cards.Count} " +
                       $"energy={pcs.Energy}/{maxEnergy} draw={pcs.DrawPile.Cards.Count} " +
                       $"discard={pcs.DiscardPile.Cards.Count} 祝福={blessing} 剧本={scriptedTurn != null}");

            if (scriptedTurn != null)
            {
                Plan.AddRange(BossScriptPlanner.Plan(this, pcs, scriptedTurn, TurnNumber));
            }
            else { Plan.AddRange(BossPlanner.Plan(this, pcs, Def)); }

            // BGM（用户 2026-10-28）：推进曲目分段 —— 现在只剩「女王一阶段 / 二阶段」
            //（炼药猎触发瓶中精灵后切二阶段；易伤战恒一阶段）。
            // ⚠ 其它档位的分段都已经删掉：`<boss>_progress` 那类 FMOD 参数对我们播的曲目
            //   完全不起作用（详见 BossBgm 顶部注释），二层改成统一帝皇蟹、碎屑储改成瀑布巨兽。
            try
            {
                BossBgm.OnBossTurn(TurnNumber);
            }
            catch (Exception e) { Logger.Log("[CharBoss] BGM 分段切换失败: " + e.Message); }

            // 小刀流：按计划重算**每张牌自己的**意图 ——
            //   · 手里剑「每 3 张攻击牌 +1 力量」的成长要按出牌顺序累加（否则越靠后的攻击预览越低）；
            //   · 终结技段数＝本回合在它之前打出的攻击数（原版 CalculatedHits 规划时是 0）。
            BladesGrowth.RecomputeAll(this, Plan);
            try
            {
                Logger.Log($"[CharBoss] 规划期 力量={Creature.GetPowerAmount<MegaCrit.Sts2.Core.Models.Powers.StrengthPower>()}" +
                           $" 补正={BossIntentBuilder.PendingRise(Creature)}");
            }
            catch { /* 忽略 */ }

            CharacterBosses.UI.BossHandUi.Refresh(this);

            if (MoveStateMachine != null)
            {
                MoveStateMachine.States[PlayStateId] = BuildMoveState();
            }

            // 把这一回合的意图立刻写进原版 `MoveState.Intents` / `NextMove.Intents`：
            // 玩家是在**自己的回合**里打出「眼部攻击」这类牌，那时第三方会问
            // `MonsterModel.IntendsToAttack`（= `NextMove.Intents` 里有没有 Attack/DeathBlow）。
            // 以前只在「她回合开始」发布一次 ⇒ 玩家的整个回合里那条一直是旧的（甚至空）⇒ 判成「不攻击」。
            PublishVanillaIntents();

            // 一层「黑球机」：**本回合的计划里有「眼部攻击」⇒ 她现在就获得「眼部攻击」buff**
            //（玩家看到这层 buff 就知道「这回合我要打过她，她那记眼部攻击就会给我虚弱」）。
            // 目视可见的 Power，她那回合结束时由它自己移除（见 Powers/GoForTheEyesAimPower）。
            try
            {
                bool aimsWithEyes = false;
                foreach (var play in Plan)
                {
                    string entry = string.Empty;
                    try { entry = play?.Card?.Id.Entry ?? string.Empty; } catch { /* 忽略 */ }
                    if (entry == "GO_FOR_THE_EYES") { aimsWithEyes = true; break; }
                }
                if (aimsWithEyes && Creature != null &&
                    Creature.GetPower<Powers.GoForTheEyesAimPower>() == null)
                {
                    _ = MegaCrit.Sts2.Core.Commands.PowerCmd.Apply<Powers.GoForTheEyesAimPower>(
                        new MegaCrit.Sts2.Core.GameActions.Multiplayer.BlockingPlayerChoiceContext(),
                        Creature, 1m, Creature, null, false);
                    Logger.Log("[CharBoss] 她的计划里有「眼部攻击」→ 给她挂上「眼部攻击」buff" +
                               "（本回合玩家若打过她，那记眼部攻击就额外给 1 层虚弱）");
                }
            }
            catch (Exception e) { Logger.Log("[CharBoss] 挂「眼部攻击」buff 失败: " + e.Message); }

            // 新回合：冷却剂的「本回合黑球不打被动」清掉（上一回合用过就算了）
            DarkPassiveSuppressed = false;
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] PrepareTurn failed: " + e);
        }
    }

    /// <summary>
    /// 药水（炼药猎「调制」）：整场只做一次 —— 补药水栏位到设计值 + 发初始药水。
    /// 药水相关的人设（栏位数 / 初始药水 / 每瓶回血）都在 <c>BossDefinition</c> 的按幕字段里。
    /// </summary>
    private void EnsurePotionsOnce(MegaCrit.Sts2.Core.Entities.Players.Player shadow)
    {
        if (PotionsPrepared) return;
        PotionsPrepared = true;
        try
        {
            int slots = Def.PotionSlotsForAct(ActIndex);
            if (slots <= 0) return;      // 这个设计不用药水
            BossPotions.EnsureSlots(shadow, slots);
            foreach (var potion in Def.PotionsForAct(ActIndex))
            {
                if (potion != null) BossPotions.Give(shadow, potion);
            }
            int healPerPotion = Def.HealPerPotionForAct(ActIndex);
            Logger.Log($"[CharBoss] 药水就绪：栏位 {shadow.MaxPotionCount} 个，" +
                       $"带着 {BossPotions.Held(shadow).Count} 瓶" +
                       (healPerPotion > 0 ? $"（调制：每瓶回 {healPerPotion} 点生命）" : "（调制：不回血）"));
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 药水初始化失败: " + e.Message);
        }
    }

    /// <summary>
    /// 重算「还没打出的牌」的意图 —— 供 Boss 自己回合开始时调一次。
    ///
    /// 为什么需要：偏差认知（<c>BiasedCognitionPower</c>）在 **Boss 回合开始**掉 1 点集中（力量跟着镜像），
    /// 而意图是在**玩家回合开始**就算死的，那时这笔还没扣，所以规划时先用
    /// <see cref="BossIntentBuilder.PendingDrop"/> 从基础值里减掉 1（意图节点渲染时会再补当前力量，正好抵消）。
    /// 等 Boss 回合真的开始时，这 1 点已经扣掉了 → 必须**把基础值还原**（<c>pendingDrop = 0</c>），
    /// 否则意图节点会用「已经降过的力量」再加一次扣减，行动时显示偏低（用户实测）。
    /// </summary>
    internal void RefreshPlannedIntents()
    {
        var pcs = Shadow?.PlayerCombatState;
        if (pcs == null || Plan.Count == 0) return;

        try
        {
            // 静默猎手这类「没有充能球」的 Boss：交给成长修正统一重算
            //（它会把手里剑成长与终结技段数一起算对，并且用**真实当前力量**）
            if ((pcs.OrbQueue.Capacity) == 0)
            {
                BladesGrowth.RecomputeAll(this, Plan, planTime: false);
                Logger.Log($"[CharBoss] 意图已按真实力量重算（{Plan.Count} 张，无球）");
                PublishVanillaIntents();
                return;
            }

            var sim = new BossOrbSim(Creature, pcs.OrbQueue);
            // 「螺旋钻击」的段数 = 本回合已花费的能量之和 ⇒ 按计划顺序边算边累加（她这个时候还一张都没打，
            // 历史里没有 `EnergySpentEntry`，只能这样预判；用户 2026-10-24 报的就是它没被当成多段）。
            int spentEnergy = 0;
            // 「爪击」第二张起要把前一张的成长先算进意图（同 BossScriptPlanner 里那笔 clawPending）。
            int clawPending = 0;
            // ★ v0.2.357（用户 2026-11-04：「伤害预览没有计算它通过把弃牌堆的牌返回手牌、
            //   由特殊效果获得的力量」）：
            //   这条「有球」分支以前把 `extraDamage` 只传了 clawPending ⇒ **本回合会到账但还没到账的力量
            //   全部丢掉**（疯狂进食 / 一心化万返手 …）。而同文件的另一条分支
            //   （无球 Boss，走 `BladesGrowth.RecomputeAll`）是算这笔的，所以只有故障机器人两套 Boss 偏低。
            //   现在按剧本顺序照同一把尺子（`BossDamagePredictor.StrengthGainOfPlanned`）累加，
            //   再减掉本回合**已经真的到账**的那部分（`PlannedStrengthGainedThisTurn`）——
            //   与 `BladesGrowth` 里 `strengthPending` 的口径完全一致。
            int plannedStrength = 0;
            foreach (var play in Plan)
            {
                string entry = string.Empty;
                try { entry = play.Card.Id.Entry; } catch { /* 忽略 */ }
                int strengthPending = Math.Max(0, plannedStrength - PlannedStrengthGainedThisTurn);
                var intents = BossIntentBuilder.IntentsFor(play.Card, play.Target, 0, 0,
                    (entry == "CLAW" ? clawPending : 0) + strengthPending,
                    BossScriptPlanner.HelixExtraHits(play.Card, spentEnergy),
                    // ★ v0.2.412（用户实测：开信刀的 5 在几次重算之间从全息影像跳到高速脱离、再跳到防御）：
                    //   烟囱段数 / 开信刀档位 / 散射炮的「本回合前面会生成几张状态牌」都在**规划期**就定死了，
                    //   这里原样再用。不传的话会退化成「从剩余计划里重新数第几张技能牌」⇒ 牌一打出去序号就前移。
                    smokePerHit: play.SmokePerHit,
                    smokeHits: play.SmokeHits,
                    letterOpenerHit: play.LetterOpenerHit,
                    pendingStatusCards: play.PendingStatusCards,
                    // ★ v0.2.527：钢铁风暴的段数（= 本回合丢掉的张数）牌面上没有变量，
                    //   规划期由剧本算好存进 `GeneratedShivs`；这条重算路以前没传 ⇒ 面板只剩「强化」。
                    shivOverride: entry == "STORM_OF_STEEL" ? Math.Max(0, play.GeneratedShivs) : 0,
                    aoeFromCaller: true);
                BossOrbSim.AppendIntents(intents, sim.Predict(play.Card), 0, BossIntentBuilder.RandomAimIsElsewhere(play.Card));
                play.Intents = intents;
                try
                {
                    spentEnergy += Math.Max(0, play.Cost);
                    if (entry == "CLAW")
                        clawPending += (int)Math.Round((double)play.Card.DynamicVars["Increase"].BaseValue);
                    plannedStrength += BossDamagePredictor.StrengthGainOfPlanned(play.Card, this);
                }
                catch { /* 忽略 */ }
            }
            if (plannedStrength > 0)
            {
                Logger.Log($"[CharBoss] 意图已按「集中已扣」重算（{Plan.Count} 张；本回合待到手力量 {plannedStrength}，" +
                           $"已到账 {PlannedStrengthGainedThisTurn}）");
            }
            else
            {
                Logger.Log($"[CharBoss] 意图已按「集中已扣」重算（{Plan.Count} 张）");
            }
            PublishVanillaIntents();
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 意图重算失败: " + e.Message);
        }
    }

    /// <summary>
    /// 把当前计划的意图**顺带写进原版 <c>MoveState.Intents</c>**，让别的 mod 也能看见。
    ///
    /// 起因：薄荷尖塔那种「把本回合将受到的总伤害显示在玩家血条右侧」的 mod，是靠读原版意图
    /// （<c>MonsterModel.GetIntents()</c> → 当前 <c>MoveState.Intents</c>）来算的。我们这排意图是
    /// 自己画在手牌栏里的，原版那条一直是空的 ⇒ 它算不到角色 Boss 的伤害（用户实测）。
    /// 这里只是把计划里已有的意图原样搬进原版字段，不新增/不改变任何战斗行为。
    /// </summary>
    /// <summary><c>MoveState.Intents</c> 的后备字段（只反射查一次，拿到就缓存）。</summary>
    private static System.Reflection.FieldInfo? IntentsBackingField;

    internal void PublishVanillaIntents()
    {
        try
        {
            var machine = MoveStateMachine;
            var log = machine?.StateLog;
            if (log == null || log.Count == 0) return;

            MoveState? state = null;
            for (int i = log.Count - 1; i >= 0 && i >= log.Count - 4; i--)
            {
                if (log[i] is MoveState move) { state = move; break; }
            }

            var all = new List<MegaCrit.Sts2.Core.MonsterMoves.Intents.AbstractIntent>();
            // 被击晕（吹哨等）：这一回合她一张牌都不会打 ⇒ 发布**空意图**。
            // 为什么不是直接 return：那样会把上一回合的旧意图留在 `state` / `NextMove` 上，
            // 第三方模组（薄荷尖塔那种「本回合将受到多少伤害」）就会照旧报出伤害 —— 那是假的。
            // 也不能把计划里的意图照旧发出去：她不会打，发出去同样是在骗第三方。
            bool stunned = Stunned;
            if (!stunned && Plan != null)
            {
                foreach (var play in Plan)
                {
                    if (play?.Intents == null) continue;
                    all.AddRange(play.Intents);
                }
            }

            // ⚠ 光写「状态机日志里那个 MoveState」**不够**（用户 2026-10-26 报的 bug）：
            //   「Boss 要打攻击牌时不算作攻击意图 ⇒ 眼部攻击上不了虚弱，别的模组的同类牌也一样」。
            // 反编译：`GoForTheEyes.OnPlay` 判断的是 `cardPlay.Target.Monster.IntendsToAttack`，而
            // <code>
            // MonsterModel.IntendsToAttack => NextMove.Intents.Any(i => i.IntentType is Attack or DeathBlow)
            // </code>
            // 也就是说别人问的是 **`NextMove`**，而 `NextMove` 是 `RollMove` 那一刻定下的实例 ——
            // 我们每回合 `States[PlayStateId] = BuildMoveState()` 换的是**新实例**，
            // `NextMove` 很可能还指着上一轮那个（甚至是我们还没填意图的那个）⇒ 意图虽然画出来了，
            // 但「他这一手是不是攻击」对第三方永远是否。
            // 所以这里**同时**写两处：日志里那个 state + `NextMove`（真正被问的那个）。
            // 展示牌（所向无敌 / 被烧掉后回合结束自动打出的彼岸咆哮…）的意图也要并进来，
            // 否则第三方问 `IntendsToAttack` 拿不到那条攻击意图。
            if (!stunned && DisplayPlays != null)
            {
                foreach (var displayPlay in DisplayPlays)
                {
                    if (displayPlay?.Intents == null) continue;
                    all.AddRange(displayPlay.Intents);
                }
            }

            var field = IntentsBackingField ??= typeof(MoveState).GetField("<Intents>k__BackingField",
                             System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                        ?? typeof(MoveState).GetField("_intents",
                             System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (field == null)
            {
                Logger.Log("[CharBoss] 发布原版意图失败：找不到 MoveState.Intents 的后备字段");
                return;
            }

            if (state != null) field.SetValue(state, all);

            MoveState? next = null;
            try { next = NextMove; } catch { next = null; }
            if (next != null && !ReferenceEquals(next, state))
            {
                field.SetValue(next, all);
                if (_nextMoveMismatchLogs++ < 8)
                {
                    Logger.Log("[CharBoss] 原版意图：NextMove 与状态机日志里那个 MoveState **不是同一个实例** ⇒ " +
                               "已把本回合意图同时写进 NextMove（否则「他这一手算不算攻击」对第三方恒为否）");
                }
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 发布原版意图失败: " + e.Message);
        }
    }

    /// <summary>「NextMove 与日志里的 MoveState 不是同一个实例」这条诊断最多打几次。</summary>
    private static int _nextMoveMismatchLogs;

    /// <summary>
    /// 她**这一手会不会真的打出攻击牌**（给 `BossIntendsToAttackPatch` 用）。
    ///
    /// 用户 2026-10-26 的口径：「只看打没打攻击牌，而不是意图里包不包含攻势」——
    /// 因为她有「群蛇形态」这类**每张牌都补一条攻势意图**的效果，用意图判断会把
    /// 「只出技能牌的回合」也说成攻击（眼部攻击就能白上虚弱了）。
    ///
    /// 算进来的一律是**真会打出去的攻击牌**：
    ///   · 计划里类型为 Attack 的牌；
    ///   · 会生成小刀的那类牌（小刀当回合就会被逐把打出去，刀也是攻击牌）；
    ///   · 展示牌（奇巧被丢弃后自动打出）里是攻击牌的（例如翻越撑击 / 连续反弹）；
    ///   · 她手牌里已经躺着的小刀（回合末会打出去，见 `SilentBlades.FrontloadRealShivs`）。
    /// </summary>
    internal bool PlansToAttackWithCards()
    {
        try
        {
            if (Plan != null)
            {
                foreach (var play in Plan)
                {
                    if (play?.Card == null) continue;
                    try { if (play.Card.Type == MegaCrit.Sts2.Core.Entities.Cards.CardType.Attack) return true; }
                    catch { /* 单张读不到就跳过 */ }
                    if (play.GeneratedShivs > 0) return true;
                }
            }

            if (DisplayPlays != null)
            {
                foreach (var play in DisplayPlays)
                {
                    if (play?.Card == null) continue;
                    try { if (play.Card.Type == MegaCrit.Sts2.Core.Entities.Cards.CardType.Attack) return true; }
                    catch { /* 忽略 */ }
                }
            }

            var hand = Shadow?.PlayerCombatState?.Hand?.Cards;
            if (hand != null)
            {
                foreach (var card in hand)
                {
                    if (card == null) continue;
                    if (ShivPlays.IsShiv(card)) return true;
                }
            }

            // 回合结束会自动打出的那张（彼岸咆哮）：本身是攻击牌 ⇒ 这一手就算攻击
            // （用户 2026-10-31：要能被「眼部攻击」这类判定认出来）。
            try
            {
                var autoType = AutoPlayFromExhaustCardType();
                if (autoType != null)
                {
                    var model = MegaCrit.Sts2.Core.Models.ModelDb.GetById<MegaCrit.Sts2.Core.Models.CardModel>(
                        MegaCrit.Sts2.Core.Models.ModelDb.GetId(autoType));
                    if (model != null &&
                        model.Type == MegaCrit.Sts2.Core.Entities.Cards.CardType.Attack) return true;
                }
            }
            catch { /* 忽略 */ }
        }
        catch { /* 读不到就按「不攻击」——宁可少判也不误判 */ }
        return false;
    }

    /// <summary>
    /// 这一回合「**被烧掉、回合结束会在消耗牌堆里自动打出**」的那张剧本牌的**类型**
    /// （例如彼岸咆哮 <c>HowlFromBeyond</c>）；没有就返回 null。
    ///
    /// 依据是剧本的 <see cref="BossTurn.FreePlayExhaustEntry"/>：它写着「回合结束从消耗牌堆自动打出的那张牌
    /// 的 entry」，这里按 entry 在当回合的剧本牌里找回**是哪张牌**，用来发布攻击意图 / 判定攻击
    /// （用户 2026-10-31：这张牌也要有群伤攻击意图，并且能被「眼部攻击」之类认成攻击）。
    /// </summary>
    private Type? AutoPlayFromExhaustCardType()
    {
        try
        {
            var script = Def?.ScriptForAct(ActIndex);
            if (script == null) return null;

            // ⚠ v0.2.259：**不能只看 `TurnNumber`** —— 玩家回合里显示的是她**下一手**的意图，
            //   那时 `TurnNumber` 还是上一个回合 ⇒ 取错剧本 ⇒ 意图里没有彼岸咆哮（用户 2026-10-31 实测「还是没有」）。
            //   所以两个都试：当前回合优先，其次下一回合。
            foreach (int turn in new[] { TurnNumber, TurnNumber + 1 })
            {
                var turnScript = script.TurnAt(turn);
                string? entry = turnScript?.FreePlayExhaustEntry;
                if (string.IsNullOrEmpty(entry) || turnScript?.Cards == null) continue;

                foreach (var sc in turnScript.Cards)
                {
                    if (sc?.Card == null) continue;
                    string cardEntry;
                    try { cardEntry = MegaCrit.Sts2.Core.Models.ModelDb.GetId(sc.Card).Entry ?? ""; }
                    catch { continue; }
                    if (cardEntry == entry)
                    {
                        if (_autoPlayIntentLogs++ < 6)
                        {
                            Logger.Log($"[CharBoss] 自动打出牌定位：她用第 {turn} 回合的剧本 ⇒ {cardEntry}" +
                                       $"（当前 TurnNumber={TurnNumber}）");
                        }
                        return sc.Card;
                    }
                }
            }
        }
        catch { /* 忽略 */ }
        return null;
    }

    /// <summary>「自动打出牌定位」日志最多打几次。</summary>
    private static int _autoPlayIntentLogs;

    /// <summary>
    /// 招式本体：故意不带意图 —— Boss 头顶那排意图会和手牌栏里每张牌下方的意图重复，
    /// 所以意图只在手牌栏里显示（见 <c>BossHandPanel</c>）。
    /// </summary>
    private MoveState BuildMoveState()
        => new(PlayStateId, PlayPlannedCards) { FollowUpStateId = ThinkStateId };

    /// <summary>
    /// 天赋君权（<c>DivineRight</c>）：原版靠「进入房间时 +3 辉星」（<c>AfterRoomEntered</c>），
    /// 而影子玩家收不到房间事件 ⇒ 他的星恒为 0，吃星的牌（群星斗篷 1 星、陨星 2 星）全都打不出。
    /// 这里每回合把他补到遗物给的数量（默认 3 星），直接写后备字段以保证规划前就生效。
    /// </summary>
    /// <summary>本场战斗的「天赋君权 +3 星」是否已经给过（只给一次，别每回合抬）。</summary>
    private bool _starterStarsGiven;

    private void EnsureStarterStars(MegaCrit.Sts2.Core.Entities.Players.PlayerCombatState pcs)
    {
        try
        {
            // ⚠ 只在**本场战斗开始时**给一次（用户 2026-10-28 实测：每回合抬到 3 星会让
            //   「花掉星以后」又被补回来 —— 第 1 回合结束剩 2 星，他回合开始被抬成 3，
            //   再叠加汇流+的下回合 +2 就成了 5 星，而正确应该是 2 + 2 = 4 星）。
            if (_starterStarsGiven) return;

            int want = 0;
            var relics = Shadow?.Relics;
            for (int i = 0; i < (relics?.Count ?? 0); i++)
            {
                var relic = relics![i];
                if (relic == null) continue;
                string entry;
                try { entry = relic.Id.Entry; } catch { continue; }
                if (entry != "DIVINE_RIGHT") continue;
                try { want = Math.Max(0, (int)relic.DynamicVars["Stars"].BaseValue); }
                catch { want = 3; }
                break;
            }
            if (want <= 0) { _starterStarsGiven = true; return; }
            _starterStarsGiven = true;
            if (pcs.Stars >= want) return;

            int before = pcs.Stars;
            // Stars 是公开可写的（内部就是 `Stars = ...`），日志里能看到 0 → 3
            pcs.Stars = want;
            Logger.Log($"[CharBoss] 天赋君权：辉星 {before} → {pcs.Stars}（原版进入房间 +{want}，影子玩家收不到）");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 天赋君权补星失败: " + e.Message);
        }
    }

    /// <summary>
    /// 她**出牌阶段开始前**用当前真实能量重规划一次（`PlayPlannedCards` 里调用）。
    ///
    /// 为什么需要（用户 2026-10-23 实测：「意图显示要打的牌最后她没打」）：
    /// 计划是在**玩家回合里**算的，而有些牌的**耗能会变** ——
    /// 例如「忧郁 `Melancholy`：每当有任何生物死亡，这张牌的耗能 -1」。
    /// 玩家在回合里杀掉召唤物后，它的耗能就降了，计划里却还按旧值记账
    /// ⇒ 计划以为打得起、实际打不出（出牌前的资源兜底会把牌拦掉）。
    /// 此刻 `pcs.Energy` 正是本回合的能量，所以重新规划得到的计划才与真正的执行一致。
    /// </summary>
    internal void ReplanAtPlayStart()
    {
        try
        {
            // ⚠ **只给灵魂骨**（身上有「灵魂绑定」）。别的 Boss 的剧本是按回合初那份计划调好的，
            // 这里重算会改变它们的节奏（用户要求：别的 Boss 别动）。
            if (Creature?.GetPower<Powers.BossSoulBoundPower>() == null) return;

            var pcs = Shadow?.PlayerCombatState;
            var turn = Def.ScriptForAct(ActIndex)?.TurnAt(TurnNumber);
            if (pcs == null || turn == null) return;

            var fresh = Core.BossScriptPlanner.Plan(this, pcs, turn, TurnNumber);
            Plan.Clear();
            Plan.AddRange(fresh);
            Logger.Log($"[CharBoss] 出牌前按真实能量重规划：{Plan.Count} 张 " +
                       $"[{string.Join(",", Plan.ConvertAll(p => p.Card.Id.Entry))}]（能量 {pcs.Energy}）");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 出牌前重规划失败: " + e.Message);
        }
    }

    /// <summary>这张牌是不是「充电」（下回合 +1 能量，需要自己记账）。</summary>
    private static bool IsChargeBattery(CardModel card){
        try { return card.Id.Entry == "CHARGE_BATTERY"; }
        catch { return false; }
    }

    /// <summary>
    /// 「下回合 +N 能量」（<c>EnergyNextTurnPower</c>：「拖延」「汇流」这类牌给的）手动结算 ——
    /// 只有定义里 <see cref="BossDefinition.HonorsNextTurnEnergyActs"/> 声明过的幕才真的兑现。
    ///
    /// 原版到期是靠 <c>EnergyNextTurnPower.AfterEnergyReset(player)</c>，而那个钩子只对
    /// <c>CombatState.Players</c> 里的人派发，影子 Player 不在里面 ⇒ 既不兑现也永不过期
    /// （<c>BossHookBridge.RemoveNextTurnPowers</c> 在它回合开始只负责删）。
    /// 二层灵魂骨的「拖延+」= 下回合 2 费，第 2 回合「守墓人(1)+死者苏生(3)+死亡行军(1)」正好靠它凑够 5 点。
    ///
    /// ⚠ 必须在**这里**（算本回合能量的那一处）读，因为剧本第 3 回合的 <c>Energy</c> 覆盖要能叠加它。
    /// </summary>
    private int PendingNextTurnEnergyCredit()
    {
        try
        {
            if (!Def.HonorsNextTurnEnergyForAct(ActIndex)) return 0;

            var power = Creature?.GetPower<MegaCrit.Sts2.Core.Models.Powers.EnergyNextTurnPower>();
            if (power == null) return 0;
            if (ReferenceEquals(power, _creditedNextTurnEnergy)) return 0;   // 同一层只补一次
            _creditedNextTurnEnergy = power;

            int amount = Math.Max(0, (int)Math.Round((double)power.Amount));
            // 先清零再移除：PowerCmd.Remove 没 await，移除没落地时下回合会再结算一次
            //（用户 2026-10-28：汇流的下回合辉星被提前/重复结算、星数对不上）。
            try { power.SetAmount(0); } catch { /* 忽略 */ }
            _ = MegaCrit.Sts2.Core.Commands.PowerCmd.Remove(power);          // 结算完就移除（原版也是）
            return amount;
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 下回合能量结算失败: " + e.Message);
            return 0;
        }
    }

    /// <summary>已经补过能量的那一层「下回合 +N 能量」（防止同一回合被重复补算）。</summary>
    private MegaCrit.Sts2.Core.Models.Powers.EnergyNextTurnPower? _creditedNextTurnEnergy;

    // ── 灵魂绑定（二层灵魂骨）的抽牌记账 ───────────────────────────────────────
    //
    // 用户 2026-10-23 的口径：
    //   · 玩家**回合内额外抽牌** → 自动化计数 + 死亡行军增伤；**回合开始补手牌不算**；
    //   · 她**自己回合开始抽的牌**（每回合 3 张 = 手牌 5 − 涅奥的祝福 2）→ 只加自动化计数，不加死亡行军；
    //   · 她**回合内额外抽牌** → 自动化计数 + 死亡行军增伤（牌本身不搬，见 ShadowBossNoDrawPatch）。

    /// <summary>玩家上个回合内额外抽了几张（她回合开始时折算成「她自己也抽了这么多张」）。</summary>
    internal int PendingDrawCredits;

    /// <summary>她**本回合**已经真的发生的「回合内抽牌」次数（把意图里预补的那部分收回来）。</summary>
    internal int HerOwnDrawsThisTurn;

    /// <summary>意图需要重算（抽牌数 / 奥斯提生命变了）。由每帧驱动做，避免在结算中途重排 UI。</summary>
    internal bool IntentsDirty;

    /// <summary>
    /// 她**本回合**已经真的打出的「灵魂」张数。
    /// 灵魂风暴的伤害 = 消耗牌堆里每张灵魂 +4，而「灵魂」是 Exhaust 牌 ⇒ 打一张就多一张。
    /// 意图在规划期已把这回合**将要**打出的灵魂提前算进去了，这里记账后把预补的那份收回来。
    /// </summary>
    internal int SoulsPlayedThisTurn;

    /// <summary>
    /// 她**本回合**已经真的给奥斯提加了多少生命（召唤 / 灵魂×吞噬生命）。
    /// 「出击」的伤害 = base + 1 × 奥斯提生命，意图里已按剧本把这回合**将要**加的生命提前补了，
    /// 这里记账后把预补的那份收回来（用户 2026-10-23 要求出击要提前算吞噬生命与灵魂加的生命）。
    /// </summary>
    internal int OstyHpGainedThisTurn;

    // ── 意图「预补池」 ───────────────────────────────────────────────────────
    //
    // 用户 2026-10-23 的口径（原话）：
    //   「对她伤害意图的『补偿』应该在伤害确实增加后再撤回来，增加多少撤多少」，
    //   「别让她的伤害在她出牌过程中来回闪」。
    //
    // 所以预补不再每帧按剧本重算，而是在**规划期一次性冻结**成这几个数字；
    // 之后每有一笔真实增量到账，就按「每单位多少」从池子里扣掉同样的量。
    // 这样任何时候「显示值 = 真实值 + 池子里剩的」，两边同步移动 ⇒ 数字只会单向变化，不会闪。

    /// <summary>死亡行军：本回合她「还没真的抽到」的那几张牌带来的加伤（真的抽到一张就扣一点）。</summary>
    internal int DrawCompensation;

    /// <summary>灵魂风暴：本回合她「还没打出的灵魂」带来的加伤。</summary>
    internal int SoulStormCompensation;

    /// <summary>出击：本回合「还没给奥斯提加到」的生命带来的加伤。</summary>
    internal int UnleashCompensation;

    /// <summary>每抽 1 张牌给死亡行军加多少（牌面的 `ExtraDamage`，通常 4）。</summary>
    internal int DeathMarchPerDraw;

    /// <summary>每张灵魂给灵魂风暴加多少（`ExtraDamage`，未升级 4 / 升级 6）。</summary>
    internal int SoulStormPerSoul;

    /// <summary>每 1 点奥斯提生命给出击加多少（`ExtraDamage`，出击是 1）。</summary>
    internal int UnleashPerOstyHp;

    /// <summary>
    /// 她**本回合**已经真的从剧本牌身上拿到的力量（烙印「获得 1 点力量」这类）。
    /// 意图里已经把「本回合将要拿到的力量」提前补进攻击预览了，这里真的到账多少就撤多少
    /// （用户 2026-10-23：烙印加力量要把伤害预览也提前补上，加上力量后再撤回去）。
    /// </summary>
    internal int PlannedStrengthGainedThisTurn;

    /// <summary>她本回合真的多抽了 <paramref name="draws"/> 张 → 从死亡行军的预补里撤掉同样的量。</summary>
    internal void WithdrawDrawCompensation(int draws)
    {
        if (draws <= 0 || DrawCompensation <= 0) return;
        int amount = Math.Min(DrawCompensation, Math.Max(1, DeathMarchPerDraw) * draws);
        DrawCompensation -= amount;
        Logger.Log($"[CharBoss] 死亡行军预补 {amount} 已撤回（真的抽到 {draws} 张，剩余预补 {DrawCompensation}）");
    }

    /// <summary>她本回合真的打出 <paramref name="souls"/> 张灵魂 → 从灵魂风暴的预补里撤掉同样的量。</summary>
    internal void WithdrawSoulCompensation(int souls)
    {
        if (souls <= 0 || SoulStormCompensation <= 0) return;
        int amount = Math.Min(SoulStormCompensation, Math.Max(1, SoulStormPerSoul) * souls);
        SoulStormCompensation -= amount;
        Logger.Log($"[CharBoss] 灵魂风暴预补 {amount} 已撤回（真的打出 {souls} 张灵魂，剩余预补 {SoulStormCompensation}）");
    }

    /// <summary>奥斯提真的涨了 <paramref name="hpGain"/> 点生命 → 从出击的预补里撤掉同样的量。</summary>
    internal void WithdrawOstyCompensation(int hpGain)
    {
        if (hpGain <= 0 || UnleashCompensation <= 0) return;
        int amount = Math.Min(UnleashCompensation, Math.Max(1, UnleashPerOstyHp) * hpGain);
        UnleashCompensation -= amount;
        Logger.Log($"[CharBoss] 出击预补 {amount} 已撤回（奥斯提真的 +{hpGain} 生命，剩余预补 {UnleashCompensation}）");
    }

    /// <summary>玩家回合内额外抽到一张（死亡行军的伤害预览要「随时」跟着涨）。</summary>
    internal void NotePlayerInTurnDraw()
    {
        PendingDrawCredits++;
        // 直接进「死亡行军预补池」：预览立即 +每张数值；
        // 她回合开始时把这几张折算成真实历史条目、同时把池子扣回去 ⇒ 显示值连续、不跳。
        if (DeathMarchPerDraw > 0) DrawCompensation += DeathMarchPerDraw;
        IntentsDirty = true;
    }

    /// <summary>她本回合又额外抽了几张（意图里预补的那份要相应收回去）。</summary>
    internal void NoteHerOwnInTurnDraw(int count)
    {
        if (count <= 0) return;
        HerOwnDrawsThisTurn += count;
        WithdrawDrawCompensation(count);
        // ⚠ 这里**不**打 IntentsDirty：她出牌过程中重算会让数字先弹一下再落回来（用户实测「来回闪」）。
        // 稳定点是每张牌打出前（PlayPlannedCards 里的 RecomputeAll）和回合结束，届时都已经是真实值。
    }

    /// <summary>
    /// 「下回合辉星」（<c>StarNextTurnPower</c>）手动结算。
    /// 原版 <c>AfterEnergyReset(Player)</c> 里做的是 <c>PlayerCmd.GainStars(Amount, player)</c> 再移除自己，
    /// 但那个钩子只对参战玩家列表里的人触发，影武者不在 ⇒ 星永远不到账
    ///（用户实测：下回合辉星的 buff 不给她加星；第二幕铸剑储君同款问题）。
    /// </summary>
    /// <summary>手动结算「下回合辉星」并把这一层移除（BossHookBridge 的到期移除路径也调它）。</summary>
    internal void SettleStarNextTurnPower()
    {
        try
        {
            var power = Creature?.GetPower<MegaCrit.Sts2.Core.Models.Powers.StarNextTurnPower>();
            if (power == null) return;
            var pcs = Shadow?.PlayerCombatState;
            if (pcs == null) return;

            int amount = Math.Max(0, (int)Math.Round((double)power.Amount));
            if (amount > 0)
            {
                // 影子玩家的星只能自己写（PlayerCmd.GainStars 依赖参战玩家上下文，拿到的是她而不是影武者）
                int before = pcs.Stars;
                pcs.Stars = before + amount;
                Logger.Log($"[CharBoss] 下回合辉星 +{amount}：{before} → {pcs.Stars}" +
                           "（原版 AfterEnergyReset 钩子对影武者不触发）");
                // 「黑洞」靠 `Hook.AfterStarsGained` 派发 ⇒ 这笔手动加星也要补一次广播（用户 2026-10-28 实测）
                try
                {
                    var cs = Creature?.CombatState;
                    if (cs != null && Shadow != null)
                        // 不能广播 Hook.AfterStarsGained：黑洞会按 HittableEnemies 解析成敌方阵营、打到自己
                        // 黑洞：**下回合辉星到账这一刻**也要结算（不能广播原版钩子 —— 会按 HittableEnemies 打到自己）
                        try
                        {
                            int bh = 0;
                            try { bh = Creature?.GetPowerAmount<MegaCrit.Sts2.Core.Models.Powers.BlackHolePower>() ?? 0; }
                            catch { bh = 0; }
                            if (bh > 0 && cs != null)
                            {
                                var victims = new System.Collections.Generic.List<MegaCrit.Sts2.Core.Entities.Creatures.Creature>();
                                foreach (var real in CharacterBosses.Core.BossLockOn.RealPlayers(cs))
                                {
                                    if (real?.Creature == null || !real.Creature.IsAlive) continue;
                                    victims.Add(real.Creature);
                                }
                                if (victims.Count > 0)
                                {
                                    _ = MegaCrit.Sts2.Core.Commands.CreatureCmd.Damage(
                                        new MegaCrit.Sts2.Core.GameActions.Multiplayer.BlockingPlayerChoiceContext(),
                                        victims, bh, MegaCrit.Sts2.Core.ValueProps.ValueProp.Unpowered, Creature);
                                    Logger.Log($"[CharBoss] 黑洞：下回合辉星 +{amount} 到账 → 对 {victims.Count} 名玩家各 {bh} 点（无来源）");
                                }
                            }
                        }
                        catch { /* 忽略 */ }
                        _ = cs;
                }
                catch { /* 广播失败不影响加星 */ }
            }
            // 先清零再移除：PowerCmd.Remove 没 await，移除没落地时下回合会再结算一次
            //（用户 2026-10-28：汇流的下回合辉星被提前/重复结算、星数对不上）。
            try { power.SetAmount(0); } catch { /* 忽略 */ }
            _ = MegaCrit.Sts2.Core.Commands.PowerCmd.Remove(power);
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 下回合辉星结算失败: " + e.Message);
        }
    }
}
