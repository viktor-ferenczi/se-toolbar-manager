using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Sandbox.Definitions;
using Sandbox.Engine.Physics;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.Gui;
using Sandbox.Game.SessionComponents;
using Sandbox.Game.World;
using VRage.Game;
using VRage.Game.ObjectBuilders.Components;
using VRage.Input;
using VRage.Utils;
using VRageMath;

namespace ClientPlugin.Logic;

// Picks the block the player faces as the block to place, with its type, subtype, skin and
// orientation
public static class FacedBlock
{
    private static MyHudNotification notification;

    // Meters, well beyond the cube builder's reach
    private const float AimDistance = 100f;

    private static readonly MethodInfo SwitchVariant = AccessTools.Method(
        typeof(MyCubeBuilder),
        "UpdateCubeBlockStageDefinition"
    );

    // True when the press was a pick, which the game must not handle as well
    public static bool HandleGameplayInput()
    {
        if (!Config.Current.PickBlockKey.HasPressed(MyInput.Static))
            return false;

        // On foot or in the spectator camera only. In a seat Ctrl+G is the game's build mode.
        if (
            MyGuiScreenGamePlay.ActiveGameplayScreen != null
            || MySession.Static?.ControlledEntity is not MyCharacter { IsDead: false } character
        )
            return false;

        if (Aim(out var block))
            Pick(block, character);
        else
            Notify("No block in view");
        return true;
    }

    // The block under the crosshair: the first one the camera's view ray hits
    private static bool Aim(out MySlimBlock block)
    {
        block = null;
        var camera = MySector.MainCamera;
        var hits = new List<MyPhysics.HitInfo>();
        MyPhysics.CastRay(
            camera.Position,
            camera.Position + camera.ForwardVector * AimDistance,
            hits
        );
        var controlled = MySession.Static.ControlledEntity?.Entity;
        foreach (var hit in hits)
        {
            var entity = hit.HkHitInfo.GetHitEntity();
            if (entity == controlled)
                continue;
            if (entity is not MyCubeGrid grid)
                return false;

            // Just inside the surface, as the game does for projectiles and decals
            block = grid.GetTargetedBlock(hit.Position - 0.001f * hit.HkHitInfo.Normal);
            return block != null && block.FatBlock is not { IsSubBlock: true };
        }
        return false;
    }

    // Equips the block like its toolbar slot would, so the game's research, DLC and safe zone
    // rules apply. The color and skin go to the new blocks, as the game's color picker sets them.
    private static void Pick(MySlimBlock block, MyCharacter character)
    {
        var definition = block.BlockDefinition;
        var player = MySession.Static.LocalHumanPlayer;
        if (
            player == null
            || !MySessionComponentSafeZones.IsActionAllowed(character, MySafeZoneAction.Building)
        )
            return;

        // The cube builder takes up the rotation it remembers for the block
        block.Orientation.GetMatrix(out var rotation);
        var builder = MyCubeBuilder.Static;
        var state = builder.m_cubeBuilderState;
        state.RotationsByDefinitionHash[definition.Id] = Quaternion.CreateFromRotationMatrix(
            rotation
        );

        // The game equips a block by its pair and variant group: it takes the pair's block of
        // the builder's size and the variant used last, so make those the picked block's
        state.SetCubeSize(definition.CubeSize);
        state.SetCurrentBlockForBlockVariantGroup(
            MyDefinitionManager.Static.GetDefinitionGroup(definition.BlockPairName)
        );

        // Put the cube builder away first, like the game's toolbar does between blocks
        if (builder.IsActivated && builder.CurrentBlockDefinition != definition)
            character.SwitchToWeapon(null);
        var placer = new MyDefinitionId(typeof(MyObjectBuilder_CubePlacer));
        if (character.CurrentWeapon?.DefinitionId != placer)
            character.SwitchToWeapon(placer);
        builder.Activate(definition.Id);
        if (!builder.IsActivated)
            return;
        // The builder may come up with another variant of the block's group. Switch to the
        // picked one like its mouse wheel does.
        if (builder.CurrentBlockDefinition != definition)
            SwitchVariant.Invoke(builder, new object[] { definition });

        FittingBlock.TurnTo(definition.Id, rotation);
        if (Config.Current.PickBlockSkin)
        {
            var skin = block.SkinSubtypeId;
            player.BuildArmorSkin =
                skin == MyStringHash.NullOrEmpty ? string.Empty : skin.ToString();
        }
        if (Config.Current.PickBlockColor)
            player.ChangeOrSwitchToColor(block.ColorMaskHSV);
    }

    private static void Notify(string text)
    {
        if (notification != null)
            MyHud.Notifications.Remove(notification);
        notification = new MyHudNotification(
            MyStringId.GetOrCompute(text),
            1500,
            level: MyNotificationLevel.Important
        );
        MyHud.Notifications.Add(notification);
    }
}
