"""Isolated headless test client for the Toolbar Manager checks.

Everything comes from a parallel test slot of the workspace
(se/testing/parallel-test-work/README.md), so other sessions can test at the same time:

    TBM_SLOT=15 uv run --with httpx python tests/rig.py start [--no-render]
    TBM_SLOT=15 uv run --with httpx python tests/rig.py stop

Client 0 of slot n gets its own Pulsar folder ~/.se-test/<task>-c0, game user data
folder ~/.se-test/<task>-c0-data, Remote port 25000 + 10n and client id
76561199500010000 + 10n, where <task> is the slot's owner. The launch goes through
run-test.sh, which waits for 8 GiB of free RAM after the launch. Pulsar compiles
Remote and Toolbar Manager from their working copies as dev folders.

The client is headless, but renders by default: without rendering MyHud.HudDefinition
stays null, and the game's own G menu throws on it in survival (the research graph).

TBM_STEAM=1 launches with Steam instead, for DLC blocks, waiting for the machine's
single SE1 Steam slot.

TBM_REMOTE points to the Remote working copy (default ~/ws/se/se1/plugins/remote),
SE_WORKSPACE to the workspace (default: three levels above this repository).
"""

from __future__ import annotations

import os
import re
import shutil
import signal
import subprocess
import sys
import time
import xml.etree.ElementTree as ET
import zipfile
from dataclasses import dataclass
from pathlib import Path

HOME = Path.home()
REPO = Path(__file__).resolve().parent.parent
REMOTE_REPO = Path(
    os.environ.get("TBM_REMOTE", HOME / "ws/se/se1/plugins/remote")
).expanduser()
WORKSPACE = Path(os.environ.get("SE_WORKSPACE", REPO.parents[2])).expanduser()
PARALLEL = WORKSPACE / "testing" / "parallel-test-work"

sys.path.insert(0, str(REMOTE_REPO / "skills" / "se-remote"))
from se_remote import RemoteAPI  # noqa: E402

PLUGIN_ID = "viktor-ferenczi/se-toolbar-manager"
PULSAR_TEMPLATE = Path(os.environ.get("TBM_PULSAR_TEMPLATE", HOME / ".config/Pulsar"))
WORLDS = REMOTE_REPO / "Worlds"
TEMPLATE = "RemoteAPITestSpace"
WORLD_NAME = "ToolbarManagerTest"
LAUNCHER = "TbmInterim.bin"

# RSS of one client settled in a world, for the RAM guard (README.md, section 3)
NEED_GIB = {"render": 5.5, "no-render": 3.7}


def _slot() -> tuple[int, str]:
    value = os.environ.get("TBM_SLOT")
    if not value:
        raise RuntimeError(
            f"Set TBM_SLOT to a claimed slot, see {PARALLEL / 'README.md'}"
        )
    owner = (
        Path(os.environ.get("SE_TEST_SLOTS", HOME / ".se-test/slots")) / value / "owner"
    )
    if not owner.exists():
        raise RuntimeError(f"Slot {value} is not claimed, see {PARALLEL / 'README.md'}")
    return int(value), owner.read_text().strip()


@dataclass
class Client:
    pulsar: Path
    appdata: Path
    port: int
    client_id: int

    @property
    def launcher(self) -> Path:
        return self.pulsar / LAUNCHER

    @property
    def pid_file(self) -> Path:
        return self.appdata / "game.pid"

    @property
    def log(self) -> Path:
        return self.appdata / "SpaceEngineers.log"

    @property
    def world(self) -> Path:
        return self.appdata / "Saves" / "tests" / WORLD_NAME

    @property
    def config(self) -> Path:
        return self.appdata / "Storage" / "ToolbarManager.cfg"

    @property
    def groups(self) -> Path:
        return self.appdata / "ToolbarManager" / "Groups"

    @property
    def url(self) -> str:
        return f"http://127.0.0.1:{self.port}"


def client() -> Client:
    n, task = _slot()
    return Client(
        HOME / f".se-test/{task}-c0",
        HOME / f".se-test/{task}-c0-data",
        25000 + 10 * n,
        76561199500010000 + 10 * n,
    )


def connect(c: Client) -> RemoteAPI:
    return RemoteAPI(c.url, username="admin", password="SpaceEngineers", timeout=60)


# ---------------------------------------------------------------------------
# Pulsar folder and configuration
# ---------------------------------------------------------------------------


def _register_source(sources: Path, name: str, folder: Path, file: str) -> None:
    tree = ET.parse(sources)
    local = tree.getroot().find("LocalPluginSources")
    if local is None:
        local = ET.SubElement(tree.getroot(), "LocalPluginSources")
    for plugin in local.findall("LocalPlugin"):
        if plugin.findtext("Name") == name:
            local.remove(plugin)
    plugin = ET.SubElement(local, "LocalPlugin")
    for tag, value in (
        ("Name", name),
        ("Folder", str(folder)),
        ("File", file),
        ("Enabled", "true"),
    ):
        ET.SubElement(plugin, tag).text = value
    tree.write(sources, encoding="utf-8", xml_declaration=True)


def _profile(ids) -> str:
    folders = "".join(
        f"<LocalFolderConfig><Id>{i}</Id><DebugBuild>true</DebugBuild></LocalFolderConfig>"
        for i in ids
    )
    return (
        '<?xml version="1.0" encoding="utf-8"?>\n'
        '<Profile xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" '
        'xmlns:xsd="http://www.w3.org/2001/XMLSchema">'
        f"<Name>Current</Name><GitHub /><DevFolder>{folders}</DevFolder>"
        "<Local /><Mods /></Profile>\n"
    )


def ensure_pulsar(c: Client) -> None:
    """Creates the client's Pulsar folder on first use, with a renamed launcher, and
    enables only Remote, Toolbar Manager and DirectTransport"""
    if not c.launcher.exists():
        subprocess.run(
            [
                str(WORKSPACE / "testing/pulsar-dev-instances/new-pulsar-instance.sh"),
                str(c.pulsar),
                str(PULSAR_TEMPLATE),
            ],
            check=True,
            stdout=subprocess.DEVNULL,
        )
        shutil.copy2(c.pulsar / "Interim.bin", c.launcher)
    sources = c.pulsar / "Legacy" / "Sources" / "sources.xml"
    _register_source(sources, "remote", REMOTE_REPO, "Remote.xml")
    _register_source(sources, "se-toolbar-manager", REPO, "ToolbarManager.xml")
    profiles = c.pulsar / "Legacy" / "Profiles"
    profiles.mkdir(exist_ok=True)
    # TBM_WITHOUT_PLUGIN=1 leaves Toolbar Manager out, to load its saves with the vanilla game
    plugins = ["remote", "direct-transport"]
    if os.environ.get("TBM_WITHOUT_PLUGIN") != "1":
        plugins.append(PLUGIN_ID)
    (profiles / "Current.xml").write_text(_profile(plugins), encoding="utf-8")


def write_configs(c: Client) -> None:
    """Remote settings, experimental mode, and optional staging override for tests."""
    c.appdata.mkdir(parents=True, exist_ok=True)
    c.config.unlink(missing_ok=True)
    if os.environ.get("TBM_STAGING_DISABLED") == "1":
        c.config.parent.mkdir(parents=True, exist_ok=True)
        c.config.write_text(
            "<Config><EnableStagingArea>false</EnableStagingArea></Config>"
        )
    (c.appdata / "Remote.cfg").write_text(
        '<?xml version="1.0" encoding="utf-8"?>\n'
        '<PluginConfig xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" '
        'xmlns:xsd="http://www.w3.org/2001/XMLSchema">\n'
        "  <Enabled>true</Enabled>\n  <ListenIP>127.0.0.1</ListenIP>\n"
        f"  <ListenPort>{c.port}</ListenPort>\n"
        "  <AdminPassword>SpaceEngineers</AdminPassword>\n"
        "</PluginConfig>\n",
        encoding="utf-8",
    )
    game_cfg = c.appdata / "SpaceEngineers.cfg"
    if not game_cfg.exists():
        shutil.copy(HOME / ".config/SpaceEngineers/SpaceEngineers.cfg", game_cfg)
    text = re.sub(
        r"(<Key>ExperimentalMode</Key>\s*<Value>\s*<Value[^>]*>)\w+(</Value>)",
        r"\1True\2",
        game_cfg.read_text(encoding="utf-8"),
    )
    game_cfg.write_text(text, encoding="utf-8")


def prepare_world(world: Path) -> Path:
    """A fresh survival copy of the Remote suite's space world, with no hand tools or
    weapons in the character's inventory. The binary save is dropped, so the game
    loads the edited XML."""
    if world.exists():
        shutil.rmtree(world)
    world.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(WORLDS / f"{TEMPLATE}.zip") as archive:
        archive.extractall(world.parent)
    (world.parent / TEMPLATE).rename(world)
    for name in ("Sandbox.sbc", "Sandbox_config.sbc"):
        path = world / name
        text = path.read_text(encoding="utf-8")
        for key, value in {
            "SessionName": world.name,
            "GameMode": "Survival",
            "TrashRemovalEnabled": "false",
            "ExperimentalMode": "true",
            "OnlineMode": "OFFLINE",
        }.items():
            text = re.sub(rf"<{key}>[^<]*</{key}>", f"<{key}>{value}</{key}>", text)
        path.write_text(text, encoding="utf-8")
    sbs = world / "SANDBOX_0_0_0_.sbs"
    text, count = re.subn(
        r"\s*<MyObjectBuilder_InventoryItem>(?:(?!</MyObjectBuilder_InventoryItem>).)*"
        r'xsi:type="MyObjectBuilder_PhysicalGunObject".*?</MyObjectBuilder_InventoryItem>',
        "",
        sbs.read_text(encoding="utf-8"),
        flags=re.S,
    )
    assert count, "No hand items found in the character's inventory"
    sbs.write_text(text, encoding="utf-8")
    (world / "SANDBOX_0_0_0_.sbsB5").unlink()
    return world


# ---------------------------------------------------------------------------
# Process. It runs in a session of its own, started through run-test.sh, so
# stopping kills exactly the process group this rig started.
# ---------------------------------------------------------------------------


def _alive(c: Client) -> int | None:
    try:
        pid = int(c.pid_file.read_text().strip())
        cmdline = Path(f"/proc/{pid}/cmdline").read_bytes()
    except (OSError, ValueError):
        return None
    return pid if str(c.launcher).encode() in cmdline else None


def running(c: Client) -> bool:
    return _alive(c) is not None


def launch(c: Client, render: bool = True) -> None:
    if running(c):
        raise RuntimeError(f"Client {c.pulsar} is already running")
    ensure_pulsar(c)
    write_configs(c)
    c.log.unlink(missing_ok=True)
    args = [
        "-multiInstance", "-lazySteam", "-noprompt", "-noupdate", "-nosplash",
        "-stablelogs", "-sources", "-appdata", str(c.appdata), "--quality", "minimal",
        "--resolution", "1280x720", "--no-audio", "--headless",
    ]  # fmt: skip
    # TBM_STEAM=1 runs with the machine's Steam account, which owns the DLCs, through the
    # single Steam slot of the game (testing/game-test-instance-modes/README.md, mode C)
    steam = os.environ.get("TBM_STEAM") == "1"
    command = [str(c.launcher), *args]
    if steam:
        command = [
            str(PARALLEL / "steam-slot.sh"),
            "se1",
            str(c.appdata),
            "--",
            *command,
        ]
    else:
        command += ["--no-steam", "--client-id", str(c.client_id)]
    env = {}
    if not render:
        command.append("--no-render")
        env["PULSAR_NO_RENDER"] = "true"
    process = subprocess.Popen(
        [
            str(PARALLEL / "run-test.sh"),
            str(NEED_GIB["render" if render else "no-render"]),
            "--",
            *command,
        ],
        cwd=c.pulsar,
        env={**os.environ, **env},
        stdout=open(c.appdata / "launch.log", "w"),
        stderr=subprocess.STDOUT,
        stdin=subprocess.DEVNULL,
        start_new_session=True,
    )
    c.pid_file.write_text(str(process.pid))


def stop(c: Client) -> None:
    pid = _alive(c)
    if pid is None:
        c.pid_file.unlink(missing_ok=True)
        return
    os.killpg(pid, signal.SIGTERM)
    deadline = time.monotonic() + 30
    while running(c) and time.monotonic() < deadline:
        time.sleep(1)
    try:
        os.killpg(pid, signal.SIGKILL)
    except ProcessLookupError:
        pass
    c.pid_file.unlink(missing_ok=True)


def wait_api(c: Client, timeout: float = 2400.0) -> RemoteAPI:
    """The RAM guard may hold the launch back for up to 30 minutes"""
    api = connect(c)
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        if not running(c):
            raise RuntimeError(f"The client exited, see {c.appdata / 'launch.log'}")
        try:
            api.ping()
            return api
        except Exception:  # noqa: BLE001 -- not listening yet
            time.sleep(2)
    raise TimeoutError("The client's Remote API did not come up")


def wait_world(api: RemoteAPI, timeout: float = 600.0) -> None:
    """Waits for the session, clicking OK on message boxes on the way (the XML world
    first asks to be loaded from XML)"""
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        time.sleep(1)
        try:
            boxes = [
                s
                for s in api.list_screens()
                if s.get("type") == "MyGuiScreenMessageBox"
            ]
            for box in boxes:
                api.control_click(text="OK", screen=box["index"])
            if not boxes and api.get_state().get("ready"):
                return
        except Exception:  # noqa: BLE001 -- the API times out while a world loads
            pass
    raise TimeoutError("The test world did not become ready")


def focus_gameplay(api: RemoteAPI, timeout: float = 60.0) -> None:
    """Closes everything above the gameplay screen"""
    keep = ("MyGuiScreenGamePlay", "MyGuiScreenHudSpace")
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        screens = [s.get("type") for s in api.list_screens()]
        extra = [i for i, kind in enumerate(screens) if kind not in keep]
        if not extra and set(keep) <= set(screens):
            return
        time.sleep(0.5)
        if extra and "MyGuiScreenLoading" not in screens:
            api.close_screen(extra[-1])
    raise TimeoutError("Could not get back to the gameplay screen")


def load_world(api: RemoteAPI, world: Path) -> None:
    try:
        api.load(str(world))
    except Exception:  # noqa: BLE001 -- the load outlives the HTTP timeout
        pass
    # Don't take the old session for the new one
    deadline = time.monotonic() + 60
    while time.monotonic() < deadline:
        try:
            if not api.get_state().get("ready"):
                break
        except Exception:  # noqa: BLE001
            break
        time.sleep(0.5)
    wait_world(api)
    focus_gameplay(api)


def start(c: Client, render: bool = True) -> RemoteAPI:
    """Starts the client and loads a fresh copy of the test world"""
    if c.groups.exists():
        shutil.rmtree(c.groups)
    launch(c, render)
    api = wait_api(c)
    load_world(api, prepare_world(c.world))
    return api


if __name__ == "__main__":
    command = sys.argv[1] if len(sys.argv) > 1 else ""
    c = client()
    if command == "start":
        stop(c)
        start(c, render="--no-render" not in sys.argv)
        print(f"Ready: SE_REMOTE_URL={c.url}")
    elif command == "stop":
        stop(c)
    else:
        sys.exit(__doc__)
