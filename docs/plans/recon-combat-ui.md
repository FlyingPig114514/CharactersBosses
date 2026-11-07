# 战斗表现层反编译侦察报告（recon-combat-ui）

目标 Mod 需求：Darkest Dungeon 风格「行动演出」——找到每个 `Creature` 的屏幕节点、读取屏幕坐标/尺寸、遮挡/高亮任意生物、在角落画 HUD 横幅与完整卡牌、可选缩放/平移镜头。

**本文仅为侦察结论，不含 Mod 代码。**

## 0. 方法与证据来源

工具：`tools/ilspy-cli`（见 `docs/plans/recon-tooling.md`）

```powershell
$dotnet = "D:\Sts2ModProject\SlaytheSpire2-ModScaffold\tools\dotnet\dotnet.exe"
$cli    = "D:\Sts2ModProject\SlaytheSpire2-ModScaffold\tools\ilspy-cli\bin\Release\net10.0\ilspycli.dll"
$asm    = "D:\Sts2ModProject\SlaytheSpire2-ModScaffold\references\sts2.dll"
& $dotnet $cli types   $asm "<typeRegex>"
& $dotnet $cli type    $asm "<typeRegex>" 1
& $dotnet $cli members $asm "<typeRegex>" 1
& $dotnet $cli method  $asm "<typeRegex>" "<methodRegex>" 1
& $dotnet $cli search  $asm "<memberNameRegex>" 40
```

原始反编译转储保存在 `docs/plans/_recon/`（`TMegaCrit__*.txt` 为整类反编译，`*-types.txt` 为类型清单，`search-*.txt` 为成员名全局搜索结果）。
下文所有 `// ===== ... =====` 代码块均为该 CLI 的原始输出，未改写。

Godot API 侧另用同一 CLI 反编译 `references/GodotSharp.dll` 核对。

---

## 1. `MegaCrit.Sts2.Core.Nodes.*` 战斗相关类型清单

### 1.1 存在性判定（精确类名）

命令：`& $dotnet $cli types $asm "\.$n$"`

| 查询名 | 结果 |
|---|---|
| `NCombat` | **不存在** |
| `NPlayer` | **不存在** |
| `NEnemy` | **不存在** |
| `NDamageNumber` | **不存在**（实际名 `NDamageNumVfx`） |
| `NCombatRoom` | `MegaCrit.Sts2.Core.Nodes.Rooms.NCombatRoom` |
| `NCombatUi` | `MegaCrit.Sts2.Core.Nodes.Combat.NCombatUi` |
| `NCreature` | `MegaCrit.Sts2.Core.Nodes.Combat.NCreature` |
| `NCard` | `MegaCrit.Sts2.Core.Nodes.Cards.NCard` |
| `NPlayerHand` | `MegaCrit.Sts2.Core.Nodes.Combat.NPlayerHand` |
| `NOverlayStack` | `MegaCrit.Sts2.Core.Nodes.Screens.Overlays.NOverlayStack` |
| `NIntent` | `MegaCrit.Sts2.Core.Nodes.Combat.NIntent` |
| `NHealthBar` | `MegaCrit.Sts2.Core.Nodes.Combat.NHealthBar` |
| `NDamageNumVfx` | `MegaCrit.Sts2.Core.Nodes.Vfx.NDamageNumVfx` |
| `NPlayerTurnBanner` / `NEnemyTurnBanner` / `NCombatStartBanner` | 均存在（`...Nodes.Combat.*`） |

### 1.2 关键类型与基类

基类取自 `members` 输出首行 `// ===== <FQN> (base <BaseType>) =====` 与整类反编译的 class 声明。

| 全限定名 | 基类 / 接口 | 场景路径 | 关键成员 |
|---|---|---|---|
| `MegaCrit.Sts2.Core.Nodes.Rooms.NCombatRoom` | `Control, IScreenContext, IRoomWithProceedButton` | `res://scenes/rooms/combat_room.tscn` | `static NCombatRoom? Instance`, `IEnumerable<NCreature> CreatureNodes`, `NCreature? GetCreatureNode(Creature)`, `void AddCreature(Creature)`, `void RemoveCreatureNode(NCreature)`, `Control SceneContainer`, `NCombatUi Ui`, `Control CombatVfxContainer`, `Control BackCombatVfxContainer`, `NCombatBackground Background`, `void RadialBlur(VfxPosition)`, `void PlaySplashVfx(Creature, Color)` |
| `MegaCrit.Sts2.Core.Nodes.Combat.NCombatUi` | `Control` | 战斗室内 `%CombatUi` | `NPlayerHand Hand`, `Control PlayContainer`, `NCardPlayQueue PlayQueue`, `Control CardPreviewContainer`, `NEndTurnButton EndTurnButton`, `Control EnergyCounterContainer`, `static bool IsDebugHidingHpBar / IsDebugHidingIntent / IsDebugHideTextVfx / IsDebugHideTargetingUi / IsDebugHideMpTargetingUi / IsDebugHideMpIntents`（**public getter / private setter**，Mod 只能读）, `void Activate(CombatState)`, `void Enable()/Disable()`, `void AnimIn()/AnimOut()` |
| `MegaCrit.Sts2.Core.Nodes.Combat.NCreature` | `Control` | `SceneHelper.GetScenePath("combat/creature")` | `Creature Entity`, `NCreatureVisuals Visuals`, `Control Hitbox`, `Control IntentContainer`, `static NCreature? Create(Creature)`, `void SetAnimationTrigger(string)`, `void AnimShake()`, `float StartDeathAnim(bool)`, `Vector2 GetTopOfHitbox()/GetBottomOfHitbox()`, `Vector2 VfxSpawnPosition`, `void ScaleTo(float,double)`, `void SetScaleAndHue(float,float)`, `void ToggleIsInteractable(bool)` |
| `MegaCrit.Sts2.Core.Nodes.Combat.NCreatureVisuals` | `Node2D` | 生物 `_scene` 内 `%Visuals` | `Node2D Body`, `Control Bounds`, `Marker2D VfxSpawnPosition / IntentPosition / OrbPosition / TalkPosition`, `float DefaultScale`, `bool IsPlayingHurtAnimation()/IsPlayingIdleAnimation()`, `void SetScaleAndHue(float,float)`, `MegaSprite SpineBody` |
| `MegaCrit.Sts2.Core.Nodes.Combat.NCreatureStateDisplay` | `Control` | 生物 `%HealthBar` | `void SetCreature(Creature)`, `void SetCreatureBounds(Control)`, `void AnimateIn(HealthBarAnimMode)`, `void AnimateOut()`, `void HideImmediately()`, `void ShowNameplate()/HideNameplate()`, `void TrackBlockStatus(Creature)` |
| `MegaCrit.Sts2.Core.Nodes.Combat.NHealthBar` | `Control` | 状态显示内 `%HealthBar` | `void SetCreature(Creature)`, `void RefreshValues()`, `void UpdateLayoutForCreatureBounds(Control)`, `void FadeOutHpLabel(float,float)`, `void FadeInHpLabel(float)`, `void AnimateInBlock(int,int)` |
| `MegaCrit.Sts2.Core.Nodes.Combat.NIntent` | `Control` | `res://scenes/combat/intent.tscn` | `static NIntent? Create(float startTime)`, `void UpdateIntent(AbstractIntent, IEnumerable<Creature>, Creature)`, `void SetFrozen(bool)`, `void PlayPerform()` |
| `MegaCrit.Sts2.Core.Nodes.Combat.NPlayerHand` | `Control` | 战斗 UI 内 `%Hand` | `static NPlayerHand? Instance`, `Control CardHolderContainer`, `IReadOnlyList<NHandCardHolder> Holders/ActiveHolders`, `NHandCardHolder FocusedHolder`, `NCard GetCard(CardModel)`, `NHandCardHolder Add(NCard, int)`, `void AnimIn()/AnimOut()` |
| `MegaCrit.Sts2.Core.Nodes.Cards.NCard` | `Control, IPoolable` | `res://scenes/cards/card.tscn` | `static NCard? Create(CardModel, ModelVisibility)`, `static void InitPool()`, `CardModel Model`, `Control Body`, `NCardHighlight CardHighlight`, `Vector2 GetCurrentSize()`, `void UpdateVisuals(PileType, CardPreviewMode)`, `static Vector2 defaultSize = (300,422)` |
| `MegaCrit.Sts2.Core.Nodes.Cards.NTinyCard` | `NButton` | — | `void SetCard(CardModel)`, `void Set(CardPoolModel, CardType, CardRarity)` |
| `MegaCrit.Sts2.Core.Nodes.Screens.Overlays.NOverlayStack` | `Control` | Run 内 `%OverlayScreensContainer` | 见 §7 |
| `MegaCrit.Sts2.Core.Nodes.Vfx.NDamageNumVfx` | `Node2D` | `SceneHelper.GetScenePath("vfx/vfx_damage_num")` | `static NDamageNumVfx? Create(Creature, DamageResult)` / `(Creature,int,bool)` / `(Vector2,int)` |
| `MegaCrit.Sts2.Core.Nodes.Combat.NPlayerTurnBanner` | `Control` | `combat/player_turn_banner` | `static NPlayerTurnBanner? Create(int roundNumber)` |
| `MegaCrit.Sts2.Core.Nodes.Combat.NEnemyTurnBanner` | `Control` | — | `static NEnemyTurnBanner? Create()` |
| `MegaCrit.Sts2.Core.Nodes.Combat.NCombatStartBanner` | `Control` | — | `static NCombatStartBanner? Create()` |
| `MegaCrit.Sts2.Core.Nodes.CommonUi.NCommonBanner` | `Control` | — | `void AnimateIn()`, `void AnimateOut()`, `void ChangeText(string)`（内部 `%Label` 是 `MegaLabel`） |
| `MegaCrit.Sts2.Core.Nodes.CommonUi.NGlobalUi` | `Control` | Run 内 `%GlobalUi` | `NTopBar TopBar`, `NOverlayStack Overlays`, `NTargetManager TargetManager`, `Control AboveTopBarVfxContainer`, `Control CardPreviewContainer`, `NCapstoneContainer CapstoneContainer`, `NMapScreen MapScreen` |
| `MegaCrit.Sts2.Core.Nodes.Combat.NTargetManager` | `Node2D` | `NGlobalUi` 子节点 `TargetManager` | `static NTargetManager? Instance`, `bool IsInSelection`, `Node? HoveredNode`, `void CancelTargeting()` |
| `MegaCrit.Sts2.Core.Nodes.Combat.NSelectionReticle` | `Control` | 生物 `%SelectionReticle` | `bool IsSelected`, `void OnSelect()`, `void OnDeselect()`（内置高亮方框） |
| `MegaCrit.Sts2.Core.Nodes.Combat.NPowerContainer` | — | 状态显示内 `%PowerContainer` | `void SetCreatureBounds(Control)` |
| `MegaCrit.Sts2.Core.Nodes.Vfx.NHitSparkVfx` | `Node2D` | — | `static NHitSparkVfx Create(Creature, bool requireInteractable)` |
| `MegaCrit.Sts2.Core.Nodes.Vfx.NBlockSparkVfx` | `Node2D` | — | `static NBlockSparkVfx Create(Creature)` |
| `MegaCrit.Sts2.Core.Nodes.Vfx.NHealNumVfx` | `Node2D` | — | `static NHealNumVfx Create(Creature, decimal)` |
| `MegaCrit.Sts2.Core.Nodes.Vfx.NAdditiveOverlayVfx` | `ColorRect` | — | `static NAdditiveOverlayVfx Create(VfxColor)`（全屏染色，自动淡入淡出 1.5s） |
| `MegaCrit.Sts2.Core.Nodes.Vfx.NSmokyVignetteVfx` | `Control` | — | `static NSmokyVignetteVfx Create(Color tint, Color highlightColor)` |
| `MegaCrit.Sts2.Core.Nodes.Vfx.NRadialBlurVfx` | `BackBufferCopy` | 战斗室 `RadialBlur` | `void Activate(VfxPosition)` |

枚举：
```csharp
public enum VfxPosition { None, Left, Center, Right }            // MegaCrit.Sts2.Core.Nodes.Vfx
public enum VfxColor { Red, Green, Blue, Purple, Black, White, Cyan, Gold, Orange, Swamp, DarkGray }
public enum ShakeStrength { None, VeryWeak, Weak, Medium, Strong, TooMuch }   // ...Nodes.Vfx.Utilities
public enum ShakeDuration { None, Short, Normal, Long, Forever }
public enum ModelVisibility { None, Visible, NotSeen, Locked }   // MegaCrit.Sts2.Core.Entities.UI
```

### 1.3 `NCombatRoom` 内部节点树（`%` 唯一名，Mod 可用 `GetNode<T>("%名称")` 拿到）

```csharp
// T...NCombatRoom.txt:_Ready()
Ui = GetNode<NCombatUi>("%CombatUi");
SceneContainer   = GetNode<Control>("%CombatSceneContainer");
_allyContainer   = GetNode<Control>("%AllyContainer");
_enemyContainer  = GetNode<Control>("%EnemyContainer");
BackCombatVfxContainer = GetNode<Control>("%BackCombatVfxContainer");
CombatVfxContainer     = GetNode<Control>("%CombatVfxContainer");
_radialBlur = GetNode<NRadialBlurVfx>("RadialBlur");
_waitingForOtherPlayersOverlay = GetNode<Control>("%WaitingForOtherPlayers");
_proceedButton = GetNode<NProceedButton>("%ProceedButton");
BgContainer = GetNode<Control>("%BgContainer");
```
（`_allyContainer` / `_enemyContainer` 是 **private 字段**，Mod 无法直接访问 → 走 `%AllyContainer` / `%EnemyContainer` 唯一名或 `CreatureNodes`。）

`NCombatUi._Ready()`：
```csharp
EnergyCounterContainer = GetNode<Control>("%EnergyCounterContainer");
_starCounter = GetNode<NStarCounter>("%StarCounter");
EndTurnButton = GetNode<NEndTurnButton>("%EndTurnButton");
PingButton = GetNode<NPingButton>("%PingButton");
_combatPilesContainer = GetNode<NCombatPilesContainer>("%CombatPileContainer");
Hand = GetNode<NPlayerHand>("%Hand");
PlayContainer = GetNode<Control>("%PlayContainer");
PlayQueue = GetNode<NCardPlayQueue>("%PlayQueue");
CardPreviewContainer = GetNode<Control>("%CardPreviewContainer");
MessyCardPreviewContainer = GetNode<NMessyCardPreviewContainer>("%MessyCardPreviewContainer");
```

`NCreature._Ready()`（单个生物的子树）：
```csharp
_stateDisplay = GetNode<NCreatureStateDisplay>("%HealthBar");
IntentContainer = GetNode<Control>("%Intents");
Hitbox = GetNode<Control>("%Hitbox");
_selectionReticle = GetNode<NSelectionReticle>("%SelectionReticle");
...
this.AddChildSafely(Visuals);          // NCreatureVisuals, Node2D
this.MoveChildSafely(Visuals, 0);
Visuals.Position = Vector2.Zero;
_stateDisplay.SetCreature(Entity);
```
`NCreatureStateDisplay._Ready()`：
```csharp
_powerContainer = GetNode<NPowerContainer>("%PowerContainer");
_nameplateContainer = GetNode<Control>("%NameplateContainer");
_nameplateLabel = GetNode<MegaLabel>("%NameplateLabel");
_healthBar = GetNode<NHealthBar>("%HealthBar");
_hpBarHitbox = GetNode<Control>("%HpBarHitbox");
```
`NCreatureVisuals._Ready()`（绑定/骨骼视觉子树）：
```csharp
_body = GetNode<Node2D>("%Visuals");
Bounds = GetNode<Control>("%Bounds");
IntentPosition = GetNode<Marker2D>("%IntentPos");
VfxSpawnPosition = GetNode<Marker2D>("%CenterPos");
OrbPosition = (HasNode("%OrbPos") ? GetNode<Marker2D>("%OrbPos") : IntentPosition);
TalkPosition = (HasNode("%TalkPos") ? GetNode<Marker2D>("%TalkPos") : null);
```

---

## 2. `Creature`（模型）→ 屏幕节点 `NCreature`（视图）

**结论：`Creature` 上就有现成方法，不需要自建字典。**

```csharp
// ===== MegaCrit.Sts2.Core.Entities.Creatures.Creature :: public NCreature? GetCreatureNode() =====
public NCreature? GetCreatureNode()
{
    if (TestMode.IsOn)
    {
        return null;
    }
    object obj = NCombatRoom.Instance?.GetCreatureNode(this);
    if (obj == null)
    {
        NBestiary? instance = NBestiary.Instance;
        if (instance == null)
        {
            return null;
        }
        obj = instance.GetCreatureNode(this);
    }
    return (NCreature?)obj;
}
```

实现是线性查找（不是字典）：

```csharp
// ===== MegaCrit.Sts2.Core.Nodes.Rooms.NCombatRoom :: public NCreature? GetCreatureNode(Creature? creature) =====
public NCreature? GetCreatureNode(Creature? creature)
{
    if (creature != null)
    {
        return Enumerable.FirstOrDefault<NCreature>(CreatureNodes, (Func<NCreature, bool>)((NCreature c) => c.Entity == creature));
    }
    return null;
}
```

全量枚举：
```csharp
public System.Collections.Generic.IEnumerable<NCreature> CreatureNodes => _creatureNodes;   // NCombatRoom
public System.Collections.Generic.IEnumerable<NCreature> RemovingCreatureNodes => ...;      // 正在移除的
```

反查（节点 → 模型）：`nCreature.Entity`（`public Creature Entity { get; private set; }`）。
节点创建点：
```csharp
public void AddCreature(Creature creature)
{
    NCreature nCreature = NCreature.Create(creature);
    _creatureNodes.Add(nCreature);
    if (creature.IsPlayer || creature.PetOwner != null) { _allyContainer.AddChildSafely(nCreature); }
    else { _enemyContainer.AddChildSafely(nCreature); }
    ...
}
```

### 2.1 屏幕位置 / 尺寸怎么读

`NCreature` 是 `Control`，**它的 `Hitbox`（`%Hitbox`）由 `UpdateBounds` 同步为生物的屏幕包围盒** —— 这就是「高亮方框」该用的矩形。同步时机是**事件驱动**（`_Ready`、Spine 动画状态切换 `BoundsUpdated`、`SetScaleAndHue`、`ScaleTo/OstyScaleToSize` 回调），**不是每帧**，所以 Mod 若在 tween 中途需要更精确的矩形，可自行重算（`Body`/`Visuals.Bounds`）或轮询：

```csharp
// ===== NCreature.UpdateBounds(Node boundsContainer) =====
private void UpdateBounds(Node boundsContainer)
{
    Control node = boundsContainer.GetNode<Control>("%Bounds");
    Vector2 size = node.Size * Visuals.Scale / _tempScale;
    Vector2 vector = (node.GlobalPosition - base.GlobalPosition) / _tempScale;
    Hitbox.Size = size;
    Hitbox.GlobalPosition = base.GlobalPosition + vector;
    _selectionReticle.Size = size;
    _selectionReticle.GlobalPosition = base.GlobalPosition + vector;
    _selectionReticle.PivotOffset = _selectionReticle.Size * 0.5f;
    IntentContainer.Position = boundsContainer.GetNode<Marker2D>("IntentPos").Position - IntentContainer.Size * 0.5f;
    IntentContainer.Position = new Vector2(IntentContainer.Position.X, IntentContainer.Position.Y * Visuals.Scale.X);
    _stateDisplay.SetCreatureBounds(Hitbox);
}
```

便捷方法：
```csharp
public Vector2 GetTopOfHitbox()    => Hitbox.GlobalPosition + new Vector2(Hitbox.Size.X * 0.5f, 0f);
public Vector2 GetBottomOfHitbox() => Hitbox.GlobalPosition + new Vector2(Hitbox.Size.X * 0.5f, Hitbox.Size.Y);
public Vector2 VfxSpawnPosition => Visuals.VfxSpawnPosition.GlobalPosition;   // 生物中心 Marker2D 的全局坐标
public Node2D Body => Visuals.GetCurrentBody();
public Control Hitbox { get; private set; }     // public getter ✔
```

> 尺寸另有 `Visuals.Bounds.Size`（本地未缩放尺寸，见 `PositionEnemies` 里 `nCreature.Visuals.Bounds.Size.X`）。

`Creature` 侧可用判定属性：
```csharp
P System.Boolean IsMonster { get; }   P System.Boolean IsPlayer { get; }   P System.Boolean IsPet { get; }
P System.Boolean IsAlive { get; }     P System.Boolean IsDead { get; }     P System.Boolean IsEnemy { get; }
P System.Boolean IsHittable { get; }  P MegaCrit.Sts2.Core.Combat.CombatSide Side { get; }
P System.String SlotName { get; set; }  P MegaCrit.Sts2.Core.Entities.Creatures.Creature PetOwner { get; set; }
P System.Int32 CurrentHp / MaxHp / Block { get; set; }
```
另有 `public Control? GetVfxContainer()`（= `NCombatRoom.Instance.CombatVfxContainer`）：

```csharp
public Control? GetVfxContainer()
{
    if (TestMode.IsOn) return null;
    if (NCombatRoom.Instance?.GetCreatureNode(this) != null) return NCombatRoom.Instance.CombatVfxContainer;
    if (NBestiary.Instance?.GetCreatureNode(this) != null)   return NBestiary.Instance.VfxContainer;
    return null;
}
```

---

## 3. 生物节点上的动画 API

### 3.1 `NCreature` 公开动画方法（全部签名）

```csharp
public void SetAnimationTrigger(string trigger);          // 转发给 Spine CreatureAnimator
public float GetCurrentAnimationLength();
public float GetCurrentAnimationTimeRemaining();
public Tween AnimDisableUi();                             // 血条 modulate:a -> 0
public Tween AnimEnableUi();
public float StartDeathAnim(bool shouldRemove);           // 触发 "Dead" 并启动 AnimDie
public void StartReviveAnim();                            // 触发 "Revive"，无则临时淡入淡出
public void AnimHideIntent(double delay = 0.0);           // IntentContainer modulate:a -> 0
public void AnimShake();                                  // 代码驱动的左右抖动（1s）
public void SetScaleAndHue(float scale, float hue);
public void ScaleTo(float size, double duration);
public void SetDefaultScaleTo(float size, float duration);
public void OstyScaleToSize(float ostyHealth, double duration);
public static Vector2 GetOstyOffsetFromPlayer(Creature osty);
public void ToggleIsInteractable(bool on);
public void DisableInteractionForDeath();
public void ShowMultiselectReticle();  public void HideMultiselectReticle();
public void ShowSingleSelectReticle(); public void HideSingleSelectReticle();
public Vector2 GetTopOfHitbox();       public Vector2 GetBottomOfHitbox();
```

`AnimShake` 源码（**这是游戏里唯一"代码驱动位移"的受击抖动**）：
```csharp
public void AnimShake()
{
    if (IsInsideTree() && (_shakeTween == null || !_shakeTween.IsRunning()) && !Visuals.IsPlayingHurtAnimation())
    {
        Visuals.Position = Vector2.Zero;
        _shakeTween = CreateTween();
        _shakeTween.TweenMethod(Callable.From<float>((Action<float>)([CompilerGenerated] (float t) =>
        {
            Visuals.Position = Vector2.Right * 10f * Mathf.Sin(t * 4f) * Mathf.Sin(t * 0.5f);
        })), 0f, (float)Math.PI * 2f, 1.0).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
    }
}
```

`StartDeathAnim` / `SetAnimationTrigger` 源码：
```csharp
public void SetAnimationTrigger(string trigger)
{
    _spineAnimator?.SetTrigger(trigger);
}

public float StartDeathAnim(bool shouldRemove)
{
    DisableInteractionForDeath();
    foreach (NIntent current in Enumerable.OfType<NIntent>(IntentContainer.GetChildren()))
        current.SetFrozen(isFrozen: true);
    Task deathAnimationTask = DeathAnimationTask;
    if (deathAnimationTask != null && !deathAnimationTask.IsCompleted) return 0f;
    float a = 0f;
    if (_spineAnimator != null)
    {
        ...
        SetAnimationTrigger("Dead");
        a = GetCurrentAnimationLength();
    }
    DeathAnimationTask = AnimDie(shouldRemove, DeathAnimCancelToken.Token);
    TaskHelper.RunSafely(DeathAnimationTask);
    ...
}
```

### 3.2 Spine 动画状态机：触发器常量与状态名

```csharp
// MegaCrit.Sts2.Core.Animation.CreatureAnimator
public const string idleTrigger    = "Idle";
public const string attackTrigger  = "Attack";
public const string powerUpTrigger = "PowerUp";
public const string castTrigger    = "Cast";
public const string deathTrigger   = "Dead";
public const string hitTrigger     = "Hit";
public const string reviveTrigger  = "Revive";

public void SetTrigger(string trigger);
public bool HasTrigger(string trigger);
public void AddAnyState(string trigger, AnimState state, Func<bool>? condition = null);
public event Action<string>? BoundsUpdated;
```
```csharp
// MegaCrit.Sts2.Core.Animation.AnimState
public static readonly string attackAnim = "attack";   // 注意：状态 Id 是小写
public static readonly string castAnim   = "cast";
public static readonly string dieAnim    = "die";
public static readonly string hurtAnim   = "hurt";
public static readonly string idleAnim   = "idle_loop";
public static readonly string lowHealthIdleAnim, reviveAnim, stunAnim;
// 触发器 -> 状态映射来源：
public virtual CreatureAnimator GenerateAnimator(MegaSprite controller)   // MonsterModel
{
    AnimState animState  = new AnimState("idle_loop", isLooping: true);
    AnimState animState2 = new AnimState("cast");
    AnimState animState3 = new AnimState("attack");
    AnimState animState4 = new AnimState("hurt");
    AnimState state      = new AnimState("die");
    ...
    creatureAnimator.AddAnyState("Idle", animState);
    creatureAnimator.AddAnyState("Cast", animState2);
    creatureAnimator.AddAnyState("Attack", animState3);
    creatureAnimator.AddAnyState("Dead", state);
    creatureAnimator.AddAnyState("Hit", animState4);
}
```

### 3.3 是否存在 `AttackAsync` / `PlayAttackAnimation`？

**没有以这些名字命名的 API。** 全局搜索 `PlayAttack` / `AttackAnim` 的结果里没有任何 `NCreature` 方法：

- `AttackAnim` 只命中 `CharacterModel.AttackAnimDelay { get; }`（以及各角色 override）。
- `PlayAttackAnim` 只命中 `NKaiserCrabBossBackground.PlayAttackAnim(...)`（Boss 背景特效，与生物无关）。
- **攻击动画的实际入口是 `CreatureCmd.TriggerAnim` + `AttackCommand`**：

```csharp
// MegaCrit.Sts2.Core.Commands.CreatureCmd
public static System.Threading.Tasks.Task TriggerAnim(Creature creature, string triggerName, float waitTime);

// MegaCrit.Sts2.Core.Commands.Builders.AttackCommand 实际调用点（Execute 内）
if (CS$<>8__locals67._attackerAnimName != null && CS$<>8__locals67._shouldPlayAnimation)
{
    val6 = CreatureCmd.TriggerAnim(CS$<>8__locals67._visualAttacker ?? CS$<>8__locals67.Attacker,
                                   CS$<>8__locals67._attackerAnimName,
                                   CS$<>8__locals67._attackerAnimDelay).GetAwaiter();
}
// 受击方：
hitTriggers.Add(TriggerAnim(receiver, "Hit", 0f));
```

`AttackCommand` 里动画名/延迟的设定（这就是「攻击前摇」的时长来源）：
```csharp
public AttackCommand FromCard(CardModel card, CardPlay? cardPlay)
{
    ...
    _attackerAnimName  = "Attack";
    _attackerAnimDelay = owner.Character.AttackAnimDelay;
}
public AttackCommand FromOsty(Creature osty, CardModel card, CardPlay? cardPlay)
{
    _attackerAnimName  = "Attack";
    _attackerAnimDelay = 0.3f;
}
public AttackCommand FromMonster(MonsterModel monster)
{
    Attacker = monster.Creature;
    _attackerAnimName = "Attack";
    _sourceType = SourceType.Monster;
    return TargetingAllOpponents(monster.Creature.CombatState);
}
```
可用的自定义攻击流水线（**Mod 可以直接复用来触发真正的「攻击演出」**）：
```csharp
public AttackCommand WithAttackerAnim(string animName, float delay, Creature? visualAttacker);
public AttackCommand WithNoAttackerAnim();
public AttackCommand AfterAttackerAnim(Func<Task> afterAttackerAnim);
public AttackCommand WithAttackerFx(string? vfx, string? sfx, string? tmpSfx);
public AttackCommand WithAttackerFx(Func<Node2D> createAttackerVfx);
public AttackCommand WithWaitBeforeHit(float fastSeconds, float standardSeconds);
public AttackCommand WithHitFx(string? vfx, string? sfx, string? tmpSfx);
public AttackCommand WithHitCount(int hitCount);
public AttackCommand OnlyPlayAnimOnce();
public Task<AttackCommand> Execute(PlayerChoiceContext choiceContext);
```

**「突刺/前冲」不是引擎位移，而是 Spine 里的 `attack` 动画本身**（代码侧没有 `Tween` 改 `Position` 的攻前冲逻辑；全库搜 `Lunge`/`MeleeMove`/`MoveTo` 均无战斗位移相关命中）。也就是说，Darkest Dungeon 式的「冲上去再回来」需要在 Mod 里自己 `Tween` `NCreature.Position`（**参考 `AnimShake` 的 `TweenMethod` + `Visuals.Position` 写法**，改动 `Visuals.Position` 而非 `NCreature.Position` 不会和布局打架）。`OstyScaleToSize` 里有现成的 `_scaleTween.Parallel().TweenProperty(this, "position", ..., duration)` 写法可作模板：

```csharp
_scaleTween.Parallel().TweenProperty(this, "position", nCreature.Position + GetOstyOffsetFromPlayer(Entity), duration);
```

---

## 4. 伤害数字 / 命中特效

### 4.1 伤害数字：`MegaCrit.Sts2.Core.Nodes.Vfx.NDamageNumVfx`（`Node2D`）

```csharp
public static NDamageNumVfx? Create(Creature target, DamageResult result);
public static NDamageNumVfx? Create(Creature target, int damage, bool requireInteractable = true);
public static NDamageNumVfx? Create(Vector2 globalPosition, int damage);
```

```csharp
public static NDamageNumVfx? Create(Creature target, int damage, bool requireInteractable = true)
{
    if (TestMode.IsOn) return null;
    NCreature nCreature = NCombatRoom.Instance?.GetCreatureNode(target);
    Vector2 globalPosition = Vector2.Zero;
    if (requireInteractable && (nCreature == null || !nCreature.IsInteractable))
    {
        if (!LocalContext.IsMe(target)) return null;
        Vector2 size = ((SceneTree)Engine.GetMainLoop()).Root.GetViewport().GetVisibleRect().Size;
        globalPosition = size * new Vector2(0.25f, 0.5f);
    }
    else if (nCreature != null)
    {
        globalPosition = nCreature.VfxSpawnPosition + _positionOffset
                       + new Vector2(Rng.Chaotic.NextFloat(-10f, 10f), Rng.Chaotic.NextFloat(-5f, 5f));
    }
    return Create(globalPosition, damage);
}

public static NDamageNumVfx? Create(Vector2 globalPosition, int damage)
{
    if (TestMode.IsOn) return null;
    NDamageNumVfx nDamageNumVfx = PreloadManager.Cache.GetScene(_scenePath).Instantiate<NDamageNumVfx>(PackedScene.GenEditState.Disabled);
    nDamageNumVfx._globalSpawnPosition = globalPosition;
    nDamageNumVfx._text = damage.ToString();
    return nDamageNumVfx;
}
```
内部常量与动画（抛物线抛体，`_Process` 自己积分）：
```csharp
private static readonly Vector2 _gravity        = new Vector2(0f, 2000f);
private static readonly Vector2 _positionOffset = new Vector2(0f, -100f);
private static readonly string  _scenePath      = SceneHelper.GetScenePath("vfx/vfx_damage_num");

public override void _Ready()
{
    MegaLabel node = GetNode<MegaLabel>("Label");
    node.SetTextAutoSize(_text);
    base.GlobalPosition = _globalSpawnPosition;
    _velocity = new Vector2(Rng.Chaotic.NextFloat(-100f, 100f), Rng.Chaotic.NextFloat(-800f, -700f));
    node.Scale = Vector2.One * Rng.Chaotic.NextFloat(1.2f, 1.3f);
    base.RotationDegrees = Rng.Chaotic.NextFloat(-5f, 5f);
    TaskHelper.RunSafely(AnimVfx());
}
public override void _Process(double delta)
{
    float num = (float)delta;
    base.Position += _velocity * num;
    _velocity += _gravity * num;
}
```

**挂载点**（`CreatureCmd.Damage` 内，两种等价写法）：
```csharp
Node vfxContainer = receiver.GetVfxContainer();          // == NCombatRoom.Instance.CombatVfxContainer
if (damage > 0 || (modifiedAmount == 0m && current.Receiver == unblockedDamageTarget))
{
    NDamageNumVfx nDamageNumVfx = NDamageNumVfx.Create(receiver, current);
    if (nDamageNumVfx != null)
    {
        if (vfxContainer != null) vfxContainer.AddChildSafely(nDamageNumVfx);
        else                      NRun.Instance.GlobalUi.AddChildSafely(nDamageNumVfx);
    }
}
if (damage > 0)
{
    vfxContainer?.AddChildSafely(NHitSparkVfx.Create(receiver));
    if (receiver != dealer && !props.HasFlag(ValueProp.SkipHurtAnim))
    {
        hitTriggers.Add(TriggerAnim(receiver, "Hit", 0f));
        if (receiver.IsMonster && receiver.Monster.HasHurtSfx) SfxCmd.Play(receiver.Monster.HurtSfx);
    }
}
...
// 格挡：
SfxCmd.Play("event:/sfx/block_hit");
Node vfxContainer2 = unblockedDamageResult.Receiver.GetVfxContainer();
vfxContainer2?.AddChildSafely(NBlockSparkVfx.Create(unblockedDamageResult.Receiver));
vfxContainer2?.AddChildSafely(NDamageBlockedVfx.Create(unblockedDamageResult.Receiver));
NGame.Instance?.ScreenShake(ShakeStrength.Weak, ShakeDuration.Short);
// 治疗：<>8__1.creature.GetVfxContainer()?.AddChildSafely(NHealNumVfx.Create(<creature>, amount));
```

### 4.2 其它命中/演出特效类型

| 类型 | 静态工厂 | 说明 |
|---|---|---|
| `...Vfx.NHitSparkVfx : Node2D` | `Create(Creature target, bool requireInteractable = true)` | 命中火花，自销毁 |
| `...Vfx.NBlockSparkVfx : Node2D` | `Create(Creature target)` | 格挡火花 |
| `...Vfx.NDamageBlockedVfx` | `Create(Creature)` | 「blocked」文字 |
| `...Vfx.NHealNumVfx : Node2D` | `Create(Creature target, decimal amount)` | 治疗数字 |
| `...Vfx.NMonsterDeathVfx` | — | 死亡特效（字段 `List<NCreature> _creatureNodes`） |
| `...Vfx.NAdditiveOverlayVfx : ColorRect` | `Create(VfxColor)` | 全屏染色 overlay，`_Ready` 里 0.5s 淡入到 a=0.1，停留 0.5s，0.5s 淡出后自毁 |
| `...Vfx.NSmokyVignetteVfx : Control` | `Create(Color tint, Color highlightColor)` | 暗角/烟雾边缘（**很适合做 DD 风压暗**） |
| `...Vfx.PlayerHurtVignetteHelper` | `static void Play()` | 玩家受伤红色暗角 |
| `...Vfx.NRadialBlurVfx : BackBufferCopy` | `NCombatRoom.RadialBlur(VfxPosition)` | 径向模糊 |
| `...Vfx.NFullscreenTextVfx` | — | 全屏大字 |
| `...Nodes.Vfx.Utilities.NHitStop` | `NGame.Instance.HitStop.DoHitStop(ShakeStrength, ShakeDuration)` | 命中顿帧 |

`VfxCmd`（`MegaCrit.Sts2.Core.Commands.VfxCmd`）——把任意 `res://scenes/**/*.tscn` 特效播到生物身上：
```csharp
public static void PlayVfx(Vector2 position, string path, Control? vfxContainer);
public static void PlayOnCreature(Creature target, string path);        // position = creatureNode.GlobalPosition
public static void PlayOnCreatureCenter(Creature target, string path);  // position = creatureNode.VfxSpawnPosition
public static void PlayOnCreatureCenters(IEnumerable<Creature> targets, string path);
public static void PlayOnCreatures(IEnumerable<Creature> targets, string path);
public static void PlayOnSide(CombatSide side, string path, ICombatState combatState);
public static void PlayFullScreenInCombat(string path, Creature? spawner);   // 挂到 CombatVfxContainer，居中
public static Node2D PlayNonCombatVfx(Node container, Vector2 position, string path);
public static Vector2? GetSideCenter(CombatSide side, ICombatState combatState);
public static Vector2? GetSideCenterFloor(CombatSide side, ICombatState combatState);
```

---

## 5. 手牌 / 卡牌视觉

### 5.1 手牌：`NPlayerHand`

```csharp
public static NPlayerHand? Instance => NCombatRoom.Instance?.Ui.Hand;
public Control CardHolderContainer { get; set; }
public IReadOnlyList<NHandCardHolder> Holders { get; }
public IReadOnlyList<NHandCardHolder> ActiveHolders { get; }
public NHandCardHolder? FocusedHolder { get; set; }
public NCard? GetCard(CardModel card);
public NCardHolder? GetCardHolder(CardModel card);
public NHandCardHolder Add(NCard card, int index);
public void Remove(CardModel card);
public void AnimIn();  public void AnimOut();
```
卡槽节点：`MegaCrit.Sts2.Core.Nodes.Cards.Holders.NHandCardHolder`（`NCardHolder` 子类），见 `MegaCrit.Sts2.Core.Nodes.Cards.Holders.NCardHolder`。

### 5.2 完整卡牌视觉：`NCard`（`Control, IPoolable`）

**有静态工厂，且是对象池（必须先 `InitPool`）：**

```csharp
// MegaCrit.Sts2.Core.Nodes.Cards.NCard
private const string _scenePath = "res://scenes/cards/card.tscn";
public static readonly Vector2 defaultSize = new Vector2(300f, 422f);

public static void InitPool()
{
    NodePool.Init<NCard>("res://scenes/cards/card.tscn", 30);
}

public static NCard? Create(CardModel card, ModelVisibility visibility = ModelVisibility.Visible)
{
    if (TestMode.IsOn) return null;
    NCard nCard = NodePool.Get<NCard>();
    nCard.Model = card;
    nCard.Visibility = visibility;
    return nCard;
}

public Vector2 GetCurrentSize() => defaultSize * base.Scale;
public void UpdateVisuals(PileType pileType, CardPreviewMode previewMode);
public void ShowUpgradePreview();
public void ActivateRewardScreenGlow();
public void OnReturnedFromPool();  public void OnFreedToPool();
public CardModel Model { get; set; }
public Control Body { get; set; }                 // %CardContainer
public NCardHighlight CardHighlight { get; set; } // %Highlight
public Node OverlayContainer { get; }             // %OverlayContainer
public Node CardVfxContainer { get; }
public TextureRect EnchantmentVfxOverride { get; }
```

`InitPool` 由 `NGame` 在启动时调用：
```csharp
private void InitPools()
{
    NCard.InitPool();
    NGridCardHolder.InitPool();
}
```
对象池实现（`MegaCrit.Sts2.Core.Nodes.Pooling.NodePool`）：
```csharp
public static NodePool<T> Init<T>(string scenePath, int prewarmCount) where T : Node, IPoolable;
public static T Get<T>() where T : Node, IPoolable;   // 未 Init 会 throw InvalidOperationException
public static void Free<T>(T obj) where T : Node, IPoolable;
```
归还方式（`GodotTreeExtensions.QueueFreeSafely` 会**自动识别 `IPoolable` 并回收**）：
```csharp
public static void QueueFreeSafely(this Node node)
{
    if (!GodotObject.IsInstanceValid(node)) return;
    IPoolable poolable = node as IPoolable;
    if (poolable != null)
    {
        node.GetParent()?.RemoveChildSafely(node);
        Callable.From((Action)delegate { NodePool.Free(poolable); }).CallDeferred();
    }
    else node.QueueFreeSafelyNoPool();
}
```

### 5.3 游戏内「显示单张完整卡牌」的官方写法（`NInspectCardScreen`）

场景里有一个现成 `NCard` 子节点，运行时只改 `Model`：
```csharp
private static readonly string _scenePath = SceneHelper.GetScenePath("screens/inspect_card_screen");
private NCard _card;
private Vector2 _cardPosition;

public override void _Ready()
{
    _card = GetNode<NCard>("Card");
    _cardPosition = _card.Position;
    ...
}
private void UpdateCardDisplay()
{
    CardModel cardModel  = _cards[_index];
    CardModel cardModel2 = (CardModel)_cards[_index].MutableClone();
    ...
    _card.Model = cardModel2;
    _card.UpdateVisuals(PileType.None, CardPreviewMode.Normal);
    ...
}
// Open(...) 里通过 tween 放大展示：
_card.Scale = Vector2.One * 1.75f;
_cardTween.TweenProperty(_card, "scale", Vector2.One * 2f, 0.15)
          .SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Spring).SetDelay(0.1);
```

**→ 在角落渲一张完整卡牌的可行路径（两种）：**

1. **对象池路线（推荐）**
   `var n = NCard.Create(cardModel, ModelVisibility.Visible);` → `someControl.AddChildSafely(n);` → `n.Position = …; n.Scale = Vector2.One * 0.6f;` → 结束时 `n.QueueFreeSafely()`（自动 `NodePool.Free`）。
   注意：`NCard._Ready()` 会 `GetNode<MegaLabel>("%TitleLabel")` 等一堆 `%` 节点，**这些在 `card.tscn` 的实例里已存在**，所以 Create 出来是完整视觉；`InitPool` 已在游戏启动时执行过，Mod 侧不必重复调用（重复 `Init` 会抛异常）。
2. **克隆路线（更保险，避免污染游戏对象池）**
   从任意已存在的 `NCard`（例如 `NCombatRoom.Instance.Ui.Hand.GetCard(model)` 或手牌 holder 的 `CardNode`）`.Duplicate()`，或 `NCard.Create` 后**不归还池**而是 `QueueFreeSafelyNoPool()`。

`NCard` 视觉子节点唯一名（`_Ready` 摘录）：`%TitleLabel %DescriptionLabel %Frame %Portrait %AncientPortrait %TypeLabel %PortraitBorder %PortraitCanvasGroup %EnergyLabel %EnergyIcon %StarLabel %StarIcon %TitleBanner %Lock %TypePlaque %UnplayableEnergyIcon %UnplayableStarIcon %Enchantment/Icon %Enchantment/Label %Highlight %CardContainer`，另有 `CardContainer/CardSparkles`。

`NCardHighlight : TextureRect`：`AnimShow() / AnimHide() / AnimHideInstantly() / AnimFlash()`，静态色 `playableColor / gold / red`。

---

## 6. 相机

### 6.1 结论：**游戏里没有 `Camera2D`（也没有任何 `Camera` 节点）**

证据（对整库成员名做正则搜索，成员名不区分大小写）：

```
=== search "cam" (覆盖 Camera/camera) ===
// --- MegaCrit.Sts2.Core.Models.EncounterModel
  M public virtual System.Single GetCameraScaling()
  M public virtual Godot.Vector2 GetCameraOffset()
// --- 以及 6 个 Encounter 子类覆写
（无任何 Godot.Camera2D / Camera3D 字段或属性）
```
`& $dotnet $cli types $asm "Camera"` 返回 **空**。
（`Godot.Camera2D` 在 `GodotSharp.dll` 里当然是有的：`Godot.Camera2D`、`Godot.CanvasLayer` 均可 `types` 命中 —— 只是 sts2.dll 从未使用。）

### 6.2 真正的「相机」= 一个 `Control` 的 transform

`NCombatRoom._Ready()`：
```csharp
SceneContainer = GetNode<Control>("%CombatSceneContainer");
...
SceneContainer.Scale = Vector2.One * _visuals.Encounter.GetCameraScaling();
SceneContainer.Position += _visuals.Encounter.GetCameraOffset();
SceneContainer.ZIndex = -10;
CombatVfxContainer.ZIndex = -9;
_window = GetTree().Root;
_window.Connect(Viewport.SignalName.SizeChanged, Callable.From(new Action(AdjustCreatureScaleForAspectRatio)));
AdjustCreatureScaleForAspectRatio();
NGame.Instance.SetScreenShakeTarget(SceneContainer);   // 屏幕震动目标就是这个"相机"
```

```csharp
// 每条遭遇可自定义
public virtual float GetCameraScaling();      // EncounterModel，默认 1
public virtual Godot.Vector2 GetCameraOffset();
```
`EncounterModel.GetCameraScaling/GetCameraOffset` 有覆写的遭遇：
`AeonglassBoss`, `BygoneEffigyElite`, `CeremonialBeastBoss`, `DecimillipedeElite`, `FabricatorNormal`, `KaiserCrabBoss`, `KnightsElite`。

### 6.3 是否已有「focus / zoom to target」效果？

**没有。** 全局搜索 `FocusOn` / `CenterOn` / `PanTo` / `Zoom`：
- `FocusOn` → 只有 `ActiveScreenContext.FocusOnDefaultControl()`（UI 焦点，非镜头）与 `NCustomRunScreen.TryFocusOnModifiersList()`。
- `CenterOn` / `PanTo` / `Zoom`（除 `SkulkingColony.ZoomMove`、`ZoomDamage` 这种技能名）→ 无。
- `NSelectionReticle`/`NTargetManager` 只做高亮框和选择，不动镜头。

### 6.4 可用的镜头类效果（现成）

```csharp
// MegaCrit.Sts2.Core.Nodes.NGame
public void SetScreenShakeTarget(Control target);
public void ClearScreenShakeTarget();
public void ScreenShake(ShakeStrength strength, ShakeDuration duration, float degAngle = -1f);
public void ScreenShakeTrauma(ShakeStrength strength);
public void SetScreenshakeMultiplier(float multiplier);
public void DoHitStop(ShakeStrength strength, ShakeDuration duration);
public Control ScreenshakeTarget { get; }
public static NGame? Instance { get; private set; }
```
`_screenShake` 是 `NGame` 场景里的 `ScreenShake` 节点（`NScreenShake`），`HitStop` 是 `HitStop` 节点（`NHitStop`）。

**→ 做「缩放/平移到目标」的 Mod 实现路径：直接对 `NCombatRoom.Instance.SceneContainer` 做 `CreateTween().TweenProperty(sceneContainer, "scale", ...)` / `"position"`。**
注意两点：
1. `SceneContainer.Position` 在 `_Ready` 已被 `GetCameraOffset()` 偏移，且 `AdjustCreatureScaleForAspectRatio()` 会改写 `_allyContainer/_enemyContainer` 的 `Scale`（**不碰 `SceneContainer.Scale`**），所以对 `SceneContainer` 做 tween 相对安全 —— 但 `SceneContainer.Scale` 一旦被 Mod 改动，`OstyScaleToSize` 里的 `Entity.CombatState.Encounter.GetCameraScaling()` 分支会与之叠加（见 `NCreature.OstyScaleToSize`）。
2. **不要把 `NGame.RootSceneContainer` 当战斗镜头**：它承载整个 Run（含所有房间）。`NGame.RootSceneContainer` 是 `NSceneContainer : Control`，只有一个 `Control CurrentScene { get; set; }` 和 `void SetCurrentScene(Control)`。

若确实需要独立节点：`Godot.Camera2D` / `Godot.CanvasLayer` 都在 `GodotSharp.dll` 里可用（已用同一 CLI 确认存在）。

---

## 7. `NOverlayStack` / `IOverlayScreen`（来自实际程序集）

```csharp
// ===== MegaCrit.Sts2.Core.Nodes.Screens.Overlays.IOverlayScreen =====
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;

namespace MegaCrit.Sts2.Core.Nodes.Screens.Overlays;

public interface IOverlayScreen : IScreenContext
{
	NetScreenType ScreenType { get; }
	bool UseSharedBackstop { get; }
	void AfterOverlayOpened();
	void AfterOverlayClosed();
	void AfterOverlayShown();
	void AfterOverlayHidden();
}
```
```csharp
// ===== MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext.IScreenContext =====
public interface IScreenContext
{
	Control? DefaultFocusedControl { get; }
	Control? FocusedControlFromTopBar => DefaultFocusedControl;   // 默认实现
}
```

```csharp
// ===== MegaCrit.Sts2.Core.Nodes.Screens.Overlays.NOverlayStack (base Control) =====
[Signal] public delegate void ChangedEventHandler();

public static NOverlayStack? Instance => NRun.Instance?.GlobalUi.Overlays;
public int ScreenCount => _overlays.Count;

public void Push(IOverlayScreen screen);
public void Remove(IOverlayScreen screen);
public IOverlayScreen? Peek();
public void Clear();
public void HideOverlays();
public void ShowOverlays();
public void ShowBackstop();
public void HideBackstop();
public event ChangedEventHandler Changed;
private Control _backstop;   // 节点名固定为 "OverlayBackstop"
```

`Push` 的实现要点（原样）：
```csharp
public void Push(IOverlayScreen screen)
{
    Peek()?.AfterOverlayHidden();
    this.AddChildSafely((Node)screen);
    _overlays.Add(screen);
    screen.AfterOverlayOpened();
    screen.AfterOverlayShown();
    _backstop.MouseFilter = MouseFilterEnum.Stop;
    _backstopFade?.Kill();
    this.MoveChildSafely(_backstop, _overlays.IndexOf(screen));
    bool stackIsCovered = StackIsCovered;
    if (stackIsCovered) screen.AfterOverlayHidden();
    if (!screen.UseSharedBackstop || stackIsCovered) _backstop.Modulate = Colors.Transparent;
    else if (ScreenCount == 1) ShowBackstop();
    else _backstop.Modulate = Colors.White;
    ActiveScreenContext.Instance.Update();
    EmitSignal(SignalName.Changed);
}
```
`_Ready` 里 `_backstop = GetNode<Control>("OverlayBackstop"); _backstop.Modulate = Colors.Transparent; _backstop.MouseFilter = MouseFilterEnum.Ignore;`
`_ExitTree` 会 `Clear()`。

**注意**：`Push` 的形参是**接口** `IOverlayScreen`，而 `.AddChildSafely((Node)screen)` 强制要求实现类同时是 `Node`（`Control`）。全部实现者都满足。已知实现：`NChooseARelicSelection`, `NRewardsScreen`, `NGameOverScreen`, `NCardGridSelectionScreen`(virtual), `NCardRewardSelectionScreen`, `NChooseABundleSelectionScreen`, `NChooseACardSelectionScreen` 等。

**→ Mod 做「演出覆盖层」时应实现 `class NMyCinematic : Control, IOverlayScreen`，然后 `NOverlayStack.Instance.Push(this)` / `Remove(this)`。** `UseSharedBackstop = true` 可白嫖一个 0.5s 淡入的半透明背板。

---

## 8. 运行时拿到「当前战斗房间节点」

三层链，全部是静态单例，直接可用：

```csharp
// 1) 战斗房间（唯一推荐入口）
public static NCombatRoom? Instance => NRun.Instance?.CombatRoom;      // NCombatRoom

// 2) Run 节点
public static NRun? Instance => NGame.Instance?.CurrentRunNode;        // NRun
public NCombatRoom? CombatRoom
{
    get
    {
        Control currentScene = _roomContainer.CurrentScene;            // _roomContainer = GetNode<NSceneContainer>("%RoomContainer")
        if (!(currentScene is NCombatRoom result))
        {
            if (currentScene is NEventRoom nEventRoom) return nEventRoom.EmbeddedCombatRoom;
            ...
        }
        return result;                                                  // （原样：分支合并）
    }
}

// 3) 游戏根
public static NGame? Instance { get; private set; }                     // NGame._Ready() 里 Instance = this;  _ExitTree 里 Instance = null;
public NRun? CurrentRunNode => RootSceneContainer.CurrentScene as NRun;
```

其它同级便捷入口：
```csharp
public static NPlayerHand?     Instance => NCombatRoom.Instance?.Ui.Hand;              // NPlayerHand
public static NTargetManager?  Instance => NRun.Instance.GlobalUi.TargetManager;       // NTargetManager
public static NOverlayStack?   Instance => NRun.Instance?.GlobalUi.Overlays;           // NOverlayStack
public static NBestiary?       Instance;                                               // 图鉴（非战斗）
```
`NCombatRoom` 里现成的广播：`public event Action ProceedButtonPressed;`，以及 `public ulong CreatedMsec { get; }`（战斗房创建时刻，可判断动画是否刚开场）。

场景树文字描述：
```
NGame (Control, autoload-ish root; %RootSceneContainer, %GlobalUi, ScreenShake, HitStop, …)
└─ NSceneContainer %RootSceneContainer
   └─ NRun  (scene res://scenes/run.tscn)
      ├─ NSceneContainer %RoomContainer      ← CurrentScene = NCombatRoom
      │  └─ NCombatRoom  (res://scenes/rooms/combat_room.tscn)
      │     ├─ Control %CombatSceneContainer   ← "相机"（Scale/Position）
      │     │  ├─ Control %AllyContainer
      │     │  └─ Control %EnemyContainer  ──> NCreature×N  (GetNode<NCreature>, Hitbox, %Intents, %HealthBar, %SelectionReticle, Visuals)
      │     ├─ Control %BgContainer
      │     ├─ Control %BackCombatVfxContainer
      │     ├─ Control %CombatVfxContainer     ← 伤害数字/命中火花挂这里
      │     ├─ NRadialBlurVfx "RadialBlur"
      │     ├─ NCombatUi %CombatUi  (… %Hand, %PlayContainer, %PlayQueue, %CardPreviewContainer …)
      │     ├─ %WaitingForOtherPlayers / %ProceedButton
      │     └─ NPlayerTurnBanner / NEnemyTurnBanner / NCombatStartBanner  ← 运行时 AddChildSafely 到 NCombatRoom
      └─ NGlobalUi %GlobalUi
         ├─ NTopBar %TopBar
         ├─ NOverlayStack %OverlayScreensContainer
         ├─ NCapstoneContainer %CapstoneScreenContainer
         ├─ NMapScreen %MapScreen
         ├─ NTargetManager "TargetManager"
         └─ %AboveTopBarVfxContainer, %CardPreviewContainer, %RelicInventory, %MultiplayerPlayerContainer …
```

---

## 9. 敌人意图显示节点 `NIntent`

**存在。** `MegaCrit.Sts2.Core.Nodes.Combat.NIntent : Control`，场景 `res://scenes/combat/intent.tscn`。

### 9.1 挂载位置

挂在 `NCreature` 的 `%Intents`（`public Control IntentContainer`）下：

```csharp
// NCreature._Ready()
IntentContainer = GetNode<Control>("%Intents");
...
// 多人模式玩家意图（另一个类）：
IntentContainer.AddChildSafely(PlayerIntentHandler);
IntentContainer.Modulate = Colors.White;
```
```csharp
// NCreature.UpdateIntent(IEnumerable<Creature> targets) —— 敌人意图的刷新逻辑
public Task UpdateIntent(IEnumerable<Creature> targets)
{
    if (Entity.Monster == null) throw new InvalidOperationException("Only valid on monsters.");
    IReadOnlyList<AbstractIntent> intents = Entity.Monster.NextMove.Intents;
    int i;
    for (i = 0; i < intents.Count && i < IntentContainer.GetChildCount(); i++)
    {
        NIntent child = IntentContainer.GetChild<NIntent>(i);
        child.SetFrozen(isFrozen: false);
        child.UpdateIntent(intents[i], targets, Entity);
    }
    float num = (float)this.GetHashCode() * 0.01f;
    for (; i < intents.Count; i++)
    {
        NIntent nIntent = NIntent.Create(num + (float)i * 0.3f);   // 每个意图错开 bob 相位
        IntentContainer.AddChildSafely(nIntent);
        nIntent.UpdateIntent(intents[i], targets, Entity);
    }
    // 多余的移除
    List<Node> val = Enumerable.ToList<Node>(Enumerable.TakeLast<Node>(IntentContainer.GetChildren(), IntentContainer.GetChildCount() - i));
    foreach (Node current in val) { IntentContainer.RemoveChildSafely(current); current.QueueFreeSafely(); }
    return Task.CompletedTask;
}
```

### 9.2 定位

位置在 `NCreature.UpdateBounds` 里每帧根据视觉包围盒的 `IntentPos` Marker 重算（**注意容器本身会被 Translate，子 `NIntent` 自己还有上下浮动**）：

```csharp
IntentContainer.Position = boundsContainer.GetNode<Marker2D>("IntentPos").Position - IntentContainer.Size * 0.5f;
IntentContainer.Position = new Vector2(IntentContainer.Position.X, IntentContainer.Position.Y * Visuals.Scale.X);
```
```csharp
// Marker 来源
IntentPosition = GetNode<Marker2D>("%IntentPos");   // NCreatureVisuals._Ready()
```
`NIntent` 自身每帧 bob：
```csharp
public override void _Process(double delta)
{
    _intentHolder.Position = Vector2.Up * (Mathf.Sin((float)Time.GetTicksMsec() * 0.001f * (float)Math.PI + _timeOffset) * 10f + 8f);
    ...
}
```

### 9.3 API

```csharp
public static NIntent? Create(float startTime);
public void UpdateIntent(AbstractIntent intent, IEnumerable<Creature> targets, Creature owner);
public void SetFrozen(bool isFrozen);      // 死亡动画期间冻结刷新
public void PlayPerform();                 // 触发粒子（敌人执行意图时）
public IEnumerable<string> AssetPaths { get; }   // "res://scenes/combat/intent.tscn" + IntentAnimData.AssetPaths
```
内部节点：`%IntentHolder`(Control), `%Intent`(Sprite2D), `%Value`(MegaRichTextLabel), `%IntentParticle`(CpuParticles2D)。
`_EnterTree` 里会 `Connect(Control.SignalName.MouseEntered/MouseExited, …)` 做 hover tip，并注册 `NCombatRoom.Instance.Ui.DebugToggleIntent`。

**隐藏意图的方法**：`NCombatUi.IsDebugHidingIntent` 是 **static 但 setter 为 private**，Mod 只能读不能写（游戏里只有 debug 热键能改）：

```csharp
// ===== NCombatUi =====
public static bool IsDebugHidingIntent { get; private set; }
public static bool IsDebugHidingHpBar  { get; private set; }
public static bool IsDebugHideTextVfx  { get; private set; }
public static bool IsDebugHideTargetingUi { get; private set; }
public static bool IsDebugHideMpTargetingUi { get; private set; }
public static bool IsDebugHideMpIntents { get; private set; }
// 唯一写点（debug 热键处理）：
IsDebugHidingIntent = !IsDebugHidingIntent;
NGame.Instance.AddChildSafely(NFullscreenTextVfx.Create(IsDebugHidingIntent ? "Hide Intents" : "Show Intents"));
```

因此 Mod 隐藏意图应改用：

```csharp
n.AnimHideIntent(delay: 0.0);                                        // 0.5s 淡出 IntentContainer
// 或直接控制容器（IntentContainer 是 public 属性）
n.IntentContainer.Visible = false;
n.IntentContainer.Modulate = new Color(1, 1, 1, 0.3f);
```

`AnimHideIntent` 完整实现：
```csharp
public void AnimHideIntent(double delay = 0.0)
{
    _intentFadeTween?.Kill();
    _intentFadeTween = CreateTween().SetParallel();
    PropertyTweener propertyTweener = _intentFadeTween.TweenProperty(IntentContainer, "modulate:a", 0f, 0.5);
    if (delay > 0.0) propertyTweener.SetDelay(delay);
}
```

---

## 10. 针对 Mod 需求的结论性配方

### 10.1 拿到每个生物的屏幕节点与矩形

```csharp
// 全体
foreach (var node in NCombatRoom.Instance!.CreatureNodes) { /* node.Entity, node.Hitbox */ }
// 单个
NCreature? n = creature.GetCreatureNode();
Vector2 topLeft   = n.Hitbox.GlobalPosition;
Vector2 size      = n.Hitbox.Size;
Vector2 centerTop = n.GetTopOfHitbox();
Vector2 centerBot = n.GetBottomOfHitbox();
Vector2 vfxCenter = n.VfxSpawnPosition;         // == Visuals.VfxSpawnPosition.GlobalPosition
```
`Hitbox` 由 `UpdateBounds` 在每次 Spine 动画切换/缩放时同步，是**唯一可靠的屏幕包围盒**。
注意：节点坐标处于 `%CombatSceneContainer` 的 `Scale/Position`（"相机"）之下，`Hitbox.GlobalPosition` 是**已含相机变换的屏幕坐标**；若 Mod 的 HUD 挂在 `NCombatRoom` 自身或 `NGlobalUi` 下，直接用 `GlobalPosition` 正确，无需再乘相机矩阵。

### 10.2 遮挡 / 淡出 / 隐藏任意生物（全部已验证的可行手段）

| 效果 | 手段 | 出处/依据 |
|---|---|---|
| 整体隐藏 | `n.Visible = false`（`CanvasItem.Visible`，GodotSharp 已确认 `P System.Boolean Visible { get; set; }`） | `Control` 继承自 `CanvasItem` |
| 整体变暗 | `n.Modulate = new Color(0.35f, 0.35f, 0.35f);` | `CanvasItem.Modulate` |
| **只暗视觉、不影响 UI** | `n.Visuals.Modulate = new Color(0.5f, 0.5f, 0.5f);` | **游戏本体就这么干**：`NCombatRoom.AddCreature` 末尾 `if (creatureNode.Position.Y < 199f) nCreature.Visuals.Modulate = new Color(0.5f, 0.5f, 0.5f);` |
| 只暗血条 | `n.AnimDisableUi()` / `n.AnimEnableUi()`（返回 `Tween`，0.5s 内 `modulate:a` 到 0/1）；或 `GetNode<NCreatureStateDisplay>("%HealthBar").AnimateOut()/AnimateIn(mode)` | `NCreature.AnimDisableUi/AnimEnableUi`、`NCreatureStateDisplay.AnimateOut/AnimateIn` |
| 只暗意图 | `n.AnimHideIntent(delay)` | 见 §3.1 / §9.3 |
| 淡出（渐隐） | `CreateTween().TweenProperty(n, "modulate:a", 0f, 0.4)`；`n.CreateTween()` 亦可 | `Node.CreateTween()`，游戏里大量使用 |
| 死亡式消失 | `n.StartDeathAnim(shouldRemove: true)`（返回等待时长） | §3.1 |
| 全屏压暗（DD 氛围） | `NAdditiveOverlayVfx.Create(VfxColor.Black)`（自动 0.5s 淡入 a=0.1 → 0.5s 停留 → 0.5s 淡出 + 自毁）；或 `NSmokyVignetteVfx.Create(tint, highlight)` | §4.2 |
| 命中顿帧 | `NGame.Instance.HitStop.DoHitStop(ShakeStrength.Strong, ShakeDuration.Short)` | §6.4 |
| 镜头震动 | `NGame.Instance.ScreenShake(ShakeStrength.Strong, ShakeDuration.Normal)` | §6.4 |

调色板（官方配色，做压暗/横幅描边可直接用）：
```csharp
// ===== MegaCrit.Sts2.Core.Helpers.StsColors (base Object) =====  —— 全部为 static readonly Godot.Color
aqua, cream, halfTransparentCream, gold, red, green, blue, darkBlue, orange, pink, purple,
blueGlow, redGlow, disabledTextForPotionPopup,
screenBackdrop, transparentBlack, halfTransparentBlack, ninetyPercentBlack, transparentWhite
// 游戏本体用法举例：
base.Modulate = StsColors.transparentWhite;    // NCommonBanner._Ready()
_card.Modulate = StsColors.transparentBlack;   // NInspectCardScreen.Open()
```

### 10.3 高亮矩形（围绕选中的一组生物）
**首选：复用游戏自带的 `NSelectionReticle`**（就是一个跟着 `Hitbox` 走的方框）：
```csharp
n.ShowSingleSelectReticle();   // -> _selectionReticle.OnSelect();  内部 0.2s 淡入 + scale 0.9->1 Expo
n.HideSingleSelectReticle();   // -> OnDeselect()（若 _isInMultiselect 则不生效）
n.ShowMultiselectReticle(); n.HideMultiselectReticle();
```
`NSelectionReticle.OnSelect()` 完整实现：
```csharp
public void OnSelect()
{
    if (!NCombatUi.IsDebugHideTargetingUi)
    {
        _currentTween?.Kill();
        _currentTween = CreateTween().SetParallel();
        _currentTween.TweenProperty(this, "modulate:a", 1f, 0.2f);
        _currentTween.TweenProperty(this, "scale", Vector2.One, 0.5).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Expo).From(Vector2.One * 0.9f);
        base.Modulate = Colors.White;
        base.Scale = Vector2.One;
        IsSelected = true;
    }
}
```
若需要自定义外观（DD 风的红框/描边），则按 §10.1 读 `Hitbox.GlobalPosition/Size`，把自建的 `ColorRect`/`NinePatchRect`/`TextureRect` 挂到 `NCombatRoom.Instance`（或 `NCombatRoom.Instance.CombatVfxContainer`），每帧或 `Hitbox` 变化时同步 `GlobalPosition/Size`。

### 10.4 顶部角落 HUD 横幅

**官方模式**：横幅实例被 `AddChildSafely` 到 `NCombatRoom.Instance` 上（整屏 Control），自身 `_Ready` 里 tween `modulate:a`：
```csharp
// CombatManager 内（Tcombatmanager.txt，三处）
NCombatRoom.Instance?.AddChildSafely(NCombatStartBanner.Create());
...
NCombatRoom.Instance?.AddChildSafely(NPlayerTurnBanner.Create(num2));
...
NCombatRoom.Instance?.AddChildSafely(NEnemyTurnBanner.Create());
```
```csharp
// NPlayerTurnBanner（自带场景 combat/player_turn_banner，Label + TurnNumber 两个 MegaLabel）
public static NPlayerTurnBanner? Create(int roundNumber)
{
    if (TestMode.IsOn) return null;
    if (NCombatUi.IsDebugHideTextVfx) return null;
    NPlayerTurnBanner nPlayerTurnBanner = PreloadManager.Cache.GetScene(_scenePath).Instantiate<NPlayerTurnBanner>(PackedScene.GenEditState.Disabled);
    nPlayerTurnBanner._roundNumber = roundNumber;
    return nPlayerTurnBanner;
}
// 显示/隐藏（Display 状态机）：
Tween tween = nPlayerTurnBanner.CreateTween();
tween.SetParallel();
tween.TweenProperty(nPlayerTurnBanner, "modulate:a", 1f, 1.0).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Expo);
tween.TweenProperty(nPlayerTurnBanner._label,     "position", nPlayerTurnBanner._label.Position + new Vector2(0f, -50f), 1.5)…;
tween.TweenProperty(nPlayerTurnBanner._turnLabel, "position", nPlayerTurnBanner._turnLabel.Position + new Vector2(0f,  50f), 1.5)…;
… tween.TweenProperty(nPlayerTurnBanner, "modulate:a", 0f, 0.3f)…
nPlayerTurnBanner.QueueFreeSafely();
```

**→ Mod 自己的横幅做法**：
1. 自建 `Control`（或 `ColorRect`/`NinePatchRect` + `MegaLabel`/`MegaRichTextLabel`），`AddChildSafely` 到 `NCombatRoom.Instance`（战斗内层级最高、不在相机缩放之下）。
   文本控件用 `MegaCrit.Sts2.addons.mega_text.MegaLabel` / `MegaCrit.Sts2.addons.mega_text.MegaRichTextLabel`（游戏内所有文本都是它们，`MegaLabel` 支持 `SetTextAutoSize(string)` 自适应字号；`NCommonBanner.ChangeText(string)` 就是内部调 `%Label.SetTextAutoSize(text)`）。
2. 左上/右上定位：屏幕尺寸来自
   ```csharp
   Vector2 size = ((SceneTree)Engine.GetMainLoop()).Root.GetViewport().GetVisibleRect().Size;   // 游戏本体用法（NDamageNumVfx）
   // 或 NGame.Instance.GetViewportRect().Size（VfxCmd.PlayFullScreenInCombat 用法）
   ```
   然后 `banner.Position = new Vector2(24, 24)`（左上）或 `new Vector2(size.X - banner.Size.X - 24, 24)`（右上）。
   若想贴合原版 top bar，可用 `NCombatRoom.Instance.Ui` / `NRun.Instance.GlobalUi.TopBar`（`NTopBar`，有 `AnimHide()/AnimShow()`）的 `Size.Y` 作为下界；`NGlobalUi.AboveTopBarVfxContainer` 是原版给「顶栏之上」留的容器。
3. 动画：`banner.CreateTween()` + `TweenProperty(this, "modulate:a", …)`，与 §10.4 原版一致。
4. 全屏演出可考虑 `NOverlayStack.Instance.Push(myOverlay)`（§7）；纯 HUD 横幅不必上 overlay（`Push` 会把屏幕焦点/背板接管掉）。

### 10.5 角落渲染完整卡牌视觉

```csharp
NCard? n = NCard.Create(cardModel, ModelVisibility.Visible);   // 对象池，InitPool 已由 NGame 调用
if (n != null)
{
    someControl.AddChildSafely(n);              // 例如 NCombatRoom.Instance 或自建 CanvasLayer 下的 Control
    n.Position = new Vector2(viewportSize.X - NCard.defaultSize.X * 0.6f - 24, 24);
    n.Scale    = Vector2.One * 0.6f;            // defaultSize = (300, 422) → 0.6 后 ≈ 180×253
    n.Modulate = Colors.White;
    // 缺省 Model 设置后由 NCard 内部 Reload()/UpdateVisuals 刷新；如需强制：
    n.UpdateVisuals(PileType.None, CardPreviewMode.Normal);
}
// 用完必须归还（否则泄漏对象池条目）：
n.QueueFreeSafely();     // 自动 NodePool.Free（IPoolable）
```
若担心与游戏共用对象池，改为 `PreloadManager.Cache.GetScene("res://scenes/cards/card.tscn").Instantiate<NCard>(PackedScene.GenEditState.Disabled)` 再自设 `Model`（**注意 `OnInstantiated`/`_Ready` 依赖 `%` 唯一名节点，`card.tscn` 实例内全部具备**；且**不归还池**）。

### 10.6 缩放 / 平移镜头（可选）

```csharp
var cam = NCombatRoom.Instance!.SceneContainer;   // Control
var t = cam.CreateTween().SetParallel();
t.TweenProperty(cam, "scale",    Vector2.One * 1.35f, 0.35).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Expo);
t.TweenProperty(cam, "position", focusOffset,          0.35).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Expo);
```
配合 `NGame.Instance.HitStop.DoHitStop(...)` / `ScreenShake(...)` 做 DD 风的冲击感；
演出结束时把 `scale/position` tween 回 `_Ready` 时的原值（**记得缓存原值**，因为 `Position` 被 `GetCameraOffset()` 改过）。

---

## 11. 明确的负面结论（避免走弯路）

1. **没有** `MegaCrit.Sts2.Core.Nodes.NCombat` / `NPlayer` / `NEnemy` / `NDamageNumber` 这些类。
2. **没有** `Camera2D`/`Camera3D` 节点，也没有任何镜头 API；"相机"是 `Control`（`NCombatRoom.SceneContainer`）的 `Scale/Position`。**没有**现成的 focus/zoom-to-target 效果。
3. **没有** `NCreature.AttackAsync` / `PlayAttackAnimation`；攻击动画经 `CreatureCmd.TriggerAnim(creature,"Attack",delay)`（由 `AttackCommand` 内部调用）触发，实际前冲位移在 Spine 动画里，**不**是代码位移。代码位移只有 `AnimShake` 与 `OstyScaleToSize` 里的 position tween 可作模板。
4. `NCombatRoom._allyContainer` / `_enemyContainer` / `_visuals` 是 **private**，Mod 需用 `%AllyContainer` / `%EnemyContainer` 唯一名或 `CreatureNodes`。
5. `NOverlayStack.Push` 需要 `Node` + `IOverlayScreen` 双重身份，且会接管 `_backstop`、`ActiveScreenContext`、屏幕焦点 —— 纯 HUD 用 `AddChildSafely(NCombatRoom.Instance)` 更轻。
6. `NCard` 是 `IPoolable`：**不能**裸 `QueueFree()`（会绕过池）；用 `QueueFreeSafely()` 或 `QueueFreeSafelyNoPool()`。另外 `NodePool.Init<T>` 重复调用会抛 `InvalidOperationException`，Mod 不要自己再 Init。
7. `NDamageNumVfx` / `NHitSparkVfx` / `NBlockSparkVfx` / `NHealNumVfx` / `NAdditiveOverlayVfx` 等在 `TestMode.IsOn` 下全部返回 `null` —— 写兼容代码时要注意判空。
8. `NCreature.Create(...)`、`NCombatRoom.Create(...)`、`NCard.Create(...)` 等工厂在 `TestMode.IsOn` 下返回 `null`。
9. `NCombatUi.IsDebugHidingIntent` 等 6 个 debug 开关是 **`public static` getter + `private set`**，Mod **不能**用它们来隐藏意图/血条；请用 `NCreature.AnimHideIntent()` / `AnimDisableUi()` / `IntentContainer.Visible` 等。
10. `NCombatRoom._allyContainer` / `_enemyContainer` 无 public 访问器；`NCreature._stateDisplay` / `_selectionReticle` / `_spineAnimator` 亦为 private（用 `GetNode<NCreatureStateDisplay>("%HealthBar")` 等唯一名替代）。

---

## 12. 原始证据文件索引（`docs/plans/_recon/`）

| 文件 | 内容 |
|---|---|
| `nodes-all.txt` | `MegaCrit.Sts2.Core.Nodes.*` 类型清单（截断 2000 行） |
| `combat-types.txt` / `cards-types.txt` / `rooms-types.txt` / `overlays-types.txt` | 各命名空间精确类名 |
| `camera-types.txt` / `search-camera.txt` / `search-cam-lower.txt` | 相机相关搜索（camera-types 为空 = 无相机类） |
| `TMegaCrit__Sts2__Core__Nodes__Rooms__NCombatRoom.txt` | NCombatRoom 整类（含 `Instance`/`Create`/节点树/定位逻辑） |
| `TMegaCrit__Sts2__Core__Nodes__Combat__NCreature.txt` | NCreature + NCreatureStateDisplay 整类（含全部动画方法、`UpdateBounds`） |
| `TMegaCrit__Sts2__Core__Nodes__Combat__NCreatureVisuals.txt` | NCreatureVisuals 整类 |
| `TMegaCrit__Sts2__Core__Nodes__Combat__NCombatUi.txt` | NCombatUi 整类（含 `%` 节点树） |
| `TMegaCrit__Sts2__Core__Nodes__Combat__NPlayerHand.txt` | NPlayerHand 整类 |
| `TMegaCrit__Sts2__Core__Nodes__Combat__NIntent.txt` | NIntent 整类 |
| `TMegaCrit__Sts2__Core__Nodes__Combat__NHealthBar.txt` | NHealthBar 整类 |
| `TMegaCrit__Sts2__Core__Nodes__Combat__NSelectionReticle.txt` | NSelectionReticle 整类 |
| `TMegaCrit__Sts2__Core__Nodes__Combat__NTargetManager.txt` | NTargetManager 整类 |
| `TMegaCrit__Sts2__Core__Nodes__Cards__NCard.txt` | NCard（含 NCardGrid/NCardHighlight 等同类文件内容） |
| `TMegaCrit__Sts2__Core__Nodes__Screens__Overlays__NOverlayStack.txt` / `...IOverlayScreen.txt` | Overlay API |
| `TMegaCrit__Sts2__Core__Nodes__Vfx__NDamageNumVfx.txt` | 伤害数字整类 |
| `Tanimator.txt` / `Tanimstate.txt` | `CreatureAnimator` / `AnimState`（触发器常量） |
| `Tattackcommand.txt` / `Tattackcontext.txt` | `AttackCommand` / `AttackContext` |
| `Tcreaturecmd.txt` | `CreatureCmd`（含伤害数字/火花挂载点） |
| `Tcombatmanager.txt` | `CombatManager`（含三处 Banner 挂载点） |
| `Tngame.txt` / `NRun-members.txt` / `Tglobalui.txt` | 根节点/单例链/全局 UI 树 |
| `Tplayerbanner.txt` / `Tcommonbanner.txt` | 横幅类实现 |
