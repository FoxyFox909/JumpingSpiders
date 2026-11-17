using System;
using Xunit;

namespace JumpingSpiders.Tests;

/// <summary>
/// Checks FlightSchedule, the timetable of a sped-up flight: same arc, same landing point, the time in the air
/// divided by the multiplier (to the nearest physics tick), and a last tick that stays within the footing that
/// JumpPlanner checked.
/// </summary>
public class FlightScheduleTests
{
    private static JumpStep Jump(double dx, double dy, double dz)
    {
        JumpStep step = JumpMath.CreateJump(10.5, 64, 10.5, 10.5 + dx, 64 + dy, 10.5 + dz, new JumpLimits());
        Assert.NotNull(step);
        return step;
    }

    [Fact]
    public void DrivenTicksAreTheNaturalTicksDividedByTheMultiplierRounded()
    {
        Assert.Equal(10, FlightSchedule.DrivenTicks(15, 1.45));
        Assert.Equal(21, FlightSchedule.DrivenTicks(30, 1.45));
        Assert.Equal(15, FlightSchedule.DrivenTicks(15, 1));
        Assert.Equal(30, FlightSchedule.DrivenTicks(15, 0.5));
        // A flight of two or more ticks keeps at least two, so the spider is in the air at the end of one.
        Assert.Equal(2, FlightSchedule.DrivenTicks(2, 1.45));
        Assert.Equal(2, FlightSchedule.DrivenTicks(3, 10));
        Assert.Equal(1, FlightSchedule.DrivenTicks(1, 1.45));
        Assert.Equal(10 * JumpMath.PhysicsTickSeconds, FlightSchedule.DrivenSeconds(15, 1.45), 12);
    }

    [Fact]
    public void InvalidMultipliersMeanTheNaturalFlight()
    {
        Assert.Equal(1, FlightSchedule.SanitizeMultiplier(0));
        Assert.Equal(1, FlightSchedule.SanitizeMultiplier(-2));
        Assert.Equal(1, FlightSchedule.SanitizeMultiplier(double.NaN));
        Assert.Equal(1, FlightSchedule.SanitizeMultiplier(double.PositiveInfinity));
        Assert.Equal(1.45, FlightSchedule.SanitizeMultiplier(1.45));
        Assert.Equal(15, FlightSchedule.DrivenTicks(15, double.NaN));
    }

    [Fact]
    public void AtMultiplierOneTheTimetableIsTheNaturalFlight()
    {
        JumpStep step = Jump(4, 1, -2);
        FlightSchedule schedule = FlightSchedule.Create(step, 1);
        Assert.Equal(step.FlightTicks, schedule.Ticks);
        Assert.Equal(step.FlightSeconds, schedule.FlightSeconds, 12);
        for (int tick = 0; tick < schedule.Ticks; tick++)
        {
            Assert.Equal(tick, schedule.NaturalTicksAt(tick), 12);
            double x;
            double y;
            double z;
            schedule.TargetAfterTick(tick, out x, out y, out z);
            double px;
            double py;
            double pz;
            JumpMath.PositionAt(step, tick * JumpMath.PhysicsTickSeconds, out px, out py, out pz);
            Assert.True(Math.Abs(x - px) < 1e-9 && Math.Abs(y - py) < 1e-9 && Math.Abs(z - pz) < 1e-9, "tick " + tick);
        }
    }

    [Fact]
    public void EveryPointIsOnTheNaturalArc()
    {
        JumpStep step = Jump(5, 2, 1);
        FlightSchedule schedule = FlightSchedule.Create(step, 1.45);
        for (int tick = 1; tick < schedule.Ticks; tick++)
        {
            double naturalTicks = schedule.NaturalTicksAt(tick);
            Assert.True(naturalTicks > schedule.NaturalTicksAt(tick - 1), "time must move forward");
            double x;
            double y;
            double z;
            schedule.TargetAfterTick(tick, out x, out y, out z);
            double px;
            double py;
            double pz;
            JumpMath.PositionAt(step, naturalTicks * JumpMath.PhysicsTickSeconds, out px, out py, out pz);
            Assert.True(Math.Abs(x - px) < 1e-9 && Math.Abs(y - py) < 1e-9 && Math.Abs(z - pz) < 1e-9, "tick " + tick);
            if (tick == schedule.Ticks - 1)
            {
                // Still above the landing height before the last tick (this jump goes up: it climbs through it earlier).
                Assert.True(y > step.ToY, "the tick before landing is below the landing height");
            }
        }
    }

    [Fact]
    public void LastPointIsTheLandingPointPushedIntoTheFloor()
    {
        JumpStep step = Jump(3, 0, 0);
        FlightSchedule schedule = FlightSchedule.Create(step, 1.45);
        double x;
        double y;
        double z;
        schedule.TargetAfterTick(schedule.Ticks, out x, out y, out z);
        Assert.Equal(step.ToX, x, 12);
        Assert.Equal(step.ToZ, z, 12);
        Assert.True(y <= step.ToY - FlightSchedule.MinLandingPushBlocks + 1e-12, "y " + y);
        // Asking past the end gives the same point.
        double x2;
        double y2;
        double z2;
        schedule.TargetAfterTick(schedule.Ticks + 3, out x2, out y2, out z2);
        Assert.Equal(y, y2, 12);
        Assert.Equal(step.FlightTicks, schedule.NaturalTicksAt(schedule.Ticks + 3), 12);
    }

    [Fact]
    public void SpedUpFlightsTakeTheNaturalTimeDividedByTheMultiplier()
    {
        double[] multipliers = new double[] { 1.45, 1.2, 2, 3 };
        for (int dy = -6; dy <= 4; dy += 2)
        {
            for (int distance = 1; distance <= 6; distance++)
            {
                JumpStep step = Jump(distance, dy, 0);
                for (int i = 0; i < multipliers.Length; i++)
                {
                    FlightSchedule schedule = FlightSchedule.Create(step, multipliers[i]);
                    double ideal = step.FlightTicks / multipliers[i];
                    string what = "jump (" + distance + ", " + dy + ") x" + multipliers[i] + ": " + schedule.Ticks + " ticks for " + step.FlightTicks;
                    Assert.True(Math.Abs(schedule.Ticks - ideal) <= 0.5 + 1e-9 || schedule.Ticks == 2, what);

                    // The last tick covers at most one natural tick, as JumpPlanner's footing check assumes.
                    double lastSpan = schedule.NaturalTicksAt(schedule.Ticks) - schedule.NaturalTicksAt(schedule.Ticks - 1);
                    Assert.True(lastSpan <= 1 + 1e-9, what + ": last tick covers " + lastSpan);
                }
            }
        }
    }

    [Fact]
    public void SlowerThanNaturalSpreadsTheArcEvenly()
    {
        JumpStep step = Jump(4, 0, 0);
        FlightSchedule schedule = FlightSchedule.Create(step, 0.5);
        int ticks = step.FlightTicks * 2;
        Assert.Equal(ticks, schedule.Ticks);
        // All but the last natural tick spread over all but the last tick, then the last natural tick in one go.
        Assert.Equal((step.FlightTicks - 1.0) / (ticks - 1), schedule.NaturalTicksAt(1), 12);
        Assert.Equal(step.FlightTicks - 1.0, schedule.NaturalTicksAt(ticks - 1), 12);
        Assert.Equal(step.FlightTicks, schedule.NaturalTicksAt(ticks), 12);
    }
}
