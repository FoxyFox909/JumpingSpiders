using System;
using Xunit;

namespace JumpingSpiders.Tests;

public class HammockOpeningTests
{
    private static bool[] Blocked(bool north, bool east, bool south, bool west)
    {
        return new bool[] { north, east, south, west };
    }

    [Fact]
    public void RandomPicksOnlyOpenSides()
    {
        Random rand = new Random(7);
        int[] seen = new int[4];
        for (int i = 0; i < 400; i++)
        {
            seen[HammockOpening.ChooseRandom(Blocked(true, false, true, false), rand)]++;
        }
        Assert.Equal(0, seen[HammockOpening.North]);
        Assert.Equal(0, seen[HammockOpening.South]);
        Assert.True(seen[HammockOpening.East] > 150 && seen[HammockOpening.West] > 150);
    }

    [Fact]
    public void RandomWithEverySideBlockedStillGivesASide()
    {
        Random rand = new Random(7);
        int[] seen = new int[4];
        for (int i = 0; i < 400; i++)
        {
            seen[HammockOpening.ChooseRandom(Blocked(true, true, true, true), rand)]++;
        }
        for (int i = 0; i < 4; i++)
        {
            Assert.True(seen[i] > 50);
        }
    }

    [Fact]
    public void TowardsThePlayerWhenThatSideIsOpen()
    {
        bool[] open = Blocked(false, false, false, false);
        Assert.Equal(HammockOpening.East, HammockOpening.ChooseTowards(open, 3, 1));
        Assert.Equal(HammockOpening.North, HammockOpening.ChooseTowards(open, 0.5, -4));
        Assert.Equal(HammockOpening.South, HammockOpening.ChooseTowards(open, -1, 2));
        Assert.Equal(HammockOpening.West, HammockOpening.ChooseTowards(open, -3, 0));
    }

    [Fact]
    public void ElseTheOpenSideNearestToThePlayer()
    {
        // Player to the east and a bit south; east is blocked: south (the side it leans to) before north.
        Assert.Equal(HammockOpening.South, HammockOpening.ChooseTowards(Blocked(false, true, false, false), 3, 1));
        Assert.Equal(HammockOpening.North, HammockOpening.ChooseTowards(Blocked(false, true, false, false), 3, -1));
        // East and south blocked: north next, west last.
        Assert.Equal(HammockOpening.North, HammockOpening.ChooseTowards(Blocked(false, true, true, false), 3, 1));
        Assert.Equal(HammockOpening.West, HammockOpening.ChooseTowards(Blocked(true, true, true, false), 3, 1));
    }

    [Fact]
    public void AllBlockedStillOpensTowardsThePlayer()
    {
        Assert.Equal(HammockOpening.East, HammockOpening.ChooseTowards(Blocked(true, true, true, true), 3, 1));
    }

    [Fact]
    public void DirectionTowards()
    {
        Assert.Equal(HammockOpening.West, HammockOpening.DirectionTowards(0, 0));
        Assert.Equal(HammockOpening.East, HammockOpening.DirectionTowards(2, 1.9));
        Assert.Equal(HammockOpening.South, HammockOpening.DirectionTowards(1, 2));
        Assert.Equal(HammockOpening.North, HammockOpening.DirectionTowards(-1, -2));
    }

    // The game's whole-model turn (MeshData.Rotate with Mat4f.RotateXYZ), copied here: the direction the model's
    // opening (-X in its own frame) points to after rotateX and rotateY.
    private static void TurnedOpening(double rotateXDeg, double rotateYDeg, out int x, out int z)
    {
        double rx = rotateXDeg * Math.PI / 180;
        double ry = rotateYDeg * Math.PI / 180;
        double sx = Math.Sin(rx);
        double cx = Math.Cos(rx);
        double sy = Math.Sin(ry);
        double cy = Math.Cos(ry);
        // Column-major matrix entries with rotateZ = 0, as Mat4f.RotateXYZ fills them.
        double m0 = cy;
        double m2 = -cx * sy;
        // Opening (-1, 0, 0): x' = -m0, z' = -m2.
        x = (int)Math.Round(-m0);
        z = (int)Math.Round(-m2);
    }

    [Fact]
    public void ModelRotationsOpenWhereTheirNamesSay()
    {
        for (int opening = 0; opening < 4; opening++)
        {
            int x;
            int z;
            TurnedOpening(0, HammockOpening.ModelRotateY(opening, false), out x, out z);
            Assert.Equal(HammockOpening.DirX[opening], x);
            Assert.Equal(HammockOpening.DirZ[opening], z);
            TurnedOpening(180, HammockOpening.ModelRotateY(opening, true), out x, out z);
            Assert.Equal(HammockOpening.DirX[opening], x);
            Assert.Equal(HammockOpening.DirZ[opening], z);
        }
        // The values written in silkhammock.json.
        Assert.Equal(270, HammockOpening.ModelRotateY(HammockOpening.North, false));
        Assert.Equal(90, HammockOpening.ModelRotateY(HammockOpening.North, true));
        Assert.Equal(90, HammockOpening.ModelRotateY(HammockOpening.South, false));
        Assert.Equal(270, HammockOpening.ModelRotateY(HammockOpening.South, true));
    }
}
