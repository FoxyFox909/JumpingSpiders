using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace JumpingSpiders;

/// <summary>
/// STUB created by the Foundation so other areas can compile against the contract; Builder B replaces it.
/// The hammock's block entity: remembers which spider sleeps in it.
/// </summary>
public class BlockEntitySilkHammock : BlockEntity
{
    /// <summary>The sleeping spider, or null. In memory only.</summary>
    public Entity Occupant;

    public bool IsFreeFor(Entity spider)
    {
        return Occupant == null || Occupant == spider;
    }

    public bool TryOccupy(Entity spider)
    {
        return false;
    }

    public void Release(Entity spider)
    {
    }

    /// <summary>Feet position for a spider resting in the hammock.</summary>
    public Vec3d GetSleepPosition()
    {
        return Pos.ToVec3d().Add(0.5, 0, 0.5);
    }
}
