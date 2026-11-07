using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using CharacterBosses.Core;
using CharacterBosses.Encounters;

namespace CharacterBosses.Patches;

/// <summary>
/// 把每一幕的 Boss 换成角色 Boss。
/// 主切点是 <see cref="ActModel.PullNextEncounter"/>：它是选房间的唯一漏斗，
/// Postfix 改写返回值即可覆盖所有路径（新游戏/读档/调试跳关）。
/// 注意必须返回 canonical 实例 —— 调用方 RunManager.CreateRoom 会自己调 .ToMutable()。
/// </summary>
[HarmonyPatch(typeof(ActModel), nameof(ActModel.PullNextEncounter))]
public static class PullNextEncounterPatch
{
    // 「本幕第几只 Boss」的记账（用户设计：第三幕先根除骨、后无色储君）。
    private static int _lastBossAct = -1;
    private static int _bossesHanded;
    private static MegaCrit.Sts2.Core.Models.ModelId? _lastHandedBossId;

    /// <summary>本幕已经发出去几只 Boss（多 Boss 同场时用来判断「第几场」）。</summary>
    internal static int BossesHandedThisAct => _bossesHanded;

    /// <summary>上次记账时的幕号。</summary>
    internal static int LastBossAct => _lastBossAct;

    [HarmonyPostfix]
    public static void Postfix(ActModel __instance, RoomType roomType, ref EncounterModel __result)
    {
        try
        {
            // 第一次拉房间时把所有 Boss 定义解析一遍写进日志（早于任何战斗，方便用户核对卡组）
            BossDefValidator.ValidateOnce();

            if (roomType == RoomType.Boss)
            {
                // 诊断：只要拉 Boss 房间就先记一行（用户 2026-10-24「进不去 2 层 boss」时日志里
                // 连这一行都没有 ⇒ 说明卡在我们之前；有这一行但没下一行 ⇒ 卡在我们这一层）。
                try
                {
                    Logger.Log($"[Hook] 拉 Boss 房间: act={__instance.Index} incoming={__result?.Id} " +
                               $"已发={_bossesHanded}");
                }
                catch { /* 忽略 */ }

                // 「本幕第几只 Boss」——用户设计：第三幕**先根除骨、后无色储君**。
                // 判定方式：同一幕里已经发过一只（our）Boss 之后，第二次拉 Boss 房间就走第二只的表
                //（`ForVanillaBoss(..., secondBoss: true)` 取 ActTable 的第 2 列）。
                int actIndex = __instance.Index;
                if (actIndex != _lastBossAct) { _lastBossAct = actIndex; _bossesHanded = 0; _lastHandedBossId = null; }

                bool wantSecond = IsSecondSlot(__instance, __result, _bossesHanded >= 1);

                // ⚠ **地图/顶栏显示的是 act 里存着的那个遭遇**（`BossSlotReplacer` 在生成房间时就写进去了），
                // 所以只要它已经是「我们的」遭遇，就**不要再按阵容重算一次**：
                // 阵容的随机种子不写进存档（拿不到 `RunState.Rng.Seed` 时靠本地随机，重启后会变），
                // 重算会给出另一个角色 ⇒ 顶栏显示「愤怒战」、实际打的是「灾厄骨」
                //（用户 2026-10-24 实测；同一条日志里 `act 2 boss #1: REGENT_BOSS_ENCOUNTER -> SILENT_SHIV…`
                //  就是这种「显示的 ≠ 实际的」）。
                // 唯一例外：这一槽是**第二只 Boss**却和第一只撞成同一个 ⇒ 必须换掉（否则两个同一只）。
                bool keepStored = false;
                if (CharBossEncounters.IsOurs(__result))
                {
                    bool duplicateSecond = wantSecond
                        && __instance.BossEncounter != null
                        && __result != null
                        && __result.Id == __instance.BossEncounter.Id;
                    keepStored = !duplicateSecond;
                }

                var resolved = keepStored
                    ? __result
                    : CharBossEncounters.ForVanillaBoss(__instance, __result, wantSecond);
                if (resolved != null && resolved.Id != __result?.Id)
                {
                    Logger.Log($"[Hook] act {actIndex} boss #{_bossesHanded + 1}: {__result?.Id} -> {resolved.Id}" +
                               (wantSecond ? "（第二只）" : string.Empty));
                    __result = resolved;
                }
                else if (keepStored)
                {
                    Logger.Log($"[Hook] act {actIndex} boss #{_bossesHanded + 1}: 沿用地图上那只 {__result?.Id}" +
                               (wantSecond ? "（第二只）" : string.Empty) + "，不再按阵容重算");
                }

                // 记账：换了 id 才算「又发了一只」（同一房间重复拉取不会重复计数）
                if (__result != null && !Equals(__result.Id, _lastHandedBossId))
                {
                    _lastHandedBossId = __result.Id;
                    _bossesHanded++;
                }
                return;
            }

            // 调试开关：第一幕的普通战斗全换成角色 Boss，省掉打通整幕
            // （配合 charbosses_debug.txt 里写 cycle 就能一场一场把 5 个角色都看一遍）
            if (DebugMode.Enabled && roomType == RoomType.Monster && __instance.Index == 0)
            {
                // 这个房间已经是我们的了（读档/重进房间）→ 不要再轮换，保持原样
                if (CharBossEncounters.IsOurs(__result)) return;

                var replacement = CharBossEncounters.ForDebug();
                if (replacement == null) return;
                Logger.Log($"[Hook] DEBUG: normal encounter -> {replacement.Id}");
                __result = replacement;
            }
        }
        catch (Exception e)
        {
            Logger.Log("[Hook] PullNextEncounter patch failed: " + e);
        }
    }

    /// <summary>
    /// 这一槽是不是「本幕第二只 Boss」（决定取 ActTable 的第 1 列还是第 2 列）。
    ///
    /// 判据改成**原版自己的那个开关**：`RoomSet.bossEncountersVisited`。
    /// 反编译依据（`MegaCrit.Sts2.Core.Rooms.RoomSet`）：
    /// <code>
    /// public EncounterModel NextBossEncounter
    /// {
    ///     if (bossEncountersVisited != 0 &amp;&amp; SecondBoss != null) return SecondBoss;
    ///     return Boss;
    /// }
    /// </code>
    /// `ActModel.PullNextEncounter(Boss)` 返回的就是它 —— 也就是说「这是第几场 Boss」这件事
    /// 原版自己记着：`bossEncountersVisited` 属于存档（`RoomSet` 序列化），两端一致、读档也对。
    /// 它在 `ActModel._rooms` 这个**非公开字段**里，所以下面用反射读（见 <see cref="BossesVisitedThisAct"/>）。
    ///
    /// ⚠ 用户 2026-10-24 实测「进阶 10 的两个三层 Boss 有可能重复」的根因就在这里：
    ///   旧判据是**比较遭遇 id**（`incoming.Id == SecondBoss.Id &amp;&amp; incoming.Id != Boss.Id`），
    ///   一旦存档里两槽存的是**同一个 id**（实测日志：`act 2 boss #1/#2` 两次的 incoming 都是
    ///   `REGENT_BOSS_ENCOUNTER`），这个比较永远为假 ⇒ 第二场被当成第一场 ⇒ 又是同一个 Boss。
    ///   现在先读原版计数（两槽 id 相同也分得清），拿不到才退回旧判据。
    /// </summary>
    private static bool IsSecondSlot(ActModel act, EncounterModel? incoming, bool fallback)
    {
        int visited = BossesVisitedThisAct(act);

        // ★★ v0.2.242（用户 2026-10-30：「进阶 10 三层两场战斗里的相同角色 Boss 都相同
        //    —— 两场都用刀猎、辉星储、易伤战」）：
        //   判「这是第几场」以前抄的是原版 `NextBossEncounter` 的完整条件
        //   （`bossEncountersVisited != 0 && SecondBoss != null`），**其中 `SecondBoss != null` 会坑我们**：
        //   原版那个第二槽常常到「我们真的要拉第二场」时才滚出来（甚至因为第二槽是我们替换出来的，
        //   字段本身可能还是空的）⇒ 这里判成「第一场」⇒ 两场都用第 1 场的阵容 ⇒ 变体完全相同。
        //   现在：只要**本幕已经打过一只**（`visited > 0`）且这一幕本来就可能有两场（进阶 ≥10 且第三幕），
        //   就直接按第二场处理，不再依赖第二槽是否已经存在。
        if (visited > 0 && TwoFightAct(act))
        {
            if (_secondSlotLogs++ < 12)
            {
                Logger.Log($"[Hook] 第 {act.Index + 1} 幕本幕已发过 {visited} 只 ⇒ 这一次按**第二场**处理" +
                           "（不再要求第二槽已经存在；否则两场会用同一套变体）");
            }
            return true;
        }

        if (visited >= 0)
        {
            // 先按原版 NextBossEncounter 的**完整条件**判：它也要求 SecondBoss != null
            //（没有第二槽的幕 —— 例如非进阶 10 —— 永远是第一场，不能因为重进房间被翻成第二只）。
            bool hasSecond = false;
            try { hasSecond = act.SecondBossEncounter != null; } catch { hasSecond = false; }
            if (!hasSecond) return false;
            return visited != 0;
        }

        try
        {
            var first = act.BossEncounter;
            var second = act.SecondBossEncounter;
            if (incoming != null && second != null && incoming.Id == second.Id &&
                (first == null || incoming.Id != first.Id))
            {
                return true;
            }
            if (incoming != null && first != null && incoming.Id == first.Id) return false;
        }
        catch { /* 用兜底 */ }

        // 读不到原版计数时的兜底：这一幕本来就有两场、且已经发过一只 ⇒ 按第二场
        if (fallback && TwoFightAct(act)) return true;
        return fallback;
    }

    /// <summary>「这一次拉 Boss 房间」是不是第二场的日志（最多 12 行）。</summary>
    private static int _secondSlotLogs;

    /// <summary>这一幕本来就可能有两场 Boss？= 进阶 ≥ 10 的第三幕（0 起下标 2）。</summary>
    private static bool TwoFightAct(ActModel? act)
    {
        try { return act != null && act.Index >= 2 && Core.BossLineups.Ascension() >= 10; }
        catch { return false; }
    }

    /// <summary>
    /// 本幕**已经拉过几只 Boss**（= 原版 <c>RoomSet.bossEncountersVisited</c>）。读不到返回 -1。
    ///
    /// `_rooms` 是 <c>ActModel</c> 的**非公开实例字段**（编译期确认 `CS0122`），只能反射；
    /// 字段和属性各缓存一次，读不到就永远走旧判据，不会每帧反射。
    /// </summary>
    private static int BossesVisitedThisAct(ActModel act)
    {
        if (act == null) return -1;
        try
        {
            if (!_roomsFieldSearched)
            {
                _roomsFieldSearched = true;
                const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
                var field = typeof(ActModel).GetField("_rooms", flags);
                if (field != null && typeof(MegaCrit.Sts2.Core.Rooms.RoomSet).IsAssignableFrom(field.FieldType))
                    _roomsField = field;
                _roomsVisitedField = typeof(MegaCrit.Sts2.Core.Rooms.RoomSet)
                    .GetField("bossEncountersVisited", flags);
                if (_roomsField == null || _roomsVisitedField == null)
                    Logger.Log("[Hook] 读不到 RoomSet 的 Boss 计数，第二只 Boss 改用遭遇 id 判据");
            }

            var rooms = _roomsField?.GetValue(act);
            var visited = _roomsVisitedField?.GetValue(rooms);
            return visited is int count ? count : -1;
        }
        catch { return -1; }
    }

    private static System.Reflection.FieldInfo? _roomsField;
    private static System.Reflection.FieldInfo? _roomsVisitedField;
    private static bool _roomsFieldSearched;
}
