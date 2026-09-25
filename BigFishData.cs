using System;
using System.Collections.Generic;
using System.Numerics;

namespace BigFishHelper;

/// <summary>
/// Ein Big Fish mit seinen Spawn-Bedingungen. Zeiten in Eorzea-Stunden (0-24, EndHour kann kleiner
/// als StartHour sein = über Mitternacht), Wetter als Lumina-Weather-RowIds (leer = egal).
/// PreviousWeatherSet = Wetter der vorherigen Wetterperiode (8 Eorzea-Stunden davor).
/// Predators = Fische, die für den "Fischer-Intuition"-Buff vorher gefangen werden müssen (Fisch-ItemId, Anzahl).
/// BaitIds = benötigter Köder (Item-Ids, mehrere = gleichwertige Alternativen), MoochPath = Fische, die
/// danach der Reihe nach gemoocht werden (leer = direkt mit dem Köder).
/// </summary>
public sealed record BigFish(
    uint ItemId,
    uint FishingSpotId,
    uint TerritoryId,
    float StartHour,
    float EndHour,
    uint[] WeatherSet,
    uint[] PreviousWeatherSet,
    (uint FishId, int Count)[] Predators,
    float Patch,
    uint[] BaitIds,
    uint[] MoochPath);

/// <summary>
/// Big Fish (bisher nur Dawntrail), erzeugt aus den Daten des FFXIV Fish Tracker
/// (https://github.com/icykoneko/ff14-fish-tracker-app, js/app/data.js: bigFish = true, patch &gt;= 7).
/// Namen/Orte kommen zur Laufzeit aus Lumina (Item/FishingSpot) in der Client-Sprache.
/// </summary>
// Angel-Positionen stehen in Data/FishingPositions.json (siehe FishingPositionStore).
public static class BigFishData
{
    public static readonly BigFish[] Dawntrail =
    {
        new(44339, 294, 1185, 0f, 24f, new uint[] { 4 }, Array.Empty<uint>(), Array.Empty<(uint, int)>(), 7.1f, new uint[] { 43858 }, Array.Empty<uint>()), // Icuvlo's Barter (Downripple) - Red Maggots
        new(44340, 297, 1187, 12f, 14f, Array.Empty<uint>(), Array.Empty<uint>(), Array.Empty<(uint, int)>(), 7.1f, new uint[] { 43854 }, Array.Empty<uint>()), // Moongripper (Sunken Stars) - White Worm
        new(44341, 301, 1188, 16f, 20f, new uint[] { 3, 4 }, Array.Empty<uint>(), Array.Empty<(uint, int)>(), 7.1f, new uint[] { 43858 }, Array.Empty<uint>()), // Cazuela Crab (Waters Hanu) - Red Maggots
        new(44342, 318, 1189, 20f, 24f, Array.Empty<uint>(), Array.Empty<uint>(), Array.Empty<(uint, int)>(), 7.1f, new uint[] { 43850, 43859 }, new uint[] { 43746, 43747 }), // Stardust Sleeper (Xty'iinbek Tsoly) - Crimson Lugworm/Ghost Nipper > Sharknose Goby > Yak T'el Crab
        new(44343, 308, 1189, 0f, 24f, new uint[] { 1 }, Array.Empty<uint>(), Array.Empty<(uint, int)>(), 7.1f, new uint[] { 43858 }, Array.Empty<uint>()), // Ilyon Asoh Cichlid (Xd'aa Talat Tsoly) - Red Maggots
        new(44344, 327, 1186, 0f, 4f, Array.Empty<uint>(), Array.Empty<uint>(), Array.Empty<(uint, int)>(), 7.1f, new uint[] { 43858 }, Array.Empty<uint>()), // Pixel Loach (Residential Sector) - Red Maggots
        new(44345, 319, 1190, 4f, 8f, new uint[] { 1, 2 }, Array.Empty<uint>(), Array.Empty<(uint, int)>(), 7.1f, new uint[] { 43858 }, Array.Empty<uint>()), // Hwittayoanaan Cichlid (Niikwerepi) - Red Maggots
        new(44346, 324, 1191, 9f, 11f, Array.Empty<uint>(), Array.Empty<uint>(), Array.Empty<(uint, int)>(), 7.1f, new uint[] { 43858 }, Array.Empty<uint>()), // Thunderswift Trout (The Driftdowns) - Red Maggots
        new(47988, 295, 1185, 5f, 7f, Array.Empty<uint>(), Array.Empty<uint>(), Array.Empty<(uint, int)>(), 7.2f, new uint[] { 43859 }, Array.Empty<uint>()), // Cabinkeep Permit (The For'ard Cabins) - Ghost Nipper
        new(47989, 300, 1188, 12f, 14f, new uint[] { 1 }, Array.Empty<uint>(), Array.Empty<(uint, int)>(), 7.2f, new uint[] { 43852, 43849 }, new uint[] { 43691 }), // Muttering Matamata (Bopo'uihih) - Honeybee/Golden Stonefly Nymph > Poison Dyefrog
        new(47990, 302, 1188, 0f, 4f, new uint[] { 3 }, new uint[] { 2 }, Array.Empty<(uint, int)>(), 7.2f, new uint[] { 43858 }, Array.Empty<uint>()), // Riverlong Candiru (Miyakabek'zu) - Red Maggots
        new(47991, 309, 1189, 10f, 12f, Array.Empty<uint>(), Array.Empty<uint>(), Array.Empty<(uint, int)>(), 7.2f, new uint[] { 43858 }, Array.Empty<uint>()), // Deep Canopy (Iq Br'aax Reservoir) - Red Maggots
        new(47992, 310, 1189, 0f, 24f, new uint[] { 3, 4 }, Array.Empty<uint>(), Array.Empty<(uint, int)>(), 7.2f, new uint[] { 43858 }, Array.Empty<uint>()), // Awaksbane Apoda (Yak Awak Tsoly) - Red Maggots
        new(47993, 320, 1190, 20f, 24f, new uint[] { 11 }, new uint[] { 2 }, Array.Empty<(uint, int)>(), 7.2f, new uint[] { 43855 }, new uint[] { 43751 }), // Ttokatoa (Lake Toari) - Popper Lure > Niikwerepi Trout
        new(47994, 325, 1191, 0f, 24f, new uint[] { 7 }, Array.Empty<uint>(), Array.Empty<(uint, int)>(), 7.2f, new uint[] { 43858 }, Array.Empty<uint>()), // Thunderous Flounder (Crackling Canyons) - Red Maggots
        new(47995, 328, 1192, 16f, 18f, new uint[] { 2 }, Array.Empty<uint>(), Array.Empty<(uint, int)>(), 7.2f, new uint[] { 43858 }, Array.Empty<uint>()), // Harlequin Queen (The Knowable) - Red Maggots
        new(46189, 299, 1187, 12f, 16f, new uint[] { 4 }, new uint[] { 2 }, Array.Empty<(uint, int)>(), 7.3f, new uint[] { 43858 }, Array.Empty<uint>()), // Prime Adjudicator (Karvarhur the First) - Red Maggots
        new(46190, 303, 1188, 6f, 8f, new uint[] { 7 }, Array.Empty<uint>(), Array.Empty<(uint, int)>(), 7.3f, new uint[] { 43858 }, Array.Empty<uint>()), // Crenicichla Miyaka (The Dewspun Bank) - Red Maggots
        new(46191, 311, 1189, 16f, 18f, new uint[] { 7 }, Array.Empty<uint>(), Array.Empty<(uint, int)>(), 7.3f, new uint[] { 43858 }, Array.Empty<uint>()), // Iron Shadowtongue (Iq Rrax Tsoly) - Red Maggots
        new(46192, 312, 1189, 0f, 4f, new uint[] { 3 }, new uint[] { 2 }, Array.Empty<(uint, int)>(), 7.3f, new uint[] { 43849, 43852 }, new uint[] { 43728 }), // Lotl-in-waiting (Xobr'it Tsoly) - Golden Stonefly Nymph/Honeybee > Cloud-eye Carp
        new(46193, 322, 1190, 18f, 24f, new uint[] { 6 }, new uint[] { 1, 2 }, Array.Empty<(uint, int)>(), 7.3f, new uint[] { 43857 }, Array.Empty<uint>()), // Azure Diver (Eastbound Zorgor) - Dragonfly
        new(46194, 323, 1191, 20f, 24f, new uint[] { 10 }, Array.Empty<uint>(), Array.Empty<(uint, int)>(), 7.3f, new uint[] { 43858 }, Array.Empty<uint>()), // Sprouting Perch (Outskirts Shallows) - Red Maggots
        new(46195, 329, 1192, 4f, 6f, new uint[] { 7 }, Array.Empty<uint>(), new (uint, int)[] { (43781, 3) }, 7.3f, new uint[] { 43858 }, Array.Empty<uint>()), // Gigagiant Snakehead (Mu Springs Eternal) - Red Maggots
        new(46196, 331, 1192, 8f, 12f, new uint[] { 2 }, new uint[] { 7 }, Array.Empty<(uint, int)>(), 7.3f, new uint[] { 43859 }, Array.Empty<uint>()), // Gondola Louvar (Canal Town South) - Ghost Nipper
        new(49794, 296, 1185, 16f, 18f, new uint[] { 7 }, new uint[] { 3 }, Array.Empty<(uint, int)>(), 7.4f, new uint[] { 43859 }, Array.Empty<uint>()), // Purse of Riches (High Tide Harbor) - Ghost Nipper
        new(49795, 304, 1188, 8f, 12f, new uint[] { 7 }, new uint[] { 3 }, Array.Empty<(uint, int)>(), 7.4f, new uint[] { 43849, 43852 }, new uint[] { 43701 }), // Punutiy Pain (Peaks Poga) - Golden Stonefly Nymph/Honeybee > Hunu Peacock Bass
        new(49796, 305, 1188, 4f, 6f, new uint[] { 7 }, Array.Empty<uint>(), Array.Empty<(uint, int)>(), 7.4f, new uint[] { 43858 }, Array.Empty<uint>()), // Shuckfin Dace (Ku'uxage) - Red Maggots
        new(49797, 313, 1189, 0f, 2f, new uint[] { 4 }, Array.Empty<uint>(), new (uint, int)[] { (43735, 3) }, 7.4f, new uint[] { 43858 }, Array.Empty<uint>()), // Shin Snuffler (Ankledeep) - Red Maggots
        new(49798, 314, 1189, 20f, 22f, new uint[] { 7 }, Array.Empty<uint>(), Array.Empty<(uint, int)>(), 7.4f, new uint[] { 43852 }, new uint[] { 43736 }), // Moxutural Greatgar (Cenote Moxutural) - Honeybee > Checkered Cichlid
        new(49799, 326, 1191, 12f, 16f, new uint[] { 2 }, new uint[] { 4 }, Array.Empty<(uint, int)>(), 7.4f, new uint[] { 43855 }, Array.Empty<uint>()), // Heirloom Goldgrouper (Alexandrian Ruins) - Popper Lure
        new(49800, 330, 1192, 2f, 4f, new uint[] { 7 }, new uint[] { 4 }, Array.Empty<(uint, int)>(), 7.4f, new uint[] { 43858 }, Array.Empty<uint>()), // Datnioides Aeroplanos (Leynode Aero) - Red Maggots
        new(49801, 333, 1192, 22f, 24f, new uint[] { 3 }, new uint[] { 7 }, new (uint, int)[] { (43796, 3), (43798, 3) }, 7.4f, new uint[] { 43858 }, Array.Empty<uint>()), // Esperance Carp (Proto Alexandria) - Red Maggots
        new(51999, 298, 1187, 0f, 2f, new uint[] { 15 }, new uint[] { 3 }, Array.Empty<(uint, int)>(), 7.5f, new uint[] { 43854 }, Array.Empty<uint>()), // Ole Ole Ole (Chirwagur Lake) - White Worm
        new(52000, 306, 1188, 13f, 15f, new uint[] { 4 }, new uint[] { 3 }, Array.Empty<(uint, int)>(), 7.5f, new uint[] { 43849 }, new uint[] { 43709 }), // Iron Oxydoras (Miyakabek'zoma) - Golden Stonefly Nymph > Dumplingfish
        new(52001, 307, 1188, 4f, 6f, new uint[] { 3 }, new uint[] { 7 }, Array.Empty<(uint, int)>(), 7.5f, new uint[] { 43855 }, Array.Empty<uint>()), // Excavator Catfish (Marsh Ligaka) - Popper Lure
        new(52002, 315, 1189, 16f, 21f, new uint[] { 7 }, new uint[] { 1 }, Array.Empty<(uint, int)>(), 7.5f, new uint[] { 43852 }, new uint[] { 43740 }), // Moonmarking Saucer (Cenote Jayunja) - Honeybee > Flawless Saucer
        new(52003, 316, 1189, 16f, 18f, new uint[] { 4 }, new uint[] { 7 }, Array.Empty<(uint, int)>(), 7.5f, new uint[] { 43858 }, Array.Empty<uint>()), // Autarch's Supper (Sapsweet Cenote) - Red Maggots
        new(52004, 317, 1189, 16f, 18f, new uint[] { 1 }, new uint[] { 4 }, Array.Empty<(uint, int)>(), 7.5f, new uint[] { 29717 }, new uint[] { 43743 }), // Bitterbark Caiman  (Bitterbark Cenote) - Versatile Lure > Blind Brotula
        new(52005, 321, 1190, 6f, 8f, new uint[] { 3 }, new uint[] { 6 }, new (uint, int)[] { (43760, 3) }, 7.5f, new uint[] { 43857 }, Array.Empty<uint>()), // Vagrant Keeper (Westbound Zorgor) - Dragonfly
        new(52006, 332, 1192, 8f, 13f, new uint[] { 4 }, new uint[] { 3 }, Array.Empty<(uint, int)>(), 7.5f, new uint[] { 43859 }, Array.Empty<uint>()), // Shined Copper Shark (Canal Town North) - Ghost Nipper
        new(52007, 299, 1187, 15f, 16f, new uint[] { 5 }, new uint[] { 4 }, Array.Empty<(uint, int)>(), 7.55f, new uint[] { 43858 }, Array.Empty<uint>()), // Ner Lar Dor (Karvarhur the First) - Red Maggots
        new(52008, 302, 1188, 13f, 15f, new uint[] { 8 }, new uint[] { 3 }, new (uint, int)[] { (43697, 6) }, 7.55f, new uint[] { 43855 }, new uint[] { 43697 }), // Xehabopo'u (Miyakabek'zu) - Popper Lure > Driftwood Catfish
        new(52009, 315, 1189, 16f, 21f, new uint[] { 7 }, new uint[] { 1 }, new (uint, int)[] { (52002, 1), (43740, 3) }, 7.55f, new uint[] { 43858 }, new uint[] { 43740 }), // Skyfrond (Cenote Jayunja) - Red Maggots > Flawless Saucer
        new(52010, 320, 1190, 22f, 24f, new uint[] { 11 }, new uint[] { 2 }, Array.Empty<(uint, int)>(), 7.55f, new uint[] { 43855 }, new uint[] { 43751, 47993 }), // Rroneek Bichir (Lake Toari) - Popper Lure > Niikwerepi Trout > Ttokatoa
        new(52011, 332, 1192, 8f, 13f, new uint[] { 4 }, new uint[] { 3 }, new (uint, int)[] { (52006, 1), (43795, 2) }, 7.55f, new uint[] { 43859 }, Array.Empty<uint>()), // Triple Threat (Canal Town North) - Ghost Nipper
        new(52297, 326, 1191, 2f, 4f, new uint[] { 50 }, new uint[] { 10 }, new (uint, int)[] { (43775, 1) }, 7.55f, new uint[] { 43858 }, Array.Empty<uint>()), // Great Ball of Lightning (Alexandrian Ruins) - Red Maggots
    };
}
