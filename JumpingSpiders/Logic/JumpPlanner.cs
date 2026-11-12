using System;
using System.Collections.Generic;

namespace JumpingSpiders;

/// <summary>
/// Plans how a spider gets somewhere by jumping: either one jump straight to the goal, or a chain of jumps
/// through intermediate perches (at most JumpLimits.MaxPlannedJumps jumps). The game's own pathfinder cannot do
/// this: it only plans walking with steps of one block.
///
/// The AI tasks call this on the server when they decide to jump, then hand the first JumpStep to JumpController.
/// It only talks to the world through IJumpWorld, so the unit tests can run it on small hand-made block worlds.
///
/// Cost: one search level looks at every cell within one jump of each spot found so far (13 x 13 x 11 cells for
/// the default limits), so call it when a decision is made, not every tick.
/// </summary>
public static class JumpPlanner
{
    /// <summary>How far below the feet (blocks) we probe to decide that there is something to stand on.</summary>
    public const double FootingProbeDepth = 0.05;

    /// <summary>
    /// The engine moves an entity vertically before horizontally in each physics tick, so on its last tick in
    /// the air the spider comes down at the position of the tick before. There must be footing under it there.
    /// We check a little further back than one tick of travel (this factor times one tick) to allow for the
    /// small steering lag of JumpController.
    /// </summary>
    public const double LastTickFootingMargin = 1.25;

    /// <summary>Feet heights this close below a whole number still count as that block cell (rounding errors).</summary>
    public const double CellHeightEpsilon = 0.01;

    /// <summary>PlanJumpToRandomSpot gives up after trying this many candidate spots whose arc turned out blocked.</summary>
    public const int RandomSpotMaxAttempts = 32;

    /// <summary>
    /// Finds a list of jumps from the start feet position to block cell goal (the spider ends standing in it).
    /// Returns an empty list when the spider already stands in the goal cell, and null when no plan with at most
    /// limits.MaxPlannedJumps jumps exists. rand picks among equally short plans, so spiders do not all take the
    /// same perch.
    /// </summary>
    public static List<JumpStep> PlanJumps(IJumpWorld world, double startX, double startY, double startZ,
        int goalX, int goalY, int goalZ, JumpLimits limits, Random rand)
    {
        int startCellX = CellOfX(startX);
        int startCellY = CellOfFeetY(startY);
        int startCellZ = CellOfX(startZ);
        if (startCellX == goalX && startCellY == goalY && startCellZ == goalZ)
        {
            return new List<JumpStep>();
        }
        if (!world.CanStandAt(goalX, goalY, goalZ))
        {
            return null;
        }

        double goalFeetX = goalX + 0.5;
        double goalFeetY = world.GetStandHeight(goalX, goalY, goalZ);
        double goalFeetZ = goalZ + 0.5;

        JumpPlanNode start = new JumpPlanNode();
        start.CellX = startCellX;
        start.CellY = startCellY;
        start.CellZ = startCellZ;
        start.FeetX = startX;
        start.FeetY = startY;
        start.FeetZ = startZ;

        HashSet<long> visited = new HashSet<long>();
        visited.Add(CellKey(startCellX, startCellY, startCellZ));
        visited.Add(CellKey(goalX, goalY, goalZ));

        List<JumpPlanNode> frontier = new List<JumpPlanNode>();
        frontier.Add(start);

        for (int depth = 1; depth <= limits.MaxPlannedJumps; depth++)
        {
            // Can any spot reached so far jump straight to the goal?
            Shuffle(frontier, rand);
            for (int i = 0; i < frontier.Count; i++)
            {
                JumpPlanNode node = frontier[i];
                JumpStep last = TryJump(world, node.FeetX, node.FeetY, node.FeetZ, goalFeetX, goalFeetY, goalFeetZ, limits);
                if (last != null)
                {
                    return BuildPlan(node, last);
                }
            }

            if (depth == limits.MaxPlannedJumps)
            {
                break;
            }

            // No: find the perches one more jump away. On the last level, a perch is only useful when the goal
            // is within one jump of it, which is a cheap test that skips most cells.
            bool mustReachGoalNext = depth + 1 == limits.MaxPlannedJumps;
            List<JumpPlanNode> next = new List<JumpPlanNode>();
            for (int i = 0; i < frontier.Count; i++)
            {
                AddReachablePerches(world, frontier[i], limits, visited, mustReachGoalNext,
                    goalFeetX, goalFeetY, goalFeetZ, next);
            }
            if (next.Count == 0)
            {
                break;
            }
            frontier = next;
        }
        return null;
    }

    /// <summary>
    /// Picks a random spot within radius blocks of a centre point that the spider can reach with one jump, and
    /// returns that jump as a one-step plan (null when there is none). Higher spots are favoured by heightBias
    /// (see HeightWeightedPicker; 0 = no preference).
    /// Exploration passes a centre near the spider; fleeing passes a centre on the far side from the threat.
    /// </summary>
    public static List<JumpStep> PlanJumpToRandomSpot(IJumpWorld world, double startX, double startY, double startZ,
        double centerX, double centerY, double centerZ, double radius, double heightBias, JumpLimits limits, Random rand)
    {
        int startCellX = CellOfX(startX);
        int startCellY = CellOfFeetY(startY);
        int startCellZ = CellOfX(startZ);

        // Collect every standable cell that is both within one jump of the start and within radius of the centre.
        List<JumpPlanNode> candidates = new List<JumpPlanNode>();
        List<double> heights = new List<double>();
        int reach = (int)Math.Ceiling(limits.MaxJumpHorizontal);
        int up = (int)Math.Ceiling(limits.MaxJumpUp);
        int down = (int)Math.Ceiling(limits.MaxJumpDown);
        for (int x = startCellX - reach; x <= startCellX + reach; x++)
        {
            for (int z = startCellZ - reach; z <= startCellZ + reach; z++)
            {
                for (int y = startCellY - down; y <= startCellY + up; y++)
                {
                    if (x == startCellX && y == startCellY && z == startCellZ)
                    {
                        continue;
                    }
                    double feetX = x + 0.5;
                    double feetZ = z + 0.5;
                    if (Distance(feetX, y + 0.5, feetZ, centerX, centerY, centerZ) > radius)
                    {
                        continue;
                    }
                    if (!WithinLimitsRoughly(startX, startY, startZ, feetX, y, feetZ, limits))
                    {
                        continue;
                    }
                    if (!world.CanStandAt(x, y, z))
                    {
                        continue;
                    }
                    JumpPlanNode node = new JumpPlanNode();
                    node.CellX = x;
                    node.CellY = y;
                    node.CellZ = z;
                    node.FeetX = feetX;
                    node.FeetY = world.GetStandHeight(x, y, z);
                    node.FeetZ = feetZ;
                    candidates.Add(node);
                    heights.Add(node.FeetY);
                }
            }
        }

        // Pick one, check its arc (the expensive part), and if blocked drop it and pick again.
        for (int attempt = 0; attempt < RandomSpotMaxAttempts && candidates.Count > 0; attempt++)
        {
            int index = HeightWeightedPicker.PickIndex(heights, heightBias, rand);
            JumpPlanNode chosen = candidates[index];
            JumpStep step = TryJump(world, startX, startY, startZ, chosen.FeetX, chosen.FeetY, chosen.FeetZ, limits);
            if (step != null)
            {
                List<JumpStep> plan = new List<JumpStep>();
                plan.Add(step);
                return plan;
            }
            candidates.RemoveAt(index);
            heights.RemoveAt(index);
        }
        return null;
    }

    /// <summary>
    /// One jump between two feet positions whose whole arc is free of blocks, or null. Tries the preferred launch
    /// angle first and then steeper and flatter ones, so that a jump onto a ledge can use a higher arc.
    /// </summary>
    public static JumpStep TryJump(IJumpWorld world, double fromX, double fromY, double fromZ,
        double toX, double toY, double toZ, JumpLimits limits)
    {
        double dx = toX - fromX;
        double dy = toY - fromY;
        double dz = toZ - fromZ;
        int count = JumpMath.AngleCandidateCount(limits);
        for (int i = 0; i < count; i++)
        {
            JumpStep step = new JumpStep();
            if (!JumpMath.SolveLaunchAtAngle(dx, dy, dz, JumpMath.AngleCandidate(limits, i), limits, step))
            {
                continue;
            }
            step.FromX = fromX;
            step.FromY = fromY;
            step.FromZ = fromZ;
            step.ToX = toX;
            step.ToY = toY;
            step.ToZ = toZ;
            if (IsArcClear(world, step))
            {
                return step;
            }
        }
        return null;
    }

    /// <summary>
    /// True when the spider's box touches no block anywhere along the jump, and there is something to land on.
    /// Mirrors the engine: each physics tick first moves the spider vertically (checked at the old horizontal
    /// position), then horizontally (checked at the new one).
    /// </summary>
    public static bool IsArcClear(IJumpWorld world, JumpStep step)
    {
        double stepX = step.VX * JumpMath.PhysicsTickSeconds;
        double stepZ = step.VZ * JumpMath.PhysicsTickSeconds;
        double motionY = step.VY / JumpMath.BlocksPerSecondPerMotionUnit;
        double relativeY = 0;
        double previousX = step.FromX;
        double previousZ = step.FromZ;

        for (int tick = 1; tick <= step.FlightTicks; tick++)
        {
            motionY = JumpMath.StepVerticalMotion(motionY);
            relativeY = relativeY + motionY * JumpMath.PositionPerMotionPerTick;
            double y = step.FromY + relativeY;
            double x = step.FromX + stepX * tick;
            double z = step.FromZ + stepZ * tick;
            if (tick == step.FlightTicks)
            {
                // The landing surface stops the fall at the landing height.
                y = step.ToY;
                x = step.ToX;
                z = step.ToZ;
            }

            if (!world.IsFree(previousX, y, previousZ))
            {
                return false;
            }
            if (!world.IsFree(x, y, z))
            {
                return false;
            }
            previousX = x;
            previousZ = z;
        }

        // Something to land on, a little behind the landing point (see LastTickFootingMargin).
        double backX = step.ToX - stepX * LastTickFootingMargin;
        double backZ = step.ToZ - stepZ * LastTickFootingMargin;
        if (!world.IsFree(backX, step.ToY, backZ))
        {
            return false;
        }
        if (world.IsFree(backX, step.ToY - FootingProbeDepth, backZ))
        {
            return false;
        }
        return true;
    }

    /// <summary>The block cell X (or Z) that contains a coordinate.</summary>
    public static int CellOfX(double x)
    {
        return (int)Math.Floor(x);
    }

    /// <summary>The block cell Y that contains feet at this height (a hair below a whole number counts as that number).</summary>
    public static int CellOfFeetY(double feetY)
    {
        return (int)Math.Floor(feetY + CellHeightEpsilon);
    }

    private static void AddReachablePerches(IJumpWorld world, JumpPlanNode from, JumpLimits limits, HashSet<long> visited,
        bool mustReachGoalNext, double goalFeetX, double goalFeetY, double goalFeetZ, List<JumpPlanNode> result)
    {
        int reach = (int)Math.Ceiling(limits.MaxJumpHorizontal);
        int up = (int)Math.Ceiling(limits.MaxJumpUp);
        int down = (int)Math.Ceiling(limits.MaxJumpDown);
        for (int x = from.CellX - reach; x <= from.CellX + reach; x++)
        {
            for (int z = from.CellZ - reach; z <= from.CellZ + reach; z++)
            {
                for (int y = from.CellY - down; y <= from.CellY + up; y++)
                {
                    double feetX = x + 0.5;
                    double feetZ = z + 0.5;
                    if (!WithinLimitsRoughly(from.FeetX, from.FeetY, from.FeetZ, feetX, y, feetZ, limits))
                    {
                        continue;
                    }
                    if (mustReachGoalNext && !WithinLimitsRoughly(feetX, y, feetZ, goalFeetX, goalFeetY, goalFeetZ, limits))
                    {
                        continue;
                    }
                    long key = CellKey(x, y, z);
                    if (visited.Contains(key))
                    {
                        continue;
                    }
                    if (!world.CanStandAt(x, y, z))
                    {
                        continue;
                    }
                    double feetY = world.GetStandHeight(x, y, z);
                    JumpStep step = TryJump(world, from.FeetX, from.FeetY, from.FeetZ, feetX, feetY, feetZ, limits);
                    if (step == null)
                    {
                        continue;
                    }
                    visited.Add(key);
                    JumpPlanNode node = new JumpPlanNode();
                    node.CellX = x;
                    node.CellY = y;
                    node.CellZ = z;
                    node.FeetX = feetX;
                    node.FeetY = feetY;
                    node.FeetZ = feetZ;
                    node.Parent = from;
                    node.StepFromParent = step;
                    node.Depth = from.Depth + 1;
                    result.Add(node);
                }
            }
        }
    }

    // A quick test against the distance limits only, before any expensive check. toCellY is the cell's floor
    // height; a partial floor can raise the feet by up to one block, so allow one block of slack upwards.
    private static bool WithinLimitsRoughly(double fromX, double fromY, double fromZ, double toX, double toCellY, double toZ, JumpLimits limits)
    {
        double dx = toX - fromX;
        double dz = toZ - fromZ;
        if (dx * dx + dz * dz > limits.MaxJumpHorizontal * limits.MaxJumpHorizontal)
        {
            return false;
        }
        double dy = toCellY - fromY;
        if (dy > limits.MaxJumpUp)
        {
            return false;
        }
        if (dy + 1 < -limits.MaxJumpDown)
        {
            return false;
        }
        return true;
    }

    private static List<JumpStep> BuildPlan(JumpPlanNode lastNode, JumpStep lastStep)
    {
        List<JumpStep> plan = new List<JumpStep>();
        JumpPlanNode node = lastNode;
        while (node != null && node.StepFromParent != null)
        {
            plan.Insert(0, node.StepFromParent);
            node = node.Parent;
        }
        plan.Add(lastStep);
        return plan;
    }

    private static void Shuffle(List<JumpPlanNode> list, Random rand)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rand.Next(i + 1);
            JumpPlanNode temp = list[i];
            list[i] = list[j];
            list[j] = temp;
        }
    }

    private static double Distance(double ax, double ay, double az, double bx, double by, double bz)
    {
        double dx = ax - bx;
        double dy = ay - by;
        double dz = az - bz;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    // Packs a block cell into one number for the visited set. 21 bits per axis is plenty for the few blocks
    // around one spider; the masks just keep negative numbers from spilling into the other axes.
    private static long CellKey(int x, int y, int z)
    {
        long kx = x & 0x1FFFFF;
        long ky = y & 0x1FFFFF;
        long kz = z & 0x1FFFFF;
        return (kx << 42) | (ky << 21) | kz;
    }
}
