using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace JumpingSpiders;

/// <summary>
/// Counts the spiders around one spider chunk, for the spawn cap and the "not near kept spiders" rule.
/// One entity query around the chunk; only loaded spiders are seen (entities in unloaded chunks do not exist
/// for the game until the chunk loads again, the same as for vanilla's own spawn caps).
///
/// A wild spider counts for a spider chunk when it is inside the chunk column widened by
/// Config.WildSpawnRadiusFromHammock blocks, or when its home hammock is in (or right next to) the chunk column
/// and it is within Config.HomeHammockForgetDistance (farther away it forgets that home anyway).
/// A kept spider (caught, placed, bred or fed: DespawnRules) counts as near when it is within
/// Config.SpawnMinKeptSpiderDistance blocks of the chunk column.
/// </summary>
public class WildSpiderCensus
{
    /// <summary>A home hammock this many blocks outside the chunk column still belongs to it (hammocks sit beside trunks).</summary>
    public const int HomeMargin = 2;

    /// <summary>Wild spiders that count for this spider chunk's cap.</summary>
    public int WildSpiders;

    /// <summary>Kept spiders near the chunk.</summary>
    public int KeptSpidersNear;

    /// <summary>Distance in blocks from the chunk column to the nearest kept spider, or -1 when none was seen.</summary>
    public double NearestKeptDistance = -1;

    public static WildSpiderCensus Take(IWorldAccessor world, int chunkX, int chunkZ)
    {
        JumpingSpidersConfig config = JumpingSpidersModSystem.Config;
        WildSpiderCensus census = new WildSpiderCensus();

        int size = TreeTrunks.ChunkSize;
        double minX = chunkX * size;
        double minZ = chunkZ * size;
        double maxX = minX + size;
        double maxZ = minZ + size;

        double reach = Math.Max(config.WildSpawnRadiusFromHammock, Math.Max(config.HomeHammockForgetDistance, config.SpawnMinKeptSpiderDistance));
        float horizontalRange = (float)(size * Math.Sqrt(0.5) + reach);
        float verticalRange = world.BlockAccessor.MapSizeY;
        Vec3d center = new Vec3d(minX + size / 2.0, world.SeaLevel, minZ + size / 2.0);
        Entity[] spiders = world.GetEntitiesAround(center, horizontalRange, verticalRange, IsLivingSpider);

        for (int i = 0; i < spiders.Length; i++)
        {
            Entity spider = spiders[i];
            double distance = DistanceToRect(spider.Pos.X, spider.Pos.Z, minX, minZ, maxX, maxZ);
            bool kept = DespawnRules.IsKeptByPlayer(SpiderAttributes.GetGeneration(spider), SpiderAttributes.GetOrigin(spider),
                SpiderAttributes.IsMarkedKeptByPlayer(spider));
            if (kept)
            {
                if (distance <= config.SpawnMinKeptSpiderDistance)
                {
                    census.KeptSpidersNear++;
                }
                if (census.NearestKeptDistance < 0 || distance < census.NearestKeptDistance)
                {
                    census.NearestKeptDistance = distance;
                }
                continue;
            }

            if (distance <= config.WildSpawnRadiusFromHammock)
            {
                census.WildSpiders++;
                continue;
            }
            BlockPos home = SpiderAttributes.GetHomeHammock(spider);
            if (home != null && distance <= config.HomeHammockForgetDistance
                && DistanceToRect(home.X + 0.5, home.Z + 0.5, minX, minZ, maxX, maxZ) <= HomeMargin)
            {
                census.WildSpiders++;
            }
        }
        return census;
    }

    // Matcher for the entity query: our spiders that are alive.
    private static bool IsLivingSpider(Entity entity)
    {
        return entity.Alive && SpiderAttributes.IsSpider(entity);
    }

    // Horizontal distance from a point to a rectangle (0 inside it).
    private static double DistanceToRect(double x, double z, double minX, double minZ, double maxX, double maxZ)
    {
        double dx = Math.Max(0, Math.Max(minX - x, x - maxX));
        double dz = Math.Max(0, Math.Max(minZ - z, z - maxZ));
        return Math.Sqrt(dx * dx + dz * dz);
    }
}
