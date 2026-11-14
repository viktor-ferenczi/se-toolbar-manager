"""In-game check of picking and cutting the faced block, on the isolated client of rig.py.

    TBM_SLOT=15 TBM_UNDO=~/ws/se/se1/plugins/se-undo \\
        uv run --with httpx python tests/faced_block_check.py [--color] [--attach] [--keep]

Pastes a wall of blocks ahead of the character and a small grid beside it, then presses
the default keys like a player: Ctrl+G and Ctrl+Insert to pick, Ctrl+Delete to cut.

Pick: the block in hand must be the faced one by type and subtype, in its size, the skin
for new blocks must be the block's, and a click must place it turned like the picked one.
The build color stays, or with --color (the "Pick the color too" option) becomes the
block's. Ctrl+G must not open the G menu.
Cut: refused without creative rights; removes a block that holds nothing else and takes
it into the hand; Ctrl+Z of the Undo plugin brings it back. In symmetry mode it removes
the mirrored block too, Undo restores both, and the block in hand places mirrored.
A cut that would split the grid asks first: No keeps the blocks, Yes removes them and
the grid splits. Screenshots of the dialogs go to the client's appdata.
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
MESSAGE_BOX = "MyGuiScreenMessageBox"

# The wall, seen from the front: a row of armor with four blocks standing on it.
#   slope  cargo    .    half   armor        y = 1
#   armor  armor  armor  armor  armor        y = 0
#   x=-2    -1      0      1      2
SLOPE, CARGO_CELL, HALF, MIRROR = (-2, 1, 0), (-1, 1, 0), (1, 1, 0), (2, 1, 0)
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
        block(CUBE, "LargeBlockArmorBlock", MIRROR),
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

    # The split question

    def question(self) -> tuple[int | None, str]:
        index = wait(lambda: self.game.screen_index(MESSAGE_BOX), 3)
        if index is None:
            return None, ""
        texts = [
            str((c.get("properties") or {}).get("text") or "")
            for c in self.game.controls(index)
        ]
        return index, " ".join(texts)

    def answer(self, index: int, text: str) -> None:
        self.api.control_click(text=text, screen=index)
        wait(lambda: self.game.screen_index(MESSAGE_BOX) is None, 5)
        time.sleep(1.0)


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

    # A block in hand, Ctrl+Insert, another type and no skin
    if check("the cargo container is in the crosshair", faced.face(wall, CARGO_CELL)):
        faced.press("Insert")
        held_is(
            faced, "Ctrl+Insert picks the container", CARGO, "LargeBlockSmallContainer"
        )
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
        faced.press("Insert")
        held_is(faced, "Ctrl+Insert picks the slope", CUBE, "LargeBlockArmorSlope")
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
            # Out of the way of the cut checks
            api.set_input_state(mouse_right=True)
            time.sleep(0.3)
            api.clear_input_state()
            time.sleep(1.0)
            check("the placed block is removed again", faced.count(wall) == len(before))


def cut_checks(faced: Faced, wall: int, undo: bool) -> None:
    game, api = faced.game, faced.api
    total = faced.count(wall)

    # Survival rules
    api.set_admin_flag("creativeTools", False)
    time.sleep(0.5)
    game.holster()
    if check("the half block is in the crosshair", faced.face(wall, HALF)):
        latest = faced.latest()
        faced.press("Delete")
        check(
            "survival: the cut is refused",
            faced.notified("needs creative mode", latest),
        )
        check("survival: the block stays", faced.count(wall) == total)
        check("survival: nothing in hand", game.held() is None, f"{game.held()}")
        faced.press("G")
        held_is(faced, "survival: pick works", CUBE, "LargeHalfArmorBlock")
    api.set_admin_flag("creativeTools", True)
    time.sleep(0.5)

    # A block that holds nothing else
    game.holster()
    if check("the half block is in the crosshair", faced.face(wall, HALF)):
        latest = faced.latest()
        faced.press("Delete")
        gone = wait(lambda: HALF not in faced.cubes(wall), 5)
        told = [
            n.get("text") for n in api.get_hud_notifications(latest)["notifications"]
        ]
        check("Ctrl+Delete removes the block", bool(gone), f"notifications: {told}")
        check(
            "no question for a block that holds nothing",
            game.screen_index(MESSAGE_BOX) is None,
        )
        check("only that block is removed", faced.count(wall) == total - 1)
        held_is(faced, "the cut block is in hand", CUBE, "LargeHalfArmorBlock")
        if undo:
            faced.press("Z")
            back = wait(lambda: faced.cubes(wall).get(HALF), 5)
            check(
                "Undo brings the cut block back",
                bool(back) and back["subtypeId"] == "LargeHalfArmorBlock",
                f"{back and back['subtypeId']}",
            )
            check(
                "Undo restores its color",
                bool(back)
                and tuple(round(c, 3) for c in back["colorMaskHsv"])
                == tuple(round(c, 3) for c in HALF_COLOR),
            )

    # Symmetry: the mirrored block goes too
    api.set_game_option("cubeBuilderUseSymmetry", True)
    api.set_grid_symmetry(wall, x=(0, 0, 0), x_odd=False)
    total = faced.count(wall)
    game.holster()
    if check("the slope is in the crosshair", faced.face(wall, SLOPE)):
        faced.press("Delete")
        gone = wait(lambda: not {SLOPE, MIRROR} & set(faced.cubes(wall)), 5)
        check("symmetry: the cut removes the mirrored block too", bool(gone))
        check("symmetry: two blocks removed", faced.count(wall) == total - 2)
        check("symmetry: no question", game.screen_index(MESSAGE_BOX) is None)
        held_is(
            faced, "symmetry: the faced block is in hand", CUBE, "LargeBlockArmorSlope"
        )
        if undo:
            faced.press("Z")
            back = wait(lambda: {SLOPE, MIRROR} <= set(faced.cubes(wall)), 5)
            cubes = faced.cubes(wall)
            check("symmetry: Undo brings both blocks back", bool(back))
            check(
                "symmetry: Undo restores the skin",
                (cubes.get(SLOPE) or {}).get("skin") == SKIN,
                f"{(cubes.get(SLOPE) or {}).get('skin')!r}",
            )
            total = faced.count(wall)

    # The block in hand places mirrored, as any block does
    if check("an armor block of the row is in the crosshair", faced.face(wall, ROW[0])):
        held_is(
            faced, "symmetry: the slope is still in hand", CUBE, "LargeBlockArmorSlope"
        )
        before = faced.cubes(wall)
        faced.click()
        new = [cell for cell in faced.cubes(wall) if cell not in before]
        mirrored = (
            len(new) == 2 and new[0][0] == -new[1][0] and new[0][1:] == new[1][1:]
        )
        check("symmetry: the picked block places mirrored", mirrored, f"new: {new}")
        api.set_input_state(mouse_right=True)
        time.sleep(0.3)
        api.clear_input_state()
        time.sleep(1.0)
        check(
            "symmetry: the placed blocks are removed again",
            faced.count(wall) == len(before),
        )

    # Symmetry and a split: both blocks of the row hold the ends
    total = faced.count(wall)
    game.holster()
    if check("the row's second block is in the crosshair", faced.face(wall, ROW[1])):
        faced.press("Delete")
        index, text = faced.question()
        if check("symmetry: a splitting cut asks first", index is not None):
            check(
                "symmetry: the question counts the blocks",
                "these 2 blocks" in text,
                text,
            )
            game.shot("cut-split-dialog-symmetry")
            faced.answer(index, "No")
            check("symmetry: No keeps the blocks", faced.count(wall) == total)
            check(
                "symmetry: No leaves the hand empty",
                game.held() is None,
                f"{game.held()}",
            )
    api.set_grid_symmetry(wall, x=None)
    api.set_game_option("cubeBuilderUseSymmetry", False)

    # The split question, without symmetry
    grids = len(api.list_grids())
    if check("the row's middle block is in the crosshair", faced.face(wall, ROW[2])):
        faced.press("Delete")
        index, text = faced.question()
        if check("a splitting cut asks first", index is not None):
            check(
                "the question tells the parts",
                "this block splits the grid into 2 parts" in text,
                text,
            )
            game.shot("cut-split-dialog")
            faced.answer(index, "No")
            check("No keeps the block", faced.count(wall) == total)
            check("No leaves the hand empty", game.held() is None, f"{game.held()}")
            check("No keeps one grid", len(api.list_grids()) == grids)

        faced.press("Delete")
        index, _ = faced.question()
        if check("the cut asks again", index is not None):
            faced.answer(index, "Yes")
            split = wait(lambda: len(api.list_grids()) == grids + 1, 10)
            check(
                "Yes removes the block and the grid splits",
                bool(split),
                f"{len(api.list_grids())} grids",
            )
            held_is(
                faced,
                "the cut block is in hand after Yes",
                CUBE,
                "LargeBlockArmorBlock",
            )


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
    check("the wall is pasted", len(cubes) == 9, f"{len(cubes)} blocks")
    check("the slope has its skin", (cubes.get(SLOPE) or {}).get("skin") == SKIN)

    pick_checks(faced, wall, small, pick_color)
    if not pick_color:
        cut_checks(faced, wall, bool(os.environ.get("TBM_UNDO")))

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
