using System;
using System.Collections.Generic;

namespace JumpingSpiders;

/// <summary>
/// Answers "which of these hammock spots on a tree trunk could a spider on the ground near the tree jump into?"
/// for wild hammock placement. Spiders cannot climb, so a wild hammock is only useful where the jump planner's
/// rules (at most two jumps, the second from a perch such as a branch or branchy leaves) can get a spider in.
///
/// How it works, for one tree:
/// 1. FindGroundStarts: the cells around the trunk where a spider stands on the ground. The caller keeps a few.
/// 2. MarkReachable: for each hammock spot, pretend the hammock is already there (JumpWorldWithExtraBox adds its
///    thin floor) and try one jump from each ground start.
/// 3. For the spots still unreached, FindPerches collects, once per tree rather than once per spot, the higher spots
///    the spider can jump up to from a ground start that are within one jump of one of those spots; then one jump
///    from each perch is tried.
/// Almost all the cost is the jump arc checks; this order keeps their number down.
///
/// The jump checks are JumpPlanner.TryJump and JumpPlanner.IsArcClear, the same ones the spider's own jumps use.
/// Pure logic on IJumpWorld, so the unit tests run it on hand-made block worlds.
/// </summary>
public static class HammockReachFinder
{
    /// <summary>
    /// A perch must lift the spider's feet at least this many blocks above the ground start. Spots level with or
    /// below the ground do not help to reach a hammock higher up, and skipping them saves most of the work.
    /// </summary>
    public const double MinPerchRise = 1;

    /// <summary>
    /// Ground spots around a trunk: for every column whose distance from the trunk column is 1..radius blocks
    /// (on the larger axis), the highest cell between baseY + searchUp and baseY - searchDown that the spider can
    /// stand in. baseY is the trunk's lowest block, which sits on the ground, so flat ground gives cells at baseY.
    /// </summary>
    public static List<StandSpot> FindGroundStarts(IJumpWorld world, int trunkX, int baseY, int trunkZ,
        int radius, int searchUp, int searchDown)
    {
        List<StandSpot> starts = new List<StandSpot>();
        for (int dx = -radius; dx <= radius; dx++)
        {
            for (int dz = -radius; dz <= radius; dz++)
            {
                if (dx == 0 && dz == 0)
                {
                    continue;
                }
                int x = trunkX + dx;
                int z = trunkZ + dz;
                for (int y = baseY + searchUp; y >= baseY - searchDown; y--)
                {
                    if (!world.CanStandAt(x, y, z))
                    {
                        continue;
                    }
                    starts.Add(MakeSpot(world, x, y, z));
                    break;
                }
            }
        }
        return starts;
    }

    /// <summary>Up to count spots taken at random from spots (the list itself is not changed).</summary>
    public static List<StandSpot> PickSome(List<StandSpot> spots, int count, Random rand)
    {
        List<StandSpot> copy = new List<StandSpot>(spots);
        for (int i = copy.Count - 1; i > 0; i--)
        {
            int j = rand.Next(i + 1);
            StandSpot temp = copy[i];
            copy[i] = copy[j];
            copy[j] = temp;
        }
        if (copy.Count > count)
        {
            copy.RemoveRange(count, copy.Count - count);
        }
        return copy;
    }

    /// <summary>
    /// Sets Reachable and Jumps on every candidate. floorBox is the hammock's collision box relative to its cell
    /// corner (its thin floor), spiderBox the spider's collision box relative to its feet.
    /// First every candidate is tried with one jump from a ground start. Only if some are left, and
    /// limits.MaxPlannedJumps is at least 2, are perches searched (FindPerches) and tried; longer routes are never tried.
    /// </summary>
    public static void MarkReachable(IJumpWorld world, List<StandSpot> starts, List<HammockCandidate> candidates,
        RelativeBox floorBox, RelativeBox spiderBox, JumpLimits limits)
    {
        List<HammockCandidate> left = new List<HammockCandidate>();
        for (int i = 0; i < candidates.Count; i++)
        {
            HammockCandidate candidate = candidates[i];
            candidate.Reachable = false;
            candidate.Jumps = 0;
            JumpWorldWithExtraBox future = WithHammock(world, candidate, floorBox, spiderBox);
            if (!future.CanStandAt(candidate.CellX, candidate.CellY, candidate.CellZ))
            {
                continue;
            }
            if (CanJumpFromAny(future, starts, candidate, limits, false))
            {
                candidate.Reachable = true;
                candidate.Jumps = 1;
                continue;
            }
            left.Add(candidate);
        }
        if (left.Count == 0 || limits.MaxPlannedJumps < 2)
        {
            return;
        }

        List<StandSpot> perches = FindPerches(world, starts, left, floorBox.Y2, limits);
        if (perches.Count == 0)
        {
            return;
        }
        for (int i = 0; i < left.Count; i++)
        {
            HammockCandidate candidate = left[i];
            JumpWorldWithExtraBox future = WithHammock(world, candidate, floorBox, spiderBox);
            if (CanJumpFromAny(future, perches, candidate, limits, true))
            {
                candidate.Reachable = true;
                candidate.Jumps = 2;
            }
        }
    }

    /// <summary>
    /// Every spot the spider can jump up to from one of the ground starts, at least MinPerchRise above that start,
    /// that is roughly within one jump of at least one of the targets (hammock cells whose floor top is floorTop
    /// above the cell bottom). Each perch remembers the jump that gets there. A perch reachable from several starts
    /// is listed once. Filtering by the targets first keeps the expensive checks to the few cells that could help.
    /// </summary>
    public static List<StandSpot> FindPerches(IJumpWorld world, List<StandSpot> starts, List<HammockCandidate> targets,
        double floorTop, JumpLimits limits)
    {
        List<StandSpot> perches = new List<StandSpot>();
        HashSet<long> found = new HashSet<long>();
        HashSet<long> notStandable = new HashSet<long>();
        int reach = (int)Math.Ceiling(limits.MaxJumpHorizontal);
        int up = (int)Math.Ceiling(limits.MaxJumpUp);

        for (int i = 0; i < starts.Count; i++)
        {
            StandSpot start = starts[i];
            for (int x = start.CellX - reach; x <= start.CellX + reach; x++)
            {
                for (int z = start.CellZ - reach; z <= start.CellZ + reach; z++)
                {
                    double feetX = x + 0.5;
                    double feetZ = z + 0.5;
                    if (!WithinHorizontal(feetX, feetZ, start.FeetX, start.FeetZ, limits.MaxJumpHorizontal))
                    {
                        continue;
                    }
                    for (int y = start.CellY + 1; y <= start.CellY + up; y++)
                    {
                        long key = CellKey(x, y, z);
                        if (found.Contains(key) || notStandable.Contains(key))
                        {
                            continue;
                        }
                        if (!CouldReachAnyTarget(feetX, y, feetZ, targets, floorTop, limits))
                        {
                            continue;
                        }
                        if (!world.CanStandAt(x, y, z))
                        {
                            notStandable.Add(key);
                            continue;
                        }
                        StandSpot perch = MakeSpot(world, x, y, z);
                        if (perch.FeetY < start.FeetY + MinPerchRise || perch.FeetY - start.FeetY > limits.MaxJumpUp)
                        {
                            continue;
                        }
                        JumpStep step = JumpPlanner.TryJump(world, start.FeetX, start.FeetY, start.FeetZ,
                            perch.FeetX, perch.FeetY, perch.FeetZ, limits);
                        if (step == null)
                        {
                            continue;
                        }
                        perch.StepFromGround = step;
                        found.Add(key);
                        perches.Add(perch);
                    }
                }
            }
        }
        return perches;
    }

    // A cheap test on distances only: could a spider in cell y (feet between y and y + 1) jump to any target?
    private static bool CouldReachAnyTarget(double feetX, int cellY, double feetZ, List<HammockCandidate> targets,
        double floorTop, JumpLimits limits)
    {
        for (int i = 0; i < targets.Count; i++)
        {
            HammockCandidate target = targets[i];
            if (!WithinHorizontal(feetX, feetZ, target.CellX + 0.5, target.CellZ + 0.5, limits.MaxJumpHorizontal))
            {
                continue;
            }
            double targetFeetY = target.CellY + floorTop;
            if (targetFeetY - cellY > limits.MaxJumpUp)
            {
                continue;
            }
            if (targetFeetY - (cellY + 1) < -limits.MaxJumpDown)
            {
                continue;
            }
            return true;
        }
        return false;
    }

    // True when one of the spots can jump into the candidate's hammock. For perches the jump up to the perch is
    // checked again in the world with the hammock, in case the new hammock is in the way of that first jump.
    private static bool CanJumpFromAny(JumpWorldWithExtraBox world, List<StandSpot> spots, HammockCandidate candidate,
        JumpLimits limits, bool checkStepFromGround)
    {
        double goalX = candidate.CellX + 0.5;
        double goalY = world.GetStandHeight(candidate.CellX, candidate.CellY, candidate.CellZ);
        double goalZ = candidate.CellZ + 0.5;
        for (int i = 0; i < spots.Count; i++)
        {
            StandSpot spot = spots[i];
            if (!WithinHorizontal(spot.FeetX, spot.FeetZ, goalX, goalZ, limits.MaxJumpHorizontal))
            {
                continue;
            }
            double dy = goalY - spot.FeetY;
            if (dy > limits.MaxJumpUp || dy < -limits.MaxJumpDown)
            {
                continue;
            }
            JumpStep step = JumpPlanner.TryJump(world, spot.FeetX, spot.FeetY, spot.FeetZ, goalX, goalY, goalZ, limits);
            if (step == null)
            {
                continue;
            }
            if (checkStepFromGround && spot.StepFromGround != null && !JumpPlanner.IsArcClear(world, spot.StepFromGround))
            {
                continue;
            }
            return true;
        }
        return false;
    }

    // The world as it would be with the candidate's hammock in place.
    private static JumpWorldWithExtraBox WithHammock(IJumpWorld world, HammockCandidate candidate, RelativeBox floorBox,
        RelativeBox spiderBox)
    {
        return new JumpWorldWithExtraBox(world, candidate.CellX, candidate.CellY, candidate.CellZ,
            floorBox.X1, floorBox.Y1, floorBox.Z1, floorBox.X2, floorBox.Y2, floorBox.Z2,
            spiderBox.X1, spiderBox.Y1, spiderBox.Z1, spiderBox.X2, spiderBox.Y2, spiderBox.Z2);
    }

    private static StandSpot MakeSpot(IJumpWorld world, int x, int y, int z)
    {
        StandSpot spot = new StandSpot();
        spot.CellX = x;
        spot.CellY = y;
        spot.CellZ = z;
        spot.FeetX = x + 0.5;
        spot.FeetY = world.GetStandHeight(x, y, z);
        spot.FeetZ = z + 0.5;
        return spot;
    }

    private static bool WithinHorizontal(double ax, double az, double bx, double bz, double range)
    {
        double dx = ax - bx;
        double dz = az - bz;
        return dx * dx + dz * dz <= range * range;
    }

    // Packs a block cell into one number for the "seen" sets (21 bits per axis, as in JumpPlanner).
    private static long CellKey(int x, int y, int z)
    {
        long kx = x & 0x1FFFFF;
        long ky = y & 0x1FFFFF;
        long kz = z & 0x1FFFFF;
        return (kx << 42) | (ky << 21) | kz;
    }
}
