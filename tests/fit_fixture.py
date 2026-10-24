"""Writes the block definitions BlockFit's offline tests use, from the installed game.

    python tests/fit_fixture.py [--game <SpaceEngineers folder>]

Mountpoints come from the CubeBlocks .sbc files and conveyor ports from the
detector_conveyor dummies of the block models, converted the way the game does it
(MyCubeBlockDefinition.SetMountPoints, MyConveyorLine.GetBlockLinePositions), so the
tests see the same shapes as the plugin in the game. The output is
tests/BlockFit.Tests/blocks.json.
"""

from __future__ import annotations

import argparse
import json
import math
import struct
import xml.etree.ElementTree as ET
from pathlib import Path

GAME = Path.home() / ".steam/debian-installation/steamapps/common/SpaceEngineers"
OUT = Path(__file__).resolve().parent / "BlockFit.Tests" / "blocks.json"

BLOCKS = [
    "LargeBlockArmorBlock",
    "LargeBlockArmorSlope",
    "LargeBlockArmorCorner",
    "LargeBlockArmorCornerInv",
    "LargeHalfArmorBlock",
    "LargeBlockArmorSlope2Base",
    "LargeBlockArmorSlope2Tip",
    "LargeGridBeamBlock",
    "LargeGridBeamBlockEnd",
    "LargeGridBeamBlockJunction",
    "LargeGridBeamBlockTJunction",
    "LargeGridBeamBlockSlope",
    "LargeGridBeamBlockRound",
    "LargeGridBeamBlockHalf",
    "LargeGridBeamBlockHalfSlope",
    "LargeBlockConveyor",
    "ConveyorTube",
    "ConveyorTubeCurved",
    "ConveyorTubeT",
    "LargeBlockConveyorCap",
    "LargeBlockConveyorPipeSeamless",
    "LargeBlockConveyorPipeEnd",
    "LargeBlockConveyorPipeFlange",
    "LargeBlockConveyorPipeCorner",
    "LargeBlockConveyorPipeJunction",
    "LargeBlockConveyorPipeT",
    "LargeBlockConveyorPipeCap",
    "LargeBlockSmallContainer",
]

SIDES = ["Right", "Top", "Front", "Left", "Bottom", "Back"]
CUBE_SIZE = {"Large": 2.5, "Small": 0.5}


# --- VRageMath, just enough of it -------------------------------------------


def cross(a, b):
    return (
        a[1] * b[2] - a[2] * b[1],
        a[2] * b[0] - a[0] * b[2],
        a[0] * b[1] - a[1] * b[0],
    )


def normalize(a):
    n = math.sqrt(sum(x * x for x in a))
    return tuple(x / n for x in a)


def from_dir(direction, suggested_up):
    """Matrix.CreateFromDir as rows (right, up, backward)"""
    up = cross(cross(direction, suggested_up), direction)
    back = normalize(tuple(-x for x in direction))
    right = normalize(cross(up, back))
    return [right, cross(back, right), back]


def scaled(rows, sx, sy, sz):
    return [(r[0] * sx, r[1] * sy, r[2] * sz) for r in rows]


def transform(v, rows):
    return tuple(sum(v[i] * rows[i][k] for i in range(3)) for k in range(3))


R, L, U, D, F, B = (1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, -1), (0, 0, 1)
WALL_TRANSFORMS = [
    scaled(from_dir(R, U), 1, 1, -1),
    scaled(from_dir(U, F), -1, 1, 1),
    scaled(from_dir(F, U), -1, 1, 1),
    scaled(from_dir(L, U), 1, 1, -1),
    scaled(from_dir(D, B), -1, 1, 1),
    scaled(from_dir(B, U), -1, 1, 1),
]
WALL_OFFSETS = [(1, 0, 1), (0, 1, 1), (1, 0, 0), (0, 0, 0), (0, 0, 0), (0, 0, 1)]


def wall_position(position, wall, size):
    t = transform(position, WALL_TRANSFORMS[wall])
    return [t[k] + WALL_OFFSETS[wall][k] * size[k] for k in range(3)]


def wall_normal(wall):
    return [round(x) for x in transform(F, WALL_TRANSFORMS[wall])]


def mount(side, start, end, size):
    wall = SIDES.index(side)
    low = [min(start[0], end[0]) + 0.001, min(start[1], end[1]) + 0.001, 0.0004]
    high = [max(start[0], end[0]) - 0.001, max(start[1], end[1]) - 0.001, -0.0004]
    return {
        "n": wall_normal(wall),
        "s": wall_position(low, wall, size),
        "e": wall_position(high, wall, size),
    }


def default_mounts(size):
    """InitMountPoints without MountPoints: every side fully mounted"""
    result = []
    for wall, (a, b) in enumerate([(2, 1), (0, 2), (0, 1), (2, 1), (0, 2), (0, 1)]):
        low = [0.001, 0.001, 0.0004]
        high = [size[a] - 0.001, size[b] - 0.001, -0.0004]
        result.append(
            {
                "n": wall_normal(wall),
                "s": wall_position(low, wall, size),
                "e": wall_position(high, wall, size),
            }
        )
    return result


# --- Model dummies -----------------------------------------------------------


def read_string(f) -> str:
    length = shift = 0
    while True:
        byte = f.read(1)[0]
        length |= (byte & 0x7F) << shift
        shift += 7
        if byte < 0x80:
            break
    return f.read(length).decode("utf-8")


def read_int(f) -> int:
    return struct.unpack("<i", f.read(4))[0]


def dummies(path: Path) -> dict[str, tuple[float, ...]]:
    """MyModelImporter.LoadTagData for the Dummies tag of a current .mwm"""
    with path.open("rb") as f:
        read_string(f)
        header = [read_string(f) for _ in range(read_int(f))]
        assert header and "Version:" in header[0], path
        index = {read_string(f): read_int(f) for _ in range(read_int(f))}
        if "Dummies" not in index:
            return {}
        f.seek(index["Dummies"])
        read_string(f)
        result = {}
        for _ in range(read_int(f)):
            name = read_string(f)
            matrix = struct.unpack("<16f", f.read(64))
            for _ in range(read_int(f)):
                read_string(f)
                read_string(f)
            result[name] = matrix
        return result


def find_model(game: Path, model: str) -> Path:
    """Model paths are case insensitive on Windows, not on Linux"""
    path = game / "Content"
    for part in model.replace("\\", "/").split("/"):
        matches = [p for p in path.iterdir() if p.name.lower() == part.lower()]
        assert matches, f"{part} not in {path}"
        path = matches[0]
    return path


def ports(game: Path, block: ET.Element, size, center, cube_size):
    model = block.findtext("Model")
    if not model:
        return []
    offset = block.find("ModelOffset")
    offset = (
        [float(offset.get(k, 0)) for k in "xyz"] if offset is not None else [0, 0, 0]
    )
    result = []
    for name, m in dummies(find_model(game, model)).items():
        parts = name.lower().split("_")
        if (
            len(parts) < 2
            or parts[0] != "detector"
            or not parts[1].startswith("conveyor")
        ):
            continue
        p = [m[12 + k] + offset[k] + size[k] * 0.5 * cube_size for k in range(3)]
        cell = [
            min(max(math.floor(p[k] / cube_size), 0), size[k] - 1) for k in range(3)
        ]
        delta = [(p[k] - (cell[k] + 0.5) * cube_size) / cube_size for k in range(3)]
        axis = max(range(3), key=lambda k: abs(delta[k]))
        direction = [0, 0, 0]
        direction[axis] = 1 if delta[axis] > 0 else -1
        result.append(
            {
                "c": [cell[k] - center[k] for k in range(3)],
                "d": direction,
                "small": len(parts) > 2 and parts[2] == "small",
            }
        )
    return result


def vector(element: ET.Element | None, default) -> list[int]:
    if element is None:
        return list(default)
    return [int(element.get(k)) for k in "xyz"]


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--game", type=Path, default=GAME)
    game = parser.parse_args().game

    definitions = {}
    for sbc in sorted((game / "Content/Data/CubeBlocks").glob("*.sbc")):
        for block in ET.parse(sbc).getroot().iter("Definition"):
            subtype = block.findtext("Id/SubtypeId")
            if subtype in BLOCKS:
                definitions[subtype] = block

    # Families as FittingBlock.ShapeOf has them: armor, else the block variant group
    groups = {}
    variants = ET.parse(game / "Content/Data/BlockVariantGroups.sbc").getroot()
    for group in variants.iter("BlockVariantGroup"):
        group_id = group.find("Id")
        for block in group.iter("Block"):
            groups.setdefault(
                block.get("Subtype"),
                f"MyObjectBuilder_BlockVariantGroup/{group_id.get('Subtype')}",
            )

    result = {}
    for name in BLOCKS:
        block = definitions[name]
        size = vector(block.find("Size"), (1, 1, 1))
        center = vector(block.find("Center"), [(s - 1) // 2 for s in size])
        mountpoints = block.find("MountPoints")
        mounts = (
            [
                mount(
                    mp.get("Side"),
                    (float(mp.get("StartX")), float(mp.get("StartY"))),
                    (float(mp.get("EndX")), float(mp.get("EndY"))),
                    size,
                )
                for mp in mountpoints.findall("MountPoint")
            ]
            if mountpoints is not None
            else default_mounts(size)
        )
        cube_size = CUBE_SIZE[block.findtext("CubeSize")]
        result[name] = {
            "size": size,
            "center": center,
            "direction": block.findtext("Direction") or "Both",
            "rotation": block.findtext("Rotation") or "Both",
            "mounts": mounts,
            "ports": ports(game, block, size, center, cube_size),
            "family": (
                "armor"
                if block.find("CubeDefinition") is not None
                else groups.get(
                    name, f"MyObjectBuilder_{block.findtext('Id/TypeId')}/{name}"
                )
            ),
        }

    OUT.parent.mkdir(exist_ok=True)
    OUT.write_text(json.dumps(result, indent=1) + "\n", encoding="utf-8")
    print(f"{len(result)} blocks written to {OUT}")


if __name__ == "__main__":
    main()
