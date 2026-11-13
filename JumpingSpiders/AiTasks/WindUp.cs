using System;
using Vintagestory.API.Common;

namespace JumpingSpiders;

/// <summary>
/// The crouch before a jump: holds the spider still for a random time while it plays the wind-up animation (a
/// jumping spider raising its front legs). The attack task uses one directly (it keeps turning towards its prey
/// while this runs); JumpRunner uses one before each of its jumps when a wind-up time is set.
///
/// Begin starts it, Tick (called every AI tick) returns true once the time is up, Cancel stops it early.
/// Timing uses the AI tick's dt, like every vanilla attack task does. Server side only.
/// </summary>
public class WindUp
{
    /// <summary>True from Begin until the time is up or Cancel is called.</summary>
    public bool Active;

    /// <summary>How long this wind-up lasts, in seconds (rolled in Begin).</summary>
    public float DurationSeconds;

    /// <summary>How long it has run so far, in seconds.</summary>
    public float ElapsedSeconds;

    private readonly EntityAgent entity;
    private string animation;

    public WindUp(EntityAgent entity)
    {
        this.entity = entity;
    }

    /// <summary>Starts a wind-up of a random length between minSeconds and maxSeconds, playing animationCode ("" = none).</summary>
    public void Begin(float minSeconds, float maxSeconds, string animationCode, Random rand)
    {
        DurationSeconds = minSeconds;
        if (maxSeconds > minSeconds)
        {
            DurationSeconds = minSeconds + (float)rand.NextDouble() * (maxSeconds - minSeconds);
        }
        ElapsedSeconds = 0;
        animation = animationCode;
        Active = true;
        SpiderTaskUtil.StopWalking(entity);
        SpiderTaskUtil.StartAnimation(entity, animation);
    }

    /// <summary>Call every AI tick while Active. Returns true in the tick the wind-up ends (the animation is stopped then).</summary>
    public bool Tick(float dt)
    {
        if (!Active)
        {
            return false;
        }
        SpiderTaskUtil.StopWalking(entity);
        ElapsedSeconds += dt;
        if (ElapsedSeconds < DurationSeconds)
        {
            return false;
        }
        Finish();
        return true;
    }

    /// <summary>Stops the wind-up now (no jump follows from it).</summary>
    public void Cancel()
    {
        if (Active)
        {
            Finish();
        }
    }

    private void Finish()
    {
        Active = false;
        SpiderTaskUtil.StopAnimation(entity, animation);
    }
}
