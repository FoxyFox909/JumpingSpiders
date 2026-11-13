using System;

namespace JumpingSpiders;

/// <summary>
/// Curious spiders stay up for a while after dusk before they go to bed. This class answers "is this spider still
/// within its stay-up time?" from the sun alone, without remembering anything per spider: it looks back in time
/// for the moment the sun set. That way the answer is right even when the spider's chunk was unloaded at dusk or
/// the server restarted in between.
///
/// The sleep task (AiTaskSpiderSleep, through SpiderAttributes.IsStayingUpAfterDusk) asks this before it sends a
/// spider to bed. How long a spider stays up comes from its daily curiosity (StayUpHours).
///
/// Corner cases, and what happens:
/// - Midnight: the search runs over the calendar's total time, so a window that spans midnight just carries on.
/// - Dawn: it is day, so there is nothing to stay up for (and the sleep task does not run by day anyway).
/// - A night shorter than the window: at most maxNightShare of the night is spent awake, so the spider still
///   sleeps for the rest of it, instead of staying up all night.
/// - A night with no dusk in reach (the sun stayed down for the whole window, as in a polar night): no stay-up.
/// </summary>
public static class StayUpAfterDusk
{
    /// <summary>Returned by the searches when the sun did not come up within the hours searched.</summary>
    public const double NotFound = -1;

    /// <summary>
    /// The searches step through time this many in-game hours at a time, then narrow the sunset or sunrise down
    /// exactly. A day shorter than this between two nights could be missed; that never happens in a normal world.
    /// </summary>
    public const double SearchStepHours = 0.1;

    /// <summary>How many halvings narrow down a sunset or sunrise once a step has found it (0.1 h / 2^12 is under a second).</summary>
    public const int BisectionSteps = 12;

    /// <summary>
    /// How long a spider with this curiosity stays up after dusk, in in-game hours: curiosity times maxHours.
    /// 0 when either is 0 or less.
    /// </summary>
    public static double StayUpHours(double curiosity, double maxHours)
    {
        if (curiosity <= 0 || maxHours <= 0)
        {
            return 0;
        }
        return curiosity * maxHours;
    }

    /// <summary>
    /// True when it is night now, the sun set less than stayUpHours ago, and less than maxNightShare (0..1) of this
    /// night has passed. Times are in calendar days (nowDays), lengths in in-game hours; hoursPerDay converts.
    /// A maxNightShare of 1 or more puts no limit on the share of the night.
    /// </summary>
    public static bool IsStayingUp(ISunHeight sun, double nowDays, double hoursPerDay, double stayUpHours, double maxNightShare)
    {
        if (sun == null || stayUpHours <= 0 || maxNightShare <= 0 || hoursPerDay <= 0)
        {
            return false;
        }
        if (IsSunUp(sun, nowDays))
        {
            return false;
        }
        double sinceSunset = HoursSinceSunset(sun, nowDays, hoursPerDay, stayUpHours);
        if (sinceSunset == NotFound)
        {
            return false;
        }

        double allowedHours = stayUpHours;
        if (maxNightShare < 1)
        {
            // Only a night shorter than this cuts the window, so the search for sunrise can stop there.
            double searchAhead = stayUpHours / maxNightShare - sinceSunset;
            double untilSunrise = HoursUntilSunrise(sun, nowDays, hoursPerDay, searchAhead);
            if (untilSunrise != NotFound)
            {
                double nightHours = sinceSunset + untilSunrise;
                allowedHours = Math.Min(allowedHours, maxNightShare * nightHours);
            }
        }
        return sinceSunset < allowedHours;
    }

    /// <summary>
    /// In-game hours since the sun last set, looking back at most searchHours. 0 when the sun is up now,
    /// NotFound when it stayed down for the whole search.
    /// </summary>
    public static double HoursSinceSunset(ISunHeight sun, double nowDays, double hoursPerDay, double searchHours)
    {
        return HoursUntilSunUp(sun, nowDays, hoursPerDay, searchHours, -1);
    }

    /// <summary>
    /// In-game hours until the sun next rises, looking ahead at most searchHours. 0 when the sun is up now,
    /// NotFound when it stays down for the whole search.
    /// </summary>
    public static double HoursUntilSunrise(ISunHeight sun, double nowDays, double hoursPerDay, double searchHours)
    {
        return HoursUntilSunUp(sun, nowDays, hoursPerDay, searchHours, 1);
    }

    /// <summary>The same test as SpiderAttributes.IsNight, the other way round: the sun is not below the horizon.</summary>
    public static bool IsSunUp(ISunHeight sun, double totalDays)
    {
        return !(sun.GetSunHeight(totalDays) < 0);
    }

    // Walks through time from nowDays (direction -1 = into the past, +1 = into the future) until the sun is up.
    private static double HoursUntilSunUp(ISunHeight sun, double nowDays, double hoursPerDay, double searchHours, int direction)
    {
        if (sun == null || hoursPerDay <= 0)
        {
            return NotFound;
        }
        if (IsSunUp(sun, nowDays))
        {
            return 0;
        }
        double downHours = 0;
        while (downHours < searchHours)
        {
            double probeHours = Math.Min(downHours + SearchStepHours, searchHours);
            if (IsSunUp(sun, nowDays + direction * probeHours / hoursPerDay))
            {
                // The sun crossed the horizon between downHours and probeHours: halve that gap a few times.
                double upHours = probeHours;
                for (int i = 0; i < BisectionSteps; i++)
                {
                    double middle = (downHours + upHours) / 2;
                    if (IsSunUp(sun, nowDays + direction * middle / hoursPerDay))
                    {
                        upHours = middle;
                    }
                    else
                    {
                        downHours = middle;
                    }
                }
                return upHours;
            }
            downHours = probeHours;
        }
        return NotFound;
    }
}
