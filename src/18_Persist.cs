// ============================================================================
//  PERSISTENCE
//
//  Survives recompiles, world reloads and server restarts. Without this, every
//  recompile throws away the recorded path and the entire survey — which is
//  exactly the moment you are most likely to recompile, because you were
//  fiddling with the config.
//
//  Only cells that actually carry information are written. A 40x40 job is 1600
//  cells but typically fewer than 200 have been touched.
// ============================================================================

string SerializeState()
{
    var b = new StringBuilder(2048);

    b.Append("V|").Append(STORAGE_REV).Append('\n');

    b.Append("S|").Append((int)state)
     .Append('|').Append(jobRunning ? 1 : 0)
     .Append('|').Append(jobComplete ? 1 : 0)
     .Append('|').Append(activeCell)
     .Append('|').Append(probePassDone ? 1 : 0)
     .Append('\n');

    // What the ship has measured about itself. These are properties of the hull
    // and the server, not of the job, so they are worth more than the survey and
    // cost six bytes: rediscovering them means another hour of digging slowly.
    b.Append("A|").Append(EncD(learnedDrillSpeed))
     .Append('|').Append(EncD(brakeDerate))
     .Append('|').Append(brakeSamples)
     // The burn rate, stored per KILOMETRE. EncD scales by 1000 and truncates to
     // a long, and a rate is order 1e-5 of a tank per metre — per metre it would
     // save as a flat zero every time. Zero doubles as "not yet calibrated",
     // which it cannot be confused with: a measured rate is always positive.
     .Append('|').Append(EncD(hydroCalibrated ? hydroPerMetre * 1000.0 : 0))
     .Append('\n');

    if (job.IsSet)
    {
        // Same eight fields, same order, same encoder as the IGC beacon. One
        // layout with two writers is a layout that drifts.
        b.Append("J").Append(JobFrameFields()).Append('\n');
    }

    // ---- Yield map ---------------------------------------------------------
    b.Append("C");
    for (int i = 0; i < cells.Length; i++)
    {
        YieldCell c = cells[i];
        // Nothing learned about this cell, nothing to save.
        if (c.State == CellState.Unknown && c.MetresDrilled < 0.5f && c.StuckCount == 0) continue;

        // A lease does not survive a restart — the ship is not where it was.
        int st = (int)(c.State == CellState.Leased ? CellState.Unknown : c.State);

        b.Append('|').Append(i)
         .Append(':').Append(st)
         .Append(':').Append(EncD(c.OreKg))
         .Append(':').Append(EncD(c.MetresDrilled))
         .Append(':').Append(EncD(c.DepthReached))
         .Append(':').Append(c.StuckCount);
    }
    b.Append('\n');

    // ---- Path --------------------------------------------------------------
    for (int i = 0; i < path.Count; i++)
    {
        Waypoint w = path[i];
        b.Append("P|").Append(EncV(w.Position))
         .Append('|').Append(EncV(w.Gravity))
         .Append('|').Append(EncD(w.Lift))
         .Append('\n');
    }

    if (homeDockSet && homeDock != null)
    {
        b.Append("D|").Append(EncV(homeDock.Position))
         .Append('|').Append(EncV(homeDockForward))
         .Append('|').Append(EncV(homeDockUp))
         .Append('|').Append(EncV(homeDock.Gravity))
         .Append('|').Append(EncD(homeDock.Lift))
         .Append('\n');
    }

    return b.ToString();
}

void LoadState()
{
    if (string.IsNullOrEmpty(Storage)) return;

    try
    {
        string[] lines = Storage.Split('\n');
        bool versionOk = false;

        path.Clear();

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];
            if (line.Length < 2) continue;

            string[] f = line.Split('|');

            switch (f[0])
            {
                case "V":
                    versionOk = f.Length > 1 && f[1] == STORAGE_REV;
                    // A format change means the saved data cannot be trusted.
                    // Starting clean beats loading garbage into a flight controller.
                    if (!versionOk) { Log("Saved state is from an older version — starting fresh"); return; }
                    break;

                case "S":
                    if (!versionOk || f.Length < 6) break;
                    LoadLifecycle(f);
                    break;

                case "A":
                    if (!versionOk || f.Length < 5) break;
                    LoadLearned(f);
                    break;

                case "J":
                    if (!versionOk || f.Length < 9) break;
                    LoadJob(f);
                    break;

                case "C":
                    if (!versionOk) break;
                    LoadCells(f);
                    break;

                case "P":
                    if (!versionOk || f.Length < 4) break;
                    path.Add(new Waypoint(DecV(f[1]), DecV(f[2]), new float[0], (float)DecD(f[3])));
                    break;

                case "D":
                    if (!versionOk || f.Length < 6) break;
                    homeDock = new Waypoint(DecV(f[1]), DecV(f[4]), new float[0], (float)DecD(f[5]));
                    homeDockForward = DecV(f[2]);
                    homeDockUp = DecV(f[3]);
                    homeDockSet = true;
                    break;
            }
        }

        BuildPathDistances();
        ComputeMaxFlyableMass();

        if (path.Count > 0 || job.IsSet)
            Log("Restored: " + path.Count + " waypoints, " + ProbedCellCount() + " surveyed cells");
    }
    catch (Exception e)
    {
        Log("Could not restore state: " + e.Message);
        // Deliberately not a fault. Losing the survey is annoying; refusing to
        // boot because of it is worse.
    }
}

void LoadLifecycle(string[] f)
{
    MinerState saved = (MinerState)ParseInt(f[1], 0);
    jobRunning = f[2] == "1";
    jobComplete = f[3] == "1";
    activeCell = ParseInt(f[4], -1);
    probePassDone = f[5] == "1";

    // Never resume mid-shaft or mid-manoeuvre. The world has moved on: the ship
    // may have been dragged, the voxels may have been changed by someone else,
    // and the control loop has no history. Coming up in Idle and letting the
    // operator restart is the honest behaviour. A fault is preserved, because
    // whatever caused it probably has not fixed itself.
    state = saved == MinerState.Fault ? MinerState.Fault : MinerState.Idle;
    stateEntry = true;

    // Idling is not enough on its own. StIdle launches the moment it sees
    // jobRunning, so restoring that flag as true means the ship undocks by
    // itself on the first tick after a recompile or a server restart — the
    // exact opposite of what the paragraph above promises, and it does it while
    // the operator is still reading the config they just changed. The run flag
    // is the operator's, and it does not survive a reload.
    if (saved != MinerState.Idle && saved != MinerState.Fault)
    {
        jobRunning = false;
        Log("Resumed from " + saved + " — idling, run 'start' to continue");
    }
}

void LoadLearned(string[] f)
{
    double cut = DecD(f[1]);
    double derate = DecD(f[2]);

    // Clamped against the *current* config, not the one that produced them. An
    // operator who has since halved drillSpeed has said something about this
    // ship, and a value learned under the old setting must not override it.
    if (cut > 0) learnedDrillSpeed = Clamp(cut, DrillSpeedFloor, DrillSpeedCap);
    if (derate > 0) brakeDerate = Clamp(derate, 0.30, 0.85);
    brakeSamples = Math.Max(0, ParseInt(f[3], 0));

    // Back to per metre. Worth keeping across a recompile: it takes several
    // 150-metre legs to measure, and until it is measured FuelToGetHome returns
    // zero and the ship flies home with no fuel reserve check at all.
    double perKm = DecD(f[4]);
    if (perKm > 0) { hydroPerMetre = perKm / 1000.0; hydroCalibrated = true; }
}

void LoadJob(string[] f)
{
    // The shared decoder, at the same offset a job push uses. It validates the
    // basis and rebuilds the cells for us: job.IsSet is false on a cold load, so
    // its "reshaped" branch always fires — which the C record parsed after this
    // one depends on, since it indexes into that array.
    AdoptJobFrame(f, 1);
}

void LoadCells(string[] f)
{
    if (cells.Length == 0) return;

    for (int i = 1; i < f.Length; i++)
    {
        string[] p = f[i].Split(':');
        if (p.Length < 6) continue;

        int idx = ParseInt(p[0], -1);
        if (idx < 0 || idx >= cells.Length) continue;

        YieldCell c = cells[idx];
        c.State = (CellState)ParseInt(p[1], 0);
        c.OreKg = (float)DecD(p[2]);
        c.MetresDrilled = (float)DecD(p[3]);
        c.DepthReached = (float)DecD(p[4]);
        c.StuckCount = ParseInt(p[5], 0);
    }
}
