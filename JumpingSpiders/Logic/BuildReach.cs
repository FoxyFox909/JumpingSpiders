using System;

namespace JumpingSpiders;

/// <summary>
/// "Is the spider close enough to build a hammock in that cell?" Pure logic, no game types, so it is covered by unit
/// tests.
///
/// A spider is within reach of a cell when the straight-line distance from the centre of its body (the middle of its
/// collision box) to the nearest point of the cell is at most Config.HammockBuildRange blocks. So a spider standing on
/// the floor reaches a cell two blocks above the floor (the cell's bottom is 2 - 0.2 = 1.8 blocks from its body), and a
/// spider holding on to the wall right beside or below the cell always does.
///
/// The straight line from its body to the cell must also be clear (IsLineClear): no building through a floor or a
/// wall. And the spider must not be inside the cell, or the game refuses to place the block there (an entity is in the
/// way).
/// </summary>
public static class BuildReach
{
    /// <summary>IsLineClear aims this far inside the cell from its nearest point, in blocks, so it never ends on a neighbour's face.</summary>
    public const double CellNudge = 0.1;

    /// <summary>IsLineClear looks for solid blocks every this many blocks along the line.</summary>
    public const double LineStep = 0.2;

    /// <summary>Distance, in blocks, from a point to the nearest point of the block cell (0 when the point is inside it).</summary>
    public static double DistanceToCell(double px, double py, double pz, int cellX, int cellY, int cellZ)
    {
        double dx = AxisGap(px, cellX);
        double dy = AxisGap(py, cellY);
        double dz = AxisGap(pz, cellZ);
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    /// <summary>
    /// True when a spider with its feet at (feetX, feetY, feetZ) and a body bodyHeight tall is within range blocks of
    /// the cell (see the class summary).
    /// </summary>
    public static bool IsWithinReach(double feetX, double feetY, double feetZ, double bodyHeight,
        int cellX, int cellY, int cellZ, double range)
    {
        return DistanceToCell(feetX, feetY + bodyHeight / 2, feetZ, cellX, cellY, cellZ) <= range;
    }

    /// <summary>
    /// True when the spider's box (halfWidth to each side of the feet, height tall), grown by margin blocks sideways,
    /// overlaps the cell. Touching is not overlapping.
    /// </summary>
    public static bool BoxOverlapsCell(double feetX, double feetY, double feetZ, double halfWidth, double height,
        double margin, int cellX, int cellY, int cellZ)
    {
        double reach = halfWidth + margin;
        bool overlapX = feetX - reach < cellX + 1 && feetX + reach > cellX;
        bool overlapY = feetY < cellY + 1 && feetY + height > cellY;
        bool overlapZ = feetZ - reach < cellZ + 1 && feetZ + reach > cellZ;
        return overlapX && overlapY && overlapZ;
    }

    /// <summary>
    /// True when the straight line from the point (fromX, fromY, fromZ) to the nearest point of the cell (moved
    /// CellNudge into it) passes through nothing solid. Checked every LineStep blocks, so a gap narrower than that may
    /// be missed (it is a sanity check, not a ray tracer). Costs one block lookup per step: about 10 for a cell 2 blocks
    /// away, about 45 for one 9 blocks away.
    /// </summary>
    public static bool IsLineClear(ISolidPointWorld world, double fromX, double fromY, double fromZ, int cellX, int cellY, int cellZ)
    {
        double toX = NudgedNearest(fromX, cellX);
        double toY = NudgedNearest(fromY, cellY);
        double toZ = NudgedNearest(fromZ, cellZ);
        double dx = toX - fromX;
        double dy = toY - fromY;
        double dz = toZ - fromZ;
        double length = Math.Sqrt(dx * dx + dy * dy + dz * dz);
        int steps = (int)Math.Ceiling(length / LineStep);
        for (int i = 1; i <= steps; i++)
        {
            double t = (double)i / steps;
            if (world.IsSolidAt(fromX + dx * t, fromY + dy * t, fromZ + dz * t))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>
    /// The whole reach test of a spider at feet (feetX, feetY, feetZ): within range of the cell (IsWithinReach) and a
    /// clear line from the middle of its body to it (IsLineClear).
    /// </summary>
    public static bool CanReach(ISolidPointWorld world, double feetX, double feetY, double feetZ, double bodyHeight,
        int cellX, int cellY, int cellZ, double range)
    {
        if (!IsWithinReach(feetX, feetY, feetZ, bodyHeight, cellX, cellY, cellZ, range))
        {
            return false;
        }
        return IsLineClear(world, feetX, feetY + bodyHeight / 2, feetZ, cellX, cellY, cellZ);
    }

    // The coordinate of the cell nearest to value on one axis, moved CellNudge into the cell (to its middle at most).
    private static double NudgedNearest(double value, int cell)
    {
        double nearest = Math.Max(cell, Math.Min(cell + 1, value));
        double middle = cell + 0.5;
        if (nearest < middle)
        {
            return Math.Min(middle, nearest + CellNudge);
        }
        return Math.Max(middle, nearest - CellNudge);
    }

    // How far a coordinate is outside the span [cell, cell + 1] on one axis (0 inside it).
    private static double AxisGap(double value, int cell)
    {
        if (value < cell)
        {
            return cell - value;
        }
        if (value > cell + 1)
        {
            return value - (cell + 1);
        }
        return 0;
    }
}
