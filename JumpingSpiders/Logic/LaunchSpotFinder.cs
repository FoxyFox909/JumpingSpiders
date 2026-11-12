using System;
using System.Collections.Generic;

namespace JumpingSpiders;

/// <summary>
/// Finds a spot to walk to, from which the spider can jump to a goal cell (a hammock up a tree, usually) when it
/// cannot jump there from where it stands. The game's pathfinder only walks, and the jump planner only jumps; this
/// joins the two: the sleep task walks to the spot this returns, then plans the jumps from there.
///
/// How: every standable cell within searchRadius blocks (horizontally) of the goal, not above it, and at a height the
/// spider can walk to (at most maxStepUp blocks above its feet and maxStepDown below, since the pathfinder only steps
/// up one block at a time) is a candidate. Whether a path really exists is left to the pathfinder.
/// Candidates are checked best first, each with one full JumpPlanner.PlanJumps search (about 1 ms), at most
/// maxPlanChecks of them, because each search is the expensive part. "Best" means little walking, and standing a
/// comfortable distance from the goal (about half a jump's reach, horizontally): right at the foot of a tree or
/// pillar the trunk itself is in the way of every arc, and far away the jumps do not reach.
///
/// Pure C# (no game types), covered by unit tests.
/// </summary>
public static class LaunchSpotFinder
{
    /// <summary>The comfortable horizontal distance from the goal, as a fraction of JumpLimits.MaxJumpHorizontal.</summary>
    public const double ComfortableReachFraction = 0.5;

    /// <summary>How much one block away from that comfortable distance counts against a spot, in blocks of walking.</summary>
    public const double ComfortWeight = 2.0;

    /// <summary>
    /// A cell the spider can walk to and jump to the goal from, or null. The result's FeetX/Y/Z is where its feet
    /// would be standing in that cell. The cell the spider stands in now is never returned (that already failed).
    /// </summary>
    public static JumpPlanNode Find(IJumpWorld world, double spiderX, double spiderY, double spiderZ,
        int goalX, int goalY, int goalZ, JumpLimits limits, int searchRadius, int maxStepUp, int maxStepDown,
        int maxPlanChecks, Random rand)
    {
        int spiderCellX = JumpPlanner.CellOfX(spiderX);
        int spiderCellY = JumpPlanner.CellOfFeetY(spiderY);
        int spiderCellZ = JumpPlanner.CellOfX(spiderZ);

        // Heights to look at: not lower than every planned jump climbing MaxJumpUp could make up for, not above the
        // goal, and within walking reach of the spider.
        int maxBelow = (int)Math.Ceiling(limits.MaxJumpUp * limits.MaxPlannedJumps);
        int lowestY = Math.Max(goalY - maxBelow, spiderCellY - maxStepDown);
        int highestY = Math.Min(goalY, spiderCellY + maxStepUp);

        List<JumpPlanNode> candidates = new List<JumpPlanNode>();
        List<double> scores = new List<double>();
        for (int x = goalX - searchRadius; x <= goalX + searchRadius; x++)
        {
            for (int z = goalZ - searchRadius; z <= goalZ + searchRadius; z++)
            {
                int dxGoal = x - goalX;
                int dzGoal = z - goalZ;
                if (dxGoal * dxGoal + dzGoal * dzGoal > searchRadius * searchRadius)
                {
                    continue;
                }
                for (int y = lowestY; y <= highestY; y++)
                {
                    if (x == goalX && y == goalY && z == goalZ)
                    {
                        continue;
                    }
                    if (x == spiderCellX && y == spiderCellY && z == spiderCellZ)
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
                    node.FeetX = x + 0.5;
                    node.FeetY = world.GetStandHeight(x, y, z);
                    node.FeetZ = z + 0.5;
                    candidates.Add(node);
                    double dxToGoal = node.FeetX - (goalX + 0.5);
                    double dzToGoal = node.FeetZ - (goalZ + 0.5);
                    double fromGoal = Math.Sqrt(dxToGoal * dxToGoal + dzToGoal * dzToGoal);
                    double score = Distance(node.FeetX, node.FeetY, node.FeetZ, spiderX, spiderY, spiderZ)
                        + ComfortWeight * Math.Abs(fromGoal - limits.MaxJumpHorizontal * ComfortableReachFraction);
                    scores.Add(score);
                }
            }
        }

        // Best score first. A full sort is not needed: we only ever look at the first few.
        for (int check = 0; check < maxPlanChecks && candidates.Count > 0; check++)
        {
            int nearest = IndexOfSmallest(scores);
            JumpPlanNode candidate = candidates[nearest];
            candidates.RemoveAt(nearest);
            scores.RemoveAt(nearest);

            List<JumpStep> plan = JumpPlanner.PlanJumps(world, candidate.FeetX, candidate.FeetY, candidate.FeetZ,
                goalX, goalY, goalZ, limits, rand);
            if (plan != null)
            {
                return candidate;
            }
        }
        return null;
    }

    private static int IndexOfSmallest(List<double> values)
    {
        int best = 0;
        for (int i = 1; i < values.Count; i++)
        {
            if (values[i] < values[best])
            {
                best = i;
            }
        }
        return best;
    }

    private static double Distance(double ax, double ay, double az, double bx, double by, double bz)
    {
        double dx = ax - bx;
        double dy = ay - by;
        double dz = az - bz;
        return Math.Sqrt(dx * dx + dy * dy + dz * dz);
    }
}
