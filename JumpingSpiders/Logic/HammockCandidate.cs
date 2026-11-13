namespace JumpingSpiders;

/// <summary>
/// One empty cell next to a tree trunk that could take a wild hammock. The spawner fills in the cell and its
/// height, HammockReachFinder fills in whether a spider on the ground could jump into it. Plain data.
/// </summary>
public class HammockCandidate
{
    public int CellX;
    public int CellY;
    public int CellZ;

    /// <summary>Blocks above the trunk base (0 = the cell beside the lowest trunk block). Higher is preferred.</summary>
    public int Height;

    /// <summary>Free for the caller: the spawner stores which side of the trunk the cell is on.</summary>
    public int Tag;

    /// <summary>Set by HammockReachFinder.MarkReachable.</summary>
    public bool Reachable;

    /// <summary>Set by HammockReachFinder.MarkReachable: 1 = straight from the ground, 2 = by way of a perch.</summary>
    public int Jumps;
}
