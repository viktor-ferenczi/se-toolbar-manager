"""A single click in the staging area or the group editor shows the block in the info panel.

    TBM_SLOT=15 uv run --with httpx python tests/staging_info_check.py [--attach] [--keep]

The block grid of the G menu updates the info panel on the right as the mouse moves over
it. A click on the staging area or on the group editor's grid did nothing to it (SE-996).
Prints PASS or FAIL per check and exits non-zero on a failure.
--attach uses a client already started with `rig.py start`, --keep leaves it running.
"""

from __future__ import annotations

import sys
import time
import traceback

import rig
from item_groups_check import TOOLBAR_CONFIG, Game, check, results, wait


def shown_icons(game: Game, screen: int) -> tuple[str, ...]:
    """The block variant icons of the info panel on the right. They identify the shown
    block group, the block name label draws its text without exposing it to Remote."""
    variants = [
        c
        for c in game.controls(screen)
        if c.get("name") == "BlockVariantsGrid" and c.get("visible")
    ]
    if not variants:
        return ()
    return tuple(i["icons"][0] for i in variants[0]["properties"]["items"])


def icon(game: Game, screen: int, grid: str, slot: int) -> str:
    items = game.grids(screen)[grid]["properties"]["items"]
    return next(i for i in items if i["slot"] == slot)["icons"][0]


def shows(game: Game, screen: int, expected: str) -> bool:
    return bool(wait(lambda: expected in shown_icons(game, screen), 5))


def follows(game: Game, screen: int, grid: str, a: int, b: int, name: str) -> None:
    """Selects slot b, then slot a and b of the grid, holding blocks of different groups,
    and checks that the panel follows. A click selects the slot under the mouse, Remote's
    "select" does the same without moving the mouse. Remote's click gestures can't be used:
    when they end, the game's cursor jumps to the middle of the block grid, whose own hover
    handler then shows the block there."""
    game.grid_click(screen, grid, b, "select")
    check(
        f"{name}: the panel doesn't show the block before its click",
        icon(game, screen, grid, a) not in shown_icons(game, screen),
        str(shown_icons(game, screen)),
    )
    for slot in (a, b):
        game.grid_click(screen, grid, slot, "select")
        check(
            f"{name}: selecting slot {slot} shows its block",
            shows(game, screen, icon(game, screen, grid, slot)),
            f"panel shows {shown_icons(game, screen)}",
        )


def run(game: Game) -> None:
    screen = game.open_toolbar_config()
    blocks = game.grids(screen)["blocks"]["properties"]["items"]
    # Two blocks of different groups than the one the panel shows (Remote's gestures
    # leave the game's cursor in the middle of the block grid, the panel shows the block there)
    shown = shown_icons(game, screen)
    check("the info panel shows a block", bool(shown))
    first, second = [i for i in blocks if i["icons"][0] not in shown][:2]

    game.drag(screen, "blocks", first["slot"], "staging", 0)
    game.drag(screen, "blocks", second["slot"], "staging", 1)
    check(
        "blocks staged",
        [icon(game, screen, "staging", n) for n in (0, 1)]
        == [first["icons"][0], second["icons"][0]],
        str(game.staging_names(screen)),
    )
    follows(game, screen, "staging", 0, 1, "staging area")

    for slot in (0, 1):
        game.grid_click(screen, "staging", slot, "click", button="right")
    check("right click still removes staged items", not game.staging_names(screen))

    # The group editor takes the place of the staging area. A block of the block grid
    # adds its whole variant group, so the second block goes after the first one's.
    screen = game.open_edit_mode()
    game.name_dialog(screen, "NewGroupButton", f"Info test {int(time.time())}")
    game.drag(screen, "blocks", first["slot"], "group", 0)
    last = len(game.group_items(screen))
    game.drag(screen, "blocks", second["slot"], "group", last)
    check(
        "blocks added to the group",
        [icon(game, screen, "group", n) for n in (0, last)]
        == [first["icons"][0], second["icons"][0]],
        str(game.group_items(screen)),
    )
    follows(game, screen, "group", 0, last, "group editor")
    game.close_screen(TOOLBAR_CONFIG)


def main() -> int:
    attach = "--attach" in sys.argv
    keep = "--keep" in sys.argv
    client = rig.client()
    api = rig.connect(client) if attach else rig.start(client)
    game = Game(api, client)
    try:
        errors_before = game.exception_count()
        run(game)
        check(
            "no new exceptions in the game log",
            game.exception_count() == errors_before,
            f"{game.exception_count() - errors_before} new",
        )
    except Exception as e:  # noqa: BLE001 -- report and fall through to the summary
        traceback.print_exc()
        check("run completed", False, f"{type(e).__name__}: {e}")
    finally:
        if not attach and not keep:
            rig.stop(client)

    failed = [name for name, ok, _ in results if not ok]
    print(f"\n{len(results) - len(failed)} passed, {len(failed)} failed")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
