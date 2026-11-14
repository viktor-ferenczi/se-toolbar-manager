"""In-game check of the fitting blocks keys and the quick selector on the game's own
block variants, on the isolated client of rig.py.

    TBM_SLOT=16 uv run --with httpx python tests/game_groups_check.py [--attach] [--keep]

A heavy armor block from an ordinary toolbar slot is in hand, which is in no group of
the player. The game's block variant group of heavy armor has six blocks per size. With
the grids of fit_check.py: the next fitting block button, the quick Shift press and
Shift with the wheel must select the variant that fits the aimed gap, and a left click
must place it turned to fit. Shift+Ctrl must open the quick selector with the six
variants, the fitting ones marked, and a click must select the clicked variant. The
game's own handling must stay: the plain wheel steps through all variants and a second
press of the slot switches the block's size. A block without variants (the gyroscope)
gets neither, and a group of the player still works with the same keys.

TBM_GAME_GROUPS_OFF=1 starts the client with the "Game block groups" option off and
checks that the keys then leave the game's block alone, while the player's group works.
Prints PASS or FAIL per check and exits non-zero on a failure.
--attach uses a client this script started before with --keep, which leaves it running.
"""

from __future__ import annotations

import os
import sys
import time

import fit_check
import quick_selector_check
import rig
from fit_check import (
    ARMOR,
    BACKWARD,
    DOWN,
    FORWARD,
    GROUP_SLOTS,
    GROUPS,
    LEFT,
    PLAIN_SLOT,
    RIGHT,
    Case,
    Fit,
    cube,
)
from item_groups_check import Game, check, results
from quick_selector_check import SCREEN, Selector

OFF = os.environ.get("TBM_GAME_GROUPS_OFF") == "1"

# The large blocks of the game's ArmorHeavyCubeGroup, in the group's order
HEAVY = [
    "LargeHeavyBlockArmorBlock",
    "LargeHeavyBlockArmorSlope",
    "LargeHeavyBlockArmorCorner",
    "LargeHeavyBlockArmorCornerInv",
    "LargeBlockHeavyArmorCornerSquare",
    "LargeBlockHeavyArmorCornerSquareInverted",
]
# One gyroscope per grid size, so it has no variants to step through
NO_VARIANTS = "LargeBlockGyro"

CUBE_GAP = [cube(DOWN), cube(LEFT), cube(RIGHT), cube(FORWARD), cube(BACKWARD)]
CASES = [
    Case(
        "game cube gap",
        "Heavy",
        "LargeHeavyBlockArmorSlope",
        CUBE_GAP,
        DOWN,
        ["LargeHeavyBlockArmorBlock", "LargeHeavyBlockArmorBlock"],
    ),
    Case(
        "game slope gap",
        "Heavy",
        "LargeHeavyBlockArmorBlock",
        [
            cube(DOWN),
            cube(FORWARD),
            cube(LEFT, "LargeBlockArmorSlope"),
            cube(RIGHT, "LargeBlockArmorSlope"),
        ],
        FORWARD,
        ["LargeHeavyBlockArmorSlope", "LargeHeavyBlockArmorSlope"],
        "Forward",
        "Up",
    ),
    Case(
        "game corner gap",
        "Heavy",
        "LargeHeavyBlockArmorBlock",
        [
            cube(DOWN),
            cube(RIGHT, "LargeBlockArmorSlope"),
            cube(FORWARD, "LargeBlockArmorSlope", forward="Right"),
        ],
        RIGHT,
        # Of the six variants the square corner ranks best where the slopes meet. The
        # player's Armor group of fit_check.py has no such block, there it is the corner.
        ["LargeBlockHeavyArmorCornerSquare"],
        "Right",
        "Up",
        (0, -0.6, 0),
    ),
]


def use_game_group() -> None:
    """Lets the helpers of fit_check.py hold and name the variants like a group's
    blocks: the slot of the heavy armor block and the plain wheel reach all of them"""
    GROUPS["Heavy"] = ["CubeBlock/" + name for name in HEAVY]
    GROUP_SLOTS["Heavy"] = PLAIN_SLOT


def run_game_keys(fit: Fit) -> None:
    """The game's own keys with a variant in hand, as without the plugin"""
    game = fit.game
    fit_check.reset_wheel(game)
    game.press_slot(PLAIN_SLOT)
    seen = [fit.held()]
    for _ in HEAVY:
        game.scroll(120)
        seen.append(fit.held())
    check(
        "game keys: the plain wheel steps through all variants and around",
        set(seen) == set(HEAVY) and seen[0] == seen[-1] == HEAVY[0],
        str(seen),
    )
    fit_check.reset_wheel(game)
    game.press_slot(PLAIN_SLOT)
    game.press_slot(PLAIN_SLOT)
    small = fit.held()
    game.press_slot(PLAIN_SLOT)
    check(
        "game keys: pressing the slot again switches the size and back",
        [small, fit.held()] == ["SmallHeavyBlockArmorBlock", HEAVY[0]],
        f"{small}, {fit.held()}",
    )


def run_quick(fit: Fit) -> None:
    """Shift alone selects the fitting variant and keeps it, Shift with the wheel
    stays on the only one that fits"""
    case = CASES[0]
    fit.paste(case.blocks)
    if not check("game quick: holds the slope", fit_check.hold(fit, case)):
        return
    fit.aim(case.aim)
    fit.quick()
    first = fit.held()
    fit.quick()
    second = fit.held()
    fit.shift_scroll(120)
    third = fit.held()
    check(
        "game quick: Shift selects the cube, keeps it, and so does Shift+wheel",
        [first, second, third] == [HEAVY[0]] * 3,
        f"{first}, {second}, {third}",
    )


def run_shot(fit: Fit, sel: Selector) -> None:
    """The selector as it opens, before any click, for the reviewer"""
    case = CASES[0]
    fit.paste(case.blocks)
    if not fit_check.hold(fit, case):
        return
    fit.aim(case.aim)
    fit.button()
    if check("game selector: opens with the slope in hand", sel.open()):
        check(
            "game selector: shows the six variants, the cube marked",
            len(sel.grid()["properties"]["items"]) == len(HEAVY)
            and sel.fitting() == {0},
            str(sel.fitting()),
        )
        sel.game.shot("game-groups-selector")
    sel.release()


def run_off(fit: Fit, sel: Selector) -> None:
    """With the option off the keys leave the game's block alone"""
    case = CASES[0]
    fit.paste(case.blocks)
    if not check("off: holds the slope", fit_check.hold(fit, case), str(fit.held())):
        return
    fit.aim(case.aim)
    fit.button()
    held = fit.held()
    fit.quick()
    fit.button(mouse_xbutton2=True)
    check("off: Shift and the button keep the block", fit.held() == held, fit.held())
    sel.keys(quick_selector_check.KEYS)
    time.sleep(1.5)
    check("off: Shift+Ctrl opens nothing", sel.game.screen_index(SCREEN) is None)
    sel.keys([])
    time.sleep(0.5)


def run_no_variants(fit: Fit, sel: Selector) -> None:
    """A block without variants in hand: nothing to select from"""
    game = fit.game
    fit.paste(CUBE_GAP)
    fit_check.reset_wheel(game)
    fit.api.set_toolbar_slot(PLAIN_SLOT, "MyObjectBuilder_Gyro/" + NO_VARIANTS)
    game.press_slot(PLAIN_SLOT)
    fit.aim(DOWN)
    fit.button()
    fit.no_fit_shown()
    fit.quick()
    fit.button(mouse_xbutton2=True)
    check(
        "no variants: Shift and the button keep the block, silently",
        fit.held() == NO_VARIANTS and not fit.no_fit_shown(),
        str(fit.held()),
    )
    sel.keys(quick_selector_check.KEYS)
    time.sleep(1.5)
    check(
        "no variants: Shift+Ctrl opens nothing",
        game.screen_index(SCREEN) is None and fit.held() == NO_VARIANTS,
        f"holds {fit.held()}",
    )
    sel.keys([])
    time.sleep(0.5)
    fit_check.reset_wheel(game)
    fit.api.set_toolbar_slot(PLAIN_SLOT, ARMOR + fit_check.PLAIN_BLOCK)


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
        use_game_group()
        fit = Fit(game)
        sel = Selector(fit)

        run_game_keys(fit)
        if OFF:
            run_off(fit, sel)
        else:
            for case in CASES:
                try:
                    fit_check.run_case(fit, case)
                except Exception as e:  # noqa: BLE001 -- go on with the next case
                    check(f"{case.name}: completed", False, f"{type(e).__name__}: {e}")
            run_quick(fit)
            for case in CASES:
                try:
                    quick_selector_check.run_case(sel, case)
                except Exception as e:  # noqa: BLE001 -- go on with the next case
                    check(f"{case.name}: completed", False, f"{type(e).__name__}: {e}")
                    sel.release()
            run_shot(fit, sel)
            run_no_variants(fit, sel)

        # A group of the player, with the same keys
        own = next(c for c in fit_check.CASES if c.name == "conveyor open end")
        fit_check.run_case(fit, own)
        quick_selector_check.run_case(sel, own)
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
