namespace JumpingSpiders;

/// <summary>
/// What a hammock build attempt (AiTaskSpiderBuildHammock) is doing. The attempt walks through these in order, one
/// small piece of work per server tick, and comes back to FindingStances after every jump or walk.
/// </summary>
public enum BuildPhase
{
    /// <summary>No attempt running.</summary>
    None,

    /// <summary>Trying random cells near the spider until one can hold a hammock.</summary>
    Picking,

    /// <summary>Start of a round: build at once if within reach, else list the spots within reach where it could stand.</summary>
    FindingStances,

    /// <summary>One jump-planner search to the nearest of those spots.</summary>
    Planning,

    /// <summary>Listing the spots within reach where it could hold on to a wall or ceiling.</summary>
    FindingClings,

    /// <summary>Testing jumps from here to those spots, a few per tick.</summary>
    CheckingClingJumps,

    /// <summary>Choosing where to walk: a spot to stand on, or a spot to jump to a clinging spot from.</summary>
    ChoosingWalk,

    /// <summary>Testing jumps to a clinging spot from the launch spots it could walk to, a few per tick.</summary>
    CheckingLaunchSpots,

    /// <summary>Jumping to a spot where it can stand.</summary>
    Jumping,

    /// <summary>Jumping at a wall or ceiling, ready to hold on as soon as it touches one within reach.</summary>
    ClingJumping,

    /// <summary>Walking (the game's pathfinder) to a spot.</summary>
    Walking,

    /// <summary>
    /// The last few tenths of a block onto the exact spot: a sidle along the floor, or a crawl along the wall or ceiling
    /// it holds on to (physics paused).
    /// </summary>
    Sidling,

    /// <summary>Standing within reach, building (for buildTimeMs, from the task's JSON).</summary>
    Building,

    /// <summary>Holding on to a wall or ceiling with its physics paused, building.</summary>
    Clinging,

    /// <summary>The attempt is over (Outcome says how); the task ends this tick.</summary>
    Done
}
