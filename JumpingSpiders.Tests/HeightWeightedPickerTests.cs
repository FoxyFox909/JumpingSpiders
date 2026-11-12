using System;
using System.Collections.Generic;
using Xunit;

namespace JumpingSpiders.Tests;

public class HeightWeightedPickerTests
{
    private static int[] CountPicks(List<double> heights, double bias, int draws)
    {
        int[] counts = new int[heights.Count];
        Random rand = new Random(11);
        for (int i = 0; i < draws; i++)
        {
            counts[HeightWeightedPicker.PickIndex(heights, bias, rand)]++;
        }
        return counts;
    }

    [Fact]
    public void HigherCandidatesArePickedMoreOften()
    {
        List<double> heights = new List<double>();
        heights.Add(70);
        heights.Add(71);
        heights.Add(72);
        heights.Add(73);
        int draws = 60000;
        int[] counts = CountPicks(heights, 2, draws);

        // Weights (h - 70 + 1)^2 = 1, 4, 9, 16 out of 30.
        double[] expected = new double[] { 1.0 / 30, 4.0 / 30, 9.0 / 30, 16.0 / 30 };
        for (int i = 0; i < heights.Count; i++)
        {
            double share = (double)counts[i] / draws;
            Assert.InRange(share, expected[i] * 0.9, expected[i] * 1.1);
        }
        Assert.True(counts[3] > counts[2] && counts[2] > counts[1] && counts[1] > counts[0]);
    }

    [Fact]
    public void OrderOfCandidatesDoesNotMatter()
    {
        List<double> heights = new List<double>();
        heights.Add(73);
        heights.Add(70);
        int[] counts = CountPicks(heights, 1, 20000);
        // Weights 4 and 1.
        Assert.InRange((double)counts[0] / 20000, 0.75, 0.85);
    }

    [Fact]
    public void ZeroBiasIsUniform()
    {
        List<double> heights = new List<double>();
        heights.Add(0);
        heights.Add(10);
        heights.Add(20);
        int draws = 30000;
        int[] counts = CountPicks(heights, 0, draws);
        for (int i = 0; i < heights.Count; i++)
        {
            Assert.InRange((double)counts[i] / draws, 0.30, 0.37);
        }
    }

    [Fact]
    public void EmptyListGivesMinusOne()
    {
        Assert.Equal(-1, HeightWeightedPicker.PickIndex(new List<double>(), 2, new Random(1)));
        Assert.Equal(-1, HeightWeightedPicker.PickIndex(null, 2, new Random(1)));
    }

    [Fact]
    public void SingleCandidateIsAlwaysPicked()
    {
        List<double> heights = new List<double>();
        heights.Add(5);
        Assert.Equal(0, HeightWeightedPicker.PickIndex(heights, 3, new Random(1)));
    }
}
