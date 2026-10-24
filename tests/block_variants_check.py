"""In-game checks of game block groups added to a group, on the isolated client of rig.py.

    TBM_SLOT=15 uv run --with httpx python tests/block_variants_check.py [--attach] [--keep]

The light and heavy armor blocks of the G menu are game block groups (the tiles with a +).
Dragged or double clicked into a group, they add their blocks one by one. Slot presses and
the mouse wheel then step through the group's own order, not the game's variant cycle.
Without --attach the game restarts at the end to check that the group reloads as saved.
Prints PASS or FAIL per check and exits non-zero on a failure. --attach and --keep work
as in item_groups_check.py.
"""

from __future__ import annotations

import sys

import rig
from item_groups_check import (
    TOOLBAR_CONFIG,
    Game,
    check,
    held_check,
    results,
)

LIGHT = "MyObjectBuilder_CubeBlock/LargeBlockArmorBlock"
HEAVY = "MyObjectBuilder_CubeBlock/LargeHeavyBlockArmorBlock"
SLOT = 0


def short(item: str) -> str:
    return item.removeprefix("MyObjectBuilder_")


def items(game: Game) -> list[str]:
    return game.saved_groups().get("Armor", [])


def build_group(game: Game) -> tuple[list[str], list[str]]:
    """The group by drag and double click. Returns the light and heavy blocks it got."""
    screen = game.open_edit_mode()
    game.name_dialog(screen, "NewGroupButton", "Armor")
    game.select_category(screen, "Armor Blocks")

    game.drag_to_group(screen, "Light Armor Block", 0)
    light = items(game)
    check(
        "drag adds the blocks of the game group, its first block first",
        len(light) > 1 and light[0] == LIGHT and len(set(light)) == len(light),
        str(light),
    )
    check(
        "the group lists each block",
        len(game.group_items(screen)) == len(light),
        str(game.group_items(screen)),
    )

    game.double_click_block(screen, "Heavy Armor Block")
    both = items(game)
    heavy = both[len(light) :]
    check(
        "double click appends the blocks of another game group",
        both[: len(light)] == light
        and len(heavy) > 1
        and heavy[0] == HEAVY
        and not set(heavy) & set(light),
        str(heavy),
    )

    game.drag_to_group(screen, "Light Armor Block", 0)
    game.double_click_block(screen, "Heavy Armor Block")
    check("blocks already in the group are skipped", items(game) == both)

    # Heavy first, and without the second light block, so the group's order differs
    # from the game's variant cycle
    game.move_in_group(screen, len(light), 0)
    game.remove_from_group(screen, 2)
    expected = [heavy[0]] + light[:1] + light[2:] + heavy[1:]
    check(
        "single blocks of the group can be moved and removed",
        items(game) == expected,
        str(items(game)[:4]),
    )

    game.click(screen, "DoneGroupButton")
    game.select_category(screen, "Groups")
    game.drag_group_to_toolbar(screen, "Armor", SLOT)
    game.close_screen(TOOLBAR_CONFIG)
    check(
        "group on the toolbar",
        game.toolbar().get(SLOT, {}).get("name") == "Armor",
        str(game.toolbar().get(SLOT)),
    )
    return light, heavy


def run(game: Game) -> list[str]:
    rig.focus_gameplay(game.api)
    for slot in range(9):
        game.api.set_toolbar_slot(slot, None)
    errors_before = game.exception_count()

    light, heavy = build_group(game)
    group = items(game)

    def held(name: str, index: int) -> None:
        held_check(game, name, short(group[index % len(group)]))

    game.holster()
    game.press_slot(SLOT)
    held("slot press: the first block", 0)
    # Like the slot of a single block: the game switches to the small variant and back
    game.press_slot(SLOT)
    held_check(
        game,
        "pressed again: the game's small variant",
        short(group[0]).replace("/Large", "/Small", 1),
    )
    game.press_slot(SLOT)
    held("pressed again: the large one", 0)
    game.scroll(120)
    held("wheel up: the group's next block, not the game's next variant", 1)
    game.scroll(120)
    held("wheel up again", 2)
    game.scroll(-120)
    held("wheel down", 1)
    game.scroll(-120)
    held("wheel down again: the first block", 0)
    game.scroll(-120)
    held("wheel down: wraps around to the last block", -1)
    game.clear_scroll()

    check(
        "no exceptions in the game log",
        game.exception_count() == errors_before,
        f"{game.exception_count() - errors_before} new",
    )
    return group


def run_after_restart(game: Game, group: list[str]) -> None:
    rig.stop(game.client)
    rig.launch(game.client)
    api = rig.wait_api(game.client)
    rig.load_world(api, game.client.world)
    restarted = Game(api, game.client)
    screen = restarted.open_edit_mode()
    restarted.select_group(screen, "Armor")
    check(
        "the group reloads with its single blocks",
        items(restarted) == group and len(restarted.group_items(screen)) == len(group),
        str(restarted.group_items(screen)[:4]),
    )
    restarted.click(screen, "DoneGroupButton")
    restarted.close_screen(TOOLBAR_CONFIG)


def main() -> int:
    attach = "--attach" in sys.argv
    keep = "--keep" in sys.argv
    client = rig.client()
    api = rig.connect(client) if attach else rig.start(client)
    game = Game(api, client)
    try:
        group = run(game)
        if not attach:
            run_after_restart(game, group)
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
