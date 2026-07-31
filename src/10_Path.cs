// ============================================================================
//  PATH RECORDING AND FOLLOWING
//
//  You fly the route once; the ship flies it forever after. This is PAM's best
//  idea and it is worth stealing wholesale: no pathfinder can know that the gap
//  between those two ridges is the safe way home, but a pilot does.
//
//  What we add is the lift survey. At every recorded point we note how many
//  newtons of upward thrust the ship actually had there. That single number is
//  what lets the ship refuse a load it cannot climb out with, instead of finding
//  out at 200 m with a full hold.
// ============================================================================

/// <summary>Metres of travel between recorded points.</summary>
double RecordInterval { get { return Math.Max(5.0, shipRadius * 2.0); } }

void StartRecording()
{
    path.Clear();
    recording = true;
    maxFlyableMass = -1;

    // Waypoint zero is the dock. Capture its full frame so we can line up on the
    // connector later from the right side and the right way up.
    CaptureHomeDock();
    AddWaypoint(true);

    Log("Recording started");
}

void StopRecording()
{
    if (!recording) return;
    AddWaypoint(true);
    recording = false;
    BuildPathDistances();
    ComputeMaxFlyableMass();
    Log("Recorded " + path.Count + " waypoints, "
        + (maxFlyableMass > 0 ? "lift limit " + Fmt(maxFlyableMass / 1000.0, 1) + "t" : "no gravity on route"));
}

void CaptureHomeDock()
{
    if (controller == null) return;

    if (dockConnector != null)
    {
        MatrixD c = dockConnector.WorldMatrix;
        homeDock = new Waypoint(dockConnector.GetPosition(), gravity, SampleEfficiency(), (float)CurrentLift());
        // The connector's forward is the direction it mates along. Approaching
        // down that axis is the only way to dock reliably.
        homeDockForward = c.Forward;
        homeDockUp = c.Up;
    }
    else
    {
        MatrixD m = controller.WorldMatrix;
        homeDock = new Waypoint(shipPos, gravity, SampleEfficiency(), (float)CurrentLift());
        homeDockForward = m.Forward;
        homeDockUp = m.Up;
    }
    homeDockSet = true;
}

/// <summary>
/// Re-anchor the dock without touching the rest of the route.
///
/// The dock frame is otherwise a one-shot snapshot taken by StartRecording, so
/// repositioning a connector meant re-flying and re-recording the entire route
/// to change the last five metres of it. This re-takes the frame and waypoint
/// zero — the controller's position while mated — and leaves every other
/// waypoint exactly where it is.
///
/// It re-anchors the approach, not the route. If the base moved far enough that
/// the recorded path no longer arrives near it, the path is wrong as well and
/// wants recording properly.
/// </summary>
void RecaptureDock()
{
    CaptureHomeDock();

    Waypoint w = new Waypoint(shipPos, gravity, SampleEfficiency(), (float)CurrentLift());
    if (path.Count == 0) path.Add(w);
    else path[0] = w;

    // Both are derived from the waypoints and one of those just moved.
    BuildPathDistances();
    ComputeMaxFlyableMass();

    Log("Dock re-anchored, " + path.Count + " waypoints kept");
}

/// <summary>Called every tick while recording.</summary>
void RecordTick()
{
    if (!recording) return;
    if (path.Count == 0) { AddWaypoint(true); return; }
    if (Vector3D.DistanceSquared(shipPos, lastRecordPos) < RecordInterval * RecordInterval) return;

    // Hard cap. An operator who forgets to stop recording should not be able to
    // grow this until the block runs out of instruction budget serialising it.
    if (path.Count >= 400)
    {
        if (recording) { Log("Path full at 400 points — recording stopped"); StopRecording(); }
        return;
    }
    AddWaypoint(false);
}

void AddWaypoint(bool force)
{
    if (controller == null) return;
    if (!force && Vector3D.DistanceSquared(shipPos, lastRecordPos) < 1.0) return;

    path.Add(new Waypoint(shipPos, gravity, SampleEfficiency(), (float)CurrentLift()));
    lastRecordPos = shipPos;
}

/// <summary>Upward thrust available right now, in newtons. Zero in space.</summary>
double CurrentLift()
{
    if (gravity.LengthSquared() < 1e-6) return 0;
    return ThrustAlong(-Vector3D.Normalize(gravity));
}

/// <summary>
/// Per-thruster-type effectiveness here. Purely diagnostic — it is what lets the
/// display say "atmospherics dead above this point" instead of just refusing to
/// fly with no explanation.
/// </summary>
float[] SampleEfficiency()
{
    if (thrusterTypes.Count == 0) return new float[0];
    float[] eff = new float[thrusterTypes.Count];

    for (int i = 0; i < thrusterTypes.Count; i++)
    {
        string type = thrusterTypes[i];
        float effective = 0, nominal = 0;

        for (int t = 0; t < thrusters.Count; t++)
        {
            IMyThrust th = thrusters[t];
            if (th.BlockDefinition.SubtypeId != type) continue;
            if (!th.IsFunctional) continue;
            effective += th.MaxEffectiveThrust;
            nominal += th.MaxThrust;
        }
        eff[i] = nominal > 0 ? effective / nominal : -1f;
    }
    return eff;
}

/// <summary>
/// The heaviest the ship may be and still fly the whole route.
///
/// Taken as the minimum over every recorded point of (lift / gravity), because
/// the route is only as flyable as its worst spot — usually the point where the
/// atmospheric thrusters have thinned out but you are still deep in the well.
/// </summary>
void ComputeMaxFlyableMass()
{
    maxFlyableMass = -1;

    for (int i = 0; i < path.Count; i++)
    {
        Waypoint wp = path[i];
        double g = wp.Gravity.Length();
        if (g < 0.05) continue;              // space, or near enough
        if (wp.Lift <= 0) continue;

        double capable = (wp.Lift / g) * liftSafetyFactor;
        if (maxFlyableMass < 0 || capable < maxFlyableMass) maxFlyableMass = capable;
    }

    if (homeDockSet && homeDock != null && homeDock.Gravity.Length() >= 0.05 && homeDock.Lift > 0)
    {
        double capable = (homeDock.Lift / homeDock.Gravity.Length()) * liftSafetyFactor;
        if (maxFlyableMass < 0 || capable < maxFlyableMass) maxFlyableMass = capable;
    }
}

/// <summary>
/// Cargo mass we can still take on before the route stops being flyable.
/// Feeds the "go home now" decision, so a miner in heavy gravity leaves early
/// with a part load rather than filling up and stranding itself.
/// </summary>
double RemainingLiftMargin()
{
    if (maxFlyableMass <= 0) return double.MaxValue;
    return maxFlyableMass - shipMass;
}

bool OverLiftLimit()
{
    return maxFlyableMass > 0 && shipMass >= maxFlyableMass;
}

// ---------------------------------------------------------------------------
//  FOLLOWING
// ---------------------------------------------------------------------------

/// <summary>
/// Precompute distance from the dock to each waypoint, so "how far is home"
/// is a lookup rather than a walk of the whole path every tick.
/// </summary>
void BuildPathDistances()
{
    pathCumulative = new double[path.Count];
    double total = 0;
    for (int i = 0; i < path.Count; i++)
    {
        if (i > 0) total += Vector3D.Distance(path[i].Position, path[i - 1].Position);
        pathCumulative[i] = total;
    }
}

/// <summary>
/// Metres still to fly to reach the dock, following the recorded route rather
/// than the straight line — which is the distance that actually costs fuel.
/// </summary>
double DistanceHomeAlongPath()
{
    if (path.Count == 0) return 0;
    if (pathCumulative.Length != path.Count) BuildPathDistances();

    int idx = Math.Max(0, Math.Min(path.Count - 1, pathIndex));
    // Route distance from waypoint 0, plus however far off that waypoint we are.
    return pathCumulative[idx] + Vector3D.Distance(shipPos, path[idx].Position);
}

double WaypointReached { get { return Math.Max(4.0, shipRadius * 1.5); } }

/// <summary>Nearest waypoint to the ship. Used to rejoin the route from wherever
/// we happen to be, rather than insisting on starting from one end.</summary>
int NearestWaypoint()
{
    int best = -1;
    double bestDist = double.MaxValue;
    for (int i = 0; i < path.Count; i++)
    {
        double d = Vector3D.DistanceSquared(path[i].Position, shipPos);
        if (d < bestDist) { bestDist = d; best = i; }
    }
    return best;
}

/// <summary>
/// Fly the recorded route. <paramref name="outbound"/> runs dock to job,
/// otherwise job to dock.
/// </summary>
/// <returns>True once the far end has been reached.</returns>
bool FollowPath(bool outbound)
{
    if (path.Count == 0) return true;

    pathIndex = Math.Max(0, Math.Min(path.Count - 1, pathIndex));
    Waypoint wp = path[pathIndex];

    double dist = Vector3D.Distance(shipPos, wp.Position);

    if (dist < WaypointReached)
    {
        int next = outbound ? pathIndex + 1 : pathIndex - 1;
        if (next < 0 || next >= path.Count) return true;      // arrived
        pathIndex = next;
        wp = path[pathIndex];
    }

    // Look ahead one point so the ship carves the corner rather than stopping
    // dead at every waypoint. Makes a long route enormously faster and stops the
    // stop-start lurching that shakes ore out of open drills.
    Vector3D aim = wp.Position;
    int ahead = outbound ? pathIndex + 1 : pathIndex - 1;
    if (ahead >= 0 && ahead < path.Count && dist > WaypointReached * 1.5)
    {
        Vector3D toNext = Vector3D.Normalize(path[ahead].Position - wp.Position);
        aim = wp.Position + toNext * Math.Min(WaypointReached, dist * 0.3);
    }

    double speedLimit = cruiseSpeed;

    // Slow down for the ends of the route, where the interesting obstacles are.
    int fromEnd = outbound ? path.Count - 1 - pathIndex : pathIndex;
    if (fromEnd <= 1) speedLimit = Math.Min(speedLimit, 15.0);

    FlyTo(aim, speedLimit);

    // Fly nose-first along the direction of travel: it keeps the drills pointing
    // where we are going, which is where a collision would come from.
    Vector3D heading = aim - shipPos;
    if (heading.LengthSquared() > 4.0)
        Orient(heading, gravity.LengthSquared() > 1e-6 ? -gravity : Vector3D.Zero);

    return false;
}

/// <summary>Set up the path index for a run in the given direction.</summary>
void BeginPath(bool outbound)
{
    if (path.Count == 0) { pathIndex = 0; return; }

    int nearest = NearestWaypoint();
    double distToNearest = Vector3D.Distance(path[nearest].Position, shipPos);

    // Close to the route: rejoin where we are. Far from it: start from the end
    // we are supposed to be starting from and fly in.
    if (distToNearest < WaypointReached * 4)
        pathIndex = nearest;
    else
        pathIndex = outbound ? 0 : path.Count - 1;
}
