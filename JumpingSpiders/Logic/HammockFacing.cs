using System;

namespace JumpingSpiders;

/// <summary>
/// Which way a spider sleeping in a hammock faces: out through the hammock's opening. Pure C# (no game types),
/// covered by unit tests; BlockSilkHammock passes in its support direction.
///
/// The hammock model (shapes/block/silkhammock-wall.json) has its back panel on its +X side and its opening on its
/// -X side. The block JSON turns it so the back panel is against the supporting wall, so a wall hammock opens away
/// from its wall. Ceiling and floor hammocks are not turned around Y (the floor one is only turned upside down
/// around X), so both open towards -X (west).
///
/// Yaw is the game's convention: radians, 0 looks towards +Z (south), pi/2 towards +X (east); the yaw towards a
/// direction (dx, dz) is Atan2(dx, dz), as in the vanilla look-at and pathfinding code. The spider's model has its
/// head towards -X like vanilla animals, and the game's renderer turns it so that it looks along its yaw.
/// </summary>
public static class HammockFacing
{
    /// <summary>X part of the direction ceiling and floor hammocks open towards: -X, west.</summary>
    public const int VerticalOpeningX = -1;

    /// <summary>Z part of the direction ceiling and floor hammocks open towards.</summary>
    public const int VerticalOpeningZ = 0;

    /// <summary>
    /// The yaw, in [0, 2 pi), of a spider sleeping in a hammock whose support (the block holding it) is in direction
    /// (supportX, supportY, supportZ) from the hammock: one of the six unit directions, e.g. (0, 0, -1) for a hammock
    /// on a wall to its north, (0, 1, 0) for one under a ceiling.
    /// </summary>
    public static double SleepYaw(int supportX, int supportY, int supportZ)
    {
        int lookX = -supportX;
        int lookZ = -supportZ;
        if (supportX == 0 && supportZ == 0)
        {
            // Ceiling or floor (or no direction at all): the model's own opening.
            lookX = VerticalOpeningX;
            lookZ = VerticalOpeningZ;
        }
        double yaw = Math.Atan2(lookX, lookZ);
        if (yaw < 0)
        {
            yaw = yaw + 2 * Math.PI;
        }
        return yaw;
    }
}
