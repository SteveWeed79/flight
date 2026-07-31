// VEIN — Vectored Extraction & Intelligent Navigation
// Minified build. Readable source: src/ in the project repository.
// Commands: start | stop | home | clear | record start|stop | job set <w> <h> <d>
const string VEIN_VERSION = "1.0.0";
const string STORAGE_REV  = "2";
public enum Role
{
Miner,
Dispatcher
}
public enum MinerState
{
Idle,
Undocking,
Outbound,
Selecting,
Approaching,
Descending,
Ascending,
Inbound,
Docking,
Unloading,
Servicing,
Fault
}
public enum DepthMode
{
Fixed,
AutoOre,
AutoVoid
}
public enum HoleOrder
{
Serpentine,
Spiral,
Prospect
}
public enum EjectMode
{
Off,
Stone,
StoneAndIce
}
public enum CellState
{
Unknown,
Leased,
Rich,
Barren,
Exhausted,
Blocked
}
public enum ShaftResult
{
Completed,
OreExhausted,
CargoFull,
Stuck,
Aborted
}
public class Waypoint
{
public Vector3D Position;
public Vector3D Gravity;
public float[] ThrusterEfficiency;
public float Lift;
public Waypoint() { }
public Waypoint(Vector3D pos, Vector3D grav, float[] eff, float lift)
{
Position = pos;
Gravity = grav;
ThrusterEfficiency = eff;
Lift = lift;
}
public bool InGravity { get { return Gravity.LengthSquared() > 0.0001; } }
}
public class Job
{
public bool IsSet;
public Vector3D Origin;
public Vector3D Right;
public Vector3D Forward;
public Vector3D Down;
public Vector3D Gravity;
public int Width = 5;
public int Height = 5;
public int Depth = 40;
public double Spacing = 2.4;
public Job() { }
public Vector3D CellMouth(int col, int row, double _ae)
{
double cx = (col - (Width - 1) * 0.5) * Spacing;
double cy = (row - (Height - 1) * 0.5) * Spacing;
return Origin + Right * cx + Forward * cy - Down * _ae;
}
public Vector3D CellDepth(int col, int row, double depth)
{
return CellMouth(col, row, 0) + Down * depth;
}
public int CellCount { get { return Width * Height; } }
public int IndexOf(int col, int row) { return row * Width + col; }
}
public class YieldCell
{
public CellState State = CellState.Unknown;
public float OreKg;
public float MetresDrilled;
public float DepthReached;
public long LeasedBy;
public long LeaseExpiresTick;
public int StuckCount;
public float Yield
{
get { return MetresDrilled > 0.5f ? OreKg / MetresDrilled : 0f; }
}
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
public class DroneRecord
{
public long Address;
public string Name = "?";
public MinerState State = MinerState.Idle;
public float CargoFill;
public float Battery;
public Vector3D Position;
public long LastSeenTick;
public int LeasedCell = -1;
public int DockSlot = -1;
public double Lane;
}
public class OreSighting
{
public string OreType = "";
public Vector3D Position;
public long Tick;
}
public struct Health
{
public bool Ok;
public string Detail;
public static Health Good() { Health h; h.Ok = true; h.Detail = ""; return h; }
public static Health Bad(string why) { Health h; h.Ok = false; h.Detail = why; return h; }
}
Role role = Role.Miner;
string shipName = "";
string blockTag = "";
string _j = "VEIN";
DepthMode depthMode = DepthMode.AutoOre;
HoleOrder holeOrder = HoleOrder.Prospect;
EjectMode ejectMode = EjectMode.Stone;
double shaftOverlap = 0.15;
double drillSpeed = 1.2;
double retreatSpeed = 3.0;
double cruiseSpeed = 40.0;
double dockSpeed = 1.5;
double cargoFullAt = 0.92;
bool drillOnRetreat = false;
double probeDepth = 12.0;
int probeStride = 2;
double _o = 0.8;
bool useOreDetectorMod = true;
double oreScanRange = 500.0;
double minBattery = 0.30;
double minHydrogen = 0.25;
double resumeBattery = 0.95;
double _ac = 0.90;
double _af = 0.80;
double transitAltitude = 25.0;
double stateTimeout = 240.0;
bool stopOnDamage = false;
double droneTimeout = 30.0;
double laneSpacing = 12.0;
int dockSlots = 1;
string lcdTag = "[VEIN]";
bool verboseEcho = true;
readonly MyIni ini = new MyIni();
const string S_ID = "vein.identity";
const string _r = "vein.mining";
const string S_SCOUT = "vein.scouting";
const string S_SAFE = "vein.safety";
const string S_FLEET = "vein.fleet";
const string S_DISP = "vein.display";
void LoadConfig()
{
configError = "";
string raw = Me.CustomData;
if (string.IsNullOrWhiteSpace(raw))
{
WriteConfig();
return;
}
MyIniParseResult parse;
if (!ini.TryParse(raw, out parse))
{
configError = "Custom Data line " + parse.LineNo + ": " + parse.Error;
return;
}
role      = ParseRole(ini.Get(S_ID, "role").ToString("Miner"));
shipName  = ini.Get(S_ID, "shipName").ToString("");
blockTag  = ini.Get(S_ID, "blockTag").ToString("");
_j = ini.Get(S_ID, "channel").ToString("VEIN");
depthMode = ParseDepthMode(ini.Get(_r, "depthMode").ToString("AutoOre"));
holeOrder = ParseHoleOrder(ini.Get(_r, "holeOrder").ToString("Prospect"));
ejectMode = ParseEjectMode(ini.Get(_r, "eject").ToString("Stone"));
shaftOverlap  = Clamp(ini.Get(_r, "shaftOverlap").ToDouble(0.15), 0.0, 0.75);
drillSpeed    = Clamp(ini.Get(_r, "drillSpeed").ToDouble(1.2), 0.05, 10.0);
retreatSpeed  = Clamp(ini.Get(_r, "retreatSpeed").ToDouble(3.0), 0.2, 20.0);
cruiseSpeed   = Clamp(ini.Get(_r, "cruiseSpeed").ToDouble(40.0), 1.0, 300.0);
dockSpeed     = Clamp(ini.Get(_r, "dockSpeed").ToDouble(1.5), 0.2, 10.0);
cargoFullAt   = Clamp(ini.Get(_r, "cargoFullAt").ToDouble(0.92), 0.1, 0.99);
drillOnRetreat = ini.Get(_r, "drillOnRetreat").ToBoolean(false);
probeDepth    = Clamp(ini.Get(S_SCOUT, "probeDepth").ToDouble(12.0), 2.0, 200.0);
probeStride   = (int)Clamp(ini.Get(S_SCOUT, "probeStride").ToInt32(2), 1, 8);
_o = Clamp(ini.Get(S_SCOUT, "barrenThreshold").ToDouble(0.8), 0.0, 100.0);
useOreDetectorMod = ini.Get(S_SCOUT, "useOreDetectorMod").ToBoolean(true);
oreScanRange  = Clamp(ini.Get(S_SCOUT, "oreScanRange").ToDouble(500.0), 50.0, 20000.0);
minBattery    = Clamp(ini.Get(S_SAFE, "minBattery").ToDouble(0.30), 0.05, 0.95);
minHydrogen   = Clamp(ini.Get(S_SAFE, "minHydrogen").ToDouble(0.25), 0.0, 0.95);
resumeBattery = Clamp(ini.Get(S_SAFE, "resumeBattery").ToDouble(0.95), 0.1, 1.0);
_ac = Clamp(ini.Get(S_SAFE, "resumeHydrogen").ToDouble(0.90), 0.0, 1.0);
_af = Clamp(ini.Get(S_SAFE, "liftSafetyFactor").ToDouble(0.80), 0.2, 1.0);
transitAltitude = Clamp(ini.Get(S_SAFE, "transitAltitude").ToDouble(25.0), 2.0, 500.0);
stateTimeout  = Math.Max(0.0, ini.Get(S_SAFE, "stateTimeout").ToDouble(240.0));
stopOnDamage  = ini.Get(S_SAFE, "stopOnDamage").ToBoolean(false);
droneTimeout  = Clamp(ini.Get(S_FLEET, "droneTimeout").ToDouble(30.0), 5.0, 600.0);
laneSpacing   = Clamp(ini.Get(S_FLEET, "laneSpacing").ToDouble(12.0), 3.0, 100.0);
dockSlots     = (int)Clamp(ini.Get(S_FLEET, "dockSlots").ToInt32(1), 1, 32);
lcdTag        = ini.Get(S_DISP, "lcdTag").ToString("[VEIN]");
verboseEcho   = ini.Get(S_DISP, "verboseEcho").ToBoolean(true);
if (resumeBattery <= minBattery) resumeBattery = Math.Min(1.0, minBattery + 0.15);
if (_ac <= minHydrogen) _ac = Math.Min(1.0, minHydrogen + 0.15);
WriteConfig();
}
void WriteConfig()
{
ini.Clear();
ini.Set(S_ID, "role", role.ToString());
ini.SetComment(S_ID, "role", "Miner or Dispatcher. Miners find a Dispatcher automatically;\nwith none on the channel they just run the job themselves.");
ini.Set(S_ID, "shipName", shipName);
ini.SetComment(S_ID, "shipName", "Shown on fleet displays. Blank = use the grid name.");
ini.Set(S_ID, "blockTag", blockTag);
ini.SetComment(S_ID, "blockTag", "Only use blocks whose name contains this. Blank = whole grid.\nSet it if the miner docks to a base that also has drills.");
ini.Set(S_ID, "channel", _j);
ini.Set(_r, "depthMode", depthMode.ToString());
ini.SetComment(_r, "depthMode", "Fixed    - always drill to the job depth.\nAutoOre  - stop when ore stops arriving (recommended).\nAutoVoid - stop only on breakthrough into empty space.");
ini.Set(_r, "holeOrder", holeOrder.ToString());
ini.SetComment(_r, "holeOrder", "Serpentine - corner to corner, least travel.\nSpiral     - outward from centre.\nProspect   - probe wide, then chase the ore (recommended).");
ini.Set(_r, "eject", ejectMode.ToString());
ini.SetComment(_r, "eject", "Off, Stone, or StoneAndIce. Needs at least one ejector/connector\nin Throw Out mode. Roughly triples time-on-site.");
ini.Set(_r, "shaftOverlap", shaftOverlap);
ini.SetComment(_r, "shaftOverlap", "0.15 = shafts overlap 15%. Higher clears more rock, digs more holes.");
ini.Set(_r, "drillSpeed", drillSpeed);
ini.SetComment(_r, "drillSpeed", "m/s downward while cutting. Above ~2 m/s drills stop keeping up\nand you jam. Lower it on a light ship.");
ini.Set(_r, "retreatSpeed", retreatSpeed);
ini.Set(_r, "cruiseSpeed", cruiseSpeed);
ini.SetComment(_r, "cruiseSpeed", "Ceiling only. Real speed is capped by whatever the ship can\nactually stop from, given its mass and the local gravity.");
ini.Set(_r, "dockSpeed", dockSpeed);
ini.Set(_r, "cargoFullAt", cargoFullAt);
ini.Set(_r, "drillOnRetreat", drillOnRetreat);
ini.Set(S_SCOUT, "probeDepth", probeDepth);
ini.SetComment(S_SCOUT, "probeDepth", "Prospect mode: metres per test shaft before judging a cell.");
ini.Set(S_SCOUT, "probeStride", probeStride);
ini.SetComment(S_SCOUT, "probeStride", "Probe every Nth cell to build the first map. 2 is a good default;\n3-4 on a big site you want surveyed fast.");
ini.Set(S_SCOUT, "barrenThreshold", _o);
ini.SetComment(S_SCOUT, "barrenThreshold", "kg of ore per metre below which a cell is written off.");
ini.Set(S_SCOUT, "useOreDetectorMod", useOreDetectorMod);
ini.SetComment(S_SCOUT, "useOreDetectorMod", "Auto-detect Racher's 'Ore Detector Raycast' mod and read real ore\ncoordinates from it. Harmless if the mod is absent.");
ini.Set(S_SCOUT, "oreScanRange", oreScanRange);
ini.Set(S_SAFE, "minBattery", minBattery);
ini.Set(S_SAFE, "minHydrogen", minHydrogen);
ini.Set(S_SAFE, "resumeBattery", resumeBattery);
ini.Set(S_SAFE, "resumeHydrogen", _ac);
ini.Set(S_SAFE, "liftSafetyFactor", _af);
ini.SetComment(S_SAFE, "liftSafetyFactor", "Fraction of measured lift we will spend. 0.8 leaves 20% in hand\nfor a heavy load and a bad angle.");
ini.Set(S_SAFE, "transitAltitude", transitAltitude);
ini.Set(S_SAFE, "stateTimeout", stateTimeout);
ini.SetComment(S_SAFE, "stateTimeout", "Seconds before the watchdog calls a state hung and recovers.\n0 disables it, which is rarely what you want.");
ini.Set(S_SAFE, "stopOnDamage", stopOnDamage);
ini.Set(S_FLEET, "droneTimeout", droneTimeout);
ini.SetComment(S_FLEET, "droneTimeout", "Seconds of silence before a drone is presumed lost and its shaft\nis handed to somebody else.");
ini.Set(S_FLEET, "laneSpacing", laneSpacing);
ini.SetComment(S_FLEET, "laneSpacing", "Metres between drone altitude lanes over the site. Must exceed\nthe largest drone's height by a comfortable margin.");
ini.Set(S_FLEET, "dockSlots", dockSlots);
ini.Set(S_DISP, "lcdTag", lcdTag);
ini.Set(S_DISP, "verboseEcho", verboseEcho);
string rendered = ini.ToString();
if (rendered != Me.CustomData) Me.CustomData = rendered;
}
static bool Same(string a, string b)
{
if (a == null || b == null) return false;
return a.Trim().ToUpperInvariant() == b.Trim().ToUpperInvariant();
}
Role ParseRole(string s)
{
if (Same(s, "Dispatcher")) return Role.Dispatcher;
if (Same(s, "Miner")) return Role.Miner;
BadValue(s, "role", "Miner");
return Role.Miner;
}
DepthMode ParseDepthMode(string s)
{
if (Same(s, "Fixed")) return DepthMode.Fixed;
if (Same(s, "AutoOre")) return DepthMode.AutoOre;
if (Same(s, "AutoVoid")) return DepthMode.AutoVoid;
BadValue(s, "depthMode", "AutoOre");
return DepthMode.AutoOre;
}
HoleOrder ParseHoleOrder(string s)
{
if (Same(s, "Serpentine")) return HoleOrder.Serpentine;
if (Same(s, "Spiral")) return HoleOrder.Spiral;
if (Same(s, "Prospect")) return HoleOrder.Prospect;
BadValue(s, "holeOrder", "Prospect");
return HoleOrder.Prospect;
}
EjectMode ParseEjectMode(string s)
{
if (Same(s, "Off")) return EjectMode.Off;
if (Same(s, "Stone")) return EjectMode.Stone;
if (Same(s, "StoneAndIce") || Same(s, "StoneIce")) return EjectMode.StoneAndIce;
BadValue(s, "eject", "Stone");
return EjectMode.Stone;
}
void BadValue(string got, string key, string fallback)
{
if (string.IsNullOrWhiteSpace(got)) return;
configError = "Custom Data: '" + got.Trim() + "' is not a valid " + key + ", using " + fallback + ".";
}
string configError = "";
string _f = "Booting";
string faultReason = "";
readonly List<string> log = new List<string>();
const int LOG_MAX = 12;
long tick;
double clock;
double dt = 1.0 / 6.0;
double loadPeak;
string lastRender = "";
IMyShipController controller;
IMyShipConnector dockConnector;
readonly List<IMyGyro> gyros = new List<IMyGyro>();
readonly List<IMyThrust> thrusters = new List<IMyThrust>();
readonly List<IMyShipDrill> drills = new List<IMyShipDrill>();
readonly List<IMyCargoContainer> cargo = new List<IMyCargoContainer>();
readonly List<IMyBatteryBlock> batteries = new List<IMyBatteryBlock>();
readonly List<IMyGasTank> hydrogenTanks = new List<IMyGasTank>();
readonly List<IMyShipConnector> ejectors = new List<IMyShipConnector>();
readonly List<IMyTextSurface> screens = new List<IMyTextSurface>();
readonly List<IMyTextSurface> panels = new List<IMyTextSurface>();
readonly List<IMyCameraBlock> cameras = new List<IMyCameraBlock>();
readonly List<IMyOreDetector> oreDetectors = new List<IMyOreDetector>();
long lastScanTick = long.MinValue;
const int RESCAN_INTERVAL = 600;
double shipMass = 1.0;
double shipRadius = 3.0;
double drillRadius = 1.4;
Vector3D drillOffset = Vector3D.Zero;
bool isLargeGrid;
double derivedSpacing = 2.4;
Vector3D selectionOrigin;
readonly List<string> thrusterTypes = new List<string>();
readonly float[,] thrustByAxis = new float[3, 2];
readonly Dictionary<string, float[,]> thrustByType = new Dictionary<string, float[,]>();
readonly List<IMyThrust>[,] thrustBuckets = new List<IMyThrust>[3, 2];
Vector3D shipPos;
Vector3D shipVel;
Vector3D gravity;
double speed;
bool flightActive;
double distToTarget;
double alignError;
readonly List<Waypoint> path = new List<Waypoint>();
bool recording;
Vector3D lastRecordPos;
int _n;
Waypoint homeDock;
Vector3D homeDockForward;
Vector3D homeDockUp;
bool _ag;
double _b = -1;
readonly Job job = new Job();
YieldCell[] cells = new YieldCell[0];
int _a = -1;
double _g;
double _aa;
double _x;
bool _h;
double shaftStartOre;
double _e = -1;
double shaftStartVolume;
bool _s;
double lastOreSample;
double _t;
int noOreTicks;
double stuckRefDepth;
int stuckTicks;
int stuckRetries;
int dockRetries;
double _ad;
double cargoVolume;
double _q;
double _i;
double oreAboard;
double hydroPerMetre;
bool _y;
double hydroSampleFill;
Vector3D hydroSamplePos;
double[] pathCumulative = new double[0];
MinerState state = MinerState.Idle;
int stateTicks;
bool stateEntry = true;
bool _p;
bool _m;
ShaftResult pendingResult = ShaftResult.Completed;
bool _ab;
IMyBroadcastListener listener;
IMyUnicastListener unicast;
long _c;
long _u;
readonly Dictionary<long, DroneRecord> fleet = new Dictionary<long, DroneRecord>();
double myLane;
int myDockSlot = -1;
readonly Dictionary<int, long> dockSlotOwner = new Dictionary<int, long>();
long lastRequestTick;
bool _l;
bool oreModProbed;
readonly List<OreSighting> sightings = new List<OreSighting>();
const int SIGHTINGS_MAX = 64;
int scanAzimuth;
int scanElevation;
readonly List<MyInventoryItem> itemScratch = new List<MyInventoryItem>();
readonly List<IMyTerminalBlock> blockScratch = new List<IMyTerminalBlock>();
readonly StringBuilder sb = new StringBuilder();
public Program()
{
Runtime.UpdateFrequency = UpdateFrequency.Update10;
try
{
LoadConfig();
ScanBlocks();
LoadState();
SetupIgc();
Log("VEIN " + VEIN_VERSION + " ready (" + role + ")");
}
catch (Exception e)
{
faultReason = "Boot failed: " + e.Message;
state = MinerState.Fault;
}
}
public void Save()
{
try { Storage = SerializeState(); }
catch {   }
}
public void Main(string argument, UpdateType updateSource)
{
try
{
if ((updateSource & (UpdateType.Terminal | UpdateType.Trigger | UpdateType.Script)) != 0
&& !string.IsNullOrWhiteSpace(argument))
{
HandleCommand(argument);
}
PumpIgc();
if ((updateSource & (UpdateType.Update1 | UpdateType.Update10 | UpdateType.Update100)) == 0)
{
Render(true);
return;
}
tick++;
double elapsed = Runtime.TimeSinceLastRun.TotalSeconds;
dt = Clamp(elapsed, 0.008, 0.5);
clock += dt;
if (tick - lastScanTick > RESCAN_INTERVAL) ScanBlocks();
SampleShip();
if (role == Role.Dispatcher) TickDispatcher();
else TickMiner();
Render();
TrackLoad();
}
catch (Exception e)
{
EnterFault("Unhandled: " + e.Message);
try { SafeStop(); } catch { }
Render(true);
}
}
void _d(MinerState next)
{
if (state == next)
{
stateTicks = 0;
stateEntry = true;
return;
}
state = next;
stateTicks = 0;
stateEntry = true;
stuckTicks = 0;
}
void Watchdog()
{
if (stateTimeout <= 0) return;
if (state == MinerState.Idle || state == MinerState.Fault) return;
if (state == MinerState.Servicing) return;
if (stateTicks * dt < stateTimeout) return;
Log("Watchdog: " + state + " ran over " + Fmt(stateTimeout, 0) + "s");
switch (state)
{
case MinerState.Descending:
case MinerState.Ascending:
MarkCellStuck();
pendingResult = ShaftResult.Stuck;
_d(MinerState.Ascending);
break;
case MinerState.Outbound:
case MinerState.Approaching:
case MinerState.Selecting:
_d(MinerState.Inbound);
break;
case MinerState.Docking:
dockRetries++;
if (dockRetries >= 3) EnterFault("Could not dock after 3 attempts");
else { Log("Docking retry " + dockRetries); _d(MinerState.Inbound); }
break;
default:
EnterFault("State " + state + " timed out");
break;
}
}
void EnterFault(string why)
{
if (state == MinerState.Fault) return;
faultReason = why;
Log("FAULT: " + why);
state = MinerState.Fault;
stateTicks = 0;
stateEntry = true;
_p = false;
ReleaseLease(ShaftResult.Aborted);
SafeStop();
}
void ClearFault()
{
faultReason = "";
stuckRetries = 0;
dockRetries = 0;
_d(MinerState.Idle);
Log("Fault cleared");
}
void TrackLoad()
{
double used = Runtime.MaxInstructionCount > 0
? (double)Runtime.CurrentInstructionCount / Runtime.MaxInstructionCount
: 0.0;
loadPeak = Math.Max(used, loadPeak * 0.97);
}
bool BudgetTight(double fraction = 0.6)
{
if (Runtime.MaxInstructionCount <= 0) return false;
return (double)Runtime.CurrentInstructionCount / Runtime.MaxInstructionCount > fraction;
}
void Log(string msg)
{
log.Add(Fmt(clock, 0) + "s  " + msg);
while (log.Count > LOG_MAX) log.RemoveAt(0);
}
void ScanBlocks()
{
lastScanTick = tick;
gyros.Clear(); thrusters.Clear(); drills.Clear(); cargo.Clear();
batteries.Clear(); hydrogenTanks.Clear(); ejectors.Clear();
screens.Clear(); cameras.Clear(); oreDetectors.Clear();
var controllers = new List<IMyShipController>();
GridTerminalSystem.GetBlocksOfType(controllers, Mine);
controller = null;
for (int i = 0; i < controllers.Count; i++)
{
if (controllers[i] is IMyRemoteControl) { controller = controllers[i]; break; }
}
if (controller == null && controllers.Count > 0) controller = controllers[0];
GridTerminalSystem.GetBlocksOfType(gyros, Mine);
GridTerminalSystem.GetBlocksOfType(thrusters, Mine);
GridTerminalSystem.GetBlocksOfType(drills, Mine);
GridTerminalSystem.GetBlocksOfType(cargo, Mine);
GridTerminalSystem.GetBlocksOfType(batteries, Mine);
var tanks = new List<IMyGasTank>();
GridTerminalSystem.GetBlocksOfType(tanks, Mine);
for (int i = 0; i < tanks.Count; i++)
{
if (tanks[i].BlockDefinition.SubtypeId.ToUpperInvariant().Contains("HYDROGEN"))
hydrogenTanks.Add(tanks[i]);
}
var connectors = new List<IMyShipConnector>();
GridTerminalSystem.GetBlocksOfType(connectors, Mine);
dockConnector = null;
for (int i = 0; i < connectors.Count; i++)
{
IMyShipConnector c = connectors[i];
if (c.ThrowOut) { ejectors.Add(c); continue; }
if (c.Status == MyShipConnectorStatus.Connected) dockConnector = c;
else if (dockConnector == null) dockConnector = c;
}
GridTerminalSystem.GetBlocksOfType(cameras, Mine);
GridTerminalSystem.GetBlocksOfType(oreDetectors, Mine);
CollectScreens();
BuildThrustModel();
MeasureShip();
ArmCameras();
}
bool Mine(IMyTerminalBlock b)
{
if (!b.IsSameConstructAs(Me)) return false;
if (blockTag.Length > 0 && !b.CustomName.Contains(blockTag)) return false;
return true;
}
void CollectScreens()
{
panels.Clear();
blockScratch.Clear();
GridTerminalSystem.GetBlocksOfType(blockScratch, b => Mine(b) && b.CustomName.Contains(lcdTag));
for (int i = 0; i < blockScratch.Count; i++)
{
var provider = blockScratch[i] as IMyTextSurfaceProvider;
if (provider == null || provider.SurfaceCount == 0) continue;
IMyTextSurface s = provider.GetSurface(0);
s.ContentType = ContentType.SCRIPT;
s.Script = "";
s.ScriptBackgroundColor = C_BG;
panels.Add(s);
}
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
void MeasureShip()
{
isLargeGrid = Me.CubeGrid.GridSizeEnum == MyCubeSize.Large;
double gridSize = isLargeGrid ? 2.5 : 0.5;
Vector3I min = Me.CubeGrid.Min, max = Me.CubeGrid.Max;
Vector3D extent = new Vector3D(max.X - min.X + 1, max.Y - min.Y + 1, max.Z - min.Z + 1) * gridSize;
shipRadius = Math.Max(1.0, extent.Length() * 0.5);
if (controller == null || drills.Count == 0)
{
drillRadius = isLargeGrid ? 1.9 : 0.65;
drillOffset = Vector3D.Zero;
derivedSpacing = Math.Max(0.5, drillRadius * 2.0 * (1.0 - shaftOverlap));
return;
}
double singleCut = isLargeGrid ? 1.9 : 0.65;
MatrixD refInv = MatrixD.Transpose(controller.WorldMatrix.GetOrientation());
Vector3D ctrlPos = controller.GetPosition();
double maxLateral = 0;
Vector3D offsetSum = Vector3D.Zero;
for (int i = 0; i < drills.Count; i++)
{
Vector3D local = Vector3D.TransformNormal(drills[i].GetPosition() - ctrlPos, refInv);
double lateral = Math.Sqrt(local.X * local.X + local.Y * local.Y);
if (lateral > maxLateral) maxLateral = lateral;
offsetSum += local;
}
drillRadius = maxLateral + singleCut;
drillOffset = offsetSum / drills.Count;
derivedSpacing = Math.Max(0.5, drillRadius * 2.0 * (1.0 - shaftOverlap));
}
Health CheckReadiness()
{
if (controller == null) return Health.Bad("No Remote Control or cockpit found");
if (gyros.Count == 0) return Health.Bad("No gyroscopes");
if (thrusters.Count == 0) return Health.Bad("No thrusters");
if (drills.Count == 0) return Health.Bad("No drills");
if (dockConnector == null) return Health.Bad("No connector");
if (!job.IsSet) return Health.Bad("No job set — use: job set <w> <h> <depth>");
if (path.Count == 0 && !_ag) return Health.Bad("No path recorded — use: record start/stop");
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
if (_b > 0 && shipMass > _b)
return Health.Bad("Too heavy for the route: " + Fmt(shipMass / 1000.0, 1) + "t of "
+ Fmt(_b / 1000.0, 1) + "t");
return Health.Good();
}
int DamagedBlockCount()
{
int n = 0;
for (int i = 0; i < thrusters.Count; i++) if (!thrusters[i].IsFunctional) n++;
for (int i = 0; i < gyros.Count; i++) if (!gyros[i].IsFunctional) n++;
for (int i = 0; i < drills.Count; i++) if (!drills[i].IsFunctional) n++;
return n;
}
const double BRAKE_DERATE = 0.75;
void BuildThrustModel()
{
for (int a = 0; a < 3; a++)
for (int s = 0; s < 2; s++)
{
if (thrustBuckets[a, s] == null) thrustBuckets[a, s] = new List<IMyThrust>();
else thrustBuckets[a, s].Clear();
}
thrusterTypes.Clear();
thrustByType.Clear();
if (controller == null) return;
MatrixD inv = MatrixD.Transpose(controller.WorldMatrix.GetOrientation());
for (int i = 0; i < thrusters.Count; i++)
{
IMyThrust t = thrusters[i];
Vector3D push = Vector3D.TransformNormal(t.WorldMatrix.Backward, inv);
int axis = 0;
double best = Math.Abs(push.X);
if (Math.Abs(push.Y) > best) { axis = 1; best = Math.Abs(push.Y); }
if (Math.Abs(push.Z) > best) { axis = 2; best = Math.Abs(push.Z); }
double component = axis == 0 ? push.X : (axis == 1 ? push.Y : push.Z);
int sign = component >= 0 ? 0 : 1;
thrustBuckets[axis, sign].Add(t);
string type = t.BlockDefinition.SubtypeId;
if (!thrustByType.ContainsKey(type))
{
thrustByType[type] = new float[3, 2];
thrusterTypes.Add(type);
}
}
thrusterTypes.Sort();
RefreshThrustCapacity();
}
void RefreshThrustCapacity()
{
for (int a = 0; a < 3; a++)
for (int s = 0; s < 2; s++)
thrustByAxis[a, s] = 0f;
foreach (var kv in thrustByType)
{
float[,] m = kv.Value;
for (int a = 0; a < 3; a++) for (int s = 0; s < 2; s++) m[a, s] = 0f;
}
for (int a = 0; a < 3; a++)
for (int s = 0; s < 2; s++)
{
List<IMyThrust> bucket = thrustBuckets[a, s];
if (bucket == null) continue;
for (int i = 0; i < bucket.Count; i++)
{
IMyThrust t = bucket[i];
if (!t.IsFunctional || !t.Enabled) continue;
thrustByAxis[a, s] += t.MaxEffectiveThrust;
float[,] typeMap;
if (thrustByType.TryGetValue(t.BlockDefinition.SubtypeId, out typeMap))
typeMap[a, s] += t.MaxEffectiveThrust;
}
}
}
double ThrustAlong(Vector3D worldDir)
{
if (controller == null) return 0;
if (worldDir.LengthSquared() < 1e-9) return 0;
worldDir = Vector3D.Normalize(worldDir);
Vector3D local = Vector3D.TransformNormal(worldDir, MatrixD.Transpose(controller.WorldMatrix.GetOrientation()));
double limit = double.MaxValue;
for (int axis = 0; axis < 3; axis++)
{
double c = axis == 0 ? local.X : (axis == 1 ? local.Y : local.Z);
if (Math.Abs(c) < 1e-4) continue;
float cap = thrustByAxis[axis, c >= 0 ? 0 : 1];
if (cap <= 0) return 0;
limit = Math.Min(limit, cap / Math.Abs(c));
}
return limit == double.MaxValue ? 0 : limit;
}
double StoppingAccel(Vector3D dir)
{
double raw = ThrustAlong(-dir) / Math.Max(1.0, shipMass);
return Math.Max(0.15, raw - gravity.Length());
}
void FlyTo(Vector3D target, double maxSpeed)
{
flightActive = true;
if (controller == null) return;
Vector3D toTarget = target - controller.GetPosition();
distToTarget = toTarget.Length();
Vector3D dir = distToTarget > 1e-4 ? toTarget / distToTarget : Vector3D.Zero;
double stopAccel = StoppingAccel(dir.LengthSquared() > 0 ? dir : Vector3D.Up);
double arrivalSpeed = Math.Sqrt(2.0 * stopAccel * Math.Max(0.0, distToTarget)) * BRAKE_DERATE;
double want = Math.Min(maxSpeed, arrivalSpeed);
if (alignError > 20.0) want *= Math.Max(0.15, 1.0 - (alignError - 20.0) / 70.0);
want = Math.Min(want, 95.0);
SetVelocity(dir * want);
}
void SetVelocity(Vector3D desiredVelWorld)
{
if (controller == null) return;
if (controller.DampenersOverride) controller.DampenersOverride = false;
Vector3D velError = desiredVelWorld - shipVel;
const double TAU = 0.45;
Vector3D desiredAccel = velError / TAU;
double accelCap = ThrustAlong(desiredAccel) / Math.Max(1.0, shipMass);
if (accelCap > 0 && desiredAccel.Length() > accelCap)
desiredAccel = Vector3D.Normalize(desiredAccel) * accelCap;
Vector3D force = (desiredAccel - gravity) * shipMass;
ApplyForce(force);
}
void ApplyForce(Vector3D worldForce)
{
if (controller == null) return;
Vector3D local = Vector3D.TransformNormal(worldForce, MatrixD.Transpose(controller.WorldMatrix.GetOrientation()));
ApplyAxis(0, local.X);
ApplyAxis(1, local.Y);
ApplyAxis(2, local.Z);
}
void ApplyAxis(int axis, double needed)
{
int pos = needed >= 0 ? 0 : 1;
int neg = 1 - pos;
float cap = thrustByAxis[axis, pos];
float ratio = cap > 1f ? (float)Clamp(Math.Abs(needed) / cap, 0.0, 1.0) : 0f;
List<IMyThrust> on = thrustBuckets[axis, pos];
if (on != null)
for (int i = 0; i < on.Count; i++)
if (on[i].IsFunctional) on[i].ThrustOverridePercentage = ratio;
List<IMyThrust> off = thrustBuckets[axis, neg];
if (off != null)
for (int i = 0; i < off.Count; i++)
if (off[i].IsFunctional) off[i].ThrustOverridePercentage = 0f;
}
void Orient(Vector3D desiredForward, Vector3D desiredUp)
{
if (controller == null || gyros.Count == 0) return;
if (desiredForward.LengthSquared() < 1e-9)
{
ReleaseGyros();
alignError = 0;
return;
}
desiredForward = Vector3D.Normalize(desiredForward);
if (desiredUp.LengthSquared() < 1e-9)
desiredUp = gravity.LengthSquared() > 1e-6 ? -Vector3D.Normalize(gravity) : controller.WorldMatrix.Up;
desiredUp = desiredUp - desiredForward * Vector3D.Dot(desiredUp, desiredForward);
if (desiredUp.LengthSquared() < 1e-9)
{
desiredUp = Math.Abs(desiredForward.Z) < 0.9
? Vector3D.Normalize(Vector3D.Cross(desiredForward, Vector3D.Forward))
: Vector3D.Normalize(Vector3D.Cross(desiredForward, Vector3D.Right));
}
else desiredUp = Vector3D.Normalize(desiredUp);
MatrixD m = controller.WorldMatrix;
Vector3D errAxis = Vector3D.Cross(m.Forward, desiredForward)
+ Vector3D.Cross(m.Up, desiredUp);
alignError = ToDegrees(Math.Acos(Clamp(Vector3D.Dot(m.Forward, desiredForward), -1.0, 1.0)));
if (errAxis.LengthSquared() < 1e-6 && alignError > 90.0)
{
Vector3D seed = Math.Abs(m.Forward.Z) < 0.9 ? Vector3D.Forward : Vector3D.Right;
errAxis = Vector3D.Normalize(Vector3D.Cross(m.Forward, seed));
}
Vector3D angVel = controller.GetShipVelocities().AngularVelocity;
const double KP = 3.0;
const double KD = 0.55;
Vector3D command = errAxis * KP - angVel * KD;
double maxRate = isLargeGrid ? 0.6 : 1.8;
if (command.Length() > maxRate) command = Vector3D.Normalize(command) * maxRate;
ApplyGyros(command);
}
void ApplyGyros(Vector3D worldCommand)
{
for (int i = 0; i < gyros.Count; i++)
{
IMyGyro g = gyros[i];
if (!g.IsFunctional) continue;
Vector3D local = Vector3D.TransformNormal(worldCommand, MatrixD.Transpose(g.WorldMatrix));
g.GyroOverride = true;
g.Pitch = (float)(-local.X);
g.Yaw   = (float)(-local.Y);
g.Roll  = (float)(-local.Z);
}
}
void ReleaseGyros()
{
for (int i = 0; i < gyros.Count; i++)
{
IMyGyro g = gyros[i];
if (!g.GyroOverride) continue;
g.GyroOverride = false;
g.Pitch = 0f; g.Yaw = 0f; g.Roll = 0f;
}
}
void SafeStop()
{
flightActive = false;
for (int a = 0; a < 3; a++)
for (int s = 0; s < 2; s++)
{
List<IMyThrust> bucket = thrustBuckets[a, s];
if (bucket == null) continue;
for (int i = 0; i < bucket.Count; i++) bucket[i].ThrustOverridePercentage = 0f;
}
ReleaseGyros();
SetDrills(false);
if (controller != null) controller.DampenersOverride = true;
}
void SetDrills(bool on)
{
for (int i = 0; i < drills.Count; i++)
if (drills[i].Enabled != on) drills[i].Enabled = on;
}
void SetThrusters(bool on)
{
for (int i = 0; i < thrusters.Count; i++)
if (thrusters[i].Enabled != on) thrusters[i].Enabled = on;
}
void SampleShip()
{
if (controller == null) return;
shipPos = controller.GetPosition();
MyShipVelocities v = controller.GetShipVelocities();
shipVel = v.LinearVelocity;
speed = shipVel.Length();
gravity = controller.GetNaturalGravity();
shipMass = Math.Max(1.0, controller.CalculateShipMass().PhysicalMass);
RefreshThrustCapacity();
SampleInventories();
if (tick % 6 == 0) SampleOre();
}
Vector3D DrillFace()
{
if (controller == null) return shipPos;
MatrixD m = controller.WorldMatrix;
return shipPos
+ m.Right * drillOffset.X
+ m.Up * drillOffset.Y
+ m.Backward * drillOffset.Z;
}
const string TYPE_ORE = "MyObjectBuilder_Ore";
const string SUB_STONE = "Stone";
const string SUB_ICE = "Ice";
void SampleInventories()
{
double vol = 0, maxVol = 0;
for (int i = 0; i < cargo.Count; i++)
AccumulateVolume(cargo[i].GetInventory(0), ref vol, ref maxVol);
for (int i = 0; i < drills.Count; i++)
AccumulateVolume(drills[i].GetInventory(0), ref vol, ref maxVol);
_ad = maxVol > 0 ? vol / maxVol : 0;
cargoVolume = vol;
double stored = 0, capacity = 0;
for (int i = 0; i < batteries.Count; i++)
{
IMyBatteryBlock b = batteries[i];
if (!b.IsFunctional) continue;
stored += b.CurrentStoredPower;
capacity += b.MaxStoredPower;
}
_q = capacity > 0 ? stored / capacity : 1.0;
double gas = 0; int tanks = 0;
for (int i = 0; i < hydrogenTanks.Count; i++)
{
if (!hydrogenTanks[i].IsFunctional) continue;
gas += hydrogenTanks[i].FilledRatio;
tanks++;
}
_i = tanks > 0 ? gas / tanks : 1.0;
}
static void AccumulateVolume(IMyInventory inv, ref double vol, ref double maxVol)
{
if (inv == null) return;
vol += (double)inv.CurrentVolume;
maxVol += (double)inv.MaxVolume;
}
void SampleOre()
{
double ore = 0;
for (int i = 0; i < cargo.Count; i++)
AccumulateOre(cargo[i].GetInventory(0), ref ore);
for (int i = 0; i < drills.Count; i++)
AccumulateOre(drills[i].GetInventory(0), ref ore);
oreAboard = ore;
}
void AccumulateOre(IMyInventory inv, ref double ore)
{
if (inv == null) return;
itemScratch.Clear();
inv.GetItems(itemScratch);
for (int i = 0; i < itemScratch.Count; i++)
if (IsValuableOre(itemScratch[i].Type)) ore += (double)itemScratch[i].Amount;
}
static bool IsValuableOre(MyItemType t)
{
return t.TypeId == TYPE_ORE && t.SubtypeId != SUB_STONE;
}
double ShaftOreSoFar()
{
return Math.Max(0.0, oreAboard - shaftStartOre);
}
bool CargoFull { get { return _ad >= cargoFullAt; } }
bool EjectWaste()
{
if (ejectMode == EjectMode.Off || ejectors.Count == 0) return true;
bool movedAny = false;
for (int e = 0; e < ejectors.Count; e++)
{
IMyShipConnector ej = ejectors[e];
if (!ej.IsFunctional) continue;
IMyInventory dst = ej.GetInventory(0);
if (dst == null) continue;
if (PushWasteInto(dst)) movedAny = true;
if (BudgetTight(0.75)) break;
}
if (!movedAny)
{
for (int e = 0; e < ejectors.Count; e++)
{
IMyInventory inv = ejectors[e].GetInventory(0);
if (inv != null && (double)inv.CurrentVolume > 0.001) return false;
}
return true;
}
return false;
}
bool PushWasteInto(IMyInventory dst)
{
bool moved = false;
for (int i = 0; i < cargo.Count; i++)
if (DrainWaste(cargo[i].GetInventory(0), dst)) moved = true;
for (int i = 0; i < drills.Count; i++)
if (DrainWaste(drills[i].GetInventory(0), dst)) moved = true;
return moved;
}
bool DrainWaste(IMyInventory src, IMyInventory dst)
{
if (src == null || dst == null || src == dst) return false;
if (!src.IsConnectedTo(dst)) return false;
itemScratch.Clear();
src.GetItems(itemScratch);
bool moved = false;
for (int i = itemScratch.Count - 1; i >= 0; i--)
{
if (!IsWaste(itemScratch[i].Type)) continue;
if (src.TransferItemTo(dst, i, null, true, null)) moved = true;
}
return moved;
}
bool IsWaste(MyItemType t)
{
if (t.TypeId != TYPE_ORE) return false;
if (t.SubtypeId == SUB_STONE) return true;
if (t.SubtypeId == SUB_ICE && ejectMode == EjectMode.StoneAndIce) return true;
return false;
}
bool UnloadToBase()
{
if (dockConnector == null || dockConnector.Status != MyShipConnectorStatus.Connected)
return false;
blockScratch.Clear();
GridTerminalSystem.GetBlocksOfType(blockScratch,
b => b is IMyCargoContainer && !b.IsSameConstructAs(Me));
IMyShipConnector far = dockConnector.OtherConnector;
if (far != null) blockScratch.Add(far);
if (blockScratch.Count == 0)
{
return _ad < 0.02;
}
bool moved = false;
for (int d = 0; d < blockScratch.Count; d++)
{
IMyInventory dst = blockScratch[d].GetInventory(0);
if (dst == null) continue;
if ((double)dst.CurrentVolume >= (double)dst.MaxVolume * 0.99) continue;
for (int i = 0; i < cargo.Count; i++)
if (DrainAll(cargo[i].GetInventory(0), dst)) moved = true;
for (int i = 0; i < drills.Count; i++)
if (DrainAll(drills[i].GetInventory(0), dst)) moved = true;
if (BudgetTight(0.75)) return false;
}
if (moved) SampleInventories();
return _ad < 0.02;
}
bool DrainAll(IMyInventory src, IMyInventory dst)
{
if (src == null || dst == null || src == dst) return false;
if (!src.IsConnectedTo(dst)) return false;
itemScratch.Clear();
src.GetItems(itemScratch);
bool moved = false;
for (int i = itemScratch.Count - 1; i >= 0; i--)
if (src.TransferItemTo(dst, i, null, true, null)) moved = true;
return moved;
}
void SetBatteryCharging(bool charging)
{
for (int i = 0; i < batteries.Count; i++)
{
IMyBatteryBlock b = batteries[i];
if (!b.IsFunctional) continue;
ChargeMode want = charging ? ChargeMode.Recharge : ChargeMode.Auto;
if (b.ChargeMode != want) b.ChargeMode = want;
}
}
void SetTanksFilling(bool filling)
{
for (int i = 0; i < hydrogenTanks.Count; i++)
{
IMyGasTank t = hydrogenTanks[i];
if (!t.IsFunctional) continue;
if (t.Stockpile != filling) t.Stockpile = filling;
}
}
void UpdateFuelModel()
{
if (hydrogenTanks.Count == 0) return;
if (hydroSamplePos == Vector3D.Zero)
{
hydroSamplePos = shipPos;
hydroSampleFill = _i;
return;
}
double travelled = Vector3D.Distance(shipPos, hydroSamplePos);
if (travelled < 150.0) return;
double used = hydroSampleFill - _i;
hydroSamplePos = shipPos;
hydroSampleFill = _i;
if (used <= 0) return;
double rate = used / travelled;
hydroPerMetre = _y ? hydroPerMetre * 0.7 + rate * 0.3 : rate;
_y = true;
}
double FuelToGetHome()
{
if (!_y || hydrogenTanks.Count == 0) return 0;
double distance = DistanceHomeAlongPath();
if (distance <= 0) return 0;
return distance * hydroPerMetre * 1.6;
}
bool FuelCriticalForReturn()
{
double need = FuelToGetHome();
if (need <= 0) return false;
return _i < need + 0.05;
}
bool ServiceComplete()
{
return _q >= resumeBattery && _i >= _ac;
}
void SetJob(int width, int height, int depth)
{
if (controller == null) { Log("Cannot set job: no controller"); return; }
MatrixD m = controller.WorldMatrix;
job.IsSet = true;
job.Origin = DrillFace();
job.Down = Vector3D.Normalize(m.Forward);
job.Right = Vector3D.Normalize(m.Right);
job.Forward = Vector3D.Normalize(m.Up);
job.Gravity = gravity;
job.Width = Math.Max(1, width);
job.Height = Math.Max(1, height);
job.Depth = Math.Max(1, depth);
MeasureShip();
job.Spacing = derivedSpacing;
RebuildCells();
_a = -1;
_m = false;
_s = false;
Log("Job " + job.Width + "x" + job.Height + " @" + job.Depth + "m, pitch "
+ Fmt(job.Spacing, 1) + "m");
}
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
const int SCORE_BUDGET = 48;
int scoreCursor;
int CellCol(int idx) { return idx % job.Width; }
int CellRow(int idx) { return idx / job.Width; }
int SelectNextCell()
{
return SelectNextCell(shipPos);
}
int SelectNextCell(Vector3D origin)
{
selectionOrigin = origin;
if (cells.Length != job.CellCount) RebuildCells();
switch (holeOrder)
{
case HoleOrder.Serpentine: _h = false; return NextSerpentine();
case HoleOrder.Spiral:     _h = false; return NextSpiral();
default:                                          return NextProspect();
}
}
int NextSerpentine()
{
for (int row = 0; row < job.Height; row++)
{
for (int i = 0; i < job.Width; i++)
{
int col = (row % 2 == 0) ? i : job.Width - 1 - i;
int idx = job.IndexOf(col, row);
if (cells[idx].Available) return idx;
}
}
return -1;
}
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
if (x == y || (x < 0 && x == -y) || (x > 0 && x == 1 - y))
{
int t = dx; dx = -dy; dy = t;
}
x += dx; y += dy;
}
return -1;
}
int NextProspect()
{
if (!_s)
{
int probe = NextProbeCell();
if (probe >= 0) { _h = true; return probe; }
_s = true;
Log("Survey complete: " + RichCellCount() + " rich of " + ProbedCellCount() + " probed");
}
_h = false;
int best = -1;
double bestScore = double.MinValue;
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
if (best < 0 && RemainingCellCount() > 0)
{
for (int i = 0; i < cells.Length; i++)
if (cells[i].Available) return i;
}
return best;
}
void Consider(int idx, ref int best, ref double bestScore)
{
if (idx < 0 || idx >= cells.Length) return;
if (!cells[idx].Available) return;
double score = ScoreCell(idx);
if (cells[idx].State == CellState.Barren && score < _o) return;
if (score < bestScore) return;
bestScore = score;
best = idx;
}
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
int EffectiveProbeStride()
{
const double TARGET_SPACING_M = 8.0;
int byDistance = (int)Math.Round(TARGET_SPACING_M / Math.Max(0.5, job.Spacing));
return Math.Max(1, Math.Max(probeStride, byDistance));
}
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
double ScoreCell(int idx)
{
int col = CellCol(idx), row = CellRow(idx);
const int RADIUS = 2;
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
if (cell.MetresDrilled < 0.5f) continue;
int dc = c - col, dr = r - row;
double d2 = dc * dc + dr * dr;
double w = 1.0 / (1.0 + d2);
weighted += w * cell.Yield;
weight += w;
}
}
double estimate = weight > 0 ? weighted / weight : _o * 1.5;
estimate += SightingBonus(col, row);
double travel = Vector3D.Distance(job.CellMouth(col, row, 0), selectionOrigin);
estimate -= travel * 0.004;
return estimate;
}
double SightingBonus(int col, int row)
{
if (sightings.Count == 0) return 0;
Vector3D mouth = job.CellMouth(col, row, 0);
double bonus = 0;
for (int i = 0; i < sightings.Count; i++)
{
Vector3D delta = sightings[i].Position - mouth;
double along = Vector3D.Dot(delta, job.Down);
Vector3D lateral = delta - job.Down * along;
double lat = lateral.Length();
if (lat > job.Spacing * 4) continue;
if (along < -job.Spacing || along > job.Depth + 20) continue;
bonus += 25.0 / (1.0 + lat);
}
return bonus;
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
void RecordShaftResult(int idx, ShaftResult result, double oreKg, double metres,
double depthReached, bool wasProbe)
{
if (idx < 0 || idx >= cells.Length) return;
YieldCell cell = cells[idx];
cell.OreKg += (float)oreKg;
cell.MetresDrilled += (float)metres;
cell.DepthReached = Math.Max(cell.DepthReached, (float)depthReached);
cell.LeasedBy = 0;
cell.LeaseExpiresTick = 0;
switch (result)
{
case ShaftResult.Completed:
if (wasProbe)
cell.State = cell.Yield >= _o ? CellState.Rich : CellState.Barren;
else
cell.State = CellState.Exhausted;
break;
case ShaftResult.OreExhausted:
cell.State = cell.Yield >= _o ? CellState.Exhausted : CellState.Barren;
break;
case ShaftResult.CargoFull:
cell.State = cell.Yield >= _o ? CellState.Rich : CellState.Unknown;
break;
case ShaftResult.Stuck:
cell.StuckCount++;
cell.State = cell.StuckCount >= 3 ? CellState.Blocked : CellState.Unknown;
break;
case ShaftResult.Aborted:
if (cell.State == CellState.Leased) cell.State = CellState.Unknown;
break;
}
if (role == Role.Miner && _c != 0)
SendShaftReport(idx, result, oreKg, metres, depthReached, wasProbe);
}
void MarkCellStuck()
{
if (_a < 0 || _a >= cells.Length) return;
cells[_a].StuckCount++;
if (cells[_a].StuckCount >= 3)
{
cells[_a].State = CellState.Blocked;
Log("Cell " + CellCol(_a) + "," + CellRow(_a) + " blocked");
}
}
double RecordInterval { get { return Math.Max(5.0, shipRadius * 2.0); } }
void StartRecording()
{
path.Clear();
recording = true;
_b = -1;
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
+ (_b > 0 ? "lift limit " + Fmt(_b / 1000.0, 1) + "t" : "no gravity on route"));
}
void CaptureHomeDock()
{
if (controller == null) return;
if (dockConnector != null)
{
MatrixD c = dockConnector.WorldMatrix;
homeDock = new Waypoint(dockConnector.GetPosition(), gravity, SampleEfficiency(), (float)CurrentLift());
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
_ag = true;
}
void RecordTick()
{
if (!recording) return;
if (path.Count == 0) { AddWaypoint(true); return; }
if (Vector3D.DistanceSquared(shipPos, lastRecordPos) < RecordInterval * RecordInterval) return;
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
double CurrentLift()
{
if (gravity.LengthSquared() < 1e-6) return 0;
return ThrustAlong(-Vector3D.Normalize(gravity));
}
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
void ComputeMaxFlyableMass()
{
_b = -1;
for (int i = 0; i < path.Count; i++)
{
Waypoint wp = path[i];
double g = wp.Gravity.Length();
if (g < 0.05) continue;
if (wp.Lift <= 0) continue;
double capable = (wp.Lift / g) * _af;
if (_b < 0 || capable < _b) _b = capable;
}
if (_ag && homeDock != null && homeDock.Gravity.Length() >= 0.05 && homeDock.Lift > 0)
{
double capable = (homeDock.Lift / homeDock.Gravity.Length()) * _af;
if (_b < 0 || capable < _b) _b = capable;
}
}
double RemainingLiftMargin()
{
if (_b <= 0) return double.MaxValue;
return _b - shipMass;
}
bool OverLiftLimit()
{
return _b > 0 && shipMass >= _b;
}
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
double DistanceHomeAlongPath()
{
if (path.Count == 0) return 0;
if (pathCumulative.Length != path.Count) BuildPathDistances();
int idx = Math.Max(0, Math.Min(path.Count - 1, _n));
return pathCumulative[idx] + Vector3D.Distance(shipPos, path[idx].Position);
}
double WaypointReached { get { return Math.Max(4.0, shipRadius * 1.5); } }
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
bool FollowPath(bool outbound)
{
if (path.Count == 0) return true;
_n = Math.Max(0, Math.Min(path.Count - 1, _n));
Waypoint wp = path[_n];
double dist = Vector3D.Distance(shipPos, wp.Position);
if (dist < WaypointReached)
{
int next = outbound ? _n + 1 : _n - 1;
if (next < 0 || next >= path.Count) return true;
_n = next;
wp = path[_n];
}
Vector3D aim = wp.Position;
int ahead = outbound ? _n + 1 : _n - 1;
if (ahead >= 0 && ahead < path.Count && dist > WaypointReached * 1.5)
{
Vector3D toNext = Vector3D.Normalize(path[ahead].Position - wp.Position);
aim = wp.Position + toNext * Math.Min(WaypointReached, dist * 0.3);
}
double speedLimit = cruiseSpeed;
int fromEnd = outbound ? path.Count - 1 - _n : _n;
if (fromEnd <= 1) speedLimit = Math.Min(speedLimit, 15.0);
FlyTo(aim, speedLimit);
Vector3D heading = aim - shipPos;
if (heading.LengthSquared() > 4.0)
Orient(heading, gravity.LengthSquared() > 1e-6 ? -gravity : Vector3D.Zero);
return false;
}
void BeginPath(bool outbound)
{
if (path.Count == 0) { _n = 0; return; }
int nearest = NearestWaypoint();
double distToNearest = Vector3D.Distance(path[nearest].Position, shipPos);
if (distToNearest < WaypointReached * 4)
_n = nearest;
else
_n = outbound ? 0 : path.Count - 1;
}
void TickMiner()
{
stateTicks++;
if (recording) RecordTick();
CheckDamage();
Watchdog();
UpdateOreScan();
EjectWhileFlying();
if (!Docked) UpdateFuelModel();
if (!Docked && state != MinerState.Idle && state != MinerState.Fault)
SetThrusters(true);
bool _w = stateEntry;
stateEntry = false;
switch (state)
{
case MinerState.Idle:        StIdle(_w); break;
case MinerState.Undocking:   StUndocking(_w); break;
case MinerState.Outbound:    StOutbound(_w); break;
case MinerState.Selecting:   StSelecting(_w); break;
case MinerState.Approaching: StApproaching(_w); break;
case MinerState.Descending:  StDescending(_w); break;
case MinerState.Ascending:   StAscending(_w); break;
case MinerState.Inbound:     StInbound(_w); break;
case MinerState.Docking:     StDocking(_w); break;
case MinerState.Unloading:   StUnloading(_w); break;
case MinerState.Servicing:   StServicing(_w); break;
case MinerState.Fault:       StFault(_w); break;
}
SendHeartbeat();
}
void StIdle(bool _w)
{
if (_w) { SafeStop(); _f = "Idle"; }
if (!_p || _m) return;
Health h = CheckReadiness();
if (!h.Ok) { _f = "Not ready: " + h.Detail; return; }
_d(Docked ? MinerState.Undocking : MinerState.Outbound);
}
bool Docked
{
get { return dockConnector != null && dockConnector.Status == MyShipConnectorStatus.Connected; }
}
void StUndocking(bool _w)
{
if (_w)
{
_f = "Undocking";
SetThrusters(true);
SetBatteryCharging(false);
SetTanksFilling(false);
if (Docked) dockConnector.Disconnect();
stuckRefDepth = 0;
}
if (Docked) { dockConnector.Disconnect(); return; }
Vector3D away = dockConnector != null
? dockConnector.WorldMatrix.Forward
: (controller != null ? controller.WorldMatrix.Up : Vector3D.Up);
Vector3D clear = homeDock != null ? homeDock.Position : shipPos;
double travelled = Vector3D.Distance(shipPos, clear);
double needed = shipRadius * 2.5;
if (travelled >= needed)
{
BeginPath(true);
_d(MinerState.Outbound);
return;
}
FlyTo(clear + away * needed, dockSpeed * 2.0);
}
void StOutbound(bool _w)
{
if (_w) { _f = "Outbound"; BeginPath(true); SetDrills(false); }
if (!_v()) { _d(MinerState.Inbound); return; }
if (FollowPath(true)) _d(MinerState.Selecting);
}
void StSelecting(bool _w)
{
if (_w)
{
_f = "Choosing shaft";
_a = -1;
_ab = false;
}
if (HasDispatcher)
{
if (!_ab)
{
RequestLease();
_ab = true;
lastRequestTick = tick;
return;
}
if (_a >= 0) { _ab = false; BeginShaft(); return; }
if (tick - lastRequestTick > 60)
{
if (tick - _u > (long)(droneTimeout / Math.Max(dt, 0.01)))
{
Log("Dispatcher lost — continuing solo");
_c = 0;
}
_ab = false;
}
return;
}
int cell = SelectNextCell();
if (cell < 0)
{
_m = true;
Log("Job complete");
_d(MinerState.Inbound);
return;
}
_a = cell;
cells[cell].State = CellState.Leased;
BeginShaft();
}
void BeginShaft()
{
SampleOre();
_g = 0;
_aa = 0;
shaftStartOre = oreAboard;
lastOreSample = 0;
_t = 0;
noOreTicks = 0;
stuckRetries = 0;
_e = -1;
_x = _h ? Math.Min(probeDepth, job.Depth) : job.Depth;
_d(MinerState.Approaching);
}
void StApproaching(bool _w)
{
if (_a < 0) { _d(MinerState.Selecting); return; }
if (_w) { _f = "To shaft " + CellLabel(_a); SetDrills(false); }
if (!_v()) { _z(ShaftResult.Aborted); return; }
int col = CellCol(_a), row = CellRow(_a);
double _ae = transitAltitude + myLane;
Vector3D above = job.CellMouth(col, row, _ae);
FlyTo(ControllerTargetFor(above), cruiseSpeed * 0.5);
Orient(job.Down, job.Forward);
bool overHole = distToTarget < Math.Max(1.5, shipRadius * 0.4);
bool square = alignError < 4.0;
if (!overHole || !square) return;
if (!ShaftHasRock(_ae)) { SkipEmptyCell(); return; }
_d(MinerState.Descending);
}
bool ShaftHasRock(double _ae)
{
if (cameras.Count == 0) return true;
double reach = _ae + Math.Min(_x, 60.0);
double hit;
if (!TryScanAhead(reach, out hit)) return true;
if (hit < 0) return false;
return hit <= _ae + 8.0;
}
void SkipEmptyCell()
{
Log(CellLabel(_a) + " is open space — skipping");
RecordShaftResult(_a, ShaftResult.Completed, 0, 0, 0, _h);
if (_a >= 0 && _a < cells.Length)
cells[_a].State = CellState.Barren;
ReleaseLeaseLocal();
_a = -1;
_d(MinerState.Selecting);
}
void StDescending(bool _w)
{
if (_a < 0) { _d(MinerState.Selecting); return; }
int col = CellCol(_a), row = CellRow(_a);
if (_w)
{
_f = (_h ? "Probing " : "Drilling ") + CellLabel(_a);
stuckRefDepth = CurrentShaftDepth(col, row);
stuckTicks = 0;
lastOreSample = ShaftOreSoFar();
_e = -1;
shaftStartVolume = cargoVolume;
}
_g = CurrentShaftDepth(col, row);
if (_g > _aa) _aa = _g;
if (_e < 0 && cargoVolume > shaftStartVolume + 0.001)
_e = _g;
bool inRock = _g > -2.0;
SetDrills(inRock);
double descentSpeed = inRock ? drillSpeed : Math.Min(12.0, Math.Max(retreatSpeed, 6.0));
if (!_v()) { _z(ShaftResult.Aborted); return; }
if (CargoFull || OverLiftLimit()) { _z(ShaftResult.CargoFull); return; }
if (_g >= EffectiveDepthLimit()) { _z(ShaftResult.Completed); return; }
if (DepthExhausted()) { _z(ShaftResult.OreExhausted); return; }
if (inRock && IsStuck())
{
stuckRetries++;
if (stuckRetries > 3) { _z(ShaftResult.Stuck); return; }
Log("Stuck at " + Fmt(_g, 1) + "m, backing off (" + stuckRetries + "/3)");
Vector3D relief = job.CellDepth(col, row, Math.Max(0, _g - 2.0));
FlyTo(ControllerTargetFor(relief), retreatSpeed);
Orient(job.Down, job.Forward);
stuckTicks = 0;
stuckRefDepth = _g - 2.0;
return;
}
double aimDepth = Math.Min(EffectiveDepthLimit(), Math.Max(_g, 0.0) + 5.0);
Vector3D bite = job.CellDepth(col, row, aimDepth);
FlyTo(ControllerTargetFor(bite), descentSpeed);
Orient(job.Down, job.Forward);
}
void StAscending(bool _w)
{
if (_w)
{
_f = "Withdrawing";
SetDrills(drillOnRetreat);
}
if (_a < 0) { _d(MinerState.Selecting); return; }
int col = CellCol(_a), row = CellRow(_a);
double depth = CurrentShaftDepth(col, row);
double _ae = transitAltitude + myLane;
Vector3D clearOfHole = job.CellMouth(col, row, _ae);
Vector3D exitPoint = depth > 1.0
? job.CellDepth(col, row, Math.Max(0, depth - 6.0))
: clearOfHole;
FlyTo(ControllerTargetFor(exitPoint), retreatSpeed);
Orient(job.Down, job.Forward);
if (depth > 1.0) return;
SetDrills(false);
FinishShaft();
}
void FinishShaft()
{
ShaftResult result = pendingResult;
SampleOre();
double ore = ShaftOreSoFar();
double cut = _e >= 0
? Math.Max(0.0, _aa - _e)
: 0.0;
if (_e < 0 && result == ShaftResult.Completed)
Log(CellLabel(_a) + " never reached rock");
RecordShaftResult(_a, result, ore, cut, cut, _h);
ReleaseLeaseLocal();
Log(CellLabel(_a) + " " + result + ": " + Fmt(ore, 0) + "kg / "
+ Fmt(cut, 1) + "m cut");
_a = -1;
if (!_p) { _d(MinerState.Inbound); return; }
if (result == ShaftResult.Aborted || result == ShaftResult.CargoFull)
{
_d(MinerState.Inbound);
return;
}
if (CargoFull || OverLiftLimit() || !_v())
{
_d(MinerState.Inbound);
return;
}
_d(MinerState.Selecting);
}
void _z(ShaftResult why)
{
pendingResult = why;
_d(MinerState.Ascending);
}
void EjectWhileFlying()
{
if (ejectMode == EjectMode.Off || ejectors.Count == 0) return;
if (Docked) return;
if (state == MinerState.Descending || state == MinerState.Ascending) return;
if (tick % 20 != 0) return;
if (BudgetTight(0.6)) return;
EjectWaste();
}
void StInbound(bool _w)
{
if (_w)
{
_f = "Returning";
SetDrills(false);
BeginPath(false);
if (HasDispatcher) RequestDock();
}
if (FollowPath(false)) _d(MinerState.Docking);
}
void StDocking(bool _w)
{
if (_w) { _f = "Docking"; SetDrills(false); }
if (dockConnector == null) { EnterFault("No connector to dock with"); return; }
if (!_ag) { EnterFault("No dock recorded"); return; }
if (Docked)
{
SafeStop();
dockRetries = 0;
_d(MinerState.Unloading);
return;
}
Vector3D mate = homeDock.Position;
Vector3D axis = homeDockForward;
double _ae = Math.Max(6.0, shipRadius * 2.0);
Vector3D hold = mate + axis * _ae;
Vector3D offAxis = shipPos - mate;
double along = Vector3D.Dot(offAxis, axis);
double lateral = (offAxis - axis * along).Length();
Vector3D connectorOffset = dockConnector.GetPosition() - shipPos;
if (lateral > 1.0 || along > _ae * 1.4)
{
FlyTo(hold - connectorOffset, dockSpeed * 3.0);
}
else
{
FlyTo(mate - connectorOffset, dockSpeed);
if (dockConnector.Status == MyShipConnectorStatus.Connectable)
dockConnector.Connect();
}
Orient(-axis, homeDockUp);
}
void StUnloading(bool _w)
{
if (_w)
{
_f = "Unloading";
SafeStop();
SetBatteryCharging(true);
SetTanksFilling(true);
}
if (!Docked) { _d(MinerState.Docking); return; }
if (UnloadToBase()) _d(MinerState.Servicing);
}
void StServicing(bool _w)
{
if (_w)
{
_f = "Charging";
SetBatteryCharging(true);
SetTanksFilling(true);
SetThrusters(false);
}
if (!Docked) { _d(MinerState.Docking); return; }
if (tick % 30 == 0) UnloadToBase();
if (!_p || _m)
{
_f = _m ? "Job complete — docked" : "Stopped — docked";
ReleaseDock();
return;
}
if (!ServiceComplete())
{
_f = "Charging " + Fmt(_q * 100, 0) + "% / H2 " + Fmt(_i * 100, 0) + "%";
return;
}
ReleaseDock();
_d(MinerState.Undocking);
}
void StFault(bool _w)
{
if (_w) SafeStop();
_f = "FAULT: " + faultReason;
}
bool _v()
{
if (_q < minBattery) return false;
if (hydrogenTanks.Count > 0 && _i < minHydrogen) return false;
if (FuelCriticalForReturn()) return false;
return true;
}
double EffectiveDepthLimit()
{
if (_e < 0) return job.Depth;
return _e + _x;
}
double CurrentShaftDepth(int col, int row)
{
Vector3D mouth = job.CellMouth(col, row, 0);
return Vector3D.Dot(DrillFace() - mouth, job.Down);
}
Vector3D ControllerTargetFor(Vector3D desiredFacePos)
{
return desiredFacePos - (DrillFace() - shipPos);
}
bool IsStuck()
{
if (_g > stuckRefDepth + 0.15)
{
stuckRefDepth = _g;
stuckTicks = 0;
return false;
}
stuckTicks++;
return stuckTicks > 40;
}
bool DepthExhausted()
{
if (depthMode == DepthMode.Fixed) return false;
if (_e < 0) { _t = _g; return false; }
if (_g < _e + 6.0) { _t = _g; return false; }
double now = depthMode == DepthMode.AutoOre ? ShaftOreSoFar() : _ad * 1000.0;
if (now > lastOreSample + 0.5)
{
lastOreSample = now;
_t = _g;
noOreTicks = 0;
return false;
}
noOreTicks++;
bool dryDistance = _g - _t > 5.0;
bool dryTime = noOreTicks > 60;
return dryDistance && dryTime;
}
void CheckDamage()
{
if (!stopOnDamage) return;
if (state == MinerState.Fault || state == MinerState.Idle) return;
if (DamagedBlockCount() == 0) return;
Log("Damage detected — returning");
if (state != MinerState.Inbound && state != MinerState.Docking
&& state != MinerState.Unloading && state != MinerState.Servicing)
{
if (_a >= 0) _z(ShaftResult.Aborted);
else _d(MinerState.Inbound);
}
}
string CellLabel(int idx)
{
if (idx < 0) return "-";
return "[" + CellCol(idx) + "," + CellRow(idx) + "]";
}
const int ORE_SCAN_PERIOD = 6;
void ProbeOreMod()
{
oreModProbed = true;
_l = false;
if (!useOreDetectorMod || oreDetectors.Count == 0) return;
IMyOreDetector d = oreDetectors[0];
try
{
Vector3D probe = d.GetPosition() + d.WorldMatrix.Forward * 10.0;
d.SetValue("RaycastTarget", probe);
Vector3D echoed = d.GetValue<Vector3D>("RaycastTarget");
if (Vector3D.DistanceSquared(echoed, probe) > 1.0) return;
try { d.SetValue("OreBlacklist", "Stone"); } catch { }
_l = true;
Log("Ore Detector Raycast mod found — true ore scouting enabled");
}
catch
{
}
}
void UpdateOreScan()
{
if (!oreModProbed) ProbeOreMod();
if (!_l) return;
if (tick % ORE_SCAN_PERIOD != 0) return;
if (BudgetTight(0.5)) return;
ExpireSightings();
IMyOreDetector d = oreDetectors[0];
if (!d.IsFunctional) return;
double available;
try { available = d.GetValue<double>("AvailableScanRange"); }
catch { _l = false; return; }
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
_l = false;
}
}
bool NextScanTarget(Vector3D from, out Vector3D target, out double distance)
{
target = Vector3D.Zero;
distance = 0;
if (job.IsSet)
{
int cellIdx = scanAzimuth % Math.Max(1, job.CellCount);
int col = cellIdx % job.Width;
int row = cellIdx / job.Width;
int depthStep = scanElevation % 4;
double depth = job.Depth * (0.25 + 0.25 * depthStep);
target = job.CellDepth(col, row, depth);
scanElevation++;
if (scanElevation % 4 == 0) scanAzimuth++;
distance = Vector3D.Distance(from, target);
return true;
}
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
void AddSighting(string oreType, Vector3D pos)
{
if (string.IsNullOrEmpty(oreType)) return;
if (oreType.ToUpperInvariant().Contains("STONE")) return;
double mergeRadius = Math.Max(4.0, job.Spacing);
for (int i = 0; i < sightings.Count; i++)
{
if (Vector3D.DistanceSquared(sightings[i].Position, pos) < mergeRadius * mergeRadius)
{
sightings[i].Tick = tick;
return;
}
}
OreSighting s = new OreSighting();
s.OreType = oreType;
s.Position = pos;
s.Tick = tick;
sightings.Add(s);
while (sightings.Count > SIGHTINGS_MAX) sightings.RemoveAt(0);
Log("Ore: " + oreType + " at " + Fmt(Vector3D.Distance(pos, shipPos), 0) + "m");
if (HasDispatcher) SendOreSighting(s);
}
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
bool TryScanAhead(double maxRange, out double distance)
{
distance = -1;
for (int i = 0; i < cameras.Count; i++)
{
IMyCameraBlock cam = cameras[i];
if (!cam.IsFunctional) continue;
if (!cam.EnableRaycast) { cam.EnableRaycast = true; continue; }
if (cam.AvailableScanRange < maxRange) continue;
MyDetectedEntityInfo hit = cam.Raycast(maxRange);
if (hit.IsEmpty() || !hit.HitPosition.HasValue) return true;
if (hit.Type != MyDetectedEntityType.Asteroid && hit.Type != MyDetectedEntityType.Planet)
return true;
distance = Vector3D.Distance(cam.GetPosition(), hit.HitPosition.Value);
return true;
}
return false;
}
void ArmCameras()
{
for (int i = 0; i < cameras.Count; i++)
if (cameras[i].IsFunctional && !cameras[i].EnableRaycast)
cameras[i].EnableRaycast = true;
}
string ScoutStatus()
{
if (_l) return "ore-raycast (" + sightings.Count + " leads)";
if (oreDetectors.Count > 0) return "probe map (no ore mod)";
return "probe map";
}
const int IGC_MAX_PER_TICK = 12;
void SetupIgc()
{
listener = IGC.RegisterBroadcastListener(_j);
listener.SetMessageCallback(_j);
unicast = IGC.UnicastListener;
unicast.SetMessageCallback(_j);
}
void PumpIgc()
{
if (listener == null) return;
int handled = 0;
while (listener.HasPendingMessage && handled < IGC_MAX_PER_TICK)
{
MyIGCMessage m = listener.AcceptMessage();
HandleMessage(m.Source, m.Data as string);
handled++;
}
while (unicast != null && unicast.HasPendingMessage && handled < IGC_MAX_PER_TICK * 2)
{
MyIGCMessage m = unicast.AcceptMessage();
HandleMessage(m.Source, m.Data as string);
handled++;
}
}
void HandleMessage(long src, string body)
{
if (string.IsNullOrEmpty(body)) return;
if (src == IGC.Me) return;
string[] f = body.Split('|');
if (f.Length == 0) return;
switch (f[0])
{
case "B":  OnBeacon(src, f); break;
case "H":  OnHeartbeat(src, f); break;
case "LR": OnLeaseRequest(src, f); break;
case "LG": OnLeaseGrant(src, f); break;
case "LD": OnLeaseDenied(src, f); break;
case "SR": OnShaftReport(src, f); break;
case "DR": OnDockRequest(src, f); break;
case "DG": OnDockGrant(src, f); break;
case "DX": OnDockRelease(src, f); break;
case "OS": OnOreSighting(src, f); break;
case "C":  if (f.Length > 1) HandleCommand(f[1], false); break;
}
}
bool HasDispatcher { get { return role == Role.Miner && _c != 0; } }
void SendHeartbeat()
{
if (role != Role.Miner || _c == 0) return;
if (tick % 12 != 0) return;
string body = "H|" + ShipLabel()
+ "|" + (int)state
+ "|" + EncD(_ad)
+ "|" + EncD(_q)
+ "|" + EncV(shipPos)
+ "|" + _a;
IGC.SendUnicastMessage(_c, _j, body);
}
void RequestLease()
{
if (_c == 0) return;
IGC.SendUnicastMessage(_c, _j, "LR|" + ShipLabel());
}
void SendShaftReport(int cellIdx, ShaftResult result, double oreKg, double metres,
double depthReached, bool wasProbe)
{
if (_c == 0) return;
string body = "SR|" + cellIdx + "|" + (int)result
+ "|" + EncD(oreKg) + "|" + EncD(metres)
+ "|" + EncD(depthReached) + "|" + (wasProbe ? 1 : 0);
IGC.SendUnicastMessage(_c, _j, body);
}
void RequestDock()
{
if (_c == 0) return;
IGC.SendUnicastMessage(_c, _j, "DR|" + ShipLabel());
}
void ReleaseDock()
{
if (_c == 0 || myDockSlot < 0) return;
IGC.SendUnicastMessage(_c, _j, "DX|" + myDockSlot);
myDockSlot = -1;
}
void SendOreSighting(OreSighting s)
{
if (_c == 0) return;
IGC.SendUnicastMessage(_c, _j, "OS|" + s.OreType + "|" + EncV(s.Position));
}
void ReleaseLease(ShaftResult why)
{
if (_a < 0) return;
if (_c != 0)
SendShaftReport(_a, why, ShaftOreSoFar(), _aa, _aa, _h);
ReleaseLeaseLocal();
_a = -1;
}
void ReleaseLeaseLocal()
{
if (_a < 0 || _a >= cells.Length) return;
if (cells[_a].State == CellState.Leased) cells[_a].State = CellState.Unknown;
cells[_a].LeasedBy = 0;
}
void SendBeacon()
{
if (!job.IsSet) { IGC.SendBroadcastMessage(_j, "B|0"); return; }
string body = "B|1"
+ "|" + job.Width + "|" + job.Height + "|" + job.Depth
+ "|" + EncD(job.Spacing)
+ "|" + EncV(job.Origin)
+ "|" + EncV(job.Right)
+ "|" + EncV(job.Forward)
+ "|" + EncV(job.Down);
IGC.SendBroadcastMessage(_j, body);
}
void GrantLease(long to, int cellIdx, double depthLimit, bool isProbe, double lane)
{
string body = "LG|" + cellIdx + "|" + EncD(depthLimit)
+ "|" + (isProbe ? 1 : 0) + "|" + EncD(lane);
IGC.SendUnicastMessage(to, _j, body);
}
void DenyLease(long to, string reason)
{
IGC.SendUnicastMessage(to, _j, "LD|" + reason);
}
void GrantDock(long to, int slot)
{
IGC.SendUnicastMessage(to, _j, "DG|" + slot);
}
void OnBeacon(long src, string[] f)
{
if (role != Role.Miner) return;
if (_c != 0 && _c != src)
{
if (tick - _u < 600)
{
Log("Second dispatcher on channel '" + _j + "' — ignoring it");
return;
}
Log("Switching dispatcher — previous one went quiet");
}
_c = src;
_u = tick;
if (f.Length < 10 || f[1] != "1") return;
int w = _k(f[2], job.Width);
int h = _k(f[3], job.Height);
int d = _k(f[4], job.Depth);
bool reshaped = !job.IsSet || w != job.Width || h != job.Height;
job.IsSet = true;
job.Width = w; job.Height = h; job.Depth = d;
job.Spacing = DecD(f[5]);
job.Origin = DecV(f[6]);
job.Right = DecV(f[7]);
job.Forward = DecV(f[8]);
job.Down = DecV(f[9]);
if (!ValidateJobBasis()) return;
if (reshaped || cells.Length != job.CellCount) RebuildCells();
}
void OnHeartbeat(long src, string[] f)
{
if (role != Role.Dispatcher || f.Length < 7) return;
DroneRecord r;
if (!fleet.TryGetValue(src, out r))
{
r = new DroneRecord();
r.Address = src;
r.Lane = fleet.Count * laneSpacing;
fleet[src] = r;
Log("Drone joined: " + f[1]);
}
r.Name = f[1];
r.State = (MinerState)_k(f[2], 0);
r.CargoFill = (float)DecD(f[3]);
r.Battery = (float)DecD(f[4]);
r.Position = DecV(f[5]);
r.LeasedCell = _k(f[6], -1);
r.LastSeenTick = tick;
}
void OnLeaseRequest(long src, string[] f)
{
if (role != Role.Dispatcher) return;
if (!job.IsSet) { DenyLease(src, "nojob"); return; }
if (!_p || _m) { DenyLease(src, "paused"); return; }
DroneRecord r;
if (!fleet.TryGetValue(src, out r))
{
r = new DroneRecord();
r.Address = src;
r.Name = f.Length > 1 ? f[1] : "?";
r.Lane = fleet.Count * laneSpacing;
fleet[src] = r;
}
r.LastSeenTick = tick;
int cell = SelectNextCell(r.Position.LengthSquared() > 1 ? r.Position : shipPos);
if (cell < 0) { DenyLease(src, "done"); return; }
cells[cell].State = CellState.Leased;
cells[cell].LeasedBy = src;
cells[cell].LeaseExpiresTick = tick + (long)(droneTimeout * 2 / Math.Max(dt, 0.01));
r.LeasedCell = cell;
double limit = _h ? Math.Min(probeDepth, job.Depth) : job.Depth;
GrantLease(src, cell, limit, _h, r.Lane);
}
void OnLeaseGrant(long src, string[] f)
{
if (role != Role.Miner || f.Length < 5) return;
_a = _k(f[1], -1);
_x = DecD(f[2]);
_h = f[3] == "1";
myLane = DecD(f[4]);
if (_a >= 0 && _a < cells.Length)
cells[_a].State = CellState.Leased;
}
void OnLeaseDenied(long src, string[] f)
{
if (role != Role.Miner) return;
_ab = false;
if (f.Length < 2) return;
if (f[1] == "done")
{
_m = true;
Log("Dispatcher reports job complete");
_d(MinerState.Inbound);
}
else if (f[1] == "paused")
{
Log("Dispatcher paused — returning");
_d(MinerState.Inbound);
}
}
void OnShaftReport(long src, string[] f)
{
if (role != Role.Dispatcher || f.Length < 7) return;
int idx = _k(f[1], -1);
if (idx < 0 || idx >= cells.Length) return;
if (cells[idx].LeasedBy != 0 && cells[idx].LeasedBy != src) return;
ShaftResult result = (ShaftResult)_k(f[2], 0);
RecordShaftResult(idx, result, DecD(f[3]), DecD(f[4]), DecD(f[5]), f[6] == "1");
DroneRecord r;
if (fleet.TryGetValue(src, out r)) r.LeasedCell = -1;
}
void OnDockRequest(long src, string[] f)
{
if (role != Role.Dispatcher) return;
foreach (var kv in dockSlotOwner)
if (kv.Value == src) { GrantDock(src, kv.Key); return; }
for (int slot = 0; slot < dockSlots; slot++)
{
if (dockSlotOwner.ContainsKey(slot)) continue;
dockSlotOwner[slot] = src;
DroneRecord r;
if (fleet.TryGetValue(src, out r)) r.DockSlot = slot;
GrantDock(src, slot);
return;
}
GrantDock(src, -1);
}
void OnDockGrant(long src, string[] f)
{
if (role != Role.Miner || f.Length < 2) return;
myDockSlot = _k(f[1], -1);
}
void OnDockRelease(long src, string[] f)
{
if (role != Role.Dispatcher || f.Length < 2) return;
int slot = _k(f[1], -1);
long owner;
if (dockSlotOwner.TryGetValue(slot, out owner) && owner == src)
dockSlotOwner.Remove(slot);
DroneRecord r;
if (fleet.TryGetValue(src, out r)) r.DockSlot = -1;
}
void OnOreSighting(long src, string[] f)
{
if (f.Length < 3) return;
AddSighting(f[1], DecV(f[2]));
}
string ShipLabel()
{
if (shipName.Length > 0) return Sanitize(shipName);
return Sanitize(Me.CubeGrid.CustomName);
}
static string Sanitize(string s)
{
if (string.IsNullOrEmpty(s)) return "?";
return s.Replace('|', '/').Replace(',', ' ');
}
void TickDispatcher()
{
stateTicks++;
_f = "Dispatching";
if (recording) RecordTick();
UpdateOreScan();
if (tick % 30 == 0) SendBeacon();
ExpireLeases();
ExpireDrones();
}
void ExpireLeases()
{
if (cells.Length == 0) return;
for (int i = 0; i < cells.Length; i++)
{
YieldCell c = cells[i];
if (c.State != CellState.Leased) continue;
if (c.LeaseExpiresTick == 0 || tick < c.LeaseExpiresTick) continue;
Log("Lease on " + CellLabel(i) + " expired — reissuing");
c.State = c.MetresDrilled > 0.5f ? CellState.Rich : CellState.Unknown;
c.LeasedBy = 0;
c.LeaseExpiresTick = 0;
}
}
void ExpireDrones()
{
if (fleet.Count == 0) return;
long limit = (long)(droneTimeout / Math.Max(dt, 0.01));
var lost = new List<long>();
foreach (var kv in fleet)
if (tick - kv.Value.LastSeenTick > limit) lost.Add(kv.Key);
for (int i = 0; i < lost.Count; i++)
{
long addr = lost[i];
DroneRecord r = fleet[addr];
Log("Drone lost: " + r.Name);
if (r.LeasedCell >= 0 && r.LeasedCell < cells.Length)
{
YieldCell c = cells[r.LeasedCell];
if (c.State == CellState.Leased && c.LeasedBy == addr)
{
c.State = c.MetresDrilled > 0.5f ? CellState.Rich : CellState.Unknown;
c.LeasedBy = 0;
}
}
if (r.DockSlot >= 0)
{
long owner;
if (dockSlotOwner.TryGetValue(r.DockSlot, out owner) && owner == addr)
dockSlotOwner.Remove(r.DockSlot);
}
fleet.Remove(addr);
}
if (lost.Count > 0) RepackLanes();
}
void RepackLanes()
{
int i = 0;
foreach (var kv in fleet)
{
kv.Value.Lane = i * laneSpacing;
i++;
}
}
int ActiveDroneCount()
{
int n = 0;
foreach (var kv in fleet)
if (kv.Value.State != MinerState.Idle && kv.Value.State != MinerState.Fault) n++;
return n;
}
double FleetOreTotal()
{
double t = 0;
for (int i = 0; i < cells.Length; i++) t += cells[i].OreKg;
return t;
}
double FleetMetresTotal()
{
double t = 0;
for (int i = 0; i < cells.Length; i++) t += cells[i].MetresDrilled;
return t;
}
static double Clamp(double v, double lo, double hi)
{
if (double.IsNaN(v)) return lo;
return v < lo ? lo : (v > hi ? hi : v);
}
static double ToDegrees(double radians)
{
return radians * 180.0 / Math.PI;
}
static string Fmt(double v, int decimals)
{
if (double.IsNaN(v) || double.IsInfinity(v)) return "-";
bool negative = v < 0;
v = Math.Abs(v);
if (decimals <= 0)
{
long whole = (long)Math.Round(v);
return (negative && whole != 0 ? "-" : "") + whole.ToString();
}
long scale = 1;
for (int i = 0; i < decimals; i++) scale *= 10;
long scaled = (long)Math.Round(v * scale);
long units = scaled / scale;
long frac = scaled % scale;
string fracText = frac.ToString();
while (fracText.Length < decimals) fracText = "0" + fracText;
return (negative && scaled != 0 ? "-" : "") + units.ToString() + "." + fracText;
}
static string FmtMass(double kg)
{
if (kg < 1000) return Fmt(kg, 0) + "kg";
return Fmt(kg / 1000.0, 1) + "t";
}
static string Bar(double fraction, int width)
{
fraction = Clamp(fraction, 0.0, 1.0);
int filled = (int)Math.Round(fraction * width);
var b = new StringBuilder(width + 2);
b.Append('[');
for (int i = 0; i < width; i++) b.Append(i < filled ? '#' : '-');
b.Append(']');
return b.ToString();
}
const double WIRE_SCALE = 1000.0;
static string EncD(double v)
{
if (double.IsNaN(v) || double.IsInfinity(v)) return "0";
double scaled = v * WIRE_SCALE;
if (scaled > 9.0e18) scaled = 9.0e18;
if (scaled < -9.0e18) scaled = -9.0e18;
return ((long)Math.Round(scaled)).ToString();
}
static double DecD(string s)
{
long v;
return long.TryParse(s, out v) ? v / WIRE_SCALE : 0.0;
}
static string EncV(Vector3D v)
{
return EncD(v.X) + "," + EncD(v.Y) + "," + EncD(v.Z);
}
static Vector3D DecV(string s)
{
if (string.IsNullOrEmpty(s)) return Vector3D.Zero;
string[] p = s.Split(',');
if (p.Length < 3) return Vector3D.Zero;
return new Vector3D(DecD(p[0]), DecD(p[1]), DecD(p[2]));
}
static int _k(string s, int fallback)
{
int v;
return int.TryParse(s, out v) ? v : fallback;
}
static double ParseDouble(string s, double fallback)
{
if (string.IsNullOrEmpty(s)) return fallback;
s = s.Trim();
bool negative = s.StartsWith("-");
if (negative || s.StartsWith("+")) s = s.Substring(1);
string wholeText = s, fracText = "";
int dot = s.IndexOfAny(new char[] { '.', ',' });
if (dot >= 0)
{
wholeText = s.Substring(0, dot);
fracText = s.Substring(dot + 1);
}
if (wholeText.Length == 0) wholeText = "0";
long whole;
if (!long.TryParse(wholeText, out whole)) return fallback;
double value = whole;
if (fracText.Length > 0)
{
long frac;
if (long.TryParse(fracText, out frac))
{
double divisor = 1;
for (int i = 0; i < fracText.Length; i++) divisor *= 10;
value += frac / divisor;
}
}
return negative ? -value : value;
}
void Render(bool force = false)
{
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
catch {   }
}
if (force || tick % 12 == 0) RenderSprites();
if (verboseEcho) Echo(lastRender);
}
void RenderMiner()
{
sb.Append(state.ToString()).Append(" — ").Append(_f).Append('\n');
if (recording)
sb.Append("RECORDING  ").Append(path.Count).Append(" points\n");
sb.Append("Cargo  ").Append(Bar(_ad, 12)).Append(' ')
.Append(Fmt(_ad * 100, 0)).Append("%  ").Append(FmtMass(oreAboard)).Append(" ore\n");
sb.Append("Power  ").Append(Bar(_q, 12)).Append(' ')
.Append(Fmt(_q * 100, 0)).Append("%\n");
if (hydrogenTanks.Count > 0)
sb.Append("H2     ").Append(Bar(_i, 12)).Append(' ')
.Append(Fmt(_i * 100, 0)).Append("%\n");
if (_a >= 0)
{
sb.Append("Shaft  ").Append(CellLabel(_a));
if (_h) sb.Append(" probe");
sb.Append("  ").Append(Fmt(_g, 1)).Append('/')
.Append(Fmt(_x, 0)).Append("m\n");
}
if (job.IsSet)
{
sb.Append("Job    ").Append(job.Width).Append('x').Append(job.Height)
.Append(" @").Append(job.Depth).Append("m  ")
.Append(Fmt(JobProgress() * 100, 0)).Append("% done, ")
.Append(RemainingCellCount()).Append(" left\n");
}
if (_b > 0)
{
double margin = RemainingLiftMargin();
sb.Append("Lift   ").Append(FmtMass(shipMass)).Append(" of ")
.Append(FmtMass(_b));
if (margin < 0) sb.Append("  OVER");
sb.Append('\n');
}
if (_y)
{
sb.Append("Fuel   burn ").Append(Fmt(hydroPerMetre * 100000, 2))
.Append("%/km, return needs ").Append(Fmt(FuelToGetHome() * 100, 0)).Append("%\n");
}
sb.Append("Scout  ").Append(ScoutStatus()).Append('\n');
if (flightActive)
sb.Append("Nav    ").Append(Fmt(distToTarget, 1)).Append("m  ")
.Append(Fmt(speed, 1)).Append("m/s  err ")
.Append(Fmt(alignError, 0)).Append("deg\n");
sb.Append("Load   ").Append(Fmt(loadPeak * 100, 0)).Append("% of tick budget\n");
}
void RenderDispatcher()
{
sb.Append(_f).Append("  ").Append(fleet.Count).Append(" drone(s), ")
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
void RenderMap()
{
if (!job.IsSet || cells.Length == 0) return;
sb.Append('\n');
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
sb.Append(idx == _a ? '@' : CellGlyph(cells[idx]));
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
int from = Math.Max(0, log.Count - 6);
for (int i = from; i < log.Count; i++) sb.Append(log[i]).Append('\n');
}
static string Pad(string s, int width)
{
if (s == null) s = "";
if (s.Length >= width) return s.Substring(0, width);
return s + new string(' ', width - s.Length);
}
void HandleCommand(string argument, bool allowRelay = true)
{
if (string.IsNullOrWhiteSpace(argument)) return;
string[] a = argument.Trim().ToLowerInvariant().Split(new char[] { ' ' },
StringSplitOptions.RemoveEmptyEntries);
if (a.Length == 0) return;
switch (a[0])
{
case "start":
case "run":
CmdStart();
break;
case "stop":
case "pause":
_p = false;
Log("Stopped by operator");
if (state != MinerState.Fault && state != MinerState.Idle
&& state != MinerState.Unloading && state != MinerState.Servicing)
_d(MinerState.Inbound);
break;
case "halt":
_p = false;
SafeStop();
_d(MinerState.Idle);
Log("Halted in place");
break;
case "home":
_p = false;
_d(MinerState.Inbound);
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
case "record":
CmdRecord(a);
break;
case "job":
CmdJob(a);
break;
case "mode":
if (a.Length > 1) { depthMode = ParseDepthMode(a[1]); WriteConfig(); Log("Depth mode: " + depthMode); }
break;
case "order":
if (a.Length > 1) { holeOrder = ParseHoleOrder(a[1]); WriteConfig(); Log("Hole order: " + holeOrder); }
break;
case "eject":
if (a.Length > 1) { ejectMode = ParseEjectMode(a[1]); WriteConfig(); Log("Eject: " + ejectMode); }
break;
case "fleet":
if (a.Length > 1 && allowRelay)
{
string rest = string.Join(" ", a, 1, a.Length - 1);
IGC.SendBroadcastMessage(_j, "C|" + rest);
Log("Relayed to fleet: " + rest);
}
break;
case "status":
Log(state + " / " + _f);
break;
case "scan":
oreModProbed = false;
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
_p = true;
_m = false;
SendBeacon();
Log("Dispatching to fleet");
return;
}
Health h = CheckReadiness();
if (!h.Ok) { Log("Cannot start: " + h.Detail); return; }
_p = true;
_m = false;
Log("Started");
if (state == MinerState.Idle) _d(Docked ? MinerState.Undocking : MinerState.Outbound);
}
void CmdReset()
{
RebuildCells();
sightings.Clear();
_a = -1;
_m = false;
_s = false;
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
_ag = false;
_b = -1;
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
Log("job set <w> <h> <depth> | job depth <m> | job size <w> <h> | job here");
return;
}
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
SetJob(_k(a[2], 5), _k(a[3], 5), _k(a[4], 40));
if (role == Role.Dispatcher) SendBeacon();
break;
case "here":
SetJob(job.Width, job.Height, job.Depth);
if (role == Role.Dispatcher) SendBeacon();
break;
case "size":
if (a.Length < 4) { Log("job size <width> <height>"); return; }
job.Width = Math.Max(1, _k(a[2], job.Width));
job.Height = Math.Max(1, _k(a[3], job.Height));
RebuildCells();
_s = false;
Log("Job resized to " + job.Width + "x" + job.Height);
if (role == Role.Dispatcher) SendBeacon();
break;
case "depth":
if (a.Length < 3) { Log("job depth <metres>"); return; }
job.Depth = Math.Max(1, _k(a[2], job.Depth));
Log("Job depth " + job.Depth + "m");
if (role == Role.Dispatcher) SendBeacon();
break;
default:
Log("job set | here | size | depth");
break;
}
}
string SerializeState()
{
var b = new StringBuilder(2048);
b.Append("V|").Append(STORAGE_REV).Append('\n');
b.Append("S|").Append((int)state)
.Append('|').Append(_p ? 1 : 0)
.Append('|').Append(_m ? 1 : 0)
.Append('|').Append(_a)
.Append('|').Append(_s ? 1 : 0)
.Append('\n');
if (job.IsSet)
{
b.Append("J|").Append(job.Width)
.Append('|').Append(job.Height)
.Append('|').Append(job.Depth)
.Append('|').Append(EncD(job.Spacing))
.Append('|').Append(EncV(job.Origin))
.Append('|').Append(EncV(job.Right))
.Append('|').Append(EncV(job.Forward))
.Append('|').Append(EncV(job.Down))
.Append('\n');
}
b.Append("C");
for (int i = 0; i < cells.Length; i++)
{
YieldCell c = cells[i];
if (c.State == CellState.Unknown && c.MetresDrilled < 0.5f && c.StuckCount == 0) continue;
int st = (int)(c.State == CellState.Leased ? CellState.Unknown : c.State);
b.Append('|').Append(i)
.Append(':').Append(st)
.Append(':').Append(EncD(c.OreKg))
.Append(':').Append(EncD(c.MetresDrilled))
.Append(':').Append(EncD(c.DepthReached))
.Append(':').Append(c.StuckCount);
}
b.Append('\n');
for (int i = 0; i < path.Count; i++)
{
Waypoint w = path[i];
b.Append("P|").Append(EncV(w.Position))
.Append('|').Append(EncV(w.Gravity))
.Append('|').Append(EncD(w.Lift))
.Append('\n');
}
if (_ag && homeDock != null)
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
if (!versionOk) { Log("Saved state is from an older version — starting fresh"); return; }
break;
case "S":
if (!versionOk || f.Length < 6) break;
LoadLifecycle(f);
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
_ag = true;
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
}
}
void LoadLifecycle(string[] f)
{
MinerState saved = (MinerState)_k(f[1], 0);
_p = f[2] == "1";
_m = f[3] == "1";
_a = _k(f[4], -1);
_s = f[5] == "1";
state = saved == MinerState.Fault ? MinerState.Fault : MinerState.Idle;
stateEntry = true;
if (saved != MinerState.Idle && saved != MinerState.Fault)
Log("Resumed from " + saved + " — idling, run 'start' to continue");
}
void LoadJob(string[] f)
{
job.IsSet = true;
job.Width = Math.Max(1, _k(f[1], 5));
job.Height = Math.Max(1, _k(f[2], 5));
job.Depth = Math.Max(1, _k(f[3], 40));
job.Spacing = DecD(f[4]);
job.Origin = DecV(f[5]);
job.Right = DecV(f[6]);
job.Forward = DecV(f[7]);
job.Down = DecV(f[8]);
if (!ValidateJobBasis()) return;
RebuildCells();
}
void LoadCells(string[] f)
{
if (cells.Length == 0) return;
for (int i = 1; i < f.Length; i++)
{
string[] p = f[i].Split(':');
if (p.Length < 6) continue;
int idx = _k(p[0], -1);
if (idx < 0 || idx >= cells.Length) continue;
YieldCell c = cells[idx];
c.State = (CellState)_k(p[1], 0);
c.OreKg = (float)DecD(p[2]);
c.MetresDrilled = (float)DecD(p[3]);
c.DepthReached = (float)DecD(p[4]);
c.StuckCount = _k(p[5], 0);
}
}
static readonly Color C_BG      = new Color(8, 12, 16);
static readonly Color C_PANEL   = new Color(18, 26, 34);
static readonly Color C_INK     = new Color(150, 180, 200);
static readonly Color C_DIM     = new Color(70, 90, 105);
static readonly Color C_ACCENT  = new Color(90, 200, 255);
static readonly Color C_WARN    = new Color(255, 170, 60);
static readonly Color C_BAD     = new Color(255, 80, 70);
static readonly Color C_GOOD    = new Color(120, 230, 140);
void RenderSprites()
{
for (int i = 0; i < panels.Count; i++)
{
try { DrawDashboard(panels[i]); }
catch {   }
}
}
void DrawDashboard(IMyTextSurface s)
{
Vector2 size = s.SurfaceSize;
Vector2 origin = (s.TextureSize - size) * 0.5f;
using (MySpriteDrawFrame frame = s.DrawFrame())
{
Fill(frame, origin, size, C_BG);
float pad = size.Y * 0.03f;
float headerH = size.Y * 0.13f;
DrawHeader(frame, origin + new Vector2(pad, pad),
new Vector2(size.X - pad * 2, headerH));
float bodyY = pad * 2 + headerH;
float bodyH = size.Y - bodyY - pad;
bool wide = size.X > size.Y * 1.4f;
float mapW = wide ? (size.X - pad * 3) * 0.58f : size.X - pad * 2;
float mapH = wide ? bodyH : bodyH * 0.62f;
DrawMapPanel(frame, origin + new Vector2(pad, bodyY), new Vector2(mapW, mapH));
Vector2 sidePos = wide
? origin + new Vector2(pad * 2 + mapW, bodyY)
: origin + new Vector2(pad, bodyY + mapH + pad);
Vector2 sideSize = wide
? new Vector2(size.X - mapW - pad * 3, bodyH)
: new Vector2(size.X - pad * 2, bodyH - mapH - pad);
if (role == Role.Dispatcher) DrawFleetPanel(frame, sidePos, sideSize);
else DrawShipPanel(frame, sidePos, sideSize);
}
}
void DrawHeader(MySpriteDrawFrame frame, Vector2 pos, Vector2 size)
{
Fill(frame, pos, size, C_PANEL);
bool fault = state == MinerState.Fault;
Color accent = fault ? C_BAD : C_ACCENT;
Fill(frame, pos, new Vector2(size.Y * 0.10f, size.Y), accent);
float fs = size.Y * 0.030f;
Text(frame, "VEIN", pos + new Vector2(size.Y * 0.28f, size.Y * 0.10f), fs * 1.15f, accent);
string sub = role == Role.Dispatcher
? fleet.Count + " drone" + (fleet.Count == 1 ? "" : "s")
: (HasDispatcher ? "fleet" : "solo");
Text(frame, sub, pos + new Vector2(size.Y * 0.28f, size.Y * 0.55f), fs * 0.62f, C_DIM);
string headline = fault ? faultReason : _f;
if (headline.Length > 34) headline = headline.Substring(0, 33) + "…";
Text(frame, headline, pos + new Vector2(size.X * 0.30f, size.Y * 0.10f), fs * 0.80f,
fault ? C_BAD : C_INK);
if (job.IsSet)
{
Text(frame, Fmt(JobProgress() * 100, 0) + "%",
pos + new Vector2(size.X - size.Y * 0.20f, size.Y * 0.10f), fs * 1.05f, C_INK,
TextAlignment.RIGHT);
float w = size.X - size.Y * 0.40f;
Vector2 barPos = pos + new Vector2(size.X * 0.30f, size.Y * 0.72f);
Fill(frame, barPos, new Vector2(w * 0.62f, size.Y * 0.06f), C_DIM);
Fill(frame, barPos, new Vector2(w * 0.62f * (float)JobProgress(), size.Y * 0.06f), accent);
}
}
void DrawMapPanel(MySpriteDrawFrame frame, Vector2 pos, Vector2 size)
{
Fill(frame, pos, size, C_PANEL);
if (!job.IsSet || cells.Length == 0)
{
Text(frame, "no job set", pos + size * 0.5f, size.Y * 0.06f, C_DIM, TextAlignment.CENTER);
return;
}
float pad = size.Y * 0.05f;
float labelH = size.Y * 0.10f;
Vector2 area = new Vector2(size.X - pad * 2, size.Y - pad * 2 - labelH);
float cell = Math.Min(area.X / job.Width, area.Y / job.Height);
float gap = cell > 8f ? cell * 0.08f : 0f;
Vector2 gridSize = new Vector2(cell * job.Width, cell * job.Height);
Vector2 gridPos = pos + new Vector2(pad, pad) + (area - gridSize) * 0.5f;
float peak = 0.01f;
for (int i = 0; i < cells.Length; i++)
if (cells[i].Yield > peak) peak = cells[i].Yield;
int step = 1;
while ((job.Width / step) * (job.Height / step) > 280) step++;
for (int row = 0; row < job.Height; row += step)
{
for (int col = 0; col < job.Width; col += step)
{
int idx = job.IndexOf(col, row);
Vector2 p = gridPos + new Vector2(col * cell, row * cell);
Fill(frame, p + new Vector2(gap * 0.5f, gap * 0.5f),
new Vector2(cell * step - gap, cell * step - gap), CellColor(cells[idx], peak));
}
}
if (role == Role.Dispatcher)
{
foreach (var kv in fleet)
DrawDroneMarker(frame, gridPos, cell, kv.Value.Position, C_ACCENT);
}
else if (_a >= 0)
{
int col = CellCol(_a), row = CellRow(_a);
Vector2 p = gridPos + new Vector2((col + 0.5f) * cell, (row + 0.5f) * cell);
Sprite(frame, "CircleHollow", p, new Vector2(cell * 1.9f, cell * 1.9f), Color.White);
}
Text(frame, "yield map  ·  peak " + Fmt(peak, 1) + " kg/m",
pos + new Vector2(size.X * 0.5f, size.Y - labelH), size.Y * 0.055f, C_DIM,
TextAlignment.CENTER);
}
static Color CellColor(YieldCell c, float peak)
{
switch (c.State)
{
case CellState.Unknown: return new Color(24, 34, 44);
case CellState.Leased:  return new Color(70, 130, 200);
case CellState.Blocked: return new Color(120, 40, 40);
case CellState.Barren:  return new Color(40, 36, 32);
}
float t = peak > 0 ? Clamped(c.Yield / peak) : 0f;
Color hot = t < 0.5f
? Lerp(new Color(90, 30, 25), new Color(220, 150, 40), t * 2f)
: Lerp(new Color(220, 150, 40), new Color(110, 240, 130), (t - 0.5f) * 2f);
if (c.State == CellState.Exhausted) return Dim(hot, 0.45f);
return hot;
}
void DrawDroneMarker(MySpriteDrawFrame frame, Vector2 gridPos, float cell, Vector3D world, Color col)
{
if (job.Spacing <= 0) return;
Vector3D d = world - job.Origin;
double x = Vector3D.Dot(d, job.Right) / job.Spacing + (job.Width - 1) * 0.5;
double y = Vector3D.Dot(d, job.Forward) / job.Spacing + (job.Height - 1) * 0.5;
if (x < -1 || x > job.Width || y < -1 || y > job.Height) return;
Vector2 p = gridPos + new Vector2((float)(x + 0.5) * cell, (float)(y + 0.5) * cell);
Sprite(frame, "Circle", p, new Vector2(cell * 0.7f, cell * 0.7f), col);
}
void DrawShipPanel(MySpriteDrawFrame frame, Vector2 pos, Vector2 size)
{
Fill(frame, pos, size, C_PANEL);
float pad = size.Y * 0.06f;
float fs = size.Y * 0.055f;
float y = pos.Y + pad;
float rowH = size.Y * 0.145f;
Gauge(frame, new Vector2(pos.X + pad, y), size.X - pad * 2, rowH, "CARGO",
_ad, FmtMass(oreAboard) + " ore", GaugeColor(_ad, true));
y += rowH;
Gauge(frame, new Vector2(pos.X + pad, y), size.X - pad * 2, rowH, "POWER",
_q, Fmt(_q * 100, 0) + "%", GaugeColor(_q, false));
y += rowH;
if (hydrogenTanks.Count > 0)
{
Gauge(frame, new Vector2(pos.X + pad, y), size.X - pad * 2, rowH, "H2",
_i, Fmt(_i * 100, 0) + "%", GaugeColor(_i, false));
y += rowH;
}
y += pad * 0.5f;
if (_a >= 0)
{
string what = (_h ? "probe " : "shaft ") + CellLabel(_a);
Text(frame, what, new Vector2(pos.X + pad, y), fs, C_DIM);
Text(frame, Fmt(Math.Max(0, _g), 1) + " m",
new Vector2(pos.X + size.X - pad, y), fs, C_INK, TextAlignment.RIGHT);
y += rowH * 0.75f;
}
if (_b > 0)
{
bool over = shipMass >= _b;
Text(frame, "lift", new Vector2(pos.X + pad, y), fs, C_DIM);
Text(frame, FmtMass(shipMass) + " / " + FmtMass(_b),
new Vector2(pos.X + size.X - pad, y), fs, over ? C_BAD : C_INK, TextAlignment.RIGHT);
y += rowH * 0.75f;
}
if (_y)
{
double need = FuelToGetHome();
bool tight = _i < need + 0.15;
Text(frame, "return needs", new Vector2(pos.X + pad, y), fs, C_DIM);
Text(frame, Fmt(need * 100, 0) + "% H2",
new Vector2(pos.X + size.X - pad, y), fs, tight ? C_WARN : C_INK,
TextAlignment.RIGHT);
y += rowH * 0.75f;
}
Text(frame, "scout", new Vector2(pos.X + pad, y), fs, C_DIM);
Text(frame, _l ? "ore raycast" : "probe map",
new Vector2(pos.X + size.X - pad, y), fs,
_l ? C_GOOD : C_INK, TextAlignment.RIGHT);
}
void DrawFleetPanel(MySpriteDrawFrame frame, Vector2 pos, Vector2 size)
{
Fill(frame, pos, size, C_PANEL);
float pad = size.Y * 0.05f;
float fs = size.Y * 0.048f;
float y = pos.Y + pad;
Text(frame, FmtMass(FleetOreTotal()) + " recovered", new Vector2(pos.X + pad, y), fs * 1.2f, C_GOOD);
y += size.Y * 0.11f;
Text(frame, Fmt(FleetMetresTotal(), 0) + " m drilled  ·  " + RemainingCellCount() + " shafts left",
new Vector2(pos.X + pad, y), fs * 0.85f, C_DIM);
y += size.Y * 0.10f;
if (fleet.Count == 0)
{
Text(frame, "no drones on channel", new Vector2(pos.X + pad, y), fs, C_WARN);
return;
}
float rowH = Math.Min(size.Y * 0.13f, (size.Y - (y - pos.Y) - pad) / Math.Max(1, fleet.Count));
foreach (var kv in fleet)
{
DroneRecord r = kv.Value;
if (y + rowH > pos.Y + size.Y) break;
Color dot = r.State == MinerState.Fault ? C_BAD
: r.State == MinerState.Idle ? C_DIM : C_GOOD;
Sprite(frame, "Circle", new Vector2(pos.X + pad + fs * 0.4f, y + rowH * 0.35f),
new Vector2(fs * 0.55f, fs * 0.55f), dot);
string name = r.Name.Length > 12 ? r.Name.Substring(0, 12) : r.Name;
Text(frame, name, new Vector2(pos.X + pad + fs * 1.1f, y), fs, C_INK);
Text(frame, r.State.ToString(), new Vector2(pos.X + size.X - pad, y), fs * 0.8f,
C_DIM, TextAlignment.RIGHT);
float barW = size.X - pad * 2 - fs * 1.1f;
float barX = pos.X + pad + fs * 1.1f;
float barY = y + rowH * 0.62f;
Fill(frame, new Vector2(barX, barY), new Vector2(barW, rowH * 0.08f), C_BG);
Fill(frame, new Vector2(barX, barY), new Vector2(barW * r.CargoFill, rowH * 0.08f), C_ACCENT);
Fill(frame, new Vector2(barX, barY + rowH * 0.13f), new Vector2(barW, rowH * 0.08f), C_BG);
Fill(frame, new Vector2(barX, barY + rowH * 0.13f),
new Vector2(barW * r.Battery, rowH * 0.08f), GaugeColor(r.Battery, false));
y += rowH;
}
}
void Gauge(MySpriteDrawFrame frame, Vector2 pos, float w, float h,
string label, double value, string readout, Color col)
{
float fs = h * 0.34f;
Text(frame, label, pos, fs, C_DIM);
Text(frame, readout, new Vector2(pos.X + w, pos.Y), fs, C_INK, TextAlignment.RIGHT);
Vector2 barPos = new Vector2(pos.X, pos.Y + h * 0.52f);
Vector2 barSize = new Vector2(w, h * 0.20f);
Fill(frame, barPos, barSize, C_BG);
Fill(frame, barPos, new Vector2(w * Clamped((float)value), barSize.Y), col);
}
static Color GaugeColor(double v, bool fullIsBad)
{
double t = fullIsBad ? 1.0 - v : v;
if (t > 0.5) return C_GOOD;
if (t > 0.2) return C_WARN;
return C_BAD;
}
static void Fill(MySpriteDrawFrame frame, Vector2 pos, Vector2 size, Color col)
{
frame.Add(new MySprite()
{
Type = SpriteType.TEXTURE,
Data = "SquareSimple",
Position = pos + size * 0.5f,
Size = size,
Color = col,
Alignment = TextAlignment.CENTER
});
}
static void Sprite(MySpriteDrawFrame frame, string id, Vector2 centre, Vector2 size, Color col)
{
frame.Add(new MySprite()
{
Type = SpriteType.TEXTURE,
Data = id,
Position = centre,
Size = size,
Color = col,
Alignment = TextAlignment.CENTER
});
}
static void Text(MySpriteDrawFrame frame, string text, Vector2 pos, float scale, Color col,
TextAlignment align = TextAlignment.LEFT)
{
frame.Add(new MySprite()
{
Type = SpriteType.TEXT,
Data = text,
Position = pos,
RotationOrScale = scale,
Color = col,
Alignment = align,
FontId = "White"
});
}
static float Clamped(float v) { return v < 0f ? 0f : (v > 1f ? 1f : v); }
static float Clamped(double v) { return Clamped((float)v); }
static Color Lerp(Color a, Color b, float t)
{
t = Clamped(t);
return new Color(
(int)(a.R + (b.R - a.R) * t),
(int)(a.G + (b.G - a.G) * t),
(int)(a.B + (b.B - a.B) * t));
}
static Color Dim(Color c, float f)
{
return new Color((int)(c.R * f), (int)(c.G * f), (int)(c.B * f));
}
