"""In-game checks that a group's toolbar slot starts over once it is not in use, on the
isolated client of rig.py.

    TBM_SLOT=15 uv run --with httpx python tests/group_reset_check.py [--attach] [--keep]

A custom block group of two armor slopes, a slot of the heavy armor block, whose vanilla
block group has six blocks, and a welder are on the toolbar. Each group slot is used,
moved on to another block with the wheel, and left by picking the welder or by putting
the block away. The option "Custom block groups start over" makes the custom block group's
slot show and equip its first block again, "Vanilla block groups start over" makes the
heavy armor block's slot equip that block instead of the variant used last. Turned off, both continue where they were. While
a slot is in use, pressing it again and switching toolbar pages change nothing.

TBM_RESET_CUSTOM=0 and TBM_RESET_VANILLA=0 start the client with that option off, both are
on by default. The plugin settings must show both options, checked as configured.
Prints PASS or FAIL per check and exits non-zero on a failure.
--attach uses a client this script started before with --keep, which leaves it running.
"""

from __future__ import annotations

import os
import sys
import time

import rig
from item_groups_check import GUN, TOOLBAR_CONFIG, Game, check, held_check, results

RESET_CUSTOM = os.environ.get("TBM_RESET_CUSTOM", "1") == "1"
RESET_VANILLA = os.environ.get("TBM_RESET_VANILLA", "1") == "1"

CUBE = "CubeBlock/"
LIGHT = "LargeBlockArmorSlope"
HEAVY = "LargeHeavyBlockArmorSlope"
SMALL_HEAVY = "SmallHeavyBlockArmorSlope"
GAME_BLOCK = "LargeHeavyBlockArmorBlock"
# The other blocks of the game's ArmorHeavyCubeGroup
GAME_VARIANTS = {
    "LargeHeavyBlockArmorSlope",
    "LargeHeavyBlockArmorCorner",
    "LargeHeavyBlockArmorCornerInv",
    "LargeBlockHeavyArmorCornerSquare",
    "LargeBlockHeavyArmorCornerSquareInverted",
}
WELDER = "Welder/Welder4"

GROUP_SLOT = 0
GAME_SLOT = 1
WELDER_SLOT = 2
OPTIONS = {
    "Custom block groups start over": RESET_CUSTOM,
    "Vanilla block groups start over": RESET_VANILLA,
}


def subtype(held: str | None) -> str | None:
    return held.rsplit("/", 1)[-1] if held else None


class Icons:
    """The icons of the toolbar slots and of the blocks"""

    def __init__(self, game: Game):
        self.game = game
        self.blocks: dict[str, str] = {}

    def block(self, name: str) -> str:
        if name not in self.blocks:
            found = self.game.api.get_definition("MyObjectBuilder_CubeBlock", name)
            self.blocks[name] = found["icons"][0]
        return self.blocks[name]

    def slot(self, slot: int) -> str | None:
        """The icon of a slot as the toolbar of the G menu shows it. Remote can't see the
        HUD's toolbar, both draw the icons of the toolbar items."""
        screen = self.game.open_toolbar_config()
        time.sleep(0.5)
        items = self.game.grids(screen)["toolbar"]["properties"]["items"]
        item = next((i for i in items if i["slot"] == slot), None)
        self.game.close_screen(TOOLBAR_CONFIG)
        return item["icons"][0] if item and item.get("icons") else None

    def shows(self, name: str, slot: int, block: str) -> bool:
        time.sleep(0.5)
        shown = self.slot(slot)
        return check(
            name,
            shown == self.block(block),
            f"shows {shown}, {block} is {self.block(block)}",
        )


def check_settings(game: Game) -> None:
    api = game.api
    rig.focus_gameplay(api)
    api.key("OemQuestion", ["LeftControl", "LeftAlt"])
    plugins = game.wait_screen("ConfigurePlugin")
    rows = game.control(plugins, "Table")["properties"]["rows"]
    row = next(r["index"] for r in rows if r["cells"][0] == "Toolbar Manager")
    api.control_click(name="Table", screen=plugins, row=row, double_click=True)
    settings = game.wait_screen("SettingsScreen")
    time.sleep(1)
    controls = game.controls(settings)
    for label, expected in OPTIONS.items():
        at = next(
            (
                i
                for i, c in enumerate(controls)
                if (c.get("properties") or {}).get("text") == label
            ),
            None,
        )
        box = (
            None
            if at is None
            else next(
                (
                    c
                    for c in controls[at + 1 :]
                    if c.get("type") == "MyGuiControlCheckbox"
                ),
                None,
            )
        )
        state = (box or {}).get("properties", {}).get("isChecked")
        check(
            f"settings: '{label}' shown, checked {expected}",
            state is expected,
            f"label found {at is not None}, checked {state}",
        )
    game.shot("settings")
    for kind in ("SettingsScreen", "ConfigurePlugin"):
        index = game.screen_index(kind)
        if index is not None:
            api.close_screen(index)
            time.sleep(0.5)
    rig.focus_gameplay(api)


def setup(game: Game) -> None:
    api = game.api
    rig.focus_gameplay(api)
    for slot in range(18):
        api.set_toolbar_slot(slot, None)
    game.give("Welder4Item")

    screen = game.open_edit_mode()
    game.name_dialog(screen, "NewGroupButton", "Slopes")
    game.select_category(screen, "Armor Blocks")
    game.drag_to_group(screen, "Light Armor Slope", 0)
    game.drag_to_group(screen, "Heavy Armor Slope", 1)
    game.click(screen, "DoneGroupButton")
    game.close_screen(TOOLBAR_CONFIG)

    screen = game.open_toolbar_config()
    game.select_category(screen, "Groups")
    game.drag_group_to_toolbar(screen, "Slopes", GROUP_SLOT)
    game.close_screen(TOOLBAR_CONFIG)

    api.set_toolbar_slot(GAME_SLOT, CUBE + GAME_BLOCK)
    api.set_toolbar_slot(WELDER_SLOT, f"{GUN}/Welder4Item")
    time.sleep(1)
    toolbar = game.toolbar()
    check(
        "toolbar set up",
        toolbar.get(GROUP_SLOT, {}).get("name") == "Slopes"
        and GAME_SLOT in toolbar
        and WELDER_SLOT in toolbar,
        str({k: v["name"] for k, v in toolbar.items()}),
    )
    game.holster()


def leave(game: Game, how: str) -> None:
    if how == "welder":
        game.press_slot(WELDER_SLOT)
        held_check(game, "  the welder in hand", WELDER)
    else:
        game.holster()


# The wheel input is an absolute counter, so it is cleared only at the end: after a clear
# the game still has the last value, and setting it again would not turn the wheel.


def group_slot(game: Game, icons: Icons) -> None:
    word = "starts over" if RESET_CUSTOM else "continues"
    for how in ("welder", "holster"):
        game.press_slot(GROUP_SLOT)
        if subtype(game.held()) != HEAVY:
            game.scroll(120)
        held_check(game, f"group, before {how}: on the second block", CUBE + HEAVY)
        icons.shows(
            f"group, before {how}: the slot shows the block in hand", GROUP_SLOT, HEAVY
        )

        leave(game, how)
        game.shot(f"group-after-{how}")
        first = LIGHT if RESET_CUSTOM else HEAVY
        icons.shows(
            f"group, after {how}: the slot {word}, shows {first}", GROUP_SLOT, first
        )
        game.press_slot(GROUP_SLOT)
        held_check(
            game, f"group, after {how}: the slot {word}, equips {first}", CUBE + first
        )
        game.holster()

    # In use, the slot keeps its block: pressing it again switches the size, and another
    # toolbar page leaves it in the hand and on the wheel
    game.press_slot(GROUP_SLOT)
    if subtype(game.held()) != HEAVY:
        game.scroll(120)
    game.press_slot(GROUP_SLOT)
    held_check(game, "group in use, pressed again: the small block", CUBE + SMALL_HEAVY)
    game.press_slot(GROUP_SLOT)
    held_check(game, "group in use, pressed again: the large block", CUBE + HEAVY)
    for page in (1, 0):
        rig.focus_gameplay(game.api)
        game.api.key(f"D{page + 1}", ["LeftControl"])
        time.sleep(1.0)
    icons.shows(
        "group in use, after a page switch: the slot shows the block in hand",
        GROUP_SLOT,
        HEAVY,
    )
    game.scroll(120)
    held_check(
        game,
        "group in use, after a page switch: the wheel steps the group",
        CUBE + LIGHT,
    )
    game.holster()


def game_slot(game: Game, icons: Icons) -> None:
    word = "starts over" if RESET_VANILLA else "continues"
    for how in ("welder", "holster"):
        game.press_slot(GAME_SLOT)
        if subtype(game.held()) == GAME_BLOCK:
            game.scroll(120)
        variant = subtype(game.held())
        check(
            f"game, before {how}: the wheel steps to a variant",
            variant in GAME_VARIANTS,
            str(variant),
        )
        icons.shows(
            f"game, before {how}: the slot shows its own block", GAME_SLOT, GAME_BLOCK
        )

        leave(game, how)
        game.shot(f"game-after-{how}")
        icons.shows(
            f"game, after {how}: the slot shows its own block", GAME_SLOT, GAME_BLOCK
        )
        game.press_slot(GAME_SLOT)
        expected = GAME_BLOCK if RESET_VANILLA else variant
        held_check(
            game,
            f"game, after {how}: the slot {word}, equips {expected}",
            CUBE + str(expected),
        )
        game.holster()


def run(game: Game) -> None:
    errors_before = game.exception_count()
    icons = Icons(game)
    check_settings(game)
    setup(game)
    group_slot(game, icons)
    game_slot(game, icons)
    game.clear_scroll()
    check(
        "no exceptions in the game log",
        game.exception_count() == errors_before,
        f"{game.exception_count() - errors_before} new",
    )


def main() -> int:
    if not (RESET_CUSTOM and RESET_VANILLA):
        os.environ["TBM_CONFIG"] = os.environ.get("TBM_CONFIG", "") + (
            f"<ResetCustomGroupSlots>{str(RESET_CUSTOM).lower()}</ResetCustomGroupSlots>"
            f"<ResetVanillaGroupSlots>{str(RESET_VANILLA).lower()}</ResetVanillaGroupSlots>"
        )
    print(f"Custom block groups start over: {RESET_CUSTOM}, vanilla: {RESET_VANILLA}")

    attach = "--attach" in sys.argv
    keep = "--keep" in sys.argv
    client = rig.client()
    api = rig.connect(client) if attach else rig.start(client)
    game = Game(api, client)
    try:
        run(game)
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
