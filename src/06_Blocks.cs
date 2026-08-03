// ============================================================================
//  BLOCK DISCOVERY
//
//  Rescanned periodically rather than only at compile, so a repaired thruster or
//  a newly welded drill is picked up without operator action. The scan is the
//  most expensive thing this script does, which is why it is rate-limited.
// ============================================================================

void ScanBlocks()
{
    lastScanTick = tick;

    gyros.Clear(); thrusters.Clear(); drills.Clear(); cargo.Clear();
    batteries.Clear(); reactors.Clear(); hydrogenTanks.Clear(); ejectors.Clear();
    screens.Clear(); cameras.Clear(); oreDetectors.Clear();

    // ---- Controller ---------------------------------------------------------
    // A Remote Control is strongly preferred: it is the only controller with a
    // dependable forward axis when nobody is sitting in the ship.
    var controllers = new List<IMyShipController>();
    GridTerminalSystem.GetBlocksOfType(controllers, Mine);
    controller = null;
    for (int i = 0; i < controllers.Count; i++)
    {
        if (controllers[i] is IMyRemoteControl) { controller = controllers[i]; break; }
    }
    if (controller == null && controllers.Count > 0) controller = controllers[0];

    // ---- Movement -----------------------------------------------------------
    GridTerminalSystem.GetBlocksOfType(gyros, Mine);
    GridTerminalSystem.GetBlocksOfType(thrusters, Mine);

    // ---- Work ---------------------------------------------------------------
    GridTerminalSystem.GetBlocksOfType(drills, Mine);

    // ---- Storage and power --------------------------------------------------
    GridTerminalSystem.GetBlocksOfType(cargo, Mine);
    GridTerminalSystem.GetBlocksOfType(batteries, Mine);
    GridTerminalSystem.GetBlocksOfType(reactors, Mine);

    var tanks = new List<IMyGasTank>();
    GridTerminalSystem.GetBlocksOfType(tanks, Mine);
    for (int i = 0; i < tanks.Count; i++)
    {
        // Oxygen tanks share the interface. Only hydrogen matters for flight.
        if (tanks[i].BlockDefinition.SubtypeId.ToUpperInvariant().Contains("HYDROGEN"))
            hydrogenTanks.Add(tanks[i]);
    }

    // ---- Connectors ---------------------------------------------------------
    var connectors = new List<IMyShipConnector>();
    GridTerminalSystem.GetBlocksOfType(connectors, Mine);
    dockConnector = null;
    for (int i = 0; i < connectors.Count; i++)
    {
        IMyShipConnector c = connectors[i];
        if (c.ThrowOut) { ejectors.Add(c); continue; }
        // Whichever connector is actually latched is by definition the dock.
        if (c.Status == MyShipConnectorStatus.Connected) dockConnector = c;
        else if (dockConnector == null) dockConnector = c;
    }

    // ---- Sensing ------------------------------------------------------------
    GridTerminalSystem.GetBlocksOfType(cameras, Mine);
    GridTerminalSystem.GetBlocksOfType(oreDetectors, Mine);

    // ---- Displays -----------------------------------------------------------
    CollectScreens();

    // ---- Derived ------------------------------------------------------------
    BuildThrustModel();
    MeasureShip();

    // Cameras only build up scan range while raycasting is enabled, so arm them
    // now rather than at the moment we first want a reading.
    ArmCameras();
}

/// <summary>
/// Block filter. Two jobs: keep us on our own construct, and honour the tag.
///
/// IsSameConstructAs is the important part. Without it, a miner docked at a base
/// happily grabs the base's thrusters and batteries, then reports 4 million kg of
/// lift and flies itself into the ground. Subgrids joined by rotors and pistons
/// still count as ours, which is what we want for a drill arm.
/// </summary>
bool Mine(IMyTerminalBlock b)
{
    if (!b.IsSameConstructAs(Me)) return false;
    if (blockTag.Length > 0 && !b.CustomName.Contains(blockTag)) return false;
    return true;
}

void CollectScreens()
{
    panels.Clear();

    // Typed, so the predicate is not run against every block of whatever the ship
    // happens to be docked to. Cockpits and consoles are providers too, so this
    // is collected separately below rather than folded in.
    var surfaces = new List<IMyTextPanel>();
    GridTerminalSystem.GetBlocksOfType(surfaces, b => Mine(b) && b.CustomName.Contains(lcdTag));

    blockScratch.Clear();
    for (int i = 0; i < surfaces.Count; i++) blockScratch.Add(surfaces[i]);

    for (int i = 0; i < blockScratch.Count; i++)
    {
        // The programmable block's own screen is handled below and must not be
        // claimed here as well, or two renderers fight over one surface.
        if (blockScratch[i] == Me) continue;

        var provider = blockScratch[i] as IMyTextSurfaceProvider;
        if (provider == null || provider.SurfaceCount == 0) continue;

        // A plain LCD panel is also a provider with one surface, so this single
        // path covers panels, cockpits and consoles alike.
        IMyTextSurface s = provider.GetSurface(0);

        // SCRIPT mode hands the surface to us for sprite drawing. Clearing Script
        // is required — a built-in script selected in the terminal would
        // otherwise keep repainting over everything we draw.
        s.ContentType = ContentType.SCRIPT;
        s.Script = "";
        s.ScriptBackgroundColor = C_BG;
        panels.Add(s);
    }

    // The PB's own screen is too small for the dashboard, so it keeps the text
    // readout. It is also the one screen guaranteed to exist, which makes it the
    // right place for the detail a graphical panel leaves out.
    if (Me.SurfaceCount > 0)
    {
        IMyTextSurface own = Me.GetSurface(0);
        own.ContentType = ContentType.TEXT_AND_IMAGE;
        own.Font = "Monospace";
        own.FontSize = 0.5f;
        own.TextPadding = 2f;
        screens.Add(own);
    }
}

/// <summary>
/// Work out the ship's physical envelope and where the drill face is.
///
/// Everything downstream — shaft spacing, standoff distance, stuck margins —
/// is derived from these numbers rather than hardcoded, so the same script flies
/// a 3-block scout and a 200-block strip miner without retuning.
/// </summary>
void MeasureShip()
{
    isLargeGrid = Me.CubeGrid.GridSizeEnum == MyCubeSize.Large;
    double gridSize = isLargeGrid ? 2.5 : 0.5;

    Vector3I min = Me.CubeGrid.Min, max = Me.CubeGrid.Max;
    Vector3D extent = new Vector3D(max.X - min.X + 1, max.Y - min.Y + 1, max.Z - min.Z + 1) * gridSize;
    shipRadius = Math.Max(1.0, extent.Length() * 0.5);

    if (controller == null || drills.Count == 0)
    {
        // No drills is legitimate for a dispatcher or a pure scout.
        drillRadius = isLargeGrid ? 1.9 : 0.65;
        drillOffset = Vector3D.Zero;
        derivedSpacing = Math.Max(0.5, drillRadius * 2.0 * (1.0 - shaftOverlap));
        return;
    }

    // A single drill carves a roughly hemispherical pocket of this radius.
    // These are the vanilla numbers; they are not exposed by the API.
    double singleCut = isLargeGrid ? 1.9 : 0.65;

    MatrixD refInv = MatrixD.Transpose(controller.WorldMatrix.GetOrientation());
    Vector3D ctrlPos = controller.GetPosition();

    // Two passes. The centroid first, because the head's radius is its spread
    // about its own centre — measuring the spread from the controller instead
    // charges the head for however far off-axis it is mounted, and a drill arm
    // slung under the nose then reports a cutting radius metres too wide. The
    // shafts are spaced from that number, so the whole grid comes out coarse and
    // the ship leaves uncut rock between every hole.
    Vector3D offsetSum = Vector3D.Zero;
    for (int i = 0; i < drills.Count; i++)
        offsetSum += Vector3D.TransformNormal(drills[i].GetPosition() - ctrlPos, refInv);
    drillOffset = offsetSum / drills.Count;

    double maxLateral = 0;
    for (int i = 0; i < drills.Count; i++)
    {
        Vector3D local = Vector3D.TransformNormal(drills[i].GetPosition() - ctrlPos, refInv);
        double dx = local.X - drillOffset.X, dy = local.Y - drillOffset.Y;
        double lateral = Math.Sqrt(dx * dx + dy * dy);
        if (lateral > maxLateral) maxLateral = lateral;
    }

    // The cutting face is as wide as the drill cluster plus one drill's reach.
    drillRadius = maxLateral + singleCut;

    // Shaft pitch this hull would choose for itself. Overlap trades throughput
    // for how completely the rock clears.
    //
    // Deliberately NOT written straight into the job. Rescans happen every
    // minute, and a drone that recalculated its own spacing would drift away
    // from the dispatcher's grid — after which its cell [3,4] and everyone
    // else's are different holes, and drones start drilling into each other.
    // The job's spacing is set once, when the job is created or received.
    derivedSpacing = Math.Max(0.5, drillRadius * 2.0 * (1.0 - shaftOverlap));
}

// ---------------------------------------------------------------------------
//  HEALTH
// ---------------------------------------------------------------------------

/// <summary>
/// Is the ship physically capable of the job right now? Checked before launch
/// rather than discovered halfway down a shaft.
/// </summary>
Health CheckReadiness()
{
    if (controller == null) return Health.Bad("No Remote Control or cockpit found");
    if (gyros.Count == 0) return Health.Bad("No gyroscopes");
    if (thrusters.Count == 0) return Health.Bad("No thrusters");
    if (drills.Count == 0) return Health.Bad("No drills");
    if (dockConnector == null) return Health.Bad("No connector");
    if (!job.IsSet) return Health.Bad("No job set — use: job set <w> <h> <depth>");
    if (path.Count == 0 && !homeDockSet) return Health.Bad("No path recorded — use: record start/stop");

    // Functional AND enabled. Checking only IsFunctional passes a ship whose
    // thrusters are all switched off, which then reports zero thrust capacity
    // and sits there — or falls — until the watchdog eventually notices.
    int liveThrust = 0, offThrust = 0;
    for (int i = 0; i < thrusters.Count; i++)
    {
        if (!thrusters[i].IsFunctional) continue;
        if (thrusters[i].Enabled) liveThrust++; else offThrust++;
    }
    if (liveThrust == 0 && offThrust > 0) return Health.Bad("All thrusters switched off");
    if (liveThrust == 0) return Health.Bad("All thrusters damaged");

    int liveGyros = 0;
    for (int i = 0; i < gyros.Count; i++) if (gyros[i].IsFunctional) liveGyros++;
    if (liveGyros == 0) return Health.Bad("All gyroscopes damaged");

    // Lift check. Refusing to launch beats discovering mid-climb that a full
    // load plus this gravity exceeds what the thrusters can hold up.
    if (maxFlyableMass > 0 && shipMass > maxFlyableMass)
        return Health.Bad("Too heavy for the route: " + Fmt(shipMass / 1000.0, 1) + "t of "
                          + Fmt(maxFlyableMass / 1000.0, 1) + "t");

    return Health.Good();
}

/// <summary>Count of blocks that have taken damage. Used by the stopOnDamage option.</summary>
int DamagedBlockCount()
{
    int n = 0;
    for (int i = 0; i < thrusters.Count; i++) if (!thrusters[i].IsFunctional) n++;
    for (int i = 0; i < gyros.Count; i++) if (!gyros[i].IsFunctional) n++;
    for (int i = 0; i < drills.Count; i++) if (!drills[i].IsFunctional) n++;
    return n;
}
