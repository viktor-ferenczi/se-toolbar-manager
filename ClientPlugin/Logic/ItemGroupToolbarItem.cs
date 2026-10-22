using System.Linq;
using Sandbox.Common.ObjectBuilders;
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

    private ItemGroup Group => Config.Current.FindItemGroup(GroupId);

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
        var member = group.PickNext(direction);
        if (member == null || member == group.Held)
            return false;

        // The game's own activation of a tool or block slot, which also syncs it in multiplayer
        if (!member.Item.Activate())
            return false;
        WantsToBeActivated = true;
        return true;
    }

    public override ChangeInfo Update(MyEntity owner, long playerID = 0, bool anyoneCanUse = false)
    {
        var group = Group;
        var changes = SetDisplayName(group?.Name ?? "Missing group");
        if (group == null)
            return changes | SetEnabled(false);

        group.Update(owner, playerID);
        changes |= SetEnabled(group.AnyAvailable);

        // Show the held item of the group, or else the one using the group would equip
        var held = group.Held;
        var shown = held ?? group.PickNext() ?? group.Members.FirstOrDefault();
        if (shown != null)
            changes |= SetIcons(shown.Item.Icons);

        WantsToBeSelected = held != null;
        return changes;
    }

    public override bool Equals(object obj) =>
        obj is ItemGroupToolbarItem other && other.GroupId == GroupId;

    public override int GetHashCode() => GroupId.GetHashCode();
}
