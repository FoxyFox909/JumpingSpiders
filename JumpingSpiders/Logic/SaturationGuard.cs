namespace JumpingSpiders;

/// <summary>
/// Keeps the "live prey only" rule for breeding. Pure logic, covered by unit tests.
///
/// Vanilla breeding reads a saturation number that the vanilla eat task raises whenever the animal eats from a
/// food source: a trough, a basket trap's bait (also when the trap fails to catch it), or loot on the ground.
/// The spider needs that eat task (it is what walks it into a baited trap), but must not get breeding credit
/// from it. So the spider remembers how much saturation it was credited for real meals ("credited") and
/// EntityBehaviorJumpingSpider holds the real value to that:
/// - saturation above the credited amount came from somewhere else and is taken away again;
/// - saturation below it was used up by breeding, and the credited amount follows it down.
/// </summary>
public static class SaturationGuard
{
    /// <summary>Differences smaller than this (in portions) are float noise, not food.</summary>
    public const float Tolerance = 0.001f;

    /// <summary>
    /// The saturation the spider is allowed to have, given what it has now and what it was credited.
    /// This is also the new credited amount.
    /// </summary>
    public static float AllowedSaturation(float current, float credited)
    {
        if (current > credited + Tolerance)
        {
            return credited;
        }
        return current;
    }

    /// <summary>True when AllowedSaturation would change the current value.</summary>
    public static bool NeedsCorrection(float current, float credited)
    {
        return current > credited + Tolerance;
    }
}
