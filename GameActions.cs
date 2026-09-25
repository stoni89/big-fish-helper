using System.Linq;
using System.Numerics;
using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using Lumina.Excel.Sheets;

namespace BigFishHelper;

/// <summary>Kleine Helfer für Spielaktionen der Automation (Teleport, Mount, Klassenwechsel, Auswerfen).</summary>
public static class GameActions
{
    private const uint MountRouletteGeneralActionId = 9;
    private const uint DismountGeneralActionId = 23;
    private const uint CastActionId = 289; // Fischer: "Auswerfen"
    private const uint FisherClassJobId = 18;

    /// <summary>
    /// Nächster freigeschalteter großer Ätherit in der Zielzone zur angegebenen Weltposition -
    /// Position des Ätheriten über seinen Kartenmarker (MapMarker) bestimmt.
    /// </summary>
    public static unsafe uint? FindNearestAetheryte(uint territoryId, Vector3 target)
    {
        var uiState = UIState.Instance();
        var aetherytes = Plugin.DataManager.GetExcelSheet<Aetheryte>()
            .Where(a => a.IsAetheryte && a.Territory.RowId == territoryId && uiState != null && uiState->IsAetheryteUnlocked(a.RowId))
            .ToList();
        if (aetherytes.Count == 0)
            return null;

        return aetherytes
            .Select(a => (a.RowId, Position: ResolveAetherytePosition(a)))
            .OrderBy(a => a.Position is { } p ? Vector2.DistanceSquared(new Vector2(p.X, p.Z), new Vector2(target.X, target.Z)) : float.MaxValue)
            .First().RowId;
    }

    private static Vector3? ResolveAetherytePosition(Aetheryte aetheryte)
    {
        var markerSheet = Plugin.DataManager.GetSubrowExcelSheet<MapMarker>();
        foreach (var map in Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Map>())
        {
            if (map.TerritoryType.RowId != aetheryte.Territory.RowId || !markerSheet.TryGetRow(map.MapMarkerRange, out var markers))
                continue;

            foreach (var marker in markers)
            {
                if (marker.DataType != 3 || marker.DataKey.RowId != aetheryte.RowId)
                    continue;

                var worldX = (marker.X - 1024f) * 100f / map.SizeFactor - map.OffsetX;
                var worldZ = (marker.Y - 1024f) * 100f / map.SizeFactor - map.OffsetY;
                return new Vector3(worldX, 0, worldZ);
            }
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

    public static bool IsFisher() => Plugin.ObjectTable.LocalPlayer?.ClassJob.RowId == FisherClassJobId;

    /// <summary>Wechselt auf das erste Ausrüstungsset für Fischer - false, wenn es keins gibt.</summary>
    public static unsafe bool EquipFisherGearset()
    {
        var gearsets = RaptureGearsetModule.Instance();
        if (gearsets == null)
            return false;

        for (var i = 0; i < 100; i++)
        {
            var entry = gearsets->GetGearset(i);
            if (entry == null || !entry->Flags.HasFlag(RaptureGearsetModule.GearsetFlag.Exists) || entry->ClassJob != FisherClassJobId)
                continue;

            return gearsets->EquipGearset(i) == 0;
        }

        return false;
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
}
