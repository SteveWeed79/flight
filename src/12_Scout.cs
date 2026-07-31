// ============================================================================
//  SCOUTING
//
//  Read this before wondering why your miner still digs the occasional dry hole.
//
//  Vanilla Space Engineers gives in-game scripts NO access to the Ore Detector.
//  Keen have turned down that API more than once. There is no clever workaround:
//  cameras raycast voxels but report "Asteroid", never "Cobalt". Any vanilla
//  script that claims to fly straight to ore is guessing.
//
//  So VEIN scouts two ways, and uses whichever it has:
//
//    TIER 1 — Probe mapping. Always available. Shallow test shafts on a coarse
//             lattice, ore-per-metre recorded per cell, deep shafts placed by
//             the resulting map. Implemented in 09_Job.cs; this file feeds it.
//
//    TIER 2 — Ore Detector Raycast bridge. If Racher's mod is installed we get
//             real ore coordinates and the probe map becomes a formality. We
//             detect the mod at runtime and light up automatically.
// ============================================================================

const int ORE_SCAN_PERIOD = 6;       // ticks between rays; they are not cheap

/// <summary>
/// One-time check for the Ore Detector Raycast mod. The mod adds terminal
/// properties to IMyOreDetector; on vanilla those properties do not exist and
/// the setter throws. That throw is the detection mechanism.
/// </summary>
void ProbeOreMod()
{
    oreModProbed = true;
    oreModAvailable = false;

    if (!useOreDetectorMod || oreDetectors.Count == 0) return;

    IMyOreDetector d = oreDetectors[0];
    try
    {
        // Write a target, read it back. A vanilla detector throws on the write;
        // a half-installed mod may accept the write and lose the value, so we
        // verify rather than trust.
        Vector3D probe = d.GetPosition() + d.WorldMatrix.Forward * 10.0;
        d.SetValue("RaycastTarget", probe);
        Vector3D echoed = d.GetValue<Vector3D>("RaycastTarget");

        if (Vector3D.DistanceSquared(echoed, probe) > 1.0) return;

        // Stone everywhere would drown out everything we care about.
        try { d.SetValue("OreBlacklist", "Stone"); } catch { }

        oreModAvailable = true;
        Log("Ore Detector Raycast mod found — true ore scouting enabled");
    }
    catch
    {
        // Vanilla. Entirely expected; probe mapping carries the load.
    }
}

/// <summary>Called once per tick. Cheap unless it is actually time to scan.</summary>
void UpdateOreScan()
{
    if (!oreModProbed) ProbeOreMod();
    if (!oreModAvailable) return;
    if (tick % ORE_SCAN_PERIOD != 0) return;
    if (BudgetTight(0.5)) return;

    ExpireSightings();

    IMyOreDetector d = oreDetectors[0];
    if (!d.IsFunctional) return;

    // The modded detector charges range like a camera does. Firing a ray we
    // cannot afford just wastes the call.
    double available;
    try { available = d.GetValue<double>("AvailableScanRange"); }
    catch { oreModAvailable = false; return; }

    Vector3D target;
    double needed;
    if (!NextScanTarget(d.GetPosition(), out target, out needed)) return;
    if (needed > available || needed > oreScanRange) return;

    try
    {
        d.SetValue("RaycastTarget", target);
        MyDetectedEntityInfo hit = d.GetValue<MyDetectedEntityInfo>("RaycastResult");
        if (!hit.IsEmpty() && hit.HitPosition.HasValue)
            AddSighting(hit.Name, hit.HitPosition.Value);
    }
    catch
    {
        oreModAvailable = false;
    }
}

/// <summary>
/// Choose where to point the next ray.
///
/// With a job set we deliberately do not sweep the sky — we rake the volume
/// under the site, cell by cell and depth by depth, because a sighting there
/// lands directly on the yield map and changes what we dig next. A blind 360
/// sweep looks more impressive and is worth much less.
/// </summary>
bool NextScanTarget(Vector3D from, out Vector3D target, out double distance)
{
    target = Vector3D.Zero;
    distance = 0;

    if (job.IsSet)
    {
        // Walk cells across the grid and depths down each one.
        int cellIdx = scanAzimuth % Math.Max(1, job.CellCount);
        int col = cellIdx % job.Width;
        int row = cellIdx / job.Width;

        // Four sample depths spread through the job's depth range.
        int depthStep = scanElevation % 4;
        double depth = job.Depth * (0.25 + 0.25 * depthStep);

        target = job.CellDepth(col, row, depth);

        scanElevation++;
        if (scanElevation % 4 == 0) scanAzimuth++;

        distance = Vector3D.Distance(from, target);
        return true;
    }

    // No job yet: sweep for something interesting. Coarse spiral over the sphere.
    double az = (scanAzimuth % 24) * (Math.PI * 2.0 / 24.0);
    double el = ((scanElevation % 7) - 3) * (Math.PI / 8.0);

    Vector3D dir;
    Vector3D.CreateFromAzimuthAndElevation(az, el, out dir);
    if (controller != null) dir = Vector3D.TransformNormal(dir, controller.WorldMatrix);

    scanAzimuth++;
    if (scanAzimuth % 24 == 0) scanElevation++;

    distance = Math.Min(oreScanRange, 1000.0);
    target = from + dir * distance;
    return true;
}

/// <summary>
/// File an ore sighting. Deduplicated by position, because sweeping the same
/// deposit repeatedly would otherwise let one big vein dominate the score map
/// purely by being scanned more often.
/// </summary>
void AddSighting(string oreType, Vector3D pos)
{
    if (string.IsNullOrEmpty(oreType)) return;
    if (oreType.ToUpperInvariant().Contains("STONE")) return;

    double mergeRadius = Math.Max(4.0, job.Spacing);
    for (int i = 0; i < sightings.Count; i++)
    {
        if (Vector3D.DistanceSquared(sightings[i].Position, pos) < mergeRadius * mergeRadius)
        {
            sightings[i].Tick = tick;      // refresh, do not duplicate
            return;
        }
    }

    OreSighting s = new OreSighting();
    s.OreType = oreType;
    s.Position = pos;
    s.Tick = tick;
    sightings.Add(s);

    // Oldest out first when full.
    while (sightings.Count > SIGHTINGS_MAX) sightings.RemoveAt(0);

    Log("Ore: " + oreType + " at " + Fmt(Vector3D.Distance(pos, shipPos), 0) + "m");

    if (HasDispatcher) SendOreSighting(s);
}

/// <summary>
/// Drop sightings for ground we have since mined out, so the score map does not
/// keep steering the ship back to a hole it already emptied.
/// </summary>
void ExpireSightings()
{
    if (!job.IsSet || cells.Length == 0) return;

    for (int i = sightings.Count - 1; i >= 0; i--)
    {
        int idx = CellIndexNear(sightings[i].Position);
        if (idx < 0) continue;
        if (cells[idx].State == CellState.Exhausted) sightings.RemoveAt(i);
    }
}

/// <summary>Which job cell a world position sits over, or -1 if outside the grid.</summary>
int CellIndexNear(Vector3D worldPos)
{
    if (!job.IsSet || job.Spacing <= 0) return -1;

    Vector3D delta = worldPos - job.Origin;
    double x = Vector3D.Dot(delta, job.Right);
    double y = Vector3D.Dot(delta, job.Forward);

    int col = (int)Math.Round(x / job.Spacing + (job.Width - 1) * 0.5);
    int row = (int)Math.Round(y / job.Spacing + (job.Height - 1) * 0.5);

    if (col < 0 || col >= job.Width || row < 0 || row >= job.Height) return -1;
    return job.IndexOf(col, row);
}

// ---------------------------------------------------------------------------
//  VANILLA CAMERA SENSING
//
//  Cameras cannot see ore, but they can see rock, which is worth plenty:
//  it tells us where the surface actually is rather than where the job frame
//  says it should be.
// ---------------------------------------------------------------------------

/// <summary>
/// Raycast straight ahead for solid material.
///
/// The tri-state matters. "No camera had enough charge" and "the camera looked
/// and there is nothing there" are completely different answers, and collapsing
/// them into a single -1 makes the caller unable to act on either. Callers must
/// distinguish a scan that did not happen from a scan that found empty space.
/// </summary>
/// <param name="distance">Metres to the surface, or -1 if the scan found nothing.</param>
/// <returns>True if a scan actually took place.</returns>
bool TryScanAhead(double maxRange, out double distance)
{
    distance = -1;

    for (int i = 0; i < cameras.Count; i++)
    {
        IMyCameraBlock cam = cameras[i];
        if (!cam.IsFunctional) continue;

        // A camera only accumulates range once raycasting is switched on, so an
        // unarmed camera is armed here and will be usable a few seconds later.
        if (!cam.EnableRaycast) { cam.EnableRaycast = true; continue; }
        if (cam.AvailableScanRange < maxRange) continue;

        MyDetectedEntityInfo hit = cam.Raycast(maxRange);

        if (hit.IsEmpty() || !hit.HitPosition.HasValue) return true;    // looked, saw nothing

        // Only voxels count. A passing ship is not the surface.
        if (hit.Type != MyDetectedEntityType.Asteroid && hit.Type != MyDetectedEntityType.Planet)
            return true;

        distance = Vector3D.Distance(cam.GetPosition(), hit.HitPosition.Value);
        return true;
    }

    return false;   // nothing was in a position to look
}

/// <summary>Keep cameras charged so a raycast is available when we want one.</summary>
void ArmCameras()
{
    for (int i = 0; i < cameras.Count; i++)
        if (cameras[i].IsFunctional && !cameras[i].EnableRaycast)
            cameras[i].EnableRaycast = true;
}

/// <summary>Summary of scouting capability for the display.</summary>
string ScoutStatus()
{
    if (oreModAvailable) return "ore-raycast (" + sightings.Count + " leads)";
    if (oreDetectors.Count > 0) return "probe map (no ore mod)";
    return "probe map";
}
