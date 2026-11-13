using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace JumpingSpiders;

/// <summary>
/// Registers the spider's AI task classes under the codes the entity JSON uses in its "aitasks" list.
/// Called from JumpingSpidersModSystem.StartServerSide: creature AI only runs on the server.
///
/// The game's task registry (AiTaskRegistry) maps a JSON "code" to a C# class; the taskai behavior creates one
/// task object per entry for every spider, with the (EntityAgent, JsonObject, JsonObject) constructor. The codes
/// carry our mod id because the registry is shared by all mods.
///
/// What the tasks share (all in this folder):
/// - JumpRunner: does a planned chain of jumps (wind-up, flight, pause on the perch), used by all four tasks.
/// - WindUp: the crouch with the front legs raised before a jump (the attack uses it directly).
/// - SpiderTaskUtil: turning, stopping the walking AI, emotion-state sources, animations, debug log.
/// - The pure planning helpers in Logic: JumpPlanner, LandingSpotFilter, FleeDirection, PounceMath, LaunchSpotFinder.
/// </summary>
public static class TaskRegistration
{
    public const string JumpAttackCode = "jumpingspiders.jumpattack";
    public const string JumpFleeCode = "jumpingspiders.jumpflee";
    public const string ExploreCode = "jumpingspiders.explore";
    public const string SleepCode = "jumpingspiders.sleep";

    public static void Register(ICoreServerAPI api)
    {
        AiTaskRegistry.Register<AiTaskSpiderJumpAttack>(JumpAttackCode);
        AiTaskRegistry.Register<AiTaskSpiderJumpFlee>(JumpFleeCode);
        AiTaskRegistry.Register<AiTaskSpiderExplore>(ExploreCode);
        AiTaskRegistry.Register<AiTaskSpiderSleep>(SleepCode);
    }
}
