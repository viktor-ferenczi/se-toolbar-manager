// Offline tests of BlockFit on vanilla block definitions from blocks.json, which
// tests/fit_fixture.py extracts from the installed game.
//
//     dotnet run --project tests/BlockFit.Tests [-- --scores]
//
// Prints PASS or FAIL per check and exits non-zero on a failure. --scores also prints
// the score of every candidate.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ClientPlugin.Logic;
using static ClientPlugin.Logic.BlockFit;

var blocks = Load(Path.Combine(AppContext.BaseDirectory, "blocks.json"));
var printScores = args.Contains("--scores");
var failures = 0;

var origin = new Int3(0, 0, 0);
var up = new Int3(0, 1, 0);
var down = new Int3(0, -1, 0);
var forward = new Int3(0, 0, -1);
var backward = new Int3(0, 0, 1);
var left = new Int3(-1, 0, 0);
var right = new Int3(1, 0, 0);
var identity = Rotation.All[0];

// The direction Presses steps in
var step = 1;

// Rotation of a block facing forward with its top up, as MyBlockOrientation has it
Rotation Facing(Int3 f, Int3 u)
{
    var z = -f;
    var x = new Int3(u.Y * z.Z - u.Z * z.Y, u.Z * z.X - u.X * z.Z, u.X * z.Y - u.Y * z.X);
    return Rotation.All.First(r => r.X.Equals(x) && r.Y.Equals(u));
}

Dictionary<Int3, Placed> Grid(params (string name, Int3 position, Rotation rotation)[] items)
{
    var grid = new Dictionary<Int3, Placed>();
    foreach (var (name, position, rotation) in items)
    {
        var placed = new Placed(blocks[name], rotation, position);
        foreach (var cell in placed.Cells)
            grid[cell] = placed;
    }
    return grid;
}

Candidate[] Group(params string[] names) =>
    names.Select(n => new Candidate { Shape = blocks[n], Rotations = Rotations(n) }).ToArray();

// The rotations MyCubeBuilder.CheckValidBlockRotation allows for the definition
Rotation[] Rotations(string name)
{
    var shape = blocks[name];
    return Rotation
        .All.Where(r =>
        {
            var f = r.Apply(forward);
            var u = r.Apply(up);
            if (directions[name] != "Horizontal")
                return true;
            if (f.Equals(up) || f.Equals(down))
                return false;
            return rotationsAllowed[name] != "Vertical" || u.Equals(up);
        })
        .ToArray();
}

void Scores(string title, Candidate[] group, Dictionary<Int3, Placed> grid, Rotation rotation)
{
    if (!printScores)
        return;
    Console.WriteLine($"  {title}");
    foreach (var candidate in group)
    {
        var best = candidate
            .Rotations.Select(r =>
                (r, s: Score(new Placed(candidate.Shape, r, origin), Lookup(grid)))
            )
            .Where(x => x.s.HasValue)
            .OrderBy(x => x.s)
            .FirstOrDefault();
        Console.WriteLine(
            $"    {candidate.Shape.Name}: {(best.s.HasValue ? $"{best.s:0.000} {best.r}" : "no fit")}"
        );
    }
}

Func<Int3, Placed> Lookup(Dictionary<Int3, Placed> grid) =>
    cell => grid.TryGetValue(cell, out var block) ? block : null;

// Each press starts from the block the previous one picked
void Presses(
    string title,
    Dictionary<Int3, Placed> grid,
    string[] group,
    string held,
    params (string name, Rotation rotation)[] expected
)
{
    var candidates = Group(group);
    Scores(title, candidates, grid, identity);
    var current = Array.IndexOf(group, held);
    var rotation = identity;
    for (var press = 0; press < Math.Max(expected.Length, 1); press++)
    {
        var pick = Next(candidates, current, step, rotation, origin, Lookup(grid));
        var got = pick.HasValue ? group[pick.Value.Index] : null;
        var want = expected.Length == 0 ? null : expected[press].name;
        var ok = got == want;
        if (ok && want != null && expected[press].rotation != null)
            ok = SameShape(want, pick.Value.Rotation, expected[press].rotation);
        Check(
            $"{title}, press {press + 1}",
            ok,
            $"picked {got ?? "nothing"} {pick?.Rotation}, expected {want ?? "nothing"} {(expected.Length == 0 ? null : expected[press].rotation)}"
        );
        if (!pick.HasValue)
            break;
        current = pick.Value.Index;
        rotation = pick.Value.Rotation;
    }
}

// Rotations that give a block the same faces and ports are the same placement
bool SameShape(string name, Rotation a, Rotation b)
{
    var pa = new Placed(blocks[name], a, origin);
    var pb = new Placed(blocks[name], b, origin);
    return Int3.Directions.All(d => pa.Face(origin, d).SequenceEqual(pb.Face(origin, d)))
        && new HashSet<(Int3, Int3)>(pa.Ports.Select(p => (p.Cell, p.Direction))).SetEquals(
            pb.Ports.Select(p => (p.Cell, p.Direction))
        );
}

void Check(string name, bool ok, string detail)
{
    Console.WriteLine($"{(ok ? "PASS" : "FAIL")}  {name}{(ok ? "" : ": " + detail)}");
    if (!ok)
        failures++;
}

var armor = new[]
{
    "LargeBlockArmorSlope",
    "LargeBlockArmorCorner",
    "LargeBlockArmorBlock",
    "LargeBlockArmorCornerInv",
};

// A hole in a floor with a wall behind it: only the cube closes it
Presses(
    "armor: cube gap",
    Grid(
        ("LargeBlockArmorBlock", down, identity),
        ("LargeBlockArmorBlock", left, identity),
        ("LargeBlockArmorBlock", right, identity),
        ("LargeBlockArmorBlock", forward, identity),
        ("LargeBlockArmorBlock", backward, identity)
    ),
    armor,
    "LargeBlockArmorSlope",
    ("LargeBlockArmorBlock", identity),
    ("LargeBlockArmorBlock", identity)
);

// A gap in a row of slopes on a floor against a wall: the slope, turned like its neighbors
Presses(
    "armor: slope gap",
    Grid(
        ("LargeBlockArmorBlock", down, identity),
        ("LargeBlockArmorBlock", forward, identity),
        ("LargeBlockArmorSlope", left, identity),
        ("LargeBlockArmorSlope", right, identity)
    ),
    armor,
    "LargeBlockArmorBlock",
    ("LargeBlockArmorSlope", identity),
    ("LargeBlockArmorSlope", identity)
);

// The outer corner of a hip roof: a floor below, two upright slopes meeting at the corner
Presses(
    "armor: corner gap",
    Grid(
        ("LargeBlockArmorBlock", down, identity),
        ("LargeBlockArmorSlope", right, identity),
        ("LargeBlockArmorSlope", forward, Facing(right, up))
    ),
    armor,
    "LargeBlockArmorBlock",
    ("LargeBlockArmorCorner", identity),
    ("LargeBlockArmorCorner", identity)
);

var beams = new[] { "LargeBlockArmorBlock", "LargeGridBeamBlockEnd", "LargeGridBeamBlock" };

// The open end of a beam running backward: the beam end and the beam both continue it
Presses(
    "beam: open end",
    Grid(
        ("LargeGridBeamBlock", backward, identity),
        ("LargeGridBeamBlock", new Int3(0, 0, 2), identity)
    ),
    beams,
    "LargeBlockArmorBlock",
    ("LargeGridBeamBlockEnd", identity),
    ("LargeGridBeamBlock", identity),
    ("LargeGridBeamBlockEnd", identity)
);

var conveyors = new[]
{
    "LargeBlockArmorBlock",
    "LargeBlockConveyorCap",
    "ConveyorTube",
    "ConveyorTubeCurved",
};

// The open end of a tube line going up: the cap closes it, the tubes continue it
Presses(
    "conveyor: open end",
    Grid(("ConveyorTube", down, identity), ("ConveyorTube", new Int3(0, -2, 0), identity)),
    conveyors,
    "LargeBlockArmorBlock",
    ("LargeBlockConveyorCap", identity),
    ("ConveyorTube", identity),
    ("ConveyorTubeCurved", null),
    ("LargeBlockConveyorCap", identity)
);

// A port on the flat face of a conveyor junction takes a cap or starts a tube
Presses(
    "conveyor: tube at a flat port",
    Grid(("LargeBlockConveyor", down, identity)),
    conveyors,
    "LargeBlockArmorBlock",
    ("LargeBlockConveyorCap", identity),
    ("ConveyorTube", identity),
    ("ConveyorTubeCurved", null)
);

// Beside the middle of a tube no conveyor block fits, and the cube has nothing to mount on
Presses(
    "conveyor: tube middle",
    Grid(("ConveyorTube", right, identity)),
    conveyors,
    "LargeBlockArmorBlock"
);

// A gap between two tubes of a line: only the straight tube connects both
Presses(
    "conveyor: gap in a line",
    Grid(("ConveyorTube", down, identity), ("ConveyorTube", up, identity)),
    conveyors,
    "LargeBlockConveyorCap",
    ("ConveyorTube", identity),
    ("ConveyorTube", identity)
);

var pipes = new[]
{
    "LargeBlockConveyorPipeSeamless",
    "LargeBlockConveyorPipeEnd",
    "LargeBlockConveyorPipeJunction",
    "LargeBlockConveyorPipeCap",
};

// The industrial pipes connect to each other the same way
Presses(
    "pipe: open end",
    Grid(("LargeBlockConveyorPipeSeamless", down, identity)),
    pipes,
    "LargeBlockConveyorPipeSeamless",
    ("LargeBlockConveyorPipeEnd", identity),
    ("LargeBlockConveyorPipeJunction", identity),
    ("LargeBlockConveyorPipeCap", identity),
    ("LargeBlockConveyorPipeSeamless", identity)
);

// Stepping backward visits the same fits in the opposite order
step = -1;
Presses(
    "conveyor: open end, backward",
    Grid(("ConveyorTube", down, identity), ("ConveyorTube", new Int3(0, -2, 0), identity)),
    conveyors,
    "LargeBlockArmorBlock",
    ("ConveyorTubeCurved", null),
    ("ConveyorTube", identity),
    ("LargeBlockConveyorCap", identity),
    ("ConveyorTubeCurved", null)
);
step = 1;

// The current rotation stays when it fits as well as any other
{
    var turned = Facing(left, up);
    var pick = Next(
        Group(armor),
        0,
        1,
        turned,
        origin,
        Lookup(Grid(("LargeBlockArmorBlock", down, identity)))
    );
    Check(
        "armor: the cube on a floor keeps the current rotation",
        pick.HasValue
            && armor[pick.Value.Index] == "LargeBlockArmorBlock"
            && pick.Value.Rotation.Equals(turned),
        $"picked {(pick.HasValue ? armor[pick.Value.Index] : "nothing")} {pick?.Rotation}"
    );
}

Console.WriteLine(failures == 0 ? "All checks passed" : $"{failures} checks failed");
return failures == 0 ? 0 : 1;

static Dictionary<string, Shape> Load(string path)
{
    using var json = JsonDocument.Parse(File.ReadAllText(path));
    var result = new Dictionary<string, Shape>();
    foreach (var block in json.RootElement.EnumerateObject())
    {
        var b = block.Value;
        result[block.Name] = new Shape
        {
            Name = block.Name,
            Size = ReadInt3(b.GetProperty("size")),
            Center = ReadInt3(b.GetProperty("center")),
            Mounts = b.GetProperty("mounts")
                .EnumerateArray()
                .Select(m => new Mount
                {
                    Normal = ReadInt3(m.GetProperty("n")),
                    Start = m.GetProperty("s")
                        .EnumerateArray()
                        .Select(v => v.GetDouble())
                        .ToArray(),
                    End = m.GetProperty("e").EnumerateArray().Select(v => v.GetDouble()).ToArray(),
                })
                .ToArray(),
            Ports = b.GetProperty("ports")
                .EnumerateArray()
                .Select(p => new Port
                {
                    Cell = ReadInt3(p.GetProperty("c")),
                    Direction = ReadInt3(p.GetProperty("d")),
                    Small = p.GetProperty("small").GetBoolean(),
                })
                .ToArray(),
        };
        directions[block.Name] = b.GetProperty("direction").GetString();
        rotationsAllowed[block.Name] = b.GetProperty("rotation").GetString();
    }
    return result;
}

static Int3 ReadInt3(JsonElement e)
{
    var v = e.EnumerateArray().Select(x => x.GetInt32()).ToArray();
    return new Int3(v[0], v[1], v[2]);
}

partial class Program
{
    static readonly Dictionary<string, string> directions = new();
    static readonly Dictionary<string, string> rotationsAllowed = new();
}
