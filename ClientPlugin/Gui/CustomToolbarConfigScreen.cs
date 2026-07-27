using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using HarmonyLib;
using Sandbox.Game.Entities;
using Sandbox.Game.Gui;
using Sandbox.Game.Screens;
using Sandbox.Game.Screens.Helpers;
using Sandbox.Graphics.GUI;
using VRage.Game;
using VRage.Input;
using VRageMath;


namespace ClientPlugin.Gui;

[SuppressMessage("ReSharper", "UnusedMember.Global")]
public class CustomToolbarConfigScreen : MyGuiScreenCubeBuilder
{
    private static readonly MethodInfo SearchItemTextboxTextChangedMethod = AccessTools.Method(typeof(MyGuiScreenToolbarConfigBase), "OnSearchItemTexboxTextChanged");
    private static readonly FieldInfo FramesBeforeSearchEnabledField = AccessTools.Field(typeof(MyGuiScreenToolbarConfigBase), "m_framesBeforeSearchEnabled");

    private readonly CustomSearchCondition customSearchCondition = new CustomSearchCondition();
    private MyGuiControlLabel searchInfoLabel;

    public CustomToolbarConfigScreen(int scrollOffset = 0, MyCubeBlock owner = null, int? gamepadSlot = null) : base(scrollOffset, owner, gamepadSlot)
    {
    }

    public CustomToolbarConfigScreen(int scrollOffset, MyCubeBlock owner, string selectedPage, bool hideOtherPages, int? gamepadSlot = null) : base(scrollOffset, owner, selectedPage, hideOtherPages, gamepadSlot)
    {
    }

    protected override void OnClosed()
    {
        if (Config.Current.KeepBlockSearchText)
        {
            Config.Current.LatestBlockSearchText = SearchText;
            Config.Save();
        }

        base.OnClosed();
    }

    public override void LoadContent()
    {
        base.LoadContent();

        if (!Config.Current.KeepBlockSearchText)
            return;

        var searchText = Config.Current.LatestBlockSearchText.Trim();
        if (searchText.Length == 0)
            return;

        SetSearchText(Config.Current.LatestBlockSearchText.Trim());
    }

    private void SetSearchText(string text)
    {
        var framesBeforeSearchEnabledField = (int)FramesBeforeSearchEnabledField.GetValue(this);
        if (framesBeforeSearchEnabledField > 0)
        {
            MyEntities.InvokeLater(() => SetSearchText(text));
        }

        m_searchBox.SearchText = text;
        // m_searchBox.TextBox.MoveCarriageToEnd();
        m_searchBox.TextBox.SelectAll();

        // KEEN!!! This method works only if m_framesBeforeSearchEnabled <= 0
        SearchItemTextboxTextChangedMethod.Invoke(this, new object[] { text });
    }

    private string SearchText => m_searchBox.SearchText ?? "";

    protected override void AddToolsAndAnimations(IMySearchCondition searchCondition)
    {
        if (searchCondition == m_nameSearchCondition)
        {
            customSearchCondition.SearchName = m_searchBox.TextBox.Text;
            base.AddToolsAndAnimations(customSearchCondition);
            return;
        }

        base.AddToolsAndAnimations(searchCondition);
    }

    protected override void UpdateGridBlocksBySearchCondition(IMySearchCondition searchCondition)
    {
        if (searchCondition == m_nameSearchCondition)
        {
            customSearchCondition.SearchName = m_searchBox.TextBox.Text;
            base.UpdateGridBlocksBySearchCondition(customSearchCondition);
            return;
        }

        base.UpdateGridBlocksBySearchCondition(searchCondition);
    }

    public override void HandleInput(bool receivedFocusInThisUpdate)
    {
        if (MyInput.Static.IsNewKeyPressed(MyKeys.Enter))
        {
            LoadSelectedItem();
            return;
        }

        base.HandleInput(receivedFocusInThisUpdate);
    }

    private void LoadSelectedItem()
    {
        if (m_gridBlocks.SelectedIndex == null)
            return;

        var selectedItem = m_gridBlocks.GetItemAt(m_gridBlocks.SelectedIndex ?? 0);
        if (!(selectedItem?.UserData is GridItemUserData userData))
            return;

        var toolbarItemBuilder = userData.ItemData();
        if (toolbarItemBuilder is MyObjectBuilder_ToolbarItemEmpty)
            return;

        var toolbarItem = MyToolbarItemFactory.CreateToolbarItem(userData.ItemData());
        if (toolbarItem is MyToolbarItemActions && MyInput.Static.IsJoystickLastUsed)
            return;

        this.AddGridItemToToolbar(toolbarItemBuilder);

        CloseScreen();
    }

    public override void RecreateControls(bool contructor)
    {
        base.RecreateControls(contructor);

        searchInfoLabel = new MyGuiControlLabel(
            new Vector2(m_searchBox.PositionX + m_searchBox.Size.X * 0.5f + 0.02f, m_searchBox.PositionY + 0.005f),
            new Vector2(0.2f, m_searchBox.Size.Y))
        {
            Font = m_searchBox.TextBox.TextFont,
            Text = "Quick Search Mode"
        };

        Controls.Add(searchInfoLabel);
    }
}
