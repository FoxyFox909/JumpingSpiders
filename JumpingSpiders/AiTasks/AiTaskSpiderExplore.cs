using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace JumpingSpiders;

/// <summary>
/// Curious hopping around, JSON code "jumpingspiders.explore": a domesticated spider now and then makes a short
/// outing of one or more hops to random spots nearby, preferring high ones (spiders like a lookout), with a pause to
/// look around between hops. The adult and baby JSON give it minGeneration 1, so only domesticated spiders explore.
///
/// (How the game drives a task: see AiTaskSpiderJumpAttack.)
///
/// How curious it is drives everything: SpiderAttributes.GetCuriosity gives today's roll (Config.CuriosityMin..Max),
/// already multiplied by Config.CuriosityNightMultiplier at night.
/// - Every decisionIntervalMs it starts an outing with probability exploreChance * curiosity (and only when the
///   cooldown is over and a hop is possible).
/// - After each hop it goes on with probability continueChance * curiosity, at most maxHops hops per outing.
/// - Each hop lands in a ball of landingRadius around the spider; heightBias favours higher spots.
/// - Territory: hops never land farther than territoryRadius (horizontally) from an anchor point, so the spider
///   explores one area (its enclosure) instead of drifting away. The anchor is where it first explored after being
///   loaded; it moves to the spider if the spider ends up far outside it (carried, or walked away). 0 = no limit.
///   This does not stop it hopping over a low fence inside that radius. Because it likes high spots it also climbs
///   perch by perch (tested: a 3-high perch next to a 7-high wall took it onto the wall top), so an enclosure needs
///   a roof, or walls more than Config.MaxJumpUp higher than anything a spider can stand on inside.
/// Every hop has a short wind-up. The cooldown (vanilla mincooldown/maxcooldown, ms) starts after each outing.
/// </summary>
public class AiTaskSpiderExplore : AiTaskBase
{
    /// <summary>Name used in the debug log.</summary>
    public const string LogName = "explore";

    // ---- Knobs from the task's JSON entry (see the constructor for units and defaults) ----
    private readonly int decisionIntervalMs;
    private readonly float exploreChance;
    private readonly float continueChance;
    private readonly int maxHops;
    private readonly float landingRadius;
    private readonly float heightBias;
    private readonly float territoryRadius;
    private readonly int pauseMinMs;
    private readonly int pauseMaxMs;

    // ---- State ----
    private readonly JumpRunner runner;
    private long nextDecisionMs;
    private List<JumpStep> pendingPlan;
    private double outingCuriosity;
    private int hopsDone;
    private bool hopping;
    private bool pausing;
    private long pauseUntilMs;
    private bool hasAnchor;
    private double anchorX;
    private double anchorZ;

    public AiTaskSpiderExplore(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig)
        : base(entity, taskConfig, aiConfig)
    {
        // Also read by the base class from the same entry: priority, mincooldown/maxcooldown (ms of rest after an
        // outing), minGeneration.

        // Milliseconds: how often it considers starting an outing.
        decisionIntervalMs = taskConfig["decisionIntervalMs"].AsInt(4000);
        // 0..1: chance per decision to start an outing, at curiosity 1 (multiplied by the curiosity).
        exploreChance = taskConfig["exploreChance"].AsFloat(0.6f);
        // 0..1: chance after each hop to hop on, at curiosity 1 (multiplied by the curiosity).
        continueChance = taskConfig["continueChance"].AsFloat(0.75f);
        // Most hops in one outing.
        maxHops = taskConfig["maxHops"].AsInt(4);
        // Blocks: each hop lands within this distance of the spider.
        landingRadius = taskConfig["landingRadius"].AsFloat(6);
        // Preference for high landing spots: weight = (height above the lowest candidate + 1) ^ heightBias.
        heightBias = taskConfig["heightBias"].AsFloat(2);
        // Blocks: hops stay within this distance (horizontally) of the anchor point; 0 = anywhere.
        territoryRadius = taskConfig["territoryRadius"].AsFloat(12);
        // Milliseconds: look-around pause between hops, a random time between these two.
        pauseMinMs = taskConfig["pauseMinMs"].AsInt(800);
        pauseMaxMs = taskConfig["pauseMaxMs"].AsInt(3000);

        runner = new JumpRunner(entity);
        // Milliseconds: the short crouch before each hop, a random time between these two (0 and 0 = none).
        runner.WindUpMinSeconds = taskConfig["windUpMinMs"].AsInt(250) / 1000f;
        runner.WindUpMaxSeconds = taskConfig["windUpMaxMs"].AsInt(600) / 1000f;
        // Degrees per second: how fast it turns towards the hop direction during the crouch.
        runner.TurnSpeedRadPerSec = taskConfig["turnSpeedDegPerSec"].AsFloat(360) * (float)Math.PI / 180f;
        // Animation codes from the entity JSON; "" = none.
        runner.WindUpAnimation = SpiderTaskUtil.ReadAnimation(taskConfig, "windUpAnimation", "jumpwindup");
        runner.JumpAnimation = SpiderTaskUtil.ReadAnimation(taskConfig, "jumpAnimation", "jump");
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
        if (now < nextDecisionMs)
        {
            return false;
        }
        nextDecisionMs = now + decisionIntervalMs;

        if (IsOnCooldown())
        {
            return false;
        }
        if (!PreconditionsSatisfied())
        {
            return false;
        }
        if (!entity.OnGround || entity.Swimming)
        {
            return false;
        }
        double curiosity = SpiderAttributes.GetCuriosity(entity);
        if (entity.World.Rand.NextDouble() > exploreChance * curiosity)
        {
            return false;
        }
        pendingPlan = PlanHop();
        if (pendingPlan == null)
        {
            return false;
        }
        outingCuriosity = curiosity;
        return true;
    }

    public override void StartExecute()
    {
        base.StartExecute();
        hopsDone = 0;
        pausing = false;
        SpiderTaskUtil.Log(entity, LogName, "starts an outing (curiosity " + outingCuriosity.ToString("0.00") + ")");
        StartHop(pendingPlan);
        pendingPlan = null;
    }

    public override bool ContinueExecute(float dt)
    {
        if (runner.IsBusy())
        {
            runner.Tick(dt);
            if (runner.IsBusy())
            {
                return true;
            }
        }

        if (hopping)
        {
            // A hop just ended.
            hopping = false;
            SpiderTaskUtil.Log(entity, LogName, "hop " + (hopsDone + 1) + " over (" + runner.State + ", " + runner.LastEndReason
                + ") at " + SpiderTaskUtil.Format(entity.Pos.X, entity.Pos.Y, entity.Pos.Z));
            if (runner.State != JumpRunState.Reached)
            {
                return false;
            }
            hopsDone++;
            if (hopsDone >= maxHops)
            {
                return false;
            }
            if (entity.World.Rand.NextDouble() > continueChance * outingCuriosity)
            {
                return false;
            }
            pausing = true;
            pauseUntilMs = entity.World.ElapsedMilliseconds
                + (long)(SpiderTaskUtil.RandomSeconds(entity.World.Rand, pauseMinMs, pauseMaxMs) * 1000);
            return true;
        }

        if (pausing)
        {
            SpiderTaskUtil.StopWalking(entity);
            if (entity.World.ElapsedMilliseconds < pauseUntilMs || !entity.OnGround)
            {
                return true;
            }
            pausing = false;
            List<JumpStep> plan = PlanHop();
            if (plan == null)
            {
                return false;
            }
            StartHop(plan);
            return true;
        }
        return false;
    }

    public override void FinishExecute(bool cancelled)
    {
        runner.Stop();
        hopping = false;
        pausing = false;
        base.FinishExecute(cancelled);
    }

    private void StartHop(List<JumpStep> plan)
    {
        JumpStep step = plan[0];
        SpiderTaskUtil.Log(entity, LogName, "hop " + (hopsDone + 1) + " to " + SpiderTaskUtil.Format(step.ToX, step.ToY, step.ToZ)
            + " (" + (step.ToY - step.FromY).ToString("+0.00;-0.00") + " blocks)");
        runner.Start(plan);
        hopping = true;
    }

    // One hop to a random spot nearby (favouring high ones) inside the territory, or null.
    private List<JumpStep> PlanHop()
    {
        IJumpWorld world = new SpiderJumpWorld(entity);
        JumpLimits limits = SpiderTaskUtil.CreateLimits();
        if (territoryRadius > 0)
        {
            UpdateAnchor(limits);
            LandingSpotFilter filter = new LandingSpotFilter(world);
            filter.SetAnchor(anchorX, anchorZ, territoryRadius);
            world = filter;
        }
        return JumpPlanner.PlanJumpToRandomSpot(world, entity.Pos.X, entity.Pos.Y, entity.Pos.Z,
            entity.Pos.X, entity.Pos.Y, entity.Pos.Z, landingRadius, heightBias, limits, entity.World.Rand);
    }

    // The territory's centre: set the first time, and moved to the spider when it is out of hopping range of it.
    private void UpdateAnchor(JumpLimits limits)
    {
        double dx = entity.Pos.X - anchorX;
        double dz = entity.Pos.Z - anchorZ;
        double outOfReach = territoryRadius + limits.MaxJumpHorizontal;
        if (!hasAnchor || dx * dx + dz * dz > outOfReach * outOfReach)
        {
            hasAnchor = true;
            anchorX = entity.Pos.X;
            anchorZ = entity.Pos.Z;
            SpiderTaskUtil.Log(entity, LogName, "territory centred at " + SpiderTaskUtil.Format(anchorX, entity.Pos.Y, anchorZ));
        }
    }
}
