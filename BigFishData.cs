using System;
using System.Collections.Generic;
using System.Numerics;

namespace BigFishHelper;

/// <summary>
/// Ein Big Fish mit seinen Spawn-Bedingungen. Zeiten in Eorzea-Stunden (0-24, EndHour kann kleiner
/// als StartHour sein = über Mitternacht), Wetter als Lumina-Weather-RowIds (leer = egal).
/// PreviousWeatherSet = Wetter der vorherigen Wetterperiode (8 Eorzea-Stunden davor).
/// Predators = Fische, die für den "Fischer-Intuition"-Buff vorher gefangen werden müssen (Fisch-ItemId, Anzahl).
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
    float Patch);

/// <summary>
/// Big Fish (bisher nur Dawntrail), erzeugt aus den Daten des FFXIV Fish Tracker
/// (https://github.com/icykoneko/ff14-fish-tracker-app, js/app/data.js: bigFish = true, patch &gt;= 7).
/// Namen/Orte kommen zur Laufzeit aus Lumina (Item/FishingSpot) in der Client-Sprache.
/// </summary>
public static class BigFishData
{
    /// <summary>
    /// Von Hand eingetragene Angel-Positionen (Weltkoordinaten) je Big Fish (Item-Id) - nur Fische
    /// mit Position kann die Automation (Play-Seite) selbst anfliegen.
    /// </summary>
    public static readonly Dictionary<uint, Vector3> FishingPositions = new()
    {
        [52009] = new Vector3(-91.882744f, -214.14522f, 469.12256f), // Skyfrond (Yak T'el, Cenote Jayunja)
    };

    // Blickrichtungen (FFXIV-Rotation in Bogenmaß) - 0 = Süden, π/2 = Osten, π = Norden, -π/2 = Westen.
    public const float FacingSouth = 0f;
    public const float FacingEast = MathF.PI / 2f;
    public const float FacingNorth = MathF.PI;
    public const float FacingWest = -MathF.PI / 2f;

    /// <summary>
    /// Blickrichtung an der Angel-Position (Richtung Wasser) je Big Fish - vor dem Auswerfen wird der
    /// Charakter so gedreht. Ohne Eintrag bleibt die Richtung nach dem Landen unverändert.
    /// </summary>
    public static readonly Dictionary<uint, float> FishingFacings = new()
    {
        [52009] = FacingEast, // Skyfrond
    };

    public static readonly BigFish[] Dawntrail =
    {
        new(44339, 294, 1185, 0f, 24f, new uint[] { 4 }, Array.Empty<uint>(), Array.Empty<(uint, int)>(), 7.1f), // Icuvlo's Barter (Downripple)
        new(44340, 297, 1187, 12f, 14f, Array.Empty<uint>(), Array.Empty<uint>(), Array.Empty<(uint, int)>(), 7.1f), // Moongripper (Sunken Stars)
        new(44341, 301, 1188, 16f, 20f, new uint[] { 3, 4 }, Array.Empty<uint>(), Array.Empty<(uint, int)>(), 7.1f), // Cazuela Crab (Waters Hanu)
        new(44342, 318, 1189, 20f, 24f, Array.Empty<uint>(), Array.Empty<uint>(), Array.Empty<(uint, int)>(), 7.1f), // Stardust Sleeper (Xty'iinbek Tsoly)
        new(44343, 308, 1189, 0f, 24f, new uint[] { 1 }, Array.Empty<uint>(), Array.Empty<(uint, int)>(), 7.1f), // Ilyon Asoh Cichlid (Xd'aa Talat Tsoly)
        new(44344, 327, 1186, 0f, 4f, Array.Empty<uint>(), Array.Empty<uint>(), Array.Empty<(uint, int)>(), 7.1f), // Pixel Loach (Residential Sector)
        new(44345, 319, 1190, 4f, 8f, new uint[] { 1, 2 }, Array.Empty<uint>(), Array.Empty<(uint, int)>(), 7.1f), // Hwittayoanaan Cichlid (Niikwerepi)
        new(44346, 324, 1191, 9f, 11f, Array.Empty<uint>(), Array.Empty<uint>(), Array.Empty<(uint, int)>(), 7.1f), // Thunderswift Trout (The Driftdowns)
        new(47988, 295, 1185, 5f, 7f, Array.Empty<uint>(), Array.Empty<uint>(), Array.Empty<(uint, int)>(), 7.2f), // Cabinkeep Permit (The For'ard Cabins)
        new(47989, 300, 1188, 12f, 14f, new uint[] { 1 }, Array.Empty<uint>(), Array.Empty<(uint, int)>(), 7.2f), // Muttering Matamata (Bopo'uihih)
        new(47990, 302, 1188, 0f, 4f, new uint[] { 3 }, new uint[] { 2 }, Array.Empty<(uint, int)>(), 7.2f), // Riverlong Candiru (Miyakabek'zu)
        new(47991, 309, 1189, 10f, 12f, Array.Empty<uint>(), Array.Empty<uint>(), Array.Empty<(uint, int)>(), 7.2f), // Deep Canopy (Iq Br'aax Reservoir)
        new(47992, 310, 1189, 0f, 24f, new uint[] { 3, 4 }, Array.Empty<uint>(), Array.Empty<(uint, int)>(), 7.2f), // Awaksbane Apoda (Yak Awak Tsoly)
        new(47993, 320, 1190, 20f, 24f, new uint[] { 11 }, new uint[] { 2 }, Array.Empty<(uint, int)>(), 7.2f), // Ttokatoa (Lake Toari)
        new(47994, 325, 1191, 0f, 24f, new uint[] { 7 }, Array.Empty<uint>(), Array.Empty<(uint, int)>(), 7.2f), // Thunderous Flounder (Crackling Canyons)
        new(47995, 328, 1192, 16f, 18f, new uint[] { 2 }, Array.Empty<uint>(), Array.Empty<(uint, int)>(), 7.2f), // Harlequin Queen (The Knowable)
        new(46189, 299, 1187, 12f, 16f, new uint[] { 4 }, new uint[] { 2 }, Array.Empty<(uint, int)>(), 7.3f), // Prime Adjudicator (Karvarhur the First)
        new(46190, 303, 1188, 6f, 8f, new uint[] { 7 }, Array.Empty<uint>(), Array.Empty<(uint, int)>(), 7.3f), // Crenicichla Miyaka (The Dewspun Bank)
        new(46191, 311, 1189, 16f, 18f, new uint[] { 7 }, Array.Empty<uint>(), Array.Empty<(uint, int)>(), 7.3f), // Iron Shadowtongue (Iq Rrax Tsoly)
        new(46192, 312, 1189, 0f, 4f, new uint[] { 3 }, new uint[] { 2 }, Array.Empty<(uint, int)>(), 7.3f), // Lotl-in-waiting (Xobr'it Tsoly)
        new(46193, 322, 1190, 18f, 24f, new uint[] { 6 }, new uint[] { 1, 2 }, Array.Empty<(uint, int)>(), 7.3f), // Azure Diver (Eastbound Zorgor)
        new(46194, 323, 1191, 20f, 24f, new uint[] { 10 }, Array.Empty<uint>(), Array.Empty<(uint, int)>(), 7.3f), // Sprouting Perch (Outskirts Shallows)
        new(46195, 329, 1192, 4f, 6f, new uint[] { 7 }, Array.Empty<uint>(), new (uint, int)[] { (43781, 3) }, 7.3f), // Gigagiant Snakehead (Mu Springs Eternal)
        new(46196, 331, 1192, 8f, 12f, new uint[] { 2 }, new uint[] { 7 }, Array.Empty<(uint, int)>(), 7.3f), // Gondola Louvar (Canal Town South)
        new(49794, 296, 1185, 16f, 18f, new uint[] { 7 }, new uint[] { 3 }, Array.Empty<(uint, int)>(), 7.4f), // Purse of Riches (High Tide Harbor)
        new(49795, 304, 1188, 8f, 12f, new uint[] { 7 }, new uint[] { 3 }, Array.Empty<(uint, int)>(), 7.4f), // Punutiy Pain (Peaks Poga)
        new(49796, 305, 1188, 4f, 6f, new uint[] { 7 }, Array.Empty<uint>(), Array.Empty<(uint, int)>(), 7.4f), // Shuckfin Dace (Ku'uxage)
        new(49797, 313, 1189, 0f, 2f, new uint[] { 4 }, Array.Empty<uint>(), new (uint, int)[] { (43735, 3) }, 7.4f), // Shin Snuffler (Ankledeep)
        new(49798, 314, 1189, 20f, 22f, new uint[] { 7 }, Array.Empty<uint>(), Array.Empty<(uint, int)>(), 7.4f), // Moxutural Greatgar (Cenote Moxutural)
        new(49799, 326, 1191, 12f, 16f, new uint[] { 2 }, new uint[] { 4 }, Array.Empty<(uint, int)>(), 7.4f), // Heirloom Goldgrouper (Alexandrian Ruins)
        new(49800, 330, 1192, 2f, 4f, new uint[] { 7 }, new uint[] { 4 }, Array.Empty<(uint, int)>(), 7.4f), // Datnioides Aeroplanos (Leynode Aero)
        new(49801, 333, 1192, 22f, 24f, new uint[] { 3 }, new uint[] { 7 }, new (uint, int)[] { (43796, 3), (43798, 3) }, 7.4f), // Esperance Carp (Proto Alexandria)
        new(51999, 298, 1187, 0f, 2f, new uint[] { 15 }, new uint[] { 3 }, Array.Empty<(uint, int)>(), 7.5f), // Ole Ole Ole (Chirwagur Lake)
        new(52000, 306, 1188, 13f, 15f, new uint[] { 4 }, new uint[] { 3 }, Array.Empty<(uint, int)>(), 7.5f), // Iron Oxydoras (Miyakabek'zoma)
        new(52001, 307, 1188, 4f, 6f, new uint[] { 3 }, new uint[] { 7 }, Array.Empty<(uint, int)>(), 7.5f), // Excavator Catfish (Marsh Ligaka)
        new(52002, 315, 1189, 16f, 21f, new uint[] { 7 }, new uint[] { 1 }, Array.Empty<(uint, int)>(), 7.5f), // Moonmarking Saucer (Cenote Jayunja)
        new(52003, 316, 1189, 16f, 18f, new uint[] { 4 }, new uint[] { 7 }, Array.Empty<(uint, int)>(), 7.5f), // Autarch's Supper (Sapsweet Cenote)
        new(52004, 317, 1189, 16f, 18f, new uint[] { 1 }, new uint[] { 4 }, Array.Empty<(uint, int)>(), 7.5f), // Bitterbark Caiman  (Bitterbark Cenote)
        new(52005, 321, 1190, 6f, 8f, new uint[] { 3 }, new uint[] { 6 }, new (uint, int)[] { (43760, 3) }, 7.5f), // Vagrant Keeper (Westbound Zorgor)
        new(52006, 332, 1192, 8f, 13f, new uint[] { 4 }, new uint[] { 3 }, Array.Empty<(uint, int)>(), 7.5f), // Shined Copper Shark (Canal Town North)
        new(52007, 299, 1187, 15f, 16f, new uint[] { 5 }, new uint[] { 4 }, Array.Empty<(uint, int)>(), 7.55f), // Ner Lar Dor (Karvarhur the First)
        new(52008, 302, 1188, 13f, 15f, new uint[] { 8 }, new uint[] { 3 }, new (uint, int)[] { (43697, 6) }, 7.55f), // Xehabopo'u (Miyakabek'zu)
        new(52009, 315, 1189, 16f, 21f, new uint[] { 7 }, new uint[] { 1 }, new (uint, int)[] { (52002, 1), (43740, 3) }, 7.55f), // Skyfrond (Cenote Jayunja)
        new(52010, 320, 1190, 22f, 24f, new uint[] { 11 }, new uint[] { 2 }, Array.Empty<(uint, int)>(), 7.55f), // Rroneek Bichir (Lake Toari)
        new(52011, 332, 1192, 8f, 13f, new uint[] { 4 }, new uint[] { 3 }, new (uint, int)[] { (52006, 1), (43795, 2) }, 7.55f), // Triple Threat (Canal Town North)
        new(52297, 326, 1191, 2f, 4f, new uint[] { 50 }, new uint[] { 10 }, new (uint, int)[] { (43775, 1) }, 7.55f), // Great Ball of Lightning (Alexandrian Ruins)
    };
}
