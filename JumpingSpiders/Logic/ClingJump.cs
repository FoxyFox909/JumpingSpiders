using System;

namespace JumpingSpiders;

/// <summary>
/// The jump a spider makes to hold on to a wall or under a ceiling for a moment while it builds a hammock, and the
/// test that it has got hold. Pure logic, no game types, so it is covered by unit tests.
///
/// A normal jump (JumpPlanner) must end on something to stand on. A cling jump ends in the air, against a surface:
/// - Wall: the arc ends at the clinging spot, the spider's box right against the wall (it comes down onto that point,
///   like any jump). AiTaskSpiderBuildHammock catches it there and pauses its physics.
/// - Ceiling: the arc peaks at the clinging spot, the spider's back just under the ceiling. It is aimed at a point
///   CeilingLedgeClearance below the spot with that ledge clearance, so the arc's highest point is the spot itself
///   (JumpMath makes an upward arc peak at least the ledge clearance above its end). The task catches the spider at the
///   top of the arc.
/// Every arc is checked against the blocks like a normal jump (JumpPlanner.TryJump), only without the footing test at the end.
/// </summary>
public static class ClingJump
{
    /// <summary>For a ceiling cling, how far below the clinging spot the arc is aimed (and so how far above its end it peaks), in blocks.</summary>
    public const double CeilingLedgeClearance = 0.1;

    /// <summary>How many halvings FindHold uses to find how close to the surface the spider can be (0.1 / 2^8 is under 0.001 blocks).</summary>
    public const int HoldSearchSteps = 8;

    /// <summary>
    /// A jump from the feet position (fromX, fromY, fromZ) to the clinging stance whose whole flight is free of blocks,
    /// or null. JumpPlanner.TryJump without the footing test, so the launch angles are tried in the same order.
    /// </summary>
    public static JumpStep TryJump(IJumpWorld world, double fromX, double fromY, double fromZ, BuildStance stance, JumpLimits limits)
    {
        if (stance == null || !stance.Clinging)
        {
            return null;
        }
        JumpLimits used = limits;
        double aimY = stance.FeetY;
        if (stance.IsCeiling())
        {
            used = limits.Copy();
            used.LedgeClearance = CeilingLedgeClearance;
            aimY = stance.FeetY - CeilingLedgeClearance;
        }
        return JumpPlanner.TryJump(world, fromX, fromY, fromZ, stance.FeetX, aimY, stance.FeetZ, used, false);
    }

    /// <summary>
    /// Has a spider with its feet at (x, y, z) got hold of a surface in direction (dirX, dirY, dirZ)? True when its box
    /// fits where it is and something solid is within probe blocks beyond the box on that side. Then holdX/Y/Z is where
    /// to put it so that its box is against the surface, gap blocks away from it.
    /// </summary>
    public static bool FindHold(IJumpWorld world, double x, double y, double z, int dirX, int dirY, int dirZ,
        double probe, double gap, out double holdX, out double holdY, out double holdZ)
    {
        holdX = x;
        holdY = y;
        holdZ = z;
        if (!world.IsFree(x, y, z))
        {
            return false;
        }
        if (world.IsFree(x + dirX * probe, y + dirY * probe, z + dirZ * probe))
        {
            return false;
        }
        // The largest move towards the surface that still fits lies between free and blocked.
        double free = 0;
        double blocked = probe;
        for (int i = 0; i < HoldSearchSteps; i++)
        {
            double middle = (free + blocked) / 2;
            if (world.IsFree(x + dirX * middle, y + dirY * middle, z + dirZ * middle))
            {
                free = middle;
            }
            else
            {
                blocked = middle;
            }
        }
        double move = Math.Max(0, free - gap);
        holdX = x + dirX * move;
        holdY = y + dirY * move;
        holdZ = z + dirZ * move;
        return true;
    }
}
