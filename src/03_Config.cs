// ============================================================================
//  CONFIGURATION
//
//  Everything lives in the Programmable Block's Custom Data as INI. Edit it,
//  then run the "reload" argument (or just recompile). Missing keys are written
//  back with their defaults, so the block always documents itself.
//
//  Note that the write-back is a full rewrite: keys VEIN does not recognise are
//  dropped, so Custom Data is not a place to keep your own notes.
// ============================================================================

// ---- Identity -------------------------------------------------------------
Role role = Role.Miner;
string shipName = "";
/// <summary>Blocks tagged with this are claimed by VEIN. Empty = use whole grid.</summary>
string blockTag = "";
/// <summary>IGC channel. Change it if two fleets share a server.</summary>
string igcChannel = "VEIN";

// ---- Mining ---------------------------------------------------------------
DepthMode depthMode = DepthMode.AutoOre;
HoleOrder holeOrder = HoleOrder.Prospect;
EjectMode ejectMode = EjectMode.Stone;
/// <summary>Fraction of drill radius that adjacent shafts overlap. 0.15 = 15% overlap.</summary>
double shaftOverlap = 0.15;
/// <summary>m/s while drilling downward. Slow is fast — outrunning the drills just jams you.</summary>
double drillSpeed = 0.8;
/// <summary>m/s while backing out of a shaft.</summary>
double retreatSpeed = 3.0;
/// <summary>m/s in open space along the recorded path.</summary>
double cruiseSpeed = 40.0;
/// <summary>m/s during final dock approach.</summary>
double dockSpeed = 0.8;
/// <summary>Return to base at this cargo fill fraction.</summary>
double cargoFullAt = 0.92;
/// <summary>Leave drills running on the way up. Widens the shaft, costs time.</summary>
bool drillOnRetreat = false;
/// <summary>Learn the cutting speed this hull can actually sustain, starting from
/// <see cref="drillSpeed"/>. Off means the configured value is used verbatim.</summary>
bool adaptiveDrill = true;

// ---- Scouting -------------------------------------------------------------
/// <summary>Metres. How deep a probe shaft goes before we judge the cell.</summary>
double probeDepth = 12.0;
/// <summary>Probe every Nth cell in each axis to build the first map. 2 = every other cell.</summary>
int probeStride = 2;
/// <summary>kg ore per metre below which a cell is called barren.</summary>
double barrenThreshold = 0.8;
/// <summary>Try to use the Ore Detector Raycast mod if it is installed.</summary>
bool useOreDetectorMod = true;
/// <summary>Metres. How far the modded detector rays reach.</summary>
double oreScanRange = 500.0;
/// <summary>Extend the job grid outward when the ore is still rich at its edge,
/// rather than reporting the job complete at a boundary you guessed.</summary>
bool growToOre = true;
/// <summary>Ceiling on cells the job may grow to. Stops a rich seam growing the
/// site until the yield map costs more to score than the mining earns.</summary>
int growLimit = 400;

// ---- Safety ---------------------------------------------------------------
/// <summary>Head home below this battery fraction.</summary>
double minBattery = 0.30;
/// <summary>Head home below this hydrogen fraction.</summary>
double minHydrogen = 0.25;
/// <summary>Head home below this many kilograms of uranium across all reactors.
/// Ignored entirely on a ship with no reactors.</summary>
double minUranium = 2.0;
/// <summary>Resume work above this battery fraction.</summary>
double resumeBattery = 0.80;
/// <summary>Resume work above this hydrogen fraction.</summary>
double resumeHydrogen = 0.90;
/// <summary>Fraction of available lift we are willing to use. Headroom for gusts and mistakes.</summary>
double liftSafetyFactor = 0.80;
/// <summary>Metres above the job plane the ship flies when crossing the site.</summary>
double transitAltitude = 25.0;
/// <summary>Seconds a state may run before the watchdog intervenes. 0 = no limit.</summary>
double stateTimeout = 240.0;
/// <summary>Halt everything rather than risk it, if the ship takes damage mid-job.</summary>
bool stopOnDamage = false;
/// <summary>Learn the braking derate from how close approaches actually come to
/// running out of brakes. Off means <see cref="configBrakeDerate"/> is used verbatim.</summary>
bool adaptiveBraking = true;
/// <summary>Starting point for the derate, and the fixed value when adaptation is
/// off. Both ancestors ship something in this region: PAM 0.70, SCAM 0.50.</summary>
double configBrakeDerate = BRAKE_DERATE;

// ---- Fleet ----------------------------------------------------------------
/// <summary>Seconds without a heartbeat before a drone is presumed lost and its work reissued.</summary>
double droneTimeout = 30.0;
/// <summary>Vertical spacing between drone traffic lanes, metres.</summary>
double laneSpacing = 12.0;
/// <summary>Dispatcher only: how many connectors are available for unloading.</summary>
int dockSlots = 1;
/// <summary>One drone at a time in the airspace over the site, via the
/// dispatcher's mutex. Off falls back to altitude lanes alone.</summary>
bool airspaceLock = true;
/// <summary>Seconds a drone waits for the airspace before going anyway. A
/// dispatcher that stops answering must not be able to park the fleet.</summary>
double lockPatience = 60.0;

// ---- Display --------------------------------------------------------------
/// <summary>LCDs whose name contains this get VEIN output.</summary>
string lcdTag = "[VEIN]";
bool verboseEcho = true;

// ---------------------------------------------------------------------------

readonly MyIni ini = new MyIni();

const string S_ID = "vein.identity";
const string S_MINE = "vein.mining";
const string S_SCOUT = "vein.scouting";
const string S_SAFE = "vein.safety";
const string S_FLEET = "vein.fleet";
const string S_DISP = "vein.display";

/// <summary>
/// Parse Custom Data. Never throws: a malformed file falls back to defaults and
/// raises a warning rather than bricking the block.
/// </summary>
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
    igcChannel = ini.Get(S_ID, "channel").ToString("VEIN");

    depthMode = ParseDepthMode(ini.Get(S_MINE, "depthMode").ToString("AutoOre"));
    holeOrder = ParseHoleOrder(ini.Get(S_MINE, "holeOrder").ToString("Prospect"));
    ejectMode = ParseEjectMode(ini.Get(S_MINE, "eject").ToString("Stone"));
    shaftOverlap  = Clamp(ini.Get(S_MINE, "shaftOverlap").ToDouble(0.15), 0.0, 0.75);
    drillSpeed    = Clamp(ini.Get(S_MINE, "drillSpeed").ToDouble(0.8), 0.05, 10.0);
    retreatSpeed  = Clamp(ini.Get(S_MINE, "retreatSpeed").ToDouble(3.0), 0.2, 20.0);
    cruiseSpeed   = Clamp(ini.Get(S_MINE, "cruiseSpeed").ToDouble(40.0), 1.0, 300.0);
    dockSpeed     = Clamp(ini.Get(S_MINE, "dockSpeed").ToDouble(0.8), 0.2, 10.0);
    cargoFullAt   = Clamp(ini.Get(S_MINE, "cargoFullAt").ToDouble(0.92), 0.1, 0.99);
    drillOnRetreat = ini.Get(S_MINE, "drillOnRetreat").ToBoolean(false);
    adaptiveDrill = ini.Get(S_MINE, "adaptiveDrill").ToBoolean(true);

    probeDepth    = Clamp(ini.Get(S_SCOUT, "probeDepth").ToDouble(12.0), 2.0, 200.0);
    probeStride   = (int)Clamp(ini.Get(S_SCOUT, "probeStride").ToInt32(2), 1, 8);
    barrenThreshold = Clamp(ini.Get(S_SCOUT, "barrenThreshold").ToDouble(0.8), 0.0, 100.0);
    useOreDetectorMod = ini.Get(S_SCOUT, "useOreDetectorMod").ToBoolean(true);
    oreScanRange  = Clamp(ini.Get(S_SCOUT, "oreScanRange").ToDouble(500.0), 50.0, 20000.0);
    growToOre     = ini.Get(S_SCOUT, "growToOre").ToBoolean(true);
    growLimit     = (int)Clamp(ini.Get(S_SCOUT, "growLimit").ToInt32(400), 1, 4096);

    minBattery    = Clamp(ini.Get(S_SAFE, "minBattery").ToDouble(0.30), 0.05, 0.95);
    minHydrogen   = Clamp(ini.Get(S_SAFE, "minHydrogen").ToDouble(0.25), 0.0, 0.95);
    minUranium    = Math.Max(0.0, ini.Get(S_SAFE, "minUranium").ToDouble(2.0));
    resumeBattery = Clamp(ini.Get(S_SAFE, "resumeBattery").ToDouble(0.80), 0.1, 1.0);
    resumeHydrogen = Clamp(ini.Get(S_SAFE, "resumeHydrogen").ToDouble(0.90), 0.0, 1.0);
    liftSafetyFactor = Clamp(ini.Get(S_SAFE, "liftSafetyFactor").ToDouble(0.80), 0.2, 1.0);
    transitAltitude = Clamp(ini.Get(S_SAFE, "transitAltitude").ToDouble(25.0), 2.0, 500.0);
    stateTimeout  = Math.Max(0.0, ini.Get(S_SAFE, "stateTimeout").ToDouble(240.0));
    stopOnDamage  = ini.Get(S_SAFE, "stopOnDamage").ToBoolean(false);
    adaptiveBraking = ini.Get(S_SAFE, "adaptiveBraking").ToBoolean(true);
    configBrakeDerate = Clamp(ini.Get(S_SAFE, "brakeDerate").ToDouble(BRAKE_DERATE), 0.30, 0.85);

    droneTimeout  = Clamp(ini.Get(S_FLEET, "droneTimeout").ToDouble(30.0), 5.0, 600.0);
    laneSpacing   = Clamp(ini.Get(S_FLEET, "laneSpacing").ToDouble(12.0), 3.0, 100.0);
    dockSlots     = (int)Clamp(ini.Get(S_FLEET, "dockSlots").ToInt32(1), 1, 32);
    airspaceLock  = ini.Get(S_FLEET, "airspaceLock").ToBoolean(true);
    lockPatience  = Clamp(ini.Get(S_FLEET, "lockPatience").ToDouble(60.0), 5.0, 600.0);

    lcdTag        = ini.Get(S_DISP, "lcdTag").ToString("[VEIN]");
    verboseEcho   = ini.Get(S_DISP, "verboseEcho").ToBoolean(true);

    // Resume thresholds below the abort thresholds would trap the ship in a
    // dock/undock loop forever. Quietly fix rather than let it happen.
    if (resumeBattery <= minBattery) resumeBattery = Math.Min(1.0, minBattery + 0.15);
    if (resumeHydrogen <= minHydrogen) resumeHydrogen = Math.Min(1.0, minHydrogen + 0.15);

    // A drone waiting for airspace sits inside its own shaft, which is a state
    // the watchdog is timing. Let the wait outlast the watchdog and the two
    // fight: the watchdog resets the state, the ship waits again, and it never
    // gets out of the hole. The wait must always lose.
    if (stateTimeout > 0) lockPatience = Math.Min(lockPatience, stateTimeout * 0.5);

    // Seed the learned values, but only if nothing has been learned yet — a
    // reload to change an unrelated key must not throw away an hour of the ship
    // measuring itself. 'learn reset' is the way to deliberately start over.
    if (learnedDrillSpeed <= 0) learnedDrillSpeed = drillSpeed;

    // Re-seed when the operator has actually changed the figure, not only when
    // nothing has been learned yet. Editing brakeDerate and running 'reload' did
    // nothing at all once a single approach had been measured, which makes it
    // look like a knob that is not wired up.
    if (brakeSamples == 0 || configBrakeDerate != seededBrakeDerate)
    {
        brakeDerate = configBrakeDerate;
        if (seededBrakeDerate >= 0 && configBrakeDerate != seededBrakeDerate) brakeSamples = 0;
        seededBrakeDerate = configBrakeDerate;
    }

    WriteConfig();
}

/// <summary>Render the live config back to Custom Data, comments and all.</summary>
void WriteConfig()
{
    ini.Clear();

    ini.Set(S_ID, "role", role.ToString());
    ini.SetComment(S_ID, "role", "Miner or Dispatcher. Miners find a Dispatcher automatically;\nwith none on the channel they just run the job themselves.");
    ini.Set(S_ID, "shipName", shipName);
    ini.SetComment(S_ID, "shipName", "Shown on fleet displays. Blank = use the grid name.");
    ini.Set(S_ID, "blockTag", blockTag);
    ini.SetComment(S_ID, "blockTag", "Only use blocks whose name contains this. Blank = whole grid.\nSet it if the miner docks to a base that also has drills.");
    ini.Set(S_ID, "channel", igcChannel);

    ini.Set(S_MINE, "depthMode", depthMode.ToString());
    ini.SetComment(S_MINE, "depthMode", "Fixed    - always drill to the job depth.\nAutoOre  - stop when ore stops arriving (recommended).\nAutoVoid - stop only on breakthrough into empty space.");
    ini.Set(S_MINE, "holeOrder", holeOrder.ToString());
    ini.SetComment(S_MINE, "holeOrder", "Serpentine - corner to corner, least travel.\nSpiral     - outward from centre.\nProspect   - probe wide, then chase the ore (recommended).");
    ini.Set(S_MINE, "eject", ejectMode.ToString());
    ini.SetComment(S_MINE, "eject", "Off, Stone, or StoneAndIce. Needs at least one ejector/connector\nin Throw Out mode. Roughly triples time-on-site.");
    ini.Set(S_MINE, "shaftOverlap", shaftOverlap);
    ini.SetComment(S_MINE, "shaftOverlap", "0.15 = shafts overlap 15%. Higher clears more rock, digs more holes.");
    ini.Set(S_MINE, "drillSpeed", drillSpeed);
    ini.SetComment(S_MINE, "drillSpeed", "m/s downward while cutting. SCAM ships 0.6 and PAM warns that\nabove ~2 the drills stop keeping up and you jam. Slow is fast here:\ntime lost to a wedged ship dwarfs time saved cutting quickly.");
    ini.Set(S_MINE, "retreatSpeed", retreatSpeed);
    ini.Set(S_MINE, "cruiseSpeed", cruiseSpeed);
    ini.SetComment(S_MINE, "cruiseSpeed", "Ceiling only. Real speed is capped by whatever the ship can\nactually stop from, given its mass and the local gravity.");
    ini.Set(S_MINE, "dockSpeed", dockSpeed);
    ini.SetComment(S_MINE, "dockSpeed", "m/s on the final mating run. PAM uses 0.5 and docking is the\nmanoeuvre most likely to go wrong; slower is genuinely better here.");
    ini.Set(S_MINE, "cargoFullAt", cargoFullAt);
    ini.Set(S_MINE, "drillOnRetreat", drillOnRetreat);
    ini.Set(S_MINE, "adaptiveDrill", adaptiveDrill);
    ini.SetComment(S_MINE, "adaptiveDrill", "Learn the cutting speed this hull can hold, starting from drillSpeed\nand staying between a quarter and 2.5x of it. Shown on the screen.\nOff uses drillSpeed exactly.");

    ini.Set(S_SCOUT, "probeDepth", probeDepth);
    ini.SetComment(S_SCOUT, "probeDepth", "Prospect mode: metres per test shaft before judging a cell.");
    ini.Set(S_SCOUT, "probeStride", probeStride);
    ini.SetComment(S_SCOUT, "probeStride", "Probe every Nth cell to build the first map. 2 is a good default;\n3-4 on a big site you want surveyed fast.");
    ini.Set(S_SCOUT, "barrenThreshold", barrenThreshold);
    ini.SetComment(S_SCOUT, "barrenThreshold", "kg of ore per metre below which a cell is written off.");
    ini.Set(S_SCOUT, "useOreDetectorMod", useOreDetectorMod);
    ini.SetComment(S_SCOUT, "useOreDetectorMod", "Auto-detect Racher's 'Ore Detector Raycast' mod and read real ore\ncoordinates from it. Harmless if the mod is absent.");
    ini.Set(S_SCOUT, "oreScanRange", oreScanRange);
    ini.Set(S_SCOUT, "growToOre", growToOre);
    ini.SetComment(S_SCOUT, "growToOre", "When the survey says the ore is still rich at the edge of the job,\nextend the grid that way instead of calling the job finished.\nProspect order only. Bounded by growLimit.");
    ini.Set(S_SCOUT, "growLimit", growLimit);
    ini.SetComment(S_SCOUT, "growLimit", "Most cells the job may grow to. 400 is a 20x20 site.");

    ini.Set(S_SAFE, "minBattery", minBattery);
    ini.Set(S_SAFE, "minHydrogen", minHydrogen);
    ini.Set(S_SAFE, "minUranium", minUranium);
    ini.SetComment(S_SAFE, "minUranium", "Kilograms across all reactors. Ignored if the ship has none.\n0 disables the check.");
    ini.Set(S_SAFE, "resumeBattery", resumeBattery);
    ini.SetComment(S_SAFE, "resumeBattery", "Charge level at which work resumes. SCAM uses 0.8 — the last\nfifth of a charge takes disproportionately long and buys little.");
    ini.Set(S_SAFE, "resumeHydrogen", resumeHydrogen);
    ini.Set(S_SAFE, "liftSafetyFactor", liftSafetyFactor);
    ini.SetComment(S_SAFE, "liftSafetyFactor", "Fraction of measured lift we will spend. 0.8 leaves 20% in hand\nfor a heavy load and a bad angle.");
    ini.Set(S_SAFE, "transitAltitude", transitAltitude);
    ini.Set(S_SAFE, "stateTimeout", stateTimeout);
    ini.SetComment(S_SAFE, "stateTimeout", "Seconds before the watchdog calls a state hung and recovers.\n0 disables it, which is rarely what you want.");
    ini.Set(S_SAFE, "stopOnDamage", stopOnDamage);
    ini.Set(S_SAFE, "adaptiveBraking", adaptiveBraking);
    ini.SetComment(S_SAFE, "adaptiveBraking", "Measure how much braking authority approaches actually demand and\ntune the derate to suit this hull and this server's tick rate.\nShown on the screen. Off uses brakeDerate exactly.");
    ini.Set(S_SAFE, "brakeDerate", configBrakeDerate);
    ini.SetComment(S_SAFE, "brakeDerate", "Fraction of the theoretical sqrt(2ad) stopping speed the ship will\nuse. Starting point when adaptiveBraking is on, fixed value when it\nis off. PAM ships 0.70, SCAM 0.50. Lower is slower and safer.");

    ini.Set(S_FLEET, "droneTimeout", droneTimeout);
    ini.SetComment(S_FLEET, "droneTimeout", "Seconds of silence before a drone is presumed lost and its shaft\nis handed to somebody else.");
    ini.Set(S_FLEET, "laneSpacing", laneSpacing);
    ini.SetComment(S_FLEET, "laneSpacing", "Metres between drone altitude lanes over the site. Must exceed\nthe largest drone's height by a comfortable margin. Formation only —\nexclusion is airspaceLock's job.");
    ini.Set(S_FLEET, "dockSlots", dockSlots);
    ini.Set(S_FLEET, "airspaceLock", airspaceLock);
    ini.SetComment(S_FLEET, "airspaceLock", "One drone at a time in the airspace over the site, granted by the\ndispatcher with a queue behind it. This is what actually stops two\ndrones wanting the same hole. Ignored by a solo miner.");
    ini.Set(S_FLEET, "lockPatience", lockPatience);
    ini.SetComment(S_FLEET, "lockPatience", "Seconds a drone waits for the airspace before proceeding anyway.");

    ini.Set(S_DISP, "lcdTag", lcdTag);
    ini.Set(S_DISP, "verboseEcho", verboseEcho);

    string rendered = ini.ToString();
    if (rendered != Me.CustomData) Me.CustomData = rendered;
}

// Enum parsing is written out longhand on purpose. Enum.Parse and Enum.GetNames
// are reflection and the Programmable Block sandbox blocks reflection outright —
// a generic helper would compile in an IDE and then throw in game.

/// <summary>Case-insensitive compare with no culture surprises.</summary>
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
