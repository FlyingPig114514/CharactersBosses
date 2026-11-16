# CharacterBosses — 交接状态

> 更新：2026-11-16 深夜（v0.2.711 已装机 + 已上传工坊 + 已推 GitHub）

## 🚀 Next phase

> ### ✅ v0.2.711 已发布（2026-11-16 18:15）
>
> · `.dist\CharacterBosses-v0.2.711.zip`（17,617,520 B），包内两份 Impl 都是 `v0.2.711`；
> · 工坊 item **3830639796** 上传成功（`UPLOAD OK`）；`workshop.json` 的 description / changeNote 都已更新到 v0.2.711；
> · GitHub `74ae0f7..ec68139` 已推（含 0.2.668~0.2.711 累计改动）。
>
> **本版内容**：三层两只观者 Boss（神格观 / Ω观）+ 联机「每场最终遭遇」广播 + 观者只在全端同一实现时出场
> + `WantsSecondBoss` 口径统一 + 说明框层级 + B 版神格「真退再进」。
> 细节见 `docs/plans/desync-2026-11-16-mp-act3-second-boss.md` 与 `docs/plans/watcher-b-divinity-reentry-2026-11-16.md`。
>
> **发布时踩的坑（下次别再踩）**：
> 1. `workshop\content\` 顶层曾残留 2026-11-15 的**旧副本**（dll/pck/json）。游戏用
>    `ModManager.ReadSteamMods → ReadModsInDirRecursive` **递归**扫工坊目录 ⇒ 同一 id 两份会被判
>    「重复加载」。上传前必须确认 `workshop\content\` 里**只有** `CharacterBosses\` 子目录（里面是新的 6 个文件）。
> 2. `changenote.txt` 以前是「一行 + 字面 `\n`」，Steam 会照字面显示。现在改成**真正的多行文件**
>    （`mk-workshop-json.ps1` 的 `Esc` 会把它转成 JSON 转义 ⇒ 页面按行显示）。
> 3. 工坊本机缓存（`steamapps\workshop\content\2868840\3830639796`）要等 Steam 自己刷新才会变新，
>    不能用它验证上传结果；看上传器的 `Successfully uploaded` + `UPLOAD OK` 为准。
> 4. 本机 `git push` 直接跑会被沙箱拦（凭据助手要命名管道：`couldn't create signal pipe, Win32 error 5`
>    ⇒ `could not read Username`）⇒ 需要 `danger-full-access` 重试同一条命令。
>
> **仍需实机验证**（下一轮）：
> · B 版神格观「已在神格再满 10 层」：期望 `退出姿态：神格 → 无姿态` + 能量再 +3 + 力量再 +2；从平静进神格 `能量 N → N+5`。
> · 说明框：开地图/图例/浮层时应出现 `已离开战斗屏（或压着别的界面）⇒ 收掉常驻特殊效果说明框`。
> · 联机（要多台机）：两端 `※阵容输入※` 与 `※多 Boss 生成指纹※` 必须逐字一致；
>   客人应出现 `客人端：第 3 幕第二场按**房主的结论**替换 ⇒ …`；房主应出现 `观者 Boss：房主判定「可以出现」=…`。
>   ⚠ **协议变了 ⇒ 联机两端必须同一版本。**

## 环境与硬规矩（沿用）

1. **不要擅自关用户的游戏**；装机前先 `Get-Process SlayTheSpire2`，占用就只编译等他说。
2. 构建用 `.\tools\dotnet\dotnet.exe build src\CharacterBosses.csproj -c Release -p:Sts2Target=111`（107 同理），**两个目标都要 0 错误**。
   ⚠ 107 里没有 `WatcherModPresence` / `WatcherCards`（整段在 `#if STS2_111`）⇒ 碰它们要加宏保护。
3. 改版本要同时改 `src/CharacterBosses.json` 的 `version` 和 `src/Hooks/Core/BuildInfo.cs` 的 `Tag`。
   ⚠ `src/CharacterBosses.json` 里是 GBK 乱码状的非 ASCII 内容 —— 改版本时用**字节级 ASCII 替换**，别整文件重写。
4. 发布三步：`install-mod.bat`（装机）→ `tools\pack-dist.ps1`（.dist + workshop\content）→ `tools\upload-workshop.ps1`（要 Steam 登录）。
5. **这台机器上 agent 拉不起游戏**（`steam://run` / `-applaunch` 都没反应；直接跑 exe 会 `No appID found`）⇒ 请用户自己启动。
6. 反编译 B 版：`D:\SteamLibrary\steamapps\workshop\content\2868840\3747492505\Watcher.dll`；A 版 `3747526116`；本体 `.refs\sts2_111.dll`
7. 观者只启用 **3747492505（lamali，B 版）**；两个都启用时 `Probe()` 优先 A 版。

## 其它在手事项

- 工坊介绍里的观者 mod 名字按用户原文写成 **Boninall / Lamall**（工坊作者实际是 `Boninall` / `lamali`）—— 若要改成 lamali 需再传一次。
- 更早遗留待实机验证：蛇眼猎整场、养手骨无界命匣召唤、事件机谋杀加伤、情感芯片球被动翻倍。
