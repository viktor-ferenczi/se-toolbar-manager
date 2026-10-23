"""In-game check of the fitting block button, on the isolated client of rig.py.

    TBM_SLOT=16 uv run --with httpx python tests/fit_check.py [--attach] [--keep]

Starts the client with block groups written before the launch, pastes small static
grids with a gap, aims the cube builder into the gap and presses the next fitting block
button (extra mouse button 2) with a block of a group in hand. Each press must select the
expected block, and a left click must place it in the expected rotation. Shift and the
wheel, and the previous button, step through the fitting blocks too. The middle button
must paint as in the vanilla game. These are the in-game counterparts of the offline
cases in tests/BlockFit.Tests. TBM_FIT_CASES=name,... runs only those cases ("wheel" for
the wheel checks). Prints PASS or FAIL per check and exits non-zero on a failure.
--attach uses a client this script started before with --keep, which leaves it running.
"""

from __future__ import annotations

import os
import shutil
import sys
import time
from dataclasses import dataclass

import rig
from item_groups_check import TOOLBAR_CONFIG, Game, check, results, wait
from se_remote import CallOp

ARMOR = "MyObjectBuilder_CubeBlock/"
GROUPS = {
    "Armor": [
        "CubeBlock/LargeBlockArmorSlope",
        "CubeBlock/LargeBlockArmorCorner",
        "CubeBlock/LargeBlockArmorBlock",
        "CubeBlock/LargeBlockArmorCornerInv",
    ],
    "Beams": [
        "CubeBlock/LargeBlockArmorBlock",
        "CubeBlock/LargeGridBeamBlockEnd",
        "CubeBlock/LargeGridBeamBlock",
    ],
    "Conveyors": [
        "CubeBlock/LargeBlockArmorBlock",
        "CubeBlock/LargeBlockConveyorCap",
        "ConveyorConnector/ConveyorTube",
        "ConveyorConnector/ConveyorTubeCurved",
    ],
}
GROUP_SLOTS = {"Armor": 1, "Beams": 2, "Conveyors": 3}
PLAIN_SLOT = 5
# A block in no group, so no group is in use with it in hand
PLAIN_BLOCK = "LargeHeavyBlockArmorBlock"
# Beams are Heavy Industry DLC blocks, which only a client with Steam owns
STEAM = os.environ.get("TBM_STEAM") == "1"
NO_FIT = "No block of the group fits here"

# Grid directions as MyBlockOrientation names them
DIRECTIONS = {
    "Forward": (0, 0, -1),
    "Backward": (0, 0, 1),
    "Left": (-1, 0, 0),
    "Right": (1, 0, 0),
    "Up": (0, 1, 0),
    "Down": (0, -1, 0),
}
NAMES = {v: k for k, v in DIRECTIONS.items()}

# The gap is at TARGET of each pasted grid
TARGET = (0, 3, 0)


def add(a, b):
    return tuple(x + y for x, y in zip(a, b))


@dataclass
class Case:
    name: str
    group: str
    held: str
    blocks: list[tuple[str, str, tuple[int, int, int], str, str]]
    # Direction from the gap to the block to aim at, the character stands opposite
    aim: tuple[int, int, int]
    # Subtype after each press, None for no fit
    presses: list[str | None]
    # Forward and up the placed block must have, None for any
    forward: str | None = None
    up: str | None = None
    # Meters to move the aimed point by, to hit the solid part of a slanted face
    offset: tuple[float, float, float] = (0, 0, 0)


def cube(cell, subtype="LargeBlockArmorBlock", forward="Forward", up="Up"):
    return ("CubeBlock", subtype, add(TARGET, cell), forward, up)


def tube(cell, subtype="ConveyorTube", kind="ConveyorConnector"):
    return (kind, subtype, add(TARGET, cell), "Forward", "Up")


DOWN, UP = (0, -1, 0), (0, 1, 0)
LEFT, RIGHT = (-1, 0, 0), (1, 0, 0)
FORWARD, BACKWARD = (0, 0, -1), (0, 0, 1)

CASES = [
    Case(
        "cube gap",
        "Armor",
        "LargeBlockArmorSlope",
        [cube(DOWN), cube(LEFT), cube(RIGHT), cube(FORWARD), cube(BACKWARD)],
        DOWN,
        ["LargeBlockArmorBlock", "LargeBlockArmorBlock"],
    ),
    Case(
        "slope gap",
        "Armor",
        "LargeBlockArmorBlock",
        [
            cube(DOWN),
            cube(FORWARD),
            cube(LEFT, "LargeBlockArmorSlope"),
            cube(RIGHT, "LargeBlockArmorSlope"),
        ],
        # Aligning to the default mount point would put the bottom on the wall
        FORWARD,
        ["LargeBlockArmorSlope", "LargeBlockArmorSlope"],
        "Forward",
        "Up",
    ),
    Case(
        "corner gap",
        "Armor",
        "LargeBlockArmorBlock",
        [
            cube(DOWN),
            cube(RIGHT, "LargeBlockArmorSlope"),
            cube(FORWARD, "LargeBlockArmorSlope", forward="Right"),
        ],
        RIGHT,
        ["LargeBlockArmorCorner"],
        "Forward",
        "Up",
        (0, -0.6, 0),
    ),
    Case(
        "beam end",
        "Beams",
        "LargeBlockArmorBlock",
        [
            cube(BACKWARD, "LargeGridBeamBlock"),
            cube((0, 0, 2), "LargeGridBeamBlock"),
        ],
        BACKWARD,
        (
            ["LargeGridBeamBlockEnd", "LargeGridBeamBlock", "LargeGridBeamBlockEnd"]
            if STEAM
            # Without the DLC the beams are skipped and the cube is the best left
            else ["LargeBlockArmorBlock"]
        ),
        "Forward" if STEAM else None,
        "Up" if STEAM else None,
    ),
    Case(
        "conveyor open end",
        "Conveyors",
        "LargeBlockArmorBlock",
        [tube(DOWN), tube((0, -2, 0))],
        DOWN,
        [
            "LargeBlockConveyorCap",
            "ConveyorTube",
            "ConveyorTubeCurved",
            "LargeBlockConveyorCap",
        ],
        None,
        "Up",
    ),
    Case(
        "tube at a flat port",
        "Conveyors",
        "LargeBlockArmorBlock",
        [tube(DOWN, "LargeBlockConveyor", "Conveyor")],
        DOWN,
        ["LargeBlockConveyorCap", "ConveyorTube"],
        None,
        "Up",
    ),
    Case(
        "tube middle",
        "Conveyors",
        "LargeBlockArmorBlock",
        [tube(RIGHT)],
        RIGHT,
        [None],
    ),
]


def write_groups(c: rig.Client) -> None:
    if c.groups.exists():
        shutil.rmtree(c.groups)
    c.groups.mkdir(parents=True)
    for order, (name, items) in enumerate(GROUPS.items()):
        strings = "".join(f"<string>MyObjectBuilder_{i}</string>" for i in items)
        (c.groups / f"{name}.xml").write_text(
            '<?xml version="1.0" encoding="utf-8"?>\n'
            f"<ItemGroup><Id>{name.lower()}</Id><Name>{name}</Name>"
            f"<Order>{order}</Order><Items>{strings}</Items></ItemGroup>\n",
            encoding="utf-8",
        )


def start(c: rig.Client):
    rig.stop(c)
    write_groups(c)
    rig.launch(c)
    api = rig.wait_api(c)
    rig.load_world(api, rig.prepare_world(c.world))
    return api


def blueprint(blocks) -> str:
    cubes = "".join(
        f'<MyObjectBuilder_CubeBlock xsi:type="MyObjectBuilder_{kind}">'
        f'<SubtypeName>{subtype}</SubtypeName><Min x="{x}" y="{y}" z="{z}" />'
        f'<BlockOrientation Forward="{forward}" Up="{up}" />'
        "</MyObjectBuilder_CubeBlock>"
        for kind, subtype, (x, y, z), forward, up in blocks
    )
    return (
        '<?xml version="1.0"?>'
        '<Definitions xmlns:xsd="http://www.w3.org/2001/XMLSchema" '
        'xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"><ShipBlueprints>'
        '<ShipBlueprint xsi:type="MyObjectBuilder_ShipBlueprintDefinition">'
        '<Id Type="MyObjectBuilder_ShipBlueprintDefinition" Subtype="FitTest" />'
        "<CubeGrids><CubeGrid><SubtypeName /><EntityId>0</EntityId>"
        "<PersistentFlags>CastShadows InScene</PersistentFlags>"
        '<PositionAndOrientation><Position x="0" y="0" z="0" />'
        '<Forward x="0" y="0" z="-1" /><Up x="0" y="1" z="0" /></PositionAndOrientation>'
        f"<GridSizeEnum>Large</GridSizeEnum><CubeBlocks>{cubes}</CubeBlocks>"
        "<IsStatic>true</IsStatic><IsUnsupportedStation>true</IsUnsupportedStation>"
        "<DisplayName>FitTest</DisplayName></CubeGrid></CubeGrids>"
        "</ShipBlueprint></ShipBlueprints></Definitions>"
    )


class Fit:
    def __init__(self, game: Game):
        self.game = game
        self.api = game.api
        self.home = tuple(self.api.get_character()["position"])
        self.grid = None
        self.notifications = 0

    def world(self, cell) -> tuple[float, ...]:
        return tuple(
            self.api.call([CallOp.grid_to_world(self.grid, cell)]).call(0)["world"]
        )

    def cubes(self) -> dict[tuple, dict]:
        cubes = self.api.list_cubes(self.grid, 0, 200)["cubes"]
        return {tuple(c["cellMin"]): c for c in cubes}

    def paste(self, blocks) -> None:
        self.clear()
        self.api.character_teleport(*self.home)
        time.sleep(1)
        state = self.api.get_character()
        ahead = tuple(p + 15 * f for p, f in zip(state["position"], state["forward"]))
        grids = self.api.paste_blueprint(
            xml=blueprint(blocks), position=ahead, forward=(0, 0, -1), up=(0, 1, 0)
        )
        self.grid = grids[0]["entityId"]
        cells = wait(lambda: len(self.cubes()) == len(blocks) and self.cubes(), 10)
        expected = {b[2] for b in blocks}
        if not cells or set(cells) != expected:
            raise AssertionError(f"Pasted cells {sorted(cells or [])}, not {expected}")

    def clear(self) -> None:
        if self.grid is not None:
            self.api.close_grid(self.grid)
            self.grid = None
            time.sleep(1)

    def aim(self, direction, offset=(0, 0, 0)) -> None:
        """Faces the face between the gap and its neighbor in the direction from 6 m"""
        gap = self.world(TARGET)
        neighbor = self.world(add(TARGET, direction))
        face = tuple((a + b) / 2 + o for a, b, o in zip(gap, neighbor, offset))
        away = tuple(a - b for a, b in zip(gap, neighbor))
        length = sum(x * x for x in away) ** 0.5
        stand = tuple(g + 6 * a / length for g, a in zip(gap, away))
        self.api.character_teleport(*stand)
        time.sleep(1)
        self.api.character_look_at(*face, tolerance=0.5)
        time.sleep(1)

    def button(self, **buttons) -> None:
        # Keeps the wheel counter: resetting it would be a wheel turn back
        self.api.set_input_state(scroll=self.game.wheel, **buttons)
        time.sleep(0.3)
        self.api.set_input_state(scroll=self.game.wheel)
        time.sleep(1.0)

    def shift_scroll(self, delta: int) -> None:
        self.game.wheel += delta
        self.api.set_input_state(keys=["LeftShift"], scroll=self.game.wheel)
        time.sleep(0.3)
        # Without keys set_input_state leaves Shift held
        self.api._post(
            "/v1/input/state",
            {"keyboard": {"keys": []}, "mouse": {"scroll": self.game.wheel}},
        )
        time.sleep(1.0)

    def no_fit_shown(self) -> bool:
        data = self.api.get_hud_notifications(self.notifications)
        self.notifications = data.get("latest", self.notifications)
        return any(NO_FIT in n.get("text", "") for n in data.get("notifications", []))

    def held(self) -> str | None:
        held = self.game.held()
        return held.split("/")[-1] if held else None


def setup_toolbar(game: Game) -> None:
    api = game.api
    rig.focus_gameplay(api)
    for slot in range(9):
        api.set_toolbar_slot(slot, None)
    api.set_toolbar_slot(PLAIN_SLOT, ARMOR + PLAIN_BLOCK)
    api.set_admin_flag("creativeTools", True)
    screen = game.open_toolbar_config()
    game.select_category(screen, "Groups")
    for name, slot in GROUP_SLOTS.items():
        game.drag_group_to_toolbar(screen, name, slot)
    game.close_screen(TOOLBAR_CONFIG)


def hold(fit: Fit, case: Case) -> bool:
    """Steps the group's slot until the case's first block is in hand"""
    game = fit.game
    game.holster()
    for _ in range(len(GROUPS[case.group]) + 1):
        game.press_slot(GROUP_SLOTS[case.group])
        if fit.held() == case.held:
            return True
    return False


def run_case(fit: Fit, case: Case) -> None:
    fit.paste(case.blocks)
    if not check(f"{case.name}: holds {case.held}", hold(fit, case), str(fit.held())):
        return
    fit.aim(case.aim, case.offset)
    fit.no_fit_shown()
    colors = {cell: c["colorMaskHsv"] for cell, c in fit.cubes().items()}
    before = len(colors)
    for press, expected in enumerate(case.presses, 1):
        fit.button(mouse_xbutton2=True)
        if expected is None:
            check(
                f"{case.name}: press {press} finds no fit",
                fit.held() == case.held and fit.no_fit_shown(),
                f"holds {fit.held()}",
            )
        else:
            check(
                f"{case.name}: press {press} selects {expected}",
                fit.held() == expected,
                f"holds {fit.held()}",
            )

    painted = [
        cell for cell, c in fit.cubes().items() if c["colorMaskHsv"] != colors[cell]
    ]
    check(f"{case.name}: the button didn't paint", not painted, f"painted {painted}")

    last = case.presses[-1]
    if last is None:
        return
    fit.button(mouse_left=True)
    placed = wait(lambda: len(fit.cubes()) > before and fit.cubes().get(TARGET), 5)
    detail = (
        f"{placed['subtypeId']} {placed['forward']} {placed['up']}"
        if placed
        else "nothing placed"
    )
    check(
        f"{case.name}: places {last}"
        + (f" facing {case.forward}" if case.forward else "")
        + (f" with up {case.up}" if case.up else ""),
        bool(placed)
        and placed["subtypeId"] == last
        and (case.forward is None or placed["forward"] == case.forward)
        and (case.up is None or placed["up"] == case.up),
        detail,
    )


def run_dlc_skip(fit: Fit) -> None:
    """Without Steam no DLC is owned: the slot and the wheel skip the beams"""
    game = fit.game
    game.holster()
    held = []
    for _ in range(3):
        game.press_slot(GROUP_SLOTS["Beams"])
        held.append(fit.held())
    game.scroll(120)
    held.append(fit.held())
    game.scroll(-120)
    held.append(fit.held())
    check(
        "dlc: the Beams slot and the wheel keep to the cube",
        held == ["LargeBlockArmorBlock"] * len(held),
        str(held),
    )


def run_wheel(fit: Fit) -> None:
    """Shift and the wheel step through the fitting blocks only, both ways, the previous
    button steps back, the plain wheel steps through the whole group, and the middle
    button paints with a group's block in hand"""
    game = fit.game
    fit.paste([tube(DOWN), tube((0, -2, 0))])
    case = Case("wheel", "Conveyors", "LargeBlockArmorBlock", [], DOWN, [])
    if not check("wheel: holds LargeBlockArmorBlock", hold(fit, case), str(fit.held())):
        return
    fit.aim(DOWN)
    steps = []
    for name, action in [
        ("Shift+wheel up", lambda: fit.shift_scroll(120)),
        ("Shift+wheel up", lambda: fit.shift_scroll(120)),
        ("Shift+wheel down", lambda: fit.shift_scroll(-120)),
        ("previous button", lambda: fit.button(mouse_xbutton1=True)),
        ("plain wheel up", lambda: game.scroll(120)),
    ]:
        action()
        steps.append(f"{name}: {fit.held()}")
    expected = [
        "Shift+wheel up: LargeBlockConveyorCap",
        "Shift+wheel up: ConveyorTube",
        "Shift+wheel down: LargeBlockConveyorCap",
        "previous button: ConveyorTubeCurved",
        "plain wheel up: LargeBlockArmorBlock",
    ]
    for got, want in zip(steps, expected):
        check(f"wheel: {want}", got == want, got)

    tube_cell = add(TARGET, DOWN)
    before = fit.cubes()[tube_cell]["colorMaskHsv"]
    fit.button(mouse_middle=True)
    color = fit.cubes()[tube_cell]["colorMaskHsv"]
    check(
        "wheel: the middle button paints with a group's block in hand",
        color != before and fit.held() == "LargeBlockArmorBlock",
        f"{before} -> {color}, holds {fit.held()}",
    )


def run_vanilla(fit: Fit) -> None:
    """A block from an ordinary slot: the middle button paints, the block stays"""
    game = fit.game
    fit.paste([cube(DOWN)])
    # Resetting the wheel counter is a wheel turn, which must not reach the plain block
    game.holster()
    game.clear_scroll()
    time.sleep(1)
    game.press_slot(PLAIN_SLOT)
    fit.aim(DOWN)
    before = fit.cubes()[add(TARGET, DOWN)]["colorMaskHsv"]
    fit.button(mouse_middle=True)
    color = fit.cubes()[add(TARGET, DOWN)]["colorMaskHsv"]
    check(
        "vanilla: the middle button keeps the block of an ordinary slot",
        fit.held() == PLAIN_BLOCK,
        str(fit.held()),
    )
    check("vanilla: the middle button paints", color != before, f"{before} -> {color}")


def main() -> int:
    attach = "--attach" in sys.argv
    keep = attach or "--keep" in sys.argv
    client = rig.client()
    api = rig.connect(client) if attach else start(client)
    game = Game(api, client)
    try:
        errors_before = game.exception_count()
        for index in range(14):
            api.set_build_color(index, 0.0, 0.5, 0.5)
        setup_toolbar(game)
        fit = Fit(game)
        only = os.environ.get("TBM_FIT_CASES")
        for case in CASES:
            if only and case.name not in only.split(","):
                continue
            try:
                run_case(fit, case)
            except (
                Exception
            ) as e:  # noqa: BLE001 -- report and go on with the next case
                check(f"{case.name}: completed", False, f"{type(e).__name__}: {e}")
            game.shot(case.name.replace(" ", "-"))
        if not only or "wheel" in only.split(","):
            run_wheel(fit)
        if not STEAM:
            run_dlc_skip(fit)
        run_vanilla(fit)
        fit.clear()
        check(
            "no new exceptions in the game log",
            game.exception_count() == errors_before,
            f"{game.exception_count() - errors_before} new",
        )
    except Exception as e:  # noqa: BLE001 -- report and fall through to the summary
        check("run completed", False, f"{type(e).__name__}: {e}")
    finally:
        if not keep:
            rig.stop(client)

    failed = [name for name, ok, _ in results if not ok]
    print(f"\n{len(results) - len(failed)} passed, {len(failed)} failed")
    for name in failed:
        print(f"  FAIL {name}")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
