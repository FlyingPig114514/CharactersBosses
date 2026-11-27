using System;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes;              // NGame
using MegaCrit.Sts2.Core.Nodes.CommonUi;     // NInputManager

namespace CharacterBosses.Core;

/// <summary>
/// 把 F8/F9 挂到**游戏自己的输入入口**上（v0.2.976）。
///
/// 反编译依据（`.refs\sts2_113.dll`，0.111 里签名完全相同）：
///   · `MegaCrit.Sts2.Core.Nodes.NGame._Input(Godot.InputEvent inputEvent)`               —— public override
///   · `MegaCrit.Sts2.Core.Nodes.CommonUi.NInputManager._UnhandledKeyInput(Godot.InputEvent)` —— public override
/// 两个都是**唯一**同名方法（各 1 个），没有重载 ⇒ 不会撞上 AmbiguousMatchException；
/// 但按工程硬规矩仍然**写死参数类型**（`new[] { typeof(InputEvent) }`），不靠名字猜。
///
/// 为什么用后缀而不是前缀：我们只是**旁听**，绝不能改变游戏自己的输入处理结果
/// （尤其不能返回 false / 不能 `SetInputAsHandled`，那会把游戏的按键吞掉）。
/// `HotkeyRouter.Handle` 内部全包了 try/catch，挂掉也不会影响游戏。
/// </summary>
[HarmonyPatch(typeof(NGame), "_Input", new[] { typeof(InputEvent) })]
internal static class GameInputHotkeyPatch
{
    [HarmonyPostfix]
    public static void Postfix(InputEvent inputEvent)
    {
        try { HotkeyRouter.Handle(inputEvent, "game.NGame._Input"); } catch { /* 输入路径绝不能被我们打断 */ }
    }
}

/// <summary>同上，挂在游戏的输入管理器上（它比 `_Input` 更下游，但能证明「unhandled 通道」到底通不通）。</summary>
[HarmonyPatch(typeof(NInputManager), "_UnhandledKeyInput", new[] { typeof(InputEvent) })]
internal static class InputManagerHotkeyPatch
{
    [HarmonyPostfix]
    public static void Postfix(InputEvent inputEvent)
    {
        try { HotkeyRouter.Handle(inputEvent, "game.NInputManager._UnhandledKeyInput"); } catch { /* 同上 */ }
    }
}
