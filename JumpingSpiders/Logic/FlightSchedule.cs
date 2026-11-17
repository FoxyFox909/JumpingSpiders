using System;

namespace JumpingSpiders;

/// <summary>
/// The timetable of one jump as the spider really flies it: where its feet must be at the end of each physics tick
/// so that it follows the planned arc (a JumpStep, worked out for the game's natural gravity) faster or slower than
/// the game's physics would carry it. JumpController steers the spider onto these points tick by tick.
///
/// Why a timetable and not just a stronger launch: the game's gravity is fixed, so a spider launched 1.45 times as
/// fast would fly a different, higher and longer arc. Following the same arc in less time needs the velocities times
/// the speed multiplier and the downward pull times its square, which only steering every tick can give.
///
/// The game moves entities in whole physics ticks of 1/30 s and can only notice a landing at the end of one, so:
/// - The flight takes DrivenTicks ticks: the natural flight's tick count divided by the multiplier, rounded to the
///   nearest whole tick (at least 2 when the natural flight has 2 or more, so the spider is in the air for at least
///   one tick end).
/// - The last tick covers the last natural tick of the arc, the one in which the natural flight comes down. The
///   engine moves an entity down before it moves it sideways, so in its last tick a spider comes down where it was
///   at the start of that tick, and JumpPlanner only checks for footing up to one natural tick (and a bit) before
///   the landing point. The ticks before share the rest of the arc evenly, so with 1.45 the spider is a little
///   faster than 1.45 times while in the air and slows to natural speed for its last tick. With a multiplier of 1
///   the timetable is exactly the natural flight.
/// - The last point is MinLandingPushBlocks below the landing height (or lower if the natural arc is): the floor
///   stops the spider there and the game then reports it as on the ground.
///
/// Pure C#, covered by unit tests.
/// </summary>
public class FlightSchedule
{
    /// <summary>
    /// The last point of a flight is at least this far (blocks) below the landing height, so that the move really
    /// runs into the floor; just touching it would not count as landing in the game.
    /// </summary>
    public const double MinLandingPushBlocks = 0.05;

    /// <summary>The planned jump this timetable flies.</summary>
    public JumpStep Step;

    /// <summary>How many times as fast as the natural flight (the config value, or 1 if that was not a positive number).</summary>
    public double SpeedMultiplier;

    /// <summary>How many physics ticks the flight takes, from launch to the tick it lands in.</summary>
    public int Ticks;

    /// <summary>The same in seconds.</summary>
    public double FlightSeconds;

    // For each tick end 0..Ticks: how far along the natural flight that is, in natural physics ticks.
    private double[] naturalTicksAt;

    /// <summary>The multiplier actually used: 1 when the given one is not a positive number.</summary>
    public static double SanitizeMultiplier(double multiplier)
    {
        if (double.IsNaN(multiplier) || double.IsInfinity(multiplier) || multiplier <= 0)
        {
            return 1;
        }
        return multiplier;
    }

    /// <summary>How many physics ticks a jump whose natural flight takes naturalTicks lasts at this speed multiplier.</summary>
    public static int DrivenTicks(int naturalTicks, double multiplier)
    {
        if (naturalTicks <= 1)
        {
            // A one-tick hop (a step down off an edge, say) cannot be split up.
            return Math.Max(naturalTicks, 0);
        }
        double m = SanitizeMultiplier(multiplier);
        int ticks = (int)Math.Round(naturalTicks / m, MidpointRounding.AwayFromZero);
        int fewest = Math.Min(naturalTicks, 2);
        if (ticks < fewest)
        {
            ticks = fewest;
        }
        return ticks;
    }

    /// <summary>DrivenTicks in seconds: how long the spider is in the air on that jump.</summary>
    public static double DrivenSeconds(int naturalTicks, double multiplier)
    {
        return DrivenTicks(naturalTicks, multiplier) * JumpMath.PhysicsTickSeconds;
    }

    /// <summary>The timetable for flying step at multiplier times its natural speed.</summary>
    public static FlightSchedule Create(JumpStep step, double multiplier)
    {
        FlightSchedule schedule = new FlightSchedule();
        schedule.Step = step;
        schedule.SpeedMultiplier = SanitizeMultiplier(multiplier);
        int naturalTicks = step.FlightTicks;
        int ticks = DrivenTicks(naturalTicks, schedule.SpeedMultiplier);
        schedule.Ticks = ticks;
        schedule.FlightSeconds = ticks * JumpMath.PhysicsTickSeconds;
        schedule.naturalTicksAt = new double[ticks + 1];
        schedule.naturalTicksAt[0] = 0;
        if (ticks == 0)
        {
            return schedule;
        }

        // The last tick covers the last natural tick (the one the natural flight lands in); the ticks before share
        // the rest of the arc evenly. With a multiplier of 1 every tick covers exactly one natural tick.
        double share = 0;
        if (ticks > 1)
        {
            share = (double)(naturalTicks - 1) / (ticks - 1);
        }
        for (int tick = 1; tick < ticks; tick++)
        {
            schedule.naturalTicksAt[tick] = tick * share;
        }
        schedule.naturalTicksAt[ticks] = naturalTicks;
        return schedule;
    }

    /// <summary>
    /// How far along the natural flight the spider is at the end of physics tick number tick (0 = launch),
    /// in natural physics ticks. After the last tick it stays at the end.
    /// </summary>
    public double NaturalTicksAt(int tick)
    {
        if (tick <= 0)
        {
            return 0;
        }
        if (tick >= Ticks)
        {
            return naturalTicksAt[Ticks];
        }
        return naturalTicksAt[tick];
    }

    /// <summary>
    /// Where the feet should be at the end of physics tick number tick (0 = the take-off point). The last tick's
    /// point is the landing point pushed down into the floor (see MinLandingPushBlocks); later ticks give the same.
    /// </summary>
    public void TargetAfterTick(int tick, out double x, out double y, out double z)
    {
        if (tick <= 0)
        {
            x = Step.FromX;
            y = Step.FromY;
            z = Step.FromZ;
            return;
        }
        if (tick >= Ticks)
        {
            double arcEnd = JumpMath.RelativeHeightAtTicks(Step, Step.FlightTicks);
            double pushed = Step.ToY - Step.FromY - MinLandingPushBlocks;
            x = Step.ToX;
            y = Step.FromY + Math.Min(arcEnd, pushed);
            z = Step.ToZ;
            return;
        }
        double naturalTicks = naturalTicksAt[tick];
        double seconds = naturalTicks * JumpMath.PhysicsTickSeconds;
        x = Step.FromX + Step.VX * seconds;
        y = Step.FromY + JumpMath.RelativeHeightAtTicks(Step, naturalTicks);
        z = Step.FromZ + Step.VZ * seconds;
    }
}
