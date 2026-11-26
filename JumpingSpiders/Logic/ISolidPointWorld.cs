namespace JumpingSpiders;

/// <summary>
/// "Is there something solid at this exact point?" The question behind the line-of-reach and line-of-sight checks of
/// hammock building (BuildReach.IsLineClear). SpiderJumpWorld answers it from the real blocks' collision boxes; the
/// unit tests answer it from their small hand-made worlds.
/// </summary>
public interface ISolidPointWorld
{
    /// <summary>True when the point lies inside a collision box of a block (plants, air and water have none).</summary>
    bool IsSolidAt(double x, double y, double z);
}
