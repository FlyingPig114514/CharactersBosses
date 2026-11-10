using System;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;

namespace CharacterBosses.Patches;

/// <summary>
/// **铁甲战士 boss 的攻击语音**（用户 2026-11-10 需求，第二批）：
/// 「音效稍微有点早，等他的攻击牌造成伤害的时候再放音效。而且音效有点太频繁了，改成每回合第一次打出
///   以下牌的时候放：痛击、完美打击、突破、全身撞击、巨石、彼岸咆哮、闪电霹雳、欺凌」
///
/// 所以是**两个点配合**：
///   ① 挂待播标记 —— `CardCmd.AutoPlay` 前缀：这张牌的主人是铁甲战士 boss、
///      条目在 <see cref="Core.BossVoice.AttackVoiceCards"/> 那 8 张里、本回合还没喊过 ⇒ `VoiceArmed = true`。
///      （挂 AutoPlay 而不是 `PlayOne`，是为了连「彼岸咆哮」那种**回合结束时从消耗堆自动打出**的也能覆盖。）
///   ② 真正出声 —— `Hook.AfterDamageGiven` 前缀：伤害落地那一刻，如果 dealer 是她且还挂着待播标记
///      ⇒ 播语音、清标记、记「本回合已喊过」。多段牌只在第一段响一次。
///
/// 两个方法在两个版本（107/111）签名一致，都是 **async 返回 Task** ⇒ 这里只挂 **Prefix**、从不 `return false`，
/// 所以不需要 `ref Task __result`（本项目踩过「async 前缀返 false 不给 __result ⇒ NRE」的坑）。
/// </summary>
internal static class IroncladBossAttackVoicePatch
{
    /// <summary>① 打出那 8 张之一时挂上待播标记（真正出声要等伤害落地）。</summary>
    [HarmonyPatch(typeof(CardCmd), "AutoPlay",
        new[] { typeof(PlayerChoiceContext), typeof(CardModel), typeof(Creature), typeof(AutoPlayType), typeof(bool), typeof(bool) })]
    internal static class ArmPatch
    {
        private static void Prefix(CardModel card)
        {
            try
            {
                if (card == null) return;
                if (card.Owner?.Creature?.Monster is not Monsters.IroncladBoss boss) return;
                if (boss.VoiceFiredThisTurn || boss.VoiceArmed) return;

                string entry = card.Id?.Entry ?? string.Empty;
                if (!Core.BossVoice.AttackVoiceCards.Contains(entry)) return;

                boss.VoiceArmed = true;
                Logger.Log($"[CharBoss] 语音：{entry} 属于会喊的 8 张之一 ⇒ 等它造成伤害时再喊");
            }
            catch (Exception e)
            {
                try { Logger.Log("[CharBoss] 语音待播标记失败: " + e.Message); } catch { /* 忽略 */ }
            }
        }
    }

    /// <summary>② 伤害落地那一刻出声（只在她自己打出的那段伤害上、每回合最多一次）。</summary>
    [HarmonyPatch(typeof(Hook), "AfterDamageGiven",
        new[]
        {
            typeof(PlayerChoiceContext), typeof(ICombatState), typeof(Creature), typeof(DamageResult),
            typeof(ValueProp), typeof(Creature), typeof(CardModel),
        })]
    internal static class FirePatch
    {
        private static void Prefix(Creature dealer)
        {
            try
            {
                if (dealer?.Monster is not Monsters.IroncladBoss boss) return;
                if (!boss.VoiceArmed || boss.VoiceFiredThisTurn) return;

                boss.VoiceArmed = false;          // 多段牌只在第一段响
                boss.VoiceFiredThisTurn = true;   // 本回合不再喊
                Core.BossVoice.PlayAttackVoice(dealer);
            }
            catch (Exception e)
            {
                try { Logger.Log("[CharBoss] 攻击语音播放失败: " + e.Message); } catch { /* 忽略 */ }
            }
        }
    }
}
