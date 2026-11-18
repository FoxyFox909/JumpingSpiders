namespace JumpingSpiders;

/// <summary>
/// The maths for reshaping one model element's box along its own axes (the "[x, y, z]" entries of ElementScales).
/// Pure logic, covered by unit tests; EntityBehaviorJumpingSpider.OnTesselation applies it to a copy of the shape.
///
/// How the game places an element (the mesh builder and the animators agree on this for the rest pose):
///   point in the model = RotationOrigin + Rotation * Scale * (From + p - RotationOrigin)
/// where p runs over the box, 0 to (To - From), and everything is in model units (16 to a block). From, To and
/// RotationOrigin are in the parent's coordinates, measured from the parent's own From corner (for a root element,
/// from the model's origin). So a child's joint (its RotationOrigin) sits at parent.From + child.RotationOrigin in
/// the parent's coordinates.
///
/// Reshaping moves From and To toward the element's RotationOrigin by a factor per axis. That gives the element
/// exactly the box a ScaleX/Y/Z of the same factors would give it (same corners, same joint), but leaves the
/// element's matrix without any scale, so the element's children stay unskewed however an animation turns them.
/// Each child is moved so its joint lands where the same per-axis factors would take it: still attached, at the
/// same place on the reshaped box. The child's own size is not changed.
/// </summary>
public static class BoxReshape
{
    /// <summary>
    /// A corner of the box (From or To) after reshaping: moved toward rotationOrigin by factors, per axis.
    /// rotationOrigin may be null (the game then turns the element about 0, 0, 0). Returns a new array.
    /// </summary>
    public static double[] ReshapeCorner(double[] corner, double[] rotationOrigin, double[] factors)
    {
        double[] result = new double[3];
        for (int axis = 0; axis < 3; axis++)
        {
            double origin = OriginOnAxis(rotationOrigin, axis);
            result[axis] = origin + factors[axis] * (corner[axis] - origin);
        }
        return result;
    }

    /// <summary>
    /// How far a direct child of the reshaped element must move, in the parent's coordinates, so that its joint keeps
    /// its place on the reshaped box. The child's joint is at parent.From + childRotationOrigin; after reshaping it
    /// belongs at newParentFrom + factors * childRotationOrigin, and newParentFrom is where From moved, so the child
    /// moves by (factors - 1) * childRotationOrigin. A child with no rotation origin turns about the parent's From
    /// corner, which moved with the box, so it needs no move. Returns a new array.
    /// </summary>
    public static double[] ChildShift(double[] childRotationOrigin, double[] factors)
    {
        double[] result = new double[3];
        for (int axis = 0; axis < 3; axis++)
        {
            result[axis] = (factors[axis] - 1.0) * OriginOnAxis(childRotationOrigin, axis);
        }
        return result;
    }

    /// <summary>point + shift, as a new array (for moving a child's From, To and RotationOrigin).</summary>
    public static double[] Add(double[] point, double[] shift)
    {
        double[] result = new double[3];
        for (int axis = 0; axis < 3; axis++)
        {
            result[axis] = point[axis] + shift[axis];
        }
        return result;
    }

    private static double OriginOnAxis(double[] rotationOrigin, int axis)
    {
        if (rotationOrigin == null)
        {
            return 0.0;
        }
        return rotationOrigin[axis];
    }
}
