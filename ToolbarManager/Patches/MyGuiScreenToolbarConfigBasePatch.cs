using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using HarmonyLib;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.Gui;
using Sandbox.Game.GUI;
using Sandbox.Game.Screens.Helpers;
using Sandbox.Graphics.GUI;
using ToolbarManager.Gui;
using VRage.Audio;
using VRage.Game;
using VRage.Input;
using VRage.ObjectBuilders.Private;
using VRage.Utils;
using VRageMath;

namespace ToolbarManager.Patches
{
    // ReSharper disable UnusedMember.Local
    // ReSharper disable UnusedType.Global
    // ReSharper disable InconsistentNaming
    [HarmonyPatch(typeof(MyGuiScreenToolbarConfigBase))]
    public static class MyGuiScreenToolbarConfigBasePatch
    {
        private static Config Cfg => Config.Current;
        
        // Keep a set of staging grids, one for each toolbar.
        // This is not persisted, so they are gone when a game is restarted or a new world is loaded.
        private static readonly Dictionary<long, MyGuiControlGrid> StagingAreas = new Dictionary<long, MyGuiControlGrid>(16);

        public static void OnSessionLoading()
        {
            StagingAreas.Clear();
        }
        
        [HarmonyPostfix]
        [HarmonyPatch(nameof(MyGuiScreenToolbarConfigBase.RecreateControls))]
        private static void RecreateControlsPostfix(MyGuiScreenToolbarConfigBase __instance)
        {
            if (!Cfg.EnableStagingArea)
                return;
            
            var toolbarType = MyToolbarComponent.CurrentToolbar?.ToolbarType;

            var button = new MyGuiControlButton
            {
                Text = "TM",
                Name = "OpenToolbarManagerButton",
                VisualStyle = MyGuiControlButtonStyleEnum.Square,
                Position = new Vector2(-0.45f, 0.425f)
            };

            button.SetToolTip("Open Toolbar Manager");

            button.ButtonClicked += OpenProfilesScreen;
            button.Enabled = toolbarType != null && toolbarType != MyToolbarType.None;

            __instance.Elements.Add(button);
            
            // Allow for drag&drop reordering of toolbar items on the currently selected toolbar page
            __instance.m_toolbarControl.m_toolbarItemsGrid.ItemDragged += (sender, eventArgs) => OnToolbarItemDragged(__instance, sender, eventArgs);
            
            // Shortcuts
            var gridBlocksPanel = __instance.m_gridBlocksPanel;
            var toolbarLabel = __instance.m_toolbarLabel;

            // Available space
            var topLeft = gridBlocksPanel.Position - gridBlocksPanel.Size * 0.5f;
            var panelWidth = gridBlocksPanel.Size.X;
            var availableHeight = gridBlocksPanel.Size.Y;
            
            // Sizes
            const float spacing = 0.002f;
            const float cellHeight = 0.07f;
            var stagingHeight = 0.208f + cellHeight * (Cfg.StagingAreaRowCount - 2f);
            var stagingLabelHeight = toolbarLabel.Size.Y;
            var gridBlocksPanelHeight = availableHeight - spacing - stagingLabelHeight - spacing - stagingHeight + /* WHY??? */ 0.058f;
            
            // Reduce the size of the original block grid to make space for the staging area
            gridBlocksPanel.OriginAlign = MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_TOP;
            gridBlocksPanel.Position = topLeft;
            gridBlocksPanel.Size = new Vector2(panelWidth, gridBlocksPanelHeight);
            
            // Staging label
            var stagingLabel = new MyGuiControlLabel();
            stagingLabel.Text = "Staging";
            stagingLabel.AddTooltip(Config.StagingAreaDescription);
            stagingLabel.ColorMask = toolbarLabel.ColorMask;
            stagingLabel.TextScale = toolbarLabel.TextScale;
            stagingLabel.OriginAlign = MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_TOP;
            stagingLabel.Position = topLeft + new Vector2(0f, gridBlocksPanelHeight + spacing) + /* WHY??? */ new Vector2(0.17f, 0.063f);
            stagingLabel.Size = new Vector2(0.15f, stagingLabelHeight);
            
            // Staging grid and scrollable panel
            var entityId = __instance.m_toolbarControl?.m_shownToolbar?.Owner?.EntityId ?? __instance.m_character?.EntityId ?? 0;
            var stagingGrid = StagingAreas.TryGetValue(entityId, out var existingArea) ? existingArea : StagingAreas[entityId] = new MyGuiControlGrid();
            if (stagingGrid.VisualStyle != MyGuiControlGridStyleEnum.Toolbar)
            {
                stagingGrid.VisualStyle = MyGuiControlGridStyleEnum.Toolbar;
                stagingGrid.ColorMask = Vector4.One;
                stagingGrid.ItemBackgroundColorMask = __instance.m_gridBlocks.ColorMask;
                stagingGrid.ColumnsCount = 10;
                stagingGrid.RowsCount = 10;
            }
            
            var stagingPanel = new MyGuiControlScrollablePanel(stagingGrid);
            stagingPanel.BackgroundTexture = MyGuiControlGrid.GetVisualStyle(MyGuiControlGridStyleEnum.ToolsBlocks).BackgroundTexture;
            stagingPanel.ColorMask = __instance.m_gridBlocks.ColorMask;
            stagingPanel.ScrollbarVEnabled = true;
            stagingPanel.ScrolledAreaPadding = new MyGuiBorderThickness(10f / MyGuiConstants.GUI_OPTIMAL_SIZE.X, 10f / MyGuiConstants.GUI_OPTIMAL_SIZE.Y);
            stagingPanel.OriginAlign = MyGuiDrawAlignEnum.HORISONTAL_LEFT_AND_VERTICAL_TOP;
            stagingPanel.Position = stagingLabel.Position + new Vector2(0f, stagingLabelHeight + spacing);
            stagingPanel.Size = new Vector2(panelWidth, stagingHeight - 0.05f);
            
            toolbarLabel.Position += new Vector2(0f, 0.004f);
            
            __instance.AddControl(stagingLabel);
            __instance.AddControl(stagingPanel);
            
            // Must re-insert the controls before toolbarLabel, otherwise they would be rendered in front of the context menu
            __instance.Controls.m_controls.Remove(stagingLabel);
            __instance.Controls.m_controls.Remove(stagingPanel);
            var index = __instance.Controls.m_controls.FindIndex(c => c == toolbarLabel);
            __instance.Controls.m_controls.Insert(index, stagingPanel);
            __instance.Controls.m_controls.Insert(index, stagingLabel);
            
            __instance.m_dragAndDrop.ItemDropped += (sender, eventArgs) => OnStagingGridOnDrop(stagingGrid, eventArgs);
            
            stagingGrid.ItemClicked += OnStagingGridItemClicked;
            stagingGrid.ItemDragged += (sender, eventArgs) => OnStagingGridOnDrag(__instance, sender, eventArgs);
        }

        [HarmonyPrefix]
        [HarmonyPatch(nameof(MyGuiScreenToolbarConfigBase.dragAndDrop_OnDrop))]
        private static bool DragAndDropOnDropPrefix(MyGuiScreenToolbarConfigBase __instance, MyDragAndDropEventArgs eventArgs)
        {
            if (!Cfg.EnableStagingArea)
                return true;
            
            // Handle the case of dropping configured block toolbar items from the staging area into the toolbar (no context menu is required)
            // Most of this code is a verbatim copy from MyGuiScreenToolbarConfigBase.dragAndDrop_OnDrop to avoid having to use a very complex a transpiler patch.
            // Only change is that it handles only the case when an action has already been defined for the terminal block: tb._Action != null
            if (eventArgs.DropTo != null && !__instance.m_toolbarControl.IsToolbarGrid(eventArgs.DragFrom.Grid) && __instance.m_toolbarControl.IsToolbarGrid(eventArgs.DropTo.Grid))
            {
                var userData = (MyGuiScreenToolbarConfigBase.GridItemUserData)eventArgs.Item.UserData;
                var data = userData.ItemData();
                if (data is MyObjectBuilder_ToolbarItemEmpty || data == null)
                    return false;
                if ((data is MyObjectBuilder_ToolbarItemTerminalBlock tb && tb._Action != null) ||
                    (data is MyObjectBuilder_ToolbarItemTerminalGroup tg && tg._Action != null))
                {
                    var toolbarItem = MyToolbarItemFactory.CreateToolbarItem(data);
                    if (toolbarItem is MyToolbarItemActions)
                    {
                        DropGridItemToToolbar(toolbarItem, eventArgs.DropTo.ItemIndex);
                    }
                    else
                    {
                        DropGridItemToToolbar(toolbarItem, eventArgs.DropTo.ItemIndex);
                        if (toolbarItem == null)
                            MyLog.Default.Log(MyLogSeverity.Error, "Inconsistency between grid item and known definitions, the item won't be activated");
                        else if (toolbarItem.WantsToBeActivated)
                            MyToolbarComponent.CurrentToolbar.ActivateItemAtSlot(eventArgs.DropTo.ItemIndex, playActivationSound: false, userActivated: false);
                    }
                    return false;
                }
            }
            return true;
        }

        // We must have a copy of this method, because we don't want to re-enter the Programmable Block arguments
        private static void DropGridItemToToolbar(MyToolbarItem item, int slot)
        {
            // Calling RequestItemParameters is skipped, so parameters are not requested.
            // We keep only the success clause, which actually sets the toolbar slot.
            // Verbatim copy of game code follows:
            
            var currentToolbar = MyToolbarComponent.CurrentToolbar;
            for (var slot1 = 0; slot1 < currentToolbar.SlotCount; ++slot1)
            {
                if (currentToolbar.GetSlotItem(slot1) != null && currentToolbar.GetSlotItem(slot1).Equals(item))
                    currentToolbar.SetItemAtSlot(slot1, null);
            }

            MyGuiAudio.PlaySound(MyGuiSounds.HudItem);
            MyToolbarComponent.CurrentToolbar.SetItemAtSlot(slot, item);
        }

        private static void OnStagingGridOnDrop(MyGuiControlGrid stagingGrid, MyDragAndDropEventArgs eventArgs)
        {
            if (eventArgs.DropTo == null)
                return;
            
            if (eventArgs.DropTo.Grid != stagingGrid) 
                return;
            
            var item = eventArgs.Item;
            
            // The user data needs to be converted to avoid crash on moving items from the staging area to the toolbar
            MyGuiScreenToolbarConfigBase.GridItemUserData userData;
            switch (item.UserData)
            {
                case MyGuiScreenToolbarConfigBase.GridItemUserData gridItemUserData:
                    userData = gridItemUserData;
                    break;
                
                case MyToolbarItem toolbarItem:
                    userData = new MyGuiScreenToolbarConfigBase.GridItemUserData();
                    userData.ItemData = () => toolbarItem.GetObjectBuilder();
                    break;
                
                default:
                    throw new Exception($"Unknown iter.UserData: {item.UserData.GetType().FullName}");
            }

            // Moved inside the staging area?
            if (eventArgs.DragFrom.Grid == stagingGrid)
            {
                // Swap the target item (null if empty) with the dragged one
                // (There is no need to clone the item, it is already a staging one.)
                var previous = stagingGrid.GetItemAt(eventArgs.DropTo.ItemIndex);
                stagingGrid.SetItemAt(eventArgs.DropTo.ItemIndex, item);
                stagingGrid.SetItemAt(eventArgs.DragFrom.ItemIndex, previous);
            }
            else
            {
                // Clone the item to remove the toolbar number and current state, which is not relevant in the staging area
                var clone = new MyGuiGridItem(item.Icons, item.SubIcon, item.ToolTip, userData);
                clone.OverlayText = item.OverlayText;
            
                // Remove any existing item, so identical ones are "moved" and not copied all the time (reduces clutter)
                for (var i = 0; i < stagingGrid.m_items.Count; i++)
                {
                    if (clone.IsSameBlock(stagingGrid.m_items[i]))
                    {
                        stagingGrid.SetItemAt(i, null);
                        break;
                    }
                }
                
                // Place the item in the staging area, overwrite the cell if it has been occupied
                stagingGrid.SetItemAt(eventArgs.DropTo.ItemIndex, clone);
            }
        }

        private static bool IsSameBlock(this MyGuiGridItem item, MyGuiGridItem other)
        {
            if (item == null || other == null)
            {
                return false;
            }
            
            if (item == other) 
                return true;

            if (item.UserData is MyGuiScreenToolbarConfigBase.GridItemUserData itemUserData &&
                other.UserData is MyGuiScreenToolbarConfigBase.GridItemUserData otherUserData)
            {
                var id = itemUserData.ItemData();
                var od = otherUserData.ItemData();
                if (id == od)
                    return true;

                // Comparing with XML serialization
                // (slow, but compares deep and by value regardless of toolbar item builder type)
                var ims = new MemoryStream();
                MyObjectBuilderSerializerKeen.Serializer.Serialize(ims, id);
                var oms = new MemoryStream();
                MyObjectBuilderSerializerKeen.Serializer.Serialize(oms, od);
                return ims.ToArray().SequenceEqual(oms.ToArray());
            }

            return false;
        }

        private static void OnStagingGridItemClicked(MyGuiControlGrid stagingGrid, MyGuiControlGrid.EventArgs eventArgs)
        {
            if (eventArgs.Button == MySharedButtonsEnum.Secondary)
            {
                stagingGrid.SetItemAt(eventArgs.ItemIndex, null);
            }
        }

        private static void OnStagingGridOnDrag(MyGuiScreenToolbarConfigBase myGuiScreenToolbarConfigBase, MyGuiControlGrid stagingGrid, MyGuiControlGrid.EventArgs eventArgs)
        {
            var item = stagingGrid.GetItemAt(eventArgs.ItemIndex);
            if (item == null || !item.Enabled) 
                return;
            
            var info = new MyDragAndDropInfo();
            info.Grid = stagingGrid;
            info.ItemIndex = eventArgs.ItemIndex;
            myGuiScreenToolbarConfigBase.m_dragAndDrop.StartDragging(MyDropHandleType.MouseRelease, eventArgs.Button, item, info, false);
            
            stagingGrid.HideToolTip();
        }

        private static void OnToolbarItemDragged(MyGuiScreenToolbarConfigBase myGuiScreenToolbarConfigBase, MyGuiControlGrid sender, MyGuiControlGrid.EventArgs eventArgs)
        {
            // Disallow dragging the very last "hand" slot (it would be meaningless and would crash)
            if (eventArgs.ItemIndex == 9)
                return;

            myGuiScreenToolbarConfigBase.StartDragging(MyDropHandleType.MouseRelease, sender, ref eventArgs);
        }

        private static void OpenProfilesScreen(MyGuiControlButton obj)
        {
            var currentToolbar = MyToolbarComponent.CurrentToolbar;
            if (currentToolbar == null)
                return;

            var profilesDialog = new ProfilesDialog(currentToolbar);
            MyGuiSandbox.AddScreen(profilesDialog);
            profilesDialog.Closed += OnProfilesDialogClosed;
        }

        private static void OnProfilesDialogClosed(MyGuiScreenBase dialog, bool isUnloading)
        {
            if (Config.Current.KeepProfileSearchText)
                Config.Save();
        }

        [HarmonyPrefix]
        [HarmonyPatch(nameof(MyGuiScreenToolbarConfigBase.AddGridItemToToolbar))]
        private static bool AddGridItemToToolbarPrefix()
        {
            if (!Cfg.PreventOverwritingSlots)
                return true;

            // SE overwrites the first block of ship toolbars or the selected block of character toolbars on
            // double-clicking grid blocks. We disable this here and require the player to explicitly drag&drop
            // blocks to overwrite existing, non-empty toolbar slots. It is way less error prone.

            // The culprit is this code line:
            // int slot1 = currentToolbar.SelectedSlot ?? 0;

            // Prefix condition to disallow this case (avoid having to use a transpiler):
            var currentToolbar = MyToolbarComponent.CurrentToolbar;
            var slotCount = currentToolbar.SlotCount;
            for (var slot = 0; slot < slotCount; ++slot)
            {
                if (currentToolbar.GetSlotItem(slot) == null)
                {
                    // There is at least one free slot, which will be chosen as target by the original implementation
                    return true;
                }
            }

            // There is no free toolbar slot on the currently selected page.
            // Disallow overwriting the selected or first slot.
            MyGuiAudio.PlaySound(MyGuiSounds.HudUnable);
            return false;
        }
    }
}