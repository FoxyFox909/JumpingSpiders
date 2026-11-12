using System;
using System.Collections.Generic;
using Vintagestory.API.MathTools;

namespace JumpingSpiders;

/// <summary>
/// Everything WildSpiderSpawner.TrySpawn needs to try one wild spawn in one spider chunk, spelled out so that the
/// periodic spawner, the /jspider spawn command and tests can each choose them. Plain data.
/// Use Create to start from the config's values.
/// </summary>
public class SpawnRequest
{
    /// <summary>The spider chunk column (chunk coordinates: block X / 32, block Z / 32).</summary>
    public int ChunkX;
    public int ChunkZ;

    /// <summary>True: a spider with a hammock spawns asleep in it. The spawner passes SpiderAttributes.IsNight.</summary>
    public bool Night;

    /// <summary>Chance (0..1) that the spider comes with a hammock. 1 = always, 0 = never.</summary>
    public double HammockChance;

    /// <summary>Feet positions of the players to keep the distance rules against.</summary>
    public List<Vec3d> PlayerPositions = new List<Vec3d>();

    /// <summary>
    /// When false, the distance-to-players rules are skipped (for /jspider spawn: the player stands right there).
    /// </summary>
    public bool CheckPlayerDistance = true;

    /// <summary>When false, the per spider chunk cooldown after the last spawn is skipped.</summary>
    public bool CheckCooldown = true;

    /// <summary>Most tree trunks tested with the jump planner when a new hammock must be placed.</summary>
    public int MaxTreesToTest;

    public Random Rand;

    /// <summary>A request with the config's values: hammock chance, tree budget, all rules on, no players.</summary>
    public static SpawnRequest Create(int chunkX, int chunkZ, bool night, Random rand)
    {
        JumpingSpidersConfig config = JumpingSpidersModSystem.Config;
        SpawnRequest request = new SpawnRequest();
        request.ChunkX = chunkX;
        request.ChunkZ = chunkZ;
        request.Night = night;
        request.HammockChance = config.WildHammockChance;
        request.MaxTreesToTest = config.WildHammockTreesPerAttempt;
        request.Rand = rand;
        return request;
    }
}
