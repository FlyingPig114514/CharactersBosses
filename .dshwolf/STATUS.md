# CharacterBosses — 交接状态

> 更新：2026-11-28 20:09（**v0.2.1002 已发布并已装手机**：打包 / 工坊 / GitHub / 手机四项全齐）。
> · 创意工坊条目 **3830639796** 已更新（上传器输出 `Successfully uploaded` + `UPLOAD OK`）；
> · GitHub `main` = `dab089b`，tag **`v0.2.1002`**，Release 挂 zip **19,026,153 B**，`releases/latest` 实测 = v0.2.1002；
> · 给玩家的下载页：https://github.com/FlyingPig114514/CharactersBosses/releases/latest
> · 手机：**已装 v0.2.1002**（USB + adb，serial `A3TE026721000487`，设备 BKQ-AN90 / 包名 `com.megacrit.sts2re`）。
>   落点 `files/mods/CharacterBosses/`（注意：手机上是**子目录**，不是散装在 `mods/` 根），5 个文件 md5 与本地构建逐一相同，
>   `CharacterBosses.json` 里 version=`0.2.1002`。装法：`adb push` 到 `/data/local/tmp/cb` → `run-as … cp` + `chmod 666`
>   （⚠ `adb shell "cat > file"` 流式传输实测只写进 4 字节，别再用那招；`/data/local/tmp` 反而是通的）。
>   暂存包留在 `.tools\phone-stage-mods.tar.gz`（`mods/CharacterBosses/` 布局）。
>
> 版本号口径：`0.2.999` 之后一律 `0.2.NNNN`（下一版 `0.2.1003`），不再进 0.3.x。

## 🚀 Next phase

> ### ⏳ v0.2.1002 已发布，等实机复验（用户还没在我这边确认过玩法）
>
> 1. **时间扭曲不再给力量**；2. **节制 +2 力量延后到玩家回合开始**；
> 3. **意图预览吃「同回合前面 Boss 先挂的易伤」**（`src/Hooks/Core/VulnerableLookahead.cs`，日志 `易伤前瞻：…`）；
> 4. 修好 ≥4 只 Boss 时超长意图行半透明。
> 详细口径见 `docs/plans/changelog-history.md` 的 `## v0.2.1002` 与发布记录。
>
> ### ⚠ 手机安装（下一件事）
> 1. 手机开**无线调试**（或插 USB）；2. `adb devices` 能看到设备后：
>    `powershell -File .tools\upload-b64.ps1` → 再 `cmd /c .tools\push-and-extract.bat`（tar 内是 `mods/`，解到 `files/` 下）；
> 3. 验版本：`adb -s <serial> shell "run-as com.megacrit.sts2re cat files/mods/CharacterBosses.json"` 应为 `0.2.1002`；
> 4. 手机运行时装的是 **net9** ⇒ `CharacterBosses.Impl111.dll.bin` 必须来自 `src\bin\Release-111-phone\`（本包已是）。
>
> ### 仍在等实机验证（更早）
> · v0.2.1001：多人时时间扭曲只记被锁定玩家；v0.2.1000：塔1 金边辉光 + 那个钟（尺寸 0.30 屏短边、匀速旋转）；
> · v0.2.997/0.2.998：6 只站位与次序、勤学精进、以手拒之返还、统帅模式切视角。
>
> ### 可能再改的点
> · `VulnerableLookahead` 的「摧残 ×2」是按“效果翻倍”推的；只处理「前面的 Boss 挂易伤」，遗物/玩家自身产生的易伤不在范围内。
>
## 环境与硬规矩（沿用）

1. **不要擅自关用户的游戏**；装机前先 `Get-Process "Slay the Spire 2"`（游戏开着时 `install-mod.bat` 的 copy 会被占用，
   表现为 `[verify] ✗ 部署版本不一致`）。
2. 构建用 `.\tools\dotnet\dotnet.exe build src\CharacterBosses.csproj -c Release -p:Sts2Target=111`（113 同理，
   手机 `-p:Sts2Target=111 -p:PhoneBuild=1`），**三份都要 0 错误**。
3. 改版本两处：`src/CharacterBosses.json`（**带 UTF-8 BOM**，`edit` 会弄丢，用 `WriteAllText(...,UTF8Encoding($true))` 补）
   + `src/Hooks/Core/BuildInfo.cs` 的 `Tag`（Tag 里**不能有反斜杠**）；changelog 条目写进
   `docs\plans\changelog-history.md`（`## vX.Y.Z` 段插在发布记录之后、上一版之前）。
4. 发布五步：`install-mod.bat "<游戏路径>"` → `changenote.txt` + `tools\mk-workshop-json.ps1` →
   `tools\pack-dist.ps1` → `tools\upload-workshop.ps1` → `git commit` + `git push` + 更新本文件。
5. 新增贴图三步（v0.2.999 实做路径）：放进 `tools\art\import_proj\CharacterBosses\images\<类>\` →
   `tools\godot\Godot_v4.5.1-stable_mono_win64_console.exe --headless --path tools\art\import_proj --import` →
   把 `res://` / `.import` / `.ctex` 三行追加进 `tools\art\pck_extras.txt`（`make-extras.ps1` **不存在**）。
6. 本机 agent **能**起游戏（`tools\launch_sts2.ps1`），但用户 2026-11-28 明确要求**不要再自己测**，只有他说才测。
7. 反编译：`.refs\sts2_113.dll` / `.refs\sts2_111.dll`，工具 `tools\ilspy-cli`（用法见 `docs/plans/recon-tooling.md`）；
   **塔1 的类**（`.class`）用 `ilspy-cli` 读不了 —— 直接 `[System.IO.Compression.ZipFile]` 拆
   `D:\SteamLibrary\steamapps\common\SlayTheSpire\desktop-1.0.jar` 抽常量池看字段/字符串。
8. 观者只启用 **3747492505（lamali，B 版）**。
9. ⚠ 工作区外、`workspace-write` 沙箱下会失败、需 `danger-full-access` 重跑**同一条**命令：`install-mod.bat`、`git push`、
   读写 `%APPDATA%\SlayTheSpire2\`、读 Steam 目录。
10. 上传瞬时失败（`k_EResultFileNotFound` / `k_EResultFail`）⇒ **等 8~10 秒重跑**；判成功只看 `Successfully uploaded`。
11. 判「联机不同步」：`tools\compare-mp-logs.ps1 -LogA 房主 -LogB 客人`。
12. **日志响度**：新诊断一律用 `Core.LogLoudness.Diag(...)`（手机静音、PC 照旧）。
13. ⚠ **Key 名字必须两边对得上**：`SubstituteIfUnavailable` 返回的 Key 必须在 `EncounterOf` 的 switch 里真实存在。
14. ⚠ 修「Boss 死后的钩子/遗物」**不要**再碰 `Player.DeactivateHooks()` / `IsActiveForHooks`（0.2.974 的回归）。
15. ⚠ **本模组自己 `new` 的 Godot 节点收不到 `_Process` / `_Draw` / `_Ready` / `_Input`** ⇒
    要么 Harmony 挂游戏自己的节点，要么用引擎 `Tween` 改内建节点属性（`UI\BossHandPlayFx`、`Core\TimeWarpFx`）。

## 其它在手事项

- 工坊介绍里的观者 mod 名字按用户原文写成 **Boninall / Lamall**（实际作者 `Boninall` / `lamali`）。
- 更早遗留待实机验证：蛇眼猎整场、养手骨无界命匣召唤、事件机谋杀加伤、情感芯片球被动翻倍。