using System.Collections.Generic;
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
        if (fishParameterByItemId == null)
        {
            fishParameterByItemId = new Dictionary<uint, uint>();
            foreach (var row in Plugin.DataManager.GetExcelSheet<FishParameter>())
            {
                var item = row.Item.RowId;
                if (item != 0)
                    fishParameterByItemId.TryAdd(item, row.RowId);
            }
        }

        return fishParameterByItemId.TryGetValue(itemId, out var id) ? id : null;
    }
}
