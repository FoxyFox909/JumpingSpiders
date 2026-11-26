using System;
using System.Collections.Generic;
using Xunit;

namespace JumpingSpiders.Tests;

public class RandomOrderTests
{
    [Fact]
    public void EveryPositionComesOnce()
    {
        for (int count = 0; count < 12; count++)
        {
            int[] order = RandomOrder.Shuffled(count, new Random(count));
            Assert.Equal(count, order.Length);
            HashSet<int> seen = new HashSet<int>();
            for (int i = 0; i < order.Length; i++)
            {
                Assert.InRange(order[i], 0, count - 1);
                Assert.True(seen.Add(order[i]));
            }
        }
    }

    [Fact]
    public void EveryOrderOfThreeIsAboutEquallyLikely()
    {
        Random rand = new Random(5);
        Dictionary<string, int> counts = new Dictionary<string, int>();
        int draws = 60000;
        for (int i = 0; i < draws; i++)
        {
            int[] order = RandomOrder.Shuffled(3, rand);
            string key = order[0] + "" + order[1] + order[2];
            int count;
            counts.TryGetValue(key, out count);
            counts[key] = count + 1;
        }
        Assert.Equal(6, counts.Count);
        foreach (KeyValuePair<string, int> entry in counts)
        {
            Assert.InRange((double)entry.Value / draws, 0.15, 0.18);
        }
    }
}
