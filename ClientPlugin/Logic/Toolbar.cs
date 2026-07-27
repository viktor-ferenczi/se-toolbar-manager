using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using LitJson;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Cube;
using Sandbox.Game.Screens.Helpers;
using VRage.Game;
using VRage.Game.Entity;
using VRage.ObjectBuilders;

namespace ClientPlugin.Logic;

public class Toolbar
{
    private readonly MyToolbarType type;
    private readonly List<Slot> slots = new List<Slot>();

    public Toolbar(MyToolbar toolbar)
    {
        if (toolbar == null)
            return;

        type = toolbar.ToolbarType;

        for (var index = 0; index < toolbar.ItemCount; index++)
            slots.Add(new Slot(index, toolbar.GetItemAtIndex(index)));
    }

    private Toolbar(MyObjectBuilder_Toolbar toolbarBuilder)
    {
        if (toolbarBuilder == null)
            return;

        type = toolbarBuilder.ToolbarType;

        for (var index = 0; index < toolbarBuilder.Slots.Count; index++)
        {
            slots.Add(new Slot(index, toolbarBuilder.Slots[index].Data));
        }
    }

    public void Clear()
    {
        slots.Clear();
    }

    public static Toolbar Read(string path)
    {
        MyObjectBuilderSerializer.DeserializeXML<MyObjectBuilder_Toolbar>(path, out var toolbarBuilder);
        return new Toolbar(toolbarBuilder);
    }

    public void Write(string path)
    {
        var builder = new MyObjectBuilder_Toolbar
        {
            ToolbarType = type
        };

        foreach (var slot in slots)
        {
            builder.Slots.Add(new MyObjectBuilder_Toolbar.Slot
            {
                Index = slot.Index,
                Data = slot.Builder
            });
        }

        MyObjectBuilderSerializer.SerializeXML(path, false, builder);
    }

    public void WriteJson(string path)
    {
        using (var stream = new StreamWriter(path))
            WriteJson(stream);
    }

    private void WriteJson(StreamWriter stream)
    {
        var d = new JsonData
        {
            ["ToolbarType"] = type.ToString()
        };

        var ss = new JsonData();
        ss.SetJsonType(JsonType.Array);
        d["Slots"] = ss;

        foreach (var slot in slots)
        {
            var s = new JsonData
            {
                ["Index"] = slot.Index,
                ["Page"] = slot.Page,
                ["Number"] = slot.Number,
                ["IsEmpty"] = slot.IsEmpty
            };

            if (slot.Builder == null)
            {
                s["Type"] = "";
                s["Name"] = "";
            }
            else
            {
                s["Type"] = slot.Builder.GetType().Name;
                switch (slot.Builder)
                {
                    case MyObjectBuilder_ToolbarItemDefinition itemBuilder:
                        s["Name"] = itemBuilder.DefinitionId.ToString();
                        break;

                    case MyObjectBuilder_ToolbarItemTerminalGroup groupBuilder:
                        s["Name"] = groupBuilder.GroupName;
                        break;

                    case MyObjectBuilder_ToolbarItemTerminalBlock blockBuilder:
                        if (MyEntities.TryGetEntityById(blockBuilder.BlockEntityId, out var block))
                            s["Name"] = block.DisplayNameText;
                        else
                            s["Name"] = "?";
                        break;

                    default:
                        s["Name"] = "??";
                        break;
                }
            }

            ss.Add(s);
        }

        var writer = new JsonWriter
        {
            PrettyPrint = true,
        };
        d.ToJson(writer);
        stream.Write(writer.ToString());
    }

    public void Set(MyToolbar toolbar)
    {
        var count = Math.Min(slots.Count, toolbar.ItemCount);
        for (var index = 0; index < count; index++)
            slots[index].Set(toolbar);
    }

    public void Merge(MyToolbar toolbar)
    {
        var count = Math.Min(slots.Count, toolbar.ItemCount);
        for (var index = 0; index < count; index++)
            slots[index].Merge(toolbar);
    }

    public void Dissociate(MyToolbar currentToolbar)
    {
        if (!(currentToolbar.Owner is MyTerminalBlock))
            return;

        var utf8 = Encoding.UTF8;
        var sha1 = SHA1.Create();
        foreach (var slot in slots)
        {
            switch (slot.Builder)
            {
                case MyObjectBuilder_ToolbarItemTerminalBlock blockBuilder:
                    if (MyEntities.TryGetEntityById(blockBuilder.BlockEntityId, out var block))
                        blockBuilder.BlockEntityId = CalculateDisplayNameTextChecksum(utf8, sha1, block);
                    break;

                case MyObjectBuilder_ToolbarItemTerminalGroup groupBuilder:
                    groupBuilder.BlockEntityId = 0;
                    break;
            }
        }
    }

    public void Associate(MyToolbar currentToolbar)
    {
        if (!(currentToolbar.Owner is MyTerminalBlock terminalBlock))
            return;

        var physicalGroup = MyCubeGridGroups.Static.Physical.GetGroup(terminalBlock.CubeGrid);
        if (physicalGroup == null)
            return;

        var blockSlots = new Dictionary<long, List<Slot>>();
        foreach (var slot in slots)
        {
            switch (slot.Builder)
            {
                case MyObjectBuilder_ToolbarItemTerminalBlock blockBuilder:
                    if (blockSlots.TryGetValue(blockBuilder.BlockEntityId, out var blockSlotList))
                        blockSlotList.Add(slot);
                    else
                        blockSlots[blockBuilder.BlockEntityId] = new List<Slot> { slot };
                    break;
            }
        }

        var utf8 = Encoding.UTF8;
        var sha1 = SHA1.Create();
        foreach (var physicalNode in physicalGroup.Nodes)
        {
            var grid = physicalNode.NodeData;
            foreach (var block in grid.GetFatBlocks<MyTerminalBlock>())
            {
                var checksum = CalculateDisplayNameTextChecksum(utf8, sha1, block);
                if (blockSlots.TryGetValue(checksum, out var blockSlotList))
                {
                    foreach (var slot in blockSlotList)
                    {
                        if (slot.Builder is MyObjectBuilder_ToolbarItemTerminalBlock blockBuilder)
                            blockBuilder.BlockEntityId = block.EntityId;
                    }
                }
            }
        }

        foreach (var slot in slots)
        {
            if (slot.Builder is MyObjectBuilder_ToolbarItemTerminalGroup groupBuilder)
                groupBuilder.BlockEntityId = terminalBlock.EntityId;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long CalculateDisplayNameTextChecksum(Encoding encoding, HashAlgorithm hashAlgorithm, MyEntity block)
    {
        var sha1Hash = hashAlgorithm.ComputeHash(encoding.GetBytes(block.DisplayNameText));
        var checksum = -Math.Abs(BitConverter.ToInt64(sha1Hash, 0));
        return checksum;
    }
}
