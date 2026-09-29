using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Game.ClientState.Conditions;
using ECommons;
using ECommons.Automation;
using ECommons.UIHelpers.AddonMasterImplementations;
using FFXIVClientStructs.FFXIV.Client.Game.Control;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace BigFishHelper;

/// <summary>
/// Schmale Schnittstelle, über die ein <see cref="SpecialRoute"/> dieselben Teleport-/vnavmesh-
/// Verbindungen von <see cref="FishingAutomation"/> mitbenutzt, statt eigene zu öffnen.
/// </summary>
internal interface ISpecialRouteHost
{
    /// <summary>Teleport per Lifestream (Fallback: spieleigener Teleport) - wie FishingAutomation.UpdateTeleporting. Nur für GROSSE Ätheriten.</summary>
    bool TeleportToAetheryte(uint aetheryteId);

    /// <summary>Teleport zu einem Aethernet-Kristall über seinen (Lumina-)Namen, z.B. "Lancer's Guild" - übernimmt Zielen/Interagieren/Menüauswahl am nächsten großen Ätherit selbst (Lifestream).</summary>
    bool TeleportToAethernetShardByName(string placeName);

    /// <summary>Laufweg per vnavmesh (immer zu Fuß, kein Mount - für Städte/Housing-Areas). true = vnavmesh hat den Weg angenommen.</summary>
    bool BeginWalkTo(Vector3 destination);

    bool IsWalkRunning();

    void StopWalk();

    /// <summary>Ob vnavmesh das Navmesh der aktuellen Zone schon fertig generiert hat (siehe FishingAutomation.UpdateWaitingForZone - direkt nach einem Zonenwechsel kann das noch einen Moment dauern).</summary>
    bool IsNavmeshReady();

    /// <summary>Ob Lifestream gerade eine eigene Aktion ausführt (Teleport/Laufweg) - zum Abwarten, bis ein per Lifestream ausgelöster Aethernet-Sprung fertig ist (der kann, anders als ein Telepo-Teleport, auch INNERHALB derselben Zone liegen, siehe HousingFerryRoute).</summary>
    bool IsLifestreamBusy();

    /// <summary>Laufweg per Lifestream (vnavmesh-unabhängig) - z.B. um über eine Zonengrenze zu laufen, an der vnavmesh nicht weiterpathfinden kann (siehe ZoneCrossingRoute).</summary>
    void BeginLifestreamMoveTo(Vector3 destination);

    /// <summary>Direkte, ungeprüfte Laufbewegung per vnavmesh (ohne Pathfinding-Validierung, einfach geradeaus zum Punkt - wie UpdateExactPositioning) - für eine kurze Vorwärtsbewegung über eine Zonengrenze, bei der ein Ziel jenseits der Grenze sonst als "nicht erreichbar" abgelehnt würde.</summary>
    void BeginDirectMoveTo(Vector3 destination);
}

internal enum SpecialRouteStepResult
{
    InProgress,
    Done,
    Failed,
}

/// <summary>
/// Manuelle Mehrschritt-Route für Fische, deren Zone nicht direkt per Ätherit erreichbar ist (siehe
/// "Spezial Fische" in der ToDo-Datei, z.B. Sweetnewt - nur per NPC-Fähre aus Old Gridania in einen
/// Housing-Ward von The Lavender Beds). Meldet <see cref="SpecialRouteStepResult.Done"/>, sobald man
/// in der Zielzone (BigFish.TerritoryId) steht - danach übernimmt FishingAutomation wieder normal
/// (WaitingForZone -> Mounting/Flying zur gespeicherten Angel-Position).
/// </summary>
internal abstract class SpecialRoute
{
    protected readonly ISpecialRouteHost Host;
    protected readonly BigFish Fish;
    protected readonly string RouteName;

    protected static readonly TimeSpan RetryInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan AethernetTeleportGracePeriod = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan AethernetTeleportTimeout = TimeSpan.FromSeconds(30);
    // Nach Ankunft (Zonenwechsel ODER Aethernet-Sprung) erst kurz absetzen lassen, bevor der nächste
    // Schritt (Laufen/Interagieren) beginnt - gleiches Prinzip wie ZoneSettleDelay in
    // FishingAutomation.UpdateWaitingForZone (Nutzeranforderung: "immer auf Teleport + Ladescreen
    // abwarten"). Zusätzlich zur reinen Zeit auch BetweenAreas/BetweenAreas51 abwarten, falls die
    // äußere Tick()-Sperre (siehe FishingAutomation.Tick) genau in dem Moment kurz durchrutscht.
    protected static readonly TimeSpan SettleDelay = TimeSpan.FromSeconds(2);

    protected DateTime LastActionAt;
    // Aethernet-Sprung: wann ausgelöst (für Grace-Period + Timeout) - siehe TickAethernetTeleport.
    private DateTime? aethernetTeleportInvokedAt;
    // Wann die Ankunftsbedingung (Zone/IsLifestreamBusy) zuerst erfüllt war - für SettleDelay, siehe
    // TickTeleport/TickAethernetTeleport. Ein gemeinsames Feld reicht, da pro Schritt nur eine der
    // beiden Methoden aktiv ist.
    private DateTime? arrivedAt;
    // Für TickForceTeleport (siehe dort) - wann der erzwungene Teleport ausgelöst wurde.
    private DateTime? forceTeleportInvokedAt;

    protected SpecialRoute(ISpecialRouteHost host, BigFish fish, string routeName)
    {
        Host = host;
        Fish = fish;
        RouteName = routeName;
    }

    public string StatusText { get; protected set; } = string.Empty;

    /// <summary>
    /// Ob dieser Sonderweg auch dann laufen soll, wenn man schon in der Zielzone steht (Standard:
    /// false - die übliche Kurzschluss-Logik in FishingAutomation.UpdateWaiting/StartTest überspringt
    /// Sonderwege dann normalerweise). NUR für Fälle wie AlwaysTeleportRoute (Nutzeranforderung:
    /// "immer zuerst zum Hauptätherit teleportieren, egal ob man schon in der Zone ist" - z.B.
    /// Rhalgr's Reach, damit der Lifestream-Laufweg mit seinen festen Wegpunkten IMMER vom selben
    /// bekannten Startpunkt losläuft, statt von einer zufälligen Stelle in der Zone).
    /// </summary>
    public virtual bool AlwaysRun => false;

    /// <summary>Auf den ersten Schritt zurücksetzen - wird bei jedem neuen Trip zu diesem Fisch aufgerufen.</summary>
    public virtual void Reset()
    {
        LastActionAt = DateTime.MinValue;
        aethernetTeleportInvokedAt = null;
        arrivedAt = null;
        forceTeleportInvokedAt = null;
    }

    public abstract SpecialRouteStepResult Tick(DateTime now);

    /// <summary>Ist die Zielzone (BigFish.TerritoryId) schon erreicht? - gemeinsame Fertig-Bedingung aller Sonderwege.</summary>
    protected bool HasArrivedInTargetZone() => Plugin.ClientState.TerritoryType == Fish.TerritoryId;

    /// <summary>Teleport zu einem GROSSEN Ätherit (Telepo/Lifestream.Teleport - normaler Welt-Teleport) - wartet auf den Zonenwechsel + SettleDelay danach.</summary>
    protected SpecialRouteStepResult TickTeleport(DateTime now, uint territoryId, Vector3 reference, Action onArrived, string statusText)
    {
        StatusText = statusText;

        if (Plugin.ClientState.TerritoryType == territoryId)
        {
            arrivedAt ??= now;
            if (now - arrivedAt.Value < SettleDelay || Plugin.Condition[ConditionFlag.BetweenAreas] || Plugin.Condition[ConditionFlag.BetweenAreas51])
                return SpecialRouteStepResult.InProgress;

            onArrived();
            LastActionAt = DateTime.MinValue;
            arrivedAt = null;
            return SpecialRouteStepResult.InProgress;
        }

        arrivedAt = null;

        // Nutzeranforderung: während der Teleport-Animation (~5s Cast) NICHT erneut auswerfen - sonst
        // könnte ein zweiter Teleport-Versuch mitten in den Cast der Kanalisierung/Ladeanimation
        // hineinplatzen (gleiche Prüfung wie FishingAutomation.UpdateTeleporting).
        if (Plugin.Condition[ConditionFlag.Casting] || now - LastActionAt < RetryInterval)
            return SpecialRouteStepResult.InProgress;

        var aetheryte = GameActions.FindNearestAetheryte(territoryId, reference);
        if (aetheryte == null)
        {
            Plugin.Log.Warning($"[{RouteName}] Kein freigeschalteter Ätherit in Zone {territoryId} gefunden - Sonderweg abgebrochen.");
            return SpecialRouteStepResult.Failed;
        }

        LastActionAt = now;
        var teleported = Host.TeleportToAetheryte(aetheryte.Value);
        Plugin.Log.Info($"[{RouteName}] Teleport zu Ätherit #{aetheryte.Value} (Zone {territoryId}): {(teleported ? "ausgelöst" : "FEHLGESCHLAGEN")}.");

        return SpecialRouteStepResult.InProgress;
    }

    /// <summary>
    /// Wie TickTeleport, aber löst den Teleport IMMER aus - auch wenn man schon in der Zielzone steht
    /// (siehe AlwaysRun/AlwaysTeleportRoute). Meldet Done, sobald der Teleport abgeschlossen +
    /// SettleDelay vorbei ist - es gibt hier keinen "onArrived"-Folgeschritt, das ist immer der letzte
    /// Schritt der Route.
    /// </summary>
    protected SpecialRouteStepResult TickForceTeleport(DateTime now, uint territoryId, Vector3 reference)
    {
        StatusText = Loc.T("Sonderweg: Teleportiere zum Hauptätherit...", "Special route: teleporting to the main aetheryte...");

        if (forceTeleportInvokedAt is { } invokedAt)
        {
            if (Plugin.Condition[ConditionFlag.Casting] || Plugin.Condition[ConditionFlag.BetweenAreas] || Plugin.Condition[ConditionFlag.BetweenAreas51])
                return SpecialRouteStepResult.InProgress;

            if (now - invokedAt < SettleDelay)
                return SpecialRouteStepResult.InProgress;

            forceTeleportInvokedAt = null;
            return SpecialRouteStepResult.Done;
        }

        if (Plugin.Condition[ConditionFlag.Casting] || now - LastActionAt < RetryInterval)
            return SpecialRouteStepResult.InProgress;

        var aetheryte = GameActions.FindNearestAetheryte(territoryId, reference);
        if (aetheryte == null)
        {
            Plugin.Log.Warning($"[{RouteName}] Kein freigeschalteter Ätherit in Zone {territoryId} gefunden - Sonderweg abgebrochen.");
            return SpecialRouteStepResult.Failed;
        }

        LastActionAt = now;
        var teleported = Host.TeleportToAetheryte(aetheryte.Value);
        Plugin.Log.Info($"[{RouteName}] Erzwungener Teleport zu Ätherit #{aetheryte.Value} (Zone {territoryId}): {(teleported ? "ausgelöst" : "FEHLGESCHLAGEN")}.");
        if (teleported)
            forceTeleportInvokedAt = now;

        return SpecialRouteStepResult.InProgress;
    }

    /// <summary>
    /// Aethernet-Sprung NAMENTLICH (Lifestream löst Zielen/Interagieren/Menüauswahl am nächsten
    /// großen Ätherit selbst auf). Wartet NICHT auf einen Zonenwechsel (anders als TickTeleport) -
    /// der Ziel-Kristall kann, anders als bei einem normalen Welt-Teleport, auch INNERHALB derselben
    /// Zone liegen (z.B. Twitchbeard: "Fishermen's Guild" liegt vermutlich noch in Limsa Lominsa
    /// Lower Decks) - stattdessen über Lifestream.IsBusy abwarten, bis der interne Lifestream-Task
    /// (hinlaufen/interagieren/auswählen) fertig ist.
    /// </summary>
    protected SpecialRouteStepResult TickAethernetTeleport(DateTime now, string aethernetShardName, Action onArrived)
    {
        StatusText = Loc.T($"Sonderweg: Springe zu {aethernetShardName}...", $"Special route: jumping to {aethernetShardName}...");

        if (aethernetTeleportInvokedAt is { } invokedAt)
        {
            // Grace-Period: Lifestream braucht einen Moment, bis IsBusy tatsächlich true meldet.
            if (now - invokedAt < AethernetTeleportGracePeriod)
                return SpecialRouteStepResult.InProgress;

            if (Host.IsLifestreamBusy())
            {
                if (now - invokedAt > AethernetTeleportTimeout)
                {
                    Plugin.Log.Warning($"[{RouteName}] Aethernet-Sprung zu \"{aethernetShardName}\" nach {AethernetTeleportTimeout.TotalSeconds:F0}s immer noch aktiv - mache trotzdem weiter.");
                }
                else
                {
                    return SpecialRouteStepResult.InProgress;
                }
            }

            // Nutzeranforderung: nach dem Sprung (kann ein voller Zonenwechsel mit Ladescreen sein,
            // z.B. Sweetnewt nach Old Gridania) immer erst absetzen lassen, bevor es weitergeht.
            arrivedAt ??= now;
            if (now - arrivedAt.Value < SettleDelay || Plugin.Condition[ConditionFlag.BetweenAreas] || Plugin.Condition[ConditionFlag.BetweenAreas51])
                return SpecialRouteStepResult.InProgress;

            Plugin.Log.Info($"[{RouteName}] Aethernet-Sprung zu \"{aethernetShardName}\" abgeschlossen.");
            onArrived();
            LastActionAt = DateTime.MinValue;
            aethernetTeleportInvokedAt = null;
            arrivedAt = null;
            return SpecialRouteStepResult.InProgress;
        }

        if (now - LastActionAt < RetryInterval)
            return SpecialRouteStepResult.InProgress;

        LastActionAt = now;
        var teleported = Host.TeleportToAethernetShardByName(aethernetShardName);
        Plugin.Log.Info($"[{RouteName}] Aethernet-Sprung zu \"{aethernetShardName}\": {(teleported ? "ausgelöst" : "FEHLGESCHLAGEN")}.");
        if (teleported)
            aethernetTeleportInvokedAt = now;

        return SpecialRouteStepResult.InProgress;
    }
}

/// <summary>Registry der Sonderwege, keyed by ItemId - gleiches Muster wie FishingAutomation.ExtraCastablePositionRadius.</summary>
internal static class SpecialRoutes
{
    private static readonly Dictionary<uint, Func<ISpecialRouteHost, BigFish, SpecialRoute>> Factories = new()
    {
        // Sweetnewt (The Lavender Beds, Item-Id 7923): kein Ätherit im Housing-Ward - nur per
        // NPC-Fähre "Romarique" in Old Gridania erreichbar.
        [7923] = (host, fish) => new HousingFerryRoute(
            host, fish, "SweetnewtRoute",
            startCityTerritoryId: 132, // New Gridania - best effort, siehe HousingFerryRoute-Kommentar.
            aethernetShardName: "Lancers' Guild", // Plural-Possessiv, siehe HousingFerryRoute-Kommentar.
            npcApproachPosition: new Vector3(179.56758f, -2.1843035f, -240.9919f),
            npcName: "Romarique",
            wantedSelectStringEntries: new[] { "Seek Passage to the Lavender Beds", "Go to specified ward." }),

        // Twitchbeard (Mist, Item-Id 7917): kein Ätherit im Housing-Ward - nur per NPC-Fähre
        // "Rerenasu" in Limsa Lominsa Lower Decks erreichbar (siehe ToDo-Datei/Nutzerangabe).
        [7917] = (host, fish) => new HousingFerryRoute(
            host, fish, "TwitchbeardRoute",
            startCityTerritoryId: 129, // Limsa Lominsa Lower Decks (128 laut Nutzer-Report falsch, dort kein Ätherit gefunden).
            aethernetShardName: "Fishermen's Guild",
            npcApproachPosition: new Vector3(-190.7669f, 0.9999907f, 210.70517f),
            npcName: "Rerenasu",
            wantedSelectStringEntries: new[] { "Seek Passage to Mist", "Go to specified ward." }),

        // Goldenfin (Limsa Lominsa Upper Decks, Item-Id 7685): kein großer Ätherit dort - nur per
        // Aethernet-Sprung von Limsa Lominsa Lower Decks aus erreichbar. KEIN NPC/Dialog nötig
        // (kein Housing-Ward) - nach dem Sprung übernimmt die normale vnavmesh-Anflugkette wieder
        // (siehe AethernetShardRoute).
        [7685] = (host, fish) => new AethernetShardRoute(
            host, fish, "GoldenfinRoute",
            startCityTerritoryId: 129, // Limsa Lominsa Lower Decks.
            aethernetShardName: "The Aftcastle"),

        // Carp Diem (Upper Black Tea Brook, Item-Id 7701): kein großer Ätherit dort - nur per
        // Aethernet-Sprung von New Gridania aus erreichbar.
        [7701] = (host, fish) => new AethernetShardRoute(
            host, fish, "CarpDiemRoute",
            startCityTerritoryId: 132, // New Gridania.
            aethernetShardName: "Mih Khetto's Amphitheatre"),

        // Matron Carp (Whispering Gorge, Item-Id 7708): kein großer Ätherit dort - nur per
        // Aethernet-Sprung von New Gridania aus erreichbar.
        [7708] = (host, fish) => new AethernetShardRoute(
            host, fish, "MatronCarpRoute",
            startCityTerritoryId: 132, // New Gridania.
            aethernetShardName: "Lancers' Guild"),

        // Spearnose (The Goblet, Item-Id 7919): kein Ätherit im Housing-Ward - nur per NPC-Fähre
        // "Ivory Sparrow" in Ul'dah erreichbar. Anders als Sweetnewt/Twitchbeard fragt dieser NPC
        // laut Nutzerangabe NICHT erst "Seek Passage to X", sondern bietet "Go to specified ward."
        // direkt an - deshalb nur EIN Eintrag in wantedSelectStringEntries. The Goblet hat zudem
        // kein vnavmesh (siehe FishingAutomation.NoVnavmeshTerritories/LifestreamWaypoints).
        [7919] = (host, fish) => new HousingFerryRoute(
            host, fish, "SpearnoseRoute",
            startCityTerritoryId: 130, // Ul'dah.
            aethernetShardName: "Miners' Guild",
            npcApproachPosition: new Vector3(31.87359f, 10.000681f, 158.34656f),
            npcName: "Ivory Sparrow",
            wantedSelectStringEntries: new[] { "Go to specified ward." }),

        // Dravanisches Hinterland (Item-Id 399) hat keinen eigenen Ätherit - nur zu Fuß über die
        // Zonengrenze aus Idyllshire erreichbar (siehe ZoneCrossingRoute). Alle fünf Fische teilen
        // sich denselben Grenzpunkt (Nutzerangabe).
        [15632] = (host, fish) => new ZoneCrossingRoute( // The Ewer King
            host, fish, "TheEwerKingRoute", startCityTerritoryId: 478, boundaryPoint: new Vector3(74.11529f, 205f, 142.3507f)),
        [16748] = (host, fish) => new ZoneCrossingRoute( // Madam Butterfly
            host, fish, "MadamButterflyRoute", startCityTerritoryId: 478, boundaryPoint: new Vector3(74.11529f, 205f, 142.3507f)),
        [16754] = (host, fish) => new ZoneCrossingRoute( // Bobgoblin Bass
            host, fish, "BobgoblinBassRoute", startCityTerritoryId: 478, boundaryPoint: new Vector3(74.11529f, 205f, 142.3507f)),
        [17585] = (host, fish) => new ZoneCrossingRoute( // The Speaker
            host, fish, "TheSpeakerRoute", startCityTerritoryId: 478, boundaryPoint: new Vector3(74.11529f, 205f, 142.3507f)),
        [17590] = (host, fish) => new ZoneCrossingRoute( // Armor Fish
            host, fish, "ArmorFishRoute", startCityTerritoryId: 478, boundaryPoint: new Vector3(74.11529f, 205f, 142.3507f)),

        // The Gambler (Shirogane, Item-Id 23064): kein Ätherit im Housing-Ward - nur per NPC-Fähre
        // "Kimachi" in Kugane erreichbar. Shirogane hat zudem kein vnavmesh (siehe
        // FishingAutomation.NoVnavmeshTerritories) - für diesen Fisch reicht der direkte Lifestream-
        // Laufweg ohne Zwischenstationen.
        [23064] = (host, fish) => new HousingFerryRoute(
            host, fish, "TheGamblerRoute",
            startCityTerritoryId: 628, // Kugane.
            aethernetShardName: "Shiokaze Hostelry",
            npcApproachPosition: new Vector3(-116.06814f, -7.01f, -41.069096f),
            npcName: "Kimachi",
            wantedSelectStringEntries: new[] { "Go to specified ward." }),

        // Princess Killifish (Shirogane, Item-Id 24213): gleicher Weg wie The Gambler, aber der
        // direkte Lifestream-Laufweg klappt hier laut Nutzerangabe nicht - braucht vier feste
        // Zwischenstationen (siehe FishingAutomation.LifestreamWaypoints).
        [24213] = (host, fish) => new HousingFerryRoute(
            host, fish, "PrincessKillifishRoute",
            startCityTerritoryId: 628, // Kugane.
            aethernetShardName: "Shiokaze Hostelry",
            npcApproachPosition: new Vector3(-116.06814f, -7.01f, -41.069096f),
            npcName: "Kimachi",
            wantedSelectStringEntries: new[] { "Go to specified ward." }),

        // Rhalgr's Reach (Item-Id 635) hat zwar einen normalen Ätherit, aber der Lifestream-Laufweg
        // dort läuft über feste Wegpunkte (siehe FishingAutomation.LifestreamWaypoints) - die brauchen
        // laut Nutzeranforderung IMMER einen frischen Teleport zum Hauptätherit als Startpunkt, auch
        // wenn man schon in der Zone steht (sonst würde der feste Wegpunkt-Pfad von einer zufälligen
        // Stelle aus losgehen). Siehe AlwaysTeleportRoute/AlwaysRun.
        [24205] = (host, fish) => new AlwaysTeleportRoute(host, fish, "WatcherCatfishRoute"),
        [23057] = (host, fish) => new AlwaysTeleportRoute(host, fish, "HookstealerRoute"),
        [24206] = (host, fish) => new AlwaysTeleportRoute(host, fish, "BloodtailZombieRoute"),
    };

    public static SpecialRoute? Get(BigFish fish, ISpecialRouteHost host) =>
        Factories.TryGetValue(fish.ItemId, out var factory) ? factory(host, fish) : null;
}

/// <summary>
/// Gemeinsamer Ablauf für Fische in Housing-Wards, die nur per NPC-Fähre erreichbar sind (siehe
/// "Spezial Fische" in der ToDo-Datei): Teleport in die Startstadt -> Lifestream-Aethernet-Sprung zum
/// Kristall in der Nähe des NPCs (kann, anders als ein normaler Ätherit-Teleport, auch INNERHALB
/// derselben Zone liegen) -> zu Fuß zum NPC -> Talk/Dialog "Seek Passage to X" -> "Go to specified
/// ward." -> Ward-Auswahl bestätigen -> warten auf den Zonenwechsel.
///
/// ACHTUNG: TerritoryIds, Aethernet-Kristallname und die Addon-Texte/-Namen für die Ward-Auswahl sind
/// ohne Live-Test nicht zu 100% sicher - siehe Log-Ausgaben bei jedem Schritt zur Kalibrierung (Vorgehen
/// wie beim Castable-Radius-Fix für andere Fische: erster Versuch nach bestem Wissen, bei Fehlschlag
/// anhand der Log-Zeilen nachjustieren).
/// </summary>
internal sealed class HousingFerryRoute : SpecialRoute
{
    private readonly uint startCityTerritoryId;
    private readonly string aethernetShardName;
    private readonly Vector3 npcApproachPosition;
    private readonly string npcName;

    // Reihenfolge der SelectString/SelectIconString-Auswahl (siehe Konstruktor) - danach (Ward-
    // Auswahl) generisch "erstbeste Auswahl, dann Select/Yes" (siehe TickDialogue).
    private readonly string[] wantedSelectStringEntries;

    private static readonly TimeSpan DialogueClickInterval = TimeSpan.FromSeconds(1);
    private const float NpcInteractRange = 4f;

    private enum Step
    {
        TeleportStartCity,
        AethernetTeleport,
        WalkToNpc,
        TargetAndInteract,
        Dialogue,
    }

    private Step step;
    private int wantedEntryIndex;
    private DateTime lastUnknownAddonLogAt = DateTime.MinValue;
    private static readonly TimeSpan UnknownAddonLogInterval = TimeSpan.FromSeconds(3);
    private DateTime? walkToNpcStartedAt;
    // Zähler aufeinanderfolgender Talk-Klicks ohne Fortschritt (siehe TickDialogue) - falls das
    // Talk-Fenster nie zu SelectString/SelectYesno weiterschaltet (z.B. weil OpenObjectInteraction/
    // InteractWithObject den Dialog nicht sauber verknüpft hat), lieber klar abbrechen statt endlos
    // ohne erkennbaren Fortschritt weiterzuklicken.
    private int talkClickStreak;
    private const int MaxTalkClickStreak = 30;
    // Gleiches Prinzip für die Ward-Auswahl (siehe TickDialogue/HousingSelectBlock) - der genaue
    // Callback-Wert für den "Select"-Knopf ist ohne Spieldaten nicht 100% sicher (best effort).
    private int wardSelectClickStreak;
    private const int MaxWardSelectClickStreak = 10;

    /// <summary>
    /// wantedSelectStringEntries: Reihenfolge der SelectString/SelectIconString-Auswahl, z.B.
    /// ["Seek Passage to X", "Go to specified ward."] (Sweetnewt/Twitchbeard) - manche NPCs (z.B.
    /// Spearnose/Ivory Sparrow) fragen laut Nutzerangabe NICHT erst "Seek Passage to X", sondern
    /// bieten "Go to specified ward." direkt als einzige/erste Option an, deshalb hier als freie
    /// Liste statt fest zwei Einträge.
    /// </summary>
    public HousingFerryRoute(
        ISpecialRouteHost host, BigFish fish, string routeName, uint startCityTerritoryId,
        string aethernetShardName, Vector3 npcApproachPosition, string npcName, string[] wantedSelectStringEntries)
        : base(host, fish, routeName)
    {
        this.startCityTerritoryId = startCityTerritoryId;
        this.aethernetShardName = aethernetShardName;
        this.npcApproachPosition = npcApproachPosition;
        this.npcName = npcName;
        this.wantedSelectStringEntries = wantedSelectStringEntries;
    }

    public override void Reset()
    {
        base.Reset();
        step = Step.TeleportStartCity;
        lastUnknownAddonLogAt = DateTime.MinValue;
        walkToNpcStartedAt = null;
        wantedEntryIndex = 0;
        talkClickStreak = 0;
        wardSelectClickStreak = 0;
    }

    public override SpecialRouteStepResult Tick(DateTime now)
    {
        if (HasArrivedInTargetZone())
            return SpecialRouteStepResult.Done;

        switch (step)
        {
            case Step.TeleportStartCity:
                return TickTeleport(now, startCityTerritoryId, Vector3.Zero, () => step = Step.AethernetTeleport,
                    Loc.T("Sonderweg: Teleportiere in die Startstadt...", "Special route: teleporting to the start city..."));

            case Step.AethernetTeleport:
                // Aethernet-Kristall NAMENTLICH (nicht "nächster Kristall zur NPC-Position" - das hatte
                // bei Sweetnewt laut Nutzer-Report fälschlich einen anderen, geometrisch näheren, aber
                // zu Fuß weiter entfernten Kristall getroffen).
                return TickAethernetTeleport(now, aethernetShardName, () => step = Step.WalkToNpc);

            case Step.WalkToNpc:
                return TickWalkToNpc(now);

            case Step.TargetAndInteract:
                return TickTargetAndInteract(now);

            case Step.Dialogue:
                return TickDialogue(now);

            default:
                return SpecialRouteStepResult.Failed;
        }
    }

    private static readonly TimeSpan WalkToNpcTimeout = TimeSpan.FromSeconds(45);

    private SpecialRouteStepResult TickWalkToNpc(DateTime now)
    {
        StatusText = Loc.T($"Sonderweg: Laufe zu {npcName}...", $"Special route: walking to {npcName}...");

        var player = Plugin.ObjectTable.LocalPlayer;
        if (player == null)
            return SpecialRouteStepResult.InProgress;

        var distance = Vector3.Distance(player.Position, npcApproachPosition);
        if (distance <= NpcInteractRange)
        {
            Host.StopWalk();
            step = Step.TargetAndInteract;
            LastActionAt = DateTime.MinValue;
            walkToNpcStartedAt = null;
            return SpecialRouteStepResult.InProgress;
        }

        // vnavmesh kann das Mesh der Zone nach dem Zonenwechsel noch generieren (siehe
        // FishingAutomation.UpdateWaitingForZone) - ohne diese Wartelogik nimmt pathfindAndMoveCloseTo
        // den Weg sonst gar nicht erst an (kein Fehler, einfach kein Fortschritt - genau das führte
        // laut Nutzer-Report zum "Hängenbleiben ohne Logs").
        if (!Host.IsNavmeshReady())
        {
            StatusText = Loc.T("Sonderweg: Warte auf vnavmesh-Navmesh...", "Special route: waiting for vnavmesh's navmesh...");
            return SpecialRouteStepResult.InProgress;
        }

        walkToNpcStartedAt ??= now;
        if (now - walkToNpcStartedAt.Value > WalkToNpcTimeout)
        {
            Plugin.Log.Warning($"[{RouteName}] Nach {WalkToNpcTimeout.TotalSeconds:F0}s immer noch {distance:F1} Yalm von {npcName} entfernt - Sonderweg abgebrochen.");
            return SpecialRouteStepResult.Failed;
        }

        if (!Host.IsWalkRunning() && now - LastActionAt > RetryInterval)
        {
            LastActionAt = now;
            var accepted = Host.BeginWalkTo(npcApproachPosition);
            Plugin.Log.Info($"[{RouteName}] Laufe zu {npcName} (noch {distance:F1} Yalm): {(accepted ? "Weg angenommen" : "vnavmesh hat den Weg abgelehnt")}.");
        }

        return SpecialRouteStepResult.InProgress;
    }

    private unsafe SpecialRouteStepResult TickTargetAndInteract(DateTime now)
    {
        StatusText = Loc.T($"Sonderweg: Spreche mit {npcName}...", $"Special route: talking to {npcName}...");

        if (now - LastActionAt < RetryInterval)
            return SpecialRouteStepResult.InProgress;

        var npc = Plugin.ObjectTable.FirstOrDefault(o => o.IsTargetable && o.Name.TextValue == npcName);
        if (npc == null)
        {
            LastActionAt = now;
            Plugin.Log.Warning($"[{RouteName}] NPC \"{npcName}\" nicht in der Nähe gefunden - versuche erneut.");
            return SpecialRouteStepResult.InProgress;
        }

        var player = Plugin.ObjectTable.LocalPlayer;
        var distance = player != null ? Vector3.Distance(player.Position, npc.Position) : float.MaxValue;

        // npcApproachPosition ist nur ein grober Anlaufpunkt in der Nähe - die TATSÄCHLICHE NPC-
        // Position kann davon ein paar Yalm abweichen (Nutzer-Report bei Sweetnewt: bei 5.7 Yalm
        // ging noch kein Talk-Fenster auf - der eigentliche Interact-Radius im Spiel ist knapper) -
        // deshalb hier zusätzlich auf die ECHTE NPC-Position nachlaufen, statt uns auf
        // npcApproachPosition/die grobe Ankunftsprüfung aus TickWalkToNpc zu verlassen.
        if (distance > NpcInteractRange)
        {
            LastActionAt = now;
            Host.StopWalk();
            var accepted = Host.BeginWalkTo(npc.Position);
            Plugin.Log.Info($"[{RouteName}] Noch {distance:F1} Yalm von \"{npcName}\" entfernt (> {NpcInteractRange:F1}) - laufe genauer hin: {(accepted ? "Weg angenommen" : "vnavmesh hat den Weg abgelehnt")}.");
            return SpecialRouteStepResult.InProgress;
        }

        LastActionAt = now;
        Plugin.Log.Info($"[{RouteName}] NPC \"{npcName}\" gefunden ({distance:F1} Yalm entfernt) - ziele/interagiere.");

        var gameObject = (GameObject*)npc.Address;
        var targetSystem = TargetSystem.Instance();
        targetSystem->SetHardTarget(gameObject);
        targetSystem->InteractWithObject(gameObject);
        step = Step.Dialogue;
        wantedEntryIndex = 0;
        return SpecialRouteStepResult.InProgress;
    }

    /// <summary>
    /// Generischer Dialog-Fortschritt: Talk wird weggeklickt, SelectString/SelectIconString sucht
    /// zuerst die erwarteten Einträge (Reihenfolge siehe wantedSelectStringEntries), danach
    /// (Ward-Auswahl) den ersten verfügbaren Eintrag, SelectYesno bestätigt mit "Yes" - bis die
    /// Zielzone erreicht ist (siehe Tick/HasArrivedInTargetZone) oder das Sonderweg-Timeout greift
    /// (siehe FishingAutomation.UpdateSpecialRoute).
    /// </summary>
    private SpecialRouteStepResult TickDialogue(DateTime now)
    {
        StatusText = Loc.T("Sonderweg: Bestätige Passage...", "Special route: confirming passage...");

        if (now - LastActionAt < DialogueClickInterval)
            return SpecialRouteStepResult.InProgress;

        // WICHTIG: nur handeln, wenn das Talk-Addon auch wirklich sichtbar/bereit ist - Dalamud
        // behält ein einmal geöffnetes Talk-Addon offenbar auch nach dem Schließen (unsichtbar)
        // vorrätig, TryGetAddonMaster findet es dann weiterhin über den Namen. Ein früherer Bug hier
        // ist bei "nicht sichtbar" sofort zurückgekehrt, OHNE die anderen Addon-Typen (SelectString/
        // SelectIconString/SelectYesno) überhaupt zu prüfen - dadurch blieb die Route hängen, obwohl
        // z.B. SelectIconString die ganze Zeit tatsächlich offen war (siehe Nutzer-Report).
        if (GenericHelpers.TryGetAddonMaster<AddonMaster.Talk>("Talk", out var talk) && talk.IsVisible && talk.IsAddonReady)
        {
            LastActionAt = now;
            talkClickStreak++;
            if (talkClickStreak > MaxTalkClickStreak)
            {
                Plugin.Log.Warning($"[{RouteName}] Talk-Fenster schaltet seit {MaxTalkClickStreak} Klicks nicht weiter (vermutlich hat Interact/Target den Dialog nicht sauber gestartet) - Sonderweg abgebrochen.");
                return SpecialRouteStepResult.Failed;
            }

            // Diagnose für "Talk-Klick schaltet nicht weiter": OccupiedInEvent/OccupiedInQuestEvent
            // sollte gesetzt sein, SOLANGE man tatsächlich in einem laufenden NPC-Gespräch steckt -
            // ist das NICHT gesetzt, ist das gefundene Talk-Fenster vermutlich losgelöst vom NPC
            // (z.B. weil Interact das Gespräch nie richtig gestartet hat) und der Klick geht ins Leere.
            var occupied = Plugin.Condition[ConditionFlag.OccupiedInEvent] || Plugin.Condition[ConditionFlag.OccupiedInQuestEvent];
            Plugin.Log.Info($"[{RouteName}] Talk-Fenster offen - klicke weiter ({talkClickStreak}/{MaxTalkClickStreak}), OccupiedInEvent={occupied}.");
            talk.Click();
            return SpecialRouteStepResult.InProgress;
        }

        talkClickStreak = 0;

        if (GenericHelpers.TryGetAddonMaster<AddonMaster.SelectYesno>("SelectYesno", out var yesno))
        {
            LastActionAt = now;
            Plugin.Log.Info($"[{RouteName}] SelectYesno offen (\"{yesno.Text}\") - bestätige mit Yes.");
            yesno.Yes();
            return SpecialRouteStepResult.InProgress;
        }

        if (GenericHelpers.TryGetAddonMaster<AddonMaster.SelectString>("SelectString", out var selectString))
            return TickSelectStringLike("SelectString", selectString.Entries.Select(e => (e.Text, (Action)e.Select)).ToList());

        // Die Ward-Auswahl ("Select"-Button laut Nutzerangabe) zeigt jeden Ward-Eintrag typischerweise
        // mit einem Symbol (Haus-Icon je Verfügbarkeit) - das ist im Gegensatz zu den beiden
        // vorherigen SelectString-Auswahlen ein eigenes Addon (SelectIconString).
        if (GenericHelpers.TryGetAddonMaster<AddonMaster.SelectIconString>("SelectIconString", out var selectIconString))
            return TickSelectStringLike("SelectIconString", selectIconString.Entries.Select(e => (e.Text, (Action)e.Select)).ToList());

        // Ward-Auswahl-Fenster ("Select Residential Ward" laut Nutzer-Screenshot) - ein eigenständiges
        // Addon ohne ECommons-AddonMaster-Wrapper (kein SelectString/SelectIconString). Der markierte
        // Ward ist bereits vorausgewählt (Nutzeranforderung: "unten rechts auf Select klicken") -
        // Callback.Fire mit Wert 0 simuliert den Klick auf den "Select"-Knopf (best effort, siehe
        // Log-Kalibrierung falls falsch).
        unsafe
        {
            if (GenericHelpers.TryGetAddonByName<AtkUnitBase>("HousingSelectBlock", out var housingSelect)
                && housingSelect != null && housingSelect->IsVisible)
            {
                LastActionAt = now;
                wardSelectClickStreak++;
                if (wardSelectClickStreak > MaxWardSelectClickStreak)
                {
                    Plugin.Log.Warning($"[{RouteName}] \"Select\" auf der Ward-Auswahl schaltet seit {MaxWardSelectClickStreak} Versuchen nicht weiter - Sonderweg abgebrochen.");
                    return SpecialRouteStepResult.Failed;
                }

                Plugin.Log.Info($"[{RouteName}] Ward-Auswahl offen - klicke \"Select\" ({wardSelectClickStreak}/{MaxWardSelectClickStreak}).");
                Callback.Fire(housingSelect, true, 0);
                return SpecialRouteStepResult.InProgress;
            }
        }

        wardSelectClickStreak = 0;

        // Kein bekanntes Dialog-Addon offen - entweder zwischen zwei Schritten (kurz abwarten) oder
        // der Zonenwechsel läuft schon (wird in Tick/HasArrivedInTargetZone erkannt). Alle paar
        // Sekunden loggen, falls die Route hier tatsächlich hängen bleibt (unbekanntes Addon) - siehe
        // Nutzer-Report "bleibt im NPC-Dialog hängen".
        if (now - lastUnknownAddonLogAt > UnknownAddonLogInterval)
        {
            lastUnknownAddonLogAt = now;
            var visibleAddons = string.Join(", ", GameActions.GetVisibleAddonNames());
            Plugin.Log.Warning($"[{RouteName}] Kein bekanntes Dialog-Addon (Talk/SelectYesno/SelectString/SelectIconString/HousingSelectBlock) offen. Aktuell sichtbare Addons: [{visibleAddons}]");
        }

        return SpecialRouteStepResult.InProgress;
    }

    private SpecialRouteStepResult TickSelectStringLike(string addonName, List<(string Text, Action Select)> entries)
    {
        LastActionAt = DateTime.UtcNow;

        if (entries.Count == 0)
        {
            Plugin.Log.Warning($"[{RouteName}] {addonName} ohne Einträge - Sonderweg abgebrochen.");
            return SpecialRouteStepResult.Failed;
        }

        var wanted = wantedEntryIndex < wantedSelectStringEntries.Length ? wantedSelectStringEntries[wantedEntryIndex] : null;
        var matchIndex = wanted != null
            ? entries.FindIndex(e => e.Text.Contains(wanted, StringComparison.OrdinalIgnoreCase))
            : -1;

        // Ward-Auswahl (nach den beiden bekannten Einträgen) - kein bekannter Text mehr, laut
        // Nutzerangabe reicht "Select" auf der ersten/bereits markierten Auswahl.
        var index = matchIndex >= 0 ? matchIndex : 0;
        var entry = entries[index];

        Plugin.Log.Info($"[{RouteName}] {addonName}-Auswahl ({entries.Count} Einträge): \"{entry.Text}\".");
        if (matchIndex >= 0)
            wantedEntryIndex++;

        entry.Select();
        return SpecialRouteStepResult.InProgress;
    }
}

/// <summary>
/// Einfacher Sonderweg für Fische, deren Zone keinen eigenen großen Ätherit hat, aber (anders als
/// <see cref="HousingFerryRoute"/>) auch keinen NPC/Dialog braucht - z.B. Goldenfin (Limsa Lominsa
/// Upper Decks): Teleport in die Startstadt -> Lifestream-Aethernet-Sprung zum benannten Kristall ->
/// fertig, die normale vnavmesh-Anflugkette (WaitingForZone -> Mounting/Flying) übernimmt danach den
/// Rest zur gespeicherten Angel-Position wie gewohnt.
/// </summary>
internal sealed class AethernetShardRoute : SpecialRoute
{
    private readonly uint startCityTerritoryId;
    private readonly string aethernetShardName;

    private enum Step
    {
        TeleportStartCity,
        AethernetTeleport,
    }

    private Step step;

    public AethernetShardRoute(ISpecialRouteHost host, BigFish fish, string routeName, uint startCityTerritoryId, string aethernetShardName)
        : base(host, fish, routeName)
    {
        this.startCityTerritoryId = startCityTerritoryId;
        this.aethernetShardName = aethernetShardName;
    }

    public override void Reset()
    {
        base.Reset();
        step = Step.TeleportStartCity;
    }

    public override SpecialRouteStepResult Tick(DateTime now)
    {
        if (HasArrivedInTargetZone())
            return SpecialRouteStepResult.Done;

        return step switch
        {
            Step.TeleportStartCity => TickTeleport(now, startCityTerritoryId, Vector3.Zero, () => step = Step.AethernetTeleport,
                Loc.T("Sonderweg: Teleportiere in die Startstadt...", "Special route: teleporting to the start city...")),
            Step.AethernetTeleport => TickAethernetTeleport(now, aethernetShardName, () => { }),
            _ => SpecialRouteStepResult.Failed,
        };
    }
}

/// <summary>
/// Sonderweg für Fische in einer Zone ganz ohne eigenen Ätherit (z.B. Dravanisches Hinterland) - nur
/// zu Fuß über die Zonengrenze aus einer Nachbarzone erreichbar: Teleport in die Startstadt -> per
/// vnavmesh zu einem Punkt kurz VOR der Zonengrenze (weiter kommt vnavmesh nicht, weil das Ziel
/// jenseits davon noch nicht geladen ist) -> die letzten Schritte über die Grenze per Lifestream
/// (vnavmesh-unabhängig) bis der Zonenwechsel (BigFish.TerritoryId) erkannt wird.
/// </summary>
internal sealed class ZoneCrossingRoute : SpecialRoute
{
    private readonly uint startCityTerritoryId;
    private readonly Vector3 boundaryPoint;

    private const float BoundaryArriveDistance = 3f;
    private static readonly TimeSpan WalkToBoundaryTimeout = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan CrossBorderTimeout = TimeSpan.FromSeconds(60);

    private enum Step
    {
        TeleportStartCity,
        WalkToBoundary,
        CrossBorder,
    }

    private Step step;
    private DateTime? stepStartedAt;

    public ZoneCrossingRoute(ISpecialRouteHost host, BigFish fish, string routeName, uint startCityTerritoryId, Vector3 boundaryPoint)
        : base(host, fish, routeName)
    {
        this.startCityTerritoryId = startCityTerritoryId;
        this.boundaryPoint = boundaryPoint;
    }

    public override void Reset()
    {
        base.Reset();
        step = Step.TeleportStartCity;
        stepStartedAt = null;
    }

    public override SpecialRouteStepResult Tick(DateTime now)
    {
        if (HasArrivedInTargetZone())
            return SpecialRouteStepResult.Done;

        return step switch
        {
            Step.TeleportStartCity => TickTeleport(now, startCityTerritoryId, Vector3.Zero, () => step = Step.WalkToBoundary,
                Loc.T("Sonderweg: Teleportiere in die Startstadt...", "Special route: teleporting to the start city...")),
            Step.WalkToBoundary => TickWalkToBoundary(now),
            Step.CrossBorder => TickCrossBorder(now),
            _ => SpecialRouteStepResult.Failed,
        };
    }

    private SpecialRouteStepResult TickWalkToBoundary(DateTime now)
    {
        StatusText = Loc.T("Sonderweg: Laufe zur Zonengrenze...", "Special route: walking to the zone border...");

        var player = Plugin.ObjectTable.LocalPlayer;
        if (player == null)
            return SpecialRouteStepResult.InProgress;

        var distance = Vector3.Distance(player.Position, boundaryPoint);
        if (distance <= BoundaryArriveDistance)
        {
            Host.StopWalk();
            step = Step.CrossBorder;
            LastActionAt = DateTime.MinValue;
            stepStartedAt = null;
            return SpecialRouteStepResult.InProgress;
        }

        if (!Host.IsNavmeshReady())
        {
            StatusText = Loc.T("Sonderweg: Warte auf vnavmesh-Navmesh...", "Special route: waiting for vnavmesh's navmesh...");
            return SpecialRouteStepResult.InProgress;
        }

        stepStartedAt ??= now;
        if (now - stepStartedAt.Value > WalkToBoundaryTimeout)
        {
            Plugin.Log.Warning($"[{RouteName}] Nach {WalkToBoundaryTimeout.TotalSeconds:F0}s immer noch {distance:F1} Yalm von der Zonengrenze entfernt - Sonderweg abgebrochen.");
            return SpecialRouteStepResult.Failed;
        }

        if (!Host.IsWalkRunning() && now - LastActionAt > RetryInterval)
        {
            LastActionAt = now;
            var accepted = Host.BeginWalkTo(boundaryPoint);
            Plugin.Log.Info($"[{RouteName}] Laufe zur Zonengrenze (noch {distance:F1} Yalm): {(accepted ? "Weg angenommen" : "vnavmesh hat den Weg abgelehnt")}.");
        }

        return SpecialRouteStepResult.InProgress;
    }

    // Wie viele Yalm geradeaus in der aktuellen Blickrichtung weitergelaufen wird, um die Zonengrenze
    // zu überqueren (Nutzeranforderung: "einfach nur in derselben Blickrichtung paar Yard weiter" -
    // Lifestream.MoveEx zu einem festen Zielpunkt hatte laut Nutzer-Report zu einem völlig falschen
    // Laufweg geführt).
    private const float CrossBorderStepDistance = 8f;
    private static readonly TimeSpan CrossBorderStepInterval = TimeSpan.FromSeconds(3);

    /// <summary>
    /// Letztes Stück über die Zonengrenze - vnavmesh kann hier nicht mehr per Pathfinding weiter (Ziel
    /// liegt jenseits der geladenen Zone), Lifestream.MoveEx zu einem festen Punkt hat laut Nutzer-
    /// Report ebenfalls einen falschen Weg genommen. Stattdessen: alle paar Sekunden einfach
    /// CrossBorderStepDistance Yalm geradeaus in der AKTUELLEN Blickrichtung weiterlaufen (per
    /// vnavmesh.Path.MoveTo - keine Pathfinding-Validierung, siehe ISpecialRouteHost.BeginDirectMoveTo),
    /// bis der Zonenwechsel erkannt wird (siehe Tick/HasArrivedInTargetZone - danach übernimmt die
    /// normale Anflugkette wieder).
    /// </summary>
    private SpecialRouteStepResult TickCrossBorder(DateTime now)
    {
        StatusText = Loc.T("Sonderweg: Überquere die Zonengrenze...", "Special route: crossing the zone border...");

        stepStartedAt ??= now;
        if (now - stepStartedAt.Value > CrossBorderTimeout)
        {
            Plugin.Log.Warning($"[{RouteName}] Nach {CrossBorderTimeout.TotalSeconds:F0}s immer noch nicht in Zone {Fish.TerritoryId} angekommen - Sonderweg abgebrochen.");
            return SpecialRouteStepResult.Failed;
        }

        if (now - LastActionAt < CrossBorderStepInterval)
            return SpecialRouteStepResult.InProgress;

        var player = Plugin.ObjectTable.LocalPlayer;
        if (player == null)
            return SpecialRouteStepResult.InProgress;

        LastActionAt = now;
        // FFXIV-Rotation: 0 = Süden, π/2 = Osten (siehe GameActions.Face) -> Vorwärtsvektor.
        var forward = new Vector3(MathF.Sin(player.Rotation), 0f, MathF.Cos(player.Rotation)) * CrossBorderStepDistance;
        var target = player.Position + forward;
        Host.BeginDirectMoveTo(target);
        Plugin.Log.Info($"[{RouteName}] Laufe {CrossBorderStepDistance:F0} Yalm geradeaus über die Zonengrenze zu {target}.");

        return SpecialRouteStepResult.InProgress;
    }
}

/// <summary>
/// Teleportiert IMMER zum Hauptätherit der Zielzone, auch wenn man schon dort steht (siehe AlwaysRun) -
/// für Fische, deren Lifestream-Laufweg mit festen Wegpunkten (siehe
/// FishingAutomation.LifestreamWaypoints) einen bekannten, konsistenten Startpunkt braucht statt von
/// einer zufälligen Stelle in der Zone loszulaufen (Nutzeranforderung, z.B. Rhalgr's Reach).
/// </summary>
internal sealed class AlwaysTeleportRoute : SpecialRoute
{
    public override bool AlwaysRun => true;

    public AlwaysTeleportRoute(ISpecialRouteHost host, BigFish fish, string routeName) : base(host, fish, routeName)
    {
    }

    public override SpecialRouteStepResult Tick(DateTime now) => TickForceTeleport(now, Fish.TerritoryId, Vector3.Zero);
}
