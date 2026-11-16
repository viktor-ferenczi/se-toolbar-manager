using System.Collections.Generic;
using HarmonyLib;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.Game.Screens.Helpers;
using VRage.Game;

namespace ClientPlugin.Patches;

// ReSharper disable UnusedMember.Local
// ReSharper disable UnusedType.Global
// ReSharper disable InconsistentNaming

// The cube builder remembers the variant used last per vanilla block group, and a slot
// of the group's block equips that one. With the option on, the slot goes back to its own
// block once its block is not in the hand any more, so it equips the block its icon shows.
[HarmonyPatch(typeof(MyToolbar))]
public static class VanillaGroupSlotPatch
{
    private static List<MyCubeBlockDefinition> inUse = new();
    private static List<MyCubeBlockDefinition> found = new();

    public static void OnSessionLoading() => inUse.Clear();

    [HarmonyPostfix]
    [HarmonyPatch(nameof(MyToolbar.Update))]
    private static void UpdatePostfix(MyToolbar __instance)
    {
        if (
            __instance.ToolbarType != MyToolbarType.Character
            || __instance != MyToolbarComponent.CurrentToolbar
        )
            return;

        // Every page, so switching pages with the block in hand doesn't count as leaving it
        found.Clear();
        for (var i = 0; i < __instance.ItemCount; i++)
        {
            if (
                __instance.GetItemAtIndex(i)
                    is MyToolbarItemCubeBlock { WantsToBeSelected: true } item
                && item.BlockDefinition.BlockStages is { Length: > 0 }
            )
                found.Add(item.BlockDefinition);
        }

        if (
            Config.Current.ResetVanillaGroupSlots
            && MyCubeBuilder.Static?.CubeBuilderState is { } state
        )
        {
            foreach (var block in inUse)
            {
                if (!found.Contains(block))
                    state.SetCurrentBlockForBlockVariantGroup(
                        MyDefinitionManager.Static.GetDefinitionGroup(block.BlockPairName)
                    );
            }
        }

        (inUse, found) = (found, inUse);
    }
}
