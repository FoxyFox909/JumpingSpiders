using System;

namespace JumpingSpiders;

/// <summary>
/// The ballistic model of a spider jump, the solver that turns "I want to land there" into a launch velocity, and
/// the sums JumpController uses to fly the spider along that arc in the game.
///
/// How the game moves a creature (EntityBehaviorControlledPhysics, read from the 1.22.7 source):
/// - Physics runs in fixed ticks of 1/30 second.
/// - Speeds are stored as "motion": blocks per 1/60 second. So 1 block per second is a motion of 1/60,
///   and in one physics tick the position moves by motion * 2.
/// - In every tick, in this order: gravity lowers motion.Y by (0.37 + extra) / 30, where extra = 0.015 * |motion.Y|
///   while falling; then air drag multiplies all of motion by 0.983 ^ (33 / 30); then the position moves by motion * 2
///   (vertical part first, then horizontal, each stopped by blocks).
///
/// The arc (the "natural arc") is planned as if the game's own physics flew the whole jump: the vertical part
/// exactly as gravity and drag would move it, the horizontal part at a constant speed. Checked on a real 1.22.7
/// server: the game's physics follows this model to 0.00001 blocks per tick. The model assumes default physics:
/// the entity JSON must not change gravityFactor, airDragFallingFactor or groundDragFactor (attributes.physics).
///
/// In the game the spider flies that same arc faster (Config.JumpFlightSpeedMultiplier): FlightSchedule says where
/// on the arc it must be at the end of every physics tick, and JumpController writes, after every physics tick, the
/// motion that makes the next tick end there (ComputeDriveMotion). The game's physics still does the moving, so
/// blocks still stop the spider. Reach, landing points and the planner's collision checks are those of the arc.
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

    /// <summary>
    /// How many halvings the speed search does: 25 leave the launch speed within 18 / 2^25 (about 5e-7) blocks per
    /// second. Checked against 50: every solved jump of the unit tests' grid keeps its landing tick, and lands within
    /// 2e-9 blocks of its target both in the engine simulator and on a 1.22.7 server, the same as with 50.
    /// </summary>
    public const int SpeedSearchIterations = 25;

    /// <summary>
    /// How far (blocks) JumpController may move the spider sideways in one physics tick beyond the planned move, to
    /// catch up when something pushed it off the arc. Normally the spider is within 0.001 blocks of the plan and this
    /// never matters; it stops a knocked-away spider from dashing back in one jerk.
    /// </summary>
    public const double MaxCatchUpBlocksPerTick = 0.25;

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
    /// </summary>
    public static double HorizontalMotionForOneTick(double distance)
    {
        return distance / (PositionPerMotionPerTick * DragPerTick);
    }

    /// <summary>
    /// The inverse of one physics tick of vertical motion: the motion.Y (motion units) to write before a physics
    /// tick so that, after the engine's gravity and air drag, the tick moves the spider up by distance blocks
    /// (negative = down). JumpController writes this after every physics tick of a flight.
    /// </summary>
    public static double VerticalMotionForMove(double distance)
    {
        // What must be left after gravity, so that drag and the move give the distance.
        double afterGravity = distance / (PositionPerMotionPerTick * DragPerTick);
        double motion = afterGravity + GravityPerSecond * PhysicsTickSeconds;
        if (motion < 0)
        {
            // Falling: the engine also pulls by FallingExtraGravityFactor times the downward motion.
            motion = motion / (1 + FallingExtraGravityFactor * PhysicsTickSeconds);
        }
        return motion;
    }

    /// <summary>
    /// Height of the natural arc above the take-off point (blocks) after the given number of physics ticks of the
    /// natural flight. Fractions of a tick are interpolated in a straight line, as the engine moves in one go per
    /// tick. Past the landing tick the arc simply goes on down, as if there were no floor.
    /// </summary>
    public static double RelativeHeightAtTicks(JumpStep step, double ticks)
    {
        if (ticks <= 0)
        {
            return 0;
        }
        int wholeTicks = (int)Math.Floor(ticks);
        double fraction = ticks - wholeTicks;
        double m = step.VY / BlocksPerSecondPerMotionUnit;
        double y = 0;
        for (int tick = 1; tick <= wholeTicks; tick++)
        {
            m = StepVerticalMotion(m);
            y = y + m * PositionPerMotionPerTick;
        }
        if (fraction <= 0)
        {
            return y;
        }
        m = StepVerticalMotion(m);
        return y + m * PositionPerMotionPerTick * fraction;
    }

    /// <summary>
    /// The vertical motion (motion units) the game's own physics would give the spider at this point of the natural
    /// flight (counted in natural physics ticks). JumpController hands the spider back to the game's physics with it
    /// when a flight is cut short, so that it drops the way an unhurried jump would.
    /// </summary>
    public static double NaturalMotionYAtTicks(JumpStep step, double ticks)
    {
        double m = step.VY / BlocksPerSecondPerMotionUnit;
        int wholeTicks = (int)Math.Floor(Math.Max(0, ticks));
        for (int tick = 1; tick <= wholeTicks; tick++)
        {
            m = StepVerticalMotion(m);
        }
        return m;
    }

    /// <summary>
    /// The driving rule of JumpController: the motion to write after physics tick number ticksDone of a flight
    /// (0 = at launch) so that the next physics tick ends where the schedule says, starting from where the spider
    /// really is. Any lag (from the engine's friction, a push) is made up in that one tick, by at most
    /// MaxCatchUpBlocksPerTick beyond the planned move.
    ///
    /// The game's ground friction keeps acting for 0.15 s after take-off ("coyote time", see
    /// GroundFrictionTicksAfterTakeOff); groundFrictionNextTick says it will act in the next tick (see
    /// NextTickHasGroundFriction), and the horizontal motion is then raised to make up for it (GroundFrictionPerTick).
    /// Call only while ticksDone is less than schedule.Ticks.
    /// </summary>
    public static void ComputeDriveMotion(FlightSchedule schedule, int ticksDone, bool groundFrictionNextTick,
        double currentX, double currentY, double currentZ, out double motionX, out double motionY, out double motionZ)
    {
        double targetX;
        double targetY;
        double targetZ;
        schedule.TargetAfterTick(ticksDone + 1, out targetX, out targetY, out targetZ);
        double previousX;
        double previousY;
        double previousZ;
        schedule.TargetAfterTick(ticksDone, out previousX, out previousY, out previousZ);

        double dx = targetX - currentX;
        double dz = targetZ - currentZ;
        double length = Math.Sqrt(dx * dx + dz * dz);
        double plannedX = targetX - previousX;
        double plannedZ = targetZ - previousZ;
        double maxLength = Math.Sqrt(plannedX * plannedX + plannedZ * plannedZ) + MaxCatchUpBlocksPerTick;
        if (length > maxLength && length > 0)
        {
            dx = dx * maxLength / length;
            dz = dz * maxLength / length;
        }

        // The next tick still gets the ground friction: ask for more so that what is left is what we want.
        if (groundFrictionNextTick)
        {
            dx = dx / GroundFrictionPerTick;
            dz = dz / GroundFrictionPerTick;
        }
        motionX = HorizontalMotionForOneTick(dx);
        motionZ = HorizontalMotionForOneTick(dz);
        motionY = VerticalMotionForMove(targetY - currentY);
    }

    /// <summary>
    /// The horizontal motion that moves the spider towards (targetX, targetZ) in one physics tick, by at most
    /// maxPerTick blocks. JumpController uses it after the planned landing tick when the spider has not touched down
    /// yet (the block it aimed for was removed, say): it keeps the spider above the landing point while it falls.
    /// </summary>
    public static void ComputeHoldMotion(double targetX, double targetZ, double currentX, double currentZ, double maxPerTick,
        out double motionX, out double motionZ)
    {
        double dx = targetX - currentX;
        double dz = targetZ - currentZ;
        double length = Math.Sqrt(dx * dx + dz * dz);
        if (length > maxPerTick && length > 0)
        {
            dx = dx * maxPerTick / length;
            dz = dz * maxPerTick / length;
        }
        motionX = HorizontalMotionForOneTick(dx);
        motionZ = HorizontalMotionForOneTick(dz);
    }

    /// <summary>
    /// Keeps count of the engine's ground friction (PModuleOnGround) for JumpController: call once before each
    /// physics tick, with whether the spider is on the ground now. Returns true when the friction will act in that
    /// tick. frictionTicksLeft is the count carried from call to call (start it at 0): standing on the ground
    /// restarts the 0.15 s of coyote time, which then lasts GroundFrictionTicksAfterTakeOff ticks.
    /// </summary>
    public static bool NextTickHasGroundFriction(bool onGroundNow, ref int frictionTicksLeft)
    {
        if (onGroundNow)
        {
            frictionTicksLeft = GroundFrictionTicksAfterTakeOff;
        }
        if (frictionTicksLeft > 0)
        {
            frictionTicksLeft--;
            return true;
        }
        return false;
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
    /// Only the unit tests call this (and CreateJump and PositionAt): they are the reference model the tests check the
    /// planner and the engine simulator against. The mod plans through JumpPlanner.TryJump, which walks the same
    /// angles itself so that it can check each arc against the blocks.
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
    /// Only the unit tests use it.
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
    /// tick the spider does not go below the landing height (the landing surface stops it). Only the unit tests use
    /// it (FlightSchedule and JumpController follow the arc tick by tick instead).
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
        double relativeY = RelativeHeightAtTicks(step, ticks);

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
