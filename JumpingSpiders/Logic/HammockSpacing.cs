using System;

namespace JumpingSpiders;

/// <summary>
/// The "at most one hammock per 4 x 4 x 4 box" rule (the 4 is HammockSpacing in the config).
///
/// Two hammocks are too close when they are less than spacing blocks apart on every axis at once. This is the
/// strict reading of the rule: no 4 x 4 x 4 box anywhere, aligned to the grid or not, can then hold two hammocks.
/// Hammocks 4 blocks apart along any one axis are fine.
///
/// HammockFinder.HasHammockWithinSpacing (in the Hammock folder) uses this to scan the blocks around a spot.
/// </summary>
public static class HammockSpacing
{
    /// <summary>True when hammocks at block positions a and b would break the spacing rule.</summary>
    public static bool Conflicts(int ax, int ay, int az, int bx, int by, int bz, int spacing)
    {
        return Math.Abs(ax - bx) < spacing && Math.Abs(ay - by) < spacing && Math.Abs(az - bz) < spacing;
    }

    /// <summary>
    /// How many blocks around a spot (in each direction, on each axis) must be checked for other hammocks.
    /// A spacing of 4 means a 7 x 7 x 7 scan centred on the spot.
    /// </summary>
    public static int ScanRadius(int spacing)
    {
        if (spacing < 1)
        {
            return 0;
        }
        return spacing - 1;
    }
}
