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

    // Einstellungen -> Allgemein -> Anflug: Sprint (auf Abklingzeit) nutzen, solange man in einer Stadt
    // zu Fuß unterwegs ist (kein Aufsitzen möglich) - siehe FishingAutomation.UpdateCitySprint.
    public bool UseSprintInCities { get; set; } = true;

    // Öffnet beim Klick auf "Start" automatisch das kleine Status-Overlay (siehe StatusOverlayWindow).
    public bool ShowOverlayOnStart { get; set; } = false;

    // Timer-Seite: bereits gefangene Big Fish ausblenden.
    public bool HideCaughtFish { get; set; } = true;

    // Big Fish (Item-Ids), die man machen will - Checkbox in der Tabelle.
    public HashSet<uint> EnabledFish { get; set; } = new();

    // Pro Big Fish (Item-Id) die eingetragene Zeit in Minuten (0 = aus) - siehe Timer-Seite.
    public Dictionary<uint, int> FishAlertMinutes { get; set; } = new();

    // Pro Big Fish (Item-Id) das zugeordnete AutoHook-Preset (Name) - siehe Timer-Seite.
    public Dictionary<uint, string> FishAutoHookPresets { get; set; } = new();

    // Manueller Tie-Break-Sortierschlüssel (Nutzeranforderung) für Fische, die zur exakt gleichen
    // Zeit losfliegen - ohne Eintrag hier sortiert FishingAutomation.GetPlannedFish stattdessen nach
    // der Uptime-Rarität (seltenere zuerst). Wird nur bei manuellem Hoch/Runter in der "Geplante
    // Fische"-Tabelle (Play-Seite) gesetzt, per Tausch mit dem jeweiligen Nachbarn.
    public Dictionary<uint, double> FishTieBreakOrder { get; set; } = new();

    // "Always Up Fish Backup Timer" (Nutzeranforderung, Einstellungen -> Allgemein): "immer
    // verfügbare" Fische starten nur, wenn in den nächsten X Minuten kein Prep Timer eines NICHT
    // immer verfügbaren Fischs beginnt (0 = aus, Default) - siehe FishingAutomation.UpdateWaiting.
    public int AlwaysUpFishBackupTimerMinutes { get; set; } = 0;

    // Einstellungen -> Allgemein -> Angeln: nach dem Angeln (Fisch gefangen/Fenster vorbei) alle
    // Fische im Hauptinventar nativ desynthetisieren (AgentSalvage.SalvageItem, kein Fremd-Plugin
    // nötig), aber nur, wenn dafür auch wirklich Zeit ist (Nutzeranforderung) - siehe
    // FishingAutomation.ShouldDesynthesizeNow/UpdateDesynthesizing.
    public bool DesynthesisAfterFishing { get; set; } = false;

    // Einstellungen -> Allgemein -> Angeln -> "Desynthesis nach dem Angeln" -> "Big Fish ignorieren"
    // (Nutzeranforderung): Desynthesis betrifft standardmäßig ALLE Fische im Hauptinventar, mit diesem
    // Schalter werden Big Fish (BigFishData.All) davon ausgenommen - siehe
    // FishingAutomation.UpdateDesynthesizing.
    public bool DesynthesisIgnoreBigFish { get; set; } = false;

    // Einstellungen -> Allgemein -> Angeln: konfiguriertes Ausrüstungsset-Preset, auf das die
    // Automation als ALLERERSTES wechselt, noch vor jedem Teleport (Nutzeranforderung) - siehe
    // FishingAutomation.UpdateSwitchingJobFirst. -1 = noch keins ausgewählt (Start-Knopf bleibt dann
    // deaktiviert, siehe MainWindow).
    public int FisherGearsetIndex { get; set; } = -1;

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}
