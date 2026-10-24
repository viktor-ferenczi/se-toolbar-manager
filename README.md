# Toolbar Manager

Toolbar Manager plugin for [Space Engineers 1](https://www.spaceengineersgame.com/).

Please consider supporting my work on [Patreon](https://www.patreon.com/semods) or one time via [PayPal](https://www.paypal.com/paypalme/vferenczi/).

- [SE Mods Discord](https://discord.gg/PYPFPGf3Ca) FAQ, Troubleshooting, Support, Bug Reports, Discussion
- [Pulsar Discord](https://discord.gg/z8ZczP2YZY) Everything about plugins
- [YouTube Channel](https://www.youtube.com/@couldntfindafreename)
- [Source code](https://github.com/viktor-ferenczi/se-toolbar-manager)
- [Bug reports](https://discord.gg/KWzDu683zs)

## Installation

1. Exit from Space Engineers
2. Install [Pulsar](https://github.com/SpaceGT/Pulsar)
3. Start Space Engineers
4. Open the **Plugins** menu (it should show up in the Main Menu)
5. Add the **Toolbar Manager** plugin to your list (`+` icon)
6. Apply the change and let the game restart

After enabling the plugin it will be active for all single- and multiplayer worlds.

*Enjoy!*

## Features

This plugin allows for the saving, loading and convenient editing of character and block
toolbars. For example, you can quickly load a "standard" toolbar for your character into 
any game (including multiplayer ones), which can greatly speed up your builds by using 
toolbar slots you have already memorized. It can also be used to fix the broken toolbars of
your ships in survival based on the profiles you saved in your creative design world earlier.

This plugin works for both offline and online multiplayer games without
the need for any server side support. Toolbar profiles are saved locally,
nothing is stored on any server.

### Editing toolbars

This plugin adds a new "Staging" area in the G menu, which allows for the convenient reordering
of toolbar items, including moving them between toolbar pages.

The staging areas are preserved for the character and each block with a toolbar in-memory during
gameplay, but **not saved** over sessions (world loads) and game restarts. Please save your
toolbars into profiles after editing.

You can disable the staging area in the plugin's configuration, should it cause any issues.

### Saving/loading toolbars (profiles)

The **G menu** has a new TM buttons at the bottom left to manage saved toolbar profiles.

**New** saves the currently open toolbar as a new profile.

**Load** overwrites your currently open toolbar from the selected profile.

**Merge** loads only the used slots from the profile and leaves the rest of them as is.

You can also **Rename** and **Delete** toolbar profiles.

#### Profile folders (for backup)

Saved toolbars are stored as XML files in folders under:
`%AppData%\Roaming\SpaceEngineers\ToolbarManager`

There are subdirectories for each toolbar type. Toolbars within the same type are compatible,
however they may not have the same amount of slots available in-game. For example, it is
possible to save the toolbar from any Cockpit and load the profile into a Remote Control.

Up to 6 backups of each profile is kept, even after deleting the profile. Renaming profiles
also renames the backup files. Orphaned backup files (with the main profile deleted) are
cleaned up after 90 days, but only when a profile is deleted the next time.

### Groups

A group puts several tools, weapons and blocks into one slot of your character toolbar.
Using the slot equips the first available item in tile order. Pressing the slot again
with a tool or weapon of the group in hand moves on to the next item, all tiers of a tool
counting as one. With the default group that is welder, grinder, then hand drill. With a
block of the group in hand, pressing the slot again does what it does on a block's own
slot: it switches between the block's large and small variant. Tools you don't carry,
blocks you haven't unlocked and blocks of DLCs you don't own are skipped. If none of the
items can be used, nothing happens.

Several groups can be on the toolbar at the same time, on any slots and pages, and they
may share items. Each slot steps only through its own group. Going back to a group's
slot equips the item it was on last.

While holding a tool, weapon or block from the group used last, with that group on the
current toolbar page, scroll up to move forward through the group or down to move backward.
The wheel and the slot pick the first available item of each kind in tile order,
regardless of its level, and every block is a step of its own. With a block of the group
in hand the wheel steps through the group instead of the game's block variants. Scrolling
in menus or with Ctrl, Shift or Alt held keeps the game's normal behavior, except for the
fitting blocks below.

The G menu lists the groups under **Groups**. Drag a group onto the toolbar to use it.
Click its tile to open the editor with that group selected. The **TG** button
at the bottom left of the G menu turns on edit mode, where the
staging area shows the items of the group selected above it:

- Drag tools, weapons and blocks from the G menu into the group, or double click them to
  add them at the end. The block variants on the right can be dragged in too. Greyed out
  items, which you don't have or haven't researched yet, can be added as well, so a group
  can be set up before you get every tier of a tool.
- A game block group, a block tile with a `+` in its corner like the Light Armor Block,
  adds its blocks one by one, in the order the game cycles through them. Blocks already
  in the group stay where they are. This way the group can mix blocks of several game
  groups, and single blocks can be moved or removed. To add only one block of a game
  group, drag it from the block variants on the right. A group saved before version 1.7.4
  keeps such a tile as its first block only; add the tile again to get the others.
- Drag items inside the group to change their order. The first one wins, so put the one
  you prefer first. For example, an Enhanced Welder before an Elite Welder is selected
  when both are in your inventory. Right click removes an item.
- **New**, **Rename** and **Delete** manage the groups. **Duplicate** asks for a name
  and copies the selected group's items in their current order. New, Rename and
  Duplicate reject existing group names, ignoring case and surrounding spaces,
  and leave the naming dialog open so you can correct the name.
- **Reset default** restores or recreates the default **Hand Tools** group without
  changing other groups: the Elite, Proficient, Enhanced and basic tiers of the welder,
  grinder and hand drill, in that order.
- **Done** or **TG** again brings the staging area back. The group editor is also
  available when staging is disabled; closing it returns to the normal G menu.

The **Edit groups** button in the plugin's configuration opens the G menu in edit mode, when
you are in a world.

Each group is saved as a separate XML file in `ToolbarManager/Groups` under the game's
user data folder, beside the `Character`, `Ship` and other toolbar profile folders.
Group names use the same filename sanitizing rules as toolbar profile names. Renaming
or deleting a group renames or removes its file.

Toolbar slots holding a group are saved
with the world and in toolbar profiles. A world saved with group slots still loads without
the plugin; those slots are just empty then. On a dedicated server the server does not keep
group slots, so you have to put them back after reconnecting.

#### Fitting blocks

With a block of a group in hand and the placement box on a grid, hold Shift and turn the
mouse wheel to step through only the blocks of the group that fit there, each turned the
way it fits. The extra mouse buttons do the same without the wheel: the forward button
(mouse 5) selects the next fitting block, the back button (mouse 4) the previous one.
After the last fitting block it starts over. For example, with the light armor blocks in
a group it picks the cube for a hole in a wall, the slope for a gap in a row of slopes
and the corner at the corner of a sloped roof. If nothing in the group fits, the game's
sound of a refused placement plays, a short message says so and the block in hand stays.
The wheel without Shift steps through the whole group as before.

Pressing Shift alone is the quick way: it keeps the block in hand if it fits, turned the
way it fits, or else selects the next block that fits. Then the wheel can step on from
there. When nothing fits, the quick press stays silent.

A block fits when its face matches the face of the block you aim at, and the faces of the
other blocks around it of the same kind: armor with armor, beams with beams, and so on for
the blocks of a game block group. Other neighbors don't count, so a beam can continue past
a wall instead of being closed off to hug it. A face that would stick out where the
neighbor has none, like a full face against the slanted side of a slope, counts more than
leaving part of the neighbor's face uncovered. The blocks of the group that match best are
the ones that fit. The shapes come from the blocks' mount points.

Blocks with conveyor or upgrade ports fit by their ports instead: every port of a
neighbor pointing into the block has to be met by a port of the same kind (large
conveyor, small conveyor or upgrade), no port may run into a block that has no such port
there, and at least one port has to connect. So a tube is not offered against the middle
of another tube, and an upgrade module goes on a refinery or assembler with its ports on
the machine's upgrade ports, with as many of them connected as possible. Ports of a kind
the block doesn't have are left alone: a tube may run past a machine's upgrade ports, and
armor may cover them. A block without ports fits an open conveyor end only as a cap of the
port's size, mounted on that end alone: the small cap on a small port, the medium cap on
the large port of a small grid's medium conveyors.

This works on small grids too, with small and medium conveyors, and for blocks larger
than one cube, placed the way the game places them against the aimed face. Modded blocks
work the same way when their models name their ports like the game's blocks do
(`detector_conveyor`, `detector_conveyor_small`, `detector_upgrade`) and their
definitions have mount points; armor counts as one kind if it is built from the game's
armor shapes, other blocks by their block variant group.

The plugin's configuration can change the wheel's modifier key to Ctrl or Alt or turn it
off, turn the quick press off, assign the next and previous actions to other mouse buttons, and add keyboard keys
for them. When the middle button is assigned, a plain middle button press that selects a
block doesn't paint; otherwise it paints as in the game.

### Quick block selection

Press the `BACKSLASH` or `PIPE` key (on English keyboard) to open the quick
block search menu. It works the same way as the original menu, but the search
rules are optimized to find blocks primarily by capital letters and digits:

- `AB` **Armor Blocks**
- `PB` **Programmable Block**
- `SK` **Survival Kit**
- `AT` **Atmospheric Thrusters**
- `LHT` **Large Hydrogen Thrusters**
- `211` **All 2x1x1 armor blocks**

Adding the subsequent lower case letters after an upper case one allows
for narrowing down in case of ambiguity:

- `PH` **Parachute Hatch** and **Point Hand**
- `PHat` **Parachute Hatch** only

The order of characters must match exactly. Lower case characters and
space are skipped after a matching upper chase character or digit.

Separating multiple search patterns by space is not supported, currently.

You can change the hotkey in the plugin's configuration.

### Configuration

All key features of this plugin are configurable. You can change any of the configuration options without having to restart or reload anything. Please see below how to access the plugin's configuration dialog.

#### From the main menu

Double-click on the **Toolbar Manager** plugin in Pulsar's **Plugins** dialog, which will open the plugin's detailed information dialog. Use the **Settings** button in that dialog.

#### During game play

If you are in a menu (Terminal, etc.), then close that. Press `Ctrl-Alt-/` to open the **Configure a plugin** dialog. Choose the **Toolbar Manager** plugin.

## Credits

### Patreon Supporters

_in alphabetical order_

#### Admiral level

- BetaMark
- Casinost
- Mordith - Guardians SE
- Robot10
- wafoxxx

#### Captain level

- Diggz
- jiringgot
- Jimbo
- Kam Solastor
- lazul
- Linux123123
- Lotan
- Lurking StarCpt
- NeonDrip
- NeVaR
- opesoorry

### Testers

- Avaness
- mkaito

### Creators

- avaness - Plugin Loader
- Fred XVI - Racing maps
- Kamikaze - M&M mod
- LTP
- Mordith - Guardians SE
- Mike Dude - Guardians SE
- SwiftyTech - Stargate Dimensions

**Thank you very much for all your support and testing effort!**
## Development

This project is based on the
[Space Engineers client plugin template](https://github.com/viktor-ferenczi/se-client-plugin-template).

### Prerequisites

- [Space Engineers](https://store.steampowered.com/app/244850/Space_Engineers/)
- [Python 3.12](https://python.org) (requires 3.12 or newer)
- [Pulsar](https://github.com/SpaceGT/Pulsar)
- [.NET Framework 4.8.1 Developer Pack](https://dotnet.microsoft.com/en-us/download/dotnet-framework/net481) and
  [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0)

### Setting up a working copy

`Directory.Build.props` finds the game's `Bin64` folder through Steam. To override it, run
`setup.py`, which writes the detected path into `Directory.Build.props.user` (a local, **not
committed** file), or edit that file by hand.

Load the working copy through a Pulsar development folder: start Pulsar with `-sources`, then
add this repository with the Sources button. Building the solution does not deploy the plugin.
It is copied into Pulsar's `Local` plugin folder only if `Pulsar` is set in
`Directory.Build.props.user` or passed as `-p:Pulsar=...`.

### Plugin version

The plugin version lives in `Version.Build.props`, which **is** committed and imported by
`Directory.Build.props`. Bump the version there.

### Building and debugging on .NET 10

- Start the game with the `Interim.exe` Pulsar executable with the `-sources` command line option.
- Click on the Sources button in Pulsar's dialog, then register this repository as a development folder.
- Load `ToolbarManager.xml` (the PluginHub registration) as well.
- Select `Debug` mode and run `Interim.exe`, then attach the debugger.
- Select `Release` mode to test exactly how Pulsar will build and run the plugin on the player's machine.

### Accessing internal, protected and private members in game code

The [Krafs publicizer](https://github.com/krafs/Publicizer) is enabled for `Sandbox.Game` and
`Sandbox.Graphics`, see `ClientPlugin/Tools/GameAssembliesToPublicize.cs` and the `Publicize`
items in `ClientPlugin/ClientPlugin.csproj`.

### In-game checks

`tests/item_groups_check.py` checks the groups in a headless client driven through the
[Remote](https://github.com/CometWorks/remote) plugin: the G menu entry, edit mode, using
the groups with different tools in the inventory and with a block, the staging area and
saving the world. It
prints PASS or FAIL per check. It needs Linux, the game, Pulsar in `~/.config/Pulsar`, a
working copy of Remote with the `cubeBlock` field of the character state
([CometWorks/remote#51](https://github.com/CometWorks/remote/pull/51); `TBM_REMOTE`,
default `~/ws/se/se1/plugins/remote`) and a claimed
test slot of the workspace, see `tests/rig.py`:

```bash
TBM_SLOT=15 uv run --with httpx python tests/item_groups_check.py
```

`tests/editor_modes_check.py` checks TG, Done, tile selection and editing with staging
enabled and disabled, using the same test slot.

`tests/tool_order_check.py` checks button and wheel cycling with lower levels before
higher levels in tile order, then checks that reordering a tool changes the selection.

`tests/separate_groups_check.py` puts a tool group, a block group, a mixed group and
two groups sharing tools on separate slots of two toolbar pages, and checks that each
slot steps only through its own group and continues it where it was.

`tests/greyed_items_check.py` checks that greyed out tools go into a group by drag and
double click in edit mode only, and that the group equips them once they are in the
inventory. It needs a Remote build whose grid gestures reach disabled items.

### Release

- Always make the final release from a RELEASE build and test it before publishing.
- Pulsar compiles the source code on the player's machine, watch out for differences.
