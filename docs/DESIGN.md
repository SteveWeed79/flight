# Design notes: what VEIN takes from PAM and SCAM, and what it changes

## A note on the name "S.C.U.M"

If you came here looking for S.C.U.M — the script is **[SCAM] Simple Concurrent
Adaptive Min3r** by cheerkin. It is worth knowing that the Steam Workshop listing
has since been removed for violating Steam's content guidelines, which is almost
certainly about the acronym rather than the code. The design ideas survive in the
Workshop discussions and in scripts that credit it, and they are good ideas. They
are treated seriously below.

---

## The two ancestors

### PAM — Path Auto Miner (Keks)

PAM's insight is that **the route is the hard part, and a human should provide
it**. You fly dock-to-site once; the ship replays it forever. No pathfinder is
going to work out that the gap between two ridges is the safe way home, and PAM
does not try.

The parts worth stealing:

- **Recorded waypoints with local physics.** PAM stores gravity and per-thruster-
  type effectiveness at each point. That is what lets it compute a maximum
  flyable mass rather than discovering it halfway up a climb.
- **Oriented shaft grids.** The job frame is captured from the ship's own
  attitude, so "down" means whatever the drills were pointing at. The same code
  works on a planet and on the side of an asteroid.
- **Direct thrust and gyro control.** PAM does not use the Remote Control
  autopilot, because the autopilot cannot be told to descend at exactly 1.2 m/s
  while holding attitude, and it gives up in gravity.
- **Stuck detection against depth, not velocity.** A ship grinding against rock
  can be moving plenty while going nowhere.

Where it falls short:

- **It digs the whole rectangle.** Every cell gets the same effort whether it is
  solid platinum or solid nothing.
- **States can hang indefinitely.** A docking approach that never quite lines up
  will sit there until you notice.
- **Development stopped.** The author moved on. It still runs, but nothing is
  being fixed.
- **The shipped script is obfuscated**, which makes it effectively unmodifiable
  — the reason a community deobfuscation project exists at all.

### SCAM — Simple Concurrent Adaptive Min3r (cheerkin)

SCAM's insight is that **one ship is a bottleneck, and the drills already tell
you where the ore is**.

The parts worth stealing:

- **Dispatcher and drones.** A base brain owns the work; drones own nothing but
  their current task. Drones become disposable, which is the correct property for
  something you are flying into rock.
- **Real concurrency on one deposit.** Many drones mine the same site at once,
  with exclusive claims on shared path segments so they do not collide.
- **Adaptive depth from ore income.** If the drills stop producing, stop digging.
  This is the single highest-value behaviour in any mining script and it needs no
  special blocks at all.
- **Early-game viability.** It was built to be useful before you have a
  hangar full of purpose-built miners.

Where it falls short:

- **The dispatcher is mandatory.** No base brain, no mining.
- **Tuned for spherical vanilla deposits.** Less happy with irregular ones.
- **Adaptation is per-shaft and then forgotten.** Nothing accumulates into a
  picture of the site.

---

## What VEIN does differently

### 1. It surveys before it commits

This is the main change, and it is the one that matters most in practice.

PAM digs every cell to full depth in a fixed order. SCAM stops each shaft early
when it dries up, which helps, but still visits every cell.

VEIN treats the site as something to be **prospected**:

1. **Survey pass.** Drill shallow probes — 12 m by default — on a coarse lattice,
   every second cell by default. Record kilograms of ore per metre for each.
2. **Production pass.** Score every remaining cell from the survey, using inverse-
   distance weighting over nearby known cells, plus a large bonus for any
   confirmed ore coordinates, minus a travel-distance term. Dig the best one.
   Repeat, folding each result back into the map.

A cell whose own probe came up dry is only revisited if its neighbours turned out
rich. When everything left scores below the barren threshold, the job is
finished — that is a *success*, not a failure. The entire point is to stop
digging rock that has nothing in it.

The map survives recompiles and world reloads, and on a fleet it lives at the
dispatcher, so every drone's findings steer everyone else.

### 2. Ore detection is honest, and gets better with a mod

Neither ancestor could locate ore before drilling, because vanilla Space Engineers
does not permit it. VEIN does not pretend otherwise, and instead:

- makes the vanilla path as good as it can be (the survey above), and
- **auto-detects Racher's Ore Detector Raycast mod** and switches to real ore
  coordinates when it is present.

The bridge is a runtime capability test, not a config flag: write a value to the
detector's modded terminal property and read it back. Vanilla throws; that throw
is the detection mechanism. Full reasoning in [SCOUTING.md](SCOUTING.md).

VEIN also uses vanilla camera raycast for something cameras *can* do: confirming
there is solid material down the shaft before committing to a descent. On an
asteroid, where the job rectangle routinely overhangs empty space, that saves a
full descend-and-ascend cycle per empty cell.

### 3. Fleet coordination that survives losing drones

SCAM's dispatcher model is right. VEIN tightens the failure handling:

- **Shaft assignments are timed leases, not grants.** A drone that dies holding
  one does not remove that cell from the job forever; the lease expires and the
  cell is reissued.
- **Drones are presumed dead on silence,** and everything they held — shaft,
  dock slot — is reclaimed.
- **Altitude lanes.** Each drone gets its own height band over the site, so two
  drones crossing never share an altitude. Cheap, and it removes the entire class
  of mid-air collisions swarm scripts are known for. Lanes repack when a drone
  leaves, so three drones use lanes 0/1/2 rather than 0/3/7.
- **Work goes to the nearest drone.** The dispatcher scores the site from the
  requesting drone's position, not from the base.
- **Stale reports are rejected.** Only the drone currently holding a lease may
  report on it, so a late message from an expired lease cannot overwrite the
  result of whoever is digging that cell now.
- **The dispatcher is optional.** A miner that hears no beacon runs the identical
  job logic locally. A miner whose dispatcher goes quiet mid-job logs it and
  continues solo rather than parking forever.

### 4. Stability work

The things that make a script trustworthy overnight rather than something you
babysit:

- **A watchdog on every state.** Nothing runs unbounded. Each state has a
  sensible recovery: a hung shaft backs out and blacklists the cell after three
  strikes; a hung route heads home, because the route home is known-good; a hung
  dock approach retries three times and then faults rather than grinding at the
  connector.
- **Clamped `dt`.** A paused game, a world load or a laggy server hands you a
  garbage timestep. Unclamped, the controller applies one colossal correction on
  the first tick back — an excellent way to fly a loaded miner into a mountain.
- **A fixed control time constant, not `dt`.** Gain that depends on server tick
  rate is why scripts that fly beautifully in single player oscillate themselves
  apart on a busy dedicated server.
- **Conservative stopping distance.** The speed limiter assumes gravity is
  working fully against you whatever way you are pointing. It costs a little
  speed and buys a lot of not-crashing.
- **`IsSameConstructAs` on every block query.** Without it, a miner docked at a
  base grabs the base's thrusters and batteries, reports enormous lift, and flies
  itself into the ground. Subgrids on rotors and pistons still count as ours.
- **Live thrust capacity.** `MaxEffectiveThrust` is recomputed every tick, not
  cached at compile. An atmospheric thruster loses authority as you climb, and a
  script that caches this will fly you off the top of the atmosphere and drop you.
- **Instruction budget guards.** Expensive optional work — block scans, fleet
  iteration, raycasts — is deferred when the tick is already loaded, so the block
  is not killed for complexity.
- **It never resumes mid-shaft after a reload.** The world has moved on: the ship
  may have been dragged, the voxels may have been changed by someone else, and
  the control loop has no history. It comes up idle and waits to be told to
  continue. A fault, by contrast, is preserved, because whatever caused it
  probably has not fixed itself.
- **Integers on the wire.** Every number in an inter-grid message is an integer
  scaled by 1000. A script that writes `"12.5"` and is read by a client whose
  locale uses a comma decimal separator parses it as `125`, and a drone that
  thinks the shaft is ten times deeper than it is will drill until something
  breaks. Integers have no separator and cannot go wrong.

### 5. Readable source

PAM ships obfuscated. VEIN ships as modules in `src/` with the reasoning written
down, plus a build step that produces the minified script the editor will accept.
The minified output keeps identifier names by default, so an in-game stack trace
still names real methods.

---

## What VEIN does *not* do

Worth being clear about, so nobody wastes an evening:

- **No pathfinding or obstacle avoidance en route.** It flies your recorded path.
  If you record a path through a mountain, it will fly through the mountain.
- **No combat awareness.** `stopOnDamage` will bring it home when blocks break;
  it will not evade.
- **No rotor or piston drill arms.** It flies the whole ship into the hole, like
  both its ancestors.
- **No console support.** Programmable Blocks are PC and dedicated server only.
  That is an engine limitation, not a choice.
- **It cannot see ore through rock in vanilla.** Nothing can. See
  [SCOUTING.md](SCOUTING.md).

---

## Tuning notes

| Symptom | Setting | Direction |
|---|---|---|
| Ship jams in shafts | `drillSpeed` | Lower. Above ~2 m/s drills stop keeping up. |
| Too many dry holes | `probeStride` | Lower — survey more finely. |
| Survey takes too long | `probeStride`, `probeDepth` | Raise stride, lower depth. |
| Gives up on good ground | `barrenThreshold` | Lower. |
| Fills up too fast | `eject` | `Stone` roughly triples time on site. |
| Strands itself in gravity | `liftSafetyFactor` | Lower to 0.7 or below. |
| Drones bump each other | `laneSpacing` | Raise above the tallest drone. |
| "Script too complex" | `verboseEcho` off; smaller job grid | — |
