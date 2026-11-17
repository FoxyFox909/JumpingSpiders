using System;

namespace JumpingSpiders;

/// <summary>
/// The maths of daytime naps. Pure logic, no game types, so it is covered by unit tests.
///
/// The day is cut into slots of Config.NapSlotHours in-game hours. The slots of one spider are shifted by a random
/// amount (0 to one slot long) that is rolled at the start of each in-game day, so spiders do not all roll at the same
/// moment. Slot number k of a day runs from hour shift + k * slotHours to shift + (k + 1) * slotHours; the hours before
/// the shift are slot -1. Once per slot, by day, the spider rolls for a nap with NapChance; a won roll gives a nap of
/// NapLengthHours. The sleep task (AiTaskSpiderSleep) does the rolling; SpiderAttributes remembers the last slot rolled
/// and the day's shift on the entity, so a chunk reload or a restart never gives a second roll in the same slot.
/// </summary>
public static class NapSchedule
{
    /// <summary>The in-game day number (0 = the world's first day) at totalHours.</summary>
    public static int DayIndex(double totalHours, double hoursPerDay)
    {
        if (hoursPerDay <= 0)
        {
            return 0;
        }
        return (int)Math.Floor(totalHours / hoursPerDay);
    }

    /// <summary>The hour of the day (0 up to hoursPerDay) at totalHours.</summary>
    public static double HourOfDay(double totalHours, double hoursPerDay)
    {
        if (hoursPerDay <= 0)
        {
            return 0;
        }
        return totalHours - DayIndex(totalHours, hoursPerDay) * hoursPerDay;
    }

    /// <summary>
    /// The slot number of an hour of the day, for slots slotHours long starting shiftHours after midnight. The hours
    /// before the shift are slot -1.
    /// </summary>
    public static int SlotIndex(double hourOfDay, double shiftHours, double slotHours)
    {
        if (slotHours <= 0)
        {
            return 0;
        }
        return (int)Math.Floor((hourOfDay - shiftHours) / slotHours);
    }

    /// <summary>The hour of the day at which slot number slot begins.</summary>
    public static double SlotStartHour(int slot, double shiftHours, double slotHours)
    {
        return shiftHours + slot * slotHours;
    }

    /// <summary>
    /// The chance (0..1) to nap in one slot, for a spider with this curiosity for the day:
    /// chanceMax * (curiosityMax - curiosity) / (curiosityMax - curiosityMin), clamped to 0..chanceMax.
    /// The least curious spider (curiosityMin) gets chanceMax, the most curious (curiosityMax) 0, and in between it falls
    /// in a straight line. With an empty curiosity range (min not below max) it is chanceMax * (1 - curiosity).
    /// </summary>
    public static double NapChance(double curiosity, double curiosityMin, double curiosityMax, double chanceMax)
    {
        if (chanceMax <= 0)
        {
            return 0;
        }
        double share;
        if (curiosityMax > curiosityMin)
        {
            share = (curiosityMax - curiosity) / (curiosityMax - curiosityMin);
        }
        else
        {
            share = 1 - curiosity;
        }
        share = Math.Max(0, Math.Min(1, share));
        return Math.Min(1, chanceMax * share);
    }

    /// <summary>
    /// A nap length in in-game hours from a random number (0..1): minMinutes to maxMinutes in-game minutes, every length
    /// equally likely. A maxMinutes below minMinutes counts as minMinutes.
    /// </summary>
    public static double NapLengthHours(double random01, double minMinutes, double maxMinutes)
    {
        if (maxMinutes < minMinutes)
        {
            maxMinutes = minMinutes;
        }
        double minutes = minMinutes + random01 * (maxMinutes - minMinutes);
        return minutes / 60.0;
    }

    /// <summary>True when (day, slot) is not the slot rolled last, so the spider may roll now.</summary>
    public static bool IsNewSlot(int day, int slot, int lastDay, int lastSlot)
    {
        return day != lastDay || slot != lastSlot;
    }
}
