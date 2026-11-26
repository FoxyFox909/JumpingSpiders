using System;
using Xunit;

namespace JumpingSpiders.Tests;

/// <summary>
/// NearbyStandSpot on hand-made worlds. The point (the player's feet) is at 0.5, 1, 0.5: standing on a stone floor in
/// layer y = 0.
/// </summary>
public class NearbyStandSpotTests
{
    private static TestBlockWorld FlatWorld()
    {
        TestBlockWorld world = new TestBlockWorld();
        world.Fill(-12, 0, -12, 12, 0, 12);
        return world;
    }

    private static double HorizontalDistance(StandSpot spot)
    {
        double dx = spot.FeetX - 0.5;
        double dz = spot.FeetZ - 0.5;
        return Math.Sqrt(dx * dx + dz * dz);
    }

    [Fact]
    public void OnFlatGroundTheSpotIsInTheRingOnTheGround()
    {
        TestBlockWorld world = FlatWorld();
        for (int seed = 0; seed < 50; seed++)
        {
            StandSpot spot = NearbyStandSpot.Find(world, 0.5, 1, 0.5, 2, 5, 3, new Random(seed));
            Assert.NotNull(spot);
            Assert.Equal(1, spot.CellY);
            Assert.Equal(1.0, spot.FeetY, 6);
            double distance = HorizontalDistance(spot);
            Assert.InRange(distance, 2.0, 5.0);
        }
    }

    [Fact]
    public void PrefersThePlayersLevelOverAnotherFloor()
    {
        // A second stone layer at y = 3: one can stand under it (y = 1) and on it (y = 4).
        TestBlockWorld world = FlatWorld();
        world.Fill(-12, 3, -12, 12, 3, 12);
        StandSpot below = NearbyStandSpot.Find(world, 0.5, 1, 0.5, 2, 5, 3, new Random(1));
        StandSpot above = NearbyStandSpot.Find(world, 0.5, 4, 0.5, 2, 5, 3, new Random(1));
        Assert.Equal(1, below.CellY);
        Assert.Equal(4, above.CellY);
    }

    [Fact]
    public void ClimbsOnlyAsFarAsAllowed()
    {
        // The ground around the player is two blocks higher (the player's own column is never searched).
        TestBlockWorld world = FlatWorld();
        world.Fill(-12, 1, -12, 12, 2, 12);
        StandSpot reachable = NearbyStandSpot.Find(world, 0.5, 1, 0.5, 2, 5, 3, new Random(2));
        Assert.NotNull(reachable);
        Assert.Equal(3, reachable.CellY);
        Assert.Null(NearbyStandSpot.Find(world, 0.5, 1, 0.5, 2, 5, 1, new Random(2)));
    }

    [Fact]
    public void NothingWhenEveryCellIsSolid()
    {
        TestBlockWorld world = new TestBlockWorld();
        world.Fill(-12, -5, -12, 12, 8, 12);
        Assert.Null(NearbyStandSpot.Find(world, 0.5, 1, 0.5, 2, 5, 3, new Random(3)));
    }

    [Fact]
    public void NeverInsideTheMinimumDistance()
    {
        // Only the cells right next to the player have a floor, so there is nothing to stand on 2 or more blocks away.
        TestBlockWorld world = new TestBlockWorld();
        world.Fill(-1, 0, -1, 1, 0, 1);
        Assert.Null(NearbyStandSpot.Find(world, 0.5, 1, 0.5, 2, 5, 3, new Random(4)));
        Assert.NotNull(NearbyStandSpot.Find(world, 0.5, 1, 0.5, 1, 5, 3, new Random(4)));
    }

    [Fact]
    public void FindsTheSameSpotsAsBeforeTheSharedColumnScan()
    {
        // Terraces 0 to 3 blocks high in strips three blocks wide along x, the player at y = 2.3. The spots are the ones
        // NearbyStandSpot found with these seeds before it used StandSpotScan.FirstInColumn.
        TestBlockWorld world = FlatWorld();
        for (int x = -12; x <= 12; x++)
        {
            int height = ((x + 12) / 3) % 4;
            if (height > 0)
            {
                world.Fill(x, 1, -12, x, height, 12);
            }
        }
        string[] expected = new string[] { "-2,4,0", "-2,4,-3", "3,2,-2", "-4,3,2", "-2,4,-2" };
        for (int seed = 1; seed <= 5; seed++)
        {
            StandSpot spot = NearbyStandSpot.Find(world, 0.5, 2.3, 0.5, 2, 5, 2, new Random(seed));
            Assert.Equal(expected[seed - 1], spot.CellX + "," + spot.CellY + "," + spot.CellZ);
            Assert.Equal(spot.CellY, spot.FeetY, 9);
        }
    }
}
