# Custom block groups

A custom block group, or group for short, is one you make in Toolbar Manager. It puts
several tools, weapons and blocks into one slot of your character toolbar. The game's own
predefined groups, like the heavy armor cube with its slopes and corners (the block tiles
with a `+` in the G menu), are called vanilla block groups here.

Using the slot equips the first available item in tile order. Pressing the slot again
with a tool or weapon of the group in hand moves on to the next item, all tiers of a tool
counting as one. With the default group that is welder, grinder, then hand drill. With a
block of the group in hand, pressing the slot again does what it does on a block's own
slot: it switches between the block's large and small variant. Tools you don't carry,
blocks you haven't unlocked and blocks of DLCs you don't own are skipped. If none of the
items can be used, nothing happens.

A group can hold both sizes of a block, like the large Hinge and the small grid's Hinge
3x3, and modded blocks that share a block pair name. Each of them is a step of its own:
the wheel equips exactly the block listed, whatever grid size the last block had.

Several groups can be on the toolbar at the same time, on any slots and pages, and they
may share items. Each slot steps only through its own group.

When you put the group's item away or pick another slot, the group's slot goes back to
the group's first item, so it always shows the same icon. To have the slot show and equip
the item it was on last instead, turn off **Custom block groups start over** in the
plugin's configuration.

**Vanilla block groups start over** does the same for a slot of a block in a vanilla block
group, like the heavy armor cube. The slot's icon always shows the cube, but the game
equips the variant you used last, say a slope. With the option on, picking the slot again
after leaving it equips the cube, the block its icon shows.

Switching toolbar pages with the item still in hand doesn't count as leaving the slot.

While holding a tool, weapon or block from the group used last, with that group on the
current toolbar page, scroll up to move forward through the group or down to move backward.
The wheel and the slot pick the first available item of each kind in tile order,
regardless of its level, and every block is a step of its own. With a block of the group
in hand the wheel steps through the group instead of its vanilla block group. Scrolling
in menus or with Ctrl, Shift or Alt held keeps the game's normal behavior, except for the
[fitting blocks](fitting-blocks.md).

The G menu lists the groups under **Groups**, by name, each tile showing the group's first
item. An empty group has an empty tile. Drag a group onto the toolbar to use it.
Click its tile to open the editor with that group selected. The **TG** button
at the bottom left of the G menu turns on edit mode, where the
staging area shows the items of the group selected above it:

- Drag tools, weapons and blocks from the G menu into the group, or double click them to
  add them at the end. The block variants on the right can be dragged in too. Greyed out
  items, which you don't have or haven't researched yet, can be added as well, so a group
  can be set up before you get every tier of a tool.
- A vanilla block group, a block tile with a `+` in its corner like the Light Armor Block,
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
  and leave the naming dialog open so you can correct the name. The groups are listed by
  name, ignoring case and with numbers by value (Group 2 before Group 10), and the group
  you just made, renamed or copied stays selected in its new place.
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

[Back to the README](../README.md)
