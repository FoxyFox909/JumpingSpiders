using System;
using Xunit;

namespace JumpingSpiders.Tests;

/// <summary>
/// Checks the jump model and launch solver against EngineJumpSimulator, an independent transcription of the
/// engine's physics tick, including the driving that JumpController does in the game after every physics tick.
/// </summary>
public class JumpMathTests
{
    private const double Natural = 1.0;
    private const double Default = 1.45;

    private static JumpLimits DefaultLimits()
    {
        return new JumpLimits();
    }

    [Fact]
    public void VerticalModelMatchesEngineTickForTick()
    {
        JumpStep step = JumpMath.CreateJump(0, 0, 0, 3, 1, 0, DefaultLimits());
        Assert.NotNull(step);

        EngineJumpSimulator sim = new EngineJumpSimulator();
        sim.FloorY = step.ToY;
        sim.SimulateCoyoteTime = false;
        sim.MotionY = step.VY / 60.0;
        for (int tick = 1; tick < step.FlightTicks; tick++)
        {
            // Hold the horizontal speed exactly as the model assumes.
            sim.MotionX = JumpMath.HorizontalMotionForOneTick(step.VX * JumpMath.PhysicsTickSeconds);
            sim.MotionZ = JumpMath.HorizontalMotionForOneTick(step.VZ * JumpMath.PhysicsTickSeconds);
            sim.PhysicsTick();

            double x;
            double y;
            double z;
            JumpMath.PositionAt(step, tick * JumpMath.PhysicsTickSeconds, out x, out y, out z);
            // The engine works partly in float, the model in double: allow for that.
            Assert.True(Math.Abs(sim.Y - y) < 1e-4, "tick " + tick + ": engine y " + sim.Y + ", model y " + y);
            Assert.True(Math.Abs(sim.X - x) < 1e-4, "tick " + tick + ": engine x " + sim.X + ", model x " + x);
        }
    }

    [Fact]
    public void SolvedJumpsLandOnTargetAcrossGrid()
    {
        JumpLimits limits = DefaultLimits();
        int solved = 0;
        for (int dyStep = -6; dyStep <= 4; dyStep++)
        {
            for (int distanceStep = 1; distanceStep <= 12; distanceStep++)
            {
                double distance = distanceStep * 0.5;
                double dy = dyStep;
                // A diagonal direction, so X and Z are both exercised.
                double dx = distance * 0.8;
                double dz = -distance * 0.6;
                JumpStep step = JumpMath.CreateJump(10, 64, 10, 10 + dx, 64 + dy, 10 + dz, limits);
                if (step == null)
                {
                    continue;
                }
                solved++;

                Assert.True(step.FlightSeconds <= limits.MaxFlightSeconds + 1e-9);
                double speed = Math.Sqrt(step.VX * step.VX + step.VY * step.VY + step.VZ * step.VZ);
                Assert.True(speed <= limits.MaxJumpSpeed + 1e-6, "too fast: " + speed);

                AssertLandsOnTarget(step, Natural);
                AssertLandsOnTarget(step, Default);
                AssertLandsOnTarget(step, 2.0);
                AssertLandsOnTarget(step, 0.7);
            }
        }
        // 11 heights x 12 distances, all within the default limits.
        Assert.True(solved == 132, "only " + solved + " of 132 grid targets were solvable");
    }

    [Fact]
    public void ImportantJumpsAreSolvable()
    {
        JumpLimits limits = DefaultLimits();
        // Straight onto a ledge 4 blocks up, from close by and from further away.
        Assert.NotNull(JumpMath.CreateJump(0, 0, 0, 1, 4, 0, limits));
        Assert.NotNull(JumpMath.CreateJump(0, 0, 0, 3, 4, 0, limits));
        // A long flat jump and long jumps down.
        Assert.NotNull(JumpMath.CreateJump(0, 0, 0, 6, 0, 0, limits));
        Assert.NotNull(JumpMath.CreateJump(0, 0, 0, 6, -6, 0, limits));
        Assert.NotNull(JumpMath.CreateJump(0, 0, 0, 0.5, -6, 0, limits));
    }

    [Fact]
    public void JumpUpFourBlocksLandsOnTargetWithEngineFriction()
    {
        JumpLimits limits = DefaultLimits();
        JumpStep step = JumpMath.CreateJump(0.5, 1, 0.5, 3.5, 5, 0.5, limits);
        Assert.NotNull(step);
        AssertLandsOnTarget(step, Default);
        AssertLandsOnTarget(step, Natural);
    }

    [Fact]
    public void WithoutSteeringGroundFrictionMakesTheJumpFallShort()
    {
        // This is why JumpController re-writes the horizontal motion: the engine keeps applying ground friction
        // for 0.15 s after take-off. Launching once and letting go lands far short.
        JumpLimits limits = DefaultLimits();
        JumpStep step = JumpMath.CreateJump(0, 0, 0, 4, 0, 0, limits);
        EngineJumpSimulator sim = new EngineJumpSimulator();
        sim.FloorY = step.ToY;
        sim.MotionX = JumpMath.HorizontalMotionForOneTick(step.VX * JumpMath.PhysicsTickSeconds);
        sim.MotionY = step.VY / 60.0;
        bool leftGround = false;
        while (sim.TicksDone < 200)
        {
            sim.PhysicsTick();
            if (!sim.OnGround)
            {
                leftGround = true;
            }
            if (leftGround && sim.OnGround)
            {
                break;
            }
        }
        Assert.True(sim.X < 1.0, "expected a short jump without steering, got x = " + sim.X);
    }

    [Fact]
    public void SolverRefusesTargetsBeyondLimits()
    {
        JumpLimits limits = DefaultLimits();
        JumpStep result = new JumpStep();
        Assert.False(JumpMath.SolveLaunch(2, 4.5, 0, limits, result), "too high");
        Assert.False(JumpMath.SolveLaunch(2, -6.5, 0, limits, result), "too deep");
        Assert.False(JumpMath.SolveLaunch(6.5, 0, 0, limits, result), "too far");
        Assert.False(JumpMath.SolveLaunch(4.7, 0, 4.7, limits, result), "too far diagonally");
        Assert.False(JumpMath.SolveLaunch(0, 1, 0, limits, result), "straight up");

        // Within the distance limits but beyond the speed cap.
        JumpLimits slow = DefaultLimits();
        slow.MaxJumpSpeed = 8;
        Assert.False(JumpMath.SolveLaunch(6, 4, 0, slow, result), "too fast for MaxJumpSpeed");
        Assert.False(JumpMath.SolveLaunch(1, 2, 0, slow, result), "2 blocks up needs more than 8 b/s");
        Assert.True(JumpMath.SolveLaunch(1, 0, 0, slow, result), "a short hop should still work");

        // Within everything except the flight time.
        JumpLimits quick = DefaultLimits();
        quick.MaxFlightSeconds = 0.3;
        Assert.False(JumpMath.SolveLaunch(1, -6, 0, quick, result), "a 6 block drop takes longer than 0.3 s");
    }

    [Fact]
    public void PreferredAngleIsUsedWhenItWorks()
    {
        JumpLimits limits = DefaultLimits();
        JumpStep step = JumpMath.CreateJump(0, 0, 0, 4, 0, 0, limits);
        double horizontal = Math.Sqrt(step.VX * step.VX + step.VZ * step.VZ);
        double angle = Math.Atan2(step.VY, horizontal) * 180 / Math.PI;
        // Rounding the landing to a whole tick makes the launch a little steeper, never flatter.
        Assert.True(angle >= limits.JumpAngleDeg - 1e-6 && angle < limits.JumpAngleDeg + 4, "angle " + angle);
    }

    [Fact]
    public void AngleCandidatesStartAtPreferredAndStayInRange()
    {
        JumpLimits limits = DefaultLimits();
        Assert.Equal(limits.JumpAngleDeg, JumpMath.AngleCandidate(limits, 0));
        Assert.Equal(limits.JumpAngleDeg + 5, JumpMath.AngleCandidate(limits, 1));
        Assert.Equal(limits.JumpAngleDeg - 5, JumpMath.AngleCandidate(limits, 2));
        bool sawMin = false;
        bool sawMax = false;
        for (int i = 0; i < JumpMath.AngleCandidateCount(limits); i++)
        {
            double angle = JumpMath.AngleCandidate(limits, i);
            if (Math.Abs(angle - limits.MinJumpAngleDeg) < 1e-9)
            {
                sawMin = true;
            }
            if (Math.Abs(angle - limits.MaxJumpAngleDeg) < 1e-9)
            {
                sawMax = true;
            }
        }
        Assert.True(sawMin && sawMax);
    }

    [Fact]
    public void VerticalMotionForMoveIsTheInverseOfOnePhysicsTick()
    {
        double[] distances = new double[] { 0.6, 0.2, 0.01, 0, -0.005, -0.05, -0.4, -0.9 };
        for (int i = 0; i < distances.Length; i++)
        {
            double motion = JumpMath.VerticalMotionForMove(distances[i]);
            double moved = JumpMath.StepVerticalMotion(motion) * JumpMath.PositionPerMotionPerTick;
            Assert.True(Math.Abs(moved - distances[i]) < 1e-12, "asked " + distances[i] + ", moved " + moved);
        }
    }

    [Fact]
    public void RelativeHeightAtTicksFollowsTheTickLoopAndGoesOnPastLanding()
    {
        JumpStep step = JumpMath.CreateJump(0, 0, 0, 4, 1, 0, DefaultLimits());
        double m = step.VY / 60.0;
        double y = 0;
        for (int tick = 1; tick <= step.FlightTicks + 5; tick++)
        {
            double before = y;
            m = JumpMath.StepVerticalMotion(m);
            y = y + m * JumpMath.PositionPerMotionPerTick;
            Assert.True(Math.Abs(JumpMath.RelativeHeightAtTicks(step, tick) - y) < 1e-12, "tick " + tick);
            double halfway = JumpMath.RelativeHeightAtTicks(step, tick - 0.5);
            Assert.True(Math.Abs(halfway - (before + y) / 2) < 1e-12, "half tick before " + tick);
            Assert.True(Math.Abs(JumpMath.NaturalMotionYAtTicks(step, tick) - m) < 1e-15, "motion at tick " + tick);
        }
        // Past the landing tick the arc is below the landing height.
        Assert.True(JumpMath.RelativeHeightAtTicks(step, step.FlightTicks + 1) < step.ToY - step.FromY);
    }

    [Fact]
    public void GroundFrictionLastsFiveTicksAfterLeavingTheGround()
    {
        int left = 0;
        Assert.True(JumpMath.NextTickHasGroundFriction(true, ref left));
        for (int tick = 2; tick <= JumpMath.GroundFrictionTicksAfterTakeOff; tick++)
        {
            Assert.True(JumpMath.NextTickHasGroundFriction(false, ref left), "tick " + tick);
        }
        Assert.False(JumpMath.NextTickHasGroundFriction(false, ref left));
        Assert.False(JumpMath.NextTickHasGroundFriction(false, ref left));
        // A launch from the air: no friction at all.
        int none = 0;
        Assert.False(JumpMath.NextTickHasGroundFriction(false, ref none));
    }

    [Fact]
    public void DrivenFlightAtMultiplierOneIsTheNaturalFlight()
    {
        JumpStep step = JumpMath.CreateJump(0.5, 1, 0.5, 4.3, 3, -1.2, DefaultLimits());
        Assert.NotNull(step);
        int landedTick;
        double maxLag;
        EngineJumpSimulator sim = FlyDriven(step, Natural, -1, 0, out landedTick, out maxLag);
        Assert.Equal(step.FlightTicks, landedTick);
        Assert.True(maxLag < 1e-4, "strayed " + maxLag + " blocks from the natural arc");
        Assert.True(Math.Abs(sim.X - step.ToX) < 1e-4 && Math.Abs(sim.Z - step.ToZ) < 1e-4);
    }

    [Fact]
    public void DrivenFlightIsShorterByTheMultiplier()
    {
        JumpLimits limits = DefaultLimits();
        JumpStep[] steps = new JumpStep[]
        {
            JumpMath.CreateJump(0, 0, 0, 3, 0, 0, limits),
            JumpMath.CreateJump(0, 0, 0, 6, 0, 0, limits),
            JumpMath.CreateJump(0, 0, 0, 2, 4, 0, limits),
            JumpMath.CreateJump(0, 0, 0, 2, -6, 0, limits)
        };
        for (int i = 0; i < steps.Length; i++)
        {
            int landedTick;
            double maxLag;
            FlyDriven(steps[i], Default, -1, 0, out landedTick, out maxLag);
            double ideal = steps[i].FlightTicks / Default;
            Assert.True(Math.Abs(landedTick - ideal) <= 0.5 + 1e-9,
                "natural " + steps[i].FlightTicks + " ticks, driven " + landedTick + ", ideal " + ideal.ToString("0.00"));
        }
    }

    [Fact]
    public void DrivenFlightCatchesUpAfterAPush()
    {
        JumpStep step = JumpMath.CreateJump(0, 0, 0, 5, 1, 0, DefaultLimits());
        int landedTick;
        double maxLag;
        // Something shoves the spider 0.3 blocks sideways in the middle of the flight.
        EngineJumpSimulator sim = FlyDriven(step, Default, 6, 0.3, out landedTick, out maxLag);
        double error = Math.Sqrt((sim.X - step.ToX) * (sim.X - step.ToX) + (sim.Z - step.ToZ) * (sim.Z - step.ToZ));
        Assert.True(sim.OnGround, "did not land");
        Assert.True(error < 1e-3, "landed " + error + " blocks off");
    }

    [Fact]
    public void HoldMotionIsCappedAndAimsAtTheTarget()
    {
        double mx;
        double mz;
        JumpMath.ComputeHoldMotion(3, 4, 0, 0, 0.5, out mx, out mz);
        double movedX = mx * JumpMath.PositionPerMotionPerTick * JumpMath.DragPerTick;
        double movedZ = mz * JumpMath.PositionPerMotionPerTick * JumpMath.DragPerTick;
        Assert.True(Math.Abs(movedX - 0.3) < 1e-9 && Math.Abs(movedZ - 0.4) < 1e-9, "moved " + movedX + ", " + movedZ);
        JumpMath.ComputeHoldMotion(3.1, 4, 3, 4, 0.5, out mx, out mz);
        Assert.True(Math.Abs(mx * JumpMath.PositionPerMotionPerTick * JumpMath.DragPerTick - 0.1) < 1e-9);
        Assert.True(Math.Abs(mz) < 1e-12);
    }

    private static void AssertLandsOnTarget(JumpStep step, double multiplier)
    {
        int landedTick;
        double maxLag;
        EngineJumpSimulator sim = FlyDriven(step, multiplier, -1, 0, out landedTick, out maxLag);
        FlightSchedule schedule = FlightSchedule.Create(step, multiplier);
        double error = Math.Sqrt((sim.X - step.ToX) * (sim.X - step.ToX) + (sim.Z - step.ToZ) * (sim.Z - step.ToZ));
        string what = "jump d=(" + (step.ToX - step.FromX).ToString("0.00") + ", " + (step.ToY - step.FromY).ToString("0.00")
            + ", " + (step.ToZ - step.FromZ).ToString("0.00") + ") x" + multiplier;
        Assert.True(landedTick > 0, what + ": did not land");
        Assert.True(Math.Abs(sim.Y - step.ToY) < 1e-6, what + ": landed at y " + sim.Y);
        Assert.True(error < 1e-3, what + ": landed " + error.ToString("0.0000") + " blocks from the target");
        Assert.True(landedTick == schedule.Ticks, what + ": landed after " + landedTick + " ticks, planned " + schedule.Ticks);
        // Driven every physics tick, the spider stays on the timetable (JumpPlanner.IsArcClear relies on that).
        Assert.True(maxLag < 1e-3, what + ": strayed " + maxLag.ToString("0.0000") + " blocks from the timetable");
    }

    // Launches like JumpController.Launch and drives like its physics hook: after every physics tick it writes the
    // motion for the next one. pushAfterTick (or -1) shoves the spider pushBlocks along +Z after that tick.
    // landedTick is the tick it touched down in (0 = never); maxLag is the largest distance from the timetable at
    // the end of a tick, not counting the tick of the push nor the first tick: the simulator starts with a sub-step
    // remainder that gives the first tick three friction sub-steps instead of two, and the next tick makes that up.
    private static EngineJumpSimulator FlyDriven(JumpStep step, double multiplier, int pushAfterTick, double pushBlocks,
        out int landedTick, out double maxLag)
    {
        maxLag = 0;
        landedTick = 0;
        FlightSchedule schedule = FlightSchedule.Create(step, multiplier);
        EngineJumpSimulator sim = new EngineJumpSimulator();
        sim.X = step.FromX;
        sim.Y = step.FromY;
        sim.Z = step.FromZ;
        sim.FloorY = step.ToY;

        int frictionTicksLeft = 0;
        double plannedMoveY = Drive(sim, schedule, 0, ref frictionTicksLeft);
        bool leftGround = false;
        while (sim.TicksDone < schedule.Ticks + 30)
        {
            sim.PhysicsTick();
            int ticksDone = sim.TicksDone;
            if (ticksDone >= 2 && ticksDone <= schedule.Ticks && ticksDone != pushAfterTick)
            {
                double planX;
                double planY;
                double planZ;
                schedule.TargetAfterTick(ticksDone, out planX, out planY, out planZ);
                if (ticksDone == schedule.Ticks)
                {
                    planY = step.ToY;
                }
                double lag = Math.Sqrt((planX - sim.X) * (planX - sim.X) + (planY - sim.Y) * (planY - sim.Y) + (planZ - sim.Z) * (planZ - sim.Z));
                if (lag > maxLag)
                {
                    maxLag = lag;
                }
            }
            if (!sim.OnGround)
            {
                leftGround = true;
            }
            // The landing rule of JumpController.
            if (leftGround && (sim.OnGround || (sim.CollidedVertically && plannedMoveY < 0)))
            {
                landedTick = ticksDone;
                break;
            }
            if (ticksDone == pushAfterTick)
            {
                sim.Z = sim.Z + pushBlocks;
            }
            if (ticksDone < schedule.Ticks)
            {
                plannedMoveY = Drive(sim, schedule, ticksDone, ref frictionTicksLeft);
            }
            else
            {
                double mx;
                double mz;
                JumpMath.ComputeHoldMotion(step.ToX, step.ToZ, sim.X, sim.Z, 0.6, out mx, out mz);
                sim.MotionX = mx;
                sim.MotionZ = mz;
                plannedMoveY = 0;
            }
        }
        return sim;
    }

    // Writes the drive motion for the next tick; returns the planned vertical move of that tick.
    private static double Drive(EngineJumpSimulator sim, FlightSchedule schedule, int ticksDone, ref int frictionTicksLeft)
    {
        bool friction = JumpMath.NextTickHasGroundFriction(sim.OnGround, ref frictionTicksLeft);
        double mx;
        double my;
        double mz;
        JumpMath.ComputeDriveMotion(schedule, ticksDone, friction, sim.X, sim.Y, sim.Z, out mx, out my, out mz);
        sim.MotionX = mx;
        sim.MotionY = my;
        sim.MotionZ = mz;
        double x;
        double y;
        double z;
        schedule.TargetAfterTick(ticksDone + 1, out x, out y, out z);
        return y - sim.Y;
    }
}
