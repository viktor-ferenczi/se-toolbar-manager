using System.Diagnostics.CodeAnalysis;
using HarmonyLib;
using Sandbox.Game.Gui;
using Sandbox.Game.Screens.Helpers;
using Sandbox.Graphics.GUI;
using ClientPlugin.Gui;
using VRage.Game;
using VRage.Input;

namespace ClientPlugin.Patches;

[HarmonyPatch(typeof(MyGuiScreenGamePlay))]
[SuppressMessage("ReSharper", "UnusedMember.Global")]
// ReSharper disable once UnusedType.Global
public static class MyGuiScreenGamePlayPatch
{
    [HarmonyPrefix]
    [HarmonyPatch(nameof(MyGuiScreenGamePlay.HandleUnhandledInput))]
    public static bool HandleUnhandledInputPrefix()
    {
        var myInput = MyInput.Static;
        if (!myInput.IsNewKeyPressed(Config.Current.BlockSearchKey.Key))
            return true;

        if (MyGuiScreenGamePlay.ActiveGameplayScreen != null)
            return true;

        var toolbarType = MyToolbarComponent.CurrentToolbar?.ToolbarType;
        switch (toolbarType)
        {
            case MyToolbarType.Character:
            case MyToolbarType.BuildCockpit:
                break;
            default:
                return true;
        }

        OpenCubeBuilder();
        return false;
    }

    private static void OpenCubeBuilder()
    {
        if (!(MyGuiSandbox.CreateScreen(typeof(CustomToolbarConfigScreen), 0, null, null) is CustomToolbarConfigScreen dialog))
            return;

        MyGuiScreenGamePlay.ActiveGameplayScreen = dialog;
        MyGuiSandbox.AddScreen(dialog);
    }
}
