using System;

namespace JumpingSpiders;

/// <summary>
/// Which way a spider sleeping in a hammock faces: out through the hammock's opening. Pure C# (no game types),
/// covered by unit tests; BlockSilkHammock passes in its support direction.
///
/// The hammock model (shapes/block/silkhammock-wall.json) has its back panel on its +X side and its opening on its
/// -X side. The block JSON turns it so the back panel is against the supporting wall, so a wall hammock opens away
/// from its wall. Ceiling and floor hammocks can open towards any of the four sides (HammockOpening): the two original
/// variants, silkhammock-up and silkhammock-down, open towards -X (west), the others as their names say.
///
/// Yaw is the game's convention: radians, 0 looks towards +Z (south), pi/2 towards +X (east); the yaw towards a
/// direction (dx, dz) is Atan2(dx, dz), as in the vanilla look-at and pathfinding code. The spider's model has its
/// head towards -X like vanilla animals, and the game's renderer turns it so that it looks along its yaw.
/// </summary>
public static class HammockFacing
{
    /// <summary>
    /// The yaw, in [0, 2 pi), of a spider sleeping in a hammock whose support (the block holding it) is in direction
    /// (supportX, supportY, supportZ) from the hammock: one of the six unit directions, e.g. (0, 0, -1) for a hammock
    /// on a wall to its north, (0, 1, 0) for one under a ceiling. A wall hammock looks away from its wall (the opening
    /// is ignored); a ceiling or floor hammock looks out along its opening (openingX, openingZ), and west, where
    /// silkhammock-up and silkhammock-down open (HammockOpening.DefaultOpening), when that is (0, 0).
    /// </summary>
    public static double SleepYaw(int supportX, int supportY, int supportZ, int openingX, int openingZ)
    {
        int lookX = -supportX;
        int lookZ = -supportZ;
        if (supportX == 0 && supportZ == 0)
        {
            // Ceiling or floor (or no direction at all): the hammock's opening.
            lookX = openingX;
            lookZ = openingZ;
        }
        if (lookX == 0 && lookZ == 0)
        {
            lookX = HammockOpening.DirX[HammockOpening.DefaultOpening];
            lookZ = HammockOpening.DirZ[HammockOpening.DefaultOpening];
        }
        double yaw = Math.Atan2(lookX, lookZ);
        if (yaw < 0)
        {
            yaw = yaw + 2 * Math.PI;
        }
        return yaw;
    }
}
