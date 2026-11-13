using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace JumpingSpiders;

/// <summary>
/// Recognises grown tree trunks and finds them in a chunk column, for wild hammock placement.
///
/// Trunks are not one block class in Vintage Story: ordinary logs are BlockLog, but resin logs (pine, acacia),
/// narrow cypress logs and redwood log sections are other classes. What all grown trunk blocks share is wood material
/// plus the "treeFellingGroupCode" attribute the axe uses to fell a whole tree. Logs placed by players have no such
/// attribute, so log cabins are never mistaken for trees. (Whether a trunk can actually hold a hammock is asked
/// separately: cypress logs, for example, have no solid sides, so nothing attaches to them.)
///
/// Finding trunks is cheap: the world generator grows every tree from the ground surface it generated, and the
/// map chunk remembers that surface height for every column ("WorldGenTerrainHeightMap", never changed later).
/// So each of the 32 x 32 columns needs only a look at the one or two blocks just above that height.
/// </summary>
public static class TreeTrunks
{
    /// <summary>Block attribute that only grown (tree) logs have; the axe's tree felling reads it too.</summary>
    public const string TreeFellingAttribute = "treeFellingGroupCode";

    /// <summary>How many blocks above the generated surface height a trunk base may start (uneven ground).</summary>
    public const int BaseSearchHeight = 2;

    /// <summary>Blocks per side of a chunk column.</summary>
    public const int ChunkSize = 32;

    /// <summary>True for a grown tree trunk block of any kind.</summary>
    public static bool IsTrunk(Block block)
    {
        if (block == null || block.Id == 0 || block.BlockMaterial != EnumBlockMaterial.Wood)
        {
            return false;
        }
        return block.Attributes != null && block.Attributes[TreeFellingAttribute].Exists;
    }

    /// <summary>
    /// Every trunk standing in chunk column (chunkX, chunkZ), lowest block within BaseSearchHeight blocks above the
    /// generated surface. Height is counted up to maxHeight (no need to know more). Reads about 2 blocks per column
    /// plus the trunks' heights. The column must be loaded (unloaded blocks read as air, so it finds nothing).
    /// </summary>
    public static List<TrunkColumn> FindInChunk(IBlockAccessor blockAccessor, IMapChunk mapChunk, int chunkX, int chunkZ, int maxHeight)
    {
        List<TrunkColumn> trunks = new List<TrunkColumn>();
        if (mapChunk == null || mapChunk.WorldGenTerrainHeightMap == null)
        {
            return trunks;
        }
        ushort[] heightMap = mapChunk.WorldGenTerrainHeightMap;
        BlockPos pos = new BlockPos(0);
        for (int localX = 0; localX < ChunkSize; localX++)
        {
            for (int localZ = 0; localZ < ChunkSize; localZ++)
            {
                int x = chunkX * ChunkSize + localX;
                int z = chunkZ * ChunkSize + localZ;
                int surfaceY = heightMap[localZ * ChunkSize + localX];
                for (int y = surfaceY + 1; y <= surfaceY + BaseSearchHeight; y++)
                {
                    pos.Set(x, y, z);
                    if (!IsTrunk(blockAccessor.GetBlock(pos)))
                    {
                        continue;
                    }
                    // A trunk block with another one below is not the base; that column was handled at its base.
                    pos.Set(x, y - 1, z);
                    if (IsTrunk(blockAccessor.GetBlock(pos)))
                    {
                        break;
                    }
                    TrunkColumn trunk = new TrunkColumn();
                    trunk.X = x;
                    trunk.Z = z;
                    trunk.BaseY = y;
                    trunk.Height = CountHeight(blockAccessor, x, y, z, maxHeight, pos);
                    trunks.Add(trunk);
                    break;
                }
            }
        }
        return trunks;
    }

    /// <summary>True when the block at (x, y, z) is a trunk block.</summary>
    public static bool IsTrunkAt(IBlockAccessor blockAccessor, int x, int y, int z, BlockPos scratch)
    {
        scratch.Set(x, y, z);
        return IsTrunk(blockAccessor.GetBlock(scratch));
    }

    // Trunk blocks from baseY upwards, at most maxHeight.
    private static int CountHeight(IBlockAccessor blockAccessor, int x, int baseY, int z, int maxHeight, BlockPos scratch)
    {
        int height = 0;
        while (height < maxHeight && IsTrunkAt(blockAccessor, x, baseY + height, z, scratch))
        {
            height++;
        }
        return height;
    }
}
