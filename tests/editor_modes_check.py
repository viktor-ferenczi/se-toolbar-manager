"""Check the group editor with staging enabled and disabled in an isolated slot.

TBM_SLOT=15 uv run --with httpx python tests/editor_modes_check.py
"""

import os

import rig
from item_groups_check import Game


def expect(condition: bool, name: str) -> None:
    assert condition, name
    print(f"PASS {name}", flush=True)


def run(staging_enabled: bool) -> None:
    os.environ["TBM_STAGING_DISABLED"] = "0" if staging_enabled else "1"
    mode = "staging enabled" if staging_enabled else "staging disabled"
    client = rig.client()
    try:
        game = Game(rig.start(client), client)
        game.give("HandDrill4Item")
        screen = game.open_toolbar_config()
        expect(
            ("staging" in game.grids(screen)) == staging_enabled,
            f"{mode}: normal G menu respects the setting",
        )
        game.click(screen, "OpenItemGroupsButton")
        expect(game.editing(screen), f"{mode}: TG opens the editor")
        game.name_dialog(screen, "NewGroupButton", "Editor test")
        game.select_category(screen, "Character Tools")
        game.drag_to_group(screen, "Elite Hand Drill", 0)
        expect(
            game.group_items(screen) == ["Elite Hand Drill"]
            and game.saved_groups()["Editor test"]
            == ["MyObjectBuilder_PhysicalGunObject/HandDrill4Item"],
            f"{mode}: dragging into a new group saves its items",
        )
        game.click(screen, "OpenItemGroupsButton")
        expect(
            not game.editing(screen)
            and ("staging" in game.grids(screen)) == staging_enabled,
            f"{mode}: TG closes the editor and restores the normal G menu",
        )
        game.click(screen, "OpenItemGroupsButton")
        expect(
            game.editing(screen) and game.selected_group(screen) == "Editor test",
            f"{mode}: TG reopens the selected group",
        )
        game.click(screen, "DoneGroupButton")
        expect(
            not game.editing(screen)
            and ("staging" in game.grids(screen)) == staging_enabled,
            f"{mode}: Done closes the editor",
        )
        game.select_category(screen, "Groups")
        game.grid_click(
            screen, "blocks", game.grid_names(screen).index("Editor test"), "click"
        )
        expect(
            game.editing(screen) and game.selected_group(screen) == "Editor test",
            f"{mode}: clicking a group tile opens its editor",
        )
    finally:
        rig.stop(client)


if __name__ == "__main__":
    run(staging_enabled=True)
    run(staging_enabled=False)
    print("14 passed", flush=True)
