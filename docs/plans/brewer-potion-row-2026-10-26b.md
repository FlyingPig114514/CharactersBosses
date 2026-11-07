# 炼药猎药水栏：不实时刷新的**真正**原因与修法（2026-10-26 下午，v0.2.6 → v0.2.12）

> 装机 DLL：`sha256 F16AE2830ABFD2F0CD22AEF7B1C4B6DE1191B3F52EA0FBFC205A9FC4E7FFF401`（**v0.2.12**）。
> 接手者先读这一页 + `handover-brewer-2026-10-26.md`。
> 上一版交接说「怀疑 BossStatusRow.Fill 里旧的 BossPotion_* 格子没被清掉」——
> **方向对了一半，结论错了**。这一页是用 `mod_log.txt`（12:31–12:33 那次实机）逐行核对出来的。

---

## 1. 证据：rowChildren 为什么在 15 / 29 / 31 之间跳

`Fill` 里清理旧子节点的写法是：

```csharp
foreach (var child in row.GetChildren())
{
    if (ReferenceEquals(child, keep)) { row.RemoveChild(child); continue; }
    if (GodotObject.IsInstanceValid(child)) child.QueueFree();   // ← 只"排队"
}
```

**`QueueFree()` 不是立刻删除**，它只是把这棵树标记成「本帧末销毁」。也就是说此刻那些旧格子
**仍然挂在 `row` 下面**，于是：

- `row.GetChildCount()` = 「上一批正要被删的旧格子」+「这一轮新加的格子」⇒ 15/29/31 乱跳；
- 同一次 `PlaceBesideCards`（`ContentWidth` / `LayoutChildren`）把旧格子一起算进宽度与下标。

实机日志里的铁证（同一秒里三次定位）：

```
[12:32:35] 状态条 Fill: held=2 rowChildren=27         ← 旧 5 瓶 + 旧 10 遗物 + 新 2 瓶 + 新 10 遗物
[12:32:35] 状态条定位: rowW=1802        ↑ 27 个子节点全被算进宽度
[12:32:35] 状态条屏幕=(-180.28, 153.5) 尺寸=(2050, 138)   ← 整条状态栏被算成一倍宽、x 跑到屏幕外
[12:32:35] 状态条定位: rowW=872 …       ← 过了帧边界、旧格子真被删掉之后才恢复正常
```

（`rowChildren` 的期望值 = 药水数 + 遗物数 + 能量球；本例 2 + 10 + 1 = 13，临时多出来的全是「还没咽气的旧格子」。）

## 2. 真正让用户看到「图标不更新」的三件事

1. **布局被旧格子污染**：每帧会短暂按「双倍宽度」排一次，状态栏被推到 x≈-180、药水/遗物被挤到屏幕外再弹回。
   眼睛看到的就是「一顿乱跳、像是没更新」。
2. **药水意图一个都画不出来**（日志 `药水意图渲染失败: Object reference not set…`）：
   `NIntent._Ready()` 里才 `GetNode("%IntentParticle")/%Value`，而当时 `cell` **还没 AddChild 进树**，
   `_Ready` 没跑 ⇒ `UpdateIntent` 里 `_intentParticle.Texture = …` 直接空引用。
3. **刷新的「触发点」全靠调用方自觉**：喝药水有三条路径（回合开始的剧本药水 / 某张牌之前「先喝一瓶」 /
   炼制药水炼出一瓶），谁都没义务记得调 `Refresh` ⇒ 漏刷就永不过期。

## 3. v0.2.6 改了什么

| 文件 | 改动 |
|---|---|
| `src/Hooks/UI/BossStatusRow.cs` | `Fill` 清空改为**同步 `RemoveChild` + `QueueFree`**（旧格子立刻离树，排版只看得到本轮的格子）；遗物图标改为**复用 + `MoveChild` 排序**（不再每次重建 8～10 个原版 `NRelicInventoryHolder`，也不会有出树再入树的抖动）；自检日志打「药水格子数 / 期望子节点数 / rowW」 |
| `src/Hooks/UI/BossPotionRow.cs` | 格子**先入树再挂意图**（修空引用）；格子名带序号（`BossPotion_0_…`，避免 Godot 自动改名成 `@Control@4271`）；意图挪到图标**正下方**（`potionIntentGapY` 热调，负数=上移）；删掉早已停用的「缩没/弹入」死代码 |
| `src/Hooks/Patches/BossFrameTickPatch.cs` | 每帧比对 `BossPotions.Version`，一变就 `Refresh` ⇒ **任何路径改了药水，当帧就重画**；新增 5 秒一条的 fps 性能日志 |
| `src/Hooks/Core/PerfLog.cs`（新） | 计数：面板重建次数/均耗时、状态条重建次数/均耗时、子节点数异常次数 |
| 日志降噪 | `遗物图标`（每 Fill 8～10 条）、`状态条定位`、`hand ui`、`子节点 … 屏幕=` 全部改成「只打前 N 次」 |

## 4. 复测时看什么

`mod_log.txt` 里应当看到（每次喝药之后立刻出现，不再等一秒）：

```
[CharBoss] 喝药水：DEXTERITY_POTION（目标 静默猎手）
[CharBoss] 移除已喝药水失败: ...        ← 原版自己已经移除过，无害
[CharBoss] 喝药后栏位剩 4 瓶
[CharBoss] 药水变动（版本 31→32）→ 重画状态条
[CharBoss] 状态条 Fill: 药水=4(格子 4) 遗物=10 复用遗物=是 清掉=0 rowChildren=14/14 rowW=…
```

- **`格子数 == 药水数`、`rowChildren == 期望`** ⇒ 状态条干净了（这是新加的自检，不正常就会一直打）。
- `rowW` 应该在 800～1100 之间稳定，**不会再出现 1802 / 2050**。
- 药水下面应当出现原版意图图标（重画时会打一条 `药水意图：… 位置=(…) 在树上=True`）。

## 5. 卡顿：现在有数据可查了

每 5 秒一条：

```
[CharBoss] 性能(5s): fps 现=142 最低=61 | 面板重建=6次（均 1.20ms）状态条重建=6次（均 0.35ms 异常=0）
```

判读方法：

- **fps 掉、`面板重建` 次数也跟着涨** ⇒ 是我们的锅（下一步：把药水格子也做成复用、把
  `LayoutCells` 的 `Logger.Log` 全关掉）；
- **fps 掉、重建次数是 0～2** ⇒ 与本模组无关：那时我们在帧上只花 0.1ms，去看别的 mod / 原版特效。

上一版日志里 `帧回调耗时 avg=0.05ms` 只能证明「每帧驱动那点活很便宜」，
**重建节点的耗时以前根本没被计时**（`_dbgTotalMs` 和 `_dbgFillMs` 其实是同一个 stopwatch，两个数字一样），
所以「不是本模组」这个结论一直缺证据。现在补上了。

## 6. v0.2.7（当时误标 v0.3.1）：意图太小太靠下 + 「喝一瓶消失一瓶」（用户 2026-10-26 第二张截图）

### 6.1 意图的**实测**几何（别再猜，这是从 v0.2.6 的 `意图节点` 诊断行里读出来的）

```
Intent[NIntent]          pos=(-32,-32) size=(64,64)     ← 根节点被 HBox 摆在 (-32,-32)
  IntentHolder[Control]  pos=(0,0)    size=(64,64)
    Intent[Sprite2D]     pos=(30,31)                      ← 箭头中心 ≈ HBox 原点
    Value[MegaRichTextLabel] pos=(2,40) size=(62,23)
```

⇒ **箭头可视中心 = HBoxContainer 的原点，可视半径 = 32 × 缩放**。
旧的 `scale=0.5` ⇒ 箭头只有 32px（药水是 56px，所以「太小」），
旧的位置算成中心 y≈125（图标下沿才 56，所以「太靠下」）—— 两张截图完全对得上。

现在：`scale = potionIntentScalePct/100`（默认 **100** = 和卡牌下面那排意图一样大）、
`holder.Position = (56/2, 56 + potionIntentGapY + 32×scale)`（默认 gap **12** ⇒ 中心 (28,100)，
箭头占 68～132，正好落在「图标下沿」和「卡面上沿」之间）。
`NIntent._Process` 里还有个 ±10px 的上下浮动（`_intentHolder.Position = Up*(sin*10+8)`），
所以静止位置再往上抬约 8px 都属正常，不会压到药水。

### 6.2 「喝多瓶时喝第一瓶，这回合要喝的药就全没了」

日志（v0.2.6，12:55）：

```
12:55:11 held=8 计划喝=3 → 箭头在 CLARITY / RADIANT_TINCTURE / DEXTERITY_POTION
12:55:24 喝药后剩 7 → 同一次刷新里 held 已经是 6（RADIANT 也被喝掉了）→ 只剩 1 个箭头
12:55:25 held=5 → 0 个箭头
```

三瓶 `OnUseWrapper` 之间**没有任何等待**，引擎在同一帧就把三瓶全结算了 ⇒ 界面从
「8 瓶 + 3 箭头」直接跳到「5 瓶 + 0 箭头」，看起来像「喝第一瓶就全没了」（其实三次刷新只被看到一次）。

修法（`CharBossMonster.ShowPotionGoneAsync`）：**每喝一瓶**都 `BossHandUi.Refresh(this)` 立刻重画，
再 `await Cmd.Wait(0.45f)` ⇒ 一瓶一瓶消失。剧本里「某张牌之前先喝一瓶」（第 4/8 回合）也走同一条。

> 想要「缩小淡出」的消失动画也可以做（组件里那段 ghost 代码还留着思路），但要把 ghost 挂在**状态条之外**，
> 否则它会参与同一帧的宽度/下标计算（就是本次修掉的那个坑）。目前先用「一瓶一瓶消失」。

## 7. 已知遗留

- 「调制」第三条「她可以格挡中毒伤害」**仍未实现**（见交接 §3C，别用猜的 Harmony 签名）。

## 8. v0.2.8：意图**偏右半个图标**的真正原因（别再套容器）

用户第二张截图：箭头比药水整体偏右。对照 v0.2.7 的几何公式，问题出在**用了 `HBoxContainer`**：

```
刚 AddChild 完（容器还没排版）：Intent[NIntent] pos=(-32,-32)
容器排版之后：                Intent[NIntent] pos=(0,0)
```

`pos` 那个 (-32,-32) 是**排版前的快照**，我把它当成稳定位置 ⇒ 整个公式差 (30,31)。
`HBoxContainer` 会把子节点摆回 (0,0)，可见中心于是变成「容器原点 +(30,31)」，横向偏 30px。

**v0.2.8 改法**：药水意图**不再套任何容器**，`NIntent` 直接挂成格子的子节点，位置完全由我们算
（缩放绕原点；箭头可视中心 = 节点原点 +(30,31)×缩放；可视半径 ≈ 32×缩放）：

```csharp
node.Scale    = new Vector2(scale, scale);
node.Position = new Vector2(Side*0.5f - 30f*scale,                    // 水平居中在药水中线上
                            Side + gap + 32f*scale - 31f*scale);      // 箭头中心 = 图标下沿 + 间隙 + 半径
```

日志 `药水意图：… 节点原点=(-2, 69) 箭头中心=(28,100)` 可以一眼验证：x=28 = 药水中线，
y=100 = 图标下沿(56) + 间隙 12 + 半径 32。还有偏差就直接改 `potionIntentGapY` / `potionIntentScalePct`（热调）。

## 9. v0.2.9（当时误标 v0.3.0）：意图压到药水上重叠 + 「多了张不出的牌」

### 9.1 意图位置

用户 2026-10-26：「第三回合出的牌太多会把药水意图挡住，所以还是把药水的意图上移和药水本身重叠吧」
⇒ `potionIntentGapY` 默认 **12 → -60**，此时意图中心 y = 56 + (-60) + 32 = **28 = 药水图标中线**，
64px 的箭头直接压在 56px 的药水图标上，卡牌再多也挡不到。热调仍在 `charbosses_ui.txt`。

### 9.2 「第一回合出牌后多了张不出的牌」

日志实证（v0.2.8，13:05 那局第 1 回合）：

```
13:05:41 剧本手牌(6): [LEG_SWEEP,SURVIVOR,DECAY,PIERCING_WAIL,PRECISE_CUT,DEFEND_SILENT]
13:05:41 剧本第 1 回合: 打 [LEG_SWEEP,SURVIVOR,PIERCING_WAIL,PRECISE_CUT] 跳过 [DECAY,DEFEND_SILENT] 余费=0
13:05:44 喝药水：CLARITY（明晰提取物 = 抽 N 张牌）
13:05:45 hand ui: 7 card(s)          ← 手牌 6 → 7
```

机制：她的**计划是在玩家回合里就排好的**（这样你才能提前看到意图），而「明晰提取物」是
**她回合开始喝下去才抽牌**的 ⇒ 抽进来的牌天然不在计划里；这回合的费也按剧本花完（`余费=0`），
所以它只能压暗挂着、这回合不出。**不是面板残留**（打过一张就少一格，日志 7→6→4→3→2 全对得上）。

为了下次一眼确认是哪张、以及有没有重复，新增诊断（每次内容变化打一行）：

```
[CharBoss] 手牌栏：7 张里有 3 张本回合不会打 [DECAY,DEFEND_SILENT,XXX]（全部=[…]，计划 4 张；
           剧本没写 / 费不够 / 喝药抽进来的都算）
```

若 `全部=[…]` 里真出现两张 `LEG_SWEEP`，那就是另一个问题（重复发牌），日志会直接暴露出来。

## 10. v0.2.11（当时误标 v0.3.1）：「多了张不出的同名牌」= 明晰提取物抽到了她卡组的同名牌（已修）

v0.2.9 那条诊断直接给出了答案（15:08–15:10 那局）：

```
15:09:07 7 张里有 3 张本回合不会打 [DECAY,DEFEND_SILENT,LEG_SWEEP]
         全部=[LEG_SWEEP,SURVIVOR,PIERCING_WAIL,PRECISE_CUT,DECAY,DEFEND_SILENT,LEG_SWEEP]   ← 两张扫腿
15:10:33 5 张里有 1 张本回合不会打 [SURVIVOR]
         全部=[ALCHEMIZE,LEG_SWEEP,PRECISE_CUT,SURVIVOR,SURVIVOR]                             ← 两张生存者
```

**机制**（两处都对得上）：

1. 「剧本手牌」是 `BossScriptRunner.BuildHand` 里 **`combat.CreateCard(...)` 新建的卡实例**；
   她那副真实卡组（18 张）**一直原封不动躺在抽牌堆里**；
2. 第 1、5 回合的剧本都要喝**明晰提取物**，效果是「抽 {Cards} 张牌」⇒ 从抽牌堆顶抽走一张**真实卡牌**；
3. 抽牌堆顺序 = 卡组顺序 `LEG_SWEEP, SURVIVOR, SERPENT_FORM, …`
   ⇒ 第 1 次抽到 `LEG_SWEEP`、第 2 次抽到 `SURVIVOR` —— 正好是用户报的「一张打一张不打」那两张。

**修法**（`Core/BossPotions.DrinkAsync`）：喝药前给手牌拍快照，喝完把**这次新进手牌**的牌放回抽牌堆顶
（引用比较，见 `ContainsSame`）。于是「手牌 = 剧本」，药水不会再给她多出一张计划外的牌；
什么都没丢（那张牌回到牌堆顶）。日志：
`喝药水抽到的 <牌> 已放回抽牌堆（她这回合的手牌由剧本决定，不会多出永不参与的牌）`。

> 判据是「喝完之后手里多出来的牌」，不是药水类型 ⇒ 以后剧本要是喝「宇宙药剂 / 无色药水」也一样成立。


