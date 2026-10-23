using System.Collections.Generic;
using System.Linq;
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
using VRage.Input;
using VRageMath;

namespace ClientPlugin.Patches;

// ReSharper disable UnusedMember.Local
// ReSharper disable UnusedType.Global
// ReSharper disable InconsistentNaming

[HarmonyPatch(typeof(MyToolbar))]
public static class ItemGroupActivationPatch
{
    [HarmonyPrefix]
    [HarmonyPatch(nameof(MyToolbar.ActivateItemAtIndex))]
    private static bool ActivateItemAtIndexPrefix(
        MyToolbar __instance,
        int index,
        bool checkIfWantsToBeActivated,
        ref bool __result
    )
    {
        if (!(__instance.GetItemAtIndex(index) is ItemGroupToolbarItem item))
            return true;

        if (
            __instance.StagedSelectedSlot.HasValue
            && __instance.SlotToIndex(__instance.StagedSelectedSlot.Value) != index
        )
            __instance.StagedSelectedSlot = null;

        __result = false;
        if (!item.Enabled || (checkIfWantsToBeActivated && !item.WantsToBeActivated))
            return false;

        // Unselect would holster the current tool before the group can identify it.
        // Another slot containing that tool can be selected instead of this group.
        __result = item.Activate();
        __instance.OnItemActivated?.Invoke(item);
        return false;
    }
}

[HarmonyPatch(typeof(MyToolbarComponent))]
public static class ItemGroupScrollPatch
{
    [HarmonyPrefix]
    [HarmonyPatch(nameof(MyToolbarComponent.HandleInput))]
    private static bool HandleInputPrefix()
    {
        var input = MyInput.Static;
        var delta = input.DeltaMouseScrollWheelValue();
        var toolbar = MyToolbarComponent.CurrentToolbar;
        if (
            delta == 0
            || toolbar?.ToolbarType != MyToolbarType.Character
            || !toolbar.CanPlayerActivateItems
            || !(MySession.Static?.ControlledEntity is MyCharacter character)
            || MySession.Static.IsCameraUserControlledSpectator()
            || MyGuiScreenGamePlay.DisableInput
            || MyGuiScreenGamePlay.ActiveGameplayScreen != null
            || MyScreenManager.GetScreenWithFocus() != MyGuiScreenGamePlay.Static
            || input.IsAnyCtrlKeyPressed()
            || input.IsAnyShiftKeyPressed()
            || input.IsAnyAltKeyPressed()
        )
            return true;

        // Prefer the selected group; an ordinary tool slot may also be selected.
        var item = toolbar.SelectedItem as ItemGroupToolbarItem;
        for (var slot = 0; slot < toolbar.SlotCount; slot++)
        {
            var candidate = item ?? toolbar.GetSlotItem(slot) as ItemGroupToolbarItem;
            if (candidate == null)
                continue;

            var group = Config.Current.FindItemGroup(candidate.GroupId);
            group?.Update(character, MySession.Static.LocalPlayerId);
            if (group?.Held?.Item is MyToolbarItemWeapon)
            {
                candidate.Activate(delta > 0 ? 1 : -1);
                return false;
            }

            if (item != null)
                break;
        }

        return true;
    }
}

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
        DisplayNameString = "Groups",
        ItemIds = new HashSet<string>(),
        SearchBlocks = false,
        IsToolCategory = true,
    };

    private static void OnGroupTileReleased(
        MyGuiScreenToolbarConfigBase __instance,
        MyGuiControlGrid sender,
        MyGuiControlGrid.EventArgs eventArgs
    )
    {
        if (
            !IsCharacterToolbar(__instance)
            || eventArgs.Button != MySharedButtonsEnum.Primary
            || __instance.m_dragAndDrop.IsActive()
            || !__instance.m_categoriesListbox.SelectedItems.Any(item => item.UserData == Category)
        )
            return;

        var tile = sender.TryGetItemAt(eventArgs.RowIndex, eventArgs.ColumnIndex);
        if (
            tile?.UserData is not MyGuiScreenToolbarConfigBase.GridItemUserData data
            || !ItemGroupToolbarItem.IsMarker(data.ItemData(), out var groupId)
        )
            return;

        GroupEditor.Open(__instance, groupId);
    }

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
        var playerId = MySession.Static.LocalPlayerId;
        foreach (var group in Config.Current.ItemGroups)
        {
            var matches = searchCondition is MySearchByCategoryCondition byCategory
                ? byCategory.SelectedCategories.Contains(Category)
                : searchCondition == null || searchCondition.MatchesCondition(group.Name);
            if (!matches || group.Members.Count == 0)
                continue;

            // Shows the item using the group would equip, but stays enabled without any,
            // so the group can be put on the toolbar before getting the tools
            var item = new ItemGroupToolbarItem(group.Id);
            item.Update(character, playerId);
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

        // Wait for release so pressing a tile can still begin a drag to the toolbar.
        __instance.m_gridBlocks.ItemReleased += (grid, args) =>
            OnGroupTileReleased(__instance, grid, args);

        var button = new MyGuiControlButton
        {
            Text = "TG",
            Name = "OpenItemGroupsButton",
            VisualStyle = MyGuiControlButtonStyleEnum.Square,
            Position = new Vector2(-0.405f, 0.425f),
        };
        button.SetToolTip(
            GroupEditor.IsEditing(__instance)
                ? (Config.Current.EnableStagingArea ? "Return to staging" : "Close group editor")
                : "Edit groups"
        );
        button.Checked = GroupEditor.IsEditing(__instance);
        button.ButtonClicked += _ => GroupEditor.Toggle(__instance);
        __instance.Controls.Add(button);
    }

    // The game greys out tools, weapons and blocks the player doesn't have or hasn't researched
    // and refuses to drag or double click them. While editing a group they can still be added,
    // the game decides at use time whether the group's item is usable.
    private static bool IsGreyedOutWhileEditing(
        MyGuiScreenToolbarConfigBase screen,
        MyGuiGridItem item
    ) => item is { Enabled: false } && GroupEditor.IsEditing(screen);

    // The drag handlers, not the StartDragging they call: the JIT may inline that small
    // method into them, which would skip a patch on it
    [HarmonyPrefix]
    [HarmonyPatch(nameof(MyGuiScreenToolbarConfigBase.OnGridDrag))]
    private static bool OnGridDragPrefix(
        MyGuiScreenToolbarConfigBase __instance,
        MyGuiControlGrid sender,
        MyGuiControlGrid.EventArgs eventArgs
    )
    {
        var item = sender.GetItemAt(eventArgs.ItemIndex);
        if (!IsGreyedOutWhileEditing(__instance, item))
            return true;

        __instance.m_dragAndDrop.StartDragging(
            MyDropHandleType.MouseRelease,
            eventArgs.Button,
            item,
            new MyDragAndDropInfo { Grid = sender, ItemIndex = eventArgs.ItemIndex },
            false
        );
        sender.HideToolTip();
        return false;
    }

    [HarmonyPrefix]
    [HarmonyPatch("OnResearchGraphItemDragged")]
    private static bool OnResearchGraphItemDraggedPrefix(
        MyGuiScreenToolbarConfigBase __instance,
        object sender,
        MyGuiGridItem item
    )
    {
        if (!IsGreyedOutWhileEditing(__instance, item))
            return true;

        __instance.m_dragAndDrop.StartDragging(
            MyDropHandleType.MouseRelease,
            MySharedButtonsEnum.Primary,
            item,
            new MyDragAndDropInfo(),
            false
        );
        (sender as MyGuiControlResearchGraph)?.HideToolTip();
        return false;
    }

    // Only a drop on the group grid takes a greyed out item, the toolbar keeps refusing it
    [HarmonyPrefix]
    [HarmonyPriority(Priority.First)]
    [HarmonyPatch(nameof(MyGuiScreenToolbarConfigBase.OnDragAndDropOnDrop))]
    private static bool OnDragAndDropOnDropPrefix(
        MyGuiScreenToolbarConfigBase __instance,
        MyDragAndDropEventArgs eventArgs
    ) => !IsGreyedOutWhileEditing(__instance, eventArgs.Item);

    [HarmonyPrefix]
    [HarmonyPatch(
        "OnGridItemDoubleClicked",
        typeof(MyGuiControlGrid),
        typeof(MyGuiControlGrid.EventArgs),
        typeof(bool)
    )]
    private static bool OnGridItemDoubleClickedPrefix(
        MyGuiScreenToolbarConfigBase __instance,
        MyGuiControlGrid sender,
        MyGuiControlGrid.EventArgs eventArgs
    ) =>
        !AppendGreyedOut(
            __instance,
            sender.TryGetItemAt(eventArgs.RowIndex, eventArgs.ColumnIndex)
        );

    [HarmonyPrefix]
    [HarmonyPatch("OnResearchGraphItemDoubleClicked")]
    private static bool OnResearchGraphItemDoubleClickedPrefix(
        MyGuiScreenToolbarConfigBase __instance,
        object sender
    ) => !AppendGreyedOut(__instance, (sender as MyGuiControlResearchGraph)?.SelectedItem);

    private static bool AppendGreyedOut(MyGuiScreenToolbarConfigBase screen, MyGuiGridItem item)
    {
        if (!IsGreyedOutWhileEditing(screen, item))
            return false;

        var data = (
            item.UserData as MyGuiScreenToolbarConfigBase.GridItemUserData
        )?.ItemData?.Invoke();
        if (data != null && !(data is MyObjectBuilder_ToolbarItemEmpty))
            GroupEditor.Current?.Append(data);
        return true;
    }

    // Groups go only on the character's own toolbar, not on block toolbars
    // and not on the single gamepad slot
    public static bool IsCharacterToolbar(MyGuiScreenToolbarConfigBase screen) =>
        screen.m_character != null
        && !screen.m_gamepadSlot.HasValue
        && MyToolbarComponent.CurrentToolbar?.ToolbarType == MyToolbarType.Character;
}
