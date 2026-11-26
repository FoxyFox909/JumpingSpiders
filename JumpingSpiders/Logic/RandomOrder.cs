using System;

namespace JumpingSpiders;

/// <summary>
/// A random order to go through a list in: the planner's spots, spawn candidates, tree trunks and so on. Pure C#,
/// covered by unit tests.
///
/// Shuffled gives the positions 0..count-1 in random order (a Fisher-Yates shuffle); the caller then reads its list
/// as list[order[i]]. Shuffling positions instead of the list itself lets one method serve lists of any type.
/// </summary>
public static class RandomOrder
{
    /// <summary>The numbers 0 to count - 1, each once, in random order. Uses count - 1 draws from rand.</summary>
    public static int[] Shuffled(int count, Random rand)
    {
        int[] order = new int[count];
        for (int i = 0; i < count; i++)
        {
            order[i] = i;
        }
        for (int i = count - 1; i > 0; i--)
        {
            int j = rand.Next(i + 1);
            int temp = order[i];
            order[i] = order[j];
            order[j] = temp;
        }
        return order;
    }
}
