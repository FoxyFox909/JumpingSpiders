using System;
using System.Collections.Generic;
using Xunit;

namespace JumpingSpiders.Tests;

/// <summary>
/// JumpPlanner on small hand-made block worlds. Every world has a stone floor in layer y = 0, so a spider
/// standing on it has its feet at y = 1. Cells are named (x, y, z) by the block the feet are in.
/// </summary>
public class JumpPlannerTests
{
    private static TestBlockWorld FlatWorld()
    {
        TestBlockWorld world = new TestBlockWorld();
        world.Fill(-12, 0, -12, 12, 0, 12);
        return world;
    }

    [Fact]
    public void DirectJumpOnFlatGround()
    {
        TestBlockWorld world = FlatWorld();
        List<JumpStep> plan = JumpPlanner.PlanJumps(world, 0.5, 1, 0.5, 4, 1, 0, new JumpLimits(), new Random(1));
        Assert.NotNull(plan);
        Assert.Single(plan);
        Assert.Equal(4.5, plan[0].ToX, 6);
        Assert.Equal(1.0, plan[0].ToY, 6);
        Assert.Equal(0.5, plan[0].ToZ, 6);
        Assert.True(JumpPlanner.IsArcClear(world, plan[0]));
    }

    [Fact]
    public void DirectJumpOntoPillarFourUp()
    {
        TestBlockWorld world = FlatWorld();
        world.Fill(3, 1, 0, 3, 4, 0);   // a pillar; its top is at y = 5
        List<JumpStep> plan = JumpPlanner.PlanJumps(world, 0.5, 1, 0.5, 3, 5, 0, new JumpLimits(), new Random(1));
        Assert.NotNull(plan);
        Assert.Single(plan);
        Assert.Equal(5.0, plan[0].ToY, 6);
    }

    [Fact]
    public void JumpOntoSlabLandsOnItsTop()
    {
        TestBlockWorld world = FlatWorld();
        world.SetBlock(3, 1, 0, 0.5);
        List<JumpStep> plan = JumpPlanner.PlanJumps(world, 0.5, 1, 0.5, 3, 1, 0, new JumpLimits(), new Random(1));
        Assert.NotNull(plan);
        Assert.Equal(1.5, plan[0].ToY, 6);
    }

    [Fact]
    public void LowCeilingBlocksTheJump()
    {
        JumpLimits limits = new JumpLimits();
        limits.MinJumpAngleDeg = 35;   // no flat skimming arcs, so the ceiling clearly gets in the way
        limits.MaxPlannedJumps = 1;

        TestBlockWorld open = FlatWorld();
        Assert.NotNull(JumpPlanner.PlanJumps(open, 0.5, 1, 0.5, 4, 1, 0, limits, new Random(1)));

        // Same world with a ceiling one block above the floor: only 0.5 blocks of headroom over the spider.
        TestBlockWorld covered = FlatWorld();
        covered.Fill(-12, 2, -12, 12, 2, 12);
        Assert.Null(JumpPlanner.PlanJumps(covered, 0.5, 1, 0.5, 4, 1, 0, limits, new Random(1)));
    }

    [Fact]
    public void WallBlocksTheJump()
    {
        TestBlockWorld world = FlatWorld();
        // A wall 6 blocks high (too high to jump onto) across the whole world, between start and goal.
        world.Fill(2, 1, -12, 2, 6, 12);
        Assert.Null(JumpPlanner.PlanJumps(world, 0.5, 1, 0.5, 4, 1, 0, new JumpLimits(), new Random(1)));

        // A low wall can be jumped over.
        TestBlockWorld low = FlatWorld();
        low.Fill(2, 1, -12, 2, 1, 12);
        List<JumpStep> plan = JumpPlanner.PlanJumps(low, 0.5, 1, 0.5, 4, 1, 0, new JumpLimits(), new Random(1));
        Assert.NotNull(plan);
        for (int i = 0; i < plan.Count; i++)
        {
            Assert.True(JumpPlanner.IsArcClear(low, plan[i]));
        }
    }

    [Fact]
    public void GoalSevenUpNeedsAnIntermediatePerch()
    {
        TestBlockWorld world = FlatWorld();
        world.Fill(3, 1, 0, 3, 4, 0);   // perch: top at y = 5, 4 up from the start
        world.Fill(6, 1, 0, 6, 7, 0);   // goal pillar: top at y = 8, 7 up from the start

        List<JumpStep> plan = JumpPlanner.PlanJumps(world, 0.5, 1, 0.5, 6, 8, 0, new JumpLimits(), new Random(3));
        Assert.NotNull(plan);
        Assert.Equal(2, plan.Count);
        // The first jump lands on the perch, the second on the goal, and they join up.
        Assert.Equal(3.5, plan[0].ToX, 6);
        Assert.Equal(5.0, plan[0].ToY, 6);
        Assert.Equal(plan[0].ToX, plan[1].FromX, 6);
        Assert.Equal(plan[0].ToY, plan[1].FromY, 6);
        Assert.Equal(6.5, plan[1].ToX, 6);
        Assert.Equal(8.0, plan[1].ToY, 6);

        // Allowed only one jump, the goal is out of reach.
        JumpLimits single = new JumpLimits();
        single.MaxPlannedJumps = 1;
        Assert.Null(JumpPlanner.PlanJumps(world, 0.5, 1, 0.5, 6, 8, 0, single, new Random(3)));
    }

    [Fact]
    public void UnreachableGoalGivesNull()
    {
        TestBlockWorld world = FlatWorld();
        world.Fill(3, 1, 0, 3, 4, 0);   // perch 4 up
        world.Fill(6, 1, 0, 6, 9, 0);   // goal 10 up: even from the perch it is 6 up
        Assert.Null(JumpPlanner.PlanJumps(world, 0.5, 1, 0.5, 6, 10, 0, new JumpLimits(), new Random(1)));
    }

    [Fact]
    public void GoalThatCannotBeStoodOnGivesNull()
    {
        TestBlockWorld world = FlatWorld();
        // Cell (4, 3, 0) is in mid air.
        Assert.Null(JumpPlanner.PlanJumps(world, 0.5, 1, 0.5, 4, 3, 0, new JumpLimits(), new Random(1)));
    }

    [Fact]
    public void AlreadyAtGoalGivesEmptyPlan()
    {
        TestBlockWorld world = FlatWorld();
        List<JumpStep> plan = JumpPlanner.PlanJumps(world, 0.5, 1, 0.5, 0, 1, 0, new JumpLimits(), new Random(1));
        Assert.NotNull(plan);
        Assert.Empty(plan);
    }

    [Fact]
    public void ArcThroughABlockIsNotClear()
    {
        TestBlockWorld world = FlatWorld();
        JumpStep step = JumpMath.CreateJump(0.5, 1, 0.5, 5.5, 1, 0.5, new JumpLimits());
        Assert.True(JumpPlanner.IsArcClear(world, step));

        // Put a block where the spider is half way through the flight.
        double x;
        double y;
        double z;
        JumpMath.PositionAt(step, step.FlightSeconds / 2, out x, out y, out z);
        world.SetBlock((int)Math.Floor(x), (int)Math.Floor(y + 0.25), (int)Math.Floor(z));
        Assert.False(JumpPlanner.IsArcClear(world, step));
    }

    [Fact]
    public void LandingRightAtTheEdgeOfAGapIsNotClear()
    {
        // The engine moves vertically before horizontally, so on its last tick in the air the spider comes down
        // where it was one tick earlier. Landing just past the edge of a block means coming down over the gap.
        TestBlockWorld world = new TestBlockWorld();
        world.HalfWidth = 0.1;
        world.Fill(-2, 0, -2, 0, 0, 2);   // take-off platform
        world.SetBlock(5, 0, 0);          // a single landing block, with a gap before it

        JumpStep atEdge = JumpMath.CreateJump(0.5, 1, 0.5, 5.15, 1, 0.5, new JumpLimits());
        Assert.NotNull(atEdge);
        Assert.False(JumpPlanner.IsArcClear(world, atEdge));

        JumpStep atCentre = JumpMath.CreateJump(0.5, 1, 0.5, 5.5, 1, 0.5, new JumpLimits());
        Assert.True(JumpPlanner.IsArcClear(world, atCentre));
    }

    [Fact]
    public void RandomSpotFavoursHighPerches()
    {
        TestBlockWorld world = FlatWorld();
        world.Fill(3, 1, 0, 3, 3, 0);   // one pillar, top at y = 4 (3 up)

        int onPillar = 0;
        int runs = 400;
        Random rand = new Random(7);
        for (int i = 0; i < runs; i++)
        {
            List<JumpStep> plan = JumpPlanner.PlanJumpToRandomSpot(world, 0.5, 1, 0.5, 2.5, 2.5, 0.5, 3, 4, new JumpLimits(), rand);
            Assert.NotNull(plan);
            Assert.Single(plan);
            JumpStep step = plan[0];
            Assert.True(JumpPlanner.IsArcClear(world, step));
            double dx = step.ToX - 2.5;
            double dz = step.ToZ - 0.5;
            Assert.True(Math.Sqrt(dx * dx + dz * dz) <= 3.0 + 1e-6 + 1.0);
            if (step.ToY > 3.9)
            {
                onPillar++;
            }
        }
        // About 25 ground cells and one pillar top: uniform choice would hit the pillar about 4% of the time.
        // With bias 4 its weight is (3 + 1)^4 = 256 against 1 per ground cell, so it should win most draws.
        Assert.True(onPillar > runs / 2, "pillar picked " + onPillar + " times out of " + runs);
    }

    [Fact]
    public void RandomSpotWithoutBiasRarelyPicksThePillar()
    {
        TestBlockWorld world = FlatWorld();
        world.Fill(3, 1, 0, 3, 3, 0);
        int onPillar = 0;
        int runs = 400;
        Random rand = new Random(7);
        for (int i = 0; i < runs; i++)
        {
            List<JumpStep> plan = JumpPlanner.PlanJumpToRandomSpot(world, 0.5, 1, 0.5, 2.5, 2.5, 0.5, 3, 0, new JumpLimits(), rand);
            Assert.NotNull(plan);
            if (plan[0].ToY > 3.9)
            {
                onPillar++;
            }
        }
        Assert.True(onPillar < runs / 8, "pillar picked " + onPillar + " times out of " + runs);
    }

    [Fact]
    public void RandomSpotWithNothingReachableGivesNull()
    {
        // A spider in a closed 1-block box: walls all round, a lid on top.
        TestBlockWorld boxed = new TestBlockWorld();
        boxed.Fill(-12, 0, -12, 12, 0, 12);
        boxed.Fill(-1, 1, -1, 1, 1, -1);
        boxed.Fill(-1, 1, 1, 1, 1, 1);
        boxed.Fill(-1, 1, 0, -1, 1, 0);
        boxed.Fill(1, 1, 0, 1, 1, 0);
        boxed.Fill(-1, 2, -1, 1, 2, 1);
        Assert.Null(JumpPlanner.PlanJumpToRandomSpot(boxed, 0.5, 1, 0.5, 0.5, 1, 0.5, 5, 0, new JumpLimits(), new Random(1)));
    }
}
