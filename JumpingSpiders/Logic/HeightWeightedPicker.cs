using System;
using System.Collections.Generic;

namespace JumpingSpiders;

/// <summary>
/// Weighted random choice that favours higher candidates. Spiders like high places: exploration and flee jumps
/// (JumpPlanner.PlanJumpToRandomSpot) and wild hammock placement on tree trunks use this to pick among possible spots.
///
/// Each candidate gets the weight (height above the lowest candidate + 1) ^ bias, so the lowest candidate has
/// weight 1. With bias 0 every candidate is equally likely; with bias 2 a spot 3 blocks above the lowest one is
/// 16 times as likely as the lowest.
///
/// A caller that picks again and again from the same candidates (dropping the ones that did not work) computes the
/// weights once (Weights), picks with PickIndexByWeight and drops a candidate with RemoveAt, which keeps the two
/// lists in step. That gives exactly the same picks as PickIndex on the heights each time, without the powers.
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
        return PickIndexByWeight(Weights(heights, bias), rand);
    }

    /// <summary>The weight of every candidate (see the class summary), in the order of heights.</summary>
    public static List<double> Weights(List<double> heights, double bias)
    {
        List<double> weights = new List<double>(heights.Count);
        double lowest = Lowest(heights);
        for (int i = 0; i < heights.Count; i++)
        {
            weights.Add(WeightOf(heights[i], lowest, bias));
        }
        return weights;
    }

    /// <summary>Picks one index into weights, each as likely as its weight. Returns -1 when the list is empty.</summary>
    public static int PickIndexByWeight(List<double> weights, Random rand)
    {
        if (weights == null || weights.Count == 0)
        {
            return -1;
        }

        double total = 0;
        for (int i = 0; i < weights.Count; i++)
        {
            total += weights[i];
        }

        double roll = rand.NextDouble() * total;
        double sum = 0;
        for (int i = 0; i < weights.Count; i++)
        {
            sum += weights[i];
            if (roll < sum)
            {
                return i;
            }
        }

        // Only reached through rounding when roll is extremely close to total.
        return weights.Count - 1;
    }

    /// <summary>
    /// Drops candidate index from both lists. Weights count from the lowest candidate, so when the lowest one goes the
    /// rest are weighed again.
    /// </summary>
    public static void RemoveAt(List<double> heights, List<double> weights, int index, double bias)
    {
        double lowestBefore = Lowest(heights);
        heights.RemoveAt(index);
        weights.RemoveAt(index);
        if (heights.Count == 0)
        {
            return;
        }
        double lowest = Lowest(heights);
        if (lowest == lowestBefore)
        {
            return;
        }
        for (int i = 0; i < heights.Count; i++)
        {
            weights[i] = WeightOf(heights[i], lowest, bias);
        }
    }

    /// <summary>The weight of one candidate at the given height, when the lowest candidate is at lowest.</summary>
    public static double WeightOf(double height, double lowest, double bias)
    {
        return Math.Pow(height - lowest + 1, bias);
    }

    private static double Lowest(List<double> heights)
    {
        double lowest = heights[0];
        for (int i = 1; i < heights.Count; i++)
        {
            if (heights[i] < lowest)
            {
                lowest = heights[i];
            }
        }
        return lowest;
    }
}
