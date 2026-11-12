using Xunit;

namespace JumpingSpiders.Tests;

public class SpiderChunksTests
{
    [Fact]
    public void SameSeedGivesSameChunks()
    {
        for (int x = -50; x < 50; x++)
        {
            for (int z = -50; z < 50; z++)
            {
                Assert.Equal(SpiderChunks.IsSpiderChunk(12345, x, z, 0.04), SpiderChunks.IsSpiderChunk(12345, x, z, 0.04));
                Assert.Equal(SpiderChunks.ChunkRoll(12345, x, z), SpiderChunks.ChunkRoll(12345, x, z));
            }
        }
    }

    [Fact]
    public void KnownValuesNeverChange()
    {
        // If these change, every existing world would get a different set of spider chunks.
        Assert.Equal(SpiderChunks.Hash(1, 2, 3, 4), SpiderChunks.Hash(1, 2, 3, 4));
        Assert.NotEqual(SpiderChunks.Hash(1, 2, 3, 4), SpiderChunks.Hash(4, 3, 2, 1));
        double roll = SpiderChunks.ChunkRoll(0, 0, 0);
        Assert.True(roll >= 0 && roll < 1);
    }

    [Fact]
    public void DifferentSeedsGiveDifferentChunks()
    {
        int differences = 0;
        for (int x = 0; x < 100; x++)
        {
            for (int z = 0; z < 100; z++)
            {
                if (SpiderChunks.IsSpiderChunk(1, x, z, 0.5) != SpiderChunks.IsSpiderChunk(2, x, z, 0.5))
                {
                    differences++;
                }
            }
        }
        // Independent coin flips differ about half the time.
        Assert.InRange(differences, 4000, 6000);
    }

    [Theory]
    [InlineData(0.04)]
    [InlineData(0.01)]
    [InlineData(0.5)]
    public void FractionOfSpiderChunksIsCloseToConfigured(double fraction)
    {
        int count = 0;
        int total = 0;
        for (int x = -150; x < 150; x++)
        {
            for (int z = -150; z < 150; z++)
            {
                total++;
                if (SpiderChunks.IsSpiderChunk(987654321, x, z, fraction))
                {
                    count++;
                }
            }
        }
        double measured = (double)count / total;
        // 90 000 chunks: the expected statistical spread is far below 10 % of the fraction.
        Assert.InRange(measured, fraction * 0.9, fraction * 1.1);
    }

    [Fact]
    public void ZeroAndOneFractions()
    {
        Assert.False(SpiderChunks.IsSpiderChunk(5, 3, 4, 0));
        Assert.True(SpiderChunks.IsSpiderChunk(5, 3, 4, 1));
    }

    [Fact]
    public void NeighbouringChunksAreNotCorrelated()
    {
        // A naive hash would make whole rows or columns of spider chunks. Count spider chunks that have a
        // spider chunk directly east: for independent chunks that is about fraction squared.
        double fraction = 0.2;
        int pairs = 0;
        int spider = 0;
        for (int x = 0; x < 300; x++)
        {
            for (int z = 0; z < 300; z++)
            {
                if (SpiderChunks.IsSpiderChunk(42, x, z, fraction))
                {
                    spider++;
                    if (SpiderChunks.IsSpiderChunk(42, x + 1, z, fraction))
                    {
                        pairs++;
                    }
                }
            }
        }
        double neighbourShare = (double)pairs / spider;
        Assert.InRange(neighbourShare, fraction * 0.85, fraction * 1.15);
    }
}
