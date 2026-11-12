using System;

namespace JumpingSpiders;

/// <summary>
/// Picks the point a fleeing spider heads for: straight away from the threat, a given distance from the spider.
/// AiTaskSpiderJumpFlee uses it as the centre of the area where it looks for a landing spot, and as the point it
/// runs towards when no jump is possible. Pure C# (no game types), covered by unit tests.
/// </summary>
public static class FleeDirection
{
    /// <summary>Closer than this (blocks, horizontally) the threat gives no direction, and fallbackYaw is used.</summary>
    public const double MinUsefulDistance = 0.01;

    /// <summary>
    /// The point distance blocks from the spider, on the far side from the threat (horizontal plane only).
    /// When the threat stands right on the spider, the direction fallbackYaw is used instead, in the game's yaw
    /// convention (radians, 0 = towards +Z, x = sin(yaw), z = cos(yaw)); pass the spider's own yaw.
    /// </summary>
    public static void AwayPoint(double spiderX, double spiderZ, double threatX, double threatZ, double distance,
        double fallbackYaw, out double x, out double z)
    {
        double dx = spiderX - threatX;
        double dz = spiderZ - threatZ;
        double length = Math.Sqrt(dx * dx + dz * dz);
        if (length < MinUsefulDistance)
        {
            dx = Math.Sin(fallbackYaw);
            dz = Math.Cos(fallbackYaw);
            length = 1;
        }
        x = spiderX + dx / length * distance;
        z = spiderZ + dz / length * distance;
    }
}
