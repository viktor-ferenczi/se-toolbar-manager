using System.Reflection;
using HarmonyLib;
using Sandbox.Game.World;
using Sandbox.Graphics.GUI;
using ToolbarManager.Patches;
using ToolbarManager.Settings;
using ToolbarManager.Settings.Layouts;
using VRage.Plugins;

// Set the assembly version manually if compiled by Pulsar (it won't create what was in AssemblyInfo.cs before)
#if !DEV_BUILD
[assembly: AssemblyVersion("1.6.5.0")]
[assembly: AssemblyFileVersion("1.6.5.0")]
#endif

namespace ToolbarManager;

// ReSharper disable NotAccessedField.Local
// ReSharper disable once UnusedType.Global
// ReSharper disable once ClassNeverInstantiated.Global
public class Plugin : IPlugin
{
    public const string Name = "ToolbarManager";
    public static Plugin Instance { get; private set; }
    private SettingsGenerator settingsGenerator;
    
    public void Init(object gameInstance)
    {
        Instance = this;
        Instance.settingsGenerator = new SettingsGenerator();
            
        var harmony = new Harmony("ToolbarManager");
        harmony.PatchAll(Assembly.GetExecutingAssembly());

        MySession.OnLoading += OnSessionLoading;
    }

    public void Update()
    {
    }

    public void Dispose()
    {
        MySession.OnLoading -= OnSessionLoading;
        Instance = null;
    }

    private void OnSessionLoading()
    {
        MyGuiScreenToolbarConfigBasePatch.OnSessionLoading();
    }

    // ReSharper disable once UnusedMember.Global
    public void OpenConfigDialog()
    {
        Instance.settingsGenerator.SetLayout<Simple>();
        MyGuiSandbox.AddScreen(Instance.settingsGenerator.Dialog);
    }
}