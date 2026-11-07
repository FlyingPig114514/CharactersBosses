# 多人：Boss 格挡 / Power 层数不同步（2026-10-25 已修）

> 症状（用户 + 队友实测）：2 人联机、第一幕「愤怒战」。
> 队友那边：**12 层覆甲**、格挡 **22**；另一端：格挡只有 **10**（= 锚的 10）。
> 队友的原话判断也是对的：「覆甲提前触发了，所以 Boss 没动手就不同步」。

## 一、12 层覆甲 —— `PowerCmd.Apply` 会按人数放大敌人的 Power 层数

反编译 `MegaCrit.Sts2.Core.Commands.PowerCmd.Apply`：

```csharp
if (combatState.Players.Count > 1
    && (target.IsPrimaryEnemy || target.IsSecondaryEnemy)
    && power.ShouldScaleInMultiplayer)
    amount = power.GetScaledAmountForMultiplayer(combatState, applier, amount, target, cardSource);
```

而 `PlatingPower`（覆甲）**自己重写了**缩放，不是基类那套「×人数×幕系数」：

```csharp
// PlatingPower.GetScaledAmountForMultiplayer
return ((players.Count - 1) * 2 + 1) * amount;    // 2 人 = ×3
```

我们的「护喉甲补覆甲」（`BossHookBridge.GrantGorgetPlatingAtCombatStart`）给的是卡面值 **4 层**
⇒ 2 人局实际到账 **4 × 3 = 12 层**。这正是队友看到的数字。

同类风险（同一个缩放开关）：`ArtifactPower`（人工合成 3 层 → 联机变多）、`BufferPower`、`SkittishPower`、
`SlipperyPower`、以及基类默认的 `amount × 人数 × 幕系数`。

**修法**：新增 `src/Hooks/Patches/BossNoMultiplayerPowerScalePatch.cs` ——
`GetScaledAmountForMultiplayer` 是 virtual，`PowerCmd.Apply` 走虚调用，所以**基类 + 上述 5 个重写**一起挂 postfix：
目标 `Monster is CharBossMonster` ⇒ 还原成原值。与既有的两条同族补丁一致：
`BossNoMultiplayerHpScalePatch`（血量不缩放）、`BossNoMultiplayerBlockScalePatch`（格挡不缩放）。

## 二、22 vs 10 —— 那笔「覆甲换格挡」是谁给的、什么时候给

```csharp
// PlatingPower.BeforeSideTurnStart
if (side != CombatSide.Player) return;      // 只在**玩家**回合开始
if (Owner.IsPlayer) return;                 // 敌人才算
if (combatState.RoundNumber > 1) return;    // 只有第 1 回合
return CreatureCmd.GainBlock(Owner, Amount, Unpowered, null);
```

也就是「敌人身上有覆甲 ⇒ 第 1 回合玩家回合开始时，给等于覆甲层数的格挡」。
22 = 锚 10 + 覆甲 12。

**为什么两端不一样**：我们补覆甲是**异步 fire-and-forget**
（`PrepareTurn` 是同步方法 → `BossHookBridge.FireCombatStartWithoutAwait` → `TaskHelper.RunSafely(FireCombatStart)`，
`PowerCmd.Apply` 里第一个 `await` 之后才真正落账）。这个时点在不同客户端可能落在
「第 1 回合玩家回合开始」这条钩子的**前**或**后**：落在前面就有这笔格挡，落在后面就永远没有
（该钩子只认 `RoundNumber <= 1`）⇒ 一端 22、一端 10。

**修法（确定性写法，和「卡戎之灰 / 奥利哈钢」同一套）**：
1. `Patches/BossRelicHookBridge.cs` 新增 `BossRelicSuppressPlatingTurnStartPatch`：
   原版那条 `PlatingPower.BeforeSideTurnStart` 对**我们的 Boss** 一律压掉；
2. `GrantGorgetPlatingAtCombatStart` 在挂上覆甲之后**自己补一次等量格挡**
   （`CreatureCmd.GainBlock(me, 实际层数, Unpowered, null)`），并加幂等判断
   （已有覆甲就不再叠，读档/重进不会变成 8 层）；
3. 日志改成带实际值：`护喉甲：战斗开始 +4 层覆甲（请求 4；格挡补到 14，…）` —— 下次联机一眼能对数。

对其它覆甲来源没有副作用：玩家回合在第 1 回合永远早于她的回合，
所以「她打出岩石铠甲（StoneArmor）拿到覆甲」时 `RoundNumber` 早就不满足 `<= 1`，原版本来也不会给这笔格挡。

## 三、验证要点（下次联机）

- 2 人开局进第一幕 Boss 房，两端看愤怒战：**覆甲应是 4 层**、格挡应是 **锚 10 + 覆甲 4 = 14**，两端一致；
- 日志两侧都应出现 `护喉甲：战斗开始 +4 层覆甲（请求 4；格挡补到 14…）`；
- 进阶 8 以下的愤怒战没有护喉甲，格挡应只有锚的 10（两端一致）。

## 四、没解决 / 待观察

- 这份日志（`mod_log (6).txt`）里四次战斗全都在 Boss 还没轮到出手时就结束了（没有任何 `block=` 行），
  所以「谁先看到 22」只能靠上面的反编译推理定位；如果修完仍不同步，需要两端各自的完整 `mod_log.txt`
  一起比对（同一时间戳附近的 `护喉甲`/`格挡缩放` 行）。
- 其它同类「战斗开始类遗物补触发」（数据磁盘/意外光滑的石头/弹珠袋）都是同一个异步时点，
  目前没收到不同步反馈；若出现，按第二条同样的办法处理（压掉原版 + 我们确定性做一次）。
