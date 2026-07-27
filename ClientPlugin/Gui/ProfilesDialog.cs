using System;
using System.Linq;
using System.Text;
using LitJson;
using Sandbox;
using Sandbox.Game.Gui;
using Sandbox.Game.Screens.Helpers;
using Sandbox.Graphics.GUI;
using ClientPlugin.Extensions;
using ClientPlugin.Logic;
using VRage.Game;
using VRage.Utils;
using VRageMath;


namespace ClientPlugin.Gui;

// ReSharper disable VirtualMemberCallInConstructor
public class ProfilesDialog : MyGuiScreenDebugBase
{
    private MyGuiControlTable profilesTable;
    private MyGuiControlSearchBox searchBox;
    private MyGuiControlButton newButton, updateButton, loadButton, mergeButton, renameButton, deleteButton, closeButton;
    private readonly int[] usedSlotCounts = new int[9];
    private readonly ProfileStorage storage;

    public override string GetFriendlyName() => "ProfilesDialog";

    public ProfilesDialog(MyToolbar currentToolbar)
        : base(new Vector2(0.5f, 0.5f), new Vector2(1f, 0.8f), MyGuiConstants.SCREEN_BACKGROUND_COLOR * MySandboxGame.Config.UIBkOpacity, true)
    {
        storage = new ProfileStorage(currentToolbar);

        RecreateControls(true);

        CanBeHidden = true;
        CanHideOthers = true;
        CloseButtonEnabled = true;
    }

    public override void RecreateControls(bool constructor)
    {
        base.RecreateControls(constructor);

        AddCaption("Toolbar Manager", Color.White.ToVector4(), new Vector2(0.0f, 0.003f));

        CreateButtons();
        CreateTable();
        RefreshTableRows();
    }

    private Vector2 DialogSize => m_size ?? Vector2.One;

    private void CreateTable()
    {
        profilesTable = new MyGuiControlTable
        {
            Position = new Vector2(0.001f, -0.5f * DialogSize.Y + 0.08f),
            Size = new Vector2(0.9f * DialogSize.X, DialogSize.Y - 0.1f),
            OriginAlign = MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_TOP,
            ColumnsCount = 10,
            VisibleRowsCount = 17,
        };

        var q = 0.76f;
        var w = 0.22f / 9f;
        profilesTable.SetCustomColumnWidths(new[] { q, w, w, w, w, w, w, w, w, w });
        profilesTable.SetColumnName(0, new StringBuilder("Name"));
        profilesTable.SetColumnComparison(0, CellTextComparison);
        for (var i = 1; i < 10; i++)
            profilesTable.SetColumnName(i, new StringBuilder($"{i}"));
        profilesTable.SortByColumn(0);

        profilesTable.ItemSelected += OnItemSelected;
        profilesTable.ItemDoubleClicked += OnItemDoubleClicked;

        searchBox = new MyGuiControlSearchBox
        {
            Position = profilesTable.Position + new Vector2(-0.45f, -0.036f),
            Size = new Vector2(0.35f, 0.032f),
            OriginAlign = MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_CENTER,
            SearchText = Config.Current.KeepProfileSearchText ? Config.Current.LatestProfileSearchText : ""
        };
        searchBox.OnTextChanged += OnSearchTextChange;

        Controls.Add(profilesTable);
        Controls.Add(searchBox);
    }

    private void OnSearchTextChange(string text)
    {
        RefreshTableRows();
        SelectRow(0);

        Config.Current.LatestProfileSearchText = Config.Current.KeepProfileSearchText ? text : "";
    }

    private void OnItemSelected(MyGuiControlTable table, MyGuiControlTable.EventArgs args)
    {
        UpdateButtons();
    }

    private void OnItemDoubleClicked(MyGuiControlTable table, MyGuiControlTable.EventArgs args)
    {
        var name = SelectedName;
        if (Load(name, false))
        {
            CloseScreen();
        }
    }

    private bool Load(string name, bool merge)
    {
        if (name == null)
            return false;

        try
        {
            storage.Load(name, merge);
        }
        catch (Exception e)
        {
            MyLog.Default.Error($"ToolbarManager: Failed to load toolbar \"{{name}}\": {e}");
            MyGuiSandboxExt.Show("Failed to load toolbar", "Error");
            return false;
        }

        return true;
    }

    private void UpdateButtons()
    {
        var isProfileSelected = profilesTable.SelectedRowIndex != null && profilesTable.SelectedRow?.UserData != null;

        newButton.Visible = !isProfileSelected;
        updateButton.Visible = isProfileSelected;

        loadButton.Enabled = isProfileSelected;
        mergeButton.Enabled = isProfileSelected;
        renameButton.Enabled = isProfileSelected;
        deleteButton.Enabled = isProfileSelected;
    }

    private int CellTextComparison(MyGuiControlTable.Cell x, MyGuiControlTable.Cell y)
    {
        if (x == null)
            return y == null ? 0 : 1;

        return y == null ? -1 : TextComparison(x.Text, y.Text);
    }

    private int TextComparison(StringBuilder x, StringBuilder y)
    {
        if (x == null)
            return y == null ? 0 : 1;

        return y == null ? -1 : x.CompareTo(y);
    }

    private void CreateButtons()
    {
        newButton = new MyGuiControlButton(
            visualStyle: MyGuiControlButtonStyleEnum.Small,
            originAlign: MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER,
            text: new StringBuilder("New"),
            onButtonClick: OnNew);

        updateButton = new MyGuiControlButton(
            visualStyle: MyGuiControlButtonStyleEnum.Small,
            originAlign: MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER,
            text: new StringBuilder("Update"),
            onButtonClick: OnUpdate);

        loadButton = new MyGuiControlButton(
            visualStyle: MyGuiControlButtonStyleEnum.Small,
            originAlign: MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER,
            text: new StringBuilder("Load"),
            onButtonClick: OnLoad);

        mergeButton = new MyGuiControlButton(
            visualStyle: MyGuiControlButtonStyleEnum.Small,
            originAlign: MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER,
            text: new StringBuilder("Merge"),
            onButtonClick: OnMerge);

        renameButton = new MyGuiControlButton(
            visualStyle: MyGuiControlButtonStyleEnum.Small,
            originAlign: MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER,
            text: new StringBuilder("Rename"),
            onButtonClick: OnRename);

        deleteButton = new MyGuiControlButton(
            visualStyle: MyGuiControlButtonStyleEnum.Small,
            originAlign: MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER,
            text: new StringBuilder("Delete"),
            onButtonClick: OnDelete);

        closeButton = new MyGuiControlButton(
            visualStyle: MyGuiControlButtonStyleEnum.Small,
            originAlign: MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER,
            text: new StringBuilder("Close"),
            onButtonClick: OnClose);

        var y = 0.5f * (DialogSize.Y - 0.15f);

        var w = 0.85f * DialogSize.X;
        var s = w / 6f;
        var x = -2.5f * s;
        var i = 0;

        newButton.Position = new Vector2(x, y);
        updateButton.Position = new Vector2(x, y);
        loadButton.Position = new Vector2(++i * s + x, y);
        mergeButton.Position = new Vector2(++i * s + x, y);
        renameButton.Position = new Vector2(++i * s + x, y);
        deleteButton.Position = new Vector2(++i * s + x, y);
        closeButton.Position = new Vector2(++i * s + x, y);

        newButton.SetToolTip("Save the current toolbar as a new profile");
        updateButton.SetToolTip("Update the selected profile with the current toolbar");
        loadButton.SetToolTip("Load the selected profile replacing the current toolbar");
        mergeButton.SetToolTip("Merge the selected profile into the current toolbar");
        renameButton.SetToolTip("Rename the selected profile");
        deleteButton.SetToolTip("Delete the selected profile");
        closeButton.SetToolTip("Close the dialog with no further changes");

        Controls.Add(newButton);
        Controls.Add(updateButton);
        Controls.Add(loadButton);
        Controls.Add(mergeButton);
        Controls.Add(renameButton);
        Controls.Add(deleteButton);
        Controls.Add(closeButton);
    }

    private void OnNew(MyGuiControlButton button)
    {
        var defaultName = "";

        if (Config.Current.KeepProfileSearchText)
        {
            defaultName = $"{searchBox.SearchText.Trim()} ";
        }

        MyGuiSandbox.AddScreen(new NameDialog(SaveAsNewProfile, "Save as new toolbar", defaultName, moveCursorToEnd: true));
    }

    private void OnUpdate(MyGuiControlButton obj)
    {
        var name = SelectedName;
        MyGuiSandbox.AddScreen(
            MyGuiSandbox.CreateMessageBox(buttonType: MyMessageBoxButtonsType.YES_NO,
                messageText: new StringBuilder($"Are you sure to update this saved toolbar?\r\n\r\n{name}"),
                messageCaption: new StringBuilder("Confirmation"),
                callback: result => OnSaveConfirmation(result, name)));
    }

    private void OnLoad(MyGuiControlButton button)
    {
        if (Load(SelectedName, false))
            CloseScreen();
    }

    private void OnMerge(MyGuiControlButton button)
    {
        if (Load(SelectedName, true))
            CloseScreen();
    }

    private void OnRename(MyGuiControlButton _)
    {
        var oldName = SelectedName;
        if (oldName == "")
            return;

        MyGuiSandbox.AddScreen(new NameDialog(newName => OnNewNameSpecified(oldName, newName), "Rename saved toolbar", oldName));
    }

    private void OnDelete(MyGuiControlButton _)
    {
        var name = SelectedName;
        if (name == "")
            return;

        MyGuiSandbox.AddScreen(
            MyGuiSandbox.CreateMessageBox(buttonType: MyMessageBoxButtonsType.YES_NO,
                messageText: new StringBuilder($"Are you sure to delete this saved toolbar?\r\n\r\n{name}"),
                messageCaption: new StringBuilder("Confirmation"),
                callback: result => OnDeleteForSure(result, name)));
    }

    private void OnClose(MyGuiControlButton button)
    {
        CloseScreen();
    }

    private void RefreshTableRows()
    {
        profilesTable.Clear();

        var pattern = searchBox.SearchText?
            .ToLower()
            .Split(' ')
            .Where(part => part.Length != 0)
            .ToArray();

        foreach (var name in storage.IterProfileNames())
        {
            if (name.ToLower().IsMatching(pattern))
            {
                AppendProfileToTable(name);
            }
        }

        profilesTable.Add(new MyGuiControlTable.Row());
        SelectRow(profilesTable.Rows.Count - 1);
    }

    private void SelectRow(int index)
    {
        profilesTable.SelectedRowIndex = index;

        UpdateButtons();

        if (SelectedName != null)
            profilesTable.ScrollToSelection();
    }

    private void AppendProfileToTable(string name)
    {
        var json = storage.ReadJson(name);
        CountUsedSlots(json);

        var row = new MyGuiControlTable.Row(name);

        row.AddCell(new MyGuiControlTable.Cell(name));

        for (var i = 0; i < 9; i++)
            row.AddCell(new MyGuiControlTable.Cell(usedSlotCounts[i] > 0 ? $"{usedSlotCounts[i]}" : "-"));

        profilesTable.Add(row);
    }

    private void CountUsedSlots(JsonData json)
    {
        for (var i = 0; i < 9; i++)
            usedSlotCounts[i] = 0;

        try
        {
            var slots = json["Slots"];
            if (slots == null)
                return;

            var slotCount = slots.Count;
            for (var i = 0; i < slotCount; i++)
            {
                var page = i / 9;
                if (page > 8)
                    break;
                if (!(bool)slots[i]["IsEmpty"])
                    usedSlotCounts[page]++;
            }
        }
        catch (SystemException e)
        {
            MyLog.Default.Warning($"ToolbarManager: Failed to count free slots ({e})");
        }
    }

    private void OnNewNameSpecified(string oldName, string newName)
    {
        newName = newName.SanitizeFileName();
        if (storage.HasProfile(newName) && !oldName.EqualsIgnoreCase(newName))
        {
            MyGuiSandbox.AddScreen(
                MyGuiSandbox.CreateMessageBox(buttonType: MyMessageBoxButtonsType.YES_NO,
                    messageText: new StringBuilder($"Are you sure to overwrite this saved toolbar?\r\n\r\n{newName}"),
                    messageCaption: new StringBuilder("Confirmation"),
                    callback: result => OnRenameConfirmation(result, oldName, newName)));
        }
        else
        {
            OnRenameConfirmation(MyGuiScreenMessageBox.ResultEnum.YES, oldName, newName);
        }
    }

    private void OnRenameConfirmation(MyGuiScreenMessageBox.ResultEnum result, string oldName, string newName)
    {
        if (result != MyGuiScreenMessageBox.ResultEnum.YES)
            return;

        storage.Rename(oldName, newName);

        RefreshTableRows();
        SelectRowByName(newName);
    }

    private void OnDeleteForSure(MyGuiScreenMessageBox.ResultEnum result, string name)
    {
        if (result != MyGuiScreenMessageBox.ResultEnum.YES)
            return;

        storage.Delete(name);

        RefreshTableRows();
    }

    private void SaveAsNewProfile(string name)
    {
        if (name == null || name.Trim().Length == 0)
            return;

        if (storage.HasProfile(name))
        {
            MyGuiSandbox.AddScreen(
                MyGuiSandbox.CreateMessageBox(buttonType: MyMessageBoxButtonsType.YES_NO,
                    messageText: new StringBuilder($"Are you sure to overwrite this saved toolbar?\r\n\r\n{name}"),
                    messageCaption: new StringBuilder("Confirmation"),
                    callback: result => OnSaveConfirmation(result, name)));
        }
        else
        {
            OnSaveConfirmation(MyGuiScreenMessageBox.ResultEnum.YES, name);
        }
    }

    private void OnSaveConfirmation(MyGuiScreenMessageBox.ResultEnum result, string name)
    {
        if (result != MyGuiScreenMessageBox.ResultEnum.YES)
            return;

        try
        {
            storage.Save(name);
        }
        catch (Exception e)
        {
            MyLog.Default.Error($"ToolbarManager: Failed to save toolbar \"{{name}}\": {e}");
            MyGuiSandboxExt.Show("Failed to save toolbar", "Error");
        }

        RefreshTableRows();
        SelectRowByName(name);
    }

    private bool TryFindListItem(string name, out int index)
    {
        var count = profilesTable.RowsCount;
        for (index = 0; index < count; index++)
        {
            if (profilesTable.Rows[index].UserData is string s && s == name)
                return true;
        }

        index = -1;
        return false;
    }

    private string SelectedName => profilesTable.SelectedRow?.UserData as string;

    private void SelectRowByName(string name)
    {
        if (!TryFindListItem(name, out var i))
            return;

        SelectRow(i);
    }
}
