# Configuration reference

Everything lives in the Programmable Block's **Custom Data** as INI. Edit it, then
run the `reload` argument (or recompile). Missing keys are written back with their
defaults, so the block always documents itself.

Values are clamped to sane ranges on load. A malformed file falls back to defaults
and shows a warning on the screen rather than bricking the block.

---

## `[vein.identity]`

| Key | Default | Meaning |
|---|---|---|
| `role` | `Miner` | `Miner` or `Dispatcher`. Miners find a dispatcher automatically; with none on the channel they run the job themselves. |
| `shipName` | *(blank)* | Shown on fleet displays. Blank uses the grid name. |
| `blockTag` | *(blank)* | Only use blocks whose name contains this. Blank means the whole construct. |
| `channel` | `VEIN` | IGC channel. Change it if two fleets share a server. |

**`blockTag`** matters if a miner docks at a base that also has drills or
thrusters. Block queries are already restricted to the miner's own construct, so
this is a second line of defence rather than the primary one.

---

## `[vein.mining]`

| Key | Default | Meaning |
|---|---|---|
| `depthMode` | `AutoOre` | `Fixed` — always drill to job depth. `AutoOre` — stop when ore stops arriving. `AutoVoid` — stop only on breakthrough into empty space. |
| `holeOrder` | `Prospect` | `Serpentine` — corner to corner, least travel. `Spiral` — outward from centre. `Prospect` — survey, then chase the ore. |
| `eject` | `Stone` | `Off`, `Stone`, or `StoneAndIce`. Needs an ejector or a connector in Throw Out mode. |
| `shaftOverlap` | `0.15` | Fraction by which adjacent shafts overlap. Higher clears more rock and digs more holes. |
| `drillSpeed` | `0.8` | m/s downward while cutting. Starting point when `adaptiveDrill` is on. |
| `retreatSpeed` | `3.0` | m/s backing out of a shaft. |
| `cruiseSpeed` | `40.0` | m/s ceiling along the recorded route. |
| `dockSpeed` | `0.8` | m/s on final dock approach. |
| `cargoFullAt` | `0.92` | Return home at this cargo fill fraction. |
| `drillOnRetreat` | `false` | Keep drills running on the way up. Widens the shaft, costs time. |
| `adaptiveDrill` | `true` | Learn the cutting speed this hull can actually hold. |

**`drillSpeed` is a starting point, not a fixed speed.** With `adaptiveDrill` on
— the default — the ship measures how fast the hole is actually deepening against
how fast it was told to descend, backs off hard when the drills fall behind, and
creeps up while they are keeping cleanly ahead. It stays between a quarter and
2.5x of this value, so the setting still bounds the outcome. Watch the `Learn`
line on the display to see where it has settled; `learn reset` starts it over
after a refit. Turn `adaptiveDrill` off to use this number exactly.

Above roughly 2 m/s the drills stop keeping up with the hull and the ship wedges,
which is what the adaptation is looking for. If it jams often anyway, lower this.

**`cruiseSpeed` is a ceiling, not a target.** Actual speed is capped by whatever
the ship can genuinely stop from given its mass and the local gravity.

**`eject = Stone`** roughly triples time on site. Stone is the overwhelming
majority of what a drill picks up and hauling it home is pure waste.

---

## `[vein.scouting]`

| Key | Default | Meaning |
|---|---|---|
| `probeDepth` | `12.0` | Metres per test shaft before judging a cell. |
| `probeStride` | `2` | Probe every Nth cell in each axis to build the first map. |
| `barrenThreshold` | `0.8` | kg of ore per metre below which a cell is written off. |
| `useOreDetectorMod` | `true` | Auto-detect the Ore Detector Raycast mod. Harmless if absent. |
| `oreScanRange` | `500.0` | Metres the modded detector rays reach. |
| `growToOre` | `true` | Extend the grid when the ore is still rich at its edge instead of calling the job done. Prospect order only. |
| `growLimit` | `400` | Most cells the job may grow to. `400` is a 20x20 site. |

**`probeDepth` must reach the ore layer.** If ore on your planet starts at 20 m
and you probe to 12 m, every probe reports barren and the script concludes the
site is empty. This is the single most common cause of "it says there's nothing
here". See [SCOUTING.md](SCOUTING.md).

`probeStride = 1` surveys every cell — thorough and slow. `3` or `4` is good for a
large site you want a rough picture of quickly.

---

## `[vein.safety]`

| Key | Default | Meaning |
|---|---|---|
| `minBattery` | `0.30` | Head home below this battery fraction. |
| `minHydrogen` | `0.25` | Head home below this hydrogen fraction. |
| `resumeBattery` | `0.80` | Resume work above this. |
| `resumeHydrogen` | `0.90` | Resume work above this. |
| `liftSafetyFactor` | `0.80` | Fraction of measured lift the ship is willing to spend. |
| `transitAltitude` | `25.0` | Metres above the job plane when crossing the site. |
| `stateTimeout` | `240.0` | Seconds before the watchdog calls a state hung. `0` disables it. |
| `minUranium` | `2.0` | Head home below this many kg of uranium across all reactors. Ignored with no reactors; `0` disables. |
| `stopOnDamage` | `false` | Return home if blocks take damage mid-job. |
| `adaptiveBraking` | `true` | Learn the braking derate from how much authority approaches actually demand. |
| `brakeDerate` | `0.60` | Fraction of the theoretical `sqrt(2ad)` stopping speed used. Starting point when `adaptiveBraking` is on, fixed value when it is off. |

Resume thresholds below their matching abort thresholds would trap the ship in a
dock/undock loop forever, so they are quietly corrected upward on load.

**`liftSafetyFactor`** applies to the lift survey taken while recording the route.
`0.8` leaves 20% in hand for a heavy load at a bad angle. Lower it if the ship
ever struggles on the climb home.

**`stateTimeout = 0` disables the watchdog**, which is rarely what you want. The
watchdog is the main reason the script recovers on its own.

**`brakeDerate` is the margin on `sqrt(2ad)`.** The formula is exact for an ideal
actuator and optimistic for a real one: thrusters ramp rather than snapping to
full output, tick rate varies, and mass climbs while the hold fills. With
`adaptiveBraking` on, the ship watches how much of its braking authority
approaches genuinely demand and moves this figure to suit itself, between 0.30
and 0.85. Lower is slower and safer. For reference, PAM ships 0.70 and SCAM
0.50 — and neither number is derived from anything but watching ships.

---

## `[vein.fleet]`

Only meaningful with a dispatcher.

| Key | Default | Meaning |
|---|---|---|
| `droneTimeout` | `30.0` | Seconds of silence before a drone is presumed lost and its shaft reissued. |
| `laneSpacing` | `12.0` | Metres between drone altitude lanes over the site. |
| `dockSlots` | `1` | Dispatcher only: how many connectors are available for unloading. |
| `airspaceLock` | `true` | One drone at a time in the airspace over the site, granted by the dispatcher with a queue behind it. |
| `lockPatience` | `60.0` | Seconds a drone waits for the airspace before going anyway. Capped at half `stateTimeout`. |

**`laneSpacing` must comfortably exceed the tallest drone's height.** Lanes are
formation, not exclusion: they stop two drones cruising at the same height, and
that is all they do. `airspaceLock` is what actually stops two drones wanting the
same place at the same time.

**`airspaceLock` is worth leaving on.** A drone asks the dispatcher before
crossing the site and waits its turn if somebody else is out there — inside its
own shaft, if it is on the way up, because that is the one volume nobody else can
be sent to. Locks expire on silence and on a hard timeout, so a drone that
explodes holding one does not stall the deposit. `purge` releases everything by
hand if it ever comes to that.

**`lockPatience` is a safety valve, not a tuning knob.** A dispatcher that stops
answering must not be able to park the whole fleet in mid-air, so a drone that
has waited this long proceeds on lanes alone and says so in the log.

---

## `[vein.display]`

| Key | Default | Meaning |
|---|---|---|
| `lcdTag` | `[VEIN]` | LCDs whose name contains this get VEIN output. |
| `verboseEcho` | `true` | Write the full status to the block's detail panel. |

Turn `verboseEcho` off if you are close to the instruction budget. Watch the
`Load` line on the display — sustained above 80% is the warning sign.

---

## Example: cautious planetary miner

```ini
[vein.mining]
drillSpeed=0.8
cargoFullAt=0.80
eject=Stone

[vein.scouting]
probeDepth=30
probeStride=3

[vein.safety]
minBattery=0.45
liftSafetyFactor=0.65
```

## Example: asteroid drone in a fleet

```ini
[vein.identity]
role=Miner
shipName=Drone-2

[vein.mining]
drillSpeed=2.0
cruiseSpeed=90
eject=StoneAndIce

[vein.scouting]
probeDepth=20
probeStride=2

[vein.safety]
transitAltitude=40
```
