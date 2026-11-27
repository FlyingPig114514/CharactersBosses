# Bug 报告：统帅模式下宝箱遗物投票记到了 1 号位（可直接转给作者 穷仔）

> 对象：`AiTeammate`（穷仔的尖塔搭子）0.2.1，游戏 v0.114.0
> 复现者：飞猪（存档 `ai_commander_run`，统帅模式，本机操控 2 席以上）
> 我方临时兼容：CharacterBosses v0.2.970（`Compat/AiTeammateTreasureCompat.cs`），作者修好后可作废

## 一、现象

统帅模式（一台机器操控多个座位、按键/侧键切视角）进宝箱房：

1. **非 1 号位选遗物 ⇒ 1 号位跟着被记成"已选"**；
2. 切回别的座位再看，**只剩 1 号位是已选状态**，其他座位从未投过票；
3. 同一轮里反复点不同座位，1 号位的票被反复覆盖 ⇒ 票永远凑不齐，**宝箱看起来就是卡住**。

## 二、证据（`godot.log`，Debug 日志级别）

```
[DEBUG] [TreasureRoomRelicSynchronizer] Relic index 1 (RELIC.HORN_CLEAT (23969939)) is being picked by local player 1
[DEBUG] [ActionQueueSynchronizer] Sending message to clients to enqueue action NetPickRelicAction for player 1 index 1 round 0
[DEBUG] [TreasureRoomRelicSynchronizer] Player MegaCrit.Sts2.Core.Entities.Players.Player picked relic at index 1: RELIC.HORN_CLEAT (23969939)
[Commander] perspective=101; transport=1                      ← 视角此时已经切到 101 号座位
[DEBUG] [TreasureRoomRelicSynchronizer] Relic index 0 (RELIC.VAJRA (50380041)) is being picked by local player 1
[DEBUG] [ActionQueueSynchronizer] Sending message to clients to enqueue action NetPickRelicAction for player 1 index 0 round 0
```

两次不同座位的点击，`NetPickRelicAction` 里**都是 `for player 1`**。

## 三、根因

原版 `MegaCrit.Sts2.Core.Multiplayer.Game.TreasureRoomRelicSynchronizer.PickRelicLocally`（0.114.0 反编译）：

```csharp
public void PickRelicLocally(int? index)
{
    if (_currentRelics == null) { _logger.Warn("Attempted to pick relic while relic picking is not active!"); return; }

    _predictedVote = new PlayerVote { index = index, voteReceived = true };
    _actionQueueSynchronizer.RequestEnqueue(new PickRelicAction(LocalPlayer, index, _currentRound));   // ← 写死 LocalPlayer
    VotesChanged?.Invoke();
}

public void SkipRelicLocally() => PickRelicLocally(null);
```

`LocalPlayer` 是**本机玩家**（不是"当前视角的座位"）。统帅模式只切换了**视角/输入归属**，
没有接管这一处，所以投票永远记在 1 号位。

## 四、建议修法（改一处即可，遗物投票 + 放弃选遗物都覆盖）

在 `CommanderMode.Active` 且「当前操控的座位 ≠ 本机玩家」时，用**当前操控的座位**重建这一票。
顺序必须与上面原版一致（`RequestEnqueue` → 预测票 → `VotesChanged`）：

```csharp
[HarmonyPatch(typeof(TreasureRoomRelicSynchronizer), nameof(TreasureRoomRelicSynchronizer.PickRelicLocally))]
internal static class CommanderRelicPickPatch
{
    [HarmonyPrefix]
    static bool Prefix(TreasureRoomRelicSynchronizer __instance, int? index)
    {
        if (!CommanderMode.Active) return true;
        var seat = CommanderMode.Selected;                       // 当前操控的座位
        var local = AccessTools.Property(typeof(TreasureRoomRelicSynchronizer), "LocalPlayer")
                              .GetValue(__instance) as Player;   // internal 属性 ⇒ 反射
        if (seat == null || (local != null && seat.NetId == local.NetId)) return true;

        var relics = AccessTools.Field(typeof(TreasureRoomRelicSynchronizer), "_currentRelics")
                               .GetValue(__instance) as IList;
        if (relics == null) return true;                         // 与原生一致：投票没开始就早退
        int round = (int)AccessTools.Field(typeof(TreasureRoomRelicSynchronizer), "_currentRound").GetValue(__instance);

        var aqs = (ActionQueueSynchronizer)AccessTools.Field(typeof(TreasureRoomRelicSynchronizer), "_actionQueueSynchronizer")
                                              .GetValue(__instance);
        aqs.RequestEnqueue(new PickRelicAction(seat, index, round));

        var voteType = typeof(TreasureRoomRelicSynchronizer).GetNestedType("PlayerVote", BindingFlags.Public | BindingFlags.NonPublic);
        var vote = Activator.CreateInstance(voteType);
        voteType.GetField("index").SetValue(vote, index);
        voteType.GetField("voteReceived").SetValue(vote, true);
        AccessTools.Field(typeof(TreasureRoomRelicSynchronizer), "_predictedVote").SetValue(__instance, vote);

        (AccessTools.Field(typeof(TreasureRoomRelicSynchronizer), "VotesChanged").GetValue(__instance) as Action)?.Invoke();
        return false;
    }
}
```

要点：

- **别改 `_localPlayerId` 再调原方法** —— 那会顺手改掉同帧里别处读 `LocalPlayer` 的含义，不建议；
- `SkipRelicLocally()` 原版就是 `PickRelicLocally(null)`，**不用另外挂钩子**；
- 预测票（`_predictedVote`）与 `VotesChanged` 一定要补上，否则宝箱界面的"已选"高亮不会刷新；
- 只有在「统帅模式 + 当前座位不是本机玩家」时才接管，单人 / 普通联机零影响。

## 五、我方兼容（可选参考）

`CharacterBosses` v0.2.970 里已经有一份等价实现：`src/Hooks/Compat/AiTeammateTreasureCompat.cs`
（反射 `AiTeammate.CommanderMode.Active/Selected`，只在需要时接住 `PickRelicLocally`）。
作者在 `AiTeammate` 内修好后，我们这份会自动让位（它只在「票要记到别人名下」时接管，两边都改也只是重复设同一个值）。

日志验证点：装好之后进宝箱房，`mod_log.txt` 里应出现

```
[CharBoss] 尖塔搭子兼容：宝箱那一票记在**当前操控的座位** <名字>（index=N，轮次 0；原版会记在 1 号位）
```

而 `godot.log` 里 `NetPickRelicAction for player 1` 应变成对应座位的 NetId。
