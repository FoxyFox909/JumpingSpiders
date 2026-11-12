using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace JumpingSpiders;

/// <summary>
/// STUB created by the Foundation so the project compiles; Builder C (entity) fills it in.
///
/// The spider's own entity behavior, registered as "jumpingspiders.spider" and listed in the spider entity JSON.
/// An entity behavior is a component the game attaches to every entity of a type; it gets callbacks such as
/// OnGameTick and OnEntityReceiveDamage. The game creates it with the (Entity entity) constructor.
/// </summary>
public class EntityBehaviorJumpingSpider : EntityBehavior
{
    /// <summary>The code used in JSON and in SpiderRegistration.</summary>
    public const string Code = "jumpingspiders.spider";

    public EntityBehaviorJumpingSpider(Entity entity) : base(entity)
    {
    }

    public override string PropertyName()
    {
        return Code;
    }

    /// <summary>Called by the hunting task when this spider killed victim: sated, meal credit, hammock building.</summary>
    public void OnKilledEntity(Entity victim)
    {
    }

    /// <summary>Called by the hammock when a player breaks it while this spider sleeps in it.</summary>
    public void OnHammockBrokenWhileAsleep(IPlayer byPlayer)
    {
    }
}
