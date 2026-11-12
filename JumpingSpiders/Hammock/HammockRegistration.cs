using Vintagestory.API.Common;

namespace JumpingSpiders;

/// <summary>
/// Registers the hammock's C# classes under the names the block JSON uses ("class" and "entityClass" in
/// assets/jumpingspiders/blocktypes/silkhammock.json). Called from JumpingSpidersModSystem.Start on both the server
/// and the client, because both sides build the same block objects from the JSON. The game's class registry is
/// global and silently overwrites duplicate names, so the names carry our mod id.
/// </summary>
public static class HammockRegistration
{
    /// <summary>Name of BlockSilkHammock in the block JSON's "class".</summary>
    public const string BlockClassName = "jumpingspiders.BlockSilkHammock";

    /// <summary>Name of BlockEntitySilkHammock in the block JSON's "entityClass".</summary>
    public const string BlockEntityClassName = "jumpingspiders.SilkHammock";

    public static void Register(ICoreAPI api)
    {
        api.RegisterBlockClass(BlockClassName, typeof(BlockSilkHammock));
        api.RegisterBlockEntityClass(BlockEntityClassName, typeof(BlockEntitySilkHammock));
    }
}
