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
    if (recording) RecordTick();

    CheckDamage();
    Watchdog();
    UpdateOreScan();
    EjectWhileFlying();
    if (!Docked) UpdateFuelModel();

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
    if (entry) { SafeStop(); ReleaseAirspace(); statusLine = "Idle"; }
    if (!jobRunning || jobComplete) return;

    // A fleet drone launches on the dispatcher's word, not its own. Without
    // this, a drone that had just come home *because* the dispatcher went quiet
    // would take off again immediately, fly to the site, find nobody to lease
    // from, and come back — a round trip's worth of hydrogen per lap, for as
    // long as the outage lasts. Waiting on the pad costs nothing and resumes by
    // itself the moment a beacon arrives.
    if (DispatcherSilent) { statusLine = "Waiting for dispatcher"; return; }

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

        // Retry, then go home. A dispatcher that has stopped answering must not
        // be able to park the whole fleet in mid-air — but the fix for that is
        // not to promote every survivor to solo mining, which is what this used
        // to do. See DispatcherSilent: solo ships skip the airspace mutex, so
        // that turned one dead dispatcher into several drones digging the same
        // deposit with no exclusion between them. The shaft already in hand is
        // always finished first; this branch only ever runs between shafts.
        if (tick - lastRequestTick > 60)
        {
            if (DispatcherSilent)
            {
                Log("Dispatcher silent — returning to base");
                awaitingLease = false;
                SetState(MinerState.Inbound);
                return;
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
    SampleOre();          // exact figure to measure this shaft's yield against
    shaftDepth = 0;
    shaftMaxDepth = 0;
    shaftStartOre = oreAboard;
    lastOreSample = 0;
    lastOreGainDepth = 0;
    noOreTicks = 0;
    stuckRetries = 0;
    ascendRetries = 0;

    shaftContactDepth = -1;

    // Metres of rock to cut, not depth from the job plane. A resumed shaft needs
    // no special handling: the already-cut section returns no material, so
    // contact is simply detected again at the old bottom.
    shaftDepthLimit = shaftIsProbe ? Math.Min(probeDepth, job.Depth) : job.Depth;

    SetState(MinerState.Approaching);
}

// ---------------------------------------------------------------------------

void StApproaching(bool entry)
{
    if (activeCell < 0) { SetState(MinerState.Selecting); return; }
    if (entry) { statusLine = "To shaft " + CellLabel(activeCell); SetDrills(false); }

    if (!HasReservesForWork()) { AbandonShaft(ShaftResult.Aborted); return; }

    int col = CellCol(activeCell), row = CellRow(activeCell);

    // Cruise in our own altitude lane, so drones crossing the site are stacked
    // rather than nose to nose. Lanes are formation, though, not exclusion —
    // that is the lock's job, below.
    double standoff = transitAltitude + myLane;
    Vector3D above = job.CellMouth(col, row, standoff);

    // One drone in the shared airspace at a time. Wait where we are, squared up
    // and at our lane height, rather than improvising a hold pattern: when the
    // lock arrives we want to already be pointing the right way.
    if (!AcquireAirspace(LOCK_SITE))
    {
        statusLine = "Waiting for airspace";
        if (lockHoldPoint == Vector3D.Zero) lockHoldPoint = shipPos;
        FlyTo(lockHoldPoint, cruiseSpeed * 0.25);
        Orient(job.Down, job.Forward);
        return;
    }
    lockHoldPoint = Vector3D.Zero;

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
///
/// "Within reach" has to mean exactly what the descent means by it, which is
/// <see cref="EffectiveDepthLimit"/>'s pre-contact bound: the job's own depth,
/// measured from the plane. An earlier version also required the rock to start
/// within 8 m of the plane, which contradicted the descent logic outright — that
/// tolerates a surface tens of metres down and is written to do so — and the
/// disagreement was expensive rather than merely untidy, because a cell rejected
/// here is written off as Barren and never revisited. On an asteroid, where the
/// surface wanders either side of any plane you pick, that discards good rock
/// permanently on the strength of one raycast.
/// </summary>
bool ShaftHasRock(double standoff)
{
    if (cameras.Count == 0) return true;

    // Same span the ship would descend before giving up. Longer than the old
    // reach, so a camera is more often short of charge for it — which lands on
    // the safe answer below, not a wrong one.
    double reach = standoff + job.Depth;

    double hit;
    if (!TryScanAhead(reach, out hit)) return true;   // no camera had charge

    if (hit < 0) return false;                        // scanned, genuinely empty
    return hit <= reach;                              // rock anywhere we would dig
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
        shaftContactDepth = -1;
        shaftStartVolume = cargoVolume;
    }

    shaftDepth = CurrentShaftDepth(col, row);
    if (shaftDepth > shaftMaxDepth) shaftMaxDepth = shaftDepth;

    // Committed to the hole. Nobody else can want this volume now, so hand the
    // shared airspace on rather than sit on it for the length of a deep shaft.
    // SCAM does the same thing at the same moment and for the same reason.
    if (shaftDepth > 1.0 && heldLock.Length > 0) ReleaseAirspace();

    // First material back means we have reached the real surface. On a planet
    // that is within a metre of the job plane and this barely matters; on an
    // asteroid the surface wanders tens of metres either side of it, and
    // without this every measurement below is taken from the wrong datum.
    if (shaftContactDepth < 0 && cargoVolume > shaftStartVolume + 0.001)
        shaftContactDepth = shaftDepth;

    // We enter this state from the traffic-separation altitude, which can be
    // 25 m or more above the surface. Descending that gap at cutting speed with
    // the drills spinning wastes twenty seconds and a chunk of power per shaft.
    // Drop fast through the air, then slow down and switch on at the rock.
    bool inRock = shaftDepth > -2.0;
    SetDrills(inRock);
    double cutSpeed = DrillSpeedNow();
    double descentSpeed = inRock ? cutSpeed : Math.Min(12.0, Math.Max(retreatSpeed, 6.0));

    // Only learn from ground we are genuinely cutting. Above the surface the
    // ship is in free air and would teach the model that it can cut at 6 m/s.
    UpdateDrillLearning(inRock && shaftContactDepth >= 0, shaftDepth, cutSpeed);

    // ---- Stop conditions, most urgent first --------------------------------
    if (!HasReservesForWork()) { AbandonShaft(ShaftResult.Aborted); return; }
    if (CargoFull || OverLiftLimit()) { AbandonShaft(ShaftResult.CargoFull); return; }
    if (shaftDepth >= EffectiveDepthLimit()) { AbandonShaft(ShaftResult.Completed); return; }
    if (DepthExhausted()) { AbandonShaft(ShaftResult.OreExhausted); return; }

    // ---- Stuck handling ----------------------------------------------------
    // Only meaningful once we are actually cutting. In open air above the hole
    // there is nothing to be stuck on, and the check would misfire while the
    // ship is still accelerating downward.
    if (inRock && IsStuck())
    {
        stuckRetries++;
        // A jam is the strongest evidence there is that the commanded cutting
        // speed is wrong for this hull. Much stronger than a slow tick.
        PenaliseDrillSpeed();
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
    double aimDepth = Math.Min(EffectiveDepthLimit(), Math.Max(shaftDepth, 0.0) + 5.0);
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

    // Ask for the airspace on the way up rather than on arrival at the top, so
    // the queue is working while we climb and the common case costs nothing.
    bool clear = AcquireAirspace(LOCK_SITE);

    // Climb the shaft axis exactly. Any lateral drift on the way up and the ship
    // wedges itself against the wall it just cut.
    double exitDepth = Math.Max(0, depth - 6.0);

    // Without the lock, stop just short of the mouth. This is SCAM's
    // WaitingForLockInShaft, and the choice of place is the whole point: our own
    // shaft is the one volume nobody else can be granted.
    if (!clear)
    {
        exitDepth = Math.Max(exitDepth, 2.0);
        statusLine = "Holding in shaft — airspace busy";
    }

    Vector3D exitPoint = depth > 1.0
        ? job.CellDepth(col, row, exitDepth)
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
    SampleOre();          // exact figure now the shaft is finished
    double ore = ShaftOreSoFar();

    // Yield is kilograms per metre *drilled*, so the vacuum we fell through to
    // reach the surface must not count. Including it would dilute the yield of
    // every cell whose surface sits below the job plane, and the prospector
    // would then steer away from exactly the ground it should be working.
    double cut = shaftContactDepth >= 0
        ? Math.Max(0.0, shaftMaxDepth - shaftContactDepth)
        : 0.0;

    // Never made contact: the shaft was empty space all the way down. Report it
    // as a completed, barren cell rather than a stuck or failed one.
    if (shaftContactDepth < 0 && result == ShaftResult.Completed)
        Log(CellLabel(activeCell) + " never reached rock");

    RecordShaftResult(activeCell, result, ore, cut, cut, shaftIsProbe);
    ReleaseLeaseLocal();

    Log(CellLabel(activeCell) + " " + result + ": " + Fmt(ore, 0) + "kg / "
        + Fmt(cut, 1) + "m cut");

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
        // Off the site and onto the recorded route, which everyone shares and
        // which the lanes exist to separate. Holding the site lock all the way
        // home would serialise the whole fleet for no benefit.
        ReleaseAirspace();
        ResetDockWait();
        if (HasDispatcher) RequestDock();
    }

    if (!FollowPath(false)) return;

    // End of the route, but the connector may still be somebody else's. Hold
    // off rather than crowding the pad — squared up on the mating axis, so the
    // approach starts from the right attitude the moment the slot arrives.
    if (!AcquireDockSlot())
    {
        statusLine = "Waiting for a dock slot";
        if (dockHoldPoint == Vector3D.Zero) dockHoldPoint = shipPos;
        FlyTo(dockHoldPoint, dockSpeed);
        if (homeDockSet) Orient(-homeDockForward, homeDockUp);
        return;
    }

    SetState(MinerState.Docking);
}

// ---------------------------------------------------------------------------

void StDocking(bool entry)
{
    if (entry)
    {
        statusLine = "Docking";
        SetDrills(false);
        dockNearZone = false;
        connectDebounce = 0;
        dockStallTicks = 0;
        lastDockDist = double.MaxValue;
    }

    if (dockConnector == null) { EnterFault("No connector to dock with"); return; }
    if (!homeDockSet) { EnterFault("No dock recorded"); return; }

    if (Docked)
    {
        SafeStop();
        dockRetries = 0;
        dockNearZone = false;
        SetState(MinerState.Unloading);
        return;
    }

    Vector3D mate = homeDock.Position;
    Vector3D axis = homeDockForward;
    double standoff = Math.Max(6.0, shipRadius * 2.0);

    Vector3D hold = mate + axis * standoff;
    Vector3D offAxis = shipPos - mate;
    double along = Vector3D.Dot(offAxis, axis);
    double lateral = (offAxis - axis * along).Length();

    // Our own connector has to end up on the pad, not the controller.
    Vector3D connectorOffset = dockConnector.GetPosition() - shipPos;

    // Face the connector the way it was facing when recorded. Done first so the
    // alignment gate below reads this tick's error, not last tick's.
    Orient(-axis, homeDockUp);

    // ---- Stage 1: get squarely off the connector face ----------------------
    if (lateral > 1.0 || along > standoff * 1.4)
    {
        dockNearZone = false;
        FlyTo(hold - connectorOffset, dockSpeed * 3.0);
        return;
    }

    // ---- Stage 2: stop and square up before committing ---------------------
    // PAM holds station at the approach point and aligns to within ten degrees
    // before it starts the mating run at all. Translating and rotating at the
    // same time is what produces the diagonal arrivals that bounce off the
    // collar — which the old code warned about in a comment and then did anyway.
    if (alignError > 10.0 && !dockNearZone)
    {
        FlyTo(hold - connectorOffset, dockSpeed);
        statusLine = "Docking — squaring up";
        return;
    }

    // ---- Stage 3: mating run -----------------------------------------------
    // The slow zone latches. Without it the ship oscillates between approach
    // speed and mating speed at the boundary, which reads as juddering and
    // makes the final centimetres take far longer than they should.
    double mateDist = Vector3D.Distance(shipPos + connectorOffset, mate);
    double nearDist = Math.Max(1.5, Math.Min(5.0, shipRadius * 0.3));
    if (mateDist <= nearDist) dockNearZone = true;

    FlyTo(mate - connectorOffset, dockNearZone ? dockSpeed : dockSpeed * 2.5);

    if (dockConnector.Status == MyShipConnectorStatus.Connectable)
    {
        // Do not grab the first frame it is possible. PAM waits several ticks of
        // continuous connectable status first, because latching while still
        // drifting sideways either fails outright or yanks the ship straight.
        connectDebounce++;
        if (connectDebounce > 5) dockConnector.Connect();
        dockStallTicks = 0;
        return;
    }

    connectDebounce = 0;

    // ---- Stalled approach detection ----------------------------------------
    // The watchdog would eventually catch this, but four minutes of a ship
    // grinding against a collar is not a useful failure. PAM notices in about
    // two seconds by watching whether the distance is still falling.
    double rounded = Math.Round(mateDist, 1);
    if (rounded < lastDockDist) { lastDockDist = rounded; dockStallTicks = 0; }
    else dockStallTicks++;

    if (dockStallTicks > 20)
    {
        Log("Dock approach stalled at " + Fmt(mateDist, 1) + "m — backing off");
        dockRetries++;
        if (dockRetries >= 3) { EnterFault("Could not dock after 3 attempts"); return; }
        SetState(MinerState.Inbound);
    }
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
        // Name uranium when it is the one holding us. A base with none to give
        // holds the ship here indefinitely, and Servicing is exempt from the
        // watchdog, so a wait that does not say why is indistinguishable from a
        // hang. The other two always finish on their own.
        statusLine = reactors.Count > 0 && minUranium > 0 && uraniumKg < minUranium
            ? "Waiting for uranium " + Fmt(uraniumKg, 1) + "/" + Fmt(minUranium, 1) + "kg"
            : "Charging " + Fmt(batteryFill * 100, 0) + "% / H2 " + Fmt(hydrogenFill * 100, 0) + "%";
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
    // A reactor ship with no batteries reports full power indefinitely, so
    // without this it would run its reactors dry in flight and never come home.
    if (reactors.Count > 0 && minUranium > 0 && uraniumKg < minUranium) return false;

    // The measured check, once the ship has told us how much it drinks. A fixed
    // percentage is wasteful on a short hop and fatal on a long one; this asks
    // the only question that matters — is there still enough to get home.
    if (FuelCriticalForReturn()) return false;

    return true;
}

/// <summary>
/// Where this shaft stops, as a depth below the job plane.
///
/// Two regimes. Before the drills touch anything we are descending through
/// vacuum toward a surface that may sit well below the plane, and the only
/// sensible bound is the job's own depth — no rock by then means an empty cell.
/// Once contact is made the limit becomes metres of actual cut, so a 12 m probe
/// really does cut 12 m of rock rather than stopping 12 m below a plane it has
/// not reached yet.
/// </summary>
double EffectiveDepthLimit()
{
    if (shaftContactDepth < 0) return job.Depth;
    return shaftContactDepth + shaftDepthLimit;
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

    // Still falling through vacuum toward an asteroid whose surface sits below
    // the job plane. There is nothing to be exhausted yet — judging the cell
    // here would write off good rock the drills have not even touched, which is
    // exactly how an irregular asteroid poisons the whole yield map.
    if (shaftContactDepth < 0) { lastOreGainDepth = shaftDepth; return false; }

    // The first few metres of actual rock are surface material and tell us
    // nothing. Measured from contact, not from the plane.
    if (shaftDepth < shaftContactDepth + 6.0) { lastOreGainDepth = shaftDepth; return false; }

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
