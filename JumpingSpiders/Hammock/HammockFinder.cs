using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace JumpingSpiders;

/// <summary>
/// Finds hammocks in the world. Used by the sleep task (a free hammock for tonight), by HammockBuilder and by
/// spawning (the spacing rule), and by anything that has a hammock position and wants its block entity.
///
/// Searches go through the vanilla POI registry (see BlockEntitySilkHammock), which files hammocks by chunk column,
/// so a search only looks at the hammocks in the few chunk columns around the centre instead of scanning blocks.
/// The registry only exists on the server, so FindNearestFree, FindAllWithin and HasHammockWithinSpacing find
/// nothing on a client. Hammocks in chunks that are not loaded are not found (a spider there is not active either).
/// </summary>
public static class HammockFinder
{
    /// <summary>The hammock block entity at pos, or null if there is no hammock there (or its chunk is not loaded).</summary>
    public static BlockEntitySilkHammock GetHammockAt(IWorldAccessor world, BlockPos pos)
    {
        if (world == null || pos == null)
        {
            return null;
        }
        return world.BlockAccessor.GetBlockEntity<BlockEntitySilkHammock>(pos);
    }

    /// <summary>
    /// The nearest hammock within range blocks of from (straight-line distance to the hammock's cell centre) that is
    /// free for forSpider or already claimed by it, or null. Pass the spider's entity.Pos.XYZ as from.
    /// Server only.
    /// </summary>
    public static BlockEntitySilkHammock FindNearestFree(IWorldAccessor world, Vec3d from, double range, Entity forSpider)
    {
        POIRegistry registry = GetRegistry(world);
        if (registry == null || from == null)
        {
            return null;
        }
        HammockPoiSearch search = new HammockPoiSearch();
        search.ForSpider = forSpider;
        IPointOfInterest found = registry.GetNearestPoi(from, (float)range, search.MatchFreeHammock);
        return found as BlockEntitySilkHammock;
    }

    /// <summary>Every hammock within range blocks of center (to the hammock's cell centre), in no particular order. Server only.</summary>
    public static List<BlockEntitySilkHammock> FindAllWithin(IWorldAccessor world, Vec3d center, double range)
    {
        HammockPoiSearch search = new HammockPoiSearch();
        POIRegistry registry = GetRegistry(world);
        if (registry == null || center == null)
        {
            return search.Found;
        }
        registry.GetNearestPoi(center, (float)range, search.CollectHammock);
        return search.Found;
    }

    /// <summary>
    /// True when a hammock at pos would be too close to an existing one under the "one hammock per
    /// spacing x spacing x spacing box" rule (see HammockSpacing). A hammock already at pos counts too. Server only.
    /// </summary>
    public static bool HasHammockWithinSpacing(IWorldAccessor world, BlockPos pos, int spacing)
    {
        if (pos == null)
        {
            return false;
        }
        List<BlockEntitySilkHammock> nearby = FindAllWithin(world, CellCenter(pos), SpacingSearchRange(spacing));
        return ConflictsWithAny(pos, nearby, spacing);
    }

    /// <summary>True when a hammock at pos would break the spacing rule against any hammock in the list.</summary>
    public static bool ConflictsWithAny(BlockPos pos, List<BlockEntitySilkHammock> hammocks, int spacing)
    {
        for (int i = 0; i < hammocks.Count; i++)
        {
            BlockPos other = hammocks[i].Pos;
            if (other.dimension != pos.dimension)
            {
                continue;
            }
            if (HammockSpacing.Conflicts(pos.X, pos.Y, pos.Z, other.X, other.Y, other.Z, spacing))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// The search radius that surely contains every hammock that can conflict with a spot: conflicting hammocks are
    /// at most ScanRadius blocks away on each axis, so at most ScanRadius * sqrt(3) in a straight line.
    /// One block is added so rounding never drops one.
    /// </summary>
    public static double SpacingSearchRange(int spacing)
    {
        return HammockSpacing.ScanRadius(spacing) * Math.Sqrt(3) + 1;
    }

    /// <summary>The centre of a block cell, the way the POI registry stores hammock positions.</summary>
    public static Vec3d CellCenter(BlockPos pos)
    {
        return pos.ToVec3d().Add(0.5, 0.5, 0.5);
    }

    private static POIRegistry GetRegistry(IWorldAccessor world)
    {
        if (world == null || world.Side != EnumAppSide.Server)
        {
            return null;
        }
        return world.Api.ModLoader.GetModSystem<POIRegistry>();
    }
}
