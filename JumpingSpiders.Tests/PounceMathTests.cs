using Xunit;

namespace JumpingSpiders.Tests;

/// <summary>PounceMath: aiming ahead of a moving target and the box gap used for "did the pounce hit".</summary>
public class PounceMathTests
{
    [Fact]
    public void LeadPositionAimsAhead()
    {
        Assert.Equal(10.0, PounceMath.LeadPosition(10, 4, 0.5, 0), 9);   // no lead
        Assert.Equal(12.0, PounceMath.LeadPosition(10, 4, 0.5, 1), 9);   // full lead
        Assert.Equal(11.0, PounceMath.LeadPosition(10, 4, 0.5, 0.5), 9);
        Assert.Equal(8.0, PounceMath.LeadPosition(10, -4, 0.5, 1), 9);
    }

    [Fact]
    public void OverlappingBoxesHaveNoGap()
    {
        Assert.Equal(0.0, PounceMath.BoxGap(0, 0, 0, 1, 1, 1, 0.5, 0.5, 0.5, 2, 2, 2), 9);
        Assert.Equal(0.0, PounceMath.BoxGap(0, 0, 0, 1, 1, 1, 1, 0, 0, 2, 1, 1), 9);   // touching faces
    }

    [Fact]
    public void GapAlongOneAxis()
    {
        Assert.Equal(0.75, PounceMath.BoxGap(0, 0, 0, 1, 1, 1, 1.75, 0, 0, 2, 1, 1), 9);
        Assert.Equal(0.75, PounceMath.BoxGap(1.75, 0, 0, 2, 1, 1, 0, 0, 0, 1, 1, 1), 9);   // either order
        Assert.Equal(2.0, PounceMath.BoxGap(0, 3, 0, 1, 4, 1, 0, 0, 0, 1, 1, 1), 9);        // vertical
    }

    [Fact]
    public void GapAcrossACornerIsEuclidean()
    {
        // 3 blocks apart along X and 4 along Z: 5 blocks corner to corner.
        Assert.Equal(5.0, PounceMath.BoxGap(0, 0, 0, 1, 1, 1, 4, 0, 5, 5, 1, 6), 9);
    }
}
