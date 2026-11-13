namespace JumpingSpiders;

/// <summary>
/// One spot in JumpPlanner's search: a block cell the spider can stand in, its exact feet position,
/// and how the spider got there (the previous spot and the jump from it). Only used inside JumpPlanner.
/// </summary>
public class JumpPlanNode
{
    public int CellX;
    public int CellY;
    public int CellZ;

    public double FeetX;
    public double FeetY;
    public double FeetZ;

    /// <summary>The spot this one was reached from, or null for the start.</summary>
    public JumpPlanNode Parent;

    /// <summary>The jump from Parent to here, or null for the start.</summary>
    public JumpStep StepFromParent;

    /// <summary>Number of jumps from the start to here.</summary>
    public int Depth;
}
