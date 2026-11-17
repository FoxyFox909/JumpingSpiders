using Xunit;

namespace JumpingSpiders.Tests;

public class BuildReachTests
{
    private const double BodyHeight = 0.4;
    private const double HalfWidth = 0.4;

    [Fact]
    public void DistanceToCell()
    {
        Assert.Equal(0, BuildReach.DistanceToCell(5.5, 5.5, 5.5, 5, 5, 5), 6);
        Assert.Equal(1, BuildReach.DistanceToCell(5.5, 7, 5.5, 5, 5, 5), 6);
        Assert.Equal(1.5, BuildReach.DistanceToCell(3.5, 5.5, 5.5, 5, 5, 5), 6);
        // Diagonally off a corner: sqrt(1 + 1 + 1).
        Assert.Equal(System.Math.Sqrt(3), BuildReach.DistanceToCell(4, 4, 4, 5, 5, 5), 6);
    }

    [Fact]
    public void FromTheFloorItReachesTwoBlocksUpButNotThree()
    {
        // Feet on the floor at y = 10, below cells 12 (2 up) and 13 (3 up).
        Assert.True(BuildReach.IsWithinReach(0.5, 10, 0.5, BodyHeight, 0, 12, 0, 2));
        Assert.False(BuildReach.IsWithinReach(0.5, 10, 0.5, BodyHeight, 0, 13, 0, 2));
    }

    [Fact]
    public void Sideways()
    {
        // Two cells away on the same floor: 1.5 blocks from the body to the cell's face.
        Assert.True(BuildReach.IsWithinReach(2.5, 10, 0.5, BodyHeight, 0, 10, 0, 2));
        // Three cells away: 2.5.
        Assert.False(BuildReach.IsWithinReach(3.5, 10, 0.5, BodyHeight, 0, 10, 0, 2));
    }

    [Fact]
    public void BoxOverlap()
    {
        // Standing in the middle of the next cell: the 0.8 wide box stays 0.1 out of the site.
        Assert.False(BuildReach.BoxOverlapsCell(1.5, 10, 0.5, HalfWidth, BodyHeight, 0, 0, 10, 0));
        // 0.3 off the middle towards the site: it reaches 0.2 into it.
        Assert.True(BuildReach.BoxOverlapsCell(1.2, 10, 0.5, HalfWidth, BodyHeight, 0, 0, 10, 0));
        // A margin makes the middle of the next cell count as too close.
        Assert.True(BuildReach.BoxOverlapsCell(1.5, 10, 0.5, HalfWidth, BodyHeight, 0.3, 0, 10, 0));
        // In the cell below: the 0.4 tall box does not reach up into the site.
        Assert.False(BuildReach.BoxOverlapsCell(0.5, 9, 0.5, HalfWidth, BodyHeight, 0, 0, 10, 0));
        // In the site itself.
        Assert.True(BuildReach.BoxOverlapsCell(0.5, 10, 0.5, HalfWidth, BodyHeight, 0, 0, 10, 0));
    }

    [Fact]
    public void LineThroughAFloorOrWallIsBlocked()
    {
        TestBlockWorld world = new TestBlockWorld();
        world.Fill(-5, -1, -5, 5, -1, 5);
        world.Fill(3, 0, -5, 3, 4, 5);
        // From a body on the floor: a cell 2 up beside it is in sight, the cell under the floor is not.
        Assert.True(BuildReach.IsLineClear(world, 0.5, 0.2, 0.5, 1, 2, 0));
        Assert.False(BuildReach.IsLineClear(world, 0.5, 0.2, 0.5, 0, -2, 0));
        // Nor is a cell on the far side of the wall.
        Assert.False(BuildReach.IsLineClear(world, 2.5, 0.2, 0.5, 4, 0, 0));
        // But one just beside the wall on this side is.
        Assert.True(BuildReach.IsLineClear(world, 0.5, 0.2, 0.5, 2, 1, 0));
        // CanReach needs both: range and a clear line.
        Assert.True(BuildReach.CanReach(world, 0.5, 0, 0.5, BodyHeight, 1, 2, 0, 2));
        Assert.False(BuildReach.CanReach(world, 0.5, 0, 0.5, BodyHeight, 0, -2, 0, 2));
    }
}
