"""In-game check of the quick selector at the start of a session (SE-692), on the
isolated client of rig.py.

    TBM_SLOT=16 uv run --with httpx python tests/session_start_check.py [--attach] [--keep]

Starts from a config written by 1.8.0, without the QuickSelectorKeys element. With a
block of a group in hand aimed into a gap, pressing Shift and then Ctrl a tenth of a
second later, as a player does, must open the selector, and so must Ctrl before Shift.
Shift first also does the quick fitting block press, which switches the block in hand.
With a block of no group in hand nothing opens, and the game log says why.
Prints PASS or FAIL per check and exits non-zero on a failure.
"""

from __future__ import annotations

import sys
import time

import fit_check
import rig
from fit_check import CASES, PLAIN_SLOT, Fit
from item_groups_check import Game, check, results, wait
from quick_selector_check import KEYS, SCREEN, Selector

# 1.8.0 had no quick selector, its config has no QuickSelectorKeys element
CONFIG_1_8_0 = """<?xml version="1.0" encoding="utf-8"?>
<Config xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema">
  <EnableStagingArea>true</EnableStagingArea>
  <StagingAreaRowCount>3</StagingAreaRowCount>
  <PreventOverwritingSlots>true</PreventOverwritingSlots>
  <UseCharacterProfilesForBuildCockpit>true</UseCharacterProfilesForBuildCockpit>
  <KeepBlockSearchText>false</KeepBlockSearchText>
  <LatestBlockSearchText />
  <KeepProfileSearchText>false</KeepProfileSearchText>
  <LatestProfileSearchText />
  <BlockSearchKey><Key>OemPipe</Key><Ctrl>false</Ctrl><Alt>false</Alt><Shift>false</Shift></BlockSearchKey>
  <FitScrollModifier>Shift</FitScrollModifier>
  <QuickFit>true</QuickFit>
  <NextFitButton>ExtraButton2</NextFitButton>
  <NextFitKey><Key>None</Key><Ctrl>false</Ctrl><Alt>false</Alt><Shift>false</Shift></NextFitKey>
  <PreviousFitButton>ExtraButton1</PreviousFitButton>
  <PreviousFitKey><Key>None</Key><Ctrl>false</Ctrl><Alt>false</Alt><Shift>false</Shift></PreviousFitKey>
</Config>
"""

# The armor gap of fit_check.py
CASE = next(c for c in CASES if c.name == "cube gap")
NO_GROUP = "the quick selector keys found no group in use"


def write_configs(c: rig.Client, write=rig.write_configs) -> None:
    write(c)
    c.config.parent.mkdir(parents=True, exist_ok=True)
    c.config.write_text(CONFIG_1_8_0, encoding="utf-8")


def chord(sel: Selector, first: str) -> bool:
    """One key of the chord, then the other a tenth of a second later as a player
    presses them, and whether the selector opens"""
    sel.keys([])
    time.sleep(0.5)
    sel.keys([first])
    time.sleep(0.1)
    sel.keys(KEYS)
    return bool(wait(lambda: sel.game.screen_index(SCREEN) is not None, 3))


def run(game: Game, fit: Fit, sel: Selector) -> None:
    fit.paste(CASE.blocks, CASE.grid)
    for name, first in (
        ("Shift before Ctrl", "LeftShift"),
        ("Ctrl before Shift", "LeftControl"),
    ):
        if not check(
            f"{name}: holds {CASE.held}", fit_check.hold(fit, CASE), str(fit.held())
        ):
            continue
        fit.aim(CASE.aim, CASE.offset, CASE.distance)
        fit.button()
        check(f"{name}: opens the selector", chord(sel, first), f"holds {fit.held()}")
        sel.release()

    # A block of no group: nothing opens and the log tells why
    game.holster()
    game.press_slot(PLAIN_SLOT)
    fit.aim(CASE.aim, CASE.offset, CASE.distance)
    before = len(log_lines(game))
    check(
        "plain block: Shift+Ctrl opens nothing",
        not chord(sel, "LeftShift"),
        f"holds {fit.held()}",
    )
    sel.release()
    reason = wait(lambda: [l for l in log_lines(game)[before:] if NO_GROUP in l], 5)
    check(
        "plain block: the log tells why",
        bool(reason)
        and "Armor: holds nothing" in reason[0]
        and "used last" in reason[0],
        reason[0].split(NO_GROUP)[-1][:300] if reason else "no log line",
    )


def log_lines(game: Game) -> list[str]:
    return game.client.log.read_text(encoding="utf-8", errors="replace").splitlines()


def main() -> int:
    attach = "--attach" in sys.argv
    keep = attach or "--keep" in sys.argv
    client = rig.client()
    rig.write_configs = write_configs
    api = rig.connect(client) if attach else fit_check.start(client)
    game = Game(api, client)
    try:
        errors_before = game.exception_count()
        api.clear_input_state()
        time.sleep(2)
        fit_check.setup_toolbar(game)
        fit = Fit(game)
        run(game, fit, Selector(fit))
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
