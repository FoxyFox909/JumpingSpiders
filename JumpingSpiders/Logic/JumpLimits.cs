namespace JumpingSpiders;

/// <summary>
/// The physical jumping ability of a spider, as plain numbers.
/// This is a copy of the jump fields of JumpingSpidersConfig, so that the code in the Logic folder
/// (and its unit tests) does not depend on the config class or on the game.
/// Build one with JumpingSpidersConfig.CreateJumpLimits(), or fill the fields by hand in tests.
/// </summary>
public class JumpLimits
{
    /// <summary>Preferred launch angle above the horizontal, in degrees. Tried first.</summary>
    public double JumpAngleDeg = 50;

    /// <summary>Flattest launch angle the solver may fall back to, in degrees.</summary>
    public double MinJumpAngleDeg = 20;

    /// <summary>Steepest launch angle the solver may fall back to, in degrees.</summary>
    public double MaxJumpAngleDeg = 80;

    /// <summary>
    /// Launch speed cap, in blocks per second. 18 is about the least that reaches MaxJumpUp = 4 blocks:
    /// the game's gravity is strong (about 22 blocks/s^2), so even straight up 14 b/s only reaches 3.4 blocks.
    /// </summary>
    public double MaxJumpSpeed = 18;

    /// <summary>Highest landing point above the take-off point, in blocks.</summary>
    public double MaxJumpUp = 4;

    /// <summary>Deepest landing point below the take-off point, in blocks.</summary>
    public double MaxJumpDown = 6;

    /// <summary>Longest horizontal distance of one jump, in blocks.</summary>
    public double MaxJumpHorizontal = 6;

    /// <summary>
    /// When jumping up, how far above the landing height the arc must peak, in blocks. Without it the solver finds
    /// arcs that only just reach a ledge at their highest point and would graze its edge.
    /// </summary>
    public double LedgeClearance = 0.4;

    /// <summary>How many jumps the planner may chain to reach a goal (2 = one intermediate perch).</summary>
    public int MaxPlannedJumps = 2;

    /// <summary>Longest allowed flight, in seconds. Also the safety timeout of a jump in the game.</summary>
    public double MaxFlightSeconds = 3;
}
