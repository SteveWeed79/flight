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
//  Division of labour, now that all three mechanisms exist:
//      lease  — who owns this work            (expires on silence)
//      lock   — who may occupy this airspace  (expires on silence or timeout)
//      lane   — what height you cruise at     (formation, not exclusion)
//      slot   — which base connector is yours (released on undock)
// ============================================================================

/// <summary>The one section VEIN uses: the shared airspace over the job.</summary>
const string LOCK_SITE = "site";

// ---- Miner side -----------------------------------------------------------

/// <summary>Section we hold, empty if none.</summary>
string heldLock = "";
/// <summary>Section we are queued for, empty if none.</summary>
string wantLock = "";
/// <summary>Tick of our last ask, for re-ask backoff.</summary>
long lockAskTick;
/// <summary>Clock reading when we started waiting, in seconds. Bounds the wait.</summary>
double lockWaitStartedAt;
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

    if (wantLock != section)
    {
        wantLock = section;
        lockAskTick = 0;
        lockWaitStartedAt = clock;
        lockOverridden = false;
    }

    // Waiting is bounded. A dispatcher that has stopped answering must not be
    // able to hold the whole fleet in mid-air. Note what the override does and
    // does not do: this ship stops waiting for a section nobody is granting, so
    // it can fly home. It does not resume mining unpoliced — a drone whose
    // dispatcher has gone silent returns to base and waits there.
    if (clock - lockWaitStartedAt > lockPatience)
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

// ---- Dock slots -----------------------------------------------------------
//
//  The fourth mechanism, and until now the only one that was not actually
//  enforced. The dispatcher allocated slot numbers, answered -1 when they were
//  all taken, and expired them when a drone went quiet — but nothing on the
//  drone ever waited for the answer. A drone told to hold off flew the mating
//  run anyway, which is precisely the collision the slot exists to prevent.
//
//  No queue here, unlike the lock. The dispatcher re-grants a slot the asker
//  already holds and otherwise answers -1, so a drone simply asks again.

/// <summary>Clock reading when we started waiting for a slot, in seconds. Zero
/// when not waiting.</summary>
double dockWaitStartedAt;
/// <summary>Tick of our last ask, for re-ask backoff.</summary>
long dockAskTick;
/// <summary>Set once we have given up waiting, so we complain exactly once.</summary>
bool dockOverridden;
/// <summary>Where we parked while waiting. Captured once, so the ship holds a
/// fixed point instead of drifting on whatever it was doing when it stopped.</summary>
Vector3D dockHoldPoint;

/// <summary>
/// May we start the mating run?
///
/// True immediately for a solo miner — the connector is nobody else's — and
/// true once the dispatcher has granted a slot. Otherwise it re-asks
/// periodically and returns false so the caller can hold station.
///
/// Bounded like the airspace lock, and for a sharper version of the same
/// reason: a dispatcher that has stopped answering must not be able to hold a
/// fleet of loaded ships in the air outside their own base, burning the
/// hydrogen they need to land.
/// </summary>
bool AcquireDockSlot()
{
    if (!HasDispatcher) return true;
    if (myDockSlot >= 0) return true;

    if (dockWaitStartedAt == 0) dockWaitStartedAt = clock;

    if (clock - dockWaitStartedAt > dockPatience)
    {
        if (!dockOverridden)
        {
            dockOverridden = true;
            Log("No dock slot granted — docking anyway");
        }
        return true;
    }

    // Re-ask, because a request dropped by the per-tick message cap must not
    // strand a loaded ship short of its own connector.
    if (dockAskTick == 0 || tick - dockAskTick > 60)
    {
        RequestDock();
        dockAskTick = tick;
    }
    return false;
}

/// <summary>Forget any slot wait. Called when a return leg begins.</summary>
void ResetDockWait()
{
    dockWaitStartedAt = 0;
    dockAskTick = 0;
    dockOverridden = false;
    dockHoldPoint = Vector3D.Zero;
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

    heldLock = f[1];
    wantLock = "";
    lockOverridden = false;
}

// ---- Dispatcher side ------------------------------------------------------

/// <summary>Who holds each section.</summary>
readonly Dictionary<string, long> lockOwner = new Dictionary<string, long>();
/// <summary>When each section was granted, for the hold timeout.</summary>
readonly Dictionary<string, double> lockGrantedAt = new Dictionary<string, double>();
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
    lockGrantedAt[section] = clock;
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

    double hold = Math.Max(30.0, stateTimeout);

    lockScratch.Clear();
    foreach (var kv in lockOwner)
    {
        long owner = kv.Value;
        if (owner == 0) continue;

        DroneRecord r;
        bool gone = !fleet.TryGetValue(owner, out r) || clock - r.LastSeenAt > droneTimeout;

        double granted;
        lockGrantedAt.TryGetValue(kv.Key, out granted);
        bool stale = granted != 0 && clock - granted > hold;

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
        lockGrantedAt.Clear();
        lockQueue.Clear();
        Log("Airspace locks purged");
        return;
    }

    ReleaseAirspace();
    Log("Airspace released");
}

/// <summary>Who holds what, for the dispatcher's screen.</summary>
string AirspaceStatus()
{
    if (!airspaceLock) return "off";

    long owner = 0;
    lockOwner.TryGetValue(LOCK_SITE, out owner);

    int waiting = 0;
    List<long> q;
    if (lockQueue.TryGetValue(LOCK_SITE, out q)) waiting = q.Count;

    if (owner == 0) return waiting > 0 ? "free, " + waiting + " waiting" : "free";

    DroneRecord r;
    string name = fleet.TryGetValue(owner, out r) ? r.Name : "?";
    return name + (waiting > 0 ? " (+" + waiting + " waiting)" : "");
}
