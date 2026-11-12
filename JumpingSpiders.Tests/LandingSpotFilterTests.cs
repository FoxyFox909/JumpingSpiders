using System;
using System.Collections.Generic;
using Xunit;

namespace JumpingSpiders.Tests;

/// <summary>LandingSpotFilter: the "away from the threat" and "near the anchor" rules used by fleeing and exploring.</summary>
public class LandingSpotFilterTests
{
    private static TestBlockWorld FlatWorld()
    {
        TestBlockWorld world = new TestBlockWorld();
        world.Fill(-12, 0, -12, 12, 0, 12);
        return world;
    }

    [Fact]
    public void WithoutRulesEverythingPassesThrough()
    {
        TestBlockWorld world = FlatWorld();
        LandingSpotFilter filter = new LandingSpotFilter(world);
        Assert.True(filter.CanStandAt(3, 1, 3));
        Assert.False(filter.CanStandAt(3, 0, 3));   // inside the floor
        Assert.Equal(world.GetStandHeight(3, 1, 3), filter.GetStandHeight(3, 1, 3));
        Assert.Equal(world.IsFree(0.5, 0.5, 0.5), filter.IsFree(0.5, 0.5, 0.5));
    }

    [Fact]
    public void ThreatRuleRejectsCellsNearTheThreat()
    {
        LandingSpotFilter filter = new LandingSpotFilter(FlatWorld());
        filter.SetThreat(0.5, 0.5, 3);
        Assert.False(filter.CanStandAt(0, 1, 0));
        Assert.False(filter.CanStandAt(2, 1, 0));   // 2 blocks away
        Assert.True(filter.CanStandAt(3, 1, 0));    // exactly 3 blocks away
        Assert.True(filter.CanStandAt(-5, 1, 4));
    }

    [Fact]
    public void AnchorRuleRejectsCellsFarFromTheAnchor()
    {
        LandingSpotFilter filter = new LandingSpotFilter(FlatWorld());
        filter.SetAnchor(0.5, 0.5, 4);
        Assert.True(filter.CanStandAt(0, 1, 0));
        Assert.True(filter.CanStandAt(4, 1, 0));
        Assert.False(filter.CanStandAt(5, 1, 0));
        Assert.False(filter.CanStandAt(-4, 1, -4));   // about 5.7 blocks away diagonally
    }

    [Fact]
    public void FleeJumpsAlwaysGainDistance()
    {
        // The spider at (0.5, 0.5), the threat two blocks to the west. Every random flee jump must land at least
        // 2 + 1.5 blocks from the threat.
        TestBlockWorld world = FlatWorld();
        JumpLimits limits = new JumpLimits();
        for (int seed = 0; seed < 25; seed++)
        {
            LandingSpotFilter filter = new LandingSpotFilter(world);
            filter.SetThreat(-1.5, 0.5, 3.5);
            List<JumpStep> plan = JumpPlanner.PlanJumpToRandomSpot(filter, 0.5, 1, 0.5, 4.5, 1, 0.5, 3, 0, limits, new Random(seed));
            Assert.NotNull(plan);
            double dx = plan[0].ToX - (-1.5);
            double dz = plan[0].ToZ - 0.5;
            Assert.True(Math.Sqrt(dx * dx + dz * dz) >= 3.5, "seed " + seed);
        }
    }

    [Fact]
    public void NoSpotWhenTheRulesLeaveNothing()
    {
        LandingSpotFilter filter = new LandingSpotFilter(FlatWorld());
        filter.SetAnchor(0.5, 0.5, 0.4);   // only the spider's own cell is within the anchor radius
        List<JumpStep> plan = JumpPlanner.PlanJumpToRandomSpot(filter, 0.5, 1, 0.5, 0.5, 1, 0.5, 6, 0, new JumpLimits(), new Random(3));
        Assert.Null(plan);
    }
}
