# 怪物 / Intent / Move 系统逆向侦察报告

目标：为 Harmony Mod 定位「特定 BOSS 使用特定大招时、在效果结算**之前**播放过场动画」的 Hook 点，
并把用户给出的中文 BOSS / 招式名映射到游戏真实 id。

- 程序集：`references\sts2.dll`（9,757,184 bytes）
- 反编译器：`tools\ilspy-cli`（用法见 `docs/plans/recon-tooling.md`）
- 游戏安装目录：`C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2`
- 结论性质：**仅侦察，未编写任何 Mod 代码。**

---

## 0. 方法论与一个关键发现

### 0.1 本地化数据的获取（最快路径，已验证可行）

游戏内容打包在 `SlayTheSpire2.pck`（1,990,705,700 bytes）。仓库自带 Godot 4.5.1
(`tools\godot\Godot_v4.5.1-stable_mono_win64_console.exe`)，可以直接把它当 resource pack 挂载：

```gdscript
# .recon/extract_pck.gd (本次侦察期间创建)
const PCK := "C:/Program Files (x86)/Steam/steamapps/common/Slay the Spire 2/SlayTheSpire2.pck"
func _initialize() -> void:
    var ok := ProjectSettings.load_resource_pack(PCK, true)
    print("load_resource_pack -> ", ok)
    ...
```

运行：

```powershell
& tools\godot\Godot_v4.5.1-stable_mono_win64_console.exe --headless `
  --script .recon\extract_pck.gd -- "res://src" "res://localization/zhs" "res://localization/eng"
# load_resource_pack -> true
# extracted files: 3739
```

结果：PCK 内共 **18,265 个文件**，其中本地化 **865 个**，结构为
`res://localization/<lang>/*.json`（16 个语言目录 + `patch_notes/`）。

- 语言目录：`eng`(172 文件)、`zht`、`zhs`(47)、`deu`、`esp`、`fra`、`ind`、`ita`、`jpn`、
  `kor`、`pol`、`ptb`、`rus`、`spa`、`tha`、`tur`
- 与本任务相关的文件：
  - `res://localization/zhs/monsters.json` ← **BOSS 名 + 招式名**
  - `res://localization/zhs/encounters.json` ← **BOSS 遭遇名**
  - `res://localization/zhs/intents.json`、`bestiary.json`
  - `res://localization/eng/monsters.json`（英文对照）

格式：**扁平 JSON，key = 本地化 key，value = 富文本字符串**（SmartFormat 风格 `{character}` /
`[gold]...[/gold]` / `[jitter]`）。示例（`zhs/monsters.json` 第 1-5 行）：

```json
{
  "AEONGLASS.moves.EBB.title": "消退",
  "AEONGLASS.moves.EYE_LASERS.title": "眼部激光",
  "AEONGLASS.moves.INCREASING_INTENSITY.title": "加大力度",
  "AEONGLASS.name": "永世沙漏",
```
（`zhs/monsters.json` 第 2-5 行，原文照录）

> 注意：`.cs` 文件虽然出现在 PCK 索引里（`res://src/Core/MonsterMoves/.../MoveState.cs`，
> 3,434 个），但**全部是 1 字节的占位存根**（内容为 `\n`），所以**源码并未随游戏发布**。
> 所有代码级证据均来自 ILSpy 对 `sts2.dll` 的反编译。

### 0.2 本地化 key 的构造规则（代码级证据）

`MegaCrit.Sts2.Core.Models.MonsterModel`：

```csharp
public static LocString L10NMonsterLookup(string entryName)
{
    return new LocString("monsters", entryName);
}

protected LocString GetBestiaryMoveName(string moveId)
{
    return new LocString("monsters", base.Id.Entry + ".moves." + moveId + ".title");
}
```

并且 `GenerateBestiaryMoveList` 会先剥掉状态 id 的 `_MOVE` 后缀：

```csharp
string text = current;
if (text.EndsWith("_MOVE"))
{
    string text2 = text;
    text = text2.Substring(0, text2.Length - 5);
}
LocString bestiaryMoveName = GetBestiaryMoveName(text);
if (!bestiaryMoveName.Exists())
{
    if (!text.EndsWith('2') && !text.EndsWith('3') && !text.EndsWith('4'))
    {
        Log.Warn("No loc for move " + current + " in monster " + Title.GetFormattedText());
```

**即：`<MODEL_ID>.moves.<STATE_ID 去掉 _MOVE>.title`。**
这条规则是下面整张映射表的基础，也是 Mod 侧从状态 id 反查中文名的官方方式。

---

## 1. Q1 — 怪物/敌人基类

**基类：`MegaCrit.Sts2.Core.Models.MonsterModel`（`: AbstractModel`）**

命名空间：`MegaCrit.Sts2.Core.Models`（不是 `Entities`）。
程序集内共 **553** 个类型匹配 `MegaCrit.Sts2.Core.Models.Monsters\..*`（含编译器生成的嵌套类型）。

### 成员表（`tools\ilspy-cli members`，节选关键项）

```csharp
public class MonsterModel : AbstractModel
{
    // ---- 招式状态机（核心） ----
    protected abstract MonsterMoveStateMachine GenerateMoveStateMachine();
    public MonsterMoveStateMachine MoveStateMachine { get; set; }
    public MoveState NextMove { get; set; }                    // ← 当前已声明的 intent
    public void ResetStateMachine();
    public void SetUpForCombat();
    public void RollMove(IEnumerable<Creature> targets);
    public void SetMoveImmediate(MoveState state, bool forceTransition = false);
    public Task PerformMove();                                 // ← 唯一执行入口
    public bool IsPerformingMove { get; set; }
    public bool IntendsToAttack { get; }
    public bool SpawnedThisTurn { get; set; }

    // ---- 本地化 ----
    public static LocString L10NMonsterLookup(string entryName);
    protected LocString GetBestiaryMoveName(string moveId);
    private static string _locTableName;
    private static string stunnedMoveId;

    // ---- 表现层 ----
    public virtual List<BestiaryMonsterMove> GenerateBestiaryMoveList(NCreatureVisuals creatureVisuals);
    protected virtual bool ShouldShowMoveInBestiary(string moveStateId);
    public NCreatureVisuals CreateVisuals();
    public virtual void SetupSkins(MegaSprite spine, MegaSkeleton skeleton);
    public virtual CreatureAnimator GenerateAnimator(MegaSprite controller);
    public virtual Task AfterAddedToRoom();
    public virtual void BeforeRemovedFromRoom();

    // ---- 战斗 / RNG ----
    public Creature Creature { get; set; }
    public ICombatState CombatState { get; }
    public Rng Rng { get; set; }
    public RunRngSet RunRng { get; set; }
    public MonsterModel CanonicalInstance { get; set; }
    public MonsterModel ToMutable();

    // ---- 数值 / 元数据 ----
    public LocString Title { get; }
    public int MinInitialHp { get; }
    public int MaxInitialHp { get; }
    public string VisualsPath { get; }
    public IEnumerable<string> AssetPaths { get; }
    public bool ShouldReceiveCombatHooks { get; }
    public string AttackSfx { get; }  public string CastSfx { get; }  public string DeathSfx { get; }
    public string HurtSfx { get; }    public bool HasDeathSfx { get; }  public bool HasHurtSfx { get; }
    public bool IsHealthBarVisible { get; }  public bool ShouldShowInCompendium { get; }
    public DamageSfxType TakeDamageSfxType { get; }
    // 私有字段：_rng, _runRng, _isPerformingMove, _creature, _moveStateMachine,
    //           <NextMove>k__BackingField, _spawnedThisTurn, _canonicalInstance
}
```

要点：

- `GenerateMoveStateMachine()` 是 **abstract**，每个怪物子类唯一必须实现的东西 → 这就是"招式表"。
- `PerformMove()` 是 **非 virtual 的 public 实例方法，无参数**，返回 `Task` → 单一执行入口，天然的最佳 Hook 点。
- `MonsterModel` 实现了 `ShouldReceiveCombatHooks`，会被 `CombatState.IterateHookListeners()` 当作
  hook listener 收集（见 §6）。

---

## 2. Q2 — Intent（意图）系统

### 2.1 类型清单

全部位于 `MegaCrit.Sts2.Core.MonsterMoves.Intents`：

| 类型 | 说明 |
|---|---|
| `AbstractIntent` | 意图抽象基类 |
| `AttackIntent` | 攻击意图基类 |
| `SingleAttackIntent` | 单击 |
| `MultiAttackIntent` | 多段 |
| `BuffIntent` / `DebuffIntent` / `CardDebuffIntent` | buff / 减益 / 给牌加诅咒 |
| `DefendIntent` / `HealIntent` / `SummonIntent` | 防御 / 治疗 / 召唤 |
| `SleepIntent` / `StunIntent` / `EscapeIntent` | 睡眠 / 眩晕 / 逃跑 |
| `StatusIntent` | 塞状态牌 |
| `DeathBlowIntent` | **处决式大招**（血量阈值即死/巨额伤害） |
| `HiddenIntent` / `UnknownIntent` | 隐藏 / 未知 |

`AbstractIntent` 成员：

```csharp
public virtual LocString GetIntentLabel(IEnumerable<Creature> targets, Creature owner);
public virtual Texture2D GetTexture(IEnumerable<Creature> targets, Creature owner);
public virtual string GetAnimation(IEnumerable<Creature> targets, Creature owner);
public HoverTip GetHoverTip(IEnumerable<Creature> targets, Creature owner);
protected virtual LocString GetIntentDescription(IEnumerable<Creature> targets, Creature owner);
public IntentType IntentType { get; }
public bool HasIntentTip { get; }
public LocString IntentLabelFormat { get; }
public string IntentPrefix { get; }
public string SpritePath { get; }
public static string _locTable;
```

```csharp
public enum IntentType
{
    Attack, Buff, Debuff, DebuffStrong, Defend, Escape, Heal, Hidden,
    Summon, Sleep, Stun, StatusCard, CardDebuff, DeathBlow, Unknown
}
```

### 2.2 Intent 如何被"选择/声明"——不是随机挑 intent，而是"先选状态，intent 是状态的属性"

**关键设计：intent 不是独立选择的，它挂在 `MoveState` 上。** `MoveState` 的构造函数直接接收 intent 数组：

```csharp
public MoveState(string stateId,
                 Func<IReadOnlyList<Creature>, Task> onPerform,
                 params AbstractIntent[] intents)
{
    _onPerform = onPerform;
    Intents = intents;
    StateId = stateId;
}
public IReadOnlyList<AbstractIntent> Intents { get; private set; }
```

选择流程（`MonsterModel`）：

```csharp
public void SetUpForCombat()
{
    MoveStateMachine = GenerateMoveStateMachine();
    SpawnedThisTurn = true;
}

public void RollMove(IEnumerable<Creature> targets)
{
    NextMove = MoveStateMachine.RollMove(targets, Creature, RunRng.MonsterAi);   // ← RNG 在这里
}
```

`MonsterMoveStateMachine.RollMove`：

```csharp
public MoveState RollMove(IEnumerable<Creature> targets, Creature owner, Rng rng)
{
    FindNextMoveState(targets, owner, rng, logMove: true);
    if (!_currentState.IsMove)
        throw new InvalidOperationException(_currentState.Id + " is not a valid move state");
    return (MoveState)_currentState;
}

private void FindNextMoveState(IEnumerable<Creature> targets, Creature owner, Rng rng, bool logMove)
{
    if (!_currentState.CanTransitionAway || (!_performedFirstMove && _currentState.IsMove))
        return;
    MonsterState monsterState = null;
    do
    {
        string nextState = _currentState.GetNextState(owner, rng);
        if (!string.IsNullOrEmpty(nextState) && !States.ContainsKey(nextState))
            throw new InvalidOperationException("no valid state found: " + nextState);
        SetCurrentState(string.IsNullOrEmpty(nextState) ? _initialState : States[nextState]);
        monsterState = ((monsterState == null && _currentState.ShouldAppearInLogs) ? _currentState : monsterState);
    }
    while (!_currentState.IsMove);
    if (logMove && monsterState != null)
        StateLog.Add(monsterState);
}
```

**RNG 结论**：随机性只存在于 `RandomBranchState.GetNextState(owner, rng)`；`MoveState` / `ConditionalBranchState`
的 `GetNextState` 是确定性的。RNG 实例来自 `RunRng.MonsterAi`
（`RunRngSet` 的 `RunRngType.MonsterAi` 分支），由 run 种子派生（见 §6）。
`MonsterAi` 是 **整条 run 共享的一个 Rng 流**，所以它同时被所有怪物消费。

### 2.3 客户端如何知道要显示哪个 intent

`MegaCrit.Sts2.Core.Nodes.Combat.NCreature.RefreshIntents()`（`NIntent` 的宿主）→
`NIntent.UpdateIntent(...)` → `NIntent.UpdateVisuals()`。`NIntent` 逐帧轮询 `CombatManager` 的
`CombatStateChanged` 事件刷新（已冻结时跳过）：

```csharp
public void UpdateIntent(AbstractIntent intent, IEnumerable<Creature> targets, Creature owner)
{
    _owner = owner; _targets = targets; _intent = intent;
    UpdateVisuals();
}

private void OnCombatStateChanged(CombatState _)
{
    if (!_isFrozen) UpdateVisuals();
}

private void UpdateVisuals()
{
    string animation = _intent.GetAnimation(_targets, _owner);
    if (_animationName != animation) { ... 重建帧序列 ... }
    _intentParticle.Texture = _intent.GetTexture(_targets, _owner);
    string text = ((intent is AttackIntent attackIntent)
        ? (attackIntent.GetIntentLabel(_targets, _owner).GetFormattedText() ?? "")
        : ((!(intent is StatusIntent)) ? string.Empty
           : (_intent.GetIntentLabel(_targets, _owner).GetFormattedText() ?? "")));
    valueLabel.Text = text;
}
```

`NIntent` 场景资源：`res://scenes/combat/intent.tscn`；帧动画走 `IntentAnimData`。

---

## 3. Q3 — 单个招式的"创作"方式：**C# 方法 + 状态机对象图**

**答案：既不是 enum，也不是数据表 / Godot 资源——是纯 C# 代码构造的状态机。**

- 招式 = `MoveState` 实例（**不是**一个方法）
- 招式 id = `MoveState.StateId`（字符串常量，如 `"BEAST_CRY_MOVE"`）
- 招式行为 = 构造函数里传入的 `Func<IReadOnlyList<Creature>, Task> onPerform`（指向该类的一个 `private Task XxxMove(IReadOnlyList<Creature> targets)` 方法）
- 招式意图 = 同一构造函数里的 `params AbstractIntent[] intents`
- 流程 = 在 `GenerateMoveStateMachine()` 里用 `FollowUpState` 串成有向图，并 `new MonsterMoveStateMachine(states, initialState)`

### 3.1 状态机基础类型

```csharp
public class MonsterMoveStateMachine
{
    public Dictionary<string, MonsterState> States { get; } = new Dictionary<string, MonsterState>();
    public List<MonsterState> StateLog { get; } = new List<MonsterState>();
    public MonsterMoveStateMachine(IEnumerable<MonsterState> states, MonsterState initialState);
    public MoveState RollMove(IEnumerable<Creature> targets, Creature owner, Rng rng);
    public void ForceCurrentState(MonsterState state);
    public void OnMovePerformed(MoveState _);
    private void FindNextMoveState(IEnumerable<Creature> targets, Creature owner, Rng rng, bool logMove);
}

public abstract class MonsterState
{
    public abstract string GetNextState(Creature owner, Rng rng);
    public abstract void RegisterStates(Dictionary<string, MonsterState> monsterStates);
    public virtual void OnEnterState();
    public virtual void OnExitState();
    public string Id { get; }
    public bool ShouldAppearInLogs { get; }
    public bool CanTransitionAway { get; }
    public bool IsMove { get; }
}

public class MoveState : MonsterState
{
    private bool _performedAtLeastOnce;
    private readonly Func<IReadOnlyList<Creature>, Task> _onPerform;

    public IReadOnlyList<AbstractIntent> Intents { get; private set; }
    public string StateId { get; }
    public bool MustPerformOnceBeforeTransitioning { get; set; }
    public string? FollowUpStateId { get; init; }
    public MonsterState? FollowUpState { get; set; }
    public override bool IsMove => true;
    public override string Id => StateId;
    public override bool CanTransitionAway => MustPerformOnceBeforeTransitioning ? _performedAtLeastOnce : true;

    public MoveState() : this("UNSET_MOVE", UnsetMove) { }
    public MoveState(string stateId,
                     Func<IReadOnlyList<Creature>, Task> onPerform,
                     params AbstractIntent[] intents);

    public Task PerformMove(IEnumerable<Creature> targets);
    public override string GetNextState(Creature owner, Rng rng);
    public override void RegisterStates(Dictionary<string, MonsterState> monsterStates);
}

public class ConditionalBranchState : MonsterState
{
    public ConditionalBranchState(string stateId);
    public void AddState(MonsterState move, Func<bool> condition);   // 手写条件
    public string BranchId { get; }
    public List<ConditionalBranchState.ConditionalBranch> States { get; }
}

public class RandomBranchState : MonsterState
{
    public RandomBranchState(string id);
    public void AddBranch(MonsterState state, int cooldown, MoveRepeatType repeatType, Func<float> weight);
    public void AddBranch(MonsterState state, int maxRepeats, Func<float> weight);
    // ... 共 11 个 AddBranch 重载
    public List<RandomBranchState.StateWeight> States { get; set; }
}

public enum MoveRepeatType { CanRepeatForever, CanRepeatXTimes, CannotRepeat, UseOnlyOnce }
```

### 3.2 完整示例 A — `CeremonialBeast`（仪式兽）的 `BEAST_CRY_MOVE`

```csharp
// MegaCrit.Sts2.Core.Models.Monsters.CeremonialBeast
protected override MonsterMoveStateMachine GenerateMoveStateMachine()
{
    List<MonsterState> val = new List<MonsterState>();
    MoveState moveState  = new MoveState("STAMP_MOVE",  StampMove,  new BuffIntent());
    MoveState moveState2 = new MoveState("PLOW_MOVE",   PlowMove,   new SingleAttackIntent(PlowDamage),  new BuffIntent());
    MoveState moveState3 = new MoveState("STUN_MOVE",   StunnedMove, new StunIntent())
    {
        MustPerformOnceBeforeTransitioning = true
    };
    BeastCryState = new MoveState("BEAST_CRY_MOVE", BeastCryMove, new DebuffIntent());
    MoveState moveState4 = new MoveState("STOMP_MOVE",  StompMove,  new SingleAttackIntent(StompDamage));
    MoveState moveState5 = new MoveState("CRUSH_MOVE",  CrushMove,  new SingleAttackIntent(CrushDamage), new BuffIntent());
    moveState.FollowUpState      = moveState2;
    moveState2.FollowUpState     = moveState2;        // 自环（横冲直撞可重复）
    moveState3.FollowUpState     = BeastCryState;
    BeastCryState.FollowUpState  = moveState4;
    moveState4.FollowUpState     = moveState5;
    moveState5.FollowUpState     = BeastCryState;      // STOMP → CRUSH → BEAST_CRY → ...
    val.Add(moveState2); val.Add(moveState); val.Add(moveState3);
    val.Add(BeastCryState); val.Add(moveState4); val.Add(moveState5);
    return new MonsterMoveStateMachine(val, moveState);
}
```

招式实现（反编译 `<BeastCryMove>d__57.MoveNext()`，去掉 IL 噪声后的等价逻辑）：

```csharp
private async Task BeastCryMove(IReadOnlyList<Creature> targets)
{
    SfxCmd.Play("event:/sfx/enemy/enemy_attacks/ceremonial_beast/ceremonial_beast_shrill");
    await CreatureCmd.TriggerAnim(Creature, "Cast", 0f);
    await Cmd.Wait(0.3f);
    VfxCmd.PlayOnCreatureCenter(Creature, "vfx/vfx_scream");
    await Cmd.Wait(0.75f);
    await PowerCmd.Apply<RingingPower>(new ThrowingPlayerChoiceContext(),
                                       targets, 1m, Creature, null);
}
```

→ **招式 = 一个 async 方法，方法体里 `await` 动画 / VFX / 音效 / 效果命令。**
这就是"大招"实现的全貌。

### 3.3 完整示例 B — `Vantom`（墨影幻灵）的 `DISMEMBER_MOVE`（肢解）

```csharp
MoveState moveState  = new MoveState("INK_BLOT_MOVE",    InkBlotMove,    new SingleAttackIntent(InkBlotDamage));
MoveState moveState2 = new MoveState("INKY_LANCE_MOVE",  InkyLanceMove,  new MultiAttackIntent(InkyLanceDamage, 2));
MoveState moveState3 = new MoveState("DISMEMBER_MOVE",   DismemberMove,  new SingleAttackIntent(DismemberDamage), new StatusIntent(3));
MoveState moveState4 = new MoveState("PREPARE_MOVE",     PrepareMove,    new BuffIntent());
moveState.FollowUpState = moveState2;  moveState2.FollowUpState = moveState3;
moveState3.FollowUpState = moveState4; moveState4.FollowUpState = moveState;   // 4 步循环
```

```csharp
private async Task DismemberMove(IReadOnlyList<Creature> targets)
{
    if (TestMode.IsOff && Creature.IsAlive)
    {
        MegaAnimationState s = (NCombatRoom.Instance?.GetCreatureNode(Creature))?
                                   .SpineAnimation.GetAnimationState();
        s?.SetAnimation("_tracks/attack_heavy", loop: false, 1);
        s?.AddAnimation("_tracks/charged_0", 0f, loop: true, 1);
    }
    NRunMusicController.Instance?.UpdateMusicParameter("vantom_progress", 3f);
    await CreatureCmd.TriggerAnim(Creature, "ATTACK_HEAVY", 0f);
    await Cmd.Wait(0.25f);
    NCombatRoom.Instance?.RadialBlur(VfxPosition.Left);                      // 镜头径向模糊
    NGame.Instance?.ScreenShake(ShakeStrength.Strong, ShakeDuration.Normal,
                                180f + Rng.Chaotic.NextFloat(-10f, 10f));    // 屏幕震动
    await DamageCmd.Attack(DismemberDamage).FromMonster(this)
                 .WithNoAttackerAnim()
                 .WithHitFx("vfx/vfx_giant_horizontal_slash",
                            "event:/sfx/enemy/enemy_attacks/vantom/vantom_dismember")
                 .Execute(null);
    NGame.Instance?.DoHitStop(ShakeStrength.Weak, ShakeDuration.Short);      // 命中定帧
    await Cmd.Wait(0.5f);
    await CardPileCmd.AddToCombatAndPreview<Wound>(targets, PileType.Discard, 3, null);  // 塞 3 张伤口
}
```

### 3.4 完整示例 C — `WaterfallGiant`（瀑布巨兽）的 `EXPLODE_MOVE`（爆炸）—— 真正的"大招"模式

```csharp
MoveState moveState  = new MoveState("PRESSURIZE_MOVE",  PressurizeMove, new BuffIntent());
MoveState moveState2 = new MoveState("STOMP_MOVE",       StompMove,  new SingleAttackIntent(StompDamage), new DebuffIntent(), new BuffIntent());
MoveState moveState3 = new MoveState("RAM_MOVE",         RamMove,    new SingleAttackIntent(RamDamage),   new BuffIntent());
MoveState moveState4 = new MoveState("SIPHON_MOVE",      SiphonMove, new HealIntent(), new BuffIntent());
MoveState moveState5 = new MoveState("PRESSURE_GUN_MOVE",PressureGunMove, new SingleAttackIntent(() => (decimal)CurrentPressureGunDamage), new BuffIntent());
MoveState moveState6 = new MoveState("PRESSURE_UP_MOVE", PressureUpMove,  new SingleAttackIntent(PressureUpDamage), new BuffIntent());
AboutToBlowState     = new MoveState("ABOUT_TO_BLOW_MOVE", AboutToBlowMove, new StunIntent()) { /* MustPerformOnce... */ };
MoveState moveState7 = new MoveState("EXPLODE_MOVE", ExplodeMove, new DeathBlowIntent(() => (decimal)SteamEruptionDamage));

moveState.FollowUpState = moveState2;   moveState2.FollowUpState = moveState3;
moveState3.FollowUpState = moveState4;  moveState4.FollowUpState = moveState5;
moveState5.FollowUpState = moveState6;  moveState6.FollowUpState = moveState2;
AboutToBlowState.FollowUpState = moveState7;
moveState7.FollowUpState = moveState7;    // 自环
```

```csharp
private async Task ExplodeMove(IReadOnlyList<Creature> targets)
{
    StopAmbientSfx();
    await DamageCmd.Attack(SteamEruptionDamage).FromMonster(this)
                 .WithAttackerAnim("Erupt", 0.1f)
                 .WithAttackerFx(null, DeathSfx)     // = 死亡音效
                 .Execute(null);
    await CreatureCmd.Kill(Creature);                // 自爆即死
    NRunMusicController.Instance?.UpdateMusicParameter("waterfall_giant_progress", 5f);
}
```

注意 `ABOUT_TO_BLOW_MOVE` 用 `StunIntent` 做"蓄力一回合"的预告，
`EXPLODE_MOVE` 用 `DeathBlowIntent`（游戏内置的"必杀"意图类型）。
`BeginCharge` 型大招在 `SetUpForCombat` / `AfterAddedToRoom` 里用
`SetMoveImmediate(AboutToBlowState, forceTransition)` 强制切状态
（存在字段 `AboutToBlowState` 与 `TriggerAboutToBlowState` 方法）。

---

## 4. Q4 — 怪物枚举 + BOSS/招式映射表

### 4.1 枚举结果

`MegaCrit.Sts2.Core.Models.Monsters.*` 共 553 个类型（含 `<XxxMove>d__NN` 状态机嵌套类型与 `Mocks.*`）。
**非嵌套的真实怪物类约 190 个**，完整清单见 `.recon/monsters-all.txt`（本次侦察产出）。

注意：`MegaCrit.Sts2.Core.Models.Monsters.Mocks.*` 是单元测试用假怪
（`MockAttackMonster`、`MockPlatingMonster` 等），不要当真实怪物。

### 4.2 用户给定名称 → 真实 id 映射表

| 中文名（游戏内官方） | English | 遭遇 loc id | 怪物 type name | 招式中文名 | 招式 loc id | 状态 id | 定义位置 |
|---|---|---|---|---|---|---|---|
| 仪式兽 | Ceremonial Beast | `CEREMONIAL_BEAST_BOSS` | `MegaCrit.Sts2.Core.Models.Monsters.CeremonialBeast` | 野兽咆哮 | `CEREMONIAL_BEAST.moves.BEAST_CRY.title` | `BEAST_CRY_MOVE` | `CeremonialBeast.GenerateMoveStateMachine()` |
| 墨影幻灵 | Vantom | `VANTOM_BOSS` | `...Monsters.Vantom` | 肢解 | `VANTOM.moves.DISMEMBER.title` | `DISMEMBER_MOVE` | `Vantom.GenerateMoveStateMachine()` |
| 乐加维林族母（用户称"祖母"） | Lagavulin Matriarch | `LAGAVULIN_MATRIARCH_BOSS` | `...Monsters.LagavulinMatriarch` | 灵魂汲取 | `LAGAVULIN_MATRIARCH.moves.SOUL_SIPHON.title` | `SOUL_SIPHON_MOVE` | `LagavulinMatriarch.GenerateMoveStateMachine()` |
| 瀑布巨兽 | Waterfall Giant | `WATERFALL_GIANT_BOSS` | `...Monsters.WaterfallGiant` | 爆炸 | `WATERFALL_GIANT.moves.EXPLODE.title` | `EXPLODE_MOVE` | `WaterfallGiant.GenerateMoveStateMachine()` |
| 帝皇蟹-火箭 | Kaiser Crab (Rocket) | `KAISER_CRAB_BOSS` | `...Monsters.Rocket`（`KaiserCrabBoss` 遭遇的 `rocket` slot） | 激光 | `ROCKET.moves.LASER.title` | `LASER_MOVE` | `Rocket.GenerateMoveStateMachine()` |
| 知识恶魔 | Knowledge Demon | `KNOWLEDGE_DEMON_BOSS` | `...Monsters.KnowledgeDemon` | 知识的诅咒 | `KNOWLEDGE_DEMON.moves.CURSE_OF_KNOWLEDGE.title` | `CURSE_OF_KNOWLEDGE_MOVE` | `KnowledgeDemon.GenerateMoveStateMachine()` |
| 无厌沙虫（用户称"无厌杀虫"） | The Insatiable | `THE_INSATIABLE_BOSS` | `...Monsters.TheInsatiable` | 吞噬 | `THE_INSATIABLE.moves.DEVOUR.title` | `DEVOUR`（**无 `_MOVE` 后缀**） | `TheInsatiable.GenerateMoveStateMachine()` |
| 女王 | Queen | `QUEEN_BOSS` | `...Monsters.Queen` | 提线木偶 | ⚠ **无 loc key** | `PUPPET_STRINGS_MOVE` | `Queen.GenerateMoveStateMachine()` |
| 女王 | Queen | 同上 | 同上 | 你是我的了 | `QUEEN.moves.YOU_ARE_MINE.title` / `QUEEN.moves.YOURE_MINE.title` | `YOU_ARE_MINE_MOVE` | 同上 |
| 女王 | Queen | 同上 | 同上 | 将头砍下 | `QUEEN.moves.OFF_WITH_YOUR_HEAD.title` | `OFF_WITH_YOUR_HEAD_MOVE` | 同上 |
| 火炬头聚合体 | Torch Head Amalgam | —（普通/精英） | `...Monsters.TorchHeadAmalgam` | 光束 | `TORCH_HEAD_AMALGAM.moves.BEAM.title` | `BEAM_MOVE` | `TorchHeadAmalgam.GenerateMoveStateMachine()` |
| 实验体 #C{Count} | Test Subject | `TEST_SUBJECT_BOSS` | `...Monsters.TestSubject` | 颅骨重击 | `TEST_SUBJECT.moves.SKULL_BASH.title` | `SKULL_BASH_MOVE` | `TestSubject.GenerateMoveStateMachine()` |
| 实验体 | Test Subject | 同上 | 同上 | 多次爪击 | `TEST_SUBJECT.moves.MULTI_CLAW.title` | `MULTI_CLAW_MOVE` | 同上 |
| 实验体 | Test Subject | 同上 | 同上 | 大力猛扑 | `TEST_SUBJECT.moves.BIG_POUNCE.title` | **`BIG_POUNCE`**（无 `_MOVE` 后缀） | 同上 |
| 永世沙漏 | Aeonglass | `AEONGLASS_BOSS` | `...Monsters.Aeonglass` | 加大力度 | `AEONGLASS.moves.INCREASING_INTENSITY.title` | `INCREASING_INTENSITY_MOVE` | `Aeonglass.GenerateMoveStateMachine()` |

**⚠ 两个必须注意的坑：**

1. **`PUPPET_STRINGS` 没有本地化标题。** `zhs/monsters.json` 与 `eng/monsters.json` 中
   `QUEEN.moves.*` 只有 `BARRIER / BURN_BRIGHT_FOR_ME / EXECUTION / IMPOSSIBLE /
   OFF_WITH_YOUR_HEAD / SUMMON_1 / SUMMON_2 / YOURE_MINE / YOU_ARE_MINE`，
   **没有 `PUPPET_STRINGS`**。所以"提线木偶"是社区/wiki 直译，游戏内该招式不显示具名 bestiary 标题
   （该招用 `CardDebuffIntent`，UI 显示意图标签而非招式名）。
   代码里的 id 确凿：`MoveState moveState = new MoveState("PUPPET_STRINGS_MOVE", PuppetStringsMove, new CardDebuffIntent());`
2. **`_MOVE` 后缀不统一。** `TestSubject` 的 `BIG_POUNCE`、`TheInsatiable` 的 `DEVOUR`
   没有 `_MOVE` 后缀；`TheInsatiable` 还有重复的 `THRASH_MOVE` / `THRASH_MOVE_2`。
   Mod 里做匹配必须按**精确字符串**，不能靠后缀推导。

### 4.3 各 BOSS 的完整招式表（全部招式）

#### 仪式兽 `CeremonialBeast`（`CEREMONIAL_BEAST`）
| 状态 id | 中文名 | Intent |
|---|---|---|
| `STAMP_MOVE` | 跺地 | `BuffIntent` |
| `PLOW_MOVE` | 横冲直撞 | `SingleAttackIntent(PlowDamage)` + `BuffIntent` |
| `STUN_MOVE` | （眩晕） | `StunIntent`，`MustPerformOnceBeforeTransitioning = true` |
| `BEAST_CRY_MOVE` | **野兽咆哮** | `DebuffIntent`（施加 `RingingPower`） |
| `STOMP_MOVE` | 踩踏 | `SingleAttackIntent(StompDamage)` |
| `CRUSH_MOVE` | 碾碎 | `SingleAttackIntent(CrushDamage)` + `BuffIntent` |

流程：`STAMP → PLOW → PLOW(自环) → [STUN] → BEAST_CRY → STOMP → CRUSH → BEAST_CRY`（循环）。
初始状态 = `moveState`（`STAMP_MOVE`）。`BeastCryState` 是实例字段，可被外部强制切换。

#### 墨影幻灵 `Vantom`（`VANTOM`）
| 状态 id | 中文名 | Intent |
|---|---|---|
| `INK_BLOT_MOVE` | 墨迹 | `SingleAttackIntent(InkBlotDamage)` |
| `INKY_LANCE_MOVE` | 墨水长枪 | `MultiAttackIntent(InkyLanceDamage, 2)` |
| `DISMEMBER_MOVE` | **肢解** | `SingleAttackIntent(DismemberDamage)` + `StatusIntent(3)`（3 张伤口） |
| `PREPARE_MOVE` | 准备 | `BuffIntent` |

流程：`INK_BLOT → INKY_LANCE → DISMEMBER → PREPARE → INK_BLOT`（4 步循环）。

#### 乐加维林族母 `LagavulinMatriarch`（`LAGAVULIN_MATRIARCH`）
| 状态 id | 中文名 | Intent |
|---|---|---|
| `SLEEP_MOVE` | 沉睡 | `SleepIntent` |
| `SLASH_MOVE` | 斩击 | `SingleAttackIntent(SlashDamage)` |
| `SLASH2_MOVE` | （斩击 2） | `SingleAttackIntent(Slash2Damage)` + `DefendIntent` |
| `DISEMBOWEL_MOVE` | 开膛破肚 | `MultiAttackIntent(DisembowelDamage, DisembowelRepeat)` |
| `SOUL_SIPHON_MOVE` | **灵魂汲取** | `DebuffIntent` + `BuffIntent` |
| `SLEEP_BRANCH` | （条件分支） | 条件：`Creature.HasPower<AsleepPower>()` → `SLEEP_MOVE`；否则 → `SLASH_MOVE` |

流程：`SLEEP → SLEEP_BRANCH → {SLEEP | SLASH} → SLASH → DISEMBOWEL → SLASH2 → SOUL_SIPHON → SLASH`。

#### 瀑布巨兽 `WaterfallGiant`（`WATERFALL_GIANT`）
| 状态 id | 中文名 | Intent |
|---|---|---|
| `PRESSURIZE_MOVE` | 增压 | `BuffIntent` |
| `STOMP_MOVE` | 践踏 | `SingleAttackIntent(StompDamage)` + `DebuffIntent` + `BuffIntent` |
| `RAM_MOVE` | 撞击 | `SingleAttackIntent(RamDamage)` + `BuffIntent` |
| `SIPHON_MOVE` | 虹吸 | `HealIntent` + `BuffIntent` |
| `PRESSURE_GUN_MOVE` | 压力炮 | `SingleAttackIntent(() => CurrentPressureGunDamage)` + `BuffIntent` |
| `PRESSURE_UP_MOVE` | 增压 | `SingleAttackIntent(PressureUpDamage)` + `BuffIntent` |
| `ABOUT_TO_BLOW_MOVE` | 即将爆发 | `StunIntent`（蓄力预告） |
| `EXPLODE_MOVE` | **爆炸** | **`DeathBlowIntent(() => SteamEruptionDamage)`** |

流程：`PRESSURIZE → STOMP → RAM → SIPHON → PRESSURE_GUN → PRESSURE_UP → STOMP …`，
`ABOUT_TO_BLOW → EXPLODE → EXPLODE(自环)`（`AboutToBlowState` 由
`TriggerAboutToBlowState` / `SetMoveImmediate` 强制切入）。

#### 帝皇蟹 `KaiserCrabBoss`（遭遇，`KAISER_CRAB_BOSS`，中文名「帝皇蟹」）

**这是一个双怪物遭遇，不是一个 MonsterModel。** 证据：

```csharp
// MegaCrit.Sts2.Core.Models.Encounters.KaiserCrabBoss : EncounterModel
private const string _crusherSlot = "crusher";
private const string _rocketSlot  = "rocket";
public override IReadOnlyList<string> Slots => new[] { "crusher", "rocket" };
public override IEnumerable<MonsterModel> AllPossibleMonsters => new[]
{
    ModelDb.Monster<Crusher>(),
    ModelDb.Monster<Rocket>()
};
protected override IReadOnlyList<(MonsterModel, string?)> GenerateMonsters()
{
    return new[] {
        (ModelDb.Monster<Crusher>().ToMutable(), "crusher"),
        (ModelDb.Monster<Rocket>().ToMutable(),  "rocket")
    };
}
```

- **`rocket` slot → `Monsters.Rocket`（中文名「火箭」，`ROCKET.name`）** ← 用户说的「帝皇蟹-火箭」
  | 状态 id | 中文名 | Intent |
  |---|---|---|
  | `TARGETING_RETICLE_MOVE` | 瞄准十字 | `SingleAttackIntent(TargetingReticleDamage)` |
  | `PRECISION_BEAM_MOVE` | 精准光束 | `SingleAttackIntent(PrecisionBeamDamage)` |
  | `CHARGE_UP_MOVE` | 蓄能 | `BuffIntent` |
  | `LASER_MOVE` | **激光** | `SingleAttackIntent(LaserDamage)` |
  | `RECHARGE_MOVE` | 重新充能 | `SleepIntent` |
  
  流程：`TARGETING_RETICLE → PRECISION_BEAM → CHARGE_UP → LASER → RECHARGE → TARGETING_RETICLE`

- `crusher` slot → `Monsters.Crusher`（中文名「碾碎爪」）
  | 状态 id | 中文名 | Intent |
  |---|---|---|
  | `THRASH_MOVE` | 撕扯 | `SingleAttackIntent(ThrashDamage)` |
  | `ENLARGING_STRIKE_MOVE` | 巨化打击 | `SingleAttackIntent(EnlargingStrikeDamage)` |
  | `BUG_STING_MOVE` | 虫蛰 | `MultiAttackIntent(BugStingDamage, BugStingTimes)` + `DebuffIntent` |
  | `ADAPT_MOVE` | 适应 | `BuffIntent` |
  | `GUARDED_STRIKE_MOVE` | 戒备打击 | `SingleAttackIntent(GuardedStrikeDamage)` + `DefendIntent` |

  流程：`THRASH → ENLARGING_STRIKE → BUG_STING → ADAPT → GUARDED_STRIKE → THRASH`

#### 知识恶魔 `KnowledgeDemon`（`KNOWLEDGE_DEMON`）
| 状态 id | 中文名 | Intent |
|---|---|---|
| `CURSE_OF_KNOWLEDGE_MOVE` | **知识的诅咒** | `DebuffIntent` |
| `SLAP_MOVE` | 抽打 | `SingleAttackIntent(SlapDamage)` |
| `KNOWLEDGE_OVERWHELMING_MOVE` | 知识过载 | `MultiAttackIntent(KnowledgeOverwhelmingDamage, 3)` |
| `PONDER_MOVE` | 思考 | `SingleAttackIntent(PonderDamage)` + `HealIntent` + `BuffIntent` |
| `CurseOfKnowledgeBranch` | （条件分支） | `_curseOfKnowledgeCounter < 3` → `CURSE_OF_KNOWLEDGE_MOVE`；`>= 3` → `SLAP_MOVE` |

流程：`CURSE_OF_KNOWLEDGE → SLAP → KNOWLEDGE_OVERWHELMING → PONDER → CurseOfKnowledgeBranch → …`

**特别相关**：该招式有专属"台词"静态 LocString 字段与本地化 key，说明它是被当作
**演出型大招**处理的：

```csharp
// 字段
private static LocString _curseOfKnowledgeStartLine;
private static LocString _curseOfKnowledgeDoneLine;
```
```json
"KNOWLEDGE_DEMON.moves.CURSE_OF_KNOWLEDGE.doneLine": "此事已成。",
"KNOWLEDGE_DEMON.moves.CURSE_OF_KNOWLEDGE.startLine": "做出选择。",
"KNOWLEDGE_DEMON.moves.CURSE_OF_KNOWLEDGE.title": "知识的诅咒",
```
（其它带台词的是 `.banter`，例如 `CALCIFIED_CULTIST.moves.INCANTATION.banter`、
`QUEEN.banter`——注意 `banter` 是**玩家侧**的嘲讽台词，不是怪物的。）

#### 无厌沙虫 `TheInsatiable`（`THE_INSATIABLE`）
| 状态 id | 中文名 | Intent |
|---|---|---|
| `LIQUIFY_GROUND_MOVE` | 液化地面 | `BuffIntent` + `StatusIntent(6)` |
| `THRASH_MOVE` | 撕扯 | `MultiAttackIntent(ThrashDamage, 2)` |
| `THRASH_MOVE_2` | 撕扯 | `MultiAttackIntent(ThrashDamage, 2)` |
| `LUNGING_BITE_MOVE` | 前扑啃咬 | `SingleAttackIntent(BiteDamage)` |
| `SALIVATE_MOVE` | 分泌唾液 | `BuffIntent` |
| **`DEVOUR`** | **吞噬** | ⚠ 见下 |

> ⚠ **`吞噬`（DEVOUR）没有出现在 `GenerateMoveStateMachine()` 的 MoveState 列表里。**
> 本地化 key `THE_INSATIABLE.moves.DEVOUR.title` = 「吞噬」存在，
> 且 `SANDPIT_POWER.smartDescription` 为「在[gold]无厌沙虫[/gold]的下[blue]{Amount}[/blue]个回合开始时，
> 你将会被吞下并直接死亡。」→ 说明「吞噬」是**通过 `SandpitPower` 触发的机制**，
> 由 `THE_INSATIABLE.moves.DEVOUR` 这个 loc key 具名，
> 但**不是走 `MoveState`/`PerformMove` 的普通招式**。
> 若 Mod 需要在「吞噬」演出前插动画，**不能**依赖 `MonsterModel.PerformMove` 的 `NextMove.Id`，
> 必须另外找（Power 触发点）。这是本报告最重要的负向结论。

流程：`LIQUIFY_GROUND → THRASH → LUNGING_BITE → SALIVATE → THRASH_2 → THRASH → …`

#### 女王 `Queen`（`QUEEN`）
| 状态 id | 中文名 | Intent | 备注 |
|---|---|---|---|
| `PUPPET_STRINGS_MOVE` | **提线木偶**（无 loc key） | `CardDebuffIntent` | 开场招 |
| `YOU_ARE_MINE_MOVE` | **你是我的了** | `DebuffIntent` | 也对应 `QUEEN.banter` |
| `BURN_BRIGHT_FOR_ME_MOVE` | 为我尽力燃烧吧 | `BuffIntent` + `DefendIntent` | 实例字段 `BurnBrightForMeState` |
| `OFF_WITH_YOUR_HEAD_MOVE` | **将头砍下** | `MultiAttackIntent(OffWithYourHeadDamage, 5)` | 5 段 |
| `EXECUTION_MOVE` | 处决 | `SingleAttackIntent(ExecutionDamage)` | |
| `ENRAGE_MOVE` | （激怒） | `BuffIntent` | 实例字段 `EnragedState`；无独立 loc |
| `BARRIER` | 屏障 | — | 有 loc：`QUEEN.moves.BARRIER.title` |
| `IMPOSSIBLE` | 绝无可能 | — | 有 loc：`QUEEN.moves.IMPOSSIBLE.title` |
| `SUMMON_1` / `SUMMON_2` | 召唤 | — | 有 loc，但**不在本状态机里** |
| `YOURE_MINE_NOW_BRANCH` | （条件分支） | 条件：`!HasAmalgamDied` → `BURN_BRIGHT_FOR_ME`；`HasAmalgamDied` → `OFF_WITH_YOUR_HEAD` |
| `BURN_BRIGHT_FOR_ME_BRANCH` | （条件分支） | 同上条件 |

完整状态机：

```csharp
MoveState moveState  = new MoveState("PUPPET_STRINGS_MOVE", PuppetStringsMove, new CardDebuffIntent());
MoveState moveState2 = new MoveState("YOU_ARE_MINE_MOVE",   YoureMineMove,    new DebuffIntent());
ConditionalBranchState conditionalBranchState  = new ConditionalBranchState("YOURE_MINE_NOW_BRANCH");
BurnBrightForMeState = new MoveState("BURN_BRIGHT_FOR_ME_MOVE", BurnBrightForMeMove, new BuffIntent(), new DefendIntent());
ConditionalBranchState conditionalBranchState2 = new ConditionalBranchState("BURN_BRIGHT_FOR_ME_BRANCH");
MoveState moveState3 = new MoveState("OFF_WITH_YOUR_HEAD_MOVE", OffWithYourHeadMove, new MultiAttackIntent(OffWithYourHeadDamage, 5));
MoveState moveState4 = new MoveState("EXECUTION_MOVE", ExecutionMove, new SingleAttackIntent(ExecutionDamage));
EnragedState = new MoveState("ENRAGE_MOVE", EnrageMove, new BuffIntent());

moveState.FollowUpState  = moveState2;
moveState2.FollowUpState = conditionalBranchState;
conditionalBranchState.AddState(BurnBrightForMeState, () => !HasAmalgamDied);
conditionalBranchState.AddState(moveState3,           () =>  HasAmalgamDied);
BurnBrightForMeState.FollowUpState = conditionalBranchState2;
conditionalBranchState2.AddState(BurnBrightForMeState, () => !HasAmalgamDied);
conditionalBranchState2.AddState(moveState3,           () =>  HasAmalgamDied);
moveState3.FollowUpState = moveState4;
moveState4.FollowUpState = EnragedState;
EnragedState.FollowUpState = moveState3;
// 初始状态 = moveState (PUPPET_STRINGS_MOVE)
```

流程：`PUPPET_STRINGS → YOU_ARE_MINE → [分支] → (BURN_BRIGHT_FOR_ME → [分支])*
→ OFF_WITH_YOUR_HEAD → EXECUTION → ENRAGE → OFF_WITH_YOUR_HEAD → …`

→ **「提线木偶→你是我的了」是固定开场连招**（无 RNG），非常适合做演出触发点。

#### 火炬头聚合体 `TorchHeadAmalgam`（`TORCH_HEAD_AMALGAM`）
| 状态 id | 中文名 | Intent |
|---|---|---|
| `STRONG_TACKLE_MOVE` | 强力冲撞 | `SingleAttackIntent(StrongTackleDamage)` |
| `TACKLE_2_MOVE` | （冲撞 2） | `SingleAttackIntent(TackleDamage)` |
| `BEAM_MOVE` | **光束** | `MultiAttackIntent(SoulBeamDamage, 3)` |
| `TACKLE_3_MOVE` | （冲撞 3） | `SingleAttackIntent(WeakTackleDamage)` |
| `TACKLE_4_MOVE` | （冲撞 4） | `SingleAttackIntent(WeakTackleDamage)` |

流程：`STRONG_TACKLE → TACKLE_2 → BEAM → TACKLE_3 → TACKLE_4 → BEAM → …`

> ⚠ 注意歧义：`BEAM_MOVE` 这个状态 id 在三处出现——`TorchHeadAmalgam`（`TORCH_HEAD_AMALGAM.moves.BEAM`
> =「光束」）、`TheAdversaryMkOne`（`THE_ADVERSARY_MK_ONE.moves.BEAM` =「光束」，用 `BeamMove`）、
> `KinPriest`（`KIN_PRIEST.moves.BEAM` =「灵魂光束」）。
> **`StateId` 不是全局唯一**，判定必须以「怪物类型 + StateId」组合为准。

#### 实验体 `TestSubject`（`TEST_SUBJECT`，中文名「实验体 #C{Count}」）
| 状态 id | 中文名 | Intent |
|---|---|---|
| `RESPAWN_MOVE` | 重生 | `HealIntent` + `BuffIntent`（实例字段 `DeadState`） |
| `BITE_MOVE` | 噬咬 | `SingleAttackIntent(BiteDamage)` |
| `SKULL_BASH_MOVE` | **颅骨重击** | `SingleAttackIntent(SkullBashDamage)` + `DebuffIntent` |
| `MULTI_CLAW_MOVE` | **多次爪击** | `MultiAttackIntent(MultiClawDamage, () => MultiClawTotalCount)` |
| `PHASE3_LACERATE_MOVE` | 撕碎 | `MultiAttackIntent(Phase3LacerateDamage, 3)` |
| **`BIG_POUNCE`** | **大力猛扑** | `SingleAttackIntent(BigPounceDamage)` |
| `BURNING_GROWL_MOVE` | 燃烧之吼 | `StatusIntent(BurningGrowlBurnCount)` + `BuffIntent` |
| `REVIVE_BRANCH` | （条件分支） | `Respawns < 2` → `PHASE3_LACERATE_MOVE`；`Respawns >= 2` → `BIG_POUNCE` |

流程（三阶段变身）：`BITE → SKULL_BASH → BITE …`（阶段 1/2），
阶段 3：`PHASE3_LACERATE → BIG_POUNCE → BURNING_GROWL → PHASE3_LACERATE …`，
死亡时 `DeadState(RESPAWN) → REVIVE_BRANCH → …`。

```csharp
DeadState.FollowUpState = conditionalBranchState;
conditionalBranchState.AddState(moveState3, () => Respawns < 2);
conditionalBranchState.AddState(moveState4, () => Respawns >= 2);
```

#### 永世沙漏 `Aeonglass`（`AEONGLASS`）
| 状态 id | 中文名 | Intent |
|---|---|---|
| `EBB_MOVE` | 消退 | `SingleAttackIntent(EbbDamage)` + `DefendIntent` |
| `EYE_LASERS_MOVE` | 眼部激光 | `MultiAttackIntent(EyeLasersDamage, EyeLasersRepeat)` |
| `INCREASING_INTENSITY_MOVE` | **加大力度** | `StatusIntent(WitherAmount)` + `BuffIntent` |

流程（3 步硬循环，**无 RNG**）：`EBB → EYE_LASERS → INCREASING_INTENSITY → EBB → …`

```csharp
MoveState moveState  = new MoveState("EBB_MOVE", EbbMove, new SingleAttackIntent(EbbDamage), new DefendIntent());
MoveState moveState2 = new MoveState("EYE_LASERS_MOVE", EyeLasersMove, new MultiAttackIntent(EyeLasersDamage, EyeLasersRepeat));
MoveState moveState3 = new MoveState("INCREASING_INTENSITY_MOVE", IncreasingIntensityMove, new StatusIntent(WitherAmount), new BuffIntent());
moveState.FollowUpState = moveState2; moveState2.FollowUpState = moveState3; moveState3.FollowUpState = moveState;
```

> 附带发现：`TheLost` 也有 `EYE_LASERS`（`THE_LOST.moves.EYE_LASERS` =「眼部激光」）、
> `Aeonglass` 与 `TheLost` 的 loc 里都有 `EBB` / `EYE_LASERS`，注意区分。

### 4.4 其它可用作「大招」判定参考的 `StateId` 命名约定

- 蓄力/预告：`ABOUT_TO_BLOW_MOVE`、`CHARGE_UP_MOVE`、`PRESSURIZE_MOVE`、`PREPARE_MOVE`、`PONDER_MOVE`
- 真正的必杀：`EXPLODE_MOVE`(`DeathBlowIntent`)、`OFF_WITH_YOUR_HEAD_MOVE`(5 段)、
  `DEVOUR`、`CURSE_OF_KNOWLEDGE_MOVE`、`DISMEMBER_MOVE`、`BEAM_MOVE`

---

## 5. Q5 — 招式在客户端如何执行，以及可 Prefix 的异步方法

### 5.1 完整调用链（全部证据出自反编译）

```
CombatManager.ExecuteEnemyTurn(turnState)                                    // async
  └─ foreach (Creature enemy in turnState.State.Enemies)
       ├─ if (!turnState.State.ContainsCreature(enemy)) continue
       ├─ NCreature nCreature = NCombatRoom.Instance?.GetCreatureNode(enemy)
       ├─ if (nCreature != null) await nCreature.PerformIntent();   // ① 客户端意图演出（动画）
       ├─ await enemy.TakeTurn();                                   // ② 逻辑执行
       ├─ await combatManager.WaitForUnpause(turnState);
       └─ if (!await combatManager.CheckWinCondition(turnState)) break;
  ├─ RunManager.Instance.ChecksumTracker.GenerateChecksum("After enemy turn end", null);
  └─ await combatManager.EndEnemyTurn(turnState);
```

用 `ExecuteEnemyTurn` 反编译体的原样片段（关键 IL 目标行）：

```csharp
IL_0136:
    nCreature = NCombatRoom.Instance?.GetCreatureNode(<enemy>5__3);
    if (nCreature != null)
    {
        val6 = nCreature.PerformIntent().GetAwaiter();      // ← ①
        ...
    }
IL_01b2:
    val5 = <enemy>5__3.TakeTurn().GetAwaiter();             // ← ②
```

```csharp
// MegaCrit.Sts2.Core.Entities.Creatures.Creature
public Task TakeTurn()   // [AsyncStateMachine(typeof(<TakeTurn>d__140))]
{
    if (!creature.IsMonster || creature.Side != CombatSide.Enemy)
        throw new InvalidOperationException("Only enemy monsters can take automated turns.");
    if (!creature.Monster.SpawnedThisTurn)
        await creature.Monster.PerformMove();     // ← 进入招式
}
```

```csharp
// MegaCrit.Sts2.Core.Models.MonsterModel
public Task PerformMove()   // [AsyncStateMachine(typeof(<PerformMove>d__105))]
{
    if (CombatState == null) return;
    await Cmd.CustomScaledWait(0.1f, 0.2f);              // ① 小延迟
    IsPerformingMove = true;
    MoveState move = NextMove;                            // ② 取当前已声明的招式
    IEnumerable<Creature> targets = CombatState.PlayerCreatures;
    if (TestMode.IsOff)
        Log.Info("Monster " + Id.Entry + " performing move " + move.Id);   // ③ 游戏自带日志！
    await move.PerformMove(targets);                      // ④ ★ 效果真正结算
    MoveStateMachine?.OnMovePerformed(move);
    CombatManager.Instance.History.MonsterPerformedMove(CombatState, this, move, targets);
    IsPerformingMove = false;
    if (Creature.IsDead && Hook.ShouldCreatureBeRemovedFromCombatAfterDeath(CombatState, Creature))
        CombatState.RemoveCreature(Creature);
    await Cmd.CustomScaledWait(0.1f, 0.4f);               // ⑤ 收尾延迟
}
```

```csharp
// MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine.MoveState
public async Task PerformMove(IEnumerable<Creature> targets)
{
    _performedAtLeastOnce = true;
    await _onPerform(targets as Creature[] ?? targets.ToArray());   // ← 招式方法本体
}
```

```csharp
// MegaCrit.Sts2.Core.Nodes.Combat.NCreature
public Task PerformIntent()   // [AsyncStateMachine(typeof(<PerformIntent>d__78))]
{
    foreach (NIntent current in IntentContainer.GetChildren().OfType<NIntent>())
    {
        current.PlayPerform();          // 粒子爆发
        current.SetFrozen(isFrozen: true);
    }
    if (SaveManager.Instance.PrefsSave.FastMode != FastModeType.Instant)
    {
        AnimHideIntent(0.4);
        await Cmd.CustomScaledWait(0.25f, 0.4f);
    }
    else
    {
        IntentContainer.Modulate = new Color(..., 0f);
    }
}
```

**重要**：`PerformIntent()`（动画）在 `TakeTurn()`（逻辑）**之前**被 await，
且 `PerformMove` 内部第一步就是 `Cmd.CustomScaledWait(0.1f, 0.2f)`。
所以「效果结算前」的天然窗口只有 **0.1–0.2 s**（再加上 `PerformIntent` 的 0.25–0.4 s）。
这个窗口不足以播放过场动画，必须显式 Hook 来延长。

### 5.2 是否已有官方 Hook 可用于「怪物出招前」？——**没有**

`MegaCrit.Sts2.Core.Hooks.Hook` 共 151 个成员。用 `Monster|Move|Intent|Turn` 过滤后，
**只有回合相关 Hook**，没有 `BeforeMonsterMove` / `AfterMonsterMove` 之类：

```
BeforeSideTurnStart(ICombatState, CombatSide, IReadOnlyList<Creature>)
AfterSideTurnStart(ICombatState, CombatSide, IReadOnlyList<Creature>)
BeforeSideTurnEnd(ICombatState, CombatSide, IEnumerable<Creature>)
AfterSideTurnEnd(ICombatState, CombatSide, IEnumerable<Creature>)
AfterPlayerTurnStart(ICombatState, PlayerChoiceContext, Player)
AfterTakingExtraTurn(ICombatState, Player)
ShouldTakeExtraTurn(ICombatState, Player)
BeforeAttack(ICombatState, AttackCommand)
AfterAttack(ICombatState, PlayerChoiceContext, AttackCommand)
```

Hook 的监听者来源（`CombatState.IterateHookListeners()`）会把
`creature.Powers`、以及非玩家 creature 的 `creature.Monster`（即 `MonsterModel`）
当作 `AbstractModel` hook listener 收集：

```csharp
for (int i = 0; i < _allies.Count + _enemies.Count; i++)
{
    Creature creature = (i < _allies.Count) ? _allies[i] : _enemies[i - _allies.Count];
    val.AddRange(creature.Powers);
    Player player = creature.Player;
    if (player == null)
        val.Add(creature.Monster);
    ...
}
```

但**没有「怪物将出招」的事件**。→ **必须用 Harmony Prefix。**

（另有 `CombatHistoryEntry` / `MonsterPerformedMoveEntry` 记录已出招，
但那是**事后**记录，`Property Move`/`Monster`/`Targets`，不能用于前置延迟。）

### 5.3 ★ 推荐 Patch 点（按优先级）

#### 首选：`MonsterModel.PerformMove()` 的 Harmony Prefix（替换式）

**签名（精确）：**

```csharp
public System.Threading.Tasks.Task PerformMove();     // 无参数，非 virtual
// 所属：MegaCrit.Sts2.Core.Models.MonsterModel
// 特性：[AsyncStateMachine(typeof(<PerformMove>d__105))]，编译后走 <>4__this = this
```

命中判定：Prefix 里拿到 `__instance`（`MonsterModel`），可读
`__instance.Id.Entry`（= `CEREMONIAL_BEAST` …）、`__instance.NextMove.Id`（= `BEAST_CRY_MOVE` …）、
`__instance.CombatState`、`__instance.Creature`。

**⚠ 关键陷阱：对 async 方法 `return false` 是危险的。**
本方法是 `Task` 返回的 async stub；若 Prefix 直接 `return false` 而不写 `__result`，
Harmony 会让调用方拿到 `null`，而 `Creature.TakeTurn()` 里是
`val = creature.Monster.PerformMove().GetAwaiter()` → **NullReferenceException**。
因此必须**自己补全 `__result` 并复刻原方法体**（原方法体只有 5 步，已完整给出）：

```csharp
// 示意（本报告不构成 Mod 代码，仅说明手法）
[HarmonyPatch(typeof(MonsterModel), nameof(MonsterModel.PerformMove))]
static class MonsterMovePatch
{
    static bool Prefix(MonsterModel __instance, ref Task __result)
    {
        __result = Body(__instance);
        return false;                     // 跳过原状态机，用我们自己的 Task 顶替
    }

    static async Task Body(MonsterModel m)
    {
        ICombatState cs = m.CombatState;
        if (cs == null) return;

        await Cmd.CustomScaledWait(0.1f, 0.2f);

        m.IsPerformingMove = true;
        MoveState move = m.NextMove;
        IEnumerable<Creature> targets = cs.PlayerCreatures;
        Log.Info("Monster " + m.Id.Entry + " performing move " + move.Id);

        if (IsCinematicTarget(m, move))        // ← 目标 BOSS + 目标招式
            await PlayCinematic(m, move);      // ★ 过场动画（在效果结算之前）

        await move.PerformMove(targets);       // 原逻辑

        m.MoveStateMachine?.OnMovePerformed(move);
        CombatManager.Instance.History.MonsterPerformedMove(cs, m, move, targets);
        m.IsPerformingMove = false;
        if (m.Creature.IsDead && Hook.ShouldCreatureBeRemovedFromCombatAfterDeath(cs, m.Creature))
            cs.RemoveCreature(m.Creature);
        await Cmd.CustomScaledWait(0.1f, 0.4f);
    }
}
```

**为什么它是首选：**
- 唯一入口，`NextMove` 已确定、`IsPerformingMove` 还是 `false`
- 同处同时拿到 **怪物实例 + 招式 id**（`MoveState` 自身不含怪物引用，这点很关键）
- 全程在 Godot 主线程的 await 链上，`await` 一个动画 Task 天然阻塞后续结算
- 命中冲突小：整局只有 `Creature.TakeTurn()` 会调它

**风险**：复刻了游戏方法体，版本更新若改了 `PerformMove` 内部步骤需要同步。

#### 备选 A：`MoveState.PerformMove(IEnumerable<Creature>)` 的 Prefix（改动最小）

```csharp
public System.Threading.Tasks.Task PerformMove(System.Collections.Generic.IEnumerable<Creature> targets);
// 所属：MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine.MoveState
```

这里**可以**安全地 `return false` + `ref Task __result`，因为能取回原逻辑：

```csharp
private readonly System.Func<System.Collections.Generic.IReadOnlyList<Creature>, System.Threading.Tasks.Task> _onPerform;
```

```csharp
static readonly AccessTools.FieldRef<MoveState, Func<IReadOnlyList<Creature>, Task>> OnPerform =
    AccessTools.FieldRefAccess<MoveState, Func<IReadOnlyList<Creature>, Task>>("_onPerform");

static bool Prefix(MoveState __instance, IEnumerable<Creature> targets, ref Task __result)
{
    __result = Body(__instance, targets);
    return false;
}

static async Task Body(MoveState state, IEnumerable<Creature> targets)
{
    if (CinemaRegistry.Matches(state.StateId))
        await PlayCinematic(state.StateId);                 // ★
    await OnPerform(state)(targets.ToArray());               // 原逻辑
}
```

- 优点：不需要复刻 `MonsterModel.PerformMove` 的 5 步流程，只包住招式本体。
- 缺点：`MoveState` **不知道自己是哪个怪物**（没有 owner 字段），
  所以必须搭配首选方案的前置 Prefix 记录「当前出招者」，
  或者只按 `StateId` 判定（注意 §4.3 提到的 `BEAM_MOVE` 跨怪物重名问题）。

#### 备选 B：`NCreature.PerformIntent()` 的 Prefix（纯客户端、最安全）

```csharp
public System.Threading.Tasks.Task PerformIntent();   // NCreature
```

它在 `enemy.TakeTurn()` **之前**被调用，是「客户端演出」的自然位置。
缺点是丢弃了 `MonsterModel` 的语义上下文（需要从 `__instance.Creature.Monster` 反查），
且若 `return false` 需复刻意图动画（`PlayPerform` + `AnimHideIntent(0.4)` + 两个 wait）。

#### 备选 C：不改流程，只加延迟

若过场动画是「独立 overlay / 全屏特效」，可以只在 `MonsterModel.PerformMove()` 的
**Prefix 里同步登记 + 起一个 Godot 场景**，不改 `__result`（`return true`），
然后在 `MoveState.PerformMove` 的 Prefix 里 `await` 一个 `TaskCompletionSource`。
这样完全不触碰游戏逻辑，最保守。

### 5.4 可复用的「演出」原语（游戏自身在招式体里就用这些）

从 §3.3/§3.4 的招式实现中提取（Mod 做 cinematic 可直接使用）：

```csharp
NCombatRoom.Instance?.RadialBlur(VfxPosition.Left);                              // 径向模糊
NGame.Instance?.ScreenShake(ShakeStrength.Strong, ShakeDuration.Normal, 180f);   // 屏幕震动
NGame.Instance?.DoHitStop(ShakeStrength.Weak, ShakeDuration.Short);              // 命中定帧
VfxCmd.PlayOnCreatureCenter(Creature, "vfx/vfx_scream");                         // 定点 VFX
SfxCmd.Play("event:/sfx/enemy/enemy_attacks/ceremonial_beast/ceremonial_beast_shrill");
await CreatureCmd.TriggerAnim(Creature, "ATTACK_HEAVY", 0f);                      // Spine 动画
NRunMusicController.Instance?.UpdateMusicParameter("vantom_progress", 3f);        // BGM 参数
await Cmd.Wait(0.75f);                                                           // 硬等待
await Cmd.CustomScaledWait(0.1f, 0.2f);                                          // 受 FastMode 缩放的等待
```

Spine 直接控制（`Vantom.DismemberMove` 用的低层写法）：

```csharp
MegaAnimationState s = (NCombatRoom.Instance?.GetCreatureNode(Creature))?
                           .SpineAnimation.GetAnimationState();
s?.SetAnimation("_tracks/attack_heavy", loop: false, 1);
s?.AddAnimation("_tracks/charged_0", 0f, loop: true, 1);
```

相关 VFX 场景资源（来自 PCK 索引，命名很有参考价值）：
`res://scenes/vfx/monsters/kaiser_crab_boss_explosion.tscn`、
`res://scenes/vfx/monsters/kin_priest_beam_vfx.tscn`、
`res://scenes/vfx/monsters/test_subject_burn_vfx.tscn`、
`res://scenes/vfx/monsters/waterfall_giant_water_shader.tres` 等。

---

## 6. Q6 — Intent / 招式信息是否在多人中同步？

**结论：不通过网络消息同步「怪物 intent」，而是通过「共享种子 + 确定性模拟 + 状态校验」同步。**

### 6.1 种子是共享的

```csharp
public class RunRngSet
{
    public string StringSeed { get; }
    public ulong Seed { get; }
    public Rng MonsterAi => GetRng(RunRngType.MonsterAi);      // ← 怪物 AI 专用流
    public Rng Shuffle { get; }  public Rng CombatTargets { get; }  /* ...共 12 条流... */

    public RunRngSet(string seed)
    {
        StringSeed = seed;
        if (seed.StartsWith("old"))
            Seed = (uint)StringHelper.GetDeterministicHashCodeOld(seed.Substring("old".Length, ...));
        else
            Seed = StringHelper.GetDeterministicHashCode(seed);
        foreach (RunRngType t in Enum.GetValues<RunRngType>())
            _rngs[t] = CreateRng(t);
    }
}

public enum RunRngType
{
    UpFront, Shuffle, UnknownMapPoint, CombatCardGeneration, CombatPotionGeneration,
    CombatCardSelection, CombatEnergyCosts, CombatTargets, MonsterAi, Niche,
    CombatOrbs, TreasureRoomRelics
}
```

`MonsterModel.RollMove` 的 RNG 来源写死为 `RunRng.MonsterAi`：

```csharp
public void RollMove(IEnumerable<Creature> targets)
{
    NextMove = MoveStateMachine.RollMove(targets, Creature, RunRng.MonsterAi);
}
```

### 6.2 同步机制（消息层）

- `MegaCrit.Sts2.Core.Multiplayer.CombatStateSynchronizer`
  — 成员显示它同步的是 **玩家数据 + RNG + 遗物袋**，而不是怪物状态：

```csharp
public CombatStateSynchronizer(INetGameService netService, RunLobby runLobby, RunState runState);
private void OnSyncPlayerMessageReceived(SyncPlayerDataMessage syncMessage, ulong senderId);
private void OnSyncRngMessageReceived(SyncRngMessage syncMessage, ulong senderId);
public void StartSync();
public Task WaitForSync();
private bool IsDisabled { get; set; }
// 字段：SerializableRunRngSet _rngSet; SerializableRelicGrabBag _sharedRelicGrabBag;
```

- 大厅阶段种子变更走 `MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby.LobbySeedChangedMessage`
- 玩家动作走 `...Messages.Game.ActionEnqueuedMessage` / `HookActionEnqueuedMessage`
- `...Messages.Game.SyncRngMessage` 同步 RNG 状态
- **搜索 `MegaCrit.Sts2.Core.Multiplayer.Messages.Game.*` 中匹配 `Monster|Intent|Move` 的类型：
  只有 `CardRemovedMessage` 命中（因为 `Move` 出现在 `CardRemoved` 里的子串巧合）。
  → **不存在任何 "MonsterIntentMessage" / "MonsterMoveMessage"。**

### 6.3 收敛校验

`ExecuteEnemyTurn` 末尾：

```csharp
RunManager.Instance.ChecksumTracker.GenerateChecksum("After enemy turn end", null);
```

配合 `MegaCrit.Sts2.Core.Multiplayer.Replay.ReplayChecksumData` 与
`CombatReplay` / `CombatReplayEvent` / `CombatReplayEventType`，
说明引擎用 **校验和比对** 来发现不同步，而非逐帧广播怪物行为。

### 6.4 对 Mod 的含义（重要）

1. 怪物 intent 是**确定性推导**的 → 只要种子一致，各客户端自己算出相同的 `NextMove`。
2. 因此**每个客户端都会各自触发你 Prefix 里的过场动画** —— 这通常是想要的
   （人人都在本地看到演出），不需要联机广播。
3. **但如果演出依赖随机数，绝对不能用 `System.Random` 或 `Rng.Chaotic`**，
   否则会破坏 `ChecksumTracker` 的校验和一致性（`Chaotic` 是独立的非确定性流，
   游戏自己在纯表现层用它，如 `DismemberMove` 的 `Rng.Chaotic.NextFloat(-10f, 10f)` 只影响震动幅度）。
   → 演出若引用随机，应使用 `RunRng` 的某条流，或干脆全确定性。
4. 你的 Prefix 若改变了 `MoveStateMachine` 的状态推进（例如插入额外状态），
   会直接导致校验和不一致 → **只做「等待 + 播动画」，不要改状态机。**
5. `await` 时长不参与校验和（时间不是模拟状态），所以**延长等待是安全的**。

---

## 7. 一页速查

| 问题 | 答案 |
|---|---|
| 怪物基类 | `MegaCrit.Sts2.Core.Models.MonsterModel : AbstractModel` |
| 招式定义处 | `protected abstract MonsterMoveStateMachine GenerateMoveStateMachine()` |
| 招式是什么 | `MoveState` 对象（字符串 stateId + `Func<IReadOnlyList<Creature>,Task>` + `AbstractIntent[]`） |
| 招式 id | `MoveState.StateId` / `MoveState.Id`，如 `BEAST_CRY_MOVE` |
| 中文名查法 | `monsters` 表 key `<ModelId.Entry>.moves.<StateId 去 _MOVE>.title` |
| Intent 怎么选 | 由状态机决定状态，intent 是 `MoveState` 的属性；随机只在 `RandomBranchState` |
| Intent RNG | `RunRng.MonsterAi`（`RunRngType.MonsterAi`） |
| 客户端显示 | `NCreature.RefreshIntents()` → `NIntent.UpdateIntent()` → `UpdateVisuals()`；场景 `res://scenes/combat/intent.tscn` |
| 执行入口 | `MonsterModel.PerformMove()` → `MoveState.PerformMove(IEnumerable<Creature>)` → `_onPerform(...)` |
| 客户端演出时机 | `NCreature.PerformIntent()`，由 `CombatManager.ExecuteEnemyTurn` 在 `TakeTurn()` 前 await |
| ★ 最佳 Hook | `MonsterModel.PerformMove()` Prefix（拿得到怪物 + `NextMove.Id`），`ref Task __result` + `return false` |
| ⚠ 陷阱 | async stub 上 `return false` 不写 `__result` → 调用方拿到 `null` Task → NRE |
| 官方 Hook | **无**「怪物出招前」Hook（`Hook` 151 成员中只有回合级） |
| 多人同步 | 共享种子 + `RunRngSet.MonsterAi` 确定性 + `CombatStateSynchronizer` + `ChecksumTracker`；**无怪物 intent 网络消息** |
| 本地化文件 | `res://localization/<lang>/*.json`（PCK 内），可用 `tools\godot` 挂载 PCK 提取 |
| PCK 里的 .cs | 全是 1 字节存根，**无游戏源码** |

---

## 8. 附录 — 本次侦察产出的证据文件

均位于仓库内（可删；属于侦察中间产物）：

| 路径 | 内容 |
|---|---|
| `.recon/pck_index.txt` | PCK 完整文件索引（18,265 项，含 `###LOC` 标记） |
| `.recon/list_pck.gd`, `.recon/extract_pck.gd` | Godot PCK 挂载/枚举/提取脚本 |
| `.recon/pck/localization/zhs/*.json` | 提取出的简体中文本地化（含 `monsters.json`、`encounters.json`） |
| `.recon/pck/localization/eng/*.json` | 提取出的英文本地化 |
| `.recon/pck/src/**` | PCK 内 `.cs` 存根（证明无源码） |
| `.recon/monsters-all.txt` | 全部 `MegaCrit.Sts2.Core.Models.Monsters.*` 类型（553） |
| `.recon/boss-*.txt` | 12 个目标 BOSS 的 `GenerateMoveStateMachine()` + 招式方法签名 |
| `.recon/movebody-*.txt` | 5 个代表性招式的完整 `MoveNext()` 反编译体 |
| `.recon/MonsterModel-members.txt` | `MonsterModel` 完整成员表 |
| `.recon/MSM-outer.txt`, `.recon/MoveState.txt` | 状态机核心类型 |
| `.recon/MonsterModel-PerformMove-body.txt` | `MonsterModel.PerformMove()` 完整体 |
| `.recon/Creature-TakeTurn-body.txt` | `Creature.TakeTurn()` 完整体 |
| `.recon/ExecuteEnemyTurn-body.txt` | `CombatManager.ExecuteEnemyTurn()` 完整体 |
| `.recon/NCreature-PerformIntent-body.txt` | `NCreature.PerformIntent()` 完整体 |
| `.recon/RunRngSet.txt` | `RunRngSet` 完整成员 |
| `.recon/mp-types.txt` | 多人相关类型清单 |

---

## 9. 给 Mod 实现的三条直接建议

1. **判定键用「怪物 ModelId + StateId」二元组**，不要只用 StateId
   （`BEAM_MOVE` 在 `TorchHeadAmalgam` / `TheAdversaryMkOne` / `KinPriest` 重名）。
   判定码：`__instance.Id.Entry == "CEREMONIAL_BEAST" && move.Id == "BEAST_CRY_MOVE"`。

2. **「无厌沙虫 - 吞噬」需要单独处理**：它不在 `TheInsatiable` 的 `GenerateMoveStateMachine()`
   里，而是 `SandpitPower` 机制（loc key `THE_INSATIABLE.moves.DEVOUR.title` 存在但无 MoveState）。
   若必须支持它，需要另外定位 `SandpitPower` 的触发点，`MonsterModel.PerformMove` Hook 覆盖不到。

3. **「女王 - 提线木偶」没有官方中文/英文 loc 标题**（`QUEEN.moves.PUPPET_STRINGS.title` 不存在）。
   若 Mod 要显示中文名，需自行硬编码「提线木偶」；判定用 `PUPPET_STRINGS_MOVE`。
   好消息是女王的开场 `PUPPET_STRINGS → YOU_ARE_MINE` 是**无随机**的固定连招，触发时机非常稳定。
