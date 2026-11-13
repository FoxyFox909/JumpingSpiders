using System;
using Xunit;

namespace JumpingSpiders.Tests;

/// <summary>
/// Checks the jump model and launch solver against EngineJumpSimulator, an independent transcription of the
/// engine's physics tick, including the steering that JumpController does in the game.
/// </summary>
public class JumpMathTests
{
    // How the AI ticks (which steer) interleave with physics ticks in a flight.
    private const int SteerBeforeEveryTick = 0;
    private const int SteerAtFiftyHertz = 1;       // the server's 20 ms entity tick against the 1/30 s physics tick
    private const int SteerBeforeTickPairs = 2;    // a busy server runs physics ticks two at a time
    private const int SteerOnlyAtLaunch = 3;       // no steering at all: what happens without JumpController

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

                AssertLandsOnTarget(step, limits, SteerBeforeEveryTick, false, 0.01);
                AssertLandsOnTarget(step, limits, SteerBeforeEveryTick, true, 0.05);
                AssertLandsOnTarget(step, limits, SteerAtFiftyHertz, true, 0.05);
                AssertLandsOnTarget(step, limits, SteerBeforeTickPairs, true, 0.08);
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
        AssertLandsOnTarget(step, limits, SteerAtFiftyHertz, true, 0.05);
    }

    [Fact]
    public void WithoutSteeringGroundFrictionMakesTheJumpFallShort()
    {
        // This is why JumpController re-writes the horizontal motion: the engine keeps applying ground friction
        // for 0.15 s after take-off. Launching once and letting go lands far short.
        JumpLimits limits = DefaultLimits();
        JumpStep step = JumpMath.CreateJump(0, 0, 0, 4, 0, 0, limits);
        double maxLag;
        EngineJumpSimulator sim = Fly(step, limits, SteerOnlyAtLaunch, true, out maxLag);
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
    public void TickClockFollowsVerticalMotion()
    {
        JumpStep step = JumpMath.CreateJump(0, 0, 0, 3, 2, 0, DefaultLimits());
        double m = step.VY / 60.0;
        Assert.Equal(0, JumpMath.EstimateTicksSinceLaunch(step, m, step.FlightTicks));
        for (int tick = 1; tick <= step.FlightTicks; tick++)
        {
            m = JumpMath.StepVerticalMotion(m);
            Assert.Equal(tick, JumpMath.EstimateTicksSinceLaunch(step, m, step.FlightTicks));
        }
    }

    private static void AssertLandsOnTarget(JumpStep step, JumpLimits limits, int pattern, bool coyote, double tolerance)
    {
        double maxLag;
        EngineJumpSimulator sim = Fly(step, limits, pattern, coyote, out maxLag);
        double error = Math.Sqrt((sim.X - step.ToX) * (sim.X - step.ToX) + (sim.Z - step.ToZ) * (sim.Z - step.ToZ));
        string what = "jump d=(" + (step.ToX - step.FromX).ToString("0.00") + ", " + (step.ToY - step.FromY).ToString("0.00")
            + ", " + (step.ToZ - step.FromZ).ToString("0.00") + ") pattern " + pattern + " coyote " + coyote;
        Assert.True(sim.OnGround, what + ": did not land");
        Assert.True(Math.Abs(sim.Y - step.ToY) < 1e-6, what + ": landed at y " + sim.Y);
        Assert.True(error < tolerance, what + ": landed " + error.ToString("0.000") + " blocks from the target");
        Assert.True(sim.TicksDone == step.FlightTicks, what + ": landed after " + sim.TicksDone + " ticks, planned " + step.FlightTicks);

        // JumpPlanner.IsArcClear assumes the spider is never more than about one tick of travel off the plan.
        // With the engine's ground friction (as in the game) the steering keeps it within a third of a tick.
        // Without the friction the friction compensation overshoots by up to one tick, and on a busy server
        // (physics ticks in pairs) the lag can reach two and a half ticks during the first few ticks.
        double tickTravel = Math.Sqrt(step.VX * step.VX + step.VZ * step.VZ) * JumpMath.PhysicsTickSeconds;
        double allowedTicks = 0.35;
        if (!coyote)
        {
            allowedTicks = 1.1;
        }
        if (pattern == SteerBeforeTickPairs)
        {
            allowedTicks = 2.5;
        }
        Assert.True(maxLag <= allowedTicks * tickTravel + 0.01, what + ": strayed " + maxLag.ToString("0.000") + " blocks from the arc, one tick is " + tickTravel.ToString("0.000"));
    }

    // Launches like JumpController.Launch and steers like JumpController.OnTick, in the given interleaving.
    // maxLag is the largest horizontal distance between the spider and the planned arc at the end of a tick.
    private static EngineJumpSimulator Fly(JumpStep step, JumpLimits limits, int pattern, bool coyote, out double maxLag)
    {
        maxLag = 0;
        EngineJumpSimulator sim = new EngineJumpSimulator();
        sim.X = step.FromX;
        sim.Y = step.FromY;
        sim.Z = step.FromZ;
        sim.FloorY = step.ToY;
        sim.SimulateCoyoteTime = coyote;
        sim.MotionY = step.VY / 60.0;
        Steer(sim, step, limits);

        double aiClock = 0.02;
        bool leftGround = false;
        int maxTicks = JumpMath.MaxTicksFor(limits.MaxFlightSeconds) + 10;
        while (sim.TicksDone < maxTicks)
        {
            double physicsTime = (sim.TicksDone + 1) * JumpMath.PhysicsTickSeconds;
            if (pattern == SteerBeforeEveryTick)
            {
                Steer(sim, step, limits);
            }
            else if (pattern == SteerAtFiftyHertz)
            {
                // Run every AI tick that comes before this physics tick.
                while (aiClock < physicsTime)
                {
                    Steer(sim, step, limits);
                    aiClock += 0.02;
                }
            }
            else if (pattern == SteerBeforeTickPairs)
            {
                if (sim.TicksDone % 2 == 0)
                {
                    Steer(sim, step, limits);
                }
            }

            sim.PhysicsTick();
            double planX;
            double planY;
            double planZ;
            JumpMath.PositionAt(step, sim.TicksDone * JumpMath.PhysicsTickSeconds, out planX, out planY, out planZ);
            double lag = Math.Sqrt((planX - sim.X) * (planX - sim.X) + (planZ - sim.Z) * (planZ - sim.Z));
            if (lag > maxLag)
            {
                maxLag = lag;
            }
            if (!sim.OnGround)
            {
                leftGround = true;
            }
            if (leftGround && sim.OnGround)
            {
                break;
            }
        }
        return sim;
    }

    private static void Steer(EngineJumpSimulator sim, JumpStep step, JumpLimits limits)
    {
        double mx;
        double mz;
        JumpMath.ComputeSteering(step, sim.X, sim.Z, sim.MotionY, limits.MaxJumpSpeed, out mx, out mz);
        sim.MotionX = mx;
        sim.MotionZ = mz;
    }
}
