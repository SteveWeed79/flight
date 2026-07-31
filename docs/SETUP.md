# Setup

## Requirements

Programmable Blocks are **PC and dedicated server only**. Consoles cannot run
scripts. Scripts must also be enabled in the world settings — "Enable in-game
scripts" in Advanced.

### On the mining ship

| Block | Required | Notes |
|---|---|---|
| Programmable Block | yes | Where the script goes |
| Remote Control | yes | Preferred over a cockpit — it has a dependable forward axis with nobody aboard |
| Gyroscope | yes | More is better on a heavy ship |
| Thrusters | yes | Needs some in all six directions |
| Drills | yes | Must point along the ship's **forward** axis |
| Connector | yes | For docking and unloading |
| Cargo container | yes | — |
| Battery | recommended | Without one, no power management |
| Hydrogen tank | if H2 powered | Ignored entirely if absent |
| Ejector / connector in Throw Out mode | recommended | Roughly triples time on site |
| Camera facing forward | recommended | Lets it skip cells over open space |
| Ore Detector | optional | Only useful with the Ore Detector Raycast mod |

**Drill orientation is the one thing people get wrong.** The script drills along
the ship's forward axis. If your drills point "down" relative to a seated pilot,
the Remote Control must be mounted so that its forward is that same direction.

### At the base

Just a connector the ship can dock to, with cargo behind it. Add a second
Programmable Block only if you want a fleet.

---

## First run

### 1. Install

Paste `dist/VEIN.min.cs` into the Programmable Block and hit **Check Code**, then
**Remember & Exit**.

The block's Custom Data fills itself in with a fully commented config. You do not
need to touch it yet.

### 2. Record the route

Dock the ship at your base connector, then run the argument:

```
record start
```

Now **fly the ship manually** from the dock to the patch you want mined. Fly the
route you would want a loaded ship to take home — around the ridge, not over it.
Points are recorded automatically every few metres.

Arrive above the site, and point the nose the way you want to drill. Straight
down for a planet surface; into the rock face for an asteroid. Then:

```
record stop
```

The log will report how many waypoints it captured and the lift limit it measured
along the route.

### 3. Define the site

Still hovering where you want to mine, with the nose pointed the drilling
direction:

```
job set 5 5 40
```

That is a 5×5 grid of shafts, 40 m deep, centred on the ship's current position.
Shaft spacing is derived from your actual drill head, so you do not specify it.

### 4. Go

```
start
```

The ship undocks, flies your route, surveys the site with shallow probes, mines
where the survey found ore, comes home when full, unloads, recharges, and goes
back out. It stops when the site is worked out.

---

## Reading the screen

Name any LCD with `[VEIN]` in it and the script will use it. The Programmable
Block's own screen is used automatically.

```
VEIN 1.0.0  Miner  [solo]
----------------------------------------
Descending — Drilling [3,2]
Cargo  [########----] 67%  4.2t ore
Power  [##########--] 84%
H2     [#######-----] 61%
Shaft  [3,2]  18.4/40m
Job    5x5 @40m  32% done, 17 left
Lift   118.4t of 142.0t
Scout  probe map (no ore mod)
Nav    2.1m  1.2m/s  err 1deg
Load   23% of tick budget

..#..
.##-.
.#@-.
..-..
.....
. new  # ore  - dry  X done  ! stuck  o busy
```

The map is the useful part. If the `#` marks cluster at one edge, your rectangle
is in the wrong place — fly there and run `job here`.

---

## Adding a fleet

Optional. A single miner works fine forever.

1. Put a Programmable Block at your base with the same script.
2. In its Custom Data set `role = Dispatcher`, and `dockSlots` to the number of
   connectors miners can unload at.
3. Run `reload`, then define the job **from the dispatcher** — either fly a ship
   there and use `job set`, or set the job on a miner and copy the values.
4. Run `start` on the dispatcher.

Every miner on the same `channel` joins automatically. No pairing step. Each gets
its own altitude lane over the site and its own shaft assignments.

Miners still need their own recorded route to their own dock. The dispatcher
coordinates *what* to dig, not *how to get there*.

To stop the whole operation, run `stop` on the dispatcher — drones finish what
they hold and come home. `fleet stop` relays the command to the drones directly.

---

## Common problems

**"Not ready: ..."** — the readiness check is telling you exactly what is missing.
The usual answers are no job set (`job set 5 5 40`) or no route recorded
(`record start` / `record stop`).

**"Too heavy for the route"** — the ship cannot lift its own current mass over the
worst point of the recorded route. Unload it, or lower `liftSafetyFactor`, or
record a route that does not climb as steeply.

**It jams in shafts.** Lower `drillSpeed`. Above about 2 m/s the drills stop
keeping up with the hull and you wedge. On a light ship, 0.8 is not unreasonable.

**It digs dry holes.** Almost always `probeDepth` not reaching the ore layer. See
[SCOUTING.md](SCOUTING.md).

**It says the job is complete but the site looks untouched.** It surveyed and
concluded there was nothing worth digging. Check `probeDepth`, then
`barrenThreshold`. `reset` clears the survey and keeps the route.

**It will not dock.** The recorded dock position comes from waypoint zero, so
`record start` must be run **while actually docked**. Re-record if in doubt.

**"Script too complex."** Turn off `verboseEcho`, use a smaller job grid, or fewer
LCDs. Watch the Load line — sustained above 80% is the warning sign.

**A fault it will not clear.** `clear` resets a fault, but only fix the cause
first — the fault text says what it was. `halt` stops everything in place if you
need the ship to stop *now*.
