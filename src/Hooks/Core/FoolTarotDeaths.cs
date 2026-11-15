#if STS2_111   // ⚠ 「愚者 / 塔罗牌」是 0.113.0 才有的 ⇒ 用户 2026-11-14：107 版本不加这个 Boss
using System;
using System.Collections.Generic;
using System.Text;
using MegaCrit.Sts2.Core.Models;

namespace CharacterBosses.Core;

/// <summary>
/// **「塔罗愚」的专属死亡提示**（用户 2026-11-14）：
///
/// > 「当有玩家死于塔罗愚时，如果他们所使用角色的名字属于左侧的任何一个，就把死亡提示改成：
/// >   [character]成为了[gold]愚者[/gold]牌组中新的[gold][对应卡牌][/gold]。
/// >   例如：铁甲战士成为了愚者牌组中新的倒吊人。」
///
/// 用户给的全表（22 张大阿卡纳 ↔ 角色）：
/// <code>
/// 愚者：愚人           故障机器人：I. 魔术师      诺努佩普：II. 女祭司
/// 女王：III. 皇后      建筑师：IV. 皇帝          商人：V. 教皇
/// 欧洛巴斯：VI. 恋人   达弗：VII. 战车           坦克斯：VIII. 力量
/// 亡命之徒：IX. 隐士   永世沙漏：X. 命运之轮     仪式兽：XI. 正义
/// 铁甲战士：XII. 倒吊人 亡灵契约师：XIII. 死神   佩尔：XIV. 节制
/// 瓦库：XV. 恶魔       尖塔：XVI. 高塔           储君：XVII. 星星
/// 静默猎手：XVIII. 月亮 特兹卡塔拉：XIX. 太阳    观者：XX. 审判
/// 涅奥：XXI. 世界
/// </code>
///
/// 用户 2026-11-15 补充：「你没查到的角色大多是先古之民、怪物 boss，
/// 但有可能有模组会将他们变成可游玩角色，所以**检测角色名字就行**」
/// ⇒ 匹配方式 = 把角色的**内部 id** 和**本地化显示名**都归一化（去空格标点、转小写、保留 CJK），
///   再跟下面这张「候选 token 表」比。表里同时放了英文名、中文名和常见的 id 片段，
///   任何一个命中就算匹配；匹配不上就保持原来的死亡提示（零风险）。
/// </summary>
internal static class FoolTarotDeaths
{
    /// <summary>角色 → 塔罗牌的 `cards` 表 entry。token 全部是小写、去标点后的形式。</summary>
    private static readonly (string Card, string[] Tokens)[] Table =
    {
        ("THE_FOOL",           new[] { "fool", "愚者", "thefool", "jester" }),
        ("THE_MAGICIAN",       new[] { "defect", "故障机器人", "thedefect", "magician" }),
        ("THE_HIGH_PRIESTESS", new[] { "norupēpu", "noru", "nonupepu", "诺努佩普", "highpriestess" }),
        ("THE_EMPRESS",        new[] { "queen", "thequeen", "女王", "empress", "皇后" }),
        ("THE_EMPEROR",        new[] { "architect", "建筑师", "emperor", "皇帝" }),
        ("THE_HIEROPHANT",     new[] { "merchant", "商人", "hierophant", "教皇" }),
        ("THE_LOVERS",         new[] { "orobos", "orobasu", "欧洛巴斯", "lovers", "恋人" }),
        ("THE_CHARIOT",        new[] { "dafu", "dave", "达弗", "chariot", "战车" }),
        ("STRENGTH",           new[] { "tanks", "tankusu", "坦克斯", "strength", "力量" }),
        ("THE_HERMIT",         new[] { "hermit", "亡命之徒", "thermit", "隐士" }),
        ("WHEEL_OF_FORTUNE",   new[] { "hourglass", "eternalhourglass", "永世沙漏", "wheeloffortune", "命运之轮" }),
        ("JUSTICE",            new[] { "ritualbeast", "仪式兽", "justice", "正义" }),
        ("THE_HANGED_MAN",     new[] { "ironclad", "铁甲战士", "theironclad", "hangedman", "倒吊人" }),
        ("DEATH",              new[] { "necrobinder", "亡灵契约师", "thenecrobinder", "death", "死神" }),
        ("TEMPERANCE",         new[] { "pael", "佩尔", "temperance", "节制" }),
        ("THE_DEVIL",          new[] { "vaku", "瓦库", "devil", "恶魔" }),
        ("THE_TOWER",          new[] { "spire", "thespire", "尖塔", "tower", "高塔" }),
        ("THE_STAR",           new[] { "regent", "储君", "theregent", "star", "星星" }),
        ("THE_MOON",           new[] { "silent", "静默猎手", "thesilent", "moon", "月亮" }),
        ("THE_SUN",            new[] { "tezkatkala", "tezkatkala", "特兹卡塔拉", "sun", "太阳" }),
        ("JUDGEMENT",          new[] { "watcher", "观者", "thewatcher", "judgement", "judgment", "审判" }),
        ("THE_WORLD",          new[] { "neow", "涅奥", "world", "世界" }),
    };

    /// <summary>这个角色对应哪张塔罗牌（取不到 = 维持原版死亡提示）。</summary>
    internal static string? CardEntryFor(CharacterModel? character)
    {
        if (character == null) return null;
        try
        {
            var keys = new HashSet<string>(StringComparer.Ordinal);
            try { var id = character.Id?.Entry; if (!string.IsNullOrEmpty(id)) keys.Add(Norm(id!)); } catch { }
            try { var name = character.Title?.GetFormattedText(); if (!string.IsNullOrEmpty(name)) keys.Add(Norm(name!)); } catch { }
            if (keys.Count == 0) return null;

            foreach (var (card, tokens) in Table)
            {
                foreach (var token in tokens)
                {
                    string norm = Norm(token);
                    if (norm.Length == 0) continue;
                    if (keys.Contains(norm)) return card;
                }
            }
        }
        catch (Exception e)
        {
            Logger.Log("[CharBoss] 塔罗死亡提示：匹配角色失败: " + e.Message);
        }
        return null;
    }

    /// <summary>归一化：只留字母/数字/CJK，全部小写（这样 `The Ironclad`、`IRONCLAD`、`铁甲战士` 都能对上）。</summary>
    private static string Norm(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            if (char.IsLetterOrDigit(ch)) sb.Append(char.ToLowerInvariant(ch));
        }
        return sb.ToString();
    }
}
#endif
