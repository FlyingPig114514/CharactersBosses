using System;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using CharacterBosses.Monsters;

namespace CharacterBosses.Core;

/// <summary>
/// 三层「状态机」的意图数学（用户 2026-11-05 的 bug 反馈：不要把烟囱/开信刀的 5 点挂在左侧独立意图列，
/// 而是挂在**触发它的那张牌**底下，像「激发球的牌把球的伤害挂在牌下面」那样）。
///
/// 反编译（v0.111）得到每张牌**生成的状态牌张数**：
///   · 「超频」`Overclock`  → 1 张「灼烧」进弃牌堆；
///   · 「内核加速」`Turbo`   → 1 张状态牌进弃牌堆；
///   · 「污秽攻击」`GunkUp`  → 1 张「黏液」进弃牌堆；
///   · 「压缩」`Compact`    → **不生成**（它只是把手里已有的状态牌变换掉）。
/// 其余牌一律 0。
/// </summary>
internal static class StatusBossMath
{
    /// <summary>这张牌打出时会生成几张**状态牌**。</summary>
    internal static int StatusCardsGenerated(CardModel? card)
    {
        try
        {
            string entry = card?.Id.Entry ?? "";
            return entry switch
            {
                "OVERCLOCK" => 1,     // 超频 → 1 张「灼烧」
                "TURBO" => 1,         // 内核加速 → 1 张「虚空」
                "GUNK_UP" => 1,       // 污秽攻击 → 1 张「黏液」
                // ★ 2026-11-05（用户：「第一回合的强撑没有标 5*2」）：
                //   「强撑」(`FightThrough`) 的卡面就是「获得 13 点格挡；将 **2** 张伤口加入你的弃牌堆」，
                //   反编译 `FightThrough.OnPlay` 是 `for (i = 0; i < 2; i++) CreateCard<Wound>(Owner)`
                //   ⇒ 两张伤口都是**她**生成的 ⇒ 烟囱打两次（5×2），不是一次 10 点。
                "FIGHT_THROUGH" => 2,
                // 「高速脱离」(`BoostAway`)：获得 6 点格挡；将 1 张「晕眩」加入弃牌堆 ⇒ 烟囱一次。
                "BOOST_AWAY" => 1,
                // 「压缩」(`Compact`) 只是把手里已有的状态牌**变成「燃料」**（`Fuel` 是 Skill，不是状态牌）
                // ⇒ 不触发烟囱，写在这里只是留个说明。
                _ => 0,
            };
        }
        catch { return 0; }
    }

    /// <summary>她身上「烟囱」的每次触发伤害（没有这件能力就是 0）。</summary>
    internal static int SmokestackDamage(CharBossMonster? boss)
    {
        try
        {
            var power = boss?.Creature?.GetPower<SmokestackPower>();
            if (power != null) return Math.Max(0, power.Amount);
        }
        catch { /* 往下走前瞻 */ }

        // ★ v0.2.411（用户实测截图：第 1 回合「强撑」没有 5×2）：规划那一刻「烟囱」还只是她手里/
        //   本回合计划里的**第一张牌**，`SmokestackPower` 根本没上身 ⇒ 读到 0。这里退一步按卡面值。
        return AnyPlannedSmokestack(boss);
    }

    /// <summary>这张牌是不是「烟囱」（打出来之后她的每次生成状态牌都打全体）。</summary>
    internal static bool IsSmokestackCard(CardModel? card)
        => string.Equals(BossDamagePredictor.EntryOf(card), "SMOKESTACK", StringComparison.Ordinal);

    /// <summary>「烟囱」这张牌打出后每张状态牌会造成多少伤害（读卡面 `SmokestackPower` 变量）。</summary>
    internal static int CardSmokestackDamage(CardModel? card)
    {
        try
        {
            int amount = BossDamagePredictor.VarPreviewInt(card, "SmokestackPower", 0);
            if (amount <= 0) amount = BossDamagePredictor.VarPreviewInt(card, "Smokestack", 0);
            return Math.Max(0, amount);
        }
        catch { return 0; }
    }

    /// <summary>
    /// 「烟囱」还没上身时，从**本回合计划**里找一张（她这回合会打出）的烟囱读数值。
    /// 找不到再看手牌（出牌期计划里那张已经打掉了）。
    /// </summary>
    private static int AnyPlannedSmokestack(CharBossMonster? boss)
    {
        try
        {
            if (boss == null) return 0;
            var plan = boss.Plan;
            if (plan != null)
            {
                for (int i = 0; i < plan.Count; i++)
                {
                    var card = plan[i]?.Card;
                    if (IsSmokestackCard(card))
                    {
                        int amount = CardSmokestackDamage(card);
                        if (amount > 0) return amount;
                    }
                }
            }
            var hand = boss.Shadow?.PlayerCombatState?.Hand?.Cards;
            if (hand != null)
            {
                for (int i = 0; i < hand.Count; i++)
                {
                    var card = hand[i];
                    if (IsSmokestackCard(card))
                    {
                        int amount = CardSmokestackDamage(card);
                        if (amount > 0) return amount;
                    }
                }
            }
        }
        catch { /* 忽略 */ }
        return 0;
    }

    /// <summary>她身上「开信刀」的每次触发伤害（没有这件遗物就是 0）。</summary>
    internal static int LetterOpenerDamage(CharBossMonster? boss)
    {
        try
        {
            var relics = boss?.Shadow?.Relics;
            if (relics == null) return 0;
            foreach (var relic in relics)
            {
                if (relic == null) continue;
                string entry;
                try { entry = relic.Id.Entry; } catch { continue; }
                if (entry != "LETTER_OPENER") continue;
                return Math.Max(0, (int)Math.Round(relic.DynamicVars.Damage.BaseValue));
            }
        }
        catch { /* 忽略 */ }
        return 0;
    }

    /// <summary>「每打出几张技能牌触发一次」（开信刀）。</summary>
    internal const int LetterOpenerSkillStep = 3;

    // ───────── 开信刀的「真实计数」诊断（v0.2.412） ─────────
    // 用户 2026-11-05：「全息影像不应该标注打 5」。按反编译，开信刀是
    // 「每打出 3 张**技能牌**打一次 5 点」，而第 4/9 回合的顺序是 超频 → 内核加速 → 全息影像，
    // 第 3 张技能牌正好是全息影像 —— 所以要么我们算错了，要么她对「哪张算技能牌」的判定和
    // 反编译不一致。这里把她遗物上的**私有计数器** `_skillsPlayedThisTurn` 直接读出来打到日志
    // （值一变就打一行），一看就知道真实计数，不用再猜。
    private static readonly System.Collections.Generic.Dictionary<CharBossMonster, int> LoCounterSeen = new();

    internal static void LogLetterOpenerCounter(CharBossMonster? boss)
    {
        try
        {
            var relics = boss?.Shadow?.Relics;
            if (boss == null || relics == null) return;

            RelicModel? relic = null;
            foreach (var r in relics)
            {
                if (r == null) continue;
                string entry;
                try { entry = r.Id.Entry; } catch { continue; }
                if (entry == "LETTER_OPENER") { relic = r; break; }
            }
            if (relic == null) return;

            var field = relic.GetType().GetField("_skillsPlayedThisTurn",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (field?.GetValue(relic) is not int count) return;

            if (LoCounterSeen.TryGetValue(boss, out int last) && last == count) return;
            LoCounterSeen[boss] = count;

            int step = LetterOpenerSkillStep;
            try { step = Math.Max(1, (int)Math.Round(relic.DynamicVars.Cards.BaseValue)); } catch { /* 用 3 */ }
            Logger.Log($"[CharBoss] 开信刀：她遗物上的真实计数 = 本回合已打出 {count} 张技能牌" +
                       $"（每 {step} 张触发一次；她的回合开始归零）" +
                       $"{(count > 0 && count % step == 0 ? " ⇒ 刚刚触发" : "")}");
        }
        catch { /* 纯诊断，失败无所谓 */ }
    }

    /// <summary>这张牌是不是「技能牌」（开信刀按技能牌计数）。</summary>
    internal static bool IsSkill(CardModel? card)
    {
        try { return card != null && card.Type == MegaCrit.Sts2.Core.Entities.Cards.CardType.Skill; }
        catch { return false; }
    }

    /// <summary>
    /// 打出这张技能牌之后，是不是正好凑够「每 <see cref="LetterOpenerSkillStep"/> 张技能牌」那一次触发。
    /// <paramref name="skillsBefore"/> = 本回合在它之前已经打出的技能牌数。
    /// </summary>
    internal static bool IsSkillForLetterOpener(CardModel? card, int skillsBefore)
    {
        if (!IsSkill(card)) return false;
        return (skillsBefore + 1) % LetterOpenerSkillStep == 0;
    }    /// <summary>
    /// 「大抱抱」(<c>BiiigHug</c>) 在**她洗牌的那一回合**生成的煤灰，会触发「烟囱」的全体伤害。
    ///
    /// ★ 2026-11-05（用户：「这两回合的伤害预览不对」）：以前这里乘了遗物的 `Cards` 变量（= 4），
    /// 以为一次洗牌生成 4 张煤灰 ⇒ 第 6 回合多算了 15 点。反编译 `BiiigHug`：
    ///   · `AfterShuffle(shuffler)`：**只有 `shuffler == Owner`（她自己洗牌）时**生成
    ///     **1 张**煤灰进她的抽牌堆（`CreateCard&lt;Soot&gt;` 只调用一次，没有循环）；
    ///   · 那个 `CardsVar(4)` 是**战斗开始时**「从卡组里删掉 4 张牌」用的，和煤灰张数无关。
    /// ⇒ 每次洗牌 = 1 张煤灰 = 烟囱打 1 次。
    /// 没有烟囱被她带着就返回 0（那时候煤灰不造成伤害）。
    /// 只在她**这一回合的剧本标了洗牌**时算（<see cref="BossTurn.ShuffleDrawPile"/>）。
    /// </summary>
    internal static int BigHugSootDamage(CharBossMonster? boss)
    {
        try
        {
            if (boss?.Creature == null) return 0;
            int stack = SmokestackDamage(boss);
            if (stack <= 0) return 0;

            var turn = boss.Def?.ScriptForAct(boss.ActIndex)?.TurnAt(boss.TurnNumber);
            if (turn == null || !turn.ShuffleDrawPile) return 0;

            var relics = boss.Shadow?.Relics;
            if (relics == null) return 0;
            foreach (var relic in relics)
            {
                if (relic == null) continue;
                string entry;
                try { entry = relic.Id.Entry; } catch { continue; }
                if (entry != "BIIIG_HUG") continue;
                // 每次洗牌只生成 **1 张**煤灰（见上面的反编译说明）
                return stack;
            }
        }
        catch { /* 忽略 */ }
        return 0;
    }
}