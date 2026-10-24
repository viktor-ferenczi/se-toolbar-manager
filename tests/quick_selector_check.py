"""In-game check of the quick selector, on the isolated client of rig.py.

    TBM_SLOT=16 uv run --with httpx python tests/quick_selector_check.py [--attach] [--keep]

Uses the grids and groups of fit_check.py. With a block of a group in hand aimed into a
gap, Shift+Ctrl held must open the selector. The tiles marked as fitting must be the
blocks the next fitting block button steps through, which is the SE-652 matcher in game.
Real mouse clicks with the keys still held must equip the clicked block, fitting or not,
mark it selected and keep the selector open. Releasing a key must close it without having
turned the camera. With a tool of a mixed group in hand nothing is marked and a click
equips a block, and without a group in use the keys do nothing.
Prints PASS or FAIL per check and exits non-zero on a failure.
--attach uses a client this script started before with --keep, which leaves it running.
"""

from __future__ import annotations

import sys
import time

import fit_check
import rig
from fit_check import CASES, GROUP_SLOTS, GROUPS, PLAIN_BLOCK, PLAIN_SLOT, Fit
from item_groups_check import Game, check, results, wait

SCREEN = "QuickSelector"
FITS = "Fits the aimed position"
KEYS = ["LeftShift", "LeftControl"]

# The selector's test needs no size switching, the mixed group takes a slot of those
del GROUPS["SizeSwitch"], GROUPS["LargeOnly"]
del GROUP_SLOTS["SizeSwitch"], GROUP_SLOTS["LargeOnly"]
GROUPS["Mixed"] = [
    "PhysicalGunObject/WelderItem",
    "CubeBlock/LargeBlockArmorBlock",
    "CubeBlock/LargeBlockArmorSlope",
]
GROUP_SLOTS["Mixed"] = 7

CHECKED = [
    "cube gap",
    "slope gap",
    "conveyor open end",
    "tube middle",
    "upgrade module",
]


class Selector:
    def __init__(self, fit: Fit):
        self.fit = fit
        self.game = fit.game
        self.api = fit.api

    def keys(self, keys, left=False) -> None:
        # Without keys set_input_state would leave the old ones held
        mouse = {"scroll": self.game.wheel, "left": left}
        self.api._post("/v1/input/state", {"keyboard": {"keys": keys}, "mouse": mouse})

    def move(self, x: int, y: int) -> None:
        """The cursor to the pixel, the held keys stay. The x and y of the input
        state would be mouse movement, not a position."""
        self.api.mouse_move(x, y)
        time.sleep(0.1)

    def open(self) -> bool:
        # After character_look_at the game's wheel counter is back at 0. Setting it again
        # with Ctrl held would be Ctrl+wheel, which turns the toolbar page.
        self.keys([])
        time.sleep(0.5)
        self.keys(KEYS)
        return bool(wait(lambda: self.game.screen_index(SCREEN) is not None, 3))

    def release(self) -> bool:
        self.keys([])
        return bool(wait(lambda: self.game.screen_index(SCREEN) is None, 3))

    def grid(self) -> dict:
        screen = self.game.screen_index(SCREEN)
        return next(
            c
            for c in self.game.controls(screen)
            if c["type"] == "MyGuiControlGrid" and c.get("name") == "QuickSelectorGrid"
        )

    def fitting(self) -> set[int]:
        return {
            item["slot"]
            for item in self.grid()["properties"]["items"]
            if any(FITS in t for t in item.get("tooltips") or [])
        }

    def selected(self) -> int | None:
        return self.grid()["properties"].get("selectedIndex")

    def over_grid(self, x: int, y: int) -> bool:
        self.move(x, y)
        return any(c.get("name") == "QuickSelectorGrid" for c in self.api.get_cursor())

    def edge(
        self, inside: tuple[int, int], outside: tuple[int, int]
    ) -> tuple[int, int]:
        """The last pixel over the grid on the line from inside to outside"""
        while max(abs(a - b) for a, b in zip(inside, outside)) > 1:
            middle = tuple((a + b) // 2 for a, b in zip(inside, outside))
            if self.over_grid(*middle):
                inside = middle
            else:
                outside = middle
        return inside

    def bounds(self) -> tuple[int, int, int, int]:
        """The grid's pixels, found with the cursor"""
        cx, cy = 640, 360
        if not self.over_grid(cx, cy):
            raise AssertionError("The grid is not in the middle of the screen")
        left = self.edge((cx, cy), (0, cy))[0]
        right = self.edge((cx, cy), (1279, cy))[0]
        top = self.edge((cx, cy), (cx, 0))[1]
        bottom = self.edge((cx, cy), (cx, 719))[1]
        return left, top, right, bottom

    def click(self, slot: int) -> None:
        """A real left click in the middle of the tile with the keys held"""
        props = self.grid()["properties"]
        left, top, right, bottom = self.bounds()
        column, row = slot % props["columns"], slot // props["columns"]
        x = round(left + (right - left) * (column + 0.5) / props["columns"])
        y = round(top + (bottom - top) * (row + 0.5) / props["rows"])
        self.move(x, y)
        time.sleep(0.2)
        self.keys(KEYS, left=True)
        time.sleep(0.3)
        self.keys(KEYS)
        time.sleep(1.0)


def matcher_set(fit: Fit, case) -> set[str]:
    """The blocks the next fitting block button steps through, until one repeats,
    none if it tells that nothing fits"""
    seen = []
    for _ in range(len(GROUPS[case.group]) + 1):
        fit.button(mouse_xbutton2=True)
        if fit.no_fit_shown():
            return set()
        held = fit.held()
        if held in seen:
            break
        seen.append(held)
    return set(seen)


def run_case(sel: Selector, case) -> None:
    fit = sel.fit
    fit.paste(case.blocks, case.grid)
    if not check(f"{case.name}: holds {case.held}", fit_check.hold(fit, case)):
        return
    fit.aim(case.aim, case.offset, case.distance)
    # After character_look_at the next wheel setting is a turn, which steps the group
    fit.button()
    fit.no_fit_shown()
    expected = matcher_set(fit, case)
    names = [i.split("/")[-1] for i in GROUPS[case.group]]
    held = fit.held()
    forward = fit.api.get_character()["forward"]

    if not check(f"{case.name}: Shift+Ctrl opens the selector", sel.open()):
        sel.release()
        return
    marked = {names[s] for s in sel.fitting()}
    check(
        f"{case.name}: fit marks match the matcher",
        marked == expected,
        f"marked {sorted(marked)}, matcher {sorted(expected)}",
    )
    check(
        f"{case.name}: the block in hand is selected",
        sel.selected() == names.index(held),
        f"selected {sel.selected()}, holds {held}",
    )

    # Non-fitting first, so a non-fitting block is shown to stay selectable
    order = sorted(
        (s for s in range(len(names)) if names[s] != held),
        key=lambda s: names[s] in expected,
    )
    for slot in order[:2]:
        sel.click(slot)
        check(
            f"{case.name}: click selects {names[slot]}"
            + (" (fits)" if names[slot] in expected else " (doesn't fit)"),
            fit.held() == names[slot] and sel.selected() == slot,
            f"holds {fit.held()}, selected {sel.selected()}",
        )
        check(
            f"{case.name}: stays open after the click",
            sel.game.screen_index(SCREEN) is not None,
        )
    game_shot(sel, case.name)

    check(f"{case.name}: releasing the keys closes it", sel.release())
    after = fit.api.get_character()["forward"]
    turned = max(abs(a - b) for a, b in zip(forward, after))
    check(f"{case.name}: the camera didn't turn", turned < 0.02, f"{turned:.3f}")


def game_shot(sel: Selector, name: str) -> None:
    sel.game.shot("quick-" + name.replace(" ", "-"))


def run_mixed(sel: Selector) -> None:
    """A tool of a mixed group in hand: no fit marks, a click equips a block"""
    fit, game = sel.fit, sel.game
    game.give("WelderItem")
    game.holster()
    game.press_slot(GROUP_SLOTS["Mixed"])
    if not check("mixed: holds the welder", fit.held() == "Welder", str(fit.held())):
        return
    if not check("mixed: Shift+Ctrl opens the selector", sel.open()):
        sel.release()
        return
    check("mixed: nothing marked with a tool in hand", not sel.fitting())
    check("mixed: the welder is selected", sel.selected() == 0, str(sel.selected()))
    sel.click(2)
    check(
        "mixed: click selects the slope",
        fit.held() == "LargeBlockArmorSlope" and sel.selected() == 2,
        f"holds {fit.held()}, selected {sel.selected()}",
    )
    sel.click(0)
    check(
        "mixed: click selects the welder again",
        fit.held() == "Welder" and sel.selected() == 0,
        f"holds {fit.held()}, selected {sel.selected()}",
    )
    game_shot(sel, "mixed")
    check("mixed: releasing the keys closes it", sel.release())


def run_no_group(sel: Selector) -> None:
    """Without a group in use the keys do nothing"""
    fit, game = sel.fit, sel.game
    fit.paste(CASES[0].blocks)
    game.holster()
    game.press_slot(PLAIN_SLOT)
    fit.aim(CASES[0].aim)
    sel.keys(KEYS)
    time.sleep(1.5)
    check(
        "plain block: Shift+Ctrl opens nothing",
        game.screen_index(SCREEN) is None and fit.held() == PLAIN_BLOCK,
        f"holds {fit.held()}",
    )
    sel.keys([])
    game.holster()
    sel.keys(KEYS)
    time.sleep(1.5)
    check(
        "empty hand: Shift+Ctrl opens nothing",
        game.screen_index(SCREEN) is None and fit.held() is None,
        f"holds {fit.held()}",
    )
    sel.keys([])
    time.sleep(0.5)


def main() -> int:
    attach = "--attach" in sys.argv
    keep = attach or "--keep" in sys.argv
    client = rig.client()
    api = rig.connect(client) if attach else fit_check.start(client)
    game = Game(api, client)
    try:
        errors_before = game.exception_count()
        api.clear_input_state()
        time.sleep(2)
        fit_check.setup_toolbar(game)
        fit = Fit(game)
        sel = Selector(fit)
        for case in CASES:
            if case.name not in CHECKED:
                continue
            try:
                run_case(sel, case)
            except Exception as e:  # noqa: BLE001 -- go on with the next case
                check(f"{case.name}: completed", False, f"{type(e).__name__}: {e}")
                sel.release()
        run_mixed(sel)
        run_no_group(sel)
        fit.clear()
        check(
            "no new exceptions in the game log",
            game.exception_count() == errors_before,
            f"{game.exception_count() - errors_before} new",
        )
    except Exception as e:  # noqa: BLE001 -- report and fall through to the summary
        check("run completed", False, f"{type(e).__name__}: {e}")
    finally:
        if not keep:
            rig.stop(client)

    failed = [name for name, ok, _ in results if not ok]
    print(f"\n{len(results) - len(failed)} passed, {len(failed)} failed")
    for name in failed:
        print(f"  FAIL {name}")
    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
