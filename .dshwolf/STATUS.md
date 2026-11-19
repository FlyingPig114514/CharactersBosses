# CharacterBosses — 交接状态

> 更新：2026-11-19（v0.2.807 **已装机 + 已打包 + 已上传工坊 + 已推 GitHub**）

## 🚀 Next phase

> ### ✅ v0.2.807 已发布
>
> · 装机 `v0.2.807`（两份 Impl 校验 OK）；打包 `.dist\CharacterBosses-v0.2.807.zip`（18,364,664 B）；
> · 工坊 item **3830639796** 上传成功（`Successfully uploaded` / `UPLOAD OK`）；
> · GitHub `4b6700b..b1a86ba` 已推。
>
> ### 本版两件事（用户 2026-11-19 截图）
> 1. **没装观者 mod 却遇到观者 Boss（占位愚者，名字 `Characterbosses`，特殊效果却是「斋戒」）**：
>    ① `BossKeys.SubstituteIfUnavailable` 的兜底目标写成 **`FOOL_BOSS`**（不存在的 Key，真名 `FOOL_TAROT`）
>    ⇒ `EncounterOf` 落 `default: return null` ⇒ 调用方当成「没有替换」；
>    ② 这条兜底只管「按 Key 造遭遇」，而 Boss 房走 `PullNextEncounter` 的「**沿用存档槽**」分支
>    （槽里可能还留着装过观者 mod 时的观者遭遇）⇒ 新增 `BossKeys.FixUnavailableEncounter(EncounterModel?)`
>    并在 `PullNextEncounterPatch` 返回前兜一次。
> 2. **Boss 的电球/黑球电到自己人**：用户口径 = **先打被锁定玩家的召唤物（奥斯提等），没有才打玩家本人**。
>    原版两个 `Passive` 在 target 为空时从施法者视角的 `HittableEnemies` 随机挑 ⇒ 会落到别的 Boss。
>    新增 `Core/BossOrbTargeting` + `LightningOrb.Passive` / `DarkOrb.Passive` 两个前缀（显式写死参数类型）；
>    只接管「球的主人是我们 Boss」的情况，玩家自己的球不动；挑宠物按 `CombatState.Creatures` 列表顺序 ⇒ 两端一致。
>
> ### 仍需实机验证
> · 不装观者 mod 开一局：**不应**再出现观者 Boss / 占位愚者；日志应见
>   `观者 Boss：这一场是 WATCHER_…，但本机探测不到观者 mod ⇒ 换成塔罗愚`。
> · 故障机器人 Boss 的电球/黑球：应打在**被它锁定的玩家的奥斯提**上；没有召唤物才打玩家本人；
>   **不该**掉到另一只 Boss 身上。
> · 上一轮待验：手机端日志量下降 / 3~4 人局 `多 Boss 站位（3 只）` / 恐惧模式 `支配对齐：战斗状态` / 极速模式 `Boss 替换判定` 行。
> · 遗留：B 版神格观「已在神格再满 10 层」、常驻说明框收框日志。

## 环境与硬规矩（沿用）

1. **不要擅自关用户的游戏**；装机前先 `Get-Process SlayTheSpire2`。
2. 构建用 `.\tools\dotnet\dotnet.exe build src\CharacterBosses.csproj -c Release -p:Sts2Target=111`（113 同理），**两个目标都要 0 错误**。
3. 改版本两处：`src/CharacterBosses.json`（**带 UTF-8 BOM**，`edit` 会弄丢，用 `WriteAllText(...,UTF8Encoding($true))` 补）
   + `src/Hooks/Core/BuildInfo.cs` 的 `Tag`。
4. 发布五步：`install-mod.bat "<游戏路径>"` → `changenote.txt` + `tools\mk-workshop-json.ps1` →
   `tools\pack-dist.ps1` → `tools\upload-workshop.ps1` → `git commit` + `git push` + 更新本文件。
5. **这台机器上 agent 拉不起游戏** ⇒ 请用户自己启动。
6. 反编译：`.refs\sts2_113.dll` / `.refs\sts2_111.dll`，工具 `tools\ilspy-cli`（用法见 `docs/plans/recon-tooling.md`）。
7. 观者只启用 **3747492505（lamali，B 版）**。
8. ⚠ 工作区外、`workspace-write` 沙箱下会失败、需 `danger-full-access` 重跑**同一条**命令：`install-mod.bat`、`git push`。
9. 上传瞬时失败（`k_EResultFileNotFound` / `k_EResultFail`）⇒ **等 8~10 秒重跑**；判成功只看 `Successfully uploaded`。
   `appworkshop_2868840.acf` / 本机工坊缓存属**订阅侧**，**不能用来验证**。
10. 判「联机不同步」：`tools\compare-mp-logs.ps1 -LogA 房主 -LogB 客人`；分叉包 `state-divergence-report.txt` 只筛 `local≠remote` 行。
11. **日志响度**：新诊断一律用 `Core.LogLoudness.Diag(...)`（手机静音、PC 照旧），必要时 `DiagStall(...)`。
12. ⚠ **Key 名字必须两边对得上**：`SubstituteIfUnavailable` 返回的 Key 必须在 `EncounterOf` 的 switch 里真实存在
    （v0.2.806 就是 `FOOL_BOSS` vs `FOOL_TAROT` 写错导致兜底静默失效）。

## 其它在手事项

- 工坊介绍里的观者 mod 名字按用户原文写成 **Boninall / Lamall**（实际作者 `Boninall` / `lamali`）。
- 更早遗留待实机验证：蛇眼猎整场、养手骨无界命匣召唤、事件机谋杀加伤、情感芯片球被动翻倍。
