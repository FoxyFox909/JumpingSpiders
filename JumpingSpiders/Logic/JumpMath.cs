using System;

namespace JumpingSpiders;

/// <summary>
/// The ballistic model of a spider jump, and the solver that turns "I want to land there" into a launch velocity.
///
/// How the game moves a creature (EntityBehaviorControlledPhysics, read from the 1.22.7 source):
/// - Physics runs in fixed ticks of 1/30 second.
/// - Speeds are stored as "motion": blocks per 1/60 second. So 1 block per second is a motion of 1/60,
///   and in one physics tick the position moves by motion * 2.
/// - In every tick, in this order: gravity lowers motion.Y by (0.37 + extra) / 30, where extra = 0.015 * |motion.Y|
///   while falling; then air drag multiplies all of motion by 0.983 ^ (33 / 30); then the position moves by motion * 2
///   (vertical part first, then horizontal, each stopped by blocks).
///
/// The vertical part of a jump is pure physics: JumpController sets motion.Y once at take-off and the engine does
/// the rest. The horizontal part is held constant by JumpController, which re-writes the horizontal motion every
/// AI tick (ComputeSteering; the engine's ground friction would otherwise eat most of it right after take-off).
/// So in this model the spider moves sideways at a constant speed, and up and down exactly like the engine does.
///
/// Checked on a real 1.22.7 server (a vanilla hare launched through JumpController, physics logged per tick):
/// the height matched this model to 0.00001 blocks on every tick, and the spider stayed within 0.0001 blocks of
/// the planned arc and landed on the planned tick. The model assumes default physics: the entity JSON must not
/// change gravityFactor, airDragFallingFactor or groundDragFactor (attributes.physics).
///
/// Everything here is pure C# so the unit tests can check it without the game.
/// </summary>
public static class JumpMath
{
    // ---- Engine constants (Vintage Story 1.22.7). These are not design knobs: they must match the game. ----

    /// <summary>Length of one server physics tick, seconds (PhysicsManager: tickInterval = 1/30).</summary>
    public const double PhysicsTickSeconds = 1.0 / 30.0;

    /// <summary>Motion is measured in blocks per 1/60 s, so speed in blocks per second = motion * 60.</summary>
    public const double BlocksPerSecondPerMotionUnit = 60.0;

    /// <summary>Gravity, in motion units per second (GlobalConstants.GravityPerSecond).</summary>
    public const double GravityPerSecond = 0.37;

    /// <summary>Extra gravity while falling, as a fraction of the downward motion (PModuleGravity: 0.015 * -motion.Y).</summary>
    public const double FallingExtraGravityFactor = 0.015;

    /// <summary>Air drag base (GlobalConstants.AirDragAlways): motion is multiplied by AirDrag ^ (seconds * 33).</summary>
    public const double AirDrag = 0.983;

    /// <summary>The exponent rate used with AirDrag, per second (PModuleMotionDrag: dt * 33).</summary>
    public const double AirDragRatePerSecond = 33.0;

    /// <summary>How far one tick moves the position, per unit of motion (dt * 60 = 2 for a 1/30 s tick).</summary>
    public const double PositionPerMotionPerTick = PhysicsTickSeconds * BlocksPerSecondPerMotionUnit;

    /// <summary>The factor air drag applies to the motion in one physics tick (about 0.9813).</summary>
    public static readonly double DragPerTick = Math.Pow(AirDrag, PhysicsTickSeconds * AirDragRatePerSecond);

    // ---- Solver settings. These only control the search, not what the spider can do. ----

    /// <summary>When the preferred angle cannot reach the target, try angles this many degrees apart.</summary>
    public const double AngleSearchStepDeg = 5.0;

    /// <summary>How many halvings the speed search does. 50 is far more precise than needed and still cheap.</summary>
    public const int SpeedSearchIterations = 50;

    /// <summary>JumpController makes up a lag behind the planned arc over this many physics ticks (see ComputeSteering).</summary>
    public const int SteeringCatchUpTicks = 2;

    /// <summary>
    /// For how many physics ticks after take-off the engine still applies ground friction: PModuleOnGround keeps
    /// working for 0.15 s of "coyote time" after an entity leaves the ground, which is 5 ticks of 1/30 s.
    /// </summary>
    public const int GroundFrictionTicksAfterTakeOff = 5;

    /// <summary>
    /// What that ground friction leaves of the horizontal motion in one physics tick: two 1/60 s sub-steps of
    /// PModuleOnGround's 0.7 each. Measured on the 1.22.7 server: exactly 0.49 on each of the 5 ticks.
    /// </summary>
    public const double GroundFrictionPerTick = 0.49;

    /// <summary>A jump shorter than this sideways (blocks) is treated as "straight up", which the spider cannot do.</summary>
    public const double MinHorizontalDistance = 0.05;

    /// <summary>Slack (blocks or blocks per second) when comparing against limits, against rounding errors.</summary>
    public const double LimitTolerance = 1e-6;

    /// <summary>
    /// One physics tick of vertical motion: gravity, then air drag, exactly as PModuleGravity and PModuleMotionDrag do it.
    /// Takes and returns motion units (blocks per 1/60 s).
    /// </summary>
    public static double StepVerticalMotion(double motionY)
    {
        double extra = 0;
        if (motionY < 0)
        {
            extra = -FallingExtraGravityFactor * motionY;
        }
        double result = motionY - (GravityPerSecond + extra) * PhysicsTickSeconds;
        return result * DragPerTick;
    }

    /// <summary>
    /// The horizontal motion value that moves the spider the given distance (blocks) in one physics tick.
    /// Air drag is applied to the motion before the move, so this is a little more than distance / 2.
    /// JumpController writes this every AI tick to keep the spider on the planned arc.
    /// </summary>
    public static double HorizontalMotionForOneTick(double distance)
    {
        return distance / (PositionPerMotionPerTick * DragPerTick);
    }

    /// <summary>
    /// How many physics ticks have passed since take-off, judged from the spider's current vertical motion
    /// (motion units). Gravity lowers the vertical motion by a fixed pattern every tick, so it works as a clock
    /// that is always in step with the physics, however the AI ticks and physics ticks interleave.
    /// Returns a value from 0 to maxTicks.
    /// </summary>
    public static int EstimateTicksSinceLaunch(JumpStep step, double motionY, int maxTicks)
    {
        double m = step.VY / BlocksPerSecondPerMotionUnit;
        if (motionY >= m)
        {
            return 0;
        }
        for (int tick = 1; tick <= maxTicks; tick++)
        {
            double previous = m;
            m = StepVerticalMotion(m);
            if (m <= motionY)
            {
                // motionY lies between tick-1 and tick: take the nearer one.
                if (previous - motionY < motionY - m)
                {
                    return tick - 1;
                }
                return tick;
            }
        }
        return maxTicks;
    }

    /// <summary>
    /// The steering rule of JumpController: the horizontal motion to write now so that the spider follows the
    /// planned arc. The vertical motion serves as the clock (see EstimateTicksSinceLaunch), so the rule always knows
    /// where on the arc the spider should be, however the AI ticks and physics ticks interleave.
    ///
    /// Each tick the spider gets the planned per-tick distance, plus a share of how far it lags behind the plan.
    /// The game's ground friction keeps acting for 0.15 s after take-off ("coyote time", GroundFrictionTicksAfterTakeOff
    /// ticks); during those ticks the command is raised to make up for it (GroundFrictionPerTick) and any lag is made
    /// up at once. Afterwards a lag is made up over SteeringCatchUpTicks ticks (or fewer, near the landing), because a busy server can run two
    /// physics ticks with the same command, and catching up fully in one tick would then overshoot and oscillate. After the planned landing tick the rule holds the spider
    /// above the landing point. maxSpeed (blocks per second) caps the catching up.
    /// </summary>
    public static void ComputeSteering(JumpStep step, double currentX, double currentZ, double currentMotionY, double maxSpeed,
        out double motionX, out double motionZ)
    {
        int ticksDone = EstimateTicksSinceLaunch(step, currentMotionY, step.FlightTicks);

        double dx;
        double dz;
        double planX;
        double planY;
        double planZ;
        if (ticksDone >= step.FlightTicks)
        {
            dx = step.ToX - currentX;
            dz = step.ToZ - currentZ;
        }
        else
        {
            PositionAt(step, ticksDone * PhysicsTickSeconds, out planX, out planY, out planZ);
            double lagX = planX - currentX;
            double lagZ = planZ - currentZ;
            // While the ground friction still brakes the spider, make up the lag at once, or it keeps growing.
            int catchUpTicks = SteeringCatchUpTicks;
            if (ticksDone < GroundFrictionTicksAfterTakeOff)
            {
                catchUpTicks = 1;
            }
            catchUpTicks = Math.Min(catchUpTicks, step.FlightTicks - ticksDone);
            dx = step.VX * PhysicsTickSeconds + lagX / catchUpTicks;
            dz = step.VZ * PhysicsTickSeconds + lagZ / catchUpTicks;
        }

        double length = Math.Sqrt(dx * dx + dz * dz);
        double maxPerTick = maxSpeed * PhysicsTickSeconds;
        if (length > maxPerTick && length > 0)
        {
            dx = dx * maxPerTick / length;
            dz = dz * maxPerTick / length;
        }

        // The next tick still gets the ground friction: ask for more so that what is left is what we want.
        if (ticksDone < GroundFrictionTicksAfterTakeOff)
        {
            dx = dx / GroundFrictionPerTick;
            dz = dz / GroundFrictionPerTick;
        }
        motionX = HorizontalMotionForOneTick(dx);
        motionZ = HorizontalMotionForOneTick(dz);
    }

    /// <summary>The number of physics ticks a flight of the given length may take at most.</summary>
    public static int MaxTicksFor(double seconds)
    {
        return (int)Math.Ceiling(seconds / PhysicsTickSeconds - LimitTolerance);
    }

    /// <summary>
    /// Finds a launch velocity that takes the spider from its feet position to a point dx, dy, dz away
    /// (blocks; dy positive = up). Fills result.VX, VY, VZ, FlightTicks and FlightSeconds; the caller sets
    /// the From and To positions (or use CreateJump, which does both).
    /// Returns false when the target is out of reach: beyond MaxJumpUp, MaxJumpDown or MaxJumpHorizontal,
    /// too fast for MaxJumpSpeed at every allowed angle, longer than MaxFlightSeconds, or straight up.
    ///
    /// How it works: it first tries the preferred launch angle, then angles further and further away from it,
    /// within MinJumpAngleDeg..MaxJumpAngleDeg. For one angle it searches the launch speed at which the arc comes
    /// down through the target height exactly at the target distance. The landing then happens during some physics
    /// tick N; the horizontal speed is finally set so that the spider is exactly above the target at the end of tick N.
    /// </summary>
    public static bool SolveLaunch(double dx, double dy, double dz, JumpLimits limits, JumpStep result)
    {
        int count = AngleCandidateCount(limits);
        for (int i = 0; i < count; i++)
        {
            if (SolveLaunchAtAngle(dx, dy, dz, AngleCandidate(limits, i), limits, result))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// How many launch angles SolveLaunch tries. Angle number 0 is the preferred angle, then they move away from
    /// it in AngleSearchStepDeg steps, alternating steeper and flatter. JumpPlanner walks the same list so that
    /// it can try a steeper arc when the first one would hit a block.
    /// </summary>
    public static int AngleCandidateCount(JumpLimits limits)
    {
        double preferred = PreferredAngle(limits);
        double largestOffset = Math.Max(preferred - limits.MinJumpAngleDeg, limits.MaxJumpAngleDeg - preferred);
        int offsetSteps = (int)Math.Ceiling(largestOffset / AngleSearchStepDeg - LimitTolerance);
        return 1 + 2 * offsetSteps;
    }

    /// <summary>
    /// Launch angle number index (degrees): preferred, preferred + 5, preferred - 5, preferred + 10, ...
    /// Angles outside MinJumpAngleDeg..MaxJumpAngleDeg are returned as they are; SolveLaunchAtAngle rejects them.
    /// </summary>
    public static double AngleCandidate(JumpLimits limits, int index)
    {
        double preferred = PreferredAngle(limits);
        if (index == 0)
        {
            return preferred;
        }
        int offsetSteps = (index + 1) / 2;
        double offset = offsetSteps * AngleSearchStepDeg;
        if (index % 2 == 1)
        {
            return preferred + offset;
        }
        return preferred - offset;
    }

    /// <summary>
    /// Like SolveLaunch, but for one fixed launch angle (degrees above horizontal). The final launch is a little
    /// steeper than this angle, because the horizontal speed is rounded down to land at the end of a whole tick.
    /// </summary>
    public static bool SolveLaunchAtAngle(double dx, double dy, double dz, double angleDeg, JumpLimits limits, JumpStep result)
    {
        double distance = Math.Sqrt(dx * dx + dz * dz);
        if (distance < MinHorizontalDistance)
        {
            return false;
        }
        if (distance > limits.MaxJumpHorizontal + LimitTolerance)
        {
            return false;
        }
        if (dy > limits.MaxJumpUp + LimitTolerance)
        {
            return false;
        }
        if (-dy > limits.MaxJumpDown + LimitTolerance)
        {
            return false;
        }
        int maxTicks = MaxTicksFor(limits.MaxFlightSeconds);
        return TrySolveAtAngle(angleDeg, distance, dy, dx, dz, limits, maxTicks, result);
    }

    /// <summary>
    /// Convenience wrapper around SolveLaunch: a complete JumpStep between two feet positions, or null when out of reach.
    /// </summary>
    public static JumpStep CreateJump(double fromX, double fromY, double fromZ, double toX, double toY, double toZ, JumpLimits limits)
    {
        JumpStep step = new JumpStep();
        if (!SolveLaunch(toX - fromX, toY - fromY, toZ - fromZ, limits, step))
        {
            return null;
        }
        step.FromX = fromX;
        step.FromY = fromY;
        step.FromZ = fromZ;
        step.ToX = toX;
        step.ToY = toY;
        step.ToZ = toZ;
        return step;
    }

    /// <summary>
    /// Feet position of the spider t seconds after take-off, following the model. Between physics ticks the
    /// position is interpolated in a straight line, as the engine moves it in one go per tick. During the last
    /// tick the spider does not go below the landing height (the landing surface stops it).
    /// </summary>
    public static void PositionAt(JumpStep step, double t, out double x, out double y, out double z)
    {
        if (t <= 0)
        {
            x = step.FromX;
            y = step.FromY;
            z = step.FromZ;
            return;
        }
        if (t >= step.FlightSeconds)
        {
            x = step.ToX;
            y = step.ToY;
            z = step.ToZ;
            return;
        }

        double ticks = t / PhysicsTickSeconds;
        int wholeTicks = (int)Math.Floor(ticks);
        double fraction = ticks - wholeTicks;

        // Run the vertical tick loop up to the tick after t.
        double m = step.VY / BlocksPerSecondPerMotionUnit;
        double yBefore = 0;
        for (int tick = 1; tick <= wholeTicks; tick++)
        {
            m = StepVerticalMotion(m);
            yBefore = yBefore + m * PositionPerMotionPerTick;
        }
        m = StepVerticalMotion(m);
        double yAfter = yBefore + m * PositionPerMotionPerTick;
        double relativeY = yBefore + (yAfter - yBefore) * fraction;

        double landingRelativeY = step.ToY - step.FromY;
        if (wholeTicks + 1 >= step.FlightTicks && relativeY < landingRelativeY)
        {
            relativeY = landingRelativeY;
        }

        x = step.FromX + step.VX * t;
        y = step.FromY + relativeY;
        z = step.FromZ + step.VZ * t;
    }

    // One launch angle: search the speed, then make the landing tick land exactly on the target.
    private static bool TrySolveAtAngle(double angleDeg, double distance, double dy, double dx, double dz,
        JumpLimits limits, int maxTicks, JumpStep result)
    {
        if (angleDeg < limits.MinJumpAngleDeg - LimitTolerance || angleDeg > limits.MaxJumpAngleDeg + LimitTolerance)
        {
            return false;
        }

        double angle = angleDeg * Math.PI / 180.0;
        double cos = Math.Cos(angle);
        double sin = Math.Sin(angle);

        // If even the fastest launch at this angle falls short, this angle cannot work.
        if (ReachAtSpeed(limits.MaxJumpSpeed, cos, sin, dy, limits.LedgeClearance, maxTicks) < distance)
        {
            return false;
        }

        // Halve the interval: low is always too short, high always long enough.
        double low = 0;
        double high = limits.MaxJumpSpeed;
        for (int i = 0; i < SpeedSearchIterations; i++)
        {
            double middle = (low + high) / 2;
            if (ReachAtSpeed(middle, cos, sin, dy, limits.LedgeClearance, maxTicks) < distance)
            {
                low = middle;
            }
            else
            {
                high = middle;
            }
        }

        double vy = high * sin;
        double crossingTicks;
        int landingTick;
        if (!FindDescendingCrossing(vy, dy, limits.LedgeClearance, maxTicks, out crossingTicks, out landingTick))
        {
            return false;
        }

        // The engine lands at the end of whole tick landingTick. Spread the horizontal distance over exactly those
        // ticks. This is a slightly lower horizontal speed than the search found, so the speed limit still holds.
        double flightSeconds = landingTick * PhysicsTickSeconds;
        double horizontalSpeed = distance / flightSeconds;
        double speed = Math.Sqrt(horizontalSpeed * horizontalSpeed + vy * vy);
        if (speed > limits.MaxJumpSpeed + LimitTolerance)
        {
            return false;
        }

        result.VX = horizontalSpeed * dx / distance;
        result.VZ = horizontalSpeed * dz / distance;
        result.VY = vy;
        result.FlightTicks = landingTick;
        result.FlightSeconds = flightSeconds;
        return true;
    }

    // Horizontal distance covered by the time the arc comes down through height dy, or -1 if it never does.
    private static double ReachAtSpeed(double speed, double cos, double sin, double dy, double ledgeClearance, int maxTicks)
    {
        double crossingTicks;
        int landingTick;
        if (!FindDescendingCrossing(speed * sin, dy, ledgeClearance, maxTicks, out crossingTicks, out landingTick))
        {
            return -1;
        }
        return speed * cos * crossingTicks * PhysicsTickSeconds;
    }

    // Runs the vertical tick loop from height 0 with launch speed vy (blocks per second) and finds where the arc
    // comes down through height dy: crossingTicks is the exact (fractional) moment, landingTick the tick it happens in.
    // When dy is above the start, the arc must also peak at least ledgeClearance above dy.
    private static bool FindDescendingCrossing(double vy, double dy, double ledgeClearance, int maxTicks,
        out double crossingTicks, out int landingTick)
    {
        double m = vy / BlocksPerSecondPerMotionUnit;
        double y = 0;
        double apex = 0;
        for (int tick = 1; tick <= maxTicks; tick++)
        {
            m = StepVerticalMotion(m);
            double previousY = y;
            y = y + m * PositionPerMotionPerTick;
            if (y > apex)
            {
                apex = y;
            }
            if (previousY > dy && y <= dy)
            {
                if (dy > 0 && apex < dy + ledgeClearance)
                {
                    break;
                }
                crossingTicks = tick - 1 + (previousY - dy) / (previousY - y);
                landingTick = tick;
                return true;
            }
        }
        crossingTicks = 0;
        landingTick = 0;
        return false;
    }

    private static double PreferredAngle(JumpLimits limits)
    {
        return Clamp(limits.JumpAngleDeg, limits.MinJumpAngleDeg, limits.MaxJumpAngleDeg);
    }

    private static double Clamp(double value, double min, double max)
    {
        if (value < min)
        {
            return min;
        }
        if (value > max)
        {
            return max;
        }
        return value;
    }
}
