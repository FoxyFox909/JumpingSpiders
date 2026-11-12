using System.Collections.Generic;
using Xunit;

namespace JumpingSpiders.Tests;

public class SpiderChunkSearchTests
{
    [Fact]
    public void FindsExactlyTheSpiderChunksInTheSquare()
    {
        List<ChunkCoord> found = SpiderChunkSearch.FindAround(777, 100, -40, 6, 0.1);

        int expected = 0;
        for (int x = 94; x <= 106; x++)
        {
            for (int z = -46; z <= -34; z++)
            {
                if (SpiderChunks.IsSpiderChunk(777, x, z, 0.1))
                {
                    expected++;
                }
            }
        }
        Assert.Equal(expected, found.Count);
        for (int i = 0; i < found.Count; i++)
        {
            Assert.True(SpiderChunks.IsSpiderChunk(777, found[i].X, found[i].Z, 0.1));
            Assert.InRange(found[i].X, 94, 106);
            Assert.InRange(found[i].Z, -46, -34);
        }
    }

    [Fact]
    public void NearestComesFirst()
    {
        List<ChunkCoord> found = SpiderChunkSearch.FindAround(5, 0, 0, 10, 0.2);
        Assert.NotEmpty(found);
        for (int i = 1; i < found.Count; i++)
        {
            Assert.True(found[i - 1].DistanceSq <= found[i].DistanceSq);
        }
        for (int i = 0; i < found.Count; i++)
        {
            Assert.Equal(found[i].X * found[i].X + found[i].Z * found[i].Z, found[i].DistanceSq);
        }
    }

    [Fact]
    public void RadiusZeroLooksOnlyAtTheCentre()
    {
        Assert.Single(SpiderChunkSearch.FindAround(1, 3, 3, 0, 1.0));
        Assert.Empty(SpiderChunkSearch.FindAround(1, 3, 3, 0, 0.0));
    }

    [Fact]
    public void SameInputsGiveSameOrder()
    {
        List<ChunkCoord> a = SpiderChunkSearch.FindAround(9, 2, 2, 8, 0.3);
        List<ChunkCoord> b = SpiderChunkSearch.FindAround(9, 2, 2, 8, 0.3);
        Assert.Equal(a.Count, b.Count);
        for (int i = 0; i < a.Count; i++)
        {
            Assert.Equal(a[i].X, b[i].X);
            Assert.Equal(a[i].Z, b[i].Z);
        }
    }
}
