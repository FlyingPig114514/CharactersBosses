# 二层变体 Boss「王储」（储君 · 王权/力量轴）

> 状态：**已实现**（v0.2.317）。用户 2026-11-02 设计稿原话见 §1，实现落点见 §6。
> Key：`REGENT_KING` ／ 遭遇：`REGENT_KING_BOSS_ENCOUNTER` ／ 剧本：`src/Hooks/Core/KingRegentScript.cs`

## 1. 用户设计稿（原话）

> 添加新boss：二层变体Boss王储
> 遗物：天赋君权、护喉甲、尘封魔典、红面具
> 特殊效果：征服者：储君的名字中包含"王"的牌在打出时会使所有玩家本回合失去1点力量。
> 每当他的攻击被完全格挡，所有玩家在本回合获得1点力量。
> PS：前者用王之凝视，后者用独白。
> 进阶8：升级宇宙冷漠，获得奥利哈钢。进阶9：升级王者之拳、战火铸就
>
> 1.封印王座、王之凝视+、笨拙
> 2.光子切割+（返顶王者之踢）、王者之拳、王者之踢(被返顶)，防御（没费）、打击（没费）
> 3.王者之踢（此时费用为2）、群星斗篷、打击（没费)、所向无敌(回合结束时处于抽牌堆顶，自动打出)
> 4.王国资产、防御、胜券在王
> 5.抉择抉择(打出宇宙冷漠）宇宙冷漠*3（分别返顶王者之踢、王者之拳、所向无敌)，战火铸就、星位序列、君王之剑、笨拙（诅咒；v0.2.320 削弱后由「何人僭越」换成）、所向无敌(回合结束时处于抽牌堆顶自动打出)
> 6.王者之拳，王者之踢（此时为1费），打击(没费)
> 7.洗牌。战火铸就、星位序列、君王之剑
> 8.防御、防御、打击（没费）
> 9.王者之拳+、群星斗篷、王者之踢（此时为0费）
> 10.宇宙冷漠(返顶王者之踢)、光子切割+（返顶所向无敌）、王者之踢、所向无敌(回合结束时处于抽牌堆顶自动打出)、打击*2(没费)
> 之后7～10循环。
> 拦掉抉择抉择和光子切割+的抽牌。

## 2. 名字 → 类 / entry 对照

| 中文名 | 类名 | entry | 说明 |
|---|---|---|---|
| 天赋君权 | `DivineRight` | `DIVINE_RIGHT` | 战斗开始给星（影子玩家靠 `BossHookBridge` 补） |
| 护喉甲 | `Gorget` | `GORGET` | 战斗开始 4 层覆甲（`BossHookBridge.GrantGorgetPlatingAtCombatStart`） |
| 尘封魔典 | `DustyTome` | `DUSTY_TOME` | 远古遗物，效果在**拾起时** ⇒ 战斗内零影响 |
| 红面具 | `RedMask` | `RED_MASK` | 战斗开始给所有玩家虚弱（`ShadowRedMaskTurnStartPatch` 保证只触发一次） |
| 奥利哈钢（进阶 8） | `Orichalcum` | `ORICHALCUM` | 回合结束无格挡 → +6 格挡（`BossRelicHookBridge`，需 `FireBossRelicHooks = true`） |
| 封印王座 | `TheSealedThrone` | `THE_SEALED_THRONE` | 1 费能力：每打出一张牌获得 1 星 |
| 王之凝视 | `MonarchsGaze` | `MONARCHS_GAZE` | 2 费能力 |
| 笨拙 | `Clumsy` | `CLUMSY` | 诅咒，打不出 |
| 光子切割 | `PhotonCut` | `PHOTON_CUT` | 1 费：10 伤、抽 1、把一张手牌放回堆顶 |
| 王者之拳 | `KinglyPunch` | `KINGLY_PUNCH` | 1 费 8 伤 |
| 王者之踢 | `KinglyKick` | `KINGLY_KICK` | 4 费 27 伤，每次抽到 −1 费 |
| 防御 | `DefendRegent` | `DEFEND_REGENT` | 1 费 |
| 打击 | `StrikeRegent` | `STRIKE_REGENT` | 1 费 |
| 群星斗篷 | `CloakOfStars` | `CLOAK_OF_STARS` | 0 费 7 格挡 |
| 所向无敌 | `IAmInvincible` | `I_AM_INVINCIBLE` | 回合结束时若在抽牌堆顶 → 免费打出 |
| 王国资产 | `Royalties` | `ROYALTIES` | 1 费能力 |
| 胜券在王 | `RoyalGamble` | `ROYAL_GAMBLE` | 0 费，+9 星 |
| 抉择，抉择 | `DecisionsDecisions` | `DECISIONS_DECISIONS` | 0 费：抽 3、选一张技能牌打出 3 次 |
| 宇宙冷漠 | `CosmicIndifference` | `COSMIC_INDIFFERENCE` | 1 费：6 格挡、把弃牌堆一张放回堆顶 |
| 战火铸就 | `WroughtInWar` | `WROUGHT_IN_WAR` | 1 费：7 伤、铸造 7 |
| 星位序列 | `Alignment` | `ALIGNMENT` | 0 费，+2 星 |
| 君王之剑 | `SovereignBlade` | `SOVEREIGN_BLADE` | 衍生，2 费，伤害 = 铸造值（基础 10） |
| 何人僭越 | `KnowThyPlace` | `KNOW_THY_PLACE` | 0 费：1 虚弱 + 1 易伤 |
| （特殊效果图标1） | `MonarchsGazeStrengthDownPower` | — | 原版「王之凝视」的临时减力量 power |
| （特殊效果图标2） | `MonologuePower` | — | 原版「独白」，只借它的**图标/名字** |

## 3. 能量账（用户稿里「没费」的点就是这里倒推出来的）

基础 2（角色基础 3 − 涅奥的祝福 1）；本变体没有任何加费遗物。

| 回合 | 能量 | 花在哪 | 用户标注 |
|---|---|---|---|
| 1 | 2 | 封印王座 1 + 王之凝视+ **1**（升级后 1 费，用户 2026-11-02 订正） | 笨拙是诅咒，打不出 |
| 2 | 2 | 光子切割+ 1 + 王者之拳 1 | 王者之踢被返顶；防御/打击没费 |
| 3 | 2 | 王者之踢 **2**（已抽到 2 次）+ 群星斗篷 0 | 打击没费 |
| 4 | 2 | 王国资产 1 + 防御 1 + 胜券在王 0 | — |
| 5 | 3 | 战火铸就 1 + 君王之剑 2 | 抉择抉择 0、宇宙冷漠由抉择抉择打出（不花钱）、星位序列 0、何人僭越 0 |
| 6 | 2 | 王者之拳 1 + 王者之踢 **1**（第 3 次抽到） | 打击没费 |
| 7 | 3 | 战火铸就 1 + 星位序列 0 + 君王之剑 2 | 「洗牌」= 抽牌堆洗一次 |
| 8 | 2 | 防御 1 + 防御 1 | 打击没费 |
| 9 | 1 | 王者之拳+ 1 + 群星斗篷 0 + 王者之踢 **0**（第 4 次抽到） | — |
| 10 | 2 | 宇宙冷漠 1 + 光子切割+ 1 + 王者之踢 **0** | 打击×2 没费 |

✅ 第 1 回合 = 2 费：封印王座(1) + 王之凝视+(**1**，升级后 −1 费，反编译
`MonarchsGaze.OnUpgrade → EnergyCost.UpgradeBy(-1)`）正好花完她基础的那 2 点能量。

**王者之踢的费用**：原版是「每次抽到这张牌 −1 费」（4 → 3 → 2 → 1 → 0），
而我们的手牌每回合都是 `BuildHand` **新建实例**，抽牌减费根本不会发生
⇒ 由剧本逐回合写死 `CostThisTurn`（2 / 1 / 0 / 0），见 §6。

## 4. 特殊效果「征服者」（`Powers/BossConquerorPower.cs`）

用户原话拆成两条，各自挂一个事件：

1. **打出带「王」的牌 → 所有玩家本回合失去 1 点力量**
   ⇒ `AfterCardPlayed`：牌的 owner 是她、且**名字里含「王」**。
   对这些牌生效：封印王座、王之凝视、王者之拳、王者之踢、君王之剑、王国资产、胜券在王。
   施加的是原版 `MonarchsGazeStrengthDownPower`（＝「王之凝视」，临时减力量、回合结束还回去）。
2. **她的攻击被完全格挡 → 所有玩家本回合获得 1 点力量**
   ⇒ `AfterDamageGiven`：dealer 是她、目标是真人玩家、`DamageResult.WasFullyBlocked`。
   施加 `Powers/BossConquerorStrengthUpPower.cs`（`TemporaryStrengthPower` 的正向分支：
   标题/描述取自原版「独白」`Monologue`，图标也借「独白」——用户 PS「后者用独白」）。

PS 那句**不写进**特殊效果描述（用户要求）。

## 5. 「返顶」与「拦抽牌」

- 「返顶」（把某张牌放回抽牌堆顶）在原版是**选牌**：光子切割+「手牌一张放堆顶」、
  宇宙冷漠「弃牌堆一张放堆顶」。Boss 出牌时选牌走 `BossCardSelector` 自动选，
  不点名就会按「价值」乱挑 ⇒ 新增**按回合的选择队列**（`BossTurn.PickOrder`），
  按剧本顺序把每次选牌钉死成指定牌。
  - 第 2 回合：`[王者之踢]`
  - 第 5 回合：`[宇宙冷漠, 王者之踢, 王者之拳, 所向无敌]`（第一个是**抉择抉择**要打出的技能牌，
    后面三个是它打出 3 次**宇宙冷漠**时各自的返顶目标）
  - 第 10 回合：`[王者之踢, 所向无敌]`
- 「拦掉抉择抉择和光子切割+的抽牌」：`BossDefinition.KeepsScriptedHandByAct[1] = true`
  ⇒ 抽牌照常发生，但抽进来的牌会被挪回抽牌堆顶，手牌恒等于剧本。
  （不用 `BlocksVanillaDraw`：那是整只 Boss 级的开关，会连带影响储君另外三幕。）
- 所向无敌：沿用 `BossTurn.FreePlayTopEntry = "I_AM_INVINCIBLE"`
  （回合结束时把它挪到堆顶、调它自己的 `AfterAutoPostPlayPhaseEntered` 免费打出），
  手牌里那张标 `DisplayOnly + AutoPlayed` 让意图里能看到格挡。

## 6. 代码落点

| 内容 | 文件 |
|---|---|
| 剧本 / 卡组 / 遗物 | `src/Hooks/Core/KingRegentScript.cs`（10 回合，`LoopFrom = 7`、`LoopLength = 4`） |
| 特殊效果 | `src/Hooks/Powers/BossConquerorPower.cs`、`src/Hooks/Powers/BossConquerorStrengthUpPower.cs` |
| 图标（灯泡 / 独白） | `src/Hooks/Patches/BossSpecialPowerIconPatch.cs` |
| 变体定义 + 进阶 | `src/Hooks/Core/BossDefs.cs`（`Regent.VariantsByAct[1]`；`FireBossRelicHooks = true`；`KeepsScriptedHandByAct[1] = true`） |
| Key / 阵容 / 互补变体 | `src/Hooks/Core/BossKeys.cs` |
| 变体判定 | `src/Hooks/Core/BossLineups.cs`（`KingActive`，遭遇优先、种子 salt 54 兜底） |
| 遭遇 | `src/Hooks/Encounters/TierBossEncounters.cs` + `CharBossEncounters.cs` |
| BGM | `src/Hooks/Core/BossBgm.cs`（二层统一帝皇蟹） |
| 藏品 | `src/Hooks/Core/DownfallCollectibleCompat.cs`（给「征服者 `CONQUEROR`」） |
| 剧本字段 | `src/Hooks/Core/BossScript.cs`（`ScriptCard.CostThisTurn`、`BossTurn.PickOrder`） |
| 自动选择 | `src/Hooks/Core/BossCardSelector.cs`（`PickOrder` 队列） |
| 意图 | `src/Hooks/Core/BossScriptPlanner.cs`（战火铸就的铸造要算进君王之剑的意图） |
| 本地化 | `content/localization/{zhs,eng}/{monsters,powers,encounters}.json` |

## 7. 待实机确认

1. ~~第 1 回合能量~~（已按用户订正改成 2 费）。
2. 抉择抉择真的把**宇宙冷漠打出 3 次**、三次返顶分别命中王者之踢 / 王者之拳 / 所向无敌。
3. 王者之踢的意图与卡面费用是否按 2 / 1 / 0 / 0 显示。
4. 特殊效果两个方向在玩家状态栏上的显示（王之凝视 / 独白）。
5. 君王之剑的伤害是否等于「基础 10 + 战火铸就的铸造 7」。

## 8. v0.2.319 三处实机修正（用户 2026-11-02 实测）

1. **战斗开始不该有浮空剑**（用户：「他没有击剑指南（只有铸剑储有），不应该在战斗开始时就把剑给铸造出来
   飘在他身边。应该在他第五回合打战火铸就的时候再铸造出来」）
   - 根因：Deck() 里写了 SovereignBlade ⇒ 战斗开始时牌堆里就有一张伤害 10 的剑牌，
     UI/BossSovereignBlade.EnsureVfxExists 每帧扫到它就补出浮空剑（日志
     战斗开始：铸出浮空君王之剑（原版 PlayCombatRoomForgeVfx，伤害 10））。
   - 修法：**卡组不再带这张牌**；并且 EnsureVfxExists 加一道闸 —— 只有「已经铸过」（伤害 > 基础 10）
     的剑才补 VFX。于是第 5 回合打出「战火铸就」时，由原版 ForgeCmd.Forge 现造一把剑
     （反编译：lades.Count == 0 → CreateCard<SovereignBlade> + 进手牌 + AddDamage +
     PlayCombatRoomForgeVfx），浮空剑就在那一刻出现。铸剑储君有击剑指南、战斗开始就铸过 ⇒ 行为不变。
2. **君王之剑意图没算本回合的铸造**（用户原话见上一条的②）
   - 根因：Core/BladesGrowth.cs 的 ladePendingForge 只认「追踪之刃」。
   - 修法：凡带 Forge 变量的**非能力牌**都算（追踪之刃 7、**战火铸就 7**）。
     能力牌仍交给 PendingFurnaceForge（熔炉是回合开始铸造，否则会重复）。
   日志里那一项写的是 (补=x+铸N+段M)，修好后规划期应显示 +铸7。
3. **王者之拳不随抽到成长**（用户：「他只有一张王者之拳，所有的王者之拳都是同一张」）
   - 根因：原版 KinglyPunch.AfterCardDrawn（card == this 时 Damage += Increase），
     而我们的手牌每回合 BuildHand 新建实例 ⇒ 抽牌钩子不触发、成长全丢。
   - 修法：CharBossMonster.KinglyPunchBonus 记本场累计值；造牌时**先把这次的 Increase
     累计进去、再把累计值加到卡面**（＝原版「抽到那一刻就涨」，第一手就已经涨过一次）。
     未升级 +4 / 升级 +6（OnUpgrade 两处都 +2）。

### 王者之拳的成长数值（按用户口径：第一次抽到就涨）

| 出现顺序 | 回合 | 卡面 | 累计 Increase |
|---|---|---|---|
| 第 1 次 | 2 | 8+4 = **12** | +4 |
| 第 2 次 | 6 | 8+8 = **16** | +8 |
| 第 3 次 | 9（王者之拳+） | 10+14 = **24** | +14 |
| 之后每个循环 | 13 / 17 …（7～10 循环里的第 9 回合） | 每次再 +4 / +6 | — |

进阶 9 起王者之拳整体升级（Increase 4 → 6、Damage 8 → 10）⇒ 第 2 / 6 / 9 回合分别是 **16 / 22 / 28**。

## 9. v0.2.320 削弱（用户 2026-11-02：「削弱一下，何人僭越改笨拙」）

第 5 回合那一格从「何人僭越」（KnowThyPlace，0 费：给玩家 1 层虚弱 + 1 层易伤）
换成诅咒「笨拙」（Clumsy：Unplayable + Ethereal，打不出、回合结束自己消耗）。
后果：这一回合不再给玩家上虚弱/易伤 ⇒ 她第 5 回合的伤害与后续（循环里没有第 5 回合）都少了一层收益，
而且她手里多一张死牌。卡组同步成「笨拙 ×2」（第 1 回合那张照旧）。

## 10. v0.2.321 微调（用户 2026-11-02：「升级封印王座，降级王之凝视（没什么实际影响）」）

第 1 回合：封印王座 → **封印王座+**（反编译 TheSealedThrone.OnUpgrade → EnergyCost.UpgradeBy(-1) ⇒ **0 费**，效果不变）；
王之凝视+ → **王之凝视**（2 费，StrengthLoss 仍是 1）。两费之和 0 + 2 = 2，与基础能量完全一致 ⇒ **强度不变**。

## 11. v0.2.322 —— 改名「王储」+ 特殊效果换成「麻痹」

用户原话：
> 把王储的战斗从 REGENT_CONQUEROR_BOSS_ENCOUNTER 改为 REGENT_KING_BOSS_ENCOUNTER。其他相关，比如王储的内部名称也跟着改。
> 更改王储特殊效果：征服者：每当储君将一张牌放回抽牌堆顶部时，所有玩家打出的下一张攻击牌的伤害减少 50%。
> 增加 debuff：麻痹。效果：你下一张攻击牌的伤害减少 50%。叠加时减少伤害减少幅度，最低 0%。
> 2 层及以上的效果就是：你下一张攻击牌的伤害减少 100%。玩家打出下一张攻击牌时该 debuff 失效。
> 麻痹图标如图，你把背景上的黑扣掉然后把闪电居中，和「下回合能量」buff 区分开。

**改名**（内部名称全部跟着换）：

| 旧 | 新 |
|---|---|
| Key REGENT_CONQUEROR | REGENT_KING |
| 遭遇 REGENT_CONQUEROR_BOSS_ENCOUNTER | **REGENT_KING_BOSS_ENCOUNTER** |
| RegentConquerorBossEncounter | RegentKingBossEncounter |
| Core/ConquerorRegentScript.cs | Core/KingRegentScript.cs |
| BossLineups.ConquerorActive / ComputeConqueror | KingActive / ComputeKing（锁定键 REGENT_KING） |

特殊效果名仍是「**征服者**」（loc key REGENT_BOSS.conqueror / BOSS_CONQUEROR_POWER）。

**新特殊效果**：她每**把一张牌放回抽牌堆顶部**（她这套牌里是 PHOTON_CUT 光子切割 /
COSMIC_INDIFFERENCE 宇宙冷漠）⇒ 所有玩家各得 **1 层「麻痹」**（Powers/ParalysisPower）。
旧的「王之凝视 / 独白」两半（BossConquerorStrengthUpPower）已删除。

**麻痹（ParalysisPower）**：
- ModifyDamageMultiplicative：减伤 = 50% × 层数，封顶 100%（2 层及以上 ⇒ 伤害 0）；
- 只对**该玩家自己打出的攻击牌**生效（cardSource.Type == Attack 且 owner 是他的 Creature）；
- AfterCardPlayed 里发现他打出攻击牌 ⇒ PowerCmd.Remove(this)（那一击吃得到减伤，之后失效）；
- StackType.Counter 叠层，ShouldScaleInMultiplayer = false。

**图标**：es://CharacterBosses/images/powers/paralysis.png
（128×128、透明背景、闪电居中；用户给的图是黄闪电，为和「下回合能量」区分做成了**红色系**渐变 + 深红描边。
源图在 	ools/art/import_proj/CharacterBosses/images/powers/paralysis.png，经 Godot 导入后
三条路径写进 	ools/art/pck_extras.txt 随 PCK 打包。）

⚠ 判据按「卡」而不是按「牌堆事件」：把牌放堆顶在原版是卡牌自己的效果，而「弃牌堆洗回抽牌堆」
同样会触发 AfterCardChangedPiles ⇒ 若挂那儿，她每次洗牌都会白给玩家上麻痹。

## 12. v0.2.323 —— 麻痹：文案 / 音效 / 图标（用户给了游戏内截图）

> 征服者的描述改为：……所有玩家下一张攻击牌的伤害就减少50%。让玩家获得此debuff时和获得其它
> debuff 一样有音效并在玩家头顶短暂显示。这个debuff的图标应该是黄色的，把其说明写的简洁，大概如图。

| 项 | 处理 |
|---|---|
| 征服者描述 | 「每当储君将一张牌放回抽牌堆顶部时，所有玩家下一张攻击牌的伤害就减少 50%。」 |
| 麻痹描述 | 「你本回合打出的下一张攻击牌造成的伤害减少{Reduction}%。」（{Reduction} = 50×层数封顶 100） |
| 音效 | 删掉 ShouldPlayVfx => false —— 反编译 NCreature 的「获得 power」段只有 if (power.ShouldPlayVfx) SfxCmd.Play(...)，就是它把减益音效关了 |
| 头顶提示 | 本来就看 PowerCmd.Apply(..., silent: false)，我们一直是 false，删掉上面那条后完整恢复 |
| 图标 | 改成**黄色**闪电（透明、128×128、居中），Godot 重新导入 |

{Reduction} 的写法照 IroncladRagePower 的 {Loss}：覆写 Description → ase.Description.Add("Reduction", …)。
**不能**用 {Amount}（那是层数，不是百分比）。

## 13. v0.2.324 —— 削弱：进阶 9 不再升级王者之拳

用户：「削弱王储，进阶9不再升级王者之踢」。

⚠ 事实核对：进阶 9 的名单里一直是**王者之拳**（KinglyPunch），**王者之踢从来没被升级过**
（mod_log.txt 里进阶 10 的开局牌表：KINGLY_PUNCH+, KINGLY_KICK, COSMIC_INDIFFERENCE+, WROUGHT_IN_WAR+）。
按「削弱王储」的意图，这次去掉的是 (9, KinglyPunch)：

| 回合 | 之前（进阶 9/10） | 现在（进阶 9/10） |
|---|---|---|
| 2 / 6 王者之拳 | 10 伤、每抽到 +6 | **8 伤、每抽到 +4** |
| 9 王者之拳+ | 剧本点名升级，未受影响 | 同左 |

进阶 9 现在只剩「升级战火铸就」；进阶 8 仍是「升级宇宙冷漠 + 奥利哈钢」。

## 14. v0.2.325 —— 征召上前 + 抱抱先生（进阶 9）

用户：「改一下，战火铸就改征召上前，进阶9改成升级征召上前并添加抱抱先生。确保抱抱先生只会攻击所有玩家，
伤害正确随回合成长，将该伤害在他意图的所有卡牌左边显示，并保证薄荷尖塔能收到这个意图。
加个警告：抱抱先生属于无来源伤害，……（后面和其它哪些一样）」

### 剧本 / 进阶
- 第 5 / 7 回合：WroughtInWar 战火铸就 → **SummonForth 征召上前**（1 费：不论何处把君王之剑放进手牌 + 铸造 8）。
  能量不变（征召上前 1 + 君王之剑 2 = 3）。Core/BladesGrowth 里「凡带 Forge 变量的非能力牌都算进
  后面那张剑的意图」这条**自动**覆盖了它（Forge = 8，升级后 10）。
- 进阶 9：(9, SummonForth) + 遗物 MrStruggles（抱抱先生）；进阶 8 仍是宇宙冷漠 + 奥利哈钢。

### 抱抱先生（Core/MrStrugglesPlays.cs，新）
反编译 MrStruggles.AfterPlayerTurnStart：
`csharp
await CreatureCmd.Damage(choiceContext, combatState.HittableEnemies,
                         Owner.PlayerCombatState.TurnNumber, ValueProp.Unpowered, Owner.Creature);
`
影子玩家不是「回合开始时的那位真人玩家」⇒ 那条钩子从不替她触发 ⇒ 由 BossHookBridge.FireTurnStart
在**她的回合开始**手动结算（CreatureCmd.Damage(ctx, [所有真人玩家], 她的回合数, Unpowered, 她的生物)）：
- **只打所有玩家**：目标 = BossLockOn.RealPlayers，不含影子玩家 / 随从，也不是「只打被锁定那位」；
- **随回合成长**：伤害 = oss.TurnNumber（与 PlayerCombatState.TurnNumber 由 AlignTurnNumber 对齐）；
- 一次传整张列表 ⇒ 游戏里就是一次全体伤害（一处飘字 / 音效）。

### 意图显示
UI/BossHandPanel.Extra.cs：新增 _leftIntent —— 用同一套 NIntent + RawAttackIntent 造一颗，
由 PlaceLeftIntent 摆到 **x = −(宽+间距)**、与其它意图同一高度 ⇒ 正好在**所有卡牌左边**。
数字来源 BossHandPanel.MrStrugglesDamage(boss) = MrStrugglesPlays.UpcomingDamage(boss)
（玩家回合里 = TurnNumber + 1，她自己行动时 = TurnNumber，与手牌栏其它意图同一口径）。

### 薄荷尖塔（MintySpire2）
Core/MintySpireDamagePreviewCompat.PendingOf 里追加这一条（群攻 ⇒ 不做 RandomAimIsElsewhere 判断），
和手牌栏用**同一个数**，两个数字不会打架。

### 文案
REGENT_BOSS.conqueror / BOSS_CONQUEROR_POWER.description 末尾加：
「警告：抱抱先生属于无来源伤害，带有「为你而死」的召唤物不会替你承受该伤害。」

## 15. v0.2.326 —— 抱抱先生意图：数字与位置

用户（截图：第一回合那颗意图上写着 2）：「第一回合应该是1。再往左一点」

1. **数字**：MrStrugglesPlays.UpcomingDamage 去掉了 +1，直接取 oss.TurnNumber。
   原因：PrepareTurn 里 TurnNumber++ 是在她**下一手**生成时做的，而玩家回合里手牌栏显示的正是那一手
   ⇒ 此刻 TurnNumber 已经等于「她即将到来的那个回合」；再加 1 就会多报 1（实测第一回合 2 而实际 1）。
2. **位置**：PlaceLeftIntent 的 x 从 -(宽+间距) 改成 第一张卡意图行左缘 − (宽+间距)，
   其中左缘 = IntentShiftX × 卡宽（−0.58 卡宽）。原来那个位置会压到第一张卡自己的意图上。

## 16. v0.2.327 —— 修「抱抱先生打两次」

用户：「第一回合受到了1点伤害2次。是不是效果重复了？」

**是重复了**，日志可证：抱抱先生：她第 1 回合开始 → 对 1 名玩家各 1 点无来源伤害（我们那次，只有一次），
可玩家挨了两下 ⇒ 另一下来自**原版** MrStruggles.AfterPlayerTurnStart。

根因：影子玩家的遗物确实在 IterateHookListeners() 里（和红面具 / 破损核心同一类坑），
「真人玩家回合开始」这件事也会广播到她的遗物 ⇒ 原版按 PlayerCombatState.TurnNumber 打了一次，
我们在**她的回合开始**又打了一次。

修法：新增 Patches/ShadowMrStrugglesPatch.cs（打法同 ShadowRedMaskTurnStartPatch，打在具体类型上），
owner 是我们的 Boss 生物时把原版那次改成 Task.CompletedTask；真人玩家自己的抱抱先生不受影响。
结算口径不变：她的回合数、所有真人玩家、ValueProp.Unpowered。