# 奥斯提（Osty）朝向修正：翻 `NCreature.Body`（`%Visuals`）

> 承接 `docs/plans/handover-osty-facing.md`。目标是**先把证据摆齐，再一次只改一处**。

## 1. 问题

第一幕 Boss「亡灵契约师」的随从**奥斯提**在敌方阵营时立绘**朝右**，需要**朝左**。纯视觉。

## 2. 已排除的路（见 handover §6，不再重复）

节点翻转 / `Visuals` 翻转 / "Body 子节点" / 容器迁移 / `DoScaleTween` 补丁 / `Visuals` 内嵌镜像容器。

## 3. 新证据（反编译 `references/sts2.dll`，全部逐字核对过）

| 事实 | 出处 |
|---|---|
| `NCreatureVisuals._Ready()` 里 `_body = GetNode<Node2D>("%Visuals")` —— 用的是 **`GetNode`（缺节点会抛异常）**，所以**任何立绘场景都必然有这个节点** | `NCreatureVisuals::_Ready` |
| `_phobiaModeBody = GetNodeOrNull("%PhobiaModeVisuals")`（可选） | 同上 |
| `IsSpineNode => _body.GetClass() == "SpineSprite"` —— 奥斯提的 `_body` **就是 SpineSprite**（它的子节点正是 `@SpineMesh2D@*`，与 handover §5.2 观察一致） | `NCreatureVisuals::get_IsSpineNode` |
| `NCreature.Body => Visuals.GetCurrentBody()`，而 `GetCurrentBody()` 返回 `_phobiaModeBody.Visible ? _phobiaModeBody : _body` | `NCreature::get_Body`、`NCreatureVisuals::GetCurrentBody` |
| **游戏自己的朝向权威**就是翻它：`SurroundedPower.FaceDirection` 对 `Owner` 与全部 `Pets` 取 `NCombatRoom.Instance.GetCreatureNode(c).Body`，然后 `body.Scale *= new Vector2(-1,1)` | `SurroundedPower::FaceDirection` + `FlipScale(Node2D)` |
| 本 Mod 的 `BossVisuals.FaceThePlayer` 已经用 `visuals.Body` 翻角色 Boss 立绘，**实机有效** | `src/Hooks/Monsters/BossVisuals.cs:60` |

### 结论 1：`Body` 不是不存在，而是**不叫 Body**

handover §6 #3 找的是**名字为 `Body` 的子节点**；真实节点是带唯一名的 **`%Visuals`**，必须走属性 `NCreature.Body` / `NCreatureVisuals.Body` 拿。这解释了那次"等于没做"。

### 结论 2：翻 `Body` 不会被血量补间抹掉

`OstyScaleToSize` 的补间是 `TweenProperty(Visuals, "scale", ...)` —— 写的是 **父级 `NCreatureVisuals` 的 Scale**（handover §5.3 的探针 1.000↔1.007 就是它）。
`Body` 是它的**子节点**，补间碰不到 ⇒ 一次镜像可以长期保持（`BossVisuals` 对角色 Boss 已实测如此）。

### 结论 3：HUD 不受影响

血条 / 名字牌 / 意图都是 `NCreature`（或其 `_stateDisplay`）的子节点，与 `Visuals.Body` 平级或在外层 ⇒ 不会镜像成反字（这是 §6 #1 的翻车点）。

## 4. 本次改动（唯一一处）

`src/Hooks/Core/BossOsty.cs`

1. 新增 `MirrorBody(NCreature)`：取 `oc.Body`（`%Visuals`），若 `Scale.X > 0` 则 `Scale = (-X, Y)`，并把 `name / class / scale` 写日志。
   幂等（已为负就不再翻），每回合摆位时调一次当兜底。
2. 新增 `ProbeBody(NCreature)`：
   - 首次打印 `Body` 的 `name / class / type / scale / 直接子节点`（**这是"Body 到底存不存在"的实机证据**）；
   - 之后 `Body.Scale.X` 一旦变化就打印 `旧 → 新`（若被写回正数，日志会立刻暴露写入时机）。
3. 在 `MirrorAndPlace` 里调用二者，并删掉那段"朝向停止尝试"的过期注释。

不动：`Visuals.Scale`（继续留给原版补间）、`NCreature.Scale`（HUD 要靠它保持正向）、战斗逻辑。

## 5. 验证方式

- 日志（`%APPDATA%\SlayTheSpire2\logs\mod_log.txt`）应出现：
  - `[CharBoss] 奥斯提 Body 探针: name=Visuals class=SpineSprite ...` ← 证明 Body 存在且是 Spine
  - `[CharBoss] 奥斯提立绘已镜像朝左 via Body ... scale=(-1.0xx, 1.0xx)`
  - 之后若出现 `Body.Scale.X 被改动: -1.0 → 1.0`，说明还有别的写入点（下一轮再定位）
- 画面：奥斯提朝左，血条/名字牌文字正向，站位不变（若镜像后水平偏移，微调 `_offset`）。

## 6. 实机结果（2026-10-18 23:0x）

朝向：**已确认修好**（用户看图确认朝左）。日志：

```
22:55:28 奥斯提立绘已镜像朝左 via Body: name=Visuals class=SpineSprite scale=(-0.26, 0.26)
22:55:28 奥斯提 Body 探针: ... 子节点=[@SpineMesh2D@3250 …, Flame, …]
22:55:45 清掉 1 只已死的奥斯提 → 新召的这只同样被翻成 (-0.26, 0.26)   ← 换节点自动补
22:55:55 她的回合开始，未出现 "Body.Scale.X 被改动"                  ← 负值保持住
```

> 注意：`Body.Scale` 的绝对值是 **0.26**（SpineSprite 在场景里作者设的），不是 1；
> 镜像只是把符号取负，幅度必须保留原值，不能写成 `-1`。

## 7. 站位（第二处，同一个函数）

用户看图后："位置不太对，往左下移一下"。

**根因**：旧做法是「镜像玩家那只奥斯提的屏幕坐标」再 `_offset.Y -= 100`，位置偏高且依赖玩家那只的位置。
**正确做法**：照抄原版公式再镜像 X —— `NCreature.GetOstyOffsetFromPlayer`：

```
Vector2.Right * 主人Hitbox宽/2 + Osty.MinOffset.Lerp(Osty.MaxOffset, hp/150)
Osty.MinOffset = (150, -75)   Osty.MaxOffset = (250, -75)
```

即原版＝「主人**右手**边、比主人**低 75**」；敌方侧 `X` 取负 → 站她**左手**边、同一高度。
（原版只对 `LocalContext.IsMe(PetOwner)` 摆位，影子玩家不满足 ⇒ 必须我们自己算。）

实机日志：`奥斯提站位: offset=(-255.66667, -75)（原版公式 X 镜像）bossPos=(1440, 680)`
（她的 Hitbox 宽 ≈ 211 ⇒ 半宽 105.7 + 150 = 255.7。）

微调入口：`BossOsty.OstyPositionNudge`（`new Vector2(左负, 下正)`），只改这一行。

## 8. 第三处：开场就不该有奥斯提（缚魂命匣的「战斗开始」召唤）

现象（用户报）：刚进战斗、她的第一个回合还没开始，她脚下就站着一只**没有血条**的奥斯提；
她的回合开始后才"正常"。

**机制（反编译 + 日志双证）**：

| 事实 | 出处 |
|---|---|
| 遗物的 `BeforeCombatStart()` 本体就是 `SummonPet()` → `OstyCmd.Summon` | `BoundPhylactery` |
| `AfterEnergyResetLate(player)` 只在 `Owner.PlayerCombatState.TurnNumber != 1` 时补召唤 | 同上 |
| ⇒ **原版就是"战斗开始先召一只"**，好让玩家第 1 回合手上就有奥斯提；描述里的"在你的回合开始时召唤1"只是文字 | 同上 |
| `CombatState.IterateHookListeners()` 遍历 `_allies/_enemies`，对**带 `Player` 的生物**把该玩家的遗物全当钩子监听者 | `CombatState::IterateHookListeners` |
| ⇒ 我们挂在 Boss 生物上的影子 Player（`ShadowPlayerFactory` 第 60-61 行反射挂的）的遗物**会被原版正常广播** | 日志「涅奥的祝福：战斗开始特殊效果」即同一广播 |

所以 `BossHookBridge.FireCombatStart` 里那句「跳过缚魂命匣」并没有拦到它 —— 那次广播不是我们发的。

**修法**：`src/Hooks/Patches/OstyNoCombatStartSummonPatch.cs`
（`[HarmonyPrefix] BoundPhylactery.BeforeCombatStart`）—— **只拦影子**（`Owner.Creature.Side == CombatSide.Enemy`），
真玩家自己的缚魂命匣照原版（他本来就该在战斗开始有一只）。
她的第一只由 `BossOsty` 在她**回合开始**召唤，与描述一致；第 2 回合起仍由
`AfterEnergyResetLate` + `BossOsty` 的每回合 Summon 负责加血，行为不变。


