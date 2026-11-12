using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace JumpingSpiders;

/// <summary>
/// The spider's own entity behavior, registered as "jumpingspiders.spider" and listed in both the client and the
/// server behavior lists of the spider entity JSON.
///
/// An entity behavior is a component the game attaches to every entity of a type. The game creates one per
/// entity with the (Entity entity) constructor, calls Initialize once the entity exists, and then calls hooks
/// such as OnGameTick (every server tick, about 30 times a second) and GetInfoText (on the client, for the
/// text a player sees when looking at the spider).
///
/// Its jobs:
/// - OnKilledEntity: the spider killed something. Any kill makes it sated; a valid meal (farm-born chicken or
///   hare) credits breeding saturation and makes a domesticated adult build a hammock (as soon as it stands on the
///   ground again: the killing bite is usually made in mid-leap). SpiderRegistration calls
///   this for every kill through the game's global death event; AI tasks may call it too (a repeat call for the
///   same victim is ignored).
/// - The "live prey only" rule: the vanilla eat task (needed so a baited basket trap lures the spider) adds
///   breeding saturation for every portion it eats. This behavior takes that back at once, so only meals
///   credited here count for breeding. See SaturationGuard.
/// - Despawn protection: a spider the player caught, placed, bred or fed never despawns. See DespawnRules.
/// - OnHammockBrokenWhileAsleep: a wild spider woken by a player breaking its hammock turns on that player.
/// - Info text: wild or domesticated, hungry or sated, asleep.
/// </summary>
public class EntityBehaviorJumpingSpider : EntityBehavior
{
    /// <summary>The code used in JSON and in SpiderRegistration.</summary>
    public const string Code = "jumpingspiders.spider";

    /// <summary>Emotion state (vanilla "emotionstates" behavior) that makes a wild spider attack players.</summary>
    public const string AggressiveStateCode = "aggressiveondamage";

    /// <summary>PropertyName of the vanilla "despawn" behavior, which is how GetBehavior finds it.</summary>
    private const string DespawnBehaviorName = "timeddespawn";

    /// <summary>
    /// How often, in real seconds, the slow bookkeeping runs (domesticated flag for clients, despawn protection).
    /// Not a design knob: it only bounds how late a change made by a debug command shows up.
    /// </summary>
    private const float SlowCheckIntervalSeconds = 2f;

    /// <summary>Despawn timer used for kept spiders: so large that it never runs out, in real seconds.</summary>
    private const float NeverDespawnSeconds = float.MaxValue;

    // Entity id of the last victim credited, so a second report of the same kill is ignored. In memory only:
    // a kill is reported within the same tick, long before any save.
    private long lastCreditedVictimId = -1;

    // True while this behavior itself writes the saturation (EnforceSaturationLimit or CreditMeal), so the
    // "hunger" listener does not react to our own change.
    private bool enforcingSaturation;

    private float slowCheckAccum;

    // True after a meal that earns a hammock, until the spider is on the ground and builds it. In memory only: a
    // spider is airborne for a second at most, so a save in between (which drops the build) is very unlikely.
    private bool hammockBuildPending;

    public EntityBehaviorJumpingSpider(Entity entity) : base(entity)
    {
    }

    public override string PropertyName()
    {
        return Code;
    }

    public override void Initialize(EntityProperties properties, JsonObject attributes)
    {
        base.Initialize(properties, attributes);

        if (entity.World.Side != EnumAppSide.Server)
        {
            return;
        }

        // A spider that never had a credit record (its first load) keeps whatever it has: for a new spider that is 0.
        if (!SpiderAttributes.HasCreditedSaturation(entity))
        {
            SpiderAttributes.SetCreditedSaturation(entity, SpiderAttributes.GetBreedingSaturation(entity));
        }

        // A modified-listener runs whenever someone calls WatchedAttributes.MarkPathDirty("hunger"), which the
        // vanilla eat task does right after adding saturation. So eaten bait is taken back in the same moment,
        // before a basket trap stores the caught spider and before breeding can see it.
        entity.WatchedAttributes.RegisterModifiedListener(SpiderAttributes.HungerTreeName, OnHungerModified);
    }

    public override void AfterInitialized(bool onFirstSpawn)
    {
        base.AfterInitialized(onFirstSpawn);
        if (entity.World.Side != EnumAppSide.Server)
        {
            return;
        }
        RunSlowChecks();
    }

    public override void OnEntityDespawn(EntityDespawnData despawn)
    {
        if (entity.World.Side == EnumAppSide.Server)
        {
            entity.WatchedAttributes.UnregisterListener(OnHungerModified);
        }
        base.OnEntityDespawn(despawn);
    }

    public override void OnGameTick(float deltaTime)
    {
        if (entity.World.Side != EnumAppSide.Server || !entity.Alive)
        {
            return;
        }

        // Every tick: vanilla breeding lowers saturation without telling the listener, so the credit has to follow
        // it down before the next meal is eaten. This behavior is listed before "taskai" in the JSON so this runs
        // before the eat task in the same tick. It is two attribute reads and a compare.
        EnforceSaturationLimit();

        // A meal made while airborne: build once the spider has landed. A spider that ends up in water gives up.
        if (hammockBuildPending)
        {
            if (entity.OnGround)
            {
                BuildPendingHammock();
            }
            else if (entity.Swimming || entity.FeetInLiquid)
            {
                hammockBuildPending = false;
            }
        }

        slowCheckAccum += deltaTime;
        if (slowCheckAccum >= SlowCheckIntervalSeconds)
        {
            slowCheckAccum = 0;
            RunSlowChecks();
        }
    }

    // ---------------- Kills and meals ----------------

    /// <summary>Called when this spider killed victim (server only): sated; meal credit; hammock building.</summary>
    public void OnKilledEntity(Entity victim)
    {
        if (entity.World.Side != EnumAppSide.Server || victim == null)
        {
            return;
        }
        if (victim.EntityId == lastCreditedVictimId)
        {
            return;
        }
        lastCreditedVictimId = victim.EntityId;

        JumpingSpidersConfig config = JumpingSpidersModSystem.Config;

        // Any kill: stop hunting prey and rivals for a while.
        SpiderAttributes.SetSatedForHours(entity, config.SatedHoursAfterKill);
        entity.WatchedAttributes.SetDouble(SpiderAttributes.LastMealHoursKey, entity.World.Calendar.TotalHours);

        bool isAdult = SpiderAttributes.IsAdult(entity);
        int victimGeneration = victim.WatchedAttributes.GetInt(SpiderAttributes.GenerationKey, 0);
        bool validMeal = MealRules.IsValidMeal(victim.Code.Path, victimGeneration, config.MealPreyCodePrefixes,
            config.MinPreyGenerationForMeal, isAdult);

        entity.World.Logger.Debug("[jumpingspiders] {0} (id {1}) killed {2} (generation {3}); valid meal: {4}",
            entity.Code, entity.EntityId, victim.Code, victimGeneration, validMeal);

        if (!validMeal)
        {
            return;
        }

        CreditMeal(config.SaturationPerMeal);

        // Fed by the player's farm: keep this spider for good.
        SpiderAttributes.MarkKeptByPlayer(entity);
        UpdateDespawnProtection();

        if (MealRules.ShouldBuildHammock(validMeal, isAdult, SpiderAttributes.IsDomesticated(entity)))
        {
            // The bite usually lands while the spider is still in the air. The builder checks reachability with the
            // jump planner from the spider's feet, and planning from mid-air would accept spots that are out of reach
            // from the ground, so the build waits until the spider stands again (see OnGameTick).
            hammockBuildPending = true;
            if (entity.OnGround)
            {
                BuildPendingHammock();
            }
        }
    }

    // Builds the hammock a meal earned. Called once the spider stands on the ground.
    private void BuildPendingHammock()
    {
        hammockBuildPending = false;
        EntityAgent agent = entity as EntityAgent;
        if (agent == null)
        {
            return;
        }
        bool built = HammockBuilder.TryBuildHammockFor(agent);
        entity.World.Logger.Debug("[jumpingspiders] {0} (id {1}) tried to build a hammock after a meal: {2}",
            entity.Code, entity.EntityId, built);
    }

    // Adds portions to the vanilla breeding saturation, as a real meal the guard lets through.
    private void CreditMeal(float portions)
    {
        // First settle anything the guard has not seen yet, so the credit starts from the right value.
        EnforceSaturationLimit();

        float credited = SpiderAttributes.GetCreditedSaturation(entity) + portions;

        // Writing the saturation runs the "hunger" listener, possibly even before the new value is in place (when
        // the hunger tree is created first). The flag makes the guard stand aside while we write both values.
        enforcingSaturation = true;
        try
        {
            SpiderAttributes.SetCreditedSaturation(entity, credited);
            SpiderAttributes.SetBreedingSaturation(entity, credited);
        }
        finally
        {
            enforcingSaturation = false;
        }
    }

    // ---------------- Live prey only (saturation guard) ----------------

    // The game calls this (through RegisterModifiedListener) whenever something marks "hunger" as changed.
    private void OnHungerModified()
    {
        EnforceSaturationLimit();
    }

    // Holds the vanilla breeding saturation to what real meals earned (see SaturationGuard).
    private void EnforceSaturationLimit()
    {
        if (enforcingSaturation)
        {
            return;
        }
        enforcingSaturation = true;
        try
        {
            float current = SpiderAttributes.GetBreedingSaturation(entity);
            float credited = SpiderAttributes.GetCreditedSaturation(entity);
            float allowed = SaturationGuard.AllowedSaturation(current, credited);

            if (SaturationGuard.NeedsCorrection(current, credited))
            {
                SpiderAttributes.SetBreedingSaturation(entity, allowed);
                entity.World.Logger.Debug("[jumpingspiders] {0} (id {1}) ate something that was not live prey; breeding saturation kept at {2}",
                    entity.Code, entity.EntityId, allowed);
            }
            if (allowed != credited)
            {
                SpiderAttributes.SetCreditedSaturation(entity, allowed);
            }
        }
        finally
        {
            enforcingSaturation = false;
        }
    }

    // ---------------- Domesticated flag and despawn protection ----------------

    private void RunSlowChecks()
    {
        SpiderAttributes.SetSyncedDomesticated(entity, SpiderAttributes.IsDomesticated(entity));
        UpdateDespawnProtection();
    }

    // A kept spider gets a despawn timer that never runs out. The vanilla despawn behavior itself stays, so wild
    // spiders despawn exactly as its JSON settings say. (Removing a behavior while the game iterates the list
    // would break; changing its timer is what vanilla does for dropped player items.)
    private void UpdateDespawnProtection()
    {
        bool kept = DespawnRules.IsKeptByPlayer(SpiderAttributes.GetGeneration(entity), SpiderAttributes.GetOrigin(entity),
            SpiderAttributes.IsMarkedKeptByPlayer(entity));
        if (!kept)
        {
            return;
        }

        // Remember it, so it stays kept after growing up (the origin attribute is not carried over).
        SpiderAttributes.MarkKeptByPlayer(entity);

        ITimedDespawn despawn = entity.GetBehavior(DespawnBehaviorName) as ITimedDespawn;
        if (despawn != null && despawn.DespawnSeconds != NeverDespawnSeconds)
        {
            despawn.DespawnSeconds = NeverDespawnSeconds;
        }
    }

    // ---------------- Hammock ----------------

    /// <summary>
    /// Called by the hammock (server) when a player breaks it while this spider sleeps in it. The spider wakes up;
    /// a wild one also turns aggressive toward players, like after being hit. A domesticated one just wakes up.
    /// </summary>
    public void OnHammockBrokenWhileAsleep(IPlayer byPlayer)
    {
        if (entity.World.Side != EnumAppSide.Server || !entity.Alive)
        {
            return;
        }

        SpiderAttributes.SetSleeping(entity, false);

        if (SpiderAttributes.IsDomesticated(entity))
        {
            return;
        }

        EntityBehaviorEmotionStates emotions = entity.GetBehavior<EntityBehaviorEmotionStates>();
        if (emotions == null)
        {
            return;
        }

        long playerEntityId = 0;
        if (byPlayer != null && byPlayer.Entity != null)
        {
            playerEntityId = byPlayer.Entity.EntityId;
        }

        // The random value 0 passes the state's "chance" roll, so breaking the hammock always angers the spider.
        // The state's own "maxGeneration" (0 in the JSON) still applies.
        bool triggered = emotions.TryTriggerState(AggressiveStateCode, 0.0, playerEntityId);
        entity.World.Logger.Debug("[jumpingspiders] {0} (id {1}) woken by a broken hammock; aggressive: {2}",
            entity.Code, entity.EntityId, triggered);
    }

    // ---------------- Info text ----------------

    /// <summary>Lines shown under the spider's name when a player looks at it (runs on the client).</summary>
    public override void GetInfoText(StringBuilder infotext)
    {
        if (!entity.Alive)
        {
            return;
        }

        if (SpiderAttributes.GetSyncedDomesticated(entity))
        {
            infotext.AppendLine(Lang.Get("jumpingspiders:jumpingspider-infotext-domesticated"));
        }
        else
        {
            infotext.AppendLine(Lang.Get("jumpingspiders:jumpingspider-infotext-wild"));
        }

        if (SpiderAttributes.IsAdult(entity))
        {
            if (SpiderAttributes.IsHungry(entity))
            {
                infotext.AppendLine(Lang.Get("jumpingspiders:jumpingspider-infotext-hungry"));
            }
            else
            {
                infotext.AppendLine(Lang.Get("jumpingspiders:jumpingspider-infotext-sated"));
            }
        }

        if (SpiderAttributes.IsSleeping(entity))
        {
            infotext.AppendLine(Lang.Get("jumpingspiders:jumpingspider-infotext-asleep"));
        }

        base.GetInfoText(infotext);
    }
}
