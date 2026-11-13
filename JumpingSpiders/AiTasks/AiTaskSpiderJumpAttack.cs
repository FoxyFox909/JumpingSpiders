using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace JumpingSpiders;

/// <summary>
/// The spider's signature attack, JSON code "jumpingspiders.jumpattack": stalk the target, wind up with the front
/// legs raised for a random moment, then leap at it on a ballistic arc and bite it if the spider touches it.
/// The adult entity JSON uses it twice: at players while the "aggressiveondamage" emotion state is on, and at prey
/// and same-sex rivals while hungry.
///
/// How the game calls a task (classic AI task, see AiTaskBase in the game's source): the task manager asks
/// ShouldExecute() of each task every AI tick, in list order; when one says yes and its priority beats the running
/// task's PriorityForCancel, it gets StartExecute(), then ContinueExecute(dt) every tick until that returns false,
/// then FinishExecute(). The classic base class leaves cooldowns and chances to the task, so ShouldExecute checks them.
///
/// One attack, phase by phase (AttackPhase):
/// 1. Approach: walk towards the target with the game's pathfinder. Every pounceCheckIntervalMs, if the target is
///    within pounceRange and one jump reaches it, stop and wind up. Gives up after maxApproachMs, when the path is
///    stuck or missing, or when the target gets farther than loseTargetRange.
/// 2. Wind-up: stand still for a random windUpMinMs..windUpMaxMs, turning to face the target.
/// 3. Leap: the target has moved meanwhile, so the jump is planned only now, at where the target is, led by its
///    speed (leadFactor) for the flight time. If no single jump reaches it any more, the attack ends without a jump.
///    If the target is already within biting reach, the spider bites without jumping.
/// 4. "Damage on landing": during the flight and on landing, the first moment the spider's box comes within hitReach
///    of the target's box it bites once. If the prey stepped aside the spider lands in the empty spot: a miss.
/// Every ending goes through FinishExecute, which starts the cooldown (vanilla mincooldown/maxcooldown, ms).
/// The kill itself is credited by the spider's own behavior through the game's death event.
/// </summary>
public class AiTaskSpiderJumpAttack : AiTaskBaseTargetable
{
    /// <summary>Name used in the debug log.</summary>
    public const string LogName = "jumpattack";

    /// <summary>The pathfinder stops walking this close to the target, in blocks (the spider then bites or pounces).</summary>
    public const float ApproachStopDistance = 1f;

    // ---- Knobs from the task's JSON entry (see the constructor for units and defaults) ----
    private readonly float seekingRange;
    private readonly float loseTargetRange;
    private readonly float pounceRange;
    private readonly int windUpMinMs;
    private readonly int windUpMaxMs;
    private readonly float leadFactor;
    private readonly float hitReach;
    private readonly float damage;
    private readonly int damageTier;
    private readonly EnumDamageType damageType;
    private readonly float knockbackStrength;
    private readonly bool onlyWhenHungry;
    private readonly bool sameSexRivalsOnly;
    private readonly bool targetEmotionSource;
    private readonly float moveSpeed;
    private readonly int maxApproachMs;
    private readonly int repathIntervalMs;
    private readonly int pounceCheckIntervalMs;
    private readonly int searchIntervalMs;
    private readonly float turnSpeedRadPerSec;
    private readonly string windUpAnimation;
    private readonly string walkAnimation;

    // ---- State of the current attack ----

    /// <summary>What the attack is doing now (read by debug tools).</summary>
    public AttackPhase Phase = AttackPhase.None;

    private readonly WindUp windUp;
    private readonly JumpRunner runner;
    private long nextSearchMs;
    private long approachDeadlineMs;
    private long nextRepathMs;
    private long nextPounceCheckMs;
    private bool approachFailed;
    private bool walking;
    private bool hitDone;

    public AiTaskSpiderJumpAttack(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig)
        : base(entity, taskConfig, aiConfig)
    {
        // Also read by the base classes from the same entry: priority, entityCodes (which creatures count as
        // targets; a trailing * matches any ending), mincooldown/maxcooldown (ms of rest after every attack),
        // whenInEmotionState, retaliateAttacks.

        // Blocks: how far away it notices a target.
        seekingRange = taskConfig["seekingRange"].AsFloat(8);
        // Blocks: it gives up once the target is farther than this.
        loseTargetRange = taskConfig["loseTargetRange"].AsFloat(16);
        // Blocks: it starts winding up once the target is this close (and one jump can reach it).
        pounceRange = taskConfig["pounceRange"].AsFloat(5);
        // Milliseconds: the wind-up lasts a random time between these two.
        windUpMinMs = taskConfig["windUpMinMs"].AsInt(500);
        windUpMaxMs = taskConfig["windUpMaxMs"].AsInt(1250);
        // 0..1: how far ahead of a moving target it aims (0 = where it is, 1 = where it will be at landing).
        leadFactor = taskConfig["leadFactor"].AsFloat(0.5f);
        // Blocks between the spider's box and the target's box that still count as a hit.
        hitReach = taskConfig["hitReach"].AsFloat(0.4f);
        // Health points per bite, before the world's creature damage setting.
        damage = taskConfig["damage"].AsFloat(3);
        // Armour tier the bite can get through, and the kind of damage (vanilla EnumDamageType names).
        damageTier = taskConfig["damageTier"].AsInt(0);
        damageType = EnumDamageType.PiercingAttack;
        string damageTypeName = taskConfig["damageType"].AsString(null);
        if (damageTypeName != null)
        {
            damageType = (EnumDamageType)Enum.Parse(typeof(EnumDamageType), damageTypeName, true);
        }
        // How hard a bite pushes the target away (vanilla knockback units; vanilla melee uses about 0.5 to 1).
        knockbackStrength = taskConfig["knockbackStrength"].AsFloat(0.5f);
        // true = only hunts while hungry (SpiderAttributes.IsHungry), i.e. not sated by a recent kill.
        onlyWhenHungry = taskConfig["onlyWhenHungry"].AsBool(false);
        // true = other jumping spiders are targets only if they are of the same sex. Babies never are.
        sameSexRivalsOnly = taskConfig["sameSexRivalsOnly"].AsBool(true);
        // true = attack only whoever caused the emotion state in whenInEmotionState (for example the player who broke
        // its hammock or hit it), or whoever hit it last; never anyone else that matches entityCodes.
        targetEmotionSource = taskConfig["targetEmotionSource"].AsBool(false);
        // Walking speed while stalking (vanilla movespeed units, about blocks per 1/60 s; the wolf runs at 0.04).
        moveSpeed = taskConfig["movespeed"].AsFloat(0.02f);
        // Milliseconds: longest stalk before giving up.
        maxApproachMs = taskConfig["maxApproachMs"].AsInt(10000);
        // Milliseconds: how often the walking path is re-aimed at the moving target.
        repathIntervalMs = taskConfig["repathIntervalMs"].AsInt(1000);
        // Milliseconds: how often, while stalking, it checks whether one jump would reach the target.
        pounceCheckIntervalMs = taskConfig["pounceCheckIntervalMs"].AsInt(300);
        // Milliseconds: how often it looks around for a target when idle (the search is not free).
        searchIntervalMs = taskConfig["searchIntervalMs"].AsInt(1000);
        // Degrees per second: how fast it turns to keep facing the target during the wind-up.
        turnSpeedRadPerSec = taskConfig["turnSpeedDegPerSec"].AsFloat(360) * (float)Math.PI / 180f;
        // Animation codes from the entity JSON; "" = none.
        windUpAnimation = SpiderTaskUtil.ReadAnimation(taskConfig, "windUpAnimation", "jumpwindup");
        walkAnimation = SpiderTaskUtil.ReadAnimation(taskConfig, "walkAnimation", "walk");

        windUp = new WindUp(entity);
        runner = new JumpRunner(entity);
        runner.JumpAnimation = SpiderTaskUtil.ReadAnimation(taskConfig, "jumpAnimation", "jump");
        // The pounce is one jump with the wind-up above, so the runner itself does no wind-up.
        runner.WindUpMinSeconds = 0;
        runner.WindUpMaxSeconds = 0;
    }

    /// <summary>Nothing may interrupt the spider while it is in the air.</summary>
    public override float PriorityForCancel
    {
        get
        {
            if (runner.IsFlying())
            {
                return SpiderTaskUtil.InFlightPriorityForCancel;
            }
            return base.PriorityForCancel;
        }
    }

    public override bool ShouldExecute()
    {
        long now = entity.World.ElapsedMilliseconds;
        if (now < nextSearchMs)
        {
            return false;
        }
        nextSearchMs = now + searchIntervalMs;

        if (IsOnCooldown())
        {
            return false;
        }
        if (entity.World.Rand.NextDouble() > ExecutionChance)
        {
            return false;
        }
        if (!PreconditionsSatisfied())
        {
            return false;
        }
        if (onlyWhenHungry && !SpiderAttributes.IsHungry(entity))
        {
            return false;
        }
        if (!entity.OnGround || entity.Swimming)
        {
            return false;
        }

        Entity target = FindTarget();
        if (target == null)
        {
            return false;
        }
        targetEntity = target;
        return true;
    }

    /// <summary>
    /// Who to attack: the cause of the emotion state, then whoever hit us, then (only without targetEmotionSource)
    /// the nearest fitting creature.
    /// </summary>
    private Entity FindTarget()
    {
        if (targetEmotionSource && WhenInEmotionStates != null)
        {
            for (int i = 0; i < WhenInEmotionStates.Length; i++)
            {
                Entity source = SpiderTaskUtil.GetEmotionSource(entity, WhenInEmotionStates[i]);
                if (IsReachableCandidate(source))
                {
                    return source;
                }
            }
        }
        if (RecentlyAttacked && ShouldRetaliateForRange(seekingRange) && IsReachableCandidate(attackedByEntity))
        {
            return attackedByEntity;
        }
        if (targetEmotionSource)
        {
            // An angry spider only goes for whoever angered or hit it. Without this, a wild spider bitten by something
            // it cannot reach (or whose hammock a player broke from out of range) would turn on any player nearby,
            // and wild spiders must stay passive to players who did not attack them first.
            return null;
        }
        return partitionUtil.GetNearestEntity(entity.Pos.XYZ, seekingRange, IsSearchMatch, EnumEntitySearchType.Creatures);
    }

    // A specific entity (emotion source or attacker) we may go for: alive, sensed, and within seeking range.
    private bool IsReachableCandidate(Entity candidate)
    {
        if (SpiderTaskUtil.IsGone(entity, candidate) || candidate == entity)
        {
            return false;
        }
        if (!CanSense(candidate, seekingRange))
        {
            return false;
        }
        return SpiderTaskUtil.Distance(entity, candidate) <= seekingRange;
    }

    // Matcher for the nearby-entity search (the game calls it for each creature near the spider).
    private bool IsSearchMatch(Entity candidate)
    {
        return IsTargetableEntity(candidate, seekingRange);
    }

    /// <summary>
    /// The vanilla target test (entityCodes, alive, can sense it, the world's creature hostility setting for
    /// players), plus our rules for other jumping spiders: never babies, and only the same sex when sameSexRivalsOnly.
    /// </summary>
    public override bool IsTargetableEntity(Entity e, float range)
    {
        if (!base.IsTargetableEntity(e, range))
        {
            return false;
        }
        if (SpiderAttributes.IsSpider(e))
        {
            if (!SpiderAttributes.IsAdult(e))
            {
                return false;
            }
            if (sameSexRivalsOnly && SpiderAttributes.IsMale(e) != SpiderAttributes.IsMale(entity))
            {
                return false;
            }
        }
        return true;
    }

    public override void StartExecute()
    {
        base.StartExecute();
        long now = entity.World.ElapsedMilliseconds;
        Phase = AttackPhase.Approach;
        approachDeadlineMs = now + maxApproachMs;
        nextRepathMs = 0;
        nextPounceCheckMs = 0;
        approachFailed = false;
        walking = false;
        hitDone = false;
        SpiderTaskUtil.Log(entity, LogName, "targets " + targetEntity.Code + " " + targetEntity.EntityId
            + " at " + SpiderTaskUtil.Distance(entity, targetEntity).ToString("0.00") + " blocks");
    }

    public override bool ContinueExecute(float dt)
    {
        if (Phase == AttackPhase.Leap)
        {
            return ContinueLeap(dt);
        }
        if (SpiderTaskUtil.IsGone(entity, targetEntity))
        {
            SpiderTaskUtil.Log(entity, LogName, "target gone during " + Phase);
            return false;
        }
        double distance = SpiderTaskUtil.Distance(entity, targetEntity);
        if (distance > loseTargetRange)
        {
            SpiderTaskUtil.Log(entity, LogName, "target escaped (" + distance.ToString("0.0") + " blocks)");
            return false;
        }
        if (Phase == AttackPhase.Approach)
        {
            return ContinueApproach(distance);
        }
        if (Phase == AttackPhase.WindUp)
        {
            return ContinueWindUp(dt);
        }
        return false;
    }

    private bool ContinueApproach(double distance)
    {
        long now = entity.World.ElapsedMilliseconds;
        if (approachFailed)
        {
            SpiderTaskUtil.Log(entity, LogName, "no way to walk to the target");
            return false;
        }
        if (now > approachDeadlineMs)
        {
            SpiderTaskUtil.Log(entity, LogName, "gave up stalking after " + maxApproachMs + " ms");
            return false;
        }

        if (now >= nextPounceCheckMs)
        {
            nextPounceCheckMs = now + pounceCheckIntervalMs;
            if (distance <= pounceRange && entity.OnGround && (IsWithinBite() || PlanPounce(false) != null))
            {
                BeginWindUp();
                return true;
            }
        }

        if (now >= nextRepathMs)
        {
            nextRepathMs = now + repathIntervalMs;
            WalkTowardsTarget();
        }
        return true;
    }

    private void BeginWindUp()
    {
        StopWalkingToTarget();
        windUp.Begin(windUpMinMs / 1000f, windUpMaxMs / 1000f, windUpAnimation, entity.World.Rand);
        Phase = AttackPhase.WindUp;
        SpiderTaskUtil.Log(entity, LogName, "winds up for " + windUp.DurationSeconds.ToString("0.00") + " s, target at "
            + SpiderTaskUtil.Distance(entity, targetEntity).ToString("0.00") + " blocks");
    }

    private bool ContinueWindUp(float dt)
    {
        SpiderTaskUtil.TurnTowards(entity, targetEntity.Pos.X, targetEntity.Pos.Z, turnSpeedRadPerSec, dt);
        if (!windUp.Tick(dt))
        {
            return true;
        }

        // The wind-up is over. The target has moved meanwhile: decide now what to do about where it is.
        if (IsWithinBite())
        {
            SpiderTaskUtil.Log(entity, LogName, "target within reach, bites without leaping");
            Bite();
            return false;
        }
        if (!entity.OnGround)
        {
            SpiderTaskUtil.Log(entity, LogName, "not on the ground after the wind-up, no leap");
            return false;
        }
        JumpStep step = PlanPounce(true);
        if (step == null)
        {
            SpiderTaskUtil.Log(entity, LogName, "target out of reach after the wind-up, no leap");
            return false;
        }
        List<JumpStep> plan = new List<JumpStep>();
        plan.Add(step);
        runner.Start(plan);
        Phase = AttackPhase.Leap;
        SpiderTaskUtil.Log(entity, LogName, "leaps from " + SpiderTaskUtil.Format(step.FromX, step.FromY, step.FromZ)
            + " to " + SpiderTaskUtil.Format(step.ToX, step.ToY, step.ToZ) + ", flight " + step.FlightSeconds.ToString("0.000")
            + " s; target at " + SpiderTaskUtil.Format(targetEntity.Pos.X, targetEntity.Pos.Y, targetEntity.Pos.Z));
        return true;
    }

    private bool ContinueLeap(float dt)
    {
        runner.Tick(dt);
        if (!hitDone && !SpiderTaskUtil.IsGone(entity, targetEntity) && IsWithinBite())
        {
            Bite();
        }
        if (runner.IsBusy())
        {
            return true;
        }
        string result = "missed";
        if (hitDone)
        {
            result = "hit";
        }
        SpiderTaskUtil.Log(entity, LogName, "leap over (" + runner.LastEndReason + ", " + result + "), landed at "
            + SpiderTaskUtil.Format(entity.Pos.X, entity.Pos.Y, entity.Pos.Z));
        return false;
    }

    public override void FinishExecute(bool cancelled)
    {
        windUp.Cancel();
        runner.Stop();
        StopWalkingToTarget();
        if (cancelled)
        {
            SpiderTaskUtil.Log(entity, LogName, "interrupted during " + Phase);
        }
        Phase = AttackPhase.None;
        base.FinishExecute(cancelled);
    }

    // ---- Helpers ----

    // One jump to the target's spot, or null. withLead: aim ahead of a moving target by leadFactor.
    private JumpStep PlanPounce(bool withLead)
    {
        SpiderJumpWorld world = new SpiderJumpWorld(entity);
        JumpLimits limits = SpiderTaskUtil.CreateLimits();
        double targetX = targetEntity.Pos.X;
        double targetY = targetEntity.Pos.Y;
        double targetZ = targetEntity.Pos.Z;

        JumpStep step = PlanJumpTo(world, targetX, targetY, targetZ, limits);
        if (step == null || !withLead || leadFactor <= 0)
        {
            return step;
        }

        // Motion is in blocks per 1/60 s; the planner works in blocks per second.
        double speedX = targetEntity.Pos.Motion.X * JumpMath.BlocksPerSecondPerMotionUnit;
        double speedZ = targetEntity.Pos.Motion.Z * JumpMath.BlocksPerSecondPerMotionUnit;
        double aheadX = PounceMath.LeadPosition(targetX, speedX, step.FlightSeconds, leadFactor);
        double aheadZ = PounceMath.LeadPosition(targetZ, speedZ, step.FlightSeconds, leadFactor);
        JumpStep led = PlanJumpTo(world, aheadX, targetY, aheadZ, limits);
        if (led != null)
        {
            return led;
        }
        return step;
    }

    // One jump to land at (x, z) on whatever floor is there at about height y, or null.
    private JumpStep PlanJumpTo(SpiderJumpWorld world, double x, double y, double z, JumpLimits limits)
    {
        int cellX = JumpPlanner.CellOfX(x);
        int cellY = JumpPlanner.CellOfFeetY(y);
        int cellZ = JumpPlanner.CellOfX(z);
        if (!world.CanStandAt(cellX, cellY, cellZ))
        {
            // A target in the middle of a hop: land on the floor under it.
            cellY = cellY - 1;
            if (!world.CanStandAt(cellX, cellY, cellZ))
            {
                return null;
            }
        }
        double feetY = world.GetStandHeight(cellX, cellY, cellZ);
        JumpStep step = JumpPlanner.TryJump(world, entity.Pos.X, entity.Pos.Y, entity.Pos.Z, x, feetY, z, limits);
        if (step == null)
        {
            // Right at the edge of a block the exact spot can fail the footing check; the cell centre may not.
            step = JumpPlanner.TryJump(world, entity.Pos.X, entity.Pos.Y, entity.Pos.Z, cellX + 0.5, feetY, cellZ + 0.5, limits);
        }
        return step;
    }

    // True when the spider's collision box is within hitReach of the target's.
    private bool IsWithinBite()
    {
        if (targetEntity.Pos.Dimension != entity.Pos.Dimension)
        {
            return false;
        }
        double gap = PounceMath.BoxGap(
            entity.Pos.X + entity.CollisionBox.X1, entity.Pos.Y + entity.CollisionBox.Y1, entity.Pos.Z + entity.CollisionBox.Z1,
            entity.Pos.X + entity.CollisionBox.X2, entity.Pos.Y + entity.CollisionBox.Y2, entity.Pos.Z + entity.CollisionBox.Z2,
            targetEntity.Pos.X + targetEntity.CollisionBox.X1, targetEntity.Pos.Y + targetEntity.CollisionBox.Y1, targetEntity.Pos.Z + targetEntity.CollisionBox.Z1,
            targetEntity.Pos.X + targetEntity.CollisionBox.X2, targetEntity.Pos.Y + targetEntity.CollisionBox.Y2, targetEntity.Pos.Z + targetEntity.CollisionBox.Z2);
        return gap <= hitReach;
    }

    // Deals the bite, once per attack. The spider is the damage's source entity, which is how the game's death
    // event (and so EntityBehaviorJumpingSpider.OnKilledEntity) and the carcass multiplier know who killed it.
    private void Bite()
    {
        hitDone = true;
        DamageSource source = new DamageSource();
        source.Source = EnumDamageSource.Entity;
        source.SourceEntity = entity;
        source.Type = damageType;
        source.DamageTier = damageTier;
        source.KnockbackStrength = knockbackStrength;
        bool hurt = targetEntity.ReceiveDamage(source, damage * GlobalConstants.CreatureDamageModifier);
        SpiderTaskUtil.Log(entity, LogName, "bites " + targetEntity.Code + " " + targetEntity.EntityId + " for " + damage
            + " (damage taken: " + hurt + ", target alive: " + targetEntity.Alive + ")");
    }

    private void WalkTowardsTarget()
    {
        bool started = pathTraverser.NavigateTo_Async(targetEntity.Pos.XYZ, moveSpeed, ApproachStopDistance,
            OnApproachGoalReached, OnApproachStuck, OnApproachNoPath);
        if (started && !walking)
        {
            walking = true;
            SpiderTaskUtil.StartAnimation(entity, walkAnimation);
        }
    }

    private void StopWalkingToTarget()
    {
        pathTraverser.Stop();
        if (walking)
        {
            walking = false;
            SpiderTaskUtil.StopAnimation(entity, walkAnimation);
        }
    }

    // Pathfinder callbacks (the game calls these from the traverser's tick).
    private void OnApproachGoalReached()
    {
        if (walking)
        {
            walking = false;
            SpiderTaskUtil.StopAnimation(entity, walkAnimation);
        }
    }

    private void OnApproachStuck()
    {
        approachFailed = true;
    }

    private void OnApproachNoPath()
    {
        approachFailed = true;
    }
}
