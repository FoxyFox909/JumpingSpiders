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
/// Candidates are checked best first, each with one full JumpPlanner.PlanJumps search, at most maxPlanChecks of them,
/// because each search is the expensive part. "Best" means little walking, and standing a comfortable distance from
/// the goal (about half a jump's reach, horizontally): right at the foot of a tree or pillar the trunk itself is in the
/// way of every arc, and far away the jumps do not reach. The searches share what they found out about which cells
/// can jump to the goal (PlanJumps with jumpsToGoal), since nearby candidates try mostly the same perches.
///
/// Cost: listing the candidates is a few hundred cell lookups. A search is cheapest when it succeeds early and dearest
/// when the goal cannot be reached at all: on terraced ground around a walled-in goal, measured on the unit tests'
/// block world, 8 searches took about 23 ms together (about 35 ms in the version before, whose searches did not share
/// their jumps to the goal), and on a 1.22.7 server the dearest single search took about 5 ms. So the sleep task
/// spreads the work over ticks, one search per tick: it creates a LaunchSpotFinder, calls CheckNext once per tick until
/// IsDone, then reads Found. Find does it all at once (for the unit tests).
///
/// Pure C# (no game types), covered by unit tests.
/// </summary>
public class LaunchSpotFinder
{
    /// <summary>The comfortable horizontal distance from the goal, as a fraction of JumpLimits.MaxJumpHorizontal.</summary>
    public const double ComfortableReachFraction = 0.5;

    /// <summary>How much one block away from that comfortable distance counts against a spot, in blocks of walking.</summary>
    public const double ComfortWeight = 2.0;

    /// <summary>The spot found, or null (none yet, or none at all once IsDone).</summary>
    public JumpPlanNode Found;

    private readonly IJumpWorld world;
    private readonly int goalX;
    private readonly int goalY;
    private readonly int goalZ;
    private readonly JumpLimits limits;
    private readonly int maxPlanChecks;
    private readonly List<JumpPlanNode> candidates = new List<JumpPlanNode>();
    private readonly List<double> scores = new List<double>();
    private readonly Dictionary<long, JumpStep> jumpsToGoal = new Dictionary<long, JumpStep>();
    private int checksDone;

    /// <summary>
    /// A cell the spider can walk to and jump to the goal from, or null. The result's FeetX/Y/Z is where its feet
    /// would be standing in that cell. The cell the spider stands in now is never returned (that already failed).
    /// Does every check at once.
    /// </summary>
    public static JumpPlanNode Find(IJumpWorld world, double spiderX, double spiderY, double spiderZ,
        int goalX, int goalY, int goalZ, JumpLimits limits, int searchRadius, int maxStepUp, int maxStepDown,
        int maxPlanChecks, Random rand)
    {
        LaunchSpotFinder finder = new LaunchSpotFinder(world, spiderX, spiderY, spiderZ, goalX, goalY, goalZ, limits,
            searchRadius, maxStepUp, maxStepDown, maxPlanChecks);
        while (!finder.IsDone())
        {
            finder.CheckNext(rand);
        }
        return finder.Found;
    }

    /// <summary>Lists and scores the candidates (no planner search yet); see Find for the parameters.</summary>
    public LaunchSpotFinder(IJumpWorld world, double spiderX, double spiderY, double spiderZ,
        int goalX, int goalY, int goalZ, JumpLimits limits, int searchRadius, int maxStepUp, int maxStepDown,
        int maxPlanChecks)
    {
        this.world = world;
        this.goalX = goalX;
        this.goalY = goalY;
        this.goalZ = goalZ;
        this.limits = limits;
        this.maxPlanChecks = maxPlanChecks;

        int spiderCellX = JumpPlanner.CellOfX(spiderX);
        int spiderCellY = JumpPlanner.CellOfFeetY(spiderY);
        int spiderCellZ = JumpPlanner.CellOfX(spiderZ);

        // Heights to look at: not lower than every planned jump climbing MaxJumpUp could make up for, not above the
        // goal, and within walking reach of the spider.
        int maxBelow = (int)Math.Ceiling(limits.MaxJumpUp * limits.MaxPlannedJumps);
        int lowestY = Math.Max(goalY - maxBelow, spiderCellY - maxStepDown);
        int highestY = Math.Min(goalY, spiderCellY + maxStepUp);

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
                    node.AtCellCentre = true;
                    candidates.Add(node);
                    double dxToGoal = node.FeetX - (goalX + 0.5);
                    double dzToGoal = node.FeetZ - (goalZ + 0.5);
                    double fromGoal = Math.Sqrt(dxToGoal * dxToGoal + dzToGoal * dzToGoal);
                    double score = JumpPlanner.Distance(node.FeetX, node.FeetY, node.FeetZ, spiderX, spiderY, spiderZ)
                        + ComfortWeight * Math.Abs(fromGoal - limits.MaxJumpHorizontal * ComfortableReachFraction);
                    scores.Add(score);
                }
            }
        }
    }

    /// <summary>True once a spot was found, or every check allowed has been made.</summary>
    public bool IsDone()
    {
        return Found != null || checksDone >= maxPlanChecks || candidates.Count == 0;
    }

    /// <summary>Checks the best candidate left with one planner search (does nothing once IsDone).</summary>
    public void CheckNext(Random rand)
    {
        if (IsDone())
        {
            return;
        }
        checksDone++;
        // Best score first. A full sort is not needed: we only ever look at the first few.
        int best = IndexOfSmallest(scores);
        JumpPlanNode candidate = candidates[best];
        candidates.RemoveAt(best);
        scores.RemoveAt(best);

        List<JumpStep> plan = JumpPlanner.PlanJumps(world, candidate.FeetX, candidate.FeetY, candidate.FeetZ,
            goalX, goalY, goalZ, limits, rand, jumpsToGoal);
        if (plan != null)
        {
            Found = candidate;
        }
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
}
