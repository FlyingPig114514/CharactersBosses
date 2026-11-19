# CharacterBosses — 交接状态

> 更新：2026-11-19（v0.2.805 **已装机 + 已打包 + 已上传工坊 + 已推 GitHub**）

## 🚀 Next phase

> ### ✅ v0.2.805 已发布（手机端性能）
>
> · 装机：两份 Impl 都是 `v0.2.805`；打包 `.dist\CharacterBosses-v0.2.805.zip`（18,358,403 B）；
> · 工坊 item **3830639796** 一次上传成功（`Successfully uploaded` / `UPLOAD OK`），changeNote 已更新；
> · GitHub `ce35890..7bfaa70` 已推。
>
> ### 本版四处（对玩家「季节」的「手机版四人联机卡顿明显，耗电也明显」）
> 结论：**不是逻辑慢** —— 实测帧回调 avg=0.05~0.08ms（一帧预算的 0.5%）。真开销是**日志 I/O**：
> 一局 3h40m 写了 **203,646 行 / 27.8MB**（≈15 行/秒），而 `SafeLogPatch` 以前**每行**都
> `Directory.CreateDirectory` + 新建 `FileStream` + 关闭（20 万次建目录/开关文件）。
> 1. `SafeLogPatch` 改**长开文件 + 缓冲写**：攒 32 行或 0.5s 才 flush；带 `⚠`/失败/不同步 的关键行
>    仍**立刻 flush**；每 5 分钟换句柄；帧驱动 `FlushIfDue()` 兜底。
> 2. 新增 `Core.LogLoudness`：**手机（Android/iOS）上纯诊断行一律不打**（帧耗时 / 按钮探针 / 回合结束计时 /
>    性能采样 / 逐次效果追踪 / 重复的观者探测行），`帧回调耗时` 只在**单帧 >20ms** 时留一行；**PC 行为不变**。
> 3. **进 Boss 房尖峰分帧**：那一帧以前同帧重建所有 Boss 面板（实测 **131ms**，常态 0.05ms）⇒ 排队、每帧 1 只。
> 4. `BossHandUi.RefreshForce` **同帧去重**（一次出牌里多处喊它，4 只 Boss 会叠）。
>
> ### 仍需实机验证
> · 手机端：日志量应显著下降（`帧回调耗时` 不再刷屏，只在卡顿时出现一行）；四人联机的卡顿/耗电应改善。
> · PC 端：日志行为与以前一致（纯诊断行照旧），若发现**PC 上也缺了**某类诊断行 ⇒ 检查 `LogLoudness` 的 `IsMobile` 分支。
> · 3/4 人局 Boss 房 `多 Boss 站位（3 只）`（v0.2.804 的人数判据改动仍需实测）。
> · 恐惧模式联机：客人应出现 `恐惧模式·支配对齐：**战斗状态**里的支配 X → Y`（v0.2.803 修复）。
> · 极速(Blitz)模式：等玩家把 `Boss 替换判定` 那行发来再定判据（该模式我们从未实测）。
> · 遗留：B 版神格观「已在神格再满 10 层」、常驻说明框收框日志。

## 环境与硬规矩（沿用）

1. **不要擅自关用户的游戏**；装机前先 `Get-Process SlayTheSpire2`，占用就只编译等他说。
2. 构建用 `.\tools\dotnet\dotnet.exe build src\CharacterBosses.csproj -c Release -p:Sts2Target=111`（113 同理），**两个目标都要 0 错误**。
   ⚠ PATH 里的 `dotnet build` 会说「No .NET SDKs were found」。
3. 改版本要同时改 `src/CharacterBosses.json` 的 `version` 和 `src/Hooks/Core/BuildInfo.cs` 的 `Tag`。
   ⚠ `src/CharacterBosses.json` **带 UTF-8 BOM**：`edit` 会弄丢，改完用
   `[System.IO.File]::WriteAllText(..., UTF8Encoding($true))` 补回（`.ps1` 同理）。
4. 发布五步：`install-mod.bat "<游戏路径>"` → `edit workshop\changenote.txt` + `tools\mk-workshop-json.ps1` →
   `tools\pack-dist.ps1` → `tools\upload-workshop.ps1` → `git commit` + `git push`。
5. **这台机器上 agent 拉不起游戏** ⇒ 请用户自己启动。
6. 反编译：`.refs\sts2_113.dll`（113）/`.refs\sts2_111.dll`（111），工具 `tools\ilspy-cli`（见 `docs/plans/recon-tooling.md`）。
7. 观者只启用 **3747492505（lamali，B 版）**；两个都启用时 `Probe()` 优先 A 版。
8. ⚠ 工作区外、`workspace-write` 沙箱下会失败、需 `danger-full-access` 重跑**同一条**命令：`install-mod.bat`、`git push`。
9. **上传重试**：`k_EResultFileNotFound` / `k_EResultFail` 都是瞬时失败，**等 8~10 秒重跑同一条命令**即可；
   判成功只看 `Successfully uploaded`（`UploadCommand.cs:301-320`）。`k_EItemUpdateStatusInvalid` 只是进度终止值；
   `appworkshop_2868840.acf` 的 `timeupdated` 与本机缓存版本号属**订阅侧**记录，**不能用来验证**。
10. 判「联机不同步」最快路径：`tools\compare-mp-logs.ps1 -LogA 房主 -LogB 客人`；分叉包
    `ritsulib_state_divergence_*.zip` 里 `state-divergence-report.txt`，只筛 `local=` 与 `remote=` 不相等的行。
11. **日志响度**：新增/改动诊断日志时优先用 `Core.LogLoudness.Diag(...)`（手机自动静音、PC 照旧），
    「只在真出问题时才要」的用 `DiagStall(...)`。别再把高频诊断直接写成 `Logger.Log`。

## 其它在手事项

- 工坊介绍里的观者 mod 名字按用户原文写成 **Boninall / Lamall**（工坊作者实际是 `Boninall` / `lamali`）。
- 更早遗留待实机验证：蛇眼猎整场、养手骨无界命匣召唤、事件机谋杀加伤、情感芯片球被动翻倍。
