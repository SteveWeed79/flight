// ============================================================================
//  OPERATOR COMMANDS
//
//  Run these as the Programmable Block's argument, from a button, a timer, or
//  the terminal. Everything is lower-cased and whitespace-tolerant, because
//  typing "Job Set 5 5 40" into a button panel at 3am should still work.
// ============================================================================

void HandleCommand(string argument, bool allowRelay = true)
{
    if (string.IsNullOrWhiteSpace(argument)) return;

    string[] a = argument.Trim().ToLowerInvariant().Split(new char[] { ' ' },
        StringSplitOptions.RemoveEmptyEntries);
    if (a.Length == 0) return;

    switch (a[0])
    {
        // ---- Lifecycle ----------------------------------------------------
        case "start":
        case "run":
            CmdStart();
            break;

        case "stop":
        case "pause":
            jobRunning = false;
            Log("Stopped by operator");
            // Finish climbing out of the shaft first if there is one. Going
            // straight home from the bottom of a hole means flying sideways
            // through the wall.
            ReturnToDock(ShaftResult.Aborted);
            break;

        case "halt":
            // Immediate, in place. For when something is going wrong right now,
            // so it deliberately does not fly anywhere — which means it cannot
            // climb out, and the cell has to be given back here instead.
            jobRunning = false;
            ReleaseLease(ShaftResult.Aborted);
            SafeStop();
            SetState(MinerState.Idle);
            Log("Halted in place");
            break;

        case "home":
            jobRunning = false;
            ReturnToDock(ShaftResult.Aborted);
            break;

        case "clear":
            if (state == MinerState.Fault) ClearFault();
            else Log("Nothing to clear");
            break;

        case "reset":
            CmdReset();
            break;

        case "reload":
            LoadConfig();
            ScanBlocks();
            Log("Config and blocks reloaded");
            break;

        // ---- Path ----------------------------------------------------------
        case "record":
            CmdRecord(a);
            break;

        // ---- Job -----------------------------------------------------------
        case "job":
            CmdJob(a);
            break;

        // ---- Settings ------------------------------------------------------
        case "mode":
            if (a.Length > 1) { depthMode = ParseDepthMode(a[1]); WriteConfig(); Log("Depth mode: " + depthMode); }
            break;

        case "order":
            if (a.Length > 1) { holeOrder = ParseHoleOrder(a[1]); WriteConfig(); Log("Hole order: " + holeOrder); }
            break;

        case "eject":
            if (a.Length > 1) { ejectMode = ParseEjectMode(a[1]); WriteConfig(); Log("Eject: " + ejectMode); }
            break;

        // ---- Fleet ---------------------------------------------------------
        case "fleet":
            // Relay the rest of the line to every drone on the channel.
            if (a.Length > 1 && allowRelay)
            {
                string rest = string.Join(" ", a, 1, a.Length - 1);
                IGC.SendBroadcastMessage(igcChannel, "C|" + rest);
                Log("Relayed to fleet: " + rest);
            }
            break;

        case "purge":
            // The escape hatch, and SCAM is right to ship one. Expiry should
            // make it unnecessary; "should" is not a thing to rely on at 2am.
            PurgeAirspace();
            break;

        // ---- Settings the ship worked out for itself -----------------------
        case "learn":
            if (a.Length > 1 && a[1] == "reset") ResetLearning();
            else Log("Learned: " + LearningSummary());
            break;

        // ---- Diagnostics ---------------------------------------------------
        case "status":
            Log(state + " / " + statusLine);
            break;

        case "scan":
            oreModProbed = false;      // force a fresh mod check
            Log("Rescanning for ore detector mod");
            break;

        default:
            Log("Unknown command: " + a[0]);
            break;
    }
}

void CmdStart()
{
    if (state == MinerState.Fault)
    {
        Log("Cannot start: clear the fault first");
        return;
    }

    if (role == Role.Dispatcher)
    {
        if (!job.IsSet) { Log("Set a job first: job set <w> <h> <depth>"); return; }
        jobRunning = true;
        jobComplete = false;
        SendBeacon();
        Log("Dispatching to fleet");
        return;
    }

    // Servicing switches the thrusters off to charge faster, and a 'halt' from
    // there leaves them off. CheckReadiness then refuses to start with "All
    // thrusters switched off" and the only way out is the terminal — a dead end
    // reached by typing two documented commands in order. Costs nothing on a
    // ship that already had them on, because SetThrusters only writes on change.
    SetThrusters(true);

    Health h = CheckReadiness();
    if (!h.Ok) { Log("Cannot start: " + h.Detail); return; }

    jobRunning = true;
    jobComplete = false;
    Log("Started");
    if (state == MinerState.Idle) SetState(Docked ? MinerState.Undocking : MinerState.Outbound);
}

void CmdReset()
{
    // Wipe the survey but keep the job frame and the recorded path — those are
    // the expensive things to set up and are almost never what you want to lose.
    RebuildCells();
    sightings.Clear();
    activeCell = -1;
    jobComplete = false;
    probePassDone = false;
    stuckRetries = 0;
    Log("Yield map cleared");
}

void CmdRecord(string[] a)
{
    if (a.Length < 2) { Log("record start | stop | clear"); return; }

    switch (a[1])
    {
        case "start":
            StartRecording();
            break;
        case "stop":
        case "end":
            StopRecording();
            break;
        case "clear":
            path.Clear();
            recording = false;
            homeDockSet = false;
            maxFlyableMass = -1;
            Log("Path cleared");
            break;
        default:
            Log("record start | stop | clear");
            break;
    }
}

void CmdJob(string[] a)
{
    if (a.Length < 2)
    {
        Log("job set <w> <h> <depth> | job depth <m> | job size <w> <h> | job here | job push");
        return;
    }

    // Anchoring uses the ship's live position and attitude. Doing that while the
    // ship is nose-down inside a shaft would put the job plane underground and
    // silently invalidate the whole survey, so it is refused unless parked.
    if ((a[1] == "set" || a[1] == "here") && role == Role.Miner
        && state != MinerState.Idle && state != MinerState.Fault && !Docked)
    {
        Log("Cannot anchor a job while flying — run 'stop' or 'halt' first");
        return;
    }

    switch (a[1])
    {
        case "set":
            if (a.Length < 5) { Log("job set <width> <height> <depth>"); return; }
            SetJob(ParseInt(a[2], 5), ParseInt(a[3], 5), ParseInt(a[4], 40));
            if (role == Role.Dispatcher) SendBeacon();
            break;

        case "here":
            // Re-anchor the existing grid at the current position and attitude.
            SetJob(job.Width, job.Height, job.Depth);
            if (role == Role.Dispatcher) SendBeacon();
            break;

        case "size":
            if (a.Length < 4) { Log("job size <width> <height>"); return; }
            job.Width = Math.Max(1, ParseInt(a[2], job.Width));
            job.Height = Math.Max(1, ParseInt(a[3], job.Height));
            RebuildCells();
            probePassDone = false;
            Log("Job resized to " + job.Width + "x" + job.Height);
            if (role == Role.Dispatcher) SendBeacon();
            break;

        case "depth":
            if (a.Length < 3) { Log("job depth <metres>"); return; }
            job.Depth = Math.Max(1, ParseInt(a[2], job.Depth));
            Log("Job depth " + job.Depth + "m");
            if (role == Role.Dispatcher) SendBeacon();
            break;

        case "push":
            // How a base-mounted dispatcher gets a frame it cannot anchor itself.
            if (role != Role.Miner) { Log("Only a miner can push a job"); return; }
            if (!job.IsSet) { Log("Nothing to push — set a job here first"); return; }
            if (dispatcherAddr == 0) { Log("No dispatcher on channel '" + igcChannel + "'"); return; }
            SendJobPush();
            Log("Job pushed to the dispatcher");
            break;

        default:
            Log("job set | here | size | depth | push");
            break;
    }
}
