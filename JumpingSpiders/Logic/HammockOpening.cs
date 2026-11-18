using System;

namespace JumpingSpiders;

/// <summary>
/// Which way the opening of a ceiling or floor hammock points. Pure C# (no game types), covered by unit tests;
/// BlockSilkHammock and HammockBuilder pass in which of the four neighbouring cells are blocked.
///
/// Directions are numbered like the game's BlockFacing.HORIZONTALS: 0 north (-Z), 1 east (+X), 2 south (+Z),
/// 3 west (-X). A side is "blocked" when the neighbouring cell on that side holds something solid (a wall, say), so a
/// spider could not look or climb out that way. Choosing is best effort: an open side whenever there is one, and when
/// all four are blocked the hammock is placed anyway.
/// - Spiders building one: a random open side (ChooseRandom).
/// - A player placing one: the side facing the player if it is open, else the open side nearest to that, else the side
///   facing the player all the same (ChooseTowards).
/// Wall hammocks do not use this: they always open away from their wall.
///
/// The model is turned per variant in the block JSON (ModelRotateY): the game turns a block model around Y first and
/// then around X (Mat4f.RotateXYZ in MeshData.Rotate), so the floor variants, which are also turned upside down with
/// rotateX 180, need different rotateY values for north and south than the ceiling variants.
/// </summary>
public static class HammockOpening
{
    public const int North = 0;
    public const int East = 1;
    public const int South = 2;
    public const int West = 3;

    /// <summary>Where the original two variants (silkhammock-up and silkhammock-down) open: west.</summary>
    public const int DefaultOpening = West;

    /// <summary>X part of each direction's unit vector, by direction number.</summary>
    public static readonly int[] DirX = new int[] { 0, 1, 0, -1 };

    /// <summary>Z part of each direction's unit vector, by direction number.</summary>
    public static readonly int[] DirZ = new int[] { -1, 0, 1, 0 };

    /// <summary>A random open side; when all four are blocked, a random side. blocked has four entries.</summary>
    public static int ChooseRandom(bool[] blocked, Random rand)
    {
        int open = 0;
        for (int i = 0; i < 4; i++)
        {
            if (!blocked[i])
            {
                open++;
            }
        }
        if (open == 0)
        {
            return rand.Next(4);
        }
        int pick = rand.Next(open);
        for (int i = 0; i < 4; i++)
        {
            if (!blocked[i])
            {
                if (pick == 0)
                {
                    return i;
                }
                pick--;
            }
        }
        return DefaultOpening;
    }

    /// <summary>
    /// The side towards a point (dx, dz) from the hammock if it is open; else the open side nearest in angle to the
    /// point (of the two next to it, the one the point leans towards, then the other, then the far side); else the side
    /// towards the point anyway.
    /// </summary>
    public static int ChooseTowards(bool[] blocked, double dx, double dz)
    {
        int towards = DirectionTowards(dx, dz);
        if (!blocked[towards])
        {
            return towards;
        }
        int clockwise = (towards + 1) % 4;
        int counterClockwise = (towards + 3) % 4;
        int first = clockwise;
        int second = counterClockwise;
        // The point leans towards whichever neighbour its vector has the larger component along.
        double alongClockwise = dx * DirX[clockwise] + dz * DirZ[clockwise];
        double alongCounter = dx * DirX[counterClockwise] + dz * DirZ[counterClockwise];
        if (alongCounter > alongClockwise)
        {
            first = counterClockwise;
            second = clockwise;
        }
        if (!blocked[first])
        {
            return first;
        }
        if (!blocked[second])
        {
            return second;
        }
        int opposite = (towards + 2) % 4;
        if (!blocked[opposite])
        {
            return opposite;
        }
        return towards;
    }

    /// <summary>The direction number closest to the horizontal vector (dx, dz); DefaultOpening for a zero vector.</summary>
    public static int DirectionTowards(double dx, double dz)
    {
        if (dx == 0 && dz == 0)
        {
            return DefaultOpening;
        }
        if (Math.Abs(dx) >= Math.Abs(dz))
        {
            if (dx > 0)
            {
                return East;
            }
            return West;
        }
        if (dz > 0)
        {
            return South;
        }
        return North;
    }

    /// <summary>
    /// The rotateY (degrees) the block JSON gives the model so that it opens towards opening: for a ceiling hammock
    /// (model upright) west 0, south 90, east 180, north 270; for a floor hammock (also rotateX 180, applied after the
    /// turn around Y, which mirrors north and south) west 0, north 90, east 180, south 270.
    /// Only the unit tests call it: they check this table against the game's rotation maths, and that it holds the
    /// values written in silkhammock.json's shapeByType, which is what guards those values.
    /// </summary>
    public static int ModelRotateY(int opening, bool floor)
    {
        if (opening == West)
        {
            return 0;
        }
        if (opening == East)
        {
            return 180;
        }
        bool north = opening == North;
        if (floor)
        {
            north = !north;
        }
        if (north)
        {
            return 270;
        }
        return 90;
    }
}
