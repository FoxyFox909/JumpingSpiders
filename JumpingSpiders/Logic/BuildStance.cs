namespace JumpingSpiders;

/// <summary>
/// One place a spider could be while it builds a hammock: standing on the ground (or a perch), or holding on to a wall
/// or under a ceiling. BuildStanceFinder makes these; AiTaskSpiderBuildHammock tries to get to one. Plain data.
/// </summary>
public class BuildStance
{
    /// <summary>False: standing on something. True: holding on to a wall or ceiling with its physics paused.</summary>
    public bool Clinging;

    /// <summary>The block cell the spider is in.</summary>
    public int CellX;
    public int CellY;
    public int CellZ;

    /// <summary>Where its feet are (the bottom centre of its collision box).</summary>
    public double FeetX;
    public double FeetY;
    public double FeetZ;

    /// <summary>
    /// For a clinging stance, the direction from the spider to the surface it holds on to: one of (1, 0, 0), (-1, 0, 0),
    /// (0, 0, 1), (0, 0, -1) for a wall, (0, 1, 0) for a ceiling. All 0 for a standing stance.
    /// </summary>
    public int SurfaceX;
    public int SurfaceY;
    public int SurfaceZ;

    /// <summary>Straight-line distance from the spider's feet when the stance was found, in blocks (the sort order).</summary>
    public double DistanceFromSpider;

    /// <summary>True for a stance under a ceiling.</summary>
    public bool IsCeiling()
    {
        return Clinging && SurfaceY > 0;
    }
}
