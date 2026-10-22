using System.Collections.Generic;
using ClientPlugin.Gui;
using ClientPlugin.Logic;
using HarmonyLib;
using Sandbox.Definitions;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.Gui;
using Sandbox.Game.Multiplayer;
using Sandbox.Game.Screens;
using Sandbox.Game.Screens.Helpers;
using Sandbox.Game.World;
using Sandbox.Graphics.GUI;
using VRage.Game;
using VRageMath;

namespace ClientPlugin.Patches;

// ReSharper disable UnusedMember.Local
// ReSharper disable UnusedType.Global
// ReSharper disable InconsistentNaming

// Turns the saved form of an item group slot back into the group, wherever the game or
// the profiles create toolbar items: world load, multiplayer join, G menu, profile load.
[HarmonyPatch(typeof(MyToolbarItemFactory))]
public static class MyToolbarItemFactoryPatch
{
    [HarmonyPrefix]
    [HarmonyPatch(nameof(MyToolbarItemFactory.CreateToolbarItem))]
    private static bool CreateToolbarItemPrefix(
        MyObjectBuilder_ToolbarItem data,
        ref MyToolbarItem __result
    )
    {
        if (!ItemGroupToolbarItem.IsMarker(data, out var groupId))
            return true;

        __result = new ItemGroupToolbarItem(groupId);
        return false;
    }
}

[HarmonyPatch(typeof(MyCharacter))]
public static class MyCharacterPatch
{
    // The game sends only definition based items to the server's copy of the player's
    // toolbar, which is the one saved with the world and given back on loading it.
    [HarmonyPrefix]
    [HarmonyPatch(nameof(MyCharacter.Toolbar_ItemChanged))]
    private static bool ToolbarItemChangedPrefix(MyToolbar toolbar, MyToolbar.IndexArgs index)
    {
        if (!(toolbar.GetItemAtIndex(index.ItemIndex) is ItemGroupToolbarItem item))
            return true;

        var player = MySession.Static.LocalHumanPlayer;
        if (player == null)
            return false;

        if (Sync.IsServer)
        {
            // Offline or hosting: the server's copy is local, set the group there directly
            MySession
                .Static.Toolbars.TryGetPlayerToolbar(player.Id)
                ?.SetItemAtIndex(index.ItemIndex, new ItemGroupToolbarItem(item.GroupId));
        }
        else
        {
            // A vanilla server can't keep the group, so it shouldn't keep the replaced item either
            MyToolBarCollection.RequestClearSlot(player.Id, index.ItemIndex);
        }

        return false;
    }
}

[HarmonyPatch(typeof(MyGuiScreenToolbarConfigBase))]
public static class ItemGroupsToolbarConfigPatch
{
    // "Groups" is the last of the game's forced category order, right after the character's
    // tools, weapons and animations. The character toolbar has no terminal block groups.
    private static readonly MyGuiBlockCategoryDefinition Category = new MyGuiBlockCategoryDefinition
    {
        Name = "Groups",
        DisplayNameString = "Tool Groups",
        ItemIds = new HashSet<string>(),
        SearchBlocks = false,
        IsToolCategory = true,
    };

    [HarmonyPostfix]
    [HarmonyPatch(nameof(MyGuiScreenToolbarConfigBase.RecreateBlockCategories))]
    private static void RecreateBlockCategoriesPostfix(
        MyGuiScreenToolbarConfigBase __instance,
        SortedDictionary<string, MyGuiBlockCategoryDefinition> categories
    )
    {
        if (IsCharacterToolbar(__instance) && !categories.ContainsKey(Category.Name))
            categories.Add(Category.Name, Category);
    }

    [HarmonyPostfix]
    [HarmonyPatch("AddToolsAndAnimations")]
    private static void AddToolsAndAnimationsPostfix(
        MyGuiScreenToolbarConfigBase __instance,
        IMySearchCondition searchCondition
    )
    {
        if (!IsCharacterToolbar(__instance))
            return;

        var character = __instance.m_character;
        foreach (var group in Config.Current.ItemGroups)
        {
            var matches = searchCondition is MySearchByCategoryCondition byCategory
                ? byCategory.SelectedCategories.Contains(Category)
                : searchCondition == null || searchCondition.MatchesCondition(group.Name);
            if (!matches || group.Definitions.Count == 0)
                continue;

            // Shows the item using the group would equip, but stays enabled without any,
            // so the group can be put on the toolbar before getting the tools
            var item = new ItemGroupToolbarItem(group.Id);
            item.Update(character);
            var gridItem = new MyGuiGridItem(
                item.Icons,
                null,
                group.Name,
                new MyGuiScreenToolbarConfigBase.GridItemUserData
                {
                    ItemData = () => ItemGroupToolbarItem.CreateMarker(group.Id),
                }
            );
            gridItem.SubIcon2 = item.SubIcon;
            __instance.m_gridBlocks.Add(gridItem);
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(MyGuiScreenToolbarConfigBase.RecreateControls))]
    private static void RecreateControlsPostfix(MyGuiScreenToolbarConfigBase __instance)
    {
        if (!IsCharacterToolbar(__instance))
            return;

        var button = new MyGuiControlButton
        {
            Text = "TG",
            Name = "OpenItemGroupsButton",
            VisualStyle = MyGuiControlButtonStyleEnum.Square,
            Position = new Vector2(-0.41f, 0.425f),
        };
        button.SetToolTip("Edit tool groups");
        button.ButtonClicked += _ => MyGuiSandbox.AddScreen(new ItemGroupsDialog());
        __instance.Controls.Add(button);
    }

    // Groups go only on the character's own toolbar, not on block toolbars
    // and not on the single gamepad slot
    private static bool IsCharacterToolbar(MyGuiScreenToolbarConfigBase screen) =>
        screen.m_character != null
        && !screen.m_gamepadSlot.HasValue
        && MyToolbarComponent.CurrentToolbar?.ToolbarType == MyToolbarType.Character;
}
