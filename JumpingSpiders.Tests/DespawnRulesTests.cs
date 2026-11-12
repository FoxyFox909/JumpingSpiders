using Xunit;

namespace JumpingSpiders.Tests;

public class DespawnRulesTests
{
    [Fact]
    public void WildSpiderMayDespawn()
    {
        Assert.False(DespawnRules.IsKeptByPlayer(0, null, false));
        Assert.False(DespawnRules.IsKeptByPlayer(0, "worldgen", false));
    }

    [Fact]
    public void BredSpiderIsKept()
    {
        Assert.True(DespawnRules.IsKeptByPlayer(1, null, false));
        Assert.True(DespawnRules.IsKeptByPlayer(0, "reproduction", false));
    }

    [Fact]
    public void CaughtOrPlacedSpiderIsKept()
    {
        Assert.True(DespawnRules.IsKeptByPlayer(0, "playerplaced", false));
    }

    [Fact]
    public void FedSpiderStaysKept()
    {
        Assert.True(DespawnRules.IsKeptByPlayer(0, null, true));
    }
}
