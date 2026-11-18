# CharacterBosses — 交接状态

> 更新：2026-11-19（v0.2.802 **已装机 + 已打包 + 已上传工坊 + 已推 GitHub**）

## 🚀 Next phase

> ### ✅ v0.2.802 已发布（装机 → 打包 → 工坊 → GitHub）
>
> · 装机：两份 Impl 都是 `v0.2.802`（`[verify] … OK`），PCK 重建成功；
> · 打包：`.dist\CharacterBosses-v0.2.802.zip`（17.49 MB），`workshop\content\` 顶层只有 `CharacterBosses\`；
> · 工坊 item **3830639796** 上传成功（`UPLOAD OK`），changeNote 已更新到 v0.2.802；
> · GitHub `3660be1..3fac2b7` 已推。
>
> ### 本版三件事
> 1. **多人「Boss 房只有 1 只」**（用户 2026-11-19 截图 + `mod_log (41)`）：
>    客人端 23:19:44 `※阵容输入※ … 玩家=1` ⇒ 阵容只排 1 条 ⇒ 23:21 结算「`REGENT_BOSS` 为主，共 **1 只** Boss」，
>    同一局 23:21:47 重算又是 `3 人局` 3 条。
>    ⇒ 房主现在把**整条阵容**随「每场最终遭遇」一起广播（`主Boss,伙伴1,伙伴2…`），客人 `BossLineups.For`
>    直接照抄；房主**每 5 秒无条件重播**；客人只在内容真变了时才清阵容缓存；
>    `MultiBossPlan.PartnersForWithKeys` 的伙伴数量改由**阵容条数**决定（不再问可能过时的玩家数）。
> 2. **打击战恐惧模式删掉「每张 +1」**（用户：这个增强从没让实现过）—— 两模式每张加成现在相同
>    （进阶 0～8 +2、进阶 9～10 +3）。「痛击自己也吃打击加成」保留（那是用户明确要过的）。
> 3. **打击战多人只统计被锁定玩家**：以前 `CountNow` 把所有真人玩家的打击牌加一起
>    （4 人局 7+40=47 张 ×3 ⇒ 完美打击 147，正是「第三回合 100+」），`AfterCardPlayed` 也「谁打都算」；
>    现在两处都走 `BossLockOn.SpecialEffectAppliesTo`（单人恒 true ⇒ 零回归）。
>
> ### 仍需实机验证（**全员必须 0.2.802**）
> · 3/4 人局任意 Boss 房：`多 Boss 站位（3 只）`、两端 `※多 Boss 生成指纹※` 逐字一致；
>   房主应出现 `勾选同步：房主记下第 X 层第 Y 场的整条阵容 = [...]`，客人应出现
>   `第 X 层第 Y 场阵容：**按房主广播的整条阵容**（… 不再自己重算）`。
> · 若只出 1 只：现在一定会留下 `伙伴 Boss 判定：… 阵容只有 N 条 ⇒ **不加伙伴**`（以前是静默 return）。
> · 打击战：多人第三回合的完美打击不再 100+（每人牌堆各算各的）；恐惧模式每张加成与正常一致。
> · 遗留：B 版神格观「已在神格再满 10 层」、常驻说明框收框日志。
>
> ### 证据归档（工作区根目录，已 gitignore）
> `mod_log (38)~(41).txt`、`godot (22)~(24).log`、8 个 `ritsulib_state_divergence_*.zip`。
> 已结论：**有包的不同步**都能归到 0.796（客人端 Boss 槽位 ⇒ 支配一端有一端没有）或 0.800（吞噬生命）；
> 0.416 / 0.588 的老包分别是远古版本与「两端阵容不同」。目前没有已知未修的不同步。

## 环境与硬规矩（沿用）

1. **不要擅自关用户的游戏**；装机前先 `Get-Process SlayTheSpire2`，占用就只编译等他说。
2. 构建用 `.\tools\dotnet\dotnet.exe build src\CharacterBosses.csproj -c Release -p:Sts2Target=111`（113 同理），**两个目标都要 0 错误**。
   ⚠ PATH 里的 `dotnet build` 会说「No .NET SDKs were found」。
3. 改版本要同时改 `src/CharacterBosses.json` 的 `version` 和 `src/Hooks/Core/BuildInfo.cs` 的 `Tag`。
   ⚠ `src/CharacterBosses.json` **带 UTF-8 BOM**：`edit` 工具会把 BOM 弄丢（`git diff` 多一行 `-﻿{`）；
   改完用 `[System.IO.File]::WriteAllBytes`（`UTF8Encoding($true)`）补回。改 `.ps1` 同理（无 BOM 的中文在 PS 5.1 里解析失败）。
4. 发布五步：`install-mod.bat "<游戏路径>"` → `edit workshop\changenote.txt` + `tools\mk-workshop-json.ps1` →
   `tools\pack-dist.ps1` → `tools\upload-workshop.ps1`（要 Steam 登录，约 1 分钟）→ `git commit` + `git push`。
5. **这台机器上 agent 拉不起游戏** ⇒ 请用户自己启动。
6. 反编译 B 版：`D:\SteamLibrary\steamapps\workshop\content\2868840\3747492505\Watcher.dll`；A 版 `3747526116`；本体 `.refs\sts2_111.dll`
7. 观者只启用 **3747492505（lamali，B 版）**；两个都启用时 `Probe()` 优先 A 版。
8. ⚠ 工作区外、`workspace-write` 沙箱下会失败、需要 `danger-full-access` 重跑**同一条**命令：`install-mod.bat`、
   `git push`（凭据助手要命名管道 ⇒ `couldn't create signal pipe, Win32 error 5`）；
   `tools\upload-workshop.ps1` 目前直接跑就成功（Steam 已登录），失败再抬。
   `pack-dist.ps1` / `mk-workshop-json.ps1` 只写工作区内，不用抬。
9. 判「联机不同步」最快路径：`tools\compare-mp-logs.ps1 -LogA 房主 -LogB 客人`；
   分叉包 `ritsulib_state_divergence_*.zip` 里的 `state-divergence-report.txt`，只筛
   `local=` 与 `remote=` 不相等的行（正则 `^(\S.*?):\s*local=(.*?);\s*remote=(.*)$` 再比较两组）。
10. 装机自检 `tools\verify-deploy.ps1` 按**字节层**找版本串（字符串堆奇数偏移会让整份 UTF-16 解码错位、误报「读不到」）。

## 其它在手事项

- 工坊介绍里的观者 mod 名字按用户原文写成 **Boninall / Lamall**（工坊作者实际是 `Boninall` / `lamali`）—— 若要改成 lamali 需再传一次。
- 更早遗留待实机验证：蛇眼猎整场、养手骨无界命匣召唤、事件机谋杀加伤、情感芯片球被动翻倍。
