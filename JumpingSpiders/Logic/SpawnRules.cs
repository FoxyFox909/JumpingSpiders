namespace JumpingSpiders;

/// <summary>
/// The yes/no rules of wild spawning that need nothing from the game, so the unit tests can check them.
/// WildSpiderSpawner (Spawning folder) gathers the numbers from the world and asks these.
/// </summary>
public static class SpawnRules
{
    /// <summary>
    /// True when the climate allows wild spiders: forest density (0..1) at least minForest, and the temperature
    /// (degrees Celsius) within minTemperature..maxTemperature.
    /// </summary>
    public static bool ClimateAllows(float forest, float temperature, float minForest, float minTemperature, float maxTemperature)
    {
        if (forest < minForest)
        {
            return false;
        }
        return temperature >= minTemperature && temperature <= maxTemperature;
    }

    /// <summary>
    /// True when the nearest player is far enough not to see the spider appear, but near enough that spawning
    /// here matters (distances in blocks). With no player at all, pass double.MaxValue: that is refused.
    /// </summary>
    public static bool PlayerDistanceAllows(double nearestPlayerDistance, double minDistance, double maxDistance)
    {
        return nearestPlayerDistance >= minDistance && nearestPlayerDistance <= maxDistance;
    }

    /// <summary>True when fewer than maxWildSpiders wild spiders already live in the spider chunk.</summary>
    public static bool HasRoomForWildSpider(int wildSpiderCount, int maxWildSpiders)
    {
        return wildSpiderCount < maxWildSpiders;
    }

    /// <summary>
    /// True when enough in-game hours have passed since the chunk's last spawn (lastSpawnHours, or
    /// WildHammockRecord.Never) for it to spawn again.
    /// </summary>
    public static bool IsCooldownOver(double lastSpawnHours, double nowHours, double cooldownHours)
    {
        if (lastSpawnHours < 0)
        {
            return true;
        }
        return nowHours - lastSpawnHours >= cooldownHours;
    }

    /// <summary>
    /// True when a spider chunk whose last hammock spot search found nothing (at searchedHours, or Never) may be
    /// searched again: trees grow and players fell them, so "no site" is not forever.
    /// </summary>
    public static bool ShouldSearchAgain(double searchedHours, double nowHours, double retryHours)
    {
        if (searchedHours < 0)
        {
            return true;
        }
        return nowHours - searchedHours >= retryHours;
    }
}
