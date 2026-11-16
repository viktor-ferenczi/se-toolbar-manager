using ClientPlugin.Logic;
using HarmonyLib;
using Sandbox.Game.Gui;

namespace ClientPlugin.Patches;

// ReSharper disable UnusedMember.Local
// ReSharper disable UnusedType.Global

[HarmonyPatch(typeof(MyGuiScreenGamePlay))]
public static class FacedBlockInputPatch
{
    [HarmonyPrefix]
    [HarmonyPatch(nameof(MyGuiScreenGamePlay.HandleUnhandledInput))]
    private static bool HandleUnhandledInputPrefix() => !FacedBlock.HandleGameplayInput();
}
