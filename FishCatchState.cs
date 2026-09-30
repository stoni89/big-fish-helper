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
    /// Alle Item-Ids aus dem Fischer-Logbuch (FishParameter) PLUS aller Items der ItemUICategory
    /// "Fish" (jeweils mit Desynth > 0) - für "Desynthesis nach dem Angeln" (Nutzeranforderung:
    /// "wirklich alle Desynthesen die auch im nativen Desynthesis Fenster drin sind"). Das
    /// FishParameter-Sheet allein deckt z.B. Ocean-Fishing-exklusive Fänge wie "Speckled Peacock
    /// Bass"/"Goldgrouper" nicht ab (Nutzer-Report: die blieben im Inventar liegen) - die
    /// ItemUICategory ergänzt genau solche Fälle. BEWUSST als Vereinigung, nicht als Ersatz: der
    /// Kategorie-Name "Fish" ist clientseitig nicht live verifizierbar (Nutzer-Report: eine frühere
    /// Fassung, die NUR auf der Kategorie beruhte, fand dadurch plötzlich GAR KEINEN Fisch mehr) -
    /// das Fischer-Logbuch bleibt so in jedem Fall als funktionierende Mindestbasis erhalten.
    /// </summary>
    public static IReadOnlySet<uint> AllFishItemIds
    {
        get
        {
            if (allFishItemIdsCache != null)
                return allFishItemIdsCache;

            EnsureFishParameterByItemId();
            var itemIds = new HashSet<uint>(fishParameterByItemId!.Keys);

            var categorySheet = Plugin.DataManager.GetExcelSheet<ItemUICategory>(Dalamud.Game.ClientLanguage.English);
            var fishCategoryIds = categorySheet
                .Where(c => string.Equals(c.Name.ToString(), "Fish", System.StringComparison.OrdinalIgnoreCase))
                .Select(c => c.RowId)
                .ToHashSet();

            if (fishCategoryIds.Count == 0)
            {
                Plugin.Log.Warning("[FishCatchState] ItemUICategory \"Fish\" nicht gefunden - nutze nur das Fischer-Logbuch als Quelle für Desynthesis.");
            }
            else
            {
                foreach (var item in Plugin.DataManager.GetExcelSheet<Item>())
                {
                    if (fishCategoryIds.Contains(item.ItemUICategory.RowId))
                        itemIds.Add(item.RowId);
                }
            }

            allFishItemIdsCache = itemIds.Where(id => IsDesynthesizable(id)).ToHashSet();
            return allFishItemIdsCache;
        }
    }

    private static bool IsDesynthesizable(uint itemId) =>
        Plugin.DataManager.GetExcelSheet<Item>().TryGetRow(itemId, out var item) && item.Desynth > 0;

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
