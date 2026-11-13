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

    /// <summary>How many jumps the planner may chain (2 = one intermediate perch).</summary>
    public int MaxPlannedJumps = 2;

    /// <summary>Safety timeout of one jump in the game, and the longest flight the solver plans, in seconds.</summary>
    public double MaxFlightSeconds = 3;

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
    /// to bed: at curiosity 1, 3 hours; at 0.2, 36 minutes. Meanwhile they explore (at the night curiosity) and hunt
    /// when hungry. 0 = they go to bed at dusk, like wild spiders always do. A spider already asleep stays asleep.
    /// </summary>
    public double CuriosityStayUpHoursMax = 3;

    /// <summary>
    /// Staying up after dusk never takes more than this share (0..1) of the night, so on short nights curious
    /// spiders still get some sleep. 1 = no limit (a night shorter than the stay-up time is then spent awake).
    /// </summary>
    public double CuriosityStayUpMaxNightShare = 0.5;

    // ---------------- Hammocks ----------------

    /// <summary>At most one hammock per box of this many blocks on each side.</summary>
    public int HammockSpacing = 4;

    /// <summary>Horizontal radius, in blocks, around the spider in which build sites are sampled.</summary>
    public int HammockBuildSearchRadius = 8;

    /// <summary>
    /// How many random block columns around the spider are looked at per build attempt. Every cell of a sampled
    /// column between HammockBuildSearchDown and HammockBuildSearchUp that can hold a hammock becomes a candidate.
    /// Cheap (no jump planning). With too few, a lone post or tree beside the spider is often not looked at at all,
    /// so the spider builds on the floor: 60 missed a post about one time in four, 150 hardly ever.
    /// </summary>
    public int HammockBuildSamples = 150;

    /// <summary>Highest build site looked at, in blocks above the spider's feet (two jumps of MaxJumpUp reach 8).</summary>
    public int HammockBuildSearchUp = 8;

    /// <summary>Lowest build site looked at, in blocks below the spider's feet.</summary>
    public int HammockBuildSearchDown = 3;

    /// <summary>
    /// How many candidate sites a build attempt checks with the jump planner before giving up. Each check is one
    /// planner search, the expensive part, so this caps the cost of one attempt. In a walled pen the highest
    /// candidates (wall tops, the outside of the wall) cannot be reached and use up checks: with 6 checks about one
    /// build in five failed there, with 20 about one in twenty-five. Measured cost of a whole build attempt with 20:
    /// 6 ms on average, under 40 ms at worst, once per meal.
    /// </summary>
    public int HammockBuildMaxPlanAttempts = 20;

    /// <summary>
    /// Which supporting block a new hammock uses when a cell has several, best first, as directions from the hammock
    /// to its support: north, east, south, west (walls such as tree trunks), up (hanging under a branch or ceiling),
    /// down (lying on the ground or a branch). Directions left out are never chosen by HammockBuilder.FindSupportFacing,
    /// which spiders use when they build. A player placing a hammock gets the face they click on, whatever this says.
    /// </summary>
    public string[] HammockSupportPreference = new string[] { "north", "east", "south", "west", "up", "down" };

    /// <summary>
    /// A hammock forgets the spider that claimed it when that spider is farther away than this, in blocks
    /// (a safety net in case a sleep task ends without releasing the hammock).
    /// </summary>
    public double HammockClaimMaxDistance = 32;

    /// <summary>
    /// Preference for high build sites: weight = (height above the lowest candidate + 1) ^ bias. With 4, a site
    /// 3 blocks up weighs as much as 256 floor sites, so a few high spots beat a whole floor of low ones; with 2 it was
    /// only 16, and on open ground beside a post half the hammocks ended up at floor level. Raising it further makes builds in
    /// walled pens fail more often (the unreachable wall tops get picked first; see HammockBuildMaxPlanAttempts).
    /// </summary>
    public double HammockHeightBias = 4.0;

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
