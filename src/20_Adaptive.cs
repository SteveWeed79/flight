// ============================================================================
//  SELF-TUNING
//
//  PAM and SCAM both ship hand-tuned constants that no derivation produces —
//  0.6 m/s drilling, a 0.5 to 0.7 braking derate — arrived at by watching real
//  ships fail. Every value VEIN derived from first principles was wrong in the
//  same direction: too fast, too confident, too early to commit.
//
//  That direction is not an accident. Every failure mode in this game is a
//  variant of "moved too fast": jamming a drill, overshooting a waypoint,
//  wedging in a shaft, latching a connector while still drifting. There is no
//  failure mode called "moved too slowly". So rather than ship another guess,
//  the ship measures itself.
//
//  ---------------------------------------------------------------------------
//  THE SAFETY RULE, which everything here obeys:
//
//      Adaptation may only ever make the ship MORE cautious than configured.
//      Never less.
//
//  Your configured value is a ceiling, not a target. A loop that can talk itself
//  into going faster is a loop that can talk itself into a crater, and an
//  adaptive controller wrapped around an untested one has no business being
//  optimistic. The worst case here is a ship that mines slowly.
//  ---------------------------------------------------------------------------
// ============================================================================

/// <summary>Recompute the live values. Cheap; called once per tick.</summary>
void UpdateAdaptive()
{
    // Configured values are ceilings. If the operator lowers one below what the
    // ship had learned, respect it immediately.
    if (learnedDrillSpeed <= 0 || learnedDrillSpeed > drillSpeed) learnedDrillSpeed = drillSpeed;
    if (learnedBrakeDerate <= 0 || learnedBrakeDerate > BRAKE_DERATE) learnedBrakeDerate = BRAKE_DERATE;
}

// ---------------------------------------------------------------------------
//  DRILL SPEED
// ---------------------------------------------------------------------------

/// <summary>
/// The drills jammed. Back off hard and stay backed off for a while.
///
/// Asymmetric on purpose: a stall costs a back-out, a retry, and sometimes a
/// blacklisted cell, whereas cutting slightly slower than optimal costs a few
/// seconds. Punish stalls sharply and recover gently.
/// </summary>
void OnDrillStall()
{
    learnedDrillSpeed = Math.Max(DRILL_SPEED_FLOOR, learnedDrillSpeed * 0.7);
    cleanCutTicks = 0;
    drillStalls++;
    Log("Drill speed -> " + Fmt(learnedDrillSpeed, 2) + " m/s after stall");
}

/// <summary>
/// Called each tick while actually cutting rock and making progress. Recovers
/// toward the configured ceiling slowly — roughly a percent per five seconds of
/// clean cutting, so it takes a sustained good run to undo one stall.
/// </summary>
void OnCleanCut()
{
    cleanCutTicks++;
    if (cleanCutTicks < 30) return;          // ~5 s at Update10
    cleanCutTicks = 0;

    if (learnedDrillSpeed >= drillSpeed) return;
    learnedDrillSpeed = Math.Min(drillSpeed, learnedDrillSpeed * 1.03);
}

const double DRILL_SPEED_FLOOR = 0.15;

// ---------------------------------------------------------------------------
//  BRAKING
// ---------------------------------------------------------------------------

/// <summary>
/// Watch an approach for overshoot, and tighten the braking derate if we find
/// any.
///
/// Overshoot is the honest signal: the controller predicted it could stop in the
/// remaining distance and it could not. Causes vary — thruster spool-up, server
/// tick rate, mass changing mid-approach — and none are worth modelling
/// separately when the outcome is directly observable.
///
/// This only ever tightens. Nothing here can decide the ship may brake later.
/// </summary>
void TrackBraking()
{
    if (!flightActive || controller == null) { brakeWatchArmed = false; return; }

    // Only interested in approaches we are actually trying to stop at, and only
    // once moving fast enough for an overshoot to mean anything.
    if (distToTarget > 200.0 || speed < 3.0)
    {
        if (brakeWatchArmed && distToTarget > brakeClosest + 8.0) brakeWatchArmed = false;
        return;
    }

    if (!brakeWatchArmed)
    {
        brakeWatchArmed = true;
        brakeClosest = distToTarget;
        return;
    }

    if (distToTarget < brakeClosest) { brakeClosest = distToTarget; return; }

    // Distance is growing again. If it has grown appreciably while we were still
    // carrying speed, we sailed past the mark.
    double overshoot = distToTarget - brakeClosest;
    if (overshoot < Math.Max(3.0, shipRadius * 0.5)) return;

    brakeWatchArmed = false;
    brakeOvershoots++;
    learnedBrakeDerate = Math.Max(BRAKE_DERATE_FLOOR, learnedBrakeDerate * 0.9);
    Log("Overshot by " + Fmt(overshoot, 1) + "m — braking derate -> "
        + Fmt(learnedBrakeDerate, 2));
}

const double BRAKE_DERATE_FLOOR = 0.25;

/// <summary>One-line summary for the displays. Adaptation you cannot see is
/// indistinguishable from a bug, which is much of why SCAM reads as fiddly.</summary>
string AdaptiveStatus()
{
    if (drillStalls == 0 && brakeOvershoots == 0) return "nominal";
    return "drill " + Fmt(learnedDrillSpeed, 2) + " (" + drillStalls + " stalls), brake "
         + Fmt(learnedBrakeDerate, 2) + " (" + brakeOvershoots + " over)";
}
