# 交接：亡灵契约师 Boss 的「奥斯提」朝向问题（未解决，纯视觉）

> 写给**下一个会话**。这份文件是为了让接手者**不要再重走已经试过的六条死路**。
> 除这一条外，亡灵契约师 Boss 的其它功能都已完成并实机验证（见 §7）。

---

## 1. 一句话问题

第一幕 Boss「亡灵契约师」的随从**奥斯提**（Osty）在敌方阵营，**立绘朝右**；
需要它**朝左**（和她同向、背对屏幕右侧）。**纯视觉，不影响任何战斗逻辑。**

---

## 2. 项目与工具链（必须项）

| 项 | 值 |
|---|---|
| 工作目录 | `D:\Sts2ModProject\Downfall Expansion Vanilla Characters as Bosses` |
| 游戏 | `D:\SteamLibrary\steamapps\common\Slay the Spire 2`（Godot 4.5 mono / C#） |
| mod 目录 | `<游戏>\mods\CharacterBosses\`（DLL / pck / manifest 三者同名） |
| 编译 | `tools\dotnet\dotnet.exe build src\CharacterBosses.csproj -c Release` |
| 安装 | 见 §3（**必须核对 SHA256**） |
| 日志 | `%APPDATA%\SlayTheSpire2\logs\mod_log.txt`（UTF-8，改代码路径优先信日志） |
| 反编译 | `tools\dotnet\dotnet.exe tools\ilspy-cli\bin\Release\net10.0\ilspycli.dll type references\sts2.dll <全名>`（另有 `members` / `method` / `search`） |
| 参考程序集 | `references\sts2.dll`（**比游戏旧**，部分成员是 internal，需反射） |

### 游戏内操作（已验证可用）

- **STS2MCP（端口 15526）能用**：
  - `GET http://localhost:15526/api/v1/singleplayer` 读状态；
  - `POST` 同一 URL，body `{"action":"end_turn"}` 结束回合；
  - `{"action":"play_card","card_index":N}` 出牌，可加 `"target":"OSTY_0"`；
  - 注意：`combat_end_turn` / `combat_play_card` 是**旧动作名，会返回 Unknown action**。
- **STS2MenuControl（8081）是坏的**：`Could not load type LobbyPlayer` → **无法程序化开新局/点继续**，
  只能让用户手动点一次「新游戏 / 继续」。之后全程可用 MCP 驱动。
- 调试开关：`<mods>\CharacterBosses\charbosses_debug.txt`，内容写 `necrobinder` 可让**第一幕普通战斗**直接变成她
  （省掉整幕；测完记得清空）。也接受 `ironclad/silent/defect/necrobinder/regent/cycle`。
- 用户当前存档：玩家 999 血（作弊），所以可以放心大胆地打。

---

## 3. 安装（踩过坑，照做）

`install-mod.bat` 在沙箱里**会静默失败**（`copy /Y … >nul` 把错误吞掉），实测出现过"改了代码但装的是旧 DLL"。
可靠流程：

```powershell
# 1) 构建
tools\dotnet\dotnet.exe build src\CharacterBosses.csproj -c Release -v:q --nologo
# 2) 关游戏（DLL 被占用会失败）
Get-Process SlayTheSpire2 | Stop-Process -Force
# 3) 复制（写游戏目录需要 danger-full-access 权限）
Copy-Item src\bin\Release\CharacterBosses.dll "<游戏>\mods\CharacterBosses\" -Force
# 4) 核对
(Get-FileHash src\bin\Release\CharacterBosses.dll -Algorithm SHA256).Hash
(Get-FileHash "<游戏>\mods\CharacterBosses\CharacterBosses.dll" -Algorithm SHA256).Hash
```

**改了 `content\localization\**` 或 `content\` 下任何文件**时，还要重打 pck：

```powershell
& tools\godot\Godot_v4.5.1-stable_mono_win64.exe --headless --path tools\pck_builder `
  --script tools\pck_builder\build_pck.gd -- `
  "<mods>\CharacterBosses\mod_manifest.json" "<mods>\CharacterBosses\CharacterBosses.pck" `
  "content" "CharacterBosses" "tools\art\pck_extras.txt"
```

> ⚠️ 当前仓库源码比游戏里装的 DLL **新一版**（源码里已停用无效的镜像尝试）。首次安装前先按上面流程装一次。

---

## 4. 相关代码（全部在 `src/Hooks/`）

| 文件 | 作用 |
|---|---|
| `Core/BossOsty.cs` | **本次主要战场**：每 Boss 回合清理死宠物、召唤兜底、爪牙标记、摆位、血条强制显示、朝向 |
| `Core/NecrobinderScript.cs` | 她的 11 回合剧本 + 遗物 + 每回合能量覆盖 |
| `Powers/NotYetPower.cs` | 特殊效果「时候未到」（格挡↔灾厄、临死续命） |
| `Core/BossDefs.cs` | `Necrobinder` 定义（血 66、遗物、剧本、特殊效果） |
| `Encounters/CharBossEncounters.cs` | `ActTable` 第一幕 = `{3,3,3}`（全是她） |
| `docs/plans/charbosses.md` §11.41 | 这一整轮的设计背景 + 踩坑记录（含本条问题的六条死路） |

---

## 5. 已经弄清的机制（接手必读，这些都是反编译+实测确认的）

1. **召唤与归属**
   - 缚魂命匣（`BoundPhylactery`）走 `OstyCmd.Summon(ctx, summoner, amount, source)`；
     `PlayerCmd.AddPet<Osty>` 内部 `CreateCreature(monster, player.Creature.Side, null)` → 它**是敌方阵营**。
   - `Player.Osty => PlayerCombatState.GetPet<Osty>()`，取 `Pets` 里**第一只** Osty。
   - **坑**：召唤出来的那只**不会自动进 `Pets`**，必须用 `SummonResult.Creature` 反射塞进
     `PlayerCombatState._pets`，否则 `shadow.Osty` 永远 null（表现为"一直是死的、碎骨/出击空转"）。
   - **坑**：碎骨（`BoneShards`）会杀死自己的 Osty，死的那只**仍留在 `CombatState._allies/_enemies` 里** →
     每个钩子广播都要遍历它，**回合越久越卡**（已修：死宠物从这两个 list 里移除）。

2. **视觉节点**
   - 随从节点被 `NCombatRoom.AddCreature` 放进**玩家侧容器**（`PlayerContainer`），所以和 Boss 不同父级；
     现在代码会把它 `Reparent` 到 `EnemyContainer`（只动节点，不动 `Creature.Side` / 战斗列表）。
   - 血条/状态栏：外层是 **`NCreatureStateDisplay`（名字就叫 `HealthBar`）**，对"非本地玩家的随从"
     会 `visible=false / alpha=0`；里面 `NHealthBar`、`NPowerContainer`、`NameplateContainer` 是好的。
     **修法**：只把外层容器和 `NameplateContainer` 的 `Visible=true / alpha=1`，
     **不要整棵子树拉 alpha**（会把血条背景漂白）。
   - 奥斯提的立绘是 **Spine 网格**（子节点是 `@SpineMesh2D@xxxx`），**没有 `Body` 节点**
     （`Body` 是角色本体立绘的结构）。

3. **朝向相关（本条问题的核心证据）**
   - 写了一个探针，每回合采样 `NCreature.Visuals.Scale.X`，实测：
     ```
     turn=1  Visuals.Scale.X  NaN → 1.007
     turn=2  1.007 → 1.000
     turn=3  1.000 → 1.007
     ```
     **始终为正**。写它的是 `NCreature.OstyScaleToSize(float health, double duration)` →
     `DoScaleTween(Vector2 scale)`：**血量一变就补间把 Visuals.Scale 设成 `Vector2(正, 正)`**。
     所以"翻 Visuals"必然被补间转回去（用户看到的现象：出场向左 → 慢慢转回右边）。

---

## 6. 已经试过并**失败**的六条路（别再重复）

| # | 做法 | 结果 |
|---|---|---|
| 1 | 翻整个 `NCreature.Scale.X` | 立绘朝左了，但**血条/名字牌文字全部镜像**（HUD 是 NCreature 的直接子节点） |
| 2 | 只翻 `NCreature.Visuals.Scale.X` | 朝左只维持一瞬，随后被 `OstyScaleToSize` 的血量补间写回正数 |
| 3 | 翻 `Visuals` 下的 `Body` 子节点 | **没有 `Body` 节点**（Spine 网格），等于没做 |
| 4 | 把节点 `Reparent` 到 `EnemyContainer` | 位置/层级对了，**朝向不变**（容器不参与它的朝向判定） |
| 5 | `[HarmonyPatch(NCreature,"DoScaleTween")]` 前缀把 `scale.X` 取负 | 未生效（补丁似乎没命中/未被调用，行内没有日志输出） |
| 6 | 在 `Visuals` 内插 `Scale=(-1,1)` 的镜像容器，把子节点搬进去 | **视觉无变化**（说明朝向固化在 Spine 骨骼内部，父级缩放被 Spine 自己的骨架覆盖/忽略） |

另外还试过"每 0.1 秒把 `Visuals.Scale.X` 掰回负数"的循环：能生效但**肉眼可见来回翻转**（先左、慢慢转右、再被掰左），已删除。

---

## 7. 已经**完成并验证**的部分（别动）

- 第一幕三个 Boss 槽全是她（`ActTable` 第一幕 = `{3,3,3}`）；血 **216**（定义 66 + 涅奥祝福 150）。
- 11 回合剧本（7～11 循环），每循环额外升级一对牌；每回合能量可用 `BossTurn.Energy` 覆盖。
- 特殊效果「时候未到」两条（格挡抵灾厄；临死用格挡+奥斯提生命续命）。
- 意图：灾厄=红↓；碎骨/友谊带**牺牲**（蓝色↓，`SacrificeIntent`）；能量汲取=攻势+强化+红↓；奥斯提牌不吃她的负力量。
- 遗物时机：船夹板（`HornCleat`）第 2 回合、闪亮口红（`SparklingRouge`）第 3 回合
  —— 真凶是它们挂 `AfterBlockCleared` 且比对 `PlayerCombatState.TurnNumber`，而我们曾把它设成"真实回合+1"。
- 奥斯提：只剩一只、每回合加血、碎骨杀死后能复活、血条与状态栏常显（文字正向）、尸体不堆积、
  不抢玩家的宠物（`PetOwner` 校验）、**回合末卡顿已根治**（死宠物移出战斗列表）。
- **爪牙（`MinionPower`）**：用户明确纠正 —— **原版爪牙是可以被选中、可以被伤害的**，
  不要把它从 `HittableEnemies` 里滤掉（曾错误地加过这个补丁，已删除）。

---

## 8. 建议的下一步（给接手者）

优先顺序：

1. **找到真正的朝向写入点**。线索：
   - `NCreature` 的公开成员里**没有** `SetFacing` / `FaceTowards` / `Flip` 之类（已 `members` 扫过），
     所以朝向大概率在 **Spine 绑定层**（`MegaCrit.Sts2.Core.Bindings.MegaSpine.*`、`MegaSprite`）里设置
     （类似 `Skeleton.ScaleX` / `skeleton.SetScaleX(-1)`）。
   - 建议：`ilspycli search references\sts2.dll "ScaleX"`、`types | Select-String Spine`，然后
     `type <Spine 类型>` 找 `scaleX` 的写入方。
2. 在 `BossOsty.MirrorAndPlace` 里，对**她的那只**（`shadow.Osty` 且 `PetOwner == shadow`）调用找到的入口。
3. 验证方式（不需要用户截图）：复用 `ProbeScale` 探针，把 Spine 骨架的 `scaleX` 也一起打出来，
   看它在 summon/加血/攻击时的取值变化；只要有一次是"我们设的负值被保留"，就说明找对了。
4. 若确实无解，就把它当已知限制：**朝向朝右，纯视觉**，并在 §11.41 的"已知限制"里写清"已穷尽节点层手段"。

---

## 9. 给下一个会话的规矩（来自用户）

1. **一次只改一处**，改完让用户验证；
2. **不许删用户要求过的功能**（卡面悬停放大、遗物顺序、能量在遗物正右方、意图版式…）；
3. **不许自造美术/字体/布局**，一律用原版节点；
4. **装完必须核对 SHA256**（`install-mod.bat` 会偶发复制旧 DLL，甚至静默失败）；
5. **别信推测**，先看 `mod_log.txt`（UTF-8）和反编译证据；
6. 用户已被这条问题消耗很多耐心 —— **不要再做"猜一种改法让他跑一次"的循环**，先拿证据再动手。

---

## 10. ✅ 已解决（2026-10-18，接手会话）—— 请先读 `docs/plans/osty-facing-body.md`

**朝向**：翻 **`NCreature.Body`** 即可，已实机验证通过（用户看图确认朝左、文字正向、跨回合稳定）。

- `NCreature.Body => Visuals.GetCurrentBody()`，而 `NCreatureVisuals._Ready()` 里是
  `_body = GetNode<Node2D>("%Visuals")`（用 `GetNode`，缺节点必抛）⇒ **任何立绘场景都有 Body**。
- **§6 #3 之所以白试**：找的是"名叫 `Body` 的子节点"；真实节点带唯一名 **`%Visuals`**，只能走 `Body` 属性拿。
  奥斯提那里它 `GetClass()=="SpineSprite"`，`@SpineMesh2D@*` 全是它的子节点（＝§5.2 看到的那些网格）。
- **为什么不会被 §5.3 的血量补间转回去**：`OstyScaleToSize` 的 `TweenProperty(Visuals,"scale",…)` 写的是
  **父级 `NCreatureVisuals`**，碰不到内层 Body。
- **游戏自己的朝向权威就是翻它**：`SurroundedPower.FaceDirection` → 对 Owner 与全部 Pets 取
  `NCombatRoom.GetCreatureNode(c).Body`，再 `Scale *= (-1,1)`。本 mod 的 `BossVisuals.FaceThePlayer` 同理。
- ⚠ `Body.Scale` 的绝对值是 **0.26**（场景里作者设的），镜像只取负号、**不要写成 -1**。

**站位**：旧「镜像玩家那只奥斯提坐标 + 上移 100」已弃用。改为原版公式
`NCreature.GetOstyOffsetFromPlayer` 的 **X 镜像**版：`Right * 主人Hitbox宽/2 + MinOffset.Lerp(MaxOffset, hp/150)`，
`MinOffset=(150,-75)` / `MaxOffset=(250,-75)` ⇒ 敌方侧站她**左手边、低 75**。
实机 offset = `(-255.67, -75)`（她的 Hitbox 宽 ≈ 211）。微调只用 `BossOsty.OstyPositionNudge`。

