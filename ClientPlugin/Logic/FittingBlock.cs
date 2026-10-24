using System.Collections.Generic;
using System.Linq;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.GUI;
using Sandbox.Game.Gui;
using Sandbox.Game.Multiplayer;
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
    private static readonly Dictionary<MyDefinitionId, Rotation[]> Rotations = new();

    // The block types of conveyor lines, see Shape.Conveyor
    private static readonly HashSet<string> ConveyorTypes = new()
    {
        "MyObjectBuilder_Conveyor",
        "MyObjectBuilder_ConveyorConnector",
        "MyObjectBuilder_ConveyorSorter",
        "MyObjectBuilder_ShipConnector",
    };

    // Another world may load mods that change the definitions
    public static void OnSessionLoading()
    {
        Shapes.Clear();
        Rotations.Clear();
    }

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

    // A new press of the wheel's modifier key alone, for quick mode
    public static bool IsQuickPress(IMyInput input)
    {
        var config = Config.Current;
        if (!config.QuickFit || input.DeltaMouseScrollWheelValue() != 0)
            return false;

        var ctrl = input.IsAnyCtrlKeyPressed();
        var shift = input.IsAnyShiftKeyPressed();
        var alt = input.IsAnyAltKeyPressed();
        return config.FitScrollModifier switch
        {
            Config.FitModifier.Shift => !ctrl
                && !alt
                && (
                    input.IsNewKeyPressed(MyKeys.LeftShift)
                    || input.IsNewKeyPressed(MyKeys.RightShift)
                ),
            Config.FitModifier.Ctrl => !shift
                && !alt
                && (
                    input.IsNewKeyPressed(MyKeys.LeftControl)
                    || input.IsNewKeyPressed(MyKeys.RightControl)
                ),
            Config.FitModifier.Alt => !ctrl
                && !shift
                && (
                    input.IsNewKeyPressed(MyKeys.LeftAlt) || input.IsNewKeyPressed(MyKeys.RightAlt)
                ),
            _ => false,
        };
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

    // The group's blocks in the size of the aimed grid, as the cube builder would place them,
    // and the empty cell they would go into
    public sealed class Aimed
    {
        public MyCubeGrid Grid;
        public Target Target;
        public Rotation Current;
        public readonly List<ItemGroup.Member> Members = new();
        public readonly List<MyCubeBlockDefinition> Definitions = new();
        public readonly List<Candidate> Candidates = new();
        public readonly Dictionary<MySlimBlock, Placed> Neighbors = new();

        public Placed Lookup(Int3 cell) => Neighbor(Grid, cell, Neighbors);

        // The best rotation of each member at the cell, null for those that don't fit
        public Pick?[] Evaluate() => BlockFit.Evaluate(Candidates, Target, Current, Lookup);
    }

    // Null without a group's block in hand aiming at an empty cell of a grid
    public static Aimed Aim(ItemGroup group)
    {
        var builder = MyCubeBuilder.Static;
        if (
            group == null
            || builder is not { IsActivated: true }
            || !Aim(builder, out var grid, out var position, out var addDir)
        )
            return null;

        var space = builder.m_gizmo.SpaceDefault;
        // The new cell is on the aimed block's face, which points to it
        var aimed = position - addDir;
        var result = new Aimed
        {
            Grid = grid,
            Current = RotationOf(space.m_localMatrixAdd),
            Target = new Target
            {
                AddPos = new Int3(position.X, position.Y, position.Z),
                AddDir = new Int3(addDir.X, addDir.Y, addDir.Z),
                Aimed = new Int3(aimed.X, aimed.Y, aimed.Z),
                LargePortSpan = grid.GridSizeEnum == MyCubeSize.Small ? 3 : 1,
            },
        };

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
                || result.Definitions.Contains(definition)
                || dlc?.GetFirstMissingDefinitionDLC(definition, Sync.MyId) != null
            )
                continue;
            result.Members.Add(candidate);
            result.Definitions.Add(definition);
            result.Candidates.Add(
                new Candidate { Shape = ShapeOf(definition), Rotations = RotationsOf(definition) }
            );
        }

        return result;
    }

    // Whether the press was for this, with a group's block in hand aiming at a grid cell
    // Direction 0 keeps the block in hand if it fits, turned to fit, and stays silent when
    // nothing fits, unless the group has no block of the grid's size: the quick press of
    // the wheel's modifier key
    public static bool Select(ItemGroupToolbarItem item, MyCharacter character, int direction)
    {
        var aimed = Aim(item.Group);
        if (aimed == null)
            return false;

        var pick = Next(
            aimed.Candidates,
            aimed.Members.IndexOf(item.Group.Held),
            direction,
            aimed.Current,
            aimed.Target,
            aimed.Lookup
        );
        // The quick press tells only when the group has no block of the grid's size at all
        if (!pick.HasValue && (direction != 0 || aimed.Candidates.Count == 0))
        {
            NotifyNoFit();
            Log(aimed, "no fitting block");
        }
        if (pick.HasValue)
            Take(item, character, aimed, pick.Value);
        return true;
    }

    // Equips the picked block of the group, turned to fit
    public static bool Take(
        ItemGroupToolbarItem item,
        MyCharacter character,
        Aimed aimed,
        Pick pick
    )
    {
        // The cube builder takes up the rotation it remembers for the block, as it does
        // when switching blocks. Both sizes, like MyCubeBuilderState.UpdateCubeBlockDefinition.
        var builder = MyCubeBuilder.Static;
        var picked = aimed.Definitions[pick.Index];
        var matrix = MatrixOf(pick.Rotation);
        var rotation = Quaternion.CreateFromRotationMatrix(matrix);
        var pairs = MyDefinitionManager.Static.GetDefinitionGroup(picked.BlockPairName);
        foreach (var definition in new[] { pairs?.Small, pairs?.Large, picked })
            if (definition != null)
                builder.m_cubeBuilderState.RotationsByDefinitionHash[definition.Id] = rotation;

        var member = aimed.Members[pick.Index];
        var held = item.Group.Held;
        if (member != held && !item.Equip(character, member, held))
            return false;

        pendingBlock = picked.Id;
        pendingRotation = matrix;
        pendingFrames = RotationFrames;
        ApplyRotation();
        return true;
    }

    // What the matcher saw, so a missed fit can be reproduced in tests/BlockFit.Tests
    private static void Log(Aimed aimed, string what)
    {
        var grid = aimed.Grid;
        var target = aimed.Target;
        Placed Lookup(Int3 cell) => aimed.Lookup(cell);
        string Describe(Placed block) =>
            block == null
                ? "empty"
                : $"{block.Shape.Name} at {block.Position} x->{block.Rotation.X} y->{block.Rotation.Y}";

        var text = new System.Text.StringBuilder();
        text.Append(
            $"ToolbarManager: {what} on a {grid.GridSizeEnum} grid at {target.AddPos}, "
                + $"from {target.AddDir}, aimed: {Describe(Lookup(target.Aimed))}"
        );
        foreach (var d in Int3.Directions)
            text.Append($"; {d}: {Describe(Lookup(target.AddPos + d))}");
        foreach (var candidate in aimed.Candidates)
        {
            var best = candidate
                .Rotations.Select(r =>
                    Score(
                        new Placed(candidate.Shape, r, Position(candidate.Shape, r, target)),
                        Lookup,
                        target
                    )
                )
                .Where(score => score.HasValue)
                .DefaultIfEmpty()
                .Min();
            // A block overlapping others is placed elsewhere than the gap
            var overlaps = candidate
                .Rotations.Select(r =>
                    new Placed(candidate.Shape, r, Position(candidate.Shape, r, target))
                        .Cells.Select(Lookup)
                        .FirstOrDefault(other => other != null)
                )
                .Where(other => other != null)
                .ToList();
            var why =
                overlaps.Count == 0
                    ? ""
                    : $", overlaps {overlaps[0].Shape.Name} at {overlaps[0].Position} "
                        + $"in {overlaps.Count}/{candidate.Rotations.Length} rotations";
            text.Append(
                $"; {candidate.Shape.Name}: {(best.HasValue ? best.Value.ToString("0.000") : "no fit")}{why}"
            );
        }
        MyLog.Default.WriteLine(text.ToString());
    }

    private static void NotifyNoFit()
    {
        // The sound of a placement the game refuses. Played directly: MyGuiAudio mutes it
        // without a helmet in vacuum with realistic sounds, and within a frame of the last.
        if (MyAudio.Static?.PlaySound(MyGuiAudio.GetCue(MyGuiSounds.HudUnable)) == null)
            MyLog.Default.WriteLine("ToolbarManager: the game didn't play the refusal sound");
        noFit ??= new MyHudNotification(
            MyStringId.GetOrCompute("No block of the group fits here"),
            1500,
            level: MyNotificationLevel.Important
        );
        MyHud.Notifications.Remove(noFit);
        MyHud.Notifications.Add(noFit);
    }

    // The grid and the empty cell the cube builder places the block in hand into. A block
    // without a variant of the grid's size gets no placement box on it, then the cell is
    // the last empty one along the view before the aimed block.
    private static bool Aim(
        MyCubeBuilder builder,
        out MyCubeGrid grid,
        out Vector3I position,
        out Vector3I addDir
    )
    {
        var space = builder.m_gizmo.SpaceDefault;
        grid = builder.m_currentGrid;
        position = space.m_addPos;
        addDir = space.m_addDir;
        if (
            grid != null
            && !builder.DynamicMode
            && grid.GridSizeEnum == builder.CurrentBlockDefinition?.CubeSize
        )
            return !grid.CubeExists(position);

        var start = MyBlockBuilderBase.IntersectionStart;
        var ray = new LineD(
            start,
            start
                + MyBlockBuilderBase.IntersectionDirection * MyBlockBuilderBase.IntersectionDistance
        );
        if (!MyCubeGrid.TryRayCastGrid(ref ray, out grid, out _))
            return false;

        var cells = new List<Vector3I>();
        grid.RayCastCells(ray.From, ray.To, cells);
        for (var i = 1; i < cells.Count; i++)
        {
            if (!grid.CubeExists(cells[i]))
                continue;
            position = cells[i - 1];
            addDir = position - cells[i];
            return !grid.CubeExists(position) && addDir.RectangularLength() == 1;
        }

        return false;
    }

    // Equipping the block activates the cube builder anew, and taking up the grid again
    // aligns the block to its default mount point. So the rotation is applied on some
    // frames, as a rotation by the player, which the alignment leaves be.
    // Enough for the cube builder to switch to the grid's block size, too
    private const int RotationFrames = 30;

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

    private static Rotation[] RotationsOf(MyCubeBlockDefinition definition)
    {
        if (!Rotations.TryGetValue(definition.Id, out var rotations))
            Rotations[definition.Id] = rotations = Rotation
                .All.Where(r =>
                    MyCubeBuilder.CheckValidBlockRotation(
                        MatrixOf(r),
                        definition.Direction,
                        definition.Rotation
                    )
                )
                .ToArray();
        return rotations;
    }

    private static Shape ShapeOf(MyCubeBlockDefinition definition)
    {
        if (Shapes.TryGetValue(definition.Id, out var shape))
            return shape;

        shape = new Shape
        {
            Name = definition.Id.SubtypeName,
            Size = new Int3(definition.Size.X, definition.Size.Y, definition.Size.Z),
            Center = new Int3(definition.Center.X, definition.Center.Y, definition.Center.Z),
            MirroringCenter = new Int3(
                definition.MirroringCenter.X,
                definition.MirroringCenter.Y,
                definition.MirroringCenter.Z
            ),
            Mounts = (definition.MountPoints ?? new MyCubeBlockDefinition.MountPoint[0])
                .Select(m => new Mount
                {
                    Normal = new Int3(m.Normal.X, m.Normal.Y, m.Normal.Z),
                    Start = new double[] { m.Start.X, m.Start.Y, m.Start.Z },
                    End = new double[] { m.End.X, m.End.Y, m.End.Z },
                })
                .ToArray(),
            Ports = PortsOf(definition),
            Family =
                definition.CubeDefinition != null ? Armor
                : definition.BlockVariantsGroup != null
                    ? definition.BlockVariantsGroup.Id.ToString()
                : definition.Id.ToString(),
            Conveyor = ConveyorTypes.Contains(definition.Id.TypeId.ToString()),
        };
        Shapes[definition.Id] = shape;
        return shape;
    }

    // The conveyor and upgrade ports of the block's model, as MyConveyorLine.GetBlockLinePositions
    // and MyMultilineConveyorEndpoint.GetLinePositions(.., "detector_upgrade") find them
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
            var name = dummy.Key.ToLower();
            var parts = name.Split('_');
            var kind =
                parts.Length >= 2 && parts[0] == "detector" && parts[1].StartsWith("conveyor")
                    ? (parts.Length > 2 && parts[2] == "small" ? SmallPort : LargePort)
                : name.Contains("detector_upgrade") ? UpgradePort
                : null;
            if (kind == null)
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
                    Kind = kind,
                }
            );
        }
        return ports.ToArray();
    }
}
