using System;

namespace JumpingSpiders.Tests;

/// <summary>
/// An independent, line-by-line transcription of how Vintage Story 1.22.7 moves a creature during one physics
/// tick (EntityBehaviorControlledPhysics.OnPhysicsTick with its modules PModuleOnGround, PModuleInAir,
/// PModuleGravity, PModuleMotionDrag, and the move in ApplyTests), reduced to a spider flying over flat ground.
/// It deliberately does not call JumpMath's own physics, so the tests compare two separate implementations.
///
/// The ground is a flat floor at FloorY that only stops downward motion (we test landing heights, not walls).
/// </summary>
public class EngineJumpSimulator
{
    // Engine constants, written out again on purpose.
    public const float TickDt = 1f / 30f;
    public const float GroundSubstep = 1 / 60f;
    public const double GroundDrag = 1 - 0.3;
    public const double Gravity = 0.37f;
    public const double AirDragAlways = 0.983f;
    public const float CoyoteSeconds = 0.15f;

    public double X;
    public double Y;
    public double Z;
    public double MotionX;
    public double MotionY;
    public double MotionZ;
    public bool OnGround = true;
    public double FloorY;

    /// <summary>When false the ground friction after take-off is left out, to compare against the pure model.</summary>
    public bool SimulateCoyoteTime = true;

    public int TicksDone;

    // PModuleOnGround state. The creature stood on the ground before the jump, so coyote time was just reset
    // and decremented once, and the sub-step accumulator holds some remainder.
    private float coyoteTimer = CoyoteSeconds - TickDt;
    private float accum = 1 / 60f;

    public void PhysicsTick()
    {
        double prevYMotion = MotionY;

        // PModuleOnGround: applies while on the ground and during coyote time after leaving it.
        bool groundModuleApplies = false;
        if (OnGround)
        {
            coyoteTimer = CoyoteSeconds;
        }
        if (coyoteTimer > 0)
        {
            groundModuleApplies = true;
        }
        if (groundModuleApplies && SimulateCoyoteTime)
        {
            coyoteTimer -= TickDt;
            float localAccum = Math.Min(1, accum + TickDt);
            while (localAccum > GroundSubstep)
            {
                localAccum -= GroundSubstep;
                // The walk vector is zero (path traverser stopped), so the walk part adds nothing.
                MotionX *= GroundDrag;
                MotionZ *= GroundDrag;
            }
            accum = localAccum;
        }
        else if (groundModuleApplies)
        {
            coyoteTimer -= TickDt;
        }

        // PModuleInAir: adds WalkVector * strength, which is zero.

        // PModuleGravity.
        double extra = Math.Max(0, -0.015f * MotionY);
        MotionY -= (Gravity + extra) * TickDt;

        // PModuleMotionDrag.
        double drag = (float)Math.Pow(AirDragAlways, TickDt * 33);
        MotionX *= drag;
        MotionY *= drag;
        MotionZ *= drag;

        // ApplyTests: vertical first, then horizontal; dtFactor = dt * 60.
        double dtFactor = TickDt * 60;
        double nextY = Y + MotionY * dtFactor;
        bool collidedVertically = false;
        if (MotionY < 0 && Y >= FloorY - 1e-9 && nextY <= FloorY)
        {
            nextY = FloorY;
            MotionY = 0;
            collidedVertically = true;
        }
        Y = nextY;
        X += MotionX * dtFactor;
        Z += MotionZ * dtFactor;

        OnGround = collidedVertically && prevYMotion <= 0;
        TicksDone++;
    }
}
