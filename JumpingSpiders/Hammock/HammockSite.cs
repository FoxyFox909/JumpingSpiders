using Vintagestory.API.MathTools;

namespace JumpingSpiders;

/// <summary>
/// A spot where a hammock could be built: the empty cell, and which neighbour would hold it. HammockBuilder picks
/// these for a build attempt. Plain data.
/// </summary>
public class HammockSite
{
    /// <summary>The empty block cell the hammock would fill.</summary>
    public BlockPos Pos;

    /// <summary>Direction from the cell to the block that would hold the hammock.</summary>
    public BlockFacing SupportFacing;
}
