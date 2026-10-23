using ClientPlugin.Logic;
using HarmonyLib;
using Sandbox.Game.Entities;
using VRage.Input;

namespace ClientPlugin.Patches;

// ReSharper disable UnusedMember.Local
// ReSharper disable UnusedType.Global
// ReSharper disable InconsistentNaming

// The fitting block button runs before the cube builder's input. The middle button also
// paints the aimed block in the game, which a press selecting a block must not do.
[HarmonyPatch(typeof(MyCubeBuilder))]
public static class FittingBlockPatch
{
    private static bool holdingButton;

    [HarmonyPrefix]
    [HarmonyPatch(nameof(MyCubeBuilder.HandleGameInput))]
    private static bool HandleGameInputPrefix(ref bool __result)
    {
        FittingBlock.ApplyRotation();

        var input = MyInput.Static;
        if (holdingButton && !input.IsMousePressed(MyMouseButtonsEnum.Middle))
            holdingButton = false;

        if (!FittingBlock.IsPressed(input))
            return true;

        var item = ItemGroupScrollPatch.GroupInUse(out var character);
        if (item == null || !FittingBlock.Select(item, character))
            return true;

        holdingButton = input.IsMousePressed(MyMouseButtonsEnum.Middle);
        __result = true;
        return false;
    }

    [HarmonyPrefix]
    [HarmonyPatch("Change")]
    private static bool ChangePrefix() => !holdingButton;
}
