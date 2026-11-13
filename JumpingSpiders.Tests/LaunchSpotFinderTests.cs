using System;
using System.Collections.Generic;
using Xunit;

namespace JumpingSpiders.Tests;

/// <summary>
/// LaunchSpotFinder: where to walk so that a high goal becomes reachable by jumping. Every world has a stone floor
/// in layer y = 0, so a spider standing on it has its feet at y = 1.
/// </summary>
public class LaunchSpotFinderTests
{
    // A floor, a pillar whose top is the goal 7 blocks above the floor (feet at y = 8, too high for one jump),
    // and a lower perch 3 blocks high next to it, so the goal takes two jumps from near the pillar.
    private static TestBlockWorld PillarWorld()
    {
        TestBlockWorld world = new TestBlockWorld();
        world.Fill(-16, 0, -16, 16, 0, 16);
        world.Fill(8, 1, 0, 8, 7, 0);   // the tall pillar; standing on it means feet in cell (8, 8, 0)
        world.Fill(6, 1, 0, 6, 4, 0);   // the perch; standing on it means feet in cell (6, 5, 0)
        return world;
    }

    [Fact]
    public void FarAwayTheGoalCannotBeReachedByJumping()
    {
        TestBlockWorld world = PillarWorld();
        List<JumpStep> plan = JumpPlanner.PlanJumps(world, -9.5, 1, 0.5, 8, 8, 0, new JumpLimits(), new Random(1));
        Assert.Null(plan);
    }

    [Fact]
    public void FindsASpotFromWhichTheGoalIsReachable()
    {
        TestBlockWorld world = PillarWorld();
        JumpLimits limits = new JumpLimits();
        JumpPlanNode spot = LaunchSpotFinder.Find(world, -9.5, 1, 0.5, 8, 8, 0, limits, 6, 1, 4, 4, new Random(1));
        Assert.NotNull(spot);
        Assert.True(world.CanStandAt(spot.CellX, spot.CellY, spot.CellZ));
        List<JumpStep> plan = JumpPlanner.PlanJumps(world, spot.FeetX, spot.FeetY, spot.FeetZ, 8, 8, 0, limits, new Random(2));
        Assert.NotNull(plan);
        Assert.True(plan.Count <= limits.MaxPlannedJumps);
    }

    [Fact]
    public void NothingWhenTheGoalIsOutOfReachFromEverywhere()
    {
        // A pillar 11 blocks high and nothing else: its top is beyond two jumps of 4 blocks up from anywhere.
        TestBlockWorld world = new TestBlockWorld();
        world.Fill(-16, 0, -16, 16, 0, 16);
        world.Fill(8, 1, 0, 8, 11, 0);
        JumpPlanNode spot = LaunchSpotFinder.Find(world, -9.5, 1, 0.5, 8, 12, 0, new JumpLimits(), 6, 1, 4, 4, new Random(1));
        Assert.Null(spot);
    }

    [Fact]
    public void OnlySuggestsSpotsWithinWalkingHeight()
    {
        // From the floor the perch top (3 blocks up) would be a fine launch spot, but the spider cannot walk up there.
        TestBlockWorld world = PillarWorld();
        for (int seed = 0; seed < 10; seed++)
        {
            JumpPlanNode spot = LaunchSpotFinder.Find(world, -9.5, 1, 0.5, 8, 8, 0, new JumpLimits(), 6, 1, 4, 20, new Random(seed));
            Assert.NotNull(spot);
            Assert.True(spot.CellY <= 2, "seed " + seed + " gave height " + spot.CellY);
        }
    }

    [Fact]
    public void NeverReturnsTheSpidersOwnCell()
    {
        TestBlockWorld world = PillarWorld();
        // Standing in a cell that would be the best launch spot (little walking, comfortable distance, a plan works
        // from there): the task only asks when planning from here failed, so the finder must suggest another cell.
        JumpPlanNode spot = LaunchSpotFinder.Find(world, 5.5, 1, -1.5, 8, 8, 0, new JumpLimits(), 6, 1, 4, 12, new Random(1));
        Assert.NotNull(spot);
        Assert.False(spot.CellX == 5 && spot.CellY == 1 && spot.CellZ == -2);
    }
}
