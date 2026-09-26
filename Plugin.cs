using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using BigFishHelper.Windows;

namespace BigFishHelper;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] internal static ICondition Condition { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;

    private const string CommandName = "/bigfish";

    public Configuration Configuration { get; }

    // Ablauf der Play-Seite (Teleport, Hinfliegen, Fischer, AutoHook) - startet immer gestoppt.
    public FishingAutomation Automation { get; }

    public readonly WindowSystem WindowSystem = new("BigFishHelper");

    private MainWindow MainWindow { get; }

    public StatusOverlayWindow StatusOverlayWindow { get; }

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();

        Automation = new FishingAutomation(this);
        MainWindow = new MainWindow(this);
        WindowSystem.AddWindow(MainWindow);

        StatusOverlayWindow = new StatusOverlayWindow(this);
        WindowSystem.AddWindow(StatusOverlayWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Öffnet/schließt das Big-Fish-Helper-Menü. / Opens/closes the Big Fish Helper menu.",
        });

        PluginInterface.UiBuilder.Draw += DrawUI;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUI;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleMainUI;
    }

    private void OnCommand(string command, string args) => ToggleMainUI();

    private void DrawUI() => WindowSystem.Draw();

    public void ToggleMainUI() => MainWindow.Toggle();

    public void Dispose()
    {
        PluginInterface.UiBuilder.Draw -= DrawUI;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUI;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleMainUI;

        WindowSystem.RemoveAllWindows();
        MainWindow.Dispose();
        StatusOverlayWindow.Dispose();
        Automation.Dispose();
        CommandManager.RemoveHandler(CommandName);
    }
}
