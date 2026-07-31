// ============================================================================
//  MINER STATE MACHINE
//
//  Every state is bounded in time by the watchdog, every state can be entered
//  from a cold start, and every state leaves the ship recoverable if the script
//  is recompiled halfway through it. That last property is the one that makes
//  the difference between a script you trust overnight and one you babysit.
// ============================================================================

void TickMiner()
{
    stateTicks++;

    if (recording) RecordTick();

    CheckDamage();
    Watchdog();
    UpdateOreScan();
    EjectWhileFlying();

    // Belt and braces: if we are off the connector, the thrusters are on. Full
    // stop. Something else — an Event Controller, a timer, a player — is free to
    // switch them off while docked to save power, but the moment we are flying
    // this is not negotiable. Cheap, because it only writes on a change.
    if (!Docked && state != MinerState.Idle && state != MinerState.Fault)
        SetThrusters(true);

    bool entry = stateEntry;
    stateEntry = false;

    switch (state)
    {
        case MinerState.Idle:        StIdle(entry); break;
        case MinerState.Undocking:   StUndocking(entry); break;
        case MinerState.Outbound:    StOutbound(entry); break;
        case MinerState.Selecting:   StSelecting(entry); break;
        case MinerState.Approaching: StApproaching(entry); break;
        case MinerState.Descending:  StDescending(entry); break;
        case MinerState.Ascending:   StAscending(entry); break;
        case MinerState.Inbound:     StInbound(entry); break;
        case MinerState.Docking:     StDocking(entry); break;
        case MinerState.Unloading:   StUnloading(entry); break;
        case MinerState.Servicing:   StServicing(entry); break;
        case MinerState.Fault:       StFault(entry); break;
    }

    SendHeartbeat();
}

// ---------------------------------------------------------------------------

void StIdle(bool entry)
{
    if (entry) { SafeStop(); statusLine = "Idle"; }
    if (!jobRunning || jobComplete) return;

    Health h = CheckReadiness();
    if (!h.Ok) { statusLine = "Not ready: " + h.Detail; return; }

    SetState(Docked ? MinerState.Undocking : MinerState.Outbound);
}

bool Docked
{
    get { return dockConnector != null && dockConnector.Status == MyShipConnectorStatus.Connected; }
}

// ---------------------------------------------------------------------------

void StUndocking(bool entry)
{
    if (entry)
    {
        statusLine = "Undocking";
        // Thrusters first, before anything else, and before we let go of the
        // connector. Plenty of people switch thrusters off while docked — by
        // hand or with an Event Controller — to save power. Undocking into
        // gravity with them still off is a long fall.
        SetThrusters(true);
        SetBatteryCharging(false);
        SetTanksFilling(false);       // stop hoarding, we need the gas now
        if (Docked) dockConnector.Disconnect();
        stuckRefDepth = 0;
    }

    if (Docked) { dockConnector.Disconnect(); return; }

    // Back straight out along the connector axis. Turning while still inside a
    // docking cradle is how ships lose landing gear.
    Vector3D away = dockConnector != null
        ? dockConnector.WorldMatrix.Forward
        : (controller != null ? controller.WorldMatrix.Up : Vector3D.Up);

    Vector3D clear = homeDock != null ? homeDock.Position : shipPos;
    double travelled = Vector3D.Distance(shipPos, clear);
    double needed = shipRadius * 2.5;

    if (travelled >= needed)
    {
        BeginPath(true);
        SetState(MinerState.Outbound);
        return;
    }

    FlyTo(clear + away * needed, dockSpeed * 2.0);
}

// ---------------------------------------------------------------------------

void StOutbound(bool entry)
{
    if (entry) { statusLine = "Outbound"; BeginPath(true); SetDrills(false); }

    // Turning back before arriving beats arriving with nothing left to get home.
    if (!HasReservesForWork()) { SetState(MinerState.Inbound); return; }

    if (FollowPath(true)) SetState(MinerState.Selecting);
}

// ---------------------------------------------------------------------------

void StSelecting(bool entry)
{
    if (entry)
    {
        statusLine = "Choosing shaft";
        activeCell = -1;
        awaitingLease = false;
    }

    // ---- Fleet: ask the dispatcher, do not self-assign ---------------------
    if (HasDispatcher)
    {
        if (!awaitingLease)
        {
            RequestLease();
            awaitingLease = true;
            lastRequestTick = tick;
            return;
        }

        if (activeCell >= 0) { awaitingLease = false; BeginShaft(); return; }

        // Retry, then fall back. A dispatcher that has stopped answering must not
        // be able to park the whole fleet indefinitely.
        if (tick - lastRequestTick > 60)
        {
            if (tick - lastDispatcherSeenTick > (long)(droneTimeout / Math.Max(dt, 0.01)))
            {
                Log("Dispatcher lost — continuing solo");
                dispatcherAddr = 0;
            }
            awaitingLease = false;
        }
        return;
    }

    // ---- Solo -------------------------------------------------------------
    int cell = SelectNextCell();
    if (cell < 0)
    {
        jobComplete = true;
        Log("Job complete");
        SetState(MinerState.Inbound);
        return;
    }

    activeCell = cell;
    cells[cell].State = CellState.Leased;
    BeginShaft();
}

/// <summary>Set up the per-shaft counters and head for the hole.</summary>
void BeginShaft()
{
    shaftDepth = 0;
    shaftMaxDepth = 0;
    shaftStartOre = oreAboard;
    lastOreSample = 0;
    lastOreGainDepth = 0;
    noOreTicks = 0;
    stuckRetries = 0;

    // A probe is a cheap look, capped well short of full depth. A production
    // shaft resumes from wherever a previous visit left off.
    double alreadyDug = activeCell >= 0 ? cells[activeCell].DepthReached : 0;
    shaftDepthLimit = shaftIsProbe
        ? Math.Min(probeDepth, job.Depth)
        : job.Depth;
    if (!shaftIsProbe && alreadyDug > 0) shaftMaxDepth = alreadyDug;

    SetState(MinerState.Approaching);
}

// ---------------------------------------------------------------------------

void StApproaching(bool entry)
{
    if (activeCell < 0) { SetState(MinerState.Selecting); return; }
    if (entry) { statusLine = "To shaft " + CellLabel(activeCell); SetDrills(false); }

    if (!HasReservesForWork()) { AbandonShaft(ShaftResult.Aborted); return; }

    int col = CellCol(activeCell), row = CellRow(activeCell);

    // Approach at our own altitude lane so two drones crossing the site are
    // never at the same height. Cheap, and it removes the entire class of
    // mid-air collisions that swarm scripts are notorious for.
    double standoff = transitAltitude + myLane;
    Vector3D above = job.CellMouth(col, row, standoff);

    FlyTo(ControllerTargetFor(above), cruiseSpeed * 0.5);
    Orient(job.Down, job.Forward);

    // Only start cutting once we are both over the hole and square to it.
    // Descending at an angle is what wedges a ship halfway down a shaft.
    bool overHole = distToTarget < Math.Max(1.5, shipRadius * 0.4);
    bool square = alignError < 4.0;

    if (!overHole || !square) return;

    // Last check before committing: is there anything down there at all?
    // The drills point along the shaft axis and so does a forward camera, so a
    // single raycast answers it. On an asteroid — where the job rectangle
    // routinely overhangs empty space — this saves a full descend/ascend cycle
    // per empty cell. It is skipped when no camera has charge, which costs
    // nothing but the old behaviour.
    if (!ShaftHasRock(standoff)) { SkipEmptyCell(); return; }

    SetState(MinerState.Descending);
}

/// <summary>
/// True if solid material lies within reach down the shaft, or if we could not
/// tell. Never returns false on a failed or unavailable scan — refusing to mine
/// because a camera was busy would be far worse than digging one dry hole.
/// </summary>
bool ShaftHasRock(double standoff)
{
    if (cameras.Count == 0) return true;

    double reach = standoff + Math.Min(shaftDepthLimit, 60.0);

    double hit;
    if (!TryScanAhead(reach, out hit)) return true;   // no camera had charge

    if (hit < 0) return false;                        // scanned, genuinely empty
    return hit <= standoff + 8.0;                     // rock starts about where expected
}

/// <summary>Write off a cell that turned out to be open space and move on.</summary>
void SkipEmptyCell()
{
    Log(CellLabel(activeCell) + " is open space — skipping");
    RecordShaftResult(activeCell, ShaftResult.Completed, 0, 0, 0, shaftIsProbe);
    if (activeCell >= 0 && activeCell < cells.Length)
        cells[activeCell].State = CellState.Barren;
    ReleaseLeaseLocal();
    activeCell = -1;
    SetState(MinerState.Selecting);
}

// ---------------------------------------------------------------------------

void StDescending(bool entry)
{
    if (activeCell < 0) { SetState(MinerState.Selecting); return; }

    int col = CellCol(activeCell), row = CellRow(activeCell);

    if (entry)
    {
        statusLine = (shaftIsProbe ? "Probing " : "Drilling ") + CellLabel(activeCell);
        stuckRefDepth = CurrentShaftDepth(col, row);
        stuckTicks = 0;
        lastOreSample = ShaftOreSoFar();
    }

    shaftDepth = CurrentShaftDepth(col, row);
    if (shaftDepth > shaftMaxDepth) shaftMaxDepth = shaftDepth;

    // We enter this state from the traffic-separation altitude, which can be
    // 25 m or more above the surface. Descending that gap at cutting speed with
    // the drills spinning wastes twenty seconds and a chunk of power per shaft.
    // Drop fast through the air, then slow down and switch on at the rock.
    bool inRock = shaftDepth > -2.0;
    SetDrills(inRock);
    double descentSpeed = inRock ? drillSpeed : Math.Min(12.0, Math.Max(retreatSpeed, 6.0));

    // ---- Stop conditions, most urgent first --------------------------------
    if (!HasReservesForWork()) { AbandonShaft(ShaftResult.Aborted); return; }
    if (CargoFull || OverLiftLimit()) { AbandonShaft(ShaftResult.CargoFull); return; }
    if (shaftDepth >= shaftDepthLimit) { AbandonShaft(ShaftResult.Completed); return; }
    if (DepthExhausted()) { AbandonShaft(ShaftResult.OreExhausted); return; }

    // ---- Stuck handling ----------------------------------------------------
    // Only meaningful once we are actually cutting. In open air above the hole
    // there is nothing to be stuck on, and the check would misfire while the
    // ship is still accelerating downward.
    if (inRock && IsStuck())
    {
        stuckRetries++;
        if (stuckRetries > 3) { AbandonShaft(ShaftResult.Stuck); return; }

        Log("Stuck at " + Fmt(shaftDepth, 1) + "m, backing off (" + stuckRetries + "/3)");
        // Back up two metres and come at it again. Usually enough to clear a
        // boulder edge the drills were grinding against without progress.
        Vector3D relief = job.CellDepth(col, row, Math.Max(0, shaftDepth - 2.0));
        FlyTo(ControllerTargetFor(relief), retreatSpeed);
        Orient(job.Down, job.Forward);
        stuckTicks = 0;
        stuckRefDepth = shaftDepth - 2.0;
        return;
    }

    // ---- Normal cutting ----------------------------------------------------
    // Aim a little past where we are rather than at the bottom of the shaft, so
    // the velocity controller holds a steady cutting speed instead of easing off
    // as it approaches a distant target. Clamped at zero so that while we are
    // still above the surface the aim point is inside the rock, not behind us.
    double aimDepth = Math.Min(shaftDepthLimit, Math.Max(shaftDepth, 0.0) + 5.0);
    Vector3D bite = job.CellDepth(col, row, aimDepth);
    FlyTo(ControllerTargetFor(bite), descentSpeed);
    Orient(job.Down, job.Forward);
}

// ---------------------------------------------------------------------------

void StAscending(bool entry)
{
    if (entry)
    {
        statusLine = "Withdrawing";
        SetDrills(drillOnRetreat);
    }

    if (activeCell < 0) { SetState(MinerState.Selecting); return; }

    int col = CellCol(activeCell), row = CellRow(activeCell);
    double depth = CurrentShaftDepth(col, row);

    double standoff = transitAltitude + myLane;
    Vector3D clearOfHole = job.CellMouth(col, row, standoff);

    // Climb the shaft axis exactly. Any lateral drift on the way up and the ship
    // wedges itself against the wall it just cut.
    Vector3D exitPoint = depth > 1.0
        ? job.CellDepth(col, row, Math.Max(0, depth - 6.0))
        : clearOfHole;

    FlyTo(ControllerTargetFor(exitPoint), retreatSpeed);
    Orient(job.Down, job.Forward);

    if (depth > 1.0) return;

    // Clear of the hole. Decide where to go next.
    SetDrills(false);
    FinishShaft();
}

/// <summary>Commit the shaft result and pick the next activity.</summary>
void FinishShaft()
{
    ShaftResult result = pendingResult;
    double ore = ShaftOreSoFar();

    RecordShaftResult(activeCell, result, ore, shaftMaxDepth, shaftMaxDepth, shaftIsProbe);
    ReleaseLeaseLocal();

    Log(CellLabel(activeCell) + " " + result + ": " + Fmt(ore, 0) + "kg / "
        + Fmt(shaftMaxDepth, 1) + "m");

    activeCell = -1;

    if (!jobRunning) { SetState(MinerState.Inbound); return; }

    if (result == ShaftResult.Aborted || result == ShaftResult.CargoFull)
    {
        SetState(MinerState.Inbound);
        return;
    }
    if (CargoFull || OverLiftLimit() || !HasReservesForWork())
    {
        SetState(MinerState.Inbound);
        return;
    }
    SetState(MinerState.Selecting);
}

/// <summary>Break off the current shaft and start climbing out.</summary>
void AbandonShaft(ShaftResult why)
{
    pendingResult = why;
    SetState(MinerState.Ascending);
}

// ---------------------------------------------------------------------------

/// <summary>
/// Push waste into the ejectors continuously, while flying, whenever we are not
/// docked.
///
/// This replaces a dedicated "stop and dump" state. Stopping to throw stone
/// overboard costs time for something that happens perfectly well in transit —
/// the ejectors drain on their own clock either way. Credit where due: this is
/// the pattern experienced players already build by hand with an Event
/// Controller wired to "not docked", and it is plainly better than what the
/// script was doing.
/// </summary>
void EjectWhileFlying()
{
    if (ejectMode == EjectMode.Off || ejectors.Count == 0) return;
    if (Docked) return;

    // Not while cutting. Ejected stone becomes floating objects, and spraying
    // them into a shaft you are currently inside is asking for a collision.
    if (state == MinerState.Descending || state == MinerState.Ascending) return;

    // Cheap most ticks: only actually moves items every so often.
    if (tick % 20 != 0) return;
    if (BudgetTight(0.6)) return;

    EjectWaste();
}

// ---------------------------------------------------------------------------

void StInbound(bool entry)
{
    if (entry)
    {
        statusLine = "Returning";
        SetDrills(false);
        BeginPath(false);
        if (HasDispatcher) RequestDock();
    }

    if (FollowPath(false)) SetState(MinerState.Docking);
}

// ---------------------------------------------------------------------------

void StDocking(bool entry)
{
    if (entry) { statusLine = "Docking"; SetDrills(false); }

    if (dockConnector == null) { EnterFault("No connector to dock with"); return; }
    if (!homeDockSet) { EnterFault("No dock recorded"); return; }

    if (Docked)
    {
        SafeStop();
        stuckRetries = 0;
        SetState(MinerState.Unloading);
        return;
    }

    // Two-stage approach: first to a point squarely off the connector face, then
    // straight down the mating axis. Coming in on a diagonal fails far more often
    // than it works.
    Vector3D mate = homeDock.Position;
    Vector3D axis = homeDockForward;
    double standoff = Math.Max(6.0, shipRadius * 2.0);

    Vector3D hold = mate + axis * standoff;
    Vector3D offAxis = shipPos - mate;
    double along = Vector3D.Dot(offAxis, axis);
    double lateral = (offAxis - axis * along).Length();

    // Our own connector has to end up on the pad, not the controller.
    Vector3D connectorOffset = dockConnector.GetPosition() - shipPos;

    if (lateral > 1.0 || along > standoff * 1.4)
    {
        FlyTo(hold - connectorOffset, dockSpeed * 3.0);
    }
    else
    {
        FlyTo(mate - connectorOffset, dockSpeed);
        if (dockConnector.Status == MyShipConnectorStatus.Connectable)
            dockConnector.Connect();
    }

    // Face the connector the way it was facing when recorded.
    Orient(-axis, homeDockUp);
}

// ---------------------------------------------------------------------------

void StUnloading(bool entry)
{
    if (entry)
    {
        statusLine = "Unloading";
        SafeStop();
        SetBatteryCharging(true);
        SetTanksFilling(true);
    }

    if (!Docked) { SetState(MinerState.Docking); return; }

    if (UnloadToBase()) SetState(MinerState.Servicing);
}

// ---------------------------------------------------------------------------

void StServicing(bool entry)
{
    if (entry)
    {
        statusLine = "Charging";
        SetBatteryCharging(true);
        SetTanksFilling(true);
        // Parked and connected — the thrusters are dead weight drawing power
        // that we are trying to put back into the batteries. Undocking turns
        // them on again, and the guard in TickMiner catches every other route.
        SetThrusters(false);
    }

    if (!Docked) { SetState(MinerState.Docking); return; }

    // Keep draining into base storage in case a sorter is slowly feeding us.
    if (tick % 30 == 0) UnloadToBase();

    if (!jobRunning || jobComplete)
    {
        statusLine = jobComplete ? "Job complete — docked" : "Stopped — docked";
        ReleaseDock();
        return;
    }

    if (!ServiceComplete())
    {
        statusLine = "Charging " + Fmt(batteryFill * 100, 0) + "% / H2 " + Fmt(hydrogenFill * 100, 0) + "%";
        return;
    }

    ReleaseDock();
    SetState(MinerState.Undocking);
}

// ---------------------------------------------------------------------------

void StFault(bool entry)
{
    if (entry) SafeStop();
    statusLine = "FAULT: " + faultReason;
    // Deliberately does nothing else. Recovery is a human decision.
}

// ---------------------------------------------------------------------------
//  SHARED PREDICATES
// ---------------------------------------------------------------------------

/// <summary>Enough power and gas to keep working and still get home.</summary>
bool HasReservesForWork()
{
    if (batteryFill < minBattery) return false;
    if (hydrogenTanks.Count > 0 && hydrogenFill < minHydrogen) return false;
    return true;
}

/// <summary>Depth in metres of the drill face below the mouth of shaft (col,row).</summary>
double CurrentShaftDepth(int col, int row)
{
    Vector3D mouth = job.CellMouth(col, row, 0);
    return Vector3D.Dot(DrillFace() - mouth, job.Down);
}

/// <summary>
/// Convert a desired drill-face position into a controller position, since the
/// flight controller flies the controller and the hole is cut by the drills.
/// </summary>
Vector3D ControllerTargetFor(Vector3D desiredFacePos)
{
    return desiredFacePos - (DrillFace() - shipPos);
}

/// <summary>
/// Not making progress despite being told to descend. Measured against depth
/// rather than velocity, because a ship grinding against rock can be moving
/// plenty while going nowhere.
/// </summary>
bool IsStuck()
{
    if (shaftDepth > stuckRefDepth + 0.15)
    {
        stuckRefDepth = shaftDepth;
        stuckTicks = 0;
        return false;
    }

    stuckTicks++;
    // Roughly four seconds of no downward progress at Update10.
    return stuckTicks > 40;
}

/// <summary>
/// Adaptive depth. The core trick, inherited from SCAM: the drills themselves
/// are the ore sensor. If a shaft has stopped producing, there is nothing below
/// worth the fuel, so stop and go somewhere else.
/// </summary>
bool DepthExhausted()
{
    if (depthMode == DepthMode.Fixed) return false;

    // The first few metres are surface material and tell us nothing.
    if (shaftDepth < 6.0) { lastOreGainDepth = shaftDepth; return false; }

    double now = depthMode == DepthMode.AutoOre ? ShaftOreSoFar() : cargoFill * 1000.0;

    if (now > lastOreSample + 0.5)
    {
        lastOreSample = now;
        lastOreGainDepth = shaftDepth;
        noOreTicks = 0;
        return false;
    }

    noOreTicks++;

    // Require both a dry stretch of shaft and a dry stretch of time. Depth alone
    // trips on a fast ship in soft rock; time alone trips whenever the drills
    // are momentarily jammed.
    bool dryDistance = shaftDepth - lastOreGainDepth > 5.0;
    bool dryTime = noOreTicks > 60;
    return dryDistance && dryTime;
}

/// <summary>React to battle damage or attrition according to policy.</summary>
void CheckDamage()
{
    if (!stopOnDamage) return;
    if (state == MinerState.Fault || state == MinerState.Idle) return;
    if (DamagedBlockCount() == 0) return;

    Log("Damage detected — returning");
    if (state != MinerState.Inbound && state != MinerState.Docking
        && state != MinerState.Unloading && state != MinerState.Servicing)
    {
        if (activeCell >= 0) AbandonShaft(ShaftResult.Aborted);
        else SetState(MinerState.Inbound);
    }
}

string CellLabel(int idx)
{
    if (idx < 0) return "-";
    return "[" + CellCol(idx) + "," + CellRow(idx) + "]";
}
