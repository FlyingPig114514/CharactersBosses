# CharacterBosses — 交接状态

> 更新：2026-11-27（**v0.2.981 已装机 + 已打包 + 已上传工坊 + 已推 GitHub**）。
> ✅ 0.2.975 → 0.2.981 已全部发布：工坊条目 3830639796 更新于 22:45:20（21,991,204 B）；
> GitHub `main` = `a28a48e`，tag `v0.2.981`，Release 460736498（zip 18,549,928 B，latest 已指向它）。

## 🚀 Next phase

> ### ✅ v0.2.980（本会话最后一版）：隐私审查 —— 三份报告文件以前完全没脱敏
>
> 用户：「检查一下，目前本模组有没有什么其它地方写了隐私信息？」
> 实测：`characterbosses_trace.txt` 有 16 处 `C:\Users\<用户名>` + 3 处 SteamID；
> `characterbosses_desync.txt` 有 **117 处 SteamID** + 40 处 `NetId=数字`；
> `characterbosses_recent.txt` 有 3 处 `C:/Users/<用户名>`（`Redact` 的路径正则只认反斜杠，Godot 给正斜杠）。
> 而 F8 的 zip 把这三份一起发给作者 ⇒ 等于把原始 ID 送出去。
> 改法：`Redact` 正则两个方向都认；`CrashTrace` / `DivergenceStateProbe` / `StallWatchdog` / `SceneTreeDumper`
> 落盘前统一过 `Redact.Text`；zip 附带文件改成「读出来→脱敏→写进去」（连旧版本的历史文件也兜住）。
> 验证：同一份 desync 磁盘上 117 处 SteamID ⇒ zip 里 0 处，变成 117 个 `P#xxxxxx` 代号（诊断价值保留）。
>
> ### ✅ 同会话的另外三版
> · **0.2.976/0.2.977**：F8/F9 **从「从来没成功过」到实测可用**（真因：我们自己的 Node `has_method(_input)=False`，
>   Godot 不派发 ⇒ 改 Harmony 挂 `NGame._Input` / `NInputManager._UnhandledKeyInput`；导出改读磁盘尾巴 + zip）；
> · **0.2.978**：设置页新增 **A7「日志在哪里 / 一键导出」**（手机没 F8 也能导，点完显示结果路径）；
> · **0.2.979**：界面上的路径一律 `PathDisplay.Mask` → `%APPDATA%`（玩家截图不再漏自己的用户名）。
>
> ### 仍需实机验证（沿用上一轮）
> F8 那条的真因自检行原文（给下一位看）：
> `has_method(_input)=False has_method(_unhandled_key_input)=False`（但 `script=有`、`processInput=True`、`path=/root/…`）
> ⇒ 与本模组早记过的「自绘 UI 的 `_Process` / `Control.Resized` 不触发」是**同一个病**。
> **要键盘/帧回调，一律 Harmony 挂游戏自己的节点**，别给自己的 Node 写虚回调。
>
> **实测**（本机 v0.2.977）：主菜单 F8 → 55 行；战斗内 F8 → 同一文件覆盖成 219 行；F9 → 场景树 4381 行 +
> 战斗房 580 行；zip 含 recent + trace + desync。
>
> ### 仍在等实机验证的清单
> · 上一轮待验：手机端日志量下降 / 3~4 人局 `多 Boss 站位（3 只）` / 恐惧模式 `支配对齐：战斗状态` / 极速模式 `Boss 替换判定` 行；
>   不装观者 mod 开一局不应出现观者 Boss；故障机器人 Boss 的电球/黑球应打在**被锁定玩家的召唤物**上。
> · 遗留：B 版神格观「已在神格再满 10 层」、常驻说明框收框日志。
> · ✅ 已完成（0.2.978/0.2.979）：BaseLib 设置页 A7「导出最近日志」按钮 + 界面上路径脱敏；
>   下一件可做：昵称脱敏只在战斗内生效（战斗外的日志行仍可能带昵称），以及 BaseLib 面板「内容比视口宽 ⇒ 右侧文字被切」那个既有布局问题。
>
> ### 发布前记得（用户口令「打包，上传创意工坊和github」）
> `git add -A && commit` + tag → `install-mod.bat pack` / `tools\pack-dist.ps1` → `tools\upload-workshop.ps1` → Release。
> **别回退 0.2.975 那条**：不要再拿 `IsActiveForHooks` / `DeactivateHooks` 去治「死掉 Boss 的遗物继续发效果」
> ——会让「愚者的箭」不返还。

## 环境与硬规矩（沿用）

1. **不要擅自关用户的游戏**；装机前先 `Get-Process SlayTheSpire2`（游戏开着时 `install-mod.bat` 的 copy 会被占用，
   表现为 `[verify] ✗ 部署版本不一致`）。
2. 构建用 `.\tools\dotnet\dotnet.exe build src\CharacterBosses.csproj -c Release -p:Sts2Target=111`（113 同理，
   手机 `-p:Sts2Target=111 -p:PhoneBuild=1`），**三份都要 0 错误**。
3. 改版本两处：`src/CharacterBosses.json`（**带 UTF-8 BOM**，`edit` 会弄丢，用 `WriteAllText(...,UTF8Encoding($true))` 补）
   + `src/Hooks/Core/BuildInfo.cs` 的 `Tag`（Tag 里**不能有反斜杠**）。
4. 发布五步：`install-mod.bat "<游戏路径>"` → `changenote.txt` + `tools\mk-workshop-json.ps1` →
   `tools\pack-dist.ps1` → `tools\upload-workshop.ps1` → `git commit` + `git push` + 更新本文件。
5. 本机 agent **能**起游戏（`tools\launch_sts2.ps1` + `tools\wait_sts2.ps1`），但需要 `danger-full-access`；
   发键自检用 `.tmp\send-keys.ps1`（ASCII-only，5.1 读中文会崩）。
6. 反编译：`.refs\sts2_113.dll` / `.refs\sts2_111.dll`，工具 `tools\ilspy-cli`（用法见 `docs/plans/recon-tooling.md`）。
7. 观者只启用 **3747492505（lamali，B 版）**。
8. ⚠ 工作区外、`workspace-write` 沙箱下会失败、需 `danger-full-access` 重跑**同一条**命令：`install-mod.bat`、`git push`、
   读写 `%APPDATA%\SlayTheSpire2\` 下任何文件。
9. 上传瞬时失败（`k_EResultFileNotFound` / `k_EResultFail`）⇒ **等 8~10 秒重跑**；判成功只看 `Successfully uploaded`。
   `appworkshop_2868840.acf` / 本机工坊缓存属**订阅侧**，**不能用来验证**。
10. 判「联机不同步」：`tools\compare-mp-logs.ps1 -LogA 房主 -LogB 客人`；分叉包 `state-divergence-report.txt` 只筛 `local≠remote` 行。
11. **日志响度**：新诊断一律用 `Core.LogLoudness.Diag(...)`（手机静音、PC 照旧），必要时 `DiagStall(...)`。
12. ⚠ **Key 名字必须两边对得上**：`SubstituteIfUnavailable` 返回的 Key 必须在 `EncounterOf` 的 switch 里真实存在
    （v0.2.806 就是 `FOOL_BOSS` vs `FOOL_TAROT` 写错导致兜底静默失效）。
13. ⚠ 修「Boss 死后的钩子/遗物」**不要**再碰 `Player.DeactivateHooks()` / `IsActiveForHooks`
    （0.2.974 的回归：愚者的箭不返还）。

## 其它在手事项

- 工坊介绍里的观者 mod 名字按用户原文写成 **Boninall / Lamall**（实际作者 `Boninall` / `lamali`）。
- 更早遗留待实机验证：蛇眼猎整场、养手骨无界命匣召唤、事件机谋杀加伤、情感芯片球被动翻倍。
