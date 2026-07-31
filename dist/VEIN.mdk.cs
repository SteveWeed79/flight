using System;
using System.Text;
using System.Collections.Generic;
using Sandbox.ModAPI.Ingame;
using Sandbox.ModAPI.Interfaces;
using Sandbox.Game.EntityComponents;
using SpaceEngineers.Game.ModAPI.Ingame;
using VRage;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.GUI.TextPanel;
using VRage.Game.ModAPI.Ingame;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRage.Game.ObjectBuilders.Definitions;
using VRageMath;

namespace VEIN
{
    public partial class Program : MyGridProgram
    {
        #region 00_Header.cs
        /*//////////////////////////////////////////////////////////////////////////////
         *  VEIN — Vectored Extraction & Intelligent Navigation
         *  A survival-grade automated mining script for Space Engineers.
         *
         *  Lineage & credit:
         *    [PAM]  Path Auto Miner       by Keks     — path recording, shaft grids,
         *                                               gravity-aware flight, LCD menus.
         *    [SCAM] Simple Concurrent
         *           Adaptive Min3r        by cheerkin — dispatcher/drone concurrency,
         *                                               adaptive depth from ore income.
         *
         *  VEIN is an independent implementation. It borrows the *ideas* that made those
         *  two scripts good, fixes the failure modes that made them frustrating, and adds
         *  a real scouting layer on top.
         *
         *  ---------------------------------------------------------------------------
         *  SETUP IN 30 SECONDS
         *  ---------------------------------------------------------------------------
         *    1. Put this script in a Programmable Block on your mining ship.
         *    2. Ship needs: Remote Control, gyro(s), thrusters, drills, a connector,
         *       cargo. Everything else is optional.
         *    3. Dock at your base connector. Run argument:  record start
         *    4. Fly manually to the spot you want mined, nose pointing the way you
         *       want to drill (usually straight down). Run:  record stop
         *    5. Run:  job set 5 5 40      (5x5 shafts, 40 m deep)
         *    6. Run:  start
         *
         *  Full docs: docs/SETUP.md, docs/CONFIG.md, docs/SPACE.md, docs/DESIGN.md, docs/SCOUTING.md
         *
         *  ---------------------------------------------------------------------------
         *  A NOTE ON ORE DETECTION (read this before asking why it digs dry holes)
         *  ---------------------------------------------------------------------------
         *  Vanilla Space Engineers does NOT let scripts read the Ore Detector. Keen has
         *  rejected that API repeatedly. Any script claiming to "fly straight to ore" in
         *  vanilla is either using a mod or guessing.
         *
         *  VEIN is honest about this and handles it in two tiers:
         *
         *    TIER 1 (vanilla, always on) — Probe scouting.
         *      Drill cheap shallow test shafts on a coarse lattice, measure kg of ore
         *      per metre drilled, build a yield map, then spend the expensive deep
         *      shafts only where the map says ore lives. See docs/SCOUTING.md.
         *
         *    TIER 2 (auto-detected, optional) — Ore Detector Raycast bridge.
         *      If Racher's "Ore Detector Raycast" mod is installed, VEIN detects it at
         *      runtime and reads true ore coordinates straight off the detector. No
         *      config needed; it just gets better. Falls back cleanly if absent.
         *
         *  ---------------------------------------------------------------------------
         *  LICENSE: MIT. Use it, fork it, sell blueprints with it. Credit is nice.
         *//////////////////////////////////////////////////////////////////////////////

        const string VEIN_VERSION = "1.0.0";
        const string STORAGE_REV  = "2";   // bump on ANY change to a persisted enum or field order
        #endregion

        #region 01_Enums.cs
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

        /// <summary>Miner lifecycle. Exactly one of these is active at a time.</summary>
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
        #endregion

        #region 02_Types.cs
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
            /// <summary>Gravity at the job site, captured at set time.</summary>
            public Vector3D Gravity;

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
            /// <summary>Tick at which an unrenewed lease expires.</summary>
            public long LeaseExpiresTick;
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
            /// <summary>Last tick we heard from it. Silence past a timeout = presumed dead.</summary>
            public long LastSeenTick;
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
        #endregion

        #region 03_Config.cs
        // ============================================================================
        //  CONFIGURATION
        //
        //  Everything lives in the Programmable Block's Custom Data as INI. Edit it,
        //  then run the "reload" argument (or just recompile). Unknown keys are left
        //  alone; missing keys are written back with their defaults, so the block always
        //  documents itself.
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
        double drillSpeed = 1.2;
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

        // ---- Safety ---------------------------------------------------------------
        /// <summary>Head home below this battery fraction.</summary>
        double minBattery = 0.30;
        /// <summary>Head home below this hydrogen fraction.</summary>
        double minHydrogen = 0.25;
        /// <summary>Head home below this many kilograms of uranium across all reactors.
        /// Ignored entirely on a ship with no reactors.</summary>
        double minUranium = 2.0;
        /// <summary>Resume work above this battery fraction.</summary>
        double resumeBattery = 0.95;
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

        // ---- Fleet ----------------------------------------------------------------
        /// <summary>Seconds without a heartbeat before a drone is presumed lost and its work reissued.</summary>
        double droneTimeout = 30.0;
        /// <summary>Vertical spacing between drone traffic lanes, metres.</summary>
        double laneSpacing = 12.0;
        /// <summary>Dispatcher only: how many connectors are available for unloading.</summary>
        int dockSlots = 1;

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
            drillSpeed    = Clamp(ini.Get(S_MINE, "drillSpeed").ToDouble(1.2), 0.05, 10.0);
            retreatSpeed  = Clamp(ini.Get(S_MINE, "retreatSpeed").ToDouble(3.0), 0.2, 20.0);
            cruiseSpeed   = Clamp(ini.Get(S_MINE, "cruiseSpeed").ToDouble(40.0), 1.0, 300.0);
            dockSpeed     = Clamp(ini.Get(S_MINE, "dockSpeed").ToDouble(0.8), 0.2, 10.0);
            cargoFullAt   = Clamp(ini.Get(S_MINE, "cargoFullAt").ToDouble(0.92), 0.1, 0.99);
            drillOnRetreat = ini.Get(S_MINE, "drillOnRetreat").ToBoolean(false);

            probeDepth    = Clamp(ini.Get(S_SCOUT, "probeDepth").ToDouble(12.0), 2.0, 200.0);
            probeStride   = (int)Clamp(ini.Get(S_SCOUT, "probeStride").ToInt32(2), 1, 8);
            barrenThreshold = Clamp(ini.Get(S_SCOUT, "barrenThreshold").ToDouble(0.8), 0.0, 100.0);
            useOreDetectorMod = ini.Get(S_SCOUT, "useOreDetectorMod").ToBoolean(true);
            oreScanRange  = Clamp(ini.Get(S_SCOUT, "oreScanRange").ToDouble(500.0), 50.0, 20000.0);

            minBattery    = Clamp(ini.Get(S_SAFE, "minBattery").ToDouble(0.30), 0.05, 0.95);
            minHydrogen   = Clamp(ini.Get(S_SAFE, "minHydrogen").ToDouble(0.25), 0.0, 0.95);
            minUranium    = Math.Max(0.0, ini.Get(S_SAFE, "minUranium").ToDouble(2.0));
            resumeBattery = Clamp(ini.Get(S_SAFE, "resumeBattery").ToDouble(0.95), 0.1, 1.0);
            resumeHydrogen = Clamp(ini.Get(S_SAFE, "resumeHydrogen").ToDouble(0.90), 0.0, 1.0);
            liftSafetyFactor = Clamp(ini.Get(S_SAFE, "liftSafetyFactor").ToDouble(0.80), 0.2, 1.0);
            transitAltitude = Clamp(ini.Get(S_SAFE, "transitAltitude").ToDouble(25.0), 2.0, 500.0);
            stateTimeout  = Math.Max(0.0, ini.Get(S_SAFE, "stateTimeout").ToDouble(240.0));
            stopOnDamage  = ini.Get(S_SAFE, "stopOnDamage").ToBoolean(false);

            droneTimeout  = Clamp(ini.Get(S_FLEET, "droneTimeout").ToDouble(30.0), 5.0, 600.0);
            laneSpacing   = Clamp(ini.Get(S_FLEET, "laneSpacing").ToDouble(12.0), 3.0, 100.0);
            dockSlots     = (int)Clamp(ini.Get(S_FLEET, "dockSlots").ToInt32(1), 1, 32);

            lcdTag        = ini.Get(S_DISP, "lcdTag").ToString("[VEIN]");
            verboseEcho   = ini.Get(S_DISP, "verboseEcho").ToBoolean(true);

            // Resume thresholds below the abort thresholds would trap the ship in a
            // dock/undock loop forever. Quietly fix rather than let it happen.
            if (resumeBattery <= minBattery) resumeBattery = Math.Min(1.0, minBattery + 0.15);
            if (resumeHydrogen <= minHydrogen) resumeHydrogen = Math.Min(1.0, minHydrogen + 0.15);

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
            ini.SetComment(S_MINE, "drillSpeed", "m/s downward while cutting. Above ~2 m/s drills stop keeping up\nand you jam. Lower it on a light ship.");
            ini.Set(S_MINE, "retreatSpeed", retreatSpeed);
            ini.Set(S_MINE, "cruiseSpeed", cruiseSpeed);
            ini.SetComment(S_MINE, "cruiseSpeed", "Ceiling only. Real speed is capped by whatever the ship can\nactually stop from, given its mass and the local gravity.");
            ini.Set(S_MINE, "dockSpeed", dockSpeed);
            ini.SetComment(S_MINE, "dockSpeed", "m/s on the final mating run. PAM uses 0.5 and docking is the\nmanoeuvre most likely to go wrong; slower is genuinely better here.");
            ini.Set(S_MINE, "cargoFullAt", cargoFullAt);
            ini.Set(S_MINE, "drillOnRetreat", drillOnRetreat);

            ini.Set(S_SCOUT, "probeDepth", probeDepth);
            ini.SetComment(S_SCOUT, "probeDepth", "Prospect mode: metres per test shaft before judging a cell.");
            ini.Set(S_SCOUT, "probeStride", probeStride);
            ini.SetComment(S_SCOUT, "probeStride", "Probe every Nth cell to build the first map. 2 is a good default;\n3-4 on a big site you want surveyed fast.");
            ini.Set(S_SCOUT, "barrenThreshold", barrenThreshold);
            ini.SetComment(S_SCOUT, "barrenThreshold", "kg of ore per metre below which a cell is written off.");
            ini.Set(S_SCOUT, "useOreDetectorMod", useOreDetectorMod);
            ini.SetComment(S_SCOUT, "useOreDetectorMod", "Auto-detect Racher's 'Ore Detector Raycast' mod and read real ore\ncoordinates from it. Harmless if the mod is absent.");
            ini.Set(S_SCOUT, "oreScanRange", oreScanRange);

            ini.Set(S_SAFE, "minBattery", minBattery);
            ini.Set(S_SAFE, "minHydrogen", minHydrogen);
            ini.Set(S_SAFE, "minUranium", minUranium);
            ini.SetComment(S_SAFE, "minUranium", "Kilograms across all reactors. Ignored if the ship has none.\n0 disables the check.");
            ini.Set(S_SAFE, "resumeBattery", resumeBattery);
            ini.Set(S_SAFE, "resumeHydrogen", resumeHydrogen);
            ini.Set(S_SAFE, "liftSafetyFactor", liftSafetyFactor);
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
        #endregion

        #region 04_Fields.cs
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
        #endregion

        #region 05_Program.cs
        // ============================================================================
        //  PROGRAM CORE — boot, tick scheduling, watchdog, fault containment
        // ============================================================================

        public Program()
        {
            // Update10 is the sweet spot. Update1 burns instruction budget for control
            // quality nobody can see; Update100 is too coarse to fly a ship into a hole.
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
                // A crash in the constructor leaves a block that looks alive and does
                // nothing. Make it loud instead.
                faultReason = "Boot failed: " + e.Message;
                state = MinerState.Fault;
            }
        }

        public void Save()
        {
            try { Storage = SerializeState(); }
            catch { /* A failed save must never take the world save down with it. */ }
        }

        public void Main(string argument, UpdateType updateSource)
        {
            try
            {
                // ---- Operator input -------------------------------------------------
                if ((updateSource & (UpdateType.Terminal | UpdateType.Trigger | UpdateType.Script)) != 0
                    && !string.IsNullOrWhiteSpace(argument))
                {
                    HandleCommand(argument);
                }

                // ---- Inter-grid traffic --------------------------------------------
                // Drained every tick regardless of source: an unread listener queue grows
                // without bound and eventually costs more to process than it is worth.
                PumpIgc();

                // ---- Periodic work --------------------------------------------------
                if ((updateSource & (UpdateType.Update1 | UpdateType.Update10 | UpdateType.Update100)) == 0)
                {
                    // Command-only invocation. Refresh the screen so the operator sees
                    // the effect of what they just typed, then stop.
                    Render(true);
                    return;
                }

                tick++;
                double elapsed = Runtime.TimeSinceLastRun.TotalSeconds;
                // A paused game, a world load, or a laggy server can hand us a garbage
                // dt. Clamp it — an unclamped dt makes the controller apply a colossal
                // correction on the first tick back, which is a great way to fly a
                // loaded miner into a mountain.
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
                // Anything that escapes to here is a bug. Do not let a bug fly the ship.
                EnterFault("Unhandled: " + e.Message);
                try { SafeStop(); } catch { }
                Render(true);
            }
        }

        // ---------------------------------------------------------------------------
        //  STATE MACHINE PLUMBING
        // ---------------------------------------------------------------------------

        /// <summary>Transition to a new state. Idempotent — re-entering resets the timer.</summary>
        void SetState(MinerState next)
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

        /// <summary>
        /// The watchdog. Every state is bounded in time; nothing is allowed to hang
        /// forever. This is the single biggest reliability difference between VEIN and
        /// the scripts it learns from — PAM in particular will sit in a docking approach
        /// until the heat death of the universe if the connector never lines up.
        /// </summary>
        void Watchdog()
        {
            if (stateTimeout <= 0) return;
            if (state == MinerState.Idle || state == MinerState.Fault) return;
            // Servicing legitimately takes as long as the batteries take.
            if (state == MinerState.Servicing) return;

            if (stateTicks * dt < stateTimeout) return;

            Log("Watchdog: " + state + " ran over " + Fmt(stateTimeout, 0) + "s");

            switch (state)
            {
                // A hung shaft is almost always a stuck ship. Back out and blacklist.
                case MinerState.Descending:
                case MinerState.Ascending:
                    MarkCellStuck();
                    // Must be set explicitly. FinishShaft reads pendingResult when the
                    // ship clears the hole, and without this it would read whatever the
                    // *previous* shaft left behind — recording a cell the ship could not
                    // even reach as completed, or worse, as unfinished and worth
                    // retrying forever.
                    pendingResult = ShaftResult.Stuck;
                    SetState(MinerState.Ascending);
                    break;

                // Hung en route: the path is probably obstructed. Going home is the
                // safest direction because the route there is known-good.
                case MinerState.Outbound:
                case MinerState.Approaching:
                case MinerState.Selecting:
                    SetState(MinerState.Inbound);
                    break;

                // Hung docking is recoverable: back off and try the approach again.
                // Three failures means something is genuinely wrong with the dock.
                case MinerState.Docking:
                    dockRetries++;
                    if (dockRetries >= 3) EnterFault("Could not dock after 3 attempts");
                    else { Log("Docking retry " + dockRetries); SetState(MinerState.Inbound); }
                    break;

                // Everything else: park it and ask for help rather than guess.
                default:
                    EnterFault("State " + state + " timed out");
                    break;
            }
        }

        /// <summary>Stop, make safe, and stay that way until a human intervenes.</summary>
        void EnterFault(string why)
        {
            if (state == MinerState.Fault) return;
            faultReason = why;
            Log("FAULT: " + why);
            state = MinerState.Fault;
            stateTicks = 0;
            // Entry tick must fire. SafeStop is called below as well, but a state that
            // never sees stateEntry is a trap for anything added to StFault later.
            stateEntry = true;
            jobRunning = false;
            ReleaseLease(ShaftResult.Aborted);
            SafeStop();
        }

        /// <summary>Clear a fault and return to idle. Does not resume the job by itself.</summary>
        void ClearFault()
        {
            faultReason = "";
            stuckRetries = 0;
            dockRetries = 0;
            SetState(MinerState.Idle);
            Log("Fault cleared");
        }

        // ---------------------------------------------------------------------------
        //  INSTRUCTION BUDGET
        // ---------------------------------------------------------------------------

        /// <summary>
        /// Track how close we are to the "script too complex" ceiling. Anything above
        /// about 0.8 sustained means a rescan or a fleet size is too expensive and we
        /// should back off before the game kills the block.
        /// </summary>
        void TrackLoad()
        {
            double used = Runtime.MaxInstructionCount > 0
                ? (double)Runtime.CurrentInstructionCount / Runtime.MaxInstructionCount
                : 0.0;
            // Decay slowly so a single spike stays visible for a few seconds.
            loadPeak = Math.Max(used, loadPeak * 0.97);
        }

        /// <summary>
        /// True when we have burned enough of this tick's budget that expensive optional
        /// work should be deferred. Guards the loops whose cost scales with the world:
        /// block scans, fleet iteration, raycasting.
        /// </summary>
        bool BudgetTight(double fraction = 0.6)
        {
            if (Runtime.MaxInstructionCount <= 0) return false;
            return (double)Runtime.CurrentInstructionCount / Runtime.MaxInstructionCount > fraction;
        }

        // ---------------------------------------------------------------------------
        //  LOGGING
        // ---------------------------------------------------------------------------

        void Log(string msg)
        {
            log.Add(Fmt(clock, 0) + "s  " + msg);
            while (log.Count > LOG_MAX) log.RemoveAt(0);
        }
        #endregion

        #region 06_Blocks.cs
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

            blockScratch.Clear();
            GridTerminalSystem.GetBlocksOfType(blockScratch, b => Mine(b) && b.CustomName.Contains(lcdTag));

            for (int i = 0; i < blockScratch.Count; i++)
            {
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

            double maxLateral = 0;
            Vector3D offsetSum = Vector3D.Zero;

            for (int i = 0; i < drills.Count; i++)
            {
                Vector3D local = Vector3D.TransformNormal(drills[i].GetPosition() - ctrlPos, refInv);
                double lateral = Math.Sqrt(local.X * local.X + local.Y * local.Y);
                if (lateral > maxLateral) maxLateral = lateral;
                offsetSum += local;
            }

            // The cutting face is as wide as the drill cluster plus one drill's reach.
            drillRadius = maxLateral + singleCut;
            drillOffset = offsetSum / drills.Count;

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
        #endregion

        #region 07_Control.cs
        // ============================================================================
        //  FLIGHT CONTROL
        //
        //  Direct thrust and gyro override. We deliberately do not use the Remote
        //  Control autopilot: it cannot be told "descend at exactly 1.2 m/s while
        //  holding this attitude", it gives up in gravity, and it has no concept of a
        //  drill jamming. Everything here is closed-loop on measured velocity.
        // ============================================================================

        /// <summary>
        /// Fraction of the theoretical stopping speed we are actually willing to use.
        ///
        /// sqrt(2·a·d) is exact for an ideal actuator and optimistic for a real one.
        /// Thrusters ramp rather than snapping to full output, server tick rate varies,
        /// ship mass changes while drilling, and the velocity controller has its own
        /// response lag. All four eat into the distance available to stop in.
        ///
        /// PAM defaults to 0.70 for the same reason and its UI marks anything above
        /// 0.80 as risky — a number arrived at by shipping to a great many players
        /// rather than by derivation, which makes it worth respecting. VEIN is already
        /// pessimistic in gravity, where it subtracts the full gravity magnitude from
        /// available deceleration, but in space that subtraction is zero and this is the
        /// only margin there is.
        /// </summary>
        const double BRAKE_DERATE = 0.75;

        /// <summary>Bucket every thruster by the ship-local direction it pushes.</summary>
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

                // A thruster's flame points Forward; the ship is shoved Backward.
                Vector3D push = Vector3D.TransformNormal(t.WorldMatrix.Backward, inv);

                // Snap to the dominant axis. Thrusters are always grid-aligned, so the
                // dominant component is ~1 and the others are float noise.
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

            thrusterTypes.Sort();  // stable index space for waypoint efficiency arrays
            RefreshThrustCapacity();
        }

        /// <summary>
        /// Re-sum available thrust. Done every tick, not just on rescan, because
        /// MaxEffectiveThrust is a live value — an atmospheric thruster loses its
        /// authority as you climb, and a script that caches this at compile time will
        /// happily fly you off the top of the atmosphere and then drop you.
        /// </summary>
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

        /// <summary>
        /// Largest force the ship can produce along a world direction.
        /// A diagonal is limited by whichever axis saturates first, which is why this
        /// is a min over axes rather than a sum.
        /// </summary>
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
                if (Math.Abs(c) < 1e-4) continue;      // this axis contributes nothing
                float cap = thrustByAxis[axis, c >= 0 ? 0 : 1];
                if (cap <= 0) return 0;                // no thrust that way at all
                limit = Math.Min(limit, cap / Math.Abs(c));
            }

            return limit == double.MaxValue ? 0 : limit;
        }

        /// <summary>
        /// Deceleration we can actually achieve while travelling along
        /// <paramref name="dir"/>. Deliberately pessimistic — it assumes gravity is
        /// working against us in full, whatever direction we are pointing. Being
        /// conservative here costs a little speed and buys a lot of not-crashing.
        /// </summary>
        double StoppingAccel(Vector3D dir)
        {
            double raw = ThrustAlong(-dir) / Math.Max(1.0, shipMass);
            return Math.Max(0.15, raw - gravity.Length());
        }

        /// <summary>Fly toward a point, arriving with zero velocity.</summary>
        void FlyTo(Vector3D target, double maxSpeed)
        {
            flightActive = true;

            if (controller == null) return;

            Vector3D toTarget = target - controller.GetPosition();
            distToTarget = toTarget.Length();

            Vector3D dir = distToTarget > 1e-4 ? toTarget / distToTarget : Vector3D.Zero;

            // Speed we could still shed before arriving: v = sqrt(2 a d).
            double stopAccel = StoppingAccel(dir.LengthSquared() > 0 ? dir : Vector3D.Up);
            double arrivalSpeed = Math.Sqrt(2.0 * stopAccel * Math.Max(0.0, distToTarget)) * BRAKE_DERATE;

            double want = Math.Min(maxSpeed, arrivalSpeed);

            // Do not travel fast while still swinging round. PAM does the same thing and
            // the reason is practical: the drills point along the ship's forward axis,
            // so a badly misaligned ship at speed is carrying its most fragile face
            // sideways into whatever it is approaching. Full speed by 20 degrees.
            if (alignError > 20.0) want *= Math.Max(0.15, 1.0 - (alignError - 20.0) / 70.0);

            // Never command more than the server will honour anyway.
            want = Math.Min(want, 95.0);

            SetVelocity(dir * want);
        }

        /// <summary>
        /// Drive the ship toward a target velocity. This is the bottom of the flight
        /// stack — everything else eventually calls here.
        /// </summary>
        void SetVelocity(Vector3D desiredVelWorld)
        {
            if (controller == null) return;

            // We own the thrusters while flying; the game's dampeners would fight us.
            if (controller.DampenersOverride) controller.DampenersOverride = false;

            Vector3D velError = desiredVelWorld - shipVel;

            // Correct the velocity error over a fixed time constant rather than over dt.
            // Using dt makes the gain depend on server tick rate, which is how scripts
            // that fly beautifully in single player oscillate themselves apart on a
            // busy dedicated server.
            const double TAU = 0.45;
            Vector3D desiredAccel = velError / TAU;

            // Do not ask for more acceleration than we own.
            double accelCap = ThrustAlong(desiredAccel) / Math.Max(1.0, shipMass);
            if (accelCap > 0 && desiredAccel.Length() > accelCap)
                desiredAccel = Vector3D.Normalize(desiredAccel) * accelCap;

            // Hold station against gravity on top of whatever manoeuvre we wanted.
            Vector3D force = (desiredAccel - gravity) * shipMass;
            ApplyForce(force);
        }

        /// <summary>Distribute a world-space force across the thruster buckets.</summary>
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

            // The opposing bucket must be explicitly zeroed. Leaving a stale override
            // there means two banks fighting, which burns fuel and reads as "the ship
            // feels sluggish" long before anyone works out why.
            List<IMyThrust> off = thrustBuckets[axis, neg];
            if (off != null)
                for (int i = 0; i < off.Count; i++)
                    if (off[i].IsFunctional) off[i].ThrustOverridePercentage = 0f;
        }

        // ---------------------------------------------------------------------------
        //  ORIENTATION
        // ---------------------------------------------------------------------------

        /// <summary>
        /// Point the ship's forward axis along <paramref name="desiredForward"/>, rolling
        /// so its up axis is as close to <paramref name="desiredUp"/> as it can be.
        /// Pass a zero forward to release the gyros.
        /// </summary>
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

            // With no roll preference, prefer "up" away from gravity — it keeps the ship
            // the right way up so a pilot dropping in is not instantly disoriented.
            if (desiredUp.LengthSquared() < 1e-9)
                desiredUp = gravity.LengthSquared() > 1e-6 ? -Vector3D.Normalize(gravity) : controller.WorldMatrix.Up;

            // Gram-Schmidt: strip out any part of "up" that lies along forward, so the
            // two axes are genuinely perpendicular before we compare against them.
            desiredUp = desiredUp - desiredForward * Vector3D.Dot(desiredUp, desiredForward);
            if (desiredUp.LengthSquared() < 1e-9)
            {
                // Degenerate: caller asked for up parallel to forward. Pick anything.
                desiredUp = Math.Abs(desiredForward.Z) < 0.9
                    ? Vector3D.Normalize(Vector3D.Cross(desiredForward, Vector3D.Forward))
                    : Vector3D.Normalize(Vector3D.Cross(desiredForward, Vector3D.Right));
            }
            else desiredUp = Vector3D.Normalize(desiredUp);

            MatrixD m = controller.WorldMatrix;

            // Cross products give a rotation axis whose length is sin(error). Summing the
            // forward and up terms handles pitch/yaw and roll in one shot without ever
            // building a quaternion or hitting a gimbal singularity.
            Vector3D errAxis = Vector3D.Cross(m.Forward, desiredForward)
                             + Vector3D.Cross(m.Up, desiredUp);

            alignError = ToDegrees(Math.Acos(Clamp(Vector3D.Dot(m.Forward, desiredForward), -1.0, 1.0)));

            // A cross product has magnitude sin(angle), so it vanishes at 180 degrees
            // exactly as it does at zero. Pointed at precisely the opposite direction —
            // rejoining a recorded route that runs back past the ship, or a dock
            // approach that needs a reversal — both terms cancel, the command is zero,
            // and the ship sits there perfectly still at maximum error until the
            // watchdog gives up. Nudge it off the singularity with any perpendicular
            // axis; one tick later the normal control has a gradient to work with.
            if (errAxis.LengthSquared() < 1e-6 && alignError > 90.0)
            {
                Vector3D seed = Math.Abs(m.Forward.Z) < 0.9 ? Vector3D.Forward : Vector3D.Right;
                errAxis = Vector3D.Normalize(Vector3D.Cross(m.Forward, seed));
            }

            Vector3D angVel = controller.GetShipVelocities().AngularVelocity;

            // PD. The derivative term is what stops a big ship wallowing past the target
            // and oscillating; without it a heavy miner never settles enough to drill.
            const double KP = 3.0;
            const double KD = 0.55;
            Vector3D command = errAxis * KP - angVel * KD;

            // PAM separates these by a factor of three — 15 for small grids against 5
            // for large — where an earlier version of this used barely half that. A
            // large grid carries enormously more rotational inertia and will overshoot
            // and hunt on a gain that suits a small one.
            double maxRate = isLargeGrid ? 0.6 : 1.8;   // rad/s
            if (command.Length() > maxRate) command = Vector3D.Normalize(command) * maxRate;

            ApplyGyros(command);
        }

        /// <summary>Push an angular velocity command into every gyro, in its own frame.</summary>
        void ApplyGyros(Vector3D worldCommand)
        {
            for (int i = 0; i < gyros.Count; i++)
            {
                IMyGyro g = gyros[i];
                if (!g.IsFunctional) continue;

                // Each gyro can be mounted in any orientation. Rotating the command into
                // the gyro's own frame is what lets a randomly-placed gyro farm cooperate
                // instead of cancelling out.
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

        // ---------------------------------------------------------------------------
        //  SAFING
        // ---------------------------------------------------------------------------

        /// <summary>
        /// Hand the ship back to the game in a state a human would consider safe:
        /// drills off, no thrust overrides, gyros released, dampeners on.
        /// Called on fault, on stop, and on dock.
        /// </summary>
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

            // Dampeners back on last. If the ship is moving, this is what actually
            // arrests it, and we want the overrides already cleared when it does.
            if (controller != null) controller.DampenersOverride = true;
        }

        void SetDrills(bool on)
        {
            for (int i = 0; i < drills.Count; i++)
                if (drills[i].Enabled != on) drills[i].Enabled = on;
        }

        /// <summary>
        /// Switch the thrusters on or off as blocks.
        ///
        /// Note this is Enabled, not the override — a disabled thruster contributes
        /// nothing to <see cref="RefreshThrustCapacity"/>, so the flight controller
        /// computes zero available thrust and the ship simply does not move. Always
        /// call this with true before attempting to fly.
        /// </summary>
        void SetThrusters(bool on)
        {
            for (int i = 0; i < thrusters.Count; i++)
                if (thrusters[i].Enabled != on) thrusters[i].Enabled = on;
        }

        // ---------------------------------------------------------------------------
        //  SAMPLING
        // ---------------------------------------------------------------------------

        /// <summary>Refresh everything the control loop reads. Called once per tick.</summary>
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
            // The expensive half, at roughly 1 Hz.
            if (tick % 6 == 0) SampleOre();
        }

        /// <summary>World position of the drill cutting face — the point that is
        /// actually at the bottom of the shaft, which is not where the controller is.</summary>
        Vector3D DrillFace()
        {
            if (controller == null) return shipPos;
            MatrixD m = controller.WorldMatrix;
            return shipPos
                 + m.Right * drillOffset.X
                 + m.Up * drillOffset.Y
                 + m.Backward * drillOffset.Z;
        }
        #endregion

        #region 08_Cargo.cs
        // ============================================================================
        //  INVENTORY, EJECTION, UNLOADING
        // ============================================================================

        const string TYPE_ORE = "MyObjectBuilder_Ore";
        const string SUB_STONE = "Stone";
        const string SUB_ICE = "Ice";

        /// <summary>
        /// Volumes, power and gas. Cheap enough to run every tick.
        ///
        /// Deliberately does NOT enumerate items. CurrentVolume is a single property
        /// read, whereas GetItems fills a list per inventory — and a ship with ten
        /// drills and five containers was doing fifteen of those six times a second for
        /// a number that only feeds a one-hertz decision. Ore counting lives in
        /// SampleOre and runs far less often.
        /// </summary>
        void SampleInventories()
        {
            double vol = 0, maxVol = 0;
            peakInventoryFill = 0;

            for (int i = 0; i < cargo.Count; i++)
                AccumulateVolume(cargo[i].GetInventory(0), ref vol, ref maxVol);

            // Drill inventories count too. On a ship without conveyors they are the
            // only storage there is, and on one with conveyors they are the buffer that
            // tells us whether ore is still arriving.
            for (int i = 0; i < drills.Count; i++)
                AccumulateVolume(drills[i].GetInventory(0), ref vol, ref maxVol);

            cargoFill = maxVol > 0 ? vol / maxVol : 0;
            cargoVolume = vol;

            // ---- Power ------------------------------------------------------------
            double stored = 0, capacity = 0;
            for (int i = 0; i < batteries.Count; i++)
            {
                IMyBatteryBlock b = batteries[i];
                if (!b.IsFunctional) continue;
                stored += b.CurrentStoredPower;
                capacity += b.MaxStoredPower;
            }
            batteryFill = capacity > 0 ? stored / capacity : 1.0;

            // ---- Hydrogen ---------------------------------------------------------
            double gas = 0; int tanks = 0;
            for (int i = 0; i < hydrogenTanks.Count; i++)
            {
                if (!hydrogenTanks[i].IsFunctional) continue;
                gas += hydrogenTanks[i].FilledRatio;
                tanks++;
            }
            // No tanks means no hydrogen dependency, so report full rather than empty —
            // otherwise an ion-only ship would refuse to ever leave the dock.
            hydrogenFill = tanks > 0 ? gas / tanks : 1.0;
        }

        void AccumulateVolume(IMyInventory inv, ref double vol, ref double maxVol)
        {
            if (inv == null) return;
            double cur = (double)inv.CurrentVolume, max = (double)inv.MaxVolume;
            vol += cur;
            maxVol += max;
            if (max > 0) peakInventoryFill = Math.Max(peakInventoryFill, cur / max);
        }

        /// <summary>
        /// Kilograms of valuable ore aboard. The expensive half of inventory sampling,
        /// so it runs at about 1 Hz rather than every tick. Adaptive depth already
        /// requires sixty dry ticks before it acts, so a second of lag changes nothing;
        /// callers that need an exact figure at a shaft boundary call this directly.
        /// </summary>
        void SampleOre()
        {
            // Uranium, while we are already walking inventories.
            uraniumKg = 0;
            for (int i = 0; i < reactors.Count; i++)
            {
                IMyInventory inv = reactors[i].GetInventory(0);
                if (inv == null) continue;
                itemScratch.Clear();
                inv.GetItems(itemScratch);
                for (int k = 0; k < itemScratch.Count; k++)
                    if (itemScratch[k].Type.SubtypeId == "Uranium") uraniumKg += (double)itemScratch[k].Amount;
            }

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

        /// <summary>Ore that is worth carrying home. Stone is not.</summary>
        static bool IsValuableOre(MyItemType t)
        {
            return t.TypeId == TYPE_ORE && t.SubtypeId != SUB_STONE;
        }

        /// <summary>
        /// Total mass moved through the drills this shaft, including whatever the
        /// conveyors already pulled back into cargo. Using drill contents alone would
        /// under-report badly on a well-conveyored ship.
        /// </summary>
        double ShaftOreSoFar()
        {
            return Math.Max(0.0, oreAboard - shaftStartOre);
        }

        /// <summary>
        /// Out of usable room. Aggregate fill is the normal signal, but a single
        /// brimming inventory also counts: a drill with no conveyor to anywhere fills
        /// up and silently stops collecting while total fill still reads ten per cent,
        /// so the ship would keep grinding away collecting nothing. PAM solves this by
        /// balancing contents between drills; refusing to keep mining is cheaper and
        /// fails in the safe direction.
        /// </summary>
        bool CargoFull
        {
            get { return cargoFill >= cargoFullAt || peakInventoryFill >= 0.98; }
        }

        // ---------------------------------------------------------------------------
        //  EJECTION
        // ---------------------------------------------------------------------------

        /// <summary>
        /// Dump worthless mass overboard. This roughly triples time-on-site: stone is
        /// the overwhelming majority of what a drill picks up, and hauling it home is
        /// pure waste.
        /// </summary>
        /// <returns>True when there is nothing left worth ejecting.</returns>
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
                if (BudgetTight(0.75)) break;   // finish next tick rather than overrun
            }

            // Done when the ejectors have drained and we found nothing new to add.
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
            // Iterate backwards: transferring removes items and shifts every index
            // above it, which silently skips entries if you walk forwards.
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


        // ---------------------------------------------------------------------------
        //  UNLOADING AT BASE
        // ---------------------------------------------------------------------------

        /// <summary>
        /// Push everything into base storage. Returns true when the ship is empty (or
        /// as empty as it is going to get, if the base is full).
        /// </summary>
        bool UnloadToBase()
        {
            if (dockConnector == null || dockConnector.Status != MyShipConnectorStatus.Connected)
                return false;

            // Base containers: reachable through the terminal system while docked, but
            // explicitly not part of our own construct.
            blockScratch.Clear();
            GridTerminalSystem.GetBlocksOfType(blockScratch,
                b => b is IMyCargoContainer && !b.IsSameConstructAs(Me));

            // The far connector is a valid destination in its own right and is the only
            // one that exists on a base whose storage sits behind a sorter.
            IMyShipConnector far = dockConnector.OtherConnector;
            if (far != null) blockScratch.Add(far);

            if (blockScratch.Count == 0)
            {
                // Nothing to push into. Not a fault — some bases just want the ship to
                // sit there while a sorter drains it.
                return cargoFill < 0.02;
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
            return cargoFill < 0.02;
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

        // ---------------------------------------------------------------------------
        //  SERVICING
        // ---------------------------------------------------------------------------

        /// <summary>Put batteries on recharge while docked, back to auto when leaving.</summary>
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

        /// <summary>Let base hydrogen flow into our tanks while docked.</summary>
        void SetTanksFilling(bool filling)
        {
            for (int i = 0; i < hydrogenTanks.Count; i++)
            {
                IMyGasTank t = hydrogenTanks[i];
                if (!t.IsFunctional) continue;
                // Stockpile pulls gas in and refuses to give any back, which is exactly
                // what we want at the pump and exactly wrong once we undock.
                if (t.Stockpile != filling) t.Stockpile = filling;
            }
        }

        // ---------------------------------------------------------------------------
        //  FUEL MODEL
        // ---------------------------------------------------------------------------

        /// <summary>
        /// Learn how much hydrogen this ship burns per metre, by watching it fly.
        ///
        /// No configuration and no assumptions about thruster count: a ship with forty
        /// hydrogen thrusters measures a high rate and turns for home early, while a
        /// frugal one runs until it is genuinely low. Sampled over long intervals so
        /// that hovering, drilling and station-keeping average out.
        /// </summary>
        void UpdateFuelModel()
        {
            if (hydrogenTanks.Count == 0) return;

            if (hydroSamplePos == Vector3D.Zero)
            {
                hydroSamplePos = shipPos;
                hydroSampleFill = hydrogenFill;
                return;
            }

            double travelled = Vector3D.Distance(shipPos, hydroSamplePos);
            if (travelled < 150.0) return;              // too short to mean anything

            double used = hydroSampleFill - hydrogenFill;
            hydroSamplePos = shipPos;
            hydroSampleFill = hydrogenFill;

            // Refuelling, or a generator outpacing the thrusters. Nothing to learn.
            if (used <= 0) return;

            double rate = used / travelled;

            // Exponential moving average. A single leg through a gravity well is not
            // representative of the whole route, and neither is a lazy drift in space.
            hydroPerMetre = hydroCalibrated ? hydroPerMetre * 0.7 + rate * 0.3 : rate;
            hydroCalibrated = true;
        }

        /// <summary>
        /// Fraction of a tank needed to fly the recorded route home from here, with
        /// margin. Returns 0 until the burn rate has been measured.
        /// </summary>
        double FuelToGetHome()
        {
            if (!hydroCalibrated || hydrogenTanks.Count == 0) return 0;

            double distance = DistanceHomeAlongPath();
            if (distance <= 0) return 0;

            // 1.6x. The return leg is the loaded one, and a loaded ship burns more than
            // the empty one that measured the rate on the way out.
            return distance * hydroPerMetre * 1.6;
        }

        /// <summary>True when we have only just enough fuel left to reach the dock.</summary>
        bool FuelCriticalForReturn()
        {
            double need = FuelToGetHome();
            if (need <= 0) return false;
            // Five points of tank held back for docking manoeuvres on arrival.
            return hydrogenFill < need + 0.05;
        }

        bool ServiceComplete()
        {
            return batteryFill >= resumeBattery && hydrogenFill >= resumeHydrogen;
        }
        #endregion

        #region 09_Job.cs
        // ============================================================================
        //  JOB GEOMETRY AND SHAFT SELECTION
        //
        //  PAM digs a rectangle in a fixed order. SCAM adapts depth per shaft but still
        //  works through the whole area. Both spend the same effort on rock that turns
        //  out to be empty as on rock that is full of gold.
        //
        //  VEIN treats the site as something to be *surveyed*: cheap shallow probes on a
        //  coarse lattice, then deep production shafts placed where the survey says ore
        //  actually is. On a typical scattered deposit that is the difference between
        //  digging 100 holes and digging 30.
        // ============================================================================

        /// <summary>
        /// Capture the current position and attitude as the job frame.
        /// Whatever direction the drills are pointing becomes "down the shaft", so this
        /// works identically on a planet surface and on the side of an asteroid.
        /// </summary>
        void SetJob(int width, int height, int depth)
        {
            if (controller == null) { Log("Cannot set job: no controller"); return; }

            MatrixD m = controller.WorldMatrix;

            job.IsSet = true;
            job.Origin = DrillFace();
            job.Down = Vector3D.Normalize(m.Forward);     // drills point forward
            job.Right = Vector3D.Normalize(m.Right);
            job.Forward = Vector3D.Normalize(m.Up);
            job.Gravity = gravity;
            job.Width = Math.Max(1, width);
            job.Height = Math.Max(1, height);
            job.Depth = Math.Max(1, depth);

            MeasureShip();                  // refresh drill geometry
            job.Spacing = derivedSpacing;   // fix the pitch once, here
            RebuildCells();

            activeCell = -1;
            jobComplete = false;
            probePassDone = false;

            Log("Job " + job.Width + "x" + job.Height + " @" + job.Depth + "m, pitch "
                + Fmt(job.Spacing, 1) + "m");
        }

        /// <summary>
        /// Normalise and sanity-check a job frame that came from outside — restored
        /// Storage or a dispatcher beacon. Truncated or corrupt data yields zero-length
        /// or non-perpendicular axes, and nothing downstream checks: CellMouth collapses
        /// every shaft onto the origin and Orient is handed a zero forward, so the ship
        /// flies to one point and sits there with no indication why.
        /// </summary>
        /// <returns>False if the frame is unusable, in which case the job is cleared.</returns>
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

            // Millimetre wire precision costs a little orthogonality; a badly skewed
            // frame means the data is wrong, not merely rounded.
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

        /// <summary>Cells scored per selection pass. Bounds the cost so a small-grid
        /// job with hundreds of cells cannot exceed the instruction limit.</summary>
        const int SCORE_BUDGET = 48;
        /// <summary>Rotating start point for the bounded scan.</summary>
        int scoreCursor;

        int CellCol(int idx) { return idx % job.Width; }
        int CellRow(int idx) { return idx / job.Width; }

        // ---------------------------------------------------------------------------
        //  SELECTION
        // ---------------------------------------------------------------------------

        /// <summary>
        /// Choose the next shaft to dig. Returns -1 when the job is finished.
        /// Also decides whether that shaft is a cheap probe or a full-depth production
        /// hole, via <see cref="shaftIsProbe"/>.
        /// </summary>
        int SelectNextCell()
        {
            return SelectNextCell(shipPos);
        }

        /// <summary>
        /// Choose the next shaft, measuring travel from <paramref name="origin"/>.
        /// The dispatcher passes the requesting drone's position so that work is handed
        /// to whoever is closest to it, rather than to whoever asked first.
        /// </summary>
        int SelectNextCell(Vector3D origin)
        {
            selectionOrigin = origin;
            if (cells.Length != job.CellCount) RebuildCells();

            switch (holeOrder)
            {
                case HoleOrder.Serpentine: shaftIsProbe = false; return NextSerpentine();
                case HoleOrder.Spiral:     shaftIsProbe = false; return NextSpiral();
                default:                                          return NextProspect();
            }
        }

        /// <summary>Corner to corner, reversing each row. Least total travel.</summary>
        int NextSerpentine()
        {
            for (int row = 0; row < job.Height; row++)
            {
                for (int i = 0; i < job.Width; i++)
                {
                    // Reverse odd rows so the ship finishes each row next to the start
                    // of the following one instead of flying all the way back.
                    int col = (row % 2 == 0) ? i : job.Width - 1 - i;
                    int idx = job.IndexOf(col, row);
                    if (cells[idx].Available) return idx;
                }
            }
            return -1;
        }

        /// <summary>Outward square spiral from the centre. Good for a centred deposit.</summary>
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

                // Turn at the corners of the growing square.
                if (x == y || (x < 0 && x == -y) || (x > 0 && x == 1 - y))
                {
                    int t = dx; dx = -dy; dy = t;
                }
                x += dx; y += dy;
            }
            return -1;
        }

        /// <summary>
        /// Survey first, then mine the good parts.
        ///
        /// Phase 1 walks a coarse lattice, drilling shallow probes. Phase 2 scores every
        /// remaining cell from what the probes found and digs the best one next.
        /// </summary>
        int NextProspect()
        {
            // ---- Phase 1: survey ---------------------------------------------------
            if (!probePassDone)
            {
                int probe = NextProbeCell();
                if (probe >= 0) { shaftIsProbe = true; return probe; }
                probePassDone = true;
                Log("Survey complete: " + RichCellCount() + " rich of " + ProbedCellCount() + " probed");
            }

            // ---- Phase 2: production ----------------------------------------------
            shaftIsProbe = false;

            int best = -1;
            double bestScore = double.MinValue;

            // Scoring every cell is O(cells x neighbourhood), which is fine on a
            // large-grid job of 80 cells and fatal on a small-grid one. A small drill
            // head cuts a ~1.1 m pitch, so a modest 20 x 20 m site is over 300 cells and
            // a full sweep runs to six figures of instructions — well past the 50,000
            // limit, and the block is killed for complexity.
            //
            // So: score the neighbourhood of the best cell we know about, then a bounded
            // rotating window of everything else. The neighbourhood term is what
            // preserves the important behaviour — following a vein once it is found —
            // while the window keeps the cost flat regardless of job size.
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

            // Nothing scored well in this window, but work remains somewhere. Take the
            // first available cell rather than reporting the job finished — a bounded
            // scan must never be able to end a job early.
            if (best < 0 && RemainingCellCount() > 0)
            {
                for (int i = 0; i < cells.Length; i++)
                    if (cells[i].Available) return i;
            }

            // Everything left is written off as barren. That is a finished job, not a
            // failure — the whole point is to stop digging rock that has nothing in it.
            return best;
        }

        /// <summary>Fold one candidate into the running best, if it qualifies.</summary>
        void Consider(int idx, ref int best, ref double bestScore)
        {
            if (idx < 0 || idx >= cells.Length) return;
            if (!cells[idx].Available) return;

            double score = ScoreCell(idx);
            // A cell already judged barren from its own probe is only worth revisiting
            // if its neighbours turned out rich.
            if (cells[idx].State == CellState.Barren && score < barrenThreshold) return;
            if (score < bestScore) return;

            bestScore = score;
            best = idx;
        }

        /// <summary>Highest-yielding cell found so far, or -1. Deliberately cheap — a
        /// field compare per cell, no neighbourhood maths.</summary>
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

        /// <summary>
        /// Lattice coarseness in cells, derived so probes land a sensible distance
        /// apart in *metres* whatever the drill head is doing.
        ///
        /// probeStride is configured in cells, but cell size is set by the drill's cut
        /// radius — 3.2 m on a large-grid head, 1.1 m on a small one. Probing every
        /// second cell means a test shaft every 6 m on the former and every 2 m on the
        /// latter, which is far more survey than the ground warrants. The configured
        /// value acts as a floor; this raises it when cells are fine.
        /// </summary>
        int EffectiveProbeStride()
        {
            const double TARGET_SPACING_M = 8.0;
            int byDistance = (int)Math.Round(TARGET_SPACING_M / Math.Max(0.5, job.Spacing));
            return Math.Max(1, Math.Max(probeStride, byDistance));
        }

        /// <summary>Next un-probed lattice point, nearest to the ship first.</summary>
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

        /// <summary>
        /// Expected value of digging this cell, in kg of ore per metre.
        ///
        /// Built from three signals, best first:
        ///   1. Real ore coordinates, if the Ore Detector Raycast mod gave us any.
        ///   2. Inverse-distance-weighted yield of nearby cells we have already dug.
        ///   3. A small optimism prior, so untouched ground is preferred over ground we
        ///      have already proved empty.
        /// Travel distance is then subtracted so that, all else equal, the ship digs the
        /// near candidate rather than flying across the site for the same result.
        /// </summary>
        double ScoreCell(int idx)
        {
            int col = CellCol(idx), row = CellRow(idx);

            const int RADIUS = 2;       // 5x5 neighbourhood; O(25), not O(n)
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
                    if (cell.MetresDrilled < 0.5f) continue;   // never sampled, says nothing

                    int dc = c - col, dr = r - row;
                    double d2 = dc * dc + dr * dr;
                    double w = 1.0 / (1.0 + d2);
                    weighted += w * cell.Yield;
                    weight += w;
                }
            }

            // With no evidence either way, assume slightly better than the cut-off. That
            // biases the ship toward exploring unknown ground rather than re-chewing
            // ground next to a single lucky hit.
            double estimate = weight > 0 ? weighted / weight : barrenThreshold * 1.5;

            estimate += SightingBonus(col, row);

            // Travel cost, expressed in the same units as yield so they can be compared.
            double travel = Vector3D.Distance(job.CellMouth(col, row, 0), selectionOrigin);
            estimate -= travel * 0.004;

            return estimate;
        }

        /// <summary>
        /// Boost from confirmed ore positions. Only ever non-zero when the Ore Detector
        /// Raycast mod is present and has actually seen something.
        /// </summary>
        double SightingBonus(int col, int row)
        {
            if (sightings.Count == 0) return 0;

            Vector3D mouth = job.CellMouth(col, row, 0);
            double bonus = 0;

            for (int i = 0; i < sightings.Count; i++)
            {
                // Distance measured on the job plane only — a deposit 40 m straight down
                // is still directly under this cell and absolutely counts.
                Vector3D delta = sightings[i].Position - mouth;
                double along = Vector3D.Dot(delta, job.Down);
                Vector3D lateral = delta - job.Down * along;

                double lat = lateral.Length();
                if (lat > job.Spacing * 4) continue;
                if (along < -job.Spacing || along > job.Depth + 20) continue;

                // Big, decaying with lateral offset. Confirmed ore should dominate
                // every other term in the score.
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

        // ---------------------------------------------------------------------------
        //  RESULT RECORDING
        // ---------------------------------------------------------------------------

        /// <summary>
        /// Fold the outcome of a finished shaft back into the map.
        ///
        /// Every input is an explicit parameter. An earlier version read the miner's
        /// live shaft fields instead, which was fine on a solo miner and quietly wrong
        /// on a dispatcher, where those fields describe nothing at all.
        /// </summary>
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
                    // A probe that hit ore is a lead, not a finished hole — leave it open
                    // so phase 2 comes back and takes it to full depth.
                    if (wasProbe)
                        cell.State = cell.Yield >= barrenThreshold ? CellState.Rich : CellState.Barren;
                    else
                        cell.State = CellState.Exhausted;
                    break;

                case ShaftResult.OreExhausted:
                    cell.State = cell.Yield >= barrenThreshold ? CellState.Exhausted : CellState.Barren;
                    break;

                case ShaftResult.CargoFull:
                    // Unfinished. Deliberately left Available so we resume it later.
                    cell.State = cell.Yield >= barrenThreshold ? CellState.Rich : CellState.Unknown;
                    break;

                case ShaftResult.Stuck:
                    cell.StuckCount++;
                    cell.State = cell.StuckCount >= 3 ? CellState.Blocked : CellState.Unknown;
                    break;

                case ShaftResult.Aborted:
                    if (cell.State == CellState.Leased) cell.State = CellState.Unknown;
                    break;
            }

            if (role == Role.Miner && dispatcherAddr != 0)
                SendShaftReport(idx, result, oreKg, metres, depthReached, wasProbe);
        }

        /// <summary>Flag the current cell as somewhere the ship keeps getting stuck.</summary>
        void MarkCellStuck()
        {
            if (activeCell < 0 || activeCell >= cells.Length) return;
            cells[activeCell].StuckCount++;
            if (cells[activeCell].StuckCount >= 3)
            {
                cells[activeCell].State = CellState.Blocked;
                Log("Cell " + CellCol(activeCell) + "," + CellRow(activeCell) + " blocked");
            }
        }
        #endregion

        #region 10_Path.cs
        // ============================================================================
        //  PATH RECORDING AND FOLLOWING
        //
        //  You fly the route once; the ship flies it forever after. This is PAM's best
        //  idea and it is worth stealing wholesale: no pathfinder can know that the gap
        //  between those two ridges is the safe way home, but a pilot does.
        //
        //  What we add is the lift survey. At every recorded point we note how many
        //  newtons of upward thrust the ship actually had there. That single number is
        //  what lets the ship refuse a load it cannot climb out with, instead of finding
        //  out at 200 m with a full hold.
        // ============================================================================

        /// <summary>Metres of travel between recorded points.</summary>
        double RecordInterval { get { return Math.Max(5.0, shipRadius * 2.0); } }

        void StartRecording()
        {
            path.Clear();
            recording = true;
            maxFlyableMass = -1;

            // Waypoint zero is the dock. Capture its full frame so we can line up on the
            // connector later from the right side and the right way up.
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
                + (maxFlyableMass > 0 ? "lift limit " + Fmt(maxFlyableMass / 1000.0, 1) + "t" : "no gravity on route"));
        }

        void CaptureHomeDock()
        {
            if (controller == null) return;

            if (dockConnector != null)
            {
                MatrixD c = dockConnector.WorldMatrix;
                homeDock = new Waypoint(dockConnector.GetPosition(), gravity, SampleEfficiency(), (float)CurrentLift());
                // The connector's forward is the direction it mates along. Approaching
                // down that axis is the only way to dock reliably.
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
            homeDockSet = true;
        }

        /// <summary>Called every tick while recording.</summary>
        void RecordTick()
        {
            if (!recording) return;
            if (path.Count == 0) { AddWaypoint(true); return; }
            if (Vector3D.DistanceSquared(shipPos, lastRecordPos) < RecordInterval * RecordInterval) return;

            // Hard cap. An operator who forgets to stop recording should not be able to
            // grow this until the block runs out of instruction budget serialising it.
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

        /// <summary>Upward thrust available right now, in newtons. Zero in space.</summary>
        double CurrentLift()
        {
            if (gravity.LengthSquared() < 1e-6) return 0;
            return ThrustAlong(-Vector3D.Normalize(gravity));
        }

        /// <summary>
        /// Per-thruster-type effectiveness here. Purely diagnostic — it is what lets the
        /// display say "atmospherics dead above this point" instead of just refusing to
        /// fly with no explanation.
        /// </summary>
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

        /// <summary>
        /// The heaviest the ship may be and still fly the whole route.
        ///
        /// Taken as the minimum over every recorded point of (lift / gravity), because
        /// the route is only as flyable as its worst spot — usually the point where the
        /// atmospheric thrusters have thinned out but you are still deep in the well.
        /// </summary>
        void ComputeMaxFlyableMass()
        {
            maxFlyableMass = -1;

            for (int i = 0; i < path.Count; i++)
            {
                Waypoint wp = path[i];
                double g = wp.Gravity.Length();
                if (g < 0.05) continue;              // space, or near enough
                if (wp.Lift <= 0) continue;

                double capable = (wp.Lift / g) * liftSafetyFactor;
                if (maxFlyableMass < 0 || capable < maxFlyableMass) maxFlyableMass = capable;
            }

            if (homeDockSet && homeDock != null && homeDock.Gravity.Length() >= 0.05 && homeDock.Lift > 0)
            {
                double capable = (homeDock.Lift / homeDock.Gravity.Length()) * liftSafetyFactor;
                if (maxFlyableMass < 0 || capable < maxFlyableMass) maxFlyableMass = capable;
            }
        }

        /// <summary>
        /// Cargo mass we can still take on before the route stops being flyable.
        /// Feeds the "go home now" decision, so a miner in heavy gravity leaves early
        /// with a part load rather than filling up and stranding itself.
        /// </summary>
        double RemainingLiftMargin()
        {
            if (maxFlyableMass <= 0) return double.MaxValue;
            return maxFlyableMass - shipMass;
        }

        bool OverLiftLimit()
        {
            return maxFlyableMass > 0 && shipMass >= maxFlyableMass;
        }

        // ---------------------------------------------------------------------------
        //  FOLLOWING
        // ---------------------------------------------------------------------------

        /// <summary>
        /// Precompute distance from the dock to each waypoint, so "how far is home"
        /// is a lookup rather than a walk of the whole path every tick.
        /// </summary>
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

        /// <summary>
        /// Metres still to fly to reach the dock, following the recorded route rather
        /// than the straight line — which is the distance that actually costs fuel.
        /// </summary>
        double DistanceHomeAlongPath()
        {
            if (path.Count == 0) return 0;
            if (pathCumulative.Length != path.Count) BuildPathDistances();

            int idx = Math.Max(0, Math.Min(path.Count - 1, pathIndex));
            // Route distance from waypoint 0, plus however far off that waypoint we are.
            return pathCumulative[idx] + Vector3D.Distance(shipPos, path[idx].Position);
        }

        double WaypointReached { get { return Math.Max(4.0, shipRadius * 1.5); } }

        /// <summary>Nearest waypoint to the ship. Used to rejoin the route from wherever
        /// we happen to be, rather than insisting on starting from one end.</summary>
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

        /// <summary>
        /// Fly the recorded route. <paramref name="outbound"/> runs dock to job,
        /// otherwise job to dock.
        /// </summary>
        /// <returns>True once the far end has been reached.</returns>
        bool FollowPath(bool outbound)
        {
            if (path.Count == 0) return true;

            pathIndex = Math.Max(0, Math.Min(path.Count - 1, pathIndex));
            Waypoint wp = path[pathIndex];

            double dist = Vector3D.Distance(shipPos, wp.Position);

            if (dist < WaypointReached)
            {
                int next = outbound ? pathIndex + 1 : pathIndex - 1;
                if (next < 0 || next >= path.Count) return true;      // arrived
                pathIndex = next;
                wp = path[pathIndex];
            }

            // Look ahead one point so the ship carves the corner rather than stopping
            // dead at every waypoint. Makes a long route enormously faster and stops the
            // stop-start lurching that shakes ore out of open drills.
            Vector3D aim = wp.Position;
            int ahead = outbound ? pathIndex + 1 : pathIndex - 1;
            if (ahead >= 0 && ahead < path.Count && dist > WaypointReached * 1.5)
            {
                Vector3D toNext = Vector3D.Normalize(path[ahead].Position - wp.Position);
                aim = wp.Position + toNext * Math.Min(WaypointReached, dist * 0.3);
            }

            double speedLimit = cruiseSpeed;

            // Slow down for the ends of the route, where the interesting obstacles are.
            int fromEnd = outbound ? path.Count - 1 - pathIndex : pathIndex;
            if (fromEnd <= 1) speedLimit = Math.Min(speedLimit, 15.0);

            FlyTo(aim, speedLimit);

            // Fly nose-first along the direction of travel: it keeps the drills pointing
            // where we are going, which is where a collision would come from.
            Vector3D heading = aim - shipPos;
            if (heading.LengthSquared() > 4.0)
                Orient(heading, gravity.LengthSquared() > 1e-6 ? -gravity : Vector3D.Zero);

            return false;
        }

        /// <summary>Set up the path index for a run in the given direction.</summary>
        void BeginPath(bool outbound)
        {
            if (path.Count == 0) { pathIndex = 0; return; }

            int nearest = NearestWaypoint();
            double distToNearest = Vector3D.Distance(path[nearest].Position, shipPos);

            // Close to the route: rejoin where we are. Far from it: start from the end
            // we are supposed to be starting from and fly in.
            if (distToNearest < WaypointReached * 4)
                pathIndex = nearest;
            else
                pathIndex = outbound ? 0 : path.Count - 1;
        }
        #endregion

        #region 11_Miner.cs
        // ============================================================================
        //  MINER STATE MACHINE
        //
        //  Every state is bounded in time by the watchdog, every state can be entered
        //  from a cold start, and every state leaves the ship recoverable if the script
        //  is recompiled halfway through it. That last property is the one that makes
        //  the difference between a script you trust overnight and one you babysit.
        // ============================================================================

        void TickMiner()
        {
            stateTicks++;

            if (recording) RecordTick();

            CheckDamage();
            Watchdog();
            UpdateOreScan();
            EjectWhileFlying();
            if (!Docked) UpdateFuelModel();

            // Belt and braces: if we are off the connector, the thrusters are on. Full
            // stop. Something else — an Event Controller, a timer, a player — is free to
            // switch them off while docked to save power, but the moment we are flying
            // this is not negotiable. Cheap, because it only writes on a change.
            if (!Docked && state != MinerState.Idle && state != MinerState.Fault)
                SetThrusters(true);

            bool entry = stateEntry;
            stateEntry = false;

            switch (state)
            {
                case MinerState.Idle:        StIdle(entry); break;
                case MinerState.Undocking:   StUndocking(entry); break;
                case MinerState.Outbound:    StOutbound(entry); break;
                case MinerState.Selecting:   StSelecting(entry); break;
                case MinerState.Approaching: StApproaching(entry); break;
                case MinerState.Descending:  StDescending(entry); break;
                case MinerState.Ascending:   StAscending(entry); break;
                case MinerState.Inbound:     StInbound(entry); break;
                case MinerState.Docking:     StDocking(entry); break;
                case MinerState.Unloading:   StUnloading(entry); break;
                case MinerState.Servicing:   StServicing(entry); break;
                case MinerState.Fault:       StFault(entry); break;
            }

            SendHeartbeat();
        }

        // ---------------------------------------------------------------------------

        void StIdle(bool entry)
        {
            if (entry) { SafeStop(); statusLine = "Idle"; }
            if (!jobRunning || jobComplete) return;

            Health h = CheckReadiness();
            if (!h.Ok) { statusLine = "Not ready: " + h.Detail; return; }

            SetState(Docked ? MinerState.Undocking : MinerState.Outbound);
        }

        bool Docked
        {
            get { return dockConnector != null && dockConnector.Status == MyShipConnectorStatus.Connected; }
        }

        // ---------------------------------------------------------------------------

        void StUndocking(bool entry)
        {
            if (entry)
            {
                statusLine = "Undocking";
                // Thrusters first, before anything else, and before we let go of the
                // connector. Plenty of people switch thrusters off while docked — by
                // hand or with an Event Controller — to save power. Undocking into
                // gravity with them still off is a long fall.
                SetThrusters(true);
                SetBatteryCharging(false);
                SetTanksFilling(false);       // stop hoarding, we need the gas now
                if (Docked) dockConnector.Disconnect();
                stuckRefDepth = 0;
            }

            if (Docked) { dockConnector.Disconnect(); return; }

            // Back straight out along the connector axis. Turning while still inside a
            // docking cradle is how ships lose landing gear.
            Vector3D away = dockConnector != null
                ? dockConnector.WorldMatrix.Forward
                : (controller != null ? controller.WorldMatrix.Up : Vector3D.Up);

            Vector3D clear = homeDock != null ? homeDock.Position : shipPos;
            double travelled = Vector3D.Distance(shipPos, clear);
            double needed = shipRadius * 2.5;

            if (travelled >= needed)
            {
                BeginPath(true);
                SetState(MinerState.Outbound);
                return;
            }

            FlyTo(clear + away * needed, dockSpeed * 2.0);
        }

        // ---------------------------------------------------------------------------

        void StOutbound(bool entry)
        {
            if (entry) { statusLine = "Outbound"; BeginPath(true); SetDrills(false); }

            // Turning back before arriving beats arriving with nothing left to get home.
            if (!HasReservesForWork()) { SetState(MinerState.Inbound); return; }

            if (FollowPath(true)) SetState(MinerState.Selecting);
        }

        // ---------------------------------------------------------------------------

        void StSelecting(bool entry)
        {
            if (entry)
            {
                statusLine = "Choosing shaft";
                activeCell = -1;
                awaitingLease = false;
            }

            // ---- Fleet: ask the dispatcher, do not self-assign ---------------------
            if (HasDispatcher)
            {
                if (!awaitingLease)
                {
                    RequestLease();
                    awaitingLease = true;
                    lastRequestTick = tick;
                    return;
                }

                if (activeCell >= 0) { awaitingLease = false; BeginShaft(); return; }

                // Retry, then fall back. A dispatcher that has stopped answering must not
                // be able to park the whole fleet indefinitely.
                if (tick - lastRequestTick > 60)
                {
                    if (tick - lastDispatcherSeenTick > (long)(droneTimeout / Math.Max(dt, 0.01)))
                    {
                        Log("Dispatcher lost — continuing solo");
                        dispatcherAddr = 0;
                    }
                    awaitingLease = false;
                }
                return;
            }

            // ---- Solo -------------------------------------------------------------
            int cell = SelectNextCell();
            if (cell < 0)
            {
                jobComplete = true;
                Log("Job complete");
                SetState(MinerState.Inbound);
                return;
            }

            activeCell = cell;
            cells[cell].State = CellState.Leased;
            BeginShaft();
        }

        /// <summary>Set up the per-shaft counters and head for the hole.</summary>
        void BeginShaft()
        {
            SampleOre();          // exact figure to measure this shaft's yield against
            shaftDepth = 0;
            shaftMaxDepth = 0;
            shaftStartOre = oreAboard;
            lastOreSample = 0;
            lastOreGainDepth = 0;
            noOreTicks = 0;
            stuckRetries = 0;

            shaftContactDepth = -1;

            // Metres of rock to cut, not depth from the job plane. A resumed shaft needs
            // no special handling: the already-cut section returns no material, so
            // contact is simply detected again at the old bottom.
            shaftDepthLimit = shaftIsProbe ? Math.Min(probeDepth, job.Depth) : job.Depth;

            SetState(MinerState.Approaching);
        }

        // ---------------------------------------------------------------------------

        void StApproaching(bool entry)
        {
            if (activeCell < 0) { SetState(MinerState.Selecting); return; }
            if (entry) { statusLine = "To shaft " + CellLabel(activeCell); SetDrills(false); }

            if (!HasReservesForWork()) { AbandonShaft(ShaftResult.Aborted); return; }

            int col = CellCol(activeCell), row = CellRow(activeCell);

            // Approach at our own altitude lane so two drones crossing the site are
            // never at the same height. Cheap, and it removes the entire class of
            // mid-air collisions that swarm scripts are notorious for.
            double standoff = transitAltitude + myLane;
            Vector3D above = job.CellMouth(col, row, standoff);

            FlyTo(ControllerTargetFor(above), cruiseSpeed * 0.5);
            Orient(job.Down, job.Forward);

            // Only start cutting once we are both over the hole and square to it.
            // Descending at an angle is what wedges a ship halfway down a shaft.
            bool overHole = distToTarget < Math.Max(1.5, shipRadius * 0.4);
            bool square = alignError < 4.0;

            if (!overHole || !square) return;

            // Last check before committing: is there anything down there at all?
            // The drills point along the shaft axis and so does a forward camera, so a
            // single raycast answers it. On an asteroid — where the job rectangle
            // routinely overhangs empty space — this saves a full descend/ascend cycle
            // per empty cell. It is skipped when no camera has charge, which costs
            // nothing but the old behaviour.
            if (!ShaftHasRock(standoff)) { SkipEmptyCell(); return; }

            SetState(MinerState.Descending);
        }

        /// <summary>
        /// True if solid material lies within reach down the shaft, or if we could not
        /// tell. Never returns false on a failed or unavailable scan — refusing to mine
        /// because a camera was busy would be far worse than digging one dry hole.
        /// </summary>
        bool ShaftHasRock(double standoff)
        {
            if (cameras.Count == 0) return true;

            double reach = standoff + Math.Min(shaftDepthLimit, 60.0);

            double hit;
            if (!TryScanAhead(reach, out hit)) return true;   // no camera had charge

            if (hit < 0) return false;                        // scanned, genuinely empty
            return hit <= standoff + 8.0;                     // rock starts about where expected
        }

        /// <summary>Write off a cell that turned out to be open space and move on.</summary>
        void SkipEmptyCell()
        {
            Log(CellLabel(activeCell) + " is open space — skipping");
            RecordShaftResult(activeCell, ShaftResult.Completed, 0, 0, 0, shaftIsProbe);
            if (activeCell >= 0 && activeCell < cells.Length)
                cells[activeCell].State = CellState.Barren;
            ReleaseLeaseLocal();
            activeCell = -1;
            SetState(MinerState.Selecting);
        }

        // ---------------------------------------------------------------------------

        void StDescending(bool entry)
        {
            if (activeCell < 0) { SetState(MinerState.Selecting); return; }

            int col = CellCol(activeCell), row = CellRow(activeCell);

            if (entry)
            {
                statusLine = (shaftIsProbe ? "Probing " : "Drilling ") + CellLabel(activeCell);
                stuckRefDepth = CurrentShaftDepth(col, row);
                stuckTicks = 0;
                lastOreSample = ShaftOreSoFar();
                shaftContactDepth = -1;
                shaftStartVolume = cargoVolume;
            }

            shaftDepth = CurrentShaftDepth(col, row);
            if (shaftDepth > shaftMaxDepth) shaftMaxDepth = shaftDepth;

            // First material back means we have reached the real surface. On a planet
            // that is within a metre of the job plane and this barely matters; on an
            // asteroid the surface wanders tens of metres either side of it, and
            // without this every measurement below is taken from the wrong datum.
            if (shaftContactDepth < 0 && cargoVolume > shaftStartVolume + 0.001)
                shaftContactDepth = shaftDepth;

            // We enter this state from the traffic-separation altitude, which can be
            // 25 m or more above the surface. Descending that gap at cutting speed with
            // the drills spinning wastes twenty seconds and a chunk of power per shaft.
            // Drop fast through the air, then slow down and switch on at the rock.
            bool inRock = shaftDepth > -2.0;
            SetDrills(inRock);
            double descentSpeed = inRock ? drillSpeed : Math.Min(12.0, Math.Max(retreatSpeed, 6.0));

            // ---- Stop conditions, most urgent first --------------------------------
            if (!HasReservesForWork()) { AbandonShaft(ShaftResult.Aborted); return; }
            if (CargoFull || OverLiftLimit()) { AbandonShaft(ShaftResult.CargoFull); return; }
            if (shaftDepth >= EffectiveDepthLimit()) { AbandonShaft(ShaftResult.Completed); return; }
            if (DepthExhausted()) { AbandonShaft(ShaftResult.OreExhausted); return; }

            // ---- Stuck handling ----------------------------------------------------
            // Only meaningful once we are actually cutting. In open air above the hole
            // there is nothing to be stuck on, and the check would misfire while the
            // ship is still accelerating downward.
            if (inRock && IsStuck())
            {
                stuckRetries++;
                if (stuckRetries > 3) { AbandonShaft(ShaftResult.Stuck); return; }

                Log("Stuck at " + Fmt(shaftDepth, 1) + "m, backing off (" + stuckRetries + "/3)");
                // Back up two metres and come at it again. Usually enough to clear a
                // boulder edge the drills were grinding against without progress.
                Vector3D relief = job.CellDepth(col, row, Math.Max(0, shaftDepth - 2.0));
                FlyTo(ControllerTargetFor(relief), retreatSpeed);
                Orient(job.Down, job.Forward);
                stuckTicks = 0;
                stuckRefDepth = shaftDepth - 2.0;
                return;
            }

            // ---- Normal cutting ----------------------------------------------------
            // Aim a little past where we are rather than at the bottom of the shaft, so
            // the velocity controller holds a steady cutting speed instead of easing off
            // as it approaches a distant target. Clamped at zero so that while we are
            // still above the surface the aim point is inside the rock, not behind us.
            double aimDepth = Math.Min(EffectiveDepthLimit(), Math.Max(shaftDepth, 0.0) + 5.0);
            Vector3D bite = job.CellDepth(col, row, aimDepth);
            FlyTo(ControllerTargetFor(bite), descentSpeed);
            Orient(job.Down, job.Forward);
        }

        // ---------------------------------------------------------------------------

        void StAscending(bool entry)
        {
            if (entry)
            {
                statusLine = "Withdrawing";
                SetDrills(drillOnRetreat);
            }

            if (activeCell < 0) { SetState(MinerState.Selecting); return; }

            int col = CellCol(activeCell), row = CellRow(activeCell);
            double depth = CurrentShaftDepth(col, row);

            double standoff = transitAltitude + myLane;
            Vector3D clearOfHole = job.CellMouth(col, row, standoff);

            // Climb the shaft axis exactly. Any lateral drift on the way up and the ship
            // wedges itself against the wall it just cut.
            Vector3D exitPoint = depth > 1.0
                ? job.CellDepth(col, row, Math.Max(0, depth - 6.0))
                : clearOfHole;

            FlyTo(ControllerTargetFor(exitPoint), retreatSpeed);
            Orient(job.Down, job.Forward);

            if (depth > 1.0) return;

            // Clear of the hole. Decide where to go next.
            SetDrills(false);
            FinishShaft();
        }

        /// <summary>Commit the shaft result and pick the next activity.</summary>
        void FinishShaft()
        {
            ShaftResult result = pendingResult;
            SampleOre();          // exact figure now the shaft is finished
            double ore = ShaftOreSoFar();

            // Yield is kilograms per metre *drilled*, so the vacuum we fell through to
            // reach the surface must not count. Including it would dilute the yield of
            // every cell whose surface sits below the job plane, and the prospector
            // would then steer away from exactly the ground it should be working.
            double cut = shaftContactDepth >= 0
                ? Math.Max(0.0, shaftMaxDepth - shaftContactDepth)
                : 0.0;

            // Never made contact: the shaft was empty space all the way down. Report it
            // as a completed, barren cell rather than a stuck or failed one.
            if (shaftContactDepth < 0 && result == ShaftResult.Completed)
                Log(CellLabel(activeCell) + " never reached rock");

            RecordShaftResult(activeCell, result, ore, cut, cut, shaftIsProbe);
            ReleaseLeaseLocal();

            Log(CellLabel(activeCell) + " " + result + ": " + Fmt(ore, 0) + "kg / "
                + Fmt(cut, 1) + "m cut");

            activeCell = -1;

            if (!jobRunning) { SetState(MinerState.Inbound); return; }

            if (result == ShaftResult.Aborted || result == ShaftResult.CargoFull)
            {
                SetState(MinerState.Inbound);
                return;
            }
            if (CargoFull || OverLiftLimit() || !HasReservesForWork())
            {
                SetState(MinerState.Inbound);
                return;
            }
            SetState(MinerState.Selecting);
        }

        /// <summary>Break off the current shaft and start climbing out.</summary>
        void AbandonShaft(ShaftResult why)
        {
            pendingResult = why;
            SetState(MinerState.Ascending);
        }

        // ---------------------------------------------------------------------------

        /// <summary>
        /// Push waste into the ejectors continuously, while flying, whenever we are not
        /// docked.
        ///
        /// This replaces a dedicated "stop and dump" state. Stopping to throw stone
        /// overboard costs time for something that happens perfectly well in transit —
        /// the ejectors drain on their own clock either way. Credit where due: this is
        /// the pattern experienced players already build by hand with an Event
        /// Controller wired to "not docked", and it is plainly better than what the
        /// script was doing.
        /// </summary>
        void EjectWhileFlying()
        {
            if (ejectMode == EjectMode.Off || ejectors.Count == 0) return;
            if (Docked) return;

            // Not while cutting. Ejected stone becomes floating objects, and spraying
            // them into a shaft you are currently inside is asking for a collision.
            if (state == MinerState.Descending || state == MinerState.Ascending) return;

            // Cheap most ticks: only actually moves items every so often.
            if (tick % 20 != 0) return;
            if (BudgetTight(0.6)) return;

            EjectWaste();
        }

        // ---------------------------------------------------------------------------

        void StInbound(bool entry)
        {
            if (entry)
            {
                statusLine = "Returning";
                SetDrills(false);
                BeginPath(false);
                if (HasDispatcher) RequestDock();
            }

            if (FollowPath(false)) SetState(MinerState.Docking);
        }

        // ---------------------------------------------------------------------------

        void StDocking(bool entry)
        {
            if (entry) { statusLine = "Docking"; SetDrills(false); }

            if (dockConnector == null) { EnterFault("No connector to dock with"); return; }
            if (!homeDockSet) { EnterFault("No dock recorded"); return; }

            if (Docked)
            {
                SafeStop();
                dockRetries = 0;
                SetState(MinerState.Unloading);
                return;
            }

            // Two-stage approach: first to a point squarely off the connector face, then
            // straight down the mating axis. Coming in on a diagonal fails far more often
            // than it works.
            Vector3D mate = homeDock.Position;
            Vector3D axis = homeDockForward;
            double standoff = Math.Max(6.0, shipRadius * 2.0);

            Vector3D hold = mate + axis * standoff;
            Vector3D offAxis = shipPos - mate;
            double along = Vector3D.Dot(offAxis, axis);
            double lateral = (offAxis - axis * along).Length();

            // Our own connector has to end up on the pad, not the controller.
            Vector3D connectorOffset = dockConnector.GetPosition() - shipPos;

            if (lateral > 1.0 || along > standoff * 1.4)
            {
                FlyTo(hold - connectorOffset, dockSpeed * 3.0);
            }
            else
            {
                FlyTo(mate - connectorOffset, dockSpeed);
                if (dockConnector.Status == MyShipConnectorStatus.Connectable)
                    dockConnector.Connect();
            }

            // Face the connector the way it was facing when recorded.
            Orient(-axis, homeDockUp);
        }

        // ---------------------------------------------------------------------------

        void StUnloading(bool entry)
        {
            if (entry)
            {
                statusLine = "Unloading";
                SafeStop();
                SetBatteryCharging(true);
                SetTanksFilling(true);
            }

            if (!Docked) { SetState(MinerState.Docking); return; }

            if (UnloadToBase()) SetState(MinerState.Servicing);
        }

        // ---------------------------------------------------------------------------

        void StServicing(bool entry)
        {
            if (entry)
            {
                statusLine = "Charging";
                SetBatteryCharging(true);
                SetTanksFilling(true);
                // Parked and connected — the thrusters are dead weight drawing power
                // that we are trying to put back into the batteries. Undocking turns
                // them on again, and the guard in TickMiner catches every other route.
                SetThrusters(false);
            }

            if (!Docked) { SetState(MinerState.Docking); return; }

            // Keep draining into base storage in case a sorter is slowly feeding us.
            if (tick % 30 == 0) UnloadToBase();

            if (!jobRunning || jobComplete)
            {
                statusLine = jobComplete ? "Job complete — docked" : "Stopped — docked";
                ReleaseDock();
                return;
            }

            if (!ServiceComplete())
            {
                statusLine = "Charging " + Fmt(batteryFill * 100, 0) + "% / H2 " + Fmt(hydrogenFill * 100, 0) + "%";
                return;
            }

            ReleaseDock();
            SetState(MinerState.Undocking);
        }

        // ---------------------------------------------------------------------------

        void StFault(bool entry)
        {
            if (entry) SafeStop();
            statusLine = "FAULT: " + faultReason;
            // Deliberately does nothing else. Recovery is a human decision.
        }

        // ---------------------------------------------------------------------------
        //  SHARED PREDICATES
        // ---------------------------------------------------------------------------

        /// <summary>Enough power and gas to keep working and still get home.</summary>
        bool HasReservesForWork()
        {
            if (batteryFill < minBattery) return false;
            if (hydrogenTanks.Count > 0 && hydrogenFill < minHydrogen) return false;
            // A reactor ship with no batteries reports full power indefinitely, so
            // without this it would run its reactors dry in flight and never come home.
            if (reactors.Count > 0 && minUranium > 0 && uraniumKg < minUranium) return false;

            // The measured check, once the ship has told us how much it drinks. A fixed
            // percentage is wasteful on a short hop and fatal on a long one; this asks
            // the only question that matters — is there still enough to get home.
            if (FuelCriticalForReturn()) return false;

            return true;
        }

        /// <summary>
        /// Where this shaft stops, as a depth below the job plane.
        ///
        /// Two regimes. Before the drills touch anything we are descending through
        /// vacuum toward a surface that may sit well below the plane, and the only
        /// sensible bound is the job's own depth — no rock by then means an empty cell.
        /// Once contact is made the limit becomes metres of actual cut, so a 12 m probe
        /// really does cut 12 m of rock rather than stopping 12 m below a plane it has
        /// not reached yet.
        /// </summary>
        double EffectiveDepthLimit()
        {
            if (shaftContactDepth < 0) return job.Depth;
            return shaftContactDepth + shaftDepthLimit;
        }

        /// <summary>Depth in metres of the drill face below the mouth of shaft (col,row).</summary>
        double CurrentShaftDepth(int col, int row)
        {
            Vector3D mouth = job.CellMouth(col, row, 0);
            return Vector3D.Dot(DrillFace() - mouth, job.Down);
        }

        /// <summary>
        /// Convert a desired drill-face position into a controller position, since the
        /// flight controller flies the controller and the hole is cut by the drills.
        /// </summary>
        Vector3D ControllerTargetFor(Vector3D desiredFacePos)
        {
            return desiredFacePos - (DrillFace() - shipPos);
        }

        /// <summary>
        /// Not making progress despite being told to descend. Measured against depth
        /// rather than velocity, because a ship grinding against rock can be moving
        /// plenty while going nowhere.
        /// </summary>
        bool IsStuck()
        {
            if (shaftDepth > stuckRefDepth + 0.15)
            {
                stuckRefDepth = shaftDepth;
                stuckTicks = 0;
                return false;
            }

            stuckTicks++;
            // Roughly four seconds of no downward progress at Update10.
            return stuckTicks > 40;
        }

        /// <summary>
        /// Adaptive depth. The core trick, inherited from SCAM: the drills themselves
        /// are the ore sensor. If a shaft has stopped producing, there is nothing below
        /// worth the fuel, so stop and go somewhere else.
        /// </summary>
        bool DepthExhausted()
        {
            if (depthMode == DepthMode.Fixed) return false;

            // Still falling through vacuum toward an asteroid whose surface sits below
            // the job plane. There is nothing to be exhausted yet — judging the cell
            // here would write off good rock the drills have not even touched, which is
            // exactly how an irregular asteroid poisons the whole yield map.
            if (shaftContactDepth < 0) { lastOreGainDepth = shaftDepth; return false; }

            // The first few metres of actual rock are surface material and tell us
            // nothing. Measured from contact, not from the plane.
            if (shaftDepth < shaftContactDepth + 6.0) { lastOreGainDepth = shaftDepth; return false; }

            double now = depthMode == DepthMode.AutoOre ? ShaftOreSoFar() : cargoFill * 1000.0;

            if (now > lastOreSample + 0.5)
            {
                lastOreSample = now;
                lastOreGainDepth = shaftDepth;
                noOreTicks = 0;
                return false;
            }

            noOreTicks++;

            // Require both a dry stretch of shaft and a dry stretch of time. Depth alone
            // trips on a fast ship in soft rock; time alone trips whenever the drills
            // are momentarily jammed.
            bool dryDistance = shaftDepth - lastOreGainDepth > 5.0;
            bool dryTime = noOreTicks > 60;
            return dryDistance && dryTime;
        }

        /// <summary>React to battle damage or attrition according to policy.</summary>
        void CheckDamage()
        {
            if (!stopOnDamage) return;
            if (state == MinerState.Fault || state == MinerState.Idle) return;
            if (DamagedBlockCount() == 0) return;

            Log("Damage detected — returning");
            if (state != MinerState.Inbound && state != MinerState.Docking
                && state != MinerState.Unloading && state != MinerState.Servicing)
            {
                if (activeCell >= 0) AbandonShaft(ShaftResult.Aborted);
                else SetState(MinerState.Inbound);
            }
        }

        string CellLabel(int idx)
        {
            if (idx < 0) return "-";
            return "[" + CellCol(idx) + "," + CellRow(idx) + "]";
        }
        #endregion

        #region 12_Scout.cs
        // ============================================================================
        //  SCOUTING
        //
        //  Read this before wondering why your miner still digs the occasional dry hole.
        //
        //  Vanilla Space Engineers gives in-game scripts NO access to the Ore Detector.
        //  Keen have turned down that API more than once. There is no clever workaround:
        //  cameras raycast voxels but report "Asteroid", never "Cobalt". Any vanilla
        //  script that claims to fly straight to ore is guessing.
        //
        //  So VEIN scouts two ways, and uses whichever it has:
        //
        //    TIER 1 — Probe mapping. Always available. Shallow test shafts on a coarse
        //             lattice, ore-per-metre recorded per cell, deep shafts placed by
        //             the resulting map. Implemented in 09_Job.cs; this file feeds it.
        //
        //    TIER 2 — Ore Detector Raycast bridge. If Racher's mod is installed we get
        //             real ore coordinates and the probe map becomes a formality. We
        //             detect the mod at runtime and light up automatically.
        // ============================================================================

        const int ORE_SCAN_PERIOD = 6;       // ticks between rays; they are not cheap

        /// <summary>
        /// One-time check for the Ore Detector Raycast mod. The mod adds terminal
        /// properties to IMyOreDetector; on vanilla those properties do not exist and
        /// the setter throws. That throw is the detection mechanism.
        /// </summary>
        void ProbeOreMod()
        {
            oreModProbed = true;
            oreModAvailable = false;

            if (!useOreDetectorMod || oreDetectors.Count == 0) return;

            IMyOreDetector d = oreDetectors[0];
            try
            {
                // Write a target, read it back. A vanilla detector throws on the write;
                // a half-installed mod may accept the write and lose the value, so we
                // verify rather than trust.
                Vector3D probe = d.GetPosition() + d.WorldMatrix.Forward * 10.0;
                d.SetValue("RaycastTarget", probe);
                Vector3D echoed = d.GetValue<Vector3D>("RaycastTarget");

                if (Vector3D.DistanceSquared(echoed, probe) > 1.0) return;

                // Stone everywhere would drown out everything we care about.
                try { d.SetValue("OreBlacklist", "Stone"); } catch { }

                oreModAvailable = true;
                Log("Ore Detector Raycast mod found — true ore scouting enabled");
            }
            catch
            {
                // Vanilla. Entirely expected; probe mapping carries the load.
            }
        }

        /// <summary>Called once per tick. Cheap unless it is actually time to scan.</summary>
        void UpdateOreScan()
        {
            if (!oreModProbed) ProbeOreMod();
            if (!oreModAvailable) return;
            if (tick % ORE_SCAN_PERIOD != 0) return;
            if (BudgetTight(0.5)) return;

            ExpireSightings();

            IMyOreDetector d = oreDetectors[0];
            if (!d.IsFunctional) return;

            // The modded detector charges range like a camera does. Firing a ray we
            // cannot afford just wastes the call.
            double available;
            try { available = d.GetValue<double>("AvailableScanRange"); }
            catch { oreModAvailable = false; return; }

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
                oreModAvailable = false;
            }
        }

        /// <summary>
        /// Choose where to point the next ray.
        ///
        /// With a job set we deliberately do not sweep the sky — we rake the volume
        /// under the site, cell by cell and depth by depth, because a sighting there
        /// lands directly on the yield map and changes what we dig next. A blind 360
        /// sweep looks more impressive and is worth much less.
        /// </summary>
        bool NextScanTarget(Vector3D from, out Vector3D target, out double distance)
        {
            target = Vector3D.Zero;
            distance = 0;

            if (job.IsSet)
            {
                // Walk cells across the grid and depths down each one.
                int cellIdx = scanAzimuth % Math.Max(1, job.CellCount);
                int col = cellIdx % job.Width;
                int row = cellIdx / job.Width;

                // Four sample depths spread through the job's depth range.
                int depthStep = scanElevation % 4;
                double depth = job.Depth * (0.25 + 0.25 * depthStep);

                target = job.CellDepth(col, row, depth);

                scanElevation++;
                if (scanElevation % 4 == 0) scanAzimuth++;

                distance = Vector3D.Distance(from, target);
                return true;
            }

            // No job yet: sweep for something interesting. Coarse spiral over the sphere.
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

        /// <summary>
        /// File an ore sighting. Deduplicated by position, because sweeping the same
        /// deposit repeatedly would otherwise let one big vein dominate the score map
        /// purely by being scanned more often.
        /// </summary>
        void AddSighting(string oreType, Vector3D pos)
        {
            if (string.IsNullOrEmpty(oreType)) return;
            if (oreType.ToUpperInvariant().Contains("STONE")) return;

            double mergeRadius = Math.Max(4.0, job.Spacing);
            for (int i = 0; i < sightings.Count; i++)
            {
                if (Vector3D.DistanceSquared(sightings[i].Position, pos) < mergeRadius * mergeRadius)
                {
                    sightings[i].Tick = tick;      // refresh, do not duplicate
                    return;
                }
            }

            OreSighting s = new OreSighting();
            s.OreType = oreType;
            s.Position = pos;
            s.Tick = tick;
            sightings.Add(s);

            // Oldest out first when full.
            while (sightings.Count > SIGHTINGS_MAX) sightings.RemoveAt(0);

            Log("Ore: " + oreType + " at " + Fmt(Vector3D.Distance(pos, shipPos), 0) + "m");

            if (HasDispatcher) SendOreSighting(s);
        }

        /// <summary>
        /// Drop sightings for ground we have since mined out, so the score map does not
        /// keep steering the ship back to a hole it already emptied.
        /// </summary>
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

        /// <summary>Which job cell a world position sits over, or -1 if outside the grid.</summary>
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

        // ---------------------------------------------------------------------------
        //  VANILLA CAMERA SENSING
        //
        //  Cameras cannot see ore, but they can see rock, which is worth plenty:
        //  it tells us where the surface actually is rather than where the job frame
        //  says it should be.
        // ---------------------------------------------------------------------------

        /// <summary>
        /// Raycast straight ahead for solid material.
        ///
        /// The tri-state matters. "No camera had enough charge" and "the camera looked
        /// and there is nothing there" are completely different answers, and collapsing
        /// them into a single -1 makes the caller unable to act on either. Callers must
        /// distinguish a scan that did not happen from a scan that found empty space.
        /// </summary>
        /// <param name="distance">Metres to the surface, or -1 if the scan found nothing.</param>
        /// <returns>True if a scan actually took place.</returns>
        bool TryScanAhead(double maxRange, out double distance)
        {
            distance = -1;

            for (int i = 0; i < cameras.Count; i++)
            {
                IMyCameraBlock cam = cameras[i];
                if (!cam.IsFunctional) continue;

                // A camera only accumulates range once raycasting is switched on, so an
                // unarmed camera is armed here and will be usable a few seconds later.
                if (!cam.EnableRaycast) { cam.EnableRaycast = true; continue; }
                if (cam.AvailableScanRange < maxRange) continue;

                MyDetectedEntityInfo hit = cam.Raycast(maxRange);

                if (hit.IsEmpty() || !hit.HitPosition.HasValue) return true;    // looked, saw nothing

                // Only voxels count. A passing ship is not the surface.
                if (hit.Type != MyDetectedEntityType.Asteroid && hit.Type != MyDetectedEntityType.Planet)
                    return true;

                distance = Vector3D.Distance(cam.GetPosition(), hit.HitPosition.Value);
                return true;
            }

            return false;   // nothing was in a position to look
        }

        /// <summary>Keep cameras charged so a raycast is available when we want one.</summary>
        void ArmCameras()
        {
            for (int i = 0; i < cameras.Count; i++)
                if (cameras[i].IsFunctional && !cameras[i].EnableRaycast)
                    cameras[i].EnableRaycast = true;
        }

        /// <summary>Summary of scouting capability for the display.</summary>
        string ScoutStatus()
        {
            if (oreModAvailable) return "ore-raycast (" + sightings.Count + " leads)";
            if (oreDetectors.Count > 0) return "probe map (no ore mod)";
            return "probe map";
        }
        #endregion

        #region 13_Igc.cs
        // ============================================================================
        //  INTER-GRID COMMUNICATION
        //
        //  Wire format is plain text, pipe delimited: TYPE|field|field|...
        //
        //  Numbers travel as integers scaled by 1000, never as decimal strings. A
        //  script that writes "12.5" and is read by a client whose locale uses a comma
        //  decimal separator silently parses it as 125 — and a drone that thinks the
        //  shaft is ten times deeper than it is will drill until something breaks.
        //  Integers have no separator and cannot go wrong.
        // ============================================================================

        const int IGC_MAX_PER_TICK = 12;

        void SetupIgc()
        {
            listener = IGC.RegisterBroadcastListener(igcChannel);
            listener.SetMessageCallback(igcChannel);
            unicast = IGC.UnicastListener;
            unicast.SetMessageCallback(igcChannel);
        }

        /// <summary>Drain both inboxes. Bounded per tick so a message storm cannot
        /// starve the control loop.</summary>
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
            if (src == IGC.Me) return;                 // our own broadcast coming back

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

        bool HasDispatcher { get { return role == Role.Miner && dispatcherAddr != 0; } }

        // ---------------------------------------------------------------------------
        //  OUTBOUND — miner side
        // ---------------------------------------------------------------------------

        void SendHeartbeat()
        {
            if (role != Role.Miner || dispatcherAddr == 0) return;
            if (tick % 12 != 0) return;                // ~2 Hz at Update10

            string body = "H|" + ShipLabel()
                + "|" + (int)state
                + "|" + EncD(cargoFill)
                + "|" + EncD(batteryFill)
                + "|" + EncV(shipPos)
                + "|" + activeCell;

            IGC.SendUnicastMessage(dispatcherAddr, igcChannel, body);
        }

        void RequestLease()
        {
            if (dispatcherAddr == 0) return;
            IGC.SendUnicastMessage(dispatcherAddr, igcChannel, "LR|" + ShipLabel());
        }

        void SendShaftReport(int cellIdx, ShaftResult result, double oreKg, double metres,
                             double depthReached, bool wasProbe)
        {
            if (dispatcherAddr == 0) return;
            string body = "SR|" + cellIdx + "|" + (int)result
                        + "|" + EncD(oreKg) + "|" + EncD(metres)
                        + "|" + EncD(depthReached) + "|" + (wasProbe ? 1 : 0);
            IGC.SendUnicastMessage(dispatcherAddr, igcChannel, body);
        }

        void RequestDock()
        {
            if (dispatcherAddr == 0) return;
            IGC.SendUnicastMessage(dispatcherAddr, igcChannel, "DR|" + ShipLabel());
        }

        void ReleaseDock()
        {
            if (dispatcherAddr == 0 || myDockSlot < 0) return;
            IGC.SendUnicastMessage(dispatcherAddr, igcChannel, "DX|" + myDockSlot);
            myDockSlot = -1;
        }

        void SendOreSighting(OreSighting s)
        {
            if (dispatcherAddr == 0) return;
            IGC.SendUnicastMessage(dispatcherAddr, igcChannel, "OS|" + s.OreType + "|" + EncV(s.Position));
        }

        /// <summary>Release whatever we hold, locally and at the dispatcher.</summary>
        void ReleaseLease(ShaftResult why)
        {
            if (activeCell < 0) return;
            if (dispatcherAddr != 0)
                SendShaftReport(activeCell, why, ShaftOreSoFar(), shaftMaxDepth, shaftMaxDepth, shaftIsProbe);
            ReleaseLeaseLocal();
            activeCell = -1;
        }

        /// <summary>Clear the local lease flag without telling anyone.</summary>
        void ReleaseLeaseLocal()
        {
            if (activeCell < 0 || activeCell >= cells.Length) return;
            if (cells[activeCell].State == CellState.Leased) cells[activeCell].State = CellState.Unknown;
            cells[activeCell].LeasedBy = 0;
        }

        // ---------------------------------------------------------------------------
        //  OUTBOUND — dispatcher side
        // ---------------------------------------------------------------------------

        /// <summary>
        /// Announce the job. Broadcast rather than unicast so a freshly built drone
        /// joins the fleet the moment it powers up, with no pairing step.
        /// </summary>
        void SendBeacon()
        {
            if (!job.IsSet) { IGC.SendBroadcastMessage(igcChannel, "B|0"); return; }

            string body = "B|1"
                + "|" + job.Width + "|" + job.Height + "|" + job.Depth
                + "|" + EncD(job.Spacing)
                + "|" + EncV(job.Origin)
                + "|" + EncV(job.Right)
                + "|" + EncV(job.Forward)
                + "|" + EncV(job.Down);

            IGC.SendBroadcastMessage(igcChannel, body);
        }

        void GrantLease(long to, int cellIdx, double depthLimit, bool isProbe, double lane)
        {
            string body = "LG|" + cellIdx + "|" + EncD(depthLimit)
                        + "|" + (isProbe ? 1 : 0) + "|" + EncD(lane);
            IGC.SendUnicastMessage(to, igcChannel, body);
        }

        void DenyLease(long to, string reason)
        {
            IGC.SendUnicastMessage(to, igcChannel, "LD|" + reason);
        }

        void GrantDock(long to, int slot)
        {
            IGC.SendUnicastMessage(to, igcChannel, "DG|" + slot);
        }

        // ---------------------------------------------------------------------------
        //  INBOUND
        // ---------------------------------------------------------------------------

        void OnBeacon(long src, string[] f)
        {
            if (role != Role.Miner) return;

            // A second dispatcher on the same channel would otherwise steal the miner
            // on every beacon, so leases come from one and reports go to whichever
            // spoke last. Stay with the first one heard and say so.
            if (dispatcherAddr != 0 && dispatcherAddr != src)
            {
                if (tick - lastDispatcherSeenTick < 600)
                {
                    Log("Second dispatcher on channel '" + igcChannel + "' — ignoring it");
                    return;
                }
                Log("Switching dispatcher — previous one went quiet");
            }

            dispatcherAddr = src;
            lastDispatcherSeenTick = tick;

            if (f.Length < 10 || f[1] != "1") return;

            // Adopt the dispatcher's job frame verbatim. Every drone working from the
            // same origin and axes is what makes cell indices mean the same thing to
            // everyone — without it, drone 2's cell [3,4] is somewhere else entirely.
            int w = ParseInt(f[2], job.Width);
            int h = ParseInt(f[3], job.Height);
            int d = ParseInt(f[4], job.Depth);

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
                // Stack new arrivals into their own altitude band.
                r.Lane = fleet.Count * laneSpacing;
                fleet[src] = r;
                Log("Drone joined: " + f[1]);
            }

            r.Name = f[1];
            r.State = (MinerState)ParseInt(f[2], 0);
            r.CargoFill = (float)DecD(f[3]);
            r.Battery = (float)DecD(f[4]);
            r.Position = DecV(f[5]);
            r.LeasedCell = ParseInt(f[6], -1);
            r.LastSeenTick = tick;
        }

        void OnLeaseRequest(long src, string[] f)
        {
            if (role != Role.Dispatcher) return;
            if (!job.IsSet) { DenyLease(src, "nojob"); return; }
            // 'stop' at the dispatcher must actually stop the fleet. Drones already in
            // the air finish what they hold and then find no more work waiting.
            if (!jobRunning || jobComplete) { DenyLease(src, "paused"); return; }

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

            // Score the site from where this drone actually is, so the nearest free
            // shaft goes to the nearest drone instead of to whoever spoke first.
            int cell = SelectNextCell(r.Position.LengthSquared() > 1 ? r.Position : shipPos);
            if (cell < 0) { DenyLease(src, "done"); return; }

            cells[cell].State = CellState.Leased;
            cells[cell].LeasedBy = src;
            cells[cell].LeaseExpiresTick = tick + (long)(droneTimeout * 2 / Math.Max(dt, 0.01));
            r.LeasedCell = cell;

            double limit = shaftIsProbe ? Math.Min(probeDepth, job.Depth) : job.Depth;
            GrantLease(src, cell, limit, shaftIsProbe, r.Lane);
        }

        void OnLeaseGrant(long src, string[] f)
        {
            if (role != Role.Miner || f.Length < 5) return;

            activeCell = ParseInt(f[1], -1);
            shaftDepthLimit = DecD(f[2]);
            shaftIsProbe = f[3] == "1";
            myLane = DecD(f[4]);

            if (activeCell >= 0 && activeCell < cells.Length)
                cells[activeCell].State = CellState.Leased;
        }

        void OnLeaseDenied(long src, string[] f)
        {
            if (role != Role.Miner) return;
            awaitingLease = false;

            if (f.Length < 2) return;

            if (f[1] == "done")
            {
                jobComplete = true;
                Log("Dispatcher reports job complete");
                SetState(MinerState.Inbound);
            }
            else if (f[1] == "paused")
            {
                // Not finished, just no work right now. Go home and wait rather than
                // loiter over the site burning hydrogen.
                Log("Dispatcher paused — returning");
                SetState(MinerState.Inbound);
            }
            // "nojob": leave awaitingLease clear and retry on the next pass.
        }

        void OnShaftReport(long src, string[] f)
        {
            if (role != Role.Dispatcher || f.Length < 7) return;

            int idx = ParseInt(f[1], -1);
            if (idx < 0 || idx >= cells.Length) return;

            // Only the drone that holds the lease may report on it. Without this a
            // stale message from a drone whose lease already expired would overwrite
            // the result of whoever is digging that cell now.
            if (cells[idx].LeasedBy != 0 && cells[idx].LeasedBy != src) return;

            ShaftResult result = (ShaftResult)ParseInt(f[2], 0);
            RecordShaftResult(idx, result, DecD(f[3]), DecD(f[4]), DecD(f[5]), f[6] == "1");

            DroneRecord r;
            if (fleet.TryGetValue(src, out r)) r.LeasedCell = -1;
        }

        void OnDockRequest(long src, string[] f)
        {
            if (role != Role.Dispatcher) return;

            // Already holding one? Re-grant it rather than allocate a second.
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

            GrantDock(src, -1);       // all full, hold off
        }

        void OnDockGrant(long src, string[] f)
        {
            if (role != Role.Miner || f.Length < 2) return;
            myDockSlot = ParseInt(f[1], -1);
        }

        void OnDockRelease(long src, string[] f)
        {
            if (role != Role.Dispatcher || f.Length < 2) return;
            int slot = ParseInt(f[1], -1);

            long owner;
            if (dockSlotOwner.TryGetValue(slot, out owner) && owner == src)
                dockSlotOwner.Remove(slot);

            DroneRecord r;
            if (fleet.TryGetValue(src, out r)) r.DockSlot = -1;
        }

        void OnOreSighting(long src, string[] f)
        {
            if (f.Length < 3) return;
            // Dispatcher aggregates every drone's findings into one shared map, so a
            // lead found by one scout steers the whole fleet.
            AddSighting(f[1], DecV(f[2]));
        }

        string ShipLabel()
        {
            if (shipName.Length > 0) return Sanitize(shipName);
            return Sanitize(Me.CubeGrid.CustomName);
        }

        /// <summary>Strip delimiters so a grid called "Miner|1" cannot corrupt the wire format.</summary>
        static string Sanitize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "?";
            return s.Replace('|', '/').Replace(',', ' ');
        }
        #endregion

        #region 14_Dispatcher.cs
        // ============================================================================
        //  DISPATCHER
        //
        //  A stationary brain at the base. It owns the job and the yield map; drones own
        //  nothing but their current shaft. That split is what makes the fleet robust:
        //  a drone can explode mid-shaft and the only thing lost is one lease, which
        //  expires on its own and gets handed to somebody else.
        //
        //  It is entirely optional. A single miner with no dispatcher on the channel
        //  runs the identical job logic locally.
        // ============================================================================

        void TickDispatcher()
        {
            stateTicks++;
            statusLine = "Dispatching";

            if (recording) RecordTick();
            UpdateOreScan();

            // Announce ourselves steadily. New drones need to hear this to join, and
            // existing ones use it as the liveness signal.
            if (tick % 30 == 0) SendBeacon();

            ExpireLeases();
            ExpireDrones();
        }

        /// <summary>
        /// Reclaim shafts whose holder has gone quiet.
        ///
        /// This is the failure mode that kills naive swarm scripts: a drone dies holding
        /// a lease, nobody ever digs that cell, and the job never completes. A lease is
        /// a timed loan, not a permanent grant.
        /// </summary>
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

        /// <summary>Forget drones we have not heard from, and free what they held.</summary>
        void ExpireDrones()
        {
            if (fleet.Count == 0) return;

            long limit = (long)(droneTimeout / Math.Max(dt, 0.01));

            // Collect first, mutate after — removing from a dictionary mid-enumeration
            // throws, and it will throw on the exact night you are not watching.
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

            // Lanes are handed out by join order, so a departure leaves a gap. Repack so
            // three drones always use lanes 0/1/2 rather than 0/3/7.
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

        // ---------------------------------------------------------------------------
        //  FLEET STATISTICS
        // ---------------------------------------------------------------------------

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
        #endregion

        #region 15_Util.cs
        // ============================================================================
        //  UTILITIES
        //
        //  Number formatting and parsing are done by hand throughout. It looks like
        //  reinventing the wheel; it is not. Space Engineers runs on the client's own
        //  locale, so ToString("F1") produces "12,5" for a large part of the player base
        //  and every round trip through a string silently corrupts. Integers and manual
        //  assembly have no such failure mode.
        // ============================================================================

        static double Clamp(double v, double lo, double hi)
        {
            if (double.IsNaN(v)) return lo;
            return v < lo ? lo : (v > hi ? hi : v);
        }

        static double ToDegrees(double radians)
        {
            return radians * 180.0 / Math.PI;
        }

        // ---------------------------------------------------------------------------
        //  HUMAN-READABLE FORMATTING
        // ---------------------------------------------------------------------------

        /// <summary>Fixed-point format with a guaranteed '.' separator in every locale.</summary>
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

        /// <summary>Compact mass: kg below a tonne, tonnes above.</summary>
        static string FmtMass(double kg)
        {
            if (kg < 1000) return Fmt(kg, 0) + "kg";
            return Fmt(kg / 1000.0, 1) + "t";
        }

        /// <summary>A text progress bar, e.g. [####------] for 0.4.</summary>
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

        // ---------------------------------------------------------------------------
        //  WIRE ENCODING
        //
        //  Everything on the wire is an integer. Doubles are scaled by 1000, giving
        //  millimetre precision on positions, which is far finer than anything we
        //  navigate to.
        // ---------------------------------------------------------------------------

        const double WIRE_SCALE = 1000.0;

        static string EncD(double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) return "0";
            double scaled = v * WIRE_SCALE;
            // Saturate rather than overflow into a wrapped negative.
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

        static int ParseInt(string s, int fallback)
        {
            int v;
            return int.TryParse(s, out v) ? v : fallback;
        }

        static double ParseDouble(string s, double fallback)
        {
            // Operator input, so it may genuinely contain a decimal point. Parse the
            // two halves as integers and reassemble, which works whatever the locale
            // thinks a separator is.
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
        #endregion

        #region 16_Display.cs
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
        #endregion

        #region 17_Commands.cs
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
                    if (state != MinerState.Fault && state != MinerState.Idle
                        && state != MinerState.Unloading && state != MinerState.Servicing)
                        SetState(MinerState.Inbound);
                    break;

                case "halt":
                    // Immediate, in place. For when something is going wrong right now.
                    jobRunning = false;
                    SafeStop();
                    SetState(MinerState.Idle);
                    Log("Halted in place");
                    break;

                case "home":
                    jobRunning = false;
                    SetState(MinerState.Inbound);
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
                Log("job set <w> <h> <depth> | job depth <m> | job size <w> <h> | job here");
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

                default:
                    Log("job set | here | size | depth");
                    break;
            }
        }
        #endregion

        #region 18_Persist.cs
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

            if (saved != MinerState.Idle && saved != MinerState.Fault)
                Log("Resumed from " + saved + " — idling, run 'start' to continue");
        }

        void LoadJob(string[] f)
        {
            job.IsSet = true;
            job.Width = Math.Max(1, ParseInt(f[1], 5));
            job.Height = Math.Max(1, ParseInt(f[2], 5));
            job.Depth = Math.Max(1, ParseInt(f[3], 40));
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
        #endregion

        #region 19_Sprites.cs
        // ============================================================================
        //  SPRITE DASHBOARD
        //
        //  LCDs tagged with lcdTag get this instead of monospace text. The centrepiece
        //  is the yield heatmap — the survey rendered as colour, which is the whole
        //  point of building a survey in the first place. A table of numbers tells you
        //  the script is running; a heatmap tells you the ore is all in the north-east
        //  corner and your job rectangle is in the wrong place.
        //
        //  Everything is drawn from SurfaceSize, so it lays out correctly on a square
        //  LCD, a wide one, or a cockpit screen without any configuration.
        // ============================================================================

        // Palette. Deliberately muted except for the data itself, so the eye lands on
        // the map rather than the chrome.
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
                catch { /* a panel destroyed mid-draw must not take the script down */ }
            }
        }

        void DrawDashboard(IMyTextSurface s)
        {
            Vector2 size = s.SurfaceSize;
            // The drawable texture is usually larger than the visible surface; the
            // visible area sits centred inside it. Skip this and everything is drawn
            // offset toward the top-left corner.
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

                // Wide panels put the map beside the readouts; tall or square ones
                // stack them, so a cockpit screen stays legible.
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

        // ---------------------------------------------------------------------------
        //  HEADER
        // ---------------------------------------------------------------------------

        void DrawHeader(MySpriteDrawFrame frame, Vector2 pos, Vector2 size)
        {
            Fill(frame, pos, size, C_PANEL);

            bool fault = state == MinerState.Fault;
            Color accent = fault ? C_BAD : C_ACCENT;

            // Accent stripe down the left edge — the fastest "is it healthy" signal on
            // the whole screen, readable from across the room.
            Fill(frame, pos, new Vector2(size.Y * 0.10f, size.Y), accent);

            float fs = size.Y * 0.030f;
            Text(frame, "VEIN", pos + new Vector2(size.Y * 0.28f, size.Y * 0.10f), fs * 1.15f, accent);

            string sub = role == Role.Dispatcher
                ? fleet.Count + " drone" + (fleet.Count == 1 ? "" : "s")
                : (HasDispatcher ? "fleet" : "solo");
            Text(frame, sub, pos + new Vector2(size.Y * 0.28f, size.Y * 0.55f), fs * 0.62f, C_DIM);

            string headline = fault ? faultReason : statusLine;
            if (headline.Length > 34) headline = headline.Substring(0, 33) + "…";
            Text(frame, headline, pos + new Vector2(size.X * 0.30f, size.Y * 0.10f), fs * 0.80f,
                 fault ? C_BAD : C_INK);

            if (job.IsSet)
            {
                Text(frame, Fmt(JobProgress() * 100, 0) + "%",
                     pos + new Vector2(size.X - size.Y * 0.20f, size.Y * 0.10f), fs * 1.05f, C_INK,
                     TextAlignment.RIGHT);

                // Thin progress rule along the bottom of the header.
                float w = size.X - size.Y * 0.40f;
                Vector2 barPos = pos + new Vector2(size.X * 0.30f, size.Y * 0.72f);
                Fill(frame, barPos, new Vector2(w * 0.62f, size.Y * 0.06f), C_DIM);
                Fill(frame, barPos, new Vector2(w * 0.62f * (float)JobProgress(), size.Y * 0.06f), accent);
            }
        }

        // ---------------------------------------------------------------------------
        //  YIELD HEATMAP
        // ---------------------------------------------------------------------------

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

            // Square cells, centred, sized to whichever axis runs out first.
            float cell = Math.Min(area.X / job.Width, area.Y / job.Height);
            float gap = cell > 8f ? cell * 0.08f : 0f;
            Vector2 gridSize = new Vector2(cell * job.Width, cell * job.Height);
            Vector2 gridPos = pos + new Vector2(pad, pad) + (area - gridSize) * 0.5f;

            // Normalise colour against the best cell found so far, so the map stays
            // readable whether you are mining iron or platinum.
            float peak = 0.01f;
            for (int i = 0; i < cells.Length; i++)
                if (cells[i].Yield > peak) peak = cells[i].Yield;

            // One sprite per cell is the dominant cost of the whole dashboard. Past a
            // few hundred it is a meaningful share of the tick budget for a picture no
            // one can read cell-by-cell anyway, so large jobs draw a coarser summary.
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

            // Where the drones actually are, drawn over the map.
            if (role == Role.Dispatcher)
            {
                foreach (var kv in fleet)
                    DrawDroneMarker(frame, gridPos, cell, kv.Value.Position, C_ACCENT);
            }
            else if (activeCell >= 0)
            {
                int col = CellCol(activeCell), row = CellRow(activeCell);
                Vector2 p = gridPos + new Vector2((col + 0.5f) * cell, (row + 0.5f) * cell);
                Sprite(frame, "CircleHollow", p, new Vector2(cell * 1.9f, cell * 1.9f), Color.White);
            }

            Text(frame, "yield map  ·  peak " + Fmt(peak, 1) + " kg/m",
                 pos + new Vector2(size.X * 0.5f, size.Y - labelH), size.Y * 0.055f, C_DIM,
                 TextAlignment.CENTER);
        }

        /// <summary>Colour for a cell: state first, then yield as a heat ramp.</summary>
        static Color CellColor(YieldCell c, float peak)
        {
            switch (c.State)
            {
                case CellState.Unknown: return new Color(24, 34, 44);
                case CellState.Leased:  return new Color(70, 130, 200);
                case CellState.Blocked: return new Color(120, 40, 40);
                case CellState.Barren:  return new Color(40, 36, 32);
            }

            // Rich or Exhausted — colour by how good it actually was.
            float t = peak > 0 ? Clamped(c.Yield / peak) : 0f;

            // Dark red → orange → yellow → green. Ramps through hue rather than
            // brightness so it stays distinguishable on a dim LCD.
            Color hot = t < 0.5f
                ? Lerp(new Color(90, 30, 25), new Color(220, 150, 40), t * 2f)
                : Lerp(new Color(220, 150, 40), new Color(110, 240, 130), (t - 0.5f) * 2f);

            // Worked-out cells are dimmed rather than recoloured, so you can still see
            // where the good ground was after it has been mined.
            if (c.State == CellState.Exhausted) return Dim(hot, 0.45f);
            return hot;
        }

        void DrawDroneMarker(MySpriteDrawFrame frame, Vector2 gridPos, float cell, Vector3D world, Color col)
        {
            if (job.Spacing <= 0) return;

            Vector3D d = world - job.Origin;
            double x = Vector3D.Dot(d, job.Right) / job.Spacing + (job.Width - 1) * 0.5;
            double y = Vector3D.Dot(d, job.Forward) / job.Spacing + (job.Height - 1) * 0.5;

            // Off the edge of the site — in transit, probably. Not worth drawing.
            if (x < -1 || x > job.Width || y < -1 || y > job.Height) return;

            Vector2 p = gridPos + new Vector2((float)(x + 0.5) * cell, (float)(y + 0.5) * cell);
            Sprite(frame, "Circle", p, new Vector2(cell * 0.7f, cell * 0.7f), col);
        }

        // ---------------------------------------------------------------------------
        //  SIDE PANELS
        // ---------------------------------------------------------------------------

        void DrawShipPanel(MySpriteDrawFrame frame, Vector2 pos, Vector2 size)
        {
            Fill(frame, pos, size, C_PANEL);

            float pad = size.Y * 0.06f;
            float fs = size.Y * 0.055f;
            float y = pos.Y + pad;
            float rowH = size.Y * 0.145f;

            Gauge(frame, new Vector2(pos.X + pad, y), size.X - pad * 2, rowH, "CARGO",
                  cargoFill, FmtMass(oreAboard) + " ore", GaugeColor(cargoFill, true));
            y += rowH;

            Gauge(frame, new Vector2(pos.X + pad, y), size.X - pad * 2, rowH, "POWER",
                  batteryFill, Fmt(batteryFill * 100, 0) + "%", GaugeColor(batteryFill, false));
            y += rowH;

            if (hydrogenTanks.Count > 0)
            {
                Gauge(frame, new Vector2(pos.X + pad, y), size.X - pad * 2, rowH, "H2",
                      hydrogenFill, Fmt(hydrogenFill * 100, 0) + "%", GaugeColor(hydrogenFill, false));
                y += rowH;
            }

            y += pad * 0.5f;

            if (activeCell >= 0)
            {
                string what = (shaftIsProbe ? "probe " : "shaft ") + CellLabel(activeCell);
                Text(frame, what, new Vector2(pos.X + pad, y), fs, C_DIM);
                Text(frame, Fmt(Math.Max(0, shaftDepth), 1) + " m",
                     new Vector2(pos.X + size.X - pad, y), fs, C_INK, TextAlignment.RIGHT);
                y += rowH * 0.75f;
            }

            if (maxFlyableMass > 0)
            {
                bool over = shipMass >= maxFlyableMass;
                Text(frame, "lift", new Vector2(pos.X + pad, y), fs, C_DIM);
                Text(frame, FmtMass(shipMass) + " / " + FmtMass(maxFlyableMass),
                     new Vector2(pos.X + size.X - pad, y), fs, over ? C_BAD : C_INK, TextAlignment.RIGHT);
                y += rowH * 0.75f;
            }

            // Measured fuel reserve. Shown rather than hidden, so you can see what the
            // ship has learned about its own thirst and judge whether to trust it.
            if (hydroCalibrated)
            {
                double need = FuelToGetHome();
                bool tight = hydrogenFill < need + 0.15;
                Text(frame, "return needs", new Vector2(pos.X + pad, y), fs, C_DIM);
                Text(frame, Fmt(need * 100, 0) + "% H2",
                     new Vector2(pos.X + size.X - pad, y), fs, tight ? C_WARN : C_INK,
                     TextAlignment.RIGHT);
                y += rowH * 0.75f;
            }

            Text(frame, "scout", new Vector2(pos.X + pad, y), fs, C_DIM);
            Text(frame, oreModAvailable ? "ore raycast" : "probe map",
                 new Vector2(pos.X + size.X - pad, y), fs,
                 oreModAvailable ? C_GOOD : C_INK, TextAlignment.RIGHT);
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
                if (y + rowH > pos.Y + size.Y) break;      // ran out of panel

                Color dot = r.State == MinerState.Fault ? C_BAD
                          : r.State == MinerState.Idle ? C_DIM : C_GOOD;
                Sprite(frame, "Circle", new Vector2(pos.X + pad + fs * 0.4f, y + rowH * 0.35f),
                       new Vector2(fs * 0.55f, fs * 0.55f), dot);

                string name = r.Name.Length > 12 ? r.Name.Substring(0, 12) : r.Name;
                Text(frame, name, new Vector2(pos.X + pad + fs * 1.1f, y), fs, C_INK);
                Text(frame, r.State.ToString(), new Vector2(pos.X + size.X - pad, y), fs * 0.8f,
                     C_DIM, TextAlignment.RIGHT);

                // Two hairline bars: cargo above, battery below.
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

        /// <summary>Green when healthy, amber then red as it approaches trouble.
        /// <paramref name="fullIsBad"/> flips the sense for things like cargo.</summary>
        static Color GaugeColor(double v, bool fullIsBad)
        {
            double t = fullIsBad ? 1.0 - v : v;
            if (t > 0.5) return C_GOOD;
            if (t > 0.2) return C_WARN;
            return C_BAD;
        }

        // ---------------------------------------------------------------------------
        //  PRIMITIVES
        // ---------------------------------------------------------------------------

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
        #endregion

    }
}
