using System;
using System.Collections.Generic;

namespace JumpingSpiders;

/// <summary>
/// Weighted random choice that favours higher candidates. Spiders like high places: exploration jumps and
/// hammock building use this to pick among possible spots.
///
/// Each candidate gets the weight (height above the lowest candidate + 1) ^ bias, so the lowest candidate has
/// weight 1. With bias 0 every candidate is equally likely; with bias 2 a spot 3 blocks above the lowest one is
/// 16 times as likely as the lowest.
/// </summary>
public static class HeightWeightedPicker
{
    /// <summary>
    /// Picks one index into heights (any unit, usually feet Y in blocks). Returns -1 when the list is empty.
    /// </summary>
    public static int PickIndex(List<double> heights, double bias, Random rand)
    {
        if (heights == null || heights.Count == 0)
        {
            return -1;
        }

        double lowest = heights[0];
        for (int i = 1; i < heights.Count; i++)
        {
            if (heights[i] < lowest)
            {
                lowest = heights[i];
            }
        }

        double total = 0;
        for (int i = 0; i < heights.Count; i++)
        {
            total += WeightOf(heights[i], lowest, bias);
        }

        double roll = rand.NextDouble() * total;
        double sum = 0;
        for (int i = 0; i < heights.Count; i++)
        {
            sum += WeightOf(heights[i], lowest, bias);
            if (roll < sum)
            {
                return i;
            }
        }

        // Only reached through rounding when roll is extremely close to total.
        return heights.Count - 1;
    }

    /// <summary>The weight of one candidate at the given height, when the lowest candidate is at lowest.</summary>
    public static double WeightOf(double height, double lowest, double bias)
    {
        return Math.Pow(height - lowest + 1, bias);
    }
}
