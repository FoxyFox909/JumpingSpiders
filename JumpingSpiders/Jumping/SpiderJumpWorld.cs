using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace JumpingSpiders;

/// <summary>
/// The real game world as JumpPlanner sees it (IJumpWorld), for one spider's collision box.
/// Create one when a task wants to plan, pass it to JumpPlanner, and drop it; it is cheap.
///
/// Vintage Story concepts used here:
/// - An entity's CollisionBox is a box relative to its feet position (its Pos), e.g. -0.3..0.3 wide and 0..0.5 high.
/// - Blocks have collision boxes too (GetCollisionBoxes): a full block is 0..1, a slab 0..0.5, a fence up to 1.5,
///   and air or plants have none. The CollisionTester class checks an entity box against all of them.
/// - Blocks can hold liquid in a separate "fluid layer", so water is checked with BlockLayersAccess.Fluid.
/// - Dimensions: the game can stack extra mini-worlds above the normal one by offsetting Y. Positions here use
///   the entity's ordinary Y; the dimension offset is added only where the game's collision code wants it.
///
/// Main thread only (it reuses scratch objects). It reads blocks through the world's normal block accessor.
/// </summary>
public class SpiderJumpWorld : IJumpWorld
{
    /// <summary>How far below the feet (blocks) we probe to decide that there is something to stand on.</summary>
    public const double FootingProbeDepth = 0.05;

    private readonly IBlockAccessor blockAccessor;
    private readonly Cuboidf collisionBox;
    private readonly int dimension;
    private readonly double dimensionYOffset;

    // A private CollisionTester, because the world's shared one keeps scratch state between calls.
    private readonly CollisionTester collisionTester = new CollisionTester();
    private readonly Vec3d testPos = new Vec3d();
    private readonly BlockPos cellPos;

    public SpiderJumpWorld(EntityAgent spider)
        : this(spider.World.BlockAccessor, spider.CollisionBox, spider.Pos.Dimension)
    {
    }

    public SpiderJumpWorld(IBlockAccessor blockAccessor, Cuboidf collisionBox, int dimension)
    {
        this.blockAccessor = blockAccessor;
        this.collisionBox = collisionBox;
        this.dimension = dimension;
        dimensionYOffset = (double)dimension * BlockPos.DimensionBoundary;
        cellPos = new BlockPos(dimension);
    }

    public bool IsFree(double x, double y, double z)
    {
        testPos.Set(x, y + dimensionYOffset, z);
        // false = touching a block is fine, only overlapping counts (the spider touches the floor it stands on).
        return !collisionTester.IsColliding(blockAccessor, collisionBox, testPos, false);
    }

    public double GetStandHeight(int x, int y, int z)
    {
        double height = y;

        // A partial block in the cell itself (slab, hammock) raises the feet to its top.
        double topInCell = CollisionTop(x, y, z);
        if (topInCell > 0)
        {
            height = y + topInCell;
        }

        // A block below that is taller than one block (a fence) reaches into this cell.
        double topBelow = CollisionTop(x, y - 1, z) - 1;
        if (topBelow > 0 && y + topBelow > height)
        {
            height = y + topBelow;
        }
        return height;
    }

    public bool CanStandAt(int x, int y, int z)
    {
        cellPos.Set(x, y, z);

        // Unloaded chunks read as air; never plan into them.
        if (blockAccessor.GetChunkAtBlockPos(cellPos) == null)
        {
            return false;
        }

        // No landing in water or lava.
        Block fluid = blockAccessor.GetBlock(cellPos, BlockLayersAccess.Fluid);
        if (fluid != null && fluid.IsLiquid())
        {
            return false;
        }

        double standY = GetStandHeight(x, y, z);
        if (standY >= y + 1)
        {
            // The cell is full; standing on it means being in the cell above.
            return false;
        }
        double feetX = x + 0.5;
        double feetZ = z + 0.5;
        if (!IsFree(feetX, standY, feetZ))
        {
            return false;
        }
        // Footing: lowered a little, the box must hit something.
        return !IsFree(feetX, standY - FootingProbeDepth, feetZ);
    }

    // The highest collision box top of the block in this cell, relative to the cell (0 when it has none).
    private double CollisionTop(int x, int y, int z)
    {
        cellPos.Set(x, y, z);
        Block block = blockAccessor.GetMostSolidBlock(cellPos);
        Cuboidf[] boxes = block.GetCollisionBoxes(blockAccessor, cellPos);
        if (boxes == null)
        {
            return 0;
        }
        double top = 0;
        for (int i = 0; i < boxes.Length; i++)
        {
            if (boxes[i] != null && boxes[i].Y2 > top)
            {
                top = boxes[i].Y2;
            }
        }
        return top;
    }
}
