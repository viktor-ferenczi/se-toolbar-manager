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

Toolbar Manager saves, loads and edits character and block toolbars. Load your usual
character toolbar into any world, multiplayer included, and build with the slots you
already know by heart. Or fix the broken toolbars of a ship in survival from the profiles
you saved in your creative design world.

It works offline and in online multiplayer without any support from the server. Toolbar
profiles are saved on your computer, nothing goes to a server.

### Saving and loading toolbars (profiles)

The **G menu** has new buttons at the bottom left to manage saved toolbar profiles:

- **New** saves the open toolbar as a new profile.
- **Load** replaces the open toolbar with the selected profile.
- **Merge** loads only the used slots of the profile and leaves the other slots as they are.
- **Rename** and **Delete** rename and delete profiles.

Where the profiles are stored and how their backups work: [Profile folders and backups](Docs/profiles.md).

### Editing toolbars

The G menu gets a **Staging** area, where you can reorder toolbar items and move them
between toolbar pages.

The staging areas of the character and of each block with a toolbar are kept in memory
while you play, but they are **not saved** over world loads and game restarts. Save your
toolbars into profiles after editing. The staging area can be turned off in the plugin's
configuration.

### Picking the block you face

Press `Ctrl+G` with a block under the crosshair, up to 100 m away, to take it into your
hand as the block to place. It is the same block exactly: its type and subtype in the
grid's size and turned the same way. Its color and skin become the color and skin of the
blocks you place; turn off **Also pick color** or **Also pick skin** in the plugin's
configuration to keep your own. The game's color picker (`Shift+P`) takes the color and
the skin without the block. It works with a block, a tool or nothing in hand, and in
survival too: you get the block to place, not its components. Blocks you haven't
researched or whose DLC you don't own are refused the way their toolbar slot would be.

The key works on foot, with the jetpack and in the spectator camera. It does nothing in a
seat, where `Ctrl+G` is the game's build mode. It can be changed or turned off in the
plugin's configuration. There is no gamepad binding.

### Quick block selection

Press the `BACKSLASH` or `PIPE` key (on an English keyboard) to open the G menu with the
block search active. The search finds blocks mainly by their capital letters and digits:

- `AB` **Armor Blocks**
- `PB` **Programmable Block**
- `SK` **Survival Kit**
- `AT` **Atmospheric Thrusters**
- `LHT` **Large Hydrogen Thrusters**
- `211` **All 2x1x1 armor blocks**

Add the lower case letters that follow a capital letter to narrow down an ambiguous search:

- `PH` **Parachute Hatch** and **Point Hand**
- `PHat` **Parachute Hatch** only

The order of characters must match exactly. Lower case characters and
space are skipped after a matching upper case character or digit.

Several search patterns separated by spaces are not supported. The key can be changed in
the plugin's configuration.

### Groups

A custom block group, or group for short, puts several tools, weapons and blocks into one
slot of your character toolbar. Pressing the slot again or turning the mouse wheel steps
through the group. You make groups in the G menu's group editor (the **TG** button) and
drag them onto the toolbar from the **Groups** category. How the slot, the wheel and the
editor work: [Custom block groups](Docs/groups.md).

### Fitting blocks

With a block of a group in hand and the placement box on a grid, hold Shift and turn the
mouse wheel to step through only the blocks of the group that fit there, each turned the
way it fits. Pressing Shift alone picks a fitting block right away. The extra mouse
buttons step forward and back. How a block is found to fit: [Fitting blocks](Docs/fitting-blocks.md).

### Quick selector

Hold Shift+Ctrl with an item of a group in hand to see all items of the group on a panel
and click one to equip it. The blocks that fit the aimed position are highlighted.
Details: [Quick selector](Docs/quick-selector.md).

### Vanilla block groups

The fitting blocks keys and the quick selector also work on the game's own block variants,
like the heavy armor cube with its slopes and corners, without a group of your own.
Details: [Vanilla block groups](Docs/vanilla-block-groups.md).

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

Building, debugging and releasing the plugin: [Development](Docs/development.md). The
in-game and offline checks: [Checks](Docs/checks.md).
