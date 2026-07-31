// ============================================================================
//  AIRSPACE LOCKS
//
//  Taken from SCAM, which asks a dispatcher for a named section, waits in a FIFO
//  queue if somebody holds it, and releases when done. Leases and locks are
//  orthogonal and were conflated here for a long time:
//
//      a LEASE answers  "who owns this work"
//      a LOCK  answers  "who may occupy this space"
//
//  VEIN already had expiring leases and no spatial exclusion at all. SCAM has
//  locks that never expire — which is exactly why it ships a manual purge
//  command for the deadlocks that produces. Doing both, with expiry on the lock
//  as well, is strictly better than either.
//
//  Sections are blocks of cells rather than one lock over the whole site. One
//  global lock would serialise the fleet down to a single working drone; per
//  cell would be pointless, because the lease already guarantees no two drones
//  are assigned the same shaft. What actually collides is the shared airspace
//  drones descend and climb through, so that is what gets carved up.
// ============================================================================

/// <summary>Cells per side of a lock section.</summary>
const int SECTION_CELLS = 4;

/// <summary>Name of the section containing a cell. Kept short — it goes on the wire.</summary>
string SectionFor(int cellIdx)
{
    if (cellIdx < 0 || job.Width <= 0) return "g";
    return "s" + (CellCol(cellIdx) / SECTION_CELLS) + "_" + (CellRow(cellIdx) / SECTION_CELLS);
}

// ---------------------------------------------------------------------------
//  MINER SIDE
// ---------------------------------------------------------------------------

/// <summary>
/// Do we hold the airspace for the cell we are about to work?
///
/// Solo miners hold everything by definition. Fleet miners ask and wait; the
/// caller holds station meanwhile rather than committing to a descent.
/// </summary>
bool HoldsAirspaceFor(int cellIdx)
{
    if (!HasDispatcher) return true;

    string want = SectionFor(cellIdx);
    if (heldLock == want) return true;

    // Holding the wrong one — release before asking for another, or the
    // dispatcher's table and ours disagree about what we own.
    if (heldLock.Length > 0 && heldLock != want) { ReleaseAirspace(); return false; }

    if (!awaitingLock || tick - lockAskedTick > 120)
    {
        IGC.SendUnicastMessage(dispatcherAddr, igcChannel, "KA|" + want);
        awaitingLock = true;
        lockAskedTick = tick;
    }
    return false;
}

void ReleaseAirspace()
{
    if (heldLock.Length == 0) return;
    if (HasDispatcher)
        IGC.SendUnicastMessage(dispatcherAddr, igcChannel, "KR|" + heldLock);
    heldLock = "";
    awaitingLock = false;
}

void OnLockGranted(string section)
{
    heldLock = section;
    awaitingLock = false;
}

// ---------------------------------------------------------------------------
//  DISPATCHER SIDE
// ---------------------------------------------------------------------------

void OnLockRequest(long src, string section)
{
    long owner;
    if (lockOwner.TryGetValue(section, out owner))
    {
        if (owner == src) { GrantLock(src, section); return; }   // already theirs

        List<long> q;
        if (!lockQueue.TryGetValue(section, out q)) { q = new List<long>(); lockQueue[section] = q; }
        if (!q.Contains(src)) q.Add(src);
        return;
    }

    lockOwner[section] = src;
    lockExpiry[section] = tick + (long)(LOCK_TIMEOUT_S / Math.Max(dt, 0.01));
    GrantLock(src, section);
}

void OnLockRelease(long src, string section)
{
    long owner;
    if (!lockOwner.TryGetValue(section, out owner) || owner != src) return;

    lockOwner.Remove(section);
    lockExpiry.Remove(section);
    PromoteNextWaiter(section);
}

void GrantLock(long to, string section)
{
    IGC.SendUnicastMessage(to, igcChannel, "KG|" + section);
}

/// <summary>Hand a freed section to whoever has been waiting longest.</summary>
void PromoteNextWaiter(string section)
{
    List<long> q;
    if (!lockQueue.TryGetValue(section, out q) || q.Count == 0) return;

    long next = q[0];
    q.RemoveAt(0);
    lockOwner[section] = next;
    lockExpiry[section] = tick + (long)(LOCK_TIMEOUT_S / Math.Max(dt, 0.01));
    GrantLock(next, section);
}

/// <summary>
/// Reclaim sections whose holder has gone quiet.
///
/// This is the part SCAM lacks. Its locks are held until released, so a drone
/// destroyed mid-shaft blocks that airspace permanently and the only remedy is
/// an operator typing a purge command. A lock is a timed loan, exactly like a
/// lease.
/// </summary>
void ExpireAirspaceLocks()
{
    if (lockOwner.Count == 0) return;

    lockScratch.Clear();
    foreach (var kv in lockExpiry)
        if (tick >= kv.Value) lockScratch.Add(kv.Key);

    for (int i = 0; i < lockScratch.Count; i++)
    {
        string section = lockScratch[i];
        Log("Airspace lock " + section + " expired — reissuing");
        lockOwner.Remove(section);
        lockExpiry.Remove(section);
        PromoteNextWaiter(section);
    }
}

/// <summary>Drop everything a departing drone was holding or queued for.</summary>
void PurgeDroneLocks(long addr)
{
    lockScratch.Clear();
    foreach (var kv in lockOwner) if (kv.Value == addr) lockScratch.Add(kv.Key);

    for (int i = 0; i < lockScratch.Count; i++)
    {
        lockOwner.Remove(lockScratch[i]);
        lockExpiry.Remove(lockScratch[i]);
        PromoteNextWaiter(lockScratch[i]);
    }

    foreach (var kv in lockQueue) kv.Value.Remove(addr);
}

/// <summary>Operator escape hatch. SCAM has one and needs it; expiry means this
/// should never be necessary, which is exactly why it is worth keeping.</summary>
void PurgeAllLocks()
{
    lockOwner.Clear();
    lockExpiry.Clear();
    lockQueue.Clear();
    Log("All airspace locks purged");
}

const double LOCK_TIMEOUT_S = 180.0;
