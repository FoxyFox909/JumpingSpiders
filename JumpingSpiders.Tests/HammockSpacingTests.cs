using Xunit;

namespace JumpingSpiders.Tests;

public class HammockSpacingTests
{
    [Fact]
    public void SameSpotConflicts()
    {
        Assert.True(HammockSpacing.Conflicts(10, 70, 10, 10, 70, 10, 4));
    }

    [Fact]
    public void ThreeApartOnEveryAxisConflicts()
    {
        Assert.True(HammockSpacing.Conflicts(10, 70, 10, 13, 73, 13, 4));
        Assert.True(HammockSpacing.Conflicts(10, 70, 10, 7, 67, 7, 4));
    }

    [Fact]
    public void FourApartOnAnyOneAxisIsFine()
    {
        Assert.False(HammockSpacing.Conflicts(10, 70, 10, 14, 70, 10, 4));
        Assert.False(HammockSpacing.Conflicts(10, 70, 10, 10, 74, 10, 4));
        Assert.False(HammockSpacing.Conflicts(10, 70, 10, 10, 70, 6, 4));
        Assert.False(HammockSpacing.Conflicts(10, 70, 10, 13, 73, 14, 4));
    }

    [Fact]
    public void WorksAcrossNegativeCoordinates()
    {
        Assert.True(HammockSpacing.Conflicts(-1, 70, -1, 1, 70, 1, 4));
        Assert.False(HammockSpacing.Conflicts(-2, 70, 0, 2, 70, 0, 4));
    }

    [Fact]
    public void NoTwoHammocksFitInAnyFourCube()
    {
        // Every pair of cells inside one 4 x 4 x 4 box conflicts, wherever the box is.
        for (int dx = 0; dx < 4; dx++)
        {
            for (int dy = 0; dy < 4; dy++)
            {
                for (int dz = 0; dz < 4; dz++)
                {
                    Assert.True(HammockSpacing.Conflicts(5, 5, 5, 5 + dx, 5 + dy, 5 + dz, 4));
                }
            }
        }
    }

    [Fact]
    public void SpacingOneOnlyForbidsTheSameCell()
    {
        Assert.True(HammockSpacing.Conflicts(0, 0, 0, 0, 0, 0, 1));
        Assert.False(HammockSpacing.Conflicts(0, 0, 0, 1, 0, 0, 1));
    }

    [Fact]
    public void ScanRadius()
    {
        Assert.Equal(3, HammockSpacing.ScanRadius(4));
        Assert.Equal(0, HammockSpacing.ScanRadius(1));
        Assert.Equal(0, HammockSpacing.ScanRadius(0));
    }
}
