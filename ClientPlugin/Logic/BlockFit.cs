using System;
using System.Collections.Generic;
using System.Linq;

namespace ClientPlugin.Logic;

// Finds the blocks of a group that fit an empty cell of a grid: their faces match the faces
// of the neighbors they touch, and their conveyor ports line up with the neighbors' ports.
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

    // Faces that differ more than this in total don't fit at all
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
        public bool Small;
    }

    // What a block definition looks like to the matcher
    public sealed class Shape
    {
        public string Name;
        public Int3 Size,
            Center;
        public Mount[] Mounts;
        public Port[] Ports;

        public bool HasPorts => Ports.Length != 0;
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
                Small = p.Small,
            });

        // The mountpoints on the face of a grid cell of this block towards the direction,
        // sampled at the same grid points for both blocks touching on that face
        public bool[] Face(Int3 cell, Int3 direction)
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

    // How badly the block fits the cell, 0 for a perfect fit, null if it doesn't fit at all
    public static double? Score(Placed block, Func<Int3, Placed> grid)
    {
        var cells = new HashSet<Int3>(block.Cells);
        var boundary = cells
            .SelectMany(cell => Int3.Directions.Select(d => (cell, d)))
            .Where(f => !cells.Contains(f.cell + f.d))
            .ToList();
        if (boundary.Count == 0 || cells.Any(cell => grid(cell) != null))
            return null;

        // Ports of the neighbors pointing into the block's cells
        var incoming = new Dictionary<(Int3, Int3), bool>();
        foreach (var (cell, d) in boundary)
        {
            var neighbor = grid(cell + d);
            var port = neighbor
                ?.Ports.Where(p => p.Cell.Equals(cell + d) && p.Direction.Equals(-d))
                .Select(p => (bool?)p.Small)
                .FirstOrDefault();
            if (port.HasValue)
                incoming[(cell, d)] = port.Value;
        }

        if (block.Shape.HasPorts)
            return PortsFit(block, grid, cells, incoming) ? 0 : (double?)null;

        if (incoming.Count != 0)
            return CapFits(block, boundary, incoming) ? 0 : (double?)null;

        // A face sticking out where the neighbor has none, like a full face against the
        // slanted side of a slope, is worse than leaving part of the neighbor's face
        // uncovered, like the floor beside a corner block
        var sticking = 0;
        var uncovered = 0;
        var contact = 0;
        foreach (var (cell, d) in boundary)
        {
            var neighbor = grid(cell + d);
            if (neighbor == null)
                continue;

            var own = block.Face(cell, d);
            var other = neighbor.Face(cell + d, -d);
            for (var k = 0; k < own.Length; k++)
            {
                if (own[k] && !other[k])
                    sticking++;
                else if (other[k] && !own[k])
                    uncovered++;
                else if (own[k])
                    contact++;
            }
        }

        if (contact == 0)
            return null;

        var score = (sticking + 0.5 * uncovered) / (Samples * Samples);
        return score <= MaxMismatch ? score : (double?)null;
    }

    // Each neighbor port pointing in is met by a port of the same size, no port runs into
    // an occupied cell without a port to meet it, and at least one port connects
    private static bool PortsFit(
        Placed block,
        Func<Int3, Placed> grid,
        HashSet<Int3> cells,
        Dictionary<(Int3, Int3), bool> incoming
    )
    {
        var connected = 0;
        foreach (var port in block.Ports)
        {
            var facing = port.Cell + port.Direction;
            if (cells.Contains(facing))
                continue;

            if (incoming.TryGetValue((port.Cell, port.Direction), out var small))
            {
                if (small != port.Small)
                    return false;
                connected++;
            }
            else if (grid(facing) != null)
                return false;
        }

        return connected != 0 && connected == incoming.Count;
    }

    // A block without ports closes an open conveyor end if it is mounted only on the
    // faces the ports point into, like a conveyor cap
    private static bool CapFits(
        Placed block,
        List<(Int3 cell, Int3 d)> boundary,
        Dictionary<(Int3, Int3), bool> incoming
    )
    {
        var mounted = boundary.Where(f => block.Face(f.cell, f.d).Any(s => s)).ToList();
        return mounted.Count == incoming.Count && mounted.All(incoming.ContainsKey);
    }

    // The best rotation of each candidate at the cell, null for those that don't fit
    public static Pick?[] Evaluate(
        IList<Candidate> candidates,
        Int3 position,
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
                var score = Score(new Placed(candidates[i].Shape, rotation, position), grid);
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

    // The first fitting candidate after the current one, wrapping around to the current
    // one itself. Null if none fits.
    public static Pick? Next(
        IList<Candidate> candidates,
        int current,
        Rotation rotation,
        Int3 position,
        Func<Int3, Placed> grid
    )
    {
        var picks = Evaluate(candidates, position, rotation, grid);
        for (var step = 1; step <= candidates.Count; step++)
        {
            var pick = picks[
                ((current + step) % candidates.Count + candidates.Count) % candidates.Count
            ];
            if (pick.HasValue)
                return pick;
        }

        return null;
    }
}
