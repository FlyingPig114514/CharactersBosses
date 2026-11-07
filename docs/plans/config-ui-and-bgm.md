# 配置界面 + 音乐替换总规格（用户 2026-10-28 定稿）

> 本文件是**唯一权威记录**。
> **状态：已实现（v0.2.121，2026-10-28）** —— 下面 §C 的待办已全部查清并落地，答案见 §D。
> 实现位置：`src/Hooks/Core/BossBgm.cs`（曲目）、`src/Hooks/Core/BossConfig.cs`（配置存盘）、
> `src/Hooks/Config/BossConfigModConfig.cs` + `BossConfigBaseLibBootstrap.cs`（**BaseLib** 配置界面）。

## 配置界面用 **BaseLib**（用户 2026-10-28 第二次确认）

第一版是自绘 Godot 面板（F9 呼出），用户反馈「和原版不契合」⇒ 已整块删除，改用 BaseLib：

- 入口：**主菜单 →「模组配置」→ CharacterBosses**（BaseLib 注入的子菜单，外面那套外壳/字体/主题全是原版）。
- 实现：`BossConfigModConfig : SimpleModConfig`，override `SetupConfigUI` 往 BaseLib 给的容器里塞行；
  注册由 `BossConfigBaseLibBootstrap`（挂在 `NMainMenu._Ready` 后缀）调 `ModConfigRegistry.Register`。
- **BaseLib 是可选依赖**：csproj 里 `Private=false`，运行期先查程序集在不在再碰它的类型
  （`NoInlining` 隔开），没装 BaseLib 时静默跳过、只保留手改 `charbosses_config.json`。
- 数据仍然只在 `charbosses_config.json`（地图清单是**动态**的，装不了 BaseLib 属性的表达方式；
  而且这个文件让「没装 BaseLib 也能玩」成立）。
- 已知外观限制（**不是本模组的 bug**）：在 1706×1066 这种窗口里，BaseLib 的配置面板右侧会被窗口裁掉
  （拿崩坠自己的页签对照实验过，一模一样；面板底部有横向滚动条）。
  所以「当前是开还是关」写在**左边文字**里，右侧那颗按钮只是操作入口。

> **v0.2.122 更新（用户 2026-10-28：要能打勾的框）**：
> A1 地图与 A3 都改成**原版勾选框**了。BaseLib 的 `NConfigOptionRow` 会把控件排到行尾
> （该窗口下落到屏幕外），而且裸 `new NTickbox()` 没有视觉子节点（图来自 `res://scenes/ui/tickbox.tscn`），
> 所以现在**自己搭 `HBoxContainer` 行**（控件在左、文字 ExpandFill），勾选框 = 普通 `Button`
> （`Flat`+`ToggleMode`）切换游戏自己的两张贴图
> `res://images/atlases/ui_atlas.sprites/checkbox_{ticked,unticked}.tres`。
> 每行的文字里仍然重复写一遍状态（`已替换` / `保持原版`），双保险。

## A. 配置项

### A1. 希望被替换的地图（按 3 幕列出当前所有地图）
默认（只装本模组）：
- 第一幕：密林 ✔ / 暗港 ✔
- 第二幕：巢穴 ✔
- 第三幕：荣耀 ✔
装了增加地图的模组时要**动态列出更多地图**（遍历该幕的地图模型，而不是写死四个）。
原版这四个地图**默认全开**（开启 = 该地图的 Boss 会被替换）。

> 实现：`BossConfig.MapsByAct()` 遍历 `ModelDb.ActsByIndex`（真·动态，装了加地图的模组会列出来），
> 存盘在 `charbosses_config.json` 的 `maps` 里（缺项按「开」处理）。
> 生效点只有一个：`CharBossEncounters.ForVanillaBoss` 开头 —— 四个替换切点全都走它。

### A2. 同一幕的 Boss 混合匹配 —— 默认开、**锁定**（不可关）
理由：Boss 数量不足（一层只有 5 个），无法单独匹配。

> 实现：`BossConfig.MixedMatching` 恒为 `true`，界面里那颗按钮 `Disabled = true`，写盘时也强制 true。

### A3. Boss 战 BGM 来源（**三选一**）
1. **使用原版其他 Boss 的音乐** ✔ 默认选中
2. 使用一代 boss 战的音乐 ✘（默认关）
3. 使用本模组自制的音乐（**锁定关闭**，暂无自制音乐）

> 实现：`BossConfig.BgmSource`（1/2；写 3 会被夹回 1）。②只覆盖那三只故障机器人（一代 Boss1/2/3 的 ogg）。

## B. 音乐对照表（A3 = 选项 1「原版其他 Boss 的音乐」时生效）

实际填进 `BossBgm.FmodByEncounter` 的表（**事件名不是猜的**，全部抄自原版同类遭遇的
`EncounterModel.CustomBgm`，见 §D）：

| 我们的 Boss（遭遇 id） | 曲目事件 | 来源 |
|---|---|---|
| 愤怒战 `IRONCLAD_ANGER` | `event:/music/act1_boss_the_kin` | 同族小队 |
| 碎屑储 `REGENT_DEBRIS` | `event:/music/act1_b_boss_waterfall_giant` | 瀑布巨兽（**用户 2026-10-28 改口径**，原来墨影幻灵；分段参数无效故换掉） |
| 奇巧猎 `SILENT_SLY` | **保持原版** | 乐加维林族母**没有专属曲目**（见 §D-1） |
| 灾厄骨 `NECROBINDER_DOOM` | `event:/music/act1_b_boss_soul_fysh` | 灵魂异鱼 |
| 黑球机 `DEFECT_DARK` | `event:/music/act1_b_boss_waterfall_giant` | 瀑布巨兽 |
| 涂毒猎 `SILENT_ENVENOM` / 灵魂骨 `NECROBINDER_SOUL` / 铸剑储 `REGENT_FORGE` | `event:/music/act2_boss_kaiser_crab` | 帝皇蟹 |
| 物理机 `DEFECT_CLAW` / 放血战 `IRONCLAD_BLOOD` | `event:/music/act2_boss_kaiser_crab` | 帝皇蟹（**用户 2026-10-28 改口径：二层全部用帝皇蟹**） |
| 炼药猎 `SILENT_BREWER` | `event:/music/act3_boss_queen` | 女王（瓶中精灵后二阶段） |
| 易伤战 `IRONCLAD_VULN` | `event:/music/act3_boss_queen` | 女王（恒一阶段） |
| 其余全部 | `event:/music/act3_boss_queen` | 永世沙漏（原版 Aeonglass 就是这个事件） |

## C. 待办 / 阻塞（**已全部解决**，答案见 §D）

1. ~~抽出 `Music.bank` 找事件名~~ → 不需要：事件名直接写在原版遭遇的 `CustomBgm` 里。
2. ~~无厌沙虫几种分段~~ → bank 里 `STS2_Hive_Boss_Insaitable_v2_Layer1..Layer5` = **5 层**
   （参数 `insatiable_progress` 的 max 在 act2_a1.bank 里读出来是 **10**）⇒ 每 **20%** 生命一层。
3. ~~女王一/二阶段事件~~ → 同一事件的两段，用 `UpdateMusicParameter("queen_progress", 1|2)` 切；
   触发点：炼药猎的**瓶中精灵**（`BossFairyInABottlePatch`）。
4. ~~墨影幻灵的分段/重音机制~~ → `UpdateMusicParameter("vantom_progress", 1|2|3|5)`，
   **3 = 肢解的重音**（`Vantom.DismemberMove` 里就是写 3）⇒ 碎屑储的彗星照抄。
5. ~~配置界面选型~~ → 不用 BaseLib（csproj 没引用它，不能假定玩家装了）⇒ 自绘 Godot 面板 + F9。

## D. 关键实证（别再重查）

0. **`<boss>_progress` 这类 FMOD 参数在我们自己播的曲目上不起作用**（2026-10-28 三场实测）：
   - 血 18.5% ⇒ 写 `insatiable_progress=8`，`UpdateMusicParameter` 返回成功，音乐毫无变化；
   - 同样用 `update_global_parameter`（反射拿 `_proxy`）交替写 0/10，`已发送=True`，依然毫无变化；
   - 而 `PlayCustomMusic` 换曲（换成知识恶魔）**立刻能听出来** ⇒ **唯一有效的手段是换曲目**。
   - 背景：`STS2_Hive_Boss_Insaitable_v2_Layer1~5` 是**同一个事件内部的 5 个音频层**（bank 里能查到），
     原版也只在沙虫死亡时写一次 10 —— 那条很可能本来就没接线。
   - 结论：**不要再基于这些参数设计效果**；女王 `queen_progress` / 墨影幻灵 `vantom_progress`
     的调用保留只是为了和原版写法一致（同样不起作用，但无害）。
     二层因此按用户口径**统一用帝皇蟹**（一首到底，不做分段）。
1. **乐加维林族母没有专属 Boss 曲目**：`LagavulinMatriarchBoss` 是唯一一个**不覆盖 `CustomBgm`**
   的原版 Boss（其余 10 个都覆盖了），它进 Boss 房放的是本幕默认音乐
   （`ActModel.BgMusicOptions`：密林 `act1_a1_v1`/`act1_a2_v2`、暗港 `act1_b1_v1`、
   巢穴 `act2_a1_v2`/`act2_a2_v2`、荣耀 `act3_a1_v1`/`act3_a2_v1`）。
   ⇒ 奇巧猎也「不接管」，听到的和打族母时一致。
2. **永世沙漏没有独立曲目**：`AeonglassBoss.CustomBgm => "event:/music/act3_boss_queen"`。
3. 曲目**只在当前幕的 bank 里**（`ActModel.MusicBankPaths` 只加载当前幕），所以
   「第二幕的事件名在第一幕硬播」= 找不到事件；而 `PlayCustomMusic` 会先 `stop_music`
   ⇒ 结果是**整场没有音乐**。为此每条曲目都带「属于第几幕」，幕不对就不接管。
4. 无厌沙虫原版**只在死亡时**写 `insatiable_progress = 10`（平时不动）——所谓「沙坑倒计时越激昂」
   在原版代码里并不存在，所以这里按用户口径改成「玩家血越低越激昂」：`(1 - 最低玩家HP%) × 10`。
5. 原版 FMOD 参数取值（`grep UpdateMusicParameter` 反编译所得）：
   `queen_progress` 1/2/5、`test_subject_progress` 1/2/5、`vantom_progress` 1/2/3/5、
   `insatiable_progress` 10（死亡）。
