"""Verify that tool cycling uses tile order, including lower levels first.

TBM_SLOT=15 uv run --with httpx python tests/tool_order_check.py
"""

import rig
from item_groups_check import Game, TOOLBAR_CONFIG


def expect_held(game: Game, expected: str, name: str) -> None:
    actual = game.held()
    assert actual == "MyObjectBuilder_" + expected, f"{name}: {actual}"
    print(f"PASS {name}: {actual}", flush=True)


def run() -> None:
    client = rig.client()
    try:
        game = Game(rig.start(client), client)
        for slot in range(9):
            game.api.set_toolbar_slot(slot, None)
        for kind in ("Welder", "AngleGrinder", "HandDrill"):
            for tier in ("2", "4"):
                game.give(f"{kind}{tier}Item")

        screen = game.open_edit_mode()
        game.name_dialog(screen, "NewGroupButton", "Tile order")
        game.select_category(screen, "Character Tools")
        for name in (
            "Elite Welder",
            "Enhanced Welder",
            "Elite Grinder",
            "Enhanced Grinder",
            "Elite Hand Drill",
            "Enhanced Hand Drill",
        ):
            game.drag_to_group(screen, name, len(game.group_items(screen)))
        for slot in (1, 3, 5):
            game.move_in_group(screen, slot, slot - 1)
        assert game.group_items(screen) == [
            "Enhanced Welder",
            "Elite Welder",
            "Enhanced Grinder",
            "Elite Grinder",
            "Enhanced Hand Drill",
            "Elite Hand Drill",
        ]
        print("PASS lower levels moved before higher levels in tile order", flush=True)
        game.click(screen, "DoneGroupButton")
        game.select_category(screen, "Groups")
        game.drag_group_to_toolbar(screen, "Tile order", 3)
        game.close_screen(TOOLBAR_CONFIG)

        welder = "Welder/Welder2"
        grinder = "AngleGrinder/AngleGrinder2"
        drill = "HandDrill/HandDrill2"
        game.press_slot(3)
        expect_held(game, welder, "initial selection uses the first tile")
        for expected in (grinder, drill, welder):
            game.press_slot(3)
            expect_held(
                game, expected, "button cycling prefers the earlier lower level"
            )
        for delta, sequence in (
            (120, (grinder, drill, welder)),
            (-120, (drill, grinder, welder)),
        ):
            for expected in sequence:
                game.scroll(delta)
                expect_held(
                    game, expected, f"wheel {delta} prefers the earlier lower level"
                )
        game.clear_scroll()

        screen = game.open_edit_mode()
        game.move_in_group(screen, 1, 0)
        assert game.group_items(screen)[:2] == ["Elite Welder", "Enhanced Welder"]
        print("PASS higher level moved before lower level in tile order", flush=True)
        game.click(screen, "DoneGroupButton")
        game.close_screen(TOOLBAR_CONFIG)
        for expected in (grinder, drill, "Welder/Welder4"):
            game.press_slot(3)
            expect_held(game, expected, "button cycling follows the changed tile order")
    finally:
        rig.stop(client)


if __name__ == "__main__":
    run()
    print("15 passed", flush=True)
