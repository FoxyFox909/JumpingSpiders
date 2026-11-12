using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace JumpingSpiders;

/// <summary>
/// STUB created by the Foundation so other areas can compile against the contract; Builder B replaces it.
/// </summary>
public static class HammockBuilder
{
    /// <summary>The direction of a block that can hold a hammock at pos, or null when the cell cannot hold one.</summary>
    public static BlockFacing FindSupportFacing(IWorldAccessor world, BlockPos pos)
    {
        return null;
    }

    public static bool PlaceHammock(IWorldAccessor world, BlockPos pos, BlockFacing supportFacing)
    {
        return false;
    }

    /// <summary>A domesticated spider builds a hammock after a meal.</summary>
    public static bool TryBuildHammockFor(EntityAgent spider)
    {
        return false;
    }
}
