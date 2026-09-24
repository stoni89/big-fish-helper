using System;
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

    private int selectedNavIndex;
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

    private readonly (FontAwesomeIcon Icon, string Label, Action Draw)[] navItems;

    private const ImGuiWindowFlags BaseFlags =
        ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoResize;

    public MainWindow(Plugin plugin) : base($"{PluginDisplayName} (v{VersionText})##BigFishHelper", BaseFlags)
    {
        this.plugin = plugin;

        // Einstellungs-Seiten - erstmal nur "Allgemein" (noch ohne Einträge).
        navItems = new (FontAwesomeIcon, string, Action)[]
        {
            (FontAwesomeIcon.Cog, Loc.T("Allgemein", "General"), DrawGeneralTab),
        };

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
        const float sidebarWidth = 200f;

        ImGui.BeginChild("##OptionsRail", new Vector2(railWidth, 0f), false, ImGuiWindowFlags.NoScrollbar);
        ImGui.Spacing();
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
        if (railPage == RailPage.Settings)
        {
            ImGui.BeginChild("##OptionsSidebar", new Vector2(sidebarWidth, 0f), false, ImGuiWindowFlags.NoScrollbar);
            ImGui.Spacing();
            ImGui.SetWindowFontScale(1.5f);
            ImGui.TextUnformatted(Loc.T("Einstellungen", "Settings"));
            ImGui.SetWindowFontScale(1f);
            ImGui.Spacing();
            for (var i = 0; i < navItems.Length; i++)
            {
                if (ModernUi.SidebarItem(navItems[i].Icon, navItems[i].Label, selectedNavIndex == i))
                    selectedNavIndex = i;
            }
            ImGui.EndChild();

            ImGui.SameLine();

            ImGui.BeginChild("##OptionsContent", contentSize, false);
            ImGui.Spacing();
            ImGui.Indent(4f);
            navItems[selectedNavIndex].Draw();
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

    // ---- Einstellungen ----

    private void DrawGeneralTab()
    {
        ModernUi.SectionHeader(Loc.T("Allgemein", "General"), Loc.T("Allgemeine Einstellungen.", "General settings."));
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
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.05f, 0.09f, 0.16f, 1f));
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
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.05f, 0.09f, 0.16f, 1f));
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
