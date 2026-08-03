# VEIN — Vectored Extraction & Intelligent Navigation

An automated mining script for Space Engineers, for the Programmable Block.

It records a route, surveys a site, digs where the ore actually is, comes home
when it is full, and does not strand itself in gravity. One miner works alone; a
dispatcher and any number of drones work together. Same script, both roles.

Built by studying the two scripts that defined this genre — **[PAM] Path Auto
Miner** by Keks and **[SCAM] Simple Concurrent Adaptive Min3r** by cheerkin —
keeping what worked and fixing what did not. See [docs/DESIGN.md](docs/DESIGN.md)
for the full comparison.

---

## Quick start

1. Paste `dist/VEIN.min.cs` into a Programmable Block on your mining ship.
2. The ship needs a **Remote Control**, **gyro**, **thrusters**, **drills**, a
   **connector**, and **cargo**. Everything else is optional.
3. Dock at your base connector and run the argument `record start`.
4. Fly manually to the patch you want mined, nose pointed the way you want to
   drill. Run `record stop`.
5. Run `job set 5 5 40` — a 5×5 grid of shafts, 40 m deep.
6. Run `start`.

It will undock, fly your route, survey the site, mine it, come back, unload,
recharge, and go again until the site is worked out.

Full walkthrough: [docs/SETUP.md](docs/SETUP.md).

---

## What it does

**Flies your route, not a straight line.** You fly dock-to-site once and the ship
repeats it forever. No pathfinder knows the gap between those two ridges is the
safe way home; you do.

**Refuses loads it cannot lift.** While recording, it measures actual thrust
against gravity at every point on the route. The worst point sets a mass ceiling,
and the ship leaves with a part load rather than filling up and stranding itself
at 200 m.

**Surveys before it commits.** Cheap shallow probes on a coarse lattice build a
map of ore-per-metre, then the expensive deep shafts go where the map says. On a
scattered deposit that is the difference between 100 holes and 30.

**Knows when to stop digging.** The drills are the ore sensor. When a shaft stops
producing, there is nothing below worth the fuel.

**Follows the ore off the edge of the job.** If the survey says a boundary of
your grid is still producing when the work runs out, the boundary was a guess and
the deposit carries on. It extends the grid that way and keeps going, so the
shape of the site ends up matching the shape of the ore rather than the rectangle
you typed.

**Measures itself instead of trusting a number somebody typed.** Cutting speed
and braking margin are both properties of *your* hull on *your* server, so the
ship works them out by watching what happens: back off when the drills stall,
creep up when they are cutting cleanly, and tighten the braking profile if
approaches keep spending more authority than they should. Both figures are on the
screen and both can be pinned in config if you would rather they did not move.

**Scales to a fleet without a rewrite.** Put a second Programmable Block at your
base, set `role = Dispatcher`, and every miner on the channel joins
automatically. Shaft assignments are timed leases, so a drone that explodes
mid-shaft costs you one lease, not the job. The site is divided into stripes and
only one drone at a time manoeuvres over each, granted by the dispatcher with a
queue behind it — and unlike the script that idea comes from, the grant expires,
so a drone that dies holding one does not deadlock the ground under it.

**Does not hang.** Every state has a watchdog. Stuck in a shaft, hung on a dock
approach, waiting on a dispatcher that stopped answering — all of them recover on
their own or stop safely and say why.

---

## Ore detection, honestly

**Vanilla Space Engineers gives scripts no access to the Ore Detector.** Keen have
declined that API more than once. Cameras raycast voxels but report `Asteroid`,
never `Cobalt`. Any vanilla script that claims to fly straight to ore is guessing.

VEIN handles this in two tiers and uses whichever it has:

| | Available | How it finds ore |
|---|---|---|
| **Tier 1** | Always | Shallow probe shafts on a lattice, ore-per-metre per cell, deep shafts placed by score |
| **Tier 2** | If the *Ore Detector Raycast* mod is installed | Real ore coordinates read straight off the detector |

Tier 2 is detected at runtime. No configuration — install the mod and the script
notices. Remove it and the script falls back without complaint.

The reasoning, and how to get the most out of Tier 1, is in
[docs/SCOUTING.md](docs/SCOUTING.md).

---

## Commands

Run these as the Programmable Block's argument.

| Command | Effect |
|---|---|
| `start` | Begin or resume the job |
| `stop` | Climb out of the shaft, return, and park |
| `halt` | Stop immediately where you are |
| `home` | Return and dock without finishing the job |
| `clear` | Clear a fault after fixing the cause |
| `reset` | Wipe the survey, keep the route and job frame |
| `record start` / `record stop` / `record clear` | Route recording |
| `job set <w> <h> <depth>` | Define the site at the ship's current position and attitude |
| `job here` | Re-anchor the existing grid where the ship is now |
| `job size <w> <h>` / `job depth <m>` | Adjust without re-anchoring |
| `job push` | Send this miner's job frame to the dispatcher, which cannot anchor one itself |
| `mode fixed\|autoore\|autovoid` | Depth strategy |
| `order serpentine\|spiral\|prospect` | Shaft ordering |
| `eject off\|stone\|stoneandice` | What to throw overboard |
| `fleet <command>` | Relay a command to every drone on the channel |
| `purge` | Release every airspace lock. The 2am escape hatch |
| `learn` / `learn reset` | Show what the ship has measured about itself, or forget it |
| `reload` | Re-read Custom Data and rescan blocks |

---

## Configuration

Everything is in the block's **Custom Data**, as INI, with comments explaining
each setting. Edit it and run `reload`. Missing keys are written back with their
defaults, so the block documents itself.

Reference: [docs/CONFIG.md](docs/CONFIG.md).

---

## Building

The script is written as modules in `src/` and assembled by a build step. The
in-game editor caps out around 100,000 characters and the commented source is
well past that, so the minified build is what you paste.

```
python3 tools/validate.py    # structural checks — stands in for the compiler
python3 tools/build.py       # -> dist/VEIN.cs and dist/VEIN.mdk.cs
python3 tools/minify.py      # -> dist/VEIN.min.cs   (paste this one)
```

| Artefact | Use |
|---|---|
| `dist/VEIN.min.cs` | **Paste into the Programmable Block.** ~98k chars. |
| `dist/VEIN.cs` | Same code, fully commented. Too large for the in-game editor. |
| `dist/VEIN.mdk.cs` | Wrapped for MDK2 / Visual Studio, for IntelliSense and real whitelist checking. |

`tools/validate.py` exists because a Space Engineers script cannot be compiled
without the game's assemblies. It catches unbalanced braces, duplicate members,
calls to methods that do not exist, and use of API the Programmable Block sandbox
blocks — which is most of the ways a modular script actually breaks.

`tools/minify.py` strips comments, then squeezes every space and line break that
is not holding two tokens apart, then shortens the fewest identifiers that get
the result under the editor limit — so the names in an in-game stack trace are
mostly still real. It verifies its own output before writing: literals unchanged,
brackets balanced, and no renamed identifier still appearing in the result. That
last check matters, because a field of a helper class is declared bare and used
qualified, and a renamer that cannot see `job.Width` will happily rename the
declaration alone and hand you a script that compiles into a different program.
Anything reached through a dot is therefore off limits. `--aggressive` renames
everything it is allowed to, for when you need the headroom.

---

## Credits

- **Keks** — [PAM] Path Auto Miner. Path recording, shaft grids, gravity-aware
  flight. The template for the whole category.
- **cheerkin** — [SCAM] Simple Concurrent Adaptive Min3r. Dispatcher/drone
  concurrency and adaptive depth from ore income.
- **Racher** — Ore Detector Raycast mod, which VEIN bridges to for Tier 2 scouting.
- **f1lmovsky** — the deobfuscated PAM source, which made studying PAM's
  internals possible at all.

VEIN is an independent implementation, not a fork of either script.

MIT licensed.
