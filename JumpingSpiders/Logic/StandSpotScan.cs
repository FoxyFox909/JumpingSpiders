using System;
using System.Collections.Generic;

namespace JumpingSpiders;

/// <summary>
/// The scans for block cells a spider can stand in (IJumpWorld.CanStandAt) that several callers share:
/// - FirstInColumn: the first such cell in one block column, top down or nearest to a height first. Ground spots
///   around a tree for wild hammocks (HammockReachFinder.FindGroundStarts), the ground in a wild spawn's column
///   (WildSpiderSpawner) and a spot next to the player for /jspider spawn (NearbyStandSpot).
/// - InRing: every such cell in the columns around a point, within a band of heights. Launch spots to walk to before
///   jumping: to a hammock (LaunchSpotFinder) and to a wall or ceiling to build at (BuildStanceFinder.FindLaunchSpots).
/// Each caller adds its own rules (which heights, which cells to leave out, the order it tries them in).
///
/// Every spot found stands in the middle of its cell at the cell's stand height (MakeSpot). Pure C# (no game types),
/// covered by unit tests.
/// </summary>
public static class StandSpotScan
{
    /// <summary>A spot in cell (x, y, z): feet in the middle of the cell, at its stand height. Does not check that the spider can stand there.</summary>
    public static StandSpot MakeSpot(IJumpWorld world, int x, int y, int z)
    {
        StandSpot spot = new StandSpot();
        spot.CellX = x;
        spot.CellY = y;
        spot.CellZ = z;
        spot.FeetX = x + 0.5;
        spot.FeetY = world.GetStandHeight(x, y, z);
        spot.FeetZ = z + 0.5;
        return spot;
    }

    /// <summary>
    /// The first cell of column (x, z) the spider can stand in, at heights from baseY - down to baseY + up, or null.
    /// The heights are tried from the top down, or with nearestFirst nearest to baseY first: baseY, one below, one
    /// above, two below, ... (HeightOffset).
    /// </summary>
    public static StandSpot FirstInColumn(IJumpWorld world, int x, int z, int baseY, int up, int down, bool nearestFirst)
    {
        if (!nearestFirst)
        {
            for (int y = baseY + up; y >= baseY - down; y--)
            {
                if (world.CanStandAt(x, y, z))
                {
                    return MakeSpot(world, x, y, z);
                }
            }
            return null;
        }
        int steps = 2 * Math.Max(up, down);
        for (int step = 0; step <= steps; step++)
        {
            int offset = HeightOffset(step);
            if (offset > up || offset < -down)
            {
                continue;
            }
            int y = baseY + offset;
            if (world.CanStandAt(x, y, z))
            {
                return MakeSpot(world, x, y, z);
            }
        }
        return null;
    }

    /// <summary>The height offsets in the order FirstInColumn tries them nearest first: 0, -1, +1, -2, +2, ...</summary>
    public static int HeightOffset(int step)
    {
        int distance = (step + 1) / 2;
        if (step % 2 == 1)
        {
            return -distance;
        }
        return distance;
    }

    /// <summary>
    /// Every cell the spider can stand in, at heights lowY to highY, in the columns at most squareRadius columns (on
    /// each axis) from column (cellX, cellZ) whose centre is minDistance to maxDistance blocks (horizontally) from the
    /// point (centreX, centreZ). In scan order: by x, then z, then y, each from low to high.
    /// </summary>
    public static List<StandSpot> InRing(IJumpWorld world, int cellX, int cellZ, int squareRadius, double centreX, double centreZ,
        double minDistance, double maxDistance, int lowY, int highY)
    {
        List<StandSpot> spots = new List<StandSpot>();
        for (int x = cellX - squareRadius; x <= cellX + squareRadius; x++)
        {
            for (int z = cellZ - squareRadius; z <= cellZ + squareRadius; z++)
            {
                double dx = x + 0.5 - centreX;
                double dz = z + 0.5 - centreZ;
                double horizontal = Math.Sqrt(dx * dx + dz * dz);
                if (horizontal < minDistance || horizontal > maxDistance)
                {
                    continue;
                }
                for (int y = lowY; y <= highY; y++)
                {
                    if (world.CanStandAt(x, y, z))
                    {
                        spots.Add(MakeSpot(world, x, y, z));
                    }
                }
            }
        }
        return spots;
    }
}
