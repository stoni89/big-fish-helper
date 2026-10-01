using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Interface;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Bindings.ImGui;

namespace BigFishHelper.Windows;

/// <summary>
/// Kleines Set wiederverwendbarer ImGui-Bausteine für einen moderneren Look des Optionsfensters
/// (dunkler Verlaufshintergrund, abgerundete Karten mit sanftem Leuchtrahmen, animierte Toggle-
/// Switches, Icon-Sidebar) - ImGui bietet dafür von sich aus nichts, alles hier wird manuell per
/// Draw-List gezeichnet. Hält dafür etwas eigenen Animationszustand (animTime/animDelta/
/// animatedValues) - bewusst auf einfache, pro-ID interpolierte float-Werte beschränkt, keine
/// Abhängigkeit zu einem bestimmten Fenster oder zu Plugin/Configuration.
/// </summary>
public static class ModernUi
{
    private static float animTime;
    private static float animDelta;

    /// <summary>Von MainWindow.PreDraw einmal pro Frame mit ImGui.GetIO().DeltaTime aufgerufen.</summary>
    public static void AdvanceAnimationTime(float deltaSeconds)
    {
        animDelta = deltaSeconds;
        animTime += deltaSeconds;
    }

    // Pro-ID weich interpolierter Wert (z.B. Toggle-Knopf-Position, Sidebar-Hover-Helligkeit) -
    // exponentielle Annäherung an target, Geschwindigkeit über speed (höher = schneller).
    private static readonly Dictionary<string, float> animatedValues = new();

    private static float Animate(string id, float target, float speed)
    {
        if (!animatedValues.TryGetValue(id, out var current))
            current = target;

        var t = Math.Clamp(animDelta * speed, 0f, 1f);
        current += (target - current) * t;
        animatedValues[id] = current;
        return current;
    }

    private static Vector4 LerpColor(Vector4 a, Vector4 b, float t) =>
        new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t, a.W + (b.W - a.W) * t);

    public static readonly Vector4 CardBg = new(0.13f, 0.21f, 0.33f, 0.92f);
    public static readonly Vector4 CardBorder = new(1f, 1f, 1f, 0.06f);
    // Helles Blau als Plugin-Farbe (Big Fish Helper) - auch die Hintergründe sind blau statt grau-schwarz.
    public static readonly Vector4 Accent = new(0.55f, 0.83f, 1f, 1f);
    public static readonly Vector4 AccentHover = new(0.68f, 0.89f, 1f, 1f);
    public static readonly Vector4 TextMuted = new(0.64f, 0.73f, 0.84f, 1f);
    public static readonly Vector4 ToggleOff = new(0.24f, 0.33f, 0.46f, 1f);
    public static readonly Vector4 ToggleOffHover = new(0.30f, 0.40f, 0.54f, 1f);
    public static readonly Vector4 SidebarHover = new(1f, 1f, 1f, 0.06f);
    public static readonly Vector4 SidebarSelected = new(0.55f, 0.83f, 1f, 0.18f);
    public static readonly Vector4 WindowBg = new(0.08f, 0.13f, 0.21f, 1f);

    // Text/Icons auf Flächen in der (hellen) Akzentfarbe - dunkel statt weiß, sonst kaum lesbar.
    public static readonly Vector4 TextOnAccent = new(0.05f, 0.09f, 0.16f, 1f);

    // Card-Innenabstand links (per ImGui.Indent in BeginCard) UND rechts - rechts gibt es dafür
    // keine ImGui-Bordfunktion, daher müssen alle rechtsbündigen Helfer hier (LabelRow, ToggleRow,
    // TextDisabledWrapped) ihre verfügbare Breite explizit um diesen Wert verkleinern, sonst reicht
    // ihr Inhalt bis an den echten Fensterrand, während links durch Indent() schon 14px Abstand ist.
    public const float CardMargin = 14f;

    /// <summary>
    /// Setzt globale Stil-Werte (Rundungen, Abstände, Grundfarben) für einen moderneren Look - muss
    /// VOR ImGui.Begin() aufgerufen werden (also in PreDraw(), nicht in Draw()!), sonst greift der
    /// Innenabstand (WindowPadding) nicht für das äußere Fenster selbst, da dessen Content-Bereich
    /// schon beim Begin()-Aufruf mit dem bis dahin aktiven Wert berechnet wird. Muss von der
    /// Aufrufstelle immer mit PopStyle() beendet werden (z.B. in PostDraw()).
    /// </summary>
    public static void PushStyle(Vector2? windowPadding = null)
    {
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 14f);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 10f);
        ImGui.PushStyleVar(ImGuiStyleVar.GrabRounding, 10f);
        ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarRounding, 10f);
        ImGui.PushStyleVar(ImGuiStyleVar.TabRounding, 10f);
        // Standardmäßig ein schmaler "Griff", der auf der Schiene schwimmt - das ließ Slider neben
        // den (voll ausgefüllten) Dropdown-Boxen kleiner/dünner wirken, obwohl die Box selbst exakt
        // gleich hoch ist (beide nutzen dasselbe FramePadding). Ein breiterer Griff gleicht das an.
        ImGui.PushStyleVar(ImGuiStyleVar.GrabMinSize, 24f);
        // Y-Anteile bewusst knapper als X (10/8) - die X-Werte betreffen den horizontalen Abstand
        // z.B. zwischen Beschriftung und Regler in derselben Zeile, die Y-Werte die Zeilenhöhe -
        // hier klein zu gehen macht JEDEN Einstellungspunkt (Toggle, Slider, Dropdown, ...) niedriger.
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(10f, 9f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, windowPadding ?? new Vector2(12f, 12f));
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(10f, 5f));

        ImGui.PushStyleColor(ImGuiCol.WindowBg, WindowBg);
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0f, 0f, 0f, 0f));
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.93f, 0.94f, 0.97f, 1f));
        ImGui.PushStyleColor(ImGuiCol.FrameBg, new Vector4(0.17f, 0.26f, 0.39f, 1f));
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, new Vector4(0.21f, 0.31f, 0.45f, 1f));
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, new Vector4(0.24f, 0.35f, 0.50f, 1f));
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.20f, 0.30f, 0.44f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.26f, 0.37f, 0.52f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, new Vector4(0.30f, 0.42f, 0.58f, 1f));
        ImGui.PushStyleColor(ImGuiCol.SliderGrab, Accent);
        ImGui.PushStyleColor(ImGuiCol.SliderGrabActive, AccentHover);
        ImGui.PushStyleColor(ImGuiCol.CheckMark, Accent);
        ImGui.PushStyleColor(ImGuiCol.PopupBg, new Vector4(0.10f, 0.16f, 0.25f, 0.98f));
        ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(1f, 1f, 1f, 0.05f));
        ImGui.PushStyleColor(ImGuiCol.Separator, new Vector4(1f, 1f, 1f, 0.08f));

        // Scrollbars (z.B. der Fischtabelle) in der Akzentfarbe statt dem ImGui-Standard-Grau, damit
        // sie zum Rest des (blauen) Plugin-Looks passen (Nutzeranforderung).
        ImGui.PushStyleColor(ImGuiCol.ScrollbarBg, new Vector4(0.10f, 0.16f, 0.25f, 0.6f));
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrab, Accent);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabHovered, AccentHover);
        ImGui.PushStyleColor(ImGuiCol.ScrollbarGrabActive, AccentHover);

        // Tabs (siehe z.B. Fischdaten-Seite: ein Tab pro Addon) - sonst der Standard-ImGui-Look
        // (helles Grau/eckig), passt farblich nicht zum Rest (dunkles Blau, abgerundet, Akzentfarbe).
        ImGui.PushStyleColor(ImGuiCol.Tab, new Vector4(0f, 0f, 0f, 0f));
        ImGui.PushStyleColor(ImGuiCol.TabHovered, SidebarHover);
        ImGui.PushStyleColor(ImGuiCol.TabActive, SidebarSelected);
        ImGui.PushStyleColor(ImGuiCol.TabUnfocused, new Vector4(0f, 0f, 0f, 0f));
        ImGui.PushStyleColor(ImGuiCol.TabUnfocusedActive, SidebarSelected);
    }

    public static void PopStyle()
    {
        ImGui.PopStyleColor(22);
        ImGui.PopStyleVar(9);
    }

    /// <summary>
    /// Einzelner, quadratischer Icon-Button für die schmale äußere Navigationsleiste (wie im
    /// Referenzdesign links außen) - horizontal zentriert in der verfügbaren Breite.
    /// </summary>
    // Icon-Schriftart hätte in normaler Größe sonst viel Luft um ein recht kleines Glyph -
    // Hochskalieren macht das Icon selbst größer, statt nur den (gleich großen) Button drumherum.
    private const float RailIconScale = 1.7f;

    private static IFontHandle? railIconFontHandle;

    /// <summary>
    /// Eigene, in nativer Pixelgröße gebaute Variante der Icon-Schrift für die Rail-Buttons -
    /// vorher wurde die normale (kleine) Icon-Schrift per SetWindowFontScale hochskaliert, was
    /// sichtbar verschwommen aussah (Texturvergrößerung statt echter Schriftgröße), genau wie beim
    /// Titeltext im Fensterkopf (siehe MainWindow.GetTitleFontHandle).
    /// </summary>
    private static IFontHandle GetRailIconFontHandle()
    {
        railIconFontHandle ??= Plugin.PluginInterface.UiBuilder.FontAtlas.NewDelegateFontHandle(e => e.OnPreBuild(tk =>
            tk.AddFontAwesomeIconFont(new SafeFontConfig
            {
                SizePx = Plugin.PluginInterface.UiBuilder.FontDefaultSizePx * RailIconScale,
            })));
        return railIconFontHandle;
    }

    public static bool RailButton(FontAwesomeIcon icon, bool selected, string? tooltip = null, bool showDot = false)
    {
        const float size = 42f;

        var avail = ImGui.GetContentRegionAvail().X;
        var offsetX = (avail - size) * 0.5f;
        if (offsetX > 0f)
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + offsetX);

        ImGui.PushStyleColor(ImGuiCol.Button, selected ? Accent : new Vector4(0f, 0f, 0f, 0f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, selected ? AccentHover : SidebarHover);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, AccentHover);
        // Icon selbst gedämpfter grau statt des globalen (fast weißen) Text-Standards, solange
        // nicht ausgewählt - im ausgewählten Zustand bleibt es dagegen kräftig weiß.
        ImGui.PushStyleColor(ImGuiCol.Text, selected ? TextOnAccent : TextMuted);

        bool clicked;
        var railFontHandle = GetRailIconFontHandle();
        if (railFontHandle is { Available: true })
        {
            using (railFontHandle.Push())
                clicked = ImGui.Button($"{icon.ToIconString()}##rail_{icon}", new Vector2(size, size));
        }
        else
        {
            // Schrift noch nicht fertig gebaut (z.B. kurz nach dem Start) - übergangsweise die alte
            // Hochskalierungs-Methode, damit trotzdem sofort ein Icon zu sehen ist.
            using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
            {
                ImGui.SetWindowFontScale(RailIconScale);
                clicked = ImGui.Button($"{icon.ToIconString()}##rail_{icon}", new Vector2(size, size));
                ImGui.SetWindowFontScale(1f);
            }
        }

        ImGui.PopStyleColor(4);

        // Roter Punkt oben rechts am Button, z.B. für "hier fehlt etwas" (fehlendes benötigtes
        // Plugin) - bewusst NACH PopStyleColor auf dem Item-Rect des schon fertig gezeichneten
        // Buttons plaziert, statt selbst ein eigenes Item zu sein.
        if (showDot)
        {
            const float dotDiameter = 10f;
            var buttonMin = ImGui.GetItemRectMin();
            var buttonMax = ImGui.GetItemRectMax();
            var dotCenter = new Vector2(buttonMax.X, buttonMin.Y) + new Vector2(-dotDiameter * 0.35f, dotDiameter * 0.35f);
            var drawList = ImGui.GetWindowDrawList();
            drawList.AddCircleFilled(dotCenter, dotDiameter * 0.5f + 1.5f, ImGui.ColorConvertFloat4ToU32(WindowBg), 12);
            drawList.AddCircleFilled(dotCenter, dotDiameter * 0.5f, ImGui.ColorConvertFloat4ToU32(new Vector4(0.95f, 0.3f, 0.35f, 1f)), 12);
        }

        if (tooltip != null && ImGui.IsItemHovered())
            ImGui.SetTooltip(tooltip);

        return clicked;
    }

    /// <summary>
    /// Startet eine abgerundete "Karte" um die nachfolgend gezeichneten Widgets - der Hintergrund
    /// wird per Draw-List-Channel HINTER den Inhalt gezeichnet (Standard-ImGui-Trick, da die
    /// Draw-List sonst nur in Zeichenreihenfolge - und damit immer OBEN - zeichnen könnte). Jeder
    /// Aufruf muss mit EndCard() beendet werden.
    /// </summary>
    public static void BeginCard()
    {
        ImGui.Indent(CardMargin);
        ImGui.BeginGroup();
        ImGui.GetWindowDrawList().ChannelsSplit(2);
        ImGui.GetWindowDrawList().ChannelsSetCurrent(1);
    }

    // Deutlich knapper als CardMargin (14, für links/rechts nötig, damit der Kartenhintergrund mit
    // dem eingerückten Inhalt UND der GroupLabel-Überschrift darüber fluchtet) - nur oben/unten gab
    // es keinen Grund für denselben großzügigen Wert, das ließ jede Karte unnötig hoch wirken.
    public const float CardVerticalPadding = 10f;

    // Abstand NACH einer Karte (bis zur nächsten Überschrift/Karte) - bewusst eigener, größerer Wert
    // statt CardVerticalPadding wiederzuverwenden: CardVerticalPadding bestimmt zusätzlich die
    // Karten-INNENhöhe, das hier soll nur den Außenabstand danach vergrößern, ohne die Karte selbst
    // wieder aufzublähen.
    private const float CardTrailingGap = 26f;

    public static void EndCard(float paddingX = CardMargin, float paddingY = CardVerticalPadding, Vector4? borderColor = null)
    {
        ImGui.EndGroup();

        var min = ImGui.GetItemRectMin() - new Vector2(paddingX, paddingY);
        var max = ImGui.GetItemRectMax() + new Vector2(paddingX, paddingY);

        var drawList = ImGui.GetWindowDrawList();
        drawList.ChannelsSetCurrent(0);

        const float rounding = 16f;

        // Weicher Schatten - mehrere nach unten versetzte, zunehmend blassere Rechtecke statt
        // eines echten Blurs (den gibt ImGui-Draw-Lists nicht her).
        for (var i = 3; i >= 1; i--)
        {
            var offset = new Vector2(0f, i * 2.5f);
            var alpha = 0.035f * i;
            drawList.AddRectFilled(min + offset, max + offset, ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, alpha)), rounding);
        }

        // Flache Füllung - ein echter Verlauf (AddRectFilledMultiColor) unterstützt keine Rundung
        // (eckige Ecken unter dem runden Rahmen), und ein separates Tönungs-Band oben sah als
        // sichtbar abgesetzter Streifen in der ersten Zeile falsch aus - einfach einfarbig, der
        // pulsierende Rahmen liefert den Akzent.
        drawList.AddRectFilled(min, max, ImGui.ColorConvertFloat4ToU32(CardBg), rounding);

        // Leicht pulsierender, akzentfarbener Rahmen-Schimmer statt eines starren Rands.
        var pulse = 0.5f + 0.5f * MathF.Sin(animTime * 1.3f);
        var glowBorder = LerpColor(borderColor ?? CardBorder, Accent, 0.25f + 0.2f * pulse);
        glowBorder.W = 0.35f + 0.15f * pulse;
        drawList.AddRect(min, max, ImGui.ColorConvertFloat4ToU32(glowBorder), rounding, ImDrawFlags.None, 1.5f);

        drawList.ChannelsMerge();

        ImGui.Unindent(CardMargin);
        ImGui.Dummy(new Vector2(0f, CardTrailingGap));
    }

    // Fester, gleicher Abstand ÜBER und UNTER der Trennlinie (siehe CardDivider) - per Draw-List
    // statt ImGui.Separator() (dessen eigene Innenabstände nicht exakt symmetrisch sind), damit der
    // Einstellungstext/Toggle/Combobox einer Zeile exakt vertikal mittig zwischen der Linie darüber
    // und der Linie darunter sitzt (Nutzeranforderung/Screenshot).
    private const float CardDividerPadding = 9f;

    /// <summary>
    /// Dünne horizontale Trennlinie ZWISCHEN mehreren Einstellungen innerhalb derselben Karte (z.B.
    /// Mount-Auswahl und Sprint-Umschalter im "Anflug"-Block) - NICHT zu verwechseln mit der Linie in
    /// SectionHeader (die trennt Titel/Hilfstext von den Karten darunter, nicht einzelne Zeilen
    /// INNERHALB einer Karte).
    /// </summary>
    public static void CardDivider()
    {
        var width = ImGui.GetContentRegionAvail().X;
        var pos = ImGui.GetCursorScreenPos();
        var lineY = pos.Y + CardDividerPadding;
        ImGui.GetWindowDrawList().AddLine(new Vector2(pos.X, lineY), new Vector2(pos.X + width, lineY), ImGui.GetColorU32(ImGuiCol.Separator), 1f);
        ImGui.Dummy(new Vector2(0f, CardDividerPadding * 2f));
    }

    /// <summary>
    /// Kleine Zwischenüberschrift direkt über einer Karte (z.B. "Match intro" im Referenzdesign) -
    /// um CardMargin eingerückt, damit sie mit dem eingerückten Karteninhalt darunter fluchtet statt
    /// mit dem (weiter links liegenden) Kartenrand. Mit kleinem, sanft pulsierendem Akzent-Punkt
    /// davor.
    /// </summary>
    public static void GroupLabel(string text)
    {
        ImGui.Indent(CardMargin);

        const float dotRadius = 3.5f;
        var lineHeight = ImGui.GetTextLineHeight() * 1.2f;
        var dotCenter = ImGui.GetCursorScreenPos() + new Vector2(dotRadius, lineHeight * 0.5f);
        var pulse = 0.6f + 0.4f * (0.5f + 0.5f * MathF.Sin(animTime * 2f));
        ImGui.GetWindowDrawList().AddCircleFilled(dotCenter, dotRadius, ImGui.ColorConvertFloat4ToU32(new Vector4(Accent.X, Accent.Y, Accent.Z, pulse)), 16);
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + dotRadius * 2f + 8f);

        ImGui.SetWindowFontScale(1.2f);
        ImGui.TextUnformatted(text);
        ImGui.SetWindowFontScale(1f);
        ImGui.Unindent(CardMargin);
        ImGui.Dummy(new Vector2(0f, 14f));
    }

    /// <summary>
    /// Größere, fette Überschrift + gedämpfter Untertext darunter - für den Titel oben in jedem
    /// Tab-Inhalt (entspricht "In match" / "The social touches..." im Referenzdesign). Darunter
    /// statt einer schlichten Trennlinie ein leicht pulsierender, akzentfarbener Balken.
    /// </summary>
    public static void SectionHeader(string title, string? subtitle = null)
    {
        // Etwas Abstand nach oben, damit Titel/Hilfstext nicht ganz oben kleben, sondern ungefähr
        // auf Höhe des "Settings"-Texts in der Sidebar daneben sitzen.
        ImGui.Dummy(new Vector2(0f, 10f));

        ImGui.SetWindowFontScale(1.25f);
        ImGui.TextUnformatted(title);
        ImGui.SetWindowFontScale(1f);

        if (!string.IsNullOrEmpty(subtitle))
        {
            ImGui.PushStyleColor(ImGuiCol.Text, TextMuted);
            ImGui.TextWrapped(subtitle);
            ImGui.PopStyleColor();
        }

        // Balken zwischen Titel/Hilfstext und den eigentlichen Einstellungen darunter, mit etwas
        // mehr Luft danach als ein einzelnes Spacing() geben würde - sonst säße die erste
        // GroupLabel/Karte eines Tabs sichtbar zu knapp darunter.
        ImGui.Spacing();

        var avail = ImGui.GetContentRegionAvail().X;
        var barWidth = MathF.Min(260f, avail);
        var barPos = ImGui.GetCursorScreenPos();
        var pulse = 0.5f + 0.5f * (0.5f + 0.5f * MathF.Sin(animTime * 1.6f));
        var left = ImGui.ColorConvertFloat4ToU32(new Vector4(Accent.X, Accent.Y, Accent.Z, 0.35f + 0.35f * pulse));
        var right = ImGui.ColorConvertFloat4ToU32(new Vector4(Accent.X, Accent.Y, Accent.Z, 0f));
        ImGui.GetWindowDrawList().AddRectFilledMultiColor(barPos, barPos + new Vector2(barWidth, 3f), left, right, right, left);
        ImGui.Dummy(new Vector2(0f, 3f));

        ImGui.Dummy(new Vector2(0f, 10f));
    }

    /// <summary>
    /// Positioniert den Cursor für das nächste Widget rechtsbündig am Rand des verfügbaren
    /// Inhaltsbereichs (Karte/Fenster), NACHDEM label links geschrieben wurde - für Zeilen im
    /// Stil "Beschriftung ..................... Regler" wie im Referenzdesign. Ruft selbst kein
    /// Widget auf - direkt danach z.B. ImGui.SliderFloat mit SetNextItemWidth(controlWidth) davor.
    /// helpText siehe HelpIconIfHovered-Kommentar - die Zeilenhöhe wird dabei als einfache
    /// Framehöhe angenommen (für mehrzeilige Controls direkt HelpIconIfHovered selbst aufrufen).
    /// Gibt die Zeilen-Startposition zurück - direkt nach dem eigentlichen Widget (Combo/Slider) an
    /// EndLabelRow übergeben, sonst bestimmt das Widget selbst (mitsamt ImGuis eigener, je nach
    /// Widget-Typ leicht unterschiedlicher Abstands-Verbuchung) die Zeilenhöhe, was neben
    /// ToggleRow (das den Cursor explizit auf eine feste Höhe setzt) sichtbar uneinheitlich aussah
    /// (Nutzer-Report/Screenshot). contentOffsetY verschiebt NUR Label+Widget (nicht rowStart selbst,
    /// das weiterhin die ursprüngliche Zeilenposition für EndLabelRow liefert) - für Fälle, in denen
    /// EndLabelRow mit einer reduzierten Höhe aufgerufen wird (siehe dort) und Inhalt/Widget dadurch
    /// nicht mehr mittig in der (jetzt kürzeren) Zeile sitzen, z.B. einen kleinen negativen Wert,
    /// um den Inhalt wieder ein paar Pixel nach oben zu rücken (Nutzeranforderung).
    /// </summary>
    public static Vector2 LabelRow(string label, float controlWidth, string? helpText = null, float contentOffsetY = 0f)
    {
        // Von Hand positioniert statt AlignTextToFramePadding()+SameLine() (Nutzer-Report/Screenshot:
        // "Fisher preset"-Beschriftung sitzt spürbar über der Mitte der Combobox, obwohl
        // ToggleRow-Zeilen direkt darunter sauber zentriert wirken) - exakt dieselbe Technik wie
        // ToggleRow schon nutzt (siehe dessen Kommentar): Label UND das direkt danach vom Aufrufer
        // gezeichnete Widget beide von Hand gegen dieselbe Zeilenhöhe (GetFrameHeight(), die
        // Standardhöhe von Combo/Slider/Button) zentrieren, statt AlignTextToFramePadding zu vertrauen.
        var rowHeight = ImGui.GetFrameHeight();
        var textHeight = ImGui.GetTextLineHeight();
        var rowStart = ImGui.GetCursorPos();
        var rowScreenMin = ImGui.GetCursorScreenPos();
        var totalAvail = ImGui.GetContentRegionAvail().X - CardMargin;

        // Per Draw-List statt SetCursorPos()+TextUnformitted() (Nutzer-Report/Screenshot: Beschriftung
        // sitzt bei der JEWEILS ERSTEN Zeile pro Karte über der Mitte, bei allen weiteren Zeilen aber
        // korrekt zentriert) - siehe ToggleRow-Kommentar: TextUnformitted addiert beim Zeichnen
        // intern noch einen "Zeilen-Basislinien-Offset", der von der GroupLabel-Überschrift direkt
        // davor nachhängen kann. AddText umgeht das komplett.
        var labelPos = rowScreenMin + new Vector2(0f, (rowHeight - textHeight) * 0.5f + contentOffsetY);
        ImGui.GetWindowDrawList().AddText(labelPos, ImGui.GetColorU32(ImGuiCol.Text), label);
        var labelMax = labelPos + ImGui.CalcTextSize(label);
        var labelMinY = labelPos.Y;

        var widgetX = totalAvail > controlWidth ? rowStart.X + totalAvail - controlWidth : rowStart.X;
        ImGui.SetCursorPos(new Vector2(widgetX, rowStart.Y + contentOffsetY));
        ImGui.SetNextItemWidth(controlWidth);

        if (!string.IsNullOrEmpty(helpText))
            HelpIconIfHovered(rowScreenMin, new Vector2(totalAvail, rowHeight), labelMax, labelMinY, helpText);

        return rowStart;
    }

    /// <summary>
    /// Direkt NACH dem eigentlichen Widget (Combo/SliderInt) aufzurufen, mit der von LabelRow
    /// zurückgegebenen Zeilen-Startposition - setzt den Cursor explizit auf eine feste Zeilenhöhe,
    /// statt dem Widget selbst (und ImGuis Spacing-Verbuchung danach) die tatsächliche Zeilenhöhe zu
    /// überlassen. measuredHeight optional für Widgets, bei denen GetItemRectSize() direkt danach
    /// nicht mehr zuverlässig das richtige Element liefert (z.B. ein Combo, in dessen Popup danach
    /// noch weitere Elemente gezeichnet wurden - siehe Aufrufer) - dort wird die Höhe gleich nach
    /// BeginCombo() selbst gemessen und hier durchgereicht. Ohne Angabe wird GetItemRectSize() DIESES
    /// Frames zuletzt gezeichneten Elements genutzt (z.B. direkt nach einem SliderInt sicher).
    /// </summary>
    public static void EndLabelRow(Vector2 rowStart, float? measuredHeight = null)
    {
        var height = measuredHeight ?? ImGui.GetItemRectSize().Y;
        ImGui.SetCursorPos(rowStart + new Vector2(0f, height));
    }

    /// <summary>
    /// Zeichnet ein kleines "?"-Icon direkt hinter labelEndScreenPos (und zeigt helpText als
    /// Tooltip), aber NUR solange die Maus irgendwo über der übergebenen Zeilenfläche
    /// (rowScreenMin bis rowScreenMin+rowSize) schwebt - verschwindet wieder, sobald die Maus die
    /// Zeile verlässt (siehe ToggleRow/LabelRow-Aufrufer). Per Draw-List statt eines echten Widgets,
    /// damit es das Layout/die Cursor-Position nicht beeinflusst.
    /// </summary>
    public static void HelpIconIfHovered(Vector2 rowScreenMin, Vector2 rowSize, Vector2 labelEndScreenPos, float labelTopScreenY, string helpText)
    {
        if (!ImGui.IsMouseHoveringRect(rowScreenMin, rowScreenMin + rowSize))
            return;

        Vector2 iconSize;
        using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
            iconSize = ImGui.CalcTextSize(FontAwesomeIcon.QuestionCircle.ToIconString());

        var iconPos = new Vector2(labelEndScreenPos.X + 6f, labelTopScreenY + (ImGui.GetTextLineHeight() - iconSize.Y) * 0.5f);

        using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
        {
            ImGui.GetWindowDrawList().AddText(iconPos, ImGui.ColorConvertFloat4ToU32(TextMuted), FontAwesomeIcon.QuestionCircle.ToIconString());
        }

        // Tooltip bewusst nur, wenn die Maus wirklich über dem kleinen Icon selbst steht (nicht
        // schon irgendwo in der Zeile, die nur den Icon-Zeichnungsversuch auslöst) - explizite
        // Nutzeranforderung.
        if (ImGui.IsMouseHoveringRect(iconPos, iconPos + iconSize))
            ImGui.SetTooltip(helpText);
    }

    // Von ToggleRow UND ToggleSwitch genutzt, damit beide immer dieselbe Höhe annehmen - exakt die
    // Standard-Framehöhe (1.0x), damit Toggle- und Combo-/Slider-Zeilen gleich hoch sind (Nutzer-
    // Report/Screenshot: unterschiedlich große Abstände zwischen den Trennlinien, je nachdem ob
    // dazwischen ein Toggle oder eine Combobox/Slider stand - ein Versuch, stattdessen die
    // Combo-/Slider-Zeilen per Dummy() auf die größere Toggle-Höhe aufzupolstern, sah noch
    // schlechter aus, daher jetzt umgekehrt: der Toggle ist nicht mehr extra groß).
    private const float ToggleHeightScale = 1f;

    /// <summary>
    /// Zeile "Beschriftung ..................... Toggle" - Kombination aus LabelRow und
    /// ToggleSwitch für den häufigsten Fall (ein Bool-Setting pro Zeile). helpText siehe
    /// HelpIconIfHovered-Kommentar - ersetzt den früher permanent darunter stehenden Fließtext:
    /// erscheint nur noch als "?"-Icon neben dem Titel, solange die Zeile gehovert wird.
    /// </summary>
    /// <param name="heightReduction">Feinjustierung der Zeilenhöhe (siehe EndLabelRow/LabelRow-Kommentar) - Standard 1px wie überall sonst, einzelne Zeilen können einen anderen Wert übergeben.</param>
    /// <param name="contentOffsetY">Verschiebt NUR Label+Toggle (nicht den Zeilen-Endpunkt) - z.B. ein negativer Wert, um den Inhalt bei größerem heightReduction wieder zu zentrieren.</param>
    public static bool ToggleRow(string label, ref bool value, string? helpText = null, float heightReduction = 1f, float contentOffsetY = 0f)
    {
        // Bewusst mit von Hand berechneten Positionen statt AlignTextToFramePadding() (das nimmt
        // die volle Standard-Framehöhe an) - der Toggle weicht davon ab (siehe ToggleHeightScale),
        // dagegen hätte AlignTextToFramePadding den Text falsch positioniert. Beide Elemente werden
        // hier gegen dieselbe Zeilenhöhe zentriert, unabhängig davon, welches der beiden (Text oder
        // Toggle) gerade höher ist.
        var toggleHeight = ImGui.GetFrameHeight() * ToggleHeightScale;
        var toggleWidth = toggleHeight * 1.8f;
        var textHeight = ImGui.GetTextLineHeight();
        var rowHeight = MathF.Max(toggleHeight, textHeight);
        var rowStart = ImGui.GetCursorPos();
        var rowScreenMin = ImGui.GetCursorScreenPos();

        // VOR jeder Cursor-Bewegung gemessen - liefert die Breite von rowStart.X bis zum rechten
        // Kartenrand, unabhängig davon, wie breit das Label ist.
        var totalAvail = ImGui.GetContentRegionAvail().X - CardMargin;

        // Per Draw-List statt SetCursorPos()+TextUnformitted() (Nutzer-Report/Screenshot: Beschriftung
        // sitzt bei der JEWEILS ERSTEN Zeile pro Karte über der Mitte, bei allen weiteren Zeilen aber
        // korrekt zentriert) - TextUnformitted addiert beim Zeichnen intern noch einen "Zeilen-
        // Basislinien-Offset" (window->DC.CurrLineTextBaseOffset), der von der GroupLabel-Überschrift
        // direkt davor (größere Schriftskalierung) nachhängen kann, weil unser manuelles SetCursorPos
        // ImGuis normale Zeilenverfolgung umgeht. AddText zeichnet direkt an der übergebenen
        // Bildschirmposition, ganz ohne diesen zusätzlichen Offset - exakt dieselbe Technik wie schon
        // beim "?"-Hilfe-Icon in HelpIconIfHovered.
        var labelPos = rowScreenMin + new Vector2(0f, (rowHeight - textHeight) * 0.5f + contentOffsetY);
        ImGui.GetWindowDrawList().AddText(labelPos, ImGui.GetColorU32(ImGuiCol.Text), label);
        var labelMax = labelPos + ImGui.CalcTextSize(label);
        var labelMinY = labelPos.Y;

        var toggleX = totalAvail > toggleWidth ? rowStart.X + totalAvail - toggleWidth : rowStart.X;
        ImGui.SetCursorPos(new Vector2(toggleX, rowStart.Y + (rowHeight - toggleHeight) * 0.5f + contentOffsetY));
        var changed = ToggleSwitch($"##toggle_{label}", ref value);

        if (!string.IsNullOrEmpty(helpText))
            HelpIconIfHovered(rowScreenMin, new Vector2(totalAvail, rowHeight), labelMax, labelMinY, helpText);

        ImGui.SetCursorPos(rowStart + new Vector2(0f, rowHeight - heightReduction));
        return changed;
    }

    /// <summary>
    /// Ein einzelner "An/Aus"-Schalter statt einer eckigen Checkbox - visuell wie in modernen
    /// Settings-UIs üblich (siehe Referenzbild). Der Knopf gleitet beim Umschalten weich zur Seite
    /// statt zu springen, dazu ein leichtes Glühen um die Schiene, wenn eingeschaltet. Verhält sich
    /// wie ImGui.Checkbox: gibt true zurück, wenn der Wert sich durch einen Klick geändert hat, und
    /// schreibt den neuen Wert in value.
    /// </summary>
    public static bool ToggleSwitch(string id, ref bool value, float heightScale = ToggleHeightScale)
    {
        var height = ImGui.GetFrameHeight() * heightScale;
        var width = height * 1.8f;
        var pos = ImGui.GetCursorScreenPos();

        ImGui.InvisibleButton(id, new Vector2(width, height));
        var changed = false;
        if (ImGui.IsItemClicked())
        {
            value = !value;
            changed = true;
        }

        var hovered = ImGui.IsItemHovered();
        var trackColor = value ? (hovered ? AccentHover : Accent) : (hovered ? ToggleOffHover : ToggleOff);
        var radius = height * 0.5f;
        var knobRadius = radius - 2.5f;
        var drawList = ImGui.GetWindowDrawList();

        var knobT = Animate(id, value ? 1f : 0f, 14f);

        var glowAlpha = 0.22f * knobT;
        if (glowAlpha > 0.01f)
        {
            var glowColor = new Vector4(Accent.X, Accent.Y, Accent.Z, glowAlpha);
            drawList.AddRectFilled(pos - new Vector2(3f, 3f), pos + new Vector2(width + 3f, height + 3f), ImGui.GetColorU32(glowColor), radius + 3f);
        }

        // GetColorU32() statt ColorConvertFloat4ToU32() - rechnet den von ImGui.BeginDisabled()
        // gesetzten Alpha-Dimm-Faktor (style.Alpha) mit ein, sonst bleibt der Schalter bei
        // ausgegrauten Zeilen (z.B. "Ignore Big Fish", solange "Desynthesis nach dem Angeln" aus
        // ist) trotzdem voll sichtbar, während Label und Combo/Slider-Widgets sich korrekt abdunkeln.
        drawList.AddRectFilled(pos, pos + new Vector2(width, height), ImGui.GetColorU32(trackColor), radius);
        var knobX = pos.X + radius + (width - radius * 2f) * knobT;
        drawList.AddCircleFilled(new Vector2(knobX, pos.Y + radius), knobRadius, ImGui.GetColorU32(Vector4.One), 32);

        return changed;
    }

    /// <summary>
    /// Eine Zeile in der linken Icon-Sidebar (Nav-Eintrag) - abgerundete Hervorhebung (weich
    /// einblendend) + blauer, leicht pulsierender Akzentstrich links, wenn ausgewählt, sonst nur
    /// dezentes Hover. Gibt true zurück, wenn angeklickt.
    /// </summary>
    public static bool SidebarItem(FontAwesomeIcon icon, string label, bool selected)
    {
        var width = ImGui.GetContentRegionAvail().X;
        var height = 38f;
        var startPos = ImGui.GetCursorScreenPos();

        // Transparent - der sichtbare Hintergrund wird gleich von Hand mit echter Rundung
        // gezeichnet. ImGui.Selectable rundet sein eigenes Hintergrundrechteck nicht zuverlässig
        // (bleibt eckig, unabhängig vom global gesetzten FrameRounding), daher hier selbst gemacht,
        // genau wie schon bei ModernUi.BeginCard/EndCard für die Karten.
        ImGui.PushStyleColor(ImGuiCol.Header, new Vector4(0f, 0f, 0f, 0f));
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, new Vector4(0f, 0f, 0f, 0f));
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, new Vector4(0f, 0f, 0f, 0f));
        var clicked = ImGui.Selectable($"##sidebar_{label}", selected, ImGuiSelectableFlags.None, new Vector2(width, height));
        var hovered = ImGui.IsItemHovered();
        ImGui.PopStyleColor(3);

        var drawList = ImGui.GetWindowDrawList();

        // Weich einblendende Hervorhebung statt eines sofortigen An/Aus - fühlt sich beim
        // Durchfahren der Liste mit der Maus runder an.
        var targetHighlight = selected ? 1f : hovered ? 0.6f : 0f;
        var highlight = Animate($"sidebar_{label}", targetHighlight, 16f);
        if (highlight > 0.01f)
        {
            var baseColor = selected ? SidebarSelected : SidebarHover;
            var fadedColor = new Vector4(baseColor.X, baseColor.Y, baseColor.Z, baseColor.W * highlight);
            drawList.AddRectFilled(startPos, startPos + new Vector2(width, height), ImGui.ColorConvertFloat4ToU32(fadedColor), 10f);
        }

        if (selected)
        {
            // Breiterer Balken mit voller Kapsel-Rundung (Radius = halbe Breite) statt der vorher
            // fast eckig wirkenden schmalen 3px-Linie - dafür oben/unten etwas eingerückt, sonst
            // wäre bei voller Zeilenhöhe kaum noch etwas von der Rundung an den Enden zu sehen.
            const float barWidth = 5f;
            const float barMarginY = 8f;
            var pulse = 0.75f + 0.25f * (0.5f + 0.5f * MathF.Sin(animTime * 2.2f));
            var barColor = new Vector4(Accent.X, Accent.Y, Accent.Z, pulse);

            drawList.AddRectFilled(
                startPos + new Vector2(0f, barMarginY),
                startPos + new Vector2(barWidth, height - barMarginY),
                ImGui.ColorConvertFloat4ToU32(barColor),
                barWidth * 0.5f);
        }

        var textColor = selected ? Vector4.One : TextMuted;
        var iconPos = startPos + new Vector2(16f, height * 0.5f - ImGui.GetTextLineHeight() * 0.5f);
        using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
            drawList.AddText(iconPos, ImGui.ColorConvertFloat4ToU32(textColor), icon.ToIconString());

        var labelPos = startPos + new Vector2(42f, height * 0.5f - ImGui.GetTextLineHeight() * 0.5f);
        drawList.AddText(labelPos, ImGui.ColorConvertFloat4ToU32(textColor), label);

        return clicked;
    }
}
