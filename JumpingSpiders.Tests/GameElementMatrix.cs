using System;

namespace JumpingSpiders.Tests;

/// <summary>
/// Test helper: the game's matrix for one model element, written out in doubles so tests can place the corners of
/// a model's boxes exactly as the game would, without the game's DLLs.
///
/// It is ShapeElement.GetLocalTransformMatrix(animVersion 0, ...) from vsapi 1.22.7, the version the client animator
/// uses for animations with no "version" field (all of this mod's), the one the game caches the inverse rest pose
/// with, and the same order as the client's mesh builder (ShapeTesselator):
///   T(origin) * Rx * Ry * Rz (element rotation + keyframe rotation) * S(element scale * keyframe scale)
///     * T(From + keyframe offset - origin)
/// with From, To and origin divided by 16 (model units to blocks). A child's matrix is its parent's matrix times
/// its own. The numbers pinned in ElementReshapeTransformTests were measured with the game's own method on the
/// headless server, so they also check that this copy matches.
/// </summary>
public static class GameElementMatrix
{
    // Matrices are double[4, 4], [row, column], acting on column vectors (x, y, z, 1).

    public static double[,] Identity()
    {
        double[,] m = new double[4, 4];
        for (int i = 0; i < 4; i++)
        {
            m[i, i] = 1;
        }
        return m;
    }

    public static double[,] Multiply(double[,] a, double[,] b)
    {
        double[,] r = new double[4, 4];
        for (int i = 0; i < 4; i++)
        {
            for (int j = 0; j < 4; j++)
            {
                double sum = 0;
                for (int k = 0; k < 4; k++)
                {
                    sum += a[i, k] * b[k, j];
                }
                r[i, j] = sum;
            }
        }
        return r;
    }

    private static double[,] Translation(double x, double y, double z)
    {
        double[,] m = Identity();
        m[0, 3] = x;
        m[1, 3] = y;
        m[2, 3] = z;
        return m;
    }

    private static double[,] Scaling(double x, double y, double z)
    {
        double[,] m = Identity();
        m[0, 0] = x;
        m[1, 1] = y;
        m[2, 2] = z;
        return m;
    }

    private static double[,] RotationAbout(int axis, double degrees)
    {
        double rad = degrees * Math.PI / 180.0;
        double c = Math.Cos(rad);
        double s = Math.Sin(rad);
        int a = (axis + 1) % 3;
        int b = (axis + 2) % 3;
        double[,] m = Identity();
        m[a, a] = c;
        m[a, b] = -s;
        m[b, a] = s;
        m[b, b] = c;
        return m;
    }

    /// <summary>
    /// The element's local matrix. from and origin in model units; rotation and keyframeRotation in degrees (X, Y,
    /// Z); scale per axis; keyframeOffset in blocks (as the game's keyframe offsets are applied). origin may be null.
    /// </summary>
    public static double[,] Local(double[] from, double[] origin, double[] rotation, double[] scale,
        double[] keyframeRotation, double[] keyframeOffset)
    {
        double[] o = new double[3];
        if (origin != null)
        {
            o[0] = origin[0] / 16.0;
            o[1] = origin[1] / 16.0;
            o[2] = origin[2] / 16.0;
        }
        double[,] m = Translation(o[0], o[1], o[2]);
        m = Multiply(m, RotationAbout(0, rotation[0] + keyframeRotation[0]));
        m = Multiply(m, RotationAbout(1, rotation[1] + keyframeRotation[1]));
        m = Multiply(m, RotationAbout(2, rotation[2] + keyframeRotation[2]));
        m = Multiply(m, Scaling(scale[0], scale[1], scale[2]));
        m = Multiply(m, Translation(from[0] / 16.0 + keyframeOffset[0] - o[0],
            from[1] / 16.0 + keyframeOffset[1] - o[1],
            from[2] / 16.0 + keyframeOffset[2] - o[2]));
        return m;
    }

    /// <summary>The point (x, y, z) in blocks, transformed by m.</summary>
    public static double[] Apply(double[,] m, double x, double y, double z)
    {
        double[] r = new double[3];
        for (int i = 0; i < 3; i++)
        {
            r[i] = m[i, 0] * x + m[i, 1] * y + m[i, 2] * z + m[i, 3];
        }
        return r;
    }
}
