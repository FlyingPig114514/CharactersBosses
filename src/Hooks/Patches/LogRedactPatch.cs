using System;
using HarmonyLib;
using CharacterBosses.Core;

namespace CharacterBosses.Patches;

/// <summary>
/// **日志脱敏的唯一出口**（v0.2.892）。
///
/// 为什么不改 `ModEntry.cs` 里的 `Logger.Log`：项目规范明确禁止改那个文件（`Logger` 属于脚手架身份）。
/// 所以用 Harmony 给 `Logger.Log` 挂前缀，把**要写进 mod_log 的那行字符串**先过一遍
/// <see cref="Redact.Text"/>（SteamID / NetId → 局内代号 `P#xxxxxx`、路径里的用户名 → `&lt;userdir&gt;`、
/// 玩家昵称 → 同一个代号）。
///
/// 失败时**原样放行**：日志宁可多一条真 ID，也不能因为脱敏而丢日志。
/// </summary>
[HarmonyPatch(typeof(Logger), nameof(Logger.Log))]
internal static class LogRedactPatch
{
    [HarmonyPrefix]
    public static void Prefix(ref string message)
    {
        try
        {
            if (message == null) return;
            message = Redact.Text(message);
        }
        catch { /* 原样放行 */ }
    }
}
