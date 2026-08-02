// ============================================================================
//  PROGRAM CORE — boot, tick scheduling, watchdog, fault containment
// ============================================================================

public Program()
{
    // Update10 is the sweet spot. Update1 burns instruction budget for control
    // quality nobody can see; Update100 is too coarse to fly a ship into a hole.
    Runtime.UpdateFrequency = UpdateFrequency.Update10;

    try
    {
        LoadConfig();
        ScanBlocks();
        LoadState();
        SetupIgc();
        Log("VEIN " + VEIN_VERSION + " ready (" + role + ")");
    }
    catch (Exception e)
    {
        // A crash in the constructor leaves a block that looks alive and does
        // nothing. Make it loud instead.
        faultReason = "Boot failed: " + e.Message;
        state = MinerState.Fault;
    }
}

public void Save()
{
    try { Storage = SerializeState(); }
    catch { /* A failed save must never take the world save down with it. */ }
}

public void Main(string argument, UpdateType updateSource)
{
    try
    {
        // ---- Operator input -------------------------------------------------
        if ((updateSource & (UpdateType.Terminal | UpdateType.Trigger | UpdateType.Script)) != 0
            && !string.IsNullOrWhiteSpace(argument))
        {
            HandleCommand(argument);
        }

        // ---- Inter-grid traffic --------------------------------------------
        // Drained every tick regardless of source: an unread listener queue grows
        // without bound and eventually costs more to process than it is worth.
        PumpIgc();

        // ---- Periodic work --------------------------------------------------
        if ((updateSource & (UpdateType.Update1 | UpdateType.Update10 | UpdateType.Update100)) == 0)
        {
            // Command-only invocation. Refresh the screen so the operator sees
            // the effect of what they just typed, then stop.
            Render(true);
            return;
        }

        tick++;
        double elapsed = Runtime.TimeSinceLastRun.TotalSeconds;
        // A paused game, a world load, or a laggy server can hand us a garbage
        // dt. Clamp it — an unclamped dt makes the controller apply a colossal
        // correction on the first tick back, which is a great way to fly a
        // loaded miner into a mountain.
        dt = Clamp(elapsed, 0.008, 0.5);
        clock += dt;

        if (tick - lastScanTick > RESCAN_INTERVAL) ScanBlocks();

        SampleShip();

        if (role == Role.Dispatcher) TickDispatcher();
        else TickMiner();

        Render();
        TrackLoad();
    }
    catch (Exception e)
    {
        // Anything that escapes to here is a bug. Do not let a bug fly the ship.
        EnterFault("Unhandled: " + e.Message);
        try { SafeStop(); } catch { }
        Render(true);
    }
}

// ---------------------------------------------------------------------------
//  STATE MACHINE PLUMBING
// ---------------------------------------------------------------------------

/// <summary>Transition to a new state. Idempotent — re-entering resets the timer.</summary>
void SetState(MinerState next)
{
    if (state == next)
    {
        stateTicks = 0;
        stateEntry = true;
        return;
    }
    state = next;
    stateTicks = 0;
    stateEntry = true;
    stuckTicks = 0;
}

/// <summary>
/// The watchdog. Every state is bounded in time; nothing is allowed to hang
/// forever. This is the single biggest reliability difference between VEIN and
/// the scripts it learns from — PAM in particular will sit in a docking approach
/// until the heat death of the universe if the connector never lines up.
/// </summary>
void Watchdog()
{
    if (stateTimeout <= 0) return;
    if (state == MinerState.Idle || state == MinerState.Fault) return;
    // Servicing legitimately takes as long as the batteries take.
    if (state == MinerState.Servicing) return;

    if (stateTicks * dt < stateTimeout) return;

    Log("Watchdog: " + state + " ran over " + Fmt(stateTimeout, 0) + "s");

    switch (state)
    {
        // A hung shaft is almost always a stuck ship. Back out and blacklist.
        case MinerState.Descending:
            MarkCellStuck();
            // Must be set explicitly. FinishShaft reads pendingResult when the
            // ship clears the hole, and without this it would read whatever the
            // *previous* shaft left behind — recording a cell the ship could not
            // even reach as completed, or worse, as unfinished and worth
            // retrying forever.
            pendingResult = ShaftResult.Stuck;
            ascendRetries = 0;
            SetState(MinerState.Ascending);
            break;

        // A hung *ascent* is the one case re-entry cannot fix. Sending Ascending
        // back to Ascending resets this very watchdog (SetState rearms even on a
        // self-transition), so a ship wedged in its own hole would retry until
        // the world was reloaded, marking the same cell stuck on every lap. One
        // more attempt is worth having — a snagged ascent sometimes frees itself
        // once the drills are restarted — and then it has to stop and say so.
        case MinerState.Ascending:
            if (ascendRetries >= 1)
            {
                EnterFault("Unable to withdraw from shaft");
                break;
            }
            ascendRetries++;
            MarkCellStuck();
            pendingResult = ShaftResult.Stuck;
            Log("Ascent retry " + ascendRetries);
            SetState(MinerState.Ascending);
            break;

        // Hung en route: the path is probably obstructed. Going home is the
        // safest direction because the route there is known-good.
        case MinerState.Outbound:
        case MinerState.Approaching:
        case MinerState.Selecting:
            SetState(MinerState.Inbound);
            break;

        // Hung docking is recoverable: back off and try the approach again.
        // Three failures means something is genuinely wrong with the dock.
        case MinerState.Docking:
            dockRetries++;
            if (dockRetries >= 3) EnterFault("Could not dock after 3 attempts");
            else { Log("Docking retry " + dockRetries); SetState(MinerState.Inbound); }
            break;

        // Everything else: park it and ask for help rather than guess.
        default:
            EnterFault("State " + state + " timed out");
            break;
    }
}

/// <summary>Stop, make safe, and stay that way until a human intervenes.</summary>
void EnterFault(string why)
{
    if (state == MinerState.Fault) return;
    faultReason = why;
    Log("FAULT: " + why);
    state = MinerState.Fault;
    stateTicks = 0;
    // Entry tick must fire. SafeStop is called below as well, but a state that
    // never sees stateEntry is a trap for anything added to StFault later.
    stateEntry = true;
    jobRunning = false;
    ReleaseLease(ShaftResult.Aborted);
    ReleaseAirspace();
    SafeStop();
}

/// <summary>Clear a fault and return to idle. Does not resume the job by itself.</summary>
void ClearFault()
{
    faultReason = "";
    stuckRetries = 0;
    dockRetries = 0;
    ascendRetries = 0;
    SetState(MinerState.Idle);
    Log("Fault cleared");
}

// ---------------------------------------------------------------------------
//  INSTRUCTION BUDGET
// ---------------------------------------------------------------------------

/// <summary>
/// Track how close we are to the "script too complex" ceiling. Anything above
/// about 0.8 sustained means a rescan or a fleet size is too expensive and we
/// should back off before the game kills the block.
/// </summary>
void TrackLoad()
{
    double used = Runtime.MaxInstructionCount > 0
        ? (double)Runtime.CurrentInstructionCount / Runtime.MaxInstructionCount
        : 0.0;
    // Decay slowly so a single spike stays visible for a few seconds.
    loadPeak = Math.Max(used, loadPeak * 0.97);
}

/// <summary>
/// True when we have burned enough of this tick's budget that expensive optional
/// work should be deferred. Guards the loops whose cost scales with the world:
/// block scans, fleet iteration, raycasting.
/// </summary>
bool BudgetTight(double fraction = 0.6)
{
    if (Runtime.MaxInstructionCount <= 0) return false;
    return (double)Runtime.CurrentInstructionCount / Runtime.MaxInstructionCount > fraction;
}

// ---------------------------------------------------------------------------
//  LOGGING
// ---------------------------------------------------------------------------

void Log(string msg)
{
    log.Add(Fmt(clock, 0) + "s  " + msg);
    while (log.Count > LOG_MAX) log.RemoveAt(0);
}
