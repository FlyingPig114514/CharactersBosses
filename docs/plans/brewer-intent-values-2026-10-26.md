# 炼药猎：意图数字必须等于真实数字（2026-10-26 晚，**v0.2.12**；此版曾误标为 v0.3.2）

> 用户原话（这一批需求的来源）：
> 「提前算好攻击牌的实际伤害，比如喝药会因为爬行动物饰品增加临时力量，精确切击的伤害与手牌数有关。
> 在狡诈药水处显示 6*3 的基础伤害，受力量虚弱等影响。在打出群蛇形态的当回合和之后的回合的每张牌下面
> 标上打 4 的攻击意图（群蛇本身不标，4 的伤害意图不要再重复造成伤害。这个伤害不受力量虚弱等影响）。
> 多人时，让群蛇形态的伤害也受跟踪锁定影响，只攻击被锁定目标。
> 在特殊效果处添加提示：警告：爆炸药水和群蛇形态属于无来源伤害，带有「为你而死」的召唤物不会替你承受该伤害。」

全部**反编译核对过**，没有猜签名。

---

## 1. 爬行动物饰品 = 每喝一瓶 +3 力量（本回合）

```
ReptileTrinket.AfterPotionUsed(potion, target):
    if (potion.Owner == Owner && CombatManager.Instance.IsInProgress)
        PowerCmd.Apply<ReptileTrinketPower>(ctx, Owner.Creature, DynamicVars.Strength.BaseValue, ...)
ReptileTrinket.CanonicalVars = PowerVar<StrengthPower>(3)
ReptileTrinketPower : TemporaryStrengthPower        // 本回合有效，回合结束移除
```

她的剧本是「药水先喝、再出牌」，所以 **3 瓶药 = +9 力量，对当回合每一张攻击牌都成立**；
而计划是在**玩家回合**里算好的（那时还没喝）⇒ 以前所有攻击意图都低了 9。
实机日志印证：`成长修正（出牌期）: 力量快照=9 当前力量=9`。

**修法**（`Core/BladesGrowth.cs` → `PotionStrengthRise`）：规划期把
`每瓶力量 × PlannedPotions.Count` 加进 `plannedBaseRise`（= 每张牌的 `pendingRise`）。
出牌期（`planTime=false`）不补 —— 那时药已经喝了，力量是真的，意图节点自己会加（避免算两遍）。

## 2. 精确切击 = 13 − 2 × 手牌里的其他牌数

```
PreciseCut.CanonicalVars = [
    CalculationBaseVar(13), ExtraDamageVar(2),
    CalculatedDamageVar(ValueProp.Move).WithMultiplier((card, _) => {
        int num = Hand.Cards.Count;
        if (card.Pile?.Type == Hand) num--;      // 其他牌
        return -num;                              // 13 + 2×(-num)
    })
]
```

⇒ 打出那一刻手牌越少，伤害越高。而意图是在玩家回合一次算死的（那时手牌满）⇒ 一直显示很低。

**修法**：`BossDamagePredictor.PreciseCutDamage(card, handCountAtPlay)`；
`BladesGrowth` 按计划顺序维护 `playedCards`，规划期传 `开局手牌 − 已打张数`、
出牌期传**当前真实手牌数**，经 `BossIntentBuilder.IntentsFor(..., handAtPlay:)` 生效。

## 3. 狡诈药水 = 6×3（受力量/虚弱影响）

```
CunningPotion: Shiv.CreateInHand(player, DynamicVars.Cards.IntValue /*3*/) 后逐个 CardCmd.Upgrade
Shiv: DamageVar(4, Move)，OnUpgrade → +2 ⇒ 小刀+ = 6
```

**修法**（`UI/BossPotionRow.IntentFor`）：`CunningPotion` → 原版 `MultiAttackIntent(6, 3)`
（小刀是攻击牌，所以力量/虚弱/易伤由意图节点自己算，正好符合「受力量虚弱等影响」）。
数字仍从模型里读（`Shiv` 的 damage 变量 + 升级），读不到才退回 6。

## 4. 群蛇形态：每张牌都多打 4（显示，不重复造成伤害）

```
SerpentFormPower.BeforeCardPlayed:  amountsForPlayedCards.Add(cardPlay.Card, Amount)
SerpentFormPower.AfterCardPlayed:   if (cardPlay.Card.Owner == Owner.Player && 取出 amount > 0) {
                                        await Cmd.CustomScaledWait(0.1f, 0.2f);
                                        Creature c = Owner.Player.RunState.Rng.CombatTargets
                                                       .NextItem(CombatState.HittableEnemies);
                                        await CreatureCmd.Damage(ctx, c, amount, ValueProp.Unpowered, Owner);
                                    }
```

- `ValueProp.Unpowered`，**不吃力量/虚弱**；可被格挡（没有 Unblockable）；
- `Owner.Player` 对她是非空的：`ShadowPlayerFactory` 用反射把 `Creature.Player` 绑到了影子 Player
  ⇒ 这个 power **确实会对她生效**（所以只需要「显示」，不需要另造伤害）。

**修法**：`BladesGrowth` 沿计划走：碰到 `SERPENT_FORM` 就把之后每张牌的 `serpentHere`
设成它的层数（读牌面的 `PowerVar<SerpentFormPower>`，读不到退回 4）；她身上已有该 power 时
（后续回合）所有牌都吃。群蛇自己那张 `serpentHere = 0`（打它的时候能力还没上身）。
`BossIntentBuilder.IntentsFor(..., serpentDamage:)` 用 **`RawAttackIntent`** 追加一条 —
Raw 意图不给 `Hook.ModifyDamage` 机会，所以数字恒等于层数（「不受力量虚弱等影响」），
而它**只是画图**：真实伤害仍由原版 power 打，不会重复。

## 5. 多人：群蛇形态跟着跟踪锁定

原版是「在 `HittableEnemies` 里**随机**挑一个」，多人就是随机挑一名玩家，等于无视锁定。

**修法**：新增 `Patches/SerpentFormLockOnPatch.cs` —— 给 `SerpentFormPower` 的
`<AfterCardPlayed>d__*` 状态机 `MoveNext` 挂前后夹，在「她的群蛇形态正在结算」这段里调用
`HittableEnemiesSidePatch.PushNarrowTo(被锁定的那位玩家的 Creature)`，于是
`HittableEnemies` 只返回那一个 ⇒ `NextItem` 只能挑到他。
单人时结果就是那个唯一玩家（零回归）；`NarrowToTargetDepth` 有 16 层兜底，不会卡死。

## 6. 文案

`调制` 描述末尾新增一行（中英 × `monsters.json` / `powers.json` 共 4 处）：

```
警告：爆炸药水和群蛇形态属于无来源伤害，带有「为你而死」的召唤物不会替你承受该伤害。
WARNING: Explosive Ampoule and Serpent Form deal sourceless damage - a summon with "Die for You" will not absorb it for you.
```

> ⚠ 英文那条里的 `"Die for You"` 必须写成 `\"Die for You\"`（JSON 转义）——
> 第一次写漏了，PCK 打包后 JSON 解析直接失败。

---

## 7. 复测看点（`mod_log.txt`）

```
[CharBoss] 爬行动物饰品：本回合要喝 3 瓶药水 → 规划期把 +9 力量（每瓶 +3）算进每张攻击牌的意图
[CharBoss] 成长修正（规划期）: … | PRECISE_CUT(补=9+手3) SERPENT_FORM(补=9) ALCHEMIZE(补=9+蛇4) …
```

- `补=` 里出现药水那 9 点 ⇒ 攻击意图与实战一致；
- `+手N`（精确切击看到的「出手时手牌数」）应随计划位置递减；
- `+蛇4` 出现在群蛇之后每张牌（**群蛇那一张没有**）；
- 药水栏里狡诈药水的意图显示 `6×3`；
- 多人时群蛇打出的伤害日志里目标应当恒为「被锁定的那位」。

---

## 8. v0.2.13：复制药水 / 爆发 → 「4×2」，以及喝药力量的「先 +9 再跳回」

### 8.1 复制药水（`DuplicationPower`）与爆发（`BurstPower`）

```
DuplicationPower.ModifyCardPlayCount(card,target,playCount) => card.Owner.Creature == Owner ? playCount+1 : playCount
BurstPower.ModifyCardPlayCount(card,target,playCount)       => 同上，但再加一条 card.Type == CardType.Skill
两者的 AfterModifyingCardPlayCount 都会 PowerCmd.Decrement(self)（爆发另有 AfterSideTurnEnd → PowerCmd.Remove）
```

`CardCmd` 里**根本没有** `ModifyCardPlayCount` 的引用 ⇒ `CardCmd.AutoPlay` 那条管线不跑这两条钩子。
所以：

- **运行时**（`CharBossMonster.PlayOne`）：两者都在这里等效实现 ——
  `dupHere`（任意牌）/`burstHere`（仅技能牌）各 +1 次重放并 `Decrement` 一层；
  两者同时存在时共打 3 次（与原版把 playCount 逐个 +1 一致）。
  ⇒ 顺带修掉「爆发对她完全空转」（剧本第 3/7 回合的爆发以前什么都不做）。
- **显示**（`BladesGrowth`）：沿计划维护 `dupLeft` / `burstLeft`：
  计划里某张牌带 `DrinkBefore = Duplicator` → 加一层；`BURST` 卡按牌面 `PowerVar<BurstPower>` 加层；
  每张牌按规则消耗一层。算出 `playCount` 传给 `BossIntentBuilder.IntentsFor`，
  段数 / 奥斯提 Raw / X 费多段都乘 `playCount`（**乘法放在 `extraHits` 累加之后**，牌自身的重放也要翻倍），
  群蛇形态那条额外意图用 `RawMultiAttackIntent(4, plays)`。
  ⚠ 出牌期（`planTime=false`）直接读**真实剩余层数**，已经打出去的牌不重复扣。

### 8.2 喝药力量「先 +9 再跳回」

爬行动物饰品的力量是**喝的那一刻**才到账，而意图预补以前是整天补满 `3 × PlannedPotions`。
她每喝一瓶真实力量 +3、预补却没撤 ⇒ 显示 `base + 9 + 3k`，等她的回合重算才掉回 `base + 9`
（用户 2026-10-26 第二问）。

修法：
- `CharBossMonster.PotionsDrunkThisTurn`：`PrepareTurn` 清零，`BossPotions.DrinkAsync` 每喝一瓶 +1
  （「剧本要喝但栏位里没有 → 跳过」也算处理过 +1，否则预补会替一瓶不存在的药一直留 +3）；
- `BladesGrowth.PotionStrengthRise(boss, planTime)` 按**还没喝的那几瓶**补：`per × (planned − drunk)`
  ⇒ `真实力量 + 预补 ≡ per × planned` 恒定；
- `CharBossMonster.ShowPotionGoneAsync` 里**先** `RefreshPlannedIntents()` **再**刷新面板，
  保证每喝一瓶立刻按新的真实力量重算（否则面板还会拿旧的预补数画一帧）。

## 9. v0.2.14：精确切击「预测 18、实打 20」+「数字一直跳」

用户录屏 `屏幕录制 2026-10-26 154836.mp4`（9.97s）抽帧看到意图数字 `15 → 18 → 12 → 14` 来回变，
并指出「开始时预测的是 18，打出来是 20」。对应的同一场日志（15:48，v0.2.13）：

```
15:48:13 剧本手牌(6): [LEG_SWEEP,SURVIVOR,DECAY,PIERCING_WAIL,PRECISE_CUT,DEFEND_SILENT]
15:48:13 成长修正（规划期）: … | PRECISE_CUT(补=9+手3)          ← 手 3 → base 13−2×2=9，+9 = 18（预测）
15:48:28 (喝完第1瓶) 当前力量=3 | PRECISE_CUT(补=6+手6)          ← 手 6 → base 3，节点再加 3 → 12
15:48:32 (打完扫腿)         手5  → base 5  → 14
15:48:33 (打完生存者)       手3  → base 9  → 18
15:48:34 (打完穿透哀嚎)     手2  → base 11 → 20                 ← 实际打出 20
```

**两个独立原因**：

1. **手牌收缩算少了 1 张** ⇒ 预测偏 2 点。真实收缩是
   `扫腿 −1`、`生存者 −2`（自己离手 + **额外弃 1 张**，日志里 `候选 5 → 选 [DECAY]`）、`穿透哀嚎 −1`
   ⇒ 打到精确切击时手牌是 **2** 张（不止「每张牌 −1」算出来的 3）。
   反编译确认 `Survivor.OnPlay` = `GainBlock` + `CardSelectCmd.FromHandForDiscard(prefs, 1)`。
2. **出牌期对所有未打出的牌都用了「当前手牌数」** ⇒ 每打一张，后面所有牌的意图都跟着变（一直跳），
   而且她回合一开始手牌还没动，数字先掉到 12。

**修法**（`Core/BladesGrowth.cs`）：

- 新增 `HandDeltaOf(card)`：这张牌**除自己离手外**还会让手牌净变几张 ——
  目前只列反编译验证过的 `SURVIVOR = −1`（弃 1 张）、`ACROBATICS = +2`（抽 3 弃 1），其它一律 0（宁可不猜）；
- 沿计划累加「收缩」：`shrink = 1 + HandDeltaOf(card) + 生成的小刀`；
  - 规划期：`handAtPlay = 开局手牌 − 前面所有牌的收缩`；
  - 出牌期：`handAtPlay = 当前真实手牌 − **还没打出去**那些牌的收缩`（已打出去的用 `StillInHand` 判定并跳过，
    因为它们已经体现在当前手牌数里了）。
- 结果：第 1 回合精确切击从规划期到她真正打出去**恒为 20**（`补=9+手2` → `补=6+手2` → `补=3+手2` → `补=0+手2`），不再跳。

> 教训：意图里的「手牌数」不是「还剩几张没打」，而是**打出那一刻的真实手牌**——
> 任何会额外弃牌/抽牌的牌（生存者、杂技…）都要进 `HandDeltaOf`，否则精确切击这种「按手牌数结算」的牌必然偏。

### 9.1 v0.2.15：v0.2.14 的符号写反了（预测 16）

`HandDeltaOf` 的语义是「**负数 = 还会少几张**（弃牌）、正数 = 还会多几张（抽牌）」，
但收缩公式写成了 `shrink = 1 + HandDeltaOf(card)` ⇒ 生存者那项变成 `1 + (−1) = 0`（等于没算它弃的那张），
手牌被估成 4 张 → `13 − 2×3 = 7`，加 9 力量 = **16**（用户 2026-10-26 第三次实测）。

正确是 **`shrink = 1 − HandDeltaOf(card)`**：扫腿 1、生存者 2（自己 + 弃 1）、穿透哀嚎 1
⇒ 累计 4 ⇒ 打到精确切击时手牌 = 6 − 4 = **2** 张 ⇒ `13 − 2×1 = 11`，加 9 力量 = **20** ✅。

另外两点：
- 生出来的小刀**当回合就会被打出去**（PlayOne 的补充循环）⇒ 对「后面的牌」的净手牌变化是 0，
  所以收缩里**不加** `play.GeneratedShivs`（v0.2.14 加过，属于多算）；
- 新增诊断：`CharBossMonster.PlayOne` 在打出精确切击时打一行
  `精确切击：出手时真实手牌 N 张——应等于意图日志里的 +手N`，两者一对就能立刻确认预测准不准。

### 9.2 v0.2.16：×2 只给「被爆发影响的牌」，不给爆发自己

用户 2026-10-26 订正：「不是给爆发变 4×2，而是给受爆发影响的牌（残影）变 4×2，因为它会被打出 2 次，触发 2 次群蛇」。

原因：我把「爆发给几层」写在了「判断这张牌打几次」**之前**，而爆发自己就是技能牌 ⇒ 它把自己也算成被影响，
于是爆发显示 ×2、运行时还会把自己再打一遍并多给一层。

修法（两边同一条规则：**出牌前**判定次数，牌的自身效果之后才算）：

- 显示（`BladesGrowth`）：先按当前 `dupLeft/burstLeft` 算出 `playCount` 并消耗层数，
  **然后**如果这张牌是 `BURST` 再把它的层数加给后续技能牌；
- 运行时（`CharBossMonster.PlayOne`）：`dupHere / burstHere` 在**主 `AutoPlay` 之前**取快照
  （原版也是出牌前问 `ModifyCardPlayCount`），重放与 `Decrement` 仍发生在主结算之后。

⇒ 爆发自身 1 次；它之后的第一张技能牌（残影）打 2 次 ⇒ 群蛇触发 2 次 ⇒ 那条额外攻击意图显示 **4×2**。

### 9.3 v0.2.17：群蛇形态的形态特效方向反了（应朝左面向玩家）

用户 2026-10-26：「开完群蛇形态后，这张牌的特效方向是反的（应该朝左朝向玩家）」。

原因：她的立绘是靠 `BossVisuals.FaceThePlayer` 把 **`Visuals.Body` 的 `scale.x` 取负**镜像的，
而形态特效走的是另一条路：

```
NSerpentFormVfx.Create(target):
    vfx = PreloadManager.Cache.GetScene(_scenePath).Instantiate<NSerpentFormVfx>();
    vfx.Initialize(target.Player);
    creatureNode.Visuals.AddFormVfx(vfx);   // → FormVfxHolder.AddChildSafely(vfx); vfx.Position = Zero;
```

`NCreatureVisuals.FormVfxHolder` 是 **`Visuals` 下一个独立的 `Control`**（反编译确认类型是 `Godot.Control`），
**不是 `Body` 的子节点** ⇒ 不继承 `Body` 的镜像，于是蛇仍然朝右、背对玩家。

修法（`Patches/SerpentFormVfxFacingPatch.cs`）：

- `[HarmonyPostfix] NSerpentFormVfx.Create`：目标 `Creature.Monster is CharBossMonster` 时，
  把**这个特效节点自己的** `scale.x` 取负（它 `Position` 被强制成 Zero，子节点都相对它定位）；
- `BossFrameTick` 的 **4Hz 慢车道**再兜一次（遍历 `FormVfxHolder` 的子节点，幂等）；
- 翻转不会被抹掉：`NSpineSpriteBoneFollower._Process` 每帧只写 `GlobalPosition`，不碰 `scale`。
- 只对我们的 Boss 生效，玩家自己打群蛇形态完全不受影响。

### 9.4 v0.2.18：顺手把**其它形态特效**也一起翻了

原版一共有五个形态特效（`MegaCrit.Sts2.Core.Nodes.Vfx.Forms.*`），
它们的 `Create` 签名**完全一致**，而且都只走**同一个挂载入口**：

```
NDemonFormVfx   （恶魔形态，铁甲）
NEchoFormVfx    （回响形态，故障）
NReaperFormVfx  （死神形态）
NSerpentFormVfx （群蛇形态，静默）
NVoidFormVfx    （虚空形态）
   └─ static Create(Creature target) → creatureNode.Visuals.AddFormVfx(vfx)
NCreatureVisuals.AddFormVfx(NFormVfx vfx):
   FormVfxHolder.FreeChildren(); FormVfxHolder.AddChildSafely(vfx); vfx.Position = Vector2.Zero;
```

所以把 v0.2.17 那个「只挂 `NSerpentFormVfx.Create`」的做法换成**只挂唯一入口**
`NCreatureVisuals.AddFormVfx`（`Patches/FormVfxFacingPatch.cs`）：

- 从 `NCreatureVisuals` 往上找 `NCreature`（最多 4 层），用它公开的 `Entity` 判断是不是我们的 Boss；
- 是就把这个形态特效节点自己的 `scale.x` 取负（幂等）；**五个形态一次全覆盖**，原版以后再加新形态也自动生效；
- 4Hz 慢车道 `EnsureFacing` 仍然兜底（`BossFrameTick` 里遍历 `FormVfxHolder` 子节点）；
- 玩家自己开形态完全不受影响（判据是 `Creature.Monster is CharBossMonster`）。

## 10. v0.2.19：调制回血「看起来没实现」+ 特殊效果说明框上移

### 10.1 回血（`Core/BossPotions.cs`）

用户 2026-10-26：「特殊效果中的喝药恢复 5 血没有实现」。

查日志的真实情况：
- **15:09 那场是生效的**：`调制：使用药水 → 回复 5 点生命（现在 516）`（受伤 511 → 516）；
- 之后每一次复测都停在**第 1 回合、满血 520/520**，那时 +5 无处可加 ⇒ 既不回血、**也不打日志**
  ⇒ 屏幕和日志都毫无痕迹，看起来就像没实现。

真正的 bug 只有一个：**混沌药水（`EntropicBrew`）走的是「补满栏位后 `return true`」的分支，
从来没走到回血那一段** ⇒ 8 瓶里唯一不回血的一瓶。

修法：
- 抽出 `HealAfterPotionAsync(shadow, healPerPotion, potionName)`，**所有喝药路径**（含混沌药水分支）都调；
- 无论生效与否都记日志，三种情况分开写清楚：
  ```
  调制：CLARITY → 回复 5 点生命（497 → 502 / 520）
  调制：CLARITY → 回 5 点生命，但她已满血 520/520 ⇒ 不生效
  调制：… 但找不到她的生物 / 她已经死亡 ⇒ 跳过
  ```
- 上限仍是「不超过最大生命」；**结算时序/数值都没变**。

### 10.2 特殊效果说明框上移（只炼药猎）

「调制」描述最长（5 行 + 警告），说明框会顶到画面底部。用户要求「往上移一些（其它 boss 的描述位置不动）」。

`UI/BossUiTuning.cs` 新增热调项 **`brewSpecialY`（默认 `-120` = 上移 120px）**，
`UI/BossSpecialUi.Place` 里只有 `boss.Def.SpecialPowerForAct(ActIndex) == typeof(BrewModPower)`（= 炼药猎）时才加这个偏移
⇒ 其它 Boss 一个像素不动。改 `mods\CharacterBosses\charbosses_ui.txt` 存盘即生效。

## 11. v0.2.20：残影被打 3 次（我改错了原版逻辑）+ 残影意图补「强化」

### 11.1 3 次的真正原因：原版 `OnPlayWrapper` 本来就按 playCount 循环

用户 2026-10-26：「残影怎么被打出了 3 次？你改原版的逻辑了？」——**是的，而且改错了**。

我此前在 v0.2.13 里手写了一段「有 DuplicationPower / BurstPower 就再打一遍」，
依据是「`CardCmd` 的类型里搜不到 `ModifyCardPlayCount`」。但反编译 `CardModel` 后看到真相：

```
GeneratePlayCount: playCount = GetEnchantedReplayCount() + 1
                   → Hook.ModifyCardPlayCount(...)          // 爆发/复制药水 在这里 +1
                   → Hook.AfterModifyingCardPlayCount(...)  // 并在这里消耗层数
OnPlayWrapper:     for (i = 0; i < playCount; i++) { CurrentPlayIndex = i; 打一次 }
```

`CardCmd.AutoPlay` 只是去调 `OnPlayWrapper` ⇒ **原版自己就会打两遍**。
我那段重放等于再补一次 ⇒ 残影 **2（原版）+ 1（我补的）= 3 次**（日志里只看到 1 行「再打一次」，
但血条上格挡涨了 3 份，用户就是这么发现的）。

**修法**：整段删除（`CharBossMonster.PlayOne`），完全交回原版。
`BladesGrowth` 里那个 `playCount` 保留，但它**只用于画意图**（4×2），不影响结算。

> 教训：不要因为「某个类里搜不到某个钩子」就断定那条管线不跑钩子 ——
> 钩子常常在更深一层（`CardModel.OnPlayWrapper`）里被调用。要么完整跟一遍调用链，要么先做「日志计数」验证。

### 11.2 残影的意图：守势 + 强化

反编译 `Blur.OnPlay` = `CreatureCmd.GainBlock(Block)` + `PowerCmd.Apply<BlurPower>(…, DynamicVars["Blur"].BaseValue, …)`
—— 变量名是**普通 `Blur`**，不是 `PowerVar<BlurPower>`，而 `ClassifyPowers` 只认泛型 `PowerVar<>`
⇒ 自动识别不到「强化」，只显示了守势。

`BossIntentBuilder` 新增点名表 `BuffVarCards = { "BLUR" }`（并进 `selfBenefit`），
于是残影的意图变成 **守势 + 强化**（守势保留，顺序不变）。
以后再有「用普通 DynamicVar 表达能力层数」的牌，往这张表里加即可。

## 12. v0.2.21：「对所有敌方」的药水被扔向她自己

用户 2026-10-26：「镣铐药水、爆炸药水这些对所有敌方生效的药水应该扔向玩家而不是自己」。

日志证据（多场都有同一行）：
```
她回合开始时 力量=0
喝药水：SHACKLING_POTION（目标 静默猎手）        ← 目标成了她自己
```

`ShacklingPotion.TargetType == AllEnemies`、`ExplosiveAmpoule.TargetType == AllEnemies`
都在 `BossPotions.ChooseTarget` 的「敌方目标」分支里，本该选真人玩家；但那个分支要求
`p.Creature.IsHittable`，而**她回合刚开始时真人玩家常常还不是 `IsHittable`**
（和 `HittableEnemiesSidePatch` 注释里记的是同一个坑）⇒ 一个都选不到，函数最后 `return me`。

两个后果：抛掷动画**朝她自己**；少数直接用 `target` 参数的效果也会落在她身上。

修法（`Core/BossPotions.cs`）：
1. `ChooseTarget`：敌方分支改成「优先可命中的真人玩家，否则退回**任意活着的真人玩家**，绝不退回自己」；
2. 喝药期间把 `HittableEnemiesSidePatch.ForceRelativeSide` 置 true（用完按原值恢复）：
   这两种药水内部读 `CombatState.HittableEnemies`，喝药那一刻必须按「Boss 出手」解释，
   否则 `CurrentSide` 还没切过来时会解释成敌方阵营（= 她自己）。

### 12.1 v0.2.22：改目标没用 —— 抛掷动画的目标点是原版**写死取 Enemy 阵营**的

用户 2026-10-26：「依旧扔给自己」。

v0.2.21 只改了 `ChooseTarget`（目标参数）与 `HittableEnemies` 的语义，但**抛掷动画用的根本不是这个参数**。
反编译 `PotionModel.OnUseWrapper`：

```csharp
if (TargetType.IsSingleTarget())
    targetPosition = NCombatRoom.Instance?.GetCreatureNode(target)?.GetBottomOfHitbox() ?? Zero;
else
{
    var list = TargetType != TargetType.AllEnemies
        ? combatState.GetCreaturesOnSide(CombatSide.Player).Where(c => c.IsHittable).ToList()
        : combatState.GetCreaturesOnSide(CombatSide.Enemy).Where(c => c.IsHittable).ToList();  // ← 写死 Enemy
    foreach (var c in list) targetPosition += node(c).VfxSpawnPosition;
    targetPosition /= list.Count;
}
var vfx = NItemThrowVfx.Create(nCreature.VfxSpawnPosition, targetPosition, potion.Image);
```

⇒ 凡是 `TargetType == AllEnemies` 的药水（镣铐药水、爆炸药水…），目标点 = **敌方阵营平均位置**，
而我们的 Boss 就在敌方阵营 ⇒ 药水朝她自己飞（效果其实早就是打在玩家身上的）。

修法：新增 `Patches/PotionThrowAtPlayerPatch.cs` —— 在 `NItemThrowVfx.Create` 的 prefix 里，
把这一发的 `targetPosition` 换成我们事先算好的**真人玩家侧平均 `VfxSpawnPosition`**（一次性，用完即清）。
`BossPotions.ArmThrowAtPlayersIfNeeded` 负责在 `OnUseWrapper` 之前算好并 Arm、
`finally` 里 Disarm（拿不到玩家位置就不干预，退回原版行为）。

> 这一条也解释了为什么「效果对了但动画还是朝自己」：**效果** 走 `CombatState.HittableEnemies`（我们早就翻对了），
> **动画** 走 `GetCreaturesOnSide(CombatSide.Enemy)`（原版写死，不看我们的任何开关）。

### 12.2 剧本：爆炸药水从第 3 回合挪到第 2 回合

用户 2026-10-26：「把第三回合扔的那个爆炸药水提前到第二回合扔，其它都不变」。

`Core/SilentBrewerScript.cs`：
- 第 2 回合 `Potions<ShacklingPotion, ShipInABottle>` → **`Potions<ShacklingPotion, ShipInABottle, ExplosiveAmpoule>`**
- 第 3 回合 `Potions<ShipInABottle, GhostInAJar, ExplosiveAmpoule>` → **`Potions<ShipInABottle, GhostInAJar>`**
- 两回合的 `Note`（设计稿逐字记录）同步改了措辞；**第 7 回合的爆炸药水保持不变**（其它都不变）。

### 12.3 v0.2.24：第 2 回合的瓶中船改成「炼制药水之后」喝

用户 2026-10-26：「第二回合的瓶中船的喝的时间放到炼制药水之后，因为在那之前这瓶药还没被炼制出来」。

日志佐证（她以前确实喝不到）：
```
剧本要喝 ShipInABottle，但栏位里没有 → 跳过
```

`Core/SilentBrewerScript.cs` 第 2 回合：
- `Potions<ShacklingPotion, ShipInABottle, ExplosiveAmpoule>` → **`Potions<ShacklingPotion, ExplosiveAmpoule>`**（回合开始只喝这两瓶）；
- 卡序里给「中和」挂上 `Drink(typeof(ShipInABottle), C<Neutralize>())` ——
  即 **夜魇 → 炼制药水+（炼出瓶中船）→ 喝瓶中船 → 中和 → 防御**。

顺带的数值一致化（`Core/BladesGrowth.cs`）：中途「打某张牌之前先喝一瓶」也会触发爬行动物饰品的 +3 力量，
所以规划期在走过 DrinkBefore 之后，给**后面那些**牌的意图补上这笔（`drinkRise`）；
出牌期不补（那时力量已经真实到账，由意图节点自己加）。

### 12.4 v0.2.25：削弱 —— 去掉「喝药回 5 血」

用户 2026-10-26：「削弱，去掉特殊效果里喝药回血这一条」。

- `Core/BossDefs.cs`：炼药猎变体的 `HealPerPotion` **5 → 0**；
- 描述（中英 × `monsters.json` / `powers.json` 共 4 处）删掉「每当静默猎手使用药水时，她恢复 5 点生命。」/「Whenever she uses a potion, she heals 5 HP.」；
- 日志从 `（调制：每瓶回 5 点生命）` 变成 `（调制：不回血）`；
- **回血代码保留**在 `BossPotions.HealAfterPotionAsync`（`healPerPotion <= 0` 时直接返回），
  以后要恢复只需把 `HealPerPotion` 填回正数。

现在「调制」的文案是：

```
调制
静默猎手的初始药水栏位没有被移除，并且会在战斗中使用药水。
她可以格挡中毒和魂劫造成的伤害。
触发瓶中精灵时，移除她的所有负面效果。
警告：爆炸药水和群蛇形态属于无来源伤害，带有「为你而死」的召唤物不会替你承受该伤害。
```
