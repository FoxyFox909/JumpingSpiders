using System;

namespace JumpingSpiders;

/// <summary>
/// The "at most one hammock per N x N x N box" rule, N being HammockSpacing in the config (3 by default).
///
/// Two hammocks are too close when they are less than N blocks apart on every axis at once. This is the strict
/// reading of the rule: no N x N x N box anywhere, aligned to the grid or not, can then hold two hammocks.
/// Hammocks N blocks apart along any one axis are fine. With N = 3, a hammock rules out the 5 x 5 x 5 cells centred on
/// it, because any two of those fit together in some 3 x 3 x 3 box.
///
/// The rule covers every hammock, whoever placed it: a spider building one, wild spawning, a player.
/// HammockFinder.HasHammockWithinSpacing (in the Hammock folder) uses this to check the hammocks around a spot.
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
    /// A spacing of 3 means a 5 x 5 x 5 scan centred on the spot.
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
