using Vintagestory.API.Server;

namespace JumpingSpiders;

/// <summary>
/// Starts the wild spider spawner and registers the /jspider debug commands.
/// Called from JumpingSpidersModSystem.StartServerSide: spawning only happens on the server.
/// </summary>
public static class SpawnRegistration
{
    public static void Register(ICoreServerAPI api)
    {
        SpiderSpawnSystem spawner = new SpiderSpawnSystem(api);
        spawner.Start();
        SpiderCommands.Register(api);
    }
}
