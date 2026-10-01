using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using ECommons;
using BigFishHelper.Windows;

namespace BigFishHelper;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    // RawLog = das von Dalamud injizierte, echte IPluginLog. Log selbst liefert stattdessen einen
    // PluginLogRecorder, der jeden Aufruf unverändert an RawLog weiterreicht (identisches Verhalten
    // in /xllog) UND zusätzlich in PluginLogStore ablegt, damit die eigene Log-Seite im Plugin-Menü
    // (siehe MainWindow.DrawLogPage) die eigenen Log-Zeilen durchsuchbar/filterbar anzeigen kann -
    // ALLE bestehenden Plugin.Log.X(...)-Aufrufstellen im Projekt bleiben dadurch unverändert.
    [PluginService] internal static IPluginLog RawLog { get; private set; } = null!;
    private static PluginLogRecorder? logRecorder;
    internal static IPluginLog Log => logRecorder ??= new PluginLogRecorder(RawLog);
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] internal static ICondition Condition { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;
    [PluginService] internal static INotificationManager NotificationManager { get; private set; } = null!;

    private const string CommandName = "/bigfish";

    public Configuration Configuration { get; }

    // Ablauf der Play-Seite (Teleport, Hinfliegen, Fischer, AutoHook) - startet immer gestoppt.
    public FishingAutomation Automation { get; }

    public readonly WindowSystem WindowSystem = new("BigFishHelper");

    private MainWindow MainWindow { get; }

    public StatusOverlayWindow StatusOverlayWindow { get; }

    public Plugin()
    {
        // Für die NPC-Automatisierung von Sonderwegen (siehe SpecialRoutes.cs) - Talk/SelectString/
        // SelectYesno-Addons klicken.
        ECommonsMain.Init(PluginInterface, this);

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
        ECommonsMain.Dispose();
    }
}
