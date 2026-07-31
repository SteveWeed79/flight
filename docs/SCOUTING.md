# Scouting: how VEIN finds ore

## The constraint nobody can engineer around

**Vanilla Space Engineers does not let in-game scripts read the Ore Detector.**

This is not an oversight VEIN can work around with cleverness. The API has been
proposed to Keen more than once — there is a rejected pull request against the
official repository adding exactly this, filling a list of ore names and
positions — and it has never shipped. Scripting veterans confirm it repeatedly on
the forums.

What is *not* available:

- `IMyOreDetector` exposes `DetectionRadius` and `BroadcastUsingAntennas`. That
  is all. There is no way to ask it what it has found.
- The ore markers you see on your HUD are generated client-side and are not
  reachable from a Programmable Block.
- Camera raycast returns `MyDetectedEntityInfo`, and for voxels the type comes
  back as `Asteroid` or `Planet`. Never `Cobalt`. Never an ore type.
- Sensors detect entities, not materials.

So any vanilla script claiming to fly straight to ore is either using a mod or
guessing. VEIN says so up front rather than quietly digging dry holes and letting
you assume it knows something.

Given that, there are exactly two honest strategies, and VEIN implements both.

---

## Tier 1 — Probe mapping (vanilla, always on)

If you cannot see through rock, **dig cheaply and remember what you found.**

The drills are already a perfectly good ore sensor. The only question is how to
spend the least drilling to learn the most about a site.

### Survey pass

Drill shallow probes on a coarse lattice — by default 12 m deep, every second
cell in each axis. A probe costs roughly a quarter of a full shaft and tells you
whether that part of the site is worth committing to.

Each probe records **kilograms of valuable ore per metre drilled**. Stone is
excluded; it is everywhere and tells you nothing.

With `probeStride = 2`, a 10×10 site is surveyed with 25 shallow holes instead
of committed to with 100 deep ones.

### Production pass

Once the lattice is surveyed, every remaining cell is scored:

```
score = inverse-distance-weighted yield of dug cells within 3 cells
      + 25 / (1 + lateral distance)   for each confirmed ore sighting
      - 0.004 × travel distance in metres
```

with an optimism prior of `1.5 × barrenThreshold` for cells that have no
evidence either way — so untouched ground is preferred over ground already proved
empty.

The highest-scoring cell is dug next, at full depth, and the result folds straight
back into the map. Ore tends to come in contiguous deposits, so a rich probe pulls
its neighbours up and the ship works outward from a strike instead of marching
across the grid.

A cell whose own probe came up dry is skipped unless its neighbours turned out
rich. When nothing left scores above the threshold, **the job is complete** — the
whole point is to stop digging rock with nothing in it.

### Why this beats what PAM and SCAM do

- PAM digs the full rectangle to full depth regardless.
- SCAM stops each shaft when it dries out, which is a real improvement, but still
  visits every cell and forgets each result afterwards.
- VEIN spends shallow holes on information and deep holes on ore, and the
  information accumulates.

### Tuning the survey

| Setting | Effect |
|---|---|
| `probeStride` | Lattice coarseness. `2` = every other cell. Raise for a faster, rougher survey; lower for a finer one. |
| `probeDepth` | Metres per probe. Must be deep enough to reach the ore layer — on planets ore often starts well below the surface. |
| `barrenThreshold` | kg/m below which a cell is written off. Lower it if the ship abandons ground you know is good. |
| `holeOrder` | Set to `Serpentine` or `Spiral` to disable prospecting entirely and dig everything, PAM-style. |

The one setting that actually matters: **`probeDepth` must reach the ore.** If
ore on your planet starts at 20 m and you probe to 12 m, every probe reports
barren and the script concludes, correctly given its evidence, that the site is
empty. If the survey says everything is dry and you know it is not, this is why.

---

## Tier 2 — Ore Detector Raycast bridge (optional mod)

If [Racher's **Ore Detector Raycast**](https://steamcommunity.com/sharedfiles/filedetails/?id=1967157772)
mod is installed, real ore coordinates become available and VEIN uses them.

### How the bridge works

The mod adds terminal properties to `IMyOreDetector` that scripts can reach
through the generic property API:

| Property | Type | Use |
|---|---|---|
| `RaycastTarget` | `Vector3D` | Write a world position to fire a ray at it |
| `RaycastResult` | `MyDetectedEntityInfo` | Read the hit: `.Name` is the ore type, `.HitPosition` the location |
| `AvailableScanRange` | `double` | Charge, like a camera's |
| `OreBlacklist` | `string` | Ore types to ignore |

Detection is a runtime capability test rather than a config flag: write a value,
read it back, and check it survived. On vanilla the write throws, and **that throw
is the detection mechanism**. Install the mod and VEIN notices on its next boot;
remove it and VEIN falls back to Tier 1 without complaint.

### How VEIN aims the rays

Deliberately **not** a 360° sweep of the sky. With a job set, VEIN rakes the
volume *underneath the site* — walking cell by cell across the grid and through
four sample depths down each one.

A blind spherical sweep looks more impressive and is worth much less: a hit
somewhere off in space is trivia, whereas a hit under cell [4,7] changes which
hole gets dug next. Sightings land directly on the yield map through
`SightingBonus`, where the 25/(1+distance) term dominates every other signal.

With no job set, it falls back to a coarse spherical sweep for exploration.

Sightings are deduplicated by position — otherwise one big vein dominates the map
purely by being scanned more often — and expire when the cell above them is mined
out. On a fleet, every sighting is forwarded to the dispatcher, so a lead found by
one drone steers all of them.

Rays are fired at most every sixth tick, skipped when the instruction budget is
already tight, and skipped when the detector has not charged enough range. They
are not cheap.

---

## What cameras are actually good for

Cameras cannot see ore. They *can* see rock, and that is worth having.

Before committing to a descent, VEIN raycasts down the shaft axis — the drills
point forward and so does a forward-mounted camera, so one ray answers it. If the
scan comes back empty, the cell is over open space and is skipped without flying
down to find out.

On an asteroid, where the job rectangle routinely overhangs nothing, this saves a
full descend-and-ascend cycle per empty cell.

The tri-state matters here and is easy to get wrong: **"no camera had enough
charge" and "the camera looked and there is nothing there" are different
answers.** Collapsing both into a single failure value makes the check unable to
ever fire. VEIN never skips a cell on a scan that did not happen — refusing to
mine because a camera was busy would be far worse than digging one dry hole.

Mount at least one camera facing the same way as the drills to get this. Without
one, everything still works; you just pay for empty cells the slow way.

---

## Practical advice

**On planets.** Ore is in layers, often 15–40 m down. Set `probeDepth` past the
overburden or every probe reports barren. Start with a wide, shallow job to find
the deposit, then set a smaller, deeper job on top of it.

**In space.** Asteroid ore is in blobs, usually nearer the core than the surface.
`probeStride 2` with a generous `probeDepth` works well. A camera facing the
drills pays for itself immediately here.

**Reading the map.** The LCD draws the site directly:

```
. new    # ore    - dry
X done   ! stuck  o busy    @ current shaft
```

If the `#` marks cluster at one edge, your job rectangle is in the wrong place.
Fly there, run `job here`, and the grid re-anchors around the ore.

**When it says the job is complete but the site looks untouched**, it surveyed
and concluded there was nothing worth digging. Check `probeDepth` first, then
`barrenThreshold`. `reset` clears the survey and starts over without losing your
recorded route.
