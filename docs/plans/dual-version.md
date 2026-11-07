# 双版本（v0.107 正式版 / v0.111 beta）构建说明

> 状态：**代码已完成，两个 dll 都能编出来**（2026-11-05）。107 实机加载验证需要人手动启动游戏确认。

## 为什么必须「两个 dll」

v0.107 和 v0.111 的 `sts2.dll` 不是简单的加接口，而是**改签名 + 删类型**：

| 差异 | v0.111 | v0.107 |
|---|---|---|
| `Hook.ModifyDamage` | 11 参（多 `CardPlay`） | 10 参 |
| `CardPlay.Player` | 有 | **没有**（用 `CardPlay.Card.Owner`） |
| `OrbCmd.Passive` | 4 参（`countAffectedByHooks`） | 3 参 |
| `Shiv.CreateInHand` | 4 参（`creator`） | 3 参 |
| `CreatureCmd.LoseBlock` | `(ctx, creature, amount, dealer)` | `(creature, amount)` |
| `CharacterModel.GenerateAnimator` | `(MegaSprite, Creature)` | `(MegaSprite)` |
| `NCreatureVisuals.Body` | 属性 | `GetCurrentBody()` |
| `FeralPower` 的「打完去哪」钩子 | `ModifyCardPlayResultLocation`(+`CardLocation`) | `ModifyCardPlayResultPileTypeAndPosition`(+`(PileType, CardPilePosition)`) |
| `Mod.assemblies` / `Mod.workshopId` | 有 | 只有 `Mod.assembly`，没有 workshopId |
| `PoisonPower.Trigger` / `Hook.BeforeSideTurnEnd` / `CardPileCmd.DrawInternal` / `CombatManager.AwaitTurnEndAndSwitchSides` | 有 | **没有**（旧版分别对应 `AfterSideTurnStart` / `Hook.BeforeTurnEnd` / `CardPileCmd.Draw`） |
| `OneForAll` / `OneForAllPower` / `BladeSymphony` / `Hibernate` / `HibernatePower` / `ActBankLoadRetry` | 有 | **整类都没有** |

只要有一个「类型级」引用缺失，游戏加载模组时 `Assembly.GetTypes()` 就抛
`ReflectionTypeLoadException`，**整个模组加载失败**（不是某个功能坏掉）。

## 怎么编

```powershell
# 正式版（v0.107.1）
dotnet build src\CharacterBosses.csproj -c Release -p:Sts2Target=107
# → src\bin\Release-107\CharacterBosses.dll

# beta（v0.111）
dotnet build src\CharacterBosses.csproj -c Release -p:Sts2Target=111
# → src\bin\Release-111\CharacterBosses.dll
```

然后把对应版本的那个 `CharacterBosses.dll` + `CharacterBosses.json` 拷进
`<游戏目录>\mods\CharacterBosses\`（PCK 两版通用，不用换）。

⚠ 参考程序集放在 **`.refs\sts2_107.dll` / `.refs\sts2_111.dll`**，**不要**再往 `references\` 里放第二份
sts2 副本：两个文件的程序集简单名都是 `sts2`，MSBuild 的 `ResolveAssemblyReference` 会抓到错的那份，
表现为「编译期看得见旧版没有的类型、运行时才炸 TypeLoadException」（本次踩过）。

## 源码里的写法

- 版本相关调用一律走 `Hooks/Core/GameCompat.cs` 的封装：
  `GameCompat.ActorOf(cardPlay)`、`GameCompat.ModifyDamage(...)`、
  `GameCompatExtra.CreateShivInHand / CreateShivInOne / OrbPassive / LoseBlock`；
- 纯补丁类里的差异用 `#if STS2_107 / #else / #endif`；
- 旧版没有的**内容**（一心化万 / 冬眠 / 刀刃交响曲 / 形态特效 / 立刻触发中毒）在 107 上**整段跳过**，
  由 `Core/GameCompat` 的运行时探测 + `[HarmonyPrepare]` 守卫兜住，并记一行日志。

## 自检（不需要启动游戏）

仓库里带了两把小工具（都是独立的小 console 工程，用 `tools\dotnet` 编）：

### ① `tools\refcheck` —— 静态检查「引用是否都存在」

```powershell
dotnet tools\refcheck\refcheck.csproj -c Release
dotnet tools\refcheck\bin\Release\net10.0\refcheck.dll src\bin\Release-107\CharacterBosses.dll .refs\sts2_107.dll
```
输出「指向 sts2 的类型引用 N 个，缺失 M 个 / 成员引用 …」。**类型缺失 = 游戏里 `GetTypes()` 直接炸
`ReflectionTypeLoadException`（整个模组加载失败）**；成员缺失 = 该调用一旦执行就 `MissingMethodException`。

### ② `tools\modloadtest` —— 离线「装进 ALC + 枚举类型」

```powershell
dotnet tools\modloadtest\modloadtest.csproj -c Release
dotnet tools\modloadtest\bin\Release\net10.0\modloadtest.dll src\bin\Release-107\CharacterBosses.dll .refs\sts2_107.dll references
```
它把目标版本的 `sts2.dll`、`GodotSharp`、`0Harmony`、模组 dll 装进同一个 `AssemblyLoadContext`，
然后 `Assembly.GetTypes()` —— 这正是游戏加载模组时的那一步。`PatchAll` 只能给出「Harmony 的
detour 引擎在当前 .NET 运行时上不支持」的环境错误，真正的补丁挂载需要在游戏里验。

### 2026-11-05 实测结果

| 组合 | 类型数 | 类型加载错误 | 备注 |
|---|---|---|---|
| `Release-107` + `sts2_107` | 612 | **0** | 目标组合，正常 |
| `Release-111` + `sts2_111` | 615 | **0** | 目标组合，正常 |
| `Release-111` + `sts2_107`（**对照组**） | 0 | **1** | 复现用户截图里的 `TypeLoadException: OneForAllPower` |

对照组证明这套自检确实能抓出「用新版 dll 跑旧版游戏」这个真实故障，而 107 目标构建已经修掉它。

`refcheck` 结果：`Release-107` 类型引用 628 / 缺失 **0**，成员引用 467 / 找不到 **0**；
`Release-111` 类型引用 635 / 缺失 **0**，成员引用 470 / 找不到 **0**。

⚠ 沙箱里**无法启动游戏**（GUI 进程创建被拦：连 `notepad` 都起不来），所以「进战斗实测」这一步
必须由人在真机上点一次启动来确认。

### ③ Harmony 补丁「绑定」检查（`tools\refcheck\*.ps1`）

`PatchAll` 里只要**一个**补丁类挂不上（目标方法找不到、或补丁方法的形参名在目标方法里不存在），
Harmony 就会抛 `HarmonyException` 并**中断后面的所有补丁** —— 那比缺个功能严重得多。
所以另外查了两类补丁：

- `list_patch_bindings.ps1` / `check_string_bindings_107.ps1`：33 处**字符串寻址**的补丁
  （`HarmonyPatch(typeof(X), "方法名")`）里，凡是绑定了非特殊形参（不是 `__instance`/`__result`/
  `__args`…）的 14 条，逐个核对参数名是否出现在 107 的对应方法上 ⇒ **14/14 全部存在**。
- `check_typed_bindings_107.ps1`：`typeof(X), nameof(Y)` 形式的补丁里有 68 条绑定了具名参数；
  逐个抽查了 107 侧的签名，`Hook.AfterCardPlayed / AfterCardDrawn / AfterPlayerTurnStart / AfterDeath /
  AfterDamageGiven / AfterCombatEnd / BeforeCombatStart / AfterCurrentHpChanged / AfterCardExhausted /
  AfterPreventingDeath / AfterSideTurnStart / BeforeCardPlayed`、`CardCmd.AutoPlay`、
  `CardPileCmd.Add / AddGeneratedCardToCombat / AutoPlayFromDrawPile`、`PowerCmd.Apply`、
  `CreatureCmd.Damage / GainBlock`、`CombatState.GetOpponentsOf` 的参数名**两版一致**。
- 另外：凡是用 `new[]{ typeof(...) … }` 显式钉住重载的补丁（`CreatureCmd.Damage`、
  `CardPileCmd.Add`、`PowerCmd.Apply`、`CreatureCmd.LoseBlock` 等），**重载签名由编译器校验**
  ⇒ 两版都对得上（这也说明这些补丁不会因为「名字相同、参数不同」而在旧版挂错重载）。

## 107 上的已知降级（有意为之）

| 功能 | 107 上的状态 |
|---|---|
| 三层事件机「一心化万」 | 跳过（旧版没这张牌/power） |
| 二层物理机「冬眠」与冰球格挡补丁 | 跳过 |
| 多人换牌：冬眠 / 刀刃交响曲 / 一心化万 三条 | 跳过（表里其余条目照常） |
| 形态特效朝左（群蛇/恶魔等） | 跳过（旧版没有 `AddFormVfx` 那套节点） |
| 「立刻触发中毒」可格挡 | 跳过（旧版没有 `PoisonPower.Trigger`） |
| 情感芯片触发球被动 | 走 3 参 `OrbCmd.Passive`（旧版没有「让镀金缆线参与」的开关，少一次额外触发） |
| 野性回手拦截 | 走旧版 `ModifyCardPlayResultPileTypeAndPosition` 那一支，等价 |
| 拦抽牌 | 走旧版 `CardPileCmd.Draw` 两个入口 |
| 麻痹减伤 | 已改成 `AbstractModel.ModifyDamageMultiplicative` 的 Harmony 补丁，两版都生效 |
