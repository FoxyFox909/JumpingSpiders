using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace JumpingSpiders;

/// <summary>
/// Registers the spider's code classes with the game, and listens for kills made by spiders.
/// Called from JumpingSpidersModSystem.Start on both the server and the client, because both sides build
/// entities and their behaviors from the entity JSON.
///
/// - "jumpingspiders.spider" is EntityBehaviorJumpingSpider (kills, meals, despawn protection, info text).
/// - "jumpingspiders.grow" is EntityBehaviorSpiderGrow (vanilla growing that keeps our saved state).
/// - On the server, the game's global death event (fired for every creature that dies) tells the killing spider
///   about its kill. That covers any way a spider can kill, so the AI tasks need not report kills themselves
///   (if they do, the spider ignores the second report).
/// </summary>
public static class SpiderRegistration
{
    public static void Register(ICoreAPI api)
    {
        api.RegisterEntityBehaviorClass(EntityBehaviorJumpingSpider.Code, typeof(EntityBehaviorJumpingSpider));
        api.RegisterEntityBehaviorClass(EntityBehaviorSpiderGrow.Code, typeof(EntityBehaviorSpiderGrow));

        if (api.Side == EnumAppSide.Server)
        {
            api.Event.OnEntityDeath += OnEntityDeath;
        }
    }

    // The game calls this for every entity that dies (not for despawning), before its drops are made.
    // damageSource can be null, for example for /entity kill commands.
    private static void OnEntityDeath(Entity victim, DamageSource damageSource)
    {
        if (damageSource == null)
        {
            return;
        }
        // GetCauseEntity is the real attacker: the creature itself for a bite, the shooter for a projectile.
        Entity killer = damageSource.GetCauseEntity();
        if (killer == null || killer == victim || !SpiderAttributes.IsSpider(killer))
        {
            return;
        }
        EntityBehaviorJumpingSpider spiderBehavior = killer.GetBehavior<EntityBehaviorJumpingSpider>();
        if (spiderBehavior == null)
        {
            return;
        }
        spiderBehavior.OnKilledEntity(victim);
    }
}
