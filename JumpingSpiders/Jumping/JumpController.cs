using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace JumpingSpiders;

/// <summary>
/// Flies one entity along a planned JumpStep: launches it, keeps it on the arc, and reports when the flight ends.
/// Each AI task that jumps owns one; the task calls Launch once, then OnTick from its ContinueExecute every AI
/// tick while InFlight is true, and Cancel from FinishExecute if it is stopped early. Server side only.
///
/// Why this is more than "set the velocity once" (all read from the 1.22.7 physics code):
/// - Physics runs in its own fixed 1/30 s ticks, separate from the AI ticks, and moves the entity by its
///   Pos.Motion (blocks per 1/60 s). Gravity and air drag act on Motion every physics tick.
/// - After an entity leaves the ground the engine keeps applying ground friction to the horizontal motion for
///   0.15 s ("coyote time"), which would eat almost all of a sideways launch. So every AI tick this controller
///   writes the horizontal motion again, steering the spider onto the planned arc (JumpMath.ComputeSteering).
///   The vertical motion is set once at launch and left to the physics.
/// - While a creature walks, the AI's path traverser sets Controls.WalkVector, which also pushes it around in the
///   air. Launch stops the traverser, and every tick the walk vector is zeroed again in case some task restarted it.
/// - Entity.OnGround is only updated by the next physics tick, so right after launch it is still true. A flight
///   therefore counts as landed only after OnGround has been seen false once.
///
/// A flight always ends, with EndReason saying why: landing, hitting a wall or ceiling, water, death, never
/// leaving the ground (Config.JumpLiftoffTimeoutSeconds) or the Config.MaxFlightSeconds timeout.
/// </summary>
public class JumpController
{
    /// <summary>True from Launch until the flight ends.</summary>
    public bool InFlight;

    /// <summary>True during the one OnTick call in which the spider touched down (EndReason is then Landed).</summary>
    public bool LandedThisTick;

    /// <summary>Why the last flight ended; None while flying or before the first jump.</summary>
    public JumpEndReason EndReason = JumpEndReason.None;

    /// <summary>The jump being flown (or last flown), or null before the first Launch.</summary>
    public JumpStep CurrentStep;

    private readonly EntityAgent entity;
    private long launchTimeMs;
    private bool leftGround;
    private double maxFlightSeconds;
    private double liftoffTimeoutSeconds;
    private double maxSpeed;

    public JumpController(EntityAgent entity)
    {
        this.entity = entity;
    }

    /// <summary>
    /// Starts a jump from where the spider is now. The step should start at (or very near) the spider's feet;
    /// small differences are steered out during the flight.
    /// </summary>
    public void Launch(JumpStep step)
    {
        JumpingSpidersConfig config = JumpingSpidersModSystem.Config;
        maxFlightSeconds = config.MaxFlightSeconds;
        liftoffTimeoutSeconds = config.JumpLiftoffTimeoutSeconds;
        maxSpeed = config.MaxJumpSpeed;

        CurrentStep = step;
        InFlight = true;
        LandedThisTick = false;
        EndReason = JumpEndReason.None;
        leftGround = false;
        launchTimeMs = entity.World.ElapsedMilliseconds;

        StopWalking();

        // Face the jump direction. Yaw 0 looks towards +Z; Atan2(x, z) is the game's convention (see AiTaskSeekEntity).
        float yaw = (float)Math.Atan2(step.VX, step.VZ);
        entity.Pos.Yaw = yaw;
        entity.BodyYaw = yaw;

        entity.Pos.Motion.Y = step.VY / JumpMath.BlocksPerSecondPerMotionUnit;
        Steer();
    }

    /// <summary>Call every AI tick while InFlight. dt is the AI tick length in seconds (not needed by the physics clock).</summary>
    public void OnTick(float dt)
    {
        LandedThisTick = false;
        if (!InFlight)
        {
            return;
        }

        if (!entity.Alive)
        {
            End(JumpEndReason.Died);
            return;
        }
        if (entity.ShouldDespawn)
        {
            End(JumpEndReason.Cancelled);
            return;
        }
        if (entity.FeetInLiquid || entity.Swimming)
        {
            End(JumpEndReason.Water);
            return;
        }

        // Measured on the world clock, so a flight that was frozen (entity became inactive) times out at once.
        double elapsedSeconds = (entity.World.ElapsedMilliseconds - launchTimeMs) / 1000.0;

        if (!leftGround)
        {
            if (!entity.OnGround)
            {
                leftGround = true;
            }
            else if (elapsedSeconds > liftoffTimeoutSeconds)
            {
                End(JumpEndReason.NoLiftoff);
                return;
            }
        }

        if (leftGround)
        {
            // OnGround is checked first: touching the side of the landing block while landing still counts as landing.
            if (entity.OnGround)
            {
                StopHorizontalMotion();
                End(JumpEndReason.Landed);
                LandedThisTick = true;
                return;
            }
            if (entity.CollidedHorizontally)
            {
                StopHorizontalMotion();
                End(JumpEndReason.HitWall);
                return;
            }
            if (entity.CollidedVertically)
            {
                // Collided vertically without being on the ground means it hit something above.
                StopHorizontalMotion();
                End(JumpEndReason.HitCeiling);
                return;
            }
        }

        if (elapsedSeconds > maxFlightSeconds)
        {
            End(JumpEndReason.TimedOut);
            return;
        }

        StopWalking();
        Steer();
    }

    /// <summary>Ends the flight now; the spider keeps its current motion and falls normally.</summary>
    public void Cancel()
    {
        if (InFlight)
        {
            End(JumpEndReason.Cancelled);
        }
        LandedThisTick = false;
    }

    private void End(JumpEndReason reason)
    {
        InFlight = false;
        EndReason = reason;
    }

    // Writes the horizontal motion that keeps the spider on the planned arc for the next physics tick.
    private void Steer()
    {
        double motionX;
        double motionZ;
        JumpMath.ComputeSteering(CurrentStep, entity.Pos.X, entity.Pos.Z, entity.Pos.Motion.Y, maxSpeed, out motionX, out motionZ);
        entity.Pos.Motion.X = motionX;
        entity.Pos.Motion.Z = motionZ;
    }

    private void StopHorizontalMotion()
    {
        entity.Pos.Motion.X = 0;
        entity.Pos.Motion.Z = 0;
    }

    // Stops the AI's path following so it does not steer the spider in the air.
    private void StopWalking()
    {
        EntityBehaviorTaskAI taskAi = entity.GetBehavior<EntityBehaviorTaskAI>();
        if (taskAi != null && taskAi.PathTraverser != null && taskAi.PathTraverser.Active)
        {
            taskAi.PathTraverser.Stop();
        }
        entity.Controls.WalkVector.Set(0, 0, 0);
    }
}
