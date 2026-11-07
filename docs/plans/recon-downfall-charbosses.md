# Downfall `charbosses` 架构侦察报告（移植清单级）

> 目标：读懂 Slay the Spire 1 的 **Downfall（崩坠）** mod 如何把「原版可玩角色」做成会自己打牌的 Boss，
> 为「在 StS2 里做同样的事」产出一份可执行的移植清单。
>
> 本报告**只做侦察**，不含任何 mod 代码。所有结论都给出类名 + 方法签名 + 关键字节码/字符串常量证据。
>
> 分析对象：`D:\SteamLibrary\steamapps\workshop\content\646570\1610056683\Downfall.jar`（482 MB，4075 个 `.class`，其中 `charbosses/**` 780 个）
> 工具：JDK 17 `javap -p -c -l -constants`（`-l` 的局部变量表让变量名可读）
> 原始转储：`.recon/downfall/dcb-*.txt`（命名规则见文末附录）

---

## 0. 一句话结论（先看这个）

**Downfall 的 charbosses 不是「给怪物一副牌组」，而是一个"伪玩家"运行时。**

它把 `AbstractMonster` 子类改造成拥有玩家全部状态机（能量 / 手牌 / 姿态 / 充能球 / 遗物 / 卡牌结算管线），
但**没有抽牌堆和弃牌堆**：每回合的"手牌"由 archetype 脚本 `getThisTurnCards()` 按 `turn` 序号**硬编码返回一个固定卡牌列表**，
Boss 再按脚本顺序"贪心 + 首个可支付"地一张张打出去。

```
玩家点结束回合
      └─> 游戏调用 monster.takeTurn()
              ├─ startTurn()                      // 重置每回合计数、遗物 atTurnStart
              ├─ addToBot(CharbossMakePlayAction) // 用 StS1 action queue 驱动"打牌"
              ▼
        makePlay()  ←──────────────────────────┐
          遍历 hand（已按 newPrio 排序 ≈ 脚本顺序）
          找第一张 canUse() && getPriority() > -100 的牌
          ├─ 有 → useCard(card, this, 当前能量)   │
          │        addToBot(DelayedActionAction(CharbossDoNextCardAction)) ─┘  // 递归打下一张
          └─ 无 → addToBot(EnemyTriggerEndOfTurnOrbActions)   // 本回合结束
```

**这正是移植的关键：**

| 崩坠概念 | 一句话本质 |
|---|---|
| `AbstractBossDeckArchetype` | **一整套按回合号写死的出牌剧本**（不是卡组，是脚本） |
| `AbstractBossCard` | 原版卡牌的"镜像版"，`use(p, m)` 里 `p` 是**被打的玩家**、`m` 是 Boss 自己 |
| `EnemyUseCardAction` | 用 `UseCardAction` 的生命周期语义重写一遍"一张牌结算完"的全过程 |
| `EnemyCardGroup` | 手牌容器；`moveToDiscardPile` / `moveToExhaustPile` 是**空实现**（打完即消失） |
| `EnemyEnergyManager` + `EnemyEnergyPanel.totalCount` | Boss 的能量池（静态 int），每回合 `recharge()` |
| `charbosses.patches.*` | 把 StS1 里"假设 owner 是玩家"的地方一个个补回来 |

---

## 1. Boss 基类：`AbstractCharBoss` 与 `EnemyEnergyManager`

### 1.1 继承与字段

```java
// javap -p charbosses.bosses.AbstractCharBoss
public abstract class charbosses.bosses.AbstractCharBoss
        extends com.megacrit.cardcrawl.monsters.AbstractMonster {
  public static AbstractCharBoss boss;            // ← 全局单例，Boss 侧所有代码都靠它
  public static boolean finishedSetup;

  public ArrayList<AbstractCharbossRelic> relics; // ← 自己的遗物列表（不是 player.relics）
  public AbstractStance stance;                   // ← 观者的姿态
  public ArrayList<AbstractOrb> orbs;             // ← 机器人的充能球
  public int maxOrbs, masterMaxOrbs;
  public int masterHandSize, gameHandSize;        // 构造函数里 masterHandSize = 3
  public int mantraGained;
  public boolean powerhouseTurn;

  public EnemyEnergyManager energy;               // ← 能量
  public EnergyOrbInterface energyOrb;            // ← 复用玩家的 EnergyOrbRed/Green/Blue/Purple
  public EnemyEnergyPanel energyPanel;            // ← 屏幕上的能量球 UI

  public CardGroup hand;      // EnemyCardGroup(HAND)
  public CardGroup limbo;     // EnemyCardGroup(UNSPECIFIED)，用于 retain/暂存
  public AbstractCard cardInUse;

  public int damagedThisCombat, cardsPlayedThisTurn, attacksPlayedThisTurn;
  public AbstractPlayer.PlayerClass chosenClass;
  public AbstractBossDeckArchetype chosenArchetype;  // ← 当前这一幕的剧本
  public boolean onSetupTurn;
  private static int attacksDrawnForAttackPhase, setupsDrawnForSetupPhase;
  public String energyString;   // 构造函数里 = "[E]"，子类改成 "[R]" / 等
}
```

> **关键否定事实（很重要）**：这个类**没有** `drawPile`、`discardPile`、`exhaustPile` 字段。
> `grep` 整个 `AbstractCharBoss` 转储后确认：只有一个 `hand` 和 `limbo`。
> **Boss 没有抽牌堆/弃牌堆**——这是整个架构的地基。

### 1.2 初始化流程

```java
// AbstractCharBoss(String name, String id, int maxHealth, float hb_x, ..., PlayerClass playerClass)
// 构造函数关键片段（dcb-AbstractCharBoss-full.txt:57-152）
 77: new  charbosses/ui/EnemyEnergyPanel          ; this.energyPanel = new EnemyEnergyPanel(this)
 89: new  charbosses/cards/EnemyCardGroup
 94: getstatic CardGroup$CardGroupType.HAND
 98: invokespecial EnemyCardGroup.<init>:(CardGroupType;AbstractCharBoss)V
101: putfield hand
104: new  charbosses/cards/EnemyCardGroup
109: getstatic CardGroup$CardGroupType.UNSPECIFIED
116: putfield limbo
119: iconst_3
121: putfield masterHandSize          // = 3
```

```java
// AbstractCharBoss.init()  （AbstractMonster.init 的覆盖）
 0: putstatic     boss = this                       // 设全局单例
 4: setHp(maxHealth)
12: getstatic     downfall.downfallMod.useLegacyBosses:Z
15: ifne 29
18: energy.energyMaster = 3                         // useLegacyBosses == false
26: goto 37
29: energy.energyMaster = 2                         // useLegacyBosses == true（默认值）
37: generateAll()                                   // ← 生成卡组/maxHP
41: super.init()
45: preBattlePrep()
49: finishedSetup = true
53: type = AbstractMonster$EnemyType.BOSS
```

```java
// AbstractCharBoss.generateAll()
 0: generateDeck()                                  // ← 子类在这里选 archetype
 4: maxHealth += chosenArchetype.maxHPModifier
20: if (AbstractDungeon.ascensionLevel >= 9) {
28:    if (useLegacyBosses) maxHealth = round(maxHealth * 1.2f);
52:    else                maxHealth += chosenArchetype.maxHPModifierAsc;
68: currentHealth = maxHealth
76: updateHealthBar()
```

```java
// AbstractCharBoss.preBattlePrep()
 0: damagedThisCombat = 0; cardsPlayedThisTurn = 0; attacksPlayedThisTurn = 0
15: maxOrbs = 0; orbs.clear(); increaseMaxOrbSlots(masterMaxOrbs, false)
36: isBloodied = currentHealth <= maxHealth/2
58: AbstractPlayer.poisonKillCount = 0              // ← 借用了玩家的静态计数器
62: gameHandSize = masterHandSize
70: cardInUse = null
75: AbstractDungeon.overlayMenu.endTurnButton.enabled = false   // ← 战斗中永久禁用"结束回合"按钮
85: hand.clear()
92: if (hasRelic("SlaversCollar")) ((SlaversCollar)getRelic("SlaversCollar")).beforeEnergyPrep()
115: energy.prep()
122: powers.clear()
129: healthBarUpdatedEvent()
133: applyPreCombatLogic()                           // → relics.atPreBattle()
```

### 1.3 能量：`charbosses.core.EnemyEnergyManager`

```java
public class charbosses.core.EnemyEnergyManager extends com.megacrit.cardcrawl.core.EnergyManager {
  public EnemyEnergyManager(int e)   { super(e); }
  public void prep();
  public void recharge();
  public void use(int e);
}
```

- **`prep()`**（战斗开始调一次）
  ```
  0: energy = energyMaster          // 重置每回合基础能量
  8: EnemyEnergyPanel.totalCount = 0
  ```
- **`recharge()`**（在 `applyEndOfTurnTriggers()` 里调用 = Boss 回合结束时为下一回合重置；战斗开始时
  `usePreBattleAction()` 也调一次）——即"每回合能量怎么重置"：
  ```
   0: if (boss == null) return
   6: if (boss.hasRelic("Art of War"))  getRelic("Art of War").onTrigger()
  28: if (boss.hasRelic("Ice Cream")) {
  39:    if (EnemyEnergyPanel.totalCount > 0) {          // 有剩余才提示
  45:       getRelic("Ice Cream").flash()
  56:       addToTop(new RelicAboveCreatureAction(boss, getRelic("Ice Cream")))
         }
  80:    EnemyEnergyPanel.addEnergy(this.energy)         // 累积
  87: } else if (boss.hasPower("Conserve")) {
101:    if (totalCount > 0) addToTop(new ReducePowerAction(boss, boss, "Conserve", 1))
129:    EnemyEnergyPanel.addEnergy(this.energy)
 136: } else {
139:    EnemyEnergyPanel.setEnergy(this.energy)          // 覆盖式重置
  }
  ```
- **`use(int)`** → `EnemyEnergyPanel.useEnergy(e)`

**每回合能量 = `energyMaster`**：`useLegacyBosses == false` → **3 点**；`== true`（默认）→ **2 点**（`AbstractCharBoss.init()` 里硬写，构造 `EnemyEnergyManager(c)` 时传的值会被覆盖）。
`Ice Cream`（冰淇淋：能量保留）与 `Conserve` power 会改成"累加"模式；`Art of War` 在 recharge 时被 `onTrigger()`。

实际能量池是 **`charbosses.ui.EnemyEnergyPanel.totalCount`（`public static int`）**：
```java
public static void setEnergy(int);   // totalCount = v  (clamp 0..999)
public static void addEnergy(int);   // totalCount += v，播 "ADRENALINE" 音效 + VFX
public static void useEnergy(int);   // totalCount -= v  (clamp)
```
`EnemyEnergyPanel` 还负责把能量球画在 Boss 身上（`renderOrb` / `renderVfx` / `updateVfx`），
以及 `getCurrentEnergy()`（匿名内部类 `AbstractCharBoss$2` 用它做意图预估）。

### 1.4 回合流程

```java
// takeTurn()
 0: attacksDrawnForAttackPhase = 0
 4: setupsDrawnForSetupPhase = 0
 8: startTurn()
12: addToBot(new charbosses.actions.util.CharbossMakePlayAction())
23: onSetupTurn = !onSetupTurn          // 每回合翻转"布置/进攻"标记
```

```java
// startTurn()
 0: cardsPlayedThisTurn = 0
 5: attacksPlayedThisTurn = 0
10: for (c : hand) ((AbstractBossCard)c).lockIntentValues = true   // 锁住意图数值
51: applyStartOfTurnRelics()      // stance.atStartOfTurn() + relics.atTurnStart()
55: applyStartOfTurnPreDrawCards()
59: applyStartOfTurnCards()
63: applyStartOfTurnOrbs()
```

```java
// makePlay()  —— AI 主循环（dcb-AbstractCharBoss-full.txt:471）
 0: for (AbstractCard _c : hand.group) {
31:    AbstractBossCard c = (AbstractBossCard) _c;
35:    if (!c.canUse(AbstractDungeon.player, this)) continue;
47:    if (c.getPriority(hand.group) <= -100)      continue;
62:    useCard(c, this, charbosses.ui.EnemyEnergyPanel.totalCount);
71:    addToBot(new DelayedActionAction(new CharbossDoNextCardAction()));   // ← 递归
89:    return;
     }
93: addToBot(new charbosses.actions.orb.EnemyTriggerEndOfTurnOrbActions());
```

`CharbossMakePlayAction.update()` 与 `CharbossDoNextCardAction.update()` 都是
`if (AbstractCharBoss.boss != null) AbstractCharBoss.boss.makePlay(); isDone = true;`
——即**用 StS1 的 action queue 做协程**，每张牌结算完再回来打下一张。

`endTurnStartTurn()`（玩家回合结束时由 `CharbossTurnstartDrawAction` 触发，见 §2.4）：
```
if (!room.isBattleOver && currentHealth > 0) {
   addToBot(AbstractCharBoss$1)                        // "发牌"：把剧本的牌塞进手牌
   addToBot(new WaitAction(0.2f))
   applyStartOfTurnPostDrawRelics()                    // relics.atTurnStartPostDraw()
   applyStartOfTurnPostDrawPowers()
   if (!AbstractDungeon.player.hasRelic("Runic Dome"))
       addToBot(AbstractCharBoss$2)                    // "思考"：算哪些牌这回合打得出
   cardsPlayedThisTurn = 0; attacksPlayedThisTurn = 0
}
```

`applyEndOfTurnTriggers()`（Boss 回合结束）：
```
 if (hasPower("stslib:Stunned")) chosenArchetype.turn -= 1        // 被眩晕则剧本不推进
 energy.recharge()                                               // ← 能量重置点
 for (p : powers) p.onEnergyRecharge()
 for (c : hand)   c.triggerOnEndOfTurnForPlayingCard()
 stance.onEndOfTurn()
 if (useLegacyBosses) { addToBot(new EnemyDiscardAtEndOfTurnAction());
                        for (c : hand) c.resetAttributes(); }
 addToBot(new DelayedActionAction(new CharbossTurnstartDrawAction()))   // → endTurnStartTurn()
```

### 1.5 玩家式管线被逐个复刻（这是移植时的"必须清单"）

`AbstractCharBoss` 里有一整组"玩家才有的钩子"，全部手写在怪物身上：

| 方法 | 实际做的事（字节码证据） |
|---|---|
| `applyPreCombatLogic()` | `for (r : relics) r.atPreBattle()` |
| `applyStartOfCombatPreDrawLogic()` | `for (r : relics) r.atBattleStartPreDraw()` |
| `applyStartOfCombatLogic()` | `for (r : relics) r.atBattleStart()` |
| `applyStartOfTurnRelics()` | `stance.atStartOfTurn()` + `for (r : relics) r.atTurnStart()` |
| `applyStartOfTurnPostDrawRelics()` | `for (r : relics) r.atTurnStartPostDraw()` |
| `applyStartOfTurnPreDrawCards()` | `for (c : hand) c.atTurnStartPreDraw()` |
| `onCardDrawOrDiscard()` | `for (p : powers) p.onDrawOrDiscard(); for (r : relics) r.onDrawOrDiscard()` |
| `updateCardsOnDiscard()` | `for (c : hand) c.didDiscard()` |
| `damage(DamageInfo)` | 完整重写玩家受伤流程（见下） |
| `addBlock / gainEnergy / loseEnergy / heal / die / switchedStance / onStanceChange / channelOrb / evokeOrb / …` | 都是手写转发到 `EnemyEnergyPanel` / orbs / stance |
| `getRelicNames()` | `for (r : relics) arr.add(r.relicId)`（供 UI 显示） |

`damage(DamageInfo)` 的要点（`dcb-AbstractCharBoss-full.txt:2318`）：
```
stslib BindingPatches$DisableReactionaryActionBinding.disableBefore(this)   // 关掉原版"反应式绑定"
output = max(0, info.output); if (hasPower("Intangible") && output > 1) output = 1
output = decrementBlock(info, output)
// 伤害来源分类处理：
if (info.owner == this)        for (r : this.relics)  output = r.onAttackToChangeDamage(info, output)
if (info.owner == AbstractDungeon.player)
                               for (r : player.relics) output = r.onAttackToChangeDamage(info, output)
if (info.owner != null)        for (p : info.owner.powers) output = p.onAttackToChangeDamage(info, output)
... 之后 onAttacked / brokeBlock / thorns / powers / relics 的完整链条
```
——**Boss 的遗物只在自己打自己（自伤牌）时生效；玩家打 Boss 时走玩家遗物。**

### 1.6 不确定性

- `attacksDrawnForAttackPhase` / `setupsDrawnForSetupPhase` 两个静态计数器只在 `takeTurn()` 里被清零，
  在 `charbosses/` 里没有其它写点（对 780 个 class 做过二进制字符串扫描确认没有别处写入），
  疑似 legacy 残留。真正驱动 `onSetupTurn` 翻转的是 `takeTurn()` 尾部的取反。
- `AbstractDungeon.player.poisonKillCount` 被 Boss 复用/清零，说明 Downfall 对全局静态状态有侵入；StS2 移植时要换成实例状态。

---

## 2. 卡组：`AbstractBossDeckArchetype` = 按回合号的"出牌剧本"

### 2.1 抽象 API 其实很薄

```java
// javap -p charbosses.bosses.AbstractBossDeckArchetype
public abstract class charbosses.bosses.AbstractBossDeckArchetype {
  public static final Logger logger;
  public static MonsterStrings bossMechanicString;      // LocalizedStrings.getMonsterStrings("downfall:BossMechanics")
  public String bossMechanicName, bossMechanicDesc;     // Boss 机制说明（显示在左侧信息面板）
  public int maxHPModifier, maxHPModifierAsc;
  public int actNum;                                    // 1 / 2 / 3
  public boolean looped;                                // 剧本"第二轮"开关
  public int turn;                                      // 当前回合号（脚本的索引）
  private String ID;                                    // → AbstractDungeon.lastCombatMetricKey

  public AbstractBossDeckArchetype(String id, String loggerClassName, String loggerArchetypeName);

  public abstract void initializeBonusRelic();                          // ← 唯一抽象方法（A19+ 额外遗物）
  public void initialize()                      { }                     // 空，子类覆盖：加遗物
  public void addedPreBattle()                  { if (useLegacyBosses) initializeBossPanel(); }
  public ArrayList<AbstractCard> getThisTurnCards() { return new ArrayList<>(); }  // ← 子类覆盖：本回合剧本
  public void addRelic(AbstractCharbossRelic r) { r.instantObtain(AbstractCharBoss.boss); }
  public void addToList(ArrayList<AbstractCard> c, AbstractCard q, boolean upgraded);
  public void addToList(ArrayList<AbstractCard> c, AbstractBossCard q, boolean upgraded, int energyGeneratedIfPlayed);
  public void initializeBossPanel();   // 把 mechanicName/Desc 写进 charbosses.BossMechanicDisplayPanel
}
```

**回答"抽象方法是什么"**：
- 真正 `abstract` 的只有 **`initializeBonusRelic()`**。
- 真正的"卡组声明"接口是 **`getThisTurnCards()`**（默认空列表）。
- 基类构造函数的副作用：`if (AbstractDungeon.actNum != 4) AbstractDungeon.lastCombatMetricKey = id;`

### 2.2 具体例子 A：`Ironclad/NewAge/ArchetypeAct1StatusesNewAge`

```java
// javap -p
public class ArchetypeAct1StatusesNewAge extends ArchetypeBaseIronclad {
  boolean secondLoop;
  private AbstractCharbossRelic theArtOfWar;
  public void addedPreBattle();
  public void initialize();
  public ArrayList<AbstractCard> getThisTurnCards();
  public void initializeBonusRelic();
}
```

**构造函数**（声明 ID / 幕 / 血量 / Boss 机制）：
```
 1: ldc  "IC_STATUS_ARCHETYPE"                     // id
 3: ldc  "Status"                                  // logger 名
 5: invokespecial ArchetypeBaseIronclad.<init>:(String;String)V   // → super(id, "Ironclad", name)
15: maxHPModifier += 60
25: actNum = 1
29: bossMechanicName = IroncladStatusPower.NAME
36: bossMechanicDesc = IroncladStatusPower.DESC[0]
```

**`initialize()`**（挂遗物）：
```
 0: addRelic(new CBR_NeowsBlessing())
11: theArtOfWar = new CBR_ArtOfWar(); addRelic(theArtOfWar)
30: addRelic(new CBR_ChampionsBelt())
41: extraUpgrades = AbstractDungeon.ascensionLevel >= 4     // 局部变量名 extraUpgrades（-l 可见）
```

**`getThisTurnCards()` —— 这就是"卡组"**：
```
 0: cardsList = new ArrayList()
 8: extraUpgrades = ascensionLevel >= 4
21: if (!looped) {
      switch (turn) {
        case 0: addToList(cardsList, new EnBash());          addToList(cardsList, new EnMalfunctioning()); addToList(cardsList, new EnDoubt());        turn++; break;
        case 1: addToList(cardsList, new EnWildStrike());    addToList(cardsList, new EnRecklessCharge()); addToList(cardsList, new EnDefendRed());   turn++; break;
        case 2: addToList(cardsList, new EnPowerThrough(true), extraUpgrades); addToList(cardsList, new EnSecondWind()); addToList(cardsList, new EnDefendRed());
                theArtOfWar.beginPulse();  turn++; break;
        case 3: addToList(cardsList, new EnSeeingRed());     addToList(cardsList, new EnImmolate());      addToList(cardsList, new EnClumsy());      turn++; break;
        case 4: addToList(cardsList, new EnTrueGrit(), true);×2 ; addToList(cardsList, new EnWound()); theArtOfWar.beginPulse(); turn++; break;
        case 5: addToList(cardsList, new EnThunderclap(), true); addToList(cardsList, new EnFeelNoPain()); addToList(cardsList, new EnDazed());
                turn = 0; looped = true; break;                       // ← 第一轮结束，进入 looped 剧本
      }
    } else {
      switch (turn) {                                 // ← 第二套（循环）剧本
        case 0: EnPowerThrough(true)+extraUpgrades, EnTrueGrit(true), EnWound
        case 1: EnWildStrike, EnRecklessCharge, EnBash
        case 2: EnSecondWind, EnBurn, EnWound;  theArtOfWar.beginPulse()
        case 3: EnImmolate, EnThunderclap(true), EnDazed
        case 4: EnTrueGrit(true), EnDefendRed, EnWound; theArtOfWar.beginPulse(); turn = 0
      }
    }
    return cardsList;
```

**`initializeBonusRelic()`**：`addRelic(new CBR_Orichalcum())`（A19+ 时由 `CharBossIronclad.generateDeck()` 调用）。

> 注意 `addToList(list, card, upgraded)` 的重载会**就地 `card.upgrade()`**；
> `addToList(list, bossCard, upgraded, energyGeneratedIfPlayed)` 会额外写
> `card.energyGeneratedIfPlayed = n`（用于让意图预估知道这张牌会产出多少能量）。

### 2.3 具体例子 B：`Silent/NewAge/ArchetypeAct1PoisonNewAge`

```java
public class ArchetypeAct1PoisonNewAge extends ArchetypeBaseSilent {
  public ArchetypeAct1PoisonNewAge();
  public void addedPreBattle();
  public void initialize();
  public ArrayList<AbstractCard> getThisTurnCards();
  public void initializeBonusRelic();
}
```

**构造函数**：`super("SI_POISON_ARCHETYPE", "Poison")` → `ArchetypeBaseSilent(id, name)` → `AbstractBossDeckArchetype(id, "Silent", name)`

**`initialize()`（挂遗物，4 件）**：
```
 1: addRelic(new CBR_NeowsBlessing())
12: addRelic(new CBR_TwistedFunnel())
23: addRelic(new CBR_Anchor())
34: addRelic(new CBR_Lantern())
```

**`addedPreBattle()`（★ archetype 可以改写 Boss 的外观和开局状态）**：
```
 1: super.addedPreBattle()
40: new charbosses/cards/other/Antidote()                 // ← 给玩家塞一张"解药"牌（机制用）
46: ldc  "loadAnimation"
87: ldc  "expansioncontentResources/images/bosses/silent/1/Poison_Silent.atlas"
92: ldc  "expansioncontentResources/images/bosses/silent/1/Poison_Silent.json"
113: ldc "Idle"
```
即：**同一个 `CharBossSilent` 类，靠 archetype 换一套 Spine 动画资源和主题皮肤**
（铁甲 Act1 就在 `AbstractCharBoss` 之外用 `downfallResources/images/fgShrooms.png` / `bgShrooms.png` 做背景）。

**`getThisTurnCards()`**：与 A 同构的 `tableswitch(turn)` 剧本，只是
- 第一段只有 **4 回合**（`switch(turn) { case 0..3 }`，`turn == 3` 时置 `looped = true`）；
- `looped` 后是 **3 回合**的第二段剧本；
- 每回合 2~3 张牌，全部来自 `charbosses/cards/green/*`：
  `EnBurst` / `EnCripplingCloud` / `EnSurvivor` / `EnPoisonedStab` / `EnDodgeAndRoll` / `EnStrikeGreen`
  / `EnFootwork` / `EnNoxiousFumes` / `EnBane` / `EnDeflect` / `EnDefendGreen`。

**`initializeBonusRelic()`**：`addRelic(new CBR_Vajra())`（A19+）。

### 2.4 "卡组是每场战斗新洗还是固定顺序？"

**两者都不是——它根本没有卡组。**

| 问题 | 答案 | 证据 |
|---|---|---|
| 有抽牌堆吗？ | **没有**。`AbstractCharBoss` 无 `drawPile` 字段 | 成员列表 |
| 有弃牌堆吗？ | **没有**，且 `moveToDiscardPile` 是空实现 | `EnemyCardGroup.moveToDiscardPile` 只打日志 + `onCardDrawOrDiscard()` |
| 有消耗堆吗？ | **没有**，`moveToExhaustPile` 只播 `ExhaustCardEffect` | `EnemyCardGroup.moveToExhaustPile` |
| 手牌从哪来？ | 每回合由 `archetype.getThisTurnCards()` **new 出全新的卡对象**塞进 `hand` | `AbstractCharBoss$1.update()` |
| 顺序？ | **完全固定**：按 `addToList` 的调用顺序 | `AbstractCharBoss$1` 里 `Collections.sort(handAsBoss, new AbstractCharBoss$sortByNewPrio(this))`，而 `newPrio` 默认 0 |
| 有随机吗？ | 几乎没有；唯一随机是 `Strange Spoon`（消耗牌 50% 不消耗）等遗物 | `EnemyUseCardAction` 里 `AbstractDungeon.cardRandomRng.randomBoolean()` |
| 跨回合保留？ | `Runic Pyramid` 遗物 / `Equilibrium` power / 卡牌 `retain` → 进 `limbo`，下回合 `EnemyRestoreRetainedCardsAction` 还回来 | `EnemyDiscardAtEndOfTurnAction` |

**`AbstractCharBoss$1.update()`（每个玩家回合结束时的"发牌"）**
```
 5: if (boss == null || boss.hand == null) return
20: for (AbstractCard q : getThisTurnCards()) {
50:    boss.hand.addToTop(q);
60:    if (q instanceof AbstractBossCard) ((AbstractBossCard)q).bossDarken();   // 先变暗
74:    q.current_y = Settings.HEIGHT / 2f;
84:    q.current_x = Settings.WIDTH;
     }
 95: handAsBoss = new ArrayList();
103: for (AbstractCard c : boss.hand.group) handAsBoss.add((AbstractBossCard)c);
147: Collections.sort(handAsBoss, new AbstractCharBoss$sortByNewPrio(this));
162: newHand = new ArrayList();
170: for (AbstractCard c : handAsBoss) { newHand.add(c); c.applyPowers(); c.triggerWhenDrawn(); }
215: boss.hand.group = newHand;
225: boss.hand.refreshHandLayout();
234: boss.applyPowers();
```

**`AbstractCharBoss$2.update()`（"思考"：算出这回合打得起的牌并点亮意图）**
```
 5: budget = energyPanel.getCurrentEnergy()
16: for (AbstractCard c : boss.hand.group) {
63:    if (c.costForTurn <= budget && c.costForTurn != -2 && c instanceof AbstractBossCard) {
87:        ((AbstractBossCard)c).createIntent();
94:        ((AbstractBossCard)c).bossLighten();        // 变亮 = "这回合会打"
101:       budget -= c.costForTurn;
108:       budget += ((AbstractBossCard)c).energyGeneratedIfPlayed;
118:       if (budget < 0) budget = 0;
        } else if (c.costForTurn == -2 && c.type == CURSE && c.color == CURSE) {
156:       ((AbstractBossCard)c).bossLighten();
        }
     }
166: for (c : hand) ((AbstractBossCard)c).refreshIntentHbLocation();
```

---

## 3. 【最关键】打牌机制

### 3.1 一句话

**`AbstractCharBoss.useCard()` 直接调 `AbstractCard.use(AbstractDungeon.player, this)`，然后 `addToBot(new EnemyUseCardAction(...))`。**
它**不用** `UseCardAction` 来驱动卡牌效果（不是 `GameActionManager.addToBottom(new UseCardAction(card, target))` 那条路），
而是自己实现了 `UseCardAction` 的**后处理语义**（消耗 / 回手 / 洗回 / 进弃牌堆 / power 牌 empower）。

### 3.2 `AbstractCharBoss.useCard` 逐条（`dcb-AbstractCharBoss-full.txt:2078`）

```java
public void useCard(AbstractCard c, AbstractMonster monster, int energyOnUse) {
  if (monster == null) monster = this;                              // ← 默认目标就是 Boss 自己
  if (c.type == ATTACK) { attacksPlayedThisTurn++; useFastAttackAnimation();
                          if (c.damage > random(20)) onPlayAttackCardSound(); }
  cardsPlayedThisTurn++;
  c.calculateCardDamage(monster);                                   // ← 算伤害（走下面 3.3 的管线）
  if (c.cost == -1 && EnemyEnergyPanel.totalCount >= energyOnUse && !c.ignoreEnergyOnUse)
      c.energyOnUse = EnemyEnergyPanel.totalCount;                  // ← X 费：X = 当前全部能量
  if (c.cost == -1 && c.isInAutoplay) c.freeToPlayOnce = true;
  c.use(AbstractDungeon.player, monster);                           // ★★★ 真正施加效果
  addToBot(new charbosses.actions.common.EnemyUseCardAction(c, monster));
  if (!c.dontTriggerOnUseCard) hand.triggerOnOtherCardPlayed(c);
  hand.removeCard(c);                                               // 先离开手牌
  cardInUse = c;
  c.target_x = Settings.WIDTH / 2f;  c.target_y = Settings.HEIGHT / 2f;
  if (c.costForTurn > 0 && !c.freeToPlay() && !c.isInAutoplay
      && !(hasPower("Corruption") && c.type == SKILL))
      energy.use(c.costForTurn);                                    // ← 扣能量
  ((AbstractBossCard)c).showIntent = false;
}
```

**参数语义（这是整个架构最反直觉的地方，也是移植时最容易踩的坑）：**

| `use(p, m)` 的参数 | 在 Downfall 里是谁 | 作用 |
|---|---|---|
| `p`（原版语义：卡牌拥有者） | **`AbstractDungeon.player`（真正的人类玩家）** | **被打的目标**。卡牌实现在 `use()` 里把它当扣血对象 |
| `m`（原版语义：目标怪物） | **Boss 自己**（`makePlay()` 传 `this`） | **伤害来源 / 自身 buff 目标** |

### 3.3 "一张攻击牌的效果是怎么施加到玩家身上的"

以最基础的 `charbosses.cards.red.EnStrikeRed` 为例（完整字节码见 `dcb-sample-cards-red.txt`）：

```java
public class EnStrikeRed extends AbstractBossCard {
  public static final String ID = "downfall_Charboss:Strike_R";
  public EnStrikeRed() {
    // (id, name, img, cost=1, rawDescription, ATTACK, RED, BASIC, ENEMY, Intent.ATTACK)
    super("downfall_Charboss:Strike_R", cardStrings.NAME, "red/attack/strike", 1,
          cardStrings.DESCRIPTION, ATTACK, RED, BASIC, ENEMY, Intent.ATTACK);
    baseDamage = 6;
    tags.add(CardTags.STRIKE); tags.add(CardTags.STARTER_STRIKE);
    tags.add(downfall.downfallMod.CHARBOSS_ATTACK);        // ← 自定义 tag，标记"这是 Boss 的攻击牌"
  }
  public void use(AbstractPlayer p, AbstractMonster m) {
    addToBot(new DamageAction(p,                                        // ← 打的是 p（玩家）
              new DamageInfo(m, damage, damageTypeForTurn),             // ← 来源是 m（Boss）
              AttackEffect.SLASH_DIAGONAL));
  }
  public void upgrade() { if (!upgraded) { upgradeName(); upgradeDamage(3); } }
  public AbstractCard makeCopy() { return new EnStrikeRed(); }
}
```

**攻击牌 = `new DamageAction(p, new DamageInfo(m, this.damage, damageTypeForTurn), effect)`。**
**格挡牌 = `new GainBlockAction(m, m, this.block)`**（`EnDefendRed`，m = Boss 自己）。
**debuff 牌 = `new ApplyPowerAction(p, m, new VulnerablePower(p, magicNumber, true), magicNumber)`**（`EnBash`）：

```java
// EnBash.use(AbstractPlayer p, AbstractMonster m)
 0: addToBot(new DamageAction(p, new DamageInfo(m, damage, damageTypeForTurn), BLUNT_HEAVY));
31: addToBot(new ApplyPowerAction(p, m, new VulnerablePower(p, magicNumber, true), magicNumber));
61: if (!p.hasPower("Artifact") && AbstractCharBoss.boss.hasRelic("Champion Belt"))
81:     AbstractCharBoss.boss.getRelic("Champion Belt").onTrigger();     // ← 遗物手动触发
```

注意 `EnBash` 的构造函数还会**为意图预估准备元数据**：
```
 55: tags.add(downfallMod.CHARBOSS_SETUP)     // ← 这张牌属于"布置"阶段
 66: artifactConsumedIfPlayed = 1             // 会吃掉玩家 1 层 Artifact
 71: if (boss.hasRelic("Champion Belt")) artifactConsumedIfPlayed = 2
 87: vulnGeneratedIfPlayed = magicNumber      // 会给后续牌 +50% 伤害
```

### 3.4 伤害计算管线：`AbstractBossCard.applyPowers()` / `calculateCardDamage()`

```
applyPowersToBlock()                                    // 先算格挡
tmp = baseDamage
owner = (this.owner != null) ? this.owner : AbstractCharBoss.boss
for (r : owner.relics)   tmp = r.atDamageModify(tmp, this)          // Boss 遗物（给出侧）
for (p : owner.powers)   tmp = p.atDamageGive(tmp, damageTypeForTurn, this)
tmp = owner.stance.atDamageGive(tmp, damageTypeForTurn, this)       // 姿态（愤怒/神性）
for (p : AbstractDungeon.player.powers) tmp = p.atDamageReceive(tmp, damageTypeForTurn, this)
tmp = owner.stance.atDamageReceive(tmp, damageTypeForTurn)
for (p : owner.powers)   tmp = p.atDamageFinalGive(tmp, damageTypeForTurn, this)
for (p : player.powers)  tmp = p.atDamageFinalReceive(tmp, damageTypeForTurn, this)
if (tmp < 0) tmp = 0
damage = floor(tmp)
initializeDescription()
if (intent != null && !bossDarkened) createIntent()
if (owner.hasPower("stslib:Stunned")) { bossDarken(); destroyIntent(); }
```

`calculateCardDamage(AbstractMonster mo)` 额外做：
- `mo == null` 时用 `owner`（Boss 自己）作为"被攻击者"占位；
- 计算 `damage`，并用**另一条带 `manualCustomDamageModifier` / `manualCustomDamageModifierMult` 的调用**算出 `intentDmg`（意图显示值）；
- 特例：`EnCarveReality.willUseSmite` → 把 `new EnSmite()` 的 `intentDmg` 加进来。

**含义**：**Boss 的牌用 Boss 自己的遗物/power/姿态当"给出侧"，用人（玩家）的 power 当"接收侧"**，
所以「玩家的易伤 / 虚弱 / 缓冲 / 无形 / 荆棘 / 遗物（如 Torii、Tungsten Rod）」全部照常生效。

### 3.5 一张牌"结算完"的全过程：`charbosses.actions.common.EnemyUseCardAction`

```java
public class EnemyUseCardAction extends AbstractGameAction {
  public AbstractCreature target;
  public boolean exhaustCard, returnToHand, reboundCard;
  private AbstractCard targetCard;
  public EnemyUseCardAction(AbstractCard c, AbstractCreature target);   // duration = 0.15f
  public UseCardAction makeNormalCardAction();   // 造一个"假"的原版 UseCardAction 只是为了传参给 power/relic 钩子
  public void update();
}
```

`update()`（`dcb-EnemyUseCardAction-full.txt:196`）：
```
if (duration == 0.15f && boss != null) {
   // ① 通知 power 的 onAfterUseCard（跳过 DEBUFF 类，避免玩家 debuff 反噬）
   for (p : boss.powers) if (!targetCard.dontTriggerOnUseCard && p.type != DEBUFF) {
       if (p instanceof EnemyReboundPower) ((EnemyReboundPower)p).onAfterUse(targetCard, this);
       p.onAfterUseCard(targetCard, makeNormalCardAction());
   }
   targetCard.freeToPlayOnce = false;  targetCard.isInAutoplay = false;

   // ② purgeOnUse（如某些临时牌）→ 播特效后消失
   if (targetCard.purgeOnUse) { addToTop(new EnemyShowCardAndPoofAction(targetCard)); isDone = true; boss.cardInUse = null; return; }

   // ③ POWER 牌：展示 → 等 0.7s(快模式 0.1s) → hand.empower(card) → 重新 applyPowers/glowCheck
   if (targetCard.type == POWER) { addToTop(new EnemyShowCardAction(targetCard)); addToTop(new WaitAction(...)); boss.hand.empower(targetCard); isDone = true; boss.hand.applyPowers(); boss.hand.glowCheck(); boss.cardInUse = null; return; }

   boss.cardInUse = null;
   // ④ 消耗 / 弃置 分流
   boolean spoonSave = false;
   if (exhaustCard && boss.hasRelic("Strange Spoon") && targetCard.type != POWER)
       spoonSave = AbstractDungeon.cardRandomRng.randomBoolean();          // 50%
   if (!exhaustCard || spoonSave) {
       if (spoonSave) boss.getRelic("Strange Spoon").flash();
       if (reboundCard)                          boss.hand.moveToDeck(targetCard, false);
       else if (targetCard.shuffleBackIntoDrawPile) boss.hand.moveToDeck(targetCard, true);
       else if (targetCard.returnToHand) { boss.hand.moveToHand(targetCard); boss.onCardDrawOrDiscard(); }
       else                                      boss.hand.moveToDiscardPile(targetCard);   // ← 空实现
   } else {
       boss.hand.moveToExhaustPile(targetCard);                              // ← 只播特效
   }
   targetCard.exhaustOnUseOnce = false;  targetCard.dontTriggerOnUseCard = false;
   addToBot(new EnemyHandCheckAction());          // hand.applyPowers() + hand.glowCheck()
}
tickDuration();
```

### 3.6 消耗 / 虚无 / 保留 / X 费 的处理

| 机制 | 处理方式 | 证据 |
|---|---|---|
| **消耗** | `AbstractCard.exhaust` → `exhaustCard` → `moveToExhaustPile`（**只播特效，不进任何堆**）；`Strange Spoon` 50% 豁免 | `EnemyUseCardAction` ctor `exhaustCard = c.exhaustOnUseOnce \|\| c.exhaust`；`EnemyCardGroup.moveToExhaustPile` |
| **虚无（Ethereal）** | 有专门的 `charbosses.actions.utility.EnemyExhaustAllEtherealAction`；`autoPriority()` 里 ethereal 牌 value ×2 | `AbstractBossCard.autoPriority` `210: if (isEthereal) value *= 2` |
| **保留（Retain）** | 回合末 `c.retain \|\| c.selfRetain` → `limbo.addToTop(c)`，下回合 `EnemyRestoreRetainedCardsAction`；`Runic Pyramid` 遗物 / `Equilibrium` power 让**整手**保留 | `EnemyDiscardAtEndOfTurnAction` |
| **X 费** | `AbstractCard.cost = -1` → `useCard` 里 `c.energyOnUse = EnemyEnergyPanel.totalCount`（= 当前全部能量）；卡牌自己用 `energyOnUse` 实现效果 | `AbstractCharBoss.useCard` 61-107 |
| **X 费（实现陷阱）** | `EnWhirlwind` **自己声明了一个 `private int cost`** 遮蔽父类字段，`use()` 里 `for (i = 0; i < this.cost; i++)`，而 `new EnWhirlwind()` 传 `this(0)` → **默认 0 次**；必须显式 `new EnWhirlwind(n)` | `dcb-sample-cards-xcost.txt` |
| **自伤牌** | `EnBloodletting`: `new LoseHPAction(m, m, magicNumber)`（来源和目标都是 Boss）+ `new EnemyGainEnergyAction(n)` | `dcb-sample-cards-xcost.txt` |
| **往手牌/抽牌堆加牌** | 全都有 Boss 版：`EnemyMakeTempCardInHandAction`、`EnemyMakeTempCardInDrawPileAction`、`EnemyMakeTempCardInDiscardAction`、`EnemyMakeTempCardInHandAction`（`EnPowerThrough` 用它塞 `EnWound`） | `charbosses/actions/common/` |
| **弃牌 / 消耗牌堆操作** | `EnemyDiscardAction`、`EnemyExhaustAction`、`EnemyRestoreRetainedCardsAction` | 同上 |
| **充能球** | 一整套 `charbosses/actions/orb/*`：`EnemyChannelAction` / `EnemyEvokeOrbAction` / `EnemyDarkOrbEvokeAction` / `EnemyLightningOrbPassiveAction` / `EnemyTriggerEndOfTurnOrbActions` 等 | `charbosses/orbs/AbstractEnemyOrb` + `AbstractCharBoss.channelOrb/evokeOrb` |

### 3.7 `charbosses/patches/` 各自在补什么坑（= StS2 里要重新踩的坑）

全部通过 `@SpirePatch(clz=..., method=...)` 打在原版类上（`javap -v` 的 `RuntimeVisibleAnnotations` 可见）：

| 补丁类 | 打的类.方法 | 补什么坑 |
|---|---|---|
| `AbstractBossCardPatches$RenderCardBgPatch` | `AbstractCard.renderCardBg` | 自定义卡色（RED/GREEN/BLUE/PURPLE 之外的 mod 色）的卡面背景贴图：`BaseMod.get{Power,Attack,Skill}BgTexture(color)`；用反射调私有 `renderHelper(...)` |
| `AbstractBossCardPatches$RenderEnergyPatch`（+`Instrument()` 的 `ExprEditor`） | `AbstractCard.renderEnergy` | 同一个：自定义卡色的**能量图标**贴图（`BaseMod.getEnergyOrbTexture(color)`）；用 javassist `ExprEditor` 在字节码层替换 |
| `HighlightCards` / `HighlightCards$CardGlowPatch` | `vfx.cardManip.CardGlowBorder.<ctor>` | 卡牌发光描边：`shivTexture`/`biasTexture` 两种图标；`highCostChecker`/`powerChecker` 决定高亮 |
| `DarkenCardPatch` | `AbstractCard.renderTint` | "变暗/变亮" 色调（`lightenColor` / `darkenColor`，`0.75f` 等常量）。牌在手里先 `bossDarken()`，付得起才 `bossLighten()`——**这是"哪些牌这回合会打"的视觉语言** |
| `DontRenderThatPatch$Noooooo` | `vfx.combat.PowerExpireTextEffect.render` | 屏蔽掉 Silence 的一个特效文字：`if (AbstractCharBoss.boss instanceof CharBossSilent) return SpireReturn.Return(null)` |
| `ApplyPowerPatch` | `actions.common.ApplyPowerAction.update` | **最大的坑**：给 Boss 上 power 时，原版逻辑假定 owner 是玩家。这里用 `ReflectionHacks.getPrivate/setPrivate` 直接操作 `duration`/`startingDuration`/`powerToApply`，并实现 `Ginger` 遗物（免疫虚弱）、`CBR_NeowsBlessing`（免疫 `stslib:Stunned`）等 |
| `BrokeBlockPatch` | `core.AbstractCreature.brokeBlock` | 破防时遍历所有怪物，若是 `AbstractCharBoss` 则 `for (r : relics) r.onBlockBroken(creature)`——Boss 的破防遗物钩子 |
| `ApplyFocusrPatch$ApplyFocusrPatchA/B` | `RemoveSpecificPowerAction.update` / `AbstractDungeon.onModifyPower` | 机器人"集中"变化时的聚焦特效指向错误（默认指向玩家） |
| `PaperKraneWeakPatch$SuperWeakEffect/SuperWeakText` | `WeakPower.atDamageGive` / `WeakPower.updateDescription` | 玩家拿了 Paper Krane（虚弱效果 -40%）时，**Boss 打出的虚弱**和玩家自己的虚弱效果要区分 |

**移植启示**：这些补丁的共同点全部是同一类问题——
**StS1 的引擎里有大量"隐式假定 owner/target 是玩家（或玩家侧的怪物列表）"的代码路径**。
StS2 里我们必须先系统性地找出这些假定，再决定是"补丁 hook"还是"换一条 API"。

---

## 4. AI 决策

### 4.1 枚举与比较器

```java
// charbosses.bosses.AbstractCharBoss$DrawTypes  （private enum）
public static final DrawTypes Attack;       // "Attack"
public static final DrawTypes Setup;        // "Setup"
public static final DrawTypes EitherPhase;  // "EitherPhase"
```

> ### ⚠ 重要发现：`DrawTypes` 与整套"抽牌"逻辑是**死代码**
>
> 对全部 780 个 `charbosses/*.class` 做了常量池字符串扫描：
> `chooseCardToDraw` / `performCardSearch` / `DrawTypes` **只出现在 `AbstractCharBoss` 及其内部类里**，
> 且在 `AbstractCharBoss` 内部，`chooseCardToDraw` 只有定义、**没有任何调用点**
> （`grep` `dcb-AbstractCharBoss-full.txt` 只匹配到 1857 行的定义行）。
> 同样，静态计数器 `attacksDrawnForAttackPhase` / `setupsDrawnForSetupPhase` 也只在 `takeTurn()` 里被清零、无其它写点。
>
> **结论：Downfall 早期真的有一套"抽牌堆 + 按 DrawTypes 优先级抽牌"的系统**
> （`findReplacementCardInDraw(drawPile, boolean, boolean)`、`chooseCardToDraw(List)`、
> `performCardSearch(List, DrawTypes first, DrawTypes second, DrawTypes third)`），
> 后来被**按回合号写死的剧本**取代，旧代码留在类里没删。
> 现在真正在用的"阶段"标记只有一个布尔 `onSetupTurn`（`takeTurn()` 里每回合取反）。
>
> **对移植的启示**：不要去实现 `DrawTypes` 的三段优先级搜索——那是被作者自己淘汰的方案。
> 「Boss 抽牌随机 → 无法提前预告意图 → 玩家体验崩坏」很可能正是淘汰它的原因。

```java
// charbosses.bosses.AbstractCharBoss$sortByNewPrio
public int compare(AbstractBossCard a, AbstractBossCard b) {
  return a.newPrio - b.newPrio;      // 升序
}
```
**扫描结论**：对全部 780 个 `charbosses/*.class` 做字符串扫描，"`newPrio`" 只出现在 7 个文件里：
`AbstractCharBoss$1`、`$sortByNewPrio`、`AbstractCharBoss`、`AbstractBossCard`、
以及 **`Watcher/NewAge/ArchetypeAct1RetainNewAge`、`Act2CalmNewAge`、`Act2StancesNewAge`**。
即：**默认所有牌 `newPrio == 0` → `Collections.sort` 是稳定排序 → 保持插入顺序 = 剧本顺序**；
只有观者的 3 个 archetype 会手动改优先级：
```
// ArchetypeAct1RetainNewAge
theVeryImportantSandsOfTime.newPrio = 1;    // 往后放（后打）
...
theVeryImportantSandsOfTime.newPrio = -1;   // 提前打
```

### 4.2 可移植的 AI 策略（贪心 + 首个可支付）

```
每回合结束（玩家侧）：
   hand := archetype.getThisTurnCards()        // 剧本给的 2~3 张牌
   sort(hand, by newPrio asc)                  // 实际等价于保持剧本顺序
   模拟整回合，给"付得起"的牌 createIntent + lighten   // 意图预览 = 整回合模拟结果

Boss 回合：
   repeat:
       for c in hand (按上面顺序):
           if c.canUse(player, boss) && c.getPriority(hand) > -100:
               play(c); goto repeat             // ← 一次一张，之后重新从头扫
       break                                    // 没有任何牌可打 → 结束回合
   triggerEndOfTurnOrbs()
```

`AbstractBossCard.canUse(AbstractPlayer p, AbstractMonster m)`（m 必须是 `AbstractCharBoss`）判定顺序：
```
1. m instanceof AbstractCharBoss 否则 false
2. STATUS 且 costForTurn < -1 且 boss.hasRelic("Medical Kit")  → true   （医疗包肯打状态牌）
3. CURSE  且 costForTurn < -1 且 boss.hasRelic("Blue Candle")  → true   （蓝蜡烛肯打诅咒牌）
4. CURSE  且 costForTurn < -1                                  → false
5. STATUS                                                      → false
6. cardPlayable(m) && hasEnoughEnergy()
```
`hasEnoughEnergy()`：遍历 `boss.powers.canPlayCard(this)`、检查 `Entangled`（不能攻击）、`boss.relics.canPlay(this)`、
其它手牌的 `canPlay(this)`（如 Normality 三张限制），最后
`EnemyEnergyPanel.totalCount >= costForTurn || freeToPlay() || isInAutoplay`。

`getPriority(ArrayList<AbstractCard> hand)`：
```java
if (type == STATUS) return statusValue();   // 基础 -5；有 Evolve → 0；有 Medical Kit → +4
return autoPriority();
```
`autoPriority()`（真正决定"值不值得打"）：
```
owner = this.owner;  setupPhase = owner.onSetupTurn;  blockModifier = setupPhase ? 1.5f : 1.0f
value = 0
if (type == STATUS) value -= 10
else if (type == CURSE && costForTurn < -1) value -= 100        // ≈ 永不
if (type == ATTACK) {
    if (owner.currentHealth > owner.maxHealth/2) value += max(damage, 0)
    else                                         value += (int) max(damage * 2f, 0f)   // 残血时更想打
    if (owner instanceof CharBossWatcher && owner.stance instanceof EnDivinityStance) value *= 2
}
value += (int) max(block * 1.3f * blockModifier, 0f)            // 格挡 ×1.3，布置回合 ×1.5
if (type == POWER || color == COLORLESS) {
    value = (owner.currentHealth > maxHealth/2) ? value * 5 : value / 2   // 满血优先上 power
}
if (isEthereal) value *= 2
return value
```
**注意**：`makePlay()` 的门槛是 `getPriority(...) > -100`，所以这个 value **只用来做"要不要打"的粗筛**
（负值且 ≤ -100 的诅咒直接跳过），**真正的出牌顺序由剧本书写顺序决定**，不是按 value 排序。

### 4.3 意图预览 = 整回合模拟

`AbstractCharBoss.preApplyIntentCalculations()`（`dcb-AbstractCharBoss-full.txt:821-1282`）
在 `applyPowers()` 里被调用，作用是**顺着手的顺序模拟整回合**，以便意图上显示的数字是"打完整轮之后"的数字：
```
// 先清零每张牌的 manualCustomDamageModifier / Mult
for (c : hand) c2.manualCustomDamageModifier = 0; c2.manualCustomDamageModifierMult = 1f
// 遍历手牌（顺序 = 剧本顺序），只有 !lockIntentValues 的牌参与
playerArtifact = player.hasPower("Artifact") ? player.getPower("Artifact").amount : 0
for (i = 0; i < hand.size(); i++) {
    c = hand.get(i)
    if (c.lockIntentValues) continue
    if (c.artifactConsumedIfPlayed > 0) playerArtifact -= c.artifactConsumedIfPlayed
    if (c.vulnGeneratedIfPlayed > 0 && playerArtifact <= 0)
        for (后续的牌) c2.manualCustomVulnModifier = true          // 之后的攻击牌伤害 ×1.5
    ... 同样处理 selfWeak / strength / focus / clawDamage / damageMult / block 产出 ...
    if (c.type == ATTACK) { j++;                                        // 计攻击数
        if (hasRelic("Pen Nib") && penNib.counter + j == 10) c.manualCustomDamageModifierMult = 2f }
}
// 最后：所有没变暗的牌 createIntent()
for (c : hand) if (!((AbstractBossCard)c).bossDarkened) ((AbstractBossCard)c).createIntent()
```
`AbstractBossCard` 为此在构造函数里预留了一整组"打出会产出什么"的元数据字段：
```
energyGeneratedIfPlayed, strengthGeneratedIfPlayed, damageMultGeneratedIfPlayed,
focusGeneratedIfPlayed, vulnGeneratedIfPlayed, selfWeakIfPlayed,
artifactConsumedIfPlayed, blockGeneratedIfPlayed, clawDamageGeneratedIfPlayed,
manualCustomDamageModifier, manualCustomDamageModifierMult,
manualCustomVulnModifier, manualCustomSelfWeakModifier
```
——**这些字段必须由每张牌/每个 archetype 手工填写**，否则意图会算错。这是移植时很大的一块工作量。

---

## 5. 遗物

> 本节结论来自 `.recon/downfall/findings-relics.md` 的完整侦察（含全 jar 常量池字节扫描），
> 关键转储：`dcb-relic-*.txt`。

### 5.1 有没有专门的基类？——有：`AbstractCharbossRelic`

```java
public class charbosses.relics.AbstractCharbossRelic
        extends com.megacrit.cardcrawl.relics.AbstractRelic {   // ← 是原版 AbstractRelic 的直接子类
  public AbstractCharBoss owner;      // ← mod 自己的字段（原版 AbstractRelic 里没有 owner）
  public static final float START_X, START_Y, PAD_X;
  public AbstractCharbossRelic(AbstractRelic baseRelic);                              // 从原版遗物实例复制
  public AbstractCharbossRelic(String id, RelicTier tier, LandingSound sound, Texture img);
  public void instantObtain(AbstractCharBoss p);
  public void instantObtain(AbstractCharBoss p, int slot, boolean callOnEquip);
  public void reorganizeObtain(AbstractCharBoss p, int slot, boolean callOnEquip, int relicAmount);
  public void obtain();  public void obtain(AbstractCharBoss p);
  public void render(SpriteBatch sb);   // → renderInTopPanel(sb)
  public boolean canSpawn()   { return false; }
  public void playLandingSFX() { }      // 空
  public void onEnergyRecharge() { }    // 空
  public void updateBosscharRelicPopupClick();
  public int getColumn();
}
```
**它自己不覆写任何战斗钩子**：`atBattleStart` / `atTurnStart` / `onUseCard` / `onTrigger` / `canPlay` … 全部直接继承原版 `AbstractRelic`；
覆写由 123 个具体 `CBR_*` 子类各自完成。

**最重要的设计决策：复用原版遗物实例。**
`CBR_*` 的构造器几乎都是 `super(new com.megacrit.cardcrawl.relics.XxxRelic())`，
所以 **`relicId` 就是原版 ID**（`"Burning Blood"`、`"Ice Cream"`…），贴图/tier/音效也一并继承。
于是 `boss.hasRelic("Art of War")` 这种**字符串判定在 Downfall 里是普遍用法**（不是好设计，但要知道）。

### 5.2 遗物怎么挂到 Boss 身上

唯一的挂载路径：
```java
// AbstractBossDeckArchetype
public void addRelic(AbstractCharbossRelic r) {
    r.instantObtain(AbstractCharBoss.boss);
}
```
```java
// AbstractCharbossRelic.instantObtain(AbstractCharBoss p)
 0-2: this.owner = p                                  // owner 在这里才被赋值（构造函数不设！）
 5-6: instantObtain()                                 // 无 slot 版本
```
```java
// AbstractCharbossRelic.instantObtain()  逐条
 0: if (owner == null) { logger.info("OWNER IS NULL!"); return; }
18: if (relicId.equals("Circlet") && owner.hasRelic("Circlet")) { counter++; flash(); return; }
68: isDone = true; isObtained = true;
78: currentX = Settings.WIDTH - START_X - owner.relics.size() * PAD_X;   // ★ 镜像到右上角
113: targetX = currentX; targetY = currentY;
129: flash();
133: owner.relics.add(this);                            // ★ 唯一的"持有"动作
145: hb.move(currentX, currentY);
160: onEquip();
164: relicTip();
```
**它完全不进原版玩家遗物系统**：不调 `relicObtainLogic()`、不调 `AbstractDungeon.getCurrRoom().spawnRelicAndObtain()`、
不碰 `AbstractDungeon.player.relics`（对该转储全文检索这些符号 0 命中）。
`boss.relics` 是 `AbstractCharBoss` 自己声明的 `ArrayList<AbstractCharbossRelic>`。

`obtain()` / `obtain(boss)`（带滑入动画的版本）**在本 jar 内无调用者**——`addRelic` 只走 `instantObtain`。

### 5.3 钩子怎么触发：手工镜像玩家的 30 个触发点

`AbstractCharBoss` 里手写了一整套"玩家式"的遗物转发方法（见 §1.5），
**一共 30 个原版 `AbstractRelic` 钩子被转发**，另有 8 个 boss 侧类补了转发点：

| Boss 侧位置 | 转发到的遗物钩子 |
|---|---|
| `preBattlePrep() → applyPreCombatLogic()` | `atPreBattle()` |
| `usePreBattleAction()` | `atBattleStartPreDraw()`、`atBattleStart()` |
| `startTurn() → applyStartOfTurnRelics()` | `atTurnStart()`（另加 `stance.atStartOfTurn()`） |
| `endTurnStartTurn() → applyStartOfTurnPostDrawRelics()` | `atTurnStartPostDraw()` |
| `onCardDrawOrDiscard()` | `onDrawOrDiscard()` |
| `damage(DamageInfo)` | `onAttackToChangeDamage`（×2，自伤/被打分路）、`onAttackedToChangeDamage`、`onAttack`（×2）、`onAttacked`、`onLoseHpLast`、`wasHPLost`、`onLoseHp`、`onBloodied`、`onTrigger` |
| `heal(int)` | `onPlayerHeal(int)`、`onNotBloodied()` |
| `addBlock(int)` | `onPlayerGainedBlock(float)` |
| `update()` / `render()` | `update()`、`render()` |
| `relicsDoneAnimating()` | 读 `isDone` |
| `charbosses.actions.common.EnemyUseCardAction` **构造函数** | `onUseCard(card, makeNormalCardAction())`（受 `dontTriggerOnUseCard` 保护；偏移 122→163） |
| `charbosses.cards.EnemyCardGroup.moveToExhaustPile()` | `onExhaust(card)` |
| `charbosses.cards.EnemyCardGroup.refreshHandLayout()` | `onRefreshHand()` |
| `charbosses.cards.AbstractBossCard.applyPowers()/calculateDamage()` | `atDamageModify(float, card)` |
| `charbosses.cards.AbstractBossCard.hasEnoughEnergy()` | `canPlay(card)` |
| `charbosses.actions.unique.EnemyChangeStanceAction.update()` | `onChangeStance(old, new)` |
| `charbosses.actions.orb.EnemyTriggerEndOfTurnOrbActions.update()` | `onPlayerEndTurn()`、`onEnergyRecharge()` |
| `charbosses.patches.BrokeBlockPatch.Prefix(AbstractCreature)` | `onBlockBroken(creature)`（**玩家破防时**广播给 Boss 遗物 → Boss 版 Hand Drill） |
| `charbosses.core.EnemyEnergyManager.recharge()` | `Art of War.onTrigger()`（**硬编码点名**） |
| 各 `charbosses.cards.*` 的 `use()` | 手动 `boss.getRelic("X").onTrigger()`（如 `EnBash`/`EnThunderclap` → `Champion Belt`） |
| `AbstractCharbossRelic.instantObtain*` | `onEquip()`、`relicTip()` |

**不可达的钩子（覆写了也永远不会被调用）**——这是移植时的重要教训：
- `onVictory` —— 被 9 个类覆写（`CBR_CaptainsWheel` / `CBR_CentennialPuzzle` / `CBR_HornCleat` / `CBR_Kunai` /
  `CBR_LetterOpener` / `CBR_Orichalcum` / `CBR_RedSkull` / `CBR_Shuriken` / `CBR_StoneCalendar`）；
  **`AbstractCharBoss.die()` 里也没有遗物循环** → Boss 在战斗结束时收不到任何遗物回调。
- `onUnequip`（8 个类）、`justEnteredRoom`（3 个类）、`usedUp`（2 个类）、
  以及 `onShuffle / onRest / onSmith / onEnterRoom / onMonsterDeath / onObtainCard / onUsePotion / ...`。

### 5.4 三个具象例子

**① `CBR_BurningBlood`（战斗结束回血）——实际上完全没有效果**
```java
public class CBR_BurningBlood extends AbstractCharbossRelic {
  public static final String ID = "Burning Blood";
  public CBR_BurningBlood() { super(new com.megacrit.cardcrawl.relics.BurningBlood()); }
  public String getUpdatedDescription();     // DESCRIPTIONS[0] + 6 + DESCRIPTIONS[1]
  public AbstractRelic makeCopy();
}
```
全类只有 3 个方法、零钩子覆写；原版 `BurningBlood.onVictory()` 里写死 `AbstractDungeon.player.heal(6)`
（**只会治玩家，从不治遗物持有者**），而 `onVictory` 又不在转发集合里。
全 jar 字节扫描："`Burning Blood`" 只出现在这个类自己身上 → **纯展示遗物（UI + tooltip）**。
→ 凡是只依赖 `onVictory` / `onUnequip` / `justEnteredRoom` 的 `CBR_*`，都是"图标装饰"。

**② `CBR_ArtOfWar`（Boss 版能量遗物）——被 `EnemyEnergyManager` 硬编码点名**
```java
public class CBR_ArtOfWar extends AbstractCharbossRelic {
  private boolean gainEnergyNext, firstTurn;
  public void atPreBattle() { flash(); firstTurn = true; gainEnergyNext = true; }
  public void onTrigger() {
      if (gainEnergyNext && !firstTurn) {
          flash();
          addToBot(new RelicAboveCreatureAction(this.owner, this));
          addToBot(new EnemyGainEnergyAction(1));
      }
      firstTurn = false; gainEnergyNext = true;
  }
  public void onUseCard(AbstractCard card, UseCardAction action) {
      if (card.type == ATTACK) gainEnergyNext = false;      // 打了攻击牌就取消下一回合的能量
  }
}
```
触发链（**注意 `onTrigger` 不是通用钩子**）：
```
EnemyEnergyManager.recharge():
   6: if (boss.hasRelic("Art of War")) boss.getRelic("Art of War").onTrigger();
```
原版 `ArtOfWar` 用的是 `atPreBattle + atTurnStart + onUseCard`；**Boss 版把 `atTurnStart` 换成了 `onTrigger`**，
因为 Boss 的能量刷新路径是 `EnemyEnergyManager.recharge()`，而不是原版 `EnergyManager.recharge()`。
`atPreBattle` 由 `applyPreCombatLogic()` 转发，`onUseCard` 由 `EnemyUseCardAction` 转发，
能量本身经 `EnemyGainEnergyAction` → `boss.gainEnergy()` → `EnemyEnergyPanel.addEnergy()`。
（`AbstractRelic.addToBot(a)` 就是 `AbstractDungeon.actionManager.addToBottom(a)`。）

**③ `CBR_ChampionsBelt` / `CBR_IceCream`——"作用在玩家身上"的标准写法**
```java
// CBR_ChampionsBelt.onTrigger()
 0: addToBot(new RelicAboveCreatureAction(AbstractCharBoss.boss, this));
15: addToBot(new ApplyPowerAction(
        AbstractDungeon.player,                      // target = 玩家
        AbstractCharBoss.boss,                       // source = Boss
        new WeakPower(AbstractDungeon.player, 1, true), 1));
```
由 Boss 的**卡牌**触发（`EnBash` / `EnThunderclap`）：
```java
// EnBash.use(p, m) 尾部
if (!p.hasPower("Artifact") && AbstractCharBoss.boss.hasRelic("Champion Belt"))
    AbstractCharBoss.boss.getRelic("Champion Belt").onTrigger();
```
`CBR_IceCream` 本身是**零钩子的空壳**（原版 Ice Cream 也是），效果写在
`EnemyEnergyManager.recharge()` 里按 `hasRelic("Ice Cream")` 字符串判定：
```
28: if (boss.hasRelic("Ice Cream")) {
39:    if (EnemyEnergyPanel.totalCount > 0) { getRelic("Ice Cream").flash();
56:        actionManager.addToTop(new RelicAboveCreatureAction(boss, getRelic("Ice Cream"))); }
80:    EnemyEnergyPanel.addEnergy(energy);          // 累积保留
     } else EnemyEnergyPanel.setEnergy(energy);    // 覆盖重置
```
> 两个坑：
> (a) `CBR_IceCream.ID = "IceCream"`（无空格），而实际生效的 `relicId` 是原版的 `"Ice Cream"`（有空格）
> —— **不要依赖 `CBR_*` 类里的 `ID` 常量**，要用原版英文 ID 做 `hasRelic(...)`。
> (b) Art of War 走 `addToBot`（bottom），Ice Cream 走 `addToTop`（top），队列顺序不同。

**"遗物作用于玩家"的通用模式**（全部经 `actionManager`，没有任何遗物直接改 UI 或直接改玩家字段）：
| 目的 | 写法 |
|---|---|
| 给玩家上状态 | `addToBot(new ApplyPowerAction(AbstractDungeon.player, boss, new XxxPower(player, n, true), n))` |
| 直接伤害/治疗玩家 | `addToBot(new DamageAction(AbstractDungeon.player, new DamageInfo(boss, dmg, type), effect))` |
| 给 Boss 加能量/加牌 | `charbosses.actions.common.*`（`EnemyGainEnergyAction` / `EnemyMakeTempCardInHandAction` / …） |
| 视觉反馈 | `new RelicAboveCreatureAction(<creature>, relic)` |

### 5.5 UI 上怎么显示

```java
// AbstractCharBoss.render(SpriteBatch)
 0: super.render(sb)                       // AbstractMonster.render
12: renderHand(sb)                         // 只画手牌 / limbo / cardInUse
17: stance.render(sb)
25: for (AbstractRelic r : relics) r.render(sb);      // ★ 遗物在这里画
60: for (AbstractOrb o : orbs) o.render(sb)
105: energyPanel.render(sb)
```
`AbstractCharbossRelic.render` → `renderInTopPanel(sb)`，位置常量：
```
START_X = 364.0f * Settings.scale
START_Y = Settings.HEIGHT - 174.0f * Settings.scale
PAD_X   = 72.0f * Settings.scale
currentX = Settings.WIDTH - START_X - owner.relics.size() * PAD_X    // ← 玩家遗物栏的镜像 → 屏幕右上角
```
即**与玩家遗物行同一 Y 坐标（`HEIGHT-174*scale`），但从右往左排**。
`charbosses/ui/` 里**没有**专门的遗物渲染类（只有 `EnemyEnergyPanel`(+`$1`)、`EnergyOrbHermit`）；
遗物渲染完全复用原版 `AbstractRelic.render` + 坐标覆盖。
`getRelicNames()` 返回 `relicId` 列表，但**全 jar 无调用者**（推测是留给外部/存档的公开 API）。

### 5.6 `EventRelics/`（34 个）是什么

| | 普通 `charbosses.relics.CBR_*`（123 个） | `charbosses.relics.EventRelics.CBR_*`（34 个） |
|---|---|---|
| 构造器 | `super(new 原版遗物实例)` | `super(ID, tier, sound, texture)`（自建纹理） |
| `relicId` | = 原版遗物 ID（`"Burning Blood"`…） | `downfallMod.makeID("AncientWriting")` → **`"downfall:AncientWriting"`** |
| tier / 音效 | 复制原版 | 固定 `RelicTier.SPECIAL` + `LandingSound.MAGICAL` |
| 贴图 | 复用原版 png | 统一占位图 `images/relics/backtobasics.png`，`largeImg = null` |
| 语义 | "Boss 带着这件原版遗物" | **"Boss 在这个事件里做出了什么选择 / 拿到了什么"**（事件履历标记） |

例：`CBR_AncientWriting` 的 `ID = downfallMod.makeID("AncientWriting")`；`CBR_BigFish` 还带
`public void box(ArrayList<AbstractBossCard>)`（往 Boss 卡组里塞 `EnRegret` 诅咒）和动态描述
（`DESCRIPTIONS[0]` = 选甜甜圈 → +5 最大生命；`DESCRIPTIONS[1]` = 选箱子 → 一个 Regret 诅咒 + 一件遗物）。

**现状**：3 次独立字节扫描确认 —— **本 jar 中没有任何类实例化或引用它们**，
连 `"charbosses.relics.EventRelics.CBR_" + id` 这种拼接都不存在。它们需要"传参的调用方"
（`new CBR_Augmenter(int)`、`CBR_BigFish.box(list)`），那个调用方不在这个 jar 里
（可能属于附属 mod 或被裁剪）。**移植时可以完全跳过这一块。**

### 5.7 遗物部分的移植要点

1. **不要指望 `onVictory` / `onUnequip` / `justEnteredRoom`**：Boss 在战斗结束时不会收到任何遗物回调。
2. 想让 Boss 遗物"打到玩家身上"，必须**自己显式调用**（`boss.getRelic("X").onTrigger()` 或
   `ApplyPowerAction(player, boss, power)`），或走 §5.3 的 8 个 boss 侧转发类。
3. 遗物 ID 沿用原版 ID（`super(new XxxRelic())` 构造器），所以 `hasRelic("<原版英文 ID>")` 可用；
   但**别信 `CBR_*` 类里的 `ID` 常量**（已发现不一致）。
4. **"Boss 遗物挂载"必然发生在 `boss.init() → preBattlePrep()` 期间**
   （`applyPreCombatLogic()` 在那里触发 `atPreBattle`），标准位置是 archetype 的
   `initialize()` / `initializeBonusRelic()`。StS2 版需要设计一个等价的"遗物挂载时机"。

---

## 6. 插入到每一幕

### 6.1 按幕选择 archetype：`CharBossIronclad.generateDeck()`

这是**唯一**的"按幕配置"实现点，每个 `CharBoss*` 子类各写一份
（`dcb-CharBossIronclad-full2.txt:95`，`dcb-acts-CharBossIronclad-full.txt`）：

```java
public void generateDeck() {
  if (downfallMod.overrideBossDifficulty) {                  // 调试/特殊模式
      a = new ArchetypeAct1StatusesNewAge();
      if (!downfallMod.useLegacyBosses) a = new ArchetypeAct1StatusesSimple();
      downfallMod.overrideBossDifficulty = false;
      currentHealth -= 100;
  } else if (!downfallMod.useLegacyBosses) {                 // ← Simpler 一套
      switch (AbstractDungeon.actNum) {
        case 1:  a = new ArchetypeAct1StatusesSimple();   break;
        case 2:  a = new ArchetypeAct2MushroomsSimple();  break;
        case 3:  a = new ArchetypeAct3BlockSimple();      break;
        default: a = new ArchetypeAct1StatusesSimple();
      }
  } else {                                                   // ← NewAge 一套（useLegacyBosses 默认 true）
      switch (AbstractDungeon.actNum) {
        case 1:  a = new ArchetypeAct1StatusesNewAge();   break;
        case 2:  a = new ArchetypeAct2MushroomsNewAge();  break;
        case 3:  a = new ArchetypeAct3BlockNewAge();      break;
        default: a = new ArchetypeAct1StatusesNewAge();
      }
  }
  a.initialize();                                     // 挂遗物
  this.chosenArchetype = a;
  if (AbstractDungeon.ascensionLevel >= 19) a.initializeBonusRelic();   // ★ A19+ 额外遗物
}
```

**要点：**
- 用 `AbstractDungeon.actNum`（1/2/3）`tableswitch` 直接 new 对应的 archetype 类，
  **没有注册表、没有工厂、没有配置表**——就是硬编码的 switch。
- `downfallMod.useLegacyBosses` 默认 **`true`**（三处证据：`downfall.downfallMod.<clinit>` 的 `iconst_1; putstatic useLegacyBosses`；
  `initializeConfig()` 里设置开关默认值时的 `iconst_1; putstatic`；`configDefault.setProperty("legacyBosses","TRUE")`），
  并且它确实出现在 Mod 设置界面（`ModLabeledToggleButton`）。
  **`true` → NewAge + 2 能量；`false` → Simpler + 3 能量。**
- **命名反转（重要）**：配置文案是 `downfallResources/localization/eng/UIStrings.json` 里的
  **`"Use Legacy Downfall Bosses (pre-6.0)"`**。也就是说 **`NewAge` ≠ "新"** ——
  按字面解释，`true`（默认）走的是"6.0 之前的旧设计"（`NewAge/` 类），
  `false` 走的是新版简化设计（`Simpler/` 类）。
  字节码层面的分支方向是确定的（`ifne → NewAge`）；上述历史解释是**推断**，请以实际类内容为准（见 §8）。
- 该开关的全部读取点（10 个类）：5 个 `CharBoss*.generateDeck()`、`AbstractCharBoss.init()`（能量 2/3）、
  `AbstractCharBoss.generateAll()`（A9 血量算法）、`AbstractCharBoss.applyEndOfTurnTriggers()`（是否回合末弃手）、
  `AbstractCharBoss.die()`（是否重置机制面板）、`AbstractBossDeckArchetype.addedPreBattle()`（是否显示机制面板）、
  以及 `IroncladMushroomPower` / `Fortification` / `EnemyPoisonDamageAction` 的文案/数值差异。
- `downfallMod.overrideBossDifficulty` 默认 `false`，是一个一次性开关（用掉即置回 false），用于强制 Act1 archetype 且 `currentHealth -= 100`。
- 「按幕配置」的表达方式 = **一整套 archetype 类的命名约定 `ArchetypeAct{N}{主题}{NewAge|Simpler}`**，
  每个类在构造函数里声明自己的 `actNum` / `maxHPModifier` / `bossMechanicName/Desc`。

### 6.2 进阶（Ascension）如何影响 Boss

扫描 `charbosses/` 全部 750 个顶层类（转储 `dcb-acts-ascension-all.txt`），共 4 条硬阈值：

| 阈值 | 位置 | 规则 |
|---|---|---|
| **A4** | 各 archetype 的 `initialize()` / `getThisTurnCards()` | `extraUpgrades = (ascensionLevel >= 4)`（`if_icmplt`），用于把剧本里的某些牌换成升级版：`addToList(list, card, extraUpgrades)` → 内部 `card.upgrade()` |
| **A9** | `AbstractCharBoss.generateAll()` | `maxHealth = baseMaxHealth + archetype.maxHPModifier;`<br>`if (ascensionLevel >= 9) { useLegacyBosses ? maxHealth = round(maxHealth * 1.2f) : maxHealth += archetype.maxHPModifierAsc; }`<br>**`maxHPModifierAsc` 只有 `Simpler` archetype 定义**（NewAge 全为 0），且 **Act1 = +10 / Act2 = +20 / Act3 = +30**（17 个 Simpler 类一致） |
| **A9（小怪）** | `LouseTangerine.<init>()` / `usePreBattleAction()` / `takeTurn()`；`BronzeOrb...<init>()` | A9+：`biteDamage = 8`（否则 6）、`CurlUpPower(12)`（否则 8）、`StrengthPower(4)`（否则 3）、青铜球加血（`54`） |
| **A19** | 5 个 `CharBoss*.generateDeck()` 末尾 | `if (ascensionLevel >= 19) archetype.initializeBonusRelic();`（额外遗物） |
| **A19（额外遗物/强化）** | 部分 archetype 的 `initialize()` | 例：`ArchetypeAct1VoidsSimple.initialize()` 先 `addRelic(new CBR_Vajra())`，再 `if (ascensionLevel >= 19) addRelic(new CBR_SymbioticVirus())`（同类还有 10 个 archetype） |
| **A19（power）** | `SilentDelayedWraithPower.onLoseHp()` 等 | A19 → `IntangiblePlayerPower(3)` + `HealAction(15)`；否则 `(2)` + `HealAction(10)` |
| **A>20** | `receiveStartAct()` | `ascensionLevel > 20 && isAscensionMode && actNum == 3` → `bossList.add(1, CharBossMerchant.ID)`（Downfall 扩展进阶） |

**Boss 的基础血量**（`AbstractCharBoss` 构造函数第 3 个参数 `maxHealth`，
例如 `CharBossIronclad` = `bipush 80`）**加上 archetype 的 `maxHPModifier`**：
Ironclad 60/190/300、Silent 100/240/350、Defect 102/192/280/330、Watcher 98/198/160/300/380、Hermit 95/195/363（NewAge）；
Simpler 版：Ironclad 90/190/300、Silent 100/240/350、Defect 75/195/295、Watcher 138/198/348、Hermit 88/198/368。

### 6.3 替换原版 Boss 遭遇：`downfallMod.receiveStartAct()` + 反射 `setBoss`

> 本节把「Downfall 侧」和「原版侧」都补齐了。
> 原版对照 jar：`D:\SteamLibrary\steamapps\common\SlayTheSpire\desktop-1.0.jar`
> （注意路径是 **`SlayTheSpire`（无空格）**，不是 `Slay the Spire`）
> 转储：`dcb-vanilla-AbstractDungeon-full.txt`、`dcb-vanilla-MonsterRoomBoss.txt`、`dcb-vanilla-dungeons-getBoss.txt`

**① 原版侧：Boss 是怎么被决定的（三段链路，全部有字节码）**

```java
// com.megacrit.cardcrawl.dungeons.AbstractDungeon
public static String bossKey;      // 静态字段
public static ArrayList<String> bossList;

private void setBoss(String key) {                          // ← private！
    bossKey = key;                                          // 0: aload_1 / 1: putstatic bossKey
    if (DungeonMap.boss != null && DungeonMap.bossOutline != null) { /* dispose 旧贴图 */ }
    if      (key.equals("The Guardian")) { DungeonMap.boss = loadImage("images/ui/map/boss/guardian.png"); ... }
    else if (key.equals("Hexaghost"))    { ... }
    ...  // 原版每个 Boss 一条分支
    else if (key.equals("The Heart"))    { ... images/ui/map/boss/heart.png ... }
    else {
        logger.info("WARNING: UNKNOWN BOSS ICON: " + key);   // ← 未知 key
        DungeonMap.boss = null;                              // ← 图标置空
    }
    logger.info("[BOSS]" + key);
}

public MonsterGroup getBoss() {
    lastCombatMetricKey = bossKey;                           // 0: getstatic bossKey / 3: putstatic lastCombatMetricKey
    dungeonMapScreen.map.atBoss = true;
    return MonsterHelper.getEncounter(bossKey);              // 16→19 ★ bossKey → MonsterGroup
}
```
```java
// com.megacrit.cardcrawl.rooms.MonsterRoomBoss.onPlayerEntry()
 0: monsters = CardCrawlGame.dungeon.getBoss();               // ← 唯一的遭遇来源
10: logger.info("BOSSES:" + AbstractDungeon.bossList.size());
54: CardCrawlGame.music.silenceBGM();
60: AbstractDungeon.bossList.remove(0);                        // ← bossList 只表示"还剩几场 Boss"
68: if (monsters != null) monsters.init();
```
**结论（这是 Q6 最关键的一条）**：
> 原版**没有** `getBossList()` 这种"Boss 池方法"可供 patch。
> `bossList` **只用于"这一幕还有几场 Boss 战"**（每次进 Boss 房 `remove(0)`），
> 真正决定打谁的是**静态字段 `bossKey`**，它经 `MonsterHelper.getEncounter(bossKey)`（BaseMod 的 `addMonster` 注册表）解析成 `MonsterGroup`。
> `setBoss(key)` 只做两件事：**写 `bossKey`** + **设地图上的 Boss 图标**（未知 key 时图标= null 并打警告）。
>
> 因此 Downfall 的整套做法是：**用反射写 `bossKey`，再用 `Postfix` 补上地图图标。**

**② Downfall 侧：挑人 + 注入**

```java
// downfall.downfallMod
public static ArrayList<String> possEncounterList;        // ← Boss 池（5 个 CharBoss ID）
public static String Act1BossFaced, Act2BossFaced, Act3BossFaced;   // 存档/成就记录

public static void resetBossList() {
    possEncounterList.clear();
    possEncounterList.add(CharBossIronclad.ID);   // "downfall:Ironclad"
    possEncounterList.add(CharBossSilent.ID);     // "downfall:Silent"
    possEncounterList.add(CharBossDefect.ID);     // "downfall:Defect"
    possEncounterList.add(CharBossWatcher.ID);    // "downfall:Watcher"
    possEncounterList.add(CharBossHermit.ID);     // "downfall:Hermit"
}
```
```java
// downfall.downfallMod.receiveStartAct()   —— 每一幕开始时跑（BaseMod 的 StartActSubscriber）
 0: neowtextoverride = false
 4: if (EvilModeCharacterSelect.evilMode
 7:     || (evilWithinSingleton != null && evilWithinSingleton.selected)
25:     || (CardCrawlGame.trial == null && DailyModeEvilPatch.todaysRunIsEvil)) {
37:      if (possEncounterList.size() == 0) resetBossList();
49:      if (AbstractDungeon.actNum <= 3) {
56:          AbstractDungeon.bossKey = possEncounterList.remove(
                 AbstractDungeon.cardRandomRng.random(possEncounterList.size() - 1));   // ★ 无放回随机抽一个
82:          Method m = AbstractDungeon.class.getDeclaredMethod("setBoss", String.class);  // ← ldc "setBoss"
101:         m.setAccessible(true);
106:         m.invoke(CardCrawlGame.dungeon, AbstractDungeon.bossKey);                     // ★ 反射调用原版私有方法
         }
132:     if (AbstractDungeon.ascensionLevel > 20 && AbstractDungeon.isAscensionMode
146:         && AbstractDungeon.actNum == 3)
153:         AbstractDungeon.bossList.add(1, CharBossMerchant.ID);                         // A20+ 第三幕插入商人
     }
163: if (player.hasRelic(NeowBlessing.ID)) player.increaseMaxHp(100, true)
```

**③ 注册**（`downfallMod.initializeMonsters()`，偏移 453–469 及 lambda）：
```java
BaseMod.addMonster(CharBossIronclad.ID, runHistoryName,
                   () -> new CharBossMonsterGroup(new AbstractMonster[]{ new CharBossIronclad() }));
// 同样注册 Silent / Defect / Watcher / Hermit / CharBossMerchant / NeowBossFinal
// lambda 字节码：10: new CharBossIronclad ; 18: new CharBossMonsterGroup([AbstractMonster])
```

**④ 真正存在的补丁类只有两个**

| 补丁类 | `@SpirePatch` 目标 | 作用 |
|---|---|---|
| `downfall.patches.BossSetPatch` | `clz=AbstractDungeon, method="setBoss"`（`@SpirePostfixPatch`） | `Postfix(AbstractDungeon __instance, String key)`（有 `__instance` → 反证 `setBoss` 是**实例方法**）：<br>① evilMode 下 `monsterList.replaceAll`（`lambda$Postfix$0`：`"Looter"`→`downfall:LooterAlt`、`"2 Thieves"`→`downfall:LooterAlt2`）；<br>② `__instance instanceof TheEnding && evilMode` → 清空 `bossList` 填 3×`NeowBossFinal.ID`、清空 `monsterList`/`eliteMonsterList` 各填 3×`NeowBoss.ID`、`bossKey = NeowBossFinal.ID`；<br>③ **按 `key` × 地牢类型补地图图标**（`TextureLoader.getTexture("downfallResources/images/ui/map/icon/...")` → `putstatic DungeonMap.boss / bossOutline`）。**没有 ③ 的话地图 Boss 图标就是空的**（原版对未知 key 会 `WARNING: UNKNOWN BOSS ICON` + 置 null） |
| `downfall.patches.ProceedButtonPatch2` | `clz=ProceedButton, method="goToDoubleBoss"`（`@SpirePrefixPatch`） | `Prefix(ProceedButton __instance)`：清空 `bossList` 并加 `CharBossMerchant.ID`；`bossKey = CharBossMerchant.ID`；fadeOut BGM；`new MapRoomNode(-1, 15)` + `new MonsterRoomBoss()` 设为 `AbstractDungeon.nextRoom`；`closeCurrentScreen()` + `nextRoomTransitionStart()` + `hide()`；返回 `SpireReturn.Return(null)`。**即把原版「双 Boss（Spear/Shield → Heart）」入口换成一次商人 Boss 战** |

**⚠ 澄清：`charbosses/patches/` 里一个「Boss 池/幕/房间」补丁都没有** —— 16 个类全是卡面渲染与玩家侧 power 修复（见 §3.7）。
另需排除的近似项：`downfall.patches.ExordiumThugsPatch` 打的是 `MonsterHelper.bottomHumanoid`（第 1 幕**普通遭遇**，不是 Boss）；
`downfall.patches.MonstersAreDeadPatch$CurrMapNodeSteal` 打 `MonsterGroup.areMonstersBasicallyDead`；
`downfall/patches/rooms/*`（`BetterEndingPatches`、`ShopRoomReplacePatch`）是结局/商店房间，与 Boss 池无关。

**⑤ 要点汇总**

1. **触发条件**：只有在 **Evil Mode**（Downfall 核心玩法：扮演反派去打原版角色）或每日挑战邪恶模式下才会替换。
   普通模式下 `charbosses` 完全不介入。
2. **选择机制**：不是按幕查表，而是 `possEncounterList` **无放回随机抽** → 3 幕内不会撞同一角色。
3. **注入方式**：反射写 `bossKey`（而不是改某个"Boss 池"）。
4. **地图图标**：`BossSetPatch` 按 (key × 幕) 换图标：

   | key | `TheBeyond`（第 3 幕） | `TheCity`（第 2 幕） | 其它（`Exordium`，第 1 幕） |
   |---|---|---|---|
   | `downfall:Ironclad` | `ironclad_bastion` | `ironclad_shroom` | `ironclad_status` |
   | `downfall:Silent` | `silent_shiv` | `silent_mirror_image` | `silent_poison` |
   | `downfall:Defect` | `defect_focus` | `defect_bronze_orbs` | `defect_void` |
   | `downfall:Watcher` | `watcher_divinity` | `watcher_blasphemy` | `watcher_wrath` |
   | `downfall:Hermit` | `hermit_curse` | `hermit_wheel` | `hermit_sharpshooter` |
   | `downfall:NeowBossFinal` | `neow`（无地牢判断） | | |

   → **"按幕"不是"三幕三个 Boss 类"**，而是"同一个 `CharBoss` 类因 `actNum` 不同加载不同剧本 + 不同地图图标"。
5. **A20+ 特殊处理**：`ascensionLevel > 20 && isAscensionMode && actNum == 3` → `bossList.add(1, CharBossMerchant.ID)`。
6. **第 4 幕（TheEnding）**：`BossSetPatch` 把 `bossList` 清空填 3 个 `NeowBossFinal.ID`，`bossKey = NeowBossFinal.ID`（Downfall 自己的最终 Boss，不是 CharBoss）。
7. **存档记录**：`downfallMod.saveBossFight(String id)`（唯一调用点 = `AbstractCharBoss.usePreBattleAction()`）：
   `if (getCurrRoom().event == null && !id.equals(CharBossMerchant.ID)) switch (actNum) { 1→Act1BossFaced; 2→Act2BossFaced; 3→Act3BossFaced } = id;`
8. 相关配套类：`downfall.tutorials.CharbossTutorials`（教学）、
   `downfall.util.BossCardReward` / `BossRelicReward`（**击败 CharBoss 后给玩家它的卡/遗物作为奖励**）、
   `charbosses.BossMechanicDisplayPanel`（左侧 Boss 机制提示框）、
   `downfall.events.BossTester`（调试用，会写 `downfallMod.overrideBossDifficulty = true`）。

### 6.4 Boss + 小怪的组队

**① Boss 本体永远是"单人组"**
`charbosses.actions.util.CharBossMonsterGroup extends MonsterGroup`，只被 `downfallMod.initializeMonsters()` 的 7 个
lambda 用**单元素数组**构造（`iconst_1 anewarray`）。它唯一的自有逻辑是 `applyPreTurnLogic()`：
```java
for (AbstractMonster m : monsters) {
    if (m instanceof AbstractCharBoss) {
        for (AbstractPower p : m.powers)
            if (p instanceof AbstractBossMechanicPower) ((AbstractBossMechanicPower)p).PreRoundLoseBlock();
        if (!m.hasPower("Barricade") && !m.hasPower("Blur"))
            cB.hasRelic("Calipers") ? m.loseBlock(15) : m.loseBlock();     // 格挡衰减（Calipers 只掉 15）
        if (NeowBoss.neowboss == null || NeowBoss.neowboss.offscreen) m.applyStartOfTurnPowers();
    } else m.applyStartOfTurnPowers();
}
```

**② 小怪由 archetype 的开场钩子生成**（`AbstractCharBoss.usePreBattleAction()` → `chosenArchetype.addedPreBattle()`）：

| Archetype | 开场小怪 | 证据 |
|---|---|---|
| `Defect/*/ArchetypeAct1Voids*` | `charbosses.monsters.VoidCore` | `90: new VoidCore` → `97: new SpawnMonsterAction` → `102: addToBottom` |
| `Defect/NewAge/ArchetypeAct2ClawNewAge` | `BronzeOrbWhoReallyLikesDefectForSomeReason` | 同上 |
| `Hermit/*/ArchetypeAct1Sharpshooter*` | `LouseTangerine` | `60: new LouseTangerine` |
| `Ironclad/*/ArchetypeAct3Block*` | `Fortification` | `61: new Fortification` |
| `Ironclad/*/ArchetypeAct2Mushrooms*` | **开场不生成**，只上 `IroncladMushroomPower(owner,1)` | `17: new IroncladMushroomPower` |
| `Silent/*/ArchetypeAct2MirrorImage*` | **开场不生成**，只上 `SilentMirrorImagePower` | `50: new SilentMirrorImagePower` |

`SpawnMonsterAction(monster, true)` 会把怪追加进 `AbstractDungeon.getCurrRoom().monsters.monsters`。
小怪实体都在 `charbosses/monsters/`：`MushroomRed/White/Purple`、`MirrorImageSilent`、`VoidCore`、
`BronzeOrbWhoReallyLikesDefectForSomeReason`、`LouseTangerine`、`Fortification`。

**③ 战斗中召唤**
- **蘑菇**：卡 `charbosses.cards.red.EnSummonMushrooms`（ID `downfall:SummonMushrooms`，`Intent.BUFF`）的 `use()`：
  先遍历场上蘑菇治疗/加力量，再 `new MushroomPurple/Red/White` → `new SpawnMonsterAction`（按场上数量决定召唤组合）。
- **镜像（Silent Act2）**：`charbosses.bosses.Silent.CharBossSilent$1 extends AbstractGameAction` ——
  **全 mod 唯一直接调 `MonsterGroup.addMonster` 的地方**：
  ```
  89: new MirrorImageSilent(FF)
107: new FakeOrRealPower
128/138/148: 复制本体的 currentHealth / maxHealth / currentBlock
188: instanceof basemod.interfaces.CloneablePowerInterface   // 逐条复制 power
228: new MinionPower
248: MonsterGroup.addMonster(int, AbstractMonster)
252: init()   256: applyPowers()
  ```
  `FakeOrRealPower.onAttacked()`：`73: instanceof MirrorImageSilent` → `82: isDead = true`、`88: isDying = true`、
  `94: SmallerSmokeBombEffect`、`122: RemoveSpecificPowerAction`（被打一下就破）。

**④ Boss 死亡清场**（`AbstractCharBoss.damage(DamageInfo)` 致死分支）：
```
1114: die()
1117: AbstractDungeon.getMonsters().areMonstersBasicallyDead()
1126: GameActionManager.cleanCardQueue()
      DeckPoofEffect ×2 + OverlayMenu.hideCombatPanels()
1205: if (currentBlock > 0) loseBlock + HbBlockBrokenEffect
1272: for (m : getCurrRoom().monsters.monsters)
1326:    if (m.hasPower("Minion"))                       // ← 原版 Minion power
1334:        addToTop(new HideHealthBarAction(m))
1349:        addToTop(new SuicideAction(m))
```
→ **Boss 一死，所有带原版 `"Minion"` power 的小怪立刻隐身 + 自杀**，不会拖住战斗。
另外 `CharBossIronclad.die()` 对 `Fortification`/`Mushroom*` 逐个 `InstantKillAction`；
`CharBossSilent` 把 `MirrorImageSilent` 直接置 `isDead = true`。

**⑤ NeowBoss 把 CharBoss 当小怪用**：`downfall.monsters.NeowBossSelector` + `downfall.actions.NeowGainMinionPowersAction`
用 `Act1BossFaced` / `Act2BossFaced` / `Act3BossFaced` → `charbossToGauntlet(String)` →
`downfall/monsters/gauntletbosses/{Ironclad,Silent,Defect,Watcher,Hermit}`（"已经打过的角色"反过来当小怪登场）。

### 6.5 按幕 archetype 对照表（`charbosses/bosses/*/`）

| 角色 | 幕 | `NewAge/` 类 | `Simpler/` 类 |
|---|---|---|---|
| Ironclad | 1 | `ArchetypeAct1StatusesNewAge` | `ArchetypeAct1StatusesSimple` |
| Ironclad | 2 | `ArchetypeAct2MushroomsNewAge` | `ArchetypeAct2MushroomsSimple` |
| Ironclad | 3 | `ArchetypeAct3BlockNewAge` | `ArchetypeAct3BlockSimple` |
| Silent | 1 | `ArchetypeAct1PoisonNewAge` | `ArchetypeAct1PoisonSimple` |
| Silent | 2 | `ArchetypeAct2MirrorImageNewAge` | `ArchetypeAct2MirrorImageSimple` |
| Silent | 3 | `ArchetypeAct3ShivsNewAge` | `ArchetypeAct3ShivsSimple` |
| Defect | 1 | `ArchetypeAct1TurboNewAge` | `ArchetypeAct1VoidsSimple` |
| Defect | 2 | `ArchetypeAct2ClawNewAge` | `ArchetypeAct2ClawSimple` |
| Defect | 3 | `ArchetypeAct3InserterNewAge` / `ArchetypeAct3OrbsNewAge` | `ArchetypeAct3OrbsSimple` |
| Watcher | 1 | `ArchetypeAct1RetainNewAge` | `ArchetypeAct1OmegaSimple` |
| Watcher | 2 | `ArchetypeAct2CalmNewAge` / `ArchetypeAct2StancesNewAge` | `ArchetypeAct2StanceDanceSimple` |
| Watcher | 3 | `ArchetypeAct3DivinityNewAge` / `ArchetypeAct3DualityNewAge` | `ArchetypeAct3DivinitySimple` |
| Hermit | 1 | `ArchetypeAct1SharpshooterNewAge` | `ArchetypeAct1SharpshooterSimple` |
| Hermit | 2 | `ArchetypeAct2WheelOfFateNewAge` | `ArchetypeAct2WheelOfFateSimple` |
| Hermit | 3 | `ArchetypeAct3DoomsdayNewAge` | `ArchetypeAct3BasicsSimple` |
| Merchant | 3 | `ArchetypeAct3MerchantBoss`（只有这一个，无 Simpler/NewAge 之分） | — |

> 注意几点（已用全 jar 常量池扫描核实）：
> - `Watcher/NewAge` 有 5 个类、`Defect/NewAge` 有 4 个，但 `generateDeck()` 的 `tableswitch` 每幕**只 new 一个**类。
>   多出来的类有两种情况：
>   (a) **被当作静态工具类使用**：`DefectCuriosityLightningPower$1.update()` 里调用了
>       `ArchetypeAct3InserterNewAge.fixUpOrbIntents()`（`javap` 证据见 `dcb-swap-archetype.txt`）；
>   (b) **完全无引用**：`ArchetypeAct2StancesNewAge`、`ArchetypeAct3DualityNewAge` 在整个 jar 里只被自己引用 —— 死代码。
> - `Merchant` 只有 Act3 一个剧本，且只在 `ascensionLevel > 20 && isAscensionMode && actNum == 3` 时出现（见 §6.3）。
> - 逐个 `CharBoss*` 的完整 `generateDeck()` 字节码见 `dcb-generateDeck-all.txt` 与 `dcb-acts-CharBoss*-full.txt`。

---

## 7. 文本与本地化

> 本节来自 `.recon/downfall/findings-l10n.md`（710 行）+ 自测核实。
> 抽出的 JSON 在 `.recon/downfall/l10n/<lang>/<XxxStrings>.json`（120 个文件，保留原目录结构），
> 调用点全表在 `.recon/downfall/_loc-callsites-strict.tsv`（780 个 charbosses class 全量反汇编，444 条 `ldc → 查询` 配对）。

### 7.1 ★ 三个必须先纠正的常见误解

| 常见误解 | 实际（有字节码 + 全 JSON 扫描证据） |
|---|---|
| 有 `charbossesResources/localization/...` | **不存在**。`charbosses/` 只有 780 个 `.class`（无任何资源根）；全量常量池扫描 `charbossesResources` → **0 命中**。CharBoss 的本地化**全部复用** `downfallResources/localization/<lang>/` 与**原版** `desktop-1.0.jar` 的 `localization/<lang>/` |
| `getCardStrings("downfall_Charboss:Strike_R")` | **不是**。`downfall_Charboss:Strike_R` 只是卡牌的**内部唯一 ID**（用于注册/`makeCopy` 识别）；字符串查询用的是**原版裸 id** `getCardStrings("Strike_R")` |
| Boss 机制文案来自 `LocalizedStrings.getMonsterStrings("downfall:BossMechanics")` | **该 key 在 1422 个 JSON 里都不存在**，且 `AbstractBossDeckArchetype.bossMechanicString` 字段除静态块赋值外**无任何读取方**（4075 个 class 全扫描）→ **死代码**。真正的机制名/描述来自 `PowerStrings` 的 `downfall:<XxxPower>Power` |

### 7.2 命名约定（全部附证据）

| 对象 | 约定 | 证据 |
|---|---|---|
| 命名空间 | `downfall:` | `downfallMod.modID = "downfall"`；`makeID(String)` 内 `ldc "downfall:"` |
| **Boss 卡牌内部 ID** | `downfall_Charboss:<原版卡 id>`（**下划线**，不是 `downfall:`） | `EnStrikeRed.ID="downfall_Charboss:Strike_R"`、`EnDefendRed.ID="downfall_Charboss:Defend_R"`、`EnBash.ID="downfall_Charboss:Bash"`、`EnAfterImage.ID="downfall_Charboss:After Image"`（含空格） |
| 克隆卡 ID | `downfallBossCard:` + `base.cardID` | `AbstractBossCard(AbstractCard)` 偏移 8 `ldc "downfallBossCard:"` |
| **Boss 卡牌的文案 key** | **原版裸 id**（`"Strike_R"` / `"Bash"` / `"After Image"` / `"Apotheosis"` / `"BecomeAlmighty"`…） | 各 `En*` 卡 `<clinit>`：`CardCrawlGame.languagePack.getCardStrings("Strike_R")`。统计：`getCardStrings` 共 323 处调用，317 个不同 key，其中只有 26 个是 `downfall:<Name>` 前缀 |
| **Boss 遗物 ID** | **裸原版遗物 id，无前缀** | `CBR_BurningBlood.ID="Burning Blood"`、`CBR_BlackStar.ID="BlackStar"`、`CBR_RedSkull.ID="RedSkull"` |
| **Boss 遗物文案** | **不调用 `getRelicStrings`**（`AbstractCharbossRelic` 全文 0 命中）；名字/风味/描述由 `super(baseRelic.relicId, baseRelic.imgUrl, baseRelic.tier, CLINK)` 从原版遗物**整体继承** | 只有 16 个遗物为了补一句"Boss 加成说明"才额外查 `getRelicStrings(makeID("BlackStar"))` 等 |
| **怪物 ID** | `downfall:` + 名字 | `CharBossIronclad` → `makeID("Ironclad")`；`Silent` / `Defect` / `Watcher` / `Hermit` / `makeID("CharBossMerchant")` |
| Boss 机制文案 key | `downfall:<XxxPower>Power`（PowerStrings） | `IroncladStatusPower.POWER_ID = "downfall:IroncladStatusPower"`；共 30 个这样的 power-key = boss 机制文案来源 |
| 跨包借用 | `<OtherMod>:<Key>` | `getEventString("champ:ChampTalk")`（`CharBossIronclad.takeTurn`） |
| **图片** | **JSON 里没有 `IMG` 字段**（全仓库搜 `"IMG"` → 0 命中）；路径**硬编码在 Java 常量里** | `AbstractBossCard` 构造：`ldc "downfallResources/images/cards/"` + 构造参数 IMG + `ldc ".png"` / `ldc "_p.png"`。例：`"red/attack/strike"`、`"red/skill/defend"`、`"green/power/after_image"`、`"purple/skill/wish"` |

**各 `*Strings` 文件的字段集合**（对 120 个 JSON 枚举得出，**与 StS2 的本地化结构对照时很有用**）：
```
CardStrings       : {NAME, DESCRIPTION, EXTENDED_DESCRIPTION}
RelicStrings      : {NAME, FLAVOR, DESCRIPTIONS}
PowerStrings      : {NAME, DESCRIPTIONS}
MonsterStrings    : {NAME, MOVES, DIALOG}
UIStrings         : {TEXT, EXTRA_TEXT}
CharacterStrings  : {NAMES, TEXT, OPTIONS}
EventStrings      : {NAME, DESCRIPTIONS, OPTIONS}
RunModStrings     : {NAME, DESCRIPTION}
PotionStrings     : {NAME, DESCRIPTIONS}
replacementStrings: [ {KEYS:[...], VALUE:...} ]
```

### 7.3 中英条目实例（原文照贴）

**① Boss 名字** —— `downfallResources/localization/<lang>/UIStrings.json` 的 `downfall:RunHistoryMonsterNames`，
按 `TEXT` **下标**取：[1]=Ironclad [2]=Silent [3]=Defect [4]=Watcher [5]=Merchant [6]=Hermit。

```json
// eng
"downfall:RunHistoryMonsterNames": {
  "TEXT": ["Living Wall Heads","The Ironclad","The Silent","The Defect","The Watcher","Merchant","The Hermit"]
}
// zhs
"downfall:RunHistoryMonsterNames": {
  "TEXT": ["活墙壁","铁甲战士","静默猎手","故障机器人","观者","商人","亡命之徒"]
}
```

**② 遗物** —— 原版复用遗物的文案直接来自**原版** `relics.json`：

```json
// eng (desktop-1.0.jar)
"Burning Blood": { "NAME": "Burning Blood",
  "FLAVOR": "Your body's own blood burns with an undying rage.",
  "DESCRIPTIONS": ["At the end of combat, heal #b", " HP."] }
// zhs (desktop-1.0.jar)
"Burning Blood": { "NAME": "燃烧之血",
  "FLAVOR": "你自身的血液因为无尽的愤怒而燃烧沸腾。",
  "DESCRIPTIONS": ["在战斗结束时，回复 #b", " 点生命。"] }
```
Downfall **只补"Boss 加成"那一段**（无 `NAME`）：
```json
// eng
"downfall:BlackStar":  { "DESCRIPTIONS": [" NL Boss acquired ", " additional relics, but starts with less than maximum health."] }
"downfall:BustedCrown":{ "DESCRIPTIONS": [" NL Being unable to pick optimal cards, Boss has fewer synergistic cards than usual."] }
// zhs
"downfall:BlackStar":  { "DESCRIPTIONS": [" NL Boss额外获得了 ", " 件遗物，但最大生命变少。"] }
"downfall:BustedCrown":{ "DESCRIPTIONS": [" NL 不能获得其他牌，Boss的牌组配合比正常时少。"] }
```
Downfall 自有遗物（有 `NAME`）：
```json
// eng "downfall:BlackCandle": { "NAME":"Black Candle", "FLAVOR":"The flame shines even brighter from within the darkness.", "DESCRIPTIONS":[...] }
// zhs "downfall:BlackCandle": { "NAME":"黑蜡烛",     "FLAVOR":"火焰在黑暗中闪烁得更加耀眼。",         "DESCRIPTIONS":[...] }
```

**③ UI 关键词（能量面板）** —— `downfall:EnergyPanelTip`：
```json
// eng
"downfall:EnergyPanelTip": {
  "TEXT": ["Enemy's current energy count. Cards require energy to play."],
  "EXTRA_TEXT": ["Energy"] }
// zhs
"downfall:EnergyPanelTip": {
  "TEXT": ["敌人当前的能量。打出卡牌需要能量。"],
  "EXTRA_TEXT": ["能量"] }
```
映射（字节码）：`EnemyEnergyPanel.MSG = uiStrings.TEXT`；`EnemyEnergyPanel.LABEL = uiStrings.EXTRA_TEXT`。

**④ Boss 机制名/描述** —— `PowerStrings` 的 `downfall:IroncladStatusPower`：
```json
// eng
"downfall:IroncladStatusPower": {
  "NAME": "Synchronize",
  "DESCRIPTIONS": [
    "Whenever Ironclad generates a #yStatus card, a duplicate is added into your discard pile.",
    "Whenever Ironclad generates a #yStatus card, it enters your discard pile instead. NL After losing #b",
    " more HP, you gain a #yPurity with #yRetain.",
    "Whenever Ironclad generates a #yStatus card, it enters your draw pile instead. NL After losing #b" ] }
// zhs
"downfall:IroncladStatusPower": {
  "NAME": "同步",
  "DESCRIPTIONS": ["每当 #y铁甲战士 生成 #y状态牌 时，反而将其加入你的弃牌堆。"] }
```

**⑤ 卡名**
```json
// eng —— Downfall 新增卡（有中文）
"downfall:SummonMushrooms": { "NAME":"Fungus Among Us", "DESCRIPTION":"Kill all Mushrooms. Heal HP equal to their remaining HP. NL Summon two Mushrooms." }
// zhs
"downfall:SummonMushrooms": { "NAME":"真菌环绕", "DESCRIPTION":"击杀所有蘑菇， NL 回复其剩余血量点生命。 NL 召唤两个蘑菇。" }
// 原版复用卡（来自 desktop-1.0.jar）
"Strike_R": { "NAME":"Strike", "DESCRIPTION":"Deal !D! damage." }   // zhs: "打击"
```

### 7.4 JSON 是怎么被读到的（字节码证据）

```java
// downfall/downfallMod —— 加载器
public static final String modID = "downfall";
public static String makeID(String s)    { return "downfall:" + s; }        // ldc "downfall:"
public static String assetPath(String s) { return "downfallResources/" + s; } // ldc "downfallResources/"
private String makeLocalizationPath(GameLanguage g, String name) {
    // "localization/" + lang + "/" + name + ".json"  → assetPath()
}
private void loadLocalization(GameLanguage g, Class<?> clazz) {
    BaseMod.loadCustomStringsFile(clazz, makeLocalizationPath(g, clazz.getSimpleName()));  // ← 文件名 = 类 SimpleName
    BaseMod.loadCustomStringsFile(clazz, makeLocalizationPath(g, clazz.getSimpleName(), PACKAGE_EXPANSION));
    ...  // 共 12 个资源根：guardian/slimebound/sneckomod/hexamod/expansioncontent/champ/bronze/
         // gremlin/hermit/collector/awakened/downfall —— 注意：没有 charbosses
}
private String getLangString() { /* Settings.language.name().toLowerCase(); ZHT → "zhs"; 兜底 "eng" */ }
```
即 `CardStrings.class` → `CardStrings.json`，由 `basemod.BaseMod.loadCustomStringsFile` 注册进 `CardCrawlGame.languagePack`。

```java
// charbosses.bosses.AbstractBossDeckArchetype.<clinit>
 11: getstatic CardCrawlGame.languagePack
 14: ldc       "downfall:BossMechanics"                      // ← 这个 key 在任何 JSON 里都不存在
 16: invokevirtual LocalizedStrings.getMonsterStrings(String) → MonsterStrings
 19: putstatic  bossMechanicString                           // ← 字段无读取方 = 死代码

// 真正的机制文案（ArchetypeAct1StatusesNewAge.<init>）
 30: getstatic IroncladStatusPower.NAME:String  → bossMechanicName
 37: getstatic IroncladStatusPower.DESC:[String → DESC[0] → bossMechanicDesc
// 再由 AbstractBossDeckArchetype.initializeBossPanel()
    → putstatic charbosses/BossMechanicDisplayPanel.mechanicName / mechanicDesc

// charbosses.ui.EnemyEnergyPanel.<clinit>
  3: ldc_w       "EnergyPanelTip"
  6: invokestatic downfallMod.makeID(String)          // → "downfall:EnergyPanelTip"
  9: invokevirtual LocalizedStrings.getUIString(String) → UIStrings
 18: getfield UIStrings.TEXT        → putstatic MSG
 27: getfield UIStrings.EXTRA_TEXT  → putstatic LABEL

// charbosses.cards.red.EnStrikeRed.<clinit>
  3: ldc "Strike_R" ; 5: invokevirtual LocalizedStrings.getCardStrings(String) → CardStrings
// 而 ID 常量是 "downfall_Charboss:Strike_R" —— 两个 key 系统并行
```

**调用点统计**（780 个 charbosses class 全反汇编，444 条严格配对）：
| API | 调用数 / distinct key | 其中 `downfall:*` |
|---|---|---|
| `getCardStrings` | 323 / 317 | 26（= `CardStrings.json` 的 28 个 key 减去 2 个非 charboss 的） |
| `getPowerStrings` | 86 / 81 | **30 = boss 机制文案来源** |
| `getRelicStrings` | 26 / 12 distinct / 16 个类 | 少量（补 Boss 说明） |
| `getMonsterStrings` | 8 / 6 | `downfall:BossMechanics`（死）+ 4 个自身怪 + 2 个裸原版 id |
| `getEventString` | 1 | `champ:ChampTalk` |

### 7.5 中文本地化完整度（对本项目最要紧的一条）

**key 级：基本齐** —— `CardStrings` 28/28、`RelicStrings` 78/78、`MonsterStrings` 12/12、`EventStrings` 40/40、
`CharacterStrings` 13/13、`RunModStrings` 14/14 全部 key 对齐。
**但 `PowerStrings` eng 71 / zhs 67 → 缺 4 条**（全是 Boss 机制 power，中文下机制面板会缺字）：
`downfall:DefectAttackVoidPower`、`downfall:WatcherTrackerAttacksPower`、
`downfall:WatcherTrackerSkillsPower`、`downfall:VoidOrbPower`。

**内容级：真问题** —— key 在但**值仍是英文**：
| 文件 | eng ≡ zhs 的条目数 |
|---|---|
| **CardStrings** | **19 / 28** |
| RelicStrings | 1/78（仅调试遗物 `downfall:TestRelic`） |
| EventStrings | 1/40（仅调试事件 `downfall:BossTester`） |
| Power / Monster / Character / RunMod | 0 |

**中文下仍显示英文的 19 张卡**（正是 CharBoss 各角色专属的新增机制牌）：
`downfall:CuriousRobot, Murderbot, Temptation, BagOfKnives, Hallunication, NewToxins, FeedingFrenzy,
StatusMirror, EyeForAnEye, TruePeace, EndIsNigh, FalseWorship, FleetingFaith, FaithRewarded,
Crusade, Hyperfocused, Arsenal, Steady, Revolver`
已翻译的 9 张：`Aged, Bewildered, Icky, Haunted, Malfunctioning, Scatterbrained, CurseOfBlood,
SummonMushrooms, BossMaintenance`。

**UIStrings 部分翻译**：
- `downfall:BustKeyButton`：zhs 的 14 条 `TEXT` **全是英文**，中文译文被放在**未被引用的**
  `downfall:BustKeyButtonBackupForTranslators` 里（而且只有 11 条 vs 正式 14 条）→ 该 UI 在中文下显示英文。
- `downfall:ConfigMenuText`：前 14 条已译，**最后 3 条仍是英文**，其中包括
  `"Use Legacy Downfall Bosses (pre-6.0)"` —— 也就是本报告 §6.1 那个关键开关的文案。

**⚠ `DESCRIPTIONS` 段数中英不一致（越界风险）**：`downfall:IroncladStatusPower` eng=4 段 / zhs=1 段，
而代码按 `DESC[0..3]` 按血量阈值取不同段显示 → **中文下会读到不存在的下标**。
（全量未穷举，只人工发现 1 例，但这类风险在移植/搬运文案时必须主动校验。）

**对本项目的直接启示**：
1. CharBoss 的**原版复用卡**在中文下 100% 正常（来自游戏本体的 `localization/zhs/cards.json`）；
   但如果我们在 StS2 里**新写** boss 专属卡，中英文都要自己写。
2. 若要从 Downfall 搬运中文文案，**必须**先检查 §7.5 里那 19 张英文卡与 4 个缺失 power key。
3. `relicId` 复用原版字符串这件事（§5.1）意味着**遗物文案是天然多语言的**——这是 StS2 版值得复制的技巧。

---

## 8. `NewAge` vs `Simpler`

两套 archetype **在同一个包里并存，由 `useLegacyBosses` 开关切换**（见 §6.1）：

| | `NewAge/` | `Simpler/` |
|---|---|---|
| 触发条件 | `downfallMod.useLegacyBosses == true`（**默认**） | `useLegacyBosses == false` |
| 配置文案 | `"Use Legacy Downfall Bosses (pre-6.0)"` → 按字面解释：**`NewAge` 才是"旧"设计**，`Simpler` 是 6.0 的新简化设计（★ 这是推断，见下） | |
| 每回合能量 | `energy.energyMaster = 2` | `energy.energyMaster = 3` |
| A9 加血 | `maxHealth = round(maxHealth * 1.2f)` | `maxHealth += maxHPModifierAsc`（Act1/2/3 = +10/+20/+30） |
| 回合末弃手 | `applyEndOfTurnTriggers()` 里 `if (useLegacyBosses)` 才 `EnemyDiscardAtEndOfTurnAction` + `resetAttributes()` | 不弃手 |
| 剧本长度 | 6 回合 + `looped` 后的第二套剧本（两段式） | 5 回合，脚本更短、更直白 |
| 代码量（Act1） | 铁甲 Act1: 548 行 javap；静默毒 Act1: 481 行 | 铁甲 Act1: 267 行；静默毒 Act1: 362 行 |
| 典型差异（铁甲 Act1） | `Champion Belt` + `PowerThrough`/`SecondWind`/`TrueGrit`/`FeelNoPain` 多层铺垫，`Art of War` 能量规划，`EnWound`/`EnDazed`/`EnBurn` 状态污染 | `EnMetallicize`/`EnSpotWeakness`/`EnBerserk`/`EnDemonForm`/`EnBarricade` 这种"直接上 power"的简单打法 |
| 额外机制 | 用 `secondLoop`/`looped` 做二阶段难度曲线；`IroncladStatusPower` 之类的 Boss 机制 power | 机制更少、更少依赖 power 联动 |

> **命名解读的不确定性**：字节码层面「`useLegacyBosses == true` → `NewAge/` 类」是确定的；
> 但「NewAge 是旧设计」这一句来自配置文案 `"Use Legacy Downfall Bosses (pre-6.0)"` 与 Downfall 的版本史推断。
> 无论历史如何，**`Simpler/` 是两套里明显更简单的那一套**（这一点由上面的代码量/机制对比直接支撑）。

**哪套更适合"照抄思路"？→ `Simpler/`。**

理由：
1. 它就是同一套架构的**最小可运行子集**：构造函数 + `addedPreBattle()` + `getThisTurnCards()`（`initialize`/`initializeBonusRelic` 直接继承空实现）。
2. 没有 `looped` 二阶段、没有 `newPrio` 手工调序、没有跨回合的 power 联动假设，读一份就理解全部机制。
3. 每回合能量更多（3 vs 2）→ 出牌逻辑更容错，便于先跑通再调平衡。

移植时的推荐路径：**先用 `Simpler` 的粒度（一个角色三幕各一份 5 回合剧本）在 StS2 里跑通，
再把 `NewAge` 的"二阶段剧本 + 意图模拟元数据"作为第二阶段的难度层加上去。**

---

## 9. 移植清单：「崩坠的概念 → StS2 里对应的东西 / 需要自己造的东西 / 风险」

### 9.1 概念映射主表

| # | 崩坠的概念（证据类） | StS2 里对应的东西 | 需要自己造的东西 | 风险 |
|---|---|---|---|---|
| 1 | `AbstractCharBoss extends AbstractMonster`（伪玩家运行时） | StS2 的 Boss/敌人基类 + 玩家状态机 | 一个"既是敌人又持有手牌/能量/遗物/姿态"的实体类型；StS2 里敌人和玩家的类型层级如果分离，需要组合而非继承 | **高**：继承体系不同会迫使整个架构改为组合式；先确认 StS2 敌人能否拥有手牌/牌堆概念 |
| 2 | 每回合剧本 `getThisTurnCards()`（**无卡组**） | "Boss 的牌库" / "行为脚本" | 一套可选数据驱动的剧本结构（json 或代码），支持 `turn` 索引 + `looped` 二阶段 | 低（结构简单）；但若坚持"真卡组+抽牌"，成本会显著上升 |
| 3 | `hand` = `charbosses.cards.EnemyCardGroup` | 手牌容器 | 一个"敌人的手牌"容器；StS2 若无此概念，需要自建并接入渲染 | 中：必须能渲染在屏幕上（崩坠是画在屏幕底部的"牌列"） |
| 4 | **无弃牌堆/消耗堆**：`moveToDiscardPile`/`moveToExhaustPile` 空实现 | 无 | 明确决定：StS2 版要不要真堆。若不做真堆，"消耗/保留/洗回"的语义要另找落点 | 中：`shuffleBackIntoDrawPile`/`returnToHand` 这类效果需要有目的地 |
| 5 | `EnemyEnergyManager` + `EnemyEnergyPanel.totalCount`（能量池 + 可视化） | 能量系统 | 敌人的能量池 + 屏幕上的能量球 UI（崩坠复用了玩家的 `EnergyOrbRed/Green/Blue/Purple`） | 中：UI 必须做，否则玩家看不懂 Boss 能打几张 |
| 6 | `AbstractBossCard.use(AbstractPlayer p, AbstractMonster m)`，**p = 被打的玩家，m = Boss 自己** | 卡牌效果 API | **每个角色的每张牌都要重实现一次**（崩坠 ~400 张：`charbosses/cards/{red,green,blue,purple,hermit,colorless,curses,status}`）。或：写一个"角色互换"的通用包装层 | **最高**：这是工作量的绝对大头 |
| 7 | `DamageAction(p, new DamageInfo(m, damage, type), effect)` | StS2 的伤害 API | 确认 StS2 的伤害 API 的 source/target 参数语义，并统一约定 | 中 |
| 8 | `ApplyPowerAction(p, m, new XxxPower(p, n, true), n)` | StS2 的 power/buff API | power 的施加方/承受方参数化 | 中 |
| 9 | `AbstractBossCard.applyPowers()`：Boss 遗物/power/姿态 = 给出侧；**玩家 power = 接收侧** | 伤害计算管线 | 一套"跨阵营"的伤害计算：`atDamageGive`(Boss 侧) → `atDamageReceive`(玩家侧) → `atDamageFinal*` | **高**：StS2 若没有这套 hook，要自己设计（否则玩家的易伤/虚弱/遗物全部失效） |
| 10 | `EnemyUseCardAction`（一张牌结算后的完整后处理） | `UseCardAction` 等价物 | 完整复刻：`onAfterUseCard` → `purgeOnUse` → POWER 牌 empower → exhaust/rebound/returnToHand/shuffleBack/discard → 手牌重算 | **高**：这是"显得像玩家在打牌"的关键；漏一环就会出现"能力牌打完就没了""消耗牌不消失"等怪象 |
| 11 | `Strange Spoon` / `Runic Pyramid` / `Blue Candle` / `Medical Kit` 等**阈值型遗物直接写死在结算里** | 遗物系统 | 或者在 StS2 版简化掉这些遗物，或者预留同样的钩子点 | 中：简化是合理取舍 |
| 12 | `AbstractCharbossRelic`（123 个普通 + 34 个 EventRelics）+ **30 个手工转发钩子**；`boss.relics` 是独立 `ArrayList`，完全不进玩家遗物系统 | StS2 遗物系统 | Boss 侧遗物容器 + **把玩家的每个遗物钩子点都在 Boss 回合流程里手工调一遍**（崩坠写了 ~10 个 `applyXxxRelics()` + 8 个外部转发点） | **高**：钩子点漏一个，对应遗物就静默失效 |
| 12b | **`onVictory` / `onUnequip` / `justEnteredRoom` / `usedUp` 全部不可达** → 9 个 `CBR_*` 的 `onVictory` 覆写是死代码；`CBR_BurningBlood` / `CBR_IceCream` 是纯展示空壳 | 战斗结束钩子 | 若要"Boss 战斗结束回血"这类效果，必须**自己造一个战斗结束回调** | 中：容易误以为原版钩子会自动生效 |
| 12c | 遗物"作用于玩家"统一走 `actionManager`：`ApplyPowerAction(player, boss, power)` / `DamageAction(player, DamageInfo(boss, ...))`，视觉用 `RelicAboveCreatureAction` | — | 直接照抄这套约定（源=Boss，目标=玩家） | 低 |
| 12d | 遗物 ID 复用原版 ID（`super(new XxxRelic())`），但 `CBR_*` 里的 `ID` 常量有**不一致**（`CBR_IceCream.ID="IceCream"` vs 原版 `"Ice Cream"`） | 遗物 ID 约定 | 统一 ID 规范，别留两套 | 低 |
| 13 | `EnemyCardGroup.moveToExhaustPile` 手动转发 `relic.onExhaust` / `power.onExhaust` / `card.triggerOnExhaust` | 卡牌离开手牌时的钩子 | 同上 | 中 |
| 14 | 意图预览 = `preApplyIntentCalculations()` **整回合模拟**，靠 14 个 `xxxGeneratedIfPlayed` 元数据字段 | 敌人意图系统 | 若 StS2 意图只是"下一次攻击的数字"，可以先放弃整回合模拟（大幅降复杂度）；否则要为每张牌填元数据 | **高**（若要做） / 可裁剪 |
| 15 | 出牌顺序 = 剧本顺序（`newPrio` 默认全 0） | — | 直接照抄：**"按剧本顺序，能付得起就打，一张一张来"** | 低 |
| 16 | `canUse()` / `hasEnoughEnergy()` 的玩家式合法性检查（Entangled / Normality / 遗物 canPlay / 能量） | 出牌合法性 | 一套 Boss 侧的可打性判定 | 低 |
| 17 | `SortByNewPrio` + `autoPriority()` / `statusValue()` | 优先级 | 保留 `> -100` 的粗筛即可，甚至可以直接用"剧本标记"代替 | 低 |
| 18 | `charbosses/patches/*`（9 类补丁，见 §3.7） | StS2 mod hook（参考资料里提到 Harmony） | **系统性排查 StS2 中"假定 owner 是玩家"的代码路径**：伤害结算、power 施加、破防、能力过期特效、卡面渲染、能量图标、卡牌发光/变暗 | **高**：这是"隐性工作量"，崩坠用了 16 个补丁类去填 |
| 18b | `BrokeBlockPatch`：玩家破防时把 `onBlockBroken(player)` 广播给 Boss 遗物（Boss 版 Hand Drill 的触发源） | — | 需要"玩家事件 → 广播给敌人"的反向钩子 | 中 |
| 18c | `downfall.patches.TriggerPseudoStartOfCombatAction`：Neow 复活等场景要**重放一次**"战斗开始"（`atBattleStartPreDraw` + `atBattleStart` + `recharge()`） | — | "战斗开始"语义要可重入 | 中 |
| 19 | `AbstractBossDeckArchetype.bossMechanicName/Desc`（来自 PowerStrings `downfall:<XxxPower>`）+ `BossMechanicDisplayPanel extends automaton.EasyInfoDisplayPanel` | 敌人机制说明 UI | Boss 机制文本框 | 低 |
| 19b | **本地化全靠"复用原版 key"**：卡牌文案 = `getCardStrings("Strike_R")`；遗物 = `super(baseRelic.relicId, baseRelic.imgUrl, baseRelic.tier, CLINK)` 连名字/风味/描述一起继承 | StS2 本地化系统 | **照抄这个技巧**：Boss 卡/遗物只要复用原版 id，就能免费获得全部语言；只有"新增的 boss 专属牌"和"Boss 加成说明"要自己写 | 低，但要注意：Downfall 自己的中文里 **19/28 张 boss 专属卡仍是英文**、4 个 boss 机制 power 缺中文 key（§7.5）——搬运文案前必须校验 |
| 19c | `CardStrings` / `RelicStrings` / `PowerStrings` / `UIStrings` 的字段集合（§7.2） | StS2 的对应字符串类型 | 确认 StS2 的字段名与分段规则是否一致（尤其 `DESCRIPTIONS` 的**分段数组**——Downfall 有中英段数不一致导致下标越界的实例） | 中 |
| 20 | `charbosses/relics/EventRelics/`（34 个） | 事件奖励遗物 | 与主玩法无关，可完全跳过 | 低 |
| 21 | 姿态（`charbosses/stances/EnNeutralStance` 等）/ 充能球（`charbosses/orbs/AbstractEnemyOrb`、`charbosses/actions/orb/*`）/ 真言 | StS2 对应机制 | 若 StS2 首版只要"铁甲/静默"两个角色，可以**先不做**姿态和充能球 | 中（裁剪可降低首批工作量） |
| 22 | 多角色 + 每个角色 3 幕 × 2 套 = 约 40 个 archetype 类 | 数据/配置 | 先做 1 角色 × 1 幕 × 1 套剧本，验证管线，再指数扩张 | 中 |
| 23 | 按幕插入：**不改任何"Boss 池"**，而是每幕开始反射写 `AbstractDungeon.bossKey` + `setBoss(key)`；`bossList` 只表示"还剩几场 Boss" | StS2 的遭遇/Boss 分配机制 | **先搞清楚 StS2 的 Boss 是怎么被选出来的**：是"某个池 + bossKey"？还是"地图节点直接持有遭遇 id"？这决定注入点 | **高**：这是"能不能替换 Boss"的前置问题，必须先侦察 StS2 侧再设计 |
| 23b | `downfallMod.receiveStartAct()`：5 个 CharBoss **无放回随机抽**（3 幕不重复）；只在 Evil Mode 生效 | 按幕配置 | 建议改成**数据表**（`act → boss id → archetype id`），比反射 + 随机更好维护、更好调平衡 | 低 |
| 23c | `BossSetPatch` 补地图 Boss 图标（原版对未知 key 会 `WARNING: UNKNOWN BOSS ICON` 且图标置 null） | 地图 UI | 新 Boss 一定要有地图图标资源，否则地图上是空的 | 低但容易漏 |
| 24 | 进阶：A4 剧本牌升级 / A9 血量（legacy ×1.2 vs Simpler +10/20/30）+ 小怪强化 / A19 额外遗物 / A>20 插入商人 Boss | 难度系统 | 至少实现"按进阶等级换剧本或加血 + 加遗物" | 低 |
| 24b | `saveBossFight(id)` 把已面对的角色记进 `Act1/2/3BossFaced`，供 NeowBoss 的"gauntlet"（把打过的角色当小怪）复用 | 存档 | 若要复刻"重打已击败 Boss"的玩法，需要持久化记录 | 低 |
| 25 | `playMusic()` 按 `actNum` 播 `BOSS_BOTTOM`/`BOSS_CITY`/`BOSS_BEYOND`/`MINDBLOOM` | 音乐 | — | 低 |
| 26 | `Minion` power + `charbosses.actions.util.CharBossMonsterGroup`（Boss + 小怪组队） | 遭遇战编队 | Boss 带小怪（如铁甲 Act2 蘑菇） | 中 |

### 9.2 建议的实施顺序（基于上面的风险排序）

1. **可行性验证（必须先做）**：在 StS2 里确认
   (a) 敌人能否持有"手牌"并渲染；
   (b) 敌人能否持有"能量"并显示；
   (c) 敌人的"伤害/power 施加"API 能否指定 source=敌人、target=玩家。
   —— 这三条决定第 1 行风险是否成立。
2. **最小闭环**：一个角色（铁甲）、一幕（Act1）、`Simpler` 粒度（5 回合剧本）、3~5 张牌
   （`EnStrikeRed` / `EnDefendRed` / `EnBash` / 一张 power 牌 / 一张状态牌），跑通
   "玩家结束回合 → Boss 亮意图 → Boss 一张张打牌 → 玩家扣血"。
3. **补钩子**：遗物钩子转发（`atBattleStart` / `atTurnStart` …）、`onUseCard`、`onExhaust`。
4. **补结算**：`EnemyUseCardAction` 的全部后处理分支（尤其是 POWER 牌的 empower 和消耗牌）。
5. **扩内容**：其余牌 → 三幕剧本 → `NewAge` 二阶段 → 其他角色。
6. **补平衡与 UI**：意图模拟（若要）、Boss 机制提示框、音乐、进阶分层。

### 9.3 最需要警惕的三件事

1. **"卡组"这个词会误导设计**：崩坠的 Boss 是**剧本扮演**不是**牌组驱动**。
   如果一开始按"给 Boss 一堆牌 + 抽牌堆"设计，会撞上"抽牌随机性让 Boss 无法预告意图"的问题，
   最后还是要收敛回"剧本"。（崩坠连 `drawPile` 字段都没有。）
2. **`use(p, m)` 的角色互换是全案的支点**：`p` 是受害者、`m` 是 Boss。
   所有卡牌效果、所有 patch、所有 `atDamage*` 链都建立在这个约定上。StS2 版必须尽早把等价约定钉死并写进规范。
3. **补丁工作量被严重低估**：崩坠为了"让怪物像玩家"填了 16 个补丁类（渲染、能量图标、发光变暗、
   ApplyPowerAction、brokeBlock、Focus、卡面背景、特效屏蔽…）。这部分在移植计划里要单独列一个阶段，
   不能默认"调几个 API 就完事"。

---

## 附录 A：转储文件索引（`.recon/downfall/`）

| 文件 | 内容 |
|---|---|
| `dcb-AbstractCharBoss-members.txt` / `-full.txt` | Boss 基类成员签名 / 完整字节码（4802 行） |
| `dcb-AbstractCharBoss-anon.txt` | `AbstractCharBoss$1`（发牌）、`$2`（意图预算）字节码 |
| `dcb-DrawTypes-sortByNewPrio-members.txt` / `dcb-sortByNewPrio-full.txt` | AI 枚举与比较器 |
| `dcb-EnemyEnergyManager-full.txt` | 能量管理器（`prep`/`recharge`/`use`） |
| `dcb-EnemyEnergyPanel-full.txt` | 能量面板静态方法与 UI |
| `dcb-AbstractBossCard-members.txt` / `-full.txt` | 卡牌基类（4491 行） |
| `dcb-EnemyUseCardAction-full.txt` | 出牌结算（最关键） |
| `dcb-EnemyCardGroup-members.txt` / `-full.txt` | 手牌容器（空实现的弃牌/消耗堆） |
| `dcb-AbstractBossDeckArchetype-members.txt` / `-full.txt` | 剧本基类 |
| `dcb-ArchetypeAct1StatusesNewAge-full.txt` | 剧本样例（铁甲 Act1，546 行） |
| `dcb-arch-*.txt` | Simple / NewAge 两套 Act1 剧本对照 |
| `dcb-CharBossIronclad-members.txt` / `-full2.txt` | 角色 Boss 实现（含 `generateDeck` 按幕选择） |
| `dcb-ArchetypeBase-full.txt` | `ArchetypeBaseIronclad` / `ArchetypeBaseSilent` |
| `dcb-sample-cards-red.txt` / `-curse.txt` / `-xcost.txt` | 样例卡牌字节码（`EnStrikeRed`/`EnBash`/`EnDefendRed`/`EnDoubt`/`EnWound`/`EnWhirlwind`/`EnPowerThrough`/`EnBloodletting`） |
| `dcb-patch-classlist.txt` / `dcb-patch-annotations-all.txt` | 全部 `@SpirePatch` 注解提取 |
| `dcb-patch-AbstractBossCardPatches-full.txt` / `dcb-patch-timeline-full.txt` | 渲染类 / 结算类补丁字节码 |
| `dcb-draw-actions-full.txt` | `CharbossTurnstartDrawAction` 等流程动作 |
| `dcb-EnemyDiscardAtEndOfTurnAction-full.txt` | 回合末弃牌（保留/金字塔） |
| `dcb-misc-members.txt` | `AbstractCustomBossCard` / `BossMechanicDisplayPanel` / `IroncladStatusPower` |
| `dcb-downfallMod-full.txt` | `downfall.downfallMod`（配置开关默认值、`receiveStartAct`、`resetBossList`） |
| `dcb-acts-BossSetPatch-full.txt` | `downfall.patches.BossSetPatch`（地图图标替换 + `@SpirePatch` 注解） |
| `dcb-generateDeck-all.txt` | 5 个 `CharBoss*` 的 `generateDeck()`（按幕选择 archetype） |
| `dcb-swap-archetype.txt` | `DefectCuriosityLightningPower$1` 调用 `ArchetypeAct3InserterNewAge.fixUpOrbIntents()` |
| `dcb-relic-*.txt` | 遗物相关的全部转储（见 `findings-relics.md` §10 的清单） |
| `dcb-acts-*.txt` | 按幕插入相关的 42 个转储（见 `findings-acts.md`） |
| `dcb-vanilla-AbstractDungeon-full.txt` | **原版** `desktop-1.0.jar` 的 `AbstractDungeon`（`setBoss` / `getBoss` 的真身） |
| `dcb-vanilla-MonsterRoomBoss.txt` | **原版** `MonsterRoomBoss.onPlayerEntry()`（`getBoss()` → `bossList.remove(0)`） |
| `dcb-vanilla-dungeons-getBoss.txt` | **原版** `Exordium` / `TheCity` / `TheBeyond` |
| `l10n/` | 从 jar 抽出的本地化 JSON（120 个，保留原目录结构）+ `_charboss-digest-{eng,zhs}.json` |
| `_loc-callsites-strict.tsv` | 780 个 charbosses class 的全量本地化调用点（444 条严格配对） |
| `findings-relics.md` | **遗物系统完整侦察报告**（§5 的来源，含全 jar 引用扫描） |
| `findings-acts.md` | **按幕插入/补丁完整侦察报告**（§6 的来源，941 行） |
| `findings-l10n.md` | **本地化完整侦察报告**（§7 的来源，710 行） |
| `_zhs-completeness.txt` / `_zhs-untranslated.txt` | 中文本地化 key 完整度 / 未翻译条目 |

> **原版对照 jar 路径**：`D:\SteamLibrary\steamapps\common\SlayTheSpire\desktop-1.0.jar`
> （注意是 `SlayTheSpire`，**没有空格**；写成 `Slay the Spire` 会找不到）

## 附录 B：可复现的扫描方法

```powershell
$javap = "D:\Program Files\Eclipse Adoptium\jdk-17.0.20.8-hotspot\bin\javap.exe"
$jar   = "D:\SteamLibrary\steamapps\workshop\content\646570\1610056683\Downfall.jar"

# 1) 成员签名（先看这个，判断职责，不要一上来就读字节码）
& $javap -p -classpath $jar <全限定类名>

# 2) 关键方法才看字节码；-l 能给出局部变量名（非常有用）
& $javap -p -c -l -constants -classpath $jar <全限定类名>

# 3) 注解（@SpirePatch 的 clz/method 只能从 -v 的 RuntimeVisibleAnnotations 看到）
& $javap -v -p -classpath $jar <补丁类>

# 4) 全 jar 引用扫描（判断"谁引用了谁"最可靠的方法：JVM 常量池里的类名是明文字符串）
#    先把所有 .class 解到临时目录，再对每个文件做 ASCII 解码 + 子串匹配
Add-Type -AssemblyName System.IO.Compression.FileSystem
$z=[System.IO.Compression.ZipFile]::OpenRead($jar)
foreach($e in $z.Entries){ if($e.FullName -match '\.class$'){
    [System.IO.Compression.ZipFileExtensions]::ExtractToFile($e, (Join-Path $tmp ($e.FullName -replace '/','_')), $true) } }
$z.Dispose()
# 然后对 $tmp 下每个 .class：$s=[Text.Encoding]::ASCII.GetString([IO.File]::ReadAllBytes($f)); if($s -match '<needle>'){...}
# 注意：这种方法抓不到 Class.forName 字符串拼接式的反射（本 jar 内 Class.forName 字面量 0 命中）
```

> **本报告用到的两个关键扫描结论**（都是靠方法 4 得出的）：
> 1. `newPrio` 只被 7 个类引用 → 证明"手牌顺序 = 剧本顺序"。
> 2. `chooseCardToDraw` / `DrawTypes` 只被 `AbstractCharBoss` 自己引用且无调用点 → 证明"抽牌 AI 是死代码"。
