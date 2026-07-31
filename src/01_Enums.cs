// ============================================================================
//  ENUMS
// ============================================================================

/// <summary>What this Programmable Block is for.</summary>
public enum Role
{
    /// <summary>A mining ship. Works alone, or joins a fleet automatically.</summary>
    Miner,
    /// <summary>A stationary base brain. Owns the job, hands out work, keeps the map.</summary>
    Dispatcher
}

/// <summary>Miner top-level lifecycle. Orthogonal to <see cref="NavState"/>.</summary>
public enum MinerState
{
    /// <summary>Parked. Not mining, not moving. Safe.</summary>
    Idle,
    /// <summary>Leaving the dock connector.</summary>
    Undocking,
    /// <summary>Flying the recorded path outbound, dock -> job.</summary>
    Outbound,
    /// <summary>At the job plane, choosing which shaft to dig next.</summary>
    Selecting,
    /// <summary>Moving to the mouth of the chosen shaft.</summary>
    Approaching,
    /// <summary>Drilling downward.</summary>
    Descending,
    /// <summary>Backing out of the shaft.</summary>
    Ascending,
    /// <summary>Flying the recorded path inbound, job -> dock.</summary>
    Inbound,
    /// <summary>Lining up on the dock connector.</summary>
    Docking,
    /// <summary>Connected. Pushing cargo into base.</summary>
    Unloading,
    /// <summary>Connected. Waiting on power / H2 / uranium.</summary>
    Servicing,
    /// <summary>Something is wrong. Everything is off and safe. Needs a human.</summary>
    Fault
}

/// <summary>How deep a shaft goes.</summary>
public enum DepthMode
{
    /// <summary>Always drill to the configured depth. Predictable, dumb.</summary>
    Fixed,
    /// <summary>Stop when ore stops arriving. Never wastes time in dead rock.</summary>
    AutoOre,
    /// <summary>Stop when *nothing at all* arrives (i.e. you broke through). Good for tunnels.</summary>
    AutoVoid
}

/// <summary>Order in which shafts get dug.</summary>
public enum HoleOrder
{
    /// <summary>Boustrophedon from a corner. Shortest total travel.</summary>
    Serpentine,
    /// <summary>Outward spiral from the centre. Best when the deposit is centred.</summary>
    Spiral,
    /// <summary>Yield-guided. Probe wide, then chase the richest neighbours. Default.</summary>
    Prospect
}

/// <summary>What to do with worthless mass.</summary>
public enum EjectMode
{
    /// <summary>Keep everything. You will fill up fast.</summary>
    Off,
    /// <summary>Throw away stone only.</summary>
    Stone,
    /// <summary>Throw away stone and ice.</summary>
    StoneAndIce
}

/// <summary>Confidence level for a cell on the yield map.</summary>
public enum CellState
{
    /// <summary>Never touched.</summary>
    Unknown,
    /// <summary>Reserved by a drone right now. Hands off.</summary>
    Leased,
    /// <summary>Probed or dug, produced ore.</summary>
    Rich,
    /// <summary>Probed or dug, produced nothing worth having.</summary>
    Barren,
    /// <summary>Dug to completion. Nothing left.</summary>
    Exhausted,
    /// <summary>Physically unreachable — ship kept getting stuck.</summary>
    Blocked
}

/// <summary>Why a shaft ended. Fed back into the yield map.</summary>
public enum ShaftResult
{
    /// <summary>Reached target depth normally.</summary>
    Completed,
    /// <summary>Ore ran out, stopped early. Still a good hole.</summary>
    OreExhausted,
    /// <summary>Cargo filled before depth. Resume later.</summary>
    CargoFull,
    /// <summary>Ship got stuck and gave up.</summary>
    Stuck,
    /// <summary>Power/fuel forced an abort.</summary>
    Aborted
}

/// <summary>IGC message kinds. Kept short — these go on the wire every tick.</summary>
public enum MsgType
{
    /// <summary>Dispatcher -> all. "I exist, here is the job."</summary>
    Beacon,
    /// <summary>Miner -> dispatcher. "I'm alive, here's my state."</summary>
    Heartbeat,
    /// <summary>Miner -> dispatcher. "Give me a shaft."</summary>
    LeaseRequest,
    /// <summary>Dispatcher -> miner. "Dig cell (c,r), max depth d."</summary>
    LeaseGrant,
    /// <summary>Dispatcher -> miner. "No work available."</summary>
    LeaseDenied,
    /// <summary>Miner -> dispatcher. "Done with (c,r), got X kg in Y m."</summary>
    ShaftReport,
    /// <summary>Miner -> dispatcher. "I need a dock slot."</summary>
    DockRequest,
    /// <summary>Dispatcher -> miner. "Use connector N" / "wait".</summary>
    DockGrant,
    /// <summary>Miner -> dispatcher. "Released dock slot N."</summary>
    DockRelease,
    /// <summary>Any -> all. Ore found at a world position (scout result).</summary>
    OreSighting,
    /// <summary>Console command relayed across the fleet.</summary>
    Command
}
