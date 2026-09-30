using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using ECommons.UIHelpers.AddonMasterImplementations;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;

namespace BigFishHelper;

/// <summary>Kleine Helfer für Spielaktionen der Automation (Teleport, Mount, Klassenwechsel, Auswerfen).</summary>
public static class GameActions
{
    private const uint MountRouletteGeneralActionId = 9;
    private const uint DismountGeneralActionId = 23;
    private const uint SprintGeneralActionId = 4;
    private const uint CastActionId = 289; // Fischer: "Auswerfen"
    private const uint FisherClassJobId = 18;

    /// <summary>
    /// Nächster freigeschalteter großer Ätherit in der Zielzone zur angegebenen Weltposition -
    /// Position des Ätheriten über seinen Kartenmarker (MapMarker) bestimmt.
    /// </summary>
    public static unsafe uint? FindNearestAetheryte(uint territoryId, Vector3 target) =>
        FindNearestAetheryteRow(territoryId, target, a => a.IsAetheryte);

    /// <summary>
    /// Nächster freigeschalteter Aethernet-Kristall (kleiner Kristall, z.B. "Lancer's Guild" in Old
    /// Gridania) in der Zielzone - im Gegensatz zu <see cref="FindNearestAetheryte"/> bewusst NUR
    /// unter den NICHT-großen Einträgen gesucht (siehe SpecialRoutes.cs/SweetnewtRoute - der normale
    /// Teleport findet nur große Ätheriten, für einen Aethernet-Kristall braucht es diesen Filter).
    /// </summary>
    public static unsafe uint? FindNearestAethernetShard(uint territoryId, Vector3 target) =>
        FindNearestAetheryteRow(territoryId, target, a => !a.IsAetheryte);

    private static unsafe uint? FindNearestAetheryteRow(uint territoryId, Vector3 target, Func<Aetheryte, bool> filter)
    {
        var uiState = UIState.Instance();
        var aetherytes = Plugin.DataManager.GetExcelSheet<Aetheryte>()
            .Where(a => filter(a) && a.Territory.RowId == territoryId && uiState != null && uiState->IsAetheryteUnlocked(a.RowId))
            .ToList();
        if (aetherytes.Count == 0)
            return null;

        return aetherytes
            .Select(a => (a.RowId, Position: ResolveAetherytePosition(a)))
            .OrderBy(a => a.Position is { } p ? Vector2.DistanceSquared(new Vector2(p.X, p.Z), new Vector2(target.X, target.Z)) : float.MaxValue)
            .First().RowId;
    }

    /// <summary>
    /// Weltposition eines Ätheriten über seinen Kartenmarker (MapMarker) - nutzt bewusst DIREKT
    /// aetheryte.Map (jede Aetheryte-Zeile trägt ihre eigene, richtige Karte) statt wie vorher ALLE
    /// Map-Zeilen der Zone zu durchsuchen und die erste zu nehmen, deren Marker-Bereich zufällig
    /// passt: Zonen mit mehreren Karten-Varianten (z.B. Städte mit zusätzlichen Event-/Phasen-Karten
    /// über dieselbe TerritoryType-Zeile) konnten dadurch die falsche Karte treffen - mit abweichendem
    /// SizeFactor/Offset ergab das eine stark verfälschte Position, wodurch z.B. der große Stadt-
    /// Kristall-Ätherit ("Aetheryte Plaza") fälschlich weiter weg berechnet wurde als ein tatsächlich
    /// entfernterer Ätherit (Nutzer-Report: falscher/entfernter Ätherit trotz näherem gewählt).
    /// </summary>
    private static Vector3? ResolveAetherytePosition(Aetheryte aetheryte)
    {
        if (aetheryte.Map.ValueNullable is not { } map)
            return null;

        var markerSheet = Plugin.DataManager.GetSubrowExcelSheet<MapMarker>();
        if (!markerSheet.TryGetRow(map.MapMarkerRange, out var markers))
            return null;

        foreach (var marker in markers)
        {
            if (marker.DataType != 3 || marker.DataKey.RowId != aetheryte.RowId)
                continue;

            var worldX = (marker.X - 1024f) * 100f / map.SizeFactor - map.OffsetX;
            var worldZ = (marker.Y - 1024f) * 100f / map.SizeFactor - map.OffsetY;
            return new Vector3(worldX, 0, worldZ);
        }

        return null;
    }

    public static unsafe bool Teleport(uint aetheryteId)
    {
        var telepo = Telepo.Instance();
        return telepo != null && telepo->Teleport(aetheryteId, 0);
    }

    /// <summary>Ob in der aktuellen Zone überhaupt aufgesessen werden kann (z.B. in Städten oft nicht).</summary>
    public static bool CanMountHere()
    {
        var territorySheet = Plugin.DataManager.GetExcelSheet<TerritoryType>();
        return territorySheet != null && territorySheet.TryGetRow(Plugin.ClientState.TerritoryType, out var territory) && territory.Mount;
    }

    public static unsafe bool MountRoulette() => UseGeneralAction(MountRouletteGeneralActionId);

    /// <summary>Ruft das eingestellte Mount (0 = Mount Roulette) - klappt das nicht, Mount Roulette.</summary>
    public static unsafe bool Mount(uint mountId)
    {
        if (mountId == 0)
            return MountRoulette();

        var actionManager = ActionManager.Instance();
        if (actionManager != null && actionManager->UseAction(ActionType.Mount, mountId))
            return true;

        return MountRoulette();
    }

    /// <summary>Alle freigeschalteten Mounts (RowId, Name in Client-Sprache), alphabetisch - für die Auswahl in den Einstellungen.</summary>
    public static unsafe (uint Id, string Name)[] GetUnlockedMounts()
    {
        var playerState = PlayerState.Instance();
        if (playerState == null)
            return System.Array.Empty<(uint, string)>();

        return Plugin.DataManager.GetExcelSheet<Mount>()
            .Where(m => m.RowId != 0 && !m.Singular.IsEmpty && playerState->IsMountUnlocked(m.RowId))
            .Select(m => (m.RowId, Name: MountName(m)))
            .OrderBy(m => m.Name, System.StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static string MountName(uint mountId) =>
        Plugin.DataManager.GetExcelSheet<Mount>().TryGetRow(mountId, out var mount) ? MountName(mount) : $"#{mountId}";

    // Lumina liefert "Singular" klein geschrieben (Grammatik-Baustein) - für die Anzeige Wortanfänge groß.
    private static string MountName(Mount mount)
    {
        var chars = mount.Singular.ToString().ToCharArray();
        var capitalizeNext = true;
        for (var i = 0; i < chars.Length; i++)
        {
            if (char.IsWhiteSpace(chars[i]) || chars[i] == '-')
            {
                capitalizeNext = true;
                continue;
            }

            if (capitalizeNext)
            {
                chars[i] = char.ToUpperInvariant(chars[i]);
                capitalizeNext = false;
            }
        }

        return new string(chars);
    }

    public static unsafe bool Dismount() => UseGeneralAction(DismountGeneralActionId);

    private static unsafe bool UseGeneralAction(uint id)
    {
        var actionManager = ActionManager.Instance();
        return actionManager != null && actionManager->UseAction(ActionType.GeneralAction, id);
    }

    /// <summary>Ob "Sprint" gerade nutzbar wäre (nicht auf Abklingzeit) - für Einstellungen -> Allgemein -> "Sprint in Städten nutzen".</summary>
    public static unsafe bool CanSprint()
    {
        var actionManager = ActionManager.Instance();
        return actionManager != null && actionManager->GetActionStatus(ActionType.GeneralAction, SprintGeneralActionId) == 0;
    }

    public static unsafe bool Sprint() => UseGeneralAction(SprintGeneralActionId);

    public static bool IsFisher() => Plugin.ObjectTable.LocalPlayer?.ClassJob.RowId == FisherClassJobId;

    /// <summary>
    /// Alle vorhandenen Ausrüstungssets (Index, Name, ClassJob-Kürzel) - für die Auswahl in den
    /// Einstellungen ("Fischer Preset", Nutzeranforderung: alle gespeicherten Gear Sets, nicht nur
    /// Fischer-Sets, damit man auch ein versehentlich falsches erkennt statt es aus der Liste zu
    /// verstecken).
    /// </summary>
    public static unsafe (int Index, string Name, string ClassJobAbbreviation)[] GetGearsets()
    {
        var gearsets = RaptureGearsetModule.Instance();
        if (gearsets == null)
            return System.Array.Empty<(int, string, string)>();

        var classJobSheet = Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.ClassJob>();
        var result = new List<(int, string, string)>();
        for (var i = 0; i < 100; i++)
        {
            var entry = gearsets->GetGearset(i);
            if (entry == null || !entry->Flags.HasFlag(RaptureGearsetModule.GearsetFlag.Exists))
                continue;

            var abbreviation = classJobSheet.TryGetRow(entry->ClassJob, out var classJob) ? classJob.Abbreviation.ToString() : "?";
            result.Add((i, entry->NameString, abbreviation));
        }

        return result.ToArray();
    }

    /// <summary>Name des Ausrüstungssets an diesem Index, oder null, wenn es das nicht (mehr) gibt - für die Anzeige des konfigurierten Fischer-Presets.</summary>
    public static unsafe string? GetGearsetName(int index)
    {
        var gearsets = RaptureGearsetModule.Instance();
        if (gearsets == null || index < 0)
            return null;

        var entry = gearsets->GetGearset(index);
        return entry != null && entry->Flags.HasFlag(RaptureGearsetModule.GearsetFlag.Exists) ? entry->NameString : null;
    }

    /// <summary>Ob das Ausrüstungsset an diesem Index für Fischer ist - false auch, wenn es das gar nicht (mehr) gibt. Für die Start-Knopf-Sperre (Nutzeranforderung: auch bei einem gewählten, aber falschen Klassen-Preset sperren).</summary>
    public static unsafe bool IsGearsetFisher(int index)
    {
        var gearsets = RaptureGearsetModule.Instance();
        if (gearsets == null || index < 0)
            return false;

        var entry = gearsets->GetGearset(index);
        return entry != null && entry->Flags.HasFlag(RaptureGearsetModule.GearsetFlag.Exists) && entry->ClassJob == FisherClassJobId;
    }

    /// <summary>Erster vorhandener Fischer-Gearset-Index, oder -1 - für den Default-Vorschlag in den Einstellungen (Nutzeranforderung).</summary>
    public static unsafe int FindDefaultFisherGearsetIndex()
    {
        var gearsets = RaptureGearsetModule.Instance();
        if (gearsets == null)
            return -1;

        for (var i = 0; i < 100; i++)
        {
            var entry = gearsets->GetGearset(i);
            if (entry != null && entry->Flags.HasFlag(RaptureGearsetModule.GearsetFlag.Exists) && entry->ClassJob == FisherClassJobId)
                return i;
        }

        return -1;
    }

    /// <summary>Wechselt auf das Ausrüstungsset mit diesem Index - false, wenn der Index ungültig ist oder das Set nicht (mehr) existiert.</summary>
    public static unsafe bool EquipGearset(int index)
    {
        var gearsets = RaptureGearsetModule.Instance();
        if (gearsets == null || index < 0)
            return false;

        var entry = gearsets->GetGearset(index);
        return entry != null && entry->Flags.HasFlag(RaptureGearsetModule.GearsetFlag.Exists) && gearsets->EquipGearset(index) == 0;
    }

    /// <summary>Dreht den eigenen Charakter in die angegebene Richtung (FFXIV-Rotation, 0 = Süden, π/2 = Osten).</summary>
    public static unsafe void Face(float rotation)
    {
        var player = Plugin.ObjectTable.LocalPlayer;
        if (player == null)
            return;

        ((FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)player.Address)->SetRotation(rotation);
    }

    private const uint QuitFishingActionId = 299; // Fischer: "Einholen"/"Quit"

    /// <summary>Beendet das Angeln (sonst sind Teleport/Aufsitzen nicht möglich).</summary>
    public static unsafe bool QuitFishing()
    {
        var actionManager = ActionManager.Instance();
        return actionManager != null && actionManager->UseAction(ActionType.Action, QuitFishingActionId);
    }

    public static unsafe bool Cast()
    {
        var actionManager = ActionManager.Instance();
        return actionManager != null && actionManager->UseAction(ActionType.Action, CastActionId);
    }

    /// <summary>
    /// Ob "Auswerfen" JETZT tatsächlich ausführbar wäre (0 = keine Einschränkung: nicht auf Abklingzeit,
    /// nicht schon am Angeln, richtige Klasse, in Reichweite von Wasser, ...) - ohne die Aktion
    /// tatsächlich auszulösen. Für die orange Markierung in der Fischdaten-Liste (siehe
    /// FishingAutomation.IsAtCastablePosition) - dieselbe Prüfung, die auch die Ausgrauung der
    /// Hotbar im Spiel selbst treibt.
    /// </summary>
    public static unsafe bool CanCastFishingRod()
    {
        var actionManager = ActionManager.Instance();
        return actionManager != null && actionManager->GetActionStatus(ActionType.Action, CastActionId) == 0;
    }

    /// <summary>Wie viele Stück eines Items aktuell im Inventar liegen - für die Köder-Anzeige in Fish Data.</summary>
    public static unsafe uint GetInventoryItemCount(uint itemId)
    {
        var inventoryManager = InventoryManager.Instance();
        return inventoryManager == null ? 0 : (uint)inventoryManager->GetInventoryItemCount(itemId);
    }

    /// <summary>Setzt die Karten-Flagge auf eine Weltposition - für "Fliege zum Fisch" ohne gespeicherte Position (vnavmesh.Query.Mesh.FlagToPoint findet dazu einen erreichbaren Punkt).</summary>
    public static unsafe void SetMapFlag(uint territoryId, Vector2 worldXZ)
    {
        var agentMap = FFXIVClientStructs.FFXIV.Client.UI.Agent.AgentMap.Instance();
        var territorySheet = Plugin.DataManager.GetExcelSheet<TerritoryType>();
        if (agentMap == null || !territorySheet.TryGetRow(territoryId, out var territory))
            return;

        agentMap->SetFlagMapMarker(territoryId, territory.Map.RowId, new Vector3(worldXZ.X, 0f, worldXZ.Y));
    }

    /// <summary>
    /// Namen aller GERADE SICHTBAREN Addons (Fenster) - nur für Diagnose (siehe SpecialRoutes.cs), wenn
    /// eine erwartete Addon-Suche über den Namen (z.B. "Talk") fehlschlägt/ein unsichtbares Addon
    /// liefert, um herauszufinden, welches Addon TATSÄCHLICH gerade offen ist.
    /// </summary>
    public static unsafe IReadOnlyList<string> GetVisibleAddonNames()
    {
        // Pointer sind in Iterator-Methoden (yield) nicht erlaubt - deshalb sofort in eine Liste
        // sammeln statt yield return.
        var names = new List<string>();
        var manager = RaptureAtkUnitManager.Instance();
        if (manager == null)
            return names;

        var list = manager->AllLoadedUnitsList;
        for (var i = 0; i < list.Count; i++)
        {
            var unit = list.Entries[i].Value;
            if (unit != null && unit->IsVisible)
                names.Add(unit->NameString);
        }

        return names;
    }

    // Die vier Haupttaschen - Ätherische/Armory/Ausrüstungs-"Taschen" bewusst NICHT durchsucht (Fische
    // landen nie dort). Reihenfolge egal, da nur nach passenden ItemIds gesucht wird.
    private static readonly InventoryType[] MainInventoryBags =
    {
        InventoryType.Inventory1, InventoryType.Inventory2, InventoryType.Inventory3, InventoryType.Inventory4,
    };

    /// <summary>
    /// Alle im Hauptinventar liegenden Item-IDs aus der übergebenen Menge, je ein Eintrag PRO belegtem
    /// Slot (ein Fisch über mehrere Stacks verteilt taucht also mehrfach auf) - für "Desynthesis nach
    /// dem Angeln" (Nutzeranforderung: kein Fremd-Plugin nötig, native Alternative zu PandorasBox'
    /// "Desynth All" über AgentSalvage.SalvageItem).
    /// </summary>
    public static unsafe List<uint> FindInventoryItemIds(IReadOnlySet<uint> itemIds)
    {
        var result = new List<uint>();
        var manager = InventoryManager.Instance();
        if (manager == null)
            return result;

        foreach (var bag in MainInventoryBags)
        {
            var container = manager->GetInventoryContainer(bag);
            if (container == null)
                continue;

            for (var i = 0; i < container->GetSize(); i++)
            {
                var slot = container->GetInventorySlot(i);
                if (slot != null && !slot->IsEmpty() && itemIds.Contains(slot->GetItemId()))
                    result.Add(slot->GetItemId());
            }
        }

        return result;
    }

    /// <summary>
    /// Desynthetisiert EINEN vollen Stack eines Items im Hauptinventar über die native Spielfunktion
    /// (AgentSalvage.SalvageItem) statt über ein Fremd-Plugin wie PandorasBox (Nutzeranforderung: "ich
    /// würde ungern Pandora Box als Required Plugin einbauen"). Gibt false zurück, wenn das Item nicht
    /// (mehr) im Hauptinventar liegt (z.B. schon verarbeitet). Best-effort: der dritte Parameter von
    /// SalvageItem ist nicht dokumentiert und hier nicht live verifiziert (0 geraten, vermutlich "ohne
    /// Rückfrage") - ebenso, ob zwischen zwei Aufrufen eine Wartezeit nötig ist (siehe
    /// FishingAutomation.DesynthesisStepInterval).
    /// </summary>
    public static unsafe bool TryDesynthesizeStack(uint itemId)
    {
        var manager = InventoryManager.Instance();
        if (manager == null)
            return false;

        foreach (var bag in MainInventoryBags)
        {
            var container = manager->GetInventoryContainer(bag);
            if (container == null)
                continue;

            for (var i = 0; i < container->GetSize(); i++)
            {
                var slot = container->GetInventorySlot(i);
                if (slot == null || slot->IsEmpty() || slot->GetItemId() != itemId)
                    continue;

                var agent = AgentSalvage.Instance();
                if (agent == null)
                    return false;

                agent->SalvageItem(slot, (int)slot->GetQuantity(), 0);
                Plugin.Log.Info($"[GameActions] Desynthetisiere Item #{itemId} (Menge {slot->GetQuantity()}).");
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Aktiviert im "SalvageDialog"-Fenster die Checkbox "Desynthesize entire stack" (Nutzer-
    /// Report/Screenshot: ohne sie angehakt zu haben blieb das Fenster nach dem Öffnen untätig
    /// stehen, statt den GANZEN Stack zu desynthetisieren) - über ECommons' AddonMaster-Wrapper,
    /// genau wie ein Nutzer-Klick auf die Checkbox.
    /// </summary>
    public static unsafe bool TryEnableBulkDesynthesize()
    {
        var addon = (AtkUnitBase*)Plugin.GameGui.GetAddonByName("SalvageDialog").Address;
        if (addon == null || !addon->IsVisible)
            return false;

        var dialog = new AddonMaster.SalvageDialog((nint)addon);
        if (!dialog.BulkDesynthEnabled)
            dialog.BulkDesynthEnabled = true;

        return true;
    }

    /// <summary>
    /// Bestätigt die von AgentSalvage.SalvageItem geöffnete Auswahl im "SalvageDialog"-Fenster
    /// (Nutzer-Report: der Fisch wurde nur ausgewählt, aber nie tatsächlich desynthetisiert - das
    /// SalvageItem-Fenster wählt das Item nur an, der eigentliche "Desynthesize"-Knopf muss noch
    /// gedrückt werden). Über ECommons' AddonMaster-Wrapper (klickt den echten Knopf-Callback,
    /// genau wie ein Nutzer-Klick), statt selbst mit AtkComponentButton/FireCallback zu hantieren.
    /// </summary>
    public static unsafe bool TryConfirmDesynthesize()
    {
        var addon = (AtkUnitBase*)Plugin.GameGui.GetAddonByName("SalvageDialog").Address;
        if (addon == null || !addon->IsVisible)
            return false;

        new AddonMaster.SalvageDialog((nint)addon).Desynthesize();
        Plugin.Log.Info("[GameActions] Desynthesis bestätigt (SalvageDialog.Desynthesize).");
        return true;
    }

    /// <summary>Schließt das Ergebnis-Fenster ("SalvageResult"), das nach einer bestätigten Desynthesis erscheint.</summary>
    public static unsafe bool TryCloseSalvageResult()
    {
        var addon = (AtkUnitBase*)Plugin.GameGui.GetAddonByName("SalvageResult").Address;
        if (addon == null || !addon->IsVisible)
            return false;

        new AddonMaster.SalvageResult((nint)addon).Close();
        return true;
    }

    // Native Desynthesis-Fenster (per FFXIVClientStructs-Struct-Namen verifiziert: AddonSalvageDialog/
    // AddonSalvageResult/AddonSalvageAutoDialog/AddonSalvageItemSelector) - Sicherheitsnetz zum
    // Schließen aller vier am Ende, falls eins davon trotz TryConfirmDesynthesize/TryCloseSalvageResult
    // noch offen hängt (z.B. nach einem Timeout).
    private static readonly string[] SalvageAddonNames = { "SalvageDialog", "SalvageResult", "SalvageAutoDialog", "SalvageItemSelector" };

    /// <summary>
    /// Schließt alle nativen Desynthesis-Fenster, falls noch offen (Nutzeranforderung: nach
    /// "Desynthesis nach dem Angeln" soll das Fenster geschlossen werden) - direktes Setzen von
    /// IsVisible statt eines echten Close/Callback-Aufrufs, genau wie das Unterdrücken des
    /// Kartenfensters bei anderen Automationen.
    /// </summary>
    public static unsafe void CloseDesynthesizeWindow()
    {
        foreach (var name in SalvageAddonNames)
        {
            var addon = (AtkUnitBase*)Plugin.GameGui.GetAddonByName(name).Address;
            if (addon != null && addon->IsVisible)
                addon->IsVisible = false;
        }
    }
}
