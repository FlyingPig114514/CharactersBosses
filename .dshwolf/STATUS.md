# STATUS

> **2026-10-29 晚（第二个会话）**：跟踪锁定图标数量按**方案 B（改渲染层）**修了，见下面 v0.2.148。
> 用户拍板：① 图标走方案 B（保留玩家侧图标、改渲染层回收残留节点）；② 站位**不需要**「猎人永远最左」，
> 按行动次序就好；③ **等用户说「打包」再打 zip**（用户 2026-10-30「打包」⇒ `.dist` 已更新到 **v0.2.291**，
> `CharacterBosses-v0.2.291.zip` 13,161,856 B（2026-11-02 本地打包），安装说明已写入 v0.2.291 变更）。
**当前版本：v0.2.386 / 2026-11-04**（击晕 + A/B/C/D 四项 + **皮肤 mod 兼容第三层**；`.dist\CharacterBosses-v0.2.386.zip` 已出包、工坊 content/json/changenote 已刷新，**只等上传**）

> ### 追加：v0.2.386 —— 玩家反馈「铁甲战士皮肤『绯刃战姬』换不了 Boss 皮肤」（已实机验证）
>
> **机制（反编译工坊那个「空洞骑士」皮肤 `Hcxmmx_Hollow_Knight_Skin.dll` 得出的）**：
> 皮肤 mod 有三种做法，我们原来只覆盖了前两种 ——
> 1. 改 `CharacterModel.VisualsPath` / 用 pck 覆盖同路径资源 → `CharBossMonster.VisualsPath` 委托给角色模型，能继承；
> 2. 改 `CharacterModel.CreateVisuals()` → `BossCharacterVisualsPatch` 直接调 `character.CreateVisuals()`，能继承；
> 3. **在 `NCreature._Ready` 的后置补丁里判 `entity.Player?.Character.Id.Entry`，再把自己的场景塞进立绘节点**
>    —— 前两层一点都管不着（它压根不读那两个入口）。
>
> 第 3 类的判据是 `Player player = entity.Player; if (player == null || entry != "NECROBINDER") return;`
> 而我们**在 `NCreature._Ready` 那一刻 `Entity.Player` 还是 null** —— 影子玩家是进房后 `AfterAddedToRoom`
> 才绑的（日志实证：先「立绘实例来自角色模型 CreateVisuals()」、下一行才「shadow player ready」）
> ⇒ 这类皮肤一律 return，**Boss 永远是原版模型**。**这就是玩家反馈的根因。**
>
> **为什么不「提前绑影子玩家」**（这条路是死路，别再试）：`Creature.IsPlayer => Player != null`（反编译，
> **没有** Monster 判据）⇒ 在 `_Ready` 之前绑上会让原版：
>   · `if (Entity.IsPlayer)` → 给这只怪**挂一个原版 `NOrbManager`**（和 `BossOrbDisplay` 打架）；
>   · `_isRemotePlayerOrPet = IsPlayer && !LocalContext.IsMe(Entity)` → **血条被直接隐藏**；
>   · `NCombatRoom.AddCreature` 的 `if (creature.IsPlayer || PetOwner != null)` → **把她摆到玩家那一侧**。
> 三个都得额外兜，而 `IsPlayer` 这种一行 getter 极可能被 JIT 内联、补丁不一定生效。
>
> **做法**（`src/Hooks/Patches/SkinReadyPatchReplay.cs`）：影子玩家照旧在 `AfterAddedToRoom` 绑好之后，
> 用 Harmony 的补丁表把**别的模组挂在 `NCreature._Ready` 上的后置补丁**对我们这只 Boss 的节点**补跑一次**
> （此时 `Entity.Player != null`，皮肤的条件就过了，它会自己去隐藏原立绘、挂上自己的场景）。
> 三条保守约束：每只生物**只补一次**（这类补丁多半不幂等 —— 那个皮肤每调一次就 AddChild 一个场景）；
> 只调 `static void X(NCreature)` 这一种签名，别的形态不猜；跳过我们自己的补丁 + 每个都 try/catch。
> （Harmony 这个版本的 `Harmony.GetPatchInfo` 是 internal ⇒ 反射调用；`Patches.Postfixes` 是公开字段。）
>
> **实机验证（v0.2.386，临时把工坊「空洞骑士」皮肤装进 `mods\` 做实证，验完已删除）**：
> · 我方日志：`皮肤补跑：对这只 Boss 补跑了 7/7 个「别的模组的 NCreature._Ready 后置补丁」（触发点=AfterAddedToRoom…）`
> · 截图：玩家（小骑士）+ **Boss 也变成了小骑士**（改之前她是原版亡灵契约师立绘）。
> · ⚠ 那个皮肤自己的 `HollowGlobals.Log("🎯 目标确认！强行挂载小骑士！")` 没进 `godot.log`，
>   但它确实生效了（看画面），所以别拿那行日志当判据。
>
> **给下一个会话**：如果还有皮肤类反馈，先让对方发 `mod_log.txt`，看这两行就能定位是哪一层：
> `立绘用角色模型的 VisualsPath：…（⚠ 与缺省路径不同，可能来自皮肤 mod）` /
> `立绘实例来自角色模型 CreateVisuals()` / `皮肤补跑：…补跑了 N/M 个…`。
>

> ### 追加：v0.2.385 打包（用户 2026-11-04 23:06「打包」→ 已被 v0.2.386 包取代，见上）
>
> · 当时出的包是 `.dist\CharacterBosses-v0.2.385.zip`（13,276,966 B）；
>   **现在是 `.dist\CharacterBosses-v0.2.386.zip`（13,278,128 B，dll MD5 `FA6B17C613CE`）**，
>   内部结构不变：`CharacterBosses\{CharacterBosses.dll, .json, .pck, 安装说明.txt}`；
>   pck MD5 `5CE2B833`（`content/` 自 v0.2.375 起没动过，四处 pck 同一个文件）。
> · **工坊侧已刷新到 v0.2.386**：`workshop\content\CharacterBosses\` 三件、`workshop\changenote.txt`
>   （v0.2.375 → v0.2.386 的全部用户可见变化，含皮肤兼容那段）、`workshop\workshop.json`（重生成，6148 字节）。
> · **上传命令**（Steam 必须运行并已登录；物品 id 在 `workshop\mod_id.txt` = 3830639796，别删）：
>   ```
>   powershell -ExecutionPolicy Bypass -File tools\upload-workshop.ps1
>   ```
> · 写 changenote 时发现 376～382 只有构建标签留在日志里、STATUS 里没有对应段落，
>   所以在 `mod_log.old.txt` / `mod_log.txt` 里把 `构建：v0.2.3xx（…）` 全部捞出来当素材（这招很灵，以后可以复用）。

> ### 追加：v0.2.385 —— 用户两条报障定案 + 四件改动（A/B/C/D）
>
> **用户报障**：①「打养手骨时我的奥斯提有的时候不能承担伤害，是不是『不死』影响了我」；
> ②「打辉星储君时意图显示的伤害不足以打死我，但实际把我打死了，是王者之拳还是万向斩？」
>
> **查清（反编译 + `mod_log.old.txt` 实证）**：
>   · **不是「不死」串到玩家身上** —— `BossOstyUndyingPower.IsOwnBossOsty` 三条判据
>     （影子玩家的随从 + `PetOwner.Creature.Monster is CharBossMonster` + 那只 Boss 带这层 power）
>     对玩家自己的奥斯提全部不过。
>   · **原版「为你而死」只挡有力量加成的攻击**：`DieForYouPower.ModifyUnblockedDamageTarget` 里
>     `if (!props.IsPoweredAttack()) return target;`，而
>     `IsPoweredAttack(props) = HasFlag(Move) && !HasFlag(Unpowered)`。
>     ⇒ **一切「无来源」（Unpowered）伤害奥斯提都不挡**：黑洞（`BlackHolePower.AfterStarsGained` →
>     `CreatureCmd.Damage(HittableEnemies, Amount, Unpowered, Owner)`）、群蛇形态 4、充能球被动、
>     炸弹、冰雹风暴、卡戎之灰。
>   · **王者之拳的意图是对的**：成长在造牌时补进 `Damage.BaseValue`（日志实证
>     `王者之拳成长：这次 +6 ⇒ 累计 +12；本回合这张牌伤害 22`），意图喂的就是这个 22 再过一遍
>     原版 `Hook.ModifyDamage`（`意图伤害管道 KINGLY_PUNCH: 牌面 22 → 33（目标易伤=1）`）。
>   · **万向斩（`Omnislice`）其实是两段**：第一段 8（升级 11）`Move`；第二段对
>     `GetTeammatesOf(receiver) − 目标` 造成 `实伤+溢出` 点 `Move|Unpowered`。
>     它走的是「同侧生物列表 + `CreatureCmd.Damage`」，**既不走 `HittableEnemies` 也不走
>     `AttackCommand`** ⇒ 绕过我们已有的两条「她的攻击只打真人玩家」闸门 ⇒
>     单人局里那份列表是「你 + 你的奥斯提」⇒ 第一段被改派到奥斯提、第二段又打它一份 ⇒
>     **奥斯提一条命顶两下**，紧接着的王者之拳就落在玩家身上。
>   · 另外 `BossIntentBuilder` 那句注释写的「那一刻的伤害由**回合开始那一条**表示」其实**从来没实现**。
>
> **本轮四件改动**：
>   · **A** `BossHandPanel.BlackHoleAtTurnStart` + `BuildExtraIntents`：黑洞「下回合辉星到账」那一笔
>     现在画在**所有卡牌左边**（与抱抱先生同列，按发生顺序）；`MintySpireDamagePreviewCompat.PendingOf`
>     也补上了同一个数（薄荷尖塔的「←N」原本也不算这一笔）。
>   · **B** 新文件 `Patches/OmnisliceSplashRealPlayersOnlyPatch.cs`：给
>     `CreatureCmd.Damage(ctx, IEnumerable, amount, props, dealer, cardSource, cardPlay)`
>     挂 Prefix，只对「她的万向斩 + `Unpowered|Move`（就是溅射那一段）」生效，把列表过滤成真人玩家。
>     列表可能被清空 ⇒ 不用动 `__result`，绕开了 async 那条铁律。
>   · **C**（纯诊断）`BossDamagePredictor.LogDamagePipeline` 从「全局 40 行硬上限」改成
>     **按「牌名 + 数值变化」去重**（原来一张 ASTRAL_PULSE 就能把额度烧光，真正要查的牌一行都不剩 ——
>     这次排查就卡在这里）；`CharBossMonster.LogPlayerSideSnapshot` 在她出手前打一行
>     「玩家侧都有谁、HP/格挡/关键 power」，以后再有人说「奥斯提没挡」能直接判。
>   · **D** 回响斩击按斩杀链动态显示段数：`BossDamagePredictor.EchoingSlashHits` 完全照抄
>     原版的 `while (attackCount > 0)` 循环做推演，`BossIntentBuilder` 里走 `LiveMultiAttackIntent`
>     ⇒ 每帧重算。**含「替主人承伤而死的奥斯提」**（反编译确认 `DamageResult` 是拿改派后的目标建的，
>     所以 `WasTargetKilled` 为真、原版真的会多打一段）。
>
> 版本 **v0.2.385**；dll 926,208 B、MD5 `7DE843E88D0A`（src / 游戏 / 工坊三处一致）；已装游戏 + 工坊两处。
>
> **本轮实机验证（22:49～23:0x，`silent@2` = 二层涂毒猎 + 亡灵契约师）**：
>   · ✅ **D 回响斩击**：`意图 ECHOING_SLASH: [LiveMultiAttackIntent]`（不再是单段意图），
>     推演日志 `回响斩击推演：牌面 11，先扣掉本回合前面那些牌 N 点，[hp=… block=… 奥斯提=… 每段=…] ⇒ 显示 K 段`。
>     **用户看屏幕纠过我一次**：显示 2 段时她指出「回响斩击**之前**那张打击已经把奥斯提打死了，所以并没有 2 段」
>     —— 对，推演只看了「此刻」，没算同一回合前面的牌。补了 `PriorPlannedAttackDamage`（按 `Plan` 顺序把
>     还没打出来的先行攻击伤害预扣到模拟状态上，`Plan.Remove` 保证不会双算）。
>     **同一局面 A/B：改之前 2 段、改之后 1 段**，与真实结算一致。
>     ⚠ 另一个坑：推演日志原来打印的是**跑完之后**的值（「奥斯提=0」分不清是一开始就没有还是被打死的），
>     现在改成打印**推演之前**的快照并额外打印「先扣掉 N 点」。
>   · ✅ **C 诊断**：`玩家侧快照（她第 N 回合出手前）：亡灵契约师(真人玩家) hp=… | 奥斯提(宠物) hp=1/1 … powers=[…,DIE_FOR_YOU_POWER]`
>     —— 修的时候发现**玩家的宠物不在 `combat.PlayerCreatures` 里**（只在 `Player.Pets` 上），已补一条遍历；
>     这一行本身就是「奥斯提当时在不在、有几滴血、有没有『为你而死』」的直接答案。
>   · ✅ **A 黑洞回合开始的独立意图 —— 已实机验证**（用户手工把 `REGENT_STAR_BOSS_ENCOUNTER` 开出来，
>     23:02:58～）：`独立意图（黑洞（下回合辉星到账））: 3 点`，与随后真实结算的
>     `黑洞：花星 3 → 对 1 名玩家各 3 点（无来源）` 完全对上。
>   · ⏳ **B 万向斩溅射 —— 补丁挂上了，但这个局面下**触发不到**：反编译的第二段是
>     `GetTeammatesOf(第一段实际挨打的那位) − 目标`，而这只 Boss 的万向斩（8＋力量=9）
>     第一段就把玩家那只奥斯提打死了（这场它最大只有 7 血）⇒ `IsHittable` 过滤后列表是空的
>     ⇒ 原版**根本不会调** `CreatureCmd.Damage` ⇒ 我的 Prefix 连被咨询的机会都没有。
>     ⇒ 需要「奥斯提血量 > 万向斩第一段伤害」才能真正看到 `万向斩溅射：剔掉 N 个召唤物/宠物`
>     （用户当初那一局就是这样：奥斯提更厚，第一段顶住了、第二段又打它一份）。
>     结论：**加载全程无 Harmony 报错 ⇒ 补丁确实挂上了**（`PatchAll` 找不到目标方法会抛异常），
>     但这一条的正向效果还差一次「厚奥斯提」的实机确认。
>

> ### 追加：v0.2.383 → v0.2.384 —— Boss 被击晕时的整套行为（用户 2026-11-04 需求，已实机验收）
>
> 需求：被吹哨等牌击晕时**立刻**压暗所有牌 + 撤掉所有意图 + 本回合不行动；**下回合补打**
> 上回合没完成的那份计划（连晕几回合也只补一次那一套）。完整记录见 `docs/plans/stun-behavior.md`。
>
> **反编译（本次新查）**：
>   · `CreatureCmd.Stun(creature, nextMoveId=null)` → `Stun(creature, _ => Task.CompletedTask, nextMoveId)`
>     ⇒ **stunMove 是空 Task**，击晕什么都不做，真正起作用的是「把移动状态机切到 STUNNED」；
>   · `Creature.StunInternal` 新建 `MoveState("STUNNED", stunMove, new StunIntent()){ FollowUpStateId=nextMoveId,
>     MustPerformOnceBeforeTransitioning=true }` 再 `Monster.SetMoveImmediate(state)`；
>   · **`Creature.IsStunned => Monster?.NextMove.Id == "STUNNED"`**，而 `NextMove` 只由 `MonsterModel.RollMove` 赋值；
>   · `MoveState.CanTransitionAway = MustPerformOnceBeforeTransitioning ? _performedAtLeastOnce : true`
>     ⇒ 被晕期间 `RollMove` 第一行就 return（不推进状态机、也不调 `BossThinkState.GetNextState`）。
>
> **结论（关键洞察）**：vanilla 那套 STUNNED 移动状态机**天生**给了我们想要的一切 ——
> `TakeTurn → PerformMove` 跑的是空 Task ⇒ `PlayPlannedCards` 根本进不去 ⇒
> `Plan` / `PlanReady` / `TurnNumber` / 手牌**全部原封不动**。
> 唯一会毁掉「下回合补打」的是**下一个玩家回合开始时那次 `PrepareTurn`**（STUNNED→THINK→PLAY 会经过 THINK）。
> 所以**没有接管 `CreatureCmd.Stun`、没有手改 `NextMove`**，只加了一道闸门。
>
> 改动（7 处）：
>   1. `CharBossMonster.Stunned`（读 `Creature.IsStunned`，异常一律当 false）；
>   2. `PrepareTurn()` **开头**（`Plan.Clear()` 之前）`if (Stunned) { 日志; return; }`
>      —— 这道闸门天然只在「她上一轮被晕」时成立（RollMove 在玩家回合开始跑，吹哨一定在其后）；
>   3. `BossHandPanel`：`IsBright()` 被晕时一律 false、`BuildCell` 传 `planned=null`
>      （⇒ 卡面压暗 `(0.42,0.42,0.48)` + 不建意图行）、原地刷新分支同样置空；
>   4. `BossHandPanel.Extra.BuildExtraIntents`：被晕时清完旧节点直接 return（冰雹/炸弹/球被动/抱抱先生全撤）；
>   5. `BossPotionRow`：被晕时不画「本回合要喝」的药水箭头（图标照旧，那是持有物）；
>   6. `BossStatusRow.Fill` 的**内容指纹加了「是否被晕」一位**（不加的话指纹不变 ⇒ Fill 直接 return，药水箭头顶着不走）；
>   7. `BossFrameTickPatch`：每帧比对 `boss.Stunned`（`LastStunned` 字典，每只一份），翻转即
>      `BossHandUi.RefreshForce` + 一行日志（这就是「立刻」的来源）。
>   另：`BossIntendsToAttackPatch` 被晕时回答「不攻击」；`PublishVanillaIntents` 被晕时发布**空意图**
>   （既不 return 留旧值、也不发计划里的假伤害，第三方模组读到「不会造成伤害」才是对的）。
>
> **实机验收（v0.2.383，22:14～22:17，日志实证）**：临时加过 `StunSelfTest.cs`（mod 目录放
> `charbosses_stun.txt`=回合号，到那回合 1.2 秒后调 `CreatureCmd.Stun`，与吹哨同一入口）+ `charbosses_debug.txt`
> 把第一场普通战斗换成铁甲战士 Boss。实测意外发现**玩家卡组本来就带「吹哨」**，自动打牌时它自己也在反复晕她，
> 两条路径表现完全一致：
> ```
> 剧本第 2 回合: 打 [BREAKTHROUGH,TRUE_GRIT] …            ← TurnNumber=2，Plan=2
> 击晕：本回合不行动 → 手牌全部压暗、撤掉全部意图（…TurnNumber=2）
> 击晕：跳过本回合的规划重建（…TurnNumber 停在 2，PlanReady=True）
> 击晕解除：恢复意图显示，补打待执行计划 2 张（仍是第 2 回合的那一套）
> 击晕：本回合不行动 → …（连晕第二回合）
> 击晕：跳过本回合的规划重建（…TurnNumber 停在 2）
> 敌方行动入口：PlayPlannedCards（第 2 回合，Plan=2 张）  ← ★ 补打的就是被打断的第 2 回合
> 剧本第 3 回合: 打 [AGGRESSION,BRAND] …                ← ★ 接着 N+1，不是 N+2
> ```
> 被晕那两回合之间**没有任何** `敌方行动入口` 行（`godot.log` 里是 `performing move STUNNED`）；
> 全程面板 `visible=True`、重建 0.1～1.1ms、`异常=0`、无 `NIntent failed`。
> **验完已删自检代码与两个开关文件**，重 build 成 **v0.2.384**（dll 918,016 B）。
>
> ⚠ 顺带发现（**与本次无关、没动**）：这台机器游戏窗口 1707×1067、UI 坐标空间按 1920 宽排，
> Boss 立绘与手牌栏贴右缘 ⇒ 截图里手牌栏最右那张卡被屏幕边裁掉一点。
>

> ### 追加：v0.2.375 打包 + 工坊上传准备（用户 2026-11-04「打包，并且告诉我上传创意工坊的命令」）
>
> **本地包**：`.dist\CharacterBosses-v0.2.375.zip`（13,272,336 B）
> 内容 = `CharacterBosses\{CharacterBosses.dll, .json, .pck, 安装说明.txt}`（与历史包结构一致）；
> `安装说明.txt` 首行版本号已改成 v0.2.375；三处 dll 的 MD5 都是 `1811F454FC`（src / .dist / workshop）。
>
> **工坊侧已刷新**（用户只需执行上传）：
>   · `workshop\content\CharacterBosses\` 三件换成 v0.2.375（dll 915,456 / pck 13,251,720）；
>   · `workshop\changenote.txt` 重写为 v0.2.375 的完整更新说明（约 1.2 KB）；
>   · `workshop\workshop.json` 已用 `tools\mk-workshop-json.ps1` 重新生成（changeNote 已带新说明）。
>
> **上传命令**（Steam 必须运行并已登录）：
> ```
> powershell -ExecutionPolicy Bypass -File tools\upload-workshop.ps1
> ```
> 物品 id 已存在：`workshop\mod_id.txt` = **3830639796**（**别删这个文件**，更新靠它匹配）。
> 其它用法：`-Args 'remove'` 下架、`-Help` 看帮助。
>
> ### 追加：v0.2.375 —— 小刀基础伤害是 6（升级小刀），不是 4
> 用户：「这回合这两把小刀的实际伤害是 18 和 9」⇒ 18=(6+3+9)、9=(6+3)。
> 反编译 `HiddenDaggers.OnPlay`：`if (IsUpgraded) CardCmd.Upgrade(current);` ⇒ **升级版隐秘匕首印的是升级小刀**；
> `Shiv` = `DamageVar(4m)`、`OnUpgrade` = `UpgradeValueBy(2m)` ⇒ 6 点。我们一直把 `ShivBaseDamage = 4` 写死。
> 新增 `GeneratesUpgradedShivs(card)`（目前只有 `HIDDEN_DAGGERS` + `IsUpgraded`），
> `ShivDamagePerHit` / `CleanShivDamage` 增加 `shivBase` 参数（4 或 6），刀刃交响曲同样传。
> ⚠ 别的生成刀牌（刀刃之舞 / 斗篷与匕首 / 先制打击 / 刀扇）升级改的是张数或别的数值，小刀仍是 4 ⇒ 只能按牌判断。
>
> 版本 **v0.2.375**；dll MD5 `1811F454FC`。已装游戏 + 工坊两处。

> ### 追加：v0.2.372 —— 玩家回合里刀刃之舞也变 4×3（用户 2026-11-04，第二个录屏）
>
> 用户录了第 1、2 回合的意图：**玩家回合里**刀刃之舞也变成 4×3。日志同一秒里在 4 / 6 之间来回跳：
> ```
> [16:34:04] 小刀预览: 牌面 4 → 4（目标易伤=0，目标=静默猎手）
> [16:34:04] 小刀预览: 牌面 4 → 6（目标易伤=1，目标=静默猎手）
> ```
> **两条都写「目标=静默猎手」—— 因为她自己也是静默猎手**。也就是说有些调用把**她自己**当成了受害者。
>
> **元凶是我 v0.2.370 加的那句 `?? target`**：刀刃之舞是**无目标技能牌**，
> `BossPlanner.ChooseTarget` 给它的 `target` 就是**她自己**。于是
> 「节点没给目标 → 退回 target → 受害者=她自己 → 没有易伤 → 4」。
> v0.2.369/370 两版都在**兜底链**上打转，一路把她自己兜进来了。
>
> **改法**：新增 `ResolvePreviewVictim(boss, targets, card)`，三级兜底，**每一步都校验
> `creature.Monster == null`**（真人玩家没有 Monster；我们的 Boss 挂着影子 Player、有 Monster）：
>   ① 意图节点给的目标列表里的真人玩家；
>   ② 本机玩家（`LocalContext.GetMe`）；
>   ③ 她跟踪锁定的那位。
> **彻底不再使用规划期的 `target`**（无目标牌上它是她自己，有目标牌上它可能是旧战斗的实例）。
> 6 处小刀 lambda（单段/多段/幻影之刃两段/刀刃交响曲两段）全部改走它。
>
> 版本 **v0.2.372**；dll MD5 `4E77EADD8F`；PCK 已重打。已装游戏 + 工坊。
>
> ### 追加：v0.2.371 —— FirstPlayerTarget 把影子玩家当成「玩家」
>
> **录屏取证**（`屏幕录制 2026-11-04 162341.mp4`，7 秒，流程见 `docs/plans/how-to-read-video.md`）：
> 她回合那一刻的画面上 —— **冲刺 = 19**（13×1.5，易伤算进去了）、**刀刃之舞 = 4×3**（没算易伤），
> 同一帧并存 ⇒ 不是「玩家身上没易伤」，是小刀那条意图的**受害者取错人**。
>
> **真凶（致命细节）**：`BossIntentBuilder.FirstPlayerTarget` 只判了
> `if (creature.Player == null) continue;   // 只认玩家侧`
> —— 而我们这只 Boss 的生物挂在**影子 Player** 身上，`creature.Player` **也非 null**
> ⇒ 她回合里意图节点给的 targets 一旦包含她自己，我们就把**她自己**当成「玩家」当成了受害者
> ⇒ 她身上没有易伤/虚弱 ⇒ 数字偏低。玩家回合里 targets 只有真人玩家，所以那时是 6×3（对）。
>
> **改法**：照抄原版 `LiveSingleAttackIntent` 的判据 ——
> `if (creature.Monster != null) continue;`（真人玩家没有 Monster，我们的 Boss 有）。
> 这条同时修好所有用 `FirstPlayerTarget` 的路径（小刀系、X 费多段意图…）。
> 前两版（v0.2.369 把 null 交给 `FinalDamageFor` 兜底 / v0.2.370 再退回规划期目标）
> 都只补了**兜底路径**，没碰这条判据 ⇒ 所以「还是没修好」。
>
> 版本 **v0.2.371**；dll MD5 `E819F7E115`；PCK 已重打。已装游戏 + 工坊。
> ⚠ 方法论：这类视觉差异**必须抽帧看** —— `ffmpeg` 在
> `C:\Users\<user>\AppData\Local\Programs\xiezuo\resources\ffmpeg\ffmpeg.exe`（只能输出 jpg）。
>
> ### 追加：v0.2.369 —— 「计划妥当」描述补上 power 那份；小刀目标回退修正

> ### 追加：v0.2.369 —— 两条（用户 2026-11-04）
>
> **① 「计划妥当」的描述改了两处，上一版只改了一处**
> 这份文案在本地化里**有两条独立的 key**：
>   · `monsters.json` → `SILENT_BOSS.well_laid` —— **特殊效果说明框**用它（v0.2.363 已改）；
>   · `powers.json` → `BOSS_WELL_LAID_PLANS_POWER.description` / `.smartDescription`
>     —— **power 图标的悬浮说明**用它（**上一版漏了**，用户看到的就是这份旧的）。
> 现在 16 个语言的两处都改成「她的谋杀只会在其保留在手牌中时才会因抽牌增加伤害。」
> `check-localization.ps1` 全绿；`content/` 变了 ⇒ **PCK 重打**（80 content + 48 extra，13,251,720 B）。
>
> **② 刀刃之舞在玩家回合 6×3、一进她回合变 4×3**
> 日志实证（同 1 秒内两条日志打架）：
> ```
> 意图伤害管道 DASH: 牌面 13 → 19（…目标易伤=1…）     ← 普通攻击：渲染时按「节点给的目标」算，易伤在
> 小刀预览: 牌面 4 → 4（Shiv卡实例=有，目标易伤=0）   ← 小刀：拿不到目标就回退成「她自己」⇒ 她没易伤 ⇒ 4
> ```
> **根因**：我 v0.2.366 写的 lambda 里是
> `var victim = FirstPlayerTarget(targets) ?? card.Owner?.Creature;`
> —— 她自己的回合里意图节点给的目标列表可能为空，于是 victim 变成**她本人**（身上没有易伤）⇒ 4；
> 而 `LiveSingleAttackIntent` 那条是 `target ?? LocalContext.GetMe(...) ?? dealer`
> ⇒ 兜底成**本机玩家**（有易伤）⇒ 19 / 6。
> **改法**：6 处 lambda（小刀单段/多段、幻影之刃两段、刀刃交响曲两段）全部改成
> `Creature? victim = FirstPlayerTarget(targets);`（**可能为 null**），由
> `BossDamagePredictor.FinalDamageFor` 自己兜底成本机玩家 —— 与普通攻击牌同一条规则。
> 这也解释了「小刀出来又是 6」：桌上那些真小刀走 `LiveSingleAttackIntent`，一直是对的。
>
> 版本 **v0.2.369**；dll MD5 `1436A4A117`；PCK 13,251,720 B。已装游戏 + 工坊（dll/json/pck 三件）。
>
> ### 追加：v0.2.368 —— 养手骨多吃了根除骨的布质果实（用户 2026-11-04）
> 用户：「养手骨的最大生命值不对，你是不是也给她加根除骨的布制果实了？」—— **对，确实加了**。
>
> `BossDefinition.HpForAct` 的规则是「角色起始血量 + (Hp[幕] − Hp[0])」，
> 而亡灵契约师的 `Hp = { 66, 66, **97**, 66 }` —— 第三幕那个 97 是**根除骨专属**的
> `66 + 31（布质果实）`（v0.2.286 为把果实算进去才这么写的）。
> **变体共用同一行血量** ⇒ 养手骨白吃了那 31 点：66 + 31 + 480（涅奥 120/180×） = **577**，
> 正确应该是 **546**。
>
> **改法**：`HpForAct` 里加一条 —— **变体接管这一幕时不继承「逐幕加成」**（按 0 算）；
> 变体真需要额外血量时由它自己的 `HpBonus` 显式给（事件机因为「大蘑菇」写的就是 +20）。
> 影响面核对过：
>   · 根除骨 66+31+480 = **577 不变**；事件机 75+0+480+20 = **575 不变**；
>   · 其它 Boss 各幕的 Hp 都等于基础血量（加成为 0）⇒ 全都不受影响，只有养手骨变成 546。
>
> 版本 **v0.2.368**；`content/` 未变 ⇒ PCK 未重打。dll MD5 `0D089E4E5C`。已装游戏 + 工坊。
>
> ### 追加：v0.2.367 —— 四条平衡/配置（用户 2026-11-04）
>
> **① 蛇眼猎（`SILENT_SNECKO`）进阶**
> `BossDefs` 的变体加了 `AscensionUpgrades`：
> 8 → 究极防御（`UltimateDefend`）、闪躲翻滚（`DodgeAndRoll`）；9 → 匕首雨（`DaggerSpray`）、冲刺（`Dash`）。
> ⚠ 变体的 `AscensionUpgrades` **按类型**生效（`ShadowPlayerFactory.IsUpgradedForAscension` 走
> `VariantAscensionUpgrades`），且**取代**主定义在那一幕的那一行 ⇒ 剧本里同名牌一起升级。
>
> **② 事件机遗物栏 +2 件**
> `EventMachineScript.Relics()` 在「南瓜蜡烛」和「旺购客户感恩徽章」之间插入
> **镀金缆线（`GoldPlatedCables`）** 与 **锚（`Anchor`）**。
>   · ⚠ 本作「**先加的显示在最右**」⇒ 想在状态栏上落在两者之间，就插在这两行之间；
>   · 「锚」的战斗开始 10 格挡走 `BossHookBridge` 那段**通用**的 `BeforeSideTurnStart` 补触发
>     （该段注释里点名的「锚给格挡」就是它），不用另写代码；
>   · 「镀金缆线」的 `ModifyOrbPassiveTriggerCounts` 在她身上本来就生效（v0.2.356 修好钩子链 NRE 后，
>     原版广播真的打得到影子玩家），球被动预告/待激发高亮早就按它算（`BossHandPanel.Extra.OrbPassiveDamage`）。
>
> **③ 事件机进阶**
> 8 → 升级**所有防御**（`DefendDefect`，按类型 ⇒ 剧本第 5/9 回合那些防御全变「防御+」）；
> 9 → 升级疯狂进食（`FeedingFrenzy`）。
>
> **④ 养手骨削弱：钙化 → 来生**
> `OstyKeeperScript` 第 5 回合从「钙化(1) + 来生(1) + 死亡之舞(1)」改成
> **「来生(1) + 来生(1) + 死亡之舞(1)」**，钙化从卡组里整张移除（卡组现在有两张来生）。
> 进阶 8 的 `(8, typeof(Afterlife))` 不变 —— 按类型升级 ⇒ **两张来生一起升级**。
>
> 版本 **v0.2.367**；`content/` 未变 ⇒ PCK 未重打。dll MD5 `D21D12F0F9`。已装游戏 + 工坊。
>
> ### 追加：v0.2.366 —— 小刀 4×3 的真正根因：不是算错，是**算早了**
> v0.2.365 的诊断日志直接给出答案：`小刀预览: 牌面 4 → 4（Shiv卡实例=有，目标易伤=0）`
> —— 计算那一刻玩家身上还没有易伤。而「冲刺」走 `LiveSingleAttackIntent`（**每帧渲染时重算**），
> 所以后来上的易伤立刻生效（10→15）；小刀走 `AttackIntent.Raw`，数值在**规划那一刻就冻住**了。
> **改法**：小刀改成 `LiveMultiAttackIntent`；新增 `LiveDelegateSingleAttackIntent`
> （段数 = 1 时必须画成单段，否则「隐秘匕首」2 把刀会显示成 16、7×1）；
> 幻影之刃的「第一把 +N / 其余扣回」两段、以及**刀刃交响曲**都改成动态版。
> **通用教训**：凡是「生成物 / 无来源伤害」的意图，数值若在规划期算死，玩家事后上的易伤/虚弱就不会反映
> —— 要么走 `Live*`，要么就该吃这个 bug。（`Unpowered` 那几类——群蛇形态/卡戎之灰/黑洞/速行者/
> 冰雹风暴/炸弹/球被动——本来就不吃力量易伤，显示原样数字是对的。）
>
> ### 追加：v0.2.365 —— 小刀（刀刃之舞等）的意图不吃易伤（用户 2026-11-04）
> 用户：「因为此时玩家有易伤，所以刀刃之舞应该显示 6×3 而非 4×3。其它刀牌有没有类似的问题？」
>
> **根因**：`生成刀的牌`（刀刃之舞 / 斗篷与匕首 / 隐秘匕首 / 先制打击 / 刀扇 / 刀刃交响曲）的意图
> 走的是 **Raw**（`RawMultiAttackIntent` / `AttackIntent.Raw`）—— Raw 不过意图节点自己的
> `Hook.ModifyDamage`，所以力量/虚弱/**易伤**都必须由 `ShivDamagePerHit` 自己算全。
> 而它原来的写法是：
> ```csharp
> var shiv = ShivCardFor(boss);          // combat.CreateCard<Shiv>(shadow) 造一张临时小刀
> if (shiv == null) return baseDamage;   // ← 造不出来就**直接返回牌面 4**（易伤、力量、虚弱全丢）
> return BossDamagePredictor.FinalDamage(shiv, baseDamage, target);
> ```
> 造得出卡实例时是对的（`FinalDamage` 里 dealer = `shiv.Owner.Creature` = 她 ⇒ 易伤照算），
> 但一旦造不出来（`CreateCard<Shiv>` 失败/缓存失效）就静默退化 ⇒ 有易伤也只显示 4×3。
>
> **改法**：
>   · `BossDamagePredictor` 新增 `FinalDamageFor(dealer, cardSource 可为 null, base, target)`，
>     `FinalDamage(card, …)` 改成它的薄包装（行为不变）；
>   · `ShivDamagePerHit` 改成 `FinalDamageFor(boss.Creature, shiv /*可能为 null*/, base, target)` ——
>     **dealer 显式给**，所以即使没有卡实例，目标易伤 / 她的力量 / 虚弱照样算进去；
>   · 加了诊断日志（最多 12 行）：`小刀预览: 牌面 4 → 6（Shiv卡实例=无，目标易伤=1，目标=…）`
>     —— 下次一眼就能看出是「卡实例没造出来」还是「易伤没生效」。
>
> **其它刀牌**：全部走同一个 `ShivDamagePerHit`（`IntentsFor` 里 `shivs > 0` 那一段），所以**一次全修**；
> 幻影之刃那两段（「第一把 +9」「其余」）也用它，一并覆盖。
> ⚠ 两处**本来就对、不用改**：
>   · **桌上那些真小刀**（`SilentBlades.FrontloadRealShivs` 造出来的 `PlannedPlay`）走的是
>     `LiveSingleAttackIntent` → 渲染期 `FinalDamage` ⇒ 易伤一直是算的；
>   · 匕首雨（`DAGGER_SPRAY`）不是生成刀、是普通多段攻击，同样走 Live 路径。
>
> 版本 **v0.2.365**；`content/` 未变 ⇒ PCK 未重打。dll MD5 `3FEAD04D65`。已装游戏 + 工坊。
>
> ### 追加：v0.2.364 —— 蛇眼猎第 2 回合换牌（用户 2026-11-04）
> 用户：「把突然一拳和刀刃之舞互换（改成出刀刃之舞不出突然一拳）」。
> `Core/SnakeEyesSilentScript.cs` 第 2 回合：
> ```
> 原来：冲刺(Dash,2) + 突然一拳(SuckerPunch,1) = 3     ｜ 刀刃之舞(1) 留手
> 现在：冲刺(Dash,2) + 刀刃之舞(BladeDance,1) = 3      ｜ 突然一拳(1) 留手
> ```
> 能量还是 3，费用账不变（2 + 1）。
> ⚠ **实际输出会明显变高**：刀刃之舞会印 3 把 0 费小刀，而她在同一回合就会把小刀
> Frontload 打出去（`SilentBlades.FrontloadRealShivs`）⇒ 第 2 回合大概多 3×4 ≈ 12 点伤害
> （还会吃她的力量与「一心化万」这类按牌判定的增伤）。意图下面会自动多出一条「小刀 4×3」。
> 换句话说这不是等价互换，是**加强**；如果第 2 回合太痛了，把刀刃之舞挪到别的回合或降级即可。
> （顺带：刀刃之舞原来整份剧本里**从没被打出去过**，一直在手里躺着。）
>
> 版本 **v0.2.364**；`content/` 未变 ⇒ **PCK 未重打**。dll MD5 `088CF0F6A2`。已装游戏 + 工坊。
>
> ### 追加：v0.2.363 —— 谋杀伤害偏高 + 计划妥当说明（用户 2026-11-04）
>
> **① 谋杀（Murder）伤害偏高**
> 旧写法（v0.2.346）在 `BossScriptRunner.BuildHand` 里：
> `<c>total = CardsDrawnThisCombat + 本回合手牌数; if (起点 &lt; 0) 起点 = total; since = total - 起点;</c>`
> 然后**无条件** `CardsDrawnThisCombat = total`。问题：
>   · 累加是**无条件**的 ⇒ 「谋杀已经被打出去、她手里没有它的那几个回合」抽的牌**也被算进加伤**；
>   · 而加伤进度是整场不重置的 ⇒ 越到后面越偏高（用户：「好像还是不是只在手牌中被保留时抽牌才增伤，偏高了」）。
> **改法**：新增两个字段 `MurderSeenInHand` / `MurderDrawProgress`：
>   · 它**第一次**进她手牌那一手：进度归零（那一手不算）；
>   · 之后**只有当这一手她手里确实还有谋杀**时，才 `MurderDrawProgress += 本回合手牌数`；
>   · **不在手的那几回合一律不计**（日志：`谋杀：这一手不在她手里 ⇒ 本回合的手牌不计入加伤进度`）。
> 伤害仍写入卡实例的 `CalculationBase`（`= CalculationBase + ExtraDamage × 进度`），打出后再进手接着之前的进度走（只增不减）。
> ⚠ `MurderDrawBaseline` 字段保留但已不再参与计算（老存档无所谓，它是运行时状态）。
>
> **② 「计划妥当」特殊效果说明**（`SILENT_BOSS.well_laid`，`content/localization/*/monsters.json`）
> 用户：第一段不变，第二段改成「她的谋杀只会在其保留在手牌中时才会因抽牌增加伤害。」
> **16 个语言全部同步改**（保留各语言自己的 `[gold]谋杀[/gold]` 译名）。
>   · 因为 `content/` 变了 ⇒ **PCK 已重打**：`Packed 80 content file(s)` + `48 extra file(s)`，
>     `.build\CharacterBosses.pck` = 13,251,912 B；
>   · `tools\check-localization.ps1` 全部通过（16 个语言目录）。
>   · ⚠ 踩坑记录：改这批文件的脚本**不能用 .ps1 文件**（本机 PS 5.1 把无 BOM 的 .ps1 当 ANSI 读 ⇒ 中文全乱）。
>     这次的做法：新文案放进 `tools\welllaid-new-text.json`（显式按 UTF-8 读），
>     补丁命令**全 ASCII** + `[System.IO.File]::ReadAllLines/WriteAllLines(path, …, UTF8Encoding($false))`。
>     `tools\update-welllaid-loc.ps1` 是那次失败的尝试，**留在仓库里当反面教材**（别直接跑）。
>
> 版本 **v0.2.363**；dll MD5 `FD2187FB4A`；PCK 13,251,912 B。已装游戏 + 工坊（dll/json/pck 三件都换了）。
>
> ### 追加：v0.2.362 —— 出击（奥斯提牌）的伤害预览（用户 2026-11-04）
>
> **日志实证**（v0.2.361，15:06–15:07 那场养手骨）：
> ```
> 奥斯提伤害预览 UNLEASH: base=6 extra=1 ostyHp=26 calcify=0 → 32
> 意图数值 UNLEASH: 显示 40（预补 8，段数 1，奥斯提生命=26）      ← 32 + 6(奥斯提待加) + 2(她的力量)
> 成长修正…: UNLEASH(补=0+力2)                                  ← 她的力量被算进去了
> ```
>
> **① 「多了 2」= 她自己的力量被加到了奥斯提的牌上**
> 反编译：出击的伤害是 `CalculationBase + ExtraDamage × 奥斯提当前生命`（`OstyScaledDamage`），
> **不吃**她的 `StrengthPower`。但 `IntentsFor` 里那句 `damage += extraDamage` 是**所有分支共用**的，
> 而调用方把「本回合待到手的力量」混在 `extraDamage` 里一起传 ⇒ 出击莫名 +2。
> **改法**：`IntentsFor` 新增 `strengthBonus` 参数，**只在 `!fromOsty` 时加**；
> 三个调用方（`BossScriptPlanner` 主路径、`BossScriptPlanner.PreviewIntents`、`BladesGrowth`）
> 把力量那一份从 `extraDamage` 里拆出来走 `strengthBonus`。
> 出击的「奥斯提待加生命」（`XtraOstyHpBonus × plannedOstyHp`）仍留在 `extraDamage` —— 那笔是对的。
>
> **② 「没算易伤」= 奥斯提的牌走 Raw 意图，整条伤害管道不跑**
> 这批牌用 `<c>AttackIntent.Raw</c>`（故意的：不吃她的力量），代价是**目标身上的易伤 ×1.5 也没算**
> —— 以前这里只跑 `CapDamage`（`ModifyDamageHookType.Cap`，只为「无实体 = 1」）
> ⇒ 玩家挂着易伤时出击的真实伤害远高于预览（用户说的「大幅低估」）。
> **改法**：新增 `BossDamagePredictor.OstyFinalDamage` —— 整条 `Hook.ModifyDamage` 跑一遍，但
>   · **dealer 换成奥斯提本人**（`card.Owner.Osty`）⇒ 目标侧的易伤/摧残/无实体上限照常生效，
>     她自己的力量/虚弱/致死性/一心化万（按牌判定）都不会误加进来；
>   · `cardSource` 传 **null** ⇒ 致死性不会在这里被乘第二次（它由 `BossIntentBuilder` 按
>     「本回合第一张攻击牌」手工乘一次，v0.2.360 已修，日志实证 `HIGH_FIVE 11 → 19（+75%）`）。
>   日志关键字：`奥斯提牌伤害管道 HIGH_FIVE: 牌面 11 → 16（目标易伤=1…）`。
>
> 版本 **v0.2.362**；`content/` 未变 ⇒ PCK 未重打。dll MD5 `7A310BECE6`。已装游戏 + 工坊。
>
> ### 追加：v0.2.361 —— 无界命匣每回合召唤 2 **两次**（用户 2026-11-04）
>
> 现象：养手骨每回合召唤 2 变成了两次（= 4 血）。
>
> **根因（与「王室猛毒掉两次」完全同源）**：影子玩家**收得到**原版的
> `Hook.AfterSideTurnStart` 广播 —— 只是 v0.2.351～355 期间那条链在情感芯片那里抛 NRE、
> 整条链在到她之前就断了，所以看起来「收不到」。v0.2.356 修掉 NRE 之后，原版
> `PhylacteryUnbound.AfterSideTurnStart`（→ `OstyCmd.Summon(StartOfTurn = 2)`）真的开始执行了，
> 而我们自己 `BossOsty.SummonAndRegister` 每回合也召唤 `StartOfTurn` ⇒ **2 + 2 = 4**。
> （`_lastSummonTurn` 已经把**我们自己**那次限制成每回合一次，所以多出来的必然是原版那条。）
>
> **修法**：新增 `Patches/PhylacteryNoDoubleSummonPatch` —— 遗物主人是我们的 Boss 时，
> 对 `PhylacteryUnbound.AfterSideTurnStart` 整条跳过（`ref Task __result = Task.CompletedTask`）。
> 她的召唤从此只有两个来源、各一次：
>   · **战斗开始 5**：`BossOsty.TryCombatStartSummon`（战斗真的开始的第一帧，v0.2.360）；
>   · **每回合 2**：`BossOsty.SummonAndRegister`（`StartOfTurn`）。
> 原版 `BeforeCombatStart`（5）由 `OstyNoCombatStartSummonPatch` 拦着 —— 不会再有第三处。
>
> ⚠ **这条给下一个会话的警告**：v0.2.356 修好钩子链 NRE 之后，**所有**「原版遗物/能力钩子
> 打不到影子玩家、所以我们手动补偿」的写法都可能变成**双份**。目前确认踩过的是
> 王室猛毒（`RoyalPoisonOncePatch`）和无界命匣（`PhylacteryNoDoubleSummonPatch`）；
> 项目里同类「once / NoDouble」补丁还有 `CrackedCoreOncePatch`、`FencingManualOncePatch`、
> `HappyFlowerNoDoublePatch`、`ShadowRedMaskPatch`、`ShadowDiamondDiademOncePatch`、`AkabekoOncePatch` 等 ——
> 下次实机时留意「同一条效果出现两次」的报告，优先怀疑这里。
>
> 版本 **v0.2.361**；`content/` 未变 ⇒ PCK 未重打。dll MD5 `C623633C4E`。已装游戏 + 工坊。
>
> ### 追加：v0.2.360 —— 致死性预补 / 无界命匣 5 / 收藏家藏品
>
> **① 击掌的伤害预览没算「刚打出的致死性」**
> 第 4 回合剧本是 `致死性(1) → 击掌(2) → 增生(0)`，致死性**同一回合先打**。
> 意图是在规划/重算期算的，那时 `LethalityPower` 还没上身，原来只读
> `GetPowerAmount<LethalityPower>()` ⇒ 恒为 0 ⇒ 击掌（走 Raw 意图、不过 `Hook.ModifyDamage`）那 50% 永远不显示。
> 新增 `BossIntentBuilder.LethalityPercentFor(boss, card)`：身上没有这层 power 时，去**本回合计划里**
> 找那张还没打出去的「致死性」，用它的卡面值（`LethalityPower` 变量，升级版自动算）。
> ⚠ 只给 `fromOsty` 那条路用 —— 普通攻击牌的加成由 power 自己在渲染期乘，这里再乘会双份。
>
> **② 无界命匣：战斗开始召唤 5**
> 用户口径：「让她在战斗开始时就召唤 5，然后再每回合召唤 2（把原版第一回合召唤 5 的拦了，别像王室猛毒那样重复）」。
> v0.2.347 因为「战斗开始那一刻 `IsInProgress` 还是 false ⇒ `PlayerCmd.AddPet` 抛错」,
> 把这 5 并到了她第 1 回合（5 + 2 = 7）。现在改成：
>   · `BossOsty.TryCombatStartSummon(boss)` 由 `BossFrameTickPatch` 每帧调，
>     在「战斗真的开始了」(`IsInProgress && !IsStarting`) 的**第一帧**补一次 5，之后 `CombatStartSummonDone` 置位不再给；
>   · 每回合那笔回到**纯** `StartOfTurn` = 2（`SummonAndRegister` 加了 `forcedAmount/forcedSource` 两个可选参数，
>     `FirstTurnExtraSummonOf` 不再被调用）；
>   · 原版 `PhylactereryUnbound.BeforeCombatStart` 那条 5 早已被 `Patches/OstyNoCombatStartSummonPatch` 拦掉，
>     每回合 `AfterSideTurnStart` 那条也在 `BossHookBridge` 的跳过名单里 ⇒ **不会双份**。
>
> **③ 收藏家藏品：蛇眼猎→子弹时间 / 养手骨→死者苏生 / 事件机→万物一心**
> 这三条**在 `DownfallCollectibleCompat.ResolveLoot` 的按遭遇名 switch 里本来就有**，
> 但它们和各自的兄弟设计（涂毒猎 / 根除骨 / 能力机）**落在同一个 (角色, 幕) 格**上 ——
> 一旦 `EncounterOf(SpawnVariantKey)` / `CurrentEncounterEntry()` 取不到（战斗结束时 CombatState 已被换掉），
> 就会掉回静态表 ⇒ 掉错牌。新增**按 `SpawnVariantKey` 的直查兜底**（放在最前面），并打日志：
> `收藏家藏品：蛇眼猎 → 子弹时间` / `养手骨 → 死者苏生` / `事件机 → 万物一心`。
>
> 版本 **v0.2.360**；`content/` 未变 ⇒ PCK 未重打。dll MD5 `3FEE57F0C0`。已装游戏 + 工坊。
> ⚠ 本轮仍未实机验证（沙箱里 steam 起不动游戏，用户要求也不直接跑 exe）。
>
> ### 追加：v0.2.359 —— 全息影像返手 + 一心化万的力量预补（用户 2026-11-04 反馈）
>
> **① 拦掉「全息影像」的返手，但保留「一心化万」**
> 用户原话：「拦掉全息影像的返手，因为我已经算好它要捞的牌了，并且不影响它触发『一心化万』。」
> 反编译 `Hologram.<OnPlay>d__7` 的顺序是
> `① GainBlock(3/5) → ② CardSelectCmd.FromCombatPile(Discard, 1) → ③ CardPileCmd.Add(card, Hand)`，
> 所以**不能整条 `OnPlay` 跳过**（那样连格挡都没了）。新增 `Patches/HologramNoReturnPatch`：
>   · `Hologram.OnPlay` 前缀开一个「只对这只 Boss」的窗口（`SuppressFor`），后置在 Task 完成时关掉；
>   · `CardPileCmd.Add` 的 5 参重载前缀：窗口内 + 牌主是她 + 来源是弃牌堆 ⇒ **跳过搬运**，
>     同时 `PendingReturnStrength++`（一心化万照样算 1 张 ⇒ 所有单位 +1 力量，
>     真正的加力量仍在 `PlayOne` 的同一个 await 链里结算，不另起异步链）。
>   · **只对带 `BossAllForOnePower`（一心化万）的 Boss 生效** —— 三层另一套「能力机」
>     （`DefectScript` 第 2 回合也有全息影像）没有这个特殊效果，原版行为完全不变。
>   · ⚠ `Hologram.OnPlay` 是 `protected`，`nameof` 编译不过 ⇒ 写死 `"OnPlay"` 并**钉死参数类型**
>     （`new[]{ typeof(PlayerChoiceContext), typeof(CardPlay) }`），否则 `CardModel` 上的重载会歧义。
>
> **② 一心化万本回合给的力量还是没被提前计算**
> 根因：`StrengthGainOfPlanned` 只认「万物一心」的 `ReturnsToHand`（2 张），**漏了全息影像**（1 张）。
> 第 7 回合剧本是 `疯狂科学 → 全息影像 → 冰之长枪 → 双重释放` ⇒ 后两张的预览一直少 1 点。
> 现在统一走 `BossDamagePredictor.DiscardReturnStrengthOf`（`ALL_FOR_ONE` → 剧本张数、`HOLOGRAM` → 1），
> 规划期（`BossScriptPlanner`，原来那段硬编码已删）、重算期（`RefreshPlannedIntents`）、
> 撤回期（`PlayOne` 的 `PlannedStrengthGainedThisTurn`）**三处同一把尺子**。
>
> 版本 **v0.2.359**；`content/` 未变 ⇒ PCK 未重打。dll MD5 `CBB69C920B`。已装游戏 + 工坊。
> ⚠ 这一版**没有实机验证过**：本会话里 `steam://run` 与 `steam.exe -applaunch` 都起不动游戏
> （沙箱限制），按用户要求也不再直接跑 exe ⇒ 等用户启动后看日志确认。
>
> ### 追加：v0.2.358 —— 修 v0.2.357 的两个遗留（用户 2026-11-04 反馈）
>
> **① 情感芯片那条额外球被动，算错了集合**
> 原来是把「她**整个回合打完之后**还剩的球」的被动总数**翻倍**（v0.2.348 的近似写法）。
> 但情感芯片触发在**她回合开始**那一刻 —— 那时队列里是**玩家回合结束时**那批球，两者常常不是一个集合
> （第 2 回合她打完只剩冰球，可回合开始时那颗**闪电球**照样触发一次 4 点）。
> ⇒ 用户看到的「没标注开始时闪电球的被动伤害」。
> **改法**：`BossHandPanel.Extra.OrbPassiveDamage` 里新增一段，按**现在队列里的球**（= 回合开始那批）
> 单独累加一次被动伤害加上去，不再拿总数翻倍（集中变化那时还没发生，所以不加 `focusDelta`）。
>
> **② 有球 Boss 的「待到手力量」补偿算错，且实际到账后没撤**
> `CharBossMonster.PlayPlannedCards` 每张牌打出前原来**无条件**调 `BladesGrowth.RecomputeAll`
> —— 那套是为小刀/君王之剑写的手牌路径：它不认识「一心化万返手」的力量，
> 而且会把有球 Boss 的**激发意图**一起冲掉。
> **改法**：有球（`pcs.OrbQueue.Capacity > 0`）改走它自己那套 `RefreshPlannedIntents()`，
> 没球的仍走 `BladesGrowth.RecomputeAll`；同时把 `BladesGrowth` 里的
> `plannedStrength` 也换成 `BossDamagePredictor.StrengthGainOfPlanned`（含一心化万），两条路同一把尺子。
> `PlannedStrengthGainedThisTurn` 的真实到账撤回逻辑不变 ⇒ 疯狂进食 +5、一心化万 +2 到账后各自撤回。
>
> **③ 用户提醒：启动游戏的方式**
> ⚠ **不要直接跑 `SlayTheSpire2.exe`**（会被 Steam 拦下来 / 不是正常启动路径）。
> 用项目给的脚本：`powershell -ExecutionPolicy Bypass -File tools\launch_sts2.ps1`（内部是 `steam://run/2868840`）。
> 日志/状态 API（8081 菜单、15526 战斗）照常可用。
>
> 版本 **v0.2.358**；`content/` 未变 ⇒ PCK 未重打。dll MD5 `96EBF39DCB`。
> ⚠ 装的时候游戏还开着 ⇒ 起了后台任务等进程退出后自动补拷（`job pwsh-2`）。⚠ 工坊仍未上传。
>
> ### v0.2.357 那一批（事件机六项，已实机验证前五项）
> ① 王室猛毒只在战斗开始掉一次（新增 `Patches/RoyalPoisonOncePatch`：对她跳过原版 `AfterPlayerTurnStart`
>  —— 那条**其实会打到她身上**，2026-11-04 修好钩子链 NRE 后才暴露；实测日志：战斗开始 575→571 一次，
>  之后每次只打一行「跳过原版…」）；
> ② 球被动预告算上「吞噬暗影」生成的黑暗球（`BossOrbMath.TryChannels` 补 `CONSUMING_SHADOW`→2 个 `DarkOrb`，
>  数量读 `Repeat` 变量）+ 回合结束被吞噬暗影激发的伤害；黑暗球的 `EvokeVal` **不含集中**，已单独处理；
> ③ 疯狂科学意图从「攻势+守势+牺牲+强化」改成**守势+强化**（`BossIntentBuilder` 按 entry 清零那个假的
>  `Damage` 变量），卡面 `？？？` 换成混乱 rider 文案（`Patches/MadScienceTextPatch`，文案取游戏自带
>  `events` 表 `TINKER_TIME.pages.CHOOSE_RIDER.options.CHAOS.description`，16 语言自动对）；
> ④ 情感芯片原来**一次都没触发过**（判据只有生命快照，且每一步都静默 return）⇒ 改成
> 「生命快照 **或** 战斗历史」并联，并全步骤记日志；实测已触发（第 1 回合靠历史 1 条，第 2 回合快照 6 / 历史 2 条）；
> ⑤ 有球 Boss 的意图重算补上本回合待到手力量（见 v0.2.358 的②，那是这条的完整版）；
> ⑥ **移除遗物「玉之剑」**（用户要求削弱）：`EventMachineScript.Relics()` 删掉 `SwordOfJade`，
>  战斗开始那次 +3 力量随之失效（实测不再打「玉之剑：战斗开始 +3 力量」）。
>
> ### 追加：v0.2.356 —— 「没有结束回合按钮」根因**确认并修复**（实机验证通过）

**真凶：`Patches/EmotionChipBossTakeoverPatch` 跳过 async 钩子时没给 `__result`。**

反编译确认 `EmotionChip.AfterPlayerTurnStart` 是 **async**：
```csharp
[AsyncStateMachine(typeof(<AfterPlayerTurnStart>d__5))]
public override Task AfterPlayerTurnStart(...) { builder.Start(...); return builder.Task; }
```
而我们的 Prefix 只 `return false`、**没有 `ref Task __result`** ⇒ Harmony 跳过方法体 ⇒ 返回
`default(Task)` = **null** ⇒ `Hook.AfterPlayerTurnStart` 状态机里
`model.AfterPlayerTurnStart(...).GetAwaiter()` 直接 **NullReferenceException**。

**因果链全对上**：
1. 那条链不兜异常 ⇒ 整条「玩家回合开始」中断 ⇒ `NEndTurnButton.OnTurnStarted` 轮不到 ⇒ **按钮一直 Hidden**；
2. 回合结束管线从不跑（`原版回合结束步骤：` 一条都没有）；
3. 她一手都不出（`出牌阶段开始` / `PLAY` 一条都没有）。
4. 日志顺序也精确吻合：`玩家回合开始钩子：开始` → `选择悖论：对她跳过`（那个 Prefix **有** `ref Task __result`，
   所以没炸）→ 迭代到的**下一个**监听者正是情感芯片 ⇒ 立刻 `异常:Object reference not set…（耗时 2ms）`。
5. 只有事件机有这问题（它是**唯一**带「情感芯片」的 Boss），与交接报告 §3.6-5 的观察一致。

**v0.2.355 的 finalizer 为什么完全没用（已实证，别再试这条）**：
async 方法的**整个方法体都在状态机里**，异常由状态机自己
`catch (Exception e) { builder.SetException(e); }` **塞进 Task**，**永远不会穿过方法边界**。
所以 `[HarmonyFinalizer]` 挂在返回 Task 的那个壳上时 `__exception` 永远是 null（跟
`CombatHasOurBoss` 判断**无关** —— 交接报告把原因猜错了）；挂在状态机 `MoveNext` 上同样没用（异常不逃出 MoveNext）。
**唯一能拦的位置是「拿到 Task 之后」** ⇒ 改成 Postfix。

**这一版改了三处**：
1. `EmotionChipBossTakeoverPatch`：加 `ref Task __result` 并赋 `Task.CompletedTask`（**真凶，就是这一行**）；
2. `ConfusedPowerBossPatch`（`ConfusedPower.AfterCardDrawn`，返回 `Task`）**同一类 bug**，已同样修掉 ——
   这是**蛇眼猎**身上的定时炸弹：异蛇之眼一给她上「混乱」，她每抽一张牌就炸一次抽牌管线。
   顺带全仓扫了一遍所有「bool Prefix 但没有 `__result`」的补丁，**只有这两处**是返回 Task 的；
3. `PlayerTurnStartHookSafetyPatch` 的 finalizer 换成 **Postfix**（`ref Task __result`：null 或同步 faulted ⇒
   换 `Task.CompletedTask` + 打**完整堆栈**），`CombatHasOurBoss` 改成扫**全部**生物列表
   （`Creatures`/`Allies`/`Enemies`/`PlayerCreatures`/`HittableEnemies`/`CreaturesOnCurrentSide`）。
   `EndTurnButtonTracePatch.HookWatch` 也改成打完整异常（原来只打 Message，永远看不出是谁）。

**实机验证日志（13:35:18 起，v0.2.356，`defect@3` 调试开关 ⇒ 事件机，三回合）**：
```
13:35:20 玩家回合开始钩子：完成（耗时 2ms）        ← 不再抛异常
13:35:20 结束回合按钮状态：Hidden → Enabled        ← 按钮回来了
13:36:23 敌方行动入口：PlayPlannedCards（第 2 回合，Plan=5 张）
13:36:23 出牌阶段开始 … PLAY DISTRACTION / WHITE_NOISE / CONSUMING_SHADOW / ICE_LANCE / DUALCAST
13:36:42 原版回合结束步骤：AfterAllPlayersReadyToEndTurn = 1ms
13:36:43 步骤：情感芯片完成                        ← 她自己的情感芯片实现照常在跑
13:36:43 出牌阶段开始 … PLAY MAD_SCIENCE / BOOT_SEQUENCE / HYPERBEAM
13:36:49 玩家回合开始钩子：完成（耗时 0ms） → 按钮 Hidden → Enabled
```
⚠ 保险那两条（`⚠ 玩家回合开始钩子链…`）**一次都没触发** ⇒ 真修好了，不是靠兜底。

版本 **v0.2.356**；`content/` 未变 ⇒ PCK 未重打。dll MD5 `465CDCDF4D`。已装游戏 + 工坊。⚠ 工坊仍未上传。


### 追加：v0.2.355 —— 「没有结束回合按钮」的中间结论（**因果链对，真凶猜错**，见上面的 v0.2.356）

> ↓ 这一节的「因果链」三条**完全正确**，但把 finalizer 没触发归因于 `CombatHasOurBoss` 判断失败是**错的**：
> 真正原因是 async 方法的异常根本不会穿出方法边界，finalizer 永远看不到（已实证）。


**追踪日志（13:03:41，v0.2.354）**：
```
玩家回合开始钩子：开始（真人玩家）
选择悖论：对她跳过（…）
玩家回合开始钩子：异常:Object reference not set to an instance of an object.（耗时 2ms）
```
⇒ `Hook.AfterPlayerTurnStart` 的监听者链里**某个钩子抛了 NullReferenceException**，而原版这条链没有兜异常。

**因果链（与所有现象吻合）**：
1. 玩家回合开始的流程在**抛异常处中断** ⇒ `NEndTurnButton.OnTurnStarted` 永远轮不到执行
   ⇒ `_state` 保持 `Hidden`（探针看到的 `btnState=Hidden`，而前置条件全满足）；
2. `CombatManager` 的「玩家回合开始」步骤没走完 ⇒ 结束回合管线从不跑（日志里 `原版回合结束步骤：` 一条都没有）；
3. 她那一手也永远不会被派下来（`敌方行动入口：PlayPlannedCards` 从不出现）。

**修法**：新增 `Patches/PlayerTurnStartHookSafetyPatch`（`Hook.AfterPlayerTurnStart` 的 Harmony **finalizer**）：
- 只有当这一场里有我们的 Boss 时才吞掉异常，并**打印完整堆栈**（下次就能看到是哪件遗物/哪个钩子）；
- 普通战斗照原样把异常抛出去（不影响原版行为诊断）。
v0.2.354 的「按钮被藏 2 秒就强制打开」保险保留（双保险）。

版本 **v0.2.355**；`content/` 未变 ⇒ PCK 未重打。dll MD5 `8A78BF6F76`。已装游戏 + 工坊（13:0x）。
⚠ 工坊仍未上传。


### 追加：v0.2.354 —— 结束回合按钮：条件全满足但状态是 Hidden

**探针实证（12:58 那场，v0.2.353）**：
```
inProgress=True side=Player me=ok meAlive=True extraTurns=0(含我=False)
roomMode=ActiveCombat isCurrentScreen=True handMode=Play inCardPlay=False inSelection=False
btnState=Hidden btnVisible=True
```
⇒ 反编译 `NEndTurnButton` 的所有前置条件**全部满足**，可 `_state` 是 **Hidden**
（`RefreshEnabled()` 只会在 `Enabled` 上做减法，不会把 Hidden 变回 Enabled）
⇒ 按钮是被某个 `SetState(Hidden)` 之后**再也没被打开**。而把按钮亮出来的地方是
`NEndTurnButton.OnTurnStarted(state)`（玩家回合开始）—— 与「她这一手从不开始」「回合结束管线从不跑」完全吻合。

**这一版（`Patches/BossFrameTickPatch.ProbeEndTurnButton` + `Patches/EndTurnButtonTracePatch`）**：
1. **保险**：1Hz 探针里若前置条件全满足、但 `btnState == Hidden` 连续 2 次（约 2 秒），
   就反射调它自己的 `SetState(State.Enabled)` 把按钮打开（还需 `AllPlayersReadyToEndTurn() == false`）。
2. **追踪**：`NEndTurnButton.SetState` 每次变化都记一行（带调用者），
   `Hook.AfterPlayerTurnStart` 记开始/完成/耗时 ⇒ 能看出是**谁**藏的按钮、
   以及玩家回合开始那条广播链是否卡在某个遗物钩子上。

版本 **v0.2.354**；`content/` 未变 ⇒ PCK 未重打。dll MD5 `AE72F64753`。
⚠ 装的时候游戏还开着 ⇒ 游戏目录那份 dll 被占用，只有工坊那份换成新版；
已起后台任务等游戏退出后自动补上（`job pwsh-1`）。⚠ 工坊仍未上传。


### 追加：v0.2.353 —— 「没有结束回合按钮」：结论收窄 + 探针

**12:52（v0.2.352）那次日志的关键事实**：
- **`敌方行动入口：PlayPlannedCards` 一次都没出现** ⇒ 游戏根本没把她这一手派下来；
- **`原版回合结束步骤：…` 一条都没有** ⇒ 玩家这边的「结束回合」管线也没跑。
⇒ 与「她的回合卡住」不同：是**结束回合按钮本身没出现 / 点了没进管线**。

**反编译 `NEndTurnButton`（`references/sts2.dll`）得到的前置条件**：
```csharp
// 显示/Enable：state.CurrentSide == Player && CombatManager.Instance.IsInProgress && PlayerCanTakeAction(me)
private bool PlayerCanTakeAction(Player p)
    => p.Creature.IsAlive && (PlayersTakingExtraTurn.Count == 0 || PlayersTakingExtraTurn.Contains(p));
// RefreshEnabled() 里还会藏掉按钮：
//   NCombatRoom.Instance == null || Mode != CombatRoomMode.ActiveCombat
//   || !ActiveScreenContext.Instance.IsCurrent(NCombatRoom.Instance)
//   || NCombatRoom.Instance.Ui.Hand.IsInCardSelection
```

**这一版**：`BossFrameTickPatch.ProbeEndTurnButton`（1Hz，**状态变化才打**）把这些值全打进日志：
`inProgress / side / me / meAlive / extraTurns(含我) / roomMode / isCurrentScreen / handMode / inCardPlay / inSelection / btnState / btnVisible`。

**下一步**：等用户复现一次 → 直接读 `mod_log.txt` 里的 `结束回合按钮探针：…` 行，
哪一项不满足就是根因（例如 `extraTurns=1(含我=False)` ⇒ 她被塞进了 `PlayersTakingExtraTurn`；
`roomMode=VisualOnly` ⇒ 房间模式被改动；`inSelection=True` ⇒ 有选牌界面没关）。

版本 **v0.2.353**；`content/` 未变 ⇒ PCK 未重打。dll MD5 `0D0AC55E70`。已装游戏 + 工坊。
⚠ 工坊仍未上传。


### 追加：v0.2.352 —— 养手骨进阶 9 去掉「钙化」

用户 2026-11-03：「养手骨进阶 9 不再升级钙化」。
`BossDefs.Necrobinder.VariantsByAct[2]`（`NECROBINDER_OSTY`）的 `AscensionUpgrades` 删掉 `(9, Calcify)`，
现在进阶 9 只升级 **致死性**（进阶 8 不变：死亡之舞 + 来生 + 遗物骇人头盔）。
⚠ 主定义里那条 `(9, 3, typeof(Calcify))` 是给**根除骨**（基础三层设计）的，没有动。

版本 **v0.2.352**；`content/` 未变 ⇒ PCK 未重打。dll MD5 `C480E86463`。已装游戏 + 工坊。
⚠ 工坊仍未上传。


### 追加：v0.2.351 —— 事件机「回合结束」两次排查的结论与下一步

**已排除**（读 `mod_log.txt` 22:55 / 23:05 两次事件机战斗实证）：
1. **不是选择悖论**：v0.2.348 已经对它整条跳过（日志 23:05:44 有 `选择悖论：对她跳过`），照样卡。
2. **不是她出牌流程里某张牌**：整场**没有任何** `出牌阶段开始`、也没有任何 `PLAY …`。
3. **关键新证据**：`VanillaTurnEndTimingPatch` 的
   `原版回合结束步骤：WaitUntilQueueIsEmpty / DoTurnEnd / AfterAllPlayersReadyToEndTurn /
   AwaitTurnEndAndSwitchSides` **一条都没有**（同一份日志里 23:01 的养手骨战斗这些行都正常出现）
   ⇒ **「结束回合」这个动作根本没被游戏接收/派发**（按钮点不到或点了没进管线），
   而不是她的回合流程卡在半路。
   ⚠ 但 `PlayPlannedCards` 的入口日志是这一版才加的，所以「游戏到底有没有派她的行动」下一版日志才分得清。

**这一版做的事（`CharBossMonster.PlayPlannedCards`）**：
- 入口第一行就写日志 `敌方行动入口：PlayPlannedCards（第 N 回合，Plan=… 张）`；
- 每个阶段各写一行：`步骤：回合开始钩子完成` / `步骤：情感芯片完成` / `步骤：奥斯提收尾完成` / `出牌阶段开始`；
- `BossHookBridge.FireTurnStart` 外面套了 **3 秒上限**：超时就不再等它、继续这一回合
  （日志 `⚠ 回合开始钩子超过 3000ms 没返回`）。

**下一步**：等用户复现一次后**直接读** `%APPDATA%\SlayTheSpire2\logs\mod_log.txt`（本会话有读权限）：
- 若出现 `敌方行动入口` 但后面某一行缺失 ⇒ 卡在该阶段（已有 3 秒保险会跳过）；
- 若连 `敌方行动入口` 都没有、且仍然没有 `原版回合结束步骤` ⇒ 问题在**游戏侧派发前**
  （End Turn 按钮/回合状态），要往 `NCombatUi` / `CombatManager` 那一侧查。

版本 **v0.2.351**；`content/` 未变 ⇒ PCK 未重打。dll MD5 `08374B1813`。
已装游戏 + 工坊（23:1x）。⚠ 工坊仍未上传。


### 追加：v0.2.350 —— 玩家反馈：幻刃的增伤错误显示到后续小刀

**玩家原话（Steam 评论，2026-11-03）**：「刀猎存在显示问题，幻刃的增伤会错误的显示到后续小刀上导致预计伤害存在误差」。

**根因**（反编译 `PhantomBladesPower.ModifyDamageAdditive`）：
它靠「本回合**已经打完**几把刀」判断是不是第一把（`History.CardPlaysFinished` 里带 Shiv 标签的条数 > 0 ⇒ 不加）。
而意图是在她出手**之前**算的 ⇒ 历史里 0 条 ⇒ 它给**每一把刀**都 +9（升级 12）。
另外我们自己在 `BladesGrowth` 里也会给「计划里第一把刀」手工 +9 ⇒ 第一把变 +18、其余也多 9。

**修法**：
1. 新增 `Patches/PhantomBladesFirstShivPatch`（`ModifyDamageAdditive` 后置）：
   · 牌主是我们的影子 Boss、且这张刀**在计划里**：第一把保留、后面的压回 0；
   · **不在计划里**的刀（`ShivDamagePerHit` 拿合成刀算「每把刀多少」时用的那种）⇒ 原样返回，
     交给 `BossIntentBuilder` 的 `shivBonus` / `shivBonusBakedIn` 那套加减账。
2. `CharBossMonster` 新增 `IsFirstPlannedShivCard` / `PlanContainsShivCard`（判据挂在 Boss 上，补丁与意图两侧共用）。
3. `BladesGrowth`：幻影之刃**已经在身上**（`phantomOnHer`）时，真刀不再由我们手工加那份增伤（否则第一把 +2N）。

版本 **v0.2.350**；`content/` 未变 ⇒ PCK 未重打。dll 891,904 B（MD5 `FBBA5973A2`）。
已装游戏 + 工坊（23:10）。changenote / `workshop.json` 已更新。⚠ 工坊仍未上传。


### 追加：v0.2.349 —— 遗物栏「缚魂命匣没被替换」的真因（用户猜对了）

**用户 2026-11-03**：「养手骨仍然没有遗物『欧洛巴斯之触』，无界命匣还是没有替换缚魂命匣。
**是不是之前有地方把 boss 遗物写成硬编码了**？」

**日志实证**（22:59:48 那场养手骨）：
`NRelicContainerHolder-RELIC_PHYLACTERY_UNBOUND` **与** `RELIC_BOUND_PHYLACTERY` **同时存在**；
且召唤行是 `奥斯提召唤成功：amount=1 source=BOUND_PHYLACTERY`（每回合 1）——
说明 `TurnStartSummonOf` 先扫到的是**角色自带的缚魂命匣**。

**真因**：`ShadowPlayerFactory` 用 `Player.CreateForNewRun(character, …)` 建影子玩家，
而它会**自动发该角色的起始遗物**（亡灵契约师 = `BoundPhylactery`）。
于是定义里写「无界命匣」之后，她身上是**两件都有**（自带缚魂命匣 + 我们加的无界命匣）。

**修法**（`ShadowPlayerFactory`，在加完定义里的遗物之后）：
若遗物表里含 `PhylacteryUnbound` ⇒ 用 `shadow.RemoveRelicInternal(旧缚魂命匣, silent: true)` 把
角色自带的那件摘掉（并日志）。只在这一档生效；其它亡灵契约师 Boss 的列表里写的就是缚魂命匣，
自带那件与定义重名、原本就会被 `GetRelicById != null` 跳过 ⇒ 行为不变。

同时把 `TouchOfOrobas`（欧洛巴斯之触）**加回**她的遗物栏（第 4 位）：它就是做这次升级的遗物，
理应留着；它的「拾起时换遗物」逻辑仍对我们这只 Boss 关掉（已经升级过了）。

现在遗物栏：无界命匣（起始位）、涅奥的祝福、骨笛、欧洛巴斯之触、剃刀牙、钗、书签；
召唤：第 1 回合 `5+2=7`，之后每回合 `2`。

版本 **v0.2.349**；`content/` 未变 ⇒ PCK 未重打。dll 890,880 B（MD5 `2C0DC61574`）。
已装游戏 + 工坊（23:06）。changenote / `workshop.json` 已更新。⚠ 工坊仍未上传。


### 追加：v0.2.347 —— 无界命匣的位置 + 召唤不出来的真因

用户 2026-11-03：「欧洛巴斯之触是将初始遗物替换为先古版本，所以应该**直接把缚魂命匣换成无界命匣**，
它就应该在初始遗物那个位置，而不是二者同时存在。并且现在并没有成功召唤出奥斯提。
另外改这些的时候千万别影响到别的亡灵契约师。」

1. **遗物栏**：`OstyKeeperScript.Relics()` 去掉 `TouchOfOrobas`，只留
   `[PhylacteryUnbound, NeowBlessing, BoneFlute, RazorTooth, Sai, Bookmark]` —— 无界命匣就在起始遗物位。
2. **召唤不出来的真因**：战斗开始那一刻 `CombatManager.IsInProgress` 还是 false，
   `PlayerCmd.AddPet` 会抛错（Telemetry 里那条已知噪音「PHYLACTERY_UNBOUND：召唤时机不在战斗进行中」
   就是它）⇒ 原来放在 `FireCombatStart` 里的「战斗开始召唤 5」永远拿不到，还可能连带把她的奥斯提搞没。
   ⇒ 删掉 `FireCombatStart` 里那段，改由 `BossOsty.SummonAndRegister` 在**她第 1 个回合开始**一次性给
   `StartOfCombat(5) + StartOfTurn(2) = 7`，之后每回合 2（新增 `BossOsty.FirstTurnExtraSummonOf`）。
   召唤成功/失败都写了日志。
3. **不影响其它亡灵契约师**：`TurnStartSummonOf` 对 `BOUND_PHYLACTERY` 仍返回 `Summon`(=1)，
   `FirstTurnExtraSummonOf` 对缚魂命匣返回 0；`IsTurnStartRelic` / `OstyUnboundNoCombatStartSummonPatch`
   只对新遗物生效 ⇒ 灾厄骨 / 灵魂骨 / 根除骨行为与以前完全一致。

版本 **v0.2.347**；`content/` 未变 ⇒ PCK 未重打。dll 889,344 B（MD5 `0F267AC5AC`）。
已装游戏 + 工坊（22:57）。changenote / `workshop.json` 已更新。⚠ 工坊仍未上传。


### 追加：v0.2.346 —— 谋杀口径再确认 + 事件机卡回合

**谋杀**（用户 2026-11-03：「谋杀还是同一张，增伤应该保持」）：
`BossScriptRunner.BuildHand` 里 `MurderDrawBaseline` 改成**整场只钉一次**（第一次进她手牌时），
之后不再因为「它离手 / 下一手重建」而重置 ⇒ 加伤只增不减（打出后再进手也接着累计）。

**事件机「没法结束回合」**：两个可疑点，都改成安全写法：
1. `Patches/BossDiscardToHandStrengthPatch` 以前在补丁里**另起异步任务** `PowerCmd.Apply` ——
   它会往战斗动作队列塞动作，可能和「正在 await 的那次出牌」互相等 ⇒ 她的回合永不结束。
   现在补丁只 `boss.PendingReturnStrength++` 记账，真正加力量在 `PlayOne` 里**同一个 await 链**结算
   （`Core/EventMachineMechanics.GrantStrengthToAllUnitsAsync`），每回合开始把池子清零。
2. 「羽化」的卡牌展示 `BossCardPreview.ShowAsync` 改成**不 await**（`TaskHelper.RunSafely`），
   展示本身卡住也不会拖死这一手。
⚠ 若仍能复现，需要 `%APPDATA%\SlayTheSpire2\logs\mod_log.txt` 里她那一回合的尾部
（`PLAY …` / `一心化万` / `羽化` / `出牌阶段结束` 这几行）才能定位。

版本 **v0.2.346**；`content/` 未变 ⇒ PCK 未重打。dll 888,832 B（MD5 `71E0993E66`）。
已装游戏 + 工坊（22:53）。changenote / `workshop.json` 已更新。⚠ 工坊仍未上传。


### 追加：v0.2.345 —— 养手骨改成「无界命匣」召唤

用户 2026-11-03：「死者苏生还是没打出来。算了这样吧，不搞注能牌了，你把她的放电异虾改成欧洛巴斯之触，
把缚魂命匣改成无界命匣（召唤奥斯提相关代码参考缚魂命匣）」。

**删掉的**：`BossOstyUndyingPower` 里整套「注能·死者苏生+」（`AfterApplied` + `PlayImbuedReanimate` +
`GrantCombatStartSummon`），以及 `CharBossMonster.OstyReanimatePlayed` 标记；卡组里的 `Reanimate` 也去掉。
（`DownfallCollectibleCompat` 里「养手骨藏品 = 死者苏生」**保留**，那是玩家拿到的收藏牌。）

**换上的遗物**（`OstyKeeperScript.Relics()`）：
- 缚魂命匣 `BoundPhylactery` → **无界命匣** `PhylacteryUnbound`（`StartOfCombat`=5、`StartOfTurn`=2，**没有** `Summon` 变量）
- 放电异虾 `ElectricShrymp` → **欧洛巴斯之触** `TouchOfOrobas`（拾起时把起始遗物升级成远古版 —— 它正好就是
  `BoundPhylactery → PhylacteryUnbound` 那条映射，所以设计上说得通）

**召唤接线**（沿用原来缚魂命匣那套）：
- `BossOsty.TurnStartSummonOf(shadow)` / `CombatStartSummonOf(shadow)`：两种遗物都认得，读对对应的变量；
  原来三处「直接读 `DynamicVars.Summon`」的地方全部改走它（无界命匣没有 `Summon` 变量，直接读会退回 1）。
- 战斗开始召唤 5：`BossHookBridge.FireCombatStart` 里结算；
  新增 `OstyUnboundNoCombatStartSummonPatch` 把原版 `PhylacteryUnbound.BeforeCombatStart` 对影子拦住（防双份）。
- 每回合开始召唤 2：仍由 `BossOsty` 结算；`IsTurnStartRelic` 加上 `PHYLACTERY_UNBOUND`；
  `BladesGrowth` 的「出击」预补从 +1 改成 +2。
- 新增 `OstyTouchOfOrobasNoPickupPatch`：她的遗物已经是升级版 ⇒ 关掉欧洛巴斯之触的「换遗物」逻辑。

版本 **v0.2.345**；`content/` 未变 ⇒ PCK 未重打。dll 889,344 B（MD5 `B6DB4C7E15`）。
已装游戏 + 工坊（22:47）。changenote / `workshop.json` 已更新。⚠ 工坊仍未上传。


### 追加：v0.2.344 —— 谋杀改口径 + 碎屑储「伤口→疑虑」核查

**谋杀（用户 2026-11-03 改口径）**：「改成她保留在手牌中时抽牌才增加伤害（把这个加到特殊效果里）」。
- `BossScriptRunner.BuildHand`：把加伤起点钉在「谋杀第一次进手那一刻」的累计抽牌数
  （`CharBossMonster.MurderDrawBaseline`），伤害 = `CalculationBase(1) + ExtraDamage × 起点之后抽到的张数`；
  谋杀离手（打出）后起点重置（`-1`），下次进手重新起算。
- 例：脚本里 T4 谋杀进手（保留）→ T5 打出：只算 T5 那一手新抽到的牌。
- **特殊效果「计划妥当」的说明**里加了这条规则：`SILENT_BOSS.well_laid` 与
  `BOSS_WELL_LAID_PLANS_POWER.description` 各 16 种语言全部更新（checker 全绿）。

**「碎屑储君的伤口是不是没改成疑虑」**：查证 —— 早就改了。`DebrisRegentScript.cs` 第 4 / 8 回合
已经是 `Doubt()`（v0.2.288 / 2026-11-01 改的），文件里也有当时的注释。
⇒ 那位玩家跑的是 **v0.2.287 或更早**；让他看 `mod_log.txt` 首行的
`===== CharacterBosses 构建：v0.2.xxx =====` 就能确认版本。

版本 **v0.2.344**；`content/` 变了 ⇒ **PCK 已重打**（13,252,040 B）。dll 891,392 B（MD5 `4525765EDE`）。
已装游戏 + 工坊。changenote / `workshop.json` 已更新。⚠ 工坊仍未上传。


### 追加：v0.2.343 —— 养手骨战斗开始没打出「注能 · 死者苏生+」

用户报：战斗开始时既没打出那张牌，也没有那次召唤（效果完全没发生）。

**根因**：这一步只挂在 `BossOstyUndyingPower.AfterApplied` 上，而它内部
`if (shadow == null || combat == null || pcs == null) return;` 是**静默返回** —— 那一刻只要
影子玩家/牌堆还没就绪就什么都不做，日志里也看不出来；两条路（打出牌 / 退回召唤）都没走到。

**修法**（`OpenBossHookBridge.SideTurn.FireCombatStart` + `BossOstyUndyingPower`）：
1. 在**战斗开始那段流程**里显式调 `Powers.BossOstyUndyingPower.PlayImbuedReanimate(boss, ctx)`
   （判据：`Def.SpecialPowerForAct(act) == typeof(BossOstyUndyingPower)`），不再依赖钩子时机；
2. `CharBossMonster.OstyReanimatePlayed` 幂等 ⇒ 两条触发路径都走也只结算一次；
3. 每个失败分支都写日志（shadow/combat/pcs 到底哪个是 null、牌取不到、AutoPlay 抛了什么），
   并且 `pcs` 没就绪时也**退回直接召唤**；
4. 保底召唤值改成 **25** —— 「死者苏生+」是升级版（卡面 20 + 升级 5），和真打出的那张一致
   （以前写死 20）。

版本 **v0.2.343**；`content/` 未变 ⇒ PCK 未重打。dll 890,880 B（MD5 `C4876847D4`）。
已装游戏 + 工坊（22:32）。changenote / `workshop.json` 已更新。⚠ 工坊仍未上传。


### 追加：v0.2.342 —— 蛇眼猎 4 修 + 遗物 + 中文改名

**改名（只动中文）**：静默猎手各套设计的中文简称 **贼 → 猎** ——
奇巧贼→奇巧猎、涂毒贼→涂毒猎、炼药贼→炼药猎、刀贼→刀猎。
仓库里 87 个文件（代码注释 + docs + tools 注释）批量替换；`.dshwolf/`、`buglog.json`、
`mod_log*.txt`、用户自己的 `sheet*.txt` 这些**历史记录**保留原文（用户以后仍可能口语说「贼」，
含义见 `.dshwolf/cerebrum.md` 的 Conventions）。

| 用户报的 | 根因 | 修法 |
|---|---|---|
| 有的卡费用没被强制修改（跟踪没改成 3 费） | `CostThisTurn` 用的是 `AddThisCombat(-delta, reduceOnly: true)` —— **reduceOnly 只允许降费**，所以剧本要「涨价」的牌（跟踪 2→3）静默失败 | 改成 `SetThisCombat(目标, false)` |
| （同上，另一半） | 她带着「异蛇之眼」⇒ 有 `ConfusedPower`，它在**牌进手时**把费用重随机成 0~3（`SetThisCombat`） | 新增 `Patches/ConfusedPowerBossPatch`：牌主是我们的影子 Boss 就跳过随机 |
| 谋杀没按抽牌数增伤 | 她的牌是剧本每回合重建的、从不走原版抽牌 ⇒ 历史里 `CardDrawnEntry` 一条都没有 ⇒ `CalculatedDamage = CalculationBase(1) + ExtraDamage × 0` | `BossScriptRunner.BuildHand` 记账 `CharBossMonster.CardsDrawnThisCombat`（每回合手牌数累加），把 `CalculationBase` 改写为 `base + per × 张数` |
| 抱抱先生打完后意图还在 | 意图直接读 `UpcomingDamage = TurnNumber`，没有任何「本回合已结算」标记 | `MrStrugglesPlays` 结算后记 `MrStrugglesFiredForTurn`，`UpcomingDamage` 在该回合之后返回 0（下回合 `PrepareTurn` 让 `TurnNumber`++ 后自然恢复） |
| 加遗物「斗篷扣」 | 原版挂 `BeforeSideTurnEnd`，影子玩家收不到广播（且只有 `FireBossRelicHooks` 的 Boss 才走那条补发路径） | 新增 `Core/CloakClaspPlays`（回合结束、丢手牌之前：格挡 = 遗物 Block × 手牌张数），加进 `SnakeEyesSilentScript.Relics()` |

顺带：工坊简介里的 Boss 数量 20（5/7/8）→ **23（5/8/10）**。

版本 **v0.2.342**；`content/` 未变 ⇒ PCK 未重打。dll 890,368 B（MD5 `960F07B485`）。
已装游戏 + 工坊（22:27）。changenote / `workshop.json` 已更新（note 551 字符）。⚠ 工坊仍未上传。


### 追加：v0.2.341 —— 养手骨能量/致死性/猛晃 + 情感芯片

| 用户报的 | 根因 | 修法 |
|---|---|---|
| 养手骨初始能量应该是 2，第 4 回合起因第 3 回合打出「友谊」才变 3 | 我上一版把 9 个回合全写成 3 费 | `OstyKeeperScript`：第 1～3 回合 `Energy = 2`，第 4～9 回合 `Energy = 3`（友谊的 +1 从她第 4 回合生效；影子玩家收不到那条能量钩子 ⇒ 只能写死） |
| 「致死性」开了以后每回合第一张攻击牌的预览还是没增加 | 那几张「第一张攻击牌」（击掌 / 紧追不放+ …）都是**奥斯提**的牌，走 `AttackIntent.Raw` —— 渲染时**不过** `Hook.ModifyDamage`，所以 power 的 ×1.5 永远不会落到预览上；而普通攻击牌走 `Live*Intent`→`FinalDamage`（带 cardSource）会自己乘 | `BossIntentBuilder.IntentsFor`：**只对 `fromOsty`** 的那一支手动乘 `(100+pct)/100`（pct 读 `LethalityPower`），并且只在 `IsFirstPlannedAttackCard(card)` 时乘 —— 普通牌不动，避免 ×2.25 |
| 「猛晃」段数不对 | 段数 = 1 + 本回合奥斯提已攻击次数，取自战斗历史；意图在她出手前算 ⇒ 恒 1。上一版只在**规划期**补了 extraHits，重画意图（`BladesGrowth.RecomputeAll`）时又退回 1 | `BossIntentBuilder.IntentsFor` 里对 `RATTLE` 按 `boss.OstyAttacksBefore(card)`（走 `Plan` 顺序）补 `extraHits`；规划期 `Plan` 为空 ⇒ 由规划器本地计数器补，不会双算 |
| 情感芯片的功能要实现 | 原版 `EmotionChip.AfterPlayerTurnStart` 用「上一个**玩家**回合」的历史判断掉血，影子玩家回合号是手动对齐的 ⇒ 判定永远不成立 | 新增 `Core/EmotionChipPlays.cs`：每回合开始记她的生命基线（`CharBossMonster.HpAtOwnTurnStart`），下个回合开始若掉过血 ⇒ 用原版同一入口 `OrbCmd.Passive(ctx, orb, null, true)` 逐颗触发被动；`Patches/EmotionChipBossTakeoverPatch` 把原版那条对我们这只 Boss 关掉（防双份） |

顺手把「致死性/第一张攻击牌」的判据统一成 `CharBossMonster.IsFirstPlannedAttackCard`
（规划期看 `PlannedFirstAttackCard`，出牌期查 `Plan`），补丁与意图构造两侧共用同一份逻辑。

版本 **v0.2.341**；`content/` 没变 ⇒ **PCK 不用重打**。dll 886,784 B（MD5 `4F2946B3A8`）。
已装游戏 + 工坊（22:15）。changenote / `workshop.json` 已更新（note 506 字符）。⚠ 工坊仍未上传。


### 追加：v0.2.340 —— 蛇眼猎打不出来 + 事件机一轮反馈

**① 蛇眼猎的致命错误：`VariantsByAct` 下标插错了**
用户实测 `fight SILENT_SNECKO_BOSS_ENCOUNTER` 打出来的却是涂毒贼。根因：v0.2.337 加「蛇眼猎」时
把这一项**插到了数组最前面**（= 一层 `[0]`），于是二层 `[1]` 是 null（退回主设计 = 涂毒贼），
而「炼药贼」也被挤到 `[3]`（= 第四层）。已改成按幕摆正并加了「下标 = 幕下标」的注释。
⚠ 教训：`VariantsByAct[i]` 的 i 是**幕下标（0 起）**，加变体必须数好位置。

**② 事件机 6 条**
| 用户要求 | 落点 |
|---|---|
| 羽化的展示不要自绘、要用原版的（能量汲取那套） | 新增 `Core/BossCardPreview.cs`（`CardCmd.Preview` + `SuppressPreviewFlyPatch` + 副本从玩家弃牌堆摘掉），`EventMachineMechanics` 改用它；自绘的 `UI/BossCardShowcase` 不再用于羽化 |
| 疯狂科学要的是**技能牌**（获得格挡 + 印一张 0 费牌），并**拦掉它的印牌** | `BossScript.BuildHand`：`TinkerTimeType = Skill`、`TinkerTimeRider = None`（⚠ 不能让 Chaos rider 跑：工厂返回空时它会 `.First()` 抛异常） |
| 拦掉万物一心的返手（已经提前算好） | `AllForOneReturnsAsync` 不再搬牌，只按剧本点名张数给力量 |
| 全息影像的捞牌也要触发「一心化万」 | 新增 `Patches/BossDiscardToHandStrengthPatch.cs`（`CardPileCmd.Add(card, PileType.Hand, …)` 前后置：来源是弃牌堆 + 牌主是我们的 Boss ⇒ 所有单位 +1 力量） |
| 伤害意图没算疯狂进食的临时力量 | `BossScriptPlanner` 里「本回合会拿到的力量」从「只认预备打击」改成**通用** `BossDamagePredictor.StrengthGainOf` |
| 大蘑菇 +20 最大生命 | `BossVariant.HpBonus` + `BossDefinition.VariantHpBonusForAct` + `MinInitialHp/MaxInitialHp/FixActHp`，事件机填 20 |
| 王室猛毒改在战斗开始掉血（她第 1 回合开始不掉） | `BossHookBridge.SideTurn`：新增 `ApplyRoyalPoisonAtCombatStart`（战斗开始掉 4 点 无格挡+无来源） |
| 加遗物：玉之剑（涅奥的祝福与数据磁盘之间）、失效的无礼之茶（旺购徽章右侧）、情感芯片（末尾） | `EventMachineScript.Relics()` 重排；`SwordOfJade` 由新增的 `GrantSwordOfJadeStrengthAtCombatStart` 补力量；`TeaOfDiscourtesy` 在 `ShadowPlayerFactory` 里反射把 `CombatsLeft` 置 0（= 已失效）；`EmotionChip` 仅作藏品（她的回合开始钩子收不到） |

版本 **v0.2.340**；`content/` 没变 ⇒ **PCK 不用重打**（仍 13,247,816 B）。
dll 883,200 B（MD5 `40A224CD29`）。已装游戏 + 工坊（22:06）。changenote / `workshop.json` 已更新（note 718 字符）。
⚠ 工坊仍未上传。

### 追加：v0.2.339 —— 养手骨 4 修 + 剧本重排（用户逐条点名）

| # | 现象 | 根因 | 修法 |
|---|---|---|---|
| 1 | 第 2 回合「猛晃」「增生」不出 | `IsOstyAttack` 的名单只有 戳击/重压/出击/碎骨，**漏了「紧追不放」/「猛晃」/「击掌」/「响指」** ⇒ 规划期「重压」不免费、按 2 费记账吃掉能量 | 名单 → `OstyAttackEntries`（出击/碎骨兜底）+ **数据判据** `BossDamagePredictor.HasVar(card,"OstyDamage")` |
| 2 | 伤害预览没算「致死性」 | `LethalityFirstAttackOnlyPatch` 只能查 `boss.Plan`，而**规划期 Plan 还没填好** ⇒ 每张攻击牌的 ×1.5 都被压回 ×1 | 新增 `CharBossMonster.PlannedFirstAttackCard`，`BossScriptPlanner.Plan` 里填、补丁**先查它**再回落 Plan |
| 3 | 特殊效果「不死」图标没变灯泡 | `BossSpecialPowerIconPatch` 是**逐个类型点名**的白名单，`BossOstyUndyingPower` 漏了 | 判据改成「**是不是本 mod 的 Power**」（沿基类链查程序集）⇒ 以后所有特殊效果自动用「好奇」灯泡；麻痹/跟踪锁定两个专门图标保留 |
| 4 | 第 7 回合同 #1 | 同上 | 同上 |
| 5 | （顺带）「猛晃」段数 | `Rattle.CalculatedHits` 数的是战斗历史，规划期历史为空 ⇒ 恒 1 段 | 规划期记 `ostyAttacksThisTurn`，`extraHits` 补给它（第 2/7 回合 = 3 段） |

**剧本重排**（`Core/OstyKeeperScript.cs`，全部照用户原话）：9 回合，**`LoopFrom=7, LoopLength=3`**（以前 6～8）。
战斗开始仍是「注能·死者苏生+」。逐回合：
1 响指(1，`PickOrder` 点名给吸引仇恨加保留)+护卫(1)+吸引仇恨(2，没费、`Hold+Retain`)；
2 紧追不放(1)+重压(0)+猛晃(1)+吸引仇恨(1，`Hold+Retain`)；
3 吸引仇恨(0)+友谊(1)+出击(1)+增生(1，`Hold+Retain`)；
4 致死性(1)+击掌(2)+增生(0)+笨拙；
5 钙化(1)+来生(1)+死亡之舞(1)；
6 护卫+(1)+出击+(1)+吸引仇恨+(2，`Hold+Retain`)；
7 紧追不放+(1)+重压+(0)+猛晃+(1)+吸引仇恨+(1)；
8 响指+(1，`PickOrder` 点名给增生+加保留)+击掌+(2)+增生+(1，`Hold+Retain`)；
9 增生+(0)+护卫+(1)+出击+(1)+吸引仇恨+(2，`Hold+Retain`)。
每回合 `Energy = 3`（亡灵契约师 4 − 涅奥 1），账逐回合核对过（注释里写着）。

版本 **v0.2.339**；`content/` 没变 ⇒ **PCK 不用重打**（仍是 13,247,816 B，20:16 那版）。
dll 875,008 B（MD5 `039421B4C4`）。**已安装**游戏 + 工坊两处（21:45）。changenote / `workshop.json` 已更新（note 952 字符）。
⚠ 工坊仍未上传。

### 追加：v0.2.338 —— 三层变体「事件机」（两个新 Boss 全部落地）

用户 2026-11-03 的两条口径（原话）：

* 「疯狂科学印的牌**不用展示**，因为是直接印手牌的。羽化展示是因为印到抽牌堆了。」
* 「**以后做完就直接装游戏**」⇒ 每次 build 完直接装 `mods\CharacterBosses` 与
  `steamapps\workshop\content\2868840\3830639796\CharacterBosses`（两条路径在工作区外，需要 full-access）。

**落点**
| 落点 | 内容 |
|---|---|
| `Core/EventMachineScript.cs`（新） | 9 回合剧本（`LoopFrom=5, LoopLength=5`）+ 卡组 + 遗物；能量逐回合写死 3 |
| `Core/EventMachineMechanics.cs`（新） | ① 万物一心按剧本名单返手 0 费攻击牌 ⇒ 每张给**所有单位** +1 力量；② 羽化造 3 张 0 费攻击牌进抽牌堆并 `BossCardShowcase` 展示 |
| `Patches/BossAllForOneFilterPatch.cs`（新） | 让她打出的 `AllForOne.Filter` 恒 false（原版会把 0 费技能/能力也拿回来） |
| `Powers/BossAllForOnePower.cs`（新） | 特殊效果「一心化万」的显示用 power |
| `Core/BossScript.cs` | `ScriptCard` 新增 `ReturnsToHand` / `PrintsToDrawPile` / `ShowPrints` / `EnchantSpiral`；`BuildHand` 处理「漩涡」附魔与 `MadScience.TinkerTimeType = Attack`；`BossScriptRunner.FindEntry` |
| `Monsters/CharBossMonster.cs` | 出牌后结算万物一心 / 羽化；**余费补打**条件加 `ActIndex == 2 && TurnNumber == 3`（用户要的「其它三层 Boss 第三回合没费的牌有费就打」） |
| `Core/BossScriptPlanner.cs` | 万物一心返手 N 张 ⇒ 后续攻击意图预补 N 力量 |
| `Core/MultiplayerCardSwap.cs` | 启动流程 → 能量涌动（多人） |
| 注册 | `BossKeys`（Key/三层候补/互补 能力机↔事件机/`EncounterOf`）、`BossLineups.EventMachineActive`（salt **57**）、`BossDefs.Defect.VariantsByAct[2]`、`TierBossEncounters.DefectEventBossEncounter`（`TierAct=2`）、`CharBossEncounters`（+ `DefectCharacterIndex = 2`）、藏品=万物一心、灯泡图标（顺带补 v0.2.337 漏掉的「计划妥当」） |
| 本地化 | `DEFECT_BOSS.one_for_all` + `BOSS_ALL_FOR_ONE_POWER.*` + `DEFECT_EVENT_BOSS_ENCOUNTER.*` 共 5 条 × **16 种语言全部人工翻译**（checker 16 目录全绿） |

**两个「数据填不出来」的坑（反编译确认）**
* `MadScience.Type => TinkerTimeType`，该字段默认 `CardType.None`，`OnPlay` 的 `switch` 对 None 直接
  `throw` ⇒ 造牌时必须设成 `Attack`，否则一打出就抛异常。
* 羽化原版是随机印 3 张攻击牌（`CardFactory.GetDistinctForCombat`，已被我们拦成空），
  所以印哪三张必须由剧本给。

版本 **v0.2.338**；`content/` 变了 ⇒ PCK 已重打（`Packed 80 content file(s)` + `48 extra file(s)`，
13,247,816 B）。dll 873,984 B。**已安装**到游戏与工坊两处（21:20）。
changenote / `workshop.json` 已更新（note 707 字符）。⚠ 工坊**仍未上传**。

### 追加：v0.2.337 —— 二层变体「蛇眼猎」

用户 2026-11-03 一次给了两个新 Boss 的完整设计稿，原话**逐字**记在
`docs/plans/boss-snake-eyes-and-event-machine.md`（含名字→类对照、能量账、待实现机制清单）。

**已实现：二层「蛇眼猎」`SILENT_SNECKO`**（静默猎手 · 异蛇之眼轴，与「涂毒贼」二选一）
| 落点 | 内容 |
|---|---|
| `Core/SnakeEyesSilentScript.cs`（新） | 12 回合剧本（`LoopFrom=10, LoopLength=3`）+ 卡组 + 遗物；逐张写死费用 |
| `Core/BossScript.cs` | 新增 `ScriptCard.Retain`（+ `BuildHand` 里 `CardKeyword.Retain`）⇒ 「计划妥当」= 保留该牌 |
| `Powers/BossWellLaidPlansPower.cs`（新） | 特殊效果「计划妥当」的显示用 power（判定在剧本里，避免与剧本打架） |
| 注册 | `BossKeys`（Key/二层候补/互补变体 涂毒贼↔蛇眼猎/`EncounterOf`）、`BossLineups.SneckoActive`（salt 56）、`BossDefs.Silent.VariantsByAct[1]`、`TierBossEncounters.SilentSneckoBossEncounter`（`TierAct=1`）、`CharBossEncounters`（`TierEncounters` + `TierEncounterFor(1,1)`）、`BossBgm`（二层帝皇蟹）、`DownfallCollectibleCompat`（藏品=子弹时间） |
| 本地化 | zhs/eng 手写 `SILENT_BOSS.well_laid`、`BOSS_WELL_LAID_PLANS_POWER.*`、`SILENT_SNECKO_BOSS_ENCOUNTER.*`；其余 14 种语言用英文占位补齐（checker 16 目录全绿） |

**异蛇之眼的做法**：原版随机改费**被绕过**（她的手牌本来就是剧本重建的），改为每张牌
`ScriptCard.CostThisTurn = 用户给的数字` ⇒ 卡面/规划/扣费一致；「不出」的牌也照样写费用
（`HoldInstead`），子弹时间后面那批写 0 费。
能量逐回合写死：第 1 回合 **0 费**（`ForceZeroEnergy`）、第 4 回合 2 费、其余 3 费。

**未实现：三层「事件机」`DEFECT_EVENT`** —— 它的核心机制都比数据复杂，已列成清单：
① 万物一心只返**0 费攻击牌** + 每返一张「所有单位 +1 力量」；② 南瓜蜡烛 5 充能每回合 +1 费（影子玩家要手动结充能）；
③ 羽化/疯狂科学要**拦成指定牌**并把那几张**展示给玩家**（像能量汲取敲牌那样）；
④ 「让其它三层 Boss 第三回合加一条：没费的牌有费就打」（影响所有三层 Boss 的规划器）；
⑤ 选择悖论印的「飞跃」带保留。**下一版按设计稿实现。**

版本 **v0.2.337**；`content/` 变了 ⇒ **PCK 已重打**（`Packed 80 content file(s)` + `48 extra file(s)`，
13,231,720 B）。dll `6537ED51AF`。⚠ 仍未安装（沿用用户「不要发沙箱外请求」的要求）。
changenote / `workshop.json` 已更新（note 505 字符）。


### 追加：v0.2.336 —— 养手骨三处修正（用户逐条点名）

用户：「1.做致死性的伤害补偿。3.你可以不通过这张牌生效，但你在战斗开始要展示亡灵契约师打出了带有注能的死者苏生。
另外注意死者苏生是升级的。4.响指是0费」

1. **致死性（`LethalityPower`）的意图补偿** —— 新增 `Patches/LethalityFirstAttackOnlyPatch.cs`。
   反编译确认原版判定：数 `CombatManager.History.CardPlaysStarted` 里本回合她**已经开始打**的攻击牌，
   数量匹配才返回 `1 + Amount/100`（×1.5）。而**意图是规划期画的**，那时历史里一张都没有
   ⇒ 判定对**每一张**攻击牌都成立 ⇒ 第 2 张起的攻击意图虚高 50%。
   补丁用**本回合计划里第一张攻击牌**（`boss.Plan` 里第一个非 `StampedeOnly` 的 `CardType.Attack`）
   当唯一受益者，其余回落 ×1；真正出牌时原版按历史自己算，两边一致。
2. **战斗开始展示「注能 · 死者苏生+」** —— `Powers/BossOstyUndyingPower.PlayImbuedReanimate`：
   造一张 `Reanimate` → `UpgradeInternal()`（**升级版**）→ 附魔 `MegaCrit…Enchantments.Imbued`（注能）
   → 进手牌 → `CardCmd.AutoPlay(...)`。⇒ 玩家能看到牌面动画/音效，召唤值取卡面（不再写死 20）。
   取不到卡或附魔失败时退回旧行为（直接召唤 20），不会卡流程。
3. **响指 0 费** ⇒ 重排能量：第 1 回合 响指 0 + 护卫 1 = 1；第 8 回合 护卫+ 1 + 响指+ 0 + 击掌+ 2 = **3**
   ⇒ 八个回合统一 **3 费**（= 基础 4 − 涅奥 1），不再有那个离群的 4。

版本 **v0.2.336**；本轮只改代码（`content/` 没变）⇒ **PCK 未重打**（仍是 v0.2.335 的 `13,223,880 B`，
比 content 最新时间戳 15:19:31 晚，确认是最新）。dll `91B41C1599`（855,040 B，17:48）。
`tools/check-localization.ps1` 16 个语言目录全绿；changenote / `workshop.json` 已更新（note 347 字符）。

⚠ 仍未安装：游戏 `mods\CharacterBosses\` 与工坊目录还是 v0.2.334 的 dll。等用户说「装 / 上传」再动。

⚠ 顺便修了一处自己搞出来的事故：我脚本改写 `BuildInfo.cs` 时 anchor 取错（`Select-String` 对整串文本只返回一行 ⇒
把整个文件替换成了一行 `Tag`），已按原 API（`Tag` + `LogOnce()`：打构建行 + 装薄荷尖塔兼容）重建，
命名空间仍是 `CharacterBosses.Core`，编译 0 错误。


### 追加：v0.2.335 —— 新增三层变体 Boss「养手骨」（亡灵契约师 · 奥斯提轴）

用户 2026-11-03 设计稿（原话要点见下），内部名按要求含 **osty** ⇒ Key `NECROBINDER_OSTY`、
遭遇 `NECROBINDER_OSTY_BOSS_ENCOUNTER`、剧本 `Core/OstyKeeperScript.cs`、
特殊效果 `Powers/BossOstyUndyingPower.cs`。

**落地清单**：
| 位置 | 内容 |
|---|---|
| `Core/OstyKeeperScript.cs`（新） | 8 回合剧本（`LoopFrom=6, LoopLength=3`）+ 卡组 + 遗物；能量逐回合写死（2/2/2/3/3/3/2/4，按用户剧本倒推；基础 4 − 涅奥 1 = 3） |
| `Powers/BossOstyUndyingPower.cs`（新） | 特殊效果「不死」：① `Hook.ShouldDie` 前缀 ⇒ 她的奥斯提不判死并补回 1 HP；② `ModifyHpLostBeforeOsty/AfterOsty` 把奥斯提要吃的伤害截到「当前生命 − 1」；③ `DieForYouPower.ModifyUnblockedDamageTarget` 后置：奥斯提只剩 1 HP 时把目标改回**她本人**（`[ThreadStatic] Redirecting` 防递归）；④ `AfterApplied` 在战斗开始结算「注能 · 死者苏生+」= 召唤 20（只一次） |
| `Core/BossKeys.cs` | 新 Key、三层候补、互补变体（根除骨 ↔ 养手骨）、`EncounterOf` |
| `Core/BossLineups.cs` | `OstyActive()` / `ComputeOsty()`（遭遇优先，种子 salt 55 兜底） |
| `Core/BossDefs.cs` | `Necrobinder.VariantsByAct[2]`；进阶 8 = 死亡之舞+来生 + 骇人头盔，进阶 9 = 致死性+钙化 |
| `Encounters/TierBossEncounters.cs` | `NecrobinderOstyBossEncounter`（`TierAct => 2`） |
| `Encounters/CharBossEncounters.cs` | `TierEncounters` 注册 + `TierEncounterFor(3, 2)` 分支 + `NecrobinderCharacterIndex` |
| `Core/BossBgm.cs` | 三层沿用「女王」 |
| `Core/DownfallCollectibleCompat.cs` | 藏品 = 死者苏生 `Reanimate` |
| 本地化 | zhs/eng 手写（`NECROBINDER_BOSS.undying`、`BOSS_OSTY_UNDYING_POWER.*`、`NECROBINDER_OSTY_BOSS_ENCOUNTER.*`）；其余 14 种语言先用**英文原文**占位补齐（`tools/check-localization.ps1` 16 个目录全绿），之后可再翻 |

**多人**：吸引仇恨 → 骸骨军团 用的是已有的 `MultiplayerCardSwap` 表（三层亡灵契约师那一行本来就在），
所以单/多人自动切换；「不死」只认自己主人那只奥斯提（`BossOsty.IsBossOsty` + 她有这层 power），
其它 Boss 的奥斯提不受影响。

**尚未做 / 待实机验证**：
1. `ModifyHpLostBeforeOsty/AfterOsty` 与 `DieForYouPower` 的**实际接管顺序**没有实机验证过
   （只靠反编译推断）⇒ 第一次进战斗要看日志 `不死：…` 系列是否按预期出现；
2. 「致死性」（每回合第一张攻击牌 +50%）的**意图预览**还没做规划期补偿（和当年手里剑/预备打击
   一样的活），目前意图可能对第 2 张起的攻击牌偏高 —— 用户明确要求「伤害预览要正确」，这条**必须补**；
3. `注能 · 死者苏生+` 目前是「直接召唤 20」而不是真的打出一张牌（避免再踩 fire-and-forget 的联机坑），
   如果要求严格按「打出一张牌」来，需要接到可 await 的战斗开始链上；
4. 第 8 回合给了 4 费（离群），因为护卫+ 1 + 响指+ 1 + 击掌+ 2 = 4；若用户本意是 3 费（击掌+ 打不出），
   改 `OstyKeeperScript` 第 8 回合的 `Energy` 一个数字即可。

版本 **v0.2.335**；`content/` 变了 ⇒ **PCK 已重打**（`Packed 80 content file(s)` + `48 extra file(s)`，
13,223,880 B）。⚠ **没有安装**：用户说「做完之后就不要发装到沙箱外的请求了，就直接停」⇒
游戏 `mods\CharacterBosses\` 与工坊目录仍是 v0.2.334 的 dll（`4EDE5A94DB`），
本轮新构建产物只在 `src\bin\Release\` 与 `.build\CharacterBosses.pck`。
changenote / `workshop.json` 已更新（note 501 字符）。


### 追加：v0.2.334 —— 辉星储削弱：伽马爆破 → 星界脉冲

用户：「削弱辉星储，伽马爆破换星界脉冲，其它不变」。

`Core/RegentStarScript.cs` 里两处（第 1、5 回合那张 0 费牌）+ 卡组两张一起换：
- 旧：`GammaBlast` 伽马爆破（0 费 · 13 伤 + 2 易伤 + 2 虚弱）
- 新：`AstralPulse` 星界脉冲（0 费 · 对全体 6 伤 ×2）

⇒ 少了单体 13 点 + 双重易伤/虚弱（配合「群星荟萃」给玩家保留/能量的节奏，这一刀挺明显）。
剧本其它回合、卡组其它牌、遗物、进阶表**一个都没动**（用户要求「其它不变」）。
注释与 `Note` 文案同步改成「星界脉冲」，`Deck()` 里那两行 `typeof(GammaBlast)` 也换掉
（否则牌表自检会显示一张剧本里没有的牌）。

版本 **v0.2.334**；`content/` 没变 ⇒ **PCK 未重打**（仍 `105AC39ADF`）。dll `4EDE5A94DB`、
json `5814B6224B`，构建 / 游戏 / 工坊三处一致；changenote 已合并写 v0.2.334 + v0.2.333 两版、
`mk-workshop-json.ps1` 已重跑（note 505 字符）。

⏳ 工坊上传仍等用户（「现在高峰，等会吧」）⇒ v0.2.329 起的版本都还没上工坊。


### 追加：v0.2.333 —— 变体身份丢失（炼药贼的死亡提示显示成刀贼）

用户：「炼药贼死亡提示不对，显示成刀贼的了。你看看其它变体boss会不会也会出现这种情况。」

**会，而且不止一只。**根因统一：死亡提示（`BossLossMessagePatches`）→ `CharBossEncounters.EncounterForBoss`
→ `TierEncounterFor(角色, 幕)`，里面那几个「变体分支」（`SilentBrewerAt3()` / `KingActive()` /
`BoulderAt3()` / `RegentStarAt3()`）都靠 `BossLineups.Latched(...)` = **本场锁定 + 从当前 CombatState
读房间遭遇**。玩家死亡那一刻战斗正在结束、`CombatState` 已经换了 ⇒ 锁定表被清空、遭遇读不到
⇒ 重算落到**同一档的主变体**上：

| 变体 | 幕 | 会错显示成 | 程度 |
|---|---|---|---|
| 杂耍战 `IRONCLAD_JUGGLER` | 2 | 放血战 | **必然**（`TierEncounterFor` 里根本没有这一条） |
| 王储 `REGENT_KING` | 2 | 铸剑储君 | 竞态 |
| 炼药贼 `SILENT_BREWER` | 3 | 刀贼 | 竞态（玩家报的这条） |
| 辉星储 `REGENT_STAR` | 3 | 无色储君 | 竞态 |
| 巨石战 `IRONCLAD_BOULDER` | 3 | 易伤战 | 竞态 |

**修法（治本）**：生成 Boss 的那一刻就把变体身份写进实例 ——
`CharBossMonster.SpawnVariantKey`，由 `CharBossEncounterBase.GenerateMonsters` 写入
（主 Boss 用 `BossKeys.KeyOfEncounter(this)`；多人的伙伴由新方法
`MultiBossPlan.PartnersForWithKeys` 一并返回各自的 Key）。之后：
- `CharBossEncounters.EncounterForBoss` **优先**用 `BossKeys.EncounterOf(boss.SpawnVariantKey)`（覆盖死亡提示、`LockOnPower` 显示名）；
- `DownfallCollectibleCompat.ResolveLoot` 同样优先用它（藏品掉落也不会再错）；
- `Telemetry.SafeEncounter(boss)` 也优先用它（数据表的 encounter 字段不再记成同档另一套设计）。

版本 **v0.2.333**；`content/` 没变 ⇒ **PCK 未重打**（仍 `105AC39ADF`）。dll `3EAA696434`、
json `B178301A39`，构建 / 游戏 / 工坊三处一致；changenote 已写、`mk-workshop-json.ps1` 已重跑（note 517 字符）。

⏳ 工坊上传仍等用户（他说「现在高峰，等会吧」）⇒ v0.2.329 起的版本都还没上工坊。


### 追加：v0.2.332 —— 16 种语言全部补齐

用户：「继续完成其它语言支持」。这一版把剩下的 12 种一次做完，现在游戏设置里的 16 种语言**全部有文案**：

| 语言 | 目录 | 做法 |
|---|---|---|
| 简中 / 繁中 | `zhs` / `zht` | zhs 手写；zht 由 `Microsoft.VisualBasic.Strings::StrConv(TraditionalChinese)` 转换 + 边角修补 |
| English / 日本語 | `eng` / `jpn` | 手写 |
| 한국어 / Русский / Polski | `kor` / `rus` / `pol` | 子代理翻译（术语查官方表） |
| Deutsch / Français / Italiano | `deu` / `fra` / `ita` | 同上 |
| Español (ES) / Español (LatAm) / Português (BR) | `esp` / `spa` / `ptb` | 同上（esp 与 spa 官方术语**确实不同**：Monarca vs Regente、forja vs Forja…） |
| Türkçe / Bahasa Indonesia / ไทย | `tur` / `ind` / `tha` | 同上（泰语官方表较小，缺的词回落英文） |

每语言 5 张表（monsters / powers / encounters / relics / intents），共 **16 × 5 = 80 个文件**。

**新工具**：
- `tools/check-localization.ps1` —— 一键校验所有语言：JSON 可解析、键集合与 zhs 完全一致、
  占位符只用模组注册过的（`{character}/{Boss}/{Pronoun}/{Pronoun2}/{Extra}/{Loss}/{Reduction}`）、
  `\n` 行数一致、`[gold]`/`[blue]` 闭合、非中日韩语言里不得残留汉字。
  用法：`powershell -NoProfile -ExecutionPolicy Bypass -File tools\check-localization.ps1`（可加 `-Langs kor,rus`）。
  ⚠ 含中文的 .ps1 必须存成 **UTF-8 带 BOM**（PS5.1 否则按 ANSI 读，直接语法错）。
- `.build/extract_many.gd` —— 一次从游戏 PCK 抽多张本地化表到 `.build/gameloc/<lang>/`（术语来源）。

**踩过的坑（下次直接复用）**：
1. 我生成的 `_glossary.json` 里**没有 `char:` 键** —— 因为 `characters.json` 我只抽了 jpn/zht。
   需要角色名时要么补抽 `characters.json`，要么让子代理自己从 pck 里读（这次子代理就是这么绕过的）。
2. `{Pronoun}` 里填的是**中文**（他/她/它）、`{Pronoun2}` 填的是英文（his/her/its），都没做本地化
   ⇒ 非中文语言里应该用 `{Pronoun2}`（eng 一直是这么做的），checker 因此只查「未知占位符」、不要求各语言一致。
3. 祭仪 / 火热研究（Downfall 的 token 牌）在任何语言的官方表里都没有 ⇒ 各语言按字面翻译或保留英文。

版本 **v0.2.332**；`content/` 变了 ⇒ **PCK 已重打**（`Packed 80 content file(s)` + `48 extra file(s)`，
13,208,552 B）。dll `840C37AC9A`、json `FD26D95F9F`、pck `105AC39ADF`，构建 / 游戏 / 工坊三处一致；
`tools/check-localization.ps1` 全绿（16 个语言目录）；changenote 已写、工坊描述的语言那一句改成全 16 种、
`mk-workshop-json.ps1` 已重跑（note 471 字符）。


### 追加：v0.2.331 —— 藏品换成「王之凝视 / 契约终结」

用户：「现在高峰，等会吧（指上传工坊）。把王储藏品定为王之凝视，杂耍藏品定为契约终结」
⇒ 只改 `Core/DownfallCollectibleCompat.cs` 两个 case（这张牌就是打赢那只 Boss 后给的**无色藏品牌**）：

| Boss | 旧 | 新 | entry |
|---|---|---|---|
| 王储 `REGENT_KING_BOSS_ENCOUNTER` | 征服者 `Conqueror` | **王之凝视** `MonarchsGaze` | `MONARCHS_GAZE` |
| 杂耍战 `IRONCLAD_JUGGLER_BOSS_ENCOUNTER` | 杂耍 `Juggling` | **契约终结** `PactsEnd` | `PACTS_END` |

版本 **v0.2.331**；`content/` 没变 ⇒ **PCK 未重打**（仍是 v0.2.330 那次 `0F492BFDEE`，
12,917,064 B，含 jpn/zht 四语言）。dll `DE91BEA89C`、json `C44A6767D1`，构建 / 游戏 / 工坊三处一致；
changenote 已写、`mk-workshop-json.ps1` 已重跑（note 95 字符）。

⏳ 上传工坊被用户推迟（「现在高峰，等会吧」）⇒ v0.2.329~0.2.331 这几版**还没上工坊**；
等用户说可以了就跑 `tools\upload-workshop.ps1`。`.dist` 本地包仍是 v0.2.328 那份。


### 追加：v0.2.330 —— 多语言：日本語 + 繁體中文

玩家留言（`takokakao214`）：「japanese plz」。用户问「能不能做到同时直接支持杀戮尖塔2支持的所有语言」。

**能做到**：游戏按 `res://<modId>/localization/<语言代码>/<表名>.json` 找 mod 的本地化表
（`tools/pck_builder/build_pck.gd` 就是把 `content/localization/<lang>/*` 打包成这个路径），
所以加语言 = 加一个目录。清单在 `src/CharacterBosses.json` 里**不用声明**。

**游戏支持 16 种语言**（从 `SlayTheSpire2.pck` 里 `res://localization/` 的目录名数出来的）：
`eng / zhs / zht / jpn / kor / deu / fra / esp / spa / ita / pol / ptb / rus / tha / tur / ind`。

**术语怎么保证和游戏一致**：用 `Godot --headless --script .build/extract_many.gd` 把游戏 PCK 里
15 种语言的 `cards / powers / relics / card_keywords / monsters / encounters / potions / characters`
八张表全抽到 `.build/gameloc/<lang>/`（共 105 个文件），翻译时逐个查官方译名：
巨石 = 巨大な岩、彼岸咆哮 = 彼方からの遠吠え、虚无 = エセリアル、消耗 = 廃棄、易伤 = 弱体、
虚弱 = 脱力、中毒 = 毒、灾厄 = 破滅、铸造 = 鍛造、覆甲 = プレート、力量 = 筋力、
抱抱先生 = ミスター・ストラグル、瓶中精灵 = 瓶詰の妖精、为你而死 = 身代わり、储君 = リージェント…

**本次新增**：
- `content/localization/jpn/`（monsters / powers / encounters / relics / intents 五张表，人工翻译）；
- `content/localization/zht/`（由 zhs 用 `Microsoft.VisualBasic.Strings::StrConv(TraditionalChinese)` 机械转换，
  再补 于→於 / 不低于→不低於 / 等同于→等同於 等边角修补）；
- 工坊描述里加了一句支持语言说明。
- 术语表与抽取脚本留在 `.build/gameloc/`、`.build/extract_many.gd`（`.build` 不进包），后续语言可直接复用。

版本 **v0.2.330**；`content/` 变了 ⇒ **PCK 已重打**（`Packed 20 content file(s)` —— 4 语言 × 5 表，
+ `48 extra file(s)`，12,917,064 B）。dll `AE05F77C04`、json `8DED0BD7E3`、pck `0F492BFDEE`，
构建 / 游戏 / 工坊三处一致；changenote 已写、`mk-workshop-json.ps1` 已重跑（note 459 字符）。

⚠ 待办：剩下 13 种语言（kor / deu / fra / esp / spa / ita / pol / ptb / rus / tha / tur / ind）还没做；
韩语与俄语玩家基数较大，建议下一批先做 kor + rus。


### 追加：v0.2.329 —— 修联机断线：瓶中精灵复活未被 await

玩家反馈（经飞书，附「本地代码检查」结论）：
> `BossFairyInABottlePatch.Prefix` 在 `Hook.ShouldDie` 里 `TaskHelper.RunSafe(SaveAsync(...))` 后直接返回，
> `SaveAsync` 里的回血、清负面都是异步操作，但任务没有被死亡/出牌流程等待 ⇒ 清负面还没结束就生成了校验，
> 双方进度不同而失步。原版采用 ShouldDie 登记 preventer、再由死亡流程 `await AfterPreventingDeath`。

**确认成立**，并且找到了正确挂点。反编译 `CreatureCmd.KillWithoutCheckingWinCondition`：
```csharp
preventer = null;
if (force || creature.MaxHp <= 0 || Hook.ShouldDie(runState, combatState, creature, out preventer)) { …正常死亡… }
if (recursion >= 10) throw …;
await Hook.AfterDeath(…, wasRemovalPrevented: true, 0f);
await Hook.AfterPreventingDeath(runState, combatState, preventer, creature);   // ← 一定被 await
if (creature.IsDead) await KillWithoutCheckingWinCondition(creature, force, recursion + 1);
```
而 `Hook.AfterPreventingDeath` 本体是
`if (listeners.Contains(preventer)) { await preventer.AfterPreventingDeath(creature); preventer.InvokeExecutionFinished(); }`
（`preventer == null` 时什么都不做、也不会 NRE —— 这也解释了为什么老写法「能救命但没人等」）。

**修法**（`Patches/BossFairyInABottlePatch.cs`）：
- `Hook.ShouldDie` 前缀：只负责「判定不死」（`__result = false`），**不再** `TaskHelper.RunSafe`；
- 新增**顶层**补丁 `BossFairyAfterPreventingDeathPatch`（`Hook.AfterPreventingDeath` 前缀）：
  当 creature 是我们的 Boss 且栏位里还有瓶中精灵时，`__result = SaveAsync(...)` 并 `return false`
  ⇒ 丢药水 + `CreatureCmd.Heal(30%)` + 清全部负面（含灾厄）全部在**死亡流程的 await 之内**，两端同步。
- 保留 `SaveAsync` 里的「药水已被处理过就跳过」防重，日志加了「复活流程结束（…都在 await 之内）」一行。
- 补丁类特意做成**顶层类**（`Harmony.PatchAll()` 对本仓库其它补丁都是顶层，避免嵌套类的边界情况；
  `MusicTracePatch` 的注释里记过「PatchAll 中途抛异常会整套补丁挂不上」的教训）。

版本 **v0.2.329**；`content/` 没变 ⇒ **PCK 未重打**（`7CDF9A1F04`）。dll `9E389226C7`、json `9F435AFD48`，
构建 / 游戏 / 工坊三处一致；changenote 已写、`mk-workshop-json.ps1` 已重跑（note 388 字符）。

⚠ 仍需联机实测：三人或以上局里让炼药贼被打死触发瓶中精灵，两端日志都应出现
`瓶中精灵：丢弃药水 → 回复 …` 紧跟着 `复活流程结束（回血 + 清负面都在死亡流程的 await 之内）`，
之后不应再出现 different-sync/回滚。


### 追加：v0.2.328 —— 王者之拳全部降级 + 打包

用户：「有的回合的王者之拳仍是升级的（进阶9~10），把他们降级。然后打包，并给我上传创意工坊的命令」

- 唯一还在升级的那张是 **剧本第 9 回合**点名的 `C<KinglyPunch>(true)`（王者之拳+：10 伤、每抽到 +6）。
  改成 `C<KinglyPunch>()` ⇒ 进阶 9/10 的王者之拳统一 **8 伤 / +4**，与第 2、6 回合一致。
  （0.2.324 已把进阶 9 的「升级王者之拳」从 `AscensionUpgrades` 里删掉，所以这是最后一处。）
- **打包**：`.dist\CharacterBosses\` 里的三个文件换成当前构建（dll `72F64F6905`、json `B01F17E573`、
  pck `7CDF9A1F04`），`安装说明.txt` 更新成 v0.2.328（新增「自 v0.2.291 起的重要变更」段，
  旧的 v0.2.291 段落降级成「历史」），然后打成 **`.dist\CharacterBosses-v0.2.328.zip`**
  （13,183,932 B，内部结构 `CharacterBosses\{dll,json,pck,安装说明.txt}`，三个二进制与源 MD5 逐一核对一致）。
  ⚠ 打包时踩过一次坑：`CreateFromDirectory` 的目标写在源目录里会自锁、`includeBaseDirectory` 会把临时目录名
  也打进去 ⇒ 正确做法是「把 `CharacterBosses` 文件夹复制到临时目录，再以**该文件夹**为源 + includeBaseDirectory=true」。
- 上传命令见给用户的回复（`tools\upload-workshop.ps1`）。

版本 **v0.2.328**；`content/` 没变 ⇒ **PCK 未重打**（仍是 0.2.325 那次 `7CDF9A1F04`）。
changenote 已写、`mk-workshop-json.ps1` 已重跑（note 415 字符）。


### 追加：v0.2.327 —— 修「抱抱先生打两次」

用户：「第一回合受到了1点伤害2次。是不是效果重复了？」—— **是重复了**。
日志证据：`抱抱先生：她第 1 回合开始 → 对 1 名玩家各 1 点无来源伤害` 只有**一次**（我们那次），
但玩家挨了两下 ⇒ 另一下来自**原版** `MrStruggles.AfterPlayerTurnStart`。

根因：影子玩家的遗物确实在 `IterateHookListeners()` 里（和红面具 / 破损核心同一类坑），
「真人玩家回合开始」也会广播到她的遗物 ⇒ 原版按 `PlayerCombatState.TurnNumber` 打一次，
我们在**她的回合开始**（`BossHookBridge.FireTurnStart → MrStrugglesPlays`）又打一次。

修法：新增 `src/Hooks/Patches/ShadowMrStrugglesPatch.cs`（打法同 `ShadowRedMaskTurnStartPatch`，
打在**具体类型**上），owner 是我们的 Boss 生物时把原版那次换成 `Task.CompletedTask`；
真人玩家自己的抱抱先生完全不受影响。结算口径不变（她的回合数 / 所有真人玩家 / `Unpowered`）。

版本 **v0.2.327**；`content/` 没变 ⇒ **PCK 未重打**（`7CDF9A1F04`）。dll `3A709EEDE8`、
json `6A43941EB3`，构建 / 游戏 / 工坊三处一致（补丁类已在 dll 元数据里核对到）；
changenote 已写、`mk-workshop-json.ps1` 已重跑（note 301 字符）；设计记录追加 §16。


### 追加：v0.2.326 —— 抱抱先生意图：数字与位置

用户（截图里第一回合那颗写着 2）：「第一回合应该是1。再往左一点」

1. **数字**：`MrStrugglesPlays.UpcomingDamage` 去掉 `+1`，直接取 `boss.TurnNumber`。
   `PrepareTurn` 里 `TurnNumber++` 是在生成她**下一手**时做的，而玩家回合里手牌栏显示的就是那一手
   ⇒ 此刻 `TurnNumber` 已等于「她即将到来的回合」；再加 1 就多报 1（实测第一回合显示 2、实际打 1）。
2. **位置**：`PlaceLeftIntent` 的 x 从 `-(宽+间距)` 改成 `第一张卡意图行左缘 − (宽+间距)`
   （左缘 = `IntentShiftX × 卡宽` = −0.58 卡宽）。原来的位置会压到第一张卡自己的意图上。

版本 **v0.2.326**；`content/` 没变 ⇒ **PCK 未重打**（`7CDF9A1F04`）。dll `F3F09F7C4D`、
json `1EE944BCA6`，构建 / 游戏 / 工坊三处一致；changenote 已写、`mk-workshop-json.ps1` 已重跑（note 214 字符）；
设计记录追加 §15。


### 追加：v0.2.325 —— 王储：征召上前 + 抱抱先生

用户：「改一下，战火铸就改征召上前，进阶9改成升级征召上前并添加抱抱先生。确保抱抱先生只会攻击所有玩家，
伤害正确随回合成长，将该伤害在他意图的所有卡牌左边显示，并保证薄荷尖塔能收到这个意图。
加个警告：抱抱先生属于无来源伤害，……（后面和其它哪些一样）」

**剧本 / 进阶**：第 5 / 7 回合 `WroughtInWar`(战火铸就) → **`SummonForth`**(征召上前，1 费：把君王之剑放进手牌
+ 铸造 8)；能量不变（1 + 剑 2 = 3）。进阶 9 = `(9, SummonForth)` + 遗物 **`MrStruggles`**（抱抱先生）。
`BladesGrowth` 那条「凡带 `Forge` 变量的非能力牌都算进后面那张剑的意图」自动覆盖了它（8 / 升级 10）。

**抱抱先生**（新 `Core/MrStrugglesPlays.cs`）：反编译 `MrStruggles.AfterPlayerTurnStart` →
`CreatureCmd.Damage(ctx, combatState.HittableEnemies, Owner.PlayerCombatState.TurnNumber, Unpowered, Owner.Creature)`。
影子玩家不是「回合开始的那位真人玩家」⇒ 那条钩子从不替她触发 ⇒ 由 `BossHookBridge.FireTurnStart`
在**她的回合开始**手动结算：目标 = `BossLockOn.RealPlayers`（**只打所有玩家**，不含影子/随从，也不是只打锁定者）；
伤害 = `boss.TurnNumber`（**随回合成长**，与 `PlayerCombatState.TurnNumber` 对齐）；整张列表一次传 ⇒ 一次全体伤害。

**意图显示**（`UI/BossHandPanel.Extra.cs`）：新增 `_leftIntent`，同一套 `NIntent` + `RawAttackIntent`，
`PlaceLeftIntent` 把它摆在 **x = −(宽+间距)**、与其他意图同高 ⇒ **所有卡牌左边**。
数字 = `BossHandPanel.MrStrugglesDamage(boss)` = `MrStrugglesPlays.UpcomingDamage(boss)`
（玩家回合里 = `TurnNumber+1`，她自己行动时 = `TurnNumber`）。

**薄荷尖塔**：`MintySpireDamagePreviewCompat.PendingOf` 追加这一条（群攻，不做 `RandomAimIsElsewhere`），
与手牌栏同一个数。

**警告**：`REGENT_BOSS.conqueror` / `BOSS_CONQUEROR_POWER.description` 末尾加
「警告：抱抱先生属于无来源伤害，带有「为你而死」的召唤物不会替你承受该伤害。」

版本 **v0.2.325**；`content/` 变了 ⇒ **PCK 已重打**（`Packed 10 content file(s)` + `48 extra file(s)`，
12,874,248 B）。dll `0E43E07BD0`、json `0742B953F5`、pck `7CDF9A1F04`，构建 / 游戏 / 工坊三处一致；
changenote 已写、`mk-workshop-json.ps1` 已重跑（note 358 字符）；设计记录追加 §14。


### 追加：v0.2.324 —— 王储削弱：进阶 9 不再升级王者之拳

用户：「削弱王储，进阶9不再升级王者之踢」。

⚠ **事实核对**：进阶 9 的名单里一直是**王者之拳**（`KinglyPunch`）；**王者之踢从来没被升级过** ——
`mod_log.txt` 里进阶 10 的开局牌表就是 `KINGLY_PUNCH+, KINGLY_KICK, COSMIC_INDIFFERENCE+, WROUGHT_IN_WAR+`
（踢没有 `+`）。按「削弱王储」的意图，这次去掉 `(9, KinglyPunch)`：

| 回合 | 之前（进阶 9/10） | 现在（进阶 9/10） |
|---|---|---|
| 2 / 6 王者之拳 | 10 伤、每抽到 +6 | **8 伤、每抽到 +4** |
| 9 王者之拳+ | 剧本点名升级 | 未动（剧本写的就是 +） |

进阶 9 现在只剩「升级战火铸就」；进阶 8 仍为「升级宇宙冷漠 + 奥利哈钢」。
改动只有 `BossDefs.cs` 里 `REGENT_KING` 变体的 `AscensionUpgrades` 一行；
设计记录 `docs/plans/boss-king-regent.md` 追加 §13。

版本 **v0.2.324**；`content/` 没变 ⇒ **PCK 未重打**（`7E998B0C9C`）。dll `D3595E3EE7`、
json `C4A6029379`，构建 / 游戏 / 工坊三处一致；changenote 已写、`mk-workshop-json.ps1` 已重跑（note 297 字符）。


### 追加：v0.2.323 —— 王储：麻痹的文案 / 音效 / 图标

用户给了张游戏内 tooltip 截图（标题「麻痹」+ 黄色闪电 + 「你本回合打出的下一张攻击牌造成的伤害减少100%。」）：
> 征服者的描述改为：……所有玩家下一张攻击牌的伤害就减少50%。让玩家获得此debuff时和获得其它debuff
> 一样有音效并在玩家头顶短暂显示。这个debuff的图标应该是黄色的，把其说明写的简洁，大概如图。

- **征服者**（`REGENT_BOSS.conqueror` / `BOSS_CONQUEROR_POWER.description`）：
  「每当储君将一张牌放回抽牌堆顶部时，所有玩家下一张攻击牌的伤害就减少 50%。」
- **麻痹**（`PARALYSIS_POWER.description`）：简化成
  「你本回合打出的下一张攻击牌造成的伤害减少{Reduction}%。」——
  `{Reduction}` 由 `ParalysisPower.Description` 覆写里的 `description.Add("Reduction", …)` 填，
  值 = `50 × 层数` 封顶 100（写法同 `IroncladRagePower` 的 `{Loss}`；**不能**用 `{Amount}`，那是层数）。
- **音效**：之前我给这层 power 写了 `ShouldPlayVfx => false`。反编译 `NCreature` 的「获得 power」段
  只有 `if (power.ShouldPlayVfx) SfxCmd.Play(buff/debuff)` ⇒ 正是那个开关把减益音效关掉了。
  现在**删掉覆写**（基类默认 = `IsVisible`），音效与头顶提示都回来；
  头顶那个 `NPowerAppliedDebuffVfx` 本来就只看 `PowerCmd.Apply(..., silent: false)`（我们一直是 false）。
- **图标**：重新生成为**黄色**闪电（透明背景、128×128、居中、金黄渐变 + 深琥珀描边），
  Godot 重新导入（`.ctex` 文件名不变，`pck_extras.txt` 不用动）。

版本 **v0.2.323**；`content/` 变了 ⇒ **PCK 已重打**（`Packed 10 content file(s)` + `48 extra file(s)`，
12,873,800 B）。dll `6B4AD03212`、json `6DD46CD727`、pck `7E998B0C9C`，构建 / 游戏 / 工坊三处一致；
changenote 已写、`mk-workshop-json.ps1` 已重跑（note 260 字符）。


### 追加：v0.2.322 —— 「王储」改名 + 特殊效果换成「麻痹」

用户原话：
> 把王储的战斗从 REGENT_CONQUEROR_BOSS_ENCOUNTER 改为 REGENT_KING_BOSS_ENCOUNTER。其他相关，
> 比如王储的内部名称也跟着改。更改王储特殊效果：征服者：每当储君将一张牌放回抽牌堆顶部时，
> 所有玩家打出的下一张攻击牌的伤害减少 50%。增加 debuff：麻痹。效果：你下一张攻击牌的伤害减少 50%。
> 叠加时减少伤害减少幅度，最低 0%。2 层及以上的效果就是：你下一张攻击牌的伤害减少 100%。
> 玩家打出下一张攻击牌时该 debuff 失效。麻痹图标如图，你把背景上的黑扣掉然后把闪电居中，
> 和「下回合能量」buff 区分开。

**改名**（8 个源文件 + 本地化 + 文档）：`REGENT_CONQUEROR` → **`REGENT_KING`**、
`REGENT_CONQUEROR_BOSS_ENCOUNTER` → **`REGENT_KING_BOSS_ENCOUNTER`**、
`RegentConquerorBossEncounter` → `RegentKingBossEncounter`、
`Core/ConquerorRegentScript.cs` → `Core/KingRegentScript.cs`、
`BossLineups.ConquerorActive/ComputeConqueror` → `KingActive/ComputeKing`（锁定键 `REGENT_KING`）。
特殊效果**名字仍是「征服者」**（loc key `REGENT_BOSS.conqueror` / `BOSS_CONQUEROR_POWER`）。
设计记录 `docs/plans/boss-conqueror-regent.md` → `docs/plans/boss-king-regent.md`；
`docs/plans/console-boss-debug.md` 的 `fight` 命令已更新。

**新特殊效果**（`Powers/BossConquerorPower.cs` 重写）：她打出「返顶」牌
（`PHOTON_CUT` 光子切割 / `COSMIC_INDIFFERENCE` 宇宙冷漠，另留 `SHINING_STRIKE`/`GLIMMER` 备用）
⇒ 每个真人玩家 +1 层 `Powers/ParalysisPower`。
⚠ 判据按**卡**而不是按 `AfterCardChangedPiles`：弃牌堆洗回抽牌堆同样是「牌换堆」，
挂那儿她每回合洗牌都会白送麻痹。

**麻痹**（`Powers/ParalysisPower.cs`，新）：`ModifyDamageMultiplicative` 返回
`1 − min(1, 0.5 × 层数)`（只认「他自己打出的攻击牌」：`cardSource.Type == Attack` 且 owner 是他的 Creature），
`AfterCardPlayed` 里他打出攻击牌后 `PowerCmd.Remove(this)`。`StackType.Counter`、
`ShouldScaleInMultiplayer = false`。旧的 `BossConquerorStrengthUpPower`（独白那半）已删除。

**图标**：`res://CharacterBosses/images/powers/paralysis.png` —— 128×128、透明背景、闪电居中、
**红色系**（用户给的图是黄闪电，为和「下回合能量」区分开做成红色 + 深红描边）。
源图 `tools/art/import_proj/CharacterBosses/images/powers/paralysis.png`，用
`Godot --headless --path tools\art\import_proj --import` 生成 `.import` 与
`.godot/imported/paralysis.png-5a191af8….ctex`，三条路径已追加进 `tools/art/pck_extras.txt`
（`Packed 48 extra file(s)`）。图标由 `Patches/BossSpecialPowerIconPatch` 指过去。

版本 **v0.2.322**；`content/` 变了 ⇒ **PCK 已重打**（`Packed 10 content file(s)` + `48 extra file(s)`，
12,874,312 B）。dll `334D2BAD15`、json `502E377A35`、pck `DA35660E5A`，构建 / 游戏 / 工坊三处一致；
changenote 已写、`mk-workshop-json.ps1` 已重跑（note 405 字符）。

⚠ 待实机确认：麻痹减伤是否真的作用在玩家下一张攻击牌上（日志 `麻痹：… 吃 N 层 ⇒ 伤害 ×0.5（−50%）` /
`麻痹：… 打出了攻击牌 X ⇒ 麻痹失效`）；红闪电图标在状态栏上的观感。


### 追加：v0.2.321 —— 王储微调 + 杂耍战削弱

用户原话：
> 王储：升级封印王座，降级王之凝视（没什么实际影响）。
> 削弱杂耍战：奥利哈钢换折扇 / 第二回合改成：突破，打击，打击（惊逃打出），打击（杂耍复制，虚无被烧）/
> 第三回合改成：打击，打击，打击，打击（杂耍复制，惊逃打出）/ 第四回合改成：防御，防御，痛击（惊逃）/
> 第七回合改成：坚毅+（烧彼岸咆哮）、邪眼（印本回合有消耗牌获得额外格挡）、彼岸咆哮（被烧自动打出）/
> 进阶8改升级无惧疼痛、邪眼。进阶9改升级完美打击、彼岸咆哮

**征服者储君**：第 1 回合 `封印王座` → **封印王座+**（反编译 `TheSealedThrone.OnUpgrade →
EnergyCost.UpgradeBy(-1)` ⇒ **0 费**，效果不变），`王之凝视+` → **王之凝视**（2 费、`StrengthLoss` 仍 1）
⇒ 两费之和仍是 2，与她基础能量一致，强度确实不变（用户自己也是这么说的）。

**杂耍战**（`Core/JugglerIroncladScript.cs` + `BossDefs` 的变体进阶表）：
- 遗物 奥利哈钢 → **精致折扇** `OrnamentalFan`（每 3 张攻击 +4 格挡；原版遗物自己数，影子玩家收得到，
  同手里剑/苦无，见 `Core/SilentScript.cs` 注释 ⇒ 不需要桥接）。
- 第 2 回合：**突破** `Breakthrough`（1 费、全体 9 伤、自己 −1 HP）替换「预备打击」⇒ 少 3 点临时力量。
- 第 3 回合：**打击 ×4**（前三张自己打 = 3 费，第四张杂耍复制由惊逃打出）；踩踏下线。
- 第 4 回合：**防御、防御、痛击（惊逃）**，去掉原来那张打击。
- 第 7 回合：**坚毅+（烧彼岸咆哮）、邪眼 `EvilEye`、彼岸咆哮（回合结束自动打出）**，去掉原来那张防御。
  ⚠ 邪眼「本回合消耗过牌再 +8」在意图里只画 8（判定发生在真正打出时）⇒ 实战 16。
- 进阶 8：无惧疼痛 + **邪眼**（原为坚毅）；进阶 9：**完美打击** + 彼岸咆哮（原为预备打击）。
- 卡组：`SetupStrike`/`Stomp` 下线，换入 `Breakthrough`/`EvilEye`。

版本 **v0.2.321**；`content/` 没变 ⇒ **PCK 未重打**（`148278314B`）。dll `92F24CFE13`、
json `8B4D440BC8`，构建 / 游戏 / 工坊三处一致；changenote 已写、`mk-workshop-json.ps1` 已重跑（note 501 字符）。
`docs/plans/boss-juggler.md`、`docs/plans/boss-conqueror-regent.md` 都已补记。


### 追加：v0.2.320 —— 征服者储君削弱：何人僭越 → 笨拙

用户一句话：「削弱一下，何人僭越改笨拙」。
第 5 回合那一格从「何人僭越」（`KnowThyPlace`，0 费：给玩家 1 层虚弱 + 1 层易伤）换成
诅咒「笨拙」（`Clumsy`，反编译 `CanonicalKeywords = Unplayable + Ethereal` —— 打不出、回合结束自己消耗）。
⇒ 这一回合不再给玩家上虚弱与易伤，手里还多一张死牌。卡组同步成「笨拙 ×2」（第 1 回合那张不变）。
⚠ 第 5 回合**不在** 7～10 的循环里 ⇒ 这是**一次性**削弱；如果之后还觉得强，动循环里的第 8/10 回合更有效。

版本 **v0.2.320**；`content/` 没变 ⇒ **PCK 未重打**（`148278314B`）。dll `A683C65DBE`、
json `9273A74B05`，构建 / 游戏 / 工坊三处一致；changenote 已写、`mk-workshop-json.ps1` 已重跑（note 182 字符）。
设计记录 `docs/plans/boss-conqueror-regent.md` 追加 §9。


### 追加：v0.2.319 —— 征服者储君：浮空剑、铸造意图、王者之拳成长

用户实机三条（`mod_log.txt` 20:00–20:04 那一场，`REGENT_CONQUEROR` 判定 True、两个特殊效果都触发过）：

1. **「他没有击剑指南…不应该在战斗开始时就把剑给铸造出来飘在他身边」**
   日志证据：`战斗开始：铸出浮空君王之剑（原版 PlayCombatRoomForgeVfx，伤害 10）`。
   根因：`ConquerorRegentScript.Deck()` 里写了 `SovereignBlade` ⇒ 开局牌堆里就有那张未铸造的剑牌，
   `UI/BossSovereignBlade.EnsureVfxExists` 每帧扫到就补 VFX。
   修法：① **卡组不再带这张牌**；② `EnsureVfxExists` 加闸 —— 只有「已铸过」（`Damage > 10`，
   反编译 `SovereignBlade._baseDamage = 10`）的剑才补 VFX。于是第 5 回合打「战火铸就」时由原版
   `ForgeCmd.Forge` 现造（`blades.Count == 0` → `CreateCard` + 进手牌 + `AddDamage` +
   `PlayCombatRoomForgeVfx`）⇒ 剑在那一刻铸出来。铸剑储君有击剑指南、开局就铸过 ⇒ 行为不变。
2. **「君王之剑的伤害意图没有计算本回合战火铸就会对它的铸造」**
   日志证据：规划期 `SOVEREIGN_BLADE(补=0+铸0+段0)`（出牌后重算才是对的）。
   根因：`Core/BladesGrowth.cs` 的 `bladePendingForge` 只认 `SEEKING_EDGE`。
   修法：凡带 `Forge` 变量的**非能力牌**都算（追踪之刃 7、**战火铸就 7**）；能力牌仍走
   `PendingFurnaceForge`（熔炉是回合开始铸造，否则重复）。
3. **「王者之拳没有在每次抽到时增加伤害」**
   根因：原版 `KinglyPunch.AfterCardDrawn`（`card == this` → `Damage += Increase`），
   我们每回合新建实例 ⇒ 抽牌钩子不触发。
   修法：新增 `CharBossMonster.KinglyPunchBonus`；`BuildHand` 造这张牌时**先把这次的 `Increase`
   累计进去、再把累计值加到卡面**（＝原版「抽到那一刻就涨」，第一手就已经涨过一次）。
   未升级 +4 / 升级 +6（`OnUpgrade` 两处都 +2）。日志：`王者之拳成长：这次 +N ⇒ 累计 +M`。

版本 **v0.2.319**；`content/` 没变 ⇒ **PCK 未重打**（`148278314B`）。dll `7196723D70`、
json `7BAD3B335D`，构建 / 游戏 / 工坊三处一致；changenote 已写、`mk-workshop-json.ps1` 已重跑（note 367 字符）。
设计记录 `docs/plans/boss-conqueror-regent.md` 追加了 §8 记这三条。

**用户拍板「与原版相同，第一次抽到就该涨」** ⇒ 王者之拳的成长顺序就是本版写的那样（先累计 Increase、
再写到卡面），**没有改行为**；只把那处注释改对了，并重新构建安装了一次（同一版本号 v0.2.319，
dll 换成 `EA14B160C0`）。数值：未升级 第 2/6/9 回合 = 12 / 16 / 24，进阶 9 起 = 16 / 22 / 28。


### 追加：v0.2.318 —— 征服者储君第 1 回合改成 2 费

用户：「王之凝视+是1费」——反编译确认 `MonarchsGaze.OnUpgrade` → `EnergyCost.UpgradeBy(-1)`（2 → 1）
⇒ 第 1 回合 封印王座 1 + 王之凝视+ 1 = **2**，正好是她基础的能量（角色 3 − 涅奥 1），
v0.2.317 里「这一回合多给 1 费」的写法撤掉。第 1 回合的牌没变（封印王座、王之凝视+、笨拙）。
`docs/plans/boss-conqueror-regent.md` §3 的能量表与 §7 的待确认项同步更新。

版本 **v0.2.318**；`content/` 没变 ⇒ **PCK 未重打**（仍是 `148278314B`，12,867,532 B）。
dll `B827590406`、json `F325E05E88`，构建 / 游戏 / 工坊三处一致；
`workshop\changenote.txt` 已写、`mk-workshop-json.ps1` 已重跑（note 166 字符）。

### 追加：v0.2.317 —— 新增二层变体 Boss「征服者储君」

用户设计稿原文见 `docs/plans/boss-conqueror-regent.md` §1（本会话逐条落实）。这是二层储君的
**第二套设计**，与「放血战 / 杂耍战」「无色储君 / 辉星储」同一套机制：**以房间遭遇为准**
（`REGENT_CONQUEROR_BOSS_ENCOUNTER`），拿不到遭遇才按种子 50/50（salt 54）。

**特殊效果「征服者」**（`Powers/BossConquerorPower.cs`）分两半：
- **前半**（`AfterCardPlayed`）：她打出**名字里含「王」**的牌 → 每个真人玩家施加原版
  `MonarchsGazeStrengthDownPower` 1 层（＝「王之凝视」的**临时**减力量，玩家回合结束还回去）。
  判定用两把尺子：当前语言标题含「王」**或** entry 属于
  {THE_SEALED_THRONE, MONARCHS_GAZE, KINGLY_PUNCH, KINGLY_KICK, SOVEREIGN_BLADE, ROYALTIES, ROYAL_GAMBLE}
  （英文下标题里当然没有「王」）。
- **后半**（`AfterDamageGiven` + `DamageResult.WasFullyBlocked`）：她的攻击被完全格挡 →
  给每个真人玩家施加 `Powers/BossConquerorStrengthUpPower`（`TemporaryStrengthPower` 的**正向**分支）。
  用户 PS「后者用独白」⇒ 图标借 `MonologuePower`、标题/描述写成本模组 key 但值是「独白」/「本回合获得 N 点力量。」
  （**没**沿用 `TEMPORARY_STRENGTH_POWER.description`：原版没有正向子类，那个 key 很可能不存在）。

**新机制（可复用）**：
1. `ScriptCard.CostThisTurn` —— 本回合这张牌的**真实费用**（造牌时 `EnergyCost.AddThisCombat(-n)`，
   卡面/规划/扣费三者一致）。王者之踢原版「每抽到 −1 费」在我们的「每回合新建实例」模型下不会发生
   ⇒ 逐回合写死 3（第 2 回合留在手里）/ 2 / 1 / 0 / 0。
2. `BossTurn.PickOrder` —— **选择队列**：按顺序钉死每一次「让 Boss 选牌」。
   第 2 回合 `[王者之踢]`（光子切割+ 返顶）；第 5 回合 `[宇宙冷漠, 王者之踢, 王者之拳, 所向无敌]`
   （第一个是抉择抉择要打出的技能牌，后三个是宇宙冷漠被 3 次打出时各自的返顶目标）；
   第 10 回合 `[王者之踢, 所向无敌]`。实现在 `BossCardSelector.TakeForcedPick`（游标按 (Boss,回合) 记，
   命中才 +1）。
3. 「拦掉抉择抉择和光子切割+的抽牌」⇒ `BossDefs.Regent.KeepsScriptedHandByAct = {false,true,false,false}`
   （抽牌照常发生、抽进来的牌挪回抽牌堆顶；不用 `BlocksVanillaDraw`，那是整只 Boss 级开关）。
4. `BossDefs.Regent.FireBossRelicHooks = true` —— 进阶 8 的遗物「奥利哈钢」需要
   `BossRelicHookBridge` 补跑「回合结束无格挡 +6」；储君另外三幕本来没有战斗内触发遗物，空操作。

**其余落点**：`BossKeys`（Key/二层候补/互补变体/遭遇映射）、`BossLineups.ConquerorActive`、
`TierBossEncounters.RegentConquerorBossEncounter`（`TierAct = 1`）、`CharBossEncounters`
（`TierEncounters` + `TierEncounterFor(4,1)`）、`BossBgm`（二层统一帝皇蟹）、
`DownfallCollectibleCompat`（藏品给「征服者 `CONQUEROR`」）、
`BossScriptPlanner`（把**任何带 `Forge` 变量的牌**算进后面那张君王之剑的意图 —— 战火铸就 7）、
4 个本地化文件（zhs/eng × monsters/powers/encounters）。

⚠ **第 1 回合的 3 费还没跟用户确认**：封印王座(1) + 王之凝视+(2) = 3，而她的基础只有 2
（角色 3 − 涅奥 1），本变体没有任何加费遗物 ⇒ 这一回合由剧本多给 1 费。
若用户要「第 1 回合只打封印王座」，把 `ConquerorRegentScript` 第 1 回合的 `Energy` 改 2 即可。

版本 **v0.2.317**；`content/` 变了 ⇒ **PCK 已重打**（`Packed 10 content file(s)` + `45 extra file(s)`，
12,867,532 B）。dll `E1646B9644`、json `0D229F4425`、pck `148278314B`，构建 / 游戏 / 工坊三处 MD5 一致；
`workshop\changenote.txt` 已写、`workshop\description.txt` 的 Boss 计数改成 **20 个（5/7/8）**、
`mk-workshop-json.ps1` 已重跑（note 506 字符）。

### 追加：v0.2.316 —— 完美打击只剩 18；燃烧契约描述改成玩家口径

用户（截图：完美打击 `造成 18 点伤害。你每有一张…伤害 +2。`）：
> 「完美打伤害怎么只剩 18 了，不应该是 20 吗？还有那个特殊效果描述改成：
> 铁甲战士通过杂耍生成的牌带有「虚无」和「消耗」。」

**18 是我 v0.2.315「清旧牌」的副作用**：`PurgeStaleStrikeCards` 把上一批「打击」旧实例清掉后，
牌堆里 **只剩零星几张**（手牌重建 / 弃牌堆时序问题），`AllCards` 里带 Strike 标签的牌不足 7 张
⇒ 原版公式 `6 + 2 × count` 自然掉到 18。

**改成「只接管一次、不动牌堆」**：重写并重新启用 `BossScriptRunner.TakeOverPerfectedStrike(card, boss)`：
- `count = deckStrikes + boss.StrikeCopiesMade`（卡组 6 张 + 杂耍真正生成的复制牌数）；
- `baseVal / perCard` 读 `DynamicVars["CalculationBase"/"ExtraDamage"].BaseValue`（6 / 2，升级 3）；
- `live = CountStrikeCardsInPiles(boss)`（此刻牌堆里真实带 Strike 标签的张数）；
- 只改 **`CalculationBase = max(0, target - perCard × live)`**，`ExtraDamage` **保持原版**
  ⇒ 卡面仍显示「每有一张打击 +2」，实际伤害 = `6 + 2 × (6 + 复制数)`。
第六回合（循环起点）那一下 = **20**，易伤 + 力量后 = **33**。

**燃烧契约描述**（四处本地化：zhs `monsters.pact` / zhs `powers` description / eng 同名两处）：
- zhs：「燃烧契约\n铁甲战士通过杂耍生成的牌带有「虚无」和「消耗」。」
- eng：「Burning Pact\nCards the Ironclad creates through Juggling have Ethereal and Exhaust.」

版本 **v0.2.316**；`content/` 变了 ⇒ **PCK 已重打**（`Packed 10 content file(s)` + `45 extra file(s)`，
12,865,836 B）。dll `279649514B2D`、json `00DCC5269D4A`、pck `6E66342AA93B`，
构建 / 游戏 / 工坊三处 MD5 一致；`workshop\changenote.txt` 已写、`mk-workshop-json.ps1` 已重跑（note 269 字符）。
`PurgeStaleStrikeCards` 保留但已不参与完美打击结算（接管生效时以接管值为准）。

### 追加：v0.2.315 —— 完美打击被改成「每有 1 张打击伤害 +0」

用户（卡面截图：`造成 33 点伤害。你每有一张名字中含有「打击」的牌，伤害 +0。`）：
> 「完美打怎么被你改成每有 1 张打击伤害加 0 了？这样伤害就偏低了」

**是我 v0.2.305 的接管方式有问题**：我把 `ExtraDamage` 清零、把「基础 + 每张 × 张数」整个写进
`CalculationBase` ⇒ 卡面那句「每有一张…伤害 +N」自然变成 **+0**，而且**不再随牌堆成长**。

**改回治本**：不动卡面数值，改**牌堆计数**：
- 新增 `CharBossMonster.StrikeCopyInstances`（记录「杂耍真正生成的复制牌」实例）；
- `BossScriptRunner.PurgeStaleStrikeCards(pcs, boss)`：每回合 `BuildHand` 重建手牌时，
  把**带 `Strike` 标签、但不是复制牌**的旧实例从手牌/抽牌/弃牌/消耗牌堆里清掉
  ⇒ `AllCards` 里始终是「这套牌的 6 张」+「每循环 +1 的复制牌」；
- 撤掉 `TakeOverPerfectedStrike` 的调用（方法留着但不再调）。
⚠ 只清 Strike 标签的牌：「彼岸咆哮」要靠**消耗牌堆**参与回合结束自动打出，不能被清。

于是完美打击：`6 + 2/3 × 7`、每个循环 +2/3，卡面文案与数值都回到原版。

版本 **v0.2.315**；`content/` 没变 ⇒ PCK 未重打。dll `0A2D3D62A736`、json `B6274F2554E4`，
构建 / 游戏 / 工坊三处一致；`workshop\changenote.txt` 已写、`mk-workshop-json.ps1` 已重跑（note 298 字符）。

### 追加：v0.2.314 —— 奥利哈钢与「虚无消耗」的先后

用户：
> 「第二回合打击因为虚无在他回合结束时消耗然后因为无惧疼痛起甲是和奥利哈钢的起甲时机是相同的，
> 所以那一回合他应该会获得 6+3 或 4 点格挡（第三回合因为是惊逃打出消耗所以在奥利哈钢之前，维持现状）」

**原来的次序错了**：`CharBossMonster` 回合结束是
```
惊逃 → **虚无消耗 + 无惧疼痛（+3/4）** → 所向无敌/彼岸咆哮 → **奥利哈钢（若格挡=0 则 +6）** → 丢手牌
```
⇒ 第二回合「虚无消耗」先给了 3 点格挡，奥利哈钢那条 `if (me.Block > 0) return;` 直接跳过 ⇒ **只拿到 3**。

**改成**：
```
惊逃 → 所向无敌/彼岸咆哮 → **奥利哈钢（+6，此时格挡仍为 0）** → **虚无消耗 + 无惧疼痛（+3/4）** → 丢手牌
```
⇒ 第二回合 = **6 + 3（进阶 8 后 4）= 9 / 10 点格挡**。
第三回合那张踩踏是被「惊逃」**打出**时消耗的（发生在她出牌阶段）⇒ 天然早于奥利哈钢，
那时她已经因为无惧疼痛有格挡 ⇒ 奥利哈钢不给 6 ⇒ **维持现状**。

版本 **v0.2.314**；`content/` 没变 ⇒ PCK 未重打。dll `A709C305C76B`、json `5E32670B91F4`，
构建 / 游戏 / 工坊三处一致；`workshop\changenote.txt` 已写、`mk-workshop-json.ps1` 已重跑（note 274 字符）。

### 追加：v0.2.313 —— 惊逃那张又没意图（分支顺序问题）

用户：「第三回合惊逃打的踩踏又不显示攻击意图了」。

**根因**：`JugglerIroncladScript.Copy<Stomp>(playedByStampede: true)` 同时给了三个标记
（`HoldInstead` + `JugglingCopy` + `StampedePlay`），而 `BossScriptPlanner` 里的分支顺序是

```
IsPlayable → HoldInstead（保留在手，continue） → DisplayOnly → … → 正常规划
```

⇒ 它被「保留在手」那条**先吃掉**，永远走不到 v0.2.307 给它准备的「0 费计划牌」路径
⇒ 既不进 `Plan`、也没有 `DisplayPlays` 意图。

**修法（两处，互相兜底）**：
1. 剧本 helper：`HoldInstead = !playedByStampede` —— 会被惊逃打出的那张不再标「保留在手」；
2. 规划器：`if (entry.HoldInstead && !stampedeOnly)` —— 以后就算两个都标，也以 `StampedePlay` 为准。

版本 **v0.2.313**；`content/` 没变 ⇒ PCK 未重打。dll `7DE7C7FCC8C8`、json `82F337897D69`，
构建 / 游戏 / 工坊三处一致；`workshop\changenote.txt` 已写、`mk-workshop-json.ps1` 已重跑（note 241 字符）。

## 2026-11-02：v0.2.312 —— 「有格挡时血条应该是银蓝色」

用户（截图：二层杂耍战 HP 200/200、格挡 24，血条却是**红色**；追问后澄清
「拥有格挡时，boss 的血条应该是和格挡一样的银蓝色的」）。

**先自查「我瞎改哪了」**：本模组**没有**任何贴图覆盖 ——
PCK 的 extras 全部落在 `res://CharacterBosses/...`（地图图标 / 锁定图标 / 一代 BGM），
不碰原版路径；我们改过的图标面只有 `PowerModel.PackedIconPath`（power 图标）、
地图/顶栏遭遇图标、`SacrificeIntent` 的箭头。**血条配色不在其中。**

**真正原因**（反编译 `NHealthBar.RefreshBlockUi`）：
```csharp
if (_creature.Block <= 0 && (tracked == null || tracked.Block <= 0)) {
    _blockContainer.Visible = false;
    _hpForeground.SelfModulate = _redForegroundColor;   // 没格挡 → 红
    return;
}
_hpForeground.SelfModulate = _blockHpForegroundColor;    // 有格挡 → 银蓝 #3B6FA3
```
这件事**完全靠 `RefreshBlockUi` 被调**，而触发它的那条链路只跟「本机玩家的格挡变化」走；
我们的 Boss 是**影子玩家**（不在 `CombatState.Players`）⇒ 收不到：
- 战斗开始那 20 点（钻石头冠）加上去时，血条节点都还没绑定；
- 之后的格挡（护喉甲 / 奥利哈钢 / 无惧疼痛…）也不走那条 UI 链路。
⇒ 数字有、颜色不变。

**修法**：新文件 `Core/BossBlockBarTint.cs`，在 `BossFrameTick` 的 everyBoss 循环里
（4Hz 级别的车道）调 `Enforce(boss)`：
- 拿到 `NCreature._healthBar`（反射，和 `BossOsty` 同一套路）；
- 读 `NHealthBar._hpForeground` 的 `SelfModulate`，**已经是银蓝就不动**（避免每帧重绘）；
- 否则反射调 `RefreshBlockUi()` + `RefreshText()`。
日志：`血条配色：<KEY> 有 N 点格挡，血条前景 (r,g,b,1) → (0.23,0.44,0.64,1)（银蓝 = 有格挡）`。

版本 **v0.2.312**；`content/` 没变 ⇒ PCK 未重打。dll `5EC05796C2B3`、json `0EB50B764DD0`，
构建 / 游戏 / 工坊三处一致；`workshop\changenote.txt` 已写、`mk-workshop-json.ps1` 已重跑。

## 2026-11-02：v0.2.311 —— 「不同步摘要后面没了」是我们自己截断的

用户贴出表里一条 diag（`IRONCLAD_ANGER_BOSS_ENCOUNTER`，轮次 4，两名玩家锁定图标 0 / 2 个，
然后 `Boss NECROBINDER_BOSS：HP 97/` 就断了）并问：「是不是一次只能上传这么多」。

**不是飞书的限制，是我们自己的截断**：`Telemetry.Diagnostic` 里
```csharp
["line"] = message.Length > 300 ? message.Substring(0, 300) : message
```
而「不同步状态摘要」是 `Patches/DivergenceStateProbe` 拼的**多行**文本
（轮次 / 遭遇 / 每个真人玩家 / 每只 Boss 的 power+手牌+计划 / 锁定分配表），300 字写不到一半。

**修法**：新增 `ClipDiagLine(message)`
- 含「不同步」⇒ 上限 **1500 字**（`DiagLineLimitDesync`）；
- 其它 diag 仍 300 字。
两处写表的地方都换成它。

版本 **v0.2.311**；`content/` 没变 ⇒ PCK 未重打。dll `94C033843673`、json `D36C236ACB36`，
构建 / 游戏 / 工坊三处一致；`workshop\changenote.txt` 已写、`mk-workshop-json.ps1` 已重跑。

**顺带记下那条摘要里暴露的真问题（还没查）**：
```
玩家 …87209：锁定图标 0 个；HP 0/80 格挡 0
玩家 …45874：锁定图标 2 个 [NECROBINDER_BOSS,IRONCLAD_BOSS]；HP 59/66 格挡 52
```
「一个玩家两个锁定图标、另一个零个」正是我们 2026-10-29 修过的那类
（`BossLockOn.ReconcilePowers` 该收敛成「每人恰好一个、名字含所有锁他的 Boss」）。
⚠ 这条是**房主端**的摘要，只有两端对不上才会被记为不同步；等用户下次复现时
把完整摘要（现在 1500 字够了）发来，就能看出两端差在哪一项。

### 追加：v0.2.310 —— 玩家原话 & 两侧兜底

玩家原话（用户转述）：
> 「还有，打**炼药贼**的时候用**倾泻**打**巨石**疑似只打自己。我战士玩的少，倾泻应该不会打自己的吧」
> 以及用户补充：「巨石战不会给玩家塞巨石……」

所以在 v0.2.309 的基础上把兜底做成**两侧通用**（不再假设「一定是玩家的牌」）：

`Patches/AutoPlayTargetForRealPlayerPatch.cs`（`CardCmd.AutoPlay` 前缀，只在 `target == null`
且这张牌 `TargetType.AnyEnemy` 时动手）：
- 牌的主人是**真人玩家** ⇒ 补「Boss 侧」第一个活着的（`Combat.Enemies` → 退 `HittableEnemies`）；
- 牌的主人是**我们的 Boss**（`CharBossMonster`）⇒ 补「玩家侧」第一个活着的
  （优先 `BossLockOn.TargetFor` 锁定那位，和跟踪锁定同口径）；
- 其它（原版怪物 / 别的模组）⇒ **不动**。

根因仍是同一句反编译结论：原版给 null 目标找靶的条件是
`HittableEnemies` 里 `c.IsPlayer && c != card.Owner.Creature` —— **只对怪物驾驶的牌成立**，
真人玩家的攻击牌在这个列表里挑不到人 ⇒ 落回自己。

⚠ 「巨石」到底是谁塞给玩家的：本模组只在**巨石战自己的剧本**里用 `GiantRock`（`C<GiantRock>`），
没有任何地方把它塞进玩家牌堆。所以这张牌在玩家手上出现的原因还没定位
（可能是别的模组/崩坠的赠牌，或玩家记混了牌名）；但**只要它是「玩家自动打出且没给目标」，
现在两侧都会被兜底**，砸自己这件事不会再发生。

版本 **v0.2.310**；`content/` 没变 ⇒ PCK 未重打。dll `83D64BDEA2EE`、json `984343F37FA0`，
构建 / 游戏 / 工坊三处一致；`workshop\changenote.txt` 已写、`mk-workshop-json.ps1` 已重跑。

**待复验**：玩家打倾泻 → 自动打出的攻击牌砸向 Boss；日志出现
`自动出牌没给目标：<牌>（拥有者 <谁>）⇒ 补成 Boss 侧 <谁>`。

### 追加：v0.2.309 —— 用户澄清：是**玩家**打倾泻，巨石砸自己

用户：「不是，是**玩家**打倾泻，倾泻的巨石打到玩家自己了」。
⇒ v0.2.308 修的是「她的牌没翻侧」，方向错了（那条留着也无害）。

**真实链路**：
1. 玩家打「倾泻」（`Cascade`）→ `CardPileCmd.AutoPlayFromDrawPile(ctx, 玩家, N, Top, false)`
   —— 从**玩家的摸牌堆顶**把牌自动打出去；
2. 牌堆顶那张是「**巨石**」（`GiantRock`，1 费攻击、`TargetType.AnyEnemy`）——
   它是**巨石战塞进玩家牌堆的衍生牌**（所以只有本模组在场时才会撞上）；
3. 自动出牌走 `CardCmd.AutoPlay(ctx, card, **target: null**)`，
   而原版给 null 目标找靶的那段过滤是
   `HittableEnemies` 里 `c.IsPlayer && c != card.Owner.Creature`
   —— **这个条件只对「怪物驾驶的牌」成立**（怪物视角下「敌人」= 真人玩家）。
   玩家自己的攻击牌在 `HittableEnemies`（= 敌方阵营）里根本找不到 `IsPlayer` 的候选
   ⇒ 挑不到人 ⇒ 落回**自己**。

**结论**：这不是「翻侧泄漏」，而是**原版对「玩家自动出牌 + 需要目标」没有兜底**；
巨石是我们塞给玩家的牌，所以只有我们没有别人会撞到。

**修法**：新文件 `Patches/AutoPlayTargetForRealPlayerPatch.cs` ——
`CardCmd.AutoPlay` 前缀，当「目标为 null + 拥有者是真人玩家 + 这张牌 `TargetType.AnyEnemy`」时，
替它挑一个**活着的敌方（Boss 侧）目标**（`Combat.Enemies` 里第一个活着的，拿不到再退回
`HittableEnemies`）。她自己（影子玩家）的牌不在这里处理，仍走 v0.2.308 的翻侧逻辑。

版本 **v0.2.309**；`content/` 没变 ⇒ PCK 未重打。dll `B39A061F129C`、json `988B3B88171D`，
构建 / 游戏 / 工坊三处一致；`workshop\changenote.txt` 已写、`mk-workshop-json.ps1` 已重跑。

**待复验**：玩家打倾泻把巨石打出来 ⇒ 巨石应砸向 Boss；日志出现
`玩家自动出牌：GIANT_ROCK 原本没有目标 ⇒ 补成敌方阵营 XXX`。

## 2026-11-02：v0.2.308 —— 「倾泻打出巨石会打自己」（与地狱狂徒同一类）

用户：「有玩家反馈倾泻打出巨石会打自己，你看看和之前的地狱狂徒是不是一类问题？」

**是同一类，只是方向相反。** 反编译核对：

- **倾泻 `Cascade`**（铁甲战士，1 费技能）：
  `CardPileCmd.AutoPlayFromDrawPile(ctx, Owner, n, CardPilePosition.Top, forceExhaust:false)`
  —— **从摸牌堆顶自动打出 N 张**；
- **巨石 `GiantRock`**（衍生 token，1 费攻击，`TargetType.AnyEnemy`）：
  `OnPlay` 直接打 `cardPlay.Target`，而 `AutoPlay(…, target: null)` 的目标是从
  **`HittableEnemies`** 里挑的（和地狱狂徒完全同一个机制）。

历史对照：

| 事件 | 谁在自动出牌 | 问题方向 |
|---|---|---|
| 地狱狂徒 + 百年积木（v0.2.293/294） | **玩家**的牌 | 被翻成「玩家侧」⇒ 玩家自己的打击打到自己 |
| 倾泻 + 巨石（本次） | **她的**牌 | 没被翻成「玩家侧」⇒ 她按敌方阵营选靶 ⇒ 打她自己 |

**根因**：翻侧判据是「`CurrentSide == CombatSide.Enemy`」，而 `AutoPlayFromDrawPile` 是
**自己 await 的一段链**，可能在她回合边界之后才真正选靶 ⇒ 那一刻 `CurrentSide` 已经是 Player
⇒ 判据不成立 ⇒ 目标按敌方阵营（= 她自己）解析。

**修法（两处）**：
1. `HittableEnemiesSidePatch` 新增 `ForceRelativeHold`（**计数**，不是 bool，防嵌套/并发被提前清掉）；
   新文件 `Patches/AutoPlayFromDrawPileSideHoldPatch.cs`：
   给她这条入口 `Prefix` 时 `++`，`Postfix` 拿到的 `Task` 挂 `ContinueWith` 收尾再 `--`
   ⇒ **整条自动出牌链**都按玩家侧解析；
2. 翻侧判据放宽：`BossActingDepth > 0`（她在 `PlayOne` 的 AutoPlay 窗口里）或
   `ForceRelativeHold > 0` 时**也翻**，不再只看 `CurrentSide`。
   ②b 那条「没有角色 Boss 就跳过」同样认这两个窗口。

⚠ 安全性：这两条只会**多翻**「她正在出手」的那几个窗口（本来就应该翻），
玩家回合依然不翻（`BossActingDepth`、`ForceRelativeHold` 都是 0）。

版本 **v0.2.308**；`content/` 没变 ⇒ PCK 未重打。dll `3A0C1B2E2529`、json `F7CC2370C990`，
构建 / 游戏 / 工坊三处一致；`workshop\changenote.txt` 已写、`mk-workshop-json.ps1` 已重跑。

### 追加：v0.2.307 —— 惊逃的预览必须和「直接打牌」共用一条路

用户（附录像 `屏幕录制 2026-11-02 173234.mp4`）：
> 「你就不能让惊逃的伤害预览计算逻辑和直接打牌一样吗？你这个还是没修好，
> 而且直接打出伤害预览会在力量等加成到账之后就将预览里补的伤害撤回去，惊逃直接叠加起来了。」

**用户说到了点子上**。此前的差别：

| | 直接打出的牌 | 惊逃打出的那张 |
|---|---|---|
| 在哪 | `boss.Plan`（`PlannedPlay`） | `boss.DisplayPlays`（只用来画意图） |
| 每打一张牌之后 | `BladesGrowth.RecomputeAll(planTime:false)` **重算**一遍 → 力量真到账后用实时力量、预补归 0 ⇒「撤回」 | **不重算** ⇒ 预补一直挂着 ⇒ 力量到账后**叠加** |

**修法（v0.2.307）**：给 `PlannedPlay` 加 `StampedeOnly`，惊逃那张：
- 由规划器**走普通出牌那条路**规划（`result.Add`），只是 `Cost = 0`、`StampedeOnly = true`；
- `CharBossMonster` 的播放循环里 `if (play.StampedeOnly) continue;`（她自己不打它）；
- 回合结束仍由 `Core/StampedePlays` 把它打出去（挑牌时优先剧本点名的那张）。

于是它现在和普通出牌**共用**：意图渲染、`RecomputeAll` 重算、「力量到账后撤回预补」。日志里的
「预补力量 +N」也只在真的还有牌没打出去时才是 N。

版本 **v0.2.307**；`content/` 没变 ⇒ PCK 未重打。dll `3C62AE765310`、json `064F8003AA52`，
构建 / 游戏 / 工坊三处一致；`workshop\changenote.txt` 已写、`mk-workshop-json.ps1` 已重跑。

## 2026-11-02：v0.2.306 —— 炼药贼用瓶中精灵复活后没清掉灾厄

用户：「炼药贼使用瓶中精灵复活后没有清除掉自身的灾厄」。

**我们的实现**（`Patches/BossFairyInABottlePatch.cs`，2026-10-25 起）：
`Hook.ShouldDie` 前缀拦下她的死亡 → 丢药水 → `CreatureCmd.Heal(30% 最大生命)` →
`ClearDebuffsAsync()` 清掉**所有 `PowerType.Debuff`** 的 power（当时用户要求「瓶中精灵触发时清除所有负面效果」）。

**为什么灾厄会漏**：灾厄有**两套实现** ——
- 原版亡灵契约师：`MegaCrit.Sts2.Core.Models.Powers.DoomPower`（`Type => Debuff`）；
- 崩坠/收藏家 mod：`Collector.CollectorCode.Powers.CollectorDoomPower`，id 形如
  `COLLECTOR-COLLECTOR_DOOM_POWER`（反编译确认它 `: base((PowerType)2, …)`，但**这是别的模组自定义的
  power**，不同模组还可能改移除流程 / 分类）⇒ 只按 `Type == Debuff` 过滤并不可靠，
  用户实测就出现了「复活了但灾厄还在」。

**修法**：
1. 判据加一条**按名字**：`Id.Entry` 或类型名里含 `DOOM` 的 power，无论什么 `Type` 一律清掉
   （`IsDoomPower()`），日志里会明确写「清除灾厄 XXX（N）」；
2. 复活**前后**各打一行完整 power 名单：
   `瓶中精灵：复活前她身上的 power [...]` / `复活后 ...` —— 下次再有问题一眼就能看出是哪一层没清。

版本 **v0.2.306**；`content/` 没变 ⇒ PCK 未重打。dll `A5CD48F28DEA`、json `3D954456CDD4`，
构建 / 游戏 / 工坊三处一致；`workshop\changenote.txt` 已写、`mk-workshop-json.ps1` 已重跑。

**待用户复验**：炼药贼带瓶中精灵被打死 → 复活后她身上不应再有灾厄；
日志里应出现 `清除灾厄 COLLECTOR-COLLECTOR_DOOM_POWER（N）`（或原版 `DOOM_POWER`）。

### 追加：v0.2.305 —— 完美打击虚高（57 vs 43）

用户：
> 「第六回合完美打击的伤害显著偏高，原本卡组中有 7 张名字中包含打击的牌，
> 伤害应该是（6+3*7+2）*1.5=43.5，向下取整为 43，但是实际上是 57。
> 你是不是把他原本卡组中的打击数量重算了一遍？」

**是的，而且是架构性问题**。原版公式（反编译 `CalculatedVar.Calculate`）：
```
伤害 = CalculationBase + ExtraDamage × (牌堆里带 Strike 标签的牌数)
```
那个「牌数」数的是 `PlayerCombatState.AllCards`（**所有牌堆**）。本模组的 Boss 手牌
**每回合都是重新造的**（`BossScriptRunner.BuildHand`），前几回合的剧本牌全堆在弃牌堆里 ⇒
也被数进去 ⇒ 张数 10 而不是 7（`6 + 3×10 + 2` = 38，×1.5 易伤 = **57**）。

**修法**：新增 `BossScriptRunner.TakeOverPerfectedStrike(card, boss)`，在造牌时接管这一张：
- 张数改成「`boss.Def.DeckForAct` 里带 `CardTag.Strike` 的牌」+「本场已生成的打击类复制」
  （`CharBossMonster.StrikeCopiesMade`，在 `BuildHand` 造 `JugglingCopy` 时按 Strike 标签累加）；
- 把 `CalculationBase` 写成 `基础 + 每张 × 张数`、`ExtraDamage` 归零
  ⇒ `Calculate` 就是我们的数字 + 0 ×（被污染的张数）；升级 / 易伤 / 力量仍由原版管线算。
日志：`完美打击：按「卡组 6 张打击 + 复制 1 张 = 7 张」结算 ⇒ 基础 6 + 3×7 = 27`。

版本 **v0.2.305**；`content/` 没变 ⇒ PCK 未重打。dll `9DFC20D69AA3`、json `FB75316EB0B0`，
构建 / 游戏 / 工坊三处一致；`workshop\changenote.txt` 已写、`mk-workshop-json.ps1` 已重跑。

### 追加：v0.2.304 —— 用户点出了具体数字：10 对、9 错

用户（第二张手牌截图：预备打击+、打击 6、打击 6、打击 6(复制)；意图 9 / **10** / **9**）：
> 「你这两张打击确实是不同的逻辑。你让惊逃、奇巧、彼岸咆哮这些特殊打出方式的伤害和普通出牌的伤害预览
> 计算方式对齐（你仍可以把他们分成不同的类）。比如这里，按照打出卡牌的预览计算方式是对的，
> 而惊逃的计算方式就是错的（进阶 9~10 升级预备打击，预备打击+ 会提供 4 点临时力量而非 3 点）。」

**两个真问题**：

1. **预备打击的力量算成 3 而不是 4**。`SetupStrike+` 的 `PowerVar<StrengthPower>` 升级 +1，
   但升级值只在**预览管线**里（`UpdateDynamicVarPreview` → `PreviewValue`），
   我上一版直接读 `BaseValue` ⇒ 永远是未升级的 3。
   ⇒ 新增 `BossDamagePredictor.VarPreviewInt(card, key, fallback)`（与 `ReadBaseDamage` 同一条管线：
   `ClearPreview` → `UpdateDynamicVarPreview` → 逐个 `UpdateCardPreview(runGlobalHooks:false)` → 读 `PreviewValue`），
   预备打击的力量改用它。
2. **三条「特殊打出方式」各自抄了一份意图参数**。现在统一成一个入口
   `PreviewIntents(card, target, pendingDrop, pendingRise, inTurnStrength)`：
   惊逃（`StampedePlay`）、奇巧（`Sly`）、彼岸咆哮（`Burned` + `FreePlayExhaustEntry`）、
   以及「只显示意图」的 `DisplayOnly` 全部走它，与她自己出牌那条**同一个 `IntentsFor` 口径**
   （差异只保留在「本条路径特有的东西」上：X 费段数、全身撞击的格挡、爪击成长 —— 这些普通路径才有）。

版本 **v0.2.304**；`content/` 没变 ⇒ PCK 未重打。dll `D10224DFE21B`、json `8A02D4EB0F24`，
构建 / 游戏 / 工坊三处一致；`workshop\changenote.txt` 已写、`mk-workshop-json.ps1` 已重跑。

### 追加：v0.2.303 —— 「两个打击伤害不一样」与补偿口径统一

用户（附手牌截图：预备打击+、打击 6、打击 6、打击 6（复制，虚无+消耗）；
下面三个意图 9 / 10 / 9）：
> 「？？这两个打击怎么伤害还不一样，你是不是用了两套不同的伤害补偿机制？」

**先承认：确实有两层数值，但它们是「不同时刻的值」，不是两套算法**：

| 位置 | 数值 | 含义 |
|---|---|---|
| **卡面** | 6 | 卡牌自己的 `DynamicVars` —— **此刻真实**的伤害。那 3 点力量要等她把预备打击**打出去**才到账 |
| **意图** | 9 = 6 + 3 | 规划期的**预测**（`inTurnStrength`），也就是她打完预备打击后真会打出的数字 |

**但确实存在「不止一条展示路径」的真问题**：全模块有四处会生成「她即将打出的牌的意图」——
① 她自己出牌（`result.Add`）；② 惊逃打出（`StampedePlay`）；③ 被丢弃后奇巧自动打出（`Sly`）；
④ 被烧掉后从消耗堆自动打出（`Burned` + `FreePlayExhaustEntry`）。
v0.2.302 只给 ① ② 补了 `inTurnStrength`，③ ④ 没补 ⇒ 同一张牌换个位置数字就不同。
v0.2.303 把 ③ ④ 也改成带 `extraDamage: inTurnStrength`，四条路径口径一致。

**另外加了诊断**：`[CharBoss] 意图 XXX: [...]（预补力量 +N、力量遗留 -M、回合前 +K、费用 C）`
—— 下次如果还看到两张牌的意图不一样，把这两行发出来就能立刻定位是哪一笔多加/少加。

⚠ 也记一个观察：按剧本第二回合她只会打出 2 张攻击牌（预备打击 + 打击），
「手里剑」要第 3 张攻击才 +1，所以两张打击的意图**都应该**是 9；
如果实测是 9 和 10，说明还有别的地方在加力量（等日志确认）。

版本 **v0.2.303**；`content/` 没变 ⇒ PCK 未重打。dll `C515C6129B42`、json `EF92534B1259`，
构建 / 游戏 / 工坊三处一致；`workshop\changenote.txt` 已写、`mk-workshop-json.ps1` 已重跑。

### 追加：v0.2.302 —— 意图里补上「还没到账」的力量

用户：
> 「第二回合的第二张打击（被惊逃打出的）没有算预备打击的临时力量。」
> 「第三回合的第 4 张踩踏没有计算手里剑。」

两个都是**同一类**问题：意图节点只加「此刻真实存在的力量」，而这两笔力量要等牌打出去才到账
（规划期不存在）⇒ 必须由规划器按剧本顺序预补。

- `SetupStrike.OnPlay`（反编译）：先打 7 伤，再 `PowerCmd.Apply<SetupStrikePower>(…, Owner.Creature, 3)` ——
  **力量加给她自己**（临时力量）。
- `Shuriken.AfterCardPlayed`（反编译）：`AttacksPlayedThisTurn++`，每满 `Cards`（3）就
  `+Strength`（1）—— 第 3 张**打完**才到账 ⇒ **第 4 张**攻击吃得到。

修法（`Core/BossScriptPlanner.cs`）：循环里新增 `inTurnStrength`（本回合已预补的力量），
① 计划到 `SETUP_STRIKE` 时 `+= 3`（读卡上的 `Strength` 变量，读不到用 3）；
② 计划到攻击牌且她身上有 `SHURIKEN` 时，攻击计数满 3 的倍数就 `+= 1`（读遗物的 `Cards`/`Strength`）；
③ 这个值同时加到
   · 普通出牌的 `extraDamage`（排在后面的攻击意图跟着涨）；
   · **惊逃那张牌的展示意图**（`StampedePlay` 分支的 `extraDamage`）。
日志：`意图预补：预备打击给 3 点临时力量 ⇒ 本回合后续攻击意图 +3` /
`意图预补：手里剑（第 3 张攻击）⇒ 本回合后续攻击意图 +1`。

版本 **v0.2.302**；`content/` 没变 ⇒ PCK 未重打。dll `C60BE12B8A5F`、json `6A2FB7C5139B`，
构建 / 游戏 / 工坊三处一致；`workshop\changenote.txt` 已写、`mk-workshop-json.ps1` 已重跑。

### 追加：v0.2.301 —— 第 2/3 回合与「虚无 → 无惧疼痛」

用户第三条反馈：

1. **第 2 回合首张 ↔ 第 3 回合首张对调**：T2 变成「预备打击、打击、打击(惊逃)、打击(复制)」，
   T3 变成「打击、打击、踩踏(她说 3→1 费)、踩踏(复制→惊逃)」。费用都对得上（T2 = 2、T3 = 3）。
2. **复制牌「虚无」自行消耗 + 无惧疼痛起甲** —— 两件事原版都不会替她做：
   · 原版「回合结束把手牌里的虚无牌消耗掉」跑在**真人玩家**的收尾流程里，影子玩家不在
     `CombatState.Players` ⇒ 从来不会消耗；
   · 「无惧疼痛」是**她身上的 power**，power 钩子扇出同样到不了影子玩家
     （与 `SneakyOnBossAttack` 同一个坑）⇒ 消耗了也不给格挡。
   新文件 `Core/EtherealHandExhaust.cs`：她回合结束（**惊逃之后、丢手牌之前**）把带
   `CardKeyword.Ethereal` 的手牌挪进消耗牌堆，每张调一次
   `FeelNoPainPower.AfterCardExhausted(ctx, card, false)` 给格挡。
   ⚠ 时机说明（用户确认）：奥利哈钢看的是**她回合开始**（`BeforeSideTurnStart`）那一刻有没有格挡，
   与回合结束这笔格挡无关 ⇒ 不会被压掉。
3. **被惊逃打出的复制踩踏补意图**：`Copy<T>(playedByStampede: true)` ——
   带这个标记的复制牌照样进 `DisplayPlays`（画亮 + 出攻击意图）；
   第二回合那张不带标记的打击复制只自行消耗、不画意图。

版本 **v0.2.301**；`content/` 没变 ⇒ PCK 未重打。dll `0F2E564BA159`、json `5409B915E770`，
构建 / 游戏 / 工坊三处一致；DLL 内确认含 `v0.2.301`；`workshop\changenote.txt` 已写、
`mk-workshop-json.ps1` 已重跑。

### 追加：v0.2.300 —— 四处再修

用户第二轮反馈：

1. **「开心小花计数怎么不变了」** —— 上一版我用 `GetField("TurnsSeen")` 写计数，
   而 `TurnsSeen` 是 **public 属性**（`P System.Int32 TurnsSeen { get; set; }`，后备字段 `_turnsSeen`）
   ⇒ 反射找不到字段、又不报错 ⇒ 图标计数一直停在 0。现在直接
   `if (relic is HappyFlower flower) flower.TurnsSeen = value;`。
2. **踩踏（STOMP）的减费** —— 反编译：`ReduceCostBy(count(CardPlaysFinished 里本人本回合的攻击牌))` →
   `EnergyCost.AddThisTurn(-amount)`，也就是**打出时**按「本回合已打完的攻击牌数」减费。
   规划期发生在这些牌打出去之前 ⇒ 必须自己记账：`BossScriptPlanner` 新增
   `attacksPlannedThisTurn`（每计划一张攻击牌 +1），STOMP 的费用改成
   `max(0, cost - attacksPlannedThisTurn)`。于是第三回合：
   开心小花 +1 能量（3 费）⇒ 预备打击 1 + 打击 1 + **踩踏 3→1** 都能打出来。
3. **被复制的踩踏要被惊逃打出去** —— 用户：「杂耍复制踩踏，然后回合结束时被复制的踩踏自动打出然后消耗
   （踩踏结算先于虚无）」。⇒ 第三回合剧本改成 `预备打击、打击、踩踏(她自己打)、踩踏(Copy)`；
   `StampedePlays` 的挑选顺序补第 ③ 步：**普通攻击牌都没有时，退而打复制牌**
   （第二回合仍然是「惊逃打普通那张、复制那张因虚无消失」）。
4. **特殊效果图标** —— 「燃烧契约」的图标改成原版「好奇」（`CuriousPower` 的灯泡）：
   在 `Patches/BossSpecialPowerIconPatch` 的名单里加上 `Powers.BossBurningPactPower`。

版本 **v0.2.300**；`content/` 没变 ⇒ PCK 未重打。dll `F4D67A2E6103`、json `18FF76B8AB8E`，
构建 / 游戏 / 工坊三处一致；DLL 内确认含 `v0.2.300`；`workshop\changenote.txt` 已写、
`mk-workshop-json.ps1` 已重跑。

## 2026-11-02：v0.2.299 —— 杂耍战三处反馈

用户实测三条：

### 1. 开心小花计数/加费时点错（**别搞出重复加费**）

用户口径：「战斗开始时计数 0，第 1、4、7（3n+1）回合 1，第 2、5、8（3n+2）回合 2，
第 3、6、9（3n）回合加费并归 0」。

根因：原版 `HappyFlower.AfterSideTurnStart` 是「每次某个阵营回合开始 +1，满 3 加 1 费」，
而**影子玩家的遗物会被广播多次**（原版广播 + `BossHookBridge` 的补触发）⇒ 计数跑得比回合快。

修法（与「赤牛 / 破损核心 / 击剑指南」同一套）：
- 新文件 `Patches/HappyFlowerNoDoublePatch.cs`：`HappyFlower.AfterSideTurnStart` 前缀，
  拥有者是我们 Boss ⇒ 跳过并返回 `Task.CompletedTask`（**原版那条一律不跑，不会有第二次加费**）；
- 新文件 `Core/HappyFlowerPlays.cs`：在 `BossHookBridge.FireSideTurnStart`（她自己的回合开始）
  按 `TurnNumber` 算：`turn % 3` 即计数（1/2/0），为 0 时 `PlayerCmd.GainEnergy(1)` 并记日志；
  顺带把遗物自己的 `TurnsSeen` 反射摆到同一位置，让图标进度和实际一致。

### 2. 惊逃打出的攻击牌没有攻击意图

`StampedePower` 是**回合结束**才把它打出去，而手牌栏只给 `Plan` / `DisplayPlays` 里的牌画意图 ⇒ 光秃秃。
修法：新增 `ScriptCard.StampedePlay`，规划器里照 `DisplayOnly + AutoPlayed` 的做法
把它塞进 `DisplayPlays`（画亮 + `BossIntentBuilder.IntentsFor` 出意图）。

### 3. 第二回合应该是 4 张打击

用户：「前 2 张费用打出，第三张惊逃打出，第四张杂耍复制然后因为虚无消耗」。
⇒ 剧本第 2 回合改成 `打击、打击、打击(StampedePlay)、打击(JugglingCopy)`；
第 3 回合同理（`预备打击、打击、踩踏(StampedePlay)、踩踏(JugglingCopy)`）；第 4 回合 `打击、防御、痛击(StampedePlay)`。

同时把 `Core/StampedePlays.cs` 从「调原版实现（**随机**挑攻击牌）」改成**自己挑**：
优先剧本点名（`StampedePlay`）那张 → 其次其它攻击牌 → **跳过复制牌**（带虚无+消耗，本来就该自己消失）
⇒ 不再有「惊逃随机打到复制牌」的不确定性。

版本 **v0.2.299**；`content/` 没变 ⇒ PCK 未重打。dll `9FC1FC75D58F`、json `89D61A8F3200`，
构建 / 游戏 / 工坊三处一致；DLL 内确认含 `v0.2.299`；`workshop\changenote.txt` 已写、
`mk-workshop-json.ps1` 已重跑。

**待复验**：① 开心小花只在她第 3、6、9… 回合各 +1 费（日志 `开心小花：第 N 回合（3 的倍数）→ +1 能量`）；
② 惊逃打出的那张牌在手牌栏里有攻击意图；③ 第 2 回合手牌 4 张打击。

## 2026-11-02：v0.2.298 —— 新 Boss：二层变体「**杂耍战**」

用户口述设计（权威记录见 `docs/plans/boss-juggler.md`，**改设计先改那份**）。
二层铁甲战士的第二套设计，与「放血战」并列 ⇒ **以房间遭遇为准**（`IRONCLAD_JUGGLER_BOSS_ENCOUNTER`），
拿不到遭遇按种子 50/50（salt 53）。

### 关键机制（全部反编译核对过）

| 设计稿里的名字 | 类 | 真实行为 |
|---|---|---|
| 杂耍 | `Juggling` / `JugglingPower` | 能力：`BeforeCardPlayed` 里把**这回合前 3 张攻击牌**各克隆一张扔进手牌 |
| 惊逃 | `Stampede` / `StampedePower` | 能力：`AfterAutoPostPlayPhaseEntered` 里**回合结束**随机打出手牌里 `Amount` 张攻击牌，**不花能量** |
| 彼岸咆哮 | `HowlFromBeyond` | 回合结束时若在**消耗牌堆**里就自动打出（框架已有 `FreePlayExhaustEntry`） |
| 完美打击 | `PerfectedStrike` | 伤害 = 6 + 2 ×「`AllCards` 里带 Strike 标签的牌数」 |

### 特殊效果「燃烧契约」（`Powers/BossBurningPactPower.cs`）

「杂耍生成的攻击牌获得**虚无**和**消耗**」——两半实现：
1. **拦掉原版**：新文件 `Patches/JugglingNoClonePatch.cs` —— `JugglingPower.BeforeCardPlayed` 前缀，
   拥有者是我们自己的 `CharBossMonster` 就跳过（⚠ 该 `Owner` 是 **Creature**，写 `.Creature.Monster` 编译不过）；
2. **剧本提前摆牌**：新增 `ScriptCard.JugglingCopy`，`BuildHand` 造牌时加
   `Ethereal` + `Exhaust` 关键字，并配 `HoldInstead`（她不花能量打它）。

### 惊逃必须手动派发

`StampedePower.AfterAutoPostPlayPhaseEntered(ctx, player)` 只派发给 `CombatState.Players` 里的真人，
影子玩家收不到 ⇒ 新文件 `Core/StampedePlays.cs`，在 `CharBossMonster` 的**回合结束**（丢手牌之前）
直接调它自己的实现；调用点就在 `FreePlayTopEntry`（所向无敌）那段之前。

### 剧本与能量

7 回合、之后 **2～7 循环**（`LoopFrom = 2`、`LoopLength = 6`）。
能量：基础 3 − 涅奥 1 = 2；**第 1 回合灯笼 +1 ⇒ 3**，**第 3、6 回合开心小花 +1 ⇒ 3**。
逐回合：杂耍+无惧疼痛惊逃+ / 打击×2+复制 / 预备打击打击踩踏+复制 / 打击防御+痛击复制 /
防御防御受伤 / 完美打击贪婪愧疚 / 防御坚毅(烧彼岸咆哮)+彼岸咆哮自动打出。

### 接线清单

- `BossKeys`：新 Key `IRONCLAD_JUGGLER`（角色 0）、二层候补加它、`EncounterOf` 映射、
  与 `IRONCLAD_BLOOD` 互为 `ComplementVariant`；
- `CharBossEncounters`：`IroncladJugglerBossEncounter`（`TierAct => 1`）+ 注册进 `TierEncounters`；
- `BossDefs.Ironclad.VariantsByAct[1]`：deck / relics / script / SpecialLoc `IRONCLAD_BOSS.pact`
  / `BossBurningPactPower` / 进阶 8 升无惧疼痛+坚毅、进阶 9 升预备打击+彼岸咆哮；
- `BossLineups.JugglerActive()`（遭遇优先 + 种子 salt 53）；
- BGM：`IRONCLAD_JUGGLER_BOSS_ENCOUNTER` → 帝皇蟹（二层统一）；
- 崩坠藏品：`IRONCLAD_JUGGLER_BOSS_ENCOUNTER` → 「杂耍」`Juggling`；
- 本地化（zh/en 各三份）：`IRONCLAD_BOSS.pact`、`IRONCLAD_JUGGLER_BOSS_ENCOUNTER.{title,loss}`、
  `BOSS_BURNING_PACT_POWER.{title,description}`。

⚠ **踩坑记录**：往 `BossDefs` 插 `BossVariant` 时用「`null, null,` 后面接 `new BossVariant`」当锚点，
这段文本在**三个**角色定义里都有 ⇒ 一次插进了 3 份（编译能过，但会给别的 Boss 挂错变体）。
已按「只保留第一处」修正；以后插入这类块要先确认锚点唯一。

版本 **v0.2.298**；**PCK 已重打**（本地化变了）并校验（10 个 content 文件 + 45 个 extras）。
三处一致：dll `CB56D2FB99BA`、json `B605AC2DC53A`、pck `CD2384FC526E`（12,865,836 B）；
DLL 内确认含 `v0.2.298`；`workshop\changenote.txt` 已写、`mk-workshop-json.ps1` 已重跑。

**待用户实测**：控制台 `fight IRONCLAD_JUGGLER_BOSS_ENCOUNTER` 直接点名这一套；
重点看 ①第 3 回合那张「踩踏」有没有因为费用打不出来（3 能量 + 它自身减费应该够）；
②「惊逃」回合结束打出的到底是不是那张复制牌（原版是**随机**挑手牌里的攻击牌，
若要求严格只打复制牌，需要再加点名机制）。

## 2026-11-02：v0.2.297 —— 血量公式被我改错了（2/3 幕 Boss 少血）

用户：「你是不是让 boss 血量直接基础+120×层数了，怎么 2、3 层 boss 的血也变少了」——
**用户说对了**。

**根因**：`src/Hooks/Monsters/CharBossMonster.Descriptor.cs` 里
```csharp
BlessingHpBonus => Def.CarriesNeowBlessing ? NeowBlessing.MaxHpBonus * (ActIndex + 1) : 0;
```
v0.2.287 把 `NeowBlessing.MaxHpBonus` 从 150 改成「拾起值」的别名（= `PickupMaxHpBonus` = **120**），
于是这个 `×(幕+1)` 的公式**静默**变成了 `120 × (幕+1)`：

| 幕 | 应该是 | 实际（v0.2.287～296） | 差 |
|---|---|---|---|
| 1 | +120 | +120 | 0 |
| 2 | +300 | +240 | **−60** |
| 3 | +480 | +360 | **−120** |

证据：用户 12:54 那局的记录行 `REGENT_BOSS … win，血量 0/435` —— 435 = 75 + **360**，
正是三层储君的错误值（正确应为 555）。

**修法**：改成两项分别取常量（不再用那个别名）：
```csharp
? NeowBlessing.PickupMaxHpBonus + NeowBlessing.PerActMaxHpBonus * Math.Max(0, ActIndex)
```

**修完对表**（脚本算过一遍）：
| 角色 | 1 层 | 2 层 | 3 层 |
|---|---|---|---|
| 铁甲战士 80 | 200 | 380 | 560 |
| 静默猎手 70 | 190 | 370 | 550 |
| 故障机器人 75 | 195 | 375 | 555 |
| 亡灵契约师 66 | 186 | 366 | **577**（含布质果实 31） |
| 储君 75 | 195 | 375 | 555 |

顺带把散落在 `BossDefinition` / `BossDefs` / `DefectScript` / `NecrobinderAct3Script` /
`SilentBrewerScript` / `CharBossMonster.Descriptor` / `BossMultiplayerHpPatch` 注释里的旧算式
（150×3 = 450 / 525 / 520 / 547）全部改成新值，免得下次又被注释误导。

版本 **v0.2.297**；dll `9C1803DFB7DD`、json `5E6B076039F2`，构建 / 游戏 / 工坊三处一致；
DLL 内确认含 `v0.2.297`；PCK 未变；`workshop\changenote.txt` 已写、`mk-workshop-json.ps1` 已重跑。

**教训（写进流程）**：`NeowBlessing.MaxHpBonus` 这个「兼容旧代码」的别名是**语义已变**的别名，
任何 `× 幕数` 的地方都不能再用它 —— 三个常量现已各自命名（`PickupMaxHpBonus` / `PerActMaxHpBonus`）。

## 2026-11-02：v0.2.296 —— 赤牛给辉星储加了两次活力

用户本机实测（进阶 10 的辉星储带赤牛）：
> 「赤牛有 bug，给辉星储加了 2 次活力（应该只在战斗开始时加一次，他第一回合开始时不加）」

**根因**（与「击剑指南 / 破损核心 / 红面具」**完全同一类**双触发）：

- 反编译 `Akabeko.AfterSideTurnStart`（逐字）：
  ```csharp
  if (participants.Contains(Owner.Creature) && Owner.PlayerCombatState.TurnNumber <= 1)
      await PowerCmd.Apply<VigorPower>(Owner.Creature, DynamicVars["VigorPower"].IntValue /* 8 */, …);
  ```
- 路径 ①：我们的 `BossHookBridge.FireCombatStart` 在**战斗开始**逐件遗物手动调
  `AfterSideTurnStart(CombatSide.Player, [她], combat)`，并把影子玩家的 `TurnNumber` 临时摆成 1
  ⇒ 条件成立 ⇒ 活力 +8（这是用户要的那一次）；
- 路径 ②：她**回合开始**时**原版自己会广播**（`IterateHookListeners` 把「带 Player 的生物」的遗物
  也算监听者），而她的 `TurnNumber` 在第 1 回合正好是 1 ⇒ 又 +8（合计 16）。

**修法**：新文件 `src/Hooks/Patches/AkabekoOncePatch.cs`，照 `CrackedCoreOncePatch` /
`FencingManualForgeOncePatch` 的写法给 `Akabeko.AfterSideTurnStart` 挂前缀：
拥有者是本模组的 `CharBossMonster` ⇒ 直接跳过并返回 `Task.CompletedTask`
（⚠ 跳过返回 Task 的方法必须给已完成 Task，否则调用方 `await null` → NRE，「击剑指南」当年踩过）；
真人玩家一律走原版。日志：`赤牛：跳过原版在她回合开始的那次活力（战斗开始时已给过，现有活力 N）`。

版本 **v0.2.296**；dll `49A10B8E014F`、json `9269BCA37A7D`，构建 / 游戏 / 工坊三处一致；
DLL 内确认含 `v0.2.296` 与 `AkabekoOncePatch`；PCK 未变；
`workshop\changenote.txt` 已写、`mk-workshop-json.ps1` 已重跑。

**待复验**：进阶 9+ 打辉星储，战斗开始时她身上应只有 **8** 层活力，第一回合开始不再涨。

**顺带记一个还没修的显示问题（等用户确认要不要动）**：`VigorPower.ModifyDamageAdditive` 是
「每次算伤害都加 Amount」，所以只要她身上还有活力，**第一回合所有攻击意图**都会显示 +8
（实际只有第一张攻击吃），这与原版玩家侧的表现一致；要消掉得在意图里像「星星点点」那样
按「先预补、之后逐张扣回」处理。

## 2026-11-02：v0.2.295 —— 「Boss 打死了，卡面还卡在屏幕中间」

用户本机实测：「搞了一堆剑柄打击+，最后 boss 都被揍死了还卡在屏幕中间，是不是结算有问题？」

**先看日志（`%APPDATA%\SlayTheSpire2\logs\mod_log.txt`，12:53 那一局，跑的是 v0.2.293）**：

- `真人玩家的牌在敌方回合里结算：POMMEL_STRIKE` 连续 5 行（12:53:20/21/22/23/24）——
  我们那条日志**每进程最多 5 行**，说明那条自动出牌链跑了远不止 5 次；
- 每一行相隔约 1 秒 ⇒ 卡面是一张一张「飞过去 → 结算 → 出下一张」的节奏；
- 结束时 `数据记录（一场一行）：REGENT_BOSS 为主 … win，血量 0/435，回合 2`
  ⇒ **伤害结算完全正常**，Boss 确实是被这串牌打死的；
- 同一局还有 `原版回合结束步骤：AwaitTurnEndAndSwitchSides = 38177ms`（38 秒）
  与另外两条 21696ms / 9364ms —— 那是原版回合收尾阶段被这条长链拖住的实测值。

**链条本身是玩家自己的组合技**（不是我们算错）：被 Boss 打中 →「百年积木」在**敌方回合**抽 3 张
→「地狱狂徒」把抽到的「剑柄打击+」自动打出 → 它又抽 1 张 → 又触发……（抽牌堆循环起来就是无限链）。

**真正的遗留 bug（我们这边）**：链条中途把 Boss 打死 ⇒ 战斗结束 ⇒
原版「把卡面收回去」那一步属于**出牌动画流程**，战斗一结束就不跑了；
而我们的 `StrayBossCardQueuePatch.Reap()` **只收她自己打出的牌**（判据 `Monster is CharBossMonster`），
玩家这些自动出牌的卡面没人收 ⇒ 一直吊在屏幕中间。

**修法**（`StrayBossCardQueuePatch` + `BossFrameTickPatch`）：
- 新增 `ReapLeftoversAfterCombat()`：把 `NCardPlayQueue` 里剩下的**所有**卡面移除
  （失败就隐藏），并记一行 `战斗结束后清理残留卡面：移除 N 个、隐藏 M 个`；
- 新增 `SweepLeftoverCardsAfterCombat()`（1Hz），只在 **`CombatManager.IsInProgress == false`** 时动手；
  调用点放在 `TickInner` 里 `boss == null` 早退**之前**（战斗结束时她已经被释放了，
  原来的 4Hz 慢车道兜不到）。

版本 **v0.2.295**；dll `8D2EA1203FB9`、json `18E4EB94B623`，构建 / 游戏 / 工坊三处一致；
DLL 内确认含 `v0.2.295`；PCK 未变；`workshop\changenote.txt` 已写、`mk-workshop-json.ps1` 已重跑。

**待复验**：再打一次同样的无限链 → Boss 死后卡面应当立刻消失，日志里出现
`战斗结束后清理残留卡面：移除 N 个…`。

### 追加：v0.2.294 —— v0.2.293 的第一张还是漏（用户本机实测「第一张剑柄打击还是会打自己」）

**结论：登记点挂错了位置。** 原版 `CardCmd.AutoPlay` 的顺序是
**先在 `AutoPlay` 内部把目标挑出来 → 然后才走 `Hook.BeforeCardPlayed`**。
所以 v0.2.293 只在 `Hook.BeforeCardPlayed` 登记，必然漏掉每一串自动出牌的**第一张**
（第 2、3 张能对，是因为前一张的登记还没被注销 ——「第一张会打自己」正是这个特征）。

修法：再挂一根**更早**的钩子 —— `[HarmonyPatch(typeof(CardCmd), nameof(CardCmd.AutoPlay))]` 前缀，
按**卡牌实例**登记（`IsRealPlayerCardModel`：`card.Owner.Creature.Monster == null && Player != null`）。
注销仍在 `Hook.AfterCardPlayed` 后缀，两种键（`CardPlay` 实例 / 卡牌实例）各摘一次；
15 秒兜底把两张表一起清。

⚠ 踩坑记录：Harmony 前缀**不能把原版 6 个形参全抄一遍** —— 原版最后一个参数叫
`skipCardPileVisuals`（我一开始写成了 `skipCardPlayVfx`），名字对不上会在挂载期抛错。
现在只声明用得上的 `Prefix(CardModel card)`。

版本 **v0.2.294**；dll `ECD24E98C700`、json `187AA8D58E99`，三处（构建/游戏/工坊）一致；
DLL 内确认含 `v0.2.294` 与新补丁类 `RealPlayerAutoPlaySideExemptPatch`；PCK 未变。

## 2026-11-02：v0.2.293 —— 玩家反馈真 bug：「地狱狂徒 + 百年积木」自己的打击打自己

工坊评论（友利奈绪，12 小时前）：
> 「反馈一个bug，玩家战士打了地狱狂徒，被boss击中触发遗物百年积木的效果，积木抽上来的打击牌
> 会自动对玩家打出而非对boss打出」

**先把名字对上**（很关键，差点查错方向）：
- 「地狱狂徒」`HELLRAISER` **不是 Boss，是一张铁甲战士能力牌**：
  「每当你抽到名字中有『打击』的牌时，对一名随机敌人打出这张牌。」（`HellraiserPower`）
- 「百年积木」`CENTENNIAL_PUZZLE`：每场战斗**第一次损失生命**时抽 `Cards` 张。

**完整链路（逐条反编译核对）**：
1. 玩家被 Boss 打中（未格挡伤害）⇒ `CentennialPuzzle.AfterDamageReceived` 在**敌方回合里**
   连调 `CardPileCmd.Draw(ctx, Owner)` 抽 3 张；
2. 抽到「打击」⇒ `HellraiserPower.AfterCardDrawnEarly` 调
   `CardCmd.AutoPlay(choiceContext, card, target: null)` —— **不给目标**；
3. 原版帮忙选目标的那段（`CardCmd.AutoPlay` 里的过滤委托 `<AutoPlay>b__0`）是
   「在 `HittableEnemies` 里挑 `c.IsAlive && c.IsPlayer && c != card.Owner.Creature`」；
4. **我们的锅**：`HittableEnemiesSidePatch` 在**整个敌方回合**把 `HittableEnemies` 翻成「玩家侧」
   （判据只有 `CurrentSide == CombatSide.Enemy` + `BossFightActive`）⇒ 第 3 步只在玩家里挑
   ⇒ 玩家自己的打击打到了玩家自己（单人时就是他自己）。

**修法（只加一道豁免，不改任何既有规则）**：
- `CombatSidePatches`：新增 `RealPlayerCardDepth`（记录 `CardPlay` 实例的 `HashSet`，引用相等）
  + `PushRealPlayerCard/PopRealPlayerCard/RealPlayerCardStale`；
  `HittableEnemiesSidePatch.Prefix` 在「① 豁免」之后加一条
  **`if (RealPlayerCardDepth > 0 && !RealPlayerCardStale()) return true;`**（走原版语义）。
- 新文件 `Patches/RealPlayerCardSideExemptPatch.cs`：`Hook.BeforeCardPlayed` 前缀登记、
  `Hook.AfterCardPlayed` 后缀注销。这两个钩子对 `CardCmd.AutoPlay` 打出的牌**同样派发**
  （依据：`CharBossMonster` 注释里记的「她 AutoPlay 的牌靠原版 `Hook.AfterCardPlayed` 扇出，
  只是扇给 Power 那步会漏」⇒ 钩子本体是会被调的）。
- 判据 `IsRealPlayerCard`：`cardPlay.Player.Creature.Monster == null && Player != null`
  ⇒ 她（影子玩家）的牌不算，照旧翻侧。
- 兜底：登记表超过 15 秒没等到 `AfterCardPlayed` 就整段作废并记一行日志
  （否则「翻侧」会被永久关掉 ⇒ 她自己的 AOE 打到自己，那是更严重的回归）。

**为什么不改成「只有她出手时才翻」**：`BossActingDepth` 虽然一直在计数，但她的**回合边界效果**
（炸弹、冰雹风暴、充能球被动、药水、奥斯提攻击）都不在出牌窗口里 —— 收窄成 `BossActingDepth > 0`
会让这些效果按「敌方阵营」解析而打到她自己身上，风险远大于收益。豁免方向更安全。

版本 **v0.2.293**；`content/` 没变 ⇒ PCK 不用重打。dll `CE663C841285`、json `557566C418E7`，
`mods\CharacterBosses\` 与工坊 `3830639796` 都已同步；DLL 内确认含 `v0.2.293` 与两个新补丁类名；
`workshop\changenote.txt` 已写、`tools\mk-workshop-json.ps1` 已重跑（note 332 字符）。

**待复验**（玩家那套配置）：铁甲战士 +「地狱狂徒」，被 Boss 打中让「百年积木」抽到打击 ⇒
那几张打击应打向 Boss。`mod_log.txt` 里会有
`真人玩家的牌在敌方回合里结算：<ENTRY> ⇒ 这一段「可命中敌人」走原版`（最多 5 行）。

## 2026-11-02：v0.2.292 —— 表里那两条「出牌队列滞留」不是 bug（诊断收窄）

用户贴了表里两条 diag 问「是真 bug 吗」：

```
[CharBoss] ⚠ 出牌队列滞留：SURVIVOR（拥有者 WBZ）已在队列里 3.0s，队列内卡面 2 个
[CharBoss] ⚠ 出牌队列滞留：FUSED_GU_ZHEN_REN_CARD_SHI_BAI_GU_GU_ZHEN_REN_CARD_SHI_BAI_GU（拥有者 古月方源）…
```

**结论：不是本模组的 bug，也不是卡死。** 证据两条：

1. **拥有者都是真人玩家**（`WBZ` / `古月方源`），不是我们的影子玩家 —— 我们的 Boss 出牌走 `CardCmd.AutoPlay`，
   选择器由我们自动回答，卡面另有清理（`StrayBossCardQueuePatch`）。
2. **原版 `Survivor.OnPlay` 反编译**：`GainBlock(8)` 之后 `CardSelectCmd.FromHandForDiscard(ctx, owner, …, 1)`
   —— **等玩家自己挑一张牌弃掉**。选牌面板开着的时候，那张牌的卡面本来就一直挂在 `NCardPlayQueue` 里，
   玩家多想几秒就是几秒。蛊真人那张 `FUSED_…` 是别的模组的融合牌（长演出 / 要选择 / 网络延迟），同一类。

**真问题是我们自己这条看门狗的判据太宽**（而且它还把线索当“问题”传进了表里）：

- `BossFrameTickPatch.WatchPlayQueue` 以前扫**整个队列**（谁的牌都报）⇒ 玩家只要出「需要选择」的牌
  就可能被记一条 ⚠。现在只认**我们 Boss 的牌**，判据与 `StrayBossCardQueuePatch.IsOurBossCard` **同一个**
  （`card.Model?.Owner?.Creature?.Monster is CharBossMonster`，该方法从 `private` 改成 `internal` 复用）。
  日志里的“队列内卡面 N 个”也跟着改成“我们 Boss 的卡面 N 个”。
- `Telemetry.Diagnostic` 新增一条本地专用过滤：含「出牌队列滞留」的行**只写本机 `mod_log.txt`，永不进表**
  —— 它只是线索，不属于「失败 / 异常 / 不同步」三类。

版本 **v0.2.292**；`content/` 没变 ⇒ PCK 不用重打。dll `452A5FAA6679`、json `B0FAD615CEF3`，
`mods\CharacterBosses\` 与工坊 `3830639796` 都已同步；DLL 内确认含 `v0.2.292`；
`workshop\changenote.txt` 已写、`tools\mk-workshop-json.ps1` 已重跑（note 232 字符）。

**注意**：`.dist\CharacterBosses-v0.2.291.zip` 是上一条命令打的本地包，**不含这次修复**；
要发给玩家就说一声「打包」，我重打成 v0.2.292。

## 2026-11-01 晚：v0.2.291 —— 削弱灾厄骨 + 统计脚本加「进阶」筛选

用户（附 `feishu-stats.ps1` 的输出截图）：「削弱灾厄骨，将她第8回合的无处可逃和第11回合的第一张打击调换位置。
另外，为这个表格再增加一个筛选条件：进阶为10」。

### 1. 灾厄骨（`src/Hooks/Core/NecrobinderScript.cs`）

| 回 | 改前 | 改后 |
|---|---|---|
| 8 | 死亡使者、**无处可逃**、防御（没费） | 死亡使者、**打击**、防御（没费） |
| 11 | **打击**、打击、能量汲取 | **无处可逃**、打击、能量汲取 |

- 费用仍对得上：死亡使者 2 + 无处可逃/打击 1 = 3（第 8 回合防御照旧没费）；
  第 11 回合 无处可逃 1 + 打击 1 + 能量汲取 1 = 3，三张都打得出去。
- 效果 = 削弱：第 8 回合少一次灾厄、改成 6 点普攻；灾厄压力挪到第 11 回合。
- 升级顺序不受影响（死亡使者仍是第 11 回合那次能量汲取之后才升；无处可逃仍由第 11 回合的
  能量汲取负责升级）。

### 2. `tools\feishu-stats.ps1` 新增 `-Asc10` / `-Ascension`

- `-Asc10`：只要**进阶 10** 的对局（就是用户要的那个条件）；
- `-Ascension 9,10` / `-Ascension 5`：通用写法（逗号/分号/空格分隔都行，重复也无所谓）；
- 读不到进阶的行（缺字段的旧行）**一律排除**，并且单独计数「进阶不符」；
- 过滤提示会打印「只要进阶 10」，和 `-SoloOnly` / `-VanillaChars` 可以叠加；
- `-SelfTest` 的假数据加了 `ascension`（5 和 10 都有，其中一行特意写成 `10.0` 走 double 这条路），
  离线就能验：`-SelfTest -Asc10` ⇒ 5 场（排除升 5 那场）、`-Ascension 5` ⇒ 1 场、`-Ascension 9,10` ⇒ 5 场。
- ⚠ 踩坑：`$Ascension.Split(',', ';', ' ')` 在 PS 5.1 会绑到 `Split(char[], int, ...)` 重载上直接抛
  「Cannot convert argument "count"」⇒ 改用 `-split '[,;\s]+'`。BOM 已重新补回（PS 5.1 才认中文）。

### 3. `ascension` 列本来就存在（我一开始判断错了 —— 已订正）

我最初看 `tools\feishu-add-columns.ps1` 里没有 `ascension`，就推断「表里没这一列、需要补建」。
**用户随即贴了截图**：表里确实有 `ascension` 列，值是 `10.0`（数字列；非进阶局是 `0.0`）
⇒ **不需要跑 add-columns**，`-Asc10` 对着现有数据就能筛。

离线证明（`.tmp\test-asc-parse.ps1`：复制 `AsText`/`AsNum` + 同一段筛选逻辑，喂飞书 `/records`
的几种真形状）：`10` / `10.0` / 富文本 `[{"text":"10"}]` 都能读成 `10`；`0` / `0.0` / 缺字段
一律 `null` 或 `0` ⇒ 被排除（6 行样例里保留 3 行、排除 3 行）。
`ascension` 仍然留在 `feishu-add-columns.ps1` 的列表里（幂等；重建表时会打印「已存在，跳过」）。

版本 **v0.2.291**；`content/` 没变 ⇒ PCK 不用重打。dll `85C8AB2FB4A3`、json `3936726832D6`，
`mods\CharacterBosses\` 与工坊 `3830639796` 都已同步；DLL 内确认含 `v0.2.291`；
`workshop\changenote.txt` 已写、`tools\mk-workshop-json.ps1` 已重跑（note 153 字符）。

### 4. 本地安装包（用户 2026-11-02：「有玩家网不好登不上创意工坊，在本地打包」）

`.dist\CharacterBosses-v0.2.291.zip`（**13,161,856 B**，2026-11-02 10:07 生成）：
`CharacterBosses\{CharacterBosses.dll 801,280 / CharacterBosses.json 598 / CharacterBosses.pck 12,864,940 / 安装说明.txt 95,077}`。

- **完整性**：解压回来重算 MD5，三个二进制与游戏 `mods\CharacterBosses\` **逐一相同**
  （dll `85C8AB2FB4A3` / json `3936726832D6` / pck `F0A2DE13834F`）；zip 里的 DLL 只含 `v0.2.291`（旧版串已无）。
- **安装说明**：标题升到 v0.2.291，**新增两处** ——
  ① 开头「【本地安装（创意工坊连不上 / 网不好时用这个包）】」：BaseLib 前置依赖（工坊 id `3737335127`，
  上不了工坊就让朋友打包 `mods\BaseLib\` 发过来）、解压到 `mods\CharacterBosses\` 的三个文件、
  模组列表勾选、联机必须同版本、卸载方式；
  ② 「【v0.2.291 重要变更（自 v0.2.248 起）】」：血量曲线（涅奥 120/180 + 根除骨 +31）、
  碎屑储（疑虑）、灾厄骨（第 8/11 回合调换）、奇巧贼连续削弱、黑球机（1 张冷光 + 数据磁盘）、
  共用匹配池修复、一代 Boss 曲按幕、薄荷尖塔伤害预测兼容、数据上传修复。
  旧的 v0.2.248 及更早历史**全部保留**（文档 1108 行 / 95,077 B）。
- ⚠ `.dist\CharacterBosses\` 是暂存目录（每次打包覆盖 dll/json/pck；`安装说明.txt` 是累积文档）。


## 2026-11-01 晚：v0.2.290 —— 药水伤害也补进薄荷尖塔的预测

用户追问：「炼药贼的爆炸药水有被计算吗？」→ **没有**（v0.2.289 只补了炸弹 / 冰雹风暴 / 充能球被动）。

**为什么没算**：药水是她在**回合开始**先喝的东西，反编译原版 `PotionModel.OnUse`：
- 爆裂安瓿 `ExplosiveAmpoule`：`DamageVar(10, Unpowered)`，目标是 `CombatState.HittableEnemies`（= 玩家侧全体）；
- 火焰药水 `FirePotion`：`DamageVar(20, Unpowered)`，单体。
它们的意图只画在**我们自己**的药水栏格子上（`BossPotionRow.AddIntent`），
`PublishVanillaIntents` 只发牌意图（`Plan[].Intents` / `DisplayPlays`）⇒ 药水伤害**从不进 `NextMove.Intents`**，
薄荷尖塔自然一个数都算不到。

**改动**：
- `src/Hooks/UI/BossPotionRow.cs` 抽出两个共用口径：
  · `DirectDamage(potion)` —— 这一瓶**自己直接打出**的伤害（爆裂安瓿 10 / 火焰药水 20；其余 0）；
    `IntentFor` 改成用它（显示值不变，只是不再各自硬编码 20）；
  · `PlannedDirectDamage(boss)` —— 本回合**计划要喝、且栏位里真的有**的那几瓶，返回 `(Area, Single)`；
    与 `DrinkPlannedPotionsAsync` 同口径（找不到就不喝 ⇒ 这里也不算）。
    ⚠ 狡诈药水 `CunningPotion` **不计**：它的伤害靠生成的「小刀+」牌打出，那几张牌本来就有意图
    （否则会重复计算）。
- `src/Hooks/Core/MintySpireDamagePreviewCompat.cs` 的 `PendingOf` 末尾加上：
  群攻（爆裂安瓿）对每个人都算；单体（火焰药水）只有被这只 Boss 锁定的人算
  （复用 `BossIntentBuilder.RandomAimIsElsewhere`）。

版本 **v0.2.290**；`content/` 没变 ⇒ PCK 不用重打。
dll `8C5DB1AAECD4`、json `7A88229AE748`，`mods\CharacterBosses\` 与工坊 `3830639796` 都已同步；
DLL 内确认含 `v0.2.290`；`workshop\changenote.txt` 已写、`tools\mk-workshop-json.ps1` 已重跑（note 216 字符）。


## 2026-11-01 晚：v0.2.289 —— 兼容「薄荷尖塔」（MintySpire2）的伤害预测

用户：「「薄荷尖塔」模组的伤害预测现在似乎不会计算「无来源伤害」（如储君的炸弹）。兼容一下（不影响其它逻辑）」。

**先确认对方算法**（反编译工坊 `3737336234` 的 `MintySpire2.dll`：
`MintySpire2Code.combat.SummedIncomingDamageRender`）。它在本机玩家血条右边画 `←N`，
N = `private static int CalculateIncomingDamage(Creature creature)`，只累加三样：

1. 每只敌人 `NextMove.Intents` 里 `IntentType = Attack` 的 `AttackIntent.GetTotalDamage`；
2. 玩家身上的 `DisintegrationPower` / `ConstrictPower`；
3. 玩家**手牌**里「回合结束伤自己」的牌（`CardTurnEndInspector`）。

⇒ 它是**只认意图**的。而我们 Boss 的三样「无来源伤害」根本不在意图里（我们自己在
`BossHandPanel.Extra.cs` 里另画一行「独立意图」）：**炸弹**（`TheBombPower`，剩 1 回合就在她回合结束炸，
40 / 进阶 9 起 50）、**冰雹风暴**（`HailstormPower` 按层数）、**充能球被动**（闪电 / 玻璃球各触发一次，
含镀金缆线）。

**做法**（新文件 `src/Hooks/Core/MintySpireDamagePreviewCompat.cs`）：
- 反射找 `MintySpire2...SummedIncomingDamageRender` → `CalculateIncomingDamage`，挂**后置补丁**把三样加进返回值；
- 只加算术、不碰任何状态 ⇒ 出牌 / 结算 / 胜负全不变（用户要的「不影响其它逻辑」）；
- 数字**直接复用** `BossHandPanel.HailstormDamage` / `BombDamage` / `OrbPassiveDamage`
  （这三个方法从 `private static` 改成 `internal static`，逻辑一行没动；`OrbPassiveDamage` 多一个
  `quiet` 参数，因为薄荷尖塔每刷一次血条就问一次，不静音会刷屏）⇒ 与手牌栏的「独立意图」永远一致；
- 多人：炸弹 / 冰雹风暴打全场（每人看真实数字），充能球被动是单体随机 ⇒ 复用
  `BossIntentBuilder.RandomAimIsElsewhere`，本地玩家不是被锁的人就按 0 算；
- 只认 `CharBossMonster`（随从 / 奥斯提自动排除）⇒ 原版怪物、别的模组怪物完全不受影响；
- 没装薄荷尖塔、或它改类名/方法名（版本不符）⇒ 反射拿不到就安静跳过，只记一行日志。

挂载点在 `BuildInfo.LogOnce`（和其它「所有程序集加载完之后才能挂」的补丁放在一起）。

版本 **v0.2.289**；`content/` 没变 ⇒ PCK 不用重打。
dll `8791BDA135DB`、json `A385A16A95FC`，`mods\CharacterBosses\` 与
`steamapps\workshop\content\2868840\3830639796\CharacterBosses\` 都已同步；DLL 里确认有
`v0.2.289` 与 `SummedIncomingDamageRender` 两个串（UTF-16）；`workshop\changenote.txt` 已写、
`tools\mk-workshop-json.ps1` 已重跑（note 272 字符）。

**待用户复验**：打储君那一局，`mod_log.txt` 里应有
`薄荷尖塔兼容：已给她血条上的「←N」补上本模组 Boss 的**无来源伤害**…`；
炸弹剩 1 回合时，血条右边的 `←N` 应比之前多 40（进阶 9 起多 50），且与手牌栏那颗炸弹意图的数字一致。


## 2026-11-01 晚：v0.2.288 —— 削弱「碎屑储君」（伤口 → 疑虑）

用户：「削弱碎屑储，把伤口改成疑虑（诅咒）」。

**改动**（`src/Hooks/Core/DebrisRegentScript.cs`，只动第 4、8 回合各一张牌）：
- `C<Wound>()` → **`C<Doubt>()`**（`MegaCrit.Sts2.Core.Models.Cards.Doubt` / `DOUBT` / 疑虑 / 诅咒，费用 −1 ⇒ 不可打出）
- 回合备注改成「防御、防御、疑虑（诅咒，打不出；回合结束还在手里 → 自己吃 1 虚弱）」，类注释第 16/20 行同步

**为什么这算削弱**（反编译核对，不是猜）：STS2 的 `Doubt` 是 `CardType.Curse` + `CanonicalKeywords = Unplayable`，
`HasTurnEndInHandEffect = true`，`OnTurnEndInHand` 给**持有者自己**挂 1 层 `WeakPower`
（新挂时 `SkipNextDurationTick = true`，所以不会当场掉一层）。她手牌里的诅咒**会**按时结算 ——
走的是 `CharBossMonster.RunCurseTurnEndHooks`（早前为「羞耻不给脆弱」专门补的那条路，
按 `HasTurnEndInHandEffect` 逐张调 `OnTurnEndInHandWrapper`），疑虑也在那份名单里（注释第 601 行）。
⇒ 从第 4 回合起她基本常驻虚弱，**攻击 −25%**；伤害数值本身没动。

版本 **v0.2.288**；`content/` 没变 ⇒ **PCK 不用重打**。
三处 MD5 一致（dll `2EA6EE899006`、json `E8A042615DC9`、pck `F0A2DE13834F`，PCK 与 v0.2.287 同一份）：
`mods\CharacterBosses\` 与 `steamapps\workshop\content\2868840\3830639796\CharacterBosses\` 都已同步；
`workshop\changenote.txt` 已写、`tools\mk-workshop-json.ps1` 已重跑（note 143 字符、Bilibili 行 0）。
（联机不同步真凶：钻石头冠 20 格挡在帧回调里异步补 ⇒ 两端 20 vs 0；改到同步钩子）

## 2026-10-31 晚：新增 `tools\feishu-stats.ps1`（直接算胜率/难度）

用户：「有没有办法能直接算出胜率？」→ 飞书公式列没法跨行聚合（没有全表 COUNTIF），所以给了脚本。

**做法**：读 cfg → 拉全表记录（`page_size=500` 翻页）→ **只保留** `kind=boss_fight`、`result` 为 win/lose、
`lineup` 不带 `console:`/`aborted:` 前缀的行（可选 `-MinVersion` 过滤旧版本）→ 分组聚合 → 控制台打表 + 可选 CSV。

**分组**：`-By boss`（默认）/ `players` / `charmod` / `boss+players` / `boss+charmod`。

**每行输出**：对局、胜、负、胜率、平均回合、承伤（`pDmg/pMaxHp`）、残血（`pHpEnd/pMaxHp`）、
Boss剩（`hpTotal/maxHpTotal`）、打死（`bossesKilled/bossTotal`）。
难度分档**按原版基准**（用户 2026-10-31 纠正：「原版全游戏击杀率最高的 Boss 也就 25%（玩家胜率 75%），
最低的 12%（玩家胜率 88%）」）：**胜率 <75% 标红**（比原版最难的还难）、**>88% 标黄**（比原版最温和的还简单）、
中间落在原版区间内为正常。阈值可用 `-HardBelow` / `-EasyAbove` 调。

**筛选开关**（用户 2026-10-31：「筛选数据，去掉多人，只囊括使用原版角色的单人战斗」）：
- `-SoloOnly`：只算 `players = 1`（读不到人数的旧行也排除）
- `-VanillaChars`：只算 `pCharMod` 全是 `vanilla` 的对局（**v0.2.267 之前没有这一列 ⇒ 老行会被算进“读不到角色模组”并排除**）
- `-CleanSolo`：上面两个一起开（一条命令拿到“单人 + 原版角色”的干净样本）
- 过滤放在「收集完 $rows 之后」统一做 ⇒ 联网和 `-SelfTest` 两条路都生效；跳过的计数会分列打印（多人/模组角色/读不到角色模组）
**分组口径修正**（用户 2026-10-31：「你这是把所有的战士、猎人等不同 boss 混在一起了吗？我们目前有 18 个」）：
表里的 `boss` 列只有**角色**（`SILENT_BOSS` / `DEFECT_BOSS` … 共 5 个）⇒ 三个静默猎手变体全被合并。
真正的 18 只是**变体键**，写在 `lineup`（第 0 位 = 主 Boss）与 `bossList`（多人局全部）里。
- 默认 `-By variant`（新增）：按变体分组，能出 18 行；`-By variant+players` / `-By variant+charmod` 同理
- `-By boss` 保留（看“哪个职业当 Boss 最难”时有用），但文档里注明它会合并变体
- 新增 `-ExplodeMulti`：多人局把该场记到**场上每只** Boss 名下（默认只记阵容第 0 位那只）
- 标题改成「按 X；共 N 场对局、M 个分组」，一眼看出分组数量对不对
**排版对齐**（用户 2026-10-31：「你这个好像没对齐」）：`-f` 的宽度按**字符数**算，中文字符实际占 2 列 ⇒ 表头与数据错位。
改为用「显示宽度」自己补空格：`CharWidth/DispWidth/PadL/PadR/CutDisp`（CJK、全角标点、`…` 都按 2 列算），
并加了分隔线；分组名超 26 显示列就按显示宽度截断加 `…`（CSV 里仍是全名）。
**自检开关** `-SelfTest`：不连飞书，用 5 行假数据验证统计与排版（沙箱没外网时也能验）。
已验证：三种分组、CSV 导出（`.tmp\stats-test.csv` 已测后删除）、超长分组名截断加 `…`（CSV 里仍是全名）。
BOM 已补（PS 5.1 才认中文）。

用法：
```
powershell -ExecutionPolicy Bypass -File tools\feishu-stats.ps1
powershell -ExecutionPolicy Bypass -File tools\feishu-stats.ps1 -By boss+charmod -MinVersion 0.2.276
powershell -ExecutionPolicy Bypass -File tools\feishu-stats.ps1 -SelfTest -NoCsv     # 离线自检
```

## 2026-10-31 晚：新增 `tools\feishu-grant-permission.ps1`（把自己加成表格协作者）

用户：「我怎么获得编辑表格的权限？」

**根因**：这张多维表格是用**应用（机器人）的 tenant_access_token** 通过 API 建的 ⇒ **所有者是那个应用**，
用户的飞书账号默认只能"通过链接看"，所以网页上没有编辑按钮、也建不了视图。

**脚本**（读 cfg 拿 app_id/secret/app_token，纯 curl + 中文消息所以带 BOM）：
- `-Email you@example.com`（默认）或 `-MemberType openid|userid -MemberId …`
- 默认 `-Perm full_access`，可 `view` / `edit`
- `-TransferOwner`：直接调 `members/transfer_owner`（`remove_old_owner=false&stay_put=false&old_owner_perm=full_access`）
  把所有权转给用户 —— 之后网页上什么都能改
- 常见错误码直接给结论：`99991672` = 应用缺 `drive:drive`（或 `docs:permission.member:create`）权限，
  去开放平台加完「创建版本并发布」；`1064006` = 找不到该成员（邮箱要和你登录飞书用的一致）
- 结尾打印 `https://www.feishu.cn/base/<app_token>?table=<table_id>`，并提示用 `tools\feishu-check.ps1` 打真实租户链接

⚠ 万一应用没有云文档权限、又不想加：所有事情（清表 / 建列 / 导出）都能用脚本以应用身份完成，
**UI 权限只影响"你自己在网页上手动看/改/建视图"**。

### 追加（用户 2026-10-31：「能不能不用飞书邮箱？我看开通好像还要买域名」）

- **飞书不需要买域名**：免费版/团队版自带 `<团队>.feishu.cn` 子域，`xxx.feishu.cn/base/...` 就在它下面；
  要买域名的只有「企业自定义域名」这种高级功能，与本模组无关。
- 脚本改为**默认走链接分享**，完全不需要邮箱/成员信息：
  `PATCH /open-apis/drive/v1/permissions/{app_token}/public?type=bitable` body `{ link_share_entity: "tenant_editable" }`
  ⇒「组织内拿到链接的人**可编辑**」；`-LinkShare anyone_editable / tenant_readable / closed` 可选，
  成功后会再 `GET .../public` 把当前的 `link_share_entity` 打出来确认。
- 邮箱/`-TransferOwner` 变成**可选**（有 open_id 也能用 `-MemberType openid`）。

### 追加：过滤语义改成 AND

用户跑 `-MinVersion 0.2.276 -KeepKind boss_fight` 时把路径写成了 `...\tools\feishu-clear-table.ps1`（占位符），
顺便发现原来的**OR**语义不对：两个条件同时给时，只要满足其一就保留（新版 diag 也会留下）。
现在改成 **AND**：给了几个条件就必须同时满足 ⇒
`-MinVersion 0.2.276 -KeepKind boss_fight` = **只留「v0.2.276+ 的对局行」**，旧版本行与所有 diag 行都删。

## 2026-11-01 晚：查「进战斗时闪退」——是老毛病（coreclr 访问冲突），不是 v0.2.286/287 引起的

用户：「进入战斗失败了？」。查证（全部实证，不是猜）：

- `godot.log` 本次会话（21:14 起）**没有**正常退出的 `... leaked at exit` 收尾 ⇒ 是**进程崩溃**（CTD），不是游戏内的逻辑失败
- `%LOCALAPPDATA%\CrashDumps\SlayTheSpire2.exe.32108.dmp`（57.4 MB，21:16:21）
  → 崩溃线程 17232、异常码 **0xC0000005（访问冲突）**、地址 `0x7FFFB7C89302`、
  **归属模块 `coreclr.dll (+0x2E9302)`**（.NET 运行时内部），当时加载 248 个模块
- Windows 事件日志（Application Error）佐证：`出错模块名称: coreclr.dll`、`异常代码: 0xc0000005`
- **同一签名 30 天内已出现 5 次**：10-10、10-19、10-20、10-24、11-01（另有 3 次 0xC00000FD 栈溢出：10-30）
  ⇒ **早于** v0.2.286/287，属于这台机器长期存在的闪退模式，与本次只改数字/文本无关
- 崩溃前 2 秒我们最后一行动作是「顶栏 Boss 图标：DEFECT_DARK → 用角色头像」；同样的日志在 20:03、20:09
  两个**没崩**的会话里也出现过 ⇒ 不是触发点。本次会话 mod_log 里没有任何异常/失败行

**新增工具**：`tools\read-crash-dump.ps1` —— 纯 PowerShell 解析 minidump，打印
「崩溃线程 / 异常码（含中文名）/ 异常地址 / 归属模块 / 模块总数 / 当时加载的本地 mod」。
⚠ 踩坑两条：`MINIDUMP_MODULE` 固定 108 字节（`VS_FIXEDFILEINFO` 52 字节，我一开始只跳了 32）；
Hashtable 里 **uint32 键和 int 键 Equals 不相等** ⇒ 键要 `[int]` 转换，异常码要用 `'{0:X8}'` 字符串比对。

**下一步（待用户配合）**：先确认能否稳定复现、崩在哪一步；再 A/B（先禁 `STS2_MCP`/`STS2MenuControl`
这些注入型 mod，再禁本模组，最后二分工坊模组）。
## 2026-10-31 晚：v0.2.287 —— 血量曲线：一层 −30 / 二层不变 / 三层 +30

用户：「目前 1 层 boss 胜率显著偏低（最低 43%），所以调整涅奥的祝福：拾起 +120；每进入新的一幕 +180
（相当于 1 层 −30 血、2 层不变、3 层 +30 血）。记得改所有 1、3 层 boss 生命值，并且记得考虑根除骨的布质果实。」

**改动**（`src/Hooks/Relics/NeowBlessing.cs`）：
- 新增两个常量：`PickupMaxHpBonus = 120`（原 150）、`PerActMaxHpBonus = 180`（原 150），
  `MaxHpBonus` 保留为 `PickupMaxHpBonus` 的别名防其它引用编译不过
- `AfterObtained` 用拾起值、`AfterActEntered` 用每幕值（之前两处都读同一个 150 常量）
- 本地化 `content/localization/{zhs,eng}/relics.json` 的 `NEOW_BLESSING.description` / `eventDescription` 同步 120/180

**血量不需要逐个手改** —— `HpForAct` = 角色起始血量 + 该幕的 Hp 数组加成，逐幕增量全来自涅奥祝福：

| 角色（基础血量） | 一层（+120） | 二层（+300，不变） | 三层（+480） |
|---|---|---|---|
| 铁甲战士 80 | 200（原 230） | 380 | 560（原 530） |
| 静默猎手 70 | 190（原 220） | 370 | 550（原 520） |
| 故障机器人 75 | 195（原 225） | 375 | 555（原 525） |
| 亡灵契约师 66 | 186（原 216） | 366 | **577**（66+480+**31 布质果实**，原 547） |
| 储君 75 | 195（原 225） | 375 | 555（原 525） |

顺带把散落在 `BossDefs` / `BossDefinition` / `DefectScript` / `NecrobinderAct3Script` / `SilentBrewerScript`
注释里的旧血量算式（「150×3 = 525/520/547…」）全部改成新值，避免以后又被注释误导。

版本 **v0.2.287**；PCK 已重打（本地化变了）并抽包验证；
三处 SHA256 一致：dll `B3AC3AB8F7485EDA`、json `851B94C5E750761F`、pck `A96545C852A7F4E5`。
## 2026-10-31 晚：v0.2.286 —— 根除骨少 31 点最大生命（布质果实没生效）

用户：「布制果实没有正确额外增加根除骨的最大生命值。把她的最大生命值增加 31（增加到 547）」。

**根因**：`BossDefinition.HpForAct(act)` 以前**无条件**返回 `CharacterModel.StartingHp`，
把 `BossDefs.Necrobinder` 里 `Hp = new[] { 66, 66, 97, 66 }` 那个 **97（第三幕 = 66 + 31 布质果实）** 整个吃掉了：
- 拾起 `NeowBlessing` +150、每进一幕再 +150（`NeowBlessing.MaxHpBonus`）⇒ 第三幕 = 基础 + 450
- 所以 66 + 450 = **516**（实际），97 + 450 = **547**（设计）
- 那 31 点原本指望遗物「拾起时」钩子给，但影子玩家收不到那个钩子（`NecrobinderAct3Script` 的注释也写了
  「所以 Hp 定义里不要再加」——写是写进数组了，但读取时被无视）

**修法**：`HpForAct` = `StartingHp + (Hp[幕] − Hp[0])`
- 根除骨第三幕：66 + (97−66) = 97 ⇒ 97 + 450 = **547** ✓
- 其它 Boss 的数组各幕都等于基础血量（80/70/75/75）⇒ 加成 0，**行为完全不变**
- 读不到 `StartingHp` 时仍退回数组值

版本 **v0.2.286**（DLL `7DB2AFA7145E0479`），已装进游戏 + 同步 workshop。
验证：第三幕打根除骨，血条应是 **547**（日志里 `BossDefValidator` 的 `HP=66,66,97,66` 不变）。

## 2026-10-31 晚：v0.2.285 —— 修「共用匹配池好像不起作用，一直给替换的 boss」

玩家反馈：开了「与原本 Boss 共用匹配池」却永远打角色 Boss。

**查证过程**：用 C# 把 `BossLineups.Roll` 原样跑 10 万种子 × 3 幕 ⇒ 分布是 50/50，**看起来没问题**；
但把种子换成**退化值**（读不到本局种子时的 `1` 和 `FreshSeed()`）后真相出来：

```
seed=1        → act0/1/2 = 1,1,1   （全是角色 Boss！）
FreshSeed     → act0/1/2 = 0,0,1
```

**两个真 bug**：
1. `Roll` 用 `h % count` 取的是**最低比特**，而三项系数都是奇数 ⇒ 种子退化成常量时
   三幕的奇偶性由 `(seed + act + salt)` 决定，**一起落到同一侧**。
2. `ReadSeed` 只走反射 + `Convert.ToInt64`：`Seed` 若是 `ulong` 且超出 long 范围会抛异常 →
   被 catch 吞掉 → 返回 0 → 走退化种子。而 `Telemetry.SafeSeed` 用强类型 `state.Rng.Seed` 一直能读到。

**修法**：
- `Roll`：多混一轮（`h ^= h>>29; h *= 0xc2b2ae3d27d4eb4f; h ^= h>>32`）再取模；常量种子下 act0/1/2 变成 0,0,1 / 0,1,0，分布仍 50/50
- `ReadSeed`：先走强类型 `IRunState.Rng.Seed`，失败再反射；新增 `ToSeed()` 安全转换（ulong/long/int/uint/string，不用 Convert 抛异常）
- 诊断：种子变化时 `ResetSharePoolLog()`（以前整个进程只记一次，开第二局就再也看不到抽取日志）
  + 每局打一行 `共用匹配池 = 开/关（种子 …）`
- 版本 **v0.2.285**（DLL `7EA3FA425B0D372D`），已装进游戏 + 同步 workshop

## 2026-10-31 晚：v0.2.284 —— A3=②「一代 Boss 曲」对**所有** Boss 生效（按幕挑曲）

用户：「A3 选择使用一代曲目时，所有 boss 都会使用一代音乐，而不只是 3 个机器人（都使用自己所在层的）」。

- 新增 `Gen1Ogg(int act)`：`act 0/1/2 → STS_Boss1/2/3_NewMix_v1.ogg`，四层起沿用 Boss3
- 新增 `Gen1OggFor(encounter, act)`：先查 `OggByEncounter`（个别覆盖），再按幕
- `TryPlayGen1(string? encounterEntry, int act)`：不再「查不到就 false」；只有 **ogg 载入失败**才返回 false
  ⇒ 调用方（`OnCombatStart` 的 A3=② 分支、`PlayVanillaOther` 的 FMOD 找不到兜底）都传了 act
- 删掉「一代音乐只覆盖那三只故障机器人」的旧注释
- 版本 **v0.2.284**（DLL `76A18FA8F2DA2C90`），已装进游戏 + 同步 workshop
- 素材来源（给用户剪视频用）：一代三首原文件在 STS1 的 `desktop-1.0.jar` → `audio/music/STS_Boss{1,2,3}_NewMix_v1.ogg`，
  已解到 `tools\art\audio\sts1\`（另外还导了 `STS_Act4_BGM_v2.ogg` / `STS_Boss4_v6.ogg` / `STS_BossVictoryStinger_4_v3_MUSIC.ogg`
  —— 用户要的「心脏战 BGM」；**STS2 里没有心脏 Boss**，只能用一代的）

## 2026-10-31 晚：v0.2.283 —— 奇巧贼第 5、7 回再调（都由「打击」改「防御」）

- **第 5 回**：后空翻、早有准备+（弃**打击 + 防御**）、**防御**、中和、隐秘匕首（手牌已空，不弃牌）
  ⇒ 费用 后空翻1 + 防御1 = 2
- **第 7 回**：杂技（弃后空翻）、后空翻（奇巧）、早有准备+（弃打击×2）、**防御**、中和、隐秘匕首（弃打击×2）
  ⇒ 费用 杂技1 + 防御1 = 2（原来是杂技 + 打击）
- 版本 **v0.2.283**（DLL `C894BFD371A6E56F`），已装进游戏 + 同步 workshop

## 2026-10-31 晚：v0.2.282 —— 奇巧贼再削（愧疚 / 双防御）

用户：「怎么感觉还是强，匕首雨换愧疚（诅咒），第八回合打 2 张防御」。
- 第 4 回：`DaggerSpray` → **`Guilty`（愧疚，`GUILTY` = 诅咒，0 费）**
- 第 8 回：`DaggerSpray + 防御 + 防御(没费)` → **`DefendSilent × 2`**（1+1 = 2 费，两张都打得出去）
- 版本 **v0.2.282**（DLL `012FEC3813B0747A`），已装进游戏 + 同步 workshop

## 2026-10-31 晚：v0.2.281 —— 奇巧贼剧本第三次修订

用户新稿（逐回合）落地的改动：

| 回 | 变化 |
|---|---|
| 2 | 翻越撑击排到余像/鬼祟**之前**；单人末尾多一张「防御（没费）」；多人改成 打击 → 防御 |
| 3 | 补回「打击（没费）」+「笨拙（0 费诅咒）」 |
| 4 | 打击 → **匕首雨**（`DaggerSpray`，1 费，没费） |
| 5 | 早有准备+ 改丢**防御×2**；**隐秘匕首不再弃牌**（此时手牌已空，用户明确） |
| 7 | 隐秘匕首改丢「防御 + 打击」 |
| 8 | 打击 → **匕首雨** |
| 1 / 6 / 9 | 不变 |

- 第 2 回合的实现：`C<Prepared+>`, `FlickFlack(discard)`, `Afterimage(discardInsteadWhenMultiplayer)`,
  `StrikeSilent`, `DefendSilent(discardInsteadSingleOnly)`, `DefendSilent`（多出来的那张在单人里是“没费”，多人里多显示一张但不影响费用）
- 进阶 9 升级项仍是 翻越撑击 + 连续反弹
- 版本 **v0.2.281**（DLL `31A57FFBF0292117`），已装进游戏 + 同步 workshop

## 2026-10-31 晚：v0.2.280 —— 奇巧贼进阶 9 升级项改回「翻越撑击 + 连续反弹」

用户：「进阶 9 改为升级翻越撑击和连续反弹」。
- 第 2/6 回合的 `FlickFlack` 两处都加 `upgradeMinAscension: 9`
- 第 3/9 回合的 `Ricochet` 保持 `upgradeMinAscension: 9`
- `HiddenDaggers` 去掉该标记（不再随进阶升级）
- 版本 **v0.2.280**（DLL `6CB985432536C956`），已装进游戏 + 同步 workshop
- ⚠ 踩坑记录：用 `[System.IO.File]::WriteAllText` 做批量字符串替换时，**第二处** `FlickFlack`
  因行尾/缩进不同没被替换到，只能再用 edit 逐处确认 —— 批量替换后一定要 `Select-String` 复核每处命中。

## 2026-10-31 晚：v0.2.279 —— 奇巧贼整套剧本按用户新稿重写（削弱）

用户给了 9 回合完整剧本 + 「进阶9改为升级连续反弹和隐秘匕首，其它不变」。

| 回 | 内容（2 费/回合） |
|---|---|
| 1 | 速行者+（2）；打击×2、防御×2 没费（**去掉偏折**） |
| 2 | 早有准备+（0，升级后丢 2 张）→ 单人：余像(1)+打击(1)，丢翻越撑击+防御；多人：鬼祟+(奇巧)+翻越撑击(奇巧)被丢，打防御(1)+打击(1) |
| 3 | 杂技(1) 丢连续反弹、生存者(1) 丢触不可及（两张奇巧免费打出）；**去掉笨拙与打击** |
| 4 | 谋划专家+(1) + 尖啸(1)；打击没费（**去掉铭记死亡**） |
| 5 | 后空翻(1) + 早有准备+（丢打击+防御）+ 打击(1) + 中和 + 隐秘匕首（再丢防御+打击） |
| 6 | 洗牌。早有准备+（丢翻越撑击+触不可及）+ 防御 + 打击（**去掉铭记死亡**） |
| 7 | 杂技丢后空翻（奇巧）+ 早有准备+（丢打击×2）+ 打击 + 中和 + 隐秘匕首（丢打击×2） |
| 8 | 打击 + 防御 + 防御（第二张没费，**去掉偏折**） |
| 9 | 防御 + 生存者（丢连续反弹） |

- 循环仍是 6～9（`LoopFrom=6`）
- **进阶 9+**：连续反弹、隐秘匕首 用 `ScriptCard.UpgradeMinAscension = 9`（`C<>` helper 新增参数），
  单人/多人都生效 —— 没动 `MultiplayerCardSwap`（那条只管多人换牌）
- ⚠ 第 2 回合单人/多人**顺序不一致**（用户稿：单人「余像」在早有准备+ 之前，多人「早有准备+」在最前）。
  一张剧本表只能有一种顺序 ⇒ 按**多人**顺序（早有准备+ 在前）；单人只是余像显示在它后面，费用不变
- 隐秘匕首要弃 2 张，用户新稿没写弃什么 ⇒ 沿用旧稿（第 5 回丢防御+打击、第 7 回丢打击×2），否则选择器会乱弃
- 版本 **v0.2.279**（DLL `2D48EBABC5E26B94`），已装进游戏 + 同步 workshop

## 2026-10-31 晚：v0.2.278 —— 去掉 `mode` 列（用户反悔，直接用 `players`）

用户：「感觉不需要 mode，直接看玩家人数就行了。把它去掉」。

- `Telemetry.FinalizeFight`：删掉 `mode` 字段与 `playersNow`，`players` 恢复成 `SafePlayers()`
- 字段白名单、`tools\feishu-add-columns.ps1` 都删掉 `mode`（脚本里留了一行注释说明来龙去脉）
- 表里若已建了 `mode` 列，**留着也只是空着**，用户在 UI 里删掉即可
- 版本 **v0.2.278**（DLL `F09F4839CCA054C5`），已装进游戏 + 同步 workshop
- 分组建议改为：`players = 1` 即单人；`players > 1` 即多人

## 2026-10-31 晚：v0.2.277 —— 数据加 `mode` 列（solo / mp）

用户：「目前数据看起来有点杂，能不能把多人和单人分开，然后单人中把每个 boss 分开？」

- `Telemetry.FinalizeFight` 新增 `mode` 字段：`SafePlayers() <= 1 ? "solo" : "mp"`，
  并把 `players` 也改成同一个数（以前两处各算一次）
- 白名单 / `tools\feishu-add-columns.ps1` 都已加（文本列）
- 看板用法（不用改代码）：
  - 分组：先按 `mode`（solo / mp），再按 `boss` —— 单人局每行只有一只 Boss，就是一屏一个 Boss
  - 多人局一场好几只 ⇒ 用 `bossList` / `bossTotal` / `hpList`，`boss` 只是阵容第 0 位
  - 只看完成局：筛 `result` 非空（aborted 不上传，本来就不会进表）
- 版本 **v0.2.277**（DLL `5BF1C55E32E6619E`），已装进游戏 + 同步 workshop

## 2026-10-31 晚：`tools\feishu-clear-table.ps1` 新增 `-MinVersion`

用户：「因为之前版本的失败都没能上传成功，导致数据里只有赢。给我把版本低于 v0.2.276 的记录删掉的代码」。

- 新参数 `-MinVersion 0.2.276`：只保留 `mod` 字段里版本 **>= 该值** 的记录，其余删除
  （`mod` 形如 `v0.2.276 / 2026-10-31（…）`，用正则 `v?(\d+)\.(\d+)\.(\d+)` 取版本）
- **读不到版本号的记录不删**（单独报数），避免误伤
- 与 `-KeepKind` 可组合：命中任一「保留」条件就留下
- 用法：
  ```
  powershell -ExecutionPolicy Bypass -File tools\feishu-clear-table.ps1 -MinVersion 0.2.276 -DryRun   # 先看要删多少
  powershell -ExecutionPolicy Bypass -File tools\feishu-clear-table.ps1 -MinVersion 0.2.276            # 真删
  ```
- 分类计数：`版本过滤：>= X 保留 N 条；无版本号保留 M 条；将要删除的是低于 X 的 K 条`

## 2026-10-31 晚：v0.2.276 —— 真修「战败局不上传」（v0.2.269 那版没修到点上）

用户实测：一局故意被打死（没上传）、一局控制台秒了对面（上传成功）。

**本机证据**（`charbosses-telemetry.jsonl`）：
```
15:15:47 boss_fight  result=<空>  lineup=aborted:console:DEFECT_DARK  turns=3   ← 被打死那局
15:16:06 boss_fight  result=win   lineup=console:DEFECT_DARK          turns=1   ← 控制台秒杀
```
`mod_log.txt` 同一时刻：`这一场**没有真正打完**…（Boss 1 只，其中死 0 只；真人玩家 0 人、全灭/未知）`。

**为什么 v0.2.269 没生效**：那次只给 `FinalizeFightAtCombatEnd`（`Hook.AfterCombatEnd`）传了 `runState.IsGameOver`，
但战败这一局实际是走 **`BeforeRemovedFromRoom` → `NoteBossEnded`** 那条路径收尾的（房间被拆时 Boss 也被移出），
那条路调用 `FinalizeFight` 时 `playerCount = 0`、`gameOver = false` ⇒ `lost` 依旧不成立 ⇒ aborted。

**这次的修法（改在 `FinalizeFight` 里，两条路径都经过它）**：
新增 `RunPlayersAllDead(out int count)` —— 直接读 `IRunState.Players` 各自 `Creature.IsAlive`：
- 全部读到且都死了 ⇒ **战败**（记录 + 上传）；
- 还有人活着 / 一个都读不到 ⇒ 不下结论，仍按 aborted（避免把 SL / 控制台结束误判成战败）。
并打一行 `数据记录：判负兜底 ⇒ 本局玩家 N 人、存活=…、整局已结束=…、战斗里数到 M 人/… ⇒ 按战败/仍按未打完`，
下次一看日志就知道判据对不对。

版本 **v0.2.276**（DLL `F9B24BE735C8763D`），已装进游戏 + 同步 workshop。
⚠ 注意：用户 23:15:00 那次日志里有 `配置：上传游玩数据 → 关（只写本机）`，说明中途关过上传开关，
排查时别把它当成 bug。

## 2026-10-31 晚：v0.2.275 —— 飞书 `99991663 Invalid access token` 自愈

用户贴日志：`[CharBoss] 飞书多维表格写入失败：{"code":99991663,"msg":"Invalid access token..."}`。

**原因**：`Telemetry.FeishuToken()` 把拿到的 `tenant_access_token` 缓存 100 分钟，**飞书那边提前作废**
（作者重置 secret、后台刷新、多端抢用）时，客户端会一直拿着坏令牌重试到进程结束；
另外旧代码在「取不到 token」时**什么都没清**，等于失败也被"记住"。

**修法**：
- `UploadToFeishuBitable` 拆成「外层重试 + `TryUploadToFeishuBitable`」：
  命中 `99991663` / `99991668` / `Invalid access token` ⇒ `InvalidateFeishuToken()` →
  重新取令牌 → **同一批再发一次**
- `FeishuToken()`：取不到 token / 抛异常时**一律清缓存**，并把飞书原文写进日志
  （提示「若是刚重置过 app_secret，说明这个客户端还是旧 secret ⇒ 更新模组」）
- 顺带核对：本机 cfg 里的 secret 与代码内置常量**一致**（`elCCQa…`，32 位），说明作者没换过 secret；
  那这次失效就是令牌被作废，重取即恢复。

版本 **v0.2.275**（DLL `5AB74B287E7ED688`，三处一致，DLL 内版本串已确认），已装进游戏 + 同步 workshop。
⚠ 本轮踩坑：`BuildInfo.Tag` 里不能写**半角双引号**（会把 C# 字符串截断）—— 已改用「」。

## 2026-10-31 晚：v0.2.274 —— 奇巧贼第 3 回合把多的 1 费花在「生存者」

用户：「这样第三回合多了 1 费，那一费给生存者（丢弃触不可及）、触不可及（奇巧）」。

第 3 回合最终形态（费用正好 2）：
```
杂技(1) → 连续反弹（被丢，奇巧免费打出）
        → 笨拙(0)
        → 生存者(1，丢触不可及) → 触不可及（被丢，奇巧免费打出）
        → 打击（没费）
```
核对：`Untouchable` 在 sts2.dll 里 `CanonicalKeywords => CardKeyword.Sly`（原生奇巧），
所以 `discardInstead + costOverride: 0` 正确，不需要额外 `sly: true`。
版本 **v0.2.274**（DLL `9DA51B2FD2EF16ED`），已装进游戏 + 同步 workshop。

## 2026-10-31 晚：v0.2.273 —— 奇巧贼第 3/4 回合定稿（笨拙 / 尖啸）

用户：「把狩猎和尖啸的位置换一下，然后狩猎改笨拙」。
⇒ 第 3 回合：`Acrobatics` + **`Clumsy`（笨拙，诅咒牌，0 费）**；
   第 4 回合：`MasterPlanner+` + **`PiercingWail`（尖啸，回到 2026-10-26 的样子）**。
卡组里把上一版加的 `TheHunt` 去掉了（剧本不再用它）。
⚠ 第 3 回合现在只花 1 费（杂技），剩 1 费不消耗 —— 剧本只打列出来的牌，闲置能量不会自己找牌打。
版本 **v0.2.273**（DLL `5DAA5025F0A06244`），已装进游戏 + 同步 workshop。

## 2026-10-31 晚：v0.2.272 —— 平衡：削弱奇巧贼 / 增强黑球机

用户口径：
> 「削弱奇巧贼：尖啸换成狩猎，灵动步伐换成尖啸。增强黑球机，其特殊效果修改为：
>   战斗开始时，将1张冷光加入你的手牌……后面不变，描述和效果都改。为其添加遗物：数据磁盘。」

**① 奇巧贼（`SILENT_SLY`，`Core/SilentSlyScript.cs`）**
- 第 3 回合：`Footwork`（灵动步法，+2 敏捷）→ **`PiercingWail`（尖啸）**
- 第 4 回合：`PiercingWail` → **`TheHunt`（狩猎，10 伤害）**
- 卡组补 `TheHunt`；费用不变（杂技1+尖啸1 / 谋划专家+1+狩猎1，三张都是 1 费）
- 剧本 note 文字同步改

**② 黑球机（`DEFECT_DARK`）**
- 特殊效果「冷却剂」：`BossHookBridge.SideTurn.GrantCoolantLuminesceAtCombatStart` 从
  「手牌 + 抽牌堆各 1 张」改成 **只给 1 张、只进手牌**；日志同步改写
- 本地化 `content/localization/{zhs,eng}/powers.json` 的 `BOSS_COOLANT_POWER.description` 改为
  「战斗开始时，将 1 张「冷光」加入你的手牌。」/ `add 1 Luminesce to your hand`（后半句不动）
- 遗物表（`Core/DarkOrbBossScript.Relics()`）加 **`DataDisk`（数据磁盘）**——
  战斗开始 +1 集中由既有的 `GrantDataDiskFocusAtCombatStart` 自动兑现（它按「身上真有这件遗物」判断）
- `Powers/BossCoolantPower.cs` 与 `DarkOrbBossScript` 的文档注释一并改正（原来写的是 3 张冷光+）

**PCK 已重打**（content/ 里的本地化变了）并验证：从 `.build\CharacterBosses.pck` 里
`res://CharacterBosses/localization/zhs/powers.json` 抽出来确认新描述在内。

版本 **v0.2.272**；三份（`src/bin/Release` / 游戏 `mods\CharacterBosses\` / `workshop\content`）SHA256 一致：
dll `9B96579DC232E141`、json `5A56D593923589B1`、pck `BEB650475E9F9AA5`。

## 2026-10-31 晚：v0.2.271 —— 数据只收「我们自己 Boss 战」的

用户贴表：`act=4.0` + `SUKUNA-SUKUNA_GAU…`，以及一条
`⚠ 出牌队列滞留：TOJI-QUICKSTEP（拥有者 伏黑甚尔）…` ⇒「怎么别的模组的第四幕也上传过来了」。

**根因**：`Diagnostic()` 在**不在我们 Boss 战**时也是「立即写 + 上传」，而它填的
`act` / `encounter` 取的是**当前房间** ⇒ 玩家在别的模组第四幕（Sukuna）或别的模组角色战斗里出问题，
那条日志就带着别人的 act 4 / 遭遇进了我们的表。触发者是我们自己的两个全局诊断：
1. `Telemetry.Diagnostic`（任何含 失败/异常/不同步/⚠ 的日志）
2. `BossFrameTickPatch.WatchPlayQueue` 的「出牌队列滞留」看门狗（**全局扫 `NCardPlayQueue`**，
   所以能看到别的模组的卡与角色名）

**修法**：
- `Diagnostic`：`InBossFight()` 为假 ⇒ `Write(..., queueForUpload: false)`，**只写本机、绝不上传**；
  diag 行补 `fight` 字段（`boss` / `none`）
- `WatchPlayQueue`：先判 `BossShadowPets.BossFightActive(当前战斗)`，不是我们的战斗就直接清表返回

版本 **v0.2.271**（DLL SHA256 `7E4809E775695AD3`），已装进游戏 + 同步 workshop。
本机 jsonl 里 `TOJI`/`SUKUNA` 命中 0 条（说明这些行来自已经更新到 v0.2.270 之前的玩家机器）。

## 2026-10-31 晚：v0.2.270 —— 三条「设计如此」的报错停止上报 + BGM 退让链

用户看完 5 条 diag（PAELS_LEGION NRE / VEXING_PUZZLEBOX 空序列 / PHYLACTERY_UNBOUND 召唤抛错 /
两行 BGM 找不到瀑布巨兽）后的决定：
> 「这几个除了 bgm 好像都是当时设计的时候因为原版的走不通另辟蹊径搞出来的（把原版的功能拦了自己实现），
>   暂时不动它们（也让玩家那边不再上传它们），修 4」

**① 不改行为，只停上报**（`Telemetry.Diagnostic` → 新增 `IsKnownDesignNoise`）：
含 `遗物 PAELS_LEGION` / `遗物 VEXING_PUZZLEBOX` / `遗物 PHYLACTERY_UNBOUND` 或
`Attempted to add a creature outside of combat` 的日志**不再进 diag/上传**；
本机 `mod_log.txt` 仍照旧写（排查用）。

**② BGM 退让链**（`BossBgm.PlayVanillaOther` + 新增 `EventCandidates`）：
以前 `EventAvailable` 为假就直接放弃（保持原版音乐）。现在按顺序试：
1. 原曲事件；
2. **换 bank 命名**的同一首（`act1_b_boss_x` ↔ `act1_boss_x`，一代二幕两套 bank 就是这个差别）；
3. **本模组自带的一代 ogg**（`TryPlayGen1`，资源文件不依赖 bank，只有三只故障机器人有）；
4. 表里其它曲目（按表顺序，结果稳定）；
5. 全不行才不接管。
换曲时会打日志说明「原曲 X 在本幕 bank 里没有 ⇒ 改用 Y」。

版本 **v0.2.270**（DLL SHA256 `A835D1AC336D8250`），已装进游戏 + 同步 workshop，
`changenote.txt` / `workshop.json` 已更新。
待验证：碎屑储 / 黑球机在"没有 act1_b bank"的地图上应能听到自定义 BGM（日志会写改用了哪首）。

## 2026-10-31 晚：v0.2.269 —— 修「表里一个 lose 都没有」

用户：「目前数据中一个失败都没有，是数据上传有问题还是玩家们确实没输过？」

**证据（本机 19 条 boss_fight）**：`win` 8 条、`result` 为空（aborted）11 条、**lose 0 条**；
且那些 aborted 行的日志都是 `真人玩家 0 人、全灭/未知` ⇒ `BossLockOn.RealPlayers(combat)` 在某些时刻返回空。

**代码上的真凶**：判负条件 `lost = !allDead && playerCount > 0 && !playersAlive`
（`Telemetry.FinalizeFight`）。战败时 `CombatState.Players` 可能**已经被清空/拆掉** ⇒ `playerCount = 0`
⇒ `lost` 不成立 ⇒ `aborted = true` ⇒ **aborted 从不上传** ⇒ 表里永远只有 win。

**修法**：
1. 玩家数取不到（0）时退回扫 `combat.Creatures` 里 `IsPlayer && Monster == null` 的生物
   （影子 Player 的 Creature 就是 Boss，必须排除）
2. 判负再加一条：`IRunState.IsGameOver == true`（整局已结束）也算败 —— 战败时这条基本必然成立
3. `FinalizeFight(boss, playersAlive, playerCount, bool gameOver=false)`；`TelemetryCombatEndPatch`
   现在把 `IRunState` 也传进来
4. 兜底日志加信息：`（Boss N 只、真人玩家 M 人、活着/全灭、整局已结束=True/False）`
   —— 下次玩家贴日志就能直接看出判负依据对不对

版本 **v0.2.269**（DLL SHA256 `26C9683832AD95AC`），已装进游戏 + 同步 workshop。
**待验证**：重启后故意让 Boss 打死一次，日志应出现 `数据记录（一场一行）：… lose …`，
`charbosses-telemetry.jsonl` 里 result=lose 且会正常上传。

## 2026-10-31 晚：v0.2.268 —— 作者署名改成「飞猪」

用户（截图：游戏内「已下载的模组」里 `Author: VibeCoding`）：「把作者改成飞猪」。

- `src/CharacterBosses.json`：`author` `VibeCoding` → **飞猪**（全仓库只有这一处出现 VibeCoding，已确认）
- 版本 `0.2.268` + `BuildInfo.Tag` 同步；DLL 重编（SHA256 `3DFFD371708C6ABA`），已装进游戏 + 同步 workshop
  （`CharacterBosses.json` 三份 SHA256 一致 `26AE9A8799F8`）
- `changenote.txt` / `workshop.json` 已更新
- 注意：**创意工坊页面的作者是 Steam 账号**，模组里这个 `author` 只影响游戏内模组列表/详情显示

## 2026-10-31 晚：v0.2.267 —— 数据收集加「玩家角色」与「角色所属模组」

用户：「增加上传的数据：玩家操控的角色，角色所属模组。因为有模组角色很超模会一定程度影响数据」。

- `pChar`：本场真人玩家操控的角色（`player.Character.Id.Entry`，多人逗号分隔，**顺序与 `pList` 一致**）
- `pCharMod`：该角色所属模组 —— 原版角色 = `vanilla`；模组角色 = `ModManager.GetLoadedMods()` 里
  `Mod.assemblies` 反查到的 `Mod.manifest.id`（有 `Mod.workshopId` 就拼成 `Downfall#3737335127`）；
  反查不到退回 `mod:<程序集名>`。实现 `Telemetry.CharacterModOf(CharacterModel)`
- 两个字段都在战斗开始的快照里取（`NoteCombatStarting`），随 `AppendPlayerHpFields` 一起写出行；
  白名单（`UploadToFeishuBitable`）已加，`tools\feishu-add-columns.ps1` 也加了这两列（文本类型）
- 版本 **v0.2.267**（DLL SHA256 `E72EA42F2B9AA976`），已装进游戏 + 同步 workshop

## 2026-10-31 晚：v0.2.266 —— 修那两个真 diag（佩尔的士兵 / 出牌前钩子）

对上一条里发现的两个真 bug（用户：「修吧」）。

**① `遗物 PAELS_LEGION 的 AfterSideTurnStart 失败: NullReferenceException`**
- 反编译确认 `PaelsLegion`（**原版遗物**，在 `VulnIroncladScript` 与 `RegentScript` 的遗物表里）：
  `BeforeCombatStart → SummonPet() → PlayerCmd.AddPet<Monsters.PaelsLegion>(Owner)` 召一只**纯装饰随从**
  （`IsHealthBarVisible=false`，无任何 hook 重写）；之后 `AfterSideTurnStart` / `AfterCardPlayed` 只是对这只随从
  触发 Sleep/Wake/Block 三种动画，外加 `Cooldown` 记账（`CanonicalVars` 里 `Turns=2`）。
  真正的战斗效果在 `ModifyBlockMultiplicative`（格挡倍率），**不依赖随从**。
- 影子玩家收不到原版 `BeforeCombatStart` ⇒ 随从永远不存在 ⇒ 每个 Boss 回合
  `Owner.PlayerCombatState.GetPet<PaelsLegion>().Monster` NRE；而且它是在 **Hook 广播链**里抛的
  ⇒ 同一链上后面的监听者一起哑掉。
- 修法：`BossHookBridge.SideTurn.EnsurePaelsLegionPet(shadow)` —— 战斗开始时，若她身上有该遗物且场上没有这只随从，
  就 `PlayerCmd.AddPet<Monsters.PaelsLegion>(shadow)`。Boss 死后仍由 `BossShadowPets.RemoveOnBossDeath` 摘掉
  （那段代码本来就是为它写的）。

**② `触发出牌前钩子失败: Sequence contains no elements`（本机 6 条）**
- 旧实现 `BossHookBridge.FireTurnStart` 直接调 `Hook.AfterPlayerTurnStart(combat, ctx, shadow)`：那是一条**广播链**，
  会对**整场战斗的每个监听者**派发（连真人玩家的遗物都会因 Boss 的回合被触发），
  且任何一件抛异常 ⇒ **后面的全哑**，我们只能 catch 到一句笼统日志。
- 修法：新增 `FirePlayerTurnStartPerModel` —— **只对影子玩家自己的** 遗物 + 能力 + 随从能力逐个派发
  Early/After/Late 三个阶段，每条单独 try/catch；`RunModelHook` 里同一件+同一钩子每进程只记一次日志，
  并**点名是哪一件**（`遗物 XXX 的 AfterPlayerTurnStart 失败: …（本场跳过，其余照常）`）。

版本 **v0.2.266**（DLL SHA256 `067F5CD167BEE399`），已装进游戏 + 同步 workshop，`changenote.txt` / `workshop.json` 已更新。
待用户重启游戏后在**易伤战 / 储君**（这两个带佩尔的士兵）验一次：日志应出现
`佩尔的士兵：战斗开始补召随从`，且不再出现那两条报错。

## 2026-10-31 晚：v0.2.265 —— 修「表里那些每分钟一条的脏数据」（诊断自喂死循环）

用户贴表：「你是不是把玩家 sl 的也上传了，这有脏数据啊，应该 boss 战结果出来再上传吧」。

**根因（自喂死循环，不是 SL）**：`FlushUploads` 成功后日志会打
`[CharBoss] 诊断记录 N 条 → 写入主表（失败/异常 与 不同步摘要）`，这句话同时含「失败」「异常」「不同步」
⇒ 满足 `Diagnostic()` 的 `worthKeeping` 过滤 ⇒ 自己变成一条 `diag` ⇒ 再上传 ⇒ 再打同一句…
（本机 jsonl 的 15 条 diag 里有 7 条正是这段文字，3~5 分钟一条，和表里空行一一对应；
每进程被 `_diagSeen` 拦一次、但「诊断记录 1 条 / 2 条」计数一变就是新 key ⇒ 每轮都能再进一条。）

**修法**：
1. `Diagnostic()` 直接丢弃自家遥测日志：含「诊断记录」「数据上传成功」「暂不上传」的一律 return
2. 战斗中的 diag **先攒进 `_fightDiag`（每场上限 6 条）**，在 `FinalizeFight` 写完 `boss_fight` 行之后再写
   ⇒ 只有真正打完（有 win/lose）才有 diag；`aborted`（中途退出 / SL 回滚）那场的 diag **只丢不传**
3. diag 行补 `result` / `boss`，能和对应那一场对上
4. `NoteCombatStarting` 里清空 `_fightDiag`（上一场没写完的残留丢掉）

**顺带从 diag 里发现两个真 bug（尚未修，待用户决定）**：
- `触发出牌前钩子失败: Sequence contains no elements` —— `BossHookBridge.FireTurnStart` 里
  `Hook.AfterPlayerTurnStart(combat, ctx, shadow)` 抛空序列（某个遗物/能力对影子玩家 `.First()` 落空）
- `遗物 PAELS_LEGION 的 AfterSideTurnStart 失败: NullReferenceException` ——
  `PaelsLegion` 是**原版遗物**（`MegaCrit.Sts2.Core.Models.Relics.PaelsLegion`），桥接替影子玩家触发时炸

清理已有脏行：`tools\feishu-clear-table.ps1 -KeepKind boss_fight`（只保留对局行）。
版本 **v0.2.265**，已装进游戏 / 同步 workshop，`changenote.txt` 与 `workshop.json` 已更新。

## 2026-10-31 晚：v0.2.264 —— 数据收集加「玩家侧血量」（区分轻松赢 / 险胜）

用户：「只有 boss 的血量相关数据…玩家击败 boss 就只有 win 和 boss 0 血，但艰难胜利和轻松胜利肯定有区别」，
并明确要求：玩家最大生命、进入 Boss 战血量、结束血量、**战斗中失去与恢复的血量分别记**
（避免缩放仪 Pantograph / 再生药水这类回血干扰判断）。

**新增 6 个字段**（只有 `boss_fight` 行有）：
`pMaxHp` 最大生命合计、`pHpStart` 进场血量、`pHpEnd` 结束血量、`pDmg` 战斗中掉的血、
`pHeal` 战斗中回的血、`pList` 每人「开局/上限-结束/上限」（如 `72/80-21/80;60/75-0/75`）。

**实现（两处同步钩子，绝不在帧回调里做统计）**：
- 新文件 `src/Hooks/Patches/TelemetryPlayerHpPatch.cs`
  - `Hook.BeforeCombatStart(IRunState, ICombatState)` 的 **Prefix** 拍快照（Prefix 是刻意的：
    缩放仪这类「开局回血」发生在战斗开始的钩子链里，要的是**回血之前**的进场血量）
  - `Hook.AfterCurrentHpChanged(IRunState, ICombatState, Creature, decimal)` 的 Postfix 累计
- `Telemetry.NoteCombatStarting` / `NotePlayerHpChanged` / `AppendPlayerHpFields`；
  只统计 `BossLockOn.RealPlayers`（影子 Player 的 Creature 就是 Boss 本体，必须滤掉），
  且 `creature.IsPlayer && creature.Monster == null`
- **符号已核对反编译**：伤害是 `Hook.AfterCurrentHpChanged(..., -UnblockedDamage)`（负）、
  治疗是 `(..., amount)`（正）⇒ `delta<0` 计 `pDmg`、`delta>0` 计 `pHeal`。
  且伤害传的是 **UnblockedDamage** ⇒ 格挡吸收的部分不算「掉血」，正合适。
- `NumericKeys` 与 `UploadToFeishuBitable` 的字段白名单都已加上这 6 项；
  `tools\feishu-add-columns.ps1` 也扩了 6 列（幂等，用户跑一次即可）
- 新工具 `tools\telemetry-tail.ps1`：把本机 JSONL 里最新几条 `boss_fight` 打出来（`-Count` / `-AllFields`）

版本 **v0.2.264**，已装进游戏、已同步 `workshop/content`、`changenote.txt` 与 `workshop.json` 已更新。
⚠ 游戏里正在跑的实例还是旧 DLL，**要重启才生效**；建列还需要用户跑一次 `feishu-add-columns.ps1`（要联网）。

## 2026-10-31 晚：上传报 `DllNotFoundException: steam_api64` 的根因与修法

用户跑 `& "..\..\tools\dotnet\dotnet.exe" "bin\Release\net8.0\ModUploader.dll" upload -w "..\..\workshop"` 报
`Unable to load DLL 'steam_api64' ... (0x8007007E)`。

**根因**：进程镜像是 `tools\dotnet\dotnet.exe`，所以 Windows 认为的「应用程序目录」是 **`tools\dotnet\`**，
而不是放 `steam_api64.dll` 的 `bin\Release\net8.0\` ⇒ 按名字加载必然失败。
用探针实测确认：`NativeLibrary.Load("steam_api64")` **按名失败、按完整路径成功**（两个 dll 都能 by-path 加载，
说明文件本身完好、不是位数/运行库问题；系统里 vcruntime140 / msvcp140 都在）。

**两个验证过的修法**：
| 法 | 命令 |
|---|---|
| a（推荐，已封装成脚本） | `tools\upload-workshop.ps1`：设 `DOTNET_ROOT=tools\dotnet` 后跑 apphost `bin\Release\net8.0\ModUploader.exe`（exe 自己所在目录就是 app dir） |
| b | 把 `bin\Release\net8.0` 前置到 `PATH`，继续用 `dotnet ....dll`（PATH 会被加载器搜到） |

新脚本 `tools\upload-workshop.ps1`（纯 ASCII）：没编译就先编译、缺 `steam_api64.dll`/`steam_appid.txt` 就从
`steam\` 补、设好 `DOTNET_ROOT` 再跑；`-Help` 透传 `upload --help`，成功后打印 `workshop\mod_id.txt` 里的物品 id。

## 2026-10-31 晚：新增 `tools\rotate-feishu-secret.ps1`（换 secret 一条命令）

```
powershell -ExecutionPolicy Bypass -File tools\rotate-feishu-secret.ps1 -NewSecret "<新 secret>"
#   -DryRun 只打印不落盘 / -NoBuild / -NoInstall / -NoSync / -SkipLocalConfig
#   -OldSecret "<旧值>" 可选：与源码里现存常量不符就直接中止（防拿错 secret）
```

自动做：① 校验新 secret（长度、无空白、非原值）→ ② 重算 base64 替换 `BuiltInAppSecretB64` →
③ 版本 `+0.0.1`（`CharacterBosses.json` + `BuildInfo.Tag` 两处，Tag 尾巴写明「旧 secret 已作废」）→
④ 顺带改本机 `charbosses_config.cfg` 的 `feishuAppSecret`（先 `.bak-<时间>`；**失败只警告不中止**，
因为源码此时已改完）→ ⑤ 编译 → ⑥ 装进游戏（`GameDir` 从 csproj 读）→ ⑦ 同步 `workshop\content` →
⑧ 字节扫 DLL 自检：新 base64 在（UTF-16 偏移）、旧 base64 不在、**明文 secret 不在**。

不做（脚本结尾会打印提示）：后台重置 secret、改 `workshop\changenote.txt`、跑
`tools\mk-workshop-json.ps1`、上传工坊。

脚本**故意纯 ASCII**（PS 5.1 把无 BOM 的 .ps1 当 ANSI 读，中文会把解析搞坏 —— 这次真踩到了：
全角括号出现在正则里导致整段解析失败）；必须进 C# 源的中文 Tag 尾巴用 base64 存。

已用假 secret 全流程实测：版本 0.2.263→0.2.264、编译通过、自检 `new at 640671 / old False / plain False`；
随后恢复源码、重编真 v0.2.263 并重新装进游戏 + 同步 workshop（DLL SHA256 三处一致 `846CBBC2EC841AF2`，
四个 base64 常量在 640613/640671/640761/640835，明文 secret 与假常量都扫不到）。

顺带修正：`BuildInfo.Tag` 里的日期写成 2026-11-01，实际系统日期是 **2026-10-31**，已改正。

## 2026-10-31 晚：v0.2.263 —— 修「玩家开着上传但飞书表里一条数据都没有」

用户：「已经有玩家在创意工坊下载并开启了数据上传，但表格里什么都没有」。**根因三条**（都在代码里，有行号）：

1. `BossConfig.cs` 的飞书六项默认值全是 `""`，DLL 里也没有内置值（当初为了安全**故意**没打进去）
   ⇒ 玩家端 `FeishuBitableReady`=false、`AnyUploadEndpoint`=false（`:563/:585`）
   ⇒ `TelemetryUpload` 恒 false（`:599`），`SetTelemetryUpload(true)` 还会被拒并记「只在本机记录」（`:623`）
   ⇒ `Telemetry.FlushUploads` 第一道闸 `if (!AnyUploadEndpoint) return;` **静默**返回（`Telemetry.cs:478`），玩家日志里连报错都没有。
2. `TelemetryConsentPrompt.cs:99` 只在配了通用 `telemetryUrl` 时才 `SetTelemetryUpload(true)`，**飞书四项被漏掉**
   ⇒ 玩家点「同意」也只会写本机。
3. 老玩家 cfg 早已生成（空串），反序列化会**覆盖**类默认值 ⇒ 光改默认值对老玩家无效；且 `consentAsked`=true 让他们**永远看不到第二次弹窗**。

**修法（用户拍板选 A：把端点内置，接受 secret 可被提取）**：

- `BossConfig.cs`：新增 `BuiltInAppIdB64 / BuiltInAppSecretB64 / BuiltInAppTokenB64 / BuiltInTableIdB64`（base64，
  只是不让 `strings` 一眼看到，**不是安全措施**）+ `BackfillBuiltInEndpoints(data)`：**只填空值、不覆盖玩家手填**，
  在 `Load()` 里调用并落盘；`WriteSnapshot` 补 `ConsentVersion`
- `BossConfig.cs`：新增 `consentVersion` 字段 + `NeedsConsentPrompt`（`!ConsentAsked || ConsentVersion < 1`）
- `TelemetryConsentPrompt.cs`：gate 改 `NeedsConsentPrompt`；`accepted && BossConfig.AnyUploadEndpoint` ⇒ 开上传；
  答完写 `SetConsentVersion(ConsentVersionCurrent=1)` ⇒ **老玩家会被再问一次**（当初那句「会上传」没兑现，现在补上）
- 版本 **v0.2.263**（`CharacterBosses.json` + `BuildInfo.Tag`），已装进游戏、已同步 `workshop/content`；
  PCK 未变（文案没改）。DLL 里能扫到 4 个 base64 常量（UTF-16 偏移 640613/640671/640761/640835），
  **明文 secret 在 UTF-8 与 UTF-16 里都搜不到**
- `workshop/changenote.txt` → v0.2.263（含「会再问一次」的用户可见说明）

**作者侧的运维约定（A 方案的代价）**：飞书自建应用**只授权这一张表**、**撤销 `base:app:create`**、
每 1~3 个月或发现异常时重置 secret、定期 `tools\feishu-export.ps1` 导 CSV 备份。
要彻底停掉上传 = 把四个 `BuiltIn*B64` 常量清空再发版。

**注意**：v0.2.263 目前只在本地装好，工坊上还是 v0.2.261 ⇒ 玩家要等下次上传才会拿到修复。

## 2026-10-31：创意工坊**描述定稿写进配置**，上传器已离线构建（等用户上传）

用户：「把英文中的 "My Bilibili: 疯狂的飞猪_" 删除，然后写进配置，准备上传」。

- `workshop/description.txt` —— **用户原文 6 段逐段对照**（灵感/崩坠 · 联机与跟踪锁定 · 18 个 Boss(5/5/8) · 数据收集 · 作者与致谢 · 素材声明），
  与用户「模组介绍的中文部分大概这样」那段**逐字一致**；英文段里删掉 `My Bilibili: 疯狂的飞猪_`，中文段保留 `作者B站：疯狂的飞猪_`
  - ⚠ 教训：上一轮我先把它写成自己编的「做了什么/可选设置/已知限制」结构（会话被压缩，没照用户原文）⇒ 用户当场指出。
    恢复方法：会话日志 `C:\Users\<user>\.dsh\sessions\<workspace>\session-*.jsonl.zstd` 是**多帧拼接的 zstd**
    （node 的 `zstdDecompressSync` 只解第一帧，必须按 magic `28 B5 2F FD` 切帧逐帧解），可搜回原文。
  - 目前描述**没有加任何 BBCode**（`[h2]/[b]/[list]` 一律没用），保持用户原文纯段落；要小标题/加粗再说
- `workshop/workshop.json` 由新脚本 `tools/mk-workshop-json.ps1` 重建（读 `description.txt` + `title.txt` + `changenote.txt`，
  手写 JSON 转义 ⇒ 中文**不以 `\uXXXX` 存储**，可直接读改）：title 28 字（玩家角色Boss / Characters Bosses）、
  description **3627 字 / 14 段**、changeNote 139 字；`dependencies: [3737335127]`（BaseLib），
  **不再写** `visibility` / `tags` / `contentDescriptors`（见下「上传地雷」）
- `changeNote` 现为 **v0.2.262**（`workshop/changenote.txt` 是它的源文件）
- 上传器**离线构建成功**：NuGet 缓存里已有 `Steamworks.NET 15.0.1` + `System.CommandLine 3.0.0-preview`，
  产物 `sts2-mod-uploader-main/sts2-mod-uploader-main/bin/Release/net8.0/ModUploader.dll`（要 .NET 8 运行时，本机 8.0.31 在）
- 素材复核：`image.png` 974,216 B；`previews/` 6 张最大 463,711 B（全部 <1 MB）；
  `workshop/content/CharacterBosses/{dll,json,pck}` 的 SHA256 与 `src/bin/Release*` / `.build` 一致，DLL 里版本串 **v0.2.262**（重编后；PCK 未变）

- **用户手动把工坊标题改成「玩家角色Boss / Characters Bosses」** ⇒ 已同步三处，避免下次上传改回去：
  `workshop/title.txt`（`mk-workshop-json.ps1` 的源文件）→ `workshop/workshop.json` 的 `title`；
  游戏内显示名 `src/CharacterBosses.json` 的 `name` 改成「玩家角色Boss (Characters Bosses)」；
  `BuildInfo.Tag` 与 `version` 一起到 **v0.2.262**，DLL 已重编、已装进游戏、已同步 `workshop/content`
- ⚠ **顺手拆掉的三个上传地雷**（看 `UploadCommand.cs`：键为 null 才跳过）
  `visibility` ⇒ 曾写死 `private`，会把已发布物品改回私有；`tags: []` ⇒ `SetItemTags` 会**清空**网页上选的 tag；
  `contentDescriptors: []` ⇒ 会把网页上勾的内容分级删掉。三者现在**一律不写进 json**，网页手动设置就不会被覆盖
- **工坊物品 id = `3830639796`**，记在 `workshop/mod_id.txt`（上传器把 mod_id.txt 写在**工作区目录**里，不是上传器项目目录）；以后更新认它
- `changeNote` 现为 v0.2.262（显示名变更说明，`workshop/changenote.txt` 是源文件）


**上传命令（Steam 需先启动并登录；沙箱无外网，用户自己跑；系统 `dotnet` 无 SDK ⇒ 用 `tools\dotnet`）**：
```
cd sts2-mod-uploader-main\sts2-mod-uploader-main
& "..\..\tools\dotnet\dotnet.exe" "bin\Release\net8.0\ModUploader.dll" upload -w "..\..\workshop"
```

## 2026-10-30 深夜：创意工坊**发布准备完成**（未上传）

用户：「我打算明天上 steam 创意工坊，你先做一下准备」。

**已备好**（`docs/plans/workshop-release-checklist.md` 是完整清单）：

- `workshop/workshop.json` —— 双语标题/描述（富文本 `/h2/h3/list/b` 标记）、`visibility: private`、
  `dependencies: [3737335127]`（BaseLib 工坊 id）、`changeNote`、tags 空（上传后在网页选最稳）;
- `workshop/image.png` —— 920×920 PNG / 131 KB（Steam 限 <1MB），由新脚本
  `tools/make-workshop-image.ps1` 生成（深色底 + 「角色当 Boss」+ 五个角色标签），可随时重跑/替换;
- `workshop/content/CharacterBosses/` —— DLL + 清单 + PCK，与 `src/bin/Release` / `.build` **SHA256 一致**;
- `src/CharacterBosses.json` 的显示名/描述改成双语（`id` 不变，仍是 `CharacterBosses`）。

**上传器环境修复**（明天不用再弄）：

| 问题 | 处理 |
|---|---|
| `global.json` 要求 .NET SDK 9.0.0，本机只有 10.0.401 ⇒ 直接 "A compatible .NET SDK was not found" | 改成 `10.0.0`（备份 `global.json.bak`） |
| csproj 的 `HintPath=steam\Steamworks.NET.dll` 但仓库只带 .nupkg | 从 nupkg 取出 `Steamworks.NET.dll`（netstandard2.1）放进 `steam\` |

⚠ 首次构建需要联网做 NuGet restore（`Steamworks.NET 15.0.1` + `System.CommandLine 3.0.0-preview`）；
沙箱无外网，只验证到「SDK 能解析」。

**安全检查**：DLL/PCK 里搜不到 旧飞书 secret（已失效，不再记录）、`APP_ID_REDACTED`、`BITABLE_APP_TOKEN_REDACTED`；
载荷**不含** `mod_manifest.json`（有玩家日志里那条 `mod_manifest.json missing 'id'` 的 ERROR 就是它造成的）。

**待用户拍板**：① 封面用排版图还是实战截图；② 是否加 `workshop/previews/`（额外展示图）；
③ tags（上传后在网页选）。

**明天上传命令**：
```
cd sts2-mod-uploader-main\sts2-mod-uploader-main
dotnet build ModUploader.csproj -c Release
dotnet run -- upload -w "..\..\workshop"
```
（Steam 必须先启动并登录；成功后生成 `mod_id.txt`，别删。）
## v0.2.242 → v0.2.248：一轮连续修复（已打包）

| 版本 | 修的东西 |
|---|---|
| v0.2.242 | 进阶 10 第三幕两场用同一套变体：`IsSecondSlot` 不再要求第二槽已存在（`visited>0` + 进阶≥10 且第三幕 ⇒ 第二场）；新增 `BossKeys.ComplementVariant` 硬保险（刀贼↔炼药贼 / 易伤战↔巨石战 / 无色储君↔辉星储） |
| v0.2.243 | cfg 写坏不再卡启动：`BossConfig.Current` 加锁只解析一次、「读取失败」只记一次、坏文件自动备份 `.broken-<时间>`（玩家手改少个逗号导致 1 秒 411 行日志 + 黑屏） |
| v0.2.244 | 数据降噪：性能统计/构建横幅不进 diag；同类 diag 每进程一次；修「重复移除已喝药水」抛异常；控制台点名战斗的 lineup 前缀 `console:` |
| v0.2.245 | 战斗结果三分：win / lose / **aborted**（双方都活着就结束 ⇒ result 留空 + lineup 前缀 aborted:） |
| v0.2.246 | 上传分流：杂项默认不上传；新增 `feishuDiagTableId`（想收杂项就写单独表），字段缓存按表区分 |
| v0.2.247 | 定稿：只上传 `boss_fight` + 「失败/异常」「不同步摘要」两类 diag；`consent` 与其它 kind 只留本机（`Write(queueForUpload:false)`） |
| v0.2.248 | 多人不重复上传：联机时**只有房主**上传对局数据（客人只写本机）；每条记录带 `role`（solo/host/guest） |

配套脚本：`tools\feishu-check.ps1`（凭证/表格/机器人自检 + 打印真实 URL）、
`tools\feishu-create-bitable.ps1`（建表 + 16 字段 + 打印真实链接）、
`tools\feishu-add-diag-table.ps1`（在已有表格里建 `diag_log`）、
`tools\feishu-export.ps1`（把表导成 CSV，应用读自己的表不需要分享）。

**已打包**：`.dist\CharacterBosses-v0.2.248.zip`（13,145,789 B，DLL/JSON 校验为 0.2.248）。
## v0.2.242：进阶 10 三层两场 Boss 用了**同一套变体**

用户 2026-10-30 晚：「进阶10 的 3 层 Boss 两场战斗里的相同角色 Boss 都相同（两场都用刀贼、辉星储、易伤战），
你是不是写反了」。

**不是写反**：`BossLineups.BuildAct3Pairs` 里 fight 0 / fight 1 是互补的（三对变体各分一半），
错的是**「这是第几场」的判定** —— 两场都被当成第一场，于是都取了同一半。

1. `BossReplacementPatches.IsSecondSlot` 以前照抄原版 `NextBossEncounter` 的完整条件
   （`bossEncountersVisited != 0 && SecondBoss != null`）。而剧情的实际顺序是：
   **第二槽往往到「真的要拉第二场」时才滚出来**（我们自己也没强建第二槽）⇒ `SecondBoss == null`
   ⇒ 判成第一场 ⇒ 第二场按第 1 场阵容生成 ⇒ 变体完全相同（刀贼 / 辉星储 / 易伤战）。
   现在：本幕 `visited > 0` 且「进阶 ≥10 且第三幕」⇒ **直接按第二场**，不再看第二槽是否存在
   （日志：`第 N 幕本幕已发过 X 只 ⇒ 这一次按第二场处理`）。
   读不到原版计数时的 id 兜底路径也补了同样的条件。
2. 即使在别处把两槽写成同一个遭遇（历史存档 / 旧版本），也加了一道硬保险：
   `ForVanillaBoss(secondBoss:true)` 末尾若算出来的 Key **与第一场阵容里的某个 Key 完全相同**，
   就换成同角色的互补变体（新增 `BossKeys.ComplementVariant`：刀贼↔炼药贼、易伤战↔巨石战、
   无色储君↔辉星储），并记日志。能力机 / 根除骨这类没有成对变体的保留原样。

**验证方式**：进阶 10 三层打一轮，日志里应出现两套**互补**阵容，例如
`第 3 层第 1 场阵容：[SILENT_SHIV,NECROBINDER_ERADICATE,REGENT_COLORLESS,IRONCLAD_VULN]` +
`第 3 层第 2 场阵容：[DEFECT_AI,SILENT_BREWER,REGENT_STAR,IRONCLAD_BOULDER]`。

已安装 v0.2.242（还没打包）。
## v0.2.241：联机不同步的**真凶** —— 钻石头冠那 20 格挡在帧回调里异步补

用户 2026-10-30 22:00 给了 **RitsuLib 诊断包**（`ritsulib_state_divergence_..._checksum_0_....zip`）。
它是杀手级工具：不同步时把**两端的状态逐项列出来对比**（`state-divergence-report.txt`，5276 行）。

比对结果（checksum #0，local=client …865617287，remote=…210246781）：
**只有 5 个字段不一样**，核心一行是
`creatures[6].block: local=20; remote=0`（creatures[6] = `MONSTER.SILENT_BOSS`）。
其余（每个生物 HP/力量/power、每张牌含关键词、遗物含 Props、药水、金币、15 个 RNG 计数器、
Choice/Reward IDs）全部一致。

**根因**：`BossHookBridge.GrantDiamondDiademAtCombatStart` 在战斗开始 `GainBlock(20)`，
而原版「战斗开始清零」与它的相对时序**两端不同**；随后靠 `BossFrameTickPatch` 每帧调
`EnsureDiamondDiademBlock` → `Task.Delay(120)` 后异步补。有的客户端补到了（20），
有的机器那几帧没跑到（0）⇒ 同一只 Boss 两端格挡不同 ⇒ 校验和必然分叉。

**改法（v0.2.241）**：
1. 战斗开始**只施加「残影 2 层」**，不再发格挡（`GrantDiamondDiademAtCombatStart`）；
2. 那 20 点格挡改在 **`Hook.AfterSideTurnStart(Player)` 的 postfix** 里同步补
   （新 `DiademBlockSyncPatch` → `BossHookBridge.GrantDiademBlockSync`，用
   `Creature.GainBlockInternal` 同步写）。这条钩子两端都会被 await、且在校验和之前 ⇒ 两端必然一致；
3. 删掉 `EnsureDiamondDiademBlock` / `RetryDiademBlockAsync` / 帧驱动里那次调用
   —— **教训：战斗状态绝不能在帧回调/UI tick 里改**。

已构建 + 安装 + 打包：`.dist\CharacterBosses-v0.2.241.zip`（13,142,479 B）。
包里同时包含 v0.2.238（HittableEnemies 收口）、v0.2.239（藏品关键词）、v0.2.240（灵魂绑定锁定）。

**待用户验证**：4 人局打第三幕 4 只角色 Boss，不再出现 `block: local=20; remote=0` 这类分叉。
## 2026-10-30 22:20 联机不同步（godot 8/9 + mod_log 15）的双端比对结论

用户给了**两台不同玩家**的 `godot.log`（…074363039 与 …865617287）+ 客人侧 `mod_log (15).txt`。

**逐行比对同一份 `After player turn start` / checksum 1695 的 LOCAL STATE DUMP：前 839 行完全相同** ——
4 名玩家的 HP/格挡/power、每张牌及其关键词、遗物、药水、金币、**全部 RNG 计数**（`RNG global seed: WED3MRLPMJT9`）、
Choice/Reward IDs 都一样；怪物列表也一致（我们的 4 只 Boss：SILENT 518/520、NECROBINDER 494/516、
REGENT 507/525、IRONCLAD 512/530；锁定分配 SILENT→Hermit / NECROBINDER→SlimeBoss / REGENT→Necrobinder /
IRONCLAD→Hexaghost）。两份日志打印的 `Local: 206725170 / Remote: 3458261928` 也完全相同。

⇒ **这两台客户端彼此一致、且与房主的校验和一致**；被点名的是 **7656119…（Hermit 玩家）**，
而且本轮三次不同步（20:17:39、20:18:01、21:59:10）**全部**点名同一位 ⇒ **是那一台机器算出来的状态与所有人不同**。

两台机器的模组清单差异极大却仍然一致，所以不是「装了不同的美容 mod 就必炸」：
- A（…074363039）：SpeedX + BaseLib + CharacterBosses + Downfall，**RitsuLib 关**；
- B（…865617287）：BaseLib + RegentCardsAnimeRework + 4 个皮肤 mod + 商人「阿塔」+ SkinManager + **RitsuLib 开** + CharacterBosses + Downfall。

**下一步（等用户给）**：Hermit 那台的 `mod_log.txt`（看 `===== CharacterBosses 构建：v0.2.xxx =====`）
+ `godot.log` + 模组清单；**以及 log (9) 里提到的那种 RitsuLib 诊断包 zip**
（`ritsulib_state_divergence_20261030_215919_…_checksum_1695_….zip`，里面有逐项 diff）。
三个最可能的差异源：① CharacterBosses 版本不同（我们给 Boss 关掉了多人血量/格挡缩放，旧版/未装的一端 Boss HP 会不同）；
② 只有那一台启用了改卡/改数值的 mod（含 RitsuLib 这类钩子扩展）；③ 那台的 `charbosses_config.cfg` 设置不同（共用池/地图开关）。
## v0.2.240：灵魂骨的「灵魂绑定」在联机里把**所有玩家**的抽牌都算给了她

用户 2026-10-30 晚：「有bug，联机时灵魂骨的特殊效果会计算所有玩家的抽牌，而不是只有被她锁定的玩家」。

- 位置：`Patches/SoulBoundDrawPatch.cs` 的 `Hook.AfterCardDrawn` 前缀。它原来只判
  「抽牌的是不是**真人玩家**」（`card.Owner.Creature.Monster == null`），**没有判锁定**
  ⇒ 4 人局里任何人回合内抽牌都会 `CreditAutomation` + `boss.NotePlayerInTurnDraw()`
  ⇒ 死亡行军倍率被灌爆、自动化疯狂给能量（明显过强，且和「对应型特殊效果」的设计不符）。
- 改法：加 `BossLockOn.SpecialEffectAppliesTo(boss, 抽牌者)` 判定 —— 单人恒 true（零回归），
  多人只有**锁定她的那位**为 true。判据来自同步数据（`PureVictim`），不会引入不同步。
  被挡掉时记一行 `灵魂绑定：X **不是她锁定的玩家** ⇒ 这次抽牌不计入死亡行军/自动化`（最多 10 行）。
- 顺带确认：`BossSoulBoundPower` 本身只是标记，没有别的「对全体玩家生效」的路径；
  预补池 `PendingDrawCredits` / `DrawCompensation` 都由同一个入口写入，所以一处收口即可。

**待用户验证**：多人局打灵魂骨，只有被锁定那位抽牌时她的死亡行军/自动化才涨。


## v0.2.239：收藏家藏品（我们的 Boss 掉落牌）导致联机不同步 —— 已修

玩家那边（`godot (7).log`）的 AI 说是「收藏家的角色 boss 藏品」，**它对了**。日志证据（同一份 LOCAL STATE DUMP 里）：

- 7286–7287：`Serialized card: SerializableCard CARD.AGGRESSION. Upgrades: 0 … Floor:` + `Keywords: Ethereal`
  ← `AGGRESSION` = 我们的掉落 `IRONCLAD_BOSS act1 → 好勇斗狠`，`Floor` 为空 = 它是**藏品**（不是牌库里的牌）；
- 10627–10628：`SerializableCard CARD.SNEAKY. Upgrades: 1 … Floor:` + `Keywords: Ethereal,Sly`
  ← `SNEAKY+` = `SILENT_BOSS act1`（多人）→ 鬼祟+，同样是藏品；
- 原版 dump 里**每张牌都打印 `Keywords:`**（例如 `ASCENDERS_BANE → Ethereal,Unplayable,Eternal`）
  ⇒ **关键词是校验和的一部分**。

**根因**：`DownfallCollectibleCompat.AfterGetCollectibles`（挂在崩坠 `CollectiblesModel.GetCollectibles`
上的 postfix）会给「我们掉落表里那几张」调 `ApplyCollectibleKeywords` 补 `Ethereal` / `Exhaust`。
而 `GetCollectibles` 是**藏品册的界面路径** —— 只有「恰好渲染了那个册子」的客户端会跑，
所以同一张藏品在**不同客户端关键词不同**（一端 `Ethereal,Exhaust`、另一端只有 `Ethereal`）。
关键词不同 ⇒ 打出时「进消耗堆还是弃牌堆」不同、回合结束是否消耗也不同 ⇒ **任何一场战斗**（包括小怪关）
的下一次校验和必然 divergence。这也解释了为什么它挑「收藏家手里有我们藏品」的那些局炸。

**改法**：`GetCollectibles` 里**只保留**「卡实例 → 藏品册下标」的记账（纯本地映射，不改状态），
**不再动任何牌**；关键词只在**造牌**时加（`CreateCard` / `CreateRewardCard`，两端跑同一套同步流程）。

**待用户确认**：① 4 人局小怪关不再不同步；② 若「消耗」标记在**已经躺在册子里**的旧藏品上消失，
下一步走崩坠自己的**房主/持有者端写回**（`AddCollectible` 之后重写该条目，只由持有者端写、再同步），
这样关键词既能显示、又两端一致。


## v0.2.238：普通战斗（小怪关）我们也在改选靶 —— 已收口

用户 2026-10-30 晚给了两份日志：`godot (7).log`（玩家机，`We are Client 7656119…`）+ `mod_log (14).txt`。

**日志里的事实**：

- 不同步发生在 **act 1 → act 2 之后的第一场普通战斗**（`12:37:07`–`12:38:32` 投票进入下一幕，随后
  `After player turn start` 的 checksum id 94 / 70 连续两次 divergence）。
- 那一场是 `MONSTER.BOWLBUG_ROCK`（235 HP）+ `MONSTER.BOWLBUG_EGG`（115 HP），外加两只**玩家侧召唤物**
  `MONSTER.BYRDPIP` / `MONSTER.PAELS_LEGION`（9999/9999）——**没有任何角色 Boss**，我们的 Boss AI / 锁定 / 手牌栏全都不在场。
- 报出来的是「**player 7656119… 的校验和与房主不一致**」，本机（…074363039）不是被点名的那一方。
- 那台机器的 mod 列表：SpeedX（个人速度模组）、BaseLib（**同时**有 Steam v3.4.7 与本地 v3.4.5，
  日志明确 `Steam version is greater … disabling the local version`）、CharacterBosses、Downfall 0.1.15。

**我们自己的问题（已修）**：`CombatSidePatches.HittableEnemiesSidePatch` 的「敌方回合把
`HittableEnemies` 翻成玩家侧」**根本没有闸门** —— 它在**每一场战斗**的敌方回合都生效，
等于替所有原版怪物 / 别的模组的怪物改了选靶语义。单机时只是「和原版不一样」（还顺手盖掉了一些别的模组的选靶 bug，
所以一直没人发现）；**联机时它就是不同步的源头**：只要有一台机器没装本模组 / 版本不同 / 没加载成功，
两端在敌方回合算出来的目标就不是同一批。
现在加了闸门：`BossShadowPets.BossFightActive(combat)`（本场真的有 `CharBossMonster`）才翻，
普通战斗 100% 走原版，并记一行 `这一场没有角色 Boss ⇒ HittableEnemies 走原版`。
（其余会碰模拟的补丁逐个核对过：毒 / 红面具 / 钻石头冠 / 鬼祟 / 暗黑球 / 抽牌 / 彗星碎片 / 死 Boss 不出手…
都已经有「owner 是真人玩家就 return true」或「必须是我们的 Boss」的闸门。）

**还需要用户配合的两件事**：

1. 判定「是不是我们的问题」的**决定性证据是房主那份 `godot.log`**：里面有
   `State divergence detected! Local: … Remote: …` 和**房主自己的** `LOCAL STATE DUMP`。
   两份 dump 一对，差在哪一项（HP / power / 手牌 / RNG counter / Choice IDs…）立刻就出来了。
   现在只有客人那一侧的 dump。
2. 联机**四台机器的模组与版本必须一模一样**（本模组、BaseLib、各种角色 mod）。
   这台机器同时挂着两份 BaseLib（3.4.7 + 3.4.5）和 SpeedX —— 任何一个别人没有的模组都会自己造成不同步。


## v0.2.237：联机不同步（客人打原版 Boss、房主打角色 Boss）

用户 2026-10-30 晚：「有玩家联机出bug了，你先修完，然后打包。`mod_log (14).txt`」

**证据链**（该日志是**客人**端，v0.2.230 会话从第 51192 行开始）：

- `20:17:35 [Hook] 拉 Boss 房间: act=0 incoming=ENCOUNTER.CEREMONIAL_BEAST_BOSS 已发=0`
  → 客人这一槽**还是原版**仪典兽；紧接着 `20:17:39 ⚠ 不同步状态摘要（检测到不同步）：轮次 1`，
  而这份摘要里**一只我们的 Boss 都没有**（说明客人打的根本不是我们那场战斗）。
- `20:17:57 客人端：第 1 幕Boss 跟随房主 ⇒ DEFECT_DARK_BOSS_ENCOUNTER`（还有 2/3 幕与三层第二场）
  → 房主的数据**18 秒后才到**，而那时战斗已经打起来了；随后 20:18:01、20:18:41 又各 divergence 一次
  （全在轮次 1，客人视角 HP / 锁定分配两次完全相同 ⇒ 一直在回滚重打同一场）。

**根因**：`ForVanillaBoss` 的客人分支把两件事混成一件 ——「房主真的没换」和「房主的数据还没同步到本机」。
后者 ⇒ 客人拿原版 Boss 去对房主的角色 Boss ⇒ 必炸，且回滚后仍然一致地炸下去。

**改法**：

1. 客人分支：同步槽位是我们的 → 照旧跟随；**否则不再 `return null`**，改为**继续走本机那套确定性规则**
   （种子 `RunState.Rng.Seed` + 幕 + 场 + 人数 + 进阶，全部来自同步数据；`BossLineups` 里本来就保证两端一致），
   并打一行 `客人端：第 N 幕Boss 同步槽位还是原版（…）⇒ … 先按与房主相同的确定性规则算一个`。
2. `BossSlotReplacer.ReplaceOne`：**客人不再 `SetBossEncounter`**（客户端别改同步状态），
   只在生成战斗时用返回值；打一行 `客人端**不写**同步状态` 说明。
3. `DivergenceStateProbe`：摘要加**遭遇 id / 本机是房主还是客人 / 本幕存档 Boss 槽**，
   以及每只 Boss 的**手牌·抽·弃·耗张数 / 能量·星 / 本回合出牌计划** —— 下次两端日志一对就能指出是哪一项。

**仍需用户验证**：4 人局进 Boss 房不再不同步（两端同版本、同设置）。若还炸，请把**两份** `mod_log.txt`
（房主 + 客人）一起发来，新摘要能直接对齐差异项。

## v0.2.236：飞书多维表格自适应字段（AI 建的表也能写进去）

用户 2026-10-30 用飞书「模板中心 → AI 建多维表格」建好了表（表名 `boss_fights`，字段是 AI 自己编的
`at/bossHp/turns/seed/cfg/line/胜局标记`），并问「你说的 URL 在哪」。

- **URL 就是** `https://wcrmm5hp6i.feishu.cn/base/<app_token>?table=<table_id>&view=…` ——
  本例 `app_token = X8n7b9Eg3GaDTWRsfJ9ncJntH`、`table_id = tblazV2VYw2EZRjg`。
- 代码改动：`UploadToFeishuBitable` 现在**先 `GET …/fields`**（缓存 10 分钟）拿到表内真实字段，
  只写**对得上的**字段；表里没有的（如 `result`/`boss`/`ascension`…）本次跳过，并在 `mod_log.txt` 记
  `飞书多维表格：表里没有这些列 ⇒ 本次跳过 [...]`，作者把列补上就自动带上（不用改代码）。
- 读不到字段列表（应用没加为该表协作者 / 没有 `bitable:app`）会明确记
  `读不到字段列表（应用没加为该表协作者，或权限不足）⇒ 本次跳过`。
- v0.2.235 把 `seed` 改成按**文本**发（19 位整数按数字会丢精度）。


## v0.2.234：国内可用 —— 飞书上收数据

用户 2026-10-30：「我在国内谷歌表格打不开，能不能用飞书云文档之类的？」

`Telemetry.FlushUploads` 现在按优先级选择去处（**任何一项配了就能用**，都在 `charbosses_config.cfg` 里）：

1. **飞书多维表格**（`feishuAppId` + `feishuAppSecret` + `feishuBitableAppToken` + `feishuTableId`）：
   `POST /open-apis/auth/v3/tenant_access_token/internal` 换 token（缓存 100 分钟）→
   `POST /open-apis/bitable/v1/apps/{app}/tables/{table}/records/batch_create`（100 条/批）；
   数字列（act/ascension/players/bossMaxHp/bossHp/turns/seed）按 number 发，其余按文本 ⇒ 表格里能排序求和；
   失败会打 `飞书多维表格写入失败：<响应>` 并把数据留本地队列重试。
2. **飞书群机器人**（`feishuBotUrl`）：零鉴权 `POST /open-apis/bot/v2/hook/...`，
   每次上传发一条文本消息（最多列 20 条 `[战斗] win | 第3幕 … | 进阶10 1人 | 回合9 | HP 0/225 | 种子…`）。
3. **通用 `telemetryUrl`**：原样的 `{"mod":…,"records":[…]}` POST（自建服务 / Worker / Apps Script）。

「有没有配端点」统一用 `BossConfig.AnyUploadEndpoint` 判断（A6 行、弹窗自动勾选都跟着它）。
文档 `docs/plans/telemetry-backend.md` 重写为**飞书优先**：接法①群机器人 5 分钟上手、
接法②多维表格（列名清单、权限 `bitable:app`、`app_token/table_id` 从 URL 里取、仪表盘/分组统计怎么做），
Google 表格那段降级成附录，Cloudflare 保留为可选。


## v0.2.233：数据要能到你手上 —— 上传层 + 后端方案

用户 2026-10-30：「这玩家的 boss 战数据要是不上传我怎么进行平衡呢？…之后我从哪里查这些数据？」

### 代码侧（这一版做的）

- 新增配置 `telemetryUpload`（A6）与 `telemetryUrl`（收集端点，**默认空 = 完全不联网**）。
  `BossConfig.TelemetryUpload` 只有「A5 开 + 端点非空 + 玩家勾了 A6」三者同时满足才为真。
- `Telemetry.Write` 写本机后，若允许上传 ⇒ 追加到 `telemetry/upload-queue.jsonl`，
  后台线程 `POST` 到端点；**2xx 才删队列**，失败留在本地下次再发（队列上限 400 条，丢最旧的）。
  15 秒超时、全 try/catch，绝不阻塞游戏。
- 配置界面 A5 区块新增两行：**A6「上传游玩数据给作者」**（没配端点时显示「作者未配置收集端点 ⇒ 只在本机记录」）、
  **「把数据文件复制到桌面」**（手动发给作者的兜底）。
- 首次弹窗：玩家选「开启」且端点已配置 ⇒ 自动同时打开 A6 并记日志（弹窗正文已写明会上传匿名数据）。
- 弹窗文案同步更正（不再写「不会上传」，改成「先存本机；作者配置了服务器则一并上传匿名数据」）。

### 文档（给作者的实际上机步骤）

`docs/plans/telemetry-backend.md`：**方案 A = Google 表格 + Apps Script（免费、零运维，数据和图表都在表格里）**、
方案 B = Cloudflare Worker + R2/D1、方案 C = 玩家手动导出；含完整脚本、部署步骤、`telemetryUrl` 填法、
以及「按 boss×result 做透视表看胜率」的做法。

### 我这边的边界（诚实说明）

我**没有**服务器/账号可以替作者搭，所以默认端点为空 —— 作者把 Apps Script（或 Worker）链接拿到后，
填进 `%APPDATA%\SlayTheSpire2\mod_configs\CharacterBosses\charbosses_config.cfg` 的 `telemetryUrl` 即可；
也可以发给我，我把它作为**默认值**写进 `BossConfig`，这样分发给所有玩家时自动生效。


## v0.2.232：首次加载用「原版弹窗」问一次

用户 2026-10-30：「让玩家首次加载本模组时弹窗进行询问（用原版的弹窗，别自创）是否开启选项，
具体文本可以参考原版的（我记得原版也会问这个）」。

### 用的是哪一个原版弹窗（没有自创 UI）

反编译 + 抽场景确认原版自带「通用二选一弹窗」：

- 场景 `res://scenes/ui/generic_popup.tscn` → 类 `MegaCrit.Sts2.Core.Nodes.Multiplayer.NGenericPopup`
- 用法：`NGenericPopup.Create()` → `NModalContainer.Instance.Add(popup, showBackstop: false)`
  → `await popup.WaitForConfirmation(body, header, noButton, yesButton)`（true = 点了「是」）
- 原版自己的「放弃本局 / 断线重连」确认框走的就是同一套 `NModalContainer` 机制。

### 文本（参考原版 `settings_ui` 的「上传游戏数据」那一组）

原版原文（抽 `localization/zhs/settings_ui.json` 得到，作为参考）：

```
UPLOAD_GAMEPLAY_DATA            = 上传游戏数据
UPLOAD_GAMEPLAY_DATA_DESCRIPTION= 一些游玩数据和错误报告会被发送给制作组，用以修复bug和作为平衡的依据。
                                  任何个人信息都不会被收集。
```

我们的版本（放进我们已经在打包的 `encounters` 本地化表，键 `TELEMETRY_PROMPT_*`；
`Loc()` 里如果找不到自己的键，会**退到原版 `settings_ui` 的同名文案**）：

- 标题：`上传游戏数据（CharacterBosses）`
- 正文：把数据保存在**本机文件**里，用以修 bug 和平衡；列出会记的字段；
  强调**不会上传、不会联网、不收集个人信息（不写 SteamID/昵称）**；默认关闭、随时可在 A5 改。
- 按钮：`开启` / `保持关闭`

### 时机与「不再打扰」

- 挂在主菜单 `NMainMenu._Ready` 之后（`BossConfigRegisterMainMenuPatch`）→ 等 0.8 秒
  （确保弹窗容器进树）→ 弹一次。
- 新增配置项 `consentAsked`（持久化，已修好写盘）：**是/否都记「问过」**，之后不再弹。
- 拿不到 `NModalContainer` 或弹窗创建失败 ⇒ 不标记「问过」，下次启动再试（不打扰也不丢流程）。
- 选「开启」→ `SetTelemetry(true)` + 写 `consent` 记录；选「保持关闭」→ 维持默认关。


## v0.2.231：A5「本地对战数据记录」（可选、只写本机）

用户 2026-10-30：「有些模组（比如邪教徒）可以收集玩家数据（先询问玩家）来帮助作者平衡与修 bug，
你能给本模组也搞一个吗」。

### 设计原则（写在 `Core/Telemetry.cs` 顶部注释里）

1. **默认关**：只有玩家在「模组配置 → CharacterBosses → **A5**」自己打开才记录；打开即知情同意，
   数据文件第一行写一条 `consent`（时间/版本/平台）。
2. **只写本机、不联网、不上传**：文件在
   `%APPDATA%\SlayTheSpire2\mod_configs\CharacterBosses\telemetry\charbosses-telemetry.jsonl`（一行一条 JSON）；
   想帮作者就把文件发出来。
3. **不含身份信息**：不写 SteamID / 昵称 / 存档路径。
4. **绝不影响游戏**：全部 `try/catch` 吞异常、`FileShare.ReadWrite`（双开安全）；文件 >4MB 轮转 `.1`；
   `diag` 每进程最多 300 条。
5. 关掉后**不再写任何数据**（旧文件保留，玩家自己删）。

### 记什么

- `boss_fight`（每场我们的 Boss 战结束一条）：`result`(win/lose)、`act`、`encounter`、`lineup`、`boss`、
  `ascension`、`players`、`bossMaxHp`、`bossHp`、`turns`、`seed`、
  `cfg{mixedMatching,sharePool,asc10Act3OneEach,bgmSource}`、`mod`、`at`(UTC)。
- `diag`：我们自己打的「失败 / 异常 / Exception / 不同步 / ⚠」日志行（截断 300 字），用于 bug 定位。

### 接线

- `Telemetry.BossFightEnded(...)` ← `CharBossMonster.BeforeRemovedFromRoom`
- `Telemetry.Diagnostic(...)` ← `SafeLogPatch`（我们 `Logger.Log` 的前缀）
- `Telemetry.WriteConsentOnce()` ← 打开开关时 + `BuildInfo.LogOnce`（已开启的老配置也补一条）
- 配置界面新增 A5 区块（BaseLib 勾选框 + 文件路径说明）

### 顺带修掉的 bug

`BossConfig.WriteSnapshot` 以前**漏写** `SharePool` / `Asc10Act3OneEach`（还把 `version` 硬写成 1）
⇒ A4 的两项每次重启都被悄悄重置回默认。现在全部原样写回，并带上新的 `telemetry`。


> 打包记录（用户 2026-10-30「打包」）：`.dist\CharacterBosses-v0.2.230.zip`（13,123,204 字节，15:07 生成）
> = `CharacterBosses.dll`(707,584) + `CharacterBosses.json`(0.2.230) + `CharacterBosses.pck`(12,863,500) +
> `安装说明.txt`(77,818)；三个二进制与本机 `mods\CharacterBosses\` SHA256 **一致**。
> 安装说明新增「v0.2.230 重要变更（自 v0.2.229 起）」1 条：祸不单行对角色 Boss 掉落的藏品生效。

## v0.2.230：『祸不单行』打不出两次我们的藏品牌

用户 2026-10-30：「收藏家的『祸不单行』不能使角色 boss 掉落的藏品卡打出 2 次」。

反编译 `Collector.CollectorCode.Powers.DoubleTroublePower.ModifyCardPlayCount`：

```csharp
if (card.Owner.Creature == Owner && card is ICollectible) return playCount + 1;
return playCount;
```

它只认实现了 **`ICollectible`** 的「它自己的藏品牌」。我们的藏品是**原版牌**
（遗传算法 / 涂毒 / 根除…），类型上不可能是 `ICollectible` ⇒ 永远 +0。

修法（`Core/DownfallCollectibleCompat.cs`）：给 `DoubleTroublePower.ModifyCardPlayCount` 挂 postfix ——
若 `__result == playCount`（原版没加过）、牌的拥有者就是这层 power 的主人、且牌 id 在**我们掉落表**里
⇒ `__result = playCount + 1`。日志：`祸不单行 → 藏品牌「GENETIC_ALGORITHM」这次也会被打出两次`。


> 打包记录（用户 2026-10-30「打包」）：`.dist\CharacterBosses-v0.2.229.zip`（13,122,744 字节，14:21 生成）
> = `CharacterBosses.dll`(706,048) + `CharacterBosses.json`(0.2.229) + `CharacterBosses.pck`(12,863,500) +
> `安装说明.txt`(77,136)；三个二进制与本机 `mods\CharacterBosses\` SHA256 **一致**。
> 安装说明新增「v0.2.229 重要变更（自 v0.2.228 起）」2 条：收窄出牌队列干预 / 新增「出牌队列滞留」看门狗。

## v0.2.229：杀小怪卡死（我方对原版出牌流程的干预过宽 + 加看门狗）

用户 2026-10-30 截图：「我杀个小怪怎么卡死了」。日志特征：
- `mod_log.txt` 停在 `忧郁减费记账：噬尸蛞蝓 死亡 → 本场累计 2 个`（我们的 `Hook.AfterDeath` postfix），
  之后**连「帧回调耗时」都不再写** ⇒ 主线程卡住（不是崩溃）；
- `godot.log` 最后是 `[SteamRandomMatchMod][FormVfx] Cleared stuck Play-pile card=COLLECTOR-STRIKE_COLLECTOR`
  与几条 `res://Collector/images/character/reserve_icon.png` 资源加载失败。
  ⇒ 那一瞬间有卡面卡在**出牌队列**里。

两处改动：

1. **收窄 `BossCardQueueNoThrowPatch`**：以前是「只要 `NCardPlayQueue` 里找不到这张牌就跳过原版」
   —— 等于替**所有**卡（玩家卡、别的模组的卡）把原版流程吞掉。现在**先确认这张牌的拥有者是我们的
   `CharBossMonster`**，否则一律走原版。
2. **新增 1Hz 看门狗** `WatchPlayQueue`（在 `BossFrameTickPatch`，放在 `boss == null` 早退之前，
   所以普通小怪战也会跑）：任何卡面在 `NCardPlayQueue` 里滞留 >3 秒就打一行
   `⚠ 出牌队列滞留：<卡>（拥有者 <生物>）已在队列里 N.Ns，队列内卡面 M 个`。
   只读不改，下次真卡死时这行就是线索（谁的牌、卡多久）。

⚠ 说明：这一版**没有**确证「是我们的代码造成卡死」——我们在小怪战里几乎不干活（`boss == null` 早退）。
若下次仍卡死，看 `⚠ 出牌队列滞留` 那行 + `godot.log` 末尾的资源加载失败，就能分清是
「出牌动画流程卡住」还是「别的模组（SteamRandomMatchMod / Downfall 的 reserve_icon）」。


> 打包记录（用户 2026-10-30「打包」）：`.dist\CharacterBosses-v0.2.228.zip`（13,121,578 字节，14:12 生成）
> = `CharacterBosses.dll`(704,512) + `CharacterBosses.json`(0.2.228) + `CharacterBosses.pck`(12,863,500) +
> `安装说明.txt`(76,071)；三个二进制与本机 `mods\CharacterBosses\` SHA256 **一致**。
> 安装说明新增「v0.2.228 重要变更（自 v0.2.212 起）」5 条：胜利音 / 地图徽章 / 三条死亡提示与本地化补齐 /
> 收藏家藏品遗传算法永久成长 / 中间三个崩溃版本已修好。


## v0.2.228：第二只 Boss 的胜利音被上一场遗留的计时器切掉

用户 2026-10-30：「进阶 10 的 3 层第二个 boss 死亡时没播放胜利音乐」。`mod_log.txt` 实证：

```
[14:05:11 … 14:05:59] BGM：已安排 2 秒后还原原版音乐（SceneTreeTimer）     ← 每 2 秒一行，几十行
[14:06:01] BGM：… 给「queen_progress」发死亡段落 5（等玩家离开这个房间再还原，20 秒兜底）
[14:06:01] BGM：已安排 20 秒后还原原版音乐（SceneTreeTimer）
[14:06:01] BGM：还原原版音乐（Boss 死亡段落播完；原来在放 event:/music/act3_boss_queen）   ← 同一秒就被切了
```

链条：`ScheduleRestore` 的计时器到点时如果 `CombatInProgress()` 为真就**自己再排 2 秒**——
上一场结束后 `IsInProgress` 在结算期间仍为真 ⇒ 整整一场战斗每 2 秒重排一次（刷屏 + 埋雷）；
第二只 Boss 刚把死亡段落发出去，**场上随便一个旧计时器**接着触发 `Restore("Boss 死亡段落播完")`
⇒ 段落一秒都没播完。

修法（三处）：

1. **计时器带代号**：`_restoreGen` 每安排一次 +1，回调里 `if (gen != _restoreGen) return;`
   —— 旧计时器从此完全失效；
2. **到点还在战斗/结算就返回，不再自己重排**（重排交给「离开房间」那条路 `OnRoomEntered`）；
3. **开新的一场战斗时清掉上一场的待还原状态**：`OnCombatStart` 里重置
   `_deathPendingRestore` / `_deathPlaybackScheduled` 并 `_restoreGen++`
   （三层 asc10 是连打两场，这条最要紧）。


## v0.2.227：那圈装饰不是（只）`PlaceholderOutline`

用户 2026-10-30 截图：徽章中间已经是我们的深灰剪影，但**外面那圈尖刺/叶子还在**。
v0.2.225/226 明明已经把 `%PlaceholderOutline` 设成 `Visible = false` 了 ⇒ 说明装饰来自别处。

反查 `res://scenes/ui/boss_map_point.tscn`（从游戏 PCK 抽出来的真实节点树）：

```
BossMapPoint (Control)
└─ SpriteContainer (Node2D)
   ├─ SpineSprite (SpineSprite, 带 shaders/boss_map_point.gdshader)      ← 装饰可能是它
   └─ PlaceholderImage (TextureRect 374×350)
      └─ PlaceholderOutline (TextureRect, show_behind_parent)           ← <BossNodePath>_outline.png
```

⇒ 现在改成**把 `SpriteContainer` 下除我们那张 `PlaceholderImage` 以外的所有 `CanvasItem` 全部隐藏**
（包括它的子节点 `PlaceholderOutline` 和 `SpineSprite`），并用一行日志记下被隐藏的节点名：
`地图 Boss 徽章：已隐藏节点上的旧美术 [SpineSprite,PlaceholderOutline]`。
这样下次日志就能直接告诉我们那圈装饰到底是谁画的。


## v0.2.226：灾厄骨也改回角色徽记（并纠正一处错误归属）

用户 2026-10-30：「我怎么不记得我说过『亡灵契约师统一用群魂/骷髅那张』？改回角色徽记」。

那条说法只出现在**我 2026-10-29 自己写的代码注释里**，把 `soul_bone` 一刀切给了
灾厄骨 / 灵魂骨 / 根除骨三个档位；实际上张图是 **2026-10-26 只给「灵魂骨」** 的。
现在按用户口径收拾干净：

| 档位 | 地图徽章 |
|---|---|
| 灾厄骨 `NECROBINDER_DOOM`（一层） | **角色徽记**（亡灵契约师） |
| 灵魂骨 `NECROBINDER_SOUL`（二层） | `soul_bone`（用户 2026-10-26 指定，保留） |
| 根除骨 `NECROBINDER_ERADICATE`（三层） | **角色徽记** |

代码里现在只有 `NecrobinderSoulBossEncounter` 覆写 `BossNodePath`。

**教训（已写进 cerebrum）**：不要在代码注释里把「用户说过某句话」当成事实依据 ——
本轮就是因为这条我自己写下的「用户口径」，多绕了两版、还把徽章改错。
涉及「用户要求」的注释一律写清依据（原话 / 日志 / 截图），不确定就标「待确认」。


## v0.2.225：根除骨本来就没有专属美术

用户 2026-10-30 纠正：「根除骨没有专属美术，灵魂骨才有」。

代码里 `NecrobinderEradicateBossEncounter` 却写了
`BossNodePath => res://CharacterBosses/images/map/soul_bone`（2026-10-29 我按「亡灵契约师统一用群魂/骷髅」
一刀切加的）⇒ 三层第二颗徽章因此既撞图、又绕过了「换角色徽记」的逻辑。

修法：**删掉根除骨的 `BossNodePath` 覆写**（继承 TheKin 占位）⇒ 由 `BossMapPointCharacterIconPatch`
换成**亡灵契约师角色徽记**，外圈旧装饰隐藏。灵魂骨的 `soul_bone` 保留不动。

另外新增一行诊断（每「遭遇 + 第几颗」一次）：
`地图 Boss 徽章判定：<entry>（第一颗/第二颗，按现有贴图判定/按地图点判定）` ——
两颗节点共用同一张占位图时，只能靠 `MapPoint` 判第几颗，判错会立刻在这行暴露出来。

⚠ 待确认：**灾厄骨（一层）仍用 `soul_bone`**（这是 2026-10-29 用户明确要求的「亡灵契约师统一用群魂/骷髅」）。
按今天「只有灵魂骨才有」的口径，灾厄骨是否也该改回角色徽记？没动，等用户答复。


## v0.2.224：第二颗徽章画成第一颗的角色 + 外圈残留

用户 2026-10-30 截图 + 日志定位（这一局三层阵容：第 1 场 `REGENT_STAR`、第 2 场 `NECROBINDER_ERADICATE`）：

### ① 第二颗画的是储君（应该是根除骨）

老挑法两颗节点都用 `[本槽, 另一槽]` 两个候选，并且要求「这个遭遇**没有**专属美术」——
而**根除骨恰好有专属美术**（`BossNodePath = res://CharacterBosses/images/map/soul_bone`）
⇒ 第一轮就被 `UsesPlaceholderArt` 否掉 ⇒ **跨槽回落到第一场的储君** ⇒ 两颗都画成储君。

新逻辑：**先确定「这颗节点是哪一格」，再只认那一格的遭遇**：

1. `MatchByArt`：拿节点**当前显示的贴图路径**和两个槽的 `BossNodePath` 比对（最可靠，直接来自同步数据）；
   两颗共用同一张占位图时，按 `Point` 判出的「第几颗」决定比对顺序；
2. 比不出来才用 `Point` 判 → 取对应槽；第二槽为空时按确定性阵容推出；
3. **绝不跨槽回落**；
4. 这一格的遭遇**自己有专属美术、且节点现在画的正是它** ⇒ 一个像素都不动（根除骨从此保持 soul_bone 图）。

### ② 外圈那圈「女王的图标」

`%PlaceholderOutline` 是 `<BossNodePath>_outline.png`，即**原本那只 Boss** 的装饰图
（储君档位没有专属美术 ⇒ 用的是 TheKinBoss 那圈尖刺/叶子，看着像女王的）。
既然中间的画已换成角色徽记，这圈旧装饰只是残渣 ⇒ 现在 `outline.Visible = false` 直接隐藏
（v0.2.222 换圆盘 ⇒ 变成光秃秃的圆；v0.2.223 保留 ⇒ 残留别人的装饰；这次直接藏掉）。

## v0.2.223：徽章外圈装饰（短暂存在的一版）

v0.2.222 把 outline 换成圆盘后原版那圈装饰没了、变成「光秃秃一个圆 + 一颗星」，
所以 0.2.223 先改回「保留原版外圈」；用户随即指出「第二颗还是画成储君 + 外圈还残留女王的装饰」，
于是有了上面的 v0.2.224。

## v0.2.222：胜利音被切断 + 第二颗徽章叠着原版底图

用户 2026-10-30：「胜利音效没完全播完就戛然而止了」+「进阶 10 第三幕第二场的地图徽章是
第一场的角色剪影和原版 Boss 图像（女王）叠在一起」。

### ① 胜利音被切断：固定 5 秒太短

死亡段落有多长事先不知道，v0.2.216 定的 `DeathPlaybackSeconds = 5.0` 会把它切断。
**改成「玩家离开战斗房」才还原**：新增 `BossBgm.OnRoomEntered()`，挂在
`RunManager.EnterMapPointInternal(int, MapPointType, AbstractRoom, bool)` 的前缀上
（进入下一个地图点的唯一入口）——这个时机天然就是段落的终点，既不切歌，也不会让 Boss 曲目拖进地图。
原 5 秒计时器改为 **20 秒兜底**（万一玩家一直停在结算界面）。
日志：`BGM：离开战斗房 → 还原原版音乐（死亡段落已完整放完）`。

### ② 第二颗徽章叠图 + 白色刺眼

- 上一版把 `%PlaceholderOutline` 交还给原版 —— 但它的贴图是 `<BossNodePath>_outline.png`，
  **是「原本那只 Boss」的剪影底图**（这次是女王），于是和我们换上去的角色徽记叠在一起。
  ⇒ 现在 outline 也换成我们生成的**同色调圆盘**（既遮路线虚线、也不残留别人美术）。
- 剪影原本是**纯白**（指望地图染色），但某些地图状态下不染色 ⇒ 白得刺眼。
  ⇒ 改成原版占位图那种深灰：剪影 `#4A545E`、圆盘 `#5D6870`（比圆盘略深，形状能看出来）。

## v0.2.221：进战斗黑屏崩溃（v0.2.220 我又埋了一次递归）

用户 2026-10-30：「进战斗就一直黑屏，然后游戏崩溃了」。日志最后一行正是：

```
[13:12:58] 收藏家兼容：成长钩子命中「GENETIC_ALGORITHM」（IncreasedBlock=0，按此算格挡=1，Increase=3）
```

也就是：钩子确实被调到了（说明 `IncreasedBlock` setter 这个挂点是对的 ✔），然后**崩在钩子内部**。

根因：写回逻辑为了「按旧格挡值定位条目」去调 `CardModel.FromSerializable(sc)`
—— 而它会把条目里的 `[SavedProperty]` **重新赋值** ⇒ 又触发 `IncreasedBlock` 的 setter
⇒ 又进 postfix ⇒ 又反序列化 … **无限递归 → 栈溢出 → 黑屏崩溃**（Godot 不会打 "Stack overflow"，
`godot.log` 里啥也看不见，只有 mod_log 停在那一行）。

修法：

1. **`[ThreadStatic] _persisting` 重入锁**：本线程正在写回时再进来直接返回；
2. **这个 postfix 里绝不再反序列化**：`SerializableCard` 自带 `Id` 与 `Props.ints`
   （`SavedProperty<int>` 的 `name`/`value`），直接读 `Id.Entry` 和 `CurrentBlock` 的整数值就能匹配；
3. **幂等短路**：条目里已经是新值（`CurrentBlock == 1+IncreasedBlock` 且 `IncreasedBlock` 相同）就立刻返回，
   避免每次反序列化都白跑一趟反射 + 列表复制。

## v0.2.220：藏品堆里的「遗传算法」仍然不涨 —— 换钩子点 + 换定位方式

用户 2026-10-30：「还是没有永久增加格挡」。日志显示 v0.2.219 的
`已挂上 遗传算法.BuffFromPlay` 确实装上了，但**没有任何「写回」或「写回失败」的行** ⇒
补丁要么没被调到、要么走到了「实例映射查不到就静默 return」那条。

两个都修：

1. **换主钩子**：`GeneticAlgorithm.BuffFromPlay(int)` 只有两行，**极可能被 JIT 内联**（内联后挂在方法入口的
   补丁永远不会被调用）。现在主钩子挂 `GeneticAlgorithm.IncreasedBlock` 的 **setter**
   （它有 `AssertMutable()`，不会被内联），`BuffFromPlay` 作为副钩子一起挂；写回幂等，重复调用无害。
2. **换定位方式**：不再只依赖「`GetCollectibles` 重建出来的实例」——
   藏品堆里的牌在打出时常常是**另一份实例**。新增慢路径：按
   「同一张牌 + 同样的升级状态 + 条目里存的格挡 = 打之前的数值（`1+IncreasedBlock−Increase`）」
   去 `CollectorDeck` 里找条目；`BuffFromPlay` 是先加 `IncreasedBlock`、后写 `CurrentBlock`，
   所以 setter 钩子里 `CurrentBlock` 还是旧值，格挡值一律按 `1 + IncreasedBlock` 算。
3. **诊断补全**（下次日志一眼能定位）：
   `成长钩子命中「GENETIC_ALGORITHM」（IncreasedBlock=…，按此算格挡=…，Increase=…）` /
   `按旧格挡值匹配到藏品册第 N 条` / `没能定位到藏品册条目（…）；藏品册现有 [...]` /
   `写回藏品成长跳过 —— 藏品册是空的`。

## v0.2.219：藏品堆里的「遗传算法」打出后不涨格挡

用户 2026-10-30：「加入收藏家藏品堆中的遗传算法不会因为它的打出而增加获得的格挡量」。

反编译两边确认根因：

- `GeneticAlgorithm.OnPlay`：`BuffFromPlay(+3)` 只加在**这张牌实例**和它的 **`DeckVersion`**（牌库里那张）上
  —— 这就是原版「牌在牌库永久成长」的实现方式；
- 收藏家的藏品堆 = `CollectiblesModel.CollectorDeck`，存的是 **`List<SerializableCard>` 序列化快照**，
  `GetCollectibles(player)` 每次都用 `CardModel.FromSerializable(...)` **重建新实例**
  ⇒ 藏品堆里的牌**没有「牌库那一张」**（`DeckVersion == null`），成长只落在临时实例上，
  下次重建又回到原始数值 ⇒ **永远不涨**。

修法（`Core/DownfallCollectibleCompat.cs`）：

1. `GetCollectibles` 的 postfix 里记下「重建出来的卡实例 → 在 CollectorDeck 里的下标」（`CollectibleIndex`）；
2. 新增 `GeneticAlgorithm.BuffFromPlay(int)` 的 postfix：这套牌若是藏品堆里那张，
   就把它的 `ToSerializable()`（`CurrentBlock`/`IncreasedBlock` 都是 `[SavedProperty]`，会一起存进去）
   写回该下标，并像它自己的 `AddCollectible` 那样用 `Set(player, 新列表)` 提交；
3. 卡实例在藏品堆里常常还没挂 `Owner` ⇒ 先按 Owner 找，找不到再按实例遍历所有玩家；
4. 日志：`收藏家兼容：藏品「GENETIC_ALGORITHM」# 0 打出后的成长已写回藏品册（累计格挡 +3）`（同一张只报一次）。

## v0.2.218：打完 Boss 卡死崩溃（我自己 0.2.216 埋的无限递归）

用户 2026-10-30：「击败 boss 后游戏直接卡住（播放了 1 秒胜利音效），然后崩溃」。
`mod_log.txt` 直接给了铁证：**同一秒里 `BGM：兜底还原` 打了 9621 行**。

链条：

```
TickRestore → Restore → controller.UpdateTrack()
          ↑                    ↓
          └── MusicTrace.UpdateTrackPatch.Postfix（HasPendingRestore 为真）
```

`Restore()` 原来把 `_playing = null` 放在**最后**，所以 `UpdateTrack` 的 postfix 询问
「还有没还原的曲目」时仍得到「有」⇒ 又调 `TickRestore` ⇒ 又 `Restore` ⇒ 又 `UpdateTrack` …
**无限递归 + 每帧一行日志**（`Godot` 写盘扛不住）⇒ 卡死崩溃。
「1 秒胜利音」= 递归里第一次 `StopCustomMusic()` 把刚起的死亡段落切掉了。

修法（四道锁）：

1. `Restore()` 改成**先清状态再动 FMOD**：`_playing = null` / `_claimed = false` / `_playingEncounter = null`
   全部提前，之后任何回调再问都得到「没有」；
2. 新增 `_restoring` 互斥标志，`Restore` 重入直接返回（`finally` 里复位）；
3. `HasPendingRestore`（`UpdateTrack` postfix、`NMapScreen._Process` 驱动都用它）改成
   `_playing != null && !_restoring && !_deathPlaybackScheduled`；
4. `TickRestore()` 增加 `_deathPlaybackScheduled` 判断（**死亡段落在放时不许兜底切歌**）
   + `_fallbackLogged`（兜底日志每场只一行）。

## v0.2.217：用户指定的 3 条死亡提示改写

用户 2026-10-30（对照原版 12 只 Boss 的 `loss` 文案后）指定：

| 遭遇 | 旧 | 新 |
|---|---|---|
| 灾厄骨 `NECROBINDER_DOOM` | {character} 被亡灵契约师拖向了生命尽头。 | **[gold]亡灵契约师[/gold]将{character}召唤到了灾厄的末路。** |
| 炼药贼 `SILENT_BREWER` | {character} 被静默猎手的药水淹没了。 | **{character}没能击穿[gold]静默猎手[/gold]用药水构筑的屏障。** |
| 物理机 `DEFECT_CLAW` | {character} 被故障机器人用爪击一点点拆碎了。 | **{character}被[gold]故障机器人[/gold]的超能光束湮灭了。** |

并**删除死键** `DEFECT_START_BOSS_ENCOUNTER.title/.loss`（代码里没有这个遭遇类，永远不会显示）。

- 中英文都改了（eng 对应用户句意译为：beckoned … to the bitter end of calamity / could not break through
  the potion-wrought barrier / annihilated by the Hyper Beam）。
- Boss 名沿用我们既有的 `[gold]…[/gold]` 标注（与原版把 Boss 名标金一致）。
- 两个语言的键集仍然一致（各 62 键）；PCK 已重建并用 `extract_pck_file.gd` 抽出来验证：
  三条新文案都在、`初始牌组` 已不存在。
- 顺带存档了参考文档 `docs/plans/vanilla-boss-loss-messages.md`（原版 12 只 Boss 的标题 + 提示 + 幕次归属）。

## v0.2.216：三条一起修（胜利音 / 地图徽章白圆盘 / 本地化 key 漏翻）

用户 2026-10-30 反馈三件事，都拿到实证了。

### ① Boss 胜利音：原版是**给自己曲目发「进度=5 死亡段落」**

反编译 `MegaCrit.Sts2.Core.Models.Monsters.WaterfallGiant / SoulFysh` 等原版 Boss：**死亡时**调
`NRunMusicController.Instance?.UpdateMusicParameter("<boss>_progress", 5f)`
（1/2 = 一阶段/二阶段，**5 = 死亡/收尾**）。`sts2.dll` 里全部进度参数名（实测枚举）：
`the_kin_progress` / `queen_progress` / `kaiser_crab_progress` / `waterfall_giant_progress` /
`soulfysh_progress`（**原版没有下划线**）/ `vantom_progress` / `ceremonial_beast_progress`。

我们的 Boss 是角色 ⇒ 没人发这个参数 ⇒ 死亡时曲目被我们直接切回本幕音乐
⇒ 听起来就是「小怪战斗结束」那种淡出（用户原话：「有点像打完小怪的音效（音乐淡出）」）。

修法：最后一只 Boss 倒下时 -> `SetParam(该曲目的 _progress, 5f)` -> 让死亡段落放
`DeathPlaybackSeconds`（5 秒）-> 再用 `SceneTreeTimer` 还原原版音乐
（**不依赖战斗内每帧驱动**：战斗一结束那些驱动就停，v0.2.209 的「4 秒后还原」就是因此从未跑起来）。
日志：`BGM：… ⇒ 给「queen_progress」发死亡段落 5（原版 Boss 死亡就是这么放的；5 秒后还原原版音乐）`
+ `BGM：已安排 5 秒后还原原版音乐（SceneTreeTimer）`。
v0.2.214/215 那段「补放 `debug_audio/victory.mp3`」已删除（那是运行历史界面的音效，不是胜利音）。

### ② 进阶 10 第三幕第二颗地图徽章变成「一大块白圆盘」

`BossMapPointCharacterIconPatch` 以前把 `%PlaceholderOutline` 换成**自己生成的纯白圆盘**。
原版那颗是徽章背后的**深色**实心盘，会被地图染色；白圆盘在部分状态下不染色 ⇒ 一大块白，
剪影又是白色（靠染色才变深）⇒ 看起来就是「白圆 + 灰星星」。
修法：**不再覆盖 outline**（保留原版深色圆盘，路线虚线照样被挡住），只替换 `%PlaceholderImage` 的剪影。

### ③ 顶栏 / 战斗提示显示 `encounters.REGENT_STAR_BOSS_ENCOUNTER.title`

`content/localization/{zhs,eng}/encounters.json` 漏了**辉星储**（`RegentStarBossEncounter`，后加的档位）。
现在补齐 `REGENT_STAR_BOSS_ENCOUNTER` 以及全部 8 个 `*_NORMAL_ENCOUNTER`（调试战斗用的那批），
中英文都补、键集比对一致（各 64 键）。**PCK 已重建并验证**（`extract_pck_file.gd` 抽出来确认含新 key）。

## v0.2.215：用户澄清「往昔之章已卸掉、就是原版地图」⇒ 上一轮的解释作废，胜利音改为自己补

用户 2026-10-30：「我给往昔之章去掉了已经，就是原版地图」。
核对 `godot.log`（10:35 那次启动）的模组清单：**ActsFromThePast 已不在加载列表里**（只剩 14 个模组），
所以上一轮「模组配乐的幕没有 CombatEnd 段落」的解释**不成立**；`_currentTrack=event:/music/act2_a1_v2`
就是**原版**第二幕地图音乐（这个名字在 `sts2.dll` 里没有字面量，因为原版是拼出来的，不代表它是模组的）。

### 再挖一层：原版的战斗胜利音在哪

- 反编译 `CombatManager.EndCombatInternal`：战斗结束只调 `NRunMusicController.UpdateTrack()`
  ⇒ `update_global_parameter("Progress", GetTrack(roomType))`，战斗房且 `!IsInProgress` ⇒ `CombatEnd`(7)。
  也就是「胜利」= **本幕地图音乐**的那一段。普通小怪战之所以听得到，是因为那一场放的就是本幕地图音乐。
- 扫 `sts2.dll`（853MB）/ `SlayTheSpire2.pck`（1.9GB）：`event:/music/…` 只有 4 条字面量，
  **没有任何 victory/stinger 事件** —— 说明真正的胜利音是 FMOD **bank 内部**的段落，文件名层面查不到。
- 但 PCK 里确实有 **`res://debug_audio/victory.mp3`**（连同 42 个同目录音效，`.import` 齐全）。
  这是原版资源里唯一一个明确叫「victory」的音频。

### 修法

1. Boss 战赢了（`OnLastBossDied` / `OnFightEnded`）在还原音乐之后，**由我们补放一次**
   `res://debug_audio/victory.mp3`（`ResourceLoader.Load<AudioStream>`，`.mp3` 走 `.import`；
   拿不到资源就依次退到往昔之章的 `boss_victory_stinger_*`，都没有就静默跳过）。
   播完自动 `QueueFree`；日志 `BGM 胜利段：补放胜利短音 res://debug_audio/victory.mp3（第 N 幕；…）`。
2. 还原时在 `StopCustomMusic()` 之后**再补一次 `controller.UpdateTrack()`**：
   `StopCustomMusic` 的参数是紧跟着新起的事件实例设下去的，可能被吞（用户反馈「音乐有切换但不是胜利音」）；
   `UpdateTrack()` 正是游戏自己在战斗结束做的同一件事，补一次只是同值重复。
3. 新增每局一次的 `BGM 本幕音乐库：<Act.MusicBankPaths>` 诊断行。

## v0.2.214：「音乐切了但不是胜利音」——原版本来就没有独立胜利主题

用户 2026-10-30：「我刚刚打过了 2 层 boss，boss 死亡时音乐有切换但并不是胜利的音效」。
`mod_log.txt` 实证（第二幕 涂毒贼 `SILENT_ENVENOM`）：

```
[10:32:48] BGM：SILENT_ENVENOM_BOSS_ENCOUNTER → 「帝皇蟹」event:/music/act2_boss_kaiser_crab
[10:35:26] BGM：Boss 被移出房间 ⇒ 立刻还原原版音乐（曲目=event:/music/act2_boss_kaiser_crab）
[10:35:26] ♪ StopCustomMusic（调用者 BossBgm.Restore ← BossBgm.OnFightEnded）| _currentTrack=event:/music/act2_a1_v2
```

即：v0.2.213 的「当场还原」生效了（音乐确实切回去了），但**原版 StS2 并没有独立的胜利主题**——
`CombatManager` 战斗结束时只是 `update_global_parameter("Progress", 7)`（CombatEnd），
让**本幕地图音乐**切到「战斗结束」那一段过渡。反编译 `sts2.dll` 里全部 `event:/music/…` 只有
`act1_a1_v1 / act1_a2_v2 / act3_a1_v1 / act3_a2_v1` 四条，**没有任何 victory/stinger 事件**。
而这一局第二幕是**往昔之章（Acts from the Past）的幕**（`act2_a1_v2` 不是原版事件），
它那套曲目有没有 CombatEnd 段落全看它自己 ⇒ 结果就是「切回主题曲，但不是胜利音」。

往昔之章**自带**胜利短音（反编译它的 DLL 与 PCK 确认）：
`res://ActsFromThePast/bgm/boss_victory_stinger_1..4.ogg` —— 平时是它自己的 Boss 被杀时放的；
我们的角色 Boss 顶掉了它的 Boss ⇒ 没人放。

**修法**：Boss 战赢了（`OnLastBossDied` / `OnFightEnded`）在还原音乐之后，由我们**补放一次**胜利短音：
按幕次取 `boss_victory_stinger_{幕}`（拿不到就退 1 号），`ResourceLoader.Exists` 失败就静默跳过、
原版过渡照旧；播完自动 `QueueFree`。日志：
`BGM 胜利段：补放胜利短音 boss_victory_stinger_2.ogg（本幕音乐库…：<bank>）`。
另外新增**每局一次**的诊断行 `BGM 本幕音乐库：<Act.MusicBankPaths>`，下次能直接看出这一幕用的是谁的音乐。

## v0.2.213：反过来了 —— 胜利音乐恰恰需要「当场还原」（v0.2.209 的判断是错的）

用户 2026-10-30：「我刚刚打了一局，1 层 boss 死后没有播放胜利音乐」。`mod_log.txt` 实证（愤怒战）：

```
[10:15:57] BGM：IRONCLAD_ANGER_BOSS_ENCOUNTER → 「同族小队」event:/music/act1_boss_the_kin
[10:17:20] BGM：Boss 被移出房间（本场战斗结束） ⇒ 曲目再放 4 秒后还原原版音乐（待还原=3999ms）
[10:17:20] ♪ UpdateTrack（调用者 EndCombatInternal）… 曲目=event:/music/act1_boss_the_kin | _currentTrack=event:/music/act1_a1_v1
[10:17:36] ♪ StopMusic（调用者 CloseToMenu）… 待还原=-11392ms 后还原
```

两条结论：

1. **`Progress=7` 是给 `_currentTrack`（本幕地图音乐）用的段落参数**，战斗结束那一刻游戏只是
   `update_global_parameter("Progress", 7)` —— 而 Boss 那首自定义曲目（`act1_boss_the_kin` /
   `act3_boss_queen` / `act2_boss_kaiser_crab`）**不响应它** ⇒ 停在我们曲目上 = 一声不响。
   胜利段落真正出现的地方是 **`StopCustomMusic()`**：它 `stop_music` + 把 `_currentTrack` 重新放起来
   + `Progress=7`（CombatEnd）。也就是 v0.2.176～0.2.208 一直在做的事。
2. **「4 秒后再还原」根本没跑到**：`待还原` 从 3999ms 直接变成 -11392ms（回到主菜单那一刻早已过期），
   中间没有任何 `StopCustomMusic` 调用 —— 战斗结束后战斗内每帧驱动已停，而备用驱动
   `NMapScreen._Process` 在奖励界面时地图节点还没实例化。这条延迟路线本来就不可靠。

修法（v0.2.213）：

- `BossBgm.OnLastBossDied()` / `OnFightEnded()` 都改成**当场 `Restore()`**（`StopCustomMusic()`）。
  `BeforeRemovedFromRoom` 实测就在 `EndCombatInternal` 之前（日志顺序），所以不会把胜利段落掐掉；
  随后游戏再设一次 `Progress=7` 也只是同值重复。
- `TickRestore()` 降级成纯兜底（不在战斗里 + 我们还占着曲目 ⇒ 还原），不再有延迟计时。
- 保留 v0.2.210 的两条：混匹「本场遭遇自带专属曲目 ⇒ 不接管」、`_currentTrack == null` 时补 `UpdateMusic()`。
- 「音乐调用追踪」（v0.2.209 加的 `♪ 音乐调用 …`）这次直接定位了问题，保留。

顺带确认：这一局是**往昔之章（Acts from the Past）的幕**（`_currentTrack=event:/music/act1_a1_v1`），
所以小怪战也是一代配乐 —— 与「所有音乐变成一代」那条反馈一致，与我们无关（见下条）。

> 打包记录（用户 2026-10-30「打包」，随后因初始化崩溃必须重打）：`.dist\CharacterBosses-v0.2.212.zip`
> （13,116,459 字节，09:57 生成）= `CharacterBosses.dll`(695,296) + `CharacterBosses.json`(0.2.212) +
> `CharacterBosses.pck`(12,860,972) + `安装说明.txt`(73,844)；三个二进制与本机 `mods\CharacterBosses\` 哈希一致。

## v0.2.212：紧急修「模组程序集DLL初始化失败」（v0.2.211 装上去就弹）

用户 2026-10-30 截图 + `godot.log` 实证：

```
[ERROR] Exception thrown when calling mod initializer of type CharacterBosses.ModEntry:
 ---> HarmonyLib.HarmonyException: Ambiguous match for HarmonyMethod[
      (class=MegaCrit.Sts2.Core.Nodes.Audio.NRunMusicController, methodname=UpdateTrack, type=Normal, args=undefined)]
 ---> System.Reflection.AmbiguousMatchException: Ambiguous match found for '… Void UpdateTrack()'.
   at HarmonyLib.AccessTools.DeclaredMethod(...)
   at HarmonyLib.Harmony.PatchAll(Assembly assembly)
   at CharacterBosses.ModEntry.Initialize()
```

**根因**：`NRunMusicController` 有**两个** `UpdateTrack`
（`public void UpdateTrack()` 与 `private void UpdateTrack(string label, float trackIndex)`，反编译确认），
而 v0.2.209 新加的「音乐调用追踪」用 `nameof(NRunMusicController.UpdateTrack)` **没写参数类型** ⇒
Harmony 无法判定 ⇒ 抛 `AmbiguousMatchException` ⇒ **`PatchAll` 中断 ⇒ 整个模组一个补丁都没挂上**
（挂掉的不只是音乐追踪，是我们全部 80+ 个补丁）。

**修法**：`Patches/MusicTracePatch.cs` 里 7 个音乐补丁 + `MusicRestoreDriverPatch`（`NMapScreen._Process`）
全部写死参数类型（`new Type[0]` / `new[]{typeof(string)}` / `new[]{typeof(double)}` …）。
**新增 `tools/check-harmony-attributes.ps1`**：列出所有「没写参数类型的 `nameof` 目标」（当前 70 处，都是历史补丁、
已确认无重载；音乐那 8 个已不在名单里），以后改补丁前先跑一遍。

**教训（已写进 cerebrum Do-Not-Repeat）**：`[HarmonyPatch]` 只要是 `nameof` 写法就必须给参数类型；
一个补丁类炸掉 = 整个模组不生效，症状是启动时弹「程序集DLL初始化失败」。

## v0.2.211：多人不同步（`godot (5).log` + `mod_log (13).txt`）+ 顺带挖到一条 BGM 静音

用户 2026-10-30 给了 `godot (5).log` + `mod_log (13).txt`，说「多人不同步」。

### 证据：房主场上 2 只 Boss，客人只有 1 只

`godot (5).log` 里只有 **1 次**不同步：

```
[ERROR] State divergence detected! Checksum with ID 291 for client 7656119… doesn't match host's!
Context: After player turn start. Local: 845204345. Remote: 3315705523.
LOCAL STATE DUMP / REMOTE STATE DUMP（房主 = 7656119…，在 E:\ 那台机器上）
```

逐条 diff 两份 dump（129 行 vs 139 行，22 行差异）⇒ **真正的差异只有一处**：

| | 房主（LOCAL） | 客人（REMOTE） |
|---|---|---|
| 生物 | `DEFECT_BOSS`(225/225) + **`NECROBINDER_BOSS`(216/216, NOT_YET)** | 只有 `DEFECT_BOSS` |
| 房主玩家身上的能力 | 1 个 `LOCK_ON_POWER` | **0 个** |
| `RNG counter Niche` | Counter 10 | Counter 9 |

即：**「灾厄骨」这只搭子 Boss 只在房主那边造出来了**，客人那边整只不存在；
锁定图标数量与 `Niche` RNG 计数都只是它的**后果**（两只 Boss ⇒ 两把锁；多造一只怪 ⇒ 多消耗一次 Niche 随机）。

### 为什么会分叉：版本不一致 + 旧版判定依赖本机状态

- `mod_log (13).txt` 的构建行是 **v0.2.204**，`变体判定…（换场后才会重算；避免一场里中途换剧本）` 也是旧文案
  ⇒ **那台机器（房主，`E:\SteamLibrary`）跑的是 v0.2.204**，而本机已是 v0.2.208→v0.2.211。
  两个版本的「阵容怎么选」实现不同（v0.2.207 才把判定全改成只看同步数据），**跨版本联机必然分叉**。
- 旧版（≤0.2.206）这条路上有三个已知的本机相关输入：① 读不到本局种子时用**随机兜底种子**
  （`Random.Shared.NextInt64() ^ TickCount64`，每台机器不一样）；② 变体判定用本机的 `_currentFightAct/_currentFightIndex`；
  ③ 会把「按种子猜的答案」锁存。三者都会让一端多一只/少一只搭子。

### 这一版的硬化（不管哪一端，只要版本一致就更难分叉）

1. `MultiBossPlan.SyncedFightOf`：判断「我是本幕第几场」时**优先用同步数据**
   （`act.BossEncounter` = 第一场、`act.SecondBossEncounter` = 第二场）——这是房主写进本局状态、客人读到同一份的结论；
   只有两个槽位都不占（例如混匹时我们只是搭子）才退回「按种子反推」。
2. **两端指纹日志**（每台机器各打一行，必须一模一样）：
   ```
   多 Boss 生成指纹※ 第 1 层第 1 场 · 玩家=2 · 阵容=[DEFECT_DARK,NECROBINDER_DOOM] · 我=DEFECT_DARK · 伙伴=[NECROBINDER_DOOM=…]
   ※本机生成※ DEFECT_DARK_BOSS_ENCOUNTER → 怪物 2 只 [MONSTER.DEFECT_BOSS,MONSTER.NECROBINDER_BOSS]
   ```
   下次两份 `mod_log` 一对比，是「阵容不同」「玩家数读到不同」还是「模型取不到 ⇒ 跳过」，一眼就能看出来。

### 顺带：同一条日志里的「整场没音乐」

`godot (5).log` 第 12620 行：

```
cannot find music path: event:/music/act1_b_boss_waterfall_giant
```

一层有**两套 bank**（`act1_*` / `act1_b_*`），哪套被加载取决于这张地图用哪套配乐 ⇒
「幕次对」不等于「事件存在」。而 `PlayCustomMusic` 是 `stop_music` + `update_music`：
事件找不到时原版音乐**已经被停了** ⇒ 那一整场没音乐（黑球机 / 碎屑储 / 灾厄骨 都会中）。
修法：切曲目**之前**先问 proxy `load_act_bank(bank, 事件)`（外面套官方重试 `ActBankLoadRetry.Run`，3×50ms），
本幕 bank 一个都不认 ⇒ 不接管、保留原版 Boss 音乐，并记一行说明。

### 复测要点（v0.2.211）

- **两台机器都升到 v0.2.211**（版本不一致时联机一定不同步，这一点没有代码办法绕开）。
- 开 Boss 战时比对两边 `mod_log.txt` 的 `※多 Boss 生成指纹※` / `※本机生成※` 两行：必须一模一样。
- 万一还不同步：v0.2.208 起两端都会打 `⚠ 不同步状态摘要（检测到不同步）`，把**两台的 `mod_log.txt`** 一起给过来即可逐项 diff。

> 打包记录（用户 2026-10-30「打包」）：`.dist\CharacterBosses-v0.2.210.zip`（13,113,305 字节，23:06 生成）
> = `CharacterBosses.dll`(691,712) + `CharacterBosses.json`(0.2.210) + `CharacterBosses.pck`(12,860,972) +
> `安装说明.txt`(70,341)；DLL/JSON/PCK 的 SHA256 与本机 `mods\CharacterBosses\` **逐一核对一致**。
> 安装说明新增「v0.2.210 重要变更（自 v0.2.208 起）」5 条（胜利音效 / 混匹不抢曲 / 一代音乐补音乐 / 多人只切一次 / 音乐调用追踪）。

## v0.2.210：把 BGM 这条线上的「同类问题」按 Boss 逐个过了一遍

用户 2026-10-30：「你检查检查其它 boss 有没有类似的问题」（接 v0.2.209 的三层 BGM 问题）。

### 逐个遭遇的结论（`BossBgm.FmodByEncounter` 全表）

| 幕 | 遭遇 | 曲目 | 幕次守卫 | v0.2.209 之后 |
|---|---|---|---|---|
| 1 | 愤怒战 IRONCLAD_ANGER | `act1_boss_the_kin` | act0 ✔ | 与 v0.2.209 同一套逻辑（死亡后不停、4 秒后还原）⇒ 胜利段落按原版 |
| 1 | 黑球机 DEFECT_DARK | `act1_b_boss_waterfall_giant` / ②`STS_Boss1` | act0 ✔ | **②那条有独立坑，见下** |
| 1 | 灾厄骨 NECROBINDER_DOOM | `act1_b_boss_soul_fysh` | act0 ✔ | 同愤怒战 |
| 1 | 碎屑储君 REGENT_DEBRIS | `act1_b_boss_waterfall_giant` | act0 ✔ | 同愤怒战 |
| 1 | 奇巧贼 SILENT_SLY | **不接管**（原版乐加维林族母自己也没曲目） | — | 我们从不碰音乐 ⇒ 原版胜利段落天然正常 |
| 2 | 放血战 / 涂毒贼 / 灵魂骨 / 铸剑储君 / 物理机 | `act2_boss_kaiser_crab`（物理机 ②`STS_Boss2`） | act1 ✔ | 同愤怒战 |
| 3 | 刀贼 / 炼药贼 / 易伤战 | `act3_boss_queen` | act2 ✔ | v0.2.209 主修对象 |
| 3 | 巨石战 / 能力机 / 根除骨 / 无色储君 / 辉星储 | 兜底「永世沙漏」`act3_boss_queen` | act2 ✔ | 同上（也是这一版才补上 `queen_progress` 的开局发送） |

**结论：幕次对照全部正确**（每条曲目的 `Act` 与它出现的那一幕一致，不会误吞原版音乐），
「死亡后不停 + 延迟还原」是**通用**逻辑，所以 1/2 层那些 Boss 的胜利段落顺带一起修好了。

### 又挖到两个真问题（都在这一版修掉）

1. **混匹时会把「人家自己的主题曲」换成我们的兜底曲**（`A4 与原本 Boss 共用匹配池` 打开后才会遇到）。
   原版女王 / 无厌沙虫 / 墨影幻灵这些遭遇**自带** `EncounterModel.CustomBgm`（⇒ `HasBgm == true`），
   原版 `CombatManager.StartCombatInternal` 已经给它放好专属曲目了；我们的 `OnCombatStart` 会在 Boss 第一回合
   再 `PlayCustomMusic` 一次（`stop_music` + 换曲）⇒ 把人家那首顶掉。
   修法：`OnCombatStart` 开头查 `CombatState.Encounter.CustomBgm`，非空就**不接管**，并记
   `BGM：本场遭遇自己有专属曲目（…）⇒ 不接管，让原版/那个模组自己放（胜利段落也由它自己走）`。
   （我们自己的遭遇一个都没写 `CustomBgm` ⇒ 正常流程不受影响，反编译确认 `HasBgm => CustomBgm != ""`。）

2. **一代音乐（A3 = ②）那条路：打完黑球机 / 物理机 / 能力机之后整段没音乐，连环境音都没了。**
   根因：`TryPlayGen1` 为了压掉原版音乐调 `controller.Call("StopMusic")`，而 `StopMusic` =
   `stop_music` + `stop_ambience` + **`_currentTrack = null`** + `UnloadActBanks()`。
   回头我们还原时调 `StopCustomMusic()`，它内部是
   `stop_music` + `if (_currentTrack != null) { update_music(_currentTrack); Progress=7; }`
   —— `_currentTrack` 已被清空 ⇒ **它只发 stop_music，不放回任何音乐**（bank 也被卸了）。
   修法：`Restore()` 里读完 `_currentTrack`（反射，字段名来自反编译）若为空 ⇒ 补一次 `controller.UpdateMusic()`
   （重新按本局种子解析本幕 BGM + `LoadActBank` 载回 + `UpdateAmbience` 刷环境音），并记日志说明原因。
   注：一代 ogg **不是 FMOD 事件**，`Progress=7` 对它无效 ⇒ 这条路上本来就没有「原版胜利段落」，属已知差异。

### 复测看点（v0.2.210）

- 混匹抽到「原版女王 / 沙虫 / 墨影幻灵 + 我们的角色 Boss」⇒ 应听到**原版那首**，日志有 `本场遭遇自己有专属曲目 ⇒ 不接管`。
- 把 A3 切成「②一代 Boss 音乐」，打黑球机/物理机/能力机 ⇒ 战斗结束后 4 秒，音乐与环境音应**都回来**
  （日志：`BGM：还原原版音乐（…）` + `还原时发现 _currentTrack 为空 … 补一次 UpdateMusic()`）。

## v0.2.209：三层 Boss 战「BGM 放一会就没了 / 没有胜利音效」

用户 2026-10-30：「3 层 boss 战 bgm 放一会就没了，也没有胜利音效。」

### 胜利音效：找到根因（反编译实证），是我们自己掐的

`CombatManager.EndCombatInternal`（战斗结束）最后一步：

```csharp
NRunMusicController.Instance?.UpdateTrack();
```

而 `UpdateTrack()` = `update_global_parameter("Progress", GetTrack(roomType))`，
`GetTrack` 里 `roomType.IsCombatRoom() && !CombatManager.Instance.IsInProgress ⇒ MusicProgressTrack.CombatEnd`（= **7**）。

⇒ **胜利段落不是另起一首曲子，而是给我们「当前正在响的那条曲目」设 `Progress=7` 切出来的。**
我们在 v0.2.176 把 `BossBgm.Stop()`（= `StopCustomMusic()` = `stop_music` + 按 `_currentTrack` 重播）
提前到「最后一只 Boss 倒下那一帧」，而那一帧**就在**战斗结束前后 ⇒ `stop_music` 把刚切出来的胜利段落一起掐掉；
v0.2.175 把它放在 `BeforeRemovedFromRoom` 同样掐（那一步实测也在死亡同一秒，见下）。

**证据（本机 `mod_log.txt`，22:48:48）**：`DEFECT_BOSS 已死` → `战斗结束（BeforeRemovedFromRoom）：释放 1 套 Boss 面板`
全部发生在**同一秒**；而整份日志里**一条**「BGM：最后一只 Boss 倒下」都没有 ⇒ 停音乐的时机根本轮不到 `IsPlaying` 判断。

### 修法

1. 新增 `BossBgm.OnLastBossDied()`：最后一只 Boss 倒下时**故意不停音乐**，只放掉「占着音乐」的标记，
   记账「`VictoryKeepMs`（4 秒）后再还原」⇒ 游戏自己用 `Progress=7` 把胜利段落在我们曲目上放完。
2. 新增 `BossBgm.OnFightEnded(reason)`（`BeforeRemovedFromRoom` 两处改调它）与 `BossBgm.TickRestore(nowUsec)`：
   到点且**确实不在战斗里**才 `StopCustomMusic()`；万一又进战斗则顺延 2 秒。
3. 延迟还原有两个驱动：战斗内每帧驱动（`BossFrameTickPatch` 慢车道）+ **新增 `MusicRestoreDriverPatch`**
   （挂原版 `NMapScreen._Process`，地图节点整局常驻，离开战斗房后照样跑 —— 否则那 4 秒空档里没人还原，
   Boss 音乐会被拖进地图）。
4. **多人重复切曲**：`OnCombatStart` 以前每只 Boss 的战斗开始钩子都调一次 ⇒ 同一秒 4 次
   `PlayCustomMusic`（= 音乐被从头切 4 遍，日志里能看到同一秒 4 行 BGM）。现在同「战斗状态 + 遭遇」只接管一次
   （比对 `ReferenceEquals(CombatManager.DebugOnlyGetState())`，所以「退出再开一局抽到同一只 Boss」不会误判成已接管）。
5. 女王那首（= 第三幕兜底曲，永世沙漏 / 刀石战 / 能力机 / 根除骨…）以前**从来没发过** `queen_progress`
   （只有易伤战 / 炼药贼两项会发）⇒ 现在开局与每回合统一发一阶段值（与「原版女王自己会发的那一次」对齐）。
6. 新增 `Patches/MusicTracePatch.cs`（**纯观察，不跳过任何原版方法**）：挂在 `NRunMusicController` 的
   `PlayCustomMusic / StopCustomMusic / StopMusic / UpdateMusic / UpdateTrack / UpdateMusicParameter / UpdateCustomTrack`，
   战斗期间打 `♪ 音乐调用 <方法>（调用者 <非我们、非 NRunMusicController 的第一帧>）| 曲目=… 占着=… 待还原=… | _currentTrack=…`，
   每个方法 1.5 秒最多一行、全场 200 行上限。**下一份日志就能直接看出「放一会就没了」是谁干的**
   （原版哪一步，或往昔之章 / 第四幕 / 崩坠 / RitsuLib 等模组）。

### 复测看点（v0.2.209）

- 第三层 Boss 打死 ⇒ 应该听到**胜利段落**（`Progress=7`），4 秒后回到原版音乐；
  日志应出现 `BGM：最后一只 Boss 倒下 → **不停音乐**…` 与 `BGM：还原原版音乐（战后延迟还原；原来在放 …）`。
- 多人（3~4 只 Boss）开战 ⇒ 只应有 **1 行** `BGM：… → 「…」`，其余是 `已经接管过了 ⇒ 不重复切换`，
  而不是同一秒 4 行。
- 若「放一会就没了」还在，把 `mod_log.txt` 给过来：`♪ 音乐调用 …（调用者 …）` 那几行会直接点名凶手。

## v0.2.208：从 `godot (4).log` 挖到的东西 + 加了对比用的探针

> 打包（用户 2026-10-29 第六次「打包」）：`.dist\CharacterBosses-v0.2.208.zip`（13,109,261 字节，22:07 生成）
> = `CharacterBosses.dll`(683,520) + `CharacterBosses.json`(0.2.208) + `CharacterBosses.pck`(12,860,972) +
> `安装说明.txt`(67,828)；DLL/PCK 哈希与本机 `mods\CharacterBosses\` 完全一致。
> 安装说明新增「v0.2.208 重要变更（自 v0.2.207 起）」2 条（不同步诊断摘要 / 锁定图标清理一场一次）。

用户 2026-10-29 给了 `godot (4).log`（房主 = `7656119…` 的日志）。里面 **5 次不同步**：

```
State divergence detected! Checksum with ID 593 for client 7656119… doesn't match host's!
Context: finished action execution PlayCardAction card: CARD.OUTRAGE ... targetid: 4
（另外 4 次：ID 29 / 36 / 17 / 38，动作分别是 OUTRAGE、STRIKE_IRONCLAD、OUTRAGE、STRIKE_IRONCLAD）
```

- 每次都是「**玩家打出一张指向 `targetid: 4`（＝静默猎手那格）的牌，动作结束那一刻**」校验和对不上；
- 房主的 `LOCAL STATE DUMP` 里，4 名真人玩家**每人身上恰好 1 个 `LOCK_ON_POWER`**，
  四只 Boss 的特殊效果也都在（`SLEEVE_TRICK / DEVIATION / RESONANCE / BOSS_PRIMAL_FORCE`），看着都正常；
- **但客人的 `LOCAL STATE DUMP` 不在我们手上**（`StateDivergenceMessage` 只带两个校验和数字），
  所以没法逐项 diff —— 这就是本轮要解决的「没数据」问题。

另外从 `mod_log (12).txt` 里看到一处**我们自己的浪费**（不是不同步主因，但会增加同步状态抖动）：

```
锁定图标实例 #1 创建（Ironclad, SILENT_BOSS）→ 跟踪锁定：SILENT_BOSS → Ironclad
锁定图标被摘除（第 1 次）→ 新战斗：把 Ironclad 身上的旧「跟踪锁定」摘掉了（1 份）   ← 我们自己的清理
锁定图标实例 #4 创建（Ironclad, SILENT_BOSS）                                       ← 于是又建一次
```

`CleanupStaleForNewFight` 是**每只 Boss 生成时都跑**（4 只 Boss = 同一秒跑 4 次），
会把本场刚挂上的图标当成「旧图标」摘掉 ⇒ 加一份删一份来回折腾。

### 本轮改动

1. **新增 `Patches/DivergenceStateProbe.cs`**：挂在原版 `ChecksumTracker.LogStateDivergence`
   （房主和客人都会走到）上，打一行 `⚠ 不同步状态摘要（检测到不同步）`，内容是
   - 每名真人玩家（按 `NetId` 排序）的**锁定图标实例数 + 每份的施加者**、HP/格挡；
   - 每只我们 Boss 的关键 power 层数（袖里乾坤 / 群情激奋 / 偏差认知 / 共鸣 / 带毒刺击 /
     原始力量 / 命运同担 / 群星荟萃 / 何人僭越 / 触媒）+ HP/格挡；
   - 我们算出来的**锁定分配表**（Boss→玩家/NetId）。
   ⇒ 下次复现时**两边各发一份 `mod_log.txt`**，把这些行并排比，就能直接看出是哪一项不一样。
2. `BossLockOn.CleanupStaleForNewFight` 改成**一场只清一次**（用当前 `ICombatState` 做门闩，
   `Reset()` 里清掉），不再「自删自建」。

### 下次复现要的东西（明天）

- 房主与客人**各自**的 `mod_log.txt`（含 `⚠ 不同步状态摘要` 那几行）；
- 客人的 `godot.log`（它的 `LOCAL STATE DUMP` 是另一半证据）。

拿到这两份就能定位到具体字段（我最怀疑的仍是 `LOCK_ON_POWER` 的实例数 ——
它历史上就被证实是同步校验的一部分，见 `CleanupStaleForNewFight` 的类注释：
「实机诊断包：local LOCK_ON_POWER:6 / remote:4 ⇒ 直接报多人数据不同步」）。

## v0.2.207：多人不同步 —— 变体判定把「种子猜测」锁死了

> 打包（用户 2026-10-29 第五次「打包」）：`.dist\CharacterBosses-v0.2.207.zip`（13,105,646 字节，21:59 生成）
> = `CharacterBosses.dll`(658,432) + `CharacterBosses.json`(0.2.207) + `CharacterBosses.pck`(12,860,972) +
> `安装说明.txt`(66,979)；DLL/PCK 哈希与本机 `mods\CharacterBosses\` 完全一致。
> 安装说明新增「v0.2.207 重要变更（自 v0.2.206 起）」1 条（多人不同步的根因与修法）。

用户 2026-10-29 给了 `mod_log (12).txt`（4 段会话，含 3 人局与 4 人局）反馈「有不同步」。
我们的日志里没有「不同步」字样，但**同一秒内**能看到这条铁证：

```
[21:00:23] 变体判定（本场锁定）：REGENT_STAR = True    ← 战斗还没开始，按种子猜的
[21:00:23] 拉 Boss 房间: act=2 incoming=ENCOUNTER.QUEEN_BOSS 已发=0
[21:00:23] act 2 boss #1: QUEEN_BOSS -> REGENT_COLORLESS_BOSS_ENCOUNTER（第二只）
[21:00:24] 变体判定：战斗状态对象变了 ⇒ 锁定清空（换场/新战斗）
[21:00:24] 变体判定（本场锁定）：REGENT_STAR = False   ← 真数据到了，答案变了
```

也就是说：`v0.2.196` 的「本场锁定」把**开战前按种子猜出来的答案**缓存进了 `LatchTable`。
只要某个客户端恰好在「遭遇还没就绪」时先问了一次，它整场都会用那个**猜出来的变体**
（剧本、卡组、特殊效果全不一样）⇒ 与另一个客户端对不上 ⇒ **多人数据不同步**。

### 修法（`Core/BossLineups.cs`）

1. **判据补上一条「同步数据」来源**：新增 `CurrentFightLineup()` ——
   拿**存档里的主 Boss 遭遇**（`act.BossEncounter` → `BossKeys.KeyOfEncounter`）去两层阵容里反查
   这属于**哪一场**，于是「静默/铁甲/储君用哪套设计」直接从**这一场的阵容**读出来（两端一致、不依赖本机记账）；
   `ComputeStarRegent` / `ComputeBoulder` / `ComputeBrewer` 的判据顺序变成
   **① 房间遭遇 → ② 存档主 Boss 反查的阵容 → ③ 种子（不可靠）**；
2. **只有「决定性」的答案才锁定**：`Latched` 改成接收 `Func<(bool Value, bool Decisive)>`，
   走 ①② 得到的结果才写进 `LatchTable`；**走 ③ 种子得到的答案一律不缓存**，
   下次调用重算 —— 等真数据到了自然就对了。
   日志也区分了：只有决定性答案才会打 `变体判定（本场锁定）：… （来自同步数据；换场后才会重算）`。

### 顺带确认（同一份日志里）

- `21:53:42` 那场 4 人局：`SILENT_BREWER=False / REGENT_STAR=False / IRONCLAD_BOULDER=True`
  正好对上阵容 `[DEFECT_AI, SILENT_SHIV, REGENT_COLORLESS, IRONCLAD_BOULDER]` ✔（遭遇就绪后判据是准的）；
- `act 2 boss #1 …（第二只）` 是因为读档时原版计数 `bossEncountersVisited` 已经是 1（存档在第一场之后），
  这属于原版同步数据，两端一致，不是 bug。

### 复测看点

- 多人读档/进 Boss 房：日志里**不应再出现**「先 True 后 False」这种同一场的翻转；
- 决定性答案的日志是「（来自同步数据；换场后才会重算）」，且每场每个变体只有一行；
- 若仍然不同步，请给**出错那台机器的 `godot.log`** —— 原版会把 `StateDiverged` 的具体字段打出来。

## v0.2.206：「带毒刺击」不再让触媒 / 毒性爆发波及玩家方

> 打包（用户 2026-10-29 第四次「打包」）：`.dist\CharacterBosses-v0.2.206.zip`（13,105,088 字节，21:48 生成）
> = `CharacterBosses.dll`(657,408) + `CharacterBosses.json`(0.2.206) + `CharacterBosses.pck`(12,860,972) +
> `安装说明.txt`(66,079)；DLL/PCK 哈希与本机 `mods\CharacterBosses\` 完全一致。
> 安装说明新增「v0.2.206 重要变更（自 v0.2.204 起）」两条：A4 子项（进阶 10 第三幕固定一场原本+一场角色）、
> 带毒刺击的触媒/毒性爆发修正。

用户 2026-10-29：「让涂毒贼的特殊效果『带毒刺击』不再会使『触媒』和『毒性爆发』也对玩家方产生效果，
并且删掉相关警告（就删那一个）」。

### 根因：`TriggerCount` 算错了「谁的触媒」

我们接管中毒结算时自己实现了一遍 `TriggerCount`，以前写的是**毒主人自己的触媒**：

```csharp
TriggerCountOf(poison) => min(poison.Amount, 1 + poison.Owner.GetPowerAmount<AccelerantPower>())
```

反编译原版 `PoisonPower.TriggerCount` 其实是**对手**：

```csharp
var opp = CombatState.GetOpponentsOf(Owner).Where(c => c.IsAlive);
return Math.Min(Amount, 1 + opp.Sum(a => a.GetPowerAmount<AccelerantPower>()));
```

也就是触媒属于「**下毒的那一方**」——它让**对手身上**的中毒多触发几次。写成「毒主人自己的」以后：
- 涂毒贼的触媒反而给她**自己**的毒加速（对玩家毫无影响）；
- 而**玩家**身上的触媒会把玩家**自己**的中毒多触发几次（＝打自己），
  「毒性爆发」的「立即触发」走的是同一条 `TriggerBlockableAsync` ⇒ 一起变成「对玩家方生效」。

### 修法

`PoisonStrikePower.TriggerCountOf` 改回原版口径：只累加**对手**（`CombatState.GetOpponentsOf`）里存活者的触媒层数；
顺手删掉已经没人用的 `AccelerantOf`。

本地化同步删掉那条警告（**只删这一条**，另一条关于「为你而死」的保留）：
- zhs：去掉「警告：触媒和毒性爆发的特别效果在本场战斗中对全场生效。」
- eng：去掉 “WARNING: The special effects of Accelerant and Outbreak apply to the whole combat in this fight.”

### 复测看点

- 涂毒贼打出**触媒** → 玩家身上的中毒多触发几次（她自己的毒不受影响）；
- 玩家打出**触媒** → **只**加成 Boss 身上的中毒（玩家自己身上的毒不再被加速）；
- 玩家打出**毒性爆发** → 只对敌方（Boss）生效；
- 「带毒刺击」描述里只剩「为你而死」那一条警告。

## v0.2.205：A4 子项「进阶 10 第三幕固定一场原本 + 一场角色」

用户 2026-10-29：「在混匹那加一条设置：进阶 10 的 3 层 Boss 固定为 1 原本 1 角色。
这个设置需要开启混匹，开启后…不再会有可能都是角色或都是原本的 boss。这个多人也是只看房主。」

### 实现

- 配置项：`charbosses_config.cfg` 的 `asc10Act3OneEach`（`BossConfig.Asc10Act3OneEach`）；
  **生效值 = 混匹开 && 本项开**（`Asc10Act3OneEach` 返回生效值，`Asc10Act3OneEachRaw` 返回勾选状态供界面显示）；
- `BossLineups.Asc10Act3PicksVanilla(fight)`：**整幕只抽一次**（salt 63），两场互补
  ⇒ 一定一场原版、一场角色（`SharePoolPicksVanilla` 是「每场各抽各的」，可能两场同性质）；
- `CharBossEncounters.ForVanillaBoss`：混匹开着时，若「本项开 && 幕 ≥ 3 && 进阶 ≥ 10」则走整幕互补抽取，
  否则仍走每场各抽；日志里会追加「（进阶 10 第三幕固定「一场原本 + 一场角色」）」；
- 多人：客人根本不读本机配置（v0.2.202 的客人分支），所以这项同样**只看房主**；
- 配置界面 A4 段下面加一行勾选框：
  - 勾上：`开（第三幕一场原版、一场角色）`
  - 不勾：`关（两场各抽各的）`
  - **混匹关着时**追加灰字：`（需要开启「与原本 Boss 共用匹配池」才生效）`。

### 复测看点

- 进阶 10 第三幕（A4 混匹 + 本项都开）：两场 Boss 战**必定一场原版、一场本模组角色**；
  哪一场是原版由种子决定、一幕开场即定，地图预告 = 实际；
- 只开混匹、不开本项：仍可能两场都是原版/都是角色（各抽各的）；
- 不开混匹：本项置灰提示，无任何影响；
- 联机：以房主为准。

## v0.2.204：A4 文案 + 配置文件存到「用户数据目录」

用户 2026-10-29（截图）两点：

### ① A4 的状态文字说反了

以前关着的时候写的是「关（**勾了**就一定是角色 Boss）」，跟实际相反。现在：

- **不勾** ⇒ `关（一定是角色 Boss）` —— 也就是完全替换；
- **勾上** ⇒ `开（原版 Boss 与角色 Boss 混抽）`。

### ② 配置文件不能再写进模组目录

用户担心（完全正确）：以后上创意工坊，玩家的模组目录是 `steamapps/workshop/content/...`，
更新会覆盖、也可能只读 ⇒ 用户设置会丢。

`BossConfig.FilePath` 改成**按优先级挑第一个可写目录**：

1. `%APPDATA%\SlayTheSpire2\mod_configs\CharacterBosses\charbosses_config.cfg`
   （用户数据目录；`mod_configs` 就是 BaseLib 存各模组配置的地方，放个子目录好找且不撞名）；
2. Godot 的 `user://mod_config\CharacterBosses\`；
3. 兜底才是 DLL 所在目录（以前的行为，仅当上面都不可写）。

每张候选目录都先做一次「写临时文件再删」的可写性测试（`IsWritable`），只读的创意工坊目录会被自动跳过。

**迁移**：新位置没有配置文件时，会把**旧位置（模组目录）里的 `charbosses_config.cfg` /
更老的 `charbosses_config.json`** 读出来写到新位置并删掉旧的，日志：
`配置：已把 charbosses_config.cfg（旧位置：模组目录）迁移到 <新路径>（用户数据目录，创意工坊更新不会覆盖它）`。

配置界面底部也改成显示**实际路径**（`BossConfig.DescribeConfigPath()`），不再写死 `mods\...`。

### 复测看点

- A4 那一行关着时显示「关（一定是角色 Boss）」；
- 底部「改动立刻保存到：<APPDATA>\SlayTheSpire2\mod_configs\CharacterBosses\charbosses_config.cfg」；
- 勾一个地图 → 关掉界面再开，状态仍然保留；模组目录里不再有 `charbosses_config.cfg`（已迁移走）；
- 日志里能看到那行「已把 … 迁移到 …」。

### 打包（用户 2026-10-29 第三次「打包」）

- `.dist\CharacterBosses-v0.2.204.zip`（12.5 MB，21:24 生成）：
  `CharacterBosses.dll`(655,360) + `CharacterBosses.json`(0.2.204) + `CharacterBosses.pck`(12,861,164) +
  `安装说明.txt`(64,707)；
- DLL / PCK 哈希与本机 `mods\CharacterBosses\` 里那份**完全一致**（已核对）；
- 安装说明新增「v0.2.204 重要变更（自 v0.2.200 起）」6 条：
  第四幕心脏兼容 / 往昔之章（非原版地图默认关）/ 设置以房主为准 / A4 共用匹配池 /
  配置文件换位置到用户数据目录（含自动迁移）/ 配置界面顺序 A1→A4。

## v0.2.203：配置界面顺序按编号排

用户 2026-10-29（截图，画面上是 A4 → A2 → A3）：「你这什么神秘排列顺序。以当前编号为准，
把顺序改成 1432，然后再把顺序调对」。

原因：A4「与原本 Boss 共用匹配池」是 v0.2.202 顺手加进 `BuildMapSection` **末尾**的，
而 `SetupConfigUI` 的调用顺序还是 A1 → A2 → A3 ⇒ 画面上成了 **1 / 4 / 2 / 3**。

修法：把 A4 抽成独立方法 `BuildSharePoolSection`，`SetupConfigUI` 改成
**`BuildMapSection`(A1) → `BuildMixedMatchingSection`(A2) → `BuildBgmSection`(A3) → `BuildSharePoolSection`(A4)**，
每段之间照旧插一条分割线；编号一个都没改（用户要求「以当前编号为准」）。

### 复测看点

- 打开「模组配置 → CharacterBosses」：自上而下应为
  **A1 要替换的地图 → A2 同幕混合匹配 → A3 Boss 战 BGM 来源 → A4 与原本 Boss 共用匹配池**；
- 四段的勾选框/状态文字与之前完全一致，功能不变。

## v0.2.202：设置以房主为准 + A4「与原本 Boss 共用匹配池」

用户 2026-10-29（两条新要求）：
> 「多人时，所有影响游戏的设置（除音乐外的所有）以房主的为准，避免不同步。
> 新增一个设置：与原本 Boss 共用匹配池。开启后，开启了替换 boss 的地图的 boss 会在这张地图原本的 boss
> 和这一幕本模组的 Boss 中随机抽取，而不是单纯的替换。多人时，如果开启此选项后抽取到本模组 boss，
> 则该 boss 成为这场 boss 战的主 boss，然后和完全替换一样 boss 人数 = 玩家人数。
> 进阶 10 的 3 层第一场 boss 战出现的所有 boss 不会再出现在第二场 boss 战的抽取范围中，
> 如果抽到的还是本模组的 boss 他们也不会成为这场战斗的副 boss。
> 这些抽取由种子决定，且在一幕的开始时就决定好了，不会出现 boss 预告与实际 boss 不符的情况。」

### ① 多人：设置以房主为准

- 新增 `Core/NetRole.cs`：`RunManager.Instance.NetService.Type`（`NetGameType`：
  Singleplayer / Host / Client / Replay）→ `IsHostOrSolo` / `IsGuest`；
- `CharBossEncounters.ForVanillaBoss` **客人分支**：多人里的非房主**完全不读本机配置**
  （地图开关、共用匹配池都不看），只跟随房主已经写进**同步状态**的结论：
  同步状态里是本模组的遭遇 ⇒ 返回它；否则 `return null`（保持原版）。
  日志：`客人端：第 N 幕Boss 跟随房主 ⇒ <遭遇>`。用 `[ThreadStatic] InGuestFollow` 防重入
  （`SecondBossEncounter` 的 getter 是我们补过的，客人分支里再读它会递归）。
- **音乐（A3）不在此列**：各机自便（用户明确「除音乐外」）。
- 配置界面 A1 顶部加了一句说明：多人时这些设置以房主为准。

### ② A4「与原本 Boss 共用匹配池」

- 配置项 `BossConfig.SharePool`（`charbosses_config.cfg` 的 `sharePool`，默认 **关**），
  配置界面「**A4 · 与原本 Boss 共用匹配池**」一行勾选框 + 说明；
- 抽取：`BossLineups.SharePoolPicksVanilla(act, fight)` = `Roll(act, salt 61+fight, 2) == 0`
  —— **只用「本局种子 + 幕 + 场」** ⇒ 一幕开始（生成房间）时就定下来，
  **地图预告 = 实际 Boss**，联机两端一致；
- 在 `ForVanillaBoss` 里（房主/单人路径、且该地图开了替换之后）：
  - 抽到「原本的 Boss」⇒ `return null`（这一场不替换），日志 `共用匹配池：第 N 幕第 M 场抽到原本的 Boss ⇒ 这一场不替换`；
  - 抽到「本模组的角色 Boss」⇒ 照旧走完全替换：**主 Boss = 阵容第 0 位，Boss 数 = 玩家数**
    （伴侣由 `MultiBossPlan.PartnersFor` 补），日志同名但写「抽到本模组的角色 Boss ⇒ 按完全替换处理」。

### ③ 进阶 10 第三幕：第一场出现过的 Boss 不再进第二场的抽取范围

- 2 人局走 `BossLineups.Build` 的 `usedInThisAct`（第一场的 Key 全部排除）；
- 3 人及以上走 `BuildAct3Pairs` 的**互补变体**（两场天然不重复，含搭子）；
- 于是「抽到的本模组 Boss 也不会成为第二场的副boss」自动成立。

### 复测看点

- 多人：**客人端配置文件里怎么勾都不影响实际 Boss**（房主说了算）；日志出现「客人端：…跟随房主」；
- A4 关（默认）：行为和以前一致（勾了替换就一定是角色 Boss）；
- A4 开：同一局里每一幕第一场/第二场各自抽一次，地图上预告的是原版 Boss 时实际也是原版，
  预告是角色 Boss 时实际就是角色 Boss（且 Boss 数 = 玩家数）；
- 进阶 10 第三幕：第一场打过的 Boss 不会在第二场再出现（含搭子）。

## v0.2.201：兼容「第四幕心脏」「往昔之章」

## v0.2.201：装了「第四幕心脏」「往昔之章」两个模组

用户 2026-10-29：「我加入了「第四幕心脏」模组…你需要保证本模组不会替换任何第四幕的 boss。
我还添加了「往昔之章」模组，它添加了 1 代的地图，每幕 1 张。当游戏中有任何非原版地图时，
它们的 boss 替换选项应当是默认关闭的。」

### ① 第 4 幕及以后：一律不碰

以前 `CharBossEncounters.ActTable` 有第 4 行（`{4,0,1}`「备用（将来若出现第四幕）」），
而 `ForVanillaBoss` 用的是 `Math.Clamp(act.Index, 0, 3)` ⇒ **第 4 幕的 Boss 也会被换成角色 Boss**。

修法（两处硬闸）：

- `CharBossEncounters.ForVanillaBoss`：开头 `act.Index >= 3` 直接 `return null`
  （四个切点——发现顺序 / 生成房间 / 读档校验 / 第二槽读取——全部走这个漏斗，所以一次就够）；
  日志每幕一次：`第 4 幕不在本模组覆盖范围内（只做前 3 幕）⇒ 该幕 Boss 保持原版，不做任何替换`；
- `MultiBossPlan.PartnersFor`：`CurrentActIndex >= 3` 直接不加伙伴 Boss（否则第 4 幕会被塞进我们的角色）。

### ② 非原版地图的 Boss 替换默认「关」

以前所有地图（含模组加的）都是「缺项 = 开」，而且 `BossConfig.Load()` 第一次见到新地图时
**直接补成 `true`** ⇒ 装了「往昔之章」以后，它那三张 1 代地图会被自动替换。

现在的规则：

- `BossConfig.IsMapEnabledById(entry)`：
  **用户显式设过 → 听用户的**；没设过 → **原版四张（OVERGROWTH / UNDERDOCKS / HIVE / GLORY）默认「开」，
  其余（模组加的图）默认「关」**；认不出 id 也按「关」；
- `BossConfig.IsMapEnabled(act)` 同上；读配置异常也改成按「关」处理（宁可不动别人的幕）；
- `Load()` 补齐地图清单时按 `IsVanillaMap(entry)` 给默认值，不再一律 `true`；
- **存盘结构 version 1 → 2 的迁移**：旧版把非原版地图都写成了 `true`，
  升级时一次性把它们**改回 `false`** 并打日志
  `配置迁移：N 张非原版地图的 Boss 替换已改回「关」…`；玩家想要就自己到「模组配置」里打开。

配置界面本来就把地图标成 `·原版` / `·其它模组`，所以现在打开「模组配置」会看到：
原版四张 = 已替换，往昔之章那几张 = 保持原版（可手动勾）。

### 复测看点

- 装了「第四幕心脏」：第 4 幕的 Boss **完全原版**，日志里只出现一次「第 4 幕不在本模组覆盖范围内…」；
- 装了「往昔之章」：地图上那三张 1 代地图的 Boss **默认不替换**；
  「模组配置 → CharacterBosses」里它们显示「·其它模组　保持原版」，手动勾上才替换；
- 原版四张图（密林/暗港/巢穴/荣耀）行为不变，仍是替换。

## v0.2.200：进阶 10 第三幕「第二个 Boss」的地图徽章

## v0.2.200：进阶 10 第三幕「第二个 Boss」的地图节点还是原版 Boss 图标

用户 2026-10-29：「现在进阶 10 的 3 层的第二个 boss 在地图上的图标不是角色剪影而是原版 boss」。

`Patches/BossMapPointCharacterIconPatch.cs` 里判「这颗节点是第几只 Boss」原本只有一条：

```csharp
var encounter = ReferenceEquals(point, runState.Map.SecondBossMapPoint) ? act.SecondBossEncounter : act.BossEncounter;
```

两个坑：

1. **对象引用不可靠**：节点上的 `Point` 与 `runState.Map.SecondBossMapPoint` 在读档 / 地图重建后
   未必是同一个 `MapPoint` 实例 ⇒ 判成「第一只」⇒ 拿第一只的遭遇去判断，
   而第一只常有专属美术 ⇒ 直接 return，第二颗就保持原版占位图；
2. **第二槽可能还是 null**：进阶 10 第三幕的地图绘制可能早于「第二槽被我们替换」
   （`SecondBossEncounterGetPatch` 只在 `__result != null` 时才替换）⇒ 同样什么都做不了。

修法（三重判据 + 兜底）：

- `SamePoint(a, b)`：引用相同**或坐标相同**（`MapPoint` 的私有 `coord` 字段，用 Traverse 读）；
- 候选顺序按「是不是第二颗」排，然后**只认「我们自己的、且会用占位图的」遭遇**；
- `ShowingPlaceholder(image)`：读节点当前贴图的 `ResourcePath`，和 `TheKinBoss.BossNodePath` 比对
  —— 如果不是占位图且又不是第二颗，就不动它；
- **兜底**：第二颗但 `act.SecondBossEncounter` 仍为 null 时，用确定性阵容
  `Core.BossLineups.For(act.Index, 1)[0]` → `Core.BossKeys.EncounterOf(...)` 推出第二场主 Boss 再取角色徽记，
  日志：`地图徽章：第二场 Boss 槽还是空的 ⇒ 按阵容推出 <KEY>（<遭遇>）来取角色徽记`。

### 复测看点

- 进阶 10 第三幕：**两颗** Boss 节点的徽章都是角色剪影（第一颗 + 第二颗）；
- 日志里若出现上面那行「按阵容推出 …」，说明确实踩到了「第二槽为 null」；
- 若某一颗仍然不对，把该局面下 `第 3 层第 1/2 场阵容：…` 那两行一起发我。

### 打包（用户 2026-10-29「打包」）

- `.dist\CharacterBosses-v0.2.200.zip`（12.5 MB，20:42 生成）：`CharacterBosses.dll`(649,216) +
  `CharacterBosses.json`(0.2.200) + `CharacterBosses.pck`(12,861,164) + `安装说明.txt`(61,986)；
- DLL / PCK 哈希与本机 `mods\CharacterBosses\` 里那份**完全一致**（已核对）；
- 安装说明新增「v0.2.200 重要变更（自 v0.2.182 起）」11 条（多人不同步 / 三层分场 / 收尾停顿 /
  卡停半空 / 进阶10 第二颗徽章 / 愤怒停顿 / 鬼祟 / 文案 / 两个计数字 / 涂毒警告 / 巨石战剧本）。

## v0.2.199：多人「回合收尾」停顿随回合递增 —— 出牌队列残留

用户 2026-10-29：「单人可以说完全没有停顿。多人则是感觉前 2 个回合还好，第三回合就明显感觉长了。」

日志核对（同一场 4 人 Boss 战，每回合的 `敌方回合结束派发 … +Nms`）：

```
回合 1: +594ms
回合 2: +1094ms
回合 3: +2578ms
回合 4: +2609ms
回合 5: +2625ms
回合 6: +2625ms
回合 7: +2641ms
回合 8: +2609ms
```

另一场开头是 `+156 / +110 / +125 / +110`，然后 `+609 / +1125 / +2641` —— **同样的「越打越久、到 ~2.6s 封顶」**。
而同一时间我们自己的收尾一直是 `球 0ms / 遗物 0ms / 诅咒 0ms / 弃手牌 0ms`（「最后一张牌后 0ms」）
⇒ 那段时间花在**原版回合收尾流程里的等待**，而且**随回合数增长** ⇒ 说明有东西在**累积残留**。

最符合的累积源：`NCardPlayQueue`。我们 Boss 的牌是 `CardCmd.AutoPlay` 打的，
原版把「非本机玩家出牌」的卡面塞进 `NCardPlayQueue._playQueue`；
v0.2.185 我们只是把卡面**隐藏**，item 一直留着 ⇒ **每回合每只 Boss 3~4 张牌 = 一堆残留**，
回合收尾那条「等队列/等动画」的流程就越晚结束（等不到就等到超时 ≈2.5s，正好对上封顶值）。

修法（`Patches/StrayBossCardQueuePatch.cs`）：

- `Reap()` 不再只隐藏，改用原版接口 `RemoveCardFromQueueForCancellation(card, false)`
  **真正把 item 摘掉**（内部 Kill 补间 + 从 `_playQueue` 移除 + 让卡面淡出后 `QueueFree`）；
  万一抛异常退回「只隐藏」，不会把界面搞崩；
- 新增 `BossCardQueueNoThrowPatch`：`RemoveCardFromQueueForExecution(card)` 找不到那张牌时
  **直接跳过**（原版会抛 `InvalidOperationException`），免得「我们已经摘掉」反而把出牌流程炸掉。

### 复测看点

- 多人同一场里，`敌方回合结束派发 … +Nms` 应该**不再随回合增长**（希望稳定在几十~几百 ms）；
- v0.2.198 加的 `原版回合结束步骤：<步骤> = Nms` 会指出剩余时间花在 `WaitUntilQueueIsEmpty…`
  还是 `DoTurnEnd` / `AwaitTurnEndAndSwitchSides`；若还有 ~2.5s 且落在队列等待上，
  下一步就改成「出牌动作在动作队列里正常收尾」（不只是节点层面）。

## v0.2.198：给原版「回合结束」各步骤挂计时探针

## v0.2.198：那 2.6 秒已经确认**不在我们代码里**，探针指向原版流程

用户 2026-10-29 的实时日志（v0.2.195 的探针已生效）：

```
[20:25:49] 出牌阶段结束: 共 4057ms（最后一张牌后 0ms）
[20:25:49] 回合收尾耗时: 球 0ms / 遗物 0ms / 诅咒 0ms / 弃手牌 0ms（我们这边总计 4057ms）
[20:25:55] 敌方回合结束派发（BeforeSideTurnEnd）：距我们上次回合收尾 +2625ms
```

⇒ 打完最后一张牌之后，**我们自己这边的收尾总计 ~0ms**，
但原版派发「敌方回合结束」（炸弹/冰雹风暴就在这一刻结算）是 **+2.6 秒**之后，
而且三次采样分别是 2625 / 2641 / 2609 ms —— **非常接近一个固定值**，典型的「超时等待」。

所以本轮加了 `Patches/VanillaTurnEndTimingPatch.cs`：给 `CombatManager` 的四个回合结束步骤
挂「完成后计时」（异步方法只能等 Task 完成才知道耗时）：

| 探针 | 说明 |
|---|---|
| `AfterAllPlayersReadyToEndTurn` | 所有玩家点了结束回合之后开始跑 |
| `WaitUntilQueueIsEmpty…` | **等动作队列排空**（可疑点：队列里有个永远「没准备好」的动作 ⇒ 等到超时） |
| `DoTurnEnd` | 回合结束牌等 |
| `AwaitTurnEndAndSwitchSides` | 收尾 + 换边（`Hook.BeforeSideTurnEnd` 就在这附近派发） |

日志：`原版回合结束步骤：<步骤> = Nms` —— 哪个数字大，那 2.6 秒就在哪。

猜测（等日志确认）：`WaitUntilQueueIsEmptyOrWaitingOnNonPlayerDrivenAction` 在等一个
**永远不「准备好」的动作**。我们 Boss 的牌是 `CardCmd.AutoPlay` 打出去的，
如果它留下的 `PlayCardAction` / 我们那套 `BlockingPlayerChoiceContext` 没被正确收尾，
这个等待就会一路等到超时（≈2.5s），表现正好是「最后一个 Boss 出完牌后停 1~2 秒」。

### 下一步

用户复现一次后把 `原版回合结束步骤：…` 那几行发我；若确认是队列等待，就改我们的出牌路径
（让动作在队列里正常结束 / 或在回合收尾时把残留动作清掉）。

## v0.2.197：三人联机「先杀储君 → SL → 不同步」

用户 2026-10-29 提供 `mod_log (11).txt`（含三段会话，第三段是 3 人局）。日志里能直接看到这条链路：

```
[20:10:41] 战斗结束（BeforeRemovedFromRoom）：释放 1 套 Boss 面板
[20:10:50] 第 3 层第 2 场阵容：（3 人局，进阶 10）[SILENT_SHIV,REGENT_COLORLESS,IRONCLAD_VULN]   ← 读档校验把 (幕=3, 场=2) 记了下来
[20:10:52] 拉 Boss 房间: act=0 incoming=ENCOUNTER.DEFECT_DARK_BOSS_ENCOUNTER 已发=1
[20:10:52] act 0 boss #2: 沿用地图上那只 …，不再按阵容重算
[20:10:52] 多人(3 人)：这一幕同时上 3 只 Boss …（第 1 层第 1 场阵容 [DEFECT_DARK,IRONCLAD_ANGER,REGENT_DEBRIS]）
```

也就是：**读档那一刻，`_currentFightAct/_currentFightIndex` 被写成了 (2, 1)（第 3 层第 2 场）**，
而真正在打的是第 1 幕的 Boss 房；而这三个 `*Active()` 变体判据当时正是
「房间遭遇 → **本机 (幕, 场) 去查阵容** → 本局种子」。

问题在于 `_currentFightAct/_currentFightIndex` 是**本地记账**：生成房间、读档校验
（`ValidateRoomsAfterLoad`）、控制台 `fight` 都会改它，而**房主和客人跑这些路径的时机并不一样**
⇒ 两台机器可能用不同的 (幕, 场) 去查阵容 ⇒ 对「这一场储君/战士/猎手用哪套变体」给出不同答案
⇒ 剧本 / 卡组 / 特殊效果不同 ⇒ 多人「数据不同步」（而且 `v0.2.196` 的「本场锁定」会把那个错误答案锁死整场）。

### 修法

`ComputeStarRegent` / `ComputeBoulder` / `ComputeBrewer` **删掉「按本机 (幕, 场) 查阵容」这一步**，
只保留两条**两端必然一致**的判据：

1. **房间遭遇**（`CombatManager.DebugOnlyGetState().Encounter.Id.Entry`）—— 联机同步的数据；
   主 Boss 是本角色时直接给出答案（控制台点名也走这条）；
2. **本局种子**（`RegentStarAt3` / `BoulderAt3` / `SilentBrewerAt3`）—— 与三层阵容
   （`v0.2.195` 的「变体对分场」就是用这三个函数切两半）**同一套 salt**，所以
   「阵容里排的是谁」与「她实际用哪套剧本」天然一致。

配合 `v0.2.196` 的本场锁定（同一条战斗里只算一次），一场里不会中途换剧本、两端也不会算出不同答案。

### 复测看点

- 三人/四人联机：杀完任意 Boss（含先杀储君）→ 存档 → 读档，**不再报不同步**；
- 日志里每场只出现一次 `变体判定（本场锁定）：… = …`；
- 三层阵容仍是 `v0.2.195` 的「三对变体分两场」。

## v0.2.196：一场战斗里的「变体」必须锁定

用户 2026-10-29：「这储君怎么第一回合是辉星储的意图，第二回合就开始打炸弹（无色储君的剧本）了？」

### 根因

三层储君/铁甲/静默都靠 `BossDefinition.ActiveVariant(act)` 判断「这一幕用哪套设计」：

```csharp
var variant = VariantsByAct[...];
return variant.Enabled() ? variant : null;      // 每帧都会被问
```

而 `Enabled` = `BossLineups.StarRegentActive()` / `BoulderActive()` / `BrewerActive()`，
它们的三级判据在**战斗刚开始那一小段时间里会变**：

1. 房间遭遇（`CombatManager.Instance.DebugOnlyGetState()?.Encounter?.Id`）—— 战斗刚建立时可能是 null；
2. 当前阵容（`_currentFightAct/_currentFightIndex` + `For(act, fight)`）—— `NoteCurrentFight` 还没跑到时是上一场的值；
3. 种子 50/50（`RegentStarAt3` 等）。

于是第 1 回合按「种子 ⇒ 辉星储」算出意图，第 2 回合遭遇/阵容就绪后变成「无色储君」
⇒ **剧本、卡组、特殊效果整场中途换掉**（表现就是你说的：先辉星储的意图，然后开始打炸弹）。

### 修法

`BossLineups` 新增 `Latched(key, compute)`：按**当前战斗状态对象的引用**（`DebugOnlyGetState()`）
锁定第一次算出的结果，同一场里所有调用都返回它；引用一变（换场 / 控制台 `fight` / 战斗结束）就清空重算。
`StarRegentActive` / `BoulderActive` / `BrewerActive` 全部改成 `Latched(...)` 包一层。

日志（诊断用，正常一场只会看到一次）：

```
变体判定（本场锁定）：REGENT_STAR = True（换场后才会重算；避免一场里中途换剧本）
变体判定：战斗状态对象变了 ⇒ 锁定清空（换场/新战斗）      ← 同一场里出现这行就说明锁定策略要换写法
```

### 复测看点

- 三层储君：整场的意图/剧本/特殊效果只用一套（辉星储 **或** 无色储君），不会中途变成另一个；
- 铁甲（易伤战/巨石战）、静默（刀贼/炼药贼）同理；
- 日志里每场只应出现一次「变体判定（本场锁定）」。

## v0.2.195：三层阵容改「变体对分场」+ 回合结束耗时探针

### ① 三层阵容（`Core/BossLineups.cs`，3 人及以上生效）

用户 2026-10-29 的新规则（原话）：「四人联机的时候，为了保证每一场上并没有相同角色的 boss，
两场 boss 战中都必须要包含一个猎人、战士和储君，因为只有这三个角色有两个 boss，
并且要保证两场战斗中出现的两个 boss 是不同的。目前我遇到了两个刀贼在同一场战斗里面，
并且有一个巨石战在上一场战斗中出现过了。」

三层候补的 8 个 Key 正好是**三对变体 + 两个单变体**：
猎 刀贼 `SILENT_SHIV` / 炼药贼 `SILENT_BREWER`；战 易伤战 `IRONCLAD_VULN` / 巨石战 `IRONCLAD_BOULDER`；
储 无色储君 `REGENT_COLORLESS` / 辉星储 `REGENT_STAR`；外加 能力机 `DEFECT_AI`、根除骨 `NECROBINDER_ERADICATE`。

新实现 `BuildAct3Pairs(need, fight, mainTaken)`：
- 每场固定给「猎 + 战 + 储」各一只；
- **两场互补**：哪一半落第一场由既有的确定性 50/50 决定（`SilentBrewerAt3` / `BoulderAt3` /
  `RegentStarAt3`，只依赖「本局种子 + salt」⇒ 联机两端一致），第二场自动拿另一半
  ⇒ 刀贼/炼药贼、易伤战/巨石战、无色储君/辉星储 **绝不与你同场、也不跨场重复**；
- **4 人局第 4 只**：能力机 / 根除骨各分一场（salt 57 决定谁在第一场）；
- 顺序按三层行动次序 猎→机→骨→储→战（易伤战/巨石战最右）；
- 主 Boss `[0]`：优先「本局还没当过主 Boss 的角色」，其次「不与本幕第一场主 Boss 同角色」；
- 日志：`第 3 层第 N 场阵容（三层分场规则：三对变体分两场 + 第 4 位）：[...]（N 只）`。

2 人局（每场 2 只）仍走原来的随机 + 跨场不重复逻辑，不受影响。

### ② 炸弹 / 冰雹风暴的结算延迟（用户：「最后一个 boss 出完牌后又隔了快 2 秒」）

这两样都在**敌方阵营回合结束**（原版 `Hook.BeforeSideTurnEnd`）结算，所以先量化再改：

1. 修掉一个**死诊断**：`PlayTurn` 里的 `lastMs` 从来没被赋值 ⇒ 老日志
   「出牌阶段结束: 共 Xms（最后一张牌后 Xms）」两数永远相等、其实没信息。现在每打完一张牌就记一次；
2. 新增 `回合收尾耗时: 球 Xms / 遗物 Yms / 诅咒 Zms / 弃手牌 Wms（我们这边总计 T ms）`；
3. 新增 `Patches/SideTurnEndTimingPatch.cs`：原版派发 `Hook.BeforeSideTurnEnd` 时打
   `敌方回合结束派发（BeforeSideTurnEnd）：距我们上次回合收尾 +Nms`，并在 PlayTurn 末尾
   `MarkOwnTurnEnded()` 记时间戳。

判断：**+N 很小**（几十 ms）⇒ 那 2 秒在我们这边（看收尾各行）；**+N ≈ 2 秒** ⇒ 延迟在原版派发之前。

## v0.2.193 + v0.2.194：巨石战定稿 + 炸弹巡检

### ① 巨石战（`Core/BoulderIroncladScript.cs`）

用户 2026-10-29 两轮订正：「把彼岸咆哮换成余烬…」→ 随后「把巨石战的那张余烬换成打击，
第三回合改为打击、防御、坚毅，第八回合还是没费出不了」（第八回合他选了「就让第三张打出来」）。

| 回合 | 现在 |
|---|---|
| 3 | **打击、防御、坚毅**（原「巨石+、防御、防御」） |
| 4 | **打击、坚毅+**（点名被消耗的是**防御**）—— 彼岸咆哮 → 余烬 → 最终**打击** |
| 8 | 巨石+、打击、**打击** |

- 卡组：`Cinder`（余烬）已移除；彼岸咆哮更早一版已删；`FreePlayExhaustEntry` 那套也不需要了。
- ⚠ 第 8 回合费用账变了：巨石+ 1 + 打击 1 + 打击 1 = **3 = 每回合 3 费** ⇒ **三张都会打出来**，
  不再「第三张没费」。用户已确认按字面执行。

### ② 炸弹（储君）：「第二回合只打了 1 个，第四回合怎么炸了两个」

反编译确认的规则：
- `TheBomb`（2 费）施加 `TheBombPower`：层数 = `Turns`（3）、威力 = `BombDamage`（40；**+ 版 50**），
  每次施加都用 `SetDamage` **覆盖**威力；
- **`TheBombPower.InstanceType = Instanced`**（多份可共存，见 `LockOnPower` 注释）⇒ 每颗炸弹是**独立实例**；
- `BeforeSideTurnEnd` 只在**她自己阵营回合结束**（`participants.Contains(Owner)`）触发：
  层数 > 1 ⇒ `PowerCmd.Decrement`；层数 ≤ 1 ⇒ 闪一下 → 等 0.2~0.4s →
  对 `HittableEnemies` **逐个**放 `NFireSmokePuffVfx` → 造伤害 → 移除。
- ⇒「第 N 回合打出的炸弹」在她**第 N+2 回合结束时**爆。

按剧本（第 2/4/7 回合各一颗）：T2 那颗 T4 末爆（40）；T4 那颗 T6 末爆（50）；
**第 4 回合本应只有一次爆炸**。用户看到两次，可能是：① 爆炸烟雾按目标**各放一次**（多人在场就像两团）；
② 某实例被重复施加（＝重复出牌）；③ 4 个客户端共用一个 `mod_log.txt`，日志交错。

**新增诊断** `Patches/BombDiagnosticsPatch.cs`：在她阵营每次回合结束、原版结算那一刻，
前缀清点 `Creature.Powers.OfType<TheBombPower>()` 的**实例数 / 各层数 / 各威力**：

```
炸弹巡检：她身上 N 个炸弹实例，层数 [..]，威力 [..] ⇒ 本次结算**会爆炸**（层数 ≤ 1 的那些）
```

只在「有要爆的」或「实例数 ≥ 2」时刷，重复状态每会话只打一次 ⇒ 下次一眼就能分清是
「真有两个实例都到 1 层」还是「只有一个实例、屏幕两团烟」。

## v0.2.192：巨石战把「彼岸咆哮」换成「余烬」

用户 2026-10-29：「把彼岸咆哮换成余烬，第 4 回合改成余烬（烧防御）、坚毅。
第八回合中没费不打的彼岸咆哮换成没费不打的余烬」。

改动（`Core/BoulderIroncladScript.cs`）：

- **卡组**：`HowlFromBeyond`（彼岸咆哮）→ **`Cinder`（余烬，2 费攻击 18 + 随机消耗 1 张牌）**；
- **第 4 回合**：`防御、坚毅+（烧彼岸咆哮）` → **`余烬、坚毅+`**，剧本点名被烧的那张改成**防御**；
- **第 8 回合**：`巨石+、打击、彼岸咆哮（没费）` → `巨石+、打击、余烬（没费）`
  （余烬 2 费，巨石 1 + 打击 1 = 2 ⇒ 第 3 张打不出，和原来一样只是摆设）；
- 第 4 回合不再需要 `FreePlayExhaustEntry = "HOWL_FROM_BEYOND"`（那套「回合结束从消耗堆自动打出」
  只服务彼岸咆哮），字段已删；`BossScript.FreePlayExhaustEntry` 机制本身保留。

⚠ **余烬的消耗是真随机**：反编译 `Cinder.OnPlay` 用的是
`Owner.RunState.Rng.CombatCardSelection.NextItem(pile.Cards)` ⇒ 不走 `CardSelectCmd`
⇒ **不经过我们的自动选择器**，剧本点名对它无效（抽到哪张烧哪张）。
剧本那张 `Burn<DefendIronclad>()`（防御）现在由 **`坚毅+` 的「消耗 1 张」**兜住
（`TrueGrit` 走选择器，会优先吃点名名单）。若要做到「余烬一定烧防御」，还需要额外补丁去改它的随机挑选。

### 复测看点

- 第 4 回合意图：**攻击 18（余烬）** ＋ 格挡 9（坚毅+）；日志会显示余烬随机消耗了哪张；
- 第 8 回合意图：攻击 28＋6，**余烬不出**（费不够）；
- 循环 6~8 里不再出现「全体 18」。

## v0.2.191：文案（眼部攻击中文改「它」+ 涂毒贼两条警告）

用户 2026-10-29：「眼部攻击中文也改成它。为涂毒贼在描述中添加 2 条警告…」

- `GO_FOR_THE_EYES_AIM_POWER`（zhs）：两处「她」→「**它**」（故障机器人无性别）；
- `POISON_STRIKE_POWER`（涂毒贼「带毒刺击」，zhs + eng）追加两条警告：
  - 「警告：中毒属于持续性伤害，带有「为你而死」的召唤物不会替你承受该伤害。」
  - 「警告：触媒和毒性爆发的特别效果在本场战斗中对全场生效。」
  - 英文：`WARNING: Poison is damage over time - a summon with "Die for You" will not absorb it for you.` /
    `WARNING: The special effects of Accelerant and Outbreak apply to the whole combat in this fight.`
- PCK 已重建并抽出验证。

## v0.2.190：特殊效果文案与实现对齐（单体 = you）

用户 2026-10-29 口径（重要，以后写文案都按这条）：
**描述里的「你 / you」= 被这只 Boss 跟踪锁定的那位玩家**（单体/对应型），
不是「只要有玩家做某事」。所以凡是由 `BossLockOn.SpecialEffectAppliesTo` 把关的效果，
英文一律不能写 “a player / all players”。

### 文案改动（zhs + eng，PCK 已重建并抽出验证）

| Key | 改动 |
|---|---|
| `SLEEVE_TRICK_POWER` | “the first time **a player** plays a card costing 2 or more” → “the first time **you** play a card costing 2 or more” |
| `DEVIATION_POWER` | “Whenever **a player** plays a Power card” → “Whenever **you** play a Power card” |
| `RESONANCE_POWER` | eng “Whenever **a player** generates a card” → “Whenever **you** generate a card”；zhs 「每当玩家在战斗内生成卡牌」→「每当**你**在战斗内生成卡牌」 |
| `GO_FOR_THE_EYES_AIM_POWER` | “(or an AoE that hits **her**)” → “(or an AoE that hits **it**)” —— 故障机器人无性别 |
| `BOSS_PRIMAL_FORCE_POWER` | 描述滞后修正：**2 → 3 点覆甲**；「未被格挡伤害 → 你获得 1 层虚弱」**早已改成「你抽 1 张牌」**（`BossPrimalForcePower.AfterDamageGiven` → `CardCmd.DiscardAndDraw(弃0抽1)`） |

### 彗星（`CometBossPower`）：补上单体判据

用户：「Comet 属于单体特殊效果」。碎屑改判**早就**是按锁定玩家走的
（`CometDebrisRedirect` 里 `BossLockOn.TargetFor`），但 `AfterCardPlayed` 那条触发漏了判据 ——
**谁打碎屑都会触发**，多人时等于全局效果。现在加上：

```csharp
if (!Core.BossLockOn.SpecialEffectAppliesTo(Owner.Monster as Monsters.CharBossMonster, actor)) return;
```

单人恒成立（行为不变）；多人时只有被这只储君锁定的人打碎屑才算。
（格挡/清负面/敌方掉血那三段仍是「所有玩家」——那是设计上给全队的好处，文案照实写。）

### 复测看点

- 多人：没被储君锁定的人打碎屑 **不触发**彗星；被锁的人打才触发；
- 描述与实现一致：单体效果英文全是 “you”；
- 原始力量：巨石给 **3 覆甲**、未格挡伤害让你**抽 1 张牌**。

## v0.2.189：偏差认知不再豁免「祭仪 / 火热研究」+ 删掉两处括号补注

用户 2026-10-29（两张截图）：
「把这两个描述中的括号和括号内的内容删了。另外，移除『偏差认知』对觉醒者的特判，
即不再将『祭仪』和『火热研究』作为例外，这部分的描述改为：『你每打出 1 张能力牌，故障机器人获得 1 点集中。』」

### ① 偏差认知（`Powers/DeviationPower.cs`）

删掉 `IsDownfallStrengthToken(card)` 这个判据（连同方法本身与调用点）——
崩坠觉醒者的两张衍生能力牌「祭仪 `Ceremony`」「火热研究 `BurningStudy`」
以前被当作「加力量的产物」豁免、不给集中，现在**一视同仁**：任何 `CardType.Power` 都给 1 集中。

（历史：v 早先之所以豁免，是怕白送 Boss 集中；用户评估后决定取消，行为以本条为准。）

### ② 两处描述（`content/localization/{zhs,eng}/powers.json`，PCK 已重建）

- 偏差认知：「你每打出 1 张能力牌，故障机器人获得 1 点集中；它的力量与集中数值始终相等。」
  （删掉「非『祭仪』或『火热研究』」这个例外，以及括号补注「（一边变化，另一边同步变化）」）
- 何人僭越：删掉括号补注「（图标上的数字是本回合还剩多少点掉血预算：75 → 0）」；
  图标上的 75 → 0 计数**保留**（v0.2.188 做的那个 `DisplayAmount` 不动）。

### 复测看点

- 玩家打出任意能力牌（含祭仪 / 火热研究）→ 故障机器人 +1 集中（日志：
  `偏差认知：玩家打出能力牌 <牌> → Boss +1 集中`）；
- 两条描述里都不再有括号补注；何人僭越的图标数字仍然从 75 往下掉。

## v0.2.188：何人僭越阈值恒 25 + 计数 75；顺带修掉「描述里显示 1」

用户 2026-10-29（截图：描述写着「失去 **1** 点生命」）：
「让他减铸造所需失去血量**不随人数变化，恒为 25**，每回合 3 次，计数 **75**。另外如图，目前描述中写的数字不对」。

### ① 为什么描述会变成「1」

v0.2.187 我把「阈值」塞进了 `Amount`（想让 `{Amount}` 显示 100）。但原版
`PowerModel.Description` 里的 `{Amount}` 读的就是**层数**，而层数会被战斗/施加流程写回 1
（`StackType.Counter` 的层数语义），于是文案变成了「失去 1 点生命」。
**教训：`{Amount}` 不能拿来当自定义数值用**，要显示自己的数字就加**自定义占位符**。

### ② 现在的做法

- `KnowThyPlacePower.ThresholdFor()` → **恒 25**（不再按人数放大）；
- 图标计数 `DisplayAmount` = `(3 − 已触发) × 25 − 本回合已累计掉血` ⇒ 每回合 75 → 0，
  三次用完为 0，她回合开始回到 75（与用户「计数 75」一致）；掉血/触发后立刻重画；
- **不再动 `Amount`**，描述里的 25 直接写进本地化文案（阈值已固定），
  另加一句解释图标数字：「…每回合至多触发 3 次（图标上的数字是本回合还剩多少点掉血预算：75 → 0）」。
- `IroncladRagePower`：描述改用自定义占位符 `{Loss}`（`Description` 覆写里 `Add("Loss", …)`），
  图标数字改用 `DisplayAmount => 力量损失(3/2/1)`；**不再写 `Amount`**，实际扣力量仍按人数算。

### 复测看点

- 何人僭越：描述「失去 25 点生命」；图标 **75**，每掉 25 血 −25 并扣 5 铸造 + 全队 −1 力量，
  三次后 0、再掉血不扣铸造，她回合开始回 75；
- 群情激奋：4 人局描述「失去 1 点力量」、图标 1；单人局 3；数字不再变成 1/层数。

## v0.2.187：群情激奋 / 何人僭越的描述改成随人数（已被 v0.2.188 修正为阈值恒 25）

用户 2026-10-29（两张截图）：
「4 人时群情激奋的描述错误，应为本回合失去 1 点力量（不过效果是对的）；
何人僭越的描述有误，应为 100 点生命。（这两个都没有随人数改变描述）」
另外给了两个做法：
「群情激奋在 1/2/3/4 人时分别是 3/2/1/1 层，就减对应层数的力量；
何人僭越做成硬化外壳那样，计数显示成 人数×25×3（= 每回合能减铸造的血量），计数归 0 后本回合不会再减铸造，效果不变」。

### ① 群情激奋（`Powers/IroncladRagePower.cs`）

- `StackType` 从 `Single` 改成 **`Counter`**，`Amount` = 「本回合每次触发会失去多少力量」
  = 1 人 3 / 2 人 2 / 3～4 人 1（沿用原来的 `StrengthLossPerAnger`）；
  在**回合开始**与**每次有「愤怒」被打出**时刷新（`SetAmount(n, silent: true)`；`Amount ≥ 1`，
  因为原版 `ShouldRemoveDueToAmount` 只看 Amount，归 0 会把整个 power 摘掉）；
- 描述改成读 `{Amount}`（原版 `PowerModel.Description` 自带这个占位符）；
- 实际扣力量也改成读 `Amount`，**效果与以前逐值一致**（只是描述/图标数字现在对得上）。

### ② 何人僭越（`Powers/KnowThyPlacePower.cs`）

- 阈值本来就是 `25 × 真人玩家数`（4 人 = **100**），只是描述写死了 25 ⇒ 描述改成 `{Amount}`，
  并把 `Amount` 在回合开始时刷成当前阈值（≥1，同上）；
- 新增 `DisplayAmount` 覆写 = **「本回合还能吃下多少掉血才会再扣一次铸造」**
  = `(3 − 已触发次数) × 阈值 − 本回合已累计掉血`：
  - 4 人局一进场就是 `3 × 100 = 300`（正是用户说的「人数×25×3」）；
  - 每掉 100 血 → 扣一次铸造、计数 −100；三次用完 → **0**（这一回合不再扣铸造）；
  - 回合开始清零 → 回到 300；
  - 掉血 / 触发后都会 `InvokeDisplayAmountChanged()` 让图标立刻重画；
- `StackType` 改成 `Counter`（原版只在 Counter 时画数字），`ShouldScaleInMultiplayer = false`
  （层数自己按人数算，别让原版再乘一次）。

### ③ 本地化

`content/localization/{zhs,eng}/powers.json` 里这两条都换成 `{Amount}` 占位符：
- 群情激奋：「…铁甲战士本回合失去 {Amount} 点力量。」
- 何人僭越：「每当储君在一回合内失去 {Amount} 点生命，…」
**PCK 已重建并验证**（`extract_pck_file.gd` 抽出来确认过 `{Amount}` 在包里）。

### 复测看点

- 4 人局：群情激奋图标显示 **1**、描述「失去 1 点力量」；她中一次「愤怒」确实只掉 1 力量；
- 4 人局：何人僭越图标显示 **300**，她每被打掉 100 血图标 −100、铸造 −5、全队 −1 力量；
  三次之后图标 **0**、再掉血也不扣铸造；她回合开始回到 300；
- 单人局两条都还是原来的数字（群情激奋 3 / 何人僭越 25 与 75）。

## v0.2.186：「指向性攻击牌停在空中」的真凶 —— 我自己写的鬼祟前缀

用户 2026-10-29（截图：中和被举在半空）：「现在奇巧贼和我打出的指向性攻击牌都停在空中了」。

### 根因：`Task` 返回值的方法，前缀 `return false` 却没给 `__result`

v0.2.183 我加的 `SneakyOnlyBossAttacksPatch` 长这样：

```csharp
[HarmonyPrefix]
public static bool Prefix(SneakyPower __instance, CardPlay cardPlay)
{
    …
    if (ReferenceEquals(cardOwner.Creature, powerOwner)) return false;   // ← 没有 __result
    if (IsRealPlayer(combat, cardOwner)) return false;                   // ← 没有 __result
    return true;
}
```

而原版方法是 **`public Task AfterCardPlayed(PlayerChoiceContext, CardPlay)`**。
Harmony 跳过原方法时，`__result` 保持默认值 = **`null`**（`Task` 是引用类型）
⇒ 原版 `Hook.AfterCardPlayed` 对每个 power 的结果 `await` 一个 **null Task**
⇒ `NullReferenceException` 在出牌流程中间炸掉 ⇒ **这张牌永远结算不完，卡面就停在半空**
（而且不弹任何红字，因为异常在异步链里被吞了）。

日志正好对上了：18:07:38 / 18:07:42 两行

```
鬼祟：真人玩家 飞猪 打出攻击牌 STRIKE_SILENT → **不触发**
鬼祟：真人玩家 飞猪 打出攻击牌 NEUTRALIZE   → **不触发**
```

—— 那正是截图里那张「中和」被举在半空的前一刻：**我的前缀刚把 Task 变成 null**。

奇巧贼自己的攻击牌走的是分支 ①（自己打牌不给自己格挡），同一个坑，
这就是上一轮「她打出打击就停在空中」的真正成因（v0.2.185 那个出牌队列清理只是
顺带把队列里那份卡面藏了，并没有修到出牌流程本身）。

### 修法

两个 `return false` 之前都补上 `__result = Task.CompletedTask;`，
并把 `ref Task __result` 加进前缀签名。顺手把**全部** 60 多个 Harmony 前缀扫了一遍
（`.build/audit_prefixes.py`：找「前缀里 `return false` 但方法体没出现 `__result`」），
现在只剩两个：

- `BossMultiplayerHpPatch` → 目标 `Creature.ScaleMonsterHpForMultiplayer(...)` 返回 **`void`** ✔；
- `SafeLogPatch` → 目标 `Logger.Log(...)` 返回 **`void`** ✔。

两个都不需要 `__result`，既有的坑只有鬼祟这一处。

### 复测看点

- 奇巧贼打攻击牌、**你自己**打指向性攻击牌（中和 / 打击…）都能正常结算，不再有卡停在空中；
- 「真人玩家打攻击牌不触发鬼祟」这条仍然生效（日志里那两行还会出现，但这次只是 `return false + Task.CompletedTask`）；
- 其它 Boss 打攻击牌时她的格挡照常长。

## v0.2.185：「奇巧贼打出就停在空中」的疑似容器 —— 原版出牌队列

用户 2026-10-29 补充确认：**是她打出去的那张卡停在半空**，而且「一直在那，直到我返回主菜单」。
⇒ 不是悬浮预览残留（那是 v0.2.184 加固的那条），而是**卡面节点被放进了一个没人清理的容器**。

反编译 `NCardPlayQueue.OnActionEnqueued(GameAction)`（原版每收到一个 `PlayCardAction` 都会跑）：

```csharp
if (LocalContext.IsMe(playCardAction.Player)) { …拿玩家手牌里的卡面… }
else
{
    var creatureNode = NCombatRoom.Instance.GetCreatureNode(playCardAction.Player.Creature);
    var handler = creatureNode.PlayerIntentHandler;
    var nCard = NCard.Create(cardModel);
    nCard.GlobalPosition = handler.CardIntent.GlobalPosition + …;   // ← 卡面被摆在屏幕中上方
    nCard.Scale = Vector2.One * 0.25f;
    AddChildSafely(nCard); _playQueue.Add(item); TweenCardToQueuePosition(item, …);
}
```

这条分支本来是「**远程真人玩家**正在出牌」的展示，定位用玩家意图位、清理走玩家出牌的动画流程。
我们的 Boss 是**影子 Player**（不在 `CombatState.Players`、也没有玩家手牌 UI），出牌是
`CardCmd.AutoPlay` ⇒ 卡面建出来、补间到队列位置之后，**没有任何一步会把它收走**
⇒ 一张大卡永久吊在屏幕中上方，直到战斗界面被销毁（回主菜单）才没了。和用户描述完全对上。

修法：新增 `Patches/StrayBossCardQueuePatch.cs` —— 只做**最小、可自愈**的清理：

- 扫 `NCardPlayQueue` 的**直接子节点**，凡是「卡面所属生物是 `CharBossMonster`」的，
  `Visible = false` + 全透明；
- **只隐藏、不 QueueFree**：队列内部还记着这个 item，执行阶段要按 item 找节点，
  提前释放会撞上它的下标查找（`InvalidOperationException`）；
- 只认我们自己的 Boss ⇒ 真人玩家的牌面一个像素都不动；
- 触发点两处：**每次 Boss 打完一张牌立刻扫一遍**（`CharBossMonster.PlayOne` 里），
  以及 `BossFrameTick` 的 4Hz 慢车道兜底。
- 日志（每张牌只报一次）：
  `出牌队列：<牌> 是 Boss 打出的牌 → 队列里的那份卡面已隐藏`。

### 复测看点

- Boss 打牌时**不再有卡停在空中**；日志里应出现上面那行「出牌队列：… 已隐藏」；
- 真人玩家自己出牌的大卡展示、飞进弃牌堆等动画**一切照旧**（我们只碰 Boss 的牌）。

## v0.2.184：奇巧贼第 2 回合的次序 + 「一张大卡吊在半空」

用户 2026-10-29（截图 + 两句话）：「奇巧贼的打击停在了空中」＋「把她出（或者说丢弃）鬼祟和翻越撑击的顺序换一下」。

### ① 次序：翻越撑击 排到 鬼祟（单人版是「余像」）前面 —— 已改

`Core/SilentSlyScript.cs` 第 2 回合的行序改成：

```
早有准备+ → 翻越撑击（被丢 → 奇巧自动打出） → 鬼祟/余像 → 打击 → 防御（单人时才被丢）
```

- 多人局这两张都是被「早有准备」丢掉的 ⇒ 丢弃/结算顺序跟着剧本行序走，现在先翻越撑击、再鬼祟；
- 单人局这一格是真打出去的「余像」（1 费），顺带挪到翻越撑击之后；总费用不变：
  早有准备 0 + 余像 1 + 打击 1 = 2（多人：打击 1 + 防御 1 = 2）。

### ② 「打击停在空中」—— 先加固了已知那类残留（真凶待用户确认）

「一张大卡吊在屏幕中央不走」在我们这儿**有过唯一一个已知成因**（见
`UI/BossHandCard.Hover.cs` 顶部注释）：`NCard` 自带悬浮大卡预览，**悬停状态下把节点拆掉**
收不到 `MouseExited` ⇒ 预览面板残留成一张大卡。以前 `ReleaseCell` 只对**直接子节点**发退出事件，
本轮改成**递归**遍历整棵子树：每个 `NCard` 先 `MouseFilter = Ignore`（之后进出事件一律不触发）
再发 `MouseExited`。

⚠ 但截图里那张卡是谁的、怎么触发的还需要用户确认（两种成因要两种修法）：
那个时间点（17:56:52）的日志正好有一行 `命运同担：玩家被迫弃掉 STRIKE_SILENT`
——如果那张大卡是**被强制弃掉的那张玩家打击**，那就是「强制弃牌的飞行动画卡住」，
得去改 `Powers/SharedFateDiscardPower` 的弃牌方式（而不是卡面预览的清理）。

## v0.2.183 之前的编码事故说明（保留备查）

> ⚠ **2026-10-29 15:21 本文件被一次编码事故损坏，已修复**：我（AI）用 PowerShell 的
> `Get-Content -Raw` + `Set-Content -Encoding UTF8` 改这个文件，而这台机器的 PS 5.1 按 ANSI(GBK)
> 解码 UTF-8 ⇒ 整份变乱码。随后做了反向还原（GBK 重编码 → UTF-8 解码，救回约 89%）+ 逐行修复：
> 现为 1219 行、121 个小标题、0 个缺口字符。个别位置（约 2400 处单字/标点）是**按上下文补的**，
> 技术结论、日志字符串、类名、版本号均未改动；原始乱码件留在 `.build/status_salvage.txt` 备查。
> 权威的 bug 记录一直是 `.dshwolf/buglog.json`（本次未受影响）。**以后不要再用 PowerShell 改中文文件。**


## v0.2.183：愤怒战打「愤怒」停顿太久 + 玩家打攻击牌仍然触发鬼祟

用户 2026-10-29（4 开实测）：「愤怒战打出愤怒的时候停顿的时间太长了」＋「玩家打出攻击牌还是会触发奇巧贼的鬼祟」。

### ① 愤怒：4 人局一次「愤怒」停 4 秒多

日志实证（v0.2.182，一秒一条，正好 4 条）：

```
[17:50:42] 群情激奋：铁甲战士打出愤怒 → 给 飞猪 的弃牌堆塞一张愤怒+（弃牌堆 13 张，其中愤怒 6 张）
[17:50:43] 群情激奋：铁甲战士打出愤怒 → 给 [unknown] 的弃牌堆塞一张愤怒+（…）
[17:50:44] 群情激奋：铁甲战士打出愤怒 → 给 [unknown] 的弃牌堆塞一张愤怒+（…）
[17:50:45] 群情激奋：铁甲战士打出愤怒 → 给 [unknown] 的弃牌堆塞一张愤怒+（…）
```

`Powers/IroncladRagePower.GiveAngerCopiesToPlayers` 以前是**每个真人玩家各来一遍**
`CardCmd.Preview(copy, 0.75f)` + `await Cmd.Wait(1f)` ⇒ 停顿按人数线性累加。
修法：**先把所有复制品全部塞进弃牌堆，最后只展示一次、只等一次**（0.8 秒），
和原版碎屑储君「一次展示一张」的观感一致。

顺带修了同一段里的红字（日志 17:51:46 连着三条
`群情激奋：塞愤怒复制品失败: Index was out of range`）：
反编译 `CardPileCmd.AddGeneratedCardToCombat` = `(await AddGeneratedCardsToCombat(...))[0]`，
而它在**战斗已结算**（`!CombatManager.Instance.IsInProgress`）时返回**空数组** ⇒ 下标 `[0]` 越界。
现在先判 `CombatIsLive`，并把 `IndexOutOfRangeException` 当「战斗结束、剩下的人不用塞了」安静跳过
（只在有跳过时打一行 `愤怒复制品成功 N 份、跳过 M 份`）。

### ② 鬼祟：真人玩家打攻击牌不该触发

用户前一版要求「排除奇巧贼自己，只判定其它 boss」，这一版进一步明确「玩家那条路也要掐掉」：
最终语义 = **只有「另一只 Boss 打出攻击牌」才给奇巧贼格挡**。

原版 `SneakyPower.AfterCardPlayed` 的条件是「**任何**别的生物打攻击牌 ⇒ 我格挡」，
真人玩家打出攻击牌正好命中。新增 `Patches/SneakyOnlyBossAttacksPatch.cs`：
给 `SneakyPower.AfterCardPlayed(PlayerChoiceContext, CardPlay)` 挂前缀，只在
`CardType.Attack` 上动手 —— 判据是「这张牌的 `Owner` 是否在 `CombatState.Players` 里」：

- 在 ⇒ **真人玩家** ⇒ `return false`（不触发），并打一行
  `鬼祟：真人玩家 <名字> 打出攻击牌 <牌> → **不触发**（只有其它 Boss 的攻击牌才算）`；
- 不在（我们的 Boss 用的**影子 Player** 就不在）⇒ `return true` 交回原版（实际扇出不到，
  仍由 `SneakyOnBossAttack` 按「先拍快照、原版没给才补」的方式补）。

非攻击牌、判据取不到（异常/空）时一律 `return true` —— 宁可多给一次也不凭空吞掉原版逻辑。

### 复测看点

- 4 人局愤怒战打「愤怒」：停顿**不再是 4 秒**，一次只有一下展示（≈0.8 秒），
  日志里 4 条「塞一张愤怒」应当是**同一秒**内的连号；
- 真人玩家打出攻击牌时，奇巧贼**不长格挡**；其它 Boss 打出攻击牌时她**照样**长格挡；
- 若日志出现 `塞愤怒复制品失败` 且不是 `IndexOutOfRangeException`，把整行发我。

## v0.2.182：地图 Boss 徽章和名字对不上（静默猎手，画的却是「战士的头像」）

用户 2026-10-29：「地图 boss 节点提示写静默猎手，但图标是铁甲战士的头像——这不对吧，上面是战士的头像」。
根因（日志 + 反编译实证，不是猜的）：

- 徽章贴图来自 `NBossMapPoint._Ready`：`enc.BossNodePath + ".png"` / `+ "_outline.png"`。
- 我们的遭遇只有**少数档位**有专属图（`silent_shiv` / `silent_poison` / `soul_bone` / `defect_*`），
  其余全部走 `CharBossEncounterBase.BossNodePath` 的兜底 = **TheKinBoss 的占位剪影**
  （那张画的是三只披甲武士 ⇒ 用户眼里的「战士的头像」）。
- 本局日志正好点名两只：
  ```
  结算/顶栏图标缺图 → 改用原版占位图标（…silent_brewer_boss_encounter.png → …the_kin_boss.png）
  结算/顶栏图标缺图 → 改用原版占位图标（…ironclad_boulder_boss_encounter.png → …the_kin_boss.png）
  ```
  ⇒ 第三幕这两颗 Boss 徽章长得**一模一样**，而且都和提示文字对不上。
修法（三层，互不冲突）：

1. `SilentBrewerBossEncounter`（炼药贼）→ `silent_poison`：同角色、同「药剂」轴，
   也和第 3 幕另一套「刀贼」的 `silent_shiv` 区分得开（当初改成占位图就是为了区分刀贼，现在换成更对的那张）。
2. `NecrobinderDoomBossEncounter` / `NecrobinderEradicateBossEncounter` → `soul_bone`：
   亡灵契约师全档位统一用那张骷髅徽章。
3. 新增 `src/Hooks/Patches/BossMapPointCharacterIconPatch.cs`：**没有专属美术**的档位
   （目前就是铁甲战士 / 储君的所有档位），徽章改用**角色自己的徽记**
   （`CharacterModel.IconTexture`，就是顶栏那枚 88×88 角色徽记）的**白色剪影** + 白色圆盘衬底。
   白色是必须的：原版会用 `ActModel.MapTraveledColor` 给主图标染色、`MapBgColor` 给衬底染色，
   白剪影染出来才是原版那种「深色剪影 + 地图底色圆盘」；彩色图乘上去会糊成一团黑。
   有专属图的档位**一个像素都不动**，别人的遭遇（关掉替换 / 原版 Boss）同样不动。
   日志：`地图 Boss 徽章：<遭遇 id> 没有专属美术 → 改用角色徽记 <角色>（白色剪影，随地图染色）`。
保命措施：整块 postfix 包在 try 里，画不出来就保持原版占位图 —— 地图屏崩了整局都进不去（前车之鉴见 `EncounterIconPathPatch` 里那处 `NTopBarBossIcon` 把 `NRun._Ready` 打断）。

### 职责边界（用户 2026-10-29 当面确认，别再搞混）

- **顶栏那颗 Boss 预览**：用**各角色的头像 + 角色名**（「铁甲战士」这样），这是设计如此，
  由 `TopBarBossIconFallbackPatch` 负责（`CharacterModel.IconTexture`），本轮**没改一行**。
- **地图上的 Boss 节点徽章**：是**另一套美术**（`BossNodePath`），和顶栏头像无关；
  有崩坠专属图的用崩坠图，没有的用角色徽记剪影（本条补丁）。
- 用户 2026-10-29 拍板：**保留本方案**（铁甲 / 储君先用角色徽记剪影看效果），不当成 bug 再改回去。
### 复测看点

- 第三幕地图的两颗 Boss 节点：提示「静默猎手」那颗应该是**毒 / 药剂**徽章，提示「铁甲战士」那颗是**铁甲头盔**剪影。
- 第一幕亡灵契约师（灾厄骨）不再是披甲武士。
- 全图不再出现「名字是 A、脸是 B」的错配。
- 若日志刷 `角色徽记 xxx 生成失败` / `地图 Boss 徽章改用角色徽记失败`，把整行发我。
- 想给铁甲 / 储君换成真正的崩坠风大图：把 512px 的 `X.png` + `X_outline.png` 放进
  `tools\art\import_proj\CharacterBosses\images\map\`、登记到 `tools\art\pck_extras.txt`，
  重建 PCK，然后在对应遭遇类里覆写 `BossNodePath`（本补丁会自动让路）。
## v0.2.181：刀贼没拿到钻石头冠的 20 点格挡

用户 2026-10-29：「钻石头冠没有在战斗开始时给予刀贼的 20 点格挡」。
日志（v0.2.180，14:47~15:00）已经把答案写出来了：
```
[CharBoss] 钻石头冠：战斗开始 +20 格挡、+2 层残影（…；，现在格挡 0
[CharBoss] 钻石头冠：跳过她回合开始的重复触发（20 格挡 + 1 模糊已在战斗开始时给过）
```

`GainBlock` 确实调了，但**读回来是 0** —— 那一刻她还没真正进入战斗（战斗开始的流程随后会把格挡清零），
于是一进战斗这 20 点就没了；残影倒是留下了（所以「跳过重复触发」那行还会出现，看起来像给过）。
修法（不动原来的时机，加一层兜底）：
1. 把「她身上钻石头冠给多少格挡」抽成 `DiademBlockOf(shadow)`。
2. 新增 `BossHookBridge.EnsureDiamondDiademBlock(boss)`：在她第 1 回合之前（`TurnNumber <= 1`），
   只要**她身上还没有格挡**、又确实带着这件遗物，就等 120ms（让「战斗开始清零」先跑完）补一次；
   每个 Boss 只补一次（`DiademRetrying` 防重入），补完打日志：
   `钻石头冠：战斗开始那笔格挡被清掉 ⇒ 补回 20 点（现在 20，第 1 回合）`
3. 由每帧驱动对**每只** Boss 调用（`BossFrameTick` 的每帧车道，判断都很廉价：
   有格挡 / 没这件遗物 / 已过了 1 回合时直接返回）。
### 复测看点

- 刀贼开局身上就应该有 **20 格挡**（且撑过玩家回合，因为残影还在）。
- 日志应出现 `钻石头冠：战斗开始那笔格挡被清掉 ⇒ 补回 20 点（现在 20，第 1 回合）`。
- 其它带钻石头冠的 Boss（一层刀贼同款设计）同样生效。
## v0.2.180：图层方案彻底回退（三种做法全部失败）

用户 2026-10-29（接连两张截图）：地图界面、暂停菜单上仍然浮着整排意图箭头 →「？」。
三种做法全部失败，已全部回退。
| 版本 | 做法 | 结果 |
|---|---|---|
| v0.2.177 | 面板 `ZIndex=200` | 意图/卡面浮在地图之上 |
| v0.2.178 | 面板 `ZIndex=10` + `OnFrame` 非战斗隐藏 | 仍然浮在地图/暂停菜单上 |
| v0.2.179 | 面板不设 ZIndex，意图行改挂在**面板**下、`ZIndex=2` | 仍然浮在暂停菜单之上（截图 2 张） |

⇒ **结论：这个项目的 UI 一律不能抬 `ZIndex`**。其实 `BossStatusRow.Icons.cs` 里早就写过同一条：
「图层：和能量球同层（不抬 ZIndex，否则切地图/设置时本 mod 的节点会浮在游戏界面之上）」—— 这次没有先看那条注释就走了三遍弯路，已记进 `.dshwolf/cerebrum.md` 的 Do-Not-Repeat。

回退内容：
- `BossHandCard.BuildIntentRow` 恢复 `cell.AddChild(row)`、去掉 `ZIndex`。
- 删掉 `Rows`（格子→意图行）登记表与 `DropIntentRow/IntentRowOf/ClearIntentRows`。
- `RefreshIntents` 恢复「在格子里找名为 `Intents` 的子节点并重建」；
- `LayoutCells` 恢复在格子循环里给意图行定位。
- `BossHandPanel` 里三处 `DropIntentRow/ClearIntentRows` 调用与 `IntentZIndex/PanelZIndex` 常量删除。
- **保留**：v0.2.177 的另一半「炸弹倒计时为 1 时图标闪烁」（与图层无关），以及 v0.2.178 在
  `OnFrame` 开头加的「不在战斗里就隐藏整块」闸门（纯防御，不影响任何显示层级）。

⇒ 副作用：v0.2.160 那句「意图行最右被卡面挡住」的问题**回来了**（翻越撑击的数字可能再次被压住）。
若以后还要治，只能换思路（例如把意图画到卡面**上方**而不是下方、或缩短意图行宽、换行），
**不要再动 ZIndex**。
## v0.2.179：抬面板图层这条路走不通 —— 改成只抬「意图行」自己

用户 2026-10-29（附截图，地图界面顶上仍挂着整排意图箭头）：
「还是切到地图还显示。你要不把这两版回退（退到改意图之前，炸弹的改动不回退）然后再调高它的图层吧」。

**结论：抬 `BossHandPanel.ZIndex` 这条路是错的**——ZIndex 只在**同一个父节点**的兄弟之间比较，
把面板抬到 10/200 就等于把它抬到「战斗房 UI 里其它兄弟（地图/结算）之上」，所以怎么调都会顶穿界面。
（v0.2.177 的 200、v0.2.178 的 10 都试过了，都不行。）

回退 + 换方案：
1. **面板自己不再设 `ZIndex`**（回到默认，地图/结算照旧盖住我们）；
2. **意图行从「格子的子节点」改成「面板的子节点」**（`BossHandCard.BuildIntentRow` 里 `parent.AddChild(row)`，
   由 `Rows`（格子→意图行）登记表管理生命周期：`DropIntentRow` / `IntentRowOf` / `ClearIntentRows`）；
   ⇒ 意图行与卡面是**同一父节点的兄弟** ⇒ 给它 `ZIndex = 2` 就能压过本面板**所有卡面**，
   而它仍然在面板内部，**绝不会越过地图/结算界面**。
3. `LayoutCells` 里意图行的位置改成「格子的横向位置 + 原来的 0.4 卡宽的左上偏移」；
4. 格子被回收的三处（增量摘除 / `ClearCells` / `Reset`）都调 `DropIntentRow`，避免意图残留。
5. 炸弹闪烁（v0.2.177 的另一半）**保留不动**。
### 复测看点

- 战斗中：意图仍然压在卡面之上（不再被邻卡挡住）；
- 切到地图 / 结算界面：**不会再看得到**意图或面板（面板本身回到默认图层）；
- 若地图上还能看到，那说明还有别的「我们自己的高层节点」需要排查，把截图里那排箭头的具体位置告诉我。
## v0.2.178：意图层级太高，连地图界面都被压住

用户 2026-10-29：「意图的显示层级似乎太高了，切到地图界面都会显示。让它们比卡牌遗物能量的图层高就行」。
v0.2.177 把面板 `ZIndex` 直接给到 **200** ⇒ 连地图/结算界面都被它压住了。两处调整：

1. `BossHandPanel.PanelZIndex`：200 → **10**（原版同层 Control 基本都是 0，10 足够压过卡面/遗物/能量；
   真正的浮层（地图/选项）在别的 CanvasLayer 里，本来就不该被我们影响）。
   意图行仍保留 `IntentZIndex = 20`（同一个格子的兄弟之间比较，卡面永远盖不住意图）。
2. `BossHandPanel.OnFrame` 开头加了一道硬闸门：**`CombatState.IsLiveCombat() != true` ⇒ 整块 `Visible = false` 并直接返回**
   （以前只在 `FollowBoss` 里顺手设一次，某些时序下面板还留着 ⇒ 于是「切到地图界面还看得见」）。
### 复测看点

- 战斗中：意图仍然压在卡面之上（不被遮挡）。
- 切到地图/结算界面：面板与意图**都不再出现**。
- 回到战斗：面板自动恢复显示（同一处 `OnFrame` 里 `Visible = true`）。
## v0.2.177：意图图层 + 炸弹闪烁

### ①「攻势」意图放到较高图层
用户 2026-10-29：「让所有『攻势』显示在较高图层，避免被其它卡牌遮挡」。
Godot 的 `CanvasItem.ZIndex` **只在同一个父节点下的兄弟之间比较**，所以分两层抬：
- `BossHandPanel._Ready` 里 `ZIndex = BossHandPanel.PanelZIndex(200)`：整块面板压过原版手牌/卡面。
- `BossHandCard.BuildIntentRow` 里 `row.ZIndex = BossHandPanel.IntentZIndex(20)`：
  意图行与卡面是**同一个格子的兄弟** ⇒ 面板内意图永远压在卡面之上（同一面板内其它格子的卡也盖不住它）。
- 跨面板（别的 Boss 的那套面板）仍按节点顺序决定 —— 两套面板都已经是高层了。
### ②「炸弹」倒计时剩 1 时图标闪烁
用户 2026-10-29：「当储君 buff 栏中的炸弹倒计时为 1 时，让该 buff 闪烁，类似 1 代崩坠中的商人」。
新增 `Patches/BombBlink.cs`（由 `BossFrameTick` 每帧调用，**不用自己的 `_Process`** —— 本模组里那个不触发）：
- 找这只 Boss 身上 `TheBombPower` 对应的 `NPower` 图标节点（递归 + 按 Boss 缓存，失效重找）；
- `Amount <= 1`（还有 1 回合就炸）⇒ `Modulate` 在 0.4～1.0 的正弦呼吸（周期 800ms）；
- 其余情况恢复 `Modulate` 全白。只改显示，不动任何战斗数值；战斗结束 `BossFrameTick.ResetForNewFight` 里清缓存。
### 复测看点

- 意图图标不再被邻座卡或自己那张卡压住（尤其之前被挡的翻越撑击数字）。
- 储君的炸弹显示「1」时图标开始一闪一闪，爆掉或层数变化后恢复正常。
## v0.2.176：所有 Boss 死亡时没有胜利音效 —— 我们停音乐停得太早

用户 2026-10-29：「所有 boss 死亡时没有播放胜利音效」。
v0.2.175 把 `BossBgm.Stop()` 挪到了「没有别的 Boss 活着」时才调，但它仍然只在 `CharBossMonster.BeforeRemovedFromRoom`（Boss **被移出房间**）那一步执行 —— 而这一块**晚于**游戏自己的「战斗结束/胜利」音乐（反编译 `NRunMusicController.GetTrack`：`roomType.IsCombatRoom() && !CombatManager.IsInProgress ⇒ MusicProgressTrack.CombatEnd`）。
也就是说游戏已经切到战斗结束音乐了，我们随后再来一次 `StopCustomMusic()`（`stop_music` + 按 `_currentTrack` 重播 + `Progress=7`）⇒ **把它掐掉**。
修法：把「还原原版音乐」提前到**最后一只 Boss 倒下的那一刻**（每帧驱动里，和
`BossLockOn.OnBossDied` 同一个循环）。

```csharp
if (!MultiBossPlan.AnyOtherBossAlive(each) && BossBgm.IsPlaying)
{
    Logger.Log("[CharBoss] BGM：最后一只 Boss 倒下 → 立即还原原版音乐（战斗结束/胜利音效由游戏自己播）");
    BossBgm.Stop();
}
```

`BeforeRemovedFromRoom` 里那两处调用保留作为兜底（`Stop()` 在 `_playing == null` 时是空操作）。
新增 `BossBgm.IsPlaying` 便于判断/记录。
### 复测看点

- 打死最后一只 Boss ⇒ 日志出现上面那行；随后应听到原版**战斗结束/胜利**音效。
- 中途打死一只（其它还活着）⇒ 音乐继续，不出现这行。
## v0.2.175：死一只 Boss 就静音 —— `BeforeRemovedFromRoom` 无条件停 BGM

用户 2026-10-29：「一个 boss 死亡后 boss 战的背景音乐就停了。应该等所有 boss 都死亡后再停止播放，
然后播放 boss 战胜利音效」。
根因：`CharBossMonster.BeforeRemovedFromRoom`（Boss 被移出房间 = 死亡/战斗结束都会跑）里**无条件**调了两次 `BossBgm.Stop()` ⇒ 任意一只 Boss 一死就把我们播的曲子掐掉，而其它 Boss 还在打。
修法：新增 `ShouldStopBgmOnLeaveRoom()`，两处调用都改成按它判断：
- 场上**没有别的活着的我们的 Boss** ⇒ 停（随后由游戏播胜利音效）；
- 还有别的 Boss 活着、且战斗仍在进行 ⇒ **不停**。
- 还有别的 Boss 活着、但战斗已经不在（玩家全灭 / 退出）⇒ 停。
### 复测看点

- 打死一只 Boss ⇒ 音乐**继续**，直到最后一只倒下才停（然后响起原版胜利音效）。
- 单人局行为不变（唯一那只死了就是最后一只）。
## v0.2.174：Boss 死亡会刷新所有人的锁定 —— 锚点序号取的是「当前 Enemies 下标」

用户 2026-10-29：「boss 死亡会刷新锁定，没有被锁定的玩家不是那个对应 boss 死亡的而是另一个玩家，
所有玩家对应的 boss 都发生了变化。应该是**只有死亡 boss 对应的玩家失去跟踪锁定**，其它玩家与 boss 的锁定关系不变」。
根因在 `ComputeAssignment` 的「锚点」：

```csharp
var bosses = OurBossesSorted(combat);      // 当前 Enemies 里的我们的 Boss
...
var anchor = roster[i % roster.Count];     // i = 当前列表里的下标
```

死掉的 Boss 会从 `CombatState.Enemies` 里**消失** ⇒ 排在它后面的 Boss 下标**整体前移一位** ⇒ 每个 Boss 算出来的锚点玩家都换了人 ⇒ 所有人的锁定关系一起变。（`Locks` 是纯函数算出来的缓存，所以「刷新」是必然的。）

修法：锚点序号改用**「本场 Boss 花名册」**（`BossRank` / `BossRosterOf`）：
- 花名册 = 本场出现过的我们的 Boss 的**怪物 id**，按 id 升序、**只增不减**；
- 死掉的 Boss 仍然占着它那一格（只是不再锁定任何人，`!IsAlive` 直接跳过）；
- ⇒ 死的只有它自己那一份锁定消失，其余 Boss 与玩家的关系一个字都不动。
- 花名册按 `CombatState` 缓存，`Reset()`（战斗结束）时清空。

配合已有的行为：**玩家**阵亡时那只 Boss 仍然按「被锁份数最少的活人」改派（v0.2.158 的口径，不变）；
本次改的是**Boss** 阵亡那一条。
### 复测看点

- 打死一只 Boss ⇒ 只有**它锁定的那名玩家**身上少一个「跟踪锁定」图标；
- `跟踪锁定分配表` 里其它 Boss 与玩家的对应关系**与死前完全一样**（不会出现全员换人）。
- 玩家阵亡的那条逻辑不受影响（孤儿 Boss 仍会补给被锁最少的活人）。
## v0.2.173：「牺牲」描述统一 + 手套又烧匕首雨（旧名单跨回合）
### ①「牺牲」意图的文案

用户 2026-10-29：「把本模组自创的意图类型『牺牲』的描述改成『这个敌人……』（后面不变），
与原版其他的意图描述统一。其他都不改。」
`content/localization/zhs/intents.json`：`SACRIFICE.description`
`这名敌人会付出代价来换取强力效果。` ⇒ **`这个敌人会付出代价来换取强力效果。`**
（英文版 `This enemy will …` 本来就是原版句式，未动。）
⇒ 改了 `content/` ⇒ **已重打 PCK** 并用 `find_in_pck.gd` 确认包内是新文案。
### ②涂毒贼第 3 回合又开始烧匕首雨 —— 旧回合的名单被拿去用了
日志（v0.2.172，14:16）：
```
自动选择：点名名单为空 ⇒ 按剧本第 2 回合现算（点名 [RICOCHET,DEFEND_SILENT]；她当前 TurnNumber=2）
自动选择: 候选 3 个 → 选 [DEFEND_SILENT]（第 1 回合的点名：…）        ← 回合标号都不对…（下一回合，手牌变成 带毒刺击/匕首雨/打击）
自动选择: 候选 3 个 → 选 [DAGGER_SPRAY]（第 2 回合的点名：0 张按实例 + 0 张按牌名 + 其余 1 张兜底；名单 1 条）
```

根因：v0.2.166 的「按剧本现算」顺手把 `ActingBoss/ActingTurn`（本是「**正在出牌**」的标记）写成了某一回合，而 `Current()` **只看这两个字段、不校验回合号** ⇒ 下一个回合的手套消耗拿到的是**上一回合的旧名单**（里面点名的连续反弹/防御在新手牌里根本不存在）⇒ 命中 0 张 ⇒ 退化成兜底排序
⇒ 三张 1 费攻击牌里按牌名挑中 `DAGGER_SPRAY`。
修法：
1. `Current()` 换成 `LiveMarks(boss, turn, options)`：**先按「候选项属于谁 + 它现在的 `TurnNumber`」**取，
   只有「`ActingBoss` 就是这只 Boss 且 `ActingTurn` 等于当前回合」（= 真的正在出牌）才直接用，
   否则**按剧本对当前回合现算**。
2. `SeededMark` **不再读** `ActingBoss/ActingTurn`（那两个字段只由 `PlayOne` 设置）；
3. 兜底排序 `Order()` 的「剧本要打的牌压最后」也改成用本次选择的 boss/回合（`_orderBoss/_orderTurn`），
   不再读那两个可能过期的静态字段。
### 复测看点

- 第 3 回合：`自动选择: 候选 3 个 → 选 [STRIKE_SILENT]（第 3 回合的点名：…）`；匕首雨照常打出。
- 「牺牲」意图悬浮文案以「**这个敌人**」开头。
## v0.2.172：切断的印牌

用户 2026-10-29：「切断会往手牌中添加灵魂，但这张灵魂已经被写在『剧本』里了。把切断的印牌拦了」。
反编译 `Severance.OnPlay`：
```csharp
var souls = Soul.Create(Owner, 3, CombatState);                                   // → 3 张灵魂
await CardPileCmd.AddGeneratedCardToCombat(souls[0], PileType.Draw,    Owner, CardPilePosition.Random);
await CardPileCmd.AddGeneratedCardToCombat(souls[1], PileType.Discard, Owner);
await CardPileCmd.AddGeneratedCardToCombat(souls[2], PileType.Hand,    Owner);     // ← 进手牌那张
```

剧本里（`SoulBoneScript`）已经把这张灵魂写好了 ⇒ 再印一张，手牌里就多出一张剧本没算过的牌。
改法（新增 `Patches/SeveranceNoCardGenerationPatch`）：
- 在 `CardPileCmd.AddGeneratedCardToCombat` 上挂 Harmony 前缀；
- 只在「**正在打出的那张牌**是 `SEVERANCE`」这个窗口里，把生成**整块吞掉**
  （返回 `Task.FromResult<CardPileAddResult>(default)` —— 该结构体 `success=false`，
  语义就是「没加进去」，调用方 `await` 完照常继续，不会卡住）；
- 「正在打哪张牌」由 `CharBossMonster.CurrentPlayingEntry` 提供（`PlayOne` 里出牌前置位、`finally` 里清掉）。
- 要拦别的牌的印牌，把它的 entry 加进 `CharBossMonster.BlocksGeneratedCardsEntries` 即可（目前只有 `SEVERANCE`）。

真人玩家、别的牌、别的 Boss 一律不受影响（判据里先要求 `creator.Creature.Monster is CharBossMonster`）。

日志：`[CharBoss] 拦掉 SEVERANCE 的印牌：SOUL → Hand（这张牌剧本里已经写好了，再印一张会让手牌和计划对不上）`

### 复测看点

- 灵魂骨（二层）打出切断后，手牌里**不应**多出一张灵魂（只有剧本里那张）。
- 那行日志应出现 3 次（抽牌堆 / 弃牌堆 / 手牌各一次）。
## v0.2.171：为什么有的 Boss 打完牌后界面就没了 —— 手牌为空即收面板

用户 2026-10-29：「有的 boss 的界面在他打完本回合要打的牌就消失了（如未打的牌，能量，遗物等），有的则保留。能不能统一成保留？」。
根因在 `BossHandUi.Refresh`：
```csharp
var hand = boss.Shadow?.PlayerCombatState?.Hand.Cards;
if (hand == null || hand.Count == 0) { ReleasePanel(boss); return; }   // ⇒ 手牌为空就整块收
```

⇒ **把手牌全部打完**（含奇巧自动打出/生成牌都出掉）之后手牌是空的 ⇒ 整块面板（卡面 + 状态条：能量/遗物/药水/辉星 + 独立意图）一起被释放；而手里还留着「打不出的牌」的 Boss 手牌不为空 ⇒ 面板保留。所以表现不统一。
修法：**只在拿不到手牌数据时才收面板**；`hand.Count == 0` 时照旧 `Rebuild`（卡面区自然变空、状态条继续显示）。面板的生命周期于是只有两个终点：她阵亡（`ReleasePanel`）或战斗结束（`EndFight`）。
`LayoutCells` 本来就按「本回合最多几张牌」（`_turnCards`）算宽度，所以卡面清空后状态条**不会左滑/跳动**。
### 复测看点

- 某只 Boss 把本回合要打的牌全打完后，她的能量/遗物/药水仍然留在原位；下一回合新牌进来时卡面直接补齐；
- 不会再出现「几只 Boss 里有的界面消失、有的还在」。
## v0.2.170：刀刃之舞印的小刀没吃到「一心化万」的增伤

用户 2026-10-29：「涂毒贼的刀刃之舞没有考虑一心化万的增伤（它本身是 1 费但它印的小刀是 0 费）」。
关键点说得很准：`OneForAllPower.ModifyDamageAdditive` 判的是**`cardSource`（被强化的那张牌）**：
```csharp
if (cardSource == null) return 0m;
if (cardSource.Owner.Creature != Owner) return 0m;
...
if (cardPlay == null && cardSource.EnergyCost.GetWithModifiers(CostModifiers.All) != 0) return 0m;
return Amount;
```

刀刃之舞自己是 1 费（不吃），但它**印出来的小刀是 0 费攻击牌**（吃 +N）。而我们的意图预览是这样算的：`ShivBaseDamage(4) + 待到手力量`，然后交给原版意图节点 —— 节点那一路的 `cardSource` 是 **null**（原版自己的写法）⇒ 这类「按牌判定」的增伤永远进不了预览。
修法（`BossIntentBuilder`）：
1. 新增 `ShivCardFor(boss)`：每只 Boss 缓存一张**真实的小刀牌**（`CombatState.CreateCard<Shiv>(shadow)`）；
2. 新增 `ShivDamagePerHit(boss, pendingRise, target)`：把「4 + 待到手力量」喂进
   `BossDamagePredictor.FinalDamage(shivCard, …)` —— 也就是**用真正的牌过一遍原版伤害管道**，
   于是力量/虚弱/易伤与「0 费攻击 +N」全都算对；
3. 意图类型从 `AttackIntent.Multi`（节点会再算一次）换成 `AttackIntent.Raw`（数字原样），
   避免管道算完又被节点加一遍；幻影之刃的「第一击 +9」照旧；
4. 「刀刃交响曲」每只队友那把刀的伤害也走同一函数（各自吃自己的「一心化万」）。
### 复测看点

- 涂毒贼打出「一心化万」后，刀刃之舞 / 隐秘匕首 / 刀刃交响曲的**每把刀**数字应比原来多 3（升级 +4）；
- 没有一心化万时与以前完全一致（4、幻影之刃 13 + 4×N-1）。
## v0.2.169：手套为什么「不听剧本」—— 两个 bug 叠在一起
用户 2026-10-29：「第三回合涂毒贼不打匕首雨（好像被手套烧了？），但是我有写烧打击，为什么手套不听剧本的？」。
剧本第 3 回合确实写了 `C<StrikeSilent>(burned: true)`（烧打击）。手套烧错牌是**两个 bug**：
### ① v0.2.166 的「按剧本现算」返回了**错误回合**的名单
手套的「消耗」发生在她回合刚开始，那一刻 `boss.TurnNumber` **还是上一回合**。`DeriveFromScript` 会依次试
`TurnNumber` 与 `TurnNumber+1`：第 2 回合的剧本里没有能对上的牌（补不到），第 3 回合的剧本补到了**打击** —— **但旧代码补完之后仍然返回「TurnNumber（第 2 回合）那份」，那当然是空的** ⇒ 选牌照样退化成兜底排序。
### ② 兜底排序会在「三张一模一样的攻击牌」里按牌名挑

第 3 回合手牌 = 带毒刺击 / 匕首雨 / 打击，全是 1 费攻击（价值档位、费用都一样）。按牌名序 `DAGGER_SPRAY < POISONED_STAB < STRIKE_SILENT` ⇒ **匕首雨被选中烧掉**。
### 修法

1. `DeriveFromScript` 返回**真正补到东西的那一回合**的名单（并打一行
   `自动选择：点名名单为空 ⇒ 按剧本第 N 回合现算（点名 [X]；她当前 TurnNumber=M）`，以后一眼可查）。
2. `Order()`（兜底排序）最外层新增一档：**剧本这一回合要打出去的牌一律排到最后**（`PlayTypesFor`）。
   就算点名失败，也不会再把「剧本要打的牌」烧掉/弃掉。
### 复测看点

- 第 3 回合日志：`自动选择 → 选 [STRIKE_SILENT]（第 3 回合的点名：…）`（不是 `DAGGER_SPRAY`）；
- 匕首雨照常打出（4 点×2）；手套烧的是打击。
## v0.2.168：为什么战士跑到故障机器人前面 —— 旧的「左右分组」排序
用户 2026-10-29：「我记得 2 层行动顺序不是 猎骨机战储 吗？怎么现在战士在故障机器人前面？」。
链路：行动的先后 = `CombatState.Enemies` 的次序 = 遭遇 `GenerateMonsters()` 里那个列表的次序
（而且从 v0.2.141 起**站位也恒等于这个次序**，`BossEnemySpreadPatch.PlacementRank` 用 `Enemies` 下标）。
而那个列表以前是按**`LeftRank` 左右分组**排的：
```csharp
// 旧：静默/亡灵/铁甲 = 左组(0)，故障/储君 = 右组(1)
return monster is SilentBoss or NecrobinderBoss or IroncladBoss ? 0 : 1;
```

⇒ **铁甲战士被算进左组**，于是不管阵容怎么排，战士永远排在故障机器人（右组）前面 ⇒ 得到「猎骨**战**机储」。
改法：`GenerateMonsters` 的排序换成**职业固定次序**（`StationRank(act, monster)`）：

| 幕 | 次序（左→右 = 先手→后手） |
|---|---|
| 第 1、2 幕 | **猎（静默）→ 骨（亡灵）→ 机（故障）→ 战（铁甲）→ 储（储君）** ⇒ 用户口径 |
| 第 3 幕 | 猎→骨→机→战→储（保留老设计：**易伤战永远最后**） |

日志：`[CharBoss] N 只 Boss 行动顺序（按职业次序，第 2 幕）：SILENT_ENVENOM → NECROBINDER_SOUL → DEFECT_CLAW → IRONCLAD_BLOOD → REGENT_FORGE`

### 若第三幕也要统一成「猎骨机战储」
把 `CharBossEncounters.StationRank` 里 `act >= 2` 那段改成和上面一样的次序即可（一行改动）。
我没有直接改，是因为三层一直有「**易伤战永远最后**」这条你的老设计（v0.2.130）。
### 复测看点

- 二层 4 人局，日志里那行应是 `猎→骨→机→战→储`，屏幕上从左到右也一致；
- 行动顺序 = 站位顺序（这条从 v0.2.141 起成立，这里只是换了排序依据）。
## v0.2.167：涂毒贼第 2 回合 —— 防御还是被烧（用户订正），只剩「弃连续反弹」这一处要改

用户 2026-10-29 再次订正：「哦不对我忘了算手套了，应该是这样：生存者（丢弃连续反弹）→ 连续反弹（奇巧）→ **防御（被烧）**」。
⇒ v0.2.166 里我把第 2 回合改成「防御打出、烧打击」是**多余**的，已回退。
```text
第 2 回合（最终）：生存者（弃 1 张 → 连续反弹）、连续反弹（奇巧：被丢弃后免费打出）、防御（被烘焙手套烧掉）
```

「1~7 回合：打击×4、防御×3」的账也回到原样。
**真正的修复保留在 v0.2.166 的选择器改动上**（`BossCardSelector.DeriveFromScript`）：
- 烘焙手套的「消耗」发生在**她回合刚开始、早于本回合规划**，那一刻点名名单是空的 ⇒
  旧逻辑退化成「奇巧优先 ⇒ 价值」，把**连续反弹（奇巧）当消耗目标烧掉**了；
- 现在名单为空时会**按剧本就地补一份**（读候选项的 owner 认 Boss + `TurnNumber`（含 +1 回合），
  把 `Burned` / `DiscardInstead` 点名的牌按实例登记）⇒ 手套烧的是剧本点名的**防御**。
  随后「生存者」弃的才是**连续反弹**，奇巧再把它免费打出去。
### 复测看点

- 日志：`剧本第 2 回合: 打 [SURVIVOR] 跳过 [RICOCHET(被丢弃 ⇒ 奇巧自动打出),DEFEND_SILENT(被烧)]`。
- `自动选择 → 选 [RICOCHET]`（不是 DEFEND_SILENT）；
- 面板：生存者（格挡）、连续反弹的攻势意图，防御那张不参与本回合。
## v0.2.166：涂毒贼第 2 回合 —— 消耗发生在规划之前，奇巧牌被「烧」掉

用户 2026-10-29（v0.2.165 之后仍然没区别）：「她第二回合的意图应该是**生存者（丢弃连续反弹）、连续反弹（奇巧）、防御**」。
### 真正的链路
1. 她回合**开始**时，烘焙手套（`ToastyMittens.AfterPlayerTurnStart`）要「消耗 1 张手牌」——
   这一刻**本回合的规划还没有**（`MarkPreferDiscard` 在 `Plan` 里），所以点名名单是**空的**。
2. 名单为空 ⇒ 选择器退化成**奇巧优先 ⇒ 价值 ⇒ 费用**（奇巧优先本来是给「丢弃后免费打出」用的）。
3. 于是**连续反弹（奇巧）被当成消耗目标烧掉了** —— 它既没被丢弃、也没有奇巧自动打出。
4. 之后「生存者」要弃 1 张，手里只剩防御 ⇒ 弃防御。
### 修法（两层）

1. **选择器在名单为空时按剧本就地补一份**（`BossCardSelector.DeriveFromScript`）：
   拿候选项的 owner 认出是哪只 Boss、读它的 `TurnNumber`（顺带看 +1 回合），
   把剧本那一回合 `Burned` / `DiscardInstead`（含多人/单人变体）点名的牌**按实例补进名单**。
   ⇒ 烘焙手套会去烧剧本点名的那张，而不是「奇巧优先」烧掉连续反弹。
2. **剧本按用户口述调整第 2 回合**：
   ```text
   改前：生存者, 连续反弹(被丢) , 防御(被烧)
   改后：生存者, 连续反弹(被丢) , 防御 , 打击(被烧)
   ```
   费用仍然刚好：生存者 1 + 防御 1 = 2，连续反弹奇巧免费。
   ⇒ 这会改变「烧牌账」：原本 1~7 回合烧「打击×3、防御×3」，现在是**打击×5、防御×2**
   （第 2 回合烧的那张由防御改成打击）。若你希望维持 4/3，说一声我把另一回合的烧牌对调。
### 复测看点

- 第 2 回合面板意图应是三张：**生存者（弃 1 张 ⇒ 连续反弹）**、**连续反弹（奇巧自动打出）**、**防御（格挡）**。
- 日志：`剧本第 2 回合: 打 [SURVIVOR,DEFEND_SILENT] 跳过 [RICOCHET(被丢弃 ⇒ 奇巧自动打出),STRIKE_SILENT(被烧)]`、
  ⇒ `自动选择 → 选 [RICOCHET]`。
- 若哪张奇巧牌又被漏标，规划器里的 `⚠ 剧本自检` 会点名。
## v0.2.165：涂毒贼第 2 回合「生存者」丢了防御，连续反弹没打出去

用户 2026-10-29：「涂毒贼第二回合生存者丢防御而不是连续反弹导致连续反弹没有被打出」。
根因是**剧本数据**，不在选择器：`SilentPoisonScript` 第 2 回合（和第 9 回合）的连续反弹写的是 `C<Ricochet>(costOverride: 0)` —— **漏了 `discardInstead: true`**。没有这个标记 ⇒ `MarkPreferDiscard` 不会被调 ⇒ 自动选择器只能按「价值 ⇒ 费用」挑，把纯技能的防御（rank 1）挑走了，而连续反弹（奇巧）一直躺在手里，自然不会自动打出。（对照 `SilentSlyScript` 里同样位置写的是 `C<Ricochet>(discardInstead: true, costOverride: 0)`。）

修法：
1. 两处改成 `C<Ricochet>(discardInstead: true, costOverride: 0)`。
   该剧本的 `C<T>` 助手原来**没有** `discardInstead` 参数，一并补上（并转发到 `ScriptCard.DiscardInstead`）。
2. **加了一条剧本自检**（`BossScriptPlanner` 每次规划后）：手牌里剩下的**奇巧牌**
   （`CardKeyword.Sly`，被丢弃会免费自动打出）如果**既没排进计划、也没被点名丢弃**，就打一行
   `⚠ 剧本自检（第 N 回合）：奇巧牌 [X] 既没排进计划、也没被点名丢弃（…检查剧本）`。
   「费不够 / 不可打出 / 不在手牌」导致没打的不算（避免刷无意义警告）。
   ⇒ 以后哪个剧本再漏写这个标记，日志会直接点名。
顺手把全仓脚本里的奇巧牌条目都核了一遍：其余（`SilentSlyScript` 的 FlickFlack / Ricochet / Reflex /
Tactician / Untouchable / Backflip(sly:true)）**都有** `discardInstead`，只有涂毒贼这两处漏了。
### 复测看点

- 第 2 回合：`剧本第 2 回合: 打 [SURVIVOR] 跳过 [RICOCHET(被丢弃 ⇒ 奇巧自动打出), DEFEND_SILENT(被烧)]`，
  并且 `自动选择 → 选 [RICOCHET]（第 2 回合的点名：…）`。
- 连续反弹应被丢弃并**免费打出**（打出 4 次随机伤害）。
- 若还有别的剧本漏写，日志里会有上面那行 `⚠ 剧本自检`。
## v0.2.164：一心化万的「0 费攻击 +3」没进伤害预览
用户 2026-10-29：「2 层物理机打出一心化万后，boss 的伤害预览中好像没有计算一心化万对 0 费攻击牌的增伤」。
反编译 `OneForAllPower.ModifyDamageAdditive`（一心化万）：
```csharp
if (!props.IsPoweredAttack()) return 0m;
if (cardSource == null) return 0m;                       // ← 关键
if (cardSource.Owner.Creature != Owner) return 0m;        // 只加自己的牌
if (cardPlay?.Card.EnergyCost.CostsX ?? cardSource.EnergyCost.CostsX) return 0m;      // X 费不算
if (cardPlay != null && cardPlay.Resources.EnergySpent != 0) return 0m;               // 实际花了费不算
if (cardPlay == null && cardSource.EnergyCost.GetWithModifiers(CostModifiers.All) != 0) return 0m;
return Amount;                                            // +3（升级 +4）
```

我们的预览管道 `BossDamagePredictor.FinalDamage` 调 `Hook.ModifyDamage(...)` 时把
**`cardSource` 与 `cardPlay` 都传成 null** ⇒ 第一条 `cardSource == null` 就直接返回 0。于是「只给 0 费攻击牌 +3」这条永远不生效（实伤是对的，因为实伤有真实的 CardPlay）。
修法：`FinalDamage` / `CapDamage` 的 `cardSource` 传成**这张牌本身**（`cardPlay` 仍为 null —— 预览阶段没有真实 CardPlay，而且原版专门写了 `cardPlay == null` 时「看牌面费用」的那半边）。⇒ 0 费攻击牌预览 +3，1 费牌不加，与实伤一致。
`LogDamagePipeline` 那行（`意图伤害管道 <牌名>: 牌面 X → Y（力量…，目标易伤=…）`）可以直接核对有没有加上。
### 复测看点

- 物理机打出「一心化万」后，**0 费攻击牌**（超越光速 / 螺旋钻机等）的意图数字 = 牌面 +3（升级 +4）；
- 1 费、2 费的攻击牌不受影响；
- 日志里对应牌应出现 `意图伤害管道 …: 牌面 X → X+3`。
## v0.2.163：暗黑充能球改为打「被这只 Boss 锁定的那位」，而不是血最少的玩家

用户 2026-10-29：「做，让暗黑充能球攻击被锁定玩家而非血最少的玩家」。
反编译 `DarkOrb.&lt;Evoke&gt;d__11.MoveNext`：
```csharp
IReadOnlyList<Creature> hittableEnemies = darkOrb.CombatState.HittableEnemies;
var weakestEnemy = hittableEnemies.MinBy(c => c.CurrentHp);       // ⇒ 血最少的
await CreatureCmd.Damage(…, weakestEnemy, darkOrb.EvokeVal, Unpowered, owner);
```
它读的是 **`HittableEnemies`**（不是 `GetOpponentsOf`），所以我们那套「只留锁定玩家」的收窄管不到它。
做法与群蛇形态（`SerpentFormLockOnPatch`）完全同一套：
新增 `Patches/DarkOrbLockOnPatch`，给 `DarkOrb` 的 `<Evoke>` 状态机 `MoveNext` 挂前缀 —— 在状态机**刚开始**（`<>1__state == -1`，`HittableEnemies` 还没被读）时调用
`HittableEnemiesSidePatch.RequestNarrowOnce(被锁定的那位)`，把「可命中敌人」一次性收窄成一个人，后面读 `MinBy(CurrentHp)` 于是只能选他。
- 只设**一次**（判 `<>1__state`）：一次性收窄两帧内有效，重复设置会在唤起提前结束时残留、被别的读取吃走。
- 真人玩家的暗黑球**不动**（只对我们 Boss 的球生效）；
- 挂载入口在 `BuildInfo.LogOnce`（和群蛇形态一起），启动日志会打
  `暗黑充能球：已挂 N 个 Evoke 状态机（多人时由「血最少的玩家」改为「只打被这只 Boss 锁定的那位」）`。
顺带核对：**玻璃球**的激发是 `Damage(HittableEnemies 全部)` —— 真群攻，打所有玩家，属预期，未改动。
### 复测看点

- 启动日志应有上面那行「已挂 N 个 Evoke 状态机」；
- 多人时暗黑球的激发伤害只落在**被黑球机锁定的那位**身上（哪怕别的玩家血更少）；
- 单人：唯一玩家，行为与原来一致。
## v0.2.162：把「放宽收窄」的所有入口都核了一遍（用户追问）
用户 2026-10-29：「那其它的随机攻击牌、充能球或能力（如群蛇形态）会不会也被你放宽了？」。
### 又堵掉一个洞：`MultiTargetCardDepth` 的时间窗豁免

v0.2.141 的写法是「**正在打一张群攻牌时，整段不许收窄**」。那是一个**时间窗**，所以那张群攻牌结算过程中**嵌套触发**的任何随机伤害（充能球被动、别的随机单体）
也一起被放宽了 —— 和连续反弹那个 bug 同一性质。现在 `NarrowToLockedPlayer` **完全不再读** `MultiTargetCardDepth`：豁免只由 `AoeAttackNotNarrowedPatch` 在**这次攻击本身**上判（真正的 `TargetingAllOpponents` 才恢复成全部对手；`IsRandomlyTargeted` 一律不放宽）。`MultiTargetCardDepth` 仍由 `CharBossMonster` 递增/递减，但**已不参与判定**（留作诊断）。
### 逐条核对结果（反编译 + 代码）
| 路径 | 实际行为 | 结论 |
|---|---|---|
| 单体牌（打击 / 太阳打击 / 碰撞轨迹 / 彗星…） | `Targeting(cardPlay.Target)`，目标由我们给（出牌时实时锁定） | ⇒ 只打被锁的人 |
| 随机单体/随机多段（**连续反弹** / `TargetingRandomOpponents`） | `GetOpponentsOf` ⇒ 我们收窄 | ⇒ 只打被锁的人（v0.2.161 修好的） |
| 真正的群攻（翻越撑击、播种、匕首飞舞、闪电霹雳…） | `AttackCommand.GetPossibleTargets` 多目标分支 ⇒ `AoeAttackNotNarrowedPatch` 恢复成全体 | ⇒ 打全体 |
| **闪电球 / 玻璃球被动** | `CombatState.GetOpponentsOf(owner).Where(IsHittable)` ⇒ `Rng.NextItem` | ⇒ 收窄生效，只打被锁的那位 |
| **群蛇形态** | `HittableEnemies` + `Rng.NextItem`，由 `SerpentFormLockOnPatch` 做**一次性**窗口收窄 | ⇒ 不受本次改动影响 |
| 全体生效的非攻击（迷雾、尖啸、冰雹风暴、炸弹） | `HittableEnemies` 全体遍历 | ⇒ 本来就该打全体 |

### ⇒ 顺带发现一个**不是这次改坏、但和设计稿不符**的地方
**黑暗球**（`DarkOrb`）的激发是
`Enumerable.MinBy(CombatState.HittableEnemies, c => c.CurrentHp)` —— 它从**全体可命中敌人**里取**血最少**的那个，不经过 `GetOpponentsOf` ⇒ 可以打到**没被锁定的玩家**。
同理还有「弹跳药瓶」这类「随机给予一名敌人」的效果（也走 `HittableEnemies`）。
这与跟踪锁定的文案「充能球只会攻击你」不符。要修的话得**逐个消费点**挂钩子（像 `SerpentFormLockOnPatch` 那样在 `DarkOrb.Evoke` 开一次性收窄窗口），不能全局收窄 `HittableEnemies`（那会把迷雾/尖啸/冰雹这些真群攻也变成单体）。**等用户点头再做。**

## v0.2.161：连续反弹打到了没被锁的玩家 + 意图顺序（防御仍未到最右）

### ① 连续反弹（Ricochet）攻击了没被奇巧贼锁定的玩家 —— v0.2.149 的群攻豁免放错了

用户 2026-10-29：「连续反弹会攻击没被奇巧贼锁定的玩家？？？？？？」
根因：v0.2.149 为了修「翻越撑击只打一个人」，加了
`AoeAttackNotNarrowedPatch`（`AttackCommand.GetPossibleTargets` 的 Postfix）：**只要 `IsMultiTargeted`
就把收窄结果恢复成全部对手**。可是反编译显示 `TargetingRandomOpponents(...)` 干的事情是：
```csharp
_combatState = combatState;   // ⇒ IsMultiTargeted 也变成 true
IsRandomlyTargeted = true;
```

⇒ 连续反弹这种「**每次只打一个随机对手**」的牌也被当成群攻放行了，于是打到了任何玩家。
修法：在放行之前先看原版现成的判断 `IsRandomlyTargeted` —— **随机目标一律照旧收窄**
（只打被锁定的那位），只有真正的 `TargetingAllOpponents`（翻越撑击 / 播种 / 匕首飞舞…）才豁免。
### ② 意图顺序：防御还是没到最右
v0.2.160 只把「**同一张牌内部**」的 `DefendIntent` 挪到末尾 —— 可用户看到的那一行是**跨卡牌**的，
顺序由**剧本里卡牌的先后**决定，所以翻越撑击（被丢弃的奇巧牌，排在防御前面）依然在最右。
这次直接改**剧本顺序**（`SilentSlyScript` 第 2 回合）：

```
改前：余像, 鬼祟+ , 早有准备+ , 翻越撑击(被丢 , 防御 , 打击
改后：余像, 鬼祟+ , 早有准备+ , 翻越撑击(被丢 , 打击 , 防御
```

费用不变（早有准备 0 + 打击 1 + 防御 1 = 2 费），单人/多人语义不变（防御仍是「只在单人时被丢弃」）。
### 复测看点

- 连续反弹只应打到**被奇巧贼锁定**的那位（日志 `选靶收窄：SILENT_SLY_BOSS …` 或直接看受击者 NetId）；
- 第 2 回合意图行最右边应是**格挡箭头**，翻越撑击的伤害数字在它左边。
## v0.2.160：杂技丢错牌（点名名单被「下一回合的规划」清掉）+ 意图行防御排最后
### ① 奇巧贼第 3 回合「杂技」丢了「触不可及」，连续反弹没被丢 ⇒ 奇巧没触发
日志（v0.2.159，12:56）：
```
剧本第 3 回合: 打 [ACROBATICS,FOOTWORK] 跳过 [RICOCHET(被丢弃 ⇒ 奇巧自动打出), …]
PLAY ACROBATICS
自动选择: 候选 8 个（min=1 max=1）→ 选 [UNTOUCHABLE]（点名 0 张按实例 + 0 张按牌名 + 其余 1 张兜底）
```
规划期明明点名了 `RICOCHET`，可**选牌那一刻名单是空的** ⇒ 退化成「按价值挑」，把本来要打的
「触不可及」扔了，连续反弹留在手里没被丢弃 ⇒ 奇巧不触发、那张牌也没打出去。
根因：`PreferDiscard` 是**全局一份**，而 `BossScriptPlanner.Plan` 一进门就 `ClearPreferDiscard()`
「新回合重新点名单」。但是**回合执行期间**，规划器还会为**下一回合**再算一次计划（意图刷新）——
那一次把本回合刚点好的名单**清空**了，于是执行中的第 3 回合丢了名单。
修法：名单改成**按 (Boss, 回合) 各存一份**（`BossCardSelector.PreferByTurn`）：
- `MarkPreferDiscard(boss, turn, card)` / `ClearPreferDiscard(boss, turn)` 只动这一回合那份。
- 选牌时用「**正在出牌的那只 Boss + 它这一回合**」（`ActingBoss` / `ActingTurn`，由
  `CharBossMonster.PlayOne` 设置）去取名单；
- 日志改成 `（第 N 回合的点名：X 张按实例 + Y 张按牌名 + 其余 Z 张兜底；名单 M 条）`，可直接核对。
### ② 意图行把「防御」挪到最后
用户 2026-10-29：「把第二回合奇巧贼的意图预览改成**防御是最后一个**，因为现在这张**翻越撑击的伤害数字会被挡掉**」。
意图行是从卡牌左侧往右排的，越靠右越容易被右边的卡面/邻座面板压住；格挡图标只是一个箭头、几乎不带数字信息 ⇒ 压住它损失最小。
`BossHandCard.BuildIntentRow` 现在把 `DefendIntent` 全部排到这一行**末尾**，
其余意图保持原顺序（先牌本身、再激发球）。
### 复测看点

- 第 3 回合：`自动选择 …（第 3 回合的点名：0 张按实例 + 1 张按牌名 + …；名单 N 条）`
  且选的是 **RICOCHET**；随后它应被丢弃并因奇巧自动打出。
- 意图行：同一张牌的多条意图里，**格挡箭头在最后**，伤害数字在它左边（不被挡）。
## v0.2.159：鬼祟必须**排除奇巧贼自己**

用户 2026-10-29 提醒：「让鬼祟判定攻击牌时排除奇巧贼自己，只判定其它 boss」。
v0.2.158 的实现里本来就有 `if (ReferenceEquals(holder.Creature, attacker.Creature)) continue;`。这一版把它做成**双路比对 + 可核对**：
1. 出牌者 Boss 的生物 == 持有者 ⇒ 不触发（等价于原版 `cardPlay.Card.Owner.Creature != Owner`）；
2. **卡牌 owner 的生物** == 持有者 ⇒ 也不触发（换牌 / 生成牌的极端情况下 `attacker` 与卡牌 owner 可能不是同一个对象）。
3. `EnsureGrantedAsync` 里再保险一次：`holder == attacker.Creature` 直接跳过（补偿阶段绝不给自己加格挡）。
4. 加一条**一次性**诊断日志，方便核对这条要求真的生效：
   `鬼祟（<Boss> 自己打出攻击牌 <牌名> ⇒ 不触发（只判定其它 Boss 的攻击牌；原版条件 Card.Owner.Creature != Owner）`

其余同 v0.2.158（只在**其它 Boss** 打出攻击牌时才会给她补格挡，且「原版给了就不补」）。
## v0.2.158：锁定要「尽量均匀」，鬼祟对其它 Boss 的攻击牌不触发
### ① 锁定分配：锚点不搬家 + 孤儿按「被锁份数最少」补位
用户 2026-10-29：「应该让 boss 优先锁定身上『跟踪锁定』数量少的玩家（相同就等概率，但保证 SL 不能随便变），避免出现死了 2 个队友剩下两个人一个被 3 只 boss 锁另一个还是只有 1 个锁的情况」。
v0.2.157 的纯函数在这条上是错的：`目标 = 花名册[rank % 花名册长度]`，死者直接落到「活着的 NetId 最小那位」⇒ 两个队友阵亡后会全压到同一个人身上（就是用户看到的 3:1）。
现在 `PureVictim` 改成**整张表的纯函数**（`ComputeAssignment`）：
1. **锚点**：`花名册[rank % n]`，该玩家活着就锁他 —— **永不搬家**（保证「玩家死亡不引起别人的锁平移」）。
2. **孤儿**（锚点已阵亡的那只 Boss）：按顺序分给**当前被锁份数最少**的活人；
   并列时用「本局种子 + 幕数」算出的固定轮转 `StableRotate` —— 看起来等概率，
   但同一局同一幕永远同一个结果 ⇒ **SL 前后不变，四个客户端也一致**。
3. 整表按帧缓存（`AssignmentOf`），避免每张牌都重算。
结果：4 Boss 对 4 人 ⇒ 1:1:1:1；死 1 人 ⇒ 只有那只是孤儿的 Boss 换人（别人不动）。**剩 2 人只剩 2 只 ⇒ 2:2**（不是 3:1）。
### ② 鬼祟（`SneakyPower`）对「其它 Boss 的攻击牌」不触发

用户 2026-10-29：「玩家打出攻击牌会触发奇巧贼的鬼祟，而其它 boss 打攻击牌不会。」
反编译 `SneakyPower.AfterCardPlayed`：`cardPlay.Card.Owner.Creature != Owner && Type == Attack`
⇒ `GainBlock(Owner, Amount)`。它靠原版 `Hook.AfterCardPlayed` 扇出，而我们 Boss 的牌是 `CardCmd.AutoPlay` 打的、所在生物又**不在 `CombatState.Players` 里** ⇒ 扇出漏掉。
修法（新增 `Patches/SneakyOnBossAttack.cs`，在 `CharBossMonster.PlayOne` 里成对调用）：出牌前后各拍一次「持有鬼祟者（除出牌者本人）的格挡」—— **原版给了（格挡变了）就不动，没给就按 `SneakyPower.Amount` 补一份**，所以不会双份。
日志：`鬼祟（<Boss> 打出攻击牌 <牌名>，但原版没给 <持有者> 格挡（影子玩家不在 CombatState.Players，扇出漏了）⇒ 补 N 点`。
### 复测看点

- 两人存活时，4 只 Boss 应分成 **2:2**（日志 `跟踪锁定分配表` 里能直接看到）；
- 死一个队友时，**只有那只孤儿 Boss** 换人，另外几只不动；
- 其它 Boss 打出攻击牌时，奇巧贼应获得格挡；若日志出现上面那行「补 N 点」，说明确实是原版扇出漏了
  （若一直不出现也拿到了格挡，说明原版其实给了，那就没坏）。
## v0.2.157：「谁被谁锁」必须是各端算得一样的东西 —— 改成纯函数
用户 2026-10-29：「**没变化**。这是第一回合，也没人死。」（在 v0.2.156 之后，被锁的 1 号位看到的伤害仍是 0）。
### 为什么前几版都治不好

`Locks`（Boss→玩家 的映射）是**每个客户端各自维护的本地字典**。而「谁被谁锁」必须每个客户端
算出**同一个**答案，否则就会出现：

- 我这台认为储君锁的是**别人** ⇒ 我的意图「按人清零」显示 **0**。
- 打人的那台（或结算那一侧）认为锁的是**我** ⇒ 真打 **10**。
v0.2.150~156 的分配都**依赖已经攒下的 `Locks` 内容**（`PickLeastLockedAmong` 按当前负荷挑人）。所以各端只要有一点点时序差就会分叉；「第一回合也这样」说明分叉在**开局第一次分配**就发生了
（`combat.Players` 是逐个填满的，各端看到的瞬间状态不同；再加上 `EnsurePower` 在各自时机认领）。v0.2.156 我把判据改成「按实时锁定判定」，但**实时锁在各端本来就不一致**，所以显示仍然是 0。
### 修法：`PureVictim` 纯函数（唯一权威）
```text
花名册 = 本局真人玩家，按 NetId 升序、**包含已阵亡的** ⇒ 顺序不随死亡变化。
rank   = 这只 Boss 在「场上我们的 Boss（按怪物 id 升序）」里的下标
目标   = 花名册[rank % 花名册长度]；若他已阵亡 ⇒ 取「还活着的、NetId 最小」的那位
```

三条性质正好对上用户的三次口径：
1. **各端一致**：只依赖联机同步的数据（NetId、怪物 id），所以「显示打谁」与「结算打谁」永远是同一个答案；
2. **玩家死亡不搬家**：花名册含死人也让别人 rank 不变 ⇒ 4 人 4 Boss 时 A、B、C、D 的一一对应一旦成立，
   3 号位死后**只有 C 换人**（交给活着的 NetId 最小的 = 1 号位），A/B/D 一个不动
   （「应该是 1 号位同时对自己本来对的和 3 号位对的」）。
3. **确定性**：SL 前后、不同客户端都算得出同一结果。
改动点：
- `BossLockOn.PureVictim(boss, combat)` 新增；`TargetFor`（出牌/选靶/碎屑/收窄）、`PeekLock`（意图显示）
  在**多人局**一律走它；`Locks` 降级成**缓存**（只给日志与图标用），由 `Rebalance` 以 4Hz 从纯函数同步。
- `Rebalance` 不再「按当前负荷挑人」（那是分叉的根源），只把纯函数结果写进 `Locks` 并打分配表。
- `ReassignDeadLocks` 同样改成「同步纯函数结果」（它负责立刻搬图标，不等慢车道）。
- 玩家还没到齐（`RunState.Players.Count > 花名册人数`）时不锁 —— 各端读同一个 `RunState`，闸门也一致。
- 日志目标一律带 `NetId`（`DescribeTarget`），便于用同一个 NetId 核对「显示 / 出牌 / 碎屑 / 收窄」。
### 复测看点

- 第一回合就该正常：被储君锁定的那名玩家看到真实伤害（碰撞轨迹 10），其他人看 0。
- 日志 `跟踪锁定：REGENT_BOSS → Silent/NetId=N` 的 N，应该和 `PLAY COLLISION_COURSE … -> …/NetId=N`、「碎屑洗给…」里的 N **完全一致**。
- 四人局里每台客户端的这一行应该**一模一样**。
## v0.2.156：被锁的人看到「0 伤害」，但实际被打 —— 显示与出牌用了两套判据
用户 2026-10-29 截图（储君的「碰撞轨迹」卡的意图）：
> 我操控的玩家（1 号位）被储君锁定，但他的牌显示的伤害是 0（这张牌确实对我打出伤害了），应该是 10。
根因：v0.2.155 只改了**出牌**那一侧：

| | 判据 |
|---|---|
| 出牌 | v0.2.155 起：**出牌瞬间**的 `BossLockOn.TargetFor(boss)` |
| 显示的「按人清零」 | `BossIntentBuilder` 的 `AimsElsewhere(target)` —— 比的是**计划里存的那个 `PlannedPlay.Target`** |

规划发生在玩家回合/她回合开始，之后锁定可能被改派 ⇒ 计划里的 target 是**别人**（于是对本地玩家判成
「打的是别人」→ `RawAttackIntent(0)`），而出牌按实时锁打到了**本地玩家**。「显示 0、实际挨 10」就是这么来的（和 v0.2.155 那个「打错人」是同一个根，方向相反）。
修法：新增只读访问器 `BossLockOn.PeekLock(boss)`（**不产生副作用**，不像 `TargetFor` 会顺手分配）。
`BossIntentBuilder` 的判据换成 `AimedAwayFromLocal(card, target)`：
1. 这只 Boss 此刻**锁着谁** ⇒ 和本地玩家比（与出牌同一个真相）；
2. **还没锁**（开局玩家未到齐等）→ 退回旧法 `AimsElsewhere(target)`，宁可显示真实数字也不要误判成 0。
`RandomAimIsElsewhere(boss)`（闪电球被动那一条用）同样改用 `PeekLock`，避免「没有锁时人人都显示 0」。
### 复测看点

- 被锁定的那名玩家看到**真实伤害**（碰撞轨迹 10），其他人看到 0。
- 与 `PLAY … -> {…}/NetId=N` 的 NetId 一致；
- 若某只 Boss 还没分配锁（开局瞬间），人人看到真实数字（不会全 0）。
## v0.2.155：出牌目标 vs「碎屑给谁」用的不是同一个玩家
用户 2026-10-29：「储君**攻击了被故障机器人锁定的 2 号位**，然后把**碎屑塞给了被他锁定的 1 号位**」。
两条路径取目标的方式不一样：

| 谁要用目标 | 取的时机与来源 |
|---|---|
| 出牌（`CardCmd.AutoPlay(ctx, card, target)`，`CollisionCourse` / `SolarStrike` / `Comet` 都是 `TargetType.AnyEnemy` + `Targeting(cardPlay.Target)`） | **计划里存的那个 `PlannedPlay.Target`**（`PlayOne` 只在它「空 / 已死」时才重算） |
| 碎屑洗给谁（`CometDebrisRedirect`） | **出牌那一刻**的 `BossLockOn.TargetFor(boss)` |

计划是**玩家回合 / 她回合开始**算的，而锁定关系之后可能被改派（`ReassignDeadLocks`、开局那次分配晚于规划…）
⇒ 攻击按旧计划打甲、碎屑按新的锁给乙，同一张牌落到两个不同的人身上。
修法（`CharBossMonster.PlayOne` 开头）：只要这张牌**需要一个目标**
（`AnyEnemy` / `AllEnemies` / `RandomEnemy`，与 `BossPlanner.ChooseTarget` 同一判据），
就**用出牌瞬间的 `TargetFor` 重新取一次**，并打一行 `<牌> 的出牌目标按实时锁定改判：<旧> ⇒ <新>（计划里那份已过期）`。
取不到锁就退回原目标（单人零回归；群攻本来按全体结算，不受影响）。
**顺手修的日志盲区**：以前打 `PLAY … -> {Creature.Name}`，而远端玩家的名字在本机经常解析不出来，三个人一起打印全是 `[unknown]` —— 根本没法核对「谁挨了打、碎屑给了谁」。现在统一成 `描述 = <昵称|?>/<角色类名>/NetId=N`（`CharBossMonster.DescribePlayer`）。
> ⇒ 另外提醒：**四个实例在同一台机器上会写同一个 `mod_log.txt`**，不同客户端（各自的「本机玩家」）
> 的行是交织在一起的。核对「谁打谁/给谁」一定要看 NetId，别只看昵称。
### 复测看点

- 同一张牌：`PLAY <牌> … -> {…}/NetId=N` 里的 NetId 应该与这只 Boss 的锁定对象一致
  （`锁定图标巡检` / `跟踪锁定改派` 行里那只 Boss 与那名玩家的 NetId 能对上）。
- 出现 `出牌目标按实时锁定改判：… ⇒ …` 说明确实抓到过计划过期（这行出现是好事）。
- 碎屑落点与上面那个 NetId 相同。
## v0.2.154：玩家阵亡后的锁定改派 —— 不要再「整表平移」
用户 2026-10-29 实测（3 号位阵亡后）：
> 1 号位的对应 boss 被替换成**之前 3 号位**对应的；2 号位变成**同时持 1 号位之前对应的 boss 和它本来对应的**
> 锁定。按理说应该是 3 号位死后 **1 号位同时被自己本来那只和 3 号位那只**锁，
> 而不是把自己原来那只传递给 2 号位。
根因：`Rebalance`（4Hz）以前是「**用本局种子算一整张表**，对不上的锁统统改写」：

```csharp
int offset = StableOffset(sorted[0], alive.Count);          // ⇒ 依赖**当前活人数**
var want = alive[((i + offset) % alive.Count ...)];          // 4 人 → 3 人 ⇒ offset 变了
if (current != want) Locks[boss.Creature] = want;            // ⇒ 明明没死的锁也被换人
```

只要死一个人，`alive.Count` 4⇒3 ⇒ `offset` 变化 ⇒ **整张表平移**，于是「3 号位的 Boss 跑到 2 号位」。v0.2.150 加的 `ReassignDeadLocks` 补得再好，也会被这张表紧接着改掉。
改法（**唯一分配规则：按当前负荷挑人，且不动已经成立的锁**）：
1. `Rebalance` 只处理「**没有锁** / **锁的人已经死了**」的 Boss：
   `PickLeastLockedAmong(alive)`（被锁次数最少的活人，并列取 NetId 最小）；`currentOk` 的一律 `continue`。
   ⇒ 3 号位死后，3 号位那只 Boss 改派给「当前被锁最少的」1 号位（此时三人各 1 只），
   1、2、4 号位**原来的锁一个字都不动**。
2. `PickForBoss`（`TargetFor` 用的那条路）也改成同一规则，并加一道闸门：
   **`RunState.Players.Count > 活着的人数` 时先不锁**（开局几帧 `combat.Players` 逐个填满，
   否则先来的几只 Boss 会全挑同一个人 —— 那正是当初引入「整表」要解决的问题，现在由闸门 + 负荷分配承担）。
3. 删掉 `StableOffset` / `AliveBossOrder`（按种子算偏移那套已无调用者）。
### 复测看点

- 3 号位阵亡 ⇒ 日志 `跟踪锁定改派：<3号位那只> ⇒ <1号位角色>（还没有锁：…）`，
  **不应**再出现别的 Boss 改派行；分配表里 1、2、4 号位仍是原来那几只。
- 图标：3 号位那只的图标从尸体上消失，出现在 1 号位身上（一 Boss 一份，所以 1 号位身上现在是 2 个）。
## v0.2.153：跟踪锁定改成「一 Boss 一份实例」——像坚韧之环 / 炸弹那样共存

用户 2026-10-29：「跟踪锁定还是替换而不是共存，**它应该像坚韧之环、炸弹那样可以共存**」。
反编译确认了原版那种「共存」是怎么来的（`PowerCmd.FindExistingInstanceForStacking`）：

```csharp
power.InstanceType switch {
    PowerInstanceType.Instanced           => null,                                   // 永远建新实例
    PowerInstanceType.InstancedPerApplier => target.GetPowerInstances(power.Id)
                                                   .FirstOrDefault(p => p.Applier == applier), // 同一施加者合并
    PowerInstanceType.None                => target.GetPower(power.Id),               // 同类型只留一份 ⇒ 我们原来是这样
}
```

「坚韧之环」`ToricToughnessPower`、「炸弹」`TheBombPower` 都是 `Instanced`（多份共存）。我们原来是 `None`，所以**第二只 Boss 锁同一名玩家时，新的那份把旧的顶掉** —— 图标只有一个、悬浮里只剩最新那只 Boss。v0.2.151 让文案列出所有名字只是掩盖，用户要的是**图标共存**。
改动：
1. `Powers/LockOnPower.cs`：`InstanceType => InstancedPerApplier`（**每只 Boss 各一份**，同一只 Boss 重复
   Apply 只合并到它自己那份 ⇒ 不会多出图标）；删掉 `_bosses` 名单与 `SetBosses/AddBoss/RemoveBoss/HasBoss/
   TargetBosses/ClearAll`；`Description` 改成只讲**这份图标代表的那只 Boss**（取 `Applier`，
   取不到才退回注册表第一名）。`StackType` 仍 `Single`（层数恒 1、图标不显示数字）。
2. `BossLockOn.ReconcilePowers` 改成**集合对账**（不再是「每人只留一份」）：
   · `wanted` = 注册表里此刻锁定他的、还活着的 Boss；`byBoss` = 他身上现有实例按**施加者**分组；
   · 不在 `wanted` 里 / 施加者丢失的 / 同一只 Boss 的多余副本 ⇒ 摘掉；
   · `wanted` 里有但没有实例的 ⇒ 补挂一份（`TryApplyLockIcon`，按 (玩家,Boss) 节流）；
   巡检日志改成 `每人：实例数/应有数`。
3. `SweepDuplicateLockPowers` 只摘「同一只 Boss 的副本」（不同 Boss 必须共存）；
   `DropLockIcon` 只摘「这只 Boss 那一份」；`CleanupStaleForNewFight` 摘掉全部（每场重挂）。
4. 记账键从「玩家」改成 (玩家, Boss) 组合。
⇒ **多人同步风险（要知道）**：powers 属于联机状态。2026-10-22 那次掉线的诊断包是 `LOCK_ON_POWER: local 2 / remote 1`（当时用 Counter 把「几只 Boss 盯我」塞进**层数**）。现在层数恒 1，分歧只可能出现在**实例个数**上；而锁定表是按种子算的固定表、各端一致 ⇒ 正常情况应当收敛。**若四人局又报「多人数据不同步」掉线**，回退方法就一行：
`LockOnPower.InstanceType => PowerInstanceType.None`，并把 `ReconcilePowers` 换回「只留一份」（v0.2.152 的版本）。
### 复测看点

- 一名玩家被两只 Boss 锁住 ⇒ 状态栏上**两个**锁定图标，各自悬浮只讲自己那只 Boss。
- 日志 `锁定图标实例 #N 创建：（applier=XXX_BOSS）`，以及 `锁定图标巡检（每名玩家：实例数/应有数）`
  应显示 `2/2` 这种。
- Boss 死掉 / 不再锁他 ⇒ 只摘它那一份，另一个图标留着。
## v0.2.152：「攻势」意图文案改过 —— 回退（用户 2026-10-29 实测截图否掉了）

v0.2.151 把多人局的攻击意图文案从原版「攻击**所有人**」改成「只会落在{Target}身上，造成{Damage}点伤害」。用户实测截图（v0.2.151）：

> 攻势 · 这个敌人的攻击只会落在**另一名玩家**身上，造成**0**点伤害。
两个问题，都不是调文案能救的：
1. **数字是 0** —— 悬浮里的伤害取的是**该客户端实际渲染的那条意图**，而「意图按人清零」
   （`BossIntentBuilder` 的 `aimedElsewhere`）对非被锁者建的就是 `RawAttackIntent(0)`。
   于是「点名 + 0 伤害」比原版那句笼统的「攻击所有人」更糟。
2. **名字取不到** —— 远端玩家的 `Creature.Name` 在本机经常是 `[unknown]`
   （日志里 `PLAY … -> [unknown]（hp=33 side=Player）`），只能退化成本人看不到的「另一名玩家」。
结论：**整块回退**（删 `Patches/BossAttackIntentTipPatch.cs`，删 `content/localization/*/intents.json`
里的 `CHARBOSS_ATTACK*.description` 两个键，重打 PCK）。原版「攻击所有人」这句措辞就留着 —— 用户当初也是「（不能改就算了）」。
> 若以后还想做，正确的做法不是改悬浮文案，而是**让每条意图本身带上「打谁」**：
> 要么给 `RawAttackIntent` 之类加一个 `TargetName` 并让 `GetIntentDescription` 用它，
> 要么在「按人清零」时**不建 0 伤害意图**、改为不显示 —— 但这会改变意图栏的既有语义，得先想清楚。
v0.2.151 里另外两项（跟踪锁定名字并集、闪电球被动按人显示）**保留**，与本次回退无关。
## v0.2.151：跟踪锁定共存 / 闪电球被动的「按人显示」/ 攻势意图文案（③ 已在 v0.2.152 回退）
用户 2026-10-29 第三条反馈（附意图悬浮截图：「攻势 · 这个敌人将要攻击所有人造成 6 点伤害」）。
### ① 跟踪锁定不再被「最新那只 Boss」顶掉（名字要能共存）
`LockOnPower` 的名字 `_bosses` 里只有**当初 Apply 它的那一只**。而一只新 Boss 锁同一名玩家时，它的 `PowerCmd.Apply` 会**建出新的一份** LockOnPower（`InstanceType.None` 只禁止同时存在两份，
重新 Apply 就是「旧的没了、新的刚建」）⇒ 新实例名单里只有它自己 ⇒ 悬浮里只剩最新那只，先前锁他的那只名字被顶掉（用户："跟踪锁定被替换成新 boss 的了，应该让它们能共存"）。
修法：`Description` 里**始终**把 `BossLockOn.BossesLocking(me, combat)`（权威注册表）并进来，不再只在「名单为空」时才补。
### ② 闪电球的**被动**伤害只对被锁定的那名玩家显示

`BossHandPanel.OrbPassiveDamage` 算出的「本回合充能球被动会造成的伤害」以前**每个客户端都显示真实数字**。但球被动是**单体随机**（原版 `NextItem(HittableEnemies)`，而她在出手时那份列表被收窄成被锁定的那位）⇒ 只有他会挨。修法：`BossIntentBuilder.RandomAimIsElsewhere(boss)`（新增 Boss 重载）为真时显示 0，
并打一行 `独立意图（充能球被动）N 点 ⇒ 对本地玩家显示 0`。
**冰雹风暴 / 炸弹是全体伤害，不做这一条**（它们对每个人都该显示真实数字 —— 用户明确指出两者的区别）。
### ③「攻势」意图的悬浮文案改成点名一名玩家
原版 `AbstractIntent.GetIntentDescription` 按 `IsMultiplayer` 变量在 `ATTACK.description` 里选分支，
多人局就写「攻击**所有人**」—— 而我们的 Boss 单体攻击被收窄成只打一名玩家。
新增 `Patches/BossAttackIntentTipPatch`（`AttackIntent.GetIntentDescription` 的 Postfix）：
owner 是我们的 Boss 且多人局时，换成我们自己的本地化键 `CHARBOSS_ATTACK.description` / `CHARBOSS_ATTACK_MULTI.description`。
变量 `{Target}`（本机玩家显示「你」，否则显示对方名字，取不到就「另一名玩家」）、`{Damage}`、`{Repeat}`。
覆盖所有 `AttackIntent` 派生类（含 `RawAttackIntent` / `LiveSingleAttackIntent` 等）；单人局原样返回。
⇒ **这次改了本地化文件 ⇒ 必须重建 PCK**（见下）。`content/localization/{zhs,eng}/intents.json` 加了两个键，
已用 `tools\pck_builder\build_pck.gd` 重打并验证键在包内（`find_in_pck.gd` 搜到 4 条）。
⇒ PCK 12861452 字节已装进游戏。
> 只在改 DLL 时：`dotnet build` + 复制 DLL/清单即可。
> **改了 `content/` 或 `tools/art/pck_extras.txt` 时还必须重打 PCK**：
> ```powershell
> New-Item -ItemType Directory -Force -Path .build | Out-Null
> tools\godot\Godot_v4.5.1-stable_mono_win64_console.exe --headless --path tools\pck_builder `
>   --script build_pck.gd -- (Resolve-Path src\CharacterBosses.json).Path `
>   (Resolve-Path .build).Path\CharacterBosses.pck (Resolve-Path content).Path CharacterBosses `
>   (Resolve-Path tools\art\pck_extras.txt).Path
> ```
> ⇒ 输出路径必须**绝对**（Godot 把相对路径解析到 `--path` 那个项目目录），否则报 `Failed to start PCK packer: 20`。
### 复测看点

- 同一名玩家被两只 Boss 锁住时，悬浮里应列出**两只**名字。
- 非被锁者看到的「充能球被动」那条意图是 **0**（日志 `对本地玩家显示 0`），被锁者看到真实数字；
- 任何「攻势」悬浮都不再说「所有人」，而是「这个敌人的攻击只会落在<某个玩家>身上，造成 N 点伤害」（或 `N×M` 版本）。
## v0.2.150：四人局实测的四个问题（全部有 `mod_log.txt` 实证）
用户 2026-10-29 第二波反馈，四条都能在 11:20~11:26 那次 v0.2.149 的日志里找到对应行。
### ① 奇巧贼第 11 回合「隐秘匕首」丢掉了不该丢的牌（剧本点名两张打击，实际丢了防御 ×2）
日志：`自动选择: 候选 6 个（min=2 max=2）→ 选 [DEFEND_SILENT,DEFEND_SILENT]`
（剧本第 11 回合写的是 `HIDDEN_DAGGERS` + **打击 ×2**）。
根因：`BossCardSelector.PreferDiscard` 是**按牌实例**记的，而她回合开始时
`BossScriptRunner.BuildHand` 会**把整手牌重新造一份** ⇒ 规划期那些实例全部失效，
`PreferDiscard.Contains(c)` 一个都命中不了 ⇒ 退化成「价值→费用」排序，
0 费的**中和**、1 费的**防御**就排在 1 费的打击前面。
修法：`MarkPreferDiscard` 同时按**「牌名 + 是否升级」记份数**（`PreferDiscardByKey`）；
选牌分三层 —— ①实例命中 ⇒ ②同名配额命中 ⇒ ③兜底排序。日志会写 `（点名 N 张按实例 + M 张按牌名 + 其余 K 张兜底）`。
### ② 灾厄骨第 6 回合拿 2 费打出了三张 1 费牌（护卫本来「没费」）

日志：`NECROBINDER_BOSS turn 6: ... energy=2/2` 和 `剧本第 6 回合: 打 [FRIENDSHIP,UNLEASH,BODYGUARD] 余费=0`
（`FRIENDSHIP 回能 +1 ⇒ 本回合能量 2`）。
根因：**友谊（`Friendship`）的 `Energy` 变量不是「立刻回 1 费」**。反编译是 `PowerCmd.Apply<FriendshipPower>(…, DynamicVars.Energy.BaseValue, …)`，而 `FriendshipPower` 只实现 `ModifyMaxEnergy => amount + Amount` —— 它涨的是**每回合能量上限**。
我们的「牌上 `Energy` 变量直接补进 `pcs.Energy`」逻辑把它当成立刻回能 ⇒ 凭空多 1 费 ⇒ 第 3 张（护卫）白打。
修法：`BossScriptPlanner.DeferredEnergyCard` 加上 `FRIENDSHIP`（这个判据同时被规划期和出牌期使用，
两边一起对齐）。⇒ 第 6 回合恢复成「友谊 + 出击，护卫没费」，与剧本注释一致。
> 若用户其实**要**让第 6 回合打得出护卫，改法是把那一回合的 `TurnE(2, …)` 改成 `TurnE(3, …)`（一行），说一声就改。
### ③ 休眠给其它 Boss 的格挡是「份数 × 真人数」
日志（11:25:56~11:26:02）：同一颗球 5 点、2 点格挡**连续 4 次**，每行都「转给其它 Boss [3 只]」。
根因：原版是 `foreach (Player p in CombatState.Players) if (p != Owner) GainBlock(amount)` —— **每名真人各调一次**，4 人局就是 4 次；我们每次进来都把 `amount` 发给**所有**其它 Boss
⇒ 每只 Boss 实际拿到 4 份（4 人局）。修法：只让「这一圈里第一名真人」的那一次负责转发（`FirstPlayerInLoop`），其余几次照样吞掉（真人拿不到，这是设计），**不再转发** ⇒ 每颗球结算，其它 Boss 各拿 1 份。
### ④ 玩家阵亡后，锁没改派给别人
原来 `OnPlayerDied` 只**删**注册表条目不挑新目标；`TargetFor` 只在被问到的时候才自愈，`Rebalance` 的「按种子固定表」又会因为**活着的人数变了**（⇒ `offset` 跟着变）整张表平移，
并不保证「死者那只」一定换人。⇒ 锁可以一直停在死人身上（图标自然也不会跑到别人身上）。
修法：新增 `BossLockOn.ReassignDeadLocks(combat)`（`Rebalance` 开头 + `OnPlayerDied` 里各调一次）：锁着的人不活着 / 不在这一场 ⇒ 立刻按「被锁次数最少的活人」改派、摘掉死者的图标、重建一遍图标，
并打 `跟踪锁定改派：<BOSS> ⇒ <新玩家>（原来锁着 <旧玩家> 已阵亡/离场；存活 N 人）`。
### 复测看点

- 第 11 回合：`自动选择 → 选 [STRIKE_SILENT,STRIKE_SILENT]（点名 0 张按实例 + 2 张按牌名 + 其余 0 张兜底）`
- 灾厄骨第 6 回合：不再有 `FRIENDSHIP 回能 +1`；计划里只剩 2 费
- 休眠：每颗球只出现**一次** `⇒ 转给其它 Boss [...]`
- 有人阵亡：出现 `跟踪锁定改派：…`，且死者的图标消失、新目标立刻有图标
## v0.2.149：翻越撑击（FlickFlack）只打一个人 —— 上个会话的豁免只覆盖「我们自己打出的牌」
用户 2026-10-29：「翻越撑击不是群攻。上个会话怎么搞的，之前播种也不是群攻，它是把哪改坏了吗？」。
先说事实：**翻越撑击就是群攻**。反编译 `MegaCrit.Sts2.Core.Models.Cards.FlickFlack`：
`base(1, CardType.Attack, CardRarity.Common, TargetType.AllEnemies)`、
`DamageVar(7m)`、`CardKeyword.Sly`，卡面文本（官方中文）「对所有敌人造成7点伤害」，
伤害是 `DamageCmd.Attack(7).FromCard(...).TargetingAllOpponents(CombatState)` —— 和**播种 Sow 的写法一模一样**。所以它只打一个人是 bug。
**上个会话没有「改坏」新东西，是它的豁免挂错了层**。
| | 播种（Sow） | 翻越撑击（FlickFlack） |
|---|---|---|
| 剧本里怎么写的 | 主动打出 | `discardInstead: true` = 被「早有准备」**丢掉** |
| 谁把它打出去 | `CharBossMonster` 的 `CardCmd.AutoPlay`（我们自己的代码） | **原版** `CardCmd.<DiscardAndDraw>d__4` 里的 `AutoPlay(…, AutoPlayType.SlyDiscard)` |
| v0.2.141 的豁免（`MultiTargetCardDepth` 只在 `CharBossMonster` 的 AutoPlay 前后加减） | ⇒ 生效 | ⇒ 那一刻 depth=0 ⇒ 仍被收窄 |

⇒ 同一个漏洞还覆盖：爆发/复制药水的重放、瓶中精灵、别的 mod 代打、以后新增的自动打出机制。
**修法**：豁免改挂在**攻击经过的唯一汇合点** `AttackCommand.GetPossibleTargets` 上，
判据从「这张牌是不是我们打出去的」换成「**这次攻击是不是多目标**」：

```csharp
// 原版（反编译）
if (IsSingleTargeted) return new ReadOnlySingleElementList<Creature>(_singleTarget);
if (IsMultiTargeted) { … return _combatState.GetOpponentsOf(Attacker); }   // ⇒ 被我们的收窄改过
```

新增 `Patches.AoeAttackNotNarrowedPatch`（Postfix）：`IsMultiTargeted` 且攻击者是我们的 Boss、且结果**退化成 1 个**对手时，暂停收窄（`SkipNarrowDepth`）重算一次完整对手列表并替换。
日志：`群攻豁免：<BOSS> 的多目标攻击被收窄成 1 个目标 ⇒ 恢复成全体 N 名对手`。
连带核对过的两张（**不受影响，属预期**）：
- 连续反弹 `Ricochet`：`TargetType.RandomEnemy` + `TargetingRandomOpponents` ⇒ 随机单体，
  本来就该只打锁定玩家（`AoeAttackNotNarrowedPatch` 只认 `IsMultiTargeted`，不会误放行）；
- 尖啸 `PiercingWail`：虽然是 `AllEnemies`，但走的是 `HittableEnemies` 逐个 `PowerCmd.Apply`，
  不经过 `GetOpponentsOf` ⇒ 一直就是全体。
v0.2.141 那层 `MultiTargetCardDepth` **保留**（早期一层，无害，且能让收窄日志不刷屏）。新的一层是权威。
⇒ **未实机验证**（本会话游戏起不来）。验证看点：奇巧贼**第 2 回合**和**第 6 回合**
「早有准备」丢掉翻越撑击后，应打出 **N 个受击数字**（N = 活着的真人数），
且日志出现 `群攻豁免：SILENT_SLY_BOSS 的多目标攻击被收窄成 1 个目标 ⇒ 恢复成全体 4 名对手`。
## v0.2.148：锁定图标 —— 方案 B，去渲染层把残留节点收掉（用户拍板）

### 根因（这次是从反编译里读出来的，不再是猜的）
`NPowerContainer`（`docs/plans/_recon/NPowerContainer.txt`）：

```csharp
private void Add(PowerModel power) {          // ⇒ Creature.PowerApplied 触发
    if (power.IsVisible) {                     //    ⇒ 没有任何去重
        var n = NPower.Create(power); _powerNodes.Add(n); AddChildSafely(n);
    }
}
private void Remove(PowerModel power) {       // ⇒ Creature.PowerRemoved 触发
    if (CombatManager.Instance.IsInProgress) { //   ⇒ 战斗不在进行中 ⇒ 整段跳过，列表里那项也不摘
        var n = _powerNodes.FirstOrDefault(x => x.Model == power);   // ⇒ 只摘第一个匹配项
        if (n != null) { _powerNodes.Remove(n); n.QueueFreeSafely(); }
    }
}
```

三处不对称合起来就是「状态栏上出现两个锁定图标」：
1. `Add` 不去重 ⇒ 同一个模型被加两次就是两个节点；
2. `Remove` 在 `IsInProgress == false` 时什么都不做 ⇒ 清单里已经没了、屏幕上还在。
3. `Remove` 按引用只摘第一个 ⇒ 一旦出现两个节点，Power 消失时只回收一个，另一个**永久残留**。
这也解释了 handover §3.2 那条「巡检显示 1 个但屏幕上有 2 个」——多出来的从来不是第二份 Power
（`InstanceType.None` 根本不允许），而是**没被回收的 UI 节点**。
### 改动（新增 `src/Hooks/Patches/PowerIconNodeReconcilePatch.cs`）
1. **`Add` 前置去重**（`PowerIconAddDedupePatch`，Harmony Prefix）：
   只对 `InstanceType.None` 的 Power 生效（同类型不可能有两份活实例，第二个节点必是重复）：
   · 同类型且是**活的**节点 ⇒ 不再新建；
   · 同类型但是**残留**节点（模型已不在 `Powers` 里）⇒ **改绑到新模型**（`NPower.Model = power`），
     不新建 —— 屏幕上从头到尾只有一个，连一帧闪烁都没有。
2. **`Remove` 兜底**（`PowerIconRemoveFallbackPatch`，Postfix）：原版因为 `IsInProgress == false`
   整段跳过时，我们自己把那个节点摘掉。
3. **5Hz 权威对账**（由 `BossFrameTick` 驱动；`Containers` 登记表由 `SetCreature` Postfix 填）：
   节点的 `Model` 必须**引用出现在 `creature.Powers` 里**（唯一真相），且同一份模型只允许一个节点；
   否则摘掉（先 `RemoveChild` 让它这一帧就消失，再 `QueueFree`）并调一次 `UpdatePositions`。
   ⇒ 不碰 `IsVisible` —— 队友身上的图标该不该显示仍由原版 `LocalContext.IsMe` 决定，零回归。
### 同时放宽的那条
`BossLockOn` 的「每名玩家每场最多补挂 2 次」放宽到 **8 次**（`MaxIconAppliesPerPlayer`）：
那个上限本来就是给「残留节点」打的补丁，现在残留会被回收、新图标会复用残留节点 ⇒ 不必再靠「少挂」来掩盖问题；压太低反而会出现「一个图标都没有」。
### 本轮**没能**完成的验证
本会话 `steam://run/2868840` / 直接起 `SlayTheSpire2.exe` **都起不来**（Steam 在跑，游戏进程 60 秒内不出现，
日志目录里没有新 `godot*.log`）⇒ 无法实机确认。**下一步要靠用户 4 开实测**，看这几行：

| 日志行 | 含义 |
|---|---|
| `状态栏图标：<POWER> 已有节点，拦下重复创建` | 原版想建第二个节点，被拦下（去重生效） |
| `状态栏图标：<POWER> 复用残留节点（改绑新模型）` | 用了残留节点的位置，没有新建（屏幕上始终 1 个） |
| `状态栏图标：<POWER> 已被摘除但原版没回收节点` | 战斗不在进行中时 Remove 跳过的那一半被补上。 |
| `状态栏图标对账：回收 N 个残留/重复图标节点（场上 M 个状态栏）` | 5Hz 对账真的摘掉了东西（**反复出现说明还有别的来源在造节点**） |
| `状态栏图标对账（<生物名>）：<POWER>：背后已经没有这个 Power（残留）` | 具体是哪个图标、为什么被摘 |

期望：**每人正好 1 个锁定图标、开局到战斗结束都不变**。
> ⇒ 代码是照着反编译逐行对的（`Add`/`Remove`/`_powerNodes`/`_creature`/`UpdatePositions` 的方法与字段名全部核对过），
> 但没有跑过一次 —— Harmony 目标名若有误，`mod_log.txt` 里会有补丁失败的红字。
## v0.2.147：锁定图标 —— 结论是「游戏自己会把我们挂的图标摘掉」
v0.2.146「只给本机玩家挂」的实测结果：「除了 1 号位有 2 个，其它玩家都没有了」⇒ **图标根本不会跨客户端同步**（我们不给别人挂，别人就真的没有）。所以那条路是错的，已回退。
v0.2.145 的摘除探针抓到了真正的机制（10:38:29 那条链）：
```
锁定图标实例 #1 创建：飞猪（applier=静默猎手）
锁定图标被摘除：飞猪（第 1 次；这次不是我们主动摘的就会没有对应的 `跟踪锁定` 日志）
锁定图标实例 #2/#3/#4 创建：…           ⇒ 又补
锁定图标被摘除：[unknown]（第 2/3 次）。10:38:30  锁定图标实例 #5/#6/#7 创建      ⇒ 又补一遍（屏幕上的第二个就是这么来的）
10:38:47  锁定图标被摘 ×4
```

我们自己的删除点只有三处（没人锁他 / 摘重复层 / Boss 死亡），**三处都会各打日志，而这里一条都没有**
⇒ 是**游戏自己**（power 生命周期 / 多人状态同步）把我们挂的 `LockOnPower` 摘掉的。而摘掉的图标节点不会立刻从状态栏消失，我们再补一份 ⇒ 屏幕上有 2 个。
两处改动：
1. **回退**「只给本机玩家挂」→ 仍然给所有人挂（否则别人一个都没有）。
2. 每名玩家每场**最多挂 2 次**（`_createdCount` 上限 2）：第一次被摘补一份，再被摘就不补
   （宁可少一个图标，也不要屏幕上出现两个）。
   另外 `CleanupStaleForNewFight` **不再**清这份记录 —— 它每只 Boss 生成时都跑一次
   （⇒ 4 只 Boss 同一秒跑 4 次），v0.2.145 的 `#5~#7` 就是被它把「已挂过」清掉才冒出来的。
⇒ 若仍然不对，建议直接**去掉这个图标**（锁定关系不依赖它，`BossLockOn.Locks` 才是真相），
改成在 Boss 自己的面板上显示「锁定：XXX」。等用户拍板。
## v0.2.146：双锁定图标（只给本机挂）—— 已证明方向错误
用户 2026-10-29：「现在战斗一开始 1、3、4 就有两个跟踪锁定了」—— 连开局都不需要等，说明不是「中途消失又补一份」，而是**一开始就挂了两份**。
根因：`PowerCmd.Apply` 走战斗动作队列，而**远端玩家的生物由他自己那台客户端权威结算**：我们在本地替远端玩家 Apply 一次、他自己那台客户端也会 Apply 一次 ⇒ 那名玩家身上 2 个图标。本机玩家永远只有 1 个（只有本机权威）—— 正好对上「1/3/4 有 2 个、本机那个只有 1 个」。
修法（和原版行为一致）：`ReconcilePowers` 里用游戏自己的判断 `MegaCrit.Sts2.Core.Context.LocalContext.GetMe(combat)` 拿到「本机是谁」，
**只对账本机玩家**，别人的图标交给状态同步。拿不到 `me` 时退回旧行为（对所有人对账），
免得一个图标都不挂。
顺带保留：`_createdOnce`（本场每人只建一次）+ `LockOnPowerRemoveProbe`（摘除探针）。
⇒ 待复测：开局 4 人应该 1 个图标，且**每个人的悬浮文字里都列出真正锁他的那几只 Boss**。
## v0.2.145：锁定图标「多出一个」的日志证据

用户 2026-10-29 录屏（`屏幕录制 2026-10-29 103202.mp4`）：打「播种」时 1/3/4 号位各多出一个跟踪锁定。v0.2.144 的诊断日志抓到：

```
10:31:37 锁定图标实例 #1 创建：飞猪（applier=静默猎手）     ⇒ 开局时 4 人各 1 个
10:31:37 锁定图标实例 #2/#3/#4 创建：…
10:31:37 巡检：Silent=0 | Silent=1 | Ironclad=0 | Ironclad=0
10:31:48 巡检：Silent=1 | Silent=1 | Ironclad=1 | Ironclad=1  ⇒ 每人 1 个，正常
10:31:48 锁定图标实例 #5/#6/#7 创建：…                       ⇒ 又给 3 个人各建 1 个 ⇒ 屏幕上有 2 个
```

即：这 3 个图标在 11 秒后**要么被摘掉了、要么本机 `Powers` 里看不到**。于是对账逻辑认为「他没有图标」又补了一份 —— 玩家看到的就是「本来有一个，又加了一个」。
两处改动：
1. **本场每名玩家只允许创建一次**（`_createdOnce`，新战斗 `Reset()` /
   `CleanupStaleForNewFight` 时清空）。看不到图标 ⇒ 认定是同步时序延迟，
   **不再补建**。这样从机制上不可能再出现第二个图标。
2. 新增 `Patches/LockOnPowerRemoveProbe.cs`：`Creature.RemovePowerInternal` 上挂 postfix：
   只要摘掉的是 `LockOnPower` 就打 `锁定图标被摘除：<玩家>（第 N 次）`。
   我们自己的三个删除点都会各打自己的日志，所以**这行出现而没有对应日志 = 游戏自己清的**。
⇒ 待复测：若图标变成「消失且不再补」，说明真的是被别处摘掉了，看探针那行即可定位。
## v0.2.143：休眠不给其它 Boss 格挡

原版 `FrostOrb.Passive / Evoke` 里那句分发是：
```csharp
if (Owner.Creature.HasPower<HibernatePower>())
    foreach (Player p in CombatState.Players)
        if (p != Owner) await CreatureCmd.GainBlock(p.Creature, PassiveVal/EvokeVal, Unpowered, null);
```
而**我们 Boss 的影子 Player 被故意排除在 `CombatState.Players` 之外**
（`BossHookBridge` 类注释第 21 行：「影子 Player 不在 `CombatState.Players` 里 —— 我们故意把它排除，避免它参与玩家回合」）。所以这圈循环**只到真人玩家**，其它 Boss 一个都拿不到。`HibernateFrostBlockPatch` 以前只做「拦掉发真人玩家那一笔」，注释里「其它 Boss 本来就能拿到」是错的 —— 于是格挡既没给玩家、也没给 Boss，凭空消失。
现在拦下之后**主动转给其它 Boss**：`OtherBosses(combat, 施法者的 Creature)` 取场上除施法者外
所有活着的 `CharBossMonster`，`GrantToOthersAsync` 逐个 `GainBlock(同样数值, Unpowered)`。
日志：`休眠：冰球格挡 2 不发给真人玩家飞猪 ⇒ 转给其它 Boss [SILENT_BOSS,REGENT_BOSS,…]`。
## v0.2.142：四人局第五次实测（站位偏左、奇巧弃牌、双锁定图标仍未修好）
1. **播种已修好**（v0.2.141 的 `MultiTargetCardDepth` 生效），本次没再报。
2. **站位偏左**（用户截图：右边一大片空着）—— v0.2.141 把右侧夹到 `视口宽-310`：
   1920 的视口只用到 1610，而且 vanilha 的 4 只挤在 1157..1460（跨度才 303），
   我们的 `minX-150 / maxX+150` 因此也没撑开（最大 span 只有 603）。
   现在**不再以原版位置为基准**：最右那只贴右缘（`视口宽 - 0.75*SpreadX`），
   相邻中心距固定 `1.5*SpreadX`（≈300 = 一个意图面板宽），最左那只不越过 `0.17*视口宽`（玩家区）。
   1920 视口下四只落在 x≈870 / 1170 / 1470 / 1770。
3. **奇巧贼第 2 回合「翻越撑击」没被「早有准备」丢掉** ——
   自动选择器（替 Boss 回答「弃 N 张」）按「价值→费用」挑，把 0 费防御挑走了。
   日志：`自动选择: 候选 6 个（min=2 max=2）→ 选 [DEFEND_SILENT,SNEAKY]`。
   奇巧（`CardKeyword.Sly` / `IsSlyThisTurn`）牌被丢弃会**自动打出** ⇒ 丢弃它们白赚一次，
   现在排序里插入一档「奇巧优先」，优先级仅次于剧本点名。
4. **1/3/4 号位仍是 2 个锁定图标** —— 两个独立原因，都修了：
   · **分配撞车**：`PickForBoss` 让每只 Boss 各自算 `(rank + offset) % 玩家数`，
     而 `combat.Players` 在开场几帧里是**逐个填满**的 ⇒ 先算的用旧分母、后算的用新分母：
     NECROBINDER 与 REGENT 都算到了 Ironclad（日志三条 `校准` 里两条指向 Ironclad，
     DEFECT_BOSS 一条都没有）。改成 `Rebalance` **一次算完整张表**
     （Boss 按怪物 id 排、玩家按 NetId 排、`bosses[i] ⇒ players[(i+offset)%n]`），
     天然双射，并打一张 `跟踪锁定分配表`。
   · **重复实例**：`PowerCmd.Apply` 返回时那份 Power 还没进 `creature.Powers`，
     v0.2.141 的 `_applying` 在 Apply 返回时就释放了 ⇒ 下次对账仍看到 0 ⇒ 再挂一份。
     改成 `Dictionary<玩家hash, 发起帧号>`，**600 帧内只允许发起一次**，看到图标才释放，
     失败立刻释放重试。
⇒ 待复测：4 只站位是否更靠右且不再压意图；`跟踪锁定分配表` 是否 4 人各 1 只；每人 1 个图标。
## v0.2.141：四人局第四次实测（播种、双锁定图标、站位次序、太挤）

1. **播种依旧不群攻（死亡使者正常）—— 真凶不是群蛇形态**：
   反编译 `Sow.OnPlay`：`DamageCmd.Attack(8).FromCard(sow, cardPlay).TargetingAllOpponents(combat)`，
   而 `AttackCommand.GetPossibleTargets()` 在**多目标**时返回的就是
   `_combatState.GetOpponentsOf(Attacker)` —— **群攻牌的打击列表和随机单体牌同源**。
   我们在 `GetOpponentsOf` 的 postfix 里做的「只留锁定玩家」收窄（`ShadowPetNotAnOpponentPatch`），
   于是必然把群攻变单体（日志里那两条 `选靶收窄：NECROBINDER_BOSS …（原来 2 → 1）`
   就是它，v0.2.140 里日志把数量打成「原来 1 项」是打印位置错了，已修）。
   **修法**：新增 `HittableEnemiesSidePatch.MultiTargetCardDepth`，
   `CharBossMonster` 在 `CardCmd.AutoPlay` 前后对 `TargetType.AllEnemies / AllAllies` 成对加减，
   收窄在 `depth > 0` 直接返回。群攻牌整段豁免，随机单体牌照旧跟着锁定走。
2. **1/3/4 号位有 2 个跟踪锁定** —— 根因是「每只 Boss 各自 `PowerCmd.Apply`」：
   Apply 走动作队列，两只 Boss 同一帧进来**都判定「身上还没有」** ⇒ 各建一份，
   而 Apply 返回时那份还没进 `creature.Powers`，所以「Apply 完立刻查重复」也漏。
   现在**只有唯一入口** `BossLockOn.ReconcilePowers`：按注册表（`IsLocked`）
   把每名玩家的**一份**图标内容整体重建（`LockOnPower.SetBosses`），多余的实例直接摘掉；
   `Rebalance` 每 15 帧对账一次，收敛到「每人一个图标、名字含真正锁他的那几只」。
   删掉了 `ApplyLockPowerAsync` / `RemoveExtraLockPowers` 这两个会互相打架的旧路径。
3. **站位顺序和行动顺序不一致** —— 之前按「角色固定表（猎0→机1→骨2→储3→战4）」排，
   而真正的出手顺序是 `CombatState.Enemies` 的下标。改成**直接用 Enemies 下标**，
   站位顺序从此恒等于行动顺序（新加 Boss 也不用维护表）。日志里加了
   `行动次序 0:XXX 1:YYY …` 和 `视口宽`，对表用。
4. **4 只不再外扩太多** —— 恢复外扩（两侧各 `bossSpreadX*0.75`），
   但**夹到视口内**（右侧留 `bossSpreadX*1.55` + 110 的面板余量），
   再以中线为轴兜底撑到至少 3 个间距。这样既不会像 0.2.139 那样把 x 顶出屏（2110），
   也不会像 0.2.140 那样挤在 803 像素里互相压意图面板。
⇒ 待复测：4 只站位左右顺序 = 出手顺序；播种打出 4 个受击数字；每人只有 1 个锁定图标。
## v0.2.140：四人局第三次实测
· 群蛇形态的收窄从「作用域 Push/Pop」改成**一次性** `RequestNarrowOnce`（两帧窗口），
  出牌前 `ResetNarrowing()` 清残留 —— 这条不是播种的真凶（见 v0.2.141），
  但它确实存在跨牌串台的隐患，保留。
· 每帧回调加同帧去重（`Engine.GetFramesDrawn()`），3 个驱动不再各跑一遍。
## v0.2.139：黑球机第 1 回合 + 群攻诊断 + 锁定清扫

· `BossTurn.ForceZeroEnergy`：脚本里 `Energy = 0` 以前等于「不覆盖」（规划器仍按 2 费算），
  所以休眠被错误地预告成「打出」；黑球机第 1 回合改成强制 0 费。
· 群攻牌打出时打一行 `群攻：<ID>：HittableEnemies 解析出 N 个目标` 诊断。
· `BossLockOn.SweepDuplicateLockPowers` 每回合清扫多余锁定层。
## v0.2.137：站位次序 + 锁定图标重复（用户 2026-10-29 四人局第二次实测）

4 只 Boss 确实都出来了（v0.2.136 修好），剩两件事：
1. **猎人没在最左** —— `BossEnemySpreadPatch` 原来按「谁想站左」分组、组内次序是
   亡灵 0 / 静默 1 / 铁甲 2 / 故障 3 / 储君 4 ⇒ 二层最左是**亡灵契约师**。
   改成按**幕**的固定次序（与 `BossLineups` 文档口径一致）：
   · 一层：猎→骨→战→储→机；
   · 二、三层：**猎→骨→机→战→储**（猎人最左，易伤战/巨石战永远最右）。
   落位仍按序号轮流：左上/下偏左/上偏右/右下。
2. **两个玩家各有 2 个跟踪锁定（描述完全一样）** —— 并发竞态：
   `EnsurePower` 里是 `_ = ApplyLockPowerAsync(...)` 不 await，多只 Boss 同一帧都进来，
   都看到「身上还没有」⇒ 各 Apply 一份。现在**Apply 之后再查一次重复**
   （`RemoveExtraLockPowers`，以本次 Apply 拿到的那份为准），把多余的 `PowerCmd.Remove`。
⇒ 待复测：四人局应是 1(左上)/2(下偏左)/3(上偏右)/4(右下)，每人**恰好 1 个**锁定图标、悬浮里有对应 Boss 名；日志应出现 `身上有多余的锁定层（并发/历史遗留）⇒ 摘掉` 说明去重生效过。
## v0.2.136：四人联机的两个问题（用户 2026-10-29 本地 4 开实测）
用户截图：四人局只看到**3 只 Boss**；玩家身上的「跟踪锁定」有的**两个**（且悬浮没有 Boss 名）、有的**没有**。
1. **只出 3 只** —— 根因在 `BossLineups.PlayersPerFight()`：`Math.Clamp(PlayerCount(), 1, 3)`
   （注释写着「4 人暂不支持，按 3 处理」）。改成夹到 **4**。
   候补数量够：一层、二层各 5 只、三层 8 只；「同角色不重复 / 两场不重复」这些约束会自动让步。
2. **锁定图标两个 / 没名字** ——
   · 去重：`ApplyLockPowerAsync` 发现同一名玩家身上有**多份** `LockOnPower` 时，
     只保留第一份，其余 `PowerCmd.Remove`（历史上多次 Apply 的遗留；`StackType.Single` 只保证合并、
     不保证不重复创建）；
   · 补名字：`LockOnPower.Description` 以前只从自己那份「谁挂过我」名单取名字，名单为空就退化成
     「一只角色 Boss」。现在名单为空时**按权威注册表算**：新增
     `BossLockOn.BossesLocking(player, combat)`（用 `IsLocked` 逐只 Boss 判定）。
3. 「有人没有锁定」本来就是**分配结果**（5 只 Boss / 4 名玩家 ⇒ 必有 1 人没被盯），
   出满 4 只之后按 round-robin 应当 1:1。
⇒ 待复测：四人局是否 4 只都在、站位是否是 左上/下偏左/上偏右/右下、每人是否各 1 个锁定且悬浮有名字。
日志行：`[CharBoss] 多人(4 人)：这一幕同时上 4 只 Boss …`、`[CharBoss] 跟踪锁定：<BOSS> ⇒ <玩家>`、`[CharBoss] 4 只 Boss 错位（4 只，左上/下偏左/上偏右/右下，间距 …）`。
## v0.2.135：四人联机站位（用户 2026-10-29）
`BossEnemySpreadPatch` 原来只写了「2 只（左上/右下）和 3 只（左上/中下/右上，`i%3`）」两种，现在按人数分档，**4 只排成平行四边形**（用户原话：左上、下偏左、上偏右、右下）。

| 人数 | 站位（按排序后的序号） |
|---|---|
| 2 | 左上 / 右下（原样，零回归） |
| 3 | 左上 / 中下 / 右上（原样） |
| 4+ | **左上 / 下偏左 / 上偏右 / 右下**（上排=左上·上偏右，下排=下偏左·右下） |

X 的取法：最左 = `minX - SpreadX*0.75`、中间两只 = `centerX ± MidX`、最右 = `maxX + SpreadX*0.75`。
Y：上排 `baseY - SpreadY*0.65`、下排 `baseY + SpreadY*0.65`。
**间距全部可热调**（`mods\CharacterBosses\charbosses_ui.txt`，改完下一场生效）：`bossSpreadX`=200、`bossSpreadY`=140、`boss4MidX`=110（4 只时中间两只离中线多远）。
⇒ 本地无法开 4 人局 —— 日志会打 `4 只 Boss 错位（4 只，左上/下偏左/上偏右/右下，间距 200×140）：…→(x,y)`，照那行坐标调 `charbosses_ui.txt` 即可，不用重编译。
## v0.2.134：补三只缺失/错误的藏品（用户 2026-10-29）
18 个变体逐个核对 `DownfallCollectibleCompat.LootTable`（按「角色 + 幕」索引）后发现：
- **放血战**（二层铁甲）**完全没有**藏品 —— 表里没有这一格；
- **巨石战**（三层铁甲）掉的是**易伤战的主宰**、**辉星储**（三层储君）掉的是**无色储君的武器库** ——
  因为同角色同幕的两套设计撞在同一格。按用户指定补上（都是原版牌，`ResolveLoot` 里按**房间遭遇**区分变体，控制台 `fight <id>` 也判得对）：
放血战 ⇒ **绯红披风** `CrimsonMantle`；巨石战 ⇒ **原始力量** `PrimalForce`；辉星储 ⇒ **虚空形态** `VoidForm`。
## v0.2.133：修「加载模组时 ReflectionTypeLoadException」+ 配置文件改名（用户 2026-10-29 反馈）
用户另一台机器（E: 盘）截图：模组列表里 CharacterBosses 红字报错
`System.Reflection.ReflectionTypeLoadException`。`godot (3).log` 里直接写明：

```
[INFO] New sorting order: 0: CharacterBosses  1: BaseLib     ⇒ 我们排在 BaseLib 前面
[ERROR] Could not load file or assembly 'BaseLib, Version=3.4.7.0'
```

根因：清单 `"dependencies": []` 是空的 ⇒ 排序把 BaseLib 排到我们后面；而 `ModManager.TryLoadMod` 用 `Assembly.GetTypes()` 枚举我们的类型，`BossConfigModConfig`
继承 BaseLib 的 `SimpleModConfig` ⇒ BaseLib 还没加载 ⇒ 整个类型枚举抛异常。
修复：
1. `src/CharacterBosses.json` 里 `"dependencies": [ { "id": "BaseLib" } ]`
   （`ModDependency` 的字段是 `id` + `min_version`）。
   ⇒ **代价：现在硬依赖 BaseLib**，没装会加载失败 —— 已在安装说明里写明。
2. 配置文件改名 `charbosses_config.json` → **`charbosses_config.cfg`**：
   游戏扫描 mod 目录会把**每个** `*.json` 当清单反序列化，我们这份不是清单 ⇒
   每次启动刷一条 `Caught JsonException ... deserialize mod manifest json`。
3. 旧文件自动迁移：`NMainMenu._Ready` 里调 `BossConfig.EnsureLoaded()`（新增）：
   读到旧的 `.json` 就写成 `.cfg` 并删掉旧的（懒加载的话旧文件会一直躺在那里继续被扫描到）。
   **实测**：迁移日志 `配置：已从旧文件 charbosses_config.json 迁移到 charbosses_config.cfg`，
   重启后 `godot.log` 里我们的 ERROR 归零（只剩 BaseLib 自己那两条 Collector 的）。
安装说明已重写开头（BaseLib 依赖、配置改名、BGM 对照表、多人修复等）。坏掉的 `CharacterBosses-v0.2.130.zip` 已删除。
## v0.2.130：碎屑储 BGM 换成瀑布巨兽（用户 2026-10-28 定稿）
碎屑储原本是墨影幻灵 +「随回合切段 / 彗星对重音」，但那套靠的是 `vantom_progress` 参数 —— 和沙虫那档一样**实测完全无效**。用户口径：**换成瀑布巨兽**（`event:/music/act1_b_boss_waterfall_giant`）。⇒ `vantom_progress` 相关代码（`OnBossCardPlayed` 彗星重音、Vantom 常量、`ContainsCard`）**已整块删除**。
当前曲目表（`BossBgm.FmodByEncounter`）：

| 我们的 Boss | 曲目 |
|---|---|
| 愤怒战 | 同族小队 `act1_boss_the_kin` |
| 碎屑储 / 黑球机 | 瀑布巨兽 `act1_b_boss_waterfall_giant` |
| 灾厄骨 | 灵魂异鱼 `act1_b_boss_soul_fysh` |
| 二层全部（涂毒贼/灵魂骨/铸剑师/物理机/放血战） | 帝皇蟹 `act2_boss_kaiser_crab` |
| 炼药贼 / 易伤战 | 女王 `act3_boss_queen`（`queen_progress` 调用保留，但**同样听不出效果** —— 用户「先这样吧」） |
| 其余全部 | 永世沙漏 = `act3_boss_queen` |
## v0.2.129：二层音乐全部用帝皇蟹（用户 2026-10-28 定稿）
沙虫那档「按玩家血量变激昂」**技术上走不通**，实测证据（三场）：
- 血 18.5% ⇒ `insatiable_progress=8`，`UpdateMusicParameter` 返回成功，音乐毫无变化；
- 反射 `_proxy` 调 `update_global_parameter` 交替 0/10，`已发送 True`，依然毫无变化；
- 同一次战斗里 `PlayCustomMusic` 换成「知识恶魔」**立刻能听出来** ⇒ 只有换曲目有效。
原因：`STS2_Hive_Boss_Insaitable_v2_Layer1~5` 是**同一事件内部的 5 个音频层**（bank 实证），
原版也只在沙虫死亡时写一次 10（那条很可能本来就没接线）。
⇒ 按用户口径：**二层所有 Boss（涂毒贼/灵魂骨/铸剑师/物理机/放血战）统一用帝皇蟹**
`event:/music/act2_boss_kaiser_crab`，一首到底；沙虫分段、A/B 实验、血量读取代码**全部删除**
（`CharBossMonster.LowestPlayerHpFraction` 也删了，`BossBgm.OnBossTurn` 不再收血量参数）。调试开关 `charbosses_bgm_alt.txt` 已从 mods 目录移除。
⇒ 女王 `queen_progress` / 墨影幻灵 `vantom_progress` 的调用仍保留（和原版写法一致），但**同样不起作用**，别再基于它们设计新效果。
## v0.2.122：配置界面用**原版勾选框**

用户发来原版勾选框截图，要求「替不替换」改成能打勾的框。现在地图行和 A3 都是那种框（截图见 `.recon/baselib-final2.png`）。三个坑（都写进 `.dshwolf/buglog.json` 与 cerebrum）：
1. `new NTickbox()` / `NConfigTickbox()` **没有视觉子节点**（勾选框的图来自 `res://scenes/ui/tickbox.tscn`），
   `ConnectSignals()` 空引用被 try 吞掉 ⇒ 一个像素都不画；子类 `_Ready` 里也**不能**调 `base._Ready()`
   （原版直接抛 `Don't call base._Ready()! Call ConnectSignals() instead.`）。
2. BaseLib 的 `NConfigOptionRow` 是 MarginContainer，**控件靠右**，这个窗口尺寸下控件落在
   `x=610..1694`（实测日志）× 显示缩放 ⇒ 跑到屏幕外，根本点不到。
3. `ModConfig.CreateRawLabelControl` 的标签**高度 0、最小宽度 1**，不给 ExpandFill/最小尺寸就整行不画。
最终实现（`src/Hooks/Config/BossConfigModConfig.cs`）：自己拼 `HBoxContainer` 行（**控件在左、文本 ExpandFill**），
勾选框 = 普通 `Button`（`Flat`+`ToggleMode`）切换游戏自己的贴图
`res://images/atlases/ui_atlas.sprites/checkbox_ticked.tres` / `checkbox_unticked.tres`。A3 三选一同样用勾选框（点一个自动取消别的）。
**未验证**：点击（合成鼠标事件进不去游戏窗口，键盘能进、鼠标不行）—— 需要人点一下确认。

## v0.2.121：配置界面改用 BaseLib（用户 2026-10-28 第二次要求）

用户反馈自绘面板「和原版不契合」，并要求「用 BaseLib 做一个」⇒ **自绘面板 + F9 热键已删除**。
- 入口在**主菜单 →「模组配置」→ CharacterBosses**（BaseLib 注入的子菜单）。
- 代码：`src/Hooks/Config/BossConfigModConfig.cs`（`SimpleModConfig` + `SetupConfigUI`）、
  `BossConfigBaseLibBootstrap.cs`（注册 + 可选依赖守卫）、`Patches/BossConfigRegisterPatch.cs`
  （挂 `NMainMenu._Ready`；只挂 `NGame._Ready` 实测不触发——补丁生效时那次 _Ready 已经在跑了）。
- **BaseLib 是可选依赖**：csproj `Private=false` + 运行期查程序集再碰它的类型（`NoInlining` 隔开）。
  没装 BaseLib ⇒ 界面不可用，但 `charbosses_config.json` 手改照旧生效。
- **修「地图开关关掉后开不回来」**：旧面板把构建时的开关值捕进了点击闭包 ⇒ 第二次点击传的还是旧值。
  现在每次点击都现查 `BossConfig.IsMapEnabledById` 再取反。
- **已知外观限制（不是我们的 bug）**：706×1066 窗口下 BaseLib 配置面板右侧会被裁掉
  （拿崩坠自己的页签对照过，一模一样；底部有横向滚动条）。所以「开/关」写在**左边文字**里
  （`密林（OVERGROWTH·原版）已替换`），右边那颗按钮只是操作入口。
- 调试开关：DLL 同目录放 `charbosses_open_config.txt` ⇒ 主菜单出现 2 秒后自动打开配置子菜单
  （自动化验证用，正常不需要；已从 mods 目录删掉）。
## v0.2.120 追加：意图上多出来的「×1」（保留）
根除骨的**戳击**显示 `6×1`；刀贼开了**幻影刀刃**那回合的**隐秘匕首**显示 `16点×1`。
根因同一个 —— 先算段数、再**无条件**建多段意图。修法：`Core/AttackIntent.Multi() / .Raw()`
作为「多段 vs 单段」唯一出口（段数 ≤1 退回单段），fromOsty / X 费 / 幻影刀刃余数 /
终结技（`SilentBlades` + `BladesGrowth`）、狡诈药水 全部换成它。


## 本轮做完（v0.2.119）
### BGM（`docs/plans/HANDOVER-bgm-and-config.md` 第 3.3 节）
1. **曲目名全部查清并接线**：事件名直接抄原版遭遇的 `CustomBgm`（不是挖 bank 挖出来的）：
   `act1_boss_the_kin` / `act1_boss_vantom` / `act1_b_boss_waterfall_giant` / `act1_b_boss_soul_fysh`
   / `act2_boss_kaiser_crab` / `act2_boss_the_insatiable` / `act3_boss_queen`。
   「其余全部 ⇒ 永世沙漏」= `act3_boss_queen`（原版 `AeonglassBoss.CustomBgm` 就是它）。
2. **没有 doormaker**：三层 Boss 是 Queen / TestSubject / Aeonglass，永恒沙漏 Aeonglass 复用女王那首。
3. **乐加维林族母没有专属曲目**（唯一不覆盖 `CustomBgm` 的原版 Boss）⇒ 奇巧贼也「不接管」。
4. **一首 ogg 已改为仅 A3 选项②启用**（默认不再替换那三只故障机器人）。
5. **幕次守卫**：跨幕播事件，`stop_music` 之后播不出来 = 整场没音乐，所以每条曲目都带幕号，幕不对就不接管。
6. **分段**：沙虫 `insatiable_progress = (1 - 最低玩家HP%) × 10`（10 层 ⇒ 每 20%）；
   女王 `queen_progress` 1（易伤战第 1 场）/ 2（炼药贼触发瓶中精灵后）；
   碎屑储 `vantom_progress` 1（普通回合）/ 3（有彗星的回合）+ 彗星落地那一击 = 重音，照抄「肢解」）。
### 配置界面（第 3.7 节 A1/A2/A3）
- 存盘：`mods\CharacterBosses\charbosses_config.json`（首次加载自动生成，四个原版地图默认全开）。
- 界面在**用 BaseLib** 的主菜单 →「模组配置」→ CharacterBosses（详见本文最前面的 v0.2.121 一节；
  自绘面板和 F9 已删除）。
- A1 动态列出 `ModelDb.ActsByIndex` 里**当前所有**地图；A2 锁定常开；A3 三选一（③锁定）。
- **已实测**：注册成功（日志 `已把配置注册到 BaseLib`）、界面构建无异常、中文正常、
  行内状态文字随配置变化；键盘/鼠标点击**没有**在自动化里验证成功（合成鼠标事件进不去游戏窗口）。
## 🚀 Next phase

> ### ⏳ v0.2.528：配置界面补上**华丽猎的能力面板**（`BossInfoTable`）
>
> `BossKeys.RosterFor` 里早就有 `SILENT_GRAND_FINALE`，但 `BossInfoTable` 没登记它 ⇒
> 鼠标停在配置界面那个名字上什么都没有。已按用户原文补上：
> `循环：8～10 / 单体能力：极强 / 群攻能力：极强 / 单体高压回合：2、3、8、9 / 群体高压回合：9 / 特殊效果类型：全局`
> （「全局」= 特殊效果「压制」给**所有玩家**塞虚空）。
> 查看位置：主菜单 →「模组配置」→ CharacterBosses → 三层那张表里「华丽猎」那一格，**鼠标停上去**。
> ⚠ 这套字段表是硬编码在 `src/Hooks/Core/BossInfoTable.cs`（DLL 里），不走 pck/本地化。

> ### ⏳ v0.2.527：华丽猎「每帧重算盖掉规划值」三条（用户第二轮截图 + 日志定位）
>
> **排查口诀（已进 cerebrum）**：规划日志对、面板不对 ⇒ 一定是 `BladesGrowth.RecomputeAll`（无球）
> 或 `CharBossMonster.RefreshPlannedIntents`（有球）这条**每帧重算**路少传了参数。
> 凡是牌面上没有、只能由剧本算出来的意图数值（`shivOverride`/`extraHits`/`discardsThisTurn`/`simBlock`…），
> **三条路都要传**。
>
> 1. **钢铁风暴只有「强化」蓝箭头** → 两条重算路都没传 `shivOverride` ⇒ `ShivsFrom(STORM_OF_STEEL)`=0；两条路已补。
> 2. **第 9 回合铭记死亡低 4** → `BladesGrowth.discardsSoFar` 从 0 起算，漏了「必备工具回合开始丢的 1 张」；已改成 `HasToolsOfTrade ? 1 : 0`。
> 3. **第 5 回合只打出 1 张打击** → `TakeToolsDiscard` 只按牌名挑第一张、把**计划要打的那张**丢了，出牌循环「不在手牌」静默跳过；已改成优先挑不在 `boss.Plan` 里的那张。
>
> v0.2.525 已**实机验证通过**：弹珠袋只剩 1 层易伤、谋杀 20/42/63/84 逐回合吻合、
> 选牌分桶把 4 处弃牌全对上（夜魇→杂技、杂技→战术大师、隐秘匕首→2 防御、手上技法→防御）、
> 第 6 回合 `打 [PIERCING_WAIL,STRIKE,SURVIVOR]`。

> ### ⏳ v0.2.526：**多人不同步**（玩家上报 `mod_log (25).txt`）
>
> 一句话：**阵容里出现「同一只 Boss 两次」**（实测 `[DEFECT_RAINBOW,DEFECT_RAINBOW]`、`[SILENT_SHIV,SILENT_SHIV]`、
> 11-07 的 `[NECROBINDER_SOUL,NECROBINDER_SOUL]`）⇒ `PartnersForWithKeys` 跳过全部同 Key 条目
> ⇒ **2 人局只出 1 只 Boss、一名玩家没有锁定图标**；而「阵容 = 玩家数」这件事正是上一轮
> （`desync-2026-11-08-modlog23.md`，房主 2 只/客人 1 只）那种轮次 1 分叉的形态。
> 对照很干净：**阵容不重复的场次（一层两场）完全没不同步**，重复的场次全中（本日志 3/3、11-08 那轮 4/4）。
>
> 根因（代码走查）：`BossLineups.Build` ⑥ 读 `SyncedMainCharacter`（= `act.BossEncounter` /
> `act.SecondBossEncounter`，**客人不写、房主写、getter 还会按当前版本重算**）来判「主 Boss 的角色」，
> 判错 ⇒ 搭子没跳过主角色 ⇒ 排出两只同款。**这是阵容里唯一依赖本机/同步时机的地方。**
>
> 改动（已在游戏目录）：阵容改成**只由（种子,幕,场）决定** + 同 Key 硬保险；`PartnersForWithKeys`
> 按「只数=玩家数」补齐并夹上限；`FindFightOf` 采用同步槽位前要求「那场真的有我」；
> 不同步摘要新增 `多人阵容：玩家=N 阵容指纹[…] 场上我方 Boss=M 只（两端这一行必须一致）`
> ⇒ 以后**一份日志**就能判断是阵容分叉还是生成分叉。细节见 `docs/plans/desync-2026-11-09-modlog25.md`。
>
> **还要玩家做的**：创意工坊线上仍是 **v0.2.513**（这次修复没上传），要复现得先上传 + 四台机同一包；
> 若仍不同步，这次请**每人各一份 `mod_log.txt`**（摘要里那两行并排一比即可）。
> 已排除「混版」：0.2.513 的 zip 与 workshop 两份 Impl 都是 0.2.513。

> ### ⏳ v0.2.525 华丽猎 6 条（`docs/plans/grand-finale-fixes-2026-11-09.md`）
>
> 一次改完、107/111 都编过、只部署了那一轮。要看的日志：
> · 弹珠袋 —— `弹珠袋：清掉 N 个「跳过下一次衰减」标记`，且 `玩家侧快照（她第 2 回合出手前）` **不应**再有 `VULNERABLE_POWER`；
> · 谋杀 —— `谋杀：华丽猎按回合查表 ⇒ 第 4 回合 = 20`（8→42、11→63、14→84）；
> · 小刀 —— `小刀预览: 牌面 6 → …`、`小刀段数诊断：STORM_OF_STEEL … 剧本丢弃=5/4 张`；
> · 选牌 —— 每次选牌日志现在带 `发起者=<牌名>（按丢弃者分桶）`；
> · 第 6 回合 —— `剧本第 6 回合: 打 [PIERCING_WAIL,STRIKE_SILENT,SURVIVOR] 跳过 [DEFEND_SILENT(被丢弃)]`。
>
> **本轮的三个反编译定论（别再走回头路）**：
> 1. `PowerCmd.Apply` 里有 `if (target.Side == Player && power.Type == Debuff) power.SkipNextDurationTick = true;`，
>    而 `TickDownDuration` 见到它**跳过这一次衰减** ⇒ 这就是「易伤持续三回合」的唯一根因；施加完清掉即可。
> 2. `BagOfMarbles.BeforeSideTurnStart` 判的是「可命中敌人」而不是阵营，影子玩家的遗物也算监听者
>    ⇒ 必须**压掉它**，不能像 v0.2.524 那样放行（否则 1 层变 2 层）。
> 3. `HiddenDaggers` 没有 `OnUpgrade` ⇒ 升级**不改 Shivs 张数**（仍 2 张），只把生成的刀升级成 6 点
>    ⇒ 隐秘匕首 正确面板是 **6×2**（低进阶 4×2）。
>
> ✅ **已发布（2026-11-09 17:50）**：打包 `.dist\CharacterBosses-v0.2.528.zip`（13,827,689 B，
> 包内两份 Impl 都是 v0.2.528，护栏验过）、工坊已上传（item **3830639796**，登录身份「飞猪」，
> 上传字节数 15,807,873 = 新内容总和，逐字对上）、GitHub 已推送 `387d04f..07a8780`。
> 工坊 `changenote.txt` 与 `docs\安装说明.txt` 都改成了 **v0.2.528**（说明里新增「自 v0.2.513 起」那一段）。

0. ✅ **（v0.2.384 完成）「Boss 被击晕时的整套行为」已实现并实机验收通过** ——
   压暗全部手牌 / 撤掉全部意图 / 本回合零出牌 / **下回合补打被打断的那一套（连晕两回合也一样）**。
   关键洞察：vanilla 的 `STUNNED` 空 MoveState 天然跳过了 `PlayPlannedCards`（计划与 TurnNumber 原封不动），
   唯一要拦的是玩家回合开始时那次 `PrepareTurn`。细节 + 日志原文见 `docs/plans/stun-behavior.md`。
   **别再回头接管 `CreatureCmd.Stun` 或手改 `NextMove`。**
1. **v0.2.385 还剩两项没实机跑**（都只在**辉星储君** `REGENT_STAR` 身上触发，
   而变体按遭遇 id / 本局三层阵容挑，调试开关到不了）：
   · **A** 卡牌左边有没有那颗「黑洞（下回合辉星到账）」独立意图（日志 `独立意图（黑洞（下回合辉星到账））: N 点`）；
   · **B** 万向斩溅射是否无视奥斯提（日志 `万向斩溅射：剔掉 N 个召唤物/宠物…`）。
   若想主动验证：得让本局三层主 Boss 是 REGENT_STAR（换个种子/存档），`charbosses_debug.txt` 写 `regent@3`。
2. 仍要逐个实机验证（更早的）：**蛇眼猎整场**（v0.2.356 刚修掉它的抽牌炸弹，必测）、
   养手骨的无界命匣召唤（第 1 回合 7、之后 2）、养手骨「不死」的两条 hook 接管顺序、
   事件机的谋杀加伤与情感芯片球被动翻倍显示。
3. **工坊仍未上传**（v0.2.375 之后都没传）：`.dist` 与 `workshop\` 都已备好到 **v0.2.385**，等用户开口跑
   `powershell -ExecutionPolicy Bypass -File tools\upload-workshop.ps1`（Steam 需运行并登录）。
4. 用户侧可选脚本：`tools\upload-workshop.ps1`、`tools\feishu-add-columns.ps1`、
   `tools\feishu-clear-table.ps1 -MinVersion 0.2.276`。
5. ⚠ 顺带发现（没动）：这台机器游戏窗口 1707×1067、UI 坐标空间按 1920 宽排，
   Boss 立绘与手牌栏贴右缘，截图里最右那张卡被屏幕边裁掉一点。

## 其它挂着的（非 BGM，来自 HANDOVER §6，本轮未动）
1. 放血战血量应该按 `TierAct` 算（控制台在一层打也应是 380），现在按玩家当前幕算 230。
2. 放血战自伤不掉血（血契/御血/突破/烙印）待查；
3. 奥斯提被上毒待查。
4. 祭品抽牌待复验；
5. 灵魂骨「回合开始抽牌」归类已修（v0.2.117），待玩家复验。
