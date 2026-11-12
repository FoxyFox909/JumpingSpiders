namespace JumpingSpiders;

/// <summary>
/// One chunk column (32 x 32 blocks, chunk X = block X / 32) and its squared distance, in chunks, from the point a
/// search started at. Plain data, made by SpiderChunkSearch.
/// </summary>
public class ChunkCoord
{
    public int X;
    public int Z;

    /// <summary>Squared distance from the search centre, in chunks (0 for the centre chunk itself).</summary>
    public int DistanceSq;
}
