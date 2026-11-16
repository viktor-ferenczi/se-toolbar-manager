# Fitting blocks

With a block of a [group](groups.md) in hand and the placement box on a grid, hold Shift and turn the
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
there. When nothing fits, the quick press stays silent, unless the group has no block
of the grid's size at all.

While Shift stays held, the quick press is done again whenever the placement box moves
to another cell or another face of a block. So you can hold Shift and move the aim until
the block sits right, without pressing Shift again. These repeats are always silent.

A block fits when its face matches the face of the block you aim at. The other blocks
around it rank the candidates if they are of the same kind: armor with armor, beams with
beams, and so on for the blocks of a vanilla block group. Other neighbors don't count, so a
beam can continue past a wall instead of being closed off to hug it. A face that would
stick out where the neighbor has none, like a full face against the slanted side of a
slope, counts more than leaving part of the neighbor's face uncovered. Armor closes
surfaces, so for armor both count on every side; other blocks, like a half beam in a frame
of full beams, only must not stick out past their neighbors. The blocks of the group that
match best are the ones that fit. The shapes come from the blocks' mount points.

A gap in a row is the exception: aim at a block that has the same block, turned the same
way, on the other side of the gap, and that block fits the gap too, whatever else is
around. So a rounded armor block goes back between its two neighbors even with a half
armor block beside the row, where the beams of the group would otherwise match better.

Blocks with conveyor or upgrade ports fit by their ports instead: every port of a
neighbor pointing into the block has to be met by a port of the same kind (large
conveyor, small conveyor or upgrade), and at least one port has to connect. A conveyor
block (tube, junction, sorter or connector) also may not have a port running into a block
that has no such port there, so a tube is not offered against the middle of another tube.
Other blocks may: a thruster, tank or machine can leave a port against a beam or wall, as
the game allows, so a large hydrogen thruster fits under a tank's port even when beams
frame the space. An upgrade module goes on a refinery or assembler with its ports on the
machine's upgrade ports, with as many of them connected as possible. Ports of a kind
the block doesn't have are left alone: a tube may run past a machine's upgrade ports, and
armor may cover them. A block without ports fits an open conveyor end only as a cap of the
port's size, mounted on that end alone: the small cap on a small port, the medium cap on
the large port of a small grid's medium conveyors.

The plain, reinforced and industrial tubes all connect to each other, but a line takes
the tubes of its own look when the group has them: a reinforced tube continues with the
reinforced tubes, not with the plain or industrial ones. Junctions, sorters, connectors
and the plain caps go with any look. A reinforced tube also turns its open side the way
the line has it. The industrial pipes meet any other block with their pipe end, its wide
end on that block: the pipe end is offered there instead of the straight pipes, also on
a plain or reinforced tube, and it is not offered between two industrial pipes. The
curved pipe and the pipe junctions have no end piece, so they fit as before.

Fitting only narrows the group down. The blocks that fit keep the order they have in the
group.

This works on small grids too, with small and medium conveyors, and for blocks larger
than one cube, placed the way the game places them against the aimed face. With a block in
hand that has no variant of the aimed grid's size, the fitting blocks come from the group's
blocks of that size. Modded blocks
work the same way when their models name their ports like the game's blocks do
(`detector_conveyor`, `detector_conveyor_small`, `detector_upgrade`) and their
definitions have mount points; armor counts as one kind if it is built from the game's
armor shapes, other blocks by their vanilla block group.

The plugin's configuration can change the wheel's modifier key to Ctrl or Alt or turn it
off, turn the quick press off, assign the next and previous actions to other mouse buttons, and add keyboard keys
for them. When the middle button is assigned, a plain middle button press that selects a
block doesn't paint; otherwise it paints as in the game.

[Back to the README](../README.md)
