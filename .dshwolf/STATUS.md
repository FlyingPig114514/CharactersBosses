# CharacterBosses — 交接状态

> 更新：2026-11-16 深夜（v0.2.711 已装机，等实机/联机验证）

## 🚀 Next phase

> ### ⏳ v0.2.711 已装机（`[verify] 两份实现的版本串都是 v0.2.711 OK`）
>
> 本版三件（细节见 `docs/plans/desync-2026-11-16-mp-act3-second-boss.md`）：
>
> **① 多人「每场最终遭遇」广播（用户拍板「广播」）**：`BossPicksMessage` 改双向，
> 房主带 `Bosses`（`幕:场|KEY` / `幕:场|VANILLA`），客人无条件采用；
> 新客人首次上报时强制重播一次；换局 `ResetForNewRun()` 清同步状态。
> 关掉「客人没收到勾选 / 地图开关默认值（模组图默认不替换）/ 候补表不同」三类分叉。
>
> **② 观者只在「全端同一实现」时出场（用户拍板）**：`WatcherCards.ProviderId`（boninall/lamali/空）
> 由客人上报，房主要「玩家数−1 份且每份都跟自己一样」才把观者放进三层候补表；
> 客人用房主广播的 `wok=`，没收到就不放。`BossKeys.WatcherRoster()` 已接闸门。
>
> **③ 顺手修的真 bug**：`MultiBossPlan.WantsSecondBoss` 与 `BossLineups.PlayerCount()` 口径统一
> （前者读不到 RunState 时按 1 人算 ⇒ 「阵容按 2 只排、伙伴一只都不加」）。
> 另加 `※阵容输入※` 一行诊断（候补表条数/含观者/勾选是否收到/地图开关/指纹），两份日志一比即可定位。
>
> **⚠ 协议变了 ⇒ 多人两端必须同一版本。** 装机只到本机，工坊仍需重新打包上传玩家才拿得到。
>
> **要看的日志**：
> ```
> Select-String -Path "$env:APPDATA\SlayTheSpire2\logs\mod_log.txt" -Pattern "※阵容输入※|客人端：|观者 Boss：|勾选同步：|※多 Boss 生成指纹※|拉 Boss 房间"
> ```
> 联机期望：房主 `观者 Boss：房主判定「可以出现」=True/False（…期待 N 份、已收到 N 份）`；
> 客人 `观者 Boss：客人已上报本机观者实现（w=lamali）`、`客人端：第 3 幕第二场按**房主的结论**替换 ⇒ …`；
> 两端 `※阵容输入※` 与 `※多 Boss 生成指纹※` 必须逐字一致。

## ⏳ 上一版（v0.2.709）未实机验证的条目

- B 版神格观「已在神格再满 10 层」：期望 `退出姿态：神格 → 无姿态`（或 `进神格：她本来就在神格 ⇒ 先退出姿态再重新进`）
  + 能量再 +3 + 力量再 +2；从平静进神格 `能量 N → N+5`。
- 说明框：打开地图/图例/浮层时应出现 `已离开战斗屏（或压着别的界面）⇒ 收掉常驻特殊效果说明框`。
- ✅ 已钉死：v0.2.708 的「`SetStanceSilently` 之后补跑 `OnEnterStance`」已实机验证通过。

## 环境与硬规矩（沿用）

1. **不要擅自关用户的游戏**；装机前先 `Get-Process SlayTheSpire2`，占用就只编译等他说。
2. 构建用 `.\tools\dotnet\dotnet.exe build src\CharacterBosses.csproj -c Release -p:Sts2Target=111`（107 同理），**两个目标都要 0 错误**。
   ⚠ 107 里没有 `WatcherModPresence` / `WatcherCards`（整段在 `#if STS2_111`）⇒ 碰它们要加宏保护（实测踩过）。
3. 改版本要同时改 `src/CharacterBosses.json` 的 `version` 和 `src/Hooks/Core/BuildInfo.cs` 的 `Tag`。
   ⚠ `src/CharacterBosses.json` 里是 GBK 乱码状的非 ASCII 内容 —— 改版本时用**字节级 ASCII 替换**，别整文件重写。
4. 装机：`cmd /c 'install-mod.bat "D:\SteamLibrary\steamapps\common\Slay the Spire 2"'`（要 `danger-full-access`）。
5. **这台机器上 agent 拉不起游戏**：`steam://run` 与 `steam.exe -applaunch` 都无反应（`content_log.txt` 无新记录），
   直接跑 `SlayTheSpire2.exe` 会 `No appID found` ⇒ Steamworks 失败 ⇒ 创意工坊 mod 全不加载。**请用户自己启动游戏。**
6. 反编译 B 版：`D:\SteamLibrary\steamapps\workshop\content\2868840\3747492505\Watcher.dll`；A 版 `3747526116`；本体 `.refs\sts2_111.dll`
7. 观者只启用 **3747492505（lamali，B 版）**；两个都启用时 `Probe()` 优先 A 版。

## 其它在手事项

- 工坊上传：`.dist` 与 `workshop\` 需要重新打包/上传到 0.2.711（用户开口再跑 `tools\upload-workshop.ps1`）；玩家报了联机 bug，得等这版上传后才能复测。
- 更早遗留待实机验证：蛇眼猎整场、养手骨无界命匣召唤、事件机谋杀加伤、情感芯片球被动翻倍。
