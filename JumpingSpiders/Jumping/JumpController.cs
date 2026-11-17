using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace JumpingSpiders;

/// <summary>
/// Flies one entity along a planned JumpStep: launches it, moves it along the arc, and reports when the flight ends.
/// Each AI task that jumps owns one; the task calls Launch once, then OnTick from its ContinueExecute every AI
/// tick while InFlight is true, and Cancel from FinishExecute if it is stopped early. Server side only.
///
/// The flight is faster than the game's gravity allows (Config.JumpFlightSpeedMultiplier): the spider follows the
/// planned arc on a FlightSchedule, a timetable of where it must be at the end of every physics tick. How that is
/// done (all read from the 1.22.7 physics code):
/// - Physics runs in its own fixed 1/30 s ticks, separate from the AI ticks (20 ms), and moves the entity by its
///   Pos.Motion (blocks per 1/60 s). Gravity and air drag change Motion first in every physics tick, then the
///   entity moves by it, stopped by blocks.
/// - At the end of every physics tick the game calls the entity's PhysicsUpdateWatcher. The controller hooks in
///   there for the length of a flight, and writes the motion that, after the next tick's gravity and drag, ends that
///   tick on the next point of the timetable (JumpMath.ComputeDriveMotion). The game's own physics still does the
///   moving, so the spider cannot pass through blocks. Doing this per physics tick (and not per AI tick) keeps it
///   exact however the two kinds of tick interleave, also when a busy server runs two physics ticks in a row.
/// - After an entity leaves the ground the engine keeps applying ground friction to the horizontal motion for
///   0.15 s ("coyote time"); the written motion makes up for it.
/// - While a creature walks, the AI's path traverser sets Controls.WalkVector, which also pushes it around in the
///   air. Launch stops the traverser, and the walk vector is zeroed again in every tick.
/// - Entity.OnGround is only updated by the next physics tick, so right after launch it is still true. A flight
///   therefore counts as landed only after OnGround has been seen false once.
///
/// What the spider touched is checked in the same hook, after every physics tick: the floor (landed), the side of a
/// block (hit a wall) or a block above (hit a ceiling). On a wall or ceiling the spider loses its sideways speed and
/// is handed back to the game's physics with the vertical speed an unhurried jump would have there, so it drops down
/// just as it did before the flight was sped up. The AI tick then reports the end (EndReason).
///
/// A flight always ends, with EndReason saying why: landing, hitting a wall or ceiling, water, death, never
/// leaving the ground (Config.JumpLiftoffTimeoutSeconds) or the timeout (Config.MaxFlightSeconds, sped up likewise).
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

    /// <summary>The timetable of the jump being flown (or last flown), or null before the first Launch.</summary>
    public FlightSchedule CurrentSchedule;

    /// <summary>
    /// The feet position at launch and at the end of every physics tick of the current (or last) flight, in order.
    /// The spider moves up to about 0.9 blocks per physics tick, so the attack task checks these (and the stretches
    /// between them) for a touch of its prey, not only where the spider is at each AI tick.
    /// </summary>
    public readonly List<Vec3d> FlightPath = new List<Vec3d>();

    private readonly EntityAgent entity;
    private readonly PhysicsTickDelegate physicsTickHandler;
    private bool hooked;
    private long launchTimeMs;
    private bool leftGround;
    private int ticksDone;
    private int frictionTicksLeft;
    private double lastPlannedMoveY;
    private JumpEndReason physicsEndReason = JumpEndReason.None;
    private double timeoutSeconds;
    private double liftoffTimeoutSeconds;
    private double holdBlocksPerTick;

    public JumpController(EntityAgent entity)
    {
        this.entity = entity;
        physicsTickHandler = new PhysicsTickDelegate(OnPhysicsTick);
    }

    /// <summary>
    /// Starts a jump from where the spider is now. The step should start at (or very near) the spider's feet;
    /// small differences are steered out during the flight.
    /// </summary>
    public void Launch(JumpStep step)
    {
        // A launch while a flight is still going on replaces it.
        Unhook();

        JumpingSpidersConfig config = JumpingSpidersModSystem.Config;
        double multiplier = config.JumpFlightSpeedMultiplier;
        CurrentStep = step;
        CurrentSchedule = FlightSchedule.Create(step, multiplier);
        timeoutSeconds = FlightSchedule.DrivenSeconds(JumpMath.MaxTicksFor(config.MaxFlightSeconds), multiplier);
        liftoffTimeoutSeconds = config.JumpLiftoffTimeoutSeconds;
        holdBlocksPerTick = config.MaxJumpSpeed * CurrentSchedule.SpeedMultiplier * JumpMath.PhysicsTickSeconds;

        InFlight = true;
        LandedThisTick = false;
        EndReason = JumpEndReason.None;
        physicsEndReason = JumpEndReason.None;
        leftGround = false;
        ticksDone = 0;
        frictionTicksLeft = 0;
        launchTimeMs = entity.World.ElapsedMilliseconds;
        FlightPath.Clear();
        FlightPath.Add(new Vec3d(entity.Pos.X, entity.Pos.Y, entity.Pos.Z));

        StopWalking();

        // Face the jump direction. Yaw 0 looks towards +Z; Atan2(x, z) is the game's convention (see AiTaskSeekEntity).
        float yaw = (float)Math.Atan2(step.VX, step.VZ);
        entity.Pos.Yaw = yaw;
        entity.BodyYaw = yaw;

        WriteDriveMotion();
        entity.PhysicsUpdateWatcher += physicsTickHandler;
        hooked = true;
    }

    /// <summary>Call every AI tick while InFlight. dt is the AI tick length in seconds (not needed: physics keeps the time).</summary>
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
        if (physicsEndReason != JumpEndReason.None)
        {
            End(physicsEndReason);
            if (EndReason == JumpEndReason.Landed)
            {
                LandedThisTick = true;
            }
            return;
        }
        if (entity.FeetInLiquid || entity.Swimming)
        {
            End(JumpEndReason.Water);
            return;
        }

        // Measured on the world clock, so a flight that was frozen (entity became inactive) times out at once.
        double elapsedSeconds = (entity.World.ElapsedMilliseconds - launchTimeMs) / 1000.0;
        if (!leftGround && elapsedSeconds > liftoffTimeoutSeconds)
        {
            End(JumpEndReason.NoLiftoff);
            return;
        }
        if (elapsedSeconds > timeoutSeconds)
        {
            End(JumpEndReason.TimedOut);
            return;
        }

        StopWalking();
    }

    /// <summary>Ends the flight now; the spider falls on from here the way an unhurried jump would.</summary>
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
        if (hooked)
        {
            // Still being moved along the timetable (the flight was ended from the AI tick, not by touching something).
            Unhook();
            if (ticksDone < CurrentSchedule.Ticks)
            {
                HandBackToGamePhysics(true);
            }
        }
        InFlight = false;
        EndReason = reason;
    }

    // Called by the game at the end of every physics tick of this entity while a flight is on (see the class comment).
    // On a busy server this can run on a physics thread, while the main thread waits: it only touches this entity.
    private void OnPhysicsTick(float accum, Vec3d prevPos)
    {
        if (!hooked)
        {
            return;
        }
        ticksDone++;
        FlightPath.Add(new Vec3d(entity.Pos.X, entity.Pos.Y, entity.Pos.Z));

        JumpEndReason touched = CheckWhatWasTouched();
        if (touched != JumpEndReason.None)
        {
            physicsEndReason = touched;
            Unhook();
            // After the planned landing tick gravity already had the spider, so its vertical motion is natural.
            bool wasDriven = ticksDone <= CurrentSchedule.Ticks;
            if (touched == JumpEndReason.Landed || touched == JumpEndReason.HitCeiling)
            {
                // The block stopped the vertical motion already.
                StopHorizontalMotion();
            }
            else if (touched == JumpEndReason.HitWall)
            {
                StopHorizontalMotion();
                if (wasDriven)
                {
                    HandBackToGamePhysics(false);
                }
            }
            else if (wasDriven)
            {
                HandBackToGamePhysics(true);
            }
            return;
        }

        entity.Controls.WalkVector.Set(0, 0, 0);
        if (ticksDone < CurrentSchedule.Ticks)
        {
            WriteDriveMotion();
            return;
        }

        // Past the planned landing tick without touching down (the block it aimed for is gone, say): gravity has the
        // spider again (nothing written for Y), and it is held above the landing point until it lands or times out.
        double motionX;
        double motionZ;
        JumpMath.ComputeHoldMotion(CurrentStep.ToX, CurrentStep.ToZ, entity.Pos.X, entity.Pos.Z, holdBlocksPerTick,
            out motionX, out motionZ);
        entity.Pos.Motion.X = motionX;
        entity.Pos.Motion.Z = motionZ;
        lastPlannedMoveY = 0;
    }

    // What the last physics tick ran into, or None. Mirrors how the game itself decides OnGround.
    private JumpEndReason CheckWhatWasTouched()
    {
        if (entity.FeetInLiquid || entity.Swimming)
        {
            return JumpEndReason.Water;
        }
        if (!leftGround)
        {
            if (entity.OnGround)
            {
                return JumpEndReason.None;
            }
            leftGround = true;
        }
        // OnGround is checked first: touching the side of the landing block while landing still counts as landing.
        // The game calls it OnGround only if the motion written before the tick pointed down; on a slow last tick that
        // motion can point up (it includes what gravity takes away), so a stop while moving down counts as well.
        if (entity.OnGround || (entity.CollidedVertically && lastPlannedMoveY < 0))
        {
            return JumpEndReason.Landed;
        }
        if (entity.CollidedHorizontally)
        {
            return JumpEndReason.HitWall;
        }
        if (entity.CollidedVertically)
        {
            // Collided vertically without moving down means it hit something above.
            return JumpEndReason.HitCeiling;
        }
        return JumpEndReason.None;
    }

    // Writes the motion that makes the next physics tick end on the next point of the timetable.
    private void WriteDriveMotion()
    {
        bool friction = JumpMath.NextTickHasGroundFriction(entity.OnGround, ref frictionTicksLeft);
        double motionX;
        double motionY;
        double motionZ;
        JumpMath.ComputeDriveMotion(CurrentSchedule, ticksDone, friction, entity.Pos.X, entity.Pos.Y, entity.Pos.Z,
            out motionX, out motionY, out motionZ);
        entity.Pos.Motion.Set(motionX, motionY, motionZ);

        double nextX;
        double nextY;
        double nextZ;
        CurrentSchedule.TargetAfterTick(ticksDone + 1, out nextX, out nextY, out nextZ);
        lastPlannedMoveY = nextY - entity.Pos.Y;
    }

    // Gives the spider the motion the game's own physics would have given it at this point of the arc, so that from
    // here on it moves like an ordinary jump. withHorizontal false keeps the current sideways motion.
    private void HandBackToGamePhysics(bool withHorizontal)
    {
        double naturalTicks = CurrentSchedule.NaturalTicksAt(ticksDone);
        entity.Pos.Motion.Y = JumpMath.NaturalMotionYAtTicks(CurrentStep, naturalTicks);
        if (withHorizontal)
        {
            entity.Pos.Motion.X = JumpMath.HorizontalMotionForOneTick(CurrentStep.VX * JumpMath.PhysicsTickSeconds);
            entity.Pos.Motion.Z = JumpMath.HorizontalMotionForOneTick(CurrentStep.VZ * JumpMath.PhysicsTickSeconds);
        }
    }

    private void Unhook()
    {
        if (hooked)
        {
            entity.PhysicsUpdateWatcher -= physicsTickHandler;
            hooked = false;
        }
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
