using HarmonyLib;
using Sandbox.Game.Screens.Helpers;
using Sandbox.Graphics.GUI;

namespace ClientPlugin.Patches;

// ReSharper disable UnusedMember.Local
// ReSharper disable UnusedType.Global
// ReSharper disable InconsistentNaming
[HarmonyPatch(typeof(MyGuiControlToolbar))]
public static class MyGuiControlToolbarPatch
{
    private static Config Cfg => Config.Current;
    
    [HarmonyPrefix]
    [HarmonyPatch(nameof(MyGuiControlToolbar.HandleDragAndDrop))]
    private static bool HandleDragAndDropPrefix(MyGuiControlToolbar __instance, MyDragAndDropEventArgs eventArgs)
    {
        if (!Cfg.EnableStagingArea)
            return true;
        
        // Prevent clearing the toolbar slot on cancelling drag&drop by releasing the mouse outside the toolbar
        if (eventArgs.DropTo == null || !__instance.IsToolbarGrid(eventArgs.DropTo.Grid))
            return false;

        // Prevent dropping something on the very last "hand" slot
        if (eventArgs.DropTo.ItemIndex == 9)
            return false;

        return true;
    }
}
