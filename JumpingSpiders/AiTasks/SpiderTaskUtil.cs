using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace JumpingSpiders;

/// <summary>
/// Small helpers that all four spider AI tasks use: turning to face a point, stopping the walking AI, finding who
/// caused an emotion state, playing animations, reading the jump ability from the config, and debug logging.
/// Static methods only; server side only.
///
/// Vintage Story concepts used here:
/// - Yaw is the direction an entity faces, in radians; 0 looks towards +Z and Atan2(dx, dz) gives the yaw towards a
///   point (the game's own convention, see AiTaskSeekEntity).
/// - The "path traverser" is the AI's walking engine, one per creature, shared by all its tasks. While it is
///   active it steers the creature every tick through Controls.WalkVector.
/// - Emotion states (the vanilla "emotionstates" behavior) are moods such as "fleeondamage" that are switched on
///   for a while when the creature is hit, and remember the entity id of whoever caused them.
/// </summary>
public static class SpiderTaskUtil
{
    /// <summary>
    /// While the spider is in the air, its task reports this as its PriorityForCancel, so no other task can take
    /// over mid-flight (the physics would carry the spider along the arc anyway; a second jump launched from the
    /// air would go wrong). Flights last at most Config.MaxFlightSeconds, so nothing waits long.
    /// The same trick the vanilla drifter uses for its rock throw (priorityForCancel 9).
    /// </summary>
    public const float InFlightPriorityForCancel = 9f;

    /// <summary>TurnTowards reports "facing it" once the remaining angle is below this, in radians (about 9 degrees).</summary>
    public const float FacingToleranceRad = 0.15f;

    /// <summary>A copy of the spider's physical jump ability from the config, for the planner.</summary>
    public static JumpLimits CreateLimits()
    {
        return JumpingSpidersModSystem.Config.CreateJumpLimits();
    }

    /// <summary>A random duration between minMs and maxMs milliseconds, returned in seconds.</summary>
    public static float RandomSeconds(Random rand, int minMs, int maxMs)
    {
        if (maxMs <= minMs)
        {
            return minMs / 1000f;
        }
        return (minMs + (float)rand.NextDouble() * (maxMs - minMs)) / 1000f;
    }

    /// <summary>
    /// Turns the spider towards the point (x, z), at most radPerSecond * dt this tick.
    /// Returns true when it is now facing the point (within FacingToleranceRad).
    /// </summary>
    public static bool TurnTowards(EntityAgent entity, double x, double z, float radPerSecond, float dt)
    {
        float desiredYaw = (float)Math.Atan2(x - entity.Pos.X, z - entity.Pos.Z);
        float remaining = GameMath.AngleRadDistance(entity.Pos.Yaw, desiredYaw);
        float maxTurn = radPerSecond * dt;
        float turn = GameMath.Clamp(remaining, -maxTurn, maxTurn);
        entity.Pos.Yaw = GameMath.Mod(entity.Pos.Yaw + turn, GameMath.TWOPI);
        entity.BodyYaw = entity.Pos.Yaw;
        return Math.Abs(remaining - turn) < FacingToleranceRad;
    }

    /// <summary>Stops any walking the AI is doing, so the spider stands still (or flies without being steered).</summary>
    public static void StopWalking(EntityAgent entity)
    {
        EntityBehaviorTaskAI taskAi = entity.GetBehavior<EntityBehaviorTaskAI>();
        if (taskAi != null && taskAi.PathTraverser != null && taskAi.PathTraverser.Active)
        {
            taskAi.PathTraverser.Stop();
        }
        entity.Controls.WalkVector.Set(0, 0, 0);
    }

    /// <summary>
    /// The entity that caused the given emotion state (for example the attacker behind "fleeondamage"), or null
    /// when the state is not active, has no source, or the source is no longer in the world.
    /// </summary>
    public static Entity GetEmotionSource(EntityAgent entity, string stateCode)
    {
        EntityBehaviorEmotionStates emotions = entity.GetBehavior<EntityBehaviorEmotionStates>();
        if (emotions == null || stateCode == null)
        {
            return null;
        }
        ActiveEmoState state = emotions.GetActiveEmotionState(stateCode);
        if (state == null || state.SourceEntityId == 0)
        {
            return null;
        }
        return entity.World.GetEntityById(state.SourceEntityId);
    }

    /// <summary>True when other can no longer be chased or fled from: missing, dead, removed, or in another dimension.</summary>
    public static bool IsGone(EntityAgent self, Entity other)
    {
        if (other == null || !other.Alive || other.ShouldDespawn || other.State == EnumEntityState.Despawned)
        {
            return true;
        }
        return other.Pos.Dimension != self.Pos.Dimension;
    }

    /// <summary>Straight-line distance between two entities' feet positions, in blocks.</summary>
    public static double Distance(Entity a, Entity b)
    {
        return a.Pos.DistanceTo(b.Pos);
    }

    /// <summary>Distance between two entities' feet positions ignoring height, in blocks.</summary>
    public static double HorizontalDistance(Entity a, Entity b)
    {
        double dx = a.Pos.X - b.Pos.X;
        double dz = a.Pos.Z - b.Pos.Z;
        return Math.Sqrt(dx * dx + dz * dz);
    }

    /// <summary>
    /// Starts an animation by its code in the entity JSON (client "animations" list). An empty or null code means
    /// "no animation" and is ignored, so the author can switch an animation off in the JSON with "".
    /// </summary>
    public static void StartAnimation(EntityAgent entity, string code)
    {
        if (string.IsNullOrEmpty(code))
        {
            return;
        }
        entity.AnimManager.StartAnimation(code);
    }

    /// <summary>Stops an animation started with StartAnimation (ignored for an empty code).</summary>
    public static void StopAnimation(EntityAgent entity, string code)
    {
        if (string.IsNullOrEmpty(code))
        {
            return;
        }
        entity.AnimManager.StopAnimation(code);
    }

    /// <summary>
    /// Reads an animation code knob from a task's JSON: the default when the key is missing, "" (no animation)
    /// when it is set to an empty string. Codes are lower case, like the entity JSON's animation codes.
    /// </summary>
    public static string ReadAnimation(JsonObject taskConfig, string key, string defaultCode)
    {
        JsonObject value = taskConfig[key];
        if (!value.Exists)
        {
            return defaultCode;
        }
        string code = value.AsString("");
        return code.ToLowerInvariant();
    }

    /// <summary>The block cell the spider's feet are in.</summary>
    public static BlockPos FeetCell(EntityAgent entity)
    {
        return new BlockPos(JumpPlanner.CellOfX(entity.Pos.X), JumpPlanner.CellOfFeetY(entity.Pos.Y),
            JumpPlanner.CellOfX(entity.Pos.Z), entity.Pos.Dimension);
    }

    /// <summary>
    /// Writes one line to the server's debug log (Logs/server-debug.log), prefixed with the mod and the spider, so
    /// the author can follow what a spider decided and why.
    /// </summary>
    public static void Log(EntityAgent entity, string taskName, string message)
    {
        entity.World.Logger.Debug("[jumpingspiders] {0} {1}: {2}", taskName, entity.EntityId, message);
    }

    /// <summary>A position as short text for the debug log.</summary>
    public static string Format(double x, double y, double z)
    {
        return string.Format("({0:0.00}, {1:0.00}, {2:0.00})", x, y, z);
    }
}
