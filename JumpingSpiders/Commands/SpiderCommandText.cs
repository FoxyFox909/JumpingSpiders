using System;
using System.Collections.Generic;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace JumpingSpiders;

/// <summary>
/// Builds the text the /jspider commands print: the report about one spider and the report about one chunk.
/// Kept apart from SpiderCommands so that file only has to deal with command arguments.
///
/// Positions are printed the way the game's coordinates display shows them to players: X and Z relative to the
/// world's spawn point, Y as is. That is also what /tp expects by default.
/// </summary>
public static class SpiderCommandText
{
    /// <summary>How many nearby spider chunks /jspider chunk lists.</summary>
    public const int NearestChunksShown = 5;

    /// <summary>How far, in chunks, /jspider chunk looks for nearby spider chunks.</summary>
    public const int NearestChunksSearchRadius = 16;

    /// <summary>A position as the player's coordinates display shows it.</summary>
    public static string ShowPos(ICoreAPI api, double x, double y, double z)
    {
        // The game rounds the spawn point down to whole blocks for this (BlockPos.ToLocalPosition does the same).
        EntityPos spawn = api.World.DefaultSpawnPosition;
        return string.Format("{0:0.#}, {1:0.#}, {2:0.#}", x - (int)spawn.X, y, z - (int)spawn.Z);
    }

    public static string ShowPos(ICoreAPI api, BlockPos pos)
    {
        return ShowPos(api, pos.X, pos.Y, pos.Z);
    }

    /// <summary>"north", "south-east" and so on, for a horizontal offset (north is towards smaller Z).</summary>
    public static string Compass(double dx, double dz)
    {
        if (dx * dx + dz * dz < 0.25)
        {
            return "here";
        }
        string[] names = new string[] { "east", "south-east", "south", "south-west", "west", "north-west", "north", "north-east" };
        double angle = Math.Atan2(dz, dx);
        int sector = (int)Math.Round(angle / (Math.PI / 4));
        if (sector < 0)
        {
            sector += 8;
        }
        return names[sector % 8];
    }

    /// <summary>Everything a tester wants to know about one spider, one fact per line.</summary>
    public static string DescribeSpider(ICoreServerAPI api, Entity spider, Vec3d from)
    {
        StringBuilder text = new StringBuilder();
        text.AppendLine(string.Format("{0} (id {1}) at {2}, {3:0.0} blocks away.", spider.Code, spider.EntityId,
            ShowPos(api, spider.Pos.X, spider.Pos.Y, spider.Pos.Z), spider.Pos.XYZ.DistanceTo(from)));

        int generation = SpiderAttributes.GetGeneration(spider);
        string tameness = "wild";
        if (SpiderAttributes.IsDomesticated(spider))
        {
            tameness = "domesticated";
        }
        text.AppendLine("Generation " + generation + ": " + tameness + " (domesticated from generation "
            + JumpingSpidersModSystem.Config.DomesticatedFromGeneration + ").");

        string origin = SpiderAttributes.GetOrigin(spider);
        if (origin == null)
        {
            origin = "none";
        }
        bool kept = DespawnRules.IsKeptByPlayer(generation, SpiderAttributes.GetOrigin(spider), SpiderAttributes.IsMarkedKeptByPlayer(spider));
        string keptText = "no (may despawn when no player is near)";
        if (kept)
        {
            keptText = "yes (never despawns)";
        }
        text.AppendLine("Kept by a player: " + keptText + "; origin: " + origin + ".");

        if (SpiderAttributes.IsHungry(spider))
        {
            text.AppendLine("Hungry: yes, it hunts prey and same-sex rivals.");
        }
        else
        {
            text.AppendLine("Hungry: no, sated (/jspider hungry changes that).");
        }

        string asleep = "no";
        if (SpiderAttributes.IsSleeping(spider))
        {
            asleep = "yes";
        }
        string night = "day";
        if (SpiderAttributes.IsNight(api.World, spider.Pos.XYZ))
        {
            night = "night";
        }
        text.AppendLine("Asleep: " + asleep + " (it is " + night + " there).");

        // Reading the curiosity rolls today's value if nothing has asked for it yet; that is the same roll the
        // spider would get anyway.
        string stayUp = "goes to bed at dusk";
        double stayUpHours = SpiderAttributes.GetStayUpHours(spider);
        if (stayUpHours > 0)
        {
            stayUp = string.Format("stays up {0:0.0} h after dusk", stayUpHours);
            if (SpiderAttributes.IsStayingUpAfterDusk(spider))
            {
                stayUp = stayUp + ", and is staying up now";
            }
        }
        text.AppendLine(string.Format("Curiosity today: {0:0.00} ({1:0.00} right now); {2}.",
            SpiderAttributes.GetDailyCuriosity(spider), SpiderAttributes.GetCuriosity(spider), stayUp));

        BlockPos home = SpiderAttributes.GetHomeHammock(spider);
        if (home == null)
        {
            text.AppendLine("Home hammock: none.");
        }
        else
        {
            string present = "missing (no hammock there, or its chunk is not loaded)";
            BlockEntitySilkHammock hammock = HammockFinder.GetHammockAt(api.World, home);
            if (hammock != null)
            {
                present = "present";
                Entity occupant = hammock.GetLiveOccupant();
                if (occupant == spider)
                {
                    present = "present, this spider is in it";
                }
                else if (occupant != null)
                {
                    present = "present, taken by spider id " + occupant.EntityId;
                }
            }
            text.AppendLine(string.Format("Home hammock: {0}, {1:0.0} blocks away, {2}.", ShowPos(api, home),
                spider.Pos.XYZ.DistanceTo(HammockFinder.CellCenter(home)), present));
        }

        text.Append(string.Format("Breeding saturation: {0:0.0} portions (from real meals: {1:0.0}).",
            SpiderAttributes.GetBreedingSaturation(spider), SpiderAttributes.GetCreditedSaturation(spider)));
        return text.ToString();
    }

    /// <summary>The spider chunk facts about the chunk column containing pos, and the nearest spider chunks.</summary>
    public static string DescribeChunk(ICoreServerAPI api, Vec3d pos)
    {
        JumpingSpidersConfig config = JumpingSpidersModSystem.Config;
        int size = TreeTrunks.ChunkSize;
        int chunkX = (int)pos.X / size;
        int chunkZ = (int)pos.Z / size;
        StringBuilder text = new StringBuilder();

        bool spiderChunk = SpiderChunks.IsSpiderChunk(api.World.Seed, chunkX, chunkZ, config.SpiderChunkFraction);
        string isOrNot = "is NOT a spider chunk";
        if (spiderChunk)
        {
            isOrNot = "IS a spider chunk";
        }
        text.AppendLine(string.Format("You are in chunk {0}, {1} (from {2} to {3}): it {4}.", chunkX, chunkZ,
            ShowPos(api, chunkX * size, pos.Y, chunkZ * size), ShowPos(api, chunkX * size + size - 1, pos.Y, chunkZ * size + size - 1), isOrNot));

        IMapChunk mapChunk = api.WorldManager.GetMapChunk(chunkX, chunkZ);
        if (mapChunk != null)
        {
            BlockPos center = WildHammockSites.ChunkCenterAtGround(mapChunk, chunkX, chunkZ);
            ClimateCondition climate = WildSpiderSpawner.GetSpawnClimate(api.World, center);
            if (climate == null)
            {
                text.AppendLine("Climate: no data.");
            }
            else
            {
                string verdict = "unsuitable";
                if (WildSpiderSpawner.ClimateAllows(climate))
                {
                    verdict = "suitable";
                }
                text.AppendLine("Climate: " + WildSpiderSpawner.DescribeClimate(climate) + ": " + verdict + ".");
            }

            if (spiderChunk)
            {
                text.AppendLine(DescribeRecord(api, WildHammockRecords.Load(mapChunk)));
                WildSpiderCensus census = WildSpiderCensus.Take(api.World, chunkX, chunkZ);
                string kept = "none near";
                if (census.NearestKeptDistance >= 0)
                {
                    kept = string.Format("nearest {0:0} blocks away (wild spawns need {1})", census.NearestKeptDistance, config.SpawnMinKeptSpiderDistance);
                }
                text.AppendLine("Wild spiders counted for this chunk: " + census.WildSpiders + " (cap " + config.MaxWildSpidersPerSpiderChunk
                    + "). Kept spiders: " + kept + ".");
                string claimed = "no";
                if (WildSpiderSpawner.IsClaimed(api.World, chunkX, chunkZ, config.WildSpawnRadiusFromHammock))
                {
                    claimed = "yes";
                }
                text.AppendLine("Land claim here: " + claimed + ". Player-placed blocks here and around: "
                    + WildSpiderSpawner.CountPlayerPlacedBlocks(api, chunkX, chunkZ) + " (limit " + config.SpawnMaxPlayerPlacedBlocks + ").");
            }
        }

        text.Append(DescribeNearestSpiderChunks(api, pos, chunkX, chunkZ));
        return text.ToString();
    }

    /// <summary>The nearest few spider chunks (other than the one at chunkX, chunkZ), with direction and distance.</summary>
    public static string DescribeNearestSpiderChunks(ICoreServerAPI api, Vec3d pos, int chunkX, int chunkZ)
    {
        JumpingSpidersConfig config = JumpingSpidersModSystem.Config;
        int size = TreeTrunks.ChunkSize;
        List<ChunkCoord> near = SpiderChunkSearch.FindAround(api.World.Seed, chunkX, chunkZ, NearestChunksSearchRadius, config.SpiderChunkFraction);
        StringBuilder text = new StringBuilder();
        text.Append("Nearest spider chunks:");
        int shown = 0;
        for (int i = 0; i < near.Count && shown < NearestChunksShown; i++)
        {
            if (near[i].X == chunkX && near[i].Z == chunkZ)
            {
                continue;
            }
            double centerX = near[i].X * size + size / 2.0;
            double centerZ = near[i].Z * size + size / 2.0;
            double dx = centerX - pos.X;
            double dz = centerZ - pos.Z;
            text.Append(string.Format("\n  chunk {0}, {1}: centre {2}, {3:0} blocks {4}", near[i].X, near[i].Z,
                ShowPos(api, centerX, pos.Y, centerZ), Math.Sqrt(dx * dx + dz * dz), Compass(dx, dz)));
            shown++;
        }
        if (shown == 0)
        {
            text.Append(" none within " + NearestChunksSearchRadius + " chunks.");
        }
        return text.ToString();
    }

    private static string DescribeRecord(ICoreServerAPI api, WildHammockRecord record)
    {
        double now = api.World.Calendar.TotalHours;
        string spawned = "never";
        if (record.LastSpawnTotalHours >= 0)
        {
            spawned = string.Format("{0:0.0} in-game hours ago", now - record.LastSpawnTotalHours);
        }
        if (record.State == WildHammockState.HasHammock)
        {
            BlockPos pos = new BlockPos(record.X, record.Y, record.Z, 0);
            string present = "missing";
            if (HammockFinder.GetHammockAt(api.World, pos) != null)
            {
                present = "present";
            }
            return "Wild hammock: " + ShowPos(api, pos) + " (" + present + "). Last wild spawn: " + spawned + ".";
        }
        if (record.State == WildHammockState.NoSite)
        {
            return string.Format("Wild hammock: none, no usable tree found {0:0.0} in-game hours ago. Last wild spawn: {1}.",
                now - record.SearchedTotalHours, spawned);
        }
        return "Wild hammock: none chosen yet. Last wild spawn: " + spawned + ".";
    }
}
