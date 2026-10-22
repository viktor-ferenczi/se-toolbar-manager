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

// An item group in a slot of the character toolbar.
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
        SetIcons(Group?.Definitions.Count > 0 ? Group.Definitions[0].Icons : null);
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

    public override bool Activate()
    {
        if (!(MySession.Static.ControlledEntity is MyCharacter character))
            return false;

        var item = Group?.PickNext(character);
        if (item == null || item == ItemGroup.Held(character))
            return false;

        if (
            !(
                MyToolbarItemFactory.CreateToolbarItem(
                    MyToolbarItemFactory.ObjectBuilderFromDefinition(item)
                )
                is MyToolbarItemWeapon weapon
            )
        )
            return false;

        // The game's own tool slot activation, which also syncs the change in multiplayer
        character.SwitchToWeapon(weapon);
        WantsToBeActivated = true;
        return true;
    }

    public override ChangeInfo Update(MyEntity owner, long playerID = 0, bool anyoneCanUse = false)
    {
        var group = Group;
        var character = MySession.Static.LocalCharacter;
        var onFoot = character != null && MySession.Static.ControlledEntity == character;

        var changes = SetDisplayName(group?.Name ?? "Missing group");
        changes |= SetEnabled(onFoot && group != null && group.AnyAvailable(character));

        // Show the held item of the group, or else the one using the group would equip
        var held = onFoot ? ItemGroup.Held(character) : null;
        var shown =
            group?.Contains(held) == true ? held
            : onFoot ? group?.PickNext(character)
            : null;
        shown ??= group?.Definitions.Count > 0 ? group.Definitions[0] : null;
        if (shown != null)
            changes |= SetIcons(shown.Icons);

        WantsToBeSelected = group?.Contains(held) == true;
        return changes;
    }

    public override bool Equals(object obj) =>
        obj is ItemGroupToolbarItem other && other.GroupId == GroupId;

    public override int GetHashCode() => GroupId.GetHashCode();
}
