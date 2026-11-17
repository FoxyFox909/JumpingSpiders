namespace JumpingSpiders;

/// <summary>
/// When a domesticated spider makes its next hammock build attempt. Pure logic, no game types, so it is covered by
/// unit tests.
///
/// The rule: a kill "arms" building (one hammock is owed for that kill). While armed and sated, the spider makes
/// one attempt per Config.HammockBuildIntervalHours in-game hours, until one succeeds, which disarms it until the next
/// kill. The time of the next attempt is saved on the entity (SpiderAttributes), so a spider whose chunk was
/// unloaded for a day, or a server restart, gives one attempt when it is back, not one for every hour it missed.
///
/// AiTaskSpiderBuildHammock asks IsAttemptDue in its ShouldExecute and calls NextAttemptHours when an attempt starts.
/// </summary>
public static class HammockBuildSchedule
{
    /// <summary>
    /// True when a build attempt should start now: building is armed, the spider is sated, and the time of the next
    /// attempt (nextAttemptHours, in-game total hours; 0 when none was saved yet) has come. A next attempt more than one
    /// interval away means the calendar went back (someone set the time to earlier), so it counts as due rather than
    /// making the spider wait longer than an interval.
    /// </summary>
    public static bool IsAttemptDue(bool armed, bool sated, double nowHours, double nextAttemptHours, double intervalHours)
    {
        if (!armed || !sated)
        {
            return false;
        }
        if (nowHours >= nextAttemptHours)
        {
            return true;
        }
        return nextAttemptHours - nowHours > intervalHours;
    }

    /// <summary>The time (in-game total hours) of the attempt after one that starts at nowHours.</summary>
    public static double NextAttemptHours(double nowHours, double intervalHours)
    {
        if (intervalHours <= 0)
        {
            return nowHours;
        }
        return nowHours + intervalHours;
    }
}
