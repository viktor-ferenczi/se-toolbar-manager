# Checks

`tests/item_groups_check.py` checks the groups in a headless client driven through the
[Remote](https://github.com/CometWorks/remote) plugin: the G menu entry, edit mode, using
the groups with different tools in the inventory and with a block, the staging area and
saving the world. It
prints PASS or FAIL per check. It needs Linux, the game, Pulsar in `~/.config/Pulsar` (or `TBM_PULSAR_TEMPLATE`), a
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

`tests/group_reset_check.py` checks that a custom block group slot and a slot of a block
in a vanilla block group start over once they are left by picking another slot or putting the block
away, and continue where they were with the options off (`TBM_RESET_CUSTOM=0`,
`TBM_RESET_VANILLA=0`). It also checks both options in the plugin settings.

`tests/same_pair_check.py` checks a group with both sizes of a block and with two modded
blocks sharing a `BlockPairName` (the mod in `tests/same_pair_mod`): the wheel steps through
all of them and past them, the slot switches the size, and it still works after reloading
the world. It writes the group file and loads the mod itself, in a creative world.

`tests/group_order_check.py` checks the order of the group editor's drop-down and of the
Groups category after a new group, a rename and a duplicate, the selection after each, and
the tiles of a group with blocks and of an empty group.

`tests/delete_scroll_check.py` scrolls a long group in the group editor and removes blocks
by right click at the top, middle and bottom of the rows in view and at the end of the
list. It prints the scroll offset before and after each removal: the offset stays, and
only the removal that leaves no block in view moves it up one row.

`tests/greyed_items_check.py` checks that greyed out tools go into a group by drag and
double click in edit mode only, and that the group equips them once they are in the
inventory. It needs a Remote build whose grid gestures reach disabled items.

`tests/game_groups_check.py` checks the fitting blocks keys and the quick selector with a
heavy armor block from an ordinary toolbar slot, and Shift on blocks without variants.
The game's own wheel and slot must keep working, and with the option off the keys must
leave the block alone.

`tests/faced_block_check.py` checks picking the faced block with `Ctrl+G`: the block in
hand, its size, skin and placed orientation, in symmetry mode too, and without creative
rights. With `--color` it also checks the **Also pick color** option.

`tests/block_variants_check.py` adds the light and heavy armor vanilla block groups to a
group and checks that the slot and the wheel step through the group's own order, also
after a restart.

`tests/grid_aim_check.py` steps a group of light armor blocks while facing a grid and
checks that a click still places the new block on it.

`tests/staging_info_check.py` checks that a click in the staging area and in the group
editor's grid updates the info panel on the right.

`tests/fit_check.py` checks the fitting blocks keys, the wheel and the quick press on
small static grids with gaps, and `tests/quick_selector_check.py` the quick selector on
the same grids. Both use test slot 16.

`tests/BlockFit.Tests` holds the offline counterparts of `tests/fit_check.py`. They need
no game, only the vanilla block definitions in `tests/BlockFit.Tests/blocks.json`, which
`tests/fit_fixture.py` extracts from the installed game:

```bash
dotnet run --project tests/BlockFit.Tests
```

[Back to the README](../README.md)
