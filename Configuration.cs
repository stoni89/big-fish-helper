using System;
using System.Collections.Generic;
using Dalamud.Configuration;

namespace BigFishHelper;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

    // Timer-Seite: bereits gefangene Big Fish ausblenden.
    public bool HideCaughtFish { get; set; } = true;

    // Big Fish (Item-Ids), die man machen will - Checkbox in der Tabelle.
    public HashSet<uint> EnabledFish { get; set; } = new();

    // Pro Big Fish (Item-Id) die eingetragene Zeit in Minuten (0 = aus) - siehe Timer-Seite.
    public Dictionary<uint, int> FishAlertMinutes { get; set; } = new();

    // Pro Big Fish (Item-Id) die per "Position speichern"-Knopf (nur Dev-Version) gespeicherte Angel-Position.
    public Dictionary<uint, SavedFishingPosition> SavedFishingPositions { get; set; } = new();

    // Pro Big Fish (Item-Id) das zugeordnete AutoHook-Preset (Name) - siehe Timer-Seite.
    public Dictionary<uint, string> FishAutoHookPresets { get; set; } = new();

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}
