using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;

namespace BigFishHelper;

public readonly record struct FishingPosition(Vector3 Position, float? Facing);

/// <summary>Eine Angel-Position in Data/FishingPositions.json - Position + Blickrichtung (FFXIV-Rotation).</summary>
[Serializable]
public class FishingPositionEntry
{
    // Nur zur Lesbarkeit der Datei - maßgeblich ist der Schlüssel (Item-Id).
    public string Name { get; set; } = string.Empty;
    public float X { get; set; }
    public float Y { get; set; }
    public float Z { get; set; }
    public float? Facing { get; set; }
}

/// <summary>
/// Angel-Positionen der Big Fish (Key = Item-Id) aus Data/FishingPositions.json - die Datei wird mit
/// dem Plugin ausgeliefert, damit alle Nutzer dieselben Positionen haben. Der "Position speichern"-
/// Knopf (nur Dev-Version) schreibt direkt in diese Datei im Projektordner (und in die geladene Kopie),
/// d.h. gespeicherte Positionen landen mit dem nächsten Commit/Release bei allen.
///
/// Pro Fisch sind MEHRERE Spots möglich (Nutzeranforderung: nicht immer an derselben Stelle angeln) -
/// Get() wählt bei jedem Aufruf zufällig einen davon aus. Die Automation ruft Get() bewusst nur EINMAL
/// pro Trip auf und hält das Ergebnis fest (siehe FishingAutomation.targetPosition), damit während
/// desselben Trips nicht versehentlich mehrere unterschiedliche Spots gemischt werden.
/// </summary>
public static class FishingPositionStore
{
    private const string FileName = "FishingPositions.json";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly Random Random = new();

    private static Dictionary<uint, List<FishingPositionEntry>>? entries;

    private static Dictionary<uint, List<FishingPositionEntry>> Entries => entries ??= Load();

    /// <summary>Zufällig ausgewählter Spot dieses Fischs - null, wenn keiner eingetragen ist.</summary>
    public static FishingPosition? Get(uint itemId)
    {
        if (!Entries.TryGetValue(itemId, out var list) || list.Count == 0)
            return null;

        var entry = list[Random.Next(list.Count)];
        return new FishingPosition(new Vector3(entry.X, entry.Y, entry.Z), entry.Facing);
    }

    /// <summary>Alle eingetragenen Spots dieses Fischs (Dev-Version: Liste + Löschen einzelner Spots).</summary>
    public static IReadOnlyList<FishingPositionEntry> GetAll(uint itemId) =>
        Entries.TryGetValue(itemId, out var list) ? list : Array.Empty<FishingPositionEntry>();

    /// <summary>
    /// Grober Mittelpunkt (nur X/Z) des Angelplatzes laut Spieldaten (FishingSpot X/Z sind
    /// Kartenpixel, per Map-Skalierung in Weltkoordinaten umgerechnet, wie GatherBuddy/Gathering-
    /// Plugins das für Sammelpunkte machen) - für "Fliege zum Fisch", solange noch keine genaue
    /// Position gespeichert ist. Die Höhe ist darin nicht enthalten.
    /// </summary>
    public static Vector2? GetApproximateSpotCenter(BigFish fish)
    {
        var spotSheet = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.FishingSpot>();
        if (!spotSheet.TryGetRow(fish.FishingSpotId, out var spot) || spot.TerritoryType.ValueNullable is not { } territory
            || territory.Map.ValueNullable is not { } map)
            return null;

        var x = (spot.X - 1024f) * 100f / map.SizeFactor - map.OffsetX;
        var z = (spot.Z - 1024f) * 100f / map.SizeFactor - map.OffsetY;
        return new Vector2(x, z);
    }

    /// <summary>Nur Dev-Version: einen weiteren Spot hinzufügen (Projektdatei + geladene Kopie). false, wenn die Projektdatei nicht gefunden wurde.</summary>
    public static bool Add(uint itemId, string fishName, Vector3 position, float facing)
    {
        if (!Entries.TryGetValue(itemId, out var list))
            Entries[itemId] = list = new List<FishingPositionEntry>();

        list.Add(new FishingPositionEntry { Name = fishName, X = position.X, Y = position.Y, Z = position.Z, Facing = facing });
        return Write();
    }

    /// <summary>Nur Dev-Version: einen einzelnen Spot entfernen (Projektdatei + geladene Kopie).</summary>
    public static bool RemoveAt(uint itemId, int index)
    {
        if (!Entries.TryGetValue(itemId, out var list) || index < 0 || index >= list.Count)
            return false;

        list.RemoveAt(index);
        if (list.Count == 0)
            Entries.Remove(itemId);

        return Write();
    }

    /// <summary>Pfad der Projektdatei (Quellordner des Dev-Plugins) - null, wenn nicht gefunden (z.B. installierte Version).</summary>
    public static string? SourceFilePath
    {
        get
        {
            // Dev-Plugin läuft aus <Projekt>/bin/Debug - nach oben bis zur .csproj suchen.
            var directory = Plugin.PluginInterface.AssemblyLocation.Directory;
            for (var i = 0; i < 5 && directory != null; i++, directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "BigFishHelper.csproj")))
                    return Path.Combine(directory.FullName, "Data", FileName);
            }

            return null;
        }
    }

    private static string OutputFilePath =>
        Path.Combine(Plugin.PluginInterface.AssemblyLocation.DirectoryName!, "Data", FileName);

    private static Dictionary<uint, List<FishingPositionEntry>> Load()
    {
        try
        {
            if (!File.Exists(OutputFilePath))
                return new Dictionary<uint, List<FishingPositionEntry>>();

            var raw = JsonSerializer.Deserialize<Dictionary<string, List<FishingPositionEntry>>>(File.ReadAllText(OutputFilePath), JsonOptions);
            return raw?
                .Where(kv => uint.TryParse(kv.Key, out _))
                .ToDictionary(kv => uint.Parse(kv.Key), kv => kv.Value)
                ?? new Dictionary<uint, List<FishingPositionEntry>>();
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "[FishingPositionStore] Data/FishingPositions.json nicht lesbar.");
            return new Dictionary<uint, List<FishingPositionEntry>>();
        }
    }

    private static bool Write()
    {
        var json = JsonSerializer.Serialize(
            Entries.OrderBy(kv => kv.Key).ToDictionary(kv => kv.Key.ToString(), kv => kv.Value), JsonOptions) + Environment.NewLine;

        var wroteSource = false;
        try
        {
            if (SourceFilePath is { } source)
            {
                File.WriteAllText(source, json);
                wroteSource = true;
            }

            File.WriteAllText(OutputFilePath, json);
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "[FishingPositionStore] Data/FishingPositions.json konnte nicht geschrieben werden.");
        }

        return wroteSource;
    }
}
