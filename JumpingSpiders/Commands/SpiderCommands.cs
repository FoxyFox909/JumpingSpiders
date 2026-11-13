using System.Collections.Generic;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace JumpingSpiders;

/// <summary>
/// The /jspider debug commands for testing the mod in game. Server side; only for players with the "controlserver"
/// privilege (server admins, and the player in single player).
///
/// Vintage Story's command API: api.ChatCommands.Create("jspider") makes the root command, BeginSubCommand /
/// EndSubCommand nest the sub commands, WithArgs lists their arguments (parsers), and HandleWith names the method
/// that runs. A handler gets a TextCommandCallingArgs (who called, where, the parsed arguments) and returns a
/// TextCommandResult whose message the caller sees. Sub commands inherit the root's privilege.
///
/// Commands that act on "the spider" use the spider the player looks at, or else the nearest spider within
/// TargetSearchRange blocks. From the server console, which has no position, the world's spawn point is used.
/// </summary>
public static class SpiderCommands
{
    /// <summary>How far, in blocks, the commands look for the nearest spider when you do not look at one.</summary>
    public const float TargetSearchRange = 32;

    /// <summary>How far, in blocks, /jspider build looks for the hammock it just built (to say where it is).</summary>
    public const double BuiltHammockSearchRange = 24;

    private static ICoreServerAPI sapi;

    public static void Register(ICoreServerAPI api)
    {
        sapi = api;
        CommandArgumentParsers parsers = api.ChatCommands.Parsers;

        api.ChatCommands.Create("jspider")
            .WithDescription("Jumping spider debug commands. Type /jspider to list them.")
            .RequiresPrivilege(Privilege.controlserver)
            .HandleWith(OnHelp)
            .BeginSubCommand("chunk")
                .WithDescription("Is the chunk you stand in a spider chunk? Shows its climate, wild hammock, spider count, and the nearest spider chunks.")
                .HandleWith(OnChunk)
            .EndSubCommand()
            .BeginSubCommand("info")
                .WithDescription("Shows the spider you look at (or the nearest): generation, wild or domesticated, kept, hungry, asleep, home hammock.")
                .HandleWith(OnInfo)
            .EndSubCommand()
            .BeginSubCommand("hungry")
                .WithDescription("Makes the spider you look at (or the nearest) hungry, so it hunts.")
                .HandleWith(OnHungry)
            .EndSubCommand()
            .BeginSubCommand("sated")
                .WithDescription("Makes the spider you look at (or the nearest) sated for the given in-game hours (default: the config's SatedHoursAfterKill).")
                .WithArgs(parsers.OptionalDouble("hours", -1))
                .HandleWith(OnSated)
            .EndSubCommand()
            .BeginSubCommand("generation")
                .WithDescription("Sets the generation of the spider you look at (or the nearest). 0 = wild. 1 or more = domesticated, and kept for good (setting 0 again does not undo kept).")
                .WithArgs(parsers.IntRange("generation", 0, 1000))
                .HandleWith(OnGeneration)
            .EndSubCommand()
            .BeginSubCommand("spawn")
                .WithDescription("Tries a wild spawn in the chunk you stand in, now, and says why it failed. Ignores only the distance-to-players rule and the cooldown. Options: hammock / nohammock (default: the config's chance), day / night (default: the real time).")
                .WithArgs(parsers.OptionalWordRange("hammock", "auto", "hammock", "nohammock"), parsers.OptionalWordRange("time", "now", "day", "night"))
                .HandleWith(OnSpawn)
            .EndSubCommand()
            .BeginSubCommand("build")
                .WithDescription("Makes the domesticated adult spider you look at (or the nearest) try to build a hammock now, as after a meal.")
                .HandleWith(OnBuild)
            .EndSubCommand();
    }

    private static TextCommandResult OnHelp(TextCommandCallingArgs args)
    {
        StringBuilder text = new StringBuilder();
        text.Append("Jumping spider debug commands:");
        foreach (IChatCommand sub in args.Command.Subcommands)
        {
            text.Append("\n/jspider " + sub.Name + " - " + sub.Description);
        }
        return TextCommandResult.Success(text.ToString());
    }

    private static TextCommandResult OnChunk(TextCommandCallingArgs args)
    {
        return TextCommandResult.Success(SpiderCommandText.DescribeChunk(sapi, CallerPosition(args)));
    }

    private static TextCommandResult OnInfo(TextCommandCallingArgs args)
    {
        Entity spider = FindTargetSpider(args);
        if (spider == null)
        {
            return NoSpiderFound();
        }
        return TextCommandResult.Success(SpiderCommandText.DescribeSpider(sapi, spider, CallerPosition(args)));
    }

    private static TextCommandResult OnHungry(TextCommandCallingArgs args)
    {
        Entity spider = FindTargetSpider(args);
        if (spider == null)
        {
            return NoSpiderFound();
        }
        SpiderAttributes.MakeHungry(spider);
        return TextCommandResult.Success("Spider id " + spider.EntityId + " is now hungry.");
    }

    private static TextCommandResult OnSated(TextCommandCallingArgs args)
    {
        Entity spider = FindTargetSpider(args);
        if (spider == null)
        {
            return NoSpiderFound();
        }
        double hours = (double)args[0];
        if (hours < 0)
        {
            hours = JumpingSpidersModSystem.Config.SatedHoursAfterKill;
        }
        SpiderAttributes.SetSatedForHours(spider, hours);
        return TextCommandResult.Success("Spider id " + spider.EntityId + " is now sated for " + hours + " in-game hours.");
    }

    private static TextCommandResult OnGeneration(TextCommandCallingArgs args)
    {
        Entity spider = FindTargetSpider(args);
        if (spider == null)
        {
            return NoSpiderFound();
        }
        int generation = (int)args[0];
        spider.WatchedAttributes.SetInt(SpiderAttributes.GenerationKey, generation);
        string tameness = "wild";
        if (SpiderAttributes.IsDomesticated(spider))
        {
            tameness = "domesticated";
        }
        return TextCommandResult.Success("Spider id " + spider.EntityId + " is now generation " + generation + " (" + tameness
            + "). Its AI tasks pick this up when they next start.");
    }

    private static TextCommandResult OnSpawn(TextCommandCallingArgs args)
    {
        Vec3d pos = CallerPosition(args);
        string hammockOption = WordOrDefault(args[0], "auto");
        string timeOption = WordOrDefault(args[1], "now");
        int chunkX = (int)pos.X / TreeTrunks.ChunkSize;
        int chunkZ = (int)pos.Z / TreeTrunks.ChunkSize;

        bool night = SpiderAttributes.IsNight(sapi.World, pos);
        if (timeOption == "day")
        {
            night = false;
        }
        else if (timeOption == "night")
        {
            night = true;
        }

        SpawnRequest request = SpawnRequest.Create(chunkX, chunkZ, night, sapi.World.Rand);
        if (hammockOption == "hammock")
        {
            request.HammockChance = 1;
        }
        else if (hammockOption == "nohammock")
        {
            request.HammockChance = 0;
        }
        request.CheckPlayerDistance = false;
        request.CheckCooldown = false;
        request.PlayerPositions = SpiderSpawnSystem.GetPlayerPositions(sapi);

        SpawnResult result = WildSpiderSpawner.TrySpawn(sapi, request);
        StringBuilder text = new StringBuilder();
        text.Append(result.Outcome + ": " + result.Message);
        if (result.Outcome == SpawnOutcome.NotSpiderChunk)
        {
            text.Append("\n" + SpiderCommandText.DescribeNearestSpiderChunks(sapi, pos, chunkX, chunkZ));
        }
        if (result.Spider != null)
        {
            text.Append("\n" + SpiderCommandText.DescribeSpider(sapi, result.Spider, pos));
        }
        return TextCommandResult.Success(text.ToString());
    }

    private static TextCommandResult OnBuild(TextCommandCallingArgs args)
    {
        Entity spider = FindTargetSpider(args);
        if (spider == null)
        {
            return NoSpiderFound();
        }
        EntityAgent agent = spider as EntityAgent;
        if (agent == null || !SpiderAttributes.IsAdult(spider))
        {
            return TextCommandResult.Error("Only adult spiders build hammocks.");
        }
        if (!SpiderAttributes.IsDomesticated(spider))
        {
            return TextCommandResult.Error("Spider id " + spider.EntityId + " is wild; wild spiders never build. Try /jspider generation 1 first.");
        }

        List<BlockEntitySilkHammock> before = HammockFinder.FindAllWithin(sapi.World, spider.Pos.XYZ, BuiltHammockSearchRange);
        bool built = HammockBuilder.TryBuildHammockFor(agent);
        if (!built)
        {
            return TextCommandResult.Success("Spider id " + spider.EntityId + " found no spot it could jump into (within "
                + JumpingSpidersModSystem.Config.HammockBuildSearchRadius + " blocks, keeping the hammock spacing).");
        }
        List<BlockEntitySilkHammock> after = HammockFinder.FindAllWithin(sapi.World, spider.Pos.XYZ, BuiltHammockSearchRange);
        for (int i = 0; i < after.Count; i++)
        {
            if (!before.Contains(after[i]))
            {
                return TextCommandResult.Success("Spider id " + spider.EntityId + " built a hammock at "
                    + SpiderCommandText.ShowPos(sapi, after[i].Pos) + ".");
            }
        }
        return TextCommandResult.Success("Spider id " + spider.EntityId + " built a hammock.");
    }

    // ---------------- Helpers ----------------

    /// <summary>Where the command was run: the player's position, or the world spawn point for the server console.</summary>
    private static Vec3d CallerPosition(TextCommandCallingArgs args)
    {
        if (args.Caller.Pos != null)
        {
            return args.Caller.Pos;
        }
        EntityPos spawn = sapi.World.DefaultSpawnPosition;
        return new Vec3d(spawn.X, spawn.Y, spawn.Z);
    }

    /// <summary>The spider the player looks at, or the nearest living spider within TargetSearchRange, or null.</summary>
    private static Entity FindTargetSpider(TextCommandCallingArgs args)
    {
        IPlayer player = args.Caller.Player;
        if (player != null && player.CurrentEntitySelection != null)
        {
            Entity looked = player.CurrentEntitySelection.Entity;
            if (looked != null && looked.Alive && SpiderAttributes.IsSpider(looked))
            {
                return looked;
            }
        }
        return sapi.World.GetNearestEntity(CallerPosition(args), TargetSearchRange, TargetSearchRange, IsLivingSpider);
    }

    private static bool IsLivingSpider(Entity entity)
    {
        return entity.Alive && SpiderAttributes.IsSpider(entity);
    }

    private static TextCommandResult NoSpiderFound()
    {
        return TextCommandResult.Error("No jumping spider where you look or within " + TargetSearchRange + " blocks.");
    }

    // Optional word arguments are null when left out.
    private static string WordOrDefault(object value, string fallback)
    {
        string word = value as string;
        if (string.IsNullOrEmpty(word))
        {
            return fallback;
        }
        return word.ToLowerInvariant();
    }
}
