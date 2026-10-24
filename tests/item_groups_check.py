"""In-game checks of the tool groups, on the isolated client of rig.py.

    TBM_SLOT=15 uv run --with httpx python tests/item_groups_check.py [--attach] [--keep]

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
CUBE_BLOCK = "MyObjectBuilder_CubeBlock"

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
        self.wheel = 0

    def scroll(self, delta: int) -> None:
        # UpdateStates takes an absolute wheel counter. Remote's scroll helper resets
        # it to zero on the next frame, producing an unwanted turn in the other direction.
        self.wheel += delta
        self.api.set_input_state(scroll=self.wheel)
        time.sleep(1)

    def clear_scroll(self) -> None:
        self.api.clear_input_state()
        self.wheel = 0

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
        """The hand item, or the block of the cube builder in hand"""
        state = self.api.get_character()
        if state.get("cubeBlock"):
            return state["cubeBlock"]
        weapon = state.get("weapon")
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
            if grid.get("name") == "GroupItemsGrid":
                found["group"] = grid
            elif x < 0.3 and y < 0.5:
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
        # Block tooltips have more lines after the name
        return [
            (item.get("tooltips") or [""])[0].split("\n")[0].strip()
            for item in grid["properties"]["items"]
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

    # Group edit mode of the G menu

    def open_edit_mode(self) -> int:
        screen = self.screen_index(TOOLBAR_CONFIG)
        if screen is None:
            screen = self.open_toolbar_config()
        self.click(screen, "OpenItemGroupsButton")
        time.sleep(1)
        return screen

    def editing(self, screen: int) -> bool:
        return "group" in self.grids(screen)

    def group_names(self, screen: int) -> list[str]:
        """The groups as the selector lists them: in editor order, which is saved on
        every change. Remote shows only the selected key of a combobox."""
        return list(self.saved_groups())

    def selected_group(self, screen: int) -> str | None:
        key = self.control(screen, "GroupSelector")["properties"].get("selectedKey")
        names = self.group_names(screen)
        return names[key] if key is not None and 0 <= key < len(names) else None

    def select_group(self, screen: int, name: str) -> None:
        self.api.control_set(
            "GroupSelector", self.group_names(screen).index(name), screen=screen
        )
        time.sleep(0.5)

    def group_items(self, screen: int) -> list[str]:
        return self.names(self.grids(screen)["group"])

    def name_dialog(self, screen: int, button: str, name: str) -> None:
        self.click(screen, button)
        names = self.wait_screen("NameDialog")
        self.api.control_set("Textbox", name, screen=names)
        self.api.control_click(text="OK", screen=names)
        self.wait_screen("NameDialog", present=False)

    def retry_name(self, screen: int, button: str, invalid: str, valid: str) -> None:
        before = self.saved_groups()
        self.click(screen, button)
        dialog = self.wait_screen("NameDialog")
        self.api.control_set("Textbox", invalid, screen=dialog)
        self.api.control_click(text="OK", screen=dialog)
        error = self.wait_screen("MyGuiScreenMessageBox")
        check(
            f"{button} rejects an existing name without changing groups",
            self.saved_groups() == before
            and self.screen_index("NameDialog") is not None,
        )
        self.api.control_click(text="OK", screen=error)
        self.wait_screen("MyGuiScreenMessageBox", present=False)
        dialog = self.wait_screen("NameDialog")
        self.api.control_set("Textbox", valid, screen=dialog)
        self.api.control_click(text="OK", screen=dialog)
        self.wait_screen("NameDialog", present=False)

    def delete_group(self, screen: int) -> None:
        self.click(screen, "DeleteGroupButton")
        dialog = self.wait_screen("MyGuiScreenMessageBox")
        self.api.control_click(text="Yes", screen=dialog)
        self.wait_screen("MyGuiScreenMessageBox", present=False)

    def drag_to_group(self, screen: int, name: str, slot: int) -> None:
        self.drag(screen, "blocks", self.grid_names(screen).index(name), "group", slot)

    def move_in_group(self, screen: int, slot: int, target_slot: int) -> None:
        self.drag(screen, "group", slot, "group", target_slot)

    def grid_click(
        self, screen: int, grid: str, slot: int, action: str, button="left"
    ) -> None:
        found = self.grids(screen)[grid]
        item = next(i for i in found["properties"]["items"] if i["slot"] == slot)
        self.api.grid_action(
            found["id"], slot, action, item=item["id"], button=button, screen=screen
        )
        time.sleep(1)

    def double_click_block(self, screen: int, name: str) -> None:
        self.grid_click(
            screen, "blocks", self.grid_names(screen).index(name), "double-click"
        )

    def remove_from_group(self, screen: int, slot: int) -> None:
        self.grid_click(screen, "group", slot, "click", button="right")

    def saved_groups(self) -> dict[str, list[str]]:
        """The group files in editor order, by name."""
        groups = [ET.parse(path).getroot() for path in self.client.groups.glob("*.xml")]
        groups.sort(key=lambda group: int(group.findtext("Order", "0")))
        return {
            group.findtext("Name"): [item.text for item in group.iter("string")]
            for group in groups
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
                and not line.lstrip().startswith("at ")
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
            run_after_restart(game)
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
    check("G menu lists Groups", "Groups" in categories, str(categories[:6]))
    game.select_category(screen, "Groups")
    check(
        "Groups shows the default group",
        game.grid_names(screen) == ["Hand Tools"],
        str(game.grid_names(screen)),
    )
    toolbar_before = game.toolbar()
    game.grid_click(
        screen, "blocks", game.grid_names(screen).index("Hand Tools"), "click"
    )
    check(
        "single click opens the selected group without assigning a toolbar slot",
        game.editing(screen)
        and game.selected_group(screen) == "Hand Tools"
        and game.toolbar() == toolbar_before,
    )
    game.click(screen, "DoneGroupButton")
    check(
        "groups are individual files beside toolbar profile folders, outside the config",
        (game.client.groups / "Hand Tools.xml").is_file()
        and ET.parse(game.client.config).getroot().find("ItemGroups") is None,
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
    for delta in (120, -120):
        game.scroll(delta)
        held_check(game, f"one available kind, wheel {delta}", "Welder/Welder2")
    game.clear_scroll()

    game.give("Welder4Item")
    game.holster()
    game.press_slot(3)
    held_check(game, "both tiers: Elite Welder", "Welder/Welder4")

    game.give("AngleGrinder4Item")
    for delta, expected in (
        (120, "AngleGrinder/AngleGrinder4"),
        (120, "Welder/Welder4"),
        (-120, "AngleGrinder/AngleGrinder4"),
        (-120, "Welder/Welder4"),
    ):
        game.scroll(delta)
        held_check(game, f"wheel {delta} skips missing drill: {expected}", expected)
    game.clear_scroll()

    # Every kind of the group, pressing the slot again moves to the next kind
    for subtype in (
        "AngleGrinder2Item",
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

    # A later ordinary slot wins the game's selected-slot scan when its tool is held.
    api.set_toolbar_slot(7, f"{GUN}/AngleGrinder4Item")
    forward = ("AngleGrinder/AngleGrinder4", "HandDrill/HandDrill4", "Welder/Welder4")
    for cycle in range(2):
        for expected in forward:
            game.press_slot(3)
            held_check(
                game, f"overlapping slot, cycle {cycle + 1}: {expected}", expected
            )

    for delta, sequence in (
        (120, forward),
        (-120, (forward[1], forward[0], forward[2])),
    ):
        for expected in sequence:
            game.scroll(delta)
            held_check(game, f"wheel {delta}: {expected}", expected)
    game.clear_scroll()
    api.set_toolbar_slot(7, None)

    game.holster()
    game.scroll(120)
    held_check(game, "wheel with no held group leaves hands empty", None)
    game.clear_scroll()

    # Edit mode of the G menu: the staging area turns into the group editor
    screen = game.open_edit_mode()
    check("TG turns on edit mode", game.editing(screen))
    selected = game.selected_group(screen)
    game.click(screen, "OpenItemGroupsButton")
    check(
        "TG returns to the staging area",
        not game.editing(screen) and "staging" in game.grids(screen),
    )
    game.click(screen, "OpenItemGroupsButton")
    check(
        "TG reopens the editor and preserves the selected group",
        game.editing(screen) and game.selected_group(screen) == selected,
    )
    buttons = [
        game.control(screen, name + "GroupButton")
        for name in ("New", "Rename", "Duplicate", "Delete", "Resetdefault")
    ]
    right = lambda control: control["position"]["x"] + control["size"]["x"]
    check(
        "five editor buttons have equal widths and gaps",
        all(abs(b["size"]["x"] - buttons[0]["size"]["x"]) < 0.0001 for b in buttons)
        and all(
            abs(buttons[i + 1]["position"]["x"] - right(buttons[i]) - 0.006) < 0.0001
            for i in range(4)
        ),
        str([(b["position"], b["size"]) for b in buttons]),
    )
    before_scroll = game.held()
    game.scroll(-120)
    check("wheel in editor does not switch tools", game.held() == before_scroll)
    game.clear_scroll()
    check(
        "editor shows the default group",
        game.selected_group(screen) == "Hand Tools"
        and game.group_items(screen)[:2] == ["Elite Welder", "Proficient Welder"],
        f"{game.selected_group(screen)}: {game.group_items(screen)[:2]}",
    )

    # A custom group of tools and a block, preferring the lower tier drill
    game.retry_name(screen, "NewGroupButton", " hand tools ", "Builder")
    check(
        "new group created and selected",
        game.group_names(screen) == ["Hand Tools", "Builder"]
        and game.selected_group(screen) == "Builder"
        and game.group_items(screen) == [],
        f"{game.group_names(screen)}, {game.selected_group(screen)}",
    )
    game.select_category(screen, "Character Tools")
    game.drag_to_group(screen, "Elite Hand Drill", 0)
    game.drag_to_group(screen, "Enhanced Hand Drill", 1)
    game.select_category(screen, "Armor Blocks")
    game.double_click_block(screen, "Light Armor Slope")
    game.double_click_block(screen, "Heavy Armor Slope")
    check(
        "tools dragged in, blocks added by double click",
        game.group_items(screen)
        == [
            "Elite Hand Drill",
            "Enhanced Hand Drill",
            "Light Armor Slope",
            "Heavy Armor Slope",
        ],
        str(game.group_items(screen)),
    )
    game.move_in_group(screen, 1, 0)
    game.remove_from_group(screen, 3)
    game.shot("g-menu-group-editor")
    check(
        "item moved first, last one removed by right click",
        game.group_items(screen)
        == ["Enhanced Hand Drill", "Elite Hand Drill", "Light Armor Slope"],
        str(game.group_items(screen)),
    )
    toolbar = game.toolbar()
    check(
        "editing left the toolbar alone",
        set(toolbar) == {0, 1, 2, 3},
        str({k: v["name"] for k, v in toolbar.items()}),
    )
    saved = game.saved_groups()
    check(
        "groups saved to individual XML files",
        saved.get("Builder")
        == [
            f"{GUN}/HandDrill2Item",
            f"{GUN}/HandDrill4Item",
            f"{CUBE_BLOCK}/LargeBlockArmorSlope",
        ]
        and len(saved.get("Hand Tools", [])) == 12,
        str(saved.get("Builder")),
    )

    # Rename and delete
    game.retry_name(screen, "RenameGroupButton", "Hand Tools", "Builder Kit")
    game.name_dialog(screen, "RenameGroupButton", "Builder Kit")
    original = game.saved_groups()["Builder Kit"]
    game.retry_name(screen, "DuplicateGroupButton", " builder kit ", "Builder Copy")
    definitions = [
        ET.parse(path).getroot() for path in game.client.groups.glob("*.xml")
    ]
    ids = [group.findtext("Id") for group in definitions]
    check(
        "Duplicate copies the ordered items with a new id and selects the copy",
        game.saved_groups()["Builder Copy"] == original
        and len(set(ids)) == len(ids)
        and game.selected_group(screen) == "Builder Copy",
    )
    game.remove_from_group(screen, 0)
    check(
        "editing the duplicate leaves its source unchanged",
        game.saved_groups()["Builder Kit"] == original
        and game.saved_groups()["Builder Copy"] == original[1:],
    )
    game.delete_group(screen)
    game.name_dialog(screen, "NewGroupButton", "Path/Group")
    check(
        "group names use toolbar filename sanitizing",
        game.selected_group(screen) == "Path-Group"
        and (game.client.groups / "Path-Group.xml").is_file(),
    )
    game.name_dialog(screen, "RenameGroupButton", "Path?Group")
    check(
        "renaming updates the filename and removes the old file",
        (game.client.groups / "Path.Group.xml").is_file()
        and not (game.client.groups / "Path-Group.xml").exists(),
    )
    game.retry_name(screen, "DuplicateGroupButton", "Path:Group", "Sanitized Copy")
    game.delete_group(screen)
    game.select_group(screen, "Path.Group")
    game.delete_group(screen)
    check(
        "deleting groups removes their files",
        not (game.client.groups / "Path.Group.xml").exists()
        and not (game.client.groups / "Sanitized Copy.xml").exists(),
    )
    game.name_dialog(screen, "NewGroupButton", "Scratch")
    game.click(screen, "DeleteGroupButton")
    box = game.wait_screen("MyGuiScreenMessageBox")
    api.control_click(text="Yes", screen=box)
    game.wait_screen("MyGuiScreenMessageBox", present=False)
    check(
        "group renamed, another one created and deleted",
        game.group_names(screen) == ["Hand Tools", "Builder Kit"],
        str(game.group_names(screen)),
    )

    # Leaving edit mode, the group goes on the toolbar from the Groups category
    game.click(screen, "DoneGroupButton")
    check(
        "Done brings the staging area back",
        not game.editing(screen) and "staging" in game.grids(screen),
    )
    game.select_category(screen, "Groups")
    check(
        "Groups shows both groups",
        game.grid_names(screen) == ["Hand Tools", "Builder Kit"],
        str(game.grid_names(screen)),
    )
    game.grid_click(
        screen, "blocks", game.grid_names(screen).index("Builder Kit"), "click"
    )
    check(
        "single click opens the chosen custom group",
        game.editing(screen) and game.selected_group(screen) == "Builder Kit",
    )
    game.grid_click(
        screen, "blocks", game.grid_names(screen).index("Hand Tools"), "click"
    )
    check(
        "single click switches the group in an open editor",
        game.editing(screen) and game.selected_group(screen) == "Hand Tools",
    )
    game.click(screen, "DoneGroupButton")
    game.drag_group_to_toolbar(screen, "Builder Kit", 4)
    check(
        "custom group put on the toolbar",
        game.toolbar().get(4, {}).get("name") == "Builder Kit",
    )
    game.close_screen(TOOLBAR_CONFIG)
    game.holster()
    game.press_slot(4)
    held_check(
        game, "custom order: Enhanced Hand Drill before Elite", "HandDrill/HandDrill2"
    )
    game.press_slot(4)
    held_check(game, "pressed again: the block", "CubeBlock/LargeBlockArmorSlope")
    game.press_slot(4)
    held_check(
        game,
        "pressed again with the block: its small variant",
        "CubeBlock/SmallBlockArmorSlope",
    )
    game.scroll(120)
    held_check(game, "wheel: back to the drill", "HandDrill/HandDrill2")
    game.clear_scroll()

    # Editing and resetting the default group
    screen = game.open_edit_mode()
    game.select_group(screen, "Hand Tools")
    game.remove_from_group(screen, 0)
    check(
        "Elite Welder removed from the default group",
        game.group_items(screen)[:1] == ["Proficient Welder"],
        str(game.group_items(screen)[:3]),
    )
    game.click(screen, "DoneGroupButton")
    game.close_screen(TOOLBAR_CONFIG)
    game.holster()
    game.press_slot(3)
    held_check(game, "edited default group: Enhanced Welder", "Welder/Welder2")

    screen = game.open_edit_mode()
    other_groups = {k: v for k, v in game.saved_groups().items() if k != "Hand Tools"}
    game.select_group(screen, "Builder Kit")
    game.click(screen, "ResetdefaultGroupButton")
    check(
        "reset restores the default group",
        game.selected_group(screen) == "Hand Tools"
        and game.group_items(screen)[:2] == ["Elite Welder", "Proficient Welder"]
        and game.group_names(screen) == ["Hand Tools", "Builder Kit"],
        str(game.group_items(screen)[:2]),
    )
    check(
        "Reset leaves other groups unchanged even when another group was selected",
        {k: v for k, v in game.saved_groups().items() if k != "Hand Tools"}
        == other_groups,
    )
    game.delete_group(screen)
    check("Hand Tools can be deleted", "Hand Tools" not in game.saved_groups())
    game.click(screen, "ResetdefaultGroupButton")
    check(
        "Reset recreates missing Hand Tools and preserves other groups",
        len(game.saved_groups().get("Hand Tools", [])) == 12
        and game.selected_group(screen) == "Hand Tools"
        and {k: v for k, v in game.saved_groups().items() if k != "Hand Tools"}
        == other_groups,
    )
    game.click(screen, "DoneGroupButton")
    game.close_screen(TOOLBAR_CONFIG)
    game.holster()
    game.press_slot(3)
    held_check(game, "reset default group: Elite Welder", "Welder/Welder4")

    # Edit groups in the plugin settings: the G menu opens in edit mode, alone on top
    rig.focus_gameplay(api)
    api.key("OemQuestion", ["LeftControl", "LeftAlt"])
    plugins = game.wait_screen("ConfigurePlugin")
    rows = game.control(plugins, "Table")["properties"]["rows"]
    row = next(r["index"] for r in rows if r["cells"][0] == "Toolbar Manager")
    api.control_click(name="Table", screen=plugins, row=row, double_click=True)
    settings = game.wait_screen("SettingsScreen")
    api.control_click(text="Edit groups", screen=settings)
    # The settings close first and shift the screen indexes
    game.wait_screen("SettingsScreen", present=False)
    game.wait_screen("ConfigurePlugin", present=False)
    screen = game.wait_screen("CustomToolbarConfigScreen")
    time.sleep(1)
    screen = game.screen_index("CustomToolbarConfigScreen")
    kinds = [s["type"] for s in game.screens() if s.get("state") != "HIDDEN"]
    check(
        "Edit groups in the settings opens the G menu in edit mode",
        game.editing(screen)
        and kinds == ["MyGuiScreenGamePlay", "CustomToolbarConfigScreen"],
        str(kinds),
    )
    game.click(screen, "DoneGroupButton")
    game.close_screen("CustomToolbarConfigScreen")

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

    # Pressing a toolbar slot in the G menu uses it, so the drag above moved the group on.
    # Bring it back to the welder, the group continues there after loading.
    game.holster()
    for _ in range(3):
        game.press_slot(5)
        if game.held() == "MyObjectBuilder_Welder/Welder4":
            break
    game.holster()

    # Saved with the world
    api.save()
    time.sleep(8)
    rig.load_world(api, game.client.world)
    toolbar = game.toolbar()
    check(
        "group slots survive saving and loading the world",
        toolbar.get(5, {}).get("type") == MARKER_TYPE
        and toolbar.get(4, {}).get("name") == "Builder Kit",
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


def run_after_restart(game: Game) -> None:
    expected = game.saved_groups()
    rig.stop(game.client)
    rig.launch(game.client)
    api = rig.wait_api(game.client)
    rig.load_world(api, game.client.world)
    restarted = Game(api, game.client)
    screen = restarted.open_edit_mode()
    check(
        "group files reload after restarting the game",
        restarted.saved_groups() == expected
        and restarted.selected_group(screen) == "Hand Tools"
        and len(restarted.group_items(screen)) == 12,
    )
    restarted.select_group(screen, "Builder Kit")
    check(
        "custom group order and items survive restart",
        restarted.group_items(screen)
        == ["Enhanced Hand Drill", "Elite Hand Drill", "Light Armor Slope"],
    )
    restarted.click(screen, "DoneGroupButton")
    restarted.close_screen(TOOLBAR_CONFIG)
    restarted.holster()
    restarted.press_slot(4)
    held_check(restarted, "group id survives restart", "HandDrill/HandDrill2")


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
