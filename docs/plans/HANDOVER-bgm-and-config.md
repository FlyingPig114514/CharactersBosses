# 交接报告：BGM 替换 + 配置界面（写给下一个对话）

> 本文件是**给新会话看的唯一入口**。先读它，再读 `docs/plans/config-ui-and-bgm.md`（规格原文）
> 与 `.dshwolf/STATUS.md` 的本轮 108–118（近两周改动史）。

## 0. 一句话现状

**代码已接好、只差「曲目名填表」**：`BossBgm` 已能按遭遇切 BGM、能自播一代 ogg、能调
`NRunMusicController`；但二代曲目（FMOD 事件名）目前只挖到**内部标识**，映射表里还是空串，
所以现在实际表现是「碎屑储/愤怒战照旧播原版音乐」。配置界面（A1/A2/A3）尚未开始。

## 1. 已完成（已在游戏里跑着）

- v0.2.118 起新增 `src/Hooks/Core/BossBgm.cs`：
  - 找 `NRunMusicController`（场景树递归）；
  - 一代曲目：`AudioStreamOggVorbis.LoadFromFile` → 失败退 `ResourceLoader.Load<AudioStream>`
    → **载入成功才** `StopMusic()` 压掉 FMOD（否则保留原版，避免整场静音）；
  - 二代曲目：`PlayCustomMusic("<事件名>")`；
  - 战斗结束/离房：`BeforeRemovedFromRoom` → `BossBgm.Stop()`（停自播 + `StopCustomMusic()`）。
- 接线点：`BossHookBridge.SideTurn` 的战斗开始处（`BossGamg.OnCombatStart(BossLineups.CurrentEncounterEntry())`）
  与 `CharBossMonster.Descriptor.BeforeRemovedFromRoom`。
- 一代三首 ogg 已解出并打进 PCK：
  `tools\art\audio\STS_Boss{1,2,3}_NewMix_v1.ogg` → `res://CharacterBosses/audio/…`（PCK 现 12.8MB）。
  ⚠ 按用户口径**现在不要用它们**（要等 A3 选项 2「一代音乐」被开启）。

## 2. 关键发现（踩过的坑，别重走）

### 2.1 二代音乐 = FMOD，不是文件
- 游戏 pck：`D:\SteamLibrary\steamapps\common\Slay the Spire 2\SlayTheSpire2.pck`（1.99GB）
- bank 在 `res://banks/desktop/`：`Master.bank`、`Master.strings.bank`、
  `act1_a1/a2/b1.bank`、`act2_a1/a2.bank`、`act3_a1/a2.bank`、`ambience/sfx/temp_sfx.bank`
- 已抽出到 `.recon\*.bank`；**抽法**：`tools\art\extract_pck_file.gd`
  （`godot --headless --path tools\art --script extract_pck_file.gd -- <pck> <res://路径> <输出>`）
  ⚠ 手动解析 pck 头（写 BinaryReader）**失败**，不要重试；直接 `ProjectSettings.load_resource_pack` + `FileAccess`。
- 曲目清单已导出：`.recon\music-catalogue.txt`（302 条）、`.recon\music-tracks.txt`（104 条）

### 2.2 命名规律
```
曲目：STS2_<Region|A数字>_Boss_<Boss>_<段>_v<N>      例 STS2_A1_Boss_Vantom_A2_v4 / STS2_Overgrowth_Boss_Kin_v2
事件：event:/music/boss_<内部名>                     例 event:/music/the_kin
参数：<内部名>_progress                              例 the_kin_progress / queen_progress / the_insatiable_progress
```
已确认内部名：`the_kin`（同族小队）、`vantom`（墨影幻灵）、`lagavulin_matriarch`（乐加维林族母）、
`soul_fysh`、`waterfall_giant`、`kaiser_crab`、`queen`、`the_insatiable`（无厌沙虫）、
`knowledge_demon`、`ceremonial_beast`、`test_subject`、`est_subject`。
**待查**：永世沙漏 ⇒ 在 `act3_a1/a2.bank` 里按 `doormaker` 扫（门扉缔造者 = Doormaker）。

### 2.3 音乐控制器 API（反编译实证）
```
NRunMusicController.PlayCustomMusic(string)     → _proxy.Call("update_music", customMusic)   // 传事件名
NRunMusicController.UpdateCustomTrack(string, float label) → _proxy.Call("update_custom_track", …)  // 切段
NRunMusicController.StopCustomMusic() / StopMusic()
private MusicProgressTrack GetTrack(RoomType)   // 按房间类型选曲，**不按遭遇** ⇒ 不能靠改它
```

## 3. 待做（无阻塞、无待决策）

1. 扫 `act3_*.bank` 找 `doormaker`（永世沙漏兜底曲）；
2. 数 `the_insatiable` 的 progress 分段数；
3. 填 `BossBgm.FmodByEncounter`（下表 15 行），并**删掉/停用**一代 ogg 的自动启用（改为 A3 选项 2 才用）；
4. 沙虫：`the_insatiable_progress = 1 - 玩家当前HP%`（**满血最远最平静、濒死最近最激昂**，用户已确认），
   分段按 `1/N` 均匀切（4 段 → 每 25%；5 段 → 每 20%）；
5. 女王：炼药猎「瓶中精灵」后切 `STS2_A3_Boss_TheQueen_B1_v1`（二阶段），易伤战全程 `_A1_v1`；
6. 碎屑储：音乐随回合变化（4 回合循环，循环内第 3 回合重击 ⇒ 切段）；**彗星**打出对齐音乐重音（照抄墨影幻灵「肢解」）——
   墨影幻灵的分段/重音实现还没查（`UpdateCustomTrack` 的 label 用法 + Vantom 的 `Transit_*Bar*` 事件）；
7. 配置界面（BaseLib config API 首选）：
   - A1 按 3 幕列出**当前所有地图**（动态遍历该幕地图模型，不能写死 4 个）；原版四个默认开；
   - A2「同幕混合匹配」默认开、**锁定不可关**；
   - A3 三选一：①原版其他 Boss 音乐（**默认**）②一代 Boss 音乐 ③本模组自制音乐（**锁定关闭**）。

## 4. 音乐对照表（A3 = 选项 1 时）

| 我们的 Boss | 曲目来源 |
|---|---|
| 愤怒战（一层铁甲） | 同族小队 `the_kin` |
| 碎屑储（一层储君） | 墨影幻灵 `vantom`（随回合切段 + 彗星对重音） |
| 奇巧猎（一层静默） | 乐加维林族母 `lagavulin_matriarch` |
| 灾厄骨（一层亡灵） | 灵魂异鱼 `soul_fysh` |
| 黑球机（一层故障） | 瀑布巨兽 `waterfall_giant` |
| 涂毒猎 / 灵魂骨 / 铸剑储 | 帝皇蟹 `kaiser_crab` |
| 物理机 / 放血战 | 无厌沙虫 `the_insatiable`（按玩家 HP% 驱动 progress） |
| 炼药猎 | 女王 `queen`（瓶中精灵后切二阶段） |
| 易伤战 | 女王 `queen`（一阶段） |
| 其余全部 | 永世沙漏（= 三层默认 Boss 音乐 或 Doormaker 曲） |

## 5. 开发/验证流程（照旧）

```
$env:DOTNET_ROOT="<repo>\tools\dotnet"
tools\dotnet\dotnet.exe build src\CharacterBosses.csproj -c Release -v q      # 必须 0 错误
# 改 DLL 就 +0.0.1：src\Hooks\Core\BuildInfo.cs 的 Tag 与 src\CharacterBosses.json 的 version 同时改
powershell -File tools\close_sts2.ps1 ; 拷贝 dll/json（+pck 若改了内容）到
  D:\SteamLibrary\steamapps\common\Slay the Spire 2\mods\CharacterBosses\
powershell -File tools\launch_sts2.ps1 ; 看 %APPDATA%\SlayTheSpire2\logs\{mod_log.txt,godot.log}
```
- 改 C# 源码**不要**用 PowerShell 双引号拼多行（`\r\n` 会被吞进注释/字符串，之前因此坏过两次），用编辑工具；
- PCK 重建：`tools\art\pck_extras.txt` 两列必须是「`res://` 路径|<磁盘**绝对**路径>」，
  相对路径会被静默跳过（体积不变就是没打进去）；
- 打包：`.dist\CharacterBosses\` 放 dll/json/pck/安装说明.txt → `Compress-Archive` 成 `.dist\CharacterBosses-vX.zip`。

## 6. 其它挂着的（非 BGM，别忘了）

1. **放血战血量**应按「他作为第几幕 Boss」(`TierAct`) 算（控制台在一层打也应是 380），现在按玩家当前幕 ⇒ 230；
2. **放血战自伤不掉血**（血墙/御血术/突破/烙印）待查；
3. **奥斯提被上毒**（崩坠召唤两个召唤物的玩家反馈）：需 `mod_log` 里有没有「奥斯提不吃毒」判断走哪条路径；
4. **祭品抽牌**：`DrawBlockEntry` 那行为真后未见复验（若仍抽，改从手牌差集删除）；
5. 灵魂骨的「回合开始抽牌」归类已修（v0.2.117），待玩家复验。
