using System;
using System.Linq;
using System.Text;
using ClientPlugin.Logic;
using ClientPlugin.Patches;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.Game.Gui;
using Sandbox.Game.GUI;
using Sandbox.Game.Screens.Helpers;
using Sandbox.Game.World;
using Sandbox.Graphics.GUI;
using VRage.Audio;
using VRage.Game;
using VRage.Input;
using VRage.Utils;
using VRageMath;

namespace ClientPlugin.Gui;

// Edit mode of the G menu: the staging panel shows the items of one group instead. Items come
// from the game's own block grid and variant grid by drag and drop or double click, are moved
// by dragging them inside the group and removed by right click. Reading order is the priority.
// MyGuiScreenToolbarConfigBasePatch lays the panel out, this class fills it and handles input.
public class GroupEditor
{
    // The G menu in edit mode, so a newly opened one starts with the staging area again
    private static MyGuiScreenToolbarConfigBase editingScreen;
    private static string selectedGroupId;

    public static bool IsEditing(MyGuiScreenToolbarConfigBase screen) =>
        screen != null && screen == editingScreen;

    public static GroupEditor Current { get; private set; }

    public static void Toggle(MyGuiScreenToolbarConfigBase screen)
    {
        editingScreen = IsEditing(screen) ? null : screen;
        Current = null;
        Config.Save();
        screen.RecreateControls(false);
    }

    // The plugin settings have no G menu behind them, so this opens one, in a world only
    public static void OpenFromSettings()
    {
        if (
            MySession.Static?.LocalCharacter == null
            || MyToolbarComponent.CurrentToolbar?.ToolbarType != MyToolbarType.Character
        )
        {
            MyGuiSandbox.AddScreen(
                MyGuiSandbox.CreateMessageBox(
                    messageText: new StringBuilder(
                        "Groups are edited in the G menu. Load a world, open the G menu and press TG."
                    ),
                    messageCaption: new StringBuilder("Groups")
                )
            );
            return;
        }

        // The settings and the plugin list would stay below the G menu, and the HUD drawn
        // over it, so they go first and the G menu opens in the next frame
        foreach (var other in MyScreenManager.Screens.ToList())
        {
            if (!(other is MyGuiScreenGamePlay) && !(other is MyGuiScreenHudBase))
                other.CloseScreen();
        }

        MyEntities.InvokeLater(() =>
        {
            var screen = MyGuiScreenGamePlayPatch.OpenCubeBuilder();
            if (screen != null && !IsEditing(screen))
                Toggle(screen);
        });
    }

    private readonly MyGuiScreenToolbarConfigBase screen;
    private readonly MyGuiControlCombobox groupSelector;
    private readonly MyGuiControlLabel groupLabel;
    private readonly MyGuiControlButton newButton,
        renameButton,
        deleteButton,
        resetButton,
        doneButton;

    public readonly MyGuiControlGrid Grid;

    // Two rows of controls above the group's items
    public float HeaderHeight => 2f * RowHeight + Spacing;
    private const float Spacing = 0.006f;
    private float RowHeight => Math.Max(doneButton.Size.Y, groupSelector.Size.Y);

    public GroupEditor(MyGuiScreenToolbarConfigBase screen, Vector4 itemBackgroundColorMask)
    {
        this.screen = screen;
        Current = this;

        Grid = new MyGuiControlGrid
        {
            Name = "GroupItemsGrid",
            VisualStyle = MyGuiControlGridStyleEnum.Toolbar,
            ColorMask = Vector4.One,
            ItemBackgroundColorMask = itemBackgroundColorMask,
            ColumnsCount = 10,
            RowsCount = 10,
        };

        groupLabel = new MyGuiControlLabel(text: "Group")
        {
            OriginAlign = MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER,
            Position = new Vector2(0.005f, 0f),
        };
        groupSelector = new MyGuiControlCombobox(size: new Vector2(0.25f, 0.04f))
        {
            Name = "GroupSelector",
            OriginAlign = MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER,
        };
        doneButton = CreateButton(
            "Done",
            "Leave edit mode and show the staging area again",
            _ => Toggle(screen)
        );
        newButton = CreateButton("New", "Create a new group", OnNew);
        renameButton = CreateButton("Rename", "Rename the selected group", OnRename);
        deleteButton = CreateButton("Delete", "Delete the selected group", OnDelete);
        resetButton = CreateButton(
            "Reset default",
            $"Restore the default {ItemGroup.CreateDefault().Name} group",
            OnReset
        );
    }

    private static MyGuiControlButton CreateButton(
        string text,
        string toolTip,
        Action<MyGuiControlButton> onClick
    )
    {
        var button = new MyGuiControlButton(
            visualStyle: MyGuiControlButtonStyleEnum.Small,
            originAlign: MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER,
            text: new StringBuilder(text),
            onButtonClick: onClick
        );
        button.Name = text.Replace(" ", "") + "GroupButton";
        button.SetToolTip(toolTip);
        // Small has a fixed SizeOverride, reapplied on resize, hover and focus.
        var style = MyGuiControlButton.GetVisualStyle(MyGuiControlButtonStyleEnum.Small);
        button.CustomStyle = new MyGuiControlButton.StyleDefinition
        {
            NormalTexture = style.NormalTexture,
            HighlightTexture = style.HighlightTexture,
            FocusTexture = style.FocusTexture,
            ActiveTexture = style.ActiveTexture,
            NormalFont = style.NormalFont,
            HighlightFont = style.HighlightFont,
            Padding = style.Padding,
            TextColorFocus = style.TextColorFocus,
        };
        return button;
    }

    // Places the header controls in the area above the panel, given in absolute coordinates
    public void AddHeader(Vector2 topLeft, float width)
    {
        var origin = screen.GetPositionAbsoluteCenter();
        var row1 = topLeft + new Vector2(0.01f, RowHeight / 2f);
        var row2 = row1 + new Vector2(0f, RowHeight + Spacing);

        groupLabel.Position = row1 - origin;
        groupSelector.Position = row1 + new Vector2(0.06f, 0f) - origin;
        doneButton.Position = row1 + new Vector2(width - doneButton.Size.X - 0.02f, 0f) - origin;

        var step = (width + Spacing) / 5f;
        var buttons = new[] { newButton, renameButton, deleteButton, resetButton };
        for (var i = 0; i < buttons.Length; i++)
        {
            buttons[i].Size = new Vector2(step - Spacing, buttons[i].Size.Y);
            buttons[i].Position = row2 + new Vector2(i * step, 0f) - origin;
        }

        foreach (
            var control in new MyGuiControlBase[] { groupLabel, groupSelector, doneButton }.Concat(
                buttons
            )
        )
            screen.Controls.Add(control);

        groupSelector.ItemSelected += OnGroupSelected;
        screen.m_dragAndDrop.ItemDropped += OnDrop;
        Grid.ItemClicked += OnItemClicked;
        Grid.ItemDragged += OnItemDragged;

        RefreshGroups();
    }

    private static ItemGroup[] Groups
    {
        get => Config.Current.ItemGroups;
        set
        {
            Config.Current.ItemGroups = value;
            Config.Save();
        }
    }

    private static ItemGroup SelectedGroup => Config.Current.FindItemGroup(selectedGroupId);

    private void RefreshGroups()
    {
        if (SelectedGroup == null)
            selectedGroupId = Groups.FirstOrDefault()?.Id;

        groupSelector.ClearItems();
        for (var i = 0; i < Groups.Length; i++)
            groupSelector.AddItem(i, Groups[i].Name, sort: false);

        var selected = Array.FindIndex(Groups, g => g.Id == selectedGroupId);
        if (selected >= 0)
            groupSelector.SelectItemByKey(selected, sendEvent: false);

        renameButton.Enabled = selected >= 0;
        deleteButton.Enabled = selected >= 0;
        RefreshItems();

        // The groups listed in the block grid change with their names and items
        screen.OnCategoriesItemClicked(screen.m_categoriesListbox);
    }

    private void RefreshItems()
    {
        Grid.SetItemsToDefault();
        var members = SelectedGroup?.Members;
        if (members == null)
            return;

        // One free cell after the last item to drop new ones to
        Grid.RowsCount = Math.Max(
            Config.Current.StagingAreaRowCount,
            members.Count / Grid.ColumnsCount + 1
        );
        for (var i = 0; i < members.Count; i++)
        {
            var definition = members[i].Definition;
            var item = new MyGuiGridItem(
                members[i].Item.Icons,
                null,
                definition.DisplayNameText,
                new MyGuiScreenToolbarConfigBase.GridItemUserData
                {
                    ItemData = () => MyToolbarItemFactory.ObjectBuilderFromDefinition(definition),
                }
            );
            item.AddText(
                new StringBuilder($"{i + 1}"),
                MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_TOP
            );
            Grid.SetItemAt(i, item);
        }
    }

    private void OnGroupSelected()
    {
        var index = (int)groupSelector.GetSelectedKey();
        if (index < 0 || index >= Groups.Length)
            return;

        selectedGroupId = Groups[index].Id;
        RefreshItems();
    }

    // The definition behind a dragged item: a grid item of the G menu or an item of the toolbar
    private static MyDefinitionBase DefinitionOf(object userData)
    {
        var builder = userData switch
        {
            MyGuiScreenToolbarConfigBase.GridItemUserData gridItem => gridItem.ItemData?.Invoke(),
            MyToolbarItem toolbarItem => toolbarItem.GetObjectBuilder(),
            _ => null,
        };
        if (!(builder is MyObjectBuilder_ToolbarItemDefinition definitionBuilder))
            return null;

        var definition = ItemGroup.FindDefinition(definitionBuilder.DefinitionId.ToString());
        return definition != null && ItemGroup.CreateMemberItem(definition) != null
            ? definition
            : null;
    }

    // Puts the item at the index, moving it there if the group already has it
    private void Insert(MyDefinitionBase definition, int index)
    {
        var group = SelectedGroup;
        if (group == null)
            return;

        if (definition == null)
        {
            MyGuiAudio.PlaySound(MyGuiSounds.HudUnable);
            return;
        }

        var id = definition.Id.ToString();
        var items = ListedItems(group).Where(i => i != id).ToList();
        items.Insert(Math.Min(index, items.Count), id);
        SetItems(group, items);
        MyGuiAudio.PlaySound(MyGuiSounds.HudItem);
    }

    // Double click in the block grid while editing
    public void Append(MyObjectBuilder_ToolbarItem data) =>
        Insert(
            DefinitionOf(
                new MyGuiScreenToolbarConfigBase.GridItemUserData { ItemData = () => data }
            ),
            int.MaxValue
        );

    // Item ids as listed, dropping any whose definition is not loaded
    private static string[] ListedItems(ItemGroup group) =>
        group.Members.Select(m => m.Definition.Id.ToString()).ToArray();

    private void SetItems(ItemGroup group, System.Collections.Generic.IEnumerable<string> items)
    {
        group.Items = items.ToArray();
        Groups = Groups;
        RefreshItems();
    }

    private void OnDrop(object sender, MyDragAndDropEventArgs eventArgs)
    {
        if (
            eventArgs.DropTo == null
            || eventArgs.DropTo.Grid != Grid
            || eventArgs.Item?.UserData == null
        )
            return;

        Insert(DefinitionOf(eventArgs.Item.UserData), eventArgs.DropTo.ItemIndex);
    }

    private void OnItemClicked(MyGuiControlGrid grid, MyGuiControlGrid.EventArgs eventArgs)
    {
        var group = SelectedGroup;
        if (group == null || eventArgs.Button != MySharedButtonsEnum.Secondary)
            return;

        var items = ListedItems(group).ToList();
        if (eventArgs.ItemIndex < 0 || eventArgs.ItemIndex >= items.Count)
            return;

        items.RemoveAt(eventArgs.ItemIndex);
        SetItems(group, items);
    }

    private void OnItemDragged(MyGuiControlGrid grid, MyGuiControlGrid.EventArgs eventArgs)
    {
        var item = grid.GetItemAt(eventArgs.ItemIndex);
        if (item == null)
            return;

        screen.m_dragAndDrop.StartDragging(
            MyDropHandleType.MouseRelease,
            eventArgs.Button,
            item,
            new MyDragAndDropInfo { Grid = grid, ItemIndex = eventArgs.ItemIndex },
            false
        );
        grid.HideToolTip();
    }

    private void OnNew(MyGuiControlButton _)
    {
        MyGuiSandbox.AddScreen(
            new NameDialog(
                name =>
                {
                    var group = ItemGroup.CreateNew(name.Trim());
                    Groups = Groups.Append(group).ToArray();
                    selectedGroupId = group.Id;
                    RefreshGroups();
                },
                "New group",
                "New Group"
            )
        );
    }

    private void OnRename(MyGuiControlButton _)
    {
        var group = SelectedGroup;
        if (group == null)
            return;

        MyGuiSandbox.AddScreen(
            new NameDialog(
                name =>
                {
                    group.Name = name.Trim();
                    Groups = Groups;
                    RefreshGroups();
                },
                "Rename group",
                group.Name
            )
        );
    }

    private void OnDelete(MyGuiControlButton _)
    {
        var group = SelectedGroup;
        if (group == null)
            return;

        MyGuiSandbox.AddScreen(
            MyGuiSandbox.CreateMessageBox(
                buttonType: MyMessageBoxButtonsType.YES_NO,
                messageText: new StringBuilder(
                    $"Are you sure to delete this group?\r\n\r\n{group.Name}\r\n\r\nIts toolbar slots stop working."
                ),
                messageCaption: new StringBuilder("Confirmation"),
                callback: result =>
                {
                    if (result != MyGuiScreenMessageBox.ResultEnum.YES)
                        return;

                    Groups = Groups.Where(g => g != group).ToArray();
                    selectedGroupId = null;
                    RefreshGroups();
                }
            )
        );
    }

    // Restores the default group in place, or adds it back first if it was deleted
    private void OnReset(MyGuiControlButton _)
    {
        var groups = Groups.ToList();
        var index = groups.FindIndex(g => g.Id == ItemGroup.DefaultId);
        if (index < 0)
            groups.Insert(0, ItemGroup.CreateDefault());
        else
            groups[index] = ItemGroup.CreateDefault();

        Groups = groups.ToArray();
        selectedGroupId = ItemGroup.DefaultId;
        RefreshGroups();
    }
}
