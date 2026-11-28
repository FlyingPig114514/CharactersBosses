# CharacterBosses — 交接状态

> 更新：2026-11-28（**本机已装机 v0.2.1002**；工坊 / GitHub 仍停在 v0.2.997，未上传）。
> · 工坊条目 3830639796 更新于 11-28 15:58:17（file_size 22,043,428，visibility=0 公开）；
> · GitHub `main` = `ab92ebe` → `6c3556e`，tag `v0.2.997`，Release id 461396250（zip 18,572,715 B），
>   `releases/latest` 指向 v0.2.997；给玩家的下载页：
>   https://github.com/FlyingPig114514/CharactersBosses/releases/latest
> · **v0.2.998 ~ v0.2.1002 全都没提交、没打包、没上传**（工作区有未提交改动）。
>
> 版本号口径（用户 2026-11-28 定）：0.2.999 之后写 0.2.NNNN，不再进 0.3.x（下一版 0.2.1003）。

## 🚀 Next phase

> ### ⏳ v0.2.1002（本会话最后一版，**等用户实机复验**）
>
> 1. **时间扭曲不再给力量**（用户：不要在强制终止玩家回合时让所有 boss 获得 2 力量）——
>    `BossTimeWarpPower` 触发只做「结束被锁定玩家的回合 + 塔1 特效」，16 语言说明同步改成「只结束回合」。
> 2. **节制力量延后到玩家回合开始**（用户新口径：强制结束自己的回合 + 玩家下回合开始时她与友方 +2）——
>    `TriggerAsync` 只记 `_pendingStrengthGrants++`，`AfterSideTurnStart(side==Player)` 结算；
>    文案（powers 表 + monsters 表，16 语言）已同步。
> 3. **意图预览吃「前面 Boss 先挂的易伤」**（新增 `src/Hooks/Core/VulnerableLookahead.cs`）：
>    按 `CombatState.Enemies` 的行动次序正向模拟前面每只 Boss 的计划牌；挂易伤判据 = `DynamicVars.Vulnerable`
>    （反编译实证 Bash/HighFive 都是 `PowerVar<VulnerablePower>`）；`TargetType.AllEnemies` 算给所有玩家、
>    单体算给它计划打的目标；苦难 MISERY 的「传染」也算；只在「目标现在没有易伤」时乘 1.5（目标带摧残 2.0），
>    乘在 floor 之前。无来源伤害不走这条管道，召唤物/影子玩家/原版怪物一律 1.0。
>
> **等着验的点**：① 恐惧模式两处 12 张都只结束回合、不加力量；② 节制触发后玩家侧回合开始才 +2
> （日志 `节制：玩家的回合开始 ⇒ 她与友方 +2 力量到账`）；③ 养手骨「击掌」在先 + 巨石战「彼岸咆哮」在后时，
> 彼岸咆哮的预览应比之前高 50%（有摧残 100%），日志有 `易伤前瞻：<牌> 的伤害预览 ×1.5（…）`。
>
> ### ⏳ 仍在等实机验证（更早几版）
> · v0.2.1001：多人时时间扭曲只记被锁定玩家的牌（日志 `时间扭曲：记 X 的第 n/12 张（锁定目标=Y）`）；
> · v0.2.1000：时间扭曲特效（塔1 的 borderGlow2 金边 + 那个钟，尺寸 0.30 屏短边、旋转匀速）；
> · v0.2.997/0.2.998：6 只站位与次序、≥4 只意图半透明、勤学精进、以手拒之返还、统帅模式切视角。
>
> ### 可能再改的点
> · `VulnerableLookahead` 的「摧残 ×2」是按“效果翻倍”推的（虚弱 ×0.75→×0.5 同理），若实机发现是 ×1.5 再调；
> · 只处理「前面的 Boss 挂易伤」；遗物/玩家自身产生的易伤不在前瞻范围内。
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