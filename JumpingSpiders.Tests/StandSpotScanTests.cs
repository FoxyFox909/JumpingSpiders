using System.Collections.Generic;
using Xunit;

namespace JumpingSpiders.Tests;

/// <summary>
/// StandSpotScan, the shared scans for cells a spider can stand in. Every world has a stone floor in layer y = 0, so a
/// spider standing on it has its feet at y = 1. The tests of its callers (HammockReachFinder, NearbyStandSpot,
/// LaunchSpotFinder, BuildStanceFinder) pin what each of them found before they shared these scans.
/// </summary>
public class StandSpotScanTests
{
    // A floor, a second floor whose top is at y = 4 (layer 3) and a third at y = 8 (layer 7), all over the same columns.
    private static TestBlockWorld ThreeFloors()
    {
        TestBlockWorld world = new TestBlockWorld();
        world.Fill(-8, 0, -8, 8, 0, 8);
        world.Fill(-8, 3, -8, 8, 3, 8);
        world.Fill(-8, 7, -8, 8, 7, 8);
        return world;
    }

    [Fact]
    public void ASpotStandsInTheMiddleOfItsCellAtTheStandHeight()
    {
        TestBlockWorld world = new TestBlockWorld();
        world.SetBlock(2, 1, -3, 0.5);
        StandSpot spot = StandSpotScan.MakeSpot(world, 2, 1, -3);
        Assert.Equal(2, spot.CellX);
        Assert.Equal(1, spot.CellY);
        Assert.Equal(-3, spot.CellZ);
        Assert.Equal(2.5, spot.FeetX, 9);
        Assert.Equal(1.5, spot.FeetY, 9);
        Assert.Equal(-2.5, spot.FeetZ, 9);
        Assert.Null(spot.StepFromGround);
    }

    [Fact]
    public void TopDownTakesTheHighestCellInRange()
    {
        TestBlockWorld world = ThreeFloors();
        // WildSpiderSpawner: 3 up and 3 down from the generated ground. From 1 that is 4 (on the second floor), not 8.
        Assert.Equal(4, StandSpotScan.FirstInColumn(world, 0, 0, 1, 3, 3, false).CellY);
        // From 5, the third floor (8) is just in range.
        Assert.Equal(8, StandSpotScan.FirstInColumn(world, 0, 0, 5, 3, 3, false).CellY);
        // WildHammockSites: 2 up and 2 down. From 1 nothing above 3 is looked at, so the ground floor (1).
        Assert.Equal(1, StandSpotScan.FirstInColumn(world, 0, 0, 1, 2, 2, false).CellY);
    }

    [Fact]
    public void NearestFirstTakesTheLevelThenBelowThenAbove()
    {
        TestBlockWorld world = ThreeFloors();
        // From 2: 2 and 1 below come before 3 above; 1 is standable.
        Assert.Equal(1, StandSpotScan.FirstInColumn(world, 0, 0, 2, 3, 3, true).CellY);
        // From 3: 3, 2, 4: the second floor above wins over the ground floor two below.
        Assert.Equal(4, StandSpotScan.FirstInColumn(world, 0, 0, 3, 3, 3, true).CellY);
        // Fewer cells up than down: from 6 with 1 up and 2 down, 8 is out of range and 4 is two below.
        Assert.Equal(4, StandSpotScan.FirstInColumn(world, 0, 0, 6, 1, 2, true).CellY);
    }

    [Fact]
    public void NothingWhenNoCellInRangeIsStandable()
    {
        TestBlockWorld world = ThreeFloors();
        Assert.Null(StandSpotScan.FirstInColumn(world, 0, 0, 6, 1, 1, false));
        Assert.Null(StandSpotScan.FirstInColumn(world, 0, 0, 6, 1, 1, true));
        // Outside the floors there is nothing to stand on at all.
        Assert.Null(StandSpotScan.FirstInColumn(world, 20, 20, 1, 8, 8, false));
    }

    [Fact]
    public void HeightOffsetsGoOutwardsDownFirst()
    {
        Assert.Equal(0, StandSpotScan.HeightOffset(0));
        Assert.Equal(-1, StandSpotScan.HeightOffset(1));
        Assert.Equal(1, StandSpotScan.HeightOffset(2));
        Assert.Equal(-2, StandSpotScan.HeightOffset(3));
        Assert.Equal(2, StandSpotScan.HeightOffset(4));
    }

    [Fact]
    public void ARingKeepsToItsDistancesItsSquareAndItsHeights()
    {
        TestBlockWorld world = ThreeFloors();
        // Around the point (0.3, 0.5), 1 to 2.5 blocks away, at heights 0 to 4, in the columns within 2 of column 0, 0.
        List<StandSpot> spots = StandSpotScan.InRing(world, 0, 0, 2, 0.3, 0.5, 1, 2.5, 0, 4);
        Assert.NotEmpty(spots);
        bool sawSecondFloor = false;
        for (int i = 0; i < spots.Count; i++)
        {
            StandSpot spot = spots[i];
            double dx = spot.FeetX - 0.3;
            double dz = spot.FeetZ - 0.5;
            double distance = System.Math.Sqrt(dx * dx + dz * dz);
            Assert.InRange(distance, 1.0, 2.5);
            Assert.InRange(spot.CellX, -2, 2);
            Assert.InRange(spot.CellZ, -2, 2);
            Assert.True(spot.CellY == 1 || spot.CellY == 4);
            if (spot.CellY == 4)
            {
                sawSecondFloor = true;
            }
        }
        Assert.True(sawSecondFloor);
        // Column 0, 0 is 0.2 from the point: inside the ring's hole.
        for (int i = 0; i < spots.Count; i++)
        {
            Assert.False(spots[i].CellX == 0 && spots[i].CellZ == 0);
        }
    }

    [Fact]
    public void ARingIsListedByXThenZThenHeight()
    {
        TestBlockWorld world = ThreeFloors();
        List<StandSpot> spots = StandSpotScan.InRing(world, 0, 0, 1, 0.5, 0.5, 0, 1, 0, 8);
        // The plus shape of columns within one block of the middle, each with its three floors.
        string[] expected = new string[]
        {
            "-1,1,0", "-1,4,0", "-1,8,0",
            "0,1,-1", "0,4,-1", "0,8,-1", "0,1,0", "0,4,0", "0,8,0", "0,1,1", "0,4,1", "0,8,1",
            "1,1,0", "1,4,0", "1,8,0"
        };
        Assert.Equal(expected.Length, spots.Count);
        for (int i = 0; i < spots.Count; i++)
        {
            Assert.Equal(expected[i], spots[i].CellX + "," + spots[i].CellY + "," + spots[i].CellZ);
        }
    }
}
