using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Xml.Serialization;
using ClientPlugin.Gui;
using ClientPlugin.Logic;
using ClientPlugin.Settings;
using ClientPlugin.Settings.Elements;
using ClientPlugin.Settings.Tools;
using Sandbox.Graphics.GUI;
using VRage.Input;

namespace ClientPlugin;

public class Config : INotifyPropertyChanged
{
    #region Options

    private bool enableStagingArea = true;
    private int stagingAreaRowCount = 2;
    private bool preventOverwritingSlots = true;
    private bool useCharacterProfilesForBuildCockpit = true;

    private bool keepBlockSearchText;
    private string latestBlockSearchText = "";

    private bool keepProfileSearchText;
    private string latestProfileSearchText = "";

    private Binding blockSearchKey = new Binding(MyKeys.OemPipe);
    private FitModifier fitScrollModifier = FitModifier.Shift;
    private bool quickFit = true;
    private FitMouseButton nextFitButton = FitMouseButton.ExtraButton2;
    private Binding nextFitKey = new Binding(MyKeys.None);
    private FitMouseButton previousFitButton = FitMouseButton.ExtraButton1;
    private Binding previousFitKey = new Binding(MyKeys.None);
    private QuickSelectorChord quickSelectorKeys = QuickSelectorChord.ShiftCtrl;

    private ItemGroup[] itemGroups;

    #endregion

    #region User interface

    public readonly string Title = "Toolbar Manager";

    public const string StagingAreaDescription =
        "The staging area allows for the convenient reordering of toolbar items,\nincluding moving them between toolbar pages.\n\nThe staging areas are preserved for the character\nand each block with a toolbar in-memory during gameplay,\nbut NOT SAVED over sessions (world loads) and game restarts.\nPlease save your toolbars into profiles after editing.";

    [Separator("Functionality")]
    [Checkbox(label: "Enable staging area", description: StagingAreaDescription)]
    public bool EnableStagingArea
    {
        get => enableStagingArea;
        set => SetField(ref enableStagingArea, value);
    }

    [Slider(
        1f,
        8f,
        1f,
        SliderAttribute.SliderType.Integer,
        label: "Staging rows",
        description: "Number of rows visible in the staging area [1..8]"
    )]
    public int StagingAreaRowCount
    {
        get => stagingAreaRowCount;
        set => SetField(ref stagingAreaRowCount, value);
    }

    [Checkbox(
        label: "Prevent overwriting slots",
        description: "Prevent overwriting the selected or first toolbar slot on double-clicking blocks"
    )]
    public bool PreventOverwritingSlots
    {
        get => preventOverwritingSlots;
        set => SetField(ref preventOverwritingSlots, value);
    }

    [Checkbox(
        label: "Build cockpit is character",
        description: "Building from cockpit (Ctrl-G) uses the character profiles"
    )]
    public bool UseCharacterProfilesForBuildCockpit
    {
        get => useCharacterProfilesForBuildCockpit;
        set => SetField(ref useCharacterProfilesForBuildCockpit, value);
    }

    [Separator("Recent searches")]
    [Checkbox(description: "Keep the search text between subsequent uses of the block search")]
    public bool KeepBlockSearchText
    {
        get => keepBlockSearchText;
        set => SetField(ref keepBlockSearchText, value);
    }

    public string LatestBlockSearchText
    {
        get => latestBlockSearchText;
        set => SetField(ref latestBlockSearchText, value);
    }

    [Checkbox(description: "Keep the search text between subsequent uses of the Profile dialog")]
    public bool KeepProfileSearchText
    {
        get => keepProfileSearchText;
        set => SetField(ref keepProfileSearchText, value);
    }

    public string LatestProfileSearchText
    {
        get => latestProfileSearchText;
        set => SetField(ref latestProfileSearchText, value);
    }

    [Separator("Hotkeys")]
    [Keybind(description: "Key to open the G menu and activate block search")]
    public Binding BlockSearchKey
    {
        get => blockSearchKey;
        set => SetField(ref blockSearchKey, value);
    }

    [Separator("Groups")]
    [Button(label: "Edit groups", description: "Opens the G menu in group edit mode, in a world")]
    public static void EditItemGroups() => GroupEditor.OpenFromSettings();

    public const string FitBlockDescription =
        "With a block of a group in hand and aiming at a grid, selects the next or previous\nblock of the group whose faces and conveyor ports fit the aimed position";

    [Dropdown(
        label: "Fitting blocks wheel",
        description: "Modifier key which makes the mouse wheel step through\nonly the blocks of the group that fit the aimed position"
    )]
    public FitModifier FitScrollModifier
    {
        get => fitScrollModifier;
        set => SetField(ref fitScrollModifier, value);
    }

    [Checkbox(
        label: "Quick fitting block",
        description: "Pressing the wheel's modifier key alone selects a fitting block right away:\nthe block in hand if it fits, turned to fit, or else the next one that fits"
    )]
    public bool QuickFit
    {
        get => quickFit;
        set => SetField(ref quickFit, value);
    }

    [Dropdown(label: "Next fitting block", description: FitBlockDescription)]
    public FitMouseButton NextFitButton
    {
        get => nextFitButton;
        set => SetField(ref nextFitButton, value);
    }

    [Keybind(label: "Next fitting block key", description: FitBlockDescription)]
    public Binding NextFitKey
    {
        get => nextFitKey;
        set => SetField(ref nextFitKey, value);
    }

    [Dropdown(label: "Previous fitting block", description: FitBlockDescription)]
    public FitMouseButton PreviousFitButton
    {
        get => previousFitButton;
        set => SetField(ref previousFitButton, value);
    }

    [Keybind(label: "Previous fitting block key", description: FitBlockDescription)]
    public Binding PreviousFitKey
    {
        get => previousFitKey;
        set => SetField(ref previousFitKey, value);
    }

    [Dropdown(
        label: "Quick selector keys",
        description: "Holding these keys with a group in use shows its items to pick one with the mouse,\nhighlighting the blocks that fit the aimed position"
    )]
    public QuickSelectorChord QuickSelectorKeys
    {
        get => quickSelectorKeys;
        set => SetField(ref quickSelectorKeys, value);
    }

    [XmlIgnore]
    public ItemGroup[] ItemGroups
    {
        get => itemGroups ?? (itemGroups = ItemGroupStorage.Load());
        set
        {
            var groups = value ?? Array.Empty<ItemGroup>();
            ItemGroupStorage.Save(groups);
            itemGroups = groups;
        }
    }

    public ItemGroup FindItemGroup(string id) => ItemGroups.FirstOrDefault(g => g.Id == id);

    #endregion

    // Held alone, without the other two
    public enum FitModifier
    {
        None,
        Shift,
        Ctrl,
        Alt,
    }

    // Both held, without the third modifier
    public enum QuickSelectorChord
    {
        None,
        ShiftCtrl,
        ShiftAlt,
        CtrlAlt,
    }

    // Pressed without Ctrl, Shift or Alt
    public enum FitMouseButton
    {
        None,
        MiddleButton,
        ExtraButton1,
        ExtraButton2,
    }

    #region Property change notification boilerplate

    public static readonly Config Default = new Config();
    public static readonly Config Current = ConfigStorage.Load();

    public static void Save()
    {
        ConfigStorage.Save(Current);
    }

    public event PropertyChangedEventHandler PropertyChanged;

    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    #endregion
}
