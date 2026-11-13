namespace JumpingSpiders;

/// <summary>
/// Decides which chunk columns are "spider chunks", the only places where wild spiders live.
///
/// Vintage Story splits the world into chunk columns of 32 x 32 blocks (chunk X = block X / 32, same for Z).
/// The answer must be the same every time the server starts, for the same world, so it is a hash of the world
/// seed and the chunk coordinates rather than a random roll. (Do not use string.GetHashCode for things like this:
/// in .NET it changes on every start.)
///
/// The hash is MurmurHash3 (32 bit), the same family the game uses in GameMath.MurmurHash3, written out here so
/// that this file needs nothing from the game.
/// </summary>
public static class SpiderChunks
{
    /// <summary>A constant mixed into the hash so that our chunk choice is not correlated with other mods' choices.</summary>
    public const int Salt = 0x5A1D3E;

    /// <summary>True when this chunk column is a spider chunk. fraction (0..1) is SpiderChunkFraction from the config.</summary>
    public static bool IsSpiderChunk(int worldSeed, int chunkX, int chunkZ, double fraction)
    {
        return ChunkRoll(worldSeed, chunkX, chunkZ) < fraction;
    }

    /// <summary>
    /// A fixed pseudo-random number in [0, 1) for this chunk column and world. Also usable for other per-chunk
    /// decisions that must stay the same (pass a different salt to Hash for those).
    /// </summary>
    public static double ChunkRoll(int worldSeed, int chunkX, int chunkZ)
    {
        uint hash = Hash(worldSeed, Salt, chunkX, chunkZ);
        // Use the top 24 bits: plenty of resolution, and exactly representable as a double.
        uint top = hash >> 8;
        return top / 16777216.0;
    }

    /// <summary>MurmurHash3 (x86, 32 bit) of four integers.</summary>
    public static uint Hash(int a, int b, int c, int d)
    {
        uint h = 0;
        h = MixIn(h, (uint)a);
        h = MixIn(h, (uint)b);
        h = MixIn(h, (uint)c);
        h = MixIn(h, (uint)d);

        // Finalisation: length in bytes, then the avalanche mix.
        h = h ^ 16u;
        h = h ^ (h >> 16);
        h = unchecked(h * 0x85ebca6bu);
        h = h ^ (h >> 13);
        h = unchecked(h * 0xc2b2ae35u);
        h = h ^ (h >> 16);
        return h;
    }

    private static uint MixIn(uint h, uint k)
    {
        k = unchecked(k * 0xcc9e2d51u);
        k = RotateLeft(k, 15);
        k = unchecked(k * 0x1b873593u);
        h = h ^ k;
        h = RotateLeft(h, 13);
        h = unchecked(h * 5u + 0xe6546b64u);
        return h;
    }

    private static uint RotateLeft(uint x, int bits)
    {
        return (x << bits) | (x >> (32 - bits));
    }
}
