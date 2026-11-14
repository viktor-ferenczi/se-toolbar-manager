"""In-game check of picking the faced block, on the isolated client of rig.py.

    TBM_SLOT=15 uv run --with httpx python tests/faced_block_check.py [--color] [--attach] [--keep]

Pastes a wall of blocks ahead of the character and a small grid beside it, then presses
the default key like a player: Ctrl+G.

The block in hand must be the faced one by type and subtype, in its size, from an empty
hand and with another block in hand. The skin for new blocks must be the block's, and a
click must place the block turned like the picked one. The build color stays, or with
--color (the "Pick the color too" option) becomes the block's. Ctrl+G must not open the
G menu. It works without creative rights, and with no block in view it says so.
Prints PASS or FAIL per check and exits non-zero on a failure.
--attach and --keep work as in item_groups_check.py.
"""

from __future__ import annotations

import os
import sys
import time

import rig
from item_groups_check import TOOLBAR_CONFIG, Game, check, results, wait
from se_remote import CallOp

CUBE = "MyObjectBuilder_CubeBlock"
CARGO = "MyObjectBuilder_CargoContainer"
SKIN = "Clean_Armor"
BASE_COLOR = (0.0, -0.8, 0.55)
HALF_COLOR = (0.575, 0.15, 0.2)
DISTANCE = 9.0

# The wall, seen from the front: a row of armor with four blocks standing on it.
#   slope  cargo    .    half     .          y = 1
#   armor  armor  armor  armor  armor        y = 0
#   x=-2    -1      0      1      2
SLOPE, CARGO_CELL, HALF = (-2, 1, 0), (-1, 1, 0), (1, 1, 0)
ROW = [(x, 0, 0) for x in range(-2, 3)]


def block(kind, subtype, cell, color=BASE_COLOR, skin="", forward="Forward", up="Up"):
    skin = f"<SkinSubtypeId>{skin}</SkinSubtypeId>" if skin else ""
    return (
        f'<MyObjectBuilder_CubeBlock xsi:type="{kind}">'
        f"<SubtypeName>{subtype}</SubtypeName>"
        f'<Min x="{cell[0]}" y="{cell[1]}" z="{cell[2]}" />'
        f'<BlockOrientation Forward="{forward}" Up="{up}" />'
        f'<ColorMaskHSV x="{color[0]}" y="{color[1]}" z="{color[2]}" />{skin}'
        "</MyObjectBuilder_CubeBlock>"
    )


def blueprint(name: str, size: str, blocks: list[str]) -> str:
    return (
        '<?xml version="1.0"?>'
        '<Definitions xmlns:xsd="http://www.w3.org/2001/XMLSchema" '
        'xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"><ShipBlueprints>'
        '<ShipBlueprint xsi:type="MyObjectBuilder_ShipBlueprintDefinition">'
        f'<Id Type="MyObjectBuilder_ShipBlueprintDefinition" Subtype="{name}" />'
        "<CubeGrids><CubeGrid><SubtypeName />"
        "<PersistentFlags>CastShadows InScene</PersistentFlags>"
        '<PositionAndOrientation><Position x="0" y="0" z="0" />'
        '<Forward x="0" y="0" z="-1" /><Up x="0" y="1" z="0" /></PositionAndOrientation>'
        f"<GridSizeEnum>{size}</GridSizeEnum><CubeBlocks>{''.join(blocks)}</CubeBlocks>"
        # Unsupported, a static grid out of voxel contact would turn into a ship
        "<IsStatic>true</IsStatic><IsUnsupportedStation>true</IsUnsupportedStation>"
        f"<DisplayName>{name}</DisplayName></CubeGrid></CubeGrids>"
        "</ShipBlueprint></ShipBlueprints></Definitions>"
    )


WALL = blueprint(
    "FacedBlockWall",
    "Large",
    [block(CUBE, "LargeBlockArmorBlock", cell) for cell in ROW]
    + [
        # Turned, so the pick has an orientation to carry over
        block(CUBE, "LargeBlockArmorSlope", SLOPE, skin=SKIN, forward="Right", up="Up"),
        block(CARGO, "LargeBlockSmallContainer", CARGO_CELL),
        block(CUBE, "LargeHalfArmorBlock", HALF, color=HALF_COLOR),
    ],
)
SMALL = blueprint(
    "FacedBlockSmall",
    "Small",
    [
        block(CUBE, "SmallBlockArmorBlock", (x, y, 0))
        for x in range(5)
        for y in range(5)
    ],
)


class Faced:
    def __init__(self, game: Game):
        self.game = game
        self.api = game.api
        state = self.api.get_character()
        self.position = state["position"]
        self.forward, self.up = state["forward"], state["up"]
        f, u = self.forward, self.up
        self.right = [
            f[1] * u[2] - f[2] * u[1],
            f[2] * u[0] - f[0] * u[2],
            f[0] * u[1] - f[1] * u[0],
        ]

    def ahead(self, side: float) -> tuple[float, float, float]:
        return tuple(
            p + DISTANCE * f + side * r
            for p, f, r in zip(self.position, self.forward, self.right)
        )

    def paste(self, xml: str, side: float) -> int:
        # The grid's front is its -Z side, so its +Z faces look at the character
        grids = self.api.paste_blueprint(
            xml=xml, position=self.ahead(side), forward=self.forward, up=self.up
        )
        time.sleep(2)
        return grids[0]["entityId"]

    # Blocks

    def cubes(self, grid: int) -> dict[tuple, dict]:
        cubes = self.api.list_cubes(grid, 0, 200)["cubes"]
        return {tuple(c["cellMin"]): c for c in cubes}

    def count(self, grid: int) -> int:
        return self.api.list_cubes(grid, 0, 1).get("total", 0)

    def face(self, grid: int, cell, exact: bool = True) -> bool:
        """Turns the view to the block and tells whether it is the one in the crosshair,
        or with exact off any block of the grid"""
        world = self.api.call([CallOp.grid_to_world(grid, cell)]).call(0)["world"]
        self.api.character_look_at(*world, tolerance=0.3)
        time.sleep(0.7)
        hit = self.api.get_character_target(30).get("block") or {}
        ok = hit.get("gridId") == grid and (
            not exact or tuple(hit.get("min") or ()) == tuple(cell)
        )
        if not ok:
            print(
                f"     aimed at {cell} of {grid}, in the crosshair: {hit}", flush=True
            )
        return ok

    # Keys

    def press(self, key: str) -> None:
        self.api.key(key, ["LeftControl"])
        time.sleep(1.0)

    def click(self) -> None:
        self.api.set_input_state(mouse_left=True)
        time.sleep(0.3)
        self.api.clear_input_state()
        time.sleep(1.0)

    # What the player has

    def skin(self) -> str:
        return self.api.get_player_settings()["buildArmorSkin"]

    def color(self) -> tuple:
        settings = self.api.get_player_settings()
        slot = settings["buildColorSlots"][settings["selectedBuildColorSlot"]]
        return tuple(round(c, 3) for c in slot)

    def notified(self, text: str, after: int) -> bool:
        def found():
            new = self.api.get_hud_notifications(after)["notifications"]
            return any(text in (n.get("text") or "") for n in new)

        return bool(wait(found, 3))

    def latest(self) -> int:
        return self.api.get_hud_notifications(0)["latest"]


def held_is(faced: Faced, name: str, kind: str, subtype: str) -> bool:
    want = f"{kind}/{subtype}"
    held = wait(lambda: faced.game.held() == want and want, 5) or faced.game.held()
    return check(name, held == want, f"in hand: {held}")


def pick_checks(faced: Faced, wall: int, small: int, pick_color: bool) -> None:
    game, api = faced.game, faced.api
    start_color = faced.color()

    # Empty hand, Ctrl+G
    game.holster()
    if check("the slope is in the crosshair", faced.face(wall, SLOPE)):
        faced.press("G")
        held_is(faced, "Ctrl+G picks the slope", CUBE, "LargeBlockArmorSlope")
        check("pick takes the skin", faced.skin() == SKIN, f"skin: {faced.skin()!r}")
        check(
            "Ctrl+G does not open the G menu",
            game.screen_index(TOOLBAR_CONFIG) is None,
        )
        if pick_color:
            check(
                "pick takes the color with the option on",
                faced.color() == tuple(round(c, 3) for c in BASE_COLOR),
                f"{faced.color()}",
            )
        else:
            check(
                "pick leaves the build color alone",
                faced.color() == start_color,
                f"{start_color} -> {faced.color()}",
            )

    # A block in hand, another type and no skin
    if check("the cargo container is in the crosshair", faced.face(wall, CARGO_CELL)):
        faced.press("G")
        held_is(faced, "Ctrl+G picks the container", CARGO, "LargeBlockSmallContainer")
        check("pick clears the skin", faced.skin() == "", f"skin: {faced.skin()!r}")

    if pick_color and check(
        "the half block is in the crosshair", faced.face(wall, HALF)
    ):
        faced.press("G")
        held_is(faced, "Ctrl+G picks the half block", CUBE, "LargeHalfArmorBlock")
        check(
            "pick takes the half block's color",
            faced.color() == tuple(round(c, 3) for c in HALF_COLOR),
            f"{faced.color()}",
        )

    # The small variant, not the large block of the same pair
    if check(
        "a small block is in the crosshair", faced.face(small, (2, 2, 0), exact=False)
    ):
        faced.press("G")
        held_is(faced, "pick takes the small block", CUBE, "SmallBlockArmorBlock")

    # Without creative rights: the block to place, as its toolbar slot would give it
    api.set_admin_flag("creativeTools", False)
    time.sleep(0.5)
    game.holster()
    if check("the half block is in the crosshair", faced.face(wall, HALF)):
        faced.press("G")
        held_is(faced, "pick works without creative tools", CUBE, "LargeHalfArmorBlock")
    api.set_admin_flag("creativeTools", True)
    time.sleep(0.5)

    # Nothing in view
    before = game.held()
    away = [p - 50 * f for p, f in zip(faced.position, faced.forward)]
    api.character_look_at(*away)
    time.sleep(0.7)
    latest = faced.latest()
    faced.press("G")
    check("no block in view is told", faced.notified("No block in view", latest))
    check("no block in view keeps the hand", game.held() == before, f"{game.held()}")

    # The picked block places turned like the original
    if check("the slope is in the crosshair again", faced.face(wall, SLOPE)):
        faced.press("G")
        held_is(faced, "Ctrl+G picks the slope", CUBE, "LargeBlockArmorSlope")
        time.sleep(1.0)
        before = faced.cubes(wall)
        faced.click()
        after = faced.cubes(wall)
        new = [after[cell] for cell in after if cell not in before]
        ok = check("a click places the picked block", len(new) == 1, f"{len(new)} new")
        if ok:
            slope = after[SLOPE]
            check(
                "placed as the picked subtype",
                new[0]["subtypeId"] == "LargeBlockArmorSlope",
                new[0]["subtypeId"],
            )
            check(
                "placed turned like the picked block",
                (new[0]["forward"], new[0]["up"]) == (slope["forward"], slope["up"]),
                f"{new[0]['forward']}/{new[0]['up']} vs {slope['forward']}/{slope['up']}",
            )
            # Leave the wall as it was
            api.set_input_state(mouse_right=True)
            time.sleep(0.3)
            api.clear_input_state()
            time.sleep(1.0)
            check("the placed block is removed again", faced.count(wall) == len(before))


def run(game: Game, pick_color: bool) -> None:
    api = game.api
    rig.focus_gameplay(api)
    for slot in range(9):
        api.set_toolbar_slot(slot, None)
    api.set_admin_flag("creativeTools", True)
    api.set_game_option("cubeBuilderUseSymmetry", False)
    errors_before = game.exception_count()

    # Leftovers of an earlier run on an attached client
    for grid in api.list_grids():
        if (grid.get("name") or "").startswith("FacedBlock"):
            api.close_grid(grid["entityId"])
    time.sleep(1)

    faced = Faced(game)
    wall = faced.paste(WALL, 0.0)
    small = faced.paste(SMALL, 9.0)
    cubes = faced.cubes(wall)
    check("the wall is pasted", len(cubes) == 8, f"{len(cubes)} blocks")
    check("the slope has its skin", (cubes.get(SLOPE) or {}).get("skin") == SKIN)

    pick_checks(faced, wall, small, pick_color)

    check(
        "no exceptions in the game log",
        game.exception_count() == errors_before,
        f"{game.exception_count() - errors_before} new",
    )


def main() -> int:
    attach = "--attach" in sys.argv
    keep = "--keep" in sys.argv
    pick_color = "--color" in sys.argv
    if pick_color:
        os.environ["TBM_CONFIG"] = "<PickBlockColor>true</PickBlockColor>"
    client = rig.client()
    api = rig.connect(client) if attach else rig.start(client)
    game = Game(api, client)
    try:
        run(game, pick_color)
    except Exception as e:  # noqa: BLE001 -- report and fall through to the summary
        check("run completed", False, f"{type(e).__name__}: {e}")
    finally:
        if not attach and not keep:
            rig.stop(client)

    failed = [name for name, ok, _ in results if not ok]
    print(f"\n{len(results) - len(failed)} passed, {len(failed)} failed")
    for name in failed:
        print(f"  FAIL {name}")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
