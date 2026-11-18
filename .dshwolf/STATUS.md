# CharacterBosses — 交接状态

> 更新：2026-11-19（v0.2.804 **已装机 + 已打包 + 已上传工坊 + 已推 GitHub**）

## 🚀 Next phase

> ### ✅ v0.2.804 已发布
>
> · 装机：两份 Impl 都是 `v0.2.804`；打包 `.dist\CharacterBosses-v0.2.804.zip`（18,351,678 B）；
> · 工坊 item **3830639796** 上传成功（第一次 `k_EResultFail` ⇒ sleep 8s 重跑 ⇒ `Successfully uploaded` / `UPLOAD OK`）；
> · GitHub `b61abc9..9f7c256` 已推。
>
> ### 本版：多人「Boss 少一只」（3 人局只出 2 只）
> 证据（房主 `mod_log (43)`，同一局来回跳）：`[23:42:23] RunState 有 3 人，其中 1 人已经 30 秒没说话 ⇒ 按 2 只`
> → `[23:42:41] 0 人没说话 ⇒ 按 3 只` → `[23:43:11] 又按 2 只`，而那位「没说话」的玩家 23:44 才因别的原因掉线。
> 根因：`LivePlayerCount` 靠 `BossPicksMessage` 心跳猜活人（客人每 5 秒才发一次，断一下就被判死）。
> 修法：优先读**游戏自己维护的大厅玩家列表** `RunManager.RunLobby.Players`
> （`RunLobby.HandlePlayerLeftMessage` / `PlayerRejoined` 会更新），只有不在大厅里的才算离开；
> 拿不到列表才退回心跳判活，阈值 30 秒 → **120 秒**。配合 v0.2.802「伙伴数量 = 阵容条数」双保险。
>
> ### 仍需实机验证（**全员必须 0.2.804**）
> · 3/4 人局 Boss 房：`多 Boss 站位（3 只）`；房主不再出现「其中 N 人已经 30 秒没说话」（真有人退时才会出现，
>   且应写成 `但大厅里只剩 N 人（M 人已不在大厅）`）。
> · 恐惧模式联机：客人应出现 `恐惧模式·支配对齐：**战斗状态**里的支配 X → Y`，不再 `StateDivergence`。
> · 极速(Blitz)模式：等玩家把 `Boss 替换判定` 那行发来再定判据（该模式我们从未实测）。
> · 遗留：B 版神格观「已在神格再满 10 层」、常驻说明框收框日志。

## 环境与硬规矩（沿用）

1. **不要擅自关用户的游戏**；装机前先 `Get-Process SlayTheSpire2`，占用就只编译等他说。
2. 构建用 `.\tools\dotnet\dotnet.exe build src\CharacterBosses.csproj -c Release -p:Sts2Target=111`（113 同理），**两个目标都要 0 错误**。
   ⚠ PATH 里的 `dotnet build` 会说「No .NET SDKs were found」。
3. 改版本要同时改 `src/CharacterBosses.json` 的 `version` 和 `src/Hooks/Core/BuildInfo.cs` 的 `Tag`。
   ⚠ `src/CharacterBosses.json` **带 UTF-8 BOM**：`edit` 工具会弄丢，改完用
   `[System.IO.File]::WriteAllText(..., UTF8Encoding($true))` 补回（`.ps1` 同理）。
4. 发布五步：`install-mod.bat "<游戏路径>"` → `edit workshop\changenote.txt` + `tools\mk-workshop-json.ps1` →
   `tools\pack-dist.ps1` → `tools\upload-workshop.ps1` → `git commit` + `git push`。
5. **这台机器上 agent 拉不起游戏** ⇒ 请用户自己启动。
6. 反编译：`.refs\sts2_113.dll`（113）/`.refs\sts2_111.dll`（111），工具 `tools\ilspy-cli`（见 `docs/plans/recon-tooling.md`）。
7. 观者只启用 **3747492505（lamali，B 版）**；两个都启用时 `Probe()` 优先 A 版。
8. ⚠ 工作区外、`workspace-write` 沙箱下会失败、需 `danger-full-access` 重跑**同一条**命令：`install-mod.bat`、`git push`。
9. **上传重试**：`k_EResultFileNotFound` / `k_EResultFail` 都是瞬时失败（上一次刚提交 / Steam 没缓过来），
   **等 8~10 秒重跑同一条命令即可**；判成功只看 `Successfully uploaded`（`UploadCommand.cs:301-320`）。
   `k_EItemUpdateStatusInvalid` 只是进度终止值；`appworkshop_2868840.acf` 的 `timeupdated` 与本机缓存版本号
   属**订阅侧**记录，发布后不立即变，**不能用来验证**。
10. 判「联机不同步」最快路径：`tools\compare-mp-logs.ps1 -LogA 房主 -LogB 客人`；分叉包
    `ritsulib_state_divergence_*.zip` 里 `state-divergence-report.txt`，只筛 `local=` 与 `remote=` 不相等的行。

## 其它在手事项

- 工坊介绍里的观者 mod 名字按用户原文写成 **Boninall / Lamall**（工坊作者实际是 `Boninall` / `lamali`）。
- 更早遗留待实机验证：蛇眼猎整场、养手骨无界命匣召唤、事件机谋杀加伤、情感芯片球被动翻倍。
