using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace JumpingSpiders;

/// <summary>
/// Gives a spider chunk its one wild hammock: looks first, places second. Called by WildSpiderSpawner on the server
/// main thread when a new wild spider rolled "comes with a hammock".
///
/// 1. The chunk's record (WildHammockRecords) names a hammock: if the hammock is still there, use it.
///    If it is gone (broken, burnt), forget it and carry on.
/// 2. Any hammock within Config.WildHammockReuseRange of the chunk's centre: adopt it as the chunk's hammock.
/// 3. Place a new one on a tree trunk in the chunk: a cell beside the trunk, Config.WildHammockMinHeight to
///    WildHammockMaxHeight blocks above its base, that a spider on the ground nearby could jump into (one jump, or
///    two by way of a perch such as a branch; see HammockReachFinder). Among the reachable spots, higher ones are
///    much more likely (Config.WildHammockHeightBias). Only Config.WildHammockTreesPerAttempt trunks are tested.
///    When none works, the record says so and the chunk is not searched again for Config.WildHammockRetryDays.
/// </summary>
public static class WildHammockSites
{
    /// <summary>Ground spots are searched this many blocks above and below the trunk base.</summary>
    public const int GroundSearchUpDown = 2;

    /// <summary>
    /// The chunk's hammock (existing or newly placed), or null when there is none and none could be placed.
    /// Updates and saves the record. spiderBox is the spider's collision box (relative to its feet).
    /// </summary>
    public static BlockPos FindOrPlace(IWorldAccessor world, IMapChunk mapChunk, WildHammockRecord record,
        SpawnRequest request, Cuboidf spiderBox, SpawnResult result)
    {
        JumpingSpidersConfig config = JumpingSpidersModSystem.Config;

        // 1. The remembered hammock, checked against the world.
        if (record.State == WildHammockState.HasHammock)
        {
            BlockPos remembered = new BlockPos(record.X, record.Y, record.Z, 0);
            if (HammockFinder.GetHammockAt(world, remembered) != null)
            {
                result.HammockReused = true;
                result.Note("Reused the chunk's hammock at " + Describe(remembered) + ".");
                return remembered;
            }
            result.Note("The chunk's recorded hammock at " + Describe(remembered) + " is gone.");
            record.ClearHammock();
            WildHammockRecords.Save(mapChunk, record);
        }

        // 2. Look for a hammock that is already near: reuse it rather than placing another one close by.
        BlockPos center = ChunkCenterAtGround(mapChunk, request.ChunkX, request.ChunkZ);
        BlockEntitySilkHammock nearest = FindNearestHammock(world, center, config.WildHammockReuseRange);
        if (nearest != null)
        {
            BlockPos found = nearest.Pos.Copy();
            Remember(mapChunk, record, found);
            result.HammockReused = true;
            result.Note("Adopted the existing hammock at " + Describe(found) + " as the chunk's hammock.");
            return found;
        }

        // 3. Place a new one, unless a recent search already found no usable tree.
        double now = world.Calendar.TotalHours;
        if (record.State == WildHammockState.NoSite
            && !SpawnRules.ShouldSearchAgain(record.SearchedTotalHours, now, config.WildHammockRetryDays * world.Calendar.HoursPerDay))
        {
            result.NoUsableTree = true;
            result.Note("No usable tree here (searched recently; next search after " + config.WildHammockRetryDays + " in-game days).");
            return null;
        }

        BlockPos placed = PlaceOnTree(world, mapChunk, request, spiderBox, result);
        if (placed == null)
        {
            record.ClearHammock();
            record.State = WildHammockState.NoSite;
            record.SearchedTotalHours = now;
            WildHammockRecords.Save(mapChunk, record);
            result.NoUsableTree = true;
            result.Note("No usable tree: " + result.TrunksFound + " trunks found, " + result.TrunksTested + " tested, "
                + result.SpotsTested + " hammock spots tested, none reachable by jumping.");
            return null;
        }
        Remember(mapChunk, record, placed);
        result.HammockPlaced = true;
        result.Note("Placed a new hammock at " + Describe(placed) + ", " + result.HammockHeight + " blocks above the trunk base.");
        return placed;
    }

    /// <summary>The centre column of a chunk at its generated ground height (one block above the surface).</summary>
    public static BlockPos ChunkCenterAtGround(IMapChunk mapChunk, int chunkX, int chunkZ)
    {
        int half = TreeTrunks.ChunkSize / 2;
        int y = 0;
        if (mapChunk != null && mapChunk.WorldGenTerrainHeightMap != null)
        {
            y = mapChunk.WorldGenTerrainHeightMap[half * TreeTrunks.ChunkSize + half] + 1;
        }
        return new BlockPos(chunkX * TreeTrunks.ChunkSize + half, y, chunkZ * TreeTrunks.ChunkSize + half, 0);
    }

    /// <summary>"x, y, z" for messages.</summary>
    public static string Describe(BlockPos pos)
    {
        return pos.X + ", " + pos.Y + ", " + pos.Z;
    }

    private static void Remember(IMapChunk mapChunk, WildHammockRecord record, BlockPos pos)
    {
        record.State = WildHammockState.HasHammock;
        record.X = pos.X;
        record.Y = pos.Y;
        record.Z = pos.Z;
        record.SearchedTotalHours = WildHammockRecord.Never;
        WildHammockRecords.Save(mapChunk, record);
    }

    private static BlockEntitySilkHammock FindNearestHammock(IWorldAccessor world, BlockPos center, double range)
    {
        List<BlockEntitySilkHammock> all = HammockFinder.FindAllWithin(world, HammockFinder.CellCenter(center), range);
        BlockEntitySilkHammock best = null;
        double bestDistance = double.MaxValue;
        for (int i = 0; i < all.Count; i++)
        {
            double distance = all[i].Pos.DistanceTo(center);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = all[i];
            }
        }
        return best;
    }

    // Step 3 of the class summary. Returns the new hammock's position, or null.
    private static BlockPos PlaceOnTree(IWorldAccessor world, IMapChunk mapChunk, SpawnRequest request, Cuboidf spiderBox,
        SpawnResult result)
    {
        JumpingSpidersConfig config = JumpingSpidersModSystem.Config;
        result.SearchedTrees = true;
        List<TrunkColumn> trunks = TreeTrunks.FindInChunk(world.BlockAccessor, mapChunk, request.ChunkX, request.ChunkZ,
            config.WildHammockMaxHeight + 1);
        result.TrunksFound = trunks.Count;
        Shuffle(trunks, request);

        List<HammockCandidate> reachable = new List<HammockCandidate>();
        for (int i = 0; i < trunks.Count && result.TrunksTested < request.MaxTreesToTest; i++)
        {
            List<HammockCandidate> candidates = EvaluateTrunk(world, trunks[i], request, spiderBox);
            if (candidates.Count == 0)
            {
                continue;
            }
            result.TrunksTested++;
            result.SpotsTested += candidates.Count;
            for (int c = 0; c < candidates.Count; c++)
            {
                if (candidates[c].Reachable)
                {
                    reachable.Add(candidates[c]);
                }
            }
        }
        result.SpotsReachable = reachable.Count;

        // Pick among the reachable spots, higher ones much more likely; if placing fails, pick again.
        List<double> heights = new List<double>();
        for (int i = 0; i < reachable.Count; i++)
        {
            heights.Add(reachable[i].Height);
        }
        while (reachable.Count > 0)
        {
            int index = HeightWeightedPicker.PickIndex(heights, config.WildHammockHeightBias, request.Rand);
            HammockCandidate chosen = reachable[index];
            reachable.RemoveAt(index);
            heights.RemoveAt(index);

            BlockPos pos = new BlockPos(chosen.CellX, chosen.CellY, chosen.CellZ, 0);
            if (HammockFinder.HasHammockWithinSpacing(world, pos, config.HammockSpacing))
            {
                continue;
            }
            if (HammockBuilder.PlaceHammock(world, pos, BlockFacing.ALLFACES[chosen.Tag]))
            {
                result.HammockHeight = chosen.Height;
                return pos;
            }
        }
        return null;
    }

    /// <summary>
    /// Every cell beside one trunk that could hold a wild hammock (see CollectCandidates), each marked with whether a
    /// spider on the ground near the tree could jump into it. Tag is the direction index (BlockFacing.ALLFACES) from
    /// the cell to the trunk. The expensive part of wild hammock placement: one perch search plus a few jump checks
    /// per spot. Changes nothing in the world.
    /// </summary>
    public static List<HammockCandidate> EvaluateTrunk(IWorldAccessor world, TrunkColumn trunk, SpawnRequest request, Cuboidf spiderBox)
    {
        JumpingSpidersConfig config = JumpingSpidersModSystem.Config;
        List<HammockCandidate> candidates = CollectCandidates(world, trunk, request, config);
        BlockSilkHammock anyVariant = BlockSilkHammock.GetVariant(world, BlockFacing.DOWN);
        if (candidates.Count == 0 || anyVariant == null || anyVariant.CollisionBoxes == null || anyVariant.CollisionBoxes.Length == 0)
        {
            candidates.Clear();
            return candidates;
        }
        // Every hammock variant has the same thin floor (see silkhammock.json), so any variant's box will do.
        Cuboidf floor = anyVariant.CollisionBoxes[0];
        RelativeBox floorBox = new RelativeBox(floor.X1, floor.Y1, floor.Z1, floor.X2, floor.Y2, floor.Z2);
        RelativeBox spider = new RelativeBox(spiderBox.X1, spiderBox.Y1, spiderBox.Z1, spiderBox.X2, spiderBox.Y2, spiderBox.Z2);

        JumpLimits limits = config.CreateJumpLimits();
        SpiderJumpWorld jumpWorld = new SpiderJumpWorld(world.BlockAccessor, spiderBox, 0);
        List<StandSpot> allStarts = HammockReachFinder.FindGroundStarts(jumpWorld, trunk.X, trunk.BaseY, trunk.Z,
            config.WildHammockGroundRadius, GroundSearchUpDown, GroundSearchUpDown);
        List<StandSpot> starts = HammockReachFinder.PickSome(allStarts, config.WildHammockGroundStarts, request.Rand);
        HammockReachFinder.MarkReachable(jumpWorld, starts, candidates, floorBox, spider, limits);
        return candidates;
    }

    // Empty cells beside the trunk, from the lowest to the highest allowed height, that the trunk itself can hold a
    // hammock in (Tag = the direction from the cell to the trunk), away from players if the request says so.
    private static List<HammockCandidate> CollectCandidates(IWorldAccessor world, TrunkColumn trunk, SpawnRequest request,
        JumpingSpidersConfig config)
    {
        List<HammockCandidate> candidates = new List<HammockCandidate>();
        IBlockAccessor blockAccessor = world.BlockAccessor;
        BlockPos scratch = new BlockPos(0);
        int top = config.WildHammockMaxHeight;
        if (top > trunk.Height - 1)
        {
            top = trunk.Height - 1;
        }
        for (int height = config.WildHammockMinHeight; height <= top; height++)
        {
            int y = trunk.BaseY + height;
            if (!TreeTrunks.IsTrunkAt(blockAccessor, trunk.X, y, trunk.Z, scratch))
            {
                break;
            }
            for (int side = 0; side < BlockFacing.HORIZONTALS.Length; side++)
            {
                BlockFacing outward = BlockFacing.HORIZONTALS[side];
                BlockFacing towardTrunk = outward.Opposite;
                BlockPos cell = new BlockPos(trunk.X + outward.Normali.X, y, trunk.Z + outward.Normali.Z, 0);
                BlockSilkHammock variant = BlockSilkHammock.GetVariant(world, towardTrunk);
                if (variant == null || !HammockBuilder.IsCellFreeForHammock(world, cell, variant))
                {
                    continue;
                }
                if (!BlockSilkHammock.IsSupportedFrom(blockAccessor, variant, cell, towardTrunk))
                {
                    continue;
                }
                if (request.CheckPlayerDistance && IsNearAnyPlayer(cell, request.PlayerPositions, config.SpawnMinPlayerDistance))
                {
                    continue;
                }
                HammockCandidate candidate = new HammockCandidate();
                candidate.CellX = cell.X;
                candidate.CellY = cell.Y;
                candidate.CellZ = cell.Z;
                candidate.Height = height;
                candidate.Tag = towardTrunk.Index;
                candidates.Add(candidate);
            }
        }
        return candidates;
    }

    /// <summary>True when any of the players is closer than distance blocks to the centre of the cell.</summary>
    public static bool IsNearAnyPlayer(BlockPos cell, List<Vec3d> players, double distance)
    {
        Vec3d center = HammockFinder.CellCenter(cell);
        for (int i = 0; i < players.Count; i++)
        {
            if (players[i].DistanceTo(center) < distance)
            {
                return true;
            }
        }
        return false;
    }

    private static void Shuffle(List<TrunkColumn> list, SpawnRequest request)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = request.Rand.Next(i + 1);
            TrunkColumn temp = list[i];
            list[i] = list[j];
            list[j] = temp;
        }
    }
}
