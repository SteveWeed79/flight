// VEIN — Vectored Extraction & Intelligent Navigation
// Minified build. Readable source: src/ in the project repository.
// Commands: start | stop | home | clear | record start|stop | job set <w> <h> <d>
const string _gg = "1.0.0";
const string _hg  = "2";
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
public double _nt = 2.4;
public Job() { }
public Vector3D CellMouth(int col, int row, double _aq)
{
double cx = (col - (Width - 1) * 0.5) * _nt;
double cy = (row - (Height - 1) * 0.5) * _nt;
return Origin + Right * cx + Forward * cy - Down * _aq;
}
public Vector3D CellDepth(int col, int row, double _gy)
{
return CellMouth(col, row, 0) + Down * _gy;
}
public int CellCount { get { return Width * Height; } }
public int IndexOf(int col, int row) { return row * Width + col; }
}
public class YieldCell
{
public CellState State = CellState.Unknown;
public float OreKg;
public float _fq;
public float DepthReached;
public long LeasedBy;
public long _mv;
public int StuckCount;
public float Yield
{
get { return _fq > 0.5f ? OreKg / _fq : 0f; }
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
string _gq = "";
string _gs = "";
string _i = "VEIN";
DepthMode depthMode = DepthMode.AutoOre;
HoleOrder holeOrder = HoleOrder.Prospect;
EjectMode ejectMode = EjectMode.Stone;
double _da = 0.15;
double _cm = 0.8;
double _bw = 3.0;
double _dl = 40.0;
double _cu = 0.8;
double _ew = 0.92;
bool _de = false;
bool _gw = true;
double _em = 12.0;
int _et = 2;
double _q = 0.8;
bool _bl = true;
double _db = 500.0;
double _df = 0.30;
double _cp = 0.25;
double _ep = 2.0;
double _bc = 0.80;
double _al = 0.90;
double _ar = 0.80;
double _ay = 25.0;
double _bv = 240.0;
bool _dz = false;
double _bx = 30.0;
double _cq = 12.0;
int _gx = 1;
string _nh = "[VEIN]";
bool _dh = true;
readonly MyIni ini = new MyIni();
const string _nw = "vein.identity";
const string _t = "vein.mining";
const string _bg = "vein.scouting";
const string _ak = "vein.safety";
const string _eg = "vein.fleet";
const string _lz = "vein.display";
void _iq()
{
_cv = "";
string raw = Me.CustomData;
if (string.IsNullOrWhiteSpace(raw))
{
_cw();
return;
}
MyIniParseResult parse;
if (!ini.TryParse(raw, out parse))
{
_cv = "Custom Data line " + parse.LineNo + ": " + parse.Error;
return;
}
role      = ParseRole(ini.Get(_nw, "role").ToString("Miner"));
_gq  = ini.Get(_nw, "shipName").ToString("");
_gs  = ini.Get(_nw, "blockTag").ToString("");
_i = ini.Get(_nw, "channel").ToString("VEIN");
depthMode = ParseDepthMode(ini.Get(_t, "depthMode").ToString("AutoOre"));
holeOrder = ParseHoleOrder(ini.Get(_t, "holeOrder").ToString("Prospect"));
ejectMode = ParseEjectMode(ini.Get(_t, "eject").ToString("Stone"));
_da  = _cz(ini.Get(_t, "shaftOverlap").ToDouble(0.15), 0.0, 0.75);
_cm    = _cz(ini.Get(_t, "drillSpeed").ToDouble(0.8), 0.05, 10.0);
_bw  = _cz(ini.Get(_t, "retreatSpeed").ToDouble(3.0), 0.2, 20.0);
_dl   = _cz(ini.Get(_t, "cruiseSpeed").ToDouble(40.0), 1.0, 300.0);
_cu     = _cz(ini.Get(_t, "dockSpeed").ToDouble(0.8), 0.2, 10.0);
_ew   = _cz(ini.Get(_t, "cargoFullAt").ToDouble(0.92), 0.1, 0.99);
_de = ini.Get(_t, "drillOnRetreat").ToBoolean(false);
_gw     = ini.Get(_t, "followOre").ToBoolean(true);
_em    = _cz(ini.Get(_bg, "probeDepth").ToDouble(12.0), 2.0, 200.0);
_et   = (int)_cz(ini.Get(_bg, "probeStride").ToInt32(2), 1, 8);
_q = _cz(ini.Get(_bg, "barrenThreshold").ToDouble(0.8), 0.0, 100.0);
_bl = ini.Get(_bg, "useOreDetectorMod").ToBoolean(true);
_db  = _cz(ini.Get(_bg, "oreScanRange").ToDouble(500.0), 50.0, 20000.0);
_df    = _cz(ini.Get(_ak, "minBattery").ToDouble(0.30), 0.05, 0.95);
_cp   = _cz(ini.Get(_ak, "minHydrogen").ToDouble(0.25), 0.0, 0.95);
_ep    = Math.Max(0.0, ini.Get(_ak, "minUranium").ToDouble(2.0));
_bc = _cz(ini.Get(_ak, "resumeBattery").ToDouble(0.80), 0.1, 1.0);
_al = _cz(ini.Get(_ak, "resumeHydrogen").ToDouble(0.90), 0.0, 1.0);
_ar = _cz(ini.Get(_ak, "liftSafetyFactor").ToDouble(0.80), 0.2, 1.0);
_ay = _cz(ini.Get(_ak, "transitAltitude").ToDouble(25.0), 2.0, 500.0);
_bv  = Math.Max(0.0, ini.Get(_ak, "stateTimeout").ToDouble(240.0));
_dz  = ini.Get(_ak, "stopOnDamage").ToBoolean(false);
_bx  = _cz(ini.Get(_eg, "droneTimeout").ToDouble(30.0), 5.0, 600.0);
_cq   = _cz(ini.Get(_eg, "laneSpacing").ToDouble(12.0), 3.0, 100.0);
_gx     = (int)_cz(ini.Get(_eg, "dockSlots").ToInt32(1), 1, 32);
_nh        = ini.Get(_lz, "lcdTag").ToString("[VEIN]");
_dh   = ini.Get(_lz, "verboseEcho").ToBoolean(true);
if (_bc <= _df) _bc = Math.Min(1.0, _df + 0.15);
if (_al <= _cp) _al = Math.Min(1.0, _cp + 0.15);
_cw();
}
void _cw()
{
ini.Clear();
ini.Set(_nw, "role", role.ToString());
ini.SetComment(_nw, "role", "Miner or Dispatcher. Miners find a Dispatcher automatically;\nwith none on the channel they just run the job themselves.");
ini.Set(_nw, "shipName", _gq);
ini.SetComment(_nw, "shipName", "Shown on fleet displays. Blank = use the grid name.");
ini.Set(_nw, "blockTag", _gs);
ini.SetComment(_nw, "blockTag", "Only use blocks whose name contains this. Blank = whole grid.\nSet it if the miner docks to a base that also has drills.");
ini.Set(_nw, "channel", _i);
ini.Set(_t, "depthMode", depthMode.ToString());
ini.SetComment(_t, "depthMode", "Fixed    - always drill to the job depth.\nAutoOre  - stop when ore stops arriving (recommended).\nAutoVoid - stop only on breakthrough into empty space.");
ini.Set(_t, "holeOrder", holeOrder.ToString());
ini.SetComment(_t, "holeOrder", "Serpentine - corner to corner, least travel.\nSpiral     - outward from centre.\nProspect   - probe wide, then chase the ore (recommended).");
ini.Set(_t, "eject", ejectMode.ToString());
ini.SetComment(_t, "eject", "Off, Stone, or StoneAndIce. Needs at least one ejector/connector\nin Throw Out mode. Roughly triples time-on-site.");
ini.Set(_t, "shaftOverlap", _da);
ini.SetComment(_t, "shaftOverlap", "0.15 = shafts overlap 15%. Higher clears more rock, digs more holes.");
ini.Set(_t, "drillSpeed", _cm);
ini.SetComment(_t, "drillSpeed", "m/s downward while cutting. SCAM ships 0.6 and PAM warns that\nabove ~2 the drills stop keeping up and you jam. Slow is fast here:\ntime lost to a wedged ship dwarfs time saved cutting quickly.");
ini.Set(_t, "retreatSpeed", _bw);
ini.Set(_t, "cruiseSpeed", _dl);
ini.SetComment(_t, "cruiseSpeed", "Ceiling only. Real speed is capped by whatever the ship can\nactually stop from, given its mass and the local gravity.");
ini.Set(_t, "dockSpeed", _cu);
ini.SetComment(_t, "dockSpeed", "m/s on the final mating run. PAM uses 0.5 and docking is the\nmanoeuvre most likely to go wrong; slower is genuinely better here.");
ini.Set(_t, "cargoFullAt", _ew);
ini.Set(_t, "drillOnRetreat", _de);
ini.Set(_t, "followOre", _gw);
ini.SetComment(_t, "followOre", "When the grid is worked out but the survey shows ore continuing\npast an edge, move the grid that way and keep going. Bounded at\nsix moves so a rich seam cannot walk the ship off the asteroid.");
ini.Set(_bg, "probeDepth", _em);
ini.SetComment(_bg, "probeDepth", "Prospect mode: metres per test shaft before judging a cell.");
ini.Set(_bg, "probeStride", _et);
ini.SetComment(_bg, "probeStride", "Probe every Nth cell to build the first map. 2 is a good default;\n3-4 on a big site you want surveyed fast.");
ini.Set(_bg, "barrenThreshold", _q);
ini.SetComment(_bg, "barrenThreshold", "kg of ore per metre below which a cell is written off.");
ini.Set(_bg, "useOreDetectorMod", _bl);
ini.SetComment(_bg, "useOreDetectorMod", "Auto-detect Racher's 'Ore Detector Raycast' mod and read real ore\ncoordinates from it. Harmless if the mod is absent.");
ini.Set(_bg, "oreScanRange", _db);
ini.Set(_ak, "minBattery", _df);
ini.Set(_ak, "minHydrogen", _cp);
ini.Set(_ak, "minUranium", _ep);
ini.SetComment(_ak, "minUranium", "Kilograms across all reactors. Ignored if the ship has none.\n0 disables the check.");
ini.Set(_ak, "resumeBattery", _bc);
ini.SetComment(_ak, "resumeBattery", "Charge level at which work resumes. SCAM uses 0.8 — the last\nfifth of a charge takes disproportionately long and buys little.");
ini.Set(_ak, "resumeHydrogen", _al);
ini.Set(_ak, "liftSafetyFactor", _ar);
ini.SetComment(_ak, "liftSafetyFactor", "Fraction of measured lift we will spend. 0.8 leaves 20% in hand\nfor a heavy load and a bad angle.");
ini.Set(_ak, "transitAltitude", _ay);
ini.Set(_ak, "stateTimeout", _bv);
ini.SetComment(_ak, "stateTimeout", "Seconds before the watchdog calls a state hung and recovers.\n0 disables it, which is rarely what you want.");
ini.Set(_ak, "stopOnDamage", _dz);
ini.Set(_eg, "droneTimeout", _bx);
ini.SetComment(_eg, "droneTimeout", "Seconds of silence before a drone is presumed lost and its shaft\nis handed to somebody else.");
ini.Set(_eg, "laneSpacing", _cq);
ini.SetComment(_eg, "laneSpacing", "Metres between drone altitude lanes over the site. Must exceed\nthe largest drone's height by a comfortable margin.");
ini.Set(_eg, "dockSlots", _gx);
ini.Set(_lz, "lcdTag", _nh);
ini.Set(_lz, "verboseEcho", _dh);
string _lu = ini.ToString();
if (_lu != Me.CustomData) Me.CustomData = _lu;
}
static bool _mt(string a, string b)
{
if (a == null || b == null) return false;
return a.Trim().ToUpperInvariant() == b.Trim().ToUpperInvariant();
}
Role ParseRole(string s)
{
if (_mt(s, "Dispatcher")) return Role.Dispatcher;
if (_mt(s, "Miner")) return Role.Miner;
_gt(s, "role", "Miner");
return Role.Miner;
}
DepthMode ParseDepthMode(string s)
{
if (_mt(s, "Fixed")) return DepthMode.Fixed;
if (_mt(s, "AutoOre")) return DepthMode.AutoOre;
if (_mt(s, "AutoVoid")) return DepthMode.AutoVoid;
_gt(s, "depthMode", "AutoOre");
return DepthMode.AutoOre;
}
HoleOrder ParseHoleOrder(string s)
{
if (_mt(s, "Serpentine")) return HoleOrder.Serpentine;
if (_mt(s, "Spiral")) return HoleOrder.Spiral;
if (_mt(s, "Prospect")) return HoleOrder.Prospect;
_gt(s, "holeOrder", "Prospect");
return HoleOrder.Prospect;
}
EjectMode ParseEjectMode(string s)
{
if (_mt(s, "Off")) return EjectMode.Off;
if (_mt(s, "Stone")) return EjectMode.Stone;
if (_mt(s, "StoneAndIce") || _mt(s, "StoneIce")) return EjectMode.StoneAndIce;
_gt(s, "eject", "Stone");
return EjectMode.Stone;
}
void _gt(string got, string key, string fallback)
{
if (string.IsNullOrWhiteSpace(got)) return;
_cv = "Custom Data: '" + got.Trim() + "' is not a valid " + key + ", using " + fallback + ".";
}
string _cv = "";
string _h = "Booting";
string _bm = "";
readonly List<string> log = new List<string>();
const int LOG_MAX = 12;
long _fw;
double clock;
double dt = 1.0 / 6.0;
double _iv;
string _eq = "";
IMyShipController controller;
IMyShipConnector dockConnector;
readonly List<IMyGyro> gyros = new List<IMyGyro>();
readonly List<IMyThrust> thrusters = new List<IMyThrust>();
readonly List<IMyShipDrill> drills = new List<IMyShipDrill>();
readonly List<IMyCargoContainer> cargo = new List<IMyCargoContainer>();
readonly List<IMyBatteryBlock> batteries = new List<IMyBatteryBlock>();
readonly List<IMyReactor> reactors = new List<IMyReactor>();
readonly List<IMyGasTank> hydrogenTanks = new List<IMyGasTank>();
readonly List<IMyShipConnector> ejectors = new List<IMyShipConnector>();
readonly List<IMyTextSurface> screens = new List<IMyTextSurface>();
readonly List<IMyTextSurface> panels = new List<IMyTextSurface>();
readonly List<IMyCameraBlock> cameras = new List<IMyCameraBlock>();
readonly List<IMyOreDetector> oreDetectors = new List<IMyOreDetector>();
long _gf = long.MinValue;
const int _hi = 600;
double _ba = 1.0;
double _av = 3.0;
double _dk = 1.4;
Vector3D drillOffset = Vector3D.Zero;
bool _cr;
double _bt = 2.4;
Vector3D selectionOrigin;
readonly List<string> thrusterTypes = new List<string>();
readonly float[,] thrustByAxis = new float[3, 2];
readonly Dictionary<string, float[,]> thrustByType = new Dictionary<string, float[,]>();
readonly List<IMyThrust>[,] thrustBuckets = new List<IMyThrust>[3, 2];
Vector3D shipPos;
Vector3D shipVel;
Vector3D gravity;
double speed;
bool _dc;
double _n;
double _ax;
readonly List<Waypoint> path = new List<Waypoint>();
bool _bd;
Vector3D lastRecordPos;
int _s;
Waypoint homeDock;
Vector3D homeDockForward;
Vector3D homeDockUp;
bool _as;
double _b = -1;
readonly Job job = new Job();
YieldCell[] cells = new YieldCell[0];
int _a = -1;
double _j;
double _ah;
double _ac;
bool _l;
double _ff;
double _f = -1;
double _du;
bool _v;
int _eu;
double _d;
double _g;
int _ch;
int _ct;
int _ae;
bool _x;
double _bz;
double _ce;
double _y;
int _en;
double _az;
int _cj;
int _au;
int _at;
bool _aw;
int _be;
int _am;
double _ed = double.MaxValue;
double _an;
double _ev;
double _w;
double _fd;
double _ai;
double _m;
double _eb;
double _cf;
bool _ad;
double _cs;
Vector3D hydroSamplePos;
double[] pathCumulative = new double[0];
MinerState state = MinerState.Idle;
int _ck;
bool _cl = true;
bool _u;
bool _r;
ShaftResult pendingResult = ShaftResult.Completed;
bool _aj;
IMyBroadcastListener listener;
IMyUnicastListener unicast;
long _c;
long _z;
readonly Dictionary<long, DroneRecord> fleet = new Dictionary<long, DroneRecord>();
double _nf;
int _eo = -1;
readonly Dictionary<int, long> dockSlotOwner = new Dictionary<int, long>();
long _ec;
string _dj = "";
bool _dd;
long _fj;
readonly Dictionary<string, long> lockOwner = new Dictionary<string, long>();
readonly Dictionary<string, List<long>> lockQueue = new Dictionary<string, List<long>>();
readonly Dictionary<string, long> lockExpiry = new Dictionary<string, long>();
readonly List<string> lockScratch = new List<string>();
bool _p;
bool _ea;
readonly List<OreSighting> sightings = new List<OreSighting>();
const int _jg = 64;
int _co;
int _bb;
readonly List<MyInventoryItem> itemScratch = new List<MyInventoryItem>();
readonly List<IMyTerminalBlock> blockScratch = new List<IMyTerminalBlock>();
readonly StringBuilder sb = new StringBuilder();
public Program()
{
Runtime.UpdateFrequency = UpdateFrequency.Update10;
try
{
_iq();
_gc();
LoadState();
SetupIgc();
Log("VEIN " + _gg + " ready (" + role + ")");
}
catch (Exception e)
{
_bm = "Boot failed: " + e.Message;
state = MinerState.Fault;
}
}
public void Save()
{
try { Storage = _ib(); }
catch {   }
}
public void Main(string argument, UpdateType updateSource)
{
try
{
if ((updateSource & (UpdateType.Terminal | UpdateType.Trigger | UpdateType.Script)) != 0
&& !string.IsNullOrWhiteSpace(argument))
{
_fs(argument);
}
PumpIgc();
if ((updateSource & (UpdateType.Update1 | UpdateType.Update10 | UpdateType.Update100)) == 0)
{
Render(true);
return;
}
_fw++;
double elapsed = Runtime.TimeSinceLastRun.TotalSeconds;
dt = _cz(elapsed, 0.008, 0.5);
clock += dt;
if (_fw - _gf > _hi) _gc();
_mj();
if (role == Role.Dispatcher) _hy();
else _np();
Render();
_nn();
}
catch (Exception e)
{
_cn("Unhandled: " + e.Message);
try { _dn(); } catch { }
Render(true);
}
}
void _e(MinerState next)
{
if (state == next)
{
_ck = 0;
_cl = true;
return;
}
state = next;
_ck = 0;
_cl = true;
_cj = 0;
}
void Watchdog()
{
if (_bv <= 0) return;
if (state == MinerState.Idle || state == MinerState.Fault) return;
if (state == MinerState.Servicing) return;
if (_ck * dt < _bv) return;
Log("Watchdog: " + state + " ran over " + Fmt(_bv, 0) + "s");
switch (state)
{
case MinerState.Descending:
case MinerState.Ascending:
_ju();
pendingResult = ShaftResult.Stuck;
_e(MinerState.Ascending);
break;
case MinerState.Outbound:
case MinerState.Approaching:
case MinerState.Selecting:
_e(MinerState.Inbound);
break;
case MinerState.Docking:
_at++;
if (_at >= 3) _cn("Could not dock after 3 attempts");
else { Log("Docking retry " + _at); _e(MinerState.Inbound); }
break;
default:
_cn("State " + state + " timed out");
break;
}
}
void _cn(string why)
{
if (state == MinerState.Fault) return;
_bm = why;
Log("FAULT: " + why);
state = MinerState.Fault;
_ck = 0;
_cl = true;
_u = false;
_ku(ShaftResult.Aborted);
_bi();
_dn();
}
void _mp()
{
_bm = "";
_au = 0;
_at = 0;
_e(MinerState.Idle);
Log("Fault cleared");
}
void _nn()
{
double used = Runtime.MaxInstructionCount > 0
? (double)Runtime.CurrentInstructionCount / Runtime.MaxInstructionCount
: 0.0;
_iv = Math.Max(used, _iv * 0.97);
}
bool _ds(double _fk = 0.6)
{
if (Runtime.MaxInstructionCount <= 0) return false;
return (double)Runtime.CurrentInstructionCount / Runtime.MaxInstructionCount > _fk;
}
void Log(string msg)
{
log.Add(Fmt(clock, 0) + "s  " + msg);
while (log.Count > LOG_MAX) log.RemoveAt(0);
}
void _gc()
{
_gf = _fw;
gyros.Clear(); thrusters.Clear(); drills.Clear(); cargo.Clear();
batteries.Clear(); reactors.Clear(); hydrogenTanks.Clear(); ejectors.Clear();
screens.Clear(); cameras.Clear(); oreDetectors.Clear();
var controllers = new List<IMyShipController>();
GridTerminalSystem.GetBlocksOfType(controllers, _mu);
controller = null;
for (int i = 0; i < controllers.Count; i++)
{
if (controllers[i] is IMyRemoteControl) { controller = controllers[i]; break; }
}
if (controller == null && controllers.Count > 0) controller = controllers[0];
GridTerminalSystem.GetBlocksOfType(gyros, _mu);
GridTerminalSystem.GetBlocksOfType(thrusters, _mu);
GridTerminalSystem.GetBlocksOfType(drills, _mu);
GridTerminalSystem.GetBlocksOfType(cargo, _mu);
GridTerminalSystem.GetBlocksOfType(batteries, _mu);
GridTerminalSystem.GetBlocksOfType(reactors, _mu);
var _kd = new List<IMyGasTank>();
GridTerminalSystem.GetBlocksOfType(_kd, _mu);
for (int i = 0; i < _kd.Count; i++)
{
if (_kd[i].BlockDefinition.SubtypeId.ToUpperInvariant().Contains("HYDROGEN"))
hydrogenTanks.Add(_kd[i]);
}
var connectors = new List<IMyShipConnector>();
GridTerminalSystem.GetBlocksOfType(connectors, _mu);
dockConnector = null;
for (int i = 0; i < connectors.Count; i++)
{
IMyShipConnector c = connectors[i];
if (c.ThrowOut) { ejectors.Add(c); continue; }
if (c.Status == MyShipConnectorStatus.Connected) dockConnector = c;
else if (dockConnector == null) dockConnector = c;
}
GridTerminalSystem.GetBlocksOfType(cameras, _mu);
GridTerminalSystem.GetBlocksOfType(oreDetectors, _mu);
_ih();
_go();
_hm();
_mq();
}
bool _mu(IMyTerminalBlock b)
{
if (!b.IsSameConstructAs(Me)) return false;
if (_gs.Length > 0 && !b.CustomName.Contains(_gs)) return false;
return true;
}
void _ih()
{
panels.Clear();
blockScratch.Clear();
GridTerminalSystem.GetBlocksOfType(blockScratch, b => _mu(b) && b.CustomName.Contains(_nh));
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
void _hm()
{
_cr = Me.CubeGrid.GridSizeEnum == MyCubeSize.Large;
double _ix = _cr ? 2.5 : 0.5;
Vector3I min = Me.CubeGrid.Min, max = Me.CubeGrid.Max;
Vector3D extent = new Vector3D(max.X - min.X + 1, max.Y - min.Y + 1, max.Z - min.Z + 1) * _ix;
_av = Math.Max(1.0, extent.Length() * 0.5);
if (controller == null || drills.Count == 0)
{
_dk = _cr ? 1.9 : 0.65;
drillOffset = Vector3D.Zero;
_bt = Math.Max(0.5, _dk * 2.0 * (1.0 - _da));
return;
}
double _nb = _cr ? 1.9 : 0.65;
MatrixD refInv = MatrixD.Transpose(controller.WorldMatrix.GetOrientation());
Vector3D ctrlPos = controller.GetPosition();
double _fz = 0;
Vector3D offsetSum = Vector3D.Zero;
for (int i = 0; i < drills.Count; i++)
{
Vector3D local = Vector3D.TransformNormal(drills[i].GetPosition() - ctrlPos, refInv);
double _gb = Math.Sqrt(local.X * local.X + local.Y * local.Y);
if (_gb > _fz) _fz = _gb;
offsetSum += local;
}
_dk = _fz + _nb;
drillOffset = offsetSum / drills.Count;
_bt = Math.Max(0.5, _dk * 2.0 * (1.0 - _da));
}
Health CheckReadiness()
{
if (controller == null) return Health.Bad("No Remote Control or cockpit found");
if (gyros.Count == 0) return Health.Bad("No gyroscopes");
if (thrusters.Count == 0) return Health.Bad("No thrusters");
if (drills.Count == 0) return Health.Bad("No drills");
if (dockConnector == null) return Health.Bad("No connector");
if (!job.IsSet) return Health.Bad("No job set — use: job set <w> <h> <depth>");
if (path.Count == 0 && !_as) return Health.Bad("No path recorded — use: record start/stop");
int _ga = 0, offThrust = 0;
for (int i = 0; i < thrusters.Count; i++)
{
if (!thrusters[i].IsFunctional) continue;
if (thrusters[i].Enabled) _ga++; else offThrust++;
}
if (_ga == 0 && offThrust > 0) return Health.Bad("All thrusters switched off");
if (_ga == 0) return Health.Bad("All thrusters damaged");
int _kf = 0;
for (int i = 0; i < gyros.Count; i++) if (gyros[i].IsFunctional) _kf++;
if (_kf == 0) return Health.Bad("All gyroscopes damaged");
if (_b > 0 && _ba > _b)
return Health.Bad("Too heavy for the route: " + Fmt(_ba / 1000.0, 1) + "t of "
+ Fmt(_b / 1000.0, 1) + "t");
return Health.Good();
}
int _gd()
{
int n = 0;
for (int i = 0; i < thrusters.Count; i++) if (!thrusters[i].IsFunctional) n++;
for (int i = 0; i < gyros.Count; i++) if (!gyros[i].IsFunctional) n++;
for (int i = 0; i < drills.Count; i++) if (!drills[i].IsFunctional) n++;
return n;
}
const double _gj = 0.60;
void _go()
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
int _il = 0;
double _ik = Math.Abs(push.X);
if (Math.Abs(push.Y) > _ik) { _il = 1; _ik = Math.Abs(push.Y); }
if (Math.Abs(push.Z) > _ik) { _il = 2; _ik = Math.Abs(push.Z); }
double _nl = _il == 0 ? push.X : (_il == 1 ? push.Y : push.Z);
int sign = _nl >= 0 ? 0 : 1;
thrustBuckets[_il, sign].Add(t);
string type = t.BlockDefinition.SubtypeId;
if (!thrustByType.ContainsKey(type))
{
thrustByType[type] = new float[3, 2];
thrusterTypes.Add(type);
}
}
thrusterTypes.Sort();
_cb();
}
void _cb()
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
double _ex(Vector3D worldDir)
{
if (controller == null) return 0;
if (worldDir.LengthSquared() < 1e-9) return 0;
worldDir = Vector3D.Normalize(worldDir);
Vector3D local = Vector3D.TransformNormal(worldDir, MatrixD.Transpose(controller.WorldMatrix.GetOrientation()));
double _kg = double.MaxValue;
for (int _il = 0; _il < 3; _il++)
{
double c = _il == 0 ? local.X : (_il == 1 ? local.Y : local.Z);
if (Math.Abs(c) < 1e-4) continue;
float cap = thrustByAxis[_il, c >= 0 ? 0 : 1];
if (cap <= 0) return 0;
_kg = Math.Min(_kg, cap / Math.Abs(c));
}
return _kg == double.MaxValue ? 0 : _kg;
}
double _jb(Vector3D dir)
{
double raw = _ex(-dir) / Math.Max(1.0, _ba);
return Math.Max(0.15, raw - gravity.Length());
}
void _jx(Vector3D target, double maxSpeed)
{
_dc = true;
if (controller == null) return;
Vector3D toTarget = target - controller.GetPosition();
_n = toTarget.Length();
Vector3D dir = _n > 1e-4 ? toTarget / _n : Vector3D.Zero;
double _mz = _jb(dir.LengthSquared() > 0 ? dir : Vector3D.Up);
double _kl = Math.Sqrt(2.0 * _mz * Math.Max(0.0, _n)) * _g;
double _mx = Math.Min(maxSpeed, _kl);
if (_ax > 20.0) _mx *= Math.Max(0.15, 1.0 - (_ax - 20.0) / 70.0);
_mx = Math.Min(_mx, 95.0);
_li(dir * _mx);
}
void _li(Vector3D desiredVelWorld)
{
if (controller == null) return;
if (controller.DampenersOverride) controller.DampenersOverride = false;
Vector3D velError = desiredVelWorld - shipVel;
const double TAU = 0.45;
Vector3D desiredAccel = velError / TAU;
double _ja = _ex(desiredAccel) / Math.Max(1.0, _ba);
if (_ja > 0 && desiredAccel.Length() > _ja)
desiredAccel = Vector3D.Normalize(desiredAccel) * _ja;
Vector3D _me = (desiredAccel - gravity) * _ba;
_ms(_me);
}
void _ms(Vector3D worldForce)
{
if (controller == null) return;
Vector3D local = Vector3D.TransformNormal(worldForce, MatrixD.Transpose(controller.WorldMatrix.GetOrientation()));
_hv(0, local.X);
_hv(1, local.Y);
_hv(2, local.Z);
}
void _hv(int _il, double _fi)
{
int pos = _fi >= 0 ? 0 : 1;
int neg = 1 - pos;
float cap = thrustByAxis[_il, pos];
float ratio = cap > 1f ? (float)_cz(Math.Abs(_fi) / cap, 0.0, 1.0) : 0f;
List<IMyThrust> on = thrustBuckets[_il, pos];
if (on != null)
for (int i = 0; i < on.Count; i++)
if (on[i].IsFunctional) on[i].ThrustOverridePercentage = ratio;
List<IMyThrust> off = thrustBuckets[_il, neg];
if (off != null)
for (int i = 0; i < off.Count; i++)
if (off[i].IsFunctional) off[i].ThrustOverridePercentage = 0f;
}
void _ip(Vector3D desiredForward, Vector3D desiredUp)
{
if (controller == null || gyros.Count == 0) return;
if (desiredForward.LengthSquared() < 1e-9)
{
_gi();
_ax = 0;
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
_ax = _no(Math.Acos(_cz(Vector3D.Dot(m.Forward, desiredForward), -1.0, 1.0)));
if (errAxis.LengthSquared() < 1e-6 && _ax > 90.0)
{
Vector3D seed = Math.Abs(m.Forward.Z) < 0.9 ? Vector3D.Forward : Vector3D.Right;
errAxis = Vector3D.Normalize(Vector3D.Cross(m.Forward, seed));
}
Vector3D angVel = controller.GetShipVelocities().AngularVelocity;
const double KP = 3.0;
const double KD = 0.55;
Vector3D command = errAxis * KP - angVel * KD;
double _ng = _cr ? 0.6 : 1.8;
if (command.Length() > _ng) command = Vector3D.Normalize(command) * _ng;
_mr(command);
}
void _mr(Vector3D worldCommand)
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
void _gi()
{
for (int i = 0; i < gyros.Count; i++)
{
IMyGyro g = gyros[i];
if (!g.GyroOverride) continue;
g.GyroOverride = false;
g.Pitch = 0f; g.Yaw = 0f; g.Roll = 0f;
}
}
void _dn()
{
_dc = false;
for (int a = 0; a < 3; a++)
for (int s = 0; s < 2; s++)
{
List<IMyThrust> bucket = thrustBuckets[a, s];
if (bucket == null) continue;
for (int i = 0; i < bucket.Count; i++) bucket[i].ThrustOverridePercentage = 0f;
}
_gi();
_ca(false);
if (controller != null) controller.DampenersOverride = true;
}
void _ca(bool on)
{
for (int i = 0; i < drills.Count; i++)
if (drills[i].Enabled != on) drills[i].Enabled = on;
}
void _ee(bool on)
{
for (int i = 0; i < thrusters.Count; i++)
if (thrusters[i].Enabled != on) thrusters[i].Enabled = on;
}
void _mj()
{
if (controller == null) return;
shipPos = controller.GetPosition();
MyShipVelocities v = controller.GetShipVelocities();
shipVel = v.LinearVelocity;
speed = shipVel.Length();
gravity = controller.GetNaturalGravity();
_ba = Math.Max(1.0, controller.CalculateShipMass().PhysicalMass);
_cb();
_dg();
if (_fw % 6 == 0) _hf();
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
const string _lx = "MyObjectBuilder_Ore";
const string _kr = "Stone";
const string SUB_ICE = "Ice";
void _dg()
{
double vol = 0, maxVol = 0;
_ai = 0;
for (int i = 0; i < cargo.Count; i++)
_dy(cargo[i].GetInventory(0), ref vol, ref maxVol);
for (int i = 0; i < drills.Count; i++)
_dy(drills[i].GetInventory(0), ref vol, ref maxVol);
_an = maxVol > 0 ? vol / maxVol : 0;
_ev = vol;
double stored = 0, capacity = 0;
for (int i = 0; i < batteries.Count; i++)
{
IMyBatteryBlock b = batteries[i];
if (!b.IsFunctional) continue;
stored += b.CurrentStoredPower;
capacity += b.MaxStoredPower;
}
_w = capacity > 0 ? stored / capacity : 1.0;
double gas = 0; int _kd = 0;
for (int i = 0; i < hydrogenTanks.Count; i++)
{
if (!hydrogenTanks[i].IsFunctional) continue;
gas += hydrogenTanks[i].FilledRatio;
_kd++;
}
_m = _kd > 0 ? gas / _kd : 1.0;
}
void _dy(IMyInventory inv, ref double vol, ref double maxVol)
{
if (inv == null) return;
double cur = (double)inv.CurrentVolume, max = (double)inv.MaxVolume;
vol += cur;
maxVol += max;
if (max > 0) _ai = Math.Max(_ai, cur / max);
}
void _hf()
{
_fd = 0;
for (int i = 0; i < reactors.Count; i++)
{
IMyInventory inv = reactors[i].GetInventory(0);
if (inv == null) continue;
itemScratch.Clear();
inv.GetItems(itemScratch);
for (int k = 0; k < itemScratch.Count; k++)
if (itemScratch[k].Type.SubtypeId == "Uranium") _fd += (double)itemScratch[k].Amount;
}
double ore = 0;
for (int i = 0; i < cargo.Count; i++)
_fv(cargo[i].GetInventory(0), ref ore);
for (int i = 0; i < drills.Count; i++)
_fv(drills[i].GetInventory(0), ref ore);
_eb = ore;
}
void _fv(IMyInventory inv, ref double ore)
{
if (inv == null) return;
itemScratch.Clear();
inv.GetItems(itemScratch);
for (int i = 0; i < itemScratch.Count; i++)
if (_jw(itemScratch[i].Type)) ore += (double)itemScratch[i].Amount;
}
static bool _jw(MyItemType t)
{
return t.TypeId == _lx && t.SubtypeId != _kr;
}
double _ci()
{
return Math.Max(0.0, _eb - _ff);
}
bool CargoFull
{
get { return _an >= _ew || _ai >= 0.98; }
}
bool _mn()
{
if (ejectMode == EjectMode.Off || ejectors.Count == 0) return true;
bool _lv = false;
for (int e = 0; e < ejectors.Count; e++)
{
IMyShipConnector ej = ejectors[e];
if (!ej.IsFunctional) continue;
IMyInventory dst = ej.GetInventory(0);
if (dst == null) continue;
if (_jj(dst)) _lv = true;
if (_ds(0.75)) break;
}
if (!_lv)
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
bool _jj(IMyInventory dst)
{
bool _fy = false;
for (int i = 0; i < cargo.Count; i++)
if (_is(cargo[i].GetInventory(0), dst)) _fy = true;
for (int i = 0; i < drills.Count; i++)
if (_is(drills[i].GetInventory(0), dst)) _fy = true;
return _fy;
}
bool _is(IMyInventory src, IMyInventory dst)
{
if (src == null || dst == null || src == dst) return false;
if (!src.IsConnectedTo(dst)) return false;
itemScratch.Clear();
src.GetItems(itemScratch);
bool _fy = false;
for (int i = itemScratch.Count - 1; i >= 0; i--)
{
if (!IsWaste(itemScratch[i].Type)) continue;
if (src.TransferItemTo(dst, i, null, true, null)) _fy = true;
}
return _fy;
}
bool IsWaste(MyItemType t)
{
if (t.TypeId != _lx) return false;
if (t.SubtypeId == _kr) return true;
if (t.SubtypeId == SUB_ICE && ejectMode == EjectMode.StoneAndIce) return true;
return false;
}
bool _gh()
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
return _an < 0.02;
}
bool _fy = false;
for (int d = 0; d < blockScratch.Count; d++)
{
IMyInventory dst = blockScratch[d].GetInventory(0);
if (dst == null) continue;
if ((double)dst.CurrentVolume >= (double)dst.MaxVolume * 0.99) continue;
for (int i = 0; i < cargo.Count; i++)
if (_mb(cargo[i].GetInventory(0), dst)) _fy = true;
for (int i = 0; i < drills.Count; i++)
if (_mb(drills[i].GetInventory(0), dst)) _fy = true;
if (_ds(0.75)) return false;
}
if (_fy) _dg();
return _an < 0.02;
}
bool _mb(IMyInventory src, IMyInventory dst)
{
if (src == null || dst == null || src == dst) return false;
if (!src.IsConnectedTo(dst)) return false;
itemScratch.Clear();
src.GetItems(itemScratch);
bool _fy = false;
for (int i = itemScratch.Count - 1; i >= 0; i--)
if (src.TransferItemTo(dst, i, null, true, null)) _fy = true;
return _fy;
}
void _bf(bool charging)
{
for (int i = 0; i < batteries.Count; i++)
{
IMyBatteryBlock b = batteries[i];
if (!b.IsFunctional) continue;
ChargeMode _mx = charging ? ChargeMode.Recharge : ChargeMode.Auto;
if (b.ChargeMode != _mx) b.ChargeMode = _mx;
}
}
void _cx(bool filling)
{
for (int i = 0; i < hydrogenTanks.Count; i++)
{
IMyGasTank t = hydrogenTanks[i];
if (!t.IsFunctional) continue;
if (t.Stockpile != filling) t.Stockpile = filling;
}
}
void _ha()
{
if (hydrogenTanks.Count == 0) return;
if (hydroSamplePos == Vector3D.Zero)
{
hydroSamplePos = shipPos;
_cs = _m;
return;
}
double _fe = Vector3D.Distance(shipPos, hydroSamplePos);
if (_fe < 150.0) return;
double used = _cs - _m;
hydroSamplePos = shipPos;
_cs = _m;
if (used <= 0) return;
double rate = used / _fe;
_cf = _ad ? _cf * 0.7 + rate * 0.3 : rate;
_ad = true;
}
double _dp()
{
if (!_ad || hydrogenTanks.Count == 0) return 0;
double _bs = _el();
if (_bs <= 0) return 0;
return _bs * _cf * 1.6;
}
bool _ei()
{
double need = _dp();
if (need <= 0) return false;
return _m < need + 0.05;
}
bool _hc()
{
return _w >= _bc && _m >= _al;
}
void _nu(int width, int height, int _gy)
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
job.Depth = Math.Max(1, _gy);
_hm();
job.Spacing = _bt;
_af();
_a = -1;
_r = false;
_v = false;
Log("Job " + job.Width + "x" + job.Height + " @" + job.Depth + "m, pitch "
+ Fmt(job.Spacing, 1) + "m");
}
bool _dv()
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
if (job.Spacing < 0.1 || job.Spacing > 100) job.Spacing = _bt;
return true;
}
void SetJobAt(Vector3D origin, int width, int height, int _gy)
{
_nu(width, height, _gy);
if (!job.IsSet) return;
job.Origin = origin;
Log("Job anchored at supplied coordinates");
}
void _af()
{
cells = new YieldCell[job.CellCount];
for (int i = 0; i < cells.Length; i++) cells[i] = new YieldCell();
}
const int _ks = 48;
int _di;
int _dr(int idx) { return idx % job.Width; }
int _dq(int idx) { return idx / job.Width; }
int _ap()
{
return _ap(shipPos);
}
int _ap(Vector3D origin)
{
selectionOrigin = origin;
if (cells.Length != job.CellCount) _af();
switch (holeOrder)
{
case HoleOrder.Serpentine: _l = false; return _id();
case HoleOrder.Spiral:     _l = false; return _ml();
default:                                          return _kx();
}
}
int _id()
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
int _ml()
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
int _kx()
{
if (!_v)
{
int _nd = _jt();
if (_nd >= 0) { _l = true; return _nd; }
_v = true;
Log("Survey complete: " + _jh() + " rich of " + _eh() + " probed");
}
_l = false;
int _ik = -1;
double _ao = double.MinValue;
int _nm = _lj();
if (_nm >= 0)
{
int ac = _dr(_nm), ar = _dq(_nm);
for (int r = Math.Max(0, ar - 2); r <= Math.Min(job.Height - 1, ar + 2); r++)
for (int c = Math.Max(0, ac - 2); c <= Math.Min(job.Width - 1, ac + 2); c++)
_mc(job.IndexOf(c, r), ref _ik, ref _ao);
}
int window = Math.Min(_ks, cells.Length);
for (int k = 0; k < window; k++)
_mc((_di + k) % cells.Length, ref _ik, ref _ao);
_di = (_di + window) % Math.Max(1, cells.Length);
if (_ik < 0 && _bh() > 0)
{
for (int i = 0; i < cells.Length; i++)
if (cells[i].Available) return i;
}
return _ik;
}
void _mc(int idx, ref int _ik, ref double _ao)
{
if (idx < 0 || idx >= cells.Length) return;
if (!cells[idx].Available) return;
double score = _nv(idx);
if (cells[idx].State == CellState.Barren && score < _q) return;
if (score < _ao) return;
_ao = score;
_ik = idx;
}
int _lj()
{
int _ik = -1;
float _kj = 0f;
for (int i = 0; i < cells.Length; i++)
{
if (cells[i].MetresDrilled < 0.5f) continue;
if (cells[i].Yield <= _kj) continue;
_kj = cells[i].Yield;
_ik = i;
}
return _ik;
}
int _er()
{
const double _gk = 8.0;
int _mf = (int)Math.Round(_gk / Math.Max(0.5, job.Spacing));
return Math.Max(1, Math.Max(_et, _mf));
}
int _jt()
{
int _ik = -1;
double _fl = double.MaxValue;
int stride = _er();
for (int row = 0; row < job.Height; row += stride)
{
for (int col = 0; col < job.Width; col += stride)
{
int idx = job.IndexOf(col, row);
if (cells[idx].State != CellState.Unknown) continue;
if (!cells[idx].Available) continue;
double d = Vector3D.DistanceSquared(job.CellMouth(col, row, 0), selectionOrigin);
if (d < _fl) { _fl = d; _ik = idx; }
}
}
return _ik;
}
double _nv(int idx)
{
int col = _dr(idx), row = _dq(idx);
const int _ma = 2;
double _lt = 0, weight = 0;
int c0 = Math.Max(0, col - _ma), c1 = Math.Min(job.Width - 1, col + _ma);
int r0 = Math.Max(0, row - _ma), r1 = Math.Min(job.Height - 1, row + _ma);
for (int r = r0; r <= r1; r++)
{
for (int c = c0; c <= c1; c++)
{
int n = job.IndexOf(c, r);
if (n == idx) continue;
YieldCell _by = cells[n];
if (_by.MetresDrilled < 0.5f) continue;
int dc = c - col, dr = r - row;
double d2 = dc * dc + dr * dr;
double w = 1.0 / (1.0 + d2);
_lt += w * _by.Yield;
weight += w;
}
}
double _iy = weight > 0 ? _lt / weight : _q * 1.5;
_iy += _je(col, row);
double travel = Vector3D.Distance(job.CellMouth(col, row, 0), selectionOrigin);
_iy -= travel * 0.004;
return _iy;
}
double _je(int col, int row)
{
if (sightings.Count == 0) return 0;
Vector3D mouth = job.CellMouth(col, row, 0);
double bonus = 0;
for (int i = 0; i < sightings.Count; i++)
{
Vector3D delta = sightings[i].Position - mouth;
double _mg = Vector3D.Dot(delta, job.Down);
Vector3D _gb = delta - job.Down * _mg;
double lat = _gb.Length();
if (lat > job.Spacing * 4) continue;
if (_mg < -job.Spacing || _mg > job.Depth + 20) continue;
bonus += 25.0 / (1.0 + lat);
}
return bonus;
}
int _jh()
{
int n = 0;
for (int i = 0; i < cells.Length; i++) if (cells[i].State == CellState.Rich) n++;
return n;
}
int _eh()
{
int n = 0;
for (int i = 0; i < cells.Length; i++) if (cells[i].MetresDrilled > 0.5f) n++;
return n;
}
int _bh()
{
int n = 0;
for (int i = 0; i < cells.Length; i++) if (cells[i].Available) n++;
return n;
}
double _do()
{
if (cells.Length == 0) return 0;
int done = 0;
for (int i = 0; i < cells.Length; i++)
if (cells[i].State == CellState.Exhausted || cells[i].State == CellState.Blocked) done++;
return (double)done / cells.Length;
}
bool _kn()
{
if (!_gw || cells.Length == 0) return false;
if (_eu >= _ek)
{
Log("Follow limit reached (" + _ek + ") — stopping here");
return false;
}
int band = Math.Max(1, Math.Min(2, Math.Min(job.Width, job.Height) / 3));
double _ao = 0;
int _dm = -1;
for (int edge = 0; edge < 4; edge++)
{
double sum = 0; int n = 0;
for (int row = 0; row < job.Height; row++)
{
for (int col = 0; col < job.Width; col++)
{
bool inBand =
(edge == 0 && col < band) ||
(edge == 1 && col >= job.Width - band) ||
(edge == 2 && row < band) ||
(edge == 3 && row >= job.Height - band);
if (!inBand) continue;
YieldCell c = cells[job.IndexOf(col, row)];
if (c.MetresDrilled < 0.5f) continue;
sum += c.Yield; n++;
}
}
if (n == 0) continue;
double mean = sum / n;
if (mean > _ao) { _ao = mean; _dm = edge; }
}
if (_dm < 0 || _ao < _q) return false;
Vector3D dir =
_dm == 0 ? -job.Right :
_dm == 1 ?  job.Right :
_dm == 2 ? -job.Forward : job.Forward;
double span = (_dm < 2 ? job.Width : job.Height) * job.Spacing * 0.75;
job.Origin = job.Origin + dir * span;
_af();
_v = false;
_a = -1;
_di = 0;
_eu++;
Log("Ore continues " + EdgeName(_dm) + " (" + Fmt(_ao, 1)
+ " kg/m) — job moved " + Fmt(span, 0) + "m, follow " + _eu
+ "/" + _ek);
return true;
}
static string EdgeName(int edge)
{
switch (edge)
{
case 0: return "left";
case 1: return "right";
case 2: return "back";
default: return "forward";
}
}
const int _ek = 6;
void _bp(int idx, ShaftResult result, double oreKg, double metres,
double depthReached, bool wasProbe)
{
if (idx < 0 || idx >= cells.Length) return;
YieldCell _by = cells[idx];
_by.OreKg += (float)oreKg;
_by.MetresDrilled += (float)metres;
_by.DepthReached = Math.Max(_by.DepthReached, (float)depthReached);
_by.LeasedBy = 0;
_by.LeaseExpiresTick = 0;
switch (result)
{
case ShaftResult.Completed:
if (wasProbe)
_by.State = _by.Yield >= _q ? CellState.Rich : CellState.Barren;
else
_by.State = CellState.Exhausted;
break;
case ShaftResult.OreExhausted:
_by.State = _by.Yield >= _q ? CellState.Exhausted : CellState.Barren;
break;
case ShaftResult.CargoFull:
_by.State = _by.Yield >= _q ? CellState.Rich : CellState.Unknown;
break;
case ShaftResult.Stuck:
_by.StuckCount++;
_by.State = _by.StuckCount >= 3 ? CellState.Blocked : CellState.Unknown;
break;
case ShaftResult.Aborted:
if (_by.State == CellState.Leased) _by.State = CellState.Unknown;
break;
}
if (role == Role.Miner && _c != 0)
_ef(idx, result, oreKg, metres, depthReached, wasProbe);
}
void _ju()
{
if (_a < 0 || _a >= cells.Length) return;
cells[_a].StuckCount++;
if (cells[_a].StuckCount >= 3)
{
cells[_a].State = CellState.Blocked;
Log("Cell " + _dr(_a) + "," + _dq(_a) + " blocked");
}
}
double RecordInterval { get { return Math.Max(5.0, _av * 2.0); } }
void _ia()
{
path.Clear();
_bd = true;
_b = -1;
_ht();
_dt(true);
Log("Recording started");
}
void _fn()
{
if (!_bd) return;
_dt(true);
_bd = false;
_bk();
_cc();
Log("Recorded " + path.Count + " waypoints, "
+ (_b > 0 ? "lift limit " + Fmt(_b / 1000.0, 1) + "t" : "no gravity on route"));
}
void _ht()
{
if (controller == null) return;
if (dockConnector != null)
{
MatrixD c = dockConnector.WorldMatrix;
homeDock = new Waypoint(dockConnector.GetPosition(), gravity, SampleEfficiency(), (float)_fb());
homeDockForward = c.Forward;
homeDockUp = c.Up;
}
else
{
MatrixD m = controller.WorldMatrix;
homeDock = new Waypoint(shipPos, gravity, SampleEfficiency(), (float)_fb());
homeDockForward = m.Forward;
homeDockUp = m.Up;
}
_as = true;
}
void _io()
{
if (!_bd) return;
if (path.Count == 0) { _dt(true); return; }
if (Vector3D.DistanceSquared(shipPos, lastRecordPos) < RecordInterval * RecordInterval) return;
if (path.Count >= 400)
{
if (_bd) { Log("Path full at 400 points — recording stopped"); _fn(); }
return;
}
_dt(false);
}
void _dt(bool _me)
{
if (controller == null) return;
if (!_me && Vector3D.DistanceSquared(shipPos, lastRecordPos) < 1.0) return;
path.Add(new Waypoint(shipPos, gravity, SampleEfficiency(), (float)_fb()));
lastRecordPos = shipPos;
}
double _fb()
{
if (gravity.LengthSquared() < 1e-6) return 0;
return _ex(-Vector3D.Normalize(gravity));
}
float[] SampleEfficiency()
{
if (thrusterTypes.Count == 0) return new float[0];
float[] eff = new float[thrusterTypes.Count];
for (int i = 0; i < thrusterTypes.Count; i++)
{
string type = thrusterTypes[i];
float _kh = 0, nominal = 0;
for (int t = 0; t < thrusters.Count; t++)
{
IMyThrust th = thrusters[t];
if (th.BlockDefinition.SubtypeId != type) continue;
if (!th.IsFunctional) continue;
_kh += th.MaxEffectiveThrust;
nominal += th.MaxThrust;
}
eff[i] = nominal > 0 ? _kh / nominal : -1f;
}
return eff;
}
void _cc()
{
_b = -1;
for (int i = 0; i < path.Count; i++)
{
Waypoint wp = path[i];
double g = wp.Gravity.Length();
if (g < 0.05) continue;
if (wp.Lift <= 0) continue;
double _gz = (wp.Lift / g) * _ar;
if (_b < 0 || _gz < _b) _b = _gz;
}
if (_as && homeDock != null && homeDock.Gravity.Length() >= 0.05 && homeDock.Lift > 0)
{
double _gz = (homeDock.Lift / homeDock.Gravity.Length()) * _ar;
if (_b < 0 || _gz < _b) _b = _gz;
}
}
double _ey()
{
if (_b <= 0) return double.MaxValue;
return _b - _ba;
}
bool _fp()
{
return _b > 0 && _ba >= _b;
}
void _bk()
{
pathCumulative = new double[path.Count];
double total = 0;
for (int i = 0; i < path.Count; i++)
{
if (i > 0) total += Vector3D.Distance(path[i].Position, path[i - 1].Position);
pathCumulative[i] = total;
}
}
double _el()
{
if (path.Count == 0) return 0;
if (pathCumulative.Length != path.Count) _bk();
int idx = Math.Max(0, Math.Min(path.Count - 1, _s));
return pathCumulative[idx] + Vector3D.Distance(shipPos, path[idx].Position);
}
double WaypointReached { get { return Math.Max(4.0, _av * 1.5); } }
int _hl()
{
int _ik = -1;
double _fl = double.MaxValue;
for (int i = 0; i < path.Count; i++)
{
double d = Vector3D.DistanceSquared(path[i].Position, shipPos);
if (d < _fl) { _fl = d; _ik = i; }
}
return _ik;
}
bool _ir(bool outbound)
{
if (path.Count == 0) return true;
_s = Math.Max(0, Math.Min(path.Count - 1, _s));
Waypoint wp = path[_s];
double dist = Vector3D.Distance(shipPos, wp.Position);
if (dist < WaypointReached)
{
int next = outbound ? _s + 1 : _s - 1;
if (next < 0 || next >= path.Count) return true;
_s = next;
wp = path[_s];
}
Vector3D aim = wp.Position;
int ahead = outbound ? _s + 1 : _s - 1;
if (ahead >= 0 && ahead < path.Count && dist > WaypointReached * 1.5)
{
Vector3D toNext = Vector3D.Normalize(path[ahead].Position - wp.Position);
aim = wp.Position + toNext * Math.Min(WaypointReached, dist * 0.3);
}
double _fx = _dl;
int fromEnd = outbound ? path.Count - 1 - _s : _s;
if (fromEnd <= 1) _fx = Math.Min(_fx, 15.0);
_jx(aim, _fx);
Vector3D heading = aim - shipPos;
if (heading.LengthSquared() > 4.0)
_ip(heading, gravity.LengthSquared() > 1e-6 ? -gravity : Vector3D.Zero);
return false;
}
void _hu(bool outbound)
{
if (path.Count == 0) { _s = 0; return; }
int _ne = _hl();
double _iz = Vector3D.Distance(path[_ne].Position, shipPos);
if (_iz < WaypointReached * 4)
_s = _ne;
else
_s = outbound ? 0 : path.Count - 1;
}
void _np()
{
_ck++;
if (_bd) _io();
_ls();
Watchdog();
_fm();
_hx();
_ko();
_gn();
if (!Docked) _ha();
if (!Docked && state != MinerState.Idle && state != MinerState.Fault)
_ee(true);
bool _ab = _cl;
_cl = false;
switch (state)
{
case MinerState.Idle:        StIdle(_ab); break;
case MinerState.Undocking:   _le(_ab); break;
case MinerState.Outbound:    _mi(_ab); break;
case MinerState.Selecting:   _lg(_ab); break;
case MinerState.Approaching: _jc(_ab); break;
case MinerState.Descending:  _kp(_ab); break;
case MinerState.Ascending:   _lh(_ab); break;
case MinerState.Inbound:     _nq(_ab); break;
case MinerState.Docking:     _nr(_ab); break;
case MinerState.Unloading:   _ld(_ab); break;
case MinerState.Servicing:   _lf(_ab); break;
case MinerState.Fault:       StFault(_ab); break;
}
_jf();
}
void StIdle(bool _ab)
{
if (_ab) { _dn(); _h = "Idle"; }
if (!_u || _r) return;
Health h = CheckReadiness();
if (!h.Ok) { _h = "Not ready: " + h.Detail; return; }
_e(Docked ? MinerState.Undocking : MinerState.Outbound);
}
bool Docked
{
get { return dockConnector != null && dockConnector.Status == MyShipConnectorStatus.Connected; }
}
void _le(bool _ab)
{
if (_ab)
{
_h = "Undocking";
_ee(true);
_bf(false);
_cx(false);
if (Docked) dockConnector.Disconnect();
_az = 0;
}
if (Docked) { dockConnector.Disconnect(); return; }
Vector3D away = dockConnector != null
? dockConnector.WorldMatrix.Forward
: (controller != null ? controller.WorldMatrix.Up : Vector3D.Up);
Vector3D clear = homeDock != null ? homeDock.Position : shipPos;
double _fe = Vector3D.Distance(shipPos, clear);
double _fi = _av * 2.5;
if (_fe >= _fi)
{
_hu(true);
_e(MinerState.Outbound);
return;
}
_jx(clear + away * _fi, _cu * 2.0);
}
void _mi(bool _ab)
{
if (_ab) { _h = "Outbound"; _hu(true); _ca(false); }
if (!_aa()) { _e(MinerState.Inbound); return; }
if (_ir(true)) _e(MinerState.Selecting);
}
void _lg(bool _ab)
{
if (_ab)
{
_h = "Choosing shaft";
_a = -1;
_aj = false;
}
if (HasDispatcher)
{
if (!_aj)
{
_kt();
_aj = true;
_ec = _fw;
return;
}
if (_a >= 0) { _aj = false; _it(); return; }
if (_fw - _ec > 60)
{
if (_fw - _z > (long)(_bx / Math.Max(dt, 0.01)))
{
Log("Dispatcher lost — continuing solo");
_c = 0;
}
_aj = false;
}
return;
}
int _by = _ap();
if (_by < 0 && _kn()) _by = _ap();
if (_by < 0)
{
_r = true;
Log("Job complete");
_e(MinerState.Inbound);
return;
}
_a = _by;
cells[_by].State = CellState.Leased;
_it();
}
void _it()
{
_hf();
_j = 0;
_ah = 0;
_ff = _eb;
_ce = 0;
_y = 0;
_en = 0;
_au = 0;
_f = -1;
_ac = _l ? Math.Min(_em, job.Depth) : job.Depth;
_e(MinerState.Approaching);
}
void _jc(bool _ab)
{
if (_a < 0) { _e(MinerState.Selecting); return; }
if (_ab) { _h = "To shaft " + _bj(_a); _ca(false); }
if (!_aa()) { _ag(ShaftResult.Aborted); return; }
int col = _dr(_a), row = _dq(_a);
double _aq = _ay + _nf;
Vector3D above = job.CellMouth(col, row, _aq);
_jx(ControllerTargetFor(above), _dl * 0.5);
_ip(job.Down, job.Forward);
bool overHole = _n < Math.Max(1.5, _av * 0.4);
bool square = _ax < 4.0;
if (!overHole || !square) return;
if (!_gm(_a))
{
_h = "Waiting for airspace " + _in(_a);
return;
}
if (!_kq(_aq)) { _jd(); return; }
_e(MinerState.Descending);
}
bool _kq(double _aq)
{
if (cameras.Count == 0) return true;
double reach = _aq + Math.Min(_ac, 60.0);
double hit;
if (!_km(reach, out hit)) return true;
if (hit < 0) return false;
return hit <= _aq + 8.0;
}
void _jd()
{
Log(_bj(_a) + " is open space — skipping");
_bp(_a, ShaftResult.Completed, 0, 0, 0, _l);
if (_a >= 0 && _a < cells.Length)
cells[_a].State = CellState.Barren;
_bo();
_a = -1;
_e(MinerState.Selecting);
}
void _kp(bool _ab)
{
if (_a < 0) { _e(MinerState.Selecting); return; }
int col = _dr(_a), row = _dq(_a);
if (_ab)
{
_h = (_l ? "Probing " : "Drilling ") + _bj(_a);
_az = _br(col, row);
_cj = 0;
_ce = _ci();
_f = -1;
_du = _ev;
}
_j = _br(col, row);
if (_j > _ah) _ah = _j;
if (_f < 0 && _ev > _du + 0.001)
_f = _j;
bool _lw = _j > -2.0;
_ca(_lw);
double _ki = _lw ? _d : Math.Min(12.0, Math.Max(_bw, 6.0));
if (!_aa()) { _ag(ShaftResult.Aborted); return; }
if (CargoFull || _fp()) { _ag(ShaftResult.CargoFull); return; }
if (_j >= _cy()) { _ag(ShaftResult.Completed); return; }
if (_ig()) { _ag(ShaftResult.OreExhausted); return; }
if (_lw && IsStuck())
{
_kw();
_au++;
if (_au > 3) { _ag(ShaftResult.Stuck); return; }
Log("Stuck at " + Fmt(_j, 1) + "m, backing off (" + _au + "/3)");
Vector3D relief = job.CellDepth(col, row, Math.Max(0, _j - 2.0));
_jx(ControllerTargetFor(relief), _bw);
_ip(job.Down, job.Forward);
_cj = 0;
_az = _j - 2.0;
return;
}
if (_lw) _mk();
double aimDepth = Math.Min(_cy(), Math.Max(_j, 0.0) + 5.0);
Vector3D bite = job.CellDepth(col, row, aimDepth);
_jx(ControllerTargetFor(bite), _ki);
_ip(job.Down, job.Forward);
}
void _lh(bool _ab)
{
if (_ab)
{
_h = "Withdrawing";
_ca(_de);
}
if (_a < 0) { _e(MinerState.Selecting); return; }
int col = _dr(_a), row = _dq(_a);
double _gy = _br(col, row);
double _aq = _ay + _nf;
Vector3D clearOfHole = job.CellMouth(col, row, _aq);
Vector3D exitPoint = _gy > 1.0
? job.CellDepth(col, row, Math.Max(0, _gy - 6.0))
: clearOfHole;
_jx(ControllerTargetFor(exitPoint), _bw);
_ip(job.Down, job.Forward);
if (_gy > 1.0) return;
_ca(false);
_lq();
}
void _lq()
{
ShaftResult result = pendingResult;
_hf();
double ore = _ci();
double cut = _f >= 0
? Math.Max(0.0, _ah - _f)
: 0.0;
if (_f < 0 && result == ShaftResult.Completed)
Log(_bj(_a) + " never reached rock");
_bp(_a, result, ore, cut, cut, _l);
_bo();
_bi();
Log(_bj(_a) + " " + result + ": " + Fmt(ore, 0) + "kg / "
+ Fmt(cut, 1) + "m cut");
_a = -1;
if (!_u) { _e(MinerState.Inbound); return; }
if (result == ShaftResult.Aborted || result == ShaftResult.CargoFull)
{
_e(MinerState.Inbound);
return;
}
if (CargoFull || _fp() || !_aa())
{
_e(MinerState.Inbound);
return;
}
_e(MinerState.Selecting);
}
void _ag(ShaftResult why)
{
pendingResult = why;
_e(MinerState.Ascending);
}
void _gn()
{
if (ejectMode == EjectMode.Off || ejectors.Count == 0) return;
if (Docked) return;
if (state == MinerState.Descending || state == MinerState.Ascending) return;
if (_fw % 20 != 0) return;
if (_ds(0.6)) return;
_mn();
}
void _nq(bool _ab)
{
if (_ab)
{
_h = "Returning";
_ca(false);
_hu(false);
if (HasDispatcher) _lk();
}
if (_ir(false)) _e(MinerState.Docking);
}
void _nr(bool _ab)
{
if (_ab)
{
_h = "Docking";
_ca(false);
_aw = false;
_be = 0;
_am = 0;
_ed = double.MaxValue;
}
if (dockConnector == null) { _cn("No connector to dock with"); return; }
if (!_as) { _cn("No dock recorded"); return; }
if (Docked)
{
_dn();
_at = 0;
_aw = false;
_e(MinerState.Unloading);
return;
}
Vector3D mate = homeDock.Position;
Vector3D _il = homeDockForward;
double _aq = Math.Max(6.0, _av * 2.0);
Vector3D hold = mate + _il * _aq;
Vector3D offAxis = shipPos - mate;
double _mg = Vector3D.Dot(offAxis, _il);
double _gb = (offAxis - _il * _mg).Length();
Vector3D connectorOffset = dockConnector.GetPosition() - shipPos;
_ip(-_il, homeDockUp);
if (_gb > 1.0 || _mg > _aq * 1.4)
{
_aw = false;
_jx(hold - connectorOffset, _cu * 3.0);
return;
}
if (_ax > 10.0 && !_aw)
{
_jx(hold - connectorOffset, _cu);
_h = "Docking — squaring up";
return;
}
double _iu = Vector3D.Distance(shipPos + connectorOffset, mate);
double nearDist = Math.Max(1.5, Math.Min(5.0, _av * 0.3));
if (_iu <= nearDist) _aw = true;
_jx(mate - connectorOffset, _aw ? _cu : _cu * 2.5);
if (dockConnector.Status == MyShipConnectorStatus.Connectable)
{
_be++;
if (_be > 5) dockConnector.Connect();
_am = 0;
return;
}
_be = 0;
double _nc = Math.Round(_iu, 1);
if (_nc < _ed) { _ed = _nc; _am = 0; }
else _am++;
if (_am > 20)
{
Log("Dock approach stalled at " + Fmt(_iu, 1) + "m — backing off");
_at++;
if (_at >= 3) { _cn("Could not dock after 3 attempts"); return; }
_e(MinerState.Inbound);
}
}
void _ld(bool _ab)
{
if (_ab)
{
_h = "Unloading";
_dn();
_bf(true);
_cx(true);
}
if (!Docked) { _e(MinerState.Docking); return; }
if (_gh()) _e(MinerState.Servicing);
}
void _lf(bool _ab)
{
if (_ab)
{
_h = "Charging";
_bf(true);
_cx(true);
_ee(false);
}
if (!Docked) { _e(MinerState.Docking); return; }
if (_fw % 30 == 0) _gh();
if (!_u || _r)
{
_h = _r ? "Job complete — docked" : "Stopped — docked";
_hh();
return;
}
if (!_hc())
{
_h = "Charging " + Fmt(_w * 100, 0) + "% / H2 " + Fmt(_m * 100, 0) + "%";
return;
}
_hh();
_e(MinerState.Undocking);
}
void StFault(bool _ab)
{
if (_ab) _dn();
_h = "FAULT: " + _bm;
}
bool _aa()
{
if (_w < _df) return false;
if (hydrogenTanks.Count > 0 && _m < _cp) return false;
if (reactors.Count > 0 && _ep > 0 && _fd < _ep) return false;
if (_ei()) return false;
return true;
}
double _cy()
{
if (_f < 0) return job.Depth;
return _f + _ac;
}
double _br(int col, int row)
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
if (_j > _az + 0.15)
{
_az = _j;
_cj = 0;
return false;
}
_cj++;
return _cj > 40;
}
bool _ig()
{
if (depthMode == DepthMode.Fixed) return false;
if (_f < 0) { _y = _j; return false; }
if (_j < _f + 6.0) { _y = _j; return false; }
double now = depthMode == DepthMode.AutoOre ? _ci() : _an * 1000.0;
if (now > _ce + 0.5)
{
_ce = now;
_y = _j;
_en = 0;
return false;
}
_en++;
bool _lc = _j - _y > 5.0;
bool dryTime = _en > 60;
return _lc && dryTime;
}
void _ls()
{
if (!_dz) return;
if (state == MinerState.Fault || state == MinerState.Idle) return;
if (_gd() == 0) return;
Log("Damage detected — returning");
if (state != MinerState.Inbound && state != MinerState.Docking
&& state != MinerState.Unloading && state != MinerState.Servicing)
{
if (_a >= 0) _ag(ShaftResult.Aborted);
else _e(MinerState.Inbound);
}
}
string _bj(int idx)
{
if (idx < 0) return "-";
return "[" + _dr(idx) + "," + _dq(idx) + "]";
}
const int _hk = 6;
void _ln()
{
_ea = true;
_p = false;
if (!_bl || oreDetectors.Count == 0) return;
IMyOreDetector d = oreDetectors[0];
try
{
Vector3D _nd = d.GetPosition() + d.WorldMatrix.Forward * 10.0;
d.SetValue("RaycastTarget", _nd);
Vector3D echoed = d.GetValue<Vector3D>("RaycastTarget");
if (Vector3D.DistanceSquared(echoed, _nd) > 1.0) return;
try { d.SetValue("OreBlacklist", "Stone"); } catch { }
_p = true;
Log("Ore Detector Raycast mod found — true ore scouting enabled");
}
catch
{
}
}
void _fm()
{
if (!_ea) _ln();
if (!_p) return;
if (_fw % _hk != 0) return;
if (_ds(0.5)) return;
_hp();
IMyOreDetector d = oreDetectors[0];
if (!d.IsFunctional) return;
double _kk;
try { _kk = d.GetValue<double>("AvailableScanRange"); }
catch { _p = false; return; }
Vector3D target;
double _fi;
if (!_ie(d.GetPosition(), out target, out _fi)) return;
if (_fi > _kk || _fi > _db) return;
try
{
d.SetValue("RaycastTarget", target);
MyDetectedEntityInfo hit = d.GetValue<MyDetectedEntityInfo>("RaycastResult");
if (!hit.IsEmpty() && hit.HitPosition.HasValue)
_hw(hit.Name, hit.HitPosition.Value);
}
catch
{
_p = false;
}
}
bool _ie(Vector3D from, out Vector3D target, out double _bs)
{
target = Vector3D.Zero;
_bs = 0;
if (job.IsSet)
{
int _cd = _co % Math.Max(1, job.CellCount);
int col = _cd % job.Width;
int row = _cd / job.Width;
int _nk = _bb % 4;
double _gy = job.Depth * (0.25 + 0.25 * _nk);
target = job.CellDepth(col, row, _gy);
_bb++;
if (_bb % 4 == 0) _co++;
_bs = Vector3D.Distance(from, target);
return true;
}
double az = (_co % 24) * (Math.PI * 2.0 / 24.0);
double el = ((_bb % 7) - 3) * (Math.PI / 8.0);
Vector3D dir;
Vector3D.CreateFromAzimuthAndElevation(az, el, out dir);
if (controller != null) dir = Vector3D.TransformNormal(dir, controller.WorldMatrix);
_co++;
if (_co % 24 == 0) _bb++;
_bs = Math.Min(_db, 1000.0);
target = from + dir * _bs;
return true;
}
void _hw(string oreType, Vector3D pos)
{
if (string.IsNullOrEmpty(oreType)) return;
if (oreType.ToUpperInvariant().Contains("STONE")) return;
double _gv = Math.Max(4.0, job.Spacing);
for (int i = 0; i < sightings.Count; i++)
{
if (Vector3D.DistanceSquared(sightings[i].Position, pos) < _gv * _gv)
{
sightings[i].Tick = _fw;
return;
}
}
OreSighting s = new OreSighting();
s.OreType = oreType;
s.Position = pos;
s.Tick = _fw;
sightings.Add(s);
while (sightings.Count > _jg) sightings.RemoveAt(0);
Log("Ore: " + oreType + " at " + Fmt(Vector3D.Distance(pos, shipPos), 0) + "m");
if (HasDispatcher) _hd(s);
}
void _hp()
{
if (!job.IsSet || cells.Length == 0) return;
for (int i = sightings.Count - 1; i >= 0; i--)
{
int idx = _kc(sightings[i].Position);
if (idx < 0) continue;
if (cells[idx].State == CellState.Exhausted) sightings.RemoveAt(i);
}
}
int _kc(Vector3D worldPos)
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
bool _km(double maxRange, out double _bs)
{
_bs = -1;
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
_bs = Vector3D.Distance(cam.GetPosition(), hit.HitPosition.Value);
return true;
}
return false;
}
void _mq()
{
for (int i = 0; i < cameras.Count; i++)
if (cameras[i].IsFunctional && !cameras[i].EnableRaycast)
cameras[i].EnableRaycast = true;
}
string _he()
{
if (_p) return "ore-raycast (" + sightings.Count + " leads)";
if (oreDetectors.Count > 0) return "probe map (no ore mod)";
return "probe map";
}
const int _dw = 12;
void SetupIgc()
{
listener = IGC.RegisterBroadcastListener(_i);
listener.SetMessageCallback(_i);
unicast = IGC.UnicastListener;
unicast.SetMessageCallback(_i);
}
void PumpIgc()
{
if (listener == null) return;
int _iw = 0;
while (listener.HasPendingMessage && _iw < _dw)
{
MyIGCMessage m = listener.AcceptMessage();
_fr(m.Source, m.Data as string);
_iw++;
}
while (unicast != null && unicast.HasPendingMessage && _iw < _dw * 2)
{
MyIGCMessage m = unicast.AcceptMessage();
_fr(m.Source, m.Data as string);
_iw++;
}
}
void _fr(long src, string body)
{
if (string.IsNullOrEmpty(body)) return;
if (src == IGC.Me) return;
string[] f = body.Split('|');
if (f.Length == 0) return;
switch (f[0])
{
case "B":  OnBeacon(src, f); break;
case "H":  _lo(src, f); break;
case "LR": _ic(src, f); break;
case "LG": _kv(src, f); break;
case "LD": _jq(src, f); break;
case "SR": _jl(src, f); break;
case "DR": _jr(src, f); break;
case "DG": _lp(src, f); break;
case "DX": _js(src, f); break;
case "OS": _jm(src, f); break;
case "KA": if (role == Role.Dispatcher && f.Length > 1) _jn(src, f[1]); break;
case "KG": if (role == Role.Miner && f.Length > 1) _jp(f[1]); break;
case "KR": if (role == Role.Dispatcher && f.Length > 1) _jo(src, f[1]); break;
case "C":  if (f.Length > 1) _fs(f[1], false); break;
}
}
bool HasDispatcher { get { return role == Role.Miner && _c != 0; } }
void _jf()
{
if (role != Role.Miner || _c == 0) return;
if (_fw % 12 != 0) return;
string body = "H|" + _hb()
+ "|" + (int)state
+ "|" + _jy(_an)
+ "|" + _jy(_w)
+ "|" + _lb(shipPos)
+ "|" + _a;
IGC.SendUnicastMessage(_c, _i, body);
}
void _kt()
{
if (_c == 0) return;
IGC.SendUnicastMessage(_c, _i, "LR|" + _hb());
}
void _ef(int _cd, ShaftResult result, double oreKg, double metres,
double depthReached, bool wasProbe)
{
if (_c == 0) return;
string body = "SR|" + _cd + "|" + (int)result
+ "|" + _jy(oreKg) + "|" + _jy(metres)
+ "|" + _jy(depthReached) + "|" + (wasProbe ? 1 : 0);
IGC.SendUnicastMessage(_c, _i, body);
}
void _lk()
{
if (_c == 0) return;
IGC.SendUnicastMessage(_c, _i, "DR|" + _hb());
}
void _hh()
{
if (_c == 0 || _eo < 0) return;
IGC.SendUnicastMessage(_c, _i, "DX|" + _eo);
_eo = -1;
}
void _hd(OreSighting s)
{
if (_c == 0) return;
IGC.SendUnicastMessage(_c, _i, "OS|" + s.OreType + "|" + _lb(s.Position));
}
void _ku(ShaftResult why)
{
if (_a < 0) return;
if (_c != 0)
_ef(_a, why, _ci(), _ah, _ah, _l);
_bo();
_a = -1;
}
void _bo()
{
if (_a < 0 || _a >= cells.Length) return;
if (cells[_a].State == CellState.Leased) cells[_a].State = CellState.Unknown;
cells[_a].LeasedBy = 0;
}
void _bn()
{
if (!job.IsSet) { IGC.SendBroadcastMessage(_i, "B|0"); return; }
string body = "B|1"
+ "|" + job.Width + "|" + job.Height + "|" + job.Depth
+ "|" + _jy(job.Spacing)
+ "|" + _lb(job.Origin)
+ "|" + _lb(job.Right)
+ "|" + _lb(job.Forward)
+ "|" + _lb(job.Down);
IGC.SendBroadcastMessage(_i, body);
}
void _mm(long to, int _cd, double depthLimit, bool isProbe, double lane)
{
string body = "LG|" + _cd + "|" + _jy(depthLimit)
+ "|" + (isProbe ? 1 : 0) + "|" + _jy(lane);
IGC.SendUnicastMessage(to, _i, body);
}
void _hr(long to, string reason)
{
IGC.SendUnicastMessage(to, _i, "LD|" + reason);
}
void _ho(long to, int _na)
{
IGC.SendUnicastMessage(to, _i, "DG|" + _na);
}
void OnBeacon(long src, string[] f)
{
if (role != Role.Miner) return;
if (_c != 0 && _c != src)
{
if (_fw - _z < 600)
{
Log("Second dispatcher on channel '" + _i + "' — ignoring it");
return;
}
Log("Switching dispatcher — previous one went quiet");
}
_c = src;
_z = _fw;
if (f.Length < 10 || f[1] != "1") return;
int w = _k(f[2], job.Width);
int h = _k(f[3], job.Height);
int d = _k(f[4], job.Depth);
bool reshaped = !job.IsSet || w != job.Width || h != job.Height;
job.IsSet = true;
job.Width = w; job.Height = h; job.Depth = d;
job.Spacing = _kb(f[5]);
job.Origin = DecV(f[6]);
job.Right = DecV(f[7]);
job.Forward = DecV(f[8]);
job.Down = DecV(f[9]);
if (!_dv()) return;
if (reshaped || cells.Length != job.CellCount) _af();
}
void _lo(long src, string[] f)
{
if (role != Role.Dispatcher || f.Length < 7) return;
DroneRecord r;
if (!fleet.TryGetValue(src, out r))
{
r = new DroneRecord();
r.Address = src;
r.Lane = fleet.Count * _cq;
fleet[src] = r;
Log("Drone joined: " + f[1]);
}
r.Name = f[1];
r.State = (MinerState)_k(f[2], 0);
r.CargoFill = (float)_kb(f[3]);
r.Battery = (float)_kb(f[4]);
r.Position = DecV(f[5]);
r.LeasedCell = _k(f[6], -1);
r.LastSeenTick = _fw;
}
void _ic(long src, string[] f)
{
if (role != Role.Dispatcher) return;
if (!job.IsSet) { _hr(src, "nojob"); return; }
if (!_u || _r) { _hr(src, "paused"); return; }
DroneRecord r;
if (!fleet.TryGetValue(src, out r))
{
r = new DroneRecord();
r.Address = src;
r.Name = f.Length > 1 ? f[1] : "?";
r.Lane = fleet.Count * _cq;
fleet[src] = r;
}
r.LastSeenTick = _fw;
int _by = _ap(r.Position.LengthSquared() > 1 ? r.Position : shipPos);
if (_by < 0) { _hr(src, "done"); return; }
cells[_by].State = CellState.Leased;
cells[_by].LeasedBy = src;
cells[_by].LeaseExpiresTick = _fw + (long)(_bx * 2 / Math.Max(dt, 0.01));
r.LeasedCell = _by;
double _kg = _l ? Math.Min(_em, job.Depth) : job.Depth;
_mm(src, _by, _kg, _l, r.Lane);
}
void _kv(long src, string[] f)
{
if (role != Role.Miner || f.Length < 5) return;
_a = _k(f[1], -1);
_ac = _kb(f[2]);
_l = f[3] == "1";
_nf = _kb(f[4]);
if (_a >= 0 && _a < cells.Length)
cells[_a].State = CellState.Leased;
}
void _jq(long src, string[] f)
{
if (role != Role.Miner) return;
_aj = false;
if (f.Length < 2) return;
if (f[1] == "done")
{
_r = true;
Log("Dispatcher reports job complete");
_e(MinerState.Inbound);
}
else if (f[1] == "paused")
{
Log("Dispatcher paused — returning");
_e(MinerState.Inbound);
}
}
void _jl(long src, string[] f)
{
if (role != Role.Dispatcher || f.Length < 7) return;
int idx = _k(f[1], -1);
if (idx < 0 || idx >= cells.Length) return;
if (cells[idx].LeasedBy != 0 && cells[idx].LeasedBy != src) return;
ShaftResult result = (ShaftResult)_k(f[2], 0);
_bp(idx, result, _kb(f[3]), _kb(f[4]), _kb(f[5]), f[6] == "1");
DroneRecord r;
if (fleet.TryGetValue(src, out r)) r.LeasedCell = -1;
}
void _jr(long src, string[] f)
{
if (role != Role.Dispatcher) return;
foreach (var kv in dockSlotOwner)
if (kv.Value == src) { _ho(src, kv.Key); return; }
for (int _na = 0; _na < _gx; _na++)
{
if (dockSlotOwner.ContainsKey(_na)) continue;
dockSlotOwner[_na] = src;
DroneRecord r;
if (fleet.TryGetValue(src, out r)) r.DockSlot = _na;
_ho(src, _na);
return;
}
_ho(src, -1);
}
void _lp(long src, string[] f)
{
if (role != Role.Miner || f.Length < 2) return;
_eo = _k(f[1], -1);
}
void _js(long src, string[] f)
{
if (role != Role.Dispatcher || f.Length < 2) return;
int _na = _k(f[1], -1);
long _gu;
if (dockSlotOwner.TryGetValue(_na, out _gu) && _gu == src)
dockSlotOwner.Remove(_na);
DroneRecord r;
if (fleet.TryGetValue(src, out r)) r.DockSlot = -1;
}
void _jm(long src, string[] f)
{
if (f.Length < 3) return;
_hw(f[1], DecV(f[2]));
}
string _hb()
{
if (_gq.Length > 0) return _ly(_gq);
return _ly(Me.CubeGrid.CustomName);
}
static string _ly(string s)
{
if (string.IsNullOrEmpty(s)) return "?";
return s.Replace('|', '/').Replace(',', ' ');
}
void _hy()
{
_ck++;
_h = "Dispatching";
if (_bd) _io();
_fm();
if (_fw % 30 == 0) _bn();
_ky();
_fa();
_kz();
}
void _ky()
{
if (cells.Length == 0) return;
for (int i = 0; i < cells.Length; i++)
{
YieldCell c = cells[i];
if (c.State != CellState.Leased) continue;
if (c.LeaseExpiresTick == 0 || _fw < c.LeaseExpiresTick) continue;
Log("Lease on " + _bj(i) + " expired — reissuing");
c.State = c.MetresDrilled > 0.5f ? CellState.Rich : CellState.Unknown;
c.LeasedBy = 0;
c.LeaseExpiresTick = 0;
}
}
void _kz()
{
if (fleet.Count == 0) return;
long _kg = (long)(_bx / Math.Max(dt, 0.01));
var lost = new List<long>();
foreach (var kv in fleet)
if (_fw - kv.Value.LastSeenTick > _kg) lost.Add(kv.Key);
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
long _gu;
if (dockSlotOwner.TryGetValue(r.DockSlot, out _gu) && _gu == addr)
dockSlotOwner.Remove(r.DockSlot);
}
_hj(addr);
fleet.Remove(addr);
}
if (lost.Count > 0) _ll();
}
void _ll()
{
int i = 0;
foreach (var kv in fleet)
{
kv.Value.Lane = i * _cq;
i++;
}
}
int _gp()
{
int n = 0;
foreach (var kv in fleet)
if (kv.Value.State != MinerState.Idle && kv.Value.State != MinerState.Fault) n++;
return n;
}
double _ft()
{
double t = 0;
for (int i = 0; i < cells.Length; i++) t += cells[i].OreKg;
return t;
}
double _dx()
{
double t = 0;
for (int i = 0; i < cells.Length; i++) t += cells[i].MetresDrilled;
return t;
}
static double _cz(double v, double lo, double hi)
{
if (double.IsNaN(v)) return lo;
return v < lo ? lo : (v > hi ? hi : v);
}
static double _no(double radians)
{
return radians * 180.0 / Math.PI;
}
static string Fmt(double v, int decimals)
{
if (double.IsNaN(v) || double.IsInfinity(v)) return "-";
bool _fh = v < 0;
v = Math.Abs(v);
if (decimals <= 0)
{
long _mw = (long)Math.Round(v);
return (_fh && _mw != 0 ? "-" : "") + _mw.ToString();
}
long _md = 1;
for (int i = 0; i < decimals; i++) _md *= 10;
long _fg = (long)Math.Round(v * _md);
long units = _fg / _md;
long frac = _fg % _md;
string _cg = frac.ToString();
while (_cg.Length < decimals) _cg = "0" + _cg;
return (_fh && _fg != 0 ? "-" : "") + units.ToString() + "." + _cg;
}
static string _ej(double kg)
{
if (kg < 1000) return Fmt(kg, 0) + "kg";
return Fmt(kg / 1000.0, 1) + "t";
}
static string Bar(double _fk, int width)
{
_fk = _cz(_fk, 0.0, 1.0);
int filled = (int)Math.Round(_fk * width);
var b = new StringBuilder(width + 2);
b.Append('[');
for (int i = 0; i < width; i++) b.Append(i < filled ? '#' : '-');
b.Append(']');
return b.ToString();
}
const double _im = 1000.0;
static string _jy(double v)
{
if (double.IsNaN(v) || double.IsInfinity(v)) return "0";
double _fg = v * _im;
if (_fg > 9.0e18) _fg = 9.0e18;
if (_fg < -9.0e18) _fg = -9.0e18;
return ((long)Math.Round(_fg)).ToString();
}
static double _kb(string s)
{
long v;
return long.TryParse(s, out v) ? v / _im : 0.0;
}
static string _lb(Vector3D v)
{
return _jy(v.X) + "," + _jy(v.Y) + "," + _jy(v.Z);
}
static Vector3D DecV(string s)
{
if (string.IsNullOrEmpty(s)) return Vector3D.Zero;
string[] p = s.Split(',');
if (p.Length < 3) return Vector3D.Zero;
return new Vector3D(_kb(p[0]), _kb(p[1]), _kb(p[2]));
}
static int _k(string s, int fallback)
{
int v;
return int.TryParse(s, out v) ? v : fallback;
}
static double _ez(string s, double fallback)
{
if (string.IsNullOrEmpty(s)) return fallback;
s = s.Trim();
bool _fh = s.StartsWith("-");
if (_fh || s.StartsWith("+")) s = s.Substring(1);
string _fc = s, _cg = "";
int dot = s.IndexOfAny(new char[] { '.', ',' });
if (dot >= 0)
{
_fc = s.Substring(0, dot);
_cg = s.Substring(dot + 1);
}
if (_fc.Length == 0) _fc = "0";
long _mw;
if (!long.TryParse(_fc, out _mw)) return fallback;
double _my = _mw;
if (_cg.Length > 0)
{
long frac;
if (long.TryParse(_cg, out frac))
{
double _nj = 1;
for (int i = 0; i < _cg.Length; i++) _nj *= 10;
_my += frac / _nj;
}
}
return _fh ? -_my : _my;
}
void Render(bool _me = false)
{
if (!_me && _fw % 3 != 0)
{
if (_dh) Echo(_eq);
return;
}
sb.Clear();
sb.Append("VEIN ").Append(_gg).Append("  ").Append(role.ToString());
if (role == Role.Miner) sb.Append(HasDispatcher ? "  [fleet]" : "  [solo]");
sb.Append('\n');
sb.Append("----------------------------------------\n");
if (_cv.Length > 0)
sb.Append("! ").Append(_cv).Append('\n');
if (state == MinerState.Fault)
{
sb.Append("\n*** FAULT ***\n").Append(_bm).Append("\n\n");
sb.Append("Run 'clear' once the cause is fixed.\n");
}
if (role == Role.Miner) _lm();
else _gl();
_nx();
_ny();
_eq = sb.ToString();
for (int i = 0; i < screens.Count; i++)
{
try { screens[i].WriteText(_eq); }
catch {   }
}
if (_me || _fw % 12 == 0) _ji();
if (_dh) Echo(_eq);
}
void _lm()
{
sb.Append(state.ToString()).Append(" — ").Append(_h).Append('\n');
if (_bd)
sb.Append("RECORDING  ").Append(path.Count).Append(" points\n");
sb.Append("Cargo  ").Append(Bar(_an, 12)).Append(' ')
.Append(Fmt(_an * 100, 0)).Append("%  ").Append(_ej(_eb)).Append(" ore\n");
sb.Append("Power  ").Append(Bar(_w, 12)).Append(' ')
.Append(Fmt(_w * 100, 0)).Append("%\n");
if (hydrogenTanks.Count > 0)
sb.Append("H2     ").Append(Bar(_m, 12)).Append(' ')
.Append(Fmt(_m * 100, 0)).Append("%\n");
if (_a >= 0)
{
sb.Append("Shaft  ").Append(_bj(_a));
if (_l) sb.Append(" probe");
sb.Append("  ").Append(Fmt(_j, 1)).Append('/')
.Append(Fmt(_ac, 0)).Append("m\n");
}
if (job.IsSet)
{
sb.Append("Job    ").Append(job.Width).Append('x').Append(job.Height)
.Append(" @").Append(job.Depth).Append("m  ")
.Append(Fmt(_do() * 100, 0)).Append("% done, ")
.Append(_bh()).Append(" left\n");
}
if (_b > 0)
{
double margin = _ey();
sb.Append("Lift   ").Append(_ej(_ba)).Append(" of ")
.Append(_ej(_b));
if (margin < 0) sb.Append("  OVER");
sb.Append('\n');
}
if (reactors.Count > 0)
sb.Append("Uranium ").Append(Fmt(_fd, 1)).Append("kg\n");
if (_ad)
{
sb.Append("Fuel   burn ").Append(Fmt(_cf * 100000, 2))
.Append("%/km, return needs ").Append(Fmt(_dp() * 100, 0)).Append("%\n");
}
sb.Append("Scout  ").Append(_he()).Append('\n');
sb.Append("Tune   ").Append(_ii()).Append('\n');
if (_dc)
sb.Append("Nav    ").Append(Fmt(_n, 1)).Append("m  ")
.Append(Fmt(speed, 1)).Append("m/s  err ")
.Append(Fmt(_ax, 0)).Append("deg\n");
sb.Append("Load   ").Append(Fmt(_iv * 100, 0)).Append("% of tick budget\n");
}
void _gl()
{
sb.Append(_h).Append("  ").Append(fleet.Count).Append(" drone(s), ")
.Append(_gp()).Append(" working\n");
if (job.IsSet)
{
sb.Append("Job    ").Append(job.Width).Append('x').Append(job.Height)
.Append(" @").Append(job.Depth).Append("m  ")
.Append(Fmt(_do() * 100, 0)).Append("% done\n");
sb.Append("Yield  ").Append(_ej(_ft())).Append(" from ")
.Append(Fmt(_dx(), 0)).Append("m drilled\n");
}
else sb.Append("No job set.\n");
sb.Append("Scout  ").Append(_he()).Append('\n');
sb.Append('\n');
foreach (var kv in fleet)
{
DroneRecord r = kv.Value;
sb.Append(Pad(r.Name, 12)).Append(' ')
.Append(Pad(r.State.ToString(), 11)).Append(' ')
.Append(Pad(Fmt(r.CargoFill * 100, 0) + "%", 5))
.Append(Pad(Fmt(r.Battery * 100, 0) + "%", 5));
if (r.LeasedCell >= 0) sb.Append(_bj(r.LeasedCell));
sb.Append('\n');
}
}
void _nx()
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
void _ny()
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
void _fs(string argument, bool _mh = true)
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
_u = false;
Log("Stopped by operator");
if (state != MinerState.Fault && state != MinerState.Idle
&& state != MinerState.Unloading && state != MinerState.Servicing)
_e(MinerState.Inbound);
break;
case "halt":
_u = false;
_dn();
_e(MinerState.Idle);
Log("Halted in place");
break;
case "home":
_u = false;
_e(MinerState.Inbound);
break;
case "clear":
if (state == MinerState.Fault) _mp();
else Log("Nothing to clear");
break;
case "reset":
CmdReset();
break;
case "reload":
_iq();
_gc();
Log("Config and blocks reloaded");
break;
case "record":
CmdRecord(a);
break;
case "job":
CmdJob(a);
break;
case "mode":
if (a.Length > 1) { depthMode = ParseDepthMode(a[1]); _cw(); Log("Depth mode: " + depthMode); }
break;
case "order":
if (a.Length > 1) { holeOrder = ParseHoleOrder(a[1]); _cw(); Log("Hole order: " + holeOrder); }
break;
case "eject":
if (a.Length > 1) { ejectMode = ParseEjectMode(a[1]); _cw(); Log("Eject: " + ejectMode); }
break;
case "fleet":
if (a.Length > 1 && _mh)
{
string rest = string.Join(" ", a, 1, a.Length - 1);
IGC.SendBroadcastMessage(_i, "C|" + rest);
Log("Relayed to fleet: " + rest);
}
break;
case "status":
Log(state + " / " + _h);
break;
case "purge":
if (role == Role.Dispatcher) _jk();
else { _bi(); Log("Released held airspace"); }
break;
case "scan":
_ea = false;
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
_u = true;
_r = false;
_bn();
Log("Dispatching to fleet");
return;
}
Health h = CheckReadiness();
if (!h.Ok) { Log("Cannot start: " + h.Detail); return; }
_u = true;
_r = false;
Log("Started");
if (state == MinerState.Idle) _e(Docked ? MinerState.Undocking : MinerState.Outbound);
}
void CmdReset()
{
_af();
sightings.Clear();
_a = -1;
_r = false;
_v = false;
_au = 0;
Log("Yield map cleared");
}
void CmdRecord(string[] a)
{
if (a.Length < 2) { Log("record start | stop | clear"); return; }
switch (a[1])
{
case "start":
_ia();
break;
case "stop":
case "end":
_fn();
break;
case "clear":
path.Clear();
_bd = false;
_as = false;
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
Log("job set <w> <h> <d> | gps <x> <y> <z> | here | size <w> <h> | depth <m>");
return;
}
if ((a[1] == "set" || a[1] == "here" || a[1] == "gps") && role == Role.Miner
&& state != MinerState.Idle && state != MinerState.Fault && !Docked)
{
Log("Cannot anchor a job while flying — run 'stop' or 'halt' first");
return;
}
switch (a[1])
{
case "set":
if (a.Length < 5) { Log("job set <width> <height> <depth>"); return; }
_nu(_k(a[2], 5), _k(a[3], 5), _k(a[4], 40));
if (role == Role.Dispatcher) _bn();
break;
case "gps":
if (a.Length < 5) { Log("job gps <x> <y> <z> [depth]"); return; }
SetJobAt(new Vector3D(_ez(a[2], 0), _ez(a[3], 0), _ez(a[4], 0)),
job.Width, job.Height, a.Length > 5 ? _k(a[5], job.Depth) : job.Depth);
if (role == Role.Dispatcher) _bn();
break;
case "here":
_nu(job.Width, job.Height, job.Depth);
if (role == Role.Dispatcher) _bn();
break;
case "size":
if (a.Length < 4) { Log("job size <width> <height>"); return; }
job.Width = Math.Max(1, _k(a[2], job.Width));
job.Height = Math.Max(1, _k(a[3], job.Height));
_af();
_v = false;
Log("Job resized to " + job.Width + "x" + job.Height);
if (role == Role.Dispatcher) _bn();
break;
case "depth":
if (a.Length < 3) { Log("job depth <metres>"); return; }
job.Depth = Math.Max(1, _k(a[2], job.Depth));
Log("Job depth " + job.Depth + "m");
if (role == Role.Dispatcher) _bn();
break;
default:
Log("job set | here | size | depth");
break;
}
}
string _ib()
{
var b = new StringBuilder(2048);
b.Append("V|").Append(_hg).Append('\n');
b.Append("S|").Append((int)state)
.Append('|').Append(_u ? 1 : 0)
.Append('|').Append(_r ? 1 : 0)
.Append('|').Append(_a)
.Append('|').Append(_v ? 1 : 0)
.Append('\n');
if (job.IsSet)
{
b.Append("J|").Append(job.Width)
.Append('|').Append(job.Height)
.Append('|').Append(job.Depth)
.Append('|').Append(_jy(job.Spacing))
.Append('|').Append(_lb(job.Origin))
.Append('|').Append(_lb(job.Right))
.Append('|').Append(_lb(job.Forward))
.Append('|').Append(_lb(job.Down))
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
.Append(':').Append(_jy(c.OreKg))
.Append(':').Append(_jy(c.MetresDrilled))
.Append(':').Append(_jy(c.DepthReached))
.Append(':').Append(c.StuckCount);
}
b.Append('\n');
for (int i = 0; i < path.Count; i++)
{
Waypoint w = path[i];
b.Append("P|").Append(_lb(w.Position))
.Append('|').Append(_lb(w.Gravity))
.Append('|').Append(_jy(w.Lift))
.Append('\n');
}
b.Append("A|").Append(_jy(_d))
.Append('|').Append(_jy(_g))
.Append('|').Append(_ct)
.Append('|').Append(_ae)
.Append('\n');
if (_as && homeDock != null)
{
b.Append("D|").Append(_lb(homeDock.Position))
.Append('|').Append(_lb(homeDockForward))
.Append('|').Append(_lb(homeDockUp))
.Append('|').Append(_lb(homeDock.Gravity))
.Append('|').Append(_jy(homeDock.Lift))
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
bool _bu = false;
path.Clear();
for (int i = 0; i < lines.Length; i++)
{
string line = lines[i];
if (line.Length < 2) continue;
string[] f = line.Split('|');
switch (f[0])
{
case "V":
_bu = f.Length > 1 && f[1] == _hg;
if (!_bu) { Log("Saved state is from an older version — starting fresh"); return; }
break;
case "S":
if (!_bu || f.Length < 6) break;
_jv(f);
break;
case "J":
if (!_bu || f.Length < 9) break;
LoadJob(f);
break;
case "C":
if (!_bu) break;
LoadCells(f);
break;
case "P":
if (!_bu || f.Length < 4) break;
path.Add(new Waypoint(DecV(f[1]), DecV(f[2]), new float[0], (float)_kb(f[3])));
break;
case "A":
if (!_bu || f.Length < 5) break;
_d = _kb(f[1]);
_g = _kb(f[2]);
_ct = _k(f[3], 0);
_ae = _k(f[4], 0);
break;
case "D":
if (!_bu || f.Length < 6) break;
homeDock = new Waypoint(DecV(f[1]), DecV(f[4]), new float[0], (float)_kb(f[5]));
homeDockForward = DecV(f[2]);
homeDockUp = DecV(f[3]);
_as = true;
break;
}
}
_bk();
_cc();
if (path.Count > 0 || job.IsSet)
Log("Restored: " + path.Count + " waypoints, " + _eh() + " surveyed cells");
}
catch (Exception e)
{
Log("Could not restore state: " + e.Message);
}
}
void _jv(string[] f)
{
MinerState saved = (MinerState)_k(f[1], 0);
_u = f[2] == "1";
_r = f[3] == "1";
_a = _k(f[4], -1);
_v = f[5] == "1";
state = saved == MinerState.Fault ? MinerState.Fault : MinerState.Idle;
_cl = true;
if (saved != MinerState.Idle && saved != MinerState.Fault)
Log("Resumed from " + saved + " — idling, run 'start' to continue");
}
void LoadJob(string[] f)
{
job.IsSet = true;
job.Width = Math.Max(1, _k(f[1], 5));
job.Height = Math.Max(1, _k(f[2], 5));
job.Depth = Math.Max(1, _k(f[3], 40));
job.Spacing = _kb(f[4]);
job.Origin = DecV(f[5]);
job.Right = DecV(f[6]);
job.Forward = DecV(f[7]);
job.Down = DecV(f[8]);
if (!_dv()) return;
_af();
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
c.OreKg = (float)_kb(p[2]);
c.MetresDrilled = (float)_kb(p[3]);
c.DepthReached = (float)_kb(p[4]);
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
void _ji()
{
for (int i = 0; i < panels.Count; i++)
{
try { _ka(panels[i]); }
catch {   }
}
}
void _ka(IMyTextSurface s)
{
Vector2 size = s.SurfaceSize;
Vector2 origin = (s.TextureSize - size) * 0.5f;
using (MySpriteDrawFrame frame = s.DrawFrame())
{
_lr(frame, origin, size, C_BG);
float pad = size.Y * 0.03f;
float _ni = size.Y * 0.13f;
_mo(frame, origin + new Vector2(pad, pad),
new Vector2(size.X - pad * 2, _ni));
float bodyY = pad * 2 + _ni;
float bodyH = size.Y - bodyY - pad;
bool wide = size.X > size.Y * 1.4f;
float mapW = wide ? (size.X - pad * 3) * 0.58f : size.X - pad * 2;
float mapH = wide ? bodyH : bodyH * 0.62f;
_la(frame, origin + new Vector2(pad, bodyY), new Vector2(mapW, mapH));
Vector2 sidePos = wide
? origin + new Vector2(pad * 2 + mapW, bodyY)
: origin + new Vector2(pad, bodyY + mapH + pad);
Vector2 sideSize = wide
? new Vector2(size.X - mapW - pad * 3, bodyH)
: new Vector2(size.X - pad * 2, bodyH - mapH - pad);
if (role == Role.Dispatcher) _if(frame, sidePos, sideSize);
else _jz(frame, sidePos, sideSize);
}
}
void _mo(MySpriteDrawFrame frame, Vector2 pos, Vector2 size)
{
_lr(frame, pos, size, C_PANEL);
bool fault = state == MinerState.Fault;
Color accent = fault ? C_BAD : C_ACCENT;
_lr(frame, pos, new Vector2(size.Y * 0.10f, size.Y), accent);
float fs = size.Y * 0.030f;
_hz(frame, "VEIN", pos + new Vector2(size.Y * 0.28f, size.Y * 0.10f), fs * 1.15f, accent);
string sub = role == Role.Dispatcher
? fleet.Count + " drone" + (fleet.Count == 1 ? "" : "s")
: (HasDispatcher ? "fleet" : "solo");
_hz(frame, sub, pos + new Vector2(size.Y * 0.28f, size.Y * 0.55f), fs * 0.62f, C_DIM);
string _gr = fault ? _bm : _h;
if (_gr.Length > 34) _gr = _gr.Substring(0, 33) + "…";
_hz(frame, _gr, pos + new Vector2(size.X * 0.30f, size.Y * 0.10f), fs * 0.80f,
fault ? C_BAD : C_INK);
if (job.IsSet)
{
_hz(frame, Fmt(_do() * 100, 0) + "%",
pos + new Vector2(size.X - size.Y * 0.20f, size.Y * 0.10f), fs * 1.05f, C_INK,
TextAlignment.RIGHT);
float w = size.X - size.Y * 0.40f;
Vector2 barPos = pos + new Vector2(size.X * 0.30f, size.Y * 0.72f);
_lr(frame, barPos, new Vector2(w * 0.62f, size.Y * 0.06f), C_DIM);
_lr(frame, barPos, new Vector2(w * 0.62f * (float)_do(), size.Y * 0.06f), accent);
}
}
void _la(MySpriteDrawFrame frame, Vector2 pos, Vector2 size)
{
_lr(frame, pos, size, C_PANEL);
if (!job.IsSet || cells.Length == 0)
{
_hz(frame, "no job set", pos + size * 0.5f, size.Y * 0.06f, C_DIM, TextAlignment.CENTER);
return;
}
float pad = size.Y * 0.05f;
float labelH = size.Y * 0.10f;
Vector2 area = new Vector2(size.X - pad * 2, size.Y - pad * 2 - labelH);
float _by = Math.Min(area.X / job.Width, area.Y / job.Height);
float gap = _by > 8f ? _by * 0.08f : 0f;
Vector2 _ix = new Vector2(_by * job.Width, _by * job.Height);
Vector2 gridPos = pos + new Vector2(pad, pad) + (area - _ix) * 0.5f;
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
Vector2 p = gridPos + new Vector2(col * _by, row * _by);
_lr(frame, p + new Vector2(gap * 0.5f, gap * 0.5f),
new Vector2(_by * step - gap, _by * step - gap), CellColor(cells[idx], peak));
}
}
if (role == Role.Dispatcher)
{
foreach (var kv in fleet)
_hq(frame, gridPos, _by, kv.Value.Position, C_ACCENT);
}
else if (_a >= 0)
{
int col = _dr(_a), row = _dq(_a);
Vector2 p = gridPos + new Vector2((col + 0.5f) * _by, (row + 0.5f) * _by);
_ns(frame, "CircleHollow", p, new Vector2(_by * 1.9f, _by * 1.9f), Color.White);
}
_hz(frame, "yield map  ·  peak " + Fmt(peak, 1) + " kg/m",
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
float t = peak > 0 ? _hs(c.Yield / peak) : 0f;
Color hot = t < 0.5f
? Lerp(new Color(90, 30, 25), new Color(220, 150, 40), t * 2f)
: Lerp(new Color(220, 150, 40), new Color(110, 240, 130), (t - 0.5f) * 2f);
if (c.State == CellState.Exhausted) return Dim(hot, 0.45f);
return hot;
}
void _hq(MySpriteDrawFrame frame, Vector2 gridPos, float _by, Vector3D world, Color col)
{
if (job.Spacing <= 0) return;
Vector3D d = world - job.Origin;
double x = Vector3D.Dot(d, job.Right) / job.Spacing + (job.Width - 1) * 0.5;
double y = Vector3D.Dot(d, job.Forward) / job.Spacing + (job.Height - 1) * 0.5;
if (x < -1 || x > job.Width || y < -1 || y > job.Height) return;
Vector2 p = gridPos + new Vector2((float)(x + 0.5) * _by, (float)(y + 0.5) * _by);
_ns(frame, "Circle", p, new Vector2(_by * 0.7f, _by * 0.7f), col);
}
void _jz(MySpriteDrawFrame frame, Vector2 pos, Vector2 size)
{
_lr(frame, pos, size, C_PANEL);
float pad = size.Y * 0.06f;
float fs = size.Y * 0.055f;
float y = pos.Y + pad;
float _ij = size.Y * 0.145f;
Gauge(frame, new Vector2(pos.X + pad, y), size.X - pad * 2, _ij, "CARGO",
_an, _ej(_eb) + " ore", GaugeColor(_an, true));
y += _ij;
Gauge(frame, new Vector2(pos.X + pad, y), size.X - pad * 2, _ij, "POWER",
_w, Fmt(_w * 100, 0) + "%", GaugeColor(_w, false));
y += _ij;
if (hydrogenTanks.Count > 0)
{
Gauge(frame, new Vector2(pos.X + pad, y), size.X - pad * 2, _ij, "H2",
_m, Fmt(_m * 100, 0) + "%", GaugeColor(_m, false));
y += _ij;
}
y += pad * 0.5f;
if (_a >= 0)
{
string what = (_l ? "probe " : "shaft ") + _bj(_a);
_hz(frame, what, new Vector2(pos.X + pad, y), fs, C_DIM);
_hz(frame, Fmt(Math.Max(0, _j), 1) + " m",
new Vector2(pos.X + size.X - pad, y), fs, C_INK, TextAlignment.RIGHT);
y += _ij * 0.75f;
}
if (_b > 0)
{
bool over = _ba >= _b;
_hz(frame, "lift", new Vector2(pos.X + pad, y), fs, C_DIM);
_hz(frame, _ej(_ba) + " / " + _ej(_b),
new Vector2(pos.X + size.X - pad, y), fs, over ? C_BAD : C_INK, TextAlignment.RIGHT);
y += _ij * 0.75f;
}
if (_ad)
{
double need = _dp();
bool tight = _m < need + 0.15;
_hz(frame, "return needs", new Vector2(pos.X + pad, y), fs, C_DIM);
_hz(frame, Fmt(need * 100, 0) + "% H2",
new Vector2(pos.X + size.X - pad, y), fs, tight ? C_WARN : C_INK,
TextAlignment.RIGHT);
y += _ij * 0.75f;
}
_hz(frame, "scout", new Vector2(pos.X + pad, y), fs, C_DIM);
_hz(frame, _p ? "ore raycast" : "probe map",
new Vector2(pos.X + size.X - pad, y), fs,
_p ? C_GOOD : C_INK, TextAlignment.RIGHT);
}
void _if(MySpriteDrawFrame frame, Vector2 pos, Vector2 size)
{
_lr(frame, pos, size, C_PANEL);
float pad = size.Y * 0.05f;
float fs = size.Y * 0.048f;
float y = pos.Y + pad;
_hz(frame, _ej(_ft()) + " recovered", new Vector2(pos.X + pad, y), fs * 1.2f, C_GOOD);
y += size.Y * 0.11f;
_hz(frame, Fmt(_dx(), 0) + " m drilled  ·  " + _bh() + " shafts left",
new Vector2(pos.X + pad, y), fs * 0.85f, C_DIM);
y += size.Y * 0.10f;
if (fleet.Count == 0)
{
_hz(frame, "no drones on channel", new Vector2(pos.X + pad, y), fs, C_WARN);
return;
}
float _ij = Math.Min(size.Y * 0.13f, (size.Y - (y - pos.Y) - pad) / Math.Max(1, fleet.Count));
foreach (var kv in fleet)
{
DroneRecord r = kv.Value;
if (y + _ij > pos.Y + size.Y) break;
Color dot = r.State == MinerState.Fault ? C_BAD
: r.State == MinerState.Idle ? C_DIM : C_GOOD;
_ns(frame, "Circle", new Vector2(pos.X + pad + fs * 0.4f, y + _ij * 0.35f),
new Vector2(fs * 0.55f, fs * 0.55f), dot);
string name = r.Name.Length > 12 ? r.Name.Substring(0, 12) : r.Name;
_hz(frame, name, new Vector2(pos.X + pad + fs * 1.1f, y), fs, C_INK);
_hz(frame, r.State.ToString(), new Vector2(pos.X + size.X - pad, y), fs * 0.8f,
C_DIM, TextAlignment.RIGHT);
float barW = size.X - pad * 2 - fs * 1.1f;
float barX = pos.X + pad + fs * 1.1f;
float barY = y + _ij * 0.62f;
_lr(frame, new Vector2(barX, barY), new Vector2(barW, _ij * 0.08f), C_BG);
_lr(frame, new Vector2(barX, barY), new Vector2(barW * r.CargoFill, _ij * 0.08f), C_ACCENT);
_lr(frame, new Vector2(barX, barY + _ij * 0.13f), new Vector2(barW, _ij * 0.08f), C_BG);
_lr(frame, new Vector2(barX, barY + _ij * 0.13f),
new Vector2(barW * r.Battery, _ij * 0.08f), GaugeColor(r.Battery, false));
y += _ij;
}
}
void Gauge(MySpriteDrawFrame frame, Vector2 pos, float w, float h,
string label, double _my, string readout, Color col)
{
float fs = h * 0.34f;
_hz(frame, label, pos, fs, C_DIM);
_hz(frame, readout, new Vector2(pos.X + w, pos.Y), fs, C_INK, TextAlignment.RIGHT);
Vector2 barPos = new Vector2(pos.X, pos.Y + h * 0.52f);
Vector2 barSize = new Vector2(w, h * 0.20f);
_lr(frame, barPos, barSize, C_BG);
_lr(frame, barPos, new Vector2(w * _hs((float)_my), barSize.Y), col);
}
static Color GaugeColor(double v, bool fullIsBad)
{
double t = fullIsBad ? 1.0 - v : v;
if (t > 0.5) return C_GOOD;
if (t > 0.2) return C_WARN;
return C_BAD;
}
static void _lr(MySpriteDrawFrame frame, Vector2 pos, Vector2 size, Color col)
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
static void _ns(MySpriteDrawFrame frame, string id, Vector2 centre, Vector2 size, Color col)
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
static void _hz(MySpriteDrawFrame frame, string text, Vector2 pos, float _md, Color col,
TextAlignment align = TextAlignment.LEFT)
{
frame.Add(new MySprite()
{
Type = SpriteType.TEXT,
Data = text,
Position = pos,
RotationOrScale = _md,
Color = col,
Alignment = align,
FontId = "White"
});
}
static float _hs(float v) { return v < 0f ? 0f : (v > 1f ? 1f : v); }
static float _hs(double v) { return _hs((float)v); }
static Color Lerp(Color a, Color b, float t)
{
t = _hs(t);
return new Color(
(int)(a.R + (b.R - a.R) * t),
(int)(a.G + (b.G - a.G) * t),
(int)(a.B + (b.B - a.B) * t));
}
static Color Dim(Color c, float f)
{
return new Color((int)(c.R * f), (int)(c.G * f), (int)(c.B * f));
}
void _hx()
{
if (_d <= 0 || _d > _cm) _d = _cm;
if (_g <= 0 || _g > _gj) _g = _gj;
}
void _kw()
{
_d = Math.Max(_ge, _d * 0.7);
_ch = 0;
_ct++;
Log("Drill speed -> " + Fmt(_d, 2) + " m/s after stall");
}
void _mk()
{
_ch++;
if (_ch < 30) return;
_ch = 0;
if (_d >= _cm) return;
_d = Math.Min(_cm, _d * 1.03);
}
const double _ge = 0.15;
void _ko()
{
if (!_dc || controller == null) { _x = false; return; }
if (_n > 200.0 || speed < 3.0)
{
if (_x && _n > _bz + 8.0) _x = false;
return;
}
if (!_x)
{
_x = true;
_bz = _n;
return;
}
if (_n < _bz) { _bz = _n; return; }
double _ke = _n - _bz;
if (_ke < Math.Max(3.0, _av * 0.5)) return;
_x = false;
_ae++;
_g = Math.Max(_fu, _g * 0.9);
Log("Overshot by " + Fmt(_ke, 1) + "m — braking derate -> "
+ Fmt(_g, 2));
}
const double _fu = 0.25;
string _ii()
{
if (_ct == 0 && _ae == 0) return "nominal";
return "drill " + Fmt(_d, 2) + " (" + _ct + " stalls), brake "
+ Fmt(_g, 2) + " (" + _ae + " over)";
}
const int _fo = 4;
string _in(int _cd)
{
if (_cd < 0 || job.Width <= 0) return "g";
return "s" + (_dr(_cd) / _fo) + "_" + (_dq(_cd) / _fo);
}
bool _gm(int _cd)
{
if (!HasDispatcher) return true;
string _mx = _in(_cd);
if (_dj == _mx) return true;
if (_dj.Length > 0 && _dj != _mx) { _bi(); return false; }
if (!_dd || _fw - _fj > 120)
{
IGC.SendUnicastMessage(_c, _i, "KA|" + _mx);
_dd = true;
_fj = _fw;
}
return false;
}
void _bi()
{
if (_dj.Length == 0) return;
if (HasDispatcher)
IGC.SendUnicastMessage(_c, _i, "KR|" + _dj);
_dj = "";
_dd = false;
}
void _jp(string _o)
{
_dj = _o;
_dd = false;
}
void _jn(long src, string _o)
{
long _gu;
if (lockOwner.TryGetValue(_o, out _gu))
{
if (_gu == src) { _hn(src, _o); return; }
List<long> q;
if (!lockQueue.TryGetValue(_o, out q)) { q = new List<long>(); lockQueue[_o] = q; }
if (!q.Contains(src)) q.Add(src);
return;
}
lockOwner[_o] = src;
lockExpiry[_o] = _fw + (long)(_es / Math.Max(dt, 0.01));
_hn(src, _o);
}
void _jo(long src, string _o)
{
long _gu;
if (!lockOwner.TryGetValue(_o, out _gu) || _gu != src) return;
lockOwner.Remove(_o);
lockExpiry.Remove(_o);
_bq(_o);
}
void _hn(long to, string _o)
{
IGC.SendUnicastMessage(to, _i, "KG|" + _o);
}
void _bq(string _o)
{
List<long> q;
if (!lockQueue.TryGetValue(_o, out q) || q.Count == 0) return;
long next = q[0];
q.RemoveAt(0);
lockOwner[_o] = next;
lockExpiry[_o] = _fw + (long)(_es / Math.Max(dt, 0.01));
_hn(next, _o);
}
void _fa()
{
if (lockOwner.Count == 0) return;
lockScratch.Clear();
foreach (var kv in lockExpiry)
if (_fw >= kv.Value) lockScratch.Add(kv.Key);
for (int i = 0; i < lockScratch.Count; i++)
{
string _o = lockScratch[i];
Log("Airspace lock " + _o + " expired — reissuing");
lockOwner.Remove(_o);
lockExpiry.Remove(_o);
_bq(_o);
}
}
void _hj(long addr)
{
lockScratch.Clear();
foreach (var kv in lockOwner) if (kv.Value == addr) lockScratch.Add(kv.Key);
for (int i = 0; i < lockScratch.Count; i++)
{
lockOwner.Remove(lockScratch[i]);
lockExpiry.Remove(lockScratch[i]);
_bq(lockScratch[i]);
}
foreach (var kv in lockQueue) kv.Value.Remove(addr);
}
void _jk()
{
lockOwner.Clear();
lockExpiry.Clear();
lockQueue.Clear();
Log("All airspace locks purged");
}
const double _es = 180.0;
