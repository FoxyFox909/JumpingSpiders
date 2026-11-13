using System;

namespace JumpingSpiders.Tests;

/// <summary>
/// A made-up sun for StayUpAfterDusk tests, on a 24-hour day: height = cos(hour angle) + Offset, highest at noon
/// plus ShiftHours. Offset 0 gives sunrise at 6:00 and sunset at 18:00; a positive Offset shortens the night, a
/// negative one lengthens it (below -1 the sun never rises, above 1 it never sets).
/// </summary>
public class TestSun : ISunHeight
{
    public double Offset;
    public double ShiftHours;

    public TestSun(double offset, double shiftHours)
    {
        Offset = offset;
        ShiftHours = shiftHours;
    }

    public double GetSunHeight(double totalDays)
    {
        double hourOfDay = (totalDays - Math.Floor(totalDays)) * 24;
        double hourAngle = 2 * Math.PI * (hourOfDay - 12 - ShiftHours) / 24;
        return Math.Cos(hourAngle) + Offset;
    }

    /// <summary>The calendar time (in days) of the given hour on the given day.</summary>
    public static double At(int day, double hour)
    {
        return day + hour / 24;
    }
}
