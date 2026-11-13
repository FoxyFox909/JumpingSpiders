using Vintagestory.API.MathTools;

namespace JumpingSpiders;

/// <summary>
/// A spot where a hammock could be built: the empty cell, which neighbour would hold it, and where a spider resting
/// in it would have its feet. HammockBuilder collects these while looking for a build site. Plain data.
/// </summary>
public class HammockSite
{
    /// <summary>The empty block cell the hammock would fill.</summary>
    public BlockPos Pos;

    /// <summary>Direction from the cell to the block that would hold the hammock.</summary>
    public BlockFacing SupportFacing;

    /// <summary>Feet height of a spider resting in the hammock, in blocks (the cell's Y plus the hammock's floor).</summary>
    public double FeetY;
}
