using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace JumpingSpiders;

/// <summary>
/// The silk hammock block, where jumping spiders sleep at night. Its JSON is assets/jumpingspiders/blocktypes/silkhammock.json.
///
/// Twelve variants, named by the direction from the hammock to the block that holds it (its "support") and, for
/// ceiling and floor hammocks, the direction their opening faces:
///   silkhammock-north, -east, -south, -west   hung on a wall or tree trunk on that side; opens away from it
///   silkhammock-up, -up-north, -up-east, -up-south          hanging under a ceiling or branch
///   silkhammock-down, -down-north, -down-east, -down-south  lying on the ground or on top of a block
/// The plain silkhammock-up and silkhammock-down open west: they are the two original variants, and existing worlds
/// hold them under those codes, so the code of a hammock opening west stays as it was and only the three new
/// directions get a suffix. All twelve are states of the one "side" variant group ("up-north" is one state), so no
/// existing block code changed. Read the support and the opening with GetSupportFacing and GetOpeningFacing, never
/// from the code text, and get a variant with GetVariant.
///
/// Vintage Story concepts used here:
/// - One Block object exists per variant; the game calls its methods for every placed block of that variant, with
///   the position as a parameter. Per-position data lives in the block entity (BlockEntitySilkHammock).
/// - Variant["side"] reads the value of the "side" variant group from the block code.
/// - Most hooks run on both sides: the server decides, the client predicts and draws. World.Side tells which.
///
/// How the game calls in:
/// - TryPlaceBlock when a player places one (picks the variant from the clicked face; under a ceiling or on a floor
///   it opens towards the player if that side is free, see HammockOpening).
/// - OnNeighbourBlockChange when a block next to it changes (breaks the hammock when its support is gone).
/// - OnGettingBroken on the client while a player is breaking it (shears break it faster).
/// Drops (4 to 6 flax twine) come from the "drops" list in the JSON.
///
/// Where a spider stands: on the collision box from the JSON (GetFloorTop). Where it sleeps: at
/// GetSleepFeetHeight, the "sleepFeetHeight" attribute. For walls and the floor the two are the same. For the ceiling
/// the collision box is a thin floor (a jump cannot get into the cradle under a solid block, see the JSON), so the
/// sleep task lifts the spider into the cradle and holds it there while it sleeps (LiftsSleeper).
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

    /// <summary>Block JSON attribute: feet height of a sleeping spider above the bottom of the cell, in blocks.</summary>
    public const string SleepFeetHeightAttribute = "sleepFeetHeight";

    /// <summary>A sleep height this much above the collision box's top or more makes the hammock lift its sleeper.</summary>
    public const double LiftEpsilon = 0.001;

    /// <summary>Failure code shown when there is nothing to attach to. Vanilla has the text for it ("placefailure-requireattachable").</summary>
    public const string NoSupportFailureCode = "requireattachable";

    private BlockFacing supportFacing;
    private BlockFacing openingFacing;
    private float shearsSpeedMultiplier = 1;
    private double sleepFeetHeight;

    /// <summary>True when the block is any silk hammock variant.</summary>
    public static bool IsHammock(Block block)
    {
        return block is BlockSilkHammock;
    }

    /// <summary>
    /// The hammock variant whose support is in the given direction (north/east/south/west = wall, up = ceiling,
    /// down = floor), or null if the block is not loaded. For a ceiling or floor this is the one opening west; every
    /// variant of the same support has the same collision box, so code that only cares about the box can use this one.
    /// </summary>
    public static BlockSilkHammock GetVariant(IWorldAccessor world, BlockFacing supportFacing)
    {
        return GetVariant(world, supportFacing, null);
    }

    /// <summary>
    /// The hammock variant with this support that opens towards opening (a horizontal direction). Wall hammocks always
    /// open away from their wall, so for them opening is ignored; null means west. Null if the block is not loaded.
    /// </summary>
    public static BlockSilkHammock GetVariant(IWorldAccessor world, BlockFacing supportFacing, BlockFacing opening)
    {
        Block block = world.GetBlock(new AssetLocation(Domain, CodeBase + "-" + SideState(supportFacing, opening)));
        return block as BlockSilkHammock;
    }

    /// <summary>
    /// The state of the "side" variant group for a support and an opening: "north" for a wall to the north, "up" and
    /// "down" for a ceiling or floor hammock that opens west (the original codes), "up-north", "down-east" and so on
    /// for the other openings.
    /// </summary>
    public static string SideState(BlockFacing supportFacing, BlockFacing opening)
    {
        if (supportFacing.IsHorizontal || opening == null || opening == BlockFacing.WEST || !opening.IsHorizontal)
        {
            return supportFacing.Code;
        }
        return supportFacing.Code + "-" + opening.Code;
    }

    /// <summary>
    /// Which of the four sides of pos are blocked, in BlockFacing.HORIZONTALS order (north, east, south, west): the
    /// neighbouring cell holds something a creature collides with, so an opening there would look into a wall.
    /// </summary>
    public static bool[] FindBlockedSides(IBlockAccessor blockAccessor, BlockPos pos)
    {
        bool[] blocked = new bool[4];
        for (int i = 0; i < 4; i++)
        {
            BlockPos side = pos.AddCopy(BlockFacing.HORIZONTALS[i]);
            Block block = blockAccessor.GetBlock(side);
            if (block == null || block.Id == 0)
            {
                continue;
            }
            Cuboidf[] boxes = block.GetCollisionBoxes(blockAccessor, side);
            blocked[i] = boxes != null && boxes.Length > 0;
        }
        return blocked;
    }

    /// <summary>
    /// The variant a player gets when placing a hammock at pos held by the block in direction towardsSupport, standing
    /// at playerPos (null when unknown): a wall hammock opening away from the wall; under a ceiling or on a floor one
    /// opening towards the player if that side is free, else the free side nearest to that, else towards the player
    /// anyway (HammockOpening.ChooseTowards). TryPlaceBlock uses this.
    /// </summary>
    public static BlockSilkHammock ChooseVariantForPlacement(IWorldAccessor world, BlockPos pos, BlockFacing towardsSupport, Vec3d playerPos)
    {
        if (towardsSupport.IsHorizontal || playerPos == null)
        {
            return GetVariant(world, towardsSupport);
        }
        bool[] blocked = FindBlockedSides(world.BlockAccessor, pos);
        int opening = HammockOpening.ChooseTowards(blocked, playerPos.X - (pos.X + 0.5), playerPos.Z - (pos.Z + 0.5));
        return GetVariant(world, towardsSupport, BlockFacing.HORIZONTALS[opening]);
    }

    /// <summary>
    /// The variant a spider (or other code) gets when building at pos held by the block in direction supportFacing: a
    /// wall hammock opening away from the wall; under a ceiling or on a floor one opening towards a random free side,
    /// or any side when all four are blocked (HammockOpening.ChooseRandom).
    /// </summary>
    public static BlockSilkHammock ChooseVariantAtRandom(IWorldAccessor world, BlockPos pos, BlockFacing supportFacing)
    {
        if (supportFacing.IsHorizontal)
        {
            return GetVariant(world, supportFacing);
        }
        bool[] blocked = FindBlockedSides(world.BlockAccessor, pos);
        int opening = HammockOpening.ChooseRandom(blocked, world.Rand);
        return GetVariant(world, supportFacing, BlockFacing.HORIZONTALS[opening]);
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
        ReadSideState(Variant[SideVariantGroup]);
        if (Attributes != null)
        {
            shearsSpeedMultiplier = Attributes[ShearsSpeedAttribute].AsFloat(1);
        }
        double floorTop = GetFloorTop();
        sleepFeetHeight = floorTop;
        if (Attributes != null)
        {
            sleepFeetHeight = Attributes[SleepFeetHeightAttribute].AsDouble(floorTop);
        }
    }

    // "north" (a wall), "up", "down", or "up-north" and the like (see the class summary).
    private void ReadSideState(string state)
    {
        supportFacing = null;
        openingFacing = null;
        if (state == null)
        {
            return;
        }
        string[] parts = state.Split('-');
        supportFacing = BlockFacing.FromCode(parts[0]);
        if (supportFacing == null)
        {
            return;
        }
        if (supportFacing.IsHorizontal)
        {
            openingFacing = supportFacing.Opposite;
        }
        else if (parts.Length > 1)
        {
            openingFacing = BlockFacing.FromCode(parts[1]);
        }
        if (openingFacing == null)
        {
            openingFacing = BlockFacing.WEST;
        }
    }

    /// <summary>Direction from the hammock to the block that holds it.</summary>
    public BlockFacing GetSupportFacing()
    {
        return supportFacing;
    }

    /// <summary>Direction the hammock's opening faces (always horizontal): away from the wall, or as the variant says.</summary>
    public BlockFacing GetOpeningFacing()
    {
        return openingFacing;
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
    /// Height of the hammock's floor above the bottom of its cell, in blocks: the top of its collision box in the
    /// JSON, where a spider landing in the hammock stands. Each variant has its own box: 9/16 (just above the cradle's bottom)
    /// for wall hammocks, 1/16 for the ceiling hammock (a landing floor under the cradle) and the floor hammock (its
    /// base). Where it sleeps is GetSleepFeetHeight.
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
    /// Feet height of a sleeping spider above the bottom of the cell, in blocks: 9/16 (just above the cradle's bottom) for wall
    /// and ceiling hammocks, 1/16 (the base) for the floor hammock.
    /// </summary>
    public double GetSleepFeetHeight()
    {
        return sleepFeetHeight;
    }

    /// <summary>
    /// True when a spider lands lower than it sleeps in this variant (the ceiling hammock): falling asleep lifts it
    /// into the cradle, where nothing holds it up, so the sleep task holds it still until it wakes.
    /// </summary>
    public bool LiftsSleeper()
    {
        return sleepFeetHeight > GetFloorTop() + LiftEpsilon;
    }

    /// <summary>
    /// The yaw (radians, the game's convention: 0 looks towards +Z) of a spider sleeping in this variant: out through
    /// the opening, away from the wall for wall hammocks, the variant's opening for ceiling and floor hammocks
    /// (see HammockFacing).
    /// </summary>
    public float GetSleepYaw()
    {
        if (supportFacing == null)
        {
            return 0;
        }
        return (float)HammockFacing.SleepYaw(supportFacing.Normali.X, supportFacing.Normali.Y, supportFacing.Normali.Z,
            openingFacing.Normali.X, openingFacing.Normali.Z);
    }

    /// <summary>
    /// A player places a hammock. The player holds one variant (silkhammock-down), but the variant that ends up in
    /// the world follows the clicked face: clicking the side of a trunk gives a wall hammock against that trunk,
    /// clicking the underside of a branch a ceiling hammock, clicking the ground a floor hammock. A ceiling or floor
    /// hammock opens towards the player when that side is free (ChooseVariantForPlacement). This runs on the client
    /// too, to show the block at once; the server's choice is the one that stays.
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
        Vec3d playerPos = null;
        if (byPlayer != null && byPlayer.Entity != null)
        {
            playerPos = byPlayer.Entity.Pos.XYZ;
        }
        BlockSilkHammock variant = ChooseVariantForPlacement(world, blockSel.Position, towardsSupport, playerPos);
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
