using System;
using System.Collections.Generic;
using ClientPlugin.Logic;
using ClientPlugin.Patches;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.Screens.Helpers;
using Sandbox.Game.World;
using Sandbox.Graphics.GUI;
using VRage.Game;
using VRage.Input;
using VRage.Utils;
using VRageMath;
using static ClientPlugin.Logic.BlockFit;

namespace ClientPlugin.Gui;

// The items of the group in use on a grid in the middle of the screen, while the quick
// selector keys are held. Clicking one equips it, a block turned to fit like the fitting
// blocks wheel does. The blocks that fit the aimed cell are highlighted.
public class QuickSelector : MyGuiScreenBase
{
    private static readonly Vector4 FitColor = new Vector4(0.35f, 1f, 0.35f, 1f);
    private static readonly Vector2 Margin = new Vector2(0.03f, 0.04f);

    private static QuickSelector open;

    private readonly ItemGroupToolbarItem item;
    private readonly MyCharacter character;
    private readonly List<ItemGroup.Member> members;

    // Where the cube builder aimed when the keys were pressed, null without a block on a grid.
    // Equipping another block takes the placement box off the grid for some frames, so the
    // fit is not evaluated again while the selector is open.
    private readonly FittingBlock.Aimed aimed;
    private readonly Pick?[] picks;

    private readonly MyGuiControlGrid grid;

    // Opens the selector when the keys become held with a group in use in gameplay
    public static void HandleGameplayInput()
    {
        var input = MyInput.Static;
        if (open != null || !IsHeld(input) || !IsNewPress(input))
            return;

        var item = ItemGroupScrollPatch.GroupInUse(out var character);
        if (item?.Group == null || item.Group.Members.Count == 0)
            return;

        MyGuiSandbox.AddScreen(new QuickSelector(item, character));
    }

    private static (MyKeys a, MyKeys b, MyKeys c)? Keys(Config.QuickSelectorChord keys) =>
        keys switch
        {
            Config.QuickSelectorChord.ShiftCtrl => (MyKeys.Shift, MyKeys.Control, MyKeys.Alt),
            Config.QuickSelectorChord.ShiftAlt => (MyKeys.Shift, MyKeys.Alt, MyKeys.Control),
            Config.QuickSelectorChord.CtrlAlt => (MyKeys.Control, MyKeys.Alt, MyKeys.Shift),
            _ => null,
        };

    private static bool IsPressed(IMyInput input, MyKeys modifier) =>
        modifier switch
        {
            MyKeys.Shift => input.IsAnyShiftKeyPressed(),
            MyKeys.Control => input.IsAnyCtrlKeyPressed(),
            _ => input.IsAnyAltKeyPressed(),
        };

    private static bool IsNewPressed(IMyInput input, MyKeys modifier) =>
        modifier switch
        {
            MyKeys.Shift => input.IsNewKeyPressed(MyKeys.LeftShift)
                || input.IsNewKeyPressed(MyKeys.RightShift),
            MyKeys.Control => input.IsNewKeyPressed(MyKeys.LeftControl)
                || input.IsNewKeyPressed(MyKeys.RightControl),
            _ => input.IsNewKeyPressed(MyKeys.LeftAlt) || input.IsNewKeyPressed(MyKeys.RightAlt),
        };

    // Both keys of the pair, without the third modifier
    private static bool IsHeld(IMyInput input) =>
        Keys(Config.Current.QuickSelectorKeys) is (var a, var b, var c)
        && IsPressed(input, a)
        && IsPressed(input, b)
        && !IsPressed(input, c);

    // Pressing the second key completes the pair, holding it on after closing doesn't reopen
    private static bool IsNewPress(IMyInput input) =>
        Keys(Config.Current.QuickSelectorKeys) is (var a, var b, _)
        && (IsNewPressed(input, a) || IsNewPressed(input, b));

    private QuickSelector(ItemGroupToolbarItem item, MyCharacter character)
        : base(new Vector2(0.5f, 0.5f), MyGuiConstants.SCREEN_BACKGROUND_COLOR)
    {
        this.item = item;
        this.character = character;
        members = item.Group.Members;
        aimed = FittingBlock.Aim(item.Group);
        picks = aimed?.Evaluate();

        EnabledBackgroundFade = false;
        CanHideOthers = false;
        CanBeHidden = false;
        CloseButtonEnabled = false;
        SkipTransition = true;

        // Columns and rows close to 16:9, the last row may have empty cells on the right
        var count = members.Count;
        var columns = Math.Max(1, (int)Math.Round(Math.Sqrt(count * 16.0 / 9.0)));
        var rows = (count + columns - 1) / columns;

        grid = new MyGuiControlGrid
        {
            Name = "QuickSelectorGrid",
            VisualStyle = MyGuiControlGridStyleEnum.Inventory,
            ColumnsCount = columns,
            RowsCount = rows,
            OriginAlign = MyGuiDrawAlignEnum.HORISONTAL_CENTER_AND_VERTICAL_CENTER,
            Position = Vector2.Zero,
        };
        foreach (var member in members)
        {
            var fits = Fits(member);
            var gridItem = new MyGuiGridItem(
                member.Item.Icons,
                member.Item.SubIcon,
                member.Item.DisplayName + (fits ? "\nFits the aimed position" : ""),
                member,
                member.Available
            );
            if (fits)
                gridItem.BackgroundColor = FitColor;
            grid.Add(gridItem);
        }
        grid.ItemClicked += OnItemClicked;
        Controls.Add(grid);

        m_size = grid.Size + Margin;
        SyncSelection();
        open = this;
    }

    private bool Fits(ItemGroup.Member member)
    {
        if (picks == null)
            return false;
        var index = aimed.Members.IndexOf(member);
        return index >= 0 && picks[index].HasValue;
    }

    private void OnItemClicked(MyGuiControlGrid sender, MyGuiControlGrid.EventArgs args)
    {
        if (
            args.Button != MySharedButtonsEnum.Primary
            || sender.GetItemAt(args.ItemIndex)?.UserData is not ItemGroup.Member member
            || !member.Available
        )
            return;

        Select(member);
    }

    // Equips the member, a fitting block turned the way it fits
    private void Select(ItemGroup.Member member)
    {
        var index = aimed?.Members.IndexOf(member) ?? -1;
        if (index >= 0 && picks[index].HasValue)
            FittingBlock.Take(item, character, aimed, picks[index].Value);
        else if (!member.Held)
            item.Equip(character, member, item.Group.Held);
        SyncSelection();
    }

    private void SyncSelection()
    {
        var held = item.Group?.Held;
        var index = held == null ? -1 : members.IndexOf(held);
        grid.SelectedIndex = index >= 0 ? index : null;
    }

    public override void HandleInput(bool receivedFocusInThisUpdate)
    {
        if (
            !IsHeld(MyInput.Static)
            || item.Group == null
            || MySession.Static?.ControlledEntity != character
        )
        {
            CloseScreenNow();
            return;
        }

        base.HandleInput(receivedFocusInThisUpdate);
    }

    public override bool Update(bool hasFocus)
    {
        SyncSelection();
        return base.Update(hasFocus);
    }

    public override void CloseScreenNow(bool isUnloading = false)
    {
        if (open == this)
            open = null;
        base.CloseScreenNow(isUnloading);
    }

    public override string GetFriendlyName() => "ToolbarManagerQuickSelector";
}
