using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Plugin.Ipc;

namespace BigFishHelper;

/// <summary>
/// Ablauf des Big Fish Helper (Play-Seite): wartet, bis für einen der in den Einstellungen
/// angehakten Big Fish "nächstes Fenster minus Prep Timer" erreicht ist, teleportiert bei Bedarf
/// zum nächsten Ätheriten der Zone, fliegt per vnavmesh zur hinterlegten Angel-Position, wechselt
/// auf Fischer, wählt in AutoHook das zugeordnete Preset und startet das Angeln. Endet das Fenster
/// oder ist der Fisch gefangen, wird AutoHook wieder ausgeschaltet und auf den nächsten Fisch gewartet.
/// </summary>
public sealed class FishingAutomation : IDisposable
{
    private enum State
    {
        Waiting,
        Teleporting,
        WaitingForZone,
        Mounting,
        Flying,
        Landing,
        ExactPositioning,
        SwitchingJob,
        StartingAutoHook,
        Fishing,
    }

    private static readonly TimeSpan TeleportRetryInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ZoneTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan ZoneSettleDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MountRetryInterval = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MountTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan PathStartGrace = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan FlightTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan DismountRetryInterval = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan SettleDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan JobSwitchTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan FaceSettleDelay = TimeSpan.FromSeconds(1.5);
    private static readonly TimeSpan RestartInterval = TimeSpan.FromSeconds(10);
    // Angel-Positionen werden ohne spürbare Abweichung angeflogen: Flug mit kleiner Toleranz, danach
    // zu Fuß exakt drauf (siehe UpdateExactPositioning). Genau 0 meldet vnavmesh nie als "angekommen".
    private const float ArrivalTolerance = 0.1f;
    private const float ExactPositionTolerance = 0.1f;
    private const float ExactPathTolerance = 0.05f;
    private static readonly TimeSpan ExactPositioningTimeout = TimeSpan.FromSeconds(10);
    private const float ArrivedDistance = 2f;
    private const int MaxPathAttempts = 3;

    private readonly Plugin plugin;

    private readonly ICallGateSubscriber<Vector3, bool, float, bool> pathfindAndMoveCloseTo;
    private readonly ICallGateSubscriber<bool> pathIsRunning;
    private readonly ICallGateSubscriber<bool> pathfindInProgress;
    private readonly ICallGateSubscriber<object> pathStop;
    private readonly ICallGateSubscriber<List<Vector3>, bool, object> moveToPath;
    private readonly ICallGateSubscriber<float> pathGetTolerance;
    private readonly ICallGateSubscriber<float, object> pathSetTolerance;
    private float? savedPathTolerance;
    private readonly ICallGateSubscriber<bool, object> autoHookSetPluginState;
    private readonly ICallGateSubscriber<string, object> autoHookSetPreset;
    private readonly ICallGateSubscriber<uint, byte, bool> lifestreamTeleport;

    private State state = State.Waiting;
    private DateTime stateEnteredAt = DateTime.UtcNow;
    private DateTime lastActionAt = DateTime.MinValue;
    private BigFish? target;
    private FishWindow targetWindow;
    private int pathAttempts;
    private bool hasSeenPathRunning;
    private bool autoHookEnabledByUs;
    private DateTime lastQuitAt = DateTime.MinValue;

    // Ziel des aktuellen Laufwegs (eingetragene Angel-Position) + Blickrichtung dort.
    private Vector3 destination;
    private float? destinationFacing;


    public bool IsRunning { get; private set; }

    public string StatusText { get; private set; } = string.Empty;

    public FishingAutomation(Plugin plugin)
    {
        this.plugin = plugin;

        var pi = Plugin.PluginInterface;
        pathfindAndMoveCloseTo = pi.GetIpcSubscriber<Vector3, bool, float, bool>("vnavmesh.SimpleMove.PathfindAndMoveCloseTo");
        pathIsRunning = pi.GetIpcSubscriber<bool>("vnavmesh.Path.IsRunning");
        pathfindInProgress = pi.GetIpcSubscriber<bool>("vnavmesh.SimpleMove.PathfindInProgress");
        pathStop = pi.GetIpcSubscriber<object>("vnavmesh.Path.Stop");
        moveToPath = pi.GetIpcSubscriber<List<Vector3>, bool, object>("vnavmesh.Path.MoveTo");
        pathGetTolerance = pi.GetIpcSubscriber<float>("vnavmesh.Path.GetTolerance");
        pathSetTolerance = pi.GetIpcSubscriber<float, object>("vnavmesh.Path.SetTolerance");
        autoHookSetPluginState = pi.GetIpcSubscriber<bool, object>("AutoHook.SetPluginState");
        autoHookSetPreset = pi.GetIpcSubscriber<string, object>("AutoHook.SetPreset");
        lifestreamTeleport = pi.GetIpcSubscriber<uint, byte, bool>("Lifestream.Teleport");

        Plugin.Framework.Update += OnUpdate;
    }

    public void Dispose()
    {
        Plugin.Framework.Update -= OnUpdate;
        Stop();
    }

    /// <summary>Ob mindestens ein Fisch in den Einstellungen angehakt ist (Voraussetzung für Play/Start).</summary>
    public bool HasEnabledFish => plugin.Configuration.EnabledFish.Count > 0;

    public void Start()
    {
        if (!HasEnabledFish)
            return;

        IsRunning = true;
        target = null;
        SetState(State.Waiting);
        StatusText = Loc.T("Gestartet...", "Started...");
        Plugin.Log.Info("[FishingAutomation] Gestartet.");
    }

    /// <summary>Welcher Fisch gerade per "Fliege zum Fisch"-Knopf angeflogen wird (null = keiner).</summary>
    public BigFish? TestTarget => IsTest ? target : null;

    // "Fliege zum Fisch": nur zur Angel-Position fliegen, landen, zum Wasser drehen - ohne
    // Wartezeit, Fenster-Prüfung und ohne AutoHook.
    private bool IsTest { get; set; }

    /// <summary>"Fliege zum Fisch": sofort zur Angel-Position dieses Fischs fliegen (Teleport, falls nötig), dann stoppen.</summary>
    public void StartTest(BigFish fish)
    {
        if (IsRunning)
            Stop();

        IsRunning = true;
        IsTest = true;
        target = fish;
        targetWindow = new FishWindow(DateTime.UtcNow, DateTime.MaxValue);
        pathAttempts = 0;
        StatusText = Loc.T($"Fliege zu {FishName(fish)}...", $"Flying to {FishName(fish)}...");
        Plugin.Log.Info($"[FishingAutomation] Fliege zum Fisch: {FishName(fish)}.");

        if (Plugin.ClientState.TerritoryType != fish.TerritoryId)
            SetState(State.Teleporting);
        else
            PrepareDestination();
    }

    public void Stop()
    {
        if (!IsRunning)
            return;

        IsRunning = false;
        IsTest = false;
        StopPath();
        DisableAutoHook();
        target = null;
        SetState(State.Waiting);
        StatusText = Loc.T("Gestoppt.", "Stopped.");
        Plugin.Log.Info("[FishingAutomation] Gestoppt.");
    }

    private void OnUpdate(Dalamud.Plugin.Services.IFramework framework)
    {
        if (!IsRunning)
            return;

        try
        {
            Tick();
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "[FishingAutomation] Fehler - Automation gestoppt.");
            StatusText = Loc.T("Fehler - Automation gestoppt.", "Error - automation stopped.");
            Stop();
        }
    }

    private void Tick()
    {
        // Ladebildschirm / nicht eingeloggt - einfach abwarten.
        if (!Plugin.ClientState.IsLoggedIn || Plugin.ObjectTable.LocalPlayer == null
            || Plugin.Condition[ConditionFlag.BetweenAreas] || Plugin.Condition[ConditionFlag.BetweenAreas51])
            return;

        var now = DateTime.UtcNow;

        // Fisch inzwischen gefangen oder Fenster vorbei - AutoHook aus, auf den nächsten warten.
        // (Nicht bei "Fliege zum Fisch" - das fliegt nur zur Position.)
        if (target != null && state != State.Waiting && !IsTest)
        {
            if (FishCatchState.IsCaught(target.ItemId))
            {
                Finish(Loc.T($"{FishName(target)} gefangen!", $"{FishName(target)} caught!"));
                return;
            }

            if (now >= targetWindow.EndUtc)
            {
                Finish(Loc.T($"Fenster von {FishName(target)} vorbei.", $"Window of {FishName(target)} is over."));
                return;
            }
        }

        // Noch in der Angel-Haltung (z.B. vom vorherigen Fisch) - vor Teleport/Aufsitzen erst einholen.
        if (state is State.Teleporting or State.Mounting && Plugin.Condition[ConditionFlag.Fishing])
        {
            StatusText = Loc.T("Hole die Angel ein...", "Reeling in...");
            if (now - lastQuitAt > TimeSpan.FromSeconds(2))
            {
                lastQuitAt = now;
                GameActions.QuitFishing();
            }
            return;
        }

        switch (state)
        {
            case State.Waiting:
                UpdateWaiting(now);
                break;
            case State.Teleporting:
                UpdateTeleporting(now);
                break;
            case State.WaitingForZone:
                UpdateWaitingForZone(now);
                break;
            case State.Mounting:
                UpdateMounting(now);
                break;
            case State.Flying:
                UpdateFlying(now);
                break;
            case State.ExactPositioning:
                UpdateExactPositioning(now);
                break;
            case State.Landing:
                UpdateLanding(now);
                break;
            case State.SwitchingJob:
                UpdateSwitchingJob(now);
                break;
            case State.StartingAutoHook:
                UpdateStartingAutoHook(now);
                break;
            case State.Fishing:
                UpdateFishing(now);
                break;
        }
    }

    // ---- Warten auf den nächsten Fisch ----

    /// <summary>Ob für diesen Fisch eine Angel-Position eingetragen ist (sonst kann die Automation ihn nicht anfliegen).</summary>
    public bool CanReach(BigFish fish) => FishingPositionStore.Get(plugin.Configuration, fish.ItemId) != null;

    /// <summary>Angehakte, noch nicht gefangene, erreichbare Fische, samt Fenster und Startzeit (Fenster minus Prep Timer).</summary>
    public (BigFish Fish, FishWindow Window, DateTime TriggerUtc)[] GetPlannedFish(DateTime nowUtc)
    {
        var config = plugin.Configuration;
        return BigFishData.Dawntrail
            .Where(f => config.EnabledFish.Contains(f.ItemId) && CanReach(f) && !FishCatchState.IsCaught(f.ItemId))
            .Select(f => (Fish: f, Window: FishWindows.GetCurrentOrNext(f, nowUtc)))
            .Where(x => x.Window != null)
            .Select(x => (x.Fish, x.Window!.Value,
                x.Window.Value.StartUtc - TimeSpan.FromMinutes(config.FishAlertMinutes.GetValueOrDefault(x.Fish.ItemId))))
            .OrderBy(x => x.Item3)
            .ToArray();
    }

    private void UpdateWaiting(DateTime now)
    {
        var planned = GetPlannedFish(now);
        if (planned.Length == 0)
        {
            StatusText = Loc.T(
                "Kein angehakter Fisch, der angeflogen werden kann (oder alle gefangen).",
                "No enabled fish that can be reached (or all caught).");
            return;
        }

        var due = planned.Where(p => now >= p.TriggerUtc && now < p.Window.EndUtc).OrderBy(p => p.Window.EndUtc).FirstOrDefault();
        if (due.Fish == null)
        {
            var next = planned[0];
            StatusText = Loc.T(
                $"Warte auf {FishName(next.Fish)} - Start in {FormatSpan(next.TriggerUtc - now)}",
                $"Waiting for {FishName(next.Fish)} - starting in {FormatSpan(next.TriggerUtc - now)}");
            return;
        }

        target = due.Fish;
        targetWindow = due.Window;
        pathAttempts = 0;
        Plugin.Log.Info($"[FishingAutomation] Nächster Fisch: {FishName(target)} (Fenster {targetWindow.StartUtc:HH:mm:ss}-{targetWindow.EndUtc:HH:mm:ss} UTC).");

        if (Plugin.ClientState.TerritoryType != target.TerritoryId)
            SetState(State.Teleporting);
        else
            PrepareDestination();
    }

    /// <summary>Ziel festlegen (in der richtigen Zone): die eingetragene Angel-Position des Fischs.</summary>
    private void PrepareDestination()
    {
        pathAttempts = 0;

        if (FishingPositionStore.Get(plugin.Configuration, target!.ItemId) is not { } known)
        {
            var message = Loc.T($"Keine Angel-Position für {FishName(target)} eingetragen.", $"No fishing position set for {FishName(target)}.");
            if (IsTest)
                FinishTest(message);
            else
                Finish(message);
            return;
        }

        destination = known.Position;
        destinationFacing = known.Facing;
        SetState(State.Mounting);
    }

    // ---- Teleport in die Zone ----

    private void UpdateTeleporting(DateTime now)
    {
        StatusText = Loc.T($"Teleportiere zu {FishName(target!)}...", $"Teleporting to {FishName(target!)}...");

        if (Plugin.Condition[ConditionFlag.Casting] || now - lastActionAt < TeleportRetryInterval)
            return;

        var reference = FishingPositionStore.Get(plugin.Configuration, target!.ItemId)?.Position ?? Vector3.Zero;
        var aetheryte = GameActions.FindNearestAetheryte(target.TerritoryId, reference);
        if (aetheryte == null)
        {
            StatusText = Loc.T("Kein freigeschalteter Ätherit in der Zone - gestoppt.", "No unlocked aetheryte in the zone - stopped.");
            Stop();
            return;
        }

        lastActionAt = now;
        if (TeleportViaLifestream(aetheryte.Value) || GameActions.Teleport(aetheryte.Value))
        {
            Plugin.Log.Info($"[FishingAutomation] Teleport zu Ätherit #{aetheryte.Value}.");
            SetState(State.WaitingForZone);
        }
    }

    // Teleport über Lifestream (Pflicht-Plugin) - schlägt das fehl, wird der spieleigene Teleport genutzt.
    private bool TeleportViaLifestream(uint aetheryteId)
    {
        try
        {
            return lifestreamTeleport.HasFunction && lifestreamTeleport.InvokeFunc(aetheryteId, 0);
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[FishingAutomation] Lifestream-Teleport fehlgeschlagen - nutze den normalen Teleport.");
            return false;
        }
    }

    private void UpdateWaitingForZone(DateTime now)
    {
        StatusText = Loc.T("Warte auf die Ankunft...", "Waiting to arrive...");

        if (Plugin.ClientState.TerritoryType == target!.TerritoryId)
        {
            if (now - stateEnteredAt >= ZoneSettleDelay)
                PrepareDestination();
            return;
        }

        // Teleport abgebrochen (z.B. unterbrochen) - erneut versuchen.
        if (now - stateEnteredAt > ZoneTimeout || (!Plugin.Condition[ConditionFlag.Casting] && now - stateEnteredAt > TimeSpan.FromSeconds(10)))
            SetState(State.Teleporting);
    }

    // ---- Hinfliegen ----

    private void UpdateMounting(DateTime now)
    {
        StatusText = Loc.T("Steige auf...", "Mounting...");

        if (Plugin.Condition[ConditionFlag.Mounted] || IsNear(ArrivedDistance))
        {
            BeginPath();
            return;
        }

        if (now - stateEnteredAt > MountTimeout)
        {
            // Aufsitzen klappt nicht (z.B. hier nicht erlaubt) - zu Fuß weiter.
            BeginPath();
            return;
        }

        if (now - lastActionAt > MountRetryInterval && !Plugin.Condition[ConditionFlag.Casting])
        {
            lastActionAt = now;
            GameActions.MountRoulette();
        }
    }

    private void BeginPath()
    {
        if (IsNear(ArrivedDistance))
        {
            SetState(State.Landing);
            return;
        }

        var fly = Plugin.Condition[ConditionFlag.Mounted];
        var accepted = fly && pathfindAndMoveCloseTo.InvokeFunc(destination, true, ArrivalTolerance);
        if (!accepted)
            accepted = pathfindAndMoveCloseTo.InvokeFunc(destination, false, ArrivalTolerance);

        pathAttempts++;
        hasSeenPathRunning = false;
        SetState(State.Flying);

        if (!accepted)
            Plugin.Log.Warning("[FishingAutomation] vnavmesh hat den Weg abgelehnt.");
    }

    private void UpdateFlying(DateTime now)
    {
        StatusText = Loc.T($"Fliege zu {FishName(target!)}...", $"Flying to {FishName(target!)}...");

        var running = pathIsRunning.InvokeFunc() || pathfindInProgress.InvokeFunc();
        if (running)
        {
            hasSeenPathRunning = true;
            if (now - stateEnteredAt > FlightTimeout)
                RetryOrStop(Loc.T("Flug dauert zu lange", "Flight is taking too long"));
            return;
        }

        if (IsNear(ArrivedDistance))
        {
            SetState(State.Landing);
            return;
        }

        if (hasSeenPathRunning || now - stateEnteredAt > PathStartGrace)
            RetryOrStop(Loc.T("Angel-Position nicht erreicht", "Fishing position not reached"));
    }

    private void RetryOrStop(string reason)
    {
        StopPath();
        if (pathAttempts < MaxPathAttempts)
        {
            Plugin.Log.Info($"[FishingAutomation] {reason} - neuer Versuch.");
            SetState(State.Mounting);
            return;
        }

        StatusText = Loc.T($"{reason} - gestoppt.", $"{reason} - stopped.");
        Stop();
    }

    private void UpdateLanding(DateTime now)
    {
        StatusText = Loc.T("Lande...", "Landing...");

        if (Plugin.Condition[ConditionFlag.Mounted])
        {
            if (now - lastActionAt > DismountRetryInterval)
            {
                lastActionAt = now;
                GameActions.Dismount();
            }

            stateEnteredAt = now;
            return;
        }

        if (now - stateEnteredAt < SettleDelay || Plugin.Condition[ConditionFlag.Jumping])
            return;

        // Noch nicht exakt auf der Angel-Position - das letzte Stück zu Fuß genau draufstellen.
        if (!IsNear(ExactPositionTolerance))
        {
            SetState(State.ExactPositioning);
            return;
        }

        ArrivedAtFishingPosition();
    }

    /// <summary>
    /// Nach dem Absteigen: in gerader Linie (vnavmesh Path.MoveTo) mit enger Wegpunkt-Toleranz exakt
    /// auf die eingetragene Angel-Position laufen, danach die Toleranz wieder zurücksetzen.
    /// </summary>
    private void UpdateExactPositioning(DateTime now)
    {
        StatusText = Loc.T("Stelle mich genau auf die Angel-Position...", "Stepping exactly onto the fishing position...");

        if (lastActionAt == DateTime.MinValue)
        {
            lastActionAt = now;
            try
            {
                savedPathTolerance ??= pathGetTolerance.InvokeFunc();
                pathSetTolerance.InvokeAction(ExactPathTolerance);
            }
            catch (Exception ex)
            {
                Plugin.Log.Warning(ex, "[FishingAutomation] vnavmesh-Toleranz konnte nicht gesetzt werden.");
            }

            moveToPath.InvokeAction(new List<Vector3> { destination }, false);
            return;
        }

        var running = pathIsRunning.InvokeFunc();
        if ((running || now - lastActionAt < TimeSpan.FromSeconds(0.3)) && now - stateEnteredAt < ExactPositioningTimeout)
            return;

        StopPath();
        RestorePathTolerance();
        var player = Plugin.ObjectTable.LocalPlayer;
        if (player != null)
            Plugin.Log.Info($"[FishingAutomation] Angel-Position erreicht (Abweichung {Vector3.Distance(player.Position, destination):F2}).");
        ArrivedAtFishingPosition();
    }

    private void RestorePathTolerance()
    {
        if (savedPathTolerance is not { } tolerance)
            return;

        savedPathTolerance = null;
        try
        {
            pathSetTolerance.InvokeAction(tolerance);
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[FishingAutomation] vnavmesh-Toleranz konnte nicht zurückgesetzt werden.");
        }
    }

    private void ArrivedAtFishingPosition()
    {
        // "Fliege zum Fisch": angekommen - zum Wasser drehen und fertig.
        if (IsTest)
        {
            FaceWater();
            FinishTest(Loc.T($"An der Angel-Position von {FishName(target!)} angekommen.", $"Arrived at the fishing position of {FishName(target!)}."));
            return;
        }

        SetState(State.SwitchingJob);
    }

    private void FinishTest(string message)
    {
        Plugin.Log.Info($"[FishingAutomation] {message}");
        Stop();
        StatusText = message;
    }

    // ---- Fischer, AutoHook, Angeln ----

    private void UpdateSwitchingJob(DateTime now)
    {
        StatusText = Loc.T("Wechsle auf Fischer...", "Switching to Fisher...");

        if (GameActions.IsFisher())
        {
            if (now - lastActionAt >= SettleDelay)
                SetState(State.StartingAutoHook);
            return;
        }

        if (now - lastActionAt < JobSwitchTimeout)
            return;

        if (lastActionAt != DateTime.MinValue && now - stateEnteredAt > JobSwitchTimeout * 3)
        {
            StatusText = Loc.T("Kein Ausrüstungsset für Fischer gefunden - gestoppt.", "No Fisher gear set found - stopped.");
            Stop();
            return;
        }

        lastActionAt = now;
        if (!GameActions.EquipFisherGearset())
        {
            StatusText = Loc.T("Kein Ausrüstungsset für Fischer gefunden - gestoppt.", "No Fisher gear set found - stopped.");
            Stop();
        }
    }

    private void UpdateStartingAutoHook(DateTime now)
    {
        // Erst Richtung Wasser drehen und kurz warten, damit die Drehung übernommen ist, bevor
        // AutoHook eingeschaltet und ausgeworfen wird.
        if (lastActionAt == DateTime.MinValue)
        {
            StatusText = Loc.T("Drehe Richtung Wasser...", "Turning towards the water...");
            FaceWater();
            lastActionAt = now;
            return;
        }

        if (now - lastActionAt < FaceSettleDelay)
            return;

        StatusText = Loc.T("Starte AutoHook...", "Starting AutoHook...");

        var preset = plugin.Configuration.FishAutoHookPresets.GetValueOrDefault(target!.ItemId);
        if (!string.IsNullOrEmpty(preset))
        {
            autoHookSetPreset.InvokeAction(preset);
            Plugin.Log.Info($"[FishingAutomation] AutoHook-Preset '{preset}' gewählt.");
        }

        autoHookSetPluginState.InvokeAction(true);
        autoHookEnabledByUs = true;

        // Nicht selbst auswerfen, sondern AutoHooks "Start Actions" auslösen (derselbe Aufruf wie
        // der Knopf in AutoHook: FishManager.StartFishing, per Befehl "/ahstart").
        PressAutoHookStartActions();

        SetState(State.Fishing);
        lastActionAt = now; // nach SetState - sonst würde sofort erneut ausgelöst
    }

    private void UpdateFishing(DateTime now)
    {
        var remaining = now < targetWindow.StartUtc
            ? Loc.T($"Fenster in {FormatSpan(targetWindow.StartUtc - now)}", $"window in {FormatSpan(targetWindow.StartUtc - now)}")
            : Loc.T($"noch {FormatSpan(targetWindow.EndUtc - now)}", $"{FormatSpan(targetWindow.EndUtc - now)} left");
        StatusText = Loc.T($"Angle auf {FishName(target!)} ({remaining})", $"Fishing for {FishName(target!)} ({remaining})");

        // AutoHook wirft normalerweise selbst neu aus - ruht die Angel doch einmal länger, die
        // Start-Aktionen erneut auslösen.
        if (!Plugin.Condition[ConditionFlag.Fishing] && now - lastActionAt > RestartInterval)
        {
            lastActionAt = now;
            FaceWater();
            PressAutoHookStartActions();
        }
    }

    private static void PressAutoHookStartActions()
    {
        Plugin.Log.Info("[FishingAutomation] AutoHook Start Actions (/ahstart).");
        Plugin.CommandManager.ProcessCommand("/ahstart");
    }

    // Vor dem Auswerfen Richtung Wasser drehen (Blickrichtung der Angel-Position).
    private void FaceWater()
    {
        if (destinationFacing is { } facing)
            GameActions.Face(facing);
    }

    private void Finish(string message)
    {
        Plugin.Log.Info($"[FishingAutomation] {message}");
        StopPath();
        DisableAutoHook();
        StatusText = message;
        target = null;
        SetState(State.Waiting);
    }

    // ---- Hilfen ----

    private void SetState(State newState)
    {
        state = newState;
        stateEnteredAt = DateTime.UtcNow;
        lastActionAt = DateTime.MinValue;
    }

    private bool IsNear(float distance)
    {
        var player = Plugin.ObjectTable.LocalPlayer;
        return target != null && player != null && Plugin.ClientState.TerritoryType == target.TerritoryId
               && Vector3.Distance(player.Position, destination) <= distance;
    }

    private void StopPath()
    {
        RestorePathTolerance(); // enge Toleranz (siehe UpdateExactPositioning) nie stehen lassen
        try
        {
            if (pathStop.HasAction)
                pathStop.InvokeAction();
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[FishingAutomation] vnavmesh-Stop fehlgeschlagen.");
        }
    }

    private void DisableAutoHook()
    {
        if (!autoHookEnabledByUs)
            return;

        autoHookEnabledByUs = false;
        try
        {
            autoHookSetPluginState.InvokeAction(false);
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "[FishingAutomation] AutoHook konnte nicht ausgeschaltet werden.");
        }
    }

    public static string FishName(BigFish fish) =>
        Plugin.DataManager.GetExcelSheet<Lumina.Excel.Sheets.Item>().TryGetRow(fish.ItemId, out var item) ? item.Name.ToString() : $"#{fish.ItemId}";

    private static string FormatSpan(TimeSpan span)
    {
        if (span < TimeSpan.Zero)
            span = TimeSpan.Zero;
        var clock = $"{span.Hours:00}:{span.Minutes:00}:{span.Seconds:00}";
        return span.TotalDays >= 1 ? $"{(int)span.TotalDays}d {clock}" : clock;
    }
}
