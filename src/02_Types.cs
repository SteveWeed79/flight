// ============================================================================
//  DATA TYPES
// ============================================================================

/// <summary>
/// One recorded point on the dock&lt;-&gt;job route.
///
/// We store more than just a position. Gravity at the waypoint tells us whether
/// this leg is atmospheric or orbital, and the per-thruster-type efficiency tells
/// us how much lift we will actually have when we come back through here heavy.
/// PAM pioneered this; it is the single reason a loaded miner does not belly-flop
/// into a mountain on the way home.
/// </summary>
public class Waypoint
{
    public Vector3D Position;
    /// <summary>Natural gravity vector here. Zero means space.</summary>
    public Vector3D Gravity;
    /// <summary>
    /// Effectiveness of each thruster type at this altitude, indexed by
    /// <see cref="Program.thrusterTypes"/>. Atmospheric thrusters read ~0 in
    /// orbit; ion thrusters read ~0.3 at sea level. Diagnostic only.
    /// </summary>
    public float[] ThrusterEfficiency;

    /// <summary>
    /// Newtons of thrust available straight up against gravity, measured here.
    /// Mass-independent, so it stays valid when we come back through loaded.
    /// This is what caps how much ore we are willing to carry home.
    /// </summary>
    public float Lift;

    public Waypoint() { }

    public Waypoint(Vector3D pos, Vector3D grav, float[] eff, float lift)
    {
        Position = pos;
        Gravity = grav;
        ThrusterEfficiency = eff;
        Lift = lift;
    }

    /// <summary>True if this waypoint sits inside a planet's gravity well.</summary>
    public bool InGravity { get { return Gravity.LengthSquared() > 0.0001; } }
}

/// <summary>
/// The mining site: an oriented plane in world space plus a shaft grid on it.
///
/// Origin is the corner-or-centre reference point. Right/Forward span the
/// surface; Down is the drilling axis. These are captured from the ship's own
/// orientation when you set the job, so "down" means whatever direction your
/// drills were pointing — works on a planet surface and on an asteroid face.
/// </summary>
public class Job
{
    public bool IsSet;
    public Vector3D Origin;
    public Vector3D Right;
    public Vector3D Forward;
    public Vector3D Down;

    /// <summary>Shaft grid size.</summary>
    public int Width = 5;
    public int Height = 5;
    /// <summary>Metres. The depth cap for a single shaft.</summary>
    public int Depth = 40;
    /// <summary>Centre-to-centre shaft spacing, metres. Derived from drill radius.</summary>
    public double Spacing = 2.4;

    public Job() { }

    /// <summary>World position of the mouth of shaft (col,row), lifted by <paramref name="standoff"/> metres.</summary>
    public Vector3D CellMouth(int col, int row, double standoff)
    {
        double cx = (col - (Width - 1) * 0.5) * Spacing;
        double cy = (row - (Height - 1) * 0.5) * Spacing;
        return Origin + Right * cx + Forward * cy - Down * standoff;
    }

    /// <summary>World position <paramref name="depth"/> metres down shaft (col,row).</summary>
    public Vector3D CellDepth(int col, int row, double depth)
    {
        return CellMouth(col, row, 0) + Down * depth;
    }

    public int CellCount { get { return Width * Height; } }

    public int IndexOf(int col, int row) { return row * Width + col; }
}

/// <summary>
/// One square of the yield map. This is VEIN's memory of what the rock is like.
///
/// Both PAM and SCAM throw this away between shafts. Keeping it is what lets us
/// prospect instead of blindly grinding out a rectangle.
/// </summary>
public class YieldCell
{
    public CellState State = CellState.Unknown;
    /// <summary>Total kg of *valuable* ore recovered here (stone excluded).</summary>
    public float OreKg;
    /// <summary>Total metres drilled here.</summary>
    public float MetresDrilled;
    /// <summary>Deepest we have got, metres. Lets us resume a half-dug shaft.</summary>
    public float DepthReached;
    /// <summary>Which drone holds this cell, 0 = nobody.</summary>
    public long LeasedBy;
    /// <summary>
    /// Which *grant* the holder is working under, 0 = none. Monotonic per
    /// dispatcher, so two successive leases on the same cell to the same drone
    /// are still distinguishable — an address alone cannot tell them apart, and
    /// that is exactly the case a lease that expired and was reissued produces.
    /// </summary>
    public long LeaseId;
    /// <summary>Clock reading, in seconds, at which an unrenewed lease expires.
    /// Zero means no lease. Absolute, so a lag spike cannot move it.</summary>
    public double LeaseExpiresAt;
    /// <summary>How many times a ship got stuck here. 3 strikes and it's Blocked.</summary>
    public int StuckCount;

    /// <summary>kg of ore per metre drilled. The number the whole prospector runs on.</summary>
    public float Yield
    {
        get { return MetresDrilled > 0.5f ? OreKg / MetresDrilled : 0f; }
    }

    /// <summary>True if this cell can be handed out as work right now.</summary>
    public bool Available
    {
        get
        {
            return State != CellState.Exhausted
                && State != CellState.Blocked
                && State != CellState.Leased;
        }
    }
}

/// <summary>Dispatcher's record of one drone in the fleet.</summary>
public class DroneRecord
{
    public long Address;
    public string Name = "?";
    public MinerState State = MinerState.Idle;
    public float CargoFill;
    public float Battery;
    public Vector3D Position;
    /// <summary>Clock reading of the last message from it, in seconds. Silence
    /// past a timeout = presumed dead.</summary>
    public double LastSeenAt;
    /// <summary>Cell it currently holds, -1 if none.</summary>
    public int LeasedCell = -1;
    /// <summary>Dock slot it holds, -1 if none.</summary>
    public int DockSlot = -1;
    /// <summary>
    /// Altitude lane, metres above the job plane, assigned per drone so two
    /// drones crossing the site never share a height band. Cheap, effective
    /// separation — this is the generalisation of SCAM's blocked path segments.
    /// </summary>
    public double Lane;
}

/// <summary>An ore position we know about, from the mod bridge or from digging.</summary>
public class OreSighting
{
    public string OreType = "";
    public Vector3D Position;
    public long Tick;
}

/// <summary>Health snapshot of a subsystem, used by the fault handler.</summary>
public struct Health
{
    public bool Ok;
    public string Detail;

    public static Health Good() { Health h; h.Ok = true; h.Detail = ""; return h; }
    public static Health Bad(string why) { Health h; h.Ok = false; h.Detail = why; return h; }
}
