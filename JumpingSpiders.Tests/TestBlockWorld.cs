using System;
using System.Collections.Generic;

namespace JumpingSpiders.Tests;

/// <summary>
/// A tiny block world for planner tests. Each block fills its cell from the bottom up to a height
/// (1 = full block, 0.5 = slab). The spider is a box HalfWidth wide each way and Height tall, feet at the bottom centre.
/// </summary>
public class TestBlockWorld : IJumpWorld
{
    public double HalfWidth = 0.3;
    public double Height = 0.5;

    private readonly Dictionary<long, double> blockTops = new Dictionary<long, double>();

    public void SetBlock(int x, int y, int z)
    {
        SetBlock(x, y, z, 1.0);
    }

    public void SetBlock(int x, int y, int z, double top)
    {
        blockTops[Key(x, y, z)] = top;
    }

    /// <summary>Fills every cell of the box (inclusive corners) with full blocks.</summary>
    public void Fill(int x1, int y1, int z1, int x2, int y2, int z2)
    {
        for (int x = x1; x <= x2; x++)
        {
            for (int y = y1; y <= y2; y++)
            {
                for (int z = z1; z <= z2; z++)
                {
                    SetBlock(x, y, z);
                }
            }
        }
    }

    public bool IsFree(double x, double y, double z)
    {
        double x1 = x - HalfWidth;
        double x2 = x + HalfWidth;
        double y1 = y;
        double y2 = y + Height;
        double z1 = z - HalfWidth;
        double z2 = z + HalfWidth;
        for (int bx = (int)Math.Floor(x1); bx <= (int)Math.Floor(x2); bx++)
        {
            for (int by = (int)Math.Floor(y1) - 1; by <= (int)Math.Floor(y2); by++)
            {
                for (int bz = (int)Math.Floor(z1); bz <= (int)Math.Floor(z2); bz++)
                {
                    double top;
                    if (!blockTops.TryGetValue(Key(bx, by, bz), out top))
                    {
                        continue;
                    }
                    bool overlapX = x1 < bx + 1 - 1e-9 && x2 > bx + 1e-9;
                    bool overlapY = y1 < by + top - 1e-9 && y2 > by + 1e-9;
                    bool overlapZ = z1 < bz + 1 - 1e-9 && z2 > bz + 1e-9;
                    if (overlapX && overlapY && overlapZ)
                    {
                        return false;
                    }
                }
            }
        }
        return true;
    }

    public double GetStandHeight(int x, int y, int z)
    {
        double top;
        if (blockTops.TryGetValue(Key(x, y, z), out top))
        {
            return y + top;
        }
        return y;
    }

    public bool CanStandAt(int x, int y, int z)
    {
        double standY = GetStandHeight(x, y, z);
        if (standY >= y + 1)
        {
            return false;
        }
        if (!IsFree(x + 0.5, standY, z + 0.5))
        {
            return false;
        }
        return !IsFree(x + 0.5, standY - 0.05, z + 0.5);
    }

    private static long Key(int x, int y, int z)
    {
        return ((long)(x + 100000) * 200000 + (y + 100000)) * 200000 + (z + 100000);
    }
}
