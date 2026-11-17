using System.Collections.Generic;
using Xunit;

namespace JumpingSpiders.Tests;

public class ClingJumpTests
{
    private const double HalfWidth = 0.4;
    private const double Height = 0.4;

    private static TestBlockWorld FloorAndWall()
    {
        TestBlockWorld world = new TestBlockWorld();
        world.HalfWidth = HalfWidth;
        world.Height = Height;
        world.Fill(-8, -1, -8, 8, -1, 8);
        world.Fill(3, 0, -8, 3, 5, 8);
        return world;
    }

    private static BuildStance Find(List<BuildStance> stances, int x, int y, int z)
    {
        for (int i = 0; i < stances.Count; i++)
        {
            if (stances[i].CellX == x && stances[i].CellY == y && stances[i].CellZ == z)
            {
                return stances[i];
            }
        }
        return null;
    }

    [Fact]
    public void JumpsUpTheWallAndGetsHold()
    {
        TestBlockWorld world = FloorAndWall();
        List<BuildStance> clinging = BuildStanceFinder.FindClinging(world, world, 2, 4, 0, 2, HalfWidth, Height, -0.5, 0, 0.5);
        BuildStance target = Find(clinging, 2, 3, 0);
        Assert.NotNull(target);

        JumpStep step = ClingJump.TryJump(world, -0.5, 0, 0.5, target, new JumpLimits());
        Assert.NotNull(step);
        Assert.Equal(target.FeetX, step.ToX, 6);
        Assert.Equal(target.FeetY, step.ToY, 6);
        // A normal jump there is impossible: nothing to land on.
        Assert.Null(JumpPlanner.TryJump(world, -0.5, 0, 0.5, target.FeetX, target.FeetY, target.FeetZ, new JumpLimits()));

        // At the end of the arc it is against the wall and gets hold there.
        double holdX;
        double holdY;
        double holdZ;
        Assert.True(ClingJump.FindHold(world, step.ToX, step.ToY, step.ToZ, 1, 0, 0, BuildStanceFinder.SurfaceProbe,
            BuildStanceFinder.ClingGap, out holdX, out holdY, out holdZ));
        Assert.True(world.IsFree(holdX, holdY, holdZ));
        Assert.True(holdX <= 3 - HalfWidth && holdX > 3 - HalfWidth - 0.05);
        Assert.Equal(step.ToY, holdY, 6);
    }

    [Fact]
    public void NoHoldInTheOpen()
    {
        TestBlockWorld world = FloorAndWall();
        double holdX;
        double holdY;
        double holdZ;
        Assert.False(ClingJump.FindHold(world, 0.5, 2, 0.5, 1, 0, 0, BuildStanceFinder.SurfaceProbe, BuildStanceFinder.ClingGap,
            out holdX, out holdY, out holdZ));
        Assert.False(ClingJump.FindHold(world, 0.5, 2, 0.5, 0, 1, 0, BuildStanceFinder.SurfaceProbe, BuildStanceFinder.ClingGap,
            out holdX, out holdY, out holdZ));
    }

    [Fact]
    public void ABlockedArcIsRefused()
    {
        TestBlockWorld world = FloorAndWall();
        List<BuildStance> clinging = BuildStanceFinder.FindClinging(world, world, 2, 4, 0, 2, HalfWidth, Height, -0.5, 0, 0.5);
        BuildStance target = Find(clinging, 2, 3, 0);
        // A screen 5 high between the spider and the wall.
        world.Fill(0, 0, -8, 0, 4, 8);
        Assert.Null(ClingJump.TryJump(world, -1.5, 0, 0.5, target, new JumpLimits()));
    }

    [Fact]
    public void CeilingJumpPeaksJustUnderTheCeiling()
    {
        TestBlockWorld world = FloorAndWall();
        world.Fill(-3, 4, -3, 2, 4, 3);
        List<BuildStance> clinging = BuildStanceFinder.FindClinging(world, world, 0, 3, 0, 2, HalfWidth, Height, -1.5, 0, 0.5);
        BuildStance target = null;
        for (int i = 0; i < clinging.Count; i++)
        {
            if (clinging[i].IsCeiling() && clinging[i].CellX == 1 && clinging[i].CellZ == 0)
            {
                target = clinging[i];
            }
        }
        Assert.NotNull(target);
        JumpStep step = ClingJump.TryJump(world, -1.5, 0, 0.5, target, new JumpLimits());
        Assert.NotNull(step);

        // Highest point of the arc, tick by tick: within the hold probe of the ceiling, so it gets hold there.
        double apexY = 0;
        double apexX = 0;
        for (int tick = 1; tick <= step.FlightTicks; tick++)
        {
            double x;
            double y;
            double z;
            JumpMath.PositionAt(step, tick * JumpMath.PhysicsTickSeconds, out x, out y, out z);
            if (y > apexY)
            {
                apexY = y;
                apexX = x;
            }
        }
        Assert.True(apexY + Height <= 4);
        double holdX;
        double holdY;
        double holdZ;
        Assert.True(ClingJump.FindHold(world, apexX, apexY, 0.5, 0, 1, 0, BuildStanceFinder.SurfaceProbe, BuildStanceFinder.ClingGap,
            out holdX, out holdY, out holdZ));
    }
}
