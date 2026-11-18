# CharacterBosses — 交接状态

> 更新：2026-11-19（v0.2.800 **已装机 + 已打包 + 已上传工坊 + 已推 GitHub**）

## 🚀 Next phase

> ### ✅ v0.2.800 已发布（装机 → 打包 → 工坊 → GitHub 全部完成）
>
> · 装机：两份 Impl 都是 `v0.2.800`（`[verify] 两份实现的版本串都是 v0.2.800  OK`），PCK 重建成功；
> · 打包：`.dist\CharacterBosses-v0.2.800.zip`（18,333,800 B），`workshop\content\` 顶层只有 `CharacterBosses\`（6 文件）；
> · 工坊 item **3830639796** 上传成功（`Successfully uploaded … UPLOAD OK`），`workshop.json` 的 changeNote 已更新到 v0.2.800；
> · GitHub `dea4f6c..ed95eeb` 已推（`main` 与 `origin/main` 同步，`git status -sb` 无脏文件）。
>
> ### 本版内容（全部对得上用户当天给的四份日志 + 六个分叉包）
> 1. **联机被踢（主因）**：`BossDevourLifePower` 里 v0.2.815 那句 `if (!NetRole.IsHostOrSolo) return;` 删掉 ——
>    分叉包实证 `creatures[5].block 6 vs 0` + `creatures[1].currentHp 24 vs 37`（差 13 = 血肉戏法），
>    房主 22:32/22:35/22:37 每次 `吞噬生命：她给 … ⇒ 获得 6 点格挡 + 血肉戏法 13 点` 之后 11 秒就分叉、客人被踢。
>    **战况不是网络同步项**：两端各跑一遍同样的命令才一致，只让一端改就是制造分叉。
> 2. **阵容人数乱跳**：`LivePlayerCount` 不再把「从没听到过」的玩家当已离开；客人收到房主结论后每 5 秒发静默心跳；
>    房主 `pc=` 一变立刻重播；日志只在数变了时打一行。
> 3. `MultiBossPlan.WantsSecondBoss` 只认 `BossLineups.PlayerCount()`（阵容条数 = 伙伴数量）。
> 4. `PartnersForWithKeys`：伙伴不得与主 Boss / 已有伙伴同角色，凑不满就少出（删除「补重复」兜底）。
> 5. `tools\verify-deploy.ps1`：版本串改**字节层**查找（字符串堆奇数偏移会误报「读不到」）。
>
> ### 仍需实机验证（下一轮，必须多台机 + **全部 0.2.800**）
> · 血肉骨给人上负面时：**两端**都应出现 `吞噬生命：她给 … ⇒ 获得 6 点格挡 + 血肉戏法 13 点…`，
>   且**不再**出现 `Disconnected. Reason: StateDivergence`。
> · 3/4 人局全程：房主日志里不该再出现 `阵容人数：… 人已经 30 秒没说话`（除非真有人退）；
>   两端 `※多 Boss 生成指纹※` 逐字一致、`※伙伴角色去重自检※` 都「通过」。
> · 遗留：B 版神格观「已在神格再满 10 层」、常驻说明框收框日志。
>
> ### 证据归档（都在工作区根目录，已 gitignore）
> `mod_log (38)/(39)/(40).txt`、`godot (22)/(23)/(24).log`、6 个 `ritsulib_state_divergence_*.zip`。
> 已排查结论：**有证据的每种分叉都能归到 0.796（客人端 Boss 槽位 ⇒ 支配一端有一端没有）或 0.800（吞噬生命）**；
> 0.416 / 0.588 的老包分别是远古版本与「两端阵容不同」。目前**没有**已知未修的不同步，只是 0.796~0.800 从未联机验证过。

## 环境与硬规矩（沿用）

1. **不要擅自关用户的游戏**；装机前先 `Get-Process SlayTheSpire2`，占用就只编译等他说。
2. 构建用 `.\tools\dotnet\dotnet.exe build src\CharacterBosses.csproj -c Release -p:Sts2Target=111`（113 同理），**两个目标都要 0 错误**。
   ⚠ PATH 里的 `dotnet build` 会说「No .NET SDKs were found」—— 必须用仓库自带的 `tools\dotnet\dotnet.exe`。
3. 改版本要同时改 `src/CharacterBosses.json` 的 `version` 和 `src/Hooks/Core/BuildInfo.cs` 的 `Tag`。
   ⚠ `src/CharacterBosses.json` **带 UTF-8 BOM**：`edit` 工具会把 BOM 弄丢（`git diff` 会多一行 `-﻿{`），改完用
   `[System.IO.File]::WriteAllBytes` 补回 `EF BB BF`。改 `.ps1` 同理（无 BOM 的中文在 PS 5.1 里会解析失败）。
4. 发布四步：`install-mod.bat "<游戏路径>"` → `edit workshop\changenote.txt` + `tools\mk-workshop-json.ps1` →
   `tools\pack-dist.ps1` → `tools\upload-workshop.ps1`（要 Steam 登录，约 1 分钟）→ `git commit` + `git push`。
5. **这台机器上 agent 拉不起游戏** ⇒ 请用户自己启动。
6. 反编译 B 版：`D:\SteamLibrary\steamapps\workshop\content\2868840\3747492505\Watcher.dll`；A 版 `3747526116`；本体 `.refs\sts2_111.dll`
7. 观者只启用 **3747492505（lamali，B 版）**；两个都启用时 `Probe()` 优先 A 版。
8. ⚠ 三件事在工作区外、`workspace-write` 沙箱下会失败，都要 `danger-full-access` 重跑**同一条**命令：
   `install-mod.bat`（写游戏 `mods\`，否则 `Access is denied` + PCK 写不进去）、
   `tools\upload-workshop.ps1` 如报 Steam IPC 失败、`git push`（凭据助手要命名管道 ⇒ `couldn't create signal pipe, Win32 error 5`）。
   `pack-dist.ps1` 只写工作区内，不用抬。
9. 判「联机不同步」的最快路径：`tools\compare-mp-logs.ps1 -LogA 房主 -LogB 客人`；
   更深一层看 `ritsulib_state_divergence_*.zip` 里的 `state-divergence-report.txt`，只筛
   `local=` 与 `remote=` 不相等的行（脚本技巧：正则 `^(\S.*?):\s*local=(.*?);\s*remote=(.*)$` 再比较两组）。

## 其它在手事项

- 工坊介绍里的观者 mod 名字按用户原文写成 **Boninall / Lamall**（工坊作者实际是 `Boninall` / `lamali`）—— 若要改成 lamali 需再传一次。
- 更早遗留待实机验证：蛇眼猎整场、养手骨无界命匣召唤、事件机谋杀加伤、情感芯片球被动翻倍。
