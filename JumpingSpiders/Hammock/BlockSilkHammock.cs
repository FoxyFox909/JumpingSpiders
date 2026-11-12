using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace JumpingSpiders;

/// <summary>
/// The silk hammock block, where jumping spiders sleep at night. Its JSON is assets/jumpingspiders/blocktypes/silkhammock.json.
///
/// Six variants, named by the direction from the hammock to the block that holds it (its "support"):
///   silkhammock-north, -east, -south, -west   hung on a wall or tree trunk on that side
///   silkhammock-up                            hanging under a ceiling or branch
///   silkhammock-down                          lying on the ground or on top of a block
///
/// Vintage Story concepts used here:
/// - One Block object exists per variant; the game calls its methods for every placed block of that variant, with
///   the position as a parameter. Per-position data lives in the block entity (BlockEntitySilkHammock).
/// - Variant["side"] reads the value of the "side" variant group from the block code.
/// - Most hooks run on both sides: the server decides, the client predicts and draws. World.Side tells which.
///
/// How the game calls in:
/// - TryPlaceBlock when a player places one (picks the variant from the clicked face).
/// - OnNeighbourBlockChange when a block next to it changes (breaks the hammock when its support is gone).
/// - OnGettingBroken on the client while a player is breaking it (shears break it faster).
/// Drops (2 to 4 flax twine) come from the "drops" list in the JSON.
/// </summary>
public class BlockSilkHammock : Block
{
    /// <summary>Asset domain of the hammock blocks.</summary>
    public const string Domain = "jumpingspiders";

    /// <summary>Block code without the variant part.</summary>
    public const string CodeBase = "silkhammock";

    /// <summary>Name of the variant group that holds the support direction.</summary>
    public const string SideVariantGroup = "side";

    /// <summary>The variant used as the item in inventories and for middle-click picking.</summary>
    public const string InventoryVariant = "down";

    /// <summary>Block JSON attribute: break speed multiplier when the player holds shears.</summary>
    public const string ShearsSpeedAttribute = "shearsBreakSpeedMultiplier";

    /// <summary>Failure code shown when there is nothing to attach to. Vanilla has the text for it ("placefailure-requireattachable").</summary>
    public const string NoSupportFailureCode = "requireattachable";

    private BlockFacing supportFacing;
    private float shearsSpeedMultiplier = 1;

    /// <summary>True when the block is any silk hammock variant.</summary>
    public static bool IsHammock(Block block)
    {
        return block is BlockSilkHammock;
    }

    /// <summary>
    /// The hammock variant whose support is in the given direction (north/east/south/west = wall, up = ceiling,
    /// down = floor), or null if the block is not loaded.
    /// </summary>
    public static BlockSilkHammock GetVariant(IWorldAccessor world, BlockFacing supportFacing)
    {
        Block block = world.GetBlock(new AssetLocation(Domain, CodeBase + "-" + supportFacing.Code));
        return block as BlockSilkHammock;
    }

    /// <summary>
    /// True when the block next to pos in direction supportFacing can hold a hammock at pos.
    /// This asks the supporting block itself (CanAttachBlockAt), which by default means its face towards the hammock
    /// is solid. Tree trunks, planks, stone and the ground can hold one; leaves, glass panes, other hammocks cannot.
    /// </summary>
    public static bool IsSupportedFrom(IBlockAccessor blockAccessor, Block hammock, BlockPos pos, BlockFacing supportFacing)
    {
        BlockPos supportPos = pos.AddCopy(supportFacing);
        Block support = blockAccessor.GetBlock(supportPos);
        if (support == null || support.Id == 0)
        {
            return false;
        }
        // The face of the support that touches the hammock points back at it, hence Opposite.
        return support.CanAttachBlockAt(blockAccessor, hammock, supportPos, supportFacing.Opposite, null);
    }

    public override void OnLoaded(ICoreAPI api)
    {
        base.OnLoaded(api);
        supportFacing = BlockFacing.FromCode(Variant[SideVariantGroup]);
        if (Attributes != null)
        {
            shearsSpeedMultiplier = Attributes[ShearsSpeedAttribute].AsFloat(1);
        }
    }

    /// <summary>Direction from the hammock to the block that holds it.</summary>
    public BlockFacing GetSupportFacing()
    {
        return supportFacing;
    }

    /// <summary>True when the support of this variant is still there at pos.</summary>
    public bool CanStayAt(IBlockAccessor blockAccessor, BlockPos pos)
    {
        if (supportFacing == null)
        {
            return false;
        }
        return IsSupportedFrom(blockAccessor, this, pos, supportFacing);
    }

    /// <summary>
    /// Height of the hammock's floor above the bottom of its cell, in blocks: the top of its collision box.
    /// A spider resting in the hammock has its feet at this height.
    /// </summary>
    public double GetFloorTop()
    {
        double top = 0;
        if (CollisionBoxes == null)
        {
            return top;
        }
        for (int i = 0; i < CollisionBoxes.Length; i++)
        {
            if (CollisionBoxes[i] != null && CollisionBoxes[i].Y2 > top)
            {
                top = CollisionBoxes[i].Y2;
            }
        }
        return top;
    }

    /// <summary>
    /// A player places a hammock. The player holds one variant (silkhammock-down), but the variant that ends up in
    /// the world follows the clicked face: clicking the side of a trunk gives a wall hammock against that trunk,
    /// clicking the underside of a branch a ceiling hammock, clicking the ground a floor hammock.
    /// blockSel.Position is the empty cell that will hold the hammock, blockSel.Face the face that was clicked,
    /// so the support is one step back against that face.
    /// </summary>
    public override bool TryPlaceBlock(IWorldAccessor world, IPlayer byPlayer, ItemStack itemstack, BlockSelection blockSel, ref string failureCode)
    {
        if (TryPlaceVariant(world, byPlayer, itemstack, blockSel, blockSel.Face.Opposite, ref failureCode))
        {
            return true;
        }
        // Another reason than a missing support (cell taken, entity in the way, land claim): give up with that reason.
        if (failureCode != NoSupportFailureCode)
        {
            return false;
        }
        // The clicked block cannot hold it: try the other neighbours, in the configured order.
        BlockFacing[] order = HammockBuilder.GetSupportPreference();
        for (int i = 0; i < order.Length; i++)
        {
            if (TryPlaceVariant(world, byPlayer, itemstack, blockSel, order[i], ref failureCode))
            {
                return true;
            }
            if (failureCode != NoSupportFailureCode)
            {
                return false;
            }
        }
        return false;
    }

    // Places the variant held by the given support if that support is solid and the cell is free.
    private bool TryPlaceVariant(IWorldAccessor world, IPlayer byPlayer, ItemStack itemstack, BlockSelection blockSel,
        BlockFacing towardsSupport, ref string failureCode)
    {
        BlockSilkHammock variant = GetVariant(world, towardsSupport);
        if (variant == null || !IsSupportedFrom(world.BlockAccessor, variant, blockSel.Position, towardsSupport))
        {
            failureCode = NoSupportFailureCode;
            return false;
        }
        // CanPlaceBlock checks the cell is free, no entity is in the way, and land claims.
        if (!variant.CanPlaceBlock(world, byPlayer, blockSel, ref failureCode))
        {
            return false;
        }
        return variant.DoPlaceBlock(world, byPlayer, blockSel, itemstack);
    }

    /// <summary>
    /// A neighbour changed. If it was our support and it is gone, the hammock breaks with no player, which drops its
    /// twine and wakes a sleeping spider (see BlockEntitySilkHammock). Felling the tree it hangs on does this too.
    /// Only the server decides; the client gets the change from it.
    /// </summary>
    public override void OnNeighbourBlockChange(IWorldAccessor world, BlockPos pos, BlockPos neibpos)
    {
        base.OnNeighbourBlockChange(world, pos, neibpos);
        if (world.Side != EnumAppSide.Server)
        {
            return;
        }
        if (!CanStayAt(world.BlockAccessor, pos))
        {
            world.BlockAccessor.BreakBlock(pos, null);
        }
    }

    /// <summary>
    /// Runs on the client about every 40 ms while a player holds the break button on the hammock, and returns how
    /// many seconds of breaking are left. dt is the time since the last call. In 1.22.7 shears have no mining speed
    /// of their own, so we scale the time here when shears are held. The scaled dt also goes on to the held tool's
    /// own breaking code inside base.OnGettingBroken. The server does not re-check break times.
    /// </summary>
    public override float OnGettingBroken(IPlayer player, BlockSelection blockSel, ItemSlot itemslot, float remainingResistance, float dt, int counter)
    {
        if (player != null && player.InventoryManager != null && player.InventoryManager.ActiveTool == EnumTool.Shears)
        {
            dt = dt * shearsSpeedMultiplier;
        }
        return base.OnGettingBroken(player, blockSel, itemslot, remainingResistance, dt, counter);
    }

    /// <summary>Middle-click picking (creative) gives the inventory variant, whichever variant was clicked.</summary>
    public override ItemStack OnPickBlock(IWorldAccessor world, BlockPos pos)
    {
        BlockSilkHammock item = GetVariant(world, BlockFacing.FromCode(InventoryVariant));
        if (item == null)
        {
            return base.OnPickBlock(world, pos);
        }
        return new ItemStack(item);
    }
}
