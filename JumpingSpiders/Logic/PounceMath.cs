using System;

namespace JumpingSpiders;

/// <summary>
/// Small sums for the pounce: where to aim at a moving target, and whether the spider touched it.
/// Used by AiTaskSpiderJumpAttack. Pure C# (no game types), covered by unit tests.
/// </summary>
public static class PounceMath
{
    /// <summary>
    /// Where a target will be after flightSeconds if it keeps moving in a straight line, scaled by leadFactor
    /// (0 = aim where it is now, 1 = aim fully ahead). One axis at a time: position in blocks, speed in blocks per second.
    /// </summary>
    public static double LeadPosition(double position, double speedPerSecond, double flightSeconds, double leadFactor)
    {
        return position + speedPerSecond * flightSeconds * leadFactor;
    }

    /// <summary>
    /// The shortest distance between two axis-aligned boxes, in blocks; 0 when they touch or overlap.
    /// Each box is given by its lower corner (x1, y1, z1) and upper corner (x2, y2, z2).
    /// </summary>
    public static double BoxGap(double ax1, double ay1, double az1, double ax2, double ay2, double az2,
        double bx1, double by1, double bz1, double bx2, double by2, double bz2)
    {
        double gapX = AxisGap(ax1, ax2, bx1, bx2);
        double gapY = AxisGap(ay1, ay2, by1, by2);
        double gapZ = AxisGap(az1, az2, bz1, bz2);
        return Math.Sqrt(gapX * gapX + gapY * gapY + gapZ * gapZ);
    }

    // How far apart two ranges [a1, a2] and [b1, b2] are on one axis (0 when they overlap).
    private static double AxisGap(double a1, double a2, double b1, double b2)
    {
        if (a2 < b1)
        {
            return b1 - a2;
        }
        if (b2 < a1)
        {
            return a1 - b2;
        }
        return 0;
    }
}
