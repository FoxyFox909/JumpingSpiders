namespace JumpingSpiders;

/// <summary>
/// How one wild spawn attempt ended (SpawnResult.Outcome). Everything except Spawned is a reason for not spawning;
/// SpawnResult.Message has the details in words, which /jspider spawn prints.
/// </summary>
public enum SpawnOutcome
{
    Spawned,
    NotSpiderChunk,
    ChunkNotLoaded,
    Cooldown,
    NoClimateData,
    ClimateUnsuitable,
    Claimed,
    PlayerBuilt,
    NearKeptSpider,
    CapReached,
    NoPlayerNearby,
    NoSpawnRoom,
    EntityTypeMissing,
    Vetoed
}
