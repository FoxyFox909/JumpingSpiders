using System;
using System.Collections.Generic;

namespace JumpingSpiders;

/// <summary>
/// Finds a spot a few blocks from a point where a spider can stand, for /jspider spawn: the spider should appear
/// where the player sees it at once, on the ground, not inside blocks, not inside the player.
///
/// The block columns whose centre is between minDistance and maxDistance (horizontally) from the point are tried in
/// random order. In each column the cells nearest to the point's own height come first (the same level, one down,
/// one up, two down, ...), so on a hillside or in a cave the spider stands at the player's level rather than on the
/// surface far above or below. Pure logic on IJumpWorld, so the unit tests run it on hand-made block worlds.
/// </summary>
public static class NearbyStandSpot
{
    /// <summary>
    /// A cell the spider can stand in (IJumpWorld.CanStandAt) whose column centre is minDistance..maxDistance blocks
    /// from (x, z) and whose height is at most maxHeightDifference cells above or below the cell of feetY, or null
    /// when there is none.
    /// </summary>
    public static StandSpot Find(IJumpWorld world, double x, double feetY, double z, double minDistance, double maxDistance,
        int maxHeightDifference, Random rand)
    {
        List<StandSpot> columns = ColumnsInRing(x, z, minDistance, maxDistance);
        Shuffle(columns, rand);
        int baseY = (int)Math.Floor(feetY);
        for (int i = 0; i < columns.Count; i++)
        {
            StandSpot column = columns[i];
            for (int step = 0; step <= 2 * maxHeightDifference; step++)
            {
                int y = baseY + HeightOffset(step);
                if (!world.CanStandAt(column.CellX, y, column.CellZ))
                {
                    continue;
                }
                column.CellY = y;
                column.FeetX = column.CellX + 0.5;
                column.FeetY = world.GetStandHeight(column.CellX, y, column.CellZ);
                column.FeetZ = column.CellZ + 0.5;
                return column;
            }
        }
        return null;
    }

    /// <summary>The height offsets in the order they are tried: 0, -1, +1, -2, +2, ...</summary>
    public static int HeightOffset(int step)
    {
        int distance = (step + 1) / 2;
        if (step % 2 == 1)
        {
            return -distance;
        }
        return distance;
    }

    // One StandSpot (only CellX and CellZ set) per column whose centre is in the ring.
    private static List<StandSpot> ColumnsInRing(double x, double z, double minDistance, double maxDistance)
    {
        List<StandSpot> columns = new List<StandSpot>();
        int reach = (int)Math.Ceiling(maxDistance) + 1;
        int centerX = (int)Math.Floor(x);
        int centerZ = (int)Math.Floor(z);
        for (int cellX = centerX - reach; cellX <= centerX + reach; cellX++)
        {
            for (int cellZ = centerZ - reach; cellZ <= centerZ + reach; cellZ++)
            {
                double dx = cellX + 0.5 - x;
                double dz = cellZ + 0.5 - z;
                double distance = Math.Sqrt(dx * dx + dz * dz);
                if (distance < minDistance || distance > maxDistance)
                {
                    continue;
                }
                StandSpot column = new StandSpot();
                column.CellX = cellX;
                column.CellZ = cellZ;
                columns.Add(column);
            }
        }
        return columns;
    }

    private static void Shuffle(List<StandSpot> list, Random rand)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rand.Next(i + 1);
            StandSpot temp = list[i];
            list[i] = list[j];
            list[j] = temp;
        }
    }
}
