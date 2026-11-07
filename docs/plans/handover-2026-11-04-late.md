# 交接（2026-11-04 深夜，压缩版）——下个会话先读这一页

> 上一个会话很长（v0.2.356 → v0.2.382 全是同一场对话里做的），上下文已用尽。
> 本页就是那场的**压缩结果**：只留「现在是什么状态 + 接下来做什么 + 别踩的坑」。
> 细节去 `.dshwolf/STATUS.md` 顶部对应的 `### 追加：v0.2.3xx` 段落查（每版都记了根因与实证）。

## 1. 现在的状态

| 项 | 值 |
|---|---|
| 版本 | **v0.2.382**（`src/CharacterBosses.json` + `src/Hooks/Core/BuildInfo.cs` 两处） |
| dll | `src/bin/Release/CharacterBosses.dll` |
| PCK | `.build/CharacterBosses.pck`（13,251,720 B，`content/` 变了就重打） |
| 已装 | 游戏 `mods\CharacterBosses` 与工坊 `3830639796\CharacterBosses`（dll/json/pck 三件都换） |
| 本地包 | `.dist/CharacterBosses-v0.2.375.zip`（**落后于代码**，用户说「打包」才重打） |
| 工坊上传 | **v0.2.375 之后都没传**。`workshop/` 的 content/json/changenote 是 v0.2.375 那份 |

## 2. 接下来要做的两件事（用户 2026-11-04 提的，详见另一份文档）

**`docs/plans/todo-2026-11-04-stun-and-echo-slash.md`** —— 两条新需求，都还没动手：

1. **击晕的整套行为**（优先级更高）：
   立刻压暗所有牌 + 撤掉所有意图 + 本回合不行动 + **下回合补打被打断的那份计划**
   （难点：连续被晕不能吃掉计划 ⇒ 需要「待执行计划快照」，不能靠回退回合号）。
2. **回响斩击按斩杀链显示段数**（能斩杀就多一段，能连杀更多就更多段）。

## 3. 这一场长会话里最值得记的几条（都已进 buglog / cerebrum）

- **async 钩子跳过的铁律**：Harmony Prefix 跳过返回 `Task` 的方法，必须 `ref Task __result` 赋
  `Task.CompletedTask`，否则返回 null Task ⇒ 下游 `GetAwaiter()` 抛 NRE，
  **整条钩子链静默断掉**（症状会伪装成完全无关的 UI 问题：结束回合按钮消失、Boss 不出牌）。
  v0.2.356 就是靠这条修好了「事件机没有结束回合按钮」。
- **v0.2.356 之后的一类系统性回潮**：以前「原版钩子打不到影子玩家、所以我们手动补一次」的地方，
  现在原版钩子真的会打到她 ⇒ **同一条效果出现两次**。已修：王室猛毒、无界命匣每回合召唤。
  同类 once/NoDouble 补丁还有一批（CrackedCore / FencingManual / HappyFlower / ShadowRedMask /
  DiamondDiadem / Akabeko），**下次看到「效果两次」优先怀疑这里**。
- **意图预览必须每帧重算**：凡是数值在规划期算死的（`RawAttackIntent` / `AttackIntent.Raw`），
  玩家事后上的易伤/虚弱都不会反映。修过的：小刀系、奥斯提牌（`OstyFinalDamage`）。
- **取「受害者」的兜底必须和原版一致（本机玩家）**，绝不能兜成 dealer 自己 ——
  而我们这只 Boss 的生物挂在**影子 Player** 上，`creature.Player != null` 也成立 ⇒
  判「是不是玩家」要判 `creature.Monster == null`，不能只判 `Player == null`。
- **视觉类 bug 必须抽帧看**：`C:\Users\<user>\AppData\Local\Programs\xiezuo\resources\ffmpeg\ffmpeg.exe`
  （只能输出 jpg），流程见 `docs/plans/how-to-read-video.md`。这一场靠它 30 秒定位了小刀 4×3 的真凶。
- **`tools\*.ps1` 含中文必须存成带 BOM 的 UTF-8**，否则 PS 5.1 按 GBK 解码 ⇒ 一堆假语法错
  （`feishu-stats.ps1` 就这么坏过，已修）。
- **音乐分段的边界**：只有「原版自己在战斗中途写过 `*_progress`」的曲子才可能分段。
  ✅ 女王 `queen_progress`、墨影幻灵 `vantom_progress`、瀑布巨兽 `waterfall_giant_progress`、
  灵魂异鱼 `soulfysh_progress`；❌ 无厌沙虫 `insatiable_progress`（层在事件内部，外部写无效）。
  ⚠ 还有一个**硬限制**：第一幕有 `act1_*` / `act1_b1` 两套 bank，按地图二选一加载；
  不在本幕 bank 里的事件**加载不出来**（试过 `load_act_bank` 也不行）⇒
  墨影幻灵那首给碎屑储**放不出来**，v0.2.381 已退回瀑布巨兽 + `waterfall_giant_progress` 分段。
  用户已同意这条**先不动**。

## 4. 工作流提醒

- 改代码 → `& tools\dotnet\dotnet.exe build src\CharacterBosses.csproj -c Release -v q`（要 0 错误）
- 版本 +0.0.1（两处）→ 重 build → 重打 PCK（`content/` 变了才需要）→ 拷三件到两个目录
  （工作区外，需要 `danger-full-access`；游戏开着时 dll 被占用）
- 启动游戏用 `tools\launch_sts2.ps1`（`steam://run/2868840`），**别直接跑 exe**（用户指出会被 Steam 拦）
- 关游戏：`Stop-Process -Name SlayTheSpire2 -Force`（`close_sts2.ps1` 里的 `tskill` 已不存在）
- 本沙箱里 `steam://` / `steam.exe -applaunch` 都起不动游戏 ⇒ 要用户自己启动；
  `curl` 访问飞书会报 `schannel SEC_E_NO_CREDENTIALS`（受限 shell 的问题，不是脚本问题）
