using System;

namespace JumpingSpiders;

/// <summary>
/// Wraps another IJumpWorld and forbids some landing cells, so that JumpPlanner.PlanJumpToRandomSpot only picks
/// spots that make sense for the task asking:
/// - Fleeing sets a threat: a cell only counts when its centre is at least MinThreatDistance away from the threat
///   (horizontally), so every flee jump really gains distance.
/// - Exploring sets an anchor: a cell only counts when its centre is at most MaxAnchorDistance from the anchor
///   (horizontally), so a domesticated spider explores around one place instead of drifting off.
/// Either rule can be left unset. Only CanStandAt is filtered; IsFree and GetStandHeight are passed through, so arc
/// checks see the real world.
///
/// Pure C# (no game types), covered by unit tests.
/// </summary>
public class LandingSpotFilter : IJumpWorld
{
    private readonly IJumpWorld inner;

    private bool hasThreat;
    private double threatX;
    private double threatZ;
    private double minThreatDistance;

    private bool hasAnchor;
    private double anchorX;
    private double anchorZ;
    private double maxAnchorDistance;

    public LandingSpotFilter(IJumpWorld inner)
    {
        this.inner = inner;
    }

    /// <summary>Landing cells must be at least minDistance blocks (horizontally) from this point.</summary>
    public void SetThreat(double x, double z, double minDistance)
    {
        hasThreat = true;
        threatX = x;
        threatZ = z;
        minThreatDistance = minDistance;
    }

    /// <summary>Landing cells must be at most maxDistance blocks (horizontally) from this point.</summary>
    public void SetAnchor(double x, double z, double maxDistance)
    {
        hasAnchor = true;
        anchorX = x;
        anchorZ = z;
        maxAnchorDistance = maxDistance;
    }

    public bool IsFree(double x, double y, double z)
    {
        return inner.IsFree(x, y, z);
    }

    public double GetStandHeight(int x, int y, int z)
    {
        return inner.GetStandHeight(x, y, z);
    }

    public bool CanStandAt(int x, int y, int z)
    {
        if (!IsAllowed(x, z))
        {
            return false;
        }
        return inner.CanStandAt(x, y, z);
    }

    /// <summary>True when the rules allow landing in a cell of this column (checked at the cell centre).</summary>
    public bool IsAllowed(int x, int z)
    {
        double centreX = x + 0.5;
        double centreZ = z + 0.5;
        if (hasThreat && HorizontalDistance(centreX, centreZ, threatX, threatZ) < minThreatDistance)
        {
            return false;
        }
        if (hasAnchor && HorizontalDistance(centreX, centreZ, anchorX, anchorZ) > maxAnchorDistance)
        {
            return false;
        }
        return true;
    }

    private static double HorizontalDistance(double ax, double az, double bx, double bz)
    {
        double dx = ax - bx;
        double dz = az - bz;
        return Math.Sqrt(dx * dx + dz * dz);
    }
}
