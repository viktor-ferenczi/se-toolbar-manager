using System;
using System.Linq;
using System.Text;
using ClientPlugin.Logic;
using Sandbox;
using Sandbox.Definitions;
using Sandbox.Game.Gui;
using Sandbox.Graphics.GUI;
using VRage.Game;
using VRage.Utils;
using VRageMath;

namespace ClientPlugin.Gui;

// Edits Config.Current.ItemGroups: the groups, their items and the order of the items
// ReSharper disable VirtualMemberCallInConstructor
public class ItemGroupsDialog : MyGuiScreenDebugBase
{
    private const float ColumnWidth = 0.3f;
    private const float ColumnGap = 0.02f;
    private const float TableTop = -0.31f;

    private MyGuiControlTable groupsTable,
        itemsTable,
        availableTable;
    private MyGuiControlButton renameButton,
        deleteButton,
        upButton,
        downButton,
        removeButton,
        addButton;

    public override string GetFriendlyName() => "ItemGroupsDialog";

    public ItemGroupsDialog()
        : base(
            new Vector2(0.5f, 0.5f),
            new Vector2(1f, 0.8f),
            MyGuiConstants.SCREEN_BACKGROUND_COLOR * MySandboxGame.Config.UIBkOpacity,
            true
        )
    {
        RecreateControls(true);

        CanBeHidden = true;
        CanHideOthers = true;
        CloseButtonEnabled = true;
    }

    private static ItemGroup[] Groups
    {
        get => Config.Current.ItemGroups;
        set => Config.Current.ItemGroups = value;
    }

    private ItemGroup SelectedGroup => groupsTable.SelectedRow?.UserData as ItemGroup;

    public override void RecreateControls(bool constructor)
    {
        base.RecreateControls(constructor);

        AddCaption("Tool Groups", Color.White.ToVector4(), new Vector2(0.0f, 0.003f));

        groupsTable = CreateTable(0, "GroupsTable", "Groups");
        itemsTable = CreateTable(1, "ItemsTable", "Items, first one available wins");
        availableTable = CreateTable(2, "AvailableTable", "Tools and weapons");

        groupsTable.ItemSelected += (_, _) => RefreshItems();
        itemsTable.ItemSelected += (_, _) => UpdateButtons();
        availableTable.ItemSelected += (_, _) => UpdateButtons();
        availableTable.ItemDoubleClicked += (_, _) => OnAdd(null);

        CreateButton(0, 0, "New", "Create a new group", OnNew);
        renameButton = CreateButton(0, 1, "Rename", "Rename the selected group", OnRename);
        deleteButton = CreateButton(0, 2, "Delete", "Delete the selected group", OnDelete);
        CreateButton(
            0,
            3,
            "Reset",
            $"Restore the default {ItemGroup.CreateDefault().Name} group",
            OnReset
        );
        upButton = CreateButton(1, 0, "Up", "Move the selected item up", _ => Move(-1));
        downButton = CreateButton(1, 1, "Down", "Move the selected item down", _ => Move(1));
        removeButton = CreateButton(
            1,
            2,
            "Remove",
            "Remove the selected item from the group",
            OnRemove
        );
        addButton = CreateButton(
            2,
            0,
            "Add",
            "Add the selected item to the end of the group",
            OnAdd
        );

        var closeButton = new MyGuiControlButton(
            visualStyle: MyGuiControlButtonStyleEnum.Default,
            text: new StringBuilder("Close"),
            onButtonClick: _ => CloseScreen()
        );
        closeButton.Name = "CloseButton";
        closeButton.Position = new Vector2(0f, 0.5f * DialogSize.Y - 0.06f);
        Controls.Add(closeButton);

        RefreshGroups(0);
    }

    private Vector2 DialogSize => m_size ?? Vector2.One;

    private static float ColumnLeft(int column) =>
        -1.5f * ColumnWidth - ColumnGap + column * (ColumnWidth + ColumnGap);

    private MyGuiControlTable CreateTable(int column, string name, string title)
    {
        var table = new MyGuiControlTable
        {
            Name = name,
            Position = new Vector2(ColumnLeft(column), TableTop),
            Size = new Vector2(ColumnWidth, 0.5f),
            OriginAlign = MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_TOP,
            ColumnsCount = 1,
            VisibleRowsCount = 12,
        };
        table.SetCustomColumnWidths(new[] { 1f });
        table.SetColumnName(0, new StringBuilder(title));
        Controls.Add(table);
        return table;
    }

    private MyGuiControlButton CreateButton(
        int column,
        int index,
        string text,
        string toolTip,
        Action<MyGuiControlButton> onClick
    )
    {
        const float step = 0.15f;
        var button = new MyGuiControlButton(
            visualStyle: MyGuiControlButtonStyleEnum.Small,
            originAlign: MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_TOP,
            text: new StringBuilder(text),
            onButtonClick: onClick
        );
        button.Name = text + "Button";
        // Below the table, whose height follows its row count
        button.Position = new Vector2(
            ColumnLeft(column) + index % 2 * step,
            groupsTable.Position.Y + groupsTable.Size.Y + 0.015f + index / 2 * 0.05f
        );
        button.SetToolTip(toolTip);
        Controls.Add(button);
        return button;
    }

    private void RefreshGroups(int selectIndex)
    {
        groupsTable.Clear();
        foreach (var group in Groups)
        {
            var row = new MyGuiControlTable.Row(group);
            row.AddCell(new MyGuiControlTable.Cell(group.Name));
            groupsTable.Add(row);
        }

        groupsTable.SelectedRowIndex =
            groupsTable.RowsCount == 0
                ? null
                : MathHelper.Clamp(selectIndex, 0, groupsTable.RowsCount - 1);
        RefreshItems();
    }

    private void RefreshItems(int? selectIndex = null)
    {
        itemsTable.Clear();
        availableTable.Clear();

        var group = SelectedGroup;
        if (group != null)
        {
            foreach (var definition in group.Definitions)
                itemsTable.Add(CreateItemRow(definition));

            // Only what can go on the character toolbar as a tool or weapon
            foreach (
                var definition in MyDefinitionManager
                    .Static.GetWeaponDefinitions()
                    .Where(d => d.Public && !group.Contains(d))
                    .OrderBy(d => d.DisplayNameText)
            )
                availableTable.Add(CreateItemRow(definition));
        }

        if (selectIndex.HasValue && itemsTable.RowsCount > 0)
            itemsTable.SelectedRowIndex = MathHelper.Clamp(
                selectIndex.Value,
                0,
                itemsTable.RowsCount - 1
            );

        UpdateButtons();
    }

    private static MyGuiControlTable.Row CreateItemRow(MyPhysicalItemDefinition definition)
    {
        var row = new MyGuiControlTable.Row(definition);
        row.AddCell(new MyGuiControlTable.Cell(definition.DisplayNameText));
        return row;
    }

    private void UpdateButtons()
    {
        var hasGroup = SelectedGroup != null;
        var item = itemsTable.SelectedRowIndex;
        renameButton.Enabled = hasGroup;
        deleteButton.Enabled = hasGroup;
        upButton.Enabled = item > 0;
        downButton.Enabled = item.HasValue && item < itemsTable.RowsCount - 1;
        removeButton.Enabled = item.HasValue;
        addButton.Enabled = hasGroup && availableTable.SelectedRowIndex.HasValue;
    }

    // Keeps the selection on the group being edited
    private void SetItems(ItemGroup group, string[] items, int selectItem)
    {
        group.Items = items;
        Config.Current.ItemGroups = Groups.ToArray();
        RefreshItems(selectItem);
    }

    // Item ids of the group as listed, dropping any whose definition is not loaded
    private static string[] ListedItems(ItemGroup group) =>
        group.Definitions.Select(d => d.Id.ToString()).ToArray();

    private void Move(int delta)
    {
        var group = SelectedGroup;
        var index = itemsTable.SelectedRowIndex;
        if (group == null || !index.HasValue)
            return;

        var items = ListedItems(group);
        var target = index.Value + delta;
        if (target < 0 || target >= items.Length)
            return;

        (items[index.Value], items[target]) = (items[target], items[index.Value]);
        SetItems(group, items, target);
    }

    private void OnRemove(MyGuiControlButton _)
    {
        var group = SelectedGroup;
        var index = itemsTable.SelectedRowIndex;
        if (group == null || !index.HasValue)
            return;

        var items = ListedItems(group).ToList();
        items.RemoveAt(index.Value);
        SetItems(group, items.ToArray(), index.Value);
    }

    private void OnAdd(MyGuiControlButton _)
    {
        var group = SelectedGroup;
        if (
            group == null
            || !(availableTable.SelectedRow?.UserData is MyPhysicalItemDefinition definition)
        )
            return;

        var items = ListedItems(group).Append(definition.Id.ToString()).ToArray();
        SetItems(group, items, items.Length - 1);
    }

    private void OnNew(MyGuiControlButton _)
    {
        MyGuiSandbox.AddScreen(
            new NameDialog(
                name =>
                {
                    Groups = Groups.Append(ItemGroup.CreateNew(name.Trim())).ToArray();
                    RefreshGroups(Groups.Length - 1);
                },
                "New tool group",
                "New Group"
            )
        );
    }

    private void OnRename(MyGuiControlButton _)
    {
        var group = SelectedGroup;
        if (group == null)
            return;

        var index = groupsTable.SelectedRowIndex ?? 0;
        MyGuiSandbox.AddScreen(
            new NameDialog(
                name =>
                {
                    group.Name = name.Trim();
                    Groups = Groups.ToArray();
                    RefreshGroups(index);
                },
                "Rename tool group",
                group.Name
            )
        );
    }

    private void OnDelete(MyGuiControlButton _)
    {
        var group = SelectedGroup;
        if (group == null)
            return;

        var index = groupsTable.SelectedRowIndex ?? 0;
        MyGuiSandbox.AddScreen(
            MyGuiSandbox.CreateMessageBox(
                buttonType: MyMessageBoxButtonsType.YES_NO,
                messageText: new StringBuilder(
                    $"Are you sure to delete this tool group?\r\n\r\n{group.Name}\r\n\r\nIts toolbar slots stop working."
                ),
                messageCaption: new StringBuilder("Confirmation"),
                callback: result =>
                {
                    if (result != MyGuiScreenMessageBox.ResultEnum.YES)
                        return;

                    Groups = Groups.Where(g => g != group).ToArray();
                    RefreshGroups(index);
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
        {
            index = 0;
            groups.Insert(0, ItemGroup.CreateDefault());
        }
        else
        {
            groups[index] = ItemGroup.CreateDefault();
        }

        Groups = groups.ToArray();
        RefreshGroups(index);
    }

    protected override void OnClosed()
    {
        base.OnClosed();
        Config.Save();

        // Show the changes in the G menu below
        var toolbarConfig = MyScreenManager
            .Screens.OfType<MyGuiScreenToolbarConfigBase>()
            .FirstOrDefault();
        toolbarConfig?.OnCategoriesItemClicked(toolbarConfig.m_categoriesListbox);
    }
}
