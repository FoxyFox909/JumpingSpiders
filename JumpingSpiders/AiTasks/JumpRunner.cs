using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace JumpingSpiders;

/// <summary>
/// Carries out a plan of one or more jumps from JumpPlanner: for each jump an optional wind-up (facing the jump
/// direction), then the flight through a JumpController, then a short pause on the perch before the next jump.
/// Every task that jumps owns one runner; this is the code the five tasks share for "do the jumps I planned".
///
/// The task fills the settings (ReadSettings reads them from its JSON entry), calls Start(plan), then Tick(dt) from
/// every ContinueExecute while IsBusy(), and Stop() from FinishExecute. Afterwards State says how it went: Reached (at the goal) or Failed
/// (LastEndReason says why; Landed there means the spider was no longer standing on its perch, near where the next
/// jump starts, when that jump was due). It never re-plans by itself: the task decides what to do next.
/// Server side only.
/// </summary>
public class JumpRunner
{
    /// <summary>
    /// A jump counts as landed where planned when the feet are within this many blocks of the planned landing point,
    /// sideways and up/down. On a real server the arc is followed to about 0.0001 blocks, so this only separates
    /// "landed there" from "bounced off something and ended up elsewhere".
    /// </summary>
    public const double LandingToleranceBlocks = 0.5;

    // ---- Settings; the owning task sets them from its JSON. ----

    /// <summary>Shortest and longest wind-up before each jump, in seconds. 0 and 0 = jump at once, no wind-up.</summary>
    public float WindUpMinSeconds;
    public float WindUpMaxSeconds;

    /// <summary>Animation played while winding up ("" = none).</summary>
    public string WindUpAnimation = "jumpwindup";

    /// <summary>Animation played while in the air ("" = none).</summary>
    public string JumpAnimation = "jump";

    /// <summary>Pause on an intermediate perch before the next jump, in seconds.</summary>
    public float PauseSeconds;

    /// <summary>How fast the spider turns towards the jump direction while winding up, in radians per second.</summary>
    public float TurnSpeedRadPerSec = (float)Math.PI * 2;

    // ---- State, for the owning task to read. ----

    public JumpRunState State = JumpRunState.Idle;

    /// <summary>Why the last flight ended (from the JumpController).</summary>
    public JumpEndReason LastEndReason = JumpEndReason.None;

    /// <summary>How many jumps of the current plan landed where planned.</summary>
    public int StepsDone;

    /// <summary>The controller that flies each jump; the attack task reads it to check for a hit while airborne.</summary>
    public readonly JumpController Controller;

    private readonly EntityAgent entity;
    private readonly WindUp windUp;
    private List<JumpStep> plan;
    private int stepIndex;
    private float pauseLeft;

    public JumpRunner(EntityAgent entity)
    {
        this.entity = entity;
        Controller = new JumpController(entity);
        windUp = new WindUp(entity);
    }

    /// <summary>True while winding up, flying or pausing between jumps.</summary>
    public bool IsBusy()
    {
        return State == JumpRunState.WindingUp || State == JumpRunState.Flying || State == JumpRunState.Pausing;
    }

    /// <summary>True while the spider is in the air on one of this runner's jumps.</summary>
    public bool IsFlying()
    {
        return State == JumpRunState.Flying;
    }

    /// <summary>
    /// Reads the settings from a task's JSON entry, with the defaults the entity JSON writes out: the wind-up before
    /// each jump, the pause on a perch, the turning speed during the wind-up and the two animations. Tasks whose jumps
    /// have no wind-up (attack, flee) set those fields themselves instead.
    /// </summary>
    public void ReadSettings(JsonObject taskConfig)
    {
        // Milliseconds: the crouch before each jump, a random time between these two (0 and 0 = none).
        WindUpMinSeconds = taskConfig["windUpMinMs"].AsInt(350) / 1000f;
        WindUpMaxSeconds = taskConfig["windUpMaxMs"].AsInt(900) / 1000f;
        // Milliseconds: rest on an intermediate perch before the next jump of a plan.
        PauseSeconds = taskConfig["pauseBetweenJumpsMs"].AsInt(300) / 1000f;
        // Degrees per second: how fast it turns towards the jump direction during the crouch.
        TurnSpeedRadPerSec = taskConfig["turnSpeedDegPerSec"].AsFloat(360) * (float)Math.PI / 180f;
        // Animation codes from the entity JSON; "" = none.
        WindUpAnimation = SpiderTaskUtil.ReadAnimation(taskConfig, "windUpAnimation", "jumpwindup");
        JumpAnimation = SpiderTaskUtil.ReadAnimation(taskConfig, "jumpAnimation", "jump");
    }

    /// <summary>Starts the plan. An empty plan means "already there": State becomes Reached at once.</summary>
    public void Start(List<JumpStep> newPlan)
    {
        Stop();
        plan = newPlan;
        stepIndex = 0;
        StepsDone = 0;
        LastEndReason = JumpEndReason.None;
        if (plan == null || plan.Count == 0)
        {
            State = JumpRunState.Reached;
            return;
        }
        BeginStep();
    }

    /// <summary>Call every AI tick while IsBusy().</summary>
    public void Tick(float dt)
    {
        if (State == JumpRunState.WindingUp)
        {
            JumpStep step = plan[stepIndex];
            SpiderTaskUtil.TurnTowards(entity, step.ToX, step.ToZ, TurnSpeedRadPerSec, dt);
            if (windUp.Tick(dt))
            {
                Launch();
            }
            return;
        }

        if (State == JumpRunState.Flying)
        {
            Controller.OnTick(dt);
            if (Controller.InFlight)
            {
                return;
            }
            SpiderTaskUtil.StopAnimation(entity, JumpAnimation);
            LastEndReason = Controller.EndReason;
            JumpStep landed = plan[stepIndex];
            if (LastEndReason != JumpEndReason.Landed || !IsNear(landed.ToX, landed.ToY, landed.ToZ))
            {
                State = JumpRunState.Failed;
                return;
            }
            StepsDone++;
            stepIndex++;
            if (stepIndex >= plan.Count)
            {
                State = JumpRunState.Reached;
                return;
            }
            pauseLeft = PauseSeconds;
            State = JumpRunState.Pausing;
            return;
        }

        if (State == JumpRunState.Pausing)
        {
            SpiderTaskUtil.StopWalking(entity);
            pauseLeft -= dt;
            if (pauseLeft <= 0)
            {
                // The next jump was planned from this perch: if something pushed the spider off it meanwhile, or it
                // is not standing, that jump would start from the wrong place. The task plans again instead.
                JumpStep next = plan[stepIndex];
                if (!entity.OnGround || !IsNear(next.FromX, next.FromY, next.FromZ))
                {
                    State = JumpRunState.Failed;
                    return;
                }
                BeginStep();
            }
        }
    }

    /// <summary>Stops whatever is going on. A spider in the air keeps falling normally.</summary>
    public void Stop()
    {
        windUp.Cancel();
        if (State == JumpRunState.Flying)
        {
            Controller.Cancel();
            SpiderTaskUtil.StopAnimation(entity, JumpAnimation);
        }
        if (IsBusy())
        {
            State = JumpRunState.Idle;
        }
    }

    private void BeginStep()
    {
        if (WindUpMaxSeconds > 0)
        {
            windUp.Begin(WindUpMinSeconds, WindUpMaxSeconds, WindUpAnimation, entity.World.Rand);
            State = JumpRunState.WindingUp;
            return;
        }
        Launch();
    }

    private void Launch()
    {
        Controller.Launch(plan[stepIndex]);
        SpiderTaskUtil.StartAnimation(entity, JumpAnimation);
        State = JumpRunState.Flying;
    }

    // True when the feet are within LandingToleranceBlocks of the point, sideways and up/down.
    private bool IsNear(double x, double y, double z)
    {
        double dx = entity.Pos.X - x;
        double dz = entity.Pos.Z - z;
        double dy = entity.Pos.Y - y;
        return dx * dx + dz * dz <= LandingToleranceBlocks * LandingToleranceBlocks
            && Math.Abs(dy) <= LandingToleranceBlocks;
    }
}
