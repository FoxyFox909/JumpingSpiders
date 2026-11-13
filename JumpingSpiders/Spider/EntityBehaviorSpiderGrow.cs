using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace JumpingSpiders;

/// <summary>
/// The vanilla "grow" behavior (a baby turns into an adult after some in-game hours), plus one thing: our saved
/// spider state goes along to the adult. Registered as "jumpingspiders.grow" and used by the baby spider JSON
/// instead of "grow"; every JSON setting of vanilla grow (hoursToGrow, adultEntityCodes) works the same.
///
/// Why: vanilla growing creates a brand-new adult entity and copies only the generation, the birth date and the
/// texture choice to it. Without this, a caught wild baby would forget it was caught (and could despawn as an
/// adult), and its home hammock and hunger state would be lost.
///
/// The game calls BecomeAdult from vanilla code once the baby is old enough; "protected virtual" in vanilla
/// means a subclass may replace it. We add our copy and then let vanilla do the rest.
/// </summary>
public class EntityBehaviorSpiderGrow : EntityBehaviorGrow
{
    /// <summary>The code used in JSON and in SpiderRegistration.</summary>
    public const string Code = "jumpingspiders.grow";

    public EntityBehaviorSpiderGrow(Entity entity) : base(entity)
    {
    }

    protected override void BecomeAdult(Entity adult, bool keepTextureIndex)
    {
        // Must happen before base.BecomeAdult, which spawns the adult: its behaviors read the state when they start.
        SpiderAttributes.CopyStateTo(entity, adult);
        base.BecomeAdult(adult, keepTextureIndex);
    }
}
