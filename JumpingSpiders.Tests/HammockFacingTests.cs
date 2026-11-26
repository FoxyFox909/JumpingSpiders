using System;
using Xunit;

namespace JumpingSpiders.Tests;

/// <summary>HammockFacing: a sleeping spider looks out of the hammock's opening.</summary>
public class HammockFacingTests
{
    [Fact]
    public void WallHammocksFaceAwayFromTheirWall()
    {
        Assert.Equal(0.0, HammockFacing.SleepYaw(0, 0, -1, 0, 0), 9);           // wall to the north: looks south (+Z)
        Assert.Equal(Math.PI, HammockFacing.SleepYaw(0, 0, 1, 0, 0), 9);        // wall to the south: looks north (-Z)
        Assert.Equal(1.5 * Math.PI, HammockFacing.SleepYaw(1, 0, 0, 0, 0), 9);  // wall to the east: looks west (-X)
        Assert.Equal(0.5 * Math.PI, HammockFacing.SleepYaw(-1, 0, 0, 0, 0), 9); // wall to the west: looks east (+X)
    }

    [Fact]
    public void CeilingAndFloorHammocksWithNoOpeningFaceWest()
    {
        // No opening given: west, where silkhammock-up and silkhammock-down open (HammockOpening.DefaultOpening).
        Assert.Equal(1.5 * Math.PI, HammockFacing.SleepYaw(0, 1, 0, 0, 0), 9);
        Assert.Equal(1.5 * Math.PI, HammockFacing.SleepYaw(0, -1, 0, 0, 0), 9);
    }

    [Fact]
    public void CeilingAndFloorHammocksFaceTheirOpening()
    {
        Assert.Equal(Math.PI, HammockFacing.SleepYaw(0, 1, 0, 0, -1), 9);          // ceiling, opening north
        Assert.Equal(0.5 * Math.PI, HammockFacing.SleepYaw(0, -1, 0, 1, 0), 9);    // floor, opening east
        Assert.Equal(0.0, HammockFacing.SleepYaw(0, -1, 0, 0, 1), 9);              // floor, opening south
        // A wall hammock ignores the opening: away from its wall.
        Assert.Equal(0.0, HammockFacing.SleepYaw(0, 0, -1, 1, 0), 9);
    }

    [Fact]
    public void TheYawPointsAlongTheLookDirection()
    {
        // The game's convention: x = sin(yaw), z = cos(yaw).
        double yaw = HammockFacing.SleepYaw(1, 0, 0, 0, 0);
        Assert.Equal(-1.0, Math.Sin(yaw), 9);
        Assert.Equal(0.0, Math.Cos(yaw), 9);

        yaw = HammockFacing.SleepYaw(0, 0, -1, 0, 0);
        Assert.Equal(0.0, Math.Sin(yaw), 9);
        Assert.Equal(1.0, Math.Cos(yaw), 9);
    }

    [Fact]
    public void TheYawIsNeverNegative()
    {
        int[][] directions = new int[][]
        {
            new int[] { 0, 0, -1 }, new int[] { 1, 0, 0 }, new int[] { 0, 0, 1 },
            new int[] { -1, 0, 0 }, new int[] { 0, 1, 0 }, new int[] { 0, -1, 0 }
        };
        for (int i = 0; i < directions.Length; i++)
        {
            double yaw = HammockFacing.SleepYaw(directions[i][0], directions[i][1], directions[i][2], 0, 0);
            Assert.True(yaw >= 0 && yaw < 2 * Math.PI, "yaw " + yaw);
        }
    }
}
