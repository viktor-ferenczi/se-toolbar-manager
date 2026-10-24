"""In-game checks of several groups on separate toolbar slots, on the isolated client of rig.py.

    TBM_SLOT=15 uv run --with httpx python tests/separate_groups_check.py [--attach] [--keep]

A tool group, an overlapping tool group, a block group and a mixed group of a tool
and a block, on slots of two toolbar pages. Each slot steps only through its own group,
and switching back to a slot continues its group where it was. Prints PASS or FAIL per
check and exits non-zero on a failure. --attach and --keep work as in item_groups_check.py.
"""

from __future__ import annotations

import sys
import time

import rig
from item_groups_check import (
    TOOLBAR_CONFIG,
    Game,
    check,
    held_check,
    results,
)

WELDER = "Welder/Welder4"
GRINDER = "AngleGrinder/AngleGrinder4"
DRILL = "HandDrill/HandDrill4"
LIGHT = "CubeBlock/LargeBlockArmorSlope"
HEAVY = "CubeBlock/LargeHeavyBlockArmorSlope"
SMALL_LIGHT = "CubeBlock/SmallBlockArmorSlope"
SMALL_HEAVY = "CubeBlock/SmallHeavyBlockArmorSlope"

# Group name, then the G menu category and name of each of its items
GROUPS = {
    "Cutters": [
        ("Character Tools", "Elite Grinder"),
        ("Character Tools", "Elite Hand Drill"),
    ],
    "Armor": [
        ("Armor Blocks", "Light Armor Slope"),
        ("Armor Blocks", "Heavy Armor Slope"),
    ],
    "Mixed": [
        ("Character Tools", "Elite Welder"),
        ("Armor Blocks", "Light Armor Slope"),
    ],
}

# Page and slot of each group: Hand Tools is the default welder, grinder and drill group
SLOTS = {"Hand Tools": (0, 0), "Cutters": (0, 1), "Armor": (0, 2), "Mixed": (1, 0)}


def switch_page(game: Game, page: int) -> None:
    rig.focus_gameplay(game.api)
    game.api.key(f"D{page + 1}", ["LeftControl"])
    time.sleep(1.0)


def setup(game: Game) -> None:
    api = game.api
    rig.focus_gameplay(api)
    for slot in range(18):
        api.set_toolbar_slot(slot, None)
    for subtype in ("Welder4Item", "AngleGrinder4Item", "HandDrill4Item"):
        game.give(subtype)

    screen = game.open_edit_mode()
    for name, items in GROUPS.items():
        game.name_dialog(screen, "NewGroupButton", name)
        for index, (category, item) in enumerate(items):
            game.select_category(screen, category)
            game.drag_to_group(screen, item, index)
    game.click(screen, "DoneGroupButton")
    game.close_screen(TOOLBAR_CONFIG)
    check(
        "groups created",
        all(len(game.saved_groups().get(name, [])) == 2 for name in GROUPS),
        str(game.saved_groups()),
    )

    for page in (0, 1):
        switch_page(game, page)
        screen = game.open_toolbar_config()
        game.select_category(screen, "Groups")
        for name, (group_page, slot) in SLOTS.items():
            if group_page == page:
                game.drag_group_to_toolbar(screen, name, slot)
        game.close_screen(TOOLBAR_CONFIG)

    toolbar = game.toolbar()
    placed = {
        name: toolbar.get(page * 9 + slot, {}).get("name")
        for name, (page, slot) in SLOTS.items()
    }
    check(
        "four groups on two toolbar pages",
        all(placed[name] == name for name in SLOTS),
        str({k: v["name"] for k, v in toolbar.items()}),
    )
    switch_page(game, 0)
    game.holster()


def run(game: Game) -> None:
    errors_before = game.exception_count()
    setup(game)

    def use(name: str, expected: str, what: str) -> None:
        page, slot = SLOTS[name]
        switch_page(game, page)
        game.press_slot(slot)
        held_check(game, f"{name}: {what}", expected)

    # Two tool groups sharing the grinder and the drill
    use("Hand Tools", WELDER, "first kind")
    use("Hand Tools", GRINDER, "pressed again, next kind")
    use("Cutters", GRINDER, "same tool already in hand, stays")
    use("Cutters", DRILL, "pressed again, its own next kind")
    use("Hand Tools", GRINDER, "back to the slot, continues where it was")
    use("Hand Tools", DRILL, "pressed again")
    use("Hand Tools", WELDER, "pressed again, wraps around")
    use("Cutters", DRILL, "back to the slot, continues where it was")

    # A block group in between
    use("Armor", LIGHT, "first block")
    use("Armor", SMALL_LIGHT, "pressed again, the game's small variant")
    use("Armor", LIGHT, "pressed again, the large one")
    game.scroll(120)
    held_check(game, "wheel: the next block", HEAVY)
    game.clear_scroll()
    use("Hand Tools", WELDER, "after the block group, still where it was")
    use("Armor", HEAVY, "after the tool group, still where it was")

    # A mixed group on another page, sharing the welder and the light armor block
    use("Mixed", WELDER, "first item, on page 2")
    use("Mixed", LIGHT, "pressed again, the block")
    use("Armor", HEAVY, "page 1 again, still where it was")
    use("Armor", SMALL_HEAVY, "pressed again, the game's small variant")
    use("Armor", HEAVY, "pressed again, the large one")
    game.scroll(120)
    held_check(game, "wheel: the next block", LIGHT)
    game.clear_scroll()
    use("Mixed", LIGHT, "same block already in hand, stays")
    use("Mixed", WELDER, "pressed again, the tool")
    use("Hand Tools", WELDER, "same tool already in hand, stays")
    use("Cutters", DRILL, "still where it was")

    # The wheel steps through the group in use only
    use("Hand Tools", WELDER, "back to the welder")
    game.scroll(120)
    held_check(game, "wheel steps Hand Tools, not Mixed", GRINDER)
    use("Cutters", DRILL, "after the wheel, still where it was")
    game.scroll(120)
    held_check(game, "wheel steps Cutters, not Hand Tools", GRINDER)
    game.clear_scroll()
    use("Hand Tools", GRINDER, "where the wheel left it")

    # Holstering keeps the positions
    game.holster()
    use("Cutters", GRINDER, "after holstering, continues where it was")
    game.holster()
    use("Mixed", WELDER, "after holstering, continues where it was")

    check(
        "no exceptions in the game log",
        game.exception_count() == errors_before,
        f"{game.exception_count() - errors_before} new",
    )


def main() -> int:
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
