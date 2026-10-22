"""In-game checks of the tool groups, on the isolated client of rig.py.

    TBM_SLOT=15 python tests/item_groups_check.py [--attach] [--keep]

Starts the client and a fresh survival world whose character has no hand tools,
then drives the G menu, the Tool Groups dialog and the toolbar through Remote with
real key presses. At the end it loads the saved world in the same client without the
plugin. Prints PASS or FAIL per check and exits non-zero on a failure.
--attach uses a client already started with `rig.py start`, --keep leaves it running.
"""

from __future__ import annotations

import os
import sys
import time
import xml.etree.ElementTree as ET

import httpx

import rig

GUN = "MyObjectBuilder_PhysicalGunObject"
MARKER_TYPE = "MyObjectBuilder_ToolbarItemTerminalGroup"
TOOLBAR_CONFIG = "MyGuiScreenCubeBuilder"
DIALOG = "ItemGroupsDialog"

results: list[tuple[str, bool, str]] = []


def check(name: str, ok: bool, detail: str = "") -> bool:
    results.append((name, ok, detail))
    print(
        f"{'PASS' if ok else 'FAIL'} {name}" + (f" ({detail})" if detail else ""),
        flush=True,
    )
    return ok


def wait(condition, timeout: float = 10.0, step: float = 0.25):
    deadline = time.monotonic() + timeout
    while True:
        value = condition()
        if value or time.monotonic() > deadline:
            return value
        time.sleep(step)


class Game:
    def __init__(self, api, client: rig.Client):
        self.api = api
        self.client = client

    # Screens and controls

    def screens(self) -> list[dict]:
        return self.api.list_screens()

    def screen_index(self, kind: str) -> int | None:
        return next((s["index"] for s in self.screens() if s.get("type") == kind), None)

    def wait_screen(self, kind: str, present: bool = True) -> int | None:
        found = wait(lambda: (self.screen_index(kind) is not None) == present, 10)
        if not found:
            raise AssertionError(
                f"Screen {kind} {'did not open' if present else 'stayed open'}"
            )
        return self.screen_index(kind)

    def controls(self, screen: int) -> list[dict]:
        """The control tree of a screen, flattened"""
        flat = []

        def walk(nodes):
            for node in nodes or []:
                flat.append(node)
                walk(node.get("children"))
                walk(node.get("controls"))

        tree = self.api.get_controls(screen)
        walk(tree.get("controls") if isinstance(tree, dict) else tree)
        return flat

    def control(self, screen: int, name: str) -> dict:
        found = [c for c in self.controls(screen) if c.get("name") == name]
        if not found:
            raise AssertionError(f"No control {name} on screen {screen}")
        return found[0]

    def table_rows(self, screen: int, name: str) -> list[str]:
        return [row["cells"][0] for row in self.control(screen, name).get("rows", [])]

    def click(self, screen: int, name: str, row: int | None = None) -> None:
        self.api.control_click(name=name, screen=screen, row=row)
        time.sleep(0.3)

    def shot(self, name: str) -> None:
        """A screenshot for the reviewer, next to the game log"""
        folder = self.client.appdata / "screenshots"
        folder.mkdir(exist_ok=True)
        time.sleep(1)
        (folder / f"{name}.png").write_bytes(self.api.screenshot_bytes())

    # Character and toolbar

    def held(self) -> str | None:
        weapon = self.api.get_character().get("weapon")
        return weapon["definition"] if weapon else None

    def give(self, subtype: str) -> None:
        self.api.add_inventory_item(GUN, subtype)

    def toolbar(self) -> dict[int, dict]:
        return {item["slot"]: item for item in self.api.get_toolbar()["items"]}

    def press_slot(self, slot: int) -> None:
        """Slot 0..8 of the shown toolbar page, with the number key of a player"""
        self.api.key(f"D{slot + 1}")
        time.sleep(1.0)

    def holster(self) -> None:
        """Selecting an empty slot puts the hand item away"""
        empty = next(s for s in range(9) if s not in self.toolbar())
        self.press_slot(empty)
        wait(lambda: self.held() is None, 5)

    # The G menu

    def open_toolbar_config(self) -> int:
        rig.focus_gameplay(self.api)
        self.api.key("G")
        return self.wait_screen(TOOLBAR_CONFIG)

    def close_screen(self, kind: str) -> None:
        index = self.screen_index(kind)
        if index is not None:
            self.api.close_screen(index)
            self.wait_screen(kind, present=False)

    def categories(self, screen: int) -> list[str]:
        items = self.control(screen, "CategorySelector")["properties"]["items"]
        return [item["text"].strip() for item in items]

    def select_category(self, screen: int, name: str) -> None:
        self.api.control_set(
            "CategorySelector", self.categories(screen).index(name), screen=screen
        )
        time.sleep(0.5)

    def grids(self, screen: int) -> dict[str, dict]:
        """The block grid, the staging area and the toolbar of the G menu, by position"""
        found = {}
        for grid in (
            c for c in self.controls(screen) if c["type"] == "MyGuiControlGrid"
        ):
            x, y = grid["position"]["x"], grid["position"]["y"]
            if x < 0.3 and y < 0.5:
                found.setdefault("blocks", grid)
            elif x < 0.3:
                found.setdefault("staging", grid)
            elif (
                grid["properties"].get("rows") == 1
                and grid["properties"].get("columns") == 10
            ):
                found.setdefault("toolbar", grid)
        return found

    @staticmethod
    def names(grid: dict) -> list[str]:
        return [
            (item.get("tooltips") or [""])[0] for item in grid["properties"]["items"]
        ]

    def grid_names(self, screen: int) -> list[str]:
        return self.names(self.grids(screen)["blocks"])

    def staging_names(self, screen: int) -> list[str]:
        return self.names(self.grids(screen)["staging"])

    def drag(
        self, screen: int, source: str, slot: int, target: str, target_slot: int
    ) -> None:
        # Control ids go stale while a screen settles, Remote answers 409 then
        for attempt in range(5):
            grids = self.grids(screen)
            item = next(
                i for i in grids[source]["properties"]["items"] if i["slot"] == slot
            )
            try:
                self.api.grid_action(
                    grids[source]["id"],
                    slot,
                    "drag",
                    item=item["id"],
                    target_control=grids[target]["id"],
                    target_slot=target_slot,
                    screen=screen,
                )
                break
            except httpx.HTTPStatusError as e:
                if e.response.status_code != 409 or attempt == 4:
                    raise RuntimeError(f"Drag refused: {e.response.text}") from e
                time.sleep(1)
        time.sleep(1.5)

    def drag_group_to_toolbar(self, screen: int, name: str, slot: int) -> None:
        self.drag(
            screen, "blocks", self.grid_names(screen).index(name), "toolbar", slot
        )

    def drag_toolbar_to_staging(
        self, screen: int, slot: int, staging_slot: int
    ) -> None:
        self.drag(screen, "toolbar", slot, "staging", staging_slot)

    def drag_staging_to_toolbar(
        self, screen: int, staging_slot: int, slot: int
    ) -> None:
        self.drag(screen, "staging", staging_slot, "toolbar", slot)

    # The Tool Groups dialog, opened from the G menu

    def open_dialog(self) -> int:
        screen = self.screen_index(TOOLBAR_CONFIG)
        if screen is None:
            screen = self.open_toolbar_config()
        self.click(screen, "OpenItemGroupsButton")
        return self.wait_screen(DIALOG)

    def close_dialog(self) -> None:
        self.click(self.screen_index(DIALOG), "CloseButton")
        self.wait_screen(DIALOG, present=False)

    def table(self, screen: int, name: str) -> list[str]:
        rows = self.control(screen, name)["properties"]["rows"]
        return [row["cells"][0] for row in rows]

    def new_group(self, dialog: int, name: str) -> None:
        self.click(dialog, "NewButton")
        names = self.wait_screen("NameDialog")
        self.api.control_set("Textbox", name, screen=names)
        self.api.control_click(text="OK", screen=names)
        self.wait_screen("NameDialog", present=False)

    def add_item(self, dialog: int, name: str) -> None:
        self.click(
            dialog,
            "AvailableTable",
            row=self.table(dialog, "AvailableTable").index(name),
        )
        self.click(dialog, "AddButton")

    def saved_groups(self) -> dict[str, list[str]]:
        """The groups in the plugin's config file, by name"""
        root = ET.parse(self.client.config).getroot()
        return {
            group.findtext("Name"): [item.text for item in group.iter("string")]
            for group in root.iter("ItemGroup")
        }

    def exception_count(self) -> int:
        """Exceptions in the game log, except the debug listener port another client holds
        and LinuxCompat's list of shims, which has a WindowsException"""
        text = self.client.log.read_text(errors="replace")
        return len(
            [
                line
                for line in text.splitlines()
                if "Exception" in line
                and "SocketException" not in line
                and "MyLoadingNeedXMLException" not in line
                and "[LinuxCompat]" not in line
            ]
        )


def main() -> int:
    attach = "--attach" in sys.argv
    keep = "--keep" in sys.argv
    client = rig.client()
    api = rig.connect(client) if attach else rig.start(client)
    game = Game(api, client)
    try:
        run(game)
        if not attach:
            run_without_plugin(client)
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


def held_check(game: Game, name: str, expected: str | None) -> bool:
    held = game.held()
    want = f"MyObjectBuilder_{expected}" if expected else None
    return check(name, held == want, f"holds {held}, expected {want}")


def run(game: Game) -> None:
    api = game.api
    rig.focus_gameplay(api)

    # Slots 0-2 keep the world's basic tools, which the character doesn't have.
    # The group goes to slot 3, the custom group to slot 4, slot 8 stays empty for holstering.
    for slot in range(3, 9):
        api.set_toolbar_slot(slot, None)
    errors_before = game.exception_count()

    # The new tree entry in the G menu
    screen = game.open_toolbar_config()
    categories = game.categories(screen)
    check("G menu lists Tool Groups", "Tool Groups" in categories, str(categories[:6]))
    game.select_category(screen, "Tool Groups")
    check(
        "Tool Groups shows the default group",
        game.grid_names(screen) == ["Hand Tools"],
        str(game.grid_names(screen)),
    )
    game.drag_group_to_toolbar(screen, "Hand Tools", 3)
    slot = game.toolbar().get(3, {})
    check(
        "default group put on the toolbar",
        slot.get("type") == MARKER_TYPE and slot.get("name") == "Hand Tools",
        str(slot),
    )
    game.close_screen(TOOLBAR_CONFIG)

    # The inventory cases with the default group: Elite first, then lower tiers
    game.press_slot(3)
    held_check(game, "none in the inventory: nothing equipped", None)
    check(
        "none in the inventory: no error",
        game.exception_count() == errors_before,
        f"{game.exception_count() - errors_before} new exceptions in the game log",
    )

    game.give("Welder2Item")
    game.press_slot(3)
    held_check(game, "only the lower tier: Enhanced Welder", "Welder/Welder2")

    game.give("Welder4Item")
    game.holster()
    game.press_slot(3)
    held_check(game, "both tiers: Elite Welder", "Welder/Welder4")

    # Every kind of the group, pressing the slot again moves to the next kind
    for subtype in (
        "AngleGrinder2Item",
        "AngleGrinder4Item",
        "HandDrill2Item",
        "HandDrill4Item",
    ):
        game.give(subtype)
    game.press_slot(3)
    held_check(game, "pressed again: Elite Grinder", "AngleGrinder/AngleGrinder4")
    game.press_slot(3)
    held_check(game, "pressed again: Elite Hand Drill", "HandDrill/HandDrill4")
    game.press_slot(3)
    held_check(game, "pressed again: back to the Elite Welder", "Welder/Welder4")

    # A custom group made in the dialog, preferring the lower tier
    dialog = game.open_dialog()
    check(
        "dialog lists the default group",
        game.table(dialog, "GroupsTable") == ["Hand Tools"],
    )
    game.new_group(dialog, "Drills")
    check(
        "new group created and selected",
        game.table(dialog, "GroupsTable") == ["Hand Tools", "Drills"]
        and game.table(dialog, "ItemsTable") == [],
        str(game.table(dialog, "GroupsTable")),
    )
    game.add_item(dialog, "Elite Hand Drill")
    game.add_item(dialog, "Enhanced Hand Drill")
    check(
        "items added in order",
        game.table(dialog, "ItemsTable") == ["Elite Hand Drill", "Enhanced Hand Drill"],
        str(game.table(dialog, "ItemsTable")),
    )
    game.click(dialog, "ItemsTable", row=1)
    game.click(dialog, "UpButton")
    game.shot("dialog-custom-group")
    check(
        "item moved up",
        game.table(dialog, "ItemsTable") == ["Enhanced Hand Drill", "Elite Hand Drill"],
        str(game.table(dialog, "ItemsTable")),
    )
    game.close_dialog()
    saved = game.saved_groups()
    check(
        "groups saved to the config",
        saved.get("Drills") == [f"{GUN}/HandDrill2Item", f"{GUN}/HandDrill4Item"]
        and len(saved.get("Hand Tools", [])) == 12,
        str(saved),
    )

    screen = game.wait_screen(TOOLBAR_CONFIG)
    game.select_category(screen, "Tool Groups")
    check(
        "Tool Groups shows both groups",
        game.grid_names(screen) == ["Hand Tools", "Drills"],
        str(game.grid_names(screen)),
    )
    game.shot("g-menu-tool-groups")
    game.drag_group_to_toolbar(screen, "Drills", 4)
    check(
        "custom group put on the toolbar",
        game.toolbar().get(4, {}).get("name") == "Drills",
    )
    game.close_screen(TOOLBAR_CONFIG)
    game.holster()
    game.press_slot(4)
    held_check(
        game, "custom order: Enhanced Hand Drill before Elite", "HandDrill/HandDrill2"
    )

    # Editing and resetting the default group
    dialog = game.open_dialog()
    game.click(dialog, "GroupsTable", row=0)
    game.click(dialog, "ItemsTable", row=0)
    game.click(dialog, "RemoveButton")
    check(
        "Elite Welder removed from the default group",
        "Elite Welder" not in game.table(dialog, "ItemsTable"),
        str(game.table(dialog, "ItemsTable")[:3]),
    )
    game.close_dialog()
    game.close_screen(TOOLBAR_CONFIG)
    game.holster()
    game.press_slot(3)
    held_check(game, "edited default group: Enhanced Welder", "Welder/Welder2")

    dialog = game.open_dialog()
    game.click(dialog, "ResetButton")
    check(
        "reset restores the default group",
        game.table(dialog, "ItemsTable")[:2] == ["Elite Welder", "Proficient Welder"]
        and game.table(dialog, "GroupsTable") == ["Hand Tools", "Drills"],
        str(game.table(dialog, "ItemsTable")[:2]),
    )
    game.close_dialog()
    game.close_screen(TOOLBAR_CONFIG)
    game.holster()
    game.press_slot(3)
    held_check(game, "reset default group: Elite Welder", "Welder/Welder4")

    # The staging area takes a group and gives it back
    screen = game.open_toolbar_config()
    game.drag_toolbar_to_staging(screen, 3, 0)
    check(
        "group moved to the staging area",
        game.staging_names(screen)[:1] == ["Hand Tools"],
    )
    game.drag_staging_to_toolbar(screen, 0, 5)
    toolbar = game.toolbar()
    check(
        "group moved back from the staging area",
        toolbar.get(5, {}).get("name") == "Hand Tools" and 3 not in toolbar,
        str({k: v["name"] for k, v in toolbar.items()}),
    )
    # A plain tool the character has (Remote can't drag the disabled ones)
    api.set_toolbar_slot(6, f"{GUN}/Welder4Item")
    time.sleep(1)
    game.drag_toolbar_to_staging(screen, 6, 1)
    game.drag_staging_to_toolbar(screen, 1, 7)
    toolbar = game.toolbar()
    check(
        "a plain tool through the staging area",
        toolbar.get(7, {}).get("name") == "Elite Welder" and 6 not in toolbar,
        str({k: v["name"] for k, v in toolbar.items()}),
    )
    game.close_screen(TOOLBAR_CONFIG)

    # Saved with the world
    api.save()
    time.sleep(8)
    rig.load_world(api, game.client.world)
    toolbar = game.toolbar()
    check(
        "group slots survive saving and loading the world",
        toolbar.get(5, {}).get("type") == MARKER_TYPE
        and toolbar.get(4, {}).get("name") == "Drills",
        str({k: v["name"] for k, v in toolbar.items()}),
    )
    game.holster()
    game.press_slot(5)
    held_check(game, "after loading: Elite Welder", "Welder/Welder4")
    check(
        "no exceptions in the game log",
        game.exception_count() == errors_before,
        f"{game.exception_count() - errors_before} new",
    )


def run_without_plugin(client: rig.Client) -> None:
    """The world saved with group slots loads in a game without the plugin"""
    rig.stop(client)
    os.environ["TBM_WITHOUT_PLUGIN"] = "1"
    rig.launch(client)
    game = Game(rig.wait_api(client), client)
    rig.load_world(game.api, client.world)
    toolbar = game.toolbar()
    check(
        "without the plugin: group slots empty, the others kept",
        4 not in toolbar
        and 5 not in toolbar
        and toolbar.get(7, {}).get("name") == "Elite Welder",
        str({k: v["name"] for k, v in toolbar.items()}),
    )
    check(
        "without the plugin: no exceptions",
        game.exception_count() == 0,
        f"{game.exception_count()} in the game log",
    )


if __name__ == "__main__":
    sys.exit(main())
