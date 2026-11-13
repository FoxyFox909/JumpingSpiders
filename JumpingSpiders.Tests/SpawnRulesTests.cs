using Xunit;

namespace JumpingSpiders.Tests;

public class SpawnRulesTests
{
    [Fact]
    public void ClimateNeedsForestAndMildTemperature()
    {
        Assert.True(SpawnRules.ClimateAllows(0.5f, 15, 0.35f, 2, 32));
        Assert.True(SpawnRules.ClimateAllows(0.35f, 2, 0.35f, 2, 32));
        Assert.False(SpawnRules.ClimateAllows(0.2f, 15, 0.35f, 2, 32));
        Assert.False(SpawnRules.ClimateAllows(0.9f, -5, 0.35f, 2, 32));
        Assert.False(SpawnRules.ClimateAllows(0.9f, 35, 0.35f, 2, 32));
    }

    [Fact]
    public void PlayerMustBeInTheDistanceBand()
    {
        Assert.True(SpawnRules.PlayerDistanceAllows(40, 24, 80));
        Assert.False(SpawnRules.PlayerDistanceAllows(10, 24, 80));
        Assert.False(SpawnRules.PlayerDistanceAllows(100, 24, 80));
        Assert.False(SpawnRules.PlayerDistanceAllows(double.MaxValue, 24, 80));
    }

    [Fact]
    public void CapCountsExistingWildSpiders()
    {
        Assert.True(SpawnRules.HasRoomForWildSpider(0, 1));
        Assert.False(SpawnRules.HasRoomForWildSpider(1, 1));
        Assert.True(SpawnRules.HasRoomForWildSpider(1, 2));
        Assert.False(SpawnRules.HasRoomForWildSpider(0, 0));
    }

    [Fact]
    public void CooldownStartsAtTheLastSpawn()
    {
        Assert.True(SpawnRules.IsCooldownOver(WildHammockRecord.Never, 5, 12));
        Assert.False(SpawnRules.IsCooldownOver(100, 105, 12));
        Assert.True(SpawnRules.IsCooldownOver(100, 112, 12));
    }

    [Fact]
    public void NoSiteChunksAreSearchedAgainLater()
    {
        Assert.True(SpawnRules.ShouldSearchAgain(WildHammockRecord.Never, 5, 72));
        Assert.False(SpawnRules.ShouldSearchAgain(10, 50, 72));
        Assert.True(SpawnRules.ShouldSearchAgain(10, 82, 72));
    }
}
