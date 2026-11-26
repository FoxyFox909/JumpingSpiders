using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
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
///   hare) credits breeding saturation and, for a domesticated adult, owes one hammock: the build task
///   (AiTaskSpiderBuildHammock) then makes an attempt every in-game hour while the spider is sated, until one
///   succeeds. SpiderRegistration calls this for every kill through the game's global death event, which the game
///   fires once per death (Entity.Die does nothing for an entity that is already dead).
/// - The "live prey only" rule: the vanilla eat task (needed so a baited basket trap lures the spider) adds
///   breeding saturation for every portion it eats. This behavior takes that back at once, so only meals
///   credited here count for breeding. See SaturationGuard.
/// - Despawn protection: a spider the player caught, placed, bred or fed never despawns. See DespawnRules.
/// - OnHammockBrokenWhileAsleep: a wild spider woken by a player breaking its hammock turns on that player.
/// - OnEntityDeath: a spider that dies while held still (in a ceiling hammock's cradle, or against a wall while it
///   builds) is let go and no longer asleep; its AI tasks, which would do that, stop running at death.
/// - Info text: wild or domesticated, hungry or sated, asleep.
/// - Per-sex model tweaks (client): both adult sexes share one model file, and OnTesselation scales or reshapes the
///   elements the entity JSON lists in "elementScalesByType" (the female's thinner pedipalps). See ElementScales and
///   BoxReshape.
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

    // True while this behavior itself writes the saturation (EnforceSaturationLimit or CreditMeal), so the
    // "hunger" listener does not react to our own change.
    private bool enforcingSaturation;

    private float slowCheckAccum;

    /// <summary>
    /// Key in this behavior's JSON (the client list) for the element scale table. Written "elementScalesByType" in
    /// the JSON so each sex can have its own; the game resolves that to "elementScales" for each entity type.
    /// </summary>
    private const string ElementScalesKey = "elementScales";

    // Model elements to draw at a different size or shape than modelled, from the JSON. Empty on the server (the
    // server behavior list has no table) and for the male.
    private readonly ElementScales elementScales = new ElementScales();

    // So a misspelled element name is reported once per spider, not every time its mesh is rebuilt.
    private bool missingElementsReported;

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

        ReadElementScales(attributes);

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

    /// <summary>
    /// The game calls this when the spider dies, however it dies (also by /kill or entity.Die() without any damage).
    /// The game stops running a dead entity's AI tasks, so a task that held the spider in a ceiling hammock's cradle or
    /// against a wall never gets to undo that: it is undone here. Its physics runs again (the body drops to the floor)
    /// and it is no longer asleep.
    /// </summary>
    public override void OnEntityDeath(DamageSource damageSourceForDeath)
    {
        base.OnEntityDeath(damageSourceForDeath);
        if (entity.World.Side != EnumAppSide.Server)
        {
            return;
        }
        EntityAgent agent = entity as EntityAgent;
        if (agent != null && SpiderTaskUtil.IsPhysicsPaused(agent))
        {
            SpiderTaskUtil.SetPhysicsPaused(agent, false);
        }
        if (SpiderAttributes.IsSleeping(entity))
        {
            SpiderAttributes.SetSleeping(entity, false);
        }
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

        slowCheckAccum += deltaTime;
        if (slowCheckAccum >= SlowCheckIntervalSeconds)
        {
            slowCheckAccum = 0;
            RunSlowChecks();
        }
    }

    // ---------------- Kills and meals ----------------

    /// <summary>Called when this spider killed victim (server only): sated; meal credit; a hammock owed.</summary>
    public void OnKilledEntity(Entity victim)
    {
        if (entity.World.Side != EnumAppSide.Server || victim == null)
        {
            return;
        }
        JumpingSpidersConfig config = JumpingSpidersModSystem.Config;

        // Any kill: stop hunting prey and rivals for a while.
        SpiderAttributes.SetSatedForHours(entity, config.SatedHoursAfterKill);
        entity.WatchedAttributes.SetDouble(SpiderAttributes.LastMealHoursKey, entity.World.Calendar.TotalHours);

        bool isAdult = SpiderAttributes.IsAdult(entity);
        int victimGeneration = victim.WatchedAttributes.GetInt(SpiderAttributes.GenerationKey, 0);
        bool validMeal = MealRules.IsValidMeal(victim.Code.Path, victimGeneration, config.MealPreyCodePrefixes,
            config.MinPreyGenerationForMeal, isAdult);

        if (SpiderTaskUtil.IsDebugLogOn())
        {
            entity.World.Logger.Debug("[jumpingspiders] {0} (id {1}) killed {2} (generation {3}); valid meal: {4}",
                entity.Code, entity.EntityId, victim.Code, victimGeneration, validMeal);
        }

        if (MealRules.ShouldBuildHammock(validMeal, isAdult, SpiderAttributes.IsDomesticated(entity), config.HammockNeedsFarmPrey))
        {
            // One hammock owed for this kill. The build task makes the attempts (hourly, while sated), starting as soon
            // as the spider stands on the ground again: the killing bite is usually made in mid-leap. The time of the
            // next attempt is left as it is, so a kill never brings an attempt sooner than an hour after the last one.
            SpiderAttributes.SetHammockBuildArmed(entity, true);
        }

        if (!validMeal)
        {
            return;
        }

        CreditMeal(config.SaturationPerMeal);

        // Fed by the player's farm: keep this spider for good.
        SpiderAttributes.MarkKeptByPlayer(entity);
        UpdateDespawnProtection();

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
                if (SpiderTaskUtil.IsDebugLogOn())
                {
                    entity.World.Logger.Debug("[jumpingspiders] {0} (id {1}) ate something that was not live prey; breeding saturation kept at {2}",
                        entity.Code, entity.EntityId, allowed);
                }
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
        if (!SpiderAttributes.IsKept(entity))
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
        if (SpiderTaskUtil.IsDebugLogOn())
        {
            entity.World.Logger.Debug("[jumpingspiders] {0} (id {1}) woken by a broken hammock; aggressive: {2}",
                entity.Code, entity.EntityId, triggered);
        }
    }

    // ---------------- Model: per-sex element scales ----------------

    // Reads "elementScales" ({ "ElementName": scale or [x, y, z], ... }) from this behavior's JSON. Bad entries are
    // skipped with a warning, so a typo in the JSON never stops the spider from loading.
    private void ReadElementScales(JsonObject attributes)
    {
        if (attributes == null || !attributes[ElementScalesKey].Exists)
        {
            return;
        }

        // JsonObject wraps a token of the Newtonsoft JSON library, which the game uses to read its JSON files.
        JObject table = attributes[ElementScalesKey].Token as JObject;
        if (table == null)
        {
            entity.World.Logger.Warning("[jumpingspiders] {0}: \"{1}\" must map element names to a number or to [x, y, z]; ignored",
                entity.Code, ElementScalesKey);
            return;
        }

        foreach (JProperty entry in table.Properties())
        {
            double[] numbers = ReadNumbers(entry.Value);
            if (!elementScales.SetFromNumbers(entry.Name, numbers))
            {
                entity.World.Logger.Warning("[jumpingspiders] {0}: element scale {1} = {2} ignored (it must be one number or [x, y, z], each from {3} to {4})",
                    entity.Code, entry.Name, entry.Value.ToString(Formatting.None), ElementScales.MinScale, ElementScales.MaxScale);
            }
        }
    }

    // The numbers in one table value: a single number gives one, an array of numbers gives them all. Anything else
    // (text, an array holding something other than numbers) gives null, which ElementScales refuses.
    private static double[] ReadNumbers(JToken value)
    {
        if (IsNumber(value))
        {
            return new double[] { (double)value };
        }
        JArray array = value as JArray;
        if (array == null)
        {
            return null;
        }
        double[] numbers = new double[array.Count];
        for (int i = 0; i < array.Count; i++)
        {
            if (!IsNumber(array[i]))
            {
                return null;
            }
            numbers[i] = (double)array[i];
        }
        return numbers;
    }

    private static bool IsNumber(JToken value)
    {
        return value != null && (value.Type == JTokenType.Float || value.Type == JTokenType.Integer);
    }

    /// <summary>
    /// The game calls this just before it builds the entity's mesh from its shape (on the client, when the spider
    /// comes into view and whenever the shape is marked modified). entityShape is the shape loaded from the model
    /// file, and it is shared: the game loads each shape file once and every entity of every type that uses that
    /// file (here both adult sexes) gets the same object. So the shape is copied before it is changed, and
    /// shapeIsCloned tells the game it now holds a per-entity copy; the game then builds the mesh and this spider's
    /// animator from the copy, so the animations move the changed parts too.
    /// </summary>
    public override void OnTesselation(ref Shape entityShape, string shapePathForLogging, ref bool shapeIsCloned, ref string[] willDeleteElements)
    {
        base.OnTesselation(ref entityShape, shapePathForLogging, ref shapeIsCloned, ref willDeleteElements);

        if (elementScales.IsEmpty || entityShape == null || entityShape.Elements == null)
        {
            return;
        }

        if (!shapeIsCloned)
        {
            entityShape = entityShape.Clone();
            shapeIsCloned = true;
        }

        HashSet<string> found = new HashSet<string>(StringComparer.Ordinal);
        ResizeElements(entityShape.Elements, found);

        if (!missingElementsReported)
        {
            List<string> missing = elementScales.MissingFrom(found);
            if (missing.Count > 0)
            {
                missingElementsReported = true;
                entity.World.Logger.Warning("[jumpingspiders] {0}: the shape {1} has no element named {2}, so it cannot be resized (see elementScalesByType in the entity JSON)",
                    entity.Code, shapePathForLogging, string.Join(", ", missing));
            }
        }
    }

    // Walks the element tree, parents before children (a reshaped parent moves its children before they are
    // reshaped themselves).
    // - A uniform number multiplies the element's scale. Its children are scaled along with it by the game (a child
    //   is placed inside its parent), so they need no entry of their own; a child that is listed too gets its own
    //   number on top of its parent's.
    // - Per-axis factors reshape the element's box (see BoxReshape). The children keep their size and are moved to
    //   stay attached; a child that is listed too is reshaped in its own axes.
    private void ResizeElements(ShapeElement[] elements, HashSet<string> found)
    {
        if (elements == null)
        {
            return;
        }
        for (int i = 0; i < elements.Length; i++)
        {
            ShapeElement element = elements[i];
            double factor;
            double[] boxFactors;
            if (elementScales.TryGetScale(element.Name, out factor))
            {
                found.Add(element.Name);
                element.ScaleX = ElementScales.Combine(element.ScaleX, factor);
                element.ScaleY = ElementScales.Combine(element.ScaleY, factor);
                element.ScaleZ = ElementScales.Combine(element.ScaleZ, factor);
                // Each element remembers the inverse of its resting position (parents included), which the animator
                // uses to move the mesh. The copy inherited the unscaled one; clearing it makes the game work it out
                // again from the new scale when it loads the animator. Without this the parts would be scaled twice.
                ClearInverseTransforms(element);
            }
            else if (elementScales.TryGetBoxFactors(element.Name, out boxFactors))
            {
                found.Add(element.Name);
                ReshapeBox(element, boxFactors);
                // The resting position changed for the element and its moved children, as above.
                ClearInverseTransforms(element);
            }
            ResizeElements(element.Children, found);
        }
    }

    // Moves the element's From and To toward its rotation origin by the factors, per axis of the element's own box,
    // and moves each direct child (its From, To and rotation origin) so its joint keeps its place on the new box.
    // Grandchildren are placed from the child's From, so they move with the child. New arrays are assigned rather
    // than the old ones changed, so nothing that might share an array with the loaded shape is touched.
    private static void ReshapeBox(ShapeElement element, double[] factors)
    {
        if (element.From == null || element.To == null)
        {
            return;
        }
        element.From = BoxReshape.ReshapeCorner(element.From, element.RotationOrigin, factors);
        element.To = BoxReshape.ReshapeCorner(element.To, element.RotationOrigin, factors);

        if (element.Children == null)
        {
            return;
        }
        for (int i = 0; i < element.Children.Length; i++)
        {
            ShapeElement child = element.Children[i];
            double[] shift = BoxReshape.ChildShift(child.RotationOrigin, factors);
            if (child.From != null)
            {
                child.From = BoxReshape.Add(child.From, shift);
            }
            if (child.To != null)
            {
                child.To = BoxReshape.Add(child.To, shift);
            }
            if (child.RotationOrigin != null)
            {
                child.RotationOrigin = BoxReshape.Add(child.RotationOrigin, shift);
            }
        }
    }

    // Clears the remembered inverse resting position of the element and everything attached below it.
    private static void ClearInverseTransforms(ShapeElement element)
    {
        element.inverseModelTransform = null;
        if (element.Children == null)
        {
            return;
        }
        for (int i = 0; i < element.Children.Length; i++)
        {
            ClearInverseTransforms(element.Children[i]);
        }
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
