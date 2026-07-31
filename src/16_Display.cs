// ============================================================================
//  DISPLAY
//
//  The yield map is the point of this screen. A wall of numbers tells you the
//  script is running; a map of the site tells you at a glance that the ore is
//  all in the north-east corner and your job rectangle is in the wrong place.
// ============================================================================

/// <summary>
/// Redraw the screens. Rate-limited to about twice a second: rebuilding this
/// string six times a second is a measurable share of the instruction budget
/// and nobody can read it that fast anyway. Pass true to force a redraw after
/// a command, so the operator sees the effect immediately.
/// </summary>
void Render(bool force = false)
{
    // Echo every tick from the cached text. Skipping Echo entirely would blank
    // the block's detail panel between redraws, which reads as "the script has
    // died" to anyone glancing at it.
    if (!force && tick % 3 != 0)
    {
        if (verboseEcho) Echo(lastRender);
        return;
    }

    sb.Clear();

    sb.Append("VEIN ").Append(VEIN_VERSION).Append("  ").Append(role.ToString());
    if (role == Role.Miner) sb.Append(HasDispatcher ? "  [fleet]" : "  [solo]");
    sb.Append('\n');
    sb.Append("----------------------------------------\n");

    if (configError.Length > 0)
        sb.Append("! ").Append(configError).Append('\n');

    if (state == MinerState.Fault)
    {
        sb.Append("\n*** FAULT ***\n").Append(faultReason).Append("\n\n");
        sb.Append("Run 'clear' once the cause is fixed.\n");
    }

    if (role == Role.Miner) RenderMiner();
    else RenderDispatcher();

    RenderMap();
    RenderLog();

    lastRender = sb.ToString();

    for (int i = 0; i < screens.Count; i++)
    {
        try { screens[i].WriteText(lastRender); }
        catch { /* a screen destroyed this tick must not take the script down */ }
    }

    // Sprites are redrawn far less often than the text. A dashboard costs one
    // sprite per map cell, which on a small-grid job is several hundred per
    // frame, and nobody reads a gauge at 2 Hz. Every 12 ticks is about half a
    // second and is indistinguishable to the eye.
    if (force || tick % 12 == 0) RenderSprites();

    if (verboseEcho) Echo(lastRender);
}

void RenderMiner()
{
    sb.Append(state.ToString()).Append(" — ").Append(statusLine).Append('\n');

    if (recording)
        sb.Append("RECORDING  ").Append(path.Count).Append(" points\n");

    sb.Append("Cargo  ").Append(Bar(cargoFill, 12)).Append(' ')
      .Append(Fmt(cargoFill * 100, 0)).Append("%  ").Append(FmtMass(oreAboard)).Append(" ore\n");

    sb.Append("Power  ").Append(Bar(batteryFill, 12)).Append(' ')
      .Append(Fmt(batteryFill * 100, 0)).Append("%\n");

    if (hydrogenTanks.Count > 0)
        sb.Append("H2     ").Append(Bar(hydrogenFill, 12)).Append(' ')
          .Append(Fmt(hydrogenFill * 100, 0)).Append("%\n");

    if (activeCell >= 0)
    {
        sb.Append("Shaft  ").Append(CellLabel(activeCell));
        if (shaftIsProbe) sb.Append(" probe");
        sb.Append("  ").Append(Fmt(shaftDepth, 1)).Append('/')
          .Append(Fmt(shaftDepthLimit, 0)).Append("m\n");
    }

    if (job.IsSet)
    {
        sb.Append("Job    ").Append(job.Width).Append('x').Append(job.Height)
          .Append(" @").Append(job.Depth).Append("m  ")
          .Append(Fmt(JobProgress() * 100, 0)).Append("% done, ")
          .Append(RemainingCellCount()).Append(" left\n");
    }

    // The lift limit is the number that stops a loaded miner stranding itself in
    // gravity, so it gets its own line whenever it is actually binding.
    if (maxFlyableMass > 0)
    {
        double margin = RemainingLiftMargin();
        sb.Append("Lift   ").Append(FmtMass(shipMass)).Append(" of ")
          .Append(FmtMass(maxFlyableMass));
        if (margin < 0) sb.Append("  OVER");
        sb.Append('\n');
    }

    if (reactors.Count > 0)
        sb.Append("Uranium ").Append(Fmt(uraniumKg, 1)).Append("kg\n");

    if (hydroCalibrated)
    {
        sb.Append("Fuel   burn ").Append(Fmt(hydroPerMetre * 100000, 2))
          .Append("%/km, return needs ").Append(Fmt(FuelToGetHome() * 100, 0)).Append("%\n");
    }

    sb.Append("Scout  ").Append(ScoutStatus()).Append('\n');
    sb.Append("Tune   ").Append(AdaptiveStatus()).Append('\n');

    if (flightActive)
        sb.Append("Nav    ").Append(Fmt(distToTarget, 1)).Append("m  ")
          .Append(Fmt(speed, 1)).Append("m/s  err ")
          .Append(Fmt(alignError, 0)).Append("deg\n");

    sb.Append("Load   ").Append(Fmt(loadPeak * 100, 0)).Append("% of tick budget\n");
}

void RenderDispatcher()
{
    sb.Append(statusLine).Append("  ").Append(fleet.Count).Append(" drone(s), ")
      .Append(ActiveDroneCount()).Append(" working\n");

    if (job.IsSet)
    {
        sb.Append("Job    ").Append(job.Width).Append('x').Append(job.Height)
          .Append(" @").Append(job.Depth).Append("m  ")
          .Append(Fmt(JobProgress() * 100, 0)).Append("% done\n");
        sb.Append("Yield  ").Append(FmtMass(FleetOreTotal())).Append(" from ")
          .Append(Fmt(FleetMetresTotal(), 0)).Append("m drilled\n");
    }
    else sb.Append("No job set.\n");

    sb.Append("Scout  ").Append(ScoutStatus()).Append('\n');
    sb.Append('\n');

    foreach (var kv in fleet)
    {
        DroneRecord r = kv.Value;
        sb.Append(Pad(r.Name, 12)).Append(' ')
          .Append(Pad(r.State.ToString(), 11)).Append(' ')
          .Append(Pad(Fmt(r.CargoFill * 100, 0) + "%", 5))
          .Append(Pad(Fmt(r.Battery * 100, 0) + "%", 5));
        if (r.LeasedCell >= 0) sb.Append(CellLabel(r.LeasedCell));
        sb.Append('\n');
    }
}

/// <summary>
/// The site map. Each character is one shaft position.
///   .  untouched      o  being dug now
///   #  ore found      -  probed, empty
///   X  worked out     !  ship kept getting stuck
/// </summary>
void RenderMap()
{
    if (!job.IsSet || cells.Length == 0) return;
    sb.Append('\n');

    // Beyond this the map stops being readable on a normal LCD anyway.
    if (job.Width > 64 || job.Height > 40)
    {
        sb.Append("Map too large to draw (").Append(job.Width).Append('x')
          .Append(job.Height).Append(")\n");
        return;
    }

    for (int row = 0; row < job.Height; row++)
    {
        for (int col = 0; col < job.Width; col++)
        {
            int idx = job.IndexOf(col, row);
            sb.Append(idx == activeCell ? '@' : CellGlyph(cells[idx]));
        }
        sb.Append('\n');
    }

    sb.Append(". new  # ore  - dry  X done  ! stuck  o busy\n");
}

static char CellGlyph(YieldCell c)
{
    switch (c.State)
    {
        case CellState.Rich:      return '#';
        case CellState.Barren:    return '-';
        case CellState.Exhausted: return 'X';
        case CellState.Blocked:   return '!';
        case CellState.Leased:    return 'o';
        default:                  return '.';
    }
}

void RenderLog()
{
    if (log.Count == 0) return;
    sb.Append('\n');
    // Newest last, so the eye lands on the most recent line at the bottom.
    int from = Math.Max(0, log.Count - 6);
    for (int i = from; i < log.Count; i++) sb.Append(log[i]).Append('\n');
}

static string Pad(string s, int width)
{
    if (s == null) s = "";
    if (s.Length >= width) return s.Substring(0, width);
    return s + new string(' ', width - s.Length);
}
