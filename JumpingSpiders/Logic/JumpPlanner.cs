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
    /// small steering lag of JumpController. A sped-up flight also covers at most one tick of the natural arc in its
    /// last tick (FlightSchedule), so this check holds for it too.
    /// </summary>
    public const double LastTickFootingMargin = 1.25;

    /// <summary>
    /// IsArcClear checks a move longer than this (blocks) within one physics tick at points part-way through it too,
    /// at most this far apart. Well under the spider's smallest size (the baby's box is 0.3 tall and 0.4 wide), so a
    /// thin plate such as a hammock's 1/16 floor cannot fall between two checked points.
    /// </summary>
    public const double MaxUncheckedMove = 0.25;

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
        return PlanJumps(world, startX, startY, startZ, goalX, goalY, goalZ, limits, rand, null);
    }

    /// <summary>
    /// The same, for a caller that plans to the same goal from many starts in the same world (LaunchSpotFinder).
    /// jumpsToGoal remembers, by cell (CellKey), the jump from a spot standing in the middle of that cell to the goal,
    /// or null when there is none, so that the next search need not test it again. Start with an empty dictionary and
    /// pass the same one for that goal only.
    /// </summary>
    public static List<JumpStep> PlanJumps(IJumpWorld world, double startX, double startY, double startZ,
        int goalX, int goalY, int goalZ, JumpLimits limits, Random rand, Dictionary<long, JumpStep> jumpsToGoal)
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
        start.AtCellCentre = jumpsToGoal != null && startX == startCellX + 0.5 && startZ == startCellZ + 0.5
            && startY == world.GetStandHeight(startCellX, startCellY, startCellZ);

        HashSet<long> visited = new HashSet<long>();
        visited.Add(CellKey(startCellX, startCellY, startCellZ));
        visited.Add(CellKey(goalX, goalY, goalZ));

        List<JumpPlanNode> frontier = new List<JumpPlanNode>();
        frontier.Add(start);

        for (int depth = 1; depth <= limits.MaxPlannedJumps; depth++)
        {
            // Can any spot reached so far jump straight to the goal?
            int[] order = RandomOrder.Shuffled(frontier.Count, rand);
            for (int i = 0; i < order.Length; i++)
            {
                JumpPlanNode node = frontier[order[i]];
                JumpStep last = JumpToGoal(world, node, goalFeetX, goalFeetY, goalFeetZ, limits, jumpsToGoal);
                if (last != null)
                {
                    return BuildPlan(node, last);
                }
            }

            if (depth == limits.MaxPlannedJumps)
            {
                break;
            }

            // No: one more jump. On the last level a perch is only useful when it can jump to the goal, so each one is
            // tested against the goal as soon as it is found, and the first that works is the plan.
            if (depth + 1 == limits.MaxPlannedJumps)
            {
                for (int i = 0; i < order.Length; i++)
                {
                    List<JumpStep> plan = PlanThroughLastPerch(world, frontier[order[i]], limits, visited,
                        goalFeetX, goalFeetY, goalFeetZ, rand, jumpsToGoal);
                    if (plan != null)
                    {
                        return plan;
                    }
                }
                break;
            }

            // Otherwise collect the perches one more jump away for the next level.
            List<JumpPlanNode> next = new List<JumpPlanNode>();
            for (int i = 0; i < frontier.Count; i++)
            {
                AddReachablePerches(world, frontier[i], limits, visited, next);
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
    /// returns that jump as a one-step plan (null when there is none). Spots higher than the spider's feet are favoured
    /// by heightBias (see HeightWeightedPicker; 0 = no preference); spots at or below the spider's height all count as
    /// equally low, so a slope or drop nearby does not water the preference down.
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
                    JumpPlanNode node = NewNode(x, y, z, feetX, world.GetStandHeight(x, y, z), feetZ);
                    candidates.Add(node);
                    heights.Add(Math.Max(node.FeetY, startY));
                }
            }
        }

        // Pick one, check its arc (the expensive part), and if blocked drop it and pick again.
        List<double> weights = null;
        if (candidates.Count > 0)
        {
            weights = HeightWeightedPicker.Weights(heights, heightBias);
        }
        for (int attempt = 0; attempt < RandomSpotMaxAttempts && candidates.Count > 0; attempt++)
        {
            int index = HeightWeightedPicker.PickIndexByWeight(weights, rand);
            JumpPlanNode chosen = candidates[index];
            JumpStep step = TryJump(world, startX, startY, startZ, chosen.FeetX, chosen.FeetY, chosen.FeetZ, limits);
            if (step != null)
            {
                List<JumpStep> plan = new List<JumpStep>();
                plan.Add(step);
                return plan;
            }
            candidates.RemoveAt(index);
            HeightWeightedPicker.RemoveAt(heights, weights, index, heightBias);
        }
        return null;
    }

    /// <summary>
    /// One jump between two feet positions whose whole arc is free of blocks and that ends on something to stand on,
    /// or null. Tries the preferred launch angle first and then steeper and flatter ones, so that a jump onto a ledge
    /// can use a higher arc.
    /// </summary>
    public static JumpStep TryJump(IJumpWorld world, double fromX, double fromY, double fromZ,
        double toX, double toY, double toZ, JumpLimits limits)
    {
        return TryJump(world, fromX, fromY, fromZ, toX, toY, toZ, limits, true);
    }

    /// <summary>
    /// The same, with needFooting false for a jump that ends in the air against a wall or ceiling (ClingJump): then
    /// only the flight is checked, not what is under the landing point.
    /// </summary>
    public static JumpStep TryJump(IJumpWorld world, double fromX, double fromY, double fromZ,
        double toX, double toY, double toZ, JumpLimits limits, bool needFooting)
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
            if (IsArcClear(world, step, needFooting))
            {
                return step;
            }
        }
        return null;
    }

    /// <summary>True when the spider's box touches no block anywhere along the jump, and there is something to land on.</summary>
    public static bool IsArcClear(IJumpWorld world, JumpStep step)
    {
        return IsArcClear(world, step, true);
    }

    /// <summary>
    /// True when the spider's box touches no block anywhere along the jump, and (with needFooting) there is something
    /// to land on. Mirrors the engine: each physics tick first moves the spider vertically (checked at the old
    /// horizontal position), then horizontally (checked at the new one). A move longer than MaxUncheckedMove is also
    /// checked at points part-way through it: the engine sweeps the box along the whole move, and a steep launch rises
    /// more in one tick than the spider is tall, so checking only the ends would let it pass through a thin plate.
    /// </summary>
    public static bool IsArcClear(IJumpWorld world, JumpStep step, bool needFooting)
    {
        double stepX = step.VX * JumpMath.PhysicsTickSeconds;
        double stepZ = step.VZ * JumpMath.PhysicsTickSeconds;
        double motionY = step.VY / JumpMath.BlocksPerSecondPerMotionUnit;
        double relativeY = 0;
        double previousX = step.FromX;
        double previousY = step.FromY;
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

            // The vertical move, at the old horizontal position.
            int parts = PartsOfMove(y - previousY);
            for (int part = 1; part <= parts; part++)
            {
                if (!world.IsFree(previousX, previousY + (y - previousY) * part / parts, previousZ))
                {
                    return false;
                }
            }
            // Then the horizontal move, at the new height.
            double moveX = x - previousX;
            double moveZ = z - previousZ;
            parts = PartsOfMove(Math.Sqrt(moveX * moveX + moveZ * moveZ));
            for (int part = 1; part <= parts; part++)
            {
                if (!world.IsFree(previousX + moveX * part / parts, y, previousZ + moveZ * part / parts))
                {
                    return false;
                }
            }
            previousX = x;
            previousY = y;
            previousZ = z;
        }

        if (!needFooting)
        {
            return true;
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
        List<JumpPlanNode> result)
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
                    JumpPlanNode node = NewNode(x, y, z, feetX, feetY, feetZ);
                    node.Parent = from;
                    node.StepFromParent = step;
                    result.Add(node);
                }
            }
        }
    }

    // The last level of a search: the cells one jump from "from" that are roughly within one jump of the goal, in
    // random order (so that among equally short plans each perch is as likely). Returns the plan through the first
    // one that the spider can jump to and from which it can jump to the goal, or null.
    private static List<JumpStep> PlanThroughLastPerch(IJumpWorld world, JumpPlanNode from, JumpLimits limits,
        HashSet<long> visited, double goalFeetX, double goalFeetY, double goalFeetZ, Random rand,
        Dictionary<long, JumpStep> jumpsToGoal)
    {
        List<JumpPlanNode> cells = new List<JumpPlanNode>();
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
                    if (!WithinLimitsRoughly(feetX, y, feetZ, goalFeetX, goalFeetY, goalFeetZ, limits))
                    {
                        continue;
                    }
                    if (visited.Contains(CellKey(x, y, z)) || !world.CanStandAt(x, y, z))
                    {
                        continue;
                    }
                    cells.Add(NewNode(x, y, z, feetX, world.GetStandHeight(x, y, z), feetZ));
                }
            }
        }

        int[] order = RandomOrder.Shuffled(cells.Count, rand);
        for (int i = 0; i < order.Length; i++)
        {
            JumpPlanNode node = cells[order[i]];
            JumpStep step = TryJump(world, from.FeetX, from.FeetY, from.FeetZ, node.FeetX, node.FeetY, node.FeetZ, limits);
            if (step == null)
            {
                continue;
            }
            // Reachable: another spot of this level need not try it again (its jump to the goal is the same).
            visited.Add(CellKey(node.CellX, node.CellY, node.CellZ));
            JumpStep last = JumpToGoal(world, node, goalFeetX, goalFeetY, goalFeetZ, limits, jumpsToGoal);
            if (last == null)
            {
                continue;
            }
            node.Parent = from;
            node.StepFromParent = step;
            return BuildPlan(node, last);
        }
        return null;
    }

    // TryJump from the node to the goal, looked up in (and added to) jumpsToGoal when there is one and the node stands
    // in the middle of its cell, as every spot but a start does.
    private static JumpStep JumpToGoal(IJumpWorld world, JumpPlanNode node, double goalFeetX, double goalFeetY, double goalFeetZ,
        JumpLimits limits, Dictionary<long, JumpStep> jumpsToGoal)
    {
        if (jumpsToGoal == null || !node.AtCellCentre)
        {
            return TryJump(world, node.FeetX, node.FeetY, node.FeetZ, goalFeetX, goalFeetY, goalFeetZ, limits);
        }
        long key = CellKey(node.CellX, node.CellY, node.CellZ);
        JumpStep known;
        if (jumpsToGoal.TryGetValue(key, out known))
        {
            return known;
        }
        JumpStep step = TryJump(world, node.FeetX, node.FeetY, node.FeetZ, goalFeetX, goalFeetY, goalFeetZ, limits);
        jumpsToGoal[key] = step;
        return step;
    }

    // A spot standing in the middle of its cell (feet at the cell's stand height).
    private static JumpPlanNode NewNode(int x, int y, int z, double feetX, double feetY, double feetZ)
    {
        JumpPlanNode node = new JumpPlanNode();
        node.AtCellCentre = true;
        node.CellX = x;
        node.CellY = y;
        node.CellZ = z;
        node.FeetX = feetX;
        node.FeetY = feetY;
        node.FeetZ = feetZ;
        return node;
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

    // How many equal parts a move of this length (blocks, either sign) is checked in: 1 up to MaxUncheckedMove.
    private static int PartsOfMove(double length)
    {
        return Math.Max(1, (int)Math.Ceiling(Math.Abs(length) / MaxUncheckedMove));
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

    /// <summary>Straight-line distance between two points, in blocks.</summary>
    public static double Distance(double ax, double ay, double az, double bx, double by, double bz)
    {
        double dx = ax - bx;
        double dy = ay - by;
        double dz = az - bz;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }

    /// <summary>
    /// Packs a block cell into one number, for sets of cells already seen. 21 bits per axis is plenty for the few
    /// blocks around one spider; the masks just keep negative numbers from spilling into the other axes.
    /// </summary>
    public static long CellKey(int x, int y, int z)
    {
        long kx = x & 0x1FFFFF;
        long ky = y & 0x1FFFFF;
        long kz = z & 0x1FFFFF;
        return (kx << 42) | (ky << 21) | kz;
    }
}
