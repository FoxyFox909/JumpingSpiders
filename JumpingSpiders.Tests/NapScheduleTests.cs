using Xunit;

namespace JumpingSpiders.Tests;

public class NapScheduleTests
{
    [Fact]
    public void DayAndHour()
    {
        Assert.Equal(0, NapSchedule.DayIndex(23.9, 24));
        Assert.Equal(1, NapSchedule.DayIndex(24, 24));
        Assert.Equal(3, NapSchedule.DayIndex(24 * 3 + 7.5, 24));
        Assert.Equal(7.5, NapSchedule.HourOfDay(24 * 3 + 7.5, 24), 6);
    }

    [Fact]
    public void SlotsAreShifted()
    {
        // Two-hour slots starting 0.75 h after midnight.
        Assert.Equal(-1, NapSchedule.SlotIndex(0.5, 0.75, 2));
        Assert.Equal(0, NapSchedule.SlotIndex(0.75, 0.75, 2));
        Assert.Equal(0, NapSchedule.SlotIndex(2.7, 0.75, 2));
        Assert.Equal(1, NapSchedule.SlotIndex(2.75, 0.75, 2));
        Assert.Equal(5, NapSchedule.SlotIndex(12, 0.75, 2));
        Assert.Equal(11, NapSchedule.SlotIndex(23.9, 0.75, 2));
        Assert.Equal(10.75, NapSchedule.SlotStartHour(5, 0.75, 2), 6);
    }

    [Fact]
    public void DifferentShiftsRollAtDifferentTimes()
    {
        // At 9:30 a spider with no shift is in slot 4 (8:00 to 10:00), one shifted by 1.6 h in slot 3 (7:36 to 9:36);
        // at 9:40 the shifted one has moved on to slot 4 and rolls again, the other has not.
        Assert.Equal(4, NapSchedule.SlotIndex(9.5, 0, 2));
        Assert.Equal(3, NapSchedule.SlotIndex(9.5, 1.6, 2));
        Assert.Equal(4, NapSchedule.SlotIndex(9.67, 0, 2));
        Assert.Equal(4, NapSchedule.SlotIndex(9.67, 1.6, 2));
    }

    [Fact]
    public void ChanceFallsWithCuriosity()
    {
        Assert.Equal(0.3, NapSchedule.NapChance(0.2, 0.2, 1.0, 0.3), 6);
        Assert.Equal(0.15, NapSchedule.NapChance(0.6, 0.2, 1.0, 0.3), 6);
        Assert.Equal(0.0, NapSchedule.NapChance(1.0, 0.2, 1.0, 0.3), 6);
        Assert.True(NapSchedule.NapChance(0.3, 0.2, 1.0, 0.3) > NapSchedule.NapChance(0.5, 0.2, 1.0, 0.3));
    }

    [Fact]
    public void ChanceIsClamped()
    {
        Assert.Equal(0.3, NapSchedule.NapChance(0.0, 0.2, 1.0, 0.3), 6);
        Assert.Equal(0.0, NapSchedule.NapChance(1.2, 0.2, 1.0, 0.3), 6);
        Assert.Equal(0.0, NapSchedule.NapChance(0.2, 0.2, 1.0, 0), 6);
        Assert.Equal(1.0, NapSchedule.NapChance(0.2, 0.2, 1.0, 3), 6);
        // No curiosity range: 1 - curiosity.
        Assert.Equal(0.15, NapSchedule.NapChance(0.5, 0.5, 0.5, 0.3), 6);
    }

    [Fact]
    public void ExpectedNapsPerDayMatchTheConfigComment()
    {
        // About six daylight slots per day.
        Assert.Equal(1.8, 6 * NapSchedule.NapChance(0.2, 0.2, 1.0, 0.3), 6);
        Assert.Equal(0.9, 6 * NapSchedule.NapChance(0.6, 0.2, 1.0, 0.3), 6);
    }

    [Fact]
    public void NapLength()
    {
        Assert.Equal(0.5, NapSchedule.NapLengthHours(0, 30, 60), 6);
        Assert.Equal(1.0, NapSchedule.NapLengthHours(1, 30, 60), 6);
        Assert.Equal(0.75, NapSchedule.NapLengthHours(0.5, 30, 60), 6);
        Assert.Equal(0.5, NapSchedule.NapLengthHours(0.7, 30, 10), 6);
    }

    [Fact]
    public void OneRollPerSlot()
    {
        Assert.False(NapSchedule.IsNewSlot(10, 3, 10, 3));
        Assert.True(NapSchedule.IsNewSlot(10, 4, 10, 3));
        Assert.True(NapSchedule.IsNewSlot(11, 3, 10, 3));
        Assert.True(NapSchedule.IsNewSlot(0, 0, int.MinValue, int.MinValue));
    }
}
