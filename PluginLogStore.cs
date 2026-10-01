using System;
using System.Collections.Generic;
using Serilog.Events;

namespace BigFishHelper;

// Id = fortlaufende, eindeutige Nummer je Eintrag (siehe PluginLogStore.Add) - für die Zeilen-
// Markierung auf der Log-Seite (MainWindow.DrawLogPage): die gefilterte/angezeigte Liste wird jeden
// Frame neu aus dem Snapshot gebaut, ein reiner Listenindex wäre dafür als Auswahl-Schlüssel instabil.
public readonly record struct LogEntry(long Id, DateTime Timestamp, LogEventLevel Level, string Message);

/// <summary>
/// Hält die letzten eigenen Log-Einträge im Speicher, damit sie im Plugin-Menü (Log-Seite) durchsucht/
/// gefiltert werden können, ohne das externe /xllog-Fenster öffnen zu müssen (Nutzeranforderung) -
/// PluginLogRecorder legt hier bei jedem echten Log-Aufruf zusätzlich eine formatierte Kopie ab. Fest
/// begrenzte Kapazität, damit der Speicherverbrauch über eine lange Sitzung nicht unbegrenzt wächst -
/// älteste Einträge fallen beim Überschreiten einfach vorne raus.
/// </summary>
public static class PluginLogStore
{
    private const int MaxEntries = 5000;
    private static readonly object padlock = new();
    private static readonly List<LogEntry> entries = new();
    private static long nextId = 1;

    public static void Add(LogEventLevel level, string message)
    {
        lock (padlock)
        {
            entries.Add(new LogEntry(nextId++, DateTime.Now, level, message));
            if (entries.Count > MaxEntries)
                entries.RemoveRange(0, entries.Count - MaxEntries);
        }
    }

    /// <summary>Kopie der aktuellen Einträge - Snapshot statt der Originalliste, damit die Anzeige (anderer Frame/Thread) nicht mit gleichzeitigem Add() kollidiert.</summary>
    public static List<LogEntry> Snapshot()
    {
        lock (padlock)
            return new List<LogEntry>(entries);
    }

    public static void Clear()
    {
        lock (padlock)
            entries.Clear();
    }
}
