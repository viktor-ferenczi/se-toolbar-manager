using ToolbarManager.Settings;
using ToolbarManager.Settings.Elements;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using ToolbarManager.Settings.Tools;
using VRage.Input;


namespace ToolbarManager
{
    public class Config : INotifyPropertyChanged
    {
        public static readonly Config Default = new Config();
        public static readonly Config Current = ConfigStorage.Load();

        public static void Save()
        {
            ConfigStorage.Save(Current);
        }
        
        #region Options
        
        private bool enableStagingArea = true;
        private bool preventOverwritingSlots = true;
        private bool useCharacterProfilesForBuildCockpit = true;
        
        private bool keepBlockSearchText;
        private string latestBlockSearchText = "";
        
        private bool keepProfileSearchText;
        private string latestProfileSearchText = "";
        
        private Binding blockSearchKey = new Binding(MyKeys.OemPipe);
        
        #endregion
        
        #region User interface
        
        public readonly string Title = "Toolbar Manager";

        public const string StagingAreaDescription = "The staging area allows for the convenient reordering of toolbar items,\nincluding moving them between toolbar pages.\n\nThe staging areas are preserved for the character\nand each block with a toolbar in-memory during gameplay,\nbut NOT SAVED over sessions (world loads) and game restarts.\nPlease save your toolbars into profiles after editing.";

        [Separator("Functionality")] 
        
        [Checkbox(label: "Enable staging area", description: StagingAreaDescription)]
        public bool EnableStagingArea
        {
            get => enableStagingArea;
            set => SetField(ref enableStagingArea, value);
        }
        
        [Checkbox(label: "Prevent overwriting slots", description: "Prevent overwriting the selected or first toolbar slot on double-clicking blocks")]
        public bool PreventOverwritingSlots
        {
            get => preventOverwritingSlots;
            set => SetField(ref preventOverwritingSlots, value);
        }

        [Checkbox(label: "Build cockpit is character", description: "Building from cockpit (Ctrl-G) uses the character profiles")]
        public bool UseCharacterProfilesForBuildCockpit
        {
            get => useCharacterProfilesForBuildCockpit;
            set => SetField(ref useCharacterProfilesForBuildCockpit, value);
        }

        [Separator("Recent searches")]
        
        [Checkbox(description: "Keep the search text between subsequent uses of the block search")]
        public bool KeepBlockSearchText
        {
            get => keepBlockSearchText;
            set => SetField(ref keepBlockSearchText, value);
        }

        // [Textbox(description: "Latest block search text")]
        public string LatestBlockSearchText
        {
            get => latestBlockSearchText;
            set => SetField(ref latestBlockSearchText, value);
        }

        [Checkbox(description: "Keep the search text between subsequent uses of the Profile dialog")]
        public bool KeepProfileSearchText
        {
            get => keepProfileSearchText;
            set => SetField(ref keepProfileSearchText, value);
        }

        // [Textbox(description: "Latest profile search text")]
        public string LatestProfileSearchText
        {
            get => latestProfileSearchText;
            set => SetField(ref latestProfileSearchText, value);
        }

        [Separator("Hotkeys")]
        
        [Keybind(description: "Key to open the G menu and activate block search")]
        public Binding BlockSearchKey
        {
            get => blockSearchKey;
            set => SetField(ref blockSearchKey, value);
        }

        #endregion
        
        #region Property change notification bilerplate

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private bool SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }
        
        #endregion
    }
}