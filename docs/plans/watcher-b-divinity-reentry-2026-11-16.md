# v0.2.709：B 版神格「已在神格不再进」真凶 = 反射重载歧义 + 说明框层级

> 写于 2026-11-16 傍晚（第六轮）。用户实测两条，本文件记录根因、改法、验证点。
> 上一版 v0.2.708 的「补跑 OnEnterStance」**已实机验证通过**（见 §1），本版修的是它之后暴露出来的两件事。

## 1. v0.2.708 已验证通过（先钉死，别再动）

```
[17:30:29] 进神格：已补跑 OnEnterStance（能量 3 → 6，神格里=True）
[17:30:29] 虔信：真言满 10 ⇒ … 能量 3 → 6，神格里=True
[17:31:19] 进神格：已补跑 OnEnterStance（能量 2 → 5，神格里=True）
```

⇒ `SetStanceSilently` 之后补跑 `DivinityStance.OnEnterStance`（第一句就是
`player.PlayerCombatState.GainEnergy(3m)`）确实把那 +3 拿到了。**这条不要再改。**

## 2. 用户第六轮第一条：「她已在神格时再满 10 层真言，不退姿态也不再加费」

日志原文（同一场）：

```
[17:31:31] 进神格：StanceCmd.EnterDivinity 之后姿态模型 = DivinityStance
[17:31:31] 虔信：真言满 10 ⇒ ① 退出姿态（本来在平静=False…）… ⇒ 能量 2 → 2，神格里=True
```

### 2.1 真凶：`WatcherCards.Method` 取不到重载，静默返回 null

B 版（lamali）`Watcher.Code.Commands.StanceCmd` 里
`EnterDivinity` / `ExitStance` / `EnterCalm` **各有两份重载**：

```csharp
EnterDivinity(PlayerChoiceContext ctx, Player player, CardModel? src)
EnterDivinity(PlayerChoiceContext ctx, IEnumerable<Player> players, CardModel? src)
```

旧工具用 `t.GetMethod(name, flags)` → 歧义 ⇒ 抛 `AmbiguousMatchException` ⇒ 被 catch 吞 ⇒ 返回 `null`
⇒ 调用点 `m?.Invoke(...)` **什么都不做**。

三次日志的「之后」都等于「之前」，正是「这一步从来没执行过」的铁证：

| 时刻 | 之后姿态模型 | 她本来是 |
|---|---|---|
| 17:30:29（第一次满 10） | `NoStance` | 无姿态 |
| 17:31:19（第二次满 10） | `CalmStance` | 平静 |
| 17:31:31（第三次满 10） | `DivinityStance` | 神格 |

连带后果：
- **「退姿态」一次都没退过** ⇒ 离开平静的 +2 能量也一次都没给过（用户 v0.2.683 的口径是「让那 2 费正常加」）；
- 「进神格」全靠 v0.2.707 的 silent 强制 + v0.2.708 的补跑才成立；
- 她**本来就在神格**时，`EnterDivinityCoreAsync` 看到 `after.Contains("Divinity")` 直接跳过兜底
  ⇒ 既不退也不进 ⇒ `能量 2 → 2`（正是用户报的那条）。

### 2.2 改法（`src/Hooks/Core/WatcherCards.cs`）

1. 新增 `Overload(type, name, Type[]? parameterTypes)`：先按**参数类型**精确取；
   取不到再按名字（旧的 `Method` 行为）；还有歧义就自己挑「第二个参数是 `Player`」那条，
   并打一行 `反射消歧：StanceCmd.EnterDivinity 有多个重载 ⇒ 取 (PlayerChoiceContext, Player, CardModel)`。
   `StanceCmd.EnterDivinity / ExitStance / EnterCalm` 三处都改成写全参数类型。
2. `ExitStanceAsync` 改成 **真 await**（以前直接 `return task` 拿不到结果）→ 读完打
   `退出姿态：平静 → 无姿态`；如果姿态还在，就用 `SetStanceSilently<NoStance>` 强制清（兜底）。
3. `EnterDivinityCoreAsync`（B 版）进来先看姿态：**已经在神格就先 silent 退掉再进**
   （保证 `SetStance` 里那句「已经是这个姿态就 return」不会吃掉这次进入）。
4. 两处 ctx 调用都单独 try/catch：`WatcherHook.OnStanceChange` 之类的收尾若抛异常，
   姿态本身已经设上了（`ActiveStance` 的赋值在 `OnEnterStance` 之前），不能因此跳过兜底。
5. `InCalm` 也改问姿态模型（B 版姿态不是 power）—— 日志里 `本来在平静=False`
   而同秒姿态模型是 `CalmStance` 就是这么来的。

### 2.3 怎么验证

```
Select-String -Path "$env:APPDATA\SlayTheSpire2\logs\mod_log.txt" -Pattern "退出姿态|进神格|虔信：真言满|反射消歧"
```

期望：
- 第一次满 10：`退出姿态：无姿态 → 无姿态` + `能量 N → N+3`；
- **再一次**满 10（她已在神格）：`退出姿态：神格 → 无姿态`（或 `进神格：她本来就在神格 ⇒ 先退出姿态再重新进`）
  + `能量 N → N+3` + 力量再 +2；
- 从平静进神格：`退出姿态：平静 → 无姿态` 并且 `能量 N → N+5`（平静 2 + 神格 3）。

## 3. 用户第六轮第二条：「特殊效果描述的层级不对，切到地图页面还能看见」

### 3.1 根因

说明框挂在该死的**全局**提示层（`NGame.HoverTipsContainer`），而以前的收框判据只有
`NCombatRoom.Instance == null`。可**战斗中打开地图 / 图例 / 牌堆详情时战斗房还活着**
⇒ 判据为「在战斗里」⇒ 放行，于是这块框压在那些界面上面（截图里就是压在「图例」上）。

### 3.2 改法

新增 `BossSpecialTip.CombatScreenActive()`，四条一起判（任一不满足就收）：

1. 有战斗房且实例有效；
2. `NOverlayStack.Instance.ScreenCount == 0`（地图 / 牌堆 / 选牌 / 图例都走浮层栈）；
3. `NMapScreen.Instance.IsOpen == false`；
4. `ActiveScreenContext.Instance.IsCurrent(room)`（`NCombatRoom` 自己用的也是这条判据）。

`BossSpecialTip.Ensure` 与 `BossSpecialTipRoomGuardPatch`（每帧挂在 `NHoverTipSet._Process` 上）
都改用这一条判据；守卫那一行日志也从「已离开战斗房」改成
「已离开战斗屏（或压着别的界面）」。判据本身失败时打一行限流日志说明是哪一条不满足
（`特殊效果说明框：当前不该显示（有 1 层浮层压着…）⇒ 收掉`），下次一眼定位。

## 4. 本版涉及文件

| 作用 | 文件 |
|---|---|
| 重载消歧 + 退/进姿态 | `src/Hooks/Core/WatcherCards.cs` |
| 说明框显示判据 | `src/Hooks/UI/BossSpecialTip.cs` |
| 每帧收框守卫 | `src/Hooks/Patches/BossSpecialTipRoomGuardPatch.cs` |
| 版本号 | `src/CharacterBosses.json`（`0.2.709`）、`src/Hooks/Core/BuildInfo.cs`（`Tag`） |
