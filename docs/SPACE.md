# Open space setup

VEIN tuned strictly for asteroid mining. Everything here assumes zero gravity —
no planets, no atmosphere, no lift limit.

## Paste this into Custom Data

```ini
[vein.identity]
role=Miner
channel=VEIN

[vein.mining]
depthMode=AutoOre
holeOrder=Prospect
eject=Stone
shaftOverlap=0.15
drillSpeed=1.8
retreatSpeed=6.0
cruiseSpeed=95
dockSpeed=2.0
cargoFullAt=0.96
drillOnRetreat=false

[vein.scouting]
probeDepth=25
probeStride=2
barrenThreshold=0.8
useOreDetectorMod=true
oreScanRange=800

[vein.safety]
minBattery=0.25
minHydrogen=0.20
resumeBattery=0.95
resumeHydrogen=0.90
liftSafetyFactor=1.0
transitAltitude=15
stateTimeout=300
stopOnDamage=false

[vein.fleet]
droneTimeout=30
laneSpacing=15
dockSlots=1

[vein.display]
lcdTag=[VEIN]
verboseEcho=true
```

## Why these differ from the planetary defaults

| Setting | Planet | Space | Reason |
|---|---|---|---|
| `cruiseSpeed` | 40 | **95** | No gravity to fight and nothing to fly into. The real cap is the server's 100 m/s limit; the controller still only commands what it can stop from. |
| `retreatSpeed` | 3.0 | **6.0** | Climbing out of a shaft costs nothing without gravity. |
| `drillSpeed` | 1.2 | **1.8** | Station-keeping is free in zero-g, so more of the thrust budget goes into cutting. Drop it if the ship jams. |
| `cargoFullAt` | 0.92 | **0.96** | No lift limit means you can fill right up. On a planet the margin exists because a full hold might not climb out. |
| `liftSafetyFactor` | 0.80 | **1.0** | Inert. There is no lift survey to apply it to. |
| `transitAltitude` | 25 | **15** | Standoff from the rock face rather than terrain clearance. Keep it above your ship's longest dimension. |
| `probeDepth` | 12 | **25** | Asteroid ore sits nearer the core than the crust. Shallow probes on an asteroid find nothing but surface stone. |
| `minBattery` | 0.30 | **0.25** | Hovering costs nothing in zero-g, so the reserve needed to get home is smaller. |
| `stateTimeout` | 240 | **300** | Transits between asteroids run longer than a planetary hop to a fixed site. |
| `laneSpacing` | 12 | **15** | Approaches converge on a rock from all directions rather than descending onto a plane, so give the lanes more room. |

## What goes unused in space

These are not worth configuring — they either do nothing or resolve to no-ops:

- **The lift survey.** `ComputeMaxFlyableMass` finds no gravity anywhere on the
  route, sets no limit, and the display omits the line. `liftSafetyFactor`
  becomes inert.
- **Gravity compensation.** The `- gravity` term in the velocity controller is
  zero, and `StoppingAccel` stops subtracting a gravity penalty — so the ship
  can actually use its full braking authority.
- **Thruster efficiency sampling.** Recorded along the path, meaningful only for
  atmospheric thrusters.
- **Hydrogen** still matters — set `minHydrogen` for the H2 thrusters most
  asteroid miners use. An ion-only ship reports full and ignores it.

## Things that matter far more in space

**A forward-facing camera is close to mandatory.** On a planet the job rectangle
sits on a surface. On an asteroid it routinely overhangs empty space, and the
camera check is what stops the ship flying a full descend-and-ascend cycle into
vacuum. Without one every empty cell is paid for the slow way.

**Probe depth is the setting to get right.** Asteroid ore is deep. If the survey
reports everything barren, raise `probeDepth` before touching anything else.

**Re-anchor rather than oversize the job.** A 5×5 grid placed on the face you are
working beats a 15×15 grid that hangs off three edges of the rock. Fly to a fresh
face and run `job here`.

## The surface-datum problem, and what VEIN does about it

Worth understanding, because it is the one genuinely asteroid-specific piece of
behaviour.

The job frame is a **flat plane**. An asteroid surface is not. A cell twenty
metres from the origin can easily have its real surface thirty metres below the
nominal plane.

Depth was originally measured from the plane, which broke three things on
irregular rock:

1. **Cells were written off without being drilled.** The ship descended through
   vacuum, passed the "ignore the first 6 m" guard while still in empty space,
   collected nothing, and the adaptive-depth logic concluded the ore had run out.
   The cell was recorded barren having never touched rock — and because the
   prospector steers by that map, one bad reading pushed it away from good ground.
2. **Probe limits stopped short of the surface.** A 12 m probe limit measured
   from the plane can halt the ship while it is still in vacuum, so the probe
   cuts nothing at all.
3. **Yields came out diluted.** Kilograms per metre counted the vacuum descent as
   drilled metres, making every deep-surfaced cell look poorer than it was.

VEIN now tracks **contact depth** — the depth at which the drills first return
material, i.e. where the real surface is. Everything measures from there:

- Adaptive depth does not begin judging a cell until contact is made.
- The depth limit becomes contact plus the requested cut, so a 25 m probe cuts
  25 m of rock wherever the surface turns out to be.
- Yield counts only metres actually cut.
- Before contact the only bound is the job depth from the plane — no rock by
  then means the cell really is empty space, and it is recorded as barren rather
  than as a failure.

Resuming a half-dug shaft needs no special handling: the already-cut section
returns no material, so contact is simply found again at the old bottom.

## Fleet notes

Nothing changes structurally, but two settings interact with the geometry:

- `laneSpacing` separates approach altitudes. Around a rock rather than above a
  plane, give it more room than the planetary default.
- Antenna range matters more — asteroid fields spread drones much further from
  the dispatcher than a single planetary site does. If drones intermittently
  drop to solo mode, that is antenna range, not the script.
