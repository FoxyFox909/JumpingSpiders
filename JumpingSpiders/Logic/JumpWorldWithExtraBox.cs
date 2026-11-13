namespace JumpingSpiders;

/// <summary>
/// Wraps another IJumpWorld and adds one extra solid box inside one block cell, as if a block with that collision
/// box had been placed there. Everything else is answered by the wrapped world.
///
/// Why: before a spider builds a hammock, HammockBuilder must know whether the spider could jump into it. The cell
/// is still empty air at that point, so the real world says "nothing to stand on there". Wrapping the world with
/// the hammock's thin floor lets JumpPlanner plan as if the hammock already existed, without touching the world.
///
/// The box is given relative to the cell's corner, in blocks (0..1 on each axis), and must stay inside its cell.
/// The spider's collision box is given relative to its feet, the same way the game's entity CollisionBox is.
/// </summary>
public class JumpWorldWithExtraBox : IJumpWorld
{
    /// <summary>Overlaps thinner than this (blocks) do not count, so touching a surface is not a collision.</summary>
    public const double TouchEpsilon = 1e-6;

    private readonly IJumpWorld inner;

    private readonly int cellX;
    private readonly int cellY;
    private readonly int cellZ;

    // The extra box, in world coordinates.
    private readonly double boxMinX;
    private readonly double boxMinY;
    private readonly double boxMinZ;
    private readonly double boxMaxX;
    private readonly double boxMaxY;
    private readonly double boxMaxZ;

    // The spider's collision box, relative to its feet.
    private readonly double spiderMinX;
    private readonly double spiderMinY;
    private readonly double spiderMinZ;
    private readonly double spiderMaxX;
    private readonly double spiderMaxY;
    private readonly double spiderMaxZ;

    /// <param name="inner">The real world (or another test world).</param>
    /// <param name="cellX">Block cell that gets the extra box.</param>
    /// <param name="cellY">Block cell that gets the extra box.</param>
    /// <param name="cellZ">Block cell that gets the extra box.</param>
    /// <param name="boxX1">Extra box, relative to the cell corner, in blocks.</param>
    /// <param name="boxY1">Extra box, relative to the cell corner, in blocks.</param>
    /// <param name="boxZ1">Extra box, relative to the cell corner, in blocks.</param>
    /// <param name="boxX2">Extra box, relative to the cell corner, in blocks.</param>
    /// <param name="boxY2">Extra box, relative to the cell corner, in blocks. Its top is the new floor height.</param>
    /// <param name="boxZ2">Extra box, relative to the cell corner, in blocks.</param>
    /// <param name="spiderX1">Spider collision box relative to its feet, in blocks (e.g. -0.3).</param>
    /// <param name="spiderY1">Spider collision box relative to its feet, in blocks (usually 0).</param>
    /// <param name="spiderZ1">Spider collision box relative to its feet, in blocks.</param>
    /// <param name="spiderX2">Spider collision box relative to its feet, in blocks (e.g. 0.3).</param>
    /// <param name="spiderY2">Spider collision box relative to its feet, in blocks (its height).</param>
    /// <param name="spiderZ2">Spider collision box relative to its feet, in blocks.</param>
    public JumpWorldWithExtraBox(IJumpWorld inner, int cellX, int cellY, int cellZ,
        double boxX1, double boxY1, double boxZ1, double boxX2, double boxY2, double boxZ2,
        double spiderX1, double spiderY1, double spiderZ1, double spiderX2, double spiderY2, double spiderZ2)
    {
        this.inner = inner;
        this.cellX = cellX;
        this.cellY = cellY;
        this.cellZ = cellZ;
        boxMinX = cellX + boxX1;
        boxMinY = cellY + boxY1;
        boxMinZ = cellZ + boxZ1;
        boxMaxX = cellX + boxX2;
        boxMaxY = cellY + boxY2;
        boxMaxZ = cellZ + boxZ2;
        spiderMinX = spiderX1;
        spiderMinY = spiderY1;
        spiderMinZ = spiderZ1;
        spiderMaxX = spiderX2;
        spiderMaxY = spiderY2;
        spiderMaxZ = spiderZ2;
    }

    public bool IsFree(double x, double y, double z)
    {
        if (OverlapsExtraBox(x, y, z))
        {
            return false;
        }
        return inner.IsFree(x, y, z);
    }

    public double GetStandHeight(int x, int y, int z)
    {
        double height = inner.GetStandHeight(x, y, z);
        if (IsExtraCell(x, y, z) && boxMaxY > height)
        {
            height = boxMaxY;
        }
        return height;
    }

    public bool CanStandAt(int x, int y, int z)
    {
        if (!IsExtraCell(x, y, z))
        {
            // The extra box stays inside its own cell, so it can only get in the way of a spider in another cell
            // when the spider's box is wider than one block. Check that case too.
            if (!inner.CanStandAt(x, y, z))
            {
                return false;
            }
            return !OverlapsExtraBox(x + 0.5, inner.GetStandHeight(x, y, z), z + 0.5);
        }

        // The same test the real world uses: the box fits standing on the floor, and lowered a little it hits it.
        double standY = GetStandHeight(x, y, z);
        if (standY >= y + 1)
        {
            return false;
        }
        double feetX = x + 0.5;
        double feetZ = z + 0.5;
        if (!IsFree(feetX, standY, feetZ))
        {
            return false;
        }
        return !IsFree(feetX, standY - JumpPlanner.FootingProbeDepth, feetZ);
    }

    private bool IsExtraCell(int x, int y, int z)
    {
        return x == cellX && y == cellY && z == cellZ;
    }

    // True when the spider's box with its feet at (x, y, z) really overlaps the extra box (touching is fine).
    private bool OverlapsExtraBox(double x, double y, double z)
    {
        bool overlapX = x + spiderMinX < boxMaxX - TouchEpsilon && x + spiderMaxX > boxMinX + TouchEpsilon;
        bool overlapY = y + spiderMinY < boxMaxY - TouchEpsilon && y + spiderMaxY > boxMinY + TouchEpsilon;
        bool overlapZ = z + spiderMinZ < boxMaxZ - TouchEpsilon && z + spiderMaxZ > boxMinZ + TouchEpsilon;
        return overlapX && overlapY && overlapZ;
    }
}
