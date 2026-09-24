using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Interface;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Windowing;
using Dalamud.Utility;
using Dalamud.Bindings.ImGui;

namespace BigFishHelper.Windows;

/// <summary>
/// Hauptmenü - Aufbau identisch zum Explorer's Codex: eigene Kopfzeile (Ziehen, Doppelklick zum
/// Einklappen, Einklappen-/Schließen-Knopf), Icon-Leiste links (Einstellungen, Plugins, Über) und
/// daneben der jeweilige Inhalt. Feste Größe, öffnet immer in voller Größe.
/// </summary>
public class MainWindow : Window
{
    private enum RailPage
    {
        Play,
        Settings,
        Dependencies,
        About,
    }

    private const string PluginDisplayName = "Big Fish Helper";
    private const string GitHubUrl = "https://github.com/stoni89/big-fish-helper";

    private readonly Plugin plugin;
    private static readonly string VersionText = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "?";

    private static readonly string IconPath =
        Path.Combine(Plugin.PluginInterface.AssemblyLocation.DirectoryName!, "Data", "icon.png");

    private RailPage railPage = RailPage.Settings;
    private bool collapsed;
    private bool collapsedLastFrame;
    private Vector2 expandedSize = new(746f, 960f);

    // Rechter Randabstand für jeden Tab-Inhalt - dieselbe Größe wie der Abstand von der vertikalen
    // Trennlinie zu den Tab-Inhalten.
    private const float ContentRightMargin = 26f;

    // Die Scrollbar der Tab-Inhalte sitzt am rechten Rand des Inhaltsbereichs - um so viel näher an
    // den Fensterrand gerückt.
    private const float ScrollbarShiftRight = 12f;

    private const float HeaderBandHeightExpanded = 38f;
    private const float HeaderBandHeightCollapsed = 16f;
    private const float TitleScaleExpanded = 1.6f;
    private const float TitleScaleCollapsed = 0.9f;
    private const float IconSizeScale = 1.8f;
    private const float WindowPaddingY = 12f;
    private const float WindowPaddingYCollapsed = 2f;
    private const float CollapsedHeight = HeaderBandHeightCollapsed + WindowPaddingYCollapsed * 2f;

    private static readonly WindowSizeConstraints ExpandedSizeConstraints = new()
    {
        MinimumSize = new Vector2(520 + ContentRightMargin, 930),
        MaximumSize = new Vector2(1100 + ContentRightMargin, 1050),
    };

    private static readonly WindowSizeConstraints CollapsedSizeConstraints = new()
    {
        MinimumSize = new Vector2(420, CollapsedHeight),
        MaximumSize = new Vector2(1100, CollapsedHeight),
    };


    private const ImGuiWindowFlags BaseFlags =
        ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoResize;

    public MainWindow(Plugin plugin) : base($"{PluginDisplayName} (v{VersionText})##BigFishHelper", BaseFlags)
    {
        this.plugin = plugin;

        Size = expandedSize;
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = ExpandedSizeConstraints;
    }

    public void Dispose() { }

    // Bei jedem Öffnen in voller Breite und Höhe (siehe PreDraw).
    private bool openAtFullSize = true;

    public override void OnOpen()
    {
        openAtFullSize = true;
        collapsed = false;
    }

    /// <summary>Nicht am Titelbildschirm oder während eines Ladebildschirms anzeigen.</summary>
    public override bool DrawConditions() =>
        Plugin.ClientState.IsLoggedIn && !Plugin.Condition[ConditionFlag.BetweenAreas] && !Plugin.Condition[ConditionFlag.BetweenAreas51];

    /// <summary>
    /// Fenstergröße beim Öffnen/Ein-/Ausklappen setzen - muss vor ImGui.Begin() passieren, daher
    /// hier über Size/SizeCondition (Dalamud ruft danach selbst SetNextWindowSize auf).
    /// </summary>
    public override void PreDraw()
    {
        SizeConstraints = collapsed ? CollapsedSizeConstraints : ExpandedSizeConstraints;
        Flags = BaseFlags;

        if (openAtFullSize && !collapsed)
        {
            // Maximalgröße, aber nie größer als der sichtbare Bildschirmbereich.
            openAtFullSize = false;
            var workSize = ImGui.GetMainViewport().WorkSize;
            var max = ExpandedSizeConstraints.MaximumSize;
            expandedSize = new Vector2(MathF.Min(max.X, workSize.X), MathF.Min(max.Y, workSize.Y));
            Size = expandedSize;
            SizeCondition = ImGuiCond.Always;
        }
        else if (!collapsed && collapsedLastFrame)
        {
            Size = expandedSize;
            SizeCondition = ImGuiCond.Always;
        }
        else if (collapsed && !collapsedLastFrame)
        {
            Size = new Vector2(expandedSize.X, CollapsedHeight);
            SizeCondition = ImGuiCond.Always;
        }
        else
        {
            SizeCondition = ImGuiCond.FirstUseEver;
        }

        collapsedLastFrame = collapsed;

        // ImGuis Standard-WindowMinSize (32x32) würde das Einklappen auf CollapsedHeight verhindern.
        ImGui.PushStyleVar(ImGuiStyleVar.WindowMinSize, new Vector2(1f, 1f));
        ModernUi.PushStyle(new Vector2(12f, collapsed ? WindowPaddingYCollapsed : WindowPaddingY));
    }

    public override void PostDraw()
    {
        ModernUi.PopStyle();
        ImGui.PopStyleVar();
    }

    private static IFontHandle? titleFontHandle;

    /// <summary>In nativer Pixelgröße gebaute Titelschrift (statt verschwommen hochskaliert).</summary>
    private static IFontHandle GetTitleFontHandle()
    {
        titleFontHandle ??= Plugin.PluginInterface.UiBuilder.FontAtlas.NewDelegateFontHandle(e => e.OnPreBuild(tk =>
            tk.AddDalamudAssetFont(Dalamud.DalamudAsset.NotoSansCjkMedium, new SafeFontConfig
            {
                SizePx = Plugin.PluginInterface.UiBuilder.FontDefaultSizePx * TitleScaleExpanded,
            })));
        return titleFontHandle;
    }

    private static IDisposable? PushTitleFontIfAvailable()
    {
        var handle = GetTitleFontHandle();
        return handle is { Available: true } ? handle.Push() : null;
    }

    /// <summary>
    /// Eigene Kopfzeile statt der nativen Titelleiste: Icon + Name (ziehbar, Doppelklick klappt
    /// ein/aus), "Plugin benötigt"-Hinweis mittig, rechts Einklappen- und Schließen-Knopf.
    /// </summary>
    private void DrawCustomHeader()
    {
        var headerBandHeight = collapsed ? HeaderBandHeightCollapsed : HeaderBandHeightExpanded;
        var bandStartY = ImGui.GetCursorPosY();
        var bandStartX = ImGui.GetCursorPosX();
        var bandScreenPos = ImGui.GetCursorScreenPos();

        var regionMaxXEarly = ImGui.GetWindowContentRegionMax().X;
        var spacingEarly = ImGui.GetStyle().ItemSpacing.X;
        float headerButtonWidth;
        using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
            headerButtonWidth = ImGui.CalcTextSize(FontAwesomeIcon.Times.ToIconString()).X + ImGui.GetStyle().FramePadding.X * 2f;

        // Nur der linke Teil ist zieh-/doppelklickbar - der Bereich der beiden Knöpfe rechts bleibt frei.
        var buttonsAreaWidth = headerButtonWidth * 2f + spacingEarly;
        var dragWidth = MathF.Max(0f, regionMaxXEarly - buttonsAreaWidth - spacingEarly - bandStartX);
        ImGui.InvisibleButton("##HeaderDragArea", new Vector2(dragWidth, headerBandHeight));
        if (ImGui.IsItemHovered() && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left))
            collapsed = !collapsed;
        if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
            ImGui.SetWindowPos(ImGui.GetWindowPos() + ImGui.GetIO().MouseDelta);
        ImGui.SetCursorScreenPos(bandScreenPos);

        var titleFontPush = collapsed ? null : PushTitleFontIfAvailable();
        var usingTitleFont = titleFontPush != null;
        if (!usingTitleFont)
            ImGui.SetWindowFontScale(collapsed ? TitleScaleCollapsed : TitleScaleExpanded);

        var titleLineHeight = ImGui.CalcTextSize(PluginDisplayName).Y;
        var iconSize = collapsed ? titleLineHeight : Plugin.PluginInterface.UiBuilder.FontDefaultSizePx * IconSizeScale;
        var rowHeight = MathF.Max(titleLineHeight, iconSize);
        var rowStartY = bandStartY + (headerBandHeight - rowHeight) * 0.5f;
        ImGui.SetCursorPosY(rowStartY + (rowHeight - iconSize) * 0.5f);

        var headerIcon = Plugin.TextureProvider.GetFromFile(IconPath).GetWrapOrEmpty();
        ImGui.Image(headerIcon.Handle, new Vector2(iconSize, iconSize));

        ImGui.SameLine();
        ImGui.SetCursorPosY(rowStartY + (rowHeight - titleLineHeight) * 0.5f);
        ImGui.TextUnformatted(PluginDisplayName);

        if (!usingTitleFont)
            ImGui.SetWindowFontScale(1f);
        titleFontPush?.Dispose();

        if (!collapsed && HasMissingRequiredDependency())
        {
            var badgeText = Loc.T("Plugin benötigt", "Plugin needed");
            var badgeSize = MeasureDotBadgeSize(badgeText);
            ImGui.SetCursorPos(new Vector2(
                bandStartX + (regionMaxXEarly - bandStartX - badgeSize.X) * 0.5f,
                bandStartY + (headerBandHeight - badgeSize.Y) * 0.5f));
            DrawDotBadge(badgeText, new Vector4(0.95f, 0.35f, 0.55f, 1f), new Vector4(0.95f, 0.35f, 0.55f, 0.15f), new Vector4(0.95f, 0.35f, 0.55f, 0.6f), new Vector4(1f, 0.75f, 0.85f, 1f));
        }

        var regionMaxX = ImGui.GetWindowContentRegionMax().X;
        var spacing = ImGui.GetStyle().ItemSpacing.X;

        using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
        {
            var buttonWidth = ImGui.CalcTextSize(FontAwesomeIcon.Times.ToIconString()).X + ImGui.GetStyle().FramePadding.X * 2f;
            const float buttonHeightScale = 0.9f;
            var buttonHeight = buttonWidth * buttonHeightScale;
            var buttonYOffset = (rowHeight - buttonHeight) * 0.5f;
            var buttonSize = new Vector2(buttonWidth, buttonHeight);

            ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0f, 0f, 0f, 0f));

            ImGui.SameLine(regionMaxX - buttonWidth);
            ImGui.SetCursorPosY(rowStartY + buttonYOffset);
            if (ImGui.Button($"{FontAwesomeIcon.Times.ToIconString()}##HeaderClose", buttonSize))
                IsOpen = false;

            var collapseIcon = collapsed ? FontAwesomeIcon.ChevronDown : FontAwesomeIcon.ChevronUp;
            ImGui.SameLine(regionMaxX - buttonWidth * 2f - spacing);
            ImGui.SetCursorPosY(rowStartY + buttonYOffset);
            if (ImGui.Button($"{collapseIcon.ToIconString()}##HeaderCollapse", buttonSize))
                collapsed = !collapsed;

            ImGui.PopStyleColor();
        }

        if (!collapsed)
        {
            ImGui.SetCursorPosY(bandStartY + headerBandHeight);
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
        }
    }

    public override void Draw()
    {
        if (!collapsed)
            expandedSize = ImGui.GetWindowSize();

        DrawCustomHeader();

        if (collapsed)
            return;

        const float railWidth = 48f;

        ImGui.BeginChild("##OptionsRail", new Vector2(railWidth, 0f), false, ImGuiWindowFlags.NoScrollbar);
        ImGui.Spacing();

        // Play ganz oben - öffnet die Start-Seite. Ausgegraut, solange in den Einstellungen kein Fisch
        // angehakt ist (außer die Automation läuft noch - dann muss man sie stoppen können).
        var automation = plugin.Automation;
        var playDisabled = !automation.HasEnabledFish && !automation.IsRunning;
        if (playDisabled && railPage == RailPage.Play)
            railPage = RailPage.Settings;
        if (playDisabled)
            ImGui.BeginDisabled();
        if (ModernUi.RailButton(FontAwesomeIcon.Play, railPage == RailPage.Play, playDisabled ? null : Loc.T("Start", "Start")))
            railPage = RailPage.Play;
        if (playDisabled)
        {
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGui.SetTooltip(NoFishSelectedText);
        }

        // Etwas mehr Abstand, damit Play sichtbar von den Seiten-Knöpfen getrennt ist.
        ImGui.Dummy(new Vector2(0f, 10f));
        if (ModernUi.RailButton(FontAwesomeIcon.SlidersH, railPage == RailPage.Settings, Loc.T("Einstellungen", "Settings")))
            railPage = RailPage.Settings;
        ImGui.Spacing();
        if (ModernUi.RailButton(FontAwesomeIcon.Plug, railPage == RailPage.Dependencies, Loc.T("Plugins", "Plugins"), HasMissingRequiredDependency()))
            railPage = RailPage.Dependencies;
        ImGui.Spacing();
        if (ModernUi.RailButton(FontAwesomeIcon.InfoCircle, railPage == RailPage.About, Loc.T("Über", "About")))
            railPage = RailPage.About;
        ImGui.EndChild();

        // Vertikale Trennlinie neben der Icon-Leiste (von Hand gezeichnet - Separator() wäre zwischen
        // zwei Child-Fenstern horizontal).
        const float railToDividerGap = 10f;
        const float dividerToSidebarGap = 26f;
        var railMin = ImGui.GetItemRectMin();
        var railMax = ImGui.GetItemRectMax();
        var dividerX = railMax.X + railToDividerGap;
        ImGui.GetWindowDrawList().AddLine(new Vector2(dividerX, railMin.Y), new Vector2(dividerX, railMax.Y), ImGui.GetColorU32(ImGuiCol.Separator));

        ImGui.SameLine(0f, railToDividerGap + dividerToSidebarGap);

        var contentSize = new Vector2(-(ContentRightMargin - ScrollbarShiftRight), 0f);
        if (railPage == RailPage.Play)
        {
            ImGui.BeginChild("##PlayContent", contentSize, false);
            ImGui.Spacing();
            ImGui.Indent(4f);
            DrawPlayPage();
            ImGui.Unindent(4f);
            ImGui.EndChild();
        }
        else if (railPage == RailPage.Settings)
        {
            // Ohne eigene Unterteilung (Seitenleiste) - die Einstellungen nutzen die volle Breite.
            ImGui.BeginChild("##OptionsContent", contentSize, false);
            ImGui.Spacing();
            ImGui.Indent(4f);
            DrawSettingsPage();
            ImGui.Unindent(4f);
            ImGui.EndChild();
        }
        else if (railPage == RailPage.Dependencies)
        {
            ImGui.BeginChild("##DependenciesContent", contentSize, false);
            ImGui.Spacing();
            ImGui.Indent(4f);
            DrawDependenciesPage();
            ImGui.Unindent(4f);
            ImGui.EndChild();
        }
        else
        {
            ImGui.BeginChild("##AboutContent", contentSize, false);
            ImGui.Spacing();
            ImGui.Indent(4f);
            DrawAboutPage();
            ImGui.Unindent(4f);
            ImGui.EndChild();
        }
    }

    // ---- Start (Play) ----

    private static string NoFishSelectedText => Loc.T(
        "Kein Fisch ausgewählt - hake in den Einstellungen mindestens einen Fisch an.",
        "No fish selected - enable at least one fish in the settings.");

    /// <summary>
    /// Start-Seite: großer Start-/Stop-Knopf, aktueller Status der Automation und die angehakten
    /// Fische in der Reihenfolge, in der sie drankommen (Start = nächstes Fenster minus Prep Timer).
    /// </summary>
    private void DrawPlayPage()
    {
        var automation = plugin.Automation;
        ModernUi.SectionHeader(
            Loc.T("Start", "Start"),
            Loc.T(
                "Fliegt zum nächsten angehakten Fisch, sobald sein Fenster minus Prep Timer erreicht ist, wechselt auf Fischer und angelt mit dem AutoHook-Preset.",
                "Flies to the next enabled fish once its window minus prep timer is reached, switches to Fisher and fishes with the AutoHook preset."));

        // Großer Start-/Stop-Knopf über die volle Breite.
        var running = automation.IsRunning;
        var missingPlugin = HasMissingRequiredDependency();
        var disabled = !running && (!automation.HasEnabledFish || missingPlugin);
        var buttonWidth = ImGui.GetContentRegionAvail().X - ModernUi.CardMargin;
        ImGui.Indent(ModernUi.CardMargin);

        var red = new Vector4(0.85f, 0.3f, 0.35f, 1f);
        ImGui.PushStyleColor(ImGuiCol.Button, running ? red : ModernUi.Accent);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, running ? new Vector4(0.92f, 0.38f, 0.42f, 1f) : ModernUi.AccentHover);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, running ? red : ModernUi.Accent);
        ImGui.PushStyleColor(ImGuiCol.Text, running ? Vector4.One : ModernUi.TextOnAccent);
        if (disabled)
            ImGui.BeginDisabled();
        var clicked = IconTextButton("PlayStartStop", running ? FontAwesomeIcon.Stop : FontAwesomeIcon.Play,
            running ? Loc.T("Stop", "Stop") : Loc.T("Start", "Start"), new Vector2(buttonWidth, 44f));
        if (disabled)
            ImGui.EndDisabled();
        ImGui.PopStyleColor(4);

        if (disabled && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(!automation.HasEnabledFish
                ? NoFishSelectedText
                : Loc.T("Ein benötigtes Plugin fehlt (siehe Plugins).", "A required plugin is missing (see Plugins)."));

        if (clicked)
        {
            if (running)
                automation.Stop();
            else
                automation.Start();
        }

        ImGui.Unindent(ModernUi.CardMargin);
        ImGui.Dummy(new Vector2(0f, 10f));

        if (!string.IsNullOrEmpty(automation.StatusText))
        {
            ModernUi.BeginCard();
            ImGui.PushStyleColor(ImGuiCol.Text, running ? ModernUi.Accent : ModernUi.TextMuted);
            ImGui.TextWrapped(automation.StatusText);
            ImGui.PopStyleColor();
            ModernUi.EndCard();
        }

        // Angehakte Fische in Startreihenfolge.
        var now = DateTime.UtcNow;
        var config = plugin.Configuration;
        var enabled = BigFishData.Dawntrail.Where(f => config.EnabledFish.Contains(f.ItemId)).ToList();
        if (enabled.Count == 0)
        {
            ImGui.TextColored(ModernUi.TextMuted, NoFishSelectedText);
            return;
        }

        var planned = automation.GetPlannedFish(now);
        ModernUi.GroupLabel(Loc.T("Geplante Fische", "Planned fish"));
        const ImGuiTableFlags tableFlags = ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.PadOuterX;
        if (ImGui.BeginTable("##PlannedFish", 4, tableFlags))
        {
            ImGui.TableSetupColumn("##Fish", ImGuiTableColumnFlags.WidthStretch, 1f);
            ImGui.TableSetupColumn("##Start", ImGuiTableColumnFlags.WidthFixed, 170f);
            ImGui.TableSetupColumn("##Position", ImGuiTableColumnFlags.WidthFixed, 150f);
            ImGui.TableSetupColumn("##Note", ImGuiTableColumnFlags.WidthFixed, 220f);
            DrawTableHeader(new[] { (0, Loc.T("FISCH", "FISH")), (1, Loc.T("START", "START")), (2, Loc.T("POSITION", "POSITION")), (3, Loc.T("AUTOHOOK-PRESET", "AUTOHOOK PRESET")) }, lastColumn: 3);

            foreach (var (fish, window, trigger) in planned)
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding();
                ImGui.TextUnformatted(FishingAutomation.FishName(fish));

                ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding();
                if (now >= trigger)
                    ImGui.TextColored(CaughtColor, window.IsActive(now) ? Loc.T("Fenster aktiv", "Window active") : Loc.T("Jetzt (Vorbereitung)", "Now (prep)"));
                else
                    ImGui.TextUnformatted(Loc.T($"in {FormatCountdown(trigger - now)}", $"in {FormatCountdown(trigger - now)}"));

                // Woher die Angel-Position kommt (bzw. dass sie vor Ort gesucht wird).
                ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding();
                switch (FishingPositionStore.Get(config, fish.ItemId)?.Source)
                {
                    case FishingPositionSource.Saved:
                        ImGui.TextColored(CaughtColor, Loc.T("Gespeichert", "Saved"));
                        break;
                    case FishingPositionSource.Builtin:
                        ImGui.TextColored(CaughtColor, Loc.T("Hinterlegt", "Built-in"));
                        break;
                    default:
                        ImGui.TextColored(NotCaughtColor, Loc.T("Keine Position", "No position"));
                        break;
                }

                ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding();
                var preset = config.FishAutoHookPresets.GetValueOrDefault(fish.ItemId);
                if (string.IsNullOrEmpty(preset))
                    ImGui.TextColored(NotCaughtColor, Loc.T("Kein AutoHook-Preset", "No AutoHook preset"));
                else
                    ImGui.TextColored(ModernUi.TextMuted, preset);
            }

            // Angehakt, aber ohne eingetragene Angel-Position - wird übersprungen.
            foreach (var fish in enabled.Where(f => !automation.CanReach(f) && !FishCatchState.IsCaught(f.ItemId)))
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.AlignTextToFramePadding();
                ImGui.TextColored(ModernUi.TextMuted, FishingAutomation.FishName(fish));
                ImGui.TableNextColumn();
                ImGui.TextColored(ModernUi.TextMuted, "-");
                ImGui.TableNextColumn();
                ImGui.TextColored(NotCaughtColor, Loc.T("Keine Position", "No position"));
                ImGui.TableNextColumn();
            }

            ImGui.EndTable();
        }
    }

    // ---- Einstellungen ----

    // Prep Timer pro Fisch: 0-50 Minuten (0 = aus).
    private const int MaxPrepTimerMinutes = 50;

    /// <summary>Dauer als Countdown: "hh:mm:ss", ab einem Tag mit vorangestellten Tagen.</summary>
    private static string FormatCountdown(TimeSpan span)
    {
        if (span < TimeSpan.Zero)
            span = TimeSpan.Zero;

        var clock = $"{span.Hours:00}:{span.Minutes:00}:{span.Seconds:00}";
        return span.TotalDays >= 1 ? Loc.T($"{(int)span.TotalDays} T {clock}", $"{(int)span.TotalDays}d {clock}") : clock;
    }

    /// <summary>Fensterdauer kompakt: "mm:ss", ab einer Stunde "h:mm:ss".</summary>
    private static string FormatDuration(TimeSpan span) =>
        span.TotalHours >= 1 ? $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}" : $"{span.Minutes:00}:{span.Seconds:00}";

    private static readonly Vector4 CaughtColor = new(0.45f, 0.9f, 0.45f, 1f);
    private static readonly Vector4 NotCaughtColor = new(0.95f, 0.35f, 0.4f, 1f);

    /// <summary>
    /// Einstellungen: alle Big Fish (bisher Dawntrail) mit Gefangen-Status, Name, Countdown bis zum
    /// nächsten Fenster (bzw. Restdauer, solange es aktiv ist), Fensterdauer, Prep Timer und
    /// AutoHook-Preset pro Fisch. Sortiert nach dem nächsten Fenster - aktive zuerst.
    /// </summary>
    private void DrawSettingsPage()
    {
        var config = plugin.Configuration;
        ModernUi.SectionHeader(
            Loc.T("Einstellungen", "Settings"),
            Loc.T("Big Fish aus Dawntrail und wann sie das nächste Mal beißen.", "Dawntrail Big Fish and when they bite next."));

        var hideCaught = config.HideCaughtFish;
        ModernUi.BeginCard();
        if (ModernUi.ToggleRow(Loc.T("Bereits gefangene ausblenden", "Hide already caught fish"), ref hideCaught))
        {
            config.HideCaughtFish = hideCaught;
            config.Save();
        }
        ModernUi.EndCard();

        var now = DateTime.UtcNow;
        var itemSheet = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Item>();
        var spotSheet = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.FishingSpot>();

        var rows = BigFishData.Dawntrail
            .Select(fish => (Fish: fish, Caught: FishCatchState.IsCaught(fish.ItemId), Window: FishWindows.GetCurrentOrNext(fish, now)))
            .Where(r => !hideCaught || !r.Caught)
            .OrderBy(r => r.Window == null ? DateTime.MaxValue : r.Window.Value.IsActive(now) ? DateTime.MinValue : r.Window.Value.StartUtc)
            .ToList();

        if (rows.Count == 0)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, ModernUi.TextMuted);
            ImGui.TextUnformatted(Loc.T("Alle Big Fish gefangen - Glückwunsch!", "All Big Fish caught - congratulations!"));
            ImGui.PopStyleColor();
            return;
        }

        const ImGuiTableFlags tableFlags = ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.PadOuterX;
        var presetNames = AutoHookPresets.GetNames();

        // "Position speichern" nur in der Dev-Version (als Dev-Plugin geladen) - zum Erfassen der
        // Angel-Positionen, die danach fest in BigFishData übernommen werden.
        var isDev = Plugin.PluginInterface.IsDev;
        if (!ImGui.BeginTable("##BigFishTimers", isDev ? 9 : 8, tableFlags))
            return;

        ImGui.TableSetupColumn("##Enabled", ImGuiTableColumnFlags.WidthFixed, ImGui.GetFrameHeight());
        ImGui.TableSetupColumn("##Status", ImGuiTableColumnFlags.WidthFixed, 28f);
        ImGui.TableSetupColumn("##Fish", ImGuiTableColumnFlags.WidthStretch, 1f);
        ImGui.TableSetupColumn("##NextWindow", ImGuiTableColumnFlags.WidthFixed, 170f);
        ImGui.TableSetupColumn("##Duration", ImGuiTableColumnFlags.WidthFixed, 80f);
        ImGui.TableSetupColumn("##PrepTimer", ImGuiTableColumnFlags.WidthFixed, 150f);
        ImGui.TableSetupColumn("##AutoHookPreset", ImGuiTableColumnFlags.WidthFixed, 210f);
        ImGui.TableSetupColumn("##FlyToFish", ImGuiTableColumnFlags.WidthFixed, ImGui.GetFrameHeight() + 6f + ImGui.GetStyle().CellPadding.X * 2f);
        if (isDev)
            ImGui.TableSetupColumn("##SavePosition", ImGuiTableColumnFlags.WidthFixed, ImGui.GetFrameHeight() + 6f + ImGui.GetStyle().CellPadding.X * 2f);
        DrawTableHeader(new[] { (2, Loc.T("FISCH", "FISH")), (3, Loc.T("NÄCHSTES FENSTER", "NEXT WINDOW")), (4, Loc.T("DAUER", "DURATION")), (5, "PREP TIMER"), (6, "AUTOHOOK PRESET") }, lastColumn: isDev ? 8 : 7);

        foreach (var (fish, caught, window) in rows)
        {
            ImGui.TableNextRow();

            // Auswahl, ob man diesen Fisch machen will.
            ImGui.TableNextColumn();
            var enabled = config.EnabledFish.Contains(fish.ItemId);
            if (ImGui.Checkbox($"##enabled_{fish.ItemId}", ref enabled))
            {
                if (enabled)
                    config.EnabledFish.Add(fish.ItemId);
                else
                    config.EnabledFish.Remove(fish.ItemId);
                config.Save();
            }
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(Loc.T("Diesen Fisch machen", "Go for this fish"));

            // Gefangen: grüner Haken / rotes X.
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
                ImGui.TextColored(caught ? CaughtColor : NotCaughtColor, (caught ? FontAwesomeIcon.Check : FontAwesomeIcon.Times).ToIconString());
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(caught ? Loc.T("Gefangen", "Caught") : Loc.T("Noch nicht gefangen", "Not caught yet"));

            // Name (Angelplatz als Tooltip).
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            var name = itemSheet.TryGetRow(fish.ItemId, out var item) ? item.Name.ToString() : $"#{fish.ItemId}";
            ImGui.TextUnformatted(name);
            if (ImGui.IsItemHovered() && spotSheet.TryGetRow(fish.FishingSpotId, out var spot))
                ImGui.SetTooltip(spot.PlaceName.ValueNullable?.Name.ToString() ?? string.Empty);

            // Countdown bis zum nächsten Fenster bzw. Restdauer des aktiven.
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            if (FishWindows.IsAlwaysAvailable(fish))
            {
                ImGui.TextColored(CaughtColor, Loc.T("Immer verfügbar", "Always available"));
            }
            else if (window == null)
            {
                ImGui.TextColored(ModernUi.TextMuted, Loc.T("Unbekannt", "Unknown"));
            }
            else if (window.Value.IsActive(now))
            {
                ImGui.TextColored(CaughtColor, Loc.T($"Aktiv - noch {FormatCountdown(window.Value.EndUtc - now)}", $"Active - {FormatCountdown(window.Value.EndUtc - now)} left"));
            }
            else
            {
                ImGui.TextUnformatted(Loc.T($"in {FormatCountdown(window.Value.StartUtc - now)}", $"in {FormatCountdown(window.Value.StartUtc - now)}"));
                if (ImGui.IsItemHovered())
                    ImGui.SetTooltip(window.Value.StartUtc.ToLocalTime().ToString("g"));
            }

            // Wie lange das (aktuelle bzw. nächste) Fenster dauert.
            ImGui.TableNextColumn();
            ImGui.AlignTextToFramePadding();
            if (FishWindows.IsAlwaysAvailable(fish) || window == null)
                ImGui.TextColored(ModernUi.TextMuted, "-");
            else
                ImGui.TextUnformatted(FormatDuration(window.Value.EndUtc - window.Value.StartUtc));

            // Prep Timer: Slider 0-50 Minuten (0 = aus), gespeichert beim Loslassen.
            ImGui.TableNextColumn();
            var minutes = Math.Clamp(config.FishAlertMinutes.GetValueOrDefault(fish.ItemId), 0, MaxPrepTimerMinutes);
            ImGui.SetNextItemWidth(-1f);
            var format = minutes == 0 ? Loc.T("Aus", "Off") : Loc.T("%d Min.", "%d min");
            if (ImGui.SliderInt($"##prep_{fish.ItemId}", ref minutes, 0, MaxPrepTimerMinutes, format))
            {
                if (minutes == 0)
                    config.FishAlertMinutes.Remove(fish.ItemId);
                else
                    config.FishAlertMinutes[fish.ItemId] = minutes;
            }
            if (ImGui.IsItemDeactivatedAfterEdit())
                config.Save();

            // AutoHook-Preset: alle aktuell in AutoHook angelegten Presets zur Auswahl.
            ImGui.TableNextColumn();
            DrawAutoHookPresetCombo(config, fish.ItemId, presetNames);

            ImGui.TableNextColumn();
            DrawFlyToFishButton(fish);

            if (isDev)
            {
                ImGui.TableNextColumn();
                DrawSavePositionButton(config, fish);
            }
        }

        ImGui.EndTable();
    }

    /// <summary>
    /// "Fliege zum Fisch" pro Fisch: fliegt sofort zur eingetragenen Angel-Position (Teleport, falls
    /// nötig), landet und dreht sich zum Wasser - ohne zu angeln. Solange das für diesen Fisch läuft,
    /// wird der Knopf zum Stop-Knopf.
    /// </summary>
    private void DrawFlyToFishButton(BigFish fish)
    {
        var automation = plugin.Automation;
        var testingThis = automation.TestTarget?.ItemId == fish.ItemId;
        var mainRunning = automation.IsRunning && automation.TestTarget == null;
        var disabled = !testingThis && (mainRunning || HasMissingRequiredDependency() || !automation.CanReach(fish));
        var buttonSize = new Vector2(ImGui.GetFrameHeight() + 6f, ImGui.GetFrameHeight());

        ImGui.PushStyleColor(ImGuiCol.Button, testingThis ? new Vector4(0.85f, 0.3f, 0.35f, 0.8f) : new Vector4(0f, 0f, 0f, 0f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, testingThis ? new Vector4(0.92f, 0.38f, 0.42f, 1f) : new Vector4(ModernUi.Accent.X, ModernUi.Accent.Y, ModernUi.Accent.Z, 0.35f));
        ImGui.PushStyleColor(ImGuiCol.Text, testingThis ? Vector4.One : ModernUi.Accent);
        if (disabled)
            ImGui.BeginDisabled();
        var clicked = IconTextButton($"flytofish_{fish.ItemId}", testingThis ? FontAwesomeIcon.Stop : FontAwesomeIcon.PaperPlane, string.Empty, buttonSize);
        if (disabled)
            ImGui.EndDisabled();
        ImGui.PopStyleColor(3);

        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
        {
            ImGui.SetTooltip(testingThis
                ? Loc.T($"Anhalten\n{automation.StatusText}", $"Stop\n{automation.StatusText}")
                : mainRunning
                    ? Loc.T("Nicht möglich, solange die Automation läuft.", "Not possible while the automation is running.")
                    : HasMissingRequiredDependency()
                        ? Loc.T("Ein benötigtes Plugin fehlt (siehe Plugins).", "A required plugin is missing (see Plugins).")
                        : !automation.CanReach(fish)
                            ? Loc.T("Keine Angel-Position eingetragen.", "No fishing position set.")
                            : Loc.T("Fliege zum Fisch (zur Angel-Position, ohne zu angeln)", "Fly to fish (to the fishing position, without fishing)"));
        }

        if (!clicked)
            return;

        if (testingThis)
            automation.Stop();
        else
            automation.StartTest(fish);
    }

    /// <summary>
    /// Nur Dev-Version: speichert die aktuelle Position + Blickrichtung als Angel-Position dieses
    /// Fischs (nur in dessen Zone möglich) und kopiert die passenden Code-Zeilen für BigFishData in die
    /// Zwischenablage. Umschalt+Klick entfernt eine gespeicherte Position wieder.
    /// </summary>
    private static void DrawSavePositionButton(Configuration config, BigFish fish)
    {
        var player = Plugin.ObjectTable.LocalPlayer;
        var inZone = player != null && Plugin.ClientState.TerritoryType == fish.TerritoryId;
        var hasSaved = config.SavedFishingPositions.TryGetValue(fish.ItemId, out var saved);
        var buttonSize = new Vector2(ImGui.GetFrameHeight() + 6f, ImGui.GetFrameHeight());

        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0f, 0f, 0f, 0f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(ModernUi.Accent.X, ModernUi.Accent.Y, ModernUi.Accent.Z, 0.35f));
        ImGui.PushStyleColor(ImGuiCol.Text, hasSaved ? CaughtColor : ModernUi.TextMuted);
        if (!inZone && !hasSaved)
            ImGui.BeginDisabled();
        var clicked = IconTextButton($"savepos_{fish.ItemId}", FontAwesomeIcon.MapMarkerAlt, string.Empty, buttonSize);
        if (!inZone && !hasSaved)
            ImGui.EndDisabled();
        ImGui.PopStyleColor(3);

        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
        {
            var info = hasSaved
                ? Loc.T($"Gespeichert: {saved!.X:F2}, {saved.Y:F2}, {saved.Z:F2}\nUmschalt+Klick: entfernen", $"Saved: {saved!.X:F2}, {saved.Y:F2}, {saved.Z:F2}\nShift+click: remove")
                : string.Empty;
            var action = inZone
                ? Loc.T("Klick: aktuelle Position + Blickrichtung speichern (Code wird kopiert)", "Click: save current position + facing (code is copied)")
                : Loc.T("Nur in der Zone des Fischs speicherbar", "Can only be saved in the fish's zone");
            ImGui.SetTooltip(string.IsNullOrEmpty(info) ? action : $"{action}\n{info}");
        }

        if (!clicked)
            return;

        if (ImGui.GetIO().KeyShift && hasSaved)
        {
            config.SavedFishingPositions.Remove(fish.ItemId);
            config.Save();
            return;
        }

        if (!inZone || player == null)
            return;

        var position = player.Position;
        var facing = player.Rotation;
        config.SavedFishingPositions[fish.ItemId] = SavedFishingPosition.From(position, facing);
        config.Save();

        var name = FishingAutomation.FishName(fish);
        ImGui.SetClipboardText(FishingPositionStore.ToCodeLines(fish.ItemId, name, position, facing));
        Plugin.Log.Info($"[DevTools] Angel-Position für {name} gespeichert: {position}, Blickrichtung {facing:F3} (Code kopiert).");
    }

    private static void DrawAutoHookPresetCombo(Configuration config, uint itemId, IReadOnlyList<string> presetNames)
    {
        var noneLabel = Loc.T("Kein Preset", "No preset");
        var selected = config.FishAutoHookPresets.GetValueOrDefault(itemId);
        var selectedExists = selected != null && presetNames.Contains(selected);

        // Gespeichertes Preset gibt es in AutoHook nicht mehr (umbenannt/gelöscht) - rot markieren.
        var preview = selected == null ? noneLabel : selectedExists ? selected : Loc.T($"{selected} (fehlt)", $"{selected} (missing)");
        if (selected != null && !selectedExists)
            ImGui.PushStyleColor(ImGuiCol.Text, NotCaughtColor);

        ImGui.SetNextItemWidth(-1f);
        var open = ImGui.BeginCombo($"##autohook_{itemId}", preview);
        if (selected != null && !selectedExists)
        {
            ImGui.PopStyleColor();
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip(Loc.T("Dieses Preset gibt es in AutoHook nicht mehr.", "This preset no longer exists in AutoHook."));
        }
        else if (selectedExists && ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(selected);
        }

        if (!open)
            return;

        if (ImGui.Selectable(noneLabel, selected == null))
        {
            config.FishAutoHookPresets.Remove(itemId);
            config.Save();
        }

        if (presetNames.Count == 0)
        {
            ImGui.TextColored(ModernUi.TextMuted, Loc.T("Keine Presets in AutoHook gefunden.", "No presets found in AutoHook."));
        }
        else
        {
            ImGui.Separator();
            foreach (var name in presetNames)
            {
                if (ImGui.Selectable(name, name == selected))
                {
                    config.FishAutoHookPresets[itemId] = name;
                    config.Save();
                }
            }
        }

        ImGui.EndCombo();
    }

    /// <summary>
    /// Dezente Tabellen-Kopfzeile wie bei der Blacklist im Explorer's Codex (statt ImGui.TableHeadersRow
    /// mit farbigem Balken): kleine Großbuchstaben in TextMuted, darunter eine feine Linie über die
    /// volle Tabellenbreite.
    /// </summary>
    private static void DrawTableHeader((int Column, string Text)[] headers, int lastColumn)
    {
        ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
        ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, 0u);
        foreach (var (column, header) in headers)
        {
            ImGui.TableSetColumnIndex(column);
            ImGui.Dummy(new Vector2(0f, 2f));
            ImGui.SetWindowFontScale(0.85f);
            ImGui.TextColored(ModernUi.TextMuted, header);
            ImGui.SetWindowFontScale(1f);
        }

        ImGui.TableSetColumnIndex(lastColumn);
        var headerBottomY = ImGui.GetItemRectMax().Y + 4f;
        var tableMinX = ImGui.GetWindowPos().X + ImGui.GetWindowContentRegionMin().X;
        var tableMaxX = ImGui.GetWindowPos().X + ImGui.GetWindowContentRegionMax().X;
        // Eigenes Clip-Rechteck - sonst würde die Linie auf die aktive Spalte beschnitten.
        var drawList = ImGui.GetWindowDrawList();
        drawList.PushClipRect(new Vector2(tableMinX, headerBottomY - 1f), new Vector2(tableMaxX, headerBottomY + 1f), false);
        drawList.AddLine(new Vector2(tableMinX, headerBottomY), new Vector2(tableMaxX, headerBottomY), ImGui.GetColorU32(ImGuiCol.Separator));
        drawList.PopClipRect();
        ImGui.Dummy(new Vector2(0f, 6f));
    }

    // ---- Über ----

    private void DrawAboutPage()
    {
        const float aboutIconSize = 160f;
        var aboutIcon = Plugin.TextureProvider.GetFromFile(IconPath).GetWrapOrEmpty();
        var availWidth = ImGui.GetContentRegionAvail().X;
        if (availWidth > aboutIconSize)
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (availWidth - aboutIconSize) * 0.5f);
        ImGui.Image(aboutIcon.Handle, new Vector2(aboutIconSize, aboutIconSize));

        ImGui.SetWindowFontScale(1.2f);
        var nameWidth = ImGui.CalcTextSize(PluginDisplayName).X;
        if (availWidth > nameWidth)
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (availWidth - nameWidth) * 0.5f);
        ImGui.TextUnformatted(PluginDisplayName);
        ImGui.SetWindowFontScale(1f);

        var versionText = $"{Loc.T("Version", "Version")} {VersionText}";
        var versionWidth = ImGui.CalcTextSize(versionText).X;
        if (availWidth > versionWidth)
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (availWidth - versionWidth) * 0.5f);
        ImGui.PushStyleColor(ImGuiCol.Text, ModernUi.TextMuted);
        ImGui.TextUnformatted(versionText);
        ImGui.PopStyleColor();

        ImGui.Dummy(new Vector2(0f, 28f));

        DrawSupportCard(availWidth);
        DrawConnectSection();
    }

    /// <summary>Unterstützungs-Karte mit Herz-Icon, Text und Ko-fi-Knopf in der Akzentfarbe.</summary>
    private static void DrawSupportCard(float outerAvailWidth)
    {
        const float outerMargin = 40f;
        ImGui.Indent(outerMargin);
        ModernUi.BeginCard();
        var innerAvail = outerAvailWidth - outerMargin * 2f - ModernUi.CardMargin * 2f;
        var drawList = ImGui.GetWindowDrawList();
        var accent = ModernUi.Accent;

        const float iconDiameter = 52f;
        var iconTopLeft = ImGui.GetCursorScreenPos() + new Vector2((innerAvail - iconDiameter) * 0.5f, 0f);
        var iconCenter = iconTopLeft + new Vector2(iconDiameter * 0.5f, iconDiameter * 0.5f);
        drawList.AddCircleFilled(iconCenter, iconDiameter * 0.5f, ImGui.ColorConvertFloat4ToU32(new Vector4(accent.X, accent.Y, accent.Z, 0.18f)), 32);
        drawList.AddCircle(iconCenter, iconDiameter * 0.5f, ImGui.ColorConvertFloat4ToU32(accent), 32, 1.5f);
        using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
        {
            var glyph = FontAwesomeIcon.Heart.ToIconString();
            var glyphSize = ImGui.CalcTextSize(glyph);
            drawList.AddText(iconCenter - glyphSize * 0.5f, ImGui.ColorConvertFloat4ToU32(accent), glyph);
        }
        ImGui.Dummy(new Vector2(innerAvail, iconDiameter));
        ImGui.Spacing();

        var title = Loc.T("Aus Leidenschaft entwickelt", "Built with passion");
        ImGui.SetWindowFontScale(1.1f);
        var titleWidth = ImGui.CalcTextSize(title).X;
        if (innerAvail > titleWidth)
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (innerAvail - titleWidth) * 0.5f);
        ImGui.TextUnformatted(title);
        ImGui.SetWindowFontScale(1f);
        ImGui.Spacing();

        ImGui.PushStyleColor(ImGuiCol.Text, ModernUi.TextMuted);
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + innerAvail);
        ImGui.TextWrapped(Loc.T(
            "Dieses Plugin entsteht Update für Update in meiner Freizeit. Falls es dir das Spiel etwas " +
            "leichter macht, ist eine kleine Spende auf Ko-fi eine schöne Geste - ganz ohne Verpflichtung. " +
            "Danke, dass du dabei bist!",
            "This plugin is built update by update in my free time. If it's made your playtime a little " +
            "easier, a small Ko-fi donation is a nice gesture - never expected. Thanks for being part of this!"));
        ImGui.PopTextWrapPos();
        ImGui.PopStyleColor();
        ImGui.Spacing();

        // Dunkler Text auf dem hellen Akzent-Blau (weiß wäre darauf schlecht lesbar).
        ImGui.PushStyleColor(ImGuiCol.Button, accent);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ModernUi.AccentHover);
        ImGui.PushStyleColor(ImGuiCol.Text, ModernUi.TextOnAccent);
        if (IconTextButton("AboutKofi", FontAwesomeIcon.MugHot, Loc.T("Auf Ko-fi unterstützen", "Support on Ko-fi"), new Vector2(innerAvail, 0f)))
            Util.OpenLink("https://ko-fi.com/horstbrot");
        ImGui.PopStyleColor(3);

        ModernUi.EndCard(borderColor: new Vector4(accent.X, accent.Y, accent.Z, 0.55f));
        ImGui.Unindent(outerMargin);
    }

    /// <summary>Button mit Icon + Text (beides von Hand gezeichnet, da eine Schrift nicht beides enthält).</summary>
    private static bool IconTextButton(string id, FontAwesomeIcon icon, string text, Vector2 size)
    {
        var clicked = ImGui.Button($"##{id}", size);
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        var drawList = ImGui.GetWindowDrawList();
        var textColor = ImGui.GetColorU32(ImGuiCol.Text);

        var gap = string.IsNullOrEmpty(text) ? 0f : 8f;
        string iconGlyph;
        Vector2 iconSize;
        using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
        {
            iconGlyph = icon.ToIconString();
            iconSize = ImGui.CalcTextSize(iconGlyph);
        }
        var textSize = ImGui.CalcTextSize(text);
        var contentWidth = iconSize.X + gap + textSize.X;
        var contentStartX = min.X + (max.X - min.X - contentWidth) * 0.5f;

        using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
            drawList.AddText(new Vector2(contentStartX, min.Y + (max.Y - min.Y - iconSize.Y) * 0.5f), textColor, iconGlyph);
        drawList.AddText(new Vector2(contentStartX + iconSize.X + gap, min.Y + (max.Y - min.Y - textSize.Y) * 0.5f), textColor, text);

        return clicked;
    }

    private static Vector2 MeasureIconTextButtonSize(FontAwesomeIcon icon, string text, Vector2 padding)
    {
        float iconWidth;
        using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
            iconWidth = ImGui.CalcTextSize(icon.ToIconString()).X;
        var textSize = ImGui.CalcTextSize(text);
        var gap = string.IsNullOrEmpty(text) ? 0f : 8f;
        return new Vector2(iconWidth + gap + textSize.X + padding.X * 2f, textSize.Y + padding.Y * 2f);
    }

    /// <summary>"CONNECT"-Trenner gefolgt vom GitHub-Knopf.</summary>
    private static void DrawConnectSection()
    {
        var avail = ImGui.GetContentRegionAvail().X;
        var label = Loc.T("VERBINDEN", "CONNECT");

        string linkGlyph;
        float linkGlyphWidth;
        using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
        {
            linkGlyph = FontAwesomeIcon.Link.ToIconString();
            linkGlyphWidth = ImGui.CalcTextSize(linkGlyph).X;
        }
        var labelWidth = ImGui.CalcTextSize(label).X;

        const float iconToLabelGap = 6f;
        const float lineGap = 10f;
        var centerWidth = linkGlyphWidth + iconToLabelGap + labelWidth;
        var lineWidth = MathF.Max(0f, (avail - centerWidth - lineGap * 2f) * 0.5f);

        var drawList = ImGui.GetWindowDrawList();
        var lineY = ImGui.GetCursorScreenPos().Y + ImGui.GetTextLineHeight() * 0.5f;
        var startX = ImGui.GetCursorScreenPos().X;
        var lineColor = ImGui.ColorConvertFloat4ToU32(ModernUi.CardBorder);
        drawList.AddLine(new Vector2(startX, lineY), new Vector2(startX + lineWidth, lineY), lineColor);
        drawList.AddLine(new Vector2(startX + avail - lineWidth, lineY), new Vector2(startX + avail, lineY), lineColor);

        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + lineWidth + lineGap);
        ImGui.PushStyleColor(ImGuiCol.Text, ModernUi.TextMuted);
        using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
            ImGui.TextUnformatted(linkGlyph);
        ImGui.SameLine(0f, iconToLabelGap);
        ImGui.TextUnformatted(label);
        ImGui.PopStyleColor();

        ImGui.Spacing();
        ImGui.Spacing();

        var githubText = Loc.T("GitHub", "GitHub");
        var buttonSize = MeasureIconTextButtonSize(FontAwesomeIcon.CodeBranch, githubText, new Vector2(14f, 7f));
        if (avail > buttonSize.X)
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (avail - buttonSize.X) * 0.5f);

        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.14f, 0.16f, 0.18f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.20f, 0.22f, 0.25f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.10f, 0.11f, 0.13f, 1f));
        if (IconTextButton("AboutGitHub", FontAwesomeIcon.CodeBranch, githubText, buttonSize))
            Util.OpenLink(GitHubUrl);
        ImGui.PopStyleColor(3);
    }

    // ---- Plugins ----

    private static readonly (string InternalName, string DisplayName, string DescriptionDe, string DescriptionEn, bool Required)[] Dependencies =
    {
        ("AutoHook", "AutoHook",
            "Übernimmt das eigentliche Angeln (Haken, Köder, Aktionen) für den Big Fish Helper.",
            "Handles the actual fishing (hooking, bait, actions) for the Big Fish Helper.",
            true),
        ("vnavmesh", "vnavmesh",
            "Fliegt die Automation zur Angel-Position des Fischs.",
            "Flies the automation to the fish's fishing position.",
            true),
        ("Lifestream", "Lifestream",
            "Teleportiert zum nächsten Ätheriten, wenn der Fisch in einer anderen Zone ist.",
            "Teleports to the nearest aetheryte when the fish is in another zone.",
            true),
    };

    /// <summary>Ob ein als "Required" markiertes Plugin nicht installiert/geladen ist (Hinweis im Titel + Punkt am Plugins-Icon).</summary>
    internal static bool HasMissingRequiredDependency() =>
        Dependencies.Any(d => d.Required && !IsPluginLoaded(d.InternalName));

    private static bool IsPluginLoaded(string internalName) =>
        Plugin.PluginInterface.InstalledPlugins.Any(p => p.InternalName == internalName && p.IsLoaded);

    private static void DrawDependenciesPage()
    {
        var missingRequired = Dependencies.Count(d => d.Required && !IsPluginLoaded(d.InternalName));

        ImGui.SetWindowFontScale(1.25f);
        ImGui.TextUnformatted(Loc.T("Plugins", "Plugins"));
        ImGui.SetWindowFontScale(1f);

        if (missingRequired > 0)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.95f, 0.35f, 0.4f, 1f));
            ImGui.TextUnformatted(missingRequired == 1
                ? Loc.T("1 benötigtes Plugin fehlt.", "1 required plugin is missing.")
                : Loc.T($"{missingRequired} benötigte Plugins fehlen.", $"{missingRequired} required plugins are missing."));
            ImGui.PopStyleColor();
        }

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Dummy(new Vector2(0f, 10f));

        foreach (var dep in Dependencies)
        {
            DrawDependencyCard(dep.InternalName, dep.DisplayName, Loc.T(dep.DescriptionDe, dep.DescriptionEn), dep.Required, IsPluginLoaded(dep.InternalName));
            ImGui.Spacing();
        }
    }

    /// <summary>
    /// Eine Abhängigkeit als einzeilige Karte: Status-Kreis links, Name + "BENÖTIGT"/"OPTIONAL"
    /// (Beschreibung als "?"-Tooltip), rechts Installiert-Haken bzw. "Installieren"-Knopf.
    /// </summary>
    private static void DrawDependencyCard(string internalName, string displayName, string description, bool required, bool isInstalled)
    {
        ModernUi.BeginCard();

        const float iconDiameter = 36f;
        const float rowHeight = iconDiameter;
        var rowStart = ImGui.GetCursorScreenPos();
        var availWidth = ImGui.GetContentRegionAvail().X - ModernUi.CardMargin;
        var drawList = ImGui.GetWindowDrawList();

        var iconColor = isInstalled ? new Vector4(0.3f, 0.75f, 0.45f, 1f) : new Vector4(0.85f, 0.3f, 0.35f, 1f);
        var iconCenter = rowStart + new Vector2(iconDiameter * 0.5f, iconDiameter * 0.5f);
        drawList.AddCircleFilled(iconCenter, iconDiameter * 0.5f, ImGui.ColorConvertFloat4ToU32(iconColor), 24);
        using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
        {
            var glyph = (isInstalled ? FontAwesomeIcon.Check : FontAwesomeIcon.Times).ToIconString();
            var glyphSize = ImGui.CalcTextSize(glyph);
            drawList.AddText(iconCenter - glyphSize * 0.5f, ImGui.ColorConvertFloat4ToU32(Vector4.One), glyph);
        }

        var badgeText = required ? Loc.T("BENÖTIGT", "REQUIRED") : Loc.T("OPTIONAL", "OPTIONAL");
        var installedLabel = Loc.T("Installiert", "Installed");
        var installLabel = Loc.T("Installieren", "Install");
        const float nameStartOffset = iconDiameter + 12f;

        float statusWidth;
        if (isInstalled)
        {
            float checkIconWidth;
            using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
                checkIconWidth = ImGui.CalcTextSize(FontAwesomeIcon.Check.ToIconString()).X;
            statusWidth = checkIconWidth + ImGui.GetStyle().ItemSpacing.X + ImGui.CalcTextSize(installedLabel).X;
        }
        else
        {
            statusWidth = MeasureIconTextButtonSize(FontAwesomeIcon.Download, installLabel, ImGui.GetStyle().FramePadding).X;
        }
        var statusHeight = isInstalled ? ImGui.GetTextLineHeight() : ImGui.GetFrameHeight();

        var badgeHeight = ImGui.GetTextLineHeight() + BadgePaddingY * 2f;
        var nameRowY = rowStart.Y + (iconDiameter - badgeHeight) * 0.5f;
        ImGui.SetCursorScreenPos(new Vector2(rowStart.X + nameStartOffset, nameRowY + BadgePaddingY));
        ImGui.TextUnformatted(displayName);
        var nameTopY = ImGui.GetItemRectMin().Y;
        ImGui.SameLine();
        ImGui.SetCursorScreenPos(new Vector2(ImGui.GetCursorScreenPos().X, nameRowY));
        DrawBadge(badgeText, required);
        var labelEnd = ImGui.GetItemRectMax();

        ModernUi.HelpIconIfHovered(rowStart, new Vector2(availWidth, rowHeight), labelEnd, nameTopY, description);

        var statusY = rowStart.Y + (rowHeight - statusHeight) * 0.5f;
        ImGui.SetCursorScreenPos(new Vector2(rowStart.X + availWidth - statusWidth, statusY));
        if (isInstalled)
        {
            using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
                ImGui.TextColored(new Vector4(0.45f, 0.9f, 0.45f, 1f), FontAwesomeIcon.Check.ToIconString());
            ImGui.SameLine();
            ImGui.TextUnformatted(installedLabel);
        }
        else
        {
            ImGui.PushStyleColor(ImGuiCol.Button, ModernUi.Accent);
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ModernUi.AccentHover);
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, ModernUi.Accent);
            ImGui.PushStyleColor(ImGuiCol.Text, ModernUi.TextOnAccent);
            if (IconTextButton($"install_{internalName}", FontAwesomeIcon.Download, installLabel, new Vector2(statusWidth, ImGui.GetFrameHeight())))
                Plugin.PluginInterface.OpenPluginInstallerTo(PluginInstallerOpenKind.AllPlugins, displayName);
            ImGui.PopStyleColor(4);
        }

        // Karte immer exakt bis availWidth und über die volle Zeilenhöhe.
        ImGui.SetCursorScreenPos(new Vector2(rowStart.X + availWidth, rowStart.Y));
        ImGui.Dummy(new Vector2(0f, rowHeight));

        ModernUi.EndCard();
    }

    private const float BadgePaddingY = 3f;
    private const float BadgePaddingX = 8f;

    /// <summary>Kleine abgerundete Pille für "BENÖTIGT"/"OPTIONAL" - betont in der Akzentfarbe.</summary>
    private static void DrawBadge(string text, bool emphasized)
    {
        var textSize = ImGui.CalcTextSize(text);
        var padding = new Vector2(BadgePaddingX, BadgePaddingY);
        var size = textSize + padding * 2f;
        var pos = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();

        var accent = ModernUi.Accent;
        var bg = emphasized ? new Vector4(accent.X, accent.Y, accent.Z, 0.25f) : new Vector4(1f, 1f, 1f, 0.08f);
        var border = emphasized ? new Vector4(accent.X, accent.Y, accent.Z, 0.9f) : new Vector4(1f, 1f, 1f, 0.25f);
        var textColor = emphasized ? new Vector4(0.78f, 0.9f, 1f, 1f) : ModernUi.TextMuted;

        drawList.AddRectFilled(pos, pos + size, ImGui.ColorConvertFloat4ToU32(bg), size.Y * 0.5f);
        drawList.AddRect(pos, pos + size, ImGui.ColorConvertFloat4ToU32(border), size.Y * 0.5f);
        drawList.AddText(pos + padding, ImGui.ColorConvertFloat4ToU32(textColor), text);

        ImGui.Dummy(size);
    }

    private const float DotBadgeDotDiameter = 6f;
    private const float DotBadgeDotToTextGap = 6f;
    private static readonly Vector2 DotBadgePadding = new(10f, 3f);

    private static Vector2 MeasureDotBadgeSize(string text)
    {
        var textSize = ImGui.CalcTextSize(text);
        return new Vector2(DotBadgeDotDiameter + DotBadgeDotToTextGap + textSize.X + DotBadgePadding.X * 2f, textSize.Y + DotBadgePadding.Y * 2f);
    }

    /// <summary>Abgerundete Pille mit farbigem Punkt, z.B. "Plugin benötigt" im Fenstertitel.</summary>
    private static void DrawDotBadge(string text, Vector4 dotColor, Vector4 bgColor, Vector4 borderColor, Vector4 textColor)
    {
        var padding = DotBadgePadding;
        var size = MeasureDotBadgeSize(text);
        var pos = ImGui.GetCursorScreenPos();
        var drawList = ImGui.GetWindowDrawList();

        drawList.AddRectFilled(pos, pos + size, ImGui.ColorConvertFloat4ToU32(bgColor), size.Y * 0.5f);
        drawList.AddRect(pos, pos + size, ImGui.ColorConvertFloat4ToU32(borderColor), size.Y * 0.5f);

        var dotCenter = pos + new Vector2(padding.X + DotBadgeDotDiameter * 0.5f, size.Y * 0.5f);
        drawList.AddCircleFilled(dotCenter, DotBadgeDotDiameter * 0.5f, ImGui.ColorConvertFloat4ToU32(dotColor), 12);

        var textPos = pos + new Vector2(padding.X + DotBadgeDotDiameter + DotBadgeDotToTextGap, padding.Y);
        drawList.AddText(textPos, ImGui.ColorConvertFloat4ToU32(textColor), text);

        ImGui.Dummy(size);
    }
}
