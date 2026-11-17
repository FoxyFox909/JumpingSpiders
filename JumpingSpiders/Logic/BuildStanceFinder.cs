using System;
using System.Collections.Generic;

namespace JumpingSpiders;

/// <summary>
/// Finds where a spider could be while it builds a hammock in one cell (the "site"): every spot within reach of the
/// site (BuildReach) where it can stand, and every spot within reach where it can hold on to a wall or under a
/// ceiling. AiTaskSpiderBuildHammock calls this once per build attempt (or once more after the spider has moved) and
/// then tries to get to the nearest ones.
///
/// It only looks at the cells around the site (Config.HammockBuildRange plus one in each direction, about 300 cells
/// for a range of 2), and only through IJumpWorld, so the cost is a bounded number of block lookups and it runs on the
/// small test worlds of the unit tests.
///
/// Every spot must also have a clear line to the site (BuildReach.CanReach with the sight world): no reaching through
/// a floor or a wall.
///
/// A clinging spot is a free cell where the spider cannot stand, with something solid right beside it (a wall) or
/// right above it (a ceiling). The spider's box is put against that surface, ClingGap away from it.
/// </summary>
public static class BuildStanceFinder
{
    /// <summary>Space left between a clinging spider's box and the surface it holds, in blocks (so it does not count as stuck in it).</summary>
    public const double ClingGap = 0.02;

    /// <summary>How far beyond the spider's box a surface may be and still be held, in blocks.</summary>
    public const double SurfaceProbe = 0.1;

    /// <summary>
    /// A standing spot is only used when the spider's box, grown by this many blocks sideways, stays out of the site's
    /// cell. The pathfinder stops a little short of its goal (0.3 blocks), so a spot right next to the site would often
    /// leave the spider standing in the way of the new hammock.
    /// </summary>
    public const double StandingSiteMargin = 0.3;

    /// <summary>A clinging spider's box must keep this far (sideways) out of the site's cell, in blocks.</summary>
    public const double ClingingSiteMargin = 0.02;

    /// <summary>Launch spots for a jump to a clinging spot are at least this far from it horizontally, in blocks (no jumps straight up).</summary>
    public const double MinLaunchDistance = 1.0;

    // The five surfaces a spider can hold on to: four walls and the ceiling (directions from the spider).
    private static readonly int[] SurfaceDirX = new int[] { 1, -1, 0, 0, 0 };
    private static readonly int[] SurfaceDirY = new int[] { 0, 0, 0, 0, 1 };
    private static readonly int[] SurfaceDirZ = new int[] { 0, 0, 1, -1, 0 };

    /// <summary>
    /// Every cell within reach of the site where the spider can stand (and not be in the new hammock's way), nearest to
    /// the spider first. halfWidth and height describe the spider's box; spiderX/Y/Z are its feet.
    /// </summary>
    public static List<BuildStance> FindStanding(IJumpWorld world, ISolidPointWorld sight, int siteX, int siteY, int siteZ, double range,
        double halfWidth, double height, double spiderX, double spiderY, double spiderZ)
    {
        List<BuildStance> result = new List<BuildStance>();
        int radius = SearchRadius(range);
        for (int x = siteX - radius; x <= siteX + radius; x++)
        {
            for (int y = siteY - radius; y <= siteY + radius; y++)
            {
                for (int z = siteZ - radius; z <= siteZ + radius; z++)
                {
                    if (x == siteX && y == siteY && z == siteZ)
                    {
                        continue;
                    }
                    // Cheap first: a cell whose whole column of possible body positions is out of reach.
                    if (BuildReach.DistanceToCell(x + 0.5, y + 0.5, z + 0.5, siteX, siteY, siteZ) > range + 1)
                    {
                        continue;
                    }
                    if (!world.CanStandAt(x, y, z))
                    {
                        continue;
                    }
                    double feetX = x + 0.5;
                    double feetY = world.GetStandHeight(x, y, z);
                    double feetZ = z + 0.5;
                    if (BuildReach.BoxOverlapsCell(feetX, feetY, feetZ, halfWidth, height, StandingSiteMargin, siteX, siteY, siteZ))
                    {
                        continue;
                    }
                    if (!BuildReach.CanReach(sight, feetX, feetY, feetZ, height, siteX, siteY, siteZ, range))
                    {
                        continue;
                    }
                    BuildStance stance = new BuildStance();
                    stance.Clinging = false;
                    SetCellAndFeet(stance, x, y, z, feetX, feetY, feetZ, spiderX, spiderY, spiderZ);
                    result.Add(stance);
                }
            }
        }
        result.Sort(CompareByDistance);
        return result;
    }

    /// <summary>
    /// Every spot within reach of the site where the spider can hold on to a wall or under a ceiling, nearest to the
    /// spider first. On a wall the spider is in the middle of the cell's height; under a ceiling its back touches it.
    /// </summary>
    public static List<BuildStance> FindClinging(IJumpWorld world, ISolidPointWorld sight, int siteX, int siteY, int siteZ, double range,
        double halfWidth, double height, double spiderX, double spiderY, double spiderZ)
    {
        List<BuildStance> result = new List<BuildStance>();
        int radius = SearchRadius(range);
        for (int x = siteX - radius; x <= siteX + radius; x++)
        {
            for (int y = siteY - radius; y <= siteY + radius; y++)
            {
                for (int z = siteZ - radius; z <= siteZ + radius; z++)
                {
                    if (x == siteX && y == siteY && z == siteZ)
                    {
                        continue;
                    }
                    if (BuildReach.DistanceToCell(x + 0.5, y + 0.5, z + 0.5, siteX, siteY, siteZ) > range + 1)
                    {
                        continue;
                    }
                    // Where it can stand it stands (FindStanding); clinging is for the air.
                    if (world.CanStandAt(x, y, z))
                    {
                        continue;
                    }
                    for (int d = 0; d < SurfaceDirX.Length; d++)
                    {
                        BuildStance stance = TryClingSpot(world, sight, x, y, z, SurfaceDirX[d], SurfaceDirY[d], SurfaceDirZ[d],
                            siteX, siteY, siteZ, range, halfWidth, height, spiderX, spiderY, spiderZ);
                        if (stance != null)
                        {
                            result.Add(stance);
                        }
                    }
                }
            }
        }
        result.Sort(CompareByDistance);
        return result;
    }

    /// <summary>
    /// Cells the spider could walk to and jump from to reach a clinging spot: standable, within searchRadius blocks
    /// (horizontally) of it but at least MinLaunchDistance away, no more than one jump below it, and at heights the
    /// pathfinder can walk to from the spider's feet cell (maxStepUp above, maxStepDown below). Nearest to the spider
    /// first. Whether the jump works from there is for ClingJump.TryJump to say.
    /// </summary>
    public static List<BuildStance> FindLaunchSpots(IJumpWorld world, BuildStance cling, JumpLimits limits, int searchRadius,
        int maxStepUp, int maxStepDown, double spiderX, double spiderY, double spiderZ)
    {
        List<BuildStance> result = new List<BuildStance>();
        int spiderCellY = JumpPlanner.CellOfFeetY(spiderY);
        for (int x = cling.CellX - searchRadius; x <= cling.CellX + searchRadius; x++)
        {
            for (int z = cling.CellZ - searchRadius; z <= cling.CellZ + searchRadius; z++)
            {
                double dx = x + 0.5 - cling.FeetX;
                double dz = z + 0.5 - cling.FeetZ;
                double horizontal = Math.Sqrt(dx * dx + dz * dz);
                if (horizontal < MinLaunchDistance || horizontal > limits.MaxJumpHorizontal)
                {
                    continue;
                }
                for (int y = spiderCellY - maxStepDown; y <= spiderCellY + maxStepUp; y++)
                {
                    if (!world.CanStandAt(x, y, z))
                    {
                        continue;
                    }
                    double feetY = world.GetStandHeight(x, y, z);
                    if (cling.FeetY - feetY > limits.MaxJumpUp || feetY - cling.FeetY > limits.MaxJumpDown)
                    {
                        continue;
                    }
                    BuildStance spot = new BuildStance();
                    spot.Clinging = false;
                    SetCellAndFeet(spot, x, y, z, x + 0.5, feetY, z + 0.5, spiderX, spiderY, spiderZ);
                    result.Add(spot);
                }
            }
        }
        result.Sort(CompareByDistance);
        return result;
    }

    /// <summary>Feet position of a spider holding on in cell (x, y, z) to the surface in direction (dirX, dirY, dirZ).</summary>
    public static void ClingFeet(int x, int y, int z, int dirX, int dirY, int dirZ, double halfWidth, double height,
        out double feetX, out double feetY, out double feetZ)
    {
        feetX = x + 0.5;
        feetZ = z + 0.5;
        // On a wall: in the middle of the cell's height.
        feetY = y + (1 - height) / 2;
        if (dirX > 0)
        {
            feetX = x + 1 - halfWidth - ClingGap;
        }
        else if (dirX < 0)
        {
            feetX = x + halfWidth + ClingGap;
        }
        if (dirZ > 0)
        {
            feetZ = z + 1 - halfWidth - ClingGap;
        }
        else if (dirZ < 0)
        {
            feetZ = z + halfWidth + ClingGap;
        }
        if (dirY > 0)
        {
            feetY = y + 1 - height - ClingGap;
        }
    }

    // One clinging spot, or null when the spider does not fit there, there is nothing to hold, or it is out of reach.
    private static BuildStance TryClingSpot(IJumpWorld world, ISolidPointWorld sight, int x, int y, int z, int dirX, int dirY, int dirZ,
        int siteX, int siteY, int siteZ, double range, double halfWidth, double height,
        double spiderX, double spiderY, double spiderZ)
    {
        double feetX;
        double feetY;
        double feetZ;
        ClingFeet(x, y, z, dirX, dirY, dirZ, halfWidth, height, out feetX, out feetY, out feetZ);
        if (!BuildReach.IsWithinReach(feetX, feetY, feetZ, height, siteX, siteY, siteZ, range))
        {
            return null;
        }
        if (BuildReach.BoxOverlapsCell(feetX, feetY, feetZ, halfWidth, height, ClingingSiteMargin, siteX, siteY, siteZ))
        {
            return null;
        }
        if (!world.IsFree(feetX, feetY, feetZ))
        {
            return null;
        }
        // Something solid just beyond the box on that side: the surface it holds on to.
        if (world.IsFree(feetX + dirX * SurfaceProbe, feetY + dirY * SurfaceProbe, feetZ + dirZ * SurfaceProbe))
        {
            return null;
        }
        if (!BuildReach.IsLineClear(sight, feetX, feetY + height / 2, feetZ, siteX, siteY, siteZ))
        {
            return null;
        }
        BuildStance stance = new BuildStance();
        stance.Clinging = true;
        stance.SurfaceX = dirX;
        stance.SurfaceY = dirY;
        stance.SurfaceZ = dirZ;
        SetCellAndFeet(stance, x, y, z, feetX, feetY, feetZ, spiderX, spiderY, spiderZ);
        return stance;
    }

    private static void SetCellAndFeet(BuildStance stance, int x, int y, int z, double feetX, double feetY, double feetZ,
        double spiderX, double spiderY, double spiderZ)
    {
        stance.CellX = x;
        stance.CellY = y;
        stance.CellZ = z;
        stance.FeetX = feetX;
        stance.FeetY = feetY;
        stance.FeetZ = feetZ;
        double dx = feetX - spiderX;
        double dy = feetY - spiderY;
        double dz = feetZ - spiderZ;
        stance.DistanceFromSpider = Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    // How many cells around the site to look at: the reach, plus one for the spider's own cell.
    private static int SearchRadius(double range)
    {
        if (range <= 0)
        {
            return 1;
        }
        return (int)Math.Ceiling(range) + 1;
    }

    // For List.Sort: nearest to the spider first.
    private static int CompareByDistance(BuildStance a, BuildStance b)
    {
        return a.DistanceFromSpider.CompareTo(b.DistanceFromSpider);
    }
}
