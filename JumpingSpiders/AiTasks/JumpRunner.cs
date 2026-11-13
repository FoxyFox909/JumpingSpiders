using System;
using System.Collections.Generic;
using Vintagestory.API.Common;

namespace JumpingSpiders;

/// <summary>
/// Carries out a plan of one or more jumps from JumpPlanner: for each jump an optional wind-up (facing the jump
/// direction), then the flight through a JumpController, then a short pause on the perch before the next jump.
/// Every task that jumps owns one runner; this is the code the four tasks share for "do the jumps I planned".
///
/// The task fills the settings (from its JSON), calls Start(plan), then Tick(dt) from every ContinueExecute while
/// IsBusy(), and Stop() from FinishExecute. Afterwards State says how it went: Reached (at the goal) or Failed
/// (LastEndReason says why). It never re-plans by itself: the task decides what to do next.
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

    /// <summary>The jump being done now (or last done), or null.</summary>
    public JumpStep CurrentStep()
    {
        if (plan == null || plan.Count == 0)
        {
            return null;
        }
        return plan[Math.Min(stepIndex, plan.Count - 1)];
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
            if (LastEndReason != JumpEndReason.Landed || !IsNearLandingPoint(plan[stepIndex]))
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

    private bool IsNearLandingPoint(JumpStep step)
    {
        double dx = entity.Pos.X - step.ToX;
        double dz = entity.Pos.Z - step.ToZ;
        double dy = entity.Pos.Y - step.ToY;
        return dx * dx + dz * dz <= LandingToleranceBlocks * LandingToleranceBlocks
            && Math.Abs(dy) <= LandingToleranceBlocks;
    }
}
