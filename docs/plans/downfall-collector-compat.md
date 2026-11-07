# 与「崩坠」(mods\Downfall) 收藏家的兼容

> 用户 2026-10-23：兼容 **mods 文件夹里那一份**（`mods\Downfall\Downfall.dll`，不是创意工坊版），
> 让收藏家击败我们的 Boss 后掉落/绑定对应「藏品牌」（牌都是原版的）。
> 同目录的 `catfall2.dll` 是「守护者」重制，**没有收藏家**，不在范围内。

## 反编译到的它自己的 API（不自创）
| 类型/方法 | 说明 |
|---|---|
| `Collector.CollectorCode.Rewards.CollectibleReward(CardModel card, Player player)` | 藏品奖励，`RewardsSetIndex = 9`，描述里要 **3 点精华**（`Player.CanAffordEssence(3)`） |
| `CollectiblesModel.SyncAdd(Player, CardModel, int essenceCost)` | **public**；`CollectibleReward` 取走时走的就是它：`SpendEssence` → `AddCollectible` → 飞入藏品堆动画 → 多人同步（`LocalContext.IsMe`） |
| `CollectiblesModel.AddCollectible(Player, CardModel)` | internal；只写进 `CollectorDeck` 并刷新顶栏按钮，**不扣精华** |
| `CollectiblesModel.GetCollectibles(Player)` / `ClearCollectibles(Player)` | 读/清空藏品册 |

## 掉落表（Boss → 藏品牌，原版牌）
| Boss（定义 Key + 幕） | 藏品牌 | 类名 |
|---|---|---|
| 愤怒战 `IRONCLAD_BOSS` 1 | 好勇斗狠 | `Aggression` |
| 灾厄骨 `NECROBINDER_BOSS` 1 | 苦难 | `Misery` |
| 涂毒猎 `SILENT_BOSS` 2 | 涂毒 | `Envenom` |
| 灵魂骨 `NECROBINDER_BOSS` 2 | 彼岸一窥 | `GlimpseBeyond` |
| 铸剑储 `REGENT_BOSS` 2 | 锻打成型 | `BeatIntoShape` |
| 能力机 `DEFECT_BOSS` 3 | 创造性AI | `CreativeAi` |
| 无色储 `REGENT_BOSS` 3 | 武器库 | `Arsenal` |
| 刀猎 `SILENT_BOSS` 3 | 墨之刃 | `BladeOfInk` |
| 根除骨 `NECROBINDER_BOSS` 3 | 根除 | `Eradicate` |

## 实现
`Core/DownfallCollectibleCompat.cs`（全部**反射**调用，csproj 不引用 Downfall.dll；没装崩坠就静默跳过）：
- 触发点：`CharBossMonster.BeforeRemovedFromRoom` 且 `Creature` 已死（= 打赢了）；
- 只处理**本机是收藏家**（角色类型全名 `Collector.CollectorCode.Core.Collector`）；
- 1/2 层：`SyncAdd(player, card, 3)`（= 收藏家取走藏品奖励那一步，扣 3 精华；精华不够就不发，和它自己的 `CANT_AFFORD` 一致）；
- 3 层：`AddCollectible(player, card)`（三层没有战斗奖励，只**绑定进藏品册**，不扣精华）。

## 已知未完成的一小步
「藏品奖励那一行**显示在胜利结算界面**」还需要注入点（它自己没有 reward-screen 的 patch，
`CollectibleReward` 是在别处 new 出来的，尚未定位）。现在走的是 `SyncAdd` ——
效果等价于「玩家在结算界面点了取走」，但不显示那一行。要那一行的话，下一步继续翻它的代码。

## 2026-10-23 续：找到它的「挂奖励」位置（已实现）
反编译 `Collector.CollectorCode.Core.CollectorModel`：
- `AfterDeath(ctx, creature, ...)`：把死掉的**敌方 MonsterModel** 记进 `_defeatedEnemies`；
- `AfterCombatEnd(CombatRoom room)`：
  - 取 `ModelDb.CardPool<CollectibleCardPool>().AllCards.OfType<ICollectible>()`，
    按 `c.GetMonsterModel().Id == 被打死的怪物.Id` 配出对应的藏品卡；
  - 按房间类型给精华（1/2/3）：`room.AddExtraReward(p, new EssenceReward(n, p))`；
  - 每张藏品卡：`room.AddExtraReward(p, new CollectibleReward(card.ToMutable(), p))`；
  - 只对 `p.Character is Collector` 的玩家执行。

⇒ 掉落**就是在这个方法里**挂到结算奖励上的，且 `CombatRoom.AddExtraReward(Player, Reward)` 是 **public**。
实现方式（不自创）：在 `BuildInfo.LogOnce`（每次会话只跑一次、此时程序集都加载好）里**动态**给
`CollectorModel.AfterCombatEnd(CombatRoom)` 挂一个 Harmony **postfix**（反射找方法，不引用 Downfall.dll）：
postfix 里对「本场被我们打死的 Boss」按掉落表 `new CollectibleReward(card.ToMutable(), player)`
→ `room.AddExtraReward(player, reward)` ⇒ **结算界面会出现那一行藏品奖励**（花 3 精华取走，走它自己的 OnSelect→SyncAdd）。
3 层照旧直接 `AddCollectible` 绑定（没有战斗奖励）。动态挂载失败时回退到 SyncAdd。

## 2026-10-23 续 2：藏品牌的「虚无 / 消耗」为什么之前不显示（已修）
实测：它自己那批藏品牌（继承 `Cards.Token.Collectible`）在**模型层**声明了 `CanonicalKeywords`
⇒ 藏品册重建出来照样有；而我们的牌是**原版牌**，只能在**实例**上加关键词。
但藏品册 `CollectiblesModel.GetCollectibles(Player)` 是用 `CardModel.FromSerializable(...)` **重建**的 ——
序列化里只存 id / 升级 / 附魔，**不存关键词** ⇒ 实例上加的会丢。

修法：除了造牌时加关键词，再给 `CollectiblesModel.GetCollectibles(Player)` 挂一个 Harmony **postfix**，
把返回列表里**属于我们掉落表那 9 张**的牌补上 `Ethereal` + `Exhaust`（其余牌一律不碰）。

## 2026-10-23 续 3：为什么第一次挂 GetCollectibles 没生效 + 关键词规则订正
1. **没生效的根因**：GetCollectibles 在 **CollectiblesModel** 上，我却在 CollectorModel 的类型上找它
   ⇒ 反射拿到 null、静默跳过（日志里只有 已挂上 CollectorModel.AfterCombatEnd，没有 GetCollectibles 那行）。
   现在改成 FindType(CollectiblesModelFullName).GetMethod("GetCollectibles", …)。
2. **关键词规则（用户订正）**：
   - **能力牌（CardType.Power）**：只有 **虚无**，**不带消耗**；
   - **技能 / 攻击牌**：**虚无 + 消耗** 都有。
   统一走 ApplyCollectibleKeywords(card)：造牌时一次 + GetCollectibles postfix 一次（重建后补）。

## 2026-10-23 续 4：**3 层的唯一正常途径「思维抽取」抽不到我们的藏品**（已修）

用户反馈：「灵魂汲取无法偷取 boss 的对应藏品（这是获得 3 层 boss 藏品的唯一正常途径）」。

**先正名**（用 `tools/pck_builder/find_in_pck.gd` 在 pck 里全文搜）：
- 字面上的「灵魂汲取」只出现在**原版**里：`res://localization/zhs/monsters.json ::
  "LAGAVULIN_MATRIARCH.moves.SOUL_SIPHON.title": "灵魂汲取"`（拉加维林女族长的招式），
  跟藏品无关；
- 崩坠里唯一「偷敌人藏品牌」的牌是 **思维抽取 / Brain Drain**：
  `"COLLECTOR-BRAIN_DRAIN.description": "敌人失去{Damage}点生命。\n获得敌人的藏品牌{IfUpgraded:show:+|}。"`
  （0 费、攻击、罕见；英文描述 `Add their Collected card into your Hand`）。
  ⇒ 用户说的应该就是它，下面按它做。

反编译 `Collector.CollectorCode.Cards.Uncommon.BrainDrain.<OnPlayInternal>d__3` 的关键段：

```csharp
if (FirstOrDefault(CollectibleCardPool.AllCards.Cast<ICollectible>(),
        e => e.GetMonsterModel().Id == cardPlay.Target.Monster.Id) is CollectorCardModel c) {
    var card = CombatState.CreateCard(c, Owner);
    if (IsUpgraded) CardCmd.Upgrade(card, CardPreviewStyle.HorizontalLayout);
    CardPileCmd.AddGeneratedCardToCombat(card, PileType.Hand, Owner, CardPilePosition.Bottom);
}
```

两个坑：
1. 它按**怪物 id** 去它自己的藏品牌池里找牌 —— 我们的 Boss 是原版牌换皮，池里没有对应项；
2. 那个集合是 `CollectibleCardPool.AllCards.Cast<ICollectible>()`，只含**它自己的**藏品牌（还必须转成
   `CollectorCardModel`）⇒ 我们的原版牌**根本不在里面**，光改判断条件没用（试过，行不通）。

**修法（只管发牌这一步，不碰伤害）**：`BuildInfo.LogOnce → DownfallCollectibleCompat.TryInstall`
里再动态挂一个 Harmony **postfix** 到 `Collector.CollectorCode.Cards.Uncommon.BrainDrain.OnPlayInternal`：

- postfix 声明 `(object __instance, CardPlay cardPlay, ref Task __result)`；
- 只看 `cardPlay.Target.Monster is CharBossMonster`（打的是我们的 Boss），
  查同一张掉落表 `LootTable[(Def.Key, ActIndex+1)]`；
- 把 `__result` 换成「先 `await` 原版 Task（伤害照打），再 `CreateCard` + 必要时 `CardCmd.Upgrade
  (HorizontalLayout)` + `CardPileCmd.AddGeneratedCardToCombat(card, PileType.Hand, player,
  CardPilePosition.Bottom)`」——**参数和它自己那行一模一样**；
- 造牌走我们已有的 `CreateCard`，所以照样带「虚无 / 消耗」，也照样进藏品册后由
  `GetCollectibles` postfix 补关键词。

日志验证：`[CharBoss] 收藏家兼容：已挂上 思维抽取（BrainDrain）→ …`，抽到时
`[CharBoss] 收藏家兼容：思维抽取从 <Boss> actN 抽到了藏品牌「<牌名>」`。

