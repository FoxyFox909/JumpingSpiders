using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace JumpingSpiders;

/// <summary>
/// STUB created by the Foundation so other areas can compile against the contract; Builder B replaces it.
/// </summary>
public static class HammockFinder
{
    /// <summary>The hammock block entity at pos, or null if there is no hammock.</summary>
    public static BlockEntitySilkHammock GetHammockAt(IWorldAccessor world, BlockPos pos)
    {
        return null;
    }

    public static BlockEntitySilkHammock FindNearestFree(IWorldAccessor world, Vec3d from, double range, Entity forSpider)
    {
        return null;
    }

    /// <summary>True when another hammock breaks the spacing rule (see HammockSpacing in Logic).</summary>
    public static bool HasHammockWithinSpacing(IWorldAccessor world, BlockPos pos, int spacing)
    {
        return false;
    }
}
