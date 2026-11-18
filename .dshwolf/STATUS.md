# CharacterBosses — 交接状态

> 更新：2026-11-19（v0.2.803 **已装机 + 已打包 + 已上传工坊 + 已推 GitHub**）

## 🚀 Next phase

> ### ✅ v0.2.803 已发布
>
> · 装机：两份 Impl 都是 `v0.2.803`（`[verify] … OK`），PCK 重建成功；
> · 打包：`.dist\CharacterBosses-v0.2.803.zip`（17.5 MB）；
> · 工坊 item **3830639796** 上传成功（`Successfully uploaded` = `SubmitItemUpdateResult_t.m_eResult == k_EResultOK`），changeNote 已更新；
> · GitHub `2fb865f..49ff360` 已推。
>
> ### 本版两件事（对应玩家 ATNAS 在工坊的两条反馈）
> 1. **「恐惧模式联机，好友吃不到支配效果而导致不同步」——已修**。
>    反编译 `DreadRunModel.BeforeRoomEntered` 是支配进战斗状态的**唯一一处**
>    （普通战斗房 `CombatState.SetDominion(RunState.Dominion)`；Boss 房两处都设 null），
>    而战斗里挂 power 的 `AfterEnemiesEnteredCombat` / `AfterPlayersEnteredCombat` 用的是 **CombatState 那一份**。
>    客人的「本幕支配以房主为准」经常**晚于**那次拷贝 ⇒ 以前只对齐 `RunState` ⇒ 客人整场没有支配 ⇒ 校验和分叉。
>    新增 `DreadDominionBeforeRoomPatch`：前缀先对齐 RunState；**后缀必须先重新 `EnsureCurrentAct` 再同步 CombatState**
>    （原版方法体里的 `ApplyCurrentDominion()` 会按本机槽位重设一次，顺序反了等于没修）。
>    `EnsureCurrentAct` 末尾也顺手同步一次（房主结论晚到的场次）。
> 2. **「极速模式联机远端 boss 未替换」——我们从来没处理过极速(Blitz)模式**（全仓只有 `GameMode.Dread`）。
>    反编译确认极速模式的 `GenerateRooms(..., isBlitz)` 只改弱敌数量，Boss 槽仍是 `_rooms.Boss`，
>    能挡掉替换的只有「地图替换开关（未知 map id 默认不替换）」或「客人还没收到房主结论」两条。
>    本版加了 `Boss 替换判定` 一行诊断（模式/幕/本机/结果/地图 id 与开关/存档槽/房主结论，每个 (幕,场,模式) 一次）
>    —— **需要玩家日志才能定案**，暂未改判据。
>
> ### 仍需实机验证（**全员必须 0.2.803**）
> · 恐惧模式联机：客人应出现 `恐惧模式·支配对齐：**战斗状态**里的支配 X → Y（进房间之后核对战斗状态）`；
>   两端普通房间里敌人身上的支配 power 应一致，不再出现 `StateDivergence`。
> · 3/4 人局 Boss 房：`多 Boss 站位（3 只）`，房主 `勾选同步：房主记下第 X 层第 Y 场的整条阵容 = [...]`，
>   客人 `第 X 层第 Y 场阵容：**按房主广播的整条阵容**`。
> · 极速模式：请玩家把 `Boss 替换判定` 那行发出来（`模式=Blitz`），据此决定是放宽地图开关判据还是补房主结论同步。
> · 遗留：B 版神格观「已在神格再满 10 层」、常驻说明框收框日志。

## 环境与硬规矩（沿用）

1. **不要擅自关用户的游戏**；装机前先 `Get-Process SlayTheSpire2`，占用就只编译等他说。
2. 构建用 `.\tools\dotnet\dotnet.exe build src\CharacterBosses.csproj -c Release -p:Sts2Target=111`（113 同理），**两个目标都要 0 错误**。
   ⚠ PATH 里的 `dotnet build` 会说「No .NET SDKs were found」。
3. 改版本要同时改 `src/CharacterBosses.json` 的 `version` 和 `src/Hooks/Core/BuildInfo.cs` 的 `Tag`。
   ⚠ `src/CharacterBosses.json` **带 UTF-8 BOM**：`edit` 工具会弄丢（`git diff` 多一行 `-﻿{`），
   改完用 `[System.IO.File]::WriteAllText(..., UTF8Encoding($true))` 补回。改 `.ps1` 同理。
4. 发布五步：`install-mod.bat "<游戏路径>"` → `edit workshop\changenote.txt` + `tools\mk-workshop-json.ps1` →
   `tools\pack-dist.ps1` → `tools\upload-workshop.ps1` → `git commit` + `git push`。
5. **这台机器上 agent 拉不起游戏** ⇒ 请用户自己启动。
6. 反编译 B 版：`D:\SteamLibrary\steamapps\workshop\content\2868840\3747492505\Watcher.dll`；A 版 `3747526116`；本体 `.refs\sts2_113.dll`（113）/`.refs\sts2_111.dll`（111）。
7. 观者只启用 **3747492505（lamali，B 版）**；两个都启用时 `Probe()` 优先 A 版。
8. ⚠ 工作区外、`workspace-write` 沙箱下会失败、需 `danger-full-access` 重跑**同一条**命令：`install-mod.bat`、`git push`。
   `pack-dist.ps1` / `mk-workshop-json.ps1` 只写工作区内；`upload-workshop.ps1` 直接跑就行（Steam 已登录）。
9. **判上传成功只看** `Successfully uploaded` 那行（`UploadCommand.cs:301-320`：只有 `m_eResult == k_EResultOK` 才打印它）。
   `k_EItemUpdateStatusInvalid` 只是进度状态终止值，不代表失败；紧跟一次成功提交后再跑会撞
   `k_EResultFileNotFound`（staging 还没清），**再跑一次即可**。
   `steamapps\workshop\appworkshop_2868840.acf` 的 `timeupdated` 和本机缓存里的版本号是**订阅侧**记录，
   发布者上传后不立即变（实测 0.2.802/0.2.803 都 OK 了它仍停在 0.2.800 那次），**不能用来验证**。
10. 判「联机不同步」最快路径：`tools\compare-mp-logs.ps1 -LogA 房主 -LogB 客人`；分叉包
    `ritsulib_state_divergence_*.zip` 里 `state-divergence-report.txt`，只筛 `local=` 与 `remote=` 不相等的行。

## 其它在手事项

- 工坊介绍里的观者 mod 名字按用户原文写成 **Boninall / Lamall**（工坊作者实际是 `Boninall` / `lamali`）—— 若要改成 lamali 需再传一次。
- 更早遗留待实机验证：蛇眼猎整场、养手骨无界命匣召唤、事件机谋杀加伤、情感芯片球被动翻倍。
