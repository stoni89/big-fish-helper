using Dalamud.Game;

namespace BigFishHelper;

/// <summary>
/// Sehr einfache Übersetzungshilfe: Deutsch und Englisch, ausgewählt anhand der im Spielclient
/// eingestellten Sprache (alle anderen Sprachen fallen auf Englisch zurück) - AUSSER innerhalb eines
/// aktiven Menü-Sprachüberschreibens (siehe MenuLanguageOverride), das von Windows.OceanMainWindow.
/// Draw() für dessen komplette Laufzeit gesetzt wird (per try/finally begrenzt) - 1:1 wie
/// TheExplorersCodex.Loc/CodexMenuWindow.Draw().
/// </summary>
public static class Loc
{
    // null = kein Überschreiben aktiv (Standardfall - Spielsprache gilt überall, wie bisher).
    public static bool? MenuLanguageOverride { get; set; }

    private static bool IsGerman => MenuLanguageOverride ?? Plugin.ClientState.ClientLanguage == ClientLanguage.German;

    public static string T(string de, string en) => IsGerman ? de : en;
}
