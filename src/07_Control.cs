// ============================================================================
//  FLIGHT CONTROL
//
//  Direct thrust and gyro override. We deliberately do not use the Remote
//  Control autopilot: it cannot be told "descend at exactly 1.2 m/s while
//  holding this attitude", it gives up in gravity, and it has no concept of a
//  drill jamming. Everything here is closed-loop on measured velocity.
// ============================================================================

/// <summary>
/// Fraction of the theoretical stopping speed we are actually willing to use.
///
/// sqrt(2·a·d) is exact for an ideal actuator and optimistic for a real one.
/// Thrusters ramp rather than snapping to full output, server tick rate varies,
/// ship mass changes while drilling, and the velocity controller has its own
/// response lag. All four eat into the distance available to stop in.
///
/// Two independently shipped miners agree this must be well under 1.0, and both
/// land lower than seemed necessary from first principles: PAM defaults to 0.70
/// and marks anything above 0.80 as risky in its own UI, while SCAM ships a
/// StoppingPowerQuotient of 0.50. Neither number is derived; both come from
/// watching real ships overshoot. VEIN sat at 0.75 — more aggressive than
/// either — purely because nothing had contradicted it yet.
///
/// This is now only the *starting point*. The ship measures how much of its
/// braking authority approaches genuinely demand and moves the figure to suit
/// itself — see 20_Adaptive.cs, which explains why shipping any fixed number
/// here was the wrong instinct in the first place.
/// </summary>
const double BRAKE_DERATE = 0.60;

/// <summary>Bucket every thruster by the ship-local direction it pushes.</summary>
void BuildThrustModel()
{
    for (int a = 0; a < 3; a++)
        for (int s = 0; s < 2; s++)
        {
            if (thrustBuckets[a, s] == null) thrustBuckets[a, s] = new List<IMyThrust>();
            else thrustBuckets[a, s].Clear();
        }

    if (controller == null) return;

    MatrixD inv = MatrixD.Transpose(controller.WorldMatrix.GetOrientation());

    for (int i = 0; i < thrusters.Count; i++)
    {
        IMyThrust t = thrusters[i];

        // A thruster's flame points Forward; the ship is shoved Backward.
        Vector3D push = Vector3D.TransformNormal(t.WorldMatrix.Backward, inv);

        // Snap to the dominant axis. Thrusters are always grid-aligned, so the
        // dominant component is ~1 and the others are float noise.
        int axis = 0;
        double best = Math.Abs(push.X);
        if (Math.Abs(push.Y) > best) { axis = 1; best = Math.Abs(push.Y); }
        if (Math.Abs(push.Z) > best) { axis = 2; best = Math.Abs(push.Z); }

        double component = axis == 0 ? push.X : (axis == 1 ? push.Y : push.Z);
        int sign = component >= 0 ? 0 : 1;
        thrustBuckets[axis, sign].Add(t);
    }

    RefreshThrustCapacity();
}

/// <summary>
/// Re-sum available thrust. Done every tick, not just on rescan, because
/// MaxEffectiveThrust is a live value — an atmospheric thruster loses its
/// authority as you climb, and a script that caches this at compile time will
/// happily fly you off the top of the atmosphere and then drop you.
/// </summary>
void RefreshThrustCapacity()
{
    for (int a = 0; a < 3; a++)
        for (int s = 0; s < 2; s++)
            thrustByAxis[a, s] = 0f;

    for (int a = 0; a < 3; a++)
        for (int s = 0; s < 2; s++)
        {
            List<IMyThrust> bucket = thrustBuckets[a, s];
            if (bucket == null) continue;
            for (int i = 0; i < bucket.Count; i++)
            {
                IMyThrust t = bucket[i];
                if (!t.IsFunctional || !t.Enabled) continue;
                thrustByAxis[a, s] += t.MaxEffectiveThrust;
            }
        }
}

/// <summary>
/// Largest force the ship can produce along a world direction.
/// A diagonal is limited by whichever axis saturates first, which is why this
/// is a min over axes rather than a sum.
/// </summary>
double ThrustAlong(Vector3D worldDir)
{
    if (controller == null) return 0;
    if (worldDir.LengthSquared() < 1e-9) return 0;
    worldDir = Vector3D.Normalize(worldDir);

    Vector3D local = Vector3D.TransformNormal(worldDir, MatrixD.Transpose(controller.WorldMatrix.GetOrientation()));
    double limit = double.MaxValue;

    for (int axis = 0; axis < 3; axis++)
    {
        double c = axis == 0 ? local.X : (axis == 1 ? local.Y : local.Z);
        if (Math.Abs(c) < 1e-4) continue;      // this axis contributes nothing
        float cap = thrustByAxis[axis, c >= 0 ? 0 : 1];
        if (cap <= 0) return 0;                // no thrust that way at all
        limit = Math.Min(limit, cap / Math.Abs(c));
    }

    return limit == double.MaxValue ? 0 : limit;
}

/// <summary>
/// Deceleration we can actually achieve while travelling along
/// <paramref name="dir"/>.
/// </summary>
double StoppingAccel(Vector3D dir)
{
    double raw = ThrustAlong(-dir) / Math.Max(1.0, shipMass);

    // Only the component of gravity along the direction of travel eats into
    // stopping power. This used to subtract the full magnitude whichever way the
    // ship was pointing, which is not pessimism, it is wrong: flying sideways,
    // gravity is perpendicular and costs nothing. A typical miner's lateral
    // thrust-to-mass is 3-5 m/s^2, so `raw - 9.81` went negative and every
    // horizontal move on a planet clamped to the 0.15 floor below — about 1 m/s
    // along a recorded route, which then timed the state out on the way home.
    //
    // One-sided on purpose. Gravity pulling the way we are already braking would
    // help, and help is not banked: climbing pays the cost, descending simply
    // does not get a discount.
    double along = Vector3D.Dot(gravity, dir);
    return Math.Max(0.15, raw - Math.Max(0.0, along));
}

/// <summary>Fly toward a point, arriving with zero velocity.</summary>
void FlyTo(Vector3D target, double maxSpeed)
{
    FlyTo(target, maxSpeed, 0.0);
}

/// <summary>
/// Fly toward a point, with <paramref name="runOut"/> metres of usable travel
/// beyond it before the ship actually has to be stopped.
///
/// Following a recorded route, the next waypoint is a corridor marker, not a
/// destination. Braking for it pinned route speed at sqrt(2*a*spacing) — about
/// 8 m/s on a 10 m recording interval — whatever cruiseSpeed was set to, and
/// cruiseSpeed was therefore unreachable by construction.
///
/// The aim point deliberately stays near. Aiming at a point far down the route
/// would let the ship cut the corner off the path a human flew to avoid a
/// mountain, which is the whole reason the route exists. Only the *speed* looks
/// further ahead.
/// </summary>
void FlyTo(Vector3D target, double maxSpeed, double runOut)
{
    flightActive = true;

    if (controller == null) return;

    Vector3D toTarget = target - controller.GetPosition();
    distToTarget = toTarget.Length();

    Vector3D dir = distToTarget > 1e-4 ? toTarget / distToTarget : Vector3D.Zero;

    // Speed we could still shed before arriving: v = sqrt(2 a d).
    double stopAccel = StoppingAccel(dir.LengthSquared() > 0 ? dir : Vector3D.Up);
    double brakeFrom = Math.Max(0.0, distToTarget + Math.Max(0.0, runOut));
    double arrivalSpeed = Math.Sqrt(2.0 * stopAccel * brakeFrom) * BrakeDerateNow();

    double want = Math.Min(maxSpeed, arrivalSpeed);

    // Closing speed along the approach, not total speed: lateral drift is the
    // controller's problem, not the stopping distance's. Sampled here because
    // this is the one place that knows both the geometry and the capability.
    // Only a genuine arrival teaches anything. A fly-through has run-out and so
    // is not arrival-limited, which is exactly the distinction the learner could
    // not previously draw.
    UpdateBrakeLearning(arrivalSpeed < maxSpeed && runOut <= 0.0, distToTarget,
                        Vector3D.Dot(shipVel, dir), stopAccel);

    // Do not travel fast while still swinging round. PAM does the same thing and
    // the reason is practical: the drills point along the ship's forward axis,
    // so a badly misaligned ship at speed is carrying its most fragile face
    // sideways into whatever it is approaching. Full speed by 20 degrees.
    if (alignError > 20.0) want *= Math.Max(0.15, 1.0 - (alignError - 20.0) / 70.0);

    // Never command more than the server will honour anyway. Vanilla tops out at
    // 100 m/s; a speed mod raises it, and cruiseSpeed accepts up to 300, so an
    // operator who has raised both should not be silently held at 95.
    want = Math.Min(want, Math.Max(95.0, cruiseSpeed));

    SetVelocity(dir * want);
}

/// <summary>
/// Drive the ship toward a target velocity. This is the bottom of the flight
/// stack — everything else eventually calls here.
/// </summary>
void SetVelocity(Vector3D desiredVelWorld)
{
    if (controller == null) return;

    // We own the thrusters while flying; the game's dampeners would fight us.
    if (controller.DampenersOverride) controller.DampenersOverride = false;

    Vector3D velError = desiredVelWorld - shipVel;

    // Correct the velocity error over a fixed time constant rather than over dt.
    // Using dt makes the gain depend on server tick rate, which is how scripts
    // that fly beautifully in single player oscillate themselves apart on a
    // busy dedicated server.
    const double TAU = 0.45;
    Vector3D desiredAccel = velError / TAU;

    // Hold station against gravity on top of whatever manoeuvre we wanted. The
    // cap goes on the total, because the total is what the thrusters are asked
    // for. Capping the manoeuvre alone and then adding gravity compensation on
    // top let the sum saturate, and ApplyAxis clamps the sum — which quietly
    // takes the shortfall out of holding the ship up.
    Vector3D total = desiredAccel - gravity;
    double cap = ThrustAlong(total) / Math.Max(1.0, shipMass);
    if (cap > 0 && total.Length() > cap) total = Vector3D.Normalize(total) * cap;

    ApplyForce(total * shipMass);
}

/// <summary>Distribute a world-space force across the thruster buckets.</summary>
void ApplyForce(Vector3D worldForce)
{
    if (controller == null) return;
    Vector3D local = Vector3D.TransformNormal(worldForce, MatrixD.Transpose(controller.WorldMatrix.GetOrientation()));
    ApplyAxis(0, local.X);
    ApplyAxis(1, local.Y);
    ApplyAxis(2, local.Z);
}

void ApplyAxis(int axis, double needed)
{
    int pos = needed >= 0 ? 0 : 1;
    int neg = 1 - pos;

    float cap = thrustByAxis[axis, pos];
    float ratio = cap > 1f ? (float)Clamp(Math.Abs(needed) / cap, 0.0, 1.0) : 0f;

    List<IMyThrust> on = thrustBuckets[axis, pos];
    if (on != null)
        for (int i = 0; i < on.Count; i++)
            if (on[i].IsFunctional) on[i].ThrustOverridePercentage = ratio;

    // The opposing bucket must be explicitly zeroed. Leaving a stale override
    // there means two banks fighting, which burns fuel and reads as "the ship
    // feels sluggish" long before anyone works out why.
    List<IMyThrust> off = thrustBuckets[axis, neg];
    if (off != null)
        for (int i = 0; i < off.Count; i++)
            if (off[i].IsFunctional) off[i].ThrustOverridePercentage = 0f;
}

// ---------------------------------------------------------------------------
//  ORIENTATION
// ---------------------------------------------------------------------------

/// <summary>
/// Point the ship's forward axis along <paramref name="desiredForward"/>, rolling
/// so its up axis is as close to <paramref name="desiredUp"/> as it can be.
/// Pass a zero forward to release the gyros.
/// </summary>
void Orient(Vector3D desiredForward, Vector3D desiredUp)
{
    if (controller == null || gyros.Count == 0) return;

    if (desiredForward.LengthSquared() < 1e-9)
    {
        ReleaseGyros();
        alignError = 0;
        return;
    }

    desiredForward = Vector3D.Normalize(desiredForward);

    // With no roll preference, prefer "up" away from gravity — it keeps the ship
    // the right way up so a pilot dropping in is not instantly disoriented.
    if (desiredUp.LengthSquared() < 1e-9)
        desiredUp = gravity.LengthSquared() > 1e-6 ? -Vector3D.Normalize(gravity) : controller.WorldMatrix.Up;

    // Gram-Schmidt: strip out any part of "up" that lies along forward, so the
    // two axes are genuinely perpendicular before we compare against them.
    desiredUp = desiredUp - desiredForward * Vector3D.Dot(desiredUp, desiredForward);
    if (desiredUp.LengthSquared() < 1e-9)
    {
        // Degenerate: caller asked for up parallel to forward. Pick anything.
        desiredUp = Math.Abs(desiredForward.Z) < 0.9
            ? Vector3D.Normalize(Vector3D.Cross(desiredForward, Vector3D.Forward))
            : Vector3D.Normalize(Vector3D.Cross(desiredForward, Vector3D.Right));
    }
    else desiredUp = Vector3D.Normalize(desiredUp);

    MatrixD m = controller.WorldMatrix;

    // Cross products give a rotation axis whose length is sin(error). Summing the
    // forward and up terms handles pitch/yaw and roll in one shot without ever
    // building a quaternion or hitting a gimbal singularity.
    Vector3D errAxis = Vector3D.Cross(m.Forward, desiredForward)
                     + Vector3D.Cross(m.Up, desiredUp);

    alignError = ToDegrees(Math.Acos(Clamp(Vector3D.Dot(m.Forward, desiredForward), -1.0, 1.0)));

    // A cross product has magnitude sin(angle), so it vanishes at 180 degrees
    // exactly as it does at zero. Pointed at precisely the opposite direction —
    // rejoining a recorded route that runs back past the ship, or a dock
    // approach that needs a reversal — both terms cancel, the command is zero,
    // and the ship sits there perfectly still at maximum error until the
    // watchdog gives up. Nudge it off the singularity with any perpendicular
    // axis; one tick later the normal control has a gradient to work with.
    if (errAxis.LengthSquared() < 1e-4 && alignError > 90.0)
    {
        Vector3D seed = Math.Abs(m.Forward.Z) < 0.9 ? Vector3D.Forward : Vector3D.Right;
        errAxis = Vector3D.Normalize(Vector3D.Cross(m.Forward, seed));
    }

    Vector3D angVel = controller.GetShipVelocities().AngularVelocity;

    // PD. The derivative term is what stops a big ship wallowing past the target
    // and oscillating; without it a heavy miner never settles enough to drill.
    const double KP = 3.0;
    const double KD = 0.55;
    Vector3D command = errAxis * KP - angVel * KD;

    // PAM separates these by a factor of three — 15 for small grids against 5
    // for large — where an earlier version of this used barely half that. A
    // large grid carries enormously more rotational inertia and will overshoot
    // and hunt on a gain that suits a small one.
    double maxRate = isLargeGrid ? 0.6 : 1.8;   // rad/s
    if (command.Length() > maxRate) command = Vector3D.Normalize(command) * maxRate;

    ApplyGyros(command);
}

/// <summary>Push an angular velocity command into every gyro, in its own frame.</summary>
void ApplyGyros(Vector3D worldCommand)
{
    for (int i = 0; i < gyros.Count; i++)
    {
        IMyGyro g = gyros[i];
        if (!g.IsFunctional) continue;

        // Each gyro can be mounted in any orientation. Rotating the command into
        // the gyro's own frame is what lets a randomly-placed gyro farm cooperate
        // instead of cancelling out.
        Vector3D local = Vector3D.TransformNormal(worldCommand, MatrixD.Transpose(g.WorldMatrix));

        g.GyroOverride = true;
        g.Pitch = (float)(-local.X);
        g.Yaw   = (float)(-local.Y);
        g.Roll  = (float)(-local.Z);
    }
}

void ReleaseGyros()
{
    for (int i = 0; i < gyros.Count; i++)
    {
        IMyGyro g = gyros[i];
        if (!g.GyroOverride) continue;
        g.GyroOverride = false;
        g.Pitch = 0f; g.Yaw = 0f; g.Roll = 0f;
    }
}

// ---------------------------------------------------------------------------
//  SAFING
// ---------------------------------------------------------------------------

/// <summary>
/// Hand the ship back to the game in a state a human would consider safe:
/// drills off, no thrust overrides, gyros released, dampeners on.
/// Called on fault, on stop, and on dock.
/// </summary>
void SafeStop()
{
    flightActive = false;

    for (int a = 0; a < 3; a++)
        for (int s = 0; s < 2; s++)
        {
            List<IMyThrust> bucket = thrustBuckets[a, s];
            if (bucket == null) continue;
            for (int i = 0; i < bucket.Count; i++) bucket[i].ThrustOverridePercentage = 0f;
        }

    ReleaseGyros();
    SetDrills(false);

    // Dampeners back on last. If the ship is moving, this is what actually
    // arrests it, and we want the overrides already cleared when it does.
    if (controller != null) controller.DampenersOverride = true;
}

void SetDrills(bool on)
{
    for (int i = 0; i < drills.Count; i++)
        if (drills[i].Enabled != on) drills[i].Enabled = on;
}

/// <summary>
/// Switch the thrusters on or off as blocks.
///
/// Note this is Enabled, not the override — a disabled thruster contributes
/// nothing to <see cref="RefreshThrustCapacity"/>, so the flight controller
/// computes zero available thrust and the ship simply does not move. Always
/// call this with true before attempting to fly.
/// </summary>
void SetThrusters(bool on)
{
    for (int i = 0; i < thrusters.Count; i++)
        if (thrusters[i].Enabled != on) thrusters[i].Enabled = on;
}

// ---------------------------------------------------------------------------
//  SAMPLING
// ---------------------------------------------------------------------------

/// <summary>Refresh everything the control loop reads. Called once per tick.</summary>
void SampleShip()
{
    if (controller == null) return;

    shipPos = controller.GetPosition();
    MyShipVelocities v = controller.GetShipVelocities();
    shipVel = v.LinearVelocity;
    speed = shipVel.Length();
    gravity = controller.GetNaturalGravity();
    shipMass = Math.Max(1.0, controller.CalculateShipMass().PhysicalMass);

    RefreshThrustCapacity();
    SampleInventories();
    // The expensive half, at roughly 1 Hz.
    if (tick % 6 == 0) SampleOre();
}

/// <summary>World position of the drill cutting face — the point that is
/// actually at the bottom of the shaft, which is not where the controller is.</summary>
Vector3D DrillFace()
{
    if (controller == null) return shipPos;
    MatrixD m = controller.WorldMatrix;
    return shipPos
         + m.Right * drillOffset.X
         + m.Up * drillOffset.Y
         + m.Backward * drillOffset.Z;
}
