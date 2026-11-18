using System;
using Xunit;

namespace JumpingSpiders.Tests;

/// <summary>
/// The maths of BoxReshape on the adult model's left pedipalp (PedipalpL, turned -45 degrees about Z, with its child
/// PedipalpLB turned 90 degrees inside it), placed with the game's version-0 element matrix (GameElementMatrix).
/// The "pinned" numbers were measured on the headless server with the game's own
/// ShapeElement.GetLocalTransformMatrix after EntityBehaviorJumpingSpider.OnTesselation reshaped a copy of the real
/// model, so they hold this copy of the formula, the reshape and the behavior's use of it to what the game does.
/// Corners are relative to the parent element's (Cephalothorax's) From corner, in blocks.
/// </summary>
public class ElementReshapeTransformTests
{
    // PedipalpL and PedipalpLB as in shapes/entity/land/jumpingspider/male.json (model units).
    private static readonly double[] PalpFrom = { -1.8, 0.0, 4.5 };
    private static readonly double[] PalpTo = { 0.3, 0.8, 5.3 };
    private static readonly double[] PalpOrigin = { 0.2, 0.5, 5.0 };
    private static readonly double[] PalpRotation = { 0, 0, -45 };
    private static readonly double[] ClawFrom = { -1.1, -0.3, 0.1 };
    private static readonly double[] ClawTo = { 0.4, 0.4, 0.7 };
    private static readonly double[] ClawOrigin = { 0.3, 0.22, 0.4 };
    private static readonly double[] ClawRotation = { 0, 0, 90 };

    // The female's default: as long (X), 0.6 as thick (Y and Z), for both parts.
    private static readonly double[] Thinner = { 1, 0.6, 0.6 };

    private static readonly double[] One = { 1, 1, 1 };
    private static readonly double[] Zero = { 0, 0, 0 };

    // Measured values are floats in the game; printed to 6 decimals.
    private const double PinTolerance = 2e-6;
    private const double Exact = 1e-9;

    // The reshaped female boxes, made the way the behavior makes them: parent first, which moves the child, then the
    // child in its own axes.
    private static double[] femalePalpFrom;
    private static double[] femalePalpTo;
    private static double[] femaleClawFrom;
    private static double[] femaleClawTo;
    private static double[] femaleClawOrigin;

    static ElementReshapeTransformTests()
    {
        femalePalpFrom = BoxReshape.ReshapeCorner(PalpFrom, PalpOrigin, Thinner);
        femalePalpTo = BoxReshape.ReshapeCorner(PalpTo, PalpOrigin, Thinner);
        double[] shift = BoxReshape.ChildShift(ClawOrigin, Thinner);
        double[] movedClawFrom = BoxReshape.Add(ClawFrom, shift);
        double[] movedClawTo = BoxReshape.Add(ClawTo, shift);
        femaleClawOrigin = BoxReshape.Add(ClawOrigin, shift);
        femaleClawFrom = BoxReshape.ReshapeCorner(movedClawFrom, femaleClawOrigin, Thinner);
        femaleClawTo = BoxReshape.ReshapeCorner(movedClawTo, femaleClawOrigin, Thinner);
    }

    private static double[,] PalpMatrix(double[] from, double[] scale, double[] keyframeRotation)
    {
        return GameElementMatrix.Local(from, PalpOrigin, PalpRotation, scale, keyframeRotation, Zero);
    }

    private static double[,] ClawMatrix(double[,] palp, double[] from, double[] origin, double[] keyframeRotation)
    {
        return GameElementMatrix.Multiply(palp,
            GameElementMatrix.Local(from, origin, ClawRotation, One, keyframeRotation, Zero));
    }

    // Corner (i, j, k), each 0 or 1, of a box of the given model-unit size placed by m.
    private static double[] Corner(double[,] m, double[] from, double[] to, int i, int j, int k)
    {
        return GameElementMatrix.Apply(m, i * (to[0] - from[0]) / 16.0, j * (to[1] - from[1]) / 16.0,
            k * (to[2] - from[2]) / 16.0);
    }

    private static double[] Edge(double[,] m, double[] from, double[] to, int axis)
    {
        double[] c0 = Corner(m, from, to, 0, 0, 0);
        double[] c1 = Corner(m, from, to, axis == 0 ? 1 : 0, axis == 1 ? 1 : 0, axis == 2 ? 1 : 0);
        return new double[] { (c1[0] - c0[0]) * 16, (c1[1] - c0[1]) * 16, (c1[2] - c0[2]) * 16 };
    }

    private static double Dot(double[] a, double[] b)
    {
        return a[0] * b[0] + a[1] * b[1] + a[2] * b[2];
    }

    private static double Length(double[] a)
    {
        return Math.Sqrt(Dot(a, a));
    }

    private static double Cosine(double[] a, double[] b)
    {
        return Dot(a, b) / Length(a) / Length(b);
    }

    private static void AssertPoint(double x, double y, double z, double[] actual, double tolerance)
    {
        Assert.Equal(x, actual[0], tolerance);
        Assert.Equal(y, actual[1], tolerance);
        Assert.Equal(z, actual[2], tolerance);
    }

    [Fact]
    public void ReshapeGivesTheBoxesMeasuredInTheGame()
    {
        // PedipalpL keeps its length in X and shrinks to 0.48 x 0.48 toward its rotation origin (0.5, 5.0).
        AssertPoint(-1.8, 0.2, 4.7, femalePalpFrom, Exact);
        AssertPoint(0.3, 0.68, 5.18, femalePalpTo, Exact);
        // PedipalpLB moves so its joint keeps its place on the thinner parent, then shrinks to 1.5 x 0.42 x 0.36.
        AssertPoint(0.3, 0.132, 0.24, femaleClawOrigin, Exact);
        AssertPoint(-1.1, -0.18, 0.06, femaleClawFrom, Exact);
        AssertPoint(0.4, 0.24, 0.42, femaleClawTo, Exact);
    }

    [Fact]
    public void PinnedCornersMatchTheGame()
    {
        // Male at rest.
        double[,] palp = PalpMatrix(PalpFrom, One, Zero);
        double[,] claw = ClawMatrix(palp, ClawFrom, ClawOrigin, Zero);
        AssertPoint(-0.097985, 0.097541, 0.281250, Corner(palp, PalpFrom, PalpTo, 0, 0, 0), PinTolerance);
        AssertPoint(0.030178, 0.040089, 0.331250, Corner(palp, PalpFrom, PalpTo, 1, 1, 1), PinTolerance);
        AssertPoint(-0.113895, 0.009153, 0.287500, Corner(claw, ClawFrom, ClawTo, 0, 0, 0), PinTolerance);
        AssertPoint(-0.047604, 0.075444, 0.287500, Corner(claw, ClawFrom, ClawTo, 1, 0, 0), PinTolerance);

        // Female at rest.
        palp = PalpMatrix(femalePalpFrom, One, Zero);
        claw = ClawMatrix(palp, femaleClawFrom, femaleClawOrigin, Zero);
        AssertPoint(-0.089147, 0.106380, 0.293750, Corner(palp, femalePalpFrom, femalePalpTo, 0, 0, 0), PinTolerance);
        AssertPoint(0.024874, 0.034786, 0.323750, Corner(palp, femalePalpFrom, femalePalpTo, 1, 1, 1), PinTolerance);
        AssertPoint(-0.118138, 0.023295, 0.297500, Corner(claw, femaleClawFrom, femaleClawTo, 0, 0, 0), PinTolerance);
        AssertPoint(-0.051847, 0.089586, 0.297500, Corner(claw, femaleClawFrom, femaleClawTo, 1, 0, 0), PinTolerance);
        AssertPoint(-0.136700, 0.041857, 0.297500, Corner(claw, femaleClawFrom, femaleClawTo, 0, 1, 0), PinTolerance);
        AssertPoint(-0.118138, 0.023295, 0.320000, Corner(claw, femaleClawFrom, femaleClawTo, 0, 0, 1), PinTolerance);

        // Female in the last frame of "jumpwindup": pedipalp turned 22.5 about X, claw -12 about Z.
        palp = PalpMatrix(femalePalpFrom, One, new double[] { 22.5, 0, 0 });
        claw = ClawMatrix(palp, femaleClawFrom, femaleClawOrigin, new double[] { 0, 0, -12 });
        AssertPoint(-0.089147, 0.107836, 0.323928, Corner(palp, femalePalpFrom, femalePalpTo, 0, 0, 0), PinTolerance);
        AssertPoint(0.024874, 0.030211, 0.324247, Corner(palp, femalePalpFrom, femalePalpTo, 1, 1, 1), PinTolerance);
        AssertPoint(-0.132818, 0.040404, 0.300056, Corner(claw, femaleClawFrom, femaleClawTo, 0, 0, 0), PinTolerance);
        AssertPoint(-0.054193, 0.087578, 0.319596, Corner(claw, femaleClawFrom, femaleClawTo, 1, 0, 0), PinTolerance);
        AssertPoint(-0.147115, 0.060744, 0.308481, Corner(claw, femaleClawFrom, femaleClawTo, 0, 1, 0), PinTolerance);
        AssertPoint(-0.132818, 0.031794, 0.320843, Corner(claw, femaleClawFrom, femaleClawTo, 0, 0, 1), PinTolerance);

        // Female in the last frame of "sleep": pedipalp 22.5 about X and -22.5 about Z, claw 45 about Z.
        palp = PalpMatrix(femalePalpFrom, One, new double[] { 22.5, 0, -22.5 });
        claw = ClawMatrix(palp, femaleClawFrom, femaleClawOrigin, new double[] { 0, 0, 45 });
        AssertPoint(-0.052658, 0.138490, 0.336626, Corner(palp, femalePalpFrom, femalePalpTo, 0, 0, 0), PinTolerance);
        AssertPoint(-0.017453, 0.122408, 0.334023, Corner(claw, femaleClawFrom, femaleClawTo, 1, 0, 0), PinTolerance);
    }

    [Fact]
    public void ReshapedElementHasTheBoxAPerAxisScaleWouldGiveIt()
    {
        double[][] poses = { Zero, new double[] { 22.5, 0, 0 }, new double[] { 22.5, 0, -22.5 } };
        for (int p = 0; p < poses.Length; p++)
        {
            double[,] reshaped = PalpMatrix(femalePalpFrom, One, poses[p]);
            double[,] scaled = PalpMatrix(PalpFrom, Thinner, poses[p]);
            for (int corner = 0; corner < 8; corner++)
            {
                int i = corner & 1;
                int j = (corner >> 1) & 1;
                int k = (corner >> 2) & 1;
                double[] a = Corner(reshaped, femalePalpFrom, femalePalpTo, i, j, k);
                double[] b = Corner(scaled, PalpFrom, PalpTo, i, j, k);
                AssertPoint(b[0], b[1], b[2], a, Exact);
            }
        }
    }

    [Fact]
    public void ReshapedPedipalpIsAsLongThinnerAndUnskewedInEveryPose()
    {
        double[][] palpPoses = { Zero, new double[] { 22.5, 0, 0 }, new double[] { 0, 0, 30 }, new double[] { 22.5, 0, -22.5 } };
        double[] clawTurns = { -45, -12, 0, 45 };
        for (int p = 0; p < palpPoses.Length; p++)
        {
            for (int c = 0; c < clawTurns.Length; c++)
            {
                double[] clawPose = { 0, 0, clawTurns[c] };
                double[,] palp = PalpMatrix(femalePalpFrom, One, palpPoses[p]);
                double[,] claw = ClawMatrix(palp, femaleClawFrom, femaleClawOrigin, clawPose);
                double[,] malePalp = PalpMatrix(PalpFrom, One, palpPoses[p]);
                double[,] maleClaw = ClawMatrix(malePalp, ClawFrom, ClawOrigin, clawPose);

                // As long as the male's, 0.6 as thick, both parts.
                Assert.Equal(Length(Edge(malePalp, PalpFrom, PalpTo, 0)), Length(Edge(palp, femalePalpFrom, femalePalpTo, 0)), Exact);
                Assert.Equal(2.1, Length(Edge(palp, femalePalpFrom, femalePalpTo, 0)), Exact);
                Assert.Equal(0.48, Length(Edge(palp, femalePalpFrom, femalePalpTo, 1)), Exact);
                Assert.Equal(0.48, Length(Edge(palp, femalePalpFrom, femalePalpTo, 2)), Exact);
                Assert.Equal(Length(Edge(maleClaw, ClawFrom, ClawTo, 0)), Length(Edge(claw, femaleClawFrom, femaleClawTo, 0)), Exact);
                Assert.Equal(1.5, Length(Edge(claw, femaleClawFrom, femaleClawTo, 0)), Exact);
                Assert.Equal(0.42, Length(Edge(claw, femaleClawFrom, femaleClawTo, 1)), Exact);
                Assert.Equal(0.36, Length(Edge(claw, femaleClawFrom, femaleClawTo, 2)), Exact);

                // A box, not a skewed one: its edges stay at right angles.
                double[] x = Edge(claw, femaleClawFrom, femaleClawTo, 0);
                double[] y = Edge(claw, femaleClawFrom, femaleClawTo, 1);
                double[] z = Edge(claw, femaleClawFrom, femaleClawTo, 2);
                Assert.Equal(0, Cosine(x, y), Exact);
                Assert.Equal(0, Cosine(x, z), Exact);
                Assert.Equal(0, Cosine(y, z), Exact);

                // Attached: the claw's own joint is where the parent puts the joint point of its (reshaped) box.
                double[] jointFromClaw = GameElementMatrix.Apply(claw, (femaleClawOrigin[0] - femaleClawFrom[0]) / 16.0,
                    (femaleClawOrigin[1] - femaleClawFrom[1]) / 16.0, (femaleClawOrigin[2] - femaleClawFrom[2]) / 16.0);
                double[] jointFromPalp = GameElementMatrix.Apply(palp, femaleClawOrigin[0] / 16.0,
                    femaleClawOrigin[1] / 16.0, femaleClawOrigin[2] / 16.0);
                AssertPoint(jointFromPalp[0], jointFromPalp[1], jointFromPalp[2], jointFromClaw, Exact);
            }
        }

        // And the joint sits at the same place on the box as on the male's (as a fraction of the box's size).
        for (int axis = 0; axis < 3; axis++)
        {
            double maleFraction = ClawOrigin[axis] / (PalpTo[axis] - PalpFrom[axis]);
            double femaleFraction = femaleClawOrigin[axis] / (femalePalpTo[axis] - femalePalpFrom[axis]);
            Assert.Equal(maleFraction, femaleFraction, Exact);
        }
    }

    [Fact]
    public void PerAxisScaleOnTheParentWouldSkewTheTurnedClaw()
    {
        // The rejected way: ScaleY and ScaleZ 0.6 on PedipalpL only. The game applies the parent's scale after the
        // child's rotation, so the claw (turned 90 degrees, its length along the parent's Y) comes out shorter...
        double[,] palp = PalpMatrix(PalpFrom, Thinner, Zero);
        double[,] claw = ClawMatrix(palp, ClawFrom, ClawOrigin, Zero);
        Assert.Equal(0.9, Length(Edge(claw, ClawFrom, ClawTo, 0)), Exact);
        Assert.Equal(0.7, Length(Edge(claw, ClawFrom, ClawTo, 1)), Exact);

        // ...and once an animation turns it further (jumpwindup's last frame), no longer a box. Pinned from the game.
        palp = PalpMatrix(PalpFrom, Thinner, new double[] { 22.5, 0, 0 });
        claw = ClawMatrix(palp, ClawFrom, ClawOrigin, new double[] { 0, 0, -12 });
        double cosine = Cosine(Edge(claw, ClawFrom, ClawTo, 0), Edge(claw, ClawFrom, ClawTo, 1));
        Assert.Equal(-0.2120, cosine, 1e-4);
        AssertPoint(-0.099618, 0.054463, 0.305879, Corner(claw, ClawFrom, ClawTo, 0, 0, 0), PinTolerance);
        AssertPoint(-0.046930, 0.077673, 0.315493, Corner(claw, ClawFrom, ClawTo, 1, 0, 0), PinTolerance);
    }

    [Fact]
    public void ChildWithoutRotationOriginNeedsNoMove()
    {
        double[] shift = BoxReshape.ChildShift(null, Thinner);
        AssertPoint(0, 0, 0, shift, Exact);

        // An element with no rotation origin shrinks toward 0, 0, 0 of its parent's coordinates.
        double[] corner = BoxReshape.ReshapeCorner(new double[] { 2, 4, 6 }, null, Thinner);
        AssertPoint(2, 2.4, 3.6, corner, Exact);
    }
}
