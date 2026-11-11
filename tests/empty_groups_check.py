"""In-game check that empty groups are not saved and don't block their names (SE-1356).

    TBM_SLOT=15 uv run --with httpx python tests/empty_groups_check.py

Starts the client with an empty group file left by an older version next to a group
with an item, then drives the group editor of the G menu: a new group closed empty
with Done, the same name used again for a group with an item, and that group emptied
and closed with Done. Prints PASS or FAIL per check and exits non-zero on a failure.
"""

from __future__ import annotations

import shutil
import sys

import rig
from item_groups_check import Game, check, results

DRILL = "MyObjectBuilder_PhysicalGunObject/HandDrill4Item"
NAME = "Stale"


def group_xml(name: str, order: int, items: list[str]) -> str:
    strings = "".join(f"<string>{item}</string>" for item in items)
    return (
        '<?xml version="1.0" encoding="utf-8"?>\n'
        '<ItemGroup xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" '
        'xmlns:xsd="http://www.w3.org/2001/XMLSchema">'
        f"<Id>{name.lower()}</Id><Name>{name}</Name><Order>{order}</Order>"
        f"<Items>{strings}</Items></ItemGroup>"
    )


def start(client: rig.Client):
    """rig.start with group files written before the plugin loads them"""
    if client.groups.exists():
        shutil.rmtree(client.groups)
    client.groups.mkdir(parents=True)
    (client.groups / "Kept.xml").write_text(group_xml("Kept", 0, [DRILL]))
    (client.groups / f"{NAME}.xml").write_text(group_xml(NAME, 1, []))
    rig.launch(client)
    api = rig.wait_api(client)
    rig.load_world(api, rig.prepare_world(client.world))
    return api


def new_group(game: Game, screen: int, name: str) -> bool:
    """New with the name; False if the editor refused it"""
    try:
        game.name_dialog(screen, "NewGroupButton", name)
        return True
    except AssertionError:
        game.close_screen("MyGuiScreenMessageBox")
        game.close_screen("NameDialog")
        return False


def main() -> int:
    client = rig.client()
    try:
        game = Game(start(client), client)
        game.give("HandDrill4Item")
        stale = client.groups / f"{NAME}.xml"

        screen = game.open_edit_mode()
        check(
            "an empty group file of an older version is deleted on load",
            not stale.exists() and game.saved_groups() == {"Kept": [DRILL]},
            str(game.saved_groups()),
        )

        check(
            "the name of the deleted empty group can be used",
            new_group(game, screen, NAME),
        )
        check("a new group without items is not saved", not stale.exists())
        game.click(screen, "DoneGroupButton")
        check(
            "Done on a new empty group leaves no file",
            not game.editing(screen) and not stale.exists(),
            str(game.saved_groups()),
        )

        screen = game.open_edit_mode()
        check(
            "the name is free again after Done on an empty group",
            new_group(game, screen, NAME),
        )
        game.select_category(screen, "Character Tools")
        game.drag_to_group(screen, "Elite Hand Drill", 0)
        check(
            "a group with an item is saved",
            game.saved_groups().get(NAME) == [DRILL],
            str(game.saved_groups()),
        )
        game.click(screen, "DoneGroupButton")
        check(
            "Done keeps a group with items",
            game.saved_groups() == {"Kept": [DRILL], NAME: [DRILL]},
            str(game.saved_groups()),
        )

        screen = game.open_edit_mode()
        game.select_group(screen, NAME)
        game.remove_from_group(screen, 0)
        game.click(screen, "DoneGroupButton")
        check(
            "Done on an emptied group deletes its file",
            not stale.exists() and game.saved_groups() == {"Kept": [DRILL]},
            str(game.saved_groups()),
        )

        screen = game.open_edit_mode()
        check(
            "the emptied group's name can be reused",
            new_group(game, screen, NAME),
        )
        game.select_category(screen, "Character Tools")
        game.drag_to_group(screen, "Elite Hand Drill", 0)
        game.click(screen, "DoneGroupButton")
        check(
            "the reused name saves with its item",
            game.saved_groups() == {"Kept": [DRILL], NAME: [DRILL]},
            str(game.saved_groups()),
        )
        check("no exceptions in the game log", game.exception_count() == 0)
    finally:
        rig.stop(client)

    failed = [name for name, ok, _ in results if not ok]
    print(f"\n{len(results) - len(failed)} passed, {len(failed)} failed")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
