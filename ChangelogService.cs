using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BigFishHelper;

public sealed class ChangelogChange
{
    public string Type { get; set; } = "improved";
    public string Text { get; set; } = string.Empty;

    [JsonPropertyName("text_de")]
    public string? TextDe { get; set; }
}

public sealed class ChangelogEntry
{
    public string Version { get; set; } = string.Empty;
    public string Date { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("title_de")]
    public string? TitleDe { get; set; }

    public List<ChangelogChange> Changes { get; set; } = new();
}

/// <summary>
/// Lädt Data/Changelog.json einmalig beim ersten Zugriff und stellt die Einträge absteigend nach
/// Version sortiert bereit (neueste zuerst) - 1:1 wie TheExplorersCodex.ChangelogService.
/// </summary>
public static class ChangelogService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static List<ChangelogEntry>? cachedEntries;

    public static List<ChangelogEntry> Entries => cachedEntries ??= Load();

    /// <summary>Höchste bekannte Version, oder null, falls Changelog.json fehlt/leer ist.</summary>
    public static Version? LatestVersion =>
        Entries.Count > 0 && Version.TryParse(Entries[0].Version, out var latest) ? latest : null;

    /// <summary>Ungesehen, solange LatestVersion neuer ist als die zuletzt auf der Changelog-Seite
    /// gesehene Version (oder diese noch nie gesetzt wurde) - steuert das NEW-Badge im Menü.</summary>
    public static bool HasUnseenChangelog(Configuration config)
    {
        if (LatestVersion is not { } latest)
            return false;
        if (!Version.TryParse(config.LastSeenChangelogVersion, out var seen))
            return true;
        return latest > seen;
    }

    public static string ResolveTitle(ChangelogEntry entry) => Loc.T(entry.TitleDe ?? entry.Title, entry.Title);

    public static string ResolveText(ChangelogChange change) => Loc.T(change.TextDe ?? change.Text, change.Text);

    private static List<ChangelogEntry> Load()
    {
        try
        {
            var path = Path.Combine(Plugin.PluginInterface.AssemblyLocation.DirectoryName!, "Data", "Changelog.json");
            if (!File.Exists(path))
                return new List<ChangelogEntry>();

            var json = File.ReadAllText(path);
            var loaded = JsonSerializer.Deserialize<List<ChangelogEntry>>(json, JsonOptions) ?? new List<ChangelogEntry>();
            return loaded
                .Where(e => Version.TryParse(e.Version, out _))
                .OrderByDescending(e => Version.Parse(e.Version))
                .ToList();
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "[ChangelogService] Laden von Changelog.json fehlgeschlagen.");
            return new List<ChangelogEntry>();
        }
    }
}
