using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace JumpingSpiders;

/// <summary>
/// Puts hammocks into the world from code: the spot a domesticated spider picks for a build attempt
/// (TryPickBuildSite), and the placing itself (PlaceHammock), which wild spawning uses too for hammocks on tree trunks.
/// Server only.
///
/// How a build attempt picks its spot (TryPickBuildSite): a random cell in the cylinder around the spider
/// (Config.HammockBuildSearchRadius wide, Config.HammockBuildSearchDown below to HammockBuildSearchUp above its feet),
/// tried again until one can hold a hammock (ChooseBuildFacing) and the spider can see it (a clear straight line from
/// its eyes, BuildReach.IsLineClear: nothing behind a wall or under the floor), or the tries run out. Every such cell
/// is equally likely, whatever its height. Each try costs a handful of block lookups; nothing here plans jumps. Whether
/// the spider can get there, and the one-per-box spacing rule, are for AiTaskSpiderBuildHammock to find out.
/// </summary>
public static class HammockBuilder
{
    /// <summary>
    /// The direction of the block that would hold a hammock built by a spider at pos, or null when the cell cannot take
    /// one: it is not empty (or holds only something replaceable such as tall grass), it holds water, its chunk is not
    /// loaded, or no neighbour has a solid face towards it. When several neighbours could, the first one in
    /// Config.HammockSupportPreference wins (by default walls first, then ceiling, then floor).
    ///
    /// One sanity check on top: when the block right above the cell is solid, a wall hammock is never chosen there,
    /// because no spider could ever jump into it (only 7/16 of a block of room above its floor, see the collision boxes
    /// in silkhammock.json). The ceiling hammock, made for exactly that case, can still be chosen.
    /// </summary>
    public static BlockFacing ChooseBuildFacing(IWorldAccessor world, BlockPos pos)
    {
        return FindSupportFacing(world, pos, GetSupportPreference(), true);
    }

    /// <summary>
    /// The first direction in order whose neighbour can hold a hammock in pos, or null when there is none or the cell
    /// itself cannot take a hammock (IsCellFreeForHammock). The check that both building (ChooseBuildFacing) and wild
    /// hammocks on tree trunks (WildHammockSites) use. noWallUnderSolidBlock: never a wall hammock under a solid block
    /// (see ChooseBuildFacing); wild hammocks leave it off, since their spots are tested with real jumps instead.
    /// </summary>
    public static BlockFacing FindSupportFacing(IWorldAccessor world, BlockPos pos, BlockFacing[] order, bool noWallUnderSolidBlock)
    {
        BlockSilkHammock anyVariant = BlockSilkHammock.GetVariant(world, BlockFacing.DOWN);
        if (anyVariant == null || !IsCellFreeForHammock(world, pos, anyVariant))
        {
            return null;
        }
        bool solidAbove = noWallUnderSolidBlock && HasCollisionBoxes(world.BlockAccessor, pos.UpCopy());
        for (int i = 0; i < order.Length; i++)
        {
            if (solidAbove && order[i].IsHorizontal)
            {
                continue;
            }
            BlockSilkHammock variant = BlockSilkHammock.GetVariant(world, order[i]);
            if (variant != null && BlockSilkHammock.IsSupportedFrom(world.BlockAccessor, variant, pos, order[i]))
            {
                return order[i];
            }
        }
        return null;
    }

    /// <summary>
    /// Picks a random cell near feetCell that can hold a hammock and is visible from the eye position, trying at most
    /// tries cells (see the class summary), or returns null. used says how many tries it took. Uses world.Rand. Pass a
    /// small number of tries per server tick and call again in the next tick to spread the cost; every try is
    /// independent, so splitting them up does not change the odds.
    /// </summary>
    public static HammockSite TryPickBuildSite(IWorldAccessor world, BlockPos feetCell, double eyeX, double eyeY, double eyeZ,
        ISolidPointWorld sight, int tries, out int used)
    {
        JumpingSpidersConfig config = JumpingSpidersModSystem.Config;
        int radius = config.HammockBuildSearchRadius;
        BlockPos pos = new BlockPos(feetCell.dimension);
        used = 0;
        for (int i = 0; i < tries; i++)
        {
            used++;
            int dx = world.Rand.Next(-radius, radius + 1);
            int dz = world.Rand.Next(-radius, radius + 1);
            if (dx * dx + dz * dz > radius * radius)
            {
                // Outside the circle: a wasted try, which keeps every cell inside equally likely.
                continue;
            }
            int dy = world.Rand.Next(-config.HammockBuildSearchDown, config.HammockBuildSearchUp + 1);
            pos.Set(feetCell.X + dx, feetCell.Y + dy, feetCell.Z + dz);
            BlockFacing facing = ChooseBuildFacing(world, pos);
            if (facing != null && BuildReach.IsLineClear(sight, eyeX, eyeY, eyeZ, pos.X, pos.Y, pos.Z))
            {
                HammockSite site = new HammockSite();
                site.Pos = pos.Copy();
                site.SupportFacing = facing;
                return site;
            }
        }
        return null;
    }

    /// <summary>
    /// Places a hammock held by the block in direction supportFacing at pos. A wall hammock opens away from its wall;
    /// a ceiling or floor hammock opens towards a random side whose neighbouring cell is free, or any side when none
    /// is (BlockSilkHammock.ChooseVariantAtRandom). Returns false, and changes nothing, when the cell is not free, that
    /// neighbour cannot hold a hammock, or an entity is in the way.
    /// Does not check the spacing rule; call HammockFinder.FindHammockWithinSpacing first if it matters.
    /// </summary>
    public static bool PlaceHammock(IWorldAccessor world, BlockPos pos, BlockFacing supportFacing)
    {
        if (world == null || pos == null || supportFacing == null)
        {
            return false;
        }
        return PlaceVariant(world, pos, BlockSilkHammock.ChooseVariantAtRandom(world, pos, supportFacing));
    }

    // Places this exact variant (see PlaceHammock).
    private static bool PlaceVariant(IWorldAccessor world, BlockPos pos, BlockSilkHammock variant)
    {
        if (variant == null)
        {
            return false;
        }
        if (!IsCellFreeForHammock(world, pos, variant))
        {
            return false;
        }
        if (!BlockSilkHammock.IsSupportedFrom(world.BlockAccessor, variant, pos, variant.GetSupportFacing()))
        {
            return false;
        }
        // Like a player placing a block: not into a creature or player standing there.
        Entity[] inTheWay = world.GetIntersectingEntities(pos, variant.CollisionBoxes, IsSolidEntity);
        if (inTheWay != null && inTheWay.Length > 0)
        {
            return false;
        }
        // SetBlock also creates the block entity right away (it registers itself with the POI registry).
        world.BlockAccessor.SetBlock(variant.BlockId, pos);
        return true;
    }

    /// <summary>
    /// Config.HammockSupportPreference as directions, skipping misspelled or repeated entries. Falls back to all six
    /// directions (walls, ceiling, floor) when the setting is missing or empty.
    /// </summary>
    public static BlockFacing[] GetSupportPreference()
    {
        string[] codes = JumpingSpidersModSystem.Config.HammockSupportPreference;
        List<BlockFacing> order = new List<BlockFacing>();
        if (codes != null)
        {
            for (int i = 0; i < codes.Length; i++)
            {
                BlockFacing facing = BlockFacing.FromCode(codes[i]);
                if (facing != null && !order.Contains(facing))
                {
                    order.Add(facing);
                }
            }
        }
        if (order.Count == 0)
        {
            return BlockFacing.ALLFACES;
        }
        return order.ToArray();
    }

    /// <summary>
    /// True when pos could take a hammock as far as the cell itself goes: loaded, empty or holding only something a
    /// hammock may replace (tall grass, snow layer), and no water or lava in it.
    /// </summary>
    public static bool IsCellFreeForHammock(IWorldAccessor world, BlockPos pos, Block hammock)
    {
        IBlockAccessor blockAccessor = world.BlockAccessor;
        if (blockAccessor.GetChunkAtBlockPos(pos) == null)
        {
            return false;
        }
        Block fluid = blockAccessor.GetBlock(pos, BlockLayersAccess.Fluid);
        if (fluid != null && fluid.Id != 0)
        {
            return false;
        }
        Block current = blockAccessor.GetBlock(pos, BlockLayersAccess.Solid);
        if (current == null || current.Id == 0)
        {
            return true;
        }
        return current.IsReplacableBy(hammock);
    }

    /// <summary>True when the block at pos has something a creature collides with (a full block, a slab, a fence...).</summary>
    public static bool HasCollisionBoxes(IBlockAccessor blockAccessor, BlockPos pos)
    {
        Block block = blockAccessor.GetBlock(pos);
        if (block == null || block.Id == 0)
        {
            return false;
        }
        Cuboidf[] boxes = block.GetCollisionBoxes(blockAccessor, pos);
        return boxes != null && boxes.Length > 0;
    }

    // Matcher for GetIntersectingEntities: entities that block placement, the same test vanilla uses for players.
    private static bool IsSolidEntity(Entity entity)
    {
        return entity.IsInteractable;
    }
}
