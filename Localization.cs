using Dalamud.Game;

namespace BigFishHelper;

/// <summary>
/// Sehr einfache Übersetzungshilfe: Deutsch und Englisch, ausgewählt anhand der im Spielclient
/// eingestellten Sprache (alle anderen Sprachen fallen auf Englisch zurück).
/// </summary>
public static class Loc
{
    private static bool IsGerman => Plugin.ClientState.ClientLanguage == ClientLanguage.German;

    public static string T(string de, string en) => IsGerman ? de : en;
}
