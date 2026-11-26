using System;
using System.Collections.Generic;
using Xunit;

namespace JumpingSpiders.Tests;

/// <summary>
/// HammockReachFinder on a hand-made tree: a stone floor in layer y = 0 (feet on the ground at y = 1) and a trunk
/// column at x = 0, z = 0 from y = 1 to y = 10. Hammock spots are on the east side of the trunk, cells (1, 1 + h, 0)
/// for height h above the trunk base.
/// </summary>
public class HammockReachFinderTests
{
    private static readonly RelativeBox HammockFloor = new RelativeBox(0, 0, 0, 1, 0.0625, 1);
    private static readonly RelativeBox SpiderBox = new RelativeBox(-0.4, 0, -0.4, 0.4, 0.4, 0.4);

    private static TestBlockWorld TreeWorld()
    {
        TestBlockWorld world = new TestBlockWorld();
        world.Fill(-12, 0, -12, 12, 0, 12);
        world.Fill(0, 1, 0, 0, 10, 0);
        return world;
    }

    private static List<HammockCandidate> EastSideCandidates(int fromHeight, int toHeight)
    {
        List<HammockCandidate> candidates = new List<HammockCandidate>();
        for (int h = fromHeight; h <= toHeight; h++)
        {
            HammockCandidate candidate = new HammockCandidate();
            candidate.CellX = 1;
            candidate.CellY = 1 + h;
            candidate.CellZ = 0;
            candidate.Height = h;
            candidates.Add(candidate);
        }
        return candidates;
    }

    private static List<HammockCandidate> Evaluate(TestBlockWorld world, int fromHeight, int toHeight)
    {
        JumpLimits limits = new JumpLimits();
        List<StandSpot> starts = HammockReachFinder.FindGroundStarts(world, 0, 1, 0, 3, 2, 2);
        List<HammockCandidate> candidates = EastSideCandidates(fromHeight, toHeight);
        HammockReachFinder.MarkReachable(world, starts, candidates, HammockFloor, SpiderBox, limits);
        return candidates;
    }

    [Fact]
    public void GroundStartsSurroundTheTrunkOnTheGround()
    {
        List<StandSpot> starts = HammockReachFinder.FindGroundStarts(TreeWorld(), 0, 1, 0, 3, 2, 2);
        // A 7 x 7 square minus the trunk column.
        Assert.Equal(48, starts.Count);
        for (int i = 0; i < starts.Count; i++)
        {
            Assert.Equal(1, starts[i].CellY);
            Assert.Equal(1.0, starts[i].FeetY, 6);
            Assert.False(starts[i].CellX == 0 && starts[i].CellZ == 0);
        }
    }

    [Fact]
    public void FlatGroundHasNoPerches()
    {
        TestBlockWorld world = TreeWorld();
        JumpLimits limits = new JumpLimits();
        List<StandSpot> starts = HammockReachFinder.FindGroundStarts(world, 0, 1, 0, 3, 2, 2);
        List<StandSpot> perches = HammockReachFinder.FindPerches(world, starts, EastSideCandidates(4, 8), 0.0625, limits);
        Assert.Empty(perches);
    }

    [Fact]
    public void PerchesAreOnlyLookedForNearTheTargets()
    {
        TestBlockWorld world = TreeWorld();
        world.SetBlock(3, 3, 0);    // a perch (feet at y = 4) 2 blocks from the hammock spots
        world.SetBlock(-3, 3, 0);   // the same on the far side of the trunk, 4 blocks from them
        world.SetBlock(-6, 2, 0);   // a low perch (feet at y = 3) 7 blocks from them: out of one jump's reach
        JumpLimits limits = new JumpLimits();
        List<StandSpot> starts = HammockReachFinder.FindGroundStarts(world, 0, 1, 0, 6, 2, 2);
        List<StandSpot> perches = HammockReachFinder.FindPerches(world, starts, EastSideCandidates(5, 6), 0.0625, limits);
        bool sawNear = false;
        bool sawFarSide = false;
        for (int i = 0; i < perches.Count; i++)
        {
            Assert.False(perches[i].CellX == -6, "a perch out of reach of every target was kept");
            if (perches[i].CellX == 3 && perches[i].CellY == 4)
            {
                sawNear = true;
            }
            if (perches[i].CellX == -3 && perches[i].CellY == 4)
            {
                sawFarSide = true;
            }
            Assert.NotNull(perches[i].StepFromGround);
        }
        Assert.True(sawNear);
        Assert.True(sawFarSide);
    }

    [Fact]
    public void WithoutPerchesOnlyLowSpotsAreReachable()
    {
        List<HammockCandidate> candidates = Evaluate(TreeWorld(), 2, 8);
        // Heights 2 and 3 put the hammock floor 2.06 and 3.06 blocks above the ground: one jump (at most 4 up).
        // Height 4 is 4.06 up, just out of reach; nothing higher can be reached without a perch.
        for (int i = 0; i < candidates.Count; i++)
        {
            bool expected = candidates[i].Height <= 3;
            Assert.True(expected == candidates[i].Reachable, "height " + candidates[i].Height);
            if (expected)
            {
                Assert.Equal(1, candidates[i].Jumps);
            }
        }
    }

    [Fact]
    public void ABranchPerchMakesHigherSpotsReachableWithTwoJumps()
    {
        TestBlockWorld world = TreeWorld();
        // A branch block 3 blocks east of the trunk, top at y = 4: a perch with feet 3 blocks above the ground.
        world.SetBlock(3, 3, 0);
        List<HammockCandidate> candidates = Evaluate(world, 2, 9);
        for (int i = 0; i < candidates.Count; i++)
        {
            HammockCandidate candidate = candidates[i];
            if (candidate.Height <= 3)
            {
                Assert.True(candidate.Reachable, "height " + candidate.Height);
            }
            else if (candidate.Height <= 6)
            {
                // Hammock floor at most 4 above the perch (feet y = 4): 7.06 - 4 = 3.06 for height 6.
                Assert.True(candidate.Reachable, "height " + candidate.Height);
                Assert.Equal(2, candidate.Jumps);
            }
            else
            {
                Assert.False(candidate.Reachable, "height " + candidate.Height);
            }
        }
    }

    [Fact]
    public void BlockedCellIsNotReachable()
    {
        TestBlockWorld world = TreeWorld();
        world.SetBlock(1, 3, 0);
        List<HammockCandidate> candidates = Evaluate(world, 2, 2);
        Assert.False(candidates[0].Reachable);
    }

    [Fact]
    public void NoGroundNoReach()
    {
        // A trunk standing in deep water would look like this to the planner: nowhere to stand around it.
        TestBlockWorld world = new TestBlockWorld();
        world.Fill(0, 1, 0, 0, 10, 0);
        List<StandSpot> starts = HammockReachFinder.FindGroundStarts(world, 0, 1, 0, 3, 2, 2);
        Assert.Empty(starts);
        JumpLimits limits = new JumpLimits();
        List<HammockCandidate> candidates = EastSideCandidates(2, 4);
        HammockReachFinder.MarkReachable(world, starts, candidates, HammockFloor, SpiderBox, limits);
        for (int i = 0; i < candidates.Count; i++)
        {
            Assert.False(candidates[i].Reachable);
        }
    }

    [Fact]
    public void GroundStartsAreTheSameAsBeforeTheSharedColumnScan()
    {
        // A mound one block high east of the trunk with a two-block step on it, a slab and a pillar too high to stand
        // on. The spots are the ones FindGroundStarts found before it used StandSpotScan.FirstInColumn.
        TestBlockWorld world = TreeWorld();
        world.Fill(1, 1, -3, 3, 1, -1);
        world.Fill(2, 2, -3, 3, 3, -2);
        world.SetBlock(-2, 1, 2, 0.5);
        world.Fill(-3, 1, -3, -3, 4, -3);
        List<StandSpot> starts = HammockReachFinder.FindGroundStarts(world, 0, 1, 0, 3, 2, 2);
        // 48 columns around the trunk, less the pillar and the four columns of the step (their tops are 3 above).
        Assert.Equal(43, starts.Count);
        string[] expectedFirst = new string[] { "-3,1,-2", "-3,1,-1", "-3,1,0", "-3,1,1", "-3,1,2", "-3,1,3", "-2,1,-3" };
        for (int i = 0; i < expectedFirst.Length; i++)
        {
            Assert.Equal(expectedFirst[i], starts[i].CellX + "," + starts[i].CellY + "," + starts[i].CellZ);
        }
        string[] expectedFromTheMound = new string[] { "1,2,-3", "1,2,-2", "1,2,-1", "1,1,0", "1,1,1", "1,1,2", "1,1,3",
            "2,2,-1", "2,1,0", "2,1,1", "2,1,2", "2,1,3", "3,2,-1", "3,1,0", "3,1,1", "3,1,2", "3,1,3" };
        for (int i = 0; i < expectedFromTheMound.Length; i++)
        {
            StandSpot start = starts[starts.Count - expectedFromTheMound.Length + i];
            Assert.Equal(expectedFromTheMound[i], start.CellX + "," + start.CellY + "," + start.CellZ);
            Assert.Equal(start.CellY, start.FeetY, 9);
        }
        Assert.Equal(1.5, starts[11].FeetY, 9);   // -2, 1, 2: on the slab
    }

    [Fact]
    public void PickSomeTakesAtMostTheCountWithoutRepeats()
    {
        List<StandSpot> starts = HammockReachFinder.FindGroundStarts(TreeWorld(), 0, 1, 0, 3, 2, 2);
        List<StandSpot> some = HammockReachFinder.PickSome(starts, 4, new Random(3));
        Assert.Equal(4, some.Count);
        for (int i = 0; i < some.Count; i++)
        {
            for (int j = i + 1; j < some.Count; j++)
            {
                Assert.NotSame(some[i], some[j]);
            }
        }
        Assert.Equal(48, starts.Count);
        Assert.Equal(2, HammockReachFinder.PickSome(starts.GetRange(0, 2), 4, new Random(3)).Count);
    }
}
