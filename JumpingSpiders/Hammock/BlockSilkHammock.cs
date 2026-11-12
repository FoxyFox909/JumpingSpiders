using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace JumpingSpiders;

/// <summary>
/// STUB created by the Foundation so other areas can compile against the contract; Builder B replaces it.
/// The silk hammock block (six variants, named by the direction of the block that holds it).
/// </summary>
public class BlockSilkHammock : Block
{
    public static bool IsHammock(Block block)
    {
        return block is BlockSilkHammock;
    }

    /// <summary>Direction from the hammock to the block that holds it.</summary>
    public BlockFacing GetSupportFacing()
    {
        return null;
    }
}
