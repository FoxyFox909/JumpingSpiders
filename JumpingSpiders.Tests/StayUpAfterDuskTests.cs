using System;
using Xunit;

namespace JumpingSpiders.Tests;

public class StayUpAfterDuskTests
{
    private const double HoursPerDay = 24;
    private const double NoNightLimit = 1;

    // A sun that sets at exactly 18:00 and rises at 6:00.
    private static TestSun EvenSun()
    {
        return new TestSun(0, 0);
    }

    // A night of 2 hours around midnight: cos(hour angle) must stay below -Offset, which it does for one hour each
    // side of midnight when Offset = cos(15 degrees).
    private static TestSun ShortNightSun()
    {
        return new TestSun(Math.Cos(Math.PI / 12), 0);
    }

    [Fact]
    public void StayUpHoursScalesWithCuriosity()
    {
        Assert.Equal(1.5, StayUpAfterDusk.StayUpHours(0.5, 3), 6);
        Assert.Equal(3.0, StayUpAfterDusk.StayUpHours(1.0, 3), 6);
        Assert.Equal(0.0, StayUpAfterDusk.StayUpHours(1.0, 0), 6);
        Assert.Equal(0.0, StayUpAfterDusk.StayUpHours(0, 3), 6);
    }

    [Fact]
    public void FindsSunsetAndSunrise()
    {
        TestSun sun = EvenSun();
        Assert.Equal(2.0, StayUpAfterDusk.HoursSinceSunset(sun, TestSun.At(10, 20), HoursPerDay, 3), 2);
        Assert.Equal(10.0, StayUpAfterDusk.HoursUntilSunrise(sun, TestSun.At(10, 20), HoursPerDay, 12), 2);
        // The sun set 5 hours ago, more than the 3 searched.
        Assert.Equal(StayUpAfterDusk.NotFound, StayUpAfterDusk.HoursSinceSunset(sun, TestSun.At(10, 23), HoursPerDay, 3));
        // By day both are 0.
        Assert.Equal(0.0, StayUpAfterDusk.HoursSinceSunset(sun, TestSun.At(10, 12), HoursPerDay, 3));
        Assert.Equal(0.0, StayUpAfterDusk.HoursUntilSunrise(sun, TestSun.At(10, 12), HoursPerDay, 3));
    }

    [Fact]
    public void StaysUpForItsHoursAfterDuskThenGoesToBed()
    {
        TestSun sun = EvenSun();
        Assert.True(StayUpAfterDusk.IsStayingUp(sun, TestSun.At(10, 18.2), HoursPerDay, 3, 0.5));
        Assert.True(StayUpAfterDusk.IsStayingUp(sun, TestSun.At(10, 20.9), HoursPerDay, 3, 0.5));
        Assert.False(StayUpAfterDusk.IsStayingUp(sun, TestSun.At(10, 21.1), HoursPerDay, 3, 0.5));
        Assert.False(StayUpAfterDusk.IsStayingUp(sun, TestSun.At(10, 23), HoursPerDay, 3, 0.5));
        Assert.False(StayUpAfterDusk.IsStayingUp(sun, TestSun.At(11, 4), HoursPerDay, 3, 0.5));
    }

    [Fact]
    public void LessCuriousSpidersGoToBedSooner()
    {
        TestSun sun = EvenSun();
        double lowCuriosityHours = StayUpAfterDusk.StayUpHours(0.2, 3);
        double highCuriosityHours = StayUpAfterDusk.StayUpHours(1.0, 3);
        double now = TestSun.At(10, 19);
        Assert.False(StayUpAfterDusk.IsStayingUp(sun, now, HoursPerDay, lowCuriosityHours, 0.5));
        Assert.True(StayUpAfterDusk.IsStayingUp(sun, now, HoursPerDay, highCuriosityHours, 0.5));
    }

    [Fact]
    public void NothingToStayUpForByDay()
    {
        Assert.False(StayUpAfterDusk.IsStayingUp(EvenSun(), TestSun.At(10, 12), HoursPerDay, 3, 0.5));
        Assert.False(StayUpAfterDusk.IsStayingUp(EvenSun(), TestSun.At(10, 6.5), HoursPerDay, 3, 0.5));
    }

    [Fact]
    public void WindowCarriesOnPastMidnight()
    {
        // Sun shifted by 5 hours: it sets at 23:00.
        TestSun sun = new TestSun(0, 5);
        Assert.True(StayUpAfterDusk.IsStayingUp(sun, TestSun.At(10, 23.5), HoursPerDay, 3, 0.5));
        Assert.True(StayUpAfterDusk.IsStayingUp(sun, TestSun.At(11, 1.5), HoursPerDay, 3, 0.5));
        Assert.False(StayUpAfterDusk.IsStayingUp(sun, TestSun.At(11, 2.5), HoursPerDay, 3, 0.5));
    }

    [Fact]
    public void ShortNightIsOnlyPartlySpentAwake()
    {
        // Night from 23:00 to 1:00. With half the night allowed, a 3-hour window shrinks to 1 hour.
        TestSun sun = ShortNightSun();
        Assert.True(StayUpAfterDusk.IsStayingUp(sun, TestSun.At(10, 23.5), HoursPerDay, 3, 0.5));
        Assert.False(StayUpAfterDusk.IsStayingUp(sun, TestSun.At(11, 0.2), HoursPerDay, 3, 0.5));
        // Without the limit it would stay up the whole night.
        Assert.True(StayUpAfterDusk.IsStayingUp(sun, TestSun.At(11, 0.9), HoursPerDay, 3, NoNightLimit));
    }

    [Fact]
    public void LongNightDoesNotCutTheWindow()
    {
        // Night of about 20 hours: half of it is far more than the 3-hour window.
        TestSun sun = new TestSun(-0.8, 0);
        double sunset = 12 + 24 * Math.Acos(0.8) / (2 * Math.PI);
        Assert.True(StayUpAfterDusk.IsStayingUp(sun, TestSun.At(10, sunset + 2.9), HoursPerDay, 3, 0.5));
        Assert.False(StayUpAfterDusk.IsStayingUp(sun, TestSun.At(10, sunset + 3.1), HoursPerDay, 3, 0.5));
    }

    [Fact]
    public void PolarNightAndPolarDayNeverKeepItUp()
    {
        TestSun neverRises = new TestSun(-2, 0);
        TestSun neverSets = new TestSun(2, 0);
        for (double hour = 0; hour < 24; hour += 1.5)
        {
            Assert.False(StayUpAfterDusk.IsStayingUp(neverRises, TestSun.At(10, hour), HoursPerDay, 3, 0.5));
            Assert.False(StayUpAfterDusk.IsStayingUp(neverSets, TestSun.At(10, hour), HoursPerDay, 3, 0.5));
        }
    }

    [Fact]
    public void ZeroKnobsTurnItOff()
    {
        double now = TestSun.At(10, 18.5);
        Assert.False(StayUpAfterDusk.IsStayingUp(EvenSun(), now, HoursPerDay, 0, 0.5));
        Assert.False(StayUpAfterDusk.IsStayingUp(EvenSun(), now, HoursPerDay, 3, 0));
        Assert.False(StayUpAfterDusk.IsStayingUp(null, now, HoursPerDay, 3, 0.5));
    }
}
