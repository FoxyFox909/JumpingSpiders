using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace JumpingSpiders;

/// <summary>
/// The periodic part of wild spawning: every Config.SpawnCheckIntervalSeconds it picks a few loaded spider chunks
/// near players and hands each to WildSpiderSpawner.TrySpawn, which decides and does the rest.
///
/// A game tick listener is a method the game calls again and again on the server main thread at a fixed interval;
/// this class registers one in Start. Vintage Story's own spawner works the same way on a timer.
///
/// Cost of one check: for each player, (2 r + 1)^2 chunk hashes with r = SpawnMaxPlayerDistance / 32 rounded up
/// (49 for the default 80 blocks), a map-chunk lookup for each spider chunk found, then at most
/// Config.MaxSpawnAttemptsPerCheck spawn attempts. An attempt in a chunk whose hammock is already known, or that is
/// refused, costs well under a millisecond (a climate lookup, one entity query, a few block reads). Only an attempt
/// that must find a spot for a new hammock searches the chunk's trees (Config.WildHammockTreesPerAttempt trunks,
/// about 8 ms each), and at most one such search happens per check. Nothing is done when no player is online, when
/// the world's creature spawning is switched off, and never for chunks that are not loaded.
/// </summary>
public class SpiderSpawnSystem
{
    private readonly ICoreServerAPI api;

    public SpiderSpawnSystem(ICoreServerAPI api)
    {
        this.api = api;
    }

    public void Start()
    {
        int intervalMs = (int)(JumpingSpidersModSystem.Config.SpawnCheckIntervalSeconds * 1000);
        if (intervalMs < 1000)
        {
            intervalMs = 1000;
        }
        // The game removes the listener itself when the server shuts down.
        api.Event.RegisterGameTickListener(OnCheck, intervalMs);
    }

    private void OnCheck(float secondsSinceLast)
    {
        // The world setting that switches all creature spawning off (/worldconfig, or the "spawn creatures" option).
        if (!api.WorldManager.SaveGame.EntitySpawning)
        {
            return;
        }
        List<Vec3d> players = GetPlayerPositions(api);
        if (players.Count == 0)
        {
            return;
        }
        RunCheck(players);
    }

    /// <summary>
    /// One check for the given player positions: picks up to Config.MaxSpawnAttemptsPerCheck loaded spider chunks
    /// near them at random and tries a spawn in each (stopping after the first one that had to search trees).
    /// Returns what each attempt did.
    /// </summary>
    public List<SpawnResult> RunCheck(List<Vec3d> players)
    {
        List<SpawnResult> results = new List<SpawnResult>();
        List<ChunkCoord> candidates = FindLoadedSpiderChunksNear(players);
        Shuffle(candidates);

        int attempts = JumpingSpidersModSystem.Config.MaxSpawnAttemptsPerCheck;
        for (int i = 0; i < candidates.Count && i < attempts; i++)
        {
            ChunkCoord chunk = candidates[i];
            BlockPos center = new BlockPos(chunk.X * TreeTrunks.ChunkSize + TreeTrunks.ChunkSize / 2, api.World.SeaLevel,
                chunk.Z * TreeTrunks.ChunkSize + TreeTrunks.ChunkSize / 2, 0);
            SpawnRequest request = SpawnRequest.Create(chunk.X, chunk.Z, SpiderAttributes.IsNight(api.World, center), api.World.Rand);
            request.PlayerPositions = players;
            SpawnResult result = WildSpiderSpawner.TrySpawn(api, request);
            results.Add(result);
            if (result.Outcome == SpawnOutcome.Spawned)
            {
                api.World.Logger.Debug("[jumpingspiders] Wild spawn in chunk {0}, {1}: {2}", chunk.X, chunk.Z, result.Message);
            }
            else
            {
                api.World.Logger.VerboseDebug("[jumpingspiders] No wild spawn in chunk {0}, {1} ({2}): {3}", chunk.X, chunk.Z,
                    result.Outcome, result.Message);
            }
            // Searching a chunk's trees for a hammock spot is the one expensive step: at most one per check.
            if (result.SearchedTrees)
            {
                break;
            }
        }
        return results;
    }

    /// <summary>Feet positions of the players who are in the game, in the normal world (not in another dimension).</summary>
    public static List<Vec3d> GetPlayerPositions(ICoreServerAPI api)
    {
        List<Vec3d> positions = new List<Vec3d>();
        IPlayer[] online = api.World.AllOnlinePlayers;
        for (int i = 0; i < online.Length; i++)
        {
            IServerPlayer player = online[i] as IServerPlayer;
            if (player == null || player.ConnectionState != EnumClientState.Playing || player.Entity == null)
            {
                continue;
            }
            if (player.Entity.Pos.Dimension != 0)
            {
                continue;
            }
            positions.Add(player.Entity.Pos.XYZ);
        }
        return positions;
    }

    // Spider chunks within SpawnMaxPlayerDistance of some player whose map chunk is loaded, each listed once.
    private List<ChunkCoord> FindLoadedSpiderChunksNear(List<Vec3d> players)
    {
        JumpingSpidersConfig config = JumpingSpidersModSystem.Config;
        int radiusChunks = (int)System.Math.Ceiling(config.SpawnMaxPlayerDistance / TreeTrunks.ChunkSize);
        List<ChunkCoord> result = new List<ChunkCoord>();
        HashSet<long> seen = new HashSet<long>();
        for (int p = 0; p < players.Count; p++)
        {
            int playerChunkX = (int)players[p].X / TreeTrunks.ChunkSize;
            int playerChunkZ = (int)players[p].Z / TreeTrunks.ChunkSize;
            List<ChunkCoord> near = SpiderChunkSearch.FindAround(api.World.Seed, playerChunkX, playerChunkZ, radiusChunks,
                config.SpiderChunkFraction);
            for (int i = 0; i < near.Count; i++)
            {
                long key = ((long)near[i].X << 32) | (uint)near[i].Z;
                if (!seen.Add(key))
                {
                    continue;
                }
                if (api.WorldManager.GetMapChunk(near[i].X, near[i].Z) == null)
                {
                    continue;
                }
                result.Add(near[i]);
            }
        }
        return result;
    }

    private void Shuffle(List<ChunkCoord> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = api.World.Rand.Next(i + 1);
            ChunkCoord temp = list[i];
            list[i] = list[j];
            list[j] = temp;
        }
    }
}
