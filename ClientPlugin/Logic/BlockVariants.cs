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
// block size: the blocks the wheel steps through in the game
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

    private static List<MyCubeBlockDefinition> Stages =>
        MyCubeBuilder.Static is { IsActivated: true } builder
            ? builder.m_cubeBuilderState.CurrentBlockDefinitionStages
            : null;

    // Null without a block in hand which has variants
    public static BlockVariants InUse()
    {
        if (!Config.Current.GameBlockGroups || Stages is not { Count: > 1 } stages)
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

        var builder = MyCubeBuilder.Static;
        builder.UpdateCubeBlockStageDefinitionByBlockPair(stages[index].BlockPairName);
        builder.CubeBuilderState.SetCurrentBlockForBlockVariantGroup(
            MyDefinitionManager.Static.GetDefinitionGroup(stages[index].BlockPairName)
        );
        (VariantChanged.GetValue(builder) as Action)?.Invoke();
        return true;
    }
}
