namespace JumpingSpiders;

/// <summary>
/// One planned jump: where it starts, where it lands and how fast the spider leaves the ground.
/// Filled in by JumpMath.SolveLaunch and handed to JumpController.Launch in the game.
///
/// Positions are "feet positions": the bottom centre of the spider's collision box, in blocks.
/// Velocities and times describe the natural arc: the jump as the game's own gravity and air drag would fly it,
/// with VX and VZ held constant and VY the vertical launch speed. In the game the spider flies this same arc
/// Config.JumpFlightSpeedMultiplier times as fast (FlightSchedule has the real timing).
/// </summary>
public class JumpStep
{
    public double FromX;
    public double FromY;
    public double FromZ;

    public double ToX;
    public double ToY;
    public double ToZ;

    /// <summary>Horizontal velocity along X, blocks per second, held constant during the flight.</summary>
    public double VX;

    /// <summary>Vertical launch velocity, blocks per second (positive = up).</summary>
    public double VY;

    /// <summary>Horizontal velocity along Z, blocks per second, held constant during the flight.</summary>
    public double VZ;

    /// <summary>
    /// Time from take-off to touching down on the natural arc, in seconds. Always a whole number of physics ticks.
    /// The real flight is shorter: FlightSchedule.DrivenSeconds.
    /// </summary>
    public double FlightSeconds;

    /// <summary>The same time counted in physics ticks (1/30 s each), on the natural arc.</summary>
    public int FlightTicks;
}
