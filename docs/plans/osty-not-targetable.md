# 死掉的奥斯提仍被选靶并掉血（2026-10-23 修）

> 测试玩家反馈原文（第一批 #2）：「死亡的奥斯提仍被『转嫁疼痛』选中并掉血」。
> 状态：**已修、已构建 0 错误、已装进游戏（v0.1.13）**，待实机复测。

## 1. 「转嫁疼痛」到底是谁

它是**崩坠（mods\Downfall）**里收藏家（Collector）的牌：
`Collector.FeelMyPain` → 施加 `Collector.CollectorCode.Powers.FeelMyPainPower`。

怎么确认的：不想解包 83MB 的 `Downfall.pck`，写了 `tools/pck_builder/find_in_pck.gd`
（把 pck 挂进 Godot 再全文搜文本表）：

```
res://Collector/localization/zhs/cards.json  :: "COLLECTOR-FEEL_MY_PAIN.title": "转嫁疼痛",
res://Collector/localization/zhs/powers.json :: "COLLECTOR-FEEL_MY_PAIN_POWER.title": "转嫁疼痛",
```

它的实现（反编译 `FeelMyPainPower.<AfterCardExhausted>d__0`）：

```csharp
if (card.Owner.Creature == Owner) {
    var target = CombatState.RunState.Rng.CombatTargets
                     .NextItem(CombatState.HittableEnemies);   // ← 随机挑一只「可命中敌人」
    ...
}
```

也就是说：**它完全走 `HittableEnemies`**，没有别的花招。

## 2. 根因不是「死不掉」，而是过滤根本没跑

`Creature.IsAlive => CurrentHp > 0`、`IsDead => !IsAlive`、`IsHittable => IsDead ? false : …`，
所以血 0 的那只**本来就不该**出现在 `HittableEnemies` 里。真正的问题是**我们把过滤整段跳过了**：

`Patches/CombatSidePatches.cs` 里那两个补丁（`ShadowPetNotHittablePatch`、
`ShadowPetNotAnOpponentPatch`）开头都有一句「没随从就零开销放行」：

```csharp
if (!CharacterBosses.Core.BossShadowPets.HasKnownPets) return;
```

`HasKnownPets` 只统计**装饰性随从**（佩尔的士兵那一类，来自 `BossShadowPets.Known`）。
而**亡灵契约师没有装饰性随从**——她只有奥斯提，奥斯提按设计不进 `Known`（它要真承伤），
于是闸门永远是 `false` ⇒ 奥斯提从来没被摘出过 `HittableEnemies` / `GetOpponentsOf`。

第二个洞：`BossOsty.IsBossOsty` 靠 `PetOwner ?? Player` 判归属，而日志早就算过
「`PetOwner` 有时是 null」。两只都拿不到时它 `return false`，于是即便闸门放行，
这只奥斯提也算「不是她的」⇒ 照样留在选靶列表里。

## 3. 改了什么（三处，一个主题）

| 文件 | 改动 |
|---|---|
| `Core/BossShadowPets.cs` | 新增 `BossFightActive(ICombatState)`：这间房里有没有我们的角色 Boss，按战斗实例缓存（只在换战斗时扫一遍） |
| `Patches/CombatSidePatches.cs` | `HittableEnemies` / `GetOpponentsOf` 两个补丁的闸门改成 `HasKnownPets \|\| BossFightActive(...)`；顺带把 `!IsAlive` 的生物一律摘掉；第一次摘掉奥斯提时打一行日志（每场一次，`ResetDiagnostics` 里重置） |
| `Core/BossOsty.cs` | `IsBossOsty` 归属拿不到时退回**阵营**判据：`creature.Side == Enemy` 就算她的（这个 mod 里敌人侧只可能是我们的 Boss） |

设计不变：奥斯提仍然「替亡灵契约师挡下未被格挡的攻击伤害」（走原版
`ModifyHpLostAfterOsty` 那个钩子），只是**不再**进任何「选靶列表」——
玩家点不到、群攻扫不到、随机选靶挑不到。

## 4. 实机怎么确认修好了

`mod_log.txt` 里应该出现（每场一次）：

```
[CharBoss] 奥斯提已从「可命中敌人」里摘掉（选靶不会再挑它；本场只报一次）
```

再拿收藏家打一场、疯狂消耗牌触发「转嫁疼痛」：伤害应该落到**亡灵契约师本人**身上，
奥斯提头上不再冒伤害数字。若还看得到，把 `mod_log.txt` 里
`⚠ …受到了伤害` 那一行（`Patches/CombatSidePatches.cs` 的探针只对**装饰性随从**生效，
奥斯提需要另加探针）连同时间点一起给我。

## 5. 已知未覆盖

- 没有给奥斯提加「掉血探针」（`ShadowPetDamageProbePatch` 只认装饰性随从）。如果复测还漏，
  下一步就是把它也挂上奥斯提，直接看是哪条路径绕过了列表过滤。
- `HittableEnemiesSidePatch`（敌方回合把「可命中敌人」解释成玩家侧）没动。
