using System;
using System.Collections.Generic;
using Xunit;

namespace JumpingSpiders.Tests;

/// <summary>
/// JumpWorldWithExtraBox: planning into a hammock that does not exist yet. Every world has a stone floor in layer
/// y = 0 (feet at y = 1 on it) and a stone pillar at x = 3, z = 0 from y = 1 to y = 5. The future hammock is in
/// the air cell (2, 4, 0), against the pillar's west side, with a floor one sixteenth of a block thick.
/// </summary>
public class JumpWorldWithExtraBoxTests
{
    private const double FloorTop = 0.0625;

    private static TestBlockWorld PillarWorld()
    {
        TestBlockWorld world = new TestBlockWorld();
        world.Fill(-12, 0, -12, 12, 0, 12);
        world.Fill(3, 1, 0, 3, 5, 0);
        return world;
    }

    // The test world's spider is HalfWidth 0.3 each way and Height 0.5 tall.
    private static JumpWorldWithExtraBox WithHammockFloor(TestBlockWorld world, int x, int y, int z)
    {
        return new JumpWorldWithExtraBox(world, x, y, z,
            0, 0, 0, 1, FloorTop, 1,
            -world.HalfWidth, 0, -world.HalfWidth, world.HalfWidth, world.Height, world.HalfWidth);
    }

    [Fact]
    public void EmptyAirCellBecomesStandableWithTheFloor()
    {
        TestBlockWorld world = PillarWorld();
        Assert.False(world.CanStandAt(2, 4, 0));

        JumpWorldWithExtraBox wrapped = WithHammockFloor(world, 2, 4, 0);
        Assert.True(wrapped.CanStandAt(2, 4, 0));
        Assert.Equal(4 + FloorTop, wrapped.GetStandHeight(2, 4, 0), 6);
    }

    [Fact]
    public void TheFloorBlocksTheSpidersBox()
    {
        JumpWorldWithExtraBox wrapped = WithHammockFloor(PillarWorld(), 2, 4, 0);
        // Feet inside the floor: blocked. Feet on top of the floor: free (touching is fine).
        Assert.False(wrapped.IsFree(2.5, 4.0, 0.5));
        Assert.True(wrapped.IsFree(2.5, 4 + FloorTop, 0.5));
        // Box just above the floor in a neighbouring cell column, overlapping it sideways: blocked.
        Assert.False(wrapped.IsFree(1.9, 4.0, 0.5));
        // Far away: whatever the inner world says.
        Assert.True(wrapped.IsFree(-5.5, 1, -5.5));
    }

    [Fact]
    public void OtherCellsAreUnchanged()
    {
        TestBlockWorld world = PillarWorld();
        JumpWorldWithExtraBox wrapped = WithHammockFloor(world, 2, 4, 0);
        Assert.True(wrapped.CanStandAt(0, 1, 0));
        Assert.Equal(1.0, wrapped.GetStandHeight(0, 1, 0), 6);
        Assert.True(wrapped.CanStandAt(3, 6, 0));      // on top of the pillar
        Assert.False(wrapped.CanStandAt(2, 3, 0));     // air under the hammock
        Assert.False(wrapped.CanStandAt(3, 3, 0));     // inside the pillar
    }

    [Fact]
    public void PlannerReachesTheFutureHammock()
    {
        TestBlockWorld world = PillarWorld();
        Assert.Null(JumpPlanner.PlanJumps(world, 0.5, 1, 0.5, 2, 4, 0, new JumpLimits(), new Random(1)));

        JumpWorldWithExtraBox wrapped = WithHammockFloor(world, 2, 4, 0);
        List<JumpStep> plan = JumpPlanner.PlanJumps(wrapped, 0.5, 1, 0.5, 2, 4, 0, new JumpLimits(), new Random(1));
        Assert.NotNull(plan);
        Assert.True(plan.Count >= 1);
        JumpStep last = plan[plan.Count - 1];
        Assert.Equal(2.5, last.ToX, 6);
        Assert.Equal(4 + FloorTop, last.ToY, 6);
        Assert.Equal(0.5, last.ToZ, 6);
    }

    [Fact]
    public void TooHighStaysUnreachable()
    {
        // A pillar 14 blocks high with no perches: a hammock near its top is beyond two jumps of 4 blocks.
        TestBlockWorld world = new TestBlockWorld();
        world.Fill(-12, 0, -12, 12, 0, 12);
        world.Fill(3, 1, 0, 3, 14, 0);
        JumpWorldWithExtraBox wrapped = WithHammockFloor(world, 2, 12, 0);
        Assert.True(wrapped.CanStandAt(2, 12, 0));
        Assert.Null(JumpPlanner.PlanJumps(wrapped, 0.5, 1, 0.5, 2, 12, 0, new JumpLimits(), new Random(1)));
    }
}
