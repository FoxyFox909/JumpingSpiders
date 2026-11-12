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

    /// <summary>A spider whose "generation" is at least this is domesticated. Wild-caught spiders are generation 0.</summary>
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

    // ---------------- Hammocks ----------------

    /// <summary>At most one hammock per box of this many blocks on each side.</summary>
    public int HammockSpacing = 4;

    /// <summary>Horizontal radius, in blocks, around the spider in which build sites are sampled.</summary>
    public int HammockBuildSearchRadius = 8;

    /// <summary>
    /// How many random block columns around the spider are looked at per build attempt. Every cell of a sampled
    /// column between HammockBuildSearchDown and HammockBuildSearchUp that can hold a hammock becomes a candidate.
    /// </summary>
    public int HammockBuildSamples = 60;

    /// <summary>Highest build site looked at, in blocks above the spider's feet (two jumps of MaxJumpUp reach 8).</summary>
    public int HammockBuildSearchUp = 8;

    /// <summary>Lowest build site looked at, in blocks below the spider's feet.</summary>
    public int HammockBuildSearchDown = 3;

    /// <summary>
    /// How many candidate sites a build attempt checks with the jump planner before giving up. Each check is one
    /// planner search, the expensive part, so this caps the cost of one attempt.
    /// </summary>
    public int HammockBuildMaxPlanAttempts = 6;

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

    /// <summary>Preference for high build sites: weight = (height above the lowest candidate + 1) ^ bias.</summary>
    public double HammockHeightBias = 2.0;

    /// <summary>How far, in blocks, a spider looks for a free hammock at night.</summary>
    public double HammockSleepSearchRange = 24;

    /// <summary>A wild spider forgets its home hammock when it is farther than this, in blocks.</summary>
    public double HomeHammockForgetDistance = 48;

    // ---------------- Wild spawning ----------------

    /// <summary>Fraction (0..1) of chunk columns that are spider chunks.</summary>
    public double SpiderChunkFraction = 0.04;

    /// <summary>How often the spawner checks the spider chunks near players, in real seconds.</summary>
    public double SpawnCheckIntervalSeconds = 20;

    /// <summary>Most wild spiders alive at once per spider chunk.</summary>
    public int MaxWildSpidersPerSpiderChunk = 1;

    /// <summary>Chance (0..1) that a spider chunk gets a wild hammock.</summary>
    public double WildHammockChance = 0.75;

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

    /// <summary>Lowest wild hammock position, in blocks above the trunk base.</summary>
    public int WildHammockMinHeight = 2;

    /// <summary>Highest wild hammock position, in blocks above the trunk base.</summary>
    public int WildHammockMaxHeight = 8;

    /// <summary>Wild spiders spawn within this many blocks of their chunk's hammock.</summary>
    public int WildSpawnRadiusFromHammock = 10;

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
