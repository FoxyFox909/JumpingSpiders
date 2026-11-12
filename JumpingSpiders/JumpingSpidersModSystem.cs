using System;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace JumpingSpiders;

/// <summary>
/// The entry point of the mod. Vintage Story finds every ModSystem subclass in the mod's DLL and calls:
/// - Start(api) on both the server and the client, for registrations both sides need (block, block entity
///   and entity behavior classes, so the client can build the same objects from the data the server sends);
/// - StartServerSide(api) only on the server, for server-only things (AI tasks, spawning, commands).
/// In single player both sides run in the same process.
///
/// This class loads the config and calls each area's Register method on the right side(s).
/// </summary>
public class JumpingSpidersModSystem : ModSystem
{
    /// <summary>File name of the config inside the game's ModConfig folder.</summary>
    public const string ConfigFileName = "jumpingspiders.json";

    /// <summary>
    /// The mod's settings. Starts as the defaults so that code running before the server has loaded the file
    /// (or on a multiplayer client, which never loads it) still sees sensible values.
    /// </summary>
    public static JumpingSpidersConfig Config = new JumpingSpidersConfig();

    public override void Start(ICoreAPI api)
    {
        SpiderRegistration.Register(api);
        HammockRegistration.Register(api);
    }

    public override void StartServerSide(ICoreServerAPI api)
    {
        Config = LoadConfig(api);
        TaskRegistration.Register(api);
        SpawnRegistration.Register(api);
    }

    public override void Dispose()
    {
        // Static fields outlive a world in single player; do not carry settings into the next world.
        Config = new JumpingSpidersConfig();
    }

    // Reads ModConfig/jumpingspiders.json. A missing file gets created with the defaults. A file that cannot be
    // read (for example a typo in the JSON) is left untouched so the player can fix it, and the defaults are used.
    private JumpingSpidersConfig LoadConfig(ICoreAPI api)
    {
        JumpingSpidersConfig loaded = null;
        try
        {
            loaded = api.LoadModConfig<JumpingSpidersConfig>(ConfigFileName);
        }
        catch (Exception e)
        {
            Mod.Logger.Warning("Could not read ModConfig/{0}, using the default settings for this session. Fix or delete the file. Error: {1}",
                ConfigFileName, e.Message);
            return new JumpingSpidersConfig();
        }

        if (loaded == null)
        {
            loaded = new JumpingSpidersConfig();
        }
        if (loaded.MealPreyCodePrefixes == null)
        {
            loaded.MealPreyCodePrefixes = new JumpingSpidersConfig().MealPreyCodePrefixes;
        }

        // Write it back: creates the file on the first run and adds settings that are new in this version.
        try
        {
            api.StoreModConfig(loaded, ConfigFileName);
        }
        catch (Exception e)
        {
            Mod.Logger.Warning("Could not write ModConfig/{0}: {1}", ConfigFileName, e.Message);
        }
        return loaded;
    }
}
