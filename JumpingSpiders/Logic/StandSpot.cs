namespace JumpingSpiders;

/// <summary>
/// A block cell a spider can stand in, with its exact feet position. HammockReachFinder uses these for the ground
/// spots around a tree that a spider jumps from, and for the perches (higher spots) it can jump to on the way up.
/// Plain data.
/// </summary>
public class StandSpot
{
    public int CellX;
    public int CellY;
    public int CellZ;

    public double FeetX;
    public double FeetY;
    public double FeetZ;

    /// <summary>For a perch: the jump from a ground spot up to it. Null for a ground spot.</summary>
    public JumpStep StepFromGround;
}
