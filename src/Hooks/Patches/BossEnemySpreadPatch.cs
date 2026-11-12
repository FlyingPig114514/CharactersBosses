using System;
using System.Collections.Generic;
using HarmonyLib;
using Godot;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using CharacterBosses.Monsters;
using CharacterBosses.UI;

namespace CharacterBosses.Patches;

/// <summary>
/// 多 Boss 同场时的**错位**：挂在原版 <c>NCombatRoom.PositionEnemies</c>（private）的 postfix 上 ——
/// 原版刚把敌人排好，我们紧接着按序号叠加偏移：序号 0 偏**左上**、序号 1 偏**右下**（竖直方向也错开）。
///
/// 为什么不在 <c>BossFrameTick</c> 里每帧硬写 <c>GlobalPosition</c>：那样会被原版布局拉回去
/// （用户实测：两只始终挤在同一列、看着平行）。挂在「排版刚结束」这瞬间才站得住。
/// 单人（我们的 Boss 只有一只）时整段直接返回，行为不变。
/// </summary>
[HarmonyPatch(typeof(NCombatRoom), "PositionEnemies")]
public static class BossEnemySpreadPatch
{
    /// <summary>两只时的 X 半间距默认值（可被 `charbosses_ui.txt` 的 bossSpreadX 覆盖）。</summary>
    private const float DefaultSpreadX = 200f;

    /// <summary>两只时的 Y 半间距默认值（可被 `charbosses_ui.txt` 的 bossSpreadY 覆盖）。</summary>
    private const float DefaultSpreadY = 140f;

    /// <summary>4 只时中间两只离中线的默认距离（可被 `boss4MidX` 覆盖）。</summary>
    private const float DefaultMidX = 110f;

    private static float SpreadX => Tuning(BossUiTuning.BossSpreadX, DefaultSpreadX);
    private static float SpreadY => Tuning(BossUiTuning.BossSpreadY, DefaultSpreadY);
    private static float MidX => Tuning(BossUiTuning.Boss4MidX, DefaultMidX);

    private static float Tuning(int tuned, float fallback)
        => tuned > 0 ? tuned : fallback;

    [HarmonyPostfix]
    public static void Postfix(List<NCreature> creatures)
    {
        try
        {
            if (creatures == null) return;

            var ours = new List<NCreature>();
            foreach (var node in creatures)
            {
                if (node?.Entity?.Monster is CharBossMonster) ours.Add(node);
            }
            if (ours.Count < 2) return;   // 单人：什么都不做

            // 站位间距可以热调（charbosses_ui.txt）—— 多人局没法在本地反复试，交给实机调最快。
            try { BossUiTuning.Refresh(); } catch { /* 忽略 */ }

            // ⚠ 必须**按序轮流占位**，不能「谁属于左组就一律往左」：
            //   铁甲战士 / 静默猎手 / 亡灵契约师都在左组，两只同场时
            //   以前两只都算 `minX - SpreadX/2` ⇒ **完全同一个坐标叠在一起**
            //   （用户 2026-10-24 实测截图：愤怒战和灾厄骨叠在一块；
            //     日志：`IRONCLAD_BOSS(左上) → (1320,670) | NECROBINDER_BOSS(左上) → (1320,670)`）。
            // 现在：先按**行动次序**排序（见 PlacementRank），再按序号轮流占位，
            // 保证每只拿到**互不相同**的位置，而且**左右顺序 = 它们出手的顺序**
            // （用户 2026-10-29：「站位顺序和行动顺序不一致了」）。
            var combat = FirstCombat(ours);
            ours.Sort((a, b) =>
            {
                int rank = PlacementRank(a.Entity, combat).CompareTo(PlacementRank(b.Entity, combat));
                if (rank != 0) return rank;
                return 0;
            });

            // 先读出原版排好的位置范围，再按上面排好的顺序摆绝对位置。
            float minX = float.MaxValue, maxX = float.MinValue;
            foreach (var node in ours)
            {
                minX = Mathf.Min(minX, node.GlobalPosition.X);
                maxX = Mathf.Max(maxX, node.GlobalPosition.X);
            }
            float centerX = (minX + maxX) * 0.5f;
            float baseY = ours[0].GlobalPosition.Y;
            float upY = baseY - SpreadY * 0.65f;
            float downY = baseY + SpreadY * 0.65f;

            // 视口宽度（用来把最外侧那两只夹回屏幕内 —— 上一次直接外扩把 x 顶到 2110 出屏了）
            float viewW = 0f;
            try { viewW = ours[0].GetViewportRect().Size.X; } catch { viewW = 0f; }

            // 4 只（四人联机）：用户 2026-10-29「左上、下偏左、上偏右、右下」（平行四边形）；
            // 3 只（三人联机）：用户 2026-10-26「左上、中下、右上」；2 只：「左上 / 右下」（零回归）。
            string[] names = PlacementNames(ours.Count);
            var slotX = SlotX(ours.Count, minX, maxX, centerX, viewW);
            var slotY = SlotY(ours.Count, upY, downY);

            var parts = new List<string>();
            for (int i = 0; i < ours.Count; i++)
            {
                var node = ours[i];
                int slot = i % slotX.Length;
                var pos = new Vector2(slotX[slot], slotY[slot]);
                node.GlobalPosition = pos;
                // 诊断带上 act / rank / key —— 站位不对时一眼能看出是「排序没生效」还是「坐标被原版拉回去」
                var monster = node.Entity?.Monster as CharBossMonster;
                parts.Add($"[{i}] {node.Entity?.Monster?.Id.Entry}" +
                          $"(act={monster?.ActIndex},rank={PlacementRank(node.Entity, combat)})" +
                          $"{names[slot]} → ({pos.X:F0},{pos.Y:F0})");
            }
            // ★ 用户 2026-11-05：**仆从站储君左边**，而且不要和其他 boss 重叠。
            //   仆从不是 CharBossMonster（所以上面的 ours 循环不处理它们），这里单独摆。
            try { PlaceMinions(); } catch (Exception e) { Logger.Log("[CharBoss] 仆从摆位失败: " + e.Message); }

            Logger.Log($"[CharBoss] 多 Boss 错位（{ours.Count} 只，{string.Join("/", names)}，" +
                       $"间距 {SpreadX:F0}×{SpreadY:F0}，视口宽 {viewW:F0}，" +
                       $"行动次序 {ActionOrderText(combat, ours)}）：{string.Join(" | ", parts)}");
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 多 Boss 错位失败: " + e.Message);
        }
    }

    /// <summary>
    /// 把**仆从**摆在它的召唤者（储君）**左边**（用户 2026-11-05：「站在储君左边」）。
    /// 侍从数量最多 2，按 `-130 / -260` 的横向间距、稍微往下错开，避免挡住储君本人的意图。
    /// </summary>
    internal static void PlaceMinions()
    {
        var room = NCombatRoom.Instance;
        if (room == null) return;
        foreach (var pair in Monsters.BossMinionMonster.Registry)
        {
            var summoner = pair.Key?.Creature;
            if (summoner == null) continue;
            var summonerNode = room.GetCreatureNode(summoner);
            if (summonerNode == null) continue;
            var basePos = summonerNode.GlobalPosition;
            int i = 0;
            foreach (var minion in pair.Value)
            {
                var creature = minion?.Creature;
                if (creature == null || !creature.IsAlive) continue;
                // ★ 2026-11-05：每帧保持「占位透明 + 死后卡面隐藏」（死亡动画会重置 Modulate）
                try { minion!.EnforceArtVisuals(); } catch { /* 忽略 */ }
                var node = room.GetCreatureNode(creature);
                if (node == null) continue;
                node.GlobalPosition = new Vector2(basePos.X - 130f * (i + 1), basePos.Y + 70f + 25f * i);
                i++;
            }
        }
    }
    /// <summary>每个站位的中文名（日志与文档共用同一套说法）。</summary>
    private static string[] PlacementNames(int count) => count switch
    {
        // 5 只（5 人联机）：用户 2026-11-11「5 个 boss 的站位是 w 型」= 上下上下上（两个谷、三个峰）
        >= 5 => new[] { "左上", "左下", "中上", "右下", "右上" },
        >= 4 => new[] { "左上", "下偏左", "上偏右", "右下" },
        3 => new[] { "左上", "中下", "右上" },
        _ => new[] { "左上", "右下" },
    };

    /// <summary>
    /// 每个站位的 X 坐标。
    ///   · 4 只：最左 / 中线偏左 / 中线偏右 / 最右（平行四边形四角）；
    ///   · 3 只：最左 / 中线 / 最右；
    ///   · 2 只：最左 / 最右。
    /// 「最左/最右」是在**原版排好的范围**上再外扩 —— 不外扩的话四只挤在 803 像素里
    /// （间距 ~270），而每只的状态条/意图面板宽约 300 ⇒ 会互相压住（用户 2026-10-29：
    /// 「太挤了，有意图的牌会叠在一起」）。外扩之后再**夹到视口内**，
    /// 避免上一次那种「x=2110 直接出屏、少看见一只」。
    /// </summary>
    private static float[] SlotX(int count, float minX, float maxX, float centerX, float viewW)
    {
        if (count >= 5)
        {
            // ★ v0.2.585（5 人联机）：5 只在可用宽度里**等距**摆开（相邻 ≈ 1.2×SpreadX ≈ 240），
            //   右侧仍贴右缘、左侧仍不压玩家区 —— 和 4 只那套同一套边界规则，只是多一个点。
            //   4 只时横向占 ~900px，这里 5 个点占 ~960px，量级相同 ⇒ 战场缩放不用动（仍是 0.80）。
            float right = viewW > 600f ? viewW - SpreadX * 0.75f : maxX + SpreadX * 0.75f;
            float lo = viewW > 600f ? viewW * 0.17f : minX - SpreadX * 0.75f;
            float need5 = SpreadX * 1.2f * 4f;          // 4 个间隔
            float left = right - need5;
            if (left < lo)
            {
                left = lo;                              // 让位给玩家区，剩下宽度均分（W 形状不变）
                if (right - left < need5 * 0.5f) right = left + need5;   // 极端窄屏兜底
            }
            float span5 = right - left;
            return new[]
            {
                left,
                left + span5 * 0.25f,
                left + span5 * 0.5f,
                left + span5 * 0.75f,
                right,
            };
        }
        if (count >= 4)
        {
            // 用户 2026-10-29 第三次反馈：「右边还有那么大空间呢，再往右错一些」。
            // 上一版把右侧夹在 `视口宽 - 310`，1920 的视口只用到 1610 ⇒ 右边空了 300 多。
            // 现在**最右那只贴着右缘**（只留 0.75 个 SpreadX 的余量），
            // 相邻中心距固定 `1.5*SpreadX`（≈300，正好一个意图面板宽，不会再压住）。
            float spacing = SpreadX * 1.5f;
            float right = viewW > 600f ? viewW - SpreadX * 0.75f : maxX + SpreadX * 0.75f;
            float left = right - spacing * 3f;

            // 最左那只别压到玩家区（玩家在左边，约 0.17 屏宽）
            float lo = viewW > 600f ? viewW * 0.17f : minX - SpreadX * 0.75f;
            if (left < lo)
            {
                left = lo;
                right = left + spacing * 3f;
            }
            // 兜底：万一算出来还是比原版范围窄（面板会叠），以中线为轴撑到 3 个间距
            float need = SpreadX * 3f;
            if (right - left < need)
            {
                float axis = (left + right) * 0.5f;
                left = axis - need * 0.5f;
                right = axis + need * 0.5f;
            }
            float span = right - left;
            return new[]
            {
                left,
                left + span * 0.345f,
                left + span * 0.655f,
                right,
            };
        }
        if (count == 3)
        {
            return new[]
            {
                minX - SpreadX * 0.75f,
                centerX,
                maxX + SpreadX * 0.75f,
            };
        }
        return new[] { minX - SpreadX * 0.5f, maxX + SpreadX * 0.5f };
    }

    /// <summary>
    /// 每个站位的 Y 坐标。
    ///   · 4 只：上 / 下 / 上 / 下（平行四边形：左上·上偏右 在上排，下偏左·右下 在下排）；
    ///   · 3 只：上 / 下 / 上；
    ///   · 2 只：上 / 下。
    /// </summary>
    private static float[] SlotY(int count, float upY, float downY)
    {
        // 5 只（5 人联机）：上 / 下 / 上 / 下 / 上 —— 字母 W 的五个折点（两个谷、三个峰）
        if (count >= 5) return new[] { upY, downY, upY, downY, upY };
        if (count >= 4) return new[] { upY, downY, upY, downY };
        if (count == 3) return new[] { upY, downY, upY };
        return new[] { upY, downY };
    }

    /// <summary>
    /// **行动次序**（0 = 最左 = 最先出手）。
    ///
    /// 用户 2026-10-29 第三次实测：「现在站位顺序和行动顺序不一致了」。
    /// 之前这里是按「角色固定表」（猎 0 → 机 1 → 骨 2 → 储 3 → 战 4）排的，
    /// 而**真正的出手顺序是 `CombatState.Enemies` 里的顺序**（原版就是按这个列表逐只行动）。
    /// 所以直接取「这只 Boss 在 Enemies 里的下标」—— 站位顺序从此**恒等于**行动顺序，
    /// 不管遭遇里是怎么放的，也不管以后加不加新 Boss。
    /// </summary>
    private static int PlacementRank(MegaCrit.Sts2.Core.Entities.Creatures.Creature? entity,
                                     MegaCrit.Sts2.Core.Combat.ICombatState? combat)
    {
        try
        {
            if (entity == null) return 9;
            var enemies = combat?.Enemies;
            if (enemies == null) return 9;
            for (int i = 0; i < enemies.Count; i++)
            {
                if (ReferenceEquals(enemies[i], entity)) return i;
            }
            return 9;
        }
        catch { return 9; }
    }

    /// <summary>第一只的环境里能拿到的战斗状态（用来读 `Enemies` 的行动次序）。</summary>
    private static MegaCrit.Sts2.Core.Combat.ICombatState? FirstCombat(List<NCreature> nodes)
    {
        try
        {
            foreach (var n in nodes)
            {
                var combat = n?.Entity?.CombatState;
                if (combat != null) return combat;
            }
        }
        catch { /* 忽略 */ }
        return null;
    }

    /// <summary>日志用：把行动次序写成 `1:DEFECT 2:REGENT …`，方便和站位对表。</summary>
    private static string ActionOrderText(MegaCrit.Sts2.Core.Combat.ICombatState? combat, List<NCreature> nodes)
    {
        try
        {
            var enemies = combat?.Enemies;
            if (enemies == null) return "？";
            var parts = new List<string>();
            for (int i = 0; i < enemies.Count; i++)
            {
                var c = enemies[i];
                if (c?.Monster is not CharBossMonster) continue;
                parts.Add($"{i}:{c.Monster.Id.Entry}");
            }
            return parts.Count > 0 ? string.Join(" ", parts) : "？";
        }
        catch { return "？"; }
    }

    /// <summary>同组内的固定次序（只用来打破平手，保证两只不会拿到同一侧）。</summary>
    private static int CharacterOrder(MegaCrit.Sts2.Core.Models.MonsterModel? monster)
    {
        try
        {
            return monster switch
            {
                SilentBoss => 0,
                DefectBoss => 1,
                NecrobinderBoss => 2,
                RegentBoss => 3,
                IroncladBoss => 4,
                _ => 9,
            };
        }
        catch { return 9; }
    }

    /// <summary>是不是三层「易伤战」（铁甲战士、幕下标 2）。保留给其它地方/日志用。</summary>
    private static bool IsVulnIronclad(MegaCrit.Sts2.Core.Models.MonsterModel? monster)
    {
        try
        {
            return monster is CharBossMonster boss
                   && boss.Def?.Key == "IRONCLAD_BOSS"
                   && boss.ActIndex >= 2;
        }
        catch { return false; }
    }
}
