using Sandbox.Definitions;
using Sandbox.Game.Screens.Helpers;
using VRage.Game;

namespace ClientPlugin.Logic;

public class Slot
{
    public readonly int Index;
    public MyObjectBuilder_ToolbarItem Builder { get; private set; }
    public MyDefinitionId Id => Builder?.GetId() ?? default(MyDefinitionId);
    public int Page => Index / 9;
    public int Number => Index % 9;
    public bool IsEmpty => Builder == null;

    public Slot(int index)
    {
        Index = index;
    }

    public Slot(int index, MyDefinitionId id)
    {
        Index = index;

        if (!MyDefinitionManager.Static.TryGetDefinition(id, out MyDefinitionBase definitionBase))
            return;

        Builder = MyToolbarItemFactory.ObjectBuilderFromDefinition(definitionBase);
    }

    public Slot(int index, MyToolbarItem item)
    {
        Index = index;

        if (item == null)
            return;

        Builder = item.GetObjectBuilder();
    }

    public Slot(int index, MyObjectBuilder_ToolbarItem itemBuilder)
    {
        Index = index;

        if (itemBuilder == null)
            return;

        Builder = itemBuilder;
    }

    public Slot(int index, MyToolbarItemDefinition itemDefinition)
    {
        Index = index;

        if (itemDefinition == null)
            return;

        Builder = itemDefinition.GetObjectBuilder();
    }

    public void Clear()
    {
        Builder = null;
    }

    public void Get(MyToolbar toolbar)
    {
        var toolbarItem = toolbar.GetItemAtIndex(Index);
        if (toolbarItem == null)
        {
            Clear();
            return;
        }

        Builder = toolbarItem.GetObjectBuilder();
    }

    public void Set(MyToolbar toolbar)
    {
        if (IsEmpty)
        {
            toolbar.SetItemAtIndex(Index, null);
            return;
        }

        var toolbarItem = MyToolbarItemFactory.CreateToolbarItem(Builder);
        toolbar.SetItemAtIndex(Index, toolbarItem);
    }

    public void Merge(MyToolbar toolbar)
    {
        if (IsEmpty)
            return;

        Set(toolbar);
    }

    public void Activate(MyToolbar toolbar)
    {
        if (toolbar.CurrentPage != Page)
            toolbar.SwitchToPage(Page);

        toolbar.ActivateItemAtIndex(Index);
    }
}
