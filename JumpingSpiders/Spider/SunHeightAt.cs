using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace JumpingSpiders;

/// <summary>
/// The game's sun at one spot of the world, for the pure StayUpAfterDusk logic (which looks at the sun at other
/// times than now to find when it set).
///
/// The calendar's GetSunPosition gives the direction of the sun at any moment (in calendar days, the unit of
/// Calendar.TotalDays) and place: its Y is above 0 while the sun is above the horizon. The survival mod works it out
/// from latitude, season and time of day, so nights are longer in winter and towards the poles.
/// SpiderAttributes.IsNight uses the same test for "now".
/// </summary>
public class SunHeightAt : ISunHeight
{
    private readonly IGameCalendar calendar;
    private readonly Vec3d pos;

    public SunHeightAt(IGameCalendar calendar, Vec3d pos)
    {
        this.calendar = calendar;
        this.pos = pos;
    }

    public double GetSunHeight(double totalDays)
    {
        return calendar.GetSunPosition(pos, totalDays).Y;
    }
}
