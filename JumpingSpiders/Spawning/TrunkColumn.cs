namespace JumpingSpiders;

/// <summary>One grown tree trunk found by TreeTrunks: its block column and how tall it is. Plain data.</summary>
public class TrunkColumn
{
    public int X;
    public int Z;

    /// <summary>Y of the lowest trunk block (it stands on the ground).</summary>
    public int BaseY;

    /// <summary>Trunk blocks counted upwards from the base (capped at what wild hammocks can use).</summary>
    public int Height;
}
