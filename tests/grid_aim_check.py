"""In-game check that stepping a block group keeps aiming at the grid in view, on the
isolated client of rig.py.

    TBM_SLOT=15 uv run --with httpx python tests/grid_aim_check.py [--attach] [--keep]

Builds a static large grid in front of the character, faces it with a group of the
light armor blocks in hand, and steps the group with the slot key and the mouse wheel
without looking away. After the steps a left click must place the new block on the
grid, which the cube builder does only while its placement box is aligned to it.
The steps run several in a row, as placing a block would aim the builder anew.
Screenshots go to the client's appdata. Prints PASS or FAIL per check and exits non-zero
on a failure. --attach and --keep work as in item_groups_check.py.
"""

from __future__ import annotations

import sys
import time

import rig
from block_variants_check import SLOT, items
from item_groups_check import TOOLBAR_CONFIG, Game, check, held_check, results

DISTANCE = 12.0


def build_group(game: Game) -> list[str]:
    screen = game.open_edit_mode()
    game.name_dialog(screen, "NewGroupButton", "Armor")
    game.select_category(screen, "Armor Blocks")
    game.drag_to_group(screen, "Light Armor Block", 0)
    game.click(screen, "DoneGroupButton")
    game.select_category(screen, "Groups")
    game.drag_group_to_toolbar(screen, "Armor", SLOT)
    game.close_screen(TOOLBAR_CONFIG)
    return items(game)


def block_count(game: Game, grid_id: int) -> int:
    return game.api.list_cubes(grid_id, 0, 1).get("total", 0)


def click(game: Game) -> None:
    # Keeps the wheel counter: resetting it would be a wheel turn back
    game.api.set_input_state(mouse_left=True, scroll=game.wheel)
    time.sleep(0.3)
    game.api.set_input_state(scroll=game.wheel)
    time.sleep(1.0)


def run(game: Game) -> None:
    api = game.api
    rig.focus_gameplay(api)
    for slot in range(9):
        api.set_toolbar_slot(slot, None)
    api.set_admin_flag("creativeTools", True)
    errors_before = game.exception_count()
    group = build_group(game)

    state = api.get_character()
    position, forward, up = state["position"], state["forward"], state["up"]
    right = [
        forward[1] * up[2] - forward[2] * up[1],
        forward[2] * up[0] - forward[0] * up[2],
        forward[0] * up[1] - forward[1] * up[0],
    ]

    def face_new_grid(side: float) -> int | None:
        """A static grid ahead, faced before the steps, which then happen without turning"""
        before = {g["entityId"] for g in api.list_grids()}
        target = tuple(
            p + DISTANCE * f + side * r for p, f, r in zip(position, forward, right)
        )
        api.character_build_grid("LargeBlockArmorBlock", target)
        time.sleep(2)
        new = [g["entityId"] for g in api.list_grids() if g["entityId"] not in before]
        api.character_look_at(*target, tolerance=1)
        time.sleep(1)
        return new[0] if new else None

    # Steps in a row without looking away or placing in between, then one click
    def steps(what: str, step, members: range, side: float) -> None:
        game.holster()
        grid = face_new_grid(side)
        if not check(f"{what}: static grid built ahead", grid is not None):
            return
        game.press_slot(SLOT)
        for member in members:
            step()
            expected = group[member % len(group)].removeprefix("MyObjectBuilder_")
            held_check(game, f"{what} {member}: block", expected)
        game.shot(f"grid-aim-{what.replace(' ', '-')}")
        before = block_count(game, grid)
        click(game)
        after = block_count(game, grid)
        check(
            f"{what}: click places the block on the grid",
            after == before + 1,
            f"{before} -> {after}",
        )

    # The first press takes the group back to where it was, the steps continue from there
    steps("wheel", lambda: game.scroll(120), range(1, 3), -5.0)
    steps("wheel again", lambda: game.scroll(120), range(3, 5), 5.0)
    game.clear_scroll()

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
