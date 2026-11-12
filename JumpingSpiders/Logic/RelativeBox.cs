namespace JumpingSpiders;

/// <summary>
/// A box given relative to something, in blocks: to a block cell's corner (a block's collision box), or to an
/// entity's feet (its collision box). The same idea as the game's Cuboidf, repeated here so the Logic folder needs
/// nothing from the game. Plain data.
/// </summary>
public class RelativeBox
{
    public double X1;
    public double Y1;
    public double Z1;
    public double X2;
    public double Y2;
    public double Z2;

    public RelativeBox(double x1, double y1, double z1, double x2, double y2, double z2)
    {
        X1 = x1;
        Y1 = y1;
        Z1 = z1;
        X2 = x2;
        Y2 = y2;
        Z2 = z2;
    }
}
