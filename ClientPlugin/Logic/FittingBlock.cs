using System.Collections.Generic;
using System.Linq;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.GUI;
using Sandbox.Game.Gui;
using Sandbox.Game.Multiplayer;
using Sandbox.Game.Screens.Helpers;
using Sandbox.Game.SessionComponents;
using Sandbox.Game.World;
using VRage.Audio;
using VRage.Game;
using VRage.Game.Models;
using VRage.Input;
using VRage.Utils;
using VRageMath;
using static ClientPlugin.Logic.BlockFit;

namespace ClientPlugin.Logic;

// Selects the next block of the group in use that fits where the cube builder aims,
// see BlockFit for what fits
public static class FittingBlock
{
    private static readonly Dictionary<MyDefinitionId, Shape> Shapes = new();

    private static MyHudNotification noFit;

    // The rotation to give the picked block once the cube builder holds it on the grid
    private static MyDefinitionId pendingBlock;
    private static Matrix pendingRotation;
    private static int pendingFrames;

    // 1 for the next fitting block, -1 for the previous one, 0 if not asked for
    public static int Direction(IMyInput input, out bool middleButton)
    {
        var config = Config.Current;
        middleButton = false;

        var delta = input.DeltaMouseScrollWheelValue();
        if (delta != 0 && config.FitScrollModifier != Config.FitModifier.None)
        {
            var ctrl = input.IsAnyCtrlKeyPressed();
            var shift = input.IsAnyShiftKeyPressed();
            var alt = input.IsAnyAltKeyPressed();
            var held = config.FitScrollModifier switch
            {
                Config.FitModifier.Shift => shift && !ctrl && !alt,
                Config.FitModifier.Ctrl => ctrl && !shift && !alt,
                _ => alt && !ctrl && !shift,
            };
            if (held)
                return delta > 0 ? 1 : -1;
        }

        if (
            config.NextFitKey.HasPressed(input)
            || Pressed(input, config.NextFitButton, ref middleButton)
        )
            return 1;
        if (
            config.PreviousFitKey.HasPressed(input)
            || Pressed(input, config.PreviousFitButton, ref middleButton)
        )
            return -1;
        return 0;
    }

    private static bool Pressed(IMyInput input, Config.FitMouseButton choice, ref bool middleButton)
    {
        var button = choice switch
        {
            Config.FitMouseButton.MiddleButton => MyMouseButtonsEnum.Middle,
            Config.FitMouseButton.ExtraButton1 => MyMouseButtonsEnum.XButton1,
            Config.FitMouseButton.ExtraButton2 => MyMouseButtonsEnum.XButton2,
            _ => MyMouseButtonsEnum.None,
        };
        if (
            button == MyMouseButtonsEnum.None
            || !input.IsNewMousePressed(button)
            || input.IsAnyCtrlKeyPressed()
            || input.IsAnyShiftKeyPressed()
            || input.IsAnyAltKeyPressed()
        )
            return false;

        middleButton = button == MyMouseButtonsEnum.Middle;
        return true;
    }

    // Whether the press was for this, with a group's block in hand aiming at a grid cell
    public static bool Select(ItemGroupToolbarItem item, MyCharacter character, int direction)
    {
        var builder = MyCubeBuilder.Static;
        var group = item.Group;
        var grid = builder?.m_currentGrid;
        if (
            group == null
            || builder is not { IsActivated: true }
            || grid == null
            || builder.DynamicMode
        )
            return false;

        var space = builder.m_gizmo.SpaceDefault;
        var position = space.m_addPos;
        if (grid.CubeExists(position))
            return false;

        // The group's blocks in the size of the grid, as the cube builder would place them
        var members = new List<ItemGroup.Member>();
        var definitions = new List<MyCubeBlockDefinition>();
        var dlc = MySession.Static.GetComponent<MySessionComponentDLC>();
        foreach (var candidate in group.Members)
        {
            if (!candidate.Available || !(candidate.Definition is MyCubeBlockDefinition definition))
                continue;
            var pair = MyDefinitionManager.Static.GetDefinitionGroup(definition.BlockPairName);
            definition = grid.GridSizeEnum == MyCubeSize.Large ? pair?.Large : pair?.Small;
            // The game refuses to equip a block of a DLC the player doesn't own
            if (
                definition == null
                || definitions.Contains(definition)
                || dlc?.GetFirstMissingDefinitionDLC(definition, Sync.MyId) != null
            )
                continue;
            members.Add(candidate);
            definitions.Add(definition);
        }

        var held = group.Held;
        var candidates = definitions
            .Select(d => new Candidate { Shape = ShapeOf(d), Rotations = RotationsOf(d) })
            .ToList();
        var placed = new Dictionary<MySlimBlock, Placed>();
        var pick = Next(
            candidates,
            members.IndexOf(held),
            direction,
            RotationOf(space.m_localMatrixAdd),
            new Int3(position.X, position.Y, position.Z),
            cell => Neighbor(grid, cell, placed)
        );
        if (!pick.HasValue)
        {
            // The sound of a placement the game refuses
            MyGuiAudio.PlaySound(MyGuiSounds.HudUnable);
            noFit ??= new MyHudNotification(
                MyStringId.GetOrCompute("No block of the group fits here"),
                1500,
                level: MyNotificationLevel.Important
            );
            MyHud.Notifications.Remove(noFit);
            MyHud.Notifications.Add(noFit);
            return true;
        }

        // The cube builder takes up the rotation it remembers for the block, as it does
        // when switching blocks. Both sizes, like MyCubeBuilderState.UpdateCubeBlockDefinition.
        var picked = definitions[pick.Value.Index];
        var matrix = MatrixOf(pick.Value.Rotation);
        var rotation = Quaternion.CreateFromRotationMatrix(matrix);
        var pairs = MyDefinitionManager.Static.GetDefinitionGroup(picked.BlockPairName);
        foreach (var definition in new[] { pairs?.Small, pairs?.Large, picked })
            if (definition != null)
                builder.m_cubeBuilderState.RotationsByDefinitionHash[definition.Id] = rotation;

        var member = members[pick.Value.Index];
        if (member != held && !item.Equip(character, member, held))
            return true;

        pendingBlock = picked.Id;
        pendingRotation = matrix;
        pendingFrames = RotationFrames;
        ApplyRotation();
        return true;
    }

    // Equipping the block activates the cube builder anew, and taking up the grid again
    // aligns the block to its default mount point. So the rotation is applied on a few
    // frames, as a rotation by the player, which the alignment leaves be.
    private const int RotationFrames = 10;

    public static void ApplyRotation()
    {
        if (pendingFrames == 0)
            return;
        pendingFrames--;

        var builder = MyCubeBuilder.Static;
        if (
            builder is not { IsActivated: true }
            || builder.m_currentGrid == null
            || builder.CurrentBlockDefinition?.Id != pendingBlock
        )
            return;

        var space = builder.m_gizmo.SpaceDefault;
        var matrix = pendingRotation;
        matrix.Translation = space.m_localMatrixAdd.Translation;
        space.m_localMatrixAdd = matrix;
        builder.m_customRotation = true;
    }

    private static Placed Neighbor(
        MyCubeGrid grid,
        Int3 cell,
        Dictionary<MySlimBlock, Placed> placed
    )
    {
        var slim = grid.GetCubeBlock(new Vector3I(cell.X, cell.Y, cell.Z));
        if (slim == null)
            return null;
        if (!placed.TryGetValue(slim, out var block))
        {
            var o = slim.Orientation;
            block = new Placed(
                ShapeOf(slim.BlockDefinition),
                Find(-Vector(o.Left), Vector(o.Up)),
                new Int3(slim.Position.X, slim.Position.Y, slim.Position.Z)
            );
            placed[slim] = block;
        }
        return block;
    }

    private static Int3 Vector(Base6Directions.Direction direction)
    {
        var v = Base6Directions.GetIntVector(direction);
        return new Int3(v.X, v.Y, v.Z);
    }

    private static Int3 Round(Vector3 v) =>
        new Int3(
            (int)System.Math.Round(v.X),
            (int)System.Math.Round(v.Y),
            (int)System.Math.Round(v.Z)
        );

    private static Rotation Find(Int3 x, Int3 y) =>
        Rotation.All.FirstOrDefault(r => r.X.Equals(x) && r.Y.Equals(y)) ?? Rotation.All[0];

    // Free rotation can leave the axes, then nothing is current
    private static Rotation RotationOf(Matrix m) => Find(Round(m.Right), Round(m.Up));

    private static Matrix MatrixOf(Rotation r) =>
        Matrix.CreateWorld(
            Vector3.Zero,
            new Vector3(-r.Z.X, -r.Z.Y, -r.Z.Z),
            new Vector3(r.Y.X, r.Y.Y, r.Y.Z)
        );

    private static Rotation[] RotationsOf(MyCubeBlockDefinition definition) =>
        Rotation
            .All.Where(r =>
                MyCubeBuilder.CheckValidBlockRotation(
                    MatrixOf(r),
                    definition.Direction,
                    definition.Rotation
                )
            )
            .ToArray();

    private static Shape ShapeOf(MyCubeBlockDefinition definition)
    {
        if (Shapes.TryGetValue(definition.Id, out var shape))
            return shape;

        shape = new Shape
        {
            Name = definition.Id.SubtypeName,
            Size = new Int3(definition.Size.X, definition.Size.Y, definition.Size.Z),
            Center = new Int3(definition.Center.X, definition.Center.Y, definition.Center.Z),
            Mounts = (definition.MountPoints ?? new MyCubeBlockDefinition.MountPoint[0])
                .Select(m => new Mount
                {
                    Normal = new Int3(m.Normal.X, m.Normal.Y, m.Normal.Z),
                    Start = new double[] { m.Start.X, m.Start.Y, m.Start.Z },
                    End = new double[] { m.End.X, m.End.Y, m.End.Z },
                })
                .ToArray(),
            Ports = PortsOf(definition),
        };
        Shapes[definition.Id] = shape;
        return shape;
    }

    // The conveyor ports of the block's model, as MyConveyorLine.GetBlockLinePositions finds them
    private static Port[] PortsOf(MyCubeBlockDefinition definition)
    {
        if (string.IsNullOrEmpty(definition.Model))
            return new Port[0];

        var dummies = MyModels.GetModelOnlyDummies(definition.Model)?.Dummies;
        if (dummies == null)
            return new Port[0];

        var cubeSize = MyDefinitionManager.Static.GetCubeSize(definition.CubeSize);
        var half = new Vector3(definition.Size) * 0.5f * cubeSize;
        var ports = new List<Port>();
        foreach (var dummy in dummies)
        {
            var parts = dummy.Key.ToLower().Split('_');
            if (parts.Length < 2 || parts[0] != "detector" || !parts[1].StartsWith("conveyor"))
                continue;

            var p = dummy.Value.Matrix.Translation + definition.ModelOffset + half;
            var cell = Vector3I.Min(
                Vector3I.Max(Vector3I.Floor(p / cubeSize), Vector3I.Zero),
                definition.Size - Vector3I.One
            );
            var direction = Base6Directions.GetIntVector(
                Base6Directions.GetDirection(
                    Vector3.Normalize(
                        Vector3.DominantAxisProjection(
                            (p - (new Vector3(cell) + Vector3.Half) * cubeSize) / cubeSize
                        )
                    )
                )
            );
            var local = cell - definition.Center;
            ports.Add(
                new Port
                {
                    Cell = new Int3(local.X, local.Y, local.Z),
                    Direction = new Int3(direction.X, direction.Y, direction.Z),
                    Small = parts.Length > 2 && parts[2] == "small",
                }
            );
        }
        return ports.ToArray();
    }
}
