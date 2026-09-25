using System;
using System.Collections.Generic;
using Dalamud.Configuration;

namespace BigFishHelper;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

    // Mount zum Fliegen (Lumina-Mount-RowId) - 0 = Mount Roulette.
    public uint FlyingMountId { get; set; } = 0;

    // So viele Minuten VOR der Prep Time schon zur Angel-Position fliegen - geangelt wird erst ab der Prep Time.
    public int TravelLeadMinutes { get; set; } = 2;

    // Timer-Seite: bereits gefangene Big Fish ausblenden.
    public bool HideCaughtFish { get; set; } = true;

    // Big Fish (Item-Ids), die man machen will - Checkbox in der Tabelle.
    public HashSet<uint> EnabledFish { get; set; } = new();

    // Pro Big Fish (Item-Id) die eingetragene Zeit in Minuten (0 = aus) - siehe Timer-Seite.
    public Dictionary<uint, int> FishAlertMinutes { get; set; } = new();

    // Pro Big Fish (Item-Id) das zugeordnete AutoHook-Preset (Name) - siehe Timer-Seite.
    public Dictionary<uint, string> FishAutoHookPresets { get; set; } = new();

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}
