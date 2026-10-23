using System.Collections.Generic;
using System.Linq;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.Gui;
using Sandbox.Game.Screens.Helpers;
using Sandbox.Game.World;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.GUI;
using VRage.ObjectBuilders.Private;

namespace ClientPlugin.Logic;

// A group of tools, weapons and blocks in a slot of the character toolbar.
//
// It is saved as a vanilla terminal group object builder with a marker name and no block,
// so worlds and toolbar profiles stay loadable without the plugin: the game drops a terminal
// group without a block on loading it. See MyToolbarItemFactoryPatch for the way back.
public class ItemGroupToolbarItem : MyToolbarItem
{
    public const string Marker = "ToolbarManager:";

    public readonly string GroupId;

    // Groups on separate slots share the hand, so the held item alone can't tell which
    // group is in use or where each one is. Each group remembers the kind it equipped last,
    // and only the group used last steps on from the held item.
    private static readonly Dictionary<string, string> Positions = new();
    private static string activeGroupId;

    // Loading a world activates its saved selected slot, which must not step that group on
    public static void OnSessionLoading() => activeGroupId = null;

    private ItemGroup Group => Config.Current.FindItemGroup(GroupId);

    private string Position => Positions.TryGetValue(GroupId, out var kind) ? kind : null;

    // Whether this group is the one in use, so using it again moves to its next kind
    private bool IsCurrent(ItemGroup.Member held) =>
        activeGroupId == GroupId && held != null && held.Kind == Position;

    public ItemGroupToolbarItem(string groupId)
    {
        GroupId = groupId;
        ActivateOnClick = false;
        SetDisplayName(Group?.Name ?? "Missing group");
        // The marker the game puts on blocks with variants
        var variantTexture = MyHud.HudDefinition?.Toolbar?.ItemStyle?.VariantTexture;
        if (variantTexture != null)
            SetSubIcon(MyGuiTextures.Static.GetTexture(variantTexture.Value).Path);
        SetIcons(Group?.Members.FirstOrDefault()?.Item.Icons);
    }

    public static bool IsMarker(MyObjectBuilder_ToolbarItem builder, out string groupId)
    {
        groupId = null;
        if (
            !(builder is MyObjectBuilder_ToolbarItemTerminalGroup group)
            || group.BlockEntityId != 0
            || group.GroupName == null
            || !group.GroupName.StartsWith(Marker)
        )
            return false;

        groupId = group.GroupName.Substring(Marker.Length);
        return true;
    }

    public static MyObjectBuilder_ToolbarItemTerminalGroup CreateMarker(string groupId)
    {
        var builder =
            MyObjectBuilderSerializerKeen.CreateNewObject<MyObjectBuilder_ToolbarItemTerminalGroup>();
        builder.GroupName = Marker + groupId;
        builder.BlockEntityId = 0;
        return builder;
    }

    public override MyObjectBuilder_ToolbarItem GetObjectBuilder() => CreateMarker(GroupId);

    public override bool Init(MyObjectBuilder_ToolbarItem data) => IsMarker(data, out _);

    public override bool AllowedInToolbarType(MyToolbarType type) =>
        type == MyToolbarType.Character;

    public override bool Activate() => Activate(1);

    public bool Activate(int direction)
    {
        var group = Group;
        if (group == null || !(MySession.Static.ControlledEntity is MyCharacter character))
            return false;

        group.Update(character, MySession.Static.LocalPlayerId);
        var held = group.Held;
        var current = IsCurrent(held);
        var member = current ? group.Pick(held.Kind, direction, true) : group.Pick(Position);
        if (member == null || (current && member == held))
            return false;

        // Like the game's toolbar, put the cube builder away before switching from a block.
        // Switching in place skips the builder's reset: the placement box could lose the grid
        // in view, and a block of the same game block group would switch the grid size.
        if (member != held && MyCubeBuilder.Static is { IsActivated: true })
            character.SwitchToWeapon(null);

        // The game's own activation of a tool or block slot, which also syncs it in multiplayer.
        // Switching to a group whose item is already in the hand only makes it the current one.
        if (member != held && !member.Item.Activate())
            return false;
        Positions[GroupId] = member.Kind;
        activeGroupId = GroupId;
        WantsToBeActivated = true;
        return true;
    }

    // Whether the mouse wheel should step through this group: it is in use with a hand item
    // or a block, which the game would otherwise cycle through its own block variants with
    public bool ScrollsWith(MyCharacter character)
    {
        var group = Group;
        if (group == null)
            return false;

        group.Update(character, MySession.Static.LocalPlayerId);
        var held = group.Held;
        return IsCurrent(held)
            && (held.Item is MyToolbarItemWeapon || held.Item is MyToolbarItemCubeBlock);
    }

    public override ChangeInfo Update(MyEntity owner, long playerID = 0, bool anyoneCanUse = false)
    {
        var group = Group;
        var changes = SetDisplayName(group?.Name ?? "Missing group");
        if (group == null)
            return changes | SetEnabled(false);

        group.Update(owner, playerID);
        changes |= SetEnabled(group.AnyAvailable);

        // Show the held item of the group in use, or else the one using the group would equip
        var held = group.Held;
        var current = IsCurrent(held);
        var shown =
            (current ? held : null) ?? group.Pick(Position) ?? group.Members.FirstOrDefault();
        if (shown != null)
            changes |= SetIcons(shown.Item.Icons);

        WantsToBeSelected = current;
        return changes;
    }

    public override bool Equals(object obj) =>
        obj is ItemGroupToolbarItem other && other.GroupId == GroupId;

    public override int GetHashCode() => GroupId.GetHashCode();
}
