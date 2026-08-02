// ============================================================================
//  JOB GEOMETRY AND SHAFT SELECTION
//
//  PAM digs a rectangle in a fixed order. SCAM adapts depth per shaft but still
//  works through the whole area. Both spend the same effort on rock that turns
//  out to be empty as on rock that is full of gold.
//
//  VEIN treats the site as something to be *surveyed*: cheap shallow probes on a
//  coarse lattice, then deep production shafts placed where the survey says ore
//  actually is. On a typical scattered deposit that is the difference between
//  digging 100 holes and digging 30.
// ============================================================================

/// <summary>
/// Capture the current position and attitude as the job frame.
/// Whatever direction the drills are pointing becomes "down the shaft", so this
/// works identically on a planet surface and on the side of an asteroid.
/// </summary>
void SetJob(int width, int height, int depth)
{
    if (controller == null)
    {
        // The usual way to land here is running this on a dispatcher bolted to a
        // base, which has no remote control and no drills to derive a pitch from.
        Log(role == Role.Dispatcher
            ? "Cannot set job here: no controller. Anchor it on a miner and run 'job push'."
            : "Cannot set job: no controller");
        return;
    }

    MatrixD m = controller.WorldMatrix;

    job.IsSet = true;
    job.Origin = DrillFace();
    job.Down = Vector3D.Normalize(m.Forward);     // drills point forward
    job.Right = Vector3D.Normalize(m.Right);
    job.Forward = Vector3D.Normalize(m.Up);
    job.Width = Math.Max(1, width);
    job.Height = Math.Max(1, height);
    job.Depth = Math.Max(1, depth);

    MeasureShip();                  // refresh drill geometry
    job.Spacing = derivedSpacing;   // fix the pitch once, here
    RebuildCells();

    activeCell = -1;
    jobComplete = false;
    probePassDone = false;

    Log("Job " + job.Width + "x" + job.Height + " @" + job.Depth + "m, pitch "
        + Fmt(job.Spacing, 1) + "m");
}

/// <summary>
/// Normalise and sanity-check a job frame that came from outside — restored
/// Storage or a dispatcher beacon. Truncated or corrupt data yields zero-length
/// or non-perpendicular axes, and nothing downstream checks: CellMouth collapses
/// every shaft onto the origin and Orient is handed a zero forward, so the ship
/// flies to one point and sits there with no indication why.
/// </summary>
/// <returns>False if the frame is unusable, in which case the job is cleared.</returns>
bool ValidateJobBasis()
{
    if (job.Down.LengthSquared() < 1e-6 || job.Right.LengthSquared() < 1e-6
        || job.Forward.LengthSquared() < 1e-6)
    {
        Log("Job frame is degenerate — clearing");
        job.IsSet = false;
        return false;
    }

    job.Down = Vector3D.Normalize(job.Down);
    job.Right = Vector3D.Normalize(job.Right);
    job.Forward = Vector3D.Normalize(job.Forward);

    // Millimetre wire precision costs a little orthogonality; a badly skewed
    // frame means the data is wrong, not merely rounded.
    if (Math.Abs(Vector3D.Dot(job.Down, job.Right)) > 0.05
        || Math.Abs(Vector3D.Dot(job.Down, job.Forward)) > 0.05
        || Math.Abs(Vector3D.Dot(job.Right, job.Forward)) > 0.05)
    {
        Log("Job frame axes are not perpendicular — clearing");
        job.IsSet = false;
        return false;
    }

    if (job.Spacing < 0.1 || job.Spacing > 100) job.Spacing = derivedSpacing;
    return true;
}

void RebuildCells()
{
    cells = new YieldCell[job.CellCount];
    for (int i = 0; i < cells.Length; i++) cells[i] = new YieldCell();
}

/// <summary>Cells scored per selection pass. Bounds the cost so a small-grid
/// job with hundreds of cells cannot exceed the instruction limit.</summary>
const int SCORE_BUDGET = 48;
/// <summary>Rotating start point for the bounded scan.</summary>
int scoreCursor;

int CellCol(int idx) { return idx % job.Width; }
int CellRow(int idx) { return idx / job.Width; }

// ---------------------------------------------------------------------------
//  SELECTION
// ---------------------------------------------------------------------------

/// <summary>
/// Choose the next shaft to dig. Returns -1 when the job is finished.
/// Also decides whether that shaft is a cheap probe or a full-depth production
/// hole, via <see cref="shaftIsProbe"/>.
/// </summary>
int SelectNextCell()
{
    return SelectNextCell(shipPos);
}

/// <summary>
/// Choose the next shaft, measuring travel from <paramref name="origin"/>.
/// The dispatcher passes the requesting drone's position so that work is handed
/// to whoever is closest to it, rather than to whoever asked first.
///
/// Nothing left is not necessarily the end. If the survey says the ore is still
/// rich where the grid stops, the grid is in the wrong place — so grow it and
/// ask again.
/// </summary>
int SelectNextCell(Vector3D origin)
{
    int idx = PickCell(origin);
    if (idx >= 0) return idx;
    if (!GrowJobTowardOre()) return -1;
    return PickCell(origin);
}

int PickCell(Vector3D origin)
{
    selectionOrigin = origin;
    if (cells.Length != job.CellCount) RebuildCells();

    switch (holeOrder)
    {
        case HoleOrder.Serpentine: shaftIsProbe = false; return NextSerpentine();
        case HoleOrder.Spiral:     shaftIsProbe = false; return NextSpiral();
        default:                                          return NextProspect();
    }
}

/// <summary>Corner to corner, reversing each row. Least total travel.</summary>
int NextSerpentine()
{
    for (int row = 0; row < job.Height; row++)
    {
        for (int i = 0; i < job.Width; i++)
        {
            // Reverse odd rows so the ship finishes each row next to the start
            // of the following one instead of flying all the way back.
            int col = (row % 2 == 0) ? i : job.Width - 1 - i;
            int idx = job.IndexOf(col, row);
            if (cells[idx].Available) return idx;
        }
    }
    return -1;
}

/// <summary>Outward square spiral from the centre. Good for a centred deposit.</summary>
int NextSpiral()
{
    int cx = job.Width / 2, cy = job.Height / 2;
    int x = 0, y = 0, dx = 0, dy = -1;
    int bound = Math.Max(job.Width, job.Height);
    int steps = bound * bound;

    for (int i = 0; i < steps; i++)
    {
        int col = cx + x, row = cy + y;
        if (col >= 0 && col < job.Width && row >= 0 && row < job.Height)
        {
            int idx = job.IndexOf(col, row);
            if (cells[idx].Available) return idx;
        }

        // Turn at the corners of the growing square.
        if (x == y || (x < 0 && x == -y) || (x > 0 && x == 1 - y))
        {
            int t = dx; dx = -dy; dy = t;
        }
        x += dx; y += dy;
    }
    return -1;
}

/// <summary>
/// Survey first, then mine the good parts.
///
/// Phase 1 walks a coarse lattice, drilling shallow probes. Phase 2 scores every
/// remaining cell from what the probes found and digs the best one next.
/// </summary>
int NextProspect()
{
    // ---- Phase 1: survey ---------------------------------------------------
    if (!probePassDone)
    {
        int probe = NextProbeCell();
        if (probe >= 0) { shaftIsProbe = true; return probe; }
        probePassDone = true;
        Log("Survey complete: " + RichCellCount() + " rich of " + ProbedCellCount() + " probed");
    }

    // ---- Phase 2: production ----------------------------------------------
    shaftIsProbe = false;

    int best = -1;
    double bestScore = double.MinValue;

    // Scoring every cell is O(cells x neighbourhood), which is fine on a
    // large-grid job of 80 cells and fatal on a small-grid one. A small drill
    // head cuts a ~1.1 m pitch, so a modest 20 x 20 m site is over 300 cells and
    // a full sweep runs to six figures of instructions — well past the 50,000
    // limit, and the block is killed for complexity.
    //
    // So: score the neighbourhood of the best cell we know about, then a bounded
    // rotating window of everything else. The neighbourhood term is what
    // preserves the important behaviour — following a vein once it is found —
    // while the window keeps the cost flat regardless of job size.
    int anchor = RichestCell();
    if (anchor >= 0)
    {
        int ac = CellCol(anchor), ar = CellRow(anchor);
        for (int r = Math.Max(0, ar - 2); r <= Math.Min(job.Height - 1, ar + 2); r++)
            for (int c = Math.Max(0, ac - 2); c <= Math.Min(job.Width - 1, ac + 2); c++)
                Consider(job.IndexOf(c, r), ref best, ref bestScore);
    }

    int window = Math.Min(SCORE_BUDGET, cells.Length);
    for (int k = 0; k < window; k++)
        Consider((scoreCursor + k) % cells.Length, ref best, ref bestScore);
    scoreCursor = (scoreCursor + window) % Math.Max(1, cells.Length);

    // Nothing scored well in this window, but work remains somewhere. Take the
    // first available cell rather than reporting the job finished — a bounded
    // scan must never be able to end a job early.
    if (best < 0 && RemainingCellCount() > 0)
    {
        for (int i = 0; i < cells.Length; i++)
            if (cells[i].Available) return i;
    }

    // Everything left is written off as barren. That is a finished job, not a
    // failure — the whole point is to stop digging rock that has nothing in it.
    return best;
}

/// <summary>Fold one candidate into the running best, if it qualifies.</summary>
void Consider(int idx, ref int best, ref double bestScore)
{
    if (idx < 0 || idx >= cells.Length) return;
    if (!cells[idx].Available) return;

    double score = ScoreCell(idx);
    // A cell already judged barren from its own probe is only worth revisiting
    // if its neighbours turned out rich.
    if (cells[idx].State == CellState.Barren && score < barrenThreshold) return;
    if (score < bestScore) return;

    bestScore = score;
    best = idx;
}

/// <summary>Highest-yielding cell found so far, or -1. Deliberately cheap — a
/// field compare per cell, no neighbourhood maths.</summary>
int RichestCell()
{
    int best = -1;
    float bestYield = 0f;
    for (int i = 0; i < cells.Length; i++)
    {
        if (cells[i].MetresDrilled < 0.5f) continue;
        if (cells[i].Yield <= bestYield) continue;
        bestYield = cells[i].Yield;
        best = i;
    }
    return best;
}

/// <summary>
/// Lattice coarseness in cells, derived so probes land a sensible distance
/// apart in *metres* whatever the drill head is doing.
///
/// probeStride is configured in cells, but cell size is set by the drill's cut
/// radius — 3.2 m on a large-grid head, 1.1 m on a small one. Probing every
/// second cell means a test shaft every 6 m on the former and every 2 m on the
/// latter, which is far more survey than the ground warrants. The configured
/// value acts as a floor; this raises it when cells are fine.
/// </summary>
int EffectiveProbeStride()
{
    const double TARGET_SPACING_M = 8.0;
    int byDistance = (int)Math.Round(TARGET_SPACING_M / Math.Max(0.5, job.Spacing));
    return Math.Max(1, Math.Max(probeStride, byDistance));
}

/// <summary>Next un-probed lattice point, nearest to the ship first.</summary>
int NextProbeCell()
{
    int best = -1;
    double bestDist = double.MaxValue;
    int stride = EffectiveProbeStride();

    for (int row = 0; row < job.Height; row += stride)
    {
        for (int col = 0; col < job.Width; col += stride)
        {
            int idx = job.IndexOf(col, row);
            if (cells[idx].State != CellState.Unknown) continue;
            if (!cells[idx].Available) continue;

            double d = Vector3D.DistanceSquared(job.CellMouth(col, row, 0), selectionOrigin);
            if (d < bestDist) { bestDist = d; best = idx; }
        }
    }
    return best;
}

/// <summary>
/// Expected value of digging this cell, in kg of ore per metre.
///
/// Built from three signals, best first:
///   1. Real ore coordinates, if the Ore Detector Raycast mod gave us any.
///   2. Inverse-distance-weighted yield of nearby cells we have already dug.
///   3. A small optimism prior, so untouched ground is preferred over ground we
///      have already proved empty.
/// Travel distance is then subtracted so that, all else equal, the ship digs the
/// near candidate rather than flying across the site for the same result.
/// </summary>
double ScoreCell(int idx)
{
    int col = CellCol(idx), row = CellRow(idx);

    const int RADIUS = 2;       // 5x5 neighbourhood; O(25), not O(n)
    double weighted = 0, weight = 0;

    int c0 = Math.Max(0, col - RADIUS), c1 = Math.Min(job.Width - 1, col + RADIUS);
    int r0 = Math.Max(0, row - RADIUS), r1 = Math.Min(job.Height - 1, row + RADIUS);

    for (int r = r0; r <= r1; r++)
    {
        for (int c = c0; c <= c1; c++)
        {
            int n = job.IndexOf(c, r);
            if (n == idx) continue;
            YieldCell cell = cells[n];
            if (cell.MetresDrilled < 0.5f) continue;   // never sampled, says nothing

            int dc = c - col, dr = r - row;
            double d2 = dc * dc + dr * dr;
            double w = 1.0 / (1.0 + d2);
            weighted += w * cell.Yield;
            weight += w;
        }
    }

    // With no evidence either way, assume slightly better than the cut-off. That
    // biases the ship toward exploring unknown ground rather than re-chewing
    // ground next to a single lucky hit.
    double estimate = weight > 0 ? weighted / weight : barrenThreshold * 1.5;

    estimate += SightingBonus(col, row);

    // Travel cost, expressed in the same units as yield so they can be compared.
    double travel = Vector3D.Distance(job.CellMouth(col, row, 0), selectionOrigin);
    estimate -= travel * 0.004;

    return estimate;
}

/// <summary>
/// Boost from confirmed ore positions. Only ever non-zero when the Ore Detector
/// Raycast mod is present and has actually seen something.
/// </summary>
double SightingBonus(int col, int row)
{
    if (sightings.Count == 0) return 0;

    Vector3D mouth = job.CellMouth(col, row, 0);
    double bonus = 0;

    for (int i = 0; i < sightings.Count; i++)
    {
        // Distance measured on the job plane only — a deposit 40 m straight down
        // is still directly under this cell and absolutely counts.
        Vector3D delta = sightings[i].Position - mouth;
        double along = Vector3D.Dot(delta, job.Down);
        Vector3D lateral = delta - job.Down * along;

        double lat = lateral.Length();
        if (lat > job.Spacing * 4) continue;
        if (along < -job.Spacing || along > job.Depth + 20) continue;

        // Big, decaying with lateral offset. Confirmed ore should dominate
        // every other term in the score.
        bonus += 25.0 / (1.0 + lat);
    }
    return bonus;
}

// ---------------------------------------------------------------------------
//  GROWING THE JOB
//
//  PAM's rectangle matches nothing in particular; SCAM's circular generations
//  match a spherical deposit and nothing else. Both make you guess the shape of
//  the ore before you have seen any of it, and both then dig exactly the region
//  you guessed.
//
//  With a yield map the question stops existing. If the cells along an edge of
//  the job are still producing when the job runs out of work, the deposit
//  carries on past the boundary and the boundary was arbitrary. Extend it that
//  way and let the deposit's real shape emerge from what was measured.
// ---------------------------------------------------------------------------

/// <summary>Cells added to an edge that is still rich.</summary>
const int GROW_STEP = 2;

/// <summary>Is any shaft currently on loan to a drone? Anything that renumbers
/// cells has to check this first — indices are the fleet's shared vocabulary,
/// and moving them under a drone that holds one sends it to the wrong rock.</summary>
bool AnyCellLeased()
{
    for (int i = 0; i < cells.Length; i++)
        if (cells[i].State == CellState.Leased) return true;
    return false;
}

/// <summary>
/// Extend the grid toward ore that runs off the edge of it.
/// </summary>
/// <returns>True if the grid changed, in which case indices have moved.</returns>
bool GrowJobTowardOre()
{
    if (!growToOre || !job.IsSet || cells.Length == 0) return false;
    // Only the prospector has a map to grow from. Serpentine and Spiral are
    // "dig this rectangle" by definition and it would be rude to redefine them.
    if (holeOrder != HoleOrder.Prospect) return false;
    if (job.CellCount >= growLimit) return false;

    // Cell indices are the fleet's shared vocabulary and they are about to
    // change. Never while somebody is out there holding one.
    if (AnyCellLeased()) return false;

    int dl = EdgeStillRich(0) ? GROW_STEP : 0;
    int dr = EdgeStillRich(1) ? GROW_STEP : 0;
    int dt = EdgeStillRich(2) ? GROW_STEP : 0;
    int db = EdgeStillRich(3) ? GROW_STEP : 0;
    if (dl + dr + dt + db == 0) return false;

    int nw = job.Width + dl + dr;
    int nh = job.Height + dt + db;
    if (nw * nh > growLimit) return false;

    // Keep every cell where it is in the world. Widening by dl on the left and
    // dr on the right moves the centre by half their difference; the same on the
    // other axis. Get this wrong and the whole survey slides off its own ground.
    YieldCell[] grown = new YieldCell[nw * nh];
    for (int i = 0; i < grown.Length; i++) grown[i] = new YieldCell();

    for (int row = 0; row < job.Height; row++)
        for (int col = 0; col < job.Width; col++)
            grown[(row + dt) * nw + (col + dl)] = cells[row * job.Width + col];

    job.Origin += job.Right * (job.Spacing * (dr - dl) * 0.5)
                + job.Forward * (job.Spacing * (db - dt) * 0.5);
    job.Width = nw;
    job.Height = nh;
    cells = grown;

    // The new ground is unsurveyed, so the probe pass has work again.
    probePassDone = false;
    activeCell = -1;
    scoreCursor = 0;

    Log("Ore continues past the edge — job grown to " + nw + "x" + nh);
    if (role == Role.Dispatcher) SendBeacon();
    return true;
}

/// <summary>
/// Is the ore still worth having along one edge of the grid?
///
/// Judged on total kilograms over total metres across the whole edge rather than
/// cell by cell, so one lucky hole cannot grow the site on its own and one dry
/// hole in a good seam cannot stop it.
/// </summary>
bool EdgeStillRich(int side)
{
    double ore = 0, metres = 0;
    int sampled = 0;

    int count = (side < 2) ? job.Height : job.Width;
    for (int i = 0; i < count; i++)
    {
        int col, row;
        if (side == 0) { col = 0; row = i; }
        else if (side == 1) { col = job.Width - 1; row = i; }
        else if (side == 2) { col = i; row = 0; }
        else { col = i; row = job.Height - 1; }

        YieldCell c = cells[job.IndexOf(col, row)];
        if (c.MetresDrilled < 0.5f) continue;
        // Ground we could not physically reach says nothing about the ore.
        if (c.State == CellState.Blocked) continue;

        ore += c.OreKg;
        metres += c.MetresDrilled;
        sampled++;
    }

    // One hole is an anecdote. Demand at least two, so a single probe on a
    // corner cannot walk the job across the map.
    if (sampled < 2 || metres < 1.0) return false;
    return ore / metres >= barrenThreshold;
}

int RichCellCount()
{
    int n = 0;
    for (int i = 0; i < cells.Length; i++) if (cells[i].State == CellState.Rich) n++;
    return n;
}

int ProbedCellCount()
{
    int n = 0;
    for (int i = 0; i < cells.Length; i++) if (cells[i].MetresDrilled > 0.5f) n++;
    return n;
}

int RemainingCellCount()
{
    int n = 0;
    for (int i = 0; i < cells.Length; i++) if (cells[i].Available) n++;
    return n;
}

double JobProgress()
{
    if (cells.Length == 0) return 0;
    int done = 0;
    for (int i = 0; i < cells.Length; i++)
        if (cells[i].State == CellState.Exhausted || cells[i].State == CellState.Blocked) done++;
    return (double)done / cells.Length;
}

// ---------------------------------------------------------------------------
//  RESULT RECORDING
// ---------------------------------------------------------------------------

/// <summary>
/// Fold the outcome of a finished shaft back into the map.
///
/// Every input is an explicit parameter. An earlier version read the miner's
/// live shaft fields instead, which was fine on a solo miner and quietly wrong
/// on a dispatcher, where those fields describe nothing at all.
/// </summary>
void RecordShaftResult(int idx, ShaftResult result, double oreKg, double metres,
                       double depthReached, bool wasProbe)
{
    if (idx < 0 || idx >= cells.Length) return;
    YieldCell cell = cells[idx];

    cell.OreKg += (float)oreKg;
    cell.MetresDrilled += (float)metres;
    cell.DepthReached = Math.Max(cell.DepthReached, (float)depthReached);
    cell.LeasedBy = 0;
    cell.LeaseExpiresAt = 0;

    switch (result)
    {
        case ShaftResult.Completed:
            // A probe that hit ore is a lead, not a finished hole — leave it open
            // so phase 2 comes back and takes it to full depth.
            if (wasProbe)
                cell.State = cell.Yield >= barrenThreshold ? CellState.Rich : CellState.Barren;
            else
                cell.State = CellState.Exhausted;
            break;

        case ShaftResult.OreExhausted:
            cell.State = cell.Yield >= barrenThreshold ? CellState.Exhausted : CellState.Barren;
            break;

        case ShaftResult.CargoFull:
            // Unfinished. Deliberately left Available so we resume it later.
            cell.State = cell.Yield >= barrenThreshold ? CellState.Rich : CellState.Unknown;
            break;

        case ShaftResult.Stuck:
            cell.StuckCount++;
            cell.State = cell.StuckCount >= 3 ? CellState.Blocked : CellState.Unknown;
            break;

        case ShaftResult.Aborted:
            if (cell.State == CellState.Leased) cell.State = CellState.Unknown;
            break;
    }

    if (role == Role.Miner && dispatcherAddr != 0)
        SendShaftReport(idx, result, oreKg, metres, depthReached, wasProbe);
}

/// <summary>Flag the current cell as somewhere the ship keeps getting stuck.</summary>
void MarkCellStuck()
{
    if (activeCell < 0 || activeCell >= cells.Length) return;
    cells[activeCell].StuckCount++;
    if (cells[activeCell].StuckCount >= 3)
    {
        cells[activeCell].State = CellState.Blocked;
        Log("Cell " + CellCol(activeCell) + "," + CellRow(activeCell) + " blocked");
    }
}
