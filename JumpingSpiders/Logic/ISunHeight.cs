namespace JumpingSpiders;

/// <summary>
/// What StayUpAfterDusk needs to know about the sun: how high it stands at a given moment. In the game this is
/// SunHeightAt (the game's calendar, at one spot of the world); the unit tests use a made-up sun.
/// </summary>
public interface ISunHeight
{
    /// <summary>
    /// How high the sun stands at the given time (in calendar days, like the game's Calendar.TotalDays): above 0 the
    /// sun is up, below 0 it is night. Only the sign matters to the callers.
    /// </summary>
    double GetSunHeight(double totalDays);
}
