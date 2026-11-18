using System.Collections.Generic;
using Xunit;

namespace JumpingSpiders.Tests;

public class BuildStanceFinderTests
{
    private const double Range = 2;
    private const double HalfWidth = 0.4;
    private const double Height = 0.4;

    // A floor at y = 0 (blocks at y = -1) and a wall 6 high along x = 3 (its face towards the spider at x = 3).
    private static TestBlockWorld FloorAndWall()
    {
        TestBlockWorld world = new TestBlockWorld();
        world.HalfWidth = HalfWidth;
        world.Height = Height;
        world.Fill(-8, -1, -8, 8, -1, 8);
        world.Fill(3, 0, -8, 3, 5, 8);
        return world;
    }

    private static bool Contains(List<BuildStance> stances, int x, int y, int z)
    {
        for (int i = 0; i < stances.Count; i++)
        {
            if (stances[i].CellX == x && stances[i].CellY == y && stances[i].CellZ == z)
            {
                return true;
            }
        }
        return false;
    }

    [Fact]
    public void StandingSpotsNearAFloorSiteKeepOutOfItsWay()
    {
        TestBlockWorld world = FloorAndWall();
        List<BuildStance> stances = BuildStanceFinder.FindStanding(world, world, 2, 0, 0, Range, HalfWidth, Height, -3.5, 0, 0.5);
        Assert.NotEmpty(stances);
        Assert.True(Contains(stances, 0, 0, 0));
        Assert.True(Contains(stances, 1, 0, 0));
        Assert.False(Contains(stances, 2, 0, 0));
        for (int i = 0; i < stances.Count; i++)
        {
            BuildStance s = stances[i];
            Assert.False(s.Clinging);
            Assert.True(BuildReach.IsWithinReach(s.FeetX, s.FeetY, s.FeetZ, Height, 2, 0, 0, Range));
            Assert.False(BuildReach.BoxOverlapsCell(s.FeetX, s.FeetY, s.FeetZ, HalfWidth, Height, 0, 2, 0, 0));
            if (i > 0)
            {
                Assert.True(stances[i - 1].DistanceFromSpider <= s.DistanceFromSpider);
            }
        }
    }

    [Fact]
    public void AHighWallSiteHasNoStandingSpotButClingingOnes()
    {
        TestBlockWorld world = FloorAndWall();
        // 3 blocks up the wall: out of reach from the floor (2.8 blocks) and from the wall top (2.2).
        Assert.Empty(BuildStanceFinder.FindStanding(world, world, 2, 3, 0, Range, HalfWidth, Height, 0.5, 0, 0.5));
        // 4 up the wall the top of the wall (feet at 6) would be within 2 blocks, but the wall's own edge is in the way.
        Assert.False(Contains(BuildStanceFinder.FindStanding(world, world, 2, 4, 0, Range, HalfWidth, Height, 0.5, 0, 0.5), 3, 6, 0));
        // A spot on top of the wall is reached from the wall top.
        Assert.True(Contains(BuildStanceFinder.FindStanding(world, world, 3, 6, 0, Range, HalfWidth, Height, 0.5, 0, 0.5), 3, 6, 2));
        List<BuildStance> clinging = BuildStanceFinder.FindClinging(world, world, 2, 3, 0, Range, HalfWidth, Height, 0.5, 0, 0.5);
        Assert.NotEmpty(clinging);
        Assert.True(Contains(clinging, 2, 2, 0));
        Assert.True(Contains(clinging, 2, 1, 0));
        for (int i = 0; i < clinging.Count; i++)
        {
            BuildStance s = clinging[i];
            Assert.True(s.Clinging);
            Assert.True(world.IsFree(s.FeetX, s.FeetY, s.FeetZ));
            Assert.True(BuildReach.IsWithinReach(s.FeetX, s.FeetY, s.FeetZ, Height, 2, 3, 0, Range));
            // Every one holds on to the wall (there is no ceiling here), on this side or the far side, its box just
            // short of the wall's face.
            Assert.Equal(0, s.SurfaceY);
            if (s.CellX == 2)
            {
                Assert.Equal(1, s.SurfaceX);
                Assert.Equal(3 - HalfWidth - BuildStanceFinder.ClingGap, s.FeetX, 6);
            }
            else
            {
                Assert.Equal(4, s.CellX);
                Assert.Equal(-1, s.SurfaceX);
                Assert.Equal(4 + HalfWidth + BuildStanceFinder.ClingGap, s.FeetX, 6);
            }
        }
    }

    [Fact]
    public void NothingReachesThroughTheWall()
    {
        TestBlockWorld world = FloorAndWall();
        // A floor spot right at the foot of the wall, on this side: 2 blocks from the cells beyond the wall, but the
        // wall is in between.
        List<BuildStance> stances = BuildStanceFinder.FindStanding(world, world, 2, 0, 0, Range, HalfWidth, Height, 0.5, 0, 0.5);
        for (int i = 0; i < stances.Count; i++)
        {
            Assert.True(stances[i].CellX < 3);
        }
        List<BuildStance> clinging = BuildStanceFinder.FindClinging(world, world, 2, 2, 0, Range, HalfWidth, Height, 0.5, 0, 0.5);
        for (int i = 0; i < clinging.Count; i++)
        {
            Assert.True(clinging[i].CellX < 3);
        }
    }

    [Fact]
    public void SpotsSitRightAgainstTheSite()
    {
        Assert.Equal(1.55, BuildStanceFinder.FeetTowards(1, 2, HalfWidth), 9);
        Assert.Equal(3.45, BuildStanceFinder.FeetTowards(3, 2, HalfWidth), 9);
        Assert.Equal(2.5, BuildStanceFinder.FeetTowards(2, 2, HalfWidth), 9);
        Assert.Equal(1.55, BuildStanceFinder.FeetHeightTowards(1, 2, Height), 9);
        Assert.Equal(3.05, BuildStanceFinder.FeetHeightTowards(3, 2, Height), 9);
    }

    [Fact]
    public void HalfABlockReachStillLeavesAWayToEveryKindOfSpot()
    {
        TestBlockWorld world = FloorAndWall();
        world.Fill(-3, 4, -3, 2, 4, 3);
        // Floor spot: standing right beside it, on the four sides only.
        List<BuildStance> floor = BuildStanceFinder.FindStanding(world, world, 0, 0, 0, 0.5, HalfWidth, Height, -2.5, 0, 0.5);
        Assert.Equal(4, floor.Count);
        // Wall spot one block up: no standing spot, but holding on to the wall right below or beside it.
        Assert.Empty(BuildStanceFinder.FindStanding(world, world, 2, 1, -2, 0.5, HalfWidth, Height, -2.5, 0, 0.5));
        List<BuildStance> wall = BuildStanceFinder.FindClinging(world, world, 2, 1, -2, 0.5, HalfWidth, Height, -2.5, 0, 0.5);
        Assert.NotEmpty(wall);
        // Under the ceiling, three blocks up: holding on to the ceiling beside it.
        List<BuildStance> ceiling = BuildStanceFinder.FindClinging(world, world, 0, 3, 0, 0.5, HalfWidth, Height, -2.5, 0, 0.5);
        bool underCeiling = false;
        for (int i = 0; i < ceiling.Count; i++)
        {
            if (ceiling[i].IsCeiling())
            {
                underCeiling = true;
                Assert.True(BuildReach.IsWithinReach(ceiling[i].FeetX, ceiling[i].FeetY, ceiling[i].FeetZ, Height, 0, 3, 0, 0.5));
            }
        }
        Assert.True(underCeiling);
    }

    [Fact]
    public void UnderACeiling()
    {
        TestBlockWorld world = FloorAndWall();
        world.Fill(-2, 4, -2, 2, 4, 2);
        List<BuildStance> clinging = BuildStanceFinder.FindClinging(world, world, 0, 3, 0, Range, HalfWidth, Height, 0.5, 0, 0.5);
        bool ceiling = false;
        for (int i = 0; i < clinging.Count; i++)
        {
            if (clinging[i].IsCeiling())
            {
                ceiling = true;
                Assert.Equal(4 - Height - BuildStanceFinder.ClingGap, clinging[i].FeetY, 6);
            }
        }
        Assert.True(ceiling);
    }

    [Fact]
    public void LaunchSpotsForAClingAreOnTheFloorAndOffItsColumn()
    {
        TestBlockWorld world = FloorAndWall();
        List<BuildStance> clinging = BuildStanceFinder.FindClinging(world, world, 2, 4, 0, Range, HalfWidth, Height, 0.5, 0, 0.5);
        List<BuildStance> spots = BuildStanceFinder.FindLaunchSpots(world, clinging[0], new JumpLimits(), 3, 1, 2, 0.5, 0, 0.5);
        Assert.NotEmpty(spots);
        for (int i = 0; i < spots.Count; i++)
        {
            Assert.Equal(0, spots[i].CellY);
            Assert.False(spots[i].CellX == clinging[0].CellX && spots[i].CellZ == clinging[0].CellZ);
        }
    }

    [Fact]
    public void LaunchSpotsAreTheSameAsBeforeTheSharedScan()
    {
        // The wall world with a step one block up, a terrace two blocks up and a slab on the floor. The spots are the
        // ones FindLaunchSpots found before it used StandSpotScan.InRing (the order of equally distant spots included).
        TestBlockWorld world = FloorAndWall();
        world.Fill(-1, 0, -8, 0, 0, -1);
        world.Fill(-1, 1, 1, 2, 2, 8);
        world.SetBlock(1, 0, 0, 0.5);
        List<BuildStance> clinging = BuildStanceFinder.FindClinging(world, world, 2, 4, 0, Range, HalfWidth, Height, 0.5, 0, 0.5);
        Assert.Equal("0,2,0", clinging[0].CellX + "," + clinging[0].CellY + "," + clinging[0].CellZ);

        List<BuildStance> spots = BuildStanceFinder.FindLaunchSpots(world, clinging[0], new JumpLimits(), 3, 1, 2, 0.5, 0, 0.5);
        Assert.Equal(39, spots.Count);
        string[] expectedFirst = new string[] { "-1,0,0", "0,1,-1", "1,0,1", "1,0,-1", "-1,0,1", "-1,1,-1", "0,0,2", "-2,0,0", "2,0,0" };
        for (int i = 0; i < expectedFirst.Length; i++)
        {
            Assert.Equal(expectedFirst[i], spots[i].CellX + "," + spots[i].CellY + "," + spots[i].CellZ);
        }
        Assert.Equal("-3,0,-3", spots[38].CellX + "," + spots[38].CellY + "," + spots[38].CellZ);

        // Shorter jumps: at most 2 up, 1 down and 2.5 across, from heights -2 to 3. The floor (2.55 below the clinging
        // spot) is out; the terrace top (0.45 above it) is in.
        JumpLimits limits = new JumpLimits();
        limits.MaxJumpUp = 2;
        limits.MaxJumpDown = 1;
        limits.MaxJumpHorizontal = 2.5;
        List<BuildStance> shortSpots = BuildStanceFinder.FindLaunchSpots(world, clinging[0], limits, 3, 3, 2, 0.5, 0, 0.5);
        string[] expectedShort = new string[] { "0,1,-1", "-1,1,-1", "0,1,-2", "-1,1,-2", "-1,3,1", "1,3,1", "0,3,2", "-1,3,2", "1,3,2", "2,3,1" };
        double[] expectedDistance = new double[] { 1.4142, 1.7321, 2.2361, 2.4495, 3.3166, 3.3166, 3.6056, 3.7417, 3.7417, 3.7417 };
        Assert.Equal(expectedShort.Length, shortSpots.Count);
        for (int i = 0; i < expectedShort.Length; i++)
        {
            Assert.Equal(expectedShort[i], shortSpots[i].CellX + "," + shortSpots[i].CellY + "," + shortSpots[i].CellZ);
            Assert.Equal(expectedDistance[i], shortSpots[i].DistanceFromSpider, 4);
            Assert.False(shortSpots[i].Clinging);
        }
    }
}
