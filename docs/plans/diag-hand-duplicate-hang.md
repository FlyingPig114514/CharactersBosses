# 诊断：观者局「骑士精英战卡死」——手牌重复插入 + 主线程停摆

> 状态：**已实现（v0.2.966）**。诊断专用，**不改任何游戏逻辑**。

## 1. 事故现场（用户 2026-11-26）

用户用观者（`Watcher` 0.10.20）在两局里各卡死一次，Windows 事件日志记的是
`AppHangB1`（19:48:33 / 19:55:23），不是崩溃退出。

两次的共同特征：

| 项 | 第一次 | 第二次 |
|---|---|---|
| 战斗 | `MECHA_KNIGHT_ELITE` | `KNIGHTS_ELITE`（同为第三幕骑士精英） |
| 模式 | 恐惧模式（铁甲战士的支配在场） | 同 |
| 死前最后一条引擎日志 | `Tried to add duplicate card CARD.WATCHER_MIRACLE (59626191) to hand! Holder already exists which has the same node. Ignoring.`（`NPlayerHand.Add_Patch2` ← `CardPileCmd.GetTweenForCardsChangingPiles`） | 同一条 |
| 之后 | 所有 mod 日志与我们每 120 帧一次的探针**同时停止** ⇒ 主线程被卡住 | 同 |

## 2. 反编译结论

`NPlayerHand.Add(NCard, int)` 里那段重复判定是**原版自己**打印的（`.refs\sts2_113.dll`）：

```csharp
foreach (var holder in Holders.Concat(_holdersAwaitingQueue)) {
    if (holder.CardNode == card)                         // 同一个节点
        { Log.Error($"Tried to add duplicate card {card.Model} …same node. Ignoring."); return holder; }
    if (holder.CardNode?.Model == card.Model)            // 同一张牌、不同节点
        { Log.Error($"…same backing model. Freeing the new node."); card.QueueFreeSafely(); return holder; }
}
```

也就是说：**有人把同一张「奇迹」重复往手里塞**。原版把第二次插入吞掉并返回旧 holder，
调用方（`CardPileCmd.GetTweenForCardsChangingPiles` 的补间回调）拿到 holder 后
**手里那张新节点并没有入场**，那条补间链此后再也没有推进 ⇒ 回合开始流程停在那里。

扫了 144 个 mod DLL：**只有观者 mod 认识 `WatcherMiracle` / `WATCHER_MIRACLE`**，
而它的清单写 `min_game_version: 0.113.0`，游戏已经是 **0.114.0**。

## 3. 这次加的两个诊断（都只写日志）

### 3.1 `Patches/HandDuplicateDiagPatch.cs` —— 抓「谁在塞」

挂在 `NPlayerHand.Add` 的 **Prefix**：

- 先按原版同一套判据预判这次会不会命中重复分支（同节点 / 同模型）；
- **只有会命中时**才打印，并附上**完整调用栈**（含方法名 + 程序集名）；
- 同时记录现有 `Holders` 数量、`_holdersAwaitingQueue` 数量；
- 限流 30 条（正常抽牌一张都不打）；
- 最后一条记录留在内存里，供停摆报告引用。

### 3.2 `Core/StallWatchdog.cs` —— 卡死时把现场落盘

- 心跳源：**`SceneTree.process_frame`**（`EnsureHooked()`，启动时由 `BuildInfo.LogOnce` 调用一次，
  之后 `BossFrameTick.Tick` 里也会补挂）。每帧只写一个 `long`，零分配。
  > ⚠ 实测踩过：一开始只靠 `BossFrameTick.Tick` 当心跳，可那个 Tick 是「场上存在带星/带能量球/带刀的节点」
  > 才被调到的 —— **主菜单、读档、奖励界面根本没有这些节点**，于是菜单挂机两分钟就被误报成「主线程停摆」
  > （实机 20:30:35 / 20:36:09 两条假报告）。改挂 `process_frame` 后，菜单挂机 70 秒零误报。
  > 另外后台线程**只在心跳确实挂上 `process_frame` 后才启动**，避免用不可靠的心跳判断卡死。
- 后台线程每秒检查：心跳停了 **≥12 秒**且尚未报告过 ⇒ 往
  `%APPDATA%\SlayTheSpire2\logs\characterbosses_stall.txt` 追加一份报告
  （`FileOptions.WriteThrough`，进程被强杀也留得住）：
  - 停了多久、累计帧数、进程 id、线程数、GC 堆大小、进程已跑时长；
  - 最近 40 条 `Mark()`（本次先在「手牌重复插入」处埋点）；
  - **最后一次手牌重复插入的完整调用栈**。
- 心跳恢复（<3 秒）后自动复位，下一次卡死会再写一份。

## 4. 复现后怎么读

1. 打开 `characterbosses_stall.txt`：如果它在卡死那一刻写了报告 ⇒ 确认是「主线程停住」，
   时间点与 `mod_log.txt` 最后一行精确对得上；
2. 报告里的调用栈直接点名**是哪个 mod / 哪个方法在重复往手里塞奇迹**；
3. 拿到点名结果后再决定是给观者 mod 提 issue、还是在我们这边加兼容补丁
   （例如：命中重复时让那条补间自己收尾，不要留一张永远不入场的牌）。

## 5. 不做的事

- 不改任何战斗逻辑、不改 `NPlayerHand.Add` 的返回值、不拦原版行为；
- 不打印正常抽牌的调用栈（只在命中重复时打印，且限流 30 条）。
