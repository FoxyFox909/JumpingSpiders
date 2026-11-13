namespace JumpingSpiders;

/// <summary>
/// One planned jump: where it starts, where it lands and how fast the spider leaves the ground.
/// Filled in by JumpMath.SolveLaunch and handed to JumpController.Launch in the game.
///
/// Positions are "feet positions": the bottom centre of the spider's collision box, in blocks.
/// Velocities are in blocks per second. VX and VZ are the horizontal speed the spider really
/// travels at (JumpController keeps it constant during the flight); VY is the vertical launch speed,
/// after which gravity and air drag take over.
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

    /// <summary>Time from take-off to touching down, in seconds. Always a whole number of physics ticks.</summary>
    public double FlightSeconds;

    /// <summary>The same time counted in physics ticks (1/30 s each).</summary>
    public int FlightTicks;
}
