"""In-game checks of the group order and the group tiles, on the isolated client of rig.py.

    TBM_SLOT=15 uv run --with httpx python tests/group_order_check.py [--attach] [--keep]

Groups are listed by name, ignoring case and with numbers by value, both in the group
editor's drop-down and in the Groups category of the G menu, and stay so after a new
group, a rename and a duplicate. The selection follows the group it was on. Each tile of
the Groups category shows the group's first item, an empty group an empty tile.
Remote shows only the selected key of a combobox, so the drop-down is read key by key from
the name the Rename dialog offers. Screenshots go to the client's screenshots folder.
Prints PASS or FAIL per check and exits non-zero on a failure. --attach and --keep work
as in item_groups_check.py.
"""

from __future__ import annotations

import shutil
import sys
import time

import rig
from item_groups_check import TOOLBAR_CONFIG, Game, check, results


def selected_name(game: Game, screen: int) -> str:
    """The name of the selected group, as the Rename dialog offers it"""
    game.click(screen, "RenameGroupButton")
    dialog = game.wait_screen("NameDialog")
    name = game.control(dialog, "Textbox")["properties"]["text"]
    game.close_screen("NameDialog")
    return name


def drop_down(game: Game, screen: int) -> list[str]:
    """The names of the drop-down in key order, which is the order it lists them in.
    Selects each one, then the one selected before again."""
    selected = game.control(screen, "GroupSelector")["properties"]["selectedKey"]
    names = []
    for key in range(len(list(game.client.groups.glob("*.xml")))):
        game.api.control_set("GroupSelector", key, screen=screen)
        game.wait_screen(TOOLBAR_CONFIG)
        names.append(selected_name(game, screen))
    game.api.control_set("GroupSelector", selected, screen=screen)
    return names


def select(game: Game, screen: int, name: str) -> None:
    game.api.control_set(
        "GroupSelector", drop_down(game, screen).index(name), screen=screen
    )


def open_drop_down(game: Game, screen: int) -> None:
    """Clicks the drop-down open, for the screenshot. Remote can't click a combobox by
    name, so this clicks its pixel. GUI coordinates span the 4:3 rectangle centered in
    the rig's 1280x720 window."""
    selector = game.control(screen, "GroupSelector")
    x = selector["topLeft"]["x"] + selector["size"]["x"] / 2
    y = selector["topLeft"]["y"] + selector["size"]["y"] / 2
    game.api.click(round(160 + 960 * x), round(720 * y))
    time.sleep(1)


def tiles(game: Game) -> dict[str, list[str]]:
    """The tiles of the Groups category in grid order, with their icons"""
    screen = game.screen_index(TOOLBAR_CONFIG)
    if screen is None:
        screen = game.open_toolbar_config()
    if game.editing(screen):
        game.click(screen, "DoneGroupButton")
    game.select_category(screen, "Groups")
    items = game.grids(screen)["blocks"]["properties"]["items"]
    return {
        (item.get("tooltips") or [""])[0].split("\n")[0].strip(): item.get("icons")
        or []
        for item in items
    }


def order_check(game: Game, screen: int, step: str, expected: list[str], selected: str):
    names = drop_down(game, screen)
    check(
        f"{step}: the drop-down lists the groups by name", names == expected, str(names)
    )
    current = selected_name(game, screen)
    check(f"{step}: the selection stays on {selected}", current == selected, current)


def run(game: Game) -> None:
    screen = game.open_edit_mode()
    game.name_dialog(screen, "NewGroupButton", "Group 10")
    game.select_category(screen, "Armor Blocks")
    game.drag_to_group(screen, "Light Armor Block", 0)
    first_block = game.grids(screen)["group"]["properties"]["items"][0]["icons"]
    game.name_dialog(screen, "NewGroupButton", "group 2")
    game.double_click_block(screen, "Heavy Armor Block")
    game.name_dialog(screen, "NewGroupButton", "Alpha")
    order_check(
        game, screen, "new", ["Alpha", "group 2", "Group 10", "Hand Tools"], "Alpha"
    )

    game.name_dialog(screen, "RenameGroupButton", "Zeta")
    order_check(
        game, screen, "rename", ["group 2", "Group 10", "Hand Tools", "Zeta"], "Zeta"
    )

    select(game, screen, "group 2")
    game.name_dialog(screen, "DuplicateGroupButton", "Group 1")
    expected = ["Group 1", "group 2", "Group 10", "Hand Tools", "Zeta"]
    order_check(game, screen, "duplicate", expected, "Group 1")

    select(game, screen, "Group 10")
    open_drop_down(game, screen)
    game.shot("group-drop-down")
    # Closes the open list, or the G menu if the click didn't open one
    game.api.key("Escape")

    shown = tiles(game)
    game.shot("groups-category")
    check(
        "the Groups category lists the groups by name",
        list(shown) == expected,
        str(list(shown)),
    )
    check(
        "a group's tile shows its first block",
        bool(first_block) and shown.get("Group 10") == first_block,
        f"{shown.get('Group 10')} vs {first_block}",
    )
    check(
        "an empty group has an empty tile",
        "Zeta" in shown and not shown["Zeta"],
        str(shown.get("Zeta")),
    )
    check("no exceptions in the game log", game.exception_count() == 0)


def main() -> int:
    attach = "--attach" in sys.argv
    keep = "--keep" in sys.argv
    client = rig.client()
    if not attach:
        # Only the default group to start with
        shutil.rmtree(client.groups, ignore_errors=True)
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
