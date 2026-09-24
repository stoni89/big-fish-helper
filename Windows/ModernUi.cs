using System;
using System.Numerics;
using Dalamud.Interface;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Bindings.ImGui;

namespace BigFishHelper.Windows;

/// <summary>
/// Kleines Set wiederverwendbarer ImGui-Bausteine für einen moderneren Look des Optionsfensters
/// (dunkler Verlaufshintergrund, abgerundete Karten, Toggle-Switches, Icon-Sidebar) - ImGui bietet
/// dafür von sich aus nichts, alles hier wird manuell per Draw-List gezeichnet. Bewusst als
/// eigenständige, zustandslose Helfer (keine Abhängigkeit auf Plugin/Configuration), damit sie sich
/// auch in anderen Fenstern wiederverwenden lassen.
/// </summary>
public static class ModernUi
{
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
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 10f);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 8f);
        ImGui.PushStyleVar(ImGuiStyleVar.GrabRounding, 8f);
        ImGui.PushStyleVar(ImGuiStyleVar.ScrollbarRounding, 8f);
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
    }

    public static void PopStyle()
    {
        ImGui.PopStyleColor(13);
        ImGui.PopStyleVar(8);
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
        drawList.AddRectFilled(min, max, ImGui.ColorConvertFloat4ToU32(CardBg), 12f);
        drawList.AddRect(min, max, ImGui.ColorConvertFloat4ToU32(borderColor ?? CardBorder), 12f);
        drawList.ChannelsMerge();

        ImGui.Unindent(CardMargin);
        ImGui.Dummy(new Vector2(0f, CardTrailingGap));
    }

    /// <summary>
    /// Kleine Zwischenüberschrift direkt über einer Karte (z.B. "Match intro" im Referenzdesign) -
    /// um CardMargin eingerückt, damit sie mit dem eingerückten Karteninhalt darunter fluchtet statt
    /// mit dem (weiter links liegenden) Kartenrand.
    /// </summary>
    public static void GroupLabel(string text)
    {
        ImGui.Indent(CardMargin);
        ImGui.SetWindowFontScale(1.2f);
        ImGui.TextUnformatted(text);
        ImGui.SetWindowFontScale(1f);
        ImGui.Unindent(CardMargin);
        ImGui.Dummy(new Vector2(0f, 14f));
    }

    /// <summary>
    /// Größere, fette Überschrift + gedämpfter Untertext darunter - für den Titel oben in jedem
    /// Tab-Inhalt (entspricht "In match" / "The social touches..." im Referenzdesign).
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

        // Trennlinie zwischen Titel/Hilfstext und den eigentlichen Einstellungen darunter, mit
        // etwas mehr Luft danach als ein einzelnes Spacing() geben würde - sonst säße die erste
        // GroupLabel/Karte eines Tabs sichtbar zu knapp unter der Linie.
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Dummy(new Vector2(0f, 10f));
    }

    /// <summary>
    /// Positioniert den Cursor für das nächste Widget rechtsbündig am Rand des verfügbaren
    /// Inhaltsbereichs (Karte/Fenster), NACHDEM label links geschrieben wurde - für Zeilen im
    /// Stil "Beschriftung ..................... Regler" wie im Referenzdesign. Ruft selbst kein
    /// Widget auf - direkt danach z.B. ImGui.SliderFloat mit SetNextItemWidth(controlWidth) davor.
    /// helpText siehe HelpIconIfHovered-Kommentar - die Zeilenhöhe wird dabei als einfache
    /// Framehöhe angenommen (für mehrzeilige Controls direkt HelpIconIfHovered selbst aufrufen).
    /// </summary>
    public static void LabelRow(string label, float controlWidth, string? helpText = null)
    {
        var rowScreenMin = ImGui.GetCursorScreenPos();
        var totalAvail = ImGui.GetContentRegionAvail().X - CardMargin;

        // Richtet die Textgrundlinie an der eines Standard-Widgets (Slider/Dropdown/Button) aus -
        // ohne das säße der (niedrigere) reine Text sichtbar zu weit oben, während das danach per
        // SameLine() gezeichnete, durch FramePadding höhere Widget die restliche Zeilenhöhe füllt.
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(label);
        var labelMax = ImGui.GetItemRectMax();
        var labelMinY = ImGui.GetItemRectMin().Y;

        ImGui.SameLine();
        var avail = ImGui.GetContentRegionAvail().X - CardMargin;
        if (avail > controlWidth)
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + avail - controlWidth);
        ImGui.SetNextItemWidth(controlWidth);

        if (!string.IsNullOrEmpty(helpText))
            HelpIconIfHovered(rowScreenMin, new Vector2(totalAvail, ImGui.GetFrameHeight()), labelMax, labelMinY, helpText);
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

    // Von ToggleRow UND ToggleSwitch genutzt, damit beide immer dieselbe Höhe annehmen - größer
    // als die Standard-Framehöhe (1.15x), damit der Schalter sichtbar größer als ein Textfeld wirkt.
    private const float ToggleHeightScale = 1.05f;

    /// <summary>
    /// Zeile "Beschriftung ..................... Toggle" - Kombination aus LabelRow und
    /// ToggleSwitch für den häufigsten Fall (ein Bool-Setting pro Zeile). helpText siehe
    /// HelpIconIfHovered-Kommentar - ersetzt den früher permanent darunter stehenden Fließtext:
    /// erscheint nur noch als "?"-Icon neben dem Titel, solange die Zeile gehovert wird.
    /// </summary>
    public static bool ToggleRow(string label, ref bool value, string? helpText = null)
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

        ImGui.SetCursorPos(rowStart + new Vector2(0f, (rowHeight - textHeight) * 0.5f));
        ImGui.TextUnformatted(label);
        var labelMax = ImGui.GetItemRectMax();
        var labelMinY = ImGui.GetItemRectMin().Y;

        var toggleX = totalAvail > toggleWidth ? rowStart.X + totalAvail - toggleWidth : rowStart.X;
        ImGui.SetCursorPos(new Vector2(toggleX, rowStart.Y + (rowHeight - toggleHeight) * 0.5f));
        var changed = ToggleSwitch($"##toggle_{label}", ref value);

        if (!string.IsNullOrEmpty(helpText))
            HelpIconIfHovered(rowScreenMin, new Vector2(totalAvail, rowHeight), labelMax, labelMinY, helpText);

        ImGui.SetCursorPos(rowStart + new Vector2(0f, rowHeight));
        return changed;
    }

    /// <summary>
    /// Ein einzelner "An/Aus"-Schalter statt einer eckigen Checkbox - visuell wie in modernen
    /// Settings-UIs üblich (siehe Referenzbild). Verhält sich wie ImGui.Checkbox: gibt true zurück,
    /// wenn der Wert sich durch einen Klick geändert hat, und schreibt den neuen Wert in value.
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

        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(pos, pos + new Vector2(width, height), ImGui.ColorConvertFloat4ToU32(trackColor), radius);

        var knobRadius = radius - 2.5f;
        var knobX = value ? pos.X + width - radius : pos.X + radius;
        drawList.AddCircleFilled(new Vector2(knobX, pos.Y + radius), knobRadius, ImGui.ColorConvertFloat4ToU32(Vector4.One), 32);

        return changed;
    }

    /// <summary>
    /// Eine Zeile in der linken Icon-Sidebar (Nav-Eintrag) - abgerundete Hervorhebung + blauer
    /// Akzentstrich links, wenn ausgewählt, sonst nur dezentes Hover. Gibt true zurück, wenn
    /// angeklickt.
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
        if (selected || hovered)
        {
            drawList.AddRectFilled(
                startPos,
                startPos + new Vector2(width, height),
                ImGui.ColorConvertFloat4ToU32(selected ? SidebarSelected : SidebarHover),
                10f);
        }

        if (selected)
        {
            // Breiterer Balken mit voller Kapsel-Rundung (Radius = halbe Breite) statt der vorher
            // fast eckig wirkenden schmalen 3px-Linie - dafür oben/unten etwas eingerückt, sonst
            // wäre bei voller Zeilenhöhe kaum noch etwas von der Rundung an den Enden zu sehen.
            const float barWidth = 5f;
            const float barMarginY = 8f;
            drawList.AddRectFilled(
                startPos + new Vector2(0f, barMarginY),
                startPos + new Vector2(barWidth, height - barMarginY),
                ImGui.ColorConvertFloat4ToU32(Accent),
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
