using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace BigFishHelper;

/// <summary>
/// Namen aller Presets, die aktuell in AutoHook angelegt sind. AutoHook bietet dafür keine IPC -
/// die Namen stehen aber in dessen Konfigurationsdatei (pluginConfigs/AutoHook.json,
/// HookPresets.CustomPresets[].PresetName). Die Datei wird nur neu gelesen, wenn AutoHook sie seit dem
/// letzten Lesen gespeichert hat (höchstens alle paar Sekunden geprüft).
/// </summary>
public static class AutoHookPresets
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(3);

    private static IReadOnlyList<string> names = Array.Empty<string>();
    private static DateTime lastWriteTimeUtc = DateTime.MinValue;
    private static DateTime lastCheckUtc = DateTime.MinValue;

    public static IReadOnlyList<string> GetNames()
    {
        if (DateTime.UtcNow - lastCheckUtc < CheckInterval)
            return names;
        lastCheckUtc = DateTime.UtcNow;

        try
        {
            var path = GetConfigPath();
            if (path == null || !File.Exists(path))
            {
                names = Array.Empty<string>();
                return names;
            }

            var writeTime = File.GetLastWriteTimeUtc(path);
            if (writeTime == lastWriteTimeUtc)
                return names;

            names = ReadNames(path);
            lastWriteTimeUtc = writeTime;
        }
        catch (Exception ex)
        {
            // Z.B. während AutoHook die Datei gerade schreibt - beim nächsten Check erneut versuchen.
            Plugin.Log.Debug(ex, "[AutoHookPresets] Konfiguration von AutoHook nicht lesbar.");
        }

        return names;
    }

    // Eigene Konfigurationsdatei liegt in pluginConfigs/BigFishHelper.json - AutoHooks direkt daneben.
    private static string? GetConfigPath()
    {
        var directory = Plugin.PluginInterface.ConfigFile.DirectoryName;
        return directory == null ? null : Path.Combine(directory, "AutoHook.json");
    }

    private static IReadOnlyList<string> ReadNames(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var document = JsonDocument.Parse(stream, new JsonDocumentOptions { AllowTrailingCommas = true });

        if (!document.RootElement.TryGetProperty("HookPresets", out var hookPresets)
            || !hookPresets.TryGetProperty("CustomPresets", out var customPresets)
            || customPresets.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();

        return customPresets.EnumerateArray()
            .Select(p => p.TryGetProperty("PresetName", out var name) ? name.GetString() : null)
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .Select(n => n!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
