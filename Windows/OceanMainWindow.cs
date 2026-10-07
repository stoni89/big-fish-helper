using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Dalamud.Interface;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Utility;
using Dalamud.Bindings.ImGui;
using Lumina.Excel;
using Lumina.Excel.Sheets;
using PluginUiKit;
using Serilog.Events;

namespace BigFishHelper.Windows;

/// <summary>
/// Vorschau-Fenster für das neue Ocean-UI - 1:1 derselbe Aufbau wie TheExplorersCodex'
/// CodexMenuWindow (Seitenleiste mit Logo/Gruppe/Menüpunkt/Schließen-Knopf, Seitenkopf mit Titel/
/// Untertitel/Trenn-Ornament, Karten mit CardGroupLabel/ToggleRow/Dropdown-Zeilen), nur mit der
/// Ocean-Palette statt Codex' Tinte-Gold-Theme und von Hand gezeichneten Icons statt FontAwesome-
/// Glyphen (Nutzeranforderung: "identisch wie beim Codex Settings -> General nur mit den neuen
/// Farben und Icons"). Bewusst eine komplett SEPARATE Window-Klasse statt eines Umbaus von
/// MainWindow - das alte Menü bleibt über "/bigfish" unverändert erreichbar, bis der Nutzer das neue
/// UI final abnimmt (siehe Plugin.cs).
///
/// Nutzeranforderung: vorerst NUR "Settings -> General" - keine weitere Seite/Seitenleisten-Gruppe,
/// den Rest baut der Nutzer sich selbst. Die Seitenleiste zeigt deshalb nur die eine Gruppe mit dem
/// einen Menüpunkt, ohne Umschaltlogik.
/// </summary>
public sealed class OceanMainWindow : Window, IDisposable
{
    private const string PluginDisplayName = "Big Fish Helper";
    private const string GitHubUrl = "https://github.com/stoni89/big-fish-helper";
    private static readonly string VersionText = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "?";

    private static readonly OceanOrnaments Ornaments = new();

    // Eigene, seitenspezifische Schriftgrößen (siehe PluginUiKit/README.md "Eigene Palette und eigene
    // Verzierungen übergeben") - exakt dieselben Größen/Dateien wie die entsprechenden CodexTheme-
    // Rollen (Nutzeranforderung: "Schriftgrößen ... identisch wie beim Codex"), nur über UiFonts.
    // BuildHandle neu gebaut, da Font-Handles nicht zwischen Plugins geteilt werden können (siehe
    // UiFonts-Klassenkommentar).
    private static IFontHandle? pageTitleFont;
    private static IFontHandle PageTitleFont => pageTitleFont ??= UiFonts.BuildHandle("Cinzel-Bold.ttf", 33f); // = CodexTheme.FontTitleMenu
    private static IFontHandle? subtitleFont;
    private static IFontHandle SubtitleFont => subtitleFont ??= UiFonts.BuildHandle("AlegreyaItalic.ttf", 20f); // = CodexTheme.FontMenuSubtitle
    private static IFontHandle? navLabelFont;
    private static IFontHandle NavLabelFont => navLabelFont ??= UiFonts.BuildHandle("AlegreyaSans-Medium.ttf", 18f); // = CodexTheme.FontBodyMedium (Nav-Punkte)
    private static IFontHandle? brandSmallFont;
    private static IFontHandle BrandSmallFont => brandSmallFont ??= UiFonts.BuildHandle("Cinzel.ttf", 15f); // = CodexTheme.FontSidebarBrandSmall
    private static IFontHandle? brandLargeFont;
    private static IFontHandle BrandLargeFont => brandLargeFont ??= UiFonts.BuildHandle("Cinzel-Bold.ttf", 26f); // = CodexTheme.FontSidebarBrandLarge
    private static IFontHandle? bodySmallFont;
    private static IFontHandle BodySmallFont => bodySmallFont ??= UiFonts.BuildHandle("AlegreyaSans-Regular.ttf", 14f); // = CodexTheme.FontBodySmall (Versionstext)
    private static IFontHandle? bodyBoldFont;
    private static IFontHandle BodyBoldFont => bodyBoldFont ??= UiFonts.BuildHandle("AlegreyaSans-Bold.ttf", 18f); // = CodexTheme.FontBodyBold (Schließen-Knopf)
    private static IFontHandle? fieldLabelFont;
    private static IFontHandle FieldLabelFont => fieldLabelFont ??= UiFonts.BuildHandle("AlegreyaSans-Medium.ttf", 21f); // = CodexTheme.FontMenuFieldLabel
    private static IFontHandle? dropdownCaptionFont;
    private static IFontHandle DropdownCaptionFont => dropdownCaptionFont ??= UiFonts.BuildHandle("AlegreyaSans-Regular.ttf", 17f); // = CodexTheme.FontDropdownCaption
    private static IFontHandle? dropdownValueFont;
    private static IFontHandle DropdownValueFont => dropdownValueFont ??= UiFonts.BuildHandle("AlegreyaSans-Regular.ttf", 17f); // = CodexTheme.FontMenuDropdownValue
    private static IFontHandle? pluginNameFont;
    private static IFontHandle PluginNameFont => pluginNameFont ??= UiFonts.BuildHandle("AlegreyaSans-Bold.ttf", 21f); // = CodexTheme.FontPluginName
    private static IFontHandle? pluginDescriptionFont;
    private static IFontHandle PluginDescriptionFont => pluginDescriptionFont ??= UiFonts.BuildHandle("AlegreyaSans-Regular.ttf", 15f); // = CodexTheme.FontPluginDescription
    private static IFontHandle? pluginInstalledLabelFont;
    private static IFontHandle PluginInstalledLabelFont => pluginInstalledLabelFont ??= UiFonts.BuildHandle("AlegreyaSans-Bold.ttf", 17f); // = CodexTheme.FontPluginInstalledLabel
    private static IFontHandle? installButtonLabelFont;
    private static IFontHandle InstallButtonLabelFont => installButtonLabelFont ??= UiFonts.BuildHandle("AlegreyaSans-Bold.ttf", 14f); // = CodexTheme.FontAutoButtonLabel
    private static IFontHandle? debugCardTitleFont;
    private static IFontHandle DebugCardTitleFont => debugCardTitleFont ??= UiFonts.BuildHandle("Cinzel.ttf", 20f); // = CodexTheme.FontDebugCardTitle
    private static IFontHandle? debugStatusTextFont;
    private static IFontHandle DebugStatusTextFont => debugStatusTextFont ??= UiFonts.BuildHandle("AlegreyaSans-Regular.ttf", 17f); // = CodexTheme.FontDebugStatusText
    private static IFontHandle? debugDescriptionFont;
    private static IFontHandle DebugDescriptionFont => debugDescriptionFont ??= UiFonts.BuildHandle("AlegreyaSans-Regular.ttf", 15f); // = CodexTheme.FontDebugDescription
    private static IFontHandle? debugButtonLabelFont;
    private static IFontHandle DebugButtonLabelFont => debugButtonLabelFont ??= UiFonts.BuildHandle("AlegreyaSans-Medium.ttf", 13f); // = CodexTheme.FontDebugButtonLabel
    private static IFontHandle? logTableHeaderFont;
    private static IFontHandle LogTableHeaderFont => logTableHeaderFont ??= UiFonts.BuildHandle("Cinzel.ttf", 14f); // = CodexTheme.FontLogTableHeader
    private static IFontHandle? logRowFont;
    private static IFontHandle LogRowFont => logRowFont ??= UiFonts.BuildHandle("AlegreyaSans-Regular.ttf", 16f); // = CodexTheme.FontLogRow
    private static IFontHandle? logBadgeFont;
    private static IFontHandle LogBadgeFont => logBadgeFont ??= UiFonts.BuildHandle("AlegreyaSans-Bold.ttf", 13f); // = CodexTheme.FontLogBadge
    private static IFontHandle? logMetaFont;
    private static IFontHandle LogMetaFont => logMetaFont ??= UiFonts.BuildHandle("AlegreyaSans-Regular.ttf", 15f); // = CodexTheme.FontLogMeta
    private static IFontHandle? logChipCountFont;
    private static IFontHandle LogChipCountFont => logChipCountFont ??= UiFonts.BuildHandle("AlegreyaSans-Regular.ttf", 14f); // = CodexTheme.FontLogChipCount
    private static IFontHandle? logFilterLabelFont;
    private static IFontHandle LogFilterLabelFont => logFilterLabelFont ??= UiFonts.BuildHandle("AlegreyaSans-Medium.ttf", 16f); // = CodexTheme.FontLogFilterLabel
    private static IFontHandle? logSearchInputFont;
    private static IFontHandle LogSearchInputFont => logSearchInputFont ??= UiFonts.BuildHandle("AlegreyaSans-Regular.ttf", 18f); // = CodexTheme.FontLogSearchInput
    private static IFontHandle? logGhostButtonFont;
    private static IFontHandle LogGhostButtonFont => logGhostButtonFont ??= UiFonts.BuildHandle("AlegreyaSans-Medium.ttf", 17f); // = CodexTheme.FontLogGhostButton
    private static IFontHandle? aboutTitleFont;
    private static IFontHandle AboutTitleFont => aboutTitleFont ??= UiFonts.BuildHandle("Cinzel-Bold.ttf", 31f); // = CodexTheme.FontAboutTitle
    private static IFontHandle? aboutVersionBadgeFont;
    private static IFontHandle AboutVersionBadgeFont => aboutVersionBadgeFont ??= UiFonts.BuildHandle("AlegreyaSans-Regular.ttf", 18f); // = CodexTheme.FontAboutVersionBadge
    private static IFontHandle? aboutCardTitleFont;
    private static IFontHandle AboutCardTitleFont => aboutCardTitleFont ??= UiFonts.BuildHandle("Cinzel.ttf", 18f); // = CodexTheme.FontAboutCardTitle
    private static IFontHandle? aboutBodyFont;
    private static IFontHandle AboutBodyFont => aboutBodyFont ??= UiFonts.BuildHandle("AlegreyaSans-Regular.ttf", 17f); // = CodexTheme.FontAboutBody
    private static IFontHandle? aboutDividerLabelFont;
    private static IFontHandle AboutDividerLabelFont => aboutDividerLabelFont ??= UiFonts.BuildHandle("Cinzel.ttf", 14f); // = CodexTheme.FontAboutDividerLabel
    private static IFontHandle? aboutHintFont;
    private static IFontHandle AboutHintFont => aboutHintFont ??= UiFonts.BuildHandle("AlegreyaSans-Regular.ttf", 16f); // = CodexTheme.FontAboutHint
    private static IFontHandle? aboutLinkButtonLabelFont;
    private static IFontHandle AboutLinkButtonLabelFont => aboutLinkButtonLabelFont ??= UiFonts.BuildHandle("AlegreyaSans-Medium.ttf", 14f); // = CodexTheme.FontAboutLinkButtonLabel
    private static IFontHandle? aboutKofiLabelFont;
    private static IFontHandle AboutKofiLabelFont => aboutKofiLabelFont ??= UiFonts.BuildHandle("AlegreyaSans-Bold.ttf", 15f); // = CodexTheme.FontAboutKofiLabel
    private static IFontHandle? changelogVersionTitleFont;
    private static IFontHandle ChangelogVersionTitleFont => changelogVersionTitleFont ??= UiFonts.BuildHandle("Cinzel-Bold.ttf", 22f); // = CodexTheme.FontChangelogVersionTitle
    private static IFontHandle? changelogCollapsedTitleFont;
    private static IFontHandle ChangelogCollapsedTitleFont => changelogCollapsedTitleFont ??= UiFonts.BuildHandle("Cinzel-Bold.ttf", 15f); // = CodexTheme.FontChangelogCollapsedTitle
    private static IFontHandle? changelogMetaFont;
    private static IFontHandle ChangelogMetaFont => changelogMetaFont ??= UiFonts.BuildHandle("AlegreyaSans-Regular.ttf", 17f); // = CodexTheme.FontChangelogMeta
    private static IFontHandle? changelogChangeTextFont;
    private static IFontHandle ChangelogChangeTextFont => changelogChangeTextFont ??= UiFonts.BuildHandle("AlegreyaSans-Regular.ttf", 18f); // = CodexTheme.FontChangelogChangeText
    private static IFontHandle? changelogTagBadgeFont;
    private static IFontHandle ChangelogTagBadgeFont => changelogTagBadgeFont ??= UiFonts.BuildHandle("AlegreyaSans-Bold.ttf", 15f); // = CodexTheme.FontChangelogTagBadge
    private static IFontHandle? changelogNavBadgeFont;
    private static IFontHandle ChangelogNavBadgeFont => changelogNavBadgeFont ??= UiFonts.BuildHandle("AlegreyaSans-Bold.ttf", 11f); // = CodexTheme.FontChangelogTag

    // ---- Fish-Data-Seite - eigene Schriftgrößen gemäß Aufgabenstellung Abschnitt 1/3/4; Überschrift/Subtitle
    // nutzen PageTitleFont/SubtitleFont (Nutzeranforderung: identisch zu den anderen Seiten). ----
    private static IFontHandle? fishDataTabFont;
    private static IFontHandle FishDataTabFont => fishDataTabFont ??= UiFonts.BuildHandle("AlegreyaSans-Medium.ttf", 18f); // +3px Nutzervorgabe
    private static IFontHandle? fishDataTabCountFont;
    private static IFontHandle FishDataTabCountFont => fishDataTabCountFont ??= UiFonts.BuildHandle("AlegreyaSans-Regular.ttf", 13f);
    private static IFontHandle? fishDataMetaFont;
    private static IFontHandle FishDataMetaFont => fishDataMetaFont ??= UiFonts.BuildHandle("AlegreyaSans-Regular.ttf", 13f);
    private static IFontHandle? fishDataHeaderFont;
    private static IFontHandle FishDataHeaderFont => fishDataHeaderFont ??= UiFonts.BuildHandle("Cinzel.ttf", 14f); // +3px Nutzervorgabe
    private static IFontHandle? fishDataBoldFont;
    private static IFontHandle FishDataBoldFont => fishDataBoldFont ??= UiFonts.BuildHandle("AlegreyaSans-Bold.ttf", 17f); // +3px Nutzervorgabe
    private static IFontHandle? fishDataRegularFont;
    private static IFontHandle FishDataRegularFont => fishDataRegularFont ??= UiFonts.BuildHandle("AlegreyaSans-Regular.ttf", 16f); // +3px Nutzervorgabe
    private static IFontHandle? fishDataSmallBoldFont;
    private static IFontHandle FishDataSmallBoldFont => fishDataSmallBoldFont ??= UiFonts.BuildHandle("AlegreyaSans-Bold.ttf", 16f); // +3px Nutzervorgabe

    // Nicht in UiTheme (seiten-/spalten-spezifisch, siehe Aufgabenstellung-Hex-Werte wörtlich).
    private static readonly Vector4 FishDataActiveGreen = new(0.498f, 0.878f, 0.651f, 1f); // #7FE0A6
    private static readonly Vector4 FishDataCoralRed = new(0.898f, 0.541f, 0.455f, 1f); // #E58A74
    private static readonly Vector4 FishDataRarityYellow = new(0.914f, 0.773f, 0.420f, 1f); // #E9C46A
    private static readonly Vector4 FishDataTextButton = new(0.812f, 0.890f, 0.906f, 1f); // #CFE3E7
    private static readonly Vector4 FishDataTeleportBlue = new(0.498f, 0.784f, 0.941f, 1f); // #7FC8F0
    private static readonly Vector4 FishDataTeleportOrange = new(0.941f, 0.627f, 0.314f, 1f); // #F0A050
    private static readonly Vector4 FishDataDevBorder = new(0.361f, 0.290f, 0.149f, 1f); // #5C4A26

    private string fishDataSearchFilter = string.Empty;
    private BigFishExpansion fishDataActiveTab = BigFishData.ByExpansion[0].Expansion;
    private readonly Dictionary<uint, string> fishDataPresetSearch = new();
    private readonly Dictionary<uint, ISharedImmediateTexture> fishDataIconCache = new();

    // ---- Start-Seite - eigene Schriftgrößen gemäß Aufgabenstellung Abschnitt 2/3/4/5, +3px Nutzervorgabe
    // (Seitentitel/Untertitel bleiben unverändert PageTitleFont/SubtitleFont - identisch zu den anderen Seiten). ----
    private static IFontHandle? startStatusFont;
    private static IFontHandle StartStatusFont => startStatusFont ??= UiFonts.BuildHandle("AlegreyaSans-Regular.ttf", 16f);
    private static IFontHandle? startPillLabelFont;
    private static IFontHandle StartPillLabelFont => startPillLabelFont ??= UiFonts.BuildHandle("AlegreyaSans-Bold.ttf", 14f);
    private static IFontHandle? startNextUpLabelFont;
    private static IFontHandle StartNextUpLabelFont => startNextUpLabelFont ??= UiFonts.BuildHandle("Cinzel.ttf", 14f);
    private static IFontHandle? startFishNameFont;
    private static IFontHandle StartFishNameFont => startFishNameFont ??= UiFonts.BuildHandle("Cinzel-Bold.ttf", 23f);
    private static IFontHandle? startAreaFont;
    private static IFontHandle StartAreaFont => startAreaFont ??= UiFonts.BuildHandle("AlegreyaSans-Regular.ttf", 16f);
    private static IFontHandle? startTileLabelFont;
    private static IFontHandle StartTileLabelFont => startTileLabelFont ??= UiFonts.BuildHandle("AlegreyaSans-Regular.ttf", 15f);
    private static IFontHandle? startTileValueFont;
    private static IFontHandle StartTileValueFont => startTileValueFont ??= UiFonts.BuildHandle("AlegreyaSans-Bold.ttf", 20f);
    private static IFontHandle? startButtonFont;
    private static IFontHandle StartButtonFont => startButtonFont ??= UiFonts.BuildHandle("AlegreyaSans-Bold.ttf", 20f);
    private static IFontHandle? startHintFont;
    private static IFontHandle StartHintFont => startHintFont ??= UiFonts.BuildHandle("AlegreyaSans-Regular.ttf", 15f);
    private static IFontHandle? startPlannedTitleFont;
    private static IFontHandle StartPlannedTitleFont => startPlannedTitleFont ??= UiFonts.BuildHandle("Cinzel.ttf", 18f);
    private static IFontHandle? startPlannedCountFont;
    private static IFontHandle StartPlannedCountFont => startPlannedCountFont ??= UiFonts.BuildHandle("AlegreyaSans-Bold.ttf", 15f);
    private static IFontHandle? startPlannedHintFont;
    private static IFontHandle StartPlannedHintFont => startPlannedHintFont ??= UiFonts.BuildHandle("AlegreyaSans-Regular.ttf", 16f);
    private static IFontHandle? startPlannedHintBoldFont;
    private static IFontHandle StartPlannedHintBoldFont => startPlannedHintBoldFont ??= UiFonts.BuildHandle("AlegreyaSans-Bold.ttf", 16f);
    private static IFontHandle? startTableHeaderFont;
    private static IFontHandle StartTableHeaderFont => startTableHeaderFont ??= UiFonts.BuildHandle("Cinzel.ttf", 14f);
    private static IFontHandle? startRowPositionFont;
    private static IFontHandle StartRowPositionFont => startRowPositionFont ??= UiFonts.BuildHandle("AlegreyaSans-Bold.ttf", 16f);
    private static IFontHandle? startRowFishFont;
    private static IFontHandle StartRowFishFont => startRowFishFont ??= UiFonts.BuildHandle("AlegreyaSans-Bold.ttf", 17f);
    private static IFontHandle? startRowAreaFont;
    private static IFontHandle StartRowAreaFont => startRowAreaFont ??= UiFonts.BuildHandle("AlegreyaSans-Regular.ttf", 15f);
    private static IFontHandle? startRowValueFont;
    private static IFontHandle StartRowValueFont => startRowValueFont ??= UiFonts.BuildHandle("AlegreyaSans-Bold.ttf", 17f);
    private static IFontHandle? startRowRegularFont;
    private static IFontHandle StartRowRegularFont => startRowRegularFont ??= UiFonts.BuildHandle("AlegreyaSans-Regular.ttf", 17f);
    private static IFontHandle? startRowBaitCountFont;
    private static IFontHandle StartRowBaitCountFont => startRowBaitCountFont ??= UiFonts.BuildHandle("AlegreyaSans-Bold.ttf", 16f);

    /// <summary>Alle Font-Handles dieser Klasse einmal referenzieren, damit Dalamud sie beim (asynchronen)
    /// Atlas-Bau direkt beim Plugin-Start mit erstellt, statt erst beim ersten Zeichnen der jeweiligen Seite
    /// - 1:1 dasselbe Muster wie CodexTheme.PreloadFonts in TheExplorersCodex (Nutzer-Report: Menü/Overlay
    /// zeigten beim allerersten Öffnen kurz Platzhalter-Schrift + dadurch falsch berechnete, textbreiten-
    /// abhängige Positionen, bis der jeweilige Font-Handle fertig gebaut war). Von Plugin() aus aufrufen,
    /// NACH UiFonts.Initialize(). Das bloße Lesen der Property reicht aus (kein Warten auf den Bauvorgang
    /// nötig) - jedes Handle startet seinen eigenen Bauvorgang beim ersten Zugriff.</summary>
    internal static void PreloadFonts()
    {
        _ = AboutBodyFont;
        _ = AboutCardTitleFont;
        _ = AboutDividerLabelFont;
        _ = AboutHintFont;
        _ = AboutKofiLabelFont;
        _ = AboutLinkButtonLabelFont;
        _ = AboutTitleFont;
        _ = AboutVersionBadgeFont;
        _ = BodyBoldFont;
        _ = BodySmallFont;
        _ = BrandLargeFont;
        _ = BrandSmallFont;
        _ = ChangelogChangeTextFont;
        _ = ChangelogCollapsedTitleFont;
        _ = ChangelogMetaFont;
        _ = ChangelogNavBadgeFont;
        _ = ChangelogTagBadgeFont;
        _ = ChangelogVersionTitleFont;
        _ = DebugButtonLabelFont;
        _ = DebugCardTitleFont;
        _ = DebugDescriptionFont;
        _ = DebugStatusTextFont;
        _ = DropdownCaptionFont;
        _ = DropdownValueFont;
        _ = FieldLabelFont;
        _ = FishDataBoldFont;
        _ = FishDataHeaderFont;
        _ = FishDataMetaFont;
        _ = FishDataRegularFont;
        _ = FishDataSmallBoldFont;
        _ = FishDataTabCountFont;
        _ = FishDataTabFont;
        _ = InstallButtonLabelFont;
        _ = LogBadgeFont;
        _ = LogChipCountFont;
        _ = LogFilterLabelFont;
        _ = LogGhostButtonFont;
        _ = LogMetaFont;
        _ = LogRowFont;
        _ = LogSearchInputFont;
        _ = LogTableHeaderFont;
        _ = NavLabelFont;
        _ = PageTitleFont;
        _ = PluginDescriptionFont;
        _ = PluginInstalledLabelFont;
        _ = PluginNameFont;
        _ = StartAreaFont;
        _ = StartButtonFont;
        _ = StartFishNameFont;
        _ = StartHintFont;
        _ = StartNextUpLabelFont;
        _ = StartPillLabelFont;
        _ = StartPlannedCountFont;
        _ = StartPlannedHintBoldFont;
        _ = StartPlannedHintFont;
        _ = StartPlannedTitleFont;
        _ = StartRowAreaFont;
        _ = StartRowBaitCountFont;
        _ = StartRowFishFont;
        _ = StartRowPositionFont;
        _ = StartRowRegularFont;
        _ = StartRowValueFont;
        _ = StartStatusFont;
        _ = StartTableHeaderFont;
        _ = StartTileLabelFont;
        _ = StartTileValueFont;
        _ = SubtitleFont;
    }

    // Nicht in UiTheme (seiten-/spezifisch, siehe Aufgabenstellung-Hex-Werte wörtlich).
    private static readonly Vector4 StartRunningBorder = new(0.184f, 0.420f, 0.290f, 1f); // #2F6B4A
    private static readonly Vector4 StartStopTextColor = new(0.165f, 0.055f, 0.031f, 1f); // #2A0E08

    private (string Text, DateTime ExpiresAtUtc, uint FishItemId)? startUndoToast;

    // "Planned Fish" nicht bei jedem einzelnen Frame neu berechnen (gleiches 500ms-Cache-Muster wie
    // CodexMenuWindow.DrawDatabasePage/databaseCacheTime - Nutzeranforderung "nicht jede Seite gerendert
    // ... sondern einmal, identisch wie beim Codex Plugin"), nur zeit- ODER zustandsbasiert neu bauen.
    private (BigFish Fish, FishWindow Window, DateTime FishUtc, float? Rarity)[]? startPlannedCache;
    private long startPlannedCacheTime;
    private int startPlannedCacheKey;

    // Identisch zu CodexMenuWindow.DrawAboutKofiButton (Nutzerbestätigung: derselbe Link).
    private const string KofiUrl = "https://ko-fi.com/horstbrot";

    // Kritisch/Fatal-Farbe - wie CodexTheme.LogCritFg bewusst außerhalb der geteilten UiTheme-Palette
    // (seiten-/level-spezifisch, kein generisches Theme-Token).
    private static readonly Vector4 LogCritFg = new(0.941f, 0.416f, 0.478f, 1f); // #F06A7A

    // Dropdown-/Regler-Breite und rechter Randabstand - identisch zu CodexTheme.BeginDropdownRow/
    // DrawDisplayCard (205px Breite, 25px Randabstand).
    private const float DropdownWidth = 205f;
    private const float DropdownRightMargin = UiMetrics.DropdownRightMargin;

    private static readonly Vector4 NotCaughtColor = new(0.95f, 0.35f, 0.4f, 1f);

    // Seiten dieses Menüs - 1:1 dieselbe Gruppierung wie CodexMenuWindow.MenuPage (dort zusätzlich
    // Overlay/Database/Blacklist/Statistics, die es bei Big Fish Helper (noch) nicht gibt).
    private enum AppPage
    {
        Start,
        General,
        BigFish,
        Plugins,
        Debug,
        Log,
        Changelog,
        About,
    }

    private AppPage currentPage = AppPage.Start;

    private readonly Plugin plugin;

    private string mountFilter = string.Empty;
    private string gearsetFilter = string.Empty;

    // ---- Zustand der Log-Seite - eigener, von MainWindow unabhängiger Zustand (1:1 wie
    // CodexMenuWindow, das ebenfalls seinen eigenen, von MainWindow.DrawLogPage getrennten
    // Log-Seitenzustand hält, siehe dessen Klassenkommentar bei DrawLogPage). ----
    private string logSearch = string.Empty;
    private readonly HashSet<LogEventLevel> logLevelFilter = new()
    {
        LogEventLevel.Verbose, LogEventLevel.Debug, LogEventLevel.Information,
        LogEventLevel.Warning, LogEventLevel.Error, LogEventLevel.Fatal,
    };
    private string? logSelectedSource;
    private bool logSelectMode;
    private readonly HashSet<long> logSelectedIds = new();
    private int? logLastClickedRowIndex;
    private bool logFollowNewLines = true;
    private List<LogEntry>? logFilteredCache;
    private List<string>? logSourcesCache;
    private Dictionary<LogEventLevel, int>? logLevelCountsCache;
    private int logTotalCountCache;
    private (string Search, string? Source, int LevelsMask, int BufferCount, long LastId) logCacheKey;
    private static readonly Regex LogTokenRegex = new(@"#\d+|\b\d+/\d+\b|\b[A-Za-z_][A-Za-z0-9_]*=[^\s,;]+", RegexOptions.Compiled);

    // ---- Zustand der Changelog-Seite - 1:1 wie CodexMenuWindow (Auf-/Zu-Zustand nur zur Laufzeit
    // gehalten, nicht gespeichert). ----
    private readonly Dictionary<string, bool> changelogExpanded = new();
    private Version? changelogUnseenThreshold;
    private bool changelogMarkedSeen;

    // Feste Größe wie CodexMenuWindow (Nutzeranforderung: "identische Menübreite und -höhe wie beim
    // Codex Menü") - 1247x850, kein Resize.
    private const ImGuiWindowFlags BaseFlags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoResize;

    public OceanMainWindow(Plugin plugin) : base("##BigFishHelperOceanMenu", BaseFlags)
    {
        this.plugin = plugin;
        Size = new Vector2(1247f, 850f);
        SizeCondition = ImGuiCond.Always;
    }

    public void Dispose() { }

    public override void PreDraw()
    {
        var t = UiTheme.Active;
        ImGui.PushStyleColor(ImGuiCol.WindowBg, t.BgWindow);
        ImGui.PushStyleColor(ImGuiCol.Border, t.LineFrame);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 1f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, UiMetrics.WindowRadius);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
    }

    public override void PostDraw()
    {
        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor(2);
    }

    public override void Draw()
    {
        // 1:1 wie CodexMenuWindow.Draw() - Loc.MenuLanguageOverride nur für die Dauer dieses einen
        // Draw()-Aufrufs setzen (per try/finally begrenzt), damit die "Menüsprache"-Auswahl (siehe
        // DrawLanguageCard) tatsächlich wirkt, ohne den Rest des Plugins (Status-Overlay etc.) zu
        // beeinflussen, das weiterhin der Spielsprache folgt.
        var config = plugin.Configuration;
        Loc.MenuLanguageOverride = config.MenuLanguage == MenuLanguage.German;
        try
        {
            DrawInner();
        }
        finally
        {
            Loc.MenuLanguageOverride = null;
        }
    }

    private void DrawInner()
    {
        var scale = ImGuiHelpers.GlobalScale;

        DrawSidebar(scale);
        ImGui.SameLine(0f, 0f);
        DrawContent(scale);

        // Dünne Trennlinie zwischen Seitenleiste und Inhalt, über die volle Fensterhöhe (1:1 wie
        // CodexMenuWindow.DrawInner - auf dem Fenster-eigenen Layer, nicht in einem der Childs).
        var windowPos = ImGui.GetWindowPos();
        var sidebarLineX = windowPos.X + UiMetrics.SidebarWidth * scale;
        ImGui.GetWindowDrawList().AddLine(
            new Vector2(sidebarLineX, windowPos.Y),
            new Vector2(sidebarLineX, windowPos.Y + ImGui.GetWindowSize().Y),
            ImGui.GetColorU32(UiTheme.Active.LineCard));

        UiWidgets.DrawWindowCorners(Ornaments, UiMetrics.CornerInsetMenu * scale);
    }

    /// <summary>Seitenleiste: Logo, EINE Gruppe ("SETTINGS") mit EINEM Eintrag ("Allgemein"), Schließen-Knopf unten - 1:1 wie CodexMenuWindow.DrawSidebar, nur ohne die übrigen (noch nicht gebauten) Seiten.</summary>
    private void DrawSidebar(float scale)
    {
        var T = UiTheme.Active;
        ImGui.PushStyleColor(ImGuiCol.ChildBg, T.BgSidebar);
        ImGui.BeginChild("##OceanMenuSidebar", new Vector2(UiMetrics.SidebarWidth * scale, 0f), false);
        ImGui.Indent(18f * scale);
        ImGui.Dummy(new Vector2(0f, 20f * scale));

        DrawLogo(scale);

        ImGui.Dummy(new Vector2(0f, 24f * scale));
        UiWidgets.SectionLabel(Loc.T("AUTOMATION", "AUTOMATION"));
        ImGui.Dummy(new Vector2(0f, 6f * scale));

        DrawNavItem(scale, AppPage.Start, UiIcon.Play, Loc.T("Start", "Start"));

        ImGui.Dummy(new Vector2(0f, 18f * scale));
        UiWidgets.SectionLabel(Loc.T("EINSTELLUNGEN", "SETTINGS"));
        ImGui.Dummy(new Vector2(0f, 6f * scale));

        DrawNavItem(scale, AppPage.General, FontAwesomeIcon.ArrowsAltH, Loc.T("Allgemein", "General"));
        DrawNavItem(scale, AppPage.BigFish, UiIcon.Fish, Loc.T("Fischdaten", "Fish Data"));

        ImGui.Dummy(new Vector2(0f, 18f * scale));
        UiWidgets.SectionLabel(Loc.T("SYSTEM", "SYSTEM"));
        ImGui.Dummy(new Vector2(0f, 6f * scale));

        DrawNavItem(scale, AppPage.Plugins, FontAwesomeIcon.Plug, Loc.T("Plugins", "Plugins"));

        // Nur in der Dev-Version (Entwickler-Werkzeug) - 1:1 wie CodexMenuWindow.DrawSidebar.
        if (Plugin.PluginInterface.IsDev)
            DrawNavItem(scale, AppPage.Debug, FontAwesomeIcon.Bug, Loc.T("Debug", "Debug"));

        DrawNavItem(scale, AppPage.Log, UiIcon.Console, Loc.T("Log", "Log"));
        DrawNavItem(scale, AppPage.Changelog, FontAwesomeIcon.FileAlt, Loc.T("Änderungen", "Changelog"), showNewBadge: ChangelogService.HasUnseenChangelog(plugin.Configuration));
        DrawNavItem(scale, AppPage.About, FontAwesomeIcon.InfoCircle, Loc.T("Über", "About"));

        DrawSidebarCloseButton(scale);

        ImGui.Unindent(18f * scale);
        ImGui.EndChild();
        ImGui.PopStyleColor();
    }

    /// <summary>Logo-Zeile - Fisch-Symbol links, rechts "BIG FISH" klein über "Helper" groß (+ Version) - 1:1 wie CodexMenuWindow.DrawLogo.</summary>
    private static void DrawLogo(float scale)
    {
        var T = UiTheme.Active;
        var iconSize = 40f * scale;
        var cursor = ImGui.GetCursorScreenPos();

        ImGui.SetCursorScreenPos(cursor + new Vector2(2f * scale, 2f * scale));
        Ornaments.DrawSidebarLogo(iconSize - 4f * scale);
        ImGui.SetCursorScreenPos(cursor);
        ImGui.Dummy(new Vector2(iconSize, iconSize));

        var drawList = ImGui.GetWindowDrawList();
        var textX = cursor.X + iconSize + 10f * scale;

        float smallHeight, largeHeight;
        using (BrandSmallFont.Push())
            smallHeight = ImGui.GetFontSize();
        using (BrandLargeFont.Push())
            largeHeight = ImGui.GetFontSize();

        var startY = cursor.Y + (iconSize - (smallHeight + largeHeight)) / 2f;

        using (BrandSmallFont.Push())
            drawList.AddText(new Vector2(textX, startY), ImGui.GetColorU32(T.TextSecondary), "BIG FISH");

        var brandText = Loc.T("Helfer", "Helper");
        var brandY = startY + smallHeight;
        float brandWidth;
        using (BrandLargeFont.Push())
        {
            brandWidth = ImGui.CalcTextSize(brandText).X;
            drawList.AddText(new Vector2(textX, brandY), ImGui.GetColorU32(T.TextHeading), brandText);
        }

        var versionText = $"(v{VersionText})";
        using (BodySmallFont.Push())
        {
            var versionHeight = ImGui.GetFontSize();
            drawList.AddText(new Vector2(textX + brandWidth + 6f * scale, brandY + largeHeight - versionHeight - 3f * scale),
                ImGui.GetColorU32(T.TextDim), versionText);
        }
    }

    /// <summary>Menüpunkt - 1:1 wie CodexMenuWindow.DrawNavItem, nur mit UiIcon (von Hand gezeichnet) statt FontAwesomeIcon.</summary>
    private void DrawNavItem(float scale, AppPage page, UiIcon icon, string label, bool showNewBadge = false)
    {
        var selected = currentPage == page;
        var T = UiTheme.Active;
        const float iconBoxSize = 16f;
        var iconPx = iconBoxSize * scale;

        var height = 30f * scale;
        var width = ImGui.GetContentRegionAvail().X - 18f * scale;
        var cursor = ImGui.GetCursorScreenPos();

        var clicked = ImGui.InvisibleButton($"##OceanNav_{page}", new Vector2(width, height));
        var hovered = ImGui.IsItemHovered();

        var drawList = ImGui.GetWindowDrawList();
        if (selected)
        {
            drawList.AddRectFilled(cursor, cursor + new Vector2(width, height), ImGui.GetColorU32(T.BgSelected), UiMetrics.ControlRadius);
            drawList.AddRectFilled(cursor, cursor + new Vector2(2f * scale, height), ImGui.GetColorU32(T.Accent));
        }
        else if (hovered)
        {
            drawList.AddRectFilled(cursor, cursor + new Vector2(width, height), ImGui.GetColorU32(T.BgSelected with { W = 0.5f }), UiMetrics.ControlRadius);
        }

        var fg = selected ? T.TextHeading : T.TextSecondary;
        var contentX = cursor.X + 10f * scale;

        UiIcons.Draw(icon, new Vector2(contentX, cursor.Y + (height - iconPx) / 2f), iconPx, ImGui.GetColorU32(selected ? T.Accent : fg));

        using (NavLabelFont.Push())
        {
            var labelSize = ImGui.CalcTextSize(label);
            drawList.AddText(new Vector2(contentX + iconPx + 10f * scale, cursor.Y + (height - labelSize.Y) / 2f), ImGui.GetColorU32(fg), label);
        }

        if (showNewBadge)
            DrawNavNewBadge(scale, cursor, width, height);

        if (clicked)
            currentPage = page;
    }

    /// <summary>"◆ NEU"-Badge rechtsbündig in einem Menüpunkt (Changelog) - 1:1 wie CodexMenuWindow.DrawNavNewBadge, Raute gezeichnet statt als Glyph.</summary>
    private static void DrawNavNewBadge(float scale, Vector2 itemCursor, float itemWidth, float itemHeight)
    {
        var T = UiTheme.Active;
        var label = Loc.T("NEU", "NEW");
        float labelWidth, textHeight;
        using (ChangelogNavBadgeFont.Push())
        {
            var size = ImGui.CalcTextSize(label);
            labelWidth = size.X;
            textHeight = size.Y;
        }

        var diamondSize = 6f * scale;
        var diamondGap = 5f * scale;
        var padding = new Vector2(7f * scale, 1f * scale);
        var badgeSize = new Vector2(diamondSize + diamondGap + labelWidth + padding.X * 2f, textHeight + padding.Y * 2f);
        var badgeCursor = new Vector2(itemCursor.X + itemWidth - 12f * scale - badgeSize.X, itemCursor.Y + (itemHeight - badgeSize.Y) / 2f);

        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(badgeCursor, badgeCursor + badgeSize, ImGui.GetColorU32(T.Accent), 3f * scale);

        var diamondCenter = new Vector2(badgeCursor.X + padding.X + diamondSize / 2f, badgeCursor.Y + badgeSize.Y / 2f);
        UiWidgetsExtra.Diamond(diamondCenter, diamondSize, ImGui.GetColorU32(T.TextOnAccent), ImGui.GetColorU32(T.TextOnAccent));

        using (ChangelogNavBadgeFont.Push())
            drawList.AddText(new Vector2(badgeCursor.X + padding.X + diamondSize + diamondGap, badgeCursor.Y + padding.Y), ImGui.GetColorU32(T.TextOnAccent), label);
    }

    /// <summary>Wie DrawNavItem(UiIcon), aber mit einem echten FontAwesomeIcon-Glyphen statt eines von Hand gezeichneten UiIcon - Nutzeranforderung
    /// ("FontAwesomeIcon.InfoCircle auch beim Big Fish Helper nutzen") nur für den "Über"-Menüpunkt, alle anderen Menüpunkte bleiben bei UiIcon.</summary>
    private void DrawNavItem(float scale, AppPage page, FontAwesomeIcon icon, string label, bool showNewBadge = false)
    {
        var selected = currentPage == page;
        var T = UiTheme.Active;
        var iconFont = Plugin.PluginInterface.UiBuilder.IconFontHandle;

        float iconWidth;
        using (iconFont.Push())
            iconWidth = ImGui.CalcTextSize(icon.ToIconString()).X;

        var height = 30f * scale;
        var width = ImGui.GetContentRegionAvail().X - 18f * scale;
        var cursor = ImGui.GetCursorScreenPos();

        var clicked = ImGui.InvisibleButton($"##OceanNav_{page}", new Vector2(width, height));
        var hovered = ImGui.IsItemHovered();

        var drawList = ImGui.GetWindowDrawList();
        if (selected)
        {
            drawList.AddRectFilled(cursor, cursor + new Vector2(width, height), ImGui.GetColorU32(T.BgSelected), UiMetrics.ControlRadius);
            drawList.AddRectFilled(cursor, cursor + new Vector2(2f * scale, height), ImGui.GetColorU32(T.Accent));
        }
        else if (hovered)
        {
            drawList.AddRectFilled(cursor, cursor + new Vector2(width, height), ImGui.GetColorU32(T.BgSelected with { W = 0.5f }), UiMetrics.ControlRadius);
        }

        var fg = selected ? T.TextHeading : T.TextSecondary;
        var contentX = cursor.X + 10f * scale;

        using (iconFont.Push())
            drawList.AddText(new Vector2(contentX, cursor.Y + (height - ImGui.GetFontSize()) / 2f), ImGui.GetColorU32(selected ? T.Accent : fg), icon.ToIconString());

        using (NavLabelFont.Push())
        {
            var labelSize = ImGui.CalcTextSize(label);
            drawList.AddText(new Vector2(contentX + iconWidth + 10f * scale, cursor.Y + (height - labelSize.Y) / 2f), ImGui.GetColorU32(fg), label);
        }

        if (showNewBadge)
            DrawNavNewBadge(scale, cursor, width, height);

        if (clicked)
            currentPage = page;
    }

    /// <summary>"Schließen"-Knopf ganz unten in der Seitenleiste - 1:1 wie CodexMenuWindow.DrawSidebarCloseButton.</summary>
    private void DrawSidebarCloseButton(float scale)
    {
        var width = ImGui.GetContentRegionAvail().X - 18f * scale;
        var height = 30f * scale;
        var bottomMargin = 20f * scale;

        ImGui.SetCursorPosY(ImGui.GetWindowHeight() - height - bottomMargin);
        if (UiNav.AccentButton(Loc.T("Schließen", "Close"), new Vector2(width, height), scale, BodyBoldFont))
            IsOpen = false;
    }

    /// <summary>Inhaltsbereich: Seitenkopf (Titel/Untertitel/Trenn-Ornament) + jeweilige Seite - 1:1 wie CodexMenuWindow.DrawContent (About hat bewusst KEINEN gemeinsamen Seitenkopf, siehe dortigen Kommentar).</summary>
    private void DrawContent(float scale)
    {
        ImGui.BeginChild("##OceanMenuContent", Vector2.Zero, false);
        ImGui.Indent(UiMetrics.PageMargin * scale);
        ImGui.Dummy(new Vector2(0f, 28f * scale));

        var T = UiTheme.Active;
        // About hat KEINEN gemeinsamen Seitenkopf (zentriert stattdessen Logo/Name/Version selbst,
        // siehe DrawAboutPage) - 1:1 wie CodexMenuWindow.DrawContent (dort ebenfalls nur About
        // ausgenommen, Log bekommt ganz normal den gemeinsamen Seitenkopf). Big Fish zeichnet laut
        // eigener Aufgabenstellung (Abschnitt 1) seinen Seitenkopf selbst, MIT Suchfeld/Toggle rechts
        // bündig zum Ornament - der generische Seitenkopf kennt diese rechte Zusatzzeile nicht.
        if (currentPage != AppPage.About && currentPage != AppPage.BigFish)
        {
            var (pageTitle, pageSubtitle) = currentPage switch
            {
                AppPage.Start => (Loc.T("Start", "Start"), Loc.T("Fliegt vor dem Prep-Timer zum nächsten geplanten Fisch, wechselt zu Fischer und angelt mit dem AutoHook-Preset, sobald der Prep-Timer beginnt.", "Flies to the next planned fish before its prep time, switches to Fisher and fishes with the AutoHook preset once the prep time begins.")),
                AppPage.Plugins => (Loc.T("Plugins", "Plugins"), Loc.T("Begleit-Plugins, auf die sich die Automation stützt.", "Companion plugins the automation relies on.")),
                AppPage.Debug => (Loc.T("Debug", "Debug"), Loc.T("Nur relevant, um Angel-Positionen ohne Fremd-Tool zu ermitteln.", "Only relevant for finding fishing positions without a third-party tool.")),
                AppPage.Log => (Loc.T("Log", "Log"), Loc.T("Eigene Log-Zeilen dieses Plugins - durchsuchbar und nach Stufe filterbar.", "This plugin's own log lines - searchable and filterable by level.")),
                AppPage.Changelog => (Loc.T("Änderungsprotokoll", "Changelog"), Loc.T("Was sich im Big Fish Helper geändert hat - neueste Einträge zuerst.", "What changed in Big Fish Helper - newest entries first.")),
                _ => (Loc.T("Allgemein", "General"), Loc.T("Allgemeine Einstellungen der Automation.", "General settings of the automation.")),
            };

            using (PageTitleFont.Push())
                ImGui.TextColored(T.TextHeading, pageTitle);

            ImGui.SetCursorPosY(ImGui.GetCursorPosY() - 2f * scale);
            using (SubtitleFont.Push())
                ImGui.TextColored(T.TextSecondary, pageSubtitle);

            UiWidgets.DrawDividerOrnament(Ornaments, scale);
            ImGui.Dummy(new Vector2(0f, 16f * scale));
        }

        switch (currentPage)
        {
            case AppPage.Start:
                DrawStartPage(scale);
                break;
            case AppPage.BigFish:
                DrawFishDataPage(scale);
                break;
            case AppPage.Plugins:
                DrawPluginsPage(scale);
                break;
            case AppPage.Debug:
                if (Plugin.PluginInterface.IsDev)
                    DrawDebugPage(scale);
                break;
            case AppPage.Log:
                DrawLogPage(scale);
                break;
            case AppPage.Changelog:
                DrawChangelogPage(scale);
                break;
            case AppPage.About:
                DrawAboutPage(scale);
                break;
            default:
                DrawGeneralPage(scale);
                break;
        }

        ImGui.Unindent(UiMetrics.PageMargin * scale);
        ImGui.EndChild();
    }

    // ---- Seite "Start" - Hero-Karte (Status/Next-up/Prep-Infos links, Start/Stop rechts) + Tabelle
    // "Planned Fish" darunter. Reine Darstellung, die komplette Automatik-Logik kommt unverändert aus
    // FishingAutomation/Configuration (siehe GetPlannedFish/Start/Stop/IsAtCastablePosition) - 1:1
    // dieselben Daten wie die alte MainWindow.DrawPlayPage, nur im Ocean-Stil gezeichnet. ----

    private void DrawStartPage(float scale)
    {
        var automation = plugin.Automation;
        var now = DateTime.UtcNow;
        var planned = GetStartPlannedFishCached(automation, now);
        var contentWidth = ImGui.GetContentRegionAvail().X - PluginsPageRightMargin * scale;

        DrawStartHeroCard(scale, contentWidth, automation, planned, now);
        ImGui.Dummy(new Vector2(0f, 20f * scale));
        DrawStartPlannedHeader(scale, contentWidth, planned.Length);
        ImGui.Dummy(new Vector2(0f, 8f * scale));
        DrawStartPlannedTable(scale, contentWidth, automation, planned, now);

        if (startUndoToast is { } toast)
        {
            if (now >= toast.ExpiresAtUtc)
            {
                startUndoToast = null;
            }
            else
            {
                ImGui.Dummy(new Vector2(0f, 10f * scale));
                DrawStartUndoToast(scale, toast.Text, toast.FishItemId);
            }
        }
    }

    /// <summary>Wie CodexMenuWindow.DrawDatabasePage - GetPlannedFish() (Lumina-/FishWindows-Lookups über alle aktivierten Fische) nicht bei
    /// jedem Frame neu berechnen, nur alle 500ms oder wenn sich die Zahl der aktivierten Fische ändert (z.B. Entfernen-Knopf/Undo - siehe
    /// InvalidateStartPlannedCache). Zeitbasierte Auffrischung reicht für Countdowns/Fensterwechsel, die ohnehin nicht Frame-genau sein müssen.</summary>
    private (BigFish Fish, FishWindow Window, DateTime FishUtc, float? Rarity)[] GetStartPlannedFishCached(FishingAutomation automation, DateTime now)
    {
        var nowMs = Environment.TickCount64;
        var key = plugin.Configuration.EnabledFish.Count;
        if (startPlannedCache == null || key != startPlannedCacheKey || nowMs - startPlannedCacheTime >= 500)
        {
            startPlannedCache = automation.GetPlannedFish(now);
            startPlannedCacheTime = nowMs;
            startPlannedCacheKey = key;
        }

        return startPlannedCache;
    }

    /// <summary>Sofortiges Neu-Berechnen erzwingen (z.B. nach Entfernen/Undo in DrawStartPlannedRow/DrawStartUndoToast) - ohne das würde die Tabelle
    /// bis zu 500ms lang noch den gerade entfernten/wieder hinzugefügten Fisch zeigen.</summary>
    private void InvalidateStartPlannedCache() => startPlannedCacheTime = 0;

    private void DrawStartHeroCard(float scale, float contentWidth, FishingAutomation automation,
        (BigFish Fish, FishWindow Window, DateTime FishUtc, float? Rarity)[] planned, DateTime now)
    {
        var T = UiTheme.Active;
        var running = automation.IsRunning;
        var hasNext = planned.Length > 0;

        var stacked = contentWidth < 700f * scale;
        var rightWidth = stacked ? contentWidth : 250f * scale;
        var leftWidth = stacked ? contentWidth : contentWidth - rightWidth;

        var padX = 22f * scale;
        var padY = 18f * scale;

        // ---- Linke Spalte zuerst nur VERMESSEN (keine ChannelsSplit erlaubt, siehe Aufgabenstellung
        // Grundregel 0 - daher muss die Kartenhöhe VOR dem Hintergrund feststehen). ----
        float pillLabelHeight;
        using (StartPillLabelFont.Push())
            pillLabelHeight = ImGui.GetTextLineHeight();
        var pillHeight = 2f * scale + pillLabelHeight; // = UiWidgetsExtra.Badge-Formel (padding.Y=1*scale*2)
        float statusTextHeight;
        using (StartStatusFont.Push())
            statusTextHeight = ImGui.GetTextLineHeight();
        var statusRowHeight = MathF.Max(pillHeight, statusTextHeight);

        var iconDiameter = 48f * scale;
        float nextUpLabelH, fishNameH, areaH;
        using (StartNextUpLabelFont.Push())
            nextUpLabelH = ImGui.GetTextLineHeight();
        using (StartFishNameFont.Push())
            fishNameH = ImGui.GetTextLineHeight();
        using (StartAreaFont.Push())
            areaH = ImGui.GetTextLineHeight();
        var nextUpTextBlockHeight = nextUpLabelH + 2f * scale + fishNameH + 2f * scale + areaH;
        var nextUpRowHeight = MathF.Max(iconDiameter, nextUpTextBlockHeight);

        float tileLabelH, tileValueH;
        using (StartTileLabelFont.Push())
            tileLabelH = ImGui.GetTextLineHeight();
        using (StartTileValueFont.Push())
            tileValueH = ImGui.GetTextLineHeight();
        var tileRowHeight = 8f * scale * 2f + tileLabelH + 4f * scale + tileValueH; // = UiWidgetsExtra.InfoTile-Formel

        var leftContentHeight = statusRowHeight + 12f * scale + nextUpRowHeight + 12f * scale + tileRowHeight;
        var leftHeight = padY * 2f + leftContentHeight;

        var buttonHeight = 52f * scale;
        float hintHeight;
        using (StartHintFont.Push())
            hintHeight = ImGui.GetTextLineHeight();
        var rightContentHeight = buttonHeight + 10f * scale + hintHeight;
        var rightHeight = padY * 2f + rightContentHeight;

        var cardHeight = stacked ? leftHeight + rightHeight : MathF.Max(leftHeight, rightHeight);

        // ---- Hintergrund (jetzt bekannte Höhe) ----
        var cardOrigin = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(cardOrigin, cardOrigin + new Vector2(contentWidth, cardHeight), ImGui.GetColorU32(T.BgCard), UiMetrics.CardRadius * scale);

        var rightOrigin = stacked ? cardOrigin + new Vector2(0f, leftHeight) : cardOrigin + new Vector2(leftWidth, 0f);
        dl.AddRectFilled(rightOrigin, rightOrigin + new Vector2(rightWidth, stacked ? rightHeight : cardHeight), ImGui.GetColorU32(T.BgStatus),
            UiMetrics.CardRadius * scale, stacked ? ImDrawFlags.RoundCornersBottom : ImDrawFlags.RoundCornersRight);
        if (!stacked)
        {
            dl.AddLine(rightOrigin, rightOrigin + new Vector2(0f, cardHeight), ImGui.GetColorU32(T.LineCard));
        }
        else
        {
            dl.AddLine(rightOrigin, rightOrigin + new Vector2(contentWidth, 0f), ImGui.GetColorU32(T.LineCard));
        }

        // Äußerer Kartenrand ZULETZT (über den Status-Box-Füllungen) - sonst übermalt die BgStatus-
        // Füllung der rechten Box ihren Teil des äußeren Randes (Nutzer-Report: Umrandung um die
        // Start-Button-Box fehlt).
        dl.AddRect(cardOrigin, cardOrigin + new Vector2(contentWidth, cardHeight), ImGui.GetColorU32(T.LineCard), UiMetrics.CardRadius * scale);

        // ---- Linke Spalte zeichnen ----
        var leftOrigin = cardOrigin + new Vector2(padX, padY);
        var statusY = leftOrigin.Y + (statusRowHeight - pillHeight) / 2f;
        ImGui.SetCursorScreenPos(new Vector2(leftOrigin.X, statusY));
        var pillText = running ? Loc.T("LÄUFT", "RUNNING") : Loc.T("IDLE", "IDLE");
        var pillFg = running ? FishDataActiveGreen : T.TextSecondary;
        var pillBg = running ? FishDataActiveGreen with { W = 0.1f } : T.BgToggleOff;
        var pillLine = running ? StartRunningBorder : T.LineFrame;
        var pillSize = UiWidgetsExtra.Badge(pillText, pillFg, pillBg, pillLine, scale, StartPillLabelFont);

        var statusText = running ? automation.StatusText : Loc.T($"Bereit – {planned.Length} Fisch(e) geplant", $"Ready – {planned.Length} fish planned");
        ImGui.SetCursorScreenPos(new Vector2(leftOrigin.X + pillSize.X + 8f * scale, leftOrigin.Y + (statusRowHeight - statusTextHeight) / 2f));
        using (StartStatusFont.Push())
            ImGui.TextColored(T.TextDesc, statusText);

        var nextUpY = leftOrigin.Y + statusRowHeight + 12f * scale;
        var iconCenter = new Vector2(leftOrigin.X + iconDiameter / 2f, nextUpY + nextUpRowHeight / 2f);
        var itemSheet = Plugin.DataManager.GetExcelSheet<Item>();

        if (hasNext)
        {
            var fish = planned[0].Fish;
            var icon = itemSheet.TryGetRow(fish.ItemId, out var fishItem) ? fishItem.Icon : 0u;
            var wrap = icon != 0 ? GetFishDataIcon(icon).GetWrapOrEmpty() : null;
            UiWidgetsExtra.RoundIcon(iconCenter, iconDiameter / 2f, T.BgInput, T.Accent, wrap?.Handle);

            var textX = leftOrigin.X + iconDiameter + 14f * scale;
            var textTop = nextUpY + (nextUpRowHeight - nextUpTextBlockHeight) / 2f;
            ImGui.SetCursorScreenPos(new Vector2(textX, textTop));
            using (StartNextUpLabelFont.Push())
                UiWidgets.DrawSpacedText(Loc.T("ALS NÄCHSTES", "NEXT UP"), T.TextMuted, 1f);

            ImGui.SetCursorScreenPos(new Vector2(textX, textTop + nextUpLabelH + 2f * scale));
            using (StartFishNameFont.Push())
                ImGui.TextColored(T.TextHeading, FishDataDisplayName(fish, itemSheet));

            var territorySheet = Plugin.DataManager.GetExcelSheet<TerritoryType>();
            var spotSheet = Plugin.DataManager.GetExcelSheet<FishingSpot>();
            var area = GetStartFishAreaText(fish, territorySheet, spotSheet);
            var rarityText = planned[0].Rarity is { } rarity ? $"{rarity * 100f:0.#}%" : Loc.T("Unbekannt", "Unknown");
            var areaLine = area != null
                ? Loc.T($"{area} · Rarität {rarityText}", $"{area} · Rarity {rarityText}")
                : Loc.T($"Rarität {rarityText}", $"Rarity {rarityText}");
            ImGui.SetCursorScreenPos(new Vector2(textX, textTop + nextUpLabelH + 2f * scale + fishNameH + 2f * scale));
            using (StartAreaFont.Push())
                ImGui.TextColored(T.TextSecondary, areaLine);
        }
        else
        {
            UiWidgetsExtra.RoundIcon(iconCenter, iconDiameter / 2f, T.BgInput, T.LineFrame, null);
            var textX = leftOrigin.X + iconDiameter + 14f * scale;
            ImGui.SetCursorScreenPos(new Vector2(textX, nextUpY + (nextUpRowHeight - fishNameH) / 2f));
            using (StartFishNameFont.Push())
                ImGui.TextColored(T.TextMuted, Loc.T("Kein Fisch geplant", "No fish planned"));
        }

        var tilesY = nextUpY + nextUpRowHeight + 12f * scale;
        var tileGap = 10f * scale;
        var tileWidth = (leftWidth - padX * 2f - tileGap * 2f) / 3f;

        var prepValue = hasNext ? FormatStartCountdownValue(planned[0].FishUtc, planned[0].Window, now, isPrep: true) : "-";
        var windowValue = hasNext ? FormatStartCountdownValue(planned[0].FishUtc, planned[0].Window, now, isPrep: false) : "-";
        var presetValue = hasNext
            ? plugin.Configuration.FishAutoHookPresets.GetValueOrDefault(planned[0].Fish.ItemId) ?? Loc.T("Kein Preset", "No preset")
            : "-";

        ImGui.SetCursorScreenPos(new Vector2(leftOrigin.X, tilesY));
        UiWidgetsExtra.InfoTile(Loc.T("Prep beginnt", "Prep starts"), prepValue, scale, StartTileLabelFont, StartTileValueFont, tileWidth);
        ImGui.SetCursorScreenPos(new Vector2(leftOrigin.X + tileWidth + tileGap, tilesY));
        UiWidgetsExtra.InfoTile(Loc.T("Fenster öffnet", "Window opens"), windowValue, scale, StartTileLabelFont, StartTileValueFont, tileWidth);
        ImGui.SetCursorScreenPos(new Vector2(leftOrigin.X + (tileWidth + tileGap) * 2f, tilesY));
        UiWidgetsExtra.InfoTile(Loc.T("AutoHook-Preset", "AutoHook preset"), presetValue, scale, StartTileLabelFont, StartTileValueFont, tileWidth, FishDataTextButton);

        // ---- Rechte Spalte zeichnen ----
        var canStart = automation.HasEnabledFish;
        var buttonWidth = rightWidth - padX * 2f;
        var buttonOrigin = new Vector2(rightOrigin.X + padX, rightOrigin.Y + (( stacked ? rightHeight : cardHeight) - rightContentHeight) / 2f);

        var buttonColor = running ? FishDataCoralRed : T.Accent;
        var buttonTextColor = running ? StartStopTextColor : T.TextOnAccent;
        var buttonDisabled = !running && !canStart;

        var glowPad = 4f * scale;
        dl.AddRectFilled(buttonOrigin - new Vector2(glowPad), buttonOrigin + new Vector2(buttonWidth, buttonHeight) + new Vector2(glowPad),
            ImGui.GetColorU32(buttonColor with { W = 0.15f }), (UiMetrics.CardRadius + 2f) * scale);

        ImGui.SetCursorScreenPos(buttonOrigin);
        if (buttonDisabled)
            ImGui.BeginDisabled();
        var buttonClicked = ImGui.InvisibleButton("##OceanStartButton", new Vector2(buttonWidth, buttonHeight));
        var buttonHovered = ImGui.IsItemHovered();
        if (buttonDisabled)
            ImGui.EndDisabled();

        dl.AddRectFilled(buttonOrigin, buttonOrigin + new Vector2(buttonWidth, buttonHeight),
            ImGui.GetColorU32(buttonDisabled ? buttonColor with { W = 0.35f } : buttonColor with { W = buttonHovered ? 0.85f : 1f }), UiMetrics.CardRadius * scale);
        var buttonBorder = new Vector4(buttonColor.X * 0.6f, buttonColor.Y * 0.6f, buttonColor.Z * 0.6f, 1f);
        dl.AddRect(buttonOrigin, buttonOrigin + new Vector2(buttonWidth, buttonHeight), ImGui.GetColorU32(buttonBorder), UiMetrics.CardRadius * scale, ImDrawFlags.None, 1.5f * scale);

        var buttonIcon = running ? UiIcon.Stop : UiIcon.Play;
        var buttonLabel = running ? Loc.T("Stopp", "Stop") : Loc.T("Start", "Start");
        float labelWidth;
        using (StartButtonFont.Push())
            labelWidth = ImGui.CalcTextSize(buttonLabel).X;
        var iconSize = 15f * scale;
        var iconGap = 8f * scale;
        var contentWidthPx = iconSize + iconGap + labelWidth;
        var contentX = buttonOrigin.X + (buttonWidth - contentWidthPx) / 2f;
        UiIcons.Draw(buttonIcon, new Vector2(contentX, buttonOrigin.Y + (buttonHeight - iconSize) / 2f), iconSize, ImGui.GetColorU32(buttonTextColor));
        using (StartButtonFont.Push())
            dl.AddText(new Vector2(contentX + iconSize + iconGap, buttonOrigin.Y + (buttonHeight - ImGui.GetTextLineHeight()) / 2f), ImGui.GetColorU32(buttonTextColor), buttonLabel);

        if (buttonHovered && !buttonDisabled)
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

        if (buttonClicked && !buttonDisabled)
        {
            if (running)
            {
                automation.Stop();
            }
            else
            {
                automation.Start();
                if (plugin.Configuration.ShowOverlayOnStart)
                    plugin.StatusOverlayWindow.IsOpen = true;
            }
        }

        var hint = !canStart
            ? Loc.T("Zuerst einen Fisch in Fish Data planen.", "Plan a fish in Fish Data first.")
            : running
                ? Loc.T("Stoppt sofort.", "Stops immediately.")
                : Loc.T("Startet mit dem ersten Fisch der Liste.", "Starts with the first fish in the list.");
        var hintY = buttonOrigin.Y + buttonHeight + 10f * scale;
        float hintWidth;
        using (StartHintFont.Push())
            hintWidth = ImGui.CalcTextSize(hint).X;
        ImGui.SetCursorScreenPos(new Vector2(buttonOrigin.X + (buttonWidth - hintWidth) / 2f, hintY));
        using (StartHintFont.Push())
            ImGui.TextColored(T.TextMuted, hint);

        ImGui.SetCursorScreenPos(cardOrigin + new Vector2(0f, cardHeight));
    }

    private static string FormatStartCountdownValue(DateTime fishUtc, FishWindow window, DateTime now, bool isPrep)
    {
        if (isPrep)
        {
            if (window.IsActive(now))
                return Loc.T("Fenster aktiv", "Window active");
            return now >= fishUtc
                ? Loc.T("Jetzt (Prep)", "Now (prep)")
                : Loc.T($"in {FormatCountdown(fishUtc - now)}", $"in {FormatCountdown(fishUtc - now)}");
        }

        return window.IsActive(now)
            ? Loc.T($"in {FormatCountdown(window.EndUtc - now)}", $"in {FormatCountdown(window.EndUtc - now)}")
            : Loc.T($"in {FormatCountdown(window.StartUtc - now)}", $"in {FormatCountdown(window.StartUtc - now)}");
    }

    private static string? GetStartFishAreaText(BigFish fish, ExcelSheet<TerritoryType> territorySheet, ExcelSheet<FishingSpot> spotSheet)
    {
        var territoryName = territorySheet.TryGetRow(fish.TerritoryId, out var territory) ? territory.PlaceName.ValueNullable?.Name.ToString() : null;
        var spotName = spotSheet.TryGetRow(fish.FishingSpotId, out var spot) ? spot.PlaceName.ValueNullable?.Name.ToString() : null;
        if (string.IsNullOrEmpty(territoryName) && string.IsNullOrEmpty(spotName))
            return null;
        if (string.IsNullOrEmpty(territoryName))
            return spotName;
        if (string.IsNullOrEmpty(spotName))
            return territoryName;
        return $"{territoryName} · {spotName}";
    }

    private void DrawStartPlannedHeader(float scale, float contentWidth, int count)
    {
        var T = UiTheme.Active;
        var origin = ImGui.GetCursorScreenPos();

        ImGui.SetCursorScreenPos(origin);
        using (StartPlannedTitleFont.Push())
            ImGui.TextColored(T.TextCardTitle, Loc.T("Geplante Fische", "Planned Fish"));
        var titleHeight = ImGui.GetItemRectSize().Y;
        var titleWidth = ImGui.GetItemRectSize().X;

        var badgeText = count.ToString(CultureInfo.InvariantCulture);
        float badgeTextHeight;
        using (StartPlannedCountFont.Push())
            badgeTextHeight = ImGui.GetTextLineHeight();
        ImGui.SetCursorScreenPos(new Vector2(origin.X + titleWidth + 8f * scale, origin.Y + (titleHeight - badgeTextHeight) / 2f - 1f * scale));
        UiWidgetsExtra.Badge(badgeText, T.Accent, T.BgSelected, T.BgSelected, scale, StartPlannedCountFont, radius: 10f);

        var hintPrefix = Loc.T("Fisch hinzufügen über ", "Add fish via ");
        var hintLink = Loc.T("Fisch-Daten", "Fish Data");
        float prefixWidth, hintTextHeight;
        using (StartPlannedHintFont.Push())
        {
            prefixWidth = ImGui.CalcTextSize(hintPrefix).X;
            hintTextHeight = ImGui.GetTextLineHeight();
        }
        float linkWidth;
        using (StartPlannedHintBoldFont.Push())
            linkWidth = ImGui.CalcTextSize(hintLink).X;

        var hintY = origin.Y + (titleHeight - hintTextHeight) / 2f;
        var hintX = origin.X + contentWidth - prefixWidth - linkWidth;
        ImGui.SetCursorScreenPos(new Vector2(hintX, hintY));
        using (StartPlannedHintFont.Push())
            ImGui.TextColored(T.TextMuted, hintPrefix);

        ImGui.SetCursorScreenPos(new Vector2(hintX + prefixWidth, hintY));
        using (StartPlannedHintBoldFont.Push())
        {
            if (UiWidgets.LinkText(hintLink, T.Accent))
                currentPage = AppPage.BigFish;
        }

        ImGui.SetCursorScreenPos(new Vector2(origin.X, origin.Y + titleHeight));
    }

    private void DrawStartPlannedTable(float scale, float contentWidth, FishingAutomation automation,
        (BigFish Fish, FishWindow Window, DateTime FishUtc, float? Rarity)[] planned, DateTime now)
    {
        var T = UiTheme.Active;
        var config = plugin.Configuration;
        var itemSheet = Plugin.DataManager.GetExcelSheet<Item>();
        var territorySheet = Plugin.DataManager.GetExcelSheet<TerritoryType>();
        var spotSheet = Plugin.DataManager.GetExcelSheet<FishingSpot>();
        var presetNames = AutoHookPresets.GetNames();

        var width = contentWidth;
        var headerHeight = 34f * scale;
        const int columnCount = 9;

        ImGui.PushStyleColor(ImGuiCol.ChildBg, T.BgCard);
        ImGui.PushStyleColor(ImGuiCol.Border, T.LineCard);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, UiMetrics.CardRadius * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildBorderSize, 1f * scale);
        // Ohne explizites WindowPadding=0 fraß ImGuis Standard-Innenabstand des Child-Fensters unbemerkt
        // in die exakt bemessene Körperhöhe hinein und schnitt die letzte Zeile am unteren Rand ab
        // (Nutzer-Report: "Tabelle ist weiterhin abgeschnitten") - alle Positionen hier sind ohnehin
        // bereits von Hand berechnet, der ImGui-Standardabstand hatte nie einen Zweck.
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        // Inhaltsgetriebene Höhe (nie an die aktuell sichtbare Fensterhöhe geklemmt, siehe Nutzer-Report
        // "Tabelle sieht abgeschnitten aus" - ein an den Viewport geklemmter Wert schnitt die Karte bei
        // nur 1-2 Zeilen mitten im unteren Rand ab, statt einfach ihre natürliche Höhe zu zeigen und
        // die Seite bei Bedarf scrollen zu lassen). Ab mehr als 6 Zeilen scrollt stattdessen die Tabelle
        // selbst intern (siehe Body-Child/Clipper unten), damit sie die Seite nicht beliebig wachsen lässt.
        var bodyMinHeight = 60f * scale;
        const float maxVisibleRows = 6f;
        var bodyHeightUncapped = MathF.Max(bodyMinHeight, planned.Length * 46f * scale);
        var bodyRowsHeight = MathF.Min(bodyHeightUncapped, maxVisibleRows * 46f * scale);
        // Großzügiger fester Sicherheitsabstand (statt eines knappen 2px-Puffers) - jede noch so kleine,
        // schwer vorhersagbare Diskrepanz (Rundung, Rahmenbreite, ImGui-interne Mindestabstände) hat
        // bislang trotz exakter Neuberechnung die letzte Zeile am unteren Rand abgeschnitten (Nutzer-
        // Report, mehrfach). Lieber zuverlässig ein paar px Luft lassen als die Zeile zu riskieren.
        var height = headerHeight + bodyRowsHeight + 24f * scale;
        ImGui.BeginChild("##OceanStartPlannedCard", new Vector2(width, height), true);

        var innerWidth = ImGui.GetContentRegionAvail().X;
        var tableWidth = innerWidth - ImGui.GetStyle().ScrollbarSize;
        var headerLeft = ImGui.GetCursorScreenPos();

        if (ImGui.BeginTable("##OceanStartPlannedHeaderTable", columnCount, ImGuiTableFlags.None, new Vector2(tableWidth, headerHeight)))
        {
            StartPlannedSetupColumns(scale);
            ImGui.TableNextRow(ImGuiTableRowFlags.Headers, headerHeight);
            ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(T.BgStatus));
            ImGui.TableSetColumnIndex(2);
            UiWidgets.DrawDataTableHeaderCell(Loc.T("FISCH", "FISH"), headerHeight, 8f * scale, StartTableHeaderFont);
            ImGui.TableSetColumnIndex(3);
            UiWidgets.DrawDataTableHeaderCell(Loc.T("PREP TIMER", "PREP TIMER"), headerHeight, 8f * scale, StartTableHeaderFont);
            ImGui.TableSetColumnIndex(4);
            UiWidgets.DrawDataTableHeaderCell(Loc.T("FENSTER", "WINDOW"), headerHeight, 8f * scale, StartTableHeaderFont);
            ImGui.TableSetColumnIndex(5);
            UiWidgets.DrawDataTableHeaderCell(Loc.T("RARITÄT", "RARITY"), headerHeight, 8f * scale, StartTableHeaderFont, rightAlign: true);
            ImGui.TableSetColumnIndex(6);
            UiWidgets.DrawDataTableHeaderCell(Loc.T("KÖDER", "BAIT"), headerHeight, 8f * scale, StartTableHeaderFont);
            ImGui.TableSetColumnIndex(7);
            UiWidgets.DrawDataTableHeaderCell(Loc.T("AUTOHOOK", "AUTOHOOK"), headerHeight, 8f * scale, StartTableHeaderFont);
            ImGui.EndTable();
        }
        // X explizit vom vor BeginTable gemerkten linken Rand statt von ImGui.GetCursorScreenPos() hier
        // (Nutzer-Report: Trennlinie nur ganz rechts sichtbar - der Cursor lag nach EndTable() in diesem
        // verschachtelten Child-Kontext nicht zuverlässig am linken Rand).
        var dividerY = ImGui.GetCursorScreenPos().Y;
        ImGui.GetWindowDrawList().AddLine(new Vector2(headerLeft.X, dividerY), new Vector2(headerLeft.X + tableWidth, dividerY), ImGui.GetColorU32(T.LineCard));

        var bodyHeight = ImGui.GetContentRegionAvail().Y;
        ImGui.BeginChild("##OceanStartPlannedBody", new Vector2(innerWidth, bodyHeight), false);

        if (planned.Length == 0)
        {
            ImGui.Dummy(new Vector2(0f, bodyHeight / 2f - 12f * scale));
            var emptyPrefix = Loc.T("Noch keine Fische geplant. Prep-Timer in ", "No fish planned yet. Set a prep timer in ");
            var emptySuffix = Loc.T(" setzen, um einen hinzuzufügen.", " to add one.");
            var emptyLink = Loc.T("Fisch-Daten", "Fish Data");
            float prefixW, linkW, suffixW;
            using (StartPlannedHintFont.Push())
            {
                prefixW = ImGui.CalcTextSize(emptyPrefix).X;
                suffixW = ImGui.CalcTextSize(emptySuffix).X;
            }
            using (StartPlannedHintBoldFont.Push())
                linkW = ImGui.CalcTextSize(emptyLink).X;
            UiWidgetsExtra.CenterNext(prefixW + linkW + suffixW, innerWidth);
            using (StartPlannedHintFont.Push())
                ImGui.TextColored(T.TextMuted, emptyPrefix);
            ImGui.SameLine(0f, 0f);
            using (StartPlannedHintBoldFont.Push())
            {
                if (UiWidgets.LinkText(emptyLink, T.Accent))
                    currentPage = AppPage.BigFish;
            }
            ImGui.SameLine(0f, 0f);
            using (StartPlannedHintFont.Push())
                ImGui.TextColored(T.TextMuted, emptySuffix);
        }
        else if (ImGui.BeginTable("##OceanStartPlannedBodyTable", columnCount, ImGuiTableFlags.None, new Vector2(tableWidth, 0f)))
        {
            StartPlannedSetupColumns(scale);
            var rowHeight = 46f * scale;
            // Kein ImGuiListClipper hier (anders als bei der riesigen Fish-Data-Tabelle mit
            // hunderten Zeilen) - Planned Fish ist immer eine Handvoll Einträge (durch maxVisibleRows
            // ohnehin auf 6 sichtbare Zeilen gedeckelt), Virtualisierung bringt nichts und der Clipper
            // war der Verdacht für die erste-Zeile-zu-hoch-Anomalie (Nutzer-Report: Höhe/Balken beim
            // ersten Eintrag stimmt nicht) in dieser NICHT scrollenden Tabelle.
            for (var i = 0; i < planned.Length; i++)
                DrawStartPlannedRow(scale, planned[i], i, planned.Length, rowHeight, automation, config, itemSheet, territorySheet, spotSheet, presetNames, now, tableWidth, headerLeft.X, headerLeft.X + tableWidth);
            ImGui.EndTable();
        }
        ImGui.EndChild();

        ImGui.EndChild();
        ImGui.PopStyleVar(3);
        ImGui.PopStyleColor(2);
    }

    private static void StartPlannedSetupColumns(float scale)
    {
        ImGui.TableSetupColumn("##OceanStartPos", ImGuiTableColumnFlags.WidthFixed, 18f * scale);
        ImGui.TableSetupColumn("##OceanStartIcon", ImGuiTableColumnFlags.WidthFixed, 28f * scale);
        ImGui.TableSetupColumn("##OceanStartFish", ImGuiTableColumnFlags.WidthStretch, 1.4f);
        ImGui.TableSetupColumn("##OceanStartPrep", ImGuiTableColumnFlags.WidthFixed, 140f * scale);
        ImGui.TableSetupColumn("##OceanStartWindow", ImGuiTableColumnFlags.WidthFixed, 140f * scale);
        ImGui.TableSetupColumn("##OceanStartRarity", ImGuiTableColumnFlags.WidthFixed, 56f * scale);
        ImGui.TableSetupColumn("##OceanStartBait", ImGuiTableColumnFlags.WidthFixed, 80f * scale);
        ImGui.TableSetupColumn("##OceanStartAutoHook", ImGuiTableColumnFlags.WidthStretch, 1.3f);
        ImGui.TableSetupColumn("##OceanStartRemove", ImGuiTableColumnFlags.WidthFixed, 28f * scale);
    }

    private void DrawStartPlannedRow(float scale, (BigFish Fish, FishWindow Window, DateTime FishUtc, float? Rarity) entry, int index, int totalCount, float rowHeight,
        FishingAutomation automation, Configuration config, ExcelSheet<Item> itemSheet, ExcelSheet<TerritoryType> territorySheet, ExcelSheet<FishingSpot> spotSheet,
        IReadOnlyList<string> presetNames, DateTime now, float tableWidth, float tableInnerLeft, float tableInnerRight)
    {
        var T = UiTheme.Active;
        var fish = entry.Fish;
        var isFirst = index == 0;
        var isLast = index == totalCount - 1;

        ImGui.TableNextRow(ImGuiTableRowFlags.None, rowHeight);
        var rowTop = ImGui.GetCursorScreenPos();

        // Erster Fisch optisch 3px höher (Nutzeranforderung: "Zeilenhöhe ... um 3px vergrößern, nach
        // oben hin") - ImGuis natives TableNextRow kann eine Zeile nur nach UNTEN wachsen lassen, daher
        // hier komplett manuell: Inhalt/Hover-Fläche nutzen eine um topShift nach oben verschobene,
        // effektive Boxhöhe statt der nativen Zeilenhöhe (die native Spalten-/X-Aufteilung bleibt
        // unberührt, nur Y-Positionierung/Zentrierung pro Zelle).
        var topShift = isFirst ? 3f * scale : 0f;
        var effRowHeight = rowHeight + topShift;
        var rowTopEff = new Vector2(rowTop.X, rowTop.Y - topShift);

        // Kein breiter RowBg0-Farbwasch mehr für den ersten Fisch (Nutzer-Report: die farbliche
        // Markierung soll nur so hoch sein wie der Accent-Balken ganz links - ImGuis natives RowBg0
        // wächst aber automatisch auf die tatsächliche Zeilenhöhe, die bei größeren Inhalten höher sein
        // kann als der fest auf "rowHeight" gezeichnete Balken). Der 2px-Balken (siehe Zeilenende) bleibt
        // als alleinige Markierung.
        // Eigenes PushClipRect/PopClipRect (wie beim Balken/der Trennlinie) - ohne das kann ein von der
        // VORHERIGEN Zeile übrig gebliebenes schmales Spalten-Clip-Rect (letzter TableSetColumnIndex
        // dort) den Hover-Hintergrund dieser Zeile beschneiden, bevor hier überhaupt die erste eigene
        // Spalte gesetzt wird (Nutzer-Report: Hover-Fläche beim ersten Eintrag kleiner als der Balken).
        var rowHoverMin = new Vector2(tableInnerLeft, rowTopEff.Y);
        var rowHoverMax = new Vector2(tableInnerRight, rowTopEff.Y + effRowHeight);
        if (ImGui.IsMouseHoveringRect(rowHoverMin, rowHoverMax))
        {
            var hoverDl = ImGui.GetWindowDrawList();
            hoverDl.PushClipRect(rowHoverMin, rowHoverMax, false);
            hoverDl.AddRectFilled(rowHoverMin, rowHoverMax, ImGui.GetColorU32(T.BgSelected with { W = 0.4f }));
            hoverDl.PopClipRect();
        }

        ImGui.TableSetColumnIndex(0);
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() - topShift);
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 6f * scale);
        using (StartRowPositionFont.Push())
        {
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + (effRowHeight - ImGui.GetTextLineHeight()) / 2f);
            ImGui.TextColored(isFirst ? T.Accent : T.TextMuted, (index + 1).ToString(CultureInfo.InvariantCulture));
        }

        ImGui.TableSetColumnIndex(1);
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() - topShift);
        var iconDiameter = 28f * scale;
        var iconCellStart = ImGui.GetCursorScreenPos();
        var iconCenter = iconCellStart + new Vector2(iconDiameter / 2f, effRowHeight / 2f);
        var icon = itemSheet.TryGetRow(fish.ItemId, out var fishItem) ? fishItem.Icon : 0u;
        var wrap = icon != 0 ? GetFishDataIcon(icon).GetWrapOrEmpty() : null;
        UiWidgetsExtra.RoundIcon(iconCenter, iconDiameter / 2f, T.BgInput, T.LineFrame, wrap?.Handle);
        ImGui.Dummy(new Vector2(iconDiameter, effRowHeight));

        ImGui.TableSetColumnIndex(2);
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() - topShift);
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 8f * scale);
        var name = FishDataDisplayName(fish, itemSheet);
        var area = GetStartFishAreaText(fish, territorySheet, spotSheet);
        var atCastablePosition = automation.IsAtCastablePosition(fish);
        UiWidgetsExtra.TwoLineCell(name, StartRowFishFont, atCastablePosition ? T.Accent : T.TextHeading, area, StartRowAreaFont, T.TextMuted, effRowHeight);

        ImGui.TableSetColumnIndex(3);
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() - topShift);
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 8f * scale);
        using (StartRowValueFont.Push())
        {
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + (effRowHeight - ImGui.GetTextLineHeight()) / 2f);
            ImGui.TextColored(isFirst ? T.TextHeading : FishDataTextButton, FormatStartCountdownValue(entry.FishUtc, entry.Window, now, isPrep: true));
        }

        ImGui.TableSetColumnIndex(4);
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() - topShift);
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 8f * scale);
        using (StartRowValueFont.Push())
        {
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + (effRowHeight - ImGui.GetTextLineHeight()) / 2f);
            ImGui.TextColored(isFirst ? T.TextHeading : FishDataTextButton, FormatStartCountdownValue(entry.FishUtc, entry.Window, now, isPrep: false));
        }

        ImGui.TableSetColumnIndex(5);
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() - topShift);
        var rarityText = entry.Rarity is { } rarity ? $"{rarity * 100f:0.#}%" : Loc.T("Unbekannt", "Unknown");
        var rarityColor = entry.Rarity is { } r2 && r2 < 0.01f ? FishDataRarityYellow : T.TextSecondary;
        using (StartRowRegularFont.Push())
        {
            var textWidth = ImGui.CalcTextSize(rarityText).X;
            ImGui.SetCursorPosX(ImGui.GetContentRegionAvail().X + ImGui.GetCursorPosX() - textWidth - 8f * scale);
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + (effRowHeight - ImGui.GetTextLineHeight()) / 2f);
            ImGui.TextColored(rarityColor, rarityText);
        }

        ImGui.TableSetColumnIndex(6);
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() - topShift);
        DrawStartBaitCell(scale, fish, itemSheet, effRowHeight);

        ImGui.TableSetColumnIndex(7);
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() - topShift);
        DrawFishDataAutoHookCell(scale, fish, config, presetNames, effRowHeight, allowNoPreset: false);

        ImGui.TableSetColumnIndex(8);
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() - topShift);
        var removeSize = 28f * scale;
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + (effRowHeight - removeSize) / 2f);
        if (UiNav.IconButton(UiIcon.Trash, $"##OceanStartRemove_{fish.ItemId}", new Vector2(removeSize, removeSize),
                tooltip: Loc.T("Aus Plan entfernen", "Remove from plan"), hoverBg: FishDataCoralRed with { W = 0.1f }, hoverIconColor: FishDataCoralRed))
        {
            config.EnabledFish.Remove(fish.ItemId);
            config.Save();
            startUndoToast = (name, now + TimeSpan.FromSeconds(5), fish.ItemId);
            InvalidateStartPlannedCache();
        }

        // Generischer UIWidget-Baustein statt Einzelberechnung (Nutzer-Report: der Balken begann nicht
        // bündig an der Header-Unterkante - Ursache war die Positionsermittlung über die separate
        // Kopf-Trennlinie statt über die tatsächliche Zeile selbst). "rowTopEff"/"effRowHeight" sind
        // bereits die Quelle der Wahrheit für Hover-Fläche/Zellzentrierung dieser Zeile (siehe oben),
        // der Balken nutzt jetzt exakt dieselben Werte.
        if (isFirst)
            UiWidgets.DrawHighlightedRow(rowTopEff.Y, effRowHeight, tableInnerLeft, tableInnerRight, T.Accent with { W = 0.06f }, T.Accent, 2f * scale);

        // Eigenes PushClipRect/PopClipRect (wie UiWidgets.DrawHighlightedRow) - ImGui setzt beim
        // letzten TableSetColumnIndex(8) automatisch dessen (schmales) Spalten-Clip-Rect als aktives
        // Scissor-Rechteck; ohne eigenen Clip-Override wird die über die volle Breite gezeichnete Linie
        // dadurch auf nur diese letzte Spalte beschnitten (Nutzer-Report: Linie nur ganz rechts
        // sichtbar - dasselbe Muster, das vorher schon beim Balken/der Kopf-Trennlinie auftrat).
        if (!isLast)
        {
            var dl = ImGui.GetWindowDrawList();
            var lineY = rowTop.Y + rowHeight;
            dl.PushClipRect(new Vector2(tableInnerLeft, lineY - 1f), new Vector2(tableInnerRight, lineY + 1f), false);
            dl.AddLine(new Vector2(tableInnerLeft, lineY), new Vector2(tableInnerRight, lineY), ImGui.GetColorU32(T.LineRow));
            dl.PopClipRect();
        }
    }

    private void DrawStartBaitCell(float scale, BigFish fish, ExcelSheet<Item> itemSheet, float rowHeight)
    {
        var T = UiTheme.Active;
        var cellStart = ImGui.GetCursorScreenPos();
        var iconSize = 24f * scale;
        var dl = ImGui.GetWindowDrawList();

        var baitId = fish.BaitIds.FirstOrDefault();
        if (baitId == 0 || !itemSheet.TryGetRow(baitId, out var bait))
        {
            ImGui.Dummy(new Vector2(0f, rowHeight));
            return;
        }

        var count = GameActions.GetInventoryItemCount(baitId);
        var dimmed = count == 0;
        var y = cellStart.Y + (rowHeight - iconSize) / 2f;

        dl.AddRect(new Vector2(cellStart.X, y), new Vector2(cellStart.X + iconSize, y + iconSize), ImGui.GetColorU32(T.LineFrame), 3f * scale);
        if (bait.Icon != 0)
        {
            var wrap = GetFishDataIcon(bait.Icon).GetWrapOrEmpty();
            dl.AddImageRounded(wrap.Handle, new Vector2(cellStart.X, y), new Vector2(cellStart.X + iconSize, y + iconSize), Vector2.Zero, Vector2.One,
                ImGui.GetColorU32(new Vector4(1f, 1f, 1f, dimmed ? 0.55f : 1f)), 3f * scale);
        }

        ImGui.InvisibleButton($"##OceanStartBait_{fish.ItemId}_{baitId}", new Vector2(iconSize, rowHeight));
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip($"{bait.Name}\n{Loc.T("Bestand", "Stock")}: {count}");

        var countText = count.ToString(CultureInfo.InvariantCulture);
        var countX = cellStart.X + iconSize + 4f * scale;
        float countHeight;
        using (StartRowBaitCountFont.Push())
            countHeight = ImGui.GetTextLineHeight();
        using (StartRowBaitCountFont.Push())
            dl.AddText(new Vector2(countX, cellStart.Y + (rowHeight - countHeight) / 2f), ImGui.GetColorU32(dimmed ? FishDataCoralRed : T.TextMuted), countText);
    }

    private void DrawStartUndoToast(float scale, string fishName, uint fishItemId)
    {
        var T = UiTheme.Active;
        var text = Loc.T($"{fishName} entfernt · ", $"{fishName} removed · ");
        var undoLabel = Loc.T("Rückgängig", "Undo");

        float textWidth, textHeight, undoWidth;
        using (StartPlannedHintFont.Push())
        {
            var size = ImGui.CalcTextSize(text);
            textWidth = size.X;
            textHeight = size.Y;
        }
        using (StartPlannedHintBoldFont.Push())
            undoWidth = ImGui.CalcTextSize(undoLabel).X;

        var padX = 12f * scale;
        var padY = 8f * scale;
        var size2 = new Vector2(padX * 2f + textWidth + undoWidth, padY * 2f + textHeight);
        var cursor = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(cursor, cursor + size2, ImGui.GetColorU32(T.BgPopup), UiMetrics.CardRadius * scale);
        dl.AddRect(cursor, cursor + size2, ImGui.GetColorU32(T.LineCard), UiMetrics.CardRadius * scale);

        using (StartPlannedHintFont.Push())
            dl.AddText(cursor + new Vector2(padX, padY), ImGui.GetColorU32(T.TextSecondary), text);

        ImGui.SetCursorScreenPos(cursor + new Vector2(padX + textWidth, padY));
        using (StartPlannedHintBoldFont.Push())
        {
            if (UiWidgets.LinkText(undoLabel, T.Accent))
            {
                var fish = BigFishData.All.FirstOrDefault(f => f.ItemId == fishItemId);
                if (fish != null)
                {
                    plugin.Configuration.EnabledFish.Add(fishItemId);
                    plugin.Configuration.Save();
                    InvalidateStartPlannedCache();
                }
                startUndoToast = null;
            }
        }

        ImGui.SetCursorScreenPos(cursor);
        ImGui.Dummy(size2);
    }

    /// <summary>
    /// Seite "Allgemein" - 1:1 derselbe Aufbau wie CodexMenuWindow.DrawGeneralPage (zwei Spalten per
    /// BeginChild, links zwei gestapelte Karten, rechts eine) - dieselben Configuration-Felder wie
    /// zuvor, nur über PluginUiKit.UiWidgets (CardGroupLabel/ToggleRow/BeginDropdownRow) im Ocean-
    /// Stil statt ModernUi gezeichnet.
    /// </summary>
    private void DrawGeneralPage(float scale)
    {
        var config = plugin.Configuration;
        var availWidth = ImGui.GetContentRegionAvail().X;
        var gap = 32f * scale;
        var rightMargin = 32f * scale;
        var columnWidth = (availWidth - gap - rightMargin) / 2f;

        ImGui.BeginChild("##OceanGeneralLeft", new Vector2(columnWidth, 0f), false);
        DrawLanguageCard(scale, config);
        ImGui.Dummy(new Vector2(0f, 16f * scale));
        DrawFishingCard(scale, config);
        ImGui.EndChild();

        ImGui.SameLine(0f, gap);

        ImGui.BeginChild("##OceanGeneralRight", new Vector2(columnWidth, 0f), false);
        DrawTravelCard(scale, config);
        ImGui.EndChild();
    }

    /// <summary>Karte "Sprache" - 1:1 wie CodexMenuWindow.DrawLanguageCard (Menüsprache-Dropdown, betrifft nur dieses Menü, nicht das Status-Overlay).</summary>
    private static void DrawLanguageCard(float scale, Configuration config)
    {
        UiWidgets.BeginCard(scale);
        UiWidgets.CardGroupLabel(Loc.T("SPRACHE", "LANGUAGE"), scale, UiWidgets.CardFieldLineWidth(scale));

        var languageLabels = new (MenuLanguage Language, string Label)[]
        {
            (MenuLanguage.German, "Deutsch"),
            (MenuLanguage.English, "English"),
        };
        var currentLanguageLabel = languageLabels.First(l => l.Language == config.MenuLanguage).Label;

        var open = UiWidgets.BeginDropdownRow(Loc.T("Menüsprache", "Menu language"), currentLanguageLabel, "##OceanMenuLanguage", scale,
            caption: Loc.T("Betrifft nur das Menü, nicht das Overlay.", "Only affects the menu, not the overlay."),
            width: DropdownWidth, rightMargin: DropdownRightMargin,
            labelFont: FieldLabelFont, captionFont: DropdownCaptionFont, valueFont: DropdownValueFont);
        if (open)
        {
            foreach (var (language, label) in languageLabels)
            {
                if (ImGui.Selectable(label, config.MenuLanguage == language))
                {
                    config.MenuLanguage = language;
                    config.Save();
                }
            }
        }
        UiWidgets.EndDropdownRow(open, disabled: false);

        UiWidgets.EndCard();
    }

    private void DrawFishingCard(float scale, Configuration config)
    {
        UiWidgets.BeginCard(scale);
        UiWidgets.CardGroupLabel(Loc.T("ANGELN", "FISHING"), scale, UiWidgets.CardFieldLineWidth(scale));

        if (config.FisherGearsetIndex < 0)
        {
            var defaultIndex = GameActions.FindDefaultFisherGearsetIndex();
            if (defaultIndex >= 0)
            {
                config.FisherGearsetIndex = defaultIndex;
                config.Save();
            }
        }

        var gearsets = GameActions.GetGearsets();
        var currentGearsetName = config.FisherGearsetIndex >= 0 ? GameActions.GetGearsetName(config.FisherGearsetIndex) : null;
        var gearsetLabel = currentGearsetName ?? Loc.T("Kein Preset ausgewählt", "No preset selected");
        var invalidGearset = currentGearsetName == null || !GameActions.IsGearsetFisher(config.FisherGearsetIndex);

        if (invalidGearset)
            ImGui.PushStyleColor(ImGuiCol.Text, NotCaughtColor);
        var gearsetOpen = UiWidgets.BeginDropdownRow(Loc.T("Fischer Preset", "Fisher preset"), gearsetLabel, "##OceanFisherGearset", scale,
            caption: Loc.T(
                "Ausrüstungsset für den ersten Wechsel, vor jedem Teleport.",
                "Gear set the automation switches to first."),
            width: DropdownWidth, rightMargin: DropdownRightMargin,
            labelFont: FieldLabelFont, captionFont: DropdownCaptionFont, valueFont: DropdownValueFont);
        if (invalidGearset)
            ImGui.PopStyleColor();
        if (gearsetOpen)
        {
            ImGui.SetNextItemWidth(-1f);
            ImGui.InputTextWithHint("##OceanGearsetFilter", Loc.T("Gear Sets durchsuchen...", "Search gear sets..."), ref gearsetFilter, 100);

            ImGui.BeginChild("##OceanGearsetList", new Vector2(0f, 200f * scale));
            foreach (var (index, name, classJobAbbreviation) in gearsets)
            {
                var entryLabel = $"{name} ({classJobAbbreviation})";
                if (!string.IsNullOrWhiteSpace(gearsetFilter) && !entryLabel.Contains(gearsetFilter, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (ImGui.Selectable($"{entryLabel}##oceanGearset_{index}", config.FisherGearsetIndex == index))
                {
                    config.FisherGearsetIndex = index;
                    config.Save();
                }
            }
            ImGui.EndChild();
        }
        UiWidgets.EndDropdownRow(gearsetOpen, disabled: false);

        UiWidgets.CardDivider(scale);
        var desynthesisAfterFishing = config.DesynthesisAfterFishing;
        if (UiWidgets.ToggleRow("##OceanDesynthesisAfterFishing", Loc.T("Desynthesis nach dem Angeln", "Desynthesis after fishing"), ref desynthesisAfterFishing, scale,
                caption: Loc.T(
                    "Nur wenn in 10 Minuten kein Prep Timer beginnt.",
                    "Only if no prep timer begins within 10 minutes."),
                labelFont: FieldLabelFont, captionFont: DropdownCaptionFont))
        {
            config.DesynthesisAfterFishing = desynthesisAfterFishing;
            config.Save();
        }

        UiWidgets.CardDivider(scale);
        if (!desynthesisAfterFishing)
            ImGui.BeginDisabled();
        var desynthesisIgnoreBigFish = config.DesynthesisIgnoreBigFish;
        if (UiWidgets.ToggleRow("##OceanDesynthesisIgnoreBigFish", Loc.T("Big Fish ignorieren", "Ignore Big Fish"), ref desynthesisIgnoreBigFish, scale,
                caption: Loc.T("Desynthese von allen Fischen mit Ausnahme von Big Fish.", "Desynthesize all fish except Big Fish."),
                labelFont: FieldLabelFont, captionFont: DropdownCaptionFont))
        {
            config.DesynthesisIgnoreBigFish = desynthesisIgnoreBigFish;
            config.Save();
        }
        if (!desynthesisAfterFishing)
            ImGui.EndDisabled();

        UiWidgets.EndCard();
    }

    private void DrawTravelCard(float scale, Configuration config)
    {
        UiWidgets.BeginCard(scale);
        UiWidgets.CardGroupLabel(Loc.T("ANFLUG", "TRAVEL"), scale, UiWidgets.CardFieldLineWidth(scale));

        var rouletteLabel = Loc.T("Mount Roulette", "Mount Roulette");
        var currentMountLabel = config.FlyingMountId == 0 ? rouletteLabel : GameActions.MountName(config.FlyingMountId);
        var flyingMountOpen = UiWidgets.BeginDropdownRow(Loc.T("Mount zum Fliegen", "Mount for flying"), currentMountLabel, "##OceanFlyingMount", scale,
            caption: Loc.T("Mount für den Anflug zur Angel-Position.", "Mount for flying to the fishing spot."),
            width: DropdownWidth, rightMargin: DropdownRightMargin,
            labelFont: FieldLabelFont, captionFont: DropdownCaptionFont, valueFont: DropdownValueFont);
        if (flyingMountOpen)
        {
            if (ImGui.Selectable(rouletteLabel, config.FlyingMountId == 0))
            {
                config.FlyingMountId = 0;
                config.Save();
            }

            ImGui.Separator();
            ImGui.SetNextItemWidth(-1f);
            ImGui.InputTextWithHint("##OceanMountFilter", Loc.T("Mounts durchsuchen...", "Search mounts..."), ref mountFilter, 100);

            ImGui.BeginChild("##OceanMountList", new Vector2(0f, 200f * scale));
            foreach (var (id, name) in GameActions.GetUnlockedMounts())
            {
                if (!string.IsNullOrWhiteSpace(mountFilter) && !name.Contains(mountFilter, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (ImGui.Selectable($"{name}##oceanMount_{id}", config.FlyingMountId == id))
                {
                    config.FlyingMountId = id;
                    config.Save();
                }
            }
            ImGui.EndChild();
        }
        UiWidgets.EndDropdownRow(flyingMountOpen, disabled: false);

        UiWidgets.CardDivider(scale);
        var useSprintInCities = config.UseSprintInCities;
        if (UiWidgets.ToggleRow("##OceanUseSprintInCities", Loc.T("Sprint in Städten nutzen", "Use Sprint in cities"), ref useSprintInCities, scale,
                caption: Loc.T(
                    "Sprint beim Laufen in Städten, sobald verfügbar.",
                    "Sprint while walking in cities, whenever available."),
                labelFont: FieldLabelFont, captionFont: DropdownCaptionFont))
        {
            config.UseSprintInCities = useSprintInCities;
            config.Save();
        }

        UiWidgets.EndCard();
    }

    // ---- Seite "Plugins" - 1:1 derselbe Aufbau wie CodexMenuWindow.DrawPluginsPage (Status-Banner +
    // gruppierte Karten), ohne die Codex-spezifische "ein Kampf-Plugin von mehreren"-Gruppe, die es
    // bei Big Fish Helper nicht gibt - reine Required/Optional-Gruppierung wie Dependencies. ----

    // Rechter Randabstand der Plugins-Seite - identisch zu General (siehe DrawGeneralPage.rightMargin)
    // und CodexMenuWindow.DrawPluginsPage.PluginsPageRightMargin (beide 32px).
    private const float PluginsPageRightMargin = 32f;

    // Aus dem gelöschten alten Menü (MainWindow.cs) hierher übernommen - das alte Menü ist mit der
    // Abnahme des neuen Ocean-UI entfallen (Nutzeranforderung "altes Menü löschen"), diese Hilfen
    // werden aber weiterhin gebraucht (Plugins-Seite hier, Fish-Data-/Start-Tabellen, Status-Overlay).
    internal static string MissingPluginText => Loc.T("Ein benötigtes Plugin fehlt (siehe Plugins).", "A required plugin is missing (see Plugins).");

    internal static readonly (string InternalName, string DisplayName, string DescriptionDe, string DescriptionEn, bool Required)[] Dependencies =
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

    internal static bool IsPluginLoaded(string internalName) =>
        Plugin.PluginInterface.InstalledPlugins.Any(p => p.InternalName == internalName && p.IsLoaded);

    /// <summary>Dauer als Countdown: "hh:mm:ss", ab einem Tag mit vorangestellten Tagen.</summary>
    internal static string FormatCountdown(TimeSpan span)
    {
        if (span < TimeSpan.Zero)
            span = TimeSpan.Zero;

        var clock = $"{span.Hours:00}:{span.Minutes:00}:{span.Seconds:00}";
        return span.TotalDays >= 1 ? Loc.T($"{(int)span.TotalDays} T {clock}", $"{(int)span.TotalDays}d {clock}") : clock;
    }

    private void DrawPluginsPage(float scale)
    {
        var width = ImGui.GetContentRegionAvail().X - PluginsPageRightMargin * scale;
        DrawPluginsStatusBanner(scale, width);
        ImGui.Dummy(new Vector2(0f, 18f * scale));

        var required = Dependencies.Where(d => d.Required).ToArray();
        var optional = Dependencies.Where(d => !d.Required).ToArray();

        DrawPluginGroup(scale, Loc.T("ERFORDERLICH", "REQUIRED"), required, width);
        if (optional.Length > 0)
        {
            ImGui.Dummy(new Vector2(0f, 18f * scale));
            DrawPluginGroup(scale, Loc.T("OPTIONAL", "OPTIONAL"), optional, width);
        }
    }

    private static void DrawPluginsStatusBanner(float scale, float width)
    {
        var T = UiTheme.Active;
        var total = Dependencies.Length;
        var installedCount = Dependencies.Count(d => IsPluginLoaded(d.InternalName));
        var ready = !HasMissingRequiredDependency();

        var bg = ready ? T.OkBg : T.WarnBg;
        var border = ready ? T.OkLine : T.WarnLine;
        var fg = ready ? T.OkFg : T.WarnFg;

        string boldPart, restPart;
        if (ready)
        {
            boldPart = Loc.T("Alle Voraussetzungen erfüllt.", "All requirements met.");
            restPart = Loc.T($"{installedCount} von {total} Plugins installiert.", $"{installedCount} of {total} plugins installed.");
        }
        else
        {
            var missingNames = Dependencies
                .Where(d => d.Required && !IsPluginLoaded(d.InternalName))
                .Select(d => d.DisplayName);
            boldPart = Loc.T($"Fehlt: {string.Join(", ", missingNames)}.", $"Missing: {string.Join(", ", missingNames)}.");
            restPart = Loc.T("Automation ist deaktiviert, bis es installiert ist.", "Automation is disabled until it is installed.");
        }

        var paddingX = 16f * scale;
        var paddingY = 12f * scale;
        float textHeight;
        using (FieldLabelFont.Push())
            textHeight = ImGui.GetFontSize();
        var iconSize = 18f * scale;
        var rowHeight = MathF.Max(iconSize, textHeight) + paddingY * 2f;

        var cursor = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(cursor, cursor + new Vector2(width, rowHeight), ImGui.GetColorU32(bg), UiMetrics.CardRadius * scale);
        dl.AddRect(cursor, cursor + new Vector2(width, rowHeight), ImGui.GetColorU32(border), UiMetrics.CardRadius * scale);

        // Gefüllter Kreis (fg-Farbe) mit Haken/Kreuz in BgWindow darin - 1:1 wie CodexMenuWindow's
        // CheckCircle/ExclamationTriangle-Icon, nur von Hand gezeichnet statt FontAwesome-Glyph.
        var iconCenter = cursor + new Vector2(paddingX + iconSize / 2f, rowHeight / 2f);
        dl.AddCircleFilled(iconCenter, iconSize / 2f, ImGui.GetColorU32(fg), 24);
        var glyphSize = iconSize * 0.55f;
        UiIcons.Draw(ready ? UiIcon.Check : UiIcon.Cross, iconCenter - new Vector2(glyphSize / 2f), glyphSize, ImGui.GetColorU32(T.BgWindow));

        ImGui.SetCursorScreenPos(cursor + new Vector2(paddingX + iconSize + 10f * scale, (rowHeight - textHeight) / 2f));
        using (FieldLabelFont.Push())
            ImGui.TextColored(fg, boldPart);
        ImGui.SameLine(0f, 8f * scale);
        using (DropdownCaptionFont.Push())
            ImGui.TextColored(T.TextPrimary, restPart);

        ImGui.SetCursorScreenPos(cursor + new Vector2(0f, rowHeight));
    }

    private static void DrawPluginGroup(float scale, string title, (string InternalName, string DisplayName, string DescriptionDe, string DescriptionEn, bool Required)[] entries, float width)
    {
        var T = UiTheme.Active;
        var installedCount = entries.Count(e => IsPluginLoaded(e.InternalName));
        var allInstalled = installedCount == entries.Length;

        var titleStartY = ImGui.GetCursorPosY();
        float titleHeight;
        using (UiFonts.CardTitle.Push())
        {
            titleHeight = ImGui.GetFontSize();
            ImGui.TextColored(T.TextCardTitle, title);
        }

        // Zählungs-Badge rechts neben dem Titel ("5 / 5") - 1:1 wie CodexMenuWindow.DrawPluginGroupHeader.
        ImGui.SameLine(0f, 10f * scale);
        var badgeText = $"{installedCount} / {entries.Length}";
        var badgeFg = allInstalled ? T.TextCardTitle : T.WarnFg;
        var badgeBorder = allInstalled ? T.LineFrame : T.WarnLine;
        var badgeBg = allInstalled ? T.BgSelected : T.WarnBg;
        float badgeHeight;
        using (DropdownCaptionFont.Push())
            badgeHeight = ImGui.GetTextLineHeight() + 2f * scale * 2f;
        ImGui.SetCursorPosY(titleStartY + (titleHeight - badgeHeight) / 2f);
        UiWidgetsExtra.Badge(badgeText, badgeFg, badgeBg, badgeBorder, scale, DropdownCaptionFont);

        ImGui.SetCursorPosY(titleStartY + titleHeight);
        ImGui.Dummy(new Vector2(0f, 10f * scale));

        // Feste Außenbreite über eine 1-Spalten-Tabelle (1:1 wie CodexMenuWindow.DrawRequiredPluginsGroup/
        // DrawOptionalPluginsGroup) - DrawPluginRow richtet sein rechtsbündiges Steuerelement über
        // ImGui.GetContentRegionAvail() aus, das ohne diese Tabelle die VOLLE Fensterbreite sähe statt
        // der schmaleren, um PluginsPageRightMargin reduzierten Kartenbreite (width).
        if (ImGui.BeginTable($"##OceanPluginsGroupWidth_{title}", 1, ImGuiTableFlags.None, new Vector2(width, 0f)))
        {
            ImGui.TableNextColumn();
            UiWidgets.BeginCard(scale);
            for (var i = 0; i < entries.Length; i++)
            {
                var entry = entries[i];
                DrawPluginRow(scale, entry.DisplayName, Loc.T(entry.DescriptionDe, entry.DescriptionEn), IsPluginLoaded(entry.InternalName));
                if (i < entries.Length - 1)
                    UiWidgets.CardDivider(scale);
            }
            UiWidgets.EndCard();
            ImGui.EndTable();
        }
    }

    /// <summary>Eine Abhängigkeit als Zeile - Status-Kreis + Name+Beschreibung links, rechts ein Installiert-Haken bzw. "Installieren"-Knopf
    /// (öffnet den Dalamud-Plugin-Installer) - 1:1 wie CodexMenuWindow's DrawPluginRow, nur mit von Hand gezeichnetem Status-Kreis statt
    /// FontAwesome-Glyph. "drawDivider: false" (siehe UiWidgets.BeginSettingRow) - die Trennung zwischen Zeilen kommt bereits von der
    /// UiWidgets.CardDivider zwischen den Aufrufen (siehe DrawPluginGroup), eine zweite eigene Trennlinie würde den Zeilenabstand verdoppeln.</summary>
    /// <summary>1:1 CodexMenuWindow.DrawPluginRow (dieselben Schriftgrößen/Zeilenhöhen-Formel/Abstände), nur mit von Hand gezeichneten Icons statt FontAwesome.</summary>
    private static void DrawPluginRow(float scale, string name, string description, bool installed)
    {
        var T = UiTheme.Active;
        var rowStartY = ImGui.GetCursorPosY();
        var rowStartX = ImGui.GetCursorPosX();
        var rightEdge = rowStartX + ImGui.GetContentRegionAvail().X;

        var dotSize = 26f * scale;
        float nameHeight, descHeight;
        using (PluginNameFont.Push())
            nameHeight = ImGui.GetFontSize();
        using (PluginDescriptionFont.Push())
            descHeight = ImGui.GetFontSize();
        var textBlockHeight = nameHeight + descHeight;

        var verticalPadding = 3f * scale;
        var height = MathF.Max(dotSize, textBlockHeight) + verticalPadding * 2f - 17f * scale;

        ImGui.SetCursorPosY(rowStartY + (height - dotSize) / 2f);
        DrawPluginStatusDot(scale, installed);

        ImGui.SameLine(0f, 12f * scale);
        var textX = ImGui.GetCursorPosX();

        ImGui.SetCursorPosY(rowStartY + (height - textBlockHeight) / 2f);
        using (PluginNameFont.Push())
            ImGui.TextColored(T.TextPrimary, name);
        ImGui.SetCursorPosX(textX);
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() - 14f * scale);
        using (PluginDescriptionFont.Push())
            ImGui.TextColored(T.TextMuted, description);

        if (installed)
        {
            var label = Loc.T("Installiert", "Installed");
            float labelWidth, textHeight;
            using (PluginInstalledLabelFont.Push())
            {
                var size = ImGui.CalcTextSize(label);
                labelWidth = size.X;
                textHeight = size.Y;
            }
            var checkSize = textHeight;
            var gap = 5f * scale;
            var rightX = rightEdge - checkSize - gap - labelWidth - 20f * scale;
            var checkPos = new Vector2(rightX, rowStartY + (height - checkSize) / 2f);
            ImGui.SetCursorPos(checkPos);
            UiIcons.Draw(UiIcon.Check, ImGui.GetCursorScreenPos(), checkSize, ImGui.GetColorU32(T.OkFg));

            ImGui.SetCursorPos(new Vector2(rightX + checkSize + gap, rowStartY + (height - textHeight) / 2f));
            using (PluginInstalledLabelFont.Push())
                ImGui.TextColored(T.OkFg, label);
        }
        else
        {
            var buttonSize = MeasureInstallButton(scale);
            var rightX = rightEdge - buttonSize.X - 20f * scale;
            ImGui.SetCursorPos(new Vector2(rightX, rowStartY + (height - buttonSize.Y) / 2f));
            if (DrawInstallButton(scale, $"##OceanPluginInstall{name}", buttonSize))
                Plugin.PluginInterface.OpenPluginInstallerTo(PluginInstallerOpenKind.AllPlugins, name);
        }

        ImGui.SetCursorPosY(rowStartY + height);
    }

    private static void DrawPluginStatusDot(float scale, bool installed)
    {
        var T = UiTheme.Active;
        var size = new Vector2(26f * scale, 26f * scale);
        var cursor = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();

        var bg = installed ? T.OkBg : T.LineRow;
        var border = installed ? T.OkLine : T.LineControl;
        var fg = installed ? T.OkFg : T.TextMuted;

        dl.AddCircleFilled(cursor + size / 2f, size.X / 2f, ImGui.GetColorU32(bg));
        dl.AddCircle(cursor + size / 2f, size.X / 2f, ImGui.GetColorU32(border));

        var glyphSize = 15f * scale;
        UiIcons.Draw(installed ? UiIcon.Check : UiIcon.Cross, cursor + (size - new Vector2(glyphSize)) / 2f, glyphSize, ImGui.GetColorU32(fg));

        ImGui.Dummy(size);
    }

    private static Vector2 MeasureInstallButton(float scale)
    {
        var label = Loc.T("Installieren", "Install");
        var padding = new Vector2(12f * scale, 5f * scale);

        float labelWidth, textHeight;
        using (InstallButtonLabelFont.Push())
        {
            var size = ImGui.CalcTextSize(label);
            labelWidth = size.X;
            textHeight = size.Y;
        }

        return new Vector2(padding.X * 2f + labelWidth, padding.Y * 2f + textHeight);
    }

    /// <summary>Install-Knopf AM AKTUELLEN Cursor (vom Aufrufer vorher per SetCursorPos positioniert) - 1:1 CodexMenuWindow.DrawInstallButton, nur ohne das Download-Icon (keine Icon-Schriftart im Ocean-Look).</summary>
    private static bool DrawInstallButton(float scale, string id, Vector2 buttonSize)
    {
        var T = UiTheme.Active;
        var label = Loc.T("Installieren", "Install");

        var cursor = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton(id, buttonSize);
        var hovered = ImGui.IsItemHovered();

        var dl = ImGui.GetWindowDrawList();
        var bg = hovered ? T.Accent with { W = 0.85f } : T.Accent;
        dl.AddRectFilled(cursor, cursor + buttonSize, ImGui.GetColorU32(bg), UiMetrics.ControlRadius * scale);

        using (InstallButtonLabelFont.Push())
        {
            var textSize = ImGui.CalcTextSize(label);
            dl.AddText(cursor + (buttonSize - textSize) / 2f, ImGui.GetColorU32(T.TextOnAccent), label);
        }

        if (hovered)
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        return clicked;
    }

    // ---- Seite "Debug" (nur Dev-Version) - 1:1 derselbe Aufbau wie CodexMenuWindow.DrawDebugPage
    // (Schriftgrößen/Abstände/rechter Randabstand identisch), ohne dessen "Allgemein"-Karte (kein
    // Gegenstück zu config.ShowDebugInfo bei Big Fish Helper) - "Simulate desynthesis"/"Stopp" liegen
    // in der Simulation-Box, "Log fish list" in der Debug-Dumps-Box (Nutzeranforderung). ----

    private long debugCopyConfirmedUntil;

    private void DrawDebugPage(float scale)
    {
        var rightMargin = 32f * scale;
        var width = ImGui.GetContentRegionAvail().X - rightMargin;

        DrawDebugStatusCard(scale, width);

        ImGui.Dummy(new Vector2(0f, 16f * scale));
        DrawDebugSimulationCard(scale, width);

        ImGui.Dummy(new Vector2(0f, 16f * scale));
        DrawDebugDumpsCard(scale, width);
    }

    private void DrawDebugStatusCard(float scale, float width)
    {
        var T = UiTheme.Active;
        UiWidgets.BeginCard(scale, width: width);
        var contentWidth = width - UiWidgets.CardPaddingX * 2f;

        var titleRowY = ImGui.GetCursorPosY();
        var title = Loc.T("Aktueller Status", "Current Status");
        float titleHeight;
        using (DebugCardTitleFont.Push())
        {
            titleHeight = ImGui.CalcTextSize(title).Y;
            ImGui.TextColored(T.TextCardTitle, title);
        }
        DrawDebugCopyButton(scale, titleRowY, titleHeight, contentWidth);

        ImGui.Dummy(new Vector2(0f, 6f * scale));
        var lineCursor = ImGui.GetCursorScreenPos();
        ImGui.GetWindowDrawList().AddLine(lineCursor, lineCursor + new Vector2(contentWidth, 0f), ImGui.GetColorU32(T.LineSubtle));
        ImGui.Dummy(new Vector2(0f, 8f * scale));

        const float labelColumnWidth = 96f;

        var territoryId = Plugin.ClientState.TerritoryType;
        var territorySheet = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.TerritoryType>();
        var zoneName = territorySheet.TryGetRow(territoryId, out var territory)
            ? territory.PlaceName.ValueNullable?.Name.ToString() ?? "?"
            : "?";
        DrawDebugStatusLabel(scale, labelColumnWidth, Loc.T("Zone", "Zone"));
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 30f * scale);
        using (DebugStatusTextFont.Push())
            ImGui.TextColored(T.TextPrimary, zoneName);
        ImGui.SameLine(0f, 4f * scale);
        using (UiFonts.Mono.Push())
        {
            ImGui.TextColored(T.TextDim, "#");
            ImGui.SameLine(0f, 0f);
            ImGui.TextColored(T.TextValue, territoryId.ToString());
        }

        ImGui.Dummy(Vector2.Zero);

        var playerPos = Plugin.ObjectTable.LocalPlayer?.Position;
        DrawDebugStatusLabel(scale, labelColumnWidth, Loc.T("Weltposition", "World position"));
        ImGui.SetCursorPos(ImGui.GetCursorPos() + new Vector2(30f * scale, 2f * scale));
        if (playerPos.HasValue)
        {
            using (UiFonts.Mono.Push())
            {
                DrawDebugCoordinate("X", playerPos.Value.X, scale);
                ImGui.SameLine(0f, 8f * scale);
                DrawDebugCoordinate("Y", playerPos.Value.Y, scale);
                ImGui.SameLine(0f, 8f * scale);
                DrawDebugCoordinate("Z", playerPos.Value.Z, scale);
            }
        }
        else
        {
            using (DebugStatusTextFont.Push())
                ImGui.TextColored(T.TextDim, Loc.T("nicht verfügbar", "not available"));
        }

        UiWidgets.EndCard();
    }

    private static void DrawDebugStatusLabel(float scale, float columnWidth, string label)
    {
        var rowX = ImGui.GetCursorPosX();
        var rowY = ImGui.GetCursorPosY();
        using (DebugStatusTextFont.Push())
            ImGui.TextColored(UiTheme.Active.TextMuted, label);
        ImGui.SetCursorPos(new Vector2(rowX + columnWidth * scale, rowY));
    }

    private static void DrawDebugCoordinate(string axis, float value, float scale)
    {
        ImGui.TextColored(UiTheme.Active.TextDim, axis);
        ImGui.SameLine(0f, 4f * scale);
        ImGui.TextColored(UiTheme.Active.TextValue, value.ToString("F3", CultureInfo.InvariantCulture));
    }

    /// <summary>"In Zwischenablage kopieren"-Knopf rechts neben dem Kartentitel - 1:1 wie CodexMenuWindow.DrawDebugCopyButton, nur ohne Icon.</summary>
    private void DrawDebugCopyButton(float scale, float titleRowY, float titleHeight, float contentWidth)
    {
        var T = UiTheme.Active;
        var confirmed = Environment.TickCount64 < debugCopyConfirmedUntil;
        var label = confirmed ? Loc.T("Kopiert", "Copied") : Loc.T("In Zwischenablage kopieren", "Copy to clipboard");
        var fg = confirmed ? T.OkFg : T.TextHeading;
        var border = confirmed ? T.OkFg : T.LineControl;

        var icon = confirmed ? UiIcon.Check : UiIcon.Copy;
        var padding = new Vector2(12f * scale, 5f * scale);
        var iconGap = 6f * scale;

        float textWidth, textHeight;
        using (DebugButtonLabelFont.Push())
        {
            var size = ImGui.CalcTextSize(label);
            textWidth = size.X;
            textHeight = size.Y;
        }
        var iconSize = textHeight;

        var buttonSize = new Vector2(iconSize + iconGap + textWidth + padding.X * 2f, textHeight + padding.Y * 2f);
        var leftX = ImGui.GetCursorPosX();
        var rightX = leftX + contentWidth - buttonSize.X;
        var buttonY = titleRowY + (titleHeight - buttonSize.Y) / 2f;

        ImGui.SetCursorPos(new Vector2(rightX, buttonY));
        var cursor = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton("##OceanDebugCopyStatus", buttonSize);
        var hovered = ImGui.IsItemHovered();

        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(cursor, cursor + buttonSize, ImGui.GetColorU32(hovered ? T.BgSelected : new Vector4(0f, 0f, 0f, 0f)), UiMetrics.ControlRadius * scale);
        dl.AddRect(cursor, cursor + buttonSize, ImGui.GetColorU32(border), UiMetrics.ControlRadius * scale);

        UiIcons.Draw(icon, cursor + new Vector2(padding.X, (buttonSize.Y - iconSize) / 2f), iconSize, ImGui.GetColorU32(fg));
        using (DebugButtonLabelFont.Push())
            dl.AddText(cursor + new Vector2(padding.X + iconSize + iconGap, (buttonSize.Y - textHeight) / 2f), ImGui.GetColorU32(fg), label);

        if (hovered)
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);

        if (clicked)
        {
            var playerPos = Plugin.ObjectTable.LocalPlayer?.Position;
            if (playerPos.HasValue)
            {
                ImGui.SetClipboardText($"{playerPos.Value.X.ToString(CultureInfo.InvariantCulture)}f, " +
                                        $"{playerPos.Value.Y.ToString(CultureInfo.InvariantCulture)}f, " +
                                        $"{playerPos.Value.Z.ToString(CultureInfo.InvariantCulture)}f");
                debugCopyConfirmedUntil = Environment.TickCount64 + 1500;
            }
        }

        ImGui.SetCursorPos(new Vector2(leftX, MathF.Max(titleRowY + titleHeight, buttonY + buttonSize.Y)));
    }

    /// <summary>Karte "Simulation" - 1:1 Schriftgrößen/Abstände wie CodexMenuWindow.DrawDebugSimulationCard, mit Big Fish Helpers eigenem Inhalt
    /// (Desynthesis-Simulation starten/stoppen + Statustext) statt Codex' vier Automations-Simulationsschaltern.</summary>
    private void DrawDebugSimulationCard(float scale, float width)
    {
        var T = UiTheme.Active;
        UiWidgets.BeginCard(scale, width: width);
        var contentWidth = width - UiWidgets.CardPaddingX * 2f;

        using (DebugCardTitleFont.Push())
            ImGui.TextColored(T.TextCardTitle, Loc.T("Simulation", "Simulation"));

        ImGui.Dummy(new Vector2(0f, 6f * scale));
        var lineCursor = ImGui.GetCursorScreenPos();
        ImGui.GetWindowDrawList().AddLine(lineCursor, lineCursor + new Vector2(contentWidth, 0f), ImGui.GetColorU32(T.LineSubtle));
        ImGui.Dummy(new Vector2(0f, 8f * scale));

        var automation = plugin.Automation;
        var running = automation.IsRunning;
        if (running)
            ImGui.BeginDisabled();
        if (DrawDebugActionButton(scale, Loc.T("Desynthesis simulieren", "Simulate desynthesis")))
            automation.StartDesynthesisSimulation();
        if (running)
        {
            ImGui.EndDisabled();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            {
                ImGui.SetTooltip(Loc.T(
                    "Läuft bereits eine Automation (oder eine andere Simulation) - erst stoppen.",
                    "An automation (or another simulation) is already running - stop it first."));
            }
        }

        if (!string.IsNullOrEmpty(automation.StatusText))
        {
            ImGui.Dummy(new Vector2(0f, 8f * scale));
            using (DebugDescriptionFont.Push())
                ImGui.TextColored(automation.IsRunning ? T.Accent : T.TextMuted, automation.StatusText);
        }

        if (automation.IsRunning)
        {
            ImGui.Dummy(new Vector2(0f, 8f * scale));
            if (DrawDebugActionButton(scale, Loc.T("Stopp", "Stop")))
                automation.Stop();
        }

        UiWidgets.EndCard();
    }

    /// <summary>Karte "Debug-Ausgaben" - 1:1 Schriftgrößen/Abstände wie CodexMenuWindow.DrawDebugDumpsCard, mit Big Fish Helpers einzigem Dump-Knopf
    /// ("Log fish list") statt Codex' vielen Dump-Knöpfen (kein CodexWidgets.FlowButtons-Umbruch nötig, nur ein Eintrag).</summary>
    private void DrawDebugDumpsCard(float scale, float width)
    {
        var T = UiTheme.Active;
        UiWidgets.BeginCard(scale, width: width);
        var contentWidth = width - UiWidgets.CardPaddingX * 2f;

        var titleRowY = ImGui.GetCursorPosY();
        var rowLeftX = ImGui.GetCursorPosX();
        var title = Loc.T("Debug-Ausgaben", "Debug Dumps");
        float titleHeight;
        using (DebugCardTitleFont.Push())
        {
            titleHeight = ImGui.CalcTextSize(title).Y;
            ImGui.TextColored(T.TextCardTitle, title);
        }

        var hint = Loc.T("Schreibt die Daten ins Dalamud-Log (/xllog).", "Writes the data to the Dalamud log (/xllog).");
        float hintWidth, hintHeight;
        using (DebugDescriptionFont.Push())
        {
            var size = ImGui.CalcTextSize(hint);
            hintWidth = size.X;
            hintHeight = size.Y;
        }
        var hintX = rowLeftX + contentWidth - hintWidth;
        var hintY = titleRowY + (titleHeight - hintHeight) / 2f;
        ImGui.SetCursorPos(new Vector2(hintX, hintY));
        using (DebugDescriptionFont.Push())
            ImGui.TextColored(T.TextMuted, hint);

        ImGui.SetCursorPos(new Vector2(rowLeftX, MathF.Max(titleRowY + titleHeight, hintY + hintHeight)));
        ImGui.Dummy(new Vector2(0f, 6f * scale));
        var lineCursor = ImGui.GetCursorScreenPos();
        ImGui.GetWindowDrawList().AddLine(lineCursor, lineCursor + new Vector2(contentWidth, 0f), ImGui.GetColorU32(T.LineSubtle));
        ImGui.Dummy(new Vector2(0f, 12f * scale));

        if (DrawDebugActionButton(scale, Loc.T("Fisch-Liste ins Log schreiben", "Log fish list")))
            GameActions.DumpDesynthesizableFishInInventory();

        UiWidgets.EndCard();
    }

    /// <summary>Randloser, bordierter Knopf im Debug-Karten-Stil (1:1 CodexWidgets.FlowButtons-Optik, nur ohne Icon/Umbruch - hier immer nur ein Knopf je Karte).</summary>
    private static bool DrawDebugActionButton(float scale, string label)
    {
        var T = UiTheme.Active;
        var padding = new Vector2(11f * scale, 5f * scale);
        float textWidth, textHeight;
        using (DebugButtonLabelFont.Push())
        {
            var size = ImGui.CalcTextSize(label);
            textWidth = size.X;
            textHeight = size.Y;
        }
        var buttonSize = new Vector2(textWidth + padding.X * 2f, textHeight + padding.Y * 2f);

        var cursor = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton($"##OceanDebugAction_{label}", buttonSize);
        var hovered = ImGui.IsItemHovered();

        var dl = ImGui.GetWindowDrawList();
        var bg = hovered ? T.BgSelected : T.BgPopup;
        var border = hovered ? T.Accent : T.LineControl;
        dl.AddRectFilled(cursor, cursor + buttonSize, ImGui.GetColorU32(bg), UiMetrics.ControlRadius * scale);
        dl.AddRect(cursor, cursor + buttonSize, ImGui.GetColorU32(border), UiMetrics.ControlRadius * scale);

        using (DebugButtonLabelFont.Push())
            dl.AddText(cursor + padding, ImGui.GetColorU32(T.TextHeading), label);

        if (hovered)
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        return clicked;
    }

    // ---- Seite "Log" - 1:1 derselbe Aufbau wie CodexMenuWindow.DrawLogPage (Knöpfe, Größen,
    // Abstände, rechter Randabstand, Schriftgrößen identisch) - eigener, von MainWindow unabhängiger
    // Seitenzustand (siehe log*-Felder oben), liest aber denselben geteilten PluginLogStore/
    // LogEntry/SplitLogSource wie das alte Menü. ----

    private void DrawLogPage(float scale)
    {
        var rightMargin = 32f * scale;

        var entries = PluginLogStore.Snapshot();
        EnsureLogFilterCache(entries);

        DrawLogToolbarRow(scale, rightMargin);
        ImGui.Dummy(new Vector2(0f, 10f * scale));
        DrawLogLevelChipsRow(scale);
        ImGui.Dummy(new Vector2(0f, 8f * scale));
        DrawLogMetaRow(scale, rightMargin, logFilteredCache!.Count, logTotalCountCache);
        ImGui.Dummy(new Vector2(0f, 6f * scale));
        DrawLogConsole(scale, rightMargin, logFilteredCache!);
    }

    private void EnsureLogFilterCache(List<LogEntry> entries)
    {
        var levelsMask = 0;
        foreach (var level in logLevelFilter)
            levelsMask |= 1 << (int)level;

        var lastId = entries.Count > 0 ? entries[^1].Id : 0L;
        var key = (logSearch, logSelectedSource, levelsMask, entries.Count, lastId);
        if (logFilteredCache != null && key == logCacheKey)
            return;

        logCacheKey = key;
        logTotalCountCache = entries.Count;

        logSourcesCache = entries
            .Select(e => SplitLogSource(e.Message).Source)
            .Where(s => !string.IsNullOrEmpty(s))
            .Distinct()
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToList();

        IEnumerable<LogEntry> baseFiltered = entries;
        if (!string.IsNullOrEmpty(logSelectedSource))
            baseFiltered = baseFiltered.Where(e => SplitLogSource(e.Message).Source == logSelectedSource);
        if (!string.IsNullOrWhiteSpace(logSearch))
        {
            baseFiltered = baseFiltered.Where(e =>
                e.Message.Contains(logSearch, StringComparison.OrdinalIgnoreCase) ||
                e.Timestamp.ToString("HH:mm:ss").Contains(logSearch, StringComparison.OrdinalIgnoreCase));
        }
        var baseFilteredList = baseFiltered.ToList();

        logLevelCountsCache = baseFilteredList
            .GroupBy(e => e.Level)
            .ToDictionary(g => g.Key, g => g.Count());

        logFilteredCache = baseFilteredList.Where(e => logLevelFilter.Contains(e.Level)).ToList();
    }

    /// <summary>Werkzeugleiste Zeile 1: Suchfeld (flexible Breite) + "Quelle: ..."-Dropdown.</summary>
    private void DrawLogToolbarRow(float scale, float rightMargin)
    {
        var T = UiTheme.Active;
        var sourceLabel = Loc.T($"Quelle: {logSelectedSource ?? Loc.T("Alle", "All")}", $"Source: {logSelectedSource ?? "All"}");
        float sourceTextWidth;
        using (LogFilterLabelFont.Push())
            sourceTextWidth = ImGui.CalcTextSize(sourceLabel).X;

        var sourcePadding = new Vector2(12f * scale, 7f * scale);
        var gap = 10f * scale;
        var searchPadding = new Vector2(12f * scale, 9f * scale);
        var sourceMinWidth = sourceTextWidth + sourcePadding.X * 2f;

        var searchWidth = ImGui.GetContentRegionAvail().X - rightMargin - sourceMinWidth - gap;
        ImGui.PushStyleColor(ImGuiCol.FrameBg, T.BgInput);
        ImGui.PushStyleColor(ImGuiCol.Border, T.LineControl);
        ImGui.PushStyleColor(ImGuiCol.BorderShadow, new Vector4(0f, 0f, 0f, 0f));
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, searchPadding);
        ImGui.SetNextItemWidth(searchWidth);
        using (LogSearchInputFont.Push())
            ImGui.InputTextWithHint("##OceanLogSearch", Loc.T("Log durchsuchen …", "Search log …"), ref logSearch, 200);
        var searchHeight = ImGui.GetItemRectSize().Y;
        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor(3);

        var sourceSize = new Vector2(sourceMinWidth, searchHeight);
        ImGui.SameLine(0f, gap);
        var sourceCursor = ImGui.GetCursorScreenPos();
        var sourceClicked = ImGui.InvisibleButton("##OceanLogSourceFilter", sourceSize);
        var sourceHovered = ImGui.IsItemHovered();

        var drawList = ImGui.GetWindowDrawList();
        var sourceActive = !string.IsNullOrEmpty(logSelectedSource);
        var sourceBg = sourceActive ? T.BgSelectedStrong : sourceHovered ? T.BgSelected with { W = 0.5f } : new Vector4(0f, 0f, 0f, 0f);
        var sourceBorder = sourceActive ? T.Accent : T.LineControl;
        var sourceFg = sourceActive ? T.TextHeading : T.TextSecondary;
        drawList.AddRectFilled(sourceCursor, sourceCursor + sourceSize, ImGui.GetColorU32(sourceBg), UiMetrics.ControlRadius * scale);
        drawList.AddRect(sourceCursor, sourceCursor + sourceSize, ImGui.GetColorU32(sourceBorder), UiMetrics.ControlRadius * scale);
        using (LogFilterLabelFont.Push())
        {
            var textSize = ImGui.CalcTextSize(sourceLabel);
            drawList.AddText(sourceCursor + (sourceSize - textSize) / 2f, ImGui.GetColorU32(sourceFg), sourceLabel);
        }

        if (sourceClicked)
            ImGui.OpenPopup("##OceanLogSourcePopup");

        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(3f * scale, 3f * scale));
        if (ImGui.BeginPopup("##OceanLogSourcePopup"))
        {
            if (ImGui.Selectable(Loc.T("Alle", "All") + "##OceanLogSourceAll", logSelectedSource == null))
                logSelectedSource = null;
            foreach (var source in logSourcesCache!)
            {
                if (ImGui.Selectable(source + "##OceanLogSourceEntry_" + source, logSelectedSource == source))
                    logSelectedSource = source;
            }
            ImGui.EndPopup();
        }
        ImGui.PopStyleVar();
    }

    /// <summary>Werkzeugleiste Zeile 2: Level-Chips (Umschalter + Zähler).</summary>
    private void DrawLogLevelChipsRow(float scale)
    {
        var T = UiTheme.Active;
        var chipGap = 6f * scale;

        DrawLogLevelChip(scale, LogEventLevel.Verbose, Loc.T("Verbose", "Verbose"), T.TextDisabled, isFirst: true);
        DrawLogLevelChip(scale, LogEventLevel.Debug, Loc.T("Debug", "Debug"), T.TextMuted, isFirst: false, gap: chipGap);
        DrawLogLevelChip(scale, LogEventLevel.Information, Loc.T("Info", "Info"), T.InfoFg, isFirst: false, gap: chipGap);
        DrawLogLevelChip(scale, LogEventLevel.Warning, Loc.T("Warnung", "Warning"), T.WarnFg, isFirst: false, gap: chipGap);
        DrawLogLevelChip(scale, LogEventLevel.Error, Loc.T("Fehler", "Error"), T.ErrFg, isFirst: false, gap: chipGap);
        DrawLogLevelChip(scale, LogEventLevel.Fatal, Loc.T("Kritisch", "Critical"), LogCritFg, isFirst: false, gap: chipGap);
    }

    private void DrawLogLevelChip(float scale, LogEventLevel level, string label, Vector4 color, bool isFirst, float gap = 0f)
    {
        var T = UiTheme.Active;
        var active = logLevelFilter.Contains(level);
        logLevelCountsCache!.TryGetValue(level, out var count);
        var countText = count.ToString(CultureInfo.InvariantCulture);

        float labelWidth, countWidth, labelHeight;
        using (LogFilterLabelFont.Push())
        {
            var size = ImGui.CalcTextSize("◆ " + label);
            labelWidth = size.X;
            labelHeight = size.Y;
        }
        using (LogChipCountFont.Push())
            countWidth = ImGui.CalcTextSize(countText).X;

        var padding = new Vector2(10f * scale, 6.5f * scale);
        var innerGap = 6f * scale;
        var chipSize = new Vector2(labelWidth + innerGap + countWidth + padding.X * 2f, labelHeight + padding.Y * 2f);

        if (!isFirst)
            ImGui.SameLine(0f, gap);

        var cursor = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton("##OceanLogChip_" + level, chipSize);
        var hovered = ImGui.IsItemHovered();

        var drawList = ImGui.GetWindowDrawList();
        var bg = active ? color with { W = 0.12f } : hovered ? T.BgSelected with { W = 0.5f } : new Vector4(0f, 0f, 0f, 0f);
        var border = active ? color with { W = 0.45f } : T.LineDisabled;
        var fg = active ? color : T.TextDisabled;
        drawList.AddRectFilled(cursor, cursor + chipSize, ImGui.GetColorU32(bg), UiMetrics.ControlRadius * scale);
        drawList.AddRect(cursor, cursor + chipSize, ImGui.GetColorU32(border), UiMetrics.ControlRadius * scale);

        var textY = cursor.Y + padding.Y;
        using (LogFilterLabelFont.Push())
            drawList.AddText(new Vector2(cursor.X + padding.X, textY), ImGui.GetColorU32(fg), "◆ " + label);
        using (LogChipCountFont.Push())
        {
            var countY = cursor.Y + (chipSize.Y - ImGui.CalcTextSize(countText).Y) / 2f;
            drawList.AddText(new Vector2(cursor.X + padding.X + labelWidth + innerGap, countY), ImGui.GetColorU32(fg), countText);
        }

        if (hovered)
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        if (clicked)
        {
            if (!logLevelFilter.Remove(level))
                logLevelFilter.Add(level);
        }
    }

    /// <summary>Transparenter Knopf mit Rahmen (Select lines/Copy all/Clear) - Hover BgSelected, aktiv (Select-Modus) Accent-Rahmen statt LineControl.</summary>
    private static bool DrawLogGhostButton(float scale, string id, string label, float width, bool active, Vector4? textColor = null)
    {
        var T = UiTheme.Active;
        var size = new Vector2(width, 31f * scale);
        var cursor = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton(id, size);
        var hovered = ImGui.IsItemHovered();

        var drawList = ImGui.GetWindowDrawList();
        var bg = hovered ? T.BgSelected : new Vector4(0f, 0f, 0f, 0f);
        var border = active ? T.Accent : T.LineControl;
        var fg = textColor ?? (active ? T.TextHeading : T.TextSecondary);
        drawList.AddRectFilled(cursor, cursor + size, ImGui.GetColorU32(bg), UiMetrics.ControlRadius * scale);
        drawList.AddRect(cursor, cursor + size, ImGui.GetColorU32(border), UiMetrics.ControlRadius * scale);
        using (LogGhostButtonFont.Push())
        {
            var textSize = ImGui.CalcTextSize(label);
            drawList.AddText(cursor + (size - textSize) / 2f, ImGui.GetColorU32(fg), label);
        }

        if (hovered)
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        return clicked;
    }

    /// <summary>"{n} von {m} Zeilen" links, Select lines/Copy all/Clear rechtsbündig.</summary>
    private void DrawLogMetaRow(float scale, float rightMargin, int filteredCount, int totalCount)
    {
        var T = UiTheme.Active;
        var rowY = ImGui.GetCursorPosY();
        var buttonHeight = 31f * scale;

        var lineCountText = Loc.T($"{filteredCount} von {totalCount} Zeilen", $"{filteredCount} of {totalCount} lines");
        float textHeight;
        using (LogMetaFont.Push())
            textHeight = ImGui.CalcTextSize(lineCountText).Y;

        ImGui.SetCursorPosY(rowY + buttonHeight - textHeight);
        using (LogMetaFont.Push())
            ImGui.TextColored(T.TextDim, lineCountText);

        var selectLabel = Loc.T("Zeilen auswählen", "Select lines");
        var copyLabel = logSelectMode
            ? Loc.T($"Auswahl kopieren ({logSelectedIds.Count})", $"Copy selected ({logSelectedIds.Count})")
            : Loc.T("Alles kopieren", "Copy all");
        var clearLabel = Loc.T("Leeren", "Clear");

        float selectWidth, copyWidth, clearWidth;
        using (LogGhostButtonFont.Push())
        {
            var padX = 12f * scale * 2f;
            selectWidth = ImGui.CalcTextSize(selectLabel).X + padX;
            copyWidth = ImGui.CalcTextSize(copyLabel).X + padX;
            clearWidth = ImGui.CalcTextSize(clearLabel).X + padX;
        }
        var buttonGap = 8f * scale;
        var totalButtonsWidth = selectWidth + copyWidth + clearWidth + buttonGap * 2f;

        var rowStartX = ImGui.GetCursorPosX();
        var buttonY = rowY;

        ImGui.SameLine(MathF.Max(0f, rowStartX + ImGui.GetContentRegionAvail().X - rightMargin - totalButtonsWidth));
        ImGui.SetCursorPosY(buttonY);
        if (DrawLogGhostButton(scale, "##OceanLogSelectMode", selectLabel, selectWidth, active: logSelectMode))
        {
            logSelectMode = !logSelectMode;
            if (!logSelectMode)
            {
                logSelectedIds.Clear();
                logLastClickedRowIndex = null;
            }
        }

        ImGui.SameLine(0f, buttonGap);
        ImGui.SetCursorPosY(buttonY);
        if (DrawLogGhostButton(scale, "##OceanLogCopy", copyLabel, copyWidth, active: false))
        {
            var toCopy = logSelectMode
                ? logFilteredCache!.Where(e => logSelectedIds.Contains(e.Id))
                : logFilteredCache!;
            CopyLogLines(toCopy);
        }

        ImGui.SameLine(0f, buttonGap);
        ImGui.SetCursorPosY(buttonY);
        if (DrawLogGhostButton(scale, "##OceanLogClear", clearLabel, clearWidth, active: false))
        {
            PluginLogStore.Clear();
            logSelectedIds.Clear();
            logLastClickedRowIndex = null;
        }

        ImGui.SetCursorPosY(rowY + MathF.Max(textHeight, buttonHeight));
    }

    private static void SetupLogTableColumns(float scale)
    {
        ImGui.TableSetupColumn("##OceanLogTime", ImGuiTableColumnFlags.WidthFixed, 78f * scale);
        ImGui.TableSetupColumn("##OceanLogLevel", ImGuiTableColumnFlags.WidthFixed, 52f * scale);
        ImGui.TableSetupColumn("##OceanLogSource", ImGuiTableColumnFlags.WidthFixed, 150f * scale);
        ImGui.TableSetupColumn("##OceanLogMessage", ImGuiTableColumnFlags.WidthStretch, 1f);
    }

    private void DrawLogConsole(float scale, float rightMargin, List<LogEntry> filtered)
    {
        var T = UiTheme.Active;
        var width = ImGui.GetContentRegionAvail().X - rightMargin;
        var height = MathF.Max(100f * scale,
            ImGui.GetWindowPos().Y + ImGui.GetWindowSize().Y - ImGui.GetCursorScreenPos().Y - 60f * scale);

        ImGui.PushStyleColor(ImGuiCol.ChildBg, T.BgInput);
        ImGui.PushStyleColor(ImGuiCol.Border, T.LineCard);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 4f * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildBorderSize, 1f * scale);
        ImGui.BeginChild("##OceanLogConsole", new Vector2(width, height), true);

        var innerWidth = ImGui.GetContentRegionAvail().X;
        var headerHeight = 32f * scale;

        if (ImGui.BeginTable("##OceanLogHeaderTable", 4, ImGuiTableFlags.None, new Vector2(innerWidth, headerHeight)))
        {
            SetupLogTableColumns(scale);
            DrawLogTableHeader(scale, headerHeight);
            ImGui.EndTable();
        }

        var bodyHeight = ImGui.GetContentRegionAvail().Y;
        ImGui.BeginChild("##OceanLogBody", new Vector2(innerWidth, bodyHeight), false);

        if (filtered.Count == 0)
        {
            ImGui.Dummy(new Vector2(0f, bodyHeight / 2f - 12f * scale));
            var emptyText = Loc.T("Keine Log-Zeilen passen zu den Filtern.", "No log lines match the current filters.");
            using (LogMetaFont.Push())
                UiWidgetsExtra.CenterNext(ImGui.CalcTextSize(emptyText).X, ImGui.GetContentRegionAvail().X);
            using (LogMetaFont.Push())
                ImGui.TextColored(T.TextDim, emptyText);
        }
        else
        {
            ImGui.PushStyleColor(ImGuiCol.TableBorderLight, T.LineRow);
            if (ImGui.BeginTable("##OceanLogBodyTable", 4, ImGuiTableFlags.BordersInnerH))
            {
                SetupLogTableColumns(scale);

                var rowHeight = 24f * scale;
                var clipper = new ImGuiListClipper();
                clipper.Begin(filtered.Count, rowHeight);
                while (clipper.Step())
                {
                    for (var i = clipper.DisplayStart; i < clipper.DisplayEnd; i++)
                        DrawLogRow(scale, filtered, i, rowHeight);
                }
                clipper.End();

                ImGui.EndTable();
            }
            ImGui.PopStyleColor();

            if (logFollowNewLines)
                ImGui.SetScrollY(ImGui.GetScrollMaxY());
            logFollowNewLines = ImGui.GetScrollY() >= ImGui.GetScrollMaxY() - 2f;
        }

        ImGui.EndChild();
        ImGui.EndChild();
        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor(2);
    }

    private static void DrawLogTableHeader(float scale, float headerHeight)
    {
        var T = UiTheme.Active;
        ImGui.TableNextRow(ImGuiTableRowFlags.Headers, headerHeight);
        ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(T.BgCard));

        string[] labels = { Loc.T("ZEIT", "TIME"), Loc.T("LEVEL", "LEVEL"), Loc.T("QUELLE", "SOURCE"), Loc.T("NACHRICHT", "MESSAGE") };
        using (LogTableHeaderFont.Push())
        {
            var textHeight = ImGui.GetTextLineHeight();
            for (var c = 0; c < 4; c++)
            {
                ImGui.TableSetColumnIndex(c);
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 8f * scale);
                ImGui.SetCursorPosY(ImGui.GetCursorPosY() + (headerHeight - textHeight) / 2f);
                UiWidgets.DrawSpacedText(labels[c], T.TextTertiary, 1f * scale);
            }
        }
    }

    private void DrawLogRow(float scale, List<LogEntry> filtered, int rowIndex, float rowHeight)
    {
        var T = UiTheme.Active;
        var entry = filtered[rowIndex];
        var (source, message) = SplitLogSource(entry.Message);
        var isSelected = logSelectMode && logSelectedIds.Contains(entry.Id);
        var levelColor = LogLevelColor(entry.Level);
        var isDimmed = entry.Level is LogEventLevel.Debug or LogEventLevel.Verbose;
        var isTinted = entry.Level is LogEventLevel.Warning or LogEventLevel.Error or LogEventLevel.Fatal;
        var messageColor = isDimmed ? T.TextMuted : T.TextPrimary;

        ImGui.TableNextRow(ImGuiTableRowFlags.None, rowHeight);

        var rowBg = isSelected ? T.BgSelectedStrong
            : isTinted ? levelColor with { W = 0.05f }
            : rowIndex % 2 == 1 ? T.BgInput with { W = 1f }
            : new Vector4(0f, 0f, 0f, 0f);
        ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(rowBg));

        ImGui.TableSetColumnIndex(0);
        var rowCursor = ImGui.GetCursorScreenPos();
        ImGui.Selectable($"##OceanLogRow_{entry.Id}", false, ImGuiSelectableFlags.SpanAllColumns, new Vector2(0f, rowHeight));
        var rowHovered = ImGui.IsItemHovered();
        var rowMin = ImGui.GetItemRectMin();
        var rowMax = ImGui.GetItemRectMax();
        if (logSelectMode && rowHovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            HandleLogRowClick(rowIndex, entry.Id, filtered);
        if (!isSelected && rowHovered)
            ImGui.GetWindowDrawList().AddRectFilled(rowMin, rowMax, ImGui.GetColorU32(T.BgSelected with { W = 0.4f }));
        if (isSelected)
            ImGui.GetWindowDrawList().AddRectFilled(rowMin, new Vector2(rowMin.X + 2f * scale, rowMax.Y), ImGui.GetColorU32(T.Accent));

        ImGui.SetCursorScreenPos(rowCursor);
        using (UiFonts.Mono.Push())
        {
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 8f * scale);
            ImGui.TextColored(T.TextDim, entry.Timestamp.ToString("HH:mm:ss"));
        }

        ImGui.TableSetColumnIndex(1);
        var badgeText = LogLevelBadge(entry.Level);
        var badgeSize = new Vector2(36f * scale, 16f * scale);
        var badgeCursor = ImGui.GetCursorScreenPos() + new Vector2(0f, (rowHeight - badgeSize.Y) / 2f);
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(badgeCursor, badgeCursor + badgeSize, ImGui.GetColorU32(levelColor with { W = 0.14f }), 3f * scale);
        using (LogBadgeFont.Push())
        {
            var textSize = ImGui.CalcTextSize(badgeText);
            drawList.AddText(badgeCursor + (badgeSize - textSize) / 2f, ImGui.GetColorU32(levelColor), badgeText);
        }

        ImGui.TableSetColumnIndex(2);
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 8f * scale);
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + (rowHeight - ImGui.GetTextLineHeight()) / 2f);
        using (LogRowFont.Push())
        {
            var maxWidth = ImGui.GetContentRegionAvail().X;
            var truncatedSource = TruncateToWidth(source, maxWidth);
            ImGui.TextColored(T.TextSecondary, truncatedSource);
        }

        ImGui.TableSetColumnIndex(3);
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 8f * scale);
        var messageCellPos = ImGui.GetCursorScreenPos() + new Vector2(0f, (rowHeight - 13f * scale) / 2f);
        float maxMessageWidth;
        using (LogRowFont.Push())
            maxMessageWidth = ImGui.GetContentRegionAvail().X;
        string truncatedMessage;
        using (LogRowFont.Push())
            truncatedMessage = TruncateToWidth(message, maxMessageWidth);
        DrawLogHighlightedText(messageCellPos, truncatedMessage, messageColor);
        if (rowHovered && truncatedMessage != message)
        {
            ImGui.PushStyleColor(ImGuiCol.PopupBg, T.BgPopup);
            ImGui.BeginTooltip();
            ImGui.PushTextWrapPos(ImGui.GetFontSize() * 40f);
            using (LogRowFont.Push())
                ImGui.TextColored(T.TextPrimary, message);
            ImGui.PopTextWrapPos();
            ImGui.EndTooltip();
            ImGui.PopStyleColor();
        }
    }

    private void HandleLogRowClick(int rowIndex, long entryId, List<LogEntry> filteredList)
    {
        var io = ImGui.GetIO();
        if (io.KeyShift && logLastClickedRowIndex.HasValue)
        {
            var start = Math.Min(logLastClickedRowIndex.Value, rowIndex);
            var end = Math.Max(logLastClickedRowIndex.Value, rowIndex);
            for (var i = start; i <= end; i++)
                logSelectedIds.Add(filteredList[i].Id);
        }
        else if (io.KeyCtrl)
        {
            if (!logSelectedIds.Remove(entryId))
                logSelectedIds.Add(entryId);
        }
        else
        {
            logSelectedIds.Clear();
            logSelectedIds.Add(entryId);
        }

        logLastClickedRowIndex = rowIndex;
    }

    private static void DrawLogHighlightedText(Vector2 screenPos, string text, Vector4 baseColor)
    {
        var T = UiTheme.Active;
        var drawList = ImGui.GetWindowDrawList();
        var x = screenPos.X;
        var lastIndex = 0;

        void DrawPlain(string segment)
        {
            if (segment.Length == 0)
                return;
            using (LogRowFont.Push())
            {
                drawList.AddText(new Vector2(x, screenPos.Y), ImGui.GetColorU32(baseColor), segment);
                x += ImGui.CalcTextSize(segment).X;
            }
        }

        foreach (Match m in LogTokenRegex.Matches(text))
        {
            DrawPlain(text[lastIndex..m.Index]);
            using (UiFonts.Mono.Push())
            {
                drawList.AddText(new Vector2(x, screenPos.Y), ImGui.GetColorU32(T.Accent), m.Value);
                x += ImGui.CalcTextSize(m.Value).X;
            }
            lastIndex = m.Index + m.Length;
        }
        DrawPlain(text[lastIndex..]);
    }

    /// <summary>Kürzt per CalcTextSize auf maxWidth und hängt "…" an, falls nötig - erwartet die passende Schrift bereits gepusht.</summary>
    private static string TruncateToWidth(string text, float maxWidth)
    {
        if (string.IsNullOrEmpty(text) || ImGui.CalcTextSize(text).X <= maxWidth)
            return text;

        const string ellipsis = "…";
        var ellipsisWidth = ImGui.CalcTextSize(ellipsis).X;
        var low = 0;
        var high = text.Length;
        while (low < high)
        {
            var mid = (low + high + 1) / 2;
            if (ImGui.CalcTextSize(text[..mid]).X + ellipsisWidth <= maxWidth)
                low = mid;
            else
                high = mid - 1;
        }
        return text[..low] + ellipsis;
    }

    private static void CopyLogLines(IEnumerable<LogEntry> lines)
    {
        var text = new StringBuilder();
        foreach (var entry in lines)
        {
            var (source, message) = SplitLogSource(entry.Message);
            var sourcePrefix = string.IsNullOrEmpty(source) ? string.Empty : $"{source}: ";
            text.Append(entry.Timestamp.ToString("HH:mm:ss.fff")).Append(" [").Append(LevelLabel(entry.Level)).Append("] ")
                .Append(sourcePrefix).Append(message).Append('\n');
        }
        ImGui.SetClipboardText(text.ToString());
    }

    private static Vector4 LogLevelColor(LogEventLevel level)
    {
        var T = UiTheme.Active;
        return level switch
        {
            LogEventLevel.Verbose => T.TextDisabled,
            LogEventLevel.Debug => T.TextMuted,
            LogEventLevel.Information => T.InfoFg,
            LogEventLevel.Warning => T.WarnFg,
            LogEventLevel.Error => T.ErrFg,
            LogEventLevel.Fatal => LogCritFg,
            _ => T.TextMuted,
        };
    }

    private static string LogLevelBadge(LogEventLevel level) => LevelLabel(level);

    private static string LevelLabel(LogEventLevel level) => level switch
    {
        LogEventLevel.Verbose => "VRB",
        LogEventLevel.Debug => "DBG",
        LogEventLevel.Information => "INF",
        LogEventLevel.Warning => "WRN",
        LogEventLevel.Error => "ERR",
        LogEventLevel.Fatal => "CRT",
        _ => "???",
    };

    /// <summary>Löst die führende "[Quelle] "-Klammer einer Log-Nachricht heraus, Rest bleibt die eigentliche Nachricht ohne das Tag - 1:1 wie CodexMenuWindow.SplitLogSource.</summary>
    private static (string Source, string Message) SplitLogSource(string message)
    {
        if (message.Length > 0 && message[0] == '[')
        {
            var close = message.IndexOf(']');
            if (close > 1)
                return (message[1..close], message[(close + 1)..].TrimStart());
        }

        return (string.Empty, message);
    }

    // ---- Seite "Änderungen" - 1:1 derselbe Aufbau wie CodexMenuWindow.DrawChangelogPage (Zeitleiste
    // mit auf-/zuklappbaren Versionskarten, Schriftgrößen/Abstände/rechter Randabstand identisch) -
    // liest ChangelogService/Data/Changelog.json wie Codex, Rauten/Chevron gezeichnet statt als
    // Glyph, nur der Chevron kommt wie bei Codex aus der FontAwesome-Icon-Schrift (kennt dieses
    // Pfeilsymbol, die normale Textschrift nicht). ----

    private void DrawChangelogPage(float scale)
    {
        var T = UiTheme.Active;
        var config = plugin.Configuration;
        var entries = ChangelogService.Entries;

        if (!changelogMarkedSeen)
        {
            changelogMarkedSeen = true;
            Version.TryParse(config.LastSeenChangelogVersion, out var previouslySeen);
            changelogUnseenThreshold = previouslySeen;
            if (ChangelogService.LatestVersion is { } latest && (previouslySeen == null || latest > previouslySeen))
            {
                config.LastSeenChangelogVersion = latest.ToString();
                config.Save();
            }
        }

        if (entries.Count == 0)
        {
            using (ChangelogMetaFont.Push())
                ImGui.TextColored(T.TextDim, Loc.T("Noch keine Änderungen eingetragen.", "No changes recorded yet."));
            return;
        }

        var displayedEntries = entries.Count > 3 ? entries.GetRange(0, 3) : entries;

        var rightMargin = 32f * scale;
        var timelineWidth = 34f * scale;
        var lineX = ImGui.GetCursorScreenPos().X + 9f * scale;
        var cardWidth = ImGui.GetContentRegionAvail().X - timelineWidth - rightMargin;
        var rowStartX = ImGui.GetCursorPosX();

        float? firstDiamondY = null;
        var lastDiamondY = 0f;

        for (var i = 0; i < displayedEntries.Count; i++)
        {
            var entry = displayedEntries[i];
            var isLatest = i == 0;
            var cardTopScreenY = ImGui.GetCursorScreenPos().Y;
            var cardTopPosY = ImGui.GetCursorPosY();

            var expanded = GetChangelogExpanded(entry, isLatest);
            float headerLineHeight;
            using ((expanded ? ChangelogVersionTitleFont : ChangelogCollapsedTitleFont).Push())
                headerLineHeight = ImGui.GetTextLineHeight();
            var headerPaddingY = expanded ? UiMetrics.CardPadY * scale : 12f * scale;
            var diamondY = cardTopScreenY + headerPaddingY + headerLineHeight / 2f;

            firstDiamondY ??= diamondY;
            lastDiamondY = diamondY;

            var diamondCenter = new Vector2(lineX, diamondY);
            if (isLatest)
            {
                ImGui.GetWindowDrawList().AddCircleFilled(diamondCenter, 10f * scale, ImGui.GetColorU32(T.Accent with { W = 0.15f }));
                UiWidgetsExtra.Diamond(diamondCenter, 8.5f * scale, ImGui.GetColorU32(T.Accent), ImGui.GetColorU32(T.Accent));
            }
            else
            {
                UiWidgetsExtra.Diamond(diamondCenter, 8.5f * scale, ImGui.GetColorU32(T.BgWindow), ImGui.GetColorU32(T.LineFrame));
            }

            ImGui.SetCursorPos(new Vector2(rowStartX + timelineWidth, cardTopPosY));
            DrawChangelogVersionCard(scale, entry, expanded, cardWidth);

            if (i < displayedEntries.Count - 1)
                ImGui.Dummy(new Vector2(0f, 14f * scale));
        }

        if (firstDiamondY.HasValue && firstDiamondY.Value < lastDiamondY)
            ImGui.GetWindowDrawList().AddLine(new Vector2(lineX, firstDiamondY.Value), new Vector2(lineX, lastDiamondY), ImGui.GetColorU32(T.LineCard));
    }

    private bool GetChangelogExpanded(ChangelogEntry entry, bool isLatest)
    {
        if (!changelogExpanded.TryGetValue(entry.Version, out var expanded))
        {
            expanded = isLatest;
            changelogExpanded[entry.Version] = expanded;
        }
        return expanded;
    }

    private void DrawChangelogVersionCard(float scale, ChangelogEntry entry, bool expanded, float width)
    {
        var T = UiTheme.Active;
        var showNewBadge = Version.TryParse(entry.Version, out var entryVersion) &&
            (changelogUnseenThreshold == null || entryVersion > changelogUnseenThreshold);

        if (!expanded)
        {
            DrawChangelogCollapsedRow(scale, entry, width, showNewBadge);
            return;
        }

        UiWidgets.BeginCard(scale, width: width);
        var extraIndent = 40f * scale;
        ImGui.Indent(extraIndent);
        var contentWidth = width - UiWidgets.CardPaddingX * 2f;
        var rowX = ImGui.GetCursorPosX();
        var rowY = ImGui.GetCursorPosY();

        var titleText = string.Format(Loc.T("Version {0}", "Version {0}"), entry.Version);
        float titleWidth, titleHeight;
        using (ChangelogVersionTitleFont.Push())
        {
            var size = ImGui.CalcTextSize(titleText);
            titleWidth = size.X;
            titleHeight = size.Y;
            ImGui.TextColored(T.TextHeading, titleText);
        }

        if (showNewBadge)
        {
            ImGui.SameLine(0f, 8f * scale);
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + (titleHeight - ChangelogBadgeHeight(scale)) / 2f);
            var badgeCursor = ImGui.GetCursorScreenPos();
            var badgeSize = DrawChangelogNewBadgeAt(scale, badgeCursor);
            ImGui.Dummy(badgeSize);
        }

        var dateText = FormatChangelogDate(entry.Date);
        float dateWidth, dateHeight;
        using (ChangelogMetaFont.Push())
        {
            var size = ImGui.CalcTextSize(dateText);
            dateWidth = size.X;
            dateHeight = size.Y;
        }
        ImGui.SetCursorPos(new Vector2(rowX + contentWidth - dateWidth - 15f * scale, rowY + (titleHeight - dateHeight) / 2f));
        using (ChangelogMetaFont.Push())
            ImGui.TextColored(T.TextMuted, dateText);

        ImGui.SetCursorPos(new Vector2(rowX, rowY + titleHeight));
        ImGui.Dummy(new Vector2(0f, 10f * scale));

        var lineCursor = ImGui.GetCursorScreenPos();
        ImGui.GetWindowDrawList().AddLine(lineCursor, lineCursor + new Vector2(contentWidth, 0f), ImGui.GetColorU32(T.LineSubtle));
        ImGui.Dummy(new Vector2(0f, 10f * scale));

        DrawChangelogChangeList(scale, entry, contentWidth);

        ImGui.Unindent(extraIndent);
        UiWidgets.EndCard();
    }

    private static void DrawChangelogChangeList(float scale, ChangelogEntry entry, float contentWidth)
    {
        var T = UiTheme.Active;
        var tagColumnWidth = ChangelogTagColumnWidth(scale);
        var tagBadgeHeight = ChangelogBadgeHeight(scale);
        var rowGap = 8f * scale;

        for (var i = 0; i < entry.Changes.Count; i++)
        {
            var change = entry.Changes[i];
            var rowTopY = ImGui.GetCursorPosY();
            var tagCursor = ImGui.GetCursorScreenPos();
            DrawChangelogTagBadge(scale, tagCursor, change.Type);

            ImGui.Indent(tagColumnWidth);
            using (ChangelogChangeTextFont.Push())
            {
                var wrapPos = ImGui.GetCursorPosX() + (contentWidth - tagColumnWidth);
                ImGui.PushTextWrapPos(wrapPos);
                ImGui.TextColored(T.TextValue, ChangelogService.ResolveText(change));
                ImGui.PopTextWrapPos();
            }
            ImGui.Unindent(tagColumnWidth);

            var minRowBottomY = rowTopY + tagBadgeHeight;
            if (ImGui.GetCursorPosY() < minRowBottomY)
                ImGui.Dummy(new Vector2(0f, minRowBottomY - ImGui.GetCursorPosY()));

            if (i < entry.Changes.Count - 1)
                ImGui.Dummy(new Vector2(0f, rowGap));
        }
    }

    /// <summary>Einzeilige, zugeklappte Versionskarte - Klick auf die Zeile klappt sie auf.</summary>
    private void DrawChangelogCollapsedRow(float scale, ChangelogEntry entry, float width, bool showNewBadge)
    {
        var T = UiTheme.Active;
        var padding = new Vector2(20f * scale, 12f * scale);
        float titleHeight;
        using (ChangelogCollapsedTitleFont.Push())
            titleHeight = ImGui.GetTextLineHeight();
        var rowHeight = titleHeight + padding.Y * 2f;
        var rowSize = new Vector2(width, rowHeight);

        var cursor = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton("##OceanChangelogRow_" + entry.Version, rowSize);
        var hovered = ImGui.IsItemHovered();

        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(cursor, cursor + rowSize, ImGui.GetColorU32(hovered ? T.BgSelected : T.BgCard), UiMetrics.CardRadius);
        drawList.AddRect(cursor, cursor + rowSize, ImGui.GetColorU32(T.LineCard), UiMetrics.CardRadius);

        var titleText = string.Format(Loc.T("Version {0}", "Version {0}"), entry.Version);
        float titleWidth;
        using (ChangelogCollapsedTitleFont.Push())
        {
            titleWidth = ImGui.CalcTextSize(titleText).X;
            drawList.AddText(new Vector2(cursor.X + padding.X, cursor.Y + padding.Y), ImGui.GetColorU32(T.TextHeading), titleText);
        }

        var dateText = FormatChangelogDate(entry.Date);
        float dateWidth;
        using (ChangelogMetaFont.Push())
        {
            var size = ImGui.CalcTextSize(dateText);
            dateWidth = size.X;
            drawList.AddText(new Vector2(cursor.X + padding.X + titleWidth + 10f * scale, cursor.Y + (rowHeight - size.Y) / 2f), ImGui.GetColorU32(T.TextMuted), dateText);
        }

        if (showNewBadge)
        {
            var badgeX = cursor.X + padding.X + titleWidth + 10f * scale + dateWidth + 12f * scale;
            DrawChangelogNewBadgeAt(scale, new Vector2(badgeX, cursor.Y + (rowHeight - ChangelogBadgeHeight(scale)) / 2f));
        }

        var changesLabel = entry.Changes.Count == 1
            ? Loc.T("1 Änderung", "1 change")
            : string.Format(Loc.T("{0} Änderungen", "{0} changes"), entry.Changes.Count);
        float changesWidth, changesHeight;
        using (ChangelogMetaFont.Push())
        {
            var size = ImGui.CalcTextSize(changesLabel);
            changesWidth = size.X;
            changesHeight = size.Y;
        }

        string chevronGlyph;
        float chevronWidth, chevronHeight;
        using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
        {
            chevronGlyph = FontAwesomeIcon.ChevronRight.ToIconString();
            var size = ImGui.CalcTextSize(chevronGlyph);
            chevronWidth = size.X;
            chevronHeight = size.Y;
        }

        var chevronX = cursor.X + width - padding.X - chevronWidth;
        using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
            drawList.AddText(new Vector2(chevronX, cursor.Y + (rowHeight - chevronHeight) / 2f), ImGui.GetColorU32(T.TextMuted), chevronGlyph);

        var changesX = chevronX - 8f * scale - changesWidth;
        using (ChangelogMetaFont.Push())
            drawList.AddText(new Vector2(changesX, cursor.Y + (rowHeight - changesHeight) / 2f), ImGui.GetColorU32(T.TextMuted), changesLabel);

        if (hovered)
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
        if (clicked)
            changelogExpanded[entry.Version] = true;

        ImGui.Dummy(rowSize);
    }

    private static Vector2 DrawChangelogNewBadgeAt(float scale, Vector2 cursor)
    {
        var T = UiTheme.Active;
        var label = Loc.T("NEU", "NEW");
        var padding = new Vector2(7f * scale, 1f * scale);
        float textWidth, textHeight;
        using (ChangelogTagBadgeFont.Push())
        {
            var size = ImGui.CalcTextSize(label);
            textWidth = size.X;
            textHeight = size.Y;
        }
        var badgeSize = new Vector2(textWidth + padding.X * 2f, textHeight + padding.Y * 2f);
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(cursor, cursor + badgeSize, ImGui.GetColorU32(T.Accent), 3f * scale);
        using (ChangelogTagBadgeFont.Push())
            drawList.AddText(cursor + padding, ImGui.GetColorU32(T.TextOnAccent), label);
        return badgeSize;
    }

    private static float ChangelogBadgeHeight(float scale)
    {
        using (ChangelogTagBadgeFont.Push())
            return ImGui.GetTextLineHeight() + 2f * scale;
    }

    private static Vector2 DrawChangelogTagBadge(float scale, Vector2 cursor, string type)
    {
        var label = ChangelogTagLabel(type);
        var (fg, bg, line) = ChangelogTagColors(type);
        var padding = new Vector2(7f * scale, 1f * scale);
        float textWidth, textHeight;
        using (ChangelogTagBadgeFont.Push())
        {
            var size = ImGui.CalcTextSize(label);
            textWidth = size.X;
            textHeight = size.Y;
        }
        var badgeSize = new Vector2(textWidth + padding.X * 2f, textHeight + padding.Y * 2f);
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(cursor, cursor + badgeSize, ImGui.GetColorU32(bg), 3f * scale);
        drawList.AddRect(cursor, cursor + badgeSize, ImGui.GetColorU32(line), 3f * scale);
        using (ChangelogTagBadgeFont.Push())
            drawList.AddText(cursor + padding, ImGui.GetColorU32(fg), label);
        return badgeSize;
    }

    private static float ChangelogTagColumnWidth(float scale)
    {
        var maxWidth = 0f;
        using (ChangelogTagBadgeFont.Push())
        {
            foreach (var type in new[] { "new", "improved", "fixed", "removed" })
                maxWidth = MathF.Max(maxWidth, ImGui.CalcTextSize(ChangelogTagLabel(type)).X);
        }
        return maxWidth + 7f * scale * 2f + 10f * scale;
    }

    private static string ChangelogTagLabel(string type) => type switch
    {
        "new" => Loc.T("NEU", "NEW"),
        "fixed" => Loc.T("BEHOBEN", "FIXED"),
        "removed" => Loc.T("ENTFERNT", "REMOVED"),
        _ => Loc.T("VERBESSERT", "IMPROVED"),
    };

    private static (Vector4 Fg, Vector4 Bg, Vector4 Line) ChangelogTagColors(string type)
    {
        var T = UiTheme.Active;
        return type switch
        {
            "new" => (T.OkFg, T.OkBg, T.OkLine),
            "fixed" => (T.InfoFg, T.InfoBg, T.InfoLine),
            "removed" => (T.ErrFg, T.ErrBg, T.ErrLine),
            _ => (T.WarnFg, T.WarnBg, T.WarnLine),
        };
    }

    private static string FormatChangelogDate(string date)
    {
        if (DateTime.TryParse(date, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            try
            {
                return parsed.ToString("d", new CultureInfo(Loc.T("de-DE", "en-US")));
            }
            catch (CultureNotFoundException)
            {
                return parsed.ToString("d", CultureInfo.InvariantCulture);
            }
        }
        return date;
    }

    // ---- Seite "Über" - 1:1 derselbe Aufbau wie CodexMenuWindow.DrawAboutPage (Logo, Name,
    // Untertitel, Versions-Badge, Trenn-Ornament, Karte, "Follow the Journey"-Abschnitt), mit dem
    // NEUEN Ocean-Logo (Fisch im Ring) statt Codex' Kompass-Siegel und ohne Ko-fi-Knopf (kein
    // Gegenstück bei Big Fish Helper) - Schriftgrößen/Abstände/Ränder sonst identisch. ----

    /// <summary>Lokale X-Position und Breite der Inhalts-Box - 1:1 wie CodexMenuWindow.GetAboutBoxBounds (60px Rand links, 80px rechts, NICHT symmetrisch).</summary>
    private static (float LocalX, float Width) GetAboutBoxBounds(float scale)
    {
        const float leftMargin = 60f;
        const float rightMargin = 80f;
        var localX = ImGui.GetCursorPosX() + leftMargin * scale;
        var width = ImGui.GetContentRegionAvail().X - (leftMargin + rightMargin) * scale;
        return (localX, width);
    }

    private void DrawAboutPage(float scale)
    {
        var T = UiTheme.Active;
        var (boxX, boxWidth) = GetAboutBoxBounds(scale);

        ImGui.SetCursorPosX(boxX);
        DrawAboutSealLogo(scale, 150f, boxWidth);

        ImGui.Dummy(new Vector2(0f, 24f * scale));

        using (AboutTitleFont.Push())
        {
            ImGui.SetCursorPosX(boxX);
            UiWidgetsExtra.CenterNext(ImGui.CalcTextSize(PluginDisplayName).X, boxWidth);
            ImGui.TextColored(T.TextHeading, PluginDisplayName);
        }

        ImGui.SetCursorPosY(ImGui.GetCursorPosY() - 7f * scale);
        var tagline = Loc.T("Automatisiert das Angeln von Big Fish.", "Automates fishing for Big Fish.");
        using (SubtitleFont.Push())
        {
            ImGui.SetCursorPosX(boxX);
            UiWidgetsExtra.CenterNext(ImGui.CalcTextSize(tagline).X, boxWidth);
            ImGui.TextColored(T.TextSecondary, tagline);
        }

        ImGui.Dummy(new Vector2(0f, 3f * scale));
        DrawAboutVersionBadge(scale, boxX, boxWidth);

        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + 4f * scale);
        ImGui.SetCursorPosX(boxX);
        UiWidgetsExtra.CenterNext(UiMetrics.DividerOrnamentWidth * scale, boxWidth);
        UiWidgets.DrawDividerOrnament(Ornaments, scale);

        ImGui.Dummy(new Vector2(0f, 6f * scale));
        DrawAboutCard(scale);

        ImGui.Dummy(new Vector2(0f, 24f * scale));
        DrawAboutFollowSection(scale);
    }

    /// <summary>Bildmarke oben auf der About-Seite - 1:1 wie CodexWidgets.SealLogo (zwei konzentrische Ringe um das Logo), nur mit dem Ocean-Fisch statt Codex' Kompass.</summary>
    private static void DrawAboutSealLogo(float scale, float size, float availableWidth)
    {
        var T = UiTheme.Active;
        size *= scale;
        float inner = 5f * scale, outer = 6f * scale;
        var total = size + (inner + outer) * 2f;
        UiWidgetsExtra.CenterNext(total, availableWidth);
        var p = ImGui.GetCursorScreenPos();
        var c = p + new Vector2(total / 2f);
        var dl = ImGui.GetWindowDrawList();

        ImGui.SetCursorScreenPos(c - new Vector2(size / 2f));
        Ornaments.DrawSidebarLogo(size);

        dl.AddCircle(c, size / 2f + inner, ImGui.GetColorU32(T.Accent), 64, 1f * scale);
        dl.AddCircle(c, size / 2f + inner + outer, ImGui.GetColorU32(T.LineControl), 64, 1f * scale);
        ImGui.SetCursorScreenPos(p);
        ImGui.Dummy(new Vector2(total, total));
    }

    private static void DrawAboutVersionBadge(float scale, float localX, float availableWidth)
    {
        var T = UiTheme.Active;
        var text = string.Format(Loc.T("Version {0}", "Version {0}"), VersionText);
        var padding = new Vector2(10f * scale, 2f * scale);

        float textWidth, textHeight;
        using (AboutVersionBadgeFont.Push())
        {
            var size = ImGui.CalcTextSize(text);
            textWidth = size.X;
            textHeight = size.Y;
        }

        var badgeSize = new Vector2(textWidth + padding.X * 2f, textHeight + padding.Y * 2f);
        ImGui.SetCursorPosX(localX);
        UiWidgetsExtra.CenterNext(badgeSize.X, availableWidth);
        var cursor = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();
        dl.AddRect(cursor, cursor + badgeSize, ImGui.GetColorU32(T.LineControl), 3f * scale);
        using (AboutVersionBadgeFont.Push())
            dl.AddText(cursor + padding, ImGui.GetColorU32(T.TextSecondary), text);

        ImGui.Dummy(badgeSize);
    }

    /// <summary>Karte mit Raute+Icon, Titel und Fließtext - 1:1 Aufbau/Innenabstände wie CodexMenuWindow.DrawAboutCard, ohne Ko-fi-Knopf (kein Gegenstück).</summary>
    private void DrawAboutCard(float scale)
    {
        var T = UiTheme.Active;
        var paddingTop = 22f * scale;
        var paddingSide = 30f * scale;
        var paddingBottom = 24f * scale;

        var (boxX, cardWidth) = GetAboutBoxBounds(scale);
        var contentWidth = cardWidth - paddingSide * 2f;

        ImGui.SetCursorPosX(boxX);
        var cardOrigin = ImGui.GetCursorScreenPos();
        var contentLocalX = boxX + paddingSide;

        var dl = ImGui.GetWindowDrawList();
        dl.ChannelsSplit(2);
        dl.ChannelsSetCurrent(1);

        ImGui.SetCursorPosX(contentLocalX);
        ImGui.Dummy(new Vector2(0f, paddingTop));

        var diamondEdge = 38f * scale;
        var diamondBounds = diamondEdge * 1.41421356f;
        ImGui.SetCursorPosX(contentLocalX);
        UiWidgetsExtra.CenterNext(diamondBounds, contentWidth);
        var diamondCursor = ImGui.GetCursorScreenPos();
        var diamondCenter = diamondCursor + new Vector2(diamondBounds / 2f);
        UiWidgetsExtra.Diamond(diamondCenter, diamondEdge, ImGui.GetColorU32(T.BgSelected), ImGui.GetColorU32(T.Accent));
        var iconSize = 16f * scale;
        UiIcons.Draw(UiIcon.Heart, diamondCenter - new Vector2(iconSize / 2f), iconSize, ImGui.GetColorU32(T.Accent));
        ImGui.Dummy(new Vector2(diamondBounds, diamondBounds));

        ImGui.Dummy(new Vector2(0f, 8f * scale));
        var cardTitle = Loc.T("Von Hand geangelt", "Hooked by Hand");
        using (AboutCardTitleFont.Push())
        {
            ImGui.SetCursorPosX(contentLocalX);
            UiWidgetsExtra.CenterNext(ImGui.CalcTextSize(cardTitle).X, contentWidth);
            ImGui.TextColored(T.TextCardTitle, cardTitle);
        }

        ImGui.Dummy(new Vector2(0f, 10f * scale));
        var cardText = Loc.T(
            "Big Fish Helper entsteht in der Freizeit, Update für Update. Wenn er dir beim Angeln ein paar Wege erspart hat, hält ein Kaffee auf Ko-fi die Automation am Laufen – ganz freiwillig. Danke, dass du mit an Bord bist!",
            "Big Fish Helper is built in spare time, one update at a time. If it has saved you a few trips while fishing, a coffee on Ko-fi keeps the automation running – entirely optional. Thanks for being on board!");
        using (AboutBodyFont.Push())
            UiWidgetsExtra.CenteredWrappedText(cardText, 520f * scale, ImGui.GetColorU32(T.TextSecondary), contentLocalX, contentWidth);

        ImGui.Dummy(new Vector2(0f, 6f * scale));
        ImGui.SetCursorPosX(contentLocalX);
        UiWidgetsExtra.CenterNext(MeasureAboutKofiButtonSize(scale).X, contentWidth);
        DrawAboutKofiButton(scale);

        ImGui.Dummy(new Vector2(0f, paddingBottom));

        var cardMax = new Vector2(cardOrigin.X + cardWidth, ImGui.GetCursorScreenPos().Y);
        dl.ChannelsSetCurrent(0);
        dl.AddRectFilled(cardOrigin, cardMax, ImGui.GetColorU32(T.BgCard), UiMetrics.CardRadius);
        dl.AddRect(cardOrigin, cardMax, ImGui.GetColorU32(T.LineCard), UiMetrics.CardRadius);
        dl.ChannelsMerge();
    }

    private static Vector2 MeasureAboutKofiButtonSize(float scale)
    {
        var label = Loc.T("Auf Ko-fi unterstützen", "Support on Ko-fi");
        var padding = new Vector2(20f * scale, 9f * scale);
        var iconGap = 8f * scale;
        var iconWidth = 15f * scale;

        float textWidth, textHeight;
        using (AboutKofiLabelFont.Push())
        {
            var size = ImGui.CalcTextSize(label);
            textWidth = size.X;
            textHeight = size.Y;
        }

        return new Vector2(iconWidth + iconGap + textWidth + padding.X * 2f, textHeight + padding.Y * 2f);
    }

    /// <summary>1:1 wie CodexMenuWindow.DrawAboutKofiButton, nur mit von Hand gezeichnetem Becher-Icon statt FontAwesomeIcon.MugHot.</summary>
    private static void DrawAboutKofiButton(float scale)
    {
        var T = UiTheme.Active;
        var label = Loc.T("Auf Ko-fi unterstützen", "Support on Ko-fi");
        var padding = new Vector2(20f * scale, 9f * scale);
        var iconGap = 8f * scale;
        var iconWidth = 15f * scale;

        float textWidth, textHeight;
        using (AboutKofiLabelFont.Push())
        {
            var size = ImGui.CalcTextSize(label);
            textWidth = size.X;
            textHeight = size.Y;
        }

        var buttonSize = new Vector2(iconWidth + iconGap + textWidth + padding.X * 2f, textHeight + padding.Y * 2f);
        var cursor = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton("##OceanAboutKofi", buttonSize);
        var hovered = ImGui.IsItemHovered();

        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(cursor, cursor + buttonSize, ImGui.GetColorU32(T.Accent with { W = hovered ? 0.85f : 1f }), UiMetrics.ControlRadius * scale);

        var contentCursor = cursor + new Vector2(padding.X, (buttonSize.Y - iconWidth) / 2f);
        UiIcons.Draw(UiIcon.Mug, contentCursor, iconWidth, ImGui.GetColorU32(T.TextOnAccent));
        contentCursor.X += iconWidth + iconGap;
        contentCursor.Y = cursor.Y + (buttonSize.Y - textHeight) / 2f;
        using (AboutKofiLabelFont.Push())
            dl.AddText(contentCursor, ImGui.GetColorU32(T.TextOnAccent), label);

        if (clicked)
            Util.OpenLink(KofiUrl);
        if (hovered)
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
    }

    /// <summary>"Folge dem Fang"-Abschnitt - 1:1 wie CodexMenuWindow.DrawAboutFollowSection (Trennlinie mit Beschriftung, Hinweistext, zwei Rahmen-Knöpfe zu den GitHub-Issues).</summary>
    private void DrawAboutFollowSection(float scale)
    {
        var T = UiTheme.Active;
        var (localX, cardWidth) = GetAboutBoxBounds(scale);

        ImGui.SetCursorPosX(localX);
        using (AboutDividerLabelFont.Push())
            UiWidgetsExtra.LabeledDivider(Loc.T("Folge dem Fang", "Follow the Catch").ToUpperInvariant(), cardWidth, cardWidth);

        ImGui.Dummy(new Vector2(0f, 10f * scale));
        var hint = Loc.T(
            "Einen Fehler gefunden oder eine Idee fürs nächste Update? Sag es auf GitHub.",
            "Found a bug or have an idea for the next update? Let me know on GitHub.");
        using (AboutHintFont.Push())
        {
            ImGui.SetCursorPosX(localX);
            UiWidgetsExtra.CenterNext(ImGui.CalcTextSize(hint).X, cardWidth);
            ImGui.TextColored(T.TextSecondary, hint);
        }

        ImGui.Dummy(new Vector2(0f, 8f * scale));
        var gap = 10f * scale;
        var bugSize = MeasureAboutLinkButtonSize(Loc.T("Fehler melden", "Report a bug"), scale);
        var featureSize = MeasureAboutLinkButtonSize(Loc.T("Feature vorschlagen", "Suggest a feature"), scale);
        var totalWidth = bugSize.X + gap + featureSize.X;

        ImGui.SetCursorPosX(localX);
        UiWidgetsExtra.CenterNext(totalWidth, cardWidth);
        DrawAboutLinkButton("##OceanAboutBug", UiIcon.Bug, Loc.T("Fehler melden", "Report a bug"), bugSize, $"{GitHubUrl}/issues/new?labels=bug");
        ImGui.SameLine(0f, gap);
        DrawAboutLinkButton("##OceanAboutFeature", UiIcon.Lightbulb, Loc.T("Feature vorschlagen", "Suggest a feature"), featureSize, $"{GitHubUrl}/issues/new?labels=enhancement");
    }

    private static Vector2 MeasureAboutLinkButtonSize(string label, float scale)
    {
        var padding = new Vector2(16f * scale, 7f * scale);
        var iconGap = 6f * scale;
        var iconWidth = 14f * scale;

        float textWidth, textHeight;
        using (AboutLinkButtonLabelFont.Push())
        {
            var size = ImGui.CalcTextSize(label);
            textWidth = size.X;
            textHeight = size.Y;
        }

        return new Vector2(iconWidth + iconGap + textWidth + padding.X * 2f, textHeight + padding.Y * 2f);
    }

    private static void DrawAboutLinkButton(string id, UiIcon icon, string label, Vector2 buttonSize, string url)
    {
        var T = UiTheme.Active;
        var scale = ImGuiHelpers.GlobalScale;
        var iconGap = 6f * scale;
        var iconSize = 14f * scale;

        float textWidth, textHeight;
        using (AboutLinkButtonLabelFont.Push())
        {
            var labelSize = ImGui.CalcTextSize(label);
            textWidth = labelSize.X;
            textHeight = labelSize.Y;
        }

        var cursor = ImGui.GetCursorScreenPos();
        var clicked = ImGui.InvisibleButton(id, buttonSize);
        var hovered = ImGui.IsItemHovered();

        var dl = ImGui.GetWindowDrawList();
        var bg = hovered ? T.BgSelected : new Vector4(0f, 0f, 0f, 0f);
        var border = hovered ? T.Accent : T.LineControl;
        dl.AddRectFilled(cursor, cursor + buttonSize, ImGui.GetColorU32(bg), UiMetrics.ControlRadius * scale);
        dl.AddRect(cursor, cursor + buttonSize, ImGui.GetColorU32(border), UiMetrics.ControlRadius * scale);

        var contentWidth = iconSize + iconGap + textWidth;
        var contentLeft = cursor.X + (buttonSize.X - contentWidth) / 2f;
        UiIcons.Draw(icon, new Vector2(contentLeft, cursor.Y + (buttonSize.Y - iconSize) / 2f), iconSize, ImGui.GetColorU32(T.TextHeading));
        var textPos = new Vector2(contentLeft + iconSize + iconGap, cursor.Y + (buttonSize.Y - textHeight) / 2f);
        using (AboutLinkButtonLabelFont.Push())
            dl.AddText(textPos, ImGui.GetColorU32(T.TextHeading), label);

        if (clicked)
            Util.OpenLink(url);
        if (hovered)
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
    }

    // ---- Seite "Big Fish" (Fischdaten) - neu gebaut nach eigener Aufgabenstellung, Daten/Berechnungen/
    // Logik unverändert aus MainWindow.DrawFishDataPage/DrawFishTable übernommen (FishWindows/
    // BigFishData/FishCatchState/FishingPositionStore/AutoHookPresets/GameActions/FishingAutomation),
    // nur Darstellung/Bedienung neu im Ocean-Stil. ----

    private static string FishDataExpansionLabel(BigFishExpansion expansion) => expansion switch
    {
        BigFishExpansion.ARealmReborn => "ARR",
        BigFishExpansion.Heavensward => "HW",
        BigFishExpansion.Stormblood => "SB",
        BigFishExpansion.Shadowbringers => "SHB",
        BigFishExpansion.Endwalker => "EW",
        BigFishExpansion.Dawntrail => "DT",
        _ => expansion.ToString(),
    };

    private static string FishDataDisplayName(BigFish fish, ExcelSheet<Item> itemSheet) =>
        itemSheet.TryGetRow(fish.ItemId, out var item) ? item.Name.ToString() : $"#{fish.ItemId}";

    private ISharedImmediateTexture GetFishDataIcon(uint iconId)
    {
        if (!fishDataIconCache.TryGetValue(iconId, out var texture))
        {
            texture = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(iconId));
            fishDataIconCache[iconId] = texture;
        }
        return texture;
    }

    private void DrawFishDataPage(float scale)
    {
        var T = UiTheme.Active;
        var availWidth = ImGui.GetContentRegionAvail().X - PluginsPageRightMargin * scale;

        using (PageTitleFont.Push())
            ImGui.TextColored(T.TextHeading, Loc.T("Fischdaten", "Fish Data"));
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() - 2f * scale);
        using (SubtitleFont.Push())
            ImGui.TextColored(T.TextSecondary, Loc.T("Big Fish nach Erweiterung und wann sie als Nächstes beißen.", "Big Fish by expansion and when they bite next."));
        UiWidgets.DrawDividerOrnament(Ornaments, scale);

        ImGui.Dummy(new Vector2(0f, 26f * scale));
        DrawFishDataTabsRow(scale, availWidth);
        ImGui.Dummy(new Vector2(0f, 6f * scale));
        DrawFishDataTable(scale);
    }

    /// <summary>Erweiterungs-Tabs links, Suchfeld + direkt rechts daneben die "Gefangene ausblenden"-Box rechtsbündig
    /// auf derselben Zeile (Nutzeranforderung) - die Box wird dabei exakt auf die Höhe des Suchfelds verkleinert
    /// (Nutzeranforderung).</summary>
    private void DrawFishDataTabsRow(float scale, float availWidth)
    {
        var config = plugin.Configuration;
        var rowStart = ImGui.GetCursorPos();
        var rowStartScreen = ImGui.GetCursorScreenPos();

        var isFirst = true;
        foreach (var (expansion, fishArr) in BigFishData.ByExpansion)
        {
            var count = config.HideCaughtFish ? fishArr.Count(f => !FishCatchState.IsCaught(f.ItemId)) : fishArr.Length;
            var selected = fishDataActiveTab == expansion;
            if (UiNav.TabItem($"##OceanFishTab_{expansion}", FishDataExpansionLabel(expansion), count.ToString(CultureInfo.InvariantCulture), selected, scale, FishDataTabFont, FishDataTabCountFont, isFirst, 22f * scale))
                fishDataActiveTab = expansion;
            isFirst = false;
        }

        var tabsHeight = ImGui.GetCursorScreenPos().Y - rowStartScreen.Y;

        DrawFishDataSearchAndToggle(scale, rowStart, availWidth, tabsHeight);

        ImGui.SetCursorPos(rowStart);
        ImGui.Dummy(new Vector2(availWidth, tabsHeight));
    }

    /// <summary>Suchfeld + direkt rechts daneben die "Gefangene ausblenden"-Box, beide rechtsbündig in der Tab-Zeile
    /// verankert (Nutzeranforderung) - zeichnet absolut positioniert, beeinflusst den Haupt-Cursor nicht (Aufrufer
    /// setzt ihn danach selbst zurück).</summary>
    private void DrawFishDataSearchAndToggle(float scale, Vector2 rowStart, float availWidth, float rowHeight)
    {
        var T = UiTheme.Active;
        var config = plugin.Configuration;
        var rowRightX = rowStart.X + availWidth;

        float searchTextHeight;
        using (FishDataMetaFont.Push())
            searchTextHeight = ImGui.GetTextLineHeight();
        var searchFramePadY = 7f * scale;
        var searchHeight = searchTextHeight + searchFramePadY * 2f;

        var toggleLabel = Loc.T("Gefangene ausblenden", "Hide caught fish");
        float toggleLabelWidth, toggleLabelHeight;
        using (FishDataBoldFont.Push())
        {
            var size = ImGui.CalcTextSize(toggleLabel);
            toggleLabelWidth = size.X;
            toggleLabelHeight = size.Y;
        }
        var boxPadX = 12f * scale;
        var boxGap = 8f * scale;
        var toggleSize = new Vector2(UiMetrics.ToggleWidth * scale, UiMetrics.ToggleHeight * scale);
        // Box-Höhe exakt identisch zur Suchfeld-Höhe (Nutzeranforderung), statt eigener Padding-basierter Höhe.
        var boxSize = new Vector2(boxPadX * 2f + toggleLabelWidth + boxGap + toggleSize.X, searchHeight);

        var searchWidth = 220f * scale;
        var searchToBoxGap = 12f * scale;
        var boxX = rowRightX - boxSize.X;
        var searchX = boxX - searchToBoxGap - searchWidth;
        var topY = rowStart.Y + (rowHeight - searchHeight) / 2f - 6f * scale;

        ImGui.SetCursorPos(new Vector2(searchX, topY));
        var searchCursor = ImGui.GetCursorScreenPos();
        ImGui.PushStyleColor(ImGuiCol.FrameBg, T.BgInput);
        ImGui.PushStyleColor(ImGuiCol.Border, T.LineFrame);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 1f * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, UiMetrics.ControlRadius * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(26f * scale, searchFramePadY));
        ImGui.SetNextItemWidth(searchWidth);
        using (FishDataMetaFont.Push())
            ImGui.InputTextWithHint("##OceanFishSearch", Loc.T("Fisch suchen …", "Search fish …"), ref fishDataSearchFilter, 100);
        ImGui.PopStyleVar(3);
        ImGui.PopStyleColor(2);
        var searchIconSize = 13f * scale;
        UiIcons.Draw(UiIcon.Search, searchCursor + new Vector2(8f * scale, (searchHeight - searchIconSize) / 2f), searchIconSize, ImGui.GetColorU32(T.TextMuted));

        ImGui.SetCursorPos(new Vector2(boxX, topY));
        var boxCursor = ImGui.GetCursorScreenPos();
        var dl = ImGui.GetWindowDrawList();
        dl.AddRectFilled(boxCursor, boxCursor + boxSize, ImGui.GetColorU32(T.BgCard), 4f * scale);
        dl.AddRect(boxCursor, boxCursor + boxSize, ImGui.GetColorU32(T.LineCard), 4f * scale);
        using (FishDataBoldFont.Push())
            dl.AddText(boxCursor + new Vector2(boxPadX, (boxSize.Y - toggleLabelHeight) / 2f), ImGui.GetColorU32(T.TextPrimary), toggleLabel);

        ImGui.SetCursorScreenPos(boxCursor + new Vector2(boxPadX + toggleLabelWidth + boxGap, (boxSize.Y - toggleSize.Y) / 2f));
        var hideCaught = config.HideCaughtFish;
        if (UiWidgets.Toggle("##OceanHideCaughtFish", ref hideCaught, scale))
        {
            config.HideCaughtFish = hideCaught;
            config.Save();
        }
    }

    private void DrawFishDataTable(float scale)
    {
        var T = UiTheme.Active;
        var config = plugin.Configuration;
        var isDev = Plugin.PluginInterface.IsDev;
        var now = DateTime.UtcNow;

        var itemSheet = Plugin.DataManager.GetExcelSheet<Item>();
        var presetNames = AutoHookPresets.GetNames();

        var entry = BigFishData.ByExpansion.First(e => e.Expansion == fishDataActiveTab);
        var fishList = entry.Fish;

        var rows = fishList
            .Select(fish => (Fish: fish, Caught: FishCatchState.IsCaught(fish.ItemId), Window: FishWindows.GetCurrentOrNext(fish, now)))
            .Where(r => !config.HideCaughtFish || !r.Caught)
            .Where(r => string.IsNullOrWhiteSpace(fishDataSearchFilter) || FishDataDisplayName(r.Fish, itemSheet).Contains(fishDataSearchFilter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(r => FishWindows.IsAlwaysAvailable(r.Fish) ? 1 : r.Window != null && r.Window.Value.IsActive(now) ? 0 : 2)
            .ThenBy(r => r.Window == null ? DateTime.MaxValue : r.Window.Value.IsActive(now) ? DateTime.MinValue : r.Window.Value.StartUtc)
            .ToList();

        var caughtCount = fishList.Count(f => FishCatchState.IsCaught(f.ItemId));

        var width = ImGui.GetContentRegionAvail().X - PluginsPageRightMargin * scale;
        var height = MathF.Max(200f * scale, ImGui.GetWindowPos().Y + ImGui.GetWindowSize().Y - ImGui.GetCursorScreenPos().Y - 20f * scale);
        var headerHeight = 34f * scale;
        var columnCount = isDev ? 9 : 8;

        ImGui.PushStyleColor(ImGuiCol.ChildBg, T.BgCard);
        ImGui.PushStyleColor(ImGuiCol.Border, T.LineCard);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, UiMetrics.CardRadius * scale);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildBorderSize, 1f * scale);
        ImGui.BeginChild("##OceanFishDataCard", new Vector2(width, height), true);

        var innerWidth = ImGui.GetContentRegionAvail().X;
        // Feste Tabellenbreite abzüglich Scrollbar-Breite für Kopf- UND Körper-Tabelle - sonst zieht
        // die Körper-Tabelle (scrollender Child) ihre Spalten automatisch um die Scrollbar-Breite
        // schmäler als die Kopf-Tabelle (kein Scrollbar), was die letzte (DEV-)Spalte gegenüber ihrem
        // Header verschiebt (Nutzer-Report: "Dev Inhalt ist weiterhin nicht bündig mit dem DEV Header").
        var tableWidth = innerWidth - ImGui.GetStyle().ScrollbarSize;

        if (ImGui.BeginTable("##OceanFishDataHeaderTable", columnCount, ImGuiTableFlags.None, new Vector2(tableWidth, headerHeight)))
        {
            FishDataSetupColumns(scale, isDev);
            DrawFishDataTableHeader(scale, headerHeight, isDev);
            ImGui.EndTable();
        }
        UiWidgets.DrawDataTableHeaderDivider(tableWidth);

        var bodyHeight = ImGui.GetContentRegionAvail().Y;
        ImGui.BeginChild("##OceanFishDataBody", new Vector2(innerWidth, bodyHeight), false);

        if (rows.Count == 0)
        {
            var allCaughtAndHidden = config.HideCaughtFish && caughtCount == fishList.Length && fishList.Length > 0 && string.IsNullOrWhiteSpace(fishDataSearchFilter);
            var emptyText = allCaughtAndHidden
                ? Loc.T("Alle Big Fish dieser Erweiterung gefangen!", "All Big Fish of this expansion caught!")
                : Loc.T("Keine Fische passen zu den aktuellen Filtern.", "No fish match the current filters.");
            ImGui.Dummy(new Vector2(0f, bodyHeight / 2f - 12f * scale));
            using (FishDataMetaFont.Push())
            {
                UiWidgetsExtra.CenterNext(ImGui.CalcTextSize(emptyText).X + (allCaughtAndHidden ? 20f * scale : 0f), innerWidth);
                if (allCaughtAndHidden)
                {
                    var checkCursor = ImGui.GetCursorScreenPos();
                    UiIcons.Draw(UiIcon.Check, checkCursor, 13f * scale, ImGui.GetColorU32(T.Accent));
                    ImGui.SetCursorScreenPos(checkCursor + new Vector2(18f * scale, -2f * scale));
                }
                ImGui.TextColored(T.TextMuted, emptyText);
            }
        }
        else if (ImGui.BeginTable("##OceanFishDataBodyTable", columnCount, ImGuiTableFlags.None, new Vector2(tableWidth, 0f)))
        {
            FishDataSetupColumns(scale, isDev);
            var rowHeight = 36f * scale;
            var clipper = new ImGuiListClipper();
            clipper.Begin(rows.Count, rowHeight);
            while (clipper.Step())
            {
                for (var i = clipper.DisplayStart; i < clipper.DisplayEnd; i++)
                    DrawFishDataRow(scale, rows[i], i, rowHeight, isDev, itemSheet, presetNames, now, tableWidth);
            }
            clipper.End();
            ImGui.EndTable();
        }
        ImGui.EndChild();

        ImGui.EndChild();
        ImGui.PopStyleVar(2);
        ImGui.PopStyleColor(2);
    }

    private static void FishDataSetupColumns(float scale, bool isDev)
    {
        ImGui.TableSetupColumn("##OceanFishIcon", ImGuiTableColumnFlags.WidthFixed, 24f * scale);
        ImGui.TableSetupColumn("##OceanFishName", ImGuiTableColumnFlags.WidthStretch, 1.6f);
        ImGui.TableSetupColumn("##OceanFishBait", ImGuiTableColumnFlags.WidthFixed, 104f * scale);
        ImGui.TableSetupColumn("##OceanFishWindow", ImGuiTableColumnFlags.WidthFixed, 150f * scale);
        ImGui.TableSetupColumn("##OceanFishDuration", ImGuiTableColumnFlags.WidthFixed, 64f * scale);
        ImGui.TableSetupColumn("##OceanFishRarity", ImGuiTableColumnFlags.WidthFixed, 58f * scale);
        ImGui.TableSetupColumn("##OceanFishPrepTimer", ImGuiTableColumnFlags.WidthFixed, 104f * scale);
        ImGui.TableSetupColumn("##OceanFishAutoHook", ImGuiTableColumnFlags.WidthStretch, 1.2f);
        if (isDev)
            ImGui.TableSetupColumn("##OceanFishDev", ImGuiTableColumnFlags.WidthFixed, 86f * scale);
    }

    private static void DrawFishDataTableHeader(float scale, float headerHeight, bool isDev)
    {
        var T = UiTheme.Active;
        ImGui.TableNextRow(ImGuiTableRowFlags.Headers, headerHeight);
        ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(T.BgStatus));

        ImGui.TableSetColumnIndex(1);
        UiWidgets.DrawDataTableHeaderCell(Loc.T("FISCH", "FISH"), headerHeight, 8f * scale, FishDataHeaderFont);
        ImGui.TableSetColumnIndex(2);
        UiWidgets.DrawDataTableHeaderCell(Loc.T("KÖDER", "BAIT"), headerHeight, 8f * scale, FishDataHeaderFont);
        ImGui.TableSetColumnIndex(3);
        UiWidgets.DrawDataTableHeaderCell(Loc.T("NÄCHSTES FENSTER", "NEXT WINDOW"), headerHeight, 8f * scale, FishDataHeaderFont);
        ImGui.TableSetColumnIndex(4);
        UiWidgets.DrawDataTableHeaderCell(Loc.T("DAUER", "DURATION"), headerHeight, 8f * scale, FishDataHeaderFont, rightAlign: true);
        ImGui.TableSetColumnIndex(5);
        UiWidgets.DrawDataTableHeaderCell(Loc.T("RARITÄT", "RARITY"), headerHeight, 8f * scale, FishDataHeaderFont, rightAlign: true);
        ImGui.TableSetColumnIndex(6);
        UiWidgets.DrawDataTableHeaderCell(Loc.T("PREP TIMER", "PREP TIMER"), headerHeight, 8f * scale, FishDataHeaderFont);
        ImGui.TableSetColumnIndex(7);
        UiWidgets.DrawDataTableHeaderCell(Loc.T("AUTOHOOK", "AUTOHOOK"), headerHeight, 8f * scale, FishDataHeaderFont);
        if (isDev)
        {
            ImGui.TableSetColumnIndex(8);
            var colStart = ImGui.GetCursorScreenPos();
            UiWidgets.DrawDashedLine(new Vector2(colStart.X, colStart.Y - (headerHeight - ImGui.GetTextLineHeight()) / 2f), new Vector2(colStart.X, colStart.Y + headerHeight), ImGui.GetColorU32(FishDataDevBorder), 1f * scale, 3f * scale, 3f * scale);
            using (FishDataHeaderFont.Push())
            {
                ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 8f * scale);
                ImGui.SetCursorPosY(ImGui.GetCursorPosY() + (headerHeight - ImGui.GetTextLineHeight()) / 2f);
                UiWidgets.DrawSpacedText(Loc.T("DEV", "DEV"), FishDataRarityYellow, 1f);
            }
        }
    }

    private void DrawFishDataRow(float scale, (BigFish Fish, bool Caught, FishWindow? Window) row, int rowIndex, float rowHeight, bool isDev,
        ExcelSheet<Item> itemSheet, IReadOnlyList<string> presetNames, DateTime now, float tableWidth)
    {
        var T = UiTheme.Active;
        var config = plugin.Configuration;
        var fish = row.Fish;
        var isActive = (row.Window is { } w && w.IsActive(now)) || FishWindows.IsAlwaysAvailable(fish);

        ImGui.TableNextRow(ImGuiTableRowFlags.None, rowHeight);
        var rowTop = ImGui.GetCursorScreenPos();

        var zebraOn = rowIndex % 2 == 1;
        var rowBg = isActive ? FishDataActiveGreen with { W = 0.06f } : zebraOn ? new Vector4(1f, 1f, 1f, 0.015f) : new Vector4(0f, 0f, 0f, 0f);
        ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(rowBg));

        // Zeilen-Hover nur als reines Hintergrund-Rechteck (IsMouseHoveringRect), KEIN ImGui.Selectable
        // mit SpanAllColumns mehr - das spannte sein Klickfeld über die ganze Zeile inkl. späterer
        // Spalten und blockierte dadurch Klicks auf die Checkbox/den AutoHook-Combo in anderen Spalten
        // (Nutzer-Report: "Autohook Preset ist zurzeit nicht anklickbar").
        if (ImGui.IsMouseHoveringRect(rowTop, new Vector2(rowTop.X + tableWidth, rowTop.Y + rowHeight)))
            ImGui.GetWindowDrawList().AddRectFilled(rowTop, new Vector2(rowTop.X + tableWidth, rowTop.Y + rowHeight), ImGui.GetColorU32(T.BgSelected with { W = 0.6f }));

        ImGui.TableSetColumnIndex(0);
        DrawFishDataCheckboxCell(scale, fish, config, rowHeight);

        ImGui.TableSetColumnIndex(1);
        DrawFishDataNameCell(scale, fish, row.Caught, itemSheet, rowHeight);

        ImGui.TableSetColumnIndex(2);
        DrawFishDataBaitCell(scale, fish, itemSheet, rowHeight);

        ImGui.TableSetColumnIndex(3);
        DrawFishDataWindowCell(scale, fish, row.Window, now, rowHeight);

        ImGui.TableSetColumnIndex(4);
        DrawFishDataDurationCell(scale, fish, row.Window, rowHeight);

        ImGui.TableSetColumnIndex(5);
        DrawFishDataRarityCell(scale, fish, now, rowHeight);

        ImGui.TableSetColumnIndex(6);
        DrawFishDataPrepTimerCell(scale, fish, config, row.Window, now, rowHeight);

        ImGui.TableSetColumnIndex(7);
        DrawFishDataAutoHookCell(scale, fish, config, presetNames, rowHeight);

        if (isDev)
        {
            ImGui.TableSetColumnIndex(8);
            var colStart = ImGui.GetCursorScreenPos();
            UiWidgets.DrawDashedLine(new Vector2(colStart.X, rowTop.Y), new Vector2(colStart.X, rowTop.Y + rowHeight), ImGui.GetColorU32(FishDataDevBorder), 1f * scale, 3f * scale, 3f * scale);
            DrawFishDataDevCell(scale, fish, itemSheet, rowHeight);
        }

        ImGui.GetWindowDrawList().AddLine(new Vector2(rowTop.X, rowTop.Y + rowHeight), new Vector2(rowTop.X + tableWidth, rowTop.Y + rowHeight), ImGui.GetColorU32(T.LineRow));
    }

    /// <summary>Checkbox statt Fisch-Icon (Nutzeranforderung) - 1:1 dieselbe Freischalt-Logik wie MainWindow.DrawFishTable's Spalte 0:
    /// nur anklickbar, wenn eine Angel-Position gespeichert UND ein AutoHook-Preset gewählt ist.</summary>
    private static void DrawFishDataCheckboxCell(float scale, BigFish fish, Configuration config, float rowHeight)
    {
        var supported = FishingPositionStore.GetAll(fish.ItemId).Count > 0;
        var hasPreset = config.FishAutoHookPresets.TryGetValue(fish.ItemId, out var selectedPreset) && !string.IsNullOrEmpty(selectedPreset);
        var canEnable = supported && hasPreset;
        var enabled = canEnable && config.EnabledFish.Contains(fish.ItemId);

        var checkboxSize = ImGui.GetFrameHeight();
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + (ImGui.GetContentRegionAvail().X - checkboxSize) / 2f + 6f * scale);
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + (rowHeight - checkboxSize) / 2f);

        if (!canEnable)
            ImGui.BeginDisabled();
        UiWidgets.PushInputStyle(scale);
        if (ImGui.Checkbox($"##OceanFishEnabled_{fish.ItemId}", ref enabled))
        {
            if (enabled)
                config.EnabledFish.Add(fish.ItemId);
            else
                config.EnabledFish.Remove(fish.ItemId);
            config.Save();
        }
        UiWidgets.PopInputStyle();
        if (!canEnable)
            ImGui.EndDisabled();

        if (ImGui.IsItemHovered(canEnable ? ImGuiHoveredFlags.None : ImGuiHoveredFlags.AllowWhenDisabled))
        {
            ImGui.SetTooltip(!supported
                ? Loc.T("Aktuell nicht unterstützt (keine Angel-Position gespeichert).", "Currently unsupported (no fishing position saved).")
                : !hasPreset
                    ? Loc.T("Bitte zuerst ein AutoHook-Preset wählen.", "Please select an AutoHook preset first.")
                    : Loc.T("Für diesen Fisch angeln.", "Go for this fish."));
        }
    }

    private void DrawFishDataNameCell(float scale, BigFish fish, bool caught, ExcelSheet<Item> itemSheet, float rowHeight)
    {
        var T = UiTheme.Active;
        var name = FishDataDisplayName(fish, itemSheet);
        var atCastablePosition = plugin.Automation.IsAtCastablePosition(fish);

        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 8f * scale);
        var maxWidth = ImGui.GetContentRegionAvail().X - (caught ? 22f * scale : 0f);
        string truncated;
        using (FishDataBoldFont.Push())
            truncated = TruncateToWidth(name, maxWidth);

        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + (rowHeight - ImGui.GetTextLineHeight()) / 2f);
        using (FishDataBoldFont.Push())
            ImGui.TextColored(atCastablePosition ? UiTheme.Active.Accent : T.TextHeading, truncated);
        if (truncated != name && ImGui.IsItemHovered())
            ImGui.SetTooltip(name);

        if (caught)
        {
            ImGui.SameLine(0f, 6f * scale);
            var checkSize = 13f * scale;
            var checkCursor = ImGui.GetCursorScreenPos();
            UiIcons.Draw(UiIcon.Check, checkCursor + new Vector2(0f, (ImGui.GetTextLineHeight() - checkSize) / 2f), checkSize, ImGui.GetColorU32(T.Accent));
        }
    }

    private void DrawFishDataBaitCell(float scale, BigFish fish, ExcelSheet<Item> itemSheet, float rowHeight)
    {
        // Vollständig über Bildschirmkoordinaten statt ImGui.SameLine-Verkettung gezeichnet (mehrere
        // Köder hintereinander) - SameLine's "Y der aktuellen Zeile" ist nach manuellem SetCursorPosY
        // nicht mehr zuverlässig, siehe Session-Lehre zu Spalten-/Zeilen-Positionierung.
        var T = UiTheme.Active;
        var cellStart = ImGui.GetCursorScreenPos();
        var x = cellStart.X + 8f * scale;
        var iconSize = 22f * scale;
        var dl = ImGui.GetWindowDrawList();
        var any = false;

        foreach (var baitId in fish.BaitIds)
        {
            if (!itemSheet.TryGetRow(baitId, out var bait))
                continue;
            any = true;

            var count = GameActions.GetInventoryItemCount(baitId);
            var dimmed = count == 0;
            var y = cellStart.Y + (rowHeight - iconSize) / 2f;

            dl.AddRect(new Vector2(x, y), new Vector2(x + iconSize, y + iconSize), ImGui.GetColorU32(T.LineFrame), 3f * scale);
            if (bait.Icon != 0)
            {
                var wrap = GetFishDataIcon(bait.Icon).GetWrapOrEmpty();
                dl.AddImageRounded(wrap.Handle, new Vector2(x, y), new Vector2(x + iconSize, y + iconSize), Vector2.Zero, Vector2.One,
                    ImGui.GetColorU32(new Vector4(1f, 1f, 1f, dimmed ? 0.55f : 1f)), 3f * scale);
            }

            ImGui.SetCursorScreenPos(new Vector2(x, cellStart.Y));
            ImGui.InvisibleButton($"##OceanBaitIcon_{fish.ItemId}_{baitId}", new Vector2(iconSize, rowHeight));
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip($"{bait.Name}\n{Loc.T("Bestand", "Stock")}: {count}");

            x += iconSize + 3f * scale;

            var countText = count.ToString(CultureInfo.InvariantCulture);
            float countWidth, countHeight;
            using (FishDataSmallBoldFont.Push())
            {
                var size = ImGui.CalcTextSize(countText);
                countWidth = size.X;
                countHeight = size.Y;
            }
            using (FishDataSmallBoldFont.Push())
                dl.AddText(new Vector2(x, cellStart.Y + (rowHeight - countHeight) / 2f), ImGui.GetColorU32(dimmed ? FishDataCoralRed : T.TextMuted), countText);

            x += countWidth + 6f * scale;
        }

        ImGui.SetCursorScreenPos(cellStart);
        ImGui.Dummy(new Vector2(any ? x - cellStart.X : 0f, rowHeight));
    }

    private void DrawFishDataWindowCell(float scale, BigFish fish, FishWindow? window, DateTime now, float rowHeight)
    {
        var T = UiTheme.Active;
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 8f * scale);

        string text;
        Vector4 color;
        string? tooltip = null;
        if (FishWindows.IsAlwaysAvailable(fish))
        {
            text = Loc.T("Immer verfügbar", "Always available");
            color = FishDataActiveGreen;
        }
        else if (window == null)
        {
            text = Loc.T("Unbekannt", "Unknown");
            color = T.TextDim;
        }
        else if (window.Value.IsActive(now))
        {
            text = Loc.T($"Aktiv · {FormatCountdown(window.Value.EndUtc - now)} übrig", $"Active · {FormatCountdown(window.Value.EndUtc - now)} left");
            color = FishDataActiveGreen;
        }
        else
        {
            text = Loc.T($"in {FormatCountdown(window.Value.StartUtc - now)}", $"in {FormatCountdown(window.Value.StartUtc - now)}");
            color = FishDataTextButton;
            tooltip = window.Value.StartUtc.ToLocalTime().ToString("g");
        }

        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + (rowHeight - ImGui.GetTextLineHeight()) / 2f);
        using (FishDataBoldFont.Push())
            ImGui.TextColored(color, text);
        if (tooltip != null && ImGui.IsItemHovered())
            ImGui.SetTooltip(tooltip);
    }

    private static void DrawFishDataDurationCell(float scale, BigFish fish, FishWindow? window, float rowHeight)
    {
        var T = UiTheme.Active;
        var text = window == null || FishWindows.IsAlwaysAvailable(fish) ? "-" : FormatFishDataDuration(window.Value.EndUtc - window.Value.StartUtc);
        using (FishDataRegularFont.Push())
        {
            var textWidth = ImGui.CalcTextSize(text).X;
            ImGui.SetCursorPosX(ImGui.GetContentRegionAvail().X + ImGui.GetCursorPosX() - textWidth - 8f * scale);
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + (rowHeight - ImGui.GetTextLineHeight()) / 2f);
            ImGui.TextColored(FishDataTextButton, text);
        }
    }

    private static string FormatFishDataDuration(TimeSpan span) =>
        span.TotalHours >= 1 ? $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}" : $"{span.Minutes:00}:{span.Seconds:00}";

    private static void DrawFishDataRarityCell(float scale, BigFish fish, DateTime now, float rowHeight)
    {
        var T = UiTheme.Active;
        var percent = FishWindows.GetUptimePercent(fish, now);
        var text = percent == null ? Loc.T("Unbekannt", "Unknown") : $"{percent.Value * 100f:0.#}%";
        var color = percent is { } p && p < 0.01f ? FishDataRarityYellow : T.TextSecondary;
        using (FishDataRegularFont.Push())
        {
            var textWidth = ImGui.CalcTextSize(text).X;
            ImGui.SetCursorPosX(ImGui.GetContentRegionAvail().X + ImGui.GetCursorPosX() - textWidth - 8f * scale);
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + (rowHeight - ImGui.GetTextLineHeight()) / 2f);
            ImGui.TextColored(color, text);
        }
    }

    /// <summary>Schieberegler statt Pillen-Knopf (Nutzeranforderung: "selbe Logik wie im alten Menü") - 1:1 wie
    /// MainWindow.DrawFishTable's Prep-Timer-Spalte: Wert bei jeder Änderung geschrieben, config.Save() erst beim Loslassen.</summary>
    private void DrawFishDataPrepTimerCell(float scale, BigFish fish, Configuration config, FishWindow? window, DateTime now, float rowHeight)
    {
        const int maxPrepTimerMinutes = 50;
        var uptimeMinutes = window != null && !window.Value.IsActive(now) ? (window.Value.EndUtc - window.Value.StartUtc).TotalMinutes : 0d;
        var maxPrepMinutes = Math.Clamp((int)Math.Floor(maxPrepTimerMinutes - uptimeMinutes), 0, maxPrepTimerMinutes);
        var minutes = Math.Clamp(config.FishAlertMinutes.GetValueOrDefault(fish.ItemId), 0, maxPrepMinutes);
        var format = minutes == 0 ? Loc.T("Aus", "Off") : Loc.T("%d Min.", "%d min");

        var sliderWidth = ImGui.GetContentRegionAvail().X - 8f * scale;
        var sliderHeight = ImGui.GetFrameHeight();
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 4f * scale);
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + (rowHeight - sliderHeight) / 2f);

        UiWidgets.PushInputStyle(scale);
        ImGui.SetNextItemWidth(sliderWidth);
        using (FishDataRegularFont.Push())
        {
            if (ImGui.SliderInt($"##OceanPrepTimer_{fish.ItemId}", ref minutes, 0, Math.Max(1, maxPrepMinutes), format))
            {
                if (minutes == 0)
                    config.FishAlertMinutes.Remove(fish.ItemId);
                else
                    config.FishAlertMinutes[fish.ItemId] = minutes;
            }
        }
        UiWidgets.PopInputStyle();
        if (ImGui.IsItemDeactivatedAfterEdit())
            config.Save();
    }

    private void DrawFishDataAutoHookCell(float scale, BigFish fish, Configuration config, IReadOnlyList<string> presetNames, float rowHeight, bool allowNoPreset = true)
    {
        var T = UiTheme.Active;
        var selected = config.FishAutoHookPresets.GetValueOrDefault(fish.ItemId);
        var selectedExists = selected != null && presetNames.Contains(selected);
        var preview = selected == null
            ? Loc.T("Kein Preset", "No preset")
            : selectedExists
                ? selected
                : $"{selected} ({Loc.T("fehlt", "missing")})";
        var previewColor = selected == null ? T.TextSecondary : selectedExists ? T.TextPrimary : FishDataCoralRed;

        var comboWidth = ImGui.GetContentRegionAvail().X - 8f * scale;
        var comboHeight = 24f * scale;
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + (rowHeight - comboHeight) / 2f);

        UiWidgets.PushInputStyle(scale);
        ImGui.PushStyleColor(ImGuiCol.Text, previewColor);
        ImGui.SetNextItemWidth(comboWidth);
        bool open;
        using (FishDataRegularFont.Push())
            open = ImGui.BeginCombo($"##OceanAutoHook_{fish.ItemId}", preview, ImGuiComboFlags.HeightLarge);
        ImGui.PopStyleColor();
        if (open)
        {
            var search = fishDataPresetSearch.GetValueOrDefault(fish.ItemId, string.Empty);
            ImGui.SetNextItemWidth(-1f);
            using (FishDataRegularFont.Push())
            {
                ImGui.InputTextWithHint($"##OceanAutoHookSearch_{fish.ItemId}", Loc.T("Suchen...", "Search..."), ref search, 100);
                fishDataPresetSearch[fish.ItemId] = search;

                if (allowNoPreset && ImGui.Selectable(Loc.T("Kein Preset", "No preset"), selected == null))
                {
                    config.FishAutoHookPresets.Remove(fish.ItemId);
                    config.Save();
                }
                foreach (var name in presetNames)
                {
                    if (!string.IsNullOrWhiteSpace(search) && !name.Contains(search, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (ImGui.Selectable(name, name == selected))
                    {
                        config.FishAutoHookPresets[fish.ItemId] = name;
                        config.Save();
                    }
                }
            }
            ImGui.EndCombo();
        }
        else
        {
            fishDataPresetSearch.Remove(fish.ItemId);
        }
        UiWidgets.PopInputStyle();
    }

    private void DrawFishDataDevCell(float scale, BigFish fish, ExcelSheet<Item> itemSheet, float rowHeight)
    {
        var T = UiTheme.Active;
        var automation = plugin.Automation;
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + 8f * scale);
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + (rowHeight - 24f * scale) / 2f);

        var testingThis = automation.TestTarget?.ItemId == fish.ItemId;
        var mainRunning = automation.IsRunning && automation.TestTarget == null;
        var disabled = !testingThis && (mainRunning || HasMissingRequiredDependency() || !automation.CanFlyToFish(fish));
        var sameMap = !testingThis && Plugin.ClientState.TerritoryType == fish.TerritoryId;
        var flyColor = testingThis ? Vector4.One : sameMap ? FishDataTeleportOrange : FishDataTeleportBlue;

        var flySize = new Vector2(26f * scale, 24f * scale);
        var flyCursor = ImGui.GetCursorScreenPos();
        if (disabled)
            ImGui.BeginDisabled();
        ImGui.InvisibleButton($"##OceanFlyToFish_{fish.ItemId}", flySize);
        var flyHovered = ImGui.IsItemHovered(disabled ? ImGuiHoveredFlags.AllowWhenDisabled : ImGuiHoveredFlags.None);
        var flyClicked = !disabled && ImGui.IsItemClicked();
        if (disabled)
            ImGui.EndDisabled();

        var dl = ImGui.GetWindowDrawList();
        var flyBg = testingThis ? new Vector4(0.85f, 0.3f, 0.35f, flyHovered ? 1f : 0.85f) : flyHovered ? T.BgSelected : new Vector4(0f, 0f, 0f, 0f);
        dl.AddRectFilled(flyCursor, flyCursor + flySize, ImGui.GetColorU32(flyBg), 4f * scale);
        dl.AddRect(flyCursor, flyCursor + flySize, ImGui.GetColorU32(T.LineFrame), 4f * scale);
        var flyIconSize = 13f * scale;
        UiIcons.Draw(testingThis ? UiIcon.Cross : UiIcon.Travel, flyCursor + (flySize - new Vector2(flyIconSize)) / 2f, flyIconSize, ImGui.GetColorU32(flyColor));

        if (flyClicked)
        {
            if (testingThis)
                automation.Stop();
            else
                automation.StartTest(fish);
        }
        if (flyHovered)
        {
            var tooltip = testingThis
                ? $"{Loc.T("Stopp", "Stop")}\n{automation.StatusText}"
                : mainRunning
                    ? Loc.T("Nicht möglich, während die Automation läuft.", "Not possible while the automation is running.")
                    : HasMissingRequiredDependency()
                        ? MissingPluginText
                        : !automation.CanFlyToFish(fish)
                            ? Loc.T("Angel-Position unbekannt.", "Fishing spot unknown.")
                            : FishingPositionStore.GetAll(fish.ItemId).Count > 0
                                ? Loc.T("Zum Fisch fliegen (nur Position, ohne zu angeln).", "Fly to fish (to the fishing position, without fishing).")
                                : Loc.T("Keine Position gespeichert - fliegt zur ungefähren Angel-Stelle.", "No position saved - flies to the approximate fishing spot.");
            ImGui.SetTooltip(tooltip);
        }

        ImGui.SameLine(0f, 6f * scale);

        var spots = FishingPositionStore.GetAll(fish.ItemId);
        var hasSaved = spots.Count > 0;
        var atCastable = automation.IsAtCastablePosition(fish);
        var spotsLabel = spots.Count.ToString(CultureInfo.InvariantCulture);
        float spotsLabelWidth;
        using (FishDataSmallBoldFont.Push())
            spotsLabelWidth = ImGui.CalcTextSize(spotsLabel).X;
        var pinSize = 11f * scale;
        var spotsSize = new Vector2(pinSize + 4f * scale + spotsLabelWidth + 8f * scale, 24f * scale);
        var spotsCursor = ImGui.GetCursorScreenPos();
        ImGui.InvisibleButton($"##OceanSpots_{fish.ItemId}", spotsSize, ImGuiButtonFlags.MouseButtonLeft | ImGuiButtonFlags.MouseButtonRight);
        var spotsHovered = ImGui.IsItemHovered();
        var popupId = $"##OceanSpotsPopup_{fish.ItemId}";
        ImGui.OpenPopupOnItemClick(popupId, ImGuiPopupFlags.MouseButtonRight);

        var spotsColor = hasSaved ? FishDataActiveGreen : T.TextMuted;
        UiIcons.Draw(UiIcon.Pin, spotsCursor + new Vector2(0f, (spotsSize.Y - pinSize) / 2f), pinSize, ImGui.GetColorU32(spotsColor));
        using (FishDataSmallBoldFont.Push())
            dl.AddText(spotsCursor + new Vector2(pinSize + 4f * scale, (spotsSize.Y - ImGui.GetTextLineHeight()) / 2f), ImGui.GetColorU32(FishDataActiveGreen), spotsLabel);

        if (ImGui.IsItemClicked(ImGuiMouseButton.Left) && atCastable)
        {
            var player = Plugin.ObjectTable.LocalPlayer;
            if (player != null)
            {
                if (ImGui.GetIO().KeyShift)
                {
                    if (spots.Count > 0)
                        FishingPositionStore.RemoveAt(fish.ItemId, spots.Count - 1);
                }
                else
                {
                    FishingPositionStore.Add(fish, FishDataDisplayName(fish, itemSheet), player.Position, player.Rotation);
                }
            }
        }

        if (spotsHovered)
        {
            if (hasSaved)
            {
                var tooltip = new StringBuilder();
                tooltip.Append(Loc.T($"{spots.Count} gespeicherte Position(en):", $"{spots.Count} saved position(s):"));
                foreach (var spot in spots)
                    tooltip.Append($"\n{spot.X:F1}, {spot.Y:F1}, {spot.Z:F1}");
                tooltip.Append(Loc.T("\n\nLinksklick: Position speichern (nur an einer anglebaren Stelle).\nShift+Links: letzte Position entfernen.\nRechtsklick: einzelne Position entfernen.",
                    "\n\nLeft-click: save position (only while at a castable spot).\nShift+Left: remove last position.\nRight-click: remove a specific position."));
                ImGui.SetTooltip(tooltip.ToString());
            }
            else
            {
                ImGui.SetTooltip(Loc.T("Noch keine Position gespeichert.\nLinksklick (an einer anglebaren Stelle): Position speichern.", "No position saved yet.\nLeft-click (while at a castable spot): save position."));
            }
        }

        if (ImGui.BeginPopup(popupId))
        {
            for (var i = 0; i < spots.Count; i++)
            {
                var spot = spots[i];
                if (ImGui.Selectable($"{spot.X:F1}, {spot.Y:F1}, {spot.Z:F1}##OceanSpotEntry_{i}"))
                    FishingPositionStore.RemoveAt(fish.ItemId, i);
            }
            ImGui.EndPopup();
        }
    }

}
