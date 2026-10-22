using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Serialization;
using Sandbox.Definitions;
using Sandbox.Game.Entities.Character;
using VRage.Game;
using VRage.ObjectBuilders;

namespace ClientPlugin.Logic;

// A player defined group of hand items (tools, weapons), which goes on the character toolbar
// as a single slot. Using it equips the first item of the group the character has.
public class ItemGroup
{
    public const string DefaultId = "tools";

    // Stable key referenced from toolbar slots, so renaming a group keeps its slots
    public string Id = "";

    public string Name = "";

    // Physical item definition ids, like MyObjectBuilder_PhysicalGunObject/Welder4Item.
    // The order decides which of the equivalent items is picked first.
    public string[] Items
    {
        get => items;
        set
        {
            items = value ?? Array.Empty<string>();
            definitions = null;
        }
    }

    private string[] items = Array.Empty<string>();
    private List<MyPhysicalItemDefinition> definitions;

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

    public ItemGroup Clone() =>
        new ItemGroup
        {
            Id = Id,
            Name = Name,
            Items = (string[])Items.Clone(),
        };

    // The items of the group which exist in the loaded game content, in group order
    [XmlIgnore]
    public List<MyPhysicalItemDefinition> Definitions
    {
        get
        {
            if (definitions != null)
                return definitions;

            definitions = new List<MyPhysicalItemDefinition>(items.Length);
            foreach (var item in items)
            {
                if (
                    MyDefinitionId.TryParse(item, out var id)
                    && MyDefinitionManager.Static.TryGetDefinition(
                        id,
                        out MyPhysicalItemDefinition definition
                    )
                )
                    definitions.Add(definition);
            }

            return definitions;
        }
    }

    // Equivalent items are held the same way, like all tiers of the hand drill
    public static MyObjectBuilderType KindOf(MyPhysicalItemDefinition item) =>
        MyDefinitionManager.Static.TryGetHandItemForPhysicalItem(item.Id)?.Id.TypeId
        ?? item.Id.TypeId;

    // The same check the game's own tool slots use to enable themselves
    public static bool IsAvailable(MyCharacter character, MyPhysicalItemDefinition item) =>
        character.FindWeaponItemByDefinition(item.Id).HasValue
        || !character.WeaponTakesBuilderFromInventory(item.Id);

    public static MyPhysicalItemDefinition Held(MyCharacter character)
    {
        var weapon = character.CurrentWeapon;
        return weapon == null
            ? null
            : MyDefinitionManager.Static.GetPhysicalItemForHandItem(weapon.DefinitionId);
    }

    public bool Contains(MyPhysicalItemDefinition item) =>
        item != null && Definitions.Contains(item);

    public bool AnyAvailable(MyCharacter character) =>
        Definitions.Any(item => IsAvailable(character, item));

    // The item to equip on using the group: the first available one of the first kind,
    // or of the next kind while the character holds an item of this group.
    // Null if the character has none of the items.
    public MyPhysicalItemDefinition PickNext(MyCharacter character)
    {
        var kinds = Definitions.Select(KindOf).Distinct().ToList();
        var held = Held(character);
        var start = Contains(held) ? kinds.IndexOf(KindOf(held)) : -1;

        // The held kind comes last, so a better item of that kind is still picked up
        for (var step = 1; step <= kinds.Count; step++)
        {
            var kind = kinds[(start + step) % kinds.Count];
            var item = Definitions.FirstOrDefault(d =>
                KindOf(d) == kind && IsAvailable(character, d)
            );
            if (item != null)
                return item;
        }

        return null;
    }
}
