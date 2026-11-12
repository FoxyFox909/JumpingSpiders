using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace JumpingSpiders;

/// <summary>
/// Escape by jumping, JSON code "jumpingspiders.jumpflee": no wind-up and no damage, one quick jump after another
/// straight away from the danger, running in between when no jump is possible. The adult and baby JSON use it twice:
/// from the attacker while the "fleeondamage" emotion state is on, and (wild spiders only) from players who come
/// too close.
///
/// (How the game drives a task: see AiTaskSpiderJumpAttack.)
///
/// One flee "session" lasts until the threat is safeDistance away, the threat is gone, or fleeDurationMs is over:
/// - On the ground, it plans one jump to a random spot in a ball of landingRadius around the point jumpDistance
///   straight away from the threat (FleeDirection), and only to spots that are at least minDistanceGain farther from
///   the threat than the spider is now (LandingSpotFilter), so every jump gains ground. heightBias favours higher spots.
/// - If there is none, it runs straight away at movespeed and tries again after replanIntervalMs.
/// - After each landing it waits pauseBetweenJumpsMs before the next jump.
/// The cooldown (vanilla mincooldown/maxcooldown, ms) starts when the session ends; in the meantime the vanilla
/// "fleeentity" task in the JSON keeps a wild spider running from players.
/// </summary>
public class AiTaskSpiderJumpFlee : AiTaskBaseTargetable
{
    /// <summary>Name used in the debug log.</summary>
    public const string LogName = "jumpflee";

    /// <summary>When running, the spider stops once within this many blocks of the point it runs to.</summary>
    public const float RunStopDistance = 0.5f;

    // ---- Knobs from the task's JSON entry (see the constructor for units and defaults) ----
    private readonly float seekingRange;
    private readonly float safeDistance;
    private readonly bool fleeFromEmotionSource;
    private readonly int fleeDurationMs;
    private readonly float jumpDistance;
    private readonly float landingRadius;
    private readonly float minDistanceGain;
    private readonly float heightBias;
    private readonly int pauseBetweenJumpsMs;
    private readonly int replanIntervalMs;
    private readonly float moveSpeed;
    private readonly int searchIntervalMs;
    private readonly string runAnimation;

    // ---- State of the current flee session ----
    private readonly JumpRunner runner;
    private long nextSearchMs;
    private long fleeUntilMs;
    private long nextJumpMs;
    private bool running;
    private bool wasJumping;

    /// <summary>How many flee jumps this session made (read by debug tools).</summary>
    public int JumpsThisSession;

    /// <summary>Fleeing is not an attack, so the world's "creature hostility" setting does not switch it off.</summary>
    public override bool AggressiveTargeting
    {
        get { return false; }
    }

    public AiTaskSpiderJumpFlee(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig)
        : base(entity, taskConfig, aiConfig)
    {
        // Also read by the base classes from the same entry: priority, entityCodes (what to flee from),
        // mincooldown/maxcooldown (ms of rest after a session), whenInEmotionState, maxGeneration.

        // Blocks: a threat closer than this starts a flee session (not used with fleeFromEmotionSource).
        seekingRange = taskConfig["seekingRange"].AsFloat(6);
        // Blocks: the session ends once the threat is this far away (horizontally).
        safeDistance = taskConfig["safeDistance"].AsFloat(12);
        // true = flee from whoever caused the emotion state in whenInEmotionState (the attacker), whatever it is.
        fleeFromEmotionSource = taskConfig["fleeFromEmotionSource"].AsBool(false);
        // Milliseconds: longest flee session.
        fleeDurationMs = taskConfig["fleeDurationMs"].AsInt(6000);
        // Blocks: how far from the spider, straight away from the threat, the landing area is centred.
        jumpDistance = taskConfig["jumpDistance"].AsFloat(4.5f);
        // Blocks: radius of the ball around that point in which a landing spot is picked.
        landingRadius = taskConfig["landingRadius"].AsFloat(3);
        // Blocks: every landing spot must be at least this much farther from the threat than the spider is now.
        minDistanceGain = taskConfig["minDistanceGain"].AsFloat(1.5f);
        // Preference for high landing spots (0 = none; see HeightWeightedPicker).
        heightBias = taskConfig["heightBias"].AsFloat(1);
        // Milliseconds: rest after each landing before the next jump.
        pauseBetweenJumpsMs = taskConfig["pauseBetweenJumpsMs"].AsInt(150);
        // Milliseconds: when no jump was found, run this long before planning again (bounds the planning cost).
        replanIntervalMs = taskConfig["replanIntervalMs"].AsInt(500);
        // Running speed between jumps (vanilla movespeed units; the vanilla fleeentity entry uses 0.04).
        moveSpeed = taskConfig["movespeed"].AsFloat(0.04f);
        // Milliseconds: how often it looks for a threat while not fleeing.
        searchIntervalMs = taskConfig["searchIntervalMs"].AsInt(250);
        // Animation codes from the entity JSON; "" = none.
        runAnimation = SpiderTaskUtil.ReadAnimation(taskConfig, "runAnimation", "run");

        runner = new JumpRunner(entity);
        runner.JumpAnimation = SpiderTaskUtil.ReadAnimation(taskConfig, "jumpAnimation", "jump");
        // Fleeing jumps have no wind-up: the spider leaves at once.
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
        Entity threat = FindThreat();
        if (threat == null)
        {
            return false;
        }
        targetEntity = threat;
        return true;
    }

    // The emotion state's cause (or whoever hit us) with fleeFromEmotionSource, else the nearest creature from entityCodes.
    private Entity FindThreat()
    {
        if (fleeFromEmotionSource)
        {
            if (WhenInEmotionStates != null)
            {
                for (int i = 0; i < WhenInEmotionStates.Length; i++)
                {
                    Entity source = SpiderTaskUtil.GetEmotionSource(entity, WhenInEmotionStates[i]);
                    if (IsCloseThreat(source))
                    {
                        return source;
                    }
                }
            }
            if (RecentlyAttacked && IsCloseThreat(attackedByEntity))
            {
                return attackedByEntity;
            }
            return null;
        }
        return partitionUtil.GetNearestEntity(entity.Pos.XYZ, seekingRange, IsSearchMatch, EnumEntitySearchType.Creatures);
    }

    private bool IsCloseThreat(Entity candidate)
    {
        if (SpiderTaskUtil.IsGone(entity, candidate) || candidate == entity)
        {
            return false;
        }
        return SpiderTaskUtil.HorizontalDistance(entity, candidate) < safeDistance;
    }

    // Matcher for the nearby-entity search: the vanilla test (entityCodes, alive, sensed; players in survival mode).
    private bool IsSearchMatch(Entity candidate)
    {
        return IsTargetableEntity(candidate, seekingRange);
    }

    public override void StartExecute()
    {
        base.StartExecute();
        fleeUntilMs = entity.World.ElapsedMilliseconds + fleeDurationMs;
        nextJumpMs = 0;
        running = false;
        wasJumping = false;
        JumpsThisSession = 0;
        SpiderTaskUtil.Log(entity, LogName, "flees from " + targetEntity.Code + " " + targetEntity.EntityId + " at "
            + SpiderTaskUtil.HorizontalDistance(entity, targetEntity).ToString("0.00") + " blocks");
    }

    public override bool ContinueExecute(float dt)
    {
        long now = entity.World.ElapsedMilliseconds;

        if (runner.IsBusy())
        {
            runner.Tick(dt);
            if (runner.IsBusy())
            {
                return true;
            }
        }
        if (wasJumping)
        {
            wasJumping = false;
            nextJumpMs = now + pauseBetweenJumpsMs;
            string distanceText = "?";
            if (!SpiderTaskUtil.IsGone(entity, targetEntity))
            {
                distanceText = SpiderTaskUtil.HorizontalDistance(entity, targetEntity).ToString("0.00");
            }
            SpiderTaskUtil.Log(entity, LogName, "flee jump " + JumpsThisSession + " over (" + runner.LastEndReason
                + "), now " + distanceText + " blocks from the threat");
        }

        if (SpiderTaskUtil.IsGone(entity, targetEntity))
        {
            SpiderTaskUtil.Log(entity, LogName, "threat gone");
            return false;
        }
        double distance = SpiderTaskUtil.HorizontalDistance(entity, targetEntity);
        if (distance >= safeDistance)
        {
            SpiderTaskUtil.Log(entity, LogName, "safe at " + distance.ToString("0.00") + " blocks after " + JumpsThisSession + " jumps");
            return false;
        }
        if (now > fleeUntilMs)
        {
            SpiderTaskUtil.Log(entity, LogName, "flee time over at " + distance.ToString("0.00") + " blocks after " + JumpsThisSession + " jumps");
            return false;
        }
        if (!entity.OnGround || now < nextJumpMs)
        {
            return true;
        }

        List<JumpStep> plan = PlanFleeJump(distance);
        if (plan != null)
        {
            StopRunning();
            runner.Start(plan);
            wasJumping = true;
            JumpsThisSession++;
            return true;
        }
        nextJumpMs = now + replanIntervalMs;
        StartRunning();
        return true;
    }

    public override void FinishExecute(bool cancelled)
    {
        runner.Stop();
        StopRunning();
        if (cancelled)
        {
            SpiderTaskUtil.Log(entity, LogName, "interrupted after " + JumpsThisSession + " jumps");
        }
        base.FinishExecute(cancelled);
    }

    // One jump to a spot farther from the threat, or null when there is none.
    private List<JumpStep> PlanFleeJump(double currentDistance)
    {
        LandingSpotFilter world = new LandingSpotFilter(new SpiderJumpWorld(entity));
        world.SetThreat(targetEntity.Pos.X, targetEntity.Pos.Z, currentDistance + minDistanceGain);

        double centreX;
        double centreZ;
        FleeDirection.AwayPoint(entity.Pos.X, entity.Pos.Z, targetEntity.Pos.X, targetEntity.Pos.Z, jumpDistance,
            entity.Pos.Yaw, out centreX, out centreZ);
        return JumpPlanner.PlanJumpToRandomSpot(world, entity.Pos.X, entity.Pos.Y, entity.Pos.Z,
            centreX, entity.Pos.Y, centreZ, landingRadius, heightBias, SpiderTaskUtil.CreateLimits(), entity.World.Rand);
    }

    // Runs in a straight line away from the threat (the vanilla traverser's WalkTowards, no pathfinding).
    private void StartRunning()
    {
        double awayX;
        double awayZ;
        FleeDirection.AwayPoint(entity.Pos.X, entity.Pos.Z, targetEntity.Pos.X, targetEntity.Pos.Z, safeDistance,
            entity.Pos.Yaw, out awayX, out awayZ);
        pathTraverser.WalkTowards(new Vec3d(awayX, entity.Pos.Y, awayZ), moveSpeed, RunStopDistance, OnRunEnded, OnRunEnded);
        if (!running)
        {
            running = true;
            SpiderTaskUtil.StartAnimation(entity, runAnimation);
        }
    }

    private void StopRunning()
    {
        pathTraverser.Stop();
        if (running)
        {
            running = false;
            SpiderTaskUtil.StopAnimation(entity, runAnimation);
        }
    }

    // Pathfinder callback for both "arrived" and "stuck": stop the run animation; the next plan or run follows.
    private void OnRunEnded()
    {
        if (running)
        {
            running = false;
            SpiderTaskUtil.StopAnimation(entity, runAnimation);
        }
    }
}
