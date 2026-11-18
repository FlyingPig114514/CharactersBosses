# CharacterBosses — 交接状态

> 更新：2026-11-19（v0.2.800 **已装机 + 已打包，未上传工坊**）—— 真凶是「吞噬生命只让房主结算」

## 🚀 Next phase

> ### ✅ v0.2.800 已装机 / 已打包（2026-11-19，**未上传**）
>
> · 装机：两份 Impl 都是 `v0.2.800`（`[verify] 两份实现的版本串都是 v0.2.800  OK`），PCK 重建成功；
> · 打包：`.dist\CharacterBosses-v0.2.800.zip`（17.48 MB），`workshop\content\` 顶层只有 `CharacterBosses\`；
> · **没有上传工坊**。
>
> ### 本版三件事（都对得上用户给的四份日志）
> 1. **联机被踢（主因）**：客人 `godot (22).log` 22:37:41 `[RunLobby] Disconnected. Reason: StateDivergence`（checksum 56）。
>    `mod_log (38)/(39)` 对表：不同步字段 = `E:NECROBINDER_BOSS` 格挡 **+6（房主）vs +0（客人）**；
>    根因是 `BossDevourLifePower` 里 v0.2.815 那句 `if (!NetRole.IsHostOrSolo) return;` ——
>    房主 22:37:39 `吞噬生命：她给 Skyphantom 上了 VULNERABLE_POWER（2）⇒ 获得 6 点格挡 + 血肉戏法 13 点无来源伤害`，
>    客人同一秒只有 `本机是客人 ⇒ 只记录、不改状态`。**战况不是网络同步项**（两端各跑一遍同样的命令才一致）
>    ⇒ 删掉那道闸，两端都结算（同仓库 `DominateWeakPower` / `BossStellarGatheringPower` 就是这么做的）。
> 2. **阵容人数乱跳**：`mod_log (39)`（房主）22:36:25 数出 2 人、22:36:55 数出 1 人，其实 3 人都在玩 ——
>    客人收到房主结论后就不再发消息（`Tick` 里 `if (Received) return`）⇒ 房主把还在玩的客人判成「已离开」
>    ⇒ 阵容条数跟着塌 ⇒ 两端 Boss 条数对不上，还会触发 v0.2.799 那个「主 Boss 当自己的伙伴」。
>    现在：没听到过的玩家 **按还在算**（只有「听过之后安静 >30 秒」才剔除）、日志只在数变了时打一行、
>    客人收到结论后每 5 秒发一次**静默心跳**、房主的 `pc=` 一变立刻重播。
> 3. `MultiBossPlan.WantsSecondBoss` 改成**只认 `BossLineups.PlayerCount()`**（阵容几条 = 伙伴几只，天然同口径）。
>
> ### 仍需实机验证（下一轮，必须**多台机 + 同一版本**）
> · 血肉骨（一层 `NECROBINDER_FLESH`）给她上负面时：**两端**都应出现
>   `吞噬生命：她给 <玩家> 上了 …（N）⇒ 获得 6 点格挡 + 血肉戏法 13 点无来源伤害`（客人端以前只有「只记录」那行）；
>   期望**不再**出现 `Disconnected. Reason: StateDivergence`。
> · 3/4 人局全程：房主日志里**不该**再出现 `阵容人数：… 人已经 30 秒没说话`（除非真有人退）；
>   两端 `※多 Boss 生成指纹※` 逐字一致、`※伙伴角色去重自检※` 都是「通过」。
> · 上一轮遗留：B 版神格观「已在神格再满 10 层」、常驻说明框收框日志。

## 环境与硬规矩（沿用）

1. **不要擅自关用户的游戏**；装机前先 `Get-Process SlayTheSpire2`，占用就只编译等他说。
2. 构建用 `.\tools\dotnet\dotnet.exe build src\CharacterBosses.csproj -c Release -p:Sts2Target=111`（113 同理），**两个目标都要 0 错误**。
   ⚠ PATH 里的 `dotnet build` 会说「No .NET SDKs were found」—— 必须用仓库自带的 `tools\dotnet\dotnet.exe`。
3. 改版本要同时改 `src/CharacterBosses.json` 的 `version` 和 `src/Hooks/Core/BuildInfo.cs` 的 `Tag`。
   ⚠ `src/CharacterBosses.json` **带 UTF-8 BOM**：用 `edit` 工具改会把 BOM 弄丢（历史：`git diff` 会多一行 `-﻿{`）；
   改完用 `[System.IO.File]::WriteAllBytes` 补回 `EF BB BF`。改 `.ps1` 同理（无 BOM 的中文在 PS 5.1 里会解析失败）。
4. 发布三步：`install-mod.bat "<游戏路径>"`（路径参数必须在第 1 位，`pack` 可放任意位置）→
   `tools\pack-dist.ps1`（.dist + workshop\content）→ `tools\upload-workshop.ps1`（要 Steam 登录）。
5. **这台机器上 agent 拉不起游戏** ⇒ 请用户自己启动。
6. 反编译 B 版：`D:\SteamLibrary\steamapps\workshop\content\2868840\3747492505\Watcher.dll`；A 版 `3747526116`；本体 `.refs\sts2_111.dll`
7. 观者只启用 **3747492505（lamali，B 版）**；两个都启用时 `Probe()` 优先 A 版。
8. ⚠ 装机写的是工作区**之外**的目录（游戏 `mods\`）⇒ 沙箱 `workspace-write` 下会 `Access is denied`（copy 静默失败、PCK 写不进去）⇒ 需要 `danger-full-access` 重跑**同一条**命令。`pack-dist.ps1` 只写工作区内，不用抬。
9. 判「联机不同步」的最快路径：`tools\compare-mp-logs.ps1 -LogA 房主 -LogB 客人`；
   更深一层看 `%APPDATA%\SlayTheSpire2\logs\ritsulib_state_divergence_*.zip` 里的
   `state-divergence-report.txt`（里面有 local/remote 逐字段对比，只打 `local≠remote` 的那几行即可定位）。
10. 装机自检 `tools\verify-deploy.ps1` v0.2.800 起按**字节层**找版本串（以前整份按 UTF-16 解，奇数偏移会误报「读不到」）。

## 其它在手事项

- 工坊介绍里的观者 mod 名字按用户原文写成 **Boninall / Lamall**（工坊作者实际是 `Boninall` / `lamali`）—— 若要改成 lamali 需再传一次。
- 更早遗留待实机验证：蛇眼猎整场、养手骨无界命匣召唤、事件机谋杀加伤、情感芯片球被动翻倍。
