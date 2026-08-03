# Correction roadmap

Findings from a full read of `src/` (22 modules, 5,332 lines) and `tools/`, with a
proposed order of work.

This is not a bug list sorted by severity. It is sorted by **what unblocks what**,
because a third of the entries below cannot be confirmed — let alone fixed with
confidence — until the project has a compiler and somewhere to run its own logic.
That is Phase 0, and it comes first for a reason given at the end of this document.

## How to read an entry

Each carries a confidence marker, because they were not all established the same way:

| | |
|---|---|
| **Verified** | Demonstrated by execution or by an exhaustive check. Not a matter of opinion. |
| **Provable** | Follows from the code alone. Two call sites contradict each other, or a value is written and never read. |
| **Likely** | Depends on a fact outside the repo — usually a Space Engineers API convention. Needs a compile or a runtime to settle. |
| **Judgement** | The code does what it says; whether that is right is a design call. |

Every entry has a **Done when** line. Where that line does not need the game, it
should become a test rather than a one-off check.

---

# Phase 0 — Ground truth

Nothing here is a defect. It is the machinery whose absence is why the defects
below went unnoticed, and why some of them are still marked *Likely*.

### T-1 — Get `dist/VEIN.mdk.cs` through a real compiler

**Why first.** `tools/validate.py` is a regex linter and says so. It cannot see a
type error, an overload resolution failure, a wrong API signature, or the real
Programmable Block whitelist — it checks nine regexes where the actual whitelist
runs to hundreds of entries. The MDK2 harness exists precisely to get a genuine
compile, and it has never been used.

One pass would settle **C-1** outright and retire the entire *Likely* class,
including whether the ore-detector bridge (`12_Scout.cs:76`) is asking for
`double` where the mod supplies `float` — a mismatch that throws into a `catch`
and disables Tier 2 scouting in a way indistinguishable from the mod being absent.

**Done when** a compile of `dist/VEIN.mdk.cs` runs in CI, or at minimum is
documented as a release step in `README.md`.

### T-2 — Make the pure logic runnable outside the game

Shaft ordering, the wire codec, persistence and cell scoring are all pure
functions of their inputs. None of them needs Space Engineers to execute.

This is the highest-value item in the document, and **C-3** is the evidence: it
survived a careful read of the function and fell in ninety seconds to a twelve-line
simulation. Reading finds local defects. It is poor at coverage, convergence, and
round-trips.

Three tests worth having before any others:

- **Shaft-order coverage.** For every grid from 1×1 to 40×40 and every `HoleOrder`,
  repeatedly select and mark until selection returns −1, then assert every cell
  was visited. This is the test that catches **C-3** and would catch its
  equivalent in any future ordering.
- **Wire round-trip.** `JobFrameFields()` → `AdoptJobFrame()` over generated
  frames, including malformed and truncated ones. The whole fleet's correctness
  rests on those two staying in step and nothing but a comment enforces it.
- **Persistence round-trip.** `SerializeState()` → `LoadState()` asserted lossless.
  `STORAGE_REV` catches drift *between* versions; nothing catches drift within one.

**Done when** the three tests run from one command and are wired into the same
step as `validate.py`.

### T-3 — Validate what is actually shipped

`validate.py` reads `src/`. The artefacts pasted into the game are `dist/VEIN.cs`
and `dist/VEIN.min.cs`, and the `--no-sprites` variant applies five textual
substitutions that no tool checks at all.

**Done when** `validate.py` accepts a path argument and CI runs it over the
built output for both the default and `--no-sprites` builds.

### T-4 — Reconcile `minify.py`'s two string parsers

`strip_comments` understands verbatim strings (`tools/minify.py:96`).
`split_code_and_strings` — which feeds both the renamer and the verifier — does
not (`tools/minify.py:287`), and knows nothing of interpolated strings either.
A `$"…{expr}…"` would have its interpolation treated as literal text: the
declaration gets renamed, the use site does not, and the verifier's
"renamed name still appears in output" check cannot catch it because it only
scans code segments.

There are no `@"` or `$"` strings in the source today, so this is latent. It is
also exactly the failure mode the tool's own header claims to have closed.

**Fix.** One string scanner, used by every pass. If verbatim and interpolated
forms are not going to be supported, detect and reject them loudly rather than
mis-parsing them silently.

**Done when** both forms are either handled or rejected with a clear error, and a
tool test asserts it.

### T-5 — Pin down module concatenation order

`build.py` sorts modules by filename; the `00_`–`21_` numbering is convention with
nothing enforcing it. C# instance field initializers execute in declaration order,
so a future field initialised from another *field* rather than a `const` would
behave differently depending on where its module sorted. Nothing today does this,
and nothing would notice if something started.

**Done when** either the ordering requirement is documented in `build.py` and
`README.md`, or a check asserts that no field initializer references another field.

---

# Phase 1 — Blocking correctness

These stop the ship working. C-2 through C-6 are fixable now; C-1 is the one item
genuinely gated on Phase 0.

### C-1 — The docking axis is used with two opposite signs

**Where** `11_Miner.cs:91-93` (`StUndocking`), `11_Miner.cs:552-555` and
`11_Miner.cs:564` (`StDocking`), captured at `10_Path.cs:50-53`.

**Symptom.** `homeDockForward` is the ship's own connector's `WorldMatrix.Forward`,
recorded while mated. Two consumers cannot both be right:

- `Orient(-axis, homeDockUp)` commands the nose to `-c`. To reproduce the recorded
  docked attitude that requires `c == -n_docked` — the connector's Forward is the
  ship's Backward — which puts the base in the `+c` direction.
- `hold = mate + axis * standoff` and `away = dockConnector.WorldMatrix.Forward`
  both require `+c` to point *away* from the base.

Exactly one is inverted, whichever way the convention falls. Under the standard
Space Engineers convention — `WorldMatrix.Forward` points out of the mating face,
which is what every community docking script relies on — it is `hold` and `away`
that are wrong. That makes undocking command the ship `shipRadius * 2.5` metres
straight into the base, where it grinds until the watchdog fires; `Undocking`
falls into the `default:` arm of `Watchdog` (`05_Program.cs:155`) and faults. The
first thing the ship does after `start` is fail.

**Fix.** Settle the convention, then derive one signed vector — call it
`dockOutward` — at capture time and use it everywhere. The bug exists because the
sign is re-derived at three call sites.

**Confidence** Likely on which side is wrong; **Provable** that the two sites
disagree.

**Done when** a ship undocks, flies out, returns and mates without operator input.

### C-2 — `StDocking` stages 1–2 mix the controller and connector frames

**Where** `11_Miner.cs:551-573`.

**Symptom.**

```csharp
Vector3D mate    = homeDock.Position;   // recorded CONNECTOR position
Vector3D offAxis = shipPos - mate;      // shipPos = CONTROLLER position
double lateral   = (offAxis - axis * along).Length();
```

Mated, `shipPos == mate - connectorOffset`, so `offAxis == -connectorOffset` and
`lateral` equals the connector's **permanent** lateral offset from the controller.
On any ship whose dock connector is more than a metre off the controller's mating
axis — a side port, an off-centre rear connector, a cockpit off the centreline —
`lateral > 1.0` holds forever. Stage 1 never releases, the stall detector at
line 614 is never reached because it sits after the `return`, and the only exit is
the 240 s watchdog → three retries → `EnterFault`.

Stage 3 already does this correctly at line 591
(`Vector3D.Distance(shipPos + connectorOffset, mate)`), which is what makes it an
oversight rather than a choice.

**Fix.** Hoist `connectorOffset` above line 556 and measure from the connector:

```csharp
Vector3D offAxis = (shipPos + connectorOffset) - mate;
```

**Confidence** Provable.

**Done when** a ship with a deliberately off-centre connector completes stage 1.
Testable without the game by extracting the stage predicates.

### C-3 — `NextSpiral` does not cover the grid

**Where** `09_Job.cs:168-192`; the cause is `steps = bound * bound` at line 173.

**Symptom.** `order spiral` reaches every cell **only when width equals height and
both are odd**. Simulated exhaustively:

| Job | Reachable | |
|---|---|---|
| 5×5 | 25 / 25 | OK |
| 4×4 | 9 / 16 | misses 7 |
| 5×8 | 35 / 40 | misses 5 |
| 10×10 | 81 / 100 | misses 19 |
| 20×20 | 361 / 400 | misses 39 |

300 of the 576 grid shapes from 1×1 to 24×24 are incomplete. Because
`GrowJobTowardOre` returns false for non-Prospect orders, the `-1` propagates
straight to `jobComplete = true`: the ship reports the job finished with up to a
tenth of its shafts never dug, and nothing anywhere says so.

The spiral starts at `(W/2, H/2)` — integer division — so on any even dimension
its square sits half a cell off the grid and clips the far edge, while
`bound * bound` provides exactly enough steps for a perfectly aligned square and
no slack.

**Fix.** `steps = (2 * bound + 1) * (2 * bound + 1)`. Verified to cover every grid
from 1×1 to 40×40; worst case 6,561 iterations against 1,600, and it returns on
the first available cell. Separately, give `NextSpiral` and `NextSerpentine` the
same backstop `NextProspect` already has at `09_Job.cs:244` — if selection finds
nothing but `RemainingCellCount() > 0`, return the first available cell. No
ordering function should be able to end a job early.

**Confidence** Verified.

**Done when** the coverage test from **T-2** passes for all three orderings.

### C-4 — A world reload silently relaunches every miner

**Where** `18_Persist.cs:165-183` and `11_Miner.cs:56`.

**Symptom.** `LoadLifecycle` restores `jobRunning` verbatim while forcing
`state = Idle`, and logs *"Resumed from X — idling, run 'start' to continue"*.
But `StIdle` reads `if (!jobRunning || jobComplete) return;` and then transitions
to `Undocking`. A ship that was working when the world saved launches on the first
tick after load, with no operator. On a server restart the whole fleet launches at
once.

Three places state the intended behaviour and the code contradicts all three: the
comment at `18_Persist.cs:173-177`, the log message at line 182, and
`DESIGN.md:360` — *"It comes up idle and waits to be told to continue."*

**Fix.** Do not persist `jobRunning` as true; write `0` and require `start`. If
auto-resume is wanted instead, it needs to be deliberate, gated on a config key,
and the three statements above need correcting.

**Confidence** Provable.

**Done when** a save taken mid-job reloads to `Idle` and stays there.

### C-5 — `reload` splits the ship's IGC brain

**Where** `17_Commands.cs:56-60`, `13_Igc.cs:15-21`.

**Symptom.** `reload` runs `LoadConfig()` and `ScanBlocks()` but not `SetupIgc()`.
Change `channel` in Custom Data and the listener stays registered on the old
channel while every send uses the new one. The ship transmits on one channel and
listens on another. No error is raised anywhere.

**Fix.** Re-register the listeners in `reload` when the channel string changed.

**Confidence** Provable.

**Done when** changing `channel` and running `reload` moves both directions.

### C-6 — Job dimensions have a floor and no ceiling

**Where** `09_Job.cs:38-40`, `17_Commands.cs:231-232`, `13_Igc.cs:188-190`;
`02_Types.cs:87`.

**Symptom.** All four entry points — `job set`, `job size`, and `AdoptJobFrame`
from both the beacon and a job push — clamp with `Math.Max(1, …)` and stop there.
`growLimit` is clamped to `[1, 4096]` and bounds *automatic* growth only.

- `job set 2000 2000 40` allocates four million `YieldCell` objects inside one
  tick in `RebuildCells`. Best case that faults the block via `Main`'s catch. A
  typo does it.
- `Job.CellCount` is an unchecked `int` multiply. A corrupt beacon carrying a
  width near 2×10⁹ passes the clamp, overflows to a negative length, and
  `new YieldCell[negative]` throws. `ValidateJobBasis` carefully checks the axes
  and never looks at the dimensions.

**Fix.** Apply `growLimit` — or a dedicated ceiling — at all four entry points,
and range-check the dimensions in `ValidateJobBasis` alongside the basis vectors.

**Confidence** Provable.

**Done when** `job set 9999 9999 40` is refused with a message, and a generated
malformed frame in the **T-2** wire test is rejected rather than throwing.

---

# Phase 2 — Robustness

Not blocking. These are what turn a working script into one that needs babysitting.

### R-1 — `ShaftHasRock` trusts any camera, whatever it is pointing at

**Where** `12_Scout.cs:230-257`, consumed at `11_Miner.cs:242`.

`TryScanAhead` walks `cameras` in scan order and uses the first with charge. The
doc comment assumes a forward camera; nothing filters on facing. A rear- or
side-mounted camera reports empty space, `SkipEmptyCell` fires, and the cell is
written off as `Barren` — effectively permanently, since `NextProbeCell` only
revisits `Unknown` and `Consider` skips sub-threshold barren cells. One badly
placed camera quietly kills most of a site.

**Fix.** Only use cameras whose `WorldMatrix.Forward` is within a few degrees of
the drill axis. Fall back to the existing "could not tell, assume rock" path when
none qualifies. **Confidence** Provable.

### R-2 — No plausibility check on `job.Origin`

**Where** `09_Job.cs:62-89`, `15_Util.cs:103-109`.

`ValidateJobBasis` checks the axes are non-degenerate and orthogonal and never
looks at `Origin`. `DecV` returns `Vector3D.Zero` on any parse failure, so a
truncated beacon or a corrupt Storage line produces a job anchored at the world
origin with a perfectly valid basis.

**Fix.** Reject a frame whose origin is implausibly far from the ship or from the
recorded dock. **Confidence** Provable.

### R-3 — `UnloadToBase` will fill another docked ship

**Where** `08_Cargo.cs:227-229`.

Selects *every* `IMyCargoContainer` in the terminal system not on our construct,
then relies on `IsConnectedTo`. Two miners on the same base are conveyor-connected
through it, so miner A pumps its ore into miner B. It is also an unfiltered
whole-grid enumeration with a delegate, run every tick while `Unloading`.

**Fix.** Restrict to the grid reached through `dockConnector.OtherConnector`, or
honour a destination tag. **Confidence** Provable.

### R-4 — Dock slots leak on fault and on `halt`

**Where** `13_Igc.cs:116-121`, `05_Program.cs:162`, `17_Commands.cs:34`.

`ReleaseDock()` is reached only from `StServicing`. `EnterFault` releases the lease
and the airspace lock but not the slot; `halt` releases nothing. Meanwhile
`SendHeartbeat()` runs unconditionally at the end of `TickMiner`, so a faulted
drone never goes quiet and `ExpireDrones` never reclaims it. With `dockSlots = 1`
one faulted drone blocks the pad for everyone until `dockPatience` expires on each
arrival.

**Fix.** Call `ReleaseDock()` from `EnterFault` and from `halt`. **Confidence** Provable.

### R-5 — `job set` and `job size` renumber leased cells on a dispatcher

**Where** `17_Commands.cs:208` and `17_Commands.cs:215-237`.

`GrowJobTowardOre` (`09_Job.cs:449`) and `OnJobPush` (`13_Igc.cs:285`) both
correctly refuse while `AnyCellLeased()`. `CmdJob` does not — its only guard is
`role == Role.Miner` on the flying check. A dispatcher operator resizing a live
job re-indexes every cell and sends the fleet to the wrong rock.

**Fix.** Same `AnyCellLeased()` guard, same place. **Confidence** Provable.

### R-6 — Stale `lockHoldPoint`

**Where** `11_Miner.cs:219`, `11_Miner.cs:224`, `21_Airspace.cs:170`.

`Vector3D.Zero` is the "unset" sentinel, but nothing clears it on `Approaching`
entry or in `ReleaseAirspace`. A drone that waited for airspace on one shaft and
then abandoned it will, on its next wait, hold station at the *previous* shaft's
hold point. `StInbound` does this correctly for the dock equivalent via
`ResetDockWait()`; the asymmetry is the tell.

**Fix.** Clear it on state entry, matching `ResetDockWait`. **Confidence** Provable.

### R-7 — A full base becomes an opaque timeout fault

**Where** `08_Cargo.cs:220-260`, `05_Program.cs:155`.

`UnloadToBase` returns `cargoFill < 0.02`. If base storage is full the state never
completes, `Unloading` hits the watchdog's `default:` arm, and the operator gets
*"State Unloading timed out"* — which points at the wrong thing entirely.

**Fix.** Detect "nothing moved and every destination is at capacity" and fault with
that as the reason, or hold and report it as a wait rather than a fault.
**Confidence** Judgement.

### R-8 — Unchecked enum casts from the wire and from Storage

**Where** `13_Igc.cs:330`, `13_Igc.cs:410`, `18_Persist.cs:226`.

Out-of-range values produce enums matching no `switch` arm and printing as bare
integers on the dashboard.

**Fix.** Range-check on decode. **Confidence** Provable.

### R-9 — `EnterFault` is unprotected in the top-level catch

**Where** `05_Program.cs:78-84`.

`SafeStop()` is wrapped in `try/catch`; `EnterFault` is not, and it calls
`ReleaseLease` → `SendShaftReport` → IGC. If that throws, the exception escapes
`Main` and the block stops.

**Fix.** Wrap it. **Confidence** Provable.

### R-10 — Two off-by-ones

`11_Miner.cs:339` — `stuckRetries > 3` allows four attempts while the log says
`(n/3)`. `10_Path.cs:102-106` — recording caps the path at 400 and then calls
`StopRecording`, which appends a 401st. Both cosmetic. **Confidence** Provable.

---

# Phase 3 — Fleet and protocol

### F-1 — `LD|nojob` produces a per-tick request storm

**Where** `11_Miner.cs:135-141`, `13_Igc.cs:395`.

`StSelecting` sends `LR` whenever `!awaitingLease`; `OnLeaseDenied` clears
`awaitingLease` for `nojob` with no backoff. Every drone then sends a request and
receives a denial every tick — 6 Hz each — for as long as the dispatcher has no
job. The 60-tick backoff only applies while `awaitingLease` is set, so it never
engages on this path.

**Fix.** Treat `nojob` like the other denials: keep `awaitingLease` set and let the
existing timer run. **Confidence** Provable.

### F-2 — `PumpIgc` has no budget guard and lease requests do unbounded work

**Where** `13_Igc.cs:30-41`, `13_Igc.cs:350`.

Up to 36 messages are processed per tick, and each `LR` runs a full
`SelectNextCell` — a 25-cell neighbourhood plus a 48-cell window, each `ScoreCell`
iterating up to 64 sightings. That is thousands of vector operations per request,
before any of the tick's real work. `PumpIgc` is the one expensive loop that never
consults `BudgetTight()`.

**Fix.** Check the budget in the drain loop and defer the remainder to the next
tick; the queue is already bounded. **Confidence** Judgement — the common case is
one request per shaft, but F-1 makes the bad case reachable.

### F-3 — Grant and beacon can arrive out of order after growth

**Where** `09_Job.cs:483`, `13_Igc.cs:359`.

`GrowJobTowardOre` broadcasts the new frame and `OnLeaseRequest` then unicasts the
grant. Broadcast and unicast are separate queues with no ordering guarantee, so a
drone can apply a grant indexed against the new grid while still holding the old
one. The `activeCell < cells.Length` guard prevents a crash, not a wrong hole.

**Fix.** Carry a frame generation counter on the grant and have the drone reject a
grant whose generation it has not yet adopted. **Confidence** Provable that the
ordering is unguaranteed; **Judgement** on how often it bites.

### F-4 — `GrantLease`'s `depthLimit` is dead on arrival

**Where** `13_Igc.cs:367`, `11_Miner.cs:191`.

`OnLeaseGrant` stores it into `shaftDepthLimit`; `BeginShaft` unconditionally
overwrites it two lines later. The field costs wire bytes and creates the
impression the dispatcher controls shaft depth. Either honour it or drop it.
**Confidence** Provable.

### F-5 — `OnOreSighting` has no role check

**Where** `13_Igc.cs:457`. The one handler missing the guard every other one has.
Harmless today; it is the hole in an otherwise consistent pattern.
**Confidence** Provable.

---

# Phase 4 — Performance

### P-1 — Every IGC message forces a full sprite redraw

**Where** `05_Program.cs:51-57`, `16_Display.cs:60`.

`SetMessageCallback` causes `Main` to run with `UpdateType.IGC`, which fails the
update mask and takes the command-only path: `Render(true); return;`. The `force`
flag bypasses both rate limits, including `if (force || tick % 12 == 0)
RenderSprites()` — up to 280 map sprites plus fleet rows, rebuilt per message. A
dispatcher with six drones heartbeating at 2 Hz does that around twelve times a
second on top of its normal 2 Hz.

`force` was meant for operator commands. The IGC path should not inherit it.
**Confidence** Provable. Cheapest large win in the document.

### P-2 — `thrustByType` is recomputed every tick and never read

**Where** `07_Control.cs:93-113`. Zeroes the per-type matrices and performs a
`BlockDefinition.SubtypeId` lookup plus a dictionary hash per thruster, six times a
second. Nothing consumes the values — see **D-1**. **Confidence** Provable.

### P-3 — Allocation churn against a stated policy

`04_Fields.cs:234` says *"Allocating inside Main() is how SE scripts end up
stuttering."* Then `ExpireDrones` does `new List<long>()` every tick whenever the
fleet is non-empty (`14_Dispatcher.cs:63`), `Bar()` and `Pad()` allocate per call,
and `ScanBlocks` builds three throwaway lists per rescan. The dispatcher one is
the only per-tick offender and should become a reusable scratch list beside the
others. **Confidence** Provable.

---

# Phase 5 — Hygiene

### D-1 — Dead members

Verified unreachable across all modules:

| Item | Where | Note |
|---|---|---|
| `Waypoint.ThrusterEfficiency` | `02_Types.cs:24` | Written per waypoint, never read, not persisted |
| `SampleEfficiency()` | `10_Path.cs:131` | Exists only to fill the above |
| `thrusterTypes`, `thrustByType` | `04_Fields.cs:68,72` | Only consumers are the above and the per-tick accumulation in **P-2** |
| `Waypoint.InGravity` | `02_Types.cs:44` | Never referenced |
| `YieldCell.DepthReached` | `02_Types.cs:106` | Written, persisted, sent over IGC, never read — `BeginShaft` re-detects contact instead |
| `ParseDouble()` | `15_Util.cs:117` | Never called; operator input goes through `ParseInt` |
| `DroneRecord.Address` | `02_Types.cs:135` | Assigned in `DroneFor`, never read |
| `stateTicks++` | `14_Dispatcher.cs:15` | `Watchdog()` runs only from `TickMiner`; write-only on a dispatcher |
| `import re` | `tools/build.py:17` | Unused |

Removing the thruster-type machinery drops roughly forty lines, a per-tick
dictionary walk, and some of the minifier's rename pressure. If per-type
efficiency is wanted — the display could genuinely say *"atmospherics dead above
here"* — then wire it up. At present the cost is paid and the benefit is not
collected.

Also `lastScanTick = long.MinValue` (`04_Fields.cs:43`) overflows in
`tick - lastScanTick` on its first comparison. Harmless, because the constructor
calls `ScanBlocks()` first, but the sentinel does not do what it appears to.

### D-2 — Version string is behind the docs

`VEIN_VERSION` is `"1.1.0"` (`00_Header.cs:53`) while `CONFIG.md:149` says
*"`dockSlots` is enforced from 1.2 on"* — and the enforcement is in this build.
`record dock` and `job push` also landed after 1.1.0. The version string is what
an operator quotes in a bug report.

### D-3 — Undocumented commands

`status` and `scan` are implemented and appear nowhere in the docs, as do the
aliases `run`, `pause`, and `end`.

### D-4 — Document the destructive config write-back

`LoadConfig` writes its corrections back to Custom Data (`03_Config.cs:189-201`
and `294`), so an operator's `dockPatience = 90` is permanently rewritten to `60`
under `stateTimeout = 120`, and raising `stateTimeout` later will not restore it.
Defensible, but it should say so in `CONFIG.md`.

### D-5 — Read the docs against the code

`CONFIG.md`, `SCOUTING.md`, `SETUP.md` and `SPACE.md` — roughly 1,070 lines — have
not been checked against the implementation. `DESIGN.md` has, and it was accurate
apart from the resume behaviour in **C-4**, which suggests drift will be modest.

---

# Open questions

Not investigated. Recorded so the next pass does not have to rediscover that they
were skipped.

- **The state machine's transition graph.** All twelve `MinerState` values have a
  `case` arm. Which states can reach which, whether any is unreachable, and whether
  any has no exit under some condition — never enumerated. Mechanically checkable.
- **Braking-derate equilibrium.** `DESIGN.md:189` correctly notes that a ship
  tracking the profile perfectly sits at `derate²`. What it does not note is that
  the acceptance band is fixed at `[0.35, 0.60]`, so the converged value is pinned
  to `[√0.35, √0.60] ≈ [0.59, 0.77]` for *any* hull — which sits awkwardly with the
  premise that this measures a property of this ship and this server. Worth
  confirming it is intended.
- **Two VEIN Programmable Blocks on one construct.** Both would claim every
  thruster and fight over the overrides. `blockTag` is the only mitigation and
  nothing warns. Unmodelled.
- **The zero-gravity path end to end.** `CurrentLift()` returns 0 in space, so
  `maxFlyableMass` stays −1 and the lift ceiling never binds; `Orient`'s default up
  falls back to the controller's current up; `StoppingAccel`'s 0.15 floor is the
  only braking margin left. Pieces reasoned about, scenario never traced.
- **Control tuning.** `TAU = 0.45`, `KP = 3.0`, `KD = 0.55`, the rate clamps, the
  95 m/s command ceiling, the `0.004` travel weight in `ScoreCell`. Empirical and
  not checkable outside the game.
- **Storage size** against Space Engineers' ceiling for a 400-waypoint path plus a
  large survey. Never estimated.
- **Block ownership.** `Mine()` filters by construct and tag, not owner.
- **The two conflict-resolution merges.** `ed531e3 Resolve PR #1 conflicts in
  favour of the newer implementation` and the PR #3 merge. There are commits named
  *"Review pass: three logic bugs"* and *"Adversarial pass"*; nobody has checked
  whether those fixes survived the merges. Probably the highest-yield unchecked
  area after Phase 0.

---

# On the ordering

Phase 0 is first because the evidence says inspection has reached its limit here.
**C-3** is the proof: a coverage bug in a twenty-five-line function, read
carefully, understood incorrectly, and then found in ninety seconds by executing a
simulation of it. There is no reason to believe it was the only one of its kind,
and no amount of further reading is a reliable way to find the next.

The findings above are also confounded with where attention was spent. Docking,
fleet protocol and persistence were examined hardest and carry the most entries.
Sprite layout arithmetic, display formatting and the tuning constants carry few —
which is evidence about the review, not about the code.

So the ordering is not severity. It is: build the thing that finds bugs without a
human in the loop, then fix what is already known, then use the first to look
again.
