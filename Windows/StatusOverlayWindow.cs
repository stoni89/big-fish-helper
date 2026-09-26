using System;
using System.Numerics;
using Dalamud.Interface.Windowing;
using Dalamud.Bindings.ImGui;

namespace BigFishHelper.Windows;

/// <summary>
/// Kleines, unabhängiges Status-Fenster (siehe Configuration.ShowOverlayOnStart) - zeigt nur das
/// Nötigste, um nebenbei sehen zu können, was die Automation gerade macht und wann es weitergeht
/// (automation.StatusText deckt bereits jeden Zustand ab: Fliegen/Landen/Angeln/Warten samt
/// Countdown, siehe FishingAutomation), ohne das ganze Options-Fenster offen halten zu müssen. Wird
/// per config.ShowOverlayOnStart automatisch beim Klick auf "Start" geöffnet (siehe MainWindow.
/// DrawPlayPage) und schließt sich automatisch wieder, sobald die Automation nicht mehr läuft (egal
/// wodurch - Stop-Knopf hier, im Hauptfenster oder ein automatischer Abbruch) - es soll nur sichtbar
/// sein, solange tatsächlich etwas läuft. Bewusst eigene, KOMPAKTE Panels statt ModernUi.BeginCard/
/// EndCard - deren CardTrailingGap (26px, für die vollen Options-Seiten gedacht) wäre hier viel zu
/// viel Luft für ein kleines Overlay, das man dauerhaft nebenbei offen lässt.
/// </summary>
public class StatusOverlayWindow : Window
{
    private readonly Plugin plugin;

    private static readonly Vector4 RunningColor = new(0.45f, 0.9f, 0.45f, 1f);
    private static readonly Vector4 StopRed = new(0.85f, 0.3f, 0.35f, 1f);
    private static readonly Vector4 StopRedHover = new(0.92f, 0.38f, 0.42f, 1f);

    public StatusOverlayWindow(Plugin plugin) : base(
        "Big Fish Helper##BigFishHelperStatusOverlay",
        ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        this.plugin = plugin;
        RespectCloseHotkey = false;
        // Groß genug, damit auf Anhieb alles ohne Größenänderung sichtbar ist (Status-Karte +
        // "Als Nächstes"-Karte + Stop-Knopf) - vorher war die Startgröße knapper als der tatsächlich
        // benötigte Platz, wodurch (ohne Scrollbar, siehe NoScrollbar oben) Teile abgeschnitten waren.
        Size = new Vector2(300, 235);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(240, 190),
            MaximumSize = new Vector2(700, 500),
        };
    }

    public void Dispose() { }

    public override void PreDraw()
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowMinSize, new Vector2(1f, 1f));
        // Mehr Abstand zwischen den beiden Titelleisten-Knöpfen (Einklappen/Schließen) - sitzen
        // sonst arg zusammengequetscht in der Ecke.
        ImGui.PushStyleVar(ImGuiStyleVar.ItemInnerSpacing, new Vector2(10f, 4f));
        // Knapperes Fensterpolster als der ModernUi-Standard (12,12) - kompakteres Gesamtbild.
        ModernUi.PushStyle(new Vector2(10f, 8f));

        // Titelleiste in derselben Farbe wie der restliche Fensterhintergrund, statt im
        // Standard-Dalamud-Blauton - fällt sonst als Fremdkörper auf.
        ImGui.PushStyleColor(ImGuiCol.TitleBg, ModernUi.WindowBg);
        ImGui.PushStyleColor(ImGuiCol.TitleBgActive, ModernUi.WindowBg);
        ImGui.PushStyleColor(ImGuiCol.TitleBgCollapsed, ModernUi.WindowBg);
    }

    public override void PostDraw()
    {
        ImGui.PopStyleColor(3);
        ModernUi.PopStyle();
        ImGui.PopStyleVar(2);
    }

    public override void Draw()
    {
        var automation = plugin.Automation;

        // Nur sichtbar, solange die Automation läuft (siehe Klassen-Kommentar).
        if (!automation.IsRunning)
        {
            IsOpen = false;
            return;
        }

        // Status-Punkt als gezeichneter Kreis statt Textzeichen ("●") - das Zeichen fehlte im
        // geladenen Font-Subset und wurde stattdessen als Tofu/Fragezeichen dargestellt.
        var cursor = ImGui.GetCursorScreenPos();
        var lineHeight = ImGui.GetTextLineHeight();
        const float dotRadius = 4f;
        var dotCenter = new Vector2(cursor.X + dotRadius, cursor.Y + lineHeight * 0.5f);
        ImGui.GetWindowDrawList().AddCircleFilled(dotCenter, dotRadius, ImGui.GetColorU32(RunningColor));
        ImGui.SetCursorScreenPos(new Vector2(cursor.X + dotRadius * 2f + 6f, cursor.Y));
        ImGui.TextColored(RunningColor, Loc.T("Läuft", "Running"));

        ImGui.Dummy(new Vector2(0f, 3f));

        // Aktueller Status (Fliegen/Landen/Angeln/Warten samt Countdown) - im eigenen Panel, damit
        // er sich optisch vom Rest abhebt.
        BeginPanel();
        var status = automation.StatusText;
        ImGui.PushTextWrapPos(ImGui.GetContentRegionMax().X);
        ImGui.TextUnformatted(string.IsNullOrEmpty(status) ? "-" : status);
        ImGui.PopTextWrapPos();
        EndPanel();

        // Der nächste geplante Fisch samt Countdown - auch dann hilfreich, wenn der Status gerade
        // z.B. "Fliege zu X..." zeigt und man wissen will, WER als Nächstes drankommt.
        var planned = automation.GetPlannedFish(DateTime.UtcNow);
        if (planned.Length > 0)
        {
            ImGui.TextColored(ModernUi.TextMuted, Loc.T("Als Nächstes", "Up next"));
            ImGui.Dummy(new Vector2(0f, 1f));

            BeginPanel();
            var next = planned[0];
            var now = DateTime.UtcNow;
            var countdown = next.Window.IsActive(now)
                ? Loc.T("Fenster aktiv", "Window active")
                : Loc.T($"in {MainWindow.FormatCountdown(next.FishUtc - now)}", $"in {MainWindow.FormatCountdown(next.FishUtc - now)}");
            ImGui.TextUnformatted(FishingAutomation.FishName(next.Fish));
            ImGui.SameLine();
            ImGui.TextColored(ModernUi.TextMuted, $"- {countdown}");
            EndPanel();
        }

        // Stop-Knopf ganz unten - die Automation lässt sich so auch stoppen, ohne extra das
        // Hauptfenster öffnen zu müssen. Schließt dieses Overlay danach automatisch (siehe oben).
        ImGui.PushStyleColor(ImGuiCol.Button, StopRed);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, StopRedHover);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, StopRed);
        ImGui.PushStyleColor(ImGuiCol.Text, Vector4.One);
        if (ImGui.Button(Loc.T("Stop", "Stop") + "##OverlayStop", new Vector2(ImGui.GetContentRegionAvail().X, 28f)))
            automation.Stop();
        ImGui.PopStyleColor(4);
    }

    /// <summary>Schlankes Panel (kleine Innenabstände, kein großzügiger Außenabstand danach - siehe Klassen-Kommentar).</summary>
    private static void BeginPanel()
    {
        ImGui.BeginGroup();
        ImGui.GetWindowDrawList().ChannelsSplit(2);
        ImGui.GetWindowDrawList().ChannelsSetCurrent(1);
    }

    private static void EndPanel()
    {
        ImGui.EndGroup();
        const float padding = 7f;
        var min = ImGui.GetItemRectMin() - new Vector2(padding, padding);
        var max = ImGui.GetItemRectMax() + new Vector2(padding, padding);

        var drawList = ImGui.GetWindowDrawList();
        drawList.ChannelsSetCurrent(0);
        drawList.AddRectFilled(min, max, ImGui.ColorConvertFloat4ToU32(ModernUi.CardBg), 8f);
        drawList.AddRect(min, max, ImGui.ColorConvertFloat4ToU32(ModernUi.CardBorder), 8f);
        drawList.ChannelsMerge();

        ImGui.Dummy(new Vector2(0f, padding));
    }
}
