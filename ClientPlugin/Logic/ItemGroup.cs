using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;
using Sandbox.Definitions;
using Sandbox.Game.Screens.Helpers;
using VRage.Game;
using VRage.Game.Entity;

namespace ClientPlugin.Logic;

// A player defined group of tools, weapons and blocks, which goes on the character toolbar
// as a single slot. Using it equips the first item of the group the character can use.
public class ItemGroup
{
    public const string DefaultId = "tools";

    // Stable key referenced from toolbar slots, so renaming a group keeps its slots
    public string Id = "";

    public string Name = "";

    public int Order;

    // Definition ids, like MyObjectBuilder_PhysicalGunObject/Welder4Item for a tool or
    // MyObjectBuilder_CubeBlock/LargeBlockArmorBlock for a block. The order decides which
    // of the equivalent items is picked first.
    public string[] Items
    {
        get => items;
        set
        {
            items = value ?? Array.Empty<string>();
            members = null;
        }
    }

    private string[] items = Array.Empty<string>();
    private List<Member> members;

    // An item of the group, as the vanilla toolbar item a slot of its own would hold.
    // Updating that item tells whether it can be used now and whether it is in the hand,
    // the same way the game decides it for a toolbar slot.
    public class Member
    {
        public readonly MyDefinitionBase Definition;
        public readonly MyToolbarItem Item;

        // All tiers of a hand tool are one kind, every block is a kind of its own
        public readonly string Kind;

        public Member(MyDefinitionBase definition, MyToolbarItem item)
        {
            Definition = definition;
            Item = item;
            Kind =
                definition is MyPhysicalItemDefinition
                && MyDefinitionManager.Static.TryGetHandItemForPhysicalItem(definition.Id)
                    is { } handItem
                    ? handItem.Id.TypeId.ToString()
                    : definition.Id.ToString();
        }

        public bool Available => Item.Enabled;
        public bool Held => Item.WantsToBeSelected;
    }

    // Mimics the Tools Switcher plugin: welder, grinder and drill, the best tier first
    public static ItemGroup CreateDefault()
    {
        var tiers = new[] { "4", "3", "2", "" };
        return new ItemGroup
        {
            Id = DefaultId,
            Name = "Hand Tools",
            Items = new[] { "Welder", "AngleGrinder", "HandDrill" }
                .SelectMany(kind =>
                    tiers.Select(tier => $"MyObjectBuilder_PhysicalGunObject/{kind}{tier}Item")
                )
                .ToArray(),
        };
    }

    public static ItemGroup CreateNew(string name) =>
        new ItemGroup { Id = Guid.NewGuid().ToString("N"), Name = name };

    // Tools, weapons and blocks can be in a group, as the toolbar item a slot would hold
    public static MyToolbarItem CreateMemberItem(MyDefinitionBase definition)
    {
        var builder = MyToolbarItemFactory.ObjectBuilderFromDefinition(definition);
        if (
            !(builder is MyObjectBuilder_ToolbarItemWeapon)
            && !(builder is MyObjectBuilder_ToolbarItemCubeBlock)
        )
            return null;

        return MyToolbarItemFactory.CreateToolbarItem(builder);
    }

    public static MyDefinitionBase FindDefinition(string id)
    {
        if (!MyDefinitionId.TryParse(id, out var definitionId))
            return null;

        // Cube blocks are not in the generic definition dictionary
        if (MyDefinitionManager.Static.TryGetDefinition(definitionId, out MyDefinitionBase found))
            return found;

        return MyDefinitionManager.Static.TryGetCubeBlockDefinition(definitionId, out var block)
            ? block
            : null;
    }

    // The items of the group which exist in the loaded game content, in group order
    [XmlIgnore]
    public List<Member> Members
    {
        get
        {
            if (members != null)
                return members;

            members = new List<Member>(items.Length);
            foreach (var id in items)
            {
                var definition = FindDefinition(id);
                var item = definition == null ? null : CreateMemberItem(definition);
                if (item != null)
                    members.Add(new Member(definition, item));
            }

            return members;
        }
    }

    public void Update(MyEntity owner, long playerID)
    {
        foreach (var member in Members)
            member.Item.Update(owner, playerID);
    }

    public Member Held => Members.FirstOrDefault(m => m.Held);

    public bool AnyAvailable => Members.Any(m => m.Available);

    // The item to equip on using the group: the first available one of the first kind,
    // or of the next kind while the character holds an item of this group.
    // Null if none of the items can be used. Needs Update first.
    public Member PickNext(int direction = 1)
    {
        var kinds = Members.Select(m => m.Kind).Distinct().ToList();
        var held = Held;
        var start = held == null ? (direction > 0 ? -1 : 0) : kinds.IndexOf(held.Kind);

        // The held kind comes last, so its first available item in tile order can still be selected.
        for (var step = 1; step <= kinds.Count; step++)
        {
            var kind = kinds[(start + direction * step + kinds.Count) % kinds.Count];
            var member = Members.FirstOrDefault(m => m.Kind == kind && m.Available);
            if (member != null)
                return member;
        }

        return null;
    }
}
