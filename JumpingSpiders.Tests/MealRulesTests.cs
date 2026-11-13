using Xunit;

namespace JumpingSpiders.Tests;

public class MealRulesTests
{
    private static readonly string[] Prefixes = new string[] { "chicken-", "hare-" };

    [Fact]
    public void FarmBornHareAndChickenAreMeals()
    {
        Assert.True(MealRules.IsValidMeal("hare-european-adult-male", 1, Prefixes, 1, true));
        Assert.True(MealRules.IsValidMeal("chicken-hen", 3, Prefixes, 1, true));
        Assert.True(MealRules.IsValidMeal("chicken-roosterpoult", 1, Prefixes, 1, true));
        Assert.True(MealRules.IsValidMeal("chicken-baby", 2, Prefixes, 1, true));
    }

    [Fact]
    public void WildPreyIsNoMeal()
    {
        Assert.False(MealRules.IsValidMeal("hare-european-adult-male", 0, Prefixes, 1, true));
        Assert.False(MealRules.IsValidMeal("chicken-hen", 0, Prefixes, 1, true));
    }

    [Fact]
    public void RaccoonsAndRivalSpidersAreNoMeal()
    {
        Assert.False(MealRules.IsValidMeal("raccoon-common-adult-male", 5, Prefixes, 1, true));
        Assert.False(MealRules.IsValidMeal("jumpingspider-adult-male", 5, Prefixes, 1, true));
    }

    [Fact]
    public void BabiesGetNoMealCredit()
    {
        Assert.False(MealRules.IsValidMeal("hare-european-adult-male", 1, Prefixes, 1, false));
    }

    [Fact]
    public void MinimumGenerationIsConfigurable()
    {
        Assert.True(MealRules.IsValidMeal("hare-cape-baby-female", 0, Prefixes, 0, true));
        Assert.False(MealRules.IsValidMeal("hare-cape-baby-female", 2, Prefixes, 3, true));
    }

    [Fact]
    public void PrefixMustMatchTheStart()
    {
        Assert.False(MealRules.IsMealPrey("wildhare-x", Prefixes));
        Assert.False(MealRules.IsMealPrey("hare", Prefixes));
        Assert.True(MealRules.IsMealPrey("hare-x", Prefixes));
    }

    [Fact]
    public void MissingDataIsNoMeal()
    {
        Assert.False(MealRules.IsMealPrey(null, Prefixes));
        Assert.False(MealRules.IsMealPrey("hare-x", null));
        Assert.False(MealRules.IsMealPrey("hare-x", new string[] { null, "" }));
    }

    [Fact]
    public void OnlyDomesticatedAdultsBuildAfterAMeal()
    {
        Assert.True(MealRules.ShouldBuildHammock(true, true, true));
        Assert.False(MealRules.ShouldBuildHammock(false, true, true));
        Assert.False(MealRules.ShouldBuildHammock(true, true, false));
        Assert.False(MealRules.ShouldBuildHammock(true, false, true));
    }
}
