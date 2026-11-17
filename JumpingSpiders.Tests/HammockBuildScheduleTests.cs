using Xunit;

namespace JumpingSpiders.Tests;

public class HammockBuildScheduleTests
{
    [Fact]
    public void OnlyWhenArmedAndSated()
    {
        Assert.False(HammockBuildSchedule.IsAttemptDue(false, true, 100, 0, 1));
        Assert.False(HammockBuildSchedule.IsAttemptDue(true, false, 100, 0, 1));
        Assert.True(HammockBuildSchedule.IsAttemptDue(true, true, 100, 0, 1));
    }

    [Fact]
    public void OneAttemptPerInterval()
    {
        double next = HammockBuildSchedule.NextAttemptHours(100, 1);
        Assert.Equal(101, next, 6);
        Assert.False(HammockBuildSchedule.IsAttemptDue(true, true, 100.5, next, 1));
        Assert.False(HammockBuildSchedule.IsAttemptDue(true, true, 100.99, next, 1));
        Assert.True(HammockBuildSchedule.IsAttemptDue(true, true, 101, next, 1));
    }

    [Fact]
    public void ALongAbsenceGivesOneAttemptNotABurst()
    {
        // Unloaded for 30 hours: one attempt is due, and the one after that is an hour after it, not at once.
        double next = 101;
        double now = 131;
        Assert.True(HammockBuildSchedule.IsAttemptDue(true, true, now, next, 1));
        next = HammockBuildSchedule.NextAttemptHours(now, 1);
        Assert.False(HammockBuildSchedule.IsAttemptDue(true, true, now + 0.01, next, 1));
        Assert.True(HammockBuildSchedule.IsAttemptDue(true, true, now + 1, next, 1));
    }

    [Fact]
    public void TimeSetBackwardsDoesNotBlockForLong()
    {
        // The next attempt was planned for hour 101, then the time was set back to hour 50.
        Assert.True(HammockBuildSchedule.IsAttemptDue(true, true, 50, 101, 1));
        // Half an hour back is just "not yet".
        Assert.False(HammockBuildSchedule.IsAttemptDue(true, true, 100.5, 101, 1));
    }

    [Fact]
    public void ZeroIntervalMeansEveryCheck()
    {
        Assert.Equal(100, HammockBuildSchedule.NextAttemptHours(100, 0), 6);
        Assert.True(HammockBuildSchedule.IsAttemptDue(true, true, 100, 100, 0));
    }
}
