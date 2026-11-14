using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using ClientPlugin.Settings.Tools;
using HarmonyLib;
using Sandbox.Definitions;
using Sandbox.Engine.Physics;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.GUI;
using Sandbox.Game.Gui;
using Sandbox.Game.Multiplayer;
using Sandbox.Game.SessionComponents;
using Sandbox.Game.SessionComponents.Clipboard;
using Sandbox.Game.World;
using Sandbox.Graphics.GUI;
using VRage.Audio;
using VRage.Game;
using VRage.Game.ObjectBuilders.Components;
using VRage.Game.ObjectBuilders.Definitions;
using VRage.Input;
using VRage.Utils;
using VRageMath;

namespace ClientPlugin.Logic;

// Picks the block the player faces as the block to place, with its type, subtype, skin and
// orientation, or cuts it: removes it like the cube builder's removal does and picks it.
public static class FacedBlock
{
    private static MyHudNotification notification;
    private static int handledFrame = -1;

    // Meters, well beyond the cube builder's reach
    private const float AimDistance = 100f;

    private static readonly MethodInfo SwitchVariant = AccessTools.Method(
        typeof(MyCubeBuilder),
        "UpdateCubeBlockStageDefinition"
    );

    // Whether a pick or cut key is held, with exactly its modifiers
    public static bool IsKeyHeld(IMyInput input)
    {
        var config = Config.Current;
        return config.PickBlockKey.IsPressed(input)
            || config.PickBlockSecondKey.IsPressed(input)
            || config.CutBlockKey.IsPressed(input);
    }

    // True when the press was a pick or a cut, which the game must not handle as well.
    // Called from the gameplay screen's input and, earlier in the frame, from the clipboard's
    // delete key, where Ctrl+Delete with creative tools is the game's delete of the faced grid.
    public static bool HandleGameplayInput()
    {
        var frame = MySession.Static?.GameplayFrameCounter ?? 0;
        if (frame == handledFrame)
            return true;

        var config = Config.Current;
        var input = MyInput.Static;
        var pick =
            config.PickBlockKey.HasPressed(input) || config.PickBlockSecondKey.HasPressed(input);
        if (!pick && !config.CutBlockKey.HasPressed(input))
            return false;

        // On foot or in the spectator camera only. In a seat Ctrl+G is the game's build mode,
        // and while pasting Ctrl+Delete is left to the game.
        var clipboard = MyClipboardComponent.Static;
        if (
            MyGuiScreenGamePlay.ActiveGameplayScreen != null
            || MySession.Static?.ControlledEntity is not MyCharacter { IsDead: false } character
            || clipboard == null
            || clipboard.Clipboard.IsActive
            || clipboard.FloatingObjectClipboard.IsActive
            || clipboard.VoxelClipboard.IsActive
        )
            return false;

        handledFrame = frame;
        if (!Aim(out var block, out var cell))
            Notify("No block in view");
        else if (pick)
            Pick(block, character);
        else
            Cut(block, cell, character);
        return true;
    }

    // The block under the crosshair: the first one the camera's view ray hits, like the
    // game's own target of Ctrl+Delete, and the cell of it that was hit
    private static bool Aim(out MySlimBlock block, out Vector3I cell)
    {
        block = null;
        cell = default;
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
            var inside = hit.Position - 0.001f * hit.HkHitInfo.Normal;
            var local = Vector3D.Transform(inside, grid.PositionComp.WorldMatrixNormalizedInv);
            grid.FixTargetCube(out cell, (Vector3)(local * grid.GridSizeR));
            block = grid.GetCubeBlock(cell);
            return block != null && block.FatBlock is not { IsSubBlock: true };
        }
        return false;
    }

    // Equips the block like its toolbar slot would, so the game's research, DLC and safe zone
    // rules apply. The skin is the one new blocks get, as the game's color picker sets it.
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
        var skin = block.SkinSubtypeId;
        player.BuildArmorSkin = skin == MyStringHash.NullOrEmpty ? string.Empty : skin.ToString();
        if (Config.Current.PickBlockColor)
            player.ChangeOrSwitchToColor(block.ColorMaskHSV);
    }

    private static void Cut(MySlimBlock block, Vector3I cell, MyCharacter character)
    {
        // The local server would take the request in survival too, giving blocks away for free
        if (!MySession.Static.CreativeMode && !MySession.Static.CreativeToolsEnabled(Sync.MyId))
        {
            Notify("Cutting a block needs creative mode");
            return;
        }

        var grid = block.CubeGrid;
        var blocks = WithMirrors(grid, cell);
        if (blocks.Any(b => b.FatBlock is MyCockpit { Pilot: not null }))
        {
            Notify("The seat is occupied");
            return;
        }

        void Remove()
        {
            if (grid.Closed || blocks.Any(b => grid.GetCubeBlock(b.Min) != b))
                return;
            Pick(block, character);
            MyGuiAudio.PlaySound(MyGuiSounds.HudDeleteBlock);
            // The cube builder's own removal request, which the server checks and Undo records
            grid.RazeBlocks(blocks.Select(b => b.Min).ToList(), 0L, Sync.MyId);
        }

        // A pasted grid may be in parts already
        var parts = PartsWithout(grid, blocks);
        if (parts <= PartsWithout(grid, new List<MySlimBlock>()))
        {
            Remove();
            return;
        }

        var what = blocks.Count == 1 ? "this block" : $"these {blocks.Count} blocks";
        MyGuiSandbox.AddScreen(
            MyGuiSandbox.CreateMessageBox(
                MyMessageBoxStyleEnum.Info,
                MyMessageBoxButtonsType.YES_NO,
                new StringBuilder(
                    $"Removing {what} splits the grid into {parts} parts.\nCut anyway?"
                ),
                new StringBuilder("CUT BLOCK"),
                callback: result =>
                {
                    if (result == MyGuiScreenMessageBox.ResultEnum.YES)
                        Remove();
                }
            )
        );
    }

    // The block at the cell and, in symmetry mode, the blocks at its mirrored cells, which is
    // what the cube builder removes
    private static List<MySlimBlock> WithMirrors(MyCubeGrid grid, Vector3I cell)
    {
        var cells = new List<Vector3I> { cell };
        if (MyCubeBuilder.Static.UseSymmetry)
        {
            // MyCubeBuilderGizmo.MirrorBlockByPlane
            void Mirror(Vector3I? plane, int axis, int odd)
            {
                for (int i = 0, count = plane.HasValue ? cells.Count : 0; i < count; i++)
                {
                    var mirrored = cells[i];
                    mirrored[axis] = 2 * plane.Value[axis] - mirrored[axis] + odd;
                    cells.Add(mirrored);
                }
            }
            Mirror(grid.XSymmetryPlane, 0, grid.XSymmetryOdd ? -1 : 0);
            Mirror(grid.YSymmetryPlane, 1, grid.YSymmetryOdd ? -1 : 0);
            Mirror(grid.ZSymmetryPlane, 2, grid.ZSymmetryOdd ? 1 : 0);
        }
        return cells
            .Select(grid.GetCubeBlock)
            .Where(b => b != null && b.FatBlock is not { IsSubBlock: true })
            .Distinct()
            .ToList();
    }

    // The number of connected parts of the grid left without the blocks, as MyDisconnectHelper
    // finds them
    private static int PartsWithout(MyCubeGrid grid, List<MySlimBlock> removed)
    {
        var seen = new HashSet<MySlimBlock>(removed);
        var queue = new Queue<MySlimBlock>();
        var parts = 0;
        foreach (var first in grid.GetBlocks())
        {
            if (!seen.Add(first))
                continue;
            parts++;
            queue.Enqueue(first);
            while (queue.Count > 0)
                foreach (var neighbor in queue.Dequeue().Neighbours)
                    if (seen.Add(neighbor))
                        queue.Enqueue(neighbor);
        }
        return parts;
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
