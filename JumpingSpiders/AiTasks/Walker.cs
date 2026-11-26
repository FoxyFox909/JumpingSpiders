using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace JumpingSpiders;

/// <summary>
/// Walking or running on foot with the game's path traverser, with the walk (or run) animation playing on the way.
/// The four tasks that move on foot own one each, the way they own a JumpRunner: the sleep and build tasks walk to a
/// spot they chose (WalkTo), the attack task stalks its prey and re-aims at it every so often (NavigateTo), and the
/// flee task runs in a straight line away from the threat (RunTowards).
///
/// The path traverser (see SpiderTaskUtil) says how a walk ended through two callbacks, which the game calls from the
/// traverser's own tick, just before the tasks' tick: the goal was reached, or the spider got stuck or no path was
/// found. Walker turns them into the flags Arrived and Failed for the task to read in its ContinueExecute, and with
/// StopAnimationOnArrival or StopAnimationOnFailure set also stops the animation right there (the attack and flee
/// tasks may set off again later in the same run, and the animation starts again then). Otherwise the task stops it
/// itself when it moves on (Stop or StopAnimation). The task calls Stop() from FinishExecute. Server side only.
/// </summary>
public class Walker
{
    // ---- Settings; the owning task sets them from its JSON. ----

    /// <summary>Animation played while walking or running ("" = none).</summary>
    public string Animation = "walk";

    /// <summary>True: reaching the goal stops the animation at once.</summary>
    public bool StopAnimationOnArrival;

    /// <summary>True: getting stuck, or finding no path, stops the animation at once.</summary>
    public bool StopAnimationOnFailure;

    // ---- State, for the owning task to read. ----

    /// <summary>The traverser said the goal was reached (since the last WalkTo or ClearOutcome).</summary>
    public bool Arrived;

    /// <summary>The traverser said the spider got stuck or found no path (since the last WalkTo or ClearOutcome).</summary>
    public bool Failed;

    private readonly EntityAgent entity;
    private readonly PathTraverserBase traverser;
    private bool animating;

    /// <summary>traverser is the task's pathTraverser (one per creature, shared by all its tasks).</summary>
    public Walker(EntityAgent entity, PathTraverserBase traverser)
    {
        this.entity = entity;
        this.traverser = traverser;
    }

    /// <summary>
    /// A new walk with the game's pathfinder to goal, stopping within stopDistance blocks of it: stops the walk under
    /// way, forgets how the last one ended, then NavigateTo. False when the pathfinder did not take the request.
    /// </summary>
    public bool WalkTo(Vec3d goal, float speed, float stopDistance)
    {
        ClearOutcome();
        traverser.Stop();
        return NavigateTo(goal, speed, stopDistance);
    }

    /// <summary>
    /// Asks the game's pathfinder for a path to goal (speed in vanilla movespeed units) without stopping the walk under
    /// way or clearing Arrived and Failed: how the attack task re-aims at its moving prey. The search runs on another
    /// thread and the spider sets off when it is done. False when the pathfinder did not take the request (it is still
    /// busy with the last one, and the spider walks on along its old path, or the spider stands where no path can
    /// start); the animation then does not start. Otherwise the animation starts, if it is not playing already.
    /// </summary>
    public bool NavigateTo(Vec3d goal, float speed, float stopDistance)
    {
        bool started = traverser.NavigateTo_Async(goal, speed, stopDistance, OnArrived, OnFailed, OnFailed);
        if (started)
        {
            StartAnimation();
        }
        return started;
    }

    /// <summary>Runs in a straight line towards goal (the traverser's WalkTowards, no pathfinding) and starts the animation.</summary>
    public void RunTowards(Vec3d goal, float speed, float stopDistance)
    {
        traverser.WalkTowards(goal, speed, stopDistance, OnArrived, OnFailed);
        StartAnimation();
    }

    /// <summary>Stops walking and the animation.</summary>
    public void Stop()
    {
        traverser.Stop();
        StopAnimation();
    }

    /// <summary>Stops only the animation (when the traverser has stopped by itself).</summary>
    public void StopAnimation()
    {
        if (animating)
        {
            animating = false;
            SpiderTaskUtil.StopAnimation(entity, Animation);
        }
    }

    /// <summary>Forgets how the last walk ended (Arrived and Failed back to false).</summary>
    public void ClearOutcome()
    {
        Arrived = false;
        Failed = false;
    }

    private void StartAnimation()
    {
        if (!animating)
        {
            animating = true;
            SpiderTaskUtil.StartAnimation(entity, Animation);
        }
    }

    // Path traverser callbacks (the game calls these from the traverser's tick).
    private void OnArrived()
    {
        Arrived = true;
        if (StopAnimationOnArrival)
        {
            StopAnimation();
        }
    }

    private void OnFailed()
    {
        Failed = true;
        if (StopAnimationOnFailure)
        {
            StopAnimation();
        }
    }
}
