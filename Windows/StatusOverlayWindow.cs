using System;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Collections.Generic;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using PluginUiKit;

namespace BigFishHelper.Windows;

/// <summary>
/// Kleines, unabhängiges Status-Overlay (siehe Configuration.ShowOverlayOnStart) - Ocean-UIWidget-Umbau
/// nach Nutzervorgabe (Referenzbild "OVERLAY · STATES"): ein Fenster, das sich mit der Automatik-Phase
/// ändert (Wait -&gt; Fly -&gt; Prep -&gt; Fish), gleiches Layout, nur Status/Timer/Farben wechseln. Die
/// Automatik-Logik selbst (Start/Stop/Skip/Zustände/Countdown/Planned Fish) kommt unverändert aus
/// FishingAutomation - dieses Fenster liest nur und löst Knöpfe aus.
///
/// Bewusste Vereinfachungen ggü. der Aufgabenstellung (siehe Antwort an den Nutzer):
/// - Kein echter Fallschatten (Dalamud/ImGui-Fensterschatten-API unsicher in diesem Binding ohne
///   Live-Test zu verifizieren) - ausgelassen statt riskiert.
/// - Casts/Hooks pro Session werden NICHT gezählt (AutoHook meldet keine Wurf-/Hak-Ereignisse per IPC
///   an dieses Plugin) - nur "Caught" (FishingAutomation.SessionCaught, aus der bestehenden
///   Fang-Erkennung in OnChatMessage) ist echt, Casts/Hooks zeigen "-".
/// - "Achtung" pausiert die Automatik NICHT wirklich (kein bestehender Pause-Mechanismus in
///   FishingAutomation, der sich ohne Risiko für die laufende Automatik hätte nachrüsten lassen) -
///   "Fortsetzen" blendet die Warnung nur 60s lokal aus, die Automatik lief ohnehin schon die ganze
///   Zeit unverändert weiter (reine Anzeige).
/// </summary>
public sealed class StatusOverlayWindow : Window, IDisposable
{
    private const float WindowWidth = 320f;
    private const float TitleBarHeight = 34f;
    private const float PadX = 14f;
    private const float PadTop = 12f;
    private const float BlockGap = 11f;

    private const ImGuiWindowFlags BaseFlags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar |
        ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.AlwaysAutoResize;

    private readonly Plugin plugin;
    private readonly Dictionary<uint, ISharedImmediateTexture> iconCache = new();

    private DateTime? attentionDismissedUntilUtc;

    // ---- Schriftgrößen gemäß Aufgabenstellung Abschnitt 1/2/3/4 (eigene Rollen, kein Wiederverwenden
    // der Menü-Schriften - das Overlay hat eigene, kompaktere Größen). ----
    private static IFontHandle? titleFont;
    private static IFontHandle TitleFont => titleFont ??= UiFonts.BuildHandle("Cinzel-Bold.ttf", 15f);
    private static IFontHandle? pillFont;
    private static IFontHandle PillFont => pillFont ??= UiFonts.BuildHandle("AlegreyaSans-Bold.ttf", 15f);
    private static IFontHandle? contextFont;
    private static IFontHandle ContextFont => contextFont ??= UiFonts.BuildHandle("AlegreyaSans-Regular.ttf", 16f);
    private static IFontHandle? phaseLabelFont;
    private static IFontHandle PhaseLabelFont => phaseLabelFont ??= UiFonts.BuildHandle("AlegreyaSans-Regular.ttf", 15f);
    private static IFontHandle? phaseLabelActiveFont;
    private static IFontHandle PhaseLabelActiveFont => phaseLabelActiveFont ??= UiFonts.BuildHandle("AlegreyaSans-Bold.ttf", 15f);
    private static IFontHandle? fishNameFont;
    private static IFontHandle FishNameFont => fishNameFont ??= UiFonts.BuildHandle("Cinzel-Bold.ttf", 20f);
    private static IFontHandle? fishSubFont;
    private static IFontHandle FishSubFont => fishSubFont ??= UiFonts.BuildHandle("AlegreyaSans-Regular.ttf", 16f);
    private static IFontHandle? baitCountFont;
    private static IFontHandle BaitCountFont => baitCountFont ??= UiFonts.BuildHandle("AlegreyaSans-Bold.ttf", 17f);
    private static IFontHandle? timerLabelFont;
    private static IFontHandle TimerLabelFont => timerLabelFont ??= UiFonts.BuildHandle("AlegreyaSans-Regular.ttf", 16f);
    private static IFontHandle? timerValueFont;
    private static IFontHandle TimerValueFont => timerValueFont ??= UiFonts.BuildHandle("AlegreyaSans-Bold.ttf", 22f);
    private static IFontHandle? upNextLabelFont;
    private static IFontHandle UpNextLabelFont => upNextLabelFont ??= UiFonts.BuildHandle("Cinzel.ttf", 14f);
    private static IFontHandle? upNextNameFont;
    private static IFontHandle UpNextNameFont => upNextNameFont ??= UiFonts.BuildHandle("AlegreyaSans-Bold.ttf", 17f);
    private static IFontHandle? upNextCountdownFont;
    private static IFontHandle UpNextCountdownFont => upNextCountdownFont ??= UiFonts.BuildHandle("AlegreyaSans-Regular.ttf", 16f);
    private static IFontHandle? statValueFont;
    private static IFontHandle StatValueFont => statValueFont ??= UiFonts.BuildHandle("AlegreyaSans-Bold.ttf", 20f);
    private static IFontHandle? statLabelFont;
    private static IFontHandle StatLabelFont => statLabelFont ??= UiFonts.BuildHandle("AlegreyaSans-Regular.ttf", 15f);
    private static IFontHandle? warningTitleFont;
    private static IFontHandle WarningTitleFont => warningTitleFont ??= UiFonts.BuildHandle("AlegreyaSans-Bold.ttf", 18f);
    private static IFontHandle? warningBodyFont;
    private static IFontHandle WarningBodyFont => warningBodyFont ??= UiFonts.BuildHandle("AlegreyaSans-Regular.ttf", 17f);
    private static IFontHandle? actionButtonFont;
    private static IFontHandle ActionButtonFont => actionButtonFont ??= UiFonts.BuildHandle("AlegreyaSans-Bold.ttf", 18f);
    private static IFontHandle? compactNameFont;
    private static IFontHandle CompactNameFont => compactNameFont ??= UiFonts.BuildHandle("Cinzel-Bold.ttf", 17f);
    private static IFontHandle? compactCountdownFont;
    private static IFontHandle CompactCountdownFont => compactCountdownFont ??= UiFonts.BuildHandle("AlegreyaSans-Regular.ttf", 17f);

    /// <summary>Wie OceanMainWindow.PreloadFonts - alle Font-Handles dieser Klasse einmal referenzieren,
    /// damit Dalamud sie bereits beim Plugin-Start baut statt erst beim ersten Öffnen des Overlays.</summary>
    internal static void PreloadFonts()
    {
        _ = ActionButtonFont;
        _ = BaitCountFont;
        _ = CompactCountdownFont;
        _ = CompactNameFont;
        _ = ContextFont;
        _ = FishNameFont;
        _ = FishSubFont;
        _ = PhaseLabelActiveFont;
        _ = PhaseLabelFont;
        _ = PillFont;
        _ = StatLabelFont;
        _ = StatValueFont;
        _ = TimerLabelFont;
        _ = TimerValueFont;
        _ = TitleFont;
        _ = UpNextCountdownFont;
        _ = UpNextLabelFont;
        _ = UpNextNameFont;
        _ = WarningBodyFont;
        _ = WarningTitleFont;
    }

    // Zustandsfarben wörtlich aus der Aufgabenstellung (Abschnitt 3) - nicht Teil der generischen
    // UiTheme-Palette (seiten-/zustandsspezifisch, siehe gleiches Muster in OceanMainWindow).
    private static readonly Vector4 WaitingBorder = UiTheme.Hex("#2E6B66");
    private static readonly Vector4 FlyingColor = UiTheme.Hex("#9CC4E4");
    private static readonly Vector4 FlyingBg = UiTheme.Hex("#9CC4E4", 0.1f);
    private static readonly Vector4 FlyingBorder = UiTheme.Hex("#2C4458");
    private static readonly Vector4 FishingGreen = UiTheme.Hex("#7FE0A6");
    private static readonly Vector4 FishingBg = UiTheme.Hex("#7FE0A6", 0.1f);
    private static readonly Vector4 FishingBorder = UiTheme.Hex("#2F6B4A");
    private static readonly Vector4 AttentionYellow = UiTheme.Hex("#E9C46A");
    private static readonly Vector4 AttentionBg = UiTheme.Hex("#30271A");
    private static readonly Vector4 AttentionBorder = UiTheme.Hex("#5C4A26");
    private static readonly Vector4 CoralRed = UiTheme.Hex("#E58A74");
    private static readonly Vector4 StopTextColor = UiTheme.Hex("#2A0E08");
    private static readonly Vector4 PhaseUpcomingLine = UiTheme.Hex("#5E7C88");

    private enum UiState
    {
        Idle,
        Waiting,
        Flying,
        Prep,
        Fishing,
        Attention,
    }

    public StatusOverlayWindow(Plugin plugin) : base("##BigFishHelperStatusOverlay", BaseFlags)
    {
        this.plugin = plugin;
        RespectCloseHotkey = false;
    }

    public void Dispose() { }

    public override void PreDraw()
    {
        var scale = ImGuiHelpers.GlobalScale;
        var config = plugin.Configuration;
        var T = UiTheme.Active;

        if (config.OverlayCompact)
        {
            // Kompaktmodus: GAR kein eigener Fensterhintergrund/-rahmen mehr (Nutzer-Report: ImGuis
            // natives Fenster-Rounding (8px, siehe unten) ist viel enger als die Pillen-Rundung
            // (height/2=22px), die eigens per DrawList gezeichnet wird - die übrig bleibenden, nur
            // leicht abgerundeten Fenster-Ecken ragten dadurch sichtbar eckig über die Pille hinaus).
            // Stattdessen zeichnet DrawCompactPill die komplette Pille (Füllung + Rand) selbst, mit der
            // tatsächlich benötigten Rundung - das Fenster selbst bleibt unsichtbar (NoBackground +
            // transparente WindowBg/Border-Farben als zusätzliche Absicherung, falls ein globaler
            // Style-Push anderswo BgAlpha/Border doch noch etwas einfärben würde).
            Flags = BaseFlags | ImGuiWindowFlags.NoBackground;
            ImGui.SetNextWindowSize(new Vector2(WindowWidth * scale, 44f * scale), ImGuiCond.Always);
            ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0f, 0f, 0f, 0f));
            ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(0f, 0f, 0f, 0f));
            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0f);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        }
        else
        {
            Flags = BaseFlags;
            ImGui.SetNextWindowSize(new Vector2(WindowWidth * scale, 0f), ImGuiCond.Always);
            ImGui.PushStyleColor(ImGuiCol.WindowBg, T.BgWindow with { W = Math.Clamp(config.OverlayOpacity, 0.4f, 1f) });
            ImGui.PushStyleColor(ImGuiCol.Border, T.LineFrame);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 8f * scale);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1f * scale);
            ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        }
    }

    public override void PostDraw()
    {
        ImGui.PopStyleVar(3);
        ImGui.PopStyleColor(2);
    }

    public override void Draw()
    {
        var scale = ImGuiHelpers.GlobalScale;
        var automation = plugin.Automation;
        var config = plugin.Configuration;

        // Fenster bleibt jetzt auch im Leerlauf offen (Nutzeranforderung Abschnitt 3 "IDLE" - mit
        // Start-Knopf) statt sich wie bisher automatisch zu schließen, sobald die Automatik nicht
        // läuft. Schließen passiert nur noch über den eigenen Titelleisten-Knopf (✕).
        var now = DateTime.UtcNow;
        var isRunning = automation.IsRunning;
        var planned = automation.GetPlannedFish(now);
        var attentionReason = automation.GetAttentionReason();
        if (attentionDismissedUntilUtc is { } dismissedUntil && now >= dismissedUntil)
            attentionDismissedUntilUtc = null;
        var showAttention = attentionReason != null && attentionDismissedUntilUtc == null;

        var uiState = !isRunning
            ? UiState.Idle
            : showAttention
                ? UiState.Attention
                : automation.CurrentPhase switch
                {
                    FishingAutomation.Phase.Flying => UiState.Flying,
                    FishingAutomation.Phase.Prep => UiState.Prep,
                    FishingAutomation.Phase.Fishing => UiState.Fishing,
                    _ => UiState.Waiting,
                };

        if (config.OverlayCompact)
        {
            DrawCompactPill(scale, uiState, automation, config, planned, now);
            return;
        }

        DrawTitleBar(scale, config);

        var contentWidth = WindowWidth * scale - PadX * scale * 2f;
        ImGui.Indent(PadX * scale);
        ImGui.Dummy(new Vector2(0f, PadTop * scale));

        DrawStatusRow(scale, uiState, automation, config, contentWidth);

        if (uiState is UiState.Waiting or UiState.Flying or UiState.Prep or UiState.Fishing)
        {
            ImGui.Dummy(new Vector2(0f, BlockGap * scale));
            DrawPhaseBar(scale, uiState, contentWidth);
        }

        var fish = automation.CurrentTarget ?? (planned.Length > 0 ? planned[0].Fish : null);
        if (uiState != UiState.Idle || fish != null)
        {
            ImGui.Dummy(new Vector2(0f, BlockGap * scale));
            DrawFishRow(scale, uiState, fish, automation, contentWidth);
        }

        if (fish != null && uiState != UiState.Idle)
        {
            ImGui.Dummy(new Vector2(0f, BlockGap * scale));
            DrawTimerBox(scale, uiState, fish, planned, now, contentWidth);
        }

        if (uiState == UiState.Waiting && planned.Length > 1)
        {
            ImGui.Dummy(new Vector2(0f, BlockGap * scale));
            DrawUpNextBlock(scale, planned, now, contentWidth);
        }
        else if (uiState == UiState.Fishing)
        {
            ImGui.Dummy(new Vector2(0f, BlockGap * scale));
            DrawStatTiles(scale, automation, contentWidth);
        }
        else if (uiState == UiState.Attention && attentionReason != null)
        {
            ImGui.Dummy(new Vector2(0f, BlockGap * scale));
            DrawAttentionBox(scale, attentionReason, contentWidth);
        }
        else if (uiState == UiState.Idle && fish == null)
        {
            ImGui.Dummy(new Vector2(0f, BlockGap * scale));
            using (ContextFont.Push())
                ImGui.TextColored(UiTheme.Active.TextMuted, Loc.T("Kein Fisch geplant - Prep-Timer in Fish Data setzen.", "No fish planned - set a prep timer in Fish Data."));
        }

        ImGui.Dummy(new Vector2(0f, BlockGap * scale));
        DrawActionRow(scale, uiState, automation, config, fish, contentWidth);

        ImGui.Unindent(PadX * scale);
        ImGui.Dummy(new Vector2(0f, (PadTop - 3f) * scale));
    }

    // ---- Titelleiste ----

    private void DrawTitleBar(float scale, Configuration config)
    {
        var T = UiTheme.Active;
        var height = TitleBarHeight * scale;
        var width = ImGui.GetContentRegionAvail().X;
        var origin = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();

        var buttonSize = new Vector2(22f * scale, 22f * scale);
        // Rechter Rand der Knöpfe identisch zum Rand aller übrigen Inhaltselemente (PadX, siehe
        // DrawStatusRow/DrawTimerBox/... - contentWidth endet dort bei "origin.X + contentWidth" einer
        // um PadX eingerückten Zeile, was exakt "origin.X + width - PadX*scale" hier im nicht
        // eingerückten Titelleisten-Koordinatensystem entspricht).
        var rightEdge = origin.X + width - PadX * scale;
        var closeX = rightEdge - buttonSize.X;
        var collapseX = closeX - 6f * scale - buttonSize.X;
        var dragWidth = collapseX - origin.X - 4f * scale;

        // Ziehen über die Titelleiste verschiebt das randlose Fenster (kein natives Dalamud-Title-Bar-
        // Dragging mehr verfügbar, da NoTitleBar) - eigener Zieh-Bereich, der bewusst VOR den beiden
        // Knöpfen endet, damit sich die Hit-Flächen nicht überlappen.
        ImGui.InvisibleButton("##OceanOverlayDrag", new Vector2(dragWidth, height));
        if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
        {
            var delta = ImGui.GetMouseDragDelta(ImGuiMouseButton.Left);
            ImGui.ResetMouseDragDelta(ImGuiMouseButton.Left);
            ImGui.SetWindowPos(ImGui.GetWindowPos() + delta);
        }

        var iconSize = 14f * scale;
        var iconPos = origin + new Vector2(10f * scale, (height - iconSize) / 2f);
        UiIcons.Draw(UiIcon.Fish, iconPos, iconSize, ImGui.GetColorU32(T.Accent));

        using (TitleFont.Push())
        {
            var textY = origin.Y + (height - ImGui.GetTextLineHeight()) / 2f;
            ImGui.SetCursorScreenPos(new Vector2(iconPos.X + iconSize + 8f * scale, textY));
            UiWidgets.DrawSpacedText(Loc.T("BIG FISH HELPER", "BIG FISH HELPER"), T.TextHeading, 1f);
        }

        ImGui.SetCursorScreenPos(new Vector2(collapseX, origin.Y + (height - buttonSize.Y) / 2f));
        if (UiNav.IconButton(UiIcon.ChevronUp, "##OceanOverlayCollapse", buttonSize, Loc.T("Einklappen", "Collapse")))
        {
            config.OverlayCompact = true;
            config.Save();
        }

        ImGui.SetCursorScreenPos(new Vector2(closeX, origin.Y + (height - buttonSize.Y) / 2f));
        if (UiNav.IconButton(UiIcon.Cross, "##OceanOverlayClose", buttonSize, Loc.T("Schließen", "Close")))
            IsOpen = false;

        dl.AddLine(new Vector2(origin.X, origin.Y + height), new Vector2(origin.X + width, origin.Y + height), ImGui.GetColorU32(T.LineCard));
        ImGui.SetCursorScreenPos(new Vector2(origin.X, origin.Y + height));
    }

    // ---- a) Statuszeile ----

    private void DrawStatusRow(float scale, UiState uiState, FishingAutomation automation, Configuration config, float contentWidth)
    {
        var T = UiTheme.Active;
        var (pillText, pillFg, pillBg, pillLine) = PillStyle(uiState, config);

        var origin = ImGui.GetCursorScreenPos();
        var pillHeight = 18f * scale;
        var dotRadius = 3.5f * scale;
        float pillTextWidth;
        using (PillFont.Push())
            pillTextWidth = ImGui.CalcTextSize(pillText).X;
        var pillPadX = 8f * scale;
        var pillWidth = pillPadX * 2f + dotRadius * 2f + 5f * scale + pillTextWidth;

        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(origin, origin + new Vector2(pillWidth, pillHeight), ImGui.GetColorU32(pillBg), pillHeight / 2f);
        dl.AddRect(origin, origin + new Vector2(pillWidth, pillHeight), ImGui.GetColorU32(pillLine), pillHeight / 2f);
        dl.AddCircleFilled(origin + new Vector2(pillPadX + dotRadius, pillHeight / 2f), dotRadius, ImGui.GetColorU32(pillFg));
        using (PillFont.Push())
            UiWidgets.DrawTextShadowed(dl, origin + new Vector2(pillPadX + dotRadius * 2f + 5f, (pillHeight - ImGui.GetTextLineHeight()) / 2f), pillFg, pillText, false);

        var context = ContextText(uiState, automation, config);
        if (!string.IsNullOrEmpty(context))
        {
            using (ContextFont.Push())
            {
                var contextWidth = ImGui.CalcTextSize(context).X;
                var contextHeight = ImGui.GetTextLineHeight();
                ImGui.SetCursorScreenPos(new Vector2(origin.X + contentWidth - contextWidth, origin.Y + (pillHeight - contextHeight) / 2f));
                ImGui.TextColored(T.TextMuted, context);
            }
        }

        ImGui.SetCursorScreenPos(new Vector2(origin.X, origin.Y + pillHeight));
    }

    private (string Text, Vector4 Fg, Vector4 Bg, Vector4 Line) PillStyle(UiState uiState, Configuration config)
    {
        var T = UiTheme.Active;
        return uiState switch
        {
            UiState.Waiting => (Loc.T("WARTEN", "WAITING"), T.Accent, T.Accent with { W = 0.1f }, WaitingBorder),
            UiState.Flying => (Loc.T("FLIEGT", "FLYING"), FlyingColor, FlyingBg, FlyingBorder),
            UiState.Prep => (Loc.T("VORBEREITUNG", "PREPARING"), T.Accent, T.Accent with { W = 0.1f }, WaitingBorder),
            UiState.Fishing => (Loc.T("ANGELT", "FISHING"), FishingGreen, FishingBg, FishingBorder),
            UiState.Attention => (Loc.T("ACHTUNG", "ATTENTION"), AttentionYellow, AttentionBg, AttentionBorder),
            _ => (Loc.T("IDLE", "IDLE"), T.TextSecondary, T.BgToggleOff, T.LineFrame),
        };
    }

    private string ContextText(UiState uiState, FishingAutomation automation, Configuration config)
    {
        switch (uiState)
        {
            case UiState.Waiting:
                return $"ET {FormatEorzeaTime(DateTime.UtcNow)}";
            case UiState.Flying:
                return config.FlyingMountId == 0 ? Loc.T("Mount-Roulette", "Mount Roulette") : GameActions.MountName(config.FlyingMountId);
            case UiState.Fishing:
                return Loc.T("Fenster offen", "Window open");
            case UiState.Attention:
                return Loc.T("Pausiert", "Paused");
            case UiState.Idle:
                return Loc.T($"{automation.GetPlannedFish(DateTime.UtcNow).Length} Fisch(e) geplant", $"{automation.GetPlannedFish(DateTime.UtcNow).Length} fish planned");
            default:
                return string.Empty;
        }
    }

    // ---- b) PhaseBar ----

    private void DrawPhaseBar(float scale, UiState uiState, float contentWidth)
    {
        var T = UiTheme.Active;
        var dl = ImGui.GetWindowDrawList();
        var labels = new[] { Loc.T("Warten", "Wait"), Loc.T("Fliegen", "Fly"), Loc.T("Vorbereiten", "Prep"), Loc.T("Angeln", "Fish") };
        var activeIndex = uiState switch { UiState.Waiting => 0, UiState.Flying => 1, UiState.Prep => 2, UiState.Fishing => 3, _ => -1 };

        var gap = 6f * scale;
        var segmentWidth = (contentWidth - gap * 3f) / 4f;
        var barHeight = 3f * scale;
        var origin = ImGui.GetCursorScreenPos();

        for (var i = 0; i < 4; i++)
        {
            var x = origin.X + i * (segmentWidth + gap);
            var done = i <= activeIndex;
            var barColor = done ? T.Accent : T.LineFrame;
            dl.AddRectFilled(new Vector2(x, origin.Y), new Vector2(x + segmentWidth, origin.Y + barHeight), ImGui.GetColorU32(barColor), 2f * scale);

            var labelFont = i == activeIndex ? PhaseLabelActiveFont : PhaseLabelFont;
            var labelColor = i == activeIndex ? T.TextHeading : (i < activeIndex ? T.TextSecondary : PhaseUpcomingLine);
            using (labelFont.Push())
            {
                var labelY = origin.Y + barHeight + 4f * scale;
                ImGui.SetCursorScreenPos(new Vector2(x, labelY));
                ImGui.TextColored(labelColor, labels[i]);
            }
        }

        float labelHeight;
        using (PhaseLabelFont.Push())
            labelHeight = ImGui.GetTextLineHeight();
        ImGui.SetCursorScreenPos(new Vector2(origin.X, origin.Y + barHeight + 4f * scale + labelHeight));
    }

    // ---- c) Fischzeile ----

    private void DrawFishRow(float scale, UiState uiState, BigFish? fish, FishingAutomation automation, float contentWidth)
    {
        var T = UiTheme.Active;
        if (fish == null)
            return;

        var itemSheet = Plugin.DataManager.GetExcelSheet<Item>();
        var origin = ImGui.GetCursorScreenPos();
        var iconDiameter = 38f * scale;

        var icon = itemSheet.TryGetRow(fish.ItemId, out var fishItem) ? fishItem.Icon : 0u;
        var wrap = icon != 0 ? GetIcon(icon).GetWrapOrEmpty() : null;
        UiWidgetsExtra.RoundIcon(origin + new Vector2(iconDiameter / 2f), iconDiameter / 2f, T.BgInput, T.Accent, wrap?.Handle);

        var baitId = fish.BaitIds.FirstOrDefault();
        var baitFound = itemSheet.TryGetRow(baitId, out var bait);
        var hasBait = baitId != 0 && baitFound;
        var baitSize = 22f * scale;
        var baitCountText = hasBait ? GameActions.GetInventoryItemCount(baitId).ToString(CultureInfo.InvariantCulture) : string.Empty;
        float baitTextWidth = 0f;
        if (hasBait)
            using (BaitCountFont.Push())
                baitTextWidth = ImGui.CalcTextSize(baitCountText).X;
        var baitBlockWidth = hasBait ? baitSize + 4f * scale + baitTextWidth : 0f;

        var textX = origin.X + iconDiameter + 10f * scale;
        var textMaxWidth = contentWidth - iconDiameter - 10f * scale - (hasBait ? baitBlockWidth + 8f * scale : 0f);

        var name = FishingAutomation.FishName(fish);
        var sub = FishRowSubtext(uiState, fish, automation);

        float nameHeight, subHeight;
        using (FishNameFont.Push())
            nameHeight = ImGui.GetTextLineHeight();
        using (FishSubFont.Push())
            subHeight = ImGui.GetTextLineHeight();
        var textBlockHeight = nameHeight + 2f * scale + subHeight;
        var textTop = origin.Y + (iconDiameter - textBlockHeight) / 2f;

        ImGui.SetCursorScreenPos(new Vector2(textX, textTop));
        using (FishNameFont.Push())
            ImGui.TextColored(T.TextHeading, UiWidgetsExtra.TruncateToWidth(name, textMaxWidth));

        ImGui.SetCursorScreenPos(new Vector2(textX, textTop + nameHeight + 2f * scale));
        using (FishSubFont.Push())
            ImGui.TextColored(T.TextDesc, UiWidgetsExtra.TruncateToWidth(sub, textMaxWidth));

        if (hasBait)
        {
            var dimmed = GameActions.GetInventoryItemCount(baitId) == 0;
            var baitX = origin.X + contentWidth - baitBlockWidth;
            var baitY = origin.Y + (iconDiameter - baitSize) / 2f;
            var dl = ImGui.GetWindowDrawList();
            dl.AddRect(new Vector2(baitX, baitY), new Vector2(baitX + baitSize, baitY + baitSize), ImGui.GetColorU32(T.LineFrame), 3f * scale);
            if (bait.Icon != 0)
            {
                var baitWrap = GetIcon(bait.Icon).GetWrapOrEmpty();
                dl.AddImageRounded(baitWrap.Handle, new Vector2(baitX, baitY), new Vector2(baitX + baitSize, baitY + baitSize), Vector2.Zero, Vector2.One,
                    ImGui.GetColorU32(new Vector4(1f, 1f, 1f, dimmed ? 0.55f : 1f)), 3f * scale);
            }
            using (BaitCountFont.Push())
                dl.AddText(new Vector2(baitX + baitSize + 4f * scale, origin.Y + (iconDiameter - ImGui.GetTextLineHeight()) / 2f),
                    ImGui.GetColorU32(dimmed ? CoralRed : T.TextPrimary), baitCountText);
            if (ImGui.IsMouseHoveringRect(new Vector2(baitX, baitY), new Vector2(baitX + baitBlockWidth, baitY + baitSize)))
                ImGui.SetTooltip($"{bait.Name}\n{Loc.T("Bestand", "Stock")}: {baitCountText}");
        }

        ImGui.SetCursorScreenPos(new Vector2(origin.X, origin.Y + iconDiameter));
    }

    private string FishRowSubtext(UiState uiState, BigFish fish, FishingAutomation automation)
    {
        var territorySheet = Plugin.DataManager.GetExcelSheet<TerritoryType>();
        var spotSheet = Plugin.DataManager.GetExcelSheet<FishingSpot>();
        var territoryName = territorySheet.TryGetRow(fish.TerritoryId, out var territory) ? territory.PlaceName.ValueNullable?.Name.ToString() : null;
        var spotName = spotSheet.TryGetRow(fish.FishingSpotId, out var spot) ? spot.PlaceName.ValueNullable?.Name.ToString() : null;
        var area = !string.IsNullOrEmpty(territoryName) && !string.IsNullOrEmpty(spotName) ? $"{territoryName} · {spotName}" : territoryName ?? spotName ?? string.Empty;

        return uiState switch
        {
            UiState.Flying => string.IsNullOrEmpty(area) ? Loc.T("→ Unterwegs...", "→ On the way...") : Loc.T($"→ {area}", $"→ {area}"),
            UiState.Fishing or UiState.Prep or UiState.Attention =>
                plugin.Configuration.FishAutoHookPresets.TryGetValue(fish.ItemId, out var preset) && !string.IsNullOrEmpty(preset)
                    ? $"{Loc.T("AutoHook", "AutoHook")}: {preset}"
                    : Loc.T("Kein AutoHook-Preset", "No AutoHook preset"),
            UiState.Idle => string.IsNullOrEmpty(area) ? Loc.T("Nächster im Plan", "Next in plan") : Loc.T($"Nächster im Plan · {area}", $"Next in plan · {area}"),
            _ => area,
        };
    }

    // ---- d) TimerBox ----

    private void DrawTimerBox(float scale, UiState uiState, BigFish fish, (BigFish Fish, FishWindow Window, DateTime FishUtc, float? Rarity)[] planned, DateTime now, float contentWidth)
    {
        var T = UiTheme.Active;
        var (label, value, fraction, barColor) = TimerContent(uiState, fish, planned, now);

        var padX = 12f * scale;
        var padY = 9f * scale;
        float labelHeight, valueHeight;
        using (TimerLabelFont.Push())
            labelHeight = ImGui.GetTextLineHeight();
        using (TimerValueFont.Push())
            valueHeight = ImGui.GetTextLineHeight();
        var rowHeight = MathF.Max(labelHeight, valueHeight);
        var barHeight = fraction != null ? 4f * scale + 6f * scale : 0f;
        var boxHeight = padY * 2f + rowHeight + barHeight;

        var origin = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(origin, origin + new Vector2(contentWidth, boxHeight), ImGui.GetColorU32(T.BgInput), 5f * scale);
        dl.AddRect(origin, origin + new Vector2(contentWidth, boxHeight), ImGui.GetColorU32(T.LineCard), 5f * scale);

        using (TimerLabelFont.Push())
            dl.AddText(origin + new Vector2(padX, padY + (rowHeight - labelHeight) / 2f), ImGui.GetColorU32(T.TextMuted), label);

        using (TimerValueFont.Push())
        {
            var valueWidth = ImGui.CalcTextSize(value).X;
            dl.AddText(origin + new Vector2(contentWidth - padX - valueWidth, padY + (rowHeight - valueHeight) / 2f), ImGui.GetColorU32(barColor ?? T.TextPrimary), value);
        }

        if (fraction != null)
        {
            ImGui.SetCursorScreenPos(origin + new Vector2(padX, padY + rowHeight + 6f * scale));
            UiWidgetsExtra.ProgressBar(fraction.Value, 4f, width: contentWidth - padX * 2f, fillColor: barColor, trackColor: T.BgToggleOff);
        }

        ImGui.SetCursorScreenPos(new Vector2(origin.X, origin.Y + boxHeight));
    }

    private (string Label, string Value, float? Fraction, Vector4? Color) TimerContent(UiState uiState, BigFish fish,
        (BigFish Fish, FishWindow Window, DateTime FishUtc, float? Rarity)[] planned, DateTime now)
    {
        var window = FishWindows.GetCurrentOrNext(fish, now);
        var leadMinutes = MathF.Max(1f, plugin.Configuration.FishAlertMinutes.GetValueOrDefault(fish.ItemId, 0));

        switch (uiState)
        {
            case UiState.Waiting:
            {
                var fishUtc = planned.Length > 0 ? planned[0].FishUtc : now;
                return (Loc.T("Abflug in", "Departure in"), OceanMainWindow.FormatCountdown(fishUtc > now ? fishUtc - now : TimeSpan.Zero), null, null);
            }
            case UiState.Flying:
            {
                var fishUtc = planned.FirstOrDefault(p => p.Fish.ItemId == fish.ItemId).FishUtc;
                var remaining = fishUtc > now ? fishUtc - now : TimeSpan.Zero;
                var fraction = 1f - Math.Clamp((float)(remaining.TotalMinutes / leadMinutes), 0f, 1f);
                return (Loc.T("Prep beginnt in", "Prep starts in"), OceanMainWindow.FormatCountdown(remaining), fraction, FlyingColor);
            }
            case UiState.Prep:
            {
                if (window == null)
                    return (Loc.T("Fenster öffnet in", "Window opens in"), "-", null, null);
                var remaining = window.Value.StartUtc > now ? window.Value.StartUtc - now : TimeSpan.Zero;
                var fraction = 1f - Math.Clamp((float)(remaining.TotalMinutes / leadMinutes), 0f, 1f);
                return (Loc.T("Fenster öffnet in", "Window opens in"), OceanMainWindow.FormatCountdown(remaining), fraction, UiTheme.Active.Accent);
            }
            case UiState.Fishing:
            {
                if (window == null)
                    return (Loc.T("Fenster schließt in", "Window closes in"), "-", null, FishingGreen);
                var remaining = window.Value.EndUtc > now ? window.Value.EndUtc - now : TimeSpan.Zero;
                var total = (window.Value.EndUtc - window.Value.StartUtc).TotalMinutes;
                var fraction = total > 0 ? Math.Clamp((float)(remaining.TotalMinutes / total), 0f, 1f) : 0f;
                return (Loc.T("Fenster schließt in", "Window closes in"), OceanMainWindow.FormatCountdown(remaining), fraction, FishingGreen);
            }
            default:
            {
                var fishUtc = planned.FirstOrDefault(p => p.Fish.ItemId == fish.ItemId).FishUtc;
                return (Loc.T("Nächstes Fenster in", "Next window in"), fishUtc > now ? OceanMainWindow.FormatCountdown(fishUtc - now) : "-", null, null);
            }
        }
    }

    // ---- e) Zustandsabhängiger Zusatz ----

    private void DrawUpNextBlock(float scale, (BigFish Fish, FishWindow Window, DateTime FishUtc, float? Rarity)[] planned, DateTime now, float contentWidth)
    {
        var T = UiTheme.Active;
        using (UpNextLabelFont.Push())
            UiWidgets.DrawSpacedText(Loc.T("ALS NÄCHSTES", "UP NEXT"), T.TextMuted, 1f);
        ImGui.Dummy(new Vector2(0f, 4f * scale));

        foreach (var entry in planned.Skip(1).Take(2))
        {
            var rowTop = ImGui.GetCursorScreenPos();
            var name = FishingAutomation.FishName(entry.Fish);
            var countdown = entry.Window.IsActive(now)
                ? Loc.T("Fenster aktiv", "Window active")
                : Loc.T($"in {OceanMainWindow.FormatCountdown(entry.FishUtc - now)}", $"in {OceanMainWindow.FormatCountdown(entry.FishUtc - now)}");

            float rowHeight;
            using (UpNextNameFont.Push())
                rowHeight = ImGui.GetTextLineHeight();

            using (UpNextNameFont.Push())
                ImGui.TextColored(T.TextSecondary, UiWidgetsExtra.TruncateToWidth(name, contentWidth * 0.55f));

            using (UpNextCountdownFont.Push())
            {
                var w = ImGui.CalcTextSize(countdown).X;
                ImGui.SetCursorScreenPos(new Vector2(rowTop.X + contentWidth - w, rowTop.Y + (rowHeight - ImGui.GetTextLineHeight()) / 2f));
                ImGui.TextColored(T.TextDesc, countdown);
            }

            ImGui.SetCursorScreenPos(new Vector2(rowTop.X, rowTop.Y + rowHeight + 4f * scale));
            ImGui.GetWindowDrawList().AddLine(new Vector2(rowTop.X, rowTop.Y + rowHeight + 2f * scale), new Vector2(rowTop.X + contentWidth, rowTop.Y + rowHeight + 2f * scale), ImGui.GetColorU32(T.LineRow));
        }
    }

    private void DrawStatTiles(float scale, FishingAutomation automation, float contentWidth)
    {
        var T = UiTheme.Active;
        var gap = 8f * scale;
        var tileWidth = (contentWidth - gap * 2f) / 3f;
        var tiles = new (string Label, string Value)[]
        {
            (Loc.T("Würfe", "Casts"), "-"),
            (Loc.T("Haken", "Hooks"), "-"),
            (Loc.T("Gefangen", "Caught"), automation.SessionCaught.ToString(CultureInfo.InvariantCulture)),
        };

        var origin = ImGui.GetCursorScreenPos();
        float labelHeight, valueHeight;
        using (StatLabelFont.Push())
            labelHeight = ImGui.GetTextLineHeight();
        using (StatValueFont.Push())
            valueHeight = ImGui.GetTextLineHeight();
        var padY = 8f * scale;
        var tileHeight = padY * 2f + valueHeight + 3f * scale + labelHeight;
        var dl = ImGui.GetWindowDrawList();

        for (var i = 0; i < tiles.Length; i++)
        {
            var x = origin.X + i * (tileWidth + gap);
            var min = new Vector2(x, origin.Y);
            var max = min + new Vector2(tileWidth, tileHeight);
            dl.AddRectFilled(min, max, ImGui.GetColorU32(T.BgStatus), 4f * scale);
            dl.AddRect(min, max, ImGui.GetColorU32(T.LineCard), 4f * scale);

            using (StatValueFont.Push())
            {
                var w = ImGui.CalcTextSize(tiles[i].Value).X;
                dl.AddText(min + new Vector2((tileWidth - w) / 2f, padY), ImGui.GetColorU32(T.TextHeading), tiles[i].Value);
            }
            using (StatLabelFont.Push())
            {
                var w = ImGui.CalcTextSize(tiles[i].Label).X;
                dl.AddText(min + new Vector2((tileWidth - w) / 2f, padY + valueHeight + 3f * scale), ImGui.GetColorU32(T.TextMuted), tiles[i].Label);
            }
        }

        ImGui.SetCursorScreenPos(new Vector2(origin.X, origin.Y + tileHeight));
    }

    private void DrawAttentionBox(float scale, string reason, float contentWidth)
    {
        var T = UiTheme.Active;
        var padX = 10f * scale;
        var padY = 9f * scale;
        var title = Loc.T("Problem erkannt", "Problem detected");

        var textWrapWidth = contentWidth - padX * 2f - 22f * scale;
        float titleHeight, bodyHeight, bodyLineHeight;
        using (WarningTitleFont.Push())
            titleHeight = ImGui.GetTextLineHeight();
        using (WarningBodyFont.Push())
        {
            bodyLineHeight = ImGui.GetTextLineHeight();
            bodyHeight = bodyLineHeight * CountWrappedLines(reason, textWrapWidth);
        }

        var boxHeight = padY * 2f + titleHeight + 3f * scale + bodyHeight;
        var origin = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(origin, origin + new Vector2(contentWidth, boxHeight), ImGui.GetColorU32(AttentionBg), 5f * scale);
        dl.AddRect(origin, origin + new Vector2(contentWidth, boxHeight), ImGui.GetColorU32(AttentionBorder), 5f * scale);

        var iconSize = 16f * scale;
        UiIcons.Draw(UiIcon.Warning, origin + new Vector2(padX, padY + 1f * scale), iconSize, ImGui.GetColorU32(AttentionYellow));

        var textX = origin.X + padX + iconSize + 8f * scale;
        ImGui.SetCursorScreenPos(new Vector2(textX, origin.Y + padY));
        using (WarningTitleFont.Push())
            ImGui.TextColored(AttentionYellow, title);

        ImGui.SetCursorScreenPos(new Vector2(textX, origin.Y + padY + titleHeight + 3f * scale));
        using (WarningBodyFont.Push())
        {
            ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + textWrapWidth);
            ImGui.TextColored(T.TextSecondary, reason);
            ImGui.PopTextWrapPos();
        }

        ImGui.SetCursorScreenPos(new Vector2(origin.X, origin.Y + boxHeight));
    }

    // ---- f) Aktionszeile ----

    private void DrawActionRow(float scale, UiState uiState, FishingAutomation automation, Configuration config, BigFish? fish, float contentWidth)
    {
        var T = UiTheme.Active;
        var iconButtonWidth = 38f * scale;
        var buttonHeight = 34f * scale;
        var gap = 6f * scale;
        var mainWidth = contentWidth - iconButtonWidth - gap;

        var origin = ImGui.GetCursorScreenPos();

        var (mainLabel, mainIcon, mainBg, mainFg, mainDisabled, mainAction) = MainButton(uiState, automation, config, fish);
        if (DrawActionButton(origin, new Vector2(mainWidth, buttonHeight), mainLabel, mainIcon, mainBg, mainFg, mainDisabled, scale))
            mainAction?.Invoke();

        var menuX = origin.X + mainWidth + gap;
        if (DrawIconActionButton(new Vector2(menuX, origin.Y), new Vector2(iconButtonWidth, buttonHeight), UiIcon.Sliders, Loc.T("Menü öffnen", "Open menu"), false, scale))
            plugin.OceanMainWindow.IsOpen = true;

        ImGui.SetCursorScreenPos(new Vector2(origin.X, origin.Y + buttonHeight));
    }

    private (string Label, UiIcon Icon, Vector4 Bg, Vector4 Fg, bool Disabled, System.Action? Action) MainButton(UiState uiState, FishingAutomation automation, Configuration config, BigFish? fish)
    {
        var T = UiTheme.Active;
        if (uiState == UiState.Idle)
        {
            var disabled = !automation.HasEnabledFish;
            return (Loc.T("Start", "Start"), UiIcon.Play, T.Accent, T.TextOnAccent, disabled, () => StartAutomation(automation, config));
        }

        if (uiState == UiState.Attention)
            return (Loc.T("Fortsetzen", "Resume"), UiIcon.Play, T.Accent, T.TextOnAccent, false, () => attentionDismissedUntilUtc = DateTime.UtcNow + TimeSpan.FromSeconds(60));

        return (Loc.T("Stopp", "Stop"), UiIcon.Stop, CoralRed, StopTextColor, false, automation.Stop);
    }

    // "config" wird hier nicht gebraucht (dieses Fenster ist per Definition bereits offen, wenn der
    // Nutzer hier auf Start klickt) - der Parameter bleibt trotzdem, damit der Aufruf an beiden
    // Stellen (Hauptknopf/Kompakt-Pille) identisch aussieht und sich bei Bedarf leicht erweitern lässt.
    private static void StartAutomation(FishingAutomation automation, Configuration config) => automation.Start();

    private bool DrawActionButton(Vector2 pos, Vector2 size, string label, UiIcon icon, Vector4 bg, Vector4 fg, bool disabled, float scale)
    {
        var dl = ImGui.GetWindowDrawList();
        ImGui.SetCursorScreenPos(pos);
        if (disabled)
            ImGui.BeginDisabled();
        var clicked = ImGui.InvisibleButton("##OceanOverlayMainAction", size);
        var hovered = !disabled && ImGui.IsItemHovered();
        if (disabled)
            ImGui.EndDisabled();

        var fillColor = disabled ? bg with { W = 0.35f } : bg with { W = hovered ? 0.85f : 1f };
        dl.AddRectFilled(pos, pos + size, ImGui.GetColorU32(fillColor), 5f * scale);

        var iconSize = 13f * scale;
        var iconGap = 7f * scale;
        float labelWidth;
        using (ActionButtonFont.Push())
            labelWidth = ImGui.CalcTextSize(label).X;
        var blockWidth = iconSize + iconGap + labelWidth;
        var contentX = pos.X + (size.X - blockWidth) / 2f;
        UiIcons.Draw(icon, new Vector2(contentX, pos.Y + (size.Y - iconSize) / 2f), iconSize, ImGui.GetColorU32(fg));
        using (ActionButtonFont.Push())
            dl.AddText(new Vector2(contentX + iconSize + iconGap, pos.Y + (size.Y - ImGui.GetTextLineHeight()) / 2f), ImGui.GetColorU32(fg), label);

        if (hovered)
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        return clicked && !disabled;
    }

    private bool DrawIconActionButton(Vector2 pos, Vector2 size, UiIcon icon, string tooltip, bool disabled, float scale)
    {
        var T = UiTheme.Active;
        var dl = ImGui.GetWindowDrawList();
        ImGui.SetCursorScreenPos(pos);
        if (disabled)
            ImGui.BeginDisabled();
        var clicked = ImGui.InvisibleButton($"##OceanOverlayIconAction_{icon}", size);
        var hovered = !disabled && ImGui.IsItemHovered();
        if (disabled)
            ImGui.EndDisabled();

        dl.AddRect(pos, pos + size, ImGui.GetColorU32(T.LineFrame), 5f * scale);
        if (hovered)
            dl.AddRectFilled(pos, pos + size, ImGui.GetColorU32(T.BgSelected), 5f * scale);

        var iconSize = 14f * scale;
        UiIcons.Draw(icon, pos + (size - new Vector2(iconSize)) / 2f, iconSize, ImGui.GetColorU32(disabled ? T.TextDisabled : T.TextSecondary));

        if (hovered)
        {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            UiNav.ShowTooltip(tooltip);
        }
        return clicked && !disabled;
    }

    // ---- Kompaktmodus ----

    private void DrawCompactPill(float scale, UiState uiState, FishingAutomation automation, Configuration config,
        (BigFish Fish, FishWindow Window, DateTime FishUtc, float? Rarity)[] planned, DateTime now)
    {
        var T = UiTheme.Active;
        var height = 44f * scale;
        var width = WindowWidth * scale;
        var origin = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();

        // Nichts darf außerhalb der Pillen-Rundung gezeichnet werden (Nutzer-Report: an den Ecken war
        // noch etwas außerhalb der Rundung sichtbar) - alles in dieser Methode strikt auf das Pillen-
        // Rechteck geklemmt, auch wenn ein einzelnes Element (Hover-Rechteck eines Knopfs usw.) aus
        // Versehen etwas darüber hinausragen sollte.
        dl.PushClipRect(origin, origin + new Vector2(width, height), true);

        var (_, pillFg, _, _) = PillStyle(uiState, config);

        dl.AddRectFilled(origin, origin + new Vector2(width, height), ImGui.GetColorU32(T.BgWindow with { W = Math.Clamp(config.OverlayOpacity, 0.4f, 1f) }), height / 2f);
        dl.AddRect(origin, origin + new Vector2(width, height), ImGui.GetColorU32(T.LineFrame), height / 2f);

        var padX = 12f * scale;
        var dotRadius = 4f * scale;
        var dotCenter = origin + new Vector2(padX + dotRadius, height / 2f);
        dl.AddCircle(dotCenter, dotRadius + 3f * scale, ImGui.GetColorU32(pillFg with { W = 0.2f }), 20, 2f * scale);
        dl.AddCircleFilled(dotCenter, dotRadius, ImGui.GetColorU32(pillFg));

        var fish = automation.CurrentTarget ?? (planned.Length > 0 ? planned[0].Fish : null);
        var nameX = dotCenter.X + dotRadius + 10f * scale;
        var expandSize = new Vector2(22f * scale, 22f * scale);
        var actionSize = new Vector2(28f * scale, 28f * scale);
        var rightReserved = expandSize.X + 6f * scale + actionSize.X + 10f * scale;

        if (fish != null)
        {
            var name = FishingAutomation.FishName(fish);
            using (CompactNameFont.Push())
            {
                ImGui.SetCursorScreenPos(new Vector2(nameX, origin.Y + (height - ImGui.GetTextLineHeight()) / 2f));
                ImGui.TextColored(T.TextHeading, UiWidgetsExtra.TruncateToWidth(name, 90f * scale));
            }

            var countdown = uiState == UiState.Idle
                ? string.Empty
                : FishWindows.GetCurrentOrNext(fish, now) is { } w && w.IsActive(now)
                    ? Loc.T("aktiv", "active")
                    : OceanMainWindow.FormatCountdown((planned.FirstOrDefault(p => p.Fish.ItemId == fish.ItemId).FishUtc is { } fu && fu > now ? fu - now : TimeSpan.Zero));
            if (!string.IsNullOrEmpty(countdown))
            {
                using (CompactCountdownFont.Push())
                {
                    var cdX = origin.X + width - rightReserved - ImGui.CalcTextSize(countdown).X - 10f * scale;
                    ImGui.SetCursorScreenPos(new Vector2(cdX, origin.Y + (height - ImGui.GetTextLineHeight()) / 2f));
                    ImGui.TextColored(T.TextSecondary, countdown);
                }
            }
        }

        var running = automation.IsRunning;
        var actionX = origin.X + width - 10f * scale - actionSize.X;
        var actionPos = new Vector2(actionX, origin.Y + (height - actionSize.Y) / 2f);
        ImGui.SetCursorScreenPos(actionPos);
        var actionClicked = ImGui.InvisibleButton("##OceanOverlayCompactAction", actionSize);
        var actionHovered = ImGui.IsItemHovered();
        if (actionHovered)
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

        var actionBg = running ? CoralRed : T.Accent;
        dl.AddCircleFilled(actionPos + actionSize / 2f, actionSize.X / 2f, ImGui.GetColorU32(actionBg with { W = actionHovered ? 0.85f : 1f }));
        if (actionHovered)
            dl.AddCircle(actionPos + actionSize / 2f, actionSize.X / 2f + 2f * scale, ImGui.GetColorU32(actionBg with { W = 0.5f }), 24, 1.5f * scale);
        var actionIconSize = 12f * scale;
        UiIcons.Draw(running ? UiIcon.Stop : UiIcon.Play, actionPos + (actionSize - new Vector2(actionIconSize)) / 2f, actionIconSize, ImGui.GetColorU32(running ? StopTextColor : T.TextOnAccent));
        if (actionClicked)
        {
            if (running)
                automation.Stop();
            else
                StartAutomation(automation, config);
        }

        var expandX = actionX - 6f * scale - expandSize.X;
        var expandPos = new Vector2(expandX, origin.Y + (height - expandSize.Y) / 2f);
        ImGui.SetCursorScreenPos(expandPos);
        if (UiNav.IconButton(UiIcon.ChevronDown, "##OceanOverlayExpand", expandSize, Loc.T("Aufklappen", "Expand")))
        {
            config.OverlayCompact = false;
            config.Save();
        }

        // Ziehen über die Pille (außer den beiden Knöpfen) verschiebt das Fenster - bewusst auf den
        // Bereich VOR "expandX" begrenzt, damit es Klicks auf Start/Stop/Aufklappen nicht überlagert
        // (nicht überlappend, daher egal, dass dieser Button erst jetzt am Ende registriert wird).
        ImGui.SetCursorScreenPos(origin);
        ImGui.InvisibleButton("##OceanOverlayCompactDrag", new Vector2(expandX - origin.X, height));
        if (ImGui.IsItemActive() && ImGui.IsMouseDragging(ImGuiMouseButton.Left))
        {
            var delta = ImGui.GetMouseDragDelta(ImGuiMouseButton.Left);
            ImGui.ResetMouseDragDelta(ImGuiMouseButton.Left);
            ImGui.SetWindowPos(ImGui.GetWindowPos() + delta);
        }

        dl.PopClipRect();
        ImGui.SetCursorScreenPos(new Vector2(origin.X, origin.Y + height));
    }

    // ---- Hilfen ----

    // Standard-Eorzea-Zeit-Formel (1 Eorzea-Stunde = 175 Echtsekunden) - nur für die Kontext-Anzeige
    // "ET hh:mm" im Wartezustand, keine Spielzustands-Abfrage nötig.
    private static string FormatEorzeaTime(DateTime utc)
    {
        var eorzeaSeconds = new DateTimeOffset(utc).ToUnixTimeSeconds() * 3600 / 175;
        var minutes = eorzeaSeconds / 60 % 60;
        var hours = eorzeaSeconds / 3600 % 24;
        return $"{hours:00}:{minutes:00}";
    }

    /// <summary>Grobe Zeilenanzahl für Fließtext bei "wrapWidth" (Wort für Wort, wie UiWidgetsExtra.
    /// CenteredWrappedText) - nur um die Höhe eines nachfolgend per ImGui.TextWrapped gezeichneten Texts
    /// VORAB zu kennen (für eine Box mit inhaltsabhängiger Höhe), ohne auf eine CalcTextSize-Überladung
    /// mit Wrap-Breite angewiesen zu sein (deren Verfügbarkeit in diesem Binding unklar ist).</summary>
    private static int CountWrappedLines(string text, float wrapWidth)
    {
        var lines = 1;
        var lineWidth = 0f;
        var spaceWidth = ImGui.CalcTextSize(" ").X;
        foreach (var word in text.Split(' '))
        {
            var wordWidth = ImGui.CalcTextSize(word).X;
            var next = lineWidth == 0f ? wordWidth : lineWidth + spaceWidth + wordWidth;
            if (next > wrapWidth && lineWidth > 0f)
            {
                lines++;
                lineWidth = wordWidth;
            }
            else
            {
                lineWidth = next;
            }
        }
        return lines;
    }

    private ISharedImmediateTexture GetIcon(uint iconId)
    {
        if (!iconCache.TryGetValue(iconId, out var texture))
        {
            texture = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(iconId));
            iconCache[iconId] = texture;
        }
        return texture;
    }
}
