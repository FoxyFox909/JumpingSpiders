using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace JumpingSpiders;

/// <summary>
/// Puts hammocks into the world from code: a domesticated spider building one after a meal (TryBuildHammockFor),
/// and wild spawning placing one high on a tree trunk (PlaceHammock). Server only.
///
/// How a domesticated spider picks its spot (TryBuildHammockFor):
/// 1. Look at Config.HammockBuildSamples random block columns within Config.HammockBuildSearchRadius of the spider.
///    In each, every cell from HammockBuildSearchDown below to HammockBuildSearchUp above the spider's feet that can
///    hold a hammock (FindSupportFacing) and keeps the one-per-box spacing rule becomes a candidate.
/// 2. Pick a candidate with HeightWeightedPicker, so high spots are much more likely (Config.HammockHeightBias).
/// 3. Ask the jump planner whether the spider could get there, as if the hammock already stood there. If not, drop
///    that candidate and pick again, at most Config.HammockBuildMaxPlanAttempts times (planning is the expensive part).
/// 4. Place the hammock on the first reachable one. The spider does not have to go there now.
/// </summary>
public static class HammockBuilder
{
    /// <summary>Most tries to find a random column inside the search circle before taking whatever came up.</summary>
    public const int ColumnPickTries = 8;

    /// <summary>
    /// The direction of the block that would hold a hammock at pos, or null when the cell cannot hold one: it is not
    /// empty (or holds only something replaceable such as tall grass), it holds water, its chunk is not loaded, or no
    /// neighbour has a solid face towards it. When several neighbours could, the first one in
    /// Config.HammockSupportPreference wins (by default walls first, then ceiling, then floor).
    /// </summary>
    public static BlockFacing FindSupportFacing(IWorldAccessor world, BlockPos pos)
    {
        BlockSilkHammock anyVariant = BlockSilkHammock.GetVariant(world, BlockFacing.DOWN);
        if (anyVariant == null || !IsCellFreeForHammock(world, pos, anyVariant))
        {
            return null;
        }
        BlockFacing[] order = GetSupportPreference();
        for (int i = 0; i < order.Length; i++)
        {
            BlockSilkHammock variant = BlockSilkHammock.GetVariant(world, order[i]);
            if (variant != null && BlockSilkHammock.IsSupportedFrom(world.BlockAccessor, variant, pos, order[i]))
            {
                return order[i];
            }
        }
        return null;
    }

    /// <summary>
    /// Places the hammock variant held by the block in direction supportFacing at pos. Returns false, and changes
    /// nothing, when the cell is not free, that neighbour cannot hold a hammock, or an entity is in the way.
    /// Does not check the spacing rule; call HammockFinder.HasHammockWithinSpacing first if it matters.
    /// </summary>
    public static bool PlaceHammock(IWorldAccessor world, BlockPos pos, BlockFacing supportFacing)
    {
        if (world == null || pos == null || supportFacing == null)
        {
            return false;
        }
        BlockSilkHammock variant = BlockSilkHammock.GetVariant(world, supportFacing);
        if (variant == null)
        {
            return false;
        }
        if (!IsCellFreeForHammock(world, pos, variant))
        {
            return false;
        }
        if (!BlockSilkHammock.IsSupportedFrom(world.BlockAccessor, variant, pos, supportFacing))
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
    /// A domesticated spider builds a hammock after a meal (see the class summary for how it picks the spot).
    /// Returns true when it placed one. Does not check whether the spider is domesticated; the caller decides that.
    /// </summary>
    public static bool TryBuildHammockFor(EntityAgent spider)
    {
        if (spider == null || !spider.Alive || spider.World == null || spider.World.Side != EnumAppSide.Server)
        {
            return false;
        }
        IWorldAccessor world = spider.World;
        JumpingSpidersConfig config = JumpingSpidersModSystem.Config;

        List<HammockSite> sites = CollectBuildSites(spider, config);
        if (sites.Count == 0)
        {
            return false;
        }

        List<double> heights = new List<double>();
        for (int i = 0; i < sites.Count; i++)
        {
            heights.Add(sites[i].FeetY);
        }

        JumpLimits limits = config.CreateJumpLimits();
        SpiderJumpWorld realWorld = new SpiderJumpWorld(spider);
        for (int attempt = 0; attempt < config.HammockBuildMaxPlanAttempts && sites.Count > 0; attempt++)
        {
            int index = HeightWeightedPicker.PickIndex(heights, config.HammockHeightBias, world.Rand);
            HammockSite site = sites[index];
            sites.RemoveAt(index);
            heights.RemoveAt(index);

            if (!CanReach(spider, realWorld, site, limits, world.Rand))
            {
                continue;
            }
            if (PlaceHammock(world, site.Pos, site.SupportFacing))
            {
                return true;
            }
        }
        return false;
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

    // Step 1 of the class summary: candidate cells in random columns around the spider.
    private static List<HammockSite> CollectBuildSites(EntityAgent spider, JumpingSpidersConfig config)
    {
        IWorldAccessor world = spider.World;
        List<HammockSite> sites = new List<HammockSite>();
        BlockSilkHammock anyVariant = BlockSilkHammock.GetVariant(world, BlockFacing.DOWN);
        if (anyVariant == null)
        {
            return sites;
        }
        double floorTop = anyVariant.GetFloorTop();

        int feetX = JumpPlanner.CellOfX(spider.Pos.X);
        int feetY = JumpPlanner.CellOfFeetY(spider.Pos.Y);
        int feetZ = JumpPlanner.CellOfX(spider.Pos.Z);
        int radius = config.HammockBuildSearchRadius;

        // Every existing hammock that could clash with a candidate, fetched once for the whole attempt.
        double existingRange = Math.Sqrt(2.0 * radius * radius)
            + Math.Max(config.HammockBuildSearchUp, config.HammockBuildSearchDown)
            + HammockFinder.SpacingSearchRange(config.HammockSpacing);
        List<BlockEntitySilkHammock> existing = HammockFinder.FindAllWithin(world, spider.Pos.XYZ, existingRange);

        HashSet<long> seenColumns = new HashSet<long>();
        for (int sample = 0; sample < config.HammockBuildSamples; sample++)
        {
            int dx = 0;
            int dz = 0;
            for (int tryNumber = 0; tryNumber < ColumnPickTries; tryNumber++)
            {
                dx = world.Rand.Next(-radius, radius + 1);
                dz = world.Rand.Next(-radius, radius + 1);
                if (dx * dx + dz * dz <= radius * radius)
                {
                    break;
                }
            }
            long columnKey = ((long)(dx + radius) << 32) | (long)(dz + radius);
            if (!seenColumns.Add(columnKey))
            {
                continue;
            }

            for (int y = feetY - config.HammockBuildSearchDown; y <= feetY + config.HammockBuildSearchUp; y++)
            {
                BlockPos pos = new BlockPos(feetX + dx, y, feetZ + dz, spider.Pos.Dimension);
                BlockFacing facing = FindSupportFacing(world, pos);
                if (facing == null)
                {
                    continue;
                }
                if (HammockFinder.ConflictsWithAny(pos, existing, config.HammockSpacing))
                {
                    continue;
                }
                HammockSite site = new HammockSite();
                site.Pos = pos;
                site.SupportFacing = facing;
                site.FeetY = y + floorTop;
                sites.Add(site);
            }
        }
        return sites;
    }

    // Step 3 of the class summary: could the spider jump into this hammock if it stood there already?
    private static bool CanReach(EntityAgent spider, SpiderJumpWorld realWorld, HammockSite site, JumpLimits limits, Random rand)
    {
        BlockSilkHammock variant = BlockSilkHammock.GetVariant(spider.World, site.SupportFacing);
        Cuboidf floor = BoundsOf(variant.CollisionBoxes);
        if (floor == null)
        {
            // A hammock without a floor (if the JSON ever drops it) gives nothing to stand on.
            return false;
        }
        Cuboidf box = spider.CollisionBox;
        JumpWorldWithExtraBox future = new JumpWorldWithExtraBox(realWorld,
            site.Pos.X, site.Pos.Y, site.Pos.Z,
            floor.X1, floor.Y1, floor.Z1, floor.X2, floor.Y2, floor.Z2,
            box.X1, box.Y1, box.Z1, box.X2, box.Y2, box.Z2);
        List<JumpStep> plan = JumpPlanner.PlanJumps(future, spider.Pos.X, spider.Pos.Y, spider.Pos.Z,
            site.Pos.X, site.Pos.Y, site.Pos.Z, limits, rand);
        return plan != null;
    }

    // One box around all of a block's collision boxes, or null when it has none.
    private static Cuboidf BoundsOf(Cuboidf[] boxes)
    {
        if (boxes == null || boxes.Length == 0)
        {
            return null;
        }
        Cuboidf bounds = boxes[0].Clone();
        for (int i = 1; i < boxes.Length; i++)
        {
            bounds.X1 = Math.Min(bounds.X1, boxes[i].X1);
            bounds.Y1 = Math.Min(bounds.Y1, boxes[i].Y1);
            bounds.Z1 = Math.Min(bounds.Z1, boxes[i].Z1);
            bounds.X2 = Math.Max(bounds.X2, boxes[i].X2);
            bounds.Y2 = Math.Max(bounds.Y2, boxes[i].Y2);
            bounds.Z2 = Math.Max(bounds.Z2, boxes[i].Z2);
        }
        return bounds;
    }

    // Matcher for GetIntersectingEntities: entities that block placement, the same test vanilla uses for players.
    private static bool IsSolidEntity(Entity entity)
    {
        return entity.IsInteractable;
    }
}
