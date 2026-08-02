// ============================================================================
//  DISPATCHER
//
//  A stationary brain at the base. It owns the job and the yield map; drones own
//  nothing but their current shaft. That split is what makes the fleet robust:
//  a drone can explode mid-shaft and the only thing lost is one lease, which
//  expires on its own and gets handed to somebody else.
//
//  It is entirely optional. A single miner with no dispatcher on the channel
//  runs the identical job logic locally.
// ============================================================================

void TickDispatcher()
{
    statusLine = "Dispatching";

    if (recording) RecordTick();
    UpdateOreScan();

    // Announce ourselves steadily. New drones need to hear this to join, and
    // existing ones use it as the liveness signal.
    if (tick % 30 == 0) SendBeacon();

    ExpireLeases();
    ExpireDrones();
    ExpireAirspaceLocks();
}

/// <summary>
/// Reclaim shafts whose holder has gone quiet.
///
/// This is the failure mode that kills naive swarm scripts: a drone dies holding
/// a lease, nobody ever digs that cell, and the job never completes. A lease is
/// a timed loan, not a permanent grant.
/// </summary>
void ExpireLeases()
{
    if (cells.Length == 0) return;

    for (int i = 0; i < cells.Length; i++)
    {
        YieldCell c = cells[i];
        if (c.State != CellState.Leased) continue;
        if (c.LeaseExpiresAt == 0 || clock < c.LeaseExpiresAt) continue;

        Log("Lease on " + CellLabel(i) + " expired — reissuing");
        c.State = c.MetresDrilled > 0.5f ? CellState.Rich : CellState.Unknown;
        c.LeasedBy = 0;
        c.LeaseId = 0;
        c.LeaseExpiresAt = 0;
    }
}

/// <summary>Forget drones we have not heard from, and free what they held.</summary>
void ExpireDrones()
{
    if (fleet.Count == 0) return;

    // Collect first, mutate after — removing from a dictionary mid-enumeration
    // throws, and it will throw on the exact night you are not watching.
    var lost = new List<long>();

    foreach (var kv in fleet)
        if (clock - kv.Value.LastSeenAt > droneTimeout) lost.Add(kv.Key);

    for (int i = 0; i < lost.Count; i++)
    {
        long addr = lost[i];
        DroneRecord r = fleet[addr];
        Log("Drone lost: " + r.Name);

        if (r.LeasedCell >= 0 && r.LeasedCell < cells.Length)
        {
            YieldCell c = cells[r.LeasedCell];
            if (c.State == CellState.Leased && c.LeasedBy == addr)
            {
                c.State = c.MetresDrilled > 0.5f ? CellState.Rich : CellState.Unknown;
                c.LeasedBy = 0;
                c.LeaseId = 0;
                c.LeaseExpiresAt = 0;
            }
        }

        if (r.DockSlot >= 0)
        {
            long owner;
            if (dockSlotOwner.TryGetValue(r.DockSlot, out owner) && owner == addr)
                dockSlotOwner.Remove(r.DockSlot);
        }

        fleet.Remove(addr);

        // Airspace last, because promoting the next drone in the queue must skip
        // anyone already forgotten, and this one is now forgotten.
        ReleaseLocksOf(addr);
    }

    // Lanes are handed out by join order, so a departure leaves a gap. Repack so
    // three drones always use lanes 0/1/2 rather than 0/3/7.
    if (lost.Count > 0) RepackLanes();
}

void RepackLanes()
{
    int i = 0;
    foreach (var kv in fleet)
    {
        kv.Value.Lane = i * laneSpacing;
        i++;
    }
}

/// <summary>
/// The fleet record for an address, created if this is the first we have heard
/// of it.
///
/// Anything a drone sends counts as a sign of life, not just its heartbeat. A
/// drone that asks for airspace before its first heartbeat has landed would
/// otherwise be granted a section and have it reclaimed on the same tick for
/// being an unknown drone — a grant/revoke flap that is very hard to read from
/// the outside.
/// </summary>
DroneRecord DroneFor(long addr)
{
    DroneRecord r;
    if (!fleet.TryGetValue(addr, out r))
    {
        r = new DroneRecord();
        r.Address = addr;
        // Stack new arrivals into their own altitude band.
        r.Lane = fleet.Count * laneSpacing;
        fleet[addr] = r;
    }
    r.LastSeenAt = clock;
    return r;
}

// ---------------------------------------------------------------------------
//  FLEET STATISTICS
// ---------------------------------------------------------------------------

int ActiveDroneCount()
{
    int n = 0;
    foreach (var kv in fleet)
        if (kv.Value.State != MinerState.Idle && kv.Value.State != MinerState.Fault) n++;
    return n;
}

double FleetOreTotal()
{
    double t = 0;
    for (int i = 0; i < cells.Length; i++) t += cells[i].OreKg;
    return t;
}

double FleetMetresTotal()
{
    double t = 0;
    for (int i = 0; i < cells.Length; i++) t += cells[i].MetresDrilled;
    return t;
}
