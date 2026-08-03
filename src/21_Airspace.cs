// ============================================================================
//  AIRSPACE LOCKING
//
//  Two drones want the same twelve metres of sky above the same shaft. Altitude
//  lanes — which is what VEIN did on its own — stop them sharing a *height*, and
//  that is all they do. They do not stop two drones wanting the same place at the
//  same height, and they scale badly: N drones need N distinct altitudes, which
//  is absurd past a handful.
//
//  SCAM's answer is better and is taken wholesale here: a named-section mutex,
//  held by one drone at a time, with a FIFO queue behind it.
//
//      KA|<section>    agent -> dispatcher    ask for the lock
//      KG|<section>    dispatcher -> agent    granted ("-" means revoked)
//      KR|<section>    agent -> dispatcher    released
//
//  The dispatcher grants the section if nobody holds it and otherwise queues the
//  asker; on release it hands the section straight to the head of the queue. A
//  drone with no lock waits somewhere explicitly safe rather than improvising.
//
//  One thing is deliberately *not* copied. SCAM grants a lock outright and never
//  takes it back, which is why it ships a manual purge command: a drone that dies
//  holding the section deadlocks the deposit until a human intervenes. VEIN
//  already treats shaft ownership as a timed lease for exactly that reason, so
//  the lock gets the same treatment. A lock is a loan with a deadline.
//
//  Sections are striped by row rather than one per site — see
//  LOCK_ROWS_PER_SECTION. Division of labour across all four mechanisms:
//      lease  — who owns this work            (expires on silence)
//      lock   — who may occupy this airspace  (expires on silence or timeout)
//      lane   — what height you cruise at     (formation, not exclusion)
//      slot   — which base connector is yours (released on undock)
// ============================================================================

/// <summary>
/// Rows of cells per airspace section.
///
/// SCAM uses a single shared section, and that is right for SCAM: its deposits
/// are a few shafts across, so two drones over the same rock genuinely do
/// conflict. VEIN sites run to 20x20 and further once the grid grows, and there
/// the far corners are a hundred metres apart — serialising them saturates at
/// three or four drones and the overflow behaviour is "go anyway", which makes a
/// fleet of five less safe than a fleet of two.
///
/// Striping by row keeps exclusion where the conflict actually is. Four rows is
/// roughly ten metres of ground at a large-grid pitch, comfortably more than the
/// 12 m echelon both ancestors converged on, so two drones in adjacent stripes
/// are never in each other's way.
/// </summary>
const int LOCK_ROWS_PER_SECTION = 4;

/// <summary>Section name for the airspace above a cell.</summary>
string AirspaceSection(int cellIdx)
{
    if (!airspaceLock || cellIdx < 0 || job.Width <= 0) return "site";
    return "r" + (CellRow(cellIdx) / LOCK_ROWS_PER_SECTION);
}

// ---- Miner side -----------------------------------------------------------

/// <summary>Section we hold, empty if none.</summary>
string heldLock = "";
/// <summary>Section we are queued for, empty if none.</summary>
string wantLock = "";
/// <summary>Tick of our last ask, for re-ask backoff.</summary>
long lockAskTick;
/// <summary>Tick we started waiting. Bounds the wait.</summary>
long lockWaitTick;
/// <summary>Set once we have given up waiting, so we complain exactly once.</summary>
bool lockOverridden;
/// <summary>Where we parked while waiting. Captured once so the ship holds a
/// fixed point instead of drifting with whatever it was doing when it stopped.</summary>
Vector3D lockHoldPoint;

/// <summary>
/// May we manoeuvre in the shared airspace over the site?
///
/// Returns true immediately for a solo miner — there is nobody to collide with —
/// and true once the dispatcher has granted the section. Otherwise it sends the
/// request (re-sending periodically, because a request dropped by the per-tick
/// message cap must not park a ship forever) and returns false.
/// </summary>
bool AcquireAirspace(string section)
{
    if (!airspaceLock || !HasDispatcher) return true;
    if (heldLock == section) return true;

    // Holding a different one? Give it back before asking for this. Sections are
    // per stripe now, so a drone crossing from one to the next would otherwise
    // accumulate them and the queue behind the section it left would never run.
    if (heldLock.Length > 0) ReleaseAirspace();

    if (wantLock != section)
    {
        wantLock = section;
        lockAskTick = 0;
        lockWaitTick = tick;
        lockOverridden = false;
    }

    // Waiting is bounded. A dispatcher that has stopped answering must not be
    // able to hold the whole fleet in mid-air, which is the same reasoning that
    // makes a missing dispatcher fall back to solo mining rather than parking.
    if (tick - lockWaitTick > (long)(lockPatience / Math.Max(dt, 0.01)))
    {
        if (!lockOverridden)
        {
            lockOverridden = true;
            Log("Airspace '" + section + "' never granted — proceeding on lanes");
            // Leave the queue on the way past. Otherwise the dispatcher hands us
            // a section we stopped waiting for, we never use it, and it sits
            // owned by an uninterested drone until the hold timeout expires.
            if (dispatcherAddr != 0)
                IGC.SendUnicastMessage(dispatcherAddr, igcChannel, "KR|" + section);
        }
        return true;
    }

    if (lockAskTick == 0 || tick - lockAskTick > 60)
    {
        IGC.SendUnicastMessage(dispatcherAddr, igcChannel, "KA|" + section);
        lockAskTick = tick;
    }
    return false;
}

/// <summary>
/// Begin a fresh wait for the airspace.
///
/// Called on entry to each phase that needs it. Without this, giving up once
/// latches for the rest of the shaft cycle: wantLock still names the section, so
/// AcquireAirspace never re-arms the clock and returns true unconditionally
/// thereafter. A single busy moment during the approach would therefore disable
/// the mutex for the climb out as well — and the climb is the half that matters,
/// because that is where the ship comes up blind into shared sky.
/// </summary>
void RearmAirspace()
{
    if (heldLock.Length > 0) return;      // still ours; nothing to re-arm
    wantLock = "";
    lockOverridden = false;
}

/// <summary>Give the section back. Safe to call when we hold nothing.</summary>
void ReleaseAirspace()
{
    wantLock = "";
    lockOverridden = false;
    if (heldLock.Length == 0) return;

    if (dispatcherAddr != 0)
        IGC.SendUnicastMessage(dispatcherAddr, igcChannel, "KR|" + heldLock);
    heldLock = "";
}

void OnLockGrant(long src, string[] f)
{
    if (role != Role.Miner || f.Length < 2) return;

    // "-" is a revocation: the dispatcher decided we had it long enough, or that
    // we had gone quiet. Told rather than left to find out, so the ship stops
    // believing it owns airspace it does not.
    if (f[1] == "-")
    {
        if (heldLock.Length > 0) Log("Airspace '" + heldLock + "' revoked");
        heldLock = "";
        return;
    }

    // Only accept what we are actually waiting for. A grant can arrive after we
    // gave up, after we moved on to a different section, or after we docked —
    // and silently holding it then means the dispatcher believes a drone at base
    // owns airspace over the site until the hold timeout expires.
    if (f[1] != wantLock)
    {
        IGC.SendUnicastMessage(src, igcChannel, "KR|" + f[1]);
        return;
    }

    heldLock = f[1];
    wantLock = "";
    lockOverridden = false;
}

// ---- Dispatcher side ------------------------------------------------------

/// <summary>Who holds each section.</summary>
readonly Dictionary<string, long> lockOwner = new Dictionary<string, long>();
/// <summary>When each section was granted, for the hold timeout.</summary>
readonly Dictionary<string, long> lockGrantTick = new Dictionary<string, long>();
/// <summary>Who is waiting for each section, oldest first. A List rather than a
/// Queue because drones die and have to be removed from the middle.</summary>
readonly Dictionary<string, List<long>> lockQueue = new Dictionary<string, List<long>>();
/// <summary>Scratch for expiry, which cannot mutate a dictionary it is walking.</summary>
readonly List<string> lockScratch = new List<string>();

void OnLockAsk(long src, string[] f)
{
    if (role != Role.Dispatcher || f.Length < 2) return;
    string section = f[1];

    // Asking is a sign of life. Register it before granting, or expiry will
    // reclaim the section from a drone it has never heard of.
    DroneFor(src);

    long owner;
    if (lockOwner.TryGetValue(section, out owner) && owner != 0)
    {
        // Already theirs: a re-ask after a dropped grant. Answer it again rather
        // than queue them behind themselves, which is a deadlock of one.
        if (owner == src) { SendLockGrant(src, section); return; }
        QueueFor(section, src);
        return;
    }

    GiveLock(section, src);
}

void OnLockRelease(long src, string[] f)
{
    if (role != Role.Dispatcher || f.Length < 2) return;
    string section = f[1];

    long owner;
    if (!lockOwner.TryGetValue(section, out owner) || owner != src)
    {
        // Not theirs to give back. Harmless — a revoked drone still sends its
        // release — but it must not hand the section to the queue twice.
        DropFromQueues(src);
        return;
    }

    lockOwner[section] = 0;
    PromoteNext(section);
}

void SendLockGrant(long to, string section)
{
    IGC.SendUnicastMessage(to, igcChannel, "KG|" + section);
}

void GiveLock(string section, long to)
{
    lockOwner[section] = to;
    lockGrantTick[section] = tick;
    DropFromQueues(to);
    SendLockGrant(to, section);
}

void QueueFor(string section, long who)
{
    List<long> q;
    if (!lockQueue.TryGetValue(section, out q)) { q = new List<long>(); lockQueue[section] = q; }
    if (!q.Contains(who)) q.Add(who);
}

/// <summary>Hand a free section to whoever has waited longest.</summary>
void PromoteNext(string section)
{
    List<long> q;
    if (!lockQueue.TryGetValue(section, out q)) return;

    while (q.Count > 0)
    {
        long next = q[0];
        q.RemoveAt(0);
        // Skip anyone who died while queued.
        if (!fleet.ContainsKey(next)) continue;
        GiveLock(section, next);
        return;
    }
}

void DropFromQueues(long who)
{
    foreach (var kv in lockQueue) kv.Value.Remove(who);
}

/// <summary>
/// Take back locks whose holder has gone quiet or has had one long enough.
///
/// This is the half SCAM does not have, and the reason it needs a human with a
/// purge command. The timeout is the same bound the drone's own watchdog uses, so
/// by the time the section is reclaimed the drone that held it has already
/// recovered out of the state that wanted it.
/// </summary>
void ExpireAirspaceLocks()
{
    if (lockOwner.Count == 0) return;

    long silence = (long)(droneTimeout / Math.Max(dt, 0.01));
    // The ceiling has to outlast a legitimate hold, which is bounded by the
    // holder's own watchdog. Reclaiming sooner takes the section off a drone
    // that is still using it, which is worse than leaving it a little long.
    long hold = (long)(Math.Max(30.0, stateTimeout) / Math.Max(dt, 0.01));

    lockScratch.Clear();
    foreach (var kv in lockOwner)
    {
        long owner = kv.Value;
        if (owner == 0) continue;

        DroneRecord r;
        bool gone = !fleet.TryGetValue(owner, out r) || tick - r.LastSeenTick > silence;

        long granted;
        lockGrantTick.TryGetValue(kv.Key, out granted);
        bool stale = granted != 0 && tick - granted > hold;

        if (gone || stale) lockScratch.Add(kv.Key);
    }

    for (int i = 0; i < lockScratch.Count; i++)
    {
        string section = lockScratch[i];
        long owner = lockOwner[section];
        Log("Airspace '" + section + "' reclaimed");
        lockOwner[section] = 0;
        // Tell the holder, if it is still listening. A drone that believes it
        // owns airspace it has lost is worse than one that knows it has none.
        IGC.SendUnicastMessage(owner, igcChannel, "KG|-");
        PromoteNext(section);
    }
}

/// <summary>Release everything a departed drone held.</summary>
void ReleaseLocksOf(long who)
{
    lockScratch.Clear();
    foreach (var kv in lockOwner) if (kv.Value == who) lockScratch.Add(kv.Key);
    for (int i = 0; i < lockScratch.Count; i++)
    {
        lockOwner[lockScratch[i]] = 0;
        PromoteNext(lockScratch[i]);
    }
    DropFromQueues(who);
}

/// <summary>Operator escape hatch. SCAM ships one of these and it is not a sign
/// of weakness — it is what you want at 2am when something has gone wrong in a
/// way nobody predicted.</summary>
void PurgeAirspace()
{
    if (role == Role.Dispatcher)
    {
        lockScratch.Clear();
        foreach (var kv in lockOwner) if (kv.Value != 0) lockScratch.Add(kv.Key);
        for (int i = 0; i < lockScratch.Count; i++)
            IGC.SendUnicastMessage(lockOwner[lockScratch[i]], igcChannel, "KG|-");

        lockOwner.Clear();
        lockGrantTick.Clear();
        lockQueue.Clear();
        Log("Airspace locks purged");
        return;
    }

    ReleaseAirspace();
    Log("Airspace released");
}

/// <summary>How the sections are being used, for the dispatcher's screen.</summary>
string AirspaceStatus()
{
    if (!airspaceLock) return "off";

    int held = 0;
    foreach (var kv in lockOwner) if (kv.Value != 0) held++;

    int waiting = 0;
    foreach (var kv in lockQueue) waiting += kv.Value.Count;

    if (held == 0 && waiting == 0) return "all clear";
    return held + " stripe(s) held" + (waiting > 0 ? ", " + waiting + " waiting" : "");
}
