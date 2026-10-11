using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Plugin;

namespace BigFishHelper;

/// <summary>Zustand eines einzelnen benötigten Plugins - "Disabled" meint installiert, aber gerade nicht geladen (Nutzer hat es in /xlplugins ausgeschaltet).</summary>
public enum RequiredPluginStatus
{
    Installed,
    Disabled,
    Missing,
}

/// <summary>
/// Zentraler Dienst für die benötigten Begleit-Plugins (AutoHook/vnavmesh/Lifestream) - ersetzt die
/// frühere, rein boolesche Prüfung (Windows.OceanMainWindow.IsPluginLoaded) durch den vollen
/// Installed/Disabled/Missing-Dreiklang (Nutzeranforderung: "fehlende ODER deaktivierte Pflicht-
/// Plugins sichtbar machen"). Dalamud liefert dafür keinen direkten "IsDisabled"-Wert - IExposedPlugin
/// hat nur InternalName+IsLoaded - daher: gar nicht in InstalledPlugins gefunden = Missing, gefunden
/// aber IsLoaded=false = Disabled, IsLoaded=true = Installed.
///
/// Status wird gecacht und höchstens 1x/Sekunde neu ermittelt (Nutzeranforderung: "nicht jeden
/// Frame") - jeder GetStatus/AllReady-Aufruf stößt intern EnsureFresh() an, das bei Bedarf einmal
/// über InstalledPlugins iteriert statt bei jedem UI-Frame erneut.
/// </summary>
public static class RequiredPlugins
{
    public static readonly (string InternalName, string DisplayName, string DescriptionDe, string DescriptionEn, bool Required)[] Dependencies =
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

    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(1);
    private static DateTime lastRefreshUtc = DateTime.MinValue;
    private static readonly Dictionary<string, RequiredPluginStatus> Cache = new();

    private static void EnsureFresh()
    {
        var now = DateTime.UtcNow;
        if (lastRefreshUtc != DateTime.MinValue && now - lastRefreshUtc < RefreshInterval)
            return;

        lastRefreshUtc = now;
        foreach (var dependency in Dependencies)
            Cache[dependency.InternalName] = ComputeStatus(dependency.InternalName);
    }

    private static RequiredPluginStatus ComputeStatus(string internalName)
    {
        var plugin = Plugin.PluginInterface.InstalledPlugins.FirstOrDefault(p => p.InternalName == internalName);
        if (plugin == null)
            return RequiredPluginStatus.Missing;

        return plugin.IsLoaded ? RequiredPluginStatus.Installed : RequiredPluginStatus.Disabled;
    }

    public static RequiredPluginStatus GetStatus(string internalName)
    {
        EnsureFresh();
        return Cache.TryGetValue(internalName, out var status) ? status : ComputeStatus(internalName);
    }

    /// <summary>Alle benötigten ("Required") Plugins installiert und aktiv - steuert, ob die Automation gestartet werden darf.</summary>
    public static bool AllReady
    {
        get
        {
            EnsureFresh();
            return Dependencies.Where(d => d.Required).All(d => GetStatus(d.InternalName) == RequiredPluginStatus.Installed);
        }
    }

    /// <summary>Anzahl der benötigten Plugins, die gerade NICHT installiert+aktiv sind - für das Sidebar-Badge.</summary>
    public static int ProblemCount
    {
        get
        {
            EnsureFresh();
            return Dependencies.Count(d => d.Required && GetStatus(d.InternalName) != RequiredPluginStatus.Installed);
        }
    }

    /// <summary>Die benötigten Plugins, die gerade nicht bereit sind, in Dependencies-Reihenfolge (für Status-Banner/Hero-Zeile/Overlay).</summary>
    public static IReadOnlyList<(string DisplayName, RequiredPluginStatus Status)> Problems
    {
        get
        {
            EnsureFresh();
            return Dependencies
                .Where(d => d.Required && GetStatus(d.InternalName) != RequiredPluginStatus.Installed)
                .Select(d => (d.DisplayName, GetStatus(d.InternalName)))
                .ToList();
        }
    }

    /// <summary>Kurzer Satz "Installiere X und aktiviere Y, um zu starten." - für Tooltips am gesperrten Start-Knopf.</summary>
    public static string BuildBlockedTooltip()
    {
        var missing = Problems.Where(p => p.Status == RequiredPluginStatus.Missing).Select(p => p.DisplayName).ToList();
        var disabled = Problems.Where(p => p.Status == RequiredPluginStatus.Disabled).Select(p => p.DisplayName).ToList();

        var partsDe = new List<string>();
        if (missing.Count > 0)
            partsDe.Add($"{string.Join(", ", missing)} installieren");
        if (disabled.Count > 0)
            partsDe.Add($"{string.Join(", ", disabled)} aktivieren");

        var partsEn = new List<string>();
        if (missing.Count > 0)
            partsEn.Add($"install {string.Join(", ", missing)}");
        if (disabled.Count > 0)
            partsEn.Add($"enable {string.Join(", ", disabled)}");

        var sentenceEn = string.Join(" and ", partsEn);
        sentenceEn = char.ToUpperInvariant(sentenceEn[0]) + sentenceEn[1..];

        return Loc.T($"{string.Join(" und ", partsDe)}, um zu starten.", $"{sentenceEn} to start.");
    }
}
