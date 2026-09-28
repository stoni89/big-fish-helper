using System;
using System.Collections.Generic;
using Lumina.Excel.Sheets;

namespace BigFishHelper;

/// <summary>Ein Zeitfenster (Echtzeit, UTC), in dem ein Big Fish beißt.</summary>
public readonly record struct FishWindow(DateTime StartUtc, DateTime EndUtc)
{
    public bool IsActive(DateTime nowUtc) => nowUtc >= StartUtc && nowUtc < EndUtc;
}

/// <summary>
/// Berechnet das aktuelle bzw. nächste Zeitfenster eines Big Fish aus Eorzea-Zeit und Wetter.
/// FFXIV-Wetter ist deterministisch (Zone + Zeit): feste Perioden von 1400 Echtsekunden
/// (= 8 Eorzea-Stunden) seit der Unix-Epoche, 1 Eorzea-Stunde = 175 Echtsekunden. Pro Periode wird
/// geprüft, ob Wetter (und ggf. das Wetter der Periode davor) passen, und das Zeitfenster des
/// Fischs mit der Periode geschnitten; direkt aneinander anschließende Stücke werden zusammengefasst.
/// Ergebnisse werden pro Fisch zwischengespeichert, bis das Fenster vorbei ist.
/// </summary>
public static class FishWindows
{
    private const long WeatherPeriodSeconds = 1400;
    private const double BellSeconds = 175;

    // Obergrenze für die Suche (seltene Wetter-Kombinationen können mehrere Tage dauern).
    private const int MaxPeriods = 6000; // ~97 Echt-Tage

    private static readonly Dictionary<uint, FishWindow?> Cache = new();
    private static readonly Dictionary<uint, (byte[] Rates, uint[] Weathers)?> WeatherRateCache = new();

    // Uptime-Rarität (siehe GetUptimePercent) über die nächsten UPTIME_WINDOW_COUNT Fenster -
    // identisch zu "maxWindows = 10" im Referenz-Tracker "ff14-fish-tracker-app" (js/app/
    // fishwatcher.js), dessen Wert ff14fish.carbuncleplushy.com als Prozentzahl neben "Uptime" zeigt.
    private const int UptimeWindowCount = 10;
    private static readonly Dictionary<uint, (DateTime InvalidateAtUtc, float Uptime)> UptimeCache = new();

    /// <summary>Aktuelles oder nächstes Fenster - null, falls in der Vorschau keins gefunden wurde.</summary>
    public static FishWindow? GetCurrentOrNext(BigFish fish, DateTime nowUtc)
    {
        if (Cache.TryGetValue(fish.ItemId, out var cached) && (cached == null || cached.Value.EndUtc > nowUtc))
            return cached;

        var window = Compute(fish, nowUtc);
        Cache[fish.ItemId] = window;
        return window;
    }

    /// <summary>Ob der Fisch rund um die Uhr ohne Wetterbedingung verfügbar ist.</summary>
    public static bool IsAlwaysAvailable(BigFish fish) =>
        fish.WeatherSet.Length == 0 && fish.PreviousWeatherSet.Length == 0 && fish.StartHour == 0f && fish.EndHour >= 24f;

    /// <summary>
    /// Uptime-Rarität wie auf ff14fish.carbuncleplushy.com ("die Prozentzahl neben der Uptime") - für
    /// immer verfügbare Fische 100%, sonst Summe der Fensterdauern der nächsten UptimeWindowCount
    /// Fenster geteilt durch die Gesamtzeit vom Start des ersten bis zum Ende des letzten dieser
    /// Fenster. Identischer Algorithmus wie im Referenz-Tracker "ff14-fish-tracker-app" (js/app/
    /// fish.js: uptime()), von dem auch die übrigen Fischdaten stammen (siehe BigFishData-Kommentar).
    /// Ändert sich mit der Zeit (abhängig vom aktuellen Wettermuster) - daher wie GetCurrentOrNext
    /// zwischengespeichert, bis das ERSTE der zugrunde liegenden Fenster vorbei ist. Null, falls
    /// innerhalb der Suchgrenze (MaxPeriods) weniger als zwei Fenster gefunden wurden (extrem seltene
    /// Wetter-/Zeit-Kombination) - dann wäre der Bruch (eine einzelne Fensterdauer/sich selbst)
    /// irreführend immer 100%, egal wie selten der Fisch tatsächlich ist.
    /// </summary>
    public static float? GetUptimePercent(BigFish fish, DateTime nowUtc)
    {
        if (IsAlwaysAvailable(fish))
            return 1f;

        if (UptimeCache.TryGetValue(fish.ItemId, out var cached) && nowUtc < cached.InvalidateAtUtc)
            return cached.Uptime;

        var nowUnix = new DateTimeOffset(nowUtc).ToUnixTimeSeconds();
        double? firstStart = null;
        var firstEnd = 0d;
        var lastEnd = 0d;
        var sum = 0d;
        var count = 0;
        foreach (var (start, end) in ComputeWindowsRaw(fish, nowUnix, UptimeWindowCount))
        {
            if (firstStart == null)
            {
                firstStart = start;
                firstEnd = end;
            }

            lastEnd = end;
            sum += end - start;
            count++;
        }

        if (count < 2 || firstStart == null || lastEnd <= firstStart.Value)
            return null;

        var uptime = (float)(sum / (lastEnd - firstStart.Value));
        UptimeCache[fish.ItemId] = (ToWindow(firstStart.Value, firstEnd).EndUtc, uptime);
        return uptime;
    }

    private static FishWindow? Compute(BigFish fish, DateTime nowUtc)
    {
        var nowUnix = new DateTimeOffset(nowUtc).ToUnixTimeSeconds();
        foreach (var (start, end) in ComputeWindowsRaw(fish, nowUnix, 1))
            return ToWindow(start, end);

        return null;
    }

    /// <summary>
    /// Liefert die nächsten (bis zu) maxWindows zusammenhängenden Zeitfenster des Fischs ab nowUnix
    /// (Echtzeit-Unix-Sekunden, jeweils Start/Ende) - Basis sowohl für Compute (ein Fenster) als auch
    /// GetUptimePercent (mehrere Fenster für die Rarität). Direkt aneinander anschließende Stücke
    /// (auch über Perioden-/Wetterwechsel hinweg) werden dabei zu einem Fenster zusammengefasst.
    /// </summary>
    private static IEnumerable<(double Start, double End)> ComputeWindowsRaw(BigFish fish, long nowUnix, int maxWindows)
    {
        var periodStart = nowUnix - Mod(nowUnix, WeatherPeriodSeconds);

        double? windowStart = null;
        var windowEnd = 0d;
        var found = 0;

        for (var i = 0; i < MaxPeriods && found < maxWindows; i++)
        {
            var p = periodStart + i * WeatherPeriodSeconds;
            if (!PeriodMatches(fish, p))
            {
                if (windowStart != null)
                {
                    yield return (windowStart.Value, windowEnd);
                    found++;
                    windowStart = null;
                }

                continue;
            }

            foreach (var (segStart, segEnd) in SegmentsInPeriod(fish, p))
            {
                if (windowStart == null)
                {
                    if (segEnd <= nowUnix)
                        continue;
                    windowStart = segStart;
                    windowEnd = segEnd;
                }
                else if (Math.Abs(segStart - windowEnd) < 0.5)
                {
                    windowEnd = segEnd; // schließt direkt an
                }
                else
                {
                    yield return (windowStart.Value, windowEnd);
                    found++;
                    if (found >= maxWindows)
                        yield break;

                    windowStart = segStart;
                    windowEnd = segEnd;
                }
            }

            // Fenster endet vor dem Periodenende - kein Anschluss an die nächste Periode möglich.
            if (windowStart != null && windowEnd < p + WeatherPeriodSeconds - 0.5)
            {
                yield return (windowStart.Value, windowEnd);
                found++;
                windowStart = null;
            }
        }

        if (windowStart != null && found < maxWindows)
            yield return (windowStart.Value, windowEnd);
    }

    private static FishWindow ToWindow(double startUnix, double endUnix) =>
        new(DateTimeOffset.FromUnixTimeMilliseconds((long)(startUnix * 1000)).UtcDateTime,
            DateTimeOffset.FromUnixTimeMilliseconds((long)(endUnix * 1000)).UtcDateTime);

    private static bool PeriodMatches(BigFish fish, long periodStartUnix)
    {
        if (fish.WeatherSet.Length > 0 && Array.IndexOf(fish.WeatherSet, GetWeatherId(fish.TerritoryId, periodStartUnix)) < 0)
            return false;

        if (fish.PreviousWeatherSet.Length > 0
            && Array.IndexOf(fish.PreviousWeatherSet, GetWeatherId(fish.TerritoryId, periodStartUnix - WeatherPeriodSeconds)) < 0)
            return false;

        return true;
    }

    /// <summary>Teilstücke (Echtzeit-Unix-Sekunden) des Fisch-Zeitfensters innerhalb einer Wetterperiode, zeitlich sortiert.</summary>
    private static IEnumerable<(double Start, double End)> SegmentsInPeriod(BigFish fish, long periodStartUnix)
    {
        var periodStartHour = Mod(periodStartUnix / (long)BellSeconds, 24); // 0, 8 oder 16
        var periodEndHour = periodStartHour + 8;

        // Fisch-Fenster in Eorzea-Stunden, ggf. über Mitternacht in zwei Teile zerlegt.
        var ranges = fish.StartHour < fish.EndHour
            ? new[] { ((double)fish.StartHour, (double)fish.EndHour) }
            : new[] { (0d, (double)fish.EndHour), ((double)fish.StartHour, 24d) };

        foreach (var (rangeStart, rangeEnd) in ranges)
        {
            var from = Math.Max(rangeStart, periodStartHour);
            var to = Math.Min(rangeEnd, periodEndHour);
            if (to <= from)
                continue;

            yield return (periodStartUnix + (from - periodStartHour) * BellSeconds,
                          periodStartUnix + (to - periodStartHour) * BellSeconds);
        }
    }

    // ---- Wetter (exakter FFXIV-Algorithmus, siehe FFXIVWeather/SaintCoinach) ----

    private static int CalculateWeatherTarget(long periodStartUnixSeconds)
    {
        var bell = periodStartUnixSeconds / 175;
        // Für die Berechnung ist 16:00 = 0, 00:00 = 8, 08:00 = 16.
        var increment = (uint)(bell + 8 - (bell % 8)) % 24;
        var totalDays = (uint)(periodStartUnixSeconds / 4200);

        var calcBase = totalDays * 100 + increment;
        var step1 = (calcBase << 11) ^ calcBase;
        var step2 = (step1 >> 8) ^ step1;
        return (int)(step2 % 100);
    }

    private static uint GetWeatherId(uint territoryId, long periodStartUnix)
    {
        if (GetWeatherRate(territoryId) is not { } rate)
            return 0;

        var target = CalculateWeatherTarget(periodStartUnix);
        var cumulative = 0;
        for (var i = 0; i < rate.Rates.Length; i++)
        {
            cumulative += rate.Rates[i];
            if (target < cumulative)
                return rate.Weathers[i];
        }

        return 0;
    }

    private static (byte[] Rates, uint[] Weathers)? GetWeatherRate(uint territoryId)
    {
        if (WeatherRateCache.TryGetValue(territoryId, out var cached))
            return cached;

        (byte[], uint[])? result = null;
        var territorySheet = Plugin.DataManager.GetExcelSheet<TerritoryType>();
        var weatherRateSheet = Plugin.DataManager.GetExcelSheet<WeatherRate>();
        if (territorySheet.TryGetRow(territoryId, out var territory)
            && weatherRateSheet.TryGetRow(territory.WeatherRate.RowId, out var weatherRate))
        {
            var rates = new byte[weatherRate.Rate.Count];
            var weathers = new uint[weatherRate.Weather.Count];
            for (var i = 0; i < rates.Length; i++)
                rates[i] = weatherRate.Rate[i];
            for (var i = 0; i < weathers.Length; i++)
                weathers[i] = weatherRate.Weather[i].RowId;
            result = (rates, weathers);
        }

        WeatherRateCache[territoryId] = result;
        return result;
    }

    private static long Mod(long value, long modulus) => ((value % modulus) + modulus) % modulus;
}
