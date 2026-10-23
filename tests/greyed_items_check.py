"""In-game checks of adding greyed out items to a group, on the isolated client of rig.py.

    TBM_SLOT=15 uv run --with httpx python tests/greyed_items_check.py [--attach] [--keep]

The character of the test world has no hand tools, so the G menu shows them greyed out.
Outside edit mode they can't be dragged or double clicked, as in vanilla. In edit mode
they go into a group both ways, and the group equips the tool once it is in the
inventory. Needs a Remote whose grid gestures reach disabled items (after Remote 1.3.0).
Prints PASS or FAIL per check and exits non-zero on a failure.
"""

from __future__ import annotations

import sys

import rig
from item_groups_check import GUN, TOOLBAR_CONFIG, Game, check, held_check, results

GROUP = "Later"


def toolbar_names(game: Game) -> str:
    return str({k: v["name"] for k, v in game.toolbar().items()})


def run(game: Game) -> None:
    api = game.api
    rig.focus_gameplay(api)
    for slot in range(3, 9):
        api.set_toolbar_slot(slot, None)
    errors_before = game.exception_count()

    screen = game.open_toolbar_config()
    game.select_category(screen, "Character Tools")
    items = game.grids(screen)["blocks"]["properties"]["items"]
    greyed = {
        name
        for name, item in zip(game.grid_names(screen), items)
        if item.get("enabled") is False
    }
    check(
        "tools missing from the inventory are greyed out",
        {"Elite Hand Drill", "Elite Grinder", "Elite Welder"} <= greyed,
        str(sorted(greyed)),
    )

    # Vanilla outside edit mode: no drag, no double click
    toolbar = game.toolbar()
    welder = game.grid_names(screen).index("Elite Welder")
    game.drag(screen, "blocks", welder, "toolbar", 5)
    game.double_click_block(screen, "Elite Welder")
    check(
        "outside edit mode a greyed out tool is not dragged or double clicked",
        game.toolbar() == toolbar,
        toolbar_names(game),
    )

    # Edit mode: drag and double click add greyed out tools to the group
    game.click(screen, "OpenItemGroupsButton")
    game.name_dialog(screen, "NewGroupButton", GROUP)
    game.select_category(screen, "Character Tools")
    game.drag_to_group(screen, "Elite Hand Drill", 0)
    game.double_click_block(screen, "Elite Grinder")
    check(
        "edit mode: greyed out tools added by drag and by double click",
        game.group_items(screen) == ["Elite Hand Drill", "Elite Grinder"],
        str(game.group_items(screen)),
    )
    check(
        "the group is saved",
        game.saved_groups().get(GROUP)
        == [f"{GUN}/HandDrill4Item", f"{GUN}/AngleGrinder4Item"],
        str(game.saved_groups().get(GROUP)),
    )
    game.drag(screen, "blocks", welder, "toolbar", 5)
    check(
        "edit mode: a greyed out tool still does not go on the toolbar",
        game.toolbar() == toolbar,
        toolbar_names(game),
    )
    game.shot("greyed-items-group")

    # The group equips the tool once it is in the inventory
    game.click(screen, "DoneGroupButton")
    game.select_category(screen, "Groups")
    game.drag_group_to_toolbar(screen, GROUP, 4)
    game.close_screen(TOOLBAR_CONFIG)
    game.press_slot(4)
    held_check(game, "no tool of the group in the inventory: nothing equipped", None)
    game.give("HandDrill4Item")
    game.holster()
    game.press_slot(4)
    held_check(
        game, "the group equips the tool once it is there", "HandDrill/HandDrill4"
    )

    # Vanilla again after leaving edit mode
    screen = game.open_toolbar_config()
    game.select_category(screen, "Character Tools")
    toolbar = game.toolbar()
    game.drag(screen, "blocks", welder, "toolbar", 6)
    game.double_click_block(screen, "Elite Welder")
    check(
        "after edit mode a greyed out tool is again not dragged or double clicked",
        game.toolbar() == toolbar,
        toolbar_names(game),
    )
    game.close_screen(TOOLBAR_CONFIG)
    check(
        "no new exceptions",
        game.exception_count() == errors_before,
        f"{game.exception_count() - errors_before} new in the game log",
    )


def main() -> int:
    attach = "--attach" in sys.argv
    keep = "--keep" in sys.argv
    client = rig.client()
    api = rig.connect(client) if attach else rig.start(client)
    try:
        run(Game(api, client))
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
