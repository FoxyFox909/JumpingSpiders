using System;
using Xunit;

namespace JumpingSpiders.Tests;

/// <summary>FleeDirection: the point straight away from the threat.</summary>
public class FleeDirectionTests
{
    [Fact]
    public void PointsAwayFromTheThreat()
    {
        double x;
        double z;
        FleeDirection.AwayPoint(0, 0, -3, 0, 5, 0, out x, out z);
        Assert.Equal(5.0, x, 9);
        Assert.Equal(0.0, z, 9);

        FleeDirection.AwayPoint(10, 10, 13, 14, 5, 0, out x, out z);   // threat 3 east, 4 south: flee 3 west, 4 north
        Assert.Equal(7.0, x, 9);
        Assert.Equal(6.0, z, 9);
    }

    [Fact]
    public void ThreatOnTopOfTheSpiderUsesTheFallbackYaw()
    {
        double x;
        double z;
        FleeDirection.AwayPoint(2, 2, 2, 2, 4, 0, out x, out z);   // yaw 0 = towards +Z
        Assert.Equal(2.0, x, 9);
        Assert.Equal(6.0, z, 9);

        FleeDirection.AwayPoint(2, 2, 2, 2, 4, Math.PI / 2, out x, out z);   // yaw 90 degrees = towards +X
        Assert.Equal(6.0, x, 9);
        Assert.Equal(2.0, z, 9);
    }
}
