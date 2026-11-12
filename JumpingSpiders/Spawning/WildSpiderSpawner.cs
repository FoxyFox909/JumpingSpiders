using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace JumpingSpiders;

/// <summary>
/// The core step of wild spawning: "try to spawn one wild spider in this spider chunk now". Everything it depends on
/// comes in through a SpawnRequest, so the periodic SpiderSpawnSystem, the /jspider spawn command and tests all call
/// the same code. Server main thread only. It never loads chunks: an unloaded chunk is simply refused.
///
/// The checks run cheapest first, and the first failing one ends the attempt with its reason:
/// spider chunk? loaded? cooldown over? forest and climate? not claimed? not player-built? no kept spider near?
/// room under the cap? a player in range? Then the spider rolls whether it comes with a hammock (WildHammockSites
/// finds or places the chunk's one hammock), a spawn position is chosen (asleep inside the hammock at night,
/// otherwise on the ground within Config.WildSpawnRadiusFromHammock of it), other mods may veto the spawn through
/// the game's own "try spawn entity" event, and the spider is created.
///
/// A wild spider is an adult of either sex, generation 0, with no "origin" attribute (an origin of "playerplaced"
/// or "reproduction" would mark it as kept, so it would never despawn), and with its home hammock set if it has one.
/// </summary>
public static class WildSpiderSpawner
{
    /// <summary>Entity code of a wild spider, without the sex (male or female is added).</summary>
    public const string AdultCodePrefix = "jumpingspider-adult-";

    /// <summary>
    /// Vanilla spawning reads the climate at 109 % of sea level, whatever the spawn height (warmer and less extreme
    /// than mountain tops). We do the same so our temperature limits mean what vanilla animals' limits mean.
    /// </summary>
    public const double ClimateSampleHeightFactor = 1.09;

    /// <summary>A day spawn spot is searched this many blocks above and below the generated ground height.</summary>
    public const int GroundSearchUp = 3;
    public const int GroundSearchDown = 3;

    public static SpawnResult TrySpawn(ICoreServerAPI api, SpawnRequest request)
    {
        SpawnResult result = new SpawnResult();
        JumpingSpidersConfig config = JumpingSpidersModSystem.Config;
        IWorldAccessor world = api.World;
        int chunkX = request.ChunkX;
        int chunkZ = request.ChunkZ;

        if (!SpiderChunks.IsSpiderChunk(world.Seed, chunkX, chunkZ, config.SpiderChunkFraction))
        {
            return result.Fail(SpawnOutcome.NotSpiderChunk, "Chunk " + chunkX + ", " + chunkZ + " is not a spider chunk.");
        }

        IMapChunk mapChunk = api.WorldManager.GetMapChunk(chunkX, chunkZ);
        if (mapChunk == null || !IsGroundLoaded(api, mapChunk, chunkX, chunkZ))
        {
            return result.Fail(SpawnOutcome.ChunkNotLoaded, "The chunk is not loaded.");
        }

        WildHammockRecord record = WildHammockRecords.Load(mapChunk);
        double now = world.Calendar.TotalHours;
        if (request.CheckCooldown && !SpawnRules.IsCooldownOver(record.LastSpawnTotalHours, now, config.SpiderChunkSpawnCooldownHours))
        {
            return result.Fail(SpawnOutcome.Cooldown, string.Format("A spider spawned here {0:0.0} in-game hours ago (cooldown {1} hours).",
                now - record.LastSpawnTotalHours, config.SpiderChunkSpawnCooldownHours));
        }

        BlockPos center = WildHammockSites.ChunkCenterAtGround(mapChunk, chunkX, chunkZ);
        ClimateCondition climate = GetSpawnClimate(world, center);
        if (climate == null)
        {
            return result.Fail(SpawnOutcome.NoClimateData, "No climate data here.");
        }
        if (!ClimateAllows(climate))
        {
            return result.Fail(SpawnOutcome.ClimateUnsuitable, "Not spider country: " + DescribeClimate(climate) + ".");
        }

        if (IsClaimed(world, chunkX, chunkZ, config.WildSpawnRadiusFromHammock))
        {
            return result.Fail(SpawnOutcome.Claimed, "A land claim covers this area.");
        }

        int placedBlocks = CountPlayerPlacedBlocks(api, chunkX, chunkZ);
        if (placedBlocks > config.SpawnMaxPlayerPlacedBlocks)
        {
            return result.Fail(SpawnOutcome.PlayerBuilt, "Players placed " + placedBlocks + " blocks here and around (limit "
                + config.SpawnMaxPlayerPlacedBlocks + ").");
        }

        WildSpiderCensus census = WildSpiderCensus.Take(world, chunkX, chunkZ);
        if (census.KeptSpidersNear > 0)
        {
            return result.Fail(SpawnOutcome.NearKeptSpider, string.Format("A kept spider is {0:0} blocks away (minimum {1}).",
                census.NearestKeptDistance, config.SpawnMinKeptSpiderDistance));
        }
        if (!SpawnRules.HasRoomForWildSpider(census.WildSpiders, config.MaxWildSpidersPerSpiderChunk))
        {
            return result.Fail(SpawnOutcome.CapReached, census.WildSpiders + " wild spiders already live here (cap "
                + config.MaxWildSpidersPerSpiderChunk + ").");
        }

        if (request.CheckPlayerDistance && !AnyPlayerCouldBeInRange(request.PlayerPositions, center, config))
        {
            return result.Fail(SpawnOutcome.NoPlayerNearby, "No player within " + config.SpawnMaxPlayerDistance + " blocks.");
        }

        string sex = "female";
        if (request.Rand.NextDouble() < 0.5)
        {
            sex = "male";
        }
        EntityProperties type = world.GetEntityType(new AssetLocation(SpiderAttributes.Domain, AdultCodePrefix + sex));
        if (type == null)
        {
            return result.Fail(SpawnOutcome.EntityTypeMissing, "The spider entity type is missing (assets not loaded?).");
        }
        Cuboidf spiderBox = type.SpawnCollisionBox;

        // The hammock comes first: it must be in place before a spider is put into its cell.
        BlockPos hammock = null;
        if (request.Rand.NextDouble() < request.HammockChance)
        {
            hammock = WildHammockSites.FindOrPlace(world, mapChunk, record, request, spiderBox, result);
        }
        else
        {
            result.Note("Rolled no hammock: this spider has no home.");
        }

        SpiderJumpWorld jumpWorld = new SpiderJumpWorld(world.BlockAccessor, spiderBox, 0);
        Vec3d spawnAt = null;
        if (hammock != null && request.Night)
        {
            spawnAt = FindSleepSpot(world, jumpWorld, hammock);
            if (spawnAt != null)
            {
                result.InHammock = true;
            }
            else
            {
                result.Note("The hammock is taken or blocked, so the spider spawns beside it.");
            }
        }
        if (spawnAt == null)
        {
            // Near the hammock the spot must also be within the radius up or down, so a spider on a hillside does
            // not start at the foot of a cliff below its tree. Without a hammock any height will do.
            Vec3d around;
            double maxHeightDifference;
            if (hammock != null)
            {
                around = HammockFinder.CellCenter(hammock);
                maxHeightDifference = config.WildSpawnRadiusFromHammock;
            }
            else
            {
                around = new Vec3d(chunkX * TreeTrunks.ChunkSize + request.Rand.NextDouble() * TreeTrunks.ChunkSize, center.Y,
                    chunkZ * TreeTrunks.ChunkSize + request.Rand.NextDouble() * TreeTrunks.ChunkSize);
                maxHeightDifference = double.MaxValue;
            }
            spawnAt = FindGroundSpot(world, jumpWorld, around, maxHeightDifference, request, config);
        }
        if (spawnAt == null)
        {
            return result.Fail(SpawnOutcome.NoSpawnRoom, "Found no ground spot with room for the spider within "
                + config.WildSpawnRadiusFromHammock + " blocks (or every spot was too close to a player).");
        }
        if (request.CheckPlayerDistance && !PlayerDistanceOk(request.PlayerPositions, spawnAt, config))
        {
            return result.Fail(SpawnOutcome.NoSpawnRoom, "A player is too close to the hammock for the spider to appear in it.");
        }

        // Let other mods (and vanilla's spawn filters) veto it, the same event the game's own spawner fires.
        long herdId = api.WorldManager.GetNextUniqueId();
        EntityProperties askedType = type;
        if (!api.Event.TriggerTrySpawnEntity(world.BlockAccessor, ref type, spawnAt, herdId))
        {
            return result.Fail(SpawnOutcome.Vetoed, "Another mod refused the spawn.");
        }
        if (type == null || type.Code == null || !type.Code.Equals(askedType.Code))
        {
            return result.Fail(SpawnOutcome.Vetoed, "Another mod tried to replace the spider with another creature; not spawned.");
        }

        Entity spider = world.ClassRegistry.CreateEntity(type);
        EntityAgent agent = spider as EntityAgent;
        if (agent != null)
        {
            agent.HerdId = herdId;
        }
        spider.Pos.SetPos(spawnAt.X, spawnAt.Y, spawnAt.Z);
        spider.Pos.SetYaw((float)(request.Rand.NextDouble() * GameMath.TWOPI));
        spider.PositionBeforeFalling.Set(spawnAt.X, spawnAt.Y, spawnAt.Z);
        if (hammock != null)
        {
            SpiderAttributes.SetHomeHammock(spider, hammock);
        }
        world.SpawnEntity(spider);

        if (result.InHammock)
        {
            BlockEntitySilkHammock hammockEntity = HammockFinder.GetHammockAt(world, hammock);
            if (hammockEntity != null && hammockEntity.TryOccupy(spider))
            {
                SpiderAttributes.SetSleeping(spider, true);
            }
        }

        record.LastSpawnTotalHours = now;
        WildHammockRecords.Save(mapChunk, record);

        result.Outcome = SpawnOutcome.Spawned;
        result.Spider = spider;
        result.Hammock = hammock;
        string where = "on the ground";
        if (result.InHammock)
        {
            where = "asleep in its hammock";
        }
        result.Note(string.Format("Spawned a wild {0} (id {1}) {2} at {3:0.0}, {4:0.0}, {5:0.0}.",
            sex, spider.EntityId, where, spawnAt.X, spawnAt.Y, spawnAt.Z));
        return result;
    }

    /// <summary>True when the config's forest and temperature limits allow wild spiders in this climate.</summary>
    public static bool ClimateAllows(ClimateCondition climate)
    {
        JumpingSpidersConfig config = JumpingSpidersModSystem.Config;
        return SpawnRules.ClimateAllows(climate.ForestDensity, climate.WorldGenTemperature, config.SpawnMinForest,
            config.SpawnMinTemperature, config.SpawnMaxTemperature);
    }

    /// <summary>The climate values the spawn rules use, with the config's limits, in words.</summary>
    public static string DescribeClimate(ClimateCondition climate)
    {
        JumpingSpidersConfig config = JumpingSpidersModSystem.Config;
        return string.Format("forest {0:0.00} (needs at least {1:0.00}), temperature {2:0.0} C (needs {3} to {4})",
            climate.ForestDensity, config.SpawnMinForest, climate.WorldGenTemperature, config.SpawnMinTemperature, config.SpawnMaxTemperature);
    }

    /// <summary>
    /// The climate the way the game's own spawner sees it: the values from world generation (not today's weather),
    /// at the given column and 109 % of sea level. Null where the world has no climate data.
    /// </summary>
    public static ClimateCondition GetSpawnClimate(IWorldAccessor world, BlockPos column)
    {
        BlockPos samplePos = new BlockPos(column.X, (int)(world.SeaLevel * ClimateSampleHeightFactor), column.Z, 0);
        return world.BlockAccessor.GetClimateAt(samplePos, EnumGetClimateMode.WorldGenValues);
    }

    /// <summary>Blocks placed by players (the game's own per-chunk count) in this chunk column and the eight around it.</summary>
    public static int CountPlayerPlacedBlocks(ICoreServerAPI api, int chunkX, int chunkZ)
    {
        int chunksHigh = api.WorldManager.MapSizeY / TreeTrunks.ChunkSize;
        int total = 0;
        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dz = -1; dz <= 1; dz++)
            {
                for (int chunkY = 0; chunkY < chunksHigh; chunkY++)
                {
                    IServerChunk chunk = api.WorldManager.GetChunk(chunkX + dx, chunkY, chunkZ + dz);
                    if (chunk != null)
                    {
                        total += chunk.BlocksPlaced;
                    }
                }
            }
        }
        return total;
    }

    /// <summary>True when a land claim touches the chunk column widened by margin blocks.</summary>
    public static bool IsClaimed(IWorldAccessor world, int chunkX, int chunkZ, int margin)
    {
        List<LandClaim> claims = world.Claims.All;
        if (claims == null || claims.Count == 0)
        {
            return false;
        }
        int size = TreeTrunks.ChunkSize;
        HorRectanglei area = new HorRectanglei(chunkX * size - margin, chunkZ * size - margin,
            chunkX * size + size + margin, chunkZ * size + size + margin);
        for (int i = 0; i < claims.Count; i++)
        {
            if (claims[i].Intersects2d(area))
            {
                return true;
            }
        }
        return false;
    }

    // The chunks holding the generated ground and the trees above it must be loaded (tree tops can reach the next one).
    private static bool IsGroundLoaded(ICoreServerAPI api, IMapChunk mapChunk, int chunkX, int chunkZ)
    {
        BlockPos center = WildHammockSites.ChunkCenterAtGround(mapChunk, chunkX, chunkZ);
        int lowChunkY = center.Y / TreeTrunks.ChunkSize;
        int highChunkY = (center.Y + JumpingSpidersModSystem.Config.WildHammockMaxHeight + 2) / TreeTrunks.ChunkSize;
        for (int chunkY = lowChunkY; chunkY <= highChunkY; chunkY++)
        {
            if (api.WorldManager.GetChunk(chunkX, chunkY, chunkZ) == null)
            {
                return false;
            }
        }
        return true;
    }

    // Some player could be within SpawnMaxPlayerDistance of some spot of the chunk (its centre, plus half a diagonal).
    private static bool AnyPlayerCouldBeInRange(List<Vec3d> players, BlockPos center, JumpingSpidersConfig config)
    {
        double reach = config.SpawnMaxPlayerDistance + TreeTrunks.ChunkSize * Math.Sqrt(0.5) + config.WildSpawnRadiusFromHammock;
        Vec3d centerVec = HammockFinder.CellCenter(center);
        for (int i = 0; i < players.Count; i++)
        {
            if (players[i].HorizontalSquareDistanceTo(centerVec) <= reach * reach)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>True when the nearest player is between SpawnMinPlayerDistance and SpawnMaxPlayerDistance of the spot.</summary>
    public static bool PlayerDistanceOk(List<Vec3d> players, Vec3d spot, JumpingSpidersConfig config)
    {
        double nearest = double.MaxValue;
        for (int i = 0; i < players.Count; i++)
        {
            double distance = players[i].DistanceTo(spot);
            if (distance < nearest)
            {
                nearest = distance;
            }
        }
        return SpawnRules.PlayerDistanceAllows(nearest, config.SpawnMinPlayerDistance, config.SpawnMaxPlayerDistance);
    }

    // Feet position inside the hammock, or null when another spider has it or the spider would not fit.
    private static Vec3d FindSleepSpot(IWorldAccessor world, SpiderJumpWorld jumpWorld, BlockPos hammock)
    {
        BlockEntitySilkHammock hammockEntity = HammockFinder.GetHammockAt(world, hammock);
        if (hammockEntity == null || hammockEntity.GetLiveOccupant() != null)
        {
            return null;
        }
        Vec3d feet = hammockEntity.GetSleepPosition();
        if (!jumpWorld.IsFree(feet.X, feet.Y, feet.Z))
        {
            return null;
        }
        return feet;
    }

    // A random spot on the ground within WildSpawnRadiusFromHammock of around (horizontally, and at most
    // maxHeightDifference up or down) where the spider's box fits (no water, something to stand on), in a loaded
    // chunk, and far enough from players. Null after SpawnPositionTries misses.
    private static Vec3d FindGroundSpot(IWorldAccessor world, SpiderJumpWorld jumpWorld, Vec3d around, double maxHeightDifference,
        SpawnRequest request, JumpingSpidersConfig config)
    {
        BlockPos column = new BlockPos(0);
        for (int attempt = 0; attempt < config.SpawnPositionTries; attempt++)
        {
            // Uniform over the disc: the square root keeps spots from bunching up at the centre.
            double angle = request.Rand.NextDouble() * GameMath.TWOPI;
            double distance = Math.Sqrt(request.Rand.NextDouble()) * config.WildSpawnRadiusFromHammock;
            int x = (int)Math.Floor(around.X + Math.Cos(angle) * distance);
            int z = (int)Math.Floor(around.Z + Math.Sin(angle) * distance);
            column.Set(x, (int)around.Y, z);
            if (world.BlockAccessor.GetMapChunkAtBlockPos(column) == null)
            {
                continue;
            }
            int groundY = world.BlockAccessor.GetTerrainMapheightAt(column) + 1;
            for (int y = groundY + GroundSearchUp; y >= groundY - GroundSearchDown; y--)
            {
                if (!jumpWorld.CanStandAt(x, y, z))
                {
                    continue;
                }
                Vec3d feet = new Vec3d(x + 0.5, jumpWorld.GetStandHeight(x, y, z), z + 0.5);
                if (Math.Abs(feet.Y - around.Y) > maxHeightDifference)
                {
                    break;
                }
                if (request.CheckPlayerDistance && !PlayerDistanceOk(request.PlayerPositions, feet, config))
                {
                    break;
                }
                return feet;
            }
        }
        return null;
    }
}
