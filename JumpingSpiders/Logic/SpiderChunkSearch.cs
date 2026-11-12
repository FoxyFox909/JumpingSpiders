using System.Collections.Generic;

namespace JumpingSpiders;

/// <summary>
/// Lists the spider chunks (see SpiderChunks) around a chunk column, nearest first. The spawner uses it to find
/// the spider chunks near each player, and the /jspider chunk command to show the nearest ones.
///
/// Only the hash is evaluated, so this costs nothing but arithmetic: (2 * radius + 1)^2 hashes.
/// </summary>
public static class SpiderChunkSearch
{
    /// <summary>
    /// Every spider chunk whose chunk coordinates are within radiusChunks of (centerX, centerZ) on both axes
    /// (a square), sorted by distance to the centre, nearest first. Ties keep a fixed order (by X, then Z).
    /// </summary>
    public static List<ChunkCoord> FindAround(int worldSeed, int centerX, int centerZ, int radiusChunks, double fraction)
    {
        List<ChunkCoord> found = new List<ChunkCoord>();
        for (int dx = -radiusChunks; dx <= radiusChunks; dx++)
        {
            for (int dz = -radiusChunks; dz <= radiusChunks; dz++)
            {
                int x = centerX + dx;
                int z = centerZ + dz;
                if (!SpiderChunks.IsSpiderChunk(worldSeed, x, z, fraction))
                {
                    continue;
                }
                ChunkCoord coord = new ChunkCoord();
                coord.X = x;
                coord.Z = z;
                coord.DistanceSq = dx * dx + dz * dz;
                InsertSorted(found, coord);
            }
        }
        return found;
    }

    // Insertion keeps the list sorted by distance. The loops above visit X, then Z, in increasing order, so
    // inserting after every equal distance keeps ties in that order.
    private static void InsertSorted(List<ChunkCoord> list, ChunkCoord coord)
    {
        int index = list.Count;
        while (index > 0 && list[index - 1].DistanceSq > coord.DistanceSq)
        {
            index--;
        }
        list.Insert(index, coord);
    }
}
