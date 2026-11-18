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
    private const string KeptByPlayerKey = "keptByPlayer";
    private const string CreditedSaturationKey = "creditedSaturation";
    private const string DomesticatedFlagKey = "domesticated";
    private const string BuildArmedKey = "buildArmed";
    private const string NextBuildAttemptHoursKey = "nextBuildHours";
    private const string NapShiftKey = "napShift";
    private const string NapShiftDayKey = "napShiftDay";
    private const string NapRollDayKey = "napRollDay";
    private const string NapRollSlotKey = "napRollSlot";
    private const string NapUntilHoursKey = "napUntilHours";

    /// <summary>The vanilla breeding generation attribute (top level of WatchedAttributes).</summary>
    public const string GenerationKey = "generation";

    /// <summary>
    /// The vanilla texture variant attribute (top level of WatchedAttributes): 0 draws the entity's first texture's
    /// "base", 1 its first alternate, and so on. The server rolls it once, when an entity whose first texture has
    /// alternates enters the world without one, and saves it with the entity.
    /// </summary>
    public const string TextureIndexKey = "textureIndex";

    /// <summary>
    /// The vanilla breeding counter: WatchedAttributes tree "hunger", value "saturation", in food portions.
    /// The vanilla eat task raises it, the vanilla "multiply" behavior lowers it when a female gets pregnant.
    /// </summary>
    public const string HungerTreeName = "hunger";
    private const string SaturationKey = "saturation";

    /// <summary>Vanilla: total in-game hours of the last meal; read by the vanilla body-weight simulation.</summary>
    public const string LastMealHoursKey = "lastMealEatenTotalHours";

    /// <summary>Vanilla: where a creature came from ("playerplaced", "reproduction", ...), in entity.Attributes.</summary>
    public const string OriginKey = "origin";

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

    /// <summary>True for a spider that is alive; also a matcher for the game's entity searches.</summary>
    public static bool IsLivingSpider(Entity entity)
    {
        return entity.Alive && IsSpider(entity);
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

    /// <summary>The spider's texture variant (see TextureIndexKey), or -1 when it has none.</summary>
    public static int GetTextureIndex(Entity entity)
    {
        if (!entity.WatchedAttributes.HasAttribute(TextureIndexKey))
        {
            return -1;
        }
        return entity.WatchedAttributes.GetInt(TextureIndexKey, 0);
    }

    /// <summary>
    /// How many texture variants the spider's type has (base plus alternates; 1 when it has no alternates). On the
    /// server the textures themselves are not kept, only the count of alternates.
    /// </summary>
    public static int GetTextureVariantCount(Entity entity)
    {
        if (entity.Properties == null || entity.Properties.Client == null)
        {
            return 1;
        }
        return entity.Properties.Client.TexturesAlternatesCount + 1;
    }

    /// <summary>Domesticated spiders are bred ones, from generation Config.DomesticatedFromGeneration on.</summary>
    public static bool IsDomesticated(Entity entity)
    {
        return GetGeneration(entity) >= JumpingSpidersModSystem.Config.DomesticatedFromGeneration;
    }

    /// <summary>
    /// The domesticated state as the server last wrote it. Clients use this for display, because a multiplayer
    /// client does not load the server's config (so IsDomesticated could disagree there).
    /// </summary>
    public static bool GetSyncedDomesticated(Entity entity)
    {
        ITreeAttribute tree = GetTree(entity);
        if (tree == null)
        {
            return false;
        }
        return tree.GetBool(DomesticatedFlagKey, false);
    }

    /// <summary>Server: writes the domesticated state for clients. Does nothing when it did not change.</summary>
    public static void SetSyncedDomesticated(Entity entity, bool domesticated)
    {
        ITreeAttribute tree = GetOrCreateTree(entity);
        if (tree.HasAttribute(DomesticatedFlagKey) && tree.GetBool(DomesticatedFlagKey) == domesticated)
        {
            return;
        }
        tree.SetBool(DomesticatedFlagKey, domesticated);
        MarkDirty(entity);
    }

    // ---------------- Kept by the player (never despawns) ----------------

    /// <summary>True once the spider was caught, placed, bred or fed by the player. Never cleared.</summary>
    public static bool IsMarkedKeptByPlayer(Entity entity)
    {
        ITreeAttribute tree = GetTree(entity);
        if (tree == null)
        {
            return false;
        }
        return tree.GetBool(KeptByPlayerKey, false);
    }

    public static void MarkKeptByPlayer(Entity entity)
    {
        if (IsMarkedKeptByPlayer(entity))
        {
            return;
        }
        ITreeAttribute tree = GetOrCreateTree(entity);
        tree.SetBool(KeptByPlayerKey, true);
        MarkDirty(entity);
    }

    /// <summary>
    /// True when the player keeps this spider (caught, placed, bred or fed; see DespawnRules), so it never despawns.
    /// Reads the origin, which only the server has.
    /// </summary>
    public static bool IsKept(Entity entity)
    {
        return DespawnRules.IsKeptByPlayer(GetGeneration(entity), GetOrigin(entity), IsMarkedKeptByPlayer(entity));
    }

    /// <summary>The vanilla "origin" of the creature (entity.Attributes, server only), or null.</summary>
    public static string GetOrigin(Entity entity)
    {
        return entity.Attributes.GetString(OriginKey, null);
    }

    // ---------------- Breeding saturation (vanilla counter plus our credit) ----------------

    /// <summary>The vanilla breeding saturation, in food portions.</summary>
    public static float GetBreedingSaturation(Entity entity)
    {
        ITreeAttribute hunger = entity.WatchedAttributes.GetTreeAttribute(HungerTreeName);
        if (hunger == null)
        {
            return 0;
        }
        return hunger.GetFloat(SaturationKey, 0);
    }

    /// <summary>
    /// Sets the vanilla breeding saturation. MarkPathDirty("hunger") also runs the modified-listeners on that
    /// path, including the spider's own saturation guard.
    /// </summary>
    public static void SetBreedingSaturation(Entity entity, float saturation)
    {
        ITreeAttribute hunger = entity.WatchedAttributes.GetOrAddTreeAttribute(HungerTreeName);
        hunger.SetFloat(SaturationKey, saturation);
        entity.WatchedAttributes.MarkPathDirty(HungerTreeName);
    }

    /// <summary>True once the spider has a credited-saturation record (every spider gets one on its first load).</summary>
    public static bool HasCreditedSaturation(Entity entity)
    {
        ITreeAttribute tree = GetTree(entity);
        return tree != null && tree.HasAttribute(CreditedSaturationKey);
    }

    /// <summary>How much of the breeding saturation came from real meals (live farm-born prey), in portions.</summary>
    public static float GetCreditedSaturation(Entity entity)
    {
        ITreeAttribute tree = GetTree(entity);
        if (tree == null)
        {
            return 0;
        }
        return tree.GetFloat(CreditedSaturationKey, 0);
    }

    public static void SetCreditedSaturation(Entity entity, float saturation)
    {
        ITreeAttribute tree = GetOrCreateTree(entity);
        tree.SetFloat(CreditedSaturationKey, saturation);
        MarkDirty(entity);
    }

    // ---------------- Growing up ----------------

    /// <summary>
    /// Copies all of our saved state from one entity to another. Used when a baby turns into an adult, because the
    /// vanilla grow behavior only copies the generation to the new adult entity.
    /// </summary>
    public static void CopyStateTo(Entity from, Entity to)
    {
        ITreeAttribute tree = GetTree(from);
        if (tree == null)
        {
            return;
        }
        to.WatchedAttributes.SetAttribute(TreeName, tree.Clone());
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
        double curiosity = GetDailyCuriosity(entity);
        if (IsNight(entity.World, entity.Pos.XYZ))
        {
            curiosity = curiosity * JumpingSpidersModSystem.Config.CuriosityNightMultiplier;
        }
        return curiosity;
    }

    /// <summary>
    /// Today's curiosity roll (Config.CuriosityMin..CuriosityMax), without the night factor. Rolled on the first call
    /// of an in-game day, whoever asks first (exploring or the sleep task), so both see the same value. A day starts
    /// at midnight. Call on the server: the roll is written to the entity.
    /// </summary>
    public static double GetDailyCuriosity(Entity entity)
    {
        JumpingSpidersConfig config = JumpingSpidersModSystem.Config;
        ITreeAttribute tree = GetOrCreateTree(entity);
        int today = (int)Math.Floor(entity.World.Calendar.TotalDays);

        if (tree.HasAttribute(CuriosityKey) && tree.GetInt(CuriosityDayKey, -1) == today)
        {
            return tree.GetDouble(CuriosityKey);
        }
        double curiosity = config.CuriosityMin + entity.World.Rand.NextDouble() * (config.CuriosityMax - config.CuriosityMin);
        tree.SetDouble(CuriosityKey, curiosity);
        tree.SetInt(CuriosityDayKey, today);
        MarkDirty(entity);
        return curiosity;
    }

    /// <summary>
    /// How long this spider stays up after dusk today, in in-game hours: today's curiosity roll times
    /// Config.CuriosityStayUpHoursMax for domesticated spiders, 0 for wild ones (they go to bed at dusk).
    /// </summary>
    public static double GetStayUpHours(Entity entity)
    {
        if (!IsDomesticated(entity))
        {
            return 0;
        }
        return StayUpAfterDusk.StayUpHours(GetDailyCuriosity(entity), JumpingSpidersModSystem.Config.CuriosityStayUpHoursMax);
    }

    /// <summary>
    /// True while a domesticated spider is still within its stay-up time after dusk (GetStayUpHours, limited to
    /// Config.CuriosityStayUpMaxNightShare of the night). Worked out from the sun, not from a timer, so it is right
    /// after a chunk reload or a restart too. The sleep task asks this before it sends the spider to bed.
    /// </summary>
    public static bool IsStayingUpAfterDusk(Entity entity)
    {
        double hours = GetStayUpHours(entity);
        if (hours <= 0)
        {
            return false;
        }
        IGameCalendar calendar = entity.World.Calendar;
        SunHeightAt sun = new SunHeightAt(calendar, entity.Pos.XYZ);
        return StayUpAfterDusk.IsStayingUp(sun, calendar.TotalDays, calendar.HoursPerDay, hours,
            JumpingSpidersModSystem.Config.CuriosityStayUpMaxNightShare);
    }

    // ---------------- Hammock building (domesticated adults) ----------------

    /// <summary>
    /// True while a domesticated adult owes a hammock for a kill: set by the kill, cleared by the build attempt
    /// that succeeds. AiTaskSpiderBuildHammock makes its hourly attempts while this is set and the spider is sated.
    /// </summary>
    public static bool IsHammockBuildArmed(Entity entity)
    {
        ITreeAttribute tree = GetTree(entity);
        if (tree == null)
        {
            return false;
        }
        return tree.GetBool(BuildArmedKey, false);
    }

    public static void SetHammockBuildArmed(Entity entity, bool armed)
    {
        ITreeAttribute tree = GetOrCreateTree(entity);
        tree.SetBool(BuildArmedKey, armed);
        MarkDirty(entity);
    }

    /// <summary>In-game total hours at which the next hourly build attempt may start (0 when none was made yet).</summary>
    public static double GetNextBuildAttemptHours(Entity entity)
    {
        ITreeAttribute tree = GetTree(entity);
        if (tree == null)
        {
            return 0;
        }
        return tree.GetDouble(NextBuildAttemptHoursKey, 0);
    }

    public static void SetNextBuildAttemptHours(Entity entity, double totalHours)
    {
        ITreeAttribute tree = GetOrCreateTree(entity);
        tree.SetDouble(NextBuildAttemptHoursKey, totalHours);
        MarkDirty(entity);
    }

    // ---------------- Daytime naps ----------------

    /// <summary>
    /// How many in-game hours after midnight today's nap slots begin (0 up to Config.NapSlotHours), see NapSchedule.
    /// Rolled on the first call of an in-game day. Call on the server: the roll is written to the entity.
    /// </summary>
    public static double GetNapShiftHours(Entity entity)
    {
        JumpingSpidersConfig config = JumpingSpidersModSystem.Config;
        ITreeAttribute tree = GetOrCreateTree(entity);
        int today = (int)Math.Floor(entity.World.Calendar.TotalDays);
        if (tree.HasAttribute(NapShiftKey) && tree.GetInt(NapShiftDayKey, -1) == today)
        {
            return tree.GetDouble(NapShiftKey);
        }
        double shift = entity.World.Rand.NextDouble() * Math.Max(0, config.NapSlotHours);
        tree.SetDouble(NapShiftKey, shift);
        tree.SetInt(NapShiftDayKey, today);
        MarkDirty(entity);
        return shift;
    }

    /// <summary>The nap slot the spider is in now (see NapSchedule), and today's day number in day.</summary>
    public static int GetNapSlot(Entity entity, out int day)
    {
        IGameCalendar calendar = entity.World.Calendar;
        day = (int)Math.Floor(calendar.TotalDays);
        double hourOfDay = NapSchedule.HourOfDay(calendar.TotalHours, calendar.HoursPerDay);
        return NapSchedule.SlotIndex(hourOfDay, GetNapShiftHours(entity), JumpingSpidersModSystem.Config.NapSlotHours);
    }

    /// <summary>True when the spider has already rolled for a nap in this slot of this day.</summary>
    public static bool HasRolledNap(Entity entity, int day, int slot)
    {
        ITreeAttribute tree = GetTree(entity);
        if (tree == null)
        {
            return false;
        }
        return !NapSchedule.IsNewSlot(day, slot, tree.GetInt(NapRollDayKey, int.MinValue), tree.GetInt(NapRollSlotKey, int.MinValue));
    }

    /// <summary>Remembers that the spider rolled for a nap in this slot, so it does not roll again there (saved).</summary>
    public static void MarkNapRolled(Entity entity, int day, int slot)
    {
        ITreeAttribute tree = GetOrCreateTree(entity);
        tree.SetInt(NapRollDayKey, day);
        tree.SetInt(NapRollSlotKey, slot);
        MarkDirty(entity);
    }

    /// <summary>
    /// This spider's chance (0..1) to nap in one slot today, from today's curiosity roll (NapSchedule.NapChance).
    /// Call on the server: it may roll today's curiosity.
    /// </summary>
    public static double GetNapChance(Entity entity)
    {
        JumpingSpidersConfig config = JumpingSpidersModSystem.Config;
        return NapSchedule.NapChance(GetDailyCuriosity(entity), config.CuriosityMin, config.CuriosityMax, config.NapChanceMax);
    }

    /// <summary>In-game total hours at which the nap the spider sleeps now ends, or 0 when it is not napping.</summary>
    public static double GetNapUntilHours(Entity entity)
    {
        ITreeAttribute tree = GetTree(entity);
        if (tree == null)
        {
            return 0;
        }
        return tree.GetDouble(NapUntilHoursKey, 0);
    }

    public static void SetNapUntilHours(Entity entity, double totalHours)
    {
        ITreeAttribute tree = GetOrCreateTree(entity);
        tree.SetDouble(NapUntilHoursKey, totalHours);
        MarkDirty(entity);
    }

    /// <summary>The nap is over (or became the night's sleep). Does nothing when there was none.</summary>
    public static void ClearNap(Entity entity)
    {
        ITreeAttribute tree = GetTree(entity);
        if (tree == null || !tree.HasAttribute(NapUntilHoursKey))
        {
            return;
        }
        tree.RemoveAttribute(NapUntilHoursKey);
        MarkDirty(entity);
    }

    /// <summary>True while the spider sleeps a daytime nap (asleep, and the nap's end has not come).</summary>
    public static bool IsNapping(Entity entity)
    {
        return IsSleeping(entity) && GetNapUntilHours(entity) > entity.World.Calendar.TotalHours;
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
