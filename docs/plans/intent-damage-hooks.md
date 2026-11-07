# 意图伤害为什么不吃「减力量」（2026-10-23 修，v0.1.15）

> 用户实机截图：根除骨上挂着一排意图，玩家用**崩坠收藏家的「哀嚎」**（`COLLECTOR-CURSED_WAIL`）
> 减了她 11 点力量，根除仍显示 **10×4**，应该是 **0×4**（段数 4 没错，错的是**每段伤害**）。
> 用户要求：不只修这一张牌，要兼容**所有**减力量与**临时**减力量（任何 mod）。

## 1. 两个独立的根因

### ① 只有「原版力量」被读到

`根除`（Eradicate，X 费）走的是我们自己的 `RawIntent`（`RawMultiAttackIntent`），
它**不吃**意图节点渲染时的 `Hook.ModifyDamage`，所以 `BossIntentBuilder` 里是手算的：

```csharp
intents.Add(new RawMultiAttackIntent(Math.Max(0, damage + CurrentStrengthOf(card)), 1 + xHits));
//                                                              ↑ GetPowerAmount<StrengthPower>()
```

而崩坠「哀嚎」是这么实现的（反编译）：

```csharp
public class CursedWailPower : TemporaryDebuffPowerWrapper<CursedWail, StrengthPower>
```

「本回合失去 N 点力量」走的是**它自己的 power**，`StrengthPower` 的层数**根本不变** ⇒
手算永远是 0 修正。（原版/别的 mod 里任何「不改 StrengthPower 层数、只在 ModifyDamage 里做手脚」
的效果都会漏。）

### ② 数字在规划期就被冻住了

普通意图节点（`SingleAttackIntent` / `MultiAttackIntent`）每帧渲染都重跑一次
`Hook.ModifyDamage`，所以玩家在她回合外减她力量，数字会跟着变。
而我们的 `RawMultiAttackIntent` 把每段伤害存成一个 `int`，是**玩家回合开始时算死**的，
`RefreshPlannedIntents` / `RefreshForce` 只在少数事件（回合开始、抽牌数变化、集中变化…）里跑
⇒ 玩家中途上减力量，UI 连重画都不会重画。

## 2. 改法（三处，一个主题：**意图伤害一律走原版管道**）

| 文件 | 改动 |
|---|---|
| `Core/BossDamagePredictor.cs` | 新增 `FinalDamage(card, 牌面值, target)`：照抄原版 `AttackIntent.GetSingleDamage` —— `Hook.ModifyDamage(runState, combatState, target, dealer=她, 基础值, ValueProp.Move, null, null, ModifyDamageHookType.All, CardPreviewMode.None, out _)`。力量、虚弱、易伤、活力、以及**任何 mod** 的临时/永久增伤减伤都会算进来 |
| `Core/RawAttackIntent.cs` | 新增 `LiveMultiAttackIntent`：不再存死的 `int`，而是存一个**计算委托**，`GetIntentLabel` / `GetTotalDamage` / `Repeats` 每次被问到时重算（同一帧内缓存，避免每帧算几十次 hook 链） |
| `Core/BossIntentBuilder.cs` | X 费分支（根除）改用 `LiveMultiAttackIntent` + `FinalDamage(card, baseForHooks, victim)`；删掉 `CurrentStrengthOf` |
| `Patches/BossFrameTickPatch.cs` | 新增 `RefreshIntentsIfPowersChanged`：每帧比对「她身上所有 power 的 id+层数」指纹，变了且当前是**玩家侧行动** ⇒ `boss.IntentsDirty = true`，走既有的重算流程（她自己回合里不打标，避免数字来回闪） |
| `UI/BossHandPanel.cs` | `Rebuild` 里再加一道保险：把「每个意图的数字」拼成签名，变了就整排重建（增量同步不会重画已有格子里的意图节点） |

没有改动的部分（**故意的**）：奥斯提的攻击（`fromOsty`，吃的是奥斯提自己的力量+钙化）、
卡戎之灰（`ValueProp.Unpowered`，本来就不吃力量）、冰雹风暴/充能球激发（同样 Unpowered 语义）。
**已知仍手算的一处**：`BLADE_SYMPHONY`（刀刃交响曲）的小刀伤害还是 `ShivBaseDamage + 她的力量`，
要一起改成管道只需把那一行换成 `FinalDamage`，但它是多人换牌路径，单独一轮再动。

## 3. 实机怎么确认

`mod_log.txt` 里出现（每张牌最多 40 行，且只在「算出来的和牌面不一样」时打）：

```
[CharBoss] 意图伤害管道 ERADICATE: 牌面 11 → 0（力量=0，目标=你）
```

注意 `力量=0` 是**正常的**：哀嚎不改 `StrengthPower` 层数，修正发生在 hook 链里，
所以这一行正是「管道把它算对了」的证据。玩家减她 11 点 → 意图应立刻变 `0×4`。
