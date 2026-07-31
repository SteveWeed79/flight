// ============================================================================
//  RUNTIME STATE
// ============================================================================

// ---- Diagnostics ----------------------------------------------------------
string configError = "";
string statusLine = "Booting";
string faultReason = "";
readonly List<string> log = new List<string>();
const int LOG_MAX = 12;

/// <summary>Ticks since compile. Our clock. One tick = one Main() call.</summary>
long tick;
/// <summary>Wall-clock seconds since compile, accumulated from TimeSinceLastRun.</summary>
double clock;
/// <summary>Seconds elapsed since the previous Main(). The dt for all control maths.</summary>
double dt = 1.0 / 6.0;
/// <summary>Rolling peak of instruction-count usage, as a fraction. Shown on the LCD.</summary>
double loadPeak;
/// <summary>Last fully-rendered screen text, re-echoed on the ticks we skip.</summary>
string lastRender = "";

// ---- Blocks ---------------------------------------------------------------
IMyShipController controller;         // remote control preferred, cockpit accepted
IMyShipConnector dockConnector;
readonly List<IMyGyro> gyros = new List<IMyGyro>();
readonly List<IMyThrust> thrusters = new List<IMyThrust>();
readonly List<IMyShipDrill> drills = new List<IMyShipDrill>();
readonly List<IMyCargoContainer> cargo = new List<IMyCargoContainer>();
readonly List<IMyBatteryBlock> batteries = new List<IMyBatteryBlock>();
readonly List<IMyReactor> reactors = new List<IMyReactor>();
readonly List<IMyGasTank> hydrogenTanks = new List<IMyGasTank>();
readonly List<IMyShipConnector> ejectors = new List<IMyShipConnector>();
/// <summary>Surfaces that get plain monospace text — the PB's own screen.</summary>
readonly List<IMyTextSurface> screens = new List<IMyTextSurface>();
/// <summary>Tagged LCDs, which get the sprite dashboard instead.</summary>
readonly List<IMyTextSurface> panels = new List<IMyTextSurface>();
readonly List<IMyCameraBlock> cameras = new List<IMyCameraBlock>();
readonly List<IMyOreDetector> oreDetectors = new List<IMyOreDetector>();

/// <summary>Tick of the last full block rescan. We rescan periodically so that
/// welding on a new thruster mid-job is picked up without a recompile.</summary>
long lastScanTick = long.MinValue;
const int RESCAN_INTERVAL = 600;      // ~60 s at Update10

// ---- Ship profile ---------------------------------------------------------
double shipMass = 1.0;
/// <summary>Bounding radius of the grid, metres. Used for standoffs and stuck margins.</summary>
double shipRadius = 3.0;
/// <summary>Effective cutting radius of the drill head, metres.</summary>
double drillRadius = 1.4;
/// <summary>Local-frame offset from the controller to the drill face. Where the hole actually starts.</summary>
Vector3D drillOffset = Vector3D.Zero;
bool isLargeGrid;
/// <summary>Shaft pitch this hull's drill head implies. Copied into the job when
/// a job is created; never applied to a job received from a dispatcher.</summary>
double derivedSpacing = 2.4;

/// <summary>
/// Point that shaft selection measures travel distance from. A solo miner uses
/// its own position; a dispatcher uses the position of whichever drone is asking,
/// so drones are sent to the work nearest them rather than nearest the base.
/// </summary>
Vector3D selectionOrigin;

/// <summary>Distinct thruster subtype ids, in a stable order. Index space for
/// <see cref="Waypoint.ThrusterEfficiency"/>.</summary>
readonly List<string> thrusterTypes = new List<string>();
/// <summary>Max effective thrust per local direction: [axis 0..2, sign 0=+ 1=-].</summary>
readonly float[,] thrustByAxis = new float[3, 2];
/// <summary>Same, split per thruster subtype, so we can reason about atmosphere.</summary>
readonly Dictionary<string, float[,]> thrustByType = new Dictionary<string, float[,]>();
/// <summary>Thrusters bucketed by local push direction. Rebuilt on rescan.</summary>
readonly List<IMyThrust>[,] thrustBuckets = new List<IMyThrust>[3, 2];

// ---- Navigation -----------------------------------------------------------
Vector3D shipPos;
Vector3D shipVel;
Vector3D gravity;
double speed;

/// <summary>True while the flight controller is driving. Display only.</summary>
bool flightActive;
/// <summary>Distance to the active flight target, metres.</summary>
double distToTarget;
/// <summary>Degrees of angular error on the current orientation command.</summary>
double alignError;

// ---- Path -----------------------------------------------------------------
readonly List<Waypoint> path = new List<Waypoint>();
bool recording;
Vector3D lastRecordPos;
/// <summary>Index into <see cref="path"/> while flying it. Direction depends on state.</summary>
int pathIndex;
/// <summary>The dock we launched from, recorded as waypoint zero's frame.</summary>
Waypoint homeDock;
Vector3D homeDockForward;
Vector3D homeDockUp;
bool homeDockSet;

/// <summary>Heaviest total mass we can still fly the whole recorded path with.
/// Recomputed from thrust efficiency samples along the route. -1 = unknown.</summary>
double maxFlyableMass = -1;

// ---- Job ------------------------------------------------------------------
readonly Job job = new Job();
YieldCell[] cells = new YieldCell[0];
/// <summary>Cell index currently being worked, -1 = none.</summary>
int activeCell = -1;
/// <summary>Metres drilled in the current shaft.</summary>
double shaftDepth;
/// <summary>Deepest point reached in the current shaft. Depth can dip when we back off.</summary>
double shaftMaxDepth;
/// <summary>Depth cap for the current shaft; probes get a shallow one.</summary>
double shaftDepthLimit;
/// <summary>True if the current shaft is a scouting probe rather than production.</summary>
bool shaftIsProbe;
/// <summary>Ore in drill inventories when the shaft began, kg.</summary>
double shaftStartOre;
/// <summary>
/// Depth at which the drills first brought back material — i.e. where the real
/// rock surface is. -1 means we are still descending through vacuum.
///
/// Depth is measured from the job plane, but on an asteroid the actual surface
/// wanders tens of metres either side of it. Everything that reasons about "how
/// far have we drilled" must measure from here, not from the plane.
/// </summary>
double shaftContactDepth = -1;
/// <summary>Total cargo volume when the descent began. Contact detector.</summary>
double shaftStartVolume;
/// <summary>Whether the prospect pass has finished its probe lattice.</summary>
bool probePassDone;
/// <summary>How many times this job has walked toward continuing ore.</summary>
int followCount;

// ---- Self-tuning ----------------------------------------------------------
/// <summary>Live drilling speed. Never exceeds the configured drillSpeed.</summary>
double learnedDrillSpeed;
/// <summary>Live braking derate. Never exceeds BRAKE_DERATE.</summary>
double learnedBrakeDerate;
int cleanCutTicks;
int drillStalls;
int brakeOvershoots;
bool brakeWatchArmed;
double brakeClosest;

// ---- Adaptive depth -------------------------------------------------------
double lastOreSample;
double lastOreGainDepth;
int noOreTicks;

// ---- Stuck detection ------------------------------------------------------
double stuckRefDepth;
int stuckTicks;
int stuckRetries;
/// <summary>Failed docking approaches. Deliberately separate from
/// <see cref="stuckRetries"/>: they count unrelated things, and sharing one
/// counter meant a ship that had struggled in a shaft would fault on its first
/// docking hiccup instead of getting its three attempts.</summary>
int dockRetries;
/// <summary>Latches once inside the slow zone, so the mating run does not
/// oscillate between approach and docking speed at the boundary.</summary>
bool dockNearZone;
/// <summary>Consecutive ticks the connector has reported Connectable. Latching
/// on the first frame catches the ship still drifting sideways.</summary>
int connectDebounce;
/// <summary>Ticks since the mating distance last decreased.</summary>
int dockStallTicks;
/// <summary>Closest the connector has got on this approach, metres.</summary>
double lastDockDist = double.MaxValue;

// ---- Cargo / power --------------------------------------------------------
double cargoFill;
/// <summary>Absolute cargo volume. Finer-grained than the fill fraction, which
/// barely moves on a large ship when a few kilos of rock arrive.</summary>
double cargoVolume;
double batteryFill;
/// <summary>Kilograms of uranium across all reactors. A reactor ship with no
/// batteries reports full power forever, so this is its only fuel gauge.</summary>
double uraniumKg;
/// <summary>Fullest single inventory, 0..1. Aggregate fill hides the case where
/// one drill is brimming and has stopped collecting while the rest sit empty.</summary>
double peakInventoryFill;
double hydrogenFill;
/// <summary>kg of valuable ore aboard right now.</summary>
double oreAboard;

// ---- Fuel model -----------------------------------------------------------
// Hydrogen is the only resource that genuinely runs out. Batteries recharge from
// solar or a reactor mid-flight; hydrogen refills at base, or off ice you have
// to mine first. So the question that matters is not "am I below 25%" but "have
// I still got enough to get home from here" — which depends on how far away we
// are and how hard this particular ship drinks.
/// <summary>Measured hydrogen fraction consumed per metre travelled.</summary>
double hydroPerMetre;
/// <summary>True once we have a usable burn-rate measurement.</summary>
bool hydroCalibrated;
/// <summary>Tank level at the last sample point.</summary>
double hydroSampleFill;
/// <summary>Where we were at the last sample point.</summary>
Vector3D hydroSamplePos;
/// <summary>Distance from waypoint 0 to waypoint i, metres. Index-aligned with <see cref="path"/>.</summary>
double[] pathCumulative = new double[0];

// ---- State machine --------------------------------------------------------
MinerState state = MinerState.Idle;
/// <summary>Ticks spent in the current state. Watchdog input.</summary>
int stateTicks;
/// <summary>True only on the first tick of a state. Where per-state setup happens.</summary>
bool stateEntry = true;
/// <summary>Set when the operator has asked for work; cleared by "stop".</summary>
bool jobRunning;
/// <summary>Set when the job is finished, to stop us relaunching forever.</summary>
bool jobComplete;
/// <summary>Why the current shaft is ending. Read by FinishShaft once we are clear of the hole.</summary>
ShaftResult pendingResult = ShaftResult.Completed;
/// <summary>Fleet mode: a lease request is outstanding with the dispatcher.</summary>
bool awaitingLease;

// ---- Fleet ----------------------------------------------------------------
IMyBroadcastListener listener;
IMyUnicastListener unicast;
/// <summary>Dispatcher address if we have found one, 0 otherwise.</summary>
long dispatcherAddr;
long lastDispatcherSeenTick;
/// <summary>Dispatcher: everyone who has checked in.</summary>
readonly Dictionary<long, DroneRecord> fleet = new Dictionary<long, DroneRecord>();
/// <summary>Miner: our assigned altitude lane over the site, metres.</summary>
double myLane;
/// <summary>Miner: dock slot we hold, -1 = none.</summary>
int myDockSlot = -1;
/// <summary>Dispatcher: which drone holds each dock slot.</summary>
readonly Dictionary<int, long> dockSlotOwner = new Dictionary<int, long>();
/// <summary>Tick we last asked the dispatcher for something, for retry backoff.</summary>
long lastRequestTick;

// ---- Airspace locks -------------------------------------------------------
/// <summary>Miner: section we currently hold, empty if none.</summary>
string heldLock = "";
bool awaitingLock;
long lockAskedTick;
/// <summary>Dispatcher: who holds each section.</summary>
readonly Dictionary<string, long> lockOwner = new Dictionary<string, long>();
/// <summary>Dispatcher: FIFO of drones waiting on each section.</summary>
readonly Dictionary<string, List<long>> lockQueue = new Dictionary<string, List<long>>();
/// <summary>Dispatcher: when each held section falls in, if unrenewed.</summary>
readonly Dictionary<string, long> lockExpiry = new Dictionary<string, long>();
/// <summary>Scratch for mutating the lock tables without enumerating while removing.</summary>
readonly List<string> lockScratch = new List<string>();

// ---- Scouting -------------------------------------------------------------
/// <summary>True once we have confirmed the Ore Detector Raycast mod responds.</summary>
bool oreModAvailable;
/// <summary>Set after the one-time probe, so we do not retest every tick.</summary>
bool oreModProbed;
readonly List<OreSighting> sightings = new List<OreSighting>();
const int SIGHTINGS_MAX = 64;
int scanAzimuth;
int scanElevation;

// ---- Reusable scratch -----------------------------------------------------
// Allocating inside Main() is how SE scripts end up stuttering. These are
// cleared and refilled instead.
readonly List<MyInventoryItem> itemScratch = new List<MyInventoryItem>();
readonly List<IMyTerminalBlock> blockScratch = new List<IMyTerminalBlock>();
readonly StringBuilder sb = new StringBuilder();
