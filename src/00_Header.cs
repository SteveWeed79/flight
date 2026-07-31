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

const string VEIN_VERSION = "1.1.0";
const string STORAGE_REV  = "4";   // bump on ANY change to a persisted enum or field order
