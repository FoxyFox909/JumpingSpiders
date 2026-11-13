namespace JumpingSpiders;

/// <summary>
/// What the jump planner needs to know about the world around one spider.
/// In the game this is SpiderJumpWorld (real blocks, the spider's real collision box);
/// in the unit tests it is a small hand-made block world.
///
/// A "feet position" is the bottom centre of the spider's collision box, in blocks.
/// A "cell" is the block position (x, y, z) that contains the feet.
/// </summary>
public interface IJumpWorld
{
    /// <summary>True when the spider's collision box fits (touches nothing solid) with its feet at this point.</summary>
    bool IsFree(double x, double y, double z);

    /// <summary>True when the spider can rest with its feet in block cell (x, y, z): the box fits and there is footing below.</summary>
    bool CanStandAt(int x, int y, int z);

    /// <summary>
    /// The exact feet Y when standing in cell (x, y, z). This is y itself on a full block, or higher
    /// when the cell has a partial floor (a slab, a hammock).
    /// </summary>
    double GetStandHeight(int x, int y, int z);
}
