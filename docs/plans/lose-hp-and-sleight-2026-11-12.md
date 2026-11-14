# 碎屑储「彗星」改用原版失去生命 + 血肉戏法意图改成单体（v0.2.589，2026-11-12）

> 用户实测两条反馈，均已修，两个编译目标（107 / 111）均 0 error。

## 1. 碎屑储「彗星」：直接改血量 → 原版「失去生命」

**用户**：「碎屑储的特殊效果让玩家打出碎屑可以让所有敌人失去10生命…但你是不是直接在代码层面把他们的血减少了10？
他们都没有受击动画，而且所有奥斯提（包括血肉骨用骸骨军团召唤出来的）血都减成0了还在那站着。这里应该用原版的减少生命方法。」

**根因**（已核实反编译）：`CometBossPower.AfterCardPlayed` 第 ③ 段直接调
`Creature.LoseHpInternal(10, Unblockable|Unpowered)`。而 `Creature.LoseHpInternal` 的实现只有三行：

```csharp
bool flag = CurrentHp > 0 && amount >= CurrentHp;
int num = (int)Math.Clamp(amount, 0m, 999999999m);
CurrentHp = Math.Max(CurrentHp - num, 0);
return new DamageResult(this, props) { UnblockedDamage = …, WasTargetKilled = flag, … };
```

它**只改数字**：不播受击动画、不弹伤害数字、不走 `WasTargetKilled` 的死亡处理
⇒ 敌人没动画、奥斯提（含骸骨军团召唤的）血到 0 还站着。返回值也被丢弃了。

**原版出口**（反编译 `PoisonPower.Trigger`，中毒就是「失去生命」的标准写法）：

```csharp
await CreatureCmd.Damage(ctx, Owner, Amount, ValueProp.Unblockable | ValueProp.Unpowered, null, null);
```

**改法**：一次性对**所有存活敌方**走 5 参数重载

```csharp
await CreatureCmd.Damage(choiceContext, live, 10,
    ValueProp.Unblockable | ValueProp.Unpowered, (Creature?)null);
```

- 语义不变：仍然不吃格挡 / 力量 / 易伤（`Unblockable | Unpowered`）
- 恢复完整表现：受击动画、伤害数字、死亡处理（奥斯提会真的死掉退场）
- 日志改成打「改前（血量）→ 改后（血量 / 已死）」，方便下次对账
- ⚠ **必须用 5 参数重载**：107 的 `CreatureCmd.Damage` 没有 7 参数版本（实测 `CS1501`）

## 2. 血肉骨「血肉戏法」意图：不再显示成群体

**用户**：「血肉骨所有的血肉戏法伤害似乎都被错误的显示成群体伤害，但实际上血肉戏法本身是单体的，
而当她使用苦难时才会变成『群体』（除了被亡灵契约师锁定的玩家外显示苦难基础伤害 + 被她锁定玩家的
负面效果种类 × 9，被她锁定的玩家只显示苦难的基础伤害。记得考虑在本回合刚施加的负面效果）」

**根因**：`BossIntentBuilder.IntentsFor` 里只要 `hasDebuff` 就无条件 `new RawAttackIntent(sleight)`
⇒ 每个玩家的手牌栏都显示「自己会挨这一笔」，看起来就是群攻。

**真实语义**（反编译 `SleightOfFleshPower.AfterPowerAmountChanged`）：她每给**某个目标**上一个负面，
就对**那个目标**造成 `Amount` 点无来源伤害 ⇒ 单体，目标 = 被这只 Boss 锁定的玩家。

**改法**（三处）：

1. **普通减益牌**：换成 `LiveDelegateSingleAttackIntent`，逐帧按「看意图的人是不是被锁的那位」判断：
   - 单体牌：只有被锁定的玩家看得到（别人显示 0）；
   - 群体减益牌（`TargetType.AllEnemies`，如闪电霹雳）：每个玩家都会挨 ⇒ 都显示；
   - 数值 = 该牌负面**种类数** × 血肉戏法（原版触发点是 `AfterPowerAmountChanged`，一种类型一次）。
2. **苦难（MISERY）**：不再单开一行，而是**并进牌面伤害**成一个数：
   新分支 `MiserySleightPerHit(card) is int miseryPer` → `LiveDelegateSingleAttackIntent`，
   `FinalDamage(牌面) + (看的人没被锁 ? 种类数 × 9 : 0)`。被锁玩家只看到牌面伤害。
3. **「本回合刚施加的负面效果」**：新增
   `FleshBone.LockedPlayerDebuffKinds(boss, upToCard)` —— 除了被锁玩家**现在**身上的负面，
   还把「她本回合还没打出、但**排在苦难之前**的牌会给玩家上的负面」按**类型**去重计入
   （辅助：`FleshBone.DebuffPowerTypes(card)`，与既有 `DebuffKinds` 同一套判据）。
   原因：意图是在玩家回合画的，那时她这回合的牌一张都还没打，只看 `creature.Powers` 会漏掉
   摧残 / 违逆 / 恐惧 这些「马上要上身」的负面。

## 3. 一并说明

- 版本：清单与 `BuildInfo.Tag` 都到 **v0.2.589**；本轮**没有**上传工坊、没有改 `workshop/changenote.txt`。
- **血肉戏法的次数按「上给敌人的负面类型数」算**（用户订正：「命运同担是给敌人上一次给自己上一次，
  血肉戏法只在给**敌人**上 debuff 的时候触发，所以也应该只触发一次」）：
  用 `FleshBone.DebuffPowerTypes(card).Count`，`Math.Max(1, …)`。
  反编译核对 `SharedFate.CanonicalVars`：`EnemyStrengthLoss` / `PlayerStrengthLoss` 两个都是
  **普通 `DynamicVar`**（不是 `PowerVar<>`）⇒ 本来就不会被数进去，命运同担恒按 1 次 = 9 点。
  卡组里其余减益牌也都只上 1 种类型；真出现「一张牌给敌人上两种不同负面」时才会是 2 次
  （原版 `AfterPowerAmountChanged` 确实是每施加一次算一次）。
