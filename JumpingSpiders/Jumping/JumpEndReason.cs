namespace JumpingSpiders;

/// <summary>Why JumpController's last flight ended (JumpController.EndReason).</summary>
public enum JumpEndReason
{
    /// <summary>No flight has ended yet, or one is still going on.</summary>
    None,

    /// <summary>The spider touched down. Compare its position with the step's target to see if it got there.</summary>
    Landed,

    /// <summary>It ran into the side of a block; it now drops down.</summary>
    HitWall,

    /// <summary>It hit a block above it; it now drops down.</summary>
    HitCeiling,

    /// <summary>It came down in water (or lava).</summary>
    Water,

    /// <summary>It died in the air.</summary>
    Died,

    /// <summary>It never left the ground (something held it down, or a block was right above it).</summary>
    NoLiftoff,

    /// <summary>The flight took longer than Config.MaxFlightSeconds; the spider falls normally from here.</summary>
    TimedOut,

    /// <summary>Cancel() was called, or the entity is being removed from the world.</summary>
    Cancelled
}
