using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.Multiplayer;
using Sandbox.Game.SessionComponents;
using Sandbox.Game.World;

namespace ClientPlugin.Logic;

// What the fitting blocks keys and the quick selector work on: a player's group on a
// toolbar slot, or the game's block variants of the block in hand
public interface IGroupInUse
{
    // Null once the group is gone
    List<ItemGroup.Member> Members { get; }

    ItemGroup.Member Held { get; }

    bool Equip(MyCharacter character, ItemGroup.Member member, ItemGroup.Member held);
}

// The game's block variant group of the block in the cube builder, in the builder's
// block size: the blocks the wheel steps through in the game. A block without variants
// is alone in it.
public sealed class BlockVariants : IGroupInUse
{
    private static BlockVariants current;

    // The gamepad's toolbar listens to it
    private static readonly System.Reflection.FieldInfo VariantChanged = AccessTools.Field(
        typeof(MyCubeBuilder),
        nameof(MyCubeBuilder.OnBlockVariantChanged)
    );

    private readonly List<MyCubeBlockDefinition> stages;
    private readonly List<ItemGroup.Member> members;

    // An item the game's wheel would step to, by the conditions of
    // MyCubeBuilder.HandleBlockVariantsInput
    private sealed class Variant : ItemGroup.Member
    {
        public Variant(MyCubeBlockDefinition definition)
            : base(definition, ItemGroup.CreateMemberItem(definition)) { }

        public override bool Available
        {
            get
            {
                var block = (MyCubeBlockDefinition)Definition;
                var session = MySession.Static;
                return (!session.SurvivalMode || block.AvailableInSurvival)
                    && session
                        .GetComponent<MySessionComponentDLC>()
                        .HasDefinitionDLC(block, Sync.MyId)
                    && (
                        session.CreativeToolsEnabled(Sync.MyId)
                        || MySessionComponentResearch.Static.CanUse(session.LocalPlayerId, block.Id)
                    )
                    && (
                        !block.IsInventoryItem || MyPlatformGameSettings.SHOW_INVENTORY_ITEM_BLOCKS
                    );
            }
        }
    }

    private BlockVariants(List<MyCubeBlockDefinition> stages)
    {
        this.stages = stages.ToList();
        members = this.stages.Select(stage => (ItemGroup.Member)new Variant(stage)).ToList();
    }

    private static readonly List<MyCubeBlockDefinition> Single = new() { null };
    private static readonly List<MyCubeBlockDefinition> Derived = new();

    // The block of the variant group and size which lists the others, as the game finds it
    private static MyCubeBlockDefinition First(MyCubeBlockDefinition block) =>
        block.BlockVariantsGroup?.Blocks?.FirstOrDefault(b =>
            b.CubeSize == block.CubeSize && b.BlockStages != null
        );

    // A block without variants is a group of its own, to turn it the way it fits
    private static List<MyCubeBlockDefinition> Stages
    {
        get
        {
            if (MyCubeBuilder.Static is not { IsActivated: true } builder)
                return null;

            var stages = builder.m_cubeBuilderState.CurrentBlockDefinitionStages;
            if (stages.Count > 1)
                return stages;

            MyCubeBlockDefinition block = builder.CurrentBlockDefinition;
            if (block == null)
                return null;

            // A variant taken up directly, like from a toolbar slot holding it, leaves the
            // cube builder without its variants. They are those of the group's first block.
            if (First(block) is { } first)
            {
                if (Derived.Count == 0 || Derived[0] != first)
                {
                    Derived.Clear();
                    Derived.Add(first);
                    foreach (var id in first.BlockStages)
                        if (MyDefinitionManager.Static.TryGetCubeBlockDefinition(id, out var stage))
                            Derived.Add(stage);
                }
                if (Derived.Count > 1 && Derived.Contains(block))
                    return Derived;
            }

            Single[0] = block;
            return Single;
        }
    }

    // Null without a block in hand
    public static BlockVariants InUse()
    {
        if (!Config.Current.GameBlockGroups || Stages is not { } stages)
            return null;

        // The stages change with the block's variant group and size
        if (current == null || !current.stages.SequenceEqual(stages))
            current = new BlockVariants(stages);
        return current;
    }

    public static void OnSessionLoading() => current = null;

    public List<ItemGroup.Member> Members =>
        Stages is { } now && stages.SequenceEqual(now) ? members : null;

    public ItemGroup.Member Held => Members?.FirstOrDefault(m => m.Held);

    // Switches the variant in place, like the game's wheel does
    public bool Equip(MyCharacter character, ItemGroup.Member member, ItemGroup.Member held)
    {
        var index = Members?.IndexOf(member) ?? -1;
        if (index < 0)
            return false;
        if (stages.Count == 1)
            return true;

        // The cube builder gets its variants from the group's first block
        var builder = MyCubeBuilder.Static;
        var state = builder.m_cubeBuilderState;
        if (!state.CurrentBlockDefinitionStages.Contains(stages[index]))
            state.CurrentBlockDefinition = stages[0];
        builder.UpdateCubeBlockStageDefinitionByBlockPair(stages[index].BlockPairName);
        builder.CubeBuilderState.SetCurrentBlockForBlockVariantGroup(
            MyDefinitionManager.Static.GetDefinitionGroup(stages[index].BlockPairName)
        );
        (VariantChanged.GetValue(builder) as Action)?.Invoke();
        return true;
    }
}
