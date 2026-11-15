"""Scroll offset of the group editor (TG) when blocks are removed from a group.

    TBM_SLOT=15 uv run --with httpx python tests/delete_scroll_check.py [--attach] [--keep]

Builds a group long enough to scroll from the armor blocks, scrolls the
editor's panel with the mouse wheel and removes blocks by right click: at the top, middle
and bottom of the visible rows, off a row boundary, and at the end of the list. Each
removal must keep the scroll offset. Removing the last block until none is left in view
must move the view up by exactly one row, at that removal only. Prints the offset before
and after each removal, PASS or FAIL per check, and exits non-zero on a failure.
--attach and --keep work as in item_groups_check.py.
"""

from __future__ import annotations

import sys
import time

import rig
from item_groups_check import Game, check, results

GROUP = "Scroll test"
EPS = 1e-4


class Panel:
    """The group editor's scrollable panel, measured through Remote's control tree"""

    def __init__(self, game: Game, screen: int):
        self.game = game
        self.api = game.api
        self.screen = screen
        self.base = 0.0  # panel top minus grid top at offset 0
        self.step = 0.0  # height of one row of the grid

    def nodes(self) -> tuple[dict, dict]:
        """The panel and its grid"""
        tree = self.api.get_controls(self.screen)

        def walk(nodes, parent):
            for node in nodes or []:
                if node.get("name") == "GroupItemsGrid":
                    return parent, node
                found = walk(node.get("children"), node) or walk(
                    node.get("controls"), node
                )
                if found:
                    return found
            return None

        found = walk(tree.get("controls") if isinstance(tree, dict) else tree, None)
        if not found or found[0] is None:
            raise AssertionError("No group editor panel")
        return found

    def offset(self) -> float:
        panel, grid = self.nodes()
        return panel["topLeft"]["y"] - grid["topLeft"]["y"] - self.base

    def rows(self, offset: float) -> float:
        return offset / self.step

    def wheel(self, notches: int) -> None:
        """Turns the wheel over the panel, positive notches scroll down"""
        panel, _ = self.nodes()
        x = panel["topLeft"]["x"] + panel["size"]["x"] / 2
        y = panel["topLeft"]["y"] + panel["size"]["y"] / 2
        # 1280x720 has a 960 pixel wide 4:3 GUI area in the middle. Remote's cursor
        # report can't confirm it: the block grid's unclipped rectangle covers the panel.
        # The game skips the wheel's turn back to zero on the next frame in menus.
        for _ in range(abs(notches)):
            self.api.scroll(
                -120 if notches > 0 else 120, round(160 + 960 * x), round(720 * y)
            )
            time.sleep(1)
        self.game.clear_scroll()

    def to_end(self) -> None:
        for _ in range(40):
            before = self.offset()
            self.wheel(4)
            if abs(self.offset() - before) < EPS:
                return


def remove(game: Game, screen: int, panel: Panel, slot: int) -> bool:
    """A right click on the slot's pixel. False when the slot is not in view.
    Remote's grid action refuses any slot of a grid taller than its scrolled panel."""
    node, grid = panel.nodes()
    top = node["topLeft"]["y"]
    y = grid["topLeft"]["y"] + (slot // 10 + 0.5) * panel.step
    if not top + panel.step / 2 <= y <= top + node["size"]["y"] - panel.step / 2:
        return False
    x = grid["topLeft"]["x"] + (slot % 10 + 0.5) * grid["size"]["x"] / 10
    items = game.group_items(screen)
    px, py = round(160 + 960 * x), round(720 * y)
    # A held input state would keep the cursor where it is
    game.api.clear_input_state()
    time.sleep(0.3)
    game.api.click(px, py, button="right")
    time.sleep(1.5)
    after = game.group_items(screen)
    if after != items[:slot] + items[slot + 1 :]:
        raise AssertionError(
            f"Right click on slot {slot} at {px},{py} did not remove just that block:"
            f" {len(items)} -> {len(after)}"
        )
    return True


def remove_case(game: Game, screen: int, panel: Panel, name: str, where: str) -> None:
    """Removes a block of the first or last row in view and checks the offset is kept"""
    before = panel.offset()
    count = len(game.group_items(screen))
    first = int(panel.rows(before))  # the first row in view, or partly in view
    rows = [r for r in range(first, first + 3) if r * 10 < count]
    slots = {
        "top": [r * 10 for r in rows],
        "middle": [r * 10 + 5 for r in rows if r * 10 + 5 < count],
        "bottom": [min(r * 10 + 3, count - 1) for r in reversed(rows)],
        "end": [count - 1],
    }[where]
    for slot in slots:
        if not remove(game, screen, panel, slot):
            continue
        after = panel.offset()
        print(
            f"  {name}: slot {slot}, offset {before:.4f} -> {after:.4f}"
            f" ({panel.rows(before):.2f} -> {panel.rows(after):.2f} rows)",
            flush=True,
        )
        check(
            f"{name}: the removal keeps the scroll offset",
            len(game.group_items(screen)) == count - 1 and abs(after - before) < EPS,
            f"{before:.4f} -> {after:.4f}",
        )
        return
    check(f"{name}: a block in view to remove", False, str(slots))


def build(game: Game, screen: int, panel: Panel) -> None:
    game.name_dialog(screen, "NewGroupButton", GROUP)
    game.select_category(screen, "Armor Blocks")
    sizes = {}
    # A double click adds a block, or the blocks of a game block group one by one
    for name in game.grid_names(screen):
        game.double_click_block(screen, name)
        _, grid = panel.nodes()
        sizes[grid["properties"]["rows"]] = grid["size"]["y"]
        if len(game.group_items(screen)) >= 45:
            break
    # The block grid reaches down behind the group panel and takes right clicks there
    # (SE-1572), a short category leaves nothing behind the panel
    game.select_category(screen, "Character Tools")
    rows = sorted(sizes)
    panel.step = (sizes[rows[-1]] - sizes[rows[0]]) / (rows[-1] - rows[0])
    # Appending keeps the offset, which is still at the top for a new group
    panel.base = panel.offset()
    count = len(game.group_items(screen))
    print(f"  group of {count} blocks, row step {panel.step:.4f}", flush=True)
    check("the group is long enough to scroll", count >= 45, str(count))


def run(game: Game) -> None:
    rig.focus_gameplay(game.api)
    screen = game.open_edit_mode()
    panel = Panel(game, screen)
    build(game, screen, panel)

    panel.wheel(4)
    check("the wheel scrolls the panel", panel.offset() > EPS, f"{panel.offset():.4f}")
    for where in ("top", "middle", "bottom"):
        remove_case(game, screen, panel, f"{where} of the rows in view", where)

    # Off a row boundary, a wheel notch is a quarter page
    panel.wheel(1)
    remove_case(game, screen, panel, "half row offset", "middle")

    panel.to_end()
    remove_case(game, screen, panel, "end of the list", "end")

    # Remove the last block until none is left in view: the view goes up one row, at
    # that removal only
    moved = 0
    for _ in range(25):
        count = len(game.group_items(screen))
        before = panel.offset()
        if not remove(game, screen, panel, count - 1):
            check(f"block {count - 1} at the end is in view", False)
            break
        after = panel.offset()
        last_row_bottom = -(-(count - 1) // 10) * panel.step
        empty = last_row_bottom <= before + EPS
        expected = max(0.0, before - panel.step) if empty else before
        print(
            f"  last block {count - 1}: offset {before:.4f} -> {after:.4f}"
            f" ({panel.rows(before):.2f} -> {panel.rows(after):.2f} rows)"
            + (", no block left in view" if empty else ""),
            flush=True,
        )
        if abs(after - expected) >= EPS:
            check(
                f"removing block {count - 1} at the end",
                False,
                f"{before:.4f} -> {after:.4f}, expected {expected:.4f}",
            )
            break
        if empty:
            moved += 1
            break
    check(
        "an empty view goes up exactly one row, the removals before keep the offset",
        moved == 1,
        f"{moved} moves",
    )
    check("no exceptions in the game log", game.exception_count() == 0)


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
