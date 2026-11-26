namespace JumpingSpiders;

/// <summary>
/// One spot in JumpPlanner's search: a block cell the spider can stand in, its exact feet position,
/// and how the spider got there (the previous spot and the jump from it). LaunchSpotFinder also returns its spot as
/// one (with no Parent).
/// </summary>
public class JumpPlanNode
{
    public int CellX;
    public int CellY;
    public int CellZ;

    public double FeetX;
    public double FeetY;
    public double FeetZ;

    /// <summary>
    /// True when the feet stand in the middle of the cell at its stand height (every spot the search finds; a start
    /// only when it happens to), so that a jump from here is the same for every spot in this cell.
    /// </summary>
    public bool AtCellCentre;

    /// <summary>The spot this one was reached from, or null for the start.</summary>
    public JumpPlanNode Parent;

    /// <summary>The jump from Parent to here, or null for the start.</summary>
    public JumpStep StepFromParent;
}
