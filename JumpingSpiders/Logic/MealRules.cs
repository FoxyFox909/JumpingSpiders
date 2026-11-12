namespace JumpingSpiders;

/// <summary>
/// Decides what a spider's kill is worth. Pure logic, no game types, so it is covered by unit tests.
///
/// The rules (design decisions 2 and 10):
/// - Any kill makes the spider sated (that part needs no decision and is done by EntityBehaviorJumpingSpider).
/// - Only a kill of a farm-born chicken or hare counts as a meal: the victim's code must start with one of the
///   meal prefixes and its generation must be at least the minimum. Wild prey, raccoons and rival spiders give
///   no meal. Babies never get meal credit (they do not breed and do not hunt).
/// - A meal credits breeding saturation; a meal eaten by a domesticated adult also makes it build a hammock.
///
/// EntityBehaviorJumpingSpider.OnKilledEntity calls these with values read from the game and the config.
/// </summary>
public static class MealRules
{
    /// <summary>
    /// True when the victim's code path (the part after the colon, e.g. "hare-european-adult-male")
    /// starts with one of the meal prefixes (e.g. "hare-"). The domain is ignored, as in vanilla targeting.
    /// </summary>
    public static bool IsMealPrey(string victimCodePath, string[] mealPrefixes)
    {
        if (victimCodePath == null || mealPrefixes == null)
        {
            return false;
        }
        for (int i = 0; i < mealPrefixes.Length; i++)
        {
            string prefix = mealPrefixes[i];
            if (prefix != null && prefix.Length > 0 && victimCodePath.StartsWith(prefix))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>True when this kill counts as a meal: meal prey, farm-born, and eaten by an adult spider.</summary>
    public static bool IsValidMeal(string victimCodePath, int victimGeneration, string[] mealPrefixes,
        int minPreyGeneration, bool hunterIsAdult)
    {
        if (!hunterIsAdult)
        {
            return false;
        }
        if (victimGeneration < minPreyGeneration)
        {
            return false;
        }
        return IsMealPrey(victimCodePath, mealPrefixes);
    }

    /// <summary>True when the spider should try to build a hammock after this kill.</summary>
    public static bool ShouldBuildHammock(bool wasValidMeal, bool hunterIsAdult, bool hunterIsDomesticated)
    {
        return wasValidMeal && hunterIsAdult && hunterIsDomesticated;
    }
}
