# StS2「原版角色当 Boss」（charbosses）可行性侦察报告

> 目标：验证在 Slay the Spire 2（C# / Godot）里能否把每一幕 Boss 换成「原版可玩角色」，让该 Boss
> 拥有自己的**卡组**与**遗物**，并在战斗中**像玩家一样打牌**（对标 StS1 的 Downfall / 崩坠 mod 的 `charbosses`）。
>
> 本报告只做**逆向侦察**，不含 mod 代码，未修改 `src/`、`rules.md`、任何 `.csproj`。
> 证据来自 `references/sts2.dll` 的反编译（`tools/ilspy-cli`）、`references/BaseLib.dll`、游戏 pck 文件清单
> `.recon/pck_index.txt`，以及工作区已有的侦察成果。

## 0. 总体结论（先看这里）

| 判定 | 内容 |
|---|---|
| **结论** | **完全可行**。四个核心机制都存在**官方现成**的扩展点，不是 hack：<br>① Boss 替换有官方 API `MapCmd.SetBossEncounter(IRunState, EncounterModel)` + `ActModel.PullNextEncounter(RoomType)` 这个唯一漏斗（`RoomSet.Boss` 本身也是 public setter）；<br>② `MonsterModel` 是纯 C# 抽象类，mod 里的子类会被 `ModelDb` 通过 `ReflectionHelper.GetSubtypesInMods` 反射自动发现注册；连「怪物复用可玩角色立绘」都有官方先例（`Mocks.MockArtifactMonster → creature_visuals/defect`）；<br>③ 怪物「像玩家一样打牌」有官方 API `CardCmd.AutoPlay(...)`，**全链路只依赖 `Player` + `Creature` + `ICombatState`，不检查出牌者是不是真人玩家**；<br>④ 敌人回合是 `async Task` 链，`MoveState` 的动作本身就是 `Func<IReadOnlyList<Creature>, Task>` 委托，**可以直接在里面 await 连打 N 张牌，完全不需要 Harmony**。 |
| **需要妥协的点** | 1) 要让卡牌系统正常工作，必须给 Boss 造一个「影子 `Player`」；这会让 `Creature.IsPlayer` 为 true，进而污染 `CombatState.PlayerCreatures` / `Players`（回合逻辑）和 `NCombatRoom.AddCreature`（节点挂到友方容器）。**必须加 1~2 个定向 Harmony patch 兜底。**<br>2) 敌人走 `AutoPlay` 路径 ⇒ **不扣能量、不过 `CanPlay`**，Boss 的能量必须 mod 自己记账。<br>3) 需要玩家交互的原版卡（选牌类）不适合敌人使用。<br>4) 存档兼容：影子 Player 不在 `RunState.Players` 里，读档后需要重新构造。 |
| **最大 3 个技术风险** | ① **影子 Player 污染战斗状态**（回合开始抽 5 张 + 重置能量、回合结束弃手牌、`MonsterModel.PerformMove` 把 Boss 当目标）—— 必须过滤 `PlayerCreatures`；<br>② **联机不同步**：`EncounterId` 与影子 Player 都参与 checksum（`RunManager.ChecksumTracker` / `SendPostActionChecksum`），多人模式未验证 → MVP 只做单人；<br>③ **异步时序**：卡牌结算内部还会 await `CombatManager.WaitForUnpause()` / `Hook.AfterCardPlayed(...)` / `CheckForEmptyHand(...)`，在「敌方回合」阶段执行是否安全未验证（但靠 `MoveState._onPerform` 已能保证「敌人连打 N 张」与主循环严格串行）。<br>（原「Boss 死亡被当成玩家失败」风险已降级 —— 见 §6.6：胜负判定只看 `Enemies` + `IsPrimaryEnemy`。） |
| **最小垂直切片（能最快看到「Boss 打我们一张打击」）** | 7 步，纯代码 + 复用原版美术资源，见 §10.2。 |

---

## 1. 战斗遭遇怎么定义 / 到哪里替换一幕 Boss

### 1.1 结论

* 一幕的 Boss 由 **`EncounterModel`**（抽象类）定义；一个 act 的 Boss 存在 `ActModel._rooms.Boss`（protected `RoomSet _rooms` 里的一个字段）。
* act 在代码里是 `MegaCrit.Sts2.Core.Models.Acts.{Overgrowth, Underdocks, Hive, Glory}`，由 `ModelDb.Acts` 硬编码返回 4 个
  （`Overgrowth.Index=0, Underdocks.Index=0, Hive.Index=1, Glory.Index=2`）。
* **有三条官方/半官方的替换通道**，从「最官方」到「最兜底」：
  1. **`MapCmd.SetBossEncounter(IRunState runState, EncounterModel boss)`** —— 官方换 Boss API（内含 `RoomType.Boss` 校验，
     并刷新顶栏 Boss 图标与地图屏）。sts2.dll 内**没有任何调用者**，说明它就是留给外部（mod/测试）用的。
  2. **`RoomSet.Boss { get; set; }`** 是 public 可写属性（`ActModel.SetBossEncounter` 内部就是写它）。
  3. **`ActModel.PullNextEncounter(RoomType roomType)`** 是 Boss 进入战斗的**唯一漏斗**，Postfix 改它的返回值即可。
* ⇒ 「不新增资源就替换一幕 Boss」**完全可行**：mod 自己 new 一个 `EncounterModel` 子类（纯代码），
  通过上面任一通道挂进 act。
* ⚠️ **不要走地图 Hook**：Boss 节点是 `StandardActMap` 的固定结构点（`BossMapPoint = new MapPoint(列数/2, 行数)`），
  与 `_rooms.Boss` 无数据关系，所以 `ModifyGeneratedMap` / `ModifyGeneratedMapLate` / `AfterMapGenerated` 都换不了 Boss。

### 1.2 证据

**（a）遭遇模型 `MegaCrit.Sts2.Core.Models.EncounterModel`**（转储：`.recon/cb-EncounterModel.txt`）

```csharp
public abstract class EncounterModel : AbstractModel
{
    private const string _locTable = "encounters";
    public override bool ShouldReceiveCombatHooks => false;
    public abstract RoomType RoomType { get; }                 // Monster / Elite / Boss
    public abstract IEnumerable<MonsterModel> AllPossibleMonsters { get; }
    protected abstract IReadOnlyList<(MonsterModel, string?)> GenerateMonsters();
    public void GenerateMonstersWithSlots(IRunState runState) { ... }
    public IReadOnlyList<(MonsterModel, string?)> MonstersWithSlots { get; }
    public virtual IReadOnlyList<string> Slots => Array.Empty<string>();
    public virtual bool HasScene => false;
    public virtual IEnumerable<string> ExtraAssetPaths => Array.Empty<string>();
    public virtual MegaSkeletonDataResource? BossNodeSpineResource { get; }   // 地图 Boss 节点图标
    public virtual string BossNodePath => $"res://animations/map/{Id.Entry.ToLowerInvariant()}/{...}_node_skel_data.tres";
}
```

**（b）一个真实的 Boss 遭遇**（`.recon/cb-enc-TestSubjectBoss.txt`）

```csharp
public sealed class TestSubjectBoss : EncounterModel
{
    public override RoomType RoomType => RoomType.Boss;
    public override string CustomBgm => "event:/music/act3_boss_test_subject";
    public override IEnumerable<MonsterModel> AllPossibleMonsters
        => new <>z__ReadOnlySingleElementList<MonsterModel>(ModelDb.Monster<TestSubject>());
    protected override IReadOnlyList<(MonsterModel, string?)> GenerateMonsters()
        => new <>z__ReadOnlySingleElementList<(MonsterModel, string)>(
               (ModelDb.Monster<TestSubject>().ToMutable(), (string)null));
}
```

`& $dotnet $cli types references\sts2.dll` 中共有 **116** 个 `MegaCrit.Sts2.Core.Models.Encounters.*`
类型，其中 Boss 类 12 个：`AeonglassBoss, CeremonialBeastBoss, KaiserCrabBoss, KnowledgeDemonBoss,
LagavulinMatriarchBoss, QueenBoss, SoulFyshBoss, TestSubjectBoss, TheInsatiableBoss, TheKinBoss,
VantomBoss, WaterfallGiantBoss`。

**（c）act 如何选 Boss**（`.recon/cb-ActModel.txt`）

```csharp
public abstract class ActModel : AbstractModel
{
    protected RoomSet _rooms;
    public EncounterModel BossEncounter => _rooms.Boss;
    public EncounterModel? SecondBossEncounter => _rooms.SecondBoss;
    public IEnumerable<EncounterModel> AllBossEncounters
        => _allBossEncounters ??= AllEncounters.Where(e => e.RoomType == RoomType.Boss);
    public abstract IEnumerable<EncounterModel> GenerateAllEncounters();
    public abstract IEnumerable<EncounterModel> BossDiscoveryOrder { get; }

    public void GenerateRooms(Rng rng, UnlockState unlockState, bool isMultiplayer = false)
    {
        ...
        _rooms.Boss    = rng.NextItem(AllBossEncounters);                       // ← Boss 在这里被选定
        _rooms.Ancient = rng.NextItem(GetUnlockedAncients(unlockState).Concat(...));
    }

    public void ValidateRoomsAfterLoad(Rng rng)   // 读档后：Boss 是 DeprecatedEncounter 就重掷
    {
        if (_rooms.Boss is DeprecatedEncounter) _rooms.Boss = rng.NextItem(AllBossEncounters.Where(...));
        if (_rooms.SecondBoss is DeprecatedEncounter) _rooms.SecondBoss = ...;
    }
}
```

**（d）`RoomSet.Boss` 是公开可写属性**（`MegaCrit.Sts2.Core.Rooms.RoomSet`，本次新反编译）

```csharp
public class RoomSet
{
    private EncounterModel? _boss;
    public EncounterModel Boss
    {
        get => _boss ?? throw new InvalidOperationException("RoomSet.Boss not set! You must call GenerateRooms");
        set => _boss = value;                     // ← public setter
    }
    public EncounterModel? SecondBoss { get; set; }
    public EncounterModel NextBossEncounter
        => (bossEncountersVisited != 0 && SecondBoss != null) ? SecondBoss : Boss;
    public void MarkVisited(RoomType roomType) { ... case RoomType.Boss: bossEncountersVisited++; }
}
```

`RoomSet.ToSave()/FromSave()` 只存 `BossId` / `SecondBossId`，所以替换后存档里存的也是**我们自己的 ModelId**，
读档时走 `SaveUtil.EncounterOrDeprecated(BossId)` —— 只要 mod 的类型仍然被 `ModelDb` 注册就能正确还原。

**（e）RunState 侧入口**

```csharp
// MegaCrit.Sts2.Core.Runs.RunState
public IReadOnlyList<ActModel> Acts { get; set; }      // public setter
public ActModel Act => Acts[CurrentActIndex];
```

**（f）战斗开始时的怪物生成**（`.recon/cb-CombatRoom.txt`）

```csharp
// MegaCrit.Sts2.Core.Rooms.CombatRoom
public EncounterModel Encounter => CombatState.Encounter;
public CombatRoom(EncounterModel encounter, IRunState? runState)
{
    encounter.AssertMutable();
    CombatState = new CombatState(encounter, runState, runState?.Modifiers, ...);
}
// <StartCombat>d__46 内：
if (!combatRoom.Encounter.HaveMonstersBeenGenerated)
    combatRoom.Encounter.GenerateMonstersWithSlots(combatRoom.CombatState.RunState);
await PreloadManager.LoadRoomCombatAssets(combatRoom.Encounter, combatRoom.CombatState.RunState);
foreach ((MonsterModel, string) m in combatRoom.Encounter.MonstersWithSlots) { /* CreateCreature(...) */ }
```

且 `CombatState.CreateCreature(MonsterModel, CombatSide, string)` 是 **public**。

> ⚠️ **修正**：`CombatState.Encounter` **不是** public 可写。成员清单里显示为 `{ get; set; }` 是 ILSpy 的归一化显示，
> 实际访问器是 **`get` + `private init`**（`cb-CombatState.txt` L541）：
> ```csharp
> private readonly EncounterModel? _encounter;
> public EncounterModel? Encounter { get { return _encounter; } private init { value?.AssertMutable(); _encounter = value; } }
> ```
> ⇒ 不能靠改 `CombatState.Encounter` 换 Boss。

### 1.2b 真正的漏斗：`RunManager.CreateRoom` → `ActModel.PullNextEncounter`

```csharp
// MegaCrit.Sts2.Core.Runs.RunManager  (.recon/cb-RunManager.txt L4191)
private AbstractRoom CreateRoom(RoomType roomType, MapPointType mapPointType = Unassigned, AbstractModel? model = null)
{
    switch (roomType)
    {
    case RoomType.Monster: case RoomType.Elite: case RoomType.Boss:
        return new CombatRoom((model as EncounterModel)
                              ?? State.Act.PullNextEncounter(roomType).ToMutable(), State);   // ★ 唯一消费点
    ...
    }
}
// EnterMapPointInternal: var room = CreateRoom(roomType, MapPointType.Unassigned, model); await EnterRoom(room);   // L4379-4380

// MegaCrit.Sts2.Core.Models.ActModel
public EncounterModel PullNextEncounter(RoomType roomType) => roomType switch {
    RoomType.Monster => _rooms.NextNormalEncounter,
    RoomType.Elite   => _rooms.NextEliteEncounter,
    RoomType.Boss    => _rooms.NextBossEncounter,
    _ => throw new ArgumentOutOfRangeException(...) };

public void SetBossEncounter(EncounterModel encounter)          // ★ 官方换 Boss API（有 RoomType.Boss 校验）
{
    AssertMutable();
    if (encounter.RoomType != RoomType.Boss) throw new ArgumentException("The encounter must be a boss.");
    _rooms.Boss = encounter;
}
public void SetSecondBossEncounter(EncounterModel? encounter);
```

> **★ 陷阱**：`CreateRoom` 会对 `PullNextEncounter` 的返回值调 `.ToMutable()`，
> 而 `EncounterModel.ToMutable()` 第一行是 `AssertCanonical()`。
> 所以任何 Patch `PullNextEncounter` 的返回值都**必须返回 canonical 实例**（`ModelDb.Encounter<MyBoss>()`），
> 返回 mutable 实例会抛异常。

```csharp
// MegaCrit.Sts2.Core.Commands.MapCmd  (.recon/cb-MapCmd.txt) —— ★ 官方「换 Boss」入口
public static MapCmd SetBossEncounter(IRunState runState, EncounterModel boss)
// 内部：runState.Act.SetBossEncounter(boss); 并刷新顶栏 Boss 图标与地图屏（NMapScreen.Instance?.SetMap(...)）
```

### 1.2c 为什么「改地图」换不了 Boss（负向结论）

* `StandardActMap` 里 Boss 节点是**结构常量**：
  `BossMapPoint = new MapPoint(GetColumnCount() / 2, GetRowCount())`（列 3，行 = 地图长度，位于 `Grid` 之外），
  与 `_rooms.Boss` **没有任何数据关系**。
* `Hook.ModifyGeneratedMap(IRunState, ActMap, int)` 的本体是同步的，并且**尾部还会再调 `ModifyGeneratedMapLate`**：
  `return ModifyGeneratedMapLate(runState, map, actIndex);` —— 所以「两个都 patch」没有意义。
* ⇒ `ModifyGeneratedMap` / `ModifyGeneratedMapLate` / `AfterMapGenerated` **都不是换 Boss 的切点**，
  只能改地图节点/房型（例如追加第二个 Boss 节点）。

### 1.2d Hook 层的负向结论

* `rules.md` 里**没有任何** `ModifyEncounters` / `AfterCombatStart` / `BeforeCombatEnd`。
  实测 `Select-String .recon/Hook-members.txt -Pattern 'Encounter'` 命中数 = **0**。
* ⚠️ **`rules.md` 的 Hook 参数表把 `ICombatState` 简写成 `CombatState`，照抄会编译不过/匹配失败。**
  例：`rules.md` 第 120 行写 `BeforeCombatStart | IRunState, CombatState?`，
  实际签名是 `Hook.BeforeCombatStart(IRunState runState, ICombatState? combatState)`。
  所有带 combatState 的条目（`AfterCombatEnd` / `AfterCombatVictory` / `AfterCreatureAddedToCombat` /
  `BeforeSideTurnStart` …）都有同样的偏差。

### 1.3 注入点候选（按可行度排序）

> 顺序已按子侦察（`.recon/cb-injection-points.txt`）修正：**`PullNextEncounter` 才是正确的切点，
> `GenerateRooms` 会被 `ApplyDiscoveryOrderModifications` 覆盖，地图类 Hook 完全无效。**

| 排名 | 注入点（精确签名） | 同步/异步 | 形态 | 换 Boss 能力 | 需新资源 | 可行性 | 主要风险 |
|---|---|---|---|---|---|---|---|
| **1** | `ActModel.PullNextEncounter(RoomType roomType)` → `EncounterModel` | 同步，非 virtual | Postfix + `ref EncounterModel __result` | ★★★ 直接改写最终返回值，覆盖**所有**路径 | 否 | **高** | 必须返回 **canonical** 实例（`CreateRoom` 会 `.ToMutable()`）；需用 `__instance.SecondBossEncounter` 的 Id 比对来区分第一/第二个 Boss |
| **2** | `RunManager.GenerateRooms()`（**无参**，同步 void） | 同步 | Postfix | ★★★ 生成后统一替换所有 Act | 否 | **高** | 此时 `NRun.Instance` 可能为 null → 只调 `act.SetBossEncounter(...)`，**不要**调 `MapCmd.SetBossEncounter` |
| **3** | `MegaCrit.Sts2.Core.Commands.MapCmd.SetBossEncounter(IRunState runState, EncounterModel boss)` | 同步 | **官方 API，直接调用**（也可作为上面 Postfix 的实现体） | ★★★ | 否 | **高** | 内部会 `NMapScreen.Instance?.SetMap(...)` 刷新地图屏；`TestMode.IsOff` 时直接访问 `NRun.Instance.GlobalUi`（无 null 检查）→ 应在 `NRun` 已存在时调用 |
| **4** | `ActModel.GenerateRooms(Rng, UnlockState, bool)` **+** `ActModel.ApplyDiscoveryOrderModifications(UnlockState)` | 同步 | **两个 Postfix 必须成对** | ★★ 需两处补丁才稳 | 否 | 中高 | `RunManager.GenerateRooms` 里 `act.GenerateRooms(...)` 之后**紧跟** `act.ApplyDiscoveryOrderModifications(...)`（L4077-4081），后者会把 `_rooms.Boss` 覆盖回原版 → 只 patch 前者无效 |
| **5** | `ActModel.FromSave(SerializableActModel)` / `ActModel.ValidateRoomsAfterLoad(Rng)` | 同步 | Postfix，`ref ActModel __result` / `ActModel __instance` | ★★ 存档兼容 | 否 | 中高 | 只在读档路径生效（`RunManager.InitializeSavedRun` L3873-3879 会调用） |
| **6** | `EncounterModel.GenerateMonstersWithSlots(IRunState runState)` | 同步，public 非 virtual | Postfix + `AccessTools.FieldRefAccess` 改私有字段 `_monstersWithSlots` | ★★ 换「怪物集合」而非换 encounter | 否 | 中高 | 字段私有；`_monstersWithSlots != null` 时会 throw（只能 Postfix 覆盖）；`MonstersWithSlots`/`GetAssetPaths` 全依赖它 |
| **7** | 具体 Boss 的 `protected override GenerateMonsters()`（12 个类） | 同步 | Prefix + `ref __result` + `return false` | ★★ 按 Boss 精确替换 | 否 | 中 | 需逐类 patch；`protected` 方法要用 `AccessTools.Method` 精确指定 |
| **8** | `CombatState.CreateCreature(MonsterModel monster, CombatSide side, string? slot)` | 同步 | Prefix + `ref MonsterModel monster` | ★ 只在进战斗瞬间替换 | 否 | 中 | 也对玩家宠物/召唤物调用，需限定「Boss 房 + Side==Enemy」；`SetUpCombat` 已按原 `Encounter` 预载资源 |
| **9** | `MonsterModel.AfterAddedToRoom()`（`public virtual Task`） | **异步** | 由 mod 自己的 `MonsterModel` 覆写（**不用 Harmony**） | ✗ 不能换 Boss | 否 | 中 | 只能给已 spawn 的 Boss 加 power / 改状态机（先例：`Mocks.MockArtifactMonster` 在此 `PowerCmd.Apply<ArtifactPower>`） |
| **10** | `Hook.BeforeCombatStart(IRunState, ICombatState?)` | **异步 Task** | Postfix（wrapper 级） | △ 只能「事后换血」 | 否 | **低** | 触发时机在怪物已 `CreateCreature + AddCreature`、`NCombatRoom` 建 UI **之后**；替换会导致节点/意图/血条不一致 |
| **11** | `CombatRoom.StartCombat(IRunState?)`（`private async Task`） | **异步 private** | 只能 patch wrapper 或 IL patch `<StartCombat>d__46.MoveNext` | ★★ 理论最强、实际最脆 | 否 | 低 | 状态机结构随版本变化，不推荐 |
| **12** | `Hook.ModifyGeneratedMap` / `ModifyGeneratedMapLate` / `AfterMapGenerated` | 前两个同步返回 `ActMap`；最后一个是异步 Task | Prefix/Postfix | ✗ **换不了 Boss** | 否 | — | 只适合追加第二个 Boss 节点 / 改房型 |

**推荐方案（可直接照做）**：

```
① Postfix RunManager.GenerateRooms()                          // 新游戏：确定 Boss（用 act.SetBossEncounter，不碰 UI）
② Postfix ActModel.FromSave / ActModel.ValidateRoomsAfterLoad  // 读档：确定性
③ Postfix ActModel.PullNextEncounter(RoomType)                 // 兜底 + 覆盖全部路径（返回 canonical；先比对 SecondBossEncounter）
④ 在 NRun.Instance != null 时补一次 MapCmd.SetBossEncounter    // 刷顶栏 Boss 图标与地图屏
⑤ mod 自己的 EncounterModel : EncounterModel { RoomType => RoomType.Boss; GenerateMonsters() => (MyCharBoss.ToMutable(), null); }
   mod 自己的 MonsterModel  : MonsterModel  { VisualsPath => SceneHelper.GetScenePath("creature_visuals/ironclad"); ... }
```

### 1.4 置信度 / 风险

* 置信度：**高**。上述所有成员签名与方法体均为反编译原文（`RoomSet.Boss` 的 public setter、
  `ActModel.GenerateRooms` 的赋值、`ApplyDiscoveryOrderModifications` 的覆盖、`PullNextEncounter` 的 switch、
  `RunManager.CreateRoom` 的 `.ToMutable()`、`StandardActMap.BossMapPoint` 的固定坐标）。
* 风险：
  * **中**：`MapCmd.SetBossEncounter` 在 `RunManager.GenerateRooms()` 时机调用会 NRE（`NRun.Instance` 可能为 null）
    —— 证据是 `TestMode.IsOff` 分支无 null 检查，但未实测。→ 拆成「先 `act.SetBossEncounter`，等 `NRun` 就绪后再刷 UI」。
  * **中**：存档。`CombatRoom.ToSerializable` 会写 `EncounterId = Encounter.Id`；玩家卸掉 mod 后读档时
    `SaveUtil.EncounterOrDeprecated(id)` 的行为**未验证**。
  * **中**：多人模式。`EncounterId` 参与网络同步与 checksum（`RunManager.SendPostActionChecksum`、`ChecksumTracker`），
    自建 Encounter 的 `ModelId` 必须在所有端一致 → **MVP 建议只做单人**。
  * **低**：地图 Boss 节点的图标/BGM 预载时机（`ActModel.AssetPaths` 在建 `_rooms` 时算好）。
    若替换发生在预载后，新 Boss 的地图图标不预载 —— 但都是原版资源，运行时 `ResourceLoader` 仍能加载。

---

## 2. 怪物模型怎么定义 / mod 能否新增 `MonsterModel`

### 2.1 结论

* **能**。`MonsterModel` 是 `public abstract class MonsterModel : AbstractModel`，只有 3 个抽象成员，
  mod 里直接继承即可；**mod DLL 中的类型会被 `ReflectionHelper.GetSubtypesInMods<AbstractModel>()` 扫到**，
  所以 `ModelDb.Monster<MyBoss>()` 能取到我们的实例。
* 怪物的「行为」= `GenerateMoveStateMachine()` 返回的 `MonsterMoveStateMachine`；每个 `MoveState` 持有
  **intent 列表** + **一个 `Func<IReadOnlyList<Creature>, Task>` 动作委托**（这是后面「让 Boss 打牌」的关键）。
* 随机/分支行为由 `RandomBranchState` / `ConditionalBranchState` 提供。

### 2.2 `MonsterModel` 继承与抽象成员

```csharp
// MegaCrit.Sts2.Core.Models.MonsterModel : AbstractModel     (.recon/cb-MonsterModel.txt:32)
public abstract class MonsterModel : AbstractModel
{
    protected const string _locTableName = "monsters";
    public override bool ShouldReceiveCombatHooks => true;                       // ← 怪物会收到战斗 Hook
    public virtual LocString Title => L10NMonsterLookup(base.Id.Entry + ".name");
    public abstract int MinInitialHp { get; }
    public abstract int MaxInitialHp { get; }
    protected abstract MonsterMoveStateMachine GenerateMoveStateMachine();       // ← 唯一的“行为”抽象

    protected virtual string VisualsPath => SceneHelper.GetScenePath("creature_visuals/" + Id.Entry.ToLowerInvariant());
    public NCreatureVisuals CreateVisuals() { try { ... GetScene(VisualsPath) ... } catch { return CreateFallbackVisuals(); } }
    public virtual void SetupSkins(MegaSprite spine, MegaSkeleton skeleton) { }
    public virtual CreatureAnimator GenerateAnimator(MegaSprite controller) { ... }

    public void SetUpForCombat() { MoveStateMachine = GenerateMoveStateMachine(); SpawnedThisTurn = true; }
    public void RollMove(IEnumerable<Creature> targets) { NextMove = MoveStateMachine.RollMove(targets, Creature, RunRng.MonsterAi); }
    public void SetMoveImmediate(MoveState state, bool forceTransition = false) { ... }
    public Task PerformMove() { ... }
    public MonsterModel ToMutable() { ... }
    // 其余全部是 virtual（SFX、死亡表现、bestiary、phobia 模式 …）
}
```

> 继承关系：`MonsterModel → AbstractModel`（`.recon/MonsterModel-members.txt:1` 与 `.recon/M-AbstractModel.txt`）。
> `AbstractModel` 提供 `Id` / `IsMutable` / `ToMutable()` / `AssertMutable()` / `ShouldReceiveCombatHooks` /
> `AfterCardPlayed` 等钩子。

### 2.3 `MonsterMoveStateMachine` / `MoveState` / `AbstractIntent` 结构

```csharp
// .recon/MoveState.txt
public class MoveState : MonsterState
{
    private readonly Func<IReadOnlyList<Creature>, Task> _onPerform;   // ← 动作委托
    public IReadOnlyList<AbstractIntent> Intents { get; private set; }
    public string StateId { get; }
    public bool MustPerformOnceBeforeTransitioning { get; set; }
    public string? FollowUpStateId { get; init; }
    public MonsterState? FollowUpState { get; set; }
    public override bool CanTransitionAway => MustPerformOnceBeforeTransitioning ? _performedAtLeastOnce : true;
    public override bool IsMove => true;

    public MoveState(string stateId, Func<IReadOnlyList<Creature>, Task> onPerform, params AbstractIntent[] intents)
    { _onPerform = onPerform; Intents = intents; StateId = stateId; }

    public Task PerformMove(IEnumerable<Creature> targets) { _performedAtLeastOnce = true; return _onPerform(targets.ToArray()); }
    public override string GetNextState(Creature owner, Rng rng) => (FollowUpState?.Id ?? FollowUpStateId) ?? throw ...;
}
```

**`MoveState` 的字段就是全部**：`intent 列表`（显示用）、`StateId`（名字/日志）、`_onPerform`（动作，`Task`）、
`FollowUpStateId`/`FollowUpState`（后续状态）、`MustPerformOnceBeforeTransitioning`（是否必须执行一次才能转移）。
**随机性不在 `MoveState` 里**，而由外层的 `RandomBranchState` / `ConditionalBranchState` 提供：

```csharp
// MonsterMoveStateMachine 命名空间
public class RandomBranchState : MonsterState
{
    public void AddBranch(MonsterState state, int cooldown, MoveRepeatType repeatType, Func<float> weight);
    public void AddBranch(MonsterState state, int maxRepeats, Func<float> weight);
    // … 共 11 个重载
}
public class ConditionalBranchState : MonsterState
{
    public ConditionalBranchState(string stateId);
    public void AddState(MonsterState move, Func<bool> condition);
}
```

```csharp
// .recon/cb-MonsterMoveStateMachine.txt
public class MonsterMoveStateMachine
{
    public Dictionary<string, MonsterState> States { get; } = new();
    public List<MonsterState> StateLog { get; } = new();
    public MonsterMoveStateMachine(IEnumerable<MonsterState> states, MonsterState initialState) { ... }
    public MoveState RollMove(IEnumerable<Creature> targets, Creature owner, Rng rng) { ... }
    public void ForceCurrentState(MonsterState state) { ... }
    public void OnMovePerformed(MoveState _) { _performedFirstMove = true; }
}
```

`AbstractIntent` 的成员（`& $dotnet $cli members ... MonsterMoves.Intents.AbstractIntent`）：
`GetIntentLabel/GetTexture/GetAnimation/GetHoverTip`（virtual）、`IntentType`、`SpritePath`、`AssetPaths`。
现成 intent 子类：`SingleAttackIntent, MultiAttackIntent, DefendIntent, BuffIntent, DebuffIntent, StatusIntent,
CardDebuffIntent, HealIntent, SummonIntent, StunIntent, SleepIntent, EscapeIntent, HiddenIntent, UnknownIntent,
DeathBlowIntent`。

**真实例子**（`.recon/cb-Monster-TestSubject.txt:1244-1275`）：

```csharp
protected override MonsterMoveStateMachine GenerateMoveStateMachine()
{
    DeadState = new MoveState("RESPAWN_MOVE", RespawnMove, new HealIntent(), new BuffIntent());
    MoveState moveState  = new MoveState("BITE_MOVE", BiteMove, new SingleAttackIntent(BiteDamage));
    MoveState moveState2 = new MoveState("SKULL_BASH_MOVE", SkullBashMove, new SingleAttackIntent(SkullBashDamage), new DebuffIntent());
    MoveState moveState3 = new MoveState("MULTI_CLAW_MOVE", MultiClawMove, new MultiAttackIntent(MultiClawDamage, () => MultiClawTotalCount));
    ...
    return new MonsterMoveStateMachine(val, moveState);
}
```

### 2.4 注册 / 发现机制：mod 的类型会被扫到吗？

**会。**关键链路：

```csharp
// MegaCrit.Sts2.Core.Models.ModelDb  (.recon/cb-ModelDb.txt)
public static Type[] AllAbstractModelSubtypes
{
    get
    {
        if (_allAbstractModelSubtypes != null) return _allAbstractModelSubtypes;
        HashSet<Type> val = new();
        foreach (var t in AbstractModelSubtypes.All) val.Add(t);                 // 游戏本体类型
        foreach (var t in ReflectionHelper.GetSubtypesInMods<AbstractModel>())   // ← MOD 类型
            val.Add(t);
        _allAbstractModelSubtypes = val.ToArray();
        return _allAbstractModelSubtypes;
    }
}

public static void Init(Type[]? injectedModelTypes = null)
{
    foreach (Type type in injectedModelTypes ?? AllAbstractModelSubtypes)
    {
        ModelId id = GetId(type);
        _contentById[id] = (AbstractModel)Activator.CreateInstance(type);
    }
}
public static void Inject(Type type) { if (!Contains(type)) _contentById[GetId(type)] = (AbstractModel)Activator.CreateInstance(type); }
public static T Monster<T>() where T : MonsterModel => Get<T>();     // Get<T>() => _contentById[GetId<T>()]
public static ModelId GetId(Type type) => new ModelId(GetCategory(type), GetEntry(type));
public static string GetEntry(Type type) => StringHelper.Slugify(type.Name);
public static Type GetCategoryType(Type type)   // 沿 BaseType 上溯直到 BaseType == typeof(AbstractModel)
{ Type t = type; while (t.BaseType != typeof(AbstractModel)) t = t.BaseType; return t; }
```

```csharp
// MegaCrit.Sts2.Core.Helpers.ReflectionHelper  (.recon/cb-ReflectionHelper.txt)
public static Type[] ModTypes => _modTypes ??= ModManager.GetLoadedMods()
        .SelectMany(m => m.assemblies).SelectMany(a => a.GetTypes()).ToArray();
public static IEnumerable<Type> GetSubtypesInMods<T>() => GetSubtypesInMods(typeof(T));
// GetSubtypesFromList 过滤：type != null && !type.IsAbstract && !type.IsInterface && InheritsOrImplements(type, parentType)
```

**结论**：mod DLL 由 `ModManager` 以 `AssemblyLoadContext.LoadFromAssemblyPath` 载入并挂到 `Mod.assemblies`
（`.recon/cb-ModManager.txt:1460-1466`），随后 `ReflectionHelper.ModTypes` 遍历所有 mod assembly 的所有非抽象子类。
所以 `class IroncladBoss : MonsterModel` 会被自动注册，`ModelId = (category="monster" 的 slug, entry="ironclad_boss")`，
`ModelDb.Monster<IroncladBoss>()` 可直接取到。**没有 `ModelDb.Init` 时序问题的话**；`ModelDb.Inject(Type)`
是公开的兜底 API。

**⚠️ 重要副作用**（`.recon/cb-ModelDb.txt`）：

```csharp
public static IEnumerable<MonsterModel> Monsters
    => Acts.SelectMany(a => a.AllMonsters)
           .Concat(EventEncounters.SelectMany(e => e.AllPossibleMonsters)).Distinct();
```

`ModelDb.Monsters`（以及 `ActModel.AllMonsters`）**只包含「被某个 act 的遭遇引用到」的怪物**，
不包含所有 `MonsterModel` 子类。所以我们的 Boss 怪物**必须被我们的 `EncounterModel.AllPossibleMonsters` /
返回出去**，否则 bestiary（`BestiaryEntry`）、`DiscoveredEnemies`、怪物发现度等外围系统看不到它。

### 2.5 置信度 / 风险

* 置信度：**高**。`ModelDb` 与 `ReflectionHelper` 的源码直接读到；`MonsterModel` 抽象成员只有 3 个。
* 风险：
  * **中**：`ModelDb.AllAbstractModelSubtypes` 是**懒加载 + 永久缓存**。若它在 mod 加载完成前就被访问过，
    mod 类型永远不会进 `_contentById`。需要实测（或在 mod 初始化时自行调用 `ModelDb.Inject(typeof(MyBoss))`）。
  * **低**：`ModelId` 的 category 由 `ModelId.SlugifyCategory(typeof(MonsterModel).Name)` 决定，
    需实测确认最终 `Id.Entry` 与我们写在 loc 文件里的 key 一致（`GetEntry` 用 `StringHelper.Slugify(type.Name)`）。
  * **中**：mod 的 `MonsterModel` 需要 `RunRng`（`MonsterModel.RunRng` 的 setter 只能在 null 时设置一次），
    由 `CombatState.CreateCreature` 负责赋值 —— 正常路径没问题，手动 `new Creature(...)` 就要自己处理。

---

## 3. 外观资源：不提供 spine 动画能不能用 fallback / 复用原版角色资源

### 3.1 结论

**能，而且可以做到「零新增美术资源」**：`MonsterModel.VisualsPath` 是 `protected virtual`，
mod 的 Boss 直接**覆写它指向原版角色的战斗立绘场景**即可：

```csharp
protected override string VisualsPath => "res://scenes/creature_visuals/ironclad.tscn";
```

因为 `CharacterModel.VisualsPath` 与 `MonsterModel.VisualsPath` **用完全相同的路径规则**：
`SceneHelper.GetScenePath("creature_visuals/" + Id.Entry.ToLowerInvariant())`，且
`GetScenePath(x) => "res://scenes/" + x + ".tscn"`。

### 3.2 证据

```csharp
// MonsterModel  (.recon/cb-MonsterModel.txt:329,365,561,575)
private static readonly string _fallbackVisualsPath = SceneHelper.GetScenePath("creature_visuals/fallback");
protected virtual string VisualsPath => SceneHelper.GetScenePath("creature_visuals/" + base.Id.Entry.ToLowerInvariant());
public NCreatureVisuals CreateVisuals()
{
    try { return PreloadManager.Cache.GetScene(VisualsPath).Instantiate<NCreatureVisuals>(PackagedScene.GenEditState.Disabled); }
    catch (Exception ex) { Log.Error($"... Falling back to error scene. ..."); return CreateFallbackVisuals(); }
}
private NCreatureVisuals CreateFallbackVisuals() => PreloadManager.Cache.GetScene(_fallbackVisualsPath).Instantiate<NCreatureVisuals>(...);
```

```csharp
// CharacterModel  (.recon/cb-CharacterModel.txt:88,211)
private string VisualsPath => SceneHelper.GetScenePath("creature_visuals/" + base.Id.Entry.ToLowerInvariant());
public NCreatureVisuals CreateVisuals() => PreloadManager.Cache.GetScene(VisualsPath).Instantiate<NCreatureVisuals>(...);
```

```csharp
// SceneHelper / ImageHelper
public static string GetScenePath(string innerPath) => "res://scenes/" + innerPath + ".tscn";
public static string GetImagePath(string innerPath) => "res://images/" + innerPath;
```

**pck 里确实存在这些场景**。两重验证：

① `.recon/pck_index.txt`（由 `.recon/list_pck.gd` 通过 `ProjectSettings.load_resource_pack(游戏 pck)` + 遍历 `res://` 生成）：

| 路径 | 行号 |
|---|---|
| `res://scenes/creature_visuals/ironclad.tscn` | 890 |
| `res://scenes/creature_visuals/silent.tscn` | 925 |
| `res://scenes/creature_visuals/defect.tscn` | 866 |
| `res://scenes/creature_visuals/regent.tscn` | 919 |
| `res://scenes/creature_visuals/necrobinder.tscn` | 907 |
| `res://scenes/creature_visuals/osty.tscn`（Necrobinder 的宠物） | 910 |
| `res://scenes/creature_visuals/fallback.tscn` | 873 |

② **直接对游戏本体 pck 做二进制检索**（`D:\SteamLibrary\steamapps\common\Slay the Spire 2\SlayTheSpire2.pck`，
1 990 705 700 字节，magic `GDPC`，路径表在文件尾部；读取尾部 48 MB 做 UTF-8 子串匹配）确认存在：

```
FOUND: scenes/creature_visuals/ironclad.tscn
FOUND: scenes/creature_visuals/fallback.tscn
FOUND: scenes/creature_visuals/silent.tscn
FOUND: scenes/creature_visuals/defect.tscn
FOUND: scenes/creature_visuals/regent.tscn
FOUND: scenes/creature_visuals/necrobinder.tscn
FOUND: scenes/creature_visuals/osty.tscn
FOUND: animations/characters/ironclad
FOUND: localization/zhs/{cards,intents,relics,encounters,characters,bestiary,monsters}.json
FOUND: localization/eng/monsters.json
```

> 注：`.recon/pck_index.txt` 里同时有 `res://tools/...`、`res://src/...` 等 workspace 路径，
> 因为 `list_pck.gd` 是在以 workspace 为 project root 的 Godot 实例里跑的，`res://` 覆盖了
> 「workspace 目录 + 已加载的游戏 pck」两层。游戏资源路径（`scenes/`、`themes/`、`animations/`）
> 在 workspace 里并不存在，因此一定来自游戏 pck —— 上面的 ② 也独立确认了这一点。

动画方面，`MonsterModel.GenerateAnimator(MegaSprite)` 与 `CharacterModel.GenerateAnimator(MegaSprite, Creature)`
**使用同一套动画状态名**（`idle_loop` / `cast` / `attack` / `hurt` / `die`）：

```csharp
// MonsterModel.GenerateAnimator —— 默认实现
AnimState idle  = new AnimState("idle_loop", isLooping: true);
AnimState cast  = new AnimState("cast");
AnimState atk   = new AnimState("attack");
AnimState hurt  = new AnimState("hurt");
AnimState die   = new AnimState("die");
animator.AddAnyState("Idle", idle); animator.AddAnyState("Cast", cast);
animator.AddAnyState("Attack", atk); animator.AddAnyState("Dead", die); animator.AddAnyState("Hit", hurt);

// CharacterModel.AnimationStates —— 角色版
{ new AnimState("cast"),   "Cast"  }
{ new AnimState("attack"), "Attack" }
{ new AnimState("hurt"),   "Hit"   }
{ new AnimState("cast"),   "PowerUp" }
// 另有 idle_loop / die / low_health_loop / relaxed_loop
```

因此复用角色的 `.tscn` 后，`Attack` 动画大概率能直接播放（`AttackCommand` 会设 `_attackerAnimName = "Attack"`）。
`MonsterModel.SetupSkins(MegaSprite, MegaSkeleton)` 是**空实现的 virtual**，不覆写也没问题。

**★★ 游戏自带先例**（`.recon/cb-monster-addtoroom.txt`）：原版怪物 `Mocks.MockArtifactMonster` 就是这么干的：

```csharp
// MegaCrit.Sts2.Core.Models.Monsters.Mocks.MockArtifactMonster
protected override string VisualsPath => SceneHelper.GetScenePath("creature_visuals/defect");
```

⇒ 「怪物模型直接复用可玩角色的战斗立绘」是**官方自己就在用的做法**，不是 hack。

**★★ 游戏自带先例**（`.recon/cb-monster-addtoroom.txt`）：原版怪物 `Mocks.MockArtifactMonster` 就是这么干的：

```csharp
// MegaCrit.Sts2.Core.Models.Monsters.Mocks.MockArtifactMonster
protected override string VisualsPath => SceneHelper.GetScenePath("creature_visuals/defect");
```

⇒ 「怪物模型直接复用可玩角色的战斗立绘」是**官方自己就在用的做法**，不是 hack。

**★★ 游戏自带先例**（`.recon/cb-monster-addtoroom.txt`）：原版怪物 `Mocks.MockArtifactMonster` 就是这么干的：

```csharp
// MegaCrit.Sts2.Core.Models.Monsters.Mocks.MockArtifactMonster
protected override string VisualsPath => SceneHelper.GetScenePath("creature_visuals/defect");
```

⇒ 「怪物模型直接复用可玩角色的战斗立绘」是**官方自己就在用的做法**，不是 hack。

### 3.3 mod 能否引用原版 pck 的资源

**能，只读。** 证据（`.recon/cb-ModManager.txt:1477-1493`）：

```csharp
string text4 = Path.Combine(mod.path, modId + ".pck");
if (mod.manifest.hasPck && TestMode.IsOff)
{
    if (_fileIo != null && _fileIo.FileExists(text4))
    {
        Log.Info("Loading Godot PCK " + text4);
        if (!ProjectSettings.LoadResourcePack(text4)) throw new InvalidOperationException(...);
    }
}
```

`ProjectSettings.LoadResourcePack` 把 mod 的 pck 挂进**同一个 `res://` 虚拟文件系统**。而 mod 的资源约定放在
`res://<modId>/...` 下（见 §8 的 `res://{manifest.id}/localization/...` 约定）。所以：

* ✅ mod 可以**加载/读取**本体的 `res://scenes/...`、`res://images/...`、`res://animations/...`；
* ❌ mod **不能覆盖**本体同名 `res://scenes/...` 文件（会被写到 `res://<modId>/...` 命名空间下）。
* 由此得到一个重要设计约束：**替换 Boss 应该复用一个「已存在的怪物场景 + 覆写 `VisualsPath` 指到角色场景」，
  而不是重做一个同名场景。**

### 3.4 视觉场景的「节点契约」（复用的可行性关键）

`NCreatureVisuals` 是**一个具体类**，它的 `_Ready()` 用 unique-name 标记严格地取节点
（`.recon/cb-q347-NCreatureVisuals.txt` L294+）：

```csharp
public override void _Ready()
{
    _body            = GetNode<Node2D>("%Visuals");                     // ★ 必需（缺失会抛）
    _phobiaModeBody  = GetNodeOrNull<Node2D>("%PhobiaModeVisuals");     // 可选
    FormVfxHolder    = GetNodeOrNull<Control>("%FormVfx");              // 可选
    Bounds           = GetNode<Control>("%Bounds");                     // ★ 必需
    IntentPosition   = GetNode<Marker2D>("%IntentPos");                 // ★ 必需
    VfxSpawnPosition = GetNode<Marker2D>("%CenterPos");                 // ★ 必需
    OrbPosition      = HasNode("%OrbPos")  ? GetNode<Marker2D>("%OrbPos")  : IntentPosition;   // 可选
    TalkPosition     = HasNode("%TalkPos") ? GetNode<Marker2D>("%TalkPos") : null;             // 可选
    if (IsSpineNode)                                                     // ← .tscn 上的导出属性
    {
        SpineBody = new MegaSprite(_body);
        if (SpineBody.GetSkeleton()?.GetData() == null)
        { GD.PushWarning($"Spine skeleton data failed to load for {Name}, disabling spine animation."); SpineBody = null; }
    }
    ...
}

public void SetUpSkin(MonsterModel model)
{
    if (SpineBody != null) { var sk = SpineBody.GetSkeleton(); if (sk != null) model.SetupSkins(SpineBody, sk); }
}
public bool IsPlayingHurtAnimation() => SpineAnimation.GetCurrentAnimationName() == "hurt";
public bool IsPlayingIdleAnimation()
    => SpineAnimation.GetCurrentAnimationName() is "idle_loop" or "low_health_loop";
public void SetScaleAndHue(float scale, float hue) { ... }     // RandomizeEnemyScalesAndHues 会用到
```

**关键推论**：`CharacterModel.CreateVisuals()` 用的**就是同一个 `NCreatureVisuals` 类型**
（`PreloadManager.Cache.GetScene(VisualsPath).Instantiate<NCreatureVisuals>(...)`，见 §3.2），
所以角色的 `creature_visuals/<char>.tscn` **必然满足上述全部必需节点契约** —— 否则玩家自己就进不了战斗。
⇒ **怪物直接复用角色场景不会因为节点缺失而崩。**

**动画名也对得上**。Ironclad 的 spine 数据
（`res://animations/characters/ironclad/ironclad_skel_data.tres`，子侦察转储
`.recon/cb-q347-ironclad-skel-data-tres.txt`）里包含：
`idle_loop`、`attack`、`attack_heavy`、`hurt`、`die`，以及 `idle_loop→attack`、`idle_loop→hurt`、
`hurt→die`、`idle_loop→attack_heavy` 等混合（mix）配置。

而 `MonsterModel.GenerateAnimator` 默认要实现的状态名正是
`idle_loop` / `cast` / `attack` / `hurt` / `die`（§3.2）——
**`attack` / `hurt` / `die` / `idle_loop` 四个都能直接在 Ironclad 骨骼上找到**；
`cast` 的存在性可由 `CharacterModel.AnimationStates` 里的 `new AnimState("cast"), "Cast"` 反推
（角色施法动画必须是 `cast`），**未逐一核对 Ironclad 骨骼动画表，标为低风险待实测**。

### 3.5 置信度 / 风险

* 置信度：**高**（`VisualsPath` 是 virtual、路径规则一致、角色 `.tscn` 确实在 pck 里、pck 挂载方式明确）。
* 风险：
  * **中**：角色 `.tscn` 里绑定的脚本 / spine 骨骼尺寸 / `NCreatureVisuals` 绑定假设是按「玩家侧」布局的。
    作为敌方单位渲染时，朝向、缩放、血条位置可能需要 `CanChangeScale`、`ExtraDeathVfxPadding`、
    `HpBarSizeReduction` 等 virtual 属性微调。
  * **中**：`MonsterModel.AssetPaths` 只被 `PreloadManager` 用于预载。如果 `VisualsPath` 指向角色场景，
    建议把它加进 `AssetPaths`（默认实现已经包含 `VisualsPath`），避免首次进战斗卡顿。
  * **未验证**：角色 spine 是否包含怪物需要的 `Attack`/`Cast` trigger 之外的表现（如 `BiteTrigger`）。
    我们的 Boss 不需要自定义 trigger，所以风险低。

---

## 4. 【核心】敌人能不能打牌

### 4.1 结论（一句话）

**能。但不是走 `PlayCardAction`，而是走 `CardCmd.AutoPlay(...)`。**

* `PlayCardAction`（玩家点击出牌的路径）**强制要求 `cardModel.Owner` 是 `Player`，且卡牌当前在 `PileType.Hand`** —— 对敌人不合适。
* `CardCmd.AutoPlay(choiceContext, card, target, type, skipXCapture, skipCardPileVisuals)` 是给「自动/强制出牌」用的，
  它**不检查 `PileType.Hand`**，卡牌不在任何 pile 时会自己 `CardPileCmd.Add(card, PileType.Play)`，
  然后 `OnPlayWrapper(..., isAutoPlay: true, ...)`。
* 整条出牌链的**唯一硬依赖是 `card.Owner`（一个 `Player`）以及 `card.Owner.Creature`**，
  **没有任何地方检查「出牌者是不是真人/是不是 `CombatSide.Player`」**。
* 所以敌人出牌需要伪造的状态是：**一个 `Player` 对象 + 它的 `Creature` + `PlayerCombatState`（能量/牌堆）**，
  而这三样都有公开 API 能造出来（见 §6）。

### 4.2 `CardPlay` / `PlayCardAction` / `CardModel` 逐条

#### (a) `MegaCrit.Sts2.Core.Entities.Cards.CardPlay` 的全部字段（`.recon/CardPlay.txt`）

```csharp
public class CardPlay
{
    public CardModel Card { get; init; }
    public Player    Player { get; init; }        // ← 出牌主体，类型写死是 Player
    public Creature? Target { get; init; }
    public PileType  ResultPile { get; init; }
    public ResourceInfo Resources { get; init; }
    public bool IsAutoPlay { get; init; }
    public int PlayIndex { get; init; }
    public int PlayCount { get; init; }
    public bool IsFirstInSeries => PlayIndex == 0;
    public bool IsLastInSeries  => PlayIndex == PlayCount - 1;
}
```

#### (b) `PlayCardAction` 的构造与 `ExecuteAction()`（`.recon/t-PlayCardAction.txt`）

```csharp
public sealed class PlayCardAction : GameAction
{
    public override ulong OwnerId => Player.NetId;
    public override GameActionType ActionType => GameActionType.CombatPlayPhaseOnly;   // ← 只允许在“玩家出牌阶段”
    public Player Player { get; }
    public NetCombatCard NetCombatCard { get; }
    public ModelId CardModelId { get; }
    public uint? TargetId { get; }
    public PlayerChoiceContext? PlayerChoiceContext { get; private set; }

    public PlayCardAction(CardModel cardModel, Creature? target)
    {
        if (target != null && !target.CombatId.HasValue) throw new InvalidOperationException(...);
        Player = cardModel.Owner;                    // ← 主体 = 卡的 owner（Player）
        NetCombatCard = NetCombatCard.FromModel(cardModel);
        CardModelId = cardModel.Id;
        TargetId = target?.CombatId;
    }
    public PlayCardAction(Player player, NetCombatCard netCombatCard, ModelId cardModelId, uint? targetId) { ... }

    protected override async Task ExecuteAction()     // <ExecuteAction>d__25.MoveNext()
    {
        _card = NetCombatCard.ToCardModel();
        NCardPlayQueue.Instance?.UpdateCardBeforeExecution(this);
        Creature target = await Player.Creature.CombatState.GetCreatureAsync(TargetId, 10.0);
        CardPile? pile = _card.Pile;
        if (pile == null || pile.Type != PileType.Hand)          // ★ 硬校验：必须在手牌
        {
            NCardPlayQueue.Instance?.RemoveCardFromQueueForCancellation(this);
        }
        else
        {
            // TargetType.AnyEnemy/AnyAlly 但没传 target → Log.Warn
            if (_card.CanPlay(out _, out _) && _card.IsValidTarget(target))
            {
                Log.Info($"Player {_card.Owner.NetId} playing card ...");
                (int energy, int stars) = await _card.SpendResources();       // ★ 能量扣除点
                PlayerChoiceContext = new GameActionPlayerChoiceContext(this);
                await _card.OnPlayWrapper(PlayerChoiceContext, target, isAutoPlay: false,
                                          new ResourceInfo { EnergySpent = energy, EnergyValue = energy,
                                                             StarsSpent = stars,  StarValue = stars });
            }
            else _card.Cancel();
        }
    }
}
```

> 关键判断点（逐条）：
> * **主体**：`Player = cardModel.Owner` → 依赖 `CardModel.Owner`（`Player`），不依赖别的。
> * **牌堆校验**：`pile == null || pile.Type != PileType.Hand` → **强制手牌**。
> * **可出性**：`_card.CanPlay(...) && _card.IsValidTarget(target)`。
> * **能量扣除点**：`await _card.SpendResources()`，在 `OnPlayWrapper` **之前**。
> * **阶段限制**：`ActionType => GameActionType.CombatPlayPhaseOnly`，通过 `ActionQueueSynchronizer` 出队，
>   会被 `CombatManager` 的阶段/同步约束限制 → 敌人回合用它不现实。
> * `GameAction` 的构造还有网络同步语义（`NetCombatCard`、`TargetId`），敌方用它会产生无意义的网络包。

**⇒ 对敌人：不要用 `PlayCardAction`。**

#### (c) `CardModel.TryManualPlay` / `EnqueueManualPlay` / `CanPlay`

```csharp
// .recon/m-CardModel-TryManualPlay.txt
public bool TryManualPlay(Creature? target) { if (CanPlayTargeting(target)) { EnqueueManualPlay(target); return true; } return false; }
public bool CanPlayTargeting(Creature? target) => IsValidTarget(target) && CanPlay();
public bool CanPlay(out UnplayableReason reason, out AbstractModel? preventer)
{
    reason = UnplayableReason.None;
    ICombatState combatState = CombatState ?? _owner?.Creature.CombatState;
    if (combatState == null || Owner.PlayerCombatState == null) return false;         // ★ 需要 Owner.PlayerCombatState
    if (Keywords.Contains(CardKeyword.Unplayable)) reason |= UnplayableReason.HasUnplayableKeyword;
    if (!Owner.PlayerCombatState.HasEnoughResourcesFor(this, out var reason2)) reason |= reason2;   // ★ 能量校验
    if (TargetType == TargetType.AnyAlly && combatState.PlayerCreatures.Count(c => c.IsAlive) <= 1)
        reason |= UnplayableReason.NoLivingAllies;
    if (!Hook.ShouldPlay(combatState, this, out preventer, AutoPlayType.None)) reason |= UnplayableReason.BlockedByHook;
    if (!IsPlayable) reason |= UnplayableReason.BlockedByCardLogic;
    return reason == UnplayableReason.None;
}

// .recon/m-CardModel-EnqueueManualPlay.txt
private void EnqueueManualPlay(Creature? target)
{
    TaskHelper.RunSafely(OnEnqueuePlayVfx(target));
    RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(new PlayCardAction(this, target));
}
public async Task<(int,int)> SpendResources()
{
    int energy = Owner.PlayerCombatState.Energy;
    int energyToSpend = EnergyCost.GetAmountToSpend();
    int starsToSpend = Math.Max(0, GetStarCostWithModifiers());
    if (energyToSpend > energy && Hook.ShouldPayExcessEnergyCostWithStars(CombatState, Owner))
    { starsToSpend += (energyToSpend - energy) * 2; energyToSpend = energy; }
    await SpendEnergy(energyToSpend);
    await SpendStars(starsToSpend);
    return (energyToSpend, starsToSpend);
}
```

> `CanPlay` **不是敌人出牌的必经之路**：`CardCmd.AutoPlay` 内部只调用 `Hook.ShouldPlay`，**不调用 `CanPlay`**。
> 这很好，因为 `CanPlay` 里有一条 `TargetType.AnyAlly && PlayerCreatures.Count(IsAlive) <= 1` 会误伤敌人。

#### (d) `CardCmd.AutoPlay` —— 敌人的正确出牌入口（本次新反编译，`.recon/cb-CardCmd-AutoPlay.txt`）

```csharp
public static async Task AutoPlay(PlayerChoiceContext choiceContext, CardModel card, Creature? target,
                                  AutoPlayType type = AutoPlayType.Default,
                                  bool skipXCapture = false, bool skipCardPileVisuals = false)
{
    if (CombatManager.Instance.IsOverOrEnding || card.Owner.Creature.IsDead) return;      // ★ 只要 Owner.Creature
    ICombatState combatState = card.CombatState ?? card.Owner.Creature.CombatState;       // ★ 允许 card 不在 pile 里

    if (card.Keywords.Contains(CardKeyword.Unplayable)) { await MoveToResultPileWithoutPlaying(...); return; }
    if (!Hook.ShouldPlay(combatState, card, out var preventer, type)) { await MoveToResultPileWithoutPlaying(...); return; }

    if (card.TargetType == TargetType.AnyEnemy && target == null)
        target = card.Owner.RunState.Rng.CombatTargets.NextItem(combatState.HittableEnemies);   // ★ 需要 Owner.RunState
    if (card.TargetType == TargetType.AnyAlly && target == null)
        target = card.Owner.RunState.Rng.CombatTargets.NextItem(
                     combatState.Allies.Where(c => c != null && c.IsAlive && c.IsPlayer && c != card.Owner.Creature));
    if (target == null) { await MoveToResultPileWithoutPlaying(...); return; }

    PlayerCombatState pcs = card.Owner.PlayerCombatState;                                // ★ 需要非 null
    if (card.EnergyCost.CostsX && !skipXCapture) card.EnergyCost.CapturedXValue = pcs.Energy;
    if (!skipXCapture) card.LastStarsSpent = card.HasStarCostX ? pcs.Stars : Math.Max(0, card.GetStarCostWithModifiers());

    if (card.Pile == null) await CardPileCmd.Add(card, PileType.Play);                   // ★ 不在牌堆就自己塞进 Play 堆

    var resources = new ResourceInfo { EnergySpent = 0, EnergyValue = card.EnergyCost.GetAmountToSpend(),
                                       StarsSpent = 0,  StarValue = Math.Max(0, card.GetStarCostWithModifiers()) };
    await card.OnPlayWrapper(choiceContext, target, isAutoPlay: true, resources, skipCardPileVisuals);
}
```

> **`AutoPlay` 不扣能量、不校验 `CanPlay`、不要求手牌** —— 这正是敌人出牌想要的语义。
> Boss 的能量开销需要 mod 自己记账（例如每回合给影子 Player 设 `PlayerCombatState.Energy = N`，并自行扣减）。

#### (e) `OnPlayWrapper` 的硬性前提（`.recon/m-CardModel-OnPlayWrapper-MoveNext.txt`）

`OnPlayWrapper` 是 `public`，逐条列出它依赖的东西：

| 行号（转储） | 代码 | 依赖 |
|---|---|---|
| 187 | `choiceContext.PushModel(cardModel)` | `PlayerChoiceContext` |
| 188 | `await CombatManager.Instance.WaitForUnpause()` | 全局 |
| 365/392/417/425 | `if (cardModel.Owner.Creature.IsDead) break;` | **`Owner.Creature` 必须存活** |
| 552 | `if (isAutoPlay) await CardPileCmd.Add(cardModel, PileType.Play, Bottom, null, skipCardPileVisuals)` | `Owner` |
| 562 | `else await CardPileCmd.AddDuringManualCardPlay(cardModel)` | 手动路径（敌人不走） |
| 643 | `combatState = cardModel.CombatState; if (combatState != null) {...} else goto end` | **卡必须在战斗牌堆里**（`CardModel.CombatState` 要求 `Pile.IsCombatPile`，见 §6） |
| 646 | `resultLocation = cardModel.GetResultLocationForCardPlay()` → `new CardLocation(Owner, PileType.Discard/Exhaust/None, Bottom)` | `Owner` |
| 693 | `originalOwner = cardModel.Owner` | — |
| 720 | `effectCombatId = CombatManager.Instance.BeginCardOrPotionEffect(cardModel.Owner)` | 只按 `Player` 引用做字典计数，无副作用 |
| 380 | `task = cardModel.OnPlay(branchingPlayerChoiceContext, cardPlay)` | 卡牌自身逻辑 |
| 450 | `await Hook.AfterCardPlayed(combatState, choiceContext, cardPlay)` | Hook |
| 497 | `await CombatManager.Instance.EndCardOrPotionEffect(effectCombatId, cardModel.Owner)` | 同上 |
| 593 | `await CombatManager.Instance.CheckForEmptyHand(effectCombatId, choiceContext, originalOwner)` | 会检查 `originalOwner` 手牌是否空 → 可能触发放牌/弃牌逻辑 |

**没有任何一行检查 `Player` 是不是当前回合的玩家，也没有检查 `CombatSide`。**

#### (f) 卡牌的 `OnPlay` 到底依赖什么？（以原版 `StrikeIronclad` / `Bash` 为例）

```csharp
// MegaCrit.Sts2.Core.Models.Cards.StrikeIronclad.OnPlay  (.recon/cb-Card-StrikeIronclad.txt)
ArgumentNullException.ThrowIfNull(cardPlay.Target, "cardPlay.Target");
await DamageCmd.Attack(strikeIronclad.DynamicVars.Damage.BaseValue)
    .FromCard(strikeIronclad, cardPlay).Targeting(cardPlay.Target)
    .WithHitFx("vfx/vfx_attack_slash").Execute(choiceContext);

// MegaCrit.Sts2.Core.Commands.Builders.AttackCommand
public AttackCommand FromCard(CardModel card, CardPlay? cardPlay)
{
    if (Attacker != null) throw ...;
    Player owner = card.Owner;
    Attacker = owner.Creature;                          // ★ 伤害来源 = card.Owner.Creature
    _attackerAnimName = "Attack";
    _attackerAnimDelay = owner.Character.AttackAnimDelay;   // ★ 需要 owner.Character 非 null
    ModelSource = card; CardPlay = cardPlay; _sourceType = SourceType.Card;
    return this;
}
private IReadOnlyList<Creature> GetPossibleTargets()
{
    if (IsSingleTargeted) return new(_singleTarget);
    if (IsMultiTargeted)
    {
        if (_sourceType == SourceType.Monster) return _combatState.PlayerCreatures;   // 怪物多段攻击
        return _combatState.GetOpponentsOf(Attacker);                                // ★ 卡牌多段攻击
    }
    throw ...;
}
public AttackCommand TargetingAllOpponents(ICombatState combatState)
{
    _combatState = combatState;
    TargetSide = (Attacker.Side == CombatSide.Enemy) ? CombatSide.Player : CombatSide.Enemy;   // ★ 按 Attacker.Side 决定打谁
    return this;
}
```

```csharp
// Bash.OnPlay
await DamageCmd.Attack(DynamicVars.Damage.BaseValue).FromCard(bash, cardPlay).Targeting(cardPlay.Target)
      .WithHitFx("vfx/vfx_attack_blunt", null, "blunt_attack.mp3").Execute(choiceContext);
await PowerCmd.Apply<VulnerablePower>(choiceContext, cardPlay.Target, DynamicVars.Vulnerable.BaseValue,
                                      bash.Owner.Creature, bash);          // ★ 施加者 = Owner.Creature
```

**⇒ 只要让 `card.Owner.Creature` 指向 Boss 的那只 `Creature`（且 `Side == Enemy`），
`AttackCommand.TargetingAllOpponents` 就会自动把目标侧算成 `CombatSide.Player`，
AOE 攻击也会正确地打玩家侧。这是最关键的一个一致性验证。**

#### (g) `PlayerChoiceContext` 的选择

```csharp
// MegaCrit.Sts2.Core.GameActions.Multiplayer.BlockingPlayerChoiceContext   (.recon/t-BlockingPlayerChoiceContext.txt)
public class BlockingPlayerChoiceContext : PlayerChoiceContext
{
    public override ulong? OwnerId => null;                                        // 无主
    public override Task SignalPlayerChoiceBegun(Player chooser, PlayerChoiceOptions options) => Task.CompletedTask;
    public override Task SignalPlayerChoiceEnded() => Task.CompletedTask;
}
```

* 敌人打**不需要玩家决策**的牌（打击/防御/大部分卡）→ `new BlockingPlayerChoiceContext()` 即可。
* 需要玩家选择目标的牌（如「选择一张牌」类）→ 参考 `EnemyModel` 里敌人的做法 `new ThrowingPlayerChoiceContext()`
  （`.recon/cb-Monster-TestSubject.txt` 中 `PowerCmd.Apply<AdaptablePower>(new ThrowingPlayerChoiceContext(), ...)`），
  或 `HookPlayerChoiceContext(source, combatState, localPlayerId, GameActionType)`。
* 原版怪物攻击时甚至直接传 `null`：`DamageCmd.Attack(...).FromMonster(this)...Execute(null)`。

### 4.3 结论汇总

| 问题 | 结论 |
|---|---|
| 出牌主体必须是 `Player` 吗？ | **是**（`CardPlay.Player` 是 `Player` 类型，`CardModel.Owner` 是 `Player`），但**不要求是真人玩家**。 |
| 只依赖 `Creature` + `ICombatState` + `PlayerChoiceContext` 吗？ | 部分是：卡牌**效果**层（`DamageCmd`/`PowerCmd`）只依赖 `Creature`；但**框架**层（`CanPlay`/`SpendResources`/`OnPlayWrapper`）依赖 `Player` 及其 `PlayerCombatState`、`Character`、`RunState`。 |
| 需要伪造哪些状态？ | ① 一个 `Player`（有 `Character`、`RunState`、`Deck`）；② 该 Player 的 `Creature` 且 `CombatState` 非 null、`IsDead == false`；③ `PlayerCombatState`（提供 `Hand/DrawPile/DiscardPile/PlayPile/Energy/Stars`）；④ 卡牌实例（`CombatState.CreateCard(canonical, owner)`）并处于某个**战斗牌堆**里（`AutoPlay` 会自动放进 `Play` 堆）。 |
| 能量扣除点在哪？ | `PlayCardAction` 路径上在 `SpendResources()`；**`AutoPlay` 路径完全不扣**，需要 mod 自己管。 |
| `PileType.Hand` 校验在哪？ | 只在 `PlayCardAction.ExecuteAction()` 里（`pile == null \|\| pile.Type != PileType.Hand` → 取消）。`AutoPlay` 无此校验。 |

### 4.4 置信度 / 风险

* 置信度：**高**。`CardCmd.AutoPlay`、`PlayCardAction`、`OnPlayWrapper`、`AttackCommand.FromCard`、
  `StrikeIronclad/Bash.OnPlay` 的方法体全部逐行读到。
* 风险：
  * **中**：`OnPlayWrapper` 最后会 `CombatManager.Instance.CheckForEmptyHand(effectCombatId, choiceContext, originalOwner)`
    —— 如果给影子 Player 维护手牌，Boss 打空手牌可能触发原版的「空手牌」交互。建议**让 Boss 的卡直接来自
    `DrawPile`/无牌堆，打完进 `DiscardPile`，避免 Hand 为空时的额外逻辑**（`AutoPlay` 对无牌堆的卡也支持）。
  * **中**：`NCardPlayQueue.Instance` / `NCard.FindOnTable(cardModel)` 在 `OnPlayWrapper` 里被调用（多段攻击动画）。
    影子 Player 的卡没有 UI 节点，`NCard.FindOnTable` 返回 null 是安全的（代码有 null 检查），
    但 `CardPileCmd.Add` 的 tween 动画路径 `CreateCardNodeAndUpdateVisuals(..., owningPlayerIsLocal)` 走
    `owningPlayerIsLocal == false` 分支的行为需要实测。
  * **中**：`Hook.ShouldPlay(..., AutoPlayType)` 是全局 Hook，其他 mod / 原版遗物可能改写它。

---

## 5. 敌人回合的执行流程 & 「连续打 N 张牌」的插入点

### 5.1 调用链（全部有方法体证据）

```
CombatManager.ExecuteEnemyTurn(turnState)                        [.recon/ExecuteEnemyTurn-body.txt]
  ├─ if (actionDuringEnemyTurn != null) await actionDuringEnemyTurn()          // 剧情用的一次性回调
  ├─ foreach (Creature enemy in turnState.State.Enemies)                       // ★ 按 Side 遍历
  │     ├─ if (!turnState.State.ContainsCreature(enemy)) continue;
  │     ├─ NCreature n = NCombatRoom.Instance?.GetCreatureNode(enemy);
  │     ├─ if (n != null) await n.PerformIntent();      // 只播 intent 动画，无游戏逻辑（.recon/cb-NCreature.txt）
  │     ├─ await enemy.TakeTurn();                       // ★
  │     └─ await combatManager.WaitForUnpause(turnState);
  │        await combatManager.CheckWinCondition(turnState);
  │        if (!turnState.IsInProgress) break;
  ├─ RunManager.Instance.ChecksumTracker.GenerateChecksum("After enemy turn end", null);
  └─ await combatManager.EndEnemyTurn(turnState);
```

```
Creature.TakeTurn()                                              [.recon/Creature-TakeTurn-body.txt]
  ├─ if (!creature.IsMonster || creature.Side != CombatSide.Enemy)
  │       throw new InvalidOperationException("Only enemy monsters can take automated turns.");
  └─ if (!creature.Monster.SpawnedThisTurn)
         await creature.Monster.PerformMove();                   // ★
```

```
MonsterModel.PerformMove()                                       [.recon/MonsterModel-PerformMove-body.txt]
  ├─ if (CombatState == null) return;
  ├─ await Cmd.CustomScaledWait(0.1f, 0.2f);
  ├─ IsPerformingMove = true;
  ├─ move    = NextMove;
  ├─ targets = combatState.PlayerCreatures;                      // ★ 目标 = 玩家侧 Creature
  ├─ Log.Info($"Monster {Id.Entry} performing move {move.Id}");
  ├─ await move.PerformMove(targets);                            // ★★ MoveState._onPerform(targets)
  ├─ MoveStateMachine?.OnMovePerformed(move);
  ├─ CombatManager.Instance.History.MonsterPerformedMove(combatState, this, move, targets);
  ├─ IsPerformingMove = false;
  ├─ if (Creature.IsDead && Hook.ShouldCreatureBeRemovedFromCombatAfterDeath(combatState, Creature))
  │       combatState.RemoveCreature(Creature);
  └─ await Cmd.CustomScaledWait(0.1f, 0.4f);
```

```
MoveState.PerformMove(targets)                                   [.recon/MoveState.txt:31-74]
  ├─ _performedAtLeastOnce = true;
  └─ await _onPerform(targets.ToArray());                        // ★★★ 动作委托本身返回 Task，被完整 await
```

### 5.2 结论：插入点是 **`MoveState` 的 `_onPerform` 委托**，不是 Harmony

这是本报告最重要的一个发现之一：

```csharp
public MoveState(string stateId,
                 Func<IReadOnlyList<Creature>, Task> onPerform,   // ← 可以是 async lambda
                 params AbstractIntent[] intents);
```

因为 `PerformMove` 里是 `await _onPerform(targets)`，而整条链（`ExecuteEnemyTurn → TakeTurn → PerformMove → MoveState.PerformMove`）
**全部是 `await`**，所以 mod 可以这样写：

```csharp
// 伪代码，仅示意（不是交付代码）
private readonly MoveState _playCardsState = new MoveState(
    "CHARCARDS_MOVE",
    async (IReadOnlyList<Creature> targets) =>
    {
        for (int i = 0; i < 3; i++)
        {
            CardModel card = PickCardFromBossHand();           // AI 选牌
            Creature tgt  = targets.FirstOrDefault(c => c.IsAlive) ?? targets[0];
            await CardCmd.AutoPlay(_bossChoiceContext, card, tgt, AutoPlayType.Default);
            await Cmd.CustomScaledWait(0.2f, 0.5f);            // 打出节奏
        }
    },
    new MultiAttackIntent(0, 3));                              // intent 只影响显示
```

* **`ExecuteEnemyTurn` 会等 `PerformMove` 完成**，`PerformMove` 会等 `_onPerform` 完成 →
  整个「敌人连续打 N 张牌」的异步过程被主循环正确串行化，**不会与其它结算交叠**。
* intent 显示与实际行动是**解耦**的：`NCreature.PerformIntent()` 只播动画（`.recon/cb-NCreature.txt`
  里 `NIntent.PlayPerform()` / `SetFrozen(true)`），真正的效果在 `_onPerform`。所以 intent 图标可以
  用一个泛化的 `MultiAttackIntent/SingleAttackIntent` 表示「这回合它会打牌」，或者干脆用 `HiddenIntent`。
* 回合开始前「掷下一回合的 move」由谁调用？`CombatManager` 里有
  `creature.Monster.RollMove(state.Players.Select(p => p.Creature))`（`.recon/cb-CombatManager.txt`），
  即 `MonsterModel.RollMove(targets)` → `MoveStateMachine.RollMove(targets, Creature, RunRng.MonsterAi)`。
  `MonsterModel.SetUpForCombat()` 在进战斗时建状态机。

### 5.3 Harmony 能不能安全 await？

**不能直接 await。**

* Harmony 的 prefix/postfix **不是** async 上下文。对于 `Task` 返回的方法（C# async 方法），
  实际方法体在编译器生成的 `<Method>d__N.MoveNext()` 状态机里；Harmony patch 到的是 **stub 方法**
  （`public Task PerformMove() { builder.Start(ref stateMachine); return builder.Task; }`）。
* Postfix 在 **stub 返回 Task 的那一刻**执行（即工作**刚开始**），不是在 Task 完成后。
* Prefix 可以返回 `false` 跳过原方法，但 prefix 本身不能 `await`（除非你手动同步等待 `.GetAwaiter().GetResult()`，
  这在 Godot 主线程上会死锁，**禁止**）。
* 要真正「替换」一个 async 方法并 await，只能：① 用 Transpiler 重写状态机（脆弱、随版本崩）；
  ② 把工作塞进 `MoveState._onPerform`（推荐）；③ 用 `Hook`（本身就设计成 async 的插入点）。

**⇒ 结论：敌人回合里插入异步打牌逻辑，走 `MoveState` 委托或 `MonsterModel.PerformMove` 的替换/包装，
不要走 Harmony await。**

`Hook` 系列是安全的 async 切点（它们本身就是 `async Task` 方法，但 Harmony 的 postfix 依然不能 await 它们），
所以在 Hook 上做「异步插入」同样需要 `TaskHelper.RunSafely(...)` 这类 fire-and-forget —— 会破坏时序。
**唯一能满足「与敌人回合严格串行」的做法就是 `MoveState._onPerform`。**

### 5.4 置信度 / 风险

* 置信度：**高**（整条链的方法体都读到了，`await _onPerform(...)` 是明文）。
* 风险：
  * **低**：`MoveState.PerformMove` 的目标参数是 `combatState.PlayerCreatures`。如果 §6 的方案让影子 Player 的
    Creature 出现在 `PlayerCreatures` 里，Boss 会把自己当目标 —— 必须先做 §6 的过滤 patch。
  * **中**：`Creature.TakeTurn()` 里 `if (!creature.IsMonster || creature.Side != CombatSide.Enemy) throw`。
    我们的 Boss 天然满足（`Monster != null` + `Side == Enemy`），但如果有人试图把 Boss 做成 Player 侧就会炸。
  * **中**：`SpawnedThisTurn` 为 true 时**跳过整个 `PerformMove`**（`if (!creature.Monster.SpawnedThisTurn)`）。
    本回合新生成的怪物不出手；如果 mod 想在生成当回合就让它打牌，需要自己处理。

---

## 6. 卡组与能量：怪物能不能挂卡组 / 能不能把 Boss 表示成 Player

### 6.1 结论

* **游戏里没有「非玩家卡组」的先例**：手牌/抽牌堆/弃牌堆是 **per-`Player`** 的
  （`PlayerCombatState.Hand / DrawPile / DiscardPile / ExhaustPile / PlayPile`），
  能量也是 per-`Player`（`PlayerCombatState.Energy`，`MaxEnergy` 走 `Hook.ModifyMaxEnergy`）。
* `CombatState.Players` 是从 `PlayerCreatures` 推导出来的，而 `PlayerCreatures = Creatures.Where(c => c.IsPlayer)`，
  `Creature.IsPlayer => Player != null`。**`Players` 与 `Enemies` 是同一批 `Creature` 按 `Side` 划分的两个视图**，
  `Players` 是「`Creature.Player != null` 的 Creature 的 Player」。二者**可以重叠**（一个 Creature 同时是
  Monster 又是 Player）。
* **因此最优解是：把 Boss 的 `Creature` 同时做成「Monster」和「影子 Player 的 Creature」**，
  直接复用整套卡牌/能量/遗物系统，**不需要自建数据结构**。
* 代价：`Creature.IsPlayer` 会变成 true，污染 `PlayerCreatures` / `Players` / `NCombatRoom.AddCreature`
  —— 需要 1~3 个定向 Harmony patch 兜底（见 §6.5）。

### 6.2 `Player` 的归属（owner）概念与 `CardModel` 的归属

```csharp
// CardModel  (.recon/M-CardModel.txt:112 / .recon/cb-CardModel.txt:1772)
public Player Owner
{
    get { AssertMutable(); return _owner; }
    set {
        AssertMutable();
        if (_owner != null && value != null) throw new InvalidOperationException($"Card {Id.Entry} already has an owner.");
        _owner = value;
    }
}
public CardPile? Pile
{
    get { var owner = _owner; if (owner == null) return null;
          return owner.Piles.FirstOrDefault(p => p.Cards.Contains(this)); }     // ★ 牌堆由 owner 决定
}
public ICombatState? CombatState
{
    get { var pile = Pile;
          if ((pile != null && pile.IsCombatPile) || UpgradePreviewType == CardUpgradePreviewType.Combat)
              return _owner?.Creature.CombatState;                              // ★ 必须在“战斗牌堆”里
          return null; }
}
public IRunState? RunState => _owner?.RunState;
public ICardScope? CardScope => (ICardScope)CombatState ?? (ICardScope)(_owner?.Creature.CombatState) ?? RunState;
```

```csharp
// CardPile.IsCombatPile  → PileTypeExtensions.IsCombatPile()
public static bool IsCombatPile(this PileType pileType) { if ((uint)(pileType - 1) <= 4u) return true; return false; }
// PileType { None=0, Draw=1, Hand=2, Discard=3, Exhaust=4, Play=5, Deck=6 }  → 1..5 是战斗牌堆
```

```csharp
// Player.Piles   (.recon/cb-Player.txt)
public IEnumerable<CardPile> Piles
{
    get {
        _runPiles ??= new CardPile[1] { Deck };
        return (PlayerCombatState?.AllPiles ?? Array.Empty<CardPile>()).Concat(_runPiles);
    }
}
```

**⇒ `CardModel` 的归属完全由 `Owner`（`Player`）决定；一个 Player 的「牌堆集合」=
`PlayerCombatState.{Hand,DrawPile,DiscardPile,ExhaustPile,PlayPile}` + `Deck`。**

### 6.3 `CombatState.Players` 与 `Enemies` 的关系（关键）

```csharp
// .recon/cb-CombatState.txt:554-562
public IReadOnlyList<Creature> Allies          => _allies;                                   // Side == Player
public IReadOnlyList<Creature> Enemies         => _enemies;                                  // Side == Enemy
public IReadOnlyList<Creature> Creatures       => /* _allies + _enemies */;
public IReadOnlyList<Creature> PlayerCreatures => Creatures.Where(c => c.IsPlayer).ToList(); // ★ 按 IsPlayer
public IReadOnlyList<Player>   Players         => PlayerCreatures.Select(c => c.Player).ToList();
```

```csharp
// Creature  (.recon/cb-Creature.txt:508,510)
public bool IsMonster => Monster != null;
public bool IsPlayer  => Player  != null;         // ★ 只要 Player 非 null 就是“玩家”
```

**所以「一个 Creature 既是 Monster 又是 Player」在类型系统层面完全合法**，
`Creature` 有两个独立的 ctor：

```csharp
public Creature(MonsterModel monster, CombatSide side, string? slotName)   // 怪物creature：Monster != null，Side 任选
public Creature(Player player, int currentHp, int maxHp)                    // 玩家creature：Player != null，Side 强制 Player
```

### 6.4 全部可用的公开 API（造影子 Player 所需的每一步）

```csharp
// 1) 造 Player（不需要 RunState，先解耦）
// MegaCrit.Sts2.Core.Entities.Players.Player
public static Player CreateForNewRun(CharacterModel character, UnlockState unlockState, ulong netId)
{
    Player player = new Player(character, netId, character.StartingHp, character.StartingHp,
                               character.MaxEnergy, character.StartingGold, 3, character.BaseOrbSlotCount,
                               new RelicGrabBag(), unlockState);
    player.PopulateStartingInventory();
    return player;
}
private Player(...) {
    RunState = NullRunState.Instance;                     // ★ 故意用空 RunState
    Character = character; NetId = netId;
    Creature = new Creature(this, currentHp, maxHp);      // ★ Side = CombatSide.Player
    MaxEnergy = maxEnergy; ...
    IsActiveForHooks = Creature.IsAlive;                  // ★ 默认 true
}
private void PopulateStartingInventory()
{
    if (IsInventoryPopulated) throw new InvalidOperationException("Inventory is already populated.");
    if (!(RunState is NullRunState)) throw new InvalidOperationException(
        "A player's starting inventory must be populated before being added to a run.");   // ★ 明确支持这种用法
    PopulateStartingDeck();     // Character.StartingDeck → .ToMutable() → Deck.AddInternal
    PopulateStartingRelics();   // Character.StartingRelics → 装到 player.Relics
    foreach (var p in Character.StartingPotions) AddPotionInternal(p.ToMutable());
}
public IRunState RunState { get => _runState; set {
    if (!(_runState is NullRunState)) throw new InvalidOperationException("RunState has already been set.");
    _runState = value; } }                                  // ★ 可以且只能赋一次真实 RunState
public void ResetCombatState() => PlayerCombatState = new PlayerCombatState(this);   // ★ public
public CardPile Deck { get; }                               // CardPile(PileType.Deck)，有 public AddInternal/RemoveInternal/Clear
public int MaxEnergy { get; set; }                           // public setter
public bool IsActiveForHooks { get; private set; }           // ActivateHooks()/DeactivateHooks() 是 public
public IReadOnlyList<RelicModel> Relics { get; }
```

```csharp
// 2) 造战斗牌堆（不需要 CombatManager 之外的依赖）
public class PlayerCombatState
{
    public PlayerCombatState(Player player) {
        _player = player;
        CombatManager.Instance.StateTracker.Subscribe(this);      // 订阅进战斗状态跟踪
        foreach (var pile in AllPiles) CombatManager.Instance.StateTracker.Subscribe(pile);
        OrbQueue = new OrbQueue(player); OrbQueue.Clear(); OrbQueue.AddCapacity(player.BaseOrbSlotCount);
    }
    public CardPile Hand       { get; } = new(PileType.Hand);
    public CardPile DrawPile   { get; } = new(PileType.Draw);
    public CardPile DiscardPile{ get; } = new(PileType.Discard);
    public CardPile ExhaustPile{ get; } = new(PileType.Exhaust);
    public CardPile PlayPile   { get; } = new(PileType.Play);
    public int Energy { get; set; }                              // ★ 普通属性
    public int Stars  { get; set; }
    public int MaxEnergy => (int)Hook.ModifyMaxEnergy(_player.Creature.CombatState, _player, _player.MaxEnergy);
    public void ResetEnergy(); public void GainEnergy(decimal); public void LoseEnergy(decimal);
}
```

```csharp
// 3) 把角色卡组灌进战斗
// Player.PopulateCombatState —— 官方就是这么做的
public void PopulateCombatState(Rng rng, CombatState state)
{
    foreach (CardModel current in Deck.Cards.ToList())
    {
        CardModel card = state.CloneCard(current);
        card.DeckVersion = current;
        PlayerCombatState.DrawPile.AddInternal(card);
    }
    PlayerCombatState.DrawPile.RandomizeOrderInternal(this, rng, state);
}
```

```csharp
// 4) 造卡（挂到影子 Player 名下）
// CombatState
public CardModel CreateCard(CardModel canonicalCard, Player owner)
{ CardModel c = canonicalCard.ToMutable(); AddCard(c, owner); c.AfterCreated(); return c; }   // AddCard 内 card.Owner = owner
public T CreateCard<T>(Player owner) where T : CardModel => (T)CreateCard(ModelDb.Card<T>(), owner);
public CardModel CloneCard(CardModel mutableCard);
public void AddCard(CardModel card, Player owner);
// CardPile
public void AddInternal(CardModel card, int index = -1, bool silent = false);
public void RandomizeOrderInternal(Player player, Rng rng, CombatState state);
public void Clear(bool silent);
// CardPileCmd
public static Task<CardPileAddResult> Add(CardModel card, PileType newPileType, CardPilePosition position = ..., AbstractModel clonedBy = null, bool skipVisuals = false);
public static Task<CardModel> Draw(PlayerChoiceContext choiceContext, Player player);
public static Task<IEnumerable<CardModel>> Draw(PlayerChoiceContext choiceContext, decimal count, Player player, bool fromHandDraw);
public static Task Shuffle(PlayerChoiceContext choiceContext, Player player);
```

### 6.5 推荐架构（Architecture A：单一 Boss Creature + 影子 Player）

```
        ┌──────────────── 真实玩家（可能多人） ────────────────┐
        │ Player#1 ... Player#N  (Creature.Side = Player)     │
        └─────────────────────────────────────────────────────┘
                              ▲  卡牌效果互相作用（DamageCmd/PowerCmd 只认 Creature）
                              │
   ┌─────────────────── 影子 Player (ShadowPlayer) ──────────────────┐
   │ Player 对象：Character = Ironclad, RunState = 真实 RunState     │
   │ PlayerCombatState：Hand/DrawPile/Discard/Exhaust/Play + Energy  │
   │ Relics：BurningBlood 等（可选，见 §7）                          │
   │ Creature ←──── 反射：指向下面这只 Boss Creature                  │
   └────────────────────────────────────────────────────────────────┘
                              │
        ┌───────────── Boss Creature（唯一的战斗实体）──────────────┐
        │ Monster  = mod 的 IroncladBoss : MonsterModel            │
        │ Side     = CombatSide.Enemy     ← 保证是敌人             │
        │ Player   ← 反射：ShadowPlayer   ← 让卡牌系统能用          │
        │ CombatState = 当前 CombatState  ← CreateCreature 自动挂上 │
        └──────────────────────────────────────────────────────────┘
```

**必须配套的 Harmony patch（按重要性排序）**：

| # | Patch 目标 | 为什么 |
|---|---|---|
| **P1（必须）** | `CombatState.get_PlayerCreatures` Postfix：过滤掉「被标记为 charboss」的 Creature | `Players` 由 `PlayerCreatures` 推导，`.recon/cb-CombatManager.txt` 显示 `CombatManager` 会 `foreach (Player p in state.Players)` 做**回合开始抽 5 张、重置能量、回合结束弃手牌、玩家死亡处理**。不拦会让 Boss 参与玩家回合。 |
| **P1b（必须）** | `NCombatRoom.AddCreature` 的容器选择 | `.recon/cb-NCombatRoom.txt:867-879`：<br>`if (creature.IsPlayer \|\| creature.PetOwner != null) _allyContainer.AddChildSafely(n); else _enemyContainer.AddChildSafely(n);`<br>⇒ Boss 会被画到**玩家侧**。需要一个 prefix 强制走 enemy 容器。（注意：真正决定位置的是 `CreateAllyNodes/CreateEnemyNodes`，它们按 `_visuals.Allies/Enemies` 即 `Side` 分；只有 `AddCreature` 这一个容器判断需要改。） |
| **P2（建议）** | `CombatState.IterateHookListeners` Postfix：补上 Boss 的 `Monster` 或 relics/cards | `.recon/cb-CombatState.txt:855-915`：<br>`Player player = creature.Player; if (player == null) val.Add(creature.Monster); else { relics/potions/orbs/cards }`<br>因为我们的 Boss `Creature.Player != null`，`creature.Monster` **不会**被注册为 hook listener。是否补取决于是不是需要 `MonsterModel` 的 hooks。 |
| **P3（已大幅降级）** | `CombatManager.HandlePlayerDeath` / `LoseCombat` 的触发路径 | 见下方 §6.6：**胜负判定（`IsCombatEnding`）完全不看 `Player`，只看 `Enemies` + `IsPrimaryEnemy`**，所以 Boss 死亡天然等价于「敌人清空 → 胜利」。而「失败」只有一条入口 `CombatManager.LoseCombat()`（设置 `PendingLoss`）。在 sts2.dll 里我用 13 个 `*Combat*` 类型 + 多个候选类做了整类反编译扫描，**没有找到 `LoseCombat()` / `HandlePlayerDeath()` 的调用点**（`ilspy-cli search` 也只命中定义处），因此它们很可能由程序集外部或死代码触发。**仍建议在实机确认一次**，但风险从「高」降为「低」。 |
| **P4（可选）** | 让 Boss 的 `Creature.ModelId` 返回 `Monster.Id` | `Creature.ModelId { get { if (!IsPlayer) return Monster.Id; return Player.Character.Id; } }` —— 因为 `IsPlayer` 为 true，会返回 Ironclad 的 Id。只影响 `DiscoveredEnemies` / bestiary 统计，不影响战斗。 |

**为什么不能只「加一个隐藏友方 Creature」？**
如果把影子 Player 的 Creature 做成一个真实的玩家侧单位（`AddPlayer` 会 `AttachCreature + AddCreature` 到 `_allies`），
那 Boss 就成了友军：`NCombatRoom` 会把它排到玩家侧、怪物会打它、玩家会打它（它会死）。
**唯一的正确做法就是让 Boss Creature 本身充当影子 Player 的 Creature。**

### 6.6 胜负判定（风险 #2 的关键澄清）

```csharp
// CombatManager.IsCombatEnding(CombatTurnState) —— 全部胜负判定的核心
private bool IsCombatEnding(CombatTurnState turnState)
{
    if (!turnState.IsInProgress) return false;
    if (turnState.PendingLoss != null) return true;
    if (turnState.State.Enemies.Any(e => e != null && e.IsAlive && e.IsPrimaryEnemy)) return false;  // ★ 只看 Enemies
    if (Hook.ShouldStopCombatFromEnding(turnState.State)) return false;
    return true;                                                                                     // 胜利
}

// CombatManager.CheckWinCondition(TurnState)
private async Task<bool> CheckWinCondition(CombatTurnState turnState)
{
    if (turnState.PendingLoss != null) { ProcessPendingLoss(turnState); return true; }
    if (IsCombatEnding(turnState))     { await EndCombatInternal(turnState); return true; }
    return false;
}

// 失败的唯一入口
public void LoseCombat()
{
    CombatTurnState turnState = _turnState;
    if (turnState != null && turnState.PendingLoss == null)
        turnState.PendingLoss = new PendingLossState((CombatRoom)turnState.State.RunState.CurrentRoom);
}
```

* `Creature.IsPrimaryEnemy { get { if (Side != CombatSide.Enemy) return false; return !IsSecondaryEnemy; } }`
  （`.recon/cb-Creature.txt:617`），`IsSecondaryEnemy` 只取决于「身上是否有 `OwnerIsSecondaryEnemy` 的 Power」。
* 我们的 Boss：`Side == Enemy`、`Monster != null` → **`IsPrimaryEnemy == true`**。
  ⇒ Boss 被打死 → `Enemies` 里没有活的 primary enemy → `IsCombatEnding() == true` → **胜利结算**。
  **「Boss 究竟是 Monster 还是 Player」完全不影响胜负判定。**
* 「失败」只有 `LoseCombat()` 一个入口，它只设置 `PendingLoss`。在 sts2.dll 中对 13 个 `*Combat*` 类型
  以及 `NCombatRoom / CombatRoom / CombatStateTracker / RunManager / NGameOverScreen / CombatTurnState /
  Player / Creature` 逐个整类反编译后，**未找到 `LoseCombat()` 或 `HandlePlayerDeath()` 的调用点**。
  ⇒ 结论：**Boss 死亡原则上不会触发 run 失败**；P1 的 `PlayerCreatures` 过滤把「影子 Player 被当玩家处理」
  的所有副作用一并切断，从而形成双保险。

### 6.7 置信度 / 风险

* 置信度：
  * 「`Players`/`PlayerCreatures` 的推导关系」「`Player`/`PlayerCombatState` 全部 API 都是 public」
    「`CardModel.Owner/Pile/CombatState` 的依赖」→ **高**。
  * 「P1/P1b 两个 patch 足以让 Boss 不参与玩家回合」→ **中**（未实测；`Players` 的消费点分散在
    `CombatManager`、`CombatRoom`、UI、`RunManager`）。
  * 「Boss 死亡不会触发玩家失败」→ **低/未验证**（见 P3）。
* 风险：
  * **高**：`CombatState.StateTracker.Subscribe` / `ChecksumTracker`。
    多人模式有 checksum 追踪（`RunManager.Instance.ChecksumTracker.GenerateChecksum("After enemy turn end", null)`），
    影子 Player 不在 `RunState.Players` 里，**极可能造成 checksum 不一致（联机 desync）**。→ 建议 **MVP 只支持单人**。
  * **中**：`CardModel.Owner` 的 setter 是「一旦有 owner 就不能再改」，`CreateCard` 必须一次到位。
  * **中**：`PlayerCombatState` 构造会 `CombatManager.Instance.StateTracker.Subscribe(...)`，必须在战斗中构造（否则 `CombatManager.Instance` 可能为 null）。

---

## 7. 遗物：怪物能不能持有并触发遗物

### 7.1 结论

**能，而且几乎是「免费」的 —— 沿用 §6 的影子 Player 方案即可，不需要任何额外的自建机制。**

* 遗物的宿主必须是 `Player`（`RelicModel.Owner` 类型就是 `Player`），**游戏里没有「怪物遗物」这种概念**，
  也**没有敌方遗物先例**（详见 §7.4）。
* 但因为 Boss 的 `Creature.Player` 指向影子 Player，`CombatState.IterateHookListeners()` 会自动走到
  `player` 分支，把该 Player 的 `Relics` 全部注册为**战斗 Hook 监听者**。
* `RelicModel` 自身 `ShouldReceiveCombatHooks => true`，其 hook 回调来自 `AbstractModel` 的各 virtual
  （`BeforeCardPlayed` / `AfterCardPlayed` / `AfterCardPlayedLate` / `ShouldPlay` / `AfterModifyingCardPlayCount` …）。
  `.recon/t-CardPlayTypes.txt` 里可以数到大量 `Models.Relics.*.<AfterCardPlayed>d__N` 状态机，
  直接证明原版遗物就是靠这些 Hook 实现的。

### 7.2 证据

```csharp
// MegaCrit.Sts2.Core.Models.RelicModel : AbstractModel     (.recon/cb-RelicModel.txt)
public override bool ShouldReceiveCombatHooks => true;                    // L430

public Player Owner                                                       // L226 ★ public setter
{
    get { AssertMutable(); return _owner; }
    set {
        AssertMutable();
        if (_owner != null && _owner != value)
            throw new InvalidOperationException($"Cannot move relic from {Id.Entry} one owner to another");
        _owner = value;
    }
}

public RelicPoolModel Pool                                                // L193
    => ModelDb.AllRelicPools.First(p => p.AllRelicIds.Contains(base.Id));  // ★ First（不是 FirstOrDefault）→ 不在任何池里会抛异常

public void RemoveInternal() => HasBeenRemovedFromState = true;
public virtual Task AfterObtained();
public virtual Task AfterRemoved();
public virtual bool IsAllowed(IRunState runState);
public virtual bool IsAllowedAtNeow(Player player);
public void Flash(IEnumerable<Creature> targets);                          // 遗物闪光表现
public void UpdateTexture(TextureRect texture);
public bool IsMelted { get; set; }         public bool IsWax { get; set; }
public int  StackCount { get; set; }       public bool IsStackable { get; }
```

```csharp
// MegaCrit.Sts2.Core.Commands.RelicCmd —— 全部 public，宿主参数是 Player
public static Task<T> Obtain<T>(Player player) where T : RelicModel;
public static Task<RelicModel> Obtain(RelicModel relic, Player player, int index);
public static Task Remove(RelicModel relic);
public static Task<RelicModel> Replace(RelicModel original, RelicModel replace);
public static Task Melt(RelicModel relic);

// 也可以直接用 Player 的 public API
public void AddRelicInternal(RelicModel relic, int index, bool silent);
public void RemoveRelicInternal(RelicModel relic, bool silent);
public void MeltRelicInternal(RelicModel relic);
public IReadOnlyList<RelicModel> Relics { get; }
```

```csharp
// CombatState.IterateHookListeners()   (.recon/cb-CombatState.txt:855-915)
for (int i = 0; i < _allies.Count + _enemies.Count; i++)
{
    Creature creature = (i < _allies.Count) ? _allies[i] : _enemies[i - _allies.Count];
    val.AddRange(creature.Powers);
    Player player = creature.Player;
    if (player == null) { val.Add(creature.Monster); }                    // ← 纯怪物分支
    else {
        if (!player.IsActiveForHooks) continue;
        foreach (RelicModel r in player.Relics) if (!r.IsMelted) val.Add(r);   // ★ Boss 的遗物在这里被注册
        foreach (PotionModel p in player.PotionSlots) if (p != null) val.Add(p);
        if (player.PlayerCombatState == null) continue;
        val.AddRange(player.PlayerCombatState.OrbQueue.Orbs);
        foreach (CardPile pile in player.PlayerCombatState.AllPiles)
            foreach (CardModel c in pile.Cards) { val.Add(c); ... }        // ★ 连 Boss 的卡牌也一起注册
    }
}
// 末尾过滤：private bool Contains(AbstractModel model)
//   RelicModel  → !relicModel.HasBeenRemovedFromState && relicModel.Owner.IsActiveForHooks
//   CardModel   → !cardModel.HasBeenRemovedFromState && cardModel.Owner.IsActiveForHooks
//   PowerModel  → powerModel.Owner.CombatState != null && (powerModel.Owner.Player?.IsActiveForHooks ?? true)
```

`Player.CreateForNewRun` → `PopulateStartingInventory()` → `PopulateStartingRelics()` 会**自动**
把 `Character.StartingRelics` 装到 Player 上（`.recon/cb-Player.txt`），
所以 **Ironclad Boss 开箱即得 `BurningBlood`（战斗结束回血）**，一行代码都不用写。

### 7.3 三种实现路线对比

| 路线 | 做法 | 评价 |
|---|---|---|
| **(a) 真 `RelicModel` 挂在影子 Player 上** | `RelicCmd.Obtain(relic, shadowPlayer)` 或直接 `Player.CreateForNewRun(ModelDb.Character<Ironclad>(), ...)`（自动带起始遗物） | ✅ **推荐**。完全复用原版遗物逻辑、本地化、图标、`Hook` 派发。代价：mod 自定义遗物**必须**先 `ModHelper.AddModelToPool` 进某个 `RelicPoolModel`，否则 `relic.Pool` 会抛异常。 |
| **(b) 用 `PowerModel` 复刻遗物逻辑** | 给 Boss 挂一个自定义 Power，在里面实现 `AfterCardPlayed` 等 | ⚠️ 可行但没必要，只有当遗物需要「敌人专属」语义（例如玩家不能偷）时才考虑。注意 `Creature.IsSecondaryEnemy` 判定依赖 `Power.OwnerIsSecondaryEnemy`，别误用。 |
| **(c) 手写 Hook 代码** | 自己在 `MoveState._onPerform` 里直接写效果 | ⚠️ 最灵活但等于放弃整套框架，不推荐。 |

### 7.4 「敌方遗物」先例：**没有**

* `& $dotnet $cli search references\sts2.dll "EnemyRelic"` 无命中；`Models.Relics.*` 里的遗物全部是玩家遗物。
* 最接近的 `Models.Monsters.FakeMerchantMonster` 有一个 `THROW_RELIC_MOVE` + `_throwRelicTrigger = "throw"`，
  但反编译（`.recon/cb-Q7-FakeMerchantMonster.txt` L495/L557）显示它只是
  `DamageCmd.Attack(ThrowRelicDamage).FromMonster(this).WithAttackerAnim("throw", 0.15f)` ——
  **只是「把遗物当武器扔出去」的攻击演出，并没有真的持有 `RelicModel`**。
* 结论：**「敌人持有遗物」在本体里没有先例，但也不需要先例** —— 走影子 Player 就是正路。

### 7.5 置信度 / 风险

* 置信度：**高**（`RelicModel.Owner` 的 setter、`Pool` 的 `First(...)`、`IterateHookListeners` 的 relics 分支、
  `RelicCmd` 的签名全部为反编译原文）。
* 风险：
  * **中**：`RelicModel.Pool` 用的是 `First(...)`，**不在任何 `RelicPoolModel` 里的 mod 遗物一旦被访问 `.Pool`
    就会抛 `InvalidOperationException`**。→ mod 自定义遗物必须
    `ModHelper.AddModelToPool(typeof(SomeRelicPool), typeof(MyRelic))`，且**必须在游戏初始化前**（`isFrozen` 后 throw）。
  * **中**：`RelicModel.Owner` 的 setter 不允许换主（`Cannot move relic from X one owner to another`）。
    影子 Player 的遗物必须在 `Owner` 还是 null 时一次挂上。
  * **低**：遗物表现 `Flash()` / `UpdateTexture(TextureRect)` 会去找玩家侧 UI 节点；影子 Player 没有 UI，
    可能只是「闪不出来」（需实测确认不会 NRE）。
  * **低**：`PopulateStartingRelics` 依赖 `Character.StartingRelics`。若 Boss 想用「非本角色」的遗物组合，
    需要创建后再增删（`AddRelicInternal` / `RemoveRelicInternal`）。

### 7.6 附带确认

* `MonsterModel.ShouldReceiveCombatHooks => true`（`.recon/cb-MonsterModel.txt:351`），
  `EncounterModel.ShouldReceiveCombatHooks => false`，`ActModel.ShouldReceiveCombatHooks => false`。
* 因为 Boss 的 `Creature.Player != null`，`IterateHookListeners` 会走 `player` 分支 ——
  遗物与 Boss 自己的卡牌都会被自动注册；**但 `creature.Monster` 反而不会被注册**，
  所以如果 Boss 的 `MonsterModel` 需要实现 `AfterCardPlayed` 之类的 hook，就必须用 **P2 patch** 补上。

---

## 8. 本地化：怪物名 / 卡牌名从哪来 / mod 怎么提供中英文

### 8.1 结论

* 文本全部走 `LocString(table, key)` → `LocManager.Instance.GetTable(table)`。
* 怪物：table = `"monsters"`；`MonsterModel.Title => L10NMonsterLookup(Id.Entry + ".name")`；
  招式名 `GetBestiaryMoveName(moveId) => new LocString("monsters", Id.Entry + ".moves." + moveId + ".title")`。
* 卡牌：`CardModel.TitleLocString / Description / SelectionScreenPrompt`（table 由卡牌基类决定）。
* 语言：`eng` / `zhs`（简体） / `zht`（繁体）等，表文件是 `res://localization/<lang>/<table>.json`。
* **mod 提供文本的方式（官方支持，非 hack）**：
  在 mod 的 pck 里放 `res://<modId>/localization/<lang>/<与本体同名的表文件>.json`，
  `LocManager` 会在加载本体同名表时把 mod 的表 `MergeWith` 进去。

### 8.2 证据

```csharp
// LocString  (.recon/cb-LocString.txt)
public class LocString(string locTable, string locEntryKey)
{
    public string LocTable => locTable;
    public string LocEntryKey => locEntryKey;
    public static bool Exists(string table, string key) => LocManager.Instance.GetTable(table).HasEntry(key);
    public static LocString? GetIfExists(string table, string key) => Exists(table, key) ? new LocString(table, key) : null;
    public string GetFormattedText() => LocManager.Instance.SmartFormat(this, _variables);
    public string GetRawText() => LocManager.Instance.GetTable(LocTable).GetRawText(LocEntryKey);
}
```

```csharp
// MonsterModel
public static LocString L10NMonsterLookup(string entryName) => new LocString("monsters", entryName);
public virtual LocString Title => L10NMonsterLookup(base.Id.Entry + ".name");
protected LocString GetBestiaryMoveName(string moveId) => new LocString("monsters", Id.Entry + ".moves." + moveId + ".title");
protected const string _locTableName = "monsters";
```

```csharp
// LocManager.LoadTablesFromPath(language)   (.recon/cb-LocManager.txt:684-769)
private static string LocalizationAssetDir => "res://localization";
foreach (string current in ListLocalizationFiles(LocalizationAssetDir + "/" + language))
{
    string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(current);     // ← 表名 = 文件名
    Dictionary<string,string> val4 = LoadTable(text + "/" + current);
    LocTable fallback = (language != "eng") ? previousEngTables[fileNameWithoutExtension] : null;
    LocTable locTable = new LocTable(fileNameWithoutExtension, val4, fallback);
    ...
    foreach (string current2 in ModManager.GetModdedLocTables(language, current))    // ★ MOD 的表
    {
        Log.Info($"Found loc table from mod: {language} {current}. Merging with base loc table");
        locTable.MergeWith(LoadTable(current2));
    }
    val2[fileNameWithoutExtension] = locTable;
}
```

```csharp
// ModManager.GetModdedLocTables   (.recon/cb-ModManager.txt:1626-1649)
public static IEnumerable<string> GetModdedLocTables(string language, string file)
{
    foreach (Mod current in _mods)
        if (current.state == ModLoadState.Loaded)
        {
            string text = $"res://{current.manifest.id}/localization/{language}/{file}";
            if (ResourceLoader.Exists(text)) yield return text;
        }
}
```

```csharp
// LocManager._supportedLanguages（部分）  (.recon/cb-LocManager.txt:966-990)
obj.Add("zh_Hans", "zhs");
// 表: new[] { ("eng","en"), ("zhs","zh-hans"), ... }
```

**pck 里确实存在这些表**（`.recon/pck_index.txt` + 游戏 pck 二进制直接确认）：

`res://localization/{eng,zhs,zht}/` 下：
`acts.json, afflictions.json, ancients.json, ascension.json, badges.json, bestiary.json, card_keywords.json,
card_library.json, card_reward_ui.json, card_selection.json, cards.json, characters.json, combat_messages.json,
credits.json, enchantments.json, encounters.json, epochs.json, eras.json, events.json, extensions.json, ftues.json,
game_modes.json, game_over_screen.json, gameplay_ui.json, inspect_relic_screen.json, **intents.json**, main_menu_ui.json,
map.json, merchant_room.json, modifiers.json, **monsters.json**, orbs.json, potion_lab.json, potions.json, powers.json,
relic_collection.json, relics.json, rest_site_ui.json, rich_presence.json, run_history.json, settings_ui.json,
static_hover_tips.json, stats_screen.json, timeline.json, vfx.json`

二进制直查结果（`SlayTheSpire2.pck` 尾部 48 MB）：

```
FOUND: localization/zhs/monsters.json      FOUND: localization/eng/monsters.json
FOUND: localization/zhs/cards.json         FOUND: localization/zhs/intents.json
FOUND: localization/zhs/relics.json        FOUND: localization/zhs/encounters.json
FOUND: localization/zhs/characters.json    FOUND: localization/zhs/bestiary.json
```

### 8.3 实操结论

**★ 先搞清 `ModelId.Entry` 的大小写**（这是最容易踩的坑）：

```csharp
// MegaCrit.Sts2.Core.Models.ModelId       (.recon/cb-Q7-ModelId.txt)
public record ModelId(string Category, string Entry)
{
    private const string _bannedSuffix = "_MODEL";
    public static string SlugifyCategory(string category)
    { string t = StringHelper.Slugify(category);
      if (t.EndsWith("_MODEL")) t = t.Substring(0, t.Length - "_MODEL".Length);   // "MonsterModel" → "MONSTER"
      return t; }
    public override string ToString() => Category + "." + Entry;
}

// MegaCrit.Sts2.Core.Helpers.StringHelper   (.recon/cb-Q7-StringHelper.txt)
public static string Slugify(string txt)
{
    string a = CamelCaseRegex().Replace(txt.Trim(), "$1_$2");   // IroncladBoss → Ironclad_Boss
    string b = WhitespaceRegex().Replace(a.ToUpperInvariant(), "_");
    return SpecialCharRegex().Replace(b, "");                   // → IRONCLAD_BOSS
}
```

⇒ **`ModelId.Entry` 是 UPPER_SNAKE_CASE**：
`IroncladBoss` → `Category="MONSTER"`, `Entry="IRONCLAD_BOSS"`, `ToString()="MONSTER.IRONCLAD_BOSS"`。
所以 loc 的 key 必须是 **大写**。

* Boss 名字 → 在 mod 的 `localization/eng/monsters.json` 与 `localization/zhs/monsters.json` 里加：

  ```json
  { "IRONCLAD_BOSS.name": "The Ironclad" }      // eng
  { "IRONCLAD_BOSS.name": "铁甲战士" }           // zhs
  ```

  （`MonsterModel.Title => L10NMonsterLookup(Id.Entry + ".name")`，table = `"monsters"`。）

* 招式名 → `"IRONCLAD_BOSS.moves.<MoveStateId>.title"`（`GetBestiaryMoveName`）。
* 遭遇名/败北文案 → `localization/<lang>/encounters.json`，key 形如 `"IRONCLAD_BOSS_ENCOUNTER.title"` /
  `".loss"`。原版格式参考（子侦察抽出的 `res://localization/eng/encounters.json`，
  转储 `.recon/cb-q347-loc-eng-encounters.txt`）：

  ```json
  { "CEREMONIAL_BEAST_BOSS.title": "Ceremonial Beast",
    "CEREMONIAL_BEAST_BOSS.loss": "{character} was put to rest by the [gold]{encounter}[/gold]." }
  ```
  （支持 `[gold]...[/gold]` BBCode 与 `{placeholder}` SmartFormat 占位符。）

* 卡牌名/描述 → `cards.json`（用原版卡就不需要，原版 loc 已存在）。
* **⚠️ 限制**：`LocManager` 是**以本体表文件列表为驱动**的（`foreach (file in baseTables) { ...merge mod 的同名文件... }`），
  所以 mod **不能新增一个本体没有的表名**（比如 `charboss.json`）——必须用既有表名
  （`monsters`/`cards`/`relics`/`encounters`/`characters`/`intents`/`bestiary`/...）。
* **语言列表（14 种）**：游戏 pck 内的 `localization/README`（转储 `.recon/cb-q347-loc-README.txt`）列出
  `eng, deu, esp, ind, ita, jpn, kor, pol, ptb, rus, tha, tur, zhs, zht`（`zhs` = 简体，`zht` = 繁体）。
  `LocManager` 对非 `eng` 语言会**先加载 `eng` 表作为 fallback**
  （`if (language != "eng") val = LoadTablesFromPath("eng").Item1;`，然后 `new LocTable(name, dict, fallback)`）。
  ⇒ mod 只需提供 `eng` + `zhs`（+ 可选 `zht`），缺的 key 会自动回落英文。

**★ 顺带发现的一个 mod 结构坑**：真实 `ModManifest`（`.recon/cb-q347-ModManifest.txt`）的字段只有

```csharp
public string? id; name; author; description; version;
public bool hasPck;            // [JsonPropertyName("has_pck")]
public bool hasDll;            // [JsonPropertyName("has_dll")]
public List<ModDependency>? dependencies;
public bool affectsGameplay = true;
public string? minGameVersion;
public string initializerMethod;
```

**没有 `pck_name` 字段**。而 `ModManager` 是从 `Path.Combine(mod.path, modId + ".pck")` 找 pck 的
（`.recon/cb-ModManager.txt` L1477）。⇒ 工作区 `src/com.vibecoding.sts2mod.json` 里的 `"pck_name"` 是**无效字段**，
pck 必须命名为 **`<manifest.id>.pck`** 并放在 mod 文件夹里。
`localization/` 目录同理必须放在 `<modId>/` 命名空间下（`res://<modId>/localization/...`）。

**★ 顺带发现的一个 mod 结构坑**：真实 `ModManifest`（`.recon/cb-q347-ModManifest.txt`）的字段只有

```csharp
public string? id; name; author; description; version;
public bool hasPck;            // [JsonPropertyName("has_pck")]
public bool hasDll;            // [JsonPropertyName("has_dll")]
public List<ModDependency>? dependencies;
public bool affectsGameplay = true;
public string? minGameVersion;
public string initializerMethod;
```

**没有 `pck_name` 字段**。而 `ModManager` 是从 `Path.Combine(mod.path, modId + ".pck")` 找 pck 的
（`.recon/cb-ModManager.txt` L1477）。⇒ 工作区 `src/com.vibecoding.sts2mod.json` 里的 `"pck_name"` 是**无效字段**，
pck 必须命名为 **`<manifest.id>.pck`** 并放在 mod 文件夹里。
`localization/` 目录同理必须放在 `<modId>/` 命名空间下（`res://<modId>/localization/...`）。

**★ 顺带发现的一个 mod 结构坑**：真实 `ModManifest`（`.recon/cb-q347-ModManifest.txt`）的字段只有

```csharp
public string? id; name; author; description; version;
public bool hasPck;            // [JsonPropertyName("has_pck")]
public bool hasDll;            // [JsonPropertyName("has_dll")]
public List<ModDependency>? dependencies;
public bool affectsGameplay = true;
public string? minGameVersion;
public string initializerMethod;
```

**没有 `pck_name` 字段**。而 `ModManager` 是从 `Path.Combine(mod.path, modId + ".pck")` 找 pck 的
（`.recon/cb-ModManager.txt` L1477）。⇒ 工作区 `src/com.vibecoding.sts2mod.json` 里的 `"pck_name"` 是**无效字段**，
pck 必须命名为 **`<manifest.id>.pck`** 并放在 mod 文件夹里。
`localization/` 目录同理必须放在 `<modId>/` 命名空间下（`res://<modId>/localization/...`）。
* **⚠️ 未验证**：`_gameToWeblateLanguage` 与 CJK 的 `ConvertToW` 分支
  （`if (flag) val4 = val4.ToDictionary(kvp => kvp.Key, kvp => ConvertToW(kvp.Value))`）在 debug build 下的行为；
  release build 应该无影响。

### 8.4 置信度 / 风险

* 置信度：**高**（表名、路径、Merge 逻辑、语言码全部读到）。
* 风险：**低**。唯一需要注意的是 key 必须与 `ModelId.Entry` 严格一致（`StringHelper.Slugify(type.Name)` 的结果），
  这个需要实测 `Id.Entry` 的实际取值（例如 `IroncladBoss` → `ironclad_boss`？含数字/缩写时要小心）。

---

## 9. 卡牌与遗物的复用

### 9.1 原版角色及其卡牌命名空间

* 命名空间：**`MegaCrit.Sts2.Core.Models.Cards.*`**（`& $dotnet $cli types` 统计到 **400** 个类型，
  转储在 `.recon/cb-types-cards.txt`）。
* 原版**可玩角色**共 5 个（`ModelDb.AllCharacters`，`.recon/cb-ModelDb.txt`）：

```csharp
public static IEnumerable<CharacterModel> AllCharacters => new CharacterModel[5]
{ Character<Ironclad>(), Character<Silent>(), Character<Regent>(), Character<Necrobinder>(), Character<Defect>() };
```

* 命名空间 `MegaCrit.Sts2.Core.Models.Characters.*`；起始卡组/遗物定义在 `CharacterModel`
  的抽象属性上（`.recon/cb-CharacterModel.txt`）：

```csharp
public abstract class CharacterModel : AbstractModel
{
    public const string locTable = "characters";
    public virtual bool IsPlayable => true;
    public LocString Title => new LocString("characters", Id.Entry + ".title");
    public abstract int StartingHp { get; }
    public abstract int StartingGold { get; }
    public virtual int MaxEnergy => 3;
    public virtual int BaseOrbSlotCount => 0;
    public abstract CardPoolModel  CardPool  { get; }
    public abstract RelicPoolModel RelicPool { get; }
    public abstract PotionPoolModel PotionPool { get; }
    public abstract IEnumerable<CardModel>     StartingDeck    { get; }
    public abstract IReadOnlyList<RelicModel>  StartingRelics  { get; }
    public virtual  IReadOnlyList<PotionModel> StartingPotions => Array.Empty<PotionModel>();
    private string VisualsPath => SceneHelper.GetScenePath("creature_visuals/" + Id.Entry.ToLowerInvariant());
    public virtual CreatureAnimator GenerateAnimator(MegaSprite controller, Creature creature) { ... }
    public NCreatureVisuals CreateVisuals() => ...;
    public abstract float AttackAnimDelay { get; }
    public abstract float CastAnimDelay  { get; }
}
```

```csharp
// MegaCrit.Sts2.Core.Models.Characters.Ironclad   (.recon/cb-Character-Ironclad.txt)
public sealed class Ironclad : CharacterModel
{
    public override int StartingHp => 80;
    public override int StartingGold => 99;
    public override CardPoolModel  CardPool  => ModelDb.CardPool<IroncladCardPool>();
    public override RelicPoolModel RelicPool => ModelDb.RelicPool<IroncladRelicPool>();
    public override PotionPoolModel PotionPool => ModelDb.PotionPool<IroncladPotionPool>();
    public override IEnumerable<CardModel> StartingDeck => new CardModel[10]
    { ModelDb.Card<StrikeIronclad>(), ×5 , ModelDb.Card<DefendIronclad>(), ×4 , ModelDb.Card<Bash>() };
    public override IReadOnlyList<RelicModel> StartingRelics
        => new <>z__ReadOnlySingleElementList<RelicModel>(ModelDb.Relic<BurningBlood>());
    public override float AttackAnimDelay => 0.15f;
    public override float CastAnimDelay  => 0.25f;
}
```

（其余 4 个角色的明细见下表 —— 已由子侦察逐一核对反编译原文，转储 `.recon/cb-characters.txt`。）

**★ 五个角色的全部关键数据（全部为反编译原文）**

| 类（`Models.Characters.*`） | StartingHp | StartingGold | MaxEnergy | BaseOrbSlotCount | UnlocksAfterRunAs |
|---|---|---|---|---|---|
| `Ironclad` | 80 | 99 | 3（默认） | 0 | `null` |
| `Silent` | 70 | 99 | 3（默认） | 0 | `null` |
| `Regent` | 75 | 99 | 3（默认） | 0 | `Silent` |
| `Necrobinder` | 66 | 99 | 3（默认） | 0 | `Regent` |
| `Defect` | 75 | 99 | 3（默认） | **3** | `Necrobinder` |

> **`MaxEnergy` 没有任何可玩角色覆写** → 五个角色全是 3（`CharacterModel.MaxEnergy => 3`）。
> `StartingGold` 全部 99，`StartingPotions` 全部 `Array.Empty<PotionModel>()`。

| 角色 | `CardPool` | `RelicPool` | `PotionPool` |
|---|---|---|---|
| Ironclad | `ModelDb.CardPool<IroncladCardPool>()` | `ModelDb.RelicPool<IroncladRelicPool>()` | `ModelDb.PotionPool<IroncladPotionPool>()` |
| Silent | `SilentCardPool` | `SilentRelicPool` | `SilentPotionPool` |
| Regent | `RegentCardPool` | `RegentRelicPool` | `RegentPotionPool` |
| Necrobinder | `NecrobinderCardPool` | `NecrobinderRelicPool` | `NecrobinderPotionPool` |
| Defect | `DefectCardPool` | `DefectRelicPool` | `DefectPotionPool` |

**起始卡组（原文逐项，全部写成 `ModelDb.Card<T>()`）**

| 角色 | 张数 | 内容 |
|---|---|---|
| Ironclad | 10 | `StrikeIronclad`×5、`DefendIronclad`×4、`Bash`×1 |
| Silent | 12 | `StrikeSilent`×5、`DefendSilent`×5、`Neutralize`×1、`Survivor`×1 |
| Regent | 10 | `StrikeRegent`×4、`DefendRegent`×4、`FallingStar`×1、`Venerate`×1 |
| Necrobinder | 10 | `StrikeNecrobinder`×4、`DefendNecrobinder`×4、`Bodyguard`×1、`Unleash`×1 |
| Defect | 10 | `StrikeDefect`×4、`DefendDefect`×4、`Zap`×1、`Dualcast`×1 |

**起始遗物（原文）**

| 角色 | `StartingRelics` |
|---|---|
| Ironclad | `[ ModelDb.Relic<BurningBlood>() ]` |
| Silent | `[ ModelDb.Relic<RingOfTheSnake>() ]` |
| Regent | `[ ModelDb.Relic<DivineRight>() ]` |
| Necrobinder | `[ ModelDb.Relic<BoundPhylactery>() ]` |
| Defect | `[ ModelDb.Relic<CrackedCore>() ]` |

**动画/资源差异（做「Boss 化」时会用到）**

| 角色 | AttackAnimDelay | CastAnimDelay | PowerUpAnimDelay | 额外 trigger |
|---|---|---|---|---|
| Ironclad | 0.15 | 0.25 | =Cast | `"heavyAttack"` → anim `attack_heavy`；静态 `GetHeavyAnimIfApplicable` / `GetHeavyAttackDelayIfApplicable` |
| Silent | 0.15 | 0.25 | =Cast | `"Shiv"` → anim `shiv` |
| Regent | 0.15 | 0.25 | =Cast | `"sovereignBladeTrigger"` → anim `attack_sovereign` |
| Necrobinder | 0.15 | **0.40** | =Cast | `"summonTrigger"` → anim `cast`；`Cast`/`PowerUp` → `cast_mighty`；`ExtraAssetPaths` 加 `vfx/vfx_heal_osty`、`creature_visuals/osty` |
| Defect | 0.15 | 0.25 | **0.5** | `PowerUp` → anim `process`；`BaseOrbSlotCount => 3` |

**卡池枚举方式（显式列举，不是反射）**

```csharp
public abstract class CardPoolModel : AbstractModel, IPoolModel
{
    protected abstract CardModel[] GenerateAllCards();                  // ★ 唯一数据源
    public IEnumerable<CardModel> AllCards
    {
        get { if (_allCards == null)
              { _allCards = GenerateAllCards();
                _allCards = ModHelper.ConcatModelsFromMods(this, _allCards).ToArray(); }   // ★ mod 内容在这里并入
              return _allCards; }
    }
    public IEnumerable<ModelId> AllCardIds => _allCardIds ??= AllCards.Select(c => c.Id).ToHashSet();
}
```

| 卡池 | 张数 |
|---|---|
| `IroncladCardPool` | 90 |
| `SilentCardPool` | 91 |
| `RegentCardPool` | 91 |
| `NecrobinderCardPool` | 91 |
| `DefectCardPool` | 91 |

外加 7 个共享池：`ColorlessCardPool, CurseCardPool, DeprecatedCardPool, EventCardPool, QuestCardPool,
StatusCardPool, TokenCardPool`。遗物池每个角色 **8 件**（起始遗物就在这 8 件里），共享池 `SharedRelicPool` 118 件；
药水池来自 `*4Epoch.Potions`，`SharedPotionPool` 45 件。

**mod 往原版池里加卡的两条路**：
(a) Harmony Postfix 到具体池的 `protected CardModel[] GenerateAllCards()`（`ref CardModel[] __result`）；
(b) 官方 `ModHelper.AddModelToPool(typeof(IroncladCardPool), typeof(MyCard))` —— **必须在游戏初始化前调用**，
否则会 throw：`"Tried to add model ... but it's too late! You must add content before the game is initialized."`
（`ModHelper.ConcatModelsFromMods` 首次读取某池时会把该池 `isFrozen = true`）。
`AddModelToPool` 支持任何 `IPoolModel`（CardPool / RelicPool / PotionPool），**但不支持 Encounter**
（`ActModel.GenerateAllEncounters` 是硬编码数组，`EncounterModel` 不是 pool model）。

### 9.2 mod 能否直接引用原版卡牌类型并放进敌人卡组？

**能，而且是最省事的做法。**

* 它们都是 `public sealed class XxxYyy : CardModel`，mod 的 csproj 已经 `Reference` 了 `references/sts2.dll`
  （见 `src/Sts2ModScaffold.csproj` 的既有配置），直接 `using MegaCrit.Sts2.Core.Models.Cards;` 即可。
* 实例化必须走 `ModelDb.Card<T>()`（canonical 不可变）→ `.ToMutable()`，或更推荐
  `combatState.CreateCard(ModelDb.Card<T>(), shadowPlayer)`（一步完成 clone + 挂 owner + 进 `_allCards`）。
* 卡牌的 `OnPlay` 逻辑已经在 §4.2(f) 验证过：只要 `card.Owner.Creature` 是 Boss 且 `Side == Enemy`，
  原版卡牌的全部效果都会正确地以 Boss 为来源、以玩家为目标。
* **唯一要小心的是「需要玩家交互的卡」**（例如选牌、选弃牌、`ChooseACard` 之类）：
  它们会调用 `PlayerChoiceContext.SignalPlayerChoiceBegun`。用 `BlockingPlayerChoiceContext` 时这些卡会**卡住或行为异常**；
  MVP 阶段建议**只给 Boss 用无交互卡**（打击/防御/AOE/加 power）。

### 9.3 明细（子侦察已补全，见 §9.2 后方的完整表格）

* 五个角色的 `StartingHp / MaxEnergy / StartingDeck / StartingRelics / CardPool` 已全部核对（见上文表格）。
* `CardPoolModel.AllCards = GenerateAllCards() ∪ ModHelper.ConcatModelsFromMods(...)` —— **显式列举**，
  与命名空间/程序集/反射无关。

---

## 10. 可行性总评

### 10.1 结论

**完全可行（整体）**，但需要接受 3 处妥协、并解决 3 个风险。

| 维度 | 判定 |
|---|---|
| 替换一幕 Boss | ✅ 完全可行，纯代码，无需新美术 |
| 定义新的 Boss 怪物（含 intent / 招式状态机） | ✅ 完全可行，官方扩展点 |
| Boss 持有卡组并像玩家一样打牌 | ✅ 可行，走 `CardCmd.AutoPlay` + 影子 `Player` |
| Boss 持有并触发遗物 | ✅ 可行（依赖影子 Player），细节待子任务补充 |
| Boss 使用原版角色的贴图/动画 | ✅ 可行（覆写 `VisualsPath` 指向 `res://scenes/creature_visuals/<char>.tscn`） |
| 中英文文本 | ✅ 可行，官方 mod loc 机制 |
| 多人联机 | ⚠️ **高风险**，checksum / 同步未验证，MVP 建议只做单人 |

**妥协清单**

1. 必须给 Boss 造一个影子 `Player`，并为此写 1~3 个 Harmony patch 来隔离它在 `CombatState.Players` 中的存在。
2. 敌人出牌走 `AutoPlay`，意味着**不经过 `CanPlay` / 能量自动扣除**；Boss 的能量需要 mod 自己记账
   （可以每回合 `PlayerCombatState.Energy = N`）。
3. 部分需要玩家选择的原版卡不适合敌人使用（会阻塞或行为异常）。

**最大 3 个技术风险**

1. **影子 Player 对战斗状态的污染**：`CombatState.Players` 被 `CombatManager` 用于
   回合开始（抽 5 张 + 重置能量）、回合结束（弃手牌 + `EndOfTurnCleanup`）、玩家死亡处理、
   `EffectCombatId` 追踪。必须阻断。
   证据：`.recon/cb-CombatManager.txt` 中 `foreach (Player ... in turnState.State.Players)` 的多个状态机
   + `.recon/cb-CombatState.txt:562 public IReadOnlyList<Player> Players => PlayerCreatures.Select(c => c.Player)`。
2. **Boss 死亡被当成玩家死亡**：~~最高优先级实测项~~ → **已大幅降级**（见 §6.6）。
   `CombatManager.IsCombatEnding` 只检查 `Enemies.Any(e => e.IsAlive && e.IsPrimaryEnemy)`，
   与 `Player` 完全无关；失败只有 `LoseCombat()` 一个入口，而在 sts2.dll 里**找不到 `LoseCombat()` /
   `HandlePlayerDeath()` 的任何调用点**（对 13 个 `*Combat*` 类型 + 多个候选类做了整类反编译扫描）。
   ⇒ 剩余风险：仍有「外部/未知路径调用 `LoseCombat()`」的可能，**建议实机确认一次**，但已不是 Go/No-Go 项。
3. **异步时序 / 多人同步**：敌人连续打牌必须完全串行在 `ExecuteEnemyTurn` 的 `await` 链里
   （靠 `MoveState._onPerform` 可以做到），但 `OnPlayWrapper` 内部还会 await
   `CombatManager.WaitForUnpause()`、`Hook.AfterCardPlayed(...)`、`CheckForEmptyHand(...)`，
   这些在「敌方回合」阶段执行是否安全、以及 `RunManager.ChecksumTracker` 对影子 Player 的
   checksum 影响，都未验证。

### 10.2 最小可行垂直切片（MVP：让 Boss 打我们一张打击）

目标：进 Act1 Boss 房 → 看到 Ironclad 立绘 → 它用 `StrikeIronclad` 打我们 6 点伤害。

| 步骤 | 做什么 | 用到的 API / 证据 |
|---|---|---|
| **S1** | 定义 `IroncladBoss : MonsterModel`：`MinInitialHp/MaxInitialHp`、`VisualsPath => "res://scenes/creature_visuals/ironclad.tscn"`、`GenerateMoveStateMachine()` 返回一个 `MonsterMoveStateMachine`，含**单个** `MoveState("STRIKE_MOVE", async targets => {...}, new SingleAttackIntent(0))`。动作体先留空（只暂停 1 秒）。 | `.recon/MoveState.txt`（ctor 签名）、`.recon/cb-MonsterModel.txt:712`、`.recon/pck_index.txt:890` |
| **S2** | 定义 `IroncladBossEncounter : EncounterModel`：`RoomType => RoomType.Boss`、`AllPossibleMonsters => [ModelDb.Monster<IroncladBoss>()]`、`GenerateMonsters() => [(ModelDb.Monster<IroncladBoss>().ToMutable(), null)]`。 | `.recon/cb-enc-TestSubjectBoss.txt` |
| **S3** | **换 Boss（推荐用官方 API + 双保险）**：<br>① Harmony Postfix `ActModel.PullNextEncounter(RoomType)` → `if (/* Boss 且属于我们接管的 act */) __result = ModelDb.Encounter<IroncladBossEncounter>();`（**必须 canonical，不能 `.ToMutable()`**）<br>② Harmony Postfix `ActModel.GenerateRooms(...)`（用 `__instance.SetBossEncounter(...)`）+ Postfix `ActModel.ApplyDiscoveryOrderModifications(...)`（否则 ① 之后的 ② 会被覆盖）<br>③ 等 `NRun.Instance != null` 时调 `MapCmd.SetBossEncounter(runState, encounter)` 刷顶栏/地图屏图标<br>④ 可选：Postfix `ActModel.ValidateRoomsAfterLoad(Rng)` 覆盖读档 | `.recon/cb-MapCmd.txt`、`.recon/cb-ActModel.txt`、`.recon/cb-RunManager.txt` L4077-4081/L4191、`.recon/cb-injection-points.txt` |
| **S4** | 在 Boss 的 `MonsterModel.AfterAddedToRoom()`（`virtual Task`，在怪物进场后被 await）里：<br>① `var sp = Player.CreateForNewRun(ModelDb.Character<Ironclad>(), unlockState, ourNetId);`<br>② `sp.RunState = runState;`（只能赋一次）<br>③ `sp.ResetCombatState();`<br>④ 反射：`sp.<Creature>k__BackingField = this.Creature;` 且 `this.Creature.<Player>k__BackingField = sp;`<br>⑤ `sp.PopulateCombatState(rng, combatState);`（把 Ironclad 起始卡组灌进 DrawPile）<br>⑥ `sp.IsActiveForHooks = true;` | `.recon/cb-Player.txt`、`Player.CreateForNewRun` / `ResetCombatState` / `PopulateCombatState` 方法体 |
| **S5** | 写 P1 + P1b 两个 Harmony patch：过滤 `CombatState.PlayerCreatures`；强制 `NCombatRoom.AddCreature` 走 `_enemyContainer`。 | `.recon/cb-CombatState.txt:554-562`、`.recon/cb-NCombatRoom.txt:867-879` |
| **S6** | 在 S1 的 `onPerform` 里真正打牌：<br>`var card = sp.PlayerCombatState.DrawPile.Cards.First(c => c.Id == ModelDb.GetId<StrikeIronclad>());`<br>`await CardCmd.AutoPlay(new BlockingPlayerChoiceContext(), card, targets[0], AutoPlayType.Default);` | `.recon/cb-CardCmd-AutoPlay.txt` |
| **S7（必做验证）** | 打死 Boss，确认进入**胜利结算**而不是 run 失败 → 验证风险 #2。 | — |

> S4 的第 ④ 步（反射写 `<Player>k__BackingField` / `<Creature>k__BackingField`）是唯一的「非官方」操作。
> 如果不想用反射，替代方案是 Harmony 给 `Creature.get_Player` 做 prefix/postfix —— 但反射改字段更可控
> （只影响这一只实例）。

### 10.3 推荐实施顺序

1. **里程碑 1（技术验证）**：S1 + S2 + S3 —— 确认「Boss 被换成了 Ironclad」，用一个 `DamageCmd.Attack(...).FromMonster(this)`
   的普通招式占位（不涉及卡牌系统）。此阶段零风险。
2. **里程碑 2（核心验证）**：S4 + S5 + S6 —— Boss 打出第一张 `StrikeIronclad`。**这是本项目的 Go/No-Go 点。**
3. **里程碑 3**：Boss 卡组 AI（每回合从自己的 `DrawPile`/`DiscardPile` 循环选牌、能量预算）、
   多卡连打、intent 显示与卡牌预览。
4. **里程碑 4**：遗物（复用影子 Player 的 `Relics`）；Boss 专属卡/遗物；三幕各一个 Boss；本地化。
5. **里程碑 5（可选/高风险）**：多人联机适配。

---

## 附录 A：本报告新增的反编译转储（`.recon/`）

| 文件 | 内容 |
|---|---|
| `cb-EncounterModel.txt` | `MegaCrit.Sts2.Core.Models.EncounterModel` 完整类型 |
| `cb-enc-TestSubjectBoss.txt` | 一个 Boss 遭遇的完整实现样例 |
| `cb-ActModel.txt` | `ActModel` 完整类型（含 `GenerateRooms` 选 Boss） |
| `cb-ModelDb.txt` | `ModelDb` 完整类型（类型发现 / 注册 / `GetId`） |
| `cb-ReflectionHelper.txt` | `ReflectionHelper`（`ModTypes` / `GetSubtypesInMods`） |
| `cb-CombatState.txt` | `CombatState` 完整类型（`Players`/`PlayerCreatures`/`IterateHookListeners`/`CreateCard`） |
| `cb-CombatState-members.txt` | `CombatState` 成员清单 |
| `cb-CombatManager.txt` | `CombatManager` 完整类型（`ExecuteEnemyTurn` / `Players` 消费点） |
| `cb-Creature.txt` | `Creature` 完整类型（`IsPlayer` / `IsMonster` / `Player` / `Side`） |
| `cb-Creature-members.txt` | `Creature` 成员清单 |
| `cb-NCombatRoom.txt` | `NCombatRoom` 完整类型（`AddCreature` 容器选择 / 布局） |
| `cb-NCreature.txt` | `NCreature`（`PerformIntent` 只播动画） |
| `cb-CombatRoom.txt` | `CombatRoom`（`StartCombat` 生成怪物） |
| `cb-CardModel.txt` | `CardModel` 完整类型（`Owner`/`Pile`/`CombatState`/`OnPlayWrapper`） |
| `cb-CardCmd.txt` / `cb-CardCmd-AutoPlay.txt` | `CardCmd`（`AutoPlay` 完整实现） |
| `cb-Card-StrikeIronclad.txt` / `cb-Card-Bash.txt` | 原版起始攻击牌的 `OnPlay` |
| `cb-AttackCommand.txt` | `AttackCommand`（`FromCard` 的伤害来源解析） |
| `cb-Player.txt` | `Player` 完整类型（`CreateForNewRun` / `ResetCombatState` / `Piles` / `RunState`） |
| `cb-PlayerCombatState.txt` | `PlayerCombatState`（5 个牌堆 + 能量） |
| `cb-Player-PopulateCombatState.txt` | 战斗内卡组灌入流程 |
| `cb-CharacterModel.txt` / `cb-Character-Ironclad.txt` | `CharacterModel` / `Ironclad`（起始卡组与遗物） |
| `cb-types-cards.txt` | `Models.Cards.*` 全部 400 个类型 |
| `cb-MonsterModel.txt` | `MonsterModel` 完整类型 |
| `cb-Monster-TestSubject.txt` | 一个真实怪物的完整实现（含 `GenerateMoveStateMachine`） |
| `cb-MonsterMoveStateMachine.txt` / `cb-MonsterState.txt` / `cb-ConditionalBranchState.txt` | 招式状态机 |
| `cb-HookPlayerChoiceContext.txt` | `HookPlayerChoiceContext` |
| `cb-ModManager.txt` | `ModManager`（pck 挂载 / DLL 加载 / mod loc 表） |
| `cb-LocManager.txt` / `cb-LocString.txt` | 本地化系统 |
| `cb-RelicModel.txt` / `cb-RelicModel-members.txt` | `RelicModel`（`Owner` setter / `Pool` / `ShouldReceiveCombatHooks`） |
| `cb-Q7-ModelId.txt` | `ModelId`（`SlugifyCategory` / `Entry` 大小写规则） |
| `cb-Q7-StringHelper.txt` | `StringHelper.Slugify`（UPPER_SNAKE 的成因） |
| `cb-Q7-RelicCmd.txt` | `RelicCmd.Obtain/Remove/Replace/Melt` |
| `cb-Q7-FakeMerchantMonster.txt` | 「敌方遗物先例」的否定证据（只是攻击演出） |
| `cb-Q7-PowerModel.txt` | `PowerModel`（遗物替代路线的备选） |
| `cb-q347-NCreatureVisuals.txt` / `cb-q347-CreatureAnimator.txt` / `cb-MegaSprite.txt` | 视觉场景的节点契约与 spine 封装 |
| `cb-q347-ironclad-skel-data-tres.txt` | Ironclad 骨骼动画表（`idle_loop/attack/attack_heavy/hurt/die`） |
| `cb-q347-ModManifest.txt` | 真实 `ModManifest` 字段（**无 `pck_name`**） |
| `cb-q347-loc-README.txt` / `cb-q347-loc-completion.txt` | 游戏自带本地化说明（14 种语言）与完成度表 |
| `cb-q347-loc-eng-encounters.txt` / `-bestiary` / `-relics` / `-extensions` / `-characters`（eng+zhs） | 抽出的原版 loc 表样例，用于对照 key 命名规范 |

| `cb-RelicModel.txt` / `cb-RelicModel-members.txt` | `RelicModel`（`Owner` setter / `Pool` / `ShouldReceiveCombatHooks`） |
| `cb-Q7-ModelId.txt` | `ModelId`（`SlugifyCategory` / `Entry` 大小写规则） |
| `cb-Q7-StringHelper.txt` | `StringHelper.Slugify`（UPPER_SNAKE 的成因） |
| `cb-Q7-RelicCmd.txt` | `RelicCmd.Obtain/Remove/Replace/Melt` |
| `cb-Q7-FakeMerchantMonster.txt` | 「敌方遗物先例」的否定证据（只是攻击演出） |
| `cb-Q7-PowerModel.txt` | `PowerModel`（遗物替代路线的备选） |
| `cb-q347-NCreatureVisuals.txt` / `cb-q347-CreatureAnimator.txt` / `cb-MegaSprite.txt` | 视觉场景的节点契约与 spine 封装 |
| `cb-q347-ironclad-skel-data-tres.txt` | Ironclad 骨骼动画表（`idle_loop/attack/attack_heavy/hurt/die`） |
| `cb-q347-ModManifest.txt` | 真实 `ModManifest` 字段（**无 `pck_name`**） |
| `cb-q347-loc-README.txt` / `cb-q347-loc-completion.txt` | 游戏自带本地化说明（14 种语言）与完成度表 |
| `cb-q347-loc-eng-encounters.txt` / `-bestiary` / `-relics` / `-extensions` / `-characters`（eng+zhs） | 抽出的原版 loc 表样例，用于对照 key 命名规范 |

### 由并行子侦察额外产出（Q1/Q9 专项，共 27 个）

`.recon/cb-injection-points.txt`、`cb-q9-summary.txt`、`cb-MapCmd.txt`、`cb-ActConsoleCmd.txt`、
`cb-encounter-bosses.txt`、`cb-rooms.txt`、`cb-acts.txt`、`cb-map.txt`、`cb-RunManager.txt`、`cb-RunState.txt`、
`cb-CombatManager-full.txt`、`cb-CombatManager-setup.txt`、`cb-CombatManager-startcombat.txt`、
`cb-Hook-mapcombat.txt`、`cb-MonsterModel-members.txt`、`cb-MonsterModel-visuals.txt`、`cb-monster-addtoroom.txt`、
`cb-ModelDb-members.txt`、`cb-ModelDb-inject.txt`、`cb-ModelDb-getid.txt`、`cb-ModHelper-concat.txt`、
`cb-ModHelper-api.txt`、`cb-characters.txt`、`cb-cardpools.txt`、`cb-relicpotionpools.txt`、`cb-alltypes.txt`、
`cb-Q7-*.txt`（另见上表）、`cb-q347-*.txt`（另见上表）。

> 注：`cb-MonsterModel.txt` 由主报告与我（Q1/Q9 子侦察）各自生成过一次，内容一致（行号已验证），
> 报告中对它的引用行号 329/351/365/561/565/575/711/714/720/725/752/756 已逐行复核。
>
> 全程**未修改** `src/`、`rules.md`、任何 `.csproj`；未联网。

## 附录 B：直接引用的既有 `.recon` 文件

`MoveState.txt`、`MonsterModel-members.txt`、`MonsterModel-PerformMove-body.txt`、`MonsterModel-exec.txt`、
`Creature-TakeTurn-body.txt`、`ExecuteEnemyTurn-body.txt`、`CardPlay.txt`、`t-PlayCardAction.txt`、
`m-CardModel-TryManualPlay.txt`、`m-CardModel-EnqueueManualPlay.txt`、`m-CardModel-OnPlayWrapper.txt`、
`m-CardModel-OnPlayWrapper-MoveNext.txt`、`m-CardModel-OnPlayGenPlayCount.txt`、`m-CardCmd.txt`、
`M-ICombatState.txt`、`M-CardModel.txt`、`M-PlayerChoiceContext.txt`、`t-BlockingPlayerChoiceContext.txt`、
`t-BranchingPlayerChoiceContext.txt`、`Hook-members.txt`、`M-ModHelper.txt`、`M-IModManagerFileIo.txt`、
`pck_index.txt`。

以及 `rules.md`（Hook 签名表，行号引用见 §1.3）。

---

*报告状态：全部 10 个问题已完成。
Q1/Q9 由并行子侦察交叉验证并修正（新增官方 API `MapCmd.SetBossEncounter`、
真实漏斗 `ActModel.PullNextEncounter`、`GenerateRooms` 被 `ApplyDiscoveryOrderModifications` 覆盖的陷阱、
`CombatState.Encounter` 实为 `private init` 的更正）；Q7 的「敌方遗物先例」经确认**不存在**；
Q3 的场景节点契约已补充。
仍标注为**未验证**的项：`LoseCombat()` 的外部调用者、多人 checksum、
`MapCmd.SetBossEncounter` 的调用时机、Ironclad 骨骼是否存在 `cast` 动画、影子 Player 的实机表现。*
