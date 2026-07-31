// ============================================================================
//  INTER-GRID COMMUNICATION
//
//  Wire format is plain text, pipe delimited: TYPE|field|field|...
//
//  Numbers travel as integers scaled by 1000, never as decimal strings. A
//  script that writes "12.5" and is read by a client whose locale uses a comma
//  decimal separator silently parses it as 125 — and a drone that thinks the
//  shaft is ten times deeper than it is will drill until something breaks.
//  Integers have no separator and cannot go wrong.
// ============================================================================

const int IGC_MAX_PER_TICK = 12;

void SetupIgc()
{
    listener = IGC.RegisterBroadcastListener(igcChannel);
    listener.SetMessageCallback(igcChannel);
    unicast = IGC.UnicastListener;
    unicast.SetMessageCallback(igcChannel);
}

/// <summary>Drain both inboxes. Bounded per tick so a message storm cannot
/// starve the control loop.</summary>
void PumpIgc()
{
    if (listener == null) return;

    int handled = 0;
    while (listener.HasPendingMessage && handled < IGC_MAX_PER_TICK)
    {
        MyIGCMessage m = listener.AcceptMessage();
        HandleMessage(m.Source, m.Data as string);
        handled++;
    }
    while (unicast != null && unicast.HasPendingMessage && handled < IGC_MAX_PER_TICK * 2)
    {
        MyIGCMessage m = unicast.AcceptMessage();
        HandleMessage(m.Source, m.Data as string);
        handled++;
    }
}

void HandleMessage(long src, string body)
{
    if (string.IsNullOrEmpty(body)) return;
    if (src == IGC.Me) return;                 // our own broadcast coming back

    string[] f = body.Split('|');
    if (f.Length == 0) return;

    switch (f[0])
    {
        case "B":  OnBeacon(src, f); break;
        case "H":  OnHeartbeat(src, f); break;
        case "LR": OnLeaseRequest(src, f); break;
        case "LG": OnLeaseGrant(src, f); break;
        case "LD": OnLeaseDenied(src, f); break;
        case "SR": OnShaftReport(src, f); break;
        case "DR": OnDockRequest(src, f); break;
        case "DG": OnDockGrant(src, f); break;
        case "DX": OnDockRelease(src, f); break;
        case "OS": OnOreSighting(src, f); break;
        case "KA": OnLockAsk(src, f); break;
        case "KG": OnLockGrant(src, f); break;
        case "KR": OnLockRelease(src, f); break;
        case "JP": OnJobPush(src, f); break;
        case "JA": OnJobAck(src, f); break;
        case "C":  if (f.Length > 1) HandleCommand(f[1], false); break;
    }
}

bool HasDispatcher { get { return role == Role.Miner && dispatcherAddr != 0; } }

// ---------------------------------------------------------------------------
//  OUTBOUND — miner side
// ---------------------------------------------------------------------------

void SendHeartbeat()
{
    if (role != Role.Miner || dispatcherAddr == 0) return;
    if (tick % 12 != 0) return;                // ~2 Hz at Update10

    string body = "H|" + ShipLabel()
        + "|" + (int)state
        + "|" + EncD(cargoFill)
        + "|" + EncD(batteryFill)
        + "|" + EncV(shipPos)
        + "|" + activeCell;

    IGC.SendUnicastMessage(dispatcherAddr, igcChannel, body);
}

void RequestLease()
{
    if (dispatcherAddr == 0) return;
    IGC.SendUnicastMessage(dispatcherAddr, igcChannel, "LR|" + ShipLabel());
}

void SendShaftReport(int cellIdx, ShaftResult result, double oreKg, double metres,
                     double depthReached, bool wasProbe)
{
    if (dispatcherAddr == 0) return;
    string body = "SR|" + cellIdx + "|" + (int)result
                + "|" + EncD(oreKg) + "|" + EncD(metres)
                + "|" + EncD(depthReached) + "|" + (wasProbe ? 1 : 0);
    IGC.SendUnicastMessage(dispatcherAddr, igcChannel, body);
}

void RequestDock()
{
    if (dispatcherAddr == 0) return;
    IGC.SendUnicastMessage(dispatcherAddr, igcChannel, "DR|" + ShipLabel());
}

void ReleaseDock()
{
    if (dispatcherAddr == 0 || myDockSlot < 0) return;
    IGC.SendUnicastMessage(dispatcherAddr, igcChannel, "DX|" + myDockSlot);
    myDockSlot = -1;
}

void SendOreSighting(OreSighting s)
{
    if (dispatcherAddr == 0) return;
    IGC.SendUnicastMessage(dispatcherAddr, igcChannel, "OS|" + s.OreType + "|" + EncV(s.Position));
}

/// <summary>Release whatever we hold, locally and at the dispatcher.</summary>
void ReleaseLease(ShaftResult why)
{
    if (activeCell < 0) return;
    if (dispatcherAddr != 0)
        SendShaftReport(activeCell, why, ShaftOreSoFar(), shaftMaxDepth, shaftMaxDepth, shaftIsProbe);
    ReleaseLeaseLocal();
    activeCell = -1;
}

/// <summary>Clear the local lease flag without telling anyone.</summary>
void ReleaseLeaseLocal()
{
    if (activeCell < 0 || activeCell >= cells.Length) return;
    if (cells[activeCell].State == CellState.Leased) cells[activeCell].State = CellState.Unknown;
    cells[activeCell].LeasedBy = 0;
}

// ---------------------------------------------------------------------------
//  OUTBOUND — dispatcher side
// ---------------------------------------------------------------------------

/// <summary>
/// Announce the job. Broadcast rather than unicast so a freshly built drone
/// joins the fleet the moment it powers up, with no pairing step.
/// </summary>
void SendBeacon()
{
    if (!job.IsSet) { IGC.SendBroadcastMessage(igcChannel, "B|0"); return; }
    IGC.SendBroadcastMessage(igcChannel, "B|1" + JobFrameFields());
}

/// <summary>
/// The job frame on the wire: eight fields, always in this order. Shared by the
/// beacon and by a miner pushing a frame up, so the two directions cannot drift
/// apart — a frame that encodes one way and decodes the other is a fleet digging
/// in two different places.
/// </summary>
string JobFrameFields()
{
    return "|" + job.Width + "|" + job.Height + "|" + job.Depth
         + "|" + EncD(job.Spacing)
         + "|" + EncV(job.Origin)
         + "|" + EncV(job.Right)
         + "|" + EncV(job.Forward)
         + "|" + EncV(job.Down);
}

/// <summary>
/// Decode eight frame fields starting at <paramref name="at"/> and adopt them.
/// Cells are rebuilt only if the shape actually moved, because rebuilding throws
/// away everything already learned about the site.
/// </summary>
/// <returns>False if the frame was malformed, in which case the job is cleared.</returns>
bool AdoptJobFrame(string[] f, int at)
{
    int w = ParseInt(f[at], job.Width);
    int h = ParseInt(f[at + 1], job.Height);
    int d = ParseInt(f[at + 2], job.Depth);

    bool reshaped = !job.IsSet || w != job.Width || h != job.Height;

    job.IsSet = true;
    job.Width = w; job.Height = h; job.Depth = d;
    job.Spacing = DecD(f[at + 3]);
    job.Origin = DecV(f[at + 4]);
    job.Right = DecV(f[at + 5]);
    job.Forward = DecV(f[at + 6]);
    job.Down = DecV(f[at + 7]);

    if (!ValidateJobBasis()) return false;
    if (reshaped || cells.Length != job.CellCount) RebuildCells();
    return true;
}

void GrantLease(long to, int cellIdx, double depthLimit, bool isProbe, double lane)
{
    string body = "LG|" + cellIdx + "|" + EncD(depthLimit)
                + "|" + (isProbe ? 1 : 0) + "|" + EncD(lane);
    IGC.SendUnicastMessage(to, igcChannel, body);
}

void DenyLease(long to, string reason)
{
    IGC.SendUnicastMessage(to, igcChannel, "LD|" + reason);
}

void GrantDock(long to, int slot)
{
    IGC.SendUnicastMessage(to, igcChannel, "DG|" + slot);
}

// ---------------------------------------------------------------------------
//  INBOUND
// ---------------------------------------------------------------------------

void OnBeacon(long src, string[] f)
{
    if (role != Role.Miner) return;

    // A second dispatcher on the same channel would otherwise steal the miner
    // on every beacon, so leases come from one and reports go to whichever
    // spoke last. Stay with the first one heard and say so.
    if (dispatcherAddr != 0 && dispatcherAddr != src)
    {
        if (tick - lastDispatcherSeenTick < 600)
        {
            Log("Second dispatcher on channel '" + igcChannel + "' — ignoring it");
            return;
        }
        Log("Switching dispatcher — previous one went quiet");
    }

    dispatcherAddr = src;
    lastDispatcherSeenTick = tick;

    if (f.Length < 10 || f[1] != "1") return;

    // Adopt the dispatcher's job frame verbatim. Every drone working from the
    // same origin and axes is what makes cell indices mean the same thing to
    // everyone — without it, drone 2's cell [3,4] is somewhere else entirely.
    AdoptJobFrame(f, 2);
}

// ---------------------------------------------------------------------------
//  JOB PUSH — miner to dispatcher
//
//  A dispatcher bolted to the base cannot anchor a job: SetJob reads the local
//  controller's attitude and the local drill face, and a base has the wrong one
//  of the first and none of the second. So the frame travels the other way. Fly
//  a miner to the site, anchor it there exactly as a solo miner would, and push
//  it up. The dispatcher adopts the frame and immediately re-beacons, so the
//  rest of the fleet converges on it before anybody is granted a shaft.
// ---------------------------------------------------------------------------

void SendJobPush()
{
    IGC.SendUnicastMessage(dispatcherAddr, igcChannel, "JP" + JobFrameFields());
}

void AckJobPush(long to, bool ok, string reason)
{
    IGC.SendUnicastMessage(to, igcChannel, "JA|" + (ok ? "1" : "0") + "|" + reason);
}

void OnJobPush(long src, string[] f)
{
    if (role != Role.Dispatcher) return;
    if (f.Length < 9) { AckJobPush(src, false, "malformed"); return; }

    // Adopting a frame renumbers every cell, and cell indices are the fleet's
    // shared vocabulary. Same rule as growing the grid: never while somebody is
    // out there holding one.
    if (AnyCellLeased()) { AckJobPush(src, false, "leased"); return; }

    if (!AdoptJobFrame(f, 1)) { AckJobPush(src, false, "badframe"); return; }

    // The survey belonged to wherever the old grid was. Keeping it would report
    // ore in cells that are now somewhere else entirely.
    //
    // Unconditionally, unlike the beacon path: a push is a deliberate re-anchor
    // and the origin has almost certainly moved, but a 5x5 replacing a 5x5 is not
    // a change of *shape*, so AdoptJobFrame on its own would leave the old map in
    // place. This matches what SetJob does when a miner anchors locally.
    RebuildCells();
    activeCell = -1;
    jobComplete = false;
    probePassDone = false;
    sightings.Clear();

    Log("Adopted job " + job.Width + "x" + job.Height + " @" + job.Depth + "m from a miner");
    AckJobPush(src, true, "ok");
    SendBeacon();
}

void OnJobAck(long src, string[] f)
{
    if (role != Role.Miner || f.Length < 2) return;

    if (f[1] == "1") { Log("Dispatcher adopted the job"); return; }

    string why = f.Length > 2 ? f[2] : "refused";
    if (why == "leased")
        Log("Dispatcher refused the job: drones still hold shafts. Run 'stop' on it first.");
    else
        Log("Dispatcher refused the job: " + why);
}

void OnHeartbeat(long src, string[] f)
{
    if (role != Role.Dispatcher || f.Length < 7) return;

    bool known = fleet.ContainsKey(src);
    DroneRecord r = DroneFor(src);
    if (!known) Log("Drone joined: " + f[1]);

    r.Name = f[1];
    r.State = (MinerState)ParseInt(f[2], 0);
    r.CargoFill = (float)DecD(f[3]);
    r.Battery = (float)DecD(f[4]);
    r.Position = DecV(f[5]);
    r.LeasedCell = ParseInt(f[6], -1);
    r.LastSeenTick = tick;
}

void OnLeaseRequest(long src, string[] f)
{
    if (role != Role.Dispatcher) return;
    if (!job.IsSet) { DenyLease(src, "nojob"); return; }
    // 'stop' at the dispatcher must actually stop the fleet. Drones already in
    // the air finish what they hold and then find no more work waiting.
    if (!jobRunning || jobComplete) { DenyLease(src, "paused"); return; }

    DroneRecord r = DroneFor(src);
    if (f.Length > 1 && r.Name == "?") r.Name = f[1];

    // Score the site from where this drone actually is, so the nearest free
    // shaft goes to the nearest drone instead of to whoever spoke first.
    int cell = SelectNextCell(r.Position.LengthSquared() > 1 ? r.Position : shipPos);
    if (cell < 0) { DenyLease(src, "done"); return; }

    cells[cell].State = CellState.Leased;
    cells[cell].LeasedBy = src;
    cells[cell].LeaseExpiresTick = tick + (long)(droneTimeout * 2 / Math.Max(dt, 0.01));
    r.LeasedCell = cell;

    double limit = shaftIsProbe ? Math.Min(probeDepth, job.Depth) : job.Depth;
    GrantLease(src, cell, limit, shaftIsProbe, r.Lane);
}

void OnLeaseGrant(long src, string[] f)
{
    if (role != Role.Miner || f.Length < 5) return;

    activeCell = ParseInt(f[1], -1);
    shaftDepthLimit = DecD(f[2]);
    shaftIsProbe = f[3] == "1";
    myLane = DecD(f[4]);

    if (activeCell >= 0 && activeCell < cells.Length)
        cells[activeCell].State = CellState.Leased;
}

void OnLeaseDenied(long src, string[] f)
{
    if (role != Role.Miner) return;
    awaitingLease = false;

    if (f.Length < 2) return;

    if (f[1] == "done")
    {
        jobComplete = true;
        Log("Dispatcher reports job complete");
        SetState(MinerState.Inbound);
    }
    else if (f[1] == "paused")
    {
        // Not finished, just no work right now. Go home and wait rather than
        // loiter over the site burning hydrogen.
        Log("Dispatcher paused — returning");
        SetState(MinerState.Inbound);
    }
    // "nojob": leave awaitingLease clear and retry on the next pass.
}

void OnShaftReport(long src, string[] f)
{
    if (role != Role.Dispatcher || f.Length < 7) return;

    int idx = ParseInt(f[1], -1);
    if (idx < 0 || idx >= cells.Length) return;

    // Only the drone that holds the lease may report on it. Without this a
    // stale message from a drone whose lease already expired would overwrite
    // the result of whoever is digging that cell now.
    if (cells[idx].LeasedBy != 0 && cells[idx].LeasedBy != src) return;

    ShaftResult result = (ShaftResult)ParseInt(f[2], 0);
    RecordShaftResult(idx, result, DecD(f[3]), DecD(f[4]), DecD(f[5]), f[6] == "1");

    DroneRecord r;
    if (fleet.TryGetValue(src, out r)) r.LeasedCell = -1;
}

void OnDockRequest(long src, string[] f)
{
    if (role != Role.Dispatcher) return;

    // Already holding one? Re-grant it rather than allocate a second.
    foreach (var kv in dockSlotOwner)
        if (kv.Value == src) { GrantDock(src, kv.Key); return; }

    for (int slot = 0; slot < dockSlots; slot++)
    {
        if (dockSlotOwner.ContainsKey(slot)) continue;
        dockSlotOwner[slot] = src;
        DroneRecord r;
        if (fleet.TryGetValue(src, out r)) r.DockSlot = slot;
        GrantDock(src, slot);
        return;
    }

    GrantDock(src, -1);       // all full, hold off
}

void OnDockGrant(long src, string[] f)
{
    if (role != Role.Miner || f.Length < 2) return;
    myDockSlot = ParseInt(f[1], -1);
}

void OnDockRelease(long src, string[] f)
{
    if (role != Role.Dispatcher || f.Length < 2) return;
    int slot = ParseInt(f[1], -1);

    long owner;
    if (dockSlotOwner.TryGetValue(slot, out owner) && owner == src)
        dockSlotOwner.Remove(slot);

    DroneRecord r;
    if (fleet.TryGetValue(src, out r)) r.DockSlot = -1;
}

void OnOreSighting(long src, string[] f)
{
    if (f.Length < 3) return;
    // Dispatcher aggregates every drone's findings into one shared map, so a
    // lead found by one scout steers the whole fleet.
    AddSighting(f[1], DecV(f[2]));
}

string ShipLabel()
{
    if (shipName.Length > 0) return Sanitize(shipName);
    return Sanitize(Me.CubeGrid.CustomName);
}

/// <summary>Strip delimiters so a grid called "Miner|1" cannot corrupt the wire format.</summary>
static string Sanitize(string s)
{
    if (string.IsNullOrEmpty(s)) return "?";
    return s.Replace('|', '/').Replace(',', ' ');
}
