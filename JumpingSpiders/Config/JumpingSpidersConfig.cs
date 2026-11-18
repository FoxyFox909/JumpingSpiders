using System.Collections.Generic;

namespace JumpingSpiders;

/// <summary>
/// All global rules of the mod, in one class that the game saves as ModConfig/jumpingspiders.json.
///
/// Vintage Story's mod-config API (api.LoadModConfig / api.StoreModConfig) turns the public fields of this class
/// into JSON and back, so every field here is a setting a player can edit. JumpingSpidersModSystem loads it when
/// the server starts and writes it back, which creates the file with the defaults below on the first run and adds
/// any new fields to an older file. Code reads it everywhere as JumpingSpidersModSystem.Config.
///
/// The numbers are placeholders to be tuned by playing. Per-task tuning (wind-up times, damage, cooldowns) is not
/// here but in the entity JSON's "aitasks", which is the Vintage Story convention.
/// </summary>
public class JumpingSpidersConfig
{
    // ---------------- Debugging ----------------

    /// <summary>
    /// true: every spider writes what it decides and why (each jump, hunt, flee, bedtime, nap and build attempt) to the
    /// server's debug log (Logs/server-debug.log), and so does wild spawning. Switch it on when reporting a problem.
    /// false (default): none of that is written, or even put together. /jspider replies are not affected.
    /// </summary>
    public bool DebugLogging = false;

    // ---------------- Taming, hunger, meals ----------------

    /// <summary>
    /// A spider whose "generation" is at least this is domesticated. Wild-caught spiders are generation 0.
    /// The entity JSON gates some behaviour by generation itself, with the default of 1 written in: the player
    /// flee tasks and the "aggressiveondamage" emotion state have maxGeneration 0, the explore task has
    /// minGeneration 1. When changing this, change those to this value minus 1 and this value, or the two disagree.
    /// </summary>
    public int DomesticatedFromGeneration = 1;

    /// <summary>A killed chicken or hare only counts as a meal when its generation is at least this (1 = farm-born).</summary>
    public int MinPreyGenerationForMeal = 1;

    /// <summary>After any kill the spider ignores prey and rivals for this many in-game hours.</summary>
    public double SatedHoursAfterKill = 12;

    /// <summary>Entity code beginnings (code path, no domain) of animals whose kill can count as a meal.</summary>
    public string[] MealPreyCodePrefixes = new string[] { "chicken-", "hare-" };

    /// <summary>Saturation credited to the vanilla breeding counter ("hunger.saturation") per meal, in portions.</summary>
    public float SaturationPerMeal = 1;

    // ---------------- Physical jump ability (every jump and the planner) ----------------

    /// <summary>Preferred launch angle above the horizontal, in degrees.</summary>
    public double JumpAngleDeg = 50;

    /// <summary>Flattest launch angle used when the preferred one cannot work, in degrees.</summary>
    public double MinJumpAngleDeg = 20;

    /// <summary>Steepest launch angle used when the preferred one cannot work, in degrees.</summary>
    public double MaxJumpAngleDeg = 80;

    /// <summary>
    /// Launch speed cap, in blocks per second. About 18 is needed to reach MaxJumpUp = 4: the game's gravity is
    /// strong (about 22 blocks/s^2), so even straight up 14 blocks per second only reaches 3.4 blocks.
    /// </summary>
    public double MaxJumpSpeed = 18;

    /// <summary>Highest landing point above the take-off point, in blocks.</summary>
    public double MaxJumpUp = 4;

    /// <summary>Deepest landing point below the take-off point, in blocks.</summary>
    public double MaxJumpDown = 6;

    /// <summary>Longest horizontal distance of one jump, in blocks.</summary>
    public double MaxJumpHorizontal = 6;

    /// <summary>When jumping up, how far above the landing height the arc must peak, in blocks.</summary>
    public double JumpLedgeClearance = 0.4;

    /// <summary>
    /// How many jumps the planner may chain (2 = one intermediate perch). Each extra jump multiplies the work of one
    /// search: with 2 a search costs at most a few milliseconds, with 3 or more tens of milliseconds (about 40 ms
    /// measured for a goal that cannot be reached, which is when a search does the most), so keep it at 2.
    /// </summary>
    public int MaxPlannedJumps = 2;

    /// <summary>
    /// The longest flight the solver plans, in seconds of the natural flight (at JumpFlightSpeedMultiplier 1).
    /// The game also gives up a flight after this time divided by JumpFlightSpeedMultiplier (a safety timeout).
    /// </summary>
    public double MaxFlightSeconds = 3;

    /// <summary>
    /// How many times as fast as the game's own gravity would carry it a spider flies through the air. Every jump
    /// (attack, flee, exploring, going to a hammock) follows the same arc and lands on the same spot whatever this is;
    /// only the time in the air changes. 1.45 = the arc is flown 1.45 times as fast, so a jump is over in about 69% of
    /// the time (0.5 s becomes about 0.35 s). 1 = the natural flight, exactly as the game's physics would fly it.
    /// The jump limits above (reach, MaxJumpSpeed, MaxFlightSeconds) describe the natural arc and stay as they are.
    /// </summary>
    public double JumpFlightSpeedMultiplier = 1.45;

    /// <summary>If the spider has not left the ground this many seconds after a launch, the jump is given up.</summary>
    public double JumpLiftoffTimeoutSeconds = 0.3;

    // ---------------- Curiosity (exploration jumps) ----------------

    /// <summary>Lowest daily curiosity roll (0..1). Rolled once per in-game day per spider.</summary>
    public double CuriosityMin = 0.2;

    /// <summary>Highest daily curiosity roll (0..1).</summary>
    public double CuriosityMax = 1.0;

    /// <summary>Curiosity is multiplied by this at night.</summary>
    public double CuriosityNightMultiplier = 0.2;

    /// <summary>
    /// Domesticated spiders stay up after dusk for (today's curiosity roll times this) in-game hours before they go
    /// to bed: at curiosity 1, 1.8 hours; at 0.2, about 22 minutes. Meanwhile they explore (at the night curiosity) and
    /// hunt when hungry. 0 = they go to bed at dusk, like wild spiders always do. A spider already asleep stays asleep.
    /// </summary>
    public double CuriosityStayUpHoursMax = 1.8;

    /// <summary>
    /// Staying up after dusk never takes more than this share (0..1) of the night, so on short nights curious
    /// spiders still get some sleep. 1 = no limit (a night shorter than the stay-up time is then spent awake).
    /// </summary>
    public double CuriosityStayUpMaxNightShare = 0.5;

    // ---------------- Daytime naps ----------------

    /// <summary>
    /// By day a spider may take a nap in a hammock (never on the bare ground). The day is cut into slots this many
    /// in-game hours long, shifted per spider by a random 0 to NapSlotHours rolled at the start of each in-game day, and
    /// the spider rolls for a nap once per slot. Every spider that sleeps at night naps, wild ones in their home hammock.
    /// </summary>
    public double NapSlotHours = 2;

    /// <summary>
    /// The chance (0..1) per slot that the least curious spider naps. The chance falls in a straight line with the
    /// day's curiosity roll c (CuriosityMin..CuriosityMax, without the night factor):
    /// chance = NapChanceMax * (CuriosityMax - c) / (CuriosityMax - CuriosityMin).
    /// With the defaults: curiosity 0.2 gives 0.3, 0.6 gives 0.15, 1.0 gives 0. A day has about six slots of daylight,
    /// so the least curious spiders nap about twice a day, middling ones about once, the most curious never.
    /// 0 = no naps at all.
    /// </summary>
    public double NapChanceMax = 0.3;

    /// <summary>Shortest nap, in in-game minutes, counted from falling asleep in the hammock.</summary>
    public double NapMinMinutes = 30;

    /// <summary>Longest nap, in in-game minutes. A nap still going at dusk simply goes on as the night's sleep.</summary>
    public double NapMaxMinutes = 60;

    // ---------------- Hammocks ----------------

    /// <summary>
    /// At most one hammock per box of this many blocks on each side, for every hammock whoever placed it: two hammocks
    /// less than this many blocks apart on every axis at once are too close (see Logic/HammockSpacing.cs).
    /// A build attempt whose randomly picked spot breaks this rule fails, and the spider tries again an hour later.
    /// </summary>
    public int HammockSpacing = 3;

    /// <summary>
    /// false: any kill by a domesticated adult earns it one hammock to build. true: only a valid meal does (a
    /// farm-born chicken or hare, the same kills that count for breeding).
    /// </summary>
    public bool HammockNeedsFarmPrey = false;

    /// <summary>
    /// A domesticated adult that earned a hammock with a kill makes one build attempt this many in-game hours apart
    /// while it is sated, until one succeeds (then none until its next kill). The first comes as soon as it stands
    /// on the ground after the kill. The time of the next attempt is saved with the spider.
    /// </summary>
    public double HammockBuildIntervalHours = 1;

    /// <summary>
    /// A spider only builds when it is this close to the hammock's cell, in blocks: from the middle of its body to the
    /// nearest point of the cell, with nothing solid in between. At 0.5 it has to be right against the cell: standing
    /// on the floor beside a floor or low wall spot, or holding on to the wall or ceiling right next to or under a
    /// higher spot (it jumps there and holds on while it builds). It gets onto the exact spot with a short sidle or
    /// crawl (the build task's maxSidleDistance and maxCrawlDistance).
    /// </summary>
    public double HammockBuildRange = 0.5;

    /// <summary>
    /// Horizontal radius, in blocks, around the spider in which a build attempt picks its spot. The spot is picked at
    /// random among all cells in this cylinder that could hold a hammock, every one equally likely, high or low.
    /// </summary>
    public int HammockBuildSearchRadius = 6;

    /// <summary>
    /// Highest spot picked, in blocks above the spider's feet. From the floor a spider reaches spots up to 4 blocks up
    /// by holding on to the wall right below them (a jump of MaxJumpUp = 4, plus the reach of HammockBuildRange);
    /// higher picks only work from a perch, and otherwise just fail (rarely: they are seldom in sight from below).
    /// </summary>
    public int HammockBuildSearchUp = 5;

    /// <summary>Lowest spot picked, in blocks below the spider's feet.</summary>
    public int HammockBuildSearchDown = 2;

    /// <summary>
    /// Which supporting block a new hammock uses when a cell has several, best first, as directions from the hammock
    /// to its support: north, east, south, west (walls such as tree trunks), up (hanging under a branch or ceiling),
    /// down (lying on the ground or a branch). Directions left out are never chosen by HammockBuilder.ChooseBuildFacing,
    /// which spiders use when they build. A player placing a hammock gets the face they click on, whatever this says.
    /// </summary>
    public string[] HammockSupportPreference = new string[] { "north", "east", "south", "west", "up", "down" };

    /// <summary>
    /// A hammock forgets the spider that claimed it when that spider is farther away than this, in blocks
    /// (a safety net in case a sleep task ends without releasing the hammock).
    /// </summary>
    public double HammockClaimMaxDistance = 32;

    /// <summary>How far, in blocks, a spider looks for a free hammock at night.</summary>
    public double HammockSleepSearchRange = 24;

    /// <summary>A wild spider forgets its home hammock when it is farther than this, in blocks.</summary>
    public double HomeHammockForgetDistance = 48;

    // ---------------- Wild spawning ----------------

    /// <summary>Fraction (0..1) of chunk columns that are spider chunks.</summary>
    public double SpiderChunkFraction = 0.04;

    /// <summary>How often the spawner checks the spider chunks near players, in real seconds.</summary>
    public double SpawnCheckIntervalSeconds = 20;

    /// <summary>
    /// Most spider chunks given a spawn attempt per check, over all players together. Bounds the work of one check.
    /// Chunks still on their cooldown or at their cap are passed over without counting.
    /// </summary>
    public int MaxSpawnAttemptsPerCheck = 2;

    /// <summary>Most wild spiders alive at once per spider chunk.</summary>
    public int MaxWildSpidersPerSpiderChunk = 1;

    /// <summary>After a wild spider spawns in a spider chunk, that chunk waits this many in-game hours before the next.</summary>
    public double SpiderChunkSpawnCooldownHours = 12;

    /// <summary>
    /// Chance (0..1) that a newly spawned wild spider comes with a hammock: the spider chunk's existing one, or a new
    /// one placed on a tree. A spider that rolls no hammock has no home.
    /// </summary>
    public double WildHammockChance = 0.75;

    /// <summary>
    /// Before placing a new wild hammock, any hammock within this many blocks of the spider chunk's centre (at ground
    /// height) is reused instead. 24 covers the whole chunk column at ground height.
    /// </summary>
    public double WildHammockReuseRange = 24;

    /// <summary>Preference for high spots on the trunk: weight = (height above the lowest reachable spot + 1) ^ bias.</summary>
    public double WildHammockHeightBias = 2.0;

    /// <summary>
    /// Most tree trunks whose hammock spots are tested with the jump planner per placement attempt (the expensive
    /// part of placing a wild hammock: about 8 ms per trunk on average, 26 ms at worst, measured on generated forest).
    /// In tests, 2 found a site as often as 4 did.
    /// </summary>
    public int WildHammockTreesPerAttempt = 2;

    /// <summary>Ground spots around a trunk that the reach test jumps from (more = finds more routes, costs more).</summary>
    public int WildHammockGroundStarts = 4;

    /// <summary>How far from the trunk, in blocks, those ground spots may be.</summary>
    public int WildHammockGroundRadius = 3;

    /// <summary>A spider chunk where no usable tree was found is searched again after this many in-game days.</summary>
    public double WildHammockRetryDays = 3;

    /// <summary>Wild spiders spawn at least this far from every player, in blocks.</summary>
    public double SpawnMinPlayerDistance = 24;

    /// <summary>Wild spiders spawn only within this distance of some player, in blocks.</summary>
    public double SpawnMaxPlayerDistance = 80;

    /// <summary>Lowest forest density (0..1) at the spawn spot.</summary>
    public float SpawnMinForest = 0.35f;

    /// <summary>Lowest temperature at the spawn spot, degrees Celsius.</summary>
    public float SpawnMinTemperature = 2;

    /// <summary>Highest temperature at the spawn spot, degrees Celsius.</summary>
    public float SpawnMaxTemperature = 32;

    /// <summary>
    /// Lowest wild hammock position, in blocks above the trunk base (0 = beside the lowest trunk block, which stands
    /// on the ground). Within the range, the highest spots a spider can jump into are preferred.
    /// </summary>
    public int WildHammockMinHeight = 2;

    /// <summary>Highest wild hammock position, in blocks above the trunk base.</summary>
    public int WildHammockMaxHeight = 8;

    /// <summary>
    /// By day, wild spiders spawn on the ground within this many blocks of their hammock (or, without a hammock, of a
    /// random point of the spider chunk). Wild spiders this far outside the chunk still count for its cap.
    /// </summary>
    public int WildSpawnRadiusFromHammock = 10;

    /// <summary>Random ground spots tried per spawn before giving up on finding room for the spider.</summary>
    public int SpawnPositionTries = 12;

    /// <summary>
    /// No wild spawn within this many blocks of a spider the player keeps (caught, placed, bred or fed), so wild
    /// spiders do not turn up in spider farms.
    /// </summary>
    public double SpawnMinKeptSpiderDistance = 48;

    /// <summary>
    /// No wild spawn when players have placed more than this many blocks in the spider chunk column and the eight
    /// columns around it (the game counts blocks placed by hand per chunk). Keeps wild spiders out of bases.
    /// </summary>
    public int SpawnMaxPlayerPlacedBlocks = 20;

    /// <summary>
    /// Corrects, in place, the settings whose value would break the mod: a number that would make the code throw, loop
    /// or divide by zero (a negative search radius, zero intervals and spacings), a share or chance outside 0..1, or the
    /// upper end of a range below its lower end. Returns one sentence per corrected setting, for the server log.
    /// Values that are merely unusual (a huge radius, a small jump) are left alone.
    /// </summary>
    public List<string> Validate()
    {
        JumpingSpidersConfig defaults = new JumpingSpidersConfig();
        SettingCorrector fix = new SettingCorrector();

        SatedHoursAfterKill = fix.AtLeast("SatedHoursAfterKill", SatedHoursAfterKill, 0);
        SaturationPerMeal = (float)fix.AtLeast("SaturationPerMeal", SaturationPerMeal, 0);

        MinJumpAngleDeg = fix.Between("MinJumpAngleDeg", MinJumpAngleDeg, 1, 89);
        MaxJumpAngleDeg = fix.Between("MaxJumpAngleDeg", MaxJumpAngleDeg, 1, 89);
        MaxJumpAngleDeg = fix.NotBelow("MaxJumpAngleDeg", MaxJumpAngleDeg, "MinJumpAngleDeg", MinJumpAngleDeg);
        JumpAngleDeg = fix.Between("JumpAngleDeg", JumpAngleDeg, MinJumpAngleDeg, MaxJumpAngleDeg);
        MaxJumpSpeed = fix.AboveZero("MaxJumpSpeed", MaxJumpSpeed, defaults.MaxJumpSpeed);
        MaxJumpUp = fix.AtLeast("MaxJumpUp", MaxJumpUp, 0);
        MaxJumpDown = fix.AtLeast("MaxJumpDown", MaxJumpDown, 0);
        MaxJumpHorizontal = fix.AboveZero("MaxJumpHorizontal", MaxJumpHorizontal, defaults.MaxJumpHorizontal);
        JumpLedgeClearance = fix.AtLeast("JumpLedgeClearance", JumpLedgeClearance, 0);
        MaxPlannedJumps = fix.AtLeast("MaxPlannedJumps", MaxPlannedJumps, 1);
        MaxFlightSeconds = fix.AboveZero("MaxFlightSeconds", MaxFlightSeconds, defaults.MaxFlightSeconds);
        JumpFlightSpeedMultiplier = fix.AboveZero("JumpFlightSpeedMultiplier", JumpFlightSpeedMultiplier, defaults.JumpFlightSpeedMultiplier);
        JumpLiftoffTimeoutSeconds = fix.AboveZero("JumpLiftoffTimeoutSeconds", JumpLiftoffTimeoutSeconds, defaults.JumpLiftoffTimeoutSeconds);

        CuriosityMin = fix.Between("CuriosityMin", CuriosityMin, 0, 1);
        CuriosityMax = fix.Between("CuriosityMax", CuriosityMax, 0, 1);
        CuriosityMax = fix.NotBelow("CuriosityMax", CuriosityMax, "CuriosityMin", CuriosityMin);
        CuriosityNightMultiplier = fix.AtLeast("CuriosityNightMultiplier", CuriosityNightMultiplier, 0);
        CuriosityStayUpHoursMax = fix.AtLeast("CuriosityStayUpHoursMax", CuriosityStayUpHoursMax, 0);
        CuriosityStayUpMaxNightShare = fix.Between("CuriosityStayUpMaxNightShare", CuriosityStayUpMaxNightShare, 0, 1);

        NapSlotHours = fix.AboveZero("NapSlotHours", NapSlotHours, defaults.NapSlotHours);
        NapChanceMax = fix.Between("NapChanceMax", NapChanceMax, 0, 1);
        NapMinMinutes = fix.AtLeast("NapMinMinutes", NapMinMinutes, 0);
        NapMaxMinutes = fix.NotBelow("NapMaxMinutes", NapMaxMinutes, "NapMinMinutes", NapMinMinutes);

        HammockSpacing = fix.AtLeast("HammockSpacing", HammockSpacing, 1);
        HammockBuildIntervalHours = fix.AboveZero("HammockBuildIntervalHours", HammockBuildIntervalHours, defaults.HammockBuildIntervalHours);
        HammockBuildRange = fix.AtLeast("HammockBuildRange", HammockBuildRange, 0);
        HammockBuildSearchRadius = fix.AtLeast("HammockBuildSearchRadius", HammockBuildSearchRadius, 0);
        // At least one height to pick from: HammockBuildSearchDown below the feet up to HammockBuildSearchUp above.
        HammockBuildSearchUp = fix.NotBelow("HammockBuildSearchUp", HammockBuildSearchUp, "minus HammockBuildSearchDown", -HammockBuildSearchDown);
        HammockClaimMaxDistance = fix.AtLeast("HammockClaimMaxDistance", HammockClaimMaxDistance, 0);
        HammockSleepSearchRange = fix.AtLeast("HammockSleepSearchRange", HammockSleepSearchRange, 0);
        HomeHammockForgetDistance = fix.AtLeast("HomeHammockForgetDistance", HomeHammockForgetDistance, 0);

        SpiderChunkFraction = fix.Between("SpiderChunkFraction", SpiderChunkFraction, 0, 1);
        SpawnCheckIntervalSeconds = fix.AboveZero("SpawnCheckIntervalSeconds", SpawnCheckIntervalSeconds, defaults.SpawnCheckIntervalSeconds);
        MaxSpawnAttemptsPerCheck = fix.AtLeast("MaxSpawnAttemptsPerCheck", MaxSpawnAttemptsPerCheck, 0);
        MaxWildSpidersPerSpiderChunk = fix.AtLeast("MaxWildSpidersPerSpiderChunk", MaxWildSpidersPerSpiderChunk, 0);
        SpiderChunkSpawnCooldownHours = fix.AtLeast("SpiderChunkSpawnCooldownHours", SpiderChunkSpawnCooldownHours, 0);
        WildHammockChance = fix.Between("WildHammockChance", WildHammockChance, 0, 1);
        WildHammockReuseRange = fix.AtLeast("WildHammockReuseRange", WildHammockReuseRange, 0);
        WildHammockHeightBias = fix.AtLeast("WildHammockHeightBias", WildHammockHeightBias, 0);
        WildHammockTreesPerAttempt = fix.AtLeast("WildHammockTreesPerAttempt", WildHammockTreesPerAttempt, 0);
        WildHammockGroundStarts = fix.AtLeast("WildHammockGroundStarts", WildHammockGroundStarts, 0);
        WildHammockGroundRadius = fix.AtLeast("WildHammockGroundRadius", WildHammockGroundRadius, 0);
        WildHammockRetryDays = fix.AtLeast("WildHammockRetryDays", WildHammockRetryDays, 0);
        SpawnMinPlayerDistance = fix.AtLeast("SpawnMinPlayerDistance", SpawnMinPlayerDistance, 0);
        SpawnMaxPlayerDistance = fix.NotBelow("SpawnMaxPlayerDistance", SpawnMaxPlayerDistance, "SpawnMinPlayerDistance", SpawnMinPlayerDistance);
        SpawnMinForest = (float)fix.Between("SpawnMinForest", SpawnMinForest, 0, 1);
        SpawnMaxTemperature = (float)fix.NotBelow("SpawnMaxTemperature", SpawnMaxTemperature, "SpawnMinTemperature", SpawnMinTemperature);
        WildHammockMinHeight = fix.AtLeast("WildHammockMinHeight", WildHammockMinHeight, 0);
        WildHammockMaxHeight = fix.NotBelow("WildHammockMaxHeight", WildHammockMaxHeight, "WildHammockMinHeight", WildHammockMinHeight);
        WildSpawnRadiusFromHammock = fix.AtLeast("WildSpawnRadiusFromHammock", WildSpawnRadiusFromHammock, 0);
        SpawnPositionTries = fix.AtLeast("SpawnPositionTries", SpawnPositionTries, 0);
        SpawnMinKeptSpiderDistance = fix.AtLeast("SpawnMinKeptSpiderDistance", SpawnMinKeptSpiderDistance, 0);
        return fix.Warnings;
    }

    /// <summary>
    /// The jump fields as a JumpLimits object, the form the jump code in the Logic folder works with.
    /// Make a fresh one when you need it; it is cheap.
    /// </summary>
    public JumpLimits CreateJumpLimits()
    {
        JumpLimits limits = new JumpLimits();
        limits.JumpAngleDeg = JumpAngleDeg;
        limits.MinJumpAngleDeg = MinJumpAngleDeg;
        limits.MaxJumpAngleDeg = MaxJumpAngleDeg;
        limits.MaxJumpSpeed = MaxJumpSpeed;
        limits.MaxJumpUp = MaxJumpUp;
        limits.MaxJumpDown = MaxJumpDown;
        limits.MaxJumpHorizontal = MaxJumpHorizontal;
        limits.LedgeClearance = JumpLedgeClearance;
        limits.MaxPlannedJumps = MaxPlannedJumps;
        limits.MaxFlightSeconds = MaxFlightSeconds;
        return limits;
    }
}
