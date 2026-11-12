using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace JumpingSpiders;

/// <summary>
/// The block entity of a silk hammock: remembers which spider sleeps in it (or is on its way to), and makes the
/// hammock findable by HammockFinder.
///
/// Vintage Story concepts used here:
/// - A block entity is an object the game keeps for one placed block, for data a Block cannot hold (a Block object
///   is shared by every placed block of its variant). The JSON's "entityClass" makes the game create one whenever a
///   hammock is placed or its chunk is loaded. Initialize runs in both cases, on the server and on the client.
/// - The POI registry ("points of interest", a vanilla server-side system) is an index of block entities by
///   chunk column, so creatures can find nearby things without scanning blocks. Hen boxes and troughs use it.
///   Each hammock adds itself when it is created or loaded and removes itself when broken or unloaded, so after a
///   server restart the hammocks are found again as soon as their chunks load.
///
/// Occupancy is kept in memory only and only on the server: Occupant is the Entity object of the spider. It is not
/// saved, because an entity gets a new object when its chunk reloads and a new entity id when it is caught in a
/// trap and released. The sleep task claims the hammock again when the spider resumes sleeping. A claim held by a
/// spider that died, unloaded, was caught, or wandered off (see Config.HammockClaimMaxDistance) is dropped the next
/// time anyone asks, so the hammock never stays blocked by a spider that is gone.
/// </summary>
public class BlockEntitySilkHammock : BlockEntity, IPointOfInterest
{
    /// <summary>The POI type string; HammockFinder looks for this.</summary>
    public const string PoiType = "jumpingspiders:hammock";

    /// <summary>
    /// The spider sleeping in this hammock (or heading there to sleep), or null. Server only, in memory only.
    /// Read it through GetLiveOccupant(), which drops a claim by a spider that is gone.
    /// </summary>
    public Entity Occupant;

    // The POI registry files us by this position, so it must not change while registered; we compute it once.
    private Vec3d poiPosition;
    private bool registeredAsPoi;

    /// <summary>Where the POI registry files this hammock: the centre of its block cell.</summary>
    public Vec3d Position
    {
        get { return poiPosition; }
    }

    /// <summary>The POI type, so other mods' and vanilla searches skip hammocks.</summary>
    public string Type
    {
        get { return PoiType; }
    }

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        poiPosition = Pos.ToVec3d().Add(0.5, 0.5, 0.5);
        if (api.Side == EnumAppSide.Server)
        {
            POIRegistry registry = api.ModLoader.GetModSystem<POIRegistry>();
            if (registry != null)
            {
                registry.AddPOI(this);
                registeredAsPoi = true;
            }
        }
    }

    /// <summary>The live occupant, or null. A claim by a spider that is dead, unloaded or too far away is dropped first.</summary>
    public Entity GetLiveOccupant()
    {
        if (Occupant != null && !IsClaimStillValid(Occupant))
        {
            Occupant = null;
        }
        return Occupant;
    }

    /// <summary>True when the hammock is free, or already claimed by this same spider.</summary>
    public bool IsFreeFor(Entity spider)
    {
        Entity occupant = GetLiveOccupant();
        return occupant == null || occupant == spider;
    }

    /// <summary>Claims the hammock for this spider. False when another spider has it.</summary>
    public bool TryOccupy(Entity spider)
    {
        if (spider == null || !spider.Alive)
        {
            return false;
        }
        if (!IsFreeFor(spider))
        {
            return false;
        }
        Occupant = spider;
        return true;
    }

    /// <summary>Frees the hammock if this spider holds it. Call when the spider wakes up or gives up on it.</summary>
    public void Release(Entity spider)
    {
        if (Occupant == spider)
        {
            Occupant = null;
        }
    }

    /// <summary>Feet position for a spider resting in the hammock: the cell's centre, standing on the hammock's floor.</summary>
    public Vec3d GetSleepPosition()
    {
        double floorTop = 0;
        BlockSilkHammock hammock = Block as BlockSilkHammock;
        if (hammock == null && Api != null)
        {
            hammock = Api.World.BlockAccessor.GetBlock(Pos) as BlockSilkHammock;
        }
        if (hammock != null)
        {
            floorTop = hammock.GetFloorTop();
        }
        // Pos.Y, not InternalY: entity positions keep the dimension apart (EntityPos.Dimension), like BlockPos does.
        return new Vec3d(Pos.X + 0.5, Pos.Y + floorTop, Pos.Z + 0.5);
    }

    /// <summary>
    /// Called by the game when the hammock is broken: by a player (byPlayer set), by losing its support, or by an
    /// explosion (byPlayer null). Runs before the block is removed, while Occupant is still known. The game also
    /// calls it on the client of the player who broke it; there Occupant is always null.
    /// </summary>
    public override void OnBlockBroken(IPlayer byPlayer = null)
    {
        base.OnBlockBroken(byPlayer);
        if (Api == null || Api.Side != EnumAppSide.Server || byPlayer == null)
        {
            return;
        }
        Entity occupant = GetLiveOccupant();
        if (occupant == null || !SpiderAttributes.IsSleeping(occupant))
        {
            return;
        }
        EntityBehaviorJumpingSpider behavior = occupant.GetBehavior<EntityBehaviorJumpingSpider>();
        if (behavior != null)
        {
            behavior.OnHammockBrokenWhileAsleep(byPlayer);
        }
    }

    /// <summary>
    /// Called by the game whenever the block goes away, however that happens (always after OnBlockBroken when it was
    /// broken). Wakes the spider that slept here and takes the hammock out of the POI registry.
    /// </summary>
    public override void OnBlockRemoved()
    {
        base.OnBlockRemoved();
        if (Api != null && Api.Side == EnumAppSide.Server)
        {
            Entity occupant = GetLiveOccupant();
            if (occupant != null)
            {
                SpiderAttributes.SetSleeping(occupant, false);
            }
        }
        Occupant = null;
        UnregisterPoi();
    }

    /// <summary>Called by the game when the chunk is unloaded (also when the server stops).</summary>
    public override void OnBlockUnloaded()
    {
        base.OnBlockUnloaded();
        Occupant = null;
        UnregisterPoi();
    }

    private void UnregisterPoi()
    {
        if (!registeredAsPoi || Api == null)
        {
            return;
        }
        POIRegistry registry = Api.ModLoader.GetModSystem<POIRegistry>();
        if (registry != null)
        {
            registry.RemovePOI(this);
        }
        registeredAsPoi = false;
    }

    // A claim is valid while the spider is alive, still loaded in the world as this same object, in the same
    // dimension and not too far away. The game marks an entity Despawned when it dies and is removed, when its chunk
    // unloads, and when it is caught in a trap; a released or reloaded spider is a new Entity object.
    private bool IsClaimStillValid(Entity spider)
    {
        if (!spider.Alive || spider.State == EnumEntityState.Despawned)
        {
            return false;
        }
        if (spider.World == null || spider.World.GetEntityById(spider.EntityId) != spider)
        {
            return false;
        }
        if (spider.Pos.Dimension != Pos.dimension)
        {
            return false;
        }
        double maxDistance = JumpingSpidersModSystem.Config.HammockClaimMaxDistance;
        double dx = spider.Pos.X - (Pos.X + 0.5);
        double dy = spider.Pos.Y - (Pos.Y + 0.5);
        double dz = spider.Pos.Z - (Pos.Z + 0.5);
        return dx * dx + dy * dy + dz * dz <= maxDistance * maxDistance;
    }
}
