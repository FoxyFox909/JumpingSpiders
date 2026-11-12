using Vintagestory.API.Common;

namespace JumpingSpiders;

/// <summary>
/// Registers the spider's code classes with the game. Called from JumpingSpidersModSystem.Start on both the
/// server and the client, because both sides build entities and their behaviors from the entity JSON.
/// Builder C adds any further registrations here.
/// </summary>
public static class SpiderRegistration
{
    public static void Register(ICoreAPI api)
    {
        api.RegisterEntityBehaviorClass(EntityBehaviorJumpingSpider.Code, typeof(EntityBehaviorJumpingSpider));
    }
}
