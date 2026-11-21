# 地图 Boss 图标：一图标一 Boss（用户 2026-11-18 定案）

> 本文件是**地图节点图标**的唯一权威口径。改图标之前先读这一页。
> 代码入口：每个遭遇覆写 `EncounterModel.BossNodePath`（基类兜底见下）。

## 1. 用户口径（逐字）

> 目前地图图标有两个来源：1. 来自 1 代崩坠：黑球机使用…「虚空机」（`defect_void`），物理机使用…
> 「铜球机」（`defect_bronze_orbs`），能力机使用…「集中机」（`defect_focus`）；涂毒猎…（`silent_poison`），
> 刀猎…（`silent_shiv`）。新增：让奇巧猎使用 1 代 2 层 boss「镜像猎」（`silent_mirror_image`）的图标…
> 神格观用…（`watcher_divinity`）… 我即将做的红蓝观（内部名用猛虎下山的英文）用 1 代 1 层 boss
> 愤怒观（`watcher_wrath`）的图标。我没有提及的都先继续用剪影。
> **保证每一个图标只对应唯一的一个 boss，不能有一个图标对应两个 boss 的情况。**

## 2. 现在谁用哪张图（唯一映射）

| 图标（`res://CharacterBosses/images/map/<名>`） | 唯一使用者 | 遭遇类 |
| --- | --- | --- |
| `defect_void` | 黑球机（一层故障机器人） | `DefectDarkBossEncounter` |
| `defect_bronze_orbs` | 物理机 | `DefectClawBossEncounter` |
| `defect_focus` | 能力机 | `DefectAiBossEncounter` |
| `silent_poison` | 涂毒猎（二层静默猎手 1 号） | `SilentEnvenomBossEncounter` |
| `silent_snakebite` | 蛇咬猎（一层静默猎手 2 号） | `SilentSnakebiteBossEncounter` |
| `silent_mirror_image` | 奇巧猎（一层静默猎手） | `SilentSlyBossEncounter` |
| `silent_shiv` | 刀猎（三层静默猎手 1 号） | `SilentShivBossEncounter` |
| `ironclad_anger` | 愤怒战（一层铁甲战士 1 号） | `IroncladAngerBossEncounter` |
| `ironclad_strike` | 打击战（一层铁甲战士 2 号） | `IroncladStrikeBossEncounter` |
| `defect_thunder` | 电球机（一层故障机器人 2 号） | `DefectThunderBossEncounter` |
| `fool_tarot` | 塔罗愚（三层） | `FoolTarotBossEncounter` |
| `soul_bone` | 灵魂骨 | `NecrobinderSoulBossEncounter` |
| `osty_bone` | 养手骨 | `NecrobinderOstyBossEncounter` |
| `eradicate_bone` | 根除骨 | `NecrobinderEradicateBossEncounter` |
| `watcher_divinity` | 神格观（三层观者 1 号） | `WatcherDivinityBossEncounter` |
| **剪影（不覆写 = 基类 TheKinBoss 占位图）** | 其余全部 | 见 §3 |

## 3. 走剪影的（用户没说 ⇒ 先继续用剪影）

- 静默猎手：炼药猎 `SilentBrewer`、华丽猎 `SilentGrandFinale`、蛇眼猎 `SilentSnecko`、蛇咬猎 `SilentSnakebite`、基础 `SilentBossEncounter`
- 故障机器人：`DefectBossEncounter`（基类，已**删掉**原来那张 `defect_focus` 覆写）、状态机 `DefectStatus`、事件机 `DefectEvent`、电球机 `DefectThunder`、彩虹机 `DefectRainbow`
- 观者：Ω观 `WatcherOmega`、印牌观 `WatcherCreate`、保留观 `WatcherRetain`、预见观 `WatcherForesight`
- 亡灵契约师：灾厄骨 `NecrobinderDoom`、虚无骨 `NecrobinderEthereal`、血肉骨 `NecrobinderFlesh`
- 铁甲战士 / 储君 / 愚者：全部（没有任何覆写）
- 所有 `*NormalEncounter`（调试用普通战斗版）跟随各自的 Boss

## 4. 预留

- `watcher_wrath`（一代「愤怒观」那张）**已经从 jar 取出**放在 `tools\art\downfall\watcher_wrath{,_outline}.png`，
  **还没打进 PCK** —— 等「红蓝观」（内部名走「猛虎下山」的英文，建议 Key `WATCHER_RUSHDOWN`）落地时，
  按 §5 走一遍流程接到那个遭遇上。

## 5. 新增一张图标要走完整条链（缺一步地图上就不显示）

> **2026-11-21 起这条链已经脚本化了**，不要再手工拷文件：
> `tools\art\compose-batch2.ps1`（或你自己的绘图脚本）出图 → `tools\art\make-outline.ps1 -Band 5`
> 生成 `_outline` → 拷进 `import_proj` → `tools\art\register-map-icon.ps1 -Names <名>`（它会跑
> Godot `--import` 并从 `.import` 里读 ctex 的 hash 往 `pck_extras.txt` 追加 6 行，已存在就跳过）。
> 出图/挑图时用 `tools\art\make-icon-sheet.ps1` 把白+alpha 的图标拼到深底上看（直接看 png 是一片纯白）。
> 装完可以用 `tools\art\check-map-icons.gd` 做静态验证（从 pck 里 `load()` 一次主图与描边）：
> `Godot --headless --path tools\pck_builder --script <绝对路径>\check-map-icons.gd -- <mod.pck> <名...>`
> ⚠ 两个 PowerShell 坑：①`$band`/`$Band`、`$names`/`$Names` 这种**同名不同大小写**会把参数清空或
> 触发类型转换错误；②含中文的 .ps1 必须存成 **UTF-8 with BOM**，否则 PS 5.1 按 ANSI 读会报语法错。
>
> **素材口径**（2026-11-21 用户两次纠错后确认）：说到「飞镖」= **「飞镖」（Flechettes）卡图里那把飞刀**
> （`card_flechettes_big.png` → `tools\art\make-flechette-knife.ps1` 取亮部 + **只留最大连通块** ⇒
> `refs\flechette_knife.png`），**不是** `dart_tip45.png` 那对蛇头柄匕首；按比例裁剪那张图会切掉刀尖。
> 另：白色形状放在白色头像底下时，**短于头像宽度的形状会被完全盖住**（实测刀宽 60~125 的产物墨点完全相同）。

1. 从 `D:\SteamLibrary\steamapps\workshop\content\646570\1610056683\Downfall.jar` 取
   `downfallResources/images/ui/map/icon/<名>.png` 与 `.../outline/<名>_outline.png`（512×512）；
2. 存一份到 `tools\art\downfall\`，再拷进 `tools\art\import_proj\CharacterBosses\images\map\`；
3. 跑 Godot 导入（生成 `.import` + `.godot/imported/*.ctex`）：
   `tools\godot\Godot_v4.5.1-stable_mono_win64_console.exe --headless --path tools\art\import_proj --import`
4. `tools\art\pck_extras.txt` 追加 **6 行**（`res://路径|磁盘绝对路径`）：`<名>.png`、`<名>.png.import`、
   `.godot/imported/<名>.png-<hash>.ctex`，`_outline` 同样三行（hash 以生成的为准）；
5. 重打 PCK（`install-mod.bat` 会自动做；手动版见 `.dshwolf/cerebrum.md`）；
6. 目标遭遇覆写 `BossNodePath => "res://CharacterBosses/images/map/<名>"`。

> 只有 `.png` 没 `.ctex` ⇒ 地图节点图标空白/报 `Failed to load resource`；只有一张图被两个遭遇共用 ⇒
> `BossMapPointCharacterIconPatch` 按贴图路径反查遭遇时会认错，图标、名字、顶栏头像全可能错配 ——
> 这正是 §2 要「一图标一 Boss」的原因。
