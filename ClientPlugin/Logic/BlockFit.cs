using System;
using System.Collections.Generic;
using System.Linq;

namespace ClientPlugin.Logic;

// Finds the blocks of a group that fit an empty cell of a grid: their face matches the face
// of the aimed block, the neighbors of their family rank them, and their conveyor and
// upgrade ports line up with the neighbors' ports.
// It knows no game types, so it can be tested offline on real block definitions.
//
// Coordinates follow the game: a block's local cells run from 0 to Size - 1 along x right,
// y up and z backward, and a mountpoint's corners are in that box, from 0 to Size.
// A placed block puts its Center cell on Position, and its local cell c on
// Position + Rotation * (c - Center).
public static class BlockFit
{
    // A face is sampled on a grid of this many points per cell edge
    private const int Samples = 20;

    // Shapes this much worse than the best one still fit, in face units
    private const double Tolerance = 0.05;

    // The family of the blocks built from the game's armor shapes
    public const string Armor = "armor";

    // Port kinds, ports connect to ports of the same kind
    public const string LargePort = "large",
        SmallPort = "small",
        UpgradePort = "upgrade";

    // An aimed face that differs more than this doesn't fit at all
    private const double MaxMismatch = 1.0;

    public struct Int3 : IEquatable<Int3>
    {
        public readonly int X,
            Y,
            Z;

        public Int3(int x, int y, int z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public int this[int axis] =>
            axis == 0 ? X
            : axis == 1 ? Y
            : Z;

        public static Int3 operator +(Int3 a, Int3 b) => new Int3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

        public static Int3 operator -(Int3 a, Int3 b) => new Int3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

        public static Int3 operator -(Int3 a) => new Int3(-a.X, -a.Y, -a.Z);

        public bool Equals(Int3 other) => X == other.X && Y == other.Y && Z == other.Z;

        public override bool Equals(object obj) => obj is Int3 other && Equals(other);

        public override int GetHashCode() => (X * 397 ^ Y) * 397 ^ Z;

        public override string ToString() => $"({X}, {Y}, {Z})";

        public static readonly Int3[] Directions =
        {
            new Int3(1, 0, 0),
            new Int3(-1, 0, 0),
            new Int3(0, 1, 0),
            new Int3(0, -1, 0),
            new Int3(0, 0, 1),
            new Int3(0, 0, -1),
        };
    }

    // A proper rotation of the grid axes, as the images of the local x, y and z axes
    public sealed class Rotation
    {
        public readonly Int3 X,
            Y,
            Z;

        public Rotation(Int3 x, Int3 y, Int3 z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public Int3 Apply(Int3 v) =>
            new Int3(
                X.X * v.X + Y.X * v.Y + Z.X * v.Z,
                X.Y * v.X + Y.Y * v.Y + Z.Y * v.Z,
                X.Z * v.X + Y.Z * v.Y + Z.Z * v.Z
            );

        public Int3 Inverse(Int3 v) =>
            new Int3(
                X.X * v.X + X.Y * v.Y + X.Z * v.Z,
                Y.X * v.X + Y.Y * v.Y + Y.Z * v.Z,
                Z.X * v.X + Z.Y * v.Y + Z.Z * v.Z
            );

        public void Inverse(double[] v, double[] result)
        {
            result[0] = X.X * v[0] + X.Y * v[1] + X.Z * v[2];
            result[1] = Y.X * v[0] + Y.Y * v[1] + Y.Z * v[2];
            result[2] = Z.X * v[0] + Z.Y * v[1] + Z.Z * v[2];
        }

        public bool Equals(Rotation other) => X.Equals(other.X) && Y.Equals(other.Y);

        public override string ToString() => $"x->{X} y->{Y} z->{Z}";

        // The 24 rotations, the identity first
        public static readonly Rotation[] All = Int3
            .Directions.SelectMany(x =>
                Int3.Directions.Where(y => Dot(x, y) == 0)
                    .Select(y => new Rotation(x, y, Cross(x, y)))
            )
            .OrderBy(r => r.X.Equals(new Int3(1, 0, 0)) && r.Y.Equals(new Int3(0, 1, 0)) ? 0 : 1)
            .ToArray();

        private static int Dot(Int3 a, Int3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

        private static Int3 Cross(Int3 a, Int3 b) =>
            new Int3(a.Y * b.Z - a.Z * b.Y, a.Z * b.X - a.X * b.Z, a.X * b.Y - a.Y * b.X);
    }

    public struct Mount
    {
        public Int3 Normal;
        public double[] Start,
            End;
    }

    public struct Port
    {
        // Cell relative to the block's Center, direction the port faces
        public Int3 Cell,
            Direction;

        // Ports of the same kind connect: large or small conveyor ports, upgrade ports
        public string Kind;
    }

    // What a block definition looks like to the matcher
    public sealed class Shape
    {
        public string Name;
        public Int3 Size,
            Center,
            MirroringCenter;
        public Mount[] Mounts;
        public Port[] Ports;

        // Blocks of a family stick to each other: armor, the blocks of a block variant
        // group like the beams, or else a block alone
        public string Family;

        // Tubes, junctions, sorters and connectors carry the conveyor line, so none of
        // their ports may end against a block. Other blocks, like a thruster or a tank, may
        // leave a port against a beam or a wall, as the game allows.
        public bool Conveyor;

        public bool HasPorts => Ports.Length != 0;

        // Sampled faces by rotation, cell relative to the block's position and direction.
        // A face depends only on the shape, so it is sampled once per game session.
        public readonly Dictionary<(Int3, Int3, Int3, Int3), bool[]> Faces = new();
    }

    public sealed class Placed
    {
        public readonly Shape Shape;
        public readonly Rotation Rotation;
        public readonly Int3 Position;

        public Placed(Shape shape, Rotation rotation, Int3 position)
        {
            Shape = shape;
            Rotation = rotation;
            Position = position;
        }

        public IEnumerable<Int3> Cells
        {
            get
            {
                for (var x = 0; x < Shape.Size.X; x++)
                for (var y = 0; y < Shape.Size.Y; y++)
                for (var z = 0; z < Shape.Size.Z; z++)
                    yield return Position + Rotation.Apply(new Int3(x, y, z) - Shape.Center);
            }
        }

        // Ports in grid cells and directions
        public IEnumerable<Port> Ports =>
            Shape.Ports.Select(p => new Port
            {
                Cell = Position + Rotation.Apply(p.Cell),
                Direction = Rotation.Apply(p.Direction),
                Kind = p.Kind,
            });

        // The mountpoints on the face of a grid cell of this block towards the direction,
        // sampled at the same grid points for both blocks touching on that face
        public bool[] Face(Int3 cell, Int3 direction)
        {
            var key = (Rotation.X, Rotation.Y, cell - Position, direction);
            if (!Shape.Faces.TryGetValue(key, out var mask))
                Shape.Faces[key] = mask = Sample(cell, direction);
            return mask;
        }

        private bool[] Sample(Int3 cell, Int3 direction)
        {
            var mask = new bool[Samples * Samples];
            var local = Rotation.Inverse(direction);
            var mounts = Shape.Mounts.Where(m => m.Normal.Equals(local)).ToArray();
            if (mounts.Length == 0)
                return mask;

            var axis =
                direction.X != 0 ? 0
                : direction.Y != 0 ? 1
                : 2;
            var u = (axis + 1) % 3;
            var v = (axis + 2) % 3;
            var point = new double[3];
            var x = new double[3];
            for (var i = 0; i < Samples; i++)
            for (var j = 0; j < Samples; j++)
            {
                // Grid point relative to Position, a cell spans -0.5 to 0.5 around its index
                point[axis] = cell[axis] - Position[axis] + 0.5 * direction[axis];
                point[u] = cell[u] - Position[u] + (i + 0.5) / Samples - 0.5;
                point[v] = cell[v] - Position[v] + (j + 0.5) / Samples - 0.5;
                Rotation.Inverse(point, x);
                for (var k = 0; k < 3; k++)
                    x[k] += Shape.Center[k] + 0.5;
                mask[i * Samples + j] = mounts.Any(m => Contains(m, x));
            }

            return mask;
        }

        private static bool Contains(Mount m, double[] x)
        {
            for (var k = 0; k < 3; k++)
            {
                var low = Math.Min(m.Start[k], m.End[k]);
                var high = Math.Max(m.Start[k], m.End[k]);
                if (m.Normal[k] != 0 ? Math.Abs(x[k] - low) > 0.01 : x[k] < low || x[k] > high)
                    return false;
            }

            return true;
        }
    }

    // Where the cube builder places a block: the new cell, the direction from the aimed
    // block to it, and the cell of the aimed block
    public struct Target
    {
        public Int3 AddPos,
            AddDir,
            Aimed;

        // Cells along an edge of a large conveyor port's face: 3 on small grids, where the
        // large conveyors are the 3x3 medium ones, 1 on large grids
        public int LargePortSpan;
    }

    // The block's Center cell as the cube builder places it in the rotation: it moves the
    // block along the direction to the new cell, so the block sits on the aimed face.
    // MyCubeBuilderGizmo.UpdateGizmoCubeParts, for each axis.
    public static Int3 Position(Shape shape, Rotation rotation, Target target)
    {
        var size = rotation.Apply(shape.Size);
        var mirror = rotation.Apply(shape.MirroringCenter);
        int Shift(int axis)
        {
            var dir = Math.Sign(target.AddDir[axis]);
            return Math.Sign(size[axis]) == dir
                ? mirror[axis]
                : dir * (Math.Abs(size[axis]) - Math.Abs(mirror[axis]) - 1);
        }
        var shift = new Int3(Shift(0), Shift(1), Shift(2));
        return target.AddPos + rotation.Apply(shape.Center - shape.MirroringCenter) + shift;
    }

    public struct Candidate
    {
        public Shape Shape;

        // Rotations the block definition allows, in the order to try them
        public Rotation[] Rotations;
    }

    public struct Pick
    {
        public int Index;
        public Rotation Rotation;
        public double Score;
    }

    // How badly the block fits the cell, 0 for a perfect fit, null if it doesn't fit at all.
    // The aimed cell is a cell of the block the cube builder aims at.
    public static double? Score(Placed block, Func<Int3, Placed> grid, Target target)
    {
        var cells = new HashSet<Int3>(block.Cells);
        if (cells.Any(cell => grid(cell) != null))
            return null;
        var boundary = cells
            .SelectMany(cell => Int3.Directions.Select(d => (cell, d)))
            .Where(f => !cells.Contains(f.cell + f.d))
            .ToList();
        if (boundary.Count == 0)
            return null;

        // The kinds of the neighbors' ports pointing into the block's cells, by face. A face
        // may have several, like a conveyor port and an upgrade port.
        var incoming = new Dictionary<(Int3, Int3), HashSet<string>>();
        foreach (var (cell, d) in boundary)
        {
            var kinds = new HashSet<string>(
                grid(cell + d)
                    ?.Ports.Where(p => p.Cell.Equals(cell + d) && p.Direction.Equals(-d))
                    .Select(p => p.Kind)
                    ?? Enumerable.Empty<string>()
            );
            if (kinds.Count != 0)
                incoming[(cell, d)] = kinds;
        }

        // More connected ports pick the better rotation, like both ports of an upgrade
        // module on the machine instead of one, but don't make other blocks unfit
        // Ports of a kind the block doesn't have don't concern it: a tube may pass by the
        // upgrade ports of a machine, and armor may sit on them
        if (block.Shape.HasPorts)
        {
            var own = new HashSet<string>(block.Shape.Ports.Select(p => p.Kind));
            var connected = PortsFit(block, grid, cells, Only(incoming, own));
            return connected > 0 ? -0.001 * connected : (double?)null;
        }

        var conveyor = Only(incoming, new HashSet<string> { LargePort, SmallPort });
        if (conveyor.Count != 0)
            return CapFits(block, boundary, conveyor, target.LargePortSpan) ? 0 : (double?)null;

        // A face sticking out where the neighbor has none, like a full face against the
        // slanted side of a slope, is worse than leaving part of the neighbor's face
        // uncovered, like the floor beside a corner block.
        // The aimed face has to fit, so the block attaches there. Other neighbors count only
        // if they are of the block's family, so a beam may run past a wall and continue.
        // The aimed face decides whether the block fits at all, the others only rank it, so
        // the beams around a gap don't rule out every block that fits the aimed beam
        var aimedBlock = grid(target.Aimed);
        var sticking = 0;
        var uncovered = 0;
        var contact = 0;
        var aimedMismatch = 0.0;
        foreach (var (cell, d) in boundary)
        {
            var neighbor = grid(cell + d);
            var isAimed = neighbor != null && neighbor == aimedBlock;
            if (neighbor == null || !isAimed && neighbor.Shape.Family != block.Shape.Family)
                continue;

            var own = block.Face(cell, d);
            var other = neighbor.Face(cell + d, -d);
            var faceSticking = 0;
            var faceUncovered = 0;
            for (var k = 0; k < own.Length; k++)
            {
                if (own[k] && !other[k])
                    faceSticking++;
                else if (other[k] && !own[k])
                    faceUncovered++;
                else if (own[k] && isAimed)
                    contact++;
            }
            // Armor closes surfaces, so leaving its neighbors' faces open counts. Other blocks,
            // like a half beam in a frame of full beams, need only not stick out past them.
            sticking += faceSticking;
            if (isAimed || block.Shape.Family == Armor)
                uncovered += faceUncovered;
            if (isAimed)
                aimedMismatch += faceSticking + 0.5 * faceUncovered;
        }

        if (contact == 0 || aimedMismatch / (Samples * Samples) > MaxMismatch)
            return null;

        return (sticking + 0.5 * uncovered) / (Samples * Samples);
    }

    // Each face a neighbor port points into is met by a port of the same kind, and no port
    // of a conveyor block runs into an occupied cell without a port to meet it. The number
    // of connected faces, 0 if it doesn't fit.
    private static int PortsFit(
        Placed block,
        Func<Int3, Placed> grid,
        HashSet<Int3> cells,
        Dictionary<(Int3, Int3), HashSet<string>> incoming
    )
    {
        var own = block
            .Ports.Where(p => !cells.Contains(p.Cell + p.Direction))
            .GroupBy(p => (p.Cell, p.Direction))
            .ToDictionary(g => g.Key, g => g.Select(p => p.Kind).ToList());

        foreach (var face in incoming)
            if (!own.TryGetValue(face.Key, out var kinds) || !kinds.Any(face.Value.Contains))
                return 0;

        if (block.Shape.Conveyor)
            foreach (var face in own.Keys)
                if (!incoming.ContainsKey(face) && grid(face.Item1 + face.Item2) != null)
                    return 0;

        return incoming.Count;
    }

    private static Dictionary<(Int3, Int3), HashSet<string>> Only(
        Dictionary<(Int3, Int3), HashSet<string>> incoming,
        HashSet<string> kinds
    ) =>
        incoming
            .Where(face => face.Value.Overlaps(kinds))
            .ToDictionary(
                face => face.Key,
                face => new HashSet<string>(face.Value.Where(kinds.Contains))
            );

    // A block without ports closes open conveyor ends if it is mounted on the faces the
    // ports point into and on no face looking any other way, and it is as wide as the
    // ports, like a small conveyor cap on a small port or a medium one on a large port
    private static bool CapFits(
        Placed block,
        List<(Int3 cell, Int3 d)> boundary,
        Dictionary<(Int3, Int3), HashSet<string>> incoming,
        int largePortSpan
    )
    {
        var mounted = boundary.Where(f => block.Face(f.cell, f.d).Any(s => s)).ToList();
        var directions = new HashSet<Int3>(incoming.Keys.Select(f => f.Item2));
        var width = incoming.Values.Sum(kinds =>
            kinds.Contains(LargePort) ? largePortSpan * largePortSpan : 1
        );
        return mounted.Count == width
            && incoming.Keys.All(mounted.Contains)
            && mounted.All(f => directions.Contains(f.d));
    }

    // The best rotation of each candidate at the cell, null for those that don't fit
    public static Pick?[] Evaluate(
        IList<Candidate> candidates,
        Target target,
        Rotation current,
        Func<Int3, Placed> grid
    )
    {
        var picks = new Pick?[candidates.Count];
        for (var i = 0; i < candidates.Count; i++)
        {
            var rotations = candidates[i].Rotations;
            var ordered = rotations
                .Where(r => r.Equals(current))
                .Concat(rotations.Where(r => !r.Equals(current)));
            foreach (var rotation in ordered)
            {
                var shape = candidates[i].Shape;
                var score = Score(
                    new Placed(shape, rotation, Position(shape, rotation, target)),
                    grid,
                    target
                );
                if (score.HasValue && (!picks[i].HasValue || score.Value < picks[i].Value.Score))
                    picks[i] = new Pick
                    {
                        Index = i,
                        Rotation = rotation,
                        Score = score.Value,
                    };
            }
        }

        // Only the best shapes fit
        var fitting = picks.Where(p => p.HasValue).Select(p => p.Value.Score).ToList();
        if (fitting.Count != 0)
        {
            var best = fitting.Min();
            for (var i = 0; i < picks.Length; i++)
                if (picks[i].HasValue && picks[i].Value.Score > best + Tolerance)
                    picks[i] = null;
        }

        return picks;
    }

    // The first fitting candidate after the current one in the direction, wrapping around
    // to the current one itself. Direction 0 keeps the current one if it fits, or else
    // moves forward. Null if none fits.
    public static Pick? Next(
        IList<Candidate> candidates,
        int current,
        int direction,
        Rotation rotation,
        Target target,
        Func<Int3, Placed> grid
    )
    {
        var count = candidates.Count;
        var picks = Evaluate(candidates, target, rotation, grid);
        if (direction == 0)
        {
            if (current >= 0 && picks[current].HasValue)
                return picks[current];
            direction = 1;
        }
        if (current < 0)
            current = direction > 0 ? -1 : count;

        for (var step = 1; step <= count; step++)
        {
            var pick = picks[((current + direction * step) % count + count) % count];
            if (pick.HasValue)
                return pick;
        }

        return null;
    }
}
