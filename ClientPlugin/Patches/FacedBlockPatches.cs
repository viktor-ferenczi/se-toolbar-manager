using ClientPlugin.Logic;
using HarmonyLib;
using Sandbox.Game.Entities;
using Sandbox.Game.Gui;
using Sandbox.Game.SessionComponents.Clipboard;
using VRage.Input;

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

// With creative tools Ctrl+Delete is the game's delete of the faced grid, which the cut key
// takes over while it is bound to that
[HarmonyPatch(typeof(MyClipboardComponent))]
public static class FacedBlockDeletePatch
{
    [HarmonyPrefix]
    [HarmonyPatch("HandleDeleteInput")]
    private static bool HandleDeleteInputPrefix(ref bool __result)
    {
        if (!FacedBlock.HandleGameplayInput())
            return true;
        __result = true;
        return false;
    }
}

// Insert and Delete also turn the block in hand, with or without Ctrl
[HarmonyPatch(typeof(MyCubeBuilder))]
public static class FacedBlockRotationPatch
{
    [HarmonyPrefix]
    [HarmonyPatch("HandleRotationInput")]
    private static bool HandleRotationInputPrefix(ref bool __result)
    {
        if (!FacedBlock.IsKeyHeld(MyInput.Static))
            return true;
        __result = false;
        return false;
    }
}
