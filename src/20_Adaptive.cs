// ============================================================================
//  MEASURED CONSTANTS
//
//  Two numbers decide whether a miner works: how fast it may cut, and how much
//  of its theoretical braking authority it may spend. Both ancestors ship a
//  hand-picked figure for each, and the figures disagree — SCAM cuts at 0.6 m/s
//  and derates braking to 0.50; PAM warns above ~2 m/s and derates to 0.70. None
//  of them is derived. They were found by watching ships.
//
//  Which is the argument for shipping no number at all. Cutting speed is a
//  property of *this hull* — its drill count, its mass, the thrust it can bring
//  to bear on rock. Braking derate is a property of this hull *and* this
//  server's tick rate, because what the derate is really paying for is the lag
//  between asking for thrust and getting it. Both are observable at run time,
//  and neither ancestor observes either.
//
//  The one rule, because adaptation you cannot see is indistinguishable from a
//  bug: every learned value is displayed, is bounded by the configured value it
//  started from, is persisted as the hull property it is, and can be switched
//  off in one line of Custom Data.
// ============================================================================

// ---- Cutting speed --------------------------------------------------------

/// <summary>Cutting speed the ship has settled on, m/s. Seeded from config.</summary>
double learnedDrillSpeed = -1;
/// <summary>Smoothed downward progress while cutting, m/s. The measurement.</summary>
double drillRate;
/// <summary>Depth at the previous learning sample.</summary>
double drillRateRefDepth;
/// <summary>Consecutive ticks of clean cutting, i.e. keeping up with the command.</summary>
int drillCleanTicks;
/// <summary>Tick of the last back-off, so they cannot compound faster than the
/// measurement they are based on.</summary>
long drillBackoffTick;
/// <summary>The configured derate we last seeded from, so an operator editing it
/// and reloading is not silently ignored once a sample has been taken.</summary>
double seededBrakeDerate = -1;

/// <summary>The speed to actually descend at. One place, so the state machine
/// never has to know whether adaptation is on.</summary>
double DrillSpeedNow()
{
    if (!adaptiveDrill) return drillSpeed;
    if (learnedDrillSpeed <= 0) learnedDrillSpeed = drillSpeed;
    // Clamped on the way out, not only where it is adjusted. An operator who has
    // just halved drillSpeed in Custom Data has said something about this ship,
    // and a value learned under the old setting must not outrank it.
    return Clamp(learnedDrillSpeed, DrillSpeedFloor, DrillSpeedCap);
}

double DrillSpeedFloor { get { return Math.Max(0.05, drillSpeed * 0.25); } }
double DrillSpeedCap { get { return drillSpeed * 2.5; } }

/// <summary>
/// Watch the shaft go down and adjust the cutting speed to match.
///
/// The signal is progress against command: if the ship is told to descend at
/// 1.2 m/s and the hole is deepening at 1.1, the drills are keeping up and there
/// is headroom. If it is deepening at 0.3, the ship is riding on rock it has not
/// cut yet, which is the state that ends with a wedged miner.
///
/// Asymmetric on purpose. Backing off is immediate and large; creeping up is
/// slow and small. Every failure mode in this game is a variant of moved too
/// fast, and there is no failure mode called moved too slowly.
/// </summary>
void UpdateDrillLearning(bool cutting, double depthNow, double commanded)
{
    if (!adaptiveDrill) return;
    if (learnedDrillSpeed <= 0) learnedDrillSpeed = drillSpeed;

    if (!cutting)
    {
        drillRateRefDepth = depthNow;
        drillCleanTicks = 0;
        return;
    }

    double advance = depthNow - drillRateRefDepth;
    drillRateRefDepth = depthNow;

    // Backing off after a stall, or the depth datum moved. Nothing to learn.
    if (advance < 0 || dt <= 0) { drillCleanTicks = 0; return; }

    double rate = advance / dt;
    // Seed on first contact rather than filtering up from zero. Starting at zero
    // meant the first second of every shaft looked like a total stall and cost
    // three back-offs — 28% of the cutting speed — before the filter had caught
    // up with a ship that was cutting perfectly well.
    drillRate = drillRate > 0 ? drillRate * 0.85 + rate * 0.15 : rate;

    if (commanded < 0.05) return;
    double fraction = drillRate / commanded;

    if (fraction < 0.35)
    {
        // Not cutting anything like as fast as we asked. Back off hard — but no
        // more than once per second, because the filter feeding this decision has
        // a time constant of about that. Compounding 0.85 every tick at 6 Hz
        // drove the speed to its floor in under three seconds on evidence the
        // measurement had not finished gathering.
        if (tick - drillBackoffTick > 6)
        {
            drillBackoffTick = tick;
            learnedDrillSpeed = Math.Max(DrillSpeedFloor, learnedDrillSpeed * 0.85);
        }
        drillCleanTicks = 0;
        return;
    }

    if (fraction < 0.75) { drillCleanTicks = 0; return; }

    // Cutting cleanly. Roughly two seconds of it buys one per cent more speed.
    drillCleanTicks++;
    if (drillCleanTicks < 12) return;
    drillCleanTicks = 0;
    learnedDrillSpeed = Math.Min(DrillSpeedCap, learnedDrillSpeed * 1.01);
}

/// <summary>A shaft actually jammed. Much stronger evidence than a slow tick.</summary>
void PenaliseDrillSpeed()
{
    if (!adaptiveDrill) return;
    if (learnedDrillSpeed <= 0) learnedDrillSpeed = drillSpeed;
    learnedDrillSpeed = Math.Max(DrillSpeedFloor, learnedDrillSpeed * 0.7);
    drillCleanTicks = 0;
}

// ---- Braking derate -------------------------------------------------------

/// <summary>Fraction of the theoretical stopping speed we are willing to use.
/// Seeded from config, then measured. See <see cref="BRAKE_DERATE"/>.</summary>
double brakeDerate = BRAKE_DERATE;
/// <summary>True while the current FlyTo is arrival-limited rather than speed-limited.</summary>
bool brakeActive;
/// <summary>Worst fraction of available deceleration demanded during this approach.</summary>
double brakePeakDemand;
/// <summary>Approaches measured. Display only, but it is the difference between
/// "this is the shipped default" and "this is what your ship does".</summary>
int brakeSamples;

double BrakeDerateNow()
{
    return adaptiveBraking ? brakeDerate : configBrakeDerate;
}

/// <summary>
/// Learn the derate from how close the ship came to running out of brakes.
///
/// The physical question at every moment of an approach is: what fraction of the
/// deceleration I own would I need, right now, to stop exactly on the target?
/// That is v² / (2·d·a), and it is the number that goes above 1.0 immediately
/// before an overshoot.
///
/// A ship tracking the commanded profile perfectly sits at derate², because the
/// profile *is* derate·sqrt(2ad). Every real effect the derate exists to cover —
/// thrusters ramping, a laggy tick, mass climbing while the hold fills — shows up
/// as demand above derate². So: measure the peak demand over each approach, and
/// move the derate until the peak lands in a band that leaves useful margin.
/// Slow ship on a busy server converges low; crisp small grid in single player
/// converges high. Neither needs a human to pick a number.
/// </summary>
void UpdateBrakeLearning(bool braking, double distance, double along, double capability)
{
    if (!adaptiveBraking) return;

    if (braking && along > 3.0 && distance > 2.0 && capability > 0.2)
    {
        brakeActive = true;
        double demand = (along * along) / (2.0 * distance * capability);
        if (demand > brakePeakDemand) brakePeakDemand = demand;
        return;
    }

    // The approach ended — arrived, target changed, or we slowed below the speed
    // where any of this matters. Judge it and reset.
    if (!brakeActive) return;
    brakeActive = false;

    double peak = brakePeakDemand;
    brakePeakDemand = 0;
    if (peak <= 0.01) return;

    brakeSamples++;

    // Above 0.60 the ship was spending most of its braking authority to hold the
    // profile, which is where an overshoot comes from. Below 0.35 it barely
    // needed the brakes at all and is being made to crawl for nothing.
    if (peak > 0.60) brakeDerate *= 0.95;
    else if (peak < 0.35) brakeDerate *= 1.02;

    brakeDerate = Clamp(brakeDerate, 0.30, 0.85);
}

/// <summary>Forget both measurements and start from the configured values.
/// Wanted after a refit: a ship that has just gained ten drills and two hydrogen
/// tanks is not the ship that took these numbers.</summary>
void ResetLearning()
{
    learnedDrillSpeed = drillSpeed;
    drillRate = 0;
    drillCleanTicks = 0;
    brakeDerate = configBrakeDerate;
    brakePeakDemand = 0;
    brakeActive = false;
    brakeSamples = 0;
    Log("Learned values reset to config");
}

/// <summary>One line of what the ship has worked out about itself.</summary>
string LearningSummary()
{
    if (!adaptiveDrill && !adaptiveBraking) return "";
    string s = "";
    if (adaptiveDrill) s += "cut " + Fmt(DrillSpeedNow(), 2) + "m/s";
    if (adaptiveBraking)
    {
        if (s.Length > 0) s += "  ";
        s += "brake " + Fmt(brakeDerate, 2);
        s += brakeSamples > 0 ? " (" + brakeSamples + ")" : " (seed)";
    }
    return s;
}
