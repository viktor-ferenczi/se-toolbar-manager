"""In-game checks of a group holding both sizes of a block, on the isolated client of rig.py.

    TBM_SLOT=15 uv run --with httpx python tests/same_pair_check.py [--attach] [--keep]

The large Hinge and the small grid's Hinge 3x3 are the two sizes of one game block pair, and
the group has both, next to the small Hinge. It also has the two large blocks of the mod in
tests/same_pair_mod, which share a BlockPairName the way the blocks of some mods do. The
wheel has to step through all of them and on to the blocks after them, also after reloading
the world, and the slot has to keep switching the size. Prints PASS or FAIL per check and
exits non-zero on a failure. --attach expects a client started by this
check with --keep, since the group is written before the game starts.
"""

from __future__ import annotations

import shutil
import sys

import rig
from item_groups_check import (
    TOOLBAR_CONFIG,
    Game,
    check,
    held_check,
    results,
)

GROUP = [
    "CubeBlock/LargeBlockArmorBlock",
    "MotorAdvancedStator/LargeHinge",
    "MotorAdvancedStator/MediumHinge",
    "MotorAdvancedStator/SmallHinge",
    "CubeBlock/TbmTwinA",
    "CubeBlock/TbmTwinB",
    "CubeBlock/LargeBlockArmorSlope",
]
SLOT = 0
MOD = "TbmSamePair"
MOD_ITEM = (
    f'<Mods><ModItem FriendlyName="{MOD}"><Name>{MOD}</Name>'
    "<PublishedFileId>0</PublishedFileId></ModItem></Mods>"
)


def start(client: rig.Client):
    """Like rig.start, with the group saved before the game loads its groups"""
    if client.groups.exists():
        shutil.rmtree(client.groups)
    client.groups.mkdir(parents=True)
    items = "".join(f"<string>MyObjectBuilder_{item}</string>" for item in GROUP)
    (client.groups / "Hinges.xml").write_text(
        f"<ItemGroup><Id>hinges</Id><Name>Hinges</Name><Order>0</Order>"
        f"<Items>{items}</Items></ItemGroup>",
        encoding="utf-8",
    )
    rig.launch(client)
    api = rig.wait_api(client)
    mod = client.appdata / "Mods" / MOD
    if mod.exists():
        shutil.rmtree(mod)
    shutil.copytree(rig.REPO / "tests" / "same_pair_mod", mod)
    # Creative: in survival the game enables a small block only with it in the inventory
    world = rig.prepare_world(client.world)
    for name in ("Sandbox.sbc", "Sandbox_config.sbc"):
        path = world / name
        text = path.read_text(encoding="utf-8")
        path.write_text(
            text.replace(
                "<GameMode>Survival</GameMode>", "<GameMode>Creative</GameMode>"
            ).replace("<Mods />", MOD_ITEM),
            encoding="utf-8",
        )
    rig.load_world(api, world)
    return api


def cycle(game: Game, what: str) -> None:
    """A full turn of the wheel through the group, in both directions, from its first block"""
    count = len(GROUP)
    for step in [*range(1, count), 0]:
        game.scroll(120)
        held_check(game, f"{what}, wheel up: block {step + 1}", GROUP[step])
    for step in range(count - 1, -1, -1):
        game.scroll(-120)
        held_check(game, f"{what}, wheel down: block {step + 1}", GROUP[step])
    game.clear_scroll()


def place_group(game: Game) -> None:
    rig.focus_gameplay(game.api)
    for slot in range(9):
        game.api.set_toolbar_slot(slot, None)
    screen = game.open_toolbar_config()
    game.select_category(screen, "Groups")
    game.drag_group_to_toolbar(screen, "Hinges", SLOT)
    game.close_screen(TOOLBAR_CONFIG)
    game.holster()


def run(game: Game) -> None:
    errors_before = game.exception_count()
    place_group(game)
    game.press_slot(SLOT)
    held_check(game, "slot press: the first block", GROUP[0])
    cycle(game, "first turn")

    # On the small hinge of the pair: the slot switches its size like the game does
    game.scroll(120)
    game.scroll(120)
    held_check(game, "wheel to the small block of the pair", GROUP[2])
    game.press_slot(SLOT)
    held_check(game, "pressed again: the large block of the pair", GROUP[1])
    game.press_slot(SLOT)
    held_check(game, "pressed again: the small one", GROUP[2])
    game.scroll(120)
    held_check(game, "wheel up from there: the next block", GROUP[3])
    game.clear_scroll()

    # The group's positions outlive the world, so a jam would too
    rig.load_world(game.api, game.client.world)
    place_group(game)
    game.press_slot(SLOT)
    held_check(game, "after reloading the world: the block it was on", GROUP[3])
    game.scroll(-120)
    game.scroll(-120)
    game.scroll(-120)
    held_check(game, "wheel down to the first block", GROUP[0])
    cycle(game, "after reloading the world")

    check(
        "no exceptions in the game log",
        game.exception_count() == errors_before,
        f"{game.exception_count() - errors_before} new",
    )


def main() -> int:
    attach = "--attach" in sys.argv
    keep = "--keep" in sys.argv
    client = rig.client()
    api = rig.connect(client) if attach else start(client)
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
