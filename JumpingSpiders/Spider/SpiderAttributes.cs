using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace JumpingSpiders;

/// <summary>
/// Static helpers to read and write a spider's saved state. Every area (AI tasks, hammocks, spawning) goes through
/// these instead of touching attribute names directly, so the names live in one place.
///
/// Vintage Story concepts used here:
/// - entity.WatchedAttributes is a tree of named values that is saved with the entity and also sent to the
///   clients whenever it changes. We keep everything under one sub-tree, "jumpingspider".
///   Changing a value inside a sub-tree does not tell the game it changed, so every setter here calls
///   MarkPathDirty("jumpingspider") to have it saved and synced.
/// - "generation" is a vanilla attribute: 0 for wild-caught animals, and parent + 1 for animals born from breeding.
/// - The entity code is the creature type, e.g. jumpingspiders:jumpingspider-adult-female
///   (domain "jumpingspiders", path "jumpingspider-adult-female").
/// - The calendar (world.Calendar) gives the in-game time; TotalHours and TotalDays count from the world's start.
///
/// The setters are meant for the server; clients only read.
/// </summary>
public static class SpiderAttributes
{
    /// <summary>Asset domain of our entities (the part of the code before the colon).</summary>
    public const string Domain = "jumpingspiders";

    /// <summary>All spider entity codes start with this (then age, then sex: jumpingspider-adult-male).</summary>
    public const string CodePathPrefix = "jumpingspider-";

    /// <summary>The WatchedAttributes sub-tree that holds our state.</summary>
    public const string TreeName = "jumpingspider";

    // Names inside the sub-tree.
    private const string SatedUntilHoursKey = "satedUntilHours";
    private const string HomeHammockKey = "home";          // stored by the game as homeX, homeY, homeZ
    private const string SleepingKey = "sleeping";
    private const string CuriosityKey = "curiosity";
    private const string CuriosityDayKey = "curiosityDay";

    /// <summary>The vanilla breeding generation attribute (top level of WatchedAttributes).</summary>
    public const string GenerationKey = "generation";

    // ---------------- What kind of spider ----------------

    /// <summary>True for any of our spider entities, adult or baby.</summary>
    public static bool IsSpider(Entity entity)
    {
        if (entity == null || entity.Code == null)
        {
            return false;
        }
        return entity.Code.Domain == Domain && entity.Code.Path.StartsWith(CodePathPrefix);
    }

    /// <summary>True for adult spiders (code jumpingspider-adult-...).</summary>
    public static bool IsAdult(Entity entity)
    {
        return CodePart(entity, 1) == "adult";
    }

    /// <summary>True for male spiders (code jumpingspider-...-male), adult or baby.</summary>
    public static bool IsMale(Entity entity)
    {
        return CodePart(entity, 2) == "male";
    }

    // Part number index of the code path split at '-', or null if the entity is not a spider or the part is missing.
    private static string CodePart(Entity entity, int index)
    {
        if (!IsSpider(entity))
        {
            return null;
        }
        string[] parts = entity.Code.Path.Split('-');
        if (parts.Length <= index)
        {
            return null;
        }
        return parts[index];
    }

    // ---------------- Generation and domestication ----------------

    /// <summary>The vanilla breeding generation: 0 for wild-caught, parent + 1 for bred.</summary>
    public static int GetGeneration(Entity entity)
    {
        return entity.WatchedAttributes.GetInt(GenerationKey, 0);
    }

    /// <summary>Domesticated spiders are bred ones, from generation Config.DomesticatedFromGeneration on.</summary>
    public static bool IsDomesticated(Entity entity)
    {
        return GetGeneration(entity) >= JumpingSpidersModSystem.Config.DomesticatedFromGeneration;
    }

    // ---------------- Hunger ----------------

    /// <summary>True when the spider is not sated, so it hunts prey and rivals.</summary>
    public static bool IsHungry(Entity entity)
    {
        ITreeAttribute tree = GetTree(entity);
        if (tree == null)
        {
            return true;
        }
        double satedUntil = tree.GetDouble(SatedUntilHoursKey, 0);
        return entity.World.Calendar.TotalHours >= satedUntil;
    }

    /// <summary>Makes the spider sated for the given number of in-game hours from now.</summary>
    public static void SetSatedForHours(Entity entity, double hours)
    {
        ITreeAttribute tree = GetOrCreateTree(entity);
        tree.SetDouble(SatedUntilHoursKey, entity.World.Calendar.TotalHours + hours);
        MarkDirty(entity);
    }

    /// <summary>Makes the spider hungry right away.</summary>
    public static void MakeHungry(Entity entity)
    {
        ITreeAttribute tree = GetTree(entity);
        if (tree == null)
        {
            return;
        }
        tree.RemoveAttribute(SatedUntilHoursKey);
        MarkDirty(entity);
    }

    // ---------------- Home hammock ----------------

    /// <summary>The block position of the spider's home hammock, or null if it has none.</summary>
    public static BlockPos GetHomeHammock(Entity entity)
    {
        ITreeAttribute tree = GetTree(entity);
        if (tree == null)
        {
            return null;
        }
        // GetBlockPos is a helper from the game (TreeAttributeUtil); it reads homeX, homeY and homeZ.
        return tree.GetBlockPos(HomeHammockKey, null);
    }

    public static void SetHomeHammock(Entity entity, BlockPos pos)
    {
        ITreeAttribute tree = GetOrCreateTree(entity);
        tree.SetBlockPos(HomeHammockKey, pos);
        MarkDirty(entity);
    }

    public static void ClearHomeHammock(Entity entity)
    {
        ITreeAttribute tree = GetTree(entity);
        if (tree == null)
        {
            return;
        }
        tree.RemoveAttribute(HomeHammockKey + "X");
        tree.RemoveAttribute(HomeHammockKey + "Y");
        tree.RemoveAttribute(HomeHammockKey + "Z");
        MarkDirty(entity);
    }

    // ---------------- Sleeping ----------------

    public static bool IsSleeping(Entity entity)
    {
        ITreeAttribute tree = GetTree(entity);
        if (tree == null)
        {
            return false;
        }
        return tree.GetBool(SleepingKey, false);
    }

    public static void SetSleeping(Entity entity, bool sleeping)
    {
        ITreeAttribute tree = GetOrCreateTree(entity);
        tree.SetBool(SleepingKey, sleeping);
        MarkDirty(entity);
    }

    // ---------------- Curiosity ----------------

    /// <summary>
    /// How curious the spider is right now (Config.CuriosityMin..CuriosityMax), used by exploration. A new value is
    /// rolled once per in-game day; at night it is multiplied by Config.CuriosityNightMultiplier.
    /// Call on the server: the daily roll is written to the entity.
    /// </summary>
    public static double GetCuriosity(Entity entity)
    {
        JumpingSpidersConfig config = JumpingSpidersModSystem.Config;
        ITreeAttribute tree = GetOrCreateTree(entity);
        int today = (int)Math.Floor(entity.World.Calendar.TotalDays);

        double curiosity;
        if (tree.HasAttribute(CuriosityKey) && tree.GetInt(CuriosityDayKey, -1) == today)
        {
            curiosity = tree.GetDouble(CuriosityKey);
        }
        else
        {
            curiosity = config.CuriosityMin + entity.World.Rand.NextDouble() * (config.CuriosityMax - config.CuriosityMin);
            tree.SetDouble(CuriosityKey, curiosity);
            tree.SetInt(CuriosityDayKey, today);
            MarkDirty(entity);
        }

        if (IsNight(entity.World, entity.Pos.XYZ))
        {
            curiosity = curiosity * config.CuriosityNightMultiplier;
        }
        return curiosity;
    }

    // ---------------- Day and night ----------------

    /// <summary>
    /// True when the sun is below the horizon at this spot. This depends on latitude and season, unlike the
    /// hour of day; vanilla spawning uses the same test.
    /// </summary>
    public static bool IsNight(IWorldAccessor world, Vec3d pos)
    {
        Vec3f sun = world.Calendar.GetSunPosition(pos, world.Calendar.TotalDays);
        return sun.Y < 0;
    }

    public static bool IsNight(IWorldAccessor world, BlockPos pos)
    {
        return IsNight(world, pos.ToVec3d().Add(0.5, 0.5, 0.5));
    }

    // ---------------- Helpers ----------------

    private static ITreeAttribute GetTree(Entity entity)
    {
        return entity.WatchedAttributes.GetTreeAttribute(TreeName);
    }

    private static ITreeAttribute GetOrCreateTree(Entity entity)
    {
        return entity.WatchedAttributes.GetOrAddTreeAttribute(TreeName);
    }

    private static void MarkDirty(Entity entity)
    {
        entity.WatchedAttributes.MarkPathDirty(TreeName);
    }
}
