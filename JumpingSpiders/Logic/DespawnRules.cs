namespace JumpingSpiders;

/// <summary>
/// Which spiders the vanilla despawn behavior may remove. Pure logic, covered by unit tests.
///
/// Wild spiders despawn like other wild animals when no player is near for a while. A spider the player has
/// anything to do with is kept forever:
/// - bred: born from breeding, so its generation is above 0;
/// - caught: released from a basket or trap (vanilla marks it "playerplaced"), which also covers spiders placed
///   from a creature item in creative mode;
/// - fed: it ate a valid meal (farm-born prey), remembered in its saved state.
/// Once kept, always kept: the "kept" mark is saved on the spider and survives growing up.
/// </summary>
public static class DespawnRules
{
    /// <summary>The vanilla "origin" attribute value for creatures released or placed by a player.</summary>
    public const string PlayerPlacedOrigin = "playerplaced";

    /// <summary>The vanilla "origin" attribute value for creatures born from breeding.</summary>
    public const string ReproductionOrigin = "reproduction";

    /// <summary>True when the spider must never despawn.</summary>
    public static bool IsKeptByPlayer(int generation, string origin, bool alreadyMarkedKept)
    {
        if (alreadyMarkedKept)
        {
            return true;
        }
        if (generation > 0)
        {
            return true;
        }
        return origin == PlayerPlacedOrigin || origin == ReproductionOrigin;
    }
}
