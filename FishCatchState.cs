using System.Collections.Generic;
using System.Linq;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.Sheets;

namespace BigFishHelper;

/// <summary>
/// Ob ein Fisch im Fischer-Logbuch bereits als gefangen gilt - PlayerState.IsFishCaught erwartet die
/// FishParameter-RowId, nicht die Item-Id, daher wird die Zuordnung Item -> FishParameter einmal
/// aus Lumina aufgebaut.
/// </summary>
public static class FishCatchState
{
    private static Dictionary<uint, uint>? fishParameterByItemId;

    public static unsafe bool IsCaught(uint itemId)
    {
        var fishParameterId = GetFishParameterId(itemId);
        if (fishParameterId == null)
            return false;

        var playerState = PlayerState.Instance();
        return playerState != null && playerState->IsFishCaught(fishParameterId.Value);
    }

    private static uint? GetFishParameterId(uint itemId)
    {
        EnsureFishParameterByItemId();
        return fishParameterByItemId!.TryGetValue(itemId, out var id) ? id : null;
    }

    private static HashSet<uint>? allFishItemIdsCache;

    /// <summary>
    /// Alle Item-Ids der ItemUICategory "Fish" mit Desynth > 0 - für "Desynthesis nach dem Angeln"
    /// (Nutzeranforderung: "wirklich alle Desynthesen die auch im nativen Desynthesis Fenster drin
    /// sind"). Bewusst NICHT mehr über das FishParameter-Sheet (Fischer-Logbuch) - das deckt z.B.
    /// Ocean-Fishing-exklusive Fänge wie "Speckled Peacock Bass"/"Goldgrouper" gar nicht ab (Nutzer-
    /// Report: die blieben im Inventar liegen), während die ItemUICategory JEDEN Fisch erfasst, den
    /// auch das native Desynthesis-Fenster selbst anbieten würde. Die Kategorie wird explizit auf
    /// Englisch abgefragt (unabhängig von der Client-Sprache), damit der Namensvergleich zuverlässig
    /// bleibt.
    /// </summary>
    public static IReadOnlySet<uint> AllFishItemIds
    {
        get
        {
            if (allFishItemIdsCache != null)
                return allFishItemIdsCache;

            var categorySheet = Plugin.DataManager.GetExcelSheet<ItemUICategory>(Dalamud.Game.ClientLanguage.English);
            var fishCategoryIds = categorySheet
                .Where(c => string.Equals(c.Name.ToString(), "Fish", System.StringComparison.OrdinalIgnoreCase))
                .Select(c => c.RowId)
                .ToHashSet();

            allFishItemIdsCache = Plugin.DataManager.GetExcelSheet<Item>()
                .Where(i => i.Desynth > 0 && fishCategoryIds.Contains(i.ItemUICategory.RowId))
                .Select(i => i.RowId)
                .ToHashSet();

            return allFishItemIdsCache;
        }
    }

    private static void EnsureFishParameterByItemId()
    {
        if (fishParameterByItemId != null)
            return;

        fishParameterByItemId = new Dictionary<uint, uint>();
        foreach (var row in Plugin.DataManager.GetExcelSheet<FishParameter>())
        {
            var item = row.Item.RowId;
            if (item != 0)
                fishParameterByItemId.TryAdd(item, row.RowId);
        }
    }
}
